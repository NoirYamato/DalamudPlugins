using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P6Gauntlet;
using static AnoMech.Scenarios.Top.P6Gauntlet.TopP6GauntletPart;
using static AnoMech.Scenarios.Top.TopConstants;

namespace AnoMech.Tests;

// The Gauntlet's two Unlimited Wave Cannons play the same way: from the autos' spots out of the hitbox to read the
// arrows, into the middle for the first puddles, then round behind the exaflares. Each Exaflares part started on its own
// puts the party on those spots, so under one seed and one roll every seat stands where it does in the other part, frame
// by frame, until Exaflares 2's party leaves for its dive hold (21.5).
public class TopP6LpduAiTests
{
    private const float ExaflaresTwoLeavesForItsHold = 21.5f;

    [TestCase(1, true, 1.1f)]
    [TestCase(6, false, 1.0f)]
    public void BothWavesPlayUnlimitedWaveCannonTheSameWay(int first, bool clockwise, float step)
    {
        var seed = Random.Shared.Next();
        var wave1 = Frames(Exaflares1, seed, first, clockwise, step);
        var wave2 = Frames(Exaflares2, seed, first, clockwise, step);

        Assert.That(wave2.Select(f => f.Time), Is.EqualTo(wave1.Select(f => f.Time)));
        var widest = wave1.Zip(wave2, (a, b) => (a.Time, Apart: PerRole.All.Max(r => Vector2.Distance(a.Seats[r], b.Seats[r]))))
                          .MaxBy(f => f.Apart);
        Assert.That(widest.Apart, Is.LessThan(0.01f), $"seed {seed}: a seat stood {widest.Apart:F2} y from its spot in the other wave at {widest.Time:F2}");
    }

    // The strat times every move of the dodge from its wave's first explosion, as FirstExplosionAt has it; in one run
    // through both Exaflares parts each wave goes off then.
    [Test]
    public void EachWavesDodgeIsTimedFromItsOwnFirstExplosion()
    {
        SimTimeline? timeline = null;
        TopP6GauntletState? state = null;
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), 0, Random.Shared.Next(), new ScenarioRunOptions
        {
            StopAt = TopP6GauntletParts.GauntletStart(Exaflares2) - TopP6GauntletParts.GauntletStart(Exaflares1) + 14.5f,
            Overrides = o => ((TopP6GauntletStateOverrides)o).Parts = (Exaflares1, Exaflares2),
            Probe = p =>
            {
                timeline ??= p.Fake.Timeline;
                state ??= p.Game.Catalog.Scenarios.OfType<TopP6GauntletScenario>().Single().State;
            },
        });

        Assert.That(run.Passed, run.ToString);
        var firstExplosions = timeline!.Rows.Where(r => r["kind"] == "ability" && r["id"] == $"{ActionId.WaveCannon_7BAD}")
                                       .Select(r => float.Parse(r["t"], CultureInfo.InvariantCulture))
                                       .Order()
                                       .ToList();
        var waves = new[] { TopP6GauntletScenario.UnlimitedWaveCannon1(state!), TopP6GauntletScenario.UnlimitedWaveCannon2(state!) };
        Assert.That(waves.Select(w => w.FirstExplosionAt), Is.EqualTo(new[] { 13.9f, 90.2f }).Within(0.01f), "on the run's clock");
        foreach (var wave in waves)
            Assert.That(firstExplosions, Has.Some.EqualTo(wave.FirstExplosionAt).Within(0.05f), $"a first explosion at {wave.FirstExplosionAt:F2}");
    }

    private static List<(float Time, Dictionary<PartyRole, Vector2> Seats)> Frames(TopP6GauntletPart part, int seed, int first, bool clockwise, float step)
    {
        var frames = new List<(float Time, Dictionary<PartyRole, Vector2> Seats)>();
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), 0, seed, new ScenarioRunOptions
        {
            PlayerRole = PartyRole.MainTank,
            StopAt = ExaflaresTwoLeavesForItsHold,
            Overrides = o =>
            {
                var overrides = (TopP6GauntletStateOverrides)o;
                overrides.Parts = (part, part);
                overrides.Exaflares1First = overrides.Exaflares2First = first;
                overrides.Exaflares1Clockwise = overrides.Exaflares2Clockwise = clockwise;
                overrides.Exaflares1Step = overrides.Exaflares2Step = step;
            },
            Probe = p =>
            {
                if (p.Time < ExaflaresTwoLeavesForItsHold)
                    frames.Add((p.Time, PerRole.All.ToDictionary(r => r, r => new Vector2(p.Member(r)!.Position.X, p.Member(r)!.Position.Z))));
            },
        });
        Assert.That(run.Passed, run.ToString);
        return frames;
    }
}
