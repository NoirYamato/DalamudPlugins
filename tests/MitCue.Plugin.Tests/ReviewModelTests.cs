using MitCue.Core;
using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;
using MitCue.Import;
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
        var temperance = Catalog().Get(Temperance);

        Assert.Equal(TimingRules.DefaultOffset(temperance, 90), model.AutoOffset(row));
        Assert.Equal(28.2 + model.AutoOffset(row), model.PressTime(row)!.Value, 6);
        Assert.Equal(81.2 - 3, model.PressTime(model.Rows[1])!.Value, 6);
        Assert.Null(model.PressTime(model.Rows[2]));
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
    public void CountsTheEntriesOfAPlayer()
    {
        var model = Model(Plan());
        Assert.Equal(3, model.CountFor(Job.WHM, null));
        Assert.Equal(0, model.CountFor(Job.SCH, null));
        Assert.Equal(new[] { Job.WHM }, model.JobsInPlan());
    }
}
