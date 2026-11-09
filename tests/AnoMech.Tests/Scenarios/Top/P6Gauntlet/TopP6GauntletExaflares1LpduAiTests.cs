using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P6Gauntlet;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// The LPDU strat in the Gauntlet's Exaflares 1 part on its own, x east, z south, bearings clockwise from north, Alpha
// Omega at the centre. θ0 is the first exaflare's compass point, s +1 when the others follow it clockwise, -1
// counter-clockwise.
//
//    0.0  on the autos' spots, where a run starting here puts everyone: MT (0, -5) inside north, OT (14.5, 0) outside
//         east, the rest (0, 6) inside south.
//    2.0  out of the hitbox to read the arrows (shown from 6.4 and 7.4): MT north (0, -14), the rest south (0, 14);
//         the OT is out already.
//    8.0  everyone in the middle for the first puddle set (11.9).
//   12.9  r 14 at θ0 - 45s, beside the first exaflare and away from the second; 16.2 on to θ0, 17.9 θ0 + 45s,
//         19.8 θ0 + 90s: a compass point per puddle, behind the exaflares.
//   21.6  in to r 4 on each player's waymark bearing, clear of the last two puddles; 24.7, after the last one has
//         gone off, out to r 10.5 on it: MT A, R2 1, OT B, M2 2, H2 C, M1 3, H1 D, R1 4.
//   30.0  after the second lasers, the Wild Charge line in front of C: tanks (0, 6.5), the rest (0, 10.5).
//   36.5  back on the autos' spots.
public class TopP6GauntletExaflares1LpduAiTests
{
    internal const int LpduStrat = 0;
    private const float Tolerance = 0.45f;

    // A stage's spot is checked as each bot comes closest to it before its next move: a bot can be redirected a few
    // hundredths of a second short of its spot.
    private const float PassingTolerance = 0.6f;

    internal static readonly Dictionary<PartyRole, Vector2> AutoSpots = new()
    {
        [MainTank] = new(0f, -5f), [OffTank] = new(14.5f, 0f),
        [RegenHealer] = new(0f, 6f), [ShieldHealer] = new(0f, 6f), [MeleeDpsA] = new(0f, 6f),
        [MeleeDpsB] = new(0f, 6f), [PhysRangedDps] = new(0f, 6f), [CasterDps] = new(0f, 6f),
    };

    internal static readonly Dictionary<PartyRole, Vector2> SpreadSpots = new()
    {
        [MainTank] = new(0f, -10.5f), [CasterDps] = new(7.42f, -7.42f), [OffTank] = new(10.5f, 0f),
        [MeleeDpsB] = new(7.42f, 7.42f), [ShieldHealer] = new(0f, 10.5f), [MeleeDpsA] = new(-7.42f, 7.42f),
        [RegenHealer] = new(-10.5f, 0f), [PhysRangedDps] = new(-7.42f, -7.42f),
    };

    internal static readonly Dictionary<PartyRole, Vector2> WildChargeLine = new()
    {
        [MainTank] = new(0f, 6.5f), [OffTank] = new(0f, 6.5f),
        [RegenHealer] = new(0f, 10.5f), [ShieldHealer] = new(0f, 10.5f), [MeleeDpsA] = new(0f, 10.5f),
        [MeleeDpsB] = new(0f, 10.5f), [PhysRangedDps] = new(0f, 10.5f), [CasterDps] = new(0f, 10.5f),
    };

    internal static readonly Dictionary<PartyRole, Vector2> OutsideTheHitbox =
        PerRole.All.ToDictionary(r => r, r => r switch { MainTank => new Vector2(0f, -14f), OffTank => new Vector2(14.5f, 0f), _ => new Vector2(0f, 14f) });

    // Waymark bearing, r 4: the wait clear of the last two puddles.
    internal static Vector2 InsideTheLastPuddles(PartyRole role) => SpreadSpots[role] * (4f / 10.5f);

    // r 14 at θ0 + 45s × points, θ0 = 45 × first; -1 is the start, beside the first exaflare away from the second.
    internal static Vector2 OnThePath(int first, bool clockwise, int points)
    {
        var radians = (45f * first + (clockwise ? 45f : -45f) * points) * MathF.PI / 180f;
        return new Vector2(14f * MathF.Sin(radians), -14f * MathF.Cos(radians));
    }

    [Test]
    public void TheSpotsAreTheStratsAsWritten()
    {
        Assert.Multiple(() =>
        {
            foreach (var role in PerRole.All)
            {
                Assert.That(TopP6LpduAi.AutoSpot(role), Is.EqualTo(AutoSpots[role]).Using<Vector2>(Near), $"{role} autos");
                Assert.That(TopP6LpduAi.OutsideTheHitboxToReadTheArrows(role), Is.EqualTo(OutsideTheHitbox[role]).Using<Vector2>(Near), $"{role} arrows");
                Assert.That(TopP6GauntletLpduAi.SpreadSpot(role), Is.EqualTo(SpreadSpots[role]).Using<Vector2>(Near), $"{role} spread");
                Assert.That(TopP6GauntletLpduAi.InsideTheLastPuddles(role), Is.EqualTo(InsideTheLastPuddles(role)).Using<Vector2>(Near), $"{role} wait");
                Assert.That(TopP6LpduAi.WildChargeLineSpot(role), Is.EqualTo(WildChargeLine[role]).Using<Vector2>(Near), $"{role} Wild Charge");
            }
        });
    }

    // The start is the compass point beside the first exaflare on the side away from the second; the next three are
    // where the first three started.
    [Test]
    public void ThePathStartsBesideTheFirstExaflareAndFollowsTheirStarts([Range(0, 7)] int first, [Values] bool clockwise)
    {
        FakeGame.Install();
        var wave = new TopP6UnlimitedWaveCannon(first, clockwise, 1.9f);
        var start = TopP6LpduAi.OnTheExaflarePath(first, clockwise, TopP6LpduAi.BesideTheFirstExaflareAwayFromTheSecond);
        var firstStart = Flat(wave.Lane(0).Start.Position);
        var secondStart = Flat(wave.Lane(1).Start.Position);

        Assert.Multiple(() =>
        {
            Assert.That(start.Length(), Is.EqualTo(14f).Within(1e-3f));
            Assert.That(Degrees(start, firstStart), Is.EqualTo(45f).Within(1e-2f), "beside the first exaflare");
            Assert.That(Degrees(start, secondStart), Is.EqualTo(90f).Within(1e-2f), "away from the second");
            for (var k = 0; k < 3; k++)
                Assert.That(TopP6LpduAi.OnTheExaflarePath(first, clockwise, k), Is.EqualTo(Flat(wave.Lane(k).Start.Position) * (14f / 24f)).Using<Vector2>(Near),
                            $"exaflare {k + 1}'s point");
            for (var k = -1; k < 3; k++)
                Assert.That(TopP6LpduAi.OnTheExaflarePath(first, clockwise, k), Is.EqualTo(OnThePath(first, clockwise, k)).Using<Vector2>(Near), $"point {k}");
        });
    }

    private static IEnumerable<TestCaseData> Patterns()
        => from first in new[] { 0, 3, 6 }
           from clockwise in new[] { true, false }
           select new TestCaseData(first, clockwise).SetName($"{{m}}(first {first}, {(clockwise ? "clockwise" : "counter-clockwise")})");

    [TestCaseSource(nameof(Patterns))]
    public void StandsOnEveryStagesSpot(int first, bool clockwise)
    {
        // (from, until, spots): each stage's move, the next one.
        var stages = new (float From, float Until, Func<PartyRole, Vector2> Spot, string Name)[]
        {
            (2f, 8f, r => OutsideTheHitbox[r], "out of the hitbox"),
            (8f, 12.9f, _ => Vector2.Zero, "the middle"),
            (12.9f, 16.2f, _ => OnThePath(first, clockwise, -1), "the start"),
            (16.2f, 17.9f, _ => OnThePath(first, clockwise, 0), "the first exaflare's point"),
            (17.9f, 19.8f, _ => OnThePath(first, clockwise, 1), "the second exaflare's point"),
            (19.8f, 21.6f, _ => OnThePath(first, clockwise, 2), "the third exaflare's point"),
            (21.6f, 24.7f, InsideTheLastPuddles, "inside the last puddles"),
            (24.7f, 30f, r => SpreadSpots[r], "the Wave Cannon spread"),
            (30f, 36.5f, r => WildChargeLine[r], "the Wild Charge line"),
            (36.5f, 45f, r => AutoSpots[r], "the autos"),
        };
        var closest = stages.Select(_ => PerRole.All.ToDictionary(r => r, _ => float.MaxValue)).ToArray();
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            Overrides = o => Pin((TopP6GauntletStateOverrides)o, first, clockwise),
            Probe = p =>
            {
                for (var i = 0; i < stages.Length; i++)
                {
                    if (p.Time < stages[i].From || p.Time > stages[i].Until) continue;
                    foreach (var role in PerRole.All)
                        closest[i][role] = MathF.Min(closest[i][role], Vector2.Distance(Flat(p.Member(role)!.Position), stages[i].Spot(role)));
                }
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            for (var i = 0; i < stages.Length; i++)
                foreach (var role in PerRole.All)
                    Assert.That(closest[i][role], Is.LessThan(PassingTolerance), $"{role} at {stages[i].Name} (seed {run.Seed})");
        });
    }

    // Held on each spot from the time it arrives to the next move.
    [TestCase(7.9f, "out of the hitbox")]
    [TestCase(12.8f, "the middle")]
    [TestCase(16.1f, "the start")]
    [TestCase(19.75f, "the second exaflare's point")]
    [TestCase(24.65f, "inside the last puddles")]
    [TestCase(27.5f, "the first lasers")]
    [TestCase(29.5f, "the second lasers")]
    [TestCase(35.8f, "the Wild Charge")]
    [TestCase(40.8f, "the first Flash Gale")]
    [TestCase(44.0f, "the second Flash Gale")]
    public void HoldsItsSpotAt(float at, string what)
    {
        const int first = 5;
        const bool clockwise = false;
        var expected = at switch
        {
            < 8f => OutsideTheHitbox,
            < 12.9f => PerRole.All.ToDictionary(r => r, _ => Vector2.Zero),
            < 16.2f => PerRole.All.ToDictionary(r => r, _ => OnThePath(first, clockwise, -1)),
            < 21.6f => PerRole.All.ToDictionary(r => r, _ => OnThePath(first, clockwise, 1)),
            < 24.7f => PerRole.All.ToDictionary(r => r, InsideTheLastPuddles),
            < 30f => SpreadSpots,
            < 36.5f => WildChargeLine,
            _ => AutoSpots,
        };
        var seen = new Dictionary<PartyRole, Vector2>();
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            StopAt = at + 0.1f,
            Overrides = o => Pin((TopP6GauntletStateOverrides)o, first, clockwise),
            Probe = p =>
            {
                if (p.Crossed(at))
                    foreach (var role in PerRole.All)
                        seen[role] = Flat(p.Member(role)!.Position);
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            foreach (var (role, spot) in expected)
                Assert.That(Vector2.Distance(seen[role], spot), Is.LessThan(Tolerance), $"{role} at {seen[role]} at {what} ({at}s), expected {spot} (seed {run.Seed})");
        });
    }

    // The arrows show from 6.4 and 7.4; nothing the bots do before 12.9 tells the exaflares apart.
    [Test]
    public void NoMoveBeforeTheStartGivesTheExaflaresAway()
    {
        var seed = Random.Shared.Next();
        var runs = new[] { (0, true), (4, false), (7, true) }.Select(pattern =>
        {
            var seen = new List<Vector2>();
            var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, seed, new ScenarioRunOptions
            {
                PlayerRole = MainTank,
                StopAt = 12.95f,
                Overrides = o => Pin((TopP6GauntletStateOverrides)o, pattern.Item1, pattern.Item2),
                Probe = p =>
                {
                    if (p.Time < 12.9f) seen.AddRange(PerRole.All.Select(r => Flat(p.Member(r)!.Position)));
                },
            });
            Assert.That(run.Passed, run.ToString);
            return seen;
        }).ToList();

        Assert.That(runs[1], Is.EqualTo(runs[0]).Using<Vector2>(Near), "first 4, counter-clockwise");
        Assert.That(runs[2], Is.EqualTo(runs[0]).Using<Vector2>(Near), "first 7, clockwise");
    }

    internal static void Pin(TopP6GauntletStateOverrides o, int first, bool clockwise, float? step = null)
    {
        o.Parts = (TopP6GauntletPart.Exaflares1, TopP6GauntletPart.Exaflares1);
        o.Exaflares1First = first;
        o.Exaflares1Clockwise = clockwise;
        o.Exaflares1Step = step;
    }

    internal static Vector2 Flat(Vector3 position) => new(position.X, position.Z);

    private static float Degrees(Vector2 a, Vector2 b)
        => MathF.Acos(Math.Clamp(Vector2.Dot(Vector2.Normalize(a), Vector2.Normalize(b)), -1f, 1f)) * 180f / MathF.PI;

    private static bool Near(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < 0.01f;
}
