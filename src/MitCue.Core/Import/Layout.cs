namespace MitCue.Core.Import;

/// <summary>Whether a tab holds a plan table.</summary>
public enum TabKind
{
    /// <summary>A header row with at least one assignee column was found.</summary>
    Plan,

    /// <summary>No plan table (credits, guides, images): nothing to import. See the tab's diagnostics.</summary>
    NotAPlan,
}

/// <summary>What a column holds (docs/DESIGN.md §6 step 2).</summary>
public enum ColumnRole
{
    /// <summary>A header the detector does not recognise (reported as a diagnostic); not imported.</summary>
    Unknown,

    /// <summary>No header and no content below the header row.</summary>
    Empty,

    /// <summary>Sheet times of the row ("1:21 / 1:27", "1:47 – 1:56").</summary>
    Time,

    /// <summary>The mechanic label of the row ("Pantokrator", "Solar Ray ×2 (180k on both tanks) ★★").</summary>
    Mechanic,

    /// <summary>A per-row phase ("P2").</summary>
    Phase,

    /// <summary>A player's assignments; see <see cref="ColumnLayout.Assignee"/>.</summary>
    Assignee,

    /// <summary>Row notes (they become the entries' detail).</summary>
    Notes,

    /// <summary>Known but not imported, e.g. statistics such as Carry the Clear's "Top WHMs (of 36)".</summary>
    Ignored,
}

/// <summary>What a row is.</summary>
public enum RowKind
{
    /// <summary>Nothing but whitespace.</summary>
    Empty,

    /// <summary>Hidden in the source; skipped.</summary>
    Hidden,

    /// <summary>Above the header: a title ("TOP — White Mage").</summary>
    Title,

    /// <summary>Above the header: a legend or key ("Very Lethal ★★★", "Grey = optional").</summary>
    Legend,

    /// <summary>The header row (or a repeated header further down).</summary>
    Header,

    /// <summary>A phase section row with a name and/or zero-point hint ("P2 — Omega-M/F  (from phase start)").</summary>
    Section,

    /// <summary>A bare phase label row ("P2" in the label column).</summary>
    PhaseLabel,

    /// <summary>A mechanic row: a label and/or time and/or assignments. Rows without assignments are kept for mechanic order and calibration.</summary>
    Data,

    /// <summary>Free text that is not an assignment: author notes under the table, notes-column continuation lines, preamble prose.</summary>
    Note,

    /// <summary>A note row whose first text is a footnote ("*WAR/PLD use Mit after Protean befor Stack").</summary>
    Footnote,
}

/// <summary>Where a phase came from.</summary>
public enum PhaseSource
{
    /// <summary>The tab name ("P3", "P1/P2").</summary>
    TabName,

    /// <summary>The label column's header cell ("P1 " in LPDU's P1/P2 tab, "P5" in the tank tabs).</summary>
    HeaderCell,

    /// <summary>A section row ("P2 — Omega-M/F  (from phase start)").</summary>
    SectionRow,

    /// <summary>A bare label row ("P2", "P6").</summary>
    LabelRow,

    /// <summary>A per-row phase column ("P2", or a bare "2").</summary>
    PhaseColumn,

    /// <summary>
    /// A phase marker in front of a mechanic row's label ("P2 - Solar Ray"); the row itself is data. A last parenthetical
    /// becomes the section's <see cref="PhaseSection.ZeroHint"/>, as for section rows, but may be mechanic detail.
    /// </summary>
    MechanicLabel,
}

/// <summary>A column of a plan tab.</summary>
/// <param name="Index">0-based column index.</param>
/// <param name="Role">What the column holds.</param>
/// <param name="Header">Header cell text (trimmed), or null when the header cell is empty.</param>
/// <param name="Assignee">The parsed header of an <see cref="ColumnRole.Assignee"/> column, else null.</param>
public sealed record ColumnLayout(int Index, ColumnRole Role, string? Header, AssigneeHeader? Assignee = null);

/// <summary>
/// The start of a phase within a tab. Rows after it (up to the next section) belong to it.
/// </summary>
public sealed record PhaseSection
{
    /// <summary>Phase number as written (P3 → 3).</summary>
    public required int Number { get; init; }

    /// <summary>
    /// Phase id: the fight's N-th phase id when the detector was given a <see cref="Fights.FightDefinition"/>
    /// (and N is in range), else "pN".
    /// </summary>
    public required string Id { get; init; }

    public required PhaseSource Source { get; init; }

    /// <summary>Row index of the section (the header row for <see cref="PhaseSource.TabName"/> and <see cref="PhaseSource.HeaderCell"/>).</summary>
    public required int Row { get; init; }

    /// <summary>Phase name from a section row ("Omega-M/F"), else null.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// The section's zero-point / timing hint, kept verbatim for calibration and review: "timer from pull",
    /// "from phase start; boss targetable ≈ 0:33", "from Blind Faith; boss targetable ≈ 0:55".
    /// </summary>
    public string? ZeroHint { get; init; }

    /// <summary>The source text ("P2 — Omega-M/F  (from phase start)", "P2", or the tab name).</summary>
    public string? Text { get; init; }

    /// <summary>Source cell reference ("'TOP – Scholar'!B21"), null for <see cref="PhaseSource.TabName"/>.</summary>
    public string? Src { get; init; }
}

/// <summary>A non-empty cell of a row with its column and reference.</summary>
public sealed record RowText(int Column, string Text, string Src);

/// <summary>An assignee cell of a data row.</summary>
/// <param name="Column">The assignee column.</param>
/// <param name="Cell">The cell (text with formatting) to give to <see cref="CellParser"/>.</param>
/// <param name="Src">Reference of the cell the text comes from ("'P3'!F4").</param>
/// <param name="Spread">True when the text comes from a merged cell spanning several assignee columns of this row (it applies to each).</param>
public sealed record AssigneeCell(ColumnLayout Column, SheetCell Cell, string Src, bool Spread = false)
{
    public AssigneeHeader Assignee => this.Column.Assignee!;
}

/// <summary>One row of a tab with its classification and, for data rows, its parts.</summary>
public sealed record RowLayout
{
    /// <summary>0-based row index.</summary>
    public required int Index { get; init; }

    public required RowKind Kind { get; init; }

    /// <summary>
    /// The phase the row belongs to (the latest section at or above it), or null when unknown. For
    /// <see cref="RowKind.Section"/>/<see cref="RowKind.PhaseLabel"/> rows it is the phase they start.
    /// </summary>
    public PhaseSection? Phase { get; init; }

    /// <summary>
    /// Text of the mechanic label column, trimmed (null when empty or there is no such column). A phase marker in front
    /// of it ("P2 - Solar Ray") starts that phase (<see cref="PhaseSource.MechanicLabel"/>) and is left out ("Solar Ray");
    /// <see cref="Texts"/> keeps the cell as written.
    /// </summary>
    public string? Label { get; init; }

    /// <summary>Raw text of the time column, trimmed (null when empty).</summary>
    public string? TimeText { get; init; }

    /// <summary>The parsed row time (a single time, a range or a list), null when absent or unparsable.</summary>
    public ParsedTime? Time { get; init; }

    /// <summary>Lethality marked with ★ in the label (Carry the Clear: ★ … ★★★★), else null.</summary>
    public int? Severity { get; init; }

    /// <summary>Text of the notes column(s), joined with line breaks (null when empty).</summary>
    public string? Notes { get; init; }

    /// <summary>Non-empty assignee cells of a <see cref="RowKind.Data"/> row, in column order.</summary>
    public IReadOnlyList<AssigneeCell> Assignments { get; init; } = [];

    /// <summary>Every non-empty cell of the row (any column), in column order.</summary>
    public IReadOnlyList<RowText> Texts { get; init; } = [];

    /// <summary>Reference for the row: its label cell, else its first non-empty cell, else column A.</summary>
    public required string Src { get; init; }

    public string? PhaseId => this.Phase?.Id;
}

/// <summary>The detected layout of one tab.</summary>
public sealed class TabLayout
{
    public required SheetTab Tab { get; init; }

    public string Name => this.Tab.Name;

    public required TabKind Kind { get; init; }

    /// <summary>0-based header row, or -1 when none was found.</summary>
    public int HeaderRow { get; init; } = -1;

    /// <summary>Last row of the header block (header cells merged downwards, e.g. Carry the Clear C15:C16); equals <see cref="HeaderRow"/> otherwise.</summary>
    public int HeaderRowEnd { get; init; } = -1;

    /// <summary>Every column, in order (empty when there is no header).</summary>
    public IReadOnlyList<ColumnLayout> Columns { get; init; } = [];

    /// <summary>
    /// Assignee columns introduced by a header row repeated further down that names other players, or the same players
    /// in other columns (stacked tables). The data rows below such a header carry these columns in
    /// <see cref="AssigneeCell.Column"/>; see the "header-changed" diagnostic. Empty for a tab with one header.
    /// </summary>
    public IReadOnlyList<ColumnLayout> RepeatedHeaderColumns { get; init; } = [];

    /// <summary>The tab is hidden in the workbook (<see cref="SheetTab.Hidden"/>); <see cref="DocumentLayout.PlanTabs"/> leaves it out.</summary>
    public bool Hidden => this.Tab.Hidden;

    /// <summary>Every row of the tab, in order.</summary>
    public IReadOnlyList<RowLayout> Rows { get; init; } = [];

    /// <summary>Phase starts in row order.</summary>
    public IReadOnlyList<PhaseSection> Sections { get; init; } = [];

    /// <summary>Phase numbers named by the tab name ("P1/P2" → [1, 2]; "P1P2", the xlsx form, too). Empty when the name is not a phase list.</summary>
    public IReadOnlyList<int> NamePhases { get; init; } = [];

    /// <summary>Jobs named by the tab name ("WAR/DRK" → [WAR, DRK], "TOP – White Mage" → [WHM]).</summary>
    public IReadOnlyList<Job> NameJobs { get; init; } = [];

    /// <summary>A title found above the header ("TOP — White Mage"), else null.</summary>
    public string? Title { get; init; }

    /// <summary>Footnote texts (cells of note rows starting with "*"), for items marked with "*".</summary>
    public IReadOnlyList<RowText> Footnotes { get; init; } = [];

    public IReadOnlyList<ImportDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>The assignee columns of the header, then those of repeated headers (<see cref="RepeatedHeaderColumns"/>).</summary>
    public IEnumerable<ColumnLayout> AssigneeColumns => this.Columns.Where(c => c.Role == ColumnRole.Assignee).Concat(this.RepeatedHeaderColumns);

    public ColumnLayout? TimeColumn => this.Columns.FirstOrDefault(c => c.Role == ColumnRole.Time);

    public ColumnLayout? MechanicColumn => this.Columns.FirstOrDefault(c => c.Role == ColumnRole.Mechanic);

    public ColumnLayout? PhaseColumn => this.Columns.FirstOrDefault(c => c.Role == ColumnRole.Phase);

    public IEnumerable<ColumnLayout> NotesColumns => this.Columns.Where(c => c.Role == ColumnRole.Notes);

    public IEnumerable<RowLayout> DataRows => this.Rows.Where(r => r.Kind == RowKind.Data);

    public override string ToString() => $"{this.Name}: {this.Kind}, header row {this.HeaderRow}, {this.AssigneeColumns.Count()} assignee column(s)";
}

/// <summary>The detected layout of a document.</summary>
public sealed class DocumentLayout
{
    public required SheetDocument Document { get; init; }

    public required IReadOnlyList<TabLayout> Tabs { get; init; }

    /// <summary>
    /// Tabs that hold a plan table, except hidden ones (old versions, templates). A hidden tab the user picks by name
    /// is still in <see cref="Tabs"/> with its <see cref="TabLayout.Kind"/>.
    /// </summary>
    public IEnumerable<TabLayout> PlanTabs => this.Tabs.Where(t => t.Kind == TabKind.Plan && !t.Hidden);

    /// <summary>Diagnostics of every tab, in tab order.</summary>
    public IReadOnlyList<ImportDiagnostic> Diagnostics => this.Tabs.SelectMany(t => t.Diagnostics).ToList();

    /// <summary>
    /// The assignee columns of every plan tab, one per distinct <see cref="AssigneeHeader.Key"/> (first occurrence, tab
    /// order). LPDU gives MT, OT, H1, H2, …, then "H1 WHM", "H2 SCH", … and "CM LB Tank", "WAR", "DRK", ….
    /// </summary>
    public IReadOnlyList<AssigneeHeader> Assignees =>
        this.PlanTabs.SelectMany(t => t.AssigneeColumns).Select(c => c.Assignee!).DistinctBy(a => a.Key, StringComparer.OrdinalIgnoreCase).ToList();

    public TabLayout? Tab(string name) => this.Tabs.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
}
