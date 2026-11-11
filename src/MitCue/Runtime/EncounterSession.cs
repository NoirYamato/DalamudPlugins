using MitCue.Core.Actions;
using MitCue.Core.Events;
using MitCue.Core.Fights;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;
using MitCue.Core.Timelines;
using MitCue.Util;

namespace MitCue.Runtime;

/// <summary>Where an <see cref="EncounterSession"/> is in a pull.</summary>
internal enum SessionMode
{
    /// <summary>No pull, no countdown: nothing to show.</summary>
    Idle,

    /// <summary>A /countdown is running: reminders use timeline time = −remaining.</summary>
    Countdown,

    /// <summary>The pull is on (timeline running, or ended while still in combat).</summary>
    Running,
}

/// <summary>Everything needed to start an <see cref="EncounterSession"/>.</summary>
internal sealed class SessionSetup
{
    /// <summary>The fight.</summary>
    public required FightDefinition Fight { get; init; }

    /// <summary>The fight's parsed timeline.</summary>
    public required Timeline Timeline { get; init; }

    /// <summary>The active plan.</summary>
    public required MitPlan Plan { get; init; }

    /// <summary>Who the player is (job, level, slot, column).</summary>
    public required PlayerContext Me { get; init; }

    /// <summary>The player's own entries.</summary>
    public required IReadOnlyList<PlanEntry> OwnEntries { get; init; }

    /// <summary>Other columns' entries (shown dimmed), or null.</summary>
    public IReadOnlyList<PlanEntry>? OtherEntries { get; init; }

    /// <summary>Action catalog for timing defaults.</summary>
    public required IActionCatalog Actions { get; init; }

    /// <summary>Scheduler options (updated in place on config edits).</summary>
    public required ReminderOptions Options { get; init; }

    /// <summary>Timeline sync options.</summary>
    public required SyncMatchOptions SyncOptions { get; init; }

    /// <summary>True for <c>/mitcue preview</c> sessions.</summary>
    public bool IsPreview { get; init; }
}

/// <summary>
/// Runtime state for one fight + plan + player: a <see cref="TimelinePlayer"/> fed with game events and a
/// <see cref="ReminderScheduler"/> (plus an optional one for other columns) advanced from timeline time,
/// own action uses and cooldowns. Handles pre-pull countdowns, pull end (wipe, duty or timeline reset,
/// prolonged out-of-combat, combat end once the timeline has stopped) and the pull summary. Core calls may
/// throw; the owner catches.
/// </summary>
internal sealed class EncounterSession
{
    /// <summary>Seconds out of combat (while the timeline runs) after which the pull is considered over.</summary>
    public const double OutOfCombatResetSeconds = 10;

    /// <summary>Pull-end reason when a timeline sync stopped the timeline (a jump to 0).</summary>
    private const string TimelineResetReason = "timeline reset";

    /// <summary>A countdown that stops with more than this many seconds left was cancelled.</summary>
    private const float CountdownCancelledAbove = 1f;

    /// <summary>How long pre-pull reminders stay after a countdown reached zero without a pull.</summary>
    private const double PullGraceAfterCountdown = 20;

    private static readonly IReadOnlyList<Reminder> NoReminders = [];

    private bool syncedDuringTick;
    private bool timelineReset;
    private double lastInCombatClock;
    private double lastNpcActivityClock;
    private float lastCountdown;
    private double prePullTime;
    private double prePullClock;
    private double? countdownEndedClock;

    /// <summary>Creates the timeline player and schedulers (may throw while the core is incomplete).</summary>
    public EncounterSession(SessionSetup setup)
    {
        this.Fight = setup.Fight;
        this.Plan = setup.Plan;
        this.Me = setup.Me;
        this.Options = setup.Options;
        this.IsPreview = setup.IsPreview;
        this.OwnEntryCount = setup.OwnEntries.Count;
        this.OtherEntryCount = setup.OtherEntries?.Count ?? 0;

        // The session ends the pull after a prolonged out-of-combat (Update), so the player must not stop
        // the moment the combat flag drops: the grace would never run and re-engaging would restart at 0.
        this.Player = new TimelinePlayer(setup.Timeline, setup.SyncOptions) { StopOnCombatEnd = false };
        this.Player.Synced += this.OnSynced;

        this.Scheduler = new ReminderScheduler(setup.Fight, setup.OwnEntries, setup.Actions, setup.Options);
        this.Scheduler.BecameDue += this.OnBecameDue;

        if (setup.OtherEntries is { Count: > 0 } others)
            this.Others = new ReminderScheduler(setup.Fight, others, setup.Actions, setup.Options);

        var ids = new HashSet<uint>();
        foreach (var entry in setup.OwnEntries)
        {
            foreach (var item in entry.Do)
            {
                foreach (var id in item.ActionIds())
                    ids.Add(id);
            }
        }

        this.TrackedActionIds = ids;
    }

    /// <summary>Raised when a pull ends with something to report (summary, reason).</summary>
    public event Action<EncounterSession, PullSummary, string>? PullEnded;

    /// <summary>Raised when one of the player's own reminders becomes due.</summary>
    public event Action<EncounterSession, Reminder>? BecameDue;

    /// <summary>The fight.</summary>
    public FightDefinition Fight { get; }

    /// <summary>The plan.</summary>
    public MitPlan Plan { get; }

    /// <summary>The player context used to select entries.</summary>
    public PlayerContext Me { get; }

    /// <summary>Scheduler options (shared with the schedulers; edited in place).</summary>
    public ReminderOptions Options { get; }

    /// <summary>True for preview sessions.</summary>
    public bool IsPreview { get; }

    /// <summary>Number of the player's entries.</summary>
    public int OwnEntryCount { get; }

    /// <summary>Number of other columns' entries shown dimmed.</summary>
    public int OtherEntryCount { get; }

    /// <summary>The sync engine.</summary>
    public TimelinePlayer Player { get; }

    /// <summary>The player's reminders.</summary>
    public ReminderScheduler Scheduler { get; }

    /// <summary>Other columns' reminders, or null.</summary>
    public ReminderScheduler? Others { get; }

    /// <summary>Action ids of the player's entries (for the cooldown usage fallback).</summary>
    public HashSet<uint> TrackedActionIds { get; }

    /// <summary>Current mode.</summary>
    public SessionMode Mode { get; private set; }

    /// <summary>Timeline time of the last update (negative during a countdown).</summary>
    public double TimelineTime { get; private set; }

    /// <summary>Remaining countdown seconds of the last update (0 without a countdown).</summary>
    public float CountdownRemaining { get; private set; }

    /// <summary>True from the timeline start until the pull ends.</summary>
    public bool PullActive { get; private set; }

    /// <summary>True during a pull or countdown (settings changes wait until it is over).</summary>
    public bool IsBusy => this.PullActive || this.Mode == SessionMode.Countdown;

    /// <summary>The player's reminders to display, ordered by press time.</summary>
    public IReadOnlyList<Reminder> Visible { get; private set; } = NoReminders;

    /// <summary>Other columns' reminders to display, ordered by press time.</summary>
    public IReadOnlyList<Reminder> VisibleOthers { get; private set; } = NoReminders;

    /// <summary>Recent syncs for the Debug tab.</summary>
    public RingBuffer<SyncResult> Syncs { get; } = new(20);

    /// <summary>Number of timeline jumps forwarded to the scheduler.</summary>
    public int Jumps { get; private set; }

    /// <summary>Own uses forwarded to the scheduler.</summary>
    public int OwnUses { get; private set; }

    /// <summary>Feeds one game event (in clock order).</summary>
    public void Process(GameEvent e)
    {
        if (e is CastStarted or AbilityResolved { SourceIsPlayer: false })
            this.lastNpcActivityClock = Math.Max(this.lastNpcActivityClock, e.Time);

        this.Player.Process(e);
        if (e is DutyReset reset)
            this.EndPull(Describe(reset.Kind));
        else if (this.TakeTimelineReset())
            this.EndPull(TimelineResetReason);
    }

    /// <summary>The local player used an action at <paramref name="clock"/>.</summary>
    public void OnOwnUse(uint actionId, double clock)
    {
        if (!this.TryTimelineTimeAt(clock, out var t))
            return;
        this.Scheduler.OnOwnAction(actionId, t);
        this.OwnUses++;
    }

    /// <summary>Another player used an action (only marks other columns' reminders).</summary>
    public void OnPartyUse(uint actionId, double clock)
    {
        if (this.Others != null && this.TryTimelineTimeAt(clock, out var t))
            this.Others.OnOwnAction(actionId, t);
    }

    /// <summary>Advances the timeline and the reminders to <paramref name="now"/>.</summary>
    /// <param name="now">Clock (monotonic seconds; simulated in previews).</param>
    /// <param name="countdown">Remaining /countdown seconds, or null.</param>
    /// <param name="inCombat">The InCombat condition.</param>
    /// <param name="cooldowns">Cooldown provider for the player's reminders (null in previews).</param>
    public void Update(double now, float? countdown, bool inCombat, ICooldownProvider? cooldowns)
    {
        // Tick: forcejumps and auto-stop. Detect jumps that happened without a Synced event.
        var before = this.Player.TimeAt(now);
        var wasRunning = this.Player.IsRunning;
        this.syncedDuringTick = false;
        this.Player.Tick(now);
        if (this.TakeTimelineReset())
        {
            this.EndPull(TimelineResetReason); // a forcejump to 0
            return;
        }

        var running = this.Player.IsRunning;
        if (wasRunning && running && !this.syncedDuringTick)
        {
            var after = this.Player.TimeAt(now);
            if (Math.Abs(after - before) > 5)
                this.Jump(before, after);
        }

        if (running && !this.PullActive)
        {
            this.PullActive = true;
            this.countdownEndedClock = null;
            this.lastInCombatClock = now;
        }

        if (inCombat || !this.PullActive)
            this.lastInCombatClock = now;

        if (this.PullActive)
        {
            // Prolonged out-of-combat (DESIGN §4). Enemy activity keeps the pull alive: a dead player
            // may lose the InCombat flag while the fight goes on.
            if (running && !inCombat
                && now - this.lastInCombatClock > OutOfCombatResetSeconds
                && now - this.lastNpcActivityClock > OutOfCombatResetSeconds)
            {
                this.EndPull("out of combat");
                return;
            }

            if (!running && !inCombat)
            {
                this.EndPull("combat ended");
                return;
            }
        }

        double t;
        if (this.PullActive)
        {
            // Running, or the timeline ended while still in combat (time stays frozen at its end).
            this.Mode = SessionMode.Running;
            this.CountdownRemaining = 0;
            t = this.Player.TimeAt(now);
        }
        else if (countdown is { } remaining)
        {
            if (this.Mode != SessionMode.Countdown || remaining > this.lastCountdown + 0.5f)
                this.ResetReminders(); // a new (or restarted) countdown starts from a clean slate

            this.Mode = SessionMode.Countdown;
            this.CountdownRemaining = remaining;
            this.lastCountdown = remaining;
            this.countdownEndedClock = null;
            t = -remaining;
            this.prePullTime = t;
            this.prePullClock = now;
        }
        else
        {
            if (this.Mode == SessionMode.Countdown)
            {
                if (this.lastCountdown > CountdownCancelledAbove)
                    this.ResetReminders(); // cancelled
                else
                    this.countdownEndedClock = now; // reached zero: the pull should follow
            }

            this.Mode = SessionMode.Idle;
            this.CountdownRemaining = 0;
            if (this.countdownEndedClock is not { } ended)
            {
                this.Visible = NoReminders;
                this.VisibleOthers = NoReminders;
                return;
            }

            if (now - ended > PullGraceAfterCountdown)
            {
                this.countdownEndedClock = null;
                this.ResetReminders();
                return;
            }

            // Between "0" and the pull: hold at time 0 so nothing is judged before the fight starts.
            t = Math.Min(0, this.prePullTime + (now - this.prePullClock));
        }

        this.TimelineTime = t;
        this.Visible = this.Scheduler.Update(t, cooldowns);
        this.VisibleOthers = this.Others?.Update(t) ?? NoReminders;
    }

    /// <summary>Ends the pull without a summary and rewinds everything (<c>/mitcue reset</c>).</summary>
    public void ManualReset() => this.ResetAll();

    /// <summary>Ends the pull: raises <see cref="PullEnded"/> when anything happened, then rewinds.</summary>
    public void EndPull(string reason)
    {
        PullSummary? summary = null;
        if (this.PullActive || this.AnyReminderTouched())
            summary = this.Scheduler.Summarize();

        this.ResetAll();
        if (summary != null)
            this.PullEnded?.Invoke(this, summary, reason);
    }

    /// <summary>The phase containing timeline time <paramref name="t"/>, without allocating.</summary>
    public Phase? PhaseAt(double t)
    {
        Phase? best = null;
        foreach (var phase in this.Fight.Phases)
        {
            if (phase.Start <= t && (best == null || phase.Start >= best.Start))
                best = phase;
        }

        return best;
    }

    private static string Describe(DutyResetKind kind) => kind switch
    {
        DutyResetKind.Wipe => "wipe",
        DutyResetKind.Recommence => "duty recommenced",
        DutyResetKind.Completed => "cleared",
        DutyResetKind.LeftZone => "left the zone",
        _ => kind.ToString(),
    };

    private bool TryTimelineTimeAt(double clock, out double t)
    {
        if (this.PullActive || this.Player.IsRunning)
        {
            t = this.Player.TimeAt(clock);
            return true;
        }

        if (this.Mode == SessionMode.Countdown || this.countdownEndedClock != null)
        {
            t = Math.Min(0, this.prePullTime + (clock - this.prePullClock));
            return true;
        }

        t = 0;
        return false;
    }

    private bool AnyReminderTouched()
    {
        foreach (var r in this.Scheduler.All)
        {
            if (r.State != ReminderState.Pending)
                return true;
        }

        return false;
    }

    private void ResetReminders()
    {
        this.Scheduler.Reset();
        this.Others?.Reset();
        this.Visible = NoReminders;
        this.VisibleOthers = NoReminders;
    }

    private void ResetAll()
    {
        this.Player.Reset();
        this.ResetReminders();
        this.PullActive = false;
        this.Mode = SessionMode.Idle;
        this.TimelineTime = 0;
        this.CountdownRemaining = 0;
        this.lastCountdown = 0;
        this.countdownEndedClock = null;
    }

    /// <summary>
    /// Forwards a timeline jump (a sync that moved time by more than 5 s, <see cref="SyncResult.IsJump"/>, or a jump
    /// seen by a tick) to the schedulers: reminders a phase push jumped over are skipped, and the phase-end reminders
    /// of the phase it ended become due at once (DESIGN §5.2; their sound plays on the next <see cref="Update"/>).
    /// </summary>
    private void Jump(double from, double to)
    {
        this.Scheduler.OnTimelineJump(from, to);
        this.Others?.OnTimelineJump(from, to);
        this.Jumps++;
    }

    /// <summary>True once after a sync stopped the timeline (a jump to 0) during the pull.</summary>
    private bool TakeTimelineReset()
    {
        var reset = this.timelineReset && this.PullActive;
        this.timelineReset = false;
        return reset;
    }

    private void OnSynced(SyncResult result)
    {
        this.syncedDuringTick = true;
        this.Syncs.Add(result);
        if (result.Stopped)
        {
            // A jump to 0 (cactbot's --Reset--, the wipe's fade-in) ends the pull; the caller ends it once the
            // player is done. It is not a jump back to 0: that would put every missed and skipped reminder back
            // to Pending before the summary.
            this.timelineReset = true;
            return;
        }

        if (result.IsJump)
            this.Jump(result.FromTime, result.ToTime);
    }

    private void OnBecameDue(Reminder reminder) => this.BecameDue?.Invoke(this, reminder);
}
