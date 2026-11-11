using System.Net;
using MitCue.Cli.Import;
using MitCue.Core.Import;
using MitCue.Core.Plans;

namespace MitCue.Cli.Tests;

/// <summary>
/// <c>import</c>: Google Sheets links, downloads, tab matching, and full imports of the example sheets. The full imports
/// need the sheet readers and <see cref="PlanImporter"/>, which are written in parallel: they are skipped while those throw
/// <see cref="NotImplementedException"/>.
/// </summary>
public class ImportCommandTests : IDisposable
{
    private const string CtcId = "1chKttd7POMf-aRLMAzQjNHDIgVzOKmLk9igSt-o2X6s";

    private readonly CliKit kit = new();

    public void Dispose() => this.kit.Dispose();

    // ---- links -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("https://docs.google.com/spreadsheets/d/" + CtcId + "/edit?gid=777100#gid=777100", CtcId, 777100L, false)]
    [InlineData("https://docs.google.com/spreadsheets/d/" + CtcId + "/edit#gid=0", CtcId, 0L, false)]
    [InlineData("https://docs.google.com/spreadsheets/d/" + CtcId, CtcId, null, false)]
    [InlineData("https://docs.google.com/spreadsheets/d/" + CtcId + "/", CtcId, null, false)]
    [InlineData("http://docs.google.com/spreadsheets/u/0/d/" + CtcId + "/htmlview", CtcId, null, false)]
    [InlineData("  https://docs.google.com/spreadsheets/d/" + CtcId + "/edit?usp=sharing  ", CtcId, null, false)]
    [InlineData("https://docs.google.com/a/example.org/spreadsheets/d/" + CtcId + "/edit#gid=12", CtcId, 12L, false)]
    [InlineData("https://docs.google.com/spreadsheets/d/e/2PACX-1vQabc_DEF-123/pubhtml?gid=5", "2PACX-1vQabc_DEF-123", 5L, true)]
    public void GoogleSheetLinks_AreParsed(string url, string id, long? gid, bool published)
    {
        Assert.True(GoogleSheetLink.TryParse(url, out var link));
        Assert.Equal(id, link.Id);
        Assert.Equal(gid, link.Gid);
        Assert.Equal(published, link.Published);
        Assert.Equal(
            published ? $"https://docs.google.com/spreadsheets/d/e/{id}/pub?output=xlsx" : $"https://docs.google.com/spreadsheets/d/{id}/export?format=xlsx",
            link.ExportUrl);
    }

    [Theory]
    [InlineData("https://docs.google.com/document/d/" + CtcId + "/edit")]
    [InlineData("https://example.com/spreadsheets/d/" + CtcId)]
    [InlineData("https://docs.google.com/spreadsheets/")]
    [InlineData("docs.google.com/spreadsheets/d/" + CtcId)]
    [InlineData("ftp://docs.google.com/spreadsheets/d/" + CtcId)]
    public void OtherLinks_AreNotSheetLinks(string url) => Assert.False(GoogleSheetLink.TryParse(url, out _));

    [Fact]
    public void GoogleDocsLinks_AreRefusedWithAHint()
    {
        var r = this.kit.Run("import", "https://docs.google.com/document/d/" + CtcId + "/edit", "--fight", "top");
        Assert.Equal(3, r.ExitCode);
        Assert.Contains("Google Docs links are not supported", r.Err);
    }

    // ---- downloads (fake HTTP) ------------------------------------------------------------------------------

    [Fact]
    public void Download_OfASheetThatIsNotPublic_SaysSo()
    {
        this.kit.Http = _ => Responses.Status(HttpStatusCode.OK, "text/html", "<html>Sign in</html>");
        var r = this.kit.Run("import", $"https://docs.google.com/spreadsheets/d/{CtcId}/edit#gid=777100", "--fight", "top");
        Assert.Equal(3, r.ExitCode);
        Assert.Equal(new Uri($"https://docs.google.com/spreadsheets/d/{CtcId}/export?format=xlsx"), Assert.Single(this.kit.Requests));
        Assert.Contains("did not return a spreadsheet (got text/html)", r.Err);
        Assert.Contains("Anyone with the link", r.Err);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "404 Not Found", true)]
    [InlineData(HttpStatusCode.Unauthorized, "401 Unauthorized", true)]
    [InlineData(HttpStatusCode.InternalServerError, "500 Internal Server Error", false)]
    public void Download_Errors_AreFailures(HttpStatusCode status, string message, bool hint)
    {
        this.kit.Http = _ => Responses.Status(status);
        var r = this.kit.Run("import", $"https://docs.google.com/spreadsheets/d/{CtcId}", "--fight", "top");
        Assert.Equal(3, r.ExitCode);
        Assert.Contains("download failed: " + message, r.Err);
        Assert.Equal(hint, r.Err.Contains("Anyone with the link", StringComparison.Ordinal));
    }

    [Fact]
    public void Download_NetworkErrors_AreFailures()
    {
        this.kit.Http = _ => throw new HttpRequestException("Name or service not known");
        var r = this.kit.Run("import", $"https://docs.google.com/spreadsheets/d/{CtcId}", "--fight", "top");
        Assert.Equal(3, r.ExitCode);
        Assert.Contains("download failed: Name or service not known", r.Err);
    }

    // ---- sources and tabs -------------------------------------------------------------------------------------

    [Fact]
    public void Sources_ThatCannotBeRead_AreReported()
    {
        Assert.Contains("source not found", this.kit.Run("import", "nothing.xlsx", "--fight", "top").Err);
        Assert.Equal(2, this.kit.Run("import", this.kit.Write("plan.pdf", "%PDF"), "--fight", "top").ExitCode);
        Directory.CreateDirectory(Path.Combine(this.kit.WorkDir, "empty"));
        var empty = this.kit.Run("import", "empty", "--fight", "top");
        Assert.Equal(3, empty.ExitCode);
        Assert.Contains("has no *.json fixture tabs", empty.Err);
        Assert.Contains("--save only applies to Google Sheets links", this.kit.Run("import", "x.xlsx", "--fight", "top", "--save", "y.xlsx").Err);
    }

    [Fact]
    public void Tabs_AreMatchedIgnoringCaseAndPunctuation()
    {
        static SheetTab Tab(string name) => new() { Name = name, Rows = [] };
        var document = new SheetDocument { Title = "LPDU", Tabs = [Tab("ALOO"), Tab("P1P2"), Tab("TOP – White Mage")] };

        Assert.Null(SheetSource.MatchTabs(document, null));
        Assert.Equal(["P1P2", "TOP – White Mage"], SheetSource.MatchTabs(document, ["P1/P2", "top – white mage"]));
        Assert.Equal(["ALOO"], SheetSource.MatchTabs(document, ["aloo", "ALOO"]));

        var ex = Assert.Throws<UsageException>(() => SheetSource.MatchTabs(document, ["P3"]));
        Assert.Contains("no tab 'P3' in \"LPDU\"", ex.Message);
        Assert.Contains("\"P1P2\"", ex.Message);
    }

    // ---- full imports (need the readers and the importer) ------------------------------------------------------

    [ImporterFact]
    public void Import_FixtureFolder_WritesPlanYaml_AndReportsDiagnosticsAndCalibration()
    {
        var r = this.kit.Run(
            "import", Path.Combine(CliKit.Fixtures, "carry-the-clear"), "--fight", "top", "--tabs", "TOP – White Mage",
            "--calibration", "carry_the_clear", "--name", "CtC WHM", "-o", "ctc-whm.yaml");
        Assert.True(r.ExitCode is 0 or 1, r.ToString());
        Assert.StartsWith("Source: \"carry-the-clear\" (fixture directory), 3 tabs:", r.Out);
        Assert.Contains("Tabs imported: \"TOP – White Mage\"", r.Out);
        Assert.Contains("Diagnostics: ", r.Out);
        Assert.Contains("Calibration", r.Out);
        Assert.Contains("Plan \"CtC WHM\" (fight top): ", r.Out);
        Assert.EndsWith("ctc-whm.yaml\n", r.Out);
        Assert.Equal(r.ExitCode == 1, r.Out.Contains("  Errors (", StringComparison.Ordinal));

        var plan = PlanSerializer.FromYaml(File.ReadAllText(Path.Combine(this.kit.WorkDir, "ctc-whm.yaml")));
        Assert.Equal("top", plan.Fight);
        Assert.Equal("CtC WHM", plan.Name);
        Assert.NotEmpty(plan.Entries);
        Assert.Equal("2026-11-11", plan.Source?.Imported);

        // The plan works with the other commands.
        Assert.Equal(0, this.kit.Run("plan", "ctc-whm.yaml", "--job", "WHM").ExitCode);
        Assert.Equal(0, this.kit.Run("simulate", "ctc-whm.yaml", "--job", "WHM").ExitCode);
    }

    [ImporterFact]
    public void Import_ToStandardOutput_SendsTheReportToStandardError()
    {
        const string id = "6f1c2b9e-0000-4000-8000-000000000001";
        var r = this.kit.Run("import", Path.Combine(CliKit.Fixtures, "carry-the-clear", "top-white-mage.json"), "--fight", "top", "--date", "2026-01-02", "--id", id.ToUpperInvariant());
        Assert.True(r.ExitCode is 0 or 1, r.ToString());
        Assert.StartsWith("Source: \"top-white-mage\" (fixture file)", r.Err);
        var plan = PlanSerializer.FromYaml(r.Out);
        Assert.Equal("2026-01-02", plan.Source?.Imported);
        Assert.Equal(id, plan.Id);

        // Same input, same options: the same plan file.
        Assert.Equal(r.Out, this.kit.Run("import", Path.Combine(CliKit.Fixtures, "carry-the-clear", "top-white-mage.json"), "--fight", "top", "--date", "2026-01-02", "--id", id).Out);
    }

    [ImporterFact]
    public void Import_Xlsx_MatchesExportedTabNames()
    {
        var r = this.kit.Run("import", Path.Combine(CliKit.Fixtures, "lpdu", "export", "lpdu.xlsx"), "--fight", "top", "--tabs", "P1/P2,P3", "-o", "lpdu.yaml");
        Assert.True(r.ExitCode is 0 or 1, r.ToString());
        Assert.Contains("(xlsx file)", r.Out);
        Assert.Matches("Tabs imported: \"P1/?P2\", \"P3\"", r.Out);
    }

    [ImporterFact]
    public void Import_Csv()
    {
        var r = this.kit.Run("import", Path.Combine(CliKit.Fixtures, "carry-the-clear", "export", "csv", "top-white-mage.csv"), "--fight", "top", "-o", "csv.yaml");
        Assert.True(r.ExitCode is 0 or 1, r.ToString());
        Assert.StartsWith("Source: \"top-white-mage\" (csv file), 1 tab", r.Out);
    }

    [ImporterFact]
    public void Import_GoogleSheetLink_DownloadsTheXlsxExport()
    {
        var bytes = File.ReadAllBytes(Path.Combine(CliKit.Fixtures, "carry-the-clear", "export", "carry-the-clear.xlsx"));
        this.kit.Http = _ => Responses.Xlsx(bytes, "Carry the Clear: TOP Healing Plan.xlsx");
        var link = $"https://docs.google.com/spreadsheets/d/{CtcId}/edit#gid=777100";
        var r = this.kit.Run("import", link, "--fight", "top", "--tabs", "TOP – White Mage", "-o", "linked.yaml", "--save", "copy.xlsx");
        Assert.True(r.ExitCode is 0 or 1, r.ToString());
        Assert.Contains("Source: \"Carry the Clear: TOP Healing Plan\" (google-sheet link)", r.Out);
        Assert.Contains("The link points at tab gid 777100", r.Out);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(this.kit.WorkDir, "copy.xlsx")));
        var plan = PlanSerializer.FromYaml(File.ReadAllText(Path.Combine(this.kit.WorkDir, "linked.yaml")));
        Assert.Equal(link, plan.Source?.Url);
    }
}
