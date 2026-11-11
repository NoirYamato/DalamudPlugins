using MitCue.Core.Plans;

namespace MitCue.Cli.Commands;

/// <summary><c>check &lt;plan.yaml&gt; --job &lt;job&gt;</c>: <see cref="PlanChecker"/> findings for a player.</summary>
internal static class CheckCommand
{
    public static CommandSpec Spec { get; } = new()
    {
        Name = "check",
        Synopsis = "<plan.yaml> --job <job> [--slot <slot>] [--assignee <key>] [--fight <key>]",
        Summary = "Check a player's plan entries: unknown mechanics or actions, actions the job cannot use at the sync level, recast conflicts, gauges.",
        Details = """
            Runs the plan checker on the entries that apply to the player. Recasts are checked on the timeline's nominal
            times: conflicts within a phase are errors; across a phase transition they are warnings, because the real gap
            depends on when the earlier phase ends (an early push shortens it, and a use late in a phase is skipped when
            the phase ends first). Uses with less than 5 s to spare are "tight" warnings. Gauge findings (Lily, Addersgall,
            Aetherflow) and follow-up findings (Consolation without Seraph, ...) are best-effort heuristics, marked (h).
            Exit code 1 when there is an error.
            """,
        Options = PlayerSelection.Options,
        MinArgs = 1,
        MaxArgs = 1,
        Examples = ["check ctc-whm.yaml --job WHM --slot H1", "check lpdu.yaml --job SCH --assignee \"H2 SCH\""],
        Run = Run,
    };

    private static int Run(CliContext ctx, ParsedArgs args)
    {
        var plan = ctx.ReadPlan(args.Positionals[0]);
        var player = PlayerSelection.Select(ctx, args, plan);
        var fight = player.Fight;
        var findings = PlanChecker.Check(fight, ctx.Actions, player.Entries, player.Me.Job, player.Me.Level);

        ctx.Out.WriteLine($"Checking \"{player.PlanName}\" ({fight.Key}, level {player.Me.Level}) as {player.Description}: {Text.Count(player.Entries.Count, "entry", "entries")}");
        ctx.Out.WriteLine();
        if (findings.Count > 0)
        {
            var table = new Table("severity", "time", "code", "message", "entry");
            foreach (var f in findings)
            {
                var phase = f.Entry != null ? fight.GetMechanic(f.Entry.Mechanic)?.Phase : null;
                table.Add(
                    f.Severity.ToString().ToLowerInvariant() + (f.Heuristic ? " (h)" : string.Empty),
                    f.Time is { } t ? Text.PhaseTime(fight, t, phase) : "-",
                    f.Code,
                    f.Message,
                    f.Entry?.Id);
            }

            table.Write(ctx.Out);
            ctx.Out.WriteLine();
        }

        var errors = findings.Count(f => f.Severity == PlanFindingSeverity.Error);
        var warnings = findings.Count(f => f.Severity == PlanFindingSeverity.Warning);
        var infos = findings.Count(f => f.Severity == PlanFindingSeverity.Info);
        ctx.Out.WriteLine(findings.Count == 0
            ? "No problems found."
            : $"{Text.Count(errors, "error")}, {Text.Count(warnings, "warning")}, {infos} info{(findings.Any(f => f.Heuristic) ? "; (h) = heuristic" : string.Empty)}.");
        return errors > 0 ? ExitCodes.Problems : ExitCodes.Ok;
    }
}
