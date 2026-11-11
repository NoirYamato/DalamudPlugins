using MitCue.Core.Events;

namespace MitCue.Core.Timelines;

/// <summary>What happened when an event synced the timeline.</summary>
/// <param name="Clock">Monotonic clock of the event.</param>
/// <param name="FromTime">Timeline time just before the sync (0 if the timeline was stopped).</param>
/// <param name="ToTime">Timeline time after the sync (after any jump).</param>
/// <param name="Entry">The entry whose sync matched.</param>
/// <param name="Started">True if this sync started a stopped timeline.</param>
public sealed record SyncResult(double Clock, double FromTime, double ToTime, TimelineEntry Entry, bool Started)
{
    /// <summary>True when the sync moved time forward by more than a normal correction (phase push / jump).</summary>
    public bool IsJump => this.ToTime - this.FromTime > 5.0 || this.ToTime < this.FromTime - 5.0;
}

/// <summary>
/// Plays a <see cref="Timeline"/> against observed game events, following cactbot's playback
/// semantics (cactbot ui/raidboss/timeline.ts; see docs/research/cactbot-timelines.md).
/// All times passed in are a monotonic clock in seconds; the player never reads a clock itself.
/// </summary>
public sealed class TimelinePlayer
{
    public TimelinePlayer(Timeline timeline, SyncMatchOptions? options = null)
    {
        this.Timeline = timeline;
        this.Options = options ?? SyncMatchOptions.Default;
    }

    public Timeline Timeline { get; }

    public SyncMatchOptions Options { get; }

    /// <summary>True while time is advancing (after a start sync, until stop/reset).</summary>
    public bool IsRunning => throw new NotImplementedException();

    /// <summary>Raised whenever an event syncs the timeline (including the start).</summary>
    public event Action<SyncResult>? Synced;

    /// <summary>Raised when the timeline stops or resets.</summary>
    public event Action? Stopped;

    /// <summary>Timeline time at the given clock (frozen at its last value while stopped).</summary>
    public double TimeAt(double clock) => throw new NotImplementedException();

    /// <summary>
    /// Applies an event: syncs/starts the timeline when a matching entry's window contains the
    /// current time; stops it on <see cref="DutyReset"/>. Returns the sync result, if any.
    /// </summary>
    public SyncResult? Process(GameEvent e) => throw new NotImplementedException();

    /// <summary>Advances to the given clock: performs forcejumps that were reached and auto-stops at the end.</summary>
    public void Tick(double clock) => throw new NotImplementedException();

    /// <summary>Stops and rewinds to time 0.</summary>
    public void Reset() => throw new NotImplementedException();

    private void RaiseSynced(SyncResult r) => this.Synced?.Invoke(r);

    private void RaiseStopped() => this.Stopped?.Invoke();
}
