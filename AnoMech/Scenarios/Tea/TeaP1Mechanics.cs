using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Geometry;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using Actions = AnoMech.Scenarios.Tea.TeaP1Actions;

namespace AnoMech.Scenarios.Tea;

public enum TeaP1Boss { LivingLiquid, LiquidHand }

// The three Liquid Rages, named from the quarter that has none: facing out through it, the one on the left is West.
public enum TeaP1Tornado { West, Middle, East }

// The quarter of the arena with no tornado.
public enum TeaP1OpenQuarter { North, Northeast, East, Southeast, South, Southwest, West, Northwest }

// One Drainage tether: the tornado it comes from and the seat it starts on.
public sealed record TeaP1DrainageRoll(TeaP1Tornado From, PartyRole FirstTarget);

// One Embolus: the tornado it leaves and the boss whose spot it heads for.
public sealed record TeaP1EmbolusRoll(TeaP1Tornado From, TeaP1Boss AimsAt);

// Where one set of tornadoes stands, and the frame the fight is read in while it does: relative north is the open
// quarter, with the West, Middle and East tornadoes to the relative west, south and east.
public sealed record TeaP1TornadoLayout(TeaP1OpenQuarter Open)
{
    // Degrees clockwise from true north.
    public float OpenBearing => 45f * (int)Open;

    // What turns a relative spot into an arena one, as IAiPositions.Rotate takes it.
    public float Radians => OpenBearing * MathF.PI / 180f;

    public Vector3 Centre(TeaP1Tornado tornado) => At(tornado switch
    {
        TeaP1Tornado.West => 270f,
        TeaP1Tornado.Middle => 180f,
        _ => 90f,
    }, P1Geometry.TornadoRingRadius);

    // The arena spot `radius` from the centre on relative bearing `degrees` (0 relative north, clockwise).
    public Vector3 At(float degrees, float radius)
    {
        var radians = (OpenBearing + degrees) * MathF.PI / 180f;
        return new Vector3(MathF.Sin(radians), 0f, -MathF.Cos(radians)) * radius;
    }

    // The rotation of someone looking along relative bearing `degrees`, in Placement's convention.
    public float Facing(float degrees)
    {
        var radians = (OpenBearing + degrees) * MathF.PI / 180f;
        return MathF.Atan2(MathF.Sin(radians), -MathF.Cos(radians));
    }

    public Vector2 ToArena(Vector2 relative) => Turned(relative, Radians);

    public Vector2 ToRelative(Vector2 arena) => Turned(arena, -Radians);

    public Vector2 ToRelative(Vector3 arena) => ToRelative(new Vector2(arena.X, arena.Z));

    private static Vector2 Turned(Vector2 spot, float radians)
    {
        var (sin, cos) = MathF.SinCos(radians);
        return new Vector2(spot.X * cos - spot.Y * sin, spot.X * sin + spot.Y * cos);
    }
}

// The Liquid Rages standing now and the water round each. A puddle kills whoever enters it, from
// P1Duration.PuddleKillsAfter after it appeared: a set can appear under someone who had no way to know where.
public sealed class TeaP1Tornadoes(SimWorld world)
{
    internal const string StoodInThePuddle = "Stood in a Liquid Rage's water: each of the three tornadoes keeps a puddle round it";

    // Bots walk round a puddle this much wider than it.
    private const float BotsKeepClearBy = 0.2f;
    private const float PuddleFadeSeconds = 1.3f;

    private sealed record Tornado(TeaP1Tornado Which, Vector3 Centre, SimEnemy? Enemy, SimEventObject? Puddle, CircleObstacle Obstacle)
    {
        public HashSet<SimCharacter> Inside { get; set; } = [];
    }

    private readonly List<Tornado> tornadoes = [];
    private float appearedAt;

    public TeaP1TornadoLayout? Standing { get; private set; }

    public SimEnemy? this[TeaP1Tornado which] => tornadoes.FirstOrDefault(tornado => tornado.Which == which)?.Enemy;

    public Vector3? Centre(TeaP1Tornado which) => Standing?.Centre(which);

    public void Appear(TeaP1TornadoLayout layout)
    {
        Leave();
        Standing = layout;
        appearedAt = world.Events.Elapsed;
        foreach (var which in Enum.GetValues<TeaP1Tornado>())
        {
            var centre = layout.Centre(which);
            var enemy = world.SpawnEnemy(new EnemySpawnConfig(
                BNpcBaseId: P1BNpcBaseId.LiquidRage,
                NameId: P1BNpcNameId.LiquidRage,
                Level: Level,
                Targetable: false,
                EnemyList: EnemyListMode.Never,
                Visibility: SpawnVisibility.Visible,
                Placement: new Placement(centre, 0f)));
            var puddle = world.SpawnEventObject(new EventObjectSpawnConfig
            {
                EObjId = P1EObjId.LiquidRagePuddle,
                Placement = new Placement(centre, 0f),
            });
            var obstacle = world.Obstacles.Add(new CircleObstacle(new Vector2(centre.X, centre.Z), P1Geometry.PuddleRadius + BotsKeepClearBy));
            tornadoes.Add(new Tornado(which, centre, enemy, puddle, obstacle));
        }
    }

    public void Leave()
    {
        foreach (var tornado in tornadoes)
        {
            tornado.Enemy?.FadeOut();
            if (tornado.Puddle is { } puddle)
            {
                puddle.FadeOut();
                world.Events.Add(PuddleFadeSeconds, puddle.Despawn);
            }
            world.Obstacles.Remove(tornado.Obstacle);
        }
        tornadoes.Clear();
        Standing = null;
    }

    // Whoever a puddle already holds as it starts to kill counts as entering it then, and godmode hears of an entry
    // once however long it lasts.
    internal void KillWhoeverEntersAPuddle()
    {
        if (world.Events.Elapsed < appearedAt + P1Duration.PuddleKillsAfter) return;
        foreach (var tornado in tornadoes)
        {
            var inside = world.Party.ActiveMembers()
                .Where(member => TeaP1LivingLiquid.Apart(member.Position, tornado.Centre) <= P1Geometry.PuddleRadius)
                .ToHashSet();
            foreach (var entered in inside.Where(member => !tornado.Inside.Contains(member)).ToList())
                entered.Die(SimCharacterDeathExtensions.Environment, StoodInThePuddle);
            tornado.Inside = inside;
        }
    }
}

// The Living Liquid and the Liquid Hand, the tornadoes, and the attacks the phase plays. Each is scheduled from a time
// on the run's own clock, so a scenario's Run reads as the fight's timeline:
//
//     fight.Spawn(liquid, hand);
//     fight.ProteanWave(4.8f);
//     fight.HandOfPain(5.0f);
//
// No health is played: the raidwides are harmless and the Liquid falls on the timeline. Each boss faces the one target
// the scenario gives it and walks after it once it is out of reach. A mistake kills by the fight's own rule, with the
// reason. Every helper cast goes through CastFromHelper.

public sealed class TeaP1LivingLiquid
{
    internal const string NonTankHoldsATether =
        "Held a Drainage tether as a non-tank: each of the two tethers hits its holder like a tank buster, and a tether passes to whoever steps into it";
    internal const string InSomeoneElsesDrainage =
        "Stood in a Drainage aimed at someone else: it hits everyone within 6 y of its tether's holder";
    internal const string TwoDrainages = "Took two Drainages: the two tether holders must stand more than 6 y apart";
    internal const string DrainageWithoutMitigation = "Took Drainage without enough mitigation: it needs a tank's cooldown";
    internal const string ThrottleRanOut = "Throttle ran out: it kills unless Esuna or the Warden's Paean takes it off first";
    internal const string PalmFoundThemApart =
        "The Liquid Hand slammed as an open palm with the bosses apart: a palm, shown when they stand far apart as it glows, needs the Hand brought onto the Living Liquid before it lands";
    internal const string FistFoundThemTogether =
        "The Liquid Hand slammed as a fist with the bosses together: a fist, shown when they stand close as it glows, needs them far apart as it lands";
    internal const string EmbolusTouched = "An Embolus was touched: a player or a boss that reaches one bursts it, and that kills everyone";

    private const int WavesPerSet = 4;
    private const int Sluices = 4;
    private const int SplashCount = 6;
    private const float SplashSpacing = 1.12f;
    private const float StopsWithin = 0.05f;


    private readonly SimWorld world;
    private readonly TeaP1Presses presses;
    private readonly SpawnedHelperPool helpers;
    private readonly ThrottleCarriers throttle;
    private readonly EnemyAction waveFromTheLiquid;
    private readonly EnemyAction waveAtAPlayer;
    private readonly EnemyAction tornadoWave;

    private readonly SimEnemy?[] bosses = new SimEnemy?[2];
    private readonly Vector3[] lastSeenAt = new Vector3[2];
    private readonly SimCharacter?[] targets = new SimCharacter?[2];
    private readonly bool[] walksAfterItsTarget = new bool[2];
    private readonly bool[] hasYetToBeShownItsTarget = new bool[2];

    private readonly List<(TeaP1Tornado From, SimTether Tether)> drainageTethers = [];
    private readonly List<SimEnemy> emboli = [];
    private readonly HashSet<SimEnemy> emboliLeaving = [];
    private bool embolusBurst;
    private bool fallen;
    private float timelineEndsAt;

    public TeaP1LivingLiquid(SimWorld world, TeaP1Presses presses)
    {
        this.world = world;
        this.presses = presses;
        presses.Provoked += Hear;
        Tornadoes = new TeaP1Tornadoes(world);
        Dolls = new TeaP1JagdDolls(world, presses, this);
        throttle = new ThrottleCarriers(world, presses);
        waveFromTheLiquid = Actions.ProteanWaveFromTheLiquid(world, presses);
        waveAtAPlayer = Actions.ProteanWaveAtAPlayer(world, presses);
        tornadoWave = Actions.TornadoWave(world, presses);
        helpers = new SpawnedHelperPool(world, new EnemySpawnConfig(
            BNpcBaseId: P1BNpcBaseId.Helper,
            NameId: P1BNpcNameId.LivingLiquid,
            Level: Level,
            Targetable: false,
            EnemyList: EnemyListMode.Never,
            Visibility: SpawnVisibility.InvisibleHelper,
            Placement: new Placement(Vector3.Zero, 0f)), P1Rules.LivingLiquidHelpers);
    }

    public SimEnemy? Liquid => bosses[(int)TeaP1Boss.LivingLiquid];
    public SimEnemy? Hand => bosses[(int)TeaP1Boss.LiquidHand];

    public TeaP1Tornadoes Tornadoes { get; }

    public TeaP1JagdDolls Dolls { get; }

    // P1Rules.EmbolusSpeed; headless runs also play faster ones.
    internal float EmbolusSpeed { get; set; } = P1Rules.EmbolusSpeed;

    // The most helpers this run has kept busy at once; never more than P1Rules.LivingLiquidHelpers.
    public int BusyHelperPeak => helpers.PeakBusy;

    internal static float Apart(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    // Where `boss` stands; once it has left, where it last stood.
    public Vector3 Position(TeaP1Boss boss)
    {
        if (bosses[(int)boss] is { IsActive: true } enemy) lastSeenAt[(int)boss] = enemy.Position;
        return lastSeenAt[(int)boss];
    }

    // Called as the run starts, never from an event: a strat's first move may read where a boss stands, and a cast on
    // an actor spawned in its own frame plays no animation.
    public void Spawn(Placement liquid, Placement hand)
    {
        Spawn(TeaP1Boss.LivingLiquid, liquid);
        Spawn(TeaP1Boss.LiquidHand, hand);
        helpers.Spawn();
    }

    // The pull's start, with the Living Liquid alone: the Liquid Hand splits off it with Cascade.
    public void SpawnTheLiquid(Placement liquid)
    {
        Spawn(TeaP1Boss.LivingLiquid, liquid);
        helpers.Spawn();
    }

    private void Spawn(TeaP1Boss boss, Placement placement)
    {
        var (bnpcBaseId, nameId) = boss == TeaP1Boss.LivingLiquid
            ? (P1BNpcBaseId.LivingLiquid, P1BNpcNameId.LivingLiquid)
            : (P1BNpcBaseId.LiquidHand, P1BNpcNameId.LiquidHand);
        bosses[(int)boss] = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: bnpcBaseId,
            NameId: nameId,
            Level: Level,
            Targetable: true,
            EnemyList: EnemyListMode.Always,
            Visibility: SpawnVisibility.Visible,
            Placement: placement));
        lastSeenAt[(int)boss] = placement.Position;
    }

    // The split, from an event; the Hand's first cast comes long after. UNVERIFIED: that it starts on the off tank's
    // seat; the fight's goes to whoever holds its enmity.
    private void TheHandAppears()
    {
        Spawn(TeaP1Boss.LiquidHand, new Placement(P1Geometry.HandAppearsOn, 0f));
        TurnTo(TeaP1Boss.LiquidHand);
    }

    public void CastFromHelper(EnemyAction action, CastTarget target = default, Placement? at = null, byte animationVariation = 0)
        => helpers.Cast(action, target, at ?? Liquid?.Placement() ?? default, animationVariation);

    // -- Targets --
    // No enmity is played: each boss has the one target it was last given and faces it. It stands still while that
    // target is in reach of its auto-attack, walks after it once it is not until it is well inside that reach again
    // (P1Rules.BossSetsOffPastTheHitboxes, BossStopsPastTheHitboxes), and holds still while it casts.
    public SimCharacter? Target(TeaP1Boss boss) => targets[(int)boss];

    public void SetTarget(TeaP1Boss boss, SimCharacter target)
    {
        targets[(int)boss] = target;
        if (boss == TeaP1Boss.LivingLiquid && FacingLocked) return;
        TurnTo(boss);
    }

    // How far from its target's centre `boss` comes to rest after a walk, and how far it lets it stand before one.
    public float StopsFromItsTarget(TeaP1Boss boss) => Reach(boss) + P1Rules.BossStopsPastTheHitboxes;

    public float SetsOffBeyond(TeaP1Boss boss) => Reach(boss) + P1Rules.BossSetsOffPastTheHitboxes;

    private float Reach(TeaP1Boss boss) => (bosses[(int)boss]?.HitboxRadius ?? 0f) + (targets[(int)boss]?.HitboxRadius ?? 0f);


    // The Living Liquid on the main tank's seat and the Liquid Hand on the off tank's.
    public void TakeTargets()
    {
        if (world.Party.Get(PartyRole.MainTank) is { } mainTank) SetTarget(TeaP1Boss.LivingLiquid, mainTank);
        if (world.Party.Get(PartyRole.OffTank) is { } offTank) SetTarget(TeaP1Boss.LiquidHand, offTank);
    }

    // The client has a spawned actor only some frames after its spawn and drops what is written to it before: a boss
    // turned to its target that early is shown it again once it is there.
    private void TurnTo(TeaP1Boss boss)
    {
        if (bosses[(int)boss] is not { } enemy || !enemy.IsAlive() || targets[(int)boss] is not { } target || !target.IsAlive()) return;
        hasYetToBeShownItsTarget[(int)boss] = enemy.PacketSpawnPending;
        enemy.SetTarget(target, follow: false);
        enemy.KeepFacing(target);
    }

    private void ShowEachBossItsTargetOnceItIsThere()
    {
        foreach (var boss in Enum.GetValues<TeaP1Boss>())
            if (hasYetToBeShownItsTarget[(int)boss] && bosses[(int)boss] is { PacketSpawnPending: false })
                TurnTo(boss);
    }

    // A Provoke on either boss gives it to whoever pressed it, P1Duration.ProvokeTakesHold after the press.
    private void Hear(TeaP1Provoke provoke)
    {
        if (fallen) return;
        foreach (var boss in Enum.GetValues<TeaP1Boss>())
        {
            if (bosses[(int)boss] is not { } enemy || !enemy.IsAlive() || (ulong)enemy.GameObjectId != provoke.TargetId) continue;
            world.Events.Add(MathF.Max(0f, provoke.At + P1Duration.ProvokeTakesHold - world.Events.Elapsed), () =>
            {
                if (!fallen && provoke.By.IsAlive()) SetTarget(boss, provoke.By);
            });
        }
    }

    // A boss the client does not have yet reads no hitbox, so its reach is not known.
    private void EachBossWalksAfterATargetOutOfReach()
    {
        foreach (var boss in Enum.GetValues<TeaP1Boss>())
        {
            var index = (int)boss;
            if (bosses[index] is not { } enemy || !enemy.IsAlive() || enemy.PacketSpawnPending) continue;
            var held = boss == TeaP1Boss.LivingLiquid && FacingLocked;
            if (held || targets[index] is not { } target || !target.IsAlive())
            {
                if (walksAfterItsTarget[index]) enemy.StopMoving();
                walksAfterItsTarget[index] = false;
                continue;
            }
            var apart = Apart(enemy.Position, target.Position);
            if (!walksAfterItsTarget[index] && apart <= SetsOffBeyond(boss)) continue;
            var stop = StopsFromItsTarget(boss);
            if (apart <= stop + StopsWithin)
            {
                if (walksAfterItsTarget[index]) enemy.StopMoving();
                walksAfterItsTarget[index] = false;
                continue;
            }
            walksAfterItsTarget[index] = true;
            var from = Vector3.Normalize(new Vector3(enemy.Position.X - target.Position.X, 0f, enemy.Position.Z - target.Position.Z));
            enemy.MoveTo(target.Position + from * stop, P1Rules.BossWalkSpeed);
        }
    }


    // -- The Living Liquid's facing through a Protean sequence --
    // From its telegraphed cast's start until P1Duration.LiquidHoldsItsFacingAfterTheLastWave after its second
    // untelegraphed set it keeps the facing and the spot it had, whatever its target does.
    public bool FacingLocked { get; private set; }

    public float? LockedFacing => FacingLocked ? Liquid?.Rotation : null;

    private void LockTheFacing()
    {
        Liquid?.KeepFacing(null);
        Liquid?.StopMoving();
        walksAfterItsTarget[(int)TeaP1Boss.LivingLiquid] = false;
        FacingLocked = true;
    }

    private void ReleaseTheFacing()
    {
        FacingLocked = false;
        TurnTo(TeaP1Boss.LivingLiquid);
    }


    // -- Protean Wave --
    // The telegraphed cast: as many cones as players, each aimed as the cast starts. The Living Liquid's own goes
    // straight ahead of it, where its target is taken to stand, and one goes at every other player.
    public void ProteanWave(float castAt, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => At(castAt, () =>
        {
            if (Liquid is not { } liquid || !liquid.IsAlive()) return;
            LockTheFacing();
            liquid.Cast(waveFromTheLiquid);
            var itsOwn = Target(TeaP1Boss.LivingLiquid);
            foreach (var member in world.Party.ActiveMembers().Where(member => !ReferenceEquals(member, itsOwn)).ToList())
                CastFromHelper(waveAtAPlayer, at: liquid.Placement().Face(member.Position));
        }, file, line);


    // An untelegraphed set: one cone straight ahead, which kills whoever it hits, and one at each of the four players
    // closest to the Living Liquid. With `sluice`, a Sluice also starts under each of the four farthest from it.

    public void UntelegraphedWaves(float at, bool sluice, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => At(at, () =>
        {
            FireAnUntelegraphedSet();
            if (sluice) LaySluices();
        }, file, line);

    // The sequence's last set; the Living Liquid turns back to its target P1Duration.LiquidHoldsItsFacingAfterTheLastWave
    // later.
    public void LastUntelegraphedWaves(float at, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        At(at, FireAnUntelegraphedSet, file, line);
        At(at + P1Duration.LiquidHoldsItsFacingAfterTheLastWave, ReleaseTheFacing, file, line);
    }

    private void FireAnUntelegraphedSet()
    {
        if (Liquid is not { } liquid || !liquid.IsAlive()) return;
        liquid.Cast(Actions.ProteanWaveStraightAhead);
        foreach (var member in world.Party.Find.ClosestN(liquid.Position, WavesPerSet))
            CastFromHelper(Actions.ProteanWaveAtTheClosest, at: liquid.Placement().Face(member.Position));
    }

    private void LaySluices()
    {
        if (Liquid is not { } liquid) return;
        foreach (var spot in world.Party.Find.FarestN(liquid.Position, Sluices).Select(member => member.Position).ToList())
            CastFromHelper(Actions.Sluice, spot, new Placement(spot, 0f));
    }

    // One telegraphed cone from each tornado at the player closest to it as the cast starts.
    public void TornadoWaves(float castAt, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => At(castAt, () => FireFromEachTornado(tornadoWave), file, line);

    // One unseen cone from each tornado at the player closest to it then.
    public void UntelegraphedTornadoWaves(float at, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => At(at, () => FireFromEachTornado(Actions.TornadoWaveUnseen), file, line);

    private void FireFromEachTornado(EnemyAction wave)
    {
        foreach (var which in Enum.GetValues<TeaP1Tornado>())
            if (Tornadoes.Centre(which) is { } centre && world.Party.Find.Closest(centre) is { } closest)
                CastFromHelper(wave, at: new Placement(centre, 0f).Face(closest.Position));
    }

    // -- The raidwides --
    // Cascade; `newTornadoes` appear P1Duration.TornadoesAppearAfterCascade after it lands, and with them, at the
    // pull's, the Liquid Hand splits off the Living Liquid.
    public void Cascade(float castAt, TeaP1TornadoLayout? newTornadoes, bool theLiquidSplits = false, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        At(castAt, () => Liquid?.Cast(Actions.Cascade), file, line);
        if (newTornadoes is null && !theLiquidSplits) return;
        At(castAt + Actions.Cascade.ReleasesAfter() + P1Duration.TornadoesAppearAfterCascade, () =>
        {
            if (theLiquidSplits) TheHandAppears();
            if (newTornadoes is not null) Tornadoes.Appear(newTornadoes);
        }, file, line);
    }


    public void Splashes(float firstAt, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        for (var splash = 0; splash < SplashCount; splash++)
            At(firstAt + splash * SplashSpacing, () => Liquid?.Cast(Actions.Splash), file, line);
    }

    public void HandOfPain(float castAt, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => At(castAt, () => Hand?.Cast(Actions.HandOfPain), file, line);

    // -- The cleaves --
    public void FluidSwing(float at, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => At(at, () => Cleave(TeaP1Boss.LivingLiquid, Actions.FluidSwing), file, line);

    public void FluidStrike(float at, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => At(at, () => Cleave(TeaP1Boss.LiquidHand, Actions.FluidStrike), file, line);

    private void Cleave(TeaP1Boss boss, EnemyAction cleave)
    {
        if (bosses[(int)boss] is { } enemy && enemy.IsAlive() && targets[(int)boss] is { } target && target.IsAlive())
            enemy.Cast(cleave, target);
    }

    // -- Drainage --
    // The tethers out now, each with the tornado it comes from.
    public IReadOnlyList<(TeaP1Tornado From, SimTether Tether)> DrainageTethersOut => drainageTethers;

    // The fight draws the channel from the tornado to the player. Here a tether is declared from its holder to its
    // tornado, so the channel shows the other way round: only a tether's source carries it, and a pass skips whoever
    // carries one already, so nobody can hold both. UNVERIFIED: that the fight refuses a holder the second tether.
    public SimCharacter? Holder(SimTether tether) => tether.A;

    // Each tether starts on its roll's seat; a seat that is dead by then, or already holds the other, gives way to a
    // random living one that no roll names.
    public void DrainageTethers(float at, IReadOnlyList<TeaP1DrainageRoll> rolls, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => At(at, () =>
        {
            var holders = new List<SimCharacter>();
            var rolled = rolls.Select(roll => world.Party.Get(roll.FirstTarget)).ToList();
            foreach (var roll in rolls)
            {
                if (Tornadoes[roll.From] is not { } tornado) continue;
                var first = world.Party.Get(roll.FirstTarget);
                if (!first.IsAlive() || holders.Contains(first!))
                {
                    var free = world.Party.ActiveMembers().Where(member => !holders.Contains(member)).ToArray();
                    var unnamed = free.Where(member => !rolled.Contains(member)).ToArray();
                    var others = unnamed.Length > 0 ? unnamed : free;
                    first = others.Length == 0 ? null : world.Rng.NextObj(others);
                }
                if (first is null) continue;
                holders.Add(first);
                drainageTethers.Add((roll.From, world.Tether(End.Passable(first), tornado, P1TetherId.Drainage)));
            }
        }, file, line);

    // Drainage on each tether's holder, 6 y round it, and the tornadoes leave with their puddles. A non-tank seat
    // that holds a tether dies; so does anyone but the holder in a holder's circle, and a tank seat in two, short of
    // a tank's invulnerability. A human tank on a tank's job that takes its own needs a cooldown
    // (P1Rules.DrainageMitigation). UNVERIFIED: that a tank lives through one unmitigated in the fight, and that one
    // circle kills a tank seat that holds no tether.

    public void Drainage(float at, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => At(at, () =>
        {
            var holders = drainageTethers
                .Where(tether => Holder(tether.Tether).IsAlive())
                .Select(tether => (tether.From, Holder: Holder(tether.Tether)!))
                .ToList();
            var verdicts = new Verdicts();
            foreach (var member in world.Party.ActiveMembers().ToList())
            {
                var circles = holders.Count(holder => Apart(holder.Holder.Position, member.Position) <= P1Geometry.DrainageRadius);
                var holds = holders.Any(holder => ReferenceEquals(holder.Holder, member));
                var why = OnATankSeat(member)
                    ? circles >= 2 ? TwoDrainages
                    : circles == 1 && !holds ? InSomeoneElsesDrainage
                    : circles == 1 && ShortOfMitigation(member) ? DrainageWithoutMitigation
                    : null
                    : holds ? NonTankHoldsATether
                    : circles >= 1 ? InSomeoneElsesDrainage
                    : null;
                if (why is not null && !Actions.Invulnerable(member)) verdicts.Kill(member, P1ActionId.Drainage, why);
            }
            foreach (var (from, holder) in holders)
                Tornadoes[from]?.Cast(Actions.Drainage, holder);
            verdicts.Deal();
            foreach (var (_, tether) in drainageTethers) tether.Despawn();
            drainageTethers.Clear();
            Tornadoes.Leave();
        }, file, line);

    private static bool OnATankSeat(SimCharacter member) => member is ISimPartyMember { Role: var role } && role.IsTank();

    // As an EnemyAction's MinMit checks it: the human's alone, only while Mitigation.Required, and the shields it
    // counts are spent. A tank seat's human on another job has none of a tank's cooldowns and passes, as a bot does.
    private bool ShortOfMitigation(SimCharacter tank)
        => Actions.OnATankJob(tank) && DamageCheck.LethalCause(tank, VulnSpec.None, Severity.TankBuster.MinMit(P1Rules.DrainageMitigation),
            DamageKinds.FromAttackType(Natives.Data.Action(P1ActionId.Drainage)?.AttackType ?? 0), world.Party) is not null;

    // -- Throttle --
    // Liquid Gaol: Throttle lands on the `throttled` seats P1Duration.ThrottleAfterLiquidGaol later, but for one under
    // the Warden's Paean's barrier, and kills whoever still carries it as it runs out; a cleanse on a carrier takes it
    // off. All three tornadoes cast it.

    public void LiquidGaol(float at, IReadOnlyList<PartyRole> throttled, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var landsAt = at + P1Duration.ThrottleAfterLiquidGaol;
        At(at, () =>
        {
            foreach (var which in Enum.GetValues<TeaP1Tornado>())
                Tornadoes[which]?.Cast(P1ActionId.LiquidGaol);
        }, file, line);
        At(landsAt, () => throttle.Land(throttled), file, line);
        At(landsAt + P1Duration.Throttle, throttle.RunOut, file, line);
    }

    // Who carries Throttle now, by the run's own count.
    public IReadOnlyCollection<SimCharacter> Throttled => throttle.Carriers;

    public bool IsThrottled(SimCharacter member) => throttle.Carriers.Contains(member);

    // -- The Liquid Hand's slam --
    // The form it shows from its glow to its slam: a palm when the bosses stand far apart as it glows, else a fist.
    public bool? HandShowsAPalm { get; private set; }

    public void TheHandGlows(float at, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => At(at, () =>
        {
            if (Hand is not { } hand || !hand.IsAlive()) return;
            var palm = BossesApart() > P1Rules.HandShowsAPalmBeyond;
            HandShowsAPalm = palm;
            hand.SetModelState(palm ? P1ModelState.HandPalm : P1ModelState.HandFist);
        }, file, line);

    // The slam has no cast bar: it goes out and is judged at once. A palm, which slams with Hand of Parting, kills
    // everyone when it finds the bosses apart, and a fist, with Hand of Prayer, when it finds them together. The Hand
    // drops its form as it lands.
    public void TheHandSlams(float at, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => At(at, () =>
        {
            if (Hand is not { } hand || !hand.IsAlive() || HandShowsAPalm is not { } palm) return;
            hand.Cast(palm ? Actions.HandOfParting : Actions.HandOfPrayer);
            var apart = BossesApart();
            if (palm && apart > P1Rules.PalmNeedsThemWithin)
                world.Party.WipeAllPlayers(P1ActionId.HandOfParting, PalmFoundThemApart);
            else if (!palm && apart <= P1Rules.FistNeedsThemBeyond)
                world.Party.WipeAllPlayers(P1ActionId.HandOfPrayer, FistFoundThemTogether);

            hand.SetModelState(0);
            HandShowsAPalm = null;
        }, file, line);

    private float BossesApart() => Apart(Position(TeaP1Boss.LivingLiquid), Position(TeaP1Boss.LiquidHand));

    // -- Emboli --
    // Pressurize from each roll's tornado. An Embolus shows on it P1Duration.EmbolusAfterPressurize later, waits there
    // and, P1Duration.EmbolusSetsOffAfterPressurize after the cast, sets off through where its boss stood at the cast
    // and on out of the arena. A player or a boss whose hitbox comes within P1Geometry.EmbolusRadius of one bursts
    // it, and everyone dies.
    public void Pressurize(float at, IReadOnlyList<TeaP1EmbolusRoll> rolls, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var sent = new List<(Vector3 From, Vector3 To)>();
        At(at, () =>
        {
            foreach (var roll in rolls)
            {
                if (Tornadoes[roll.From] is not { } tornado || Tornadoes.Centre(roll.From) is not { } centre) continue;
                tornado.Cast(P1ActionId.Pressurize);
                sent.Add((centre, Position(roll.AimsAt)));
            }
        }, file, line);
        At(at + P1Duration.EmbolusAfterPressurize, () =>
        {
            foreach (var (from, to) in sent)
            {
                if (world.SpawnEnemy(new EnemySpawnConfig(
                        BNpcBaseId: P1BNpcBaseId.Embolus,
                        NameId: P1BNpcNameId.Embolus,
                        Level: Level,
                        Targetable: false,
                        EnemyList: EnemyListMode.Never,
                        Visibility: SpawnVisibility.Visible,
                        Placement: new Placement(from, 0f).Face(to))) is not { } embolus) continue;
                emboli.Add(embolus);
                var leavesAt = WhereItLeavesTheArena(from, to);
                At(at + P1Duration.EmbolusSetsOffAfterPressurize, () =>
                {
                    if (embolus.IsActive && !fallen) embolus.MoveTo(leavesAt, EmbolusSpeed);
                }, file, line);
            }
        }, file, line);
    }

    // Straight on from `from` through `through` to P1Geometry.EmbolusLeavesBeyond from the centre.
    private static Vector3 WhereItLeavesTheArena(Vector3 from, Vector3 through)
    {
        var (start, along) = (new Vector2(from.X, from.Z), new Vector2(through.X - from.X, through.Z - from.Z));
        if (along.LengthSquared() < 1e-6f) along = -start;
        along = Vector2.Normalize(along);
        var toward = Vector2.Dot(start, along);
        var reach = -toward + MathF.Sqrt(MathF.Max(0f, toward * toward - start.LengthSquared() + P1Geometry.EmbolusLeavesBeyond * P1Geometry.EmbolusLeavesBeyond));
        var leaves = start + along * reach;
        return new Vector3(leaves.X, from.Y, leaves.Y);
    }

    public IReadOnlyList<SimEnemy> Emboli => emboli;

    private void BurstATouchedEmbolus()
    {
        if (embolusBurst) return;
        foreach (var embolus in emboli.Where(embolus => embolus.IsActive && !emboliLeaving.Contains(embolus)))
        {
            var at = embolus.Position;
            if (Apart(at, Vector3.Zero) >= P1Geometry.EmbolusLeavesBeyond - StopsWithin)
            {
                FadeOut(embolus);
                continue;
            }
            var touched = world.Party.ActiveMembers().Any(member => Touches(member, at))
                          || bosses.Any(boss => boss.IsAlive() && Touches(boss!, at));
            if (!touched) continue;
            embolusBurst = true;
            embolus.Cast(Actions.Outburst);
            world.Party.WipeAllPlayers(P1ActionId.Outburst, EmbolusTouched);
            return;
        }
    }

    private static bool Touches(SimCharacter who, Vector3 embolus) => Apart(who.Position, embolus) <= who.HitboxRadius + P1Geometry.EmbolusRadius;

    // Once: a fading Embolus is still active until its fade has played.
    private void FadeOut(SimEnemy embolus)
    {
        if (embolus.IsActive && emboliLeaving.Add(embolus)) embolus.FadeOut();
    }

    // -- The Jagd Dolls --
    public void JagdDollsAppear(float at, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => At(at, Dolls.Appear, file, line);

    public void JagdDollsTurnTargetable(float at, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => At(at, Dolls.TurnTargetable, file, line);

    // Every doll that is up Exhausts at `firstAt` and every P1Duration.ExhaustEvery after it, while one is left and
    // the timeline lasts. Each Exhaust schedules the next, on the run's clock so the chain slips no frame, and none
    // past the last time the scenario has asked for: a doll nobody feeds must not keep a finished run going.
    public void Exhausts(float firstAt, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        timelineEndsAt = MathF.Max(timelineEndsAt, firstAt);
        Exhaust(firstAt, 0, file, line);
    }

    private void Exhaust(float firstAt, int nth, string file, int line)
        => OnTheRunsClock(firstAt + nth * P1Duration.ExhaustEvery, () =>
        {
            if (fallen) return;
            Dolls.Exhaust();
            if (Dolls.AnyLeft && firstAt + (nth + 1) * P1Duration.ExhaustEvery <= timelineEndsAt) Exhaust(firstAt, nth + 1, file, line);
        }, file, line);

    // -- The end --

    // The Living Liquid falls on the timeline, which ends the run; the Liquid Hand leaves
    // P1Duration.HandLeavesAfterTheLiquid later. Nothing kills from here on, so the tornadoes and the Emboli go with
    // the Liquid (UNVERIFIED when the fight's leave).
    public void TheLiquidFalls(float at, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        At(at, () =>
        {
            fallen = true;
            Position(TeaP1Boss.LivingLiquid);
            Liquid?.Defeat(P1Duration.LiquidCorpseFade);
            Tornadoes.Leave();
            Dolls.Leave();
            foreach (var embolus in emboli) FadeOut(embolus);
        }, file, line);
        At(at + P1Duration.HandLeavesAfterTheLiquid, () =>
        {
            Position(TeaP1Boss.LiquidHand);
            Hand?.FadeOut();
        }, file, line);
    }

    // From the scenario's Tick.
    public void Tick()
    {
        presses.Tick();
        Position(TeaP1Boss.LivingLiquid);
        Position(TeaP1Boss.LiquidHand);
        if (fallen) return;
        ShowEachBossItsTargetOnceItIsThere();
        EachBossWalksAfterATargetOutOfReach();
        Tornadoes.KillWhoeverEntersAPuddle();
        Dolls.Tick();
        BurstATouchedEmbolus();
    }

    // Who carries Throttle: kept here, since a status that runs out leaves before anything can see it go, a frame
    // early on a long frame.
    private sealed class ThrottleCarriers
    {
        private readonly SimWorld world;
        private readonly TeaP1Presses presses;
        private readonly List<SimCharacter> carriers = [];
        private readonly Dictionary<SimCharacter, float> barrierUntil = [];

        public ThrottleCarriers(SimWorld world, TeaP1Presses presses)
        {
            this.world = world;
            this.presses = presses;
            presses.Cleansed += Hear;
        }

        public IReadOnlyCollection<SimCharacter> Carriers => carriers;

        // A seat under the Warden's Paean's barrier gets none, and the barrier is spent.
        public void Land(IReadOnlyList<PartyRole> seats)
        {
            presses.CatchUp();
            foreach (var seat in seats)
            {
                if (world.Party.Get(seat) is not { } member || !member.IsAlive()) continue;
                if (barrierUntil.Remove(member, out var until) && world.Events.Elapsed < until) continue;
                member.AddStatus(P1StatusId.Throttle, P1Duration.Throttle);
                carriers.Add(member);
            }
        }

        // A cleanse on a seat that carries no Throttle takes nothing off, one pressed before it lands included. The
        // Warden's Paean then leaves its barrier on that seat instead, which stops the next debuff a cleanse could
        // take off. UNVERIFIED: that the barrier stops Throttle.
        private void Hear(TeaP1Cleanse cleanse)
        {
            if (carriers.Remove(cleanse.Target)) cleanse.Target.RemoveStatus(P1StatusId.Throttle);
            else if (cleanse.ActionId == P1PlayerActionId.WardensPaean) barrierUntil[cleanse.Target] = cleanse.At + P1Duration.WardensPaeanBarrier;
        }

        public void RunOut()
        {
            presses.CatchUp();
            presses.Cleansed -= Hear;
            foreach (var member in carriers.ToList()) RanOutOn(member);
        }

        // Whoever is still counted dies, whatever became of the status itself: only a cleanse takes a seat off the
        // count.
        private void RanOutOn(SimCharacter member)
        {
            if (carriers.Remove(member) && member.IsAlive()) member.Die(P1ActionId.LiquidGaol, ThrottleRanOut);
        }
    }

    // On the run's clock, whenever it is called. The latest time asked for is where the scenario's timeline ends.
    internal void At(float time, Action action, string file, int line)
    {
        timelineEndsAt = MathF.Max(timelineEndsAt, time);
        OnTheRunsClock(time, action, file, line);
    }

    private void OnTheRunsClock(float time, Action action, string file, int line)
        => world.Events.FromRunStart(() =>
        {
            world.Events.Add(time, action, file, line);
            return true;
        });
}
