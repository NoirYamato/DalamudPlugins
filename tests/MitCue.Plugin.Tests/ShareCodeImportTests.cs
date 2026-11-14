using MitCue.Core;
using MitCue.Core.Plans;
using MitCue.Import;
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
    private const string LpduTea = "a5c2ab3b-a8c7-53e7-8a49-74e85a0777d7";

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
    public void WithoutASlotARoleSheetSaysWhichSlotToSetAndToChooseIt()
    {
        // Review round 4: the message said to set "My usual party slot" or choose the plan, but setting the slot never made
        // the plan active (only plans with a column for the job are chosen automatically): both steps are needed.
        var config = new Configuration();
        var parts = SessionManagerTests.NewManager(config);
        parts.Plans.EnsureLoaded();

        Assert.True(parts.Plans.ImportShareCode(PlanSerializer.ToShareCode(RoleSheet()), Job.WHM, makeActive: true, out _, out var message), message);

        Assert.Contains("It has calls for WHM only in slot H1 or H2: set \"My usual party slot\" in the General tab, then choose it in the table above to use it.", message, StringComparison.Ordinal);
        Assert.Equal(CarryTheClearWhm, parts.Plans.ActivePlanFor("top", Job.WHM)?.Id);

        // Both steps: the role sheet's H1 call is the White Mage's.
        config.DefaultSlot = "H1";
        parts.Plans.SetActive("top", Job.WHM, "role-sheet");
        Assert.Equal(["e1"], OwnCalls(parts, Job.WHM));
    }

    [Fact]
    public void WithAUsualSlotOfAnotherRoleARoleSheetSaysToChooseTheSlotsColumnForTheJob()
    {
        // Review round 5: a tank main (usual slot MT) was told to set "My usual party slot" for a healer role sheet on their
        // White Mage, which took the Warrior's slot calls on LPDU away (35 entries to 9). The slot's column under "My
        // column" is the White Mage's seat without changing the usual slot.
        var config = new Configuration { DefaultSlot = "MT" };
        var parts = SessionManagerTests.NewManager(config);
        parts.Plans.EnsureLoaded();
        Assert.Equal(35, parts.Plans.CallsFor(parts.Plans.Get(Lpdu)!.Plan, Job.WAR));

        Assert.True(parts.Plans.ImportShareCode(PlanSerializer.ToShareCode(RoleSheet()), Job.WHM, makeActive: true, out var copy, out var message), message);

        Assert.Contains("It has calls for WHM only in slot H1 or H2: choose it in the table above for WHM, then its column \"H1\" or \"H2\" under \"My column\".", message, StringComparison.Ordinal);
        Assert.DoesNotContain("My usual party slot", message, StringComparison.Ordinal);

        // That way: the role sheet's H1 call is the White Mage's, and the Warrior keeps its MT calls.
        parts.Plans.SetActive("top", Job.WHM, "role-sheet");
        parts.Plans.SetMapping(copy!, Job.WHM, new PlayerMapping { AssigneeKey = "H1", Slot = "H1" }); // as /mitcue > Plans saves it
        Assert.Equal(["e1"], OwnCalls(parts, Job.WHM));
        Assert.Equal("MT", config.DefaultSlot);
        Assert.Equal(35, parts.Plans.CallsFor(parts.Plans.Get(Lpdu)!.Plan, Job.WAR));
    }

    private static List<string> OwnCalls(SessionManagerTests.Parts parts, Job job)
    {
        var frame = SessionManagerTests.InTop(1);
        frame.Job = job;
        frame.ClassJobId = (uint)job;
        parts.Manager.Update(frame);
        return parts.Manager.Session!.Scheduler.All.Select(r => r.Entry.Id).ToList();
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

    /// <summary>LPDU TEA as another plan of the fight (a static's copy: same calls, another id), as a share code.</summary>
    private static string StaticTeaCode(SessionManagerTests.Parts parts)
    {
        var copy = PlanSerializer.FromShareCode(PlanSerializer.ToShareCode(parts.Plans.Get(LpduTea)!.Plan));
        copy.Id = "static-tea";
        copy.Name = "Static TEA";
        return PlanSerializer.ToShareCode(copy);
    }

    [Theory]
    [InlineData(null, "It has calls for NIN only in slot M1 or M2: set \"My usual party slot\" in the General tab, then choose it in the table above to use it.")]
    [InlineData("MT", "It has calls for NIN only in slot M1 or M2: choose it in the table above for NIN, then its column \"Melee 1\" or \"Melee 2\" under \"My column\".")]
    public void ANoteForEveryoneIsNoCallOfTheJob(string? slot, string guidance)
    {
        // Review finding (C12): 'Alex Prime/PA'!M43 "Use Personals" under "Extras" is every player's. A Ninja without a
        // melee slot was told the TEA plan was their active plan and got only that note, not the guidance to the
        // "Melee 1" and "Melee 2" Feints; the import window's Save step showed it in green and ticked "Make it my active plan".
        var parts = SessionManagerTests.NewManager(new Configuration { DefaultSlot = slot });
        parts.Plans.EnsureLoaded();

        Assert.True(parts.Plans.ImportShareCode(StaticTeaCode(parts), Job.NIN, makeActive: true, out var imported, out var message), message);

        Assert.Equal(0, parts.Plans.CallsFor(imported!, Job.NIN));
        Assert.Contains(guidance, message, StringComparison.Ordinal);
        Assert.Null(parts.Plans.ActivePlanFor("tea", Job.NIN));
        var notes = imported!.Entries.Count(PlanSelection.ForEveryone);
        Assert.Contains(imported.Entries, e => e.Src == "'Alex Prime/PA'!M43" && PlanSelection.ForEveryone(e));
        var choice = ImportColumns.Choose(imported, Job.NIN, 80, slot);
        Assert.Equal((notes, 0), (choice.Mine, choice.Seat)); // the overlay shows the notes to a player who chooses the plan
        Assert.True(ImportWindow.AppliesLine(choice, Job.NIN).Warning);
    }

    [Theory]
    [InlineData(1, 0, "Only 1 note for everyone applies to you as NIN: check the columns you imported.", true)]
    [InlineData(2, 0, "Only 2 notes for everyone apply to you as NIN: check the columns you imported.", true)]
    [InlineData(0, 0, "0 entries apply to you as NIN: check the columns you imported.", true)]
    [InlineData(8, 7, "8 entries apply to you as NIN.", false)]
    [InlineData(1, 1, "1 entry applies to you as NIN.", false)]
    public void TheSaveStepWarnsWhenOnlyNotesForEveryoneApply(int mine, int seat, string text, bool warning) =>
        Assert.Equal((text, warning), ImportWindow.AppliesLine(new ColumnChoice(new PlayerMapping(), false, false, mine, seat, 0, [], []), Job.NIN));

    [Fact]
    public void WithItsSlotAMeleeGetsTheTeaPlanAndTheNoteToo()
    {
        var parts = SessionManagerTests.NewManager(new Configuration { DefaultSlot = "M1" });
        parts.Plans.EnsureLoaded();

        Assert.True(parts.Plans.ImportShareCode(StaticTeaCode(parts), Job.NIN, makeActive: true, out var imported, out var message), message);

        var feints = imported!.Entries.Count(e => e.Who.Assignee == "Melee 1" && e.Who.Slot == "M1" && e.Who.Job is null); // the seat's calls
        var notes = imported.Entries.Count(PlanSelection.ForEveryone);
        Assert.True(feints > 0 && notes > 0);
        Assert.Equal(feints, parts.Plans.CallsFor(imported, Job.NIN));
        Assert.Contains("It is your active plan for TEA as NIN.", message, StringComparison.Ordinal);
        var choice = ImportColumns.Choose(imported, Job.NIN, 80, "M1");
        Assert.Equal((feints + notes, feints), (choice.Mine, choice.Seat));
        Assert.False(ImportWindow.AppliesLine(choice, Job.NIN).Warning);
    }

    [Fact]
    public void ANoteForEveryoneGivesNoSlotOfARoleWithoutCalls()
    {
        // Review finding (C12): with only the note counted, a White Mage was told to set their usual slot for a tanks' plan.
        var tanks = new MitPlan
        {
            Id = "tanks-and-note",
            Name = "Tanks and a note",
            Fight = "top",
            Assignees = [new Assignee { Key = "MT", Slot = "MT" }, new Assignee { Key = "OT", Slot = "OT" }],
            Entries =
            [
                Kit.Entry("p1.pantokrator", -3, Kit.Reprisal, "Reprisal", job: null, slot: "MT", assignee: "MT"),
                new PlanEntry { Id = "everyone", Mechanic = "p1.pantokrator", Offset = -3, Who = new WhoSpec(), Do = [PlanItem.ForNote("Use Personals")] },
            ],
        };
        var parts = SessionManagerTests.NewManager();
        parts.Plans.EnsureLoaded();

        Assert.True(parts.Plans.ImportShareCode(PlanSerializer.ToShareCode(tanks), Job.WHM, makeActive: true, out var imported, out var message), message);

        Assert.Contains("It has no calls for WHM: choose it in the table above for the job it is for.", message, StringComparison.Ordinal);
        Assert.Equal(CarryTheClearWhm, parts.Plans.ActivePlanFor("top", Job.WHM)?.Id);
        var choice = ImportColumns.Choose(imported!, Job.WHM, 90, null);
        Assert.Equal((1, 0), (choice.Mine, choice.Seat));
        Assert.Equal(("Only 1 note for everyone applies to you as WHM: check the columns you imported.", true), ImportWindow.AppliesLine(choice, Job.WHM));
        Assert.Equal(("0 entries apply to you as WHM: check the columns you imported.", true), ImportWindow.AppliesLine(choice with { Mine = 0 }, Job.WHM));
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
