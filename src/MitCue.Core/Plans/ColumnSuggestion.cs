namespace MitCue.Core.Plans;

/// <summary>A column choice for a plan: the player's column and slot.</summary>
/// <param name="AssigneeKey">The column (assignee key) that is the player's, or null for "any column for my job".</param>
/// <param name="Slot">The player's slot with this choice, or null for none.</param>
public sealed record ColumnPick(string? AssigneeKey, string? Slot);

/// <summary>
/// The automatic "which column is me" choice for a plan and job: the one the plugin uses when the player has saved none
/// (bundled plans, share codes, imported plans), the one an import saves, and the one <c>mitcue plan/simulate/check</c>
/// use without <c>--assignee</c>. It is the job's column (<see cref="JobColumn"/>), unless that column leaves out the
/// job-less calls of the player's slot; then it is the narrowest choice that has them too and no call of another column.
/// Example: LPDU writes a Scholar's P1/P2 calls in the job-less column "H2" and the later phases' calls in "H2 SCH";
/// "H2 SCH" leaves out the "H2" calls ("Everything" on Pantokrator), so the suggestion is "any column for SCH in slot
/// H2". Several columns of the player's job are alternatives, not parts of one seat: LPDU has one per tank pair
/// ("WAR/DRK WAR", "WAR/GNB WAR", "WAR/PLD WAR") and a Warrior plays one of them, so a choice that would show calls of
/// two of them is never suggested (the job's column, the first of them, stays).
/// </summary>
public static class ColumnSuggestion
{
    /// <summary>
    /// The job's column: the column whose job is <paramref name="job"/> (preferring the one in <paramref name="slot"/>
    /// when several match), else the column of <paramref name="slot"/>; null when there is none.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <param name="job">The player's job.</param>
    /// <param name="slot">The player's configured slot (normalized, e.g. "H2"), if any.</param>
    public static Assignee? JobColumn(MitPlan plan, Job job, string? slot)
    {
        Assignee? byJob = null;
        Assignee? bySlot = null;
        foreach (var a in plan.Assignees)
        {
            if (a == null)
                continue;
            var slotMatches = slot != null && Slots.TryNormalize(a.Slot, out var s) && s == slot;
            if (a.Job == job && job != Job.None)
            {
                if (slotMatches)
                    return a;
                byJob ??= a;
            }
            else if (slotMatches)
            {
                bySlot ??= a;
            }
        }

        return byJob ?? bySlot;
    }

    /// <summary>The job's column as a choice: its key and slot (else <paramref name="defaultSlot"/>).</summary>
    public static ColumnPick ForJob(MitPlan plan, Job job, string? defaultSlot)
    {
        var column = JobColumn(plan, job, defaultSlot);
        return new ColumnPick(column?.Key, column?.Slot ?? defaultSlot);
    }

    /// <summary>The automatic choice: <see cref="Complete"/> when the job's column leaves out some of the player's calls, else the job's column.</summary>
    public static ColumnPick Suggest(MitPlan plan, Job job, string? defaultSlot)
    {
        var column = ForJob(plan, job, defaultSlot);
        return Complete(plan, job, defaultSlot, column) ?? column;
    }

    /// <summary>
    /// The choice that shows every call of the player's columns (<see cref="IntendedColumns"/>) when
    /// <paramref name="column"/> (the job's column) does not; null when it does, when the player's columns name different
    /// slots (no guess is made), or when every choice that has them all also shows calls of another column. The result
    /// is the candidate with the fewest entries: "any column for my job in my slot", or one of the player's columns.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <param name="job">The player's job.</param>
    /// <param name="defaultSlot">The configured slot, if any.</param>
    /// <param name="column">The job's column (<see cref="ForJob"/>).</param>
    public static ColumnPick? Complete(MitPlan plan, Job job, string? defaultSlot, ColumnPick column)
    {
        if (job == Job.None)
            return null;

        var columns = PlayerColumns(plan.Assignees, job, defaultSlot);
        var slot = PlayerSlot(columns, job, defaultSlot, out var consistent);
        if (!consistent)
            return null;

        var keys = IntendedColumns(columns, column.AssigneeKey);
        var intended = Intended(plan, keys, job, slot);
        if (intended.Count == 0)
            return null;

        var shown = Selected(plan, Context(column, job, defaultSlot));
        if (intended.TrueForAll(shown.Contains))
            return null;

        // Candidates: any column for the job in the player's slot, then each of the player's columns. A candidate may show
        // entries without a column (every choice shows them), but no entry of another column: that would be an alternative
        // seat of the player's (another tank pair) or another player's call.
        var mine = intended.ToHashSet();
        var candidates = new List<ColumnPick> { new(null, slot) };
        candidates.AddRange(keys.Select(k => new ColumnPick(k, slot)));
        ColumnPick? best = null;
        var bestCount = int.MaxValue;
        foreach (var candidate in candidates)
        {
            var selection = Selected(plan, Context(candidate, job, defaultSlot));
            if (selection.Count >= bestCount || !intended.TrueForAll(selection.Contains))
                continue;
            if (selection.Any(e => !mine.Contains(e) && !string.IsNullOrWhiteSpace(e.Who.Assignee)))
                continue;
            best = candidate;
            bestCount = selection.Count;
        }

        return best;
    }

    /// <summary>The player context a column choice gives (the plugin's SessionManager and preview, the CLI).</summary>
    public static PlayerContext Context(ColumnPick pick, Job job, string? defaultSlot, int level = 0) =>
        new(job, level, pick.Slot ?? defaultSlot, pick.AssigneeKey);

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
    /// is taken from a column of their job ("H2 SCH" → H2), so LPDU's "H2" (P1/P2) and "H2 SCH" (P3–P6) both match a
    /// Scholar. Every column of the job is listed, also those that are alternatives of each other (LPDU's tank pairs): an
    /// import keeps them all, and <see cref="IntendedColumns"/> tells which are the player's calls.
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

        consistent = columns.TrueForAll(a => !Slots.TryNormalize(a.Slot, out var s) || s == slot);
        return slot;
    }

    /// <summary>
    /// The columns whose calls are the player's, by key in column order: <paramref name="columnKey"/> (the job's column, or
    /// the column the player chose) when it is one of <paramref name="playerColumns"/>, and the job-less columns of the
    /// player's slot. The other columns of the player's job are alternatives of that one (another team composition).
    /// </summary>
    public static List<string> IntendedColumns(IReadOnlyList<Assignee> playerColumns, string? columnKey)
    {
        var chosen = columnKey == null ? null : playerColumns.FirstOrDefault(a => string.Equals(a.Key, columnKey, StringComparison.OrdinalIgnoreCase));
        return playerColumns.Where(a => ReferenceEquals(a, chosen) || a.Job == null || a.Job == Job.None).Select(a => a.Key).ToList();
    }

    /// <summary>The entries of the given columns that are meant for the player's job and slot.</summary>
    public static List<PlanEntry> Intended(MitPlan plan, IReadOnlyCollection<string> columnKeys, Job job, string? slot, int level = 0)
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
