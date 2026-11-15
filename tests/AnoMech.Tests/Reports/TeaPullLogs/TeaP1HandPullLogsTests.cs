using System.Numerics;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using static AnoMech.Tests.ActLogLines;
using static AnoMech.Tests.TeaTestPull;

namespace AnoMech.Tests;

// The Liquid stands 18 y east of the centre, where the Hand appears.
public class TeaP1HandPullLogsTests
{
    private static readonly Vector2 Far = new(118f, 100f), BesideTheLiquid = new(117f, 100f);

    [Test]
    public void AFormShownApartSlamsWithHandOfParting()
    {
        var findings = Findings(new TeaP1HandPullLogs(), Of(
        [
            Enemy(19.5, Hand, P1BNpcBaseId.LiquidHand, Centre, 0f, 1300000, LiquidMaxHp),
            Moved(29.7, Hand, Centre, 0f), ModelState(29.7, Hand, P1ModelState.HandPalm), Placed(34, Hand, BesideTheLiquid, 0f),
            ModelState(34.7, Hand, 0),
            Hit(34.8, Hand, P1ActionId.HandOfParting, Player(1), sourceAt: BesideTheLiquid, sequence: 4, aoe: true),
            EffectResult(35.3, Player(1), 4, 20000, Centre),
        ], [LiquidStands(x: Far.X, y: Far.Y)]));

        Assert.Multiple(() =>
        {
            Assert.That(findings.Named("The Hand's form as it glows").Measured, Does.StartWith("1 of 1 glows fit: state 20 shown with the bosses 18.00 to 18.00 y apart (n=1); state 19 never shown"));
            Assert.That(findings.Named("The Hand's form as it glows").Verdict, Is.EqualTo(Verdict.Matches));
            Assert.That(findings.Named("Which action each form slams with").Measured, Is.EqualTo("state 20 with Hand of Parting (18476)"));
            Assert.That(findings.Named("Which action each form slams with").Verdict, Is.EqualTo(Verdict.Matches));
            Assert.That(findings.Named("The slam of state 20").Measured, Is.EqualTo("1.00 y apart: killed 0 of 1. None killed, so where the line lies is not shown"));
            Assert.That(findings.Named("The slam of state 20").Verdict, Is.EqualTo(Verdict.Inconclusive));
            Assert.That(findings.Named("From the glow to the slam").Measured, Is.EqualTo("5.10 s (n=1)"));
        });
    }

    // One slam lands with the Hand beside the Liquid and kills nobody; in the next pull the Hand never leaves the
    // centre, 18 y away, and its slam kills.
    [Test]
    public void ASlamsDistanceIsHeldOnlyBySlamsOnBothSidesOfIt()
    {
        string[] Slam(Vector2 handAt, int hpLeft) =>
        [
            Enemy(19.5, Hand, P1BNpcBaseId.LiquidHand, Centre, 0f, 1300000, LiquidMaxHp),
            Moved(29.7, Hand, Centre, 0f), ModelState(29.7, Hand, P1ModelState.HandPalm), Placed(34, Hand, handAt, 0f),
            ModelState(34.7, Hand, 0),
            Hit(34.8, Hand, P1ActionId.HandOfParting, Player(1), sourceAt: handAt, sequence: 4, aoe: true),
            EffectResult(35.3, Player(1), 4, hpLeft, Centre),
        ];
        var slam = Findings(new TeaP1HandPullLogs(), Of(Slam(BesideTheLiquid, 20000), [LiquidStands(x: Far.X, y: Far.Y)]),
                            Of(Slam(Centre, 0), [LiquidStands(x: Far.X, y: Far.Y)])).Named("The slam of state 20");

        Assert.Multiple(() =>
        {
            Assert.That(slam.Measured, Is.EqualTo("1.00 y apart: killed 0 of 1; 18.00 y apart: killed 1 of 1. The line lies between 1.00 and 18.00 y"));
            Assert.That(slam.Verdict, Is.EqualTo(Verdict.Matches));
        });
    }

    // The bars stand 200,000 apart of 3,356,051, and the cast takes six tenths of its target's health.
    [Test]
    public void AHandOfPainThatTakesMostOfAPlayersHealthFailed()
    {
        var pain = Findings(new TeaP1HandPullLogs(), Of(
        [
            Enemy(19.5, Hand, P1BNpcBaseId.LiquidHand, Centre, 0f, 800000, LiquidMaxHp), StatusList(60, Liquid, 1000000, Far),
            StatusList(65, Player(1), 60000, Centre), Cast(66, Hand, P1ActionId.HandOfPain, 2.7f),
            Hit(69, Hand, P1ActionId.HandOfPain, Player(1), sequence: 5, aoe: true, damage: 60000), EffectResult(69.5, Player(1), 5, 0, Centre),
        ], [LiquidStands(x: Far.X, y: Far.Y)])).Named("Hand of Pain against the two bars");

        Assert.Multiple(() =>
        {
            Assert.That(pain.Measured, Does.Contain("1 took most of a player's health, with gaps from 5.96 % as they land"));
            Assert.That(pain.Verdict, Is.EqualTo(Verdict.NotInTheSim));
        });
    }

    // The second status comes from another player and is none of the cleave's.
    [Test]
    public void ACleaveLeavesTheStatusItsOwnEffectsName()
    {
        var leaves = Findings(new TeaP1HandPullLogs(), Of(
        [
            StatusList(9, Player(1), 40000, new Vector2(100f, 90f)),
            Hit(10.5, Liquid, P1ActionId.FluidSwing, Player(1), new Vector2(100f, 90f), new Vector2(100f, 85f), Raw(0), applies: 1025),
            Gains(10.6, 1025, 3.96f, Liquid, Player(1)), Gains(10.6, 1917, 30f, Player(4), Player(1)),
        ])).Named("What a cleave leaves");

        Assert.That((leaves.Measured, leaves.Verdict), Is.EqualTo(("Fluid Swing: Status (1025) x1", Verdict.Matches)));
    }

    // The Hand rests on the centre and is Provoked at 30.0. Its first move line that has left the centre has it 3 y on,
    // a third of a second's walk at 9 y/s.
    [TestCase(31.0, "0.67", true)]
    [TestCase(31.5, "1.17", false)]
    public void AProvokeTakesHoldAsTheHandSetsOff(double firstLineAt, string after, bool matches)
    {
        var hold = Findings(new TeaP1HandPullLogs(), Of(
        [
            Enemy(19.5, Hand, P1BNpcBaseId.LiquidHand, Centre, 0f, 1300000, LiquidMaxHp), Moved(25, Hand, Centre, 0f),
            Hit(30, Player(1), TeaLogIds.Provoke, Hand),
            Moved(firstLineAt, Hand, Centre + new Vector2(3f, 0f), 0f), Moved(firstLineAt + 0.3, Hand, Centre + new Vector2(5.7f, 0f), 0f),
        ], [LiquidStands(x: Far.X, y: Far.Y)])).Named("A Provoke taking hold");

        Assert.That((hold.Measured, hold.Verdict), Is.EqualTo(($"{after} s (n=1), median {after} s", matches ? Verdict.Matches : Verdict.Differs)));
    }

    [Test]
    public void AHandAlreadyWalkingAsItIsProvokedShowsNothing()
    {
        var hold = Findings(new TeaP1HandPullLogs(), Of(
        [
            Enemy(19.5, Hand, P1BNpcBaseId.LiquidHand, Centre, 0f, 1300000, LiquidMaxHp),
            Moved(29.5, Hand, Centre, 0f), Moved(29.8, Hand, Centre + new Vector2(2.7f, 0f), 0f),
            Hit(30, Player(1), TeaLogIds.Provoke, Hand),
            Moved(30.1, Hand, Centre + new Vector2(5.4f, 0f), 0f),
        ], [LiquidStands(x: Far.X, y: Far.Y)])).Named("A Provoke taking hold");

        Assert.That((hold.Measured, hold.Verdict), Is.EqualTo(("no Provoke on a Hand at rest", Verdict.NotInTheseLogs)));
    }
}
