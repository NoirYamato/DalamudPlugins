using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P6Gauntlet;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// LPDU's spots in the Gauntlet's Exaflares 2 part on its own (x east, z south; bearings clockwise from north). θ0 is the
// first exaflare's compass point, s = +1 when the other three follow it clockwise and -1 counter-clockwise. Every dodge
// spot is on r 14. Up to the hold the bots play Unlimited Wave Cannon as in Exaflares 1. With every job its seat's
// preset, the first melee (the enrage holder) is M1's dragoon and the second M2's monk.
//
//    0.0  on the autos' spots, where a run starting here puts everyone: MT inside north (0, -5), OT outside east
//         (14.5, 0), the rest inside south (0, 6).
//    2.0  Unlimited Wave Cannon's cast (1.9): out of the hitbox to watch the arrows, MT (0, -14), the rest (0, 14);
//         the OT is out already.
//    8.0  the second arrow has shown (7.4): everyone into the middle, where the first puddles drop (11.9).
//   12.9  out to θ0 - 45s, beside the first exaflare away from the second; the second puddles drop on the way (13.7).
//   16.2  the first exaflare has gone past at r 8 (16.1 with a 1.1 s step): on to θ0. Puddles 3-6 drop at 15.6, 17.5,
//         19.4 and 21.4.
//   17.9 / 19.8  on to θ0 + 45s, then θ0 + 90s.
//   21.5  the six hold on θ0 + 135s, where the last exaflare started; the tanks cross the boss into a Y at r 6.5, the
//         MT 135° and the OT 225° clockwise from the party's bearing: left and right of the far line as the party
//         faces the boss.
//   25.3  the first melee presses its limit break 3 on Alpha Omega right after the Cosmo Dive bar (24.9); its 4.5 s cast
//         holds it on the hold until 29.8.
//   28.0  after the dive (27.6): back to the auto spots, but the OT walks out to (16, 0), past the hold's r 14 so a melee
//         still casting there is never the farthest, or to (-16, 0) when the hold is within 5.5 y of that, clear of
//         its Flash Gale; the second melee goes to the middle instead and the MT makes room for it, back to (0, -7), so
//         it is out of the MT's Flash Gale.
//   32.0  the second melee presses its limit break 3 from the middle, once the first's has landed and refilled the
//         gauge; while the first's hasn't, it waits for it, until the first's window closes (36.1).
public class TopP6GauntletExaflares2LpduAiTests
{
    internal const int LpduStrat = 0;
    internal const float UnlimitedWaveCannonAt = 1.9f;
    internal const float RunSpeed = 6.5f;
    internal const float FollowRadius = 14f, TankRadius = 6.5f;
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

    internal static readonly Vector2 OffTankEastAfterTheDive = new(16f, 0f);
    internal static readonly Vector2 OffTankWestAfterTheDive = new(-16f, 0f);

    // The second melee in the middle; the off-tank out at r 16, west when the hold is east, 2 y from its east spot.
    internal static Dictionary<PartyRole, Vector2> AutoSpotsAfterTheDive(int first, bool clockwise, PartyRole secondMelee = MeleeDpsB)
        => PerRole.All.ToDictionary(r => r, r => r switch
        {
            MainTank => new Vector2(0f, -7f),
            OffTank => Wave(first, clockwise).Bearing(3) == 90f ? OffTankWestAfterTheDive : OffTankEastAfterTheDive,
            _ when r == secondMelee => Vector2.Zero,
            _ => AutoSpots[r],
        });

    internal static readonly float[] MovesAt = [0f, 2f, 8f, 12.9f, 16.2f, 17.9f, 19.8f, 21.5f, 28f];

    // The first melee's 4.5 s limit break 3, pressed right after the Cosmo Dive bar; it walks on as it resolves.
    internal const float FirstMeleePresses = 25.3f, FirstMeleeResolves = 29.8f;
    internal const float SecondMeleePresses = 32f, SecondMeleeResolves = 36.5f;

    // Read off the scenario's own exaflares: the k-th starts on wave.Bearing(k).
    internal static TopP6UnlimitedWaveCannon Wave(int first, bool clockwise, float step = 1.1f)
        => new(first, clockwise, UnlimitedWaveCannonAt, step);

    internal static Vector2 OnTheDodgeRing(float bearing) => Flat(TopP6AlphaOmega.AtBearing(bearing, FollowRadius));

    internal static Vector2 StartPoint(TopP6UnlimitedWaveCannon wave) => Flat(TopP6AlphaOmega.AtBearing(wave.Bearing(0) - 45f * wave.Turn, FollowRadius));

    // Each move's spot for every seat, written from the rule.
    internal static Dictionary<PartyRole, Vector2> Expected(float move, int first, bool clockwise)
    {
        var wave = Wave(first, clockwise);
        var hold = wave.Bearing(3);
        Dictionary<PartyRole, Vector2> Everyone(Vector2 spot) => PerRole.All.ToDictionary(r => r, _ => spot);
        return move switch
        {
            0f => AutoSpots,
            28f => AutoSpotsAfterTheDive(first, clockwise),
            2f => SteppedOut,
            8f => Everyone(Vector2.Zero),
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
            Assert.That(Distance(TopP6GauntletLpduAi.PartyDiveHold(state), new Vector2(holdX, holdZ)), Is.LessThan(0.01f), "hold");
            Assert.That(Distance(TopP6GauntletLpduAi.MainTankDiveSpot(state), new Vector2(mtX, mtZ)), Is.LessThan(0.01f), "MT");
            Assert.That(Distance(TopP6GauntletLpduAi.OffTankDiveSpot(state), new Vector2(otX, otZ)), Is.LessThan(0.01f), "OT");
            Assert.That(Distance(StratsStartPoint(first, clockwise), Flat(TopP6AlphaOmega.AtBearing(start, FollowRadius))),
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
                Assert.That(Distance(StratsStartPoint(first, clockwise), Expected(12.9f, first, clockwise)[MainTank]),
                            Is.LessThan(0.01f), $"{why}: start");
                for (var k = 0; k < 3; k++)
                    Assert.That(Distance(TopP6LpduAi.OnTheExaflarePath(first, clockwise, k), Expected(MovesAt[4 + k], first, clockwise)[MainTank]),
                                Is.LessThan(0.01f), $"{why}: exaflare {k}'s point");
                Assert.That(Distance(TopP6GauntletLpduAi.PartyDiveHold(state), Expected(21.5f, first, clockwise)[RegenHealer]),
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
                var party = TopP6GauntletLpduAi.PartyDiveHold(state);
                var mainTank = TopP6GauntletLpduAi.MainTankDiveSpot(state);
                var offTank = TopP6GauntletLpduAi.OffTankDiveSpot(state);
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

    // Every bot sets off for its spot (within the AI's jitter) as each move fires; the first melee, held on the hold by
    // its limit break 3's cast through the move after the dive, sets off for its spot as the cast ends.
    [Test]
    public void SetsOffForTheStratsSpotAtEveryMove([Values(0, 3, 5, 6)] int first, [Values] bool clockwise)
    {
        var problems = new List<string>();
        var player = PerRole.All[(first + (clockwise ? 1 : 0)) % 8];
        var run = Run(player, first, clockwise, 1.1f, p =>
        {
            foreach (var move in MovesAt)
                foreach (var role in PerRole.All.Where(r => r != player))
                {
                    var setsOff = move == 28f && role == MeleeDpsA ? FirstMeleeResolves : move;
                    if (!p.Crossed(setsOff + 0.05f)) continue;
                    var member = p.Member(role)!;
                    var heading = Flat(member.MoveDestination ?? member.Position);
                    var off = Distance(heading, Expected(move, first, clockwise)[role]);
                    if (off > Jitter + 0.02f) problems.Add($"{role} set off at {setsOff} for {heading}, {off:F2} y off its spot");
                }
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems));
    }

    // The first melee stands still on the hold through its limit break 3's cast, then walks on.
    [Test]
    public void TheFirstMeleeHoldsStillWhileItsLimitBreakCasts()
    {
        var seen = new Dictionary<float, Vector2>();
        float[] times = [FirstMeleePresses + 0.1f, 28.1f, FirstMeleeResolves - 0.05f];
        var run = Run(MainTank, 2, true, 1.1f, p =>
        {
            foreach (var at in times)
                if (p.Crossed(at)) seen[at] = Flat(p.Member(MeleeDpsA)!.Position);
        }, stopAt: FirstMeleeResolves + 0.5f);

        Assert.That(run.Passed, run.ToString);
        Assert.That(seen.Values.Select(at => Distance(at, seen[times[0]])), Has.All.LessThan(0.01f), "still while casting");
        Assert.That(Distance(seen[times[0]], Expected(21.5f, 2, true)[MeleeDpsA]), Is.LessThan(Tolerance), "on the hold");
    }

    // Settled: before the first exaflare arrow, in the middle, at the start point, at the dive, as the second melee
    // presses its limit break (the first, held by its own cast until 29.8, still on its way to its spot) and at the autos.
    [TestCase(6.0f, 2f)]
    [TestCase(11.8f, 8f)]
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
                foreach (var role in PerRole.All.Where(r => r != player && !(r == MeleeDpsA && at < 33f && move == 28f)))
                {
                    var off = Distance(Flat(p.Member(role)!.Position), Expected(move, first, clockwise)[role]);
                    if (off > Tolerance) problems.Add($"first {first}, clockwise {clockwise}: {role} stood {off:F2} y off its spot at {at}");
                }
            }, stopAt: at + 0.1f);
            if (!run.Passed) problems.Add(run.ToString());
        }

        Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems));
    }

    // After the dive the off-tank stands 2 y farther out than the party's hold (r 14), so a melee still casting there is
    // never the farthest for Flash Gale. When the last exaflare started east, the hold (14, 0) is 2 y from the off-tank's
    // east spot (16, 0): it takes the west one instead, so that melee also stays out of its Flash Gale; on every other roll
    // it keeps the east one, at least 11.6 y from the hold.
    [Test]
    public void TheOffTankTakesTheWestSpotWhenTheHoldIsEast()
    {
        Assert.Multiple(() =>
        {
            foreach (var (first, clockwise) in Patterns)
            {
                var state = State(first, clockwise);
                var hold = TopP6GauntletLpduAi.PartyDiveHold(state);
                var spot = TopP6GauntletLpduAi.AutoSpotAfterTheDive(OffTank, state);
                var east = Wave(first, clockwise).Bearing(3) == 90f;
                Assert.That(spot, Is.EqualTo(east ? OffTankWestAfterTheDive : OffTankEastAfterTheDive), $"first {first}, clockwise {clockwise}");
                Assert.That(Distance(spot, hold), Is.GreaterThanOrEqualTo(east ? 30f : 11.6f), $"first {first}, clockwise {clockwise}");
                Assert.That(spot.Length() - hold.Length(), Is.EqualTo(2f).Within(0.01f), "farther out than the hold");
            }
        });
    }

    // The human first melee presses late in its window (31.1, G 156.0) and stays on the hold, 0.8 y outward of it, through
    // both Flash Gales after the dive (35.3 and 38.5): the off-tank is still the farthest at each, and its Flash Gale is
    // clear of the hold, with the hold east (the off-tank west), west or south-east.
    [TestCase(5, false)]
    [TestCase(3, true)]
    [TestCase(0, true)]
    public void AFirstMeleeStillOnTheHoldOutlastsBothFlashGales(int first, bool clockwise)
    {
        const float DiveHits = 27.6f, PressesAt = 31.1f;
        float[] flashGales = [35.3f, 38.5f];
        var hold = OnTheDodgeRing(Wave(first, clockwise).Bearing(3));
        var leads = new List<float>();
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MeleeDpsA,
            StopAt = flashGales[^1] + 0.2f,
            WriteArtifactsOnFailure = false,
            Overrides = o =>
            {
                var overrides = (TopP6GauntletStateOverrides)o;
                Pin(overrides, first, clockwise, 1.1f);
                overrides.PlayerPressesItsOwnLimitBreaks = true;
            },
            Takeovers = [new Takeover(DiveHits + 0.1f, hold * ((FollowRadius + 0.8f) / FollowRadius))],
            Probe = p =>
            {
                if (p.Crossed(0f))
                    TopP6HumanLimitBreak.Press(p.World, PressesAt, TopP6GauntletRun.LimitBreak3(MeleeDpsA), () => TopP6GauntletRun.Scenario(p).AlphaOmega?.Enemy);
                foreach (var flashGale in flashGales.Where(at => p.Crossed(at - 0.05f)))
                    leads.Add(Flat(p.Member(OffTank)!.Position).Length() - Flat(p.Member(MeleeDpsA)!.Position).Length());
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(leads, Has.Count.EqualTo(2).And.All.GreaterThan(0.5f), "the off-tank's lead over the melee on the hold");
    }

    // The arrows only show 4.5 s into each exaflare's cast, so until the second has shown (7.4) nobody may give the
    // pattern away: the same seed's seats stand on the same spots, frame by frame, whatever the roll, up to the move
    // out of the middle (12.9).
    [Test]
    public void NothingDependsOnTheRollBeforeTheSecondArrowShows()
    {
        const float OutOfTheMiddle = 12.9f;
        var seed = Random.Shared.Next();
        var runs = new[] { (0, true), (5, false), (7, true) }.Select(pattern =>
        {
            var seen = new List<Vector2>();
            var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, seed, new ScenarioRunOptions
            {
                PlayerRole = MainTank,
                StopAt = OutOfTheMiddle + 0.05f,
                Overrides = o => Pin((TopP6GauntletStateOverrides)o, pattern.Item1, pattern.Item2, 1.1f),
                Probe = p =>
                {
                    if (p.Time < OutOfTheMiddle) seen.AddRange(PerRole.All.Select(r => Flat(p.Member(r)!.Position)));
                },
            });
            Assert.That(run.Passed, run.ToString);
            return seen;
        }).ToList();

        Assert.That(runs[0], Is.Not.Empty);
        Assert.That(runs[1], Is.EqualTo(runs[0]).Using<Vector2>((a, b) => Distance(a, b) < 0.01f), $"seed {seed}: first 5, counter-clockwise");
        Assert.That(runs[2], Is.EqualTo(runs[0]).Using<Vector2>((a, b) => Distance(a, b) < 0.01f), $"seed {seed}: first 7, clockwise");
    }

    // Whatever "Require tank mitigation" says: M1's limit break 3 right after the Cosmo Dive bar, M2's once the first has
    // refilled the gauge, each cast for 4.5 s on Alpha Omega; a bot's shows its cast and its ActionEffect, a debug-driven
    // player's seat only takes effect. The gauge is full from the rebuild before the first and full again after each.
    [TestCase(MainTank)]
    [TestCase(MeleeDpsA)]
    public void TheMeleeLimitBreaksGoOffAsThePlanSays(PartyRole player)
    {
        var fired = new List<(float Time, PartyRole Role, string Target)>();
        var bars = new List<int>();
        Dictionary<string, PartyRole>? seats = null;
        var rowsRead = 0;
        Dictionary<uint, PartyRole>? limitBreaks = null;
        IReadOnlyList<TopP6LimitBreakPress> presses = [];
        string? alphaOmega = null;
        var run = ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = player,
            Overrides = o => Pin((TopP6GauntletStateOverrides)o, 3, false, 1.1f),
            Probe = p =>
            {
                limitBreaks ??= new[] { MeleeDpsA, MeleeDpsB }.ToDictionary(r => LimitBreakHandler.ActionId(PartyPresets.Standard[(int)r].ClassJob, 3), r => r);
                seats ??= PerRole.All.ToDictionary(r => p.Member(r)!.GameObjectId.ObjectId.ToString("X8"), r => r);
                alphaOmega ??= TopP6GauntletRun.Scenario(p).AlphaOmega?.Enemy?.GameObjectId.ObjectId.ToString("X8");
                presses = TopP6GauntletRun.LimitBreaks(p).Presses;
                var rows = p.Fake.Timeline.Rows;
                for (; rowsRead < rows.Count; rowsRead++)
                    if (rows[rowsRead] is var row && row["kind"] == "ability" && limitBreaks.Keys.Any(id => row["id"] == $"{id}"))
                        fired.Add((p.Time, seats[row["actor"]], row["target"]));
                if (p.Crossed(25.2f) || p.Crossed(29.9f) || p.Crossed(31.9f) || p.Crossed(36.6f)) bars.Add(p.World.Party.LimitBreak.FilledBars);
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(presses.Select(r => (r.Seat, r.ByHuman, r.Level, r.Kind)),
                    Is.EqualTo(new[] { (MeleeDpsA, false, 3, TopP6LimitBreakKind.Melee), (MeleeDpsB, false, 3, TopP6LimitBreakKind.Melee) }));
        Assert.That(presses.Select(r => r.CastStartedAt), Is.EqualTo(new[] { FirstMeleePresses, SecondMeleePresses }).Within(0.05f));
        Assert.That(presses.Select(r => r.ResolvedAt), Is.EqualTo(new[] { FirstMeleeResolves, SecondMeleeResolves }).Within(0.05f));
        var expected = new[] { (FirstMeleeResolves, MeleeDpsA), (SecondMeleeResolves, MeleeDpsB) }.Where(f => f.Item2 != player).ToArray();
        Assert.That(fired.Select(f => f.Role), Is.EqualTo(expected.Select(f => f.Item2)), "a bot's ActionEffect");
        Assert.That(fired.Select(f => f.Time), Is.EqualTo(expected.Select(f => f.Item1)).Within(0.05f));
        Assert.That(fired.Select(f => f.Target), Has.All.EqualTo(alphaOmega), "aimed at Alpha Omega");
        Assert.That(bars, Is.EqualTo(new[] { 3, 3, 3, 3 }), "before and after each");
    }

    internal static ScenarioRun Run(PartyRole player, int first, bool clockwise, float step, Action<ScenarioProbe> probe, float? stopAt = null)
        => ScenarioRun.Execute(typeof(TopP6GauntletScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = player,
            StopAt = stopAt,
            Overrides = o => Pin((TopP6GauntletStateOverrides)o, first, clockwise, step),
            Probe = probe,
        });

    internal static void Pin(TopP6GauntletStateOverrides o, int first, bool clockwise, float step)
    {
        TopP6GauntletRun.Only(o, TopP6GauntletPart.Exaflares2);
        o.Exaflares2First = first;
        o.Exaflares2Clockwise = clockwise;
        o.Exaflares2Step = step;
    }

    internal static TopP6GauntletState State(int first, bool clockwise)
    {
        var overrides = new TopP6GauntletStateOverrides { Exaflares2First = first, Exaflares2Clockwise = clockwise };
        TopP6GauntletRun.Only(overrides, TopP6GauntletPart.Exaflares2);
        return new(new Rng(1), new SimParty(), overrides);
    }

    // Where the strat starts the dodge: the shared path's point beside the first exaflare.
    private static Vector2 StratsStartPoint(int first, bool clockwise)
        => TopP6LpduAi.OnTheExaflarePath(first, clockwise, TopP6LpduAi.BesideTheFirstExaflareAwayFromTheSecond);

    // Positive when `spot` is left of the line through the centre along `facing`.
    private static float LeftOf(Vector2 facing, Vector2 spot) => facing.Y * spot.X - facing.X * spot.Y;

    internal static float Distance(Vector2 a, Vector2 b) => Vector2.Distance(a, b);

    internal static Vector2 Flat(Vector3 position) => new(position.X, position.Z);
}
