using MitCue.Core.Plans;

namespace MitCue.Core.Tests.Plans;

/// <summary>Which entries are the player's (<see cref="PlanSelection"/>), with a column chosen.</summary>
public class PlanSelectionTests
{
    private static PlanEntry Entry(string? column, Job? job, string? slot) => new()
    {
        Id = "e",
        Mechanic = "p1.pantokrator",
        Who = new WhoSpec { Assignee = column, Job = job, Slot = slot },
        Do = [PlanItem.ForNote("call")],
    };

    [Theory]
    [InlineData("WAR/DRK WAR", Job.WAR, null, true)] // the chosen column
    [InlineData("MT", null, "MT", true)]            // another column, for the player's slot (job-less: LPDU's Reprisals)
    [InlineData("MT", Job.WAR, "MT", true)]         // another column, for the player's job and slot
    [InlineData("MT", Job.DRK, "MT", false)]        // another job in the player's slot
    [InlineData("OT", null, "OT", false)]           // another slot
    [InlineData("WAR/GNB WAR", Job.WAR, null, false)] // another column of the player's job: another tank pair
    [InlineData("CM LB Tank", Job.WAR, null, false)]
    [InlineData(null, null, "MT", true)]            // no column
    [InlineData("WAR/DRK P6 WAR", Job.WAR, null, true)]  // the player's composition in another tab (review round 5)
    [InlineData("WARDRK P6 WAR", Job.WAR, null, true)]   // the same, as the xlsx export names the tab
    [InlineData("WAR/DRK P6 DRK", Job.DRK, null, false)] // the co-tank's column of that tab
    [InlineData("DRK/GNB P6 WAR", Job.WAR, null, false)] // another composition's tab
    public void WithAColumnChosenTheCallsOfTheSlotStillApply(string? column, Job? job, string? slot, bool applies)
    {
        var me = new PlayerContext(Job.WAR, 90, "MT", "WAR/DRK WAR");
        Assert.Equal(applies, PlanSelection.Applies(Entry(column, job, slot), me));
    }

    [Fact]
    public void WithoutASlotNoOtherColumnApplies()
    {
        var me = new PlayerContext(Job.WAR, 90, null, "WAR/DRK WAR");
        Assert.False(PlanSelection.Applies(Entry("MT", null, "MT"), me));
        Assert.False(PlanSelection.Applies(Entry("MT", Job.WAR, "MT"), me));
        Assert.True(PlanSelection.Applies(Entry("WAR/DRK WAR", Job.WAR, null), me));
    }

    [Fact]
    public void TheJoblessCallsOfASlotOfAnotherRoleDoNotApply()
    {
        // "My usual party slot" is one setting for every job: a melee main's M1 does not give their Warrior the M1 calls.
        var me = new PlayerContext(Job.WAR, 90, "M1", "WAR/DRK WAR");
        Assert.False(PlanSelection.Applies(Entry("M1", null, "M1"), me));
        Assert.True(PlanSelection.Applies(Entry("M1", Job.WAR, "M1"), me)); // named for the Warrior in M1
    }

    [Theory]
    [InlineData("MT", Job.WAR, true)]
    [InlineData("T2", Job.GNB, true)]
    [InlineData("H2", Job.SCH, true)]
    [InlineData("M1", Job.SAM, true)]
    [InlineData("R1", Job.BRD, true)]
    [InlineData("D4", Job.PCT, true)]
    [InlineData("M1", Job.WAR, false)]
    [InlineData("H1", Job.DRK, false)]
    [InlineData("R2", Job.RPR, false)]
    [InlineData("H3", Job.WHM, false)]
    [InlineData(null, Job.WHM, false)]
    [InlineData("MT", Job.None, false)]
    public void ASlotFitsTheJobsOfItsRole(string? slot, Job job, bool fits) => Assert.Equal(fits, Slots.Fits(slot, job));
}
