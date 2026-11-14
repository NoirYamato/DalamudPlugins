using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea;
using AnoMech.Scenarios.Tea.P1LivingLiquid;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.P1LivingLiquid.TeaP1LivingLiquidPart;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Tests;

// The run rolls both tornado layouts, the Drainage tethers, Throttle's six and the Emboli. Solo, the player's own
// Throttle row is honoured; with seats (headless runs, a host), each seat's.
public class TeaP1LivingLiquidStateTests
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

    [Test]
    public void EveryLayoutTurnsUpAboutAnEighthOfTheTimeForBothSets()
    {
        var first = Seeds.Select(seed => Roll(seed).Tornadoes1.Open).GroupBy(open => open).ToDictionary(g => g.Key, g => g.Count());
        var second = Seeds.Select(seed => Roll(seed).Tornadoes2.Open).GroupBy(open => open).ToDictionary(g => g.Key, g => g.Count());

        Assert.That(first.Keys, Is.EquivalentTo(Enum.GetValues<TeaP1OpenQuarter>()));
        Assert.That(second.Keys, Is.EquivalentTo(Enum.GetValues<TeaP1OpenQuarter>()));
        Assert.That(first.Values.Concat(second.Values), Is.All.InRange(25, 75));
    }

    [Test]
    public void TheSecondLayoutMayRepeatTheFirstOrNot()
    {
        var repeats = Seeds.Count(seed => Roll(seed) is var state && state.Tornadoes1 == state.Tornadoes2);

        Assert.That(repeats, Is.InRange(20, 90));
    }

    [Test]
    public void TwoOfTheThreeTornadoesTetherTwoDifferentSeats()
    {
        var pairs = new HashSet<string>();
        var firstTargets = new HashSet<PartyRole>();
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds)
            {
                var drainage = Roll(seed).Drainage;
                Assert.That(drainage, Has.Count.EqualTo(2), $"seed {seed}");
                Assert.That(drainage[0].From, Is.LessThan(drainage[1].From), $"seed {seed}: west before middle before east");
                Assert.That(drainage[0].FirstTarget, Is.Not.EqualTo(drainage[1].FirstTarget), $"seed {seed}");
                pairs.Add($"{drainage[0].From}+{drainage[1].From}");
                firstTargets.Add(drainage[0].FirstTarget);
                firstTargets.Add(drainage[1].FirstTarget);
            }
        });
        Assert.That(pairs, Is.EquivalentTo(new[] { "West+Middle", "West+East", "Middle+East" }));
        Assert.That(firstTargets, Is.EquivalentTo(PerRole.All));
    }

    [Test]
    public void TheDrainageSettingStartsTheTethersOnTheTanksOrOnTheOthers()
    {
        var tankOrders = new HashSet<PartyRole>();
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds.Take(100))
            {
                var onTheTanks = Roll(seed, new TeaP1LivingLiquidStateOverrides { DrainageStartsOn = TeaP1DrainageStartsOn.TheTanks }).Drainage;
                var onOthers = Roll(seed, new TeaP1LivingLiquidStateOverrides { DrainageStartsOn = TeaP1DrainageStartsOn.Others }).Drainage;
                Assert.That(onTheTanks.Select(roll => roll.FirstTarget), Is.EquivalentTo(new[] { MainTank, OffTank }), $"seed {seed}");
                Assert.That(onOthers.Select(roll => roll.FirstTarget), Has.None.Matches<PartyRole>(role => role.IsTank()), $"seed {seed}");
                Assert.That(onOthers[0].FirstTarget, Is.Not.EqualTo(onOthers[1].FirstTarget), $"seed {seed}");
                Assert.That(onTheTanks.Select(roll => roll.From), Is.EqualTo(Roll(seed).Drainage.Select(roll => roll.From)), $"seed {seed}: the same tornadoes");
                tankOrders.Add(onTheTanks[0].FirstTarget);
            }
        });
        Assert.That(tankOrders, Is.EquivalentTo(new[] { MainTank, OffTank }), "either tank may start with either tether");
    }

    [Test]
    public void ThrottleLandsOnSixAndEverySeatGetsItSometimes()
    {
        var counts = PerRole.All.ToDictionary(role => role, _ => 0);
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds)
            {
                var state = Roll(seed);
                Assert.That(state.Throttled.List.Distinct().Count(), Is.EqualTo(6), $"seed {seed}");
                foreach (var role in PerRole.All)
                {
                    Assert.That(state.HasThrottle(role), Is.EqualTo(state.Throttled.List.Contains(role)), $"seed {seed}, {role}");
                    if (state.HasThrottle(role)) counts[role]++;
                }
            }
        });
        Assert.That(counts.Values, Is.All.InRange(250, 350));
    }

    [Test]
    public void SoloTheOwnThrottleRowIsHonoured()
    {
        Assert.Multiple(() =>
        {
            foreach (var seat in PerRole.All)
            {
                var party = PartyWithPlayerIn(seat);
                foreach (var wanted in new[] { true, false })
                {
                    var overrides = new TeaP1LivingLiquidStateOverrides();
                    overrides.Throttle.Mine = wanted;
                    // Seat rows are a host's: solo they are not read.
                    overrides.Throttle[seat] = !wanted;
                    foreach (var seed in Seeds.Take(30))
                        Assert.That(new TeaP1LivingLiquidState(new Rng(seed), party, overrides).HasThrottle(seat), Is.EqualTo(wanted), $"{seat}, seed {seed}");
                }
            }
        });
    }

    [Test]
    public void WithSeatsEachSeatsThrottleRowIsHonoured()
        => Hosting(() =>
        {
            var overrides = new TeaP1LivingLiquidStateOverrides();
            overrides.Throttle.Mine = false;
            overrides.Throttle[MainTank] = true;
            overrides.Throttle[ShieldHealer] = true;
            overrides.Throttle[MeleeDpsB] = false;
            overrides.Throttle[CasterDps] = false;

            Assert.Multiple(() =>
            {
                foreach (var seed in Seeds.Take(50))
                    Assert.That(Roll(seed, overrides).Throttled.List,
                                Is.EquivalentTo(new[] { MainTank, OffTank, RegenHealer, ShieldHealer, MeleeDpsA, PhysRangedDps }), $"seed {seed}");
            });
        });

    // The roll takes the first six that ask, in seat order, and still fills six when too many refuse.
    [Test]
    public void ASeventhYesAndAThirdNoKeepTheRollAtSix()
        => Hosting(() =>
        {
            var sevenYes = new TeaP1LivingLiquidStateOverrides();
            foreach (var role in PerRole.All.Take(7)) sevenYes.Throttle[role] = true;
            var threeNo = new TeaP1LivingLiquidStateOverrides();
            foreach (var role in PerRole.All.Take(3)) threeNo.Throttle[role] = false;

            Assert.Multiple(() =>
            {
                foreach (var seed in Seeds.Take(50))
                {
                    Assert.That(Roll(seed, sevenYes).Throttled.List, Is.EquivalentTo(PerRole.All.Take(6)), $"seed {seed}");
                    Assert.That(Roll(seed, threeNo).Throttled.List.Distinct().Count(), Is.EqualTo(6), $"seed {seed}");
                }
            });
        });

    [Test]
    public void TooManyYesOrNoIsAConflictOnlyWithSeats()
    {
        var sevenYes = new TeaP1LivingLiquidStateOverrides();
        foreach (var role in PerRole.All.Take(7)) sevenYes.Throttle[role] = true;
        var threeNo = new TeaP1LivingLiquidStateOverrides();
        foreach (var role in PerRole.All.Take(3)) threeNo.Throttle[role] = false;
        var sixAndTwo = new TeaP1LivingLiquidStateOverrides();
        foreach (var role in PerRole.All) sixAndTwo.Throttle[role] = (int)role < 6;

        Assert.That(sevenYes.Validate().Any, Is.False, "solo");
        Hosting(() => Assert.Multiple(() =>
        {
            Assert.That(sevenYes.Validate().Problems.Single(), Does.Contain("all want Throttle, but only 6 players get it"));
            Assert.That(threeNo.Validate().Problems.Single(), Does.Contain("all refuse Throttle, but 6 players have to take it"));
            Assert.That(sixAndTwo.Validate().Any, Is.False);
            Assert.That(new TeaP1LivingLiquidStateOverrides().Validate().Any, Is.False);
        }));
    }

    [Test]
    public void TwoOfTheThreeTornadoesSendAnEmbolusOneAtEachBoss()
    {
        var seen = new HashSet<string>();
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds)
            {
                var emboli = Roll(seed).Emboli;
                Assert.That(emboli, Has.Count.EqualTo(2), $"seed {seed}");
                Assert.That(emboli[0].From, Is.LessThan(emboli[1].From), $"seed {seed}");
                Assert.That(emboli.Select(roll => roll.AimsAt), Is.EquivalentTo(Enum.GetValues<TeaP1Boss>()), $"seed {seed}");
                seen.Add($"{emboli[0].From}+{emboli[1].From}, the first at the {emboli[0].AimsAt}");
            }
        });
        Assert.That(seen, Has.Count.EqualTo(6));
    }

    // Every roll is drawn whether or not it is forced.
    [Test]
    public void ForcingOneRollLeavesTheOthersAsTheSeedHadThem()
    {
        var forced = new (string What, Func<TeaP1LivingLiquidStateOverrides> Overrides, int[] Changes)[]
        {
            ("the first tornadoes", () => new() { Tornadoes1 = TeaP1OpenQuarter.Southwest }, [0]),
            ("the second tornadoes", () => new() { Tornadoes2 = TeaP1OpenQuarter.Southwest }, [3]),
            ("Drainage's first targets", () => new() { DrainageStartsOn = TeaP1DrainageStartsOn.Others }, [2]),
            ("the pinned Drainage", () => new() { Drainage = [new(TeaP1Tornado.West, CasterDps), new(TeaP1Tornado.East, MeleeDpsA)] }, [1, 2]),
            ("the pinned Throttle", () => new() { Throttled = PerRole.All.Skip(2).ToList() }, [4]),
            ("the pinned Emboli", () => new() { Emboli = [new(TeaP1Tornado.West, TeaP1Boss.LivingLiquid), new(TeaP1Tornado.Middle, TeaP1Boss.LiquidHand)] }, [5]),
            ("the start", () => new() { StartFrom = Cascade2 }, [6]),
        };

        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds.Take(40))
            {
                var plain = Describe(Roll(seed));
                foreach (var (what, overrides, changes) in forced)
                {
                    var with = Describe(Roll(seed, overrides()));
                    Assert.That(Enumerable.Range(0, plain.Length).Where(index => with[index] != plain[index]), Is.SubsetOf(changes), $"seed {seed}, forcing {what}");
                }
            }
        });
    }

    [Test]
    public void SoloTheOwnThrottleRowLeavesTheOtherRollsAlone()
    {
        var overrides = new TeaP1LivingLiquidStateOverrides();
        overrides.Throttle.Mine = true;

        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds.Take(40))
            {
                var plain = Describe(Roll(seed));
                var with = Describe(Roll(seed, overrides));
                Assert.That(Enumerable.Range(0, plain.Length).Where(index => with[index] != plain[index]), Is.SubsetOf(new[] { 4 }), $"seed {seed}");
            }
        });
    }

    [Test]
    public void ThePinsAreTakenAsGiven()
    {
        var overrides = new TeaP1LivingLiquidStateOverrides
        {
            Tornadoes1 = TeaP1OpenQuarter.East,
            Tornadoes2 = TeaP1OpenQuarter.Northwest,
            Drainage = [new(TeaP1Tornado.Middle, RegenHealer), new(TeaP1Tornado.East, OffTank)],
            Throttled = [MainTank, OffTank, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps],
            Emboli = [new(TeaP1Tornado.Middle, TeaP1Boss.LivingLiquid), new(TeaP1Tornado.East, TeaP1Boss.LiquidHand)],
            EmbolusSpeed = 4f,
            PlayerPressesItsOwnActions = true,
        };
        var state = Roll(3, overrides);

        Assert.Multiple(() =>
        {
            Assert.That(state.Tornadoes1.Open, Is.EqualTo(TeaP1OpenQuarter.East));
            Assert.That(state.Tornadoes2.Open, Is.EqualTo(TeaP1OpenQuarter.Northwest));
            Assert.That(state.Drainage, Is.EqualTo(overrides.Drainage));
            Assert.That(state.Throttled.List, Is.EqualTo(overrides.Throttled));
            Assert.That(state.Emboli, Is.EqualTo(overrides.Emboli));
            Assert.That(state.EmbolusSpeed, Is.EqualTo(4f));
            Assert.That(state.PlayerPressesItsOwnActions, Is.True);
            Assert.That(Roll(3).EmbolusSpeed, Is.EqualTo(P1Rules.EmbolusSpeed));
            Assert.That(Roll(3).PlayerPressesItsOwnActions, Is.False);
        });
    }

    // -- The tornadoes' frame --

    // x east, z south: the open quarter, then the west, middle and east tornadoes.
    [TestCase(TeaP1OpenQuarter.North, new float[] { -15f, 0, 0, 15f, 15f, 0 })]
    [TestCase(TeaP1OpenQuarter.Northeast, new float[] { -10.607f, -10.607f, -10.607f, 10.607f, 10.607f, 10.607f })]
    [TestCase(TeaP1OpenQuarter.East, new float[] { 0, -15f, -15f, 0, 0, 15f })]
    [TestCase(TeaP1OpenQuarter.Southeast, new float[] { 10.607f, -10.607f, -10.607f, -10.607f, -10.607f, 10.607f })]
    [TestCase(TeaP1OpenQuarter.South, new float[] { 15f, 0, 0, -15f, -15f, 0 })]
    [TestCase(TeaP1OpenQuarter.Southwest, new float[] { 10.607f, 10.607f, 10.607f, -10.607f, -10.607f, -10.607f })]
    [TestCase(TeaP1OpenQuarter.West, new float[] { 0, 15f, 15f, 0, 0, -15f })]
    [TestCase(TeaP1OpenQuarter.Northwest, new float[] { -10.607f, 10.607f, 10.607f, 10.607f, 10.607f, -10.607f })]
    public void TheThreeTornadoesStandAQuarterTurnApartLeavingTheOpenQuarter(TeaP1OpenQuarter open, float[] westMiddleEast)
    {
        var layout = new TeaP1TornadoLayout(open);

        Assert.Multiple(() =>
        {
            foreach (var tornado in Enum.GetValues<TeaP1Tornado>())
            {
                var expected = new Vector2(westMiddleEast[(int)tornado * 2], westMiddleEast[(int)tornado * 2 + 1]);
                var centre = layout.Centre(tornado);
                Assert.That(Vector2.Distance(new Vector2(centre.X, centre.Z), expected), Is.LessThan(0.01f), $"{tornado}: {centre}");
            }
            var middle = layout.Centre(TeaP1Tornado.Middle);
            var openSpot = layout.At(0f, P1Geometry.TornadoRingRadius);
            Assert.That(Vector3.Distance(openSpot, -middle), Is.LessThan(0.01f), "the open quarter is opposite the middle tornado");
        });
    }

    [Test]
    public void TheFrameTurnsRelativeSpotsIntoArenaOnesAndBack([Values] TeaP1OpenQuarter open)
    {
        var layout = new TeaP1TornadoLayout(open);
        var random = new Random(11);

        Assert.Multiple(() =>
        {
            Assert.That(Vector2.Distance(layout.ToArena(new Vector2(0f, -1f)), Flat(layout.At(0f, 1f))), Is.LessThan(0.001f), "relative north");
            Assert.That(Vector2.Distance(layout.ToArena(new Vector2(-15f, 0f)), Flat(layout.Centre(TeaP1Tornado.West))), Is.LessThan(0.001f), "relative west");
            Assert.That(Vector2.Distance(layout.ToArena(new Vector2(15f, 0f)), Flat(layout.Centre(TeaP1Tornado.East))), Is.LessThan(0.001f), "relative east");
            Assert.That(layout.Radians, Is.EqualTo((int)open * MathF.PI / 4f).Within(0.0001f));
            for (var i = 0; i < 50; i++)
            {
                var spot = new Vector2(random.NextSingle() * 40f - 20f, random.NextSingle() * 40f - 20f);
                Assert.That(Vector2.Distance(layout.ToRelative(layout.ToArena(spot)), spot), Is.LessThan(0.001f), $"{spot}");
                Assert.That(Vector2.Distance(layout.ToArena(layout.ToRelative(new Vector3(spot.X, 0f, spot.Y))), spot), Is.LessThan(0.001f), $"{spot}");
            }
            // Facing a bearing is looking at the spot on it.
            var looking = new AnoMech.Core.Game.Placement(Vector3.Zero, layout.Facing(135f)).MoveForward(5f).Position;
            Assert.That(Vector3.Distance(looking, layout.At(135f, 5f)), Is.LessThan(0.001f));
        });
    }

    // -- Parts --

    [Test]
    public void ARunPlaysBothPartsUnlessItStartsLater()
    {
        var whole = Roll(1).Parts;
        var later = Roll(1, new TeaP1LivingLiquidStateOverrides { StartFrom = Cascade2 }).Parts;

        Assert.Multiple(() =>
        {
            Assert.That(whole, Is.EqualTo(TeaP1LivingLiquidParts.Whole));
            Assert.That((whole.Plays(Protean1), whole.Plays(Cascade2)), Is.EqualTo((true, true)));
            Assert.That((whole.Start(Protean1), whole.Start(Cascade2)), Is.EqualTo((0f, 23.9f)).Within(0.001f));
            Assert.That((later.Plays(Protean1), later.Plays(Cascade2)), Is.EqualTo((false, true)));
            Assert.That(later.Start(Cascade2), Is.Zero);
            Assert.That(Roll(1, new TeaP1LivingLiquidStateOverrides { StartFrom = Protean1 }).Parts, Is.EqualTo(TeaP1LivingLiquidParts.Whole));
            Assert.That((TeaP1LivingLiquidParts.FightClock(Protean1), TeaP1LivingLiquidParts.FightClock(Cascade2)), Is.EqualTo((60.9f, 84.8f)));
        });
    }

    [Test]
    public void AHeadlessRunPinsBothEndsAndALastBeforeTheFirstIsTheFirst()
    {
        var first = Roll(1, new TeaP1LivingLiquidStateOverrides { Parts = (Protean1, Protean1) }).Parts;
        var clamped = Roll(1, new TeaP1LivingLiquidStateOverrides { Parts = (Cascade2, Protean1), StartFrom = Protean1 }).Parts;

        Assert.That((first.Plays(Protean1), first.Plays(Cascade2)), Is.EqualTo((true, false)));
        Assert.That(clamped, Is.EqualTo(new TeaP1LivingLiquidParts(Cascade2, Cascade2)));
    }

    // A kept setup holds a part as its number, and the pull is to come before these two.
    [Test]
    public void ThePartsKeepTheirNumbersWithZeroLeftForThePull()
        => Assert.That(((int)Protean1, (int)Cascade2), Is.EqualTo((1, 2)));

    // ScenarioSettingsSync is also how a setup is kept across restarts.
    [Test]
    public void TheSettingsTravelAndTheTestPinsDoNot()
    {
        var host = new TeaP1LivingLiquidStateOverrides
        {
            StartFrom = Cascade2,
            Tornadoes1 = TeaP1OpenQuarter.Southeast,
            Tornadoes2 = TeaP1OpenQuarter.West,
            DrainageStartsOn = TeaP1DrainageStartsOn.Others,
            Parts = (Protean1, Protean1),
            Drainage = [new(TeaP1Tornado.West, CasterDps), new(TeaP1Tornado.East, MeleeDpsA)],
            Throttled = PerRole.All.Take(6).ToList(),
            Emboli = [],
            EmbolusSpeed = 3f,
            PlayerPressesItsOwnActions = true,
        };
        host.Throttle.Mine = true;
        host.Throttle[ShieldHealer] = false;
        var json = ScenarioSettingsSync.Serialize(host)!;
        var peer = new TeaP1LivingLiquidStateOverrides();

        Assert.That(ScenarioSettingsSync.Apply(peer, json), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(peer.StartFrom, Is.EqualTo(Cascade2));
            Assert.That(peer.Tornadoes1, Is.EqualTo(TeaP1OpenQuarter.Southeast));
            Assert.That(peer.Tornadoes2, Is.EqualTo(TeaP1OpenQuarter.West));
            Assert.That(peer.DrainageStartsOn, Is.EqualTo(TeaP1DrainageStartsOn.Others));
            Assert.That(peer.Throttle.Mine, Is.True);
            Assert.That(peer.Throttle[ShieldHealer], Is.False);
            Assert.That(peer.Parts, Is.Null);
            Assert.That(peer.Drainage, Is.Null);
            Assert.That(peer.Throttled, Is.Null);
            Assert.That(peer.Emboli, Is.Null);
            Assert.That(peer.EmbolusSpeed, Is.Null);
            Assert.That(peer.PlayerPressesItsOwnActions, Is.False);
            Assert.That(json, Does.Not.Contain("\"Parts\"").And.Not.Contain("\"Drainage\"").And.Not.Contain("Throttled").And.Not.Contain("Emboli").And.Not.Contain("PlayerPresses"));
        });
    }

    private static TeaP1LivingLiquidState Roll(int seed, TeaP1LivingLiquidStateOverrides? overrides = null)
        => new(new Rng(seed), new SimParty(), overrides ?? new TeaP1LivingLiquidStateOverrides());

    // One string per roll, in the order they are drawn.
    private static string[] Describe(TeaP1LivingLiquidState state) =>
    [
        $"{state.Tornadoes1.Open}",
        string.Join(",", state.Drainage.Select(roll => roll.From)),
        string.Join(",", state.Drainage.Select(roll => roll.FirstTarget)),
        $"{state.Tornadoes2.Open}",
        string.Join(",", state.Throttled.List.Order()),
        string.Join(",", state.Emboli.Select(roll => $"{roll.From}>{roll.AimsAt}")),
        $"{state.Parts}",
    ];

    private static Vector2 Flat(Vector3 position) => new(position.X, position.Z);

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
        var run = ScenarioRun.Execute(typeof(TeaP1LivingLiquidScenario), TeaP1LivingLiquidStage.NoStrat, 1, new ScenarioRunOptions
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
