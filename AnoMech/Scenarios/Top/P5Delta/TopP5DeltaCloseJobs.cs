using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.Top.P5Delta;

// The monitor and both Hello World tethers only go to the four close seats, and a seat that asked for no band sits
// where the roll put it, so seats refusing those jobs could fill the close four while a seat that takes them sits far.
internal static class TopP5DeltaCloseJobs
{
    // Swaps seats that asked for no band between the close and far slots until the close four can take every job,
    // drawing from the roll only when a swap is needed.
    internal static void SeatTakersClose(Rng rng, PartyRole[] roles, bool[] claimed,
                                         IReadOnlyDictionary<PartyRole, bool> monitors,
                                         IReadOnlyDictionary<PartyRole, HelloWorldOption> hellos)
    {
        var missing = Missing(roles[..4], monitors, hellos);
        while (missing > 0)
        {
            var swaps = (from close in Enumerable.Range(0, 4)
                         where !claimed[close]
                         from far in Enumerable.Range(4, 4)
                         where !claimed[far]
                         let closeFour = roles[..4].Select((role, slot) => slot == close ? roles[far] : role).ToArray()
                         select (close, far, missing: Missing(closeFour, monitors, hellos))).ToList();
            if (swaps.Count == 0) return;
            var fewest = swaps.Min(swap => swap.missing);
            if (fewest >= missing) return;
            var best = swaps.Where(swap => swap.missing == fewest).ToList();
            var (closeSlot, farSlot, _) = best[rng.Next(best.Count)];
            (roles[closeSlot], roles[farSlot]) = (roles[farSlot], roles[closeSlot]);
            missing = fewest;
        }
    }

    // Settings where no seating of the seats that asked for no band gives the close four a taker for every job.
    internal static void FlagTooFewTakers(SettingsConflicts conflicts, ILookup<PlayerTetherAssignment, PartyRole> bands,
                                          PerRoleSetting<bool> monitor, PerRoleSetting<HelloWorldOption> helloWorld)
    {
        var monitors = PerRole.All.Where(role => monitor[role] is not null).ToDictionary(role => role, role => monitor[role]!.Value);
        var hellos = PerRole.All.Where(role => helloWorld[role] is not null).ToDictionary(role => role, role => helloWorld[role]!.Value);
        var close = bands[PlayerTetherAssignment.CloseAny].Concat(bands[PlayerTetherAssignment.CloseInner])
                                                          .Concat(bands[PlayerTetherAssignment.CloseOuter]).ToList();
        var unbanded = bands[PlayerTetherAssignment.Auto].ToList();

        bool monitorFits = false, helloFits = false, bothFit = false;
        for (var picked = 0; picked < 1 << unbanded.Count; picked++)
        {
            if (BitOperations.PopCount((uint)picked) != 4 - close.Count) continue;
            var closeFour = close.Concat(unbanded.Where((_, i) => (picked >> i & 1) == 1)).ToArray();
            var (monitorMissing, helloMissing) = MissingTakers(closeFour, monitors, hellos);
            monitorFits |= monitorMissing == 0;
            helloFits |= helloMissing == 0;
            bothFit |= monitorMissing + helloMissing == 0;
        }

        var mayBeClose = close.Concat(unbanded).ToList();
        var refuseMonitor = mayBeClose.Where(role => !monitors.GetValueOrDefault(role, true)).ToList();
        var refuseHello = mayBeClose.Where(role => hellos.GetValueOrDefault(role) == HelloWorldOption.No).ToList();
        if (!monitorFits)
            conflicts.Add($"{SettingsConflicts.Seats(refuseMonitor)} refuse the monitor, but it goes to a close player, and the tether choices leave no close player to take it.");
        if (!helloFits)
            conflicts.Add($"{SettingsConflicts.Seats(refuseHello)} refuse Hello World, but its tethers go to close players, and the tether choices leave too few close players to take them.");
        if (monitorFits && helloFits && !bothFit)
            conflicts.Add($"{SettingsConflicts.Seats(refuseMonitor.Union(refuseHello))} refuse the monitor or Hello World, but they go to close players, and the tether choices leave too few close players to take them all.");
    }

    private static int Missing(IReadOnlyCollection<PartyRole> closeFour, IReadOnlyDictionary<PartyRole, bool> monitors,
                               IReadOnlyDictionary<PartyRole, HelloWorldOption> hellos)
    {
        var (monitor, hello) = MissingTakers(closeFour, monitors, hellos);
        return monitor + hello;
    }

    // A job one of the close four asked for is theirs. Each other job needs a close seat that didn't refuse it, and
    // Near and Far need two different ones.
    private static (int Monitor, int Hello) MissingTakers(IReadOnlyCollection<PartyRole> closeFour,
                                                          IReadOnlyDictionary<PartyRole, bool> monitors,
                                                          IReadOnlyDictionary<PartyRole, HelloWorldOption> hellos)
    {
        var monitorNeeded = closeFour.Any(role => monitors.GetValueOrDefault(role)) ? 0 : 1;
        var monitorTakers = closeFour.Count(role => monitors.GetValueOrDefault(role, true));
        var tethersNeeded = (closeFour.Any(role => hellos.GetValueOrDefault(role) == HelloWorldOption.Near) ? 0 : 1)
                            + (closeFour.Any(role => hellos.GetValueOrDefault(role) == HelloWorldOption.Far) ? 0 : 1);
        var tetherTakers = closeFour.Count(role => hellos.GetValueOrDefault(role) == HelloWorldOption.Auto);
        return (Math.Max(0, monitorNeeded - monitorTakers), Math.Max(0, tethersNeeded - tetherTakers));
    }
}
