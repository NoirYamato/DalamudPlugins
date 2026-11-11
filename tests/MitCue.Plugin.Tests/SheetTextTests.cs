using MitCue.Core.Import;
using MitCue.Import;

namespace MitCue.PluginTests;

public class SheetTextTests
{
    [Theory]
    [InlineData("'TOP – White Mage'!C18", "TOP – White Mage", 17, 2)]
    [InlineData("P3!A1", "P3", 0, 0)]
    [InlineData("'P1/P2'!$AA$10", "P1/P2", 9, 26)]
    [InlineData("'It''s'!B2", "It's", 1, 1)]
    public void ParsesCellReferences(string src, string tab, int row, int column)
    {
        var cell = CellRef.TryParse(src);
        Assert.NotNull(cell);
        Assert.Equal(tab, cell.Tab);
        Assert.Equal(row, cell.Row);
        Assert.Equal(column, cell.Column);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("C18")]
    [InlineData("'P3'!")]
    [InlineData("'P3'!18")]
    [InlineData("'P3'!C0")]
    [InlineData("'P3'!C")]
    public void RejectsOtherText(string? src) => Assert.Null(CellRef.TryParse(src));

    [Fact]
    public void RoundTripsSheetTabReferences()
    {
        var tab = new SheetTab { Name = "TOP – WHM + SCH", Rows = [] };
        var cell = CellRef.TryParse(tab.Ref(41, 27))!;
        Assert.Equal((tab.Name, 41, 27), (cell.Tab, cell.Row, cell.Column));
        Assert.Equal("AB42", cell.Address);
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void NamesColumns(int column, string letters) => Assert.Equal(letters, SheetText.ColumnLetters(column));

    [Fact]
    public void WritesTheSourceForThePrompt()
    {
        var doc = new SheetDocument
        {
            Title = "Carry the Clear",
            Tabs =
            [
                new SheetTab
                {
                    Name = "TOP – White Mage",
                    Rows =
                    [
                        [SheetCell.Empty, new SheetCell { Text = "White Mage" }],
                        [],
                        [
                            new SheetCell { Text = "1:21" },
                            new SheetCell { Text = "Pantokrator\nline stacks" },
                            new SheetCell
                            {
                                Text = "Benison (0:07) / Aquaveil (0:07)",
                                Runs = [new TextRun(0, null, false), new TextRun(14, "#808080", false)],
                            },
                            new SheetCell { Text = "old call", Strike = true },
                        ],
                    ],
                },
                new SheetTab { Name = "Other", Rows = [[new SheetCell { Text = "x" }]] },
            ],
        };

        var text = SheetText.ForPrompt(doc, ["TOP – White Mage"]);

        Assert.Equal(
            "Source: Carry the Clear\n\n### Tab \"TOP – White Mage\"\n" +
            "R1 | B: White Mage\n" +
            "R3 | A: 1:21 | B: Pantokrator ⏎ line stacks | C: Benison (0:07)[#808080:  / Aquaveil (0:07)] | D: [struck: old call]\n",
            text);
        Assert.Contains("### Tab \"Other\"", SheetText.ForPrompt(doc));
        Assert.EndsWith("[... the rest of the source was cut to keep the prompt small ...]\n", SheetText.ForPrompt(doc, null, 40));
    }

    [Fact]
    public void FindsCellTextAndCountsRows()
    {
        var tab = TestData.Tab("P3", ["P3", "MT"], [null, null], ["Hello World", "Rep"]);
        var doc = new SheetDocument { Title = "LPDU", Tabs = [tab] };

        Assert.Equal("Rep", SheetText.CellText(doc, "'P3'!B3"));
        Assert.Null(SheetText.CellText(doc, "'P3'!B2"));
        Assert.Null(SheetText.CellText(doc, "'P4'!B3"));
        Assert.Null(SheetText.CellText(null, "'P3'!B3"));
        Assert.Equal(2, SheetText.NonEmptyRows(tab));
    }
}
