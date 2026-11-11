using MitCue.Core.Actions;
using MitCue.Core.Fights;
using MitCue.Core.Plans;

namespace MitCue.Core.Import;

/// <summary>A rich-text run inside a cell: formatting applies from <see cref="Start"/> to the next run.</summary>
public sealed record TextRun(int Start, string? Fg, bool Bold);

/// <summary>A merged range (inclusive row/column indexes).</summary>
public sealed record CellRange(int R0, int C0, int R1, int C1);

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

    /// <summary>Rows of cells (ragged; missing cells are empty).</summary>
    public required IReadOnlyList<IReadOnlyList<SheetCell>> Rows { get; init; }

    public IReadOnlyList<CellRange> Merges { get; init; } = [];

    public int ColumnCount => this.Rows.Count == 0 ? 0 : this.Rows.Max(r => r.Count);

    public SheetCell Cell(int row, int col) =>
        row >= 0 && row < this.Rows.Count && col >= 0 && col < this.Rows[row].Count ? this.Rows[row][col] : SheetCell.Empty;

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

/// <summary>Readers that produce a <see cref="SheetDocument"/> from the supported inputs.</summary>
public static class SheetReaders
{
    /// <summary>One tab from the JSON fixture format (docs/research/sheets-import.md).</summary>
    public static SheetTab FromFixtureJson(string json) => throw new NotImplementedException();

    /// <summary>All *.json fixtures in a directory as one document (tabs ordered by file name unless the JSON has an index).</summary>
    public static SheetDocument FromFixtureDirectory(string directory, string title) => throw new NotImplementedException();

    /// <summary>Pasted text: TSV (Google Sheets clipboard) or CSV, with quoted multi-line fields.</summary>
    public static SheetTab FromDelimited(string text, char separator = '\t', string tabName = "Pasted") => throw new NotImplementedException();

    /// <summary>An .xlsx workbook (e.g. a Google Sheets export), keeping fills, font colours, rich-text runs and merges.</summary>
    public static SheetDocument FromXlsx(Stream xlsx, string title) => throw new NotImplementedException();
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

    /// <summary>Calibration preset key in the fight file (e.g. "carry_the_clear"), used when fitting fails.</summary>
    public string? CalibrationKey { get; init; }

    /// <summary>Only import these tabs (null = every tab that looks like a plan table).</summary>
    public IReadOnlyList<string>? Tabs { get; init; }

    /// <summary>Only import these assignee columns (null = all).</summary>
    public IReadOnlyList<string>? Assignees { get; init; }

    /// <summary>ISO date to stamp on the plan (tests pass a fixed value).</summary>
    public string? ImportedDate { get; init; }
}

public sealed class ImportResult
{
    public required MitPlan Plan { get; init; }

    public IReadOnlyList<ImportDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>Fitted phase zero points (timeline seconds of sheet 0:00), for timestamped plans.</summary>
    public IReadOnlyDictionary<string, double> PhaseZeros { get; init; } = new Dictionary<string, double>();

    /// <summary>Largest absolute residual of the fit per phase (seconds).</summary>
    public IReadOnlyDictionary<string, double> PhaseSpread { get; init; } = new Dictionary<string, double>();
}

/// <summary>Converts a sheet into a <see cref="MitPlan"/> (pipeline in docs/DESIGN.md §6).</summary>
public sealed class PlanImporter
{
    public ImportResult Import(SheetDocument document, ImportOptions options) => throw new NotImplementedException();
}
