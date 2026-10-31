using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P4BlueScreen;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// The raidplan's spots (x east, z south): every row on a 13.5 y ring at bearings 292.5 / 270 / 247.5 / 225
// (west: MT R1 H1 M1) and 67.5 / 90 / 112.5 / 135 (east: OT R2 H2 M2). Stacks 1 and 3 stand 13 y out,
// 22.5 degrees either side of south, stack 2 11 y out and 20 degrees; set 2 steps straight in to 11 y after
// its beams, set 3 to 10.5 y after ring 6. When both markers of a set are on one side, the southernmost of
// them swaps sides with the other side's melee for that set only.
//
//   8.0 / 17.9 / 28.0  aims; beams 0.6 s later. 13.0 / 22.9 / 33.0 repeat snapshots; 13.6 / 23.4 / 33.5 stacks.
//   Rings: 15.7 (r < 6), 18.1 (6-12), 20.1 (12-18), 22.1 (18-24); 32.9, 35.2, 37.2, 39.2.
public class TopP4BlueScreenLpduAiTests
{
    private const int LpduStrat = 0;
    private const float Tolerance = 0.45f;

    internal static readonly PartyRole[] West = [MainTank, PhysRangedDps, RegenHealer, MeleeDpsA];
    internal static readonly PartyRole[] East = [OffTank, CasterDps, ShieldHealer, MeleeDpsB];

    internal static readonly PartyRole[][] SplitMarkers = [[PhysRangedDps, CasterDps], [RegenHealer, ShieldHealer], [MeleeDpsA, MeleeDpsB]];

    internal static readonly Dictionary<PartyRole, Vector2> Quickmarch = new()
    {
        [MainTank] = new(-12.47f, -5.17f), [PhysRangedDps] = new(-13.50f, 0f),
        [RegenHealer] = new(-12.47f, 5.17f), [MeleeDpsA] = new(-9.55f, 9.55f),
        [OffTank] = new(12.47f, -5.17f), [CasterDps] = new(13.50f, 0f),
        [ShieldHealer] = new(12.47f, 5.17f), [MeleeDpsB] = new(9.55f, 9.55f),
    };

    internal static readonly Dictionary<PartyRole, Vector2> StepIn = new()
    {
        [MainTank] = new(-10.16f, -4.21f), [PhysRangedDps] = new(-11.00f, 0f),
        [RegenHealer] = new(-10.16f, 4.21f), [MeleeDpsA] = new(-7.78f, 7.78f),
        [OffTank] = new(10.16f, -4.21f), [CasterDps] = new(11.00f, 0f),
        [ShieldHealer] = new(10.16f, 4.21f), [MeleeDpsB] = new(7.78f, 7.78f),
    };

    internal static readonly Vector2 OuterStackWest = new(-4.97f, 12.01f);
    internal static readonly Vector2 InnerStackWest = new(-3.76f, 10.34f);
    internal static readonly Vector2 FinalStepInWest = new(-4.02f, 9.70f);

    public enum Spot { Quickmarch, StepIn, OuterStack, InnerStack, FinalStepIn }

    [TestCase(7.7f, Spot.Quickmarch, 0, Description = "before aim 1")]
    [TestCase(8.6f, Spot.Quickmarch, 0, Description = "beams 1")]
    [TestCase(12.7f, Spot.OuterStack, 0)]
    [TestCase(13.0f, Spot.OuterStack, 0, Description = "repeats 1")]
    [TestCase(13.6f, Spot.OuterStack, 0, Description = "stacks 1")]
    [TestCase(17.7f, Spot.Quickmarch, 0, Description = "before aim 2")]
    [TestCase(18.1f, Spot.Quickmarch, 0, Description = "ring 2")]
    [TestCase(18.5f, Spot.Quickmarch, 0, Description = "beams 2")]
    [TestCase(19.1f, Spot.StepIn, 1)]
    [TestCase(22.1f, Spot.InnerStack, 1, Description = "ring 4")]
    [TestCase(22.9f, Spot.InnerStack, 1, Description = "repeats 2")]
    [TestCase(23.4f, Spot.InnerStack, 1, Description = "stacks 2")]
    [TestCase(27.7f, Spot.Quickmarch, 2, Description = "before aim 3")]
    [TestCase(28.6f, Spot.Quickmarch, 2, Description = "beams 3")]
    [TestCase(32.7f, Spot.OuterStack, 2)]
    [TestCase(33.0f, Spot.OuterStack, 2, Description = "repeats 3")]
    [TestCase(33.5f, Spot.OuterStack, 2, Description = "stacks 3")]
    [TestCase(35.2f, Spot.OuterStack, 2, Description = "ring 6")]
    [TestCase(37.2f, Spot.FinalStepIn, 2, Description = "ring 7")]
    [TestCase(39.2f, Spot.FinalStepIn, 2, Description = "ring 8")]
    public void StandsOnTheRaidplansSpots(float at, Spot spot, int set)
        => AssertSpots(SplitMarkers, at, ExpectedSpots(spot, SplitMarkers[set]));

    // Every pair is pinned once in each set: run i marks pair i in set 1, pair i + 9 in set 2 and pair i + 18 in set 3.
    [TestCaseSource(nameof(PairRuns))]
    public void FlexesTheSouthernmostSameSideMarkerWithTheOtherSidesMelee(int run)
    {
        var sets = Enumerable.Range(0, 3).Select(set => Pairs[(run + 9 * set) % Pairs.Length]).ToArray();
        AssertSpotsAtEachStack(sets);
    }

    [Test]
    public void TheRaidplansExampleSwapsTheMeleeForSetTwoOnly()
    {
        PartyRole[][] sets = [[PhysRangedDps, CasterDps], [OffTank, MeleeDpsB], [RegenHealer, ShieldHealer]];
        AssertSpots(sets, 22.7f, Clumps(west: [MainTank, PhysRangedDps, RegenHealer, MeleeDpsB], InnerStackWest));
        AssertSpots(sets, 32.7f, Clumps(west: West, OuterStackWest));
    }

    [Test]
    public void AMarkedMeleeSwapsWithTheOtherMelee()
        => AssertSpots([[RegenHealer, MeleeDpsA], [RegenHealer, ShieldHealer], [MeleeDpsA, MeleeDpsB]], 12.7f,
                       Clumps(west: [MainTank, PhysRangedDps, RegenHealer, MeleeDpsB], OuterStackWest));

    [Test]
    public void ATankNeverFlexes()
        => AssertSpots([[CasterDps, OffTank], [RegenHealer, ShieldHealer], [MeleeDpsA, MeleeDpsB]], 12.7f,
                       Clumps(west: [MainTank, PhysRangedDps, RegenHealer, CasterDps], OuterStackWest));

    [Test]
    public void BothMarkersOnTheSameSideAgainFlexAgain()
    {
        PartyRole[][] sets = [[MainTank, PhysRangedDps], [MainTank, PhysRangedDps], [OffTank, ShieldHealer]];
        AssertSpots(sets, 12.7f, Clumps(west: [MainTank, MeleeDpsB, RegenHealer, MeleeDpsA], OuterStackWest));
        AssertSpots(sets, 22.7f, Clumps(west: [MainTank, MeleeDpsB, RegenHealer, MeleeDpsA], InnerStackWest));
        AssertSpots(sets, 32.7f, Clumps(west: [MainTank, PhysRangedDps, RegenHealer, ShieldHealer], OuterStackWest));
    }

    private static readonly PartyRole[][] Pairs =
        PerRole.All.SelectMany((a, i) => PerRole.All.Skip(i + 1).Select(b => new[] { a, b })).ToArray();

    private static IEnumerable<int> PairRuns() => Enumerable.Range(0, Pairs.Length);

    private static void AssertSpotsAtEachStack(PartyRole[][] sets)
    {
        var expected = new Dictionary<float, Dictionary<PartyRole, Vector2>>
        {
            [12.7f] = Clumps(WestAfterFlex(sets[0]), OuterStackWest),
            [22.7f] = Clumps(WestAfterFlex(sets[1]), InnerStackWest),
            [32.7f] = Clumps(WestAfterFlex(sets[2]), OuterStackWest),
            [36.7f] = Clumps(WestAfterFlex(sets[2]), FinalStepInWest),
        };
        var seen = Probe(sets, expected.Keys.ToArray(), out var run);
        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            foreach (var (at, spots) in expected)
                foreach (var (role, spot) in spots)
                    Assert.That(Vector2.Distance(seen[at][role], spot), Is.LessThan(Tolerance),
                                $"{role} at {seen[at][role]} at {at}s, expected {spot} (markers {Describe(sets)}, seed {run.Seed})");
        });
    }

    // Written from the rule rather than the strat's code: the southern of two same-side markers crosses, and the
    // other side's melee takes its place.
    internal static PartyRole[] WestAfterFlex(PartyRole[] markers)
    {
        var westMarked = markers.Count(West.Contains);
        if (westMarked == 1) return West;
        var side = westMarked == 2 ? West : East;
        var other = westMarked == 2 ? East : West;
        var crosser = Array.IndexOf(side, markers[0]) > Array.IndexOf(side, markers[1]) ? markers[0] : markers[1];
        var filler = other[3];
        return West.Select(r => r == crosser ? filler : r == filler ? crosser : r).ToArray();
    }

    private static Dictionary<PartyRole, Vector2> ExpectedSpots(Spot spot, PartyRole[] markers) => spot switch
    {
        Spot.Quickmarch => Quickmarch,
        Spot.StepIn => StepIn,
        Spot.OuterStack => Clumps(WestAfterFlex(markers), OuterStackWest),
        Spot.InnerStack => Clumps(WestAfterFlex(markers), InnerStackWest),
        _ => Clumps(WestAfterFlex(markers), FinalStepInWest),
    };

    internal static Dictionary<PartyRole, Vector2> Clumps(PartyRole[] west, Vector2 westSpot)
        => PerRole.All.ToDictionary(r => r, r => west.Contains(r) ? westSpot : westSpot with { X = -westSpot.X });

    private static void AssertSpots(PartyRole[][] sets, float at, Dictionary<PartyRole, Vector2> expected)
    {
        var seen = Probe(sets, [at], out var run);
        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            foreach (var (role, spot) in expected)
                Assert.That(Vector2.Distance(seen[at][role], spot), Is.LessThan(Tolerance),
                            $"{role} at {seen[at][role]} at {at}s, expected {spot} (seed {run.Seed})");
        });
    }

    private static Dictionary<float, Dictionary<PartyRole, Vector2>> Probe(PartyRole[][] sets, float[] times, out ScenarioRun run)
    {
        var seen = new Dictionary<float, Dictionary<PartyRole, Vector2>>();
        run = ScenarioRun.Execute(typeof(TopP4BlueScreenScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            StopAt = times.Max() + 0.1f,
            Overrides = o => Pin((TopP4BlueScreenStateOverrides)o, sets),
            Probe = p =>
            {
                foreach (var at in times)
                    if (p.Crossed(at))
                        seen[at] = PerRole.All.ToDictionary(r => r, r => Flat(p.Member(r)!.Position));
            },
        });
        return seen;
    }

    private static string Describe(PartyRole[][] sets) => string.Join(" / ", sets.Select(s => string.Join("+", s)));

    internal static Vector2 Flat(Vector3 position) => new(position.X, position.Z);

    internal static void Pin(TopP4BlueScreenStateOverrides o, PartyRole[][] sets) => o.StackTargets = sets;
}
