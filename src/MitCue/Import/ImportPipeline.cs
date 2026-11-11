using System.Text;
using MitCue.Core.Fights;
using MitCue.Core.Import;
using MitCue.Core.Plans;

namespace MitCue.Import;

/// <summary>A failure of an import step with a message written for the player.</summary>
internal sealed class ImportStepException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ImportStepException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }

    /// <summary>True when the step failed because that part of MitCue is not built yet.</summary>
    public bool NotAvailable { get; init; }
}

/// <summary>A loaded source document, ready for the options step.</summary>
internal sealed class LoadedSource
{
    /// <summary>The document.</summary>
    public required SheetDocument Document { get; init; }

    /// <summary>Plan source kind: "google-sheet", "google-doc" or "paste".</summary>
    public required string Kind { get; init; }

    /// <summary>The browser link (stored in the plan), if any.</summary>
    public string? Url { get; init; }

    /// <summary>The tab the link pointed at (from its gid), when it could be found.</summary>
    public string? LinkedTab { get; init; }

    /// <summary>Something the player should know about this source (e.g. pasted cells have no colours).</summary>
    public string? Note { get; init; }

    /// <summary>Human description: "Google Sheet "Title" (3 tabs)".</summary>
    public string Describe()
    {
        var kind = this.Kind switch
        {
            "google-sheet" => "Google Sheet",
            "google-doc" => "Google Doc",
            _ => "Pasted cells",
        };
        var tabs = this.Document.Tabs.Count;
        return $"{kind} \"{this.Document.Title}\" ({tabs} tab{(tabs == 1 ? string.Empty : "s")})";
    }
}

/// <summary>
/// The slow import steps, run on a background task by the import window: download and read a link, read pasted cells,
/// the trial import, the real import, and reading plan YAML. Every failure becomes an <see cref="ImportStepException"/>
/// with a player-facing message; a <see cref="NotImplementedException"/> (a part of MitCue.Core that is not written yet)
/// becomes "not available in this build".
/// </summary>
internal static class ImportPipeline
{
    /// <summary>Downloads a link and reads it into a document.</summary>
    public static async Task<LoadedSource> LoadLinkAsync(SheetFetcher fetcher, GoogleLink link, CancellationToken cancel)
    {
        var result = await fetcher.FetchAsync(link, cancel).ConfigureAwait(false);
        if (!result.Ok || result.Bytes == null)
            throw new ImportStepException(result.Message);
        cancel.ThrowIfCancellationRequested();

        if (!link.IsSheet)
        {
            var html = Encoding.UTF8.GetString(result.Bytes);
            var doc = Guard("Reading Google Docs", () => DocHtmlReader.Read(html, result.Title));
            if (doc.Tabs.Count == 0)
                throw new ImportStepException("The document has no text.");
            return new LoadedSource
            {
                Document = doc,
                Kind = "google-doc",
                Url = link.BrowserUrl,
                Note = "Google Docs are read as text: tables become tabs (\"Table 1\", ...) and the other paragraphs a \"Text\" tab. Colours are not kept.",
            };
        }

        var title = result.Title ?? "Google Sheet";
        var workbook = Guard("Reading .xlsx workbooks", () => SheetReaders.FromXlsx(new MemoryStream(result.Bytes, writable: false), title));
        if (workbook.Tabs.Count == 0)
            throw new ImportStepException("The spreadsheet has no tabs.");

        string? linkedTab = null;
        if (link.Gid != null && workbook.Tabs.Count > 1)
            linkedTab = await FindLinkedTabAsync(fetcher, link, workbook, result.Title, cancel).ConfigureAwait(false);
        else if (workbook.Tabs.Count == 1)
            linkedTab = workbook.Tabs[0].Name;

        return new LoadedSource { Document = workbook, Kind = "google-sheet", Url = link.BrowserUrl, LinkedTab = linkedTab };
    }

    /// <summary>Reads cells pasted from Google Sheets (TSV) or CSV.</summary>
    public static LoadedSource LoadPasted(string text, char separator)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ImportStepException("Nothing to read: paste the cells first.");
        var tab = Guard("Reading pasted cells", () => SheetReaders.FromDelimited(text, separator, "Pasted"));
        if (tab.Rows.Count == 0 || tab.Rows.All(r => r.All(c => c.IsEmpty)))
            throw new ImportStepException("The pasted text has no cells.");
        var doc = new SheetDocument { Title = "Pasted cells", Tabs = [tab] };
        return new LoadedSource
        {
            Document = doc,
            Kind = "paste",
            LinkedTab = tab.Name,
            Note = "Pasted cells carry no colours, so grey (optional) calls are not recognised: mark them in the review, or import from the link instead.",
        };
    }

    /// <summary>Guesses the separator of pasted text: tab when any line has a tab, else comma when lines have commas.</summary>
    public static char GuessSeparator(string text)
    {
        if (text.Contains('\t', StringComparison.Ordinal))
            return '\t';
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(20).ToList();
        return lines.Count > 1 && lines.Count(l => l.Contains(',', StringComparison.Ordinal)) * 2 > lines.Count ? ',' : '\t';
    }

    /// <summary>The trial import of every tab and column, for the options step. Never throws: failures give an unavailable analysis.</summary>
    public static ImportAnalysis Analyze(SheetDocument document, ImportOptions options)
    {
        try
        {
            var result = new PlanImporter().Import(document, options);
            return ImportAnalysis.FromResult(document, result);
        }
        catch (Exception ex)
        {
            return ImportAnalysis.Unavailable(document, Explain(ex, "The sheet importer"));
        }
    }

    /// <summary>The real import with the chosen options.</summary>
    public static ImportResult Import(SheetDocument document, ImportOptions options) =>
        Guard("The sheet importer", () => new PlanImporter().Import(document, options));

    /// <summary>Reads plan YAML (an AI answer: code fences and text around it are ignored).</summary>
    public static MitPlan ParseYaml(string answer)
    {
        var yaml = LlmPrompt.ExtractYaml(answer);
        if (yaml.Length == 0)
            throw new ImportStepException("Nothing to read: paste the YAML answer first.");
        var plan = Guard("Reading plan YAML", () => PlanSerializer.FromYaml(yaml));
        if (plan == null)
            throw new ImportStepException("The YAML is empty.");
        PlanValidator.Repair(plan);
        return plan;
    }

    /// <summary>A player-facing explanation of a failed step.</summary>
    /// <param name="ex">The exception.</param>
    /// <param name="what">The step, e.g. "Reading .xlsx workbooks".</param>
    public static string Explain(Exception ex, string what)
    {
        while (ex is AggregateException { InnerException: { } inner })
            ex = inner;
        return ex switch
        {
            ImportStepException step => step.Message,
            NotImplementedException => $"{what} is not available in this build of MitCue yet (that part is still being written). The AI route (\"Paste plan YAML\") works without it.",
            FormatException format => format.Message,
            OperationCanceledException => "Cancelled.",
            _ => $"{what} failed: {ex.GetType().Name}: {ex.Message}",
        };
    }

    /// <summary>Calibration preset key whose words appear in the document title ("Carry the Clear: ..." → carry_the_clear), or null.</summary>
    public static string? GuessCalibration(FightDefinition fight, string? title)
    {
        if (string.IsNullOrWhiteSpace(title) || fight.Calibration.Count == 0)
            return null;
        var normalized = Normalize(title);
        return fight.Calibration.Keys
            .Where(k => normalized.Contains(Normalize(k), StringComparison.Ordinal))
            .OrderByDescending(k => k.Length)
            .FirstOrDefault();

        static string Normalize(string s) =>
            " " + string.Join(' ', new string(s.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ').ToArray())
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)) + " ";
    }

    /// <summary>Matches a tab name from a one-tab export ("Title - Tab.xlsx") against the workbook's (sanitised) tab names.</summary>
    internal static string? MatchTabName(SheetDocument workbook, string? oneTabFileTitle, string? workbookTitle)
    {
        if (string.IsNullOrWhiteSpace(oneTabFileTitle))
            return null;
        var name = oneTabFileTitle.Trim();
        if (!string.IsNullOrWhiteSpace(workbookTitle) && name.StartsWith(workbookTitle + " - ", StringComparison.Ordinal))
            name = name[(workbookTitle.Length + 3)..];

        var exact = workbook.Tabs.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return exact.Name;

        // Excel sheet names cannot hold \ / ? * [ ] : and are cut at 31 characters ("P1/P2" is "P1P2" in the xlsx).
        var sanitized = Sanitize(name);
        var match = workbook.Tabs.Where(t => Sanitize(t.Name) == sanitized).ToList();
        if (match.Count == 1)
            return match[0].Name;

        // The workbook title may itself contain " - ": take the longest tab name the file name ends with.
        var file = Clean(oneTabFileTitle);
        return workbook.Tabs
            .Where(t => file.EndsWith(" - " + Clean(t.Name), StringComparison.Ordinal))
            .OrderByDescending(t => t.Name.Length)
            .Select(t => t.Name)
            .FirstOrDefault();

        static string Clean(string s) =>
            new string(s.Where(c => c is not ('\\' or '/' or '?' or '*' or '[' or ']' or ':')).ToArray()).Trim().ToUpperInvariant();

        static string Sanitize(string s)
        {
            var cleaned = Clean(s);
            return cleaned.Length > 31 ? cleaned[..31] : cleaned;
        }
    }

    private static async Task<string?> FindLinkedTabAsync(SheetFetcher fetcher, GoogleLink link, SheetDocument workbook, string? workbookTitle, CancellationToken cancel)
    {
        // Best effort: the gids are not in the xlsx, but the one-tab export is named "<title> - <tab>.xlsx".
        var url = GoogleLinks.LinkedTabXlsxUrl(link);
        if (url == null)
            return null;
        try
        {
            var one = await fetcher.FetchUrlAsync(url, ExpectedContent.Xlsx, "tab", cancel).ConfigureAwait(false);
            if (!one.Ok || one.Bytes == null)
                return null;
            var byName = MatchTabName(workbook, one.Title, workbookTitle);
            if (byName != null)
                return byName;

            var single = SheetReaders.FromXlsx(new MemoryStream(one.Bytes, writable: false), one.Title ?? "tab");
            return single.Tabs.Count == 1 ? workbook.Tab(single.Tabs[0].Name)?.Name : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static T Guard<T>(string what, Func<T> step)
    {
        try
        {
            return step();
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ImportStepException)
        {
            throw new ImportStepException(Explain(ex, what), ex) { NotAvailable = ex is NotImplementedException };
        }
    }
}
