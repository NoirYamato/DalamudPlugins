using MitCue.Core;
using MitCue.Core.Actions;
using MitCue.Core.Data;
using MitCue.Core.Fights;
using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;
using MitCue.Import;
using MitCue.PluginTests.Support;
using static MitCue.PluginTests.TestData;

namespace MitCue.PluginTests;

public class ReviewModelTests
{
    private static MitPlan Plan()
    {
        var a = Entry("e1", "p1.program_loop", Job.WHM, Action(Temperance, "Temperance"));
        a.Src = "'TOP – White Mage'!C17";
        var b = Entry("e2", "p1.pantokrator", Job.WHM, Action(Temperance, "Temperance"), Action(LiturgyOfTheBell, "Liturgy of the Bell"));
        b.Src = "'TOP – White Mage'!C18";
        b.Offset = -3;
        var c = Entry("e3", "p1.made_up", Job.WHM, Action(Tetragrammaton, "Tetragrammaton"));
        c.Src = "'TOP – White Mage'!C19";
        return new MitPlan { Name = "Test", Fight = "top", Assignees = [new Assignee { Key = "White Mage", Job = Job.WHM }], Entries = [a, b, c] };
    }

    private static ReviewModel Model(MitPlan plan, IReadOnlyList<ImportDiagnostic>? diagnostics = null) =>
        new(plan, Top(), Catalog(), true, ReviewOrigin.Importer, diagnostics);

    /// <summary>The importer's notes on <see cref="Plan"/>'s Pantokrator cell (four hits): a recast fit and a caret note.</summary>
    private static List<ImportDiagnostic> FittedPantokrator() =>
    [
        new(DiagnosticSeverity.Info, "recast-fit", "Temperance at p1.pantokrator is planned 50.0 s after the Temperance at p1.program_loop (recast 60.0 s): it is pressed 3.0 s before its hit instead of 5.0 s, so that its NOW cue comes when Temperance is ready again.", "'TOP – White Mage'!C18"),
        new(DiagnosticSeverity.Info, "caret-note", "The remark under it applies to the whole cell.", "'TOP – White Mage'!C18"),
    ];

    /// <summary>The offset the scheduler presses an entry at (press − hit of its first reminder).</summary>
    private static double SchedulerOffset(PlanEntry entry)
    {
        var reminder = new ReminderScheduler(Top(), [entry], Catalog(), new ReminderOptions()).All[0];
        return reminder.PressAt - reminder.Hit;
    }

    [Fact]
    public void BuildsOneRowPerEntryWithChecks()
    {
        var model = Model(Plan());

        Assert.Equal(3, model.Rows.Count);
        Assert.Null(model.Rows[0].Worst);
        Assert.Equal(DiagnosticSeverity.Error, model.Rows[2].Worst);
        Assert.Equal(1, model.CountWorst(DiagnosticSeverity.Error));
        Assert.Equal(3, model.Rows.Select(r => r.Uid).Distinct().Count());
    }

    [Fact]
    public void AttachesImporterDiagnosticsByCellThenRow()
    {
        var diagnostics = new List<ImportDiagnostic>
        {
            new(DiagnosticSeverity.Warning, "ambiguous-mechanic", "Two mechanics match", "'TOP – White Mage'!C18"),
            new(DiagnosticSeverity.Info, "unresolved-token", "\"Lily\" became a note", "'TOP – White Mage'!B17"),
            new(DiagnosticSeverity.Warning, "no-header", "Tab X has no header row", "'X'!A1"),
            new(DiagnosticSeverity.Info, "calibration", "P1 fitted from 4 rows"),
        };

        var model = Model(Plan(), diagnostics);

        Assert.Contains(model.Rows[1].ImportIssues, i => i.Code == "ambiguous-mechanic" && i.FromImporter);
        Assert.Contains(model.Rows[0].ImportIssues, i => i.Code == "unresolved-token");
        Assert.Contains(model.GlobalIssues, i => i.Code == "no-header");
        Assert.Contains(model.GlobalIssues, i => i.Code == "calibration");
        Assert.Equal(DiagnosticSeverity.Warning, model.Rows[1].Worst);
    }

    [Fact]
    public void ChangingTheMechanicRechecksAndClearsMechanicDiagnostics()
    {
        var diagnostics = new List<ImportDiagnostic> { new(DiagnosticSeverity.Warning, "ambiguous-mechanic", "?", "'TOP – White Mage'!C19") };
        var model = Model(Plan(), diagnostics);
        var row = model.Rows[2];
        var version = row.Version;
        var revision = model.Revision;

        model.SetMechanic(row, "p2.solar_ray");

        Assert.Equal("p2.solar_ray", row.Entry.Mechanic);
        Assert.Null(row.Worst);
        Assert.Empty(row.ImportIssues);
        Assert.True(row.Edited);
        Assert.True(row.Version > version);
        Assert.True(model.Revision > revision);
    }

    /// <summary>
    /// The importer's recast-fit info gives the offset the recast fit wrote ("it is pressed 1.5 s before its hit instead of
    /// 7.2 s"): once the player resets or drags that offset, or anchors the entry to another mechanic or another hit, it no
    /// longer describes the entry, like the importer's notes about sheet times. Its other notes, such as a caret note, stay.
    /// </summary>
    [Theory]
    [InlineData("automatic")]
    [InlineData("drag")]
    [InlineData("mechanic")]
    [InlineData("hit")]
    public void ChangingAFittedPressDropsTheRecastFitInfo(string edit)
    {
        var model = Model(Plan(), FittedPantokrator());
        var row = model.Rows[1];
        Assert.Contains(row.ImportIssues, i => i.Code == "recast-fit");

        switch (edit)
        {
            case "automatic":
                model.SetOffset(row, null);
                break;
            case "drag":
                model.SetOffset(row, -2.9);
                break;
            case "mechanic":
                model.SetMechanic(row, "p1.program_loop");
                break;
            case "hit":
                model.SetHitIndex(row, 1);
                break;
        }

        Assert.True(row.Edited);
        Assert.DoesNotContain(row.ImportIssues, i => i.Code == "recast-fit");
        Assert.Contains(row.ImportIssues, i => i.Code == "caret-note");
    }

    [Fact]
    public void SettingTheHitAnEntryIsAlreadyAnchoredToChangesNothing()
    {
        // The first hit is no hit index (null) as well as hit index 0: choosing it again keeps the row as imported.
        var model = Model(Plan(), FittedPantokrator());
        var row = model.Rows[1];

        model.SetHitIndex(row, 0);

        Assert.Null(row.Entry.HitIndex);
        Assert.False(row.Edited);
        Assert.Contains(row.ImportIssues, i => i.Code == "recast-fit");
    }

    /// <summary>
    /// The same on the rows of LPDU TEA the recast fit moves: reset to automatic timing, each loses its info and keeps its
    /// other notes, and the rows not reset yet keep theirs. 'LL/BJCC'!G52, the White Mage's Asylum at Gavel, is then pressed
    /// 7.2 s before its hit, where its info said 1.5 s.
    /// </summary>
    [Fact]
    public void ResettingAFittedPressInTheTeaSheetDropsItsRecastFitInfo()
    {
        var data = new MitCue.Data.CoreData(Kit.Errors(), () => []);
        data.EnsureLoaded();
        var tea = data.GetFight("tea")!;
        var result = new PlanImporter().Import(FixtureSheets.LpduTea(), new ImportOptions { Fight = tea, Actions = data.Actions, Aliases = data.Aliases!, ImportedDate = "2026-11-11" });
        var model = new ReviewModel(result.Plan, tea, data.Actions, true, ReviewOrigin.Importer, result.Diagnostics, result.PhaseZeros, result.PhaseSpread);
        static bool Fitted(ReviewRow row) => row.ImportIssues.Any(i => i.Code == "recast-fit");
        ReviewRow Row(string src) => Assert.Single(model.Rows, r => r.Entry.Src == src);

        var gavel = Row("'LLBJCC'!G52");
        Assert.Equal(-1.5, gavel.Entry.Offset);
        Assert.Contains("it is pressed 1.5 s before its hit instead of 7.2 s", Assert.Single(gavel.ImportIssues, i => i.Code == "recast-fit").Message, StringComparison.Ordinal);

        var fitted = model.Rows.Count(Fitted);
        foreach (var src in new[] { "'LLBJCC'!G52", "'LLBJCC'!E46", "'Alex PrimePA'!G23", "'Alex PrimePA'!H45" })
        {
            var row = Row(src);
            Assert.True(Fitted(row), src);
            var others = row.ImportIssues.Where(i => i.Code != "recast-fit").ToList();

            model.SetOffset(row, null);

            Assert.Null(row.Entry.Offset);
            Assert.Equal(others, row.ImportIssues);
            fitted--;
            Assert.Equal(fitted, model.Rows.Count(Fitted));
        }

        Assert.Equal(-7.2, model.AutoOffset(gavel), 6);
        Assert.Equal(356.5 - 7.2, model.PressTime(gavel)!.Value, 6);
        Assert.Contains(gavel.ImportIssues, i => i.Code == "caret-note");
    }

    [Fact]
    public void ChangingTheMechanicDropsAHitIndexItDoesNotHave()
    {
        var model = Model(Plan());
        var row = model.Rows[1];
        model.SetHitIndex(row, 3);
        Assert.Equal(3, row.Entry.HitIndex);

        model.SetMechanic(row, "p2.solar_ray");
        Assert.Null(row.Entry.HitIndex);

        model.SetHitIndex(row, 0);
        Assert.Null(row.Entry.HitIndex);
    }

    [Fact]
    public void EditsOffsetsAndFlags()
    {
        var model = Model(Plan());
        var row = model.Rows[0];

        model.SetOffset(row, -4.26);
        Assert.Equal(-4.3, row.Entry.Offset);
        model.SetOffset(row, null);
        Assert.Null(row.Entry.Offset);

        model.SetOptional(row, true);
        model.SetWhenReady(row, true);
        model.SetTarget(row, "MT");
        model.SetNote(row, "   ");
        Assert.True(row.Entry.Optional);
        Assert.True(row.Entry.WhenReady);
        Assert.Equal("MT", row.Entry.Target);
        Assert.Null(row.Entry.Note);
        Assert.True(row.Edited);
    }

    [Fact]
    public void ComputesAutomaticTimingLikeTheScheduler()
    {
        var model = Model(Plan());
        var row = model.Rows[0];

        // Temperance (20 s) on Program Loop (tower sets 9 s apart) also covers the third set: 0.5 s before the first.
        Assert.Equal(SchedulerOffset(row.Entry), model.AutoOffset(row), 6);
        Assert.Equal(-0.5, model.AutoOffset(row), 6);
        Assert.Equal(28.2 + model.AutoOffset(row), model.PressTime(row)!.Value, 6);
        Assert.Equal(81.2 - 3, model.PressTime(model.Rows[1])!.Value, 6);
        Assert.Null(model.PressTime(model.Rows[2]));
    }

    /// <summary>
    /// The review shows the press time the overlay will use (<see cref="TimingRules.ForEntry"/>): for every entry of the
    /// bundled TEA plan, the planned press time (and for an entry without an offset, the automatic offset) is the scheduler's.
    /// </summary>
    [CoreFact]
    public void OnTheBundledTeaPlanTheReviewTimesEveryEntryLikeTheScheduler()
    {
        var data = new EmbeddedDataSource();
        var tea = new FightLibrary(data).Get("tea")!;
        var actions = ActionCatalog.Load(data.ReadText("actions/actions.yaml"));
        var plan = PlanSerializer.FromYaml(EmbeddedDataSource.Plans().ReadText("tea-lpdu.yaml"));
        var model = new ReviewModel(plan, tea, actions, true, ReviewOrigin.Yaml);
        var all = new ReminderScheduler(tea, plan.Entries, actions, new ReminderOptions()).All;
        var scheduled = all.GroupBy(r => r.Entry).ToDictionary(g => g.Key, g => g.First());

        Assert.Equal(plan.Entries.Count, model.Rows.Count);
        foreach (var row in model.Rows)
        {
            var reminder = scheduled[row.Entry];
            Assert.Equal(reminder.PressAt, model.PressTime(row)!.Value, 6);
            if (row.Entry.Offset is null)
                Assert.Equal(reminder.PressAt - reminder.Hit, model.AutoOffset(row), 6);
        }

        // Each item as the scheduler presses it, also an item of a series call that waits for its own floor ('Alex
        // Prime/PA'!E21: the Succor at 718.4, its instant Soil, Summon Seraph and Consolation after wave 4, at 720.2).
        var rows = model.Rows.ToDictionary(r => r.Entry);
        foreach (var r in all)
            Assert.Equal(r.PressAt, model.ItemPressTimes(rows[r.Entry])![r.ItemIndex], 6);
        var e21 = model.Rows.Single(r => r.Entry.Src == "'Alex Prime/PA'!E21");
        Assert.Equal([718.4, 720.2, 720.2, 720.2], model.ItemPressTimes(e21)!.Select(t => Math.Round(t, 2)));
        Assert.Equal(718.4, model.PressTime(e21)!.Value, 6);

        Assert.Contains(model.Rows, r => r.Entry.Offset is null);
    }

    /// <summary>
    /// The same on the bundled LPDU TOP plan, whose Wave Cannon cells press a 10 s call later than the rest of the cell, so
    /// that it lasts through the Wild Charge: the entry's press is its first item's, each item's is the scheduler's.
    /// </summary>
    [CoreFact]
    public void OnTheBundledLpduTopPlanTheReviewTimesEveryItemLikeTheScheduler()
    {
        var data = new EmbeddedDataSource();
        var top = new FightLibrary(data).Get("top")!;
        var actions = ActionCatalog.Load(data.ReadText("actions/actions.yaml"));
        var plan = PlanSerializer.FromYaml(EmbeddedDataSource.Plans().ReadText("top-lpdu.yaml"));
        var model = new ReviewModel(plan, top, actions, true, ReviewOrigin.Yaml);
        var all = new ReminderScheduler(top, plan.Entries, actions, new ReminderOptions()).All;
        var rows = model.Rows.ToDictionary(r => r.Entry);

        foreach (var g in all.GroupBy(r => r.Entry))
        {
            var first = g.Min(r => r.PressAt);
            Assert.Equal(first, model.PressTime(rows[g.Key])!.Value, 6);
            if (g.Key.Offset is null)
                Assert.Equal(first - g.First().Hit, model.AutoOffset(rows[g.Key]), 6);
            foreach (var r in g)
                Assert.Equal(r.PressAt, model.ItemPressTimes(rows[g.Key])![r.ItemIndex], 6);
        }

        // 'DRK/GNB'!B12 on Wave Cannon 2: Rampart and The Blackest Night at −6 s, Dark Mind and Oblation 0.5 s before the
        // first protean.
        var b12 = model.Rows.Single(r => r.Entry.Src == "'DRK/GNB'!B12");
        Assert.Equal([1263.9, 1269.4, 1269.4, 1263.9], model.ItemPressTimes(b12)!.Select(t => Math.Round(t, 2)));
        Assert.Equal(-6, model.AutoOffset(b12), 6);
    }

    [Fact]
    public void DeletesAndRestoresEntriesInOrder()
    {
        var plan = Plan();
        var model = Model(plan);
        var middle = model.Rows[1];

        model.Delete(middle);
        Assert.Equal(2, model.Rows.Count);
        Assert.DoesNotContain(middle.Entry, plan.Entries);
        Assert.Equal(1, model.UndoCount);

        Assert.True(model.UndoDelete());
        Assert.Equal(new[] { "e1", "e2", "e3" }, plan.Entries.Select(e => e.Id));
        Assert.Equal(new[] { "e1", "e2", "e3" }, model.Rows.Select(r => r.Entry.Id));
        Assert.False(model.UndoDelete());
    }

    [Fact]
    public void RenumbersMissingIds()
    {
        var plan = Plan();
        plan.Entries[1].Id = "e1";
        var model = Model(plan);

        Assert.Equal(1, model.RenumberedIds);
        Assert.Contains(model.GlobalIssues, i => i.Code == "renumbered");
        Assert.Equal(3, plan.Entries.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public void MarksPhaseEndCalls()
    {
        // Review finding: the review could not set the phase-end flag (DESIGN §5.2: "as P3 dies").
        var model = Model(Plan());
        var row = model.Rows[1];

        model.SetAtPhaseEnd(row, true);

        Assert.True(row.Entry.AtPhaseEnd);
        Assert.True(row.Edited);
        model.SetAtPhaseEnd(row, false);
        Assert.False(row.Entry.AtPhaseEnd);
    }

    [Fact]
    public void CountsTheChangesAnotherImportWouldLose()
    {
        // Review finding: going back to Options and importing again silently threw the review's edits away.
        var model = Model(Plan());
        Assert.Equal(0, model.EditCount);

        model.SetOffset(model.Rows[0], -2);
        model.SetOptional(model.Rows[0], true);
        Assert.Equal(1, model.EditCount);

        model.Delete(model.Rows[2]);
        Assert.Equal(2, model.EditCount);
        model.UndoDelete();
        Assert.Equal(1, model.EditCount);
    }

    [Fact]
    public void ReplacesANoteWithTheActionItStandsFor()
    {
        // Review finding: the review could not change what to press, so an unresolved shorthand stayed an untracked note.
        var plan = Plan();
        plan.Entries[0].Do = [PlanItem.ForNote("Tempy")];
        var diagnostics = new List<ImportDiagnostic> { new(DiagnosticSeverity.Info, "unresolved-token", "\"Tempy\" became a note", "'TOP – White Mage'!C17") };
        var model = Model(plan, diagnostics);
        var row = model.Rows[0];
        Assert.Contains(row.ImportIssues, i => i.Code == "unresolved-token");

        model.SetItem(row, 0, Action(Temperance, "Temperance"));

        Assert.Equal(Temperance, Assert.Single(row.Entry.Do).Action);
        Assert.Empty(row.ImportIssues);
        Assert.True(row.Edited);
        Assert.Equal(SchedulerOffset(row.Entry), model.AutoOffset(row), 6);
    }

    [Fact]
    public void AddsAndRemovesItemsAndRechecksThem()
    {
        var model = Model(Plan());
        var row = model.Rows[0];

        model.AddItem(row, Action(SacredSoil, "Sacred Soil"));
        Assert.Equal(2, row.Entry.Do.Count);
        Assert.Contains(row.CheckIssues, i => i.Code == "not-usable-by-job");

        model.RemoveItem(row, 1);
        Assert.Single(row.Entry.Do);
        Assert.DoesNotContain(row.CheckIssues, i => i.Code == "not-usable-by-job");

        model.RemoveItem(row, 0);
        Assert.Empty(row.Entry.Do);
        Assert.Contains(row.CheckIssues, i => i.Code == "no-items");
        model.RemoveItem(row, 5); // out of range: ignored
    }

    [Fact]
    public void OffersTheActionsOfTheEntrysJob()
    {
        var plan = Plan();
        plan.Entries[0].Who = new WhoSpec { Slot = "H2", Assignee = "H2" }; // a job-less column
        var model = Model(plan);

        Assert.Equal(Job.WHM, model.JobOf(model.Rows[1], Job.SCH));
        Assert.Equal(Job.SCH, model.JobOf(model.Rows[0], Job.SCH));
        var actions = model.ActionsFor(Job.SCH).Select(a => a.Name).ToList();
        Assert.Equal(new[] { "Adloquium", "Sacred Soil", "Succor", "Swiftcast" }, actions);
        Assert.Empty(model.ActionsFor(Job.None));
    }

    [Fact]
    public void CountsTheEntriesOfAPlayer()
    {
        var model = Model(Plan());
        Assert.Equal(3, model.CountFor(Job.WHM, null));
        Assert.Equal(0, model.CountFor(Job.SCH, null));
        Assert.Equal(new[] { Job.WHM }, model.JobsInPlan());
    }
}
