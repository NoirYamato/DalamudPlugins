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
/// <remarks>
/// <para>
/// The timeline is either stopped or running in real time. A sync is active while
/// <c>time − before ≤ now &lt; time + after</c>; while stopped, "now" is 0, so every sync whose window
/// contains 0 (the <c>InCombat</c> sync, the wide <c>window N,0</c> phase syncs) can start it. An event
/// is tried against the active syncs in order of window end, ties in file order, then against the
/// force-jump overhang; the first match wins and only one sync fires per event. A match sets the time
/// to the entry's time, or exactly to its jump target; a jump to 0 stops the timeline.
/// </para>
/// <para>
/// A <c>forcejump</c> jumps when its time is reached, as if it had happened exactly then (the overshoot
/// is kept, unlike cactbot's quirk Q1), and leaves an overhang window <c>[target, target + after)</c> in
/// which its sync can still resync to the target. Every change of time re-arms the force jumps after
/// the new time (cactbot's quirk Q2 does not re-arm them after a backward jump). Force jumps are
/// performed by <see cref="Tick"/> and before each event in <see cref="Process"/>, and they raise
/// <see cref="Synced"/> like event syncs do (with the force-jump entry and the exact jump clock).
/// </para>
/// <para>
/// The timeline stops on a <see cref="DutyReset"/>, when combat ends (<see cref="CombatStateChanged"/>
/// false), on a jump to 0, and by itself <see cref="EndGrace"/> seconds after <see cref="Timeline.EndTime"/>.
/// While stopped, <see cref="TimeAt"/> stays at the time it stopped at (0 after a jump to 0 or
/// <see cref="Reset"/>). Events without a <see cref="GameEvent.SyncType"/> and player abilities are ignored.
/// Not thread-safe.
/// </para>
/// </remarks>
public sealed class TimelinePlayer
{
    /// <summary>Seconds after <see cref="Timeline.EndTime"/> at which a running timeline stops by itself.</summary>
    public const double EndGrace = 30;

    /// <summary>cactbot ignores a resync that moves the time base by at most 2 ms.</summary>
    private const double DedupeSeconds = 0.002;

    /// <summary>Safety limit for force-jump loops performed in one call.</summary>
    private const int MaxForceJumpsPerCall = 10_000;

    private readonly Slot[] syncsByEnd;
    private readonly TimelineEntry[] forceJumps;
    private readonly double autoStopTime;

    private bool running;
    private double timebase;
    private double frozen;
    private Overhang? overhang;
    private int nextForce;

    public TimelinePlayer(Timeline timeline, SyncMatchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        this.Timeline = timeline;
        this.Options = options ?? SyncMatchOptions.Default;
        this.syncsByEnd = timeline.Entries
            .Where(e => e.CanSync)
            .OrderBy(e => e.SyncEnd)
            .ThenBy(e => e.Index)
            .Select(e => new Slot(e))
            .ToArray();
        this.forceJumps = timeline.Entries
            .Where(e => e.ForceJump && e.JumpTime != null)
            .OrderBy(e => e.Time)
            .ThenBy(e => e.Index)
            .ToArray();
        this.autoStopTime = timeline.EndTime + EndGrace;
    }

    public Timeline Timeline { get; }

    public SyncMatchOptions Options { get; }

    /// <summary>True while time is advancing (after a start sync, until stop/reset).</summary>
    public bool IsRunning => this.running;

    /// <summary>Raised whenever an event syncs the timeline (including the start).</summary>
    /// <remarks>Also raised when a forcejump is performed, with the force-jump entry.</remarks>
    public event Action<SyncResult>? Synced;

    /// <summary>Raised when the timeline stops or resets.</summary>
    public event Action? Stopped;

    /// <summary>Timeline time at the given clock (frozen at its last value while stopped).</summary>
    public double TimeAt(double clock) => this.running ? clock - this.timebase : this.frozen;

    /// <summary>
    /// Applies an event: syncs/starts the timeline when a matching entry's window contains the
    /// current time; stops it on <see cref="DutyReset"/>. Returns the sync result, if any.
    /// </summary>
    /// <remarks>
    /// Force jumps due before the event are performed first. Combat ending (<see cref="CombatStateChanged"/>
    /// with InCombat false) stops the timeline too. Feed events in clock order.
    /// </remarks>
    public SyncResult? Process(GameEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e is DutyReset or CombatStateChanged { InCombat: false })
        {
            this.Advance(e.Time);
            this.StopAt(e.Time);
            return null;
        }

        if (e.SyncType == null || e is AbilityResolved { SourceIsPlayer: true })
            return null;

        this.Advance(e.Time);
        var now = this.running ? e.Time - this.timebase : 0;
        var slots = this.syncsByEnd;
        for (var i = FirstEndingAfter(slots, now); i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot.Start > now || !slot.Entry.Sync!.Matches(e, this.Options))
                continue;
            return this.Fire(slot.Entry, slot.Target, slot.Stops, e.Time, now);
        }

        if (this.overhang is { } o && o.Start <= now && now < o.End && o.Entry.Sync!.Matches(e, this.Options))
            return this.Fire(o.Entry, o.Target, stops: false, e.Time, now);
        return null;
    }

    /// <summary>Advances to the given clock: performs forcejumps that were reached and auto-stops at the end.</summary>
    public void Tick(double clock) => this.Advance(clock);

    /// <summary>Stops and rewinds to time 0.</summary>
    public void Reset()
    {
        this.running = false;
        this.timebase = 0;
        this.frozen = 0;
        this.overhang = null;
        this.nextForce = 0;
        this.RaiseStopped();
    }

    /// <summary>
    /// Entries whose sync is active at <paramref name="clock"/>, in matching order (the force-jump
    /// overhang, if active, last). While stopped these are the syncs that can start the timeline.
    /// </summary>
    public IReadOnlyList<TimelineEntry> ActiveSyncs(double clock)
    {
        var now = this.running ? clock - this.timebase : 0;
        var list = new List<TimelineEntry>();
        for (var i = FirstEndingAfter(this.syncsByEnd, now); i < this.syncsByEnd.Length; i++)
        {
            if (this.syncsByEnd[i].Start <= now)
                list.Add(this.syncsByEnd[i].Entry);
        }

        if (this.overhang is { } o && o.Start <= now && now < o.End)
            list.Add(o.Entry);
        return list;
    }

    /// <summary>Index of the first slot whose window ends after <paramref name="now"/> (slots are sorted by end).</summary>
    private static int FirstEndingAfter(Slot[] slots, double now)
    {
        int lo = 0, hi = slots.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (slots[mid].End <= now)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }

    private SyncResult Fire(TimelineEntry entry, double target, bool stops, double clock, double now)
    {
        var started = !this.running;
        var from = this.running ? now : 0;
        if (stops)
        {
            // cactbot: SyncTo(0) then Stop(): stopped at time 0.
            var wasRunning = this.running;
            this.StopAtZero();
            var stopped = new SyncResult(clock, from, 0, entry, Started: false);
            this.RaiseSynced(stopped);
            if (wasRunning)
                this.RaiseStopped();
            return stopped;
        }

        this.SetTime(target, clock);
        var result = new SyncResult(clock, from, target, entry, started);
        this.RaiseSynced(result);
        return result;
    }

    /// <summary>Timeline time := <paramref name="time"/> at <paramref name="clock"/>; re-arms force jumps after it.</summary>
    private void SetTime(double time, double clock)
    {
        this.overhang = null;
        var newBase = clock - time;
        if (this.running && Math.Abs(newBase - this.timebase) <= DedupeSeconds)
            return; // already in sync (cactbot's 2 ms dedupe)
        this.timebase = newBase;
        this.running = true;
        this.nextForce = this.FirstForceJumpAfter(time);
    }

    private int FirstForceJumpAfter(double time)
    {
        var i = 0;
        while (i < this.forceJumps.Length && this.forceJumps[i].Time <= time)
            i++;
        return i;
    }

    /// <summary>Performs the force jumps reached by <paramref name="clock"/>, then the auto-stop at the end.</summary>
    private void Advance(double clock)
    {
        var guard = 0;
        while (this.running && this.nextForce < this.forceJumps.Length && guard++ < MaxForceJumpsPerCall)
        {
            var f = this.forceJumps[this.nextForce];
            if (clock - this.timebase < f.Time)
                break;

            // Jump as if it happened exactly when the force jump's time was reached (Q1 fix).
            var jumpClock = this.timebase + f.Time;
            var target = f.JumpTime!.Value;
            if (target == 0)
            {
                this.StopAtZero();
                this.RaiseSynced(new SyncResult(jumpClock, f.Time, 0, f, Started: false));
                this.RaiseStopped();
                return;
            }

            this.SetTime(target, jumpClock);
            this.nextForce = this.FirstForceJumpAfter(target);
            this.overhang = new Overhang(f, target, target + f.Window.After);
            this.RaiseSynced(new SyncResult(jumpClock, f.Time, target, f, Started: false));
        }

        if (this.running && clock - this.timebase >= this.autoStopTime)
        {
            this.running = false;
            this.frozen = this.autoStopTime;
            this.overhang = null;
            this.nextForce = 0;
            this.RaiseStopped();
        }
    }

    private void StopAt(double clock)
    {
        if (!this.running)
            return;
        this.frozen = clock - this.timebase;
        this.running = false;
        this.overhang = null;
        this.nextForce = 0;
        this.RaiseStopped();
    }

    private void StopAtZero()
    {
        this.running = false;
        this.frozen = 0;
        this.overhang = null;
        this.nextForce = 0;
    }

    private void RaiseSynced(SyncResult r) => this.Synced?.Invoke(r);

    private void RaiseStopped() => this.Stopped?.Invoke();

    /// <summary>A sync in matching order: its window and where it moves the time.</summary>
    private readonly struct Slot(TimelineEntry entry)
    {
        public TimelineEntry Entry { get; } = entry;

        public double Start { get; } = entry.SyncStart;

        public double End { get; } = entry.SyncEnd;

        public double Target { get; } = entry.JumpTime ?? entry.Time;

        /// <summary>True for a jump to 0, which stops the timeline.</summary>
        public bool Stops { get; } = entry.JumpTime == 0;
    }

    /// <summary>The copy of a performed force jump's sync that stays active at its target.</summary>
    private sealed record Overhang(TimelineEntry Entry, double Target, double End)
    {
        public double Start => this.Target;
    }
}
