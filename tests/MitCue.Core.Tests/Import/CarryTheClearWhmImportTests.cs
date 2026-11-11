using System.Globalization;
using MitCue.Core.Import;
using MitCue.Core.Plans;

namespace MitCue.Core.Tests.Import;

/// <summary>
/// Acceptance test of the importer on Carry the Clear's 'TOP – White Mage' tab: every entry of the plan, with its
/// mechanic, action, offset and hit index worked out by hand from the sheet, data/fights/top.yaml and
/// docs/research/mechanics.md §4 (row-relative offsets: <c>t_item − t_row + δ</c>; the hit is the first one at or after
/// the press, or for heals the last one before it).
/// </summary>
public class CarryTheClearWhmImportTests
{
    private const string Tab = "'TOP – White Mage'!";

    /// <summary>
    /// Cell, mechanic, action(s), hit index, offset ("auto" = automatic), and flags: T=target (after '='), W=when ready,
    /// O=optional, N=note (after '=').
    /// </summary>
    public static TheoryData<string, string, string, int, string, string> Expected => new()
    {
        // P1 (timer from pull; zero 0.9). Program Loop row: 0:28/0:37/0:46/0:55 = the four sets (29.1 … 56.1).
        { "C17", "p1.program_loop", "Asylum", 2, "-6", "" },                                    // ~0:40 → 41.1, before set 3 (47.1)
        { "C17", "p1.program_loop", "Afflatus Solace+Divine Benison", 0, "auto", "T=your 3rd/4th-in-line player" },
        { "C18", "p1.pantokrator", "Temperance", 0, "-3", "" },                                 // Temperance (1:18) on Pantokrator (1:21)
        { "C18", "p1.pantokrator", "Liturgy of the Bell", 0, "1", "N=right after the 1st stack" },
        { "C18", "p1.pantokrator", "Plenary Indulgence", 2, "-2", "" },                         // 1:31, before the 3rd stack (1:33)
        { "C19", "p1.wave_cannon_kyrios", "Benediction", 1, "1", "T=a tank" },                 // 1:57, after the last cleave (1:56)

        // P2 (from phase start; zero 201.8).
        { "C22", "p2.solar_ray", "Divine Benison", 0, "-5", "T=the tanks" },
        { "C22", "p2.solar_ray", "Aquaveil", 0, "-3", "T=the tanks" },
        { "C24", "p2.party_synergy_stacks", "Plenary Indulgence", 0, "-2", "" },
        { "C24", "p2.party_synergy_stacks", "Asylum", 0, "2", "" },
        { "C25", "p2.bladedance", "Divine Benison", 0, "-5", "O T=the Bladedance tank" },
        { "C25", "p2.bladedance", "Aquaveil", 0, "-4", "O T=the Bladedance tank" },
        { "C26", "p2.flares", "Temperance", 0, "-9", "" },
        { "C27", "p2.cosmo_memory", "Plenary Indulgence", 0, "-2", "" },                        // timed at the cast end, like its anchor: δ = 0

        // P3 (from phase start; zero 399.4). Hello World rounds are timed at the Latent Defect cast end, like their anchors.
        { "C30", "p3.transition", "Asylum", 1, "1.9", "" },                                      // 0:31 → 430.3, after both hits
        { "C31", "p3.hello_world", "Plenary Indulgence", 0, "-3", "W" },
        { "C33", "p3.latent_2", "Temperance", 0, "-1", "W" },
        { "C34", "p3.latent_3", "Liturgy of the Bell", 1, "-0.5", "" },                         // 1:49 → 508.5, before the tether breaks (509.0)
        { "C35", "p3.latent_4", "Asylum", 0, "-4", "" },
        { "C36", "p3.critical_error", "Plenary Indulgence", 0, "-3", "" },

        // P4 (from phase start). The label lists the six hits: lasers 0:15/0:25/0:35, stacks 0:20/0:30/0:40.
        { "C40", "p4.wave_cannon", "Temperance", 1, "-1", "W" },                                // 0:19, before the 0:20 stack
        { "C40", "p4.wave_cannon", "Asylum", 3, "-4.3", "W" },                                  // 0:26 → 632.5, before the 0:30 stack (636.8)
        { "C40", "p4.wave_cannon", "Plenary Indulgence", 3, "-2", "" },

        // P5 (from boss targetable; zero 714.7, drifting: row-relative offsets are immune to it).
        { "C43", "p5.solar_ray_1", "Divine Benison", 0, "-1", "T=tank" },                       // a tankbuster: the target is the tank
        { "C43", "p5.solar_ray_1", "Aquaveil", 0, "-1", "O T=tank" },
        { "C44", "p5.delta", "Plenary Indulgence", 0, "-3", "" },
        { "C45", "p5.delta_all", "Liturgy of the Bell", 0, "-2", "" },
        { "C45", "p5.delta_all", "Asylum", 7, "0", "" },                                         // 1:12 → 787.0 = the first Hello World hit
        { "C46", "p5.solar_ray_2", "Divine Benison", 0, "-3", "T=tank" },                      // 'Solar Ray ×2 ★★★' (×3) told apart by time
        { "C46", "p5.solar_ray_2", "Aquaveil", 0, "-2", "O T=tank" },
        { "C47", "p5.sigma", "Temperance", 0, "-18", "" },
        { "C47", "p5.sigma", "Plenary Indulgence", 0, "-3", "" },
        { "C50", "p5.sigma_hello_world", "Asylum", 2, "0", "" },
        { "C51", "p5.solar_ray_3", "Benediction", 1, "1", "T=the tank" },                       // "Bene the tank after the second hit."
        { "C52", "p5.omega", "Plenary Indulgence", 0, "-3", "" },
        { "C53", "p5.omega_all", "Temperance", 7, "-1", "O" },                                  // 4:09 via the range end 4:12 (965.4)
        { "C54", "p5.solar_ray_4", "Divine Benison", 0, "-3", "T=tank" },
        { "C54", "p5.solar_ray_4", "Aquaveil", 0, "-1", "T=tank" },
        { "C55", "p5.blind_faith", "Asylum", 0, "-16.2", "" },                                  // row timed at the 7B87 cast end: δ = −1.2
        { "C55", "p5.blind_faith", "Liturgy of the Bell", 0, "-7.2", "" },
        { "C55", "p5.blind_faith", "Plenary Indulgence", 0, "-4.2", "" },

        // P6 (from Blind Faith; zero 1099.3).
        { "C57", "p6.cosmo_memory", "Plenary Indulgence", 0, "-3", "" },                        // Plenary (1:05) for Cosmo Memory (1:08)
        { "C59", "p6.cosmo_dive_1", "Temperance", 0, "-9", "" },
        { "C59", "p6.cosmo_dive_1", "Divine Benison", 0, "-2", "" },
        { "C60", "p6.wave_cannon_1", "Plenary Indulgence", 0, "0", "" },
        { "C60", "p6.wave_cannon_1", "Asylum", 2, "-5.3", "" },
        { "C60", "p6.wave_cannon_1", "Divine Benison", 2, "-1", "" },
        { "C61", "p6.wave_cannon_2", "Divine Benison", 2, "-2", "" },
        { "C61", "p6.wave_cannon_2", "Benediction", 2, "3", "T=the front tank" },
        { "C63", "p6.cosmo_dive_2", "Aquaveil", 0, "-4", "" },
        { "C63", "p6.cosmo_dive_2", "Divine Benison", 0, "-3", "" },
        { "C63", "p6.cosmo_dive_2", "Plenary Indulgence", 0, "-3", "" },
        { "C64", "p6.cosmo_meteor", "Liturgy of the Bell", 0, "-4", "" },
        { "C64", "p6.cosmo_meteor", "Asylum", 0, "-3", "" },
        { "C64", "p6.cosmo_meteor", "Temperance", 0, "-2", "" },
        { "C66", "p6.magic_number_1", "Pulse of Life", 0, "4", "N=after the hit" },            // 'Healer LB3 (≈4:32, after the hit)'
        { "C67", "p6.magic_number_2", "Plenary Indulgence", 0, "-6", "" },
    };

    private static MitPlan Plan => PlanImports.Whm.Plan;

    [Theory]
    [MemberData(nameof(Expected))]
    public void Entry_HasTheExpectedMechanicActionAndTime(string cell, string mechanic, string actions, int hit, string offset, string flags)
    {
        var entry = Assert.Single(Plan.At(Tab + cell), e => string.Join("+", e.Names()) == actions);
        Assert.Equal(mechanic, entry.Mechanic);
        Assert.Equal(hit, entry.HitIndex ?? 0);
        if (offset == "auto")
        {
            Assert.Null(entry.Offset);
        }
        else
        {
            var expected = double.Parse(offset, CultureInfo.InvariantCulture);
            Assert.NotNull(entry.Offset);
            Assert.InRange(entry.Offset.Value, expected - 0.6, expected + 0.6);
        }

        Assert.Equal(Flag(flags, 'T'), entry.Target);
        Assert.Equal(flags.Split(' ').Contains("W"), entry.WhenReady);
        Assert.Equal(flags.Split(' ').Contains("O"), entry.Optional);
        if (Flag(flags, 'N') is { } note)
            Assert.Equal(note, entry.Note);
        Assert.False(entry.AtPhaseEnd);
        Assert.Equal(Job.WHM, entry.Who.Job);
        Assert.Equal("White Mage", entry.Who.Assignee);
        Assert.Null(entry.Who.Slot);
    }

    [Fact]
    public void Table_CoversEveryEntryAndEveryPhase()
    {
        var expected = Expected.Select(row => ((string)row[0], (string)row[2])).ToList();
        var actual = Plan.Entries.Select(e => (e.Src!.Replace(Tab, string.Empty, StringComparison.Ordinal), string.Join("+", e.Names()))).ToList();
        Assert.Equal(expected.Order(), actual.Order());
        Assert.True(expected.Count >= 30);
        Assert.Equal(["p1", "p2", "p3", "p4", "p5", "p6"], Plan.Entries.Select(e => PlanImports.Top.GetMechanic(e.Mechanic)!.Phase).Distinct().Order());
    }

    [Fact]
    public void Entries_AreInTimeOrderWithStableIds()
    {
        Assert.Equal(Enumerable.Range(1, Plan.Entries.Count).Select(i => "e" + i.ToString("000", CultureInfo.InvariantCulture)), Plan.Entries.Select(e => e.Id));
        var press = Plan.Entries.Where(e => e.Offset is not null).Select(e => e.Press(PlanImports.Top)!.Value).ToList();
        Assert.Equal(press.Order(), press);

        var again = new PlanImporter().Import(Fixtures.CarryTheClear, PlanImports.Bundled[0].Options).Plan;
        Assert.Equal(PlanSerializer.ToYaml(Plan), PlanSerializer.ToYaml(again));
    }

    [Fact]
    public void Plan_HeaderAndAssignee()
    {
        Assert.Equal("Carry the Clear – WHM", Plan.Name);
        Assert.Equal("top", Plan.Fight);
        Assert.Equal("google-sheet", Plan.Source!.Kind);
        Assert.Equal(PlanImports.CtcUrl + "#gid=777100", Plan.Source.Url);
        Assert.Equal(["TOP – White Mage"], Plan.Source.Tabs);
        Assert.Equal("2026-11-11", Plan.Source.Imported);
        var assignee = Assert.Single(Plan.Assignees);
        Assert.Equal(("White Mage", (string?)null, (Job?)Job.WHM), (assignee.Key, assignee.Slot, assignee.Job));
        Assert.True(Guid.TryParse(Plan.Id, out _));
    }

    [Fact]
    public void Entries_KeepSeverityDetailAndSource()
    {
        var temperance = Assert.Single(Plan.At(Tab + "C18"), e => e.Names()[0] == "Temperance");
        Assert.Equal(3, temperance.Severity); // ★★★
        Assert.StartsWith("Shields cover the 1st stack", temperance.Detail, StringComparison.Ordinal);
        Assert.Equal(4, Assert.Single(Plan.At(Tab + "C57")).Severity); // ★★★★
        Assert.Equal(2, Assert.Single(Plan.At(Tab + "C50")).Severity); // ★★
        Assert.All(Plan.Entries, e => Assert.StartsWith(Tab, e.Src, StringComparison.Ordinal));
    }

    [Fact]
    public void PhaseCalibration_MatchesTheResearchedZeroPoints()
    {
        // docs/research/mechanics.md §4.2: p1 0.9, p2 201.8, p3 399.4, p4 606.7, p5 714.7, p6 1099.3 (medians of the A/B/C
        // rows; the importer also counts the rows corrected by δ). Spread ≤ 0.9 s in P1–P4, up to 2.4 s in P5–P6.
        var zeros = PlanImports.Whm.PhaseZeros;
        var expected = new Dictionary<string, double> { ["p1"] = 0.9, ["p2"] = 201.8, ["p3"] = 399.4, ["p4"] = 606.7, ["p5"] = 714.7, ["p6"] = 1099.3 };
        foreach (var (phase, zero) in expected)
            Assert.InRange(zeros[phase], zero - 0.2, zero + 0.2);
        foreach (var phase in new[] { "p1", "p2", "p3", "p4" })
            Assert.InRange(PlanImports.Whm.PhaseSpread[phase], 0, 0.9);
        foreach (var phase in new[] { "p5", "p6" })
            Assert.InRange(PlanImports.Whm.PhaseSpread[phase], 0, 2.4);
    }

    [Fact]
    public void RepeatedLabels_AreToldApartByTime()
    {
        var diagnostics = PlanImports.Whm.Diagnostics.Where(d => d.Code == "ambiguous-mechanic").ToList();
        Assert.Equal(5, diagnostics.Count); // Solar Ray ×2 ★★★ (3 rows) and Magic Number (252k) ★★★★ (2 rows)
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Info, d.Severity));
        Assert.Equal("p5.solar_ray_3", Assert.Single(Plan.At(Tab + "C51")).Mechanic);
        Assert.Equal("p6.magic_number_2", Assert.Single(Plan.At(Tab + "C67")).Mechanic);
    }

    /// <summary>The value of "T=…" or "N=…", which is always the last flag.</summary>
    private static string? Flag(string flags, char key) =>
        flags.IndexOf(key + "=", StringComparison.Ordinal) is var i and >= 0 ? flags[(i + 2)..] : null;
}
