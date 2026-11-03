using System.Globalization;
using System.Numerics;
using System.Text.Json;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Multiplayer;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P5Delta;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;

namespace AnoMech.Tests;

// The LPDU strat, in the eye frame: x east, z south, the eye rel N, the beetle rel W (-20, 0), the Final rel E.
// Eye S is the same layout turned 180 degrees. Positions 0-7 are the strat's own order: inner blue N/S, outer
// blue N/S, inner green N/S, outer green N/S. Per colour the only pair holding a healer stands inner; a tie
// (both pairs or neither) keeps the roll's inner pair, and so does a colour a seat forced an inner or outer
// band in. Spots below assume Omega's monitor spares the south (m = +1) and the Swivel Cannon spares the south
// (c = +1); each stage mirrors z by its own sign.
//
//   11.0  line-up: blues (-8.6, -+4) inner, (-13.5, -+2.5) outer; greens (6.2, -+4) inner, (11.3, -+9.3) outer.
//   19.5  the inner pair of a colour swaps N/S when its N member's fist matches the outer N member's; the inner
//         blues stretch to (-13.5, -+6.5), and each green stacks with the other green of its side on the
//         intercards (9.64, -+9.64).
//   28.1  the inner blue tether pops at once; 28.3 the inner blues drop onto the outer blues.
//   30.3  inner blues to (0, -+5) for Beyond Defense; outer blues the rel NW / SW arms (their tether pops on the
//         way, about 30.7-31.0), inner greens the N / S arms, outer greens the NE / SE arms, each at r 19.5,
//         0.15 rad off the arm on the side away from its turn.
//   35.7  greens stage at (0, -+12) / (9, -+10), inside every first pulse; 38.0 on to their monitor corners
//         (-9.3, -+13) inner / (9.3, -+13) outer.
//   36.2  blues: the monitor holder (0, 5.1), the plain blues (-0.4 / 0.4 / 0, 3.2), the Beyond Defense target
//         13 y toward the Final from its own spot; 40.0 the holder steps 1.2 y east or west so its cleave
//         points away from the centre.
//   46.0  Hello Near (0, 6), Hello Distant (0, 18.5), the other blues (9.5, 16); the safe-side inner green
//         (-9.64, 9.64), the unsafe-side one waits at (-9.6, -3.4) and at 48.0 collapses to (-9.6, 4); the
//         safe-side outer green plants at (15.5, 10.7), the other goes under the beetle (-18.5, 1.5).
//   56.2  back to the boss; the outer greens at max melee (8.8, -0.5) plant / (-8.8, -0.5); at 61.2 they walk
//         together; at 62.0 the MT goes forward to (0, -5.7).
public class TopP5DeltaLpduAiTests
{
    internal const int LpduStrat = 0;
    internal const int NaStrat = 1;
    private const float Tolerance = 0.45f;

    internal const uint Yellow = BNpcBaseId.RocketPunchYellow;
    internal const uint Blue = BNpcBaseId.RocketPunchBlue;

    private static readonly ArmRotation Cw = ArmRotation.Clockwise;
    private static readonly ArmRotation Ccw = ArmRotation.CounterClockwise;

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

    public enum Request { Nothing, BlueOuterSeat, BlueInnerSeat, BeyondDefenceYes, GreenOuterSeat, GreenInnerSeat, AnyBands }

    // Rolls are written blue slots 0-3 then green 4-7, each colour's inner pair first; so is the line-up.
    private static IEnumerable<TestCaseData> InnerPairCases()
    {
        PartyRole[] healersRolledOutside = [MainTank, OffTank, RegenHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, ShieldHealer, CasterDps];
        PartyRole[] healersInside = [RegenHealer, MeleeDpsA, MainTank, OffTank, ShieldHealer, CasterDps, MeleeDpsB, PhysRangedDps];
        yield return Case("one healer pair per colour, rolled outside", healersRolledOutside, Request.Nothing, healersInside);
        yield return Case("one healer pair per colour, rolled inside", healersInside, Request.Nothing, healersInside);
        yield return Case("both healers in the outer blue pair, no healer in green",
            [MainTank, OffTank, RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps], Request.Nothing,
            [RegenHealer, ShieldHealer, MainTank, OffTank, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps]);
        yield return Case("a healer in each blue pair is a tie",
            [MainTank, RegenHealer, OffTank, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps], Request.Nothing,
            [MainTank, RegenHealer, OffTank, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps]);
        yield return Case("a healer in each green pair is a tie",
            [MainTank, OffTank, MeleeDpsA, MeleeDpsB, PhysRangedDps, RegenHealer, ShieldHealer, CasterDps], Request.Nothing,
            [MainTank, OffTank, MeleeDpsA, MeleeDpsB, PhysRangedDps, RegenHealer, ShieldHealer, CasterDps]);
        yield return Case("a forced outer blue keeps the blue roll", healersRolledOutside, Request.BlueOuterSeat,
            [MainTank, OffTank, RegenHealer, MeleeDpsA, ShieldHealer, CasterDps, MeleeDpsB, PhysRangedDps]);
        yield return Case("a forced inner blue keeps the blue roll", healersRolledOutside, Request.BlueInnerSeat,
            [MainTank, OffTank, RegenHealer, MeleeDpsA, ShieldHealer, CasterDps, MeleeDpsB, PhysRangedDps]);
        yield return Case("Beyond Defence Yes keeps the blue roll", healersRolledOutside, Request.BeyondDefenceYes,
            [MainTank, OffTank, RegenHealer, MeleeDpsA, ShieldHealer, CasterDps, MeleeDpsB, PhysRangedDps]);
        yield return Case("a forced outer green keeps the green roll", healersRolledOutside, Request.GreenOuterSeat,
            [RegenHealer, MeleeDpsA, MainTank, OffTank, MeleeDpsB, PhysRangedDps, ShieldHealer, CasterDps]);
        yield return Case("a forced inner green keeps the green roll", healersRolledOutside, Request.GreenInnerSeat,
            [RegenHealer, MeleeDpsA, MainTank, OffTank, MeleeDpsB, PhysRangedDps, ShieldHealer, CasterDps]);
        yield return Case("an Any band forces nothing", healersRolledOutside, Request.AnyBands, healersInside);
    }

    private static TestCaseData Case(string name, PartyRole[] roll, Request request, PartyRole[] lineUp)
        => new TestCaseData(roll, request, lineUp).SetName($"{{m}}({name})");

    // The seat asking sits where the roll put it, as SeatTethers leaves a seat already in its band.
    private static void Ask(TopP5DeltaStateOverrides o, Request request)
    {
        switch (request)
        {
            case Request.BlueOuterSeat: o.Tether[RegenHealer] = PlayerTetherAssignment.CloseOuter; break;
            case Request.BlueInnerSeat: o.Tether[MainTank] = PlayerTetherAssignment.CloseInner; break;
            case Request.BeyondDefenceYes: o.BeyondDefence[OffTank] = true; break;
            case Request.GreenOuterSeat: o.Tether[ShieldHealer] = PlayerTetherAssignment.FarOuter; break;
            case Request.GreenInnerSeat: o.Tether[PhysRangedDps] = PlayerTetherAssignment.FarInner; break;
            case Request.AnyBands:
                o.Tether[RegenHealer] = PlayerTetherAssignment.CloseAny;
                o.Tether[ShieldHealer] = PlayerTetherAssignment.FarAny;
                break;
        }
    }

    [TestCaseSource(nameof(InnerPairCases))]
    public void PutsTheOnlyPairWithAHealerInsideUnlessASeatForcedTheColour(PartyRole[] roll, Request request, PartyRole[] lineUp)
    {
        var state = Hosting(() => new TopP5DeltaState(new Rng(1), Overrides(roll, request), MainTank));
        Assert.That(TopP5DeltaLpduAi.LineUpOrder(state), Is.EqualTo(lineUp));
        Assert.That(ExpectedLineUp(state), Is.EqualTo(lineUp), "the test's own reading of the rule");
    }

    [TestCaseSource(nameof(InnerPairCases))]
    public void TheBotsLineUpOnThatOrder(PartyRole[] roll, Request request, PartyRole[] lineUp)
    {
        var expected = lineUp.Select((role, position) => (role, spot: LineUp[position]!.Value)).ToDictionary(t => t.role, t => t.spot);
        var seen = Probe(LpduStrat, Random.Shared.Next(), o =>
        {
            Pin(o, roll, NorthSouth.North);
            Ask(o, request);
        }, [14.5f], out var run);
        Assert.That(run.Failure, Is.Null, run.ToString);
        AssertSpots(seen[14.5f], expected, NorthSouth.North, $"at 14.5 (seed {run.Seed})");
    }

    // Over many rolls: whenever exactly one pair of a colour holds a healer, that pair is inner.
    [Test]
    public void NoHealerPairIsLeftOutsideAPairWithoutOne()
    {
        Assert.Multiple(() =>
        {
            foreach (var seed in Enumerable.Range(1, 2000))
            {
                var state = new TopP5DeltaState(new Rng(seed), new TopP5DeltaStateOverrides(), MainTank);
                var order = TopP5DeltaLpduAi.LineUpOrder(state);
                foreach (var inner in new[] { 0, 4 })
                    if (!order.Skip(inner).Take(2).Any(IsHealer))
                        Assert.That(order.Skip(inner + 2).Take(2).Any(IsHealer), Is.False,
                                    $"seed {seed}: {string.Join(" ", order)}");
                Assert.That(order, Is.EquivalentTo(state.TetherOrder), $"seed {seed}");
            }
        });
    }

    // The fists are compared by role, in the strat's order: A's healer pair is moved inside, and only then does
    // its north member (H1, yellow) match the outer north (MT, yellow), so H1 and M1 swap while MT and OT hold.
    // B's greens are moved the same way; comparing the roll's slots instead would swap M1 and R2.
    [TestCase("A", new[] { MeleeDpsA, RegenHealer, MainTank, OffTank, ShieldHealer, MeleeDpsB, PhysRangedDps, CasterDps })]
    [TestCase("B", new[] { RegenHealer, OffTank, MainTank, PhysRangedDps, MeleeDpsB, ShieldHealer, MeleeDpsA, CasterDps })]
    public void TheInnerPairFlexesOnTheOuterNorthsFistByRole(string roll, PartyRole[] afterTheFlex)
    {
        var state = Hosting(() => new TopP5DeltaState(new Rng(1), Rolls[roll].Overrides(NorthSouth.North), MainTank));
        Assert.That(TopP5DeltaLpduAi.OrderAfterTheFistFlex(state), Is.EqualTo(afterTheFlex));
        Assert.That(ExpectedAfterTheFlex(state), Is.EqualTo(afterTheFlex), "the test's own reading of the rule");
        foreach (var colour in new[] { 0, 4 })
            for (var side = 0; side < 2; side++)
                Assert.That(FistOf(state, afterTheFlex[colour + side]), Is.Not.EqualTo(FistOf(state, afterTheFlex[colour + 2 + side])),
                            $"{afterTheFlex[colour + side]} stacks with {afterTheFlex[colour + 2 + side]}");
    }

    private static IEnumerable<TestCaseData> SpotCases()
        => from roll in Rolls.Keys
           from eye in new[] { NorthSouth.North, NorthSouth.South }
           select new TestCaseData(roll, eye == NorthSouth.North).SetName($"{{m}}(roll {roll}, eye {(eye == NorthSouth.North ? "N" : "S")})");

    [TestCaseSource(nameof(SpotCases))]
    public void StandsOnTheLpduSpotsAfterEveryMove(string roll, bool eyeNorth)
    {
        var eye = eyeNorth ? NorthSouth.North : NorthSouth.South;
        var expected = ExpectedSpots[roll](eye);
        var seen = Probe(LpduStrat, Random.Shared.Next(), o => Rolls[roll].Pin(o, eye), expected.Keys.ToArray(), out var run);
        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(run.Deaths, Is.Empty, run.ToString);
        Assert.Multiple(() =>
        {
            foreach (var (at, spots) in expected)
                AssertSpots(seen[at], spots, eye, $"at {at} (roll {roll}, seed {run.Seed})");
        });
    }

    // The stretched inner blues pop as the tethers go live; the outer blues only once they walk out to their
    // arms, well clear of the 0.96 s the first break's stacks last. The unsafe inner green collapses once the
    // last Twice-come Ruin is gone, and the outer greens once every Hello World vulnerability is.
    [Test]
    public void BreaksTheTethersInLpduOrder()
    {
        Assert.Multiple(() =>
        {
            foreach (var seed in Enumerable.Range(0, 12).Select(_ => Random.Shared.Next()))
            {
                var (state, breaks, run) = BreakTimes(LpduStrat, seed);
                Assert.That(run.Passed, run.ToString);
                var order = ExpectedLineUp(state);
                float Pair(int north) => Math.Max(breaks.GetValueOrDefault(order[north], float.NaN), breaks.GetValueOrDefault(order[north + 1], float.NaN));
                var why = $"seed {seed}: {string.Join(", ", order.Select(r => $"{r} {breaks.GetValueOrDefault(r, float.NaN):F2}"))}";
                TestContext.Out.WriteLine(why);
                Assert.That(Pair(0), Is.LessThan(28.25f), $"inner blues, {why}");
                Assert.That(Pair(2), Is.InRange(30.5f, 31.2f), $"outer blues, {why}");
                Assert.That(Pair(2) - Pair(0), Is.GreaterThan(Duration.HwTetherBreakStack + 0.5f), $"blue breaks apart, {why}");
                Assert.That(Pair(4), Is.InRange(48.1f, 50f), $"inner greens, {why}");
                Assert.That(Pair(6), Is.InRange(61.2f, 63.5f), $"outer greens, {why}");
            }
        });
    }

    [TestCase(LpduStrat, true)]
    [TestCase(LpduStrat, false)]
    [TestCase(NaStrat, true)]
    [TestCase(NaStrat, false)]
    public void AutomarkersSignTheHelloWorldHolders(int strat, bool on)
    {
        var marks = new Dictionary<PartyRole, (Sign Sign, float At)>();
        TopP5DeltaState? state = null;
        var run = ScenarioRun.Execute(typeof(TopP5DeltaScenario), strat, Random.Shared.Next(), new ScenarioRunOptions
        {
            StopAt = 12f,
            Overrides = o => ((TopP5DeltaStateOverrides)o).Automarkers = on ? Automarkers.On : Automarkers.Off,
            Probe = p =>
            {
                if (!p.Crossed(11.9f)) return;
                state = Scenario(p).LastState;
                var roleById = PerRole.All.ToDictionary(r => p.Member(r)!.GameObjectId.ObjectId.ToString("X8"), r => r);
                foreach (var row in p.Fake.Timeline.Rows.Where(r => r["kind"] == "ac_target" && r["id"] == "0x1F6" && r["target"] != ""))
                    marks[roleById[row["target"]]] = ((Sign)Convert.ToUInt32(row["p"].Split(',')[0], 16),
                                                     float.Parse(row["t"], CultureInfo.InvariantCulture));
            },
        });

        Assert.That(run.Failure, Is.Null, run.ToString);
        if (!on)
        {
            Assert.That(marks, Is.Empty);
            return;
        }
        Assert.That(marks.Keys, Is.EquivalentTo(new[] { state!.NearWorldRole, state.FarWorldRole }));
        Assert.Multiple(() =>
        {
            Assert.That(marks[state.NearWorldRole].Sign, Is.EqualTo(Sign.Triangle));
            Assert.That(marks[state.FarWorldRole].Sign, Is.EqualTo(Sign.Cross));
            Assert.That(marks.Values.Select(m => m.At), Has.All.InRange(10.1f, 11f));
        });
    }

    // A peer's replay reads the same Automarkers and forced colours, so its strat makes the same decisions.
    [TestCase(false, Request.BlueOuterSeat)]
    [TestCase(true, Request.GreenInnerSeat)]
    [TestCase(true, Request.Nothing)]
    public void ReplayCarriesTheAutomarkersAndTheForcedColours(bool automarkersOn, Request request)
    {
        TopP5DeltaState? host = null;
        TopP5DeltaState? replayed = null;
        ScenarioRun.Execute(typeof(TopP5DeltaScenario), LpduStrat, Random.Shared.Next(), new ScenarioRunOptions
        {
            StopAt = 1f,
            Overrides = o =>
            {
                var overrides = (TopP5DeltaStateOverrides)o;
                overrides.Automarkers = automarkersOn ? Automarkers.On : Automarkers.Off;
                Ask(overrides, request);
            },
            Probe = p =>
            {
                if (!p.Crossed(0.5f)) return;
                var scenario = Scenario(p);
                host = scenario.LastState;
                var sent = JsonSerializer.Deserialize<MpMessage>(JsonSerializer.Serialize<MpMessage>(scenario.BuildReplayStateMessage()!))!;
                replayed = (TopP5DeltaState?)scenario.StartReplay(sent, LpduStrat, MainTank, p.World);
            },
        });

        Assert.That(host, Is.Not.Null);
        Assert.That(replayed, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(replayed!.Automarkers, Is.EqualTo(automarkersOn ? Automarkers.On : Automarkers.Off));
            Assert.That(replayed.CloseInnerOuterForced, Is.EqualTo(request == Request.BlueOuterSeat));
            Assert.That(replayed.FarInnerOuterForced, Is.EqualTo(request == Request.GreenInnerSeat));
            Assert.That(replayed.CloseInnerOuterForced, Is.EqualTo(host!.CloseInnerOuterForced));
            Assert.That(replayed.FarInnerOuterForced, Is.EqualTo(host.FarInnerOuterForced));
            Assert.That(TopP5DeltaLpduAi.OrderAfterTheFistFlex(replayed), Is.EqualTo(TopP5DeltaLpduAi.OrderAfterTheFistFlex(host)));
        });
    }

    // ---- The rule, read independently of the strat's code -----------------------------------------------

    internal static bool IsHealer(PartyRole role) => role is RegenHealer or ShieldHealer;

    internal static PartyRole[] ExpectedLineUp(TopP5DeltaState state)
    {
        var order = state.TetherOrder.ToArray();
        foreach (var (inner, forced) in new[] { (0, state.CloseInnerOuterForced), (4, state.FarInnerOuterForced) })
        {
            var innerHealer = order.Skip(inner).Take(2).Any(IsHealer);
            var outerHealer = order.Skip(inner + 2).Take(2).Any(IsHealer);
            if (forced || innerHealer || !outerHealer) continue;
            order = [.. order[..inner], .. order[(inner + 2)..(inner + 4)], .. order[inner..(inner + 2)], .. order[(inner + 4)..]];
        }
        return order;
    }

    internal static PartyRole[] ExpectedAfterTheFlex(TopP5DeltaState state)
    {
        var order = ExpectedLineUp(state);
        foreach (var inner in new[] { 0, 4 })
            if (FistOf(state, order[inner]) == FistOf(state, order[inner + 2]))
                (order[inner], order[inner + 1]) = (order[inner + 1], order[inner]);
        return order;
    }

    internal static uint FistOf(TopP5DeltaState state, PartyRole role) => state.FistColors[state.TetherOrder.ToList().IndexOf(role)];

    // ---- Pinned rolls -----------------------------------------------------------------------------------

    // A: H1+M1 rolled outer blue and moved inside; blue flexes (H1 and MT both yellow); m = +1, c = +1; OT holds
    //    the monitor and steps west; H1 refuses Beyond Defence, so M1 takes it; Near MT, Distant H1.
    // B: blue's healer pair is rolled inside already; green's H2+M2 is moved inside and flexes (H2 and M1 both
    //    yellow); m = -1, c = -1; H1 holds the monitor, takes Beyond Defence (OT refuses) and steps east; Near
    //    R1, Distant MT.
    internal sealed record Roll(
        PartyRole[] TetherOrder, uint[] FistColors, ArmRotation[] ArmRotations, Side OmegaMonitor, Side PlayerMonitor,
        Side Swivel, PartyRole Monitor, PartyRole Near, PartyRole Far, PartyRole RefusesBeyondDefence)
    {
        public void Pin(TopP5DeltaStateOverrides o, NorthSouth eye)
        {
            o.EyeSpawn = eye;
            o.TetherOrder = TetherOrder;
            o.FistColors = FistColors;
            o.ArmRotations = ArmRotations;
            o.OmegaMonitorSide = OmegaMonitor;
            o.PlayerMonitorSide = PlayerMonitor;
            o.SwivelCannonSide = Swivel;
            o.Monitor[Monitor] = true;
            o.HelloWorld[Near] = HelloWorldOption.Near;
            o.HelloWorld[Far] = HelloWorldOption.Far;
            o.BeyondDefence[RefusesBeyondDefence] = false;
        }

        public TopP5DeltaStateOverrides Overrides(NorthSouth eye)
        {
            var o = new TopP5DeltaStateOverrides();
            Pin(o, eye);
            return o;
        }
    }

    internal static readonly Dictionary<string, Roll> Rolls = new()
    {
        ["A"] = new Roll(
            [MainTank, OffTank, RegenHealer, MeleeDpsA, ShieldHealer, MeleeDpsB, PhysRangedDps, CasterDps],
            [Yellow, Blue, Yellow, Blue, Yellow, Blue, Blue, Yellow],
            [Ccw, Ccw, Ccw, Cw, Cw, Cw], Side.Right, Side.Left, Side.Left,
            Monitor: OffTank, Near: MainTank, Far: RegenHealer, RefusesBeyondDefence: RegenHealer),
        ["B"] = new Roll(
            [RegenHealer, OffTank, MainTank, PhysRangedDps, MeleeDpsA, CasterDps, ShieldHealer, MeleeDpsB],
            [Blue, Yellow, Yellow, Blue, Yellow, Blue, Yellow, Blue],
            [Cw, Ccw, Cw, Ccw, Ccw, Cw], Side.Left, Side.Left, Side.Right,
            Monitor: RegenHealer, Near: PhysRangedDps, Far: MainTank, RefusesBeyondDefence: OffTank),
    };

    // ---- The design's spots, in the eye frame -----------------------------------------------------------

    internal static readonly Vector2?[] LineUp =
        [new(-8.6f, -4f), new(-8.6f, 4f), new(-13.5f, -2.5f), new(-13.5f, 2.5f), new(6.2f, -4f), new(6.2f, 4f), new(11.3f, -9.3f), new(11.3f, 9.3f)];

    internal const float ArmBaitRadius = 19.5f;
    internal static readonly float ArmBaitDegrees = 0.15f * 180f / MathF.PI;

    // Each arm's bearing in the eye frame, by its index in the State. The scenario mirrors x for eye S.
    internal static float ArmBearing(int arm, NorthSouth eye)
        => (eye == NorthSouth.North ? new[] { 300f, 240f, 0f, 180f, 60f, 120f } : new[] { 240f, 300f, 180f, 0f, 120f, 60f })[arm];

    // On the side of the arm away from its turn.
    internal static Vector2 ArmBait(float eyeFrameArmBearing, ArmRotation[] rotations, NorthSouth eye)
    {
        var arm = Enumerable.Range(0, 6).Single(i => ArmBearing(i, eye) == eyeFrameArmBearing);
        return OnRing(eyeFrameArmBearing + ArmBaitDegrees * rotations[arm].Mul, ArmBaitRadius);
    }

    private static readonly Dictionary<string, Func<NorthSouth, SortedDictionary<float, Dictionary<PartyRole, Vector2>>>> ExpectedSpots = new()
    {
        ["A"] = eye =>
        {
            var arms = Rolls["A"].ArmRotations;
            var corners = Spots((ShieldHealer, -9.3f, -13f), (MeleeDpsB, -9.3f, 13f), (PhysRangedDps, 9.3f, -13f), (CasterDps, 9.3f, 13f));
            var monitors = Spots((OffTank, 0f, 5.1f), (MeleeDpsA, 13f, 3.2f), (RegenHealer, -0.4f, 3.2f), (MainTank, 0.4f, 3.2f));
            var end = Spots((MeleeDpsA, -0.7f, 5.7f), (RegenHealer, -0.7f, 6.5f), (MainTank, -0.7f, 7.3f), (OffTank, 0.7f, 5.7f),
                            (ShieldHealer, 0.7f, 6.5f), (MeleeDpsB, 0.7f, 7.3f), (CasterDps, 8.8f, -0.5f), (PhysRangedDps, -8.8f, -0.5f));
            var fistSetUp = Spots((MeleeDpsA, -13.5f, -6.5f), (RegenHealer, -13.5f, 6.5f), (MainTank, -13.5f, -2.5f), (OffTank, -13.5f, 2.5f),
                                  (ShieldHealer, 9.64f, -9.64f), (MeleeDpsB, 9.64f, 9.64f), (PhysRangedDps, 9.64f, -9.64f), (CasterDps, 9.64f, 9.64f));
            return new()
            {
                [14.5f] = Spots((RegenHealer, -8.6f, -4f), (MeleeDpsA, -8.6f, 4f), (MainTank, -13.5f, -2.5f), (OffTank, -13.5f, 2.5f),
                                (ShieldHealer, 6.2f, -4f), (MeleeDpsB, 6.2f, 4f), (PhysRangedDps, 11.3f, -9.3f), (CasterDps, 11.3f, 9.3f)),
                [22f] = fistSetUp,
                [29.6f] = With(fistSetUp, (MeleeDpsA, -13.5f, -2.5f), (RegenHealer, -13.5f, 2.5f)),
                [34f] = new()
                {
                    [MeleeDpsA] = new(0f, -5f), [RegenHealer] = new(0f, 5f),
                    [MainTank] = ArmBait(300f, arms, eye), [OffTank] = ArmBait(240f, arms, eye),
                    [ShieldHealer] = ArmBait(0f, arms, eye), [MeleeDpsB] = ArmBait(180f, arms, eye),
                    [PhysRangedDps] = ArmBait(60f, arms, eye), [CasterDps] = ArmBait(120f, arms, eye),
                },
                [37.5f] = Spots((ShieldHealer, 0f, -12f), (MeleeDpsB, 0f, 12f), (PhysRangedDps, 9f, -10f), (CasterDps, 9f, 10f)),
                [39.9f] = With(corners, monitors),
                [40.45f] = With(With(corners, monitors), (OffTank, -1.2f, 5.1f)),
                [47.9f] = Spots((MainTank, 0f, 6f), (ShieldHealer, -9.6f, -3.4f), (MeleeDpsB, -9.64f, 9.64f), (CasterDps, 15.5f, 10.7f)),
                [52.5f] = Spots((MainTank, 0f, 6f), (RegenHealer, 0f, 18.5f), (MeleeDpsA, 9.5f, 16f), (OffTank, 9.5f, 16f),
                                (ShieldHealer, -9.6f, 4f), (MeleeDpsB, -9.64f, 9.64f), (CasterDps, 15.5f, 10.7f), (PhysRangedDps, -18.5f, 1.5f)),
                [60f] = end,
                [65f] = With(end, (CasterDps, 4f, -0.5f), (PhysRangedDps, -4f, -0.5f), (MainTank, 0f, -5.7f)),
            };
        },
        ["B"] = eye =>
        {
            var arms = Rolls["B"].ArmRotations;
            var corners = Spots((MeleeDpsB, -9.3f, -13f), (ShieldHealer, -9.3f, 13f), (MeleeDpsA, 9.3f, -13f), (CasterDps, 9.3f, 13f));
            var monitors = Spots((RegenHealer, 13f, -5.1f), (OffTank, -0.4f, -3.2f), (MainTank, 0.4f, -3.2f), (PhysRangedDps, 0f, -3.2f));
            var end = Spots((RegenHealer, -0.7f, -5.7f), (OffTank, -0.7f, -6.5f), (MainTank, -0.7f, -7.3f), (PhysRangedDps, 0.7f, -5.7f),
                            (MeleeDpsB, 0.7f, -6.5f), (ShieldHealer, 0.7f, -7.3f), (MeleeDpsA, 8.8f, 0.5f), (CasterDps, -8.8f, 0.5f));
            var fistSetUp = Spots((RegenHealer, -13.5f, -6.5f), (OffTank, -13.5f, 6.5f), (MainTank, -13.5f, -2.5f), (PhysRangedDps, -13.5f, 2.5f),
                                  (MeleeDpsB, 9.64f, -9.64f), (ShieldHealer, 9.64f, 9.64f), (MeleeDpsA, 9.64f, -9.64f), (CasterDps, 9.64f, 9.64f));
            return new()
            {
                [14.5f] = Spots((RegenHealer, -8.6f, -4f), (OffTank, -8.6f, 4f), (MainTank, -13.5f, -2.5f), (PhysRangedDps, -13.5f, 2.5f),
                                (ShieldHealer, 6.2f, -4f), (MeleeDpsB, 6.2f, 4f), (MeleeDpsA, 11.3f, -9.3f), (CasterDps, 11.3f, 9.3f)),
                [22f] = fistSetUp,
                [29.6f] = With(fistSetUp, (RegenHealer, -13.5f, -2.5f), (OffTank, -13.5f, 2.5f)),
                [34f] = new()
                {
                    [RegenHealer] = new(0f, -5f), [OffTank] = new(0f, 5f),
                    [MainTank] = ArmBait(300f, arms, eye), [PhysRangedDps] = ArmBait(240f, arms, eye),
                    [MeleeDpsB] = ArmBait(0f, arms, eye), [ShieldHealer] = ArmBait(180f, arms, eye),
                    [MeleeDpsA] = ArmBait(60f, arms, eye), [CasterDps] = ArmBait(120f, arms, eye),
                },
                [37.5f] = Spots((MeleeDpsB, 0f, -12f), (ShieldHealer, 0f, 12f), (MeleeDpsA, 9f, -10f), (CasterDps, 9f, 10f)),
                [39.9f] = With(corners, monitors),
                [40.45f] = With(With(corners, monitors), (RegenHealer, 14.2f, -5.1f)),
                [47.9f] = Spots((PhysRangedDps, 0f, -6f), (ShieldHealer, -9.6f, 3.4f), (MeleeDpsB, -9.64f, -9.64f), (MeleeDpsA, 15.5f, -10.7f)),
                [52.5f] = Spots((PhysRangedDps, 0f, -6f), (MainTank, 0f, -18.5f), (RegenHealer, 9.5f, -16f), (OffTank, 9.5f, -16f),
                                (ShieldHealer, -9.6f, -4f), (MeleeDpsB, -9.64f, -9.64f), (MeleeDpsA, 15.5f, -10.7f), (CasterDps, -18.5f, -1.5f)),
                [60f] = end,
                [65f] = With(end, (MeleeDpsA, 4f, 0.5f), (CasterDps, -4f, 0.5f), (MainTank, 0f, 5.7f)),
            };
        },
    };

    private static Dictionary<PartyRole, Vector2> Spots(params (PartyRole Role, float X, float Z)[] spots)
        => spots.ToDictionary(s => s.Role, s => new Vector2(s.X, s.Z));

    private static Dictionary<PartyRole, Vector2> With(Dictionary<PartyRole, Vector2> spots, params (PartyRole Role, float X, float Z)[] changes)
        => With(spots, Spots(changes));

    private static Dictionary<PartyRole, Vector2> With(Dictionary<PartyRole, Vector2> spots, Dictionary<PartyRole, Vector2> changes)
    {
        var result = new Dictionary<PartyRole, Vector2>(spots);
        foreach (var (role, spot) in changes) result[role] = spot;
        return result;
    }

    // ---- Running ----------------------------------------------------------------------------------------

    internal static Vector2 ToWorld(Vector2 eyeFrame, NorthSouth eye) => eye == NorthSouth.North ? eyeFrame : -eyeFrame;

    internal static Vector2 OnRing(float bearingDegrees, float radius)
    {
        var radians = bearingDegrees * MathF.PI / 180f;
        return new Vector2(radius * MathF.Sin(radians), -radius * MathF.Cos(radians));
    }

    internal static Vector2 Flat(Vector3 position) => new(position.X, position.Z);

    internal static TopP5DeltaScenario Scenario(ScenarioProbe p) => p.Game.Catalog.Scenarios.OfType<TopP5DeltaScenario>().Single();

    // Pins every roll the strat reads except the jobs, which the seed picks.
    internal static void Pin(TopP5DeltaStateOverrides o, PartyRole[] tetherOrder, NorthSouth eye)
    {
        o.EyeSpawn = eye;
        o.TetherOrder = tetherOrder;
        o.FistColors = [Yellow, Blue, Yellow, Blue, Yellow, Blue, Blue, Yellow];
    }

    private static TopP5DeltaStateOverrides Overrides(PartyRole[] tetherOrder, Request request)
    {
        var o = new TopP5DeltaStateOverrides();
        Pin(o, tetherOrder, NorthSouth.North);
        Ask(o, request);
        return o;
    }

    internal static Dictionary<float, Dictionary<PartyRole, Vector2>> Probe(
        int strat, int seed, Action<TopP5DeltaStateOverrides> overrides, float[] times, out ScenarioRun run)
    {
        var seen = new Dictionary<float, Dictionary<PartyRole, Vector2>>();
        run = ScenarioRun.Execute(typeof(TopP5DeltaScenario), strat, seed, new ScenarioRunOptions
        {
            StopAt = times.Max() + 0.1f,
            Overrides = o => overrides((TopP5DeltaStateOverrides)o),
            Probe = p =>
            {
                foreach (var at in times)
                    if (p.Crossed(at))
                        seen[at] = PerRole.All.ToDictionary(r => r, r => Flat(p.Member(r)!.Position));
            },
        });
        return seen;
    }

    // When each member's tether broke: the first frame from the live tethers on that it lacks its colour's
    // regression. The stretched blues break on the frame the tethers go live, so they are never seen with it.
    internal static (TopP5DeltaState State, Dictionary<PartyRole, float> Breaks, ScenarioRun Run) BreakTimes(int strat, int seed)
    {
        TopP5DeltaState? state = null;
        var breaks = new Dictionary<PartyRole, float>();
        var run = ScenarioRun.Execute(typeof(TopP5DeltaScenario), strat, seed, new ScenarioRunOptions
        {
            Probe = p =>
            {
                state ??= Scenario(p).LastState;
                if (p.Time < 28.1f || state is null) return;
                for (var slot = 0; slot < 8; slot++)
                {
                    var role = state.TetherOrder[slot];
                    var regression = slot < 4 ? StatusId.HWRemoteTether : StatusId.HWLocalTether;
                    if (!breaks.ContainsKey(role) && p.Member(role) is { } member && !member.HasStatus(regression))
                        breaks[role] = p.Time;
                }
            },
        });
        return (state!, breaks, run);
    }

    private static void AssertSpots(Dictionary<PartyRole, Vector2> seen, Dictionary<PartyRole, Vector2> eyeFrameSpots, NorthSouth eye, string when)
    {
        foreach (var (role, eyeFrame) in eyeFrameSpots)
        {
            var spot = ToWorld(eyeFrame, eye);
            Assert.That(Vector2.Distance(seen[role], spot), Is.LessThan(Tolerance), $"{role} at {seen[role]} {when}, expected {spot}");
        }
    }

    private static T Hosting<T>(Func<T> make)
    {
        PerRole.ForceSeats = true;
        try
        {
            return make();
        }
        finally
        {
            PerRole.ForceSeats = false;
        }
    }
}
