using MitCue.Core.Fights;
using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Core.Tests.Data;

namespace MitCue.Core.Tests.Import;

/// <summary>A plan the importer makes from a sheet fixture: the file it is shipped as, the fixture and the options.</summary>
/// <param name="File">File name under plans/, or null for an import the tests use that is not shipped.</param>
/// <param name="Sheet">Fixture key (tests/fixtures/sheets/&lt;key&gt;).</param>
/// <param name="Options">Import options (fixed date, so the content is deterministic).</param>
internal sealed record BundledPlan(string? File, string Sheet, ImportOptions Options);

/// <summary>The TOP fight, the knowledge base and the imports of the example sheets that the importer tests share.</summary>
internal static class PlanImports
{
    public const string Date = "2026-11-11";

    public const string CtcUrl = "https://docs.google.com/spreadsheets/d/1chKttd7POMf-aRLMAzQjNHDIgVzOKmLk9igSt-o2X6s/edit";

    public const string LpduUrl = "https://docs.google.com/spreadsheets/d/1Dlnye-i7SZ0ZZ7TY7r8SKMu3nMj_PZI45zy2QqDzuCU/edit";

    private static readonly Lazy<FightDefinition> LazyTop = new(() => FightLoader.Load(RepoFiles.Data.ReadText("fights/top.yaml"), "fights/top.yaml"));

    /// <summary>
    /// The id of "NoirMit - WHM". The importer derives a plan's id from its name, so the plan keeps the id it was first
    /// shipped with (as "Carry the Clear – WHM", in 0.1.0.1): players' saved plan and column choices still find it.
    /// </summary>
    public const string NoirMitWhmId = "335b6c4f-3684-55e0-a8d0-3e78724ef200";

    private static readonly Lazy<BundledPlan> WhmPlan = new(() =>
        new("top-noirmit-whm.yaml", "carry-the-clear", Options("NoirMit - WHM", ["TOP – White Mage"], CtcUrl + "#gid=777100", "carry_the_clear", NoirMitWhmId)));

    /// <summary>The id of "Carry the Clear - SCH" (fixed, so a later rename keeps players' choices).</summary>
    public const string CarryTheClearSchId = "5b622e32-04ae-53de-a4f8-f66e4c0aae0c";

    // Shipped from 0.1.0.3; never automatic (PlanStore.BundledDefaults keeps the Scholar on LPDU).
    private static readonly Lazy<BundledPlan> SchPlan = new(() =>
        new("top-ctc-sch.yaml", "carry-the-clear", Options("Carry the Clear - SCH", ["TOP – Scholar"], CtcUrl + "#gid=777200", "carry_the_clear", CarryTheClearSchId)));

    // Carry the Clear's combined tab, shipped as "Carry the Clear – WHM + SCH" in 0.1.0.1 only: still imported by the
    // importer's tests (both columns, sequences, per-step times).
    private static readonly Lazy<BundledPlan> WhmSchPlan = new(() =>
        new(null, "carry-the-clear", Options("Carry the Clear – WHM + SCH", ["TOP – WHM + SCH"], CtcUrl + "#gid=777300", "carry_the_clear")));

    private static readonly Lazy<BundledPlan> LpduPlan = new(() => new("top-lpdu.yaml", "lpdu", Options("LPDU TOP Mitigation", null, LpduUrl, null)));

    private static readonly Lazy<ImportResult> LazyWhm = new(() => Import(WhmPlan.Value));
    private static readonly Lazy<ImportResult> LazySch = new(() => Import(SchPlan.Value));
    private static readonly Lazy<ImportResult> LazyWhmSch = new(() => Import(WhmSchPlan.Value));
    private static readonly Lazy<ImportResult> LazyLpdu = new(() => Import(LpduPlan.Value));

    public static FightDefinition Top => LazyTop.Value;

    /// <summary>The plans in plans/ and how they are made (regenerate with MITCUE_UPDATE_PLANS=1, see <see cref="PlanFilesTests"/>).</summary>
    public static IReadOnlyList<BundledPlan> Bundled => [WhmPlan.Value, SchPlan.Value, LpduPlan.Value];

    /// <summary>
    /// Every sheet import the importer tests use, by index: the White Mage tab, the combined tab (not shipped), LPDU and
    /// the Scholar tab.
    /// </summary>
    public static IReadOnlyList<BundledPlan> SheetImports => [WhmPlan.Value, WhmSchPlan.Value, LpduPlan.Value, SchPlan.Value];

    /// <summary>Carry the Clear, tab 'TOP – White Mage' (shipped as "NoirMit - WHM").</summary>
    public static ImportResult Whm => LazyWhm.Value;

    /// <summary>Carry the Clear, tab 'TOP – Scholar' (shipped as "Carry the Clear - SCH").</summary>
    public static ImportResult Sch => LazySch.Value;

    /// <summary>Carry the Clear, tab 'TOP – WHM + SCH' (both columns; not shipped).</summary>
    public static ImportResult WhmSch => LazyWhmSch.Value;

    /// <summary>LPDU, every plan tab (the five phase tabs, 'General Tank Mit til P5' and the six tank-pair tabs).</summary>
    public static ImportResult Lpdu => LazyLpdu.Value;

    public static ImportOptions Options(string? name, IReadOnlyList<string>? tabs, string? url, string? calibration, string? planId = null) => new()
    {
        PlanId = planId,
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
