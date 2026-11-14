using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.EnemyActions.EnemyActionEffects;
using static AnoMech.Core.EnemyActions.Severity;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Scenarios.Tea;

// Death reasons describe the fight, never a strat's spots. UNVERIFIED: no damage values are known, so a Hawk
// Blaster and a Sluice kill on their own, and Cruise Chaser's hits and the Protean Waves kill only through what they
// leave on whoever they hit.
public static class TeaP1Actions
{
    public const string SecondCruiseChaserHit =
        "A second hit from Cruise Chaser: his cleave and his charge each leave a vulnerability, so each may catch only its own number";

    public const string SecondWave =
        "Hit by a second Protean Wave: every wave leaves Water Resistance Down, and the next one while it lasts kills whoever carries it, short of a tank's invulnerability";

    // Declared ahead of the actions built from it.
    private static readonly VulnSpec WaveOnACarrier = P1StatusId.Invulnerabilities.Aggregate(
        VulnerableTo(P1StatusId.WaterResistanceDown), (spec, invulnerability) => spec.ProtectedBy(invulnerability));

    public static readonly EnemyAction HawkBlaster = new(P1ActionId.HawkBlaster)
    {
        Cast = new() { AnimationLock = P1Duration.HawkBlasterAnimationLock },
        Effects = [Damage(Lethal)],
        Timing = new() { DamageDelay = P1Duration.HawkBlasterDamageDelay },
        DeathExplanation = "Caught in the blast: Hawk Blasters go off unmarked, two opposite each other, each pair 45 degrees clockwise of the last, and one in the middle after every fourth pair",
    };

    public static readonly EnemyAction JKick = new(P1ActionId.JKick)
    {
        Effects = [Damage()],
        Timing = new() { DamageDelay = P1Duration.JKickDamageDelay },
    };

    public static EnemyAction AlphaSword(TeaP1Presses presses) => new(P1ActionId.AlphaSword)
    {
        Area = new() { Size = P1Geometry.AlphaSwordHalfAngle },
        Effects = CruiseChaserHit(presses, P1Rules.AlphaSwordKnockback, P1Rules.AlphaSwordKnockbackSpeed),
        Timing = new() { DamageDelay = P1Duration.CruiseChaserDamageDelay },
        DeathExplanation = SecondCruiseChaserHit,
    };

    public static EnemyAction SuperBlasstyCharge(TeaP1Presses presses) => new(P1ActionId.SuperBlasstyCharge)
    {
        Effects = CruiseChaserHit(presses, P1Rules.SuperBlasstyChargeKnockback, P1Rules.SuperBlasstyChargeKnockbackSpeed),
        Timing = new() { DamageDelay = P1Duration.CruiseChaserDamageDelay },
        DeathExplanation = SecondCruiseChaserHit,
    };

    // -- Protean Wave --
    // Each is cast along its caster's facing, so its aim is whatever its caster was turned to as the cast started. A
    // wave on someone who carries the last one's Water Resistance Down kills, bots included; a first one never does.
    // UNVERIFIED: that the Living Liquid's own telegraphed wave is a cone straight ahead and not one more aimed at a
    // player.
    public static EnemyAction ProteanWaveFromTheLiquid(TeaP1Presses presses) => KnockingWave(P1ActionId.ProteanWaveFromTheLiquid, presses);

    public static EnemyAction ProteanWaveAtAPlayer(TeaP1Presses presses) => KnockingWave(P1ActionId.ProteanWaveAtAPlayer, presses);

    public static readonly EnemyAction ProteanWaveStraightAhead = Wave(P1ActionId.ProteanWaveStraightAhead);

    public static readonly EnemyAction ProteanWaveAtTheClosest = Wave(P1ActionId.ProteanWaveAtTheClosest) with { Area = UnseenWaveCone };

    public static EnemyAction TornadoWave(TeaP1Presses presses) => KnockingWave(P1ActionId.TornadoWave, presses);

    public static readonly EnemyAction TornadoWaveUnseen = Wave(P1ActionId.TornadoWaveUnseen) with { Area = UnseenWaveCone };

    private static AreaSpec UnseenWaveCone => new() { Size = P1Geometry.ProteanWaveHalfAngle, CastTypeOverride = CastType.Cone };

    private static EnemyAction Wave(uint actionId, params IEnemyActionEffect[] more) => new(actionId)
    {
        Area = new() { Size = P1Geometry.ProteanWaveHalfAngle },
        Effects = [Damage(WaveOnACarrier), ApplyStatus(P1StatusId.WaterResistanceDown, P1Duration.WaterResistanceDown), .. more],
        Timing = new() { DamageDelay = P1Duration.LivingLiquidDamageDelay },
        DeathExplanation = SecondWave,
    };

    // The telegraphed rows throw whoever they hit; the push starts with the hit, as Cruise Chaser's does.
    private static EnemyAction KnockingWave(uint actionId, TeaP1Presses presses)
        => Wave(actionId, Push(Thrown(presses, P1Rules.ProteanWaveKnockback), P1Rules.ProteanWaveKnockbackSpeed, pushDelay: 0f));

    // -- Sluice --
    public static readonly EnemyAction Sluice = new(P1ActionId.Sluice)
    {
        Effects = [Damage(Lethal)],
        Timing = new() { DamageDelay = P1Duration.LivingLiquidDamageDelay },
        DeathExplanation = "Stood in a Sluice as it went off: one drops under each of the four players farthest from the Living Liquid and goes off 3 s later",
    };

    // -- The raidwides, harmless here --
    public static readonly EnemyAction Cascade = new(P1ActionId.Cascade) { Effects = [Damage()] };

    public static readonly EnemyAction Splash = new(P1ActionId.Splash) { Effects = [Damage()] };

    // In the fight it wipes when the two bosses' health bars differ; no health is played, so it never fails.
    public static readonly EnemyAction HandOfPain = new(P1ActionId.HandOfPain) { Effects = [Damage()] };

    // TeaP1LivingLiquid decides whom these kill; the actions only show the hits.
    public static readonly EnemyAction HandOfPrayer = new(P1ActionId.HandOfPrayer) { Effects = [Damage()] };

    public static readonly EnemyAction HandOfParting = new(P1ActionId.HandOfParting) { Effects = [Damage()] };

    public static readonly EnemyAction Drainage = new(P1ActionId.Drainage)
    {
        Effects = [Damage()],
        Timing = new() { DamageDelay = P1Duration.LivingLiquidDamageDelay },
    };

    public static readonly EnemyAction Outburst = new(P1ActionId.Outburst) { Effects = [Damage()] };

    // -- The cleaves --
    // Each boss's cleave at its target: a non-tank seat in it dies, and a human tank on a tank's job needs a cooldown
    // (P1Rules.FluidSwingMitigation, FluidStrikeMitigation).
    public static readonly EnemyAction FluidSwing = new(P1ActionId.FluidSwing)
    {
        Area = new() { Size = P1Geometry.CleaveHalfAngle },
        Effects = [Damage(TankBuster.MinMit(P1Rules.FluidSwingMitigation), split: new TankJobsOnly())],
        Timing = new() { DamageDelay = P1Duration.LivingLiquidDamageDelay },
        DeathExplanation = "Caught in Fluid Swing: the Living Liquid cleaves a quarter circle at its target, 11.5 y deep",
    };

    public static readonly EnemyAction FluidStrike = new(P1ActionId.FluidStrike)
    {
        Area = new() { Size = P1Geometry.CleaveHalfAngle },
        Effects = [Damage(TankBuster.MinMit(P1Rules.FluidStrikeMitigation), split: new TankJobsOnly())],
        Timing = new() { DamageDelay = P1Duration.LivingLiquidDamageDelay },
        DeathExplanation = "Caught in Fluid Strike: the Liquid Hand cleaves a quarter circle at its target, 11.6 y deep",
    };

    // Only a tank's job has a tank's cooldowns, and the Role setting can seat a human on any job in a tank seat. The
    // human's job is its client's.
    internal static bool OnATankJob(SimCharacter member) => PartyPresets.SkipRoleForJob(member switch
    {
        SimPlayer => Natives.BattleCharas.LocalPlayer.ClassJob,
        SimPartyNpc bot => bot.ClassJob,
        _ => 0,
    }).IsTank();

    // The cast's MinMit, asked only of a player on a tank's job: anyone else takes the plain tank buster. The engine
    // asks it of the human whatever its job.
    private sealed record TankJobsOnly : Distribution
    {
        internal override (Hit? Hit, string? Reason) Assign(EnemyActionContext ctx, IReadOnlyList<SimCharacter> hits, int index, SimCharacter target)
            => (OnATankJob(target) ? null : (Hit)TankBuster, null);
    }

    // The push starts with the hit: the client slides its own player the moment the effect arrives, and the cap is
    // only exact from where the target stands now.
    private static IEnemyActionEffect[] CruiseChaserHit(TeaP1Presses presses, float knockback, float knockbackSpeed) =>
    [
        Damage(VulnerableTo(P1StatusId.CruiseChaserVulnerability)),
        ApplyStatus(P1StatusId.CruiseChaserVulnerability, P1Duration.CruiseChaserVulnerability),
        Push(Thrown(presses, knockback), knockbackSpeed, pushDelay: 0f),
    ];

    // Bots are taken to have pressed their own anti-knockback: they hold their spots.
    internal static Func<EnemyActionContext, SimCharacter, float?> Thrown(TeaP1Presses presses, float distance)
        => (ctx, target) =>
        {
            if (ctx.Party.IsBotDriven(target) || presses.AntiKnockbackUp(target)) return null;
            var capped = CappedAtTheRim(ctx.Caster.Position, target.Position, distance);
            return capped > 0.01f ? capped : null;
        };

    // How far `at`, pushed straight away from `from`, goes before it would leave the arena: the engine stops only
    // NPCs at a wall.
    internal static float CappedAtTheRim(Vector3 from, Vector3 at, float distance)
    {
        var away = new Vector2(at.X - from.X, at.Z - from.Z);
        if (away.LengthSquared() < 1e-6f) return 0f;
        away = Vector2.Normalize(away);
        var spot = new Vector2(at.X, at.Z);
        var along = Vector2.Dot(spot, away);
        var discriminant = along * along - (spot.LengthSquared() - P1Geometry.ArenaRadius * P1Geometry.ArenaRadius);
        if (discriminant <= 0f) return 0f;
        return MathF.Min(distance, MathF.Max(0f, MathF.Sqrt(discriminant) - along));
    }
}
