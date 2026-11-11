using System.Text;
using MitCue.Core.Actions;
using MitCue.Core.Data;
using MitCue.Core.Fights;
using MitCue.Core.Plans;

namespace MitCue.Cli;

/// <summary>What the CLI needs from its surroundings (replaced in tests).</summary>
internal sealed class CliEnvironment
{
    /// <summary>Directory that relative paths are resolved against.</summary>
    public required string CurrentDirectory { get; init; }

    /// <summary>Directory of the program, where the repository's data/ is searched as a fallback.</summary>
    public required string ProgramDirectory { get; init; }

    /// <summary>Standard input (share codes and plans can be piped in with "-").</summary>
    public required Func<TextReader> Input { get; init; }

    /// <summary>Creates the HTTP handler used for Google Sheets downloads (tests pass a fake).</summary>
    public Func<HttpMessageHandler> HttpHandler { get; init; } = () => new SocketsHttpHandler();

    /// <summary>Today as an ISO date, stamped on imported plans unless --date is given.</summary>
    public Func<string> Today { get; init; } = () => DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static CliEnvironment FromProcess() => new()
    {
        CurrentDirectory = Environment.CurrentDirectory,
        ProgramDirectory = AppContext.BaseDirectory,
        Input = () => Console.In,
    };
}

/// <summary>One CLI invocation: output writers, the environment and the (lazily loaded) data.</summary>
internal sealed class CliContext(TextWriter output, TextWriter error, CliEnvironment environment)
{
    /// <summary>Embedded path of the action knowledge base.</summary>
    public const string ActionsPath = "actions/actions.yaml";

    /// <summary>Embedded path of the shorthand alias dictionary.</summary>
    public const string AliasesPath = "actions/aliases.yaml";

    private IDataSource? data;
    private string? dataDescription;
    private FightLibrary? fights;
    private ActionCatalog? actions;
    private AliasDictionary? aliases;

    public TextWriter Out { get; } = output;

    public TextWriter Err { get; } = error;

    public CliEnvironment Environment { get; } = environment;

    /// <summary>The --data directory, if given.</summary>
    public string? DataDirectory { get; set; }

    /// <summary>
    /// The data files: the --data directory; else the repository's data/ found above the current directory or the
    /// program; else the copy built into MitCue.Core.
    /// </summary>
    public IDataSource Data
    {
        get
        {
            if (this.data != null)
                return this.data;

            if (this.DataDirectory != null)
            {
                var dir = this.ResolvePath(this.DataDirectory);
                if (!Directory.Exists(dir))
                    throw new FailureException($"--data: no such directory: {dir}");
                if (!Directory.Exists(Path.Combine(dir, "fights")) && !Directory.Exists(Path.Combine(dir, "timelines")) && !Directory.Exists(Path.Combine(dir, "actions")))
                    throw new FailureException($"--data: {dir} has none of the folders fights/, timelines/, actions/.");
                var source = new DirectoryDataSource(dir);
                this.dataDescription = source.Root;
                return this.data = source;
            }

            foreach (var start in new[] { this.Environment.CurrentDirectory, this.Environment.ProgramDirectory })
            {
                try
                {
                    var source = DirectoryDataSource.FindRepoData(start);
                    this.dataDescription = source.Root;
                    return this.data = source;
                }
                catch (DirectoryNotFoundException)
                {
                    // try the next start, then the built-in copy
                }
            }

            this.dataDescription = "built-in copy (no data/ folder found)";
            return this.data = new EmbeddedDataSource();
        }
    }

    /// <summary>Where <see cref="Data"/> comes from, for messages.</summary>
    public string DataDescription
    {
        get
        {
            _ = this.Data;
            return this.dataDescription!;
        }
    }

    public FightLibrary Fights => this.fights ??= Load("the fight files (fights/*.yaml)", () => new FightLibrary(this.Data));

    public ActionCatalog Actions => this.actions ??= Load(ActionsPath, () => ActionCatalog.Load(this.Data.ReadText(ActionsPath)));

    public AliasDictionary Aliases => this.aliases ??= Load(AliasesPath, () => AliasDictionary.Load(this.Data.ReadText(AliasesPath), this.Actions));

    /// <summary>The fight with this key, or a usage error naming the fights there are.</summary>
    public FightDefinition Fight(string key) =>
        this.Fights.Get(key.Trim())
        ?? throw new UsageException($"unknown fight '{key}'. Fights: {string.Join(", ", this.Fights.Fights.Select(f => f.Key))}.");

    public string ResolvePath(string path) => Path.GetFullPath(path, this.Environment.CurrentDirectory);

    /// <summary>Reads a text file (or standard input for "-").</summary>
    public string ReadText(string path, string what)
    {
        if (path == "-")
            return this.Environment.Input().ReadToEnd();
        var full = this.ResolvePath(path);
        if (!File.Exists(full))
            throw new FailureException(Directory.Exists(full) ? $"{what} {path} is a directory, not a file." : $"{what} not found: {full}");
        return File.ReadAllText(full);
    }

    /// <summary>Writes UTF-8 text (no byte-order mark) to a file, creating its directory; "-" writes to the output.</summary>
    public void WriteText(string path, string text)
    {
        if (path == "-")
        {
            this.Out.Write(text);
            return;
        }

        var full = this.ResolvePath(path);
        try
        {
            if (Path.GetDirectoryName(full) is { Length: > 0 } dir)
                Directory.CreateDirectory(dir);
            File.WriteAllText(full, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FailureException($"could not write {full}: {ex.Message}");
        }
    }

    /// <summary>Reads a plan in any form (YAML, JSON or a share code) from a file or "-".</summary>
    public MitPlan ReadPlan(string path)
    {
        var text = this.ReadText(path, "plan");
        try
        {
            return PlanSerializer.Parse(text);
        }
        catch (FormatException ex)
        {
            throw new FailureException($"{(path == "-" ? "standard input" : path)} is not a readable plan: {ex.Message}");
        }
    }

    private static T Load<T>(string what, Func<T> load)
    {
        try
        {
            return load();
        }
        catch (Exception ex) when (ex is FormatException or IOException or ArgumentException)
        {
            throw new FailureException($"could not load {what}: {ex.Message} (run 'mitcue validate' for details)");
        }
    }
}
