using MitCue.Core;
using MitCue.Core.Plans;
using MitCue.PluginTests.Support;
using MitCue.UI;

namespace MitCue.PluginTests;

/// <summary>
/// Importing a share code in /mitcue > Plans: whether it has calls for the player's job (as the overlay would select
/// them) and whether it becomes their active plan. Regression tests for review round 3.
/// </summary>
public class ShareCodeImportTests
{
    private const string CarryTheClearWhm = "335b6c4f-3684-55e0-a8d0-3e78724ef200";
    private const string Lpdu = "1f75e1e8-7321-566c-ae86-8568019fe914";

    /// <summary>A role sheet: columns by slot only (no job), as many statics lay out their mit sheet.</summary>
    private static MitPlan RoleSheet() => new()
    {
        Id = "role-sheet",
        Name = "Our TOP mit (roles)",
        Fight = "top",
        Assignees = [new Assignee { Key = "H1", Slot = "H1" }, new Assignee { Key = "H2", Slot = "H2" }],
        Entries =
        [
            new PlanEntry { Id = "e1", Mechanic = "p1.loop_set1", Offset = -3, Who = new WhoSpec { Slot = "H1", Assignee = "H1" }, Do = [PlanItem.ForAction(Kit.Temperance, "Temperance")] },
            new PlanEntry { Id = "e2", Mechanic = "p1.loop_set3", Offset = -3, Who = new WhoSpec { Slot = "H2", Assignee = "H2" }, Do = [PlanItem.ForAction(Kit.SacredSoil, "Sacred Soil")] },
        ],
    };

    [Fact]
    public void AShareCodeOfARoleSheetCanBeMadeActiveForTheJobWhoseSlotHasCalls()
    {
        // Review round 3: no column names a job, so the store said "It has no calls for WHM" and ignored the ticked box,
        // though the overlay shows the H1 White Mage the H1 column's call (the import window counted it too).
        var parts = SessionManagerTests.NewManager(new Configuration { DefaultSlot = "H1" });
        parts.Plans.EnsureLoaded();

        Assert.True(parts.Plans.ImportShareCode(PlanSerializer.ToShareCode(RoleSheet()), Job.WHM, makeActive: true, out var copy, out var message), message);

        Assert.Equal(1, parts.Plans.CallsFor(copy!, Job.WHM));
        Assert.DoesNotContain("no calls", message, StringComparison.Ordinal);
        Assert.Contains("It is your active plan for TOP as WHM.", message, StringComparison.Ordinal);
        Assert.Equal("role-sheet", parts.Plans.ActivePlanFor("top", Job.WHM)?.Id);
    }

    [Fact]
    public void WithoutASlotARoleSheetSaysToSetOne()
    {
        var parts = SessionManagerTests.NewManager();
        parts.Plans.EnsureLoaded();

        Assert.True(parts.Plans.ImportShareCode(PlanSerializer.ToShareCode(RoleSheet()), Job.WHM, makeActive: true, out _, out var message), message);

        Assert.Contains("It has no calls for WHM without a slot: set \"My usual party slot\" in the General tab", message, StringComparison.Ordinal);
        Assert.Equal(CarryTheClearWhm, parts.Plans.ActivePlanFor("top", Job.WHM)?.Id);
    }

    [Theory]
    [InlineData(Job.MNK, "M1", 7)]
    [InlineData(Job.DRG, "M2", 8)]
    public void AMeleeCanMakeTheLpduShareCodeTheirActivePlan(Job job, string slot, int calls)
    {
        // Review round 3: LPDU's melee calls are in the job-less "M1" and "M2" columns, so a melee was told the plan had no
        // calls for them and the ticked box was ignored.
        var parts = SessionManagerTests.NewManager(new Configuration { DefaultSlot = slot });
        parts.Plans.EnsureLoaded();
        var code = PlanSerializer.ToShareCode(parts.Plans.Get(Lpdu)!.Plan);

        Assert.True(parts.Plans.ImportShareCode(code, job, makeActive: true, out var copy, out var message), message);

        Assert.Equal(calls, parts.Plans.CallsFor(copy!, job));
        Assert.Contains($"It is your active plan for TOP as {job}.", message, StringComparison.Ordinal);
        Assert.Equal(Lpdu, parts.Plans.ActivePlanFor("top", job)?.Id);
    }

    [Fact]
    public void TheMakeActiveBoxStartsUnticked()
    {
        // Review round 3: it started ticked, so a White Mage who took the co-healer's LPDU share code to look at it still
        // switched from Carry the Clear to LPDU's "H1 WHM" calls (only the message said so).
        Assert.False(ConfigWindow.ShareCodeMakesActiveByDefault);

        var parts = SessionManagerTests.NewManager(new Configuration { DefaultSlot = "H1" });
        parts.Plans.EnsureLoaded();
        var code = PlanSerializer.ToShareCode(parts.Plans.Get(Lpdu)!.Plan);
        Assert.True(parts.Plans.ImportShareCode(code, Job.WHM, ConfigWindow.ShareCodeMakesActiveByDefault, out _, out var message), message);
        Assert.Equal(CarryTheClearWhm, parts.Plans.ActivePlanFor("top", Job.WHM)?.Id);
        Assert.Contains("choose this one in the table above to use it", message, StringComparison.Ordinal);
    }
}
