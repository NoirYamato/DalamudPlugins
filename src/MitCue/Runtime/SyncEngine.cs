using MitCue.Core.Events;
using MitCue.Core.Timelines;

namespace MitCue.Runtime;

/// <summary>
/// The sync engine an <see cref="EncounterSession"/> drives: the members of the core <see cref="TimelinePlayer"/> it
/// uses (<see cref="TimelineSyncEngine"/>), so tests can run a session with a scripted engine.
/// </summary>
internal interface ISyncEngine
{
    /// <summary>Raised whenever an event (or a forcejump) syncs the timeline.</summary>
    event Action<SyncResult>? Synced;

    /// <summary>True while timeline time advances.</summary>
    bool IsRunning { get; }

    /// <summary>Timeline time at the given clock (frozen while stopped).</summary>
    double TimeAt(double clock);

    /// <summary>Applies one game event (in clock order).</summary>
    SyncResult? Process(GameEvent e);

    /// <summary>Advances to the given clock (forcejumps, auto-stop).</summary>
    void Tick(double clock);

    /// <summary>Stops and rewinds to time 0.</summary>
    void Reset();
}

/// <summary><see cref="ISyncEngine"/> backed by the core <see cref="TimelinePlayer"/>.</summary>
internal sealed class TimelineSyncEngine : ISyncEngine
{
    private readonly TimelinePlayer player;

    /// <summary>Creates the player for <paramref name="timeline"/>.</summary>
    public TimelineSyncEngine(Timeline timeline, SyncMatchOptions options)
    {
        // The session decides when a pull is over (prolonged out-of-combat), so a momentary drop of the
        // combat flag must not stop the timeline (it would restart at 0 on the next combat start).
        this.player = new TimelinePlayer(timeline, options) { StopOnCombatEnd = false };
    }

    /// <inheritdoc/>
    public event Action<SyncResult>? Synced
    {
        add => this.player.Synced += value;
        remove => this.player.Synced -= value;
    }

    /// <inheritdoc/>
    public bool IsRunning => this.player.IsRunning;

    /// <inheritdoc/>
    public double TimeAt(double clock) => this.player.TimeAt(clock);

    /// <inheritdoc/>
    public SyncResult? Process(GameEvent e) => this.player.Process(e);

    /// <inheritdoc/>
    public void Tick(double clock) => this.player.Tick(clock);

    /// <inheritdoc/>
    public void Reset() => this.player.Reset();
}
