using MitCue.Core.Import;
using MitCue.Core.Tests.Data;

namespace MitCue.Core.Tests.Import;

/// <summary>
/// Every labelled row of both example sheets resolves to the mechanic that docs/research/mechanics.md §4.1 (Carry the
/// Clear, 47 rows in each of the three tabs) and §4.4 (LPDU, 98 rows in 12 tabs) give for it.
/// </summary>
public class MechanicGroundTruthTests
{
    private static readonly Dictionary<string, string> CtcTabs = new()
    {
        ["White Mage"] = "TOP – White Mage",
        ["Scholar"] = "TOP – Scholar",
        ["WHM + SCH"] = "TOP – WHM + SCH",
    };

    /// <summary>(sheet, tab, 1-based row) → mechanic id, from the research tables.</summary>
    private static Dictionary<(string Sheet, string Tab, int Row), string> Truth()
    {
        var truth = new Dictionary<(string, string, int), string>();
        foreach (var line in RepoFiles.ReadDoc("research", "mechanics.md").Split('\n'))
        {
            var cells = line.Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length < 8 || cells[1] is not ("Carry the Clear" or "LPDU") || !int.TryParse(cells[3], out var row))
                continue;
            var mechanic = cells[6].Trim('`');
            if (cells[1] == "LPDU")
            {
                truth[("lpdu", cells[2], row)] = mechanic;
                continue;
            }

            foreach (var tab in cells[2].Split(", "))
                truth[("carry-the-clear", CtcTabs[tab], row)] = mechanic;
        }

        return truth;
    }

    [Theory]
    [InlineData("carry-the-clear", 141)]
    [InlineData("lpdu", 98)]
    public void EveryLabelledRow_MapsToTheResearchedMechanic(string sheet, int rows)
    {
        var truth = Truth().Where(t => t.Key.Sheet == sheet).ToDictionary(t => (t.Key.Tab, t.Key.Row), t => t.Value);
        Assert.Equal(rows, truth.Count);

        var options = PlanImports.Options(null, null, null, sheet == "carry-the-clear" ? "carry_the_clear" : null);
        var imported = new PlanImporter().Import(Fixtures.Doc(sheet), options).Rows.Where(r => r.Label is not null).ToList();
        var actual = imported.ToDictionary(r => (r.Tab, r.Row), r => r.Mechanic);
        Assert.Equal(truth.Keys.Order(), actual.Keys.Order()); // every labelled row, nothing else
        foreach (var ((tab, row), mechanic) in truth)
            Assert.True(mechanic == actual[(tab, row)], $"'{tab}' row {row}: expected {mechanic}, got {actual[(tab, row)]}");
        Assert.DoesNotContain(imported, r => r.Match is "unknown" or "fuzzy" or "other-phase");
    }

    [Fact]
    public void UnlabelledRows_BelongToTheRowAbove()
    {
        var rows = PlanImports.Lpdu.Rows.Where(r => r.Label is null).ToList();
        var row = Assert.Single(rows); // 'General Tank Mit til P5' row 3: "Rampart as P1 dies"
        Assert.Equal(("General Tank Mit til P5", 3, "p1.pantokrator", "previous"), (row.Tab, row.Row, row.Mechanic, row.Match));
    }
}
