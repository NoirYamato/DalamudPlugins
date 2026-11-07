using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P6CosmoDive2;
using AnoMech.Scenarios.Top.P6WaveCannon1;
using static AnoMech.Scenarios.Top.TopConstants;

namespace AnoMech.Tests;

// Both Exaflares blocks play Unlimited Wave Cannon the same way: from the autos' spots out of the hitbox to read the
// arrows, into the middle for the first puddles, then round behind the exaflares. Under one seed and one roll every seat
// stands where it does in the other block, frame by frame, until Exaflares/Dive's party leaves for its hold (21.5).
public class TopP6LpduAiTests
{
    private const float ExaflaresDiveLeavesForItsHold = 21.5f;

    [TestCase(1, true, 1.1f)]
    [TestCase(6, false, 1.0f)]
    public void BothExaflaresBlocksPlayUnlimitedWaveCannonTheSameWay(int first, bool clockwise, float step)
    {
        var seed = Random.Shared.Next();
        var waveCannon1 = Frames(typeof(TopP6WaveCannon1Scenario), TopP6WaveCannon1LpduAiTests.LpduStrat, seed,
                                 o => TopP6WaveCannon1LpduAiTests.Pin((TopP6WaveCannon1StateOverrides)o, first, clockwise, step));
        var exaflaresDive = Frames(typeof(TopP6CosmoDive2Scenario), TopP6CosmoDive2LpduAiTests.LpduStrat, seed,
                                   o => TopP6CosmoDive2LpduAiTests.Pin((TopP6CosmoDive2StateOverrides)o, first, clockwise, step));

        Assert.That(exaflaresDive.Select(f => f.Time), Is.EqualTo(waveCannon1.Select(f => f.Time)));
        var widest = waveCannon1.Zip(exaflaresDive, (a, b) => (a.Time, Apart: PerRole.All.Max(r => Vector2.Distance(a.Seats[r], b.Seats[r]))))
                                .MaxBy(f => f.Apart);
        Assert.That(widest.Apart, Is.LessThan(0.01f), $"seed {seed}: a seat stood {widest.Apart:F2} y from its spot in the other block at {widest.Time:F2}");
    }

    // The strats time every move of the dodge from the first exaflare's first explosion; each block's goes off then.
    [TestCase(typeof(TopP6WaveCannon1Scenario))]
    [TestCase(typeof(TopP6CosmoDive2Scenario))]
    public void TheDodgeIsTimedFromTheBlocksFirstExplosion(Type scenario)
    {
        SimTimeline? timeline = null;
        var run = ScenarioRun.Execute(scenario, 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            StopAt = 14.5f,
            Probe = p => timeline ??= p.Fake.Timeline,
        });

        Assert.That(run.Passed, run.ToString);
        var firstExplosion = timeline!.Rows.First(r => r["kind"] == "ability" && r["id"] == $"{ActionId.WaveCannon_7BAD}");
        Assert.That(float.Parse(firstExplosion["t"], CultureInfo.InvariantCulture), Is.EqualTo(TopP6LpduAi.TheFirstExaflareGoesOff).Within(0.05f));
    }

    private static List<(float Time, Dictionary<PartyRole, Vector2> Seats)> Frames(Type scenario, int strat, int seed, Action<object> pin)
    {
        var frames = new List<(float Time, Dictionary<PartyRole, Vector2> Seats)>();
        var run = ScenarioRun.Execute(scenario, strat, seed, new ScenarioRunOptions
        {
            PlayerRole = PartyRole.MainTank,
            StopAt = ExaflaresDiveLeavesForItsHold,
            Overrides = pin,
            Probe = p =>
            {
                if (p.Time < ExaflaresDiveLeavesForItsHold)
                    frames.Add((p.Time, PerRole.All.ToDictionary(r => r, r => new Vector2(p.Member(r)!.Position.X, p.Member(r)!.Position.Z))));
            },
        });
        Assert.That(run.Passed, run.ToString);
        return frames;
    }
}
