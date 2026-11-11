using MitCue.Core.Actions;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;

namespace MitCue.Cli.Commands;

/// <summary><c>plan &lt;plan.yaml&gt; --job &lt;job&gt;</c>: the player's checklist.</summary>
internal static class PlanCommand
{
    public static CommandSpec Spec { get; } = new()
    {
        Name = "plan",
        Synopsis = "<plan.yaml> --job <job> [--slot <slot>] [--assignee <key>] [--fight <key>] [--details]",
        Summary = "Print a player's checklist: one line per item in time order, with press time, offset, action, mechanic, target, notes and flags.",
        Details = """
            The plan file may be YAML, JSON or a share code ("-" reads standard input). Times are when the reminder says
            to press (the hit plus the offset), relative to the start of the mechanic's phase; offsets are seconds from
            the hit, marked * when the plan has no time and the default for the action is used (docs/DESIGN.md §5.1).
            Flags: optional (never counted as missed), when ready (press when off cooldown), phase end (due at the phase
            transition), seq n/m (press in order), ★ = how lethal the source says the mechanic is.
            """,
        Options = [.. PlayerSelection.Options, new("details", null, "Also show each entry's source cell and row notes.")],
        MinArgs = 1,
        MaxArgs = 1,
        Examples = ["plan ctc-whm.yaml --job WHM --slot H1", "plan lpdu.yaml --job SCH --assignee \"H2 SCH\" --details"],
        Run = Run,
    };

    private static int Run(CliContext ctx, ParsedArgs args)
    {
        var plan = ctx.ReadPlan(args.Positionals[0]);
        var player = PlayerSelection.Select(ctx, args, plan);
        var fight = player.Fight;
        var actions = ctx.Actions;
        var scheduler = new ReminderScheduler(fight, player.Entries, actions, new ReminderOptions());

        var items = scheduler.All;
        ctx.Out.WriteLine(
            $"\"{player.PlanName}\" ({fight.Key}, level {fight.SyncLevel}) as {player.Description}: " +
            $"{player.Entries.Count} of {Text.Count(plan.Entries.Count, "entry", "entries")}, {Text.Count(items.Count, "item")}");
        ctx.Out.WriteLine();

        var details = args.Has("details");
        var table = details
            ? new Table("phase", ">time", ">offset", "action", "mechanic", "target / notes", "flags", "entry", "src", "row notes")
            : new Table("phase", ">time", ">offset", "action", "mechanic", "target / notes", "flags", "entry");
        foreach (var r in items)
        {
            var e = r.Entry;
            var phase = fight.GetPhase(r.Mechanic.Phase);
            var offset = Text.Signed(r.PressAt - r.Hit) + (e.Offset == null ? "*" : " ");
            var mechanic = Text.Clip(r.Mechanic.Name, 48) + (e.HitIndex is > 0 and var h ? $" (hit {h + 1})" : string.Empty);
            var cells = new List<string?>
            {
                phase?.Id.ToUpperInvariant() ?? r.Mechanic.Phase,
                Text.Clock(r.PressAt - (phase?.Start ?? 0)),
                offset,
                ItemName(r.Item, actions),
                mechanic,
                TargetAndNote(e),
                Flags(r),
                e.Id,
            };
            if (details)
            {
                cells.Add(e.Src);
                cells.Add(Text.Clip(Text.OneLine(e.Detail), 120));
            }

            table.Add([.. cells]);
        }

        table.Write(ctx.Out);
        if (items.Any(r => r.Entry.Offset == null))
        {
            ctx.Out.WriteLine();
            ctx.Out.WriteLine("* no time in the plan: the default press time for the action is used.");
        }

        if (scheduler.Problems.Count > 0)
        {
            ctx.Out.WriteLine();
            ctx.Out.WriteLine($"Not scheduled ({scheduler.Problems.Count}):");
            foreach (var p in scheduler.Problems)
                ctx.Out.WriteLine("  " + p);
        }

        return ExitCodes.Ok;
    }

    /// <summary>The action name(s) of an item (catalog names; "A / B" for a choice) or its note.</summary>
    public static string ItemName(PlanItem item, IActionCatalog actions)
    {
        if (item.Action is { } id)
            return actions.Get(id)?.Name ?? item.Name ?? "#" + id;
        if (item.Any is { } options)
            return options.Count == 0 ? "(empty choice)" : string.Join(" / ", options.Select(o => ItemName(o, actions)));
        if (item.Note != null)
            return "note: " + Text.OneLine(item.Note);
        return item.Name ?? "(empty item)";
    }

    /// <summary>"on MT · after the 1st stack".</summary>
    public static string TargetAndNote(PlanEntry e)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(e.Target))
        {
            var t = Text.OneLine(e.Target);
            parts.Add(t.StartsWith("on ", StringComparison.OrdinalIgnoreCase) ? t : "on " + t);
        }

        if (!string.IsNullOrWhiteSpace(e.Note))
            parts.Add(Text.OneLine(e.Note));
        return string.Join(" · ", parts);
    }

    private static string Flags(Reminder r)
    {
        var e = r.Entry;
        var flags = new List<string>();
        if (e.Optional)
            flags.Add("optional");
        if (e.WhenReady)
            flags.Add("when ready");
        if (e.AtPhaseEnd)
            flags.Add("phase end");
        if (e.Seq && e.Do.Count > 1)
            flags.Add($"seq {r.ItemIndex + 1}/{e.Do.Count}");
        if (e.Severity is > 0 and var s)
            flags.Add(new string('★', Math.Min(s, 6)));
        return string.Join(", ", flags);
    }
}
