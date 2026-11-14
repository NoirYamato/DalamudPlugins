using MitCue.Core.Import;
using MitCue.Import;
using MitCue.PluginTests.Support;
using MitCue.UI;
using static MitCue.PluginTests.TestData;

namespace MitCue.PluginTests;

/// <summary>The import window's Options step: the Slashes choice reaches both imports and the review reports the reading.</summary>
public class ImportOptionsTests
{
    [Theory]
    [InlineData(SlashReading.Auto, "(automatic)")]
    [InlineData(SlashReading.Either, "either one (TBN/Oblation)")]
    [InlineData(SlashReading.All, "all of them (Soil/Succor)")]
    public void SlashChoices_AreLabelledWithAnExample(SlashReading reading, string label) => Assert.Equal(label, ImportWindow.SlashLabel(reading));

    [Fact]
    public void TheTrialAndTheRealImport_ReadSlashesTheSameWay()
    {
        var data = new MitCue.Data.CoreData(Kit.Errors(), () => []);
        data.EnsureLoaded();
        var top = data.GetFight("top")!;

        var trial = ImportWindow.OptionsFor(top, data.Actions, data.Aliases!, null, string.Empty, SlashReading.All, "2026-11-11");
        var real = ImportWindow.OptionsFor(top, data.Actions, data.Aliases!, null, "carry_the_clear", SlashReading.All, "2026-11-11", "Plan", ["P1"], ["MT"]);

        Assert.Equal((SlashReading.All, (string?)null, (IReadOnlyList<string>?)null), (trial.Slashes, trial.CalibrationKey, trial.Assignees));
        Assert.Equal((SlashReading.All, "carry_the_clear", "Plan"), (real.Slashes, real.CalibrationKey, real.PlanName));
        Assert.Equal(["P1"], real.Tabs!);
        Assert.Equal(["MT"], real.Assignees!);
    }

    [Fact]
    public void ChangingTheSlashChoice_ChangesTheOptions()
    {
        string Key(SlashReading reading) => ImportWindow.OptionsKey("top", string.Empty, reading, "Plan", ["P3", "P1/P2"], ["H2 SCH"]);

        Assert.NotEqual(Key(SlashReading.Auto), Key(SlashReading.All));
        Assert.NotEqual(Key(SlashReading.Either), Key(SlashReading.All));
        Assert.Equal(Key(SlashReading.All), ImportWindow.OptionsKey("top", string.Empty, SlashReading.All, "Plan", ["P1/P2", "P3"], ["H2 SCH"]));
    }

    [Fact]
    public void TheReview_ShowsTheSlashReading_WithTheNotesAboutTheWholeImport()
    {
        var data = new MitCue.Data.CoreData(Kit.Errors(), () => []);
        data.EnsureLoaded();
        var top = data.GetFight("top")!;
        var doc = new SheetDocument
        {
            Title = "Test sheet",
            Tabs = [Tab("P1", ["P1", "Scholar"], ["Loop 1", "Soil/Reci/Deploy/Whisper"], ["Loop 2", "Soil/Succor/Indom"])],
        };

        foreach (var (reading, items) in new[] { (SlashReading.Auto, 4), (SlashReading.Either, 1) })
        {
            var result = ImportPipeline.Import(doc, ImportWindow.OptionsFor(top, data.Actions, data.Aliases!, null, string.Empty, reading, "2026-11-11"));
            var model = new ReviewModel(result.Plan, top, data.Actions, data.ActionsLoaded, ReviewOrigin.Importer, result.Diagnostics, result.PhaseZeros, result.PhaseSpread);

            var note = Assert.Single(model.GlobalIssues, i => i.Code == "slash-reading");
            Assert.Equal(DiagnosticSeverity.Info, note.Severity);
            Assert.Contains(reading == SlashReading.Auto ? "read as 'and'" : "read as 'either one'", note.Message, StringComparison.Ordinal);
            Assert.Equal(items, result.Plan.Entries.First(e => e.Mechanic == "p1.loop_set1").Do.Count);
        }
    }
}
