using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using MitCue.Core.Import;
using D = DocumentFormat.OpenXml.Drawing;
using X = DocumentFormat.OpenXml.Spreadsheet;

namespace MitCue.Core.Tests.Import;

public class SheetReaderTests
{
    // ---- JSON fixtures -------------------------------------------------------------------------

    [Fact]
    public void FromFixtureJson_ReadsTabCellsRunsMergesAndHiddenRows()
    {
        var tab = SheetReaders.FromFixtureJson(File.ReadAllText(Path.Combine(Fixtures.Dir("carry-the-clear"), "top-white-mage.json")));

        Assert.Equal("TOP – White Mage", tab.Name);
        Assert.Equal(777100, tab.Gid);
        Assert.Equal(0, tab.Index);
        Assert.Equal(68, tab.Rows.Count);
        Assert.Equal(5, tab.ColumnCount);
        Assert.Equal([0, 1, 2, 3, 4], tab.HiddenRows);
        Assert.Empty(tab.HiddenCols);
        Assert.Contains(new CellRange(14, 2, 15, 2), tab.Merges);
        Assert.Equal(8, tab.Merges.Count);

        var c18 = tab.Cell(17, 2);
        Assert.Equal("Temp (1:18) / Bell (right after the 1st stack, ~1:22) / Plenary* (1:31)", c18.Text);
        Assert.Equal("#ebd2d3", c18.Bg);
        Assert.Null(c18.Fg);
        Assert.Equal([new TextRun(0, null, false), new TextRun(56, "#1154cc", true)], c18.Runs);

        var a18 = tab.Cell(17, 0);
        Assert.Equal("1:21 / 1:27 / 1:33 / 1:39", a18.Text);
        Assert.Equal("#545454", a18.Fg);
        Assert.True(tab.Cell(0, 0).IsEmpty);
        Assert.Same(SheetCell.Empty, tab.Cell(500, 0));
        Assert.Equal(new CellRange(14, 2, 15, 2), tab.MergeAt(15, 2));
        Assert.Null(tab.MergeAt(17, 2));
        Assert.True(tab.IsRowHidden(3));
    }

    [Fact]
    public void FromFixtureJson_RejectsNonFixtures()
    {
        Assert.Throws<FormatException>(() => SheetReaders.FromFixtureJson("[]"));
        Assert.Throws<FormatException>(() => SheetReaders.FromFixtureJson("{\"tab\":\"x\"}"));
        Assert.Throws<FormatException>(() => SheetReaders.FromFixtureJson("{ not json"));
    }

    [Fact]
    public void FromFixtureJson_ToleratesMissingFields()
    {
        var tab = SheetReaders.FromFixtureJson("""{"rows":[[{"v":"a"},null,{"v":"b","bold":true,"runs":[{"start":1,"fg":"#ff0000"}]}]]}""");

        Assert.Equal("Sheet1", tab.Name);
        Assert.Null(tab.Gid);
        Assert.Equal("a", tab.Cell(0, 0).Text);
        Assert.True(tab.Cell(0, 1).IsEmpty);
        Assert.True(tab.Cell(0, 2).Bold);
        Assert.Equal([new TextRun(1, "#ff0000", false)], tab.Cell(0, 2).Runs);
    }

    [Fact]
    public void FromFixtureDirectory_OrdersTabsByIndex()
    {
        Assert.Equal(
            ["ALOO", "P1/P2", "P3", "P4", "P5", "P6", "General Tank Mit til P5", "WAR/DRK", "WAR/GNB", "WAR/PLD", "DRK/GNB", "DRK/PLD", "GNB/PLD"],
            Fixtures.Lpdu.Tabs.Select(t => t.Name));
        Assert.Equal(["TOP – White Mage", "TOP – Scholar", "TOP – WHM + SCH"], Fixtures.CarryTheClear.Tabs.Select(t => t.Name));
        Assert.Equal("LPDU TOP Mitigation Sheet", Fixtures.Lpdu.Title);
        Assert.NotNull(Fixtures.Lpdu.Tab("p1/p2"));
    }

    [Fact]
    public void FromFixtureDirectory_WithoutIndexOrdersByFileName()
    {
        var dir = Directory.CreateTempSubdirectory("mitcue-fixtures-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "b.json"), """{"tab":"B","rows":[]}""");
            File.WriteAllText(Path.Combine(dir.FullName, "a.json"), """{"tab":"A","rows":[]}""");
            File.WriteAllText(Path.Combine(dir.FullName, "c.json"), """{"tab":"C","index":0,"rows":[]}""");
            File.WriteAllText(Path.Combine(dir.FullName, "ignored.txt"), "x");

            var doc = SheetReaders.FromFixtureDirectory(dir.FullName, "T");

            Assert.Equal(["C", "A", "B"], doc.Tabs.Select(t => t.Name));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    // ---- xlsx (Google export) vs JSON fixtures --------------------------------------------------

    public static TheoryData<string> SheetKeys => ["lpdu", "carry-the-clear"];

    [Theory]
    [MemberData(nameof(SheetKeys))]
    public void FromXlsx_MatchesTheJsonFixtures_CellByCell(string key)
    {
        var json = Fixtures.Doc(key);
        SheetDocument xlsx;
        using (var stream = File.OpenRead(Fixtures.Xlsx(key)))
            xlsx = SheetReaders.FromXlsx(stream, json.Title);

        Assert.Equal(json.Tabs.Count, xlsx.Tabs.Count);
        var mismatches = new List<string>();
        var cells = 0;
        for (var t = 0; t < json.Tabs.Count; t++)
        {
            var expected = json.Tabs[t];
            var actual = xlsx.Tabs[t];
            Assert.Equal(t, actual.Index);
            Assert.Equal(expected.Name.Replace("/", string.Empty, StringComparison.Ordinal), actual.Name); // Excel forbids '/' in sheet names
            Assert.Equal(expected.Rows.Count, actual.Rows.Count);
            Assert.Equal(expected.ColumnCount, actual.ColumnCount);
            Assert.Equal(expected.Merges, actual.Merges);
            Assert.Equal(expected.HiddenRows, actual.HiddenRows);
            Assert.Equal(expected.HiddenCols, actual.HiddenCols);
            for (var r = 0; r < expected.Rows.Count; r++)
            {
                for (var c = 0; c < expected.ColumnCount; c++)
                {
                    cells++;
                    var e = expected.Cell(r, c);
                    var a = actual.Cell(r, c);
                    var where = expected.Ref(r, c);
                    if (e.Text != a.Text)
                        mismatches.Add($"{where} text '{e.Text}' vs '{a.Text}'");
                    if (e.Bg != a.Bg || e.Fg != a.Fg || e.Bold != a.Bold || e.Strike != a.Strike)
                        mismatches.Add($"{where} format {e.Bg}/{e.Fg}/{e.Bold}/{e.Strike} vs {a.Bg}/{a.Fg}/{a.Bold}/{a.Strike}");
                    if (!e.Runs.SequenceEqual(a.Runs))
                        mismatches.Add($"{where} runs [{string.Join(",", e.Runs)}] vs [{string.Join(",", a.Runs)}]");
                    if (e.Note != a.Note)
                        mismatches.Add($"{where} note '{e.Note}' vs '{a.Note}'");
                }
            }
        }

        Assert.True(mismatches.Count == 0, $"{mismatches.Count} mismatches in {cells} cells:\n" + string.Join("\n", mismatches.Take(30)));
        Assert.True(cells > 1000);
    }

    [Fact]
    public void FromXlsx_KeepsCarryTheClearColours()
    {
        using var stream = File.OpenRead(Fixtures.Xlsx("carry-the-clear"));
        var tab = SheetReaders.FromXlsx(stream, "Carry the Clear").Tabs[1];

        // Grey runs = optional, blue bold = the author's placement, column B colour from conditional formatting.
        Assert.Equal(
            [new TextRun(0, "#808080", false), new TextRun(19, null, false), new TextRun(38, "#808080", false), new TextRun(51, null, false)],
            tab.Cell(51, 2).Runs);
        Assert.Equal("#d92f24", tab.Cell(17, 1).Fg); // ★★★
        Assert.Equal("#d100d1", tab.Cell(56, 1).Fg); // ★★★★
        Assert.Equal("#4974b9", tab.Cell(19, 1).Fg); // REGEXMATCH "enrage|dodge"
        Assert.Equal("#178038", tab.Cell(22, 1).Fg); // non-lethal
        Assert.Equal("#c3d7cd", tab.Cell(17, 2).Bg);
    }

    [Fact]
    public void FromXlsx_FormatsNumbersAndUsesThemeColours()
    {
        using var stream = File.OpenRead(Fixtures.Xlsx("lpdu"));
        var doc = SheetReaders.FromXlsx(stream, "LPDU");

        Assert.Equal("30%", doc.Tabs[6].Cell(8, 1).Text); // 'General Tank Mit til P5'!B9 = 0.3
        Assert.Equal("#ffffff", doc.Tabs[0].Cell(10, 3).Fg); // ALOO D11, white on black
    }

    [Fact]
    public void FromXlsx_RestoresTabNamesAndSingleTabGid()
    {
        var names = Fixtures.Lpdu.Tabs.Select(t => t.Name).ToList();
        using (var stream = File.OpenRead(Fixtures.Xlsx("lpdu")))
        {
            var doc = SheetReaders.FromXlsx(stream, "LPDU", new XlsxReadOptions { TabNames = names, Gid = 42 });
            Assert.Equal(names, doc.Tabs.Select(t => t.Name));
            Assert.All(doc.Tabs, t => Assert.Null(t.Gid)); // several tabs: the gid is not applied
        }

        using var single = BuildWorkbook();
        var one = SheetReaders.FromXlsx(single, "One", new XlsxReadOptions { Gid = 777200, TabNames = ["P1/P2"] });
        Assert.Equal(777200, one.Tabs[0].Gid);
        Assert.Equal("P1/P2", one.Tabs[0].Name);
    }

    [Fact]
    public void FromXlsx_ReadsNonSeekableStreams()
    {
        var bytes = File.ReadAllBytes(Fixtures.Xlsx("carry-the-clear"));
        using var stream = new NonSeekableStream(new MemoryStream(bytes));

        var doc = SheetReaders.FromXlsx(stream, "CtC");

        Assert.Equal(3, doc.Tabs.Count);
        Assert.Equal("White Mage", doc.Tabs[0].Cell(14, 2).Text);
    }

    [Fact]
    public void FromXlsx_RejectsWhatIsNotAWorkbook()
    {
        Assert.Throws<InvalidDataException>(() => SheetReaders.FromXlsx(new MemoryStream(Encoding.UTF8.GetBytes("<html>not shared</html>")), "x"));
        Assert.Throws<InvalidDataException>(() => SheetReaders.FromXlsx(new MemoryStream([]), "x"));
    }

    [Fact]
    public void FromXlsx_SyntheticWorkbook_NotesIndexedThemeInlineNumbersStrikeAndConditionalFormats()
    {
        using var stream = BuildWorkbook();
        var tab = SheetReaders.FromXlsx(stream, "Synthetic").Tabs.Single();

        Assert.Equal("Plan", tab.Name);
        Assert.Equal(4, tab.Rows.Count); // trailing empty rows are trimmed
        Assert.Equal(5, tab.ColumnCount);

        // A1: shared string with a note (legacy comment).
        Assert.Equal("Plain", tab.Cell(0, 0).Text);
        Assert.Equal("Check this", tab.Cell(0, 0).Note);

        // B1: rich text with a grey run and a bold struck run.
        var b1 = tab.Cell(0, 1);
        Assert.Equal("Grey struck", b1.Text);
        Assert.Equal([new TextRun(0, "#808080", false), new TextRun(5, null, true) { Strike = true }], b1.Runs);

        // C1: inline string; indexed font colour 10 (red) on a solid fill.
        var c1 = tab.Cell(0, 2);
        Assert.Equal("inline", c1.Text);
        Assert.Equal("#ff0000", c1.Fg);
        Assert.Equal("#ebd2d3", c1.Bg);

        // D1: theme colour 4 (accent 1); theme 1 (dark 1) is black, i.e. the default.
        Assert.Equal("#4f81bd", tab.Cell(0, 3).Fg);
        Assert.Null(tab.Cell(0, 4).Fg);

        // Row 2 is hidden: a percentage, a boolean, a bold struck cell.
        Assert.Equal([1], tab.HiddenRows);
        Assert.Equal("30%", tab.Cell(1, 0).Text);
        Assert.Equal("TRUE", tab.Cell(1, 1).Text);
        Assert.True(tab.Cell(1, 2).Bold);
        Assert.True(tab.Cell(1, 2).Strike);

        // Column E is hidden.
        Assert.Equal([4], tab.HiddenCols);

        // A3: conditional format (contains "★★★") recolours the text; B3 does not match.
        Assert.Equal("#d92f24", tab.Cell(2, 0).Fg);
        Assert.Null(tab.Cell(2, 1).Fg);

        // A4:B4 merged; a note on an otherwise empty cell C4 keeps the row.
        Assert.Equal([new CellRange(3, 0, 3, 1)], tab.Merges);
        Assert.Null(tab.Cell(3, 2).Text);
        Assert.Equal("Empty cell note", tab.Cell(3, 2).Note);
    }

    // ---- CSV exports and TSV clipboard -----------------------------------------------------------

    [Theory]
    [MemberData(nameof(SheetKeys))]
    public void FromDelimited_CsvExportsMatchTheJsonText(string key)
    {
        foreach (var expected in Fixtures.Doc(key).Tabs)
        {
            var csv = File.ReadAllText(Fixtures.Csv(key, Fixtures.FileName(expected.Name)), Encoding.UTF8);
            var actual = SheetReaders.FromDelimited(csv, ',', expected.Name);

            Assert.Equal(expected.Rows.Count, actual.Rows.Count);
            Assert.Equal(expected.ColumnCount, actual.ColumnCount);
            for (var r = 0; r < expected.Rows.Count; r++)
            {
                for (var c = 0; c < expected.ColumnCount; c++)
                    Assert.True(expected.Cell(r, c).Text == actual.Cell(r, c).Text, $"{expected.Ref(r, c)}: '{expected.Cell(r, c).Text}' vs '{actual.Cell(r, c).Text}'");
            }
        }
    }

    /// <summary>Google Sheets' text/plain writer (docs/research/sheets-import.md §3.1): quote only cells with a line break or tab.</summary>
    private static string GoogleClipboard(IEnumerable<IEnumerable<string>> rows) =>
        string.Join("\n", rows.Select(r => string.Join("\t", r.Select(c =>
            c.Contains('\n') || c.Contains('\t') ? "\"" + c.Replace("\"", "\"\"") + "\"" : c))));

    [Fact]
    public void FromDelimited_RoundTripsEveryFixtureTabThroughTheGoogleClipboard()
    {
        var grids = Fixtures.Both.SelectMany(d => d.Doc.Tabs)
            .Select(t => t.Rows.Select(r => r.Select(c => c.Text ?? string.Empty).ToList()).ToList())
            .ToList();
        grids.Add([["\"quoted\" start", "multi\nline \"q\"", "tab\tinside"], ["", "★★ Plenary* (1:31) ⏱", "a\"\"b"], ["\"Kera\" on MT", "\"x\"", "end"]]);

        foreach (var grid in grids)
        {
            // Dalamud's ImGui.GetClipboardText() turns every line ending into CRLF, also inside quoted cells.
            var clip = GoogleClipboard(grid).ReplaceLineEndings("\r\n");
            var tab = SheetReaders.FromDelimited(clip);

            Assert.Equal(grid.Count, tab.Rows.Count);
            for (var r = 0; r < grid.Count; r++)
            {
                for (var c = 0; c < grid[r].Count; c++)
                    Assert.Equal(grid[r][c].Length == 0 ? null : grid[r][c], tab.Cell(r, c).Text);
            }
        }
    }

    [Theory]
    [InlineData("a\tb\r\nc\td", """[["a","b"],["c","d"]]""")]
    [InlineData("x\t\"line1\r\nline2\"\r\ny\tz", """[["x","line1\nline2"],["y","z"]]""")]
    [InlineData("say \"hi\"\t\"Kera\" on MT", """[["say \"hi\"","\"Kera\" on MT"]]""")] // quotes without a line break or tab stay raw
    [InlineData("\"Kera\"\tb", """[["\"Kera\"","b"]]""")]
    [InlineData("\"a \"\"b\"\"\r\nc\"\tz", """[["a \"b\"\nc","z"]]""")]
    [InlineData("a\t\t\r\n\tb", """[["a","",""],["","b"]]""")]
    [InlineData("\"a\tb\"\tc", """[["a\tb","c"]]""")]
    [InlineData("a\tb\n", """[["a","b"]]""")] // trailing line break
    [InlineData("\uFEFFa\tb", """[["a","b"]]""")] // BOM
    [InlineData("a\rb\nc", """[["a"],["b"],["c"]]""")]
    [InlineData("a\n\nb", """[["a"],[""],["b"]]""")] // empty line keeps its row
    [InlineData("a\tb\tc\nd", """[["a","b","c"],["d"]]""")] // ragged
    [InlineData("a\t", """[["a",""]]""")]
    [InlineData("\"unterminated\nx", """[["\"unterminated"],["x"]]""")]
    [InlineData("", "[]")]
    public void FromDelimited_Tsv_FollowsTheGoogleClipboardRule(string text, string expected) =>
        Assert.Equal(expected, Grid(SheetReaders.FromDelimited(text)));

    [Theory]
    [InlineData("a,b\r\nc,d", """[["a","b"],["c","d"]]""")]
    [InlineData("\"a,b\",c", """[["a,b","c"]]""")]
    [InlineData("\"say \"\"hi\"\"\",x", """[["say \"hi\"","x"]]""")]
    [InlineData("\"multi\r\nline\",x\r\ny", """[["multi\nline","x"],["y"]]""")]
    [InlineData("\"multi\nline\",x", """[["multi\nline","x"]]""")]
    [InlineData("a,,c,", """[["a","","c",""]]""")]
    [InlineData("a\nb,c,d\n", """[["a"],["b","c","d"]]""")]
    [InlineData("\"q\"tail,x", """[["qtail","x"]]""")] // lenient: text after the closing quote is kept
    [InlineData("\"open,x\ny", """[["open,x\ny"]]""")] // unterminated quote runs to the end
    [InlineData("raw \"quotes\" inside,x", """[["raw \"quotes\" inside","x"]]""")]
    public void FromDelimited_Csv_FollowsRfc4180(string text, string expected) =>
        Assert.Equal(expected, Grid(SheetReaders.FromDelimited(text, ',')));

    [Fact]
    public void FromDelimited_SemicolonSeparatedAndTabName()
    {
        var tab = SheetReaders.FromDelimited("a;\"b;c\"\r\n", ';', "Mine");

        Assert.Equal("Mine", tab.Name);
        Assert.Equal("""[["a","b;c"]]""", Grid(tab));
        Assert.Empty(tab.Merges);
        Assert.Empty(tab.HiddenRows);
    }

    [Theory]
    [InlineData("a\tb", '\t')]
    [InlineData("a,b", ',')]
    [InlineData("just text", ',')]
    public void GuessSeparator_PrefersTabs(string text, char expected) => Assert.Equal(expected, SheetReaders.GuessSeparator(text));

    [Fact]
    public void FromDelimited_RejectsQuoteAsSeparator() =>
        Assert.Throws<ArgumentException>(() => SheetReaders.FromDelimited("a", '"'));

    private static string Grid(SheetTab tab) =>
        System.Text.Json.JsonSerializer.Serialize(tab.Rows.Select(r => r.Select(c => c.Text ?? string.Empty)),
            new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    // ---- synthetic workbook ----------------------------------------------------------------------

    private static MemoryStream BuildWorkbook()
    {
        var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook))
        {
            var wb = doc.AddWorkbookPart();
            wb.Workbook = new X.Workbook();

            var theme = wb.AddNewPart<ThemePart>();
            theme.Theme = new D.Theme(new D.ThemeElements(new D.ColorScheme(
                new D.Dark1Color(new D.SystemColor { Val = D.SystemColorValues.WindowText, LastColor = "000000" }),
                new D.Light1Color(new D.SystemColor { Val = D.SystemColorValues.Window, LastColor = "FFFFFF" }),
                new D.Dark2Color(new D.RgbColorModelHex { Val = "1F497D" }),
                new D.Light2Color(new D.RgbColorModelHex { Val = "EEECE1" }),
                new D.Accent1Color(new D.RgbColorModelHex { Val = "4F81BD" }),
                new D.Accent2Color(new D.RgbColorModelHex { Val = "C0504D" }),
                new D.Accent3Color(new D.RgbColorModelHex { Val = "9BBB59" }),
                new D.Accent4Color(new D.RgbColorModelHex { Val = "8064A2" }),
                new D.Accent5Color(new D.RgbColorModelHex { Val = "4BACC6" }),
                new D.Accent6Color(new D.RgbColorModelHex { Val = "F79646" }),
                new D.Hyperlink(new D.RgbColorModelHex { Val = "0000FF" }),
                new D.FollowedHyperlinkColor(new D.RgbColorModelHex { Val = "800080" }))
            { Name = "Office" }))
            { Name = "Office Theme" };

            var styles = wb.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = new X.Stylesheet(
                new X.Fonts(
                    new X.Font(),
                    new X.Font(new X.Color { Indexed = 10 }),
                    new X.Font(new X.Color { Theme = 4 }),
                    new X.Font(new X.Bold(), new X.Strike()),
                    new X.Font(new X.Color { Theme = 1 })),
                new X.Fills(
                    new X.Fill(new X.PatternFill { PatternType = X.PatternValues.None }),
                    new X.Fill(new X.PatternFill { PatternType = X.PatternValues.Gray125 }),
                    new X.Fill(new X.PatternFill(new X.ForegroundColor { Rgb = "FFEBD2D3" }) { PatternType = X.PatternValues.Solid })),
                new X.Borders(new X.Border()),
                new X.CellFormats(
                    new X.CellFormat(),
                    new X.CellFormat { FontId = 1, FillId = 2 },
                    new X.CellFormat { FontId = 2 },
                    new X.CellFormat { FontId = 3 },
                    new X.CellFormat { NumberFormatId = 9 },
                    new X.CellFormat { FontId = 4 }),
                new X.DifferentialFormats(new X.DifferentialFormat(new X.Font(new X.Color { Rgb = "FFD92F24" }))));

            var sst = wb.AddNewPart<SharedStringTablePart>();
            sst.SharedStringTable = new X.SharedStringTable(
                new X.SharedStringItem(new X.Text("Plain")),
                new X.SharedStringItem(
                    new X.Run(new X.RunProperties(new X.Color { Rgb = "FF808080" }), new X.Text("Grey ") { Space = SpaceProcessingModeValues.Preserve }),
                    new X.Run(new X.RunProperties(new X.Bold(), new X.Strike()), new X.Text("struck"))),
                new X.SharedStringItem(new X.Text("Pantokrator ★★★")),
                new X.SharedStringItem(new X.Text("Solar Ray ★★")),
                new X.SharedStringItem(new X.Text("merged")));

            static X.Cell S(string r, int index, uint style = 0) =>
                new() { CellReference = r, DataType = X.CellValues.SharedString, CellValue = new X.CellValue(index.ToString(System.Globalization.CultureInfo.InvariantCulture)), StyleIndex = style };

            var ws = wb.AddNewPart<WorksheetPart>();
            ws.Worksheet = new X.Worksheet(
                new X.Columns(new X.Column { Min = 5, Max = 5, Hidden = true, Width = 9, CustomWidth = true }),
                new X.SheetData(
                    new X.Row(
                        S("A1", 0),
                        S("B1", 1),
                        new X.Cell { CellReference = "C1", DataType = X.CellValues.InlineString, InlineString = new X.InlineString(new X.Text("inline")), StyleIndex = 1 },
                        new X.Cell { CellReference = "D1", DataType = X.CellValues.InlineString, InlineString = new X.InlineString(new X.Text("theme")), StyleIndex = 2 },
                        new X.Cell { CellReference = "E1", DataType = X.CellValues.InlineString, InlineString = new X.InlineString(new X.Text("dark1")), StyleIndex = 5 })
                    { RowIndex = 1 },
                    new X.Row(
                        new X.Cell { CellReference = "A2", CellValue = new X.CellValue("0.3"), StyleIndex = 4 },
                        new X.Cell { CellReference = "B2", DataType = X.CellValues.Boolean, CellValue = new X.CellValue("1") },
                        S("C2", 0, 3))
                    { RowIndex = 2, Hidden = true },
                    new X.Row(S("A3", 2), S("B3", 3)) { RowIndex = 3 },
                    new X.Row(S("A4", 4)) { RowIndex = 4 },
                    new X.Row(new X.Cell { CellReference = "A9", StyleIndex = 1 }) { RowIndex = 9 }),
                new X.MergeCells(new X.MergeCell { Reference = "A4:B4" }),
                new X.ConditionalFormatting(
                    new X.ConditionalFormattingRule(new X.Formula("NOT(ISERROR(SEARCH(\"★★★\",A3)))"))
                    {
                        Type = X.ConditionalFormatValues.ContainsText,
                        Operator = X.ConditionalFormattingOperatorValues.ContainsText,
                        Text = "★★★",
                        FormatId = 0,
                        Priority = 1,
                    })
                { SequenceOfReferences = new ListValue<StringValue> { InnerText = "A3:B3" } });

            var comments = ws.AddNewPart<WorksheetCommentsPart>();
            comments.Comments = new X.Comments(
                new X.Authors(new X.Author("tester")),
                new X.CommentList(
                    new X.Comment(new X.CommentText(new X.Run(new X.Text("Check this")))) { Reference = "A1", AuthorId = 0 },
                    new X.Comment(new X.CommentText(new X.Text("Empty cell note"))) { Reference = "C4", AuthorId = 0 }));

            wb.Workbook.AppendChild(new X.Sheets(new X.Sheet { Id = wb.GetIdOfPart(ws), SheetId = 1, Name = "Plan" }));
        }

        ms.Position = 0;
        return ms;
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
