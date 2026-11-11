using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Core.Tests.Data;
using MitCue.Core.Tests.Import;

namespace MitCue.Core.Tests.Plans;

/// <summary>
/// The automatic "which column is me" choice (<see cref="ColumnSuggestion"/>), shared by the plugin (bundled plans,
/// share codes, the import's saved choice) and the CLI (no <c>--assignee</c>).
/// </summary>
public class ColumnSuggestionTests
{
    /// <summary>LPDU's job-less "MT" calls: eight Reprisals and three Ramparts, for whoever tanks in MT.</summary>
    internal static readonly string[] LpduMtCalls = ["e027", "e044", "e083", "e089", "e115", "e121", "e145", "e172", "e216", "e245", "e279"];

    private static readonly Lazy<MitPlan> LazyLpdu = new(() => PlanSerializer.FromYaml(File.ReadAllText(Path.Combine(RepoFiles.Root, "plans", "top-lpdu.yaml"))));

    /// <summary>Every tab of Carry the Clear: the White Mage and Scholar tabs, and the combined tab (its columns rekeyed).</summary>
    private static readonly Lazy<MitPlan> LazyCarryTheClear = new(() =>
        new PlanImporter().Import(Fixtures.CarryTheClear, PlanImports.Options("Carry the Clear", null, PlanImports.CtcUrl, "carry_the_clear")).Plan);

    /// <summary>The same sheet with its combined tab moved first, so the combined tab's columns are the plan's first ones.</summary>
    private static readonly Lazy<MitPlan> LazyCombinedTabFirst = new(() =>
    {
        var doc = Fixtures.CarryTheClear;
        var combined = doc.Tabs.Single(t => t.Name == "TOP – WHM + SCH");
        var reordered = new SheetDocument { Title = doc.Title, Tabs = [combined, .. doc.Tabs.Where(t => t != combined)] };
        return new PlanImporter().Import(reordered, PlanImports.Options("Carry the Clear", null, PlanImports.CtcUrl, "carry_the_clear")).Plan;
    });

    private static MitPlan Lpdu => LazyLpdu.Value;

    private static List<PlanEntry> Selection(MitPlan plan, Job job, string? slot, IReadOnlyCollection<Job>? party = null)
    {
        var pick = ColumnSuggestion.Suggest(plan, job, slot, party);
        return PlanSelection.For(plan, ColumnSuggestion.Context(pick, job, slot, 90)).ToList();
    }

    private static PlanEntry Make(string id, string column, Job? job, string? slot, string mechanic = "p1.pantokrator") => new()
    {
        Id = id,
        Mechanic = mechanic,
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

    /// <summary>
    /// One column per phase for each healer: what <c>mitcue import</c> makes of a sheet whose P1/P2 tab is headed
    /// "WHM"/"SCH" and whose P3 tab is headed "H1 WHM"/"H2 SCH" (review round 3).
    /// </summary>
    private static MitPlan PerPhase() => new()
    {
        Assignees =
        [
            new Assignee { Key = "MT", Slot = "MT" },
            new Assignee { Key = "WHM", Job = Job.WHM },
            new Assignee { Key = "SCH", Job = Job.SCH },
            new Assignee { Key = "H1 WHM", Slot = "H1", Job = Job.WHM },
            new Assignee { Key = "H2 SCH", Slot = "H2", Job = Job.SCH },
        ],
        Entries =
        [
            Make("p1-sch", "SCH", Job.SCH, null, "p1.pantokrator"),
            Make("p2-sch", "SCH", Job.SCH, null, "p2.solar_ray"),
            Make("p2-sch-2", "SCH", Job.SCH, null, "p2.limitless"),
            Make("p3-sch", "H2 SCH", Job.SCH, "H2", "p3.hello_world"),
            Make("p1-whm", "WHM", Job.WHM, null, "p1.pantokrator"),
            Make("p3-whm", "H1 WHM", Job.WHM, "H1", "p3.hello_world"),
            Make("p1-mt", "MT", null, "MT", "p1.pantokrator"),
        ],
    };

    /// <summary>
    /// A "General" tab headed WAR/DRK and a tank-pair tab "WAR/DRK" with the same headers, which the importer keys
    /// "WAR/DRK WAR" and "WAR/DRK DRK" (review round 3).
    /// </summary>
    private static MitPlan GeneralAndPair() => new()
    {
        Assignees =
        [
            new Assignee { Key = "WAR", Job = Job.WAR },
            new Assignee { Key = "DRK", Job = Job.DRK },
            new Assignee { Key = "WAR/DRK WAR", Job = Job.WAR },
            new Assignee { Key = "WAR/DRK DRK", Job = Job.DRK },
        ],
        Entries =
        [
            Make("e001", "DRK", Job.DRK, null, "p1.pantokrator"),
            Make("e002", "WAR", Job.WAR, null, "p1.pantokrator"),
            Make("e003", "DRK", Job.DRK, null, "p2.solar_ray"),
            Make("e004", "WAR", Job.WAR, null, "p2.solar_ray"),
            Make("e005", "WAR/DRK WAR", Job.WAR, null, "p5.solar_ray_1"),
            Make("e006", "WAR/DRK DRK", Job.DRK, null, "p5.solar_ray_1"),
        ],
    };

    [Theory]
    [InlineData("H2")]
    [InlineData(null)]
    public void AScholarsColumnHasTheJoblessCallsOfTheirSlot(string? slot)
    {
        // The job's column "H2 SCH" (slot H2) shows the job-less "H2" calls too (PlanSelection.Applies).
        var plan = Model();
        Assert.Equal(new ColumnPick("H2 SCH", "H2"), ColumnSuggestion.Suggest(plan, Job.SCH, slot));
        Assert.Equal(["h2-any", "h2-sch"], Selection(plan, Job.SCH, slot).Select(e => e.Id));
    }

    [Theory]
    [InlineData("MT")]
    [InlineData("OT")]
    [InlineData(null)]
    public void ATankKeepsOnePairColumnAndTheJoblessCallsOfTheirSlot(string? slot)
    {
        // Round 2: "any column for WAR" also showed every tank pair's Warrior column and both Cosmo Memory roles. Round 3:
        // the pair's column alone left out the job-less MT calls (Reprisal, Rampart); they are the MT's in every pair.
        var plan = Model();
        Assert.Equal("WAR/DRK WAR", ColumnSuggestion.Suggest(plan, Job.WAR, slot).AssigneeKey);
        var ids = Selection(plan, Job.WAR, slot).Select(e => e.Id).ToList();
        Assert.Contains("wd-war", ids);
        Assert.DoesNotContain("wg-war", ids);
        Assert.DoesNotContain("lb-war", ids);
        Assert.Equal(slot == "MT", ids.Contains("mt-any"));
        Assert.Equal(slot == "MT", ids.Contains("mt-war"));
    }

    [Fact]
    public void ThePlayersColumnsAreTheirSlotsJoblessColumnsAndOnePair()
    {
        var plan = Model();
        var columns = ColumnSuggestion.PlayerColumns(plan.Assignees, Job.WAR, "MT");
        Assert.Equal(["MT", "WAR/DRK WAR", "WAR/GNB WAR"], columns.Select(a => a.Key));
        Assert.Equal(["MT", "WAR/DRK WAR"], ColumnSuggestion.IntendedColumns(plan, columns, "WAR/DRK WAR"));
        Assert.Equal(["MT", "WAR/GNB WAR"], ColumnSuggestion.IntendedColumns(plan, columns, "war/gnb war"));

        // No pair chosen (the "MT" column, or none): the player still plays one pair, the first.
        Assert.Equal(["MT", "WAR/DRK WAR"], ColumnSuggestion.IntendedColumns(plan, columns, "MT"));
        Assert.Equal(["MT", "WAR/DRK WAR"], ColumnSuggestion.IntendedColumns(plan, columns, "Nobody"));
    }

    [Theory]
    [InlineData(Job.SCH, "H2", new[] { "p1-sch", "p2-sch", "p2-sch-2", "p3-sch" })]
    [InlineData(Job.SCH, null, new[] { "p1-sch", "p2-sch", "p2-sch-2", "p3-sch" })]
    [InlineData(Job.WHM, "H1", new[] { "p1-whm", "p3-whm" })]
    [InlineData(Job.WHM, null, new[] { "p1-whm", "p3-whm" })]
    public void OneColumnPerPhaseIsOneSeat(Job job, string? slot, string[] expected)
    {
        // Review round 3: round 2 took the job's other columns for alternatives and kept one ("H2 SCH": P3 only).
        var plan = PerPhase();
        var columns = ColumnSuggestion.PlayerColumns(plan.Assignees, job, slot);
        Assert.Equal(columns.Where(a => a.Job == job).Select(a => a.Key), ColumnSuggestion.IntendedColumns(plan, columns, ColumnSuggestion.ForJob(plan, job, slot).AssigneeKey));

        Assert.Equal(new ColumnPick(null, job == Job.SCH ? "H2" : "H1"), ColumnSuggestion.Suggest(plan, job, slot));
        Assert.Equal(expected, Selection(plan, job, slot).Select(e => e.Id).Order());
    }

    [Fact]
    public void AGeneralColumnAndOnePairColumnAreOneSeat()
    {
        // The pair tab adds the Warrior's P5 Holmgang to the general tab's calls: not the same calls written twice.
        var plan = GeneralAndPair();
        Assert.Equal(new ColumnPick(null, "MT"), ColumnSuggestion.Suggest(plan, Job.WAR, "MT"));
        Assert.Equal(["e002", "e004", "e005"], Selection(plan, Job.WAR, "MT").Select(e => e.Id));
    }

    [Theory]
    [InlineData(Job.WHM, "White Mage", 57)]
    [InlineData(Job.SCH, "Scholar", 69)]
    public void ACombinedTabThatRepeatsTheJobsTabIsAnAlternative(Job job, string column, int entries)
    {
        // Carry the Clear with every tab: the combined "TOP – WHM + SCH" tab is the White Mage's (and the Scholar's) seat
        // a second time, at the same mechanics, so the calls are not shown twice (57 and 69, not 115 and 138).
        var plan = LazyCarryTheClear.Value;
        Assert.Contains(plan.Assignees, a => a.Key == "TOP – WHM + SCH " + column && ColumnSuggestion.IsComposition(a));

        Assert.Equal(column, ColumnSuggestion.Suggest(plan, job, null).AssigneeKey);
        var selection = Selection(plan, job, null);
        Assert.Equal(entries, selection.Count);
        Assert.All(selection, e => Assert.Equal(column, e.Who.Assignee));

        // A choice of the combined tab's column (kept from an earlier import): the job's own tab repeats it, so it is not
        // counted as calls the choice leaves out (review round 4: 57 White Mage calls were).
        var columns = ColumnSuggestion.PlayerColumns(plan.Assignees, job, null);
        Assert.Equal(["TOP – WHM + SCH " + column], ColumnSuggestion.IntendedColumns(plan, columns, "TOP – WHM + SCH " + column));
        Assert.Equal([column], ColumnSuggestion.IntendedColumns(plan, columns, column));
    }

    [Theory]
    [InlineData(Job.WHM, "White Mage", "H1", 58)]
    [InlineData(Job.WHM, "White Mage", null, 58)]
    [InlineData(Job.SCH, "Scholar", "H2", 69)]
    [InlineData(Job.SCH, "Scholar", null, 69)]
    public void ACombinedTabFirstInTheSheetIsStillOneSeat(Job job, string column, string? slot, int entries)
    {
        // Review round 4: with the combined tab first, the job's column was the combined tab's and the job's own tab counted
        // as more of the player's calls, so the suggestion became "any column for my job": every call twice (WHM 115 entries,
        // two Asylums at Program Loop; SCH 138), and the import saved that choice.
        var plan = LazyCombinedTabFirst.Value;
        var combined = "TOP – WHM + SCH " + column;
        Assert.Equal(combined, plan.Assignees.First(a => a.Job == job).Key);
        Assert.Contains(plan.Assignees, a => a.Key == column && a.Job == job);

        Assert.Equal(new ColumnPick(combined, slot), ColumnSuggestion.Suggest(plan, job, slot));
        Assert.Equal([combined], ColumnSuggestion.IntendedColumns(plan, ColumnSuggestion.PlayerColumns(plan.Assignees, job, slot), combined));
        var selection = Selection(plan, job, slot);
        Assert.Equal(entries, selection.Count);
        Assert.All(selection, e => Assert.Equal(combined, e.Who.Assignee));
    }

    [Theory]
    [InlineData(Job.WAR, "M1", new[] { "war" })] // a melee player's usual slot is not theirs on a tank
    [InlineData(Job.WAR, "MT", new[] { "mt", "war" })]
    [InlineData(Job.WHM, "MT", new string[0])] // no White Mage column: not the "MT" column either
    [InlineData(Job.SAM, "M1", new[] { "feint" })]
    public void AUsualSlotOfAnotherRoleIsNotThePlayersSlot(Job job, string slot, string[] expected)
    {
        // Review round 4: a Warrior whose usual slot is M1 got "any column for WAR in slot M1" with the M1 column's Feint,
        // and a White Mage whose usual slot is MT followed the "MT" column (Reprisal).
        var plan = new MitPlan
        {
            Assignees = [new Assignee { Key = "MT", Slot = "MT" }, new Assignee { Key = "M1", Slot = "M1" }, new Assignee { Key = "WAR", Job = Job.WAR }],
            Entries = [Make("mt", "MT", null, "MT"), Make("feint", "M1", null, "M1"), Make("war", "WAR", Job.WAR, null)],
        };

        Assert.Equal(expected, Selection(plan, job, slot).Select(e => e.Id).Order());
        Assert.DoesNotContain(ColumnSuggestion.PlayerColumns(plan.Assignees, job, slot), a => a.Slot is { } s && !Slots.Fits(s, job));
        Assert.Equal(Slots.Fits(slot, job) ? slot : null, ColumnSuggestion.Context(new ColumnPick(null, null), job, slot).Slot);
    }

    [Theory]
    [InlineData("MT", Job.WAR, "MT")]
    [InlineData("t1", Job.PLD, "MT")]
    [InlineData("D3", Job.DNC, "R1")] // physical ranged in a ranged slot
    [InlineData("R2", Job.PCT, "R2")]
    [InlineData("M1", Job.WAR, null)]
    [InlineData("MT", Job.WHM, null)]
    [InlineData("H1", Job.SAM, null)]
    [InlineData("M2", Job.None, "M2")] // no job: kept
    [InlineData("X9", Job.WAR, null)]
    [InlineData(null, Job.WAR, null)]
    public void AConfiguredSlotIsTheSlotOfJobsOfItsRole(string? slot, Job job, string? expected) =>
        Assert.Equal(expected, Slots.ForJob(slot, job));

    [Fact]
    public void AnotherColumnWithEveryCallIsStillChosenWhenItShowsNoOtherColumn()
    {
        // A column for everyone: "any column for SCH in H2" would show its call, the "SCH" column (with the H2 SCH calls
        // of its slot) does not.
        var plan = PerPhase();
        plan.Assignees.Add(new Assignee { Key = "Everyone" });
        plan.Entries.Add(Make("all", "Everyone", null, null));
        Assert.Equal(new ColumnPick("SCH", "H2"), ColumnSuggestion.Suggest(plan, Job.SCH, "H2"));
        Assert.DoesNotContain("all", Selection(plan, Job.SCH, "H2").Select(e => e.Id));
    }

    [Theory]
    [InlineData(Job.WAR, new[] { Job.WAR, Job.PLD, Job.WHM, Job.SCH }, "WAR/PLD WAR")]
    [InlineData(Job.WAR, new[] { Job.WAR, Job.GNB, Job.WHM, Job.SCH }, "WAR/GNB WAR")]
    [InlineData(Job.WAR, new[] { Job.WAR, Job.DRK, Job.WHM, Job.SCH }, "WAR/DRK WAR")]
    [InlineData(Job.WAR, new[] { Job.WAR, Job.WHM, Job.SCH }, "WAR/DRK WAR")] // no co-tank: the first pair
    [InlineData(Job.WAR, new Job[0], "WAR/DRK WAR")]                           // party unknown: the first pair
    [InlineData(Job.PLD, new[] { Job.PLD, Job.DRK, Job.WHM, Job.SCH }, "DRK/PLD PLD")]
    [InlineData(Job.GNB, new[] { Job.GNB, Job.PLD, Job.AST, Job.SGE }, "GNB/PLD GNB")]
    public void TheTankPairFollowsTheParty(Job job, Job[] party, string column)
    {
        // Review round 3: a Warrior next to a Paladin got the first pair's calls ("WAR/DRK WAR": Kitchen Sink at Solar
        // Ray 1, where WAR/PLD has it at Solar Ray 2), while the co-tank's dimmed calls came from "WAR/PLD PLD".
        Assert.Equal(column, ColumnSuggestion.Suggest(Lpdu, job, "MT", party).AssigneeKey);

        var ids = Selection(Lpdu, job, "MT", party).Select(e => e.Id).ToHashSet();
        Assert.Subset(ids, LpduMtCalls.ToHashSet());
        var pairs = Selection(Lpdu, job, "MT", party).Select(e => e.Who.Assignee).Where(k => k != null && k.Contains('/')).Distinct().ToList();
        Assert.Equal([column], pairs);
    }

    [Theory]
    [InlineData(Job.WAR, "MT", "WAR/DRK WAR", 35)]
    [InlineData(Job.WAR, "OT", "WAR/DRK WAR", 28)]
    [InlineData(Job.WAR, null, "WAR/DRK WAR", 9)]
    [InlineData(Job.DRK, "MT", "WAR/DRK DRK", 34)]
    [InlineData(Job.DRK, "OT", "WAR/DRK DRK", 27)]
    [InlineData(Job.DRK, null, "WAR/DRK DRK", 8)]
    [InlineData(Job.SCH, "H2", "H2 SCH", 31)]
    [InlineData(Job.SCH, null, "H2 SCH", 31)]
    [InlineData(Job.WHM, "H1", "H1 WHM", 11)]
    [InlineData(Job.AST, null, "H1 AST", 20)]
    [InlineData(Job.SGE, "H2", "H2 SGE", 31)]
    public void OnTheBundledLpduPlan(Job job, string? slot, string? column, int entries)
    {
        var pick = ColumnSuggestion.Suggest(Lpdu, job, slot);
        var selection = Selection(Lpdu, job, slot);

        Assert.Equal(column, pick.AssigneeKey);
        Assert.Equal(entries, selection.Count);

        // The healer's column has the job-less calls of their slot, as "any column in their slot" had before (the same
        // calls); the main tank has the job-less "MT" calls next to their pair's column.
        if (Jobs.RoleOf(job) == Role.Healer)
            Assert.Equal(PlanSelection.For(Lpdu, new PlayerContext(job, 90, pick.Slot)).ToHashSet(), selection.ToHashSet());
        if (slot == "MT")
            Assert.Subset(selection.Select(e => e.Id).ToHashSet(), LpduMtCalls.ToHashSet());

        // Never two calls of one action at one mechanic (three Holmgangs at Solar Ray 3 before round 2).
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

    [Theory]
    [InlineData("WAR/DRK WAR", true)]
    [InlineData("WARDRK WAR", true)] // the xlsx export's tab name
    [InlineData("TOP – WHM + SCH White Mage", true)]
    [InlineData("H2 SCH", false)]
    [InlineData("White Mage", false)]
    [InlineData("SCH", false)]
    [InlineData("CM LB Tank", false)]
    [InlineData("MT", false)]
    public void ACompositionColumnNamesSeveralJobs(string key, bool composition) =>
        Assert.Equal(composition, ColumnSuggestion.IsComposition(new Assignee { Key = key }));
}
