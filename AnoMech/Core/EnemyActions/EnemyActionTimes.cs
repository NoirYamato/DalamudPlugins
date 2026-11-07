using System;
using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Core.EnemyActions;

// How long after a cast starts it decides who it hits and sends its ActionEffect, timed as EnemyActionHandler times
// them, so a piece can tell strats and tests when its hits land.
public static class EnemyActionTimes
{
    public static float ResolvesAfter(this EnemyAction action)
        => (action.Cast.CastSecondsOverride ?? MathF.Max(0f, SheetCastSeconds(action) - CastSpec.ActionEffectOffset))
           + action.Timing.ResolveSnapshotOffset;

    public static float ReleasesAfter(this EnemyAction action)
        => action.Cast.CastSecondsOverride is { } bar ? bar + CastSpec.ActionEffectOffset : SheetCastSeconds(action);

    private static float SheetCastSeconds(EnemyAction action) => Natives.Data.Action(action.ActionId)?.CastSeconds ?? 0f;
}
