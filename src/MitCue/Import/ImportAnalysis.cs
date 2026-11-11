using MitCue.Core;
using MitCue.Core.Import;
using MitCue.Core.Plans;

namespace MitCue.Import;

/// <summary>What a trial import found in one tab.</summary>
internal sealed class TabStats
{
    /// <summary>Tab name.</summary>
    public required string Name { get; init; }

    /// <summary>Position in the document.</summary>
    public required int Index { get; init; }

    /// <summary>Rows in the grid.</summary>
    public required int Rows { get; init; }

    /// <summary>Rows with at least one non-empty cell.</summary>
    public required int NonEmptyRows { get; init; }

    /// <summary>Plan entries the trial import made from this tab (0 when unknown).</summary>
    public int Entries { get; set; }

    /// <summary>Column (assignee) keys with entries in this tab, in first-seen order.</summary>
    public List<string> Columns { get; } = [];

    /// <summary>"mechanic|job-or-slot-or-column" keys, used to spot tabs that repeat another tab.</summary>
    public HashSet<string> CallKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Entries per column key.</summary>
    public Dictionary<string, int> EntriesByColumn { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The result of a trial import of every tab and column (before the user chooses): entries per tab and per column, and
/// the default choices — the tabs with the player's column, without tabs that repeat an already chosen tab (Carry the
/// Clear has a WHM tab, a SCH tab and a combined tab), and the player's own columns.
/// </summary>
internal sealed class ImportAnalysis
{
    private ImportAnalysis(IReadOnlyList<TabStats> tabs, IReadOnlyList<Assignee> assignees, bool available, string? reason)
    {
        this.Tabs = tabs;
        this.Assignees = assignees;
        this.Available = available;
        this.Reason = reason;
    }

    /// <summary>Every tab of the document, in order.</summary>
    public IReadOnlyList<TabStats> Tabs { get; }

    /// <summary>Columns found by the trial import, in plan order.</summary>
    public IReadOnlyList<Assignee> Assignees { get; }

    /// <summary>True when the trial import ran.</summary>
    public bool Available { get; }

    /// <summary>Why the trial import did not run (user-facing), when <see cref="Available"/> is false.</summary>
    public string? Reason { get; }

    /// <summary>Statistics from a trial import result.</summary>
    public static ImportAnalysis FromResult(SheetDocument document, ImportResult result)
    {
        var tabs = BaseStats(document);
        var byName = tabs.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in result.Plan.Entries)
        {
            if (entry == null || CellRef.TryParse(entry.Src) is not { } src || !byName.TryGetValue(src.Tab, out var tab))
                continue;

            tab.Entries++;
            var column = ColumnKey(entry);
            if (column.Length > 0)
            {
                if (!tab.Columns.Contains(column, StringComparer.OrdinalIgnoreCase))
                    tab.Columns.Add(column);
                tab.EntriesByColumn[column] = tab.EntriesByColumn.GetValueOrDefault(column) + 1;
            }

            tab.CallKeys.Add(entry.Mechanic + "|" + CallOwner(entry));
        }

        var assignees = result.Plan.Assignees.Where(a => a != null && !string.IsNullOrWhiteSpace(a.Key)).ToList();

        // Columns that only appear on entries (no assignee record) still deserve a checkbox.
        foreach (var key in tabs.SelectMany(t => t.Columns))
        {
            if (!assignees.Exists(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase)))
                assignees.Add(new Assignee { Key = key });
        }

        return new ImportAnalysis(tabs, assignees, true, null);
    }

    /// <summary>Statistics without a trial import (the importer is not available).</summary>
    public static ImportAnalysis Unavailable(SheetDocument document, string reason) => new(BaseStats(document), [], false, reason);

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

    /// <summary>Columns found in the given tabs (all columns when the analysis has no per-tab data).</summary>
    public List<Assignee> ColumnsIn(IReadOnlyCollection<string> tabNames)
    {
        var keys = new HashSet<string>(
            this.Tabs.Where(t => tabNames.Contains(t.Name, StringComparer.OrdinalIgnoreCase)).SelectMany(t => t.Columns),
            StringComparer.OrdinalIgnoreCase);
        return this.Assignees.Where(a => keys.Contains(a.Key)).ToList();
    }

    /// <summary>Entries of a column in the given tabs.</summary>
    public int EntriesOf(string columnKey, IReadOnlyCollection<string> tabNames) =>
        this.Tabs.Where(t => tabNames.Contains(t.Name, StringComparer.OrdinalIgnoreCase)).Sum(t => t.EntriesByColumn.GetValueOrDefault(columnKey));

    /// <summary>
    /// Default tab choice: tabs with entries for the player's columns (or with any entries when the player has no
    /// column), the linked tab first, skipping a tab when at least half of its calls repeat calls of tabs already chosen.
    /// Without a trial import: every tab with content.
    /// </summary>
    /// <param name="job">The player's job.</param>
    /// <param name="slot">The player's slot, if configured.</param>
    /// <param name="linkedTab">The tab the link pointed at, if known.</param>
    public List<string> DefaultTabs(Job job, string? slot, string? linkedTab)
    {
        var withContent = this.Tabs.Where(t => t.NonEmptyRows >= 2).ToList();
        if (withContent.Count == 0)
            withContent = this.Tabs.ToList();
        if (!this.Available)
            return withContent.Select(t => t.Name).ToList();

        var candidates = this.Tabs.Where(t => t.Entries > 0).ToList();
        if (candidates.Count == 0)
            return withContent.Select(t => t.Name).ToList();

        var mine = PlayerColumns(this.Assignees, job, slot).Select(a => a.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int MineIn(TabStats t) => t.EntriesByColumn.Where(kv => mine.Contains(kv.Key)).Sum(kv => kv.Value);
        if (mine.Count > 0 && candidates.Any(t => MineIn(t) > 0))
            candidates = candidates.Where(t => MineIn(t) > 0).ToList();

        var ordered = candidates
            .OrderByDescending(t => linkedTab != null && string.Equals(t.Name, linkedTab, StringComparison.OrdinalIgnoreCase))
            .ThenBy(t => t.Index)
            .ToList();

        var chosen = new List<TabStats>();
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tab in ordered)
        {
            var keys = tab.CallKeys;
            if (keys.Count > 0 && chosen.Count > 0)
            {
                var repeated = keys.Count(covered.Contains);
                if (repeated * 2 >= keys.Count)
                    continue;
            }

            chosen.Add(tab);
            covered.UnionWith(keys);
        }

        return chosen.OrderBy(t => t.Index).Select(t => t.Name).ToList();
    }

    /// <summary>Default column choice within the chosen tabs: the player's columns, else every column.</summary>
    public List<string> DefaultColumns(IReadOnlyCollection<string> tabNames, Job job, string? slot)
    {
        var columns = this.ColumnsIn(tabNames);
        var mine = PlayerColumns(columns, job, slot);
        return (mine.Count > 0 ? mine : columns).Select(a => a.Key).ToList();
    }

    /// <summary>Tabs whose calls mostly repeat those of another (earlier) chosen tab, with that tab's name.</summary>
    public Dictionary<string, string> RepeatedTabs(IReadOnlyCollection<string> chosen)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tabs = this.Tabs.Where(t => chosen.Contains(t.Name, StringComparer.OrdinalIgnoreCase) && t.CallKeys.Count > 0).OrderBy(t => t.Index).ToList();
        for (var i = 1; i < tabs.Count; i++)
        {
            for (var j = 0; j < i; j++)
            {
                var overlap = tabs[i].CallKeys.Count(tabs[j].CallKeys.Contains);
                if (overlap * 2 >= tabs[i].CallKeys.Count)
                {
                    result[tabs[i].Name] = tabs[j].Name;
                    break;
                }
            }
        }

        return result;
    }

    private static List<TabStats> BaseStats(SheetDocument document) =>
        document.Tabs.Select((t, i) => new TabStats
        {
            Name = t.Name,
            Index = i,
            Rows = t.Rows.Count,
            NonEmptyRows = SheetText.NonEmptyRows(t),
        }).ToList();

    private static string CallOwner(PlanEntry entry)
    {
        var who = entry.Who;
        if (who == null)
            return string.Empty;
        if (who.Job is { } job && job != Job.None)
            return job.ToString();
        if (!string.IsNullOrWhiteSpace(who.Slot))
            return who.Slot.Trim();
        return who.Assignee?.Trim() ?? string.Empty;
    }
}
