using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MitCue.Import;

/// <summary>What a Google link points to.</summary>
internal enum GoogleFileKind
{
    /// <summary>A Google Sheets spreadsheet (<c>/spreadsheets/d/&lt;id&gt;</c>).</summary>
    Sheet,

    /// <summary>A Google Docs document (<c>/document/d/&lt;id&gt;</c>).</summary>
    Doc,
}

/// <summary>A parsed Google Sheets or Docs link.</summary>
/// <param name="Kind">Spreadsheet or document.</param>
/// <param name="Id">The file id (case-sensitive), or the "2PACX-…" id of a published-to-web link.</param>
/// <param name="Gid">The tab id from <c>gid=</c> (sheets only), if the link had one.</param>
/// <param name="Published">True for "published to the web" links (<c>/d/e/&lt;id&gt;/pubhtml</c>).</param>
internal sealed record GoogleLink(GoogleFileKind Kind, string Id, long? Gid = null, bool Published = false)
{
    /// <summary>True for spreadsheets.</summary>
    public bool IsSheet => this.Kind == GoogleFileKind.Sheet;

    /// <summary>The link a person opens in a browser (stored as the plan's source URL).</summary>
    public string BrowserUrl => GoogleLinks.BrowserUrl(this);
}

/// <summary>
/// Parses Google Sheets / Docs links in the shapes people paste (address bar, "Copy link", account-indexed
/// <c>/u/0/</c> links, <c>htmlview</c>, <c>?gid=</c> / <c>#gid=</c>, published-to-web links, bare ids) and builds
/// the export URLs MitCue downloads (docs/research/sheets-import.md §1 and §5).
/// </summary>
internal static partial class GoogleLinks
{
    /// <summary>Host of every Sheets/Docs URL.</summary>
    public const string Host = "docs.google.com";

    private const string Base = "https://" + Host;

    /// <summary>Parses a link (or a bare spreadsheet id).</summary>
    /// <param name="text">What the user pasted; surrounding spaces, quotes and angle brackets are ignored.</param>
    /// <param name="link">The parsed link.</param>
    /// <param name="error">Why the text is not a usable link (user-facing), when false is returned.</param>
    /// <returns>True when <paramref name="text"/> is a Google Sheets/Docs link.</returns>
    public static bool TryParse(string? text, [NotNullWhen(true)] out GoogleLink? link, out string error)
    {
        link = null;
        error = string.Empty;
        var s = (text ?? string.Empty).Trim().Trim('<', '>', '"', '\'', '`', '(', ')').Trim();
        if (s.Length == 0)
        {
            error = "Paste the link of a Google Sheet or Google Doc.";
            return false;
        }

        if (BareIdRegex().IsMatch(s))
        {
            // A bare id: spreadsheets are what plans are written in.
            link = new GoogleLink(GoogleFileKind.Sheet, s);
            return true;
        }

        if (!s.Contains("://", StringComparison.Ordinal))
            s = "https://" + s;

        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            string.IsNullOrEmpty(uri.Host))
        {
            error = "That does not look like a link. Copy it from the browser's address bar, or use Share > Copy link in Google Sheets.";
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host is "drive.google.com" or "drive.usercontent.google.com")
        {
            error = "This is a Google Drive file link. Open the file in Google Sheets or Docs and copy the link from the address bar (or use Share > Copy link there).";
            return false;
        }

        if (host != Host)
        {
            error = $"Only Google Sheets and Google Docs links ({Host}) are supported, not {uri.Host}.";
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var i = 0;

        // Google Workspace links: /a/<domain>/spreadsheets/d/<id>/...
        if (segments.Length > 2 && Is(segments[0], "a"))
            i = 2;

        if (i >= segments.Length)
        {
            error = "The link does not point at a spreadsheet or document.";
            return false;
        }

        GoogleFileKind kind;
        var type = segments[i];
        if (Is(type, "spreadsheets"))
        {
            kind = GoogleFileKind.Sheet;
        }
        else if (Is(type, "document"))
        {
            kind = GoogleFileKind.Doc;
        }
        else if (Is(type, "spreadsheet"))
        {
            // Very old links: /spreadsheet/ccc?key=<id>#gid=0
            var key = QueryValue(uri.Query, "key");
            if (key != null && IdRegex().IsMatch(key))
            {
                link = new GoogleLink(GoogleFileKind.Sheet, key, ParseGid(uri.Fragment) ?? ParseGid(uri.Query));
                return true;
            }

            error = "This old-style spreadsheet link has no file id. Open it in the browser and copy the new link from the address bar.";
            return false;
        }
        else if (Is(type, "presentation") || Is(type, "forms"))
        {
            error = "Only Google Sheets and Google Docs can be imported, not Slides or Forms.";
            return false;
        }
        else
        {
            error = "The link does not point at a Google Sheet or Google Doc.";
            return false;
        }

        i++;

        // Account-indexed links: /spreadsheets/u/0/d/<id>/...
        if (i + 1 < segments.Length && Is(segments[i], "u") && segments[i + 1].All(char.IsAsciiDigit))
            i += 2;

        if (i >= segments.Length || !Is(segments[i], "d"))
        {
            error = "The link has no file id. Open the file and copy the link from the address bar.";
            return false;
        }

        i++;
        var published = false;
        if (i < segments.Length && Is(segments[i], "e"))
        {
            published = true;
            i++;
        }

        if (i >= segments.Length || !IdRegex().IsMatch(segments[i]))
        {
            error = "The link has no valid file id. Copy the whole link from the address bar.";
            return false;
        }

        var id = segments[i];
        long? gid = kind == GoogleFileKind.Sheet ? ParseGid(uri.Fragment) ?? ParseGid(uri.Query) : null;
        link = new GoogleLink(kind, id, gid, published);
        return true;
    }

    /// <summary>Parses a link; null when it is not a Google Sheets/Docs link.</summary>
    public static GoogleLink? Parse(string? text) => TryParse(text, out var link, out _) ? link : null;

    /// <summary>
    /// Export URL of a spreadsheet: <c>/spreadsheets/d/&lt;id&gt;/export?format=&lt;format&gt;</c>, optionally for one tab
    /// (<c>&amp;gid=</c>; without it xlsx is the whole workbook and csv/tsv the first tab).
    /// </summary>
    public static string SheetExportUrl(string id, string format = "xlsx", long? gid = null)
    {
        var url = $"{Base}/spreadsheets/d/{Uri.EscapeDataString(id)}/export?format={Uri.EscapeDataString(format)}";
        return gid is { } g ? url + "&gid=" + g.ToString(CultureInfo.InvariantCulture) : url;
    }

    /// <summary>Export URL of a published-to-web spreadsheet: <c>/spreadsheets/d/e/&lt;id&gt;/pub?output=&lt;format&gt;</c>.</summary>
    public static string PublishedSheetExportUrl(string id, string format = "xlsx", long? gid = null)
    {
        var url = $"{Base}/spreadsheets/d/e/{Uri.EscapeDataString(id)}/pub?output={Uri.EscapeDataString(format)}";
        return gid is { } g ? url + "&single=true&gid=" + g.ToString(CultureInfo.InvariantCulture) : url;
    }

    /// <summary>Export URL of a document: <c>/document/d/&lt;id&gt;/export?format=&lt;format&gt;</c> (txt, html, docx, md, ...).</summary>
    public static string DocExportUrl(string id, string format) =>
        $"{Base}/document/d/{Uri.EscapeDataString(id)}/export?format={Uri.EscapeDataString(format)}";

    /// <summary>URL of a published-to-web document (an HTML page).</summary>
    public static string PublishedDocUrl(string id) => $"{Base}/document/d/e/{Uri.EscapeDataString(id)}/pub";

    /// <summary>The whole-workbook xlsx export of a spreadsheet link.</summary>
    public static string WorkbookXlsxUrl(GoogleLink link)
    {
        if (!link.IsSheet)
            throw new ArgumentException("Not a spreadsheet link.", nameof(link));
        return link.Published ? PublishedSheetExportUrl(link.Id) : SheetExportUrl(link.Id);
    }

    /// <summary>The one-tab xlsx export of the tab the link points at, or null when the link has no gid.</summary>
    public static string? LinkedTabXlsxUrl(GoogleLink link) =>
        link.IsSheet && link.Gid is { } gid
            ? link.Published ? PublishedSheetExportUrl(link.Id, "xlsx", gid) : SheetExportUrl(link.Id, "xlsx", gid)
            : null;

    /// <summary>The plain-text export of a document link (UTF-8 with BOM, CRLF; tables flattened with tabs).</summary>
    public static string DocTextUrl(GoogleLink link)
    {
        if (link.IsSheet)
            throw new ArgumentException("Not a document link.", nameof(link));
        return link.Published ? PublishedDocUrl(link.Id) : DocExportUrl(link.Id, "txt");
    }

    /// <summary>The HTML export of a document link (keeps tables as tables).</summary>
    public static string DocHtmlUrl(GoogleLink link)
    {
        if (link.IsSheet)
            throw new ArgumentException("Not a document link.", nameof(link));
        return link.Published ? PublishedDocUrl(link.Id) : DocExportUrl(link.Id, "html");
    }

    /// <summary>What MitCue downloads for a link: the whole workbook as xlsx for sheets, the HTML export for docs.</summary>
    public static string DownloadUrl(GoogleLink link) => link.IsSheet ? WorkbookXlsxUrl(link) : DocHtmlUrl(link);

    /// <summary>The link a person opens in a browser, e.g. <c>https://docs.google.com/spreadsheets/d/&lt;id&gt;/edit#gid=0</c>.</summary>
    public static string BrowserUrl(GoogleLink link)
    {
        var id = Uri.EscapeDataString(link.Id);
        if (link.IsSheet)
        {
            if (link.Published)
                return $"{Base}/spreadsheets/d/e/{id}/pubhtml";
            return link.Gid is { } gid
                ? $"{Base}/spreadsheets/d/{id}/edit#gid={gid.ToString(CultureInfo.InvariantCulture)}"
                : $"{Base}/spreadsheets/d/{id}/edit";
        }

        return link.Published ? PublishedDocUrl(link.Id) : $"{Base}/document/d/{id}/edit";
    }

    /// <summary>A short description for the UI, e.g. "Google Sheet 1chKtt…o2X6s, tab gid 777100".</summary>
    public static string Describe(GoogleLink link)
    {
        var what = link.IsSheet ? "Google Sheet" : "Google Doc";
        var text = $"{what} {ShortId(link.Id)}";
        if (link.Published)
            text += " (published to the web)";
        if (link.Gid is { } gid)
            text += ", tab gid " + gid.ToString(CultureInfo.InvariantCulture);
        return text;
    }

    /// <summary>"1chKtt…o2X6s" for long ids.</summary>
    public static string ShortId(string id) => id.Length <= 14 ? id : id[..6] + "…" + id[^5..];

    /// <summary>The gid in a query string or fragment ("?gid=12&amp;x" / "#gid=12&amp;range=A1"), or null.</summary>
    internal static long? ParseGid(string? part)
    {
        var value = QueryValue(part, "gid");
        return value != null && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var gid) ? gid : null;
    }

    /// <summary>The value of <paramref name="key"/> in "?a=1&amp;b=2" or "#a=1&amp;b=2" (unescaped), or null.</summary>
    internal static string? QueryValue(string? part, string key)
    {
        if (string.IsNullOrEmpty(part))
            return null;
        foreach (var pair in part.TrimStart('?', '#').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var name = eq < 0 ? pair : pair[..eq];
            if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                return eq < 0 ? string.Empty : Uri.UnescapeDataString(pair[(eq + 1)..]);
        }

        return null;
    }

    private static bool Is(string segment, string expected) => string.Equals(segment, expected, StringComparison.OrdinalIgnoreCase);

    // Google file ids: letters, digits, '-' and '_' (44 characters today; published ids start with "2PACX-").
    [GeneratedRegex("^[A-Za-z0-9_-]{10,200}$")]
    private static partial Regex IdRegex();

    // A pasted id without a URL around it.
    [GeneratedRegex("^[A-Za-z0-9_-]{25,200}$")]
    private static partial Regex BareIdRegex();
}
