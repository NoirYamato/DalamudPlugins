using System.Text.RegularExpressions;
using MitCue.Core.Fights;

namespace MitCue.Core.Import;

/// <summary>
/// Step 2 of the import pipeline (docs/DESIGN.md §6): finds each tab's header row, classifies its columns (time,
/// mechanic label, assignee, notes, phase, ignored), parses assignee headers into slot/job/role, assigns a phase to
/// every row (tab name, header cell, section rows, label rows, phase column) and separates data rows from titles,
/// legends, notes and footnotes. It does not interpret assignment cells; that is <see cref="CellParser"/>'s job.
/// </summary>
/// <remarks>
/// Heuristics, in order of trust: the header row is the row with the most cells that parse strictly as assignee
/// headers (<see cref="AssigneeHeader.TryParse"/>) plus known column keywords; columns without a header are classified
/// by content (mostly times → time; text left of the assignees → mechanic label); a row without label and time whose
/// assignee cells carry the header's fill instead of the column's data fill (LPDU writes notes in white on black under
/// its tables) is a note, otherwise long sentences and "*" footnotes are notes and short text is a label-less data row.
/// Everything uncertain is reported in <see cref="TabLayout.Diagnostics"/>.
/// </remarks>
public sealed partial class LayoutDetector
{
    private static readonly HashSet<string> NotesWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "note", "notes", "comment", "comments", "remark", "remarks", "info", "detail", "details", "explanation",
        "explanations", "description", "strategy", "strat", "tips", "tip",
    };

    private static readonly HashSet<string> TimeWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "time", "times", "timer", "timestamp", "timestamps", "timing", "timings", "when", "m:ss", "mm:ss",
    };

    private static readonly HashSet<string> MechanicWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "mechanic", "mechanics", "mech", "mechs", "event", "events", "damage", "attack", "attacks", "ability",
        "abilities", "boss", "cast", "casts", "hit", "hits",
    };

    private static readonly HashSet<string> PhaseWords = new(StringComparer.OrdinalIgnoreCase) { "phase", "phases" };

    public LayoutDetector(FightDefinition? fight = null)
    {
        this.Fight = fight;
    }

    /// <summary>The fight whose phase ids are used (null: ids are "p1", "p2", …).</summary>
    public FightDefinition? Fight { get; }

    [GeneratedRegex(@"^\s*(?:P|Phase\s*)(?<n>\d{1,2})\s*(?:[—–\-:|.]+\s*(?<name>.*?))?\s*(?:\((?<hint>[^()]*)\))?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SectionRegex();

    [GeneratedRegex(@"^\s*(?:P|Phase\s*)(?<n>\d{1,2})(?:\s*(?<sep>/|&|\+|,|-|–|and|to)?\s*(?:P|Phase\s*)?(?<n>\d{1,2}))*\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TabPhasesRegex();

    [GeneratedRegex(@"\(of\s*\d+\)|^top\b|\b(?:count|counts|usage|usages|popularity|stats|statistics|percent|percentage|logs?|parses?|kills)\b|%|#", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StatsHeaderRegex();

    [GeneratedRegex(@"[\p{L}\p{N}:%#']+")]
    private static partial Regex HeaderWordRegex();

    [GeneratedRegex(@"(?:^|\s)=(?:\s|$)|\b(?:key|legend)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegendRegex();

    /// <summary>Detects the layout of every tab.</summary>
    public DocumentLayout Detect(SheetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new DocumentLayout { Document = document, Tabs = document.Tabs.Select(this.Detect).ToList() };
    }

    /// <summary>The phase id for phase number <paramref name="number"/>: the fight's N-th phase id, else "pN".</summary>
    public string PhaseId(int number) =>
        this.Fight is { } f && number >= 1 && number <= f.Phases.Count ? f.Phases[number - 1].Id : "p" + number;

    /// <summary>
    /// Parses a phase section or label: "P2", "Phase 3", "P2 — Omega-M/F  (from phase start)",
    /// "P5 — Run: Dynamis  (from boss targetable; always 4:46 long)". <paramref name="name"/> and
    /// <paramref name="hint"/> (the last parenthetical) are null for a bare label.
    /// </summary>
    public static bool TryParseSection(string? text, out int number, out string? name, out string? hint)
    {
        number = 0;
        name = hint = null;
        if (string.IsNullOrWhiteSpace(text) || SectionRegex().Match(text) is not { Success: true } m)
            return false;
        number = int.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (number is < 1 or > 20)
            return false;
        name = m.Groups["name"].Success && m.Groups["name"].Value.Trim() is { Length: > 0 } n ? n : null;
        hint = m.Groups["hint"].Success && m.Groups["hint"].Value.Trim() is { Length: > 0 } h ? h : null;
        return true;
    }

    /// <summary>A bare phase label: "P2", "P1 ", "Phase 3", "P2:".</summary>
    public static bool TryParsePhaseLabel(string? text, out int number) =>
        TryParseSection(text, out number, out var name, out var hint) && name is null && hint is null;

    /// <summary>
    /// Phase numbers named by a tab name that is only a phase list: "P3" → [3], "P1/P2" and "P1P2" → [1, 2],
    /// "P4-P6" → [4, 5, 6]. Other names ("General Tank Mit til P5", "TOP – White Mage") give an empty list.
    /// </summary>
    public static IReadOnlyList<int> PhasesFromTabName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || TabPhasesRegex().Match(name) is not { Success: true } m)
            return [];
        var numbers = m.Groups["n"].Captures.Select(c => int.Parse(c.Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var seps = m.Groups["sep"].Captures.ToList();
        var result = new List<int> { numbers[0] };
        for (var i = 1; i < numbers.Count; i++)
        {
            // A separator capture belongs to the i-th repetition when it lies between the two numbers.
            var prevEnd = m.Groups["n"].Captures[i - 1].Index + m.Groups["n"].Captures[i - 1].Length;
            var nextStart = m.Groups["n"].Captures[i].Index;
            var sep = seps.FirstOrDefault(s => s.Index >= prevEnd && s.Index < nextStart)?.Value;
            if (sep is "-" or "–" or "to" && numbers[i] > numbers[i - 1] && numbers[i] - numbers[i - 1] <= 10)
            {
                for (var k = numbers[i - 1] + 1; k <= numbers[i]; k++)
                    result.Add(k);
            }
            else
            {
                result.Add(numbers[i]);
            }
        }

        return result.Where(n => n is >= 1 and <= 20).Distinct().ToList();
    }

    /// <summary>Jobs named in a tab name: "WAR/DRK" → [WAR, DRK], "TOP – White Mage" → [WHM], "TOP – WHM + SCH" → [WHM, SCH].</summary>
    public static IReadOnlyList<Job> JobsFromTabName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return [];
        var words = Regex.Split(name, @"[^\p{L}]+").Where(w => w.Length > 0).ToList();
        var result = new List<Job>();
        for (var i = 0; i < words.Count; i++)
        {
            if (i + 1 < words.Count && Jobs.TryParse(words[i] + " " + words[i + 1], out var two) && words[i].Length > 3)
            {
                result.Add(two);
                i++;
            }
            else if (words[i].Length == 3 && words[i].All(char.IsUpper) && Jobs.TryParse(words[i], out var abbr))
            {
                result.Add(abbr);
            }
            else if (words[i].Length > 3 && Jobs.TryParse(words[i], out var full))
            {
                result.Add(full);
            }
        }

        return result.Distinct().ToList();
    }

    /// <summary>Number of ★ in a mechanic label (Carry the Clear's lethality marks).</summary>
    public static int CountStars(string? text) => text?.Count(c => c == '★') ?? 0;

    /// <summary>Detects the layout of one tab.</summary>
    public TabLayout Detect(SheetTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        return new TabScan(this, tab).Run();
    }

    private static string? Txt(SheetTab tab, int r, int c) => tab.Cell(r, c).Text?.Trim() is { Length: > 0 } t ? t : null;

    private static IEnumerable<string> HeaderWords(string text) => HeaderWordRegex().Matches(text).Select(m => m.Value);

    private static bool IsNotesHeader(string h) => HeaderWords(h).Any(NotesWords.Contains);

    private static bool IsTimeHeader(string h) => HeaderWords(h).Any(TimeWords.Contains);

    private static bool IsMechanicHeader(string h) => HeaderWords(h).Any(MechanicWords.Contains);

    private static bool IsPhaseHeader(string h) => HeaderWords(h).Any(PhaseWords.Contains);

    private static bool IsStatsHeader(string h) => StatsHeaderRegex().IsMatch(h);

    private static bool StartsWithTime(string text) => ParsedTime.FindAll(text) is { Count: > 0 } all && all[0].Index == 0;

    private static int WordCount(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>The per-tab detection state.</summary>
    private sealed class TabScan
    {
        private readonly LayoutDetector owner;
        private readonly SheetTab tab;
        private readonly int nRows;
        private readonly int nCols;
        private readonly List<ImportDiagnostic> diags = [];
        private readonly IReadOnlyList<int> namePhases;
        private readonly List<PhaseSection> sections = [];

        public TabScan(LayoutDetector owner, SheetTab tab)
        {
            this.owner = owner;
            this.tab = tab;
            this.nRows = tab.Rows.Count;
            this.nCols = tab.ColumnCount;
            this.namePhases = PhasesFromTabName(tab.Name);
        }

        public TabLayout Run()
        {
            var nameJobs = JobsFromTabName(this.tab.Name);
            var headerRow = this.FindHeaderRow();
            if (headerRow < 0)
            {
                var rowsNoHeader = this.ClassifyPreamble(this.nRows, out var titleNoHeader);
                this.Diag(DiagnosticSeverity.Info, "not-a-plan", $"Tab '{this.tab.Name}' has no header row with player columns (slots, jobs or role names); it is not imported.", null);
                return new TabLayout
                {
                    Tab = this.tab,
                    Kind = TabKind.NotAPlan,
                    Rows = rowsNoHeader,
                    NamePhases = this.namePhases,
                    NameJobs = nameJobs,
                    Title = titleNoHeader,
                    Diagnostics = this.diags,
                };
            }

            var headerEnd = headerRow;
            foreach (var m in this.tab.Merges.Where(m => m.R0 == headerRow))
                headerEnd = Math.Max(headerEnd, m.R1);

            var columns = this.ClassifyColumns(headerRow, headerEnd);
            var rows = this.ClassifyRows(headerRow, columns, out var title, out var footnotes);
            var kind = columns.Any(c => c.Role == ColumnRole.Assignee) ? TabKind.Plan : TabKind.NotAPlan;
            return new TabLayout
            {
                Tab = this.tab,
                Kind = kind,
                HeaderRow = headerRow,
                HeaderRowEnd = headerEnd,
                Columns = columns,
                Rows = rows,
                Sections = this.sections,
                NamePhases = this.namePhases,
                NameJobs = nameJobs,
                Title = title,
                Footnotes = footnotes,
                Diagnostics = this.diags,
            };
        }

        private void Diag(DiagnosticSeverity severity, string code, string message, string? src) =>
            this.diags.Add(new ImportDiagnostic(severity, code, message, src));

        private string? Txt(int r, int c) => LayoutDetector.Txt(this.tab, r, c);

        private int FindHeaderRow()
        {
            var best = -1;
            var bestScore = 0;
            for (var r = 0; r < this.nRows; r++)
            {
                if (this.tab.IsRowHidden(r))
                    continue;
                int assignees = 0, keywords = 0, others = 0;
                for (var c = 0; c < this.nCols; c++)
                {
                    if (this.Txt(r, c) is not { } t)
                        continue;
                    if (AssigneeHeader.TryParse(t, out _))
                        assignees++;
                    else if (TryParsePhaseLabel(t, out _) || IsNotesHeader(t) || IsTimeHeader(t) || IsMechanicHeader(t) || IsStatsHeader(t) || IsPhaseHeader(t))
                        keywords++;
                    else
                        others++;
                }

                if (assignees == 0 || (assignees < 2 && keywords == 0))
                    continue;
                var score = (assignees * 2) + keywords - others;
                if (score > bestScore)
                {
                    best = r;
                    bestScore = score;
                }
            }

            return best;
        }

        private List<ColumnLayout> ClassifyColumns(int headerRow, int headerEnd)
        {
            var roles = new ColumnRole[this.nCols];
            var headers = new string?[this.nCols];
            var parsed = new AssigneeHeader?[this.nCols];
            var pending = new List<int>();
            for (var c = 0; c < this.nCols; c++)
            {
                var h = this.Txt(headerRow, c);
                headers[c] = h;
                if (this.tab.IsColumnHidden(c))
                {
                    roles[c] = ColumnRole.Ignored;
                    continue;
                }

                if (h is null)
                {
                    pending.Add(c);
                    continue;
                }

                if (TryParseSection(h, out _, out _, out _))
                {
                    roles[c] = ColumnRole.Mechanic; // "P1" heads the label column (LPDU)
                }
                else if (AssigneeHeader.TryParse(h, out var a))
                {
                    roles[c] = ColumnRole.Assignee;
                    parsed[c] = a;
                }
                else if (IsNotesHeader(h))
                {
                    roles[c] = ColumnRole.Notes;
                }
                else if (IsStatsHeader(h))
                {
                    roles[c] = ColumnRole.Ignored;
                }
                else if (IsTimeHeader(h))
                {
                    roles[c] = ColumnRole.Time;
                }
                else if (IsMechanicHeader(h))
                {
                    roles[c] = ColumnRole.Mechanic;
                }
                else if (IsPhaseHeader(h))
                {
                    roles[c] = ColumnRole.Phase;
                }
                else
                {
                    roles[c] = ColumnRole.Unknown;
                    this.Diag(DiagnosticSeverity.Warning, "unknown-column", $"Column header '{h}' is not a player, time, mechanic or notes column; the column is not imported.", this.tab.Ref(headerRow, c));
                }
            }

            var firstAssignee = Array.IndexOf(roles, ColumnRole.Assignee);
            if (firstAssignee < 0)
                firstAssignee = this.nCols;
            var mechanicCandidates = new List<(int Col, int Count)>();
            foreach (var c in pending)
            {
                var values = new List<string>();
                for (var r = headerEnd + 1; r < this.nRows; r++)
                {
                    if (!this.tab.IsRowHidden(r) && this.Txt(r, c) is { } v)
                        values.Add(v);
                }

                if (values.Count == 0)
                {
                    roles[c] = ColumnRole.Empty;
                    continue;
                }

                var timeLike = values.Count(StartsWithTime);
                if (timeLike >= 2 && timeLike >= 0.6 * values.Count)
                {
                    roles[c] = ColumnRole.Time;
                }
                else if (c < firstAssignee)
                {
                    mechanicCandidates.Add((c, values.Count(v => !StartsWithTime(v))));
                    roles[c] = ColumnRole.Unknown;
                }
                else if (values.Average(WordCount) >= 4)
                {
                    roles[c] = ColumnRole.Notes;
                }
                else
                {
                    roles[c] = ColumnRole.Unknown;
                    this.Diag(DiagnosticSeverity.Info, "unlabelled-column", $"Column {ColumnName(c)} has no header; it is not imported.", this.tab.Ref(headerRow, c));
                }
            }

            if (!roles.Contains(ColumnRole.Mechanic))
            {
                if (mechanicCandidates.OrderByDescending(x => x.Count).ThenByDescending(x => x.Col).FirstOrDefault() is { Count: > 0 } best)
                    roles[best.Col] = ColumnRole.Mechanic;
                else
                    this.Diag(DiagnosticSeverity.Warning, "no-mechanic-column", "No mechanic label column was found; rows can only be matched by time.", this.tab.Ref(headerRow, 0));
            }

            foreach (var (c, _) in mechanicCandidates.Where(x => roles[x.Col] == ColumnRole.Unknown))
                this.Diag(DiagnosticSeverity.Info, "unlabelled-column", $"Column {ColumnName(c)} has no header and is not the label column; it is not imported.", this.tab.Ref(headerRow, c));

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < this.nCols; c++)
            {
                if (parsed[c] is { } a && !keys.Add(a.Key))
                    this.Diag(DiagnosticSeverity.Warning, "duplicate-assignee", $"Two columns are headed '{a.Key}'.", this.tab.Ref(headerRow, c));
            }

            return Enumerable.Range(0, this.nCols).Select(c => new ColumnLayout(c, roles[c], headers[c], parsed[c])).ToList();
        }

        private List<RowLayout> ClassifyRows(int headerRow, List<ColumnLayout> columns, out string? title, out List<RowText> footnotes)
        {
            var rows = this.ClassifyPreamble(headerRow, out title);
            footnotes = [];
            var mech = columns.FirstOrDefault(c => c.Role == ColumnRole.Mechanic)?.Index;
            var time = columns.FirstOrDefault(c => c.Role == ColumnRole.Time)?.Index;
            var phaseCol = columns.FirstOrDefault(c => c.Role == ColumnRole.Phase)?.Index;
            var assignees = columns.Where(c => c.Role == ColumnRole.Assignee).ToList();
            var assigneeIdx = assignees.Select(c => c.Index).ToHashSet();
            var notes = columns.Where(c => c.Role == ColumnRole.Notes).Select(c => c.Index).ToList();

            // The phase in force at the header: header label cell > tab name > the last phase label above the header.
            PhaseSection? current = null;
            if (mech is { } mc && this.Txt(headerRow, mc) is { } headerLabel && TryParseSection(headerLabel, out var hn, out var hname, out var hhint))
                current = this.Section(hn, PhaseSource.HeaderCell, headerRow, hname, hhint, headerLabel, this.tab.Ref(headerRow, mc), null);
            else if (this.namePhases.Count > 0)
                current = this.Section(this.namePhases[0], PhaseSource.TabName, headerRow, null, null, this.tab.Name, null, null);
            else if (this.PreamblePhase(headerRow) is { } pre)
                current = pre;
            if (current is not null)
                this.sections.Add(current);

            // Typical fill of each assignee column in rows that have a label or a time (LPDU colours each role).
            var dataBg = new Dictionary<int, string?>();
            foreach (var a in assignees)
            {
                var fills = new List<string?>();
                for (var r = headerRow + 1; r < this.nRows; r++)
                {
                    if (this.tab.IsRowHidden(r))
                        continue;
                    var label = mech is { } m2 ? this.Txt(r, m2) : null;
                    var t = time is { } t2 ? this.Txt(r, t2) : null;
                    if ((label is not null && !TryParseSection(label, out _, out _, out _)) || t is not null)
                        fills.Add(this.tab.Cell(r, a.Index).Bg);
                }

                dataBg[a.Index] = fills.Count == 0 ? null : fills.GroupBy(f => f).OrderByDescending(g => g.Count()).First().Key;
            }

            var warnedNoPhase = false;
            var reportedMerges = new HashSet<CellRange>();
            for (var r = headerRow; r < this.nRows; r++)
            {
                var texts = this.RowTexts(r);
                var src = texts.Count > 0 ? texts[0].Src : this.tab.Ref(r, 0);
                if (r == headerRow)
                {
                    rows.Add(new RowLayout { Index = r, Kind = RowKind.Header, Phase = current, Texts = texts, Src = src });
                    continue;
                }

                if (this.tab.IsRowHidden(r))
                {
                    if (texts.Count > 0)
                        this.Diag(DiagnosticSeverity.Info, "hidden-content", $"Row {r + 1} is hidden; its content is not imported.", src);
                    rows.Add(new RowLayout { Index = r, Kind = RowKind.Hidden, Phase = current, Texts = texts, Src = src });
                    continue;
                }

                if (texts.Count == 0)
                {
                    rows.Add(new RowLayout { Index = r, Kind = RowKind.Empty, Phase = current, Src = src });
                    continue;
                }

                var label = mech is { } m ? this.Txt(r, m) : null;
                var timeText = time is { } tc ? this.Txt(r, tc) : null;
                var labelSrc = label is not null ? this.tab.Ref(r, mech!.Value) : src;
                var assignments = this.Assignments(r, headerRow, assignees, assigneeIdx, reportedMerges);
                var noteText = notes.Select(c => this.Txt(r, c)).Where(t => t is not null).ToList();
                var notesJoined = noteText.Count > 0 ? string.Join("\n", noteText) : null;

                // A per-row phase column switches the phase for this and the following rows.
                if (phaseCol is { } pc && this.Txt(r, pc) is { } pt && TryParsePhaseLabel(pt, out var pn) && pn != current?.Number)
                {
                    current = this.Section(pn, PhaseSource.PhaseColumn, r, null, null, pt, this.tab.Ref(r, pc), current);
                    this.sections.Add(current);
                }

                // Section and label rows: a phase marker with no assignment in the row.
                if (assignments.Count == 0)
                {
                    var candidates = new[] { (Text: label, Col: mech), (Text: timeText, Col: time), (Text: texts.Count == 1 ? texts[0].Text : null, Col: texts.Count == 1 ? texts[0].Column : (int?)null) };
                    var marker = candidates.FirstOrDefault(x => x.Text is not null && TryParseSection(x.Text, out _, out _, out _));
                    if (marker.Text is not null && TryParseSection(marker.Text, out var sn, out var sname, out var shint))
                    {
                        var isSection = sname is not null || shint is not null;
                        current = this.Section(sn, isSection ? PhaseSource.SectionRow : PhaseSource.LabelRow, r, sname, shint, marker.Text, this.tab.Ref(r, marker.Col!.Value), current);
                        this.sections.Add(current);
                        rows.Add(new RowLayout { Index = r, Kind = isSection ? RowKind.Section : RowKind.PhaseLabel, Phase = current, Label = label, TimeText = timeText, Notes = notesJoined, Texts = texts, Src = this.tab.Ref(r, marker.Col!.Value) });
                        continue;
                    }
                }

                // A repeated header row further down.
                if (assignees.Count > 0 && assignees.Count(a => this.Txt(r, a.Index) is { } t && AssigneeHeader.TryParse(t, out _)) >= Math.Max(2, (assignees.Count + 1) / 2))
                {
                    if (label is not null && TryParseSection(label, out var rn, out var rname, out var rhint))
                    {
                        current = this.Section(rn, PhaseSource.HeaderCell, r, rname, rhint, label, labelSrc, current);
                        this.sections.Add(current);
                    }

                    rows.Add(new RowLayout { Index = r, Kind = RowKind.Header, Phase = current, Label = label, Texts = texts, Src = src });
                    continue;
                }

                var kind = RowKind.Data;
                if (label is null && timeText is null)
                {
                    if (assignments.Count == 0)
                        kind = RowKind.Note; // only notes / unknown columns: a notes continuation line or a stray remark
                    else if (this.LooksLikeNote(r, headerRow, assignments, dataBg))
                        kind = RowKind.Note;
                }

                if (kind == RowKind.Note)
                {
                    if (texts[0].Text.StartsWith('*'))
                        kind = RowKind.Footnote;
                    footnotes.AddRange(texts.Where(t => t.Text.StartsWith('*')));
                    rows.Add(new RowLayout { Index = r, Kind = kind, Phase = current, Notes = notesJoined, Texts = texts, Src = src });
                    continue;
                }

                // Data row.
                ParsedTime? parsedTime = null;
                if (timeText is not null)
                {
                    if (!ParsedTime.TryParse(timeText, out parsedTime))
                    {
                        parsedTime = ParsedTime.FindAll(timeText) is { Count: > 0 } found && found[0].Index == 0 ? found[0].Time : null;
                        if (parsedTime is null)
                            this.Diag(DiagnosticSeverity.Warning, "unparsed-time", $"'{timeText}' in the time column is not a time.", this.tab.Ref(r, time!.Value));
                    }
                }

                if (label is null && timeText is null)
                    this.Diag(DiagnosticSeverity.Info, "missing-mechanic-label", $"Row {r + 1} has assignments but no mechanic label or time; it belongs to the mechanic before or after it.", src);
                if (current is null && !warnedNoPhase)
                {
                    warnedNoPhase = true;
                    this.Diag(DiagnosticSeverity.Warning, "no-phase", $"Rows from {r + 1} on have no phase (no phase in the tab name, header or a section row above them).", labelSrc);
                }

                var stars = CountStars(label);
                rows.Add(new RowLayout
                {
                    Index = r,
                    Kind = RowKind.Data,
                    Phase = current,
                    Label = label,
                    TimeText = timeText,
                    Time = parsedTime,
                    Severity = stars > 0 ? stars : null,
                    Notes = notesJoined,
                    Assignments = assignments,
                    Texts = texts,
                    Src = labelSrc,
                });
            }

            return rows;
        }

        /// <summary>A row without label and time: is it a note under/inside the table rather than label-less assignments?</summary>
        private bool LooksLikeNote(int r, int headerRow, IReadOnlyList<AssigneeCell> assignments, Dictionary<int, string?> dataBg)
        {
            int noteVotes = 0, dataVotes = 0;
            foreach (var a in assignments.Where(a => !a.Spread))
            {
                var headerBg = this.tab.Cell(headerRow, a.Column.Index).Bg;
                var typical = dataBg.GetValueOrDefault(a.Column.Index);
                if (string.Equals(headerBg, typical, StringComparison.OrdinalIgnoreCase))
                    continue; // header and data look alike: the fill says nothing
                var bg = this.tab.Cell(r, a.Column.Index).Bg;
                if (string.Equals(bg, headerBg, StringComparison.OrdinalIgnoreCase))
                    noteVotes++;
                else if (string.Equals(bg, typical, StringComparison.OrdinalIgnoreCase))
                    dataVotes++;
            }

            if (noteVotes > 0 && dataVotes == 0)
                return true;
            if (dataVotes > 0 && noteVotes == 0)
                return false;
            return assignments.Any(a => (a.Cell.Text?.Trim() ?? string.Empty) is var t && (WordCount(t) >= 6 || t.StartsWith('*') || t.EndsWith('.') || t.EndsWith('!')));
        }

        private List<AssigneeCell> Assignments(int r, int headerRow, List<ColumnLayout> assignees, HashSet<int> assigneeIdx, HashSet<CellRange> reportedMerges)
        {
            var list = new List<AssigneeCell>();
            foreach (var col in assignees)
            {
                var c = col.Index;
                var cell = this.tab.Cell(r, c);
                var merge = this.tab.MergeAt(r, c);
                if (merge is not null && merge.R0 <= headerRow)
                    continue; // a header cell merged downwards (Carry the Clear C15:C16)
                if (merge is not null && merge.R0 < r)
                {
                    // Continuation of a cell merged downwards: imported once, at its first row.
                    if (!this.tab.Cell(merge.R0, merge.C0).IsEmpty && reportedMerges.Add(merge))
                        this.Diag(DiagnosticSeverity.Info, "merged-rows", $"The assignment {this.tab.Ref(merge.R0, merge.C0)} spans rows {merge.R0 + 1}–{merge.R1 + 1}; it is imported once, at its first row.", this.tab.Ref(merge.R0, merge.C0));
                    continue;
                }

                var spread = merge is not null && Enumerable.Range(merge.C0, merge.C1 - merge.C0 + 1).Count(assigneeIdx.Contains) > 1;
                if (merge is not null && merge.C0 != c)
                {
                    // Covered by a merge anchored to the left in this row: the anchor's text applies to this column too.
                    var anchor = this.tab.Cell(merge.R0, merge.C0);
                    if (spread && !anchor.IsEmpty && assigneeIdx.Contains(merge.C0))
                        list.Add(new AssigneeCell(col, anchor, this.tab.Ref(merge.R0, merge.C0), true));
                    continue;
                }

                if (!cell.IsEmpty)
                    list.Add(new AssigneeCell(col, cell, this.tab.Ref(r, c), spread));
            }

            return list;
        }

        private List<RowText> RowTexts(int r)
        {
            var list = new List<RowText>();
            var count = r >= 0 && r < this.nRows ? this.tab.Rows[r].Count : 0;
            for (var c = 0; c < count; c++)
            {
                if (this.Txt(r, c) is { } t)
                    list.Add(new RowText(c, t, this.tab.Ref(r, c)));
            }

            return list;
        }

        private PhaseSection Section(int number, PhaseSource source, int row, string? name, string? hint, string? text, string? src, PhaseSection? previous)
        {
            if (this.namePhases.Count > 0 && !this.namePhases.Contains(number))
                this.Diag(DiagnosticSeverity.Warning, "phase-not-in-tab-name", $"Phase P{number} is not one of the phases named by the tab ({string.Join(", ", this.namePhases.Select(n => "P" + n))}).", src);
            if (previous is not null && number < previous.Number)
                this.Diag(DiagnosticSeverity.Warning, "phase-order", $"Phase P{number} comes after P{previous.Number}.", src);
            if (this.owner.Fight is { } fight && (number < 1 || number > fight.Phases.Count))
                this.Diag(DiagnosticSeverity.Warning, "unknown-phase", $"{fight.Name} has {fight.Phases.Count} phase(s); P{number} is not one of them.", src);
            return new PhaseSection
            {
                Number = number,
                Id = this.owner.PhaseId(number),
                Source = source,
                Row = row,
                Name = name,
                ZeroHint = hint,
                Text = text,
                Src = src,
            };
        }

        /// <summary>The last phase label/section written above the header (e.g. a "Phase 1" title), if any.</summary>
        private PhaseSection? PreamblePhase(int headerRow)
        {
            for (var r = headerRow - 1; r >= 0; r--)
            {
                if (this.tab.IsRowHidden(r))
                    continue;
                foreach (var t in this.RowTexts(r))
                {
                    if (TryParseSection(t.Text, out var n, out var name, out var hint))
                        return this.Section(n, name is null && hint is null ? PhaseSource.LabelRow : PhaseSource.SectionRow, r, name, hint, t.Text, t.Src, null);
                }
            }

            return null;
        }

        /// <summary>Rows above the header (or every row of a tab without one): titles, legends, notes.</summary>
        private List<RowLayout> ClassifyPreamble(int end, out string? title)
        {
            var kinds = new RowKind[end];
            var texts = new List<RowText>[end];
            for (var r = 0; r < end; r++)
            {
                texts[r] = this.RowTexts(r);
                kinds[r] = this.tab.IsRowHidden(r) ? RowKind.Hidden
                    : texts[r].Count == 0 ? RowKind.Empty
                    : texts[r].Any(t => t.Text.Contains('★') || t.Text.Contains('☆') || LegendRegex().IsMatch(t.Text)) ? RowKind.Legend
                    : RowKind.Note;
            }

            // Grow legend blocks to adjacent rows that have a short entry in the legend's first column.
            var legendCols = Enumerable.Range(0, end).Where(r => kinds[r] == RowKind.Legend).Select(r => texts[r][0].Column).ToHashSet();
            bool changed;
            do
            {
                changed = false;
                for (var r = 0; r < end; r++)
                {
                    if (kinds[r] != RowKind.Note)
                        continue;
                    var nearLegend = (r > 0 && kinds[r - 1] == RowKind.Legend) || (r + 1 < end && kinds[r + 1] == RowKind.Legend);
                    if (nearLegend && texts[r].Any(t => legendCols.Contains(t.Column) && WordCount(t.Text) <= 5))
                    {
                        kinds[r] = RowKind.Legend;
                        changed = true;
                    }
                }
            }
            while (changed);

            // Title: a short merged cell (Carry the Clear "TOP — White Mage"), else the first single short line.
            title = null;
            for (var r = 0; r < end && title is null; r++)
            {
                if (kinds[r] is RowKind.Hidden or RowKind.Empty)
                    continue;
                foreach (var t in texts[r])
                {
                    if (WordCount(t.Text) <= 8 && !t.Text.EndsWith(':') && !LegendRegex().IsMatch(t.Text) && !t.Text.Contains('★')
                        && this.tab.MergeAt(r, t.Column) is { } m && m.C1 > m.C0 && m.R0 == r && m.C0 == t.Column)
                    {
                        title = t.Text;
                        break;
                    }
                }
            }

            var firstContent = Enumerable.Range(0, end).FirstOrDefault(r => kinds[r] is not (RowKind.Hidden or RowKind.Empty), -1);
            if (title is null && firstContent >= 0 && kinds[firstContent] == RowKind.Note && texts[firstContent].Count == 1 && WordCount(texts[firstContent][0].Text) <= 8)
            {
                title = texts[firstContent][0].Text;
                kinds[firstContent] = RowKind.Title;
            }

            for (var r = 0; r < end; r++)
            {
                if (kinds[r] == RowKind.Note && title is not null && texts[r].Count == 1 && texts[r][0].Text == title)
                    kinds[r] = RowKind.Title;
            }

            var rows = new List<RowLayout>(Math.Max(end, 0));
            for (var r = 0; r < end; r++)
                rows.Add(new RowLayout { Index = r, Kind = kinds[r], Texts = texts[r], Src = texts[r].Count > 0 ? texts[r][0].Src : this.tab.Ref(r, 0) });
            return rows;
        }
    }

    private static string ColumnName(int col)
    {
        var letters = string.Empty;
        var c = col + 1;
        while (c > 0)
        {
            var m = (c - 1) % 26;
            letters = (char)('A' + m) + letters;
            c = (c - 1) / 26;
        }

        return letters;
    }
}
