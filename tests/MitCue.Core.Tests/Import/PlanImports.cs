using MitCue.Core.Fights;
using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Core.Tests.Data;

namespace MitCue.Core.Tests.Import;

/// <summary>A plan shipped in plans/: the fixture it is imported from and the options.</summary>
/// <param name="File">File name under plans/.</param>
/// <param name="Sheet">Fixture key (tests/fixtures/sheets/&lt;key&gt;).</param>
/// <param name="Options">Import options (fixed date, so the content is deterministic).</param>
internal sealed record BundledPlan(string File, string Sheet, ImportOptions Options);

/// <summary>The TOP fight, the knowledge base and the imports of the example sheets that the importer tests share.</summary>
internal static class PlanImports
{
    public const string Date = "2026-11-11";

    public const string CtcUrl = "https://docs.google.com/spreadsheets/d/1chKttd7POMf-aRLMAzQjNHDIgVzOKmLk9igSt-o2X6s/edit";

    public const string LpduUrl = "https://docs.google.com/spreadsheets/d/1Dlnye-i7SZ0ZZ7TY7r8SKMu3nMj_PZI45zy2QqDzuCU/edit";

    private static readonly Lazy<FightDefinition> LazyTop = new(() => FightLoader.Load(RepoFiles.Data.ReadText("fights/top.yaml"), "fights/top.yaml"));

    private static readonly Lazy<IReadOnlyList<BundledPlan>> LazyBundled = new(() =>
    [
        new("top-ctc-whm.yaml", "carry-the-clear", Options("Carry the Clear – WHM", ["TOP – White Mage"], CtcUrl + "#gid=777100", "carry_the_clear")),
        new("top-ctc-whm-sch.yaml", "carry-the-clear", Options("Carry the Clear – WHM + SCH", ["TOP – WHM + SCH"], CtcUrl + "#gid=777300", "carry_the_clear")),
        new("top-lpdu.yaml", "lpdu", Options("LPDU TOP Mitigation", null, LpduUrl, null)),
    ]);

    private static readonly Lazy<ImportResult> LazyWhm = new(() => Import(Bundled[0]));
    private static readonly Lazy<ImportResult> LazyWhmSch = new(() => Import(Bundled[1]));
    private static readonly Lazy<ImportResult> LazyLpdu = new(() => Import(Bundled[2]));

    public static FightDefinition Top => LazyTop.Value;

    /// <summary>The plans in plans/ and how they are made (regenerate with MITCUE_UPDATE_PLANS=1, see <see cref="PlanFilesTests"/>).</summary>
    public static IReadOnlyList<BundledPlan> Bundled => LazyBundled.Value;

    /// <summary>Carry the Clear, tab 'TOP – White Mage'.</summary>
    public static ImportResult Whm => LazyWhm.Value;

    /// <summary>Carry the Clear, tab 'TOP – WHM + SCH' (both columns).</summary>
    public static ImportResult WhmSch => LazyWhmSch.Value;

    /// <summary>LPDU, every plan tab (the five phase tabs, 'General Tank Mit til P5' and the six tank-pair tabs).</summary>
    public static ImportResult Lpdu => LazyLpdu.Value;

    public static ImportOptions Options(string? name, IReadOnlyList<string>? tabs, string? url, string? calibration) => new()
    {
        Fight = Top,
        Actions = RepoFiles.Actions,
        Aliases = RepoFiles.Aliases,
        PlanName = name,
        Tabs = tabs,
        SourceUrl = url,
        CalibrationKey = calibration,
        ImportedDate = Date,
    };

    public static ImportResult Import(BundledPlan plan) => new PlanImporter().Import(Fixtures.Doc(plan.Sheet), plan.Options);

    /// <summary>Entries whose source cell is <paramref name="src"/> ("'TOP – White Mage'!C18").</summary>
    public static List<PlanEntry> At(this MitPlan plan, string src) => plan.Entries.Where(e => e.Src == src).ToList();

    /// <summary>The action names of an entry, either/or groups as "A|B", notes as "note:text".</summary>
    public static List<string> Names(this PlanEntry e) => e.Do.Select(Describe).ToList();

    /// <summary>The press time on the timeline: the anchor hit plus the offset (null when the offset is automatic).</summary>
    public static double? Press(this PlanEntry e, FightDefinition fight) =>
        e.Offset is { } offset ? fight.GetMechanic(e.Mechanic)!.HitAt(e.HitIndex ?? 0) + offset : null;

    private static string Describe(PlanItem item) =>
        item.Note is { } note ? "note:" + note : item.Any is { } any ? string.Join("|", any.Select(Describe)) : item.Name ?? "?";
}
