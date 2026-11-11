using MitCue.Core.Events;
using MitCue.Core.Timelines;
using MitCue.Runtime;

namespace MitCue.PluginTests.Support;

/// <summary>
/// A stand-in for the core TimelinePlayer with the cactbot playback rules an <see cref="EncounterSession"/> relies on
/// (docs/research/cactbot-timelines.md §3.7-3.8): an InCombat start edge starts a stopped timeline at 0, combat ending
/// and DutyReset stop it, time runs with the clock, and it stops by itself at <see cref="EndTime"/>.
/// </summary>
internal sealed class ScriptedSyncEngine : ISyncEngine
{
    private static readonly TimelineEntry InCombatSync = new() { Index = 0, Line = 1, Time = 0, Name = "--sync--" };

    private double timebase;
    private double frozen;

    /// <inheritdoc/>
    public event Action<SyncResult>? Synced;

    /// <summary>Timeline time at which the timeline stops by itself, or null.</summary>
    public double? EndTime { get; set; }

    /// <summary>Every event passed to <see cref="Process"/>.</summary>
    public List<GameEvent> Processed { get; } = [];

    /// <inheritdoc/>
    public bool IsRunning { get; private set; }

    /// <inheritdoc/>
    public double TimeAt(double clock) => this.IsRunning ? clock - this.timebase : this.frozen;

    /// <inheritdoc/>
    public SyncResult? Process(GameEvent e)
    {
        this.Processed.Add(e);
        this.Tick(e.Time);
        switch (e)
        {
            case DutyReset or CombatStateChanged { InCombat: false }:
                if (this.IsRunning)
                {
                    this.frozen = e.Time - this.timebase;
                    this.IsRunning = false;
                }

                return null;
            case CombatStateChanged { InCombat: true } when !this.IsRunning:
                this.timebase = e.Time;
                this.IsRunning = true;
                var started = new SyncResult(e.Time, 0, 0, InCombatSync, Started: true);
                this.Synced?.Invoke(started);
                return started;
            default:
                return null;
        }
    }

    /// <inheritdoc/>
    public void Tick(double clock)
    {
        if (this.IsRunning && this.EndTime is { } end && clock - this.timebase >= end)
        {
            this.IsRunning = false;
            this.frozen = end;
        }
    }

    /// <inheritdoc/>
    public void Reset()
    {
        this.IsRunning = false;
        this.frozen = 0;
    }
}
