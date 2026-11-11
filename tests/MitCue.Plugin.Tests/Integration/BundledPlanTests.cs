using Dalamud.Plugin.Services;
using MitCue.Core;
using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Import;
using MitCue.Plans;
using MitCue.PluginTests.Support;
using MitCue.Runtime;

namespace MitCue.PluginTests.Integration;

/// <summary>
/// The three bundled TOP plans as the plugin loads them (embedded in MitCue.Core): which plan and column each player
/// gets without choosing, share codes of bundled plans, and the checks of plans that skip the review. Regression tests
/// for the final review of the integrated system.
/// </summary>
public class BundledPlanTests
{
    private const string CtcWhm = "335b6c4f-3684-55e0-a8d0-3e78724ef200";
    private const string CtcWhmSch = "ded79070-7479-5023-80ca-5e3ef257c99a";
    private const string Lpdu = "1f75e1e8-7321-566c-ae86-8568019fe914";

    private static SessionManagerTests.Parts Live(Job job, string? slot, Configuration? config = null)
    {
        config ??= new Configuration();
        config.DefaultSlot = slot;
        var parts = SessionManagerTests.NewManager(config);
        parts.Manager.Update(Frame(job, 1));
        return parts;
    }

    private static FrameContext Frame(Job job, double now, ulong party = 0)
    {
        var frame = SessionManagerTests.InTop(now);
        frame.Job = job;
        frame.ClassJobId = (uint)job;
        frame.PartyJobMask = party;
        return frame;
    }

    private static List<string> OwnEntryIds(SessionManagerTests.Parts parts) =>
        parts.Manager.Session!.Scheduler.All.Select(r => r.Entry.Id).Distinct().ToList();

    [Fact]
    public void TheThreePlansAreBundled()
    {
        var parts = SessionManagerTests.NewManager();
        parts.Plans.EnsureLoaded();
        (string, string)[] expected = [(CtcWhm, "Carry the Clear – WHM"), (CtcWhmSch, "Carry the Clear – WHM + SCH"), (Lpdu, "LPDU TOP Mitigation")];
        Assert.Equal(expected.Order(), parts.Plans.All.Select(p => (p.Id, p.DisplayName)).Order());
        Assert.All(parts.Plans.All, p => Assert.Equal(PlanOrigin.Bundled, p.Origin));
        Assert.Empty(parts.Errors.Snapshot());
    }

    [Theory]
    [InlineData(Job.WHM, "H1", CtcWhm, "White Mage", 57)]
    [InlineData(Job.WHM, null, CtcWhm, "White Mage", 57)]
    [InlineData(Job.SCH, "H2", Lpdu, null, 31)]
    [InlineData(Job.SCH, null, Lpdu, null, 31)]
    public void EachHealerGetsTheirSheetWithoutChoosing(Job job, string? slot, string planId, string? column, int entries)
    {
        // Final review finding: DESIGN D2 has the H2 Scholar follow LPDU, but the automatic choice (the first plan by name
        // with a column for the job) gave them "Carry the Clear – WHM + SCH", whatever slot they set.
        var parts = Live(job, slot);

        Assert.True(parts.Manager.PlanIsAutomatic);
        Assert.Equal(planId, parts.Manager.Plan?.Id);
        Assert.Equal(column, parts.Manager.Mapping?.AssigneeKey);
        Assert.Equal(entries, OwnEntryIds(parts).Count);
        Assert.StartsWith("Ready:", parts.Manager.Status, StringComparison.Ordinal);
        Assert.Empty(parts.Manager.Session!.Scheduler.Problems);
    }

    [Fact]
    public void ThePreviewChoosesTheSamePlan()
    {
        var config = new Configuration { DefaultSlot = "H2" };
        var parts = SessionManagerTests.NewManager(config);
        var preview = new PreviewDriver(config, parts.Data, Kit.GameData(), parts.Plans, parts.Errors, ServiceFake.Of<IChatGui>(), () => { });

        Assert.True(preview.Start("top", null, 1122, Job.SCH, 0, out var message), message);
        Assert.Contains("\"LPDU TOP Mitigation\" as SCH", message, StringComparison.Ordinal);
        Assert.Contains("31 entries are yours", message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCombinedCarryTheClearPlanCanStillBeChosen()
    {
        var config = new Configuration();
        config.ActivePlans[PlanStore.ActiveKey("top", Job.SCH)] = CtcWhmSch;
        var parts = Live(Job.SCH, "H2", config);

        Assert.False(parts.Manager.PlanIsAutomatic);
        Assert.Equal(CtcWhmSch, parts.Manager.Plan?.Id);
        Assert.Equal("Scholar", parts.Manager.Mapping?.AssigneeKey);
    }

    [Fact]
    public void AnImportedPlanReplacesTheBundledPlanOnlyWhenChosen()
    {
        // Docs review finding: an imported plan (or a share code) used to win the automatic choice over the bundled
        // plans, so saving one with "Make it my active plan" unticked, or taking the co-healer's share code to look at
        // it, silently switched the player's plan.
        var parts = SessionManagerTests.NewManager(new Configuration { DefaultSlot = "H2" });
        parts.Plans.EnsureLoaded();
        var own = new MitPlan { Id = "own", Name = "Aa my static's plan", Fight = "top", Entries = [Kit.Entry("p1.pantokrator", -3, Kit.SacredSoil, "Sacred Soil", Job.SCH)] };
        Assert.True(parts.Plans.Save(own, out var error), error);

        Assert.Equal(Lpdu, parts.Plans.ActivePlanFor("top", Job.SCH, out var automatic)?.Id);
        Assert.True(automatic);

        parts.Plans.SetActive("top", Job.SCH, "own");
        Assert.Equal("own", parts.Plans.ActivePlanFor("top", Job.SCH, out automatic)?.Id);
        Assert.False(automatic);
    }

    [Fact]
    public void WhereNoBundledPlanCoversTheJobTheImportedPlanIsTheAutomaticOne()
    {
        var parts = SessionManagerTests.NewManager();
        parts.Plans.EnsureLoaded();
        var tea = new MitPlan { Id = "tea-own", Name = "My TEA plan", Fight = "tea", Entries = [Kit.Entry("p1.pull", 0, Kit.SacredSoil, "Sacred Soil", Job.SCH)] };
        Assert.True(parts.Plans.Save(tea, out var error), error);

        Assert.Equal("tea-own", parts.Plans.ActivePlanFor("tea", Job.SCH, out var automatic)?.Id);
        Assert.True(automatic);
    }

    [Fact]
    public void AWhiteMageWhoImportsTheCoHealersLpduCodeKeepsCarryTheClearUnlessTheyAskForIt()
    {
        var parts = SessionManagerTests.NewManager(new Configuration { DefaultSlot = "H1" });
        parts.Plans.EnsureLoaded();
        var code = PlanSerializer.ToShareCode(parts.Plans.Get(Lpdu)!.Plan);

        Assert.True(parts.Plans.ImportShareCode(code, Job.WHM, makeActive: false, out var copy, out var message), message);
        Assert.Equal(Lpdu, copy!.Id);
        Assert.Equal(CtcWhm, parts.Plans.ActivePlanFor("top", Job.WHM, out var automatic)?.Id);
        Assert.True(automatic);
        Assert.Contains("Your active plan for TOP as WHM stays \"Carry the Clear – WHM\"", message);

        Assert.True(parts.Plans.ImportShareCode(code, Job.WHM, makeActive: true, out _, out message), message);
        Assert.Equal(Lpdu, parts.Plans.ActivePlanFor("top", Job.WHM, out automatic)?.Id);
        Assert.False(automatic);
        Assert.Contains("It is your active plan for TOP as WHM.", message);

        // The Scholar's default is LPDU by id, so the copy of it stays the Scholar's automatic plan.
        Assert.Equal(Lpdu, parts.Plans.ActivePlanFor("top", Job.SCH, out automatic)?.Id);
        Assert.True(automatic);
    }

    [Fact]
    public void AShareCodeWithoutCallsForTheCurrentJobSaysWhereToChooseIt()
    {
        var parts = SessionManagerTests.NewManager();
        parts.Plans.EnsureLoaded();
        var plan = new MitPlan { Id = "sch-only", Name = "SCH notes", Fight = "top", Entries = [Kit.Entry("p1.pantokrator", -3, Kit.SacredSoil, "Sacred Soil", Job.SCH)] };

        Assert.True(parts.Plans.ImportShareCode(PlanSerializer.ToShareCode(plan), Job.WHM, makeActive: true, out _, out var message), message);
        Assert.Contains("It has no calls for WHM", message);
        Assert.Equal(CtcWhm, parts.Plans.ActivePlanFor("top", Job.WHM)?.Id);
        Assert.Equal(Lpdu, parts.Plans.ActivePlanFor("top", Job.SCH)?.Id);
    }

    [Fact]
    public void EveryRecommendedPlanIsABundledPlanWithAColumnForItsJob()
    {
        var parts = SessionManagerTests.NewManager();
        parts.Plans.EnsureLoaded();
        foreach (var (key, id) in PlanStore.BundledDefaults)
        {
            var stored = parts.Plans.Get(id);
            Assert.True(stored != null, $"{key}: no bundled plan {id}");
            var fight = key[..key.IndexOf('/')];
            var job = Enum.Parse<Job>(key[(key.IndexOf('/') + 1)..]);
            Assert.Equal(fight, stored.Plan.Fight);
            Assert.Equal(PlanOrigin.Bundled, stored.Origin);
            Assert.Contains(stored.Plan.Assignees, a => a.Job == job);
        }
    }

    [Theory]
    [InlineData(Job.SCH, "H2", new[] { "e024", "e078" })]
    [InlineData(Job.WHM, "H1", new[] { "e023", "e077" })]
    public void OnLpduTheSuggestedColumnKeepsTheSlotsJoblessCalls(Job job, string slot, string[] jobless)
    {
        // Final review finding: the bundled LPDU plan has no saved column choice, and the suggestion (the job's column,
        // "H2 SCH") left out the job-less "H2" calls ("Everything" on Pantokrator and Limitless Synergy) that an in-game
        // import of the same sheet saves a column choice for. The CLI's simulate (no --assignee) kept them.
        var config = new Configuration { DefaultSlot = slot };
        config.ActivePlans[PlanStore.ActiveKey("top", job)] = Lpdu;
        var parts = Live(job, slot, config);
        var plan = parts.Manager.Plan!.Plan;

        var mapping = parts.Manager.Mapping!;
        Assert.True(mapping.IsSuggestion);
        Assert.Null(mapping.AssigneeKey);
        Assert.Equal(slot, mapping.Slot);
        var own = OwnEntryIds(parts);
        Assert.All(jobless, id => Assert.Contains(id, own));

        // Every entry of the player's slot meant for their job or for no job: the import's choice selects the same.
        var expected = plan.Entries
            .Where(e => (e.Who.Slot == slot || e.Who.Assignee?.StartsWith(slot, StringComparison.Ordinal) == true) && (e.Who.Job == null || e.Who.Job == job))
            .Select(e => e.Id)
            .ToList();
        Assert.Equal(expected.Order(), own.Order());
        var choice = ImportColumns.Choose(plan, job, 90, slot);
        Assert.Equal((choice.Mapping.AssigneeKey, choice.Mapping.Slot), (mapping.AssigneeKey, mapping.Slot));
        Assert.Equal(own.Count, choice.Mine);
    }

    [Theory]
    [InlineData(Job.WAR, "MT", "WAR/DRK WAR", 24)]
    [InlineData(Job.WAR, "OT", "WAR/DRK WAR", 23)]
    [InlineData(Job.WAR, null, "WAR/DRK WAR", 9)]
    [InlineData(Job.DRK, "MT", "WAR/DRK DRK", 23)]
    [InlineData(Job.DRK, "OT", "WAR/DRK DRK", 22)]
    [InlineData(Job.DRK, null, "WAR/DRK DRK", 8)]
    public void OnLpduATankGetsOneTankPairColumn(Job job, string? slot, string column, int entries)
    {
        // Review finding (41d2893): the suggestion for a tank became "any column for my job", which has LPDU's job-less MT
        // calls but also every tank pair's column of the job (WAR/DRK, WAR/GNB and WAR/PLD WAR) and both Cosmo Memory
        // roles; without a slot even the MT and the OT calls. The job's column (one pair) stays the suggestion.
        var parts = Live(job, slot);

        Assert.True(parts.Manager.PlanIsAutomatic);
        Assert.Equal(Lpdu, parts.Manager.Plan?.Id);
        Assert.Equal(column, parts.Manager.Mapping?.AssigneeKey);
        Assert.True(parts.Manager.Mapping!.IsSuggestion);
        var own = parts.Manager.Session!.Scheduler.All.Select(r => r.Entry).Distinct().ToList();
        Assert.Equal(entries, own.Count);
        Assert.All(own, e => Assert.True(
            e.Who.Assignee == null || e.Who.Assignee == column || (slot != null && e.Who.Assignee == slot && e.Who.Job == job),
            $"{e.Id} {e.Who.Assignee} {e.Who.Slot}/{e.Who.Job}"));
    }

    [Theory]
    [InlineData("MT")]
    [InlineData(null)]
    public void OnLpduAWarriorsHolmgangAtSolarRay3IsCalledOnceAndDoneWhenPressed(string? slot)
    {
        // The reviewer's probe: three Holmgang reminders at Solar Ray 3 (one per tank pair); one press satisfies one
        // reminder, so two always ended Missed, and Land Waker (LB3) was called for both Cosmo Memory roles.
        const uint holmgang = 43;
        const uint landWaker = 4240;
        var parts = Live(Job.WAR, slot);
        var scheduler = parts.Manager.Session!.Scheduler;

        var reminder = Assert.Single(scheduler.All, r => r.Mechanic.Id == "p5.solar_ray_3" && r.Item.ActionIds().Contains(holmgang));
        Assert.DoesNotContain(scheduler.All, r => r.Item.ActionIds().Contains(landWaker));
        scheduler.Update(reminder.PressAt - 20);
        scheduler.Update(reminder.PressAt);
        scheduler.OnOwnAction(holmgang, reminder.PressAt);
        scheduler.Update(reminder.Latest + 1);
        Assert.Equal(MitCue.Core.Reminders.ReminderState.Done, reminder.State);
    }

    [Fact]
    public void OnLpduTheScholarsCoHealerCallsAreTheOtherHealerSlotsAndInAPartyTheWhiteMages()
    {
        // Final review finding: the dimmed "co-healer" calls were every other healer column of LPDU (60 entries): the H2
        // Sage column, the player's own job-less H2 notes, and both the H1 White Mage and H1 Astrologian columns.
        var config = new Configuration { ShowOtherAssignees = true };
        var parts = Live(Job.SCH, "H2", config);
        var others = parts.Manager.Session!.Others!.All.Select(r => r.Entry).Distinct().ToList();
        Assert.All(others, e => Assert.Equal("H1", e.Who.Slot));
        Assert.Contains(others, e => e.Who.Job == Job.AST); // the party is not known: every H1 column

        // In a party with a White Mage the session is rebuilt (no pull is running) with that co-healer's calls only.
        var generation = parts.Manager.Generation;
        parts.Manager.Update(Frame(Job.SCH, 2, PartyJobs.Mask([Job.WAR, Job.DRK, Job.WHM, Job.SCH, Job.SAM, Job.RPR, Job.DNC, Job.PCT])));
        Assert.True(parts.Manager.Generation > generation);
        others = parts.Manager.Session!.Others!.All.Select(r => r.Entry).Distinct().ToList();
        Assert.All(others, e => Assert.True(e.Who.Slot == "H1" && e.Who.Job is null or Job.WHM, $"{e.Id} {e.Who.Slot}/{e.Who.Job}/{e.Who.Assignee}"));
        Assert.Equal(11, others.Count);
    }

    [Theory]
    [InlineData(Job.WAR, "MT", Job.DRK, "WAR/DRK DRK")]
    [InlineData(Job.DRK, "OT", Job.WAR, "WAR/DRK WAR")]
    public void OnLpduATanksCoTankCallsAreThePairsColumn(Job job, string slot, Job coTank, string pairColumn)
    {
        // Review finding (41d2893): with a WAR/DRK party the co-tank rows held the Dark Knight's column of every tank pair
        // (e251 DRK/GNB DRK and e252 DRK/PLD DRK: Living Dead at Solar Ray 3, while this pair's is at Solar Ray 2).
        var config = new Configuration { ShowOtherAssignees = true, DefaultSlot = slot };
        var parts = SessionManagerTests.NewManager(config);
        parts.Manager.Update(Frame(job, 1, PartyJobs.Mask([Job.WAR, Job.DRK, Job.WHM, Job.SCH, Job.SAM, Job.RPR, Job.DNC, Job.PCT])));

        var others = parts.Manager.Session!.Others!.All.Select(r => r.Entry).Distinct().ToList();
        Assert.All(others, e => Assert.True(e.Who.Job == null || e.Who.Job == coTank, $"{e.Id} {e.Who.Assignee} {e.Who.Job}"));
        Assert.Equal(job == Job.WAR ? 41 : 49, others.Count); // 57 and 65 before the fix
        var columns = others.Select(e => e.Who.Assignee).Distinct().ToList();
        Assert.Contains(pairColumn, columns);
        Assert.DoesNotContain(columns, c => c != null && c.Contains('/', StringComparison.Ordinal) && c != pairColumn);
        Assert.Contains(slot == "MT" ? "OT" : "MT", columns); // the co-tank's slot column
    }

    [Fact]
    public void APartyChangeRebuildsNothingWhileTheCoHealersCallsAreHidden()
    {
        var parts = Live(Job.SCH, "H2");
        var generation = parts.Manager.Generation;

        parts.Manager.Update(Frame(Job.SCH, 2, PartyJobs.Mask([Job.WHM, Job.SCH])));

        Assert.Equal(generation, parts.Manager.Generation);
        Assert.Null(parts.Manager.Session!.Others);
    }

    [Theory]
    [InlineData(CtcWhm)]
    [InlineData(CtcWhmSch)]
    [InlineData(Lpdu)]
    public void TheShareCodeOfABundledPlanReplacesItInTheList(string id)
    {
        // Final review finding: the imported copy and the bundled plan were both listed with the same id (and the same
        // ImGui ids) until "Reload plans".
        var parts = SessionManagerTests.NewManager();
        parts.Plans.EnsureLoaded();
        var code = parts.Plans.ExportShareCode(parts.Plans.Get(id)!.Plan, out var error);
        Assert.True(code != null, error);

        Assert.True(parts.Plans.ImportShareCode(code, out _, out var message), message);

        var listed = Assert.Single(parts.Plans.All, p => p.Id == id);
        Assert.Equal(PlanOrigin.User, listed.Origin);
        Assert.Equal(parts.Plans.All.Select(p => p.Id).Distinct().Count(), parts.Plans.All.Count);
        Assert.Equal(PlanOrigin.User, parts.Plans.All[0].Origin); // user plans first, as after a reload
        parts.Plans.Reload();
        Assert.Equal(PlanOrigin.User, Assert.Single(parts.Plans.All, p => p.Id == id).Origin);
    }

    [Fact]
    public void DeletingTheCopyOfABundledPlanKeepsItsSettings()
    {
        // Final review finding: deleting the copy also forgot the active-plan choice and column saved under the id,
        // although the bundled plan with that id came back.
        var config = new Configuration();
        var parts = SessionManagerTests.NewManager(config);
        parts.Plans.EnsureLoaded();
        var code = parts.Plans.ExportShareCode(parts.Plans.Get(Lpdu)!.Plan, out _)!;
        Assert.True(parts.Plans.ImportShareCode(code, out var copy, out _));
        parts.Plans.SetActive("top", Job.AST, Lpdu);
        parts.Plans.SetMapping(copy!, Job.AST, new PlayerMapping { AssigneeKey = "H1 AST", Slot = "H1" });

        Assert.True(parts.Plans.Delete(Lpdu, out var error), error);

        Assert.Equal(PlanOrigin.Bundled, Assert.Single(parts.Plans.All, p => p.Id == Lpdu).Origin);
        Assert.Equal(Lpdu, parts.Plans.ActivePlanFor("top", Job.AST, out var automatic)?.Id);
        Assert.False(automatic);
        Assert.Equal("H1 AST", parts.Plans.MappingFor(parts.Plans.Get(Lpdu)!.Plan, Job.AST).AssigneeKey);
    }

    [Fact]
    public void DeletingAnImportedPlanForgetsItsSettings()
    {
        var parts = SessionManagerTests.NewManager();
        parts.Plans.EnsureLoaded();
        var own = new MitPlan { Id = "own", Name = "Mine", Fight = "top", Entries = [Kit.Entry("p1.pantokrator", -3, Kit.Temperance, "Temperance")] };
        Assert.True(parts.Plans.Save(own, out _));
        parts.Plans.SetActive("top", Job.WHM, "own");
        parts.Plans.SetMapping(own, Job.WHM, new PlayerMapping { AssigneeKey = "x" });

        Assert.True(parts.Plans.Delete("own", out _));

        Assert.Null(parts.Plans.Get("own"));
        Assert.Equal(CtcWhm, parts.Plans.ActivePlanFor("top", Job.WHM, out var automatic)?.Id);
        Assert.True(automatic);
        Assert.Null(parts.Plans.SavedMapping(own, Job.WHM));
    }
}

/// <summary>Share codes and plan files skip the import window's review: the store checks them (DESIGN §1.3).</summary>
public class PlanCheckTests
{
    private static SessionManagerTests.Parts Checked()
    {
        var parts = SessionManagerTests.NewManager();
        parts.Data.EnsureLoaded();
        parts.Plans.Check = plan => PlanChecks.Find(plan, parts.Data);
        parts.Plans.EnsureLoaded();
        return parts;
    }

    /// <summary>A TEA (level 80) plan that asks a Scholar for Expedient (learned at 90), and an entry for an unknown mechanic.</summary>
    private static MitPlan TeaProbe() => new()
    {
        Id = "tea-probe",
        Name = "TEA probe",
        Fight = "tea",
        Entries =
        [
            new PlanEntry { Id = "e1", Mechanic = "p1.pull", Offset = 5, Who = new WhoSpec { Job = Job.SCH }, Do = [PlanItem.ForAction(25868, "Expedient")] },
            new PlanEntry { Id = "e2", Mechanic = "p9.nothing", Offset = 5, Who = new WhoSpec { Job = Job.SCH }, Do = [PlanItem.ForAction(25868, "Expedient")] },
        ],
    };

    [Fact]
    public void AShareCodeWithProblemsIsImportedAndTheProblemsAreShown()
    {
        // Final review finding: share codes were accepted without any check; the Expedient call could only end Missed.
        var parts = Checked();

        Assert.True(parts.Plans.ImportShareCode(PlanSerializer.ToShareCode(TeaProbe()), out _, out var message), message);

        Assert.Contains("Imported \"TEA probe\" (2 entries, fight tea).", message, StringComparison.Ordinal);
        Assert.Contains("problems:", message, StringComparison.Ordinal);
        Assert.Contains("Problems in /mitcue", message, StringComparison.Ordinal);
        var problem = Assert.Single(parts.Errors.Snapshot(), p => p.Key == "plans.load.check.tea-probe");
        Assert.Contains("Entry 'e1': Expedient is learned at level 90, above this fight's level 80, so the call can never be done", problem.Message, StringComparison.Ordinal);
        Assert.Contains("Entry 'e2': TEA has no mechanic \"p9.nothing\", so the call is never shown", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A plan as the AI route writes it and the review accepts it: an action name in other letter case, an either/or of
    /// which the Scholar has one option, and a hit index past the mechanic's last hit (the nearest hit is used).
    /// </summary>
    private static MitPlan ReviewedPlan() => new()
    {
        Id = "ai-plan",
        Name = "AI plan",
        Fight = "top",
        Source = new PlanSource { Kind = "llm" },
        Assignees = [new Assignee { Key = "Scholar", Job = Job.SCH, Slot = "H2" }],
        Entries =
        [
            new PlanEntry { Id = "e1", Mechanic = "p1.loop_set1", Offset = -3, Who = new WhoSpec { Job = Job.SCH, Slot = "H2", Assignee = "Scholar" }, Do = [PlanItem.ForAction(Kit.SacredSoil, "Sacred soil")] },
            new PlanEntry { Id = "e2", Mechanic = "p1.pantokrator", Offset = -3, Who = new WhoSpec { Job = Job.SCH, Slot = "H2", Assignee = "Scholar" }, Do = [PlanItem.AnyOf([PlanItem.ForAction(Kit.SacredSoil, "Sacred Soil"), PlanItem.ForAction(24298, "Kerachole")])] },
            new PlanEntry { Id = "e3", Mechanic = "p1.pantokrator", HitIndex = 9, Offset = 1, Who = new WhoSpec { Job = Job.SCH, Slot = "H2", Assignee = "Scholar" }, Do = [PlanItem.ForAction(16538, "Fey Illumination")] },
        ],
    };

    [Fact]
    public void WhatTheReviewAcceptsIsNotAProblem()
    {
        // Review finding (41d2893): the checks were the core PlanValidator's, stricter than the review where the runtime
        // does not mind (names compared exactly, every either/or option for the job, hit indexes), so a reviewed plan was
        // listed in Problems after every restart and its share code said its calls could never be done, though they work.
        var parts = Checked();
        var plan = ReviewedPlan();
        var top = parts.Data.GetFight("top")!;
        // The review has no error for them (the hit index is a warning there: "the nearest one is used").
        Assert.All(plan.Entries, e => Assert.DoesNotContain(ReviewValidator.CheckEntry(e, plan, top, parts.Data.Actions, true), i => i.Severity >= DiagnosticSeverity.Error));

        Assert.Empty(PlanChecks.Find(plan, parts.Data));
        Assert.True(parts.Plans.ImportShareCode(PlanSerializer.ToShareCode(plan), out _, out var message), message);
        Assert.DoesNotContain("problem", message, StringComparison.Ordinal);
        parts.Plans.Reload();
        Assert.DoesNotContain(parts.Errors.Snapshot(), p => p.Key.StartsWith("plans.load.check.", StringComparison.Ordinal));
    }

    [Fact]
    public void SlotSynonymsAreStoredAsTheStandardSlots()
    {
        // The plan checks follow the review, which reads "D3" as R1; the runtime compares slots by name, so the store
        // writes the standard name: the R1 player sees the call with any column choice, not only the suggested one.
        var parts = Checked();
        var plan = new MitPlan
        {
            Id = "d3-plan",
            Name = "D3 plan",
            Fight = "top",
            Assignees = [new Assignee { Key = "D3", Slot = "D3" }],
            Entries = [new PlanEntry { Id = "e1", Mechanic = "p1.loop_set3", Offset = -3, Who = new WhoSpec { Slot = "D3", Assignee = "D3" }, Do = [PlanItem.ForAction(7560, "Addle")] }],
        };

        Assert.True(parts.Plans.ImportShareCode(PlanSerializer.ToShareCode(plan), out var imported, out var message), message);

        Assert.DoesNotContain("problem", message, StringComparison.Ordinal);
        Assert.Equal("R1", imported!.Assignees[0].Slot);
        Assert.Equal("R1", imported.Entries[0].Who.Slot);
        parts.Plans.SetMapping(imported, Job.BLM, new PlayerMapping { AssigneeKey = "D3", Slot = "R1" }); // as /mitcue > Plans saves it
        var mapping = parts.Plans.MappingFor(imported, Job.BLM);
        Assert.Single(PlanSelection.For(imported, new PlayerContext(Job.BLM, 90, mapping.Slot, mapping.AssigneeKey)));
    }

    [Fact]
    public void AnEitherOrWithNoOptionForTheJobIsAProblem()
    {
        var parts = Checked();
        var plan = ReviewedPlan();
        plan.Entries[1].Do = [PlanItem.AnyOf([PlanItem.ForAction(24298, "Kerachole"), PlanItem.ForAction(24310, "Holos")])];

        var problem = Assert.Single(PlanChecks.Find(plan, parts.Data));
        Assert.Equal("Entry 'e2': None of Kerachole / Holos is a SCH action, so the call can never be done.", problem);
    }

    [Fact]
    public void APlanFileWithProblemsIsListedInProblemsUntilItIsDeleted()
    {
        var parts = Checked();
        Assert.True(parts.Plans.Save(TeaProbe(), out var error), error);
        parts.Plans.Reload();
        Assert.Contains(parts.Errors.Snapshot(), p => p.Key == "plans.load.check.tea-probe");

        Assert.True(parts.Plans.Delete("tea-probe", out error), error);
        Assert.DoesNotContain(parts.Errors.Snapshot(), p => p.Key.StartsWith("plans.load.check.", StringComparison.Ordinal));
    }

    [Fact]
    public void TheBundledPlansAndTheirShareCodesHaveNoProblems()
    {
        var parts = Checked();
        foreach (var stored in parts.Plans.All.ToList())
            Assert.Empty(PlanChecks.Find(stored.Plan, parts.Data));

        var code = parts.Plans.ExportShareCode(parts.Plans.All[0].Plan, out _)!;
        Assert.True(parts.Plans.ImportShareCode(code, out _, out var message));
        Assert.DoesNotContain("problem", message, StringComparison.Ordinal);
        Assert.Empty(parts.Errors.Snapshot());
    }

    [Fact]
    public void APlanForAnUnknownFightSaysSo()
    {
        var parts = Checked();
        var plan = TeaProbe();
        plan.Fight = "xyz";
        var problem = Assert.Single(PlanChecks.Find(plan, parts.Data));
        Assert.Contains("no fight \"xyz\"", problem, StringComparison.Ordinal);
    }
}
