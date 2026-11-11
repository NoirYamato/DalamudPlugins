using MitCue.Core;
using MitCue.Core.Plans;
using MitCue.Plans;

namespace MitCue.Import;

/// <summary>The column choice an imported plan gets for the player's job, and what the overlay will then show.</summary>
/// <param name="Mapping">The column choice the overlay uses once the plan is saved: a kept choice, the one to save, or the automatic suggestion.</param>
/// <param name="Save">True when <paramref name="Mapping"/> is saved with the plan because it is not the job's own column (the job's column would leave out some of the player's calls).</param>
/// <param name="Kept">True when the plan being replaced already has a column choice for this job; it is kept.</param>
/// <param name="Mine">Entries the overlay will show the player.</param>
/// <param name="Missed">Entries of the player's columns that <paramref name="Mapping"/> leaves out (only a kept choice can).</param>
/// <param name="Columns">The player's columns (<see cref="ImportAnalysis.PlayerColumns"/>), by key.</param>
internal sealed record ColumnChoice(PlayerMapping Mapping, bool Save, bool Kept, int Mine, int Missed, IReadOnlyList<string> Columns);

/// <summary>
/// Works out which entries of an imported plan the overlay will show the player, exactly as the runtime selects them
/// (<see cref="PlanStore.MappingFor"/>, then a <see cref="PlayerContext"/> from that mapping, then
/// <see cref="PlanSelection"/>), and the column choice saved with the plan when the job's own column would miss some of
/// the player's calls. Example: LPDU writes a Scholar's P1/P2 calls in the job-less column "H2" and the later phases'
/// calls in "H2 SCH"; the job's column "H2 SCH" leaves out every "H2" call, so the import saves "any column for my job,
/// slot H2". That is also the automatic suggestion (<see cref="ColumnSuggestion"/>), which bundled plans and share
/// codes get; saving it keeps the choice the Save step showed.
/// </summary>
internal static class ImportColumns
{
    /// <summary>The player context the runtime builds from a column choice (SessionManager and PreviewDriver).</summary>
    public static PlayerContext Context(PlayerMapping mapping, Job job, int level, string? defaultSlot) =>
        ColumnSuggestion.Context(mapping, job, defaultSlot, level);

    /// <summary>Chooses the column mapping for the player's job.</summary>
    /// <param name="plan">The imported plan.</param>
    /// <param name="job">The player's job.</param>
    /// <param name="level">The fight's level.</param>
    /// <param name="defaultSlot">The configured slot (<see cref="Configuration.DefaultSlot"/>).</param>
    /// <param name="kept">The column choice of the plan this import replaces, for this job, if any (it is kept).</param>
    public static ColumnChoice Choose(MitPlan plan, Job job, int level, string? defaultSlot, PlayerMapping? kept = null)
    {
        var column = PlanStore.ColumnMapping(plan, job, defaultSlot);
        if (job == Job.None)
            return new ColumnChoice(kept ?? column, false, kept != null, 0, 0, []);

        var columns = ColumnSuggestion.PlayerColumns(plan.Assignees, job, defaultSlot);
        var keys = columns.Select(a => a.Key).ToList();
        var slot = ColumnSuggestion.PlayerSlot(columns, job, defaultSlot, out _);
        var intended = ColumnSuggestion.Intended(plan, keys, job, slot, level);

        if (kept != null)
        {
            var keptSelection = ColumnSuggestion.Selected(plan, Context(kept, job, level, defaultSlot));
            return new ColumnChoice(kept, false, true, keptSelection.Count, intended.Count(e => !keptSelection.Contains(e)), keys);
        }

        // The automatic suggestion. When it is not the job's own column it is saved with the plan.
        var complete = ColumnSuggestion.Complete(plan, job, defaultSlot, column);
        var mapping = complete != null ? new PlayerMapping { AssigneeKey = complete.AssigneeKey, Slot = complete.Slot } : column;
        var shown = ColumnSuggestion.Selected(plan, Context(mapping, job, level, defaultSlot));
        return new ColumnChoice(mapping, complete != null, false, shown.Count, intended.Count(e => !shown.Contains(e)), keys);
    }

    /// <summary>"any column for SCH in slot H2", "column \"H2 SCH\"".</summary>
    public static string Describe(PlayerMapping mapping, Job job)
    {
        if (mapping.AssigneeKey != null)
            return $"column \"{mapping.AssigneeKey}\"";
        return mapping.Slot != null ? $"any column for {job} in slot {mapping.Slot}" : $"any column for {job}";
    }
}
