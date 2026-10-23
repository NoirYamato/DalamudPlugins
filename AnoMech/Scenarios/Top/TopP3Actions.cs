using AnoMech.Core.EnemyActions;
using static AnoMech.Core.EnemyActions.EnemyActionEffects;
using static AnoMech.Core.EnemyActions.Severity;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP3Constants;

namespace AnoMech.Scenarios.Top;

// NoirMech's TOP P3 actions, kept out of TopActions so upstream release merges stay clean.
public static class TopP3Actions
{
    // -- P3 Hello World --
    // The scenario decides who these kill from the debuffs it tracks; the actions only show the hit.
    public static readonly EnemyAction HelloWorld = new(P3ActionId.HelloWorld) { Effects = [Damage()] };

    public static readonly EnemyAction CriticalError = new(P3ActionId.CriticalError) { Effects = [Damage()] };

    public static readonly EnemyAction Stack = new(P3ActionId.Stack) { Effects = [Damage()] };

    public static readonly EnemyAction Defamation = new(P3ActionId.Defamation) { Effects = [Damage()] };

    public static readonly EnemyAction RedRotExplosion = new(P3ActionId.RedRotExplosion) { Effects = [Damage()] };

    public static readonly EnemyAction BlueRotExplosion = new(P3ActionId.BlueRotExplosion) { Effects = [Damage()] };

    public static readonly EnemyAction RegressionUnbroken = new(ActionId.HwTetherFail)
    {
        Effects = [Damage(Lethal)],
        DeathExplanation = "a regression tether ran out unbroken",
    };
    // -- end P3 Hello World --

    // -- P3 Intermission --
    // Death reasons describe the fight, never a strat's spots. UNVERIFIED: no damage values are known, so every
    // ring and blow kills.
    public static readonly EnemyAction WaveRepeater = new(P3ActionId.WaveRepeater)
    {
        Effects = [Damage(Lethal)],
        DeathExplanation = "Hit by Wave Repeater in the middle",
    };

    // Ring 1's band is the void zone, so no reason may send anyone back into it.
    public static readonly EnemyAction WaveRepeaterRing2 = WaveRepeaterRing(P3ActionId.WaveRepeaterRing2, P3Geometry.WaveRepeaterInner2,
        "Hit by Wave Repeater's second ring (6-12 y): the waves travel outward, so be out at the wall before it");

    public static readonly EnemyAction WaveRepeaterRing3 = WaveRepeaterRing(P3ActionId.WaveRepeaterRing3, P3Geometry.WaveRepeaterInner3,
        "Hit by Wave Repeater's third ring (12-18 y): wait at the wall until it has passed");

    public static readonly EnemyAction WaveRepeaterRing4 = WaveRepeaterRing(P3ActionId.WaveRepeaterRing4, P3Geometry.WaveRepeaterInner4,
        "Hit by Wave Repeater's fourth ring (18-24 y): step in behind the third ring once it has passed");

    public static readonly EnemyAction ColossalBlow = new(P3ActionId.ColossalBlow)
    {
        Effects = [Damage(Lethal)],
        DeathExplanation = "Hit by Colossal Blow: the arms that appeared first go off first",
    };

    // The scenario decides who the cannons kill.
    public static readonly EnemyAction SniperCannon = new(ActionId.SniperCannon) { Effects = [Damage()] };

    public static readonly EnemyAction HighPoweredSniperCannon = new(ActionId.HighPoweredSniperCannon) { Effects = [Damage()] };

    private static EnemyAction WaveRepeaterRing(uint actionId, float innerRadius, string deathExplanation) => new(actionId)
    {
        Area = new() { Size = innerRadius },
        Effects = [Damage(Lethal)],
        DeathExplanation = deathExplanation,
    };
    // -- end P3 Intermission --
}
