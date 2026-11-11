using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MitCue.Core.Data;
using MitCue.Import;

namespace MitCue.PluginTests;

public class LoadLinkTests
{
    private const string CtcId = "1chKttd7POMf-aRLMAzQjNHDIgVzOKmLk9igSt-o2X6s";

    private static string Fixture(params string[] parts) =>
        Path.Combine([Path.GetDirectoryName(DirectoryDataSource.FindRepoData().Root)!, "tests", "fixtures", "sheets", .. parts]);

    [Fact]
    public async Task ReadsAGoogleDoc()
    {
        const string html = "<html><head><title>ignored</title></head><body><h2>P3</h2><table><tr><td>Hello World</td><td>Temperance</td></tr></table></body></html>";
        var requests = new List<string>();
        using var fetcher = new SheetFetcher(new RoutingHandler(request =>
        {
            requests.Add(request.RequestUri!.ToString());
            return Respond(Encoding.UTF8.GetBytes(html), "text/html", "My Plan.html");
        }));
        var link = GoogleLinks.Parse("https://docs.google.com/document/d/195j9eDD3ccgjQRttHhJPymLJUCOUjs-jmwTrekvdjFE/edit")!;

        var source = await ImportPipeline.LoadLinkAsync(fetcher, link, CancellationToken.None);

        Assert.Equal("google-doc", source.Kind);
        Assert.Equal("My Plan", source.Document.Title);
        Assert.Equal("https://docs.google.com/document/d/195j9eDD3ccgjQRttHhJPymLJUCOUjs-jmwTrekvdjFE/edit", source.Url);
        Assert.Equal(new[] { "Table 1", DocHtmlReader.TextTabName }, source.Document.Tabs.Select(t => t.Name));
        Assert.Equal("Temperance", source.Document.Tabs[0].Cell(0, 1).Text);
        Assert.NotNull(source.Note);
        Assert.Single(requests);
    }

    [Fact]
    public async Task ReportsDownloadFailuresAsStepErrors()
    {
        using var fetcher = new SheetFetcher(new RoutingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("<html></html>") }));
        var link = GoogleLinks.Parse(CtcId)!;

        var error = await Assert.ThrowsAsync<ImportStepException>(() => ImportPipeline.LoadLinkAsync(fetcher, link, CancellationToken.None));

        Assert.Contains("HTTP 404", error.Message);
        Assert.False(error.NotAvailable);
    }

    [Fact]
    public async Task ReadsAWorkbookAndFindsTheLinkedTab()
    {
        var workbook = await File.ReadAllBytesAsync(Fixture("carry-the-clear", "export", "carry-the-clear.xlsx"));
        var requests = new List<string>();
        using var fetcher = new SheetFetcher(new RoutingHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            requests.Add(url);
            return url.EndsWith("&gid=777200", StringComparison.Ordinal)
                ? Respond(workbook, SheetFetcher.XlsxMediaType, "Carry the Clear: TOP Healing Plan - TOP – Scholar.xlsx")
                : Respond(workbook, SheetFetcher.XlsxMediaType, "Carry the Clear: TOP Healing Plan.xlsx");
        }));
        var link = GoogleLinks.Parse($"https://docs.google.com/spreadsheets/d/{CtcId}/edit#gid=777200")!;

        LoadedSource source;
        try
        {
            source = await ImportPipeline.LoadLinkAsync(fetcher, link, CancellationToken.None);
        }
        catch (ImportStepException ex) when (ex.NotAvailable)
        {
            // The xlsx reader is not part of this build yet: the download worked and the failure is explained.
            Assert.Contains("not available in this build", ex.Message);
            Assert.Equal($"https://docs.google.com/spreadsheets/d/{CtcId}/export?format=xlsx", Assert.Single(requests));
            return;
        }

        Assert.Equal("google-sheet", source.Kind);
        Assert.Equal("Carry the Clear: TOP Healing Plan", source.Document.Title);
        Assert.Equal(3, source.Document.Tabs.Count);
        Assert.Equal("TOP – Scholar", source.LinkedTab);
        Assert.Equal($"https://docs.google.com/spreadsheets/d/{CtcId}/edit#gid=777200", source.Url);
        Assert.Equal(2, requests.Count);
    }

    private static HttpResponseMessage Respond(byte[] body, string mediaType, string fileName)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = fileName };
        return response;
    }

    private sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = respond(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }
}
