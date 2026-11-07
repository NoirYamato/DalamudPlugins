using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P6CosmoDive2;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// LPDU's spots (x east, z south; bearings clockwise from north). θ0 is the first exaflare's compass point, s = +1
// when the other three follow it clockwise and -1 counter-clockwise. Every dodge spot is on r 14 but the first, r 14.5:
// the second puddles drop on the way out to it, about r 5, and its walk on to θ0 passes them as they go off; half a
// yalm further out keeps a human who sets off as the first exaflare passes clear of its own.
//
//    0.5  MT inside north (0, -5), OT outside east (14.5, 0), the rest inside south (0, 6).
//    2.0  Unlimited Wave Cannon's cast (1.9): out of the hitbox to watch the arrows, MT (0, -14), the rest (0, 14);
//         the OT is out already.
//    7.5  the second arrow is up (7.4): everyone into the middle, where the first puddles drop (11.9).
//   12.9  out to θ0 - 45s, beside the first exaflare away from the second; the second puddles drop on the way (13.7).
//   16.2  the first exaflare has gone past at r 8 (16.1 with a 1.1 s step): on to θ0. Puddles 3-6 drop at 15.6, 17.5,
//         19.4 and 21.4.
//   17.9 / 19.8  on to θ0 + 45s, then θ0 + 90s.
//   21.5  the six hold on θ0 + 135s, where the last exaflare started; the tanks cross the boss into a Y at r 6.5, the
//         MT 135° and the OT 225° clockwise from the party's bearing: left and right of the far line as the party
//         faces the boss.
//   25.3  M1 fires a limit break 3 for show, right after the Cosmo Dive bar.
//   28.0  after the dive (27.6): back to the auto spots, the OT walking out east; M2 goes to the middle instead and the
//         MT makes room for it, back to (0, -7), so M2 is out of the MT's Flash Gale.
//   32.0  M2 fires its limit break 3 from the middle, for show.
public class TopP6CosmoDive2LpduAiTests
{
    internal const int LpduStrat = 0;
    internal const float UnlimitedWaveCannonAt = 1.9f;
    internal const float RunSpeed = 6.5f;
    internal const float FollowRadius = 14f, StartRadius = 14.5f, TankRadius = 6.5f;
    private const float Jitter = 0.3f;
    private const float Tolerance = 0.45f;

    internal static readonly PartyRole[] TheSix = [RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps];

    internal static readonly (int First, bool Clockwise)[] Patterns =
        Enumerable.Range(0, 8).SelectMany(first => new[] { (first, true), (first, false) }).ToArray();

    internal static readonly float[] Steps = [1.1f, 1.0f];

    internal static readonly Dictionary<PartyRole, Vector2> AutoSpots =
        PerRole.All.ToDictionary(r => r, r => r switch { MainTank => new Vector2(0f, -5f), OffTank => new Vector2(14.5f, 0f), _ => new Vector2(0f, 6f) });

    internal static readonly Dictionary<PartyRole, Vector2> SteppedOut =
        PerRole.All.ToDictionary(r => r, r => r switch { MainTank => new Vector2(0f, -14f), OffTank => new Vector2(14.5f, 0f), _ => new Vector2(0f, 14f) });

    internal static readonly Dictionary<PartyRole, Vector2> AutoSpotsAfterTheDive =
        PerRole.All.ToDictionary(r => r, r => r switch { MainTank => new Vector2(0f, -7f), MeleeDpsB => Vector2.Zero, _ => AutoSpots[r] });

    internal static readonly float[] MovesAt = [0.5f, 2f, 7.5f, 12.9f, 16.2f, 17.9f, 19.8f, 21.5f, 28f];

    // Read off the scenario's own exaflares: the k-th starts on wave.Bearing(k).
    internal static TopP6UnlimitedWaveCannon Wave(int first, bool clockwise, float step = 1.1f)
        => new(first, clockwise, UnlimitedWaveCannonAt, step);

    internal static Vector2 OnTheDodgeRing(float bearing) => Flat(TopP6AlphaOmega.AtBearing(bearing, FollowRadius));

    internal static Vector2 StartPoint(TopP6UnlimitedWaveCannon wave) => Flat(TopP6AlphaOmega.AtBearing(wave.Bearing(0) - 45f * wave.Turn, StartRadius));

    // Each move's spot for every seat, written from the rule.
    internal static Dictionary<PartyRole, Vector2> Expected(float move, int first, bool clockwise)
    {
        var wave = Wave(first, clockwise);
        var hold = wave.Bearing(3);
        Dictionary<PartyRole, Vector2> Everyone(Vector2 spot) => PerRole.All.ToDictionary(r => r, _ => spot);
        return move switch
        {
            0.5f => AutoSpots,
            28f => AutoSpotsAfterTheDive,
            2f => SteppedOut,
            7.5f => Everyone(Vector2.Zero),
            12.9f => Everyone(StartPoint(wave)),
            16.2f => Everyone(OnTheDodgeRing(wave.Bearing(0))),
            17.9f => Everyone(OnTheDodgeRing(wave.Bearing(1))),
            19.8f => Everyone(OnTheDodgeRing(wave.Bearing(2))),
            21.5f => PerRole.All.ToDictionary(r => r, r => r switch
            {
                MainTank => Flat(TopP6AlphaOmega.AtBearing(hold + 135f, TankRadius)),
                OffTank => Flat(TopP6AlphaOmega.AtBearing(hold + 225f, TankRadius)),
                _ => OnTheDodgeRing(hold),
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(move)),
        };
    }

    // The first exaflare on C, the second on 3: start at 2, then C, 3 and D, hold on 4; the tanks end on B's side and
    // C's side of the boss. The first on 1, the second on A: start at B, then 1, A and 4, hold on D.
    [TestCase(4, true, -9.9f, -9.9f, 6.5f, 0f, 0f, 6.5f, 135f)]
    [TestCase(1, false, -14f, 0f, 4.6f, -4.6f, 4.6f, 4.6f, 90f)]
    public void ReadsTheGuidesExamples(int first, bool clockwise, float holdX, float holdZ, float mtX, float mtZ, float otX, float otZ, float start)
    {
        var state = State(first, clockwise);

        Assert.Multiple(() =>
        {
            Assert.That(Distance(TopP6CosmoDive2LpduAi.PartyDiveHold(state), new Vector2(holdX, holdZ)), Is.LessThan(0.01f), "hold");
            Assert.That(Distance(TopP6CosmoDive2LpduAi.MainTankDiveSpot(state), new Vector2(mtX, mtZ)), Is.LessThan(0.01f), "MT");
            Assert.That(Distance(TopP6CosmoDive2LpduAi.OffTankDiveSpot(state), new Vector2(otX, otZ)), Is.LessThan(0.01f), "OT");
            Assert.That(Distance(TopP6CosmoDive2LpduAi.PointBesideTheFirstExaflareAwayFromTheSecond(state), Flat(TopP6AlphaOmega.AtBearing(start, StartRadius))),
                        Is.LessThan(0.01f), "start");
        });
    }

    [Test]
    public void FollowsTheExaflaresRoundFromBesideTheFirst()
    {
        Assert.Multiple(() =>
        {
            foreach (var (first, clockwise) in Patterns)
            {
                var state = State(first, clockwise);
                var why = $"first {first}, clockwise {clockwise}";
                Assert.That(Distance(TopP6CosmoDive2LpduAi.PointBesideTheFirstExaflareAwayFromTheSecond(state), Expected(12.9f, first, clockwise)[MainTank]),
                            Is.LessThan(0.01f), $"{why}: start");
                for (var k = 0; k < 3; k++)
                    Assert.That(Distance(TopP6CosmoDive2LpduAi.PointOfExaflare(state, k), Expected(MovesAt[4 + k], first, clockwise)[MainTank]),
                                Is.LessThan(0.01f), $"{why}: exaflare {k}'s point");
                Assert.That(Distance(TopP6CosmoDive2LpduAi.PartyDiveHold(state), Expected(21.5f, first, clockwise)[RegenHealer]),
                            Is.LessThan(0.01f), $"{why}: hold");
            }
        });
    }

    // Seen from the party facing the boss, the main tank ends left of the line through the boss and the off-tank right,
    // each 45° off it, 9.19 y apart.
    [Test]
    public void TheTanksCrossTheBossIntoAYMainTankLeftOffTankRight()
    {
        Assert.Multiple(() =>
        {
            foreach (var (first, clockwise) in Patterns)
            {
                var state = State(first, clockwise);
                var party = TopP6CosmoDive2LpduAi.PartyDiveHold(state);
                var mainTank = TopP6CosmoDive2LpduAi.MainTankDiveSpot(state);
                var offTank = TopP6CosmoDive2LpduAi.OffTankDiveSpot(state);
                var facing = -Vector2.Normalize(party);
                var why = $"first {first}, clockwise {clockwise}";
                Assert.That(LeftOf(facing, mainTank), Is.GreaterThan(4.5f), $"{why}: MT left");
                Assert.That(LeftOf(facing, offTank), Is.LessThan(-4.5f), $"{why}: OT right");
                Assert.That(Vector2.Dot(mainTank, facing), Is.GreaterThan(4.5f), $"{why}: MT past the boss");
                Assert.That(Vector2.Dot(offTank, facing), Is.GreaterThan(4.5f), $"{why}: OT past the boss");
                Assert.That(mainTank.Length(), Is.EqualTo(6.5f).Within(0.01f));
                Assert.That(offTank.Length(), Is.EqualTo(6.5f).Within(0.01f));
                Assert.That(Distance(mainTank, offTank), Is.EqualTo(9.19f).Within(0.01f), $"{why}: tanks apart");
            }
        });
    }

    // Unjittered, at a run's 6.5 y/s: each walk ends before the next move, from the moment the move after the
    // auto spots fires.
    [Test]
    public void EveryMoveLandsBeforeTheNext()
    {
        Assert.Multiple(() =>
        {
            foreach (var (first, clockwise) in Patterns)
                foreach (var role in PerRole.All)
                    for (var i = 1; i + 1 < MovesAt.Length; i++)
                    {
                        var walk = Distance(Expected(MovesAt[i - 1], first, clockwise)[role], Expected(MovesAt[i], first, clockwise)[role]) / RunSpeed;
                        Assert.That(MovesAt[i] + walk, Is.LessThan(MovesAt[i + 1]),
                                    $"first {first}, clockwise {clockwise}: {role} leaving at {MovesAt[i]} arrives after the move at {MovesAt[i + 1]}");
                    }
        });
    }

    // Every bot sets off for its spot (within the AI's jitter) as each move fires.
    [Test]
    public void SetsOffForTheStratsSpotAtEveryMove([Values(0, 3, 5, 6)] int first, [Values] bool clockwise)
    {
        var problems = new List<string>();
        var player = PerRole.All[(first + (clockwise ? 1 : 0)) % 8];
        var run = Run(player, first, clockwise, 1.1f, p =>
        {
            foreach (var move in MovesAt)
                if (p.Crossed(move + 0.05f))
                    foreach (var role in PerRole.All.Where(r => r != player))
                    {
                        var member = p.Member(role)!;
                        var heading = Flat(member.MoveDestination ?? member.Position);
                        var off = Distance(heading, Expected(move, first, clockwise)[role]);
                        if (off > Jitter + 0.02f) problems.Add($"{role} set off at {move} for {heading}, {off:F2} y off its spot");
                    }
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems));
    }

    // Settled: before the first exaflare arrow, in the middle, at the start point, at the dive, as M2 fires its limit
    // break and at the autos.
    [TestCase(6.0f, 2f)]
    [TestCase(11.8f, 7.5f)]
    [TestCase(16.1f, 12.9f)]
    [TestCase(27.5f, 21.5f)]
    [TestCase(32.0f, 28f)]
    [TestCase(35.2f, 28f)]
    public void StandsOnTheStratsSpots(float at, float move)
    {
        var problems = new List<string>();
        foreach (var (first, clockwise) in new[] { (2, true), (7, false) })
        {
            var player = clockwise ? CasterDps : MainTank;
            var run = Run(player, first, clockwise, 1.1f, p =>
            {
                if (!p.Crossed(at)) return;
                foreach (var role in PerRole.All.Where(r => r != player))
                {
                    var off = Distance(Flat(p.Member(role)!.Position), Expected(move, first, clockwise)[role]);
                    if (off > Tolerance) problems.Add($"first {first}, clockwise {clockwise}: {role} stood {off:F2} y off its spot at {at}");
                }
            }, stopAt: at + 0.1f);
            if (!run.Passed) problems.Add(run.ToString());
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems));
    }

    // The arrows only show 4.5 s into each exaflare's cast, so until the second has shown (7.4) nobody may give the
    // pattern away: the same seed's bots stand on the same spots whatever the roll, up to the move out of the middle.
    [Test]
    public void NothingDependsOnTheRollBeforeTheSecondArrowShows()
    {
        var seed = Random.Shared.Next();
        var seen = new List<Dictionary<PartyRole, Vector2>>();
        foreach (var (first, clockwise) in new[] { (0, true), (5, false) })
        {
            var positions = new Dictionary<PartyRole, Vector2>();
            var run = ScenarioRun.Execute(typeof(TopP6CosmoDive2Scenario), LpduStrat, seed, new ScenarioRunOptions
            {
                PlayerRole = MainTank,
                StopAt = 12.95f,
                Overrides = o => Pin((TopP6CosmoDive2StateOverrides)o, first, clockwise, 1.1f),
                Probe = p =>
                {
                    if (p.Crossed(12.85f))
                        foreach (var role in PerRole.All.Where(r => r != MainTank))
                            positions[role] = Flat(p.Member(role)!.Position);
                },
            });
            Assert.That(run.Passed, run.ToString);
            seen.Add(positions);
        }

        Assert.That(seen[0].Keys, Is.EquivalentTo(seen[1].Keys));
        Assert.Multiple(() =>
        {
            foreach (var (role, at) in seen[0])
                Assert.That(Distance(at, seen[1][role]), Is.LessThan(0.01f), $"{role} at 12.85");
        });
    }

    // With tank mitigation required, so bots press their buttons: M1's limit break 3 right after the Cosmo Dive bar
    // (25.2), M2's once the gauge is back; a human melee seat fires nothing, and the gauge is full after each.
    [TestCase(MainTank)]
    [TestCase(MeleeDpsA)]
    public void TheMeleeLimitBreaksPlayForShow(PartyRole player)
    {
        var fired = new List<(float Time, PartyRole Role)>();
        var bars = new List<int>();
        Dictionary<string, PartyRole>? seats = null;
        var rowsRead = 0;
        var limitBreaks = new[] { MeleeDpsA, MeleeDpsB }.ToDictionary(r => LimitBreakHandler.ActionId(PartyPresets.Standard[(int)r].ClassJob, 3), r => r);
        var run = ScenarioRun.Execute(typeof(TopP6CosmoDive2Scenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = player,
            UserActionsEnabled = true,
            Overrides = o => Pin((TopP6CosmoDive2StateOverrides)o, 3, false, 1.1f),
            Probe = p =>
            {
                seats ??= PerRole.All.ToDictionary(r => p.Member(r)!.GameObjectId.ObjectId.ToString("X8"), r => r);
                var rows = p.Fake.Timeline.Rows;
                for (; rowsRead < rows.Count; rowsRead++)
                    if (rows[rowsRead] is var row && row["kind"] == "ability" && limitBreaks.Keys.Any(id => row["id"] == $"{id}"))
                        fired.Add((p.Time, seats[row["actor"]]));
                if (p.Crossed(25.5f) || p.Crossed(32.2f)) bars.Add(p.World.Party.LimitBreak.FilledBars);
            },
        });

        Assert.That(run.Passed, run.ToString);
        var expected = new[] { (25.3f, MeleeDpsA), (32f, MeleeDpsB) }.Where(f => f.Item2 != player).ToArray();
        Assert.That(fired.Select(f => f.Role), Is.EqualTo(expected.Select(f => f.Item2)));
        Assert.That(fired.Select(f => f.Time), Is.EqualTo(expected.Select(f => f.Item1)).Within(0.05f));
        Assert.That(bars, Is.EqualTo(new[] { 3, 3 }));
    }

    internal static ScenarioRun Run(PartyRole player, int first, bool clockwise, float step, Action<ScenarioProbe> probe, float? stopAt = null)
        => ScenarioRun.Execute(typeof(TopP6CosmoDive2Scenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = player,
            StopAt = stopAt,
            Overrides = o => Pin((TopP6CosmoDive2StateOverrides)o, first, clockwise, step),
            Probe = probe,
        });

    internal static void Pin(TopP6CosmoDive2StateOverrides o, int first, bool clockwise, float step)
    {
        o.FirstExaflare = first;
        o.Clockwise = clockwise;
        o.ExaflareStep = step;
    }

    internal static TopP6CosmoDive2State State(int first, bool clockwise)
        => new(new Rng(1), new TopP6CosmoDive2StateOverrides { FirstExaflare = first, Clockwise = clockwise });

    // Positive when `spot` is left of the line through the centre along `facing`.
    private static float LeftOf(Vector2 facing, Vector2 spot) => facing.Y * spot.X - facing.X * spot.Y;

    internal static float Distance(Vector2 a, Vector2 b) => Vector2.Distance(a, b);

    internal static Vector2 Flat(Vector3 position) => new(position.X, position.Z);
}
