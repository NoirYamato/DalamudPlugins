using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P6CosmoMeteor;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Tests.TopP6CosmoMeteorLpduAiTests;

namespace AnoMech.Tests;

// Each roll: per spread set, four players then the other four; three flares; the unmarked stack on one of the five
// without a flare. A seat's flare row forces it in or out, three in and five out at most.
public class TopP6CosmoMeteorStateTests
{
    private static readonly IEnumerable<int> Seeds = Enumerable.Range(1, 300);

    // Multiplayer fixtures leave their hosting session installed, which would make every seat real here too.
    private AnoMech.Multiplayer.MultiplayerManager? leftInstalled;

    [SetUp]
    public void Solo()
    {
        leftInstalled = Plugin.Multiplayer;
        DalamudServices.Install(nameof(Plugin.Multiplayer), null!);
    }

    [TearDown]
    public void PutBackWhatWasInstalled() => DalamudServices.Install(nameof(Plugin.Multiplayer), leftInstalled!);

    [Test]
    public void EverySetHitsFourThenTheOtherFourAndTheStackAvoidsTheFlares()
    {
        var party = PartyWithPlayerIn(MainTank);
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds)
            {
                var state = new TopP6CosmoMeteorState(new Rng(seed), party, new TopP6CosmoMeteorStateOverrides());
                for (var set = 0; set < TopP6CosmoMeteorState.SpreadSets; set++)
                {
                    var first = state.SpreadWave(set, 0);
                    var second = state.SpreadWave(set, 1);
                    Assert.That(first, Has.Length.EqualTo(4).And.Unique, $"seed {seed} set {set + 1}");
                    Assert.That(first.Concat(second), Is.EquivalentTo(PerRole.All), $"seed {seed} set {set + 1}");
                }
                Assert.That(state.Flares.List, Has.Length.EqualTo(3).And.Unique, $"seed {seed}");
                Assert.That(state.HasFlare(state.StackTarget), Is.False, $"seed {seed}: the stack on {state.StackTarget}");
            }
        });
    }

    // UNVERIFIED in the game; here every player can be rolled for each part.
    [Test]
    public void EveryPlayerTakesEveryPartOverTheSeeds()
    {
        var party = PartyWithPlayerIn(MainTank);
        var states = Seeds.Select(seed => new TopP6CosmoMeteorState(new Rng(seed), party, new TopP6CosmoMeteorStateOverrides())).ToList();

        Assert.Multiple(() =>
        {
            foreach (var role in PerRole.All)
            {
                Assert.That(states.Count(s => s.HasFlare(role)), Is.InRange(60, 170), $"{role} a flare (3 in 8 expected, 112 of 300)");
                Assert.That(states.Count(s => s.StackTarget == role), Is.InRange(20, 75), $"{role} the stack (1 in 8 expected, 38 of 300)");
                for (var set = 0; set < TopP6CosmoMeteorState.SpreadSets; set++)
                    Assert.That(states.Count(s => s.SpreadWave(set, 0).Contains(role)), Is.InRange(110, 190), $"{role} first in set {set + 1}");
            }
        });
    }

    [Test]
    public void SoloTheOwnFlareRowIsHonoured([Values(true, false)] bool mine)
    {
        Assert.Multiple(() =>
        {
            foreach (var seat in PerRole.All)
            {
                var party = PartyWithPlayerIn(seat);
                var overrides = new TopP6CosmoMeteorStateOverrides();
                overrides.Flare.Mine = mine;
                foreach (var seed in Seeds.Take(50))
                {
                    var state = new TopP6CosmoMeteorState(new Rng(seed), party, overrides);
                    Assert.That(state.HasFlare(seat), Is.EqualTo(mine), $"{seat}, seed {seed}");
                    Assert.That(state.Flares.List, Has.Length.EqualTo(3).And.Unique, $"{seat}, seed {seed}");
                }
            }
        });
    }

    // MT and R1 in, OT, H1, H2, M1 and M2 out: the third flare can only be R2.
    [Test]
    public void HostingTheSeatRowsForceFlaresInAndOut()
        => Hosting(() =>
        {
            var overrides = new TopP6CosmoMeteorStateOverrides();
            overrides.Flare[MainTank] = true;
            overrides.Flare[PhysRangedDps] = true;
            foreach (var role in new[] { OffTank, RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB })
                overrides.Flare[role] = false;

            Assert.That(overrides.Validate().Problems, Is.Empty);
            Assert.Multiple(() =>
            {
                foreach (var seed in Seeds)
                {
                    var state = new TopP6CosmoMeteorState(new Rng(seed), new SimParty(), overrides);
                    Assert.That(state.Flares.List, Is.EquivalentTo(new[] { MainTank, PhysRangedDps, CasterDps }), $"seed {seed}");
                }
            });
        });

    // Four seats ask for a flare: the three earliest seats get one.
    [Test]
    public void HostingTooManyYesesResolveTowardTheEarlierSeats()
        => Hosting(() =>
        {
            var overrides = new TopP6CosmoMeteorStateOverrides();
            foreach (var role in new[] { OffTank, ShieldHealer, MeleeDpsB, CasterDps })
                overrides.Flare[role] = true;

            Assert.Multiple(() =>
            {
                foreach (var seed in Seeds)
                {
                    var state = new TopP6CosmoMeteorState(new Rng(seed), new SimParty(), overrides);
                    Assert.That(state.Flares.List, Is.EquivalentTo(new[] { OffTank, ShieldHealer, MeleeDpsB }), $"seed {seed}");
                }
            });
        });

    // Pinning one roll draws the others as the seed had them.
    [Test]
    public void PinningTheFlaresLeavesTheSpreadsAsTheSeedHadThem()
    {
        var party = PartyWithPlayerIn(MainTank);
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds)
            {
                var auto = new TopP6CosmoMeteorState(new Rng(seed), party, new TopP6CosmoMeteorStateOverrides());
                var pinned = new TopP6CosmoMeteorState(new Rng(seed), party, new TopP6CosmoMeteorStateOverrides { Flares = [PhysRangedDps, OffTank, MeleeDpsA] });
                for (var set = 0; set < TopP6CosmoMeteorState.SpreadSets; set++)
                    Assert.That(pinned.SpreadWave(set, 0), Is.EqualTo(auto.SpreadWave(set, 0)), $"seed {seed} set {set + 1}");
                Assert.That(pinned.Flares.List, Is.EqualTo(new[] { PhysRangedDps, OffTank, MeleeDpsA }), $"seed {seed}");
                Assert.That(pinned.HasFlare(pinned.StackTarget), Is.False, $"seed {seed}");
            }
        });
    }

    [TestCase(new[] { MainTank, OffTank, RegenHealer, ShieldHealer }, new PartyRole[0], "all want a flare, but only 3 players get it")]
    [TestCase(new PartyRole[0], new[] { MainTank, OffTank, RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB }, "all refuse a flare, but 3 players have to take it")]
    public void SeatRowsTheFightCannotMeetConflict(PartyRole[] wanted, PartyRole[] refused, string problem)
    {
        var overrides = new TopP6CosmoMeteorStateOverrides();
        foreach (var role in wanted) overrides.Flare[role] = true;
        foreach (var role in refused) overrides.Flare[role] = false;

        Hosting(() => Assert.That(overrides.Validate().Problems, Has.Some.Contains(problem)));
        Assert.That(overrides.Validate().Problems, Is.Empty, "solo");
    }

    [Test]
    public void ThreeYesesAndFiveNosDoNotConflict()
    {
        var overrides = new TopP6CosmoMeteorStateOverrides();
        foreach (var role in new[] { MainTank, RegenHealer, PhysRangedDps }) overrides.Flare[role] = true;
        foreach (var role in new[] { OffTank, ShieldHealer, MeleeDpsA, MeleeDpsB, CasterDps }) overrides.Flare[role] = false;

        Hosting(() => Assert.That(overrides.Validate().Problems, Is.Empty));
    }

    // The bots clear the fight with R1 forced a flare, so the stack goes south, and with it forced out.
    [Test]
    public void TheBotsClearEitherStackSide([Values(true, false)] bool physRangedFlare, [Values(11, 23, 37, 41)] int seed)
    {
        var run = ScenarioRun.Execute(typeof(TopP6CosmoMeteorScenario), LpduStrat, seed, new ScenarioRunOptions
        {
            PlayerRole = MeleeDpsA,
            Overrides = o => ((TopP6CosmoMeteorStateOverrides)o).Flare[PhysRangedDps] = physRangedFlare,
        });

        Assert.That(run.Passed, run.ToString);
    }

    private static void Hosting(Action check)
    {
        PerRole.ForceSeats = true;
        try
        {
            check();
        }
        finally
        {
            PerRole.ForceSeats = false;
        }
    }

    private static SimParty PartyWithPlayerIn(PartyRole seat)
    {
        SimParty? party = null;
        var run = ScenarioRun.Execute(typeof(TopP6CosmoMeteorScenario), LpduStrat, 1, new ScenarioRunOptions
        {
            PlayerRole = seat,
            StopAt = 0.1f,
            Probe = p => party ??= p.World.Party,
        });
        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(party!.PlayerRole, Is.EqualTo(seat));
        return party;
    }
}
