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

// Death reasons describe the fight, never a strat's spots. No health is played: a Hawk Blaster, a Sluice and the
// Living Liquid's own untelegraphed cone kill on their own, as they did everyone they hit, and Cruise Chaser's hits
// and the other Protean Waves kill only through what they leave on whoever they hit.
public static class TeaP1Actions
{
    public const string SecondCruiseChaserHit =
        "A second hit from Cruise Chaser: his cleave and his charge each leave a vulnerability, so each may catch only its own number";

    public const string SecondWave =
        "Hit by a Protean Wave while carrying Water Resistance Down: every wave, Fluid Swing and Fluid Strike leaves it for 4 s, and a wave on whoever carries it kills, short of a tank's invulnerability";

    public const string StoodAheadOfTheLiquid =
        "Stood straight ahead of the Living Liquid as an untelegraphed set went off: besides the cones at the four closest players, each set has one along the Liquid's facing, which kills";

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
    // UNVERIFIED: that the telegraphed rows kill no more than that.
    public static EnemyAction ProteanWaveFromTheLiquid(SimWorld world, TeaP1Presses presses) => KnockingWave(P1ActionId.ProteanWaveFromTheLiquid, world, presses);

    public static EnemyAction ProteanWaveAtAPlayer(SimWorld world, TeaP1Presses presses) => KnockingWave(P1ActionId.ProteanWaveAtAPlayer, world, presses);

    // The Living Liquid's own cone of an untelegraphed set kills whoever it hits, carrier or not. UNVERIFIED: that a
    // tank's cooldowns do not take one through it.
    public static readonly EnemyAction ProteanWaveStraightAhead = new(P1ActionId.ProteanWaveStraightAhead)
    {
        Area = new() { Size = P1Geometry.ProteanWaveHalfAngle },
        Effects = [Damage(Lethal, split: new SparesTheInvulnerable()), ApplyStatus(P1StatusId.WaterResistanceDown, P1Duration.WaterResistanceDown)],
        Timing = new() { DamageDelay = P1Duration.LivingLiquidDamageDelay },
        DeathExplanation = StoodAheadOfTheLiquid,
    };

    public static readonly EnemyAction ProteanWaveAtTheClosest = Wave(P1ActionId.ProteanWaveAtTheClosest) with { Area = UnseenWaveCone };

    public static EnemyAction TornadoWave(SimWorld world, TeaP1Presses presses) => KnockingWave(P1ActionId.TornadoWave, world, presses);

    public static readonly EnemyAction TornadoWaveUnseen = Wave(P1ActionId.TornadoWaveUnseen) with { Area = UnseenWaveCone };

    private static AreaSpec UnseenWaveCone => new() { Size = P1Geometry.ProteanWaveHalfAngle, CastTypeOverride = CastType.Cone };

    private static EnemyAction Wave(uint actionId, params IEnemyActionEffect[] more) => new(actionId)
    {
        Area = new() { Size = P1Geometry.ProteanWaveHalfAngle },
        Effects = [Damage(WaveOnACarrier), ApplyStatus(P1StatusId.WaterResistanceDown, P1Duration.WaterResistanceDown), .. more],
        Timing = new() { DamageDelay = P1Duration.LivingLiquidDamageDelay },
        DeathExplanation = SecondWave,
    };

    private static EnemyAction KnockingWave(uint actionId, SimWorld world, TeaP1Presses presses)
        => Wave(actionId, new ThrownAsTheWaveGoesOut(world, presses));

    // The telegraphed rows throw whoever they hit, past the rim if it is near. Whom is decided with the hit, as the
    // bar ends; the client slides its player once the effect arrives, CastSpec.ActionEffectOffset later.
    private sealed class ThrownAsTheWaveGoesOut(SimWorld world, TeaP1Presses presses) : IEnemyActionEffect
    {
        public void Apply(EnemyActionContext ctx, IReadOnlyList<SimCharacter> hits)
        {
            var from = ctx.Caster.Position;
            foreach (var target in hits)
            {
                if (ctx.IsKilled(target) || !target.IsAlive() || ctx.Party.IsBotDriven(target) || presses.AntiKnockbackUp(target)) continue;
                world.Events.Add(CastSpec.ActionEffectOffset, () =>
                {
                    if (target.IsAlive()) target.Push(from, P1Rules.ProteanWaveKnockback, P1Rules.ProteanWaveKnockback / P1Rules.ProteanWaveKnockbackSpeed);
                });
            }
        }
    }

    // -- Sluice --
    public static readonly EnemyAction Sluice = new(P1ActionId.Sluice)
    {
        Effects = [Damage(Lethal, split: new SparesTheInvulnerable())],
        Timing = new() { DamageDelay = P1Duration.LivingLiquidDamageDelay },
        DeathExplanation = "Stood in a Sluice as it went off: one drops under each of the four players farthest from the Living Liquid and goes off 3 s later",
    };

    // A tank's invulnerability takes it through a hit that kills on its own, as through a wave.
    internal static bool Invulnerable(SimCharacter member) => P1StatusId.Invulnerabilities.Any(member.HasStatus);

    private sealed record SparesTheInvulnerable : Distribution
    {
        internal override (Hit? Hit, string? Reason) Assign(EnemyActionContext ctx, IReadOnlyList<SimCharacter> hits, int index, SimCharacter target)
            => (Invulnerable(target) ? (Hit)Normal : null, null);
    }

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

    // -- The Jagd Dolls --
    // One doll's Exhaust harms nobody. TeaP1JagdDolls decides whom two at once kill and what a fed doll does; the
    // other two actions only show the hits.
    public static readonly EnemyAction Exhaust = new(P1ActionId.Exhaust)
    {
        Effects = [Damage(), ApplyStatus(P1StatusId.LuminousAetheroplasm, P1Duration.LuminousAetheroplasm)],
    };

    public static readonly EnemyAction ExhaustFromAPlayer = new(P1ActionId.ExhaustFromAPlayer) { Effects = [Damage()] };

    public static readonly EnemyAction ReducibleComplexity = new(P1ActionId.ReducibleComplexity) { Effects = [Damage()] };

    // -- The cleaves --
    // Each boss's cleave at its target: a non-tank seat in it dies. A tank is asked for no cooldown: one at full
    // health lives through either bare. Both leave Water Resistance Down as a wave does, and neither hits a carrier of
    // it any harder.
    public static readonly EnemyAction FluidSwing = new(P1ActionId.FluidSwing)
    {
        Area = new() { Size = P1Geometry.CleaveHalfAngle },
        Effects =
        [
            Damage(TankBuster),
            ApplyStatus(P1StatusId.WaterResistanceDown, P1Duration.WaterResistanceDown),
        ],
        Timing = new() { DamageDelay = P1Duration.LivingLiquidDamageDelay },
        DeathExplanation = "Caught in Fluid Swing: the Living Liquid cleaves a quarter circle at its target, 11.5 y deep",
    };

    public static readonly EnemyAction FluidStrike = new(P1ActionId.FluidStrike)
    {
        Area = new() { Size = P1Geometry.CleaveHalfAngle },
        Effects =
        [
            Damage(TankBuster),
            ApplyStatus(P1StatusId.WaterResistanceDown, P1Duration.WaterResistanceDown),
        ],
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

    // Each hit leaves both vulnerabilities, and the next one inside the longer of them kills. The push starts with
    // the hit: the client slides its own player the moment the effect arrives. It goes its full length, past the rim
    // if that is nearer.
    private static IEnemyActionEffect[] CruiseChaserHit(TeaP1Presses presses, float knockback, float knockbackSpeed) =>
    [
        Damage(VulnerableTo(P1StatusId.PhysicalVulnerabilityUp)),
        ApplyStatus(P1StatusId.MagicVulnerabilityUp, P1Duration.MagicVulnerabilityUp),
        ApplyStatus(P1StatusId.PhysicalVulnerabilityUp, P1Duration.PhysicalVulnerabilityUp),
        Push(Thrown(presses, knockback), knockbackSpeed, pushDelay: 0f),
    ];

    // Bots are taken to have pressed their own anti-knockback: they hold their spots.
    internal static Func<EnemyActionContext, SimCharacter, float?> Thrown(TeaP1Presses presses, float distance)
        => (ctx, target) => ctx.Party.IsBotDriven(target) || presses.AntiKnockbackUp(target) ? null : distance;
}
