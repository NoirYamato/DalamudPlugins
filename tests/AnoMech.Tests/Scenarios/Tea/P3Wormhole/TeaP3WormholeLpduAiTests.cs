using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea.P3Wormhole;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// The LPDU strat (x east, z south) is upstream's NAUR with the Enumerations taken by LPDU's priority: marked M1 or R1
// joins the tanks east, marked M2 or R2 the healers west, and when both marked fall on one side the melee goes to the
// other; the unmarked DPS wait north, as NAUR has them. NAUR sends a lone marked melee east, and otherwise the first of
// the two in party order, so the strats part only when M1 and R1, or M2 and R1, are marked.
//
//   0 to 41.05  NAUR's moves: under Alexander Prime, the stack, the numbers' spots and soaks, back to the middle.
//   45.31  the Enumeration markers.   46.0  the rings: east (4.4, -0.3) with the tanks at (5.2, 0.5) and (5.2, -1.1),
//          west (-5.2, -0.3) with the healers at (-6.0, 0.5) and (-6.0, -1.1); the unmarked at (0, -6) and (-0.8, -7.4),
//          the first of them in party order nearer.   50.44  the Enumerations, each 5 y round its marked player.
public class TeaP3WormholeLpduAiTests
{
    internal const int LpduStrat = 0;
    internal const int NaStrat = 1;

    internal const float EnumerationMove = 46.0f;
    internal const float JustBeforeTheEnumerations = 50.4f;
    internal const float EnumerationRadius = 5f;
    // A bot lands on its spot; the debug-driven player's seat stops up to a frame's walk short of it, since its arrival
    // only stops the steering.
    private const float OnItsSpot = 0.15f;
    private const float Identical = 1e-4f;

    internal static readonly Vector2 EastMarked = new(4.4f, -0.3f);
    internal static readonly Vector2 WestMarked = new(-5.2f, -0.3f);
    internal static readonly Vector2[] UnmarkedNorth = [new(0f, -6.0f), new(-0.8f, -7.4f)];

    // Brute Justice's corner and the wormholes' pair.
    internal readonly record struct Layout(bool BruteJusticeNorthWest, bool PuddlesMirrored)
    {
        public override string ToString() => $"Brute Justice {(BruteJusticeNorthWest ? "NW" : "NE")}, wormholes {(PuddlesMirrored ? "WSW/ENE" : "WNW/ESE")}";
    }

    internal static readonly Layout[] Layouts = [new(false, false), new(true, false), new(false, true), new(true, true)];

    // Every pair of marked DPS, the one LPDU's priority sends to the tanks and the one NAUR's does.
    internal static readonly (PartyRole First, PartyRole Second, PartyRole LpduEast, PartyRole NaurEast)[] MarkedPairs =
    [
        (MeleeDpsA, MeleeDpsB, MeleeDpsA, MeleeDpsA),
        (MeleeDpsA, PhysRangedDps, PhysRangedDps, MeleeDpsA),
        (MeleeDpsA, CasterDps, MeleeDpsA, MeleeDpsA),
        (MeleeDpsB, PhysRangedDps, PhysRangedDps, MeleeDpsB),
        (MeleeDpsB, CasterDps, MeleeDpsB, MeleeDpsB),
        (PhysRangedDps, CasterDps, PhysRangedDps, PhysRangedDps),
    ];

    private static IEnumerable<TestCaseData> Pairs()
        => MarkedPairs.Select(pair => new TestCaseData(pair.First, pair.Second, pair.LpduEast, pair.NaurEast)
                                          .SetName($"{{m}}({Label(pair.First)} and {Label(pair.Second)} marked)"));

    internal static string Label(PartyRole role) => SettingsGrid.RoleLabel(role);

    internal static void Pin(TeaP3WormholeStateOverrides o, Layout layout, PartyRole first, PartyRole second)
    {
        o.BruteJusticeNorthWest = layout.BruteJusticeNorthWest;
        o.PuddlesMirrored = layout.PuddlesMirrored;
        o.Enumeration[first] = true;
        o.Enumeration[second] = true;
    }

    // Where LPDU's priority puts a DPS seat for the Enumerations.
    internal static Vector2 LpduSpot(PartyRole seat, PartyRole first, PartyRole second, PartyRole east)
    {
        if (seat == east) return EastMarked;
        if (seat == first || seat == second) return WestMarked;
        return UnmarkedNorth[TeaP3WormholeState.Dps.Except([first, second]).ToList().IndexOf(seat)];
    }

    [Test]
    public void TheEuLpduStratComesFirstSoItIsTheDefaultAndUpstreamsNaurIsNa()
    {
        var strats = ScenarioCatalog.Create().Scenarios.OfType<TeaP3WormholeScenario>().Single().AiStrats;
        Assert.That(strats.Select(s => (s.GetType(), s.Name, s.Group)), Is.EqualTo(new (Type, string, string?)[]
        {
            (typeof(TeaP3WormholeLpduAi), "[LPDU] Light PF", "EU"),
            (typeof(TeaP3WormholeAi), "NAUR", "NA"),
        }));
    }

    // On every layout, with the numbers and the player's seat, under the debug driver, rolled by the seed.
    [TestCaseSource(nameof(Pairs))]
    public void TheMarkedDpsTakeTheRingsByTheLpduPriority(PartyRole first, PartyRole second, PartyRole east, PartyRole naurEast)
    {
        var west = first == east ? second : first;
        Assert.Multiple(() =>
        {
            foreach (var layout in Layouts)
            {
                var seed = Random.Shared.Next();
                var seats = SeatsJustBeforeTheEnumerations(LpduStrat, seed, layout, first, second, out var run);
                var why = $"seed {seed}, {layout}, {Label(first)} and {Label(second)} marked";
                Assert.That(run.Passed, run.ToString);
                foreach (var dps in TeaP3WormholeState.Dps)
                    Assert.That(Vector2.Distance(seats[dps], LpduSpot(dps, first, second, east)), Is.LessThan(OnItsSpot), $"{Label(dps)} at {seats[dps]}, {why}");
                Assert.That(InTheRingOf(seats, east), Is.EquivalentTo(new[] { east, MainTank, OffTank }), $"the ring with the tanks, {why}");
                Assert.That(InTheRingOf(seats, west), Is.EquivalentTo(new[] { west, RegenHealer, ShieldHealer }), $"the ring with the healers, {why}");
            }
        });
    }

    // Frame by frame on one seed: every seat where NAUR's strat has it until the Enumeration move, and at the
    // Enumerations every seat where NAUR's stands but the two marked, which trade spots where the priorities part.
    [TestCaseSource(nameof(Pairs))]
    public void EverySeatStandsWhereNaurPutsItButTheMarkedWhereThePrioritiesPart(PartyRole first, PartyRole second, PartyRole east, PartyRole naurEast)
    {
        foreach (var layout in Layouts)
        {
            var seed = Random.Shared.Next();
            var lpdu = Frames(LpduStrat, seed, layout, first, second, out var lpduRun);
            var naur = Frames(NaStrat, seed, layout, first, second, out var naurRun);
            var why = $"seed {seed}, {layout}, {Label(first)} and {Label(second)} marked";

            Assert.That(lpduRun.Passed && naurRun.Passed, () => $"{lpduRun}{Environment.NewLine}{naurRun}");
            Assert.That(lpdu.Select(f => f.Time), Is.EqualTo(naur.Select(f => f.Time)), why);
            var before = lpdu.Zip(naur).Where(f => f.First.Time < EnumerationMove).ToList();
            Assert.That(before, Has.Count.GreaterThan(2700), "frames compared");
            var widest = before.Select(f => (f.First.Time, Apart: PerRole.All.Max(r => Vector2.Distance(f.First.Seats[r], f.Second.Seats[r])))).MaxBy(f => f.Apart);
            Assert.That(widest.Apart, Is.LessThan(Identical), $"{why}: a seat stood {widest.Apart:F4} y from NAUR's at {widest.Time:F2}");

            var lpduAt = lpdu.First(f => f.Time >= JustBeforeTheEnumerations).Seats;
            var naurAt = naur.First(f => f.Time >= JustBeforeTheEnumerations).Seats;
            PartyRole[] traded = east == naurEast ? [] : [first, second];
            Assert.Multiple(() =>
            {
                foreach (var role in PerRole.All)
                {
                    var naursSeat = traded.Contains(role) ? traded.Single(other => other != role) : role;
                    Assert.That(Vector2.Distance(lpduAt[role], naurAt[naursSeat]), Is.LessThan(traded.Contains(role) ? OnItsSpot : Identical),
                        $"{why}: LPDU's {Label(role)} at {lpduAt[role]}, NAUR's {Label(naursSeat)} at {naurAt[naursSeat]}");
                }
            });
        }
    }

    // Every marked pair on every layout, with the player's seat, under the debug driver, in each seat in turn and the
    // numbers shuffled by the seed.
    [Test]
    public void WithBotsAloneNobodyDiesOnAnyPairLayoutOrSeat()
    {
        var problems = new List<string>();
        var runs = 0;
        foreach (var (first, second, _, _) in MarkedPairs)
        foreach (var layout in Layouts)
        foreach (var seat in PerRole.All)
        {
            var seed = 7919 * runs++ + 11;
            var run = ScenarioRun.Execute(typeof(TeaP3WormholeScenario), LpduStrat, seed, new ScenarioRunOptions
            {
                PlayerRole = seat,
                Overrides = o => Pin((TeaP3WormholeStateOverrides)o, layout, first, second),
            });
            if (!run.Passed) problems.Add($"{layout}, {Label(first)} and {Label(second)} marked: {run}");
        }

        TestContext.Out.WriteLine($"{runs} runs, {problems.Count} with a death");
        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems.Take(20)));
    }

    internal static PartyRole[] InTheRingOf(Dictionary<PartyRole, Vector2> seats, PartyRole marked)
        => PerRole.All.Where(role => Vector2.Distance(seats[role], seats[marked]) <= EnumerationRadius).ToArray();

    internal static Vector2 Flat(Vector3 position) => new(position.X, position.Z);

    private static Dictionary<PartyRole, Vector2> SeatsJustBeforeTheEnumerations(int strat, int seed, Layout layout, PartyRole first, PartyRole second,
                                                                                 out ScenarioRun run)
    {
        var seats = new Dictionary<PartyRole, Vector2>();
        run = ScenarioRun.Execute(typeof(TeaP3WormholeScenario), strat, seed, new ScenarioRunOptions
        {
            Overrides = o => Pin((TeaP3WormholeStateOverrides)o, layout, first, second),
            Probe = p =>
            {
                if (p.Crossed(JustBeforeTheEnumerations))
                    foreach (var role in PerRole.All) seats[role] = Flat(p.Member(role)!.Position);
            },
        });
        return seats;
    }

    private static List<(float Time, Dictionary<PartyRole, Vector2> Seats)> Frames(int strat, int seed, Layout layout, PartyRole first, PartyRole second,
                                                                                   out ScenarioRun run)
    {
        var frames = new List<(float Time, Dictionary<PartyRole, Vector2> Seats)>();
        run = ScenarioRun.Execute(typeof(TeaP3WormholeScenario), strat, seed, new ScenarioRunOptions
        {
            Overrides = o => Pin((TeaP3WormholeStateOverrides)o, layout, first, second),
            Probe = p => frames.Add((p.Time, PerRole.All.ToDictionary(r => r, r => Flat(p.Member(r)!.Position)))),
        });
        return frames;
    }
}
