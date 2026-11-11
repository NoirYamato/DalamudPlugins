using MitCue.Core.Import;

namespace MitCue.Core.Tests.Import;

/// <summary>The example sheets in tests/fixtures/sheets (docs/research/sheets-import.md §2).</summary>
internal static class Fixtures
{
    private static readonly Lazy<SheetDocument> LpduDoc = new(() => SheetReaders.FromFixtureDirectory(Dir("lpdu"), "LPDU TOP Mitigation Sheet"));
    private static readonly Lazy<SheetDocument> CtcDoc = new(() => SheetReaders.FromFixtureDirectory(Dir("carry-the-clear"), "Carry the Clear: TOP Healing Plan"));

    public static string SheetsRoot { get; } = FindRoot();

    public static SheetDocument Lpdu => LpduDoc.Value;

    public static SheetDocument CarryTheClear => CtcDoc.Value;

    public static IEnumerable<(string Key, SheetDocument Doc)> Both => [("lpdu", Lpdu), ("carry-the-clear", CarryTheClear)];

    public static SheetDocument Doc(string key) => key switch
    {
        "lpdu" => Lpdu,
        "carry-the-clear" => CarryTheClear,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };

    public static string Dir(string key) => Path.Combine(SheetsRoot, key);

    public static string Xlsx(string key) => Path.Combine(SheetsRoot, key, "export", key + ".xlsx");

    public static string Csv(string key, string tabFile) => Path.Combine(SheetsRoot, key, "export", "csv", tabFile + ".csv");

    /// <summary>The fixture file name of a tab ("P1/P2" → "p1-p2", "TOP – WHM + SCH" → "top-whm-sch").</summary>
    public static string FileName(string tab) =>
        System.Text.RegularExpressions.Regex.Replace(tab.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    public static SheetTab Tab(string key, string tab) => Doc(key).Tab(tab) ?? throw new ArgumentException($"No tab '{tab}' in {key}.");

    /// <summary>A cell by A1 reference ("C18").</summary>
    public static SheetCell Cell(string key, string tab, string a1)
    {
        Assert.True(XlsxReader.TryParseA1(a1, out var pos), a1);
        return Tab(key, tab).Cell(pos.Row, pos.Col);
    }

    private static string FindRoot()
    {
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(dir, "tests", "fixtures", "sheets");
            if (Directory.Exists(candidate))
                return candidate;
        }

        throw new DirectoryNotFoundException("tests/fixtures/sheets not found above " + AppContext.BaseDirectory);
    }
}
