using MitCue.Core.Actions;
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

    /// <summary>The timeline jumped past it (phase push) before it was due.</summary>
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

    /// <summary>Heals stay Due this many seconds after the press time.</summary>
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
    public required double Latest { get; init; }

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

    public double SecondsUntilPress(double timelineTime) => this.PressAt - timelineTime;
}

public sealed record PullSummary(int Total, int Done, int Missed, int Skipped, IReadOnlyList<Reminder> MissedReminders);

/// <summary>Default timing rules (docs/DESIGN.md §5).</summary>
public static class TimingRules
{
    /// <summary>Default offset (press − hit) for an action used without an explicit time.</summary>
    public static double DefaultOffset(ActionInfo? action, int syncLevel) => throw new NotImplementedException();

    /// <summary>Earliest/latest useful use times for an item covering <paramref name="hit"/>.</summary>
    public static (double Earliest, double Latest) UseWindow(ActionInfo? action, int syncLevel, double hit, double pressAt, ReminderOptions options) =>
        throw new NotImplementedException();
}

/// <summary>
/// Turns the player's plan entries into reminders and advances their states from timeline time,
/// own action uses and cooldown information. Pure logic: no clock, no game access.
/// </summary>
public sealed class ReminderScheduler
{
    public ReminderScheduler(FightDefinition fight, IEnumerable<PlanEntry> entries, IActionCatalog actions, ReminderOptions options)
    {
        this.Fight = fight;
        this.Options = options;
        _ = entries;
        _ = actions;
    }

    public FightDefinition Fight { get; }

    public ReminderOptions Options { get; }

    /// <summary>All reminders of the pull, ordered by press time.</summary>
    public IReadOnlyList<Reminder> All => throw new NotImplementedException();

    /// <summary>Raised once when a reminder becomes Due (the plugin plays a sound).</summary>
    public event Action<Reminder>? BecameDue;

    /// <summary>Back to the start of a pull (all reminders Pending).</summary>
    public void Reset() => throw new NotImplementedException();

    /// <summary>The timeline jumped from <paramref name="fromTime"/> to <paramref name="toTime"/>; reminders in between that never became due are Skipped.</summary>
    public void OnTimelineJump(double fromTime, double toTime) => throw new NotImplementedException();

    /// <summary>The local player used an action at the given timeline time.</summary>
    public void OnOwnAction(uint actionId, double timelineTime) => throw new NotImplementedException();

    /// <summary>Advances states to <paramref name="timelineTime"/> and returns the reminders to display, ordered by press time.</summary>
    public IReadOnlyList<Reminder> Update(double timelineTime, ICooldownProvider? cooldowns = null) => throw new NotImplementedException();

    public PullSummary Summarize() => throw new NotImplementedException();

    private void RaiseBecameDue(Reminder r) => this.BecameDue?.Invoke(r);
}
