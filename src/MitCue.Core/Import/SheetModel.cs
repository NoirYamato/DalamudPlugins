using MitCue.Core.Actions;
using MitCue.Core.Fights;
using MitCue.Core.Plans;

namespace MitCue.Core.Import;

/// <summary>
/// A rich-text run inside a cell: formatting applies from <see cref="Start"/> (a UTF-16 index into
/// <see cref="SheetCell.Text"/>) to the next run. Runs carry the effective formatting of their characters,
/// so <see cref="Fg"/> null means the default (black) text colour even when the cell has its own colour.
/// </summary>
public sealed record TextRun(int Start, string? Fg, bool Bold)
{
    /// <summary>Strikethrough for this run (read from xlsx rich text; the JSON fixtures do not record it).</summary>
    public bool Strike { get; init; }
}

/// <summary>A merged range (inclusive row/column indexes).</summary>
public sealed record CellRange(int R0, int C0, int R1, int C1)
{
    public bool Contains(int row, int col) => row >= this.R0 && row <= this.R1 && col >= this.C0 && col <= this.C1;
}

public sealed class SheetCell
{
    public static readonly SheetCell Empty = new();

    public string? Text { get; init; }

    /// <summary>Background colour "#rrggbb" or null.</summary>
    public string? Bg { get; init; }

    /// <summary>Text colour "#rrggbb" or null (default/black).</summary>
    public string? Fg { get; init; }

    public bool Bold { get; init; }

    public bool Strike { get; init; }

    public IReadOnlyList<TextRun> Runs { get; init; } = [];

    /// <summary>Cell comment/note.</summary>
    public string? Note { get; init; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(this.Text);

    public override string ToString() => this.Text ?? string.Empty;
}

public sealed class SheetTab
{
    public required string Name { get; init; }

    public long? Gid { get; init; }

    /// <summary>Position of the tab in its document (0-based), when the source records it.</summary>
    public int Index { get; init; }

    /// <summary>The tab is hidden in the source workbook (xlsx <c>state="hidden"</c> or <c>"veryHidden"</c>).</summary>
    public bool Hidden { get; init; }

    /// <summary>Rows of cells (ragged; missing cells are empty).</summary>
    public required IReadOnlyList<IReadOnlyList<SheetCell>> Rows { get; init; }

    public IReadOnlyList<CellRange> Merges { get; init; } = [];

    /// <summary>Rows hidden in the source (0-based, ascending). Empty for pasted text.</summary>
    public IReadOnlyList<int> HiddenRows { get; init; } = [];

    /// <summary>Columns hidden in the source (0-based, ascending). Empty for pasted text.</summary>
    public IReadOnlyList<int> HiddenCols { get; init; } = [];

    public int ColumnCount => this.Rows.Count == 0 ? 0 : this.Rows.Max(r => r.Count);

    public SheetCell Cell(int row, int col) =>
        row >= 0 && row < this.Rows.Count && col >= 0 && col < this.Rows[row].Count ? this.Rows[row][col] : SheetCell.Empty;

    public bool IsRowHidden(int row) => this.HiddenRows.Contains(row);

    public bool IsColumnHidden(int col) => this.HiddenCols.Contains(col);

    /// <summary>The merged range covering a cell, or null when the cell is not merged.</summary>
    public CellRange? MergeAt(int row, int col) => this.Merges.FirstOrDefault(m => m.Contains(row, col));

    /// <summary>A1 reference like "'P3'!C4" for provenance.</summary>
    public string Ref(int row, int col)
    {
        var letters = string.Empty;
        var c = col + 1;
        while (c > 0)
        {
            var m = (c - 1) % 26;
            letters = (char)('A' + m) + letters;
            c = (c - 1) / 26;
        }
        return $"'{this.Name}'!{letters}{row + 1}";
    }
}

public sealed class SheetDocument
{
    public required string Title { get; init; }

    public required IReadOnlyList<SheetTab> Tabs { get; init; }

    public SheetTab? Tab(string name) => this.Tabs.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Options for <see cref="SheetReaders.FromXlsx(Stream, string, XlsxReadOptions?)"/>.</summary>
public sealed class XlsxReadOptions
{
    /// <summary>
    /// Original tab names in workbook order. Excel does not allow '/' in sheet names, so a Google export
    /// names "P1/P2" "P1P2"; pass the names from the source (e.g. Google's htmlview page) to restore them.
    /// Extra or missing entries are ignored (the xlsx name is kept).
    /// </summary>
    public IReadOnlyList<string>? TabNames { get; init; }

    /// <summary>The gid of a single-tab download (<c>export?format=xlsx&amp;gid=…</c>); applied when the workbook has one tab.</summary>
    public long? Gid { get; init; }
}

/// <summary>Readers that produce a <see cref="SheetDocument"/> from the supported inputs.</summary>
public static class SheetReaders
{
    /// <summary>One tab from the JSON fixture format (docs/research/sheets-import.md §2.2).</summary>
    /// <exception cref="FormatException">The JSON is not a fixture tab.</exception>
    public static SheetTab FromFixtureJson(string json) => FixtureJsonReader.ReadTab(json);

    /// <summary>All *.json fixtures in a directory (not recursive) as one document, ordered by their "index" (then file name).</summary>
    public static SheetDocument FromFixtureDirectory(string directory, string title) => FixtureJsonReader.ReadDirectory(directory, title);

    /// <summary>
    /// Pasted or exported text as one tab. With <c>'\t'</c> the Google Sheets clipboard rule applies (a field is quoted
    /// only when it contains a line break or tab, so a cell like <c>"Kera" on MT</c> stays raw); any other separator
    /// uses RFC 4180 quoting (quoted fields may hold separators, doubled quotes and line breaks). Line breaks inside
    /// fields become <c>\n</c>, rows may have different lengths, empty fields are empty cells (null text), and a
    /// trailing line break or UTF-8 BOM is ignored. Cells carry text only.
    /// </summary>
    public static SheetTab FromDelimited(string text, char separator = '\t', string tabName = "Pasted") =>
        DelimitedReader.ReadTab(text, separator, tabName);

    /// <summary>
    /// The separator to use for pasted text: tab when the text contains one (Google Sheets / Excel clipboard),
    /// otherwise comma.
    /// </summary>
    public static char GuessSeparator(string text) => text.Contains('\t') ? '\t' : ',';

    /// <summary>An .xlsx workbook (e.g. a Google Sheets export), keeping fills, font colours, rich-text runs and merges.</summary>
    /// <exception cref="InvalidDataException">The stream is not a readable xlsx workbook.</exception>
    public static SheetDocument FromXlsx(Stream xlsx, string title) => XlsxReader.Read(xlsx, title, null);

    /// <summary>
    /// An .xlsx workbook with options: text (shared, inline and rich strings; numbers formatted with their number
    /// format), fills, font colours (RGB, theme with tint, indexed), bold, strikethrough, rich-text runs, merges,
    /// notes (comments), hidden rows/columns, and conditional-format colours evaluated the way Google Sheets does.
    /// </summary>
    /// <exception cref="InvalidDataException">The stream is not a readable xlsx workbook.</exception>
    public static SheetDocument FromXlsx(Stream xlsx, string title, XlsxReadOptions? options) => XlsxReader.Read(xlsx, title, options);
}

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>A note about the import for the review screen.</summary>
/// <param name="Severity">How serious it is.</param>
/// <param name="Code">Stable machine code, e.g. "unresolved-token", "ambiguous-mechanic", "not-available-at-level".</param>
/// <param name="Message">Human-readable explanation.</param>
/// <param name="Src">Source cell reference, if any.</param>
public sealed record ImportDiagnostic(DiagnosticSeverity Severity, string Code, string Message, string? Src = null);

public sealed class ImportOptions
{
    public required FightDefinition Fight { get; init; }

    public required IActionCatalog Actions { get; init; }

    public required AliasDictionary Aliases { get; init; }

    public string? PlanName { get; init; }

    public string? SourceUrl { get; init; }

    /// <summary>
    /// The sheet's preset in the fight file (e.g. "carry_the_clear"): its <c>calibration</c> zero points are used for phases
    /// the importer cannot fit, and its <c>row_offsets</c> correct rows that time another event than their mechanic's anchor.
    /// Null: a preset whose words all occur in the document title, if any.
    /// </summary>
    public string? CalibrationKey { get; init; }

    /// <summary>Only import these tabs (null = every tab that looks like a plan table).</summary>
    public IReadOnlyList<string>? Tabs { get; init; }

    /// <summary>Only import these assignee columns (null = all).</summary>
    public IReadOnlyList<string>? Assignees { get; init; }

    /// <summary>ISO date to stamp on the plan (tests pass a fixed value).</summary>
    public string? ImportedDate { get; init; }

    /// <summary>
    /// The plan id. Null: a name-based GUID from the fight, plan name, source (URL or document title), tabs and assignee
    /// filter, so importing the same source again gives the same id.
    /// </summary>
    public string? PlanId { get; init; }

    /// <summary>
    /// <see cref="PlanSource.Kind"/>: "google-sheet", "google-doc", "file", "paste". Null: "google-doc"/"google-sheet" when
    /// <see cref="SourceUrl"/> is set, "paste" for a single tab named "Pasted" (<see cref="SheetReaders.FromDelimited"/>), else "file".
    /// </summary>
    public string? SourceKind { get; init; }
}

public sealed class ImportResult
{
    public required MitPlan Plan { get; init; }

    public IReadOnlyList<ImportDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>Fitted phase zero points (timeline seconds of sheet 0:00), for timestamped plans.</summary>
    public IReadOnlyDictionary<string, double> PhaseZeros { get; init; } = new Dictionary<string, double>();

    /// <summary>Largest absolute residual of the fit per phase (seconds).</summary>
    public IReadOnlyDictionary<string, double> PhaseSpread { get; init; } = new Dictionary<string, double>();

    /// <summary>Every mechanic row of the imported tabs (with or without assignments) and the mechanic it was matched to, for review.</summary>
    public IReadOnlyList<ImportedRow> Rows { get; init; } = [];
}

/// <summary>A mechanic row of an imported tab and what the importer made of it.</summary>
/// <param name="Tab">Tab name.</param>
/// <param name="Row">1-based sheet row.</param>
/// <param name="Label">The row's mechanic label, or null.</param>
/// <param name="Phase">The row's phase id, or null.</param>
/// <param name="Mechanic">The mechanic id the row's entries anchor to, or null when there is none.</param>
/// <param name="Match">
/// How: "exact", "simplified", "prefix" or "fuzzy" (by alias), "time" or "order" (among several mechanics with the label),
/// "other-phase" (the label belongs to another phase), "previous" (no label: the row above's mechanic), "next" (no label and
/// no row above in its phase: the next row's mechanic) or "unknown". A timed row re-anchored by its time is "time".
/// </param>
/// <param name="Src">The label cell (or the row's first cell).</param>
public sealed record ImportedRow(string Tab, int Row, string? Label, string? Phase, string? Mechanic, string Match, string Src);
