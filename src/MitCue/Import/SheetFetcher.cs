using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace MitCue.Import;

/// <summary>Outcome of a download.</summary>
internal enum FetchStatus
{
    /// <summary>The file was downloaded and looks right.</summary>
    Ok,

    /// <summary>Google answered with a sign-in page or 401/403: the file is not shared publicly.</summary>
    NotShared,

    /// <summary>404/410: no file with this id.</summary>
    NotFound,

    /// <summary>The file is larger than the size cap.</summary>
    TooLarge,

    /// <summary>No complete answer within the time limit.</summary>
    Timeout,

    /// <summary>The user cancelled.</summary>
    Cancelled,

    /// <summary>Google could not be reached (DNS, connection, TLS, reset).</summary>
    NetworkError,

    /// <summary>Another HTTP error (400, 429, 5xx, ...).</summary>
    HttpError,

    /// <summary>A 200 answer that is not the expected kind of file.</summary>
    UnexpectedContent,
}

/// <summary>What kind of file a request expects (decides how the answer is checked).</summary>
internal enum ExpectedContent
{
    /// <summary>An .xlsx workbook (a zip file).</summary>
    Xlsx,

    /// <summary>An HTML page (Google Docs export or published page).</summary>
    Html,

    /// <summary>Plain text (Google Docs txt export).</summary>
    Text,
}

/// <summary>Result of <see cref="SheetFetcher"/>. Immutable; safe to hand between threads.</summary>
internal sealed class FetchResult
{
    /// <summary>Outcome.</summary>
    public required FetchStatus Status { get; init; }

    /// <summary>User-facing explanation (also set on success).</summary>
    public required string Message { get; init; }

    /// <summary>The URL requested.</summary>
    public required string Url { get; init; }

    /// <summary>The body (only on success).</summary>
    public byte[]? Bytes { get; init; }

    /// <summary>Media type of the answer, e.g. "text/html".</summary>
    public string? ContentType { get; init; }

    /// <summary>File name from Content-Disposition (Google puts the spreadsheet/document title there).</summary>
    public string? FileName { get; init; }

    /// <summary>HTTP status code, when an answer arrived.</summary>
    public int? HttpStatus { get; init; }

    /// <summary>True when <see cref="Status"/> is <see cref="FetchStatus.Ok"/>.</summary>
    public bool Ok => this.Status == FetchStatus.Ok;

    /// <summary>The file name without its extension ("Carry the Clear: TOP Healing Plan"), or null.</summary>
    public string? Title
    {
        get
        {
            if (string.IsNullOrWhiteSpace(this.FileName))
                return null;
            var name = this.FileName.Trim();
            var dot = name.LastIndexOf('.');
            if (dot > 0 && name.Length - dot <= 5)
                name = name[..dot];
            return name.Trim();
        }
    }
}

/// <summary>
/// Downloads Google Sheets/Docs exports on the user's request (never automatically). Each request has a time limit
/// (default 20 s, covering headers and body), a size cap (default 25 MB) and a cancellation token, and every failure
/// becomes a <see cref="FetchResult"/> with a friendly message instead of an exception. A file that is not shared
/// publicly comes back from Google as a sign-in page (HTML) instead of the export: that is detected and explained.
/// Thread-safe: <see cref="FetchAsync"/> runs on a background task.
/// </summary>
internal sealed class SheetFetcher : IDisposable
{
    /// <summary>Default time limit of one download.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Default size cap (Google exports of plans are well under 1 MB; LPDU with an image is 420 KB).</summary>
    public const long DefaultMaxBytes = 25L * 1024 * 1024;

    /// <summary>Media type of an .xlsx file.</summary>
    public const string XlsxMediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const string ShareHelp =
        "In Google Sheets/Docs use Share > General access > \"Anyone with the link\" (Viewer), or copy the cells and use \"Paste cells\" instead.";

    private static readonly string[] LoginMarkers =
    [
        "accounts.google.com/ServiceLogin",
        "accounts.google.com/v3/signin",
        "accounts.google.com/AccountChooser",
        "accounts.google.com/InteractiveLogin",
        "id=\"identifierId\"",
    ];

    private readonly HttpClient http;
    private readonly TimeSpan timeout;
    private readonly long maxBytes;

    /// <summary>Creates the fetcher.</summary>
    /// <param name="handler">HTTP handler (tests pass a fake); null = a redirect-following SocketsHttpHandler.</param>
    /// <param name="timeout">Time limit per download (headers and body).</param>
    /// <param name="maxBytes">Size cap.</param>
    public SheetFetcher(HttpMessageHandler? handler = null, TimeSpan? timeout = null, long? maxBytes = null)
    {
        handler ??= new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
        this.http = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        this.timeout = timeout ?? DefaultTimeout;
        this.maxBytes = maxBytes ?? DefaultMaxBytes;
    }

    /// <summary>Size cap in bytes.</summary>
    public long MaxBytes => this.maxBytes;

    /// <summary>Downloads what MitCue imports for a link (<see cref="GoogleLinks.DownloadUrl"/>).</summary>
    public Task<FetchResult> FetchAsync(GoogleLink link, CancellationToken cancel) =>
        this.FetchUrlAsync(GoogleLinks.DownloadUrl(link), link.IsSheet ? ExpectedContent.Xlsx : ExpectedContent.Html, link.IsSheet ? "sheet" : "document", cancel);

    /// <summary>Downloads one URL and checks the answer.</summary>
    /// <param name="url">Export URL.</param>
    /// <param name="expected">The kind of file expected.</param>
    /// <param name="what">"sheet" or "document", for messages.</param>
    /// <param name="cancel">User cancellation.</param>
    public async Task<FetchResult> FetchUrlAsync(string url, ExpectedContent expected, string what, CancellationToken cancel)
    {
        using var timeoutSource = new CancellationTokenSource(this.timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeoutSource.Token);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await this.http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);

            var status = (int)response.StatusCode;
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            var finalUri = response.RequestMessage?.RequestUri;
            if (IsSignInHost(finalUri))
                return Fail(FetchStatus.NotShared, url, status, mediaType, $"Google asked for a sign-in instead of sending the {what}: it is not shared publicly. {ShareHelp}");

            if (!response.IsSuccessStatusCode)
                return FromHttpStatus(status, url, mediaType, what);

            if (response.Content.Headers.ContentLength is { } length && length > this.maxBytes)
                return Fail(FetchStatus.TooLarge, url, status, mediaType, TooLargeMessage(what, length, this.maxBytes));

            var bytes = await ReadCappedAsync(response.Content, this.maxBytes, linked.Token).ConfigureAwait(false);
            if (bytes == null)
                return Fail(FetchStatus.TooLarge, url, status, mediaType, TooLargeMessage(what, null, this.maxBytes));

            return Classify(expected, url, status, mediaType, FileNameOf(response.Content.Headers.ContentDisposition), bytes, what, this.maxBytes);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return Fail(FetchStatus.Cancelled, url, null, null, "Download cancelled.");
        }
        catch (OperationCanceledException)
        {
            return Fail(
                FetchStatus.Timeout,
                url,
                null,
                null,
                $"Google did not answer within {this.timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} seconds. Check your connection and try again.");
        }
        catch (HttpRequestException ex)
        {
            return Fail(FetchStatus.NetworkError, url, null, null, $"Could not reach Google ({Inner(ex)}). Check your internet connection and try again.");
        }
        catch (IOException ex)
        {
            return Fail(FetchStatus.NetworkError, url, null, null, $"The download was interrupted ({Inner(ex)}). Try again.");
        }
    }

    /// <inheritdoc/>
    public void Dispose() => this.http.Dispose();

    /// <summary>Checks a 200 answer against what was expected. Internal for tests.</summary>
    internal static FetchResult Classify(ExpectedContent expected, string url, int status, string? mediaType, string? fileName, byte[] bytes, string what, long maxBytes)
    {
        if (bytes.Length > maxBytes)
            return Fail(FetchStatus.TooLarge, url, status, mediaType, TooLargeMessage(what, bytes.Length, maxBytes));

        var looksHtml = LooksLikeHtml(bytes, mediaType);
        switch (expected)
        {
            case ExpectedContent.Xlsx:
                if (IsZip(bytes))
                    return Success(url, status, mediaType, fileName, bytes, what);
                if (looksHtml)
                {
                    return Fail(
                        FetchStatus.NotShared,
                        url,
                        status,
                        mediaType,
                        $"Google sent a web page (probably a sign-in page) instead of the spreadsheet: the {what} is not shared publicly. {ShareHelp}");
                }

                return Fail(FetchStatus.UnexpectedContent, url, status, mediaType, $"Google did not send a spreadsheet file (it sent {mediaType ?? "an unknown type"}).");

            case ExpectedContent.Html:
                if (!looksHtml)
                    return Fail(FetchStatus.UnexpectedContent, url, status, mediaType, $"Google did not send the {what} as a web page (it sent {mediaType ?? "an unknown type"}).");
                if (IsSignInPage(bytes))
                    return Fail(FetchStatus.NotShared, url, status, mediaType, $"Google sent a sign-in page instead of the {what}: it is not shared publicly. {ShareHelp}");
                return Success(url, status, mediaType, fileName, bytes, what);

            default:
                if (looksHtml)
                {
                    return Fail(
                        FetchStatus.NotShared,
                        url,
                        status,
                        mediaType,
                        $"Google sent a web page (probably a sign-in page) instead of the {what}: it is not shared publicly. {ShareHelp}");
                }

                return Success(url, status, mediaType, fileName, bytes, what);
        }
    }

    /// <summary>Maps an HTTP error status to a result. Internal for tests.</summary>
    internal static FetchResult FromHttpStatus(int status, string url, string? mediaType, string what)
    {
        var code = status.ToString(CultureInfo.InvariantCulture);
        return status switch
        {
            401 or 403 => Fail(FetchStatus.NotShared, url, status, mediaType, $"The {what} is not shared publicly (HTTP {code}). {ShareHelp}"),
            404 or 410 => Fail(FetchStatus.NotFound, url, status, mediaType, $"Google has no {what} with this id (HTTP {code}). Check that the link is complete, or copy it again."),
            400 => Fail(FetchStatus.HttpError, url, status, mediaType, $"Google rejected the request (HTTP 400). If the link points at a tab that was deleted, copy a fresh link."),
            429 => Fail(FetchStatus.HttpError, url, status, mediaType, "Google is limiting downloads right now (HTTP 429). Wait a minute and try again."),
            >= 500 => Fail(FetchStatus.HttpError, url, status, mediaType, $"Google had a problem (HTTP {code}). Try again in a moment."),
            _ => Fail(FetchStatus.HttpError, url, status, mediaType, $"Unexpected answer from Google (HTTP {code})."),
        };
    }

    /// <summary>True for a zip file (xlsx/docx are zip packages).</summary>
    internal static bool IsZip(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4 && bytes[0] == (byte)'P' && bytes[1] == (byte)'K' && bytes[2] == 3 && bytes[3] == 4;

    /// <summary>True when the media type says HTML or the body starts like an HTML page.</summary>
    internal static bool LooksLikeHtml(ReadOnlySpan<byte> bytes, string? mediaType)
    {
        if (mediaType != null && mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            return true;

        var head = Encoding.UTF8.GetString(bytes[..Math.Min(bytes.Length, 512)]).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return head.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) ||
               head.StartsWith("<html", StringComparison.OrdinalIgnoreCase) ||
               head.StartsWith("<head", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when an HTML body is a Google sign-in page.</summary>
    internal static bool IsSignInPage(ReadOnlySpan<byte> bytes)
    {
        // Sign-in pages are small; a long document that merely links to accounts.google.com is not one.
        var text = Encoding.UTF8.GetString(bytes[..Math.Min(bytes.Length, 256 * 1024)]);
        if (!text.Contains("<form", StringComparison.OrdinalIgnoreCase) && !text.Contains("identifierId", StringComparison.Ordinal))
            return false;
        foreach (var marker in LoginMarkers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>True when the final URL of a request is Google's sign-in host.</summary>
    internal static bool IsSignInHost(Uri? uri) =>
        uri != null && uri.IsAbsoluteUri &&
        (string.Equals(uri.Host, "accounts.google.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".accounts.google.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>The decoded file name of a Content-Disposition header (RFC 5987 <c>filename*</c> first).</summary>
    internal static string? FileNameOf(ContentDispositionHeaderValue? disposition)
    {
        var name = disposition?.FileNameStar;
        if (string.IsNullOrWhiteSpace(name))
            name = disposition?.FileName?.Trim('"');
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    private static async Task<byte[]?> ReadCappedAsync(HttpContent content, long maxBytes, CancellationToken cancel)
    {
        await using var stream = await content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        var initial = (int)Math.Min(content.Headers.ContentLength ?? 256 * 1024, Math.Min(maxBytes, 64L * 1024 * 1024));
        using var buffer = new MemoryStream(Math.Max(initial, 0));
        var chunk = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancel).ConfigureAwait(false);
            if (read <= 0)
                break;
            if (buffer.Length + read > maxBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string TooLargeMessage(string what, long? length, long maxBytes)
    {
        var size = length is { } l ? " (" + FormatSize(l) + ")" : string.Empty;
        return $"The {what} is too large to import{size}; the limit is {FormatSize(maxBytes)}. Large images in the file count too: remove them, or copy the cells and use \"Paste cells\".";
    }

    private static string Inner(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException != null)
            inner = inner.InnerException;
        return inner.Message.TrimEnd('.');
    }

    private static FetchResult Success(string url, int status, string? mediaType, string? fileName, byte[] bytes, string what) => new()
    {
        Status = FetchStatus.Ok,
        Message = $"Downloaded the {what} ({FormatSize(bytes.Length)}).",
        Url = url,
        Bytes = bytes,
        ContentType = mediaType,
        FileName = fileName,
        HttpStatus = status,
    };

    private static FetchResult Fail(FetchStatus status, string url, int? httpStatus, string? mediaType, string message) => new()
    {
        Status = status,
        Message = message,
        Url = url,
        ContentType = mediaType,
        HttpStatus = httpStatus,
    };

    private static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024
            ? (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
            : Math.Max(1, bytes / 1024).ToString(CultureInfo.InvariantCulture) + " KB";
}
