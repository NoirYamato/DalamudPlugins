using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using MitCue.Core.Import;

namespace MitCue.Cli.Import;

/// <summary>A sheet loaded for import, with where it came from.</summary>
/// <param name="Document">The sheet.</param>
/// <param name="Kind">"google-sheet link", "xlsx file", "fixture directory", "fixture file", "csv file" or "tsv file".</param>
/// <param name="Url">The link, for a downloaded sheet.</param>
/// <param name="Bytes">The downloaded workbook, for a downloaded sheet.</param>
internal sealed record LoadedSheet(SheetDocument Document, string Kind, string? Url = null, byte[]? Bytes = null);

/// <summary>A Google Sheets link: the spreadsheet id (or published id) and the tab gid, if any.</summary>
internal sealed partial record GoogleSheetLink(string Id, long? Gid, bool Published)
{
    public const string XlsxMediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>The whole workbook as xlsx (docs/research/sheets-import.md §1).</summary>
    public string ExportUrl => this.Published
        ? $"https://docs.google.com/spreadsheets/d/e/{this.Id}/pub?output=xlsx"
        : $"https://docs.google.com/spreadsheets/d/{this.Id}/export?format=xlsx";

    /// <summary>
    /// Reads <c>https://docs.google.com/spreadsheets/d/&lt;id&gt;/…</c> (also <c>/u/0/d/…</c>), with an optional
    /// <c>gid=</c> in the query or the fragment, and published links <c>…/d/e/&lt;id&gt;/pubhtml</c>.
    /// </summary>
    public static bool TryParse(string text, out GoogleSheetLink link)
    {
        link = null!;
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return false;
        if (!uri.Host.Equals("docs.google.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var m = SheetPath().Match(uri.AbsolutePath);
        if (!m.Success)
            return false;

        long? gid = null;
        foreach (var part in new[] { uri.Query, uri.Fragment })
        {
            var g = GidParameter().Match(part);
            if (g.Success && long.TryParse(g.Groups["gid"].Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value))
                gid = value;
        }

        link = m.Groups["pub"].Success
            ? new GoogleSheetLink(m.Groups["pub"].Value, gid, Published: true)
            : new GoogleSheetLink(m.Groups["id"].Value, gid, Published: false);
        return true;
    }

    [GeneratedRegex(@"^/spreadsheets/(?:u/\d+/)?d/(?:e/(?<pub>[A-Za-z0-9_-]+)|(?<id>[A-Za-z0-9_-]{10,}))(?:/|$)")]
    private static partial Regex SheetPath();

    [GeneratedRegex(@"(?:^|[?#&])gid=(?<gid>\d+)")]
    private static partial Regex GidParameter();
}

/// <summary>Turns the <c>import</c> source argument into a <see cref="SheetDocument"/>.</summary>
internal static class SheetSource
{
    /// <summary>Largest workbook the CLI downloads (the example sheets are under 0.5 MB).</summary>
    private const long MaxDownloadBytes = 50L * 1024 * 1024;

    public static LoadedSheet Load(CliContext ctx, string source)
    {
        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (GoogleSheetLink.TryParse(source, out var link))
                return Download(ctx, link, source);
            if (source.Contains("docs.google.com/document/", StringComparison.OrdinalIgnoreCase))
                throw new FailureException("Google Docs links are not supported by the CLI: copy the table into a sheet, or save it as .csv/.tsv and import that file.");
            throw new UsageException($"import: '{source}' is not a Google Sheets link (https://docs.google.com/spreadsheets/d/<id>/...).");
        }

        var path = ctx.ResolvePath(source);
        if (Directory.Exists(path))
        {
            var title = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            if (!Directory.EnumerateFiles(path, "*.json").Any())
                throw new FailureException($"{path} has no *.json fixture tabs (expected a folder like tests/fixtures/sheets/lpdu).");
            return new LoadedSheet(SheetReaders.FromFixtureDirectory(path, title), "fixture directory");
        }

        if (!File.Exists(path))
            throw new FailureException($"source not found: {path}");

        var name = Path.GetFileNameWithoutExtension(path);
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".xlsx":
                using (var stream = File.OpenRead(path))
                    return new LoadedSheet(SheetReaders.FromXlsx(stream, name), "xlsx file");
            case ".json":
                return new LoadedSheet(new SheetDocument { Title = name, Tabs = [SheetReaders.FromFixtureJson(File.ReadAllText(path))] }, "fixture file");
            case ".csv":
                return new LoadedSheet(new SheetDocument { Title = name, Tabs = [SheetReaders.FromDelimited(File.ReadAllText(path), ',', name)] }, "csv file");
            case ".tsv":
            case ".tab":
                return new LoadedSheet(new SheetDocument { Title = name, Tabs = [SheetReaders.FromDelimited(File.ReadAllText(path), '\t', name)] }, "tsv file");
            case ".txt":
                var text = File.ReadAllText(path);
                var separator = text.Contains('\t') ? '\t' : ',';
                return new LoadedSheet(new SheetDocument { Title = name, Tabs = [SheetReaders.FromDelimited(text, separator, name)] }, separator == '\t' ? "tsv file" : "csv file");
            default:
                throw new UsageException($"import: unsupported source '{source}': use an .xlsx, .csv, .tsv or fixture .json file, a fixture folder, or a Google Sheets link.");
        }
    }

    /// <summary>
    /// Matches tab names given on the command line to the document's tabs: exactly (ignoring case), else ignoring
    /// everything but letters and digits, because the xlsx export renames tabs ("P1/P2" becomes "P1P2").
    /// </summary>
    public static IReadOnlyList<string>? MatchTabs(SheetDocument document, IReadOnlyList<string>? requested)
    {
        if (requested == null)
            return null;
        var result = new List<string>();
        foreach (var name in requested)
        {
            var tab = document.Tabs.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                      ?? document.Tabs.FirstOrDefault(t => Compact(t.Name) == Compact(name) && Compact(name).Length > 0)
                      ?? throw new UsageException($"import: no tab '{name}' in \"{document.Title}\". Tabs: {string.Join(", ", document.Tabs.Select(t => "\"" + t.Name + "\""))}.");
            if (!result.Contains(tab.Name))
                result.Add(tab.Name);
        }

        return result;
    }

    private static string Compact(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static LoadedSheet Download(CliContext ctx, GoogleSheetLink link, string original)
    {
        var url = link.ExportUrl;
        ctx.Err.WriteLine($"Downloading {url} ...");
        using var client = new HttpClient(ctx.Environment.HttpHandler(), disposeHandler: true) { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MitCue-CLI", "1.0"));

        HttpResponseMessage response;
        try
        {
            response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new FailureException($"download failed: {ex.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var hint = response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? " Check the link, and that the sheet is shared as \"Anyone with the link\" (File > Share)."
                    : string.Empty;
                throw new FailureException($"download failed: {(int)response.StatusCode} {response.ReasonPhrase}.{hint}");
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!string.Equals(mediaType, GoogleSheetLink.XlsxMediaType, StringComparison.OrdinalIgnoreCase))
            {
                throw new FailureException(
                    $"the link did not return a spreadsheet (got {mediaType ?? "no content type"}): the sheet is probably not shared publicly. " +
                    "Share it as \"Anyone with the link\" (File > Share), or download it as .xlsx and import the file.");
            }

            if (response.Content.Headers.ContentLength is > MaxDownloadBytes)
                throw new FailureException($"the workbook is larger than {MaxDownloadBytes / (1024 * 1024)} MB.");
            var bytes = ReadBounded(response.Content);
            var title = TitleFrom(response.Content.Headers.ContentDisposition) ?? "Google Sheet " + link.Id;
            using var stream = new MemoryStream(bytes, writable: false);
            return new LoadedSheet(SheetReaders.FromXlsx(stream, title), "google-sheet link", original, bytes);
        }
    }

    /// <summary>The body, at most <see cref="MaxDownloadBytes"/> (also when the server sends no length).</summary>
    private static byte[] ReadBounded(HttpContent content)
    {
        try
        {
            using var stream = content.ReadAsStream();
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + read > MaxDownloadBytes)
                    throw new FailureException($"the workbook is larger than {MaxDownloadBytes / (1024 * 1024)} MB.");
                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new FailureException($"download failed: {ex.Message}");
        }
    }

    /// <summary>The spreadsheet title from <c>Content-Disposition</c> (RFC 5987 <c>filename*</c> first), without ".xlsx".</summary>
    private static string? TitleFrom(ContentDispositionHeaderValue? disposition)
    {
        var name = disposition?.FileNameStar ?? disposition?.FileName;
        if (string.IsNullOrWhiteSpace(name))
            return null;
        name = name.Trim().Trim('"');
        return name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? name[..^5] : name;
    }
}
