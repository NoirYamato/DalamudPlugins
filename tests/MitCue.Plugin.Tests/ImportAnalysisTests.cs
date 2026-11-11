using MitCue.Core;
using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Import;
using MitCue.PluginTests.Support;
using static MitCue.PluginTests.TestData;

namespace MitCue.PluginTests;

public class ImportAnalysisTests
{
    private static readonly string[] Mechanics = ["p1.program_loop", "p1.pantokrator", "p2.solar_ray"];

    /// <summary>Like Carry the Clear: a WHM tab, a SCH tab and a combined tab with both columns.</summary>
    private static (SheetDocument Doc, ImportResult Result) CarryTheClear()
    {
        var doc = new SheetDocument
        {
            Title = "Carry the Clear: TOP Healing Plan",
            Tabs =
            [
                Tab("TOP – White Mage", ["Time", "Mechanic", "White Mage"], ["0:28", "Program Loop", "Asylum"], ["1:21", "Pantokrator", "Temp"], ["2:40", "Solar Ray", "Bene"]),
                Tab("TOP – Scholar", ["Time", "Mechanic", "Scholar"], ["0:28", "Program Loop", "Excog"], ["1:21", "Pantokrator", "Deploy"], ["2:40", "Solar Ray", "Soil"]),
                Tab("TOP – WHM + SCH", ["Time", "Mechanic", "Scholar", "White Mage"], ["0:28", "Program Loop", "Excog", "Asylum"], ["1:21", "Pantokrator", "Deploy", "Temp"], ["2:40", "Solar Ray", "Soil", "Bene"]),
            ],
        };

        var plan = new MitPlan
        {
            Assignees = [new Assignee { Key = "White Mage", Job = Job.WHM }, new Assignee { Key = "Scholar", Job = Job.SCH }],
        };
        for (var r = 0; r < Mechanics.Length; r++)
        {
            plan.Entries.Add(Make(doc.Tabs[0], r + 1, 2, Mechanics[r], "White Mage", Job.WHM));
            plan.Entries.Add(Make(doc.Tabs[1], r + 1, 2, Mechanics[r], "Scholar", Job.SCH));
            plan.Entries.Add(Make(doc.Tabs[2], r + 1, 2, Mechanics[r], "Scholar", Job.SCH));
            plan.Entries.Add(Make(doc.Tabs[2], r + 1, 3, Mechanics[r], "White Mage", Job.WHM));
        }

        return (doc, new ImportResult { Plan = plan });
    }

    /// <summary>Like LPDU: a title tab, P1/P2 with slot columns, P3 with slot+job columns, and a tank tab.</summary>
    private static (SheetDocument Doc, ImportResult Result) Lpdu()
    {
        var doc = new SheetDocument
        {
            Title = "LPDU TOP Mitigation Sheet",
            Tabs =
            [
                Tab("ALOO", [null], ["LPDU"], [null]),
                Tab("P1P2", ["P1", "MT", "H1", "H2"], ["Pantokrator", "Party Mit", "Everything", "Everything"], ["Looper Set 3", null, null, "Kera/Soil"]),
                Tab("P3", ["P3", "MT", "H1 WHM", "H2 SCH", "H2 SGE"], ["Solar Ray", "Rep", "Wings", "Soil", "Kera"]),
                Tab("WARDRK", ["P5", "WAR", "DRK"], ["Solar Ray 1", "Kitchen Sink", "Kitchen Sink"]),
            ],
        };

        var plan = new MitPlan
        {
            Assignees =
            [
                new Assignee { Key = "MT", Slot = "MT" },
                new Assignee { Key = "H1", Slot = "H1" },
                new Assignee { Key = "H2", Slot = "H2" },
                new Assignee { Key = "H1 WHM", Slot = "H1", Job = Job.WHM },
                new Assignee { Key = "H2 SCH", Slot = "H2", Job = Job.SCH },
                new Assignee { Key = "H2 SGE", Slot = "H2", Job = Job.SGE },
                new Assignee { Key = "WAR", Job = Job.WAR },
                new Assignee { Key = "DRK", Job = Job.DRK },
            ],
        };
        plan.Entries.Add(Make(doc.Tabs[1], 1, 1, "p1.pantokrator", "MT", null, "MT"));
        plan.Entries.Add(Make(doc.Tabs[1], 1, 2, "p1.pantokrator", "H1", null, "H1"));
        plan.Entries.Add(Make(doc.Tabs[1], 1, 3, "p1.pantokrator", "H2", null, "H2"));
        plan.Entries.Add(Make(doc.Tabs[1], 2, 3, "p1.program_loop", "H2", Job.SCH, "H2"));
        plan.Entries.Add(Make(doc.Tabs[1], 2, 3, "p1.program_loop", "H2", Job.SGE, "H2"));
        plan.Entries.Add(Make(doc.Tabs[2], 1, 1, "p2.solar_ray", "MT", null, "MT"));
        plan.Entries.Add(Make(doc.Tabs[2], 1, 2, "p2.solar_ray", "H1 WHM", Job.WHM, "H1"));
        plan.Entries.Add(Make(doc.Tabs[2], 1, 3, "p2.solar_ray", "H2 SCH", Job.SCH, "H2"));
        plan.Entries.Add(Make(doc.Tabs[2], 1, 4, "p2.solar_ray", "H2 SGE", Job.SGE, "H2"));
        plan.Entries.Add(Make(doc.Tabs[3], 1, 1, "p2.solar_ray", "WAR", Job.WAR));
        plan.Entries.Add(Make(doc.Tabs[3], 1, 2, "p2.solar_ray", "DRK", Job.DRK));
        return (doc, new ImportResult { Plan = plan });
    }

    private static PlanEntry Make(SheetTab tab, int row, int col, string mechanic, string column, Job? job, string? slot = null) => new()
    {
        Id = $"{tab.Name}-{row}-{col}-{job}",
        Mechanic = mechanic,
        Who = new WhoSpec { Assignee = column, Job = job, Slot = slot },
        Do = [PlanItem.ForNote("x")],
        Src = tab.Ref(row, col),
    };

    [Fact]
    public void CountsEntriesAndColumnsPerTab()
    {
        var (doc, result) = CarryTheClear();
        var analysis = ImportAnalysis.FromResult(doc, result);

        Assert.True(analysis.Available);
        Assert.Equal(3, analysis.Tabs.Count);
        Assert.Equal(3, analysis.Tabs[0].Entries);
        Assert.Equal(new[] { "White Mage" }, analysis.Tabs[0].Columns);
        Assert.Equal(6, analysis.Tabs[2].Entries);
        Assert.Equal(new[] { "Scholar", "White Mage" }, analysis.Tabs[2].Columns);
        Assert.Equal(4, analysis.Tabs[0].NonEmptyRows);
        Assert.Equal(6, analysis.EntriesOf("White Mage", ["TOP – White Mage", "TOP – WHM + SCH"]));
    }

    [Fact]
    public void AWhiteMageGetsTheWhiteMageTabOnly()
    {
        var (doc, result) = CarryTheClear();
        var analysis = ImportAnalysis.FromResult(doc, result);

        var tabs = analysis.DefaultTabs(Job.WHM, null, null);
        Assert.Equal(new[] { "TOP – White Mage" }, tabs);
        Assert.Equal(new[] { "White Mage" }, analysis.DefaultColumns(tabs, Job.WHM, null));
    }

    [Fact]
    public void AScholarGetsTheScholarTab()
    {
        var (doc, result) = CarryTheClear();
        var analysis = ImportAnalysis.FromResult(doc, result);

        Assert.Equal(new[] { "TOP – Scholar" }, analysis.DefaultTabs(Job.SCH, null, null));
    }

    [Fact]
    public void TheLinkedTabComesFirst()
    {
        var (doc, result) = CarryTheClear();
        var analysis = ImportAnalysis.FromResult(doc, result);

        Assert.Equal(new[] { "TOP – WHM + SCH" }, analysis.DefaultTabs(Job.WHM, null, "TOP – WHM + SCH"));
    }

    [Fact]
    public void WithoutAColumnEveryNonRepeatingTabIsChosen()
    {
        var (doc, result) = CarryTheClear();
        var analysis = ImportAnalysis.FromResult(doc, result);

        var tabs = analysis.DefaultTabs(Job.WAR, null, null);
        Assert.Equal(new[] { "TOP – White Mage", "TOP – Scholar" }, tabs);
        Assert.Equal(new[] { "White Mage", "Scholar" }, analysis.DefaultColumns(tabs, Job.WAR, null));

        var repeated = analysis.RepeatedTabs(["TOP – White Mage", "TOP – Scholar", "TOP – WHM + SCH"]);
        Assert.Equal("TOP – White Mage", repeated["TOP – WHM + SCH"]);
        Assert.False(repeated.ContainsKey("TOP – Scholar"));
    }

    [Fact]
    public void AScholarGetsEveryPhaseTabAndBothH2Columns()
    {
        var (doc, result) = Lpdu();
        var analysis = ImportAnalysis.FromResult(doc, result);

        var tabs = analysis.DefaultTabs(Job.SCH, null, "ALOO");
        Assert.Equal(new[] { "P1P2", "P3" }, tabs);
        Assert.Equal(new[] { "H2", "H2 SCH" }, analysis.DefaultColumns(tabs, Job.SCH, null));
    }

    [Fact]
    public void PlayerColumnsUseTheConfiguredSlot()
    {
        var (_, result) = Lpdu();
        var columns = result.Plan.Assignees;

        Assert.Equal(new[] { "H1", "H1 WHM" }, ImportAnalysis.PlayerColumns(columns, Job.WHM, null).Select(a => a.Key));
        Assert.Equal(new[] { "MT" }, ImportAnalysis.PlayerColumns(columns, Job.GNB, "T1").Select(a => a.Key));
        Assert.Equal(new[] { "MT", "WAR" }, ImportAnalysis.PlayerColumns(columns, Job.WAR, "MT").Select(a => a.Key));
        Assert.Empty(ImportAnalysis.PlayerColumns(columns, Job.None, null));
    }

    [Fact]
    public void WithoutATrialImportTheHeaderRowsChooseThePlayersTabs()
    {
        var (doc, _) = Lpdu();
        var analysis = ImportAnalysis.Unavailable(doc, "not available");

        Assert.False(analysis.Available);
        Assert.Equal("not available", analysis.Reason);
        Assert.Equal(new[] { "MT", "H1", "H2" }, analysis.Tabs[1].HeaderColumns.Select(a => a.Key));
        Assert.Equal(new[] { "P1P2", "P3" }, analysis.DefaultTabs(Job.SCH, null, null));
        Assert.Equal(new[] { "P1P2", "P3" }, analysis.HeaderDefaultTabs(Job.SCH, null, "ALOO", out var basis));
        Assert.Equal("the tabs with your columns (H2, H2 SCH)", basis);
        Assert.Equal(new[] { "WARDRK" }, analysis.DefaultTabs(Job.WAR, null, null));
        Assert.Empty(analysis.DefaultColumns(["P3"], Job.SCH, null));
    }

    [Fact]
    public void WithoutATrialImportOrAPlayerEveryTabWithContentIsChosen()
    {
        var (doc, _) = Lpdu();
        var analysis = ImportAnalysis.Unavailable(doc, "not available");

        // The link points at the title tab (no header row): it says nothing about the plan.
        Assert.Equal(new[] { "P1P2", "P3", "WARDRK" }, analysis.HeaderDefaultTabs(Job.None, null, "ALOO", out var basis));
        Assert.Null(basis);
    }

    [Fact]
    public void WithoutATrialImportAPlayersOwnTabWinsOverACombinedTab()
    {
        // Review finding: with the importer not available, every tab was chosen, so the AI prompt for Carry the Clear held
        // the White Mage's calls twice (the White Mage tab and the combined tab, worded differently).
        var (doc, _) = CarryTheClear();
        var analysis = ImportAnalysis.Unavailable(doc, "not available");

        Assert.Equal(new[] { "TOP – White Mage" }, analysis.DefaultTabs(Job.WHM, null, "TOP – White Mage"));
        Assert.Equal(new[] { "TOP – Scholar" }, analysis.DefaultTabs(Job.SCH, null, "TOP – White Mage"));
        Assert.Equal(new[] { "TOP – White Mage" }, analysis.HeaderDefaultTabs(Job.None, null, "TOP – White Mage", out var basis));
        Assert.Equal("the tab your link points at", basis);
    }

    [Fact]
    public void ThePromptForTheRealCarryTheClearHoldsTheWhiteMagesCallsOnce()
    {
        // The reviewer's probe on the real sheet (its CSV export): the trial import is not available in this build.
        var doc = FixtureSheets.CarryTheClear();
        var analysis = ImportAnalysis.Unavailable(doc, "The sheet importer is not available in this build of MitCue yet.");

        var tabs = analysis.DefaultTabs(Job.WHM, null, "TOP – White Mage");
        var text = SheetText.ForPrompt(doc, tabs);

        Assert.Equal(new[] { "TOP – White Mage" }, tabs);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, @"Pantokrator: line stack"));
        Assert.Contains("Temp (1:18)", text);
        Assert.DoesNotContain("TOP – WHM + SCH", text);
    }

    [Fact]
    public void TheRealLpduHeadersChooseAScholarsPhaseTabs()
    {
        var doc = FixtureSheets.Lpdu();
        var analysis = ImportAnalysis.Unavailable(doc, "not available");

        Assert.Equal(new[] { "P1P2", "P3", "P4", "P5", "P6" }, analysis.DefaultTabs(Job.SCH, null, "ALOO"));
        Assert.Equal(new[] { "WARDRK", "WARGNB", "WARPLD" }, analysis.DefaultTabs(Job.WAR, null, "ALOO"));
    }

    [Theory]
    [InlineData("H2 SCH", "H2", Job.SCH)]
    [InlineData("White Mage", null, Job.WHM)]
    [InlineData("WAR", null, Job.WAR)]
    [InlineData("MT", "MT", null)]
    [InlineData("D3", "R1", null)]
    [InlineData("SGE H2", "H2", Job.SGE)]
    public void ReadsColumnHeaders(string text, string? slot, Job? job)
    {
        var column = ImportAnalysis.HeaderColumn(text);
        Assert.NotNull(column);
        Assert.Equal(text, column.Key);
        Assert.Equal(slot, column.Slot);
        Assert.Equal(job, column.Job);
    }

    [Theory]
    [InlineData("Notes")]
    [InlineData("P3")]
    [InlineData("CM LB Tank")]
    [InlineData("Top WHMs (of 36)")]
    [InlineData("TOP — White Mage")]
    [InlineData("24")]
    [InlineData("0:28")]
    [InlineData("3-4")]
    [InlineData("H2 28")]
    [InlineData("")]
    public void OtherTextIsNotAColumnHeader(string text) => Assert.Null(ImportAnalysis.HeaderColumn(text));

    [Fact]
    public void ColumnKeysFallBackToSlotAndJob()
    {
        Assert.Equal("H2 SCH", ImportAnalysis.ColumnKey(new PlanEntry { Who = new WhoSpec { Slot = "H2", Job = Job.SCH } }));
        Assert.Equal("Scholar", ImportAnalysis.ColumnKey(new PlanEntry { Who = new WhoSpec { Assignee = " Scholar ", Job = Job.SCH } }));
        Assert.Equal(string.Empty, ImportAnalysis.ColumnKey(new PlanEntry()));
    }
}
