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
        Assert.Equal("\"Test WHM\" (top, level 90) as WHM, slot H1, column \"White Mage\": 9 of 10 entries, 10 items", r.OutLines[0]);

        var rows = Rows(r);
        Assert.Equal(["e1", "e2", "e3", "e4", "e9", "e5", "e6", "e7", "e7", "e8"], rows.Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1]));
        Assert.Matches(@"^\s+P1\s+-0:05\s+-5\.0\s+Medica II\s+Pull \(combat start\)\s+e1$", rows[0]);
        Assert.Matches(@"^\s+P1\s+1:18\s+-3\.2\s+Temperance\s+Pantokrator.*★★★\s+e3$", rows[2]);

        Assert.Matches(@"^\s+P1\s+1:22\s+\+1\.0\s+Liturgy of the Bell\s+Pantokrator.*right after the 1st stack\s+e4$", rows[3]);

        // Benediction has no time: the heal default (+1 s after the hit), marked with *.
        Assert.Matches(@"^\s+P1\s+1:49\s+\+1\.0\*\s+Benediction\s+Wave Cannon Kyrios.*\s+e9$", rows[4]);
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

    [Theory]
    [InlineData(new[] { "--job", "SCH" }, "as SCH, slot H2, column \"H2 SCH\": 31 of 475 entries", "column \"H2 SCH\"")]
    [InlineData(new[] { "--job", "SCH", "--slot", "H2" }, "as SCH, slot H2, column \"H2 SCH\": 31 of 475 entries", null)]
    [InlineData(new[] { "--job", "WHM" }, "as WHM, slot H1, column \"H1 WHM\": 11 of 475 entries", "column \"H1 WHM\"")]
    [InlineData(new[] { "--job", "WAR", "--slot", "MT" }, "as WAR, slot MT, column \"WAR/DRK WAR\": 35 of 475 entries", "column \"WAR/DRK WAR\"")]
    [InlineData(new[] { "--job", "WAR" }, "as WAR, column \"WAR/DRK WAR\": 9 of 475 entries", "column \"WAR/DRK WAR\"")]
    [InlineData(new[] { "--job", "WAR", "--slot", "MT", "--assignee", "*" }, "as WAR, slot MT: 65 of 475 entries", null)]
    public void Plan_WithoutAnAssigneeSelectsWhatThePluginShows(string[] options, string header, string? note)
    {
        // Review finding (41d2893): without --slot the CLI left out the job-less "H2" calls the plugin shows a Scholar
        // (29 entries against 31), and without --assignee a tank got every tank pair's column (65 entries for WAR MT).
        // Review round 3: a column has the job-less calls of its slot (WAR MT: 24 + 8 Reprisals and 3 Ramparts).
        var r = this.kit.Run(["plan", Path.Combine(CliKit.RepoRoot, "plans", "top-lpdu.yaml"), .. options]);

        Assert.True(r.ExitCode == 0, r.ToString());
        Assert.Contains(header, r.OutLines[0], StringComparison.Ordinal);
        if (note != null)
            Assert.Contains($"note: {note}", r.Err, StringComparison.Ordinal);
        else if (!options.Contains("*"))
            Assert.DoesNotContain("as the plugin chooses", r.Err, StringComparison.Ordinal);
        if (options.Contains("*"))
            Assert.Contains("note: entries of 6 columns apply", r.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_ReadsSlotSynonymsAsThePluginDoes()
    {
        // Review round 3: the plugin's store wrote "D3" as R1 and "T1" as MT, the CLI compared the names as written and
        // selected nothing (and `check` said "No problems found" for 0 entries).
        var plan = this.kit.Write("synonyms.yaml", """
            format: mitcue-plan/1
            name: Synonym slots
            fight: top
            entries:
              - id: e001
                mechanic: p1.pantokrator
                who: {slot: D3}
                do:
                  - {action: 7560, name: Addle}
              - id: e002
                mechanic: p2.solar_ray
                who: {slot: T1}
                do:
                  - {action: 7535, name: Reprisal}
            """);

        var bard = this.kit.Run("plan", plan, "--job", "BLM", "--slot", "D3");
        Assert.True(bard.ExitCode == 0, bard.ToString());
        Assert.Contains("as BLM, slot R1: 1 of 2 entries", bard.OutLines[0], StringComparison.Ordinal);
        Assert.Equal(["e001"], Rows(bard).Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1]));

        var tank = this.kit.Run("check", plan, "--job", "WAR", "--slot", "MT");
        Assert.True(tank.ExitCode == 0, tank.ToString());
        Assert.Contains("as WAR, slot MT: 1 entry", tank.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_DoesNotUseASlotOfAnotherRole()
    {
        // Review round 4: "--job WAR --slot M1" selected the M1 column's Feint ("any column for WAR in slot M1"); the plugin
        // does not use a usual party slot of another role either.
        var plan = this.kit.Write("roles.yaml", """
            format: mitcue-plan/1
            name: Roles
            fight: top
            assignees:
              - {key: MT, slot: MT}
              - {key: M1, slot: M1}
              - {key: WAR, job: WAR}
            entries:
              - id: e001
                mechanic: p1.pantokrator
                who: {slot: MT, assignee: MT}
                do:
                  - {action: 7535, name: Reprisal}
              - id: e002
                mechanic: p1.pantokrator
                who: {slot: M1, assignee: M1}
                do:
                  - {action: 7549, name: Feint}
              - id: e003
                mechanic: p2.solar_ray
                who: {job: WAR, assignee: WAR}
                do:
                  - {action: 40, name: Thrill of Battle}
            """);

        var melee = this.kit.Run("plan", plan, "--job", "WAR", "--slot", "M1");
        Assert.True(melee.ExitCode == 0, melee.ToString());
        Assert.Contains("note: M1 is not a slot of WAR's role, so it is not used", melee.Err, StringComparison.Ordinal);
        Assert.Contains("as WAR, column \"WAR\": 1 of 3 entries", melee.OutLines[0], StringComparison.Ordinal);
        Assert.Equal(["e003"], Rows(melee).Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1]));

        var tank = this.kit.Run("plan", plan, "--job", "WAR", "--slot", "MT");
        Assert.True(tank.ExitCode == 0, tank.ToString());
        Assert.DoesNotContain("not a slot", tank.Err, StringComparison.Ordinal);
        Assert.Equal(["e001", "e003"], Rows(tank).Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1]));
    }

    [Theory]
    [InlineData("H2")]
    [InlineData(null)]
    public void Plan_TakesEveryColumnOfASheetWithOneColumnPerPhase(string? slot)
    {
        // Review round 3: a P1/P2 tab headed "SCH" and a P3 tab headed "H2 SCH" are one Scholar's calls; the CLI (like
        // the plugin) took the other column for an alternative and selected one of them.
        var plan = this.kit.Write("perphase.yaml", """
            format: mitcue-plan/1
            name: Per phase
            fight: top
            assignees:
              - {key: SCH, job: SCH}
              - {key: H2 SCH, slot: H2, job: SCH}
            entries:
              - id: e001
                mechanic: p1.pantokrator
                who: {job: SCH, assignee: SCH}
                do:
                  - {action: 188, name: Sacred Soil}
              - id: e002
                mechanic: p2.solar_ray
                who: {job: SCH, assignee: SCH}
                do:
                  - {action: 188, name: Sacred Soil}
              - id: e003
                mechanic: p3.hello_world
                who: {slot: H2, job: SCH, assignee: H2 SCH}
                do:
                  - {action: 188, name: Sacred Soil}
            """);

        string[] options = slot == null ? ["plan", plan, "--job", "SCH"] : ["plan", plan, "--job", "SCH", "--slot", slot];
        var r = this.kit.Run(options);

        Assert.True(r.ExitCode == 0, r.ToString());
        Assert.Contains("as SCH, slot H2: 3 of 3 entries", r.OutLines[0], StringComparison.Ordinal);
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
    public void Check_LevelVariants_AreWarnedUnlessTheChoiceNamesTheReportedAction()
    {
        var path = this.kit.Write("variants.yaml", """
            format: mitcue-plan/1
            name: Variants
            fight: top
            entries:
              - { id: v1, mechanic: p1.loop_set1, who: { job: SCH }, do: [ { any: [ { action: 37013, name: Concitation }, { action: 186, name: Succor } ] } ] }
              - { id: v2, mechanic: p1.loop_set3, who: { job: WAR }, do: [ { any: [ { action: 25751, name: Bloodwhetting }, { action: 3551, name: Raw Intuition } ] } ] }
              - { id: v3, mechanic: p1.loop_set1, who: { job: WAR }, do: [ { action: 3551, name: Raw Intuition } ] }
            """);

        // Review regression: the choices name the action the game reports at 90 (Succor, Bloodwhetting), so their uses
        // are tracked; only the plain Raw Intuition (reported as Bloodwhetting) is not.
        Assert.EndsWith("No problems found.\n", this.kit.Run("check", path, "--job", "SCH").Out);
        var war = this.kit.Run("check", path, "--job", "WAR");
        Assert.Contains("Raw Intuition at P1 0:27 is replaced by Bloodwhetting (25751) at level 90", war.Out);
        Assert.EndsWith("0 errors, 1 warning, 0 info.\n", war.Out);
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
