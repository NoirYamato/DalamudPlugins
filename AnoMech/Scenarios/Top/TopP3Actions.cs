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
}
