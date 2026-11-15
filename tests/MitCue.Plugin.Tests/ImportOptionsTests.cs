using MitCue.Core;
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
    public void TheSlashesHelp_StatesTheAutomaticRule()
    {
        // The help gave a simplified rule ("no \"+\"", "three or more with \"/\""): every way of joining actions, and the
        // two cells listing several actions of one job, one of them three or more, decide it (PlanImporter.ChooseSlashReading).
        // Review finding (F15): it left out ";", "|" and a wide gap, which join actions too (CellParser.CountJoins).
        Assert.Contains(
            "all of them when no cell joins actions another way " +
            "(\"+\", \",\", \"&\", \";\", \"|\", an arrow, a spaced \" / \", a line break or a gap of three or more spaces) " +
            "and at least two cells list several actions of one job with \"/\", one of them three or more (Soil/Reci/Deploy/Whisper); else either one",
            ImportWindow.SlashesHelp,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The importer reads each sheet as the help's rule says, and where a cell joins actions another way, the help, the
    /// importer's "slash-reading" note and the AI prompt's rule all name that separator, in the same words.
    /// </summary>
    [Theory]
    [InlineData(new[] { "Soil/Reci/Deploy/Whisper", "Soil/Succor" }, "read as 'and'", null)]          // the rule's case
    [InlineData(new[] { "Soil/Reci/Deploy/Whisper", "Indom" }, "read as 'either one'", null)]          // only one cell lists several
    [InlineData(new[] { "Soil/Succor", "Indom/Succor" }, "read as 'either one'", null)]                // none lists three or more
    [InlineData(new[] { "Soil/Reci/Deploy/Whisper", "Soil/Succor", "Indom, Succor" }, "read as 'either one'", "\",\"")] // a comma joins
    [InlineData(new[] { "Soil/Reci/Deploy/Whisper", "Soil/Succor", "Indom & Succor" }, "read as 'either one'", "\"&\"")]
    [InlineData(new[] { "Soil/Reci/Deploy/Whisper", "Soil/Succor", "Indom / Succor" }, "read as 'either one'", "a spaced \" / \"")]
    [InlineData(new[] { "Soil/Reci/Deploy/Whisper", "Soil/Succor", "Indom -> Succor" }, "read as 'either one'", "an arrow")]
    [InlineData(new[] { "Soil/Reci/Deploy/Whisper", "Soil/Succor", "Indom\nSuccor" }, "read as 'either one'", "a line break")]
    [InlineData(new[] { "Soil/Reci/Deploy/Whisper", "Soil/Succor", "Indom; Succor" }, "read as 'either one'", "\";\"")]
    [InlineData(new[] { "Soil/Reci/Deploy/Whisper", "Soil/Succor", "Indom | Succor" }, "read as 'either one'", "\"|\"")]
    [InlineData(new[] { "Soil/Reci/Deploy/Whisper", "Soil/Succor", "Indom   Succor" }, "read as 'either one'", "a gap of three or more spaces")]
    [InlineData(new[] { "Soil/Reci/Deploy/Whisper", "Soil/Succor", "Indom\t\t\tSuccor" }, "read as 'either one'", "a gap of three or more spaces")]
    [InlineData(new[] { "Soil/Reci/Deploy/Whisper", "Soil/Succor", "Indom  Succor" }, "read as 'and'", null)] // two spaces join nothing
    public void TheAutomaticReading_IsTheHelpsRule(string[] cells, string reading, string? separator)
    {
        var data = new MitCue.Data.CoreData(Kit.Errors(), () => []);
        data.EnsureLoaded();
        var top = data.GetFight("top")!;
        string[] loops = ["Loop 1", "Loop 2", "Loop 3", "Loop 4"];
        var doc = new SheetDocument
        {
            Title = "Test sheet",
            Tabs = [Tab("P1", [["P1", "Scholar"], .. cells.Select((cell, i) => new[] { loops[i], cell })])],
        };

        var result = ImportPipeline.Import(doc, ImportWindow.OptionsFor(top, data.Actions, data.Aliases!, null, string.Empty, SlashReading.Auto, "2026-11-11"));

        var note = Assert.Single(result.Diagnostics, d => d.Code == "slash-reading").Message;
        Assert.Contains(reading, note, StringComparison.Ordinal);
        if (separator is null)
            return;
        var prompt = LlmPrompt.Build(new LlmPromptInput { Fight = top, Jobs = [Job.SCH] });
        var rule = prompt.Split('\n').Single(l => l.Contains("\"X/Y\" between actions of different jobs", StringComparison.Ordinal));
        Assert.Contains(separator, ImportWindow.SlashesHelp, StringComparison.Ordinal);
        Assert.Contains(separator.Replace('"', '\''), note, StringComparison.Ordinal); // the note quotes with '
        Assert.Contains(separator, rule, StringComparison.Ordinal);
    }

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
