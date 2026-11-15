using System.Numerics;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using static AnoMech.Tests.ActLogLines;
using static AnoMech.Tests.TeaTestPull;

namespace AnoMech.Tests;

// The first Hawk Blasters go off 128 s into the pull, north and south.
public class TeaP1LimitCutPullLogsTests
{
    private static readonly string[] FirstPair =
    [
        AbilityAt(128, Helper, P1ActionId.HawkBlaster, new Vector2(100f, 86f), 0f), AbilityAt(128, OtherHelper, P1ActionId.HawkBlaster, new Vector2(100f, 114f), 0f),
    ];

    [Test]
    public void ThePairsAreTimedFromTheFirstAndPlacedFromTheCentre()
    {
        var findings = Findings(new TeaP1LimitCutPullLogs(), Of(
        [
            HeadMarker(125.9, Player(1), 79), HeadMarker(125.9, Player(2), 80), HeadMarker(125.945, Player(3), 81), .. FirstPair,
            AbilityAt(130.2, Helper, P1ActionId.HawkBlaster, new Vector2(109.9f, 90.1f), 0f),
            AbilityAt(130.2, OtherHelper, P1ActionId.HawkBlaster, new Vector2(90.1f, 109.9f), 0f),
            AbilityAt(136.8, Helper, P1ActionId.HawkBlaster, Centre, 0f),
        ], players: 3));

        Assert.Multiple(() =>
        {
            Assert.That(findings.Named("The Hawk Blaster pairs").Measured, Does.StartWith("2.20, none"));
            Assert.That(findings.Named("The Hawk Blaster pairs").Verdict, Is.EqualTo(Verdict.Matches));
            Assert.That(findings.Named("The Hawk Blasters in the middle").Measured, Does.StartWith("8.80, none"));
            Assert.That(findings.Named("The numbers show").Measured, Does.StartWith("-2.10 s"));
            Assert.That(findings.Named("A Hawk Blaster's spot on a cardinal").Measured, Does.StartWith("14.00 y (n=2"));
            Assert.That(findings.Named("A Hawk Blaster's spot on a diagonal").Verdict, Is.EqualTo(Verdict.Matches));
            Assert.That(findings.Named("Each pair stands an eighth of a turn").Measured, Is.EqualTo("45.0 degrees (n=1)"));
            Assert.That(findings.Named("The first pair's spots").Measured, Does.StartWith("north and south"));
            Assert.That(findings.Named("The numbers' spacing").Details.Single(), Does.EndWith("1 Gunbreaker, 2 DarkKnight, 3 WhiteMage"));
            Assert.That(findings.Named("The numbers' spacing").Measured, Is.EqualTo("the first two 0.000 s (n=1) apart; each later one 0.045 s (n=1) on"));
            Assert.That(findings.Named("The numbers' spacing").Verdict, Is.EqualTo(Verdict.Matches));
        });
    }

    // A literal the scenario no longer holds: the finding names the lost line and says so, which
    // TeaPullLogReportTests.EveryFindingFindsItsLineInTheSource looks for.
    [Test]
    public void ASeriesSaysWhichOfItsLiteralsTheScenarioNoLongerHolds()
    {
        var series = new TeaP1LimitCutPullLogs().Series("Two pairs",
        [
            ("pair 1", SimSource.EventTime(TeaSource.LimitCut, "() => HawkBlasterPair(1));")),
            ("pair 9", SimSource.EventTime(TeaSource.LimitCut, "() => HawkBlasterPair(9));")),
        ], 8f);

        Assert.Multiple(() =>
        {
            Assert.That(series.Constant, Does.StartWith(TeaSource.LimitCut + ":").And.EndWith(", :?"));
            Assert.That(series.Sim, Is.EqualTo("2.20, not found in the scenario s"));
        });
    }

    private static readonly string[] HeStandsOnTheCentre = [LiquidStands(), Enemy(-10, Chaser, P1BNpcBaseId.CruiseChaser, new Vector2(103f, 100f), 0f, 2168926, 2168926)];

    // His cleave leaves the second seat alive, 5 y east of him, and names no throw; the seat then walks 4 y on east.
    [Test]
    public void AHitThatNamesNoThrowIsNotCountedAsOne()
    {
        var east = new Vector2(105f, 100f);
        var sword = Findings(new TeaP1LimitCutPullLogs(), Of(
        [
            .. FirstPair, Placed(134.5, Chaser, Centre, MathF.PI / 2),
            Hit(135.7, Chaser, P1ActionId.AlphaSword, Player(2), east, Centre, Raw(Math.PI / 2), sequence: 7),
            EffectResult(136.2, Player(2), 7, 30000, east), StatusList(136.6, Player(2), 30000, new Vector2(109f, 100f)),
        ], HeStandsOnTheCentre, players: 2)).Named("Alpha Sword's knockback");

        Assert.Multiple(() =>
        {
            Assert.That(sword.Measured, Is.EqualTo("no throw, under nothing and alive"));
            Assert.That(sword.Details, Has.Some.EndsWith("its hit names no throw, and it held nothing against one"));
        });
    }

    // He stands on the centre and charges east through the first seat, 17 y out: the charge leaves it alive, and it is
    // dead 0.9 s later with nothing else of the fight's on it.
    [Test]
    public void APlayerDeadSoonAfterAChargeWithNoOtherHitDiedOfTheThrow()
    {
        var east = new Vector2(117f, 100f);
        var rim = Findings(new TeaP1LimitCutPullLogs(), Of(
        [
            .. FirstPair, Placed(134.5, Chaser, Centre, MathF.PI / 2),
            Hit(135.7, Chaser, P1ActionId.AlphaSword, Player(2), new Vector2(105f, 100f), Centre, Raw(Math.PI / 2), sequence: 7),
            EffectResult(136.2, Player(2), 7, 30000, new Vector2(105f, 100f)),
            Hit(137.2, Chaser, P1ActionId.SuperBlasstyCharge, Player(1), east, Centre, Raw(Math.PI / 2), sequence: 8, knockbackRow: 3),
            EffectResult(137.7, Player(1), 8, 40000, east), UpdateHp(138.1, Player(1), 0, east),
        ], HeStandsOnTheCentre, players: 2))
            .Named("The rim: a player one of his hits throws past it");

        Assert.Multiple(() =>
        {
            Assert.That(rim.Measured, Does.StartWith("1 of 1 players").And.EndWith("the rim lay 3.00 y (n=1) on along the line from him"));
            Assert.That(rim.Verdict, Is.EqualTo(Verdict.Matches));
        });
    }

    // The same charge slides the seat to the rim, where it is alive two seconds on: a wall stopped it.
    [Test]
    public void APlayerThrownPastTheRimWhoLivesOnDiffers()
    {
        var east = new Vector2(117f, 100f);
        var rim = Findings(new TeaP1LimitCutPullLogs(), Of(
        [
            .. FirstPair, Placed(134.5, Chaser, Centre, MathF.PI / 2),
            Hit(135.7, Chaser, P1ActionId.AlphaSword, Player(2), new Vector2(105f, 100f), Centre, Raw(Math.PI / 2), sequence: 7),
            EffectResult(136.2, Player(2), 7, 30000, new Vector2(105f, 100f)),
            Hit(137.2, Chaser, P1ActionId.SuperBlasstyCharge, Player(1), east, Centre, Raw(Math.PI / 2), sequence: 8, knockbackRow: 3),
            EffectResult(137.7, Player(1), 8, 40000, east), UpdateHp(137.9, Player(1), 40000, new Vector2(120f, 100f)),
            UpdateHp(138.1, Player(1), 40000, new Vector2(120f, 100f)), UpdateHp(139.4, Player(1), 40000, new Vector2(120f, 100f)),
        ], HeStandsOnTheCentre, players: 2))
            .Named("The rim: a player one of his hits throws past it");

        Assert.Multiple(() =>
        {
            Assert.That(rim.Measured, Does.StartWith("0 of 1 players"));
            Assert.That(rim.Details.Single(), Does.EndWith("17.00 y from the centre, lived on"));
            Assert.That(rim.Verdict, Is.EqualTo(Verdict.Differs));
        });
    }

    // The charge names its row, and the seat is alive where it stood two seconds on: something held it, and it says
    // nothing of the rim.
    [Test]
    public void APlayerWhoseHitNamesAThrowButIsNotSeenToSlideIsLeftOut()
    {
        var east = new Vector2(117f, 100f);
        var rim = Findings(new TeaP1LimitCutPullLogs(), Of(
        [
            .. FirstPair, Placed(134.5, Chaser, Centre, MathF.PI / 2),
            Hit(135.7, Chaser, P1ActionId.AlphaSword, Player(2), new Vector2(105f, 100f), Centre, Raw(Math.PI / 2), sequence: 7),
            EffectResult(136.2, Player(2), 7, 30000, new Vector2(105f, 100f)),
            Hit(137.2, Chaser, P1ActionId.SuperBlasstyCharge, Player(1), east, Centre, Raw(Math.PI / 2), sequence: 8, knockbackRow: 3),
            EffectResult(137.7, Player(1), 8, 40000, east), UpdateHp(138.1, Player(1), 40000, east), UpdateHp(139.4, Player(1), 40000, east),
        ], HeStandsOnTheCentre, players: 2))
            .Named("The rim: a player one of his hits throws past it");

        Assert.Multiple(() =>
        {
            Assert.That(rim.Measured, Does.EndWith("1 whose hit named such a row lived on and were not seen to slide"));
            Assert.That(rim.Details.Single(), Does.EndWith("lived on, seen no farther than 0.00 y along the line from him"));
            Assert.That(rim.Verdict, Is.EqualTo(Verdict.NotInTheseLogs));
        });
    }

    // A charge that names no throw says nothing of the rim, though its target is dead a second later.
    [Test]
    public void ADeathAfterAHitThatNamesNoThrowIsNoneOfTheRims()
    {
        var east = new Vector2(117f, 100f);
        var rim = Findings(new TeaP1LimitCutPullLogs(), Of(
        [
            .. FirstPair, Placed(134.5, Chaser, Centre, MathF.PI / 2),
            Hit(135.7, Chaser, P1ActionId.AlphaSword, Player(2), new Vector2(105f, 100f), Centre, Raw(Math.PI / 2), sequence: 7),
            EffectResult(136.2, Player(2), 7, 30000, new Vector2(105f, 100f)),
            Hit(137.2, Chaser, P1ActionId.SuperBlasstyCharge, Player(1), east, Centre, Raw(Math.PI / 2), sequence: 8),
            EffectResult(137.7, Player(1), 8, 40000, east), UpdateHp(138.1, Player(1), 0, east),
        ], HeStandsOnTheCentre, players: 2))
            .Named("The rim: a player one of his hits throws past it");

        Assert.That(rim.Verdict, Is.EqualTo(Verdict.NotInTheseLogs));
    }
}
