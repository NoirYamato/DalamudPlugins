using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MitCue.Import;

namespace MitCue.PluginTests;

public class SheetFetcherTests
{
    private static readonly GoogleLink Sheet = GoogleLinks.Parse("https://docs.google.com/spreadsheets/d/1chKttd7POMf-aRLMAzQjNHDIgVzOKmLk9igSt-o2X6s/edit#gid=777100")!;
    private static readonly GoogleLink Doc = GoogleLinks.Parse("https://docs.google.com/document/d/195j9eDD3ccgjQRttHhJPymLJUCOUjs-jmwTrekvdjFE/edit")!;
    private static readonly byte[] Zip = [(byte)'P', (byte)'K', 3, 4, 20, 0, 0, 0, 8, 0];

    [Fact]
    public async Task DownloadsAWorkbookAndReadsTheTitle()
    {
        Uri? requested = null;
        var handler = new FakeHandler((request, _) =>
        {
            requested = request.RequestUri;
            var response = Respond(HttpStatusCode.OK, Zip, SheetFetcher.XlsxMediaType, request);
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = "Carry the Clear: TOP Healing Plan.xlsx" };
            return response;
        });
        using var fetcher = new SheetFetcher(handler);

        var result = await fetcher.FetchAsync(Sheet, CancellationToken.None);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(FetchStatus.Ok, result.Status);
        Assert.Equal(Zip, result.Bytes);
        Assert.Equal("Carry the Clear: TOP Healing Plan", result.Title);
        Assert.Equal(200, result.HttpStatus);
        Assert.Equal("https://docs.google.com/spreadsheets/d/1chKttd7POMf-aRLMAzQjNHDIgVzOKmLk9igSt-o2X6s/export?format=xlsx", requested?.ToString());
    }

    [Fact]
    public async Task ASignInPageMeansNotShared()
    {
        var html = "<!DOCTYPE html><html><head><title>Google Sheets - sign in</title></head><body><form action=\"https://accounts.google.com/v3/signin/identifier\"><input id=\"identifierId\"></form></body></html>";
        using var fetcher = new SheetFetcher(new FakeHandler((request, _) => Respond(HttpStatusCode.OK, Encoding.UTF8.GetBytes(html), "text/html", request)));

        var result = await fetcher.FetchAsync(Sheet, CancellationToken.None);

        Assert.Equal(FetchStatus.NotShared, result.Status);
        Assert.Contains("not shared publicly", result.Message);
        Assert.Contains("Anyone with the link", result.Message);
        Assert.Null(result.Bytes);
    }

    [Fact]
    public async Task ARedirectToTheSignInHostMeansNotShared()
    {
        using var fetcher = new SheetFetcher(new FakeHandler((request, _) =>
        {
            var response = Respond(HttpStatusCode.OK, "<html><body>Sign in</body></html>"u8.ToArray(), "text/html", request);
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://accounts.google.com/ServiceLogin?continue=https://docs.google.com/");
            return response;
        }));

        var result = await fetcher.FetchAsync(Doc, CancellationToken.None);

        Assert.Equal(FetchStatus.NotShared, result.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, nameof(FetchStatus.NotShared))]
    [InlineData(HttpStatusCode.Unauthorized, nameof(FetchStatus.NotShared))]
    [InlineData(HttpStatusCode.NotFound, nameof(FetchStatus.NotFound))]
    [InlineData(HttpStatusCode.Gone, nameof(FetchStatus.NotFound))]
    [InlineData(HttpStatusCode.BadRequest, nameof(FetchStatus.HttpError))]
    [InlineData(HttpStatusCode.TooManyRequests, nameof(FetchStatus.HttpError))]
    [InlineData(HttpStatusCode.ServiceUnavailable, nameof(FetchStatus.HttpError))]
    public async Task MapsHttpErrors(HttpStatusCode status, string expectedStatus)
    {
        var expected = Enum.Parse<FetchStatus>(expectedStatus);
        using var fetcher = new SheetFetcher(new FakeHandler((request, _) => Respond(status, "<html></html>"u8.ToArray(), "text/html", request)));

        var result = await fetcher.FetchAsync(Sheet, CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Equal((int)status, result.HttpStatus);
        Assert.Contains(((int)status).ToString(), result.Message);
    }

    [Fact]
    public async Task RefusesFilesOverTheCapFromContentLength()
    {
        using var fetcher = new SheetFetcher(new FakeHandler((request, _) => Respond(HttpStatusCode.OK, new byte[200], SheetFetcher.XlsxMediaType, request)), maxBytes: 100);

        var result = await fetcher.FetchAsync(Sheet, CancellationToken.None);

        Assert.Equal(FetchStatus.TooLarge, result.Status);
        Assert.Contains("too large", result.Message);
    }

    [Fact]
    public async Task RefusesFilesOverTheCapWithoutContentLength()
    {
        using var fetcher = new SheetFetcher(
            new FakeHandler((request, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new StreamContent(new UnknownLengthStream(new byte[500])) };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue(SheetFetcher.XlsxMediaType);
                return response;
            }),
            maxBytes: 100);

        var result = await fetcher.FetchAsync(Sheet, CancellationToken.None);

        Assert.Equal(FetchStatus.TooLarge, result.Status);
    }

    [Fact]
    public async Task TimesOut()
    {
        using var fetcher = new SheetFetcher(
            new FakeHandler(async (request, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                return Respond(HttpStatusCode.OK, Zip, SheetFetcher.XlsxMediaType, request);
            }),
            timeout: TimeSpan.FromMilliseconds(150));

        var result = await fetcher.FetchAsync(Sheet, CancellationToken.None);

        Assert.Equal(FetchStatus.Timeout, result.Status);
        Assert.Contains("did not answer", result.Message);
    }

    [Fact]
    public async Task TellsASlowTransferFromNoAnswer()
    {
        // Review finding: a large document that was still arriving when the time ran out was reported as
        // "Google did not answer ... check your connection".
        using var fetcher = new SheetFetcher(
            new FakeHandler((request, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new StreamContent(new StallingStream(new byte[2048])) };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
                return response;
            }),
            timeout: TimeSpan.FromMilliseconds(300));

        var result = await fetcher.FetchAsync(Doc, CancellationToken.None);

        Assert.Equal(FetchStatus.Timeout, result.Status);
        Assert.Contains("did not finish downloading", result.Message);
        Assert.Contains("2 KB received", result.Message);
        Assert.DoesNotContain("did not answer", result.Message);
        Assert.Contains("AI route", result.Message);
    }

    [Fact]
    public void GivesSizeAdviceForTheKindOfFile()
    {
        var doc = SheetFetcher.Classify(ExpectedContent.Html, "u", 200, "text/html", null, new byte[2000], "document", 1000);
        Assert.Equal(FetchStatus.TooLarge, doc.Status);
        Assert.Contains("AI route", doc.Message);
        Assert.DoesNotContain("Paste cells", doc.Message);

        var sheet = SheetFetcher.Classify(ExpectedContent.Xlsx, "u", 200, SheetFetcher.XlsxMediaType, null, new byte[2000], "sheet", 1000);
        Assert.Equal(FetchStatus.TooLarge, sheet.Status);
        Assert.Contains("Paste cells", sheet.Message);
    }

    [Fact]
    public async Task CanBeCancelled()
    {
        using var cancel = new CancellationTokenSource();
        using var fetcher = new SheetFetcher(new FakeHandler(async (request, ct) =>
        {
            await cancel.CancelAsync();
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Respond(HttpStatusCode.OK, Zip, SheetFetcher.XlsxMediaType, request);
        }));

        var result = await fetcher.FetchAsync(Sheet, cancel.Token);

        Assert.Equal(FetchStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task ReportsNetworkErrors()
    {
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> fail = (_, _) => throw new HttpRequestException("No such host is known.");
        using var fetcher = new SheetFetcher(new FakeHandler(fail));

        var result = await fetcher.FetchAsync(Sheet, CancellationToken.None);

        Assert.Equal(FetchStatus.NetworkError, result.Status);
        Assert.Contains("No such host is known", result.Message);
    }

    [Fact]
    public async Task AcceptsADocsHtmlExport()
    {
        var html = "<html><head><meta content=\"text/html; charset=UTF-8\" http-equiv=\"content-type\"><style>.c1{color:#000}</style></head><body class=\"c2\"><p class=\"c1\"><span>P3 Hello World: Temperance</span></p></body></html>";
        Uri? requested = null;
        using var fetcher = new SheetFetcher(new FakeHandler((request, _) =>
        {
            requested = request.RequestUri;
            return Respond(HttpStatusCode.OK, Encoding.UTF8.GetBytes(html), "text/html", request);
        }));

        var result = await fetcher.FetchAsync(Doc, CancellationToken.None);

        Assert.True(result.Ok, result.Message);
        Assert.EndsWith("/export?format=html", requested?.ToString());
    }

    [Fact]
    public void ClassifiesUnexpectedContent()
    {
        var result = SheetFetcher.Classify(ExpectedContent.Xlsx, "u", 200, "application/pdf", null, "%PDF-1.4"u8.ToArray(), "sheet", 1000);
        Assert.Equal(FetchStatus.UnexpectedContent, result.Status);

        var text = SheetFetcher.Classify(ExpectedContent.Text, "u", 200, "text/plain", "Plan.txt", "﻿P3 Hello World"u8.ToArray(), "document", 1000);
        Assert.True(text.Ok);
        Assert.Equal("Plan", text.Title);

        var htmlForText = SheetFetcher.Classify(ExpectedContent.Text, "u", 200, null, null, "<!DOCTYPE html><html></html>"u8.ToArray(), "document", 1000);
        Assert.Equal(FetchStatus.NotShared, htmlForText.Status);
    }

    [Fact]
    public void DetectsZipAndHtml()
    {
        Assert.True(SheetFetcher.IsZip(Zip));
        Assert.False(SheetFetcher.IsZip("PK"u8));
        Assert.True(SheetFetcher.LooksLikeHtml("  <html>"u8, null));
        Assert.True(SheetFetcher.LooksLikeHtml("x"u8, "text/html"));
        Assert.False(SheetFetcher.LooksLikeHtml("a,b,c"u8, "text/csv"));
        Assert.True(SheetFetcher.IsSignInHost(new Uri("https://accounts.google.com/ServiceLogin")));
        Assert.False(SheetFetcher.IsSignInHost(new Uri("https://docs.google.com/")));
        Assert.False(SheetFetcher.IsSignInPage("<html><body><p>See https://accounts.google.com/ServiceLogin for help</p></body></html>"u8));
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, byte[] body, string mediaType, HttpRequestMessage request)
    {
        var response = new HttpResponseMessage(status) { RequestMessage = request, Content = new ByteArrayContent(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return response;
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond;

        public FakeHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond)
        {
            this.respond = (r, ct) => Task.FromResult(respond(r, ct));
        }

        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            this.respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            this.respond(request, cancellationToken);
    }

    /// <summary>A body that sends its first bytes and then stalls until the read is cancelled (a slow transfer).</summary>
    private sealed class StallingStream(byte[] first) : Stream
    {
        private bool sent;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!this.sent)
            {
                this.sent = true;
                var n = Math.Min(first.Length, buffer.Length);
                first.AsMemory(0, n).CopyTo(buffer);
                return n;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            this.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("async only");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A stream that does not report its length (chunked responses).</summary>
    private sealed class UnknownLengthStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();
    }
}
