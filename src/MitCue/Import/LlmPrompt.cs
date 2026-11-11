using System.Globalization;
using System.Text;
using MitCue.Core;
using MitCue.Core.Actions;
using MitCue.Core.Fights;

namespace MitCue.Import;

/// <summary>An action the prompt lists (id, name, jobs, kind).</summary>
/// <param name="Id">Game action id.</param>
/// <param name="Name">English name.</param>
/// <param name="Jobs">Which of the selected jobs can use it.</param>
/// <param name="Kind">Action kind (party_mit, shield, ...).</param>
internal sealed record PromptAction(uint Id, string Name, IReadOnlyList<Job> Jobs, ActionKind Kind);

/// <summary>A shorthand alias the prompt lists: what a token means for one job (or every job).</summary>
/// <param name="Token">Normalized shorthand ("temp", "lilybell").</param>
/// <param name="Job">The job it applies to, or null for every listed job.</param>
/// <param name="Actions">Action names, in order.</param>
/// <param name="Sequence">The actions are pressed in this order.</param>
/// <param name="NoteOnly">The phrase is a note, not actions.</param>
/// <param name="Note">Explanation, if any.</param>
internal sealed record PromptAlias(string Token, Job? Job, IReadOnlyList<string> Actions, bool Sequence = false, bool NoteOnly = false, string? Note = null);

/// <summary>Everything <see cref="LlmPrompt.Build"/> needs.</summary>
internal sealed class LlmPromptInput
{
    /// <summary>The fight the plan is for.</summary>
    public required FightDefinition Fight { get; init; }

    /// <summary>Jobs whose calls should be converted (at least one).</summary>
    public required IReadOnlyList<Job> Jobs { get; init; }

    /// <summary>Actions of those jobs at the fight's sync level.</summary>
    public IReadOnlyList<PromptAction> Actions { get; init; } = [];

    /// <summary>Shorthand aliases for those jobs.</summary>
    public IReadOnlyList<PromptAlias> Aliases { get; init; } = [];

    /// <summary>The plan text (e.g. <see cref="SheetText.ForPrompt"/>), or null when the user pastes it after the prompt.</summary>
    public string? SourceText { get; init; }

    /// <summary>Where the plan came from (shown to the model, stored as the plan's source).</summary>
    public string? SourceUrl { get; init; }

    /// <summary>Suggested plan name.</summary>
    public string? PlanName { get; init; }

    /// <summary>The player's party slot (MT/OT/H1/...), if known.</summary>
    public string? Slot { get; init; }

    /// <summary>A note shown when the action or alias list could not be built (e.g. data that failed to load).</summary>
    public string? DataNote { get; init; }
}

/// <summary>
/// Builds the self-contained "convert my plan" prompt for any AI chat (docs/DESIGN.md §6, LLM fallback): the plan schema
/// (§3.2) with an example made from the selected fight, the conversion rules, the fight's mechanic catalog (ids, names,
/// phases, aliases, hit times), the selected jobs' actions at the sync level and their shorthand aliases, and the plan
/// text when it is known. The answer is pasted back into MitCue and goes through <see cref="ExtractYaml"/>, the plan
/// YAML reader and <see cref="PlanValidator"/>.
/// </summary>
internal static class LlmPrompt
{
    /// <summary>Marker line the user's plan text follows when it is not included.</summary>
    public const string PastePlaceholder = "(Paste the plan here: the cells copied from the sheet, or the text of the document.)";

    /// <summary>Builds the prompt.</summary>
    public static string Build(LlmPromptInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var fight = input.Fight;
        var ci = CultureInfo.InvariantCulture;
        var jobs = input.Jobs.Where(j => j != Job.None).Distinct().ToList();
        var jobList = jobs.Count > 0 ? string.Join(", ", jobs.Select(j => $"{Jobs.FullName(j)} ({j})")) : "every job in the plan";
        var sb = new StringBuilder(64 * 1024);

        sb.Append("# Task: convert a raid mitigation plan into MitCue plan YAML\n\n");
        sb.Append("You are helping a Final Fantasy XIV player turn their raid's mitigation plan (copied from a Google Sheet or a Google Doc) ")
          .Append("into the YAML format of MitCue, a game plugin that reminds the player which of their own actions to press during the fight. ")
          .Append("MitCue checks every id you write, so only use ids from the lists below.\n\n");
        sb.Append(ci, $"- Fight: {fight.Name} (fight key `{fight.Key}`); players are synced to level {fight.SyncLevel}.\n");
        sb.Append(ci, $"- Convert the calls of: {jobList}.");
        if (!string.IsNullOrWhiteSpace(input.Slot))
            sb.Append(ci, $" The player's party slot is {input.Slot}.");
        sb.Append(" Calls for other jobs can be left out.\n");
        if (!string.IsNullOrWhiteSpace(input.SourceUrl))
            sb.Append("- Source: ").Append(input.SourceUrl).Append('\n');
        sb.Append('\n');

        sb.Append("## What to reply\n\n");
        sb.Append("Reply with exactly one YAML document in a single ```yaml code block, and nothing else. ")
          .Append("If something in the plan is unclear, keep it as a note item and mention it in the entry's `note` instead of asking.\n\n");

        sb.Append("## Format (mitcue-plan/1)\n\n");
        AppendExample(sb, input, jobs);

        sb.Append("## Rules\n\n");
        var rules = new[]
        {
            "One entry per mechanic, column and press time. Split a cell into separate entries when its parts have their own times (\"Temp (1:18) / Bell (~1:22)\" is two entries); keep actions pressed together (\"A + B\") in one entry; \"A -> B -> C\" is one entry with `seq: true`.",
            "`mechanic` must be an id from the Mechanics list. Match the row's mechanic label against the names and the \"also called\" names, within the right phase: the same name can occur in several phases, and rows are in fight order. Times in the source help to find the right one.",
            "If a row matches no mechanic, anchor it to the nearest earlier mechanic of the same phase and say so in `note`.",
            "Every action item is `{ action: <id>, name: <name> }` with an id from the Actions list that the entry's job can use. Shorthand is explained under Aliases. Never invent ids.",
            "Text that is not one of the listed actions (\"Everything\", \"Kitchen sink\", a call for another job, a vague \"party mit\") becomes `{ note: \"...\" }`.",
            "\"X/Y\" without spaces is usually an either-or across jobs (\"Kera/Soil\" = Kerachole for Sage, Sacred Soil for Scholar): give each job its own entry with its own action. Alternatives for the same job become `any: [...]`.",
            "Timing: sheet times count from the pull or from the start of a phase, NOT on the timeline used in the Mechanics list. Compute `offset` only from the source itself: offset = (time written for the action) - (time of the mechanic's row), in seconds, negative = before the hit. Example: row \"1:21 Pantokrator\", call \"Temp (1:18)\" -> `offset: -3`.",
            "When a row lists several times (\"1:21 / 1:27 / 1:33\"), they are the mechanic's hits in order: offsets are relative to the first one, or set `hit_index` (0-based) and make the offset relative to that hit. A call without its own time gets no `offset` (MitCue then picks a sensible time for the action); \"before the 3rd hit\" without a time is `hit_index: 2` without `offset`.",
            "`who`: the column's job and/or party slot (MT, OT, H1, H2, M1, M2, R1, R2) and `assignee` = the column header text exactly. List every converted column once under `assignees`.",
            "`optional: true` for calls the source marks optional (grey text, written as [#808080: ...] or similar in the source below, \"(optional)\", \"if needed\").",
            "`when_ready: true` for \"on CD\", \"on cooldown\", \"asap\", \"when available\" or a ⏱ mark.",
            "`at_phase_end: true` for calls made at the end of a phase (\"as P3 dies\", \"just before P1 ends\", \"at the transition\"): anchor them to the last mechanic of that phase (its enrage when it has one), so they are still shown when the phase is pushed early.",
            "`target`: who the action goes on (\"MT\", \"OT\", \"your 3rd/4th-in-line player\"). `note`: a short hint from the cell (\"after the 1st stack\"). `detail`: a long explanation from a notes column (optional).",
            "`src`: the source cell, e.g. \"'P3'!F4\" (tab name in quotes, then the cell address shown in the source as R<row> and the column letter), when you know it.",
            "Several tabs can repeat the same calls (one tab per job plus a combined tab, or a summary tab), often worded differently: convert each call once. When two tabs give the same job's call for the same mechanic, keep the one from the tab that belongs to that job.",
            "Do not add entries the source does not have; do not leave out calls of the requested jobs (a call repeated in another tab counts once).",
        };
        for (var i = 0; i < rules.Length; i++)
            sb.Append(ci, $"{i + 1}. {rules[i]}\n");
        sb.Append('\n');

        AppendMechanics(sb, fight);
        AppendActions(sb, input, fight);
        AppendAliases(sb, input);

        sb.Append("## The plan to convert\n\n");
        if (!string.IsNullOrWhiteSpace(input.SourceText))
        {
            sb.Append("Each line is one row of the source: R<row number>, then the non-empty cells as <column letter>: <text>. ")
              .Append("\"⏎\" is a line break inside a cell; [#rrggbb: ...] is text in that colour (see the sheet's own legend, e.g. grey = optional); [struck: ...] is struck-through text.\n\n");
            sb.Append("```text\n").Append(input.SourceText.TrimEnd()).Append("\n```\n");
        }
        else
        {
            sb.Append(PastePlaceholder).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// The YAML inside an AI answer: the ```yaml block (or the first fenced block that looks like a plan), else the text
    /// from the first "format:"/"name:"/"fight:" line on, else the trimmed text.
    /// </summary>
    public static string ExtractYaml(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return string.Empty;
        var text = answer.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        var blocks = FencedBlocks(text);
        if (blocks.Count > 0)
        {
            var best = blocks.Find(b => b.Info is "yaml" or "yml" && LooksLikePlan(b.Body))
                       ?? blocks.Find(b => LooksLikePlan(b.Body))
                       ?? blocks.Find(b => b.Info is "yaml" or "yml")
                       ?? blocks[0];
            return best.Body.Trim('\n');
        }

        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();
            if (line.StartsWith("format:", StringComparison.Ordinal) || line.StartsWith("name:", StringComparison.Ordinal) ||
                line.StartsWith("fight:", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal))
                return string.Join('\n', lines.Skip(i)).Trim();
        }

        return text.Trim();
    }

    /// <summary>Actions the jobs can use at the level, merged by id (a role action lists every selected job that has it).</summary>
    public static IReadOnlyList<PromptAction> ActionsFor(IActionCatalog catalog, IEnumerable<Job> jobs, int level)
    {
        var byId = new Dictionary<uint, (ActionInfo Info, List<Job> Jobs)>();
        var order = new List<uint>();
        foreach (var job in jobs.Where(j => j != Job.None).Distinct())
        {
            foreach (var action in catalog.ForJob(job, level))
            {
                if (!byId.TryGetValue(action.Id, out var entry))
                {
                    entry = (action, []);
                    byId[action.Id] = entry;
                    order.Add(action.Id);
                }

                if (!entry.Jobs.Contains(job))
                    entry.Jobs.Add(job);
            }
        }

        return order.Select(id => new PromptAction(id, byId[id].Info.Name, byId[id].Jobs, byId[id].Info.Kind)).ToList();
    }

    /// <summary>Aliases that mean something for at least one of the jobs (built-in and user ones).</summary>
    /// <remarks>Uses only <see cref="AliasDictionary.Keys"/> and <see cref="AliasDictionary.Lookup"/>.</remarks>
    public static IReadOnlyList<PromptAlias> AliasesFor(AliasDictionary aliases, IEnumerable<Job> jobs)
    {
        var jobList = jobs.Where(j => j != Job.None).Distinct().ToList();
        var result = new List<PromptAlias>();
        foreach (var key in aliases.Keys)
        {
            var perJob = new List<(Job Job, AliasExpansion Expansion)>();
            foreach (var job in jobList)
            {
                var expansions = aliases.Lookup(key, job);
                if (expansions.Count > 0)
                    perJob.Add((job, expansions[0]));
            }

            if (perJob.Count == 0)
                continue;

            // The same meaning for every job that has it: one line without a job.
            var first = perJob[0].Expansion;
            if (perJob.Count == jobList.Count && jobList.Count > 1 && perJob.All(p => SameMeaning(p.Expansion, first)))
            {
                result.Add(new PromptAlias(key, null, first.Actions, first.Sequence, first.NoteOnly, first.Note));
                continue;
            }

            foreach (var (job, expansion) in perJob)
                result.Add(new PromptAlias(key, job, expansion.Actions, expansion.Sequence, expansion.NoteOnly, expansion.Note));
        }

        return result;
    }

    /// <summary>Formats a mechanic hit time for the catalog ("1167.8").</summary>
    internal static string Seconds(double t) => t.ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>YAML action kind name ("party_mit").</summary>
    internal static string KindName(ActionKind kind) => kind switch
    {
        ActionKind.PartyMit => "party_mit",
        ActionKind.Shield => "shield",
        ActionKind.Regen => "regen",
        ActionKind.Heal => "heal",
        ActionKind.TankMit => "tank_mit",
        ActionKind.Invuln => "invuln",
        ActionKind.Buff => "buff",
        ActionKind.LimitBreak => "lb",
        ActionKind.Movement => "movement",
        _ => "other",
    };

    /// <summary>YAML mechanic kind name ("raidwide", "lb_check").</summary>
    internal static string KindName(MechanicKind kind) => kind switch
    {
        MechanicKind.LbCheck => "lb_check",
        _ => kind.ToString().ToLowerInvariant(),
    };

    private static bool SameMeaning(AliasExpansion a, AliasExpansion b) =>
        a.Sequence == b.Sequence && a.NoteOnly == b.NoteOnly && a.Actions.SequenceEqual(b.Actions, StringComparer.OrdinalIgnoreCase);

    private static void AppendExample(StringBuilder sb, LlmPromptInput input, List<Job> jobs)
    {
        var ci = CultureInfo.InvariantCulture;
        var fight = input.Fight;
        var damage = fight.Mechanics.Where(m => m.Kind != MechanicKind.Marker).ToList();
        var first = damage.FirstOrDefault() ?? fight.Mechanics.FirstOrDefault();
        var multi = damage.FirstOrDefault(m => m.Hits.Count > 1 && m != first) ?? first;
        var job = jobs.Count > 0 ? jobs[0] : Job.WHM;
        var own = input.Actions.Where(a => a.Jobs.Contains(job)).ToList();
        var mit = own.FirstOrDefault(a => a.Kind == ActionKind.PartyMit) ?? own.FirstOrDefault();
        var alternatives = own.Where(a => a != mit && a.Kind is ActionKind.Heal or ActionKind.Shield or ActionKind.TankMit or ActionKind.PartyMit).Take(2).ToList();
        if (alternatives.Count < 2)
            alternatives = own.Where(a => a != mit).Take(2).ToList();

        string Item(PromptAction? a, string fallbackName) =>
            a != null ? $"{{ action: {a.Id.ToString(ci)}, name: {Quote(a.Name)} }}" : $"{{ action: <id>, name: {Quote(fallbackName)} }}";

        var slot = string.IsNullOrWhiteSpace(input.Slot) ? "H1" : input.Slot;
        var key = Jobs.FullName(job);
        sb.Append("```yaml\n");
        sb.Append("format: mitcue-plan/1\n");
        sb.Append("name: ").Append(Quote(string.IsNullOrWhiteSpace(input.PlanName) ? fight.Name + " plan" : input.PlanName)).Append('\n');
        sb.Append("fight: ").Append(fight.Key).Append('\n');
        sb.Append("source: { kind: llm");
        if (!string.IsNullOrWhiteSpace(input.SourceUrl))
            sb.Append(", url: ").Append(Quote(input.SourceUrl));
        sb.Append(" }\n");
        sb.Append("assignees:                       # one per source column you convert\n");
        sb.Append(ci, $"  - {{ key: {Quote(key)}, slot: {slot}, job: {job} }}\n");
        sb.Append("entries:\n");
        sb.Append("  - id: e001                      # unique: e001, e002, ...\n");
        sb.Append(ci, $"    mechanic: {first?.Id ?? "<mechanic id>"}        # required: an id from the Mechanics list\n");
        sb.Append("    offset: -3                    # seconds relative to the hit, negative = before; leave out when the source has no time\n");
        sb.Append(ci, $"    who: {{ job: {job}, slot: {slot}, assignee: {Quote(key)} }}\n");
        sb.Append("    do:                           # what to press\n");
        sb.Append("      - ").Append(Item(mit, "Action name")).Append('\n');
        sb.Append("      - { note: \"Everything\" }    # text that is not a listed action\n");
        sb.Append("    src: \"'Plan'!C18\"             # optional: source tab and cell\n");
        sb.Append("  - id: e002\n");
        sb.Append(ci, $"    mechanic: {multi?.Id ?? "<mechanic id>"}\n");
        if (multi is { Hits.Count: > 1 })
            sb.Append("    hit_index: 1                  # optional: 0-based hit of a multi-hit mechanic the offset is relative to\n");
        sb.Append("    offset: 1\n");
        sb.Append(ci, $"    who: {{ job: {job}, assignee: {Quote(key)} }}\n");
        sb.Append("    do:\n");
        sb.Append("      - { any: [ ").Append(Item(alternatives.ElementAtOrDefault(0), "First option")).Append(", ")
          .Append(Item(alternatives.ElementAtOrDefault(1), "Second option")).Append(" ] }   # either/or\n");
        sb.Append("    seq: false                    # true: press the items in this order\n");
        sb.Append("    target: MT                    # optional: who it goes on\n");
        sb.Append("    note: \"after the 1st hit\"     # optional: short hint\n");
        sb.Append("    detail: null                  # optional: longer notes from a notes column\n");
        sb.Append("    optional: false               # true when the source marks it optional\n");
        sb.Append("    when_ready: false             # true for \"on CD\" / \"as soon as ready\"\n");
        sb.Append("    at_phase_end: false           # true for calls at the end of a phase (\"as P1 dies\")\n");
        sb.Append("```\n\n");
    }

    private static void AppendMechanics(StringBuilder sb, FightDefinition fight)
    {
        var ci = CultureInfo.InvariantCulture;
        sb.Append(ci, $"## Mechanics of {fight.Name}\n\n");
        sb.Append("Phases (id: name, start on the fight timeline):");
        foreach (var phase in fight.Phases)
            sb.Append(ci, $" {phase.Id}: {phase.Name} ({Seconds(phase.Start)} s);");
        sb.Append('\n');
        sb.Append("Columns: id | phase | hit (s on MitCue's fight timeline, for ORDER only, never for offsets) | kind | name | also called\n\n");
        foreach (var m in fight.Mechanics)
        {
            sb.Append(m.Id).Append(" | ").Append(m.Phase).Append(" | ").Append(Seconds(m.Hit));
            if (m.Hits.Count > 1)
                sb.Append(" (hits ").Append(string.Join(", ", m.Hits.Select(Seconds))).Append(')');
            sb.Append(" | ").Append(KindName(m.Kind)).Append(" | ").Append(m.Name);
            var aliases = m.Aliases.Where(a => !string.Equals(a, m.Name, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (aliases.Count > 0)
                sb.Append(" | ").Append(string.Join("; ", aliases));
            sb.Append('\n');
        }

        sb.Append('\n');
    }

    private static void AppendActions(StringBuilder sb, LlmPromptInput input, FightDefinition fight)
    {
        var ci = CultureInfo.InvariantCulture;
        sb.Append(ci, $"## Actions at level {fight.SyncLevel} (id | name | jobs | kind)\n\n");
        if (input.Actions.Count == 0)
        {
            sb.Append("(The action list is not available. Write each action as { note: \"<the text from the plan>\" } instead.)\n\n");
            return;
        }

        foreach (var a in input.Actions)
            sb.Append(a.Id.ToString(ci)).Append(" | ").Append(a.Name).Append(" | ").Append(string.Join(", ", a.Jobs)).Append(" | ").Append(KindName(a.Kind)).Append('\n');
        sb.Append('\n');
    }

    private static void AppendAliases(StringBuilder sb, LlmPromptInput input)
    {
        sb.Append("## Aliases (shorthand used in plans -> what it means)\n\n");
        if (input.Aliases.Count == 0)
        {
            sb.Append(input.DataNote ?? "(No alias list is available; use the action names.)").Append("\n\n");
            return;
        }

        foreach (var alias in input.Aliases)
        {
            sb.Append(alias.Token).Append(" = ");
            if (alias.NoteOnly)
                sb.Append("a note, not an action").Append(alias.Note is { Length: > 0 } n ? " (" + n + ")" : string.Empty);
            else
                sb.Append(string.Join(alias.Sequence ? " -> " : " + ", alias.Actions));
            if (alias.Job is { } job)
                sb.Append(" (").Append(job).Append(')');
            if (!alias.NoteOnly && alias.Note is { Length: > 0 } note && note.Length <= 160)
                sb.Append(" - ").Append(note);
            sb.Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(input.DataNote))
            sb.Append(input.DataNote).Append('\n');
        sb.Append('\n');
    }

    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static bool LooksLikePlan(string body) =>
        body.Contains("entries:", StringComparison.Ordinal) || body.Contains("mitcue-plan", StringComparison.Ordinal);

    private static List<FencedBlock> FencedBlocks(string text)
    {
        var blocks = new List<FencedBlock>();
        var lines = text.Split('\n');
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i].TrimStart();
            if (!line.StartsWith("```", StringComparison.Ordinal))
            {
                i++;
                continue;
            }

            var info = line[3..].Trim().ToLowerInvariant();
            var body = new StringBuilder();
            i++;
            while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                body.Append(lines[i]).Append('\n');
                i++;
            }

            blocks.Add(new FencedBlock(info, body.ToString()));
            i++;
        }

        return blocks;
    }

    private sealed record FencedBlock(string Info, string Body);
}
