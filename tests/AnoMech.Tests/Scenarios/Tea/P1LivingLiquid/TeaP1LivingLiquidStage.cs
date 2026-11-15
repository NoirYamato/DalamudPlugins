using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Tea;
using AnoMech.Scenarios.Tea.P1LivingLiquid;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.P1LivingLiquid.TeaP1LivingLiquidPart;

namespace AnoMech.Tests;

// Living Liquid played with no strat, so its rules can be tested apart from one: the scenario runs with a strat index
// past its list, which seats all eight and moves nobody, and this puts every seat, by teleport, where it is safe, a
// frame after each cue. A test then moves one seat into a mistake, or withholds one cleanse.
//
// Every spot is written in the relative frame (x east, z south, with the open quarter north; the tornadoes then stand
// west, south and east, 15 y out) of the tornadoes standing at its cue: the first set until Cascade 2 lands, the
// second from then on; before the first set stands, the pull's spots are the arena's own. Times below are each part's
// own; in a run of both later parts, Cascade 2's come CascadeTwo later, and after the pull Protean 1's come ProteanOne
// later.
//
//   The pull (played only when First says so)
//     0.05  the main tank 2 y south of the middle, the rest in a knot round it; the Living Liquid, far north, walks to
//           6 y from its tank. 2.0 the main tank through it to 3.9 y north of it, which turns it north.
//     10.1  Fluid Swing north. 14.2 Cascade; 14.3 the main tank 6 y north of the middle. 19.6 the tornadoes and the
//           Liquid Hand, on the off tank in the middle.
//     24.0  the split: the main tank at the rim beside the middle tornado, and the Liquid put 6 y from it, in the edge
//           of that tornado's water; the off tank far north-east with the Hand; R1 and R2 beside the east and west
//           tornadoes; H1 north, H2 south-east, the melee south-west of the middle. 29.3 a palm.
//     30.0  the main tank Provokes the Hand, which sets off 0.67 s later; 30.75 the off tank to the Liquid's flank,
//           if the Hand has left it by then.
//     32.4  each tornado aims at its closest: R2, the main tank, R1. 32.5 the ranged step out of theirs. 32.6 the
//           dolls. 33.5 each melee half a yalm from its doll, away from the tornado beside it. 34.0 the main tank's
//           invulnerability. 34.3 each damage dealer hits the doll of its quarter (R1 north-east, M2 south-east, M1
//           south-west, R2 north-west). 34.4 the slam. 35.15 the ranged back beside their tornadoes.
//     36.4  both cleaves on the main tank. 37.5 the unseen tornado waves. 38.0 the off tank Provokes the Hand back.
//     38.5  Exhaust. 40.0 each melee past the Living Liquid from its doll, which walks through the Liquid and is fed.
//           40.4 Pressurize.
//     42.5  each tank across the middle from its boss, which follows onto it; the rest on their Protean 1 spots.
//     46.6  the tanks beside the bosses. 49.14 Exhaust. 50.0 R1 onto the Liquid and R2 onto the Hand, which feeds
//           theirs. 53.5 both back out. 55.55 and 55.8 the second cleaves.
//   Protean 1
//     0.05  the tanks beside the bosses in the middle (the main tank south-east, the off tank south-west), four in a
//           knot north of it, R1 and R2 far out north-east and north-west.
//     1.5   the tanks stack south of the Living Liquid, which turns to them; the six line up north.
//     4.8   the telegraphed waves: one south along the Liquid's facing and one at every player. 5.3 everyone steps
//           out: tanks and melee round the Liquid, the other four far out north-east and north-west.
//     9.9   set 1 at the four closest (tanks, melee) and a Sluice under each of the four farthest. 9.95 they trade
//           places. 12.6 the Sluices go off. 13.0 set 2 at the four closest (healers, ranged).
//     13.05 the main tank back beside the Liquid, the off tank south-west, the six in a knot 7 to 9.4 y north.
//     17.5  the tethers, west's on the off tank and east's on the main tank unless a test says otherwise. 24.7 Drainage.
//   Cascade 2
//     0.9   (0.05 when the run starts here) everyone within 6 y of the middle. 7.3 the second tornadoes.
//     9.5   the main tank north of the Liquid. 10.5 Throttle; the healers cleanse at 11.2, 13.7, 16.2 and 18.7, each
//           itself first, then H1 down the party list and H2 up it. 12.0 H1 and R2 behind the main tank, the rest south.
//     18.15 the telegraphed waves, north and south. 18.7 the tanks and M2 round the Liquid, M1 aside, the other four
//           beside their tornadoes: H1 and R2 the west one's, H2 the middle one's, R1 the east one's. 21.3 M1 south.
//     23.25 set 1 (the tanks, the melee) and the Sluices. 23.35 the main tank backs out north-east. 24.2 each tornado
//           aims at its closest: H1, H2, R1. 24.25 those four run in and the melee go to the middle and east tornadoes;
//           25.2 the off tank steps out south-west. 26.3 set 2 (healers, ranged). 26.9 the tornadoes' waves go off down
//           their aim. 27.0 the off tank beside the west tornado. 29.25 the unseen tornado waves: the off tank, M1, M2.
//     29.35 the tanks far apart, north-east and south-west, the six in the middle. 33.15 the Hand shows a palm.
//     33.5  each tank on the far side of the middle from its boss, which brings both onto it. 38.25 the slam.
//     39.7  the Living Liquid falls.
internal sealed class TeaP1LivingLiquidStage
{
    // A strat index past the scenario's list.
    public const int NoStrat = int.MaxValue;

    // When Cascade 2's part starts in a run that plays Protean 1 first.
    public const float CascadeTwo = 23.9f;

    // When Protean 1's part starts in a run from the pull.
    public const float ProteanOne = 60.9f;

    public static readonly PartyRole[] DefaultThrottled = [MainTank, OffTank, RegenHealer, ShieldHealer, MeleeDpsA, PhysRangedDps];

    public TeaP1LivingLiquidPart First { get; init; } = Protean1;
    public TeaP1LivingLiquidPart Last { get; init; } = Cascade2;
    public TeaP1OpenQuarter Open1 { get; init; } = TeaP1OpenQuarter.North;
    public TeaP1OpenQuarter Open2 { get; init; } = TeaP1OpenQuarter.North;

    public IReadOnlyList<TeaP1DrainageRoll> Drainage { get; init; } =
        [new(TeaP1Tornado.West, OffTank), new(TeaP1Tornado.East, MainTank)];

    public IReadOnlyList<PartyRole> Throttled { get; init; } = DefaultThrottled;

    public IReadOnlyList<TeaP1EmbolusRoll> Emboli { get; init; } =
        [new(TeaP1Tornado.West, TeaP1Boss.LiquidHand), new(TeaP1Tornado.East, TeaP1Boss.LivingLiquid)];

    public IReadOnlyList<TeaP1EmbolusRoll> FirstEmboli { get; init; } =
        [new(TeaP1Tornado.West, TeaP1Boss.LiquidHand), new(TeaP1Tornado.East, TeaP1Boss.LivingLiquid)];

    public float? EmbolusSpeed { get; init; }

    // What the stage leaves undone in the pull: the main tank's Provoke at 30.0 and invulnerability at 34.0, the off
    // tank's Provoke back at 38.0, these seats' hit on their doll at 34.3, and these seats' walk onto a boss to feed
    // theirs.
    public bool NoProvoke { get; init; }
    public bool NoInvulnerability { get; init; }
    public bool NoProvokeBack { get; init; }
    public IReadOnlyCollection<PartyRole> NoHitBy { get; init; } = [];
    public IReadOnlyCollection<PartyRole> NoFeedBy { get; init; } = [];

    // The stage presses nothing for the player's seat: a test presses its own Provoke, invulnerability and doll hit.
    public bool PlayerPressesItself { get; init; }

    // The stage's healers leave these seats' Throttle on.
    public IReadOnlyCollection<PartyRole> NotCleansed { get; init; } = [];

    // The stage makes no cleanse for the player's seat: a test presses its own.
    public bool PlayerCleansesItself { get; init; }

    // The player's seat stays on the stage until then though it is a human from the start, pressing its own buttons.
    // Null: it follows the stage until a test first takes it over.
    public float? HumanUntil { get; init; }

    public bool GodMode { get; init; }

    public bool UserActionsEnabled { get; init; }

    public Action<Configuration>? Configure { get; init; }

    // Every roll is pinned; only a tether rolled onto a dead seat draws from it.
    public int? Seed { get; init; }

    public TeaP1TornadoLayout Frame1 => new(Open1);
    public TeaP1TornadoLayout Frame2 => new(Open2);

    // A Protean 1 time on the run's clock.
    public float P1(float time) => time + (First == Pull ? ProteanOne : 0f);

    // A Cascade 2 time on the run's clock.
    public float C2(float time) => time + (First == Pull ? ProteanOne + CascadeTwo : First == Protean1 ? CascadeTwo : 0f);

    // A relative spot of the first tornadoes' frame as the arena spot a Takeover takes.
    public Vector2 Arena1(float x, float z) => Frame1.ToArena(new Vector2(x, z));

    // `radius` from `from` on relative bearing `degrees` (0 relative north, clockwise).
    public static Vector2 Off(Vector2 from, float radius, float degrees)
    {
        var radians = degrees * MathF.PI / 180f;
        return from + radius * new Vector2(MathF.Sin(radians), -MathF.Cos(radians));
    }

    public static readonly Vector2 West = new(-TeaP1Constants.P1Geometry.TornadoRingRadius, 0f);
    public static readonly Vector2 South = new(0f, TeaP1Constants.P1Geometry.TornadoRingRadius);
    public static readonly Vector2 East = new(TeaP1Constants.P1Geometry.TornadoRingRadius, 0f);

    public void Pin(TeaP1LivingLiquidStateOverrides overrides)
    {
        overrides.Parts = (First, Last);
        overrides.Tornadoes1 = Open1;
        overrides.Tornadoes2 = Open2;
        overrides.Drainage = Drainage;
        overrides.Throttled = Throttled;
        overrides.FirstEmboli = FirstEmboli;
        overrides.Emboli = Emboli;
        overrides.EmbolusSpeed = EmbolusSpeed;
    }

    // Ends as a scenario run does: after the Liquid falls, at `stopAt`, or 5 s after the first death.
    public ScenarioRun Run(PartyRole player, float? stopAt = null, Action<ScenarioProbe>? probe = null, params Takeover[] takeovers)
        => ScenarioRun.Execute(typeof(TeaP1LivingLiquidScenario), NoStrat, Seed ?? Random.Shared.Next(), new ScenarioRunOptions
        {
            PlayerRole = player,
            StopAt = stopAt,
            GodMode = GodMode,
            UserActionsEnabled = UserActionsEnabled,
            Configure = Configure,
            WriteArtifactsOnFailure = false,
            Overrides = o => Pin((TeaP1LivingLiquidStateOverrides)o),
            Takeovers = HumanUntil is null ? takeovers : takeovers.Prepend(new Takeover(0f, null)).ToList(),
            Probe = p =>
            {
                Follow(p);
                probe?.Invoke(p);
            },
        });

    public static TeaP1LivingLiquidScenario Scenario(ScenarioProbe p) => p.Game.Catalog.Scenarios.OfType<TeaP1LivingLiquidScenario>().Single();

    public static TeaP1LivingLiquid Fight(ScenarioProbe p) => Scenario(p).Fight!;

    // Called every frame, puts every seat the stage still moves where the cues just crossed ask, and makes the
    // healers' cleanses.
    public void Follow(ScenarioProbe p)
    {
        foreach (var cue in cues ??= Cues().ToList())
            if (p.Crossed(cue.At) && cue.When?.Invoke(p) != false) Put(p, cue);
        if (First == Pull) PressForThePull(p);
        if (Last != Cascade2) return;
        foreach (var at in new[] { 11.2f, 13.7f, 16.2f, 18.7f })
            if (p.Crossed(C2(at))) Cleanse(p);
    }

    private List<Cue>? cues;

    // TrueNorth: its spots are the arena's own, as before any tornado stands. When: it is skipped unless this holds as
    // its time comes.
    internal sealed record Cue(float At, bool SecondFrame, Func<Vector2, Vector2, Dictionary<PartyRole, Vector2>> Spots, bool TrueNorth = false,
        Func<ScenarioProbe, bool>? When = null);

    private static readonly Vector2 MainTanksHold = Off(Vector2.Zero, 3.9f, 135f);

    // Where each boss stops from its tank: its hitbox, the tank's half yalm and 2 y.
    public const float LiquidStopsFromItsTank = 6.0f;
    public const float HandStopsFromItsTank = 4.1f;

    private static Dictionary<PartyRole, Vector2> InTheMiddle() => new()
    {
        [RegenHealer] = new(-1.1f, -4.2f), [ShieldHealer] = new(1.1f, -5.2f), [MeleeDpsA] = new(-0.3f, -5.6f), [MeleeDpsB] = new(0.4f, -4.3f),
        [PhysRangedDps] = new(1.6f, -3.4f), [CasterDps] = new(-1.6f, -3.2f), [OffTank] = new(-3.0f, 3.0f), [MainTank] = MainTanksHold,
    };

    public static readonly IReadOnlyDictionary<PartyRole, TeaP1Quarter> DollOf = new Dictionary<PartyRole, TeaP1Quarter>
    {
        [PhysRangedDps] = TeaP1Quarter.Northeast, [MeleeDpsB] = TeaP1Quarter.Southeast,
        [MeleeDpsA] = TeaP1Quarter.Southwest, [CasterDps] = TeaP1Quarter.Northwest,
    };

    // Where the doll of `quarter` appears, in the first tornadoes' frame.
    public Vector2 DollSpot(TeaP1Quarter quarter)
        => TeaP1Constants.P1Geometry.JagdDollBearings
            .Select(bearing => bearing * MathF.PI / 180f)
            .Select(radians => Frame1.ToRelative(new Vector2(MathF.Sin(radians), -MathF.Cos(radians)) * TeaP1Constants.P1Geometry.JagdDollRing))
            .Single(spot => quarter == (spot.Y < 0f
                ? spot.X >= 0f ? TeaP1Quarter.Northeast : TeaP1Quarter.Northwest
                : spot.X >= 0f ? TeaP1Quarter.Southeast : TeaP1Quarter.Southwest));

    // Half a yalm off its doll's spot, straight away from the tornado nearest that spot.
    public Vector2 BesideItsDoll(PartyRole melee)
    {
        var doll = DollSpot(DollOf[melee]);
        var nearest = new[] { West, South, East }.MinBy(tornado => Vector2.Distance(tornado, doll));
        return doll + 0.5f * Vector2.Normalize(doll - nearest);
    }

    public static readonly Vector2 MainTanksBait = Off(South, 8.4f, 258f);
    public static readonly Vector2 LiquidParksBesideTheMiddleTornado = new(-6.0f, 11.2f);
    public static readonly Vector2 OffTanksSplit = Off(Vector2.Zero, 11.5f, 39f);
    public static readonly Vector2 OffTanksFlank = new(-9.5f, 9.0f);
    public static readonly Vector2 FirstRangedsBait = Off(East, 8.4f, 10f);
    public static readonly Vector2 SecondRangedsBait = Off(West, 8.4f, 350f);

    private IEnumerable<Cue> PullCues()
    {
        yield return new(0.05f, false, (_, _) => new()
        {
            [MainTank] = new(0f, 2.0f), [OffTank] = new(0f, 0f), [MeleeDpsA] = new(2.2f, 0.6f), [MeleeDpsB] = new(-2.7f, 0.6f),
            [RegenHealer] = new(0.2f, 2.9f), [ShieldHealer] = new(2.3f, 2.7f), [PhysRangedDps] = new(-3.4f, 3.2f), [CasterDps] = new(-1.8f, 2.9f),
        }, TrueNorth: true);
        yield return new(2.0f, false, (_, _) => new() { [MainTank] = new(0f, -7.9f) }, TrueNorth: true);
        yield return new(14.3f, false, (_, _) => new() { [MainTank] = new(0f, -6.0f) }, TrueNorth: true);
        yield return new(24.0f, false, (_, _) => new()
        {
            [MainTank] = MainTanksBait, [OffTank] = OffTanksSplit,
            [RegenHealer] = new(0f, -14.3f), [ShieldHealer] = new(9.6f, 10.8f), [MeleeDpsA] = new(-5.5f, 4.5f), [MeleeDpsB] = new(-3.2f, 5.2f),
            [PhysRangedDps] = FirstRangedsBait, [CasterDps] = SecondRangedsBait,
        });
        yield return new(30.75f, false, (_, _) => new() { [OffTank] = OffTanksFlank },
            When: p => !ReferenceEquals(Fight(p).Target(TeaP1Boss.LiquidHand), p.Member(OffTank)));
        yield return new(32.5f, false, (_, _) => new() { [PhysRangedDps] = new(11.5f, -8.4f), [CasterDps] = new(-11.5f, -8.4f) });
        yield return new(33.5f, false, (_, _) => new() { [MeleeDpsA] = BesideItsDoll(MeleeDpsA), [MeleeDpsB] = BesideItsDoll(MeleeDpsB) });
        yield return new(35.15f, false, (_, _) => new() { [PhysRangedDps] = FirstRangedsBait, [CasterDps] = SecondRangedsBait });
        yield return new(40.0f, false, (liquid, _) => Feeding(new()
        {
            [MeleeDpsA] = PastTheBossFromItsDoll(liquid, DollSpot(DollOf[MeleeDpsA])), [MeleeDpsB] = PastTheBossFromItsDoll(liquid, DollSpot(DollOf[MeleeDpsB])),
        }));
        yield return new(42.5f, false, (liquid, hand) => new()
        {
            [MainTank] = AcrossTheMiddleFrom(liquid, LiquidStopsFromItsTank), [OffTank] = AcrossTheMiddleFrom(hand, HandStopsFromItsTank),
            [RegenHealer] = new(-1.1f, -4.2f), [ShieldHealer] = new(1.1f, -5.2f), [MeleeDpsA] = new(-0.3f, -5.6f), [MeleeDpsB] = new(0.4f, -4.3f),
            [PhysRangedDps] = new(11.0f, -7.8f), [CasterDps] = new(-10.3f, -8.1f),
        });
        yield return new(46.6f, false, (liquid, _) => new() { [MainTank] = Off(liquid, 3.9f, 135f), [OffTank] = new(-3.0f, 3.0f) });
        yield return new(50.0f, false, (liquid, hand) => Feeding(new() { [PhysRangedDps] = liquid, [CasterDps] = hand }));
        yield return new(53.5f, false, (_, _) => new() { [PhysRangedDps] = new(11.0f, -7.8f), [CasterDps] = new(-10.3f, -8.1f) });
    }

    // A doll walks straight at its holder and stops short of it, so a holder past a boss draws its doll through that
    // boss. The Living Liquid parks inside the middle tornado's water: 2.5 y past its centre on the far side from the
    // doll, turned round the boss in steps of 20 degrees until the spot is clear of every tornado's water.
    public static Vector2 PastTheBossFromItsDoll(Vector2 boss, Vector2 doll)
    {
        var straight = MathF.Atan2(boss.X - doll.X, doll.Y - boss.Y) * 180f / MathF.PI;
        foreach (var turn in new[] { 0f, 20f, -20f, 40f, -40f, 60f, -60f, 80f, -80f, 100f, -100f, 120f, -120f })
        {
            var spot = Off(boss, 2.5f, straight + turn);
            if (spot.Length() <= 19.3f && new[] { West, South, East }.All(tornado => Vector2.Distance(tornado, spot) >= 8.3f)) return spot;
        }
        return Off(boss, 2.5f, straight);
    }

    private Dictionary<PartyRole, Vector2> Feeding(Dictionary<PartyRole, Vector2> spots)
    {
        foreach (var seat in NoFeedBy) spots.Remove(seat);
        return spots;
    }

    // The stage's own presses of the pull, each left to a seat a test plays.
    private void PressForThePull(ScenarioProbe p)
    {
        var scenario = Scenario(p);
        var (fight, presses) = (scenario.Fight!, scenario.Presses!);
        if (p.Crossed(30.0f) && !NoProvoke && Presses(p, MainTank) is { } provoking && fight.Hand is { } taken) presses.Provoke(provoking, taken);
        // A boss walks straight at a tank that was teleported, so where it would park depends on where it came from:
        // the Liquid is put where it trails a main tank that ran to the wall, whatever the layout.
        if (p.Crossed(24.0f) && fight.Liquid is { } liquid && FollowsTheStage(p, p.Member(MainTank)!, 24.0f))
        {
            var parks = Frame1.ToArena(LiquidParksBesideTheMiddleTornado);
            liquid.StopMoving();
            liquid.SetPosition(new Vector3(parks.X, 0f, parks.Y));
        }
        if (p.Crossed(34.0f) && !NoInvulnerability && Presses(p, MainTank) is { } invulnerable) presses.Invulnerability(invulnerable);
        if (p.Crossed(38.0f) && !NoProvokeBack && Presses(p, OffTank) is { } takingBack && fight.Hand is { } back) presses.Provoke(takingBack, back);
        if (!p.Crossed(34.3f)) return;
        foreach (var (seat, quarter) in DollOf)
            if (!NoHitBy.Contains(seat) && Presses(p, seat) is { } hitting && fight.Dolls.In(Frame1, quarter) is { } doll)
                presses.Strike(hitting, doll.Enemy);
    }

    private SimCharacter? Presses(ScenarioProbe p, PartyRole seat)
    {
        if (p.Member(seat) is not { } member || !member.IsAlive() || !FollowsTheStage(p, member, p.Time)) return null;
        return PlayerPressesItself && ReferenceEquals(member, p.World.Party.Player) ? null : member;
    }

    // Each takes the Living Liquid's and the Liquid Hand's spots in its frame.
    internal IEnumerable<Cue> Cues()
    {
        if (First == Pull)
            foreach (var cue in PullCues())
                yield return cue;
        if (First <= Protean1 && Last >= Protean1)
        {
            yield return new(P1(0.05f), false, (_, _) => new()
            {
                [MainTank] = MainTanksHold, [OffTank] = new(-3.0f, 3.0f),
                [RegenHealer] = new(-1.1f, -4.2f), [ShieldHealer] = new(1.1f, -5.2f), [MeleeDpsA] = new(-0.3f, -5.6f), [MeleeDpsB] = new(0.4f, -4.3f),
                [PhysRangedDps] = new(11.0f, -7.8f), [CasterDps] = new(-10.3f, -8.1f),
            });
            yield return new(P1(1.5f), false, (liquid, _) => new()
            {
                [MainTank] = Off(liquid, 3.3f, 180f), [OffTank] = Off(liquid, 4.6f, 180f),
                [MeleeDpsB] = liquid + new Vector2(0.3f, -4.2f), [RegenHealer] = liquid + new Vector2(-1.1f, -4.2f),
                [MeleeDpsA] = liquid + new Vector2(-0.6f, -5.1f), [ShieldHealer] = liquid + new Vector2(0.6f, -5.6f),
                [CasterDps] = liquid + new Vector2(-0.6f, -6.8f), [PhysRangedDps] = liquid + new Vector2(-0.4f, -7.8f),
            });
            yield return new(P1(5.3f), false, (liquid, _) => new()
            {
                [MainTank] = liquid + new Vector2(2.9f, 3.2f), [OffTank] = liquid + new Vector2(-3.1f, 3.0f),
                [MeleeDpsA] = liquid + new Vector2(-4.7f, -0.1f), [MeleeDpsB] = liquid + new Vector2(4.0f, 0.1f),
                [PhysRangedDps] = new(10.1f, -8.7f), [ShieldHealer] = new(11.8f, -9.4f), [CasterDps] = new(-11.0f, -9.5f), [RegenHealer] = new(-8.9f, -8.4f),
            });
            yield return new(P1(9.95f), false, (liquid, _) => new()
            {
                [RegenHealer] = liquid + new Vector2(-3.5f, -2.2f), [ShieldHealer] = liquid + new Vector2(2.4f, -2.2f),
                [CasterDps] = liquid + new Vector2(-5.0f, 0.6f), [PhysRangedDps] = liquid + new Vector2(3.7f, 0.3f),
                [MainTank] = new(5.0f, 5.5f), [OffTank] = new(-5.4f, 5.4f), [MeleeDpsA] = new(-6.7f, 7.1f), [MeleeDpsB] = new(6.4f, 7.4f),
            });
            yield return new(P1(13.05f), false, (_, _) => new()
            {
                [MainTank] = MainTanksHold, [OffTank] = new(-5.4f, 5.4f),
                [RegenHealer] = new(-1.3f, -6.9f), [ShieldHealer] = new(0f, -8.0f), [MeleeDpsA] = new(-1.5f, -7.5f), [MeleeDpsB] = new(-0.2f, -8.9f),
                [PhysRangedDps] = new(-0.6f, -9.0f), [CasterDps] = new(-1.2f, -9.4f),
            });
        }
        if (Last != Cascade2) yield break;

        yield return new(C2(First == Cascade2 ? 0.05f : 0.9f), false, (_, _) => InTheMiddle());
        yield return new(C2(9.5f), true, (liquid, _) => new() { [MainTank] = Off(liquid, 3.9f, 0f) });
        yield return new(C2(12.0f), true, (liquid, _) => new()
        {
            [RegenHealer] = Off(liquid, 5.7f, 0f), [CasterDps] = Off(liquid, 7.5f, 0f),
            [ShieldHealer] = Off(liquid, 3.2f, 180f), [MeleeDpsA] = Off(liquid, 3.8f, 180f), [PhysRangedDps] = Off(liquid, 4.4f, 180f),
            [MeleeDpsB] = Off(liquid, 5.0f, 180f), [OffTank] = Off(liquid, 5.6f, 180f),
        });
        yield return new(C2(18.7f), true, (liquid, _) => new()
        {
            [MainTank] = Off(liquid, 4.3f, 40f), [MeleeDpsB] = Off(liquid, 3.4f, 85f), [OffTank] = Off(liquid, 4.0f, 270f), [MeleeDpsA] = Off(liquid, 4.0f, 215f),
            [RegenHealer] = Off(West, 8.5f, 17.3f), [CasterDps] = Off(West, 10.0f, 17.8f), [ShieldHealer] = Off(South, 8.5f, 273.4f), [PhysRangedDps] = Off(East, 8.4f, 175.4f),
        });
        yield return new(C2(21.3f), true, (liquid, _) => new() { [MeleeDpsA] = Off(liquid, 4.0f, 180f) });
        yield return new(C2(23.35f), true, (liquid, _) => new() { [MainTank] = Off(liquid, 8.5f, 43f) });
        yield return new(C2(24.25f), true, (liquid, _) => new()
        {
            [RegenHealer] = Off(liquid, 4.95f, 317f), [CasterDps] = Off(liquid, 4.0f, 275f), [PhysRangedDps] = Off(liquid, 3.2f, 95f), [ShieldHealer] = Off(liquid, 3.8f, 180f),
            [MeleeDpsA] = Off(South, 8.4f, 80f), [MeleeDpsB] = Off(East, 8.4f, 8.7f),
        });
        yield return new(C2(25.2f), true, (_, _) => new() { [OffTank] = new(-7.5f, 7.5f) });
        yield return new(C2(27.0f), true, (_, _) => new() { [OffTank] = Off(West, 8.4f, 187.7f) });
        yield return new(C2(29.35f), true, (_, _) => new()
        {
            [MainTank] = new(10.2f, -11.9f), [OffTank] = new(-13.0f, 10.8f),
            [MeleeDpsA] = new(0.8f, -5.7f), [MeleeDpsB] = new(4.5f, -3.7f), [RegenHealer] = new(-0.4f, -2.3f), [ShieldHealer] = new(-0.2f, 0.4f),
            [PhysRangedDps] = new(1.8f, -1.4f), [CasterDps] = new(-2.2f, -0.8f),
        });
        yield return new(C2(33.5f), true, (liquid, hand) => new()
        {
            [MainTank] = AcrossTheMiddleFrom(liquid, LiquidStopsFromItsTank), [OffTank] = AcrossTheMiddleFrom(hand, HandStopsFromItsTank),
            [MeleeDpsA] = new(-0.3f, -3.8f), [MeleeDpsB] = new(2.3f, -2.3f), [PhysRangedDps] = new(0.9f, -1.4f), [CasterDps] = new(-0.7f, -1.0f),
        });
    }

    private static Vector2 AcrossTheMiddleFrom(Vector2 boss, float distance)
        => boss.LengthSquared() < 1e-4f ? new Vector2(0f, distance) : -Vector2.Normalize(boss) * distance;

    // A cue's spots in the arena, with the bosses where they stand now.
    internal Dictionary<PartyRole, Vector2> ArenaSpots(ScenarioProbe p, Cue cue)
    {
        var frame = cue.TrueNorth ? new TeaP1TornadoLayout(TeaP1OpenQuarter.North) : cue.SecondFrame ? Frame2 : Frame1;
        var fight = Fight(p);
        var spots = cue.Spots(frame.ToRelative(fight.Position(TeaP1Boss.LivingLiquid)), frame.ToRelative(fight.Position(TeaP1Boss.LiquidHand)));
        return spots.ToDictionary(spot => spot.Key, spot => frame.ToArena(spot.Value));
    }

    private void Put(ScenarioProbe p, Cue cue)
    {
        foreach (var (role, spot) in ArenaSpots(p, cue))
        {
            if (p.Member(role) is not { } member || !FollowsTheStage(p, member, cue.At)) continue;
            member.StopMoving();
            member.SetPosition(new Placement(new Vector3(spot.X, 0f, spot.Y), 0f).Face(Vector3.Zero));
        }
    }

    private bool FollowsTheStage(ScenarioProbe p, SimCharacter member, float cue)
    {
        if (!ReferenceEquals(member, p.World.Party.Player)) return true;
        return HumanUntil is { } until ? cue < until : p.World.Party.IsBotDriven(member);
    }

    // Both healers at once, each taking the next seat of its order that still carries Throttle; a healer's seat a
    // human has taken over presses for itself, and so does the player's with PlayerCleansesItself.
    private void Cleanse(ScenarioProbe p)
    {
        var scenario = Scenario(p);
        var taken = new List<SimCharacter>();
        foreach (var healer in new[] { RegenHealer, ShieldHealer })
        {
            if (p.Member(healer) is not { } by || !by.IsAlive() || !FollowsTheStage(p, by, p.Time)) continue;
            if (PlayerCleansesItself && ReferenceEquals(by, p.World.Party.Player)) continue;
            var next = scenario.State!.Plan.CleanseOrder(healer)
                .Where(seat => !NotCleansed.Contains(seat))
                .Select(p.Member)
                .FirstOrDefault(member => member is not null && member.IsAlive() && scenario.Fight!.IsThrottled(member) && !taken.Contains(member));
            if (next is null) continue;
            taken.Add(next);
            scenario.Presses!.Cleanse(by, next);
        }
    }
}
