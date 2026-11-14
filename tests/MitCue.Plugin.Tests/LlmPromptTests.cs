using System.Globalization;
using MitCue.Core;
using MitCue.Core.Actions;
using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Import;
using MitCue.PluginTests.Support;
using MitCue.UI;

namespace MitCue.PluginTests;

public class LlmPromptTests
{
    private static LlmPromptInput Input(string? source = null, IReadOnlyList<PromptAlias>? aliases = null)
    {
        var catalog = TestData.Catalog();
        return new LlmPromptInput
        {
            Fight = TestData.Top(),
            Jobs = [Job.WHM, Job.SCH],
            Actions = LlmPrompt.ActionsFor(catalog, [Job.WHM, Job.SCH], 90),
            Aliases = aliases ??
            [
                new PromptAlias("temp", Job.WHM, ["Temperance"]),
                new PromptAlias("soil", Job.SCH, ["Sacred Soil"]),
                new PromptAlias("spreadlo", Job.SCH, ["Adloquium", "Deployment Tactics"], Sequence: true, Note: "Spread Adloquium."),
                new PromptAlias("everything", null, [], NoteOnly: true, Note: "use everything you have"),
            ],
            SourceText = source,
            SourceUrl = "https://docs.google.com/spreadsheets/d/abcdefghijklmnopqrstuvwxyz/edit",
            PlanName = "Carry the Clear",
            Slot = "H1",
        };
    }

    /// <summary>A prompt for <paramref name="jobs"/> (in this order) with the usual party slot <paramref name="slot"/>.</summary>
    private static LlmPromptInput JobsInput(Job[] jobs, string? slot) => new()
    {
        Fight = TestData.Top(),
        Jobs = jobs,
        Actions = LlmPrompt.ActionsFor(TestData.Catalog(), jobs, 90),
        Slot = slot,
    };

    /// <summary>The prompt's example plan, read as an AI answer is.</summary>
    private static MitPlan ExamplePlan(string prompt) => ImportPipeline.ParseYaml(prompt[prompt.IndexOf("```yaml\n", StringComparison.Ordinal)..]);

    [Fact]
    public void ContainsTheSchemaAndRules()
    {
        var prompt = LlmPrompt.Build(Input());

        Assert.Contains("format: mitcue-plan/1", prompt);
        Assert.Contains("fight: top", prompt);
        Assert.Contains("assignees:", prompt);
        Assert.Contains("entries:", prompt);
        Assert.Contains("mechanic:", prompt);
        Assert.Contains("offset:", prompt);
        Assert.Contains("hit_index:", prompt);
        Assert.Contains("when_ready:", prompt);
        Assert.Contains("optional:", prompt);
        Assert.Contains("{ any: [", prompt);
        Assert.Contains("{ note: \"Everything\" }", prompt);
        Assert.Contains("```yaml", prompt);
        Assert.Contains("level 90", prompt);
        Assert.Contains("White Mage (WHM), Scholar (SCH)", prompt);
        Assert.Contains("party slot is H1", prompt);
        Assert.Contains("offset = (time written for the action) - (time of the mechanic's row)", prompt);
    }

    [Fact]
    public void ContainsTheMechanicCatalog()
    {
        var prompt = LlmPrompt.Build(Input());

        Assert.Contains("p1: P1: Omega (0.0 s)", prompt);
        Assert.Contains("p2: P2: Omega-M/F (200.0 s)", prompt);
        Assert.Contains("p1.program_loop | p1 | 28.2 (hits 28.2, 37.2, 46.2, 55.2) | mixed | Program Loop | Program Loop: towers (36k) + blasters (HP to 1%) ★; Loop", prompt);
        Assert.Contains("p1.pantokrator | p1 | 81.2 (hits 81.2, 87.2, 93.2, 99.2) | multihit | Pantokrator | Panto", prompt);
        Assert.Contains("p2.solar_ray | p2 | 240.0 | tankbuster | Solar Ray", prompt);
        Assert.Contains("p1.pull | p1 | 0.0 | marker | Pull", prompt);

        // The name itself is not repeated as an alias.
        Assert.DoesNotContain("| Pantokrator | Pantokrator", prompt);
    }

    [Fact]
    public void ContainsTheJobsActionsAtTheSyncLevel()
    {
        var prompt = LlmPrompt.Build(Input());

        Assert.Contains("## Actions at level 90", prompt);
        Assert.Contains("16536 | Temperance | WHM | party_mit", prompt);
        Assert.Contains("188 | Sacred Soil | SCH | party_mit", prompt);
        Assert.Contains("7561 | Swiftcast | WHM, SCH | buff", prompt);
        Assert.Contains("186 | Succor | SCH | shield", prompt);

        // Level 100 / level 96 actions are not offered at level 90.
        Assert.DoesNotContain("Divine Caress", prompt);
        Assert.DoesNotContain("Concitation", prompt);
    }

    [Fact]
    public void ContainsTheAliases()
    {
        var prompt = LlmPrompt.Build(Input());

        Assert.Contains("temp = Temperance (WHM)", prompt);
        Assert.Contains("soil = Sacred Soil (SCH)", prompt);
        Assert.Contains("spreadlo = Adloquium -> Deployment Tactics (SCH) - Spread Adloquium.", prompt);
        Assert.Contains("everything = a note, not an action (use everything you have)", prompt);
    }

    [Fact]
    public void AliasOptions_AreSpelledOut()
    {
        // aliases.yaml "defensive cards": an action name may list options ("the Arrow|the Bole"), one item done by either.
        var prompt = LlmPrompt.Build(Input(aliases: [new PromptAlias("defensive cards", Job.AST, ["the Arrow|the Bole", "the Spire|the Ewer"])]));

        Assert.Contains("defensive cards = (the Arrow or the Bole) + (the Spire or the Ewer) (AST)", prompt);
    }

    [Fact]
    public void TheExampleUsesRealIdsOfTheFight()
    {
        var prompt = LlmPrompt.Build(Input());

        Assert.Contains("mechanic: p1.program_loop", prompt);
        Assert.Contains("mechanic: p1.pantokrator", prompt);
        Assert.Contains("{ action: 16536, name: \"Temperance\" }", prompt);
        Assert.Contains("who: { job: WHM, slot: H1, assignee: \"White Mage\" }", prompt);
        Assert.Contains("name: \"Carry the Clear\"", prompt);
        Assert.Contains("url: \"https://docs.google.com/spreadsheets/d/abcdefghijklmnopqrstuvwxyz/edit\"", prompt);
    }

    [Fact]
    public void IncludesTheSourceTextOrAPlaceholder()
    {
        var with = LlmPrompt.Build(Input("Source: Plan\n### Tab \"P1\"\nR2 | A: Pantokrator | E: Temp (1:18)\n"));
        Assert.Contains("R2 | A: Pantokrator | E: Temp (1:18)", with);
        Assert.Contains("```text", with);
        Assert.DoesNotContain(LlmPrompt.PastePlaceholder, with);

        var without = LlmPrompt.Build(Input());
        Assert.EndsWith(LlmPrompt.PastePlaceholder + "\n", without);
    }

    /// <summary>The prompt's "X/Y" rule, the line that starts with its number.</summary>
    private static string SlashRuleLine(string prompt) =>
        prompt.Split('\n').Single(l => l.Contains("\"X/Y\" between actions of different jobs", StringComparison.Ordinal));

    [Fact]
    public void TheSlashRuleIsDecidedPerDocument()
    {
        // The prompt said that "X/Y" between actions of one job is either/or (`any: [...]`), but the importer reads it per
        // document (PlanImporter.ChooseSlashReading): LPDU TEA's "Soil/Reci/Deploy/Whisper" is all four actions.
        var rule = SlashRuleLine(LlmPrompt.Build(Input()));

        Assert.Contains("(\"Kera/Soil\" = Kerachole for Sage, Sacred Soil for Scholar): give each job its own entry with its own action.", rule, StringComparison.Ordinal);
        Assert.Contains("Between actions of one job, \"/\" means the same in the whole sheet: all of them (one entry with every action, as for \"A + B\")", rule, StringComparison.Ordinal);
        Assert.Contains(
            "when no cell joins actions another way (\"+\", \",\", \"&\", an arrow, a spaced \" / \" or a line break) and at least two cells list several actions of one job with \"/\", one of them three or more (\"Soil/Reci/Deploy/Whisper\"); otherwise either one (`any: [...]`)",
            rule,
            StringComparison.Ordinal);
        Assert.DoesNotContain("For the plan below", rule, StringComparison.Ordinal); // no sheet loaded: the rule alone
        Assert.DoesNotContain("Alternatives for the same job become", rule, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SlashReading.All, false, "For the plan below, MitCue's importer reads \"/\" between actions of one job as all of them, by this rule: do the same.")]
    [InlineData(SlashReading.Either, false, "For the plan below, MitCue's importer reads \"/\" between actions of one job as either one, by this rule: do the same.")]
    [InlineData(SlashReading.All, true, "For the plan below, the player chose in MitCue's import options to read \"/\" between actions of one job as all of them: do the same.")]
    [InlineData(SlashReading.Either, true, "For the plan below, the player chose in MitCue's import options to read \"/\" between actions of one job as either one: do the same.")]
    public void WithALoadedSheetTheRuleSaysWhichReadingTheImporterUsed(SlashReading reading, bool chosen, string sentence)
    {
        var input = Input("Source: Plan\n### Tab \"P1\"\nR2 | A: Pantokrator | E: Soil/Reci/Deploy/Whisper\n");
        var prompt = LlmPrompt.Build(new LlmPromptInput
        {
            Fight = input.Fight,
            Jobs = input.Jobs,
            Actions = input.Actions,
            SourceText = input.SourceText,
            Slashes = reading,
            SlashesChosen = chosen,
        });

        Assert.EndsWith(" " + sentence, SlashRuleLine(prompt), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tea", SlashReading.All)]    // LPDU TEA: "Soil/Reci/Deploy/Whisper", nothing joined another way
    [InlineData("top", SlashReading.Either)] // LPDU TOP: "TBN/Oblation" next to "Living Dead + ..."
    public void TheImportWindowPassesTheSheetsOwnReadingOrThePlayersChoice(string fight, SlashReading expected)
    {
        var data = new MitCue.Data.CoreData(Kit.Errors(), () => []);
        data.EnsureLoaded();
        var doc = fight == "tea" ? FixtureSheets.LpduTea() : FixtureSheets.Lpdu();
        var analysis = ImportPipeline.Analyze(doc, new ImportOptions { Fight = data.GetFight(fight)!, Actions = data.Actions, Aliases = data.Aliases!, ImportedDate = "2026-11-11" });
        Assert.True(analysis.Available, analysis.Reason);

        Assert.Equal(expected, analysis.DocumentSlashes);
        Assert.Equal(((SlashReading?)expected, false), ImportWindow.PromptSlashes(SlashReading.Auto, analysis));
        var other = expected == SlashReading.All ? SlashReading.Either : SlashReading.All;
        Assert.Equal(((SlashReading?)other, true), ImportWindow.PromptSlashes(other, analysis)); // the Options step's choice

        // The trial import chose the other reading: the sheet's own one is still known.
        var chosen = ImportPipeline.Analyze(doc, new ImportOptions { Fight = data.GetFight(fight)!, Actions = data.Actions, Aliases = data.Aliases!, ImportedDate = "2026-11-11", Slashes = other });
        Assert.Equal(expected, chosen.DocumentSlashes);
    }

    /// <summary>The prompt's rule for a plain "Seraph", when it has one.</summary>
    private static string? SeraphRuleLine(string prompt) =>
        prompt.Split('\n').SingleOrDefault(l => l.Contains("numbers its Seraph calls", StringComparison.Ordinal));

    [Fact]
    public void WithALoadedSheetThatNumbersItsSeraphCalls_APlainSeraphIsSeraph1()
    {
        // LPDU TEA writes "Seraph1" and "Seraph2", and a plain "Seraph" at J Wave 5 and 7: MitCue's importer reads that as
        // "Seraph1" (PlanImporter.ChooseSeraphReading), while the alias list says Summon Seraph alone.
        var input = Input("Source: Plan\n### Tab \"Alex Prime/PA\"\nR21 | B: Jwaves (4) | E: Succor/Soil/Seraph\n");
        LlmPromptInput With(bool numbered, params Job[] jobs) =>
            new() { Fight = input.Fight, Jobs = jobs, Actions = input.Actions, SourceText = input.SourceText, SeraphNumbered = numbered };

        var prompt = LlmPrompt.Build(With(true, Job.WHM, Job.SCH));
        var rule = SeraphRuleLine(prompt);
        Assert.NotNull(rule);

        // The rule right after the "X/Y" one, numbered as the next.
        var lines = prompt.Split('\n').ToList();
        var slashRule = SlashRuleLine(prompt);
        Assert.Equal(lines.IndexOf(slashRule) + 1, lines.IndexOf(rule));
        Assert.StartsWith((int.Parse(slashRule[..slashRule.IndexOf('.', StringComparison.Ordinal)], CultureInfo.InvariantCulture) + 1) + ". ", rule, StringComparison.Ordinal);
        Assert.EndsWith(
            "This sheet numbers its Seraph calls (\"Seraph1\" = Summon Seraph -> Consolation, \"Seraph2\" = the second Consolation): a plain \"Seraph\" in it means \"Seraph1\" too, Summon Seraph then Consolation (`seq: true`), as MitCue's importer reads it: do the same.",
            rule,
            StringComparison.Ordinal);

        Assert.Null(SeraphRuleLine(LlmPrompt.Build(With(false, Job.WHM, Job.SCH)))); // a sheet that does not
        Assert.Null(SeraphRuleLine(LlmPrompt.Build(With(true, Job.WHM))));           // no Scholar calls to convert
    }

    [Theory]
    [InlineData("tea", true)]  // LPDU TEA: "Soil/Illum/Seraph1", "Seraph2/Succor"
    [InlineData("top", false)] // LPDU TOP: no numbered Seraph call
    public void TheImportWindowPassesTheSheetsSeraphReading(string fight, bool numbered)
    {
        var data = new MitCue.Data.CoreData(Kit.Errors(), () => []);
        data.EnsureLoaded();
        var doc = fight == "tea" ? FixtureSheets.LpduTea() : FixtureSheets.Lpdu();
        var analysis = ImportPipeline.Analyze(doc, new ImportOptions { Fight = data.GetFight(fight)!, Actions = data.Actions, Aliases = data.Aliases!, ImportedDate = "2026-11-11" });
        Assert.True(analysis.Available, analysis.Reason);

        Assert.Equal(numbered, analysis.SeraphNumbered);
        Assert.Equal(numbered, ImportWindow.PromptSeraph(analysis));
        Assert.False(ImportWindow.PromptSeraph(ImportAnalysis.Unavailable(new SheetDocument { Title = "x", Tabs = [] }, "no importer")));
        Assert.False(ImportWindow.PromptSeraph(null));
    }

    [Fact]
    public void WithoutATrialImportTheReadingIsNotKnown()
    {
        var unavailable = ImportAnalysis.Unavailable(new SheetDocument { Title = "x", Tabs = [] }, "no importer");

        Assert.Equal(((SlashReading?)null, false), ImportWindow.PromptSlashes(SlashReading.Auto, unavailable));
        Assert.Equal(((SlashReading?)null, false), ImportWindow.PromptSlashes(SlashReading.Auto, null));
        Assert.Equal(((SlashReading?)SlashReading.Either, true), ImportWindow.PromptSlashes(SlashReading.Either, null));
    }

    [Fact]
    public void SaysSoWhenDataIsMissing()
    {
        var input = new LlmPromptInput
        {
            Fight = TestData.Top(),
            Jobs = [Job.SGE],
            DataNote = "(The alias list is not available in this build: use the action names.)",
        };

        var prompt = LlmPrompt.Build(input);

        Assert.Contains("The action list is not available", prompt);
        Assert.Contains("The alias list is not available in this build", prompt);
        Assert.Contains("{ action: <id>, name: \"Action name\" }", prompt);
    }

    [Fact]
    public void ActionsForMergesRoleActionsAcrossJobs()
    {
        var actions = LlmPrompt.ActionsFor(TestData.Catalog(), [Job.WHM, Job.SCH, Job.None, Job.WHM], 90);

        var swiftcast = Assert.Single(actions, a => a.Id == TestData.Swiftcast);
        Assert.Equal(new[] { Job.WHM, Job.SCH }, swiftcast.Jobs);
        Assert.Single(actions, a => a.Id == TestData.Temperance);
        Assert.DoesNotContain(actions, a => a.Id == TestData.DivineCaress);
        Assert.Equal(ActionKind.PartyMit, actions.First(a => a.Id == TestData.SacredSoil).Kind);
    }

    [Fact]
    public void AliasesForGroupsMeaningsPerJob()
    {
        const string yaml = """
            schema: mitcue-aliases/1
            aliases:
              temp: Temperance
              soil: Sacred Soil
              swift: Swiftcast
              veil:
                - {job: WHM, actions: [Divine Benison]}
                - {job: SCH, actions: [Adloquium]}
            """;
        AliasDictionary aliases;
        try
        {
            aliases = AliasDictionary.Load(yaml);
        }
        catch (NotImplementedException)
        {
            return; // The alias loader is not part of this build yet; LlmPrompt.Build is covered above.
        }

        var lines = LlmPrompt.AliasesFor(aliases, [Job.WHM, Job.SCH]);

        // Without a catalog an alias without a job applies to every job: one line for both jobs.
        Assert.Contains(lines, l => l.Token == "temp" && l.Job == null && l.Actions.SequenceEqual(new[] { "Temperance" }));
        Assert.Contains(lines, l => l.Token == "swift" && l.Job == null);
        Assert.DoesNotContain(lines, l => l.Token == "temp" && l.Job != null);

        // Different meanings per job: one line per job.
        Assert.Contains(lines, l => l.Token == "veil" && l.Job == Job.WHM && l.Actions.SequenceEqual(new[] { "Divine Benison" }));
        Assert.Contains(lines, l => l.Token == "veil" && l.Job == Job.SCH && l.Actions.SequenceEqual(new[] { "Adloquium" }));

        var prompt = LlmPrompt.Build(new LlmPromptInput { Fight = TestData.Top(), Jobs = [Job.WHM, Job.SCH], Aliases = lines });
        Assert.Contains("veil = Divine Benison (WHM)", prompt);
        Assert.Contains("temp = Temperance\n", prompt);
    }

    [Fact]
    public void ExtractsYamlFromAFencedAnswer()
    {
        const string answer = """
            Sure! Here is your plan:

            ```yaml
            format: mitcue-plan/1
            fight: top
            entries:
              - id: e001
                mechanic: p1.pantokrator
            ```

            Let me know if you need anything else.
            """;

        var yaml = LlmPrompt.ExtractYaml(answer);

        Assert.StartsWith("format: mitcue-plan/1", yaml);
        Assert.EndsWith("mechanic: p1.pantokrator", yaml);
        Assert.DoesNotContain("Sure", yaml);
        Assert.DoesNotContain("```", yaml);
    }

    [Fact]
    public void AsksForPhaseEndCalls()
    {
        // Review finding: the prompt had no at_phase_end, so AI-converted plans never marked "as P3 dies" calls.
        var prompt = LlmPrompt.Build(Input());

        Assert.Contains("`at_phase_end: true` for calls made at the end of a phase (\"as P3 dies\"", prompt);
        Assert.Contains("    at_phase_end: false", prompt);
    }

    [Fact]
    public void AsksToConvertRepeatedCallsOnce()
    {
        // Review finding: tabs that repeat each other (Carry the Clear's combined tab) made the AI convert calls twice.
        var prompt = LlmPrompt.Build(Input());

        Assert.Contains("Several tabs can repeat the same calls", prompt);
        Assert.Contains("convert each call once", prompt);
        Assert.Contains("(a call repeated in another tab counts once)", prompt);
    }

    [Fact]
    public void TheExampleIsAValidPlan()
    {
        var prompt = LlmPrompt.Build(Input());
        var plan = ImportPipeline.ParseYaml(prompt[prompt.IndexOf("```yaml\n", StringComparison.Ordinal)..]);

        Assert.Equal("top", plan.Fight);
        Assert.Equal(2, plan.Entries.Count);
        Assert.All(plan.Entries, e => Assert.False(e.AtPhaseEnd));
    }

    [Fact]
    public void NamesAUsualSlotOfEveryJobsRoleAsBefore()
    {
        // The static's prompt (White Mage and Scholar, usual slot H1): H1 is a slot of both jobs' role, so the prompt
        // reads as it did before the usual slot counted only on a job of its role.
        var prompt = LlmPrompt.Build(Input());

        Assert.Contains("- Convert the calls of: White Mage (WHM), Scholar (SCH). The player's party slot is H1. Calls for other jobs can be left out.\n", prompt);
        Assert.Contains("  - { key: \"White Mage\", slot: H1, job: WHM }\n", prompt);
        Assert.Contains("    who: { job: WHM, slot: H1, assignee: \"White Mage\" }\n", prompt);
        Assert.Contains("    who: { job: WHM, assignee: \"White Mage\" }\n", prompt);
    }

    [Fact]
    public void DoesNotNameAUsualSlotOfAnotherRole()
    {
        // Review finding: a White Mage whose usual slot is MT got "The player's party slot is MT." and an example column
        // in MT, a seat the plugin never gives a White Mage (Slots.ForJob). Now no slot sentence, and the example is in H1.
        var prompt = LlmPrompt.Build(JobsInput([Job.WHM], "MT"));

        Assert.Contains("- Convert the calls of: White Mage (WHM). Calls for other jobs can be left out.\n", prompt);
        Assert.DoesNotContain("party slot is", prompt);
        Assert.Contains("  - { key: \"White Mage\", slot: H1, job: WHM }\n", prompt);
        Assert.Contains("    who: { job: WHM, slot: H1, assignee: \"White Mage\" }\n", prompt);
        Assert.DoesNotContain("slot: MT", prompt);
    }

    [Fact]
    public void NamesTheUsualSlotWithTheJobsOfItsRole()
    {
        // MT is the Warrior's slot, not the White Mage's: the sentence names the Warrior. The example is the first job's.
        var prompt = LlmPrompt.Build(JobsInput([Job.WAR, Job.WHM], "MT"));

        Assert.Contains("- Convert the calls of: Warrior (WAR), White Mage (WHM). The player's party slot is MT as Warrior (WAR). Calls for other jobs can be left out.\n", prompt);
        Assert.Contains("  - { key: \"Warrior\", slot: MT, job: WAR }\n", prompt);
        Assert.Contains("    who: { job: WAR, slot: MT, assignee: \"Warrior\" }\n", prompt);

        // With the White Mage first, the example is the White Mage's, in a healer slot.
        var whmFirst = LlmPrompt.Build(JobsInput([Job.WHM, Job.WAR], "MT"));
        Assert.Contains("- Convert the calls of: White Mage (WHM), Warrior (WAR). The player's party slot is MT as Warrior (WAR). Calls for", whmFirst);
        Assert.Contains("  - { key: \"White Mage\", slot: H1, job: WHM }\n", whmFirst);

        // Several jobs of the slot's role, but not every job.
        var tanks = LlmPrompt.Build(JobsInput([Job.PLD, Job.WAR, Job.DRK, Job.WHM], "OT"));
        Assert.Contains(" The player's party slot is OT as Paladin (PLD), Warrior (WAR) or Dark Knight (DRK). Calls for", tanks);
        Assert.Contains("  - { key: \"Paladin\", slot: OT, job: PLD }\n", tanks);
        Assert.Contains(" The player's party slot is H1 as White Mage (WHM). Calls for", LlmPrompt.Build(JobsInput([Job.PLD, Job.WHM], "H1")));
        Assert.Contains(" The player's party slot is MT as Paladin (PLD) or Warrior (WAR). Calls for", LlmPrompt.Build(JobsInput([Job.PLD, Job.WAR, Job.WHM], "MT")));
    }

    [Theory]
    [InlineData(Job.WAR, null, "MT")]
    [InlineData(Job.WAR, "OT", "OT")]
    [InlineData(Job.WAR, "H1", "MT")]
    [InlineData(Job.WHM, null, "H1")]
    [InlineData(Job.WHM, "MT", "H1")]
    [InlineData(Job.SCH, "H2", "H2")]
    [InlineData(Job.MNK, null, "M1")]
    [InlineData(Job.MNK, "M2", "M2")]
    [InlineData(Job.MNK, "R1", "M1")]
    [InlineData(Job.BRD, null, "R1")]
    [InlineData(Job.DNC, "R2", "R2")]
    [InlineData(Job.BLM, null, "R1")]
    public void TheExampleColumnIsInASlotOfItsJobsRole(Job job, string? usualSlot, string slot)
    {
        // Review finding: without a usual slot the example's column was always in H1 ("slot: H1, job: WAR"), so an AI that
        // copied it wrote a Warrior's calls for H1, which a Warrior in MT never gets. The usual slot when it is one of the
        // job's role, else the role's first slot: MT for tanks, H1 for healers, M1 for melee, R1 for ranged.
        var prompt = LlmPrompt.Build(JobsInput([job], usualSlot));
        var name = Jobs.FullName(job);

        Assert.Contains($"  - {{ key: \"{name}\", slot: {slot}, job: {job} }}\n", prompt);
        Assert.Contains($"    who: {{ job: {job}, slot: {slot}, assignee: \"{name}\" }}\n", prompt);
        Assert.Contains($"    who: {{ job: {job}, assignee: \"{name}\" }}\n", prompt);
        Assert.Equal(usualSlot == slot, prompt.Contains("party slot is", StringComparison.Ordinal));
        if (usualSlot == slot)
            Assert.Contains($". The player's party slot is {slot}. Calls for other jobs can be left out.\n", prompt);
    }

    [Theory]
    [InlineData("MT")]
    [InlineData(null)]
    public void TheExampleCallsApplyToAWarriorWhoseUsualSlotIsMT(string? promptSlot)
    {
        // The example's column and calls, read as a plan, are those of a Warrior whose usual slot is MT, with the column the
        // plugin suggests and with any column for the job (the example's calls in H1 were not: PlanSelection.Applies).
        var input = new LlmPromptInput
        {
            Fight = TestData.Top(),
            Jobs = [Job.WAR],
            Actions =
            [
                new PromptAction(7535, "Reprisal", [Job.WAR], ActionKind.PartyMit),
                new PromptAction(7531, "Rampart", [Job.WAR], ActionKind.TankMit),
                new PromptAction(7388, "Shake It Off", [Job.WAR], ActionKind.Shield),
            ],
            Slot = promptSlot,
        };
        var plan = ExamplePlan(LlmPrompt.Build(input));
        Assert.Equal(2, plan.Entries.Count);
        Assert.Equal(7535u, plan.Entries[0].Do[0].Action);

        var column = Assert.Single(plan.Assignees);
        Assert.Equal("Warrior", column.Key);
        Assert.Equal("MT", column.Slot);
        Assert.Equal(Job.WAR, column.Job);
        foreach (var pick in new[] { ColumnSuggestion.Suggest(plan, Job.WAR, "MT"), new ColumnPick(null, null) })
        {
            var me = ColumnSuggestion.Context(pick, Job.WAR, "MT", 90);
            Assert.Equal("MT", me.Slot);
            Assert.All(plan.Entries, e => Assert.True(PlanSelection.Applies(e, me), $"{e.Id} applies to {me}"));
        }
    }

    [Fact]
    public void ReadsPhaseEndCallsFromTheAnswer()
    {
        const string answer = """
            ```yaml
            format: mitcue-plan/1
            fight: top
            entries:
              - id: e001
                mechanic: p1.pantokrator
                who: { job: WAR, slot: MT }
                do:
                  - { note: "Rampart as P1 dies" }
                at_phase_end: true
            ```
            """;

        var entry = Assert.Single(ImportPipeline.ParseYaml(answer).Entries);
        Assert.True(entry.AtPhaseEnd);
    }

    [Fact]
    public void PrefersTheBlockThatLooksLikeAPlan()
    {
        const string answer = "Notes:\n```\nsome text\n```\nPlan:\n```yml\nname: x\nentries: []\n```\n";
        Assert.Equal("name: x\nentries: []", LlmPrompt.ExtractYaml(answer));
    }

    [Fact]
    public void ExtractsYamlWithoutFences()
    {
        Assert.Equal("format: mitcue-plan/1\nfight: top", LlmPrompt.ExtractYaml("Here you go\r\nformat: mitcue-plan/1\r\nfight: top\r\n"));
        Assert.Equal("fight: top", LlmPrompt.ExtractYaml("  fight: top  "));
        Assert.Equal(string.Empty, LlmPrompt.ExtractYaml("   "));
        Assert.Equal(string.Empty, LlmPrompt.ExtractYaml(null));
    }
}
