using MitCue.Core;
using MitCue.Core.Plans;
using MitCue.Plans;

namespace MitCue.Import;

/// <summary>The column choice an imported plan gets for the player's job, and what the overlay will then show.</summary>
/// <param name="Mapping">The column choice the overlay uses once the plan is saved: a kept choice, the one to save, or the automatic suggestion.</param>
/// <param name="Save">True when <paramref name="Mapping"/> must be saved with the plan, because the automatic suggestion would leave out some of the player's calls.</param>
/// <param name="Kept">True when the plan being replaced already has a column choice for this job; it is kept.</param>
/// <param name="Mine">Entries the overlay will show the player.</param>
/// <param name="Missed">Entries of the player's columns that <paramref name="Mapping"/> leaves out (only a kept choice can).</param>
/// <param name="Columns">The player's columns (<see cref="ImportAnalysis.PlayerColumns"/>), by key.</param>
internal sealed record ColumnChoice(PlayerMapping Mapping, bool Save, bool Kept, int Mine, int Missed, IReadOnlyList<string> Columns);

/// <summary>
/// Works out which entries of an imported plan the overlay will show the player, exactly as the runtime selects them
/// (<see cref="PlanStore.MappingFor"/>, then a <see cref="PlayerContext"/> from that mapping, then
/// <see cref="PlanSelection"/>), and the column choice to save with the plan when the automatic suggestion would miss
/// some of the player's calls. Example: LPDU writes a Scholar's P1/P2 calls in the job-less column "H2" and the later
/// phases' calls in "H2 SCH"; the suggestion is the job's column "H2 SCH", which leaves out every "H2" call, so the
/// import saves "any column for my job, slot H2" instead.
/// </summary>
internal static class ImportColumns
{
    /// <summary>The player context the runtime builds from a column choice (SessionManager and PreviewDriver).</summary>
    public static PlayerContext Context(PlayerMapping mapping, Job job, int level, string? defaultSlot) =>
        new(job, level, mapping.Slot ?? defaultSlot, mapping.AssigneeKey);

    /// <summary>Chooses the column mapping for the player's job.</summary>
    /// <param name="plan">The imported plan.</param>
    /// <param name="job">The player's job.</param>
    /// <param name="level">The fight's level.</param>
    /// <param name="defaultSlot">The configured slot (<see cref="Configuration.DefaultSlot"/>).</param>
    /// <param name="kept">The column choice of the plan this import replaces, for this job, if any (it is kept).</param>
    public static ColumnChoice Choose(MitPlan plan, Job job, int level, string? defaultSlot, PlayerMapping? kept = null)
    {
        var suggestion = PlanStore.SuggestedMapping(plan, job, defaultSlot);
        if (job == Job.None)
            return new ColumnChoice(kept ?? suggestion, false, kept != null, 0, 0, []);

        var columns = ImportAnalysis.PlayerColumns(plan.Assignees, job, defaultSlot);
        var keys = columns.Select(a => a.Key).ToList();
        var slot = PlayerSlot(columns, job, defaultSlot, out var consistent);
        var intended = Intended(plan, keys, job, level, slot);

        if (kept != null)
        {
            var keptSelection = Selected(plan, Context(kept, job, level, defaultSlot));
            return new ColumnChoice(kept, false, true, keptSelection.Count, intended.Count(e => !keptSelection.Contains(e)), keys);
        }

        var suggested = Selected(plan, Context(suggestion, job, level, defaultSlot));
        if (intended.All(suggested.Contains) || !consistent)
            return new ColumnChoice(suggestion, false, false, suggested.Count, intended.Count(e => !suggested.Contains(e)), keys);

        // Candidates: any column for the job in the player's slot, then each of the player's columns.
        var candidates = new List<PlayerMapping> { new() { AssigneeKey = null, Slot = slot } };
        candidates.AddRange(keys.Select(k => new PlayerMapping { AssigneeKey = k, Slot = slot }));
        PlayerMapping? best = null;
        HashSet<PlanEntry>? bestSelection = null;
        foreach (var candidate in candidates)
        {
            var selection = Selected(plan, Context(candidate, job, level, defaultSlot));
            if (!intended.All(selection.Contains))
                continue;
            if (bestSelection == null || selection.Count < bestSelection.Count)
            {
                best = candidate;
                bestSelection = selection;
            }
        }

        return best != null
            ? new ColumnChoice(best, true, false, bestSelection!.Count, 0, keys)
            : new ColumnChoice(suggestion, false, false, suggested.Count, intended.Count(e => !suggested.Contains(e)), keys);
    }

    /// <summary>"any column for SCH in slot H2", "column \"H2 SCH\"".</summary>
    public static string Describe(PlayerMapping mapping, Job job)
    {
        if (mapping.AssigneeKey != null)
            return $"column \"{mapping.AssigneeKey}\"";
        return mapping.Slot != null ? $"any column for {job} in slot {mapping.Slot}" : $"any column for {job}";
    }

    /// <summary>
    /// The player's slot as <see cref="ImportAnalysis.PlayerColumns"/> infers it: the configured one, else the slot of a
    /// column of their job. Not consistent when their columns name different slots (then nothing is saved).
    /// </summary>
    private static string? PlayerSlot(List<Assignee> columns, Job job, string? defaultSlot, out bool consistent)
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
    private static List<PlanEntry> Intended(MitPlan plan, List<string> columnKeys, Job job, int level, string? slot)
    {
        if (columnKeys.Count == 0)
            return [];
        var keys = columnKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var me = new PlayerContext(job, level, slot);
        return plan.Entries.Where(e => e != null && keys.Contains(ImportAnalysis.ColumnKey(e)) && PlanSelection.Applies(e, me)).ToList();
    }

    // PlanEntry has reference equality: the set holds these very entries.
    private static HashSet<PlanEntry> Selected(MitPlan plan, PlayerContext me) =>
        plan.Entries.Where(e => e != null && PlanSelection.Applies(e, me)).ToHashSet();
}
