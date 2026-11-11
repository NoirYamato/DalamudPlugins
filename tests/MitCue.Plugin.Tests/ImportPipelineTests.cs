using MitCue.Core.Import;
using MitCue.Import;
using MitCue.PluginTests.Support;

namespace MitCue.PluginTests;

public class ImportPipelineTests
{
    [Fact]
    public void ExplainsFailures()
    {
        Assert.Contains("not available in this build", ImportPipeline.Explain(new NotImplementedException(), "Reading .xlsx workbooks"));
        Assert.StartsWith("Reading .xlsx workbooks", ImportPipeline.Explain(new NotImplementedException(), "Reading .xlsx workbooks"));
        Assert.Equal("Bad YAML on line 3", ImportPipeline.Explain(new FormatException("Bad YAML on line 3"), "x"));
        Assert.Equal("Bad YAML on line 3", ImportPipeline.Explain(new AggregateException(new FormatException("Bad YAML on line 3")), "x"));
        Assert.Equal("Already explained", ImportPipeline.Explain(new ImportStepException("Already explained"), "x"));
        Assert.Equal("Cancelled.", ImportPipeline.Explain(new OperationCanceledException(), "x"));
        Assert.Equal("Import failed: InvalidOperationException: boom", ImportPipeline.Explain(new InvalidOperationException("boom"), "Import"));
    }

    [Theory]
    [InlineData("a\tb\nc\td", '\t')]
    [InlineData("a,b\nc,d\ne,f", ',')]
    [InlineData("just text", '\t')]
    [InlineData("one, two\nthree\nfour\nfive", '\t')]
    public void GuessesTheSeparator(string text, char expected) => Assert.Equal(expected, ImportPipeline.GuessSeparator(text));

    [Theory]
    [InlineData("Carry the Clear: TOP Healing Plan", "carry_the_clear")]
    [InlineData("carry-the-clear (copy)", "carry_the_clear")]
    [InlineData("LPDU TOP Mitigation Sheet", null)]
    [InlineData(null, null)]
    public void GuessesTheCalibrationPreset(string? title, string? expected) =>
        Assert.Equal(expected, ImportPipeline.GuessCalibration(TestData.Top(), title));

    [Fact]
    public void MatchesTheLinkedTabName()
    {
        var workbook = new SheetDocument
        {
            Title = "Plan",
            Tabs = [TestData.Tab("ALOO"), TestData.Tab("P1P2"), TestData.Tab("TOP – Scholar"), TestData.Tab("WARDRK")],
        };

        Assert.Equal("TOP – Scholar", ImportPipeline.MatchTabName(workbook, "Carry the Clear: TOP Healing Plan - TOP – Scholar", "Carry the Clear: TOP Healing Plan"));
        Assert.Equal("P1P2", ImportPipeline.MatchTabName(workbook, "LPDU TOP Mitigation Sheet - P1/P2", "LPDU TOP Mitigation Sheet"));
        Assert.Equal("WARDRK", ImportPipeline.MatchTabName(workbook, "A - B - WAR/DRK", null));
        Assert.Null(ImportPipeline.MatchTabName(workbook, "Plan - P9", "Plan"));
        Assert.Null(ImportPipeline.MatchTabName(workbook, null, "Plan"));
    }

    [Fact]
    public void RefusesEmptyInput()
    {
        Assert.Throws<ImportStepException>(() => ImportPipeline.LoadPasted("  \n ", '\t'));
        Assert.Throws<ImportStepException>(() => ImportPipeline.ParseYaml("   "));
    }

    [Fact]
    public void ReadsPastedCellsWhenTheReaderIsAvailable()
    {
        LoadedSource source;
        try
        {
            source = ImportPipeline.LoadPasted("P3\tMT\tH2 SCH\r\nHello World\tRep\tSoil", '\t');
        }
        catch (ImportStepException ex) when (ex.NotAvailable)
        {
            Assert.Contains("not available in this build", ex.Message);
            return;
        }

        Assert.Equal("paste", source.Kind);
        var tab = Assert.Single(source.Document.Tabs);
        Assert.Equal("Soil", tab.Cell(1, 2).Text);
        Assert.Equal(tab.Name, source.LinkedTab);
    }

    [Fact]
    public void ReadsAiAnswersWhenTheSerializerIsAvailable()
    {
        const string answer = """
            Here is the converted plan:

            ```yaml
            format: mitcue-plan/1
            name: Test plan
            fight: top
            assignees:
              - { key: "White Mage", slot: H1, job: WHM }
            entries:
              - id: e001
                mechanic: p1.pantokrator
                offset: -3
                who: { job: WHM, assignee: "White Mage" }
                do:
                  - { action: 16536, name: Temperance }
                  - { note: "Everything" }
            ```
            """;

        Core.Plans.MitPlan plan;
        try
        {
            plan = ImportPipeline.ParseYaml(answer);
        }
        catch (ImportStepException ex) when (ex.NotAvailable)
        {
            Assert.Contains("not available in this build", ex.Message);
            return;
        }

        Assert.Equal("top", plan.Fight);
        var entry = Assert.Single(plan.Entries);
        Assert.Equal("p1.pantokrator", entry.Mechanic);
        Assert.Equal(-3, entry.Offset);
        Assert.Equal(16536u, entry.Do[0].Action);
        Assert.Equal("Everything", entry.Do[1].Note);
    }

    [Fact]
    public void DescribesSources()
    {
        var source = new LoadedSource { Document = new SheetDocument { Title = "LPDU", Tabs = [TestData.Tab("P3")] }, Kind = "google-sheet" };
        Assert.Equal("Google Sheet \"LPDU\" (1 tab)", source.Describe());

        var pasted = ImportPipeline.PastedSource(TestData.Tab("P3", ["P3", "MT"]), ImportPipeline.PastedSource(TestData.Tab("Pasted", ["P1", "MT"]), null));
        Assert.Equal("pasted cells (2 tabs: Pasted, P3)", pasted.Describe());
    }

    [Fact]
    public void AddsPastedTabsToEarlierOnes()
    {
        // Review finding: each paste replaced the source, so a plan over several tabs (LPDU's P1/P2 ... P6) could not be
        // pasted completely.
        var first = ImportPipeline.PastedSource(TestData.Tab("Pasted", ["P1", "MT", "H2"], ["Pantokrator", "Party Mit", "Everything"]), null);
        Assert.Equal("Pasted", first.LinkedTab);

        var name = ImportPipeline.UniqueTabName(first.Document.Tabs, "  P3 ");
        var second = ImportPipeline.PastedSource(TestData.Tab(name, ["P3", "MT", "H2 SCH"], ["Hello World", "Rep", "Soil"]), first);

        Assert.Equal("paste", second.Kind);
        Assert.Equal(new[] { "Pasted", "P3" }, second.Document.Tabs.Select(t => t.Name));
        Assert.Null(second.LinkedTab);
        Assert.Equal("Everything", second.Document.Tabs[0].Cell(1, 2).Text);
        Assert.Equal("Soil", second.Document.Tabs[1].Cell(1, 2).Text);
        Assert.Null(second.Warning);
    }

    [Fact]
    public void NamesPastedTabsUniquely()
    {
        var tabs = new[] { TestData.Tab("Pasted"), TestData.Tab("P3"), TestData.Tab("Pasted 2") };
        Assert.Equal("Pasted", ImportPipeline.UniqueTabName([], null));
        Assert.Equal("Pasted 3", ImportPipeline.UniqueTabName(tabs, " "));
        Assert.Equal("p3 (2)", ImportPipeline.UniqueTabName(tabs, "p3"));
        Assert.Equal("P4", ImportPipeline.UniqueTabName(tabs, "P4"));
    }

    [Fact]
    public void WarnsWhenOnePasteHoldsTwoTabs()
    {
        // The reviewer's probe: LPDU's P1/P2 tab, then its P3 tab pasted below it in the same box. The P1/P2 rows were then
        // read against the P3 columns, without a word.
        var rows = FixtureSheets.Rows("lpdu", "p1-p2.csv").Concat(FixtureSheets.Rows("lpdu", "p3.csv"));
        var tab = FixtureSheets.Tab("Pasted", rows);

        var source = ImportPipeline.PastedSource(tab, null);

        Assert.NotNull(source.Warning);
        Assert.Contains("2 header rows (rows 1, 13)", source.Warning);
        Assert.Contains("Add as another tab", source.Warning);
    }

    [Fact]
    public void OneTabPastedAloneGetsNoWarning()
    {
        foreach (var file in new[] { "p1-p2.csv", "p3.csv", "p6.csv", "war-drk.csv" })
            Assert.Null(ImportPipeline.PastedSource(FixtureSheets.Tab("Pasted", FixtureSheets.Rows("lpdu", file)), null).Warning);
        foreach (var (file, _) in FixtureSheets.CarryTheClearTabs)
            Assert.Null(ImportPipeline.PastedSource(FixtureSheets.Tab("Pasted", FixtureSheets.Rows("carry-the-clear", file)), null).Warning);
    }
}
