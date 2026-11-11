using System.Diagnostics;
using System.Text.RegularExpressions;
using MitCue.Core.Fights;
using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;
using MitCue.Core.Tests.Data;

namespace MitCue.Core.Tests.Import;

/// <summary>
/// Regression tests for the review of the importer's back half against the example sheets: each test names the cell and
/// what the sheet means there (docs/research/mechanics.md §4, the cell's notes).
/// </summary>
public class ImportReviewSheetTests
{
    private static double PlannedPress(PlanEntry e)
    {
        var hit = PlanImports.Top.GetMechanic(e.Mechanic)!.HitAt(e.HitIndex ?? 0);
        if (e.Offset is { } o)
            return hit + o;
        var first = e.Do.Select(i => i.Action ?? i.Any?.FirstOrDefault()?.Action).FirstOrDefault(a => a is not null);
        return hit + TimingRules.DefaultOffset(first is { } id ? RepoFiles.Actions.Get(id) : null, 90);
    }

    private static IReadOnlyList<Reminder> Reminders(MitPlan plan, Job job, string? slot) =>
        new ReminderScheduler(PlanImports.Top, PlanSelection.For(plan, new PlayerContext(job, 90, slot)).ToList(), RepoFiles.Actions, new ReminderOptions()).All;

    // 'TOP – White Mage'!C17 "Solace + Benison (your 3rd/4th-in-line player)"; D17: "Heal the 3rd- and 4th-in-line players
    // before their towers ... (e.g. you take #4)". The towers of sets 3 and 4 are p1.program_loop hits 2 and 3 (47.1, 56.1).
    [Fact]
    public void Whm_C17_SolaceBenison_IsForThe3rdAnd4thTowers()
    {
        var e = Assert.Single(PlanImports.Whm.Plan.At("'TOP – White Mage'!C17"), x => x.Names().Contains("Afflatus Solace"));
        Assert.Equal((3, -4.0), (e.HitIndex!.Value, e.Offset!.Value)); // before the 4th tower, with Benison's shield lead
        Assert.InRange(PlannedPress(e), 47.1, 56.1);

        // A Benison before either the 3rd tower (after 42.1, when it would still last) or the 4th counts.
        var r = Assert.Single(Reminders(PlanImports.Whm.Plan, Job.WHM, null), x => x.Entry.Src == "'TOP – White Mage'!C17" && x.Item.Name == "Divine Benison");
        Assert.True(r.Earliest <= 44.0 && 52.0 <= r.Latest && r.Latest < 56.1, $"window [{r.Earliest:0.0}, {r.Latest:0.0}]");
    }

    // 'P3'!B8 "WAR can use Shake as P3 dies for 1st P4 Proteans." (a note row): mechanics.md §4.6 item 6, like "Rampart as P1
    // dies", an optional phase-end action for a Warrior in either tank slot.
    [Fact]
    public void Lpdu_P3_B8_ShakeAsP3Dies_IsImported()
    {
        var e = Assert.Single(PlanImports.Lpdu.Plan.Entries, x => x.Src == "'P3'!B8");
        Assert.Equal(("p3.ion_efflux", true, true, Job.WAR), (e.Mechanic, e.AtPhaseEnd, e.Optional, e.Who.Job!.Value));
        Assert.Equal(["Shake It Off"], e.Names());
        Assert.Null(e.Target); // "for 1st P4 Proteans" counts proteans, it names no player
        Assert.Contains(e, PlanSelection.For(PlanImports.Lpdu.Plan, new PlayerContext(Job.WAR, 90, "MT", null)));
        Assert.DoesNotContain(e, PlanSelection.For(PlanImports.Lpdu.Plan, new PlayerContext(Job.DRK, 90, "MT", null)));
    }

    // 'P4'!F9 "Shields bevor the boss is targetable", in the H2 SCH column: it must reach the Scholar's plan.
    [Fact]
    public void Lpdu_P4_F9_ScholarNote_IsKept()
    {
        var mine = PlanSelection.For(PlanImports.Lpdu.Plan, new PlayerContext(Job.SCH, 90, "H2", null)).ToList();
        Assert.Contains(mine, e => (e.Detail ?? string.Empty).Contains("Shields bevor", StringComparison.Ordinal));

        // The other note rows in a player's column (no action with a time) are kept the same way.
        var plan = PlanImports.Lpdu.Plan;
        Assert.Contains(plan.Entries, e => e.Who.Assignee == "MT" && (e.Detail ?? string.Empty).Contains("15 sec Mit is used as the clone jumps", StringComparison.Ordinal));
        Assert.Contains(plan.Entries, e => e.Who.Assignee == "H1 WHM" && (e.Detail ?? string.Empty).Contains("Every damage instance has a GCD shield", StringComparison.Ordinal));
        Assert.Contains(plan.Entries, e => e.Who.Assignee == "H1 AST" && (e.Detail ?? string.Empty).Contains("AST can shift Neutralsect", StringComparison.Ordinal));

        // 'P1/P2'!G12 is under M2, which has no P2 entry to carry it: a note entry where the note says ("after Party Synergy").
        var g12 = Assert.Single(plan.At("'P1/P2'!G12"));
        Assert.Equal(("p2.party_synergy_spreads", "M2", PlanImporter.AfterHitOffset), (g12.Mechanic, g12.Who.Slot, g12.Offset!.Value));
        Assert.Equal(["note:Addle and Feint may be used on Female to cover Autos after Party Synergy"], g12.Names());

        // Every text of an assignee column reaches the plan: as an entry, a note entry, a detail or a footnote.
        var texts = PlanImports.Lpdu.Diagnostics.Where(d => d.Code == "note-row").ToList();
        Assert.DoesNotContain(texts, d => d.Message.Contains("shown for review only", StringComparison.Ordinal));
    }

    // 'P6'!B5 "Rep*", 'P6'!C4 "… + Rep*", 'P6'!I5 "Feint*": the red "*" is the red footnote E14/E15 ("*Press 10s Mit when doing
    // the side dodge …", continued "… never too early or you won't catch the Stack!"), not B11, the footnote of B4's plain "*".
    [Theory]
    [InlineData("'P6'!B5")]
    [InlineData("'P6'!C4")]
    [InlineData("'P6'!I5")]
    public void Lpdu_P6_RedAsterisk_GetsThe10sMitFootnote(string src)
    {
        var entries = PlanImports.Lpdu.Plan.At(src).Where(e => e.Names().Any(n => n is "Reprisal" or "Feint")).ToList();
        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Contains("Press 10s Mit", e.Note ?? "<null>", StringComparison.Ordinal));
        Assert.All(entries, e => Assert.Contains("never too early or you won't catch the Stack!", e.Note ?? "<null>", StringComparison.Ordinal));
        Assert.All(entries, e => Assert.DoesNotContain("WAR/PLD use Mit", e.Note ?? string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void Lpdu_P6_PlainAsterisk_KeepsItsFootnote()
    {
        var b4 = PlanImports.Lpdu.Plan.At("'P6'!B4");
        Assert.Equal(4, b4.Count);
        Assert.All(b4, e => Assert.Equal("after 6th puddle; *WAR/PLD use Mit after Protean befor Stack", e.Note));
    }

    // 'TOP – WHM + SCH'!C51 "Protraction + Recit (2:44–2:46) → Adlo (2:50)": the Adlo time (2:50) is kept.
    [Fact]
    public void WhmSch_C51_AdloStepTime_IsKept()
    {
        var e = Assert.Single(PlanImports.WhmSch.Plan.At("'TOP – WHM + SCH'!C51"), x => x.Who.Job == Job.SCH);
        Assert.Equal(-16, e.Offset!.Value, 1);
        Assert.Equal("Adlo 2:50", e.Note);
    }

    // 'P5'!E4 "Macrocosmos + Neutralsect if P4 skip" / 'P5'!E5 "Neutralsect no skip": alternatives (Neutral Sect, 120 s recast,
    // 26 s apart). Neither Neutral Sect is mandatory, and the condition is Neutral Sect's alone.
    [Fact]
    public void Lpdu_P5_ConditionalNeutralSect_NotBothMandatory()
    {
        var ast = PlanSelection.For(PlanImports.Lpdu.Plan, new PlayerContext(Job.AST, 90, "H1", null))
            .Where(e => e.Mechanic is "p5.delta_patch" or "p5.delta_hello_world").ToList();
        Assert.All(ast.Where(e => e.Names().Contains("Neutral Sect")), e => Assert.True(e.Optional));
        Assert.Equal(2, ast.Count(e => e.Names().SequenceEqual(["Neutral Sect"])));
        Assert.False(Assert.Single(ast, e => e.Names().Contains("Macrocosmos")).Optional);
    }

    // R2 has physical ranged and casters; Addle is caster-only ('P1/P2'!I7, 'P3'!K2/K7, 'P5'!K3/K9, 'P6'!K3/K6).
    [Fact]
    public void Lpdu_R2_Addle_NotForPhysicalRanged()
    {
        foreach (var job in new[] { Job.BRD, Job.MCH, Job.DNC })
        {
            var mine = PlanSelection.For(PlanImports.Lpdu.Plan, new PlayerContext(job, 90, "R2", null)).ToList();
            var bad = mine.Where(e => e.Do.Any(i => i.Action is { } id && RepoFiles.Actions.Get(id) is { } a && !a.UsableBy(job))).ToList();
            Assert.True(bad.Count == 0, string.Join(" | ", bad.Select(e => $"{e.Src} {string.Join(",", e.Names())}")));
        }

        var blm = PlanSelection.For(PlanImports.Lpdu.Plan, new PlayerContext(Job.BLM, 90, "R2", null)).ToList();
        Assert.Equal(7, blm.Count(e => e.Names().SequenceEqual(["Addle"])));
    }

    // A target written for one action is that action's: 'P4'!B2 "Rep + Short CD to R1", 'GNB/PLD'!C6 "Rampart + Bullwark + HS +
    // Intervention Co-Tank", 'P6'!B7 "Party Mit + Rep + Short CD to R1/2".
    [Theory]
    [InlineData("'P4'!B2", "Reprisal", "Intervention")]
    [InlineData("'GNB/PLD'!C6", "Rampart+Bulwark+Holy Sheltron", "Intervention")]
    [InlineData("'P6'!B7", "Divine Veil+Reprisal", "Intervention")]
    public void Lpdu_TargetedToken_GetsItsOwnEntry(string src, string untargeted, string targeted)
    {
        var pld = PlanImports.Lpdu.Plan.At(src).Where(e => e.Who.Job is null or Job.PLD).ToList();
        Assert.Null(Assert.Single(pld, e => string.Join("+", e.Names()) == untargeted).Target);
        Assert.NotNull(Assert.Single(pld, e => string.Join("+", e.Names()) == targeted).Target);
    }

    // 'TOP – WHM + SCH'!E63 "…; tank: Aquaveil + Benison." names the target of D63's Aquaveil and Benison.
    [Fact]
    public void WhmSch_D63_TargetFromTheNotes()
    {
        var d63 = PlanImports.WhmSch.Plan.At("'TOP – WHM + SCH'!D63");
        Assert.Equal("tank", Assert.Single(d63, e => e.Names()[0] == "Aquaveil").Target);
        Assert.Equal("tank", Assert.Single(d63, e => e.Names()[0] == "Divine Benison").Target);
        Assert.Null(Assert.Single(d63, e => e.Names()[0] == "Plenary Indulgence").Target);
    }

    // 'P6'!F4 "Succor Proteans + Soil + Spreadlo Stack": Spreadlo is for the Wild Charge stack (p6.wave_cannon_1 hits[2] =
    // 1244.3), after the proteans (1236.0, 1238.0). An Adloquium at 1240.0 counts.
    [Fact]
    public void Lpdu_P6_F4_SpreadloForTheStack_IsInsideTheWindow()
    {
        var r = Assert.Single(Reminders(PlanImports.Lpdu.Plan, Job.SCH, "H2"), x => x.Entry.Src == "'P6'!F4" && x.Item.Name == "Adloquium");
        Assert.True(r.PressAt > 1238.0 && r.Earliest <= 1240.0 && 1240.0 <= r.Latest, $"window [{r.Earliest:0.0}, {r.Latest:0.0}], press {r.PressAt:0.0}, hit {r.Hit}");
        var succor = Assert.Single(Reminders(PlanImports.Lpdu.Plan, Job.SCH, "H2"), x => x.Entry.Src == "'P6'!F4" && x.Item.Name == "Succor");
        Assert.Equal(1236.0, succor.Hit); // the first protean set
    }
}

/// <summary>Regression tests for the robustness review: odd sheets get correct entries or a warning on the right cell.</summary>
public class ImportReviewRobustnessTests
{
    private const string Whm = "TOP – White Mage";

    private static ImportResult Import(SheetDocument doc, FightDefinition? fight = null, string? calibration = null) =>
        new PlanImporter().Import(doc, new ImportOptions
        {
            Fight = fight ?? PlanImports.Top,
            Actions = RepoFiles.Actions,
            Aliases = RepoFiles.Aliases,
            CalibrationKey = calibration,
            ImportedDate = PlanImports.Date,
        });

    private static SheetDocument Tsv(string tsv, string title = "Probe") =>
        new() { Title = title, Tabs = [SheetReaders.FromDelimited(tsv.ReplaceLineEndings("\n"), '\t', "Pasted")] };

    private static SheetTab SetCell(SheetTab tab, int row, int col, string text)
    {
        var rows = tab.Rows.Select(r => (IReadOnlyList<SheetCell>)r.ToList()).ToList();
        var list = rows[row].ToList();
        while (list.Count <= col)
            list.Add(SheetCell.Empty);
        var old = list[col];
        list[col] = new SheetCell { Text = text, Bg = old.Bg, Fg = old.Fg, Bold = old.Bold, Strike = old.Strike, Runs = [], Note = old.Note };
        rows[row] = list;
        return new SheetTab { Name = tab.Name, Gid = tab.Gid, Index = tab.Index, Rows = rows, Merges = tab.Merges, HiddenRows = tab.HiddenRows, HiddenCols = tab.HiddenCols };
    }

    /// <summary>A tab with its rows permuted: perm[newIndex] = oldIndex.</summary>
    private static SheetTab Permute(SheetTab tab, int[] perm)
    {
        var inverse = new int[perm.Length];
        for (var i = 0; i < perm.Length; i++)
            inverse[perm[i]] = i;
        return new SheetTab
        {
            Name = tab.Name,
            Gid = tab.Gid,
            Index = tab.Index,
            Rows = perm.Select(o => tab.Rows[o]).ToList(),
            Merges = tab.Merges.Select(m => m.R0 == m.R1 ? m with { R0 = inverse[m.R0], R1 = inverse[m.R1] } : m).ToList(),
            HiddenRows = tab.HiddenRows.Select(r => inverse[r]).Order().ToList(),
            HiddenCols = tab.HiddenCols,
        };
    }

    private static ImportResult WhmWith(int row1, int col, string text) =>
        Import(new SheetDocument { Title = "Carry the Clear: TOP Healing Plan", Tabs = [SetCell(Fixtures.Tab("carry-the-clear", Whm), row1 - 1, col, text)] }, calibration: "carry_the_clear");

    private static ImportDiagnostic? Warned(ImportResult r, int row1, string code) =>
        r.Diagnostics.FirstOrDefault(d => d.Severity != DiagnosticSeverity.Info && d.Code == code && d.Src is { } s && Regex.IsMatch(s, $"![A-Z]+{row1}$"));

    // ------------------------------------------------------------------ timed rows against their mechanic

    [Fact]
    public void TimedRow_LabelThatMeansAnotherMechanic_IsAnchoredByItsTime()
    {
        // B43 relabelled with the label of the other three P5 Solar Rays; its times (0:08 / 0:11) are the first one's.
        var r = WhmWith(43, 1, "Solar Ray ×2 ★★★");
        var benison = Assert.Single(r.Plan.At($"'{Whm}'!C43"), e => e.Names()[0] == "Divine Benison");
        Assert.Equal("p5.solar_ray_1", benison.Mechanic);
        Assert.Equal(722.6, benison.Press(PlanImports.Top)!.Value, 0.6); // the sheet's 0:07
        Assert.NotNull(Warned(r, 43, "row-time"));
        Assert.Equal("p5.solar_ray_2", Assert.Single(r.Plan.At($"'{Whm}'!C46"), e => e.Names()[0] == "Divine Benison").Mechanic);
        Assert.Null(Warned(r, 46, "ambiguous-mechanic"));
    }

    [Fact]
    public void TimedRow_TypoInItsTime_IsWarnedAndTimedFromThePhaseZero()
    {
        var r = WhmWith(27, 0, "2:57"); // was 1:57 (P2 Cosmo Memory)
        var plenary = Assert.Single(r.Plan.At($"'{Whm}'!C27"));
        Assert.Equal("p2.cosmo_memory", plenary.Mechanic);
        Assert.Equal(316.8, plenary.Press(PlanImports.Top)!.Value, 0.6);
        Assert.NotNull(Warned(r, 27, "row-time"));
        Assert.True(r.PhaseSpread["p2"] < 2, $"P2 spread {r.PhaseSpread["p2"]}"); // the row is left out of the fit
    }

    [Fact]
    public void TimedRow_ShortenedLabelContradictingItsTime_IsWarned()
    {
        var r = WhmWith(25, 1, "Limitless Synergy"); // row time 1:34 = Bladedance (295.6); the label is p2.limitless (309.7)
        var benison = Assert.Single(r.Plan.At($"'{Whm}'!C25"), e => e.Names()[0] == "Divine Benison");
        Assert.Equal(290.6, benison.Press(PlanImports.Top)!.Value, 0.6); // still the sheet's 1:29, through the phase zero
        Assert.NotNull(Warned(r, 25, "row-time"));
    }

    [Fact]
    public void TimedRows_CactbotPerHitNames_AreAnchoredByTime()
    {
        var dmu = FightLoader.Load(RepoFiles.Data.ReadText("fights/dmu.yaml"), "fights/dmu.yaml");
        var tsv = string.Join("\n",
            "Time\tMechanic\tH1 WHM\tMT",
            "\tP1",
            "0:15\tRevolting Ruin III 1\t\tShort CD (0:13)",
            "0:18\tRevolting Ruin III 2\tBene on MT (0:19)\t",
            "1:05\tHyperdrive 1\t\tShort CD (1:04)",
            "1:07\tHyperdrive 2\t\tRampart (1:06)",
            "1:37\tRevolting Ruin III 1\t\tShort CD (1:35)");
        var r = Import(Tsv(tsv, "DMU"), dmu);
        var bene = Assert.Single(r.Plan.At("'Pasted'!C4"));
        Assert.Equal(("p1.revolting_ruin_1", 19.7), (bene.Mechanic, Math.Round(bene.Press(dmu)!.Value, 1))); // the second hit of the first cast
        Assert.Equal(66.8, Assert.Single(r.Plan.At("'Pasted'!D6")).Press(dmu)!.Value, 0.6);
        Assert.All(r.Plan.At("'Pasted'!D7"), e => Assert.Equal(("p1.revolting_ruin_2", 95.1), (e.Mechanic, Math.Round(e.Press(dmu)!.Value, 1))));
        Assert.NotNull(Warned(r, 4, "row-time"));
        Assert.NotNull(Warned(r, 6, "row-time"));
        Assert.NotNull(Warned(r, 7, "row-time"));
        Assert.True(r.PhaseSpread["p1"] < 1, $"P1 spread {r.PhaseSpread["p1"]}");
    }

    // ------------------------------------------------------------------ action words

    [Fact]
    public void PlainWords_AreNotReadAsShortAliases()
    {
        var r = Import(Tsv("P6\tWAR\tGNB\tWHM\tAST\tPLD\tSGE\nCosmo Memory\tHold\tHold Nebula\tWell timed Asylum\tStay mid\tOver the top\tZone\nCD1\tHold Rampart for Cosmo Dive 2\tHoly\tBulwerk\t\t\tKerahole\n"));
        var actions = r.Plan.Entries.SelectMany(e => e.Names()).ToList();
        Assert.DoesNotContain("Holmgang", actions);
        Assert.DoesNotContain("Heart of Light", actions);
        Assert.DoesNotContain("Liturgy of the Bell", actions);
        Assert.DoesNotContain("Earthly Star", actions);
        Assert.DoesNotContain("Cover", actions);
        Assert.DoesNotContain("Zoe", actions);

        // A real typo still resolves, as a warning to check in review.
        Assert.Contains("Kerachole", actions);
        Assert.Equal(DiagnosticSeverity.Warning, Assert.Single(r.Diagnostics, d => d.Code == "fuzzy-match").Severity);
    }

    [Fact]
    public void TextAfterAnAction_IsAnotherActionOrReported()
    {
        var r = Import(Tsv("P6\tWHM\tSGE\nCosmo Memory\tTemp Bell\tKera Holos\nCD1\tTemp Xyzzy\tKera now\n"));
        Assert.Equal(["Temperance", "Liturgy of the Bell"], Assert.Single(r.Plan.At("'Pasted'!B2")).Names());
        Assert.Equal(["Kerachole", "Holos"], Assert.Single(r.Plan.At("'Pasted'!C2")).Names());
        var xyzzy = Assert.Single(r.Plan.At("'Pasted'!B3"));
        Assert.Equal(["Temperance"], xyzzy.Names());
        Assert.Equal("Xyzzy", xyzzy.Note);
        Assert.Contains(r.Diagnostics, d => d.Src == "'Pasted'!B3" && d.Severity == DiagnosticSeverity.Warning && d.Code == "unresolved-token");
        Assert.DoesNotContain(r.Diagnostics, d => d.Src == "'Pasted'!C3" && d.Severity != DiagnosticSeverity.Info); // "now" is hint text
    }

    // ------------------------------------------------------------------ rows

    [Fact]
    public void ShuffledUntimedRows_RepeatedLabelsAreWarned()
    {
        var tab = Fixtures.Tab("lpdu", "P5");
        int[] perm = [0, 11, 9, 2, 1, 7, 6, 3, 10, 5, 4, 8, 12]; // Blind Faith, Hello World (Omega's), Delta, Solar Ray 1, ...
        perm = [.. perm, .. Enumerable.Range(perm.Length, tab.Rows.Count - perm.Length)];
        var r = Import(new SheetDocument { Title = "LPDU", Tabs = [Permute(tab, perm)] });
        var hw = r.Rows.Where(x => x.Label == "Hello World").ToList();
        Assert.Contains(r.Diagnostics, d => d.Severity == DiagnosticSeverity.Warning && d.Code == "ambiguous-mechanic" && hw.Any(h => h.Src == d.Src));
        Assert.Contains(r.Diagnostics, d => d.Severity == DiagnosticSeverity.Warning && d.Code == "row-order");
    }

    [Fact]
    public void PlusGroupWithAParenthesisedHint_StaysTogether()
    {
        var r = Import(Tsv("P3\tSCH\tWAR\nCritical Error\tRecit + Adlo (just before P3 ends)\tShake + Rampart (as P3 dies)\n"));
        Assert.Equal(2, r.Plan.Entries.Count);
        Assert.All(r.Plan.Entries, e => Assert.Equal(("p3.ion_efflux", true), (e.Mechanic, e.AtPhaseEnd)));
        Assert.Equal(["Recitation", "Adloquium"], Assert.Single(r.Plan.At("'Pasted'!B2")).Names());
    }

    [Fact]
    public void UnplaceableTimeList_GivesOneEntry()
    {
        var r = Import(Tsv("P5\tSCH\nDelta\tSoil (0:28 / 0:37)\n"));
        Assert.Single(r.Plan.Entries);
        Assert.Single(r.Diagnostics, d => d.Code == "untimed");
    }

    [Fact]
    public void ExactLabelOfAnotherPhase_BeatsANearMissInThePhase()
    {
        var r = Import(Tsv("P5\tSCH\nBlind Faith\tSoil\nP5 End\tRecit + Adlo\n"));
        var row = r.Rows.Single(x => x.Label == "P5 End");
        Assert.Equal(("p6.start", "other-phase"), (row.Mechanic, row.Match));
        Assert.Contains(r.Diagnostics, d => d.Src == row.Src && d.Code == "mechanic-phase" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void UnlabelledFirstRow_TakesTheNextRowsMechanic()
    {
        var r = Import(Tsv("P6\tSCH\n\tSoil\nCosmo Memory\tSeraph\n"));
        Assert.Equal("p6.cosmo_memory", Assert.Single(r.Plan.At("'Pasted'!B2")).Mechanic);

        var alone = Import(Tsv("P6\tSCH\n\tSoil\n"));
        Assert.Contains(alone.Diagnostics, d => d.Code == "unlabelled-row" && d.Severity == DiagnosticSeverity.Warning);
    }

    // ------------------------------------------------------------------ columns

    [Theory]
    [InlineData("Healer 2 (SCH)", "H2", Job.SCH)]
    [InlineData("Healer 1 (WHM)", "H1", Job.WHM)]
    [InlineData("Healer 2 - SCH", "H2", Job.SCH)]
    [InlineData("SCH (Healer 2)", "H2", Job.SCH)]
    [InlineData("Main Tank (WAR)", "MT", Job.WAR)]
    public void SlotPhraseWithJobHeader_IsAnAssignee(string header, string slot, Job job)
    {
        Assert.True(AssigneeHeader.TryParse(header, out var h));
        Assert.Equal((slot, (Job?)job, AssigneeKind.SlotAndJob), (h!.Slot, h.Job, h.Kind));
    }

    [Fact]
    public void WrongJobHeader_IsWarnedOnce()
    {
        var r = WhmWith(15, 2, "Scholar");
        var warning = Assert.Single(r.Diagnostics, d => d.Code == "column-job");
        Assert.Equal((DiagnosticSeverity.Warning, $"'{Whm}'!C15"), (warning.Severity, warning.Src));
        Assert.Contains("WHM", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(PlanImports.Whm.Diagnostics, d => d.Code == "column-job");
    }

    // ------------------------------------------------------------------ cost

    [Fact]
    public void NestedParenthesesLabel_DoesNotStallTheImport()
    {
        var label = new string('(', 20000) + "Solar Ray" + new string(')', 20000);
        var sw = Stopwatch.StartNew();
        Import(Tsv("P5\tSCH\n" + label + "\tSoil\n"));
        Assert.True(sw.ElapsedMilliseconds < 2000, $"one 40k-character label took {sw.ElapsedMilliseconds} ms");
    }

    [Theory]
    [InlineData("a (b (c) d) e", "a e")]
    [InlineData("a (b [c] d) e [f]", "a e")]
    [InlineData("a (b (c) d", "a b d")] // unclosed: kept
    [InlineData("a b) c", "a b c")]     // stray closer: kept (then punctuation)
    public void SimplifyLabel_RemovesNestedParentheticalsInOnePass(string label, string expected) =>
        Assert.Equal(expected, ImportText.SimplifyLabel(label));
}
