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

    [Test]
    public void WhatTheOpeningDoesIsMeasuredFromTheCombatFlag()
    {
        var swing = Findings(new TeaP1TimelinePullLogs(), Of([Hit(10.5, Liquid, P1ActionId.FluidSwing, Player(1))])).Named("Opening: the first Fluid Swing");

        Assert.Multiple(() =>
        {
            Assert.That(swing.Measured, Does.StartWith("10.50 s (n=1) from the combat flag"));
            Assert.That(swing.Verdict, Is.EqualTo(Verdict.NotInTheSim));
        });
    }

    [Test]
    public void APullJoinedLateGivesNoTimeFromTheFlag()
    {
        var late = Of([Hit(8.5, Liquid, P1ActionId.FluidSwing, Player(1)), Cast(12.6, Liquid, P1ActionId.Cascade, 3.7f)], [LiquidStands(LiquidMaxHp - 20000)]);

        var swing = Findings(new TeaP1TimelinePullLogs(), late).Named("Opening: the first Fluid Swing");

        Assert.That(swing.Measured, Is.EqualTo("no samples from the combat flag; -4.10 s (n=1) from Cascade's cast"));
    }
}
