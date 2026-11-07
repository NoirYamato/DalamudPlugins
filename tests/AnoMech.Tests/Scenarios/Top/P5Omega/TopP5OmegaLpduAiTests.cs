using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Multiplayer;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P5Omega;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;

namespace AnoMech.Tests;

// The LPDU strat (x east, z south) is upstream's with two spots moved onto the strategy board's OMEGA pt 1 and pt 2
// panels. Each Hello World lines up in its own order: Near World, Distant World, the monitors (first) or the Blaster
// tethers (second), then jumps 1-4; the automarkers sign them Triangle, Cross, Bind 1-2 and Attack 1-4.
//
//   0, 20, 24  upstream's opening clump and both Omega dodges; 62 the clump again.
//   28, 47     upstream's marks for each Hello World.
//   32  first Hello World, written for the monitors firing west and mirrored east: Near (10, 0), Distant (0.2, 10)
//       south where upstream's stands north (0.2, -10), monitors (-10, -10) / (-10, 10), jumps (0.2, -19), (19, -4),
//       (19, 4), (0.2, 19).
//   48  the middle, the tether takers 3 y toward the beetle; 53 second Hello World, written for the beetle north and
//       turned with it: upstream's mirrored across the beetle's line, so Near (0, 10), Distant (-10, 0), tethers
//       (9.5, -16.5) / (-9.5, -16.5), jumps (19, 0), (4, 19), (-4, 19), (-19, 0).
// Both changes resolve as upstream's spots do. The first reflects upstream's layout north to south, monitors 1 and 2,
// jumps 1 and 4 and jumps 2 and 3 trading places; the second mirrors the whole layout across the beetle's line. Every
// jump still goes to the nearest or farthest member, the monitors to the two on their half, and the tether takers
// still pick up the passable Blaster tethers in the middle.
public class TopP5OmegaLpduAiTests
{
    internal const int LpduStrat = 0;
    internal const int NaStrat = 1;
    private const float Tolerance = 0.45f;

    private const float FirstHelloWorldMove = 32f;
    private const float FirstHelloWorldMarks = 28f;
    private const float FirstHelloWorldSpotsReached = 41.1f;
    private const float SecondHelloWorldMarks = 47f;
    private const float SecondHelloWorldSpotsReached = 59f;

    private const float RimRadius = 18f;
    private const float WorldHolderRadius = 10f;
    private const float CompassSlack = 20f;

    private const int DistantWorld = 1;

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

    // ---- Upstream's spots, read from TopP5OmegaAi, in each Hello World's order -------------------------------------

    private static readonly Vector2[] UpstreamFirstHelloWorldWhenTheMonitorsFireWest =
        [new(10f, 0f), new(0.2f, -10f), new(-10f, -10f), new(-10f, 10f), new(0.2f, -19f), new(19f, -4f), new(19f, 4f), new(0.2f, 19f)];

    private static readonly Vector2[] UpstreamSecondHelloWorldWhenTheBeetleIsNorth =
        [new(0f, 10f), new(10f, 0f), new(-9.5f, -16.5f), new(9.5f, -16.5f), new(-19f, 0f), new(-4f, 19f), new(4f, 19f), new(19f, 0f)];

    private static readonly Sign[] HelloWorldSigns =
        [Sign.Triangle, Sign.Cross, Sign.Bind1, Sign.Bind2, Sign.Attack1, Sign.Attack2, Sign.Attack3, Sign.Attack4];

    // ---- The pinned roll -------------------------------------------------------------------------------------------

    // OT holds the first Near World and SH the first Distant World, M1 and M2 the second ones; OT SH M1 M2 start with
    // the extra Dynamis, so M1 M2 take the monitors and OT SH, on 3 stacks after the first Hello World, the Blaster
    // tethers. The first Omega pair is F NE (Staff) and M SW (Sword), the second F SE (Legs) and M NW (Shield).
    internal static readonly PartyRole[] FirstHelloWorldOrder =
        [OffTank, ShieldHealer, MeleeDpsA, MeleeDpsB, MainTank, RegenHealer, PhysRangedDps, CasterDps];

    internal static readonly PartyRole[] SecondHelloWorldOrder =
        [MeleeDpsA, MeleeDpsB, OffTank, ShieldHealer, MainTank, RegenHealer, PhysRangedDps, CasterDps];

    internal static void Pin(TopP5OmegaStateOverrides o, MonitorSide monitors, Direction beetle)
    {
        o.FirstAttackDirection = Direction.NE;
        o.SecondAttackDirection = Direction.SE;
        o.FirstFAttack = OmegaAttack.Staff;
        o.FirstMAttack = OmegaAttack.Sword;
        o.SecondFAttack = OmegaAttack.Legs;
        o.SecondMAttack = OmegaAttack.Shield;
        o.FirstWaveCannonFront = true;
        o.MonitorSide = monitors;
        o.BettleSpawnDirection = beetle;
        HelloWorld(o, OffTank, HelloWorldOrderOption.First, HelloWorldTypeOption.Near);
        HelloWorld(o, ShieldHealer, HelloWorldOrderOption.First, HelloWorldTypeOption.Far);
        HelloWorld(o, MeleeDpsA, HelloWorldOrderOption.Second, HelloWorldTypeOption.Near);
        HelloWorld(o, MeleeDpsB, HelloWorldOrderOption.Second, HelloWorldTypeOption.Far);
        foreach (var role in new[] { OffTank, ShieldHealer, MeleeDpsA, MeleeDpsB })
            o.ExtraDynamis[role] = true;
        o.MonitorTargets = [MeleeDpsA, MeleeDpsB];
        o.HelloWorld1JumpOrder = [MainTank, RegenHealer, PhysRangedDps, CasterDps];
        o.BlasterTethers = [OffTank, ShieldHealer];
        o.HelloWorld2 = SecondHelloWorldOrder;
    }

    private static void HelloWorld(TopP5OmegaStateOverrides o, PartyRole role, HelloWorldOrderOption order, HelloWorldTypeOption type)
    {
        o.HelloWorldOrder[role] = order;
        o.HelloWorldType[role] = type;
    }

    internal static MonitorSide MonitorsFiring(string half) => half == "west" ? MonitorSide.Left : MonitorSide.Right;

    internal static Direction BeetleAt(string cardinal) => Direction.Cardinal.Single(d => d.Name() == cardinal);

    // ---- The strats ------------------------------------------------------------------------------------------------

    [Test]
    public void TheEuLpduStratComesFirstSoItIsTheDefaultAndUpstreamsIsNa()
    {
        var strats = ScenarioCatalog.Create().Scenarios.OfType<TopP5OmegaScenario>().Single().AiStrats;
        Assert.That(strats.Select(s => (s.GetType(), s.Name, s.Group)), Is.EqualTo(new (Type, string, string?)[]
        {
            (typeof(TopP5OmegaLpduAi), "[LPDU] Light PF", "EU"),
            (typeof(TopP5OmegaAi), "Standard", "NA"),
        }));
    }

    // Every F and M pairing on both pairs, both cannon orders and all four intercardinals for the first F.
    [TestCase(false, true, true, false, true, 1, 2)]
    [TestCase(true, true, false, false, false, 3, -2)]
    [TestCase(true, false, false, true, true, 5, 2)]
    [TestCase(false, false, true, true, false, 7, -2)]
    public void UntilTheFirstHelloWorldMoveEverySeatStandsWhereUpstreamsStratPutsIt(
        bool firstLegs, bool firstSword, bool secondLegs, bool secondSword, bool firstCannonsFireNorthAndSouth, int firstF, int secondTurn)
    {
        void PinTheOmegaPairs(TopP5OmegaStateOverrides o)
        {
            Pin(o, MonitorSide.Left, Direction.N);
            o.FirstAttackDirection = Direction.All[firstF];
            o.SecondAttackDirection = Direction.All[firstF].Rotate(secondTurn);
            o.FirstFAttack = firstLegs ? OmegaAttack.Legs : OmegaAttack.Staff;
            o.FirstMAttack = firstSword ? OmegaAttack.Sword : OmegaAttack.Shield;
            o.SecondFAttack = secondLegs ? OmegaAttack.Legs : OmegaAttack.Staff;
            o.SecondMAttack = secondSword ? OmegaAttack.Sword : OmegaAttack.Shield;
            o.FirstWaveCannonFront = firstCannonsFireNorthAndSouth;
        }

        var seed = Random.Shared.Next();
        var lpdu = Frames(LpduStrat, seed, PinTheOmegaPairs, out var lpduRun);
        var na = Frames(NaStrat, seed, PinTheOmegaPairs, out var naRun);

        Assert.That(lpduRun.Passed && naRun.Passed, () => $"{lpduRun}{Environment.NewLine}{naRun}");
        Assert.That(lpdu.Select(f => f.Time), Is.EqualTo(na.Select(f => f.Time)));
        Assert.That(lpdu, Has.Count.GreaterThan(1800), "frames compared");
        var widest = lpdu.Zip(na, (a, b) => (a.Time, Apart: PerRole.All.Max(r => Vector2.Distance(a.Seats[r], b.Seats[r]))))
                         .MaxBy(f => f.Apart);
        Assert.That(widest.Apart, Is.LessThan(1e-4f), $"seed {seed}: a seat stood {widest.Apart:F4} y from upstream's at {widest.Time:F2}");
    }

    [TestCase("west")]
    [TestCase("east")]
    public void FirstHelloWorldTakesUpstreamsSpotsButTheDistantWorldStandsSouth(string monitorHalf)
    {
        var monitors = MonitorsFiring(monitorHalf);
        var seed = Random.Shared.Next();
        var lpdu = Seats(LpduStrat, seed, o => Pin(o, monitors, Direction.N), FirstHelloWorldSpotsReached, out var lpduRun);
        var na = Seats(NaStrat, seed, o => Pin(o, monitors, Direction.N), FirstHelloWorldSpotsReached, out var naRun);

        Assert.That(lpduRun.Passed && naRun.Passed, () => $"{lpduRun}{Environment.NewLine}{naRun}");
        Assert.Multiple(() =>
        {
            for (var slot = 0; slot < 8; slot++)
            {
                var role = FirstHelloWorldOrder[slot];
                var upstream = UpstreamFirstHelloWorldWhenTheMonitorsFireWest[slot] * new Vector2(monitors.Mul, 1f);
                var lpduSpot = slot == DistantWorld ? upstream with { Y = -upstream.Y } : upstream;
                AssertNear(na[role], upstream, $"upstream's {role} (slot {slot}), monitors {monitorHalf}, seed {seed}");
                AssertNear(lpdu[role], lpduSpot, $"LPDU's {role} (slot {slot}), monitors {monitorHalf}, seed {seed}");
            }
            var distant = lpdu[FirstHelloWorldOrder[DistantWorld]];
            Assert.That(DegreesApart(BearingOf(distant), 180f), Is.LessThan(5f), $"the Distant World holder at {distant} stands due south");
            Assert.That(distant.X * monitors.Mul, Is.GreaterThan(0f), $"the Distant World holder at {distant} keeps off the monitors' half");
        });
    }

    [TestCase("N")]
    [TestCase("E")]
    [TestCase("S")]
    [TestCase("W")]
    public void SecondHelloWorldTakesUpstreamsSpotsMirroredAcrossTheBeetlesLine(string beetleCardinal)
    {
        var beetle = BeetleAt(beetleCardinal);
        var seed = Random.Shared.Next();
        var lpdu = Seats(LpduStrat, seed, o => Pin(o, MonitorSide.Left, beetle), SecondHelloWorldSpotsReached, out var lpduRun, out var beetleSpot);
        var na = Seats(NaStrat, seed, o => Pin(o, MonitorSide.Left, beetle), SecondHelloWorldSpotsReached, out var naRun, out _);

        Assert.That(lpduRun.Passed && naRun.Passed, () => $"{lpduRun}{Environment.NewLine}{naRun}");
        var bearing = beetle.RadiansFromNorth * 180f / MathF.PI;
        Assert.That(Vector2.Distance(beetleSpot, Turned(new Vector2(0f, -20f), bearing)), Is.LessThan(0.01f), $"the beetle at {beetleSpot}");
        var beetleLine = Vector2.Normalize(beetleSpot);
        Assert.Multiple(() =>
        {
            for (var slot = 0; slot < 8; slot++)
            {
                var role = SecondHelloWorldOrder[slot];
                var upstream = Turned(UpstreamSecondHelloWorldWhenTheBeetleIsNorth[slot], bearing);
                AssertNear(na[role], upstream, $"upstream's {role} (slot {slot}), beetle {beetleCardinal}, seed {seed}");
                AssertNear(lpdu[role], MirroredAcross(beetleLine, upstream), $"LPDU's {role} (slot {slot}), beetle {beetleCardinal}, seed {seed}");
            }
        });
    }

    // OMEGA pt 2 as the board draws it, the beetle west: the tether baits beside the beetle at the rim, Bind 1 north
    // of it and Bind 2 south; Near World east and Distant World south, in from the rim; the Distant Baiters 1 north
    // and 4 south on the rim; the Near Baiters 2 and 3 on the east rim, 2 the northern.
    [Test]
    public void WithTheBeetleWestTheSecondHelloWorldIsTheOmegaPt2Board()
    {
        var spots = MarkedSpots(o => Pin(o, MonitorSide.Left, Direction.W), SecondHelloWorldMarks, SecondHelloWorldSpotsReached, out var seed);
        var beetle = new Vector2(-20f, 0f);
        var why = $"seed {seed}: {string.Join(", ", spots.Select(s => $"{s.Key} {s.Value}"))}";
        Assert.Multiple(() =>
        {
            AssertOnTheRim(spots[Sign.Bind1], why);
            AssertOnTheRim(spots[Sign.Bind2], why);
            Assert.That(Vector2.Distance(spots[Sign.Bind1], beetle), Is.LessThan(12f), $"Bind 1 beside the beetle, {why}");
            Assert.That(Vector2.Distance(spots[Sign.Bind2], beetle), Is.LessThan(12f), $"Bind 2 beside the beetle, {why}");
            Assert.That(spots[Sign.Bind1].Y, Is.LessThan(beetle.Y), $"Bind 1 north of the beetle, {why}");
            Assert.That(spots[Sign.Bind2].Y, Is.GreaterThan(beetle.Y), $"Bind 2 south of the beetle, {why}");
            AssertInside(spots[Sign.Triangle], 90f, why);
            AssertInside(spots[Sign.Cross], 180f, why);
            AssertOnTheRim(spots[Sign.Attack1], 0f, why);
            AssertOnTheRim(spots[Sign.Attack4], 180f, why);
            AssertOnTheRim(spots[Sign.Attack2], 90f, why);
            AssertOnTheRim(spots[Sign.Attack3], 90f, why);
            Assert.That(spots[Sign.Attack2].Y, Is.LessThan(spots[Sign.Attack3].Y), $"Attack 2 north of Attack 3, {why}");
        });
    }

    // OMEGA pt 1 as the board draws it, the monitors east: the monitor soaks north-east (Bind 1) and south-east
    // (Bind 2); Near World west and Distant World south, in from the rim; the Distant Baiters 1 north and 4 south on
    // the rim; the Near Baiters 2 and 3 on the west rim, 2 the northern.
    [Test]
    public void WithTheMonitorsEastTheFirstHelloWorldIsTheOmegaPt1Board()
    {
        var spots = MarkedSpots(o => Pin(o, MonitorSide.Right, Direction.N), FirstHelloWorldMarks, FirstHelloWorldSpotsReached, out var seed);
        var why = $"seed {seed}: {string.Join(", ", spots.Select(s => $"{s.Key} {s.Value}"))}";
        Assert.Multiple(() =>
        {
            AssertOnBearing(spots[Sign.Bind1], 45f, why);
            AssertOnBearing(spots[Sign.Bind2], 135f, why);
            AssertInside(spots[Sign.Triangle], 270f, why);
            AssertInside(spots[Sign.Cross], 180f, why);
            AssertOnTheRim(spots[Sign.Attack1], 0f, why);
            AssertOnTheRim(spots[Sign.Attack4], 180f, why);
            AssertOnTheRim(spots[Sign.Attack2], 270f, why);
            AssertOnTheRim(spots[Sign.Attack3], 270f, why);
            Assert.That(spots[Sign.Attack2].Y, Is.LessThan(spots[Sign.Attack3].Y), $"Attack 2 north of Attack 3, {why}");
        });
    }

    // Each set clears the last and signs the Hello World's order: OT SH M1 M2 MT H1 R1 R2 at 28, M1 M2 OT SH MT H1 R1
    // R2 at 47, as upstream's strat does on the same seed.
    [Test]
    public void TheMarksAndTheirTimesAreUpstreams()
    {
        var seed = Random.Shared.Next();
        var lpdu = Marks(LpduStrat, seed, out var lpduRun);
        var na = Marks(NaStrat, seed, out var naRun);

        Assert.That(lpduRun.Passed && naRun.Passed, () => $"{lpduRun}{Environment.NewLine}{naRun}");
        Assert.That(lpdu, Is.EqualTo(na), $"seed {seed}");
        Assert.Multiple(() =>
        {
            foreach (var (at, order) in new[] { (FirstHelloWorldMarks, FirstHelloWorldOrder), (SecondHelloWorldMarks, SecondHelloWorldOrder) })
            {
                var placed = lpdu.Where(m => m.Target is not null && m.At >= at && m.At < at + 0.5f).ToList();
                Assert.That(placed.Select(m => (m.Target!.Value, m.Sign)), Is.EquivalentTo(order.Zip(HelloWorldSigns)), $"the marks at {at}");
                Assert.That(placed.Select(m => m.At), Has.All.InRange(at, at + 2 * ScenarioRun.FrameSeconds), $"the marks at {at}");
            }
            Assert.That(lpdu.Count(m => m.Target is not null), Is.EqualTo(16), "no other marks");
        });
    }

    // ---- Geometry --------------------------------------------------------------------------------------------------

    // The compass bearing of a spot from the centre: 0 north, clockwise.
    private static float BearingOf(Vector2 spot) => (MathF.Atan2(spot.X, -spot.Y) * 180f / MathF.PI + 360f) % 360f;

    private static float DegreesApart(float a, float b) => MathF.Abs((a - b + 540f) % 360f - 180f);

    private static Vector2 Turned(Vector2 spot, float bearingDegrees)
    {
        var radians = bearingDegrees * MathF.PI / 180f;
        var (sin, cos) = MathF.SinCos(radians);
        return new Vector2(spot.X * cos - spot.Y * sin, spot.X * sin + spot.Y * cos);
    }

    private static Vector2 MirroredAcross(Vector2 line, Vector2 spot) => 2f * Vector2.Dot(spot, line) * line - spot;

    private static void AssertNear(Vector2 seen, Vector2 spot, string who)
        => Assert.That(Vector2.Distance(seen, spot), Is.LessThan(Tolerance), $"{who} at {seen}, expected {spot}");

    private static void AssertOnBearing(Vector2 spot, float bearing, string why)
        => Assert.That(DegreesApart(BearingOf(spot), bearing), Is.LessThan(CompassSlack), $"{spot} on bearing {bearing}, {why}");

    private static void AssertOnTheRim(Vector2 spot, string why)
        => Assert.That(spot.Length(), Is.GreaterThan(RimRadius), $"{spot} on the rim, {why}");

    private static void AssertOnTheRim(Vector2 spot, float bearing, string why)
    {
        AssertOnTheRim(spot, why);
        AssertOnBearing(spot, bearing, why);
    }

    private static void AssertInside(Vector2 spot, float bearing, string why)
    {
        Assert.That(spot.Length(), Is.EqualTo(WorldHolderRadius).Within(1.5f), $"{spot} in from the rim, {why}");
        AssertOnBearing(spot, bearing, why);
    }

    // ---- Running ---------------------------------------------------------------------------------------------------

    internal static Vector2 Flat(Vector3 position) => new(position.X, position.Z);

    private static List<(float Time, Dictionary<PartyRole, Vector2> Seats)> Frames(
        int strat, int seed, Action<TopP5OmegaStateOverrides> pin, out ScenarioRun run)
    {
        var frames = new List<(float Time, Dictionary<PartyRole, Vector2> Seats)>();
        run = ScenarioRun.Execute(typeof(TopP5OmegaScenario), strat, seed, new ScenarioRunOptions
        {
            StopAt = FirstHelloWorldMove,
            Overrides = o => pin((TopP5OmegaStateOverrides)o),
            Probe = p =>
            {
                if (p.Time < FirstHelloWorldMove)
                    frames.Add((p.Time, PerRole.All.ToDictionary(r => r, r => Flat(p.Member(r)!.Position))));
            },
        });
        return frames;
    }

    private static Dictionary<PartyRole, Vector2> Seats(int strat, int seed, Action<TopP5OmegaStateOverrides> pin, float at, out ScenarioRun run)
        => Seats(strat, seed, pin, at, out run, out _);

    private static Dictionary<PartyRole, Vector2> Seats(
        int strat, int seed, Action<TopP5OmegaStateOverrides> pin, float at, out ScenarioRun run, out Vector2 beetle)
    {
        var seats = new Dictionary<PartyRole, Vector2>();
        var beetleSpot = Vector2.Zero;
        run = ScenarioRun.Execute(typeof(TopP5OmegaScenario), strat, seed, new ScenarioRunOptions
        {
            StopAt = at + 0.1f,
            Overrides = o => pin((TopP5OmegaStateOverrides)o),
            Probe = p =>
            {
                if (!p.Crossed(at)) return;
                foreach (var role in PerRole.All)
                    seats[role] = Flat(p.Member(role)!.Position);
                if (p.World.Children.OfType<SimEnemy>().SingleOrDefault(e => e.BNpcBaseId == BNpcBaseId.BeetleHelper) is { } shown)
                    beetleSpot = Flat(shown.Position);
            },
        });
        beetle = beetleSpot;
        return seats;
    }

    // Where the LPDU strat stands each sign that was placed at `markedAt`.
    private static Dictionary<Sign, Vector2> MarkedSpots(Action<TopP5OmegaStateOverrides> pin, float markedAt, float at, out int seed)
    {
        seed = Random.Shared.Next();
        var spots = new Dictionary<Sign, Vector2>();
        var run = ScenarioRun.Execute(typeof(TopP5OmegaScenario), LpduStrat, seed, new ScenarioRunOptions
        {
            StopAt = at + 0.1f,
            Overrides = o => pin((TopP5OmegaStateOverrides)o),
            Probe = p =>
            {
                if (!p.Crossed(at)) return;
                foreach (var mark in MarksSoFar(p).Where(m => m.Target is not null && m.At >= markedAt && m.At < markedAt + 0.5f))
                    spots[mark.Sign] = Flat(p.Member(mark.Target!.Value)!.Position);
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(spots.Keys, Is.EquivalentTo(HelloWorldSigns), $"the signs placed at {markedAt}");
        return spots;
    }

    internal readonly record struct Mark(float At, PartyRole? Target, Sign Sign);

    private static List<Mark> Marks(int strat, int seed, out ScenarioRun run)
    {
        var marks = new List<Mark>();
        run = ScenarioRun.Execute(typeof(TopP5OmegaScenario), strat, seed, new ScenarioRunOptions
        {
            StopAt = SecondHelloWorldMarks + 1f,
            Overrides = o => Pin((TopP5OmegaStateOverrides)o, MonitorSide.Left, Direction.N),
            Probe = p =>
            {
                if (p.Crossed(SecondHelloWorldMarks + 0.9f)) marks = MarksSoFar(p);
            },
        });
        return marks;
    }

    // Every sign placed or cleared so far, in order; a clear has no target.
    private static List<Mark> MarksSoFar(ScenarioProbe p)
    {
        var roleById = PerRole.All.ToDictionary(r => p.Member(r)!.GameObjectId.ObjectId.ToString("X8"), r => r);
        return p.Fake.Timeline.Rows
                .Where(r => r["kind"] == "ac_target" && r["id"] == "0x1F6")
                .Select(r => new Mark(float.Parse(r["t"], CultureInfo.InvariantCulture),
                                      r["target"] == "" ? null : roleById[r["target"]],
                                      (Sign)Convert.ToUInt32(r["p"].Split(',')[0], 16)))
                .ToList();
    }
}
