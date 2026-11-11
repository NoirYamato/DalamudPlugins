using MitCue.Cli.Commands;

namespace MitCue.Cli;

/// <summary>Entry point: global options, command dispatch, help and error handling.</summary>
internal static class Cli
{
    /// <summary>All commands in the order the help lists them.</summary>
    public static IReadOnlyList<CommandSpec> Commands { get; } =
    [
        FightCommands.Fights,
        FightCommands.Mechanics,
        TimelineCommand.Spec,
        ImportCommand.Spec,
        PlanCommand.Spec,
        SimulateCommand.Spec,
        CheckCommand.Spec,
        ValidateCommand.Spec,
        ShareCommands.Share,
        ShareCommands.Unshare,
    ];

    /// <summary>Runs one command line; returns the exit code (see <see cref="ExitCodes"/>).</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error, CliEnvironment environment)
    {
        var ctx = new CliContext(output, error, environment);
        try
        {
            var tokens = ExtractGlobalOptions(ctx, args, out var wantsHelp);
            if (tokens.Count == 0)
            {
                WriteGlobalHelp(output);
                return wantsHelp ? ExitCodes.Ok : ExitCodes.Usage;
            }

            var name = tokens[0];
            if (name == "help")
            {
                if (tokens.Count == 1)
                {
                    WriteGlobalHelp(output);
                    return ExitCodes.Ok;
                }

                WriteHelp(output, Find(tokens[1]));
                return ExitCodes.Ok;
            }

            var command = Find(name);
            var rest = tokens.Skip(1).ToList();
            if (wantsHelp || rest.Any(t => t is "-h" or "--help"))
            {
                WriteHelp(output, command);
                return ExitCodes.Ok;
            }

            var parsed = ArgParser.Parse(command, rest);
            var code = command.Run(ctx, parsed);
            output.Flush();
            return code;
        }
        catch (CliException ex)
        {
            output.Flush();
            error.WriteLine("error: " + ex.Message);
            return ex.ExitCode;
        }
        catch (NotImplementedException ex)
        {
            output.Flush();
            error.WriteLine("error: a part of MitCue.Core this command needs is not implemented in this build yet" + Where(ex) + ".");
            return ExitCodes.Failure;
        }
        catch (Exception ex) when (ex is FormatException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            output.Flush();
            error.WriteLine("error: " + ex.Message);
            return ExitCodes.Failure;
        }
    }

    public static void WriteGlobalHelp(TextWriter output)
    {
        output.WriteLine("mitcue: MitCue developer tool (fights, timelines, plan import, checklists, simulation, checks).");
        output.WriteLine("Run it with: dotnet run --project tools/MitCue.Cli -- <command> [arguments] [options]");
        output.WriteLine();
        output.WriteLine("Usage: mitcue <command> [arguments] [options]");
        output.WriteLine();
        output.WriteLine("Commands:");
        var table = new Table("name", "summary");
        foreach (var c in Commands)
            table.Add(c.Name, c.Summary);
        table.Write(output, "  ", header: false);
        output.WriteLine();
        output.WriteLine("Global options:");
        output.WriteLine("  --data <dir>   Data folder with fights/, timelines/ and actions/ (default: the repository's data/ above");
        output.WriteLine("                 the current or program directory, else the copy built into MitCue.Core).");
        output.WriteLine("  -h, --help     Show help; 'mitcue help <command>' or 'mitcue <command> --help' for one command.");
        output.WriteLine();
        output.WriteLine("Exit codes: 0 ok; 1 problems found (check, validate, import errors); 2 wrong command line;");
        output.WriteLine("            3 failure (unreadable or missing input, download error, core part not implemented).");
    }

    public static void WriteHelp(TextWriter output, CommandSpec command)
    {
        output.WriteLine($"Usage: mitcue {command.Name} {command.Synopsis}".TrimEnd());
        output.WriteLine();
        output.WriteLine(command.Summary);
        if (command.Details.Length > 0)
        {
            output.WriteLine();
            output.WriteLine(command.Details.TrimEnd());
        }

        output.WriteLine();
        output.WriteLine("Options:");
        var table = new Table("option", "help");
        foreach (var o in command.Options)
            table.Add(o.Display, o.Help);
        table.Add("--data <dir>", "Data folder (see 'mitcue --help').");
        table.Add("-h, --help", "Show this help.");
        table.Write(output, "  ", header: false);

        if (command.Examples.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("Examples:");
            foreach (var e in command.Examples)
                output.WriteLine("  mitcue " + e);
        }
    }

    private static CommandSpec Find(string name) =>
        Commands.FirstOrDefault(c => c.Name == name)
        ?? throw new UsageException($"unknown command '{name}'. Commands: {string.Join(", ", Commands.Select(c => c.Name))} (see 'mitcue --help').");

    /// <summary>Takes --data (anywhere) and a leading -h/--help out of the arguments.</summary>
    private static List<string> ExtractGlobalOptions(CliContext ctx, IReadOnlyList<string> args, out bool wantsHelp)
    {
        wantsHelp = false;
        var rest = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a == "--")
            {
                rest.AddRange(args.Skip(i));
                break;
            }

            if (a == "--data" || a.StartsWith("--data=", StringComparison.Ordinal))
            {
                if (ctx.DataDirectory != null)
                    throw new UsageException("--data is given more than once.");
                if (a.Length > "--data".Length)
                    ctx.DataDirectory = a["--data=".Length..];
                else if (i + 1 < args.Count)
                    ctx.DataDirectory = args[++i];
                else
                    throw new UsageException("--data needs a value (<dir>).");
                continue;
            }

            if (rest.Count == 0 && a is "-h" or "--help")
            {
                wantsHelp = true;
                continue;
            }

            rest.Add(a);
        }

        return rest;
    }

    private static string Where(Exception ex)
    {
        var frame = new System.Diagnostics.StackTrace(ex, fNeedFileInfo: false).GetFrames().FirstOrDefault(f => f.GetMethod()?.DeclaringType?.Namespace?.StartsWith("MitCue.Core", StringComparison.Ordinal) == true);
        var method = frame?.GetMethod();
        return method?.DeclaringType != null ? $" ({method.DeclaringType.Name}.{method.Name})" : string.Empty;
    }
}
