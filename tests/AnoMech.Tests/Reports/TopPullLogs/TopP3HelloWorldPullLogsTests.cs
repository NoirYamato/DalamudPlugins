using System.Numerics;
using AnoMech.Core.UserActions;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP3Constants;
using static AnoMech.Tests.ActLogLines;

namespace AnoMech.Tests;

// Hello World's cast starts at 0 s.
public class TopP3HelloWorldPullLogsTests
{
    private const uint Boss = 0x40000010, Helper1 = 0x40000021, Helper2 = 0x40000022, Player1 = 0x10000001, Player2 = 0x10000002;

    [Test]
    public void ABreakDistanceIsHowFarApartTheServerPutTheBreaksHelpers()
        => Assert.That(RemoteBreakDistance(broken: true).Measured, Does.StartWith("9.20 y (n=1)"));

    [Test]
    public void ARegressionThatRanOutGivesNoBreakDistance()
        => Assert.That(RemoteBreakDistance(broken: false).Measured, Does.StartWith("no samples"));

    [Test]
    public void TowersOnBothRingsWithEachColourSideBySideMatch()
    {
        var layout = TowerLayout([.. Towers(19.1, 0, 90, 180, 270), .. Towers(40.1, 45, 135, 225, 315)]);

        Assert.Multiple(() =>
        {
            Assert.That(layout.Measured, Does.StartWith("1 of 2 sets on the cardinals, 1 on the intercardinals, 0 elsewhere; "
                                                        + "each colour's towers side by side in 2"));
            Assert.That(layout.Verdict, Is.EqualTo(Verdict.Matches));
        });
    }

    [Test]
    public void TowersOnOneRingAloneAreInconclusive()
        => Assert.That(TowerLayout([.. Towers(19.1, 0, 90, 180, 270), .. Towers(40.1, 90, 180, 270, 0)]).Verdict,
                       Is.EqualTo(Verdict.Inconclusive));

    [Test]
    public void AColourWhoseTowersStandOppositeDiffers()
        => Assert.That(TowerLayout([.. Towers(19.1, 0, 180, 90, 270), .. Towers(40.1, 45, 135, 225, 315)]).Verdict,
                       Is.EqualTo(Verdict.Differs));

    [Test]
    public void ATowerDebuffLandingTheSimsDelayAfterItsTowerMatches()
    {
        var lands = Findings(
        [
            Hit(29.1, Helper1, P3ActionId.RedTower, Player1),
            Gains(29.1 + P3Duration.TowerDebuffDelay, P3StatusId.RedTowerDebuff, P3Duration.TowerDebuff, ActorIds.None, Player1),
        ]).Single(f => f.Name.StartsWith("Tower debuff lands", StringComparison.Ordinal));

        Assert.That((lands.Measured, lands.Verdict), Is.EqualTo(("0.62 s (n=1)", Verdict.Matches)));
    }

    private static LogFinding TowerLayout(string[] lines) => Findings(lines).Single(f => f.Name == "Tower layout");

    // A set's four towers 14 y out, each its own helper's cast: red on the first two bearings, blue on the other two.
    private static IEnumerable<string> Towers(double seconds, params int[] bearings)
    {
        for (var i = 0; i < bearings.Length; i++)
        {
            var helper = 0x40000030u + (uint)i;
            var action = i < 2 ? P3ActionId.RedTower : P3ActionId.BlueTower;
            var radians = bearings[i] * MathF.PI / 180f;
            yield return Cast(seconds, helper, action, 10f);
            yield return CastAt(seconds, helper, action, new Vector2(100f + 14f * MathF.Sin(radians), 100f - 14f * MathF.Cos(radians)), 0f);
        }
    }

    // A remote regression forms 6 y apart at 28 s and breaks at 30.5 s, where the server puts its two break helpers 9.2 y apart.
    private static LogFinding RemoteBreakDistance(bool broken)
    {
        var lines = new List<string>
        {
            StatusList(28, Player1, 1000, new Vector2(97f, 100f)), StatusList(28, Player2, 1000, new Vector2(103f, 100f)),
            Gains(28, P3StatusId.RemoteRegression, 10f, ActorIds.None, Player1), Gains(28, P3StatusId.RemoteRegression, 10f, ActorIds.None, Player2),
            StatusList(30.5, Player1, 1000, new Vector2(95.5f, 100f)), StatusList(30.5, Player2, 1000, new Vector2(104.5f, 100f)),
            Loses(broken ? 30.5 : 38, P3StatusId.RemoteRegression, ActorIds.None, Player1),
            Loses(broken ? 30.5 : 38, P3StatusId.RemoteRegression, ActorIds.None, Player2),
        };
        if (broken)
            lines.AddRange(
            [
                Placed(30.5, Helper1, new Vector2(95.4f, 100f), 0f), Placed(30.5, Helper2, new Vector2(104.6f, 100f), 0f),
                Hit(30.6, Helper1, ActionId.HwTetherBreak, Player1), Hit(30.6, Helper2, ActionId.HwTetherBreak, Player2),
            ]);
        return Findings([.. lines]).Single(f => f.Name.StartsWith("Remote regression", StringComparison.Ordinal));
    }

    private static List<LogFinding> Findings(string[] lines)
    {
        var pull = new ActPull
        {
            File = "Network_test.log", Number = 1, Start = Origin,
            Jobs = new() { [Player1] = (uint)JobId.WhiteMage, [Player2] = (uint)JobId.Warrior },
        };
        pull.Lines.AddRange(lines.Prepend(Cast(0, Boss, P3ActionId.HelloWorld, 4.7f)).Select(l => ActLine.Parse(l)!));
        var measures = new TopP3HelloWorldPullLogs();
        measures.Observe(new TopPull(pull));
        return measures.Findings().ToList();
    }
}
