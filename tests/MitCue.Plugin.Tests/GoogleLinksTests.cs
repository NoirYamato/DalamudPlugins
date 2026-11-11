using MitCue.Import;

namespace MitCue.PluginTests;

public class GoogleLinksTests
{
    private const string Lpdu = "1Dlnye-i7SZ0ZZ7TY7r8SKMu3nMj_PZI45zy2QqDzuCU";
    private const string Ctc = "1chKttd7POMf-aRLMAzQjNHDIgVzOKmLk9igSt-o2X6s";
    private const string Doc = "195j9eDD3ccgjQRttHhJPymLJUCOUjs-jmwTrekvdjFE";

    [Theory]
    [InlineData("https://docs.google.com/spreadsheets/d/" + Ctc + "/edit#gid=777100", Ctc, 777100L)]
    [InlineData("https://docs.google.com/spreadsheets/u/0/d/" + Lpdu + "/htmlview#gid=871196336", Lpdu, 871196336L)]
    [InlineData("https://docs.google.com/spreadsheets/u/1/d/" + Lpdu + "/htmlview", Lpdu, null)]
    [InlineData("https://docs.google.com/spreadsheets/d/" + Lpdu + "/edit?gid=984942427", Lpdu, 984942427L)]
    [InlineData("https://docs.google.com/spreadsheets/d/" + Lpdu + "/edit?gid=1#gid=2", Lpdu, 2L)]
    [InlineData("https://docs.google.com/spreadsheets/d/" + Lpdu + "/edit?usp=sharing", Lpdu, null)]
    [InlineData("https://docs.google.com/spreadsheets/d/" + Lpdu + "/edit#gid=0&range=A1:B2", Lpdu, 0L)]
    [InlineData("https://docs.google.com/spreadsheets/d/" + Lpdu, Lpdu, null)]
    [InlineData("https://docs.google.com/spreadsheets/d/" + Lpdu + "/", Lpdu, null)]
    [InlineData("https://docs.google.com/spreadsheets/d/" + Lpdu + "/export?format=xlsx&gid=5", Lpdu, 5L)]
    [InlineData("docs.google.com/spreadsheets/d/" + Ctc + "/edit", Ctc, null)]
    [InlineData("http://docs.google.com/spreadsheets/d/" + Ctc + "/edit", Ctc, null)]
    [InlineData("  <https://docs.google.com/spreadsheets/d/" + Ctc + "/edit>  ", Ctc, null)]
    [InlineData("\"https://docs.google.com/spreadsheets/d/" + Ctc + "/view\"", Ctc, null)]
    [InlineData("https://DOCS.google.com/Spreadsheets/D/" + Ctc + "/edit", Ctc, null)]
    [InlineData("https://docs.google.com/a/example.com/spreadsheets/d/" + Ctc + "/edit#gid=3", Ctc, 3L)]
    [InlineData("https://docs.google.com/spreadsheet/ccc?key=" + Ctc + "&usp=sharing#gid=7", Ctc, 7L)]
    public void ParsesSpreadsheetLinks(string url, string id, long? gid)
    {
        Assert.True(GoogleLinks.TryParse(url, out var link, out var error), error);
        Assert.Equal(GoogleFileKind.Sheet, link.Kind);
        Assert.True(link.IsSheet);
        Assert.Equal(id, link.Id);
        Assert.Equal(gid, link.Gid);
        Assert.False(link.Published);
    }

    [Theory]
    [InlineData("https://docs.google.com/document/d/" + Doc + "/edit")]
    [InlineData("https://docs.google.com/document/d/" + Doc + "/edit?tab=t.0")]
    [InlineData("https://docs.google.com/document/u/0/d/" + Doc + "/edit#heading=h.abc")]
    [InlineData("https://docs.google.com/document/d/" + Doc + "/mobilebasic")]
    [InlineData("docs.google.com/document/d/" + Doc)]
    public void ParsesDocumentLinks(string url)
    {
        Assert.True(GoogleLinks.TryParse(url, out var link, out var error), error);
        Assert.Equal(GoogleFileKind.Doc, link.Kind);
        Assert.Equal(Doc, link.Id);
        Assert.Null(link.Gid);
    }

    [Fact]
    public void ParsesPublishedLinks()
    {
        const string pub = "2PACX-1vQ_abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJ";
        var sheet = GoogleLinks.Parse($"https://docs.google.com/spreadsheets/d/e/{pub}/pubhtml?gid=12&single=true");
        Assert.NotNull(sheet);
        Assert.True(sheet.Published);
        Assert.Equal(pub, sheet.Id);
        Assert.Equal(12L, sheet.Gid);
        Assert.Equal($"https://docs.google.com/spreadsheets/d/e/{pub}/pub?output=xlsx", GoogleLinks.WorkbookXlsxUrl(sheet));

        var doc = GoogleLinks.Parse($"https://docs.google.com/document/d/e/{pub}/pub");
        Assert.NotNull(doc);
        Assert.Equal(GoogleFileKind.Doc, doc.Kind);
        Assert.True(doc.Published);
        Assert.Equal($"https://docs.google.com/document/d/e/{pub}/pub", GoogleLinks.DocHtmlUrl(doc));
    }

    [Fact]
    public void AcceptsABareId()
    {
        Assert.True(GoogleLinks.TryParse(Lpdu, out var link, out _));
        Assert.Equal(GoogleFileKind.Sheet, link.Kind);
        Assert.Equal(Lpdu, link.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("hello world")]
    [InlineData("https://example.com/spreadsheets/d/" + Lpdu + "/edit")]
    [InlineData("https://drive.google.com/file/d/" + Lpdu + "/view")]
    [InlineData("https://drive.google.com/open?id=" + Lpdu)]
    [InlineData("https://docs.google.com/presentation/d/" + Lpdu + "/edit")]
    [InlineData("https://docs.google.com/forms/d/" + Lpdu + "/viewform")]
    [InlineData("https://docs.google.com/spreadsheets/d/")]
    [InlineData("https://docs.google.com/spreadsheets/d/abc/edit")]
    [InlineData("https://docs.google.com/")]
    [InlineData("ftp://docs.google.com/spreadsheets/d/" + Lpdu)]
    public void RejectsOtherText(string? text)
    {
        Assert.False(GoogleLinks.TryParse(text, out var link, out var error));
        Assert.Null(link);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Null(GoogleLinks.Parse(text));
    }

    [Fact]
    public void ExplainsDriveLinks()
    {
        GoogleLinks.TryParse("https://drive.google.com/file/d/" + Lpdu + "/view", out _, out var error);
        Assert.Contains("Google Drive", error);
    }

    [Fact]
    public void BuildsSheetExportUrls()
    {
        Assert.Equal($"https://docs.google.com/spreadsheets/d/{Lpdu}/export?format=xlsx", GoogleLinks.SheetExportUrl(Lpdu));
        Assert.Equal($"https://docs.google.com/spreadsheets/d/{Lpdu}/export?format=xlsx&gid=984942427", GoogleLinks.SheetExportUrl(Lpdu, "xlsx", 984942427));
        Assert.Equal($"https://docs.google.com/spreadsheets/d/{Lpdu}/export?format=csv&gid=0", GoogleLinks.SheetExportUrl(Lpdu, "csv", 0));

        var link = GoogleLinks.Parse($"https://docs.google.com/spreadsheets/d/{Ctc}/edit#gid=777200")!;
        Assert.Equal($"https://docs.google.com/spreadsheets/d/{Ctc}/export?format=xlsx", GoogleLinks.WorkbookXlsxUrl(link));
        Assert.Equal($"https://docs.google.com/spreadsheets/d/{Ctc}/export?format=xlsx", GoogleLinks.DownloadUrl(link));
        Assert.Equal($"https://docs.google.com/spreadsheets/d/{Ctc}/export?format=xlsx&gid=777200", GoogleLinks.LinkedTabXlsxUrl(link));
        Assert.Null(GoogleLinks.LinkedTabXlsxUrl(link with { Gid = null }));
        Assert.Throws<ArgumentException>(() => GoogleLinks.DocTextUrl(link));
    }

    [Fact]
    public void BuildsDocExportUrls()
    {
        var link = GoogleLinks.Parse($"https://docs.google.com/document/d/{Doc}/edit")!;
        Assert.Equal($"https://docs.google.com/document/d/{Doc}/export?format=txt", GoogleLinks.DocTextUrl(link));
        Assert.Equal($"https://docs.google.com/document/d/{Doc}/export?format=html", GoogleLinks.DocHtmlUrl(link));
        Assert.Equal($"https://docs.google.com/document/d/{Doc}/export?format=html", GoogleLinks.DownloadUrl(link));
        Assert.Equal($"https://docs.google.com/document/d/{Doc}/export?format=docx", GoogleLinks.DocExportUrl(Doc, "docx"));
        Assert.Throws<ArgumentException>(() => GoogleLinks.WorkbookXlsxUrl(link));
    }

    [Fact]
    public void BuildsBrowserUrlsForProvenance()
    {
        Assert.Equal($"https://docs.google.com/spreadsheets/d/{Ctc}/edit#gid=777100", GoogleLinks.Parse($"https://docs.google.com/spreadsheets/u/0/d/{Ctc}/htmlview#gid=777100")!.BrowserUrl);
        Assert.Equal($"https://docs.google.com/spreadsheets/d/{Ctc}/edit", GoogleLinks.Parse(Ctc)!.BrowserUrl);
        Assert.Equal($"https://docs.google.com/document/d/{Doc}/edit", GoogleLinks.Parse($"https://docs.google.com/document/u/0/d/{Doc}/view")!.BrowserUrl);
    }

    [Fact]
    public void DescribesLinks()
    {
        var link = GoogleLinks.Parse($"https://docs.google.com/spreadsheets/d/{Ctc}/edit#gid=777100")!;
        Assert.Equal("Google Sheet 1chKtt…o2X6s, tab gid 777100", GoogleLinks.Describe(link));
        Assert.Equal("short", GoogleLinks.ShortId("short"));
    }

    [Theory]
    [InlineData("#gid=12", 12L)]
    [InlineData("?gid=12&x=1", 12L)]
    [InlineData("?x=1&GID=7", 7L)]
    [InlineData("#range=A1&gid=3", 3L)]
    [InlineData("#gid=abc", null)]
    [InlineData("#gid=-1", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParsesGids(string? part, long? expected) => Assert.Equal(expected, GoogleLinks.ParseGid(part));
}
