using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P6CosmoDive2;
using static AnoMech.Tests.TopP6CosmoDive2LpduAiTests;

namespace AnoMech.Tests;

// The two rolls: the compass point the first exaflare starts on (0 north, then clockwise every 45°) and the way the
// other three follow it round. Each can be forced; the exaflares' step is no roll, but headless runs pin it.
public class TopP6CosmoDive2StateTests
{
    private static readonly IEnumerable<int> Seeds = Enumerable.Range(1, 400);

    [Test]
    public void EveryForcedPatternReachesTheState()
    {
        Assert.Multiple(() =>
        {
            foreach (var (first, clockwise) in Patterns)
            foreach (var seed in Seeds.Take(20))
            {
                var state = new TopP6CosmoDive2State(new Rng(seed), new TopP6CosmoDive2StateOverrides { FirstExaflare = first, Clockwise = clockwise });
                Assert.That((state.FirstExaflare, state.Clockwise), Is.EqualTo((first, clockwise)), $"seed {seed}");
            }
        });
    }

    [Test]
    public void LeftToTheRollEveryPointAndBothTurnsComeUp()
    {
        var states = Seeds.Select(seed => new TopP6CosmoDive2State(new Rng(seed), new TopP6CosmoDive2StateOverrides())).ToList();

        Assert.That(states.Select(s => s.FirstExaflare).Distinct().Order(), Is.EqualTo(Enumerable.Range(0, 8)));
        Assert.That(states.Select(s => s.Clockwise).Distinct().Order(), Is.EqualTo(new[] { false, true }));
        Assert.That(states.GroupBy(s => (s.FirstExaflare, s.Clockwise)).Select(g => g.Count()), Has.All.GreaterThan(10),
                    "every pattern comes up in 400 seeds");
    }

    [Test]
    public void ASeedRollsTheSamePatternEveryTime()
    {
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds.Take(50))
            {
                var a = new TopP6CosmoDive2State(new Rng(seed), new TopP6CosmoDive2StateOverrides());
                var b = new TopP6CosmoDive2State(new Rng(seed), new TopP6CosmoDive2StateOverrides());
                Assert.That((b.FirstExaflare, b.Clockwise), Is.EqualTo((a.FirstExaflare, a.Clockwise)), $"seed {seed}");
            }
        });
    }

    [Test]
    public void ForcingOneRollLeavesTheOtherAsTheSeedHadIt()
    {
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds.Take(100))
            {
                var rolled = new TopP6CosmoDive2State(new Rng(seed), new TopP6CosmoDive2StateOverrides());
                var pointForced = new TopP6CosmoDive2State(new Rng(seed), new TopP6CosmoDive2StateOverrides { FirstExaflare = (rolled.FirstExaflare + 3) % 8 });
                var turnForced = new TopP6CosmoDive2State(new Rng(seed), new TopP6CosmoDive2StateOverrides { Clockwise = !rolled.Clockwise });
                Assert.That(pointForced.Clockwise, Is.EqualTo(rolled.Clockwise), $"seed {seed}: forcing the point");
                Assert.That(turnForced.FirstExaflare, Is.EqualTo(rolled.FirstExaflare), $"seed {seed}: forcing the turn");
            }
        });
    }

    [Test]
    public void TheStepIsTheUnverifiedOneUnlessPinned()
    {
        var unpinned = new TopP6CosmoDive2State(new Rng(1), new TopP6CosmoDive2StateOverrides());
        var pinned = new TopP6CosmoDive2State(new Rng(1), new TopP6CosmoDive2StateOverrides { ExaflareStep = 1.0f });

        Assert.That(unpinned.ExaflareStep, Is.EqualTo(TopP6Constants.P6Duration.ExaflareStep));
        Assert.That(pinned.ExaflareStep, Is.EqualTo(1.0f));
    }

    // The lobby's summary and the settings sync carry what a player forces, never the headless pin.
    [Test]
    public void OnlyTheForcedRollsAreShownAndSynced()
    {
        var overrides = new TopP6CosmoDive2StateOverrides { FirstExaflare = 3, Clockwise = false, ExaflareStep = 1.0f };
        var synced = new TopP6CosmoDive2StateOverrides();

        Assert.That(ScenarioSettingsSummary.Describe(overrides), Is.EquivalentTo(new[] { "First exaflare: 3", "Clockwise: No" }));
        Assert.That(ScenarioSettingsSync.Apply(synced, ScenarioSettingsSync.Serialize(overrides)!), Is.True);
        Assert.That((synced.FirstExaflare, synced.Clockwise, synced.ExaflareStep), Is.EqualTo(((int?)3, (bool?)false, (float?)null)));
    }
}
