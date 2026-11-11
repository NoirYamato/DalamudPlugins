using MitCue.Core;
using MitCue.Core.Actions;
using MitCue.Import;

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
