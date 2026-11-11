using MitCue.Core.Events;

namespace MitCue.Core.Simulation;

/// <summary>
/// The kinds of game event the plugin observes in game (its GameEventSource): combat start and end, boss cast starts,
/// resolved abilities, and duty wipes and recommences (docs/DESIGN.md §4). The fight simulator also produces the other
/// kinds a timeline syncs on (MapEffect, director updates, head markers, ...), which exist only in simulations; a
/// simulated pull played like a live one keeps just these (<c>/mitcue preview</c>, <c>mitcue simulate</c> without
/// <c>--all-events</c>), so it syncs the way a live pull does.
/// </summary>
public static class GameVisibleEvents
{
    /// <summary>True for an event of a kind the plugin sees in game.</summary>
    public static bool Contains(GameEvent e) => e is CombatStateChanged or CastStarted or AbilityResolved or DutyReset;
}
