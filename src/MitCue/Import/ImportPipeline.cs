using System.Globalization;
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

    /// <summary>A problem the player should act on (e.g. two tabs pasted into one box), shown when the source loads.</summary>
    public string? Warning { get; init; }

    /// <summary>Human description: "Google Sheet "Title" (3 tabs)", "pasted cells (2 tabs: Pasted, P3)".</summary>
    public string Describe()
    {
        var tabs = this.Document.Tabs.Count;
        var count = $"{tabs} tab{(tabs == 1 ? string.Empty : "s")}";
        if (this.Kind is not ("google-sheet" or "google-doc"))
            return tabs > 1 ? $"pasted cells ({count}: {string.Join(", ", this.Document.Tabs.Select(t => t.Name))})" : "pasted cells";
        var kind = this.Kind == "google-sheet" ? "Google Sheet" : "Google Doc";
        return $"{kind} \"{this.Document.Title}\" ({count})";
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
    /// <summary>
    /// Downloads a link and reads it into a document. A cancelled download ends with an
    /// <see cref="OperationCanceledException"/> (a cancelled task, not a failed one).
    /// </summary>
    public static async Task<LoadedSource> LoadLinkAsync(SheetFetcher fetcher, GoogleLink link, CancellationToken cancel)
    {
        var result = await fetcher.FetchAsync(link, cancel).ConfigureAwait(false);
        ThrowIfCancelled(result, cancel);
        if (!link.IsSheet)
            return await LoadDocAsync(fetcher, link, result, cancel).ConfigureAwait(false);

        if (!result.Ok || result.Bytes == null)
            throw new ImportStepException(result.Message);

        var title = result.Title ?? "Google Sheet";
        var workbook = Guard("Reading .xlsx workbooks", () => SheetReaders.FromXlsx(new MemoryStream(result.Bytes, writable: false), title));
        if (workbook.Tabs.Count == 0)
            throw new ImportStepException("The spreadsheet has no tabs.");

        string? linkedTab = null;
        if (link.Gid != null && workbook.Tabs.Count > 1)
            linkedTab = await FindLinkedTabAsync(fetcher, link, workbook, result.Title, cancel).ConfigureAwait(false);
        else if (workbook.Tabs.Count == 1)
            linkedTab = workbook.Tabs[0].Name;
        cancel.ThrowIfCancellationRequested();

        // The xlsx export renames the tabs Excel does not allow ("P1/P2" becomes "P1P2"), and the names are the plan's
        // provenance and its column names: restore Google's names, as the CLI does. The linked tab was found by the
        // export's names (the one-tab export uses them too), so it is renamed with the others.
        if (await OriginalTabNamesAsync(fetcher, link, cancel).ConfigureAwait(false) is { } names)
        {
            var renamed = WithTabNames(workbook, names);
            var linkedIndex = linkedTab == null ? -1 : workbook.Tabs.ToList().FindIndex(t => t.Name == linkedTab);
            linkedTab = linkedIndex >= 0 ? renamed.Tabs[linkedIndex].Name : linkedTab;
            workbook = renamed;
        }

        return new LoadedSource { Document = workbook, Kind = "google-sheet", Url = link.BrowserUrl, LinkedTab = linkedTab };
    }

    /// <summary>
    /// The tab names as Google shows them, from the spreadsheet's htmlview page (<see cref="GoogleHtmlView"/>). Best
    /// effort: null when the page cannot be read or lists no tabs (the export's names are kept); cancelling still cancels.
    /// </summary>
    private static async Task<IReadOnlyList<string>?> OriginalTabNamesAsync(SheetFetcher fetcher, GoogleLink link, CancellationToken cancel)
    {
        if (GoogleLinks.HtmlViewUrl(link) is not { } url)
            return null;
        try
        {
            var page = await fetcher.FetchUrlAsync(url, ExpectedContent.Html, "tab list", cancel).ConfigureAwait(false);
            ThrowIfCancelled(page, cancel);
            if (!page.Ok || page.Bytes == null)
                return null;
            var names = GoogleHtmlView.TabNames(Encoding.UTF8.GetString(page.Bytes));
            return names.Count > 0 ? names : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// The document with each tab named <c>names[tab.Index]</c> (the workbook's sheet order, as
    /// <see cref="XlsxReadOptions.TabNames"/> applies them), when that name is the one the export changed: the same once
    /// the characters Excel does not allow are removed (and cut at 31 characters). Other, missing or blank names keep the
    /// export's name, so a tab list that does not line up with the workbook never mislabels a tab.
    /// </summary>
    internal static SheetDocument WithTabNames(SheetDocument document, IReadOnlyList<string> names)
    {
        var tabs = document.Tabs.Select(t =>
            t.Index >= 0 && t.Index < names.Count && !string.IsNullOrWhiteSpace(names[t.Index]) && names[t.Index] != t.Name
            && ExcelTabName(names[t.Index]) == ExcelTabName(t.Name)
                ? new SheetTab { Name = names[t.Index], Gid = t.Gid, Index = t.Index, Hidden = t.Hidden, Rows = t.Rows, Merges = t.Merges, HiddenRows = t.HiddenRows, HiddenCols = t.HiddenCols }
                : t).ToList();

        // Two tabs must never share a name (the tab choices and the provenance use names): then keep the export's names.
        return tabs.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == tabs.Count
            ? new SheetDocument { Title = document.Title, Tabs = tabs }
            : document;
    }

    /// <summary>Name of the first pasted tab when the player gives none.</summary>
    public const string PastedTabName = "Pasted";

    /// <summary>
    /// Reads cells pasted from Google Sheets (TSV) or CSV: as a new one-tab document, or as one more tab of
    /// <paramref name="addTo"/> (a plan spread over several tabs is pasted one tab at a time).
    /// </summary>
    /// <param name="text">The pasted text.</param>
    /// <param name="separator">Tab or comma.</param>
    /// <param name="addTo">Earlier pasted cells to add this tab to, or null for a new document.</param>
    /// <param name="tabName">The tab's name (e.g. "P3"), or null/empty for "Pasted", "Pasted 2", ...</param>
    public static LoadedSource LoadPasted(string text, char separator, LoadedSource? addTo = null, string? tabName = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ImportStepException("Nothing to read: paste the cells first.");
        var name = UniqueTabName(PastedTabs(addTo), tabName);
        var tab = Guard("Reading pasted cells", () => SheetReaders.FromDelimited(text, separator, name));
        return PastedSource(tab, addTo);
    }

    /// <summary>The source made of a pasted tab, alone or after the tabs of <paramref name="addTo"/>.</summary>
    internal static LoadedSource PastedSource(SheetTab tab, LoadedSource? addTo)
    {
        if (tab.Rows.Count == 0 || tab.Rows.All(r => r.All(c => c.IsEmpty)))
            throw new ImportStepException("The pasted text has no cells.");
        var doc = new SheetDocument { Title = "Pasted cells", Tabs = [.. PastedTabs(addTo), tab] };
        return new LoadedSource
        {
            Document = doc,
            Kind = "paste",
            LinkedTab = doc.Tabs.Count == 1 ? tab.Name : null,
            Note = "Pasted cells carry no colours, so grey (optional) calls are not recognised: mark them in the review, or import from the link instead.",
            Warning = SeveralTabsWarning(tab),
        };
    }

    /// <summary>
    /// A warning when one paste holds the header rows of several tabs (the next tab pasted below the first): the rows
    /// above the last header row would not be read against their own columns.
    /// </summary>
    internal static string? SeveralTabsWarning(SheetTab tab)
    {
        var headers = ImportAnalysis.PlanHeaderRows(tab);
        if (headers.Count < 2)
            return null;
        var rows = string.Join(", ", headers.Select(r => (r + 1).ToString(CultureInfo.InvariantCulture)));
        return $"The pasted cells have {headers.Count.ToString(CultureInfo.InvariantCulture)} header rows (rows {rows}): it looks like more than one tab was pasted at once. " +
               "Paste one tab at a time: \"Read the cells\" for the first tab, then \"Add as another tab\" for each of the others.";
    }

    private static IReadOnlyList<SheetTab> PastedTabs(LoadedSource? source) => source is { Kind: "paste" } ? source.Document.Tabs : [];

    /// <summary>The name for a new pasted tab: the given one (made unique), else "Pasted", "Pasted 2", ...</summary>
    internal static string UniqueTabName(IReadOnlyList<SheetTab> earlier, string? wanted)
    {
        var taken = earlier.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = wanted?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            name = PastedTabName;
            for (var n = 2; taken.Contains(name); n++)
                name = PastedTabName + " " + n.ToString(CultureInfo.InvariantCulture);
            return name;
        }

        var unique = name;
        for (var n = 2; taken.Contains(unique); n++)
            unique = $"{name} ({n.ToString(CultureInfo.InvariantCulture)})";
        return unique;
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
        ReviewValidator.Repair(plan);
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
        var sanitized = ExcelTabName(name);
        var match = workbook.Tabs.Where(t => ExcelTabName(t.Name) == sanitized).ToList();
        if (match.Count == 1)
            return match[0].Name;

        // The workbook title may itself contain " - ": take the longest tab name the file name ends with.
        var file = WithoutExcelInvalidCharacters(oneTabFileTitle);
        return workbook.Tabs
            .Where(t => file.EndsWith(" - " + WithoutExcelInvalidCharacters(t.Name), StringComparison.Ordinal))
            .OrderByDescending(t => t.Name.Length)
            .Select(t => t.Name)
            .FirstOrDefault();
    }

    /// <summary>A tab name as an xlsx export can have it, for comparisons: without \ / ? * [ ] :, cut at 31 characters, upper case.</summary>
    private static string ExcelTabName(string name)
    {
        var cleaned = WithoutExcelInvalidCharacters(name);
        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }

    private static string WithoutExcelInvalidCharacters(string s) =>
        new string(s.Where(c => c is not ('\\' or '/' or '?' or '*' or '[' or ']' or ':')).ToArray()).Trim().ToUpperInvariant();

    /// <summary>
    /// Reads a Google Doc from its web page export, or, when that is too large or too slow to download (the export
    /// inlines every image, so a document with screenshots can be tens of MB although its text is small), from its
    /// plain-text export.
    /// </summary>
    private static async Task<LoadedSource> LoadDocAsync(SheetFetcher fetcher, GoogleLink link, FetchResult html, CancellationToken cancel)
    {
        if (html.Ok && html.Bytes != null)
        {
            var text = Encoding.UTF8.GetString(html.Bytes);
            var doc = Guard("Reading Google Docs", () => DocHtmlReader.Read(text, html.Title));
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

        // Published-to-web documents have no plain-text export.
        if (html.Status is not (FetchStatus.TooLarge or FetchStatus.Timeout) || link.Published)
            throw new ImportStepException(html.Message);

        var plain = await fetcher.FetchUrlAsync(GoogleLinks.DocTextUrl(link), ExpectedContent.Text, "document", cancel).ConfigureAwait(false);
        ThrowIfCancelled(plain, cancel);
        if (!plain.Ok || plain.Bytes == null)
            throw new ImportStepException($"{html.Message} (Its plain text could not be downloaded either: {plain.Message})");

        var body = Encoding.UTF8.GetString(plain.Bytes);
        var textDoc = Guard("Reading Google Docs", () => DocTextReader.Read(body, plain.Title ?? html.Title));
        if (textDoc.Tabs.Count == 0)
            throw new ImportStepException("The document has no text.");
        var why = html.Status == FetchStatus.TooLarge ? "too large to download as a web page" : "too slow to download as a web page";
        return new LoadedSource
        {
            Document = textDoc,
            Kind = "google-doc",
            Url = link.BrowserUrl,
            Note = "Read from the document's plain text: one row per line (cells of a table row are split at its tabs); " +
                   "tables lose their layout and colours are not kept.",
            Warning = $"The document is {why} (its images count too), so MitCue read its plain text instead, where tables lose their layout. " +
                      "If the calls are not read right, use the AI route (\"Not reading it right?\" in the next step).",
        };
    }

    /// <summary>A cancelled download ends the step as cancelled (not failed), so a dropped job leaves no exception behind.</summary>
    private static void ThrowIfCancelled(FetchResult result, CancellationToken cancel)
    {
        if (result.Status == FetchStatus.Cancelled || cancel.IsCancellationRequested)
            throw new OperationCanceledException(result.Message, cancel);
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
