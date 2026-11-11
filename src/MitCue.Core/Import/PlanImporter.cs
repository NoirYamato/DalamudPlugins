using System.Globalization;
using System.Text.RegularExpressions;
using MitCue.Core.Actions;
using MitCue.Core.Fights;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;

namespace MitCue.Core.Import;

/// <summary>
/// Converts a sheet into a <see cref="MitPlan"/> (docs/DESIGN.md §6). The front half (<see cref="LayoutDetector"/>,
/// <see cref="CellParser"/>) finds the table and splits the cells; this class resolves actions (step 4) and mechanics
/// (step 5), calibrates phases (step 6), times every entry (step 7) and assembles the plan with diagnostics for review
/// (step 8).
/// </summary>
/// <remarks>
/// <para>
/// Actions: <see cref="ActionResolver"/> for each job the column can stand for. A column naming a job resolves for that
/// job; a slot-only column ("H2") or a role column ("CM LB Tank") resolves for every job of the role and gets one entry
/// per job when the jobs' actions differ ("Kera/Soil" → SGE Kerachole, SCH Sacred Soil; "Party Mit" → one per tank).
/// Note-only phrases and unresolvable text become note items with an <c>unresolved-token</c> diagnostic (info for
/// note-only phrases and free text, warning for words that look like an action).
/// </para>
/// <para>
/// Mechanics: <see cref="MechanicResolver"/> by alias within the row's phase; repeated labels are disambiguated by the
/// row time (with the phase zero point) or by row order (docs/research/mechanics.md §4.5).
/// </para>
/// <para>
/// Timing: an item with its own time gets a row-relative offset, <c>t_item − t_row + δ</c> through the row's nearest
/// time (<see cref="RowTiming"/>; δ from the fight's <c>row_offsets</c> for the sheet), so no phase zero point is needed;
/// without a usable row time the fitted (or preset) phase zero point is used. The hit index is the hit the press belongs
/// to: the first hit at or after it, or for heals the last one before it. Items without a time are automatic unless a
/// hint applies (<see cref="HintParser"/>).
/// </para>
/// </remarks>
public sealed partial class PlanImporter
{
    /// <summary>Seconds after the hit for "after 1st Hit" / "after &lt;mechanic&gt;" hints without a time.</summary>
    public const double AfterHitOffset = 0.5;

    /// <summary>Imports a document (see the class remarks).</summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public ImportResult Import(SheetDocument document, ImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Fight);
        ArgumentNullException.ThrowIfNull(options.Actions);
        ArgumentNullException.ThrowIfNull(options.Aliases);
        return new Run(document, options).Execute();
    }

    private sealed class TabState
    {
        public required TabLayout Layout { get; init; }

        /// <summary>Position among the imported tabs (sort key of the entries).</summary>
        public int Order { get; set; }

        /// <summary>Column index → assignee key in the plan.</summary>
        public Dictionary<int, string> Keys { get; } = [];

        public List<RowState> Rows { get; } = [];
    }

    private sealed class RowState
    {
        public required TabState Tab { get; init; }

        public required RowLayout Row { get; init; }

        public MechanicCandidates Candidates { get; set; } = MechanicCandidates.None;

        public Mechanic? Mechanic { get; set; }

        /// <summary>The mechanic could not be resolved; <see cref="Mechanic"/> is a stand-in.</summary>
        public bool Unknown { get; set; }

        public IReadOnlyList<RowRef> Refs { get; set; } = [];

        public List<string> Detail { get; } = [];

        public string? PhaseId => this.Row.PhaseId;
    }

    /// <summary>A token of an item with its resolution for every candidate job.</summary>
    private sealed class TokenState
    {
        public required ParsedToken Token { get; init; }

        public required Dictionary<Job, TokenResolution> ByJob { get; init; }

        /// <summary>The kind over all candidate jobs: Actions when any job resolves it.</summary>
        public required TokenKind Kind { get; init; }

        public HintTiming Timing { get; set; } = HintTiming.None;

        /// <summary>Hint text kept in the entry note (targets and "on CD" removed).</summary>
        public List<string> Notes { get; } = [];

        public string NoteText => NoteTextOf(this.Token);
    }

    /// <summary>An entry before sorting, with its sort keys.</summary>
    private sealed record Draft(PlanEntry Entry, double Press, int Tab, int Row, int Column, int Item, int Part);

    private sealed partial class Run
    {
        private static readonly HashSet<string> FreeTextWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "after", "before", "befor", "bevor", "at", "on", "if", "when", "during", "use", "used", "then", "for", "with",
            "until", "as", "pop", "sec", "secs", "seconds", "s", "debuff", "debuffs", "stack", "stacks", "protean", "proteans",
            "hit", "hits", "raidwide", "tower", "towers", "autos", "puddle", "puddles", "uptime", "skip", "the", "and", "or",
            "is", "are", "be", "to", "of", "in", "from", "can", "may", "should", "only",
        };

        private readonly SheetDocument document;
        private readonly ImportOptions options;
        private readonly FightDefinition fight;
        private readonly int level;
        private readonly MechanicResolver mechanics;
        private readonly ActionResolver resolver;
        private readonly List<ImportDiagnostic> diagnostics = [];
        private readonly List<Draft> drafts = [];
        private readonly Dictionary<string, double> zeros = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> spreads = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> actionKeys = new(StringComparer.Ordinal);
        private string? presetKey;
        private IReadOnlyDictionary<string, double> rowOffsets = new Dictionary<string, double>();
        private LayoutDetector detector = null!;

        public Run(SheetDocument document, ImportOptions options)
        {
            this.document = document;
            this.options = options;
            this.fight = options.Fight;
            this.level = options.Fight.SyncLevel;
            this.mechanics = new MechanicResolver(options.Fight);
            this.resolver = new ActionResolver(options.Actions, options.Aliases, this.level);
        }

        [GeneratedRegex(@"\bon\s*(?:cd|cooldown)\b|\bwhen\s*(?:ever)?\s*(?:it'?s\s+)?(?:ready|up|available|back)\b|\basap\b|\bas\s+soon\s+as\s+(?:possible|ready|it'?s\s+up)\b|\bwhenever\s+ready\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex WhenReadyWords();

        [GeneratedRegex(@"[.;!?\n]+")]
        private static partial Regex SentenceBreak();

        public ImportResult Execute()
        {
            this.detector = new LayoutDetector(this.fight);
            var layout = this.detector.Detect(this.document);
            var tabs = this.SelectTabs(layout);
            this.KeyColumns(tabs);
            this.ChoosePreset();

            foreach (var tab in tabs)
                this.CollectRows(tab);
            var rows = tabs.SelectMany(t => t.Rows).ToList();
            foreach (var row in rows)
                row.Candidates = this.mechanics.Candidates(row.Row.Label, row.PhaseId);

            // Phase zero points from rows whose label means one mechanic, then the order/time rule for the others,
            // then the final fit over every resolved row (docs/research/mechanics.md §4.2–4.5).
            this.Fit(rows.Where(r => r.Candidates.Mechanics.Count == 1 && r.Candidates.Match is not (MechanicMatch.Fuzzy or MechanicMatch.OtherPhase))
                .Select(r => (r, r.Candidates.Mechanics[0])));
            this.FillPresetZeros();
            foreach (var tab in tabs)
                this.ResolveMechanics(tab);
            this.Fit(rows.Where(r => r.Mechanic is not null && !r.Unknown && r.Row.Label is not null).Select(r => (r, r.Mechanic!)));
            foreach (var row in rows.Where(r => r.Mechanic is not null && !r.Unknown))
                row.Refs = RowTiming.Align(row.Row, row.Mechanic!, this.RowOffset(row.Mechanic!));

            foreach (var row in rows)
            {
                foreach (var cell in row.Row.Assignments)
                    this.ImportCell(row, cell);
            }

            var plan = this.BuildPlan(tabs);
            return new ImportResult
            {
                Plan = plan,
                Diagnostics = this.diagnostics.ToList(),
                PhaseZeros = new Dictionary<string, double>(this.zeros, StringComparer.OrdinalIgnoreCase),
                PhaseSpread = new Dictionary<string, double>(this.spreads, StringComparer.OrdinalIgnoreCase),
            };
        }

        // ------------------------------------------------------------------ tabs and columns

        private List<TabState> SelectTabs(DocumentLayout layout)
        {
            var considered = layout.Tabs.ToList();
            if (this.options.Tabs is { } wanted)
            {
                considered = [];
                foreach (var name in wanted)
                {
                    var tab = layout.Tabs.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                        ?? layout.Tabs.FirstOrDefault(t => string.Equals(Compact(t.Name), Compact(name), StringComparison.OrdinalIgnoreCase));
                    if (tab is null)
                        this.Diag(DiagnosticSeverity.Error, "unknown-tab", $"The document has no tab '{name}' (tabs: {string.Join(", ", layout.Tabs.Select(t => "'" + t.Name + "'"))}).", null);
                    else if (!considered.Contains(tab))
                        considered.Add(tab);
                }

                considered = considered.OrderBy(t => layout.Tabs.ToList().IndexOf(t)).ToList();
            }

            foreach (var tab in considered)
                this.diagnostics.AddRange(tab.Diagnostics);

            var result = new List<TabState>();
            foreach (var tab in considered.Where(t => t.Kind == TabKind.Plan))
                result.Add(new TabState { Layout = tab, Order = result.Count });
            if (result.Count == 0)
                this.Diag(DiagnosticSeverity.Error, "no-plan-tab", "No tab with a plan table (a header row with player columns) was found.", null);
            return result;

            static string Compact(string s) => new(s.Where(char.IsLetterOrDigit).ToArray());
        }

        /// <summary>
        /// Assignee keys. A tab named after a team composition whose columns are those jobs ("WAR/DRK" with "WAR" and "DRK")
        /// is one variant among several: when its column names also head columns of other tabs, its columns are keyed
        /// "&lt;tab&gt; &lt;column&gt;" ("WAR/DRK WAR") so that the variants stay apart. A composition tab with other
        /// columns cannot be keyed cleanly and is skipped.
        /// </summary>
        private void KeyColumns(List<TabState> tabs)
        {
            var keysByTab = tabs.ToDictionary(t => t, t => t.Layout.AssigneeColumns.Select(c => c.Assignee!.Key).ToHashSet(StringComparer.OrdinalIgnoreCase));
            foreach (var tab in tabs.ToList())
            {
                var layout = tab.Layout;
                var collides = tabs.Where(o => o != tab).Any(o => keysByTab[o].Overlaps(keysByTab[tab]));
                var composition = layout.NameJobs.Count >= 2;
                var clean = composition && layout.AssigneeColumns.All(c => c.Assignee!.Kind == AssigneeKind.Job && c.Assignee.Job is { } j && layout.NameJobs.Contains(j));
                if (composition && collides && !clean)
                {
                    this.Diag(DiagnosticSeverity.Warning, "tab-skipped", $"Tab '{layout.Name}' names a team composition, but its columns ({string.Join(", ", keysByTab[tab])}) are not one per job of it, so they cannot be told apart from the same columns of other tabs; the tab is not imported.", layout.Tab.Ref(layout.HeaderRow, 0));
                    tabs.Remove(tab);
                    continue;
                }

                var rekey = clean && collides;
                foreach (var column in layout.AssigneeColumns)
                    tab.Keys[column.Index] = rekey ? layout.Name + " " + column.Assignee!.Key : column.Assignee!.Key;
                if (rekey)
                    this.Diag(DiagnosticSeverity.Info, "assignee-rekeyed", $"Tab '{layout.Name}' is one composition variant; its columns are keyed {string.Join(", ", tab.Keys.Values.Select(k => "'" + k + "'"))}.", layout.Tab.Ref(layout.HeaderRow, 0));
            }

            for (var i = 0; i < tabs.Count; i++)
                tabs[i].Order = i;
        }

        private bool AssigneeSelected(string key, string header) =>
            this.options.Assignees is not { } wanted
            || wanted.Any(w => string.Equals(w, key, StringComparison.OrdinalIgnoreCase) || string.Equals(w, header, StringComparison.OrdinalIgnoreCase));

        /// <summary>The fight's calibration preset for the sheet: the option, else a preset whose words all occur in the document title.</summary>
        private void ChoosePreset()
        {
            var key = this.options.CalibrationKey;
            if (key is null)
            {
                var title = ImportText.NormalizedWords(this.document.Title).ToHashSet(StringComparer.Ordinal);
                key = this.fight.Calibration.Keys.Concat(this.fight.RowOffsets.Keys)
                    .FirstOrDefault(k => ImportText.NormalizedWords(k.Replace('_', ' ')) is { Length: > 0 } words && words.All(title.Contains));
                if (key is not null)
                    this.Diag(DiagnosticSeverity.Info, "calibration-preset", $"Using the '{key}' calibration of {this.fight.Name} (matched by the document title '{this.document.Title}').", null);
            }
            else if (!this.fight.Calibration.ContainsKey(key) && !this.fight.RowOffsets.ContainsKey(key))
            {
                this.Diag(DiagnosticSeverity.Warning, "calibration-preset", $"{this.fight.Name} has no calibration preset '{key}' (presets: {string.Join(", ", this.fight.Calibration.Keys)}).", null);
            }

            this.presetKey = key;
            if (key is not null && this.fight.RowOffsets.TryGetValue(key, out var offsets))
                this.rowOffsets = offsets;
        }

        private double RowOffset(Mechanic m) => this.rowOffsets.TryGetValue(m.Id, out var d) ? d : 0;

        // ------------------------------------------------------------------ rows and mechanics

        private void CollectRows(TabState tab)
        {
            var layout = tab.Layout;
            var notesColumns = layout.NotesColumns.Select(c => c.Index).ToHashSet();
            RowState? lastData = null;
            foreach (var row in layout.Rows.Where(r => r.Index > layout.HeaderRowEnd))
            {
                switch (row.Kind)
                {
                    case RowKind.Data:
                        lastData = new RowState { Tab = tab, Row = row };
                        if (row.Notes is { } notes)
                            lastData.Detail.Add(notes.Trim());
                        tab.Rows.Add(lastData);
                        break;
                    case RowKind.Note or RowKind.Footnote:
                        if (lastData is not null && row.Texts.Count > 0 && row.Texts.All(t => notesColumns.Contains(t.Column)))
                        {
                            // A continuation line of the notes column belongs to the row above.
                            lastData.Detail.Add(string.Join("\n", row.Texts.Select(t => t.Text.Trim())));
                        }
                        else
                        {
                            foreach (var t in row.Texts.Where(t => !string.IsNullOrWhiteSpace(t.Text)))
                                this.Diag(DiagnosticSeverity.Info, "note-row", $"Row {row.Index + 1} is a note, not an assignment: \"{t.Text.Trim()}\" (shown for review only).", t.Src);
                        }

                        break;
                    case RowKind.Section or RowKind.PhaseLabel or RowKind.Header:
                        lastData = null;
                        break;
                }
            }
        }

        /// <summary>The resolution rule of docs/research/mechanics.md §4.5, row by row in tab order.</summary>
        private void ResolveMechanics(TabState tab)
        {
            string? phase = null;
            var prev = double.NegativeInfinity;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            RowState? last = null;
            var first = true;
            foreach (var row in tab.Rows)
            {
                if (first || !string.Equals(row.PhaseId, phase, StringComparison.OrdinalIgnoreCase))
                {
                    first = false;
                    phase = row.PhaseId;
                    prev = phase is not null && this.fight.GetPhase(phase) is { } p ? p.Start : double.NegativeInfinity;
                    used.Clear();
                    last = null;
                }

                if (row.Row.Label is null)
                {
                    this.ResolveUnlabelled(row, last);
                    continue;
                }

                var candidates = row.Candidates.Mechanics;
                if (candidates.Count == 0)
                {
                    var fallback = last?.Mechanic ?? (phase is not null ? this.mechanics.PhaseStart(phase) : null);
                    this.Diag(DiagnosticSeverity.Warning, "unknown-mechanic",
                        $"No {this.fight.Name} mechanic{(phase is null ? string.Empty : " of " + phase.ToUpperInvariant())} is called '{row.Row.Label}'; its entries are anchored to {(fallback is null ? "nothing and dropped" : fallback.Id)}. Add the label as an alias or fix the row in review.",
                        row.Row.Src);
                    row.Mechanic = fallback;
                    row.Unknown = true;
                    continue;
                }

                Mechanic chosen;
                if (candidates.Count == 1)
                {
                    chosen = candidates[0];
                }
                else
                {
                    var zero = phase is not null && this.zeros.TryGetValue(phase, out var z) ? z : (double?)null;
                    string rule;
                    if (row.Row.Time is { } time && zero is { } known)
                    {
                        var at = known + time.Seconds;
                        chosen = candidates.OrderBy(m => Math.Abs(m.Hit + this.RowOffset(m) - at)).First();
                        rule = $"its time {ParsedTime.Format(time.Seconds)} (timeline {ImportText.Seconds(at)})";
                    }
                    else
                    {
                        chosen = candidates.FirstOrDefault(m => m.Hit >= prev - 0.05 && !used.Contains(m.Id))
                            ?? candidates.FirstOrDefault(m => !used.Contains(m.Id))
                            ?? candidates[0];
                        rule = "row order";
                    }

                    var severity = used.Contains(chosen.Id) ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info;
                    this.Diag(severity, "ambiguous-mechanic",
                        $"'{row.Row.Label}' can mean {string.Join(", ", candidates.Select(m => m.Id))}; {chosen.Id} was chosen by {rule}.",
                        row.Row.Src);
                }

                switch (row.Candidates.Match)
                {
                    case MechanicMatch.Fuzzy:
                        this.Diag(DiagnosticSeverity.Info, "fuzzy-mechanic", $"'{row.Row.Label}' was read as {chosen.Id} ({chosen.Name}).", row.Row.Src);
                        break;
                    case MechanicMatch.OtherPhase:
                        this.Diag(DiagnosticSeverity.Warning, "mechanic-phase", $"'{row.Row.Label}' is {chosen.Id}, a mechanic of {chosen.Phase.ToUpperInvariant()}, but the row is in {(phase ?? "no phase").ToUpperInvariant()}.", row.Row.Src);
                        break;
                }

                row.Mechanic = chosen;
                prev = chosen.Hit;
                used.Add(chosen.Id);
                last = row;
            }
        }

        /// <summary>A row with assignments but no label: by its time when it has one, else it belongs to the row above.</summary>
        private void ResolveUnlabelled(RowState row, RowState? last)
        {
            var phase = row.PhaseId;
            if (row.Row.Time is { } time && phase is not null && this.zeros.TryGetValue(phase, out var zero))
            {
                var at = zero + time.Seconds;
                var nearest = this.fight.Mechanics.Where(m => string.Equals(m.Phase, phase, StringComparison.OrdinalIgnoreCase) && m.Kind != MechanicKind.Marker)
                    .OrderBy(m => Math.Abs(m.Hit - at)).FirstOrDefault();
                if (nearest is not null && Math.Abs(nearest.Hit - at) <= 3)
                {
                    row.Mechanic = nearest;
                    return;
                }
            }

            row.Mechanic = last?.Mechanic ?? (phase is not null ? this.mechanics.PhaseStart(phase) : null);

            // A borrowed mechanic: the row's own time (if any) does not mark its hits, so its items are timed through the
            // phase zero point instead of row-relative.
            row.Unknown = last?.Mechanic is null || row.Row.Time is not null;
        }

        /// <summary>Phase zero points: the median of <c>timeline − sheet time</c> over the matched times of the rows.</summary>
        private void Fit(IEnumerable<(RowState Row, Mechanic Mechanic)> rows)
        {
            var points = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (row, m) in rows)
            {
                if (row.PhaseId is not { } phase || row.Row.Time is null || m.Kind is MechanicKind.Enrage or MechanicKind.Other or MechanicKind.Marker)
                    continue;
                if (!string.Equals(m.Phase, phase, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!points.TryGetValue(phase, out var list))
                    points[phase] = list = [];
                list.AddRange(RowTiming.Align(row.Row, m, this.RowOffset(m)).Select(r => r.Timeline - r.SheetTime));
            }

            foreach (var (phase, list) in points)
            {
                if (list.Count == 0)
                    continue;
                var zero = RowTiming.Median(list);
                this.zeros[phase] = ImportText.Round(zero);
                this.spreads[phase] = ImportText.Round(list.Max(p => Math.Abs(p - zero)));
            }
        }

        private void FillPresetZeros()
        {
            if (this.presetKey is null || !this.fight.Calibration.TryGetValue(this.presetKey, out var preset))
                return;
            foreach (var (phase, zero) in preset)
            {
                if (!this.zeros.ContainsKey(phase))
                    this.zeros[phase] = zero;
            }
        }

        // ------------------------------------------------------------------ cells and items

        private void ImportCell(RowState row, AssigneeCell cell)
        {
            var tab = row.Tab;
            var key = tab.Keys.TryGetValue(cell.Column.Index, out var k) ? k : cell.Assignee.Key;
            if (!this.AssigneeSelected(key, cell.Assignee.Key))
                return;
            if (row.Mechanic is null)
            {
                this.Diag(DiagnosticSeverity.Warning, "dropped-cell", $"'{Flat(cell.Cell.Text)}' has no mechanic to anchor to and was not imported.", cell.Src);
                return;
            }

            var parsed = CellParser.Default.Parse(cell.Cell, cell.Src);
            foreach (var item in parsed.Items)
                this.ImportItem(row, cell, key, item);
        }

        private void ImportItem(RowState row, AssigneeCell cell, string key, ParsedItem item)
        {
            var header = cell.Assignee;
            var candidates = CandidateJobs(header);
            var tokens = new List<TokenState>();
            foreach (var token in item.Tokens)
            {
                var byJob = candidates.ToDictionary(j => j, j => this.resolver.Resolve(token, j));
                var kind = byJob.Values.Any(r => r.Kind == TokenKind.Actions) ? TokenKind.Actions
                    : byJob.Values.Any(r => r.Kind == TokenKind.NoteOnly) ? TokenKind.NoteOnly
                    : byJob.Values.Any(r => r.Kind == TokenKind.NotAvailable) ? TokenKind.NotAvailable
                    : byJob.Values.Any(r => r.Kind == TokenKind.OtherJob) ? TokenKind.OtherJob
                    : byJob.Values.All(r => r.Kind == TokenKind.Empty) ? TokenKind.Empty
                    : TokenKind.Unresolved;
                var state = new TokenState { Token = token, ByJob = byJob, Kind = kind };
                this.Describe(state, row, cell, header, candidates);
                tokens.Add(state);
            }

            if (tokens.Count == 0 || tokens.All(t => t.Kind == TokenKind.Empty))
                return;

            // Tokens with their own timing hint ("Kera after 1st Hit" next to "Eprog + Panhaima") become their own entry;
            // the steps of a sequence stay together.
            var groups = item.IsSequence
                ? [tokens]
                : tokens.GroupBy(t => t.Timing.Key).Select(g => g.ToList()).ToList();
            var times = ItemTimes(item);
            for (var g = 0; g < groups.Count; g++)
                this.ImportGroup(row, cell, key, item, groups[g], g, times, candidates);
        }

        /// <summary>Reports a token's resolution once and reads its hints.</summary>
        private void Describe(TokenState t, RowState row, AssigneeCell cell, AssigneeHeader header, IReadOnlyList<Job> candidates)
        {
            var token = t.Token;
            var who = header.Job is { } j ? j.ToString() : header.Jobs.Count > 0 ? string.Join("/", header.Jobs) : $"the {header.Key} column";
            switch (t.Kind)
            {
                case TokenKind.Actions:
                {
                    var resolved = t.ByJob.Values.Where(r => r.Kind == TokenKind.Actions).ToList();
                    var rest = resolved.Select(r => r.Rest).FirstOrDefault(r => r is not null);
                    t.Timing = HintParser.Parse(rest, token.Hints, this.mechanics, row.PhaseId, row.Mechanic, this.detector.PhaseId);
                    t.Notes.AddRange(this.HintNotes(rest, token, t.Timing));
                    foreach (var fuzzy in resolved.SelectMany(r => r.Alternatives).Where(a => a.Fuzzy).DistinctBy(a => a.Matched))
                        this.Diag(DiagnosticSeverity.Info, "fuzzy-match", $"'{fuzzy.FuzzyText}' was read as '{fuzzy.Matched}' ({string.Join(", ", fuzzy.Actions.Select(a => a.Name))}).", cell.Src);
                    var missing = resolved.SelectMany(r => r.NotAvailable).Distinct().ToList();
                    if (missing.Count > 0)
                        this.Diag(DiagnosticSeverity.Warning, "not-available-at-level", $"'{token.Phrase}': {string.Join(", ", missing)} is not available at level {this.level}; left out.", cell.Src);
                    if (t.Timing.Reanchored && t.Timing.Anchor is { } anchor)
                        this.Diag(DiagnosticSeverity.Info, "reanchored", $"'{token.Phrase}' names {anchor.Id} ({anchor.Name}); anchored there instead of the row's {row.Mechanic?.Id}.", cell.Src);
                    break;
                }

                case TokenKind.NoteOnly:
                    this.Diag(DiagnosticSeverity.Info, "unresolved-token", $"'{t.NoteText}' is a note-only phrase; kept as a note.", cell.Src);
                    break;
                case TokenKind.NotAvailable:
                {
                    var missing = t.ByJob.Values.SelectMany(r => r.NotAvailable).Distinct().ToList();
                    this.Diag(DiagnosticSeverity.Warning, "not-available-at-level", $"'{t.NoteText}' names {string.Join(", ", missing)}, which {who} does not have at level {this.level}; kept as a note.", cell.Src);
                    break;
                }

                case TokenKind.OtherJob:
                {
                    var owner = t.ByJob.Values.Select(r => r.OtherJob).FirstOrDefault(o => o is not null);
                    this.Diag(DiagnosticSeverity.Info, "other-job-action", $"'{t.NoteText}' is {(owner is null ? "another job's" : "a " + owner + "")} action, not {who}'s; kept as a note.", cell.Src);
                    break;
                }

                case TokenKind.Unresolved:
                {
                    var free = IsFreeText(token);
                    this.Diag(free ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning, "unresolved-token",
                        free ? $"'{t.NoteText}' is free text, not an action; kept as a note."
                             : $"'{t.NoteText}' is not an action of {string.Join("/", candidates)} at level {this.level}; kept as a note. Fix it in review or add an alias.",
                        cell.Src);
                    break;
                }
            }
        }

        /// <summary>The hint text worth showing: the rest and parentheticals without the target, "on CD" and a re-anchored mechanic name.</summary>
        private IEnumerable<string> HintNotes(string? rest, ParsedToken token, HintTiming timing)
        {
            var parts = new List<string>();
            if (rest is not null)
                parts.Add(rest);
            parts.AddRange(token.Hints);
            foreach (var raw in parts)
            {
                var text = raw;
                if (token.Target is { } target)
                {
                    text = text.Replace(target.Text, " ", StringComparison.OrdinalIgnoreCase);
                    if (string.Equals(text.Trim(), target.Value, StringComparison.OrdinalIgnoreCase))
                        text = string.Empty;
                }

                text = WhenReadyWords().Replace(text, " ");
                if (timing.Reanchored && timing.Anchor is { } anchor && this.mechanics.FindInText(text, anchor.Phase, anchor, whole: true) is not null)
                    text = string.Empty;
                text = Regex.Replace(text, @"\s+", " ").Trim(' ', ',', ';', ':', '-', '/');
                if (text.Length > 0)
                    yield return text;
            }
        }

        private void ImportGroup(RowState row, AssigneeCell cell, string key, ParsedItem item, List<TokenState> group, int groupIndex, IReadOnlyList<double> times, IReadOnlyList<Job> candidates)
        {
            var header = cell.Assignee;

            // The tokens of a group share their timing, except in a sequence, whose hint may sit on any step
            // ("Protraction + Recit → Adlo → Deploy (≈3:04, just before P3 ends)").
            var timing = group.Select(t => t.Timing).FirstOrDefault(t => !t.IsNone) ?? HintTiming.None;
            var condition = group.Select(t => t.Timing.JobCondition).FirstOrDefault(c => c is not null);
            var jobs = condition is { } only ? candidates.Where(j => j == only).ToList() : candidates.ToList();
            if (jobs.Count == 0)
            {
                this.Diag(DiagnosticSeverity.Info, "job-condition", $"'{item.Text}' is only for {condition}, which the {key} column is not; not imported.", cell.Src);
                return;
            }

            // Items per job; jobs that resolve no action of the group get no entry of their own.
            var perJob = new List<(Job Job, List<PlanItem> Items, bool Seq, List<ActionInfo> Actions)>();
            foreach (var job in jobs)
            {
                var items = new List<PlanItem>();
                var actions = new List<ActionInfo>();
                var seq = item.IsSequence;
                foreach (var t in group)
                {
                    if (t.ByJob[job] is { Kind: TokenKind.Actions } r)
                    {
                        if (r.Alternatives.Count == 1)
                        {
                            items.AddRange(r.Alternatives[0].Actions.Select(a => PlanItem.ForAction(a.Id, a.Name)));
                            actions.AddRange(r.Alternatives[0].Actions);
                            seq |= r.Alternatives[0].Sequence;
                        }
                        else
                        {
                            var options = r.Alternatives.SelectMany(a => a.Actions).DistinctBy(a => a.Id).ToList();
                            items.Add(PlanItem.AnyOf(options.Select(a => PlanItem.ForAction(a.Id, a.Name))));
                            actions.AddRange(options);
                        }
                    }
                    else if (t.Kind is TokenKind.NoteOnly or TokenKind.Unresolved or TokenKind.OtherJob or TokenKind.NotAvailable)
                    {
                        items.Add(PlanItem.ForNote(t.NoteText));
                    }
                }

                perJob.Add((job, items, seq, actions));
            }

            var withActions = perJob.Where(p => p.Actions.Count > 0).ToList();
            List<(Job? Job, List<PlanItem> Items, bool Seq, List<ActionInfo> Actions)> entries;
            if (withActions.Count == 0)
            {
                var notes = perJob.FirstOrDefault().Items ?? [];
                if (notes.Count == 0)
                    return;

                // Notes only: one entry for the column (per job for a column that has neither a slot nor a job).
                entries = header.Slot is null && header.Jobs.Count == 0 && jobs.Count > 1
                    ? perJob.Select(p => ((Job?)p.Job, p.Items, p.Seq, p.Actions)).ToList()
                    : [(jobs.Count == 1 ? jobs[0] : null, notes, false, [])];
            }
            else if (jobs.Count == 1 || withActions.Count == 1)
            {
                var p = withActions[0];
                entries = [(p.Job, p.Items, p.Seq, p.Actions)];
            }
            else if (header.Slot is not null && withActions.Select(p => Signature(p.Items, p.Seq)).Distinct().Count() == 1)
            {
                // The same actions for every job of the slot (a role action such as Reprisal or Feint).
                var p = withActions[0];
                entries = [(null, p.Items, p.Seq, p.Actions)];
            }
            else
            {
                entries = withActions.Select(p => ((Job?)p.Job, p.Items, p.Seq, p.Actions)).ToList();
            }

            var anchor = timing.Anchor ?? row.Mechanic!;
            var target = group.Select(t => t.Token.Target?.Value).FirstOrDefault(v => v is not null);
            var footnote = group.Any(t => t.Token.Footnote) ? this.Footnote(row, cell) : null;
            var notesText = this.EntryNote(group, footnote);
            var detail = row.Detail.Count > 0 ? string.Join("\n", row.Detail) : null;
            var optional = group.All(t => t.Token.Optional);
            var whenReady = group.Any(t => t.Token.WhenReady);
            var lastHit = Math.Max(0, anchor.Hits.Count - 1);
            var part = 0;

            // One entry per time of the item (a list such as "Kera 0:28 / 0:37" is pressed at each) and per job.
            foreach (var time in times.Count > 0 ? times.Select(t => (double?)t) : [null])
            {
                double? press = null;
                if (time is { } t)
                {
                    press = RowTiming.ToTimeline(t, row.Refs, row.PhaseId is { } ph && this.zeros.TryGetValue(ph, out var z) ? z : null);
                    if (press is null)
                        this.Diag(DiagnosticSeverity.Warning, "untimed", $"'{Flat(item.Text)}': the time {ParsedTime.Format(t)} cannot be placed (the row has no time and {row.PhaseId?.ToUpperInvariant() ?? "the phase"} no zero point); timed automatically instead.", cell.Src);
                }
                else if (timing.After)
                {
                    // "after 1st Hit" → that hit; "after Solar Ray" / "after the hit" → the last hit.
                    press = anchor.HitAt(Math.Clamp(timing.HitIndex ?? lastHit, 0, lastHit)) + AfterHitOffset;
                }

                if (press is { } planned)
                    this.CheckAfterPhaseEnd(anchor, planned, item, cell);

                foreach (var (job, items, seq, actions) in entries)
                {
                    var timingAction = actions.FirstOrDefault();

                    // A tank's single-target cooldowns without a written target are its own; others get one from the notes.
                    var isTank = job is { } j ? Jobs.RoleOf(j) == Role.Tank : header.Role == Role.Tank;
                    var singles = actions.Where(a => a.Target == ActionTarget.Single).ToList();
                    var entry = new PlanEntry
                    {
                        Mechanic = anchor.Id,
                        Who = new WhoSpec { Slot = header.Slot, Job = job, Assignee = key },
                        Do = items.Select(Clone).ToList(),
                        Seq = seq && items.Count > 1,
                        Target = target ?? (singles.Count > 0 && !isTank ? this.InferTarget(row, anchor, singles) : null),
                        Note = notesText,
                        Detail = detail,
                        Optional = optional,
                        WhenReady = whenReady,
                        AtPhaseEnd = timing.AtPhaseEnd,
                        Severity = row.Row.Severity,
                        Src = cell.Src,
                    };

                    if (press is { } p)
                    {
                        // The hit the press belongs to: the one the hint names, else the first at or after the press
                        // (for heals and "after" hints the last one before it).
                        var k = timing.HitIndex is { } h ? h
                            : timing.LastHit && (timing.Anchor is not null || time is null) ? lastHit
                            : RowTiming.HitFor(anchor, p, timing.After || IsAfterHit(timingAction));
                        k = Math.Clamp(k, 0, lastHit);
                        entry.HitIndex = k > 0 ? k : null;
                        entry.Offset = ImportText.Round(p - anchor.HitAt(k));
                    }

                    var estimate = anchor.HitAt(entry.HitIndex ?? 0) + (entry.Offset ?? TimingRules.DefaultOffset(timingAction, this.level));
                    this.drafts.Add(new Draft(entry, estimate, row.Tab.Order, row.Row.Index, cell.Column.Index, item.Index, (groupIndex * 100) + part));
                    part++;
                }
            }
        }

        /// <summary>
        /// A press planned after the last hit of a phase that another phase follows may never come: the timeline jumps to
        /// the next phase at that hit (Blue Screen, Blind Faith) or earlier (a pushed enrage). docs/research/mechanics.md §5.2:
        /// such actions belong to the next phase's start; the review shows them.
        /// </summary>
        private void CheckAfterPhaseEnd(Mechanic anchor, double press, ParsedItem item, AssigneeCell cell)
        {
            var phases = this.fight.Phases;
            var index = phases.ToList().FindIndex(p => string.Equals(p.Id, anchor.Phase, StringComparison.OrdinalIgnoreCase));
            if (index < 0 || index + 1 >= phases.Count || this.mechanics.PhaseEnd(anchor.Phase) is not { } end)
                return;
            var last = end.Hits.Count > 0 ? end.Hits[^1] : end.Hit;
            if (press > last + 0.05)
            {
                this.Diag(DiagnosticSeverity.Warning, "after-phase-end",
                    $"'{Flat(item.Text)}' is planned at {ImportText.Seconds(press)}, after {end.Id} ({ImportText.Seconds(last)}), the end of {anchor.Phase.ToUpperInvariant()}: the timeline may already be in {phases[index + 1].Id.ToUpperInvariant()}. Anchor it to {phases[index + 1].Id.ToUpperInvariant()} in review.",
                    cell.Src);
            }
        }

        private string? EntryNote(List<TokenState> group, string? footnote)
        {
            var parts = new List<string>();
            var withNotes = group.Where(t => t.Notes.Count > 0).ToList();
            foreach (var t in withNotes)
            {
                var text = string.Join("; ", t.Notes);
                parts.Add(group.Count > 1 && t.Token.Head.Length > 0 ? t.Token.Head + " " + text : text);
            }

            if (footnote is not null)
                parts.Add(footnote);
            var note = string.Join("; ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
            return note.Length == 0 ? null : note;
        }

        /// <summary>The footnote for a "*" marker: one in the cell's column, else the tab's only footnote.</summary>
        private string? Footnote(RowState row, AssigneeCell cell)
        {
            var footnotes = row.Tab.Layout.Footnotes;
            var same = footnotes.FirstOrDefault(f => f.Column == cell.Column.Index);
            var chosen = same ?? (footnotes.Count == 1 ? footnotes[0] : null);
            return chosen?.Text.Trim();
        }

        // ------------------------------------------------------------------ targets

        /// <summary>
        /// A target for single-target actions without one in the cell: a phrase "on/to &lt;who&gt;" (or "&lt;action&gt; the tank")
        /// in the row's notes, from a sentence that names the action or no action at all; else "tank" when the anchor is a
        /// tankbuster.
        /// </summary>
        private string? InferTarget(RowState row, Mechanic anchor, IEnumerable<ActionInfo> singles)
        {
            var actions = singles.ToList();
            string? general = null;
            foreach (var sentence in row.Detail.SelectMany(d => SentenceBreak().Split(d)))
            {
                var words = sentence.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim(',', ':', '(', ')', '"', '\'', '+')).Where(w => w.Length > 0).ToList();
                if (words.Count == 0)
                    continue;
                var normalized = " " + AliasDictionary.Normalize(sentence) + " ";
                var mentions = actions.Any(a => this.KeysOf(a).Any(k => normalized.Contains(" " + k + " ", StringComparison.Ordinal)));
                if (mentions && TargetIn(words, this.KeysOf(actions[0]).Concat(actions.Skip(1).SelectMany(this.KeysOf)).ToList()) is { } own)
                    return own;
                if (general is null && !this.MentionsAnyAction(sentence) && TargetIn(words, []) is { } found)
                    general = found;
            }

            return general ?? (anchor.Kind == MechanicKind.Tankbuster ? "tank" : null);
        }

        private static readonly HashSet<string> TargetNouns = new(StringComparer.OrdinalIgnoreCase)
        {
            "tank", "tanks", "co-tank", "cotank", "healer", "healers", "player", "players", "non-tank", "non-tanks", "dps",
            "melee", "ranged", "mt", "ot", "st", "r1", "r2", "m1", "m2", "h1", "h2",
        };

        private static readonly HashSet<string> Determiners = new(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "your", "both", "each", "one", "all", "other",
        };

        /// <summary>"on the tanks" → "the tanks"; "Bene the tank" (when "bene" is one of <paramref name="verbs"/>) → "the tank".</summary>
        private static string? TargetIn(List<string> words, IReadOnlyList<string> verbs)
        {
            for (var i = 0; i < words.Count; i++)
            {
                var w = words[i];
                var intro = w.Equals("on", StringComparison.OrdinalIgnoreCase) || w.Equals("to", StringComparison.OrdinalIgnoreCase);
                var verb = !intro && verbs.Contains(AliasDictionary.Normalize(w)) && i + 1 < words.Count && Determiners.Contains(words[i + 1]);
                if (!intro && !verb)
                    continue;
                for (var k = i + 1; k < words.Count && k <= i + 4; k++)
                {
                    if (TargetNouns.Contains(words[k]))
                        return string.Join(' ', words.Skip(i + 1).Take(k - i));
                    if (k > i + 1 && !Determiners.Contains(words[k - 1]) && k - i > 3)
                        break;
                }
            }

            return null;
        }

        /// <summary>Alias keys (and the normalized name) of an action, for finding it in notes.</summary>
        private List<string> KeysOf(ActionInfo action)
        {
            if (this.actionKeys.Count == 0)
            {
                foreach (var key in this.options.Aliases.Keys)
                {
                    foreach (var e in this.options.Aliases.Lookup(key))
                    {
                        foreach (var name in e.Actions.Concat(e.Targeted))
                        {
                            if (!this.actionKeys.TryGetValue(name, out var list))
                                this.actionKeys[name] = list = [];
                            if (!list.Contains(key))
                                list.Add(key);
                        }
                    }
                }
            }

            var keys = this.actionKeys.TryGetValue(action.Name, out var found) ? found.ToList() : [];
            keys.Add(AliasDictionary.Normalize(action.Name));
            return keys;
        }

        private bool MentionsAnyAction(string sentence)
        {
            var words = ImportText.NormalizedWords(sentence);
            for (var i = 0; i < words.Length; i++)
            {
                if (words[i].Length < 3)
                    continue;
                if (this.options.Aliases.MatchLongest(string.Join(' ', words, i, words.Length - i)) is { } m && m.Expansions.Any(e => !e.NoteOnly))
                    return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ plan

        private MitPlan BuildPlan(List<TabState> tabs)
        {
            var ordered = this.drafts
                .OrderBy(d => Math.Round(d.Press, 3))
                .ThenBy(d => d.Tab).ThenBy(d => d.Row).ThenBy(d => d.Column).ThenBy(d => d.Item).ThenBy(d => d.Part)
                .ToList();
            var width = Math.Max(3, ordered.Count.ToString(CultureInfo.InvariantCulture).Length);
            for (var i = 0; i < ordered.Count; i++)
                ordered[i].Entry.Id = "e" + (i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');

            var assignees = new List<Assignee>();
            foreach (var tab in tabs)
            {
                foreach (var column in tab.Layout.AssigneeColumns)
                {
                    var key = tab.Keys[column.Index];
                    if (!this.AssigneeSelected(key, column.Assignee!.Key) || assignees.Any(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    assignees.Add(new Assignee { Key = key, Slot = column.Assignee.Slot, Job = column.Assignee.Job });
                }
            }

            var tabNames = tabs.Select(t => t.Layout.Name).ToList();
            var allPlanTabs = this.document.Tabs.Count;
            var name = this.options.PlanName ?? (tabNames.Count == 1 && allPlanTabs > 1 ? $"{this.document.Title} – {tabNames[0]}" : this.document.Title);
            var kind = this.options.SourceKind
                ?? (this.options.SourceUrl is { } url
                    ? url.Contains("/document/", StringComparison.OrdinalIgnoreCase) ? "google-doc" : "google-sheet"
                    : this.document.Tabs.Count == 1 && this.document.Tabs[0].Name == "Pasted" ? "paste" : "file");
            var identity = string.Join("\n", "mitcue-import", this.fight.Key, name, this.options.SourceUrl ?? this.document.Title, string.Join("|", tabNames), string.Join("|", this.options.Assignees ?? []));

            return new MitPlan
            {
                Id = this.options.PlanId ?? ImportText.NameGuid(identity).ToString("D"),
                Name = name,
                Fight = this.fight.Key,
                Source = new PlanSource
                {
                    Kind = kind,
                    Url = this.options.SourceUrl,
                    Tabs = tabNames,
                    Imported = this.options.ImportedDate ?? DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                },
                Assignees = assignees,
                Entries = ordered.Select(d => d.Entry).ToList(),
            };
        }

        // ------------------------------------------------------------------ helpers

        private void Diag(DiagnosticSeverity severity, string code, string message, string? src) =>
            this.diagnostics.Add(new ImportDiagnostic(severity, code, message, src));

        /// <summary>The jobs a column can stand for: its job(s), else the jobs of its slot or role.</summary>
        private static IReadOnlyList<Job> CandidateJobs(AssigneeHeader header)
        {
            if (header.Jobs.Count > 0)
                return header.Jobs;
            if (header.Job is { } job)
                return [job];
            if (header.Slot is { } slot)
            {
                return slot switch
                {
                    "MT" or "OT" => RoleJobs(Role.Tank),
                    "H1" or "H2" => RoleJobs(Role.Healer),
                    "M1" or "M2" => RoleJobs(Role.Melee),
                    _ => [.. RoleJobs(Role.PhysicalRanged), .. RoleJobs(Role.Caster)],
                };
            }

            return header.Role is Role.None ? Jobs.All.Where(j => j is not (Job.BLU or Job.BST)).ToList() : RoleJobs(header.Role);
        }

        private static List<Job> RoleJobs(Role role) => Jobs.All.Where(j => Jobs.RoleOf(j) == role && j is not (Job.BLU or Job.BST)).ToList();

        /// <summary>The sheet times of an item: each time of a list, else the start of a range; a sequence uses its first step's time when that has one.</summary>
        private static IReadOnlyList<double> ItemTimes(ParsedItem item)
        {
            var time = item.IsSequence && item.Steps[0].Time is { } firstStep ? firstStep : item.Time;
            if (time is null)
                return [];
            return time.More.Count > 0 ? [time.Seconds, .. time.More] : [time.Seconds];
        }

        /// <summary>True for actions pressed after the damage: heals that are not pre-placed, and healer limit breaks.</summary>
        private static bool IsAfterHit(ActionInfo? action) => action is not null && action.When switch
        {
            UseTiming.After => true,
            UseTiming.Before => false,
            _ => TimingRules.IsUsedAfterHit(action),
        };

        private static bool IsFreeText(ParsedToken token)
        {
            var words = token.Phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length >= 4 || words.Skip(1).Any(FreeTextWords.Contains) || (words.Length > 0 && FreeTextWords.Contains(words[0]));
        }

        private static string Signature(List<PlanItem> items, bool seq) => (seq ? "seq:" : string.Empty) + string.Join("|", items.Select(i => i.ToString() + "#" + string.Join(",", i.ActionIds())));

        private static PlanItem Clone(PlanItem i) => new() { Action = i.Action, Name = i.Name, Note = i.Note, Any = i.Any?.Select(Clone).ToList() };

        private static string Flat(string? text) => Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();
    }

    /// <summary>The text a token keeps as a note: its words with the parenthetical hints ("Soil (when rots explode)").</summary>
    private static string NoteTextOf(ParsedToken token)
    {
        var phrase = token.Phrase.Length > 0 ? token.Phrase : token.Text;
        return token.Hints.Count == 0 ? phrase : phrase + " (" + string.Join("; ", token.Hints) + ")";
    }
}
