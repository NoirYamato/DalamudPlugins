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
// west, south and east, 14.5 y out) of the tornadoes standing at its cue: the first set until Cascade 2 lands, the
// second from then on. Times below are each part's own; in a run of both, Cascade 2's come CascadeTwo later.
//
//   Protean 1
//     0.05  the tanks beside the bosses in the middle (the main tank south-east, the off tank south-west), four in a
//           knot north of it, R1 and R2 far out north-east and north-west.
//     1.5   the tanks stack south of the Living Liquid, which turns to them; the six line up north.
//     4.8   the telegraphed waves: one south along the Liquid's facing and one at every player. 5.3 everyone steps
//           out: tanks and melee round the Liquid, the other four far out north-east and north-west.
//     9.9   set 1 at the four closest (tanks, melee) and a Sluice under each of the four farthest. 9.95 they trade
//           places. 12.6 the Sluices go off. 13.0 set 2 at the four closest (healers, ranged).
//     13.05 the main tank back beside the Liquid, the off tank south-west, the six in a knot 7 to 9.4 y north.
//     20.0  the tethers, west's on the off tank and east's on the main tank unless a test says otherwise. 24.6 Drainage.
//   Cascade 2
//     0.9   (0.05 when the run starts here) everyone within 6 y of the middle. 5.9 the second tornadoes.
//     9.5   the main tank north of the Liquid. 10.5 Throttle; the healers cleanse at 11.2, 13.7, 16.2 and 18.7, each
//           itself first, then H1 down the party list and H2 up it. 12.0 H1 and R2 behind the main tank, the rest south.
//     18.2  the telegraphed waves, north and south. 18.7 the tanks and M2 round the Liquid, M1 aside, the other four
//           beside their tornadoes: H1 and R2 the west one's, H2 the middle one's, R1 the east one's. 21.3 M1 south.
//     23.3  set 1 (the tanks, the melee) and the Sluices. 23.35 the main tank backs out north-east. 24.2 each tornado
//           aims at its closest: H1, H2, R1. 24.25 those four run in and the melee go to the middle and east tornadoes;
//           25.2 the off tank steps out south-west. 26.3 set 2 (healers, ranged). 26.9 the tornadoes' waves go off down
//           their aim. 27.0 the off tank beside the west tornado. 29.3 the unseen tornado waves: the off tank, M1, M2.
//     29.35 the tanks far apart, north-east and south-west, the six in the middle. 33.2 the Hand shows a palm.
//     33.5  each tank on the far side of the middle from its boss, which brings both onto it. 38.4 the slam.
//     39.7  the Living Liquid falls.
internal sealed class TeaP1LivingLiquidStage
{
    // A strat index past the scenario's list.
    public const int NoStrat = int.MaxValue;

    // When Cascade 2's part starts in a run that plays Protean 1 first.
    public const float CascadeTwo = 23.8f;

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

    public float? EmbolusSpeed { get; init; }

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

    // Nothing draws from it: every roll is pinned.
    public int? Seed { get; init; }

    public TeaP1TornadoLayout Frame1 => new(Open1);
    public TeaP1TornadoLayout Frame2 => new(Open2);

    // A Protean 1 time on the run's clock.
    public float P1(float time) => time;

    // A Cascade 2 time on the run's clock.
    public float C2(float time) => time + (First == Protean1 ? CascadeTwo : 0f);

    // A relative spot of the first, or the second, tornadoes' frame as the arena spot a Takeover takes.
    public Vector2 Arena1(float x, float z) => Frame1.ToArena(new Vector2(x, z));
    public Vector2 Arena2(float x, float z) => Frame2.ToArena(new Vector2(x, z));

    // `radius` from `from` on relative bearing `degrees` (0 relative north, clockwise).
    public static Vector2 Off(Vector2 from, float radius, float degrees)
    {
        var radians = degrees * MathF.PI / 180f;
        return from + radius * new Vector2(MathF.Sin(radians), -MathF.Cos(radians));
    }

    public static readonly Vector2 West = new(-14.5f, 0f);
    public static readonly Vector2 South = new(0f, 14.5f);
    public static readonly Vector2 East = new(14.5f, 0f);

    public void Pin(TeaP1LivingLiquidStateOverrides overrides)
    {
        overrides.Parts = (First, Last);
        overrides.Tornadoes1 = Open1;
        overrides.Tornadoes2 = Open2;
        overrides.Drainage = Drainage;
        overrides.Throttled = Throttled;
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
            if (p.Crossed(cue.At)) Put(p, cue);
        if (Last != Cascade2) return;
        foreach (var at in new[] { 11.2f, 13.7f, 16.2f, 18.7f })
            if (p.Crossed(C2(at))) Cleanse(p);
    }

    private List<Cue>? cues;

    internal sealed record Cue(float At, bool SecondFrame, Func<Vector2, Vector2, Dictionary<PartyRole, Vector2>> Spots);

    private static readonly Vector2 MainTanksHold = Off(Vector2.Zero, 3.9f, 135f);

    private static Dictionary<PartyRole, Vector2> InTheMiddle() => new()
    {
        [RegenHealer] = new(-1.1f, -4.2f), [ShieldHealer] = new(1.1f, -5.2f), [MeleeDpsA] = new(-0.3f, -5.6f), [MeleeDpsB] = new(0.4f, -4.3f),
        [PhysRangedDps] = new(1.6f, -3.4f), [CasterDps] = new(-1.6f, -3.2f), [OffTank] = new(-3.0f, 3.0f), [MainTank] = MainTanksHold,
    };

    // Each takes the Living Liquid's and the Liquid Hand's spots in its frame.
    internal IEnumerable<Cue> Cues()
    {
        if (First == Protean1)
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

        yield return new(First == Cascade2 ? 0.05f : C2(0.9f), false, (_, _) => InTheMiddle());
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
            [RegenHealer] = Off(West, 8.4f, 14f), [CasterDps] = Off(West, 9.9f, 15f), [ShieldHealer] = Off(South, 8.5f, 270f), [PhysRangedDps] = Off(East, 8.4f, 172f),
        });
        yield return new(C2(21.3f), true, (liquid, _) => new() { [MeleeDpsA] = Off(liquid, 4.0f, 180f) });
        yield return new(C2(23.35f), true, (liquid, _) => new() { [MainTank] = Off(liquid, 8.5f, 43f) });
        yield return new(C2(24.25f), true, (liquid, _) => new()
        {
            [RegenHealer] = Off(liquid, 4.95f, 317f), [CasterDps] = Off(liquid, 4.0f, 275f), [PhysRangedDps] = Off(liquid, 3.2f, 95f), [ShieldHealer] = Off(liquid, 3.8f, 180f),
            [MeleeDpsA] = Off(South, 8.4f, 90f), [MeleeDpsB] = Off(East, 8.5f, 12f),
        });
        yield return new(C2(25.2f), true, (_, _) => new() { [OffTank] = new(-7.5f, 7.5f) });
        yield return new(C2(27.0f), true, (_, _) => new() { [OffTank] = Off(West, 8.5f, 191f) });
        yield return new(C2(29.35f), true, (_, _) => new()
        {
            [MainTank] = new(8.1f, -9.4f), [OffTank] = new(-13.0f, 10.8f),
            [MeleeDpsA] = new(0.8f, -5.7f), [MeleeDpsB] = new(4.5f, -3.7f), [RegenHealer] = new(-0.4f, -2.3f), [ShieldHealer] = new(-0.2f, 0.4f),
            [PhysRangedDps] = new(1.8f, -1.4f), [CasterDps] = new(-2.2f, -0.8f),
        });
        yield return new(C2(33.5f), true, (liquid, hand) => new()
        {
            [MainTank] = AcrossTheMiddleFrom(liquid, 4.0f), [OffTank] = AcrossTheMiddleFrom(hand, 2.1f),
            [MeleeDpsA] = new(-0.3f, -3.8f), [MeleeDpsB] = new(2.3f, -2.3f), [PhysRangedDps] = new(0.9f, -1.4f), [CasterDps] = new(-0.7f, -1.0f),
        });
    }

    private static Vector2 AcrossTheMiddleFrom(Vector2 boss, float distance)
        => boss.LengthSquared() < 1e-4f ? new Vector2(0f, distance) : -Vector2.Normalize(boss) * distance;

    // A cue's spots in the arena, with the bosses where they stand now.
    internal Dictionary<PartyRole, Vector2> ArenaSpots(ScenarioProbe p, Cue cue)
    {
        var frame = cue.SecondFrame ? Frame2 : Frame1;
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
