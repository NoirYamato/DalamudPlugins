using MitCue.Core.Plans;
using MitCue.Core.Tests.Data;

namespace MitCue.Core.Tests.Plans;

/// <summary>
/// <see cref="RecastReplay"/>, the recast model <c>check</c> and the importer's recast fit share: what each planned press
/// finds (PlanCheckerTests cover the findings check makes of it).
/// </summary>
public class RecastReplayTests
{
    [Fact]
    public void APressWithoutACharge_FindsWhenTheNextOneIsReady_AndRestartsTheRecast()
    {
        var asylum = RepoFiles.Action("Asylum"); // recast 90 s, one charge
        var states = RecastReplay.Replay([new(0, asylum), new(80, asylum), new(100, asylum)], 80);

        Assert.Equal((true, double.NegativeInfinity), (states[0].HasCharge, states[0].ReadyAt));
        Assert.Equal((false, 90.0), (states[1].HasCharge, states[1].ReadyAt));
        Assert.Equal((false, 170.0), (states[2].HasCharge, states[2].ReadyAt)); // the press at 80 is assumed to happen
    }

    [Fact]
    public void APressThatDoesNotSpend_FindsAChargeOrNone_AndChangesNothing()
    {
        // The importer's optional calls: the one at 80 has no charge yet and takes none, so the one at 100 finds the charge
        // that came back at 90.
        var asylum = RepoFiles.Action("Asylum");
        var states = RecastReplay.Replay([new(0, asylum), new(80, asylum, Spends: false), new(100, asylum)], 80);

        Assert.Equal((false, 90.0), (states[1].HasCharge, states[1].ReadyAt));
        Assert.Equal((true, 90.0), (states[2].HasCharge, states[2].ReadyAt));
    }

    [Fact]
    public void Charges_AreTheSyncLevels_AndRechargeOneAtATime()
    {
        var benison = RepoFiles.Action("Divine Benison"); // recast 30 s, two charges from level 88
        RecastUse[] presses = [new(0, benison), new(10, benison), new(20, benison)];

        Assert.False(RecastReplay.Replay(presses, 80)[1].HasCharge); // one charge at 80
        var states = RecastReplay.Replay(presses, 90);
        Assert.True(states[1].HasCharge);

        // Both charges spent since they were full (SinceFull 0): the first comes back at 30.
        Assert.Equal((false, 30.0, 0), (states[2].HasCharge, states[2].ReadyAt, states[2].SinceFull));
    }

    [Fact]
    public void Chains_GroupActionsThatShareARecast()
    {
        var bloodwhetting = RepoFiles.Action("Bloodwhetting");
        var nascentFlash = RepoFiles.Action("Nascent Flash"); // the same recast group (25 s)
        var rampart = RepoFiles.Action("Rampart");
        var chains = RecastReplay.Chains(new[] { bloodwhetting, rampart, nascentFlash, rampart }, a => a).ToList();

        Assert.Equal([["Bloodwhetting", "Nascent Flash"], ["Rampart", "Rampart"]], chains.Select(c => c.Select(a => a.Name)));
    }

    [Theory]
    [InlineData("Asylum", 80, true)]
    [InlineData("Medica II", 80, false)]    // a GCD: limited by the GCD, not a recast
    [InlineData("Temperance", 70, false)]   // learned at 80
    [InlineData("Divine Benison", 80, true)]
    public void Tracked_IsOneActionTheJobCanUseWithARecast(string name, int level, bool tracked)
    {
        var action = RepoFiles.Action(name);
        var item = PlanItem.ForAction(action.Id, action.Name);

        Assert.Equal(tracked, RecastReplay.Tracked(item, RepoFiles.Actions, Job.WHM, level, new PlanCheckOptions().MinRecast) is not null);
        Assert.Null(RecastReplay.Tracked(PlanItem.AnyOf([item, PlanItem.ForAction(3569, "Asylum")]), RepoFiles.Actions, Job.WHM, level, 3)); // either/or
        Assert.Null(RecastReplay.Tracked(item, RepoFiles.Actions, Job.SCH, level, 3));                                                   // another job's
    }
}
