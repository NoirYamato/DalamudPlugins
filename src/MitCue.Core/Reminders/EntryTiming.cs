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

    /// <summary>The automatic offset (press − hit): what the entry is pressed at when the plan gives it no offset.</summary>
    public required double AutomaticOffset { get; init; }

    /// <summary>True when the plan gives the entry no offset, so <see cref="PressAt"/> is automatic.</summary>
    public required bool IsAutomatic { get; init; }

    /// <summary>The press time (timeline seconds): hit + the entry's offset, or hit + <see cref="AutomaticOffset"/>.</summary>
    public required double PressAt { get; init; }

    /// <summary>The offset of <see cref="PressAt"/> (press − hit).</summary>
    public double Offset => this.PressAt - this.Hit;

    /// <summary>
    /// The reminder does not turn due before this (timeline seconds), whatever the due lead: an automatic press for a later
    /// hit of a series comes after the hit before it (<see cref="Fights.Mechanic.Series"/>). Null otherwise.
    /// </summary>
    public double? DueNotBefore { get; init; }

    /// <summary>One per item of the entry, at the item's index (an empty timing for a missing item).</summary>
    public required IReadOnlyList<ItemTiming> Items { get; init; }
}

/// <summary>The timing of one item of an entry.</summary>
/// <param name="Action">
/// The item's timing action: its action, or the first option of an either-or item, that the catalog knows; null for notes
/// and actions the catalog does not know.
/// </param>
/// <param name="Earliest">The earliest use that still covers the hit (see <see cref="Reminder.Earliest"/>).</param>
/// <param name="Latest">The last useful moment (see <see cref="Reminder.Latest"/>).</param>
public readonly record struct ItemTiming(ActionInfo? Action, double Earliest, double Latest);
