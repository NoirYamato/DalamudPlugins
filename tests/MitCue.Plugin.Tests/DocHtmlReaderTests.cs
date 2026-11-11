using MitCue.Core.Import;
using MitCue.Import;

namespace MitCue.PluginTests;

public class DocHtmlReaderTests
{
    [Fact]
    public void ReadsParagraphsIntoATextTab()
    {
        const string html = """
            <html><head><meta charset="utf-8"><title>My &amp; Plan</title><style>.c1{color:#ff0000}</style></head>
            <body class="c5"><h1 class="c2"><span>P3</span></h1>
            <p class="c1"><span>Hello World:</span><span> Temperance &amp; Bell</span></p>
            <p><span>Line&nbsp;one<br>Line two</span></p>
            <p></p><ul><li>Kera&#39;s turn</li></ul>
            <script>var x = "<p>not text</p>";</script></body></html>
            """;

        var doc = DocHtmlReader.Read(html, null);

        Assert.Equal("My & Plan", doc.Title);
        var tab = Assert.Single(doc.Tabs);
        Assert.Equal(DocHtmlReader.TextTabName, tab.Name);
        var rows = tab.Rows.Select(r => (r[0].Text, r[1].Text)).ToList();
        Assert.Equal(
            new (string?, string?)[]
            {
                ("P3", "h1"),
                ("Hello World: Temperance & Bell", "p"),
                ("Line one", "p"),
                ("Line two", "p"),
                ("Kera's turn", "li"),
            },
            rows);
    }

    [Fact]
    public void ReadsTablesWithMergedCells()
    {
        const string html = """
            <html><body>
            <p>Before</p>
            <table><tbody>
              <tr><td><p>Mechanic</p></td><td colspan="2"><p>Healers</p></td></tr>
              <tr><td rowspan="2"><p>Pantokrator</p></td><td><p>Temp</p><p>(1:18)</p></td><td>Soil</td></tr>
              <tr><td>Bell<br>after 1st</td><td>Expedient</td></tr>
            </tbody></table>
            <p>After</p>
            </body></html>
            """;

        var doc = DocHtmlReader.Read(html, "Doc");

        Assert.Equal("Doc", doc.Title);
        Assert.Equal(new[] { "Table 1", DocHtmlReader.TextTabName }, doc.Tabs.Select(t => t.Name));
        var table = doc.Tabs[0];
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal("Mechanic", table.Cell(0, 0).Text);
        Assert.Equal("Healers", table.Cell(0, 1).Text);
        Assert.True(table.Cell(0, 2).IsEmpty);
        Assert.Equal("Pantokrator", table.Cell(1, 0).Text);
        Assert.Equal("Temp\n(1:18)", table.Cell(1, 1).Text);
        Assert.Equal("Soil", table.Cell(1, 2).Text);
        Assert.True(table.Cell(2, 0).IsEmpty);
        Assert.Equal("Bell\nafter 1st", table.Cell(2, 1).Text);
        Assert.Equal("Expedient", table.Cell(2, 2).Text);
        Assert.Equal(new[] { new CellRange(0, 1, 0, 2), new CellRange(1, 0, 2, 0) }, table.Merges);

        Assert.Equal(new[] { "Before", "After" }, doc.Tabs[1].Rows.Select(r => r[0].Text));
    }

    [Fact]
    public void FlattensNestedTablesAndSkipsImages()
    {
        var image = "<img src=\"data:image/png;base64," + new string('A', 200_000) + "\">";
        var html = $"<table><tr><td>Outer {image}<table><tr><td>inner a</td><td>inner b</td></tr></table></td><td>B</td></tr></table>";

        var doc = DocHtmlReader.Read(html, "Doc");

        var table = Assert.Single(doc.Tabs);
        Assert.Equal(2, table.Rows[0].Count);
        Assert.Equal("Outer\ninner a inner b", table.Cell(0, 0).Text);
        Assert.Equal("B", table.Cell(0, 1).Text);
    }

    [Fact]
    public void ToleratesBrokenMarkup()
    {
        var doc = DocHtmlReader.Read("<p>one<p>two</td></tr><table><tr><td>x", null);
        Assert.Equal("Google Doc", doc.Title);
        Assert.Contains(doc.Tabs, t => t.Name == "Table 1" && t.Cell(0, 0).Text == "x");
        Assert.Contains(doc.Tabs, t => t.Name == DocHtmlReader.TextTabName && t.Rows.Count == 2);
        Assert.Empty(DocHtmlReader.Read("<html><head><title></title></head><body></body></html>", null).Tabs);
    }
}
