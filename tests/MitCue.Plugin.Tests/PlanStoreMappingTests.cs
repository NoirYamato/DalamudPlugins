using MitCue.Core;
using MitCue.Core.Plans;
using MitCue.Plans;
using MitCue.PluginTests.Support;

namespace MitCue.PluginTests;

/// <summary>"My column" is chosen per plan and job, like the Plans tab shows it (one row per fight and job).</summary>
public class PlanStoreMappingTests
{
    private static (PlanStore Store, Configuration Config) NewStore()
    {
        var config = new Configuration();
        return (new PlanStore(Kit.TempDirectory(), config, () => { }, Kit.Errors()), config);
    }

    private static MitPlan HealerTabPlan() => Kit.Plan(
        [
            Kit.Entry("p1.raid", -5, Kit.Temperance, "Temperance", job: Job.WHM, assignee: "White Mage"),
            Kit.Entry("p1.raid", -5, Kit.SacredSoil, "Sacred Soil", job: Job.SCH, assignee: "Scholar"),
        ],
        new Assignee { Key = "White Mage", Job = Job.WHM },
        new Assignee { Key = "Scholar", Job = Job.SCH });

    private static MitPlan SlotPlan() => Kit.Plan(
        [
            Kit.Entry("p1.raid", -5, Kit.Temperance, "Temperance", job: null, slot: "H1", assignee: "H1"),
            Kit.Entry("p1.raid", -5, Kit.SacredSoil, "Sacred Soil", job: null, slot: "H2", assignee: "H2"),
        ],
        new Assignee { Key = "H1", Slot = "H1" },
        new Assignee { Key = "H2", Slot = "H2" });

    [Fact]
    public void ChoosingAColumnOnOneJobsRowLeavesTheOtherJobsRowAlone()
    {
        // Review finding: picking "White Mage" on the WHM row also set the SCH row, which then had no entries.
        var (store, _) = NewStore();
        var plan = HealerTabPlan();
        store.SetMapping(plan, Job.WHM, new PlayerMapping { AssigneeKey = "White Mage" });

        var whm = store.MappingFor(plan, Job.WHM);
        var sch = store.MappingFor(plan, Job.SCH);
        Assert.False(whm.IsSuggestion);
        Assert.Equal("White Mage", whm.AssigneeKey);
        Assert.True(sch.IsSuggestion);
        Assert.Equal("Scholar", sch.AssigneeKey);
        Assert.Single(PlanSelection.For(plan, new PlayerContext(Job.SCH, 90, sch.Slot, sch.AssigneeKey)));
    }

    [Fact]
    public void EachJobKeepsItsOwnSlotColumn()
    {
        var (store, _) = NewStore();
        var plan = SlotPlan();
        store.SetMapping(plan, Job.WHM, new PlayerMapping { AssigneeKey = "H1", Slot = "H1" });
        store.SetMapping(plan, Job.SCH, new PlayerMapping { AssigneeKey = "H2", Slot = "H2" });

        Assert.Equal("H1", store.MappingFor(plan, Job.WHM).AssigneeKey);
        Assert.Equal("H2", store.MappingFor(plan, Job.SCH).AssigneeKey);

        store.SetMapping(plan, Job.SCH, null);
        Assert.Equal("H1", store.MappingFor(plan, Job.WHM).AssigneeKey);
        Assert.True(store.MappingFor(plan, Job.SCH).IsSuggestion);
    }

    [Fact]
    public void AWholePlanChoiceFromAnOlderVersionCountsOnlyForJobsItFits()
    {
        var (store, config) = NewStore();
        var plan = HealerTabPlan();
        config.PlayerMappings[plan.Id] = new PlayerMapping { AssigneeKey = "White Mage" };

        Assert.Equal("White Mage", store.MappingFor(plan, Job.WHM).AssigneeKey);
        Assert.False(store.MappingFor(plan, Job.WHM).IsSuggestion);
        Assert.Equal("Scholar", store.MappingFor(plan, Job.SCH).AssigneeKey);
        Assert.True(store.MappingFor(plan, Job.SCH).IsSuggestion);
    }

    [Fact]
    public void SavingOneJobKeepsWhatAnOlderWholePlanChoiceGaveTheOtherJobs()
    {
        var (store, config) = NewStore();
        var plan = HealerTabPlan();
        config.PlayerMappings[plan.Id] = new PlayerMapping { AssigneeKey = "White Mage" };

        store.SetMapping(plan, Job.SCH, new PlayerMapping { AssigneeKey = "Scholar" });

        Assert.False(config.PlayerMappings.ContainsKey(plan.Id));
        Assert.Equal("Scholar", store.MappingFor(plan, Job.SCH).AssigneeKey);
        var whm = store.MappingFor(plan, Job.WHM);
        Assert.Equal("White Mage", whm.AssigneeKey);
        Assert.False(whm.IsSuggestion);
    }

    [Fact]
    public void MappingsAreSavedUnderPlanAndJob()
    {
        var (store, config) = NewStore();
        var plan = HealerTabPlan();
        store.SetMapping(plan, Job.SCH, new PlayerMapping { AssigneeKey = "Scholar" });

        Assert.True(config.PlayerMappings.ContainsKey(PlanStore.MappingKey(plan.Id, Job.SCH)));
        Assert.Equal($"{plan.Id}/SCH", PlanStore.MappingKey(plan.Id, Job.SCH));
    }
}
