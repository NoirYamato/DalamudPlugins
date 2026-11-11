using MitCue.Core.Fights;
using MitCue.Core.Plans;

namespace MitCue.Core.Reminders;

public enum ReminderState
{
    /// <summary>Not yet within the lookahead window.</summary>
    Pending,

    /// <summary>Visible, counting down.</summary>
    Upcoming,

    /// <summary>Press now.</summary>
    Due,

    Done,

    Missed,

    /// <summary>
    /// The timeline jumped past it (phase push) before it was due: past its press time and also past
    /// its hit (the mechanic was skipped) or past its use window. Phase-end reminders become
    /// <see cref="Due"/> at the transition instead (<see cref="Reminder.IsPhaseEnd"/>).
    /// </summary>
    Skipped,

    /// <summary>A note whose time has passed (notes are never missed).</summary>
    Passed,
}

public sealed class ReminderOptions
{
    /// <summary>Seconds before the press time a reminder appears.</summary>
    public double Lookahead { get; set; } = 15;

    /// <summary>Seconds before the press time a reminder becomes Due.</summary>
    public double DueLead { get; set; } = 1.5;

    /// <summary>Mitigation/shields stay Due until this many seconds before the hit.</summary>
    public double MitigationLatestBeforeHit { get; set; } = 0.3;

    /// <summary>
    /// Heals (and anything planned after the hit) stay Due this many seconds after the press time. Phase-end
    /// reminders made due by a phase transition stay Due max(this, 8 s) after it.
    /// </summary>
    public double HealLateWindow { get; set; } = 5;

    /// <summary>Done/Missed/Skipped reminders stay listed this long before disappearing.</summary>
    public double KeepFinishedFor { get; set; } = 3;

    public bool ShowSkipped { get; set; }
}

/// <summary>Game state the scheduler cannot know by itself (implemented by the plugin).</summary>
public interface ICooldownProvider
{
    /// <summary>Seconds until the action can be used (0 when ready), or null if unknown.</summary>
    double? SecondsUntilReady(uint actionId);

    /// <summary>False when a required job resource is missing (e.g. no Lily), or null if unknown.</summary>
    bool? HasResources(uint actionId);
}

/// <summary>One item of one plan entry, scheduled on the timeline.</summary>
public sealed class Reminder
{
    private IReadOnlyList<uint>? actionIds;
    private double latest;

    public required PlanEntry Entry { get; init; }

    /// <summary>The item within <see cref="Entry"/> (action, either-or group, or note).</summary>
    public required PlanItem Item { get; init; }

    public required int ItemIndex { get; init; }

    public required Mechanic Mechanic { get; init; }

    /// <summary>Anchor hit time (timeline seconds).</summary>
    public required double Hit { get; init; }

    /// <summary>Planned press time (timeline seconds).</summary>
    public required double PressAt { get; init; }

    /// <summary>Earliest use that still covers the hit.</summary>
    public required double Earliest { get; init; }

    /// <summary>Last useful moment; after it the reminder is missed.</summary>
    /// <remarks>
    /// Fixed when the scheduler is built, except for phase-end reminders (<see cref="IsPhaseEnd"/>): the phase
    /// transition that makes one due moves it to the transition plus a grace period (see
    /// <see cref="PhaseEndDueAt"/>); <see cref="ReminderScheduler.Reset"/>, or a rewind to before the reminder's due
    /// time, puts it back.
    /// </remarks>
    public required double Latest
    {
        get => this.latest;
        init => this.latest = value;
    }

    /// <summary>
    /// True for phase-end reminders (docs/DESIGN.md §5.2): the entry is flagged <see cref="PlanEntry.AtPhaseEnd"/>
    /// or its anchor mechanic is the phase's enrage (<see cref="MechanicKind.Enrage"/>). A phase push does not skip
    /// them: they become due at the transition, see <see cref="ReminderScheduler.OnTimelineJump"/>.
    /// </summary>
    public bool IsPhaseEnd => this.Entry.AtPhaseEnd || this.Mechanic.Kind == MechanicKind.Enrage;

    /// <summary>
    /// For a phase-end reminder that the phase transition made due: the timeline time the transition jumped to
    /// (<see cref="Latest"/> was moved to it plus the grace period). Null otherwise, and again after
    /// <see cref="ReminderScheduler.Reset"/> or a rewind to before the reminder's due time.
    /// </summary>
    public double? PhaseEndDueAt { get; internal set; }

    public ReminderState State { get; set; } = ReminderState.Pending;

    /// <summary>Timeline time of the state change into Done/Missed/Skipped/Passed.</summary>
    public double? FinishedAt { get; set; }

    public double? UsedAt { get; set; }

    public uint? UsedActionId { get; set; }

    /// <summary>Used before <see cref="Earliest"/> (effect may expire before the hit).</summary>
    public bool UsedEarly { get; set; }

    /// <summary>When the action will not be ready by <see cref="PressAt"/>: seconds it is late; else null.</summary>
    public double? ReadyIn { get; set; }

    /// <summary>True when a required job resource is missing.</summary>
    public bool MissingResources { get; set; }

    /// <summary>
    /// Action ids that satisfy this reminder, in plan order: the action, or every option of an
    /// either-or item. Empty for notes. Computed once from <see cref="Item"/>.
    /// </summary>
    public IReadOnlyList<uint> ActionIds => this.actionIds ??= this.Item.ActionIds().ToArray();

    /// <summary>True when the item names an action, so it can be done or missed; false for notes (never missed).</summary>
    public bool IsAction => this.ActionIds.Count > 0;

    public double SecondsUntilPress(double timelineTime) => this.PressAt - timelineTime;

    /// <summary>Moves <see cref="Latest"/> (the scheduler's phase-end rule).</summary>
    internal void SetLatest(double value) => this.latest = value;
}

public sealed record PullSummary(int Total, int Done, int Missed, int Skipped, IReadOnlyList<Reminder> MissedReminders);

/// <summary>An own action use seen by <see cref="ReminderScheduler.OnOwnAction"/>, kept for debugging.</summary>
/// <param name="ActionId">The action that was used.</param>
/// <param name="Time">Timeline time of the use.</param>
/// <param name="Matched">The reminder the use satisfied, or null when it was outside every reminder's window.</param>
public sealed record ActionUse(uint ActionId, double Time, Reminder? Matched);
