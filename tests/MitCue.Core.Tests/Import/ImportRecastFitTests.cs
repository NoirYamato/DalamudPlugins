using MitCue.Core.Fights;
using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;
using MitCue.Core.Tests.Data;

namespace MitCue.Core.Tests.Import;

/// <summary>
/// The importer's recast fit: an automatic press that would find its action without a charge, as <c>check</c> replays the
/// plan, waits until the action is ready again plus the due lead (the NOW cue comes when it is ready), but never later than
/// "(Late)" would press it. Most tests use a one-phase fight of single hits at chosen times (<see cref="Fight"/>) and the
/// real action catalog: Asylum (recast 90 s, pressed 7.2 s before the hit), Divine Benison (30 s, two charges from level
/// 88, −4 s), Sacred Soil (30 s, −4.5 s), Reprisal (60 s, −3 s), Succor (a 2 s cast).
/// </summary>
public class ImportRecastFitTests
{
    private static readonly ReminderOptions Defaults = new();

    private static ImportResult Import(string tsv, FightDefinition fight, SlashReading slashes = SlashReading.Auto)
    {
        var tab = SheetReaders.FromDelimited(tsv.ReplaceLineEndings("\n"), '\t', "Pasted");
        return new PlanImporter().Import(new SheetDocument { Title = "Test sheet", Tabs = [tab] }, new ImportOptions
        {
            Fight = fight,
            Actions = RepoFiles.Actions,
            Aliases = RepoFiles.Aliases,
            ImportedDate = PlanImports.Date,
            Slashes = slashes,
        });
    }

    /// <summary>A sheet with a mechanic column and the given player columns; rows are "label\tcell\tcell…".</summary>
    private static string Sheet(string phase, string columns, params string[] rows) => $"{phase}\t{columns}\n" + string.Join("\n", rows);

    /// <summary>A one-phase fight ("P1") at <paramref name="level"/> whose mechanics hit once, at the given times.</summary>
    private static FightDefinition Fight(int level, params (string Name, double Hit)[] mechanics) => new()
    {
        Key = "test",
        Name = "Test Fight",
        Territory = 1,
        SyncLevel = level,
        TimelineFile = "test.txt",
        Phases = [new Phase { Id = "p1", Name = "Phase One", Start = 0 }],
        Mechanics = mechanics.Select(m => new Mechanic
        {
            Id = "p1." + m.Name.ToLowerInvariant(),
            Name = m.Name,
            Phase = "p1",
            Hit = m.Hit,
            Hits = [m.Hit],
            Kind = MechanicKind.Raidwide,
            Aliases = [m.Name],
        }).ToList(),
    };

    private static PlanEntry At(ImportResult r, string mechanic, string action) =>
        Assert.Single(r.Plan.Entries, e => e.Mechanic == mechanic && e.Names().Contains(action));

    private static EntryTiming Timing(PlanEntry entry, FightDefinition fight) =>
        TimingRules.ForEntry(entry, fight.GetMechanic(entry.Mechanic)!, fight, RepoFiles.Actions);

    /// <summary>The recast conflicts <c>check</c> reports for a seat, as the plugin chooses its column.</summary>
    private static List<PlanFinding> Conflicts(ImportResult r, FightDefinition fight, Job job, string? slot = null) =>
        PlanChecker.Check(fight, RepoFiles.Actions, r.Plan, ColumnSuggestion.Context(ColumnSuggestion.Suggest(r.Plan, job, slot), job, slot, fight.SyncLevel))
            .Where(f => f.Code == "recast-conflict").ToList();

    private static List<ImportDiagnostic> Fits(ImportResult r) => r.Diagnostics.Where(d => d.Code == "recast-fit").ToList();

    // ---- a move ------------------------------------------------------------------------------------------------

    [Fact]
    public void AnAutomaticPress_WaitsUntilItsActionIsReady_ItsNowCueComesThen()
    {
        // TEA 'LL/BJCC' G40/G52: Asylum after Photon (pressed at 263.5, the heal-up) and again at Gavel (356.5), where its
        // regen lead (-7.2 s) would press it 85.8 s later. It waits until Asylum is ready (353.5): its reminder turns due
        // then, and it is pressed 1.5 s before the hit.
        var tea = PlanImports.Tea;
        var r = Import(Sheet("P2", "White Mage", "Photon\tAsylum + Plenary", "Gavel\tAsylum"), tea);

        var photon = At(r, "p2.photon_1", "Asylum");
        var gavel = At(r, "p2.gavel", "Asylum");
        Assert.Null(photon.Offset);
        Assert.Equal(-1.5, gavel.Offset);
        Assert.Equal(Timing(photon, tea).PressAt + 90, Timing(gavel, tea).DueAt(Defaults), 6);
        Assert.Empty(Conflicts(r, tea, Job.WHM));

        // The info names the action, the mechanic, the earlier use it waits for, and the old and new offsets.
        var info = Assert.Single(Fits(r));
        Assert.Equal((DiagnosticSeverity.Info, "'Pasted'!B3"), (info.Severity, info.Src));
        Assert.Equal(
            "Asylum at p2.gavel is planned 85.8 s after the Asylum at p2.photon_1 (recast 90.0 s): it is pressed 1.5 s before its hit instead of 7.2 s, so that its NOW cue comes when Asylum is ready again.",
            info.Message);
    }

    [Fact]
    public void ThePressIsRoundedUpToATenthOfASecond_NeverBeforeTheActionIsReady()
    {
        // Asylum on Alpha (100) is pressed at 92.8 and ready again at 182.8, so Bravo's (188.07) waits until 184.3: 3.77 s
        // before its hit, written -3.7 (184.37), not -3.8 (184.27).
        var fight = Fight(80, ("Alpha", 100), ("Bravo", 188.07));
        var r = Import(Sheet("P1", "White Mage", "Alpha\tAsylum", "Bravo\tAsylum"), fight);

        var bravo = At(r, "p1.bravo", "Asylum");
        Assert.Equal(-3.7, bravo.Offset);
        Assert.True(Timing(bravo, fight).DueAt(Defaults) >= 182.8);
        Assert.Empty(Conflicts(r, fight, Job.WHM));
    }

    // ---- the recast as check replays it -------------------------------------------------------------------------

    [Theory]
    [InlineData(80, -1.0)] // one charge: ready at 126 (96 + 30), pressed at 127, 1 s before Bravo, as "(Late)" would
    [InlineData(90, null)] // two charges from level 88: the second Benison has one
    public void Charges_AreTheSyncLevels(int level, double? offset)
    {
        // Divine Benison on Alpha (100) and Bravo (128), each 4 s before its hit.
        var fight = Fight(level, ("Alpha", 100), ("Bravo", 128));
        var r = Import(Sheet("P1", "White Mage", "Alpha\tBenison", "Bravo\tBenison"), fight);

        Assert.Null(At(r, "p1.alpha", "Divine Benison").Offset);
        Assert.Equal(offset, At(r, "p1.bravo", "Divine Benison").Offset);
        Assert.Empty(Conflicts(r, fight, Job.WHM));
    }

    [Fact]
    public void Charges_AUseWaitsForTheFirstChargeToComeBack()
    {
        // Level 90, two charges: Benisons at 96 and 106 spend both, the first comes back at 126; Charlie's (128) is
        // pressed at 127, 1 s before its hit.
        var fight = Fight(90, ("Alpha", 100), ("Bravo", 110), ("Charlie", 128));
        var r = Import(Sheet("P1", "White Mage", "Alpha\tBenison", "Bravo\tBenison", "Charlie\tBenison"), fight);

        Assert.Equal([null, null, -1.0], new[] { "p1.alpha", "p1.bravo", "p1.charlie" }.Select(m => At(r, m, "Divine Benison").Offset));
        Assert.Contains("(2 charges, recast 30.0 s)", Assert.Single(Fits(r)).Message, StringComparison.Ordinal);
        Assert.Contains("after the Divine Benison at p1.alpha", Assert.Single(Fits(r)).Message, StringComparison.Ordinal);
        Assert.Empty(Conflicts(r, fight, Job.WHM));
    }

    [Fact]
    public void ActionsThatShareARecast_WaitForEachOther()
    {
        // Bloodwhetting and Nascent Flash share one 25 s recast. Bloodwhetting on Alpha (100) at 97.6 is ready again at
        // 122.6: Bravo's (124) Nascent Flash, at 121.6 by its lead, waits until then and is pressed 1 s before the hit.
        var fight = Fight(90, ("Alpha", 100), ("Bravo", 124));
        var r = Import(Sheet("P1", "Warrior", "Alpha\tBloodwhetting + Rampart", "Bravo\tNascent Flash"), fight);

        Assert.Equal(-1.0, At(r, "p1.bravo", "Nascent Flash").Offset);
        Assert.Contains("after the Bloodwhetting at p1.alpha, which shares its recast", Assert.Single(Fits(r)).Message, StringComparison.Ordinal);
        Assert.Empty(Conflicts(r, fight, Job.WAR));
    }

    [Theory]
    [InlineData("(Asylum)", SlashReading.Auto)]            // in brackets: optional
    [InlineData("Asylum (if needed)", SlashReading.Auto)]  // a condition
    [InlineData("Asylum/Temperance", SlashReading.Either)] // an either/or: the player picks what is ready
    public void AnEarlierPressThatMayNotHappen_KeepsNoActionOnRecast(string first, SlashReading slashes)
    {
        var r = Import(Sheet("P2", "White Mage", "Photon\t" + first, "Gavel\tAsylum"), PlanImports.Tea, slashes);

        Assert.Null(At(r, "p2.gavel", "Asylum").Offset);
        Assert.Empty(Fits(r));
    }

    [Fact]
    public void AnOptionalCall_WaitsForTheActionToo()
    {
        // "Asylum (if up)" at Gavel may not be pressed, so it keeps nothing on recast; but when it is, Asylum is ready
        // only 1.5 s before the hit.
        var tea = PlanImports.Tea;
        var r = Import(Sheet("P2", "White Mage", "Photon\tAsylum + Plenary", "Gavel\tAsylum (if up)"), tea);

        var gavel = At(r, "p2.gavel", "Asylum");
        Assert.True(gavel.Optional);
        Assert.Equal(-1.5, gavel.Offset);
    }

    [Fact]
    public void ATimeInTheSheet_CountsButNeverMoves()
    {
        // P2's zero at 232.5 (Photon at 0:30, Gavel at 2:04). Asylum written at 0:31 (263.5) counts: the untimed one at
        // Gavel waits for it. Asylum written at 1:57 (7 s before Gavel) keeps that time, and check reports the recast.
        var tea = PlanImports.Tea;
        const string Head = "Time\tMechanic\tWhite Mage\n\tP2\t\n0:30\tPhoton\tAsylum + Plenary (0:31)\n";

        var untimed = Import(Head + "2:04\tGavel\tAsylum", tea);
        Assert.Equal(1.0, At(untimed, "p2.photon_1", "Asylum").Offset);
        Assert.Equal(-1.5, At(untimed, "p2.gavel", "Asylum").Offset);

        var timed = Import(Head + "2:04\tGavel\tAsylum (1:57)", tea);
        Assert.Equal(-7.0, At(timed, "p2.gavel", "Asylum").Offset);
        Assert.Empty(Fits(timed));
        Assert.Contains(Conflicts(timed, tea, Job.WHM), f => f.Message.StartsWith("Asylum planned at P2 1:04 and P2 2:30", StringComparison.Ordinal));
    }

    // ---- the bound: "(Late)" ------------------------------------------------------------------------------------

    [Fact]
    public void WhenTheActionIsReadyOnlyAfterLate_ThePressStays()
    {
        // TEA 'LL/BJCC' G13/G17: Divine Benison (one charge at 80) at Doll Feeds (46.3) and 21.6 s later for Protean 1
        // (71.9): ready at 76.3, after the hit. It stays where it is, and check reports the recast.
        var tea = PlanImports.Tea;
        var r = Import(Sheet("P1", "White Mage", "Doll Feeds\tBenison", "Protean 1\tBenison"), tea);

        Assert.Null(At(r, "p1.protean_1", "Divine Benison").Offset);
        Assert.Empty(Fits(r));
        Assert.Single(Conflicts(r, tea, Job.WHM));
    }

    [Theory]
    [InlineData(128, "Soil", -1.0)]          // ready at 125.5, 2.5 s before the hit: pressed 1 s before it
    [InlineData(128, "Succor + Soil", null)] // the Succor's 2 s cast must end 1 s before the hit: pressed 3 s before it at the latest, not yet ready
    [InlineData(129, "Succor + Soil", -3.0)] // ready 3.5 s before the hit: pressed 3 s before it, its cast ending 1 s before
    public void ACastInTheEntry_EndsBeforeTheHit(double bravo, string cell, double? offset)
    {
        // Sacred Soil on Alpha (100) at 95.5 is ready again at 125.5.
        var fight = Fight(90, ("Alpha", 100), ("Bravo", bravo));
        var r = Import(Sheet("P1", "Scholar", "Alpha\tSoil", "Bravo\t" + cell), fight);

        Assert.Equal(offset, At(r, "p1.bravo", "Sacred Soil").Offset);
        Assert.Equal(offset is null ? 1 : 0, Conflicts(r, fight, Job.SCH).Count);
    }

    // ---- chains, seats and entries ----------------------------------------------------------------------------

    [Fact]
    public void AChainOfThreeUses_EachWaitsForTheOneBeforeAsItIsPressed()
    {
        // Asylum on Alpha (100), Bravo (188) and Charlie (276), each 7.2 s before its hit by default. Bravo's waits for
        // Alpha's (ready at 182.8): pressed at 184.3. Charlie's then waits for Bravo's as moved (ready at 274.3), not as
        // planned (270.8): pressed 1 s before its hit.
        var fight = Fight(80, ("Alpha", 100), ("Bravo", 188), ("Charlie", 276));
        var r = Import(Sheet("P1", "White Mage", "Alpha\tAsylum", "Bravo\tAsylum", "Charlie\tAsylum"), fight);

        Assert.Equal([null, -3.7, -1.0], new[] { "p1.alpha", "p1.bravo", "p1.charlie" }.Select(m => At(r, m, "Asylum").Offset));
        Assert.Equal(2, Fits(r).Count);
        Assert.Empty(Conflicts(r, fight, Job.WHM));
    }

    [Fact]
    public void AnOffsetTheImporterWrote_MayMove()
    {
        // Temperance on Bravo (216) is pressed 2.5 s before its hit so that it lasts through Charlie (231), where the call
        // is "Temperance (still active)". The one on Alpha (100, at 94) is ready again only at 214: Bravo's waits until
        // then and is pressed 1 s before its hit, still lasting through Charlie.
        var fight = Fight(80, ("Alpha", 100), ("Bravo", 216), ("Charlie", 231));
        var r = Import(Sheet("P1", "White Mage", "Alpha\tTemperance", "Bravo\tTemperance", "Charlie\tTemperance"), fight);

        Assert.Equal(["note:Temperance (still active)"], Assert.Single(r.Plan.Entries, e => e.Mechanic == "p1.charlie").Names());
        Assert.Equal(-1.0, At(r, "p1.bravo", "Temperance").Offset);
        Assert.Contains("pressed 1.0 s before its hit instead of 2.5 s", Assert.Single(Fits(r)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntrySeveralSeatsSee_TakesTheLatestPressAnyOfThemNeeds()
    {
        // The MT column's Reprisal on Bravo (159) is every tank's in MT. A Warrior in MT also follows the Warrior column,
        // whose Reprisal on Alpha (100, at 97) is ready again at 157: the MT entry waits for it, for every tank.
        var fight = Fight(80, ("Alpha", 100), ("Bravo", 159));
        var r = Import(Sheet("P1", "MT\tWarrior", "Alpha\t\tReprisal", "Bravo\tReprisal\t"), fight);

        var mt = At(r, "p1.bravo", "Reprisal");
        Assert.Equal(("MT", (Job?)null, -1.0), (mt.Who.Slot, mt.Who.Job, mt.Offset!.Value));
        Assert.Null(At(r, "p1.alpha", "Reprisal").Offset);
        Assert.Empty(Conflicts(r, fight, Job.WAR, "MT"));
    }

    [Theory]
    [InlineData("Asylum + Temperance", -1.5)] // ready at 353.5, 3 s before Gavel
    [InlineData("Asylum + Medica II", -3.0)]  // the Medica II's 2 s cast must end 1 s before the hit
    public void AnEntryWithSeveralItems_MovesWhole(string cell, double offset)
    {
        var tea = PlanImports.Tea;
        var r = Import(Sheet("P2", "White Mage", "Photon\tAsylum + Plenary", "Gavel\t" + cell), tea);

        var gavel = At(r, "p2.gavel", "Asylum");
        Assert.Equal(2, gavel.Do.Count);
        Assert.Equal(offset, gavel.Offset);
        var presses = new ReminderScheduler(tea, [gavel], RepoFiles.Actions, Defaults).All.Select(x => x.PressAt).Distinct();
        Assert.Equal(356.5 + offset, Assert.Single(presses), 6);
    }

    [Theory]
    [InlineData("Kerachole → Holos", -1.0)] // the first step waits for Kerachole
    [InlineData("Holos → Kerachole", null)] // Kerachole comes after Holos in game: the sequence keeps its time
    public void ASequence_WaitsForItsFirstStepOnly(string cell, double? offset)
    {
        // Kerachole on Alpha (100) at 95.5 is ready again at 125.5. check times every step of Bravo's (128) sequence at its
        // press; in game only the first step is pressed then.
        var fight = Fight(90, ("Alpha", 100), ("Bravo", 128));
        var r = Import(Sheet("P1", "Sage", "Alpha\tKerachole", "Bravo\t" + cell), fight);

        var bravo = At(r, "p1.bravo", "Kerachole");
        Assert.True(bravo.Seq);
        Assert.Equal(offset, bravo.Offset);
    }
}
