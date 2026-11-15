using System.Numerics;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using static AnoMech.Tests.ActLogLines;
using static AnoMech.Tests.TeaTestPull;

namespace AnoMech.Tests;

public class TeaP1DollsPullLogsTests
{
    private const uint Doll1 = 0x40000061, Doll2 = 0x40000062, Orb1 = 0x40000071, Orb2 = 0x40000072;

    // The Liquid stands on the centre and the Hand 10 y south of it. The north tornado's Embolus appears 0.4 s after
    // Pressurize and goes at 2 y/s for where the Liquid stood, 15 y away, and on 7 y past it. Its first move line, 5.6 s
    // after Pressurize, has it 0.1 y out already: it set off 0.05 s before.
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
            .. Enumerable.Range(0, 12).Select(k => Moved(45.6 + k, Orb1, north + new Vector2(0f, 0.1f + 2 * k), 0f)),
        ], [LiquidStands(x: 100f, y: 100f)]));

        Assert.Multiple(() =>
        {
            Assert.That(findings.Named("Pressurize: which tornadoes cast it").Measured, Does.StartWith("1 of 1 come from two tornadoes; left out: East x1"));
            Assert.That(findings.Named("An Embolus appears on its tornado").Measured, Does.StartWith("0.40 s (n=2"));
            Assert.That(findings.Named("An Embolus sets off").Measured, Does.StartWith("5.55 s (n=1); after it appears: 5.15 s (n=1). Its first move line comes 5.60 s (n=1) "
                                                                                       + "after Pressurize with it already 0.10 y (n=1) out"));
            Assert.That(findings.Named("An Embolus sets off").Verdict, Is.EqualTo(Verdict.Matches));
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

    // The pull's line is at 33.4 + 5.1, and the status lasts 4.96 s.
    [TestCase(38.5, true)]
    [TestCase(39.4, false)]
    public void TheFirstExhaustIsHeldAgainstThePullsLineAfterTheDollsTurnTargetable(double exhaustAt, bool matches)
    {
        var exhaust = Findings(new TeaP1DollsPullLogs(), Of(
        [
            Enemy(32.6, Doll1, TeaLogIds.JagdDoll, new Vector2(102.68f, 106.47f), 0f, 43540, 43540), NameToggle(33.4, Doll1, true),
            Hit(exhaustAt, Doll1, TeaLogIds.Exhaust, Player(1)),
        ])).Named("Exhaust: when");

        Assert.That(exhaust.Sim, Does.StartWith("first 5.10 s after the dolls turn targetable, then every 10.64 s"));
        Assert.That(exhaust.Verdict, Is.EqualTo(matches ? Verdict.Matches : Verdict.Differs));
    }

    // The doll stands 3.3 y from the first seat, which has stood still for a second either side of its auto-attack; the
    // second seat is walking as it is hit.
    [Test]
    public void ADollsStandOffIsReadAsItsAutoAttackLandsOnAPlayerWhoStandsStill()
    {
        var at = new Vector2(110f, 100f);
        var standOff = Findings(new TeaP1DollsPullLogs(), Of(
        [
            Enemy(32.6, Doll1, TeaLogIds.JagdDoll, new Vector2(102.68f, 106.47f), 0f, 43540, 43540),
            Moved(36, Doll1, at + new Vector2(3.3f, 0f), 0f),
            StatusList(36.5, Player(1), 40000, at), StatusList(37.5, Player(1), 40000, at), StatusList(38.5, Player(1), 40000, at),
            Hit(37.5, Doll1, TeaLogIds.DollAutoAttack, Player(1)),
            StatusList(39.5, Player(2), 40000, at), StatusList(40.5, Player(2), 40000, at + new Vector2(0f, 3f)), StatusList(41.5, Player(2), 40000, at + new Vector2(0f, 6f)),
            Hit(40.5, Doll1, TeaLogIds.DollAutoAttack, Player(2)),
        ])).Named("A doll's stand-off");

        Assert.That(standOff.Measured, Does.StartWith("median 3.30 y, 3.30 y (n=1)"));
        Assert.That(standOff.Verdict, Is.EqualTo(Verdict.Matches));
    }

    // The Hand walks to 1.5 y from a doll three tenths of a second after it turned targetable, and on; the first player
    // acts on the doll at 35.0.
    [TestCase(false)]
    [TestCase(true)]
    public void ABossThatPassesADollNobodyHasActedOnShowsWhetherSuchADollIsFed(bool fedMeanwhile)
    {
        var spot = new Vector2(102.68f, 106.47f);
        string[] fed = fedMeanwhile ? [Tether(33.9, Doll1, Hand, TeaLogIds.FeedTether)] : [];
        var untouched = Findings(new TeaP1DollsPullLogs(), Of(
        [
            Enemy(19.5, Hand, P1BNpcBaseId.LiquidHand, Centre, 0f, 1300000, LiquidMaxHp),
            Enemy(32.6, Doll1, TeaLogIds.JagdDoll, spot, 0f, 43540, 43540), NameToggle(33.4, Doll1, true),
            Moved(33.7, Hand, spot + new Vector2(1.5f, 0f), 0f), Moved(34.0, Hand, spot + new Vector2(6f, 0f), 0f),
            .. fed,
            Hit(35.0, Player(1), 31, Doll1), Tether(35.6, Doll1, Player(1), TeaLogIds.DollTether),
        ], [LiquidStands()])).Named("A doll nobody has acted on yet");

        Assert.That(untouched.Measured, Does.StartWith($"1 of 1 dolls had a boss within 4.6 y before any player acted on them, the nearest 1.50 y; {(fedMeanwhile ? 1 : 0)} of them were fed. "
                                                       + "The latest such pass came 0.30 s after the doll turned targetable"));
        Assert.That(untouched.Verdict, Is.EqualTo(fedMeanwhile ? Verdict.Differs : Verdict.Matches));
    }

    // The feed tether goes out with the doll this far from the Liquid Hand. Dolls walk in across the line, so it is
    // held to the farthest feed: one that goes out from beyond it differs.
    [TestCase(4.5f, false)]
    [TestCase(4.0f, false)]
    [TestCase(4.9f, true)]
    public void TheFeedLineIsHeldToTheFarthestFeed(float apart, bool differs)
    {
        var spot = new Vector2(102.68f, 106.47f);
        var feeding = Findings(new TeaP1DollsPullLogs(), Of(
        [
            Enemy(19.5, Hand, P1BNpcBaseId.LiquidHand, Centre, 0f, 1300000, LiquidMaxHp),
            Enemy(32.6, Doll1, TeaLogIds.JagdDoll, spot, 0f, 43540, 43540), NameToggle(33.4, Doll1, true),
            Hit(34.0, Player(1), 31, Doll1), Tether(34.6, Doll1, Player(1), TeaLogIds.DollTether),
            Moved(39.0, Hand, spot + new Vector2(apart, 0f), 0f),
            Tether(40.0, Doll1, Hand, TeaLogIds.FeedTether),
        ], [LiquidStands()])).Named("Feeding a doll");

        Assert.That(feeding.Sim, Does.StartWith("a doll a player has hit is fed once it is within 4.6 y of a boss's centre"));
        Assert.That(feeding.Measured, Does.Contain($"the farthest {Spread.F(apart)} y"));
        Assert.That(feeding.Verdict, differs ? Is.EqualTo(Verdict.Differs) : Is.Not.EqualTo(Verdict.Differs));
    }

    // Twenty feeds show where the line lies: with none of them within a quarter yalm of it, it is drawn too far out.
    [TestCase(4.2f, true)]
    [TestCase(4.5f, false)]
    public void TwentyFeedsWithNoneNearTheFeedLineSayItLiesNearer(float farthest, bool differs)
    {
        var spot = new Vector2(102.68f, 106.47f);
        var dolls = Enumerable.Range(0, 20).Select(k => 0x40000080u + (uint)k).ToList();
        var feeding = Findings(new TeaP1DollsPullLogs(), Of(
        [
            Enemy(19.5, Hand, P1BNpcBaseId.LiquidHand, Centre, 0f, 1300000, LiquidMaxHp),
            .. dolls.SelectMany(doll => new[]
            {
                Enemy(32.6, doll, TeaLogIds.JagdDoll, spot, 0f, 43540, 43540), NameToggle(33.4, doll, true),
                Hit(34.0, Player(1), 31, doll), Tether(34.6, doll, Player(1), TeaLogIds.DollTether),
            }),
            Moved(39.0, Hand, spot + new Vector2(farthest, 0f), 0f),
            .. dolls.Select(doll => Tether(40.0, doll, Hand, TeaLogIds.FeedTether)),
        ], [LiquidStands()])).Named("Feeding a doll");

        Assert.That(feeding.Measured, Does.Contain("n=20").And.Contain($"the farthest {Spread.F(farthest)} y"));
        Assert.That(feeding.Verdict, differs ? Is.EqualTo(Verdict.Differs) : Is.Not.EqualTo(Verdict.Differs));
    }

    // The tether shows this long after the first player's action on the doll; the sim's is 0.6 s after the press.
    [TestCase(0.62, true)]
    [TestCase(0.9, false)]
    public void ADollsTetherIsHeldToTheDelayAfterItsFirstHit(double after, bool matches)
    {
        var tethers = Findings(new TeaP1DollsPullLogs(), Of(
        [
            Enemy(32.6, Doll1, TeaLogIds.JagdDoll, new Vector2(102.68f, 106.47f), 0f, 43540, 43540), NameToggle(33.4, Doll1, true),
            Hit(34.0, Player(1), 31, Doll1), Tether(34.0 + after, Doll1, Player(1), TeaLogIds.DollTether),
        ])).Named("Whom a doll tethers");

        Assert.That(tethers.Measured, Does.StartWith("1 of 1 tethers go to the first player whose action hit the doll")
                                          .And.Contain($"a median {Spread.F(after)} s after that action"));
        Assert.That(tethers.Verdict, Is.EqualTo(matches ? Verdict.Matches : Verdict.Differs));
    }

    // A boss that walks past a doll a player has acted on already says nothing of the rule.
    [Test]
    public void ABossThatPassesADollAfterItsFirstHitIsLeftOut()
    {
        var spot = new Vector2(102.68f, 106.47f);
        var untouched = Findings(new TeaP1DollsPullLogs(), Of(
        [
            Enemy(19.5, Hand, P1BNpcBaseId.LiquidHand, Centre, 0f, 1300000, LiquidMaxHp),
            Enemy(32.6, Doll1, TeaLogIds.JagdDoll, spot, 0f, 43540, 43540), NameToggle(33.4, Doll1, true),
            Hit(34.0, Player(1), 31, Doll1),
            Moved(36.0, Hand, spot + new Vector2(1.5f, 0f), 0f),
        ], [LiquidStands()])).Named("A doll nobody has acted on yet");

        Assert.That((untouched.Measured, untouched.Verdict),
                    Is.EqualTo(("no boss came within 4.6 y of any of 1 dolls before a player acted on it", Verdict.Inconclusive)));
    }
}
