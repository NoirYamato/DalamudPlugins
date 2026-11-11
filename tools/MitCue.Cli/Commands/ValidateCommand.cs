using MitCue.Cli.Validation;

namespace MitCue.Cli.Commands;

/// <summary><c>validate</c>: every data file through the real loaders, with the data tests' consistency checks.</summary>
internal static class ValidateCommand
{
    public static CommandSpec Spec { get; } = new()
    {
        Name = "validate",
        Synopsis = "[--verbose]",
        Summary = "Load every data file (fights, timelines, actions, aliases; plans/ if present) and run the data consistency checks.",
        Details = """
            The checks are those of the data tests: every fight file loads, keys and territories are unique, sync levels
            are 80/90/100, phases are ordered, mechanic ids carry their phase, hits are ordered, calibration phases exist,
            and every mechanic's entry is in its timeline at its hit (±0.05 s); every timeline parses (parser warnings are
            listed); the action knowledge base is consistent (unique names, upgrades and follow-ups point both ways, the
            sync maps agree with levels and durations); every alias resolves to actions its jobs can use at levels 80, 90
            and 100; and each file of the copy built into MitCue.Core is the folder's (else: rebuild). Plans in a plans/ folder next to data/ must
            read, name a known fight, and use only known mechanics and actions their columns' jobs can use at the sync
            level; their other plan-check findings (recasts, gauges) are listed as warnings. Exit code 1 when there is a
            problem.
            """,
        Options = [new("verbose", null, "List every warning (default: the first 5 per file).")],
        MaxArgs = 0,
        Examples = ["validate", "validate --data ../other-checkout/data"],
        Run = Run,
    };

    private static int Run(CliContext ctx, ParsedArgs args)
    {
        var validator = new DataValidator(ctx.Data).Run();
        var verbose = args.Has("verbose");
        ctx.Out.WriteLine("Data: " + ctx.DataDescription);
        foreach (var section in validator.Sections)
        {
            ctx.Out.WriteLine();
            ctx.Out.WriteLine(section.Title + (section.Items.Count > 1 ? $" ({section.Items.Count} files)" : string.Empty));
            if (section.Items.Count == 0)
            {
                ctx.Out.WriteLine("  " + (section.EmptyNote ?? "none"));
                continue;
            }

            foreach (var item in section.Items)
            {
                var status = item.Problems.Count > 0 ? "FAIL" : item.Warnings.Count > 0 ? "warn" : "ok";
                var counts = new List<string>();
                if (item.Problems.Count > 0)
                    counts.Add(Text.Count(item.Problems.Count, "problem"));
                if (item.Warnings.Count > 0)
                    counts.Add(Text.Count(item.Warnings.Count, "warning"));
                var summary = item.Summary.Length > 0 ? ": " + item.Summary : string.Empty;
                ctx.Out.WriteLine($"  {status,-4}  {item.Name}{summary}{(counts.Count > 0 ? " (" + string.Join(", ", counts) + ")" : string.Empty)}");
                foreach (var p in item.Problems)
                    ctx.Out.WriteLine("        problem: " + Text.OneLine(p));
                var warnings = verbose ? item.Warnings : item.Warnings.Take(5).ToList();
                foreach (var w in warnings)
                    ctx.Out.WriteLine("        warning: " + Text.OneLine(w));
                if (warnings.Count < item.Warnings.Count)
                    ctx.Out.WriteLine($"        ... {item.Warnings.Count - warnings.Count} more warnings (--verbose)");
            }
        }

        ctx.Out.WriteLine();
        ctx.Out.WriteLine($"Result: {Text.Count(validator.ProblemCount, "problem")}, {Text.Count(validator.WarningCount, "warning")} in {Text.Count(validator.FileCount, "file")}.");
        return validator.ProblemCount > 0 ? ExitCodes.Problems : ExitCodes.Ok;
    }
}
