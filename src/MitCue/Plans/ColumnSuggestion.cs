using MitCue.Core;
using MitCue.Core.Plans;

namespace MitCue.Plans;

/// <summary>
/// The automatic "which column is me" choice for a plan and job, the one <see cref="PlanStore.MappingFor"/> uses when
/// nothing is saved, for every plan: bundled, imported or from a share code. It is the job's column
/// (<see cref="PlanStore.SuggestAssignee"/>), unless that column leaves out calls of the player's other columns; then it
/// is the narrowest choice that has them all. Example: LPDU writes a Scholar's P1/P2 calls in the job-less column "H2"
/// and the later phases' calls in "H2 SCH"; the job's column "H2 SCH" leaves out the job-less "H2" calls ("Everything"
/// on Pantokrator and Limitless Synergy), so the suggestion is "any column for SCH in slot H2".
/// </summary>
internal static class ColumnSuggestion
{
    /// <summary>
    /// The choice that shows every call of the player's columns when <paramref name="column"/> (the job's column)
    /// does not; null when it does, when the player's columns name different slots (no guess is made), or when no
    /// choice has them all. The result is the candidate with the fewest entries: "any column for my job in my slot",
    /// or one of the player's columns.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <param name="job">The player's job.</param>
    /// <param name="defaultSlot">The configured slot (<see cref="Configuration.DefaultSlot"/>), if any.</param>
    /// <param name="column">The job's column as a mapping (<see cref="PlanStore.ColumnMapping"/>).</param>
    public static PlayerMapping? Complete(MitPlan plan, Job job, string? defaultSlot, PlayerMapping column)
    {
        if (job == Job.None)
            return null;

        var columns = PlayerColumns(plan.Assignees, job, defaultSlot);
        var slot = PlayerSlot(columns, job, defaultSlot, out var consistent);
        var intended = Intended(plan, columns.Select(a => a.Key).ToList(), job, slot);
        if (intended.Count == 0 || !consistent)
            return null;

        var shown = Selected(plan, Context(column, job, defaultSlot));
        if (intended.All(shown.Contains))
            return null;

        // Candidates: any column for the job in the player's slot, then each of the player's columns.
        var candidates = new List<PlayerMapping> { new() { AssigneeKey = null, Slot = slot } };
        candidates.AddRange(columns.Select(a => new PlayerMapping { AssigneeKey = a.Key, Slot = slot }));
        PlayerMapping? best = null;
        var bestCount = int.MaxValue;
        foreach (var candidate in candidates)
        {
            var selection = Selected(plan, Context(candidate, job, defaultSlot));
            if (intended.All(selection.Contains) && selection.Count < bestCount)
            {
                best = candidate;
                bestCount = selection.Count;
            }
        }

        return best;
    }

    /// <summary>The player context the runtime builds from a column choice (SessionManager and PreviewDriver).</summary>
    public static PlayerContext Context(PlayerMapping mapping, Job job, string? defaultSlot, int level = 0) =>
        new(job, level, mapping.Slot ?? defaultSlot, mapping.AssigneeKey);

    /// <summary>The column key of an entry: its assignee, else "slot job".</summary>
    public static string ColumnKey(PlanEntry entry)
    {
        var who = entry.Who;
        if (who == null)
            return string.Empty;
        if (!string.IsNullOrWhiteSpace(who.Assignee))
            return who.Assignee.Trim();
        return string.Join(' ', new[] { who.Slot, who.Job?.ToString() }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    /// <summary>
    /// The player's columns: those of their job, plus job-less columns of their slot. Without a configured slot the slot
    /// is taken from a column of their job ("H2 SCH" → H2), so LPDU's "H2" (P1/P2) and "H2 SCH" (P3–P6) both match a Scholar.
    /// </summary>
    public static List<Assignee> PlayerColumns(IEnumerable<Assignee> assignees, Job job, string? slot)
    {
        var all = assignees.Where(a => a != null).ToList();
        var mine = new List<Assignee>();
        if (job != Job.None)
            mine.AddRange(all.Where(a => a.Job == job));

        var mySlot = Slots.TryNormalize(slot, out var s) ? s : null;
        if (mySlot == null)
        {
            foreach (var a in mine)
            {
                if (Slots.TryNormalize(a.Slot, out var inferred))
                {
                    mySlot = inferred;
                    break;
                }
            }
        }

        if (mySlot != null)
        {
            foreach (var a in all)
            {
                if ((a.Job == null || a.Job == Job.None) && Slots.TryNormalize(a.Slot, out var aSlot) && aSlot == mySlot && !mine.Contains(a))
                    mine.Add(a);
            }
        }

        return all.Where(mine.Contains).ToList();
    }

    /// <summary>
    /// The player's slot as <see cref="PlayerColumns"/> infers it: the configured one, else the slot of a column of
    /// their job. Not consistent when their columns name different slots (then no choice is guessed).
    /// </summary>
    public static string? PlayerSlot(List<Assignee> columns, Job job, string? defaultSlot, out bool consistent)
    {
        string? slot = Slots.TryNormalize(defaultSlot, out var configured) ? configured : null;
        if (slot == null)
        {
            foreach (var column in columns.Where(a => a.Job == job))
            {
                if (Slots.TryNormalize(column.Slot, out var inferred))
                {
                    slot = inferred;
                    break;
                }
            }
        }

        consistent = columns.All(a => !Slots.TryNormalize(a.Slot, out var s) || s == slot);
        return slot;
    }

    /// <summary>The entries of the player's columns that are meant for their job and slot.</summary>
    public static List<PlanEntry> Intended(MitPlan plan, List<string> columnKeys, Job job, string? slot, int level = 0)
    {
        if (columnKeys.Count == 0)
            return [];
        var keys = columnKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var me = new PlayerContext(job, level, slot);
        return plan.Entries.Where(e => e != null && keys.Contains(ColumnKey(e)) && PlanSelection.Applies(e, me)).ToList();
    }

    /// <summary>The entries a player context selects (PlanEntry has reference equality: the set holds these very entries).</summary>
    public static HashSet<PlanEntry> Selected(MitPlan plan, PlayerContext me) =>
        plan.Entries.Where(e => e != null && PlanSelection.Applies(e, me)).ToHashSet();
}
