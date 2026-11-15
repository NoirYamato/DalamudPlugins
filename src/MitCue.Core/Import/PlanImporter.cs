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
/// hint applies (<see cref="HintParser"/>), or their action is not ready again at the automatic press: they are then
/// pressed when it is (the recast fit, <see cref="Run.FitRecasts"/>).
/// </para>
/// </remarks>
public sealed partial class PlanImporter
{
    /// <summary>Seconds after the hit for "after 1st Hit" / "after &lt;mechanic&gt;" hints without a time.</summary>
    public const double AfterHitOffset = 0.5;

    /// <summary>
    /// The offset of an instant action marked "(Late)" that is pressed before its hit ("Reprisal (Late)"): just before the
    /// hit, so that a 10 s Reprisal at Mega Holy 1 also covers Mega Holy 2, 7.1 s later. An entry with a cast is pressed
    /// its longest cast earlier, so that the cast ends this long before the hit ("Succor (Late)": −3 s).
    /// </summary>
    public const double LateOffset = -1.0;

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

    /// <summary>
    /// A note merged across several player columns about using the actions it names: it names actions and no player ("A
    /// tip for Soil/Kera timing …" over every column). It is the detail of the entries of those columns, in its section,
    /// that press one of <see cref="Actions"/>.
    /// </summary>
    /// <param name="Note">The note.</param>
    /// <param name="Columns">The player columns it spans that are imported.</param>
    /// <param name="Others">The player columns it spans that are not imported.</param>
    /// <param name="Text">Its text.</param>
    /// <param name="Actions">The actions it names (<see cref="Run.NamedActions"/>).</param>
    private sealed record BannerNote(NoteText Note, List<int> Columns, List<int> Others, string Text, HashSet<uint> Actions)
    {
        /// <summary>Entries it is the detail of.</summary>
        public int Uses { get; set; }

        /// <summary>The actions it names that those entries press.</summary>
        public HashSet<uint> Pressed { get; } = [];
    }

    /// <summary>A token of an item with its resolution for every candidate job.</summary>
    private sealed class TokenState
    {
        public required ParsedToken Token { get; init; }

        public required Dictionary<Job, TokenResolution> ByJob { get; init; }

        /// <summary>The kind over all candidate jobs: Actions when any job resolves it.</summary>
        public required TokenKind Kind { get; init; }

        /// <summary>The last token of its item: its parentheticals (hint, target, condition) speak for the whole item.</summary>
        public bool IsLast { get; init; }

        /// <summary>A parenthetical named the jobs' own actions ("Party Mit (DM/HoL)"): the token says whose it is.</summary>
        public bool JobsNamed { get; init; }

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

        /// <summary>The text the token keeps as a note: its words and parentheticals, with a target its entry does not carry ("Earthly Star (melee)").</summary>
        public string NoteText => NoteTextOf(this.Token, this.Target is null ? this.Token.Target : null);

        /// <summary>A target written after the action ("Short CD to R1", "Intervention Co-Tank"), as opposed to one in parentheses.</summary>
        public ParsedTarget? InlineTarget =>
            this.Token.Target is { } t && this.Token.Trailing is { } trailing && trailing.Contains(t.Text, StringComparison.OrdinalIgnoreCase) ? t : null;

        /// <summary>The token's own target: inline, or in parentheses on a token that is not the item's last.</summary>
        public ParsedTarget? OwnTarget => this.InlineTarget ?? (this.IsLast ? null : this.Token.Target);

        /// <summary>
        /// The target of the token's entry: one written after it; one in parentheses (its own, else the one after the item's
        /// last token) only when an action of it is used on a player.
        /// </summary>
        public ParsedTarget? Target { get; set; }

        /// <summary>Some job's action of the token is used on one player (a target makes sense for it; not Reprisal, Holmgang).</summary>
        public bool OnAPlayer => this.ByJob.Values.Any(r => r.Kind == TokenKind.Actions && r.Alternatives.Any(a => a.Actions.Any(x => x.Target == ActionTarget.Single)));

        /// <summary>
        /// An either/or with a choice of several actions ("Spread-Lo/Soil": Adloquium → Deployment Tactics, or Sacred
        /// Soil), which one plan item cannot hold: each choice is an optional entry of its own.
        /// </summary>
        public bool SplitChoices => this.ByJob.Values.Any(r => r.Kind == TokenKind.Actions && r.Alternatives.Count > 1 && r.Alternatives.Any(a => a.Actions.Count > 1 || a.Groups is not null));
    }

    /// <summary>An entry before sorting, with its sort keys.</summary>
    /// <param name="Entry">The entry.</param>
    /// <param name="Press">Its estimated press time (timeline seconds).</param>
    /// <param name="Tab">Sort key: the tab.</param>
    /// <param name="Row">Sort key: the row.</param>
    /// <param name="Column">Sort key: the column.</param>
    /// <param name="Item">Sort key: the item of the cell.</param>
    /// <param name="Part">Sort key: the entry of the item.</param>
    /// <param name="Confident">The row's label matched its mechanic (not a stand-in, the row above's or a guess from its spelling).</param>
    /// <param name="Written">
    /// The sheet gives the press time: a time written in the cell, or a hint that names it ("after 1st Hit"). The recast
    /// fit never moves it (<see cref="Run.FitRecasts"/>).
    /// </param>
    private sealed record Draft(PlanEntry Entry, double Press, int Tab, int Row, int Column, int Item, int Part, bool Confident = false, bool Written = false);

    /// <summary>An entry the recast fit presses later (<see cref="Run.FitRecasts"/>).</summary>
    /// <param name="Use">The reminder of the press that finds no charge (its entry, hit and press time).</param>
    /// <param name="Action">The action that has no charge then.</param>
    /// <param name="Offset">The entry's new offset.</param>
    /// <param name="From">The entry's offset before the recast fit moved it.</param>
    /// <param name="Earlier">The earlier press whose recast it waits for.</param>
    /// <param name="EarlierAction">That press's action (the same action, or one that shares its recast).</param>
    /// <param name="Charges">The charges of the action at the sync level.</param>
    /// <param name="AtLate">The new press is the latest "(Late)" allows: the action is ready less than the due lead before it.</param>
    private sealed record RecastMove(Reminder Use, ActionInfo Action, double Offset, double From, Reminder Earlier, ActionInfo EarlierAction, int Charges, bool AtLate)
    {
        public PlanEntry Entry => this.Use.Entry;
    }

    /// <summary>One entry of a group of tokens, before its timing (<see cref="Run.ImportGroup"/>).</summary>
    /// <param name="Job">The job it is for, or null for every job of the column.</param>
    /// <param name="Items">What to press.</param>
    /// <param name="Seq">The items are pressed in order.</param>
    /// <param name="Actions">The actions of the items.</param>
    /// <param name="Choice">For one choice of an either/or that is an entry of its own: "or …" (the other choices); the entry is optional.</param>
    private sealed record EntryParts(Job? Job, List<PlanItem> Items, bool Seq, List<ActionInfo> Actions, string? Choice = null);

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

        /// <summary>Words that name players by role or slot, written alone or in "co-tank", "off-tank", "MT/OT" (<see cref="NamesAPlayer"/>).</summary>
        private static readonly HashSet<string> PlayerWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "tank", "tanks", "cotank", "cotanks", "offtank", "offtanks", "maintank", "healer", "healers", "cohealer", "melee",
            "melees", "ranged", "caster", "casters", "dps", "mt", "ot", "st", "t1", "t2", "h1", "h2", "m1", "m2", "r1", "r2", "d1",
            "d2", "d3", "d4",
        };

        /// <summary>The reminder settings a plan is timed for: the defaults (the "NOW" cue 1.5 s before the press).</summary>
        private static readonly ReminderOptions DefaultReminders = new();

        /// <summary>The settings of <c>check</c>, whose recast model the recast fit follows (<see cref="PlanCheckOptions.MinRecast"/>).</summary>
        private static readonly PlanCheckOptions CheckDefaults = new();

        /// <summary>Half the 0.01 s offsets are written to (<see cref="ImportText.Round"/>): times this close are equal.</summary>
        private const double OffsetPrecision = 0.005;

        /// <summary>Times this close are one (a press and the moment a charge is ready again).</summary>
        private const double SameTime = 1e-6;

        /// <summary>A guard for <see cref="FitRecasts"/>: every round moves an entry later, up to "(Late)".</summary>
        private const int MaxFitRounds = 1000;

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
        private readonly List<BannerNote> bannerNotes = [];
        private readonly Dictionary<(TabState Tab, int Row, int Column), List<string>> caretRemarks = [];

        /// <summary>
        /// The limit breaks written in columns that are not imported: a limit break the sheet writes in several columns is
        /// shared whichever of them are imported (<see cref="MarkSharedLimitBreaks"/>).
        /// </summary>
        private readonly List<(string Mechanic, Role Role, string Column, string? Slot)> otherLimitBreaks = [];
        private CellParser cells = CellParser.Default;
        private SlashReading documentSlashes = SlashReading.Either;
        private bool seraphNumbered;
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
            this.cells = this.ChooseSlashReading(layout);
            this.ChooseSeraphReading(layout, tabs);

            foreach (var tab in tabs)
            {
                this.CollectRows(tab);
                this.AttachCarets(tab);
            }

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
                DocumentSlashes = this.documentSlashes,
                SeraphNumbered = this.seraphNumbered,
                Rows = rows.Select(r => new ImportedRow(r.Tab.Layout.Name, r.Row.Index + 1, r.Row.Label, r.PhaseId, r.Mechanic?.Id, r.Match, r.Row.Src)).ToList(),
                AnyoneColumns = tabs.SelectMany(t => t.Layout.AssigneeColumns.Where(c => c.Assignee!.Kind == AssigneeKind.Anyone).Select(c => new AnyoneColumn(t.Layout.Name, c.Index, c.Header ?? c.Assignee!.Key)))
                    .Distinct().ToList(),
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
            // Columns for anyone ("Extras") hold no player: they make no composition variant and are never re-keyed.
            static IEnumerable<ColumnLayout> Players(TabLayout layout) => layout.AssigneeColumns.Where(c => c.Assignee!.Kind != AssigneeKind.Anyone);
            var keysByTab = tabs.ToDictionary(t => t, t => Players(t.Layout).Select(c => c.Assignee!.Key).ToHashSet(StringComparer.OrdinalIgnoreCase));
            foreach (var tab in tabs.ToList())
            {
                var layout = tab.Layout;
                var collides = tabs.Where(o => o != tab).Any(o => keysByTab[o].Overlaps(keysByTab[tab]));
                var composition = layout.NameJobs.Count >= 2;
                var clean = composition && Players(layout).All(c => c.Assignee!.Kind == AssigneeKind.Job && c.Assignee.Job is { } j && layout.NameJobs.Contains(j));
                if (composition && collides && !clean)
                {
                    this.Diag(DiagnosticSeverity.Warning, "tab-skipped", $"Tab '{layout.Name}' names a team composition, but its columns ({string.Join(", ", keysByTab[tab])}) are not one per job of it, so they cannot be told apart from the same columns of other tabs; the tab is not imported.", layout.Tab.Ref(layout.HeaderRow, 0));
                    tabs.Remove(tab);
                    continue;
                }

                var rekey = clean && collides;
                foreach (var column in layout.AssigneeColumns)
                    tab.Keys[column.Index] = rekey && column.Assignee!.Kind != AssigneeKind.Anyone ? layout.Name + " " + column.Assignee!.Key : column.Assignee!.Key;
                if (rekey)
                    this.Diag(DiagnosticSeverity.Info, "assignee-rekeyed", $"Tab '{layout.Name}' is one composition variant; its columns are keyed {string.Join(", ", tab.Keys.Values.Select(k => "'" + k + "'"))}.", layout.Tab.Ref(layout.HeaderRow, 0));
            }

            for (var i = 0; i < tabs.Count; i++)
                tabs[i].Order = i;
        }

        private bool AssigneeSelected(string key, string header) =>
            this.options.Assignees is not { } wanted
            || wanted.Any(w => string.Equals(w, key, StringComparison.OrdinalIgnoreCase) || string.Equals(w, header, StringComparison.OrdinalIgnoreCase));

        /// <summary>The column is imported: it is chosen (<see cref="ImportOptions.Assignees"/>), or it holds calls for anyone ("Extras"), which go by job.</summary>
        private bool ColumnSelected(string key, AssigneeHeader header) => header.Kind == AssigneeKind.Anyone || this.AssigneeSelected(key, header.Key);

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

        /// <summary>
        /// What "/" between two actions of one job means in this document (<see cref="ImportOptions.Slashes"/>), reported as a
        /// "slash-reading" diagnostic. Automatic: "and" (<see cref="CellParserOptions.SlashJoins"/>) when no assignment cell
        /// joins actions any other way (<see cref="ParsedCell.Joins"/>: "+", ",", "&amp;", arrows, a spaced " / ", line breaks),
        /// and at least two cells list several actions of one job with "/", one of them three or more
        /// ("Soil/Reci/Deploy/Whisper"): a sheet that writes "TBN/Oblation" next to "Living Dead + …" means either one. Every
        /// plan tab counts, whichever tabs and columns are imported, so that a re-import of one column reads the sheet the
        /// same way.
        /// </summary>
        private CellParser ChooseSlashReading(DocumentLayout layout)
        {
            var (joined, listed, longest) = (0, 0, 0);
            string? joinExample = null;
            string? listExample = null;
            foreach (var row in layout.PlanTabs.SelectMany(t => t.DataRows))
            {
                foreach (var cell in row.Assignments)
                {
                    var parsed = CellParser.Default.Parse(cell.Cell, cell.Src);
                    if (parsed.Above is not null)
                        continue;
                    if (parsed.Joins > 0)
                    {
                        joined++;
                        joinExample ??= $"{cell.Src} '{Flat(parsed.Text)}'";
                    }

                    // The most alternatives of one token that are actions of one job the column can stand for.
                    var jobs = CandidateJobs(cell.Assignee);
                    var most = parsed.Tokens.Where(t => t.Alternatives.Count >= 2)
                        .Select(t => jobs.Select(j => t.Alternatives.Count(a => this.resolver.Resolve(t with { Alternatives = [a] }, j).Kind == TokenKind.Actions)).DefaultIfEmpty(0).Max())
                        .DefaultIfEmpty(0).Max();
                    if (most < 2)
                        continue;
                    listed++;
                    if (most > longest)
                        (longest, listExample) = (most, $"{cell.Src} '{Flat(parsed.Text)}'");
                }
            }

            var auto = joined == 0 && listed >= 2 && longest >= 3 ? SlashReading.All : SlashReading.Either;
            var reading = this.options.Slashes == SlashReading.Auto ? auto : this.options.Slashes;
            this.documentSlashes = auto;
            const string Others = "'+', ',', '&', an arrow, ' / ' or a line break";
            var counts = (listed == 0 ? "no cell lists several actions of one job with '/'" : $"{Count(listed, "cell lists", "cells list")} several actions of one job with '/', up to {longest} (e.g. {listExample})")
                + "; " + (joined == 0 ? $"no cell joins actions another way ({Others})" : $"{Count(joined, "cell joins", "cells join")} actions another way ({Others}; e.g. {joinExample})");
            var meaning = reading == SlashReading.All ? "'and' (press all of them, like '+')" : "'either one'";
            var message = this.options.Slashes != SlashReading.Auto
                ? $"'/' between actions of one job is read as {meaning}, as chosen in the import options{(auto == reading ? string.Empty : " (the sheet alone reads as " + (auto == SlashReading.All ? "'and'" : "'either one'") + ")")}: {counts}."
                : reading == SlashReading.All
                    ? $"'/' between actions of one job is read as {meaning}: {counts}. If the sheet means either one, set Slashes to 'either one' in the import options (CLI: --slashes or)."
                    : $"'/' between actions of one job is read as {meaning}: {counts}. If the sheet means all of them, set Slashes to 'all of them' in the import options (CLI: --slashes and).";
            this.Diag(DiagnosticSeverity.Info, "slash-reading", message, null);
            return reading == SlashReading.All ? new CellParser(new CellParserOptions { SlashJoins = true }) : CellParser.Default;

            static string Count(int n, string one, string many) => n.ToString(CultureInfo.InvariantCulture) + " " + (n == 1 ? one : many);
        }

        /// <summary>
        /// What a plain "Seraph" means in this document. A sheet that numbers its Seraph calls ("Seraph1": Summon Seraph and
        /// its first Consolation, "Seraph2": the second) means the same by a plain "Seraph" (LPDU's TEA: "Succor/Soil/Seraph"
        /// at J Wave 5, "Seraph/Fey Illum" at J Wave 7): it is read like "Seraph1", and a second one while Seraph is out is
        /// the other Consolation (<see cref="MarkStillActive"/>). Otherwise it is Summon Seraph alone (Carry the Clear writes
        /// its Consolations apart). Every plan tab counts, whichever tabs and columns are imported; a "seraph-reading" info
        /// names an imported cell that it changes, and <see cref="ImportResult.SeraphNumbered"/> says so for the AI prompt.
        /// </summary>
        private void ChooseSeraphReading(DocumentLayout layout, List<TabState> tabs)
        {
            string? numbered = null;
            string? plain = null;
            foreach (var tab in layout.PlanTabs)
            {
                var imported = tabs.FirstOrDefault(t => ReferenceEquals(t.Layout, tab));
                foreach (var cell in tab.DataRows.SelectMany(r => r.Assignments))
                {
                    var parsed = this.cells.Parse(cell.Cell, cell.Src);
                    foreach (var key in parsed.Tokens.SelectMany(t => t.Alternatives).Select(a => this.options.Aliases.MatchLongest(a)?.Key))
                    {
                        if (key is "seraph1" or "seraph 1" or "seraph2" or "seraph 2")
                            numbered ??= $"{cell.Src} '{Flat(parsed.Text)}'";
                        else if (key == "seraph" && imported is not null && this.ColumnSelected(imported.Keys.GetValueOrDefault(cell.Column.Index, cell.Assignee.Key), cell.Assignee))
                            plain ??= $"{cell.Src} '{Flat(parsed.Text)}'";
                    }
                }
            }

            if (numbered is null)
                return;
            this.seraphNumbered = true;
            this.resolver.ReadAs = new Dictionary<string, string>(StringComparer.Ordinal) { ["seraph"] = "seraph1" };
            if (plain is not null)
            {
                this.Diag(DiagnosticSeverity.Info, "seraph-reading",
                    $"The sheet numbers its Seraph calls (e.g. {numbered}), so a plain 'Seraph' (e.g. {plain}) is read like 'Seraph1': Summon Seraph, then Consolation.",
                    null);
            }
        }

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
        /// Cells that point up ("(^on LL)", "(^ if MCH)", "^ on BJ") are remarks about the nearest other cell above them in
        /// the same column (the same players: no repeated header in between) and phase section: their words are read as a
        /// parenthetical of each item of that cell ("Addle (on LL)": a note; "(Party Mit (if MCH))": a job filter; a target, a
        /// timing or a condition likewise; <see cref="WithRemarks"/>). They are no entry of their own; a caret with nothing
        /// above it or no words is not imported.
        /// </summary>
        private void AttachCarets(TabState tab)
        {
            for (var i = 0; i < tab.Rows.Count; i++)
            {
                var row = tab.Rows[i];
                foreach (var cell in row.Row.Assignments)
                {
                    if (CellParser.CaretRemark(cell.Cell.Text) is not { } remark
                        || !this.ColumnSelected(tab.Keys.TryGetValue(cell.Column.Index, out var k) ? k : cell.Assignee.Key, cell.Assignee))
                        continue;
                    (RowState Row, AssigneeCell Cell)? above = null;
                    for (var r = i - 1; r >= 0 && above is null && Equals(tab.Rows[r].Section, row.Section); r--)
                    {
                        if (tab.Rows[r].Row.Assignments.FirstOrDefault(a => ReferenceEquals(a.Column, cell.Column) && CellParser.CaretRemark(a.Cell.Text) is null) is { } found)
                            above = (tab.Rows[r], found);
                    }

                    if (above is not { } target || remark.Length == 0)
                    {
                        this.Diag(DiagnosticSeverity.Warning, "caret-note",
                            $"'{Flat(cell.Cell.Text)}' points at the cell above it, but {(remark.Length == 0 ? "says nothing about it" : "its column has no cell above it in this section")}; not imported.",
                            cell.Src);
                        continue;
                    }

                    var key = (tab, target.Row.Row.Index, target.Cell.Column.Index);
                    if (!this.caretRemarks.TryGetValue(key, out var remarks))
                        this.caretRemarks[key] = remarks = [];
                    remarks.Add(remark);
                    this.Diag(DiagnosticSeverity.Info, "caret-note",
                        $"'{Flat(cell.Cell.Text)}' is a remark about {target.Cell.Src} above it ('{Flat(target.Cell.Cell.Text)}'), read as '{Flat(this.WithRemarks(target.Cell.Cell, [remark]).Text)}'.",
                        cell.Src);
                }
            }
        }

        /// <summary>
        /// A cell with remarks added as parentheticals after each of its items, so that they speak for all of the cell
        /// ("Addle" + "on LL" → "Addle (on LL)"; "Soil / Expedient" + "late" → "Soil (late) / Expedient (late)"), inside the
        /// brackets of a cell that is all in brackets ("(Party Mit)" + "if MCH" → "(Party Mit (if MCH))").
        /// </summary>
        private SheetCell WithRemarks(SheetCell cell, IReadOnlyList<string> remarks)
        {
            var text = (cell.Text ?? string.Empty).TrimEnd();
            var added = string.Concat(remarks.Select(r => " (" + r + ")"));

            // After the last token of every item, from the last item back, so that the earlier positions stay (a cell in
            // brackets is parsed with its brackets blanked: the positions are the text's).
            var ends = this.cells.Parse(text).Items.Where(i => i.Tokens.Any()).Select(i => i.Tokens.Max(t => t.Start + t.Text.Length))
                .Distinct().OrderDescending().ToList();
            if (ends.Count == 0)
                ends = [CellParser.WholeBrackets(text) is var (_, close) ? close : text.Length];
            var runs = cell.Runs.ToList();
            foreach (var at in ends)
            {
                text = text[..at] + added + text[at..];
                runs = runs.Select(r => r.Start >= at ? r with { Start = r.Start + added.Length } : r).ToList();
            }

            return new SheetCell
            {
                Text = text,
                Bg = cell.Bg,
                Fg = cell.Fg,
                Bold = cell.Bold,
                Strike = cell.Strike,
                Runs = runs,
                Note = cell.Note,
            };
        }

        /// <summary>
        /// Notes written in a player's column under a table or between its rows (LPDU: "Shields bevor the boss is targetable"
        /// under the H2 SCH column, "WAR can use Shake as P3 dies for 1st P4 Proteans."). A footnote ("*…", with its lines
        /// that continue in lower case) belongs to the items marked "*". A note that names an action with a timing hint is
        /// imported as an entry (optional when it says "can"/"may"); any other note becomes the detail of the column's entries
        /// in its section (of every column it spans when merged). A note merged across several columns that names actions and
        /// no player ("A tip for Soil/Kera timing …") is the detail of the entries of those columns in its section that press
        /// one of them (<see cref="BannerNote"/>), and of every entry of those columns when none does; one that names players
        /// ("Tanks: Rampart the first tower set, then OT provokes …") is for all of its columns' entries. A merged note reads
        /// the same whichever of its columns are imported: a Scholar who imports only their column gets the Soil/Kera tip on
        /// their Soil entries, a tank none (the entries that press what it names are in columns not imported).
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
                // The player columns the note spans (the whole merge, whichever of them are imported, so that a note reads the
                // same in an import of one column as in the whole sheet's) and those of them that are imported.
                var text = note.Text.Text.Trim();
                var layout = note.Tab.Layout;
                var merge = layout.Tab.MergeAt(note.Row.Index, note.Text.Column);
                var spanned = note.Tab.Keys.Keys.Where(c => c == note.Text.Column || (merge is { } m && m.Contains(note.Row.Index, c))).Order().ToList();
                var columns = spanned.Where(c => this.ColumnSelected(note.Tab.Keys[c], layout.Columns.First(x => x.Index == c).Assignee!)).ToList();
                if (columns.Count == 0)
                    continue;
                if (footnoteCells.Contains(note.Text.Src))
                {
                    this.Diag(DiagnosticSeverity.Info, "note-row", $"Row {note.Row.Index + 1}: \"{text}\" is a footnote; it goes to the note of the items marked '*'.", note.Text.Src);
                    continue;
                }

                if (columns.Contains(note.Text.Column) && this.ReadStatement(note) is { } statement)
                {
                    this.statements.Add(statement);
                    continue;
                }

                // Over several columns, a note about using the actions it names (it names no player) is for whoever presses
                // them; one that speaks to players ("Tanks: Rampart …, then OT provokes …") is for its columns' players.
                if (spanned.Count > 1 && !NamesAPlayer(text) && this.NamedActions(text) is { Count: > 0 } named)
                {
                    this.bannerNotes.Add(new BannerNote(note, columns, spanned.Except(columns).ToList(), text, named));
                    continue;
                }

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
            foreach (var banner in this.bannerNotes)
            {
                string Names(IEnumerable<uint> ids) => string.Join(", ", ids.Select(id => this.options.Actions.Get(id)?.Name).OfType<string>().Distinct());
                var phase = banner.Note.Row.PhaseId?.ToUpperInvariant() ?? "the tab";
                if (banner.Uses > 0)
                {
                    this.Diag(DiagnosticSeverity.Info, "note-row",
                        $"Row {banner.Note.Row.Index + 1} is a note over several columns, not an assignment: \"{banner.Text}\"; kept as the detail of the {banner.Uses} entries of {phase} in its columns that press {Names(banner.Pressed)}.",
                        banner.Note.Text.Src);
                    continue;
                }

                // No entry of its columns presses an action it names. When a cell of a column it spans that is not imported
                // names one, the entries that press it are not in this import: the note is theirs, as in the whole sheet's.
                if (this.NamedInColumnsNotImported(banner) is { Count: > 0 } elsewhere)
                {
                    this.Diag(DiagnosticSeverity.Info, "note-row",
                        $"Row {banner.Note.Row.Index + 1} is a note over several columns, not an assignment: \"{banner.Text}\"; the entries of {phase} that press {Names(elsewhere)} are in its columns that are not imported (shown for review only).",
                        banner.Note.Text.Src);
                    continue;
                }

                // Otherwise the detail of every entry of its columns, as a note in each of them would be.
                var used = 0;
                foreach (var d in this.drafts.Where(d => d.Tab == banner.Note.Tab.Order && banner.Columns.Contains(d.Column) && Equals(RowAt(banner.Note.Tab.Layout, d.Row)?.Phase, banner.Note.Row.Phase)))
                {
                    if (d.Entry.Detail?.Split('\n').Contains(banner.Text) == true)
                        continue;
                    d.Entry.Detail = d.Entry.Detail is null ? banner.Text : d.Entry.Detail + "\n" + banner.Text;
                    used++;
                }

                this.Diag(DiagnosticSeverity.Info, "note-row",
                    used > 0
                        ? $"Row {banner.Note.Row.Index + 1} is a note over several columns, not an assignment: \"{banner.Text}\"; no entry of its columns in {phase} presses an action it names ({Names(banner.Actions)}), so it is the detail of all {used} entries of its columns there."
                        : $"Row {banner.Note.Row.Index + 1} is a note over several columns, not an assignment: \"{banner.Text}\"; its columns have no entry in {phase} (shown for review only).",
                    banner.Note.Text.Src);
            }

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
            // A note in a column for anyone ("Extras") is one entry for everyone.
            var candidates = CandidateJobs(header);
            IReadOnlyList<Job?> jobs = header.Slot is null && header.Jobs.Count == 0 && candidates.Count > 1 && header.Kind != AssigneeKind.Anyone ? candidates.Select(j => (Job?)j).ToList() : [header.Job];
            var lastHit = Math.Max(0, anchor.Hits.Count - 1);
            var hit = timing.After ? Math.Clamp(timing.HitIndex ?? lastHit, 0, lastHit) : 0;
            foreach (var job in jobs)
            {
                var entry = new PlanEntry
                {
                    Mechanic = anchor.Id,
                    HitIndex = hit > 0 ? hit : null,
                    Offset = timing.After ? AfterHitOffset : null,
                    Who = new WhoSpec { Slot = header.Slot, Job = job, Assignee = header.Kind == AssigneeKind.Anyone ? null : note.Tab.Keys[column] },
                    Do = [PlanItem.ForNote(text)],
                    AtPhaseEnd = timing.AtPhaseEnd,
                    Src = note.Text.Src,
                };
                this.drafts.Add(new Draft(entry, this.PressEstimate(entry, anchor), note.Tab.Order, note.Row.Index, column, 0, 0));
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
                foreach (var item in this.cells.Parse(cell.Cell, cell.Src).Items)
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
            if (CellParser.CaretRemark(cell.Cell.Text) is not null)
                return; // a remark about the cell above (AttachCarets)
            if (!this.ColumnSelected(key, cell.Assignee))
            {
                this.NoteOtherLimitBreak(row, cell, key); // a column not chosen
                return;
            }

            if (row.Mechanic is null)
            {
                this.Diag(DiagnosticSeverity.Warning, "dropped-cell", $"'{Flat(cell.Cell.Text)}' has no mechanic to anchor to and was not imported.", cell.Src);
                return;
            }

            var source = this.caretRemarks.TryGetValue((tab, row.Row.Index, cell.Column.Index), out var remarks) ? this.WithRemarks(cell.Cell, remarks) : cell.Cell;
            var parsed = this.cells.Parse(source, cell.Src);
            foreach (var item in parsed.Items)
                this.ImportItem(row, cell, key, item);
        }

        /// <summary>
        /// A cell of a column that is not imported, when it is only limit breaks ("LB3"), for each role it is a limit break
        /// of: the imported columns' limit break at the mechanic is shared with it (<see cref="MarkSharedLimitBreaks"/>).
        /// </summary>
        private void NoteOtherLimitBreak(RowState row, AssigneeCell cell, string key)
        {
            if (row.Mechanic is null || cell.Assignee.Kind == AssigneeKind.Anyone)
                return;
            var tokens = this.cells.Parse(cell.Cell, cell.Src).Items.SelectMany(i => i.Tokens).ToList();
            if (tokens.Count == 0)
                return;
            var roles = CandidateJobs(cell.Assignee)
                .Where(job => tokens.All(t => this.resolver.Resolve(t, job) is { Kind: TokenKind.Actions } r
                    && r.Alternatives.All(a => a.Actions.Count > 0 && a.Actions.All(x => x.LimitBreakTier is not null))))
                .Select(Jobs.RoleOf)
                .Distinct();
            foreach (var role in roles)
                this.otherLimitBreaks.Add((row.Mechanic.Id, role, key, cell.Assignee.Slot));
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
                (token, byJob, var named) = this.FilterByNamedActions(token, byJob, cell.Src);
                var kind = byJob.Values.Any(r => r.Kind == TokenKind.Actions) ? TokenKind.Actions
                    : byJob.Values.Any(r => r.Kind == TokenKind.NoteOnly) ? TokenKind.NoteOnly
                    : byJob.Values.Any(r => r.Kind == TokenKind.NotAvailable) ? TokenKind.NotAvailable
                    : byJob.Values.Any(r => r.Kind == TokenKind.OtherJob) ? TokenKind.OtherJob
                    : byJob.Values.All(r => r.Kind == TokenKind.Empty) ? TokenKind.Empty
                    : TokenKind.Unresolved;
                var state = new TokenState { Token = token, ByJob = byJob, Kind = kind, IsLast = i == parsedTokens.Count - 1, JobsNamed = named };
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
                t.Target = t.InlineTarget ?? (t.OnAPlayer ? t.OwnTarget ?? itemTarget : null);
            }

            // A target in parentheses that no action is used on ("Asylum (melee)", "Kera (party)": where to place it, whom
            // it is for) stays in the note, as any other parenthetical; a sequence keeps it as its target.
            if (!item.IsSequence)
            {
                foreach (var t in tokens)
                {
                    if (t.Kind == TokenKind.Actions && t.Target is null && t.OwnTarget is { } unused)
                        t.Notes.Add(unused.Text);
                }

                if (itemTarget is not null && last.Kind == TokenKind.Actions && !tokens.Any(t => ReferenceEquals(t.Target, itemTarget)))
                    last.Notes.Add(itemTarget.Text);
            }

            // Tokens with their own timing, job filter, condition or target ("Kera after 1st Hit" next to "Eprog + Panhaima",
            // "Rep + Short CD to R1") become their own entry; the steps of a sequence stay together. In a column for anyone,
            // the calls that do not say whose they are (an action several jobs share, a note) are an entry for everyone
            // apart from those that do ("Dismantle + Feint": a Machinist's Dismantle and the note "Feint"; "PLD Wings + Use
            // Personals": a Paladin's Passage of Arms and the note "Use Personals").
            var anyone = header.Kind == AssigneeKind.Anyone;
            var groups = item.IsSequence
                ? [tokens]
                : tokens.GroupBy(t => (t.Timing.Key, t.Timing.JobCondition, t.Condition is not null, t.Target?.Value,
                        anyone && t.Timing.JobCondition is null && (t.Kind != TokenKind.Actions || SharedByJobs(t, candidates))))
                    .Select(g => g.ToList()).ToList();
            var shared = new ItemShared(
                Times: ItemTimes(item),
                Condition: itemCondition,
                StepNotes: StepTimeNotes(item),
                Optional: statement?.Optional ?? false);
            for (var g = 0; g < groups.Count; g++)
                this.ImportGroup(row, cell, key, item, groups[g], g, shared, candidates, header);
        }

        /// <summary>
        /// A parenthetical that names, per job, the token's own actions ("Party Mit (DM/HoL)": Dark Missionary for a Dark
        /// Knight, Heart of Light for a Gunbreaker) says which jobs the token is for: the other jobs get no entry (no Divine
        /// Veil or Shake It Off), and the parenthetical is no note. A parenthetical that names any other action
        /// ("Seraph (Consolation ×2)", "(+ Aquaveil)") stays a hint.
        /// </summary>
        private (ParsedToken Token, Dictionary<Job, TokenResolution> ByJob, bool Named) FilterByNamedActions(ParsedToken token, Dictionary<Job, TokenResolution> byJob, string? src)
        {
            if (token.Hints.Count == 0 || !byJob.Values.Any(r => r.Kind == TokenKind.Actions))
                return (token, byJob, false);
            foreach (var hint in token.Hints)
            {
                // Each either/or part of the parenthetical ("DM", "HoL") must be an action of some job, and every action it
                // names must be one the token stands for with that job.
                var parts = CellParser.Default.Parse(hint).Tokens.SelectMany(t => t.Alternatives.Select(a => t with { Alternatives = [a], Trailing = null })).ToList();
                if (parts.Count == 0)
                    continue;
                var named = new HashSet<Job>();
                var outside = false;
                var understood = new HashSet<int>();
                foreach (var (job, own) in byJob)
                {
                    var ownIds = own.Kind == TokenKind.Actions ? own.Alternatives.SelectMany(a => a.Actions).Select(a => a.Id).ToHashSet() : [];
                    for (var p = 0; p < parts.Count; p++)
                    {
                        var r = this.resolver.Resolve(parts[p], job);
                        if (r.Kind != TokenKind.Actions || r.Alternatives.Any(a => a.Fuzzy))
                            continue;
                        understood.Add(p);
                        if (r.Alternatives.SelectMany(a => a.Actions).All(a => ownIds.Contains(a.Id)))
                            named.Add(job);
                        else
                            outside = true;
                    }
                }

                if (named.Count == 0 || outside || understood.Count < parts.Count)
                    continue;
                if (named.Count < byJob.Count(kv => kv.Value.Kind == TokenKind.Actions))
                {
                    var which = named.Select(j => $"{j} {string.Join("/", byJob[j].Alternatives.SelectMany(a => a.Actions).Select(a => a.Name).Distinct())}");
                    this.Diag(DiagnosticSeverity.Info, "named-actions", $"'{token.Phrase} ({hint})': the parenthetical names {string.Join(", ", which)}; only {(named.Count == 1 ? "that job gets" : "those jobs get")} an entry.", src);
                }

                var filtered = byJob.ToDictionary(kv => kv.Key, kv => named.Contains(kv.Key) ? kv.Value : new TokenResolution { Kind = TokenKind.OtherJob, Matched = token.Phrase });
                return (token with { Hints = token.Hints.Where(h => h != hint).ToList() }, filtered, true);
            }

            return (token, byJob, false);
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

                    // Either/or alternatives that mean nothing for any job that has the token ("Xyzzy" in "TBN/Xyzzy").
                    var lost = resolved.Select(r => (IEnumerable<string>)r.UnresolvedAlternatives).Aggregate((a, b) => a.Intersect(b, StringComparer.OrdinalIgnoreCase)).ToList();
                    foreach (var alternative in lost)
                        this.Diag(DiagnosticSeverity.Warning, "unresolved-token", $"'{alternative}' in '{token.Phrase}' is not an action of {string.Join("/", candidates)} at level {this.level}; left out of the either/or. Fix it in review or add an alias.", cell.Src);
                    if (t.SplitChoices)
                    {
                        var several = resolved.SelectMany(r => r.Alternatives).First(a => a.Actions.Count > 1 || a.Groups is not null);
                        var what = string.Join(several.Sequence ? " → " : " + ", ItemGroups(several).Select(g => g.Count == 1 ? g[0].Name : "(" + string.Join(" or ", g.Select(a => a.Name)) + ")"));
                        this.Diag(DiagnosticSeverity.Warning, "either-or-split",
                            $"'{token.Phrase}': '{several.Text ?? several.Matched}' is several actions ({what}), which one choice of an either/or cannot hold; each choice is an optional entry of its own. Check it in review, or read '/' as 'and' (Slashes: all of them).",
                            cell.Src);
                    }

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
                    var added = b.Actions.Where(x => !a.Actions.Contains(x)).ToList();
                    var merged = a with
                    {
                        Actions = [.. a.Actions, .. added],
                        Groups = a.Groups is null && b.Groups is null ? null : [.. ItemGroups(a), .. ItemGroups(b).Where(g => g.Any(added.Contains))],
                        Sequence = a.Sequence || b.Sequence,
                        Rest = b.Rest,
                    };
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

        /// <summary>The plan items of an alternative as groups of actions: its <see cref="ResolvedAlternative.Groups"/>, else one per action.</summary>
        private static IReadOnlyList<IReadOnlyList<ActionInfo>> ItemGroups(ResolvedAlternative alternative) =>
            alternative.Groups ?? alternative.Actions.Select(a => (IReadOnlyList<ActionInfo>)[a]).ToList();

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

        /// <summary>
        /// The entries of a group of tokens that share their timing, condition and target: one for the column, or one per
        /// job when the jobs' actions differ. In a column for anyone ("Extras") a call is for the jobs whose own action it
        /// names, with no assignee; an action several jobs share ("Feint") does not say whose it is and is a note for
        /// everyone (in a group with the cell's other calls for everyone: <see cref="ImportItem"/>). An either/or with a
        /// choice of several actions ("Spread-Lo/Soil") gives one optional entry per choice.
        /// </summary>
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
            var perJob = new List<EntryParts>();
            var choices = new List<EntryParts>();
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
                            items.AddRange(ItemsOf(r.Alternatives[0]));
                            actions.AddRange(r.Alternatives[0].Actions);
                            seq |= r.Alternatives[0].Sequence;
                        }
                        else if (t.SplitChoices)
                        {
                            foreach (var choice in r.Alternatives)
                            {
                                var others = r.Alternatives.Where(a => a != choice).Select(a => a.Text ?? a.Matched);
                                choices.Add(new EntryParts(job, ItemsOf(choice).ToList(), choice.Sequence, choice.Actions.ToList(), "or " + string.Join(" or ", others)));
                            }
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

                perJob.Add(new EntryParts(job, items, seq, actions));
            }

            var withActions = perJob.Where(p => p.Actions.Count > 0).ToList();
            var entries = new List<EntryParts>();
            if (header.Kind == AssigneeKind.Anyone && group.Where(t => SharedByJobs(t, jobs)).ToList() is { Count: > 0 } common)
            {
                foreach (var t in common)
                {
                    var owners = jobs.Where(j => t.ByJob[j].Kind == TokenKind.Actions);
                    this.Diag(DiagnosticSeverity.Warning, "anyone-role-action",
                        $"'{t.NoteText}' in the '{header.Key}' column is an action of {string.Join("/", owners)}: it does not say whose, so it is a note for everyone. Name the job (\"DRK Reprisal\") or move it to a player's column.",
                        cell.Src);
                }

                // The action words; what is written after them or in parentheses is in the entry's note, as for an action.
                var words = group.Select(t => t.Kind == TokenKind.Actions && t.Token.Head.Length > 0 ? t.Token.Head : t.NoteText);
                entries.Add(new EntryParts(null, [PlanItem.ForNote(string.Join(item.IsSequence ? " → " : " + ", words))], false, []));
                choices.Clear();
            }
            else if (withActions.Count == 0)
            {
                // Notes only: one entry for the column (per job for a column that has neither a slot nor a job; one for
                // everyone in a column for anyone: "Use Personals" under "Extras").
                var notes = perJob[0].Items;
                if (notes.Count > 0)
                {
                    entries.AddRange(header.Slot is null && header.Jobs.Count == 0 && jobs.Count > 1 && header.Kind != AssigneeKind.Anyone
                        ? perJob
                        : [new EntryParts(jobs.Count == 1 ? jobs[0] : null, notes, false, [])]);
                }
            }
            else if (jobs.Count == 1 || withActions.Count == 1)
            {
                entries.Add(withActions[0]);
            }
            else if (header.Slot is not null && withActions.Count == jobs.Count && withActions.Select(p => Signature(p.Items, p.Seq)).Distinct().Count() == 1)
            {
                // The same actions for every job of the slot (a role action such as Reprisal or Feint). An action only some
                // of the slot's jobs have (Addle in a ranged slot: casters only) gets per-job entries, so that the others
                // (a Bard in R2) are not reminded of it.
                entries.Add(withActions[0] with { Job = null });
            }
            else
            {
                entries.AddRange(withActions);
            }

            entries.AddRange(choices);
            if (entries.Count == 0)
                return;

            var anchor = timing.Anchor ?? row.Mechanic!;
            var target = item.IsSequence
                ? group.Select(t => t.Token.Target?.Value).FirstOrDefault(v => v is not null)
                : group.Select(t => t.Target?.Value).FirstOrDefault(v => v is not null);
            if (target is null)
                entries = entries.SelectMany(e => this.SplitByInferredTarget(e, row, anchor, header)).ToList();
            var footnote = group.Any(t => t.Token.Footnote) ? this.Footnote(row, cell, group) : null;
            var notesText = this.EntryNote(group, footnote, groupIndex == 0 ? shared.StepNotes : []);
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

                foreach (var (job, items, seq, actions, choice) in entries)
                {
                    var timingAction = actions.FirstOrDefault();

                    // A tank's single-target cooldowns without a written target are its own; others get one from the notes.
                    var isTank = job is { } j ? Jobs.RoleOf(j) == Role.Tank : header.Role == Role.Tank;
                    var singles = actions.Where(a => a.Target == ActionTarget.Single).ToList();
                    var entry = new PlanEntry
                    {
                        Mechanic = anchor.Id,
                        Who = new WhoSpec { Slot = header.Slot, Job = job, Assignee = header.Kind == AssigneeKind.Anyone ? null : key },
                        Do = items.Select(Clone).ToList(),
                        Seq = seq && items.Count > 1,
                        Target = (choice is null || singles.Count > 0 ? target : null) ?? (singles.Count > 0 && !isTank ? this.InferTarget(row, anchor, singles) : null),
                        Note = choice is null ? notesText : notesText is null ? choice : notesText + "; " + choice,
                        Detail = this.DetailFor(row, cell, actions),
                        Optional = optional || choice is not null,
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

                    // "(Late)": an entry pressed before its hit is pressed just before it, its longest cast (or the GCD a
                    // follow-up needs first) ending 1 s before the hit, but never earlier than without the hint (mitigation
                    // timed to also cover a later hit).
                    if (timing.Late && press is null && entry.Offset is null && actions.Count > 0
                        && this.TimingOf(entry, anchor).AutomaticOffset is var automatic && automatic < 0 && this.LateOffsetOf(actions) > automatic)
                    {
                        entry.Offset = this.LateOffsetOf(actions);
                    }

                    this.drafts.Add(new Draft(entry, this.PressEstimate(entry, anchor), row.Tab.Order, row.Row.Index, cell.Column.Index, item.Index, (groupIndex * 100) + part, IsConfident(row), Written: press is not null));
                    part++;
                }
            }
        }

        /// <summary>
        /// The target the row gives single-target actions (<see cref="InferTarget"/>: "tank" on a tankbuster) is theirs only:
        /// an entry that also has actions for the party or an area ("EukDiag/Kerachole" on TEA's Double Rocket Punch) becomes
        /// two, as a written target splits it ("Soil/Excog (Tank)"). A sequence, an either/or choice and an item of both
        /// kinds (or a note) keep the entry whole.
        /// </summary>
        private IEnumerable<EntryParts> SplitByInferredTarget(EntryParts parts, RowState row, Mechanic anchor, AssigneeHeader header)
        {
            var isTank = parts.Job is { } job ? Jobs.RoleOf(job) == Role.Tank : header.Role == Role.Tank;
            var singles = parts.Actions.Where(a => a.Target == ActionTarget.Single).ToList();
            if (parts.Seq || parts.Choice is not null || isTank || singles.Count == 0 || singles.Count == parts.Actions.Count)
                return [parts];
            var kinds = parts.Items.Select(this.IsSingleTarget).ToList();
            if (kinds.Any(k => k is null) || this.InferTarget(row, anchor, singles) is null)
                return [parts];

            var targeted = parts with { Items = parts.Items.Where((_, i) => kinds[i] == true).ToList(), Actions = singles };
            var rest = parts with { Items = parts.Items.Where((_, i) => kinds[i] == false).ToList(), Actions = parts.Actions.Where(a => a.Target != ActionTarget.Single).ToList() };
            return kinds[0] == true ? [targeted, rest] : [rest, targeted];
        }

        /// <summary>Whether every action of an item takes a single target; null for a note or an either/or of both kinds.</summary>
        private bool? IsSingleTarget(PlanItem item)
        {
            var kinds = item.ActionIds().Select(id => this.options.Actions.Get(id)?.Target == ActionTarget.Single).Distinct().ToList();
            return kinds.Count == 1 ? kinds[0] : null;
        }

        /// <summary>The plan items of an alternative: one per action, or an either/or per group of options ("the Arrow|the Bole").</summary>
        private static IEnumerable<PlanItem> ItemsOf(ResolvedAlternative alternative) =>
            ItemGroups(alternative).Select(g => g.Count == 1 ? PlanItem.ForAction(g[0].Id, g[0].Name) : PlanItem.AnyOf(g.Select(a => PlanItem.ForAction(a.Id, a.Name))));

        /// <summary>
        /// In a column for anyone ("Extras"): the token's words name an action of two or more of the jobs ("Feint", "Party
        /// Mit"), so it does not say whose it is; false when each job's action is named by words of its own ("PLD Wings",
        /// "Dismantle", "Minne/Mantra", "Party Mit (DM/HoL)").
        /// </summary>
        private static bool SharedByJobs(TokenState token, IEnumerable<Job> jobs) =>
            !token.JobsNamed && jobs.Where(j => token.ByJob[j].Kind == TokenKind.Actions)
                .SelectMany(j => token.ByJob[j].Alternatives.Select(a => (Job: j, a.Matched)))
                .GroupBy(x => x.Matched, StringComparer.OrdinalIgnoreCase)
                .Any(g => g.Select(x => x.Job).Distinct().Count() > 1);

        /// <summary>The row's label matched its mechanic: not a stand-in, the row above's or the next row's, or a guess from its spelling.</summary>
        private static bool IsConfident(RowState row) => !row.Unknown && row.Match is not ("unknown" or "previous" or "next" or "note" or "fuzzy" or "other-phase");

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
            TimingRules.EarliestPreHitOffset(actions, level) ?? TimingRules.DefaultOffset(null, level);

        /// <summary>
        /// The press time the scheduler will give the entry (<see cref="TimingRules.ForEntry"/>), which orders the plan's
        /// entries.
        /// </summary>
        private double PressEstimate(PlanEntry entry, Mechanic anchor) => this.TimingOf(entry, anchor).PressAt;

        /// <summary>The entry's timing as the scheduler gives it (<see cref="TimingRules.ForEntry"/>).</summary>
        private EntryTiming TimingOf(PlanEntry entry, Mechanic anchor) => TimingRules.ForEntry(entry, anchor, this.fight, this.options.Actions);

        /// <summary>The timing of an entry on its mechanic (<see cref="TimingRules.ForEntry"/>).</summary>
        private EntryTiming TimingOf(PlanEntry entry) => this.TimingOf(entry, this.fight.GetMechanic(entry.Mechanic)!);

        /// <summary>
        /// The offset "(Late)" gives an entry with these actions: <see cref="LateOffset"/>, minus the longest time one of them
        /// takes from its press to its effect (<see cref="TimeToEffect"/>), so that each effect is in place that long before
        /// the hit ("Reprisal (Late)": −1 s; "Succor (Late)", a 2 s cast: −3 s; "Eukrasian Prognosis (Late)", after Eukrasia's
        /// 2.5 s: −3.5 s).
        /// </summary>
        private double LateOffsetOf(IEnumerable<ActionInfo> actions) => LateOffset - actions.Select(this.TimeToEffect).DefaultIfEmpty(0).Max();

        /// <summary>
        /// Seconds from pressing an action to its effect: its cast, plus the recast of a GCD that is pressed right before it
        /// to make it usable (<see cref="ActionInfo.FollowUpOf"/> a GCD without a cooldown of its own, a recast under
        /// <see cref="PlanCheckOptions.MinRecast"/>: Eukrasia before Eukrasian Prognosis, not Macrocosmos, pressed long before
        /// Microcosmos).
        /// </summary>
        private double TimeToEffect(ActionInfo action) =>
            action.CastTime + (action.FollowUpOf is { } id && this.options.Actions.Get(id) is { IsGcd: true } trigger
                && trigger.RecastAt(this.level) < CheckDefaults.MinRecast ? trigger.RecastAt(this.level) : 0);

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

        /// <summary>
        /// The detail of an entry of a cell: the row's notes column, then the notes written for the cell's column in its
        /// section, then the notes merged across several columns that name one of the entry's <paramref name="actions"/>.
        /// </summary>
        private string? DetailFor(RowState row, AssigneeCell cell, IReadOnlyCollection<ActionInfo> actions)
        {
            var lines = new List<string>(row.Detail);
            var key = (row.Tab, row.Section, cell.Column.Index);
            if (this.columnNotes.TryGetValue(key, out var notes))
            {
                lines.AddRange(notes.Where(n => !lines.Contains(n)));
                this.columnNoteUse.Add(key);
            }

            foreach (var banner in this.bannerNotes)
            {
                if (banner.Note.Tab != row.Tab || !Equals(banner.Note.Row.Phase, row.Section) || !banner.Columns.Contains(cell.Column.Index)
                    || !actions.Any(a => banner.Actions.Contains(a.Id)) || lines.Contains(banner.Text))
                    continue;
                lines.Add(banner.Text);
                banner.Uses++;
                banner.Pressed.UnionWith(actions.Select(a => a.Id).Where(banner.Actions.Contains));
            }

            return lines.Count > 0 ? string.Join("\n", lines) : null;
        }

        /// <summary>
        /// The actions a note names by alias ("Soil/Kera" → Sacred Soil, Kerachole), for any job, also as they are at the sync
        /// level; note-only phrases do not count. A word that also names some job's action ("to cover all": Cover) only
        /// matters when an entry presses it.
        /// </summary>
        private HashSet<uint> NamedActions(string text)
        {
            var found = new HashSet<uint>();
            var words = ImportText.NormalizedWords(text);
            for (var i = 0; i < words.Length; i++)
            {
                if (words[i].Length < 3 || this.options.Aliases.MatchLongest(string.Join(' ', words, i, words.Length - i)) is not { } match)
                    continue;
                foreach (var expansion in this.options.Aliases.Lookup(match.Key).Where(e => !e.NoteOnly))
                {
                    foreach (var name in expansion.ActionNames)
                    {
                        if (this.options.Actions.FindByName(name, expansion.Job) is not { } action)
                            continue;
                        found.Add(action.Id);
                        found.Add(this.options.Actions.ResolveAt(action, this.level).Id);
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// The text names players: by role or slot ("Tanks: …", "then OT provokes …", "healers") or by job ("WHM covers the
        /// rest"). A note over several columns that names none is about using the actions it names (<see cref="BannerNote"/>).
        /// </summary>
        private static bool NamesAPlayer(string text) =>
            Regex.Split(text, @"[^\p{L}\p{N}]+").Any(PlayerWords.Contains) || LayoutDetector.JobsFromTabName(text).Count > 0;

        /// <summary>
        /// The actions the note names that cells of its section name in the columns it spans that are not imported: the
        /// entries that would press them are not in this import (TEA's Soil/Kera tip, importing the Tank 1 column).
        /// </summary>
        private HashSet<uint> NamedInColumnsNotImported(BannerNote banner) =>
            banner.Note.Tab.Layout.DataRows
                .Where(r => Equals(r.Phase, banner.Note.Row.Phase))
                .SelectMany(r => r.Assignments)
                .Where(a => banner.Others.Contains(a.Column.Index))
                .SelectMany(a => this.NamedActions(a.Cell.Text ?? string.Empty).Where(banner.Actions.Contains))
                .ToHashSet();

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
                        foreach (var name in e.ActionNames)
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

        /// <summary>
        /// A party buff planned again while the earlier press still lasts and before it can be pressed again (Temperance
        /// at Hidden Mines and again at Water 2: a 20 s effect, a 120 s recast) says that the effect still covers the later
        /// mechanic: it becomes the action's first follow-up available at the level (a second Seraph while Seraph is out is
        /// Consolation), else a note "Temperance (still active)". Only for an action of one player (column, slot and job)
        /// that is used on the party, an area or themselves, has one charge at the level and is planned again from another
        /// row, both rows' labels matching their mechanics, the later entry neither optional nor targeted, and the earlier
        /// press one the player makes for sure: an optional entry ("(Temperance)", "Seraph (if needed)", one choice of an
        /// either/or) may not be pressed, so it keeps no buff up.
        /// </summary>
        /// <remarks>
        /// The earlier effect must still cover the later call as the reminders time both (<see cref="CoversLaterCall"/>);
        /// an earlier entry without a time in the sheet is pressed later when that makes it cover both (TEA's Hidden Mines
        /// Temperance 3.5 s before its hit instead of 6 s, so that it lasts through Water 2). Otherwise the later call stays
        /// a press, and <c>check</c> reports the recast the sheet asks for.
        /// </remarks>
        private void MarkStillActive(List<Draft> ordered)
        {
            // Per player and action: the last entry that presses it for sure, and whether its time is the importer's (no
            // time in the sheet), so that CoversLaterCall may press it later.
            var last = new Dictionary<(string? Assignee, string? Slot, Job? Job, uint Action), (Draft Draft, bool Movable)>();
            foreach (var d in ordered.Where(d => d.Confident))
            {
                var e = d.Entry;
                var timing = this.TimingOf(e); // as the sheet plans it, before an item of it is read as still active
                for (var i = 0; i < e.Do.Count; i++)
                {
                    if (e.Do[i].Action is not { } id || this.options.Actions.Get(id) is not { } action)
                        continue;
                    var key = (e.Who.Assignee, e.Who.Slot, e.Who.Job, id);
                    var duration = action.DurationAt(this.level) ?? 0;
                    var recast = action.RecastAt(this.level);
                    if (!last.TryGetValue(key, out var prev) || (prev.Draft.Tab, prev.Draft.Row) == (d.Tab, d.Row)
                        || (prev.Draft.Entry.Mechanic, prev.Draft.Entry.HitIndex ?? 0) == (e.Mechanic, e.HitIndex ?? 0)
                        || e.Optional || e.Target is not null || action.Target is not (ActionTarget.Party or ActionTarget.Ground or ActionTarget.Self)
                        || action.ChargesAt(this.level) > 1 || duration <= 0 || recast <= duration)
                    {
                        if (!e.Optional)
                            last[key] = (d, e.Offset is null);
                        continue;
                    }

                    var earlier = prev.Draft.Entry;
                    var lasts = $"the {action.Name} at {earlier.Mechanic}, which lasts {ImportText.Seconds(duration)} s (recast {ImportText.Seconds(recast)} s)";
                    var gap = timing.PressAt - this.TimingOf(earlier).PressAt;
                    if (gap >= duration || !this.CoversLaterCall(earlier, prev.Movable, timing, duration, out var movedFrom))
                    {
                        // Pressed again after the effect ends, or a second press the sheet asks for within the recast.
                        if (gap < duration)
                        {
                            this.Diag(DiagnosticSeverity.Info, "still-active",
                                $"{action.Name} at {e.Mechanic} is planned {ImportText.Seconds(gap)} s after {lasts}, but that one does not last through this hit {(prev.Movable ? "even pressed just before its own" : "at the time the sheet gives it")}: both stay presses.",
                                e.Src);
                        }

                        last[key] = (d, e.Offset is null);
                        continue;
                    }

                    var follow = action.FollowUps.Select(f => this.options.Actions.Get(f.Id))
                        .FirstOrDefault(f => f is not null && f.AvailableAt(this.level) && (e.Who.Job is not { } job || f.UsableBy(job)));
                    string now;
                    if (follow is not null && e.Do.Any(x => x.Action == follow.Id))
                    {
                        e.Do.RemoveAt(i--);
                        now = $"left out (its follow-up {follow.Name} is already there)";
                    }
                    else
                    {
                        e.Do[i] = follow is null ? PlanItem.ForNote(action.Name + " (still active)") : PlanItem.ForAction(follow.Id, follow.Name);
                        now = follow is null ? $"a note '{action.Name} (still active)'" : follow.Name;
                    }

                    e.Seq &= e.Do.Count > 1;
                    var moved = movedFrom is { } from
                        ? $" That one is pressed {ImportText.Seconds(-earlier.Offset!.Value)} s before its hit instead of {ImportText.Seconds(-from)} s, so that it lasts through this one."
                        : string.Empty;
                    this.Diag(DiagnosticSeverity.Info, "still-active",
                        $"{action.Name} at {e.Mechanic} is planned {ImportText.Seconds(timing.PressAt - this.TimingOf(earlier).PressAt)} s after {lasts}: it is still active, so it is read as {now}.{moved}",
                        e.Src);
                }
            }
        }

        /// <summary>
        /// Whether the <paramref name="earlier"/> press of an action whose effect lasts <paramref name="duration"/> seconds
        /// still covers a <paramref name="later"/> call of it, as the reminders time both (<see cref="TimingRules.ForEntry"/>):
        /// used when its reminder turns due (the default due lead, <see cref="EntryTiming.DueAt"/>), the effect outlasts the
        /// later hit, or the later press when that comes after the hit, by the expiry margin
        /// (<see cref="TimingRules.EarliestCoveringUse"/>). When it falls short, an earlier entry the importer times
        /// (<paramref name="movable"/>: no time in the sheet) is pressed later so that it covers both, if it is then still
        /// pressed before its own hit, no later than "(Late)" would press it (<see cref="LateOffsetOf"/>);
        /// <paramref name="movedFrom"/> is then its automatic offset before.
        /// </summary>
        private bool CoversLaterCall(PlanEntry earlier, bool movable, EntryTiming later, double duration, out double? movedFrom)
        {
            movedFrom = null;
            var before = this.TimingOf(earlier);
            var from = TimingRules.EarliestCoveringUse(Math.Max(later.Hit, later.PressAt), duration);
            if (before.DueAt(DefaultReminders) >= from - OffsetPrecision)
                return true;

            // Pressed later, with its reminder due at the earliest use that still covers the later call.
            var offset = ImportText.Round(from + DefaultReminders.DueLead - before.Hit);
            if (!movable || offset > this.LateOffsetOf(earlier.Do.SelectMany(i => i.ActionIds()).Select(this.options.Actions.Get).OfType<ActionInfo>()))
                return false;
            movedFrom = before.Offset;
            earlier.Offset = offset;
            return true;
        }

        /// <summary>
        /// A limit break written for one role in several columns of different slots at one mechanic ("LB3" in both tank
        /// columns at the P3 transition) is pressed by whoever has the shared gauge: every such entry is optional (never
        /// counted as missed), with a note naming the other columns ("or Tank 2"). The columns that are not imported count
        /// too (<see cref="NoteOtherLimitBreak"/>), so that a tank who imports only their own column reads the sheet as the
        /// whole import does.
        /// </summary>
        private void MarkSharedLimitBreaks(List<Draft> ordered)
        {
            bool IsLimitBreak(PlanEntry e) => e.Do.Count > 0 && e.Do.All(i => i.ActionIds().Any() && i.ActionIds().All(id => this.options.Actions.Get(id)?.LimitBreakTier is not null));
            foreach (var group in ordered.Select(d => d.Entry).Where(IsLimitBreak).GroupBy(e => (e.Mechanic, Hit: e.HitIndex ?? 0, Role: e.Who.Job is { } j ? Jobs.RoleOf(j) : Role.None)))
            {
                // Columns of different slots of one role (Tank 1 and Tank 2), not one slot's job variants (H1 WHM, H1 AST);
                // a column not imported is matched by its mechanic and role.
                var others = this.otherLimitBreaks.Where(o => o.Mechanic == group.Key.Mechanic && o.Role == group.Key.Role).ToList();
                var columns = group.Select(e => e.Who.Assignee).Concat(others.Select(o => o.Column)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (columns.Count < 2 || group.Select(e => e.Who.Slot).Concat(others.Select(o => o.Slot)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2)
                    continue;
                foreach (var e in group)
                {
                    var alternatives = "or " + string.Join(" or ", columns.Where(c => !string.Equals(c, e.Who.Assignee, StringComparison.OrdinalIgnoreCase)));
                    e.Optional = true;
                    e.Note = e.Note is null ? alternatives : e.Note + "; " + alternatives;
                }

                this.Diag(DiagnosticSeverity.Info, "shared-limit-break",
                    $"The limit break at {group.Key.Mechanic} is written for {string.Join(" and ", columns)}: whoever has the shared gauge presses it, so each of these entries is optional.",
                    group.First().Src);
            }
        }

        /// <summary>
        /// The recast fit: an entry without a time in the sheet that is pressed before its hit, and one of whose actions
        /// would have no charge at that press for a seat that sees it (<c>check</c>'s <c>recast-conflict</c>), is pressed
        /// when the action has a charge again plus the default due lead, so that its NOW cue comes when it is ready (TEA's
        /// Gavel Asylum, 85.8 s after the Asylum pressed after Photon, recast 90 s: 1.5 s before its hit instead of 7.2 s).
        /// It is never pressed later than "(Late)" would press it (<see cref="LateOffsetOf"/>): then just after the action is
        /// ready; when the action is ready only after that, the entry stays, and <c>check</c> reports the recast. The offset
        /// is written rounded up to 0.1 s, so the press is never before the action is ready.
        /// </summary>
        /// <remarks>
        /// The recast is <c>check</c>'s (<see cref="RecastReplay"/>: recasts and charges at the sync level, one charge
        /// recharging at a time, recast groups, recasts under <see cref="PlanCheckOptions.MinRecast"/> left out), on the
        /// reminders' press times, for every seat as <c>check</c> and the plugin read the plan (<see cref="Seats"/>). Only a
        /// press the player makes for sure keeps an action on recast: not an optional entry (in brackets, with a condition,
        /// one choice of an either/or), an either/or item or a note ("Temperance (still active)"). An entry with a time in
        /// the sheet keeps it, and an offset the importer wrote (still-active's) may move. A when-ready call ("on CD") keeps
        /// its press too, and still keeps its action on recast: its reminder already waits for the action in game (it stays
        /// due while the action is on cooldown), and a move would only push the calls after it later (LPDU TOP's P5 party
        /// mitigation "on CD" at Sigma and Omega, which the Blind Faith calls follow). A sequence moves for its first
        /// step only: its later steps follow it in game, so one that <c>check</c> (which times every step at the entry's
        /// press) finds without a charge may well have one when its turn comes (TEA's Sage at Doll Feeds, Zoe → Eukrasian
        /// Prognosis → Holos → Kerachole). An entry several seats see takes the latest press any of them needs. The
        /// earliest press that finds no charge moves first, then the plan is replayed again, until nothing moves: moves
        /// only go later, up to "(Late)". A <c>recast-fit</c> info names each entry that moved.
        /// </remarks>
        private void FitRecasts(List<Draft> drafts, List<Assignee> assignees)
        {
            var movable = drafts.Where(d => !d.Written && !d.Entry.WhenReady).Select(d => d.Entry).ToHashSet();
            if (movable.Count == 0)
                return;

            var seats = this.Seats(new MitPlan { Fight = this.fight.Key, Assignees = assignees, Entries = drafts.Select(d => d.Entry).ToList() });
            var moved = new Dictionary<PlanEntry, RecastMove>();
            for (var round = 0; round < MaxFitRounds; round++)
            {
                // Every seat's presses in the order the scheduler gives them, as check reads the plan this builds.
                var order = InPressOrder(drafts.Select(d => d with { Press = this.TimingOf(d.Entry).PressAt })).Select(d => d.Entry).ToList();
                var moves = seats.SelectMany(s => this.RecastMoves(s.Job, order.Where(s.Entries.Contains).ToList(), movable)).ToList();
                if (moves.Count == 0)
                    break;

                // The earliest press that finds no charge first: a later one may wait for it.
                var first = moves.Min(m => m.Use.PressAt);
                foreach (var move in moves.Where(m => m.Use.PressAt <= first + SameTime).GroupBy(m => m.Entry).Select(g => g.MaxBy(m => m.Offset)!))
                {
                    move.Entry.Offset = move.Offset;
                    moved[move.Entry] = moved.TryGetValue(move.Entry, out var before) ? move with { From = before.From } : move;
                }
            }

            foreach (var d in drafts)
            {
                if (moved.TryGetValue(d.Entry, out var move))
                    this.ReportRecastFit(move);
            }
        }

        /// <summary>
        /// The seats of <paramref name="plan"/> as <c>check</c> and the plugin read it: every job, in each slot of its role
        /// and in none, with the entries of the automatic column choice (<see cref="ColumnSuggestion.Suggest"/>,
        /// <see cref="PlanSelection.For"/>); each job's distinct sets of entries once.
        /// </summary>
        private List<(Job Job, HashSet<PlanEntry> Entries)> Seats(MitPlan plan)
        {
            var seats = new List<(Job Job, HashSet<PlanEntry> Entries)>();
            foreach (var job in Jobs.All.Where(j => j is not (Job.BLU or Job.BST)))
            {
                foreach (var slot in new string?[] { null }.Concat(Slots.Standard.Where(s => Slots.Fits(s, job))))
                {
                    var me = ColumnSuggestion.Context(ColumnSuggestion.Suggest(plan, job, slot), job, slot, this.level);
                    var entries = PlanSelection.For(plan, me).ToHashSet();
                    if (entries.Count > 0 && !seats.Exists(s => s.Job == job && s.Entries.SetEquals(entries)))
                        seats.Add((job, entries));
                }
            }

            return seats;
        }

        /// <summary>
        /// For one seat (<paramref name="job"/>, its entries in plan order): in each chain of presses that share a recast,
        /// the first press that finds no charge and can be pressed later (<see cref="MoveFor"/>). The presses after it are
        /// replayed again once it has moved.
        /// </summary>
        private IEnumerable<RecastMove> RecastMoves(Job job, List<PlanEntry> entries, HashSet<PlanEntry> movable)
        {
            var uses = new List<(Reminder Reminder, ActionInfo Action)>();
            foreach (var r in new ReminderScheduler(this.fight, entries, this.options.Actions, DefaultReminders).All)
            {
                if (RecastReplay.Tracked(r.Item, this.options.Actions, job, this.level, CheckDefaults.MinRecast) is { } action)
                    uses.Add((r, action));
            }

            foreach (var chain in RecastReplay.Chains(uses, u => u.Action))
            {
                // A press the player may skip (an optional entry) keeps nothing on recast.
                var presses = chain.ConvertAll(u => new RecastUse(u.Reminder.PressAt, u.Action, Spends: !u.Reminder.Entry.Optional));
                var charges = Math.Max(1, chain.Max(u => u.Action.ChargesAt(this.level)));
                var states = RecastReplay.Replay(presses, this.level, charges);
                for (var i = 0; i < chain.Count; i++)
                {
                    var r = chain[i].Reminder;
                    if (!states[i].HasCharge && movable.Contains(r.Entry) && IsPressedAtTheEntrysPress(r) && this.MoveFor(chain, presses, i, states[i].ReadyAt, charges) is { } move)
                    {
                        yield return move;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// True when the reminder's item is pressed at its entry's press time in game: every item of an entry, except the
        /// later steps of a sequence, which follow its first step (<c>check</c> times them at the entry's press too, so a
        /// step it finds without a charge may well have one when its turn comes).
        /// </summary>
        private static bool IsPressedAtTheEntrysPress(Reminder r) =>
            !r.Entry.Seq || r.ItemIndex == r.Entry.Do.FindIndex(i => i.ActionIds().Any());

        /// <summary>
        /// The later press of the <paramref name="index"/>th press of <paramref name="chain"/> (replayed as
        /// <paramref name="presses"/>), which finds no charge before <paramref name="readyAt"/>: then plus the default due
        /// lead, rounded up to 0.1 s, but no later than "(Late)" would press the entry. Null when the entry is not pressed
        /// before its hit, or the action is ready only after that.
        /// </summary>
        private RecastMove? MoveFor(List<(Reminder Reminder, ActionInfo Action)> chain, List<RecastUse> presses, int index, double readyAt, int charges)
        {
            var (use, action) = chain[index];
            var offset = use.PressAt - use.Hit;
            var late = this.LateOffsetOf(use.Entry.Do.SelectMany(i => i.ActionIds()).Select(this.options.Actions.Get).OfType<ActionInfo>());
            if (use.PressAt >= use.Hit || readyAt - use.Hit > late + SameTime)
                return null;
            var due = Math.Ceiling(Math.Round((readyAt + DefaultReminders.DueLead - use.Hit) * 10, 6)) / 10;
            var later = ImportText.Round(Math.Min(due, late));
            if (later <= offset + OffsetPrecision)
                return null;

            // The press it waits for: the last one before it that spent a charge, with several charges the one whose charge
            // comes back first.
            var spent = Enumerable.Range(0, index).Where(j => presses[j].Spends).ToList();
            var (earlier, earlierAction) = chain[spent[Math.Max(0, spent.Count - charges)]];
            return new RecastMove(use, action, later, ImportText.Round(offset), earlier, earlierAction, charges, due > late);
        }

        /// <summary>The <c>recast-fit</c> info of an entry the recast fit moved (<see cref="FitRecasts"/>).</summary>
        private void ReportRecastFit(RecastMove move)
        {
            var recast = ImportText.Seconds(move.Action.RecastAt(this.level));
            var limit = move.Charges == 1 ? $"recast {recast} s" : $"{move.Charges} charges, recast {recast} s";
            var earlier = move.EarlierAction.Id == move.Action.Id
                ? $"the {move.Action.Name} at {move.Earlier.Entry.Mechanic}"
                : $"the {move.EarlierAction.Name} at {move.Earlier.Entry.Mechanic}, which shares its recast";
            var gap = move.Use.Hit + move.From - move.Earlier.PressAt;
            var when = move.AtLate
                ? $"as late as \"(Late)\" would press it, once {move.Action.Name} is ready again"
                : $"so that its NOW cue comes when {move.Action.Name} is ready again";
            this.Diag(DiagnosticSeverity.Info, "recast-fit",
                $"{move.Action.Name} at {move.Entry.Mechanic} is planned {ImportText.Seconds(gap)} s after {earlier} ({limit}): it is pressed {ImportText.Seconds(-move.Offset)} s before its hit instead of {ImportText.Seconds(-move.From)} s, {when}.",
                move.Entry.Src);
        }

        /// <summary>Drafts in press order, then in sheet order (tab, row, column, item, part).</summary>
        private static List<Draft> InPressOrder(IEnumerable<Draft> drafts) => drafts
            .OrderBy(d => Math.Round(d.Press, 3))
            .ThenBy(d => d.Tab).ThenBy(d => d.Row).ThenBy(d => d.Column).ThenBy(d => d.Item).ThenBy(d => d.Part)
            .ToList();

        private MitPlan BuildPlan(List<TabState> tabs)
        {
            var assignees = new List<Assignee>();
            foreach (var tab in tabs)
            {
                foreach (var column in tab.Layout.AssigneeColumns)
                {
                    // A column for anyone ("Extras") is no seat: its calls are for the jobs they name.
                    var key = tab.Keys[column.Index];
                    if (column.Assignee!.Kind == AssigneeKind.Anyone || !this.AssigneeSelected(key, column.Assignee.Key) || assignees.Any(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    assignees.Add(new Assignee { Key = key, Slot = column.Assignee.Slot, Job = column.Assignee.Job });
                }
            }

            var ordered = InPressOrder(this.drafts);
            this.MarkStillActive(ordered);
            this.MarkSharedLimitBreaks(ordered);
            this.FitRecasts(ordered, assignees);

            // A follow-up or a note that MarkStillActive put in an entry, or a press the recast fit moved, changes its press
            // time: the plan lists its entries in the order the scheduler presses them.
            ordered = InPressOrder(ordered.Select(d => d with { Press = this.TimingOf(d.Entry).PressAt }));
            var width = Math.Max(3, ordered.Count.ToString(CultureInfo.InvariantCulture).Length);
            for (var i = 0; i < ordered.Count; i++)
                ordered[i].Entry.Id = "e" + (i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');

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

    /// <summary>
    /// The text a token keeps as a note: its words with the parenthetical hints ("Soil (when rots explode)"), and a
    /// <paramref name="target"/> its words do not name ("Earthly Star (melee)").
    /// </summary>
    private static string NoteTextOf(ParsedToken token, ParsedTarget? target)
    {
        var phrase = token.Phrase.Length > 0 ? token.Phrase : token.Text;
        IReadOnlyList<string> hints = target is not null && !phrase.Contains(target.Text, StringComparison.OrdinalIgnoreCase) ? [target.Text, .. token.Hints] : token.Hints;
        return hints.Count == 0 ? phrase : phrase + " (" + string.Join("; ", hints) + ")";
    }
}
