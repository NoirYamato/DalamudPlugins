using MitCue.Game;
using MitCue.PluginTests.Support;

namespace MitCue.PluginTests;

/// <summary>
/// Own action uses arrive through up to three paths (own ActionEffect, pet ActionEffect, recast/charge change);
/// one use must count once, and two uses must count twice even when they are double-weaved.
/// </summary>
public class OwnUseDeduperTests
{
    private static int Accepted(params (uint Id, double Clock, OwnUseSource Source)[] detections)
    {
        var deduper = new OwnUseDeduper();
        return detections.Count(d => deduper.Accept(new OwnUse(d.Id, d.Clock, d.Source)));
    }

    [Fact]
    public void OneUseSeenByEveryPathCountsOnce()
    {
        // Recast change at the press, own ActionEffect from the server, the Seraph's Consolation later.
        Assert.Equal(1, Accepted(
            (Kit.Consolation, 100.00, OwnUseSource.Recast),
            (Kit.Consolation, 100.30, OwnUseSource.ActionEffect),
            (Kit.Consolation, 100.80, OwnUseSource.Pet)));
    }

    [Fact]
    public void DoubleWeavedChargesCountTwiceThroughEveryPath()
    {
        // Review finding: both Consolation charges about 0.65 s apart, every path firing for each.
        Assert.Equal(2, Accepted(
            (Kit.Consolation, 100.00, OwnUseSource.Recast),
            (Kit.Consolation, 100.30, OwnUseSource.ActionEffect),
            (Kit.Consolation, 100.65, OwnUseSource.Recast),
            (Kit.Consolation, 100.80, OwnUseSource.Pet),
            (Kit.Consolation, 100.95, OwnUseSource.ActionEffect),
            (Kit.Consolation, 101.45, OwnUseSource.Pet)));
    }

    [Fact]
    public void TwoChargeDropsCountTwice()
    {
        Assert.Equal(2, Accepted(
            (Kit.Consolation, 100.016, OwnUseSource.Recast),
            (Kit.Consolation, 100.716, OwnUseSource.Recast)));
    }

    [Fact]
    public void BenisonOnTwoTanksCountsTwice()
    {
        // Divine Benison on the MT, then the second charge on the OT one weave later.
        Assert.Equal(2, Accepted(
            (Kit.DivineBenison, 200.00, OwnUseSource.Recast),
            (Kit.DivineBenison, 200.15, OwnUseSource.ActionEffect),
            (Kit.DivineBenison, 200.70, OwnUseSource.Recast),
            (Kit.DivineBenison, 200.85, OwnUseSource.ActionEffect)));
    }

    [Fact]
    public void PetDetectionPairsWithACommandUpToFiveSecondsEarlier()
    {
        Assert.Equal(1, Accepted(
            (Kit.Consolation, 100.0, OwnUseSource.Recast),
            (Kit.Consolation, 104.5, OwnUseSource.Pet)));
        Assert.Equal(2, Accepted(
            (Kit.Consolation, 100.0, OwnUseSource.Recast),
            (Kit.Consolation, 105.5, OwnUseSource.Pet)));
    }

    [Fact]
    public void OtherPathsOutsideTheWindowAreNewUses()
    {
        Assert.Equal(2, Accepted(
            (Kit.Temperance, 100.0, OwnUseSource.Recast),
            (Kit.Temperance, 101.2, OwnUseSource.ActionEffect)));
    }

    [Fact]
    public void DifferentActionsNeverMerge()
    {
        Assert.Equal(2, Accepted(
            (Kit.Temperance, 100.0, OwnUseSource.ActionEffect),
            (Kit.DivineBenison, 100.1, OwnUseSource.Recast)));
    }

    [Fact]
    public void ClearForgetsEarlierUses()
    {
        var deduper = new OwnUseDeduper();
        Assert.True(deduper.Accept(new OwnUse(Kit.Temperance, 100.0, OwnUseSource.Recast)));
        deduper.Clear();
        Assert.True(deduper.Accept(new OwnUse(Kit.Temperance, 100.2, OwnUseSource.ActionEffect)));
    }
}
