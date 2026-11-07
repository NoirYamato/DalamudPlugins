using System;
using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.SimObjects;

namespace AnoMech.Core.Game;

// Invisible helpers a run spawns at its start, each cast taken by the highest-index one that is free, as retail
// picks from its helper pool. A helper is free once its last cast's bar and ActionEffect are done and its animation
// lock has run out. Stands in for a pool the zone itself would spawn.
public sealed class SpawnedHelperPool(SimWorld world, EnemySpawnConfig helper, int count)
{
    private readonly List<SimEnemy> helpers = [];

    // The most helpers busy at once since Spawn, the cast that made it so included.
    public int PeakBusy { get; private set; }

    public int Busy => helpers.Count(IsBusy);

    // A cast on an actor spawned the same frame plays no animation, so this goes before the first cast's frame.
    public void Spawn()
    {
        helpers.Clear();
        PeakBusy = 0;
        for (var i = 0; i < count; i++)
            if (world.SpawnEnemy(helper) is { } spawned)
                helpers.Add(spawned);
    }

    // False, and the cast dropped with a warning, while every helper is still busy.
    public bool Cast(EnemyAction action, CastTarget target, Placement at, byte animationVariation = 0)
    {
        var free = helpers.FindLastIndex(h => h.IsActive && !IsBusy(h));
        if (free < 0)
        {
            Plugin.Log.Warning($"SpawnedHelperPool: all {helpers.Count} helpers busy; action {action.ActionId} dropped.");
            return false;
        }
        var taken = helpers[free];
        taken.SetPosition(at);
        taken.Cast(action, target, animationVariation);
        PeakBusy = Math.Max(PeakBusy, Busy);
        return true;
    }

    private static bool IsBusy(SimEnemy helper) => helper.IsActive && helper.AnimationLock;
}
