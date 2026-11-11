using MitCue.Core;
using MitCue.Core.Fights;
using MitCue.Core.Plans;

namespace MitCue.Cli;

/// <summary>The player a plan is read for: fight, job, slot and column, and the entries that apply.</summary>
internal sealed record Player(FightDefinition Fight, MitPlan Plan, PlayerContext Me, IReadOnlyList<PlanEntry> Entries)
{
    /// <summary>"WHM, slot H1, column "White Mage"".</summary>
    public string Description =>
        this.Me.Job.ToString()
        + (this.Me.Slot != null ? ", slot " + this.Me.Slot : string.Empty)
        + (this.Me.AssigneeKey != null ? ", column \"" + this.Me.AssigneeKey + "\"" : string.Empty);

    /// <summary>"Carry the Clear – WHM" (or the plan id when it has no name).</summary>
    public string PlanName => string.IsNullOrWhiteSpace(this.Plan.Name) ? this.Plan.Id : this.Plan.Name;
}

/// <summary>The --job/--slot/--assignee/--fight options shared by plan, simulate and check.</summary>
internal static class PlayerSelection
{
    /// <summary>The <c>--assignee</c> value for every column of the player's job and slot.</summary>
    public const string AnyColumn = "*";

    public static readonly OptionSpec Job = new("job", "job", "Your job: an abbreviation (WHM, SCH) or a name (\"White Mage\"). Required.");
    public static readonly OptionSpec Slot = new("slot", "slot", "Your party slot: MT, OT, H1, H2, M1, M2, R1, R2 (also T1/T2, D1-D4).");

    public static readonly OptionSpec Assignee = new(
        "assignee",
        "key",
        "The plan column that is yours, e.g. \"White Mage\" or \"H2 SCH\" (other columns' calls for your slot apply too), or \"*\" for every column for your job and slot (default: the column the plugin chooses when none is saved).");

    public static readonly OptionSpec FightOption = new("fight", "key", "Fight key (default: the plan's fight).");

    public static IReadOnlyList<OptionSpec> Options { get; } = [Job, Slot, Assignee, FightOption];

    public static Player Select(CliContext ctx, ParsedArgs args, MitPlan plan, string? fightOverride = null)
    {
        var fightKey = fightOverride ?? args.Get("fight") ?? plan.Fight;
        if (string.IsNullOrWhiteSpace(fightKey))
            throw new UsageException($"{args.Command.Name}: the plan names no fight; pass --fight.");
        var fight = ctx.Fight(fightKey);
        if (!string.IsNullOrWhiteSpace(plan.Fight) && !string.Equals(plan.Fight, fight.Key, StringComparison.OrdinalIgnoreCase))
            ctx.Err.WriteLine($"note: the plan is for fight '{plan.Fight}', using '{fight.Key}' as asked.");

        var jobText = args.Require("job");
        if (!Jobs.TryParse(jobText, out var job))
            throw new UsageException($"{args.Command.Name}: unknown job '{jobText}'. Jobs: {string.Join(", ", Jobs.All.Select(j => j.ToString()).Order(StringComparer.Ordinal))}.");

        string? slot = null;
        if (args.Get("slot") is { } slotText)
        {
            if (!Slots.TryNormalize(slotText, out var s))
                throw new UsageException($"{args.Command.Name}: unknown slot '{slotText}'. Slots: {string.Join(", ", Slots.Standard)}.");
            slot = s;
        }

        string? assignee = null;
        var anyColumn = false;
        if (args.Get("assignee") is { } key)
        {
            if (key.Trim() == AnyColumn)
            {
                anyColumn = true;
            }
            else
            {
                var keys = plan.Assignees.Select(a => a.Key)
                    .Concat(plan.Entries.Select(e => e.Who?.Assignee).OfType<string>())
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                assignee = keys.FirstOrDefault(k => string.Equals(k, key.Trim(), StringComparison.OrdinalIgnoreCase))
                           ?? throw new UsageException($"{args.Command.Name}: the plan has no column '{key}'. Columns: {(keys.Count > 0 ? string.Join(", ", keys.Select(k => "\"" + k + "\"")) : "none")}.");
            }
        }

        PlayerContext me;
        if (assignee != null || anyColumn)
        {
            me = new PlayerContext(job, fight.SyncLevel, slot, assignee);
        }
        else
        {
            // The plugin's choice without a saved one (bundled plans, share codes): the job's column, which has the job-less
            // calls of the player's slot too (LPDU's "H2" for a Scholar in "H2 SCH"), or the choice with every column of the
            // job when its calls are in several (one per phase), so the checklist and the simulated pull have the calls the
            // overlay shows. The CLI knows no party: of a job's tank-pair columns it takes the first, as the plugin does
            // outside a party.
            var pick = ColumnSuggestion.Suggest(plan, job, slot);
            me = ColumnSuggestion.Context(pick, job, slot, fight.SyncLevel);
            var every = PlanSelection.For(plan, new PlayerContext(job, fight.SyncLevel, slot)).ToHashSet();
            if (!every.SetEquals(PlanSelection.For(plan, me)))
                ctx.Err.WriteLine($"note: {Describe(pick, job, slot)}, as the plugin chooses without a saved choice; --assignee picks a column, --assignee \"{AnyColumn}\" takes every column for your job and slot.");
        }

        var entries = PlanSelection.For(plan, me).ToList();
        if (anyColumn)
        {
            var columns = entries.Select(e => e.Who?.Assignee).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (columns.Count > 1)
                ctx.Err.WriteLine($"note: entries of {columns.Count} columns apply ({string.Join(", ", columns.Select(c => "\"" + c + "\""))}); pick yours with --assignee.");
        }

        if (entries.Count == 0)
            ctx.Err.WriteLine($"note: no entry of the plan applies to {job}{(me.Slot != null ? " " + me.Slot : string.Empty)}{(assignee != null ? " in column \"" + assignee + "\"" : string.Empty)}.");
        return new Player(fight, plan, me, entries);
    }

    /// <summary>"column "WAR/DRK WAR"", "any column for SCH in slot H2 (the slot of your job's column)".</summary>
    private static string Describe(ColumnPick pick, Job job, string? slot)
    {
        var what = pick.AssigneeKey != null
            ? $"column \"{pick.AssigneeKey}\""
            : pick.Slot != null ? $"any column for {job} in slot {pick.Slot}" : $"any column for {job}";
        return slot == null && pick.Slot != null && pick.AssigneeKey == null ? what + " (the slot of your job's column)" : what;
    }
}
