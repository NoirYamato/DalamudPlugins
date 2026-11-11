using MitCue.Core.Plans;
using MitCue.Core.Tests.Data;

namespace MitCue.Core.Tests.Plans;

/// <summary>
/// The automatic "which column is me" choice (<see cref="ColumnSuggestion"/>), shared by the plugin (bundled plans,
/// share codes, the import's saved choice) and the CLI (no <c>--assignee</c>).
/// </summary>
public class ColumnSuggestionTests
{
    private static readonly Lazy<MitPlan> LazyLpdu = new(() => PlanSerializer.FromYaml(File.ReadAllText(Path.Combine(RepoFiles.Root, "plans", "top-lpdu.yaml"))));

    private static MitPlan Lpdu => LazyLpdu.Value;

    private static List<PlanEntry> Selection(MitPlan plan, Job job, string? slot)
    {
        var pick = ColumnSuggestion.Suggest(plan, job, slot);
        return PlanSelection.For(plan, ColumnSuggestion.Context(pick, job, slot, 90)).ToList();
    }

    private static PlanEntry Make(string id, string column, Job? job, string? slot) => new()
    {
        Id = id,
        Mechanic = "p1.pantokrator",
        Who = new WhoSpec { Assignee = column, Job = job, Slot = slot },
        Do = [PlanItem.ForNote(id)],
    };

    /// <summary>LPDU's shape: slot columns, the H2 healers' columns, and one column per tank pair for each of its jobs.</summary>
    private static MitPlan Model() => new()
    {
        Assignees =
        [
            new Assignee { Key = "MT", Slot = "MT" },
            new Assignee { Key = "H2", Slot = "H2" },
            new Assignee { Key = "H2 SCH", Slot = "H2", Job = Job.SCH },
            new Assignee { Key = "WAR/DRK WAR", Job = Job.WAR },
            new Assignee { Key = "WAR/DRK DRK", Job = Job.DRK },
            new Assignee { Key = "WAR/GNB WAR", Job = Job.WAR },
            new Assignee { Key = "CM LB Tank" },
        ],
        Entries =
        [
            Make("mt-war", "MT", Job.WAR, "MT"),
            Make("mt-any", "MT", null, "MT"),           // a job-less MT call (Reprisal)
            Make("h2-any", "H2", null, "H2"),           // "Everything" in the job-less H2 column
            Make("h2-sch", "H2 SCH", Job.SCH, "H2"),
            Make("wd-war", "WAR/DRK WAR", Job.WAR, null),
            Make("wd-drk", "WAR/DRK DRK", Job.DRK, null),
            Make("wg-war", "WAR/GNB WAR", Job.WAR, null), // the same Warrior call with a Gunbreaker co-tank
            Make("lb-war", "CM LB Tank", Job.WAR, null),
        ],
    };

    [Theory]
    [InlineData("H2")]
    [InlineData(null)]
    public void AScholarGetsTheJoblessCallsOfTheirSlot(string? slot)
    {
        var plan = Model();
        Assert.Equal(new ColumnPick(null, "H2"), ColumnSuggestion.Suggest(plan, Job.SCH, slot));
        Assert.Equal(["h2-any", "h2-sch"], Selection(plan, Job.SCH, slot).Select(e => e.Id));
    }

    [Theory]
    [InlineData("MT")]
    [InlineData("OT")]
    [InlineData(null)]
    public void ATankKeepsOnePairColumn(string? slot)
    {
        // Review finding: "any column for WAR" was suggested because it had the job-less MT calls too, but it also showed
        // every tank pair's Warrior column and both Cosmo Memory roles: alternatives, not more of the player's calls.
        var plan = Model();
        Assert.Equal("WAR/DRK WAR", ColumnSuggestion.Suggest(plan, Job.WAR, slot).AssigneeKey);
        var ids = Selection(plan, Job.WAR, slot).Select(e => e.Id).ToList();
        Assert.Contains("wd-war", ids);
        Assert.DoesNotContain("wg-war", ids);
        Assert.DoesNotContain("lb-war", ids);
    }

    [Fact]
    public void ThePlayersColumnsAreTheirColumnAndTheirSlotsJoblessColumns()
    {
        var plan = Model();
        var columns = ColumnSuggestion.PlayerColumns(plan.Assignees, Job.WAR, "MT");
        Assert.Equal(["MT", "WAR/DRK WAR", "WAR/GNB WAR"], columns.Select(a => a.Key));
        Assert.Equal(["MT", "WAR/DRK WAR"], ColumnSuggestion.IntendedColumns(columns, "WAR/DRK WAR"));
        Assert.Equal(["MT", "WAR/GNB WAR"], ColumnSuggestion.IntendedColumns(columns, "war/gnb war"));
        Assert.Equal(["MT"], ColumnSuggestion.IntendedColumns(columns, "Nobody"));
    }

    [Fact]
    public void AnotherColumnWithEveryCallIsStillChosenWhenItShowsNoOtherColumn()
    {
        // The job-less H2 column with a column for everyone: "any column" would show its calls, the H2 column does not.
        var plan = Model();
        plan.Assignees.Add(new Assignee { Key = "Everyone" });
        plan.Entries.Add(Make("all", "Everyone", null, null));
        Assert.Equal(new ColumnPick("H2", "H2"), ColumnSuggestion.Suggest(plan, Job.SCH, "H2"));
    }

    [Theory]
    [InlineData(Job.WAR, "MT", "WAR/DRK WAR", 24)]
    [InlineData(Job.WAR, "OT", "WAR/DRK WAR", 23)]
    [InlineData(Job.WAR, null, "WAR/DRK WAR", 9)]
    [InlineData(Job.DRK, "MT", "WAR/DRK DRK", 23)]
    [InlineData(Job.DRK, "OT", "WAR/DRK DRK", 22)]
    [InlineData(Job.DRK, null, "WAR/DRK DRK", 8)]
    [InlineData(Job.SCH, "H2", null, 31)]
    [InlineData(Job.SCH, null, null, 31)]
    [InlineData(Job.WHM, "H1", null, 11)]
    [InlineData(Job.AST, null, null, 20)]
    [InlineData(Job.SGE, "H2", null, 31)]
    public void OnTheBundledLpduPlan(Job job, string? slot, string? column, int entries)
    {
        var pick = ColumnSuggestion.Suggest(Lpdu, job, slot);
        var selection = Selection(Lpdu, job, slot);

        Assert.Equal(column, pick.AssigneeKey);
        Assert.Equal(entries, selection.Count);

        // Never two calls of one action at one mechanic (three Holmgangs at Solar Ray 3 before the fix).
        var twice = selection
            .SelectMany(e => e.Do.SelectMany(i => i.ActionIds()).Distinct().Select(id => (e.Mechanic, Id: id)))
            .GroupBy(x => x)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(twice.Count == 0, string.Join(", ", twice));
    }

    [Fact]
    public void JobColumnPrefersTheColumnInTheConfiguredSlot()
    {
        var plan = new MitPlan
        {
            Assignees =
            [
                new Assignee { Key = "H1 SCH", Slot = "H1", Job = Job.SCH },
                new Assignee { Key = "H2", Slot = "H2" },
                new Assignee { Key = "H2 SCH", Slot = "H2", Job = Job.SCH },
            ],
        };

        Assert.Equal("H2 SCH", ColumnSuggestion.JobColumn(plan, Job.SCH, "H2")?.Key);
        Assert.Equal("H1 SCH", ColumnSuggestion.JobColumn(plan, Job.SCH, null)?.Key);
        Assert.Equal("H2", ColumnSuggestion.JobColumn(plan, Job.WHM, "H2")?.Key);
        Assert.Null(ColumnSuggestion.JobColumn(plan, Job.WHM, null));
        Assert.Equal(new ColumnPick(null, "MT"), ColumnSuggestion.ForJob(plan, Job.WAR, "MT"));
    }
}
