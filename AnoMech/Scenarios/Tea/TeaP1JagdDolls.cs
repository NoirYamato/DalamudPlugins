using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using Actions = AnoMech.Scenarios.Tea.TeaP1Actions;

namespace AnoMech.Scenarios.Tea;

// A quarter of the arena in the frame the tornadoes name: the open quarter is north.
public enum TeaP1Quarter { Northeast, Southeast, Southwest, Northwest }

public sealed class TeaP1JagdDoll
{
    internal TeaP1JagdDoll(SimEnemy enemy, Vector3 spot)
    {
        Enemy = enemy;
        Spot = spot;
    }

    public SimEnemy Enemy { get; }

    // Where it appeared.
    public Vector3 Spot { get; }

    // Whoever hit it first, whom it tethers and walks after.
    public SimCharacter? Holder { get; internal set; }
    public float? HitAt { get; internal set; }
    public float? TetheredAt { get; internal set; }
    public TeaP1Boss? FedTo { get; internal set; }

    internal bool DueToTurnTargetable { get; set; }
    internal bool Targetable { get; set; }
    internal bool WalksAfterItsHolder { get; set; }
    internal int HealthShown { get; set; } = HealthSteps;
    internal bool SaidItIsReady { get; set; }
    internal bool Leaving { get; set; }
    internal SimTether? Tether { get; set; }

    internal const int HealthSteps = 20;

    public bool IsUp => Targetable && FedTo is null && !Leaving && Enemy.IsActive;

    public bool HasBeenHit => HitAt is not null;

    // No damage is played: its health is whole until its tether shows, then falls evenly through a quarter
    // P1Duration.DollUnderAQuarterAfter later to P1Rules.DollHealthFloor, where it stays.
    public float Health(float now)
    {
        if (TetheredAt is not { } tethered) return 1f;
        var falls = (1f - P1Rules.DollIsReadyUnder) / P1Duration.DollUnderAQuarterAfter;
        return MathF.Max(P1Rules.DollHealthFloor, 1f - falls * MathF.Max(0f, now - tethered));
    }

    public TeaP1Quarter QuarterIn(TeaP1TornadoLayout frame)
    {
        var relative = frame.ToRelative(Spot);
        return relative.Y < 0f
            ? relative.X >= 0f ? TeaP1Quarter.Northeast : TeaP1Quarter.Northwest
            : relative.X >= 0f ? TeaP1Quarter.Southeast : TeaP1Quarter.Southwest;
    }
}

// The four Jagd Dolls. Each tethers the first player whose weaponskill or spell on it is heard and walks after that
// player. Each Exhausts on the timeline; two Exhausts on one player at once make that player explode. A doll a player
// has hit is fed to a boss it comes close to: harmlessly once its health is under a quarter, and killing everyone
// before.
//
// No damage is played, so a doll's health is scripted from its tether and no doll dies. UNVERIFIED: that a doll nobody
// has hit is never fed (seen only in a doll's first 0.75 s), that one can be fed from its first hit, before its tether
// shows, that an auto-attack alone tethers none (the run hears no auto-attack), that a doll fed with a quarter of its
// health or more kills everyone, where the fight's hits hard enough to kill most of a party, and that an explosion
// reaches the sheet's 8 y past a player's hitbox.
public sealed class TeaP1JagdDolls
{
    internal const string LuminousAetheroplasmBurst =
        "A player hit by two Jagd Dolls' Exhausts at once explodes and kills everyone within 8.5 y: each Exhaust reaches 8.8 y round its doll";
    internal const string ADollWasFedTooSoon =
        "A Jagd Doll reached a boss with a quarter of its health or more left: one that comes within 4 y of the Living Liquid or the Liquid Hand is fed to it, and above a quarter that kills everyone";

    private const float StopsWithin = 0.05f;

    private readonly SimWorld world;
    private readonly TeaP1Presses presses;
    private readonly TeaP1LivingLiquid fight;
    private readonly List<TeaP1JagdDoll> dolls = [];
    private bool left;

    internal TeaP1JagdDolls(SimWorld world, TeaP1Presses presses, TeaP1LivingLiquid fight)
    {
        this.world = world;
        this.presses = presses;
        this.fight = fight;
        presses.Struck += Hear;
    }

    public IReadOnlyList<TeaP1JagdDoll> All => dolls;

    public TeaP1JagdDoll? In(TeaP1TornadoLayout frame, TeaP1Quarter quarter) => dolls.FirstOrDefault(doll => doll.QuarterIn(frame) == quarter);

    // The first it hit, of those not fed yet.
    public TeaP1JagdDoll? HeldBy(SimCharacter member)
        => dolls.FirstOrDefault(doll => doll.FedTo is null && ReferenceEquals(doll.Holder, member));

    // Whether any is left to Exhaust.
    internal bool AnyLeft => !left && dolls.Any(doll => doll.FedTo is null && !doll.Leaving && doll.Enemy.IsActive);

    internal void Appear()
    {
        if (left) return;
        foreach (var bearing in P1Geometry.JagdDollBearings)
        {
            var radians = bearing * MathF.PI / 180f;
            var spot = new Vector3(MathF.Sin(radians), 0f, -MathF.Cos(radians)) * P1Geometry.JagdDollRing;
            if (world.SpawnEnemy(new EnemySpawnConfig(
                    BNpcBaseId: P1BNpcBaseId.JagdDoll,
                    NameId: P1BNpcNameId.JagdDoll,
                    Level: Level,
                    Targetable: false,
                    EnemyList: EnemyListMode.Always,
                    Visibility: SpawnVisibility.Visible,
                    Placement: new Placement(spot, 0f).Face(Vector3.Zero),
                    Appearance: P1Appearance.JagdDoll)) is { } enemy)
                dolls.Add(new TeaP1JagdDoll(enemy, spot));
        }
    }

    // The client has a spawned actor only some frames after its spawn and drops what is written to it before, so a
    // doll still on its way turns targetable on the first frame it is there.
    internal void TurnTargetable()
    {
        foreach (var doll in dolls)
        {
            doll.DueToTurnTargetable = true;
            TurnTargetableOnceItIsThere(doll);
        }
    }

    private static void TurnTargetableOnceItIsThere(TeaP1JagdDoll doll)
    {
        if (!doll.DueToTurnTargetable || doll.Enemy.PacketSpawnPending) return;
        doll.DueToTurnTargetable = false;
        if (!doll.Enemy.IsActive) return;
        doll.Enemy.SetTargetable(true);
        doll.Targetable = true;
    }

    private void Hear(TeaP1Strike strike)
    {
        if (left || dolls.FirstOrDefault(doll => doll.IsUp && (ulong)doll.Enemy.GameObjectId == strike.TargetId) is not { HasBeenHit: false } doll) return;
        doll.HitAt = strike.At;
        doll.Holder = strike.By;
        world.Events.Add(MathF.Max(0f, strike.At + P1Duration.DollTethersAfterItsFirstHit - world.Events.Elapsed), () => TetherItsHolder(doll));
    }

    private void TetherItsHolder(TeaP1JagdDoll doll)
    {
        if (left || !doll.IsUp || doll.Holder is not { } holder || !holder.IsAlive()) return;
        doll.TetheredAt = world.Events.Elapsed;
        doll.Tether = world.Tether(doll.Enemy, holder, P1TetherId.JagdDoll);
        doll.Enemy.SetTarget(holder, follow: false);
        doll.Enemy.KeepFacing(holder);
    }

    // One Exhaust from every doll that is up. Whoever two or more reach explodes, which kills everyone the explosion
    // reaches, itself included.
    internal void Exhaust()
    {
        if (left) return;
        var up = dolls.Where(doll => doll.IsUp).ToList();
        var reach = Natives.Data.Action(P1ActionId.Exhaust)?.EffectRange ?? 0;
        var exploding = world.Party.ActiveMembers()
            .Where(member => up.Count(doll => TeaP1LivingLiquid.Apart(doll.Enemy.Position, member.Position) <= reach + doll.Enemy.HitboxRadius) >= 2)
            .ToList();
        foreach (var doll in up) doll.Enemy.Cast(Actions.Exhaust);

        var blast = Natives.Data.Action(P1ActionId.ExhaustFromAPlayer)?.EffectRange ?? 0;
        var verdicts = new Verdicts();
        foreach (var member in exploding)
        {
            foreach (var caught in world.Party.ActiveMembers().Where(other => TeaP1LivingLiquid.Apart(other.Position, member.Position) <= blast + member.HitboxRadius))
                verdicts.Kill(caught, P1ActionId.ExhaustFromAPlayer, LuminousAetheroplasmBurst);
            fight.CastFromHelper(Actions.ExhaustFromAPlayer, at: member.Placement());
        }
        verdicts.Deal();
    }

    internal void Leave()
    {
        if (left) return;
        left = true;
        presses.Struck -= Hear;
        foreach (var doll in dolls)
        {
            doll.Tether?.Despawn();
            FadeOut(doll);
        }
    }

    internal void Tick()
    {
        if (left) return;
        foreach (var doll in dolls)
        {
            TurnTargetableOnceItIsThere(doll);
            if (!doll.IsUp) continue;
            WalkAfterItsHolder(doll);
            ShowItsHealth(doll);
            if (doll.HasBeenHit) FeedItToABossItHasReached(doll);
        }
    }

    // As the bosses walk: after a holder more than P1Rules.DollSetsOffPastTheHitboxes past their hitboxes, until it is
    // within P1Rules.DollStopsPastTheHitboxes.
    private static void WalkAfterItsHolder(TeaP1JagdDoll doll)
    {
        if (doll.TetheredAt is null) return;
        var enemy = doll.Enemy;
        if (doll.Holder is not { } holder || !holder.IsAlive())
        {
            if (doll.WalksAfterItsHolder) enemy.StopMoving();
            doll.WalksAfterItsHolder = false;
            doll.Tether?.Despawn();
            doll.Tether = null;
            return;
        }
        var apart = TeaP1LivingLiquid.Apart(enemy.Position, holder.Position);
        var hitboxes = enemy.HitboxRadius + holder.HitboxRadius;
        if (!doll.WalksAfterItsHolder && apart <= hitboxes + P1Rules.DollSetsOffPastTheHitboxes) return;
        var stop = hitboxes + P1Rules.DollStopsPastTheHitboxes;
        if (apart <= stop + StopsWithin)
        {
            if (doll.WalksAfterItsHolder) enemy.StopMoving();
            doll.WalksAfterItsHolder = false;
            return;
        }
        doll.WalksAfterItsHolder = true;
        var from = Vector3.Normalize(new Vector3(enemy.Position.X - holder.Position.X, 0f, enemy.Position.Z - holder.Position.Z));
        enemy.MoveTo(holder.Position + from * stop, P1Rules.DollWalkSpeed);
    }

    // The status list is the one packet that carries an actor's health. Sent with no slots it clears, on the client,
    // whatever the player's own actions put on the doll, so it goes out from an event, ahead of the world's tick in the
    // same frame, which then sends the doll's own list with the health just set. UNVERIFIED that the client's bar
    // follows; the human's own doll also says in chat when it is ready.
    private void ShowItsHealth(TeaP1JagdDoll doll)
    {
        var health = doll.Health(world.Events.Elapsed);
        var step = (int)MathF.Ceiling(health * TeaP1JagdDoll.HealthSteps - 0.001f);
        if (step != doll.HealthShown)
        {
            doll.HealthShown = step;
            world.Events.Add(0f, () =>
            {
                if (doll.Enemy.Proxy is not { Exists: true } proxy) return;
                proxy.ReceiveStatusList([], (uint)(proxy.MaxHealth * (float)step / TeaP1JagdDoll.HealthSteps), proxy.Mana, 0);
                doll.Enemy.StatusListChanged();
            });
        }
        if (doll.SaidItIsReady || health >= P1Rules.DollIsReadyUnder) return;
        doll.SaidItIsReady = true;
        if (doll.Holder is { } holder && ReferenceEquals(holder, world.Party.Player) && !world.Party.IsBotDriven(holder))
            world.Announce("Your Jagd Doll is under a quarter of its health: feed it to a boss");
    }

    private void FeedItToABossItHasReached(TeaP1JagdDoll doll)
    {
        foreach (var boss in Enum.GetValues<TeaP1Boss>())
        {
            var enemy = boss == TeaP1Boss.LivingLiquid ? fight.Liquid : fight.Hand;
            if (enemy is null || !enemy.IsAlive() || enemy.PacketSpawnPending) continue;
            if (TeaP1LivingLiquid.Apart(doll.Enemy.Position, enemy.Position) > P1Geometry.DollIsFedWithin) continue;
            Feed(doll, boss, enemy);
            return;
        }
    }

    private void Feed(TeaP1JagdDoll doll, TeaP1Boss boss, SimEnemy to)
    {
        var tooSoon = doll.Health(world.Events.Elapsed) >= P1Rules.DollIsReadyUnder;
        doll.FedTo = boss;
        doll.Tether?.Despawn();
        doll.Tether = world.Tether(doll.Enemy, to, P1TetherId.Fed);
        doll.Enemy.SetTargetable(false);
        doll.Enemy.KeepFacing(null);
        doll.Enemy.StopMoving();
        world.Events.Add(P1Duration.ReducibleComplexityAfterTheFeed, () =>
        {
            doll.Tether?.Despawn();
            doll.Tether = null;
            if (left || doll.Leaving || !doll.Enemy.IsActive) return;
            doll.Enemy.Cast(Actions.ReducibleComplexity);
            if (tooSoon) world.Party.WipeAllPlayers(P1ActionId.ReducibleComplexity, ADollWasFedTooSoon);
            FadeOut(doll);
        });
    }

    private static void FadeOut(TeaP1JagdDoll doll)
    {
        if (doll.Leaving || !doll.Enemy.IsActive) return;
        doll.Leaving = true;
        doll.Enemy.FadeOut();
    }
}
