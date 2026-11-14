using System.Numerics;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using static AnoMech.Tests.ActLogLines;
using static AnoMech.Tests.TeaTestPull;

namespace AnoMech.Tests;

public class TeaP1DollsPullLogsTests
{
    private const uint Doll1 = 0x40000061, Doll2 = 0x40000062, Orb1 = 0x40000071, Orb2 = 0x40000072;

    // The Liquid stands on the centre and the Hand 10 y south of it. The north tornado's Embolus appears 0.4 s after
    // Pressurize, sets off 5.3 s later at 2 y/s for where the Liquid stood, 15 y away, and goes on 7 y past it.
    [Test]
    public void AnEmbolusWaitsThenGoesOnPastItsSpot()
    {
        var north = new Vector2(100f, 85f);
        var findings = Findings(new TeaP1DollsPullLogs(), Of(
        [
            Enemy(19.5, Hand, P1BNpcBaseId.LiquidHand, new Vector2(100f, 110f), 0f, 1300000, LiquidMaxHp), .. Tornadoes(19.6),
            Hit(40, RageNorth, P1ActionId.Pressurize, RageNorth), Hit(40, RageWest, P1ActionId.Pressurize, RageWest),
            Enemy(40.4, Orb1, P1BNpcBaseId.Embolus, north, 0f, 3853782, 3853782),
            Enemy(40.4, Orb2, P1BNpcBaseId.Embolus, new Vector2(85f, 100f), 0.98f, 3853782, 3853782),
            .. Enumerable.Range(0, 12).Select(k => Moved(45.7 + k, Orb1, north + new Vector2(0f, 0.1f + 2 * k), 0f)),
        ], [LiquidStands(x: 100f, y: 100f)]));

        Assert.Multiple(() =>
        {
            Assert.That(findings.Named("Pressurize: which tornadoes cast it").Measured, Does.StartWith("1 of 1 come from two tornadoes; left out: East x1"));
            Assert.That(findings.Named("An Embolus appears on its tornado").Measured, Does.StartWith("0.40 s (n=2"));
            Assert.That((findings.Named("An Embolus sets off").Measured, findings.Named("An Embolus sets off").Verdict), Is.EqualTo(("5.70 s (n=1); after it appears: 5.30 s (n=1)", Verdict.Matches)));
            Assert.That((findings.Named("An Embolus's speed").Measured, findings.Named("An Embolus's speed").Verdict), Is.EqualTo(("2.00 y/s (n=1)", Verdict.Matches)));
            Assert.That(findings.Named("Where an Embolus heads").Measured, Does.Contain("in 1 of 1 pairs one heads for each boss"));
            Assert.That(findings.Named("An Embolus goes on past the spot it left for").Measured, Does.StartWith("7.10 y (n=1)"));
            Assert.That(findings.Named("An Embolus goes on past the spot it left for").Verdict, Is.EqualTo(Verdict.Matches));
        });
    }

    // Two dolls' Exhausts reach the first seat a few hundredths of a second apart, and it explodes; the second seat
    // takes one.
    [Test]
    public void TwoDollsExhaustsAtOnceMakeAPlayerExplode()
    {
        var plasm = Findings(new TeaP1DollsPullLogs(), Of(
        [
            Enemy(32.6, Doll1, TeaLogIds.JagdDoll, new Vector2(102.68f, 106.47f), 0f, 43540, 43540),
            Enemy(32.6, Doll2, TeaLogIds.JagdDoll, new Vector2(97.32f, 93.53f), 0f, 43540, 43540),
            Hit(38.9, Doll1, TeaLogIds.Exhaust, Player(1)), Hit(38.94, Doll2, TeaLogIds.Exhaust, Player(1)), Hit(38.94, Doll2, TeaLogIds.Exhaust, Player(2)),
            Hit(38.94, Player(1), TeaLogIds.ExhaustFromAPlayer, Player(3), aoe: true),
            Gains(39.4, TeaLogIds.LuminousAetheroplasm, 4.96f, Doll1, Player(1)),
        ])).Named("Luminous Aetheroplasm");

        Assert.That(plasm.Measured, Does.Contain("1 of 1 players hit by two or more dolls' Exhausts at once exploded")
                                        .And.Contain("0 of 1 hit by one did"));
    }

    [Test]
    public void TheDollsSpotsAreReadFromTheCentreAndFromTheOpenQuarter()
    {
        var spots = Findings(new TeaP1DollsPullLogs(), Of(
        [
            .. Tornadoes(19.6), Enemy(32.6, Doll1, TeaLogIds.JagdDoll, new Vector2(100f, 107f), 0f, 43540, 43540),
        ])).Named("The Jagd Dolls' spawn spots");

        Assert.That(spots.Measured, Is.EqualTo("7.00 y (n=1) from the centre, on bearings 180.0; from the tornadoes' open quarter: 90.0"));
    }
}
