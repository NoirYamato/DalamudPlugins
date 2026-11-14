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
// No health is played: the raidwides are harmless and the Liquid falls on the timeline. Each boss follows and faces
// the one target the scenario gives it. A mistake kills by the fight's own rule, with the reason. Every helper cast
// goes through CastFromHelper.
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
    private const float SplashSpacing = 1.1f;

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
    private readonly List<(TeaP1Tornado From, SimTether Tether)> drainageTethers = [];
    private readonly List<SimEnemy> emboli = [];
    private bool embolusBurst;
    private bool fallen;

    public TeaP1LivingLiquid(SimWorld world, TeaP1Presses presses)
    {
        this.world = world;
        this.presses = presses;
        Tornadoes = new TeaP1Tornadoes(world);
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

    // UNVERIFIED (P1Rules.EmbolusSpeed); headless runs also play faster ones.
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
        bosses[(int)TeaP1Boss.LivingLiquid] = SpawnBoss(P1BNpcBaseId.LivingLiquid, P1BNpcNameId.LivingLiquid, liquid);
        bosses[(int)TeaP1Boss.LiquidHand] = SpawnBoss(P1BNpcBaseId.LiquidHand, P1BNpcNameId.LiquidHand, hand);
        lastSeenAt[(int)TeaP1Boss.LivingLiquid] = liquid.Position;
        lastSeenAt[(int)TeaP1Boss.LiquidHand] = hand.Position;
        helpers.Spawn();
    }

    private SimEnemy? SpawnBoss(uint bnpcBaseId, uint nameId, Placement placement) => world.SpawnEnemy(new EnemySpawnConfig(
        BNpcBaseId: bnpcBaseId,
        NameId: nameId,
        Level: Level,
        Targetable: true,
        EnemyList: EnemyListMode.Always,
        Visibility: SpawnVisibility.Visible,
        Placement: placement));

    public void CastFromHelper(EnemyAction action, CastTarget target = default, Placement? at = null, byte animationVariation = 0)
        => helpers.Cast(action, target, at ?? Liquid?.Placement() ?? default, animationVariation);

    // -- Targets --
    // No enmity is played: each boss has the one target it was last given, follows it and faces it, and holds still
    // while it casts.
    public SimCharacter? Target(TeaP1Boss boss) => targets[(int)boss];

    public void SetTarget(TeaP1Boss boss, SimCharacter target)
    {
        targets[(int)boss] = target;
        if (boss == TeaP1Boss.LivingLiquid && FacingLocked) return;
        Chase(boss);
    }

    // The Living Liquid on the main tank's seat and the Liquid Hand on the off tank's.
    public void TakeTargets()
    {
        if (world.Party.Get(PartyRole.MainTank) is { } mainTank) SetTarget(TeaP1Boss.LivingLiquid, mainTank);
        if (world.Party.Get(PartyRole.OffTank) is { } offTank) SetTarget(TeaP1Boss.LiquidHand, offTank);
    }

    private void Chase(TeaP1Boss boss)
    {
        if (bosses[(int)boss] is not { } enemy || !enemy.IsAlive() || targets[(int)boss] is not { } target || !target.IsAlive()) return;
        enemy.SetTarget(target, follow: false);
        enemy.Follow(target, P1Rules.BossWalkSpeed);
    }

    // -- The Living Liquid's facing through a Protean sequence --
    // From its telegraphed cast's start until after its second untelegraphed set it keeps the facing it had, whatever
    // its target does. The engine turns a walking enemy along its path, so it also stands still meanwhile (UNVERIFIED
    // that the fight's does).
    public bool FacingLocked { get; private set; }

    public float? LockedFacing => FacingLocked ? Liquid?.Rotation : null;

    private void LockTheFacing()
    {
        Liquid?.Follow(null);
        FacingLocked = true;
    }

    private void ReleaseTheFacing()
    {
        FacingLocked = false;
        Chase(TeaP1Boss.LivingLiquid);
    }

    // -- Protean Wave --
    // The telegraphed cast: one cone straight ahead of the Living Liquid and one at every player, each aimed as the
    // cast starts.
    public void ProteanWave(float castAt, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => At(castAt, () =>
        {
            if (Liquid is not { } liquid || !liquid.IsAlive()) return;
            LockTheFacing();
            liquid.Cast(waveFromTheLiquid);
            foreach (var member in world.Party.ActiveMembers().ToList())
                CastFromHelper(waveAtAPlayer, at: liquid.Placement().Face(member.Position));
        }, file, line);

    // An untelegraphed set: one cone straight ahead and one at each of the four players closest to the Living Liquid.
    // With `sluice`, a Sluice also starts under each of the four farthest from it.
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
    // Cascade; `newTornadoes` appear as it lands.
    public void Cascade(float castAt, TeaP1TornadoLayout? newTornadoes, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        At(castAt, () => Liquid?.Cast(Actions.Cascade), file, line);
        if (newTornadoes is not null)
            At(castAt + Actions.Cascade.ReleasesAfter(), () => Tornadoes.Appear(newTornadoes), file, line);
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
    // that holds a tether dies; so does anyone but the holder in a holder's circle, and a tank seat in two. A human
    // tank on a tank's job that takes its own needs a cooldown (P1Rules.DrainageMitigation). UNVERIFIED: that a tank
    // lives through one unmitigated in the fight, and that one circle kills a tank seat that holds no tether.
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
                if (why is not null) verdicts.Kill(member, P1ActionId.Drainage, why);
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
    // Liquid Gaol: Throttle lands on the `throttled` seats P1Duration.ThrottleAfterLiquidGaol later and kills whoever
    // still carries it as it runs out; a cleanse on a carrier takes it off. UNVERIFIED which of the tornadoes cast it.
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

    // The slam has no cast bar: it goes out and is judged at once. A palm that finds the bosses apart, or a fist that
    // finds them together, kills everyone. UNVERIFIED: which of the two actions is the palm's, and that the Hand
    // drops its form as it lands.
    public void TheHandSlams(float at, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => At(at, () =>
        {
            if (Hand is not { } hand || !hand.IsAlive() || HandShowsAPalm is not { } palm) return;
            hand.Cast(palm ? Actions.HandOfPrayer : Actions.HandOfParting);
            var apart = BossesApart();
            if (palm && apart > P1Rules.PalmNeedsThemWithin)
                world.Party.WipeAllPlayers(P1ActionId.HandOfPrayer, PalmFoundThemApart);
            else if (!palm && apart <= P1Rules.FistNeedsThemBeyond)
                world.Party.WipeAllPlayers(P1ActionId.HandOfParting, FistFoundThemTogether);
            hand.SetModelState(0);
            HandShowsAPalm = null;
        }, file, line);

    private float BossesApart() => Apart(Position(TeaP1Boss.LivingLiquid), Position(TeaP1Boss.LiquidHand));

    // -- Emboli --
    // Pressurize from each roll's tornado; P1Duration.EmbolusAfterPressurize later an Embolus leaves it for where its
    // boss stood at the cast. A player or a boss within P1Geometry.EmbolusContact of one bursts it, and everyone
    // dies. UNVERIFIED: the speed, that each heads for one boss's spot and stops there, and the contact distance.
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
                embolus.MoveTo(to, EmbolusSpeed);
                emboli.Add(embolus);
            }
        }, file, line);
    }

    public IReadOnlyList<SimEnemy> Emboli => emboli;

    private void BurstATouchedEmbolus()
    {
        if (embolusBurst) return;
        foreach (var embolus in emboli.Where(embolus => embolus.IsActive))
        {
            var at = embolus.Position;
            var touched = world.Party.ActiveMembers().Any(member => Apart(member.Position, at) <= P1Geometry.EmbolusContact)
                          || bosses.Any(boss => boss.IsAlive() && Apart(boss!.Position, at) <= P1Geometry.EmbolusContact);
            if (!touched) continue;
            embolusBurst = true;
            embolus.Cast(Actions.Outburst);
            world.Party.WipeAllPlayers(P1ActionId.Outburst, EmbolusTouched);
            return;
        }
    }

    // -- The end --
    // The Living Liquid falls on the timeline, which ends the run; the Liquid Hand leaves
    // P1Duration.HandLeavesAfterTheLiquid later. Nothing kills from here on.
    public void TheLiquidFalls(float at, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        At(at, () =>
        {
            fallen = true;
            Position(TeaP1Boss.LivingLiquid);
            Liquid?.Defeat(P1Duration.LiquidCorpseFade);
            foreach (var embolus in emboli.Where(embolus => embolus.IsActive)) embolus.FadeOut();
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
        Tornadoes.KillWhoeverEntersAPuddle();
        BurstATouchedEmbolus();
    }

    // Who carries Throttle: kept here, since a status that runs out leaves before anything can see it go, a frame
    // early on a long frame.
    private sealed class ThrottleCarriers(SimWorld world, TeaP1Presses presses)
    {
        private readonly List<SimCharacter> carriers = [];

        public IReadOnlyCollection<SimCharacter> Carriers => carriers;

        // A cleanse pressed before it lands cleanses nothing.
        public void Land(IReadOnlyList<PartyRole> seats)
        {
            presses.CatchUp();
            foreach (var seat in seats)
            {
                if (world.Party.Get(seat) is not { } member || !member.IsAlive()) continue;
                member.AddStatus(P1StatusId.Throttle, P1Duration.Throttle);
                carriers.Add(member);
            }
            presses.Cleansed += Lift;
        }

        private void Lift(TeaP1Cleanse cleanse)
        {
            if (carriers.Remove(cleanse.Target)) cleanse.Target.RemoveStatus(P1StatusId.Throttle);
        }

        public void RunOut()
        {
            presses.CatchUp();
            presses.Cleansed -= Lift;
            foreach (var member in carriers.ToList()) RanOutOn(member);
        }

        // Whoever is still counted dies, whatever became of the status itself: only a cleanse takes a seat off the
        // count.
        private void RanOutOn(SimCharacter member)
        {
            if (carriers.Remove(member) && member.IsAlive()) member.Die(P1ActionId.LiquidGaol, ThrottleRanOut);
        }
    }

    // On the run's clock, whenever it is called.
    internal void At(float time, Action action, string file, int line)
        => world.Events.FromRunStart(() =>
        {
            world.Events.Add(time, action, file, line);
            return true;
        });
}
