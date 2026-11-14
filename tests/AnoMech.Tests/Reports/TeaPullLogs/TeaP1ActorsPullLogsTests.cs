using System.Numerics;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using static AnoMech.Tests.ActLogLines;
using static AnoMech.Tests.TeaTestPull;

namespace AnoMech.Tests;

public class TeaP1ActorsPullLogsTests
{
    [Test]
    public void TornadoesFifteenYalmsOutAreOnTheRing()
    {
        var findings = Findings(new TeaP1ActorsPullLogs(), Of(Tornadoes(19.6)));

        Assert.Multiple(() =>
        {
            Assert.That(findings.Named("The tornado ring's radius").Measured, Does.StartWith("15.00 y (n=3"));
            Assert.That(findings.Named("The tornado ring's radius").Verdict, Is.EqualTo(Verdict.Matches));
            Assert.That(findings.Named("The tornadoes' layout").Measured, Does.Contain("1 of 1 sets leave one quarter open: open east"));
        });
    }

    [Test]
    public void TheWaterIsTheObjectAddedOnATornadosSpot()
    {
        var water = Findings(new TeaP1ActorsPullLogs(), Of(
        [
            .. Tornadoes(19.6),
            Memory(19.6, "Add", 0x40000051, ("BNpcID", "1E9998"), ("PosX", "100.0000"), ("PosY", "85.0000")),
            Memory(19.6, "Add", 0x40000052, ("BNpcID", "1E9998"), ("PosX", "85.0000"), ("PosY", "100.0000")),
        ])).Named("A Liquid Rage's water");

        Assert.That((water.Measured, water.Verdict),
                    Is.EqualTo(("2 of 3 Liquid Rages have object 2005400 added on their spot as they appear", Verdict.Differs)));
    }

    [Test]
    public void TheLiquidsSpotBeforeThePullIsReadFromAPullLoggedFromItsStart()
    {
        var spot = Findings(new TeaP1ActorsPullLogs(), Of([]), Of([], [LiquidStands(LiquidMaxHp - 20000, 99.8f, 96f)])).Named("The Living Liquid before the pull");

        Assert.That(spot.Measured, Does.StartWith("(0.00, -15.00), facing"));
    }

    // A sample every tenth of a second: the first to read beyond the rim opens the count, and the 0 HP comes 0.3 s later.
    [Test]
    public void APlayerWhoGoesPastTheRimIsDeadAtOnce()
    {
        var rim = Findings(new TeaP1ActorsPullLogs(), Of(
        [
            StatusList(50.0, Player(1), 40000, new Vector2(100f, 81f)), StatusList(50.4, Player(1), 40000, new Vector2(100f, 80.2f)),
            StatusList(50.8, Player(1), 40000, new Vector2(100f, 79.4f)), StatusList(51.1, Player(1), 0, new Vector2(100f, 78.8f)),
        ])).Named("The rim");

        Assert.Multiple(() =>
        {
            Assert.That(rim.Measured, Does.StartWith("1 of 1 times a living player read beyond 20 y"));
            Assert.That(rim.Verdict, Is.EqualTo(Verdict.Matches));
        });
    }

    [Test]
    public void TheNextTrackComesWithJKickAndTheFirstIsSetAgainOnlyBeforeAPull()
    {
        var track = Findings(new TeaP1ActorsPullLogs(), Of(
        [
            Hit(150, Chaser, P1ActionId.JKick, Player(1)), Director(150, ActMusic.ChangeMusic, 723),
        ], [LiquidStands(), Director(-20, ActMusic.ChangeMusic, 706)])).Named("The phase's track");

        Assert.Multiple(() =>
        {
            Assert.That(track.Measured, Does.StartWith("2 changes over 1 pulls, 0 of them as a pull opens: track 706, track 723"));
            Assert.That(track.Details, Has.Some.EndsWith("track 723, with J Kick"));
            Assert.That(track.Verdict, Is.EqualTo(Verdict.Matches));
        });
    }
}
