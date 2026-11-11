using MitCue.Core.Actions;
using MitCue.Core.Events;
using MitCue.Core.Fights;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;
using MitCue.Core.Timelines;

namespace MitCue.Cli.Simulation;

/// <summary>
/// The plugin's encounter session without the game: a <see cref="TimelinePlayer"/> fed with game events and a
/// <see cref="ReminderScheduler"/> advanced from timeline time and own action uses. Mirrors
/// src/MitCue/Runtime/EncounterSession.cs (the CLI cannot reference the plugin): the timeline does not stop when combat
/// ends (<see cref="TimelinePlayer.StopOnCombatEnd"/> false, and InCombat edges are not passed to it during a pull), the
/// pull ends after a prolonged out-of-combat, a duty reset or a timeline reset, timeline jumps are forwarded to the
/// scheduler, and a /countdown runs the reminders on timeline time = −remaining.
/// </summary>
internal sealed class PullSession
{
    /// <summary>Seconds out of combat (while the timeline runs) after which the pull is over.</summary>
    public const double OutOfCombatResetSeconds = 10;

    private const float CountdownCancelledAbove = 1f;
    private const double PullGraceAfterCountdown = 20;

    private bool syncedDuringTick;
    private bool timelineReset;
    private double lastInCombatClock;
    private double lastNpcActivityClock;
    private float lastCountdown;
    private double prePullTime;
    private double prePullClock;
    private double? countdownEndedClock;
    private bool countdownMode;

    public PullSession(FightDefinition fight, Timeline timeline, IReadOnlyList<PlanEntry> entries, IActionCatalog actions, ReminderOptions options)
    {
        this.Player = new TimelinePlayer(timeline, new SyncMatchOptions { MatchNames = true }) { StopOnCombatEnd = false };
        this.Player.Synced += this.OnSynced;
        this.Scheduler = new ReminderScheduler(fight, entries, actions, options);
    }

    /// <summary>A timeline sync (each one, including the start, jumps and stops).</summary>
    public event Action<SyncResult>? Synced;

    /// <summary>A timeline jump forwarded to the scheduler (clock, from, to).</summary>
    public event Action<double, double, double>? Jumped;

    /// <summary>The pull ended: summary (before the reset), reason, clock.</summary>
    public event Action<PullSummary, string, double>? PullEnded;

    public TimelinePlayer Player { get; }

    public ReminderScheduler Scheduler { get; }

    /// <summary>True from the timeline start until the pull ends.</summary>
    public bool PullActive { get; private set; }

    /// <summary>Timeline time of the last update (negative during a countdown).</summary>
    public double TimelineTime { get; private set; }

    /// <summary>Clock of the current step, for event handlers.</summary>
    public double StepClock { get; private set; }

    public void Process(GameEvent e)
    {
        this.StepClock = e.Time;
        if (e is CastStarted or AbilityResolved { SourceIsPlayer: false })
            this.lastNpcActivityClock = Math.Max(this.lastNpcActivityClock, e.Time);

        // A dead player can lose the InCombat flag while the fight goes on: during a pull the edges do not reach the
        // timeline (its InCombat sync would restart it at 0); the pull ends by a duty reset or a prolonged out-of-combat.
        if (e is CombatStateChanged && this.PullActive)
            return;

        this.Player.Process(e);
        if (e is DutyReset reset)
            this.EndPull(reset.Kind == DutyResetKind.Wipe ? "wipe" : reset.Kind.ToString(), e.Time);
        else if (this.TakeTimelineReset())
            this.EndPull("timeline reset", e.Time);
    }

    /// <summary>The player used an action at <paramref name="clock"/>.</summary>
    public void OnOwnUse(uint actionId, double clock)
    {
        this.StepClock = clock;
        if (this.TryTimelineTimeAt(clock, out var t))
            this.Scheduler.OnOwnAction(actionId, t);
    }

    /// <summary>Timeline time at a clock the way own uses are timed (pull, countdown or pre-pull hold), if any.</summary>
    public bool TryTimelineTimeAt(double clock, out double t)
    {
        if (this.PullActive || this.Player.IsRunning)
        {
            t = this.Player.TimeAt(clock);
            return true;
        }

        if (this.countdownMode || this.countdownEndedClock != null)
        {
            t = Math.Min(0, this.prePullTime + (clock - this.prePullClock));
            return true;
        }

        t = 0;
        return false;
    }

    /// <summary>Advances the timeline and the reminders to <paramref name="now"/> (one frame).</summary>
    public void Update(double now, float? countdown, bool inCombat)
    {
        this.StepClock = now;
        var before = this.Player.TimeAt(now);
        var wasRunning = this.Player.IsRunning;
        this.syncedDuringTick = false;
        this.Player.Tick(now);
        if (this.TakeTimelineReset())
        {
            this.EndPull("timeline reset", now);
            return;
        }

        var running = this.Player.IsRunning;
        if (wasRunning && running && !this.syncedDuringTick)
        {
            var after = this.Player.TimeAt(now);
            if (Math.Abs(after - before) > 5)
                this.Jump(now, before, after);
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
            if (running && !inCombat
                && now - this.lastInCombatClock > OutOfCombatResetSeconds
                && now - this.lastNpcActivityClock > OutOfCombatResetSeconds)
            {
                this.EndPull("out of combat", now);
                return;
            }

            if (!running && !inCombat)
            {
                this.EndPull("combat ended", now);
                return;
            }
        }

        double t;
        if (this.PullActive)
        {
            this.countdownMode = false;
            t = this.Player.TimeAt(now);
        }
        else if (countdown is { } remaining)
        {
            if (!this.countdownMode || remaining > this.lastCountdown + 0.5f)
                this.Scheduler.Reset();
            this.countdownMode = true;
            this.lastCountdown = remaining;
            this.countdownEndedClock = null;
            t = -remaining;
            this.prePullTime = t;
            this.prePullClock = now;
        }
        else
        {
            if (this.countdownMode)
            {
                if (this.lastCountdown > CountdownCancelledAbove)
                    this.Scheduler.Reset();
                else
                    this.countdownEndedClock = now;
            }

            this.countdownMode = false;
            if (this.countdownEndedClock is not { } ended)
                return;
            if (now - ended > PullGraceAfterCountdown)
            {
                this.countdownEndedClock = null;
                this.Scheduler.Reset();
                return;
            }

            t = Math.Min(0, this.prePullTime + (now - this.prePullClock));
        }

        this.TimelineTime = t;
        this.Scheduler.Update(t);
    }

    /// <summary>
    /// Marks every reminder planned before timeline time <paramref name="time"/> as skipped: a phase preview starts the
    /// fight at that phase, so the earlier reminders were never shown and must not be judged missed at the pull.
    /// </summary>
    public void SkipBefore(double time) => this.Scheduler.OnTimelineJump(double.NegativeInfinity, Math.BitDecrement(time));

    /// <summary>Ends the pull: raises <see cref="PullEnded"/> with the summary when anything happened.</summary>
    public void EndPull(string reason, double clock)
    {
        var touched = this.PullActive || this.Scheduler.All.Any(r => r.State != ReminderState.Pending);
        this.PullActive = false;
        if (touched)
            this.PullEnded?.Invoke(this.Scheduler.Summarize(), reason, clock);
    }

    private bool TakeTimelineReset()
    {
        var reset = this.timelineReset && this.PullActive;
        this.timelineReset = false;
        return reset;
    }

    private void Jump(double clock, double from, double to)
    {
        this.Scheduler.OnTimelineJump(from, to);
        this.Jumped?.Invoke(clock, from, to);
    }

    private void OnSynced(SyncResult result)
    {
        this.syncedDuringTick = true;
        this.Synced?.Invoke(result);
        if (result.Stopped)
        {
            this.timelineReset = true;
            return;
        }

        if (result.IsJump)
            this.Jump(result.Clock, result.FromTime, result.ToTime);
    }
}
