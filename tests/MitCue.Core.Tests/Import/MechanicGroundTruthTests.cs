using MitCue.Core.Import;
using MitCue.Core.Tests.Data;

namespace MitCue.Core.Tests.Import;

/// <summary>
/// Every labelled row of both example TOP sheets resolves to the mechanic that docs/research/mechanics.md §4.1 (Carry the
/// Clear, 47 rows in each of the three tabs) and §4.4 (LPDU, 98 rows in 12 tabs) give for it, and every row of LPDU's TEA
/// sheet (32 rows in 2 tabs) to the mechanic the TEA catalog was reconciled with.
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

    /// <summary>
    /// LPDU's TEA sheet: (tab, 1-based row) → mechanic id, as the TEA catalog was reconciled against it (data/fights/tea.yaml;
    /// repeated labels by row order, "Jwaves (N)" by Brute Justice's stack count). Row 41 repeats row 39's label with no calls.
    /// </summary>
    private static readonly Dictionary<(string Tab, int Row), string> TeaTruth = new()
    {
        [("LL/BJCC", 11)] = "p1.cascade_1", // Cascade
        [("LL/BJCC", 13)] = "p1.doll_feeds", // Doll Feeds
        [("LL/BJCC", 15)] = "p1.fluid_swing_3", // Fluid Swing 3
        [("LL/BJCC", 17)] = "p1.protean_1", // Protean 1
        [("LL/BJCC", 19)] = "p1.splash_1", // Splashes
        [("LL/BJCC", 21)] = "p1.cascade_2", // Cascade
        [("LL/BJCC", 23)] = "p1.protean_2", // Protean 2
        [("LL/BJCC", 36)] = "p2.j_kick", // JKick
        [("LL/BJCC", 38)] = "p2.whirlwind_1", // Whirlwind
        [("LL/BJCC", 40)] = "p2.photon_1", // Photon
        [("LL/BJCC", 42)] = "p2.water_thunder_1", // Water 1
        [("LL/BJCC", 44)] = "p2.hidden_mines", // Mines
        [("LL/BJCC", 46)] = "p2.water_thunder_2", // Water 2
        [("LL/BJCC", 48)] = "p2.whirlwind_2", // Whirlwind
        [("LL/BJCC", 50)] = "p2.water_thunder_3", // Water 3
        [("LL/BJCC", 52)] = "p2.gavel", // Gavel Lineup
        [("LL/BJCC", 54)] = "p2.double_rocket_punch", // Double Rocket Punch
        [("LL/BJCC", 56)] = "p2.whirlwind_3", // Whirlwind
        [("Alex Prime/PA", 11)] = "p3.inception_crystals", // Inception (Crystals Boom)
        [("Alex Prime/PA", 13)] = "p3.incinerating_heat", // Post-Wormhole Stack
        [("Alex Prime/PA", 15)] = "p3.mega_holy_1", // Mega Holy
        [("Alex Prime/PA", 17)] = "p3.j_storm", // JKick
        [("Alex Prime/PA", 19)] = "p3.j_wave_1", // Jwaves (0-3)
        [("Alex Prime/PA", 21)] = "p3.j_wave_5", // Jwaves (4)
        [("Alex Prime/PA", 23)] = "p3.j_wave_7", // Jwaves (6)
        [("Alex Prime/PA", 25)] = "p3.j_wave_9", // Jwaves (8)
        [("Alex Prime/PA", 27)] = "p3.divine_judgment", // Transition Raidwide
        [("Alex Prime/PA", 39)] = "p4.optical_sight", // Optical Sight (Stack/Spread)
        [("Alex Prime/PA", 41)] = "p4.optical_sight", // Optical Sight (Stack/Spread)
        [("Alex Prime/PA", 43)] = "p4.fate_beta", // Fate Calibration Beta
        [("Alex Prime/PA", 45)] = "p4.irresistible_grace_1", // Irresistable Grace 1
        [("Alex Prime/PA", 47)] = "p4.irresistible_grace_2", // Irresistable Grace 2
    };

    [Fact]
    public void EveryLabelledRowOfTheTeaSheet_MapsToTheReconciledMechanic()
    {
        var imported = PlanImports.LpduTea.Rows.Where(r => r.Label is not null).ToList();
        var actual = imported.ToDictionary(r => (r.Tab, r.Row), r => r.Mechanic);
        Assert.Equal(TeaTruth.Keys.Order(), actual.Keys.Order()); // every labelled row, nothing else
        foreach (var ((tab, row), mechanic) in TeaTruth)
            Assert.True(mechanic == actual[(tab, row)], $"'{tab}' row {row}: expected {mechanic}, got {actual[(tab, row)]}");
        Assert.DoesNotContain(imported, r => r.Match is "unknown" or "fuzzy" or "other-phase");

        // Every entry is on its row's mechanic ("'LL/BJCC'!C11" → row 11 of 'LL/BJCC').
        foreach (var entry in PlanImports.LpduTea.Plan.Entries)
        {
            var bang = entry.Src!.LastIndexOf('!');
            Assert.True(XlsxReader.TryParseA1(entry.Src[(bang + 1)..], out var cell), entry.Src);
            Assert.True(TeaTruth[(entry.Src[1..(bang - 1)], cell.Row + 1)] == entry.Mechanic, $"{entry.Id} ({entry.Src}) is on {entry.Mechanic}");
        }
    }

    [Fact]
    public void UnlabelledRows_BelongToTheRowAbove()
    {
        var rows = PlanImports.Lpdu.Rows.Where(r => r.Label is null).ToList();
        var row = Assert.Single(rows); // 'General Tank Mit til P5' row 3: "Rampart as P1 dies"
        Assert.Equal(("General Tank Mit til P5", 3, "p1.pantokrator", "previous"), (row.Tab, row.Row, row.Mechanic, row.Match));
    }
}
