using System.Text;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;
using MitCue.Data;

namespace MitCue.Runtime;

/// <summary>Display strings of one reminder, computed once and cached (see <see cref="ReminderLabelCache"/>).</summary>
internal sealed class ReminderLabels
{
    /// <summary>Action name(s) in the client language, or the note text.</summary>
    public required string Name { get; init; }

    /// <summary>Game icon id (0 = none).</summary>
    public uint IconId { get; init; }

    /// <summary>Short mechanic name.</summary>
    public string Mechanic { get; init; } = string.Empty;

    /// <summary>"on MT · after the 1st stack", or null.</summary>
    public string? TargetAndNote { get; init; }

    /// <summary>Resource the action needs ("Lily"), for the "no …" warning.</summary>
    public string? Resource { get; init; }

    /// <summary>Column/slot/job tag for other assignees' reminders ("H2"), or null.</summary>
    public string? Tag { get; init; }

    /// <summary>True when the item is a note (nothing to press).</summary>
    public bool IsNote { get; init; }
}

/// <summary>Builds display strings for plan items and reminders.</summary>
internal static class ReminderText
{
    /// <summary>Name of a plan item: the action name (client language), names joined by " / " for either-or items, or the note.</summary>
    public static string ItemName(PlanItem item, GameData gameData)
    {
        if (item.Action is { } id)
            return gameData.ActionName(id) ?? item.Name ?? $"Action #{id}";
        if (item.Any is { Count: > 0 } options)
            return string.Join(" / ", options.Select(o => ItemName(o, gameData)));
        if (item.Note != null)
            return item.Note;
        return item.Name ?? "?";
    }

    /// <summary>Icon of a plan item (the first alternative with an icon for either-or items).</summary>
    public static uint ItemIcon(PlanItem item, GameData gameData)
    {
        if (item.Action is { } id)
            return gameData.ActionIcon(id);
        if (item.Any is { Count: > 0 } options)
        {
            foreach (var o in options)
            {
                var icon = ItemIcon(o, gameData);
                if (icon != 0)
                    return icon;
            }
        }

        return 0;
    }

    /// <summary>"on MT · after the 1st stack" from the entry's target and note.</summary>
    public static string? TargetAndNote(PlanEntry entry)
    {
        string? target = null;
        if (!string.IsNullOrWhiteSpace(entry.Target))
        {
            var t = entry.Target.Trim();
            target = t.StartsWith("on ", StringComparison.OrdinalIgnoreCase) ? t : "on " + t;
        }

        var note = string.IsNullOrWhiteSpace(entry.Note) ? null : entry.Note.Trim();
        return target != null && note != null ? target + " · " + note : target ?? note;
    }

    /// <summary>Tag naming another assignee's column.</summary>
    public static string? OtherTag(PlanEntry entry) =>
        entry.Who.Assignee ?? entry.Who.Slot ?? entry.Who.Job?.ToString();

    /// <summary>A one-line chat summary of a pull.</summary>
    public static string Summary(EncounterSession session, PullSummary summary, string reason, GameData gameData)
    {
        var sb = new StringBuilder();
        if (session.IsPreview)
            sb.Append("[Preview] ");
        sb.Append(session.Fight.Key.ToUpperInvariant()).Append(" pull ended (").Append(reason).Append("): ");
        sb.Append(summary.Done).Append(" done, ").Append(summary.Missed).Append(" missed");
        if (summary.Skipped > 0)
            sb.Append(", ").Append(summary.Skipped).Append(" skipped");
        sb.Append('.');

        if (summary.MissedReminders.Count > 0)
        {
            sb.Append(" Missed: ");
            var shown = Math.Min(5, summary.MissedReminders.Count);
            for (var i = 0; i < shown; i++)
            {
                var r = summary.MissedReminders[i];
                if (i > 0)
                    sb.Append(", ");
                sb.Append(ItemName(r.Item, gameData)).Append(" (").Append(r.Mechanic.Name).Append(')');
            }

            if (summary.MissedReminders.Count > shown)
                sb.Append(", +").Append(summary.MissedReminders.Count - shown).Append(" more");
            sb.Append('.');
        }

        return sb.ToString();
    }
}

/// <summary>Caches <see cref="ReminderLabels"/> per reminder (by reference) for the current session.</summary>
internal sealed class ReminderLabelCache
{
    private readonly Dictionary<Reminder, ReminderLabels> cache = [];
    private object? owner;

    /// <summary>Labels for a reminder of <paramref name="session"/>; the cache resets when the session changes.</summary>
    public ReminderLabels Get(object session, Reminder reminder, bool isOther, GameData gameData, Func<uint, string?> resourceOf)
    {
        if (!ReferenceEquals(session, this.owner))
        {
            this.cache.Clear();
            this.owner = session;
        }

        if (this.cache.TryGetValue(reminder, out var labels))
            return labels;

        var item = reminder.Item;
        string? resource = null;
        if (item.Action is { } id)
            resource = resourceOf(id);

        labels = new ReminderLabels
        {
            Name = ItemNameSafe(item, gameData),
            IconId = ReminderText.ItemIcon(item, gameData),
            Mechanic = reminder.Mechanic.Name,
            TargetAndNote = ReminderText.TargetAndNote(reminder.Entry),
            Resource = resource,
            Tag = isOther ? ReminderText.OtherTag(reminder.Entry) : null,
            IsNote = item.IsNote,
        };
        this.cache[reminder] = labels;
        return labels;
    }

    /// <summary>Forgets everything.</summary>
    public void Clear()
    {
        this.cache.Clear();
        this.owner = null;
    }

    private static string ItemNameSafe(PlanItem item, GameData gameData)
    {
        var name = ReminderText.ItemName(item, gameData);
        return string.IsNullOrWhiteSpace(name) ? "?" : name;
    }
}
