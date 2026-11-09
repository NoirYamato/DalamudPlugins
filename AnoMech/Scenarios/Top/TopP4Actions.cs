using AnoMech.Core.EnemyActions;
using static AnoMech.Core.EnemyActions.EnemyActionEffects;
using static AnoMech.Core.EnemyActions.Severity;
using static AnoMech.Scenarios.Top.TopP3Constants;
using static AnoMech.Scenarios.Top.TopP4Constants;

namespace AnoMech.Scenarios.Top;

// NoirMech's TOP P4 actions, kept out of TopActions so upstream release merges stay clean. Death reasons
// describe the fight, never a strat's spots. UNVERIFIED: no damage values are known, so a repeat line and a
// ring kill on their own.
public static class TopP4Actions
{
    // The scenario decides who the beams and the line stacks kill. A beam's Magic Vulnerability Up is left out:
    // its id and duration are unknown.
    public static readonly EnemyAction WaveCannonBeam = new(P4ActionId.WaveCannonBeam) { Effects = [Damage()] };

    public static readonly EnemyAction LineStack = new(P4ActionId.LineStack) { Effects = [Damage()] };

    public static readonly EnemyAction WaveCannonRepeat = new(P4ActionId.WaveCannonRepeat)
    {
        Cast = new() { OmenDelay = P4Duration.RepeatOmenDelay },
        Effects = [Damage(Lethal)],
        DeathExplanation = "Hit by the repeated Wave Cannon: it fires again down the lines aimed at everyone just before the beams",
    };

    // The same rings as the P3 intermission, but P4 dodges them by stepping in, not out to the wall.
    public static readonly EnemyAction WaveRepeater = new(P3ActionId.WaveRepeater)
    {
        Effects = [Damage(Lethal)],
        DeathExplanation = "Hit by Wave Repeater in the middle",
    };

    public static readonly EnemyAction WaveRepeaterRing2 = WaveRepeaterRing(P3ActionId.WaveRepeaterRing2, P3Geometry.WaveRepeaterInner2,
        "Hit by Wave Repeater's second ring (6-12 y): stay outside 12 y until it has gone off");

    public static readonly EnemyAction WaveRepeaterRing3 = WaveRepeaterRing(P3ActionId.WaveRepeaterRing3, P3Geometry.WaveRepeaterInner3,
        "Hit by Wave Repeater's third ring (12-18 y): step inside 12 y once the second ring has gone off");

    public static readonly EnemyAction WaveRepeaterRing4 = WaveRepeaterRing(P3ActionId.WaveRepeaterRing4, P3Geometry.WaveRepeaterInner4,
        "Hit by Wave Repeater's fourth ring (18-24 y): stay inside 18 y");

    private static EnemyAction WaveRepeaterRing(uint actionId, float innerRadius, string deathExplanation) => new(actionId)
    {
        Area = new() { Size = innerRadius },
        Effects = [Damage(Lethal)],
        DeathExplanation = deathExplanation,
    };
}
