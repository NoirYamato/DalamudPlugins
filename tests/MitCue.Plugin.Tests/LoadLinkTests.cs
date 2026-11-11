using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MitCue.Core.Data;
using MitCue.Import;
using MitCue.PluginTests.Support;

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
            if (url.EndsWith("/htmlview", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("<html></html>") };
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
            Assert.Contains($"https://docs.google.com/spreadsheets/d/{CtcId}/export?format=xlsx", requests);
            return;
        }

        Assert.Equal("google-sheet", source.Kind);
        Assert.Equal("Carry the Clear: TOP Healing Plan", source.Document.Title);
        Assert.Equal(3, source.Document.Tabs.Count);
        Assert.Equal("TOP – Scholar", source.LinkedTab);
        Assert.Equal($"https://docs.google.com/spreadsheets/d/{CtcId}/edit#gid=777200", source.Url);
        Assert.Equal(3, requests.Count); // the workbook, the linked tab, and the tab list (best effort: 404 here)
    }

    private const string LpduId = "1Dlnye-i7SZ0ZZ7TY7r8SKMu3nMj_PZI45zy2QqDzuCU";

    private static readonly string[] LpduTabNames =
        ["ALOO", "P1/P2", "P3", "P4", "P5", "P6", "General Tank Mit til P5", "WAR/DRK", "WAR/GNB", "WAR/PLD", "DRK/GNB", "DRK/PLD", "GNB/PLD"];

    /// <summary>The htmlview page's tab list as Google writes it (names as JavaScript strings, "/" escaped).</summary>
    private static string HtmlView(IEnumerable<string> names) =>
        "<!DOCTYPE html><html><head><script>var items = [];" +
        string.Concat(names.Select((n, i) => $"items.push({{name: \"{n.Replace("/", "\\/", StringComparison.Ordinal)}\", pageUrl: \"\", gid: \"{i}\"}});")) +
        "</script></head><body></body></html>";

    [Fact]
    public async Task RestoresTheTabNamesTheXlsxExportChanged()
    {
        // Final review finding: the in-game import kept Google's xlsx names ("P1P2", "WARDRK"), so its columns were
        // "WARDRK WAR" and its provenance 'P1P2'!E2, unlike the bundled LPDU plan and the CLI import of the same link.
        var lpdu = await File.ReadAllBytesAsync(Fixture("lpdu", "export", "lpdu.xlsx"));
        var requests = new List<string>();
        using var fetcher = new SheetFetcher(new RoutingHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            requests.Add(url);
            if (url.EndsWith("/htmlview", StringComparison.Ordinal))
                return Respond(Encoding.UTF8.GetBytes(HtmlView(LpduTabNames)), "text/html", "htmlview");
            return url.Contains("&gid=", StringComparison.Ordinal)
                ? Respond(lpdu, SheetFetcher.XlsxMediaType, "LPDU TOP Mitigation Sheet - P1P2.xlsx")
                : Respond(lpdu, SheetFetcher.XlsxMediaType, "LPDU TOP Mitigation Sheet.xlsx");
        }));
        var link = GoogleLinks.Parse($"https://docs.google.com/spreadsheets/d/{LpduId}/edit#gid=871196336")!;

        var source = await ImportPipeline.LoadLinkAsync(fetcher, link, CancellationToken.None);

        Assert.Equal(LpduTabNames, source.Document.Tabs.Select(t => t.Name));
        Assert.Equal("P1/P2", source.LinkedTab); // found by the export's name, then renamed with the others
        Assert.Contains($"https://docs.google.com/spreadsheets/d/{LpduId}/htmlview", requests);
    }

    [Fact]
    public async Task AnInGameImportOfTheLpduLinkHasTheBundledPlansColumnsAndProvenance()
    {
        var lpdu = await File.ReadAllBytesAsync(Fixture("lpdu", "export", "lpdu.xlsx"));
        using var fetcher = new SheetFetcher(new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/htmlview", StringComparison.Ordinal)
                ? Respond(Encoding.UTF8.GetBytes(HtmlView(LpduTabNames)), "text/html", "htmlview")
                : Respond(lpdu, SheetFetcher.XlsxMediaType, "LPDU TOP Mitigation Sheet.xlsx")));
        var source = await ImportPipeline.LoadLinkAsync(fetcher, GoogleLinks.Parse($"https://docs.google.com/spreadsheets/d/{LpduId}/edit")!, CancellationToken.None);
        var data = new MitCue.Data.CoreData(Kit.Errors(), () => []);
        data.EnsureLoaded();

        var result = ImportPipeline.Import(source.Document, new MitCue.Core.Import.ImportOptions
        {
            Fight = data.GetFight("top")!,
            Actions = data.Actions,
            Aliases = data.Aliases!,
            ImportedDate = "2026-11-11",
        });

        var bundled = MitCue.Core.Plans.PlanSerializer.FromYaml(EmbeddedDataSource.Plans().ReadText("top-lpdu.yaml"));
        Assert.Equal(bundled.Assignees.Select(a => a.Key), result.Plan.Assignees.Select(a => a.Key));
        Assert.Equal(bundled.Entries.Select(e => e.Src), result.Plan.Entries.Select(e => e.Src));
    }

    [Fact]
    public async Task AReimportReplacesTheEarlierImportWhetherOrNotGooglesTabNamesWereRestored()
    {
        // Review finding (41d2893): the tab names are restored only when the best-effort tab list can be read, and the
        // replace flow compared tab names and column keys exactly ("P1P2" against "P1/P2", "WARDRK WAR" against
        // "WAR/DRK WAR"): an import made before, or while the tab list failed, was never replaced, and its active-plan and
        // column settings were not carried over.
        var lpdu = await File.ReadAllBytesAsync(Fixture("lpdu", "export", "lpdu.xlsx"));
        var data = new MitCue.Data.CoreData(Kit.Errors(), () => []);
        data.EnsureLoaded();
        var link = GoogleLinks.Parse($"https://docs.google.com/spreadsheets/d/{LpduId}/edit")!;

        async Task<MitCue.Core.Plans.MitPlan> ImportOnce(bool tabList)
        {
            using var fetcher = new SheetFetcher(new RoutingHandler(request =>
                !request.RequestUri!.AbsolutePath.EndsWith("/htmlview", StringComparison.Ordinal)
                    ? Respond(lpdu, SheetFetcher.XlsxMediaType, "LPDU TOP Mitigation Sheet.xlsx")
                    : tabList
                        ? Respond(Encoding.UTF8.GetBytes(HtmlView(LpduTabNames)), "text/html", "htmlview")
                        : new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("<html></html>") }));
            var source = await ImportPipeline.LoadLinkAsync(fetcher, link, CancellationToken.None);
            var tabs = source.Document.Tabs.Where(t => t.Name.StartsWith('P') || t.Name.StartsWith("WAR", StringComparison.Ordinal)).Select(t => t.Name).ToList();
            var plan = ImportPipeline.Import(source.Document, new MitCue.Core.Import.ImportOptions
            {
                Fight = data.GetFight("top")!,
                Actions = data.Actions,
                Aliases = data.Aliases!,
                Tabs = tabs,
                SourceUrl = source.Url,
                ImportedDate = "2026-11-11",
            }).Plan;
            plan.Fight = "top";
            return plan;
        }

        var before = await ImportOnce(tabList: false);
        var after = await ImportOnce(tabList: true);
        Assert.Contains("P1P2", before.Source!.Tabs);
        Assert.Contains("P1/P2", after.Source!.Tabs);
        Assert.Contains(before.Assignees, a => a.Key == "WARDRK WAR");
        Assert.Contains(after.Assignees, a => a.Key == "WAR/DRK WAR");

        Assert.True(MitCue.UI.ImportWindow.SameSource(before.Source, after.Source));
        var store = new MitCue.Plans.PlanStore(Kit.TempDirectory(), new Configuration(), () => { }, Kit.Errors());
        store.EnsureLoaded();
        Assert.True(store.Save(before, out var error), error);
        Assert.Equal(before.Id, MitCue.UI.ImportWindow.DefaultReplacement(store, after));

        // Replacing it (the Save step gives the new plan the earlier one's id) keeps the column choices, under the new keys.
        store.SetMapping(before, MitCue.Core.Job.WAR, new PlayerMapping { AssigneeKey = "WARDRK WAR", Slot = "MT" });
        store.SetMapping(before, MitCue.Core.Job.SCH, new PlayerMapping { AssigneeKey = null, Slot = "H2" });
        Assert.Equal("WAR/DRK WAR", MitCue.Plans.PlanStore.CarryOver(after, store.SavedMapping(before, MitCue.Core.Job.WAR))?.AssigneeKey);
        after.Id = before.Id;
        Assert.True(store.Save(after, out error), error);
        Assert.Equal(("WAR/DRK WAR", "MT"), (store.SavedMapping(after, MitCue.Core.Job.WAR)?.AssigneeKey, store.SavedMapping(after, MitCue.Core.Job.WAR)?.Slot));
        Assert.Equal((null, "H2"), (store.SavedMapping(after, MitCue.Core.Job.SCH)?.AssigneeKey, store.SavedMapping(after, MitCue.Core.Job.SCH)?.Slot));
    }

    [Fact]
    public async Task KeepsTheExportsTabNamesWhenTheTabListCannotBeRead()
    {
        var lpdu = await File.ReadAllBytesAsync(Fixture("lpdu", "export", "lpdu.xlsx"));
        using var fetcher = new SheetFetcher(new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/htmlview", StringComparison.Ordinal)
                ? Respond(Encoding.UTF8.GetBytes("<html><body><form action=\"https://accounts.google.com/ServiceLogin\"></form></body></html>"), "text/html", "x")
                : Respond(lpdu, SheetFetcher.XlsxMediaType, "LPDU TOP Mitigation Sheet.xlsx")));
        var link = GoogleLinks.Parse($"https://docs.google.com/spreadsheets/d/{LpduId}/edit")!;

        var source = await ImportPipeline.LoadLinkAsync(fetcher, link, CancellationToken.None);

        Assert.Contains("P1P2", source.Document.Tabs.Select(t => t.Name));
        Assert.Contains("WARDRK", source.Document.Tabs.Select(t => t.Name));
    }

    [Fact]
    public async Task TheTabListIsAskedForWithTheWorkbookAndAStuckOneDoesNotHoldTheImport()
    {
        // Review finding (41d2893): the tab list was asked for only after the workbook was in, with the fetcher's whole
        // time limit (20 s), so a stuck htmlview page held a finished download; the only way out (Cancel) dropped it.
        var lpdu = await File.ReadAllBytesAsync(Fixture("lpdu", "export", "lpdu.xlsx"));
        var tabListAsked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fetcher = new SheetFetcher(
            new AsyncHandler(async (request, ct) =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/htmlview", StringComparison.Ordinal))
                {
                    tabListAsked.TrySetResult();
                    await Task.Delay(Timeout.Infinite, ct); // never answers
                }

                // The workbook is sent only once the tab list has been asked for: both requests are out together.
                await tabListAsked.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
                return Respond(lpdu, SheetFetcher.XlsxMediaType, "LPDU TOP Mitigation Sheet.xlsx");
            }),
            timeout: TimeSpan.FromSeconds(60));
        var link = GoogleLinks.Parse($"https://docs.google.com/spreadsheets/d/{LpduId}/edit")!;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var source = await ImportPipeline.LoadLinkAsync(fetcher, link, CancellationToken.None, tabListGrace: TimeSpan.FromMilliseconds(200));

        Assert.InRange(watch.Elapsed.TotalSeconds, 0, 10); // not the fetcher's 60 s
        Assert.Contains("P1P2", source.Document.Tabs.Select(t => t.Name)); // the export's names are kept
        Assert.Equal(13, source.Document.Tabs.Count);
    }

    [Fact]
    public void TabNamesApplyByPositionOnlyWhereTheExportChangedTheName()
    {
        var doc = new MitCue.Core.Import.SheetDocument
        {
            Title = "T",
            Tabs = [Indexed(TestData.Tab("P1P2"), 0), Indexed(TestData.Tab("P3"), 1), Indexed(TestData.Tab("Notes"), 2)],
        };

        Assert.Equal(["P1/P2", "P3", "Notes"], ImportPipeline.WithTabNames(doc, ["P1/P2", "P3"]).Tabs.Select(t => t.Name));
        Assert.Equal(["P1/P2", "P3", "Notes"], ImportPipeline.WithTabNames(doc, ["P1/P2", " ", "Notes", "Extra"]).Tabs.Select(t => t.Name));

        // A list that does not line up with the workbook (a tab missing from it) renames nothing.
        Assert.Equal(["P1P2", "P3", "Notes"], ImportPipeline.WithTabNames(doc, ["ALOO", "P1/P2", "P3"]).Tabs.Select(t => t.Name));

        static MitCue.Core.Import.SheetTab Indexed(MitCue.Core.Import.SheetTab tab, int index) => new() { Name = tab.Name, Index = index, Rows = tab.Rows };
    }

    [Fact]
    public async Task CancellingADownloadEndsTheStepAsCancelled()
    {
        // Review finding: a cancelled download ended as a failed step (ImportStepException "Download cancelled."),
        // so the dropped background job left an exception nobody read.
        var started = new TaskCompletionSource();
        using var fetcher = new SheetFetcher(new AsyncHandler(async (_, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        }));
        using var cancel = new CancellationTokenSource();
        var link = GoogleLinks.Parse(CtcId)!;

        var load = ImportPipeline.LoadLinkAsync(fetcher, link, cancel.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);
        Assert.True(load.IsCanceled);
    }

    [Fact]
    public void CancellingADownloadJobLeavesNoUnobservedTaskException()
    {
        // The reviewer's probe: the download job as the import window starts it, cancelled (Cancel button or the window
        // closing) while Google has not answered yet.
        var unobserved = UnobservedTasks.After(
            () =>
            {
                var mainThread = new BlockingCollection<Action>();
                var started = new ManualResetEventSlim();
                using var fetcher = new SheetFetcher(new AsyncHandler(async (_, ct) =>
                {
                    started.Set();
                    await Task.Delay(Timeout.Infinite, ct);
                    throw new InvalidOperationException("unreachable");
                }));
                using var jobs = new BackgroundJobs(mainThread.Add, ex => throw ex);
                var link = GoogleLinks.Parse(CtcId)!;

                jobs.Run("Downloading the sheet...", ct => ImportPipeline.LoadLinkAsync(fetcher, link, ct), _ => Assert.Fail("delivered"), _ => Assert.Fail("delivered"));
                Assert.True(started.Wait(TimeSpan.FromSeconds(10)), "the download never started");
                jobs.Cancel();

                Assert.True(mainThread.TryTake(out var post, TimeSpan.FromSeconds(10)), "the job never finished");
                post();
            },
            ex => ex is ImportStepException or OperationCanceledException);

        Assert.Empty(unobserved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadsThePlainTextOfADocWhoseWebPageIsTooLargeOrTooSlow(bool slow)
    {
        // Review finding: Docs with screenshots are tens of MB as a web page (images inlined as base64) and hit the size
        // cap or the time limit, although their text export is a few KB.
        const string text = "\uFEFFTOP plan\r\nP3\r\nHello World\tTemperance\r\n\r\nLatent 2\tWings";
        var requests = new List<string>();
        using var fetcher = new SheetFetcher(
            new AsyncHandler(async (request, ct) =>
            {
                var url = request.RequestUri!.ToString();
                lock (requests)
                    requests.Add(url);
                if (url.EndsWith("format=txt", StringComparison.Ordinal))
                    return Respond(Encoding.UTF8.GetBytes(text), "text/plain", "My Plan.txt");
                if (slow)
                    await Task.Delay(Timeout.Infinite, ct);
                return Respond(new byte[4096], "text/html", "My Plan.html");
            }),
            timeout: TimeSpan.FromMilliseconds(slow ? 300 : 5000),
            maxBytes: 1024);
        var link = GoogleLinks.Parse("https://docs.google.com/document/d/1csUMEsnV5kiWQeHgw9bQVrEGr5ISR3ycssHIE3j7Etg/edit")!;

        var source = await ImportPipeline.LoadLinkAsync(fetcher, link, CancellationToken.None);

        Assert.Equal(new[] { "/export?format=html", "/export?format=txt" }, requests.Select(u => u[u.LastIndexOf('/')..]));
        Assert.Equal("google-doc", source.Kind);
        Assert.Equal("My Plan", source.Document.Title);
        var tab = Assert.Single(source.Document.Tabs);
        Assert.Equal(DocHtmlReader.TextTabName, tab.Name);
        Assert.Equal(4, tab.Rows.Count);
        Assert.Equal("TOP plan", tab.Cell(0, 0).Text);
        Assert.Equal("Temperance", tab.Cell(2, 1).Text);
        Assert.Equal("Wings", tab.Cell(3, 1).Text);
        Assert.Contains("plain text", source.Warning);
        Assert.Contains(slow ? "too slow" : "too large", source.Warning);
        Assert.Contains("AI route", source.Warning);
    }

    [Fact]
    public async Task GivesDocAdviceWhenADocCannotBeDownloaded()
    {
        // A published document has no plain-text export: the failure says what to do with a document.
        using var fetcher = new SheetFetcher(new RoutingHandler(_ => Respond(new byte[4096], "text/html", "Plan.html")), maxBytes: 1024);
        var link = GoogleLinks.Parse("https://docs.google.com/document/d/e/2PACX-1vTabcdefghijklmnopqrstuvwxyz0123456789/pub")!;

        var error = await Assert.ThrowsAsync<ImportStepException>(() => ImportPipeline.LoadLinkAsync(fetcher, link, CancellationToken.None));

        Assert.Contains("too large", error.Message);
        Assert.Contains("AI route", error.Message);
        Assert.DoesNotContain("Paste cells", error.Message);
    }

    private static HttpResponseMessage Respond(byte[] body, string mediaType, string fileName)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = fileName };
        return response;
    }

    private sealed class AsyncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await respond(request, cancellationToken);
            response.RequestMessage ??= request;
            return response;
        }
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
