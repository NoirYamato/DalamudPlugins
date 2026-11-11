using MitCue.Core;
using MitCue.Core.Plans;
using MitCue.Plans;

namespace MitCue.Import;

/// <summary>The column choice an imported plan gets for the player's job, and what the overlay will then show.</summary>
/// <param name="Mapping">The column choice the overlay uses once the plan is saved: a kept choice, the one to save, or the automatic suggestion.</param>
/// <param name="Save">True when <paramref name="Mapping"/> is saved with the plan because it is not the job's own column (the job's column would leave out some of the player's calls).</param>
/// <param name="Kept">True when the plan being replaced already has a column choice for this job; it is kept.</param>
/// <param name="Mine">Entries the overlay will show the player.</param>
/// <param name="Missed">Entries of the player's columns that <paramref name="Mapping"/> leaves out: a kept choice can, and so can the job's column when no other choice has every call without showing another column's (a job's general column next to several of its tank-pair columns).</param>
/// <param name="Columns">The player's columns (<see cref="ColumnSuggestion.IntendedColumns"/>), by key.</param>
/// <param name="MissedIn">The columns of the <paramref name="Missed"/> entries, by key.</param>
internal sealed record ColumnChoice(PlayerMapping Mapping, bool Save, bool Kept, int Mine, int Missed, IReadOnlyList<string> Columns, IReadOnlyList<string> MissedIn);

/// <summary>
/// Works out which entries of an imported plan the overlay will show the player, exactly as the runtime selects them
/// (<see cref="PlanStore.MappingFor"/>, then a <see cref="PlayerContext"/> from that mapping, then
/// <see cref="PlanSelection"/>), and the column choice saved with the plan when the job's own column would miss some of
/// the player's calls. Example: a sheet whose P1/P2 tab is headed "SCH" and whose P3 tab is headed "H2 SCH"; the job's
/// column "H2 SCH" leaves out the "SCH" calls, so the import saves "any column for my job, slot H2". That is also the
/// automatic suggestion (<see cref="ColumnSuggestion"/>), which bundled plans and share codes get; saving it keeps the
/// choice the Save step showed. (LPDU's job-less "H2" calls need no saved choice: the job's column "H2 SCH" shows the
/// job-less calls of its slot.)
/// </summary>
internal static class ImportColumns
{
    /// <summary>The player context the runtime builds from a column choice (SessionManager and PreviewDriver).</summary>
    public static PlayerContext Context(PlayerMapping mapping, Job job, int level, string? defaultSlot) =>
        ColumnSuggestion.Context(PlanStore.Pick(mapping), job, defaultSlot, level);

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
            return new ColumnChoice(kept ?? column, false, kept != null, 0, 0, [], []);

        // The player's calls: those of their column (the kept one when it is one of theirs, else the job's), of the
        // job-less columns of their slot and of the job's other columns that are not a team composition; the job's columns
        // for other team compositions are alternatives (another tank pair), not their calls.
        var columns = ColumnSuggestion.PlayerColumns(plan.Assignees, job, defaultSlot);
        var basis = kept?.AssigneeKey is { } keptKey && columns.Exists(a => string.Equals(a.Key, keptKey, StringComparison.OrdinalIgnoreCase)) ? keptKey : column.AssigneeKey;
        var keys = ColumnSuggestion.IntendedColumns(plan, columns, basis);
        var slot = ColumnSuggestion.PlayerSlot(columns, job, defaultSlot, out _);
        var intended = ColumnSuggestion.Intended(plan, keys, job, slot, level);

        if (kept != null)
            return Result(kept, save: false, isKept: true);

        // The automatic suggestion. When it is not the job's own column it is saved with the plan.
        var complete = ColumnSuggestion.Complete(plan, job, defaultSlot, PlanStore.Pick(column));
        return complete != null
            ? Result(new PlayerMapping { AssigneeKey = complete.AssigneeKey, Slot = complete.Slot }, save: true, isKept: false)
            : Result(column, save: false, isKept: false);

        ColumnChoice Result(PlayerMapping mapping, bool save, bool isKept)
        {
            var shown = ColumnSuggestion.Selected(plan, Context(mapping, job, level, defaultSlot));
            var missed = intended.Where(e => !shown.Contains(e)).ToList();
            var missedIn = missed.Select(ColumnSuggestion.ColumnKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return new ColumnChoice(mapping, save, isKept, shown.Count, missed.Count, keys, missedIn);
        }
    }

    /// <summary>"any column for SCH in slot H2", "column \"H2 SCH\"".</summary>
    public static string Describe(PlayerMapping mapping, Job job)
    {
        if (mapping.AssigneeKey != null)
            return $"column \"{mapping.AssigneeKey}\"";
        return mapping.Slot != null ? $"any column for {job} in slot {mapping.Slot}" : $"any column for {job}";
    }
}
