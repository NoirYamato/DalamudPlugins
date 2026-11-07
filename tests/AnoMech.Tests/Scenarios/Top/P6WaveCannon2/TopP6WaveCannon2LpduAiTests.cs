using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Multiplayer;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P6WaveCannon2;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// The LPDU strat (x east, z south). The pairs hold the quadrant of their waymark: MT + R1 at 4 (NW), OT + R2 at 1
// (NE), H1 + M1 at 3 (SW), H2 + M2 at 2 (SE). Each spot is written for a support in the SE quadrant, (a, b); its
// DPS stands at (b, a), and the other quadrants mirror the signs, so supports side-step toward the N-S axis and
// DPS toward the E-W axis.
//
//   2.0   home, both patterns: (6.4, 8.4).
//   9.60  first set (in first the cross |c| < 5, out first the bands |c| > 10); the bots move at 9.7.
//   In first:  9.7 the pair stacks in the centre square (3.4, 3.4); 14.0 back out past home (6.4, 11.4);
//              18.0 side step (3.5, 12.0), where laser set 1 (19.07) finds them; 20.0 laser spot (3.5, 8.4)
//              for set 2 (21.07); 22.0 the stack.
//   Out first: 9.7 the pair stacks in the corner (11.3, 11.3); 14.0 back home stacked (7.3, 7.3); 16.0 side
//              step; 18.0 laser spot for both sets; 21.2 the stack.
//   The stack: a line due south toward C, tanks (0, 6.5), the other six (0, 10.5); Wild Charge 27.40.
public class TopP6WaveCannon2LpduAiTests
{
    internal const int LpduStrat = 0;
    private const float Tolerance = 0.45f;

    // Multiplayer fixtures leave their hosting session installed, which would make every seat real here too.
    private MultiplayerManager? leftInstalled;

    [SetUp]
    public void Solo()
    {
        leftInstalled = Plugin.Multiplayer;
        DalamudServices.Install(nameof(Plugin.Multiplayer), null!);
    }

    [TearDown]
    public void PutBackWhatWasInstalled() => DalamudServices.Install(nameof(Plugin.Multiplayer), leftInstalled!);

    public enum Stage { Home, CentreSquare, OutPastHome, Corner, BackHome, Side, Laser, Stack }

    internal static readonly PartyRole[][] Pairs =
        [[MainTank, PhysRangedDps], [OffTank, CasterDps], [RegenHealer, MeleeDpsA], [ShieldHealer, MeleeDpsB]];

    internal static Vector2 QuadrantSigns(PartyRole role) => role switch
    {
        MainTank or PhysRangedDps => new(-1f, -1f),
        OffTank or CasterDps => new(1f, -1f),
        RegenHealer or MeleeDpsA => new(-1f, 1f),
        _ => new(1f, 1f),
    };

    private static Vector2 SupportSpot(Stage stage) => stage switch
    {
        Stage.Home => new(6.4f, 8.4f),
        Stage.CentreSquare => new(3.4f, 3.4f),
        Stage.OutPastHome => new(6.4f, 11.4f),
        Stage.Corner => new(11.3f, 11.3f),
        Stage.BackHome => new(7.3f, 7.3f),
        Stage.Side => new(3.5f, 12f),
        _ => new(3.5f, 8.4f),
    };

    internal static Vector2 Spot(PartyRole role, Stage stage)
    {
        if (stage == Stage.Stack) return role.IsTank() ? new(0f, 6.5f) : new(0f, 10.5f);
        var support = SupportSpot(stage);
        return QuadrantSigns(role) * (role.IsDps() ? new Vector2(support.Y, support.X) : support);
    }

    internal static Dictionary<PartyRole, Vector2> Spots(Stage stage) => PerRole.All.ToDictionary(r => r, r => Spot(r, stage));

    // Every hit, and the stage each pattern meets it on.
    internal static readonly (float At, string Hit, Stage InFirst, Stage OutFirst)[] Hits =
    [
        (9.60f, "first set", Stage.Home, Stage.Home),
        (11.61f, "second set", Stage.CentreSquare, Stage.Corner),
        (11.91f, "wave 1", Stage.CentreSquare, Stage.Corner),
        (13.91f, "wave 2", Stage.CentreSquare, Stage.Corner),
        (15.91f, "wave 3", Stage.OutPastHome, Stage.BackHome),
        (17.91f, "wave 4", Stage.OutPastHome, Stage.Side),
        (19.07f, "laser set 1", Stage.Side, Stage.Laser),
        (19.91f, "wave 5", Stage.Side, Stage.Laser),
        (21.07f, "laser set 2", Stage.Laser, Stage.Laser),
        (27.40f, "Wild Charge", Stage.Stack, Stage.Stack),
    ];

    private static IEnumerable<TestCaseData> HitCases()
        => from inFirst in new[] { true, false }
           from hit in Hits
           select new TestCaseData(inFirst, hit.At - 0.05f, inFirst ? hit.InFirst : hit.OutFirst)
               .SetName($"{{m}}({(inFirst ? "in" : "out")} first, {hit.Hit}, {(inFirst ? hit.InFirst : hit.OutFirst)})");

    [TestCaseSource(nameof(HitCases))]
    public void StandsOnItsLpduSpotForEachHit(bool inFirst, float at, Stage stage)
        => AssertSpots(inFirst, at, Spots(stage));

    [TestCase(true, 13.85f, Stage.CentreSquare)]
    [TestCase(false, 13.85f, Stage.Corner)]
    [TestCase(false, 15.85f, Stage.BackHome)]
    public void EachPairStacksOnOneSpotAtTheInOutAndBackHomeSteps(bool inFirst, float at, Stage stage)
    {
        var seen = Probe(inFirst, Random.Shared.Next(), [at], out var run);
        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            foreach (var pair in Pairs)
            {
                Assert.That(Spot(pair[0], stage), Is.EqualTo(Spot(pair[1], stage)), $"{pair[0]} and {pair[1]} share one spot");
                Assert.That(Vector2.Distance(seen[at][pair[0]], seen[at][pair[1]]), Is.LessThan(2 * Tolerance),
                            $"{pair[0]} at {seen[at][pair[0]]}, {pair[1]} at {seen[at][pair[1]]} (seed {run.Seed})");
            }
        });
    }

    // Until the stack each pair keeps its quadrant; the support stands on the N-S side of the diagonal (|x| < |z|) and
    // the DPS on the E-W side, except where the pair stacks.
    [TestCase(true)]
    [TestCase(false)]
    public void PairsKeepTheirQuadrantSupportsBesideTheNorthSouthAxisDpsBesideTheEastWest(bool inFirst)
    {
        var times = Hits.Where(h => h.At < 22f).Select(h => h.At - 0.05f).ToArray();
        var seen = Probe(inFirst, Random.Shared.Next(), times, out var run);
        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            foreach (var (at, _, inFirstStage, outFirstStage) in Hits.Where(h => h.At < 22f))
            {
                var stage = inFirst ? inFirstStage : outFirstStage;
                foreach (var (role, position) in seen[at - 0.05f])
                {
                    var signs = QuadrantSigns(role);
                    Assert.That(MathF.Sign(position.X) == (int)signs.X && MathF.Sign(position.Y) == (int)signs.Y,
                                $"{role} at {position} before the {stage} hit at {at}s left its quadrant {signs} (seed {run.Seed})");
                    if (stage is Stage.CentreSquare or Stage.Corner or Stage.BackHome) continue;
                    var besideTheNorthSouthAxis = MathF.Abs(position.X) < MathF.Abs(position.Y);
                    Assert.That(besideTheNorthSouthAxis, Is.EqualTo(!role.IsDps()),
                                $"{role} at {position} at {at}s, {stage} (seed {run.Seed})");
                }
            }
        });
    }

    // The omens show at 3.90 and the first set goes off at 9.60: both patterns walk every bot the same way until
    // 9.7, and apart right after.
    [Test]
    public void NothingTheBotsDoBeforeTheFirstSetGivesThePatternAway()
    {
        var seed = Random.Shared.Next();
        var inFirst = Track(true, seed, out var inRun);
        var outFirst = Track(false, seed, out var outRun);
        Assert.That(inRun.Passed && outRun.Passed, () => $"{inRun}{Environment.NewLine}{outRun}");
        Assert.Multiple(() =>
        {
            var compared = 0;
            foreach (var (frame, positions) in inFirst.Where(f => f.Key < 9.7f))
            {
                compared++;
                foreach (var role in PerRole.All)
                    Assert.That(Vector2.Distance(positions[role], outFirst[frame][role]), Is.LessThan(1e-4f),
                                $"{role} at {frame:F2}s: {positions[role]} in first, {outFirst[frame][role]} out first (seed {seed})");
            }
            Assert.That(compared, Is.GreaterThan(500), "frames compared");
            var key = inFirst.Keys.First(t => t >= 10.5f);
            Assert.That(PerRole.All.All(r => Vector2.Distance(inFirst[key][r], outFirst[key][r]) > 5f),
                        $"every role is apart by 10.5 s (seed {seed})");
        });
    }

    private static Dictionary<float, Dictionary<PartyRole, Vector2>> Track(bool inFirst, int seed, out ScenarioRun run)
    {
        var frames = new Dictionary<float, Dictionary<PartyRole, Vector2>>();
        run = ScenarioRun.Execute(typeof(TopP6WaveCannon2Scenario), LpduStrat, seed, new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            StopAt = 10.6f,
            Overrides = o => ((TopP6WaveCannon2StateOverrides)o).InFirst = inFirst,
            Probe = p => frames[MathF.Round(p.Time, 3)] = PerRole.All.ToDictionary(r => r, r => Flat(p.Member(r)!.Position)),
        });
        return frames;
    }

    private static void AssertSpots(bool inFirst, float at, Dictionary<PartyRole, Vector2> expected)
    {
        var seen = Probe(inFirst, Random.Shared.Next(), [at], out var run);
        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            foreach (var (role, spot) in expected)
                Assert.That(Vector2.Distance(seen[at][role], spot), Is.LessThan(Tolerance),
                            $"{role} at {seen[at][role]} at {at}s, expected {spot} ({(inFirst ? "in" : "out")} first, seed {run.Seed})");
        });
    }

    private static Dictionary<float, Dictionary<PartyRole, Vector2>> Probe(bool inFirst, int seed, float[] times, out ScenarioRun run)
    {
        var seen = new Dictionary<float, Dictionary<PartyRole, Vector2>>();
        run = ScenarioRun.Execute(typeof(TopP6WaveCannon2Scenario), LpduStrat, seed, new ScenarioRunOptions
        {
            StopAt = times.Max() + 0.1f,
            Overrides = o => ((TopP6WaveCannon2StateOverrides)o).InFirst = inFirst,
            Probe = p =>
            {
                foreach (var at in times)
                    if (p.Crossed(at))
                        seen[at] = PerRole.All.ToDictionary(r => r, r => Flat(p.Member(r)!.Position));
            },
        });
        return seen;
    }

    internal static Vector2 Flat(Vector3 position) => new(position.X, position.Z);
}
