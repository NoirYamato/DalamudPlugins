using MitCue.Core.Plans;

namespace MitCue.Cli.Tests;

/// <summary><c>plan</c>, <c>check</c>, <c>share</c> and <c>unshare</c>.</summary>
public class PlanCommandTests : IDisposable
{
    private readonly CliKit kit = new();
    private readonly string whm;

    public PlanCommandTests()
    {
        this.whm = this.kit.Write("whm.yaml", SamplePlans.Whm);
    }

    public void Dispose() => this.kit.Dispose();

    /// <summary>Table rows of the checklist (lines ending with an entry id).</summary>
    private static List<string> Rows(CliResult r) =>
        r.OutLines.Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"\s(e|s)\d+$")).ToList();

    [Fact]
    public void Plan_ListsThePlayersItemsInTimeOrder()
    {
        var r = this.kit.Run("plan", this.whm, "--job", "WHM", "--slot", "H1");
        Assert.True(r.ExitCode == 0, r.ToString());
        Assert.Equal("\"Test WHM\" (top, level 90) as WHM, slot H1: 9 of 10 entries, 10 items", r.OutLines[0]);

        var rows = Rows(r);
        Assert.Equal(["e1", "e2", "e3", "e4", "e9", "e5", "e6", "e7", "e7", "e8"], rows.Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1]));
        Assert.Matches(@"^\s+P1\s+-0:05\s+-5\.0\s+Medica II\s+Pull \(combat start\)\s+e1$", rows[0]);
        Assert.Matches(@"^\s+P1\s+1:18\s+-3\.2\s+Temperance\s+Pantokrator.*★★★\s+e3$", rows[2]);

        // Bell has no time: the heal default (+1 s after the hit), marked with *.
        Assert.Matches(@"^\s+P1\s+1:22\s+\+1\.0\*\s+Liturgy of the Bell\s+Pantokrator.*right after the 1st stack\s+e4$", rows[3]);
        Assert.Matches(@"^\s+P2\s+0:07\s+-7\.5\s+Divine Benison / Tetragrammaton\s+Solar Ray \(both tanks\)\s+on MT\s+e5$", rows[5]);
        Assert.Contains("note: Medica II + Rapture straight after", rows[8]);
        Assert.Contains("optional", rows[7]);
        Assert.Contains("* no time in the plan", r.Out);
    }

    [Fact]
    public void Plan_SelectsByJobAndColumn()
    {
        var sch = this.kit.Run("plan", this.whm, "--job", "Scholar");
        Assert.Equal(0, sch.ExitCode);
        Assert.Equal(["s1"], Rows(sch).Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1]));
        Assert.Contains("Sacred Soil", sch.Out);

        var column = this.kit.Run("plan", this.whm, "--job", "WHM", "--assignee", "white mage");
        Assert.Contains("column \"White Mage\"", column.OutLines[0]);
        Assert.Equal(10, Rows(column).Count);

        var unknown = this.kit.Run("plan", this.whm, "--job", "WHM", "--assignee", "Paladin");
        Assert.Equal(2, unknown.ExitCode);
        Assert.Contains("no column 'Paladin'", unknown.Err);
        Assert.Contains("\"White Mage\", \"Scholar\"", unknown.Err);

        var none = this.kit.Run("plan", this.whm, "--job", "AST");
        Assert.Equal(0, none.ExitCode);
        Assert.Contains("no entry of the plan applies to AST", none.Err);

        Assert.Contains("unknown slot 'X9'", this.kit.Run("plan", this.whm, "--job", "WHM", "--slot", "X9").Err);
    }

    [Fact]
    public void Plan_ReadsStandardInputAndShareCodes()
    {
        this.kit.Input = SamplePlans.Whm;
        var stdin = this.kit.Run("plan", "-", "--job", "WHM");
        Assert.Equal(0, stdin.ExitCode);

        var code = this.kit.Write("code.txt", PlanSerializer.ToShareCode(PlanSerializer.FromYaml(SamplePlans.Whm)));
        var shared = this.kit.Run("plan", code, "--job", "WHM");
        Assert.Equal(stdin.Out, shared.Out);
    }

    [Fact]
    public void Plan_Details_ShowSourceAndRowNotes()
    {
        var plan = PlanSerializer.FromYaml(SamplePlans.Whm);
        plan.Entries[0].Src = "'TOP – White Mage'!C17";
        plan.Entries[0].Detail = "Line one\nLine two";
        var path = this.kit.Write("details.yaml", PlanSerializer.ToYaml(plan));
        var r = this.kit.Run("plan", path, "--job", "WHM", "--details");
        Assert.Contains("'TOP – White Mage'!C17", r.Out);
        Assert.Contains("Line one / Line two", r.Out);
    }

    [Fact]
    public void Check_ReportsFindingsAndFailsOnErrors()
    {
        var path = this.kit.Write("broken.yaml", SamplePlans.Broken);
        var r = this.kit.Run("check", path, "--job", "WHM");
        Assert.True(r.ExitCode == 1, r.ToString());
        Assert.Equal("Checking \"Broken WHM\" (top, level 90) as WHM: 4 entries", r.OutLines[0]);
        Assert.Contains("unknown mechanic 'p2.cosmo_memry' in fight 'top' (did you mean p2.cosmo_memory?)", r.Out);
        Assert.Contains("Kerachole is a SGE action, not WHM.", r.Out);
        Assert.Matches(@"error\s+P2 0:48\s+recast-conflict\s+Temperance planned at P2 0:10 and P2 0:48 \(38\.2 s apart\), recast 120 s\.\s+b2", r.Out);
        Assert.Contains("Divine Caress at P3 0:41 is learned at level 100; TOP is synced to level 90.", r.Out);
        Assert.EndsWith("4 errors, 0 warnings, 0 info.\n", r.Out);
    }

    [Fact]
    public void Check_CleanPlan()
    {
        var r = this.kit.Run("check", this.whm, "--job", "WHM", "--assignee", "White Mage");
        Assert.True(r.ExitCode == 0, r.ToString());
        Assert.EndsWith("No problems found.\n", r.Out);
    }

    [Fact]
    public void Check_MarksHeuristicFindings()
    {
        var plan = PlanSerializer.FromYaml(SamplePlans.Whm);
        var rapture = new PlanItem { Action = 16534, Name = "Afflatus Rapture" };
        plan.Entries.Add(new PlanEntry { Id = "r1", Mechanic = "p3.hello_world", Offset = 2, Who = new WhoSpec { Job = MitCue.Core.Job.WHM }, Do = [rapture, rapture, rapture, rapture] });
        var r = this.kit.Run("check", this.kit.Write("lilies.yaml", PlanSerializer.ToYaml(plan)), "--job", "WHM");
        Assert.Equal(0, r.ExitCode);
        Assert.Matches(@"warning \(h\)\s+P3 0:43\s+resource-shortage\s+Afflatus Rapture at P3 0:43 needs 1 Lily", r.Out);
        Assert.Contains("(h) = heuristic", r.Out);
    }

    [Fact]
    public void ShareAndUnshare_RoundTrip()
    {
        var share = this.kit.Run("share", this.whm);
        Assert.Equal(0, share.ExitCode);
        var code = share.Out.Trim();
        Assert.StartsWith(PlanSerializer.ShareCodePrefix, code);
        Assert.DoesNotContain('\n', code);
        Assert.Contains("\"Test WHM\": 10 entries", share.Err);
        Assert.Equal(share.Out, this.kit.Run("share", this.whm).Out);

        var expected = PlanSerializer.ToYaml(PlanSerializer.FromYaml(SamplePlans.Whm));
        var unshare = this.kit.Run("unshare", code);
        Assert.Equal(0, unshare.ExitCode);
        Assert.Equal(expected, unshare.Out);

        // From a file (wrapped in backticks and broken over lines as chat does), to a file, and from standard input.
        var file = this.kit.Write("code.txt", "`" + code[..40] + "\n" + code[40..] + "`\n");
        var toFile = this.kit.Run("unshare", file, "-o", "out/plan.yaml");
        Assert.Equal(0, toFile.ExitCode);
        Assert.Equal(expected, File.ReadAllText(Path.Combine(this.kit.WorkDir, "out", "plan.yaml")));
        Assert.Contains("Wrote \"Test WHM\" (10 entries)", toFile.Err);

        this.kit.Input = code;
        Assert.Equal(expected, this.kit.Run("unshare", "-").Out);
    }

    [Fact]
    public void Unshare_ReportsDamagedCodes()
    {
        var r = this.kit.Run("unshare", "MITCUE1:abc");
        Assert.Equal(3, r.ExitCode);
        Assert.Contains("damaged", r.Err);

        var other = this.kit.Run("unshare", "hello");
        Assert.Equal(3, other.ExitCode);
        Assert.Contains("Not a MitCue share code", other.Err);
    }
}
