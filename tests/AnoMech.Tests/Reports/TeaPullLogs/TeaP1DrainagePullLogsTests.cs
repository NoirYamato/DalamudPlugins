using System.Numerics;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using static AnoMech.Tests.ActLogLines;
using static AnoMech.Tests.TeaTestPull;

namespace AnoMech.Tests;

public class TeaP1DrainagePullLogsTests
{
    // The west tornado, the middle of a set that leaves the east open, tethers the first seat 7.2 s before its Drainage.
    [Test]
    public void ATetherShowsSevenSecondsBeforeItsDrainage()
    {
        var findings = Findings(new TeaP1DrainagePullLogs(), Of(
        [
            .. Tornadoes(19.6), StatusList(78, Player(1), 40000, new Vector2(95f, 100f)), StatusList(78, Player(2), 40000, new Vector2(105f, 100f)),
            Tether(78.9, RageWest, Player(1), P1TetherId.Drainage),
            Hit(86.1, RageWest, P1ActionId.Drainage, Player(1), sequence: 6), EffectResult(86.6, Player(1), 6, 25000, new Vector2(95f, 100f)),
        ], players: 2));

        Assert.Multiple(() =>
        {
            var shows = findings.Named("The Drainage tethers show");
            Assert.That((shows.Measured, shows.Verdict), Is.EqualTo(("7.20 s (n=1)", Verdict.Matches)));
            Assert.That(shows.Details.Single(), Does.Contain("the Middle tornado's tether (open east) starts on Gunbreaker at 78.90 s"));
            Assert.That(findings.Named("Whom the tethers start on").Measured, Is.EqualTo("1 of 1 tethers start on a tank"));
            Assert.That(findings.Named("Drainage hits its tether's holder").Measured, Does.StartWith("1 of 1 Drainages hit their tether's holder; 0 hit someone else too"));
        });
    }

    // The first seat is cleansed 4 s in; the second keeps Throttle its whole 10 s and dies.
    [Test]
    public void ThrottleIsTimedCleansedAndRunsOut()
    {
        var findings = Findings(new TeaP1DrainagePullLogs(), Of(
        [
            Gains(95.8, P1StatusId.Throttle, 10f, ActorIds.None, Player(1)), Gains(95.8, P1StatusId.Throttle, 10f, ActorIds.None, Player(2)),
            Hit(98.7, Player(3), P1PlayerActionId.Esuna, Player(1)), Loses(99.8, P1StatusId.Throttle, ActorIds.None, Player(1)),
            Loses(105.8, P1StatusId.Throttle, ActorIds.None, Player(2)), UpdateHp(105.8, Player(2), 0, Centre),
        ]));

        Assert.Multiple(() =>
        {
            Assert.That((findings.Named("Throttle's seconds").Measured, findings.Named("Throttle's seconds").Verdict),
                        Is.EqualTo(("10.00 s (n=2, 10.00 to 10.00, sd 0.00)", Verdict.Matches)));
            Assert.That(findings.Named("A cleanse takes Throttle off").Measured, Does.StartWith("1 of 1 Throttles lost early went within 1.5 s of an Esuna"));
            Assert.That((findings.Named("Throttle running out kills").Measured, findings.Named("Throttle running out kills").Verdict),
                        Is.EqualTo(("1 of 1 holders who kept it its whole time died", Verdict.Matches)));
        });
    }
}
