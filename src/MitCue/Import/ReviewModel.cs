using MitCue.Core;
using MitCue.Core.Actions;
using MitCue.Core.Fights;
using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;

namespace MitCue.Import;

/// <summary>Where the reviewed plan came from.</summary>
internal enum ReviewOrigin
{
    /// <summary>The sheet importer (link or pasted cells).</summary>
    Importer,

    /// <summary>Plan YAML pasted back from an AI chat (or written by hand).</summary>
    Yaml,
}

/// <summary>One plan entry in the review table.</summary>
internal sealed class ReviewRow
{
    /// <summary>Stable id for ImGui (entry ids can change).</summary>
    public required int Uid { get; init; }

    /// <summary>The entry (a live reference into the plan).</summary>
    public required PlanEntry Entry { get; init; }

    /// <summary>Diagnostics of the importer for this entry's source cell or row.</summary>
    public List<ReviewIssue> ImportIssues { get; } = [];

    /// <summary>Problems found by <see cref="PlanValidator"/>.</summary>
    public List<ReviewIssue> CheckIssues { get; set; } = [];

    /// <summary>The user changed something.</summary>
    public bool Edited { get; set; }

    /// <summary>Incremented whenever the entry or its issues change (display caches compare it).</summary>
    public int Version { get; set; }

    /// <summary>All issues, importer first.</summary>
    public IEnumerable<ReviewIssue> Issues => this.ImportIssues.Concat(this.CheckIssues);

    /// <summary>The most serious issue, or null.</summary>
    public DiagnosticSeverity? Worst
    {
        get
        {
            DiagnosticSeverity? worst = null;
            foreach (var issue in this.Issues)
            {
                if (worst == null || issue.Severity > worst)
                    worst = issue.Severity;
            }

            return worst;
        }
    }
}

/// <summary>
/// The editable state of the review step: the plan, one <see cref="ReviewRow"/> per entry with its diagnostics (importer
/// diagnostics matched to entries by source cell, then by source row; the rest are global), validation after every edit,
/// the phase calibration of the import, deleting with undo, and automatic timing for display. Main thread only.
/// </summary>
internal sealed class ReviewModel
{
    private readonly IActionCatalog actions;
    private readonly bool actionsLoaded;
    private readonly List<ReviewIssue> importGlobal = [];
    private readonly Stack<(int Index, ReviewRow Row)> deleted = new();
    private int nextUid;

    /// <summary>Builds the review of a plan.</summary>
    /// <param name="plan">The plan (edited in place).</param>
    /// <param name="fight">Its fight.</param>
    /// <param name="actions">Action catalog (for checks and automatic timing).</param>
    /// <param name="actionsLoaded">False when <paramref name="actions"/> is a stand-in.</param>
    /// <param name="origin">Importer or YAML.</param>
    /// <param name="diagnostics">Importer diagnostics (empty for YAML).</param>
    /// <param name="phaseZeros">Fitted phase zero points of the import.</param>
    /// <param name="phaseSpread">Fit spread per phase.</param>
    public ReviewModel(
        MitPlan plan,
        FightDefinition fight,
        IActionCatalog actions,
        bool actionsLoaded,
        ReviewOrigin origin,
        IReadOnlyList<ImportDiagnostic>? diagnostics = null,
        IReadOnlyDictionary<string, double>? phaseZeros = null,
        IReadOnlyDictionary<string, double>? phaseSpread = null)
    {
        PlanValidator.Repair(plan);
        this.Plan = plan;
        this.Fight = fight;
        this.actions = actions;
        this.actionsLoaded = actionsLoaded;
        this.Origin = origin;
        this.PhaseZeros = phaseZeros ?? new Dictionary<string, double>();
        this.PhaseSpread = phaseSpread ?? new Dictionary<string, double>();
        this.RenumberedIds = PlanValidator.NormalizeIds(plan);

        foreach (var entry in plan.Entries)
            this.Rows.Add(new ReviewRow { Uid = this.nextUid++, Entry = entry });

        this.AttachImportDiagnostics(diagnostics ?? []);
        this.RecheckAll();
    }

    /// <summary>The plan being reviewed.</summary>
    public MitPlan Plan { get; }

    /// <summary>The fight it is checked against.</summary>
    public FightDefinition Fight { get; }

    /// <summary>Importer or YAML.</summary>
    public ReviewOrigin Origin { get; }

    /// <summary>One row per entry, in plan order.</summary>
    public List<ReviewRow> Rows { get; } = [];

    /// <summary>Plan-level issues and importer diagnostics that belong to no entry.</summary>
    public List<ReviewIssue> GlobalIssues { get; } = [];

    /// <summary>Fitted phase zero points (timeline seconds of sheet 0:00).</summary>
    public IReadOnlyDictionary<string, double> PhaseZeros { get; }

    /// <summary>Largest residual of the fit per phase.</summary>
    public IReadOnlyDictionary<string, double> PhaseSpread { get; }

    /// <summary>Entry ids that were missing or duplicated and were renumbered.</summary>
    public int RenumberedIds { get; }

    /// <summary>Incremented on every change (the UI re-filters when it changes).</summary>
    public int Revision { get; private set; }

    /// <summary>Number of deletions that can be undone.</summary>
    public int UndoCount => this.deleted.Count;

    /// <summary>Rows whose worst issue has this severity.</summary>
    public int CountWorst(DiagnosticSeverity severity) => this.Rows.Count(r => r.Worst == severity);

    /// <summary>Sets the anchor mechanic (a hit index the new mechanic does not have is cleared).</summary>
    public void SetMechanic(ReviewRow row, string mechanicId)
    {
        var entry = row.Entry;
        if (string.Equals(entry.Mechanic, mechanicId, StringComparison.Ordinal))
            return;
        entry.Mechanic = mechanicId;
        if (entry.HitIndex is { } hit && this.Fight.GetMechanic(mechanicId) is { } mechanic && hit >= Math.Max(1, mechanic.Hits.Count))
            entry.HitIndex = null;
        row.ImportIssues.RemoveAll(i => i.Code.Contains("mechanic", StringComparison.OrdinalIgnoreCase));
        this.Changed(row);
    }

    /// <summary>Sets which hit of a multi-hit mechanic the offset is relative to (null = the first).</summary>
    public void SetHitIndex(ReviewRow row, int? hitIndex)
    {
        if (row.Entry.HitIndex == hitIndex)
            return;
        row.Entry.HitIndex = hitIndex is <= 0 ? null : hitIndex;
        this.Changed(row);
    }

    /// <summary>Sets the offset from the hit (null = automatic timing).</summary>
    public void SetOffset(ReviewRow row, double? offset)
    {
        if (offset is { } o)
            offset = Math.Round(Math.Clamp(o, -300, 300), 1);
        if (row.Entry.Offset == offset)
            return;
        row.Entry.Offset = offset;
        row.ImportIssues.RemoveAll(i =>
            i.Code.Contains("time", StringComparison.OrdinalIgnoreCase) || i.Code.Contains("offset", StringComparison.OrdinalIgnoreCase));
        this.Changed(row);
    }

    /// <summary>Marks the entry optional (never counted as missed).</summary>
    public void SetOptional(ReviewRow row, bool value)
    {
        if (row.Entry.Optional == value)
            return;
        row.Entry.Optional = value;
        this.Changed(row);
    }

    /// <summary>Marks the entry "press when ready".</summary>
    public void SetWhenReady(ReviewRow row, bool value)
    {
        if (row.Entry.WhenReady == value)
            return;
        row.Entry.WhenReady = value;
        this.Changed(row);
    }

    /// <summary>Sets the target text (empty = none).</summary>
    public void SetTarget(ReviewRow row, string? value)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value;
        if (row.Entry.Target == value)
            return;
        row.Entry.Target = value;
        this.Changed(row);
    }

    /// <summary>Sets the note text (empty = none).</summary>
    public void SetNote(ReviewRow row, string? value)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value;
        if (row.Entry.Note == value)
            return;
        row.Entry.Note = value;
        this.Changed(row);
    }

    /// <summary>Removes an entry (undoable).</summary>
    public void Delete(ReviewRow row)
    {
        var index = this.Rows.IndexOf(row);
        if (index < 0)
            return;
        this.Rows.RemoveAt(index);
        this.Plan.Entries.Remove(row.Entry);
        this.deleted.Push((index, row));
        this.Revision++;
    }

    /// <summary>Puts the last deleted entry back where it was. False when there is nothing to undo.</summary>
    public bool UndoDelete()
    {
        if (this.deleted.Count == 0)
            return false;
        var (index, row) = this.deleted.Pop();
        index = Math.Clamp(index, 0, this.Rows.Count);
        this.Rows.Insert(index, row);

        // Keep plan order equal to row order.
        var entryIndex = index < this.Rows.Count - 1 ? this.Plan.Entries.IndexOf(this.Rows[index + 1].Entry) : -1;
        if (entryIndex < 0)
            this.Plan.Entries.Add(row.Entry);
        else
            this.Plan.Entries.Insert(entryIndex, row.Entry);
        this.Recheck(row);
        this.Revision++;
        return true;
    }

    /// <summary>Re-validates one row.</summary>
    public void Recheck(ReviewRow row)
    {
        row.CheckIssues = PlanValidator.CheckEntry(row.Entry, this.Plan, this.Fight, this.actions, this.actionsLoaded);
        row.Version++;
    }

    /// <summary>Re-validates everything (and the plan-level issues).</summary>
    public void RecheckAll()
    {
        foreach (var row in this.Rows)
            this.Recheck(row);
        this.GlobalIssues.Clear();
        this.GlobalIssues.AddRange(this.importGlobal);
        this.GlobalIssues.AddRange(PlanValidator.CheckPlan(this.Plan, this.Fight, this.actionsLoaded));
        if (this.RenumberedIds > 0)
            this.GlobalIssues.Add(new ReviewIssue(DiagnosticSeverity.Info, "renumbered", $"{this.RenumberedIds} entries had a missing or repeated id and were renumbered."));
        this.Revision++;
    }

    /// <summary>The anchor mechanic of a row, or null when the id is unknown.</summary>
    public Mechanic? MechanicOf(ReviewRow row) =>
        string.IsNullOrWhiteSpace(row.Entry.Mechanic) ? null : this.Fight.GetMechanic(row.Entry.Mechanic);

    /// <summary>The hit time the entry is anchored to (timeline seconds), or null.</summary>
    public double? HitOf(ReviewRow row) => this.MechanicOf(row)?.HitAt(row.Entry.HitIndex ?? 0);

    /// <summary>The automatic offset (DESIGN §5.1) from the first action the catalog knows, as the scheduler computes it.</summary>
    public double AutoOffset(ReviewRow row)
    {
        ActionInfo? timing = null;
        foreach (var item in row.Entry.Do)
        {
            foreach (var id in item.ActionIds())
            {
                timing = this.actionsLoaded ? this.actions.Get(id) : null;
                if (timing != null)
                    break;
            }

            if (timing != null)
                break;
        }

        return TimingRules.DefaultOffset(timing, this.Fight.SyncLevel);
    }

    /// <summary>The planned press time (timeline seconds), or null when the mechanic is unknown.</summary>
    public double? PressTime(ReviewRow row) => this.HitOf(row) is { } hit ? hit + (row.Entry.Offset ?? this.AutoOffset(row)) : null;

    /// <summary>Jobs that have entries (by who.job or their column's job).</summary>
    public IReadOnlyList<Job> JobsInPlan()
    {
        var jobs = new SortedSet<Job>();
        foreach (var a in this.Plan.Assignees)
        {
            if (a.Job is { } j && j != Job.None)
                jobs.Add(j);
        }

        foreach (var e in this.Plan.Entries)
        {
            if (e.Who?.Job is { } j && j != Job.None)
                jobs.Add(j);
        }

        return jobs.ToList();
    }

    /// <summary>Entries that apply to a player (DESIGN §3.2 selection rule; no column chosen).</summary>
    public int CountFor(Job job, string? slot) =>
        job == Job.None ? 0 : this.Plan.Entries.Count(e => PlanSelection.Applies(e, new PlayerContext(job, this.Fight.SyncLevel, slot)));

    private void Changed(ReviewRow row)
    {
        row.Edited = true;
        this.Recheck(row);
        this.Revision++;
    }

    private void AttachImportDiagnostics(IReadOnlyList<ImportDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
            return;

        var bySrc = new Dictionary<string, List<ReviewRow>>(StringComparer.OrdinalIgnoreCase);
        var byRow = new Dictionary<(string Tab, int Row), List<ReviewRow>>();
        foreach (var row in this.Rows)
        {
            var src = row.Entry.Src;
            if (string.IsNullOrWhiteSpace(src))
                continue;
            Add(bySrc, src.Trim(), row);
            if (CellRef.TryParse(src) is { } cell)
                Add(byRow, (cell.Tab.ToUpperInvariant(), cell.Row), row);
        }

        foreach (var d in diagnostics)
        {
            if (d == null)
                continue;
            var issue = ReviewIssue.From(d);
            List<ReviewRow>? targets = null;
            if (!string.IsNullOrWhiteSpace(d.Src))
            {
                if (!bySrc.TryGetValue(d.Src.Trim(), out targets) && CellRef.TryParse(d.Src) is { } cell)
                    byRow.TryGetValue((cell.Tab.ToUpperInvariant(), cell.Row), out targets);
            }

            if (targets is { Count: > 0 })
            {
                foreach (var row in targets)
                    row.ImportIssues.Add(issue);
            }
            else
            {
                this.importGlobal.Add(issue);
            }
        }

        static void Add<TKey>(Dictionary<TKey, List<ReviewRow>> map, TKey key, ReviewRow row)
            where TKey : notnull
        {
            if (!map.TryGetValue(key, out var list))
                map[key] = list = [];
            list.Add(row);
        }
    }
}
