using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;
using MitCue.Core.Tests.Data;

namespace MitCue.Core.Tests.Import;

/// <summary>The imported plans work with the reminder scheduler for the players they are for.</summary>
public class ImportedPlanRuntimeTests
{
    public static TheoryData<int, Job, string?> Players => new()
    {
        { 0, Job.WHM, null },
        { 1, Job.WHM, null },
        { 1, Job.SCH, null },
        { 2, Job.SCH, "H2" },
        { 2, Job.SGE, "H2" },
        { 2, Job.WHM, "H1" },
        { 2, Job.AST, "H1" },
        { 2, Job.WAR, "MT" },
        { 2, Job.MCH, "R1" },
        { 3, Job.SCH, null },
        { 3, Job.SCH, "H2" },
    };

    [Theory]
    [MemberData(nameof(Players))]
    public void Scheduler_BuildsEveryReminder(int plan, Job job, string? slot)
    {
        var imported = PlanImports.Import(PlanImports.SheetImports[plan]).Plan;
        var mine = PlanSelection.For(imported, new PlayerContext(job, 90, slot)).ToList();
        Assert.NotEmpty(mine);

        var scheduler = new ReminderScheduler(PlanImports.Top, mine, RepoFiles.Actions, new ReminderOptions());
        Assert.Empty(scheduler.Problems);
        Assert.Equal(mine.Sum(e => e.Do.Count), scheduler.All.Count);
        Assert.All(scheduler.All.Where(r => r.Item.Note is null), r => Assert.True(r.Earliest <= r.PressAt && r.PressAt < r.Latest, $"{r.Entry.Id}: {r.Earliest} ≤ {r.PressAt} < {r.Latest}"));
    }

    [Fact]
    public void WhiteMagePressTimes_AreTheSheetTimesOnTheTimeline()
    {
        // Row-relative offsets reproduce "phase zero + sheet time" up to the sheet's whole-second rounding and the
        // P5/P6 drift (docs/research/mechanics.md §4.2): within 1.5 s everywhere.
        var result = PlanImports.Whm;
        var scheduler = new ReminderScheduler(PlanImports.Top, result.Plan.Entries, RepoFiles.Actions, new ReminderOptions());
        var temperance = Assert.Single(scheduler.All, r => r.Entry.Src == "'TOP – White Mage'!C18" && r.Item.Name == "Temperance");
        Assert.Equal(result.PhaseZeros["p1"] + 78, temperance.PressAt, 1.0); // whole-second rounding: up to 1 s
        var plenary = Assert.Single(scheduler.All, r => r.Entry.Src == "'TOP – White Mage'!C57");
        Assert.Equal(result.PhaseZeros["p6"] + 65, plenary.PressAt, 1.5);
        var asylum = Assert.Single(scheduler.All, r => r.Entry.Src == "'TOP – White Mage'!C55" && r.Item.Name == "Asylum");
        Assert.Equal(result.PhaseZeros["p5"] + 270, asylum.PressAt, 1.5);
    }
}
