using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P6Gauntlet;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopP6Constants;

namespace AnoMech.Tests;

// One run's rolls, in the order the fight shows them: Cosmo Arrow 1's pattern; the first Unlimited Wave Cannon's first
// exaflare (0 north, clockwise) and the way the others follow it; Wave Cannon 1's two sets of four lasers and Wild
// Charge's target; Cosmo Arrow 2's pattern, its lasers and Wild Charge's target; the second Unlimited Wave Cannon; and
// Cosmo Meteor's spread sets, three flares and the unmarked stack on one of the five without a flare. The settings force
// the patterns, the exaflares and each seat's flare; headless runs pin the rest. Every roll is drawn whatever is forced,
// so forcing one leaves the others as the seed had them. The run plays the parts First to Last: the whole phase unless
// Start from picks a later first part, or a test pins both.
public class TopP6GauntletStateTests
{
    private static readonly IEnumerable<int> Seeds = Enumerable.Range(1, 400);

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

    private static TopP6GauntletState State(int seed, TopP6GauntletStateOverrides overrides) => new(new Rng(seed), new SimParty(), overrides);

    private static TopP6GauntletState State(int seed, Action<TopP6GauntletStateOverrides>? set = null)
    {
        var overrides = new TopP6GauntletStateOverrides();
        set?.Invoke(overrides);
        return State(seed, overrides);
    }

    // ---- Cosmo Arrow 1 and 2

    [Test]
    public void AutoRollsBothPatternsForEachCosmoArrow()
    {
        var states = Seeds.Select(seed => State(seed)).ToList();

        Assert.That(states.Select(s => s.Arrow1InFirst), Has.Some.True.And.Some.False);
        Assert.That(states.Select(s => s.Arrow2InFirst), Has.Some.True.And.Some.False);
        Assert.That(states.Select(s => s.Arrow1InFirst == s.Arrow2InFirst), Has.Some.True.And.Some.False, "rolled apart");
    }

    [TestCase(true)]
    [TestCase(false)]
    public void TheSettingsPinEachPattern(bool inFirst)
    {
        Assert.That(Seeds.Take(50).Select(seed => State(seed, o => o.Arrow1InFirst = inFirst).Arrow1InFirst), Has.All.EqualTo(inFirst));
        Assert.That(Seeds.Take(50).Select(seed => State(seed, o => o.Arrow2InFirst = inFirst).Arrow2InFirst), Has.All.EqualTo(inFirst));
    }

    // ---- The Unlimited Wave Cannons

    [Test]
    public void EveryPointAndBothTurnsAreRolledForEachWave()
    {
        var states = Seeds.Select(seed => State(seed)).ToList();
        var everyPattern = Enumerable.Range(0, 8).SelectMany(point => new[] { (point, true), (point, false) }).ToList();

        Assert.That(states.Select(s => (s.Exaflares1First, s.Exaflares1Clockwise)).ToHashSet(), Is.EquivalentTo(everyPattern), "wave 1");
        Assert.That(states.Select(s => (s.Exaflares2First, s.Exaflares2Clockwise)).ToHashSet(), Is.EquivalentTo(everyPattern), "wave 2");
        Assert.That(states.GroupBy(s => (s.Exaflares2First, s.Exaflares2Clockwise)).Select(g => g.Count()), Has.All.GreaterThan(10),
                    "every pattern of wave 2 comes up in 400 seeds");
    }

    [Test]
    public void TheSettingsForceEachWavesPointAndTurn([Range(0, 7)] int point, [Values] bool clockwise)
    {
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds.Take(50))
            {
                var first = State(seed, o => { o.Exaflares1First = point; o.Exaflares1Clockwise = clockwise; });
                var second = State(seed, o => { o.Exaflares2First = point; o.Exaflares2Clockwise = clockwise; });
                Assert.That((first.Exaflares1First, first.Exaflares1Clockwise), Is.EqualTo((point, clockwise)), $"wave 1, seed {seed}");
                Assert.That((second.Exaflares2First, second.Exaflares2Clockwise), Is.EqualTo((point, clockwise)), $"wave 2, seed {seed}");
            }
        });
    }

    [Test]
    public void TheExaflaresStepAsP6DurationSaysUnlessPinned()
    {
        var free = State(1);
        var pinned = State(1, o => { o.Exaflares1Step = 1.0f; o.Exaflares2Step = 1.0f; });

        Assert.That((free.Exaflares1Step, free.Exaflares2Step), Is.EqualTo((P6Duration.ExaflareStep, P6Duration.ExaflareStep)));
        Assert.That((pinned.Exaflares1Step, pinned.Exaflares2Step), Is.EqualTo((1.0f, 1.0f)));
    }

    // ---- The Wave Cannons

    [Test]
    public void EveryoneTakesOneLaserAndAnyoneCanTakeEachWildCharge()
    {
        var states = Seeds.Select(seed => State(seed)).ToList();

        Assert.Multiple(() =>
        {
            foreach (var state in states)
            {
                Assert.That(state.Lasers1.List, Is.EquivalentTo(PerRole.All));
                Assert.That(state.Lasers2.List, Is.EquivalentTo(PerRole.All));
            }
            Assert.That(states.Select(s => s.WildCharge1Target).Distinct(), Is.EquivalentTo(PerRole.All));
            Assert.That(states.Select(s => s.WildCharge2Target).Distinct(), Is.EquivalentTo(PerRole.All));
            Assert.That(states.Select(s => s.Lasers1[0]).Distinct(), Is.EquivalentTo(PerRole.All), "anyone can be in the first four");
            Assert.That(states.Select(s => s.Lasers2[0]).Distinct(), Is.EquivalentTo(PerRole.All), "anyone can be in the first four");
            Assert.That(states.Count(s => s.Lasers1.List.SequenceEqual(s.Lasers2.List)), Is.LessThan(5), "rolled apart");
        });
    }

    // ---- Cosmo Meteor

    [Test]
    public void EverySetHitsFourThenTheOtherFourAndTheStackAvoidsTheFlares()
    {
        var party = PartyWithPlayerIn(MainTank);
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds.Take(300))
            {
                var state = new TopP6GauntletState(new Rng(seed), party, new TopP6GauntletStateOverrides());
                var rolls = state.MeteorRolls;
                for (var set = 0; set < TopP6GauntletState.SpreadSets; set++)
                {
                    var first = rolls.SpreadWave(set, 0);
                    var second = rolls.SpreadWave(set, 1);
                    Assert.That(first, Has.Length.EqualTo(4).And.Unique, $"seed {seed} set {set + 1}");
                    Assert.That(first.Concat(second), Is.EquivalentTo(PerRole.All), $"seed {seed} set {set + 1}");
                }
                Assert.That(state.Flares.List, Has.Length.EqualTo(3).And.Unique, $"seed {seed}");
                Assert.That(state.HasFlare(state.StackTarget), Is.False, $"seed {seed}: the stack on {state.StackTarget}");
                Assert.That(rolls.Flares, Is.EqualTo(state.Flares.List), $"seed {seed}");
                Assert.That(rolls.StackTarget, Is.EqualTo(state.StackTarget), $"seed {seed}");
            }
        });
    }

    // UNVERIFIED in the game; here every player can be rolled for each part.
    [Test]
    public void EveryPlayerTakesEveryMeteorPartOverTheSeeds()
    {
        var party = PartyWithPlayerIn(MainTank);
        var states = Seeds.Take(300).Select(seed => new TopP6GauntletState(new Rng(seed), party, new TopP6GauntletStateOverrides())).ToList();

        Assert.Multiple(() =>
        {
            foreach (var role in PerRole.All)
            {
                Assert.That(states.Count(s => s.HasFlare(role)), Is.InRange(60, 170), $"{role} a flare (3 in 8 expected, 112 of 300)");
                Assert.That(states.Count(s => s.StackTarget == role), Is.InRange(20, 75), $"{role} the stack (1 in 8 expected, 38 of 300)");
                for (var set = 0; set < TopP6GauntletState.SpreadSets; set++)
                    Assert.That(states.Count(s => s.MeteorRolls.SpreadWave(set, 0).Contains(role)), Is.InRange(110, 190), $"{role} first in set {set + 1}");
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
                var overrides = new TopP6GauntletStateOverrides();
                overrides.Flare.Mine = mine;
                foreach (var seed in Seeds.Take(50))
                {
                    var state = new TopP6GauntletState(new Rng(seed), party, overrides);
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
            var overrides = new TopP6GauntletStateOverrides();
            overrides.Flare[MainTank] = true;
            overrides.Flare[PhysRangedDps] = true;
            foreach (var role in new[] { OffTank, RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB })
                overrides.Flare[role] = false;

            Assert.That(overrides.Validate().Problems, Is.Empty);
            Assert.Multiple(() =>
            {
                foreach (var seed in Seeds.Take(300))
                    Assert.That(State(seed, overrides).Flares.List, Is.EquivalentTo(new[] { MainTank, PhysRangedDps, CasterDps }), $"seed {seed}");
            });
        });

    // Four seats ask for a flare: the three earliest seats get one.
    [Test]
    public void HostingTooManyYesesResolveTowardTheEarlierSeats()
        => Hosting(() =>
        {
            var overrides = new TopP6GauntletStateOverrides();
            foreach (var role in new[] { OffTank, ShieldHealer, MeleeDpsB, CasterDps })
                overrides.Flare[role] = true;

            Assert.Multiple(() =>
            {
                foreach (var seed in Seeds.Take(300))
                    Assert.That(State(seed, overrides).Flares.List, Is.EquivalentTo(new[] { OffTank, ShieldHealer, MeleeDpsB }), $"seed {seed}");
            });
        });

    [TestCase(new[] { MainTank, OffTank, RegenHealer, ShieldHealer }, new PartyRole[0], "all want a flare, but only 3 players get it")]
    [TestCase(new PartyRole[0], new[] { MainTank, OffTank, RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB }, "all refuse a flare, but 3 players have to take it")]
    public void SeatRowsTheFightCannotMeetConflict(PartyRole[] wanted, PartyRole[] refused, string problem)
    {
        var overrides = new TopP6GauntletStateOverrides();
        foreach (var role in wanted) overrides.Flare[role] = true;
        foreach (var role in refused) overrides.Flare[role] = false;

        Hosting(() => Assert.That(overrides.Validate().Problems, Has.Some.Contains(problem)));
        Assert.That(overrides.Validate().Problems, Is.Empty, "solo");
    }

    [Test]
    public void ThreeYesesAndFiveNosDoNotConflict()
    {
        var overrides = new TopP6GauntletStateOverrides();
        foreach (var role in new[] { MainTank, RegenHealer, PhysRangedDps }) overrides.Flare[role] = true;
        foreach (var role in new[] { OffTank, ShieldHealer, MeleeDpsA, MeleeDpsB, CasterDps }) overrides.Flare[role] = false;

        Hosting(() => Assert.That(overrides.Validate().Problems, Is.Empty));
    }

    // The bots clear the Cosmo Meteor part with R1 forced a flare, so the stack goes south, and with it forced out.
    [Test]
    public void TheBotsClearEitherStackSide([Values(true, false)] bool physRangedFlare, [Values(11, 23, 37, 41)] int seed)
    {
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), 0, seed, new ScenarioRunOptions
        {
            PlayerRole = MeleeDpsA,
            Overrides = o =>
            {
                var overrides = (TopP6GauntletStateOverrides)o;
                TopP6GauntletRun.Only(overrides, TopP6GauntletPart.CosmoMeteor);
                overrides.Flare[PhysRangedDps] = physRangedFlare;
            },
        });

        Assert.That(run.Passed, run.ToString);
    }

    // ---- Every roll, drawn whatever is pinned

    private static readonly (string Name, Action<TopP6GauntletStateOverrides> Pin)[] Pins =
    [
        ("Cosmo Arrow 1", o => o.Arrow1InFirst = true),
        ("wave 1's point", o => o.Exaflares1First = 3),
        ("wave 1's turn", o => o.Exaflares1Clockwise = false),
        ("lasers 1", o => o.Lasers1 = [MainTank, OffTank, RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps]),
        ("Wild Charge 1", o => o.WildCharge1Target = CasterDps),
        ("Cosmo Arrow 2", o => o.Arrow2InFirst = false),
        ("lasers 2", o => o.Lasers2 = [CasterDps, PhysRangedDps, MeleeDpsB, MeleeDpsA, ShieldHealer, RegenHealer, OffTank, MainTank]),
        ("Wild Charge 2", o => o.WildCharge2Target = MeleeDpsA),
        ("wave 2's point", o => o.Exaflares2First = 6),
        ("wave 2's turn", o => o.Exaflares2Clockwise = true),
        ("the spreads", o => o.FirstSpreads = [[MainTank, OffTank, RegenHealer, ShieldHealer], [MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps]]),
        ("the flares", o => o.Flares = [PhysRangedDps, OffTank, MeleeDpsA]),
        ("the stack", o => o.StackTarget = RegenHealer),
        ("the parts", o => o.Parts = (TopP6GauntletPart.CosmoMeteor, TopP6GauntletPart.CosmoMeteor)),
        ("start from", o => o.StartFrom = TopP6GauntletPart.Exaflares2),
    ];

    private static readonly (string Name, Func<TopP6GauntletState, object> Read)[] Rolls =
    [
        ("Cosmo Arrow 1", s => s.Arrow1InFirst),
        ("wave 1's point", s => s.Exaflares1First),
        ("wave 1's turn", s => s.Exaflares1Clockwise),
        ("lasers 1", s => string.Join(",", s.Lasers1.List)),
        ("Wild Charge 1", s => s.WildCharge1Target),
        ("Cosmo Arrow 2", s => s.Arrow2InFirst),
        ("lasers 2", s => string.Join(",", s.Lasers2.List)),
        ("Wild Charge 2", s => s.WildCharge2Target),
        ("wave 2's point", s => s.Exaflares2First),
        ("wave 2's turn", s => s.Exaflares2Clockwise),
        ("the spreads", s => string.Join("|", s.FirstSpreads.Select(set => string.Join(",", set)))),
        ("the flares", s => string.Join(",", s.Flares.List)),
        ("the stack", s => s.StackTarget),
    ];

    // The stack is rolled among the five without a flare, so pinning the flares moves it; every other pin leaves every
    // other roll as the seed had it.
    [Test]
    public void PinningARollLeavesTheOthersAsTheSeedHadThem()
    {
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds.Take(100))
            {
                var free = State(seed);
                foreach (var (pinned, pin) in Pins)
                {
                    var state = State(seed, pin);
                    foreach (var (roll, read) in Rolls.Where(r => r.Name != pinned && !(pinned == "the flares" && r.Name == "the stack")))
                        Assert.That(read(state), Is.EqualTo(read(free)), $"seed {seed}: pinning {pinned} moved {roll}");
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
                var a = State(seed);
                var b = State(seed);
                foreach (var (roll, read) in Rolls)
                    Assert.That(read(b), Is.EqualTo(read(a)), $"seed {seed}: {roll}");
            }
        });
    }

    // ---- The parts

    [Test]
    public void TheWholePhaseByDefault()
        => Assert.That(State(1).Parts, Is.EqualTo(TopP6GauntletParts.Whole));

    [TestCase(TopP6GauntletPart.Exaflares1)]
    [TestCase(TopP6GauntletPart.CosmoMeteor)]
    [TestCase(TopP6GauntletPart.MagicNumber)]
    public void StartFromPlaysFromThatPartToTheEnd(TopP6GauntletPart part)
        => Assert.That(State(1, o => o.StartFrom = part).Parts, Is.EqualTo(new TopP6GauntletParts(part, TopP6GauntletPart.MagicNumber)));

    [Test]
    public void APinnedPartRangeWinsOverStartFrom()
    {
        var state = State(1, o =>
        {
            o.StartFrom = TopP6GauntletPart.CosmoMemory;
            o.Parts = (TopP6GauntletPart.CosmoArrow2, TopP6GauntletPart.Exaflares2);
        });

        Assert.That(state.Parts, Is.EqualTo(new TopP6GauntletParts(TopP6GauntletPart.CosmoArrow2, TopP6GauntletPart.Exaflares2)));
        Assert.That(state.Parts.Plays(TopP6GauntletPart.Exaflares1), Is.False);
        Assert.That(state.Parts.Plays(TopP6GauntletPart.CosmoArrow2), Is.True);
        Assert.That(state.Parts.Plays(TopP6GauntletPart.Exaflares2), Is.True);
        Assert.That(state.Parts.Plays(TopP6GauntletPart.CosmoMeteor), Is.False);
    }

    // Each part on its own clock from its start, 1.9 s before its first cast; a later first part starts at 0.
    [Test]
    public void EachPartStartsWhereTheGauntletsClockHasIt()
    {
        var whole = TopP6GauntletParts.Whole;
        var fromTheMeteor = new TopP6GauntletParts(TopP6GauntletPart.CosmoMeteor, TopP6GauntletPart.MagicNumber);

        Assert.That(Enum.GetValues<TopP6GauntletPart>().Select(whole.Start), Is.EqualTo(new[] { 0f, 48.6f, 90.9f, 124.9f, 164.8f, 199.9f }).Within(1e-3f));
        Assert.That(fromTheMeteor.Start(TopP6GauntletPart.CosmoMeteor), Is.EqualTo(0f));
        Assert.That(fromTheMeteor.Start(TopP6GauntletPart.MagicNumber), Is.EqualTo(35.1f).Within(1e-3f));
    }

    // ---- The plan

    [Test]
    public void ThePlanComesFromTheSeatsAndTheHumansRows()
    {
        var party = PartyWithPlayerIn(MainTank);
        var auto = new TopP6GauntletState(new Rng(1), party, new TopP6GauntletStateOverrides());
        var forced = new TopP6GauntletState(new Rng(1), party, new TopP6GauntletStateOverrides { TankLimitBreaks = TopP6TankLimitBreaks.MagicNumber2 });

        Assert.That(auto.LimitBreakPlan.DutiesOf(MainTank),
                    Is.EqualTo(new[] { TopP6LimitBreakDuty.CosmoMemory, TopP6LimitBreakDuty.MagicNumber1Tank }), "the warrior main tank");
        Assert.That(forced.LimitBreakPlan.DutiesOf(MainTank), Is.EqualTo(new[] { TopP6LimitBreakDuty.MagicNumber2Tank }));
        Assert.That(forced.LimitBreakPlan.DutiesOf(OffTank),
                    Is.EqualTo(new[] { TopP6LimitBreakDuty.CosmoMemory, TopP6LimitBreakDuty.MagicNumber1Tank }));
    }

    // ---- The settings

    // The lobby's summary and the settings sync carry what a player forces, never the headless pins.
    [Test]
    public void OnlyTheForcedSettingsAreShownAndSynced()
    {
        var overrides = new TopP6GauntletStateOverrides
        {
            StartFrom = TopP6GauntletPart.CosmoMeteor,
            Exaflares1First = 3,
            Exaflares2Clockwise = false,
            TankLimitBreaks = TopP6TankLimitBreaks.MagicNumber1,
            Exaflares1Step = 1.0f,
            Parts = (TopP6GauntletPart.Exaflares1, TopP6GauntletPart.Exaflares1),
            WildCharge1Target = CasterDps,
            PlayerPressesItsOwnLimitBreaks = true,
        };
        var synced = new TopP6GauntletStateOverrides();

        Assert.That(ScenarioSettingsSummary.Describe(overrides),
                    Is.EquivalentTo(new[] { "Start from: CosmoMeteor", "Exaflares1 first: 3", "Exaflares2 clockwise: No", "Tank limit breaks: MagicNumber1" }));
        Assert.That(ScenarioSettingsSync.Apply(synced, ScenarioSettingsSync.Serialize(overrides)!), Is.True);
        Assert.That((synced.StartFrom, synced.Exaflares1First, synced.Exaflares2Clockwise, synced.TankLimitBreaks),
                    Is.EqualTo(((TopP6GauntletPart?)TopP6GauntletPart.CosmoMeteor, (int?)3, (bool?)false, (TopP6TankLimitBreaks?)TopP6TankLimitBreaks.MagicNumber1)));
        Assert.That((synced.Exaflares1Step, synced.Parts, synced.WildCharge1Target, synced.PlayerPressesItsOwnLimitBreaks),
                    Is.EqualTo(((float?)null, ((TopP6GauntletPart, TopP6GauntletPart)?)null, (PartyRole?)null, false)));
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
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), 0, 1, new ScenarioRunOptions
        {
            PlayerRole = seat,
            StopAt = 0.1f,
            Overrides = o => TopP6GauntletRun.Only((TopP6GauntletStateOverrides)o, TopP6GauntletPart.CosmoMeteor),
            Probe = p => party ??= p.World.Party,
        });
        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(party!.PlayerRole, Is.EqualTo(seat));
        return party;
    }
}
