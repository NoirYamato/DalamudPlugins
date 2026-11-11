using System.Text;
using MitCue.Core.Data;
using MitCue.Core.Import;

namespace MitCue.PluginTests.Support;

/// <summary>
/// The example sheets' CSV exports (tests/fixtures/sheets/*/export/csv) as tabs, read by a small RFC 4180 reader of
/// its own, so these tests do not depend on the core's sheet readers. Text only (CSV has no colours).
/// </summary>
internal static class FixtureSheets
{
    /// <summary>Carry the Clear's tabs in workbook order: CSV file → tab name.</summary>
    public static readonly (string File, string Tab)[] CarryTheClearTabs =
    [
        ("top-white-mage.csv", "TOP – White Mage"),
        ("top-scholar.csv", "TOP – Scholar"),
        ("top-whm-sch.csv", "TOP – WHM + SCH"),
    ];

    /// <summary>LPDU's tabs in workbook order, with the names an xlsx export gives them ("P1/P2" is "P1P2").</summary>
    public static readonly (string File, string Tab)[] LpduTabs =
    [
        ("aloo.csv", "ALOO"),
        ("p1-p2.csv", "P1P2"),
        ("p3.csv", "P3"),
        ("p4.csv", "P4"),
        ("p5.csv", "P5"),
        ("p6.csv", "P6"),
        ("general-tank-mit-til-p5.csv", "General Tank Mit til P5"),
        ("war-drk.csv", "WARDRK"),
        ("war-gnb.csv", "WARGNB"),
        ("war-pld.csv", "WARPLD"),
        ("drk-gnb.csv", "DRKGNB"),
        ("drk-pld.csv", "DRKPLD"),
        ("gnb-pld.csv", "GNBPLD"),
    ];

    public static SheetDocument CarryTheClear() => Document("carry-the-clear", "Carry the Clear: TOP Healing Plan", CarryTheClearTabs);

    public static SheetDocument Lpdu() => Document("lpdu", "LPDU TOP Mitigation Sheet", LpduTabs);

    /// <summary>One CSV export as rows of cell texts.</summary>
    public static List<List<string>> Rows(string sheet, string file) =>
        ParseCsv(File.ReadAllText(Path.Combine(Root(), sheet, "export", "csv", file), Encoding.UTF8));

    /// <summary>Rows as a tab.</summary>
    public static SheetTab Tab(string name, IEnumerable<IReadOnlyList<string>> rows) => new()
    {
        Name = name,
        Rows = rows.Select(r => (IReadOnlyList<SheetCell>)r.Select(t => string.IsNullOrEmpty(t) ? SheetCell.Empty : new SheetCell { Text = t }).ToList()).ToList(),
    };

    private static SheetDocument Document(string sheet, string title, (string File, string Tab)[] tabs) => new()
    {
        Title = title,
        Tabs = tabs.Select(t => Tab(t.Tab, Rows(sheet, t.File))).ToList(),
    };

    private static string Root() =>
        Path.Combine(Path.GetDirectoryName(DirectoryDataSource.FindRepoData().Root)!, "tests", "fixtures", "sheets");

    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = [];
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
