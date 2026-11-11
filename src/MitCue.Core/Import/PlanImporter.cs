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
/// row time (with the phase zero point) or by row order (docs/research/mechanics.md §4.5). Timed rows are then checked
/// against the fitted phase clock: a row whose time contradicts its mechanic is re-anchored by its time or timed from the
/// phase zero point, with a <c>row-time</c> warning.
/// </para>
/// <para>
/// Items: inline hints, targets and conditions ("Kera after 1st Hit", "Short CD to R1", "Neutralsect if P4 skip") make
/// their token its own entry; parentheses after an item's last token speak for the whole item. Notes written in a
/// player's column below the table become entries (an action with a timing hint) or the detail of that column's entries.
/// See README.md for the rules.
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

        /// <summary>How the mechanic was found (<see cref="ImportedRow.Match"/>).</summary>
        public string Match { get; set; } = "unknown";

        public IReadOnlyList<RowRef> Refs { get; set; } = [];

        /// <summary>The row's time contradicts its mechanic: no row-relative timing and no part in the phase fit.</summary>
        public bool TimeIgnored { get; set; }

        /// <summary>Set when the row was re-anchored by its time: its time marks this hit (not necessarily the first).</summary>
        public IReadOnlyList<RowRef>? RefsByTime { get; set; }

        public List<string> Detail { get; } = [];

        public string? PhaseId => this.Row.PhaseId;

        /// <summary>The row's phase section of its tab (notes written for a column apply to the column's entries in it).</summary>
        public PhaseSection? Section => this.Row.Phase;
    }

    /// <summary>A note row's text in an assignee column ("Shields bevor the boss is targetable" under the H2 SCH column).</summary>
    /// <param name="Tab">The tab.</param>
    /// <param name="Row">The note row.</param>
    /// <param name="Above">The data row above it in its section, or null.</param>
    /// <param name="Text">The text and its column.</param>
    private sealed record NoteText(TabState Tab, RowLayout Row, RowState? Above, RowText Text);

    /// <summary>A token of an item with its resolution for every candidate job.</summary>
    private sealed class TokenState
    {
        public required ParsedToken Token { get; init; }

        public required Dictionary<Job, TokenResolution> ByJob { get; init; }

        /// <summary>The kind over all candidate jobs: Actions when any job resolves it.</summary>
        public required TokenKind Kind { get; init; }

        /// <summary>The last token of its item: its parentheticals (hint, target, condition) speak for the whole item.</summary>
        public bool IsLast { get; init; }

        /// <summary>The text after the action words, without the target and "on CD" ("after 1st Hit", "Stack"), or null.</summary>
        public string? Rest { get; set; }

        /// <summary>Timing from the text written after the action ("Kera after 1st Hit", "Spreadlo Stack").</summary>
        public HintTiming InlineTiming { get; set; } = HintTiming.None;

        /// <summary>Timing from the token's parentheticals ("Adlo (just before P3 ends)").</summary>
        public HintTiming ParenTiming { get; set; } = HintTiming.None;

        /// <summary>The timing the token's entry uses: its own, else the item's (see <see cref="IsLast"/>).</summary>
        public HintTiming Timing { get; set; } = HintTiming.None;

        /// <summary>A condition written after the action ("Neutralsect if P4 skip", "no skip"): the entry is optional.</summary>
        public string? Condition { get; set; }

        /// <summary>A condition in the token's parentheticals.</summary>
        public string? ParenCondition { get; set; }

        /// <summary>Hint text kept in the entry note (targets and "on CD" removed).</summary>
        public List<string> Notes { get; } = [];

        public string NoteText => NoteTextOf(this.Token);

        /// <summary>A target written after the action ("Short CD to R1", "Intervention Co-Tank"), as opposed to one in parentheses.</summary>
        public ParsedTarget? InlineTarget =>
            this.Token.Target is { } t && this.Token.Trailing is { } trailing && trailing.Contains(t.Text, StringComparison.OrdinalIgnoreCase) ? t : null;

        /// <summary>The token's own target: inline, or in parentheses on a token that is not the item's last.</summary>
        public ParsedTarget? OwnTarget => this.InlineTarget ?? (this.IsLast ? null : this.Token.Target);

        /// <summary>The target of the token's entry: its own, else the one in parentheses after the item's last token when it is used on a player.</summary>
        public ParsedTarget? Target { get; set; }

        /// <summary>Some job's action of the token is used on one player (a target makes sense for it; not Reprisal, Holmgang).</summary>
        public bool OnAPlayer => this.ByJob.Values.Any(r => r.Kind == TokenKind.Actions && r.Alternatives.Any(a => a.Actions.Any(x => x.Target == ActionTarget.Single)));
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

        /// <summary>Words that may follow an action without being a hint or another action ("Kera now", "Temp again").</summary>
        private static readonly HashSet<string> FillerWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "now", "here", "again", "too", "first", "next", "together", "both", "all", "each", "either", "same", "please", "pls",
            "time", "times", "there", "it", "this", "that", "them", "these", "those", "who", "everyone", "everybody",
        };

        /// <summary>Words between a note's leading job and its action: "WAR can use Shake as P3 dies".</summary>
        private static readonly HashSet<string> StatementFillers = new(StringComparer.OrdinalIgnoreCase)
        {
            "can", "could", "may", "might", "should", "must", "will", "use", "uses", "using", "press", "presses", "pop", "pops",
            "also", "then", "please", "pls", "the", "a", "an", "their", "your", "his", "her", "its",
        };

        /// <summary>Words that make a note's action optional ("WAR can use Shake …").</summary>
        private static readonly HashSet<string> OptionalWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "can", "could", "may", "might", "optional", "optionally",
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
        private readonly Dictionary<(int Tab, int Column), ColumnTally> columnTally = [];
        private readonly List<NoteText> noteTexts = [];
        private readonly List<(NoteText Note, AssigneeHeader Header, string Text, bool Optional)> statements = [];
        private readonly Dictionary<(TabState Tab, PhaseSection? Section, int Column), List<string>> columnNotes = [];
        private readonly HashSet<(TabState Tab, PhaseSection? Section, int Column)> columnNoteUse = [];
        private readonly List<(NoteText Note, List<int> Columns, string Text)> columnNoteTexts = [];
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

        // "tank: …", "the low tank: …", "MT: …" at the start of a sentence.
        [GeneratedRegex(@"^\s*(?<who>(?:the\s+|a\s+|your\s+)?[\p{L}\p{N}/-]+(?:\s+[\p{L}\p{N}/-]+)?)\s*:(?!\d)", RegexOptions.CultureInvariant)]
        private static partial Regex WhoColon();

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

            // Timed rows whose time contradicts their mechanic (a label that means another mechanic than the time, or a
            // typo in the time): re-anchor by the time or drop the row time, then fit again without them.
            if (this.CheckRowTimes(rows))
                this.Fit(rows.Where(r => r.Mechanic is not null && !r.Unknown && r.Row.Label is not null && !r.TimeIgnored && r.RefsByTime is null).Select(r => (r, r.Mechanic!)));
            foreach (var row in rows.Where(r => r.Mechanic is not null && !r.Unknown && !r.TimeIgnored))
                row.Refs = row.RefsByTime ?? RowTiming.Align(row.Row, row.Mechanic!, this.RowOffset(row.Mechanic!));

            this.PrepareNoteRows();
            foreach (var row in rows)
            {
                foreach (var cell in row.Row.Assignments)
                    this.ImportCell(row, cell);
            }

            this.ImportStatements();
            this.ReportNoteRows();
            this.ReportColumns();
            var plan = this.BuildPlan(tabs);
            return new ImportResult
            {
                Plan = plan,
                Diagnostics = this.diagnostics.ToList(),
                PhaseZeros = new Dictionary<string, double>(this.zeros, StringComparer.OrdinalIgnoreCase),
                PhaseSpread = new Dictionary<string, double>(this.spreads, StringComparer.OrdinalIgnoreCase),
                Rows = rows.Select(r => new ImportedRow(r.Tab.Layout.Name, r.Row.Index + 1, r.Row.Label, r.PhaseId, r.Mechanic?.Id, r.Match, r.Row.Src)).ToList(),
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
            var assigneeColumns = tab.Keys.Keys.ToHashSet();
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
                        // A continuation line of the notes column belongs to the row above; a note in a player's column is
                        // that player's (PrepareNoteRows); anything else is shown for review only.
                        var inNotes = row.Texts.Where(t => notesColumns.Contains(t.Column) && !string.IsNullOrWhiteSpace(t.Text)).ToList();
                        if (lastData is not null && inNotes.Count > 0)
                            lastData.Detail.Add(string.Join("\n", inNotes.Select(t => t.Text.Trim())));
                        foreach (var t in row.Texts.Where(t => !string.IsNullOrWhiteSpace(t.Text) && !(lastData is not null && notesColumns.Contains(t.Column))))
                        {
                            if (assigneeColumns.Contains(t.Column))
                                this.noteTexts.Add(new NoteText(tab, row, lastData, t));
                            else
                                this.Diag(DiagnosticSeverity.Info, "note-row", $"Row {row.Index + 1} is a note, not an assignment: \"{t.Text.Trim()}\" (shown for review only).", t.Src);
                        }

                        break;
                    case RowKind.Section or RowKind.PhaseLabel or RowKind.Header:
                        lastData = null;
                        break;
                }
            }
        }

        /// <summary>
        /// Notes written in a player's column under a table or between its rows (LPDU: "Shields bevor the boss is targetable"
        /// under the H2 SCH column, "WAR can use Shake as P3 dies for 1st P4 Proteans."). A footnote ("*…", with its lines
        /// that continue in lower case) belongs to the items marked "*". A note that names an action with a timing hint is
        /// imported as an entry (optional when it says "can"/"may"); any other note becomes the detail of the column's entries
        /// in its section (of every column it spans when merged).
        /// </summary>
        private void PrepareNoteRows()
        {
            var footnoteCells = new HashSet<string>(StringComparer.Ordinal);
            foreach (var note in this.noteTexts.Where(n => n.Text.Text.TrimStart().StartsWith('*')))
            {
                footnoteCells.Add(note.Text.Src);
                for (var r = note.Row.Index + 1; RowAt(note.Tab.Layout, r) is { Kind: RowKind.Note or RowKind.Footnote } next; r++)
                {
                    if (next.Texts.FirstOrDefault(t => t.Column == note.Text.Column) is not { } line || line.Text.Trim() is not { Length: > 0 } trimmed || !char.IsLower(trimmed[0]))
                        break;
                    footnoteCells.Add(line.Src);
                }
            }

            foreach (var note in this.noteTexts)
            {
                var text = note.Text.Text.Trim();
                if (!this.AssigneeSelected(note.Tab.Keys[note.Text.Column], note.Tab.Layout.Columns.First(c => c.Index == note.Text.Column).Assignee!.Key))
                    continue;
                if (footnoteCells.Contains(note.Text.Src))
                {
                    this.Diag(DiagnosticSeverity.Info, "note-row", $"Row {note.Row.Index + 1}: \"{text}\" is a footnote; it goes to the note of the items marked '*'.", note.Text.Src);
                    continue;
                }

                if (this.ReadStatement(note) is { } statement)
                {
                    this.statements.Add(statement);
                    continue;
                }

                var layout = note.Tab.Layout;
                var merge = layout.Tab.MergeAt(note.Row.Index, note.Text.Column);
                var columns = note.Tab.Keys.Keys.Where(c => c == note.Text.Column || (merge is { } m && m.Contains(note.Row.Index, c)))
                    .Where(c => this.AssigneeSelected(note.Tab.Keys[c], layout.Columns.First(x => x.Index == c).Assignee!.Key)).Order().ToList();
                foreach (var column in columns)
                {
                    var key = (note.Tab, note.Row.Phase, column);
                    if (!this.columnNotes.TryGetValue(key, out var list))
                        this.columnNotes[key] = list = [];
                    if (!list.Contains(text))
                        list.Add(text);
                }

                this.columnNoteTexts.Add((note, columns, text));
            }
        }

        /// <summary>
        /// After the cells: reports where each note of a player's column went. A column with no entry in the note's section
        /// to carry it gets a note entry of its own, anchored to the mechanic the note names ("… after Party Synergy") or to
        /// the row above the note.
        /// </summary>
        private void ReportNoteRows()
        {
            foreach (var (note, columns, text) in this.columnNoteTexts)
            {
                var phase = note.Row.PhaseId?.ToUpperInvariant() ?? "the tab";
                foreach (var column in columns)
                {
                    var name = note.Tab.Keys[column];
                    if (this.columnNoteUse.Contains((note.Tab, note.Row.Phase, column)))
                    {
                        this.Diag(DiagnosticSeverity.Info, "note-row", $"Row {note.Row.Index + 1} is a note, not an assignment: \"{text}\"; kept as the detail of the '{name}' entries of {phase}.", note.Text.Src);
                        continue;
                    }

                    var anchor = this.ImportNoteEntry(note, column, text);
                    this.Diag(DiagnosticSeverity.Info, "note-row",
                        anchor is null
                            ? $"Row {note.Row.Index + 1} is a note, not an assignment: \"{text}\"; the '{name}' column has no entry in {phase} and no mechanic to anchor it to (shown for review only)."
                            : $"Row {note.Row.Index + 1} is a note, not an assignment: \"{text}\"; the '{name}' column has no entry in {phase}, so it is a note entry at {anchor}.",
                        note.Text.Src);
                }
            }
        }

        /// <summary>A note entry for a column (<see cref="ReportNoteRows"/>); returns the mechanic it is anchored to, or null.</summary>
        private string? ImportNoteEntry(NoteText note, int column, string text)
        {
            var header = note.Tab.Layout.Columns.First(c => c.Index == column).Assignee!;
            var timing = HintParser.Parse(text, [], this.mechanics, note.Row.PhaseId, note.Above?.Mechanic, this.detector.PhaseId);
            var anchor = timing.Anchor ?? note.Above?.Mechanic ?? (note.Row.PhaseId is { } p ? this.mechanics.PhaseStart(p) : null);
            if (anchor is null)
                return null;
            var candidates = CandidateJobs(header);
            IReadOnlyList<Job?> jobs = header.Slot is null && header.Jobs.Count == 0 && candidates.Count > 1 ? candidates.Select(j => (Job?)j).ToList() : [header.Job];
            var lastHit = Math.Max(0, anchor.Hits.Count - 1);
            var hit = timing.After ? Math.Clamp(timing.HitIndex ?? lastHit, 0, lastHit) : 0;
            foreach (var job in jobs)
            {
                var entry = new PlanEntry
                {
                    Mechanic = anchor.Id,
                    HitIndex = hit > 0 ? hit : null,
                    Offset = timing.After ? AfterHitOffset : null,
                    Who = new WhoSpec { Slot = header.Slot, Job = job, Assignee = note.Tab.Keys[column] },
                    Do = [PlanItem.ForNote(text)],
                    AtPhaseEnd = timing.AtPhaseEnd,
                    Src = note.Text.Src,
                };
                var estimate = anchor.HitAt(hit) + (entry.Offset ?? TimingRules.DefaultOffset(null, this.level));
                this.drafts.Add(new Draft(entry, estimate, note.Tab.Order, note.Row.Index, column, 0, 0));
            }

            return anchor.Id;
        }

        /// <summary>
        /// A note that names an action with a timing hint: optional leading job abbreviations ("WAR", "WAR/PLD"), words such as
        /// "can use", then an action of those jobs (or of the column's), then a hint HintParser reads ("as P3 dies", "after
        /// Solar Ray"). Null for anything else ("Shields bevor the boss is targetable", "AST can shift Neutralsect to CD1 …").
        /// </summary>
        private (NoteText Note, AssigneeHeader Header, string Text, bool Optional)? ReadStatement(NoteText note)
        {
            var words = note.Text.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
            var jobs = new List<Job>();
            var i = 0;
            for (; i < words.Count; i++)
            {
                var parts = words[i].Trim(',', ':', ';', '.').Split('/', '+', '&');
                var found = parts.Select(p => p.Length == 3 && p.All(char.IsAsciiLetter) && Jobs.TryParse(p, out var j) ? j : (Job?)null).ToList();
                if (found.Count == 0 || found.Any(f => f is null))
                    break;
                jobs.AddRange(found.Select(f => f!.Value));
            }

            var column = note.Tab.Layout.Columns.First(c => c.Index == note.Text.Column).Assignee!;
            var header = jobs.Count == 0 ? column : new AssigneeHeader
            {
                Key = string.Join("/", jobs),
                Kind = AssigneeKind.Job,
                Job = jobs.Count == 1 ? jobs[0] : null,
                Jobs = jobs.Distinct().ToList(),
                Role = Jobs.RoleOf(jobs[0]),
            };
            var candidates = CandidateJobs(header);
            var optional = false;
            for (; i < words.Count; i++)
            {
                var rest = string.Join(' ', words.Skip(i));
                if (candidates.Select(j => this.resolver.ResolveFollowing(rest, j)).FirstOrDefault(r => r.Kind == TokenKind.Actions && r.Rest is not null) is { } resolved)
                {
                    var timing = HintParser.Parse(resolved.Rest, [], this.mechanics, note.Row.PhaseId, note.Above?.Mechanic, this.detector.PhaseId);
                    return timing.IsNone ? null : (note, header, rest, optional);
                }

                var w = words[i].Trim(',', ':', ';', '.', '!', '?');
                if (!StatementFillers.Contains(w))
                    return null;
                optional |= OptionalWords.Contains(w);
            }

            return null;
        }

        /// <summary>Imports the notes that name an action with a timing hint (<see cref="ReadStatement"/>).</summary>
        private void ImportStatements()
        {
            foreach (var (note, header, text, optional) in this.statements)
            {
                var anchorRow = new RowState { Tab = note.Tab, Row = note.Row, Mechanic = note.Above?.Mechanic ?? (note.Row.PhaseId is { } p ? this.mechanics.PhaseStart(p) : null), Unknown = true, Match = "note" };
                if (anchorRow.Mechanic is null)
                    continue;
                var column = new ColumnLayout(note.Text.Column, ColumnRole.Assignee, header.Key, header);
                var cell = new AssigneeCell(column, new SheetCell { Text = text }, note.Text.Src);
                var key = ReferenceEquals(header, note.Tab.Layout.Columns.First(c => c.Index == note.Text.Column).Assignee) ? note.Tab.Keys[note.Text.Column] : null;
                var before = this.drafts.Count;
                foreach (var item in CellParser.Default.Parse(cell.Cell, cell.Src).Items)
                    this.ImportItem(anchorRow, cell, key, item, new NoteStatement(optional));
                var made = this.drafts.Skip(before)
                    .Select(d => string.Join(" + ", d.Entry.Do.Select(i => i.Name ?? i.Note)) + " at " + d.Entry.Mechanic + (d.Entry.AtPhaseEnd ? " (phase end)" : string.Empty))
                    .Distinct().ToList();
                var count = this.drafts.Count - before;
                this.Diag(DiagnosticSeverity.Info, "note-row",
                    $"Row {note.Row.Index + 1} is a note that names an action: \"{note.Text.Text.Trim()}\"; imported as {(count == 1 ? "an entry" : count + " entries")} for {header.Key}{(optional ? " (optional)" : string.Empty)}: {string.Join("; ", made)}.",
                    note.Text.Src);
            }
        }

        /// <summary>An item read from a note row (<see cref="ReadStatement"/>); optional when the note says the action "can" be used.</summary>
        private sealed record NoteStatement(bool Optional);

        /// <summary>
        /// The resolution rule of docs/research/mechanics.md §4.5, row by row in tab order. Untimed rows rely on the order:
        /// a row that is earlier in the fight than the row above it, or a repeated label with no unused candidate after the
        /// row above, is reported (warning), since the labels below it may then be mis-assigned.
        /// </summary>
        private void ResolveMechanics(TabState tab)
        {
            string? phase = null;
            var prev = double.NegativeInfinity;
            Mechanic? above = null; // the mechanic that set prev
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            RowState? last = null;
            var pending = new List<RowState>(); // unlabelled rows before the first labelled row of the phase
            var first = true;
            foreach (var row in tab.Rows)
            {
                if (first || !string.Equals(row.PhaseId, phase, StringComparison.OrdinalIgnoreCase))
                {
                    this.ResolvePending(pending, null);
                    first = false;
                    phase = row.PhaseId;
                    prev = phase is not null && this.fight.GetPhase(phase) is { } p ? p.Start : double.NegativeInfinity;
                    above = null;
                    used.Clear();
                    last = null;
                }

                if (row.Row.Label is null)
                {
                    if (!this.ResolveUnlabelled(row, last))
                        pending.Add(row);
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
                    row.Match = "unknown";
                    continue;
                }

                Mechanic chosen;
                if (candidates.Count == 1)
                {
                    chosen = candidates[0];
                    row.Match = row.Candidates.Match == MechanicMatch.OtherPhase ? "other-phase" : row.Candidates.Match.ToString().ToLowerInvariant();
                }
                else
                {
                    var zero = phase is not null && this.zeros.TryGetValue(phase, out var z) ? z : (double?)null;
                    string rule;
                    var severity = DiagnosticSeverity.Info;
                    if (row.Row.Time is { } time && zero is { } known)
                    {
                        // By time; a time that fits none of the candidates is reported by CheckRowTimes.
                        var at = known + time.Seconds;
                        chosen = candidates.OrderBy(m => Math.Abs(m.Hit + this.RowOffset(m) - at)).First();
                        rule = $"its time {ParsedTime.Format(time.Seconds)} (timeline {ImportText.Seconds(at)})";
                        row.Match = "time";
                    }
                    else if (candidates.FirstOrDefault(m => m.Hit >= prev - 0.05 && !used.Contains(m.Id)) is { } next)
                    {
                        chosen = next;
                        rule = "row order";
                        row.Match = "order";
                    }
                    else
                    {
                        // Every unused candidate is earlier than the row above: the rows are not in fight order, so the order
                        // rule cannot tell the repeated labels apart.
                        chosen = candidates.FirstOrDefault(m => !used.Contains(m.Id)) ?? candidates[0];
                        rule = used.Contains(chosen.Id)
                            ? "row order, although an earlier row already has it"
                            : $"row order, although it is earlier in the fight than the row above ({ImportText.Seconds(prev)}): the rows are not in fight order, so check which one each repeated label means";
                        row.Match = "order";
                        severity = DiagnosticSeverity.Warning;
                    }

                    this.Diag(severity, "ambiguous-mechanic",
                        $"'{row.Row.Label}' can mean {string.Join(", ", candidates.Select(m => m.Id))}; {chosen.Id} was chosen by {rule}.",
                        row.Row.Src);
                }

                if (candidates.Count == 1 && row.Row.Time is null && chosen.Hit < prev - 0.05 && above is not null && row.Candidates.Match != MechanicMatch.OtherPhase)
                {
                    this.Diag(DiagnosticSeverity.Warning, "row-order",
                        $"'{row.Row.Label}' is {chosen.Id} ({ImportText.Seconds(chosen.Hit)}), earlier in the fight than the row above ({above.Id}, {ImportText.Seconds(prev)}): the rows are not in fight order, so repeated labels around it may be mis-assigned.",
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
                if (row.Candidates.Match != MechanicMatch.OtherPhase)
                {
                    // A mechanic of another phase says nothing about the order of this phase's rows.
                    prev = chosen.Hit;
                    above = chosen;
                    used.Add(chosen.Id);
                }

                last = row;
                this.ResolvePending(pending, row);
            }

            this.ResolvePending(pending, null);
        }

        /// <summary>
        /// A row with assignments but no label: by its time when it has one, else it belongs to the row above. Returns false
        /// when neither applies (the first rows of a phase): <see cref="ResolvePending"/> then gives it the next row's mechanic.
        /// </summary>
        private bool ResolveUnlabelled(RowState row, RowState? last)
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
                    row.Match = "time";
                    return true;
                }
            }

            if (last?.Mechanic is null)
                return false;
            row.Mechanic = last.Mechanic;
            row.Match = "previous";

            // A borrowed mechanic: the row's own time (if any) does not mark its hits, so its items are timed through the
            // phase zero point instead of row-relative.
            row.Unknown = row.Row.Time is not null;
            return true;
        }

        /// <summary>
        /// Unlabelled rows with no row above them in their phase: the next labelled row's mechanic (<paramref name="next"/>),
        /// else (no labelled row follows in the phase) the phase's first mechanic, which is reported for review.
        /// </summary>
        private void ResolvePending(List<RowState> pending, RowState? next)
        {
            foreach (var row in pending)
            {
                if (next?.Mechanic is { } m)
                {
                    row.Mechanic = m;
                    row.Match = "next";
                    row.Unknown = row.Row.Time is not null;
                    this.Diag(DiagnosticSeverity.Info, "unlabelled-row", $"Row {row.Row.Index + 1} has no mechanic label and no row above it in {row.PhaseId?.ToUpperInvariant() ?? "its phase"}; its entries are anchored to the next row's {m.Id}.", row.Row.Src);
                    continue;
                }

                var start = row.PhaseId is { } phase ? this.mechanics.PhaseStart(phase) : null;
                row.Mechanic = start;
                row.Match = "unknown";
                row.Unknown = true;
                this.Diag(DiagnosticSeverity.Warning, "unlabelled-row",
                    start is null
                        ? $"Row {row.Row.Index + 1} has no mechanic label and no phase; its entries are not imported. Add the label in review."
                        : $"Row {row.Row.Index + 1} has no mechanic label and no labelled row around it in {row.PhaseId!.ToUpperInvariant()}; its entries are anchored to the phase start ({start.Id}). Add the label in review.",
                    row.Row.Src);
            }

            pending.Clear();
        }

        /// <summary>
        /// Phase zero points: the median of <c>timeline − sheet time</c> over the matched times of the rows. Rows that disagree
        /// with the others (a label that names another mechanic than its time, a typo in a time) are left out: the median is
        /// taken over the largest group of points that agree within <see cref="RowTimeTolerance"/>, when there is one.
        /// </summary>
        private void Fit(IEnumerable<(RowState Row, Mechanic Mechanic)> rows)
        {
            var points = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (row, m) in rows)
            {
                if (row.PhaseId is not { } phase || row.Row.Time is null || !CountsForFit(m, phase))
                    continue;
                if (!points.TryGetValue(phase, out var list))
                    points[phase] = list = [];
                list.AddRange(RowTiming.Align(row.Row, m, this.RowOffset(m)).Select(r => r.Timeline - r.SheetTime));
            }

            foreach (var (phase, list) in points)
            {
                if (list.Count == 0)
                    continue;
                var agreeing = LargestAgreeingGroup(list);
                var zero = RowTiming.Median(agreeing);
                this.zeros[phase] = ImportText.Round(zero);
                this.spreads[phase] = ImportText.Round(agreeing.Max(p => Math.Abs(p - zero)));
            }
        }

        /// <summary>Rows of these mechanic kinds and phases calibrate the phase clock (enrages, dodges and markers do not).</summary>
        private static bool CountsForFit(Mechanic m, string phase) =>
            m.Kind is not (MechanicKind.Enrage or MechanicKind.Other or MechanicKind.Marker) && string.Equals(m.Phase, phase, StringComparison.OrdinalIgnoreCase);

        /// <summary>The most points within a <see cref="RowTimeTolerance"/>-wide window (all of them when none has two or more).</summary>
        private static List<double> LargestAgreeingGroup(List<double> points)
        {
            var sorted = points.Order().ToList();
            var (bestStart, bestCount) = (0, 0);
            for (int i = 0, j = 0; i < sorted.Count; i++)
            {
                while (sorted[i] - sorted[j] > RowTimeTolerance)
                    j++;
                if (i - j + 1 > bestCount)
                    (bestStart, bestCount) = (j, i - j + 1);
            }

            return bestCount >= 2 ? sorted.GetRange(bestStart, bestCount) : sorted;
        }

        /// <summary>
        /// Seconds a timed row may be off its mechanic's hit on the fitted phase clock: the sheet's whole-second rounding and
        /// the drift of a phase (TOP Carry the Clear: up to 2.4 s in P5–P6, docs/research/mechanics.md §4.2) stay below it.
        /// </summary>
        private const double RowTimeTolerance = 5.0;

        /// <summary>
        /// Checks every timed row against the fitted phase zero point: a row whose time is more than
        /// <see cref="RowTimeTolerance"/> from its mechanic's hit is reported (warning). When the time matches a hit of another
        /// mechanic the label can mean under any match tier ("Solar Ray ×2" timed at the first Solar Ray, cactbot's per-hit
        /// "Revolting Ruin III 2" for the second hit of the first cast), the row is anchored there; otherwise the row keeps its
        /// mechanic and its items are timed through the phase zero point instead of the row time. Returns true when any row
        /// was changed.
        /// </summary>
        private bool CheckRowTimes(List<RowState> rows)
        {
            var changed = false;
            foreach (var row in rows)
            {
                if (row.Mechanic is not { } m || row.Unknown || row.Row.Label is not { } label || row.Row.Time is not { } time || row.PhaseId is not { } phase)
                    continue;
                if (!CountsForFit(m, phase) || !this.zeros.TryGetValue(phase, out var zero))
                    continue;
                var refs = RowTiming.Align(row.Row, m, this.RowOffset(m));
                var off = zero + refs[0].SheetTime - refs[0].Timeline;
                if (Math.Abs(off) <= RowTimeTolerance)
                    continue;

                changed = true;
                var at = zero + time.Seconds;
                var sheetTime = ParsedTime.Format(time.Seconds);
                var nearest = this.mechanics.AllCandidates(label, phase)
                    .Where(c => CountsForFit(c, phase))
                    .SelectMany(c => (c.Hits.Count > 0 ? c.Hits : [c.Hit]).Select((h, k) => (Mechanic: c, Index: k, Time: h + (k == 0 ? this.RowOffset(c) : 0))))
                    .OrderBy(x => Math.Abs(x.Time - at)).ThenBy(x => x.Time)
                    .FirstOrDefault();
                if (nearest.Mechanic is not null && Math.Abs(nearest.Time - at) <= RowTimeTolerance)
                {
                    row.Mechanic = nearest.Mechanic;
                    row.Match = "time";
                    row.RefsByTime = [new RowRef(time.Seconds, nearest.Index, nearest.Time)];
                    this.Diag(DiagnosticSeverity.Warning, "row-time",
                        $"'{label}' at {sheetTime} reads as {m.Id}, but that is {ImportText.Seconds(Math.Abs(off))} s from the row's time on the {phase.ToUpperInvariant()} clock (zero {ImportText.Seconds(zero)}); the time matches {nearest.Mechanic.Id} (hit {nearest.Index + 1} at {ImportText.Seconds(nearest.Time)}), so the row is anchored there. Check the label in review.",
                        row.Row.Src);
                }
                else
                {
                    row.TimeIgnored = true;
                    this.Diag(DiagnosticSeverity.Warning, "row-time",
                        $"'{label}' at {sheetTime} reads as {m.Id}, but that is {ImportText.Seconds(Math.Abs(off))} s from the row's time on the {phase.ToUpperInvariant()} clock (zero {ImportText.Seconds(zero)}); the row's items are timed from the phase zero point instead of the row time. Check the row's time and label in review.",
                        row.Row.Src);
                }
            }

            return changed;
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

        private void ImportItem(RowState row, AssigneeCell cell, string? key, ParsedItem item, NoteStatement? statement = null)
        {
            var header = cell.Assignee;
            var candidates = CandidateJobs(header);
            var parsedTokens = item.Tokens.Select(WithoutOrdinalTarget).ToList();
            var tokens = new List<TokenState>();
            for (var i = 0; i < parsedTokens.Count; i++)
            {
                var token = parsedTokens[i];
                var byJob = candidates.ToDictionary(j => j, j => this.resolver.Resolve(token, j));
                var kind = byJob.Values.Any(r => r.Kind == TokenKind.Actions) ? TokenKind.Actions
                    : byJob.Values.Any(r => r.Kind == TokenKind.NoteOnly) ? TokenKind.NoteOnly
                    : byJob.Values.Any(r => r.Kind == TokenKind.NotAvailable) ? TokenKind.NotAvailable
                    : byJob.Values.Any(r => r.Kind == TokenKind.OtherJob) ? TokenKind.OtherJob
                    : byJob.Values.All(r => r.Kind == TokenKind.Empty) ? TokenKind.Empty
                    : TokenKind.Unresolved;
                var state = new TokenState { Token = token, ByJob = byJob, Kind = kind, IsLast = i == parsedTokens.Count - 1 };
                this.Describe(state, row, cell, header, candidates);
                tokens.Add(state);
            }

            if (tokens.Count == 0 || tokens.All(t => t.Kind == TokenKind.Empty))
                return;
            if (statement is null)
                this.CountColumn(row, cell, tokens);

            if (!item.IsSequence)
                this.ReadPartWords(tokens, row);

            // What the item's last token says in parentheses (a hint, a target, a condition) speaks for every token of the
            // item without its own: "Recit + Adlo (just before P3 ends)" is one phase-end entry. Inline text speaks for its
            // token only: "Kera after 1st Hit", "Short CD to R1", "Neutralsect if P4 skip".
            var last = tokens[^1];
            var itemTiming = last.InlineTiming.IsNone ? last.ParenTiming : HintTiming.None;
            var itemJob = last.InlineTiming.JobCondition is null ? last.ParenTiming.JobCondition : null;
            var itemCondition = last.Condition is null ? last.ParenCondition : null;
            var itemTarget = last.InlineTarget is null ? last.Token.Target : null;
            foreach (var t in tokens)
            {
                var own = !t.InlineTiming.IsNone ? t.InlineTiming : !t.IsLast ? t.ParenTiming : HintTiming.None;
                var job = t.InlineTiming.JobCondition ?? (t.IsLast ? null : t.ParenTiming.JobCondition) ?? itemJob;
                t.Timing = (own.IsNone ? itemTiming : own) with { JobCondition = job };
                if (!t.IsLast)
                    t.Condition ??= t.ParenCondition;
                t.Target = t.OwnTarget ?? (t.OnAPlayer ? itemTarget : null);
            }

            // Tokens with their own timing, job filter, condition or target ("Kera after 1st Hit" next to "Eprog + Panhaima",
            // "Rep + Short CD to R1") become their own entry; the steps of a sequence stay together.
            var groups = item.IsSequence
                ? [tokens]
                : tokens.GroupBy(t => (t.Timing.Key, t.Timing.JobCondition, t.Condition is not null, t.Target?.Value)).Select(g => g.ToList()).ToList();
            var shared = new ItemShared(
                Times: ItemTimes(item),
                Condition: itemCondition,
                StepNotes: StepTimeNotes(item),
                Optional: statement?.Optional ?? false);
            for (var g = 0; g < groups.Count; g++)
                this.ImportGroup(row, cell, key, item, groups[g], g, shared, candidates, header);
        }

        /// <summary>
        /// A token whose "target" is only an ordinal ("for 1st P4 Proteans", "Rampart for 1st stack") without it: an ordinal
        /// alone counts hits, not players ("to 4" and "your 3rd/4th-in-line player" stay targets), so the words stay hint text.
        /// </summary>
        private static ParsedToken WithoutOrdinalTarget(ParsedToken token) =>
            token.Target is { } target && OrdinalOnly().IsMatch(target.Value.Trim()) ? token with { Target = null } : token;

        [GeneratedRegex(@"^\d{1,2}(?:st|nd|rd|th)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex OrdinalOnly();

        /// <summary>What the tokens of an item share.</summary>
        /// <param name="Times">The item's sheet times (<see cref="ItemTimes"/>).</param>
        /// <param name="Condition">A condition in parentheses after the item's last token ("Soil + Expedient (if needed)").</param>
        /// <param name="StepNotes">Times of later sequence steps that the entry's single press time cannot keep ("Adlo 2:50").</param>
        /// <param name="Optional">The item comes from a note that says the action "can" be used.</param>
        private sealed record ItemShared(IReadOnlyList<double> Times, string? Condition, IReadOnlyList<string> StepNotes, bool Optional);

        /// <summary>
        /// Part words after an action name the hits they mean: in "Succor Proteans + Soil + Spreadlo Stack" on Wave Cannon 1
        /// (two protean sets, then the Wild Charge stack), "Proteans" is the first group of hits and "Stack" the second. The
        /// hits of the row's mechanic are grouped where the gap between them grows (at least twice the smallest gap); when the
        /// item names as many different parts as there are groups, the n-th part means the n-th group, and a token whose part
        /// is not the first is timed for that group's first hit.
        /// </summary>
        private void ReadPartWords(List<TokenState> tokens, RowState row)
        {
            if (row.Mechanic is not { } m)
                return;
            var parts = tokens.Where(t => t.Kind == TokenKind.Actions && t.InlineTiming.IsNone && HintParser.PartNoun(t.Rest) is not null).ToList();
            var nouns = parts.Select(t => HintParser.PartNoun(t.Rest)!).Distinct().ToList();
            var groups = HitGroups(m);
            if (nouns.Count < 2 || nouns.Count != groups.Count)
                return;
            foreach (var t in parts)
            {
                var first = groups[nouns.IndexOf(HintParser.PartNoun(t.Rest)!)];
                if (first > 0)
                    t.InlineTiming = new HintTiming { HitIndex = first, JobCondition = t.InlineTiming.JobCondition };
            }
        }

        /// <summary>The first hit index of each group of hits that belong together (see <see cref="ReadPartWords"/>).</summary>
        private static List<int> HitGroups(Mechanic m)
        {
            var hits = m.Hits.Count > 0 ? m.Hits : [m.Hit];
            var starts = new List<int> { 0 };
            if (hits.Count < 2)
                return starts;
            var smallest = Enumerable.Range(1, hits.Count - 1).Min(k => hits[k] - hits[k - 1]);
            for (var k = 1; k < hits.Count; k++)
            {
                if (hits[k] - hits[k - 1] >= Math.Max(2 * smallest, smallest + 1))
                    starts.Add(k);
            }

            return starts;
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
                    this.FollowOnActions(t, row, cell, candidates);
                    var resolved = t.ByJob.Values.Where(r => r.Kind == TokenKind.Actions).ToList();
                    var rest = resolved.Select(r => r.Rest).FirstOrDefault(r => r is not null);
                    t.Rest = this.CleanRest(rest, token);
                    t.InlineTiming = HintParser.Parse(rest, [], this.mechanics, row.PhaseId, row.Mechanic, this.detector.PhaseId);
                    t.ParenTiming = HintParser.Parse(null, token.Hints, this.mechanics, row.PhaseId, row.Mechanic, this.detector.PhaseId);
                    t.Condition = HintParser.Condition(rest);
                    t.ParenCondition = token.Hints.Select(HintParser.Condition).FirstOrDefault(c => c is not null);
                    var timing = t.InlineTiming.IsNone ? t.ParenTiming : t.InlineTiming;
                    t.Notes.AddRange(this.HintNotes(rest, token, timing));
                    foreach (var fuzzy in resolved.SelectMany(r => r.Alternatives).Where(a => a.Fuzzy).DistinctBy(a => a.Matched))
                        this.Diag(DiagnosticSeverity.Warning, "fuzzy-match", $"'{fuzzy.FuzzyText}' was read as '{fuzzy.Matched}' ({string.Join(", ", fuzzy.Actions.Select(a => a.Name))}), a guess from its spelling; check it in review.", cell.Src);
                    var missing = resolved.SelectMany(r => r.NotAvailable).Distinct().ToList();
                    if (missing.Count > 0)
                        this.Diag(DiagnosticSeverity.Warning, "not-available-at-level", $"'{token.Phrase}': {string.Join(", ", missing)} is not available at level {this.level}; left out.", cell.Src);
                    if (timing.Reanchored && timing.Anchor is { } anchor)
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

        /// <summary>
        /// Checks the text after a resolved action ("Temp Bell", "Kera after 1st Hit", "Temp Xyzzy"): another action of the job
        /// written without a separator joins the token's actions (info), hint words, targets, times, mechanic names and
        /// note-only phrases stay hint text, and anything else is reported (warning) and kept in the note.
        /// </summary>
        private void FollowOnActions(TokenState t, RowState row, AssigneeCell cell, IReadOnlyList<Job> candidates)
        {
            string? unknown = null;
            var accepted = false;
            var joined = new List<string>();
            foreach (var job in t.ByJob.Keys.ToList())
            {
                var current = t.ByJob[job];
                if (current.Kind != TokenKind.Actions || current.Alternatives.Count != 1)
                    continue;
                for (var guard = 0; guard < 4; guard++)
                {
                    var text = this.CleanRest(current.Rest, t.Token);
                    if (text is null || this.IsHintText(text, row))
                    {
                        accepted = true;
                        break;
                    }

                    var next = this.resolver.ResolveFollowing(text, job);
                    if (next.Kind != TokenKind.Actions || next.Alternatives.Count != 1)
                    {
                        unknown ??= text;
                        break;
                    }

                    var a = current.Alternatives[0];
                    var b = next.Alternatives[0];
                    var merged = a with { Actions = [.. a.Actions, .. b.Actions.Where(x => !a.Actions.Contains(x))], Sequence = a.Sequence || b.Sequence, Rest = b.Rest };
                    current = current with { Alternatives = [merged], Rest = b.Rest, NotAvailable = [.. current.NotAvailable, .. next.NotAvailable] };
                    if (!joined.Contains(b.Matched, StringComparer.OrdinalIgnoreCase))
                        joined.Add(b.Matched);
                }

                t.ByJob[job] = current;
            }

            if (joined.Count > 0)
                this.Diag(DiagnosticSeverity.Info, "joined-actions", $"'{t.Token.Phrase}' was read as several actions written without a '+': '{string.Join("', '", joined)}' follow{(joined.Count == 1 ? "s" : string.Empty)} the first one.", cell.Src);
            if (unknown is not null && !accepted)
                this.Diag(DiagnosticSeverity.Warning, "unresolved-token", $"'{unknown}' after '{t.ByJob.Values.First(r => r.Kind == TokenKind.Actions).Matched}' is not an action of {string.Join("/", candidates)} at level {this.level}, a hint or a mechanic; kept in the note. Fix it in review or add an alias.", cell.Src);
        }

        /// <summary>The text after an action without its target and "on CD"/"when ready" words; null when nothing is left.</summary>
        private string? CleanRest(string? rest, ParsedToken token)
        {
            if (rest is null)
                return null;
            var text = rest;
            if (token.Target is { } target)
                text = text.Replace(target.Text, " ", StringComparison.OrdinalIgnoreCase);
            text = WhenReadyWords().Replace(text, " ");
            text = Regex.Replace(text, @"\s+", " ").Trim(' ', ',', ';', ':', '-', '/', '.');
            return text.Length == 0 ? null : text;
        }

        /// <summary>
        /// Text that reads as a hint, not as another action: it starts with a hint word (connector, ordinal, hit noun, target,
        /// slot, count, number, free-text word), a job abbreviation, a time, a mechanic of the row's phase or a note-only phrase.
        /// </summary>
        private bool IsHintText(string text, RowState row)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                return true;
            var first = words[0].Trim('.', ',', ':', ';', '!', '?', '"', '\'', '(', ')', '[', ']', '<', '>', '~', '≈', '#');
            if (first.Length == 0 || !first.Any(char.IsLetterOrDigit))
                return true;
            if (CellParser.IsHintWord(first) || HintParser.IsHintWord(first) || FreeTextWords.Contains(first) || FillerWords.Contains(first))
                return true;
            if (first.Length == 3 && first.All(char.IsAsciiLetter) && Jobs.TryParse(first, out _))
                return true;
            if (ParsedTime.FindAll(text) is { Count: > 0 } times && times[0].Index == 0)
                return true;
            if (this.mechanics.FindInText(text, row.PhaseId, row.Mechanic, whole: false) is not null)
                return true;
            return this.options.Aliases.MatchLongest(text) is { Expansions.Count: > 0 } m && m.Expansions.All(e => e.NoteOnly);
        }

        /// <summary>Counts, per assignee column, the action words that are the column's and those that are another job's.</summary>
        private void CountColumn(RowState row, AssigneeCell cell, List<TokenState> tokens)
        {
            var key = (row.Tab.Order, cell.Column.Index);
            if (!this.columnTally.TryGetValue(key, out var tally))
                this.columnTally[key] = tally = new ColumnTally(row.Tab, cell.Column);
            foreach (var t in tokens)
            {
                if (t.Kind == TokenKind.Actions)
                {
                    tally.Own++;
                }
                else if (t.Kind == TokenKind.OtherJob && t.ByJob.Values.Select(r => r.OtherJob).FirstOrDefault(o => o is not null) is { } owner)
                {
                    tally.Others[owner] = tally.Others.GetValueOrDefault(owner) + 1;
                    tally.Examples.Add(t.Token.Phrase);
                }
            }
        }

        /// <summary>
        /// A column whose action words are mostly another job's (a header naming the wrong job, "Scholar" over a White Mage's
        /// plan) turns into notes cell by cell; one warning names the job its words belong to.
        /// </summary>
        private void ReportColumns()
        {
            foreach (var tally in this.columnTally.Values)
            {
                var others = tally.Others.Values.Sum();
                if (others < 3 || others <= tally.Own)
                    continue;
                var owner = tally.Others.OrderByDescending(kv => kv.Value).First().Key;
                var header = tally.Column.Assignee!;
                this.Diag(DiagnosticSeverity.Warning, "column-job",
                    $"Most action words in the '{header.Key}' column are another job's ({others} of {others + tally.Own}, mostly {owner}: {string.Join(", ", tally.Examples.Distinct().Take(3).Select(e => "'" + e + "'"))}), so they were kept as notes. Check the column header: is this the {owner}'s column?",
                    tally.Tab.Layout.Tab.Ref(tally.Tab.Layout.HeaderRow, tally.Column.Index));
            }
        }

        /// <summary>The action words of an assignee column (see <see cref="ReportColumns"/>).</summary>
        private sealed class ColumnTally(TabState tab, ColumnLayout column)
        {
            public TabState Tab { get; } = tab;

            public ColumnLayout Column { get; } = column;

            public int Own { get; set; }

            public Dictionary<Job, int> Others { get; } = [];

            public List<string> Examples { get; } = [];
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
                text = Regex.Replace(text, @"\s+", " ").Trim(' ', ',', ';', ':', '-', '/', '.');
                if (text.Length > 0)
                    yield return text;
            }
        }

        private void ImportGroup(RowState row, AssigneeCell cell, string? key, ParsedItem item, List<TokenState> group, int groupIndex, ItemShared shared, IReadOnlyList<Job> candidates, AssigneeHeader header)
        {
            // The tokens of a group share their timing, except in a sequence, whose hint may sit on any step
            // ("Protraction + Recit → Adlo → Deploy (≈3:04, just before P3 ends)").
            var timing = group.Select(t => t.Timing).FirstOrDefault(t => !t.IsNone) ?? group[0].Timing;
            var condition = group.Select(t => t.Timing.JobCondition).FirstOrDefault(c => c is not null);
            var jobs = condition is { } only ? candidates.Where(j => j == only).ToList() : candidates.ToList();
            if (jobs.Count == 0)
            {
                this.Diag(DiagnosticSeverity.Info, "job-condition", $"'{item.Text}' is only for {condition}, which the {header.Key} column is not; not imported.", cell.Src);
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
            else if (header.Slot is not null && withActions.Count == jobs.Count && withActions.Select(p => Signature(p.Items, p.Seq)).Distinct().Count() == 1)
            {
                // The same actions for every job of the slot (a role action such as Reprisal or Feint). An action only some
                // of the slot's jobs have (Addle in a ranged slot: casters only) gets per-job entries, so that the others
                // (a Bard in R2) are not reminded of it.
                var p = withActions[0];
                entries = [(null, p.Items, p.Seq, p.Actions)];
            }
            else
            {
                entries = withActions.Select(p => ((Job?)p.Job, p.Items, p.Seq, p.Actions)).ToList();
            }

            var anchor = timing.Anchor ?? row.Mechanic!;
            var target = item.IsSequence
                ? group.Select(t => t.Token.Target?.Value).FirstOrDefault(v => v is not null)
                : group.Select(t => t.Target?.Value).FirstOrDefault(v => v is not null);
            var footnote = group.Any(t => t.Token.Footnote) ? this.Footnote(row, cell, group) : null;
            var notesText = this.EntryNote(group, footnote, groupIndex == 0 ? shared.StepNotes : []);
            var detail = this.DetailFor(row, cell);
            var conditional = group.Any(t => t.Condition is not null) || shared.Condition is not null;
            var optional = group.All(t => t.Token.Optional) || conditional || shared.Optional;
            var whenReady = group.Any(t => t.Token.WhenReady);
            var lastHit = Math.Max(0, anchor.Hits.Count - 1);
            var part = 0;

            // One entry per time of the item (a list such as "Kera 0:28 / 0:37" is pressed at each) and per job. Times that
            // cannot be placed (no row time and no phase zero point) give one automatic entry, not one copy per time.
            var times = shared.Times;
            var zero = row.PhaseId is { } ph && this.zeros.TryGetValue(ph, out var z) ? z : (double?)null;
            var placed = times.Select(t => (Time: (double?)t, Press: RowTiming.ToTimeline(t, row.Refs, zero))).ToList();
            if (placed.Count > 0 && placed.All(p => p.Press is null))
            {
                this.Diag(DiagnosticSeverity.Warning, "untimed", $"'{Flat(item.Text)}': {(times.Count == 1 ? "the time" : "the times")} {string.Join(" / ", times.Select(ParsedTime.Format))} cannot be placed (the row has no time and {row.PhaseId?.ToUpperInvariant() ?? "the phase"} no zero point); timed automatically instead.", cell.Src);
                placed = [];
            }

            // A target that names players by their order in a multi-hit mechanic ("your 3rd/4th-in-line player" on the four
            // Program Loop sets) means the hits of those players: the entry is for the last of them, pressed before it.
            var ordinalHit = placed.Count == 0 && !timing.After && timing.HitIndex is null ? OrdinalHit(target, anchor) : null;

            List<(double? Time, double? Press)> runs = placed.Count > 0 ? placed : [(null, null)];
            foreach (var (time, timedPress) in runs)
            {
                var press = timedPress;
                if (time is null && timing.After)
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
                    else if ((timing.HitIndex ?? ordinalHit) is { } hit && hit > 0 && hit <= lastHit)
                    {
                        // A part of the mechanic ("Spreadlo Stack") or a player's hit ("your 4th-in-line player"): that hit,
                        // with the automatic offset, except that an action normally pressed after its hit (a heal) is pressed
                        // before the player's hit like the item's other actions.
                        entry.HitIndex = hit;
                        if (ordinalHit is not null && IsAfterHit(timingAction))
                            entry.Offset = PreHitLead(actions, this.level);
                    }

                    var estimate = anchor.HitAt(entry.HitIndex ?? 0) + (entry.Offset ?? TimingRules.DefaultOffset(timingAction, this.level));
                    this.drafts.Add(new Draft(entry, estimate, row.Tab.Order, row.Row.Index, cell.Column.Index, item.Index, (groupIndex * 100) + part));
                    part++;
                }
            }
        }

        /// <summary>
        /// The hit a target names by order ("your 3rd/4th-in-line player", "#4", "2nd tower") on a mechanic with at least that
        /// many hits: the last one named (0-based), or null.
        /// </summary>
        private static int? OrdinalHit(string? target, Mechanic anchor)
        {
            if (target is null || anchor.Hits.Count < 2)
                return null;
            var ordinals = OrdinalNumbers().Matches(target).Select(m => int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture)).ToList();
            if (ordinals.Count == 0 || ordinals.Any(n => n < 1 || n > anchor.Hits.Count))
                return null;
            return ordinals.Max() - 1;
        }

        [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:#(?<n>\d{1,2})|(?<n>\d{1,2})(?:st|nd|rd|th))(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex OrdinalNumbers();

        /// <summary>The automatic offset of the entry's earliest pre-hit action (a shield's lead), else the default lead.</summary>
        private static double PreHitLead(List<ActionInfo> actions, int level) =>
            actions.Where(a => !IsAfterHit(a)).Select(a => TimingRules.DefaultOffset(a, level)).Where(o => o < 0).DefaultIfEmpty(TimingRules.DefaultOffset(null, level)).Min();

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

        private string? EntryNote(List<TokenState> group, string? footnote, IReadOnlyList<string> stepNotes)
        {
            var parts = new List<string>();
            var withNotes = group.Where(t => t.Notes.Count > 0).ToList();
            foreach (var t in withNotes)
            {
                var text = string.Join("; ", t.Notes);
                parts.Add(group.Count > 1 && t.Token.Head.Length > 0 ? t.Token.Head + " " + text : text);
            }

            parts.AddRange(stepNotes);
            if (footnote is not null)
                parts.Add(footnote);
            var note = string.Join("; ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
            return note.Length == 0 ? null : note;
        }

        /// <summary>
        /// The footnote for the "*" markers of a group. A marker in a colour pairs with the footnote whose "*" has that colour
        /// (LPDU P6: the red "Rep*" and "Feint*" with the red "*Press 10s Mit …", the plain "puddle*" with the plain
        /// "*WAR/PLD use Mit …"); otherwise the footnote in the cell's column, else the tab's only footnote. A footnote's
        /// continuation lines (the lines below it in its column that start in lower case) are part of it.
        /// </summary>
        private string? Footnote(RowState row, AssigneeCell cell, List<TokenState> group)
        {
            var layout = row.Tab.Layout;
            var footnotes = layout.Footnotes;
            if (footnotes.Count == 0)
                return null;
            var marker = group.Where(t => t.Token.Footnote).Select(t => MarkerColor(t.Token.Text, t.Token.Runs, SheetColors.Normalize(cell.Cell.Fg))).FirstOrDefault(c => c is not null);
            var sameColor = footnotes.Where(f => string.Equals(FootnoteColor(layout, f), marker, StringComparison.OrdinalIgnoreCase)).ToList();
            var chosen = sameColor.FirstOrDefault(f => f.Column == cell.Column.Index) ?? (sameColor.Count == 1 ? sameColor[0] : null)
                ?? footnotes.FirstOrDefault(f => f.Column == cell.Column.Index) ?? (footnotes.Count == 1 ? footnotes[0] : null);
            return chosen is null ? null : FootnoteText(layout, chosen);
        }

        /// <summary>The colour of the first "*" of a text when it differs from the cell's own text colour, else null (plain).</summary>
        private static string? MarkerColor(string text, IReadOnlyList<TextRun> runs, string? cellColor)
        {
            var star = text.IndexOf('*', StringComparison.Ordinal);
            if (star < 0)
                return null;
            var run = runs.Where(r => r.Start <= star).OrderBy(r => r.Start).LastOrDefault();
            var color = run is null ? cellColor : SheetColors.Normalize(run.Fg) ?? cellColor;
            return color is null || string.Equals(color, cellColor, StringComparison.OrdinalIgnoreCase) ? null : color;
        }

        /// <summary>The colour of a footnote's "*" (see <see cref="MarkerColor"/>), from the footnote cell's formatting.</summary>
        private static string? FootnoteColor(TabLayout layout, RowText footnote)
        {
            if (FootnoteCell(layout, footnote) is not { } at)
                return null;
            var cell = layout.Tab.Cell(at.Row, at.Col);
            var raw = cell.Text ?? string.Empty;
            return MarkerColor(raw, cell.Runs, SheetColors.Normalize(cell.Fg));
        }

        /// <summary>A footnote with its continuation lines: the next rows' texts in its column that start in lower case.</summary>
        private static string FootnoteText(TabLayout layout, RowText footnote)
        {
            var text = footnote.Text.Trim();
            if (FootnoteCell(layout, footnote) is not { } at)
                return text;
            for (var r = at.Row + 1; RowAt(layout, r) is { Kind: RowKind.Note or RowKind.Footnote } next; r++)
            {
                if (next.Texts.FirstOrDefault(t => t.Column == at.Col) is not { } line || line.Text.Trim() is not { Length: > 0 } trimmed || !char.IsLower(trimmed[0]))
                    break;
                text += " " + trimmed;
            }

            return text;
        }

        /// <summary>The layout of the tab's row with this index, or null.</summary>
        private static RowLayout? RowAt(TabLayout layout, int index) => layout.Rows.FirstOrDefault(r => r.Index == index);

        /// <summary>The row and column of a footnote text.</summary>
        private static (int Row, int Col)? FootnoteCell(TabLayout layout, RowText footnote)
        {
            foreach (var row in layout.Rows)
            {
                if (row.Texts.Contains(footnote))
                    return (row.Index, footnote.Column);
            }

            return null;
        }

        /// <summary>
        /// Times of later sequence steps that differ from the time the entry is pressed at: "Protraction + Recit (2:44–2:46) →
        /// Adlo (2:50)" is one entry at 2:44, noted "Adlo 2:50".
        /// </summary>
        private static List<string> StepTimeNotes(ParsedItem item)
        {
            var notes = new List<string>();
            if (!item.IsSequence || ItemTimes(item) is not { Count: > 0 } used)
                return notes;
            foreach (var step in item.Steps.Skip(1))
            {
                if (step.Time is not { } time || Math.Abs(time.Seconds - used[0]) < 0.01)
                    continue;
                var heads = string.Join(" + ", step.Tokens.Select(t => t.Head.Length > 0 ? t.Head : t.Phrase).Where(h => h.Length > 0));
                notes.Add((heads.Length > 0 ? heads + " " : string.Empty) + time.Text.Trim());
            }

            return notes;
        }

        /// <summary>The detail of a cell's entries: the row's notes column, then the notes written for the cell's column in its section.</summary>
        private string? DetailFor(RowState row, AssigneeCell cell)
        {
            var lines = new List<string>(row.Detail);
            var key = (row.Tab, row.Section, cell.Column.Index);
            if (this.columnNotes.TryGetValue(key, out var notes))
            {
                lines.AddRange(notes.Where(n => !lines.Contains(n)));
                this.columnNoteUse.Add(key);
            }

            return lines.Count > 0 ? string.Join("\n", lines) : null;
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
                if (mentions && WhoColon().Match(sentence) is { Success: true } colon && colon.Groups["who"].Value.Split(' ').Any(TargetNouns.Contains))
                {
                    // "tank: Aquaveil + Benison": the player before the colon is the target of the actions after it.
                    var who = colon.Groups["who"].Value;
                    return who.Length > 1 && char.IsUpper(who[0]) && who.Skip(1).All(char.IsLower) ? who.ToLowerInvariant() : who;
                }

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
