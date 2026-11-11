using System.Globalization;
using MitCue.Core.Actions;
using MitCue.Core.Fights;
using MitCue.Core.Plans;

namespace MitCue.Core.Reminders;

/// <summary>
/// Turns the player's plan entries into reminders and advances their states from timeline time,
/// own action uses and cooldown information. Pure logic: no clock, no game access.
/// </summary>
/// <remarks>
/// <para>
/// Building (docs/DESIGN.md §5.1): one <see cref="Reminder"/> per item of every entry whose
/// mechanic exists in the fight (other entries are reported in <see cref="Problems"/>). All items of
/// an entry share its press time, hit + offset; without an explicit offset the default comes from
/// the entry's timing action, i.e. the first item action the catalog resolves (for an either-or
/// item: its first resolvable option). Each item's use window comes from its own timing action.
/// </para>
/// <para>
/// States (§5.2) only move forward on <see cref="Update"/>: Pending → Upcoming → Due → Missed (or
/// Passed for notes and optional entries); <see cref="OnOwnAction"/> marks Done and
/// <see cref="OnTimelineJump"/> marks Skipped. Only a backward jump of more than 5 s or
/// <see cref="Reset"/> moves reminders back to Pending.
/// </para>
/// <para>
/// Not thread-safe: call every member from one thread (or synchronize externally).
/// <see cref="BecameDue"/> handlers run synchronously inside <see cref="Update"/>.
/// <see cref="ReminderOptions.Lookahead"/>, <see cref="ReminderOptions.DueLead"/>,
/// <see cref="ReminderOptions.KeepFinishedFor"/> and <see cref="ReminderOptions.ShowSkipped"/> are read
/// live; the use windows (<see cref="Reminder.Earliest"/>, <see cref="Reminder.Latest"/>) are fixed
/// when the scheduler is built.
/// </para>
/// </remarks>
public sealed class ReminderScheduler
{
    /// <summary>Capacity of <see cref="RecentUses"/>.</summary>
    public const int RecentUsesCapacity = 50;

    /// <summary>Lateness below this is not reported in <see cref="Reminder.ReadyIn"/>.</summary>
    private const double ReadyInThreshold = 0.25;

    /// <summary>Backward time corrections up to this size are normal sync jitter, not rewinds.</summary>
    private const double SmallBackwardCorrection = 5;

    private readonly Track[] tracks;
    private readonly List<Reminder> visibleBuffer = [];
    private readonly List<ActionUse> recentUses = new(RecentUsesCapacity);

    public ReminderScheduler(FightDefinition fight, IEnumerable<PlanEntry> entries, IActionCatalog actions, ReminderOptions options)
    {
        ArgumentNullException.ThrowIfNull(fight);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(options);

        this.Fight = fight;
        this.Options = options;

        var problems = new List<string>();
        this.tracks = Build(fight, entries, actions, options, problems);
        this.All = Array.AsReadOnly(Array.ConvertAll(this.tracks, t => t.Reminder));
        this.Problems = problems.AsReadOnly();
        this.RecentUses = this.recentUses.AsReadOnly();
    }

    public FightDefinition Fight { get; }

    public ReminderOptions Options { get; }

    /// <summary>All reminders of the pull, ordered by press time (then plan entry order, then item index).</summary>
    public IReadOnlyList<Reminder> All { get; }

    /// <summary>Plan entries that could not be scheduled (unknown mechanic), one message each.</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>
    /// The last <see cref="RecentUsesCapacity"/> own action uses, oldest first, each with the reminder it
    /// satisfied (null when the use was outside every window and therefore ignored). For debugging.
    /// </summary>
    public IReadOnlyList<ActionUse> RecentUses { get; }

    /// <summary>Raised once when a reminder becomes Due (the plugin plays a sound).</summary>
    /// <remarks>
    /// Not raised for reminders that finish (Missed/Passed) in the same update. Raised again only
    /// after the reminder went back to Pending (<see cref="Reset"/> or a backward jump).
    /// </remarks>
    public event Action<Reminder>? BecameDue;

    /// <summary>Back to the start of a pull (all reminders Pending).</summary>
    public void Reset()
    {
        foreach (var track in this.tracks)
        {
            track.Rearm();
            var r = track.Reminder;
            r.UsedAt = null;
            r.UsedActionId = null;
            r.UsedEarly = false;
        }
        this.recentUses.Clear();
        this.visibleBuffer.Clear();
    }

    /// <summary>The timeline jumped from <paramref name="fromTime"/> to <paramref name="toTime"/>; reminders in between that never became due are Skipped.</summary>
    /// <remarks>
    /// Forward jump: Pending/Upcoming reminders with a press time in (from, to] become Skipped
    /// (finished at <paramref name="toTime"/>); Due and finished reminders are left alone.
    /// Backward jump of more than 5 s: every reminder with a press time after <paramref name="toTime"/>
    /// that is not Done goes back to Pending (the next <see cref="Update"/> re-derives Upcoming/Due).
    /// Smaller backward corrections change nothing.
    /// </remarks>
    public void OnTimelineJump(double fromTime, double toTime)
    {
        if (toTime > fromTime)
        {
            foreach (var track in this.tracks)
            {
                var r = track.Reminder;
                if ((r.State is ReminderState.Pending or ReminderState.Upcoming) && r.PressAt > fromTime && r.PressAt <= toTime)
                    Finish(r, ReminderState.Skipped, toTime);
            }
        }
        else if (toTime < fromTime - SmallBackwardCorrection)
        {
            foreach (var track in this.tracks)
            {
                var r = track.Reminder;
                if (r.PressAt > toTime && r.State is not (ReminderState.Done or ReminderState.Pending))
                    track.Rearm();
            }
        }
    }

    /// <summary>The local player used an action at the given timeline time.</summary>
    /// <remarks>
    /// The use satisfies at most one reminder: the not-finished action reminder with the smallest
    /// press time whose item contains the action and whose window [press − lookahead, latest]
    /// contains the time (for when-ready entries, latest as extended by <see cref="Update"/> while
    /// waiting for the cooldown). It becomes Done; a use before <see cref="Reminder.Earliest"/> is
    /// flagged <see cref="Reminder.UsedEarly"/>. Every use is recorded in <see cref="RecentUses"/>.
    /// </remarks>
    public void OnOwnAction(uint actionId, double timelineTime)
    {
        var t = timelineTime;
        Reminder? matched = null;
        foreach (var track in this.tracks)
        {
            var r = track.Reminder;
            if (IsFinished(r.State)
                || t < r.PressAt - this.Options.Lookahead
                || t > track.Deadline
                || !Contains(track.ActionIds, actionId))
            {
                continue;
            }

            r.State = ReminderState.Done;
            r.UsedAt = t;
            r.UsedActionId = actionId;
            r.UsedEarly = t < r.Earliest;
            r.FinishedAt = t;
            r.ReadyIn = null;
            r.MissingResources = false;
            matched = r;
            break;
        }

        if (this.recentUses.Count >= RecentUsesCapacity)
            this.recentUses.RemoveAt(0);
        this.recentUses.Add(new ActionUse(actionId, t, matched));
    }

    /// <summary>Advances states to <paramref name="timelineTime"/> and returns the reminders to display, ordered by press time.</summary>
    /// <remarks>
    /// <para>
    /// Visible: Upcoming and Due reminders, plus Done and Missed ones (and Skipped ones when
    /// <see cref="ReminderOptions.ShowSkipped"/>) for <see cref="ReminderOptions.KeepFinishedFor"/>
    /// seconds after they finished. The result is a new snapshot; it is not changed by later calls.
    /// </para>
    /// <para>
    /// Cooldown awareness (Upcoming/Due action reminders, first option of the item):
    /// <see cref="Reminder.ReadyIn"/> is how many seconds after the later of the press time and now
    /// the action becomes ready (reported when more than 0.25 s), and
    /// <see cref="Reminder.MissingResources"/> is set when <paramref name="cooldowns"/> reports a missing
    /// job resource. When-ready entries stay Due past <see cref="Reminder.Latest"/> while the action is
    /// reported on cooldown; once it is ready they get max(latest − now, heal late window) more seconds.
    /// </para>
    /// </remarks>
    public IReadOnlyList<Reminder> Update(double timelineTime, ICooldownProvider? cooldowns = null)
    {
        foreach (var track in this.tracks)
            this.Advance(track, timelineTime, cooldowns);
        return this.CollectVisible(timelineTime);
    }

    /// <summary>
    /// Counts over action reminders of non-optional entries (notes and optional entries are never
    /// counted): Total = Done + Missed, Skipped, and the missed reminders ordered by press time.
    /// </summary>
    public PullSummary Summarize()
    {
        int done = 0, missed = 0, skipped = 0;
        var missedReminders = new List<Reminder>();
        foreach (var track in this.tracks)
        {
            var r = track.Reminder;
            if (!track.IsAction || r.Entry.Optional)
                continue;
            switch (r.State)
            {
                case ReminderState.Done:
                    done++;
                    break;
                case ReminderState.Missed:
                    missed++;
                    missedReminders.Add(r);
                    break;
                case ReminderState.Skipped:
                    skipped++;
                    break;
            }
        }
        return new PullSummary(done + missed, done, missed, skipped, missedReminders.AsReadOnly());
    }

    private static Track[] Build(FightDefinition fight, IEnumerable<PlanEntry> entries, IActionCatalog actions, ReminderOptions options, List<string> problems)
    {
        var built = new List<Track>();
        var entryIndex = -1;
        foreach (var entry in entries)
        {
            entryIndex++;
            if (entry == null)
                continue;

            var mechanic = fight.GetMechanic(entry.Mechanic);
            if (mechanic == null)
            {
                problems.Add(DescribeUnknownMechanic(fight, entry, entryIndex));
                continue;
            }
            if (entry.Do is not { Count: > 0 } items)
                continue;

            var hit = mechanic.HitAt(entry.HitIndex ?? 0);

            // Items of one entry share the press time; without an explicit offset it follows the
            // first item whose action the catalog knows.
            var timing = new ActionInfo?[items.Count];
            ActionInfo? entryTiming = null;
            for (var i = 0; i < items.Count; i++)
            {
                timing[i] = items[i] is { } item ? FirstResolvable(item, actions) : null;
                entryTiming ??= timing[i];
            }
            var pressAt = hit + (entry.Offset ?? TimingRules.DefaultOffset(entryTiming, fight.SyncLevel));

            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] is not { } item)
                    continue;
                var (earliest, latest) = TimingRules.UseWindow(timing[i], fight.SyncLevel, hit, pressAt, options);
                var reminder = new Reminder
                {
                    Entry = entry,
                    Item = item,
                    ItemIndex = i,
                    Mechanic = mechanic,
                    Hit = hit,
                    PressAt = pressAt,
                    Earliest = earliest,
                    Latest = latest,
                };
                built.Add(new Track(reminder, entryIndex));
            }
        }

        // (press time, entry order, item index) is a total order, so the unstable sort is deterministic.
        built.Sort(CompareTracks);
        return built.ToArray();
    }

    /// <summary>The timing action of an item: its action, or the first option of an either-or item the catalog resolves.</summary>
    private static ActionInfo? FirstResolvable(PlanItem item, IActionCatalog actions)
    {
        foreach (var id in item.ActionIds())
        {
            if (actions.Get(id) is { } action)
                return action;
        }
        return null;
    }

    private static string DescribeUnknownMechanic(FightDefinition fight, PlanEntry entry, int entryIndex)
    {
        var name = string.IsNullOrEmpty(entry.Id) ? "#" + (entryIndex + 1).ToString(CultureInfo.InvariantCulture) : "'" + entry.Id + "'";
        var src = string.IsNullOrEmpty(entry.Src) ? string.Empty : " (" + entry.Src + ")";
        return $"Entry {name}{src}: unknown mechanic '{entry.Mechanic}' in fight '{fight.Key}'; skipped.";
    }

    private static int CompareTracks(Track a, Track b)
    {
        var c = a.Reminder.PressAt.CompareTo(b.Reminder.PressAt);
        if (c != 0)
            return c;
        c = a.EntryIndex.CompareTo(b.EntryIndex);
        return c != 0 ? c : a.Reminder.ItemIndex.CompareTo(b.Reminder.ItemIndex);
    }

    private static bool IsFinished(ReminderState state) =>
        state is ReminderState.Done or ReminderState.Missed or ReminderState.Skipped or ReminderState.Passed;

    private static void Finish(Reminder r, ReminderState state, double at)
    {
        r.State = state;
        r.FinishedAt = at;
        r.ReadyIn = null;
        r.MissingResources = false;
    }

    private static bool Contains(IReadOnlyList<uint> ids, uint id)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i] == id)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Seconds the action becomes ready after the later of the press time and now (so a ready action
    /// is never reported late), or null when unknown or not more than <see cref="ReadyInThreshold"/>.
    /// </summary>
    private static double? Lateness(double? secondsUntilReady, double pressAt, double t)
    {
        if (secondsUntilReady is not { } wait)
            return null;
        var late = wait - Math.Max(pressAt - t, 0);
        return late > ReadyInThreshold ? late : null;
    }

    private void Advance(Track track, double t, ICooldownProvider? cooldowns)
    {
        var r = track.Reminder;
        if (IsFinished(r.State))
            return;

        var options = this.Options;
        var isAction = track.IsAction;

        // Cooldown of the item's first option, for reminders that are (becoming) visible or overdue.
        double? secondsUntilReady = null;
        if (isAction && cooldowns != null && (t >= r.PressAt - options.Lookahead || t > track.Deadline))
            secondsUntilReady = cooldowns.SecondsUntilReady(track.PrimaryActionId);

        if (isAction && r.Entry.WhenReady && secondsUntilReady is { } wait)
            track.ObserveReadiness(wait, t, options.HealLateWindow);

        if (t > track.Deadline)
        {
            Finish(r, isAction && !r.Entry.Optional ? ReminderState.Missed : ReminderState.Passed, t);
            return;
        }

        var becameDue = false;
        if (t >= r.PressAt - options.DueLead)
        {
            if (r.State != ReminderState.Due)
            {
                r.State = ReminderState.Due;
                becameDue = !track.DueRaised;
                track.DueRaised = true;
            }
        }
        else if (r.State == ReminderState.Pending && t >= r.PressAt - options.Lookahead)
        {
            r.State = ReminderState.Upcoming;
        }

        if (isAction && r.State is ReminderState.Upcoming or ReminderState.Due)
        {
            r.ReadyIn = Lateness(secondsUntilReady, r.PressAt, t);
            r.MissingResources = cooldowns?.HasResources(track.PrimaryActionId) == false;
        }
        else
        {
            r.ReadyIn = null;
            r.MissingResources = false;
        }

        if (becameDue)
            this.RaiseBecameDue(r);
    }

    private IReadOnlyList<Reminder> CollectVisible(double t)
    {
        var buffer = this.visibleBuffer;
        buffer.Clear();
        foreach (var track in this.tracks)
        {
            if (this.IsVisible(track.Reminder, t))
                buffer.Add(track.Reminder);
        }
        return buffer.Count == 0 ? Array.Empty<Reminder>() : buffer.ToArray();
    }

    private bool IsVisible(Reminder r, double t) => r.State switch
    {
        ReminderState.Upcoming or ReminderState.Due => true,
        ReminderState.Done or ReminderState.Missed => this.RecentlyFinished(r, t),
        ReminderState.Skipped => this.Options.ShowSkipped && this.RecentlyFinished(r, t),
        _ => false,
    };

    /// <summary>Finished at most KeepFinishedFor ago (tolerating small backward time corrections).</summary>
    private bool RecentlyFinished(Reminder r, double t) =>
        r.FinishedAt is { } at && t - at <= this.Options.KeepFinishedFor && at - t <= SmallBackwardCorrection;

    private void RaiseBecameDue(Reminder r) => this.BecameDue?.Invoke(r);

    /// <summary>A reminder plus the scheduler's private bookkeeping for it.</summary>
    private sealed class Track
    {
        public Track(Reminder reminder, int entryIndex)
        {
            this.Reminder = reminder;
            this.EntryIndex = entryIndex;
            this.ActionIds = reminder.ActionIds;
            this.Deadline = reminder.Latest;
        }

        public Reminder Reminder { get; }

        /// <summary>Position of the entry in the plan (tie-breaker for equal press times).</summary>
        public int EntryIndex { get; }

        public IReadOnlyList<uint> ActionIds { get; }

        public bool IsAction => this.ActionIds.Count > 0;

        /// <summary>The item's first option: the action whose cooldown and resources are checked.</summary>
        public uint PrimaryActionId => this.ActionIds[0];

        /// <summary>
        /// After this the reminder is missed: <see cref="Reminder.Latest"/>, or later for a when-ready
        /// entry whose action was on cooldown (infinite while it still is).
        /// </summary>
        public double Deadline { get; private set; }

        /// <summary>When-ready entries: first time the action was seen ready (in the current ready streak).</summary>
        public double? ReadySince { get; private set; }

        /// <summary><see cref="ReminderScheduler.BecameDue"/> was raised since the reminder last left Pending.</summary>
        public bool DueRaised { get; set; }

        /// <summary>When-ready entries: stay due while the action is on cooldown; once ready, allow max(latest − now, grace).</summary>
        public void ObserveReadiness(double secondsUntilReady, double t, double grace)
        {
            if (secondsUntilReady > 0)
            {
                this.ReadySince = null;
                this.Deadline = double.PositiveInfinity;
            }
            else
            {
                this.ReadySince ??= t;
                this.Deadline = Math.Max(this.Reminder.Latest, this.ReadySince.Value + grace);
            }
        }

        /// <summary>Back to Pending with no outcome (the usage of Done reminders is cleared separately).</summary>
        public void Rearm()
        {
            var r = this.Reminder;
            r.State = ReminderState.Pending;
            r.FinishedAt = null;
            r.ReadyIn = null;
            r.MissingResources = false;
            this.DueRaised = false;
            this.ReadySince = null;
            this.Deadline = r.Latest;
        }
    }
}
