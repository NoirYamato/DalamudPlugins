using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea.P1LimitCut;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using static AnoMech.Tests.TeaLane;
using static AnoMech.Tests.TeaP1LimitCutScenarioTests;
using Stage = AnoMech.Tests.TeaP1LimitCutStage;

namespace AnoMech.Tests;

// The strat's spots, written from its rule rather than its code (x east, z south; bearings clockwise from north).
// Numbers 1, 2, 5, 6 take the first pair's blaster in the west-to-north-east half and 3, 4, 7, 8 the opposite one;
// a group's spot k is 45k degrees clockwise of its first. On a spot at bearing b the pair hit next stands 14 y out,
// the odd at b - 2 facing clockwise along the rim and the even at b - 30, and the group's other pair 19.3 y out at
// b - 4 and b - 8. The first run from the middle aims further behind the spot: b - 12 for the odds and b - 16 for
// the outer even.
//
//   0.5   everyone a yalm from the middle, by role.           8.1  out to spot 0, then on with each pair (10.3, 12.5).
//   group A (1 2 5 6)  spot 2: #1 cleaved 15.4, #2 charged 16.9.   spot 5: #5 at 24.8, #6 at 26.3.   then spot 6.
//   group B (3 4 7 8)  spot 3: #3 cleaved 20.1, #4 charged 21.6.   spot 6: #7 at 29.4, #8 at 30.9.
//   A cleaved odd walks on at once, onto the outer lane. 31.0  everyone 2.5 y from the middle for J Kick (34.1).
public class TeaP1LimitCutLpduAiTests
{
    internal const int LpduStrat = 0;
    private const float Tolerance = 0.45f;

    internal static readonly PartyRole[] ExampleNumbers = Stage.Numbers;

    internal static readonly float[] HawkBlasterPairsAt = [8.0f, 10.2f, 12.4f, 14.6f, 19.0f, 21.2f, 23.5f, 25.7f];
    internal static readonly float[] HawkBlasterMiddlesAt = [16.8f, 27.9f];
    internal static readonly float[] AlphaSwordsAt = [15.4f, 20.1f, 24.8f, 29.4f];
    internal static readonly float[] CruiseChaserLandsAt = AlphaSwordsAt.Select(at => at - P1Duration.CruiseChaserLandsBeforeAlphaSword).ToArray();
    internal static readonly float[] SuperBlasstyChargesAt = [16.9f, 21.6f, 26.3f, 30.9f];

    internal const float WaitRadius = 1f, JKickRadius = 2.5f;
    private static readonly PartyRole[] ClockwiseFromNorthInTheMiddle =
        [MainTank, MeleeDpsB, ShieldHealer, CasterDps, OffTank, PhysRangedDps, RegenHealer, MeleeDpsA];

    internal static Vector2 InTheMiddle(PartyRole role, float radius)
        => Stage.OnRing(45f * Array.IndexOf(ClockwiseFromNorthInTheMiddle, role), radius);

    // Where the first run from the middle aims.
    internal static Vector2 RunOut(Stage stage, int number) => number switch
    {
        2 or 4 => stage.Spot(number, InnerEven, 0),
        1 or 3 => Stage.OnRing(stage.GroupBearing(number) - 12f, Stage.InnerRadius),
        5 or 7 => Stage.OnRing(stage.GroupBearing(number) - 12f, Stage.OuterRadius),
        _ => Stage.OnRing(stage.GroupBearing(number) - 16f, Stage.OuterRadius),
    };

    internal static float HitAt(int number) => (number % 2 == 1 ? AlphaSwordsAt : SuperBlasstyChargesAt)[(number - 1) / 2];

    // A push has moved its target well over a yalm by then; a cleaved odd only walks on 0.1 s after its hit.
    private const float JustAfterAHit = 0.08f;

    internal static void Pin(TeaP1LimitCutStateOverrides overrides, IReadOnlyList<PartyRole> numbers, TeaP1FirstBlasters axis,
                             float? landsBehind = null, Automarkers markers = Automarkers.Off)
    {
        overrides.Numbers = numbers;
        overrides.FirstBlasters = axis;
        overrides.CruiseChaserLandsBehind = landsBehind;
        overrides.Automarkers = markers;
    }

    internal static Vector2 Flat(Vector3 position) => new(position.X, position.Z);

    internal static float AngleBetween(float a, float b)
    {
        var difference = (a - b) % MathF.Tau;
        if (difference > MathF.PI) difference -= MathF.Tau;
        if (difference < -MathF.PI) difference += MathF.Tau;
        return MathF.Abs(difference);
    }

    // Only numbers that have arrived: the others are still walking then.
    private static readonly (float At, int Number, TeaLane Lane, int Spot)[] Stands =
    [
        (14.5f, 1, InnerOdd, 2), (14.5f, 2, InnerEven, 2), (14.5f, 3, InnerOdd, 2), (14.5f, 4, InnerEven, 2),
        (15.4f, 1, InnerOdd, 2), (15.4f, 2, InnerEven, 2), (15.4f, 5, OuterOdd, 2), (15.4f, 6, OuterEven, 2),
        (16.9f, 2, InnerEven, 2), (16.9f, 5, OuterOdd, 2), (16.9f, 6, OuterEven, 2), (16.9f, 3, InnerOdd, 3), (16.9f, 4, InnerEven, 3),
        (20.1f, 3, InnerOdd, 3), (20.1f, 4, InnerEven, 3), (20.1f, 7, OuterOdd, 3), (20.1f, 8, OuterEven, 3),
        (21.6f, 4, InnerEven, 3), (21.6f, 8, InnerEven, 4),
        (24.8f, 5, InnerOdd, 5), (24.8f, 6, InnerEven, 5), (24.8f, 1, OuterOdd, 5), (24.8f, 2, OuterEven, 5),
        (26.3f, 6, InnerEven, 5), (26.3f, 1, OuterOdd, 5), (26.3f, 2, OuterEven, 5), (26.3f, 7, InnerOdd, 6), (26.3f, 8, InnerEven, 6),
        (29.4f, 7, InnerOdd, 6), (29.4f, 8, InnerEven, 6), (29.4f, 3, OuterOdd, 6), (29.4f, 4, OuterEven, 6),
        (29.4f, 1, OuterOdd, 6), (29.4f, 5, OuterOdd, 6), (29.4f, 2, OuterEven, 6), (29.4f, 6, OuterEven, 6),
        (30.9f, 7, OuterOdd, 6), (30.9f, 8, InnerEven, 6), (30.9f, 3, OuterOdd, 6), (30.9f, 4, OuterEven, 6),
        (30.9f, 1, OuterOdd, 6), (30.9f, 5, OuterOdd, 6), (30.9f, 2, OuterEven, 6), (30.9f, 6, OuterEven, 6),
    ];

    private const float OnTheWaitRing = 7.9f, RunningOut = 10.0f, JKick = 34.1f, OnTheJKickRing = 34.6f;

    private static readonly float[] ReadAt =
        Stands.Select(s => s.At).Append(OnTheWaitRing).Append(JKick).Append(OnTheJKickRing).Distinct().Order().ToArray();

    [Test]
    public void StandsOnTheStratsSpots([Values] TeaP1FirstBlasters axis)
        => AssertStands(ExampleNumbers, axis, MainTank, Random.Shared.Next());

    [Test]
    public void GroupsGoByNumberWhateverTheSeat()
    {
        for (var seed = 1; seed <= 20; seed++)
        {
            var rng = new Random(seed);
            var numbers = PerRole.All.OrderBy(_ => rng.Next()).ToArray();
            AssertStands(numbers, (TeaP1FirstBlasters)rng.Next(4), PerRole.All[seed % 8], seed);
        }
    }

    private static void AssertStands(PartyRole[] numbers, TeaP1FirstBlasters axis, PartyRole player, int seed)
    {
        var stage = new Stage(axis);
        var seen = Play(numbers, axis, player, seed, ReadAt, out var run);
        var context = $"numbers {string.Join(" ", numbers)}, first blasters {axis}, player {player}, seed {seed}";

        Assert.That(run.Passed, () => $"{run}{Environment.NewLine}{context}");
        Assert.Multiple(() =>
        {
            foreach (var role in PerRole.All)
            {
                Assert.That(Vector2.Distance(seen[OnTheWaitRing][role].Position2, InTheMiddle(role, WaitRadius)), Is.LessThan(Tolerance),
                            $"{role} waits in the middle ({context})");
                Assert.That(seen[JKick][role].Position2.Length(), Is.LessThan(3f), $"{role} at J Kick ({context})");
                Assert.That(Vector2.Distance(seen[OnTheJKickRing][role].Position2, InTheMiddle(role, JKickRadius)), Is.LessThan(Tolerance),
                            $"{role} after J Kick ({context})");
            }
            foreach (var (at, number, lane, spot) in Stands)
            {
                var stands = seen[at][numbers[number - 1]].Position2;
                Assert.That(Vector2.Distance(stands, stage.Spot(number, lane, spot)), Is.LessThan(Tolerance),
                            $"#{number} at {stands} at {at}s, expected {lane} of spot {spot} at {stage.Spot(number, lane, spot)} ({context})");
            }
        });
    }

    // The straight run along the spot's own bearing passes inside the second pair's blast; the spots it aims at
    // instead are only passed.
    [Test]
    public void TheFirstRunFromTheMiddleAimsWellBehindTheFirstBlaster([Values] TeaP1FirstBlasters axis)
    {
        var stage = new Stage(axis);
        var seen = Play(ExampleNumbers, axis, MainTank, Random.Shared.Next(), [RunningOut], out var run);

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            for (var number = 1; number <= 8; number++)
            {
                var at = seen[RunningOut][ExampleNumbers[number - 1]].Position2;
                var from = InTheMiddle(ExampleNumbers[number - 1], WaitRadius);
                var to = RunOut(stage, number);
                Assert.That(OffTheLine(at, from, to), Is.LessThan(Tolerance), $"#{number} at {at}, running from {from} to {to}");
            }
        });
    }

    [Test]
    public void EachCleavedOddHasWalkedOnBeforeItsEvenIsCharged([Values] TeaP1FirstBlasters axis)
    {
        var stage = new Stage(axis);
        var seen = Play(ExampleNumbers, axis, MainTank, Random.Shared.Next(), SuperBlasstyChargesAt, out var run);

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            for (var hit = 0; hit < 4; hit++)
            {
                var odd = 2 * hit + 1;
                var at = seen[SuperBlasstyChargesAt[hit]][ExampleNumbers[odd - 1]].Position2;
                Assert.That(Vector2.Distance(at, stage.HitSpot(odd)), Is.GreaterThan(3f), $"#{odd} at {at} as #{odd + 1} is charged");
            }
        });
    }

    // A bot arrives facing the chord it walked, 22.5 degrees off the rim, and Cruise Chaser lands behind that.
    [TestCase(OffTank, Description = "every odd number a bot")]
    [TestCase(MeleeDpsA, Description = "the player's seat as #1")]
    [TestCase(MainTank, Description = "the player's seat as #3")]
    [TestCase(CasterDps, Description = "the player's seat as #5")]
    [TestCase(ShieldHealer, Description = "the player's seat as #7")]
    public void EachOddFacesClockwiseAlongTheRimAsCruiseChaserLandsBehindIt(PartyRole player)
    {
        foreach (var axis in Enum.GetValues<TeaP1FirstBlasters>())
        {
            var stage = new Stage(axis);
            var landings = new Dictionary<float, (Placement Odd, Placement CruiseChaser)>();
            var run = ScenarioRun.Execute(typeof(TeaP1LimitCutScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
            {
                PlayerRole = player,
                Overrides = o => Pin((TeaP1LimitCutStateOverrides)o, ExampleNumbers, axis),
                Probe = p =>
                {
                    for (var hit = 0; hit < 4; hit++)
                        if (p.Crossed(CruiseChaserLandsAt[hit]))
                            landings[CruiseChaserLandsAt[hit]] = (p.Member(ExampleNumbers[2 * hit])!.Placement(), CruiseChaser(p).Placement());
                },
            });

            Assert.That(run.Passed, run.ToString);
            Assert.Multiple(() =>
            {
                for (var hit = 0; hit < 4; hit++)
                {
                    var odd = 2 * hit + 1;
                    var (stands, cruiseChaser) = landings[CruiseChaserLandsAt[hit]];
                    var alongTheRim = Stage.ClockwiseAlongTheRim(stage.HitSpot(odd));
                    Assert.That(AngleBetween(stands.Rotation, alongTheRim), Is.LessThan(0.02f), $"#{odd}'s facing, first blasters {axis}");
                    Assert.That(Vector2.Distance(cruiseChaser.Position2, stands.Position2 - P1Geometry.CruiseChaserLandsBehind * Stage.Facing(alongTheRim)),
                                Is.LessThan(0.1f), $"Cruise Chaser behind #{odd}, first blasters {axis}");
                    Assert.That(AngleBetween(cruiseChaser.Rotation, alongTheRim), Is.LessThan(0.02f), $"Cruise Chaser's facing behind #{odd}, first blasters {axis}");
                }
            });
        }
    }

    // The middle blows at 16.8 and 27.9, and a charge that lands long reaches toward it.
    [Test]
    public void NobodyComesNearTheMiddleBetweenTheRunOutAndTheLastCharge([Values] TeaP1FirstBlasters axis)
    {
        var nearest = float.MaxValue;
        var run = ScenarioRun.Execute(typeof(TeaP1LimitCutScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            Overrides = o => Pin((TeaP1LimitCutStateOverrides)o, ExampleNumbers, axis),
            Probe = p =>
            {
                if (p.Time is < 10.3f or > 31.0f) return;
                nearest = MathF.Min(nearest, PerRole.All.Min(role => Flat(p.Member(role)!.Position).Length()));
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(nearest, Is.GreaterThan(11f));
    }

    // Bots are taken to have pressed their anti-knockback. A headless run drives the player's seat as one until a
    // test takes it over, and that seat presses nothing, so it has to be as safe from the knockbacks as they are.
    [Test]
    public void NobodyIsThrownBotOrThePlayersSeatDrivenAsOne([Range(1, 8)] int playersNumber)
    {
        foreach (var axis in Enum.GetValues<TeaP1FirstBlasters>())
        {
            var stage = new Stage(axis);
            var stands = new Dictionary<int, Vector2>();
            var run = ScenarioRun.Execute(typeof(TeaP1LimitCutScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
            {
                PlayerRole = ExampleNumbers[playersNumber - 1],
                Overrides = o => Pin((TeaP1LimitCutStateOverrides)o, ExampleNumbers, axis),
                Probe = p =>
                {
                    for (var number = 1; number <= 8; number++)
                        if (p.Crossed(HitAt(number) + JustAfterAHit)) stands[number] = Flat(p.Member(ExampleNumbers[number - 1])!.Position);
                },
            });

            Assert.That(run.Passed, run.ToString);
            Assert.Multiple(() =>
            {
                for (var number = 1; number <= 8; number++)
                    Assert.That(Vector2.Distance(stands[number], stage.HitSpot(number)), Is.LessThan(Tolerance),
                                $"#{number} at {stands[number]} just after its hit (the player's seat is #{playersNumber}, first blasters {axis})");
            });
        }
    }

    [Test]
    public void AutomarkersPutAttackOneToEightOnTheNumbersOnceAllEightShow()
    {
        var marks = Marks(Automarkers.On);

        Assert.That(marks.Keys, Is.EquivalentTo(PerRole.All));
        Assert.Multiple(() =>
        {
            Sign[] signs = [Sign.Attack1, Sign.Attack2, Sign.Attack3, Sign.Attack4, Sign.Attack5, Sign.Attack6, Sign.Attack7, Sign.Attack8];
            for (var number = 1; number <= 8; number++)
            {
                var (sign, at) = marks[ExampleNumbers[number - 1]];
                Assert.That(sign, Is.EqualTo(signs[number - 1]), $"#{number}'s sign");
                Assert.That(at, Is.EqualTo(6.3f).Within(0.05f), $"#{number}'s mark time");
            }
        });
    }

    [Test]
    public void AutomarkersAreOffUnlessAskedFor()
    {
        Assert.That(new TeaP1LimitCutStateOverrides().Automarkers, Is.EqualTo(Automarkers.Off));
        Assert.That(Marks(null), Is.Empty);
    }

    [Test]
    public void AutomarkersOnAndOffPutEveryoneOnTheSameSpots()
    {
        float[] hits = [.. AlphaSwordsAt, .. SuperBlasstyChargesAt];
        foreach (var seed in Enumerable.Range(0, 5).Select(_ => Random.Shared.Next()))
        {
            var on = Play(ExampleNumbers, TeaP1FirstBlasters.NorthSouth, MainTank, seed, hits, out var withSigns, Automarkers.On);
            var off = Play(ExampleNumbers, TeaP1FirstBlasters.NorthSouth, MainTank, seed, hits, out var without);
            Assert.That(withSigns.Passed && without.Passed, () => $"{withSigns}{Environment.NewLine}{without}");
            foreach (var at in hits)
                foreach (var role in PerRole.All)
                    Assert.That(Vector2.Distance(on[at][role].Position2, off[at][role].Position2), Is.LessThan(0.01f),
                                $"{role} at {at}s: {on[at][role].Position2} with automarkers, {off[at][role].Position2} without (seed {seed})");
        }
    }

    [Test]
    public void ItIsTheScenariosFirstAndOnlyStrat()
    {
        IScenario scenario = new TeaP1LimitCutScenario();

        Assert.That(scenario.AiStrats, Has.Count.EqualTo(1));
        Assert.That(scenario.AiStrats[LpduStrat], Is.TypeOf<TeaP1LimitCutLpduAi>());
        Assert.That((scenario.AiStrats[LpduStrat].Name, scenario.AiStrats[LpduStrat].Group), Is.EqualTo(("[LPDU] Light PF", "EU")));
    }

    // Everyone's placement on the frame the clock passes each of `times`.
    private static Dictionary<float, Dictionary<PartyRole, Placement>> Play(PartyRole[] numbers, TeaP1FirstBlasters axis, PartyRole player, int seed,
                                                                            float[] times, out ScenarioRun run, Automarkers markers = Automarkers.Off)
    {
        var seen = new Dictionary<float, Dictionary<PartyRole, Placement>>();
        run = ScenarioRun.Execute(typeof(TeaP1LimitCutScenario), LpduStrat, seed, new ScenarioRunOptions
        {
            PlayerRole = player,
            Overrides = o => Pin((TeaP1LimitCutStateOverrides)o, numbers, axis, markers: markers),
            Probe = p =>
            {
                foreach (var at in times)
                    if (p.Crossed(at)) seen[at] = PerRole.All.ToDictionary(role => role, role => p.Member(role)!.Placement());
            },
        });
        return seen;
    }

    // The sign each member was marked with, and when; rows with no target are the clears. Null leaves the setting
    // as the scenario has it.
    private static Dictionary<PartyRole, (Sign Sign, float At)> Marks(Automarkers? markers)
    {
        var marks = new Dictionary<PartyRole, (Sign, float)>();
        var run = ScenarioRun.Execute(typeof(TeaP1LimitCutScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            StopAt = 7.5f,
            Overrides = o =>
            {
                var overrides = (TeaP1LimitCutStateOverrides)o;
                overrides.Numbers = ExampleNumbers;
                if (markers is { } asked) overrides.Automarkers = asked;
            },
            Probe = p =>
            {
                if (!p.Crossed(7.4f)) return;
                var roleById = PerRole.All.ToDictionary(r => p.Member(r)!.GameObjectId.ObjectId.ToString("X8"), r => r);
                foreach (var row in p.Fake.Timeline.Rows.Where(r => r["kind"] == "ac_target" && r["id"] == "0x1F6" && r["target"] != ""))
                    marks[roleById[row["target"]]] = ((Sign)Convert.ToUInt32(row["p"].Split(',')[0], 16),
                                                     float.Parse(row["t"], CultureInfo.InvariantCulture));
            },
        });

        Assert.That(run.Passed, run.ToString);
        return marks;
    }

    private static float OffTheLine(Vector2 at, Vector2 from, Vector2 to)
    {
        var along = Vector2.Normalize(to - from);
        var walked = Math.Clamp(Vector2.Dot(at - from, along), 0f, Vector2.Distance(from, to));
        return Vector2.Distance(at, from + walked * along);
    }
}
