using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P6WaveCannon1;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopP6Constants;

namespace AnoMech.Tests;

// One roll per run: the first exaflare's compass point (0 north, clockwise) and which way the others follow it, the
// lasers' two sets of four and the Wild Charge's target. The settings force the first two.
public class TopP6WaveCannon1StateTests
{
    private static readonly IEnumerable<int> Seeds = Enumerable.Range(1, 400);

    [Test]
    public void EveryPointAndBothTurnsAreRolled()
    {
        var rolled = Seeds.Select(seed => State(seed, new TopP6WaveCannon1StateOverrides()))
                          .Select(s => (s.FirstExaflare, s.Clockwise))
                          .ToHashSet();

        Assert.That(rolled, Is.EquivalentTo(Enumerable.Range(0, 8).SelectMany(point => new[] { (point, true), (point, false) })));
    }

    [Test]
    public void EveryoneTakesOneLaserAndAnyoneCanTakeTheWildCharge()
    {
        var states = Seeds.Select(seed => State(seed, new TopP6WaveCannon1StateOverrides())).ToList();

        Assert.Multiple(() =>
        {
            foreach (var state in states)
                Assert.That(state.ProteanOrder.List, Is.EquivalentTo(PerRole.All));
            Assert.That(states.Select(s => s.WildChargeTarget).Distinct(), Is.EquivalentTo(PerRole.All));
            Assert.That(states.Select(s => s.ProteanOrder[0]).Distinct(), Is.EquivalentTo(PerRole.All), "anyone can be in the first four");
        });
    }

    [Test]
    public void TheSettingsForceThePointAndTheTurn([Range(0, 7)] int point, [Values] bool clockwise)
    {
        var overrides = new TopP6WaveCannon1StateOverrides { FirstExaflare = point, Clockwise = clockwise };

        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds.Take(50))
            {
                var state = State(seed, overrides);
                Assert.That((state.FirstExaflare, state.Clockwise), Is.EqualTo((point, clockwise)), $"seed {seed}");
            }
        });
    }

    // Every roll is drawn whatever is pinned, so a pin leaves the others as the seed had them.
    [Test]
    public void PinningARollLeavesTheOthersAsTheSeedHadThem()
    {
        var pins = new Action<TopP6WaveCannon1StateOverrides>[]
        {
            o => o.FirstExaflare = 3,
            o => o.Clockwise = false,
            o => o.ProteanOrder = [MainTank, OffTank, RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps],
            o => o.WildChargeTarget = CasterDps,
        };
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds.Take(50))
            {
                var free = State(seed, new TopP6WaveCannon1StateOverrides());
                for (var pin = 0; pin < pins.Length; pin++)
                {
                    var overrides = new TopP6WaveCannon1StateOverrides();
                    pins[pin](overrides);
                    var pinned = State(seed, overrides);
                    if (pin != 0) Assert.That(pinned.FirstExaflare, Is.EqualTo(free.FirstExaflare), $"seed {seed}, pin {pin}");
                    if (pin != 1) Assert.That(pinned.Clockwise, Is.EqualTo(free.Clockwise), $"seed {seed}, pin {pin}");
                    if (pin != 2) Assert.That(pinned.ProteanOrder.List, Is.EqualTo(free.ProteanOrder.List), $"seed {seed}, pin {pin}");
                    if (pin != 3) Assert.That(pinned.WildChargeTarget, Is.EqualTo(free.WildChargeTarget), $"seed {seed}, pin {pin}");
                }
            }
        });
    }

    [Test]
    public void TheSameSeedRollsTheSameRun()
    {
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds.Take(50))
            {
                var a = State(seed, new TopP6WaveCannon1StateOverrides());
                var b = State(seed, new TopP6WaveCannon1StateOverrides());
                Assert.That((b.FirstExaflare, b.Clockwise, b.WildChargeTarget), Is.EqualTo((a.FirstExaflare, a.Clockwise, a.WildChargeTarget)));
                Assert.That(b.ProteanOrder.List, Is.EqualTo(a.ProteanOrder.List));
            }
        });
    }

    [Test]
    public void TheExaflaresStepAsP6DurationSaysUnlessPinned()
    {
        Assert.That(State(1, new TopP6WaveCannon1StateOverrides()).ExaflareStep, Is.EqualTo(P6Duration.ExaflareStep));
        Assert.That(State(1, new TopP6WaveCannon1StateOverrides { ExaflareStep = 1.0f }).ExaflareStep, Is.EqualTo(1.0f));
    }

    private static TopP6WaveCannon1State State(int seed, TopP6WaveCannon1StateOverrides overrides) => new(new Rng(seed), new SimParty(), overrides);
}
