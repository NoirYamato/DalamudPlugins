using MitCue.Core.Import;
using MitCue.Core.Plans;

namespace MitCue.Core.Tests.Import;

/// <summary>
/// What the importer reports for the two example sheets: every piece of real action shorthand resolves, only note-only
/// phrases and free text are kept as notes (info), and nothing needs a warning.
/// </summary>
public class ImportDiagnosticsTests
{
    public static TheoryData<string> Sheets => new() { "carry-the-clear", "lpdu" };

    private static ImportResult All(string sheet) =>
        new PlanImporter().Import(Fixtures.Doc(sheet), PlanImports.Options(null, null, null, sheet == "carry-the-clear" ? "carry_the_clear" : null));

    [Theory]
    [MemberData(nameof(Sheets))]
    public void RealActionShorthand_ResolvesWithoutWarnings(string sheet)
    {
        var result = All(sheet);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "unresolved-token" && d.Severity != DiagnosticSeverity.Info);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code is "not-available-at-level" or "unknown-mechanic" or "fuzzy-match" or "other-job-action");
    }

    [Theory]
    [MemberData(nameof(Sheets))]
    public void RemainingWarnings_AreListedHere(string sheet)
    {
        // Every warning (or error) the importer still reports for the example sheets, with the reason it is acceptable.
        // There is none: all row labels map to mechanics (docs/research/mechanics.md §4), every action word resolves
        // through data/actions/aliases.yaml (docs/research/actions.md §12), and LPDU's tank-pair tabs are keyed cleanly.
        string[] justified = [];
        var warnings = All(sheet).Diagnostics.Where(d => d.Severity != DiagnosticSeverity.Info).Select(d => $"{d.Code} {d.Src}").ToList();
        Assert.Equal(justified, warnings);
    }

    [Fact]
    public void NotesInsteadOfActions_AreNoteOnlyPhrasesAndFreeText()
    {
        var infos = All("lpdu").Diagnostics.Where(d => d.Code == "unresolved-token").ToList();
        Assert.All(infos, d => Assert.Equal(DiagnosticSeverity.Info, d.Severity));
        var texts = infos.Select(d => d.Message[1..d.Message.IndexOf('\'', 1)]).GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(new Dictionary<string, int> { ["Everything"] = 4, ["Kitchen Sink"] = 22, ["Debuff at ~3 secs"] = 1 }, texts);
        Assert.Contains(infos, d => d.Src == "'P5'!D4" && d.Message.Contains("free text", StringComparison.Ordinal));

        Assert.DoesNotContain(All("carry-the-clear").Diagnostics, d => d.Code == "unresolved-token");
    }

    [Fact]
    public void NotesInsteadOfActions_StayInThePlan()
    {
        var plan = PlanImports.Lpdu.Plan;
        Assert.Equal(4, plan.Entries.Count(e => e.Do.Any(i => i.Note == "Everything")));
        Assert.Equal(22, plan.Entries.Count(e => e.Do.Any(i => i.Note == "Kitchen Sink")));
        var patch = Assert.Single(plan.At("'P5'!D4"));
        Assert.Equal(["Liturgy of the Bell", "note:Debuff at ~3 secs"], patch.Names());
    }

    [Fact]
    public void ReviewDiagnostics_PointAtTheirCells()
    {
        var diagnostics = PlanImports.Lpdu.Diagnostics;
        var reanchored = diagnostics.Where(d => d.Code == "reanchored").ToList();
        Assert.Equal(["'General Tank Mit til P5'!B2", "'General Tank Mit til P5'!C2"], reanchored.Select(d => d.Src));
        Assert.Equal(4, diagnostics.Count(d => d.Code == "ambiguous-mechanic")); // Hello World ×2, Magic Number ×2
        Assert.All(diagnostics.Where(d => d.Code == "ambiguous-mechanic"), d => Assert.StartsWith("'P", d.Src, StringComparison.Ordinal));
        Assert.Equal(6, diagnostics.Count(d => d.Code == "assignee-rekeyed"));
        Assert.Contains(diagnostics, d => d.Code == "note-row" && d.Src == "'P3'!B8"); // "WAR can use Shake as P3 dies …"
        Assert.Contains(diagnostics, d => d.Code == "not-a-plan"); // ALOO (front half)
    }
}
