using System.Numerics;
using AnoMech.Core.UserActions;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP3Constants;
using static AnoMech.Tests.ActLogLines;

namespace AnoMech.Tests;

// The first Wave Repeater's cast starts at 0 s. The void zone's object stands from 5.5 s to 24.5 s, and a Ninja stands 3 y
// from the centre from 23 s, its first sample, so from 22.6 s on.
public class TopP3IntermissionPullLogsTests
{
    private const uint Boss = 0x40000010, Zone = 0x40000030, Spreader = 0x40000041, Stacker = 0x40000042;
    private const uint Player1 = 0x10000001, Player2 = 0x10000002;

    [Test]
    public void APlayerWhoStoodInsideTheRadiusAndLivedBoundsWhenTheZoneStopsKilling()
    {
        var findings = Findings(VoidZone(diesAt: null));
        var stops = findings.Single(f => f.Name.StartsWith("The void zone stops killing", StringComparison.Ordinal));
        var goes = findings.Single(f => f.Name.StartsWith("The void zone's object goes", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(stops.Measured, Does.Contain("So a 6 y zone stops killing by 22.60 s; one that kills until its object goes is narrower than 3.00 y"));
            Assert.That(stops.Verdict, Is.EqualTo(Verdict.Matches));
            Assert.That(goes.Verdict, Is.EqualTo(Verdict.Inconclusive));
        });
    }

    [Test]
    public void APlayerWhoDiedSoonAfterStandingInsideBoundsNothing()
    {
        var stops = Findings(VoidZone(diesAt: 23.5)).Single(f => f.Name.StartsWith("The void zone stops killing", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(stops.Verdict, Is.EqualTo(Verdict.NotInTheseLogs));
            Assert.That(stops.Details, Is.EqualTo(new[] { "Network_test.log pull 1: Ninja stood 3.00 y out at 22.60 s and died 0.90 s later" }));
        });
    }

    // The Ninja takes its own spread and the Warrior's stack, which the Warrior shares with it.
    [Test]
    public void APlayerInTwoCannonsShowsWhatTwoDo()
    {
        var findings = Findings(
        [
            Gains(9.2, P3StatusId.SniperCannonFodder, 19f, ActorIds.None, Player1),
            Gains(9.2, P3StatusId.HighPoweredSniperCannonFodder, 19f, ActorIds.None, Player2),
            Hit(28.3, Spreader, ActionId.SniperCannon, Player1, sequence: 0x31, aoe: true),
            Hit(28.3, Stacker, ActionId.HighPoweredSniperCannon, Player1, sequence: 0x32, aoe: true),
            Hit(28.3, Stacker, ActionId.HighPoweredSniperCannon, Player2, sequence: 0x32, aoe: true),
            EffectResult(28.9, Player1, 0x31, 30000, Vector2.Zero), EffectResult(28.9, Player1, 0x32, 0, Vector2.Zero),
            EffectResult(28.9, Player2, 0x32, 60000, Vector2.Zero),
        ]);
        var twoCannons = findings.Single(f => f.Name.StartsWith("Two Sniper Cannons kill", StringComparison.Ordinal));
        var loneStack = findings.Single(f => f.Name.StartsWith("A High-powered Sniper Cannon", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That((twoCannons.Measured, twoCannons.Verdict),
                        Is.EqualTo(("1 of 1 players in two or more cannons died of them; 0 of 1 in one died of it", Verdict.Matches)));
            Assert.That(twoCannons.Details,
                        Is.EqualTo(new[] { "Network_test.log pull 1: a spread of 1 and a stack of 2 on Ninja: survived at 30000 HP, then killed" }));
            Assert.That((loneStack.Measured, loneStack.Verdict), Is.EqualTo(("no stack hit a lone living player. Of 1 stacks, 1 hit 2", Verdict.NotInTheseLogs)));
        });
    }

    private static string[] VoidZone(double? diesAt)
    {
        var lines = new List<string>
        {
            Memory(5.5, "Add", Zone, ("BNpcID", Hex(P3EObjId.VoidZone))), Memory(24.5, "Remove", Zone),
        };
        for (var s = 23.0; s <= 24.5; s += 0.25)
            if (diesAt is not { } died || s < died)
                lines.Add(StatusList(s, Player1, 50000, new Vector2(100f, 97f)));
        if (diesAt is { } death) lines.AddRange([EffectResult(death, Player1, 0x40, 0, new Vector2(100f, 97f)), Death(death + 2, Player1, Boss)]);
        return [.. lines];
    }

    private static List<LogFinding> Findings(string[] lines)
    {
        var pull = new ActPull
        {
            File = "Network_test.log", Number = 1, Start = Origin,
            Jobs = new() { [Player1] = (uint)JobId.Ninja, [Player2] = (uint)JobId.Warrior },
        };
        pull.Lines.AddRange(lines.Prepend(Cast(0, Boss, P3ActionId.WaveRepeater, 4.7f)).Select(l => ActLine.Parse(l)!));
        var measures = new TopP3IntermissionPullLogs();
        measures.Observe(new TopPull(pull));
        return measures.Findings().ToList();
    }
}
