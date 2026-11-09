using System.Numerics;
using AnoMech.Core.UserActions;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP3Constants;
using static AnoMech.Tests.ActLogLines;

namespace AnoMech.Tests;

// A remote regression forms 6 y apart at 28 s and breaks at 30.5 s, where the server puts its two break helpers 9.2 y apart.
public class TopP3HelloWorldPullLogsTests
{
    private const uint Boss = 0x40000010, Helper1 = 0x40000021, Helper2 = 0x40000022, Player1 = 0x10000001, Player2 = 0x10000002;

    [Test]
    public void ABreakDistanceIsHowFarApartTheServerPutTheBreaksHelpers()
        => Assert.That(RemoteBreakDistance(broken: true).Measured, Does.StartWith("9.20 y (n=1)"));

    [Test]
    public void ARegressionThatRanOutGivesNoBreakDistance()
        => Assert.That(RemoteBreakDistance(broken: false).Measured, Does.StartWith("no samples"));

    private static LogFinding RemoteBreakDistance(bool broken)
    {
        var lines = new List<string>
        {
            Cast(0, Boss, P3ActionId.HelloWorld, 4.7f),
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
        var pull = new ActPull
        {
            File = "Network_test.log", Number = 1, Start = Origin,
            Jobs = new() { [Player1] = (uint)JobId.WhiteMage, [Player2] = (uint)JobId.Warrior },
        };
        pull.Lines.AddRange(lines.Select(l => ActLine.Parse(l)!));
        var measures = new TopP3HelloWorldPullLogs();
        measures.Observe(new TopPull(pull));
        return measures.Findings().Single(f => f.Name.StartsWith("Remote regression", StringComparison.Ordinal));
    }
}
