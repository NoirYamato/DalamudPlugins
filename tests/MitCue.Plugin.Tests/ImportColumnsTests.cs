using MitCue.Core;
using MitCue.Core.Plans;
using MitCue.Import;
using MitCue.Plans;
using MitCue.PluginTests.Support;

namespace MitCue.PluginTests;

/// <summary>The column choice an import saves, so the overlay shows every call the Save step counts.</summary>
public class ImportColumnsTests
{
    /// <summary>LPDU's healer columns as the importer reads them (the review's model: ImportAnalysisTests.Lpdu).</summary>
    private static MitPlan Lpdu()
    {
        var plan = new MitPlan
        {
            Id = Guid.NewGuid().ToString("D"),
            Fight = "top",
            Assignees =
            [
                new Assignee { Key = "MT", Slot = "MT" },
                new Assignee { Key = "H1", Slot = "H1" },
                new Assignee { Key = "H2", Slot = "H2" },
                new Assignee { Key = "H1 WHM", Slot = "H1", Job = Job.WHM },
                new Assignee { Key = "H2 SCH", Slot = "H2", Job = Job.SCH },
                new Assignee { Key = "H2 SGE", Slot = "H2", Job = Job.SGE },
            ],
        };
        plan.Entries.Add(Make("p1-mt", "p1.pantokrator", "MT", null, "MT"));
        plan.Entries.Add(Make("p1-h1", "p1.pantokrator", "H1", null, "H1"));
        plan.Entries.Add(Make("p1-h2", "p1.pantokrator", "H2", null, "H2"));          // "Everything" in the job-less H2 column
        plan.Entries.Add(Make("p1-h2-sch", "p1.program_loop", "H2", Job.SCH, "H2")); // "Kera/Soil", split per job
        plan.Entries.Add(Make("p1-h2-sge", "p1.program_loop", "H2", Job.SGE, "H2"));
        plan.Entries.Add(Make("p3-mt", "p2.solar_ray", "MT", null, "MT"));
        plan.Entries.Add(Make("p3-whm", "p2.solar_ray", "H1 WHM", Job.WHM, "H1"));
        plan.Entries.Add(Make("p3-sch", "p2.solar_ray", "H2 SCH", Job.SCH, "H2"));
        plan.Entries.Add(Make("p3-sge", "p2.solar_ray", "H2 SGE", Job.SGE, "H2"));
        return plan;
    }

    /// <summary>
    /// One column per phase for the Scholar: a P1/P2 tab headed "SCH" and a P3 tab headed "H2 SCH" (review round 3: the
    /// import marked and imported both columns, but its Save step counted only the P3 column's calls).
    /// </summary>
    private static MitPlan PerPhase(bool withEveryoneColumn = false)
    {
        var plan = new MitPlan
        {
            Id = Guid.NewGuid().ToString("D"),
            Fight = "top",
            Assignees =
            [
                new Assignee { Key = "SCH", Job = Job.SCH },
                new Assignee { Key = "H2 SCH", Slot = "H2", Job = Job.SCH },
            ],
        };
        plan.Entries.Add(Make("p1", "p1.pantokrator", "SCH", Job.SCH, null));
        plan.Entries.Add(Make("p2", "p2.solar_ray", "SCH", Job.SCH, null));
        plan.Entries.Add(Make("p3", "p3.hello_world", "H2 SCH", Job.SCH, "H2"));
        if (withEveryoneColumn)
        {
            plan.Assignees.Add(new Assignee { Key = "Everyone" });
            plan.Entries.Add(Make("all", "p2.solar_ray", "Everyone", null, null));
        }

        return plan;
    }

    private static PlanEntry Make(string id, string mechanic, string column, Job? job, string? slot) => new()
    {
        Id = id,
        Mechanic = mechanic,
        Who = new WhoSpec { Assignee = column, Job = job, Slot = slot },
        Do = [PlanItem.ForNote("x")],
    };

    private static PlanStore Store(string? defaultSlot) =>
        new(Kit.TempDirectory(), new Configuration { DefaultSlot = defaultSlot }, () => { }, Kit.Errors());

    /// <summary>What the overlay selects for a saved plan: SessionManager's mapping and context, then PlanSelection.</summary>
    private static List<string> Runtime(PlanStore store, MitPlan plan, Job job, string? defaultSlot)
    {
        var mapping = store.MappingFor(plan, job);
        var me = new PlayerContext(job, 90, mapping.Slot ?? defaultSlot, mapping.AssigneeKey);
        return PlanSelection.For(plan, me).Select(e => e.Id).ToList();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("H2")]
    public void AScholarOnLpduKeepsTheJoblessH2Calls(string? defaultSlot)
    {
        // Review finding (the reviewer's probe): the Save step counted the "H2" and "H2 SCH" calls, but nothing was saved,
        // and the runtime's suggestion ("H2 SCH") dropped the job-less "H2" calls of P1/P2 ("Everything" on Pantokrator).
        // Since review round 3 the job's column has the job-less calls of its slot: nothing needs saving.
        var plan = Lpdu();
        var store = Store(defaultSlot);

        var choice = ImportColumns.Choose(plan, Job.SCH, 90, defaultSlot);
        if (choice.Save)
            store.SetMapping(plan, Job.SCH, choice.Mapping); // what the Save step does

        var shown = Runtime(store, plan, Job.SCH, defaultSlot);
        Assert.Equal(new[] { "p1-h2", "p1-h2-sch", "p3-sch" }, shown);
        Assert.Equal(shown.Count, choice.Mine);
        Assert.False(choice.Save);
        Assert.Equal(0, choice.Missed);
        Assert.Equal("H2 SCH", choice.Mapping.AssigneeKey);
        Assert.Equal("H2", choice.Mapping.Slot);
        Assert.Equal(new[] { "H2", "H2 SCH" }, choice.Columns);
        Assert.Equal("column \"H2 SCH\"", ImportColumns.Describe(choice.Mapping, Job.SCH));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("H2")]
    public void WithoutASavedChoiceTheSuggestionKeepsTheJoblessH2Calls(string? defaultSlot)
    {
        // Final review finding: bundled plans and share codes have no saved choice, and the suggestion was the job's column
        // "H2 SCH", which dropped the job-less "H2" calls the import saves a choice for. The suggestion now is the same.
        var plan = Lpdu();
        var store = Store(defaultSlot);

        var mapping = store.MappingFor(plan, Job.SCH);

        Assert.True(mapping.IsSuggestion);
        Assert.Equal("H2 SCH", mapping.AssigneeKey);
        Assert.Equal("H2", mapping.Slot);
        Assert.Equal(new[] { "p1-h2", "p1-h2-sch", "p3-sch" }, Runtime(store, plan, Job.SCH, defaultSlot));
        Assert.Equal(ImportColumns.Choose(plan, Job.SCH, 90, defaultSlot).Mapping.AssigneeKey, mapping.AssigneeKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("H2")]
    public void AScholarWithAColumnPerPhaseGetsEveryColumn(string? defaultSlot)
    {
        // Review round 3: the Save step counted 1 call (the job's column "H2 SCH") and warned about nothing, though the
        // import had marked and imported both columns as the player's; the overlay then showed P3 only.
        var plan = PerPhase();
        var store = Store(defaultSlot);

        var choice = ImportColumns.Choose(plan, Job.SCH, 90, defaultSlot);
        if (choice.Save)
            store.SetMapping(plan, Job.SCH, choice.Mapping);

        Assert.Equal(new[] { "SCH", "H2 SCH" }, ImportAnalysis.PlayerColumns(plan.Assignees, Job.SCH, defaultSlot).Select(a => a.Key));
        Assert.Equal(new[] { "SCH", "H2 SCH" }, choice.Columns);
        Assert.True(choice.Save);
        Assert.Equal("any column for SCH in slot H2", ImportColumns.Describe(choice.Mapping, Job.SCH));
        Assert.Equal(3, choice.Mine);
        Assert.Equal(0, choice.Missed);
        Assert.Equal(new[] { "p1", "p2", "p3" }, Runtime(store, plan, Job.SCH, defaultSlot));
    }

    [Fact]
    public void TheSuggestionIsTheJobsColumnWhenThatHasEveryCall()
    {
        // A WHM on the plan without LPDU's job-less "H1" column: the job's column "H1 WHM" has every call, so it stays.
        var plan = Lpdu();
        plan.Entries.RemoveAll(e => e.Who.Assignee == "H1");
        plan.Assignees.RemoveAll(a => a.Key == "H1");

        var mapping = Store(null).MappingFor(plan, Job.WHM);

        Assert.True(mapping.IsSuggestion);
        Assert.Equal("H1 WHM", mapping.AssigneeKey);
    }

    [Fact]
    public void PrefersAColumnWhenAnyColumnWouldAddOtherCalls()
    {
        // With a column for everyone, "any column" would also show its calls: the "SCH" column (with the "H2 SCH" calls of
        // its slot H2) is chosen instead.
        var plan = PerPhase(withEveryoneColumn: true);
        var store = Store(null);

        var choice = ImportColumns.Choose(plan, Job.SCH, 90, null);
        store.SetMapping(plan, Job.SCH, choice.Mapping);

        Assert.True(choice.Save);
        Assert.Equal("SCH", choice.Mapping.AssigneeKey);
        Assert.Equal("H2", choice.Mapping.Slot);
        Assert.Equal(new[] { "p1", "p2", "p3" }, Runtime(store, plan, Job.SCH, null));
        Assert.Equal(3, choice.Mine);
    }

    [Fact]
    public void SavesNothingWhenTheSuggestionShowsEveryCall()
    {
        // Carry the Clear: one column per job.
        var plan = new MitPlan
        {
            Assignees = [new Assignee { Key = "White Mage", Job = Job.WHM }, new Assignee { Key = "Scholar", Job = Job.SCH }],
            Entries =
            [
                Make("w1", "p1.pantokrator", "White Mage", Job.WHM, null),
                Make("w2", "p2.solar_ray", "White Mage", Job.WHM, null),
                Make("s1", "p1.pantokrator", "Scholar", Job.SCH, null),
            ],
        };

        var choice = ImportColumns.Choose(plan, Job.WHM, 90, null);

        Assert.False(choice.Save);
        Assert.True(choice.Mapping.IsSuggestion);
        Assert.Equal("White Mage", choice.Mapping.AssigneeKey);
        Assert.Equal(2, choice.Mine);
        Assert.Equal(0, choice.Missed);
    }

    [Fact]
    public void KeepsTheColumnChoiceOfAReplacedPlanAndCountsWhatItMisses()
    {
        var plan = PerPhase();
        var kept = new PlayerMapping { AssigneeKey = "H2 SCH", Slot = "H2" };

        var choice = ImportColumns.Choose(plan, Job.SCH, 90, null, kept);

        Assert.True(choice.Kept);
        Assert.False(choice.Save);
        Assert.Same(kept, choice.Mapping);
        Assert.Equal(1, choice.Mine);
        Assert.Equal(2, choice.Missed);
        Assert.Equal(new[] { "SCH" }, choice.MissedIn);
    }

    /// <summary>LPDU's tank columns: the MT slot column (with a job-less Reprisal) and one Warrior column per tank pair.</summary>
    private static MitPlan TankPairs() => new()
    {
        Id = Guid.NewGuid().ToString("D"),
        Fight = "top",
        Assignees =
        [
            new Assignee { Key = "MT", Slot = "MT" },
            new Assignee { Key = "WAR/DRK WAR", Job = Job.WAR },
            new Assignee { Key = "WAR/GNB WAR", Job = Job.WAR },
        ],
        Entries =
        [
            Make("mt-war", "p1.pantokrator", "MT", Job.WAR, "MT"),
            Make("mt-reprisal", "p1.pantokrator", "MT", null, "MT"),
            Make("wd-holmgang", "p2.solar_ray", "WAR/DRK WAR", Job.WAR, null),
            Make("wg-holmgang", "p2.solar_ray", "WAR/GNB WAR", Job.WAR, null),
        ],
    };

    [Fact]
    public void ATanksImportKeepsTheirPairColumnWithTheJoblessMtCalls()
    {
        // Review finding (41d2893): the tank pairs' columns are alternatives. "Any column for WAR in slot MT" has the
        // job-less MT call, but also the Holmgang of every pair; it is not saved. Review round 3: the job's column shows
        // the job-less MT call too (round 2 left it out and only said so here; bundled plans and share codes said nothing).
        var plan = TankPairs();
        var store = Store("MT");
        var choice = ImportColumns.Choose(plan, Job.WAR, 90, "MT");

        Assert.False(choice.Save);
        Assert.False(choice.Kept);
        Assert.Equal("WAR/DRK WAR", choice.Mapping.AssigneeKey);
        Assert.Equal(new[] { "MT", "WAR/DRK WAR" }, choice.Columns);
        Assert.Equal(3, choice.Mine);
        Assert.Equal(0, choice.Missed);
        Assert.Equal(new[] { "mt-war", "mt-reprisal", "wd-holmgang" }, Runtime(store, plan, Job.WAR, "MT"));
    }

    [Fact]
    public void AKeptTankPairColumnIsNotCountedAsMissingTheOtherPairs()
    {
        var kept = new PlayerMapping { AssigneeKey = "WAR/GNB WAR", Slot = "MT" };

        var choice = ImportColumns.Choose(TankPairs(), Job.WAR, 90, "MT", kept);

        Assert.True(choice.Kept);
        Assert.Equal(new[] { "MT", "WAR/GNB WAR" }, choice.Columns);
        Assert.Equal(3, choice.Mine);
        Assert.Equal(0, choice.Missed); // neither the job-less MT call (shown) nor the WAR/DRK pair's Holmgang (another pair)
    }

    [Fact]
    public void AKeptSlotColumnIsCountedAsMissingThePairsCalls()
    {
        // The player chose the "MT" column for the plan this replaces: it has the MT calls, but no pair's.
        var kept = new PlayerMapping { AssigneeKey = "MT", Slot = "MT" };

        var choice = ImportColumns.Choose(TankPairs(), Job.WAR, 90, "MT", kept);

        Assert.Equal(new[] { "MT", "WAR/DRK WAR" }, choice.Columns);
        Assert.Equal(2, choice.Mine);
        Assert.Equal(1, choice.Missed);
        Assert.Equal(new[] { "WAR/DRK WAR" }, choice.MissedIn);
    }

    [Fact]
    public void AGeneralColumnNextToSeveralPairsSaysWhichCallsTheJobsColumnLeavesOut()
    {
        // A general "WAR" column and two pair columns: no one choice has the general calls and one pair's without the
        // other pair's, so the job's column stays and the Save step names the pair column it leaves out.
        var plan = new MitPlan
        {
            Id = Guid.NewGuid().ToString("D"),
            Fight = "top",
            Assignees =
            [
                new Assignee { Key = "WAR", Job = Job.WAR },
                new Assignee { Key = "WAR/DRK WAR", Job = Job.WAR },
                new Assignee { Key = "WAR/GNB WAR", Job = Job.WAR },
            ],
            Entries =
            [
                Make("general", "p1.pantokrator", "WAR", Job.WAR, null),
                Make("wd-holmgang", "p5.solar_ray_3", "WAR/DRK WAR", Job.WAR, null),
                Make("wg-holmgang", "p5.solar_ray_3", "WAR/GNB WAR", Job.WAR, null),
            ],
        };

        var choice = ImportColumns.Choose(plan, Job.WAR, 90, "MT");

        Assert.False(choice.Save);
        Assert.Equal("WAR", choice.Mapping.AssigneeKey);
        Assert.Equal(new[] { "WAR", "WAR/DRK WAR" }, choice.Columns);
        Assert.Equal(1, choice.Mine);
        Assert.Equal(1, choice.Missed);
        Assert.Equal(new[] { "WAR/DRK WAR" }, choice.MissedIn);
    }

    [Fact]
    public void TheSaveStepCountsAnAiPlansSlotSynonymsAsTheOverlayShowsThem()
    {
        // Review round 3: the AI route read "D3" as written, so the Save step counted 0 calls for the R1 Bard (and left
        // "Make it my active plan" unticked) while the saved plan, with the store's standard slots, showed 1.
        var plan = ImportPipeline.ParseYaml("""
            format: mitcue-plan/1
            name: Synonym slots
            fight: top
            entries:
              - id: e001
                mechanic: p1.pantokrator
                who: {slot: D3}
                do:
                  - {action: 7549, name: Feint}
            """);

        Assert.Equal("R1", plan.Entries[0].Who.Slot);
        Assert.Equal(1, ImportColumns.Choose(plan, Job.BRD, 90, "R1").Mine);
    }

    [Fact]
    public void SavesNothingWhenThePlayersColumnsNameDifferentSlots()
    {
        // The configured slot (H1) and the job's column (H2 SCH) disagree: no guess is saved.
        var plan = Lpdu();
        var choice = ImportColumns.Choose(plan, Job.SCH, 90, "H1");
        Assert.False(choice.Save);
        Assert.True(choice.Mapping.IsSuggestion);
    }

    [Fact]
    public void ANonCombatJobGetsNoChoice()
    {
        var choice = ImportColumns.Choose(Lpdu(), Job.None, 90, null);
        Assert.False(choice.Save);
        Assert.Equal(0, choice.Mine);
    }
}
