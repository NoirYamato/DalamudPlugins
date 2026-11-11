using MitCue.Core;
using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Import;
using static MitCue.PluginTests.TestData;

namespace MitCue.PluginTests;

public class ReviewValidatorTests
{
    private static List<ReviewIssue> Check(PlanEntry entry, MitPlan? plan = null, bool actionsLoaded = true)
    {
        plan ??= new MitPlan { Fight = "top", Entries = [entry] };
        return ReviewValidator.CheckEntry(entry, plan, Top(), Catalog(), actionsLoaded);
    }

    [Fact]
    public void AValidEntryHasNoIssues()
    {
        var entry = Entry("e1", "p1.pantokrator", Job.WHM, Action(Temperance, "Temperance"));
        entry.Offset = -3;
        Assert.Empty(Check(entry));
    }

    [Fact]
    public void FlagsUnknownMechanics()
    {
        var issue = Assert.Single(Check(Entry("e1", "p1.not_a_mechanic", Job.WHM, Action(Temperance, "Temperance"))));
        Assert.Equal("unknown-mechanic", issue.Code);
        Assert.Equal(DiagnosticSeverity.Error, issue.Severity);
        Assert.Contains("p1.not_a_mechanic", issue.Message);

        Assert.Contains(Check(Entry("e2", string.Empty, Job.WHM, Action(Temperance, "Temperance"))), i => i.Code == "unknown-mechanic");
    }

    [Fact]
    public void FlagsUnknownActions()
    {
        var issue = Assert.Single(Check(Entry("e1", "p1.pantokrator", Job.WHM, Action(999999, "Made Up"))));
        Assert.Equal("unknown-action", issue.Code);
        Assert.Equal(DiagnosticSeverity.Error, issue.Severity);
        Assert.Contains("999999", issue.Message);
        Assert.Contains("Made Up", issue.Message);
    }

    [Fact]
    public void FlagsActionsTheJobCannotUse()
    {
        var issue = Assert.Single(Check(Entry("e1", "p1.pantokrator", Job.WHM, Action(SacredSoil, "Sacred Soil"))));
        Assert.Equal("not-usable-by-job", issue.Code);
        Assert.Equal("Sacred Soil is not a WHM action.", issue.Message);
    }

    [Fact]
    public void UsesTheColumnsJobWhenTheEntryHasNone()
    {
        var entry = Entry("e1", "p1.pantokrator", null, Action(SacredSoil, "Sacred Soil"));
        entry.Who = new WhoSpec { Assignee = "White Mage" };
        var plan = new MitPlan { Fight = "top", Assignees = [new Assignee { Key = "White Mage", Job = Job.WHM }], Entries = [entry] };

        Assert.Contains(Check(entry, plan), i => i.Code == "not-usable-by-job");
    }

    private const uint Rampart = 7531;
    private const uint Reprisal = 7535;

    /// <summary>The test catalog with two tank actions.</summary>
    private static List<ReviewIssue> CheckWithTankActions(PlanEntry entry, MitPlan? plan = null)
    {
        var catalog = new FakeCatalog(Catalog().All.Concat(
        [
            new MitCue.Core.Actions.ActionInfo { Id = Rampart, Name = "Rampart", Jobs = [Job.PLD, Job.WAR, Job.DRK, Job.GNB], Level = 8, Kind = MitCue.Core.Actions.ActionKind.TankMit },
            new MitCue.Core.Actions.ActionInfo { Id = Reprisal, Name = "Reprisal", Jobs = [Job.PLD, Job.WAR, Job.DRK, Job.GNB], Level = 22, Kind = MitCue.Core.Actions.ActionKind.PartyMit },
        ]));
        plan ??= new MitPlan { Fight = "top", Entries = [entry] };
        return ReviewValidator.CheckEntry(entry, plan, Top(), catalog, actionsLoaded: true);
    }

    [Fact]
    public void ChecksASlotOnlyEntryAgainstTheJobsOfItsSlot()
    {
        // Final review finding: an entry with a slot but no job (LPDU's "MT", "H2" columns, often on the AI route) was not
        // checked at all, while the core PlanValidator checks it against the slot's role: an H2 Scholar got a Rampart call.
        var rampart = Entry("e1", "p1.pantokrator", null, Action(Rampart, "Rampart"));
        rampart.Who = new WhoSpec { Slot = "H2", Assignee = "H2" };
        var plan = new MitPlan { Fight = "top", Assignees = [new Assignee { Key = "H2", Slot = "H2" }], Entries = [rampart] };

        var issue = Assert.Single(CheckWithTankActions(rampart, plan));
        Assert.Equal("not-usable-by-job", issue.Code);
        Assert.Equal(DiagnosticSeverity.Error, issue.Severity);
        Assert.Equal("Rampart is not an action of the jobs of slot H2 (WHM/SCH/AST/SGE).", issue.Message);

        // A healer action in the same column is fine.
        var soil = Entry("e2", "p1.pantokrator", null, Action(SacredSoil, "Sacred Soil"));
        soil.Who = new WhoSpec { Slot = "H2", Assignee = "H2" };
        Assert.Empty(CheckWithTankActions(soil, plan));
    }

    [Fact]
    public void UsesTheColumnsSlotWhenTheEntryHasNeitherJobNorSlot()
    {
        var entry = Entry("e1", "p1.pantokrator", null, Action(SacredSoil, "Sacred Soil"));
        entry.Who = new WhoSpec { Assignee = "MT" };
        var plan = new MitPlan { Fight = "top", Assignees = [new Assignee { Key = "MT", Slot = "MT" }], Entries = [entry] };

        var issue = Assert.Single(CheckWithTankActions(entry, plan));
        Assert.Equal("not-usable-by-job", issue.Code);
        Assert.Equal("Sacred Soil is not an action of the jobs of slot MT (PLD/WAR/DRK/GNB).", issue.Message);
    }

    [Fact]
    public void AnEitherOrGroupOfASlotOnlyEntryNeedsOneOptionOfTheSlotsJobs()
    {
        var group = Entry("e1", "p1.pantokrator", null, PlanItem.AnyOf([Action(SacredSoil, "Sacred Soil"), Action(Rampart, "Rampart")]));
        group.Who = new WhoSpec { Slot = "H2" };
        Assert.DoesNotContain(CheckWithTankActions(group), i => i.Code == "not-usable-by-job");

        var tanks = Entry("e2", "p1.pantokrator", null, PlanItem.AnyOf([Action(Rampart, "Rampart"), Action(Reprisal, "Reprisal")]));
        tanks.Who = new WhoSpec { Slot = "H2" };
        var issue = Assert.Single(CheckWithTankActions(tanks), i => i.Code == "not-usable-by-job");
        Assert.Equal("None of Rampart / Reprisal is an action of the jobs of slot H2 (WHM/SCH/AST/SGE).", issue.Message);
    }

    [Fact]
    public void TheReviewOfAnAiPlanAgreesWithTheCoreValidatorOnSlotColumns()
    {
        // The finding's case on the AI route: YAML with a slot-only column, checked by the review and by PlanValidator.
        var plan = ImportPipeline.ParseYaml("""
            format: mitcue-plan/1
            name: AI plan
            fight: top
            assignees:
              - {key: H2, slot: H2}
            entries:
              - id: e001
                mechanic: p1.pantokrator
                who: {slot: H2, assignee: H2}
                do:
                  - {action: 7531, name: Rampart}
            """);
        var catalog = new FakeCatalog(Catalog().All.Append(
            new MitCue.Core.Actions.ActionInfo { Id = Rampart, Name = "Rampart", Jobs = [Job.PLD, Job.WAR, Job.DRK, Job.GNB], Level = 8, Kind = MitCue.Core.Actions.ActionKind.TankMit }));
        var review = new ReviewModel(plan, Top(), catalog, true, ReviewOrigin.Yaml);

        Assert.Contains(PlanValidator.Validate(plan, Top(), catalog), d => d.Code == "wrong-job" && d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(DiagnosticSeverity.Error, review.Rows[0].Worst);
        Assert.Contains(review.Rows[0].Issues, i => i.Code == "not-usable-by-job");
    }

    [Fact]
    public void FlagsActionsAboveTheSyncLevelOrReplaced()
    {
        var high = Assert.Single(Check(Entry("e1", "p1.pantokrator", Job.WHM, Action(DivineCaress, "Divine Caress"))));
        Assert.Equal("not-available-at-level", high.Code);
        Assert.Equal(DiagnosticSeverity.Warning, high.Severity);
        Assert.Contains("level 100", high.Message);

        var fight = Top();
        var tea = new MitCue.Core.Fights.FightDefinition
        {
            Key = "dmu", Name = "Dancing Mad", Territory = 1363, SyncLevel = 100, TimelineFile = "x", Mechanics = fight.Mechanics, Phases = fight.Phases,
        };
        var succor = Entry("e2", "p1.pantokrator", Job.SCH, Action(Succor, "Succor"));
        var issues = ReviewValidator.CheckEntry(succor, new MitPlan { Entries = [succor] }, tea, Catalog(), true);
        var replaced = Assert.Single(issues);
        Assert.Equal("not-available-at-level", replaced.Code);
        Assert.Contains("replaced by its upgrade at level 96", replaced.Message);
    }

    [Fact]
    public void AnEitherOrGroupNeedsOneUsableOption()
    {
        var crossJob = Entry("e1", "p1.pantokrator", Job.SCH, PlanItem.AnyOf([Action(Temperance, "Temperance"), Action(SacredSoil, "Sacred Soil")]));
        Assert.Empty(Check(crossJob));

        var none = Entry("e2", "p1.pantokrator", Job.SCH, PlanItem.AnyOf([Action(Temperance, "Temperance"), Action(Tetragrammaton, "Tetragrammaton")]));
        var issue = Assert.Single(Check(none));
        Assert.Equal("not-usable-by-job", issue.Code);
        Assert.Contains("Temperance / Tetragrammaton", issue.Message);
    }

    [Fact]
    public void NotesNameMismatches()
    {
        var issue = Assert.Single(Check(Entry("e1", "p1.pantokrator", Job.WHM, Action(Temperance, "Temperence"))));
        Assert.Equal("action-name-mismatch", issue.Code);
        Assert.Equal(DiagnosticSeverity.Info, issue.Severity);

        Assert.Empty(Check(Entry("e2", "p1.pantokrator", Job.WHM, Action(LiturgyOfTheBell, "liturgy of the bell"))));
    }

    [Fact]
    public void WarnsAboutEntriesForEveryone()
    {
        var entry = Entry("e1", "p1.pantokrator", null, PlanItem.ForNote("Everything"));
        Assert.Equal("no-assignee", Assert.Single(Check(entry)).Code);
    }

    [Fact]
    public void WarnsAboutOddTiming()
    {
        var entry = Entry("e1", "p1.pantokrator", Job.WHM, Action(Temperance, "Temperance"));
        entry.HitIndex = 7;
        entry.Offset = -95;
        var codes = Check(entry).Select(i => i.Code).ToList();
        Assert.Contains("hit-index", codes);
        Assert.Contains("offset-range", codes);

        var empty = Entry("e2", "p1.pantokrator", Job.WHM);
        Assert.Equal("no-items", Assert.Single(Check(empty)).Code);
    }

    [Fact]
    public void SkipsActionChecksWithoutACatalog()
    {
        Assert.Empty(Check(Entry("e1", "p1.pantokrator", Job.WHM, Action(999999, "Made Up")), actionsLoaded: false));
        var plan = new MitPlan { Fight = "top", Entries = [Entry("e1", "p1.pantokrator", Job.WHM)] };
        Assert.Contains(ReviewValidator.CheckPlan(plan, Top(), actionsLoaded: false), i => i.Code == "actions-not-checked");
    }

    [Fact]
    public void ChecksThePlan()
    {
        var codes = ReviewValidator.CheckPlan(new MitPlan { Fight = "tea" }, Top(), true).Select(i => i.Code).ToList();
        Assert.Contains("no-entries", codes);
        Assert.Contains("fight-mismatch", codes);
    }

    [Fact]
    public void NormalizesMissingAndRepeatedIds()
    {
        var plan = new MitPlan
        {
            Entries =
            [
                Entry("e002", "p1.pull", Job.WHM),
                Entry(string.Empty, "p1.pull", Job.WHM),
                Entry("e002", "p1.pull", Job.WHM),
                Entry("e001", "p1.pull", Job.WHM),
            ],
        };

        Assert.Equal(2, ReviewValidator.NormalizeIds(plan));

        var ids = plan.Entries.Select(e => e.Id).ToList();
        Assert.Equal("e002", ids[0]);
        Assert.Equal("e001", ids[3]);
        Assert.Equal(4, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(string.Empty, ids);
    }

    [Fact]
    public void RepairsNullCollections()
    {
        var plan = new MitPlan { Assignees = null!, Entries = [new PlanEntry { Id = "e1", Mechanic = null!, Who = null!, Do = null! }] };
        ReviewValidator.Repair(plan);
        Assert.NotNull(plan.Assignees);
        Assert.NotNull(plan.Entries[0].Who);
        Assert.NotNull(plan.Entries[0].Do);
        Assert.Equal(string.Empty, plan.Entries[0].Mechanic);
    }
}
