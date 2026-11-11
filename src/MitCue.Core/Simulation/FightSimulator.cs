using MitCue.Core.Events;
using MitCue.Core.Fights;
using MitCue.Core.Timelines;

namespace MitCue.Core.Simulation;

public sealed class SimulationOptions
{
    /// <summary>Clock value (seconds) at the pull.</summary>
    public double PullClock { get; set; } = 1000;

    /// <summary>
    /// Real seconds from the pull at which each phase's first event happens (phase id → seconds).
    /// Missing phases start at their timeline start (no push variance).
    /// </summary>
    public Dictionary<string, double> PhaseRealStart { get; set; } = [];

    /// <summary>
    /// Timeline time after which a phase's remaining events are dropped (the boss died early),
    /// phase id → timeline seconds. Missing = keep all events until the next phase.
    /// </summary>
    public Dictionary<string, double> PhaseCutoff { get; set; } = [];

    /// <summary>Uniform random jitter (± seconds) added to each event time.</summary>
    public double Jitter { get; set; } = 0.15;

    public int Seed { get; set; } = 1;

    /// <summary>Emit a CombatStateChanged(false) and a DutyReset(Wipe) at this real time after the pull, if set.</summary>
    public double? WipeAt { get; set; }

    /// <summary>Boss source name to use for events whose sync has no source.</summary>
    public string DefaultSource { get; set; } = "Boss";
}

/// <summary>
/// Generates the game events a pull would produce for a timeline: combat start, then one
/// StartsUsing/Ability/... event per synced entry at its (phase-shifted, jittered) time.
/// Used by tests and by the plugin's preview mode.
/// </summary>
public static class FightSimulator
{
    public static IReadOnlyList<GameEvent> Generate(Timeline timeline, FightDefinition? fight, SimulationOptions options) =>
        throw new NotImplementedException();
}
