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
    public static readonly OptionSpec Job = new("job", "job", "Your job: an abbreviation (WHM, SCH) or a name (\"White Mage\"). Required.");
    public static readonly OptionSpec Slot = new("slot", "slot", "Your party slot: MT, OT, H1, H2, M1, M2, R1, R2 (also T1/T2, D1-D4).");
    public static readonly OptionSpec Assignee = new("assignee", "key", "The plan column that is yours, e.g. \"White Mage\" or \"H2 SCH\" (default: every column for your job and slot).");
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
        if (args.Get("assignee") is { } key)
        {
            var keys = plan.Assignees.Select(a => a.Key)
                .Concat(plan.Entries.Select(e => e.Who?.Assignee).OfType<string>())
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            assignee = keys.FirstOrDefault(k => string.Equals(k, key.Trim(), StringComparison.OrdinalIgnoreCase))
                       ?? throw new UsageException($"{args.Command.Name}: the plan has no column '{key}'. Columns: {(keys.Count > 0 ? string.Join(", ", keys.Select(k => "\"" + k + "\"")) : "none")}.");
        }

        var me = new PlayerContext(job, fight.SyncLevel, slot, assignee);
        var entries = PlanSelection.For(plan, me).ToList();

        if (assignee == null)
        {
            var columns = entries.Select(e => e.Who?.Assignee).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (columns.Count > 1)
                ctx.Err.WriteLine($"note: entries of {columns.Count} columns apply ({string.Join(", ", columns.Select(c => "\"" + c + "\""))}); pick yours with --assignee.");
        }

        if (entries.Count == 0)
            ctx.Err.WriteLine($"note: no entry of the plan applies to {job}{(slot != null ? " " + slot : string.Empty)}{(assignee != null ? " in column \"" + assignee + "\"" : string.Empty)}.");
        return new Player(fight, plan, me, entries);
    }
}
