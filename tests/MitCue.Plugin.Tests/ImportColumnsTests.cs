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
    private static MitPlan Lpdu(bool withEveryoneColumn = false)
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
        var plan = Lpdu();
        var store = Store(defaultSlot);

        var choice = ImportColumns.Choose(plan, Job.SCH, 90, defaultSlot);
        if (choice.Save)
            store.SetMapping(plan, Job.SCH, choice.Mapping); // what the Save step does

        var shown = Runtime(store, plan, Job.SCH, defaultSlot);
        Assert.Equal(new[] { "p1-h2", "p1-h2-sch", "p3-sch" }, shown);
        Assert.Equal(shown.Count, choice.Mine);
        Assert.True(choice.Save);
        Assert.Null(choice.Mapping.AssigneeKey);
        Assert.Equal("H2", choice.Mapping.Slot);
        Assert.Equal(new[] { "H2", "H2 SCH" }, choice.Columns);
        Assert.Equal("any column for SCH in slot H2", ImportColumns.Describe(choice.Mapping, Job.SCH));
    }

    [Fact]
    public void WithoutTheFixTheSuggestionDropsTheH2Calls()
    {
        // The state the finding describes: no column choice saved, so the suggestion decides.
        var plan = Lpdu();
        var shown = Runtime(Store(null), plan, Job.SCH, null);
        Assert.DoesNotContain("p1-h2", shown);
    }

    [Fact]
    public void PrefersTheSlotColumnWhenAnyColumnWouldAddOtherCalls()
    {
        // With a column for everyone, "any column" would also show its calls: the job-less H2 column is chosen instead.
        var plan = Lpdu(withEveryoneColumn: true);
        var store = Store(null);

        var choice = ImportColumns.Choose(plan, Job.SCH, 90, null);
        store.SetMapping(plan, Job.SCH, choice.Mapping);

        Assert.True(choice.Save);
        Assert.Equal("H2", choice.Mapping.AssigneeKey);
        Assert.Equal(new[] { "p1-h2", "p1-h2-sch", "p3-sch" }, Runtime(store, plan, Job.SCH, null));
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
        var plan = Lpdu();
        var kept = new PlayerMapping { AssigneeKey = "H2 SCH", Slot = "H2" };

        var choice = ImportColumns.Choose(plan, Job.SCH, 90, null, kept);

        Assert.True(choice.Kept);
        Assert.False(choice.Save);
        Assert.Same(kept, choice.Mapping);
        Assert.Equal(2, choice.Mine);
        Assert.Equal(1, choice.Missed);
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
