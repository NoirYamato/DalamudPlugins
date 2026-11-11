using System.Net;
using MitCue.Core.Actions;
using MitCue.Core.Data;
using MitCue.Core.Fights;
using MitCue.Core.Import;

namespace MitCue.Cli.Tests;

/// <summary>The result of one in-process CLI run.</summary>
internal sealed record CliResult(int ExitCode, string Out, string Err)
{
    public string[] OutLines => this.Out.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    public override string ToString() => $"exit {this.ExitCode}\n--- stdout\n{this.Out}\n--- stderr\n{this.Err}";
}

/// <summary>Runs the CLI in process against the repository's data, with a temporary working directory.</summary>
internal sealed class CliKit : IDisposable
{
    private static readonly Lazy<DirectoryDataSource> RepoData = new(() => DirectoryDataSource.FindRepoData());

    public CliKit()
    {
        this.WorkDir = Path.Combine(Path.GetTempPath(), "mitcue-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.WorkDir);
    }

    /// <summary>The repository root (parent of data/).</summary>
    public static string RepoRoot => Path.GetDirectoryName(RepoData.Value.Root)!;

    /// <summary>tests/fixtures/sheets.</summary>
    public static string Fixtures => Path.Combine(RepoRoot, "tests", "fixtures", "sheets");

    /// <summary>A scratch directory, the working directory of every run; deleted on dispose.</summary>
    public string WorkDir { get; }

    /// <summary>Text standard input gives to the next runs.</summary>
    public string Input { get; set; } = string.Empty;

    /// <summary>Answers HTTP requests of the next runs; null = no network access (the test fails if one is made).</summary>
    public Func<HttpRequestMessage, HttpResponseMessage>? Http { get; set; }

    /// <summary>Requests made through <see cref="Http"/>.</summary>
    public List<Uri> Requests { get; } = [];

    public CliResult Run(params string[] args)
    {
        var output = new StringWriter { NewLine = "\n" };
        var error = new StringWriter { NewLine = "\n" };
        var environment = new CliEnvironment
        {
            CurrentDirectory = this.WorkDir,
            ProgramDirectory = AppContext.BaseDirectory,
            Input = () => new StringReader(this.Input),
            HttpHandler = () => new FakeHandler(this),
            Today = () => "2026-11-11",
        };
        var code = Cli.Run(args, output, error, environment);
        return new CliResult(code, output.ToString(), error.ToString());
    }

    /// <summary>Writes a file under <see cref="WorkDir"/> and returns its full path.</summary>
    public string Write(string name, string text)
    {
        var path = Path.Combine(this.WorkDir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(this.WorkDir, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    private sealed class FakeHandler(CliKit kit) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            kit.Requests.Add(request.RequestUri!);
            if (kit.Http == null)
                throw new InvalidOperationException("Unexpected HTTP request in a test: " + request.RequestUri);
            return Task.FromResult(kit.Http(request));
        }
    }
}

/// <summary>
/// A fact that is skipped while the sheet readers or <see cref="PlanImporter.Import"/> still throw
/// <see cref="NotImplementedException"/> (they are implemented in parallel with the CLI).
/// </summary>
public sealed class ImporterFactAttribute : FactAttribute
{
    private static readonly Lazy<string?> Missing = new(Probe);

    public ImporterFactAttribute()
    {
        if (Missing.Value is { } why)
            this.Skip = why;
    }

    private static string? Probe()
    {
        try
        {
            SheetReaders.FromDelimited("a,b", ',', "probe");
        }
        catch (NotImplementedException)
        {
            return "The sheet readers are not implemented in this build yet.";
        }
        catch (Exception)
        {
            // implemented (the probe input may simply be unwelcome)
        }

        try
        {
            var data = DirectoryDataSource.FindRepoData();
            var fight = new FightLibrary(data).Get("top")!;
            var actions = ActionCatalog.Load(data.ReadText("actions/actions.yaml"));
            var aliases = AliasDictionary.Load(data.ReadText("actions/aliases.yaml"), actions);
            new PlanImporter().Import(new SheetDocument { Title = "probe", Tabs = [] }, new ImportOptions { Fight = fight, Actions = actions, Aliases = aliases });
        }
        catch (NotImplementedException)
        {
            return "PlanImporter.Import is not implemented in this build yet.";
        }
        catch (Exception)
        {
            // implemented
        }

        return null;
    }
}

/// <summary>HTTP responses for download tests.</summary>
internal static class Responses
{
    public static HttpResponseMessage Status(HttpStatusCode code, string mediaType = "text/html", string body = "<html></html>")
    {
        var response = new HttpResponseMessage(code) { Content = new StringContent(body) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
        return response;
    }

    public static HttpResponseMessage Xlsx(byte[] bytes, string fileName)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment") { FileNameStar = fileName };
        return response;
    }
}
