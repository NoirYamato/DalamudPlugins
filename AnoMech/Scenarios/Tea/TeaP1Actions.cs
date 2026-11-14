using System;
using System.Numerics;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.EnemyActions.EnemyActionEffects;
using static AnoMech.Core.EnemyActions.Severity;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Scenarios.Tea;

// Death reasons describe the fight, never a strat's spots. UNVERIFIED: no damage values are known, so a Hawk
// Blaster kills on its own and Cruise Chaser's hits kill only through the vulnerability they leave.
public static class TeaP1Actions
{
    public const string SecondCruiseChaserHit =
        "A second hit from Cruise Chaser: his cleave and his charge each leave a vulnerability, so each may catch only its own number";

    public static readonly EnemyAction HawkBlaster = new(P1ActionId.HawkBlaster)
    {
        Effects = [Damage(Lethal)],
        DeathExplanation = "Caught in the blast: Hawk Blasters go off unmarked, two opposite each other, each pair 45 degrees clockwise of the last, and one in the middle after every fourth pair",
    };

    public static readonly EnemyAction JKick = new(P1ActionId.JKick) { Effects = [Damage()] };

    public static EnemyAction AlphaSword(TeaP1Presses presses) => new(P1ActionId.AlphaSword)
    {
        Area = new() { Size = P1Geometry.AlphaSwordHalfAngle },
        Effects = CruiseChaserHit(presses, P1Rules.AlphaSwordKnockback, P1Rules.AlphaSwordKnockbackSpeed),
        DeathExplanation = SecondCruiseChaserHit,
    };

    public static EnemyAction SuperBlasstyCharge(TeaP1Presses presses) => new(P1ActionId.SuperBlasstyCharge)
    {
        Effects = CruiseChaserHit(presses, P1Rules.SuperBlasstyChargeKnockback, P1Rules.SuperBlasstyChargeKnockbackSpeed),
        DeathExplanation = SecondCruiseChaserHit,
    };

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
