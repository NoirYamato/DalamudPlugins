using static AnoMech.Scenarios.Tea.TeaP1Constants;
using static AnoMech.Tests.ActLogLines;
using static AnoMech.Tests.TeaTestPull;

namespace AnoMech.Tests;

public class TeaP1TimelinePullLogsTests
{
    // Protean 1's Hand of Pain is the fight's second, 0.2 s after the Liquid's telegraphed cast.
    [TestCase(60.2, true)]
    [TestCase(61.0, false)]
    public void APartsLiteralIsHeldAgainstTheTimeAfterItsAnchor(double painAt, bool matches)
    {
        var pain = Findings(new TeaP1TimelinePullLogs(), Of(
        [
            Cast(39, Hand, P1ActionId.HandOfPain, 2.7f), Cast(60, Liquid, P1ActionId.ProteanWaveFromTheLiquid, 2.7f),
            Cast(painAt, Hand, P1ActionId.HandOfPain, 2.7f),
        ])).Named("Protean 1: Hand of Pain's cast");

        Assert.That((pain.Measured, pain.Verdict), Is.EqualTo(($"{Spread.F(painAt - 60)} s (n=1)", matches ? Verdict.Matches : Verdict.Differs)));
    }

    // The pull plays its first Fluid Swing at 10.1.
    [TestCase(10.2, true)]
    [TestCase(10.5, false)]
    public void WhatTheOpeningDoesIsHeldAgainstThePullsLineFromTheCombatFlag(double swingAt, bool matches)
    {
        var swing = Findings(new TeaP1TimelinePullLogs(), Of([Hit(swingAt, Liquid, P1ActionId.FluidSwing, Player(1))])).Named("Opening: the first Fluid Swing");

        Assert.Multiple(() =>
        {
            Assert.That(swing.Measured, Does.StartWith($"{Spread.F(swingAt)} s (n=1) from the combat flag"));
            Assert.That(swing.Sim, Does.StartWith("10.10 s from the pull"));
            Assert.That(swing.Verdict, Is.EqualTo(matches ? Verdict.Matches : Verdict.Differs));
        });
    }

    // Cascade is cast at 14.2, 4.1 s after the first Fluid Swing.
    [TestCase(12.6, true)]
    [TestCase(13.2, false)]
    public void APullJoinedLateGivesNoTimeFromTheFlagAndIsHeldToCascadesCast(double cascadeAt, bool matches)
    {
        var late = Of([Hit(8.5, Liquid, P1ActionId.FluidSwing, Player(1)), Cast(cascadeAt, Liquid, P1ActionId.Cascade, 3.7f)], [LiquidStands(LiquidMaxHp - 20000)]);

        var swing = Findings(new TeaP1TimelinePullLogs(), late).Named("Opening: the first Fluid Swing");

        Assert.That(swing.Measured, Is.EqualTo($"no samples from the combat flag; {Spread.F(8.5 - cascadeAt)} s (n=1) from Cascade's cast"));
        Assert.That(swing.Verdict, Is.EqualTo(matches ? Verdict.Matches : Verdict.Differs));
    }

    [Test]
    public void WhatThePullLeavesOutIsOnlyMeasured()
    {
        var auto = Findings(new TeaP1TimelinePullLogs(), Of([Hit(3, Liquid, TeaLogIds.LiquidAutoAttack, Player(1))])).Named("Opening: the Living Liquid's first auto-attack");

        Assert.That((auto.Sim, auto.Verdict), Is.EqualTo(("not played", Verdict.NotInTheSim)));
    }
}
