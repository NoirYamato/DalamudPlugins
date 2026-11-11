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

    [Theory]
    [InlineData(null)]
    [InlineData("MT")] // a slot of another role is not the White Mage's slot
    public void WithoutASlotARoleSheetSaysWhichSlotToSetAndToChooseIt(string? defaultSlot)
    {
        // Review round 4: the message said to set "My usual party slot" or choose the plan, but setting the slot never made
        // the plan active (only plans with a column for the job are chosen automatically): both steps are needed.
        var config = new Configuration { DefaultSlot = defaultSlot };
        var parts = SessionManagerTests.NewManager(config);
        parts.Plans.EnsureLoaded();

        Assert.True(parts.Plans.ImportShareCode(PlanSerializer.ToShareCode(RoleSheet()), Job.WHM, makeActive: true, out _, out var message), message);

        Assert.Contains("It has calls for WHM only in slot H1 or H2: set \"My usual party slot\" in the General tab, then choose it in the table above to use it.", message, StringComparison.Ordinal);
        Assert.Equal(CarryTheClearWhm, parts.Plans.ActivePlanFor("top", Job.WHM)?.Id);

        // Both steps: the role sheet's H1 call is the White Mage's.
        config.DefaultSlot = "H1";
        parts.Plans.SetActive("top", Job.WHM, "role-sheet");
        var frame = SessionManagerTests.InTop(1);
        frame.Job = Job.WHM;
        frame.ClassJobId = (uint)Job.WHM;
        parts.Manager.Update(frame);
        Assert.Equal("role-sheet", parts.Manager.Plan?.Id);
        Assert.Equal(["e1"], parts.Manager.Session!.Scheduler.All.Select(r => r.Entry.Id));
    }

    [Fact]
    public void ARoleSheetWithoutCallsForTheJobsRoleSaysNothingAboutTheSlot()
    {
        // Review round 4: a White Mage without a slot was told to set one for a sheet with tank calls only.
        var tanks = new MitPlan
        {
            Id = "tanks-only",
            Name = "Tanks only",
            Fight = "top",
            Assignees = [new Assignee { Key = "MT", Slot = "MT" }, new Assignee { Key = "OT", Slot = "OT" }],
            Entries =
            [
                Kit.Entry("p1.pantokrator", -3, Kit.Reprisal, "Reprisal", job: null, slot: "MT", assignee: "MT"),
                Kit.Entry("p1.pantokrator", -3, Kit.Reprisal, "Reprisal", job: null, slot: "OT", assignee: "OT"),
            ],
        };
        var parts = SessionManagerTests.NewManager();
        parts.Plans.EnsureLoaded();

        Assert.True(parts.Plans.ImportShareCode(PlanSerializer.ToShareCode(tanks), Job.WHM, makeActive: true, out _, out var message), message);

        Assert.Contains("It has no calls for WHM: choose it in the table above for the job it is for.", message, StringComparison.Ordinal);
        Assert.DoesNotContain("My usual party slot", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnotherJobsCallInTheColumnOfTheSameSlotIsNotTheirs()
    {
        // Review round 4: a call of "H1 AST" that names no job (an AI answer, a hand-edited plan) was shown to the White
        // Mage who follows "H1 WHM" in H1, as a call of their slot; the review and the plan checks read it as the
        // Astrologian's, and it is now read so everywhere.
        var plan = new MitPlan
        {
            Id = "healer-columns",
            Name = "Healer columns",
            Fight = "top",
            Assignees = [new Assignee { Key = "H1 WHM", Slot = "H1", Job = Job.WHM }, new Assignee { Key = "H1 AST", Slot = "H1", Job = Job.AST }],
            Entries =
            [
                Kit.Entry("p1.loop_set1", -3, Kit.Temperance, "Temperance", Job.WHM, slot: "H1", assignee: "H1 WHM"),
                Kit.Entry("p1.loop_set1", -3, 3613, "Collective Unconscious", job: null, slot: "H1", assignee: "H1 AST"),
            ],
        };
        var parts = SessionManagerTests.NewManager(new Configuration { DefaultSlot = "H1" });
        parts.Plans.EnsureLoaded();

        Assert.True(parts.Plans.ImportShareCode(PlanSerializer.ToShareCode(plan), Job.WHM, makeActive: true, out var copy, out var message), message);

        Assert.Equal(Job.AST, copy!.Entries[1].Who.Job);
        Assert.Equal(1, parts.Plans.CallsFor(copy, Job.WHM));
        var frame = SessionManagerTests.InTop(1);
        frame.Job = Job.WHM;
        frame.ClassJobId = (uint)Job.WHM;
        parts.Manager.Update(frame);
        Assert.Equal("H1 WHM", parts.Manager.Mapping?.AssigneeKey);
        Assert.Equal(["Temperance"], parts.Manager.Session!.Scheduler.All.Select(r => r.Item.Name));
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
