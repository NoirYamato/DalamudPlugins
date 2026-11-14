using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea.P1LimitCut;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.P1LimitCut.TeaP1FirstBlasters;

namespace AnoMech.Tests;

// The run rolls the numbers and the first Hawk Blaster pair's axis. Solo, the player's own number row is honoured;
// with seats (headless runs, a host), each seat's.
public class TeaP1LimitCutStateTests
{
    private static readonly IEnumerable<int> Seeds = Enumerable.Range(1, 200);

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
    public void TheNumbersAreAPermutationAndEveryRoleGetsEveryNumber()
    {
        var seen = new HashSet<(PartyRole, int)>();
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds)
            {
                var state = Roll(seed);
                Assert.That(state.Numbers, Is.EquivalentTo(PerRole.All), $"seed {seed}");
                foreach (var role in PerRole.All)
                {
                    Assert.That(state.Numbers[NumberOf(state, role) - 1], Is.EqualTo(role), $"seed {seed}, {role}");
                    seen.Add((role, NumberOf(state, role)));
                }
            }
        });
        Assert.That(seen, Has.Count.EqualTo(64));
    }

    [Test]
    public void EveryAxisTurnsUpAboutAQuarterOfTheTime()
    {
        var rolled = Seeds.Select(seed => Roll(seed).FirstBlasters).GroupBy(axis => axis).ToDictionary(g => g.Key, g => g.Count());

        Assert.That(rolled.Keys, Is.EquivalentTo(Enum.GetValues<TeaP1FirstBlasters>()));
        Assert.That(rolled.Values, Is.All.InRange(30, 70));
    }

    [TestCase(WestEast, 270f)]
    [TestCase(NorthwestSoutheast, 315f)]
    [TestCase(NorthSouth, 0f)]
    [TestCase(NortheastSouthwest, 45f)]
    public void TheFirstBearingIsTheFirstPairsEndInTheWestToNorthEastHalf(TeaP1FirstBlasters axis, float bearing)
        => Assert.That(Roll(1, new TeaP1LimitCutStateOverrides { FirstBlasters = axis }).FirstBearing, Is.EqualTo(bearing));

    // Every centre is 14 y out; each pair is 45 degrees clockwise of the last,
    // and the fifth lands on the first's spots (x east, z south).
    [TestCase(WestEast, new float[] { -14, 0, -9.8995f, -9.8995f, 0, -14, 9.8995f, -9.8995f })]
    [TestCase(NorthwestSoutheast, new float[] { -9.8995f, -9.8995f, 0, -14, 9.8995f, -9.8995f, 14, 0 })]
    [TestCase(NorthSouth, new float[] { 0, -14, 9.8995f, -9.8995f, 14, 0, 9.8995f, 9.8995f })]
    [TestCase(NortheastSouthwest, new float[] { 9.8995f, -9.8995f, 14, 0, 9.8995f, 9.8995f, 0, 14 })]
    public void EachPairBlowsOppositeSpotsFortyFiveDegreesClockwiseOfTheLast(TeaP1FirstBlasters axis, float[] firstFour)
    {
        var state = Roll(1, new TeaP1LimitCutStateOverrides { FirstBlasters = axis });

        Assert.Multiple(() =>
        {
            for (var index = 0; index < 8; index++)
            {
                var expected = new Vector2(firstFour[index % 4 * 2], firstFour[index % 4 * 2 + 1]);
                var (a, b) = state.HawkBlasterPair(index);
                Assert.That(Vector2.Distance(new Vector2(a.X, a.Z), expected), Is.LessThan(0.001f), $"pair {index + 1}: {a}");
                Assert.That(Vector2.Distance(new Vector2(b.X, b.Z), -expected), Is.LessThan(0.001f), $"pair {index + 1}, opposite: {b}");
            }
        });
    }

    [Test]
    public void SoloTheOwnNumberIsHonoured()
    {
        Assert.Multiple(() =>
        {
            foreach (var seat in PerRole.All)
            {
                var party = PartyWithPlayerIn(seat);
                for (var number = 1; number <= 8; number++)
                {
                    var overrides = new TeaP1LimitCutStateOverrides();
                    overrides.Number.Mine = number;
                    // Seat rows are a host's: solo they are not read.
                    overrides.Number[seat] = number % 8 + 1;
                    foreach (var seed in Seeds.Take(20))
                    {
                        var state = new TeaP1LimitCutState(new Rng(seed), party, overrides);
                        Assert.That(NumberOf(state, seat), Is.EqualTo(number), $"{seat}, seed {seed}");
                        Assert.That(state.Numbers, Is.EquivalentTo(PerRole.All), $"{seat}, seed {seed}");
                    }
                }
            }
        });
    }

    [Test]
    public void WithSeatsEveryForcedNumberLandsOnItsSeat()
        => Hosting(() =>
        {
            var overrides = new TeaP1LimitCutStateOverrides();
            overrides.Number[MainTank] = 8;
            overrides.Number[RegenHealer] = 1;
            overrides.Number[CasterDps] = 4;
            overrides.Number.Mine = 5;

            Assert.That(overrides.Validate().Problems, Is.Empty);
            Assert.Multiple(() =>
            {
                foreach (var seed in Seeds)
                {
                    var state = Roll(seed, overrides);
                    Assert.That((NumberOf(state, MainTank), NumberOf(state, RegenHealer), NumberOf(state, CasterDps)), Is.EqualTo((8, 1, 4)), $"seed {seed}");
                    Assert.That(state.Numbers, Is.EquivalentTo(PerRole.All), $"seed {seed}");
                }
            });
        });

    // The earlier seat keeps the number; the other is left with whatever the roll gives it.
    [Test]
    public void TwoSeatsOnOneNumberConflictAndTheEarlierSeatKeepsIt()
    {
        var overrides = new TeaP1LimitCutStateOverrides();
        overrides.Number[OffTank] = 3;
        overrides.Number[MeleeDpsB] = 3;
        overrides.Number[CasterDps] = 9;

        Assert.That(overrides.Validate().Problems, Is.Empty, "solo");
        Hosting(() =>
        {
            Assert.That(overrides.Validate().Problems, Has.Count.EqualTo(1).And.Some.Contains("all want number 3, but only one player gets it."));
            Assert.Multiple(() =>
            {
                foreach (var seed in Seeds)
                {
                    var state = Roll(seed, overrides);
                    Assert.That(NumberOf(state, OffTank), Is.EqualTo(3), $"seed {seed}");
                    Assert.That(state.Numbers, Is.EquivalentTo(PerRole.All), $"seed {seed}");
                }
            });
        });
    }

    [Test]
    public void EightDifferentNumbersDoNotConflict()
    {
        var overrides = new TeaP1LimitCutStateOverrides();
        foreach (var role in PerRole.All)
            overrides.Number[role] = 8 - (int)role;

        Hosting(() =>
        {
            Assert.That(overrides.Validate().Problems, Is.Empty);
            Assert.That(Roll(1, overrides).Numbers, Is.EqualTo(Enumerable.Reverse(PerRole.All)));
        });
    }

    [Test]
    public void PinningOneRollLeavesTheOtherAsTheSeedHadIt()
    {
        Assert.Multiple(() =>
        {
            foreach (var seed in Seeds.Take(40))
            {
                var rolled = Roll(seed);
                var axisPinned = Roll(seed, new TeaP1LimitCutStateOverrides { FirstBlasters = NorthSouth });
                var numbersPinned = Roll(seed, new TeaP1LimitCutStateOverrides { Numbers = PerRole.All });

                Assert.That(axisPinned.Numbers, Is.EqualTo(rolled.Numbers), $"seed {seed}");
                Assert.That(axisPinned.FirstBlasters, Is.EqualTo(NorthSouth), $"seed {seed}");
                Assert.That(numbersPinned.FirstBlasters, Is.EqualTo(rolled.FirstBlasters), $"seed {seed}");
                Assert.That(numbersPinned.Numbers, Is.EqualTo(PerRole.All), $"seed {seed}");
            }
        });
    }

    [Test]
    public void CruiseChaserLandsThreeYalmsBehindUnlessARunPinsIt()
    {
        Assert.That(Roll(1).CruiseChaserLandsBehind, Is.EqualTo(3f));
        Assert.That(Roll(1, new TeaP1LimitCutStateOverrides { CruiseChaserLandsBehind = 4.5f }).CruiseChaserLandsBehind, Is.EqualTo(4.5f));
    }

    // Unlike the TOP scenarios that offer it.
    [Test]
    public void AutomarkersDefaultToOff()
    {
        var overrides = new TeaP1LimitCutStateOverrides();

        Assert.That(overrides.Automarkers, Is.EqualTo(Automarkers.Off));
        Assert.That(Roll(1, overrides).Automarkers, Is.EqualTo(Automarkers.Off));
        Assert.That(ScenarioSettingsSummary.Describe(overrides), Has.Member("Automarkers: Off"));
        Assert.That(Roll(1, new TeaP1LimitCutStateOverrides { Automarkers = Automarkers.On }).Automarkers, Is.EqualTo(Automarkers.On));
    }

    // ScenarioSettingsSync is also how a setup is kept across restarts.
    [Test]
    public void TheSettingsTravelAndTheTestPinsDoNot()
    {
        var host = new TeaP1LimitCutStateOverrides
        {
            FirstBlasters = NortheastSouthwest,
            Automarkers = Automarkers.On,
            Numbers = PerRole.All,
            CruiseChaserLandsBehind = 4.5f,
        };
        host.Number.Mine = 7;
        host.Number[ShieldHealer] = 2;
        var json = ScenarioSettingsSync.Serialize(host)!;
        var peer = new TeaP1LimitCutStateOverrides();

        Assert.That(ScenarioSettingsSync.Apply(peer, json), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(peer.FirstBlasters, Is.EqualTo(NortheastSouthwest));
            Assert.That(peer.Automarkers, Is.EqualTo(Automarkers.On));
            Assert.That(peer.Number.Mine, Is.EqualTo(7));
            Assert.That(peer.Number[ShieldHealer], Is.EqualTo(2));
            Assert.That(peer.Numbers, Is.Null);
            Assert.That(peer.CruiseChaserLandsBehind, Is.Null);
            Assert.That(json, Does.Not.Contain("\"Numbers\"").And.Not.Contain("CruiseChaserLandsBehind"));
        });
    }

    private static TeaP1LimitCutState Roll(int seed, TeaP1LimitCutStateOverrides? overrides = null)
        => new(new Rng(seed), new SimParty(), overrides ?? new TeaP1LimitCutStateOverrides());

    private static int NumberOf(TeaP1LimitCutState state, PartyRole role) => state.Numbers.ToList().IndexOf(role) + 1;

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
        var run = ScenarioRun.Execute(typeof(TeaP1LimitCutScenario), TeaP1LimitCutStage.NoStrat, 1, new ScenarioRunOptions
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
