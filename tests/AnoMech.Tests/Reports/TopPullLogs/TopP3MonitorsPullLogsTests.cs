using System.Numerics;
using AnoMech.Core.UserActions;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP3Constants;
using static AnoMech.Tests.ActLogLines;

namespace AnoMech.Tests;

// Omega faces north and fires east; three players stand east of him and five west. Three players facing south (their
// left is east) hold left monitors. Nine circles land: three from Omega's monitor, two from each player's.
public class TopP3MonitorsPullLogsTests
{
    private const uint Boss = 0x40000010;

    [Test]
    public void NineCirclesFitOmegasMonitorHittingHisWholeSideNotTwo()
    {
        var measured = Who().Measured;

        Assert.Multiple(() =>
        {
            Assert.That(measured, Does.Contain("topped up from the other side: 0 of 1"));
            Assert.That(measured, Does.Contain("the players' two at random as above: 1 of 1"));
        });
    }

    private static LogFinding Who()
    {
        uint Player(int i) => 0x10000000u + (uint)i;
        var east = new[] { 1, 2, 3 };
        var lines = new List<string>
        {
            Cast(0, Boss, P3ActionId.OversampledWaveCannonEast, 9.7f), CastAt(0, Boss, P3ActionId.OversampledWaveCannonEast, new Vector2(100f, 100f), MathF.PI),
        };
        for (var i = 1; i <= 8; i++)
            lines.Add(StatusList(10, Player(i), 1000, east.Contains(i) ? new Vector2(110f, 90f + 5 * i) : new Vector2(90f, 70f + 5 * i)));
        foreach (var owner in new[] { 4, 5, 6 })
            lines.Add(Gains(0, StatusId.PlayerMonitorLeft, 9999f, ActorIds.None, Player(owner)));
        var targets = new[] { 1, 2, 3, 1, 2, 2, 3, 1, 3 };
        for (var c = 0; c < targets.Length; c++)
            lines.Add(Hit(10, 0x40000100u + (uint)c, ActionId.OversampledWaveCannonAoe, Player(targets[c])));

        var pull = new ActPull
        {
            File = "Network_test.log", Number = 1, Start = Origin,
            Jobs = Enumerable.Range(1, 8).ToDictionary(Player, _ => (uint)JobId.WhiteMage),
        };
        pull.Lines.AddRange(lines.Select(l => ActLine.Parse(l)!));
        var measures = new TopP3MonitorsPullLogs();
        measures.Observe(new TopPull(pull));
        return measures.Findings().Single(f => f.Name.StartsWith("Who the monitors hit", StringComparison.Ordinal));
    }
}
