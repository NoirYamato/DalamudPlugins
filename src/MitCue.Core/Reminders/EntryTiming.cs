using MitCue.Core.Actions;

namespace MitCue.Core.Reminders;

/// <summary>
/// When one plan entry is pressed and when each of its items counts as used (docs/DESIGN.md §5.1, §5.2), from
/// <see cref="TimingRules.ForEntry"/>: the scheduler, the importer's estimates, <c>check</c> and the import review all use it.
/// </summary>
public sealed class EntryTiming
{
    /// <summary>The anchor hit (timeline seconds): the mechanic's hit at the entry's hit index.</summary>
    public required double Hit { get; init; }

    /// <summary>
    /// The automatic offset (press − hit): what the entry is pressed at when the plan gives it no offset, its first item's
    /// press (an item may be pressed later, <see cref="ItemTiming.PressAt"/>).
    /// </summary>
    public required double AutomaticOffset { get; init; }

    /// <summary>True when the plan gives the entry no offset, so <see cref="PressAt"/> is automatic.</summary>
    public required bool IsAutomatic { get; init; }

    /// <summary>The press time (timeline seconds): hit + the entry's offset, or hit + <see cref="AutomaticOffset"/>.</summary>
    public required double PressAt { get; init; }

    /// <summary>The offset of <see cref="PressAt"/> (press − hit).</summary>
    public double Offset => this.PressAt - this.Hit;

    /// <summary>
    /// The entry's press does not turn due before this (timeline seconds), whatever the due lead: a press for a later hit of
    /// a series comes after the hit before it (<see cref="Fights.Mechanic.Series"/>), the automatic press and a written one
    /// at or after that floor. Null otherwise (a written press before the floor has none). An item may have a later one
    /// (<see cref="ItemTiming.DueNotBefore"/>).
    /// </summary>
    public double? DueNotBefore { get; init; }

    /// <summary>One per item of the entry, at the item's index (a missing item is timed like a note).</summary>
    public required IReadOnlyList<ItemTiming> Items { get; init; }

    /// <summary>When the entry's press turns due, its "NOW" cue (<see cref="TimingRules.DueAt"/>); an item may turn due later.</summary>
    /// <param name="options">The reminder options (the due lead).</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    public double DueAt(ReminderOptions options) => TimingRules.DueAt(this.PressAt, this.DueNotBefore, options);
}

/// <summary>The timing of one item of an entry: its reminder's press time, NOW cue floor and use window.</summary>
/// <param name="Action">
/// The item's timing action: its action, or the first option of an either-or item, that the catalog knows; null for notes
/// and actions the catalog does not know.
/// </param>
/// <param name="PressAt">
/// The item's press time (timeline seconds): the entry's (<see cref="EntryTiming.PressAt"/>), or with an automatic press
/// later when the item waits for the series floor of its own action or for a later hit it reaches on its own, or comes
/// last (see <see cref="TimingRules.ForEntry"/>).
/// </param>
/// <param name="Earliest">The earliest use that still covers the hit (see <see cref="Reminder.Earliest"/>).</param>
/// <param name="Latest">The last useful moment (see <see cref="Reminder.Latest"/>).</param>
/// <param name="DueNotBefore">
/// The item's reminder does not turn due before this (timeline seconds), whatever the due lead (see
/// <see cref="Reminder.DueNotBefore"/>): the later of the item's series floor and, with an automatic press, the earliest
/// use that still reaches a later hit the item covers; for an item pressed last (Passage of Arms), no earlier than the
/// entry's other items' (up to its press); null when it has none.
/// </param>
public readonly record struct ItemTiming(ActionInfo? Action, double PressAt, double Earliest, double Latest, double? DueNotBefore)
{
    /// <summary>When the item's reminder turns due, its "NOW" cue (<see cref="TimingRules.DueAt"/>).</summary>
    /// <param name="options">The reminder options (the due lead).</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    public double DueAt(ReminderOptions options) => TimingRules.DueAt(this.PressAt, this.DueNotBefore, options);
}
