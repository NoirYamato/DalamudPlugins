using MitCue.Core.Plans;

namespace MitCue.Cli.Commands;

/// <summary><c>share &lt;plan.yaml&gt;</c> and <c>unshare &lt;code&gt;</c>: plan share codes.</summary>
internal static class ShareCommands
{
    public static CommandSpec Share { get; } = new()
    {
        Name = "share",
        Synopsis = "<plan.yaml>",
        Summary = "Print a plan's share code (MITCUE1:...), which players paste into /mitcue > Plans > Share code.",
        Details = "The plan may be YAML, JSON or a share code; \"-\" reads standard input. The code is printed on one line.",
        MinArgs = 1,
        MaxArgs = 1,
        Examples = ["share ctc-whm.yaml"],
        Run = RunShare,
    };

    public static CommandSpec Unshare { get; } = new()
    {
        Name = "unshare",
        Synopsis = "<code|file|-> [-o <plan.yaml>]",
        Summary = "Turn a share code back into plan YAML.",
        Details = """
            The argument is the code itself, a file containing it, or "-" for standard input. Whitespace, quotes and
            backticks around the code and line breaks inside it (from chat) are ignored.
            """,
        Options = [new("output", "plan.yaml", "Write the YAML here (default: standard output).", "o")],
        MinArgs = 1,
        MaxArgs = 1,
        Examples = ["unshare MITCUE1:... -o plan.yaml", "unshare code.txt"],
        Run = RunUnshare,
    };

    private static int RunShare(CliContext ctx, ParsedArgs args)
    {
        var plan = ctx.ReadPlan(args.Positionals[0]);
        var code = PlanSerializer.ToShareCode(plan);
        ctx.Out.WriteLine(code);
        ctx.Err.WriteLine($"\"{plan.Name}\": {Text.Count(plan.Entries.Count, "entry", "entries")}, {code.Length} characters.");
        return ExitCodes.Ok;
    }

    private static int RunUnshare(CliContext ctx, ParsedArgs args)
    {
        var argument = args.Positionals[0];
        var looksLikeCode = argument.Trim().TrimStart('`', '"', '\'').StartsWith("MITCUE", StringComparison.OrdinalIgnoreCase);
        string code;
        if (argument == "-")
            code = ctx.Environment.Input().ReadToEnd();
        else if (!looksLikeCode && File.Exists(ctx.ResolvePath(argument)))
            code = File.ReadAllText(ctx.ResolvePath(argument));
        else
            code = argument;

        MitPlan plan;
        try
        {
            plan = PlanSerializer.FromShareCode(code);
        }
        catch (FormatException ex)
        {
            throw new FailureException(ex.Message);
        }

        var output = args.Get("output") ?? "-";
        ctx.WriteText(output, PlanSerializer.ToYaml(plan));
        if (output != "-")
            ctx.Err.WriteLine($"Wrote \"{plan.Name}\" ({Text.Count(plan.Entries.Count, "entry", "entries")}) to {ctx.ResolvePath(output)}");
        return ExitCodes.Ok;
    }
}
