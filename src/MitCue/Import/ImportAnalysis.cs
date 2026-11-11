using MitCue.Core;
using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Plans;

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

    /// <summary>
    /// The columns named in the tab's header row ("MT", "H2 SCH", "White Mage"), read from the cells without the
    /// importer: they choose the default tabs when the trial import is not available.
    /// </summary>
    public List<Assignee> HeaderColumns { get; } = [];
}

/// <summary>
/// The result of a trial import of every tab and column (before the user chooses): entries per tab and per column, and
/// the default choices — the tabs with the player's column, without tabs that repeat an already chosen tab (Carry the
/// Clear has a WHM tab, a SCH tab and a combined tab), and the player's own columns. Without a trial import (the
/// importer is not available) the columns named in each tab's header row choose the tabs.
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

    /// <summary>The column key of an entry: its assignee, else "slot job" (<see cref="ColumnSuggestion.ColumnKey"/>).</summary>
    public static string ColumnKey(PlanEntry entry) => ColumnSuggestion.ColumnKey(entry);

    /// <summary>
    /// The player's columns: those of their job, plus job-less columns of their slot. Without a configured slot the slot
    /// is taken from a column of their job ("H2 SCH" → H2), so LPDU's "H2" (P1/P2) and "H2 SCH" (P3–P6) both match a
    /// Scholar (<see cref="ColumnSuggestion.PlayerColumns"/>).
    /// </summary>
    public static List<Assignee> PlayerColumns(IEnumerable<Assignee> assignees, Job job, string? slot) =>
        ColumnSuggestion.PlayerColumns(assignees, job, slot);

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
    /// Without a trial import the tabs' header rows decide (<see cref="HeaderDefaultTabs"/>).
    /// </summary>
    /// <param name="job">The player's job.</param>
    /// <param name="slot">The player's slot, if configured.</param>
    /// <param name="linkedTab">The tab the link pointed at, if known.</param>
    public List<string> DefaultTabs(Job job, string? slot, string? linkedTab)
    {
        if (!this.Available)
            return this.HeaderDefaultTabs(job, slot, linkedTab, out _);

        var candidates = this.Tabs.Where(t => t.Entries > 0).ToList();
        if (candidates.Count == 0)
            return this.WithContent().Select(t => t.Name).ToList();

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

    /// <summary>
    /// The default tabs without a trial import, from the columns named in each tab's header row: the tabs with the
    /// player's columns, without a tab whose player columns a tab of only the player's columns already has (Carry the
    /// Clear's combined "WHM + SCH" tab repeats the "White Mage" and "Scholar" tabs); for an unknown player, the tab
    /// the link points at when it has a header row; else every tab with content.
    /// </summary>
    /// <param name="job">The player's job.</param>
    /// <param name="slot">The player's slot, if configured.</param>
    /// <param name="linkedTab">The tab the link pointed at, if known.</param>
    /// <param name="basis">Why these tabs (user-facing, e.g. "the tabs with your columns (H2, H2 SCH)"), or null for "every tab".</param>
    public List<string> HeaderDefaultTabs(Job job, string? slot, string? linkedTab, out string? basis)
    {
        basis = null;
        var withContent = this.WithContent();
        var all = withContent.Select(t => t.Name).ToList();
        var headed = withContent.Where(t => t.HeaderColumns.Count > 0).ToList();
        if (headed.Count == 0)
            return all;

        // The player's columns across the tabs (a slot is inferred from a column of their job, as in PlayerColumns).
        var columns = headed.SelectMany(t => t.HeaderColumns)
            .GroupBy(a => a.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        var mine = PlayerColumns(columns, job, slot).Select(a => a.Key).ToList();
        if (mine.Count > 0)
        {
            var keys = mine.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var candidates = headed.Where(t => t.HeaderColumns.Exists(a => keys.Contains(a.Key))).ToList();
            var dedicated = candidates.Where(t => t.HeaderColumns.TrueForAll(a => keys.Contains(a.Key))).ToList();
            if (dedicated.Count > 0)
            {
                var covered = dedicated.SelectMany(t => t.HeaderColumns).Select(a => a.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
                candidates = candidates.Where(t => dedicated.Contains(t) || t.HeaderColumns.Exists(a => keys.Contains(a.Key) && !covered.Contains(a.Key))).ToList();
            }

            basis = $"the tabs with your column{(mine.Count == 1 ? string.Empty : "s")} ({string.Join(", ", mine)})";
            return candidates.OrderBy(t => t.Index).Select(t => t.Name).ToList();
        }

        var linked = linkedTab == null ? null : headed.Find(t => string.Equals(t.Name, linkedTab, StringComparison.OrdinalIgnoreCase));
        if (linked != null && this.Tabs.Count > 1)
        {
            basis = "the tab your link points at";
            return [linked.Name];
        }

        return all;
    }

    /// <summary>
    /// A column header as a column: "H2 SCH" (slot and job), "White Mage" or "WAR" (job), "MT" or "D3" (slot); null for
    /// any other text ("Notes", "P3", "Mechanic").
    /// </summary>
    public static Assignee? HeaderColumn(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Words without letters are never columns: Jobs.TryParse also reads ClassJob ids, so "0:28" would be SCH (28).
        var key = text.Trim();
        if (key.Length > 24 || !key.Any(char.IsLetter))
            return null;
        if (Slots.TryNormalize(key, out var slot))
            return new Assignee { Key = key, Slot = slot };
        if (Jobs.TryParse(key, out var job) && job != Job.None)
            return new Assignee { Key = key, Job = job };

        var words = key.Split([' ', '/', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length != 2)
            return null;
        string? wordSlot = null;
        Job? wordJob = null;
        foreach (var word in words)
        {
            if (wordSlot == null && Slots.TryNormalize(word, out var s))
                wordSlot = s;
            else if (wordJob == null && word.Any(char.IsLetter) && Jobs.TryParse(word, out var j) && j != Job.None)
                wordJob = j;
            else
                return null;
        }

        return new Assignee { Key = key, Slot = wordSlot, Job = wordJob };
    }

    /// <summary>The columns of a row that are column headers (<see cref="HeaderColumn"/>).</summary>
    public static List<Assignee> HeaderColumnsOf(IReadOnlyList<SheetCell> row)
    {
        var result = new List<Assignee>();
        foreach (var cell in row)
        {
            if (HeaderColumn(cell.Text) is { } column && !result.Exists(a => string.Equals(a.Key, column.Key, StringComparison.OrdinalIgnoreCase)))
                result.Add(column);
        }

        return result;
    }

    /// <summary>The tab's header row: the first row with the most column headers, or -1 when no row has one.</summary>
    public static int HeaderRow(SheetTab tab)
    {
        var best = -1;
        var bestCount = 0;
        for (var r = 0; r < tab.Rows.Count; r++)
        {
            var count = HeaderColumnsOf(tab.Rows[r]).Count;
            if (count > bestCount)
            {
                best = r;
                bestCount = count;
            }
        }

        return best;
    }

    /// <summary>
    /// Rows (0-based) that look like the header row of a plan with several columns (at least two column headers), the
    /// first row of each run of such rows. Two or more mean that more than one tab was pasted into one box.
    /// </summary>
    public static List<int> PlanHeaderRows(SheetTab tab)
    {
        var rows = new List<int>();
        var previous = false;
        for (var r = 0; r < tab.Rows.Count; r++)
        {
            var header = HeaderColumnsOf(tab.Rows[r]).Count >= 2;
            if (header && !previous)
                rows.Add(r);
            previous = header;
        }

        return rows;
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

    private static List<TabStats> BaseStats(SheetDocument document)
    {
        var stats = new List<TabStats>(document.Tabs.Count);
        for (var i = 0; i < document.Tabs.Count; i++)
        {
            var tab = document.Tabs[i];
            var stat = new TabStats { Name = tab.Name, Index = i, Rows = tab.Rows.Count, NonEmptyRows = SheetText.NonEmptyRows(tab) };
            var header = HeaderRow(tab);
            if (header >= 0)
                stat.HeaderColumns.AddRange(HeaderColumnsOf(tab.Rows[header]));
            stats.Add(stat);
        }

        return stats;
    }

    private List<TabStats> WithContent()
    {
        var withContent = this.Tabs.Where(t => t.NonEmptyRows >= 2).ToList();
        return withContent.Count > 0 ? withContent : this.Tabs.ToList();
    }

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
