using System.Globalization;

namespace MitCue.Cli;

/// <summary>Process exit codes.</summary>
internal static class ExitCodes
{
    /// <summary>Done; nothing wrong was found.</summary>
    public const int Ok = 0;

    /// <summary>Done, but the command reported errors (check, validate, import diagnostics).</summary>
    public const int Problems = 1;

    /// <summary>Wrong command line.</summary>
    public const int Usage = 2;

    /// <summary>The command failed: unreadable input, a download error, a missing file, an unimplemented core part.</summary>
    public const int Failure = 3;
}

/// <summary>An error that ends the command with a message and an exit code.</summary>
internal class CliException(int exitCode, string message) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}

/// <summary>A wrong command line (exit code 2).</summary>
internal sealed class UsageException(string message) : CliException(ExitCodes.Usage, message);

/// <summary>The command could not do its job (exit code 3).</summary>
internal sealed class FailureException(string message) : CliException(ExitCodes.Failure, message);

/// <summary>An option a command accepts: a flag (<see cref="Value"/> null) or an option with a value.</summary>
/// <param name="Name">Long name without dashes, e.g. "job".</param>
/// <param name="Value">Placeholder for the value in help, e.g. "job"; null for a flag.</param>
/// <param name="Help">One-line help text.</param>
/// <param name="Short">Single-letter alias without the dash, e.g. "o".</param>
internal sealed record OptionSpec(string Name, string? Value, string Help, string? Short = null)
{
    public bool IsFlag => this.Value == null;

    public string Display => (this.Short != null ? "-" + this.Short + ", " : string.Empty) + "--" + this.Name + (this.Value != null ? " <" + this.Value + ">" : string.Empty);
}

/// <summary>A command: its name, help and implementation.</summary>
internal sealed class CommandSpec
{
    public required string Name { get; init; }

    /// <summary>Arguments and options after the name, e.g. "&lt;plan.yaml&gt; --job &lt;job&gt; [--slot &lt;slot&gt;]".</summary>
    public required string Synopsis { get; init; }

    /// <summary>One line for the command list.</summary>
    public required string Summary { get; init; }

    /// <summary>Paragraphs printed by --help after the usage line.</summary>
    public string Details { get; init; } = string.Empty;

    public IReadOnlyList<OptionSpec> Options { get; init; } = [];

    public int MinArgs { get; init; }

    public int MaxArgs { get; init; }

    public IReadOnlyList<string> Examples { get; init; } = [];

    public required Func<CliContext, ParsedArgs, int> Run { get; init; }
}

/// <summary>The parsed command line of one command.</summary>
internal sealed class ParsedArgs(CommandSpec command, IReadOnlyList<string> positionals, IReadOnlyDictionary<string, string?> options)
{
    public CommandSpec Command { get; } = command;

    public IReadOnlyList<string> Positionals { get; } = positionals;

    public bool Has(string name) => options.ContainsKey(name);

    public string? Get(string name) => options.TryGetValue(name, out var value) ? value : null;

    public string Require(string name) =>
        this.Get(name) ?? throw new UsageException($"{this.Command.Name}: --{name} is required (see 'mitcue {this.Command.Name} --help').");

    /// <summary>A comma-separated list option ("a,b"), trimmed, empty items dropped; null when the option is absent.</summary>
    public IReadOnlyList<string>? GetList(string name) =>
        this.Get(name) is { } text
            ? text.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList()
            : null;

    public int? GetInt(string name)
    {
        if (this.Get(name) is not { } text)
            return null;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new UsageException($"{this.Command.Name}: --{name} needs a whole number, not '{text}'.");
        return value;
    }

    /// <summary>Seconds as a number ("12.5") or a time ("1:50").</summary>
    public double? GetSeconds(string name)
    {
        if (this.Get(name) is not { } text)
            return null;
        if (!Text.TryParseSeconds(text, out var value))
            throw new UsageException($"{this.Command.Name}: --{name} needs seconds (e.g. 12.5) or a time (e.g. 1:50), not '{text}'.");
        return value;
    }
}

/// <summary>Splits the tokens of one command into positionals and options, checking them against the command's spec.</summary>
internal static class ArgParser
{
    public static ParsedArgs Parse(CommandSpec command, IReadOnlyList<string> tokens)
    {
        var positionals = new List<string>();
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        var onlyPositionals = false;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (onlyPositionals || token == "-" || !token.StartsWith('-') || IsNumber(token))
            {
                positionals.Add(token);
                continue;
            }

            if (token == "--")
            {
                onlyPositionals = true;
                continue;
            }

            string name;
            string? inlineValue = null;
            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                name = token[2..];
                var eq = name.IndexOf('=');
                if (eq >= 0)
                {
                    inlineValue = name[(eq + 1)..];
                    name = name[..eq];
                }
            }
            else
            {
                name = token[1..];
            }

            var spec = command.Options.FirstOrDefault(o => o.Name == name || o.Short == name)
                       ?? throw new UsageException($"{command.Name}: unknown option '{token}' (see 'mitcue {command.Name} --help').");

            if (options.ContainsKey(spec.Name))
                throw new UsageException($"{command.Name}: --{spec.Name} is given more than once.");

            if (spec.IsFlag)
            {
                if (inlineValue != null)
                    throw new UsageException($"{command.Name}: --{spec.Name} takes no value.");
                options[spec.Name] = null;
                continue;
            }

            if (inlineValue == null)
            {
                if (i + 1 >= tokens.Count)
                    throw new UsageException($"{command.Name}: --{spec.Name} needs a value (<{spec.Value}>).");
                inlineValue = tokens[++i];
            }

            options[spec.Name] = inlineValue;
        }

        if (positionals.Count < command.MinArgs)
            throw new UsageException($"{command.Name}: missing argument. Usage: mitcue {command.Name} {command.Synopsis}");
        if (positionals.Count > command.MaxArgs)
            throw new UsageException($"{command.Name}: unexpected argument '{positionals[command.MaxArgs]}'. Usage: mitcue {command.Name} {command.Synopsis}");
        return new ParsedArgs(command, positionals, options);
    }

    /// <summary>"-5" or "-0.5" is a value, not an option.</summary>
    private static bool IsNumber(string token) => token.Length > 1 && token[0] == '-' && (char.IsDigit(token[1]) || token[1] == '.');
}
