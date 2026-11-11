using System.Text;
using MitCue.Core.Fights;
using MitCue.Core.Import;

namespace MitCue.Core.Tests.Import;

public class LayoutDetectorTests
{
    private static readonly LayoutDetector Detector = new();

    // Carry the Clear: 5 hidden rows, legend block, header row 15 (C15:C16 merged), six phase sections.
    private const string CtcRows = "hhhhh" + "?" + "GGGGGGG" + "N" + "H" + "S" + "DDDD" + "S" + "DDDDDDD" + "S" + "DDDDDDDDD" + "S" + "DD" + "S" + "DDDDDDDDDDDDD" + "S" + "DDDDDDDDDDDD";
    private const string CtcPhases = "---------------" + "11111" + "22222222" + "3333333333" + "444" + "55555555555555" + "6666666666666";

    /// <summary>Kinds, one character per row: H header, D data, L phase label, S section, N note, F footnote, E empty, h hidden, T title, G legend.</summary>
    private static char KindChar(RowKind k) => k switch
    {
        RowKind.Header => 'H',
        RowKind.Data => 'D',
        RowKind.PhaseLabel => 'L',
        RowKind.Section => 'S',
        RowKind.Note => 'N',
        RowKind.Footnote => 'F',
        RowKind.Empty => 'E',
        RowKind.Hidden => 'h',
        RowKind.Title => 'T',
        RowKind.Legend => 'G',
        _ => '?',
    };

    /// <summary>Column roles: M mechanic, A assignee, N notes, T time, I ignored, P phase, U unknown, E empty.</summary>
    private static char RoleChar(ColumnRole r) => r switch
    {
        ColumnRole.Mechanic => 'M',
        ColumnRole.Assignee => 'A',
        ColumnRole.Notes => 'N',
        ColumnRole.Time => 'T',
        ColumnRole.Ignored => 'I',
        ColumnRole.Phase => 'P',
        ColumnRole.Unknown => 'U',
        ColumnRole.Empty => 'E',
        _ => '?',
    };

    private static string Kinds(TabLayout t) => new(t.Rows.Select(r => KindChar(r.Kind)).ToArray());

    private static string Phases(TabLayout t) => string.Concat(t.Rows.Select(r => r.Phase is { } p ? p.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-"));

    private static string Roles(TabLayout t) => new(t.Columns.Select(c => RoleChar(c.Role)).ToArray());

    private static string Assignees(TabLayout t) =>
        string.Join("|", t.AssigneeColumns.Select(c => $"{c.Assignee!.Key}={c.Assignee.Slot ?? "-"}/{c.Assignee.Job?.ToString() ?? "-"}/{c.Assignee.Role}"));

    public static TheoryData<string, string, int, string, string, string> TabSpecs => new()
    {
        { "lpdu", "ALOO", -1, "", "EEEEEEEEEETEEEENNEENNN", "----------------------" },
        { "lpdu", "P1/P2", 0, "MAAAAAAAA", "HDDDDDDDLDDN", "111111112222" },
        { "lpdu", "P3", 0, "MAAAAAAAAAA", "HDDDDDDNEE", "3333333333" },
        { "lpdu", "P4", 0, "MAAAAAAAAAA", "HDDDDDDDN", "444444444" },
        { "lpdu", "P5", 0, "MAAAAAAAAAA", "HDDDDDDDDDDDN", "5555555555555" },
        { "lpdu", "P6", 0, "MAAAAAAAAAA", "HDDDDDDDDDFNNNN", "666666666666666" },
        { "lpdu", "General Tank Mit til P5", 0, "MAAN", "HDDLDDLDDDDLD", "1112223333344" },
        { "lpdu", "WAR/DRK", 0, "MAAN", "HDEDEDEDLDDDD", "5555555566666" },
        { "lpdu", "WAR/GNB", 0, "MAAN", "HDEDEDEDLDDDD", "5555555566666" },
        { "lpdu", "WAR/PLD", 0, "MAAN", "HDNDNDNDLDDDD", "5555555566666" },
        { "lpdu", "DRK/GNB", 0, "MAAN", "HDEDEDEDLDDDD", "5555555566666" },
        { "lpdu", "DRK/PLD", 0, "MAAN", "HDNDNDNDLDDDD", "5555555566666" },
        { "lpdu", "GNB/PLD", 0, "MAAN", "HDEDNDNDLDDDD", "5555555566666" },
        { "carry-the-clear", "TOP – White Mage", 14, "TMANI", CtcRows.Replace('?', 'E'), CtcPhases },
        { "carry-the-clear", "TOP – Scholar", 14, "TMANI", CtcRows.Replace('?', 'G'), CtcPhases },
        { "carry-the-clear", "TOP – WHM + SCH", 14, "TMAAN", CtcRows.Replace('?', 'G'), CtcPhases },
    };

    [Theory]
    [MemberData(nameof(TabSpecs))]
    public void Detect_FindsHeaderColumnsRowKindsAndPhases(string key, string tab, int headerRow, string roles, string kinds, string phases)
    {
        var layout = Detector.Detect(Fixtures.Tab(key, tab));

        Assert.Equal(headerRow < 0 ? TabKind.NotAPlan : TabKind.Plan, layout.Kind);
        Assert.Equal(headerRow, layout.HeaderRow);
        Assert.Equal(roles, Roles(layout));
        Assert.Equal(kinds, Kinds(layout));
        Assert.Equal(phases, Phases(layout));
        Assert.All(layout.Rows.Where(r => r.Phase is not null), r => Assert.Equal("p" + r.Phase!.Number, r.PhaseId));
        Assert.Equal(Enumerable.Range(0, layout.Tab.Rows.Count), layout.Rows.Select(r => r.Index));
    }

    public static TheoryData<string, string, string> AssigneeSpecs => new()
    {
        { "lpdu", "P1/P2", "MT=MT/-/Tank|OT=OT/-/Tank|H1=H1/-/Healer|H2=H2/-/Healer|M1=M1/-/Melee|M2=M2/-/Melee|R1=R1/-/None|R2=R2/-/None" },
        { "lpdu", "P3", "MT=MT/-/Tank|OT=OT/-/Tank|H1 WHM=H1/WHM/Healer|H1 AST=H1/AST/Healer|H2 SCH=H2/SCH/Healer|H2 SGE=H2/SGE/Healer|M1=M1/-/Melee|M2=M2/-/Melee|R1=R1/-/None|R2=R2/-/None" },
        { "lpdu", "P4", "MT=MT/-/Tank|OT=OT/-/Tank|H1 WHM=H1/WHM/Healer|H1 AST=H1/AST/Healer|H2 SCH=H2/SCH/Healer|H2 SGE=H2/SGE/Healer|M1=M1/-/Melee|M2=M2/-/Melee|R1=R1/-/None|R2=R2/-/None" },
        { "lpdu", "P5", "MT=MT/-/Tank|OT=OT/-/Tank|H1 WHM=H1/WHM/Healer|H1 AST=H1/AST/Healer|H2 SCH=H2/SCH/Healer|H2 SGE=H2/SGE/Healer|M1=M1/-/Melee|M2=M2/-/Melee|R1=R1/-/None|R2=R2/-/None" },
        { "lpdu", "P6", "CM LB Tank=-/-/Tank|CM Rep Tank=-/-/Tank|H1 WHM=H1/WHM/Healer|H1 AST=H1/AST/Healer|H2 SCH=H2/SCH/Healer|H2 SGE=H2/SGE/Healer|M1=M1/-/Melee|M2=M2/-/Melee|R1=R1/-/None|R2=R2/-/None" },
        { "lpdu", "General Tank Mit til P5", "MT=MT/-/Tank|OT=OT/-/Tank" },
        { "lpdu", "WAR/DRK", "WAR=-/WAR/Tank|DRK=-/DRK/Tank" },
        { "lpdu", "WAR/GNB", "WAR=-/WAR/Tank|GNB=-/GNB/Tank" },
        { "lpdu", "WAR/PLD", "WAR=-/WAR/Tank|PLD=-/PLD/Tank" },
        { "lpdu", "DRK/GNB", "DRK=-/DRK/Tank|GNB=-/GNB/Tank" },
        { "lpdu", "DRK/PLD", "DRK=-/DRK/Tank|PLD=-/PLD/Tank" },
        { "lpdu", "GNB/PLD", "GNB=-/GNB/Tank|PLD=-/PLD/Tank" },
        { "carry-the-clear", "TOP – White Mage", "White Mage=-/WHM/Healer" },
        { "carry-the-clear", "TOP – Scholar", "Scholar=-/SCH/Healer" },
        { "carry-the-clear", "TOP – WHM + SCH", "Scholar=-/SCH/Healer|White Mage=-/WHM/Healer" },
    };

    [Theory]
    [MemberData(nameof(AssigneeSpecs))]
    public void Detect_ParsesAssigneeHeaders(string key, string tab, string expected) =>
        Assert.Equal(expected, Assignees(Detector.Detect(Fixtures.Tab(key, tab))));

    [Fact]
    public void Detect_LpduP1P2_LabelsAssignmentsAndTheP2LabelRow()
    {
        var layout = Detector.Detect(Fixtures.Tab("lpdu", "P1/P2"));

        Assert.Equal(
            ["Looper Set 3", "Looper Set 4", "Pantokrator", "1st Stack", "2nd Stack", "3rd Stack", "4th Stack", "Solar Ray", "Limitless"],
            layout.DataRows.Select(r => r.Label));
        Assert.Equal([1, 2], layout.NamePhases);
        Assert.Equal(
            [(1, PhaseSource.HeaderCell, 0, "'P1/P2'!A1"), (2, PhaseSource.LabelRow, 8, "'P1/P2'!A9")],
            layout.Sections.Select(s => (s.Number, s.Source, s.Row, s.Src)));

        var looper = layout.Rows[1];
        var a = Assert.Single(looper.Assignments);
        Assert.Equal("H2", a.Assignee.Key);
        Assert.Equal("Kera/Soil", a.Cell.Text);
        Assert.Equal("'P1/P2'!E2", a.Src);
        Assert.False(a.Spread);
        Assert.Equal("'P1/P2'!A2", looper.Src);

        Assert.Equal(["'P1/P2'!B4", "'P1/P2'!C4", "'P1/P2'!D4", "'P1/P2'!E4", "'P1/P2'!H4", "'P1/P2'!I4"], layout.Rows[3].Assignments.Select(x => x.Src));
        Assert.Empty(layout.Rows[5].Assignments); // "2nd Stack": a mechanic row with nothing assigned is still data

        var note = layout.Rows[11];
        Assert.Equal(RowKind.Note, note.Kind); // white on black under the table
        Assert.Equal(["15 sec Mit is used as the clone jumps", "Addle and Feint may be used on Female to cover Autos after Party Synergy"], note.Texts.Select(t => t.Text));
    }

    [Fact]
    public void Detect_LpduTankTabs_NotesColumnAndNamedJobs()
    {
        var layout = Detector.Detect(Fixtures.Tab("lpdu", "WAR/PLD"));

        Assert.Equal([Job.WAR, Job.PLD], layout.NameJobs);
        Assert.Empty(layout.NamePhases);
        Assert.Equal("If Hallowed isn't up Kitchen Sink instead", layout.Rows[1].Notes);
        Assert.Equal("and Holmgang Solar Ray 2,", layout.Rows[2].Notes); // a notes continuation line
        Assert.Equal(RowKind.Note, layout.Rows[2].Kind);
        Assert.Equal(["Solar Ray 1", "Solar Ray 2", "Solar Ray 3", "Solar Ray 4", "CD1", "WC1", "WC2", "CD2"], layout.DataRows.Select(r => r.Label));
        Assert.Equal(["p5", "p5", "p5", "p5", "p6", "p6", "p6", "p6"], layout.DataRows.Select(r => r.PhaseId));
        Assert.Equal("Use Rampart as Cosmo Arrow cast finishes.", layout.Rows[9].Notes);
    }

    [Fact]
    public void Detect_LpduGeneralTankMit_HeaderPhaseLabelRowsAndALabelLessRow()
    {
        var layout = Detector.Detect(Fixtures.Tab("lpdu", "General Tank Mit til P5"));

        Assert.Empty(layout.NamePhases); // "til P5" is not a phase list
        var row3 = layout.Rows[2];
        Assert.Equal(RowKind.Data, row3.Kind); // "Rampart as P1 dies" in the role fill: an assignment, not a note
        Assert.Null(row3.Label);
        Assert.Equal(2, row3.Assignments.Count);
        Assert.Equal("MT prepull Mit for autos", layout.Rows[1].Notes);
        Assert.Equal([1, 2, 3, 4], layout.Sections.Select(s => s.Number));
        Assert.Equal([PhaseSource.HeaderCell, PhaseSource.LabelRow, PhaseSource.LabelRow, PhaseSource.LabelRow], layout.Sections.Select(s => s.Source));
    }

    [Fact]
    public void Detect_LpduP6_CustomTankColumnsFootnotesAndNotes()
    {
        var layout = Detector.Detect(Fixtures.Tab("lpdu", "P6"));

        Assert.Equal(
            ["'P6'!B11", "'P6'!E14"],
            layout.Footnotes.Select(f => f.Src));
        Assert.Equal("*WAR/PLD use Mit after Protean befor Stack", layout.Footnotes[0].Text);
        Assert.StartsWith("*Press 10s Mit", layout.Footnotes[1].Text, StringComparison.Ordinal);
        Assert.Equal(RowKind.Footnote, layout.Rows[10].Kind);
        Assert.Equal(["Cosmo Memory", "CD1", "WC1", "WC2", "CD2", "Meteors", "Flares", "Magic Number", "Magic Number"], layout.DataRows.Select(r => r.Label));
        var wc1 = layout.Rows[3];
        Assert.Equal(["CM LB Tank", "CM Rep Tank", "H1 AST", "H2 SCH", "H2 SGE", "R1"], wc1.Assignments.Select(a => a.Assignee.Key));
    }

    [Fact]
    public void Detect_Aloo_IsNotAPlan()
    {
        var layout = Detector.Detect(Fixtures.Tab("lpdu", "ALOO"));

        Assert.Equal(TabKind.NotAPlan, layout.Kind);
        Assert.Empty(layout.Columns);
        Assert.Empty(layout.DataRows);
        Assert.Equal("LPDU General TOP Mitigation Guideline", layout.Title);
        var d = Assert.Single(layout.Diagnostics);
        Assert.Equal(("not-a-plan", DiagnosticSeverity.Info), (d.Code, d.Severity));
    }

    [Fact]
    public void Detect_CarryTheClear_SectionsKeepNamesAndZeroPointHints()
    {
        var layout = Detector.Detect(Fixtures.Tab("carry-the-clear", "TOP – Scholar"));

        Assert.Equal(
            [
                (1, "Omega", "timer from pull", 15),
                (2, "Omega-M/F", "from phase start", 20),
                (3, "Omega Reconfigured", "from phase start; boss targetable ≈ 0:33", 28),
                (4, "Blue Screen", "from phase start; always 1:12 long", 38),
                (5, "Run: Dynamis", "from boss targetable; always 4:46 long", 41),
                (6, "Alpha Omega", "from Blind Faith; boss targetable ≈ 0:55", 55),
            ],
            layout.Sections.Select(s => (s.Number, s.Name!, s.ZeroHint!, s.Row)));
        Assert.All(layout.Sections, s => Assert.Equal(PhaseSource.SectionRow, s.Source));
        Assert.Equal("'TOP – Scholar'!B21", layout.Sections[1].Src);
        Assert.Equal("P2 — Omega-M/F  (from phase start)", layout.Sections[1].Text);
        Assert.Equal(15, layout.HeaderRowEnd);
        Assert.Equal("TOP — Scholar", layout.Title);
        Assert.Equal([Job.SCH], layout.NameJobs);
        Assert.Equal(ColumnRole.Ignored, layout.Columns[4].Role);
        Assert.Equal("Top SCHs (of 58)", layout.Columns[4].Header);
    }

    [Fact]
    public void Detect_CarryTheClear_RowTimesSeverityNotesAndAssignments()
    {
        var layout = Detector.Detect(Fixtures.Tab("carry-the-clear", "TOP – White Mage"));

        var panto = layout.Rows[17];
        Assert.Equal("Pantokrator: line stacks + missiles (65k) ★★★ (multi-hit)", panto.Label);
        Assert.Equal("1:21 / 1:27 / 1:33 / 1:39", panto.TimeText);
        Assert.Equal([81.0, 87, 93, 99], panto.Time!.All);
        Assert.Equal(3, panto.Severity);
        Assert.StartsWith("Shields cover the 1st stack", panto.Notes, StringComparison.Ordinal);
        var a = Assert.Single(panto.Assignments); // the statistics column E is ignored
        Assert.Equal("'TOP – White Mage'!C18", a.Src);
        Assert.Equal("'TOP – White Mage'!B18", panto.Src);

        var kyrios = layout.Rows[18];
        Assert.Equal((107.0, 116.0), (kyrios.Time!.Seconds, kyrios.Time.End!.Value));
        Assert.Equal(2, kyrios.Severity);

        var atomic = layout.Rows[19];
        Assert.Equal(RowKind.Data, atomic.Kind);
        Assert.Null(atomic.Severity);
        Assert.Empty(atomic.Assignments);
        Assert.Equal(128, atomic.Time!.Seconds);

        var cosmo = layout.Rows[56];
        Assert.Equal(4, cosmo.Severity);
        Assert.Equal("p6", cosmo.PhaseId);
        Assert.Equal("from Blind Faith; boss targetable ≈ 0:55", cosmo.Phase!.ZeroHint);

        Assert.Equal(47, layout.DataRows.Count());
        Assert.All(layout.DataRows, r => Assert.NotNull(r.Time));
    }

    [Fact]
    public void Detect_FixtureDiagnostics_AreOnlyTheExpectedOnes()
    {
        var lpdu = Detector.Detect(Fixtures.Lpdu);
        Assert.Equal(
            [("not-a-plan", (string?)null), ("missing-mechanic-label", "'General Tank Mit til P5'!B3")],
            lpdu.Diagnostics.Select(d => (d.Code, d.Src)));
        Assert.Equal(12, lpdu.PlanTabs.Count());

        var ctc = Detector.Detect(Fixtures.CarryTheClear);
        Assert.Equal(
            [("ignored-column", "'TOP – White Mage'!E15", DiagnosticSeverity.Info), ("ignored-column", "'TOP – Scholar'!E15", DiagnosticSeverity.Info)],
            ctc.Diagnostics.Select(d => (d.Code, d.Src, d.Severity))); // the "Top WHMs (of 36)" statistics columns
        Assert.Equal(3, ctc.PlanTabs.Count());
        Assert.NotNull(ctc.Tab("top – scholar"));
    }

    [Fact]
    public void Detect_WithAFight_UsesTheFightsPhaseIds()
    {
        var fight = Fight("omega", "omega_mf", "reconfigured", "blue_screen", "dynamis", "alpha_omega");
        var layout = new LayoutDetector(fight).Detect(Fixtures.Tab("carry-the-clear", "TOP – White Mage"));

        Assert.Equal(["omega", "omega_mf", "reconfigured", "blue_screen", "dynamis", "alpha_omega"], layout.Sections.Select(s => s.Id));
        Assert.Equal("dynamis", layout.Rows[42].PhaseId);
        Assert.Equal(["ignored-column"], layout.Diagnostics.Select(d => d.Code));
        Assert.Equal("p9", new LayoutDetector(fight).PhaseId(9));
    }

    [Fact]
    public void Detect_WithAFightWithFewerPhases_ReportsUnknownPhases()
    {
        var layout = new LayoutDetector(Fight("a", "b", "c")).Detect(Fixtures.Tab("lpdu", "WAR/DRK"));

        Assert.Equal(["p5", "p6"], layout.Sections.Select(s => s.Id));
        Assert.Equal(2, layout.Diagnostics.Count(d => d.Code == "unknown-phase" && d.Severity == DiagnosticSeverity.Warning));
    }

    [Theory]
    [InlineData("lpdu")]
    [InlineData("carry-the-clear")]
    public void Detect_XlsxAndCsvSources_GiveTheSameLayoutAsTheJsonFixtures(string key)
    {
        var json = Detector.Detect(Fixtures.Doc(key));
        SheetDocument xlsxDoc;
        using (var stream = File.OpenRead(Fixtures.Xlsx(key)))
            xlsxDoc = SheetReaders.FromXlsx(stream, key);
        var xlsx = Detector.Detect(xlsxDoc);

        for (var t = 0; t < json.Tabs.Count; t++)
        {
            var expected = json.Tabs[t];
            Assert.Equal(Kinds(expected), Kinds(xlsx.Tabs[t])); // "P1P2" (the xlsx name) still names phases 1 and 2
            Assert.Equal(Phases(expected), Phases(xlsx.Tabs[t]));
            Assert.Equal(Assignees(expected), Assignees(xlsx.Tabs[t]));
            Assert.Equal(expected.NameJobs, xlsx.Tabs[t].NameJobs); // "WARDRK" (the xlsx name) still names WAR and DRK

            // Text only (no fills, no hidden rows): notes are told apart by their wording.
            var csvTab = SheetReaders.FromDelimited(File.ReadAllText(Fixtures.Csv(key, Fixtures.FileName(expected.Name))), ',', expected.Name);
            var csv = Detector.Detect(csvTab);
            Assert.Equal(Kinds(expected).Replace('h', 'E'), Kinds(csv));
            Assert.Equal(Phases(expected), Phases(csv));
            Assert.Equal(Assignees(expected), Assignees(csv));
            Assert.Equal(expected.NameJobs, csv.NameJobs);
        }
    }

    // ---- synthetic layouts --------------------------------------------------------------------

    private static SheetTab Synthetic(string name, params string[][] rows) => Synthetic(name, [], [], rows);

    private static SheetTab Synthetic(string name, IReadOnlyList<CellRange> merges, IReadOnlyList<int> hidden, params string[][] rows) => new()
    {
        Name = name,
        Rows = rows.Select(r => (IReadOnlyList<SheetCell>)r.Select(t => t.Length == 0 ? SheetCell.Empty : new SheetCell { Text = t }).ToList()).ToList(),
        Merges = merges,
        HiddenRows = hidden,
    };

    [Fact]
    public void Detect_KeywordHeaders_TimeMechanicPhaseAndNotes()
    {
        var layout = Detector.Detect(Synthetic(
            "Plan",
            ["Phase", "Time", "Mechanic", "MT", "OT", "Notes", "Comments"],
            ["P1", "0:10", "Pull", "Rampart", "", "", ""],
            ["", "1:21", "Pantokrator", "Rep", "Reprisal", "both tanks", ""],
            ["P2", "0:12", "Solar Ray", "", "", "", "x"],
            ["", "~0:57", "Bladework", "Short CD", "", "", ""]));

        Assert.Equal("PTMAANN", Roles(layout));
        Assert.Equal("HDDDD", Kinds(layout));
        Assert.Equal("-1122", Phases(layout));
        Assert.Equal([PhaseSource.PhaseColumn, PhaseSource.PhaseColumn], layout.Sections.Select(s => s.Source));
        Assert.Equal("both tanks", layout.Rows[2].Notes);
        Assert.Equal("x", layout.Rows[3].Notes);
        Assert.True(layout.Rows[4].Time!.Approx);
        Assert.Empty(layout.Diagnostics);
    }

    [Fact]
    public void Detect_MergedAssignmentAcrossColumns_AppliesToEachColumn()
    {
        var layout = Detector.Detect(Synthetic(
            "P1",
            [new CellRange(1, 1, 1, 2)],
            [],
            ["P1", "MT", "OT", "H1"],
            ["Pull", "Party Mit", "", "Kera"]));

        var row = layout.Rows[1];
        Assert.Equal(["MT", "OT", "H1"], row.Assignments.Select(a => a.Assignee.Key));
        Assert.Equal([true, true, false], row.Assignments.Select(a => a.Spread));
        Assert.Equal(["'P1'!B2", "'P1'!B2", "'P1'!D2"], row.Assignments.Select(a => a.Src));
    }

    [Fact]
    public void Detect_AssignmentMergedDownwards_IsImportedOnceAndReported()
    {
        var layout = Detector.Detect(Synthetic(
            "P1",
            [new CellRange(1, 1, 2, 1)],
            [],
            ["P1", "MT", "OT"],
            ["Stack 1", "Rampart", "Rep"],
            ["Stack 2", "", "Rep"]));

        Assert.Equal(["MT", "OT"], layout.Rows[1].Assignments.Select(a => a.Assignee.Key));
        Assert.Equal(["OT"], layout.Rows[2].Assignments.Select(a => a.Assignee.Key));
        var d = Assert.Single(layout.Diagnostics);
        Assert.Equal(("merged-rows", "'P1'!B2"), (d.Code, d.Src));
    }

    [Fact]
    public void Detect_ReportsUnknownAndDuplicateColumns()
    {
        var layout = Detector.Detect(Synthetic(
            "P3",
            ["P3", "MT", "MT", "Banana", "OT"],
            ["Hello World", "Rep", "", "x", ""]));

        Assert.Equal("MAAUA", Roles(layout));
        Assert.Equal(["unknown-column", "duplicate-assignee"], layout.Diagnostics.Select(d => d.Code));
        Assert.Equal(["'P3'!D1", "'P3'!C1"], layout.Diagnostics.Select(d => d.Src));
    }

    [Fact]
    public void Detect_RepeatedHeaderRow_SwitchesThePhase()
    {
        var layout = Detector.Detect(Synthetic(
            "Tanks",
            ["P1", "MT", "OT"],
            ["Pull", "Rampart", ""],
            ["P2", "MT", "OT"],
            ["Solar Ray", "Holmgang", ""]));

        Assert.Equal("HDHD", Kinds(layout));
        Assert.Equal("1122", Phases(layout));
        Assert.Equal([PhaseSource.HeaderCell, PhaseSource.HeaderCell], layout.Sections.Select(s => s.Source));
    }

    [Fact]
    public void Detect_HiddenRowsAreSkippedAndReported()
    {
        var layout = Detector.Detect(Synthetic(
            "P1",
            [],
            [1],
            ["P1", "MT", "OT"],
            ["Old row", "Rampart", ""],
            ["Pull", "Rep", ""]));

        Assert.Equal("HhD", Kinds(layout));
        var d = Assert.Single(layout.Diagnostics);
        Assert.Equal(("hidden-content", DiagnosticSeverity.Info), (d.Code, d.Severity));
    }

    [Fact]
    public void Detect_PhaseConsistencyDiagnostics()
    {
        var layout = Detector.Detect(Synthetic(
            "P1/P2",
            ["P1", "MT", "OT"],
            ["Pull", "Rampart", ""],
            ["P4", "", ""],
            ["Proteans", "Rep", ""],
            ["P3", "", ""],
            ["Hello World", "Rep", ""]));

        Assert.Equal("HDLDLD", Kinds(layout));
        Assert.Equal("114433", Phases(layout));
        Assert.Equal(["phase-not-in-tab-name", "phase-not-in-tab-name", "phase-order", "phase-not-found"], layout.Diagnostics.Select(d => d.Code));
        Assert.Contains("P2", layout.Diagnostics[3].Message, StringComparison.Ordinal); // the tab names P2, but no row starts it
    }

    [Fact]
    public void Detect_NoPhaseAndUnparsableTime_AreReported()
    {
        var layout = Detector.Detect(Synthetic(
            "Healers",
            ["Time", "Mechanic", "H1", "H2"],
            ["0:10", "Pull", "Kera", ""],
            ["soon", "Stack", "", "Soil"]));

        Assert.Equal("HDD", Kinds(layout));
        Assert.Equal("---", Phases(layout));
        Assert.Equal(["no-phase", "unparsed-time"], layout.Diagnostics.Select(d => d.Code));
        Assert.Null(layout.Rows[2].Time);
        Assert.Equal("soon", layout.Rows[2].TimeText);
    }

    [Fact]
    public void Detect_PhaseTitleAboveTheHeader_StartsThePhase()
    {
        var layout = Detector.Detect(Synthetic(
            "Healers",
            ["Phase 2", "", ""],
            ["", "", ""],
            ["Mechanic", "H1", "H2"],
            ["Solar Ray", "Kera", "Soil"]));

        Assert.Equal(2, layout.HeaderRow);
        Assert.Equal("--22", Phases(layout));
        Assert.Equal(PhaseSource.LabelRow, layout.Sections.Single().Source);
        Assert.Equal("Phase 2", layout.Title);
    }

    [Fact]
    public void Detect_TextOnlyRows_NotesByWordingAndFootnotes()
    {
        var layout = Detector.Detect(Synthetic(
            "P5",
            ["P5", "MT", "OT", "H1"],
            ["Delta", "Rep", "", ""],
            ["", "Rampart as P5 starts", "", ""],
            ["", "", "", "*Kera only if the AST is not here"],
            ["", "Every damage instance has a GCD shield, usually", "", ""]));

        Assert.Equal("HDDFN", Kinds(layout));
        Assert.Equal(["'P5'!D4"], layout.Footnotes.Select(f => f.Src));
    }

    [Fact]
    public void Detect_EmptyAndRaggedTabs()
    {
        var empty = Detector.Detect(new SheetTab { Name = "Empty", Rows = [] });
        Assert.Equal(TabKind.NotAPlan, empty.Kind);
        Assert.Empty(empty.Rows);

        var ragged = Detector.Detect(Synthetic(
            "P2",
            ["P2", "MT"],
            ["Solar Ray", "Holmgang", "extra"],
            [],
            ["Limitless"]));
        Assert.Equal("MAE", Roles(ragged).Replace('U', 'E'));
        Assert.Equal("HDED", Kinds(ragged));
        Assert.Equal("Holmgang", ragged.Rows[1].Assignments.Single().Cell.Text);
        Assert.Equal(["Holmgang", "extra"], ragged.Rows[1].Texts.Skip(1).Select(t => t.Text));
    }

    [Fact]
    public void Detect_LpduDataRows_AreTheLabelledMechanicRowsPlusOneLabelLessRow()
    {
        var rows = Detector.Detect(Fixtures.Lpdu).PlanTabs.SelectMany(t => t.DataRows).ToList();

        Assert.Equal(99, rows.Count);
        Assert.Equal(98, rows.Count(r => r.Label is not null));
    }

    [Fact]
    public void DocumentAssignees_AreDistinctAcrossTabs()
    {
        var keys = Detector.Detect(Fixtures.Lpdu).Assignees.Select(a => a.Key).ToList();

        Assert.Equal(
            ["MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2", "H1 WHM", "H1 AST", "H2 SCH", "H2 SGE", "CM LB Tank", "CM Rep Tank", "WAR", "DRK", "GNB", "PLD"],
            keys);
        Assert.Equal(["White Mage", "Scholar"], Detector.Detect(Fixtures.CarryTheClear).Assignees.Select(a => a.Key));
    }

    [Fact]
    public void Detect_TabWithoutPlayerColumns_IsNotAPlan()
    {
        var layout = Detector.Detect(Synthetic("Info", ["Mechanic", "Notes"], ["Pull", "Hi"]));

        Assert.Equal(TabKind.NotAPlan, layout.Kind);
        Assert.Equal(-1, layout.HeaderRow);
    }

    [Fact]
    public void Detect_LpduR1Column_HasNoRole()
    {
        // R1 is the physical ranged column in LPDU ("Party Mit if MCH", Dismantle), not a caster column.
        var layout = Detector.Detect(Fixtures.Tab("lpdu", "P4"));
        var r1 = layout.AssigneeColumns.Single(c => c.Assignee!.Key == "R1");

        Assert.Contains(layout.DataRows.SelectMany(r => r.Assignments), a => a.Column == r1 && a.Cell.Text == "Party Mit if MCH");
        Assert.Equal(Role.None, r1.Assignee!.Role);
    }

    // ---- other people's sheets (review regressions) ------------------------------------------------

    private static string Assigned(RowLayout row) => string.Join(",", row.Assignments.Select(a => a.Assignee.Key + "=" + a.Cell.Text));

    private static SheetTab WithHiddenColumns(SheetTab tab, params int[] cols) =>
        new() { Name = tab.Name, Rows = tab.Rows, Merges = tab.Merges, HiddenRows = tab.HiddenRows, HiddenCols = cols };

    [Fact]
    public void Detect_RepeatedHeaderWithReorderedPlayers_RemapsTheRowsBelowIt()
    {
        var tab = Synthetic(
            "Tanks",
            ["P1", "MT", "OT"],
            ["Pull", "Rampart", "Reprisal"],
            ["", "", ""],
            ["P2", "OT", "MT"],
            ["Solar Ray", "Holmgang", "Reprisal"]);
        var layout = Detector.Detect(tab);

        Assert.Equal("HDEHD", Kinds(layout));
        Assert.Equal("11122", Phases(layout));
        Assert.Equal("MT=Rampart,OT=Reprisal", Assigned(layout.Rows[1]));
        Assert.Equal("OT=Holmgang,MT=Reprisal", Assigned(layout.Rows[4]));
        Assert.Equal(["'Tanks'!B5", "'Tanks'!C5"], layout.Rows[4].Assignments.Select(a => a.Src));
        Assert.Equal(["OT", "MT"], layout.RepeatedHeaderColumns.Select(c => c.Assignee!.Key));
        Assert.Equal(["MT", "OT", "OT", "MT"], layout.AssigneeColumns.Select(c => c.Assignee!.Key));
        var d = Assert.Single(layout.Diagnostics);
        Assert.Equal(("header-changed", DiagnosticSeverity.Info, "'Tanks'!B4"), (d.Code, d.Severity, d.Src));
        Assert.Equal(["MT", "OT"], Detector.Detect(new SheetDocument { Title = "t", Tabs = [tab] }).Assignees.Select(a => a.Key));
    }

    [Fact]
    public void Detect_StackedTableForOtherPlayers_UsesItsOwnHeader()
    {
        var layout = Detector.Detect(Synthetic(
            "P1",
            ["Mechanic", "MT", "OT", "H1", "H2"],
            ["Pull", "Rampart", "Reprisal", "", ""],
            ["", "", "", "", ""],
            ["Mechanic", "H1", "H2", "", ""],
            ["Pantokrator", "Kera", "Soil", "", ""],
            ["Program Loop", "", "Soil", "notes?", ""]));

        Assert.Equal("HDEHDD", Kinds(layout));
        Assert.Equal("H1=Kera,H2=Soil", Assigned(layout.Rows[4]));
        Assert.Equal("H2=Soil", Assigned(layout.Rows[5]));
        Assert.Equal(["H1", "H2"], layout.Rows[4].Assignments.Select(a => a.Assignee.Slot));

        // Column D lost its player in the second header: its text is reported, not given to H1.
        Assert.Equal(["header-changed", "unlabelled-column"], layout.Diagnostics.Select(d => d.Code));
        Assert.Equal("'P1'!D6", layout.Diagnostics[1].Src);
    }

    [Fact]
    public void Detect_RepeatedHeaderTitledWithAPhase_StartsThePhase()
    {
        var layout = Detector.Detect(Synthetic(
            "Healers",
            ["P1 Healers", "H1", "H2"],
            ["Pull", "Kera", ""],
            ["P2 Healers", "H2", "H1"],
            ["Solar Ray", "Soil", "Kera"]));

        Assert.Equal("MAA", Roles(layout));
        Assert.Equal("HDHD", Kinds(layout));
        Assert.Equal("1122", Phases(layout));
        Assert.Equal([(1, "'Healers'!A1"), (2, "'Healers'!A3")], layout.Sections.Select(x => (x.Number, x.Src)));
        Assert.Equal("H1=Kera", Assigned(layout.Rows[1]));
        Assert.Equal("H2=Soil,H1=Kera", Assigned(layout.Rows[3]));
        Assert.Equal(["guessed-phase", "guessed-phase", "header-changed"], layout.Diagnostics.Select(d => d.Code));

        // Column keywords still win over a phase number in front of them.
        Assert.Equal("MAN", Roles(Detector.Detect(Synthetic("Healers", ["P1 Mechanics", "H1", "P3 Notes"], ["Pull", "Kera", "swap"]))));
    }

    [Fact]
    public void Detect_RowWhoseCellsNameSlots_StaysAMechanicRow()
    {
        var layout = Detector.Detect(Synthetic(
            "P1",
            ["P1", "MT", "OT", "H1", "H2"],
            ["Pull", "Rampart", "", "", ""],
            ["Tether partner", "OT", "MT", "H2", "H1"],
            ["Pantokrator", "", "Rep", "Kera", "Soil"]));

        Assert.Equal("HDDD", Kinds(layout));
        Assert.Equal("Tether partner", layout.Rows[2].Label);
        Assert.Equal("MT=OT,OT=MT,H1=H2,H2=H1", Assigned(layout.Rows[2]));
        Assert.Equal("OT=Rep,H1=Kera,H2=Soil", Assigned(layout.Rows[3])); // the columns did not change
        var d = Assert.Single(layout.Diagnostics);
        Assert.Equal(("header-like-row", DiagnosticSeverity.Info, "'P1'!A3"), (d.Code, d.Severity, d.Src));
    }

    [Fact]
    public void Detect_PhaseWrittenAboveTheTimeColumn_DoesNotMakeTheTimesLabels()
    {
        var layout = Detector.Detect(Synthetic(
            "Plan",
            ["P1", "Mechanic", "MT", "OT"],
            ["0:10", "Program Loop", "Rampart", ""],
            ["1:21", "Pantokrator", "", "Rep"]));

        Assert.Equal("TMAA", Roles(layout));
        Assert.Equal(["Program Loop", "Pantokrator"], layout.DataRows.Select(r => r.Label));
        Assert.Equal([10.0, 81], layout.DataRows.Select(r => r.Time!.Seconds));
        Assert.Equal("111", Phases(layout));
        var section = Assert.Single(layout.Sections);
        Assert.Equal((1, PhaseSource.HeaderCell, "'Plan'!A1"), (section.Number, section.Source, section.Src));
        Assert.Empty(layout.Diagnostics);
    }

    [Fact]
    public void Detect_TwoLabelColumns_UsesTheFirstAndSaysSo()
    {
        // Two tables side by side: only the first label column is read.
        var layout = Detector.Detect(Synthetic(
            "P1",
            ["P1", "MT", "OT", "", "P2", "MT", "OT"],
            ["Pull", "Rampart", "", "", "Solar Ray", "Holmgang", ""]));

        Assert.Equal("MAAEMAA", Roles(layout));
        Assert.Equal(["multiple-label-columns", "duplicate-assignee", "duplicate-assignee"], layout.Diagnostics.Select(d => d.Code));
        Assert.Equal(("'P1'!E1", DiagnosticSeverity.Warning), (layout.Diagnostics[0].Src, layout.Diagnostics[0].Severity));
        Assert.Contains("only column A is used", layout.Diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Detect_PhaseColumnWithBareNumbers_SwitchesThePhase()
    {
        var named = Detector.Detect(Synthetic(
            "P1/P2",
            ["Phase", "Mechanic", "MT"],
            ["1", "Pull", "Rampart"],
            ["2", "Solar Ray", "Holmgang"]));
        Assert.Equal("PMA", Roles(named));
        Assert.Equal("112", Phases(named));
        Assert.Equal([PhaseSource.TabName, PhaseSource.PhaseColumn], named.Sections.Select(s => s.Source));
        Assert.Empty(named.Diagnostics);

        var unnamed = Detector.Detect(Synthetic(
            "Healers",
            ["Phase", "Mechanic", "H1"],
            ["1", "Pull", "Kera"],
            ["2", "Solar Ray", "Soil"]));
        Assert.Equal("-12", Phases(unnamed));
        Assert.Empty(unnamed.Diagnostics);
    }

    [Fact]
    public void Detect_PhaseLabelWithANameButNoSeparator_StartsThePhaseWhenItMovesForward()
    {
        var layout = Detector.Detect(Synthetic(
            "P4/P5",
            ["P4", "MT", "OT"],
            ["Blue Screen", "Rep", ""],
            ["P5 Delta", "", ""],
            ["Run: Dynamis", "", "Rep"]));

        Assert.Equal("HDSD", Kinds(layout));
        Assert.Equal("4455", Phases(layout));
        Assert.Equal((5, "Delta", PhaseSource.SectionRow), (layout.Sections[1].Number, layout.Sections[1].Name, layout.Sections[1].Source));
        var d = Assert.Single(layout.Diagnostics);
        Assert.Equal(("guessed-phase", DiagnosticSeverity.Info, "'P4/P5'!A3"), (d.Code, d.Severity, d.Src));
    }

    [Fact]
    public void Detect_PhaseNumberInAMechanicLabel_IsNotASectionWhenTheTabDoesNotHaveThatPhase()
    {
        var layout = Detector.Detect(Synthetic("P3", ["P3", "MT"], ["Hello World", "Rep"], ["P4 skip", ""], ["Looper", "Rampart"]));

        Assert.Equal("HDDD", Kinds(layout));
        Assert.Equal("3333", Phases(layout));
        Assert.Equal("P4 skip", layout.Rows[2].Label);
        Assert.Empty(layout.Diagnostics);
    }

    [Fact]
    public void Detect_PhaseMarkerInFrontOfAMechanicLabel_StartsThePhaseAtThatRow()
    {
        var layout = Detector.Detect(Synthetic(
            "P1/P2",
            ["P1", "MT", "OT"],
            ["Pull", "Rampart", ""],
            ["P2 - Solar Ray (both tanks)", "Holmgang", ""],
            ["Cosmo Memory", "", "Rep"]));

        Assert.Equal("HDDD", Kinds(layout));
        Assert.Equal("1122", Phases(layout));
        Assert.Equal(["Pull", "Solar Ray (both tanks)", "Cosmo Memory"], layout.DataRows.Select(r => r.Label));
        Assert.Equal("P2 - Solar Ray (both tanks)", layout.Rows[2].Texts[0].Text);
        var section = layout.Sections[1];
        Assert.Equal((2, PhaseSource.MechanicLabel, 2, "'P1/P2'!A3", "both tanks"), (section.Number, section.Source, section.Row, section.Src, section.ZeroHint));
        Assert.Empty(layout.Diagnostics);
    }

    [Fact]
    public void Detect_PhaseTheTabNameListsButNoRowStarts_IsReported()
    {
        var layout = Detector.Detect(Synthetic("P4/P5", ["P4", "MT"], ["Blue Screen", "Rep"], ["Run: Dynamis", "Rep"]));

        Assert.Equal("444", Phases(layout));
        var d = Assert.Single(layout.Diagnostics);
        Assert.Equal(("phase-not-found", DiagnosticSeverity.Warning), (d.Code, d.Severity));
        Assert.Contains("P5", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Detect_NumberSignPlayerHeaders_AreAssigneeColumns()
    {
        var layout = Detector.Detect(Synthetic(
            "P1",
            ["Mechanic", "MT", "OT", "Healer #1", "Healer #2"],
            ["Pull", "Rampart", "", "Kera", "Soil"]));

        Assert.Equal("MAAAA", Roles(layout));
        Assert.Equal("MT=Rampart,Healer #1=Kera,Healer #2=Soil", Assigned(layout.Rows[1]));
        Assert.Equal(["H1", "H2"], layout.Columns.Skip(3).Select(c => c.Assignee!.Slot));
        Assert.Empty(layout.Diagnostics);
    }

    [Fact]
    public void Detect_StatisticsColumn_IsIgnoredAndReported()
    {
        var layout = Detector.Detect(Synthetic("P1", ["Mechanic", "MT", "# of clears"], ["Pull", "Rampart", "12"]));

        Assert.Equal("MAI", Roles(layout));
        var d = Assert.Single(layout.Diagnostics);
        Assert.Equal(("ignored-column", DiagnosticSeverity.Info, "'P1'!C1"), (d.Code, d.Severity, d.Src));
    }

    [Fact]
    public void Detect_HiddenPlayerColumn_IsReported()
    {
        var layout = Detector.Detect(WithHiddenColumns(Synthetic("P1", ["P1", "MT", "OT", "H1"], ["Pull", "Rampart", "Rep", "Kera"]), 3));

        Assert.Equal("MAAI", Roles(layout));
        Assert.Equal("MT=Rampart,OT=Rep", Assigned(layout.Rows[1]));
        var d = Assert.Single(layout.Diagnostics);
        Assert.Equal(("hidden-content", DiagnosticSeverity.Info, "'P1'!D1"), (d.Code, d.Severity, d.Src));
        Assert.Contains("'H1'", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Detect_LabelLessRowsThatReadLikeNotes_AreReported()
    {
        var layout = Detector.Detect(Synthetic(
            "P1",
            ["P1", "MT", "OT", "H1"],
            ["Pantokrator", "Rampart", "Rep", "Kera"],
            ["", "Reprisal.", "", ""],
            ["", "", "", "Soil after the second hit then Succor"]));

        Assert.Equal("HDNN", Kinds(layout));
        Assert.Equal([("note-in-table", "'P1'!B3"), ("note-in-table", "'P1'!D4")], layout.Diagnostics.Select(d => (d.Code, d.Src)));
        Assert.StartsWith("Row 3 ", layout.Diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Detect_KeywordHeadersWithATrailingColon()
    {
        var layout = Detector.Detect(Synthetic("P1", ["Time:", "Mechanic:", "MT", "Notes:"], ["0:10", "Pull", "Rampart", "swap after"]));

        Assert.Equal("TMAN", Roles(layout));
        var row = Assert.Single(layout.DataRows);
        Assert.Equal(("Pull", 10.0, "swap after"), (row.Label, row.Time!.Seconds, row.Notes));
        Assert.Empty(layout.Diagnostics);
    }

    [Fact]
    public void Detect_TimeColumnOfTypedMinutesShownAsHms_IsReadAsMinutesAndSeconds()
    {
        // Sheets shows "1:21" typed into a time-formatted cell as "1:21:00" (1 h 21 min).
        var layout = Detector.Detect(Synthetic(
            "P1",
            ["Time", "Mechanic", "MT"],
            ["0:10:00", "Pull", "Rampart"],
            ["1:21:00 / 1:27:00", "Pantokrator", "Rep"]));

        Assert.Equal([10.0], layout.Rows[1].Time!.All);
        Assert.Equal([81.0, 87], layout.Rows[2].Time!.All);
        Assert.Equal("1:21:00 / 1:27:00", layout.Rows[2].TimeText);
        var d = Assert.Single(layout.Diagnostics);
        Assert.Equal(("time-as-hms", DiagnosticSeverity.Info, "'P1'!A1"), (d.Code, d.Severity, d.Src));
    }

    [Fact]
    public void Detect_RowTimeOfAnHourOrMore_IsReported()
    {
        var layout = Detector.Detect(Synthetic("P1", ["Time", "Mechanic", "MT"], ["1:21:05", "Pantokrator", "Rep"]));

        Assert.Equal(4865, layout.Rows[1].Time!.Seconds); // real seconds: not typed minutes
        var d = Assert.Single(layout.Diagnostics);
        Assert.Equal(("implausible-time", DiagnosticSeverity.Warning, "'P1'!A2"), (d.Code, d.Severity, d.Src));
    }

    [Fact]
    public void Detect_HiddenTab_IsLeftOutOfPlanTabsAndReported()
    {
        var old = Synthetic("P3 old", ["P3", "OT"], ["Hello World", "Holmgang"]);
        var layout = Detector.Detect(new SheetDocument
        {
            Title = "t",
            Tabs = [Synthetic("P3", ["P3", "MT"], ["Hello World", "Rep"]), new SheetTab { Name = old.Name, Rows = old.Rows, Hidden = true }],
        });

        Assert.Equal(["P3"], layout.PlanTabs.Select(t => t.Name));
        Assert.Equal(["MT"], layout.Assignees.Select(a => a.Key));
        var hidden = layout.Tab("P3 old")!;
        Assert.True(hidden.Hidden);
        Assert.Equal(TabKind.Plan, hidden.Kind); // still there when the user picks it by name
        var d = Assert.Single(hidden.Diagnostics);
        Assert.Equal(("hidden-tab", DiagnosticSeverity.Info), (d.Code, d.Severity));
    }

    // ---- helpers --------------------------------------------------------------------------------

    [Theory]
    [InlineData("P3", "3")]
    [InlineData("P1/P2", "1,2")]
    [InlineData("P1P2", "1,2")]
    [InlineData("Phase 4", "4")]
    [InlineData("P4-P6", "4,5,6")]
    [InlineData("P5 & P6", "5,6")]
    [InlineData("p2", "2")]
    [InlineData("General Tank Mit til P5", "")]
    [InlineData("TOP – White Mage", "")]
    [InlineData("WAR/DRK", "")]
    [InlineData("ALOO", "")]
    [InlineData("", "")]
    public void PhasesFromTabName_ReadsPhaseListNames(string name, string expected) =>
        Assert.Equal(expected, string.Join(",", LayoutDetector.PhasesFromTabName(name)));

    [Theory]
    [InlineData("WAR/DRK", "WAR,DRK")]
    [InlineData("WARDRK", "WAR,DRK")] // Google's xlsx export drops the '/' of "WAR/DRK"
    [InlineData("GNBPLD", "GNB,PLD")]
    [InlineData("TOPWHM", "")]
    [InlineData("TOP – White Mage", "WHM")]
    [InlineData("TOP – WHM + SCH", "WHM,SCH")]
    [InlineData("TOP – Scholar", "SCH")]
    [InlineData("General Tank Mit til P5", "")]
    [InlineData("P1/P2", "")]
    [InlineData("war notes", "")]
    public void JobsFromTabName_ReadsJobNames(string name, string expected) =>
        Assert.Equal(expected, string.Join(",", LayoutDetector.JobsFromTabName(name)));

    [Theory]
    [InlineData("P2 — Omega-M/F  (from phase start)", 2, "Omega-M/F", "from phase start")]
    [InlineData("P5 — Run: Dynamis  (from boss targetable; always 4:46 long)", 5, "Run: Dynamis", "from boss targetable; always 4:46 long")]
    [InlineData("P1 — Omega  (timer from pull)", 1, "Omega", "timer from pull")]
    [InlineData("Phase 3: Omega Reconfigured", 3, "Omega Reconfigured", null)]
    [InlineData("P4 (from phase start)", 4, null, "from phase start")]
    [InlineData("P2", 2, null, null)]
    [InlineData("P1 ", 1, null, null)]
    [InlineData("Phase 6", 6, null, null)]
    [InlineData("Phase Two", 2, null, null)]
    [InlineData("Phase three: Omega Reconfigured", 3, "Omega Reconfigured", null)]
    public void TryParseSection_ReadsSectionRows(string text, int number, string? name, string? hint)
    {
        Assert.True(LayoutDetector.TryParseSection(text, out var n, out var nm, out var h));
        Assert.Equal((number, name, hint), (n, nm, h));
        Assert.Equal(name is null && hint is null, LayoutDetector.TryParsePhaseLabel(text, out _));
    }

    [Theory]
    [InlineData("Pantokrator")]
    [InlineData("Protean 1")]
    [InlineData("P4 skip")]
    [InlineData("P5 Delta")] // no separator: only trusted on a row that moves the phase forward (see the synthetic tests)
    [InlineData("Phase Twofold")]
    [InlineData("P0")]
    [InlineData("Atomic Ray (P1 enrage — push before)")]
    [InlineData("")]
    public void TryParseSection_RejectsLabels(string text) => Assert.False(LayoutDetector.TryParseSection(text, out _, out _, out _));

    [Fact]
    public void CountStars_CountsLethalityMarks()
    {
        Assert.Equal(4, LayoutDetector.CountStars("Cosmo Memory (405k raidwide) ★★★★"));
        Assert.Equal(0, LayoutDetector.CountStars("Run: ****mi* (Delta)"));
        Assert.Equal(0, LayoutDetector.CountStars(null));
    }

    private static FightDefinition Fight(params string[] phaseIds) => new()
    {
        Key = "test",
        Name = "Test Fight",
        Territory = 1,
        SyncLevel = 90,
        TimelineFile = "test.txt",
        Phases = phaseIds.Select((id, i) => new Phase { Id = id, Name = id, Start = i * 100 }).ToList(),
    };
}
