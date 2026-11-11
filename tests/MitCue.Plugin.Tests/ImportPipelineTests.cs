using MitCue.Core.Import;
using MitCue.Import;

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
    }
}
