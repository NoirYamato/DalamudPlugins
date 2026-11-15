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

    private static ImportResult Import(string tsv, FightDefinition fight, SlashReading slashes = SlashReading.Auto) =>
        Import(fight, slashes, ("Pasted", tsv));

    /// <summary>A sheet of several tabs (name, rows).</summary>
    private static ImportResult Import(FightDefinition fight, SlashReading slashes, params (string Name, string Tsv)[] tabs)
    {
        var sheetTabs = tabs.Select(t => SheetReaders.FromDelimited(t.Tsv.ReplaceLineEndings("\n"), '\t', t.Name)).ToList();
        return new PlanImporter().Import(new SheetDocument { Title = "Test sheet", Tabs = sheetTabs }, new ImportOptions
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

    [Fact]
    public void AnItemOfASeriesCall_IsReplayedAtItsOwnPress()
    {
        // Waves Alpha to Delta at 100, 103, 106 and 109 (a series), and Late (136). "Succor + Soil" on Charlie is timed by
        // the Succor's 2 s cast (101.5, its own -4.5 s), but the instant Soil waits until Bravo + 0.3 s (103.3), as check
        // replays it. Late's Soil (131.5) is then 28.2 s after it, not the entry's 30 s, and waits until Soil is ready
        // again (133.3): -1.2 s.
        var single = Fight(90, ("Alpha", 100), ("Bravo", 103), ("Charlie", 106), ("Delta", 109), ("Late", 136));
        var waves = new Mechanic { Id = "p1.waves", Name = "Waves", Phase = "p1", Hit = 100, Hits = [100, 103, 106, 109], Kind = MechanicKind.Multihit, Series = true, Aliases = ["Waves"] };
        var fight = new FightDefinition
        {
            Key = single.Key,
            Name = single.Name,
            Territory = single.Territory,
            SyncLevel = single.SyncLevel,
            TimelineFile = single.TimelineFile,
            Phases = single.Phases,
            Mechanics = [waves, .. single.Mechanics],
        };
        var r = Import(Sheet("P1", "Scholar", "Charlie\tSuccor + Soil", "Late\tSoil"), fight);

        var charlie = Timing(At(r, "p1.charlie", "Sacred Soil"), fight);
        Assert.Equal((101.5, 103.3), (Math.Round(charlie.PressAt, 2), Math.Round(charlie.Items[1].PressAt, 2)));
        Assert.Equal(-1.2, At(r, "p1.late", "Sacred Soil").Offset);
        Assert.Empty(Conflicts(r, fight, Job.SCH));
        Assert.Equal(
            "Sacred Soil at p1.late is planned 28.2 s after the Sacred Soil at p1.charlie (recast 30.0 s): it is pressed 1.2 s before its hit instead of 4.5 s, so that its NOW cue comes when Sacred Soil is ready again.",
            Assert.Single(Fits(r)).Message);
    }

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
    public void AWhenReadyCall_KeepsItsPress_AndKeepsItsActionOnRecast()
    {
        // Reprisal (recast 60 s, 3 s before the hit) on Alpha (100, at 97), "on CD" on Bravo (159) and again on Charlie (218).
        // Bravo's reminder waits for Reprisal in game (it stays due while Reprisal is on cooldown): it keeps its press
        // (156), and check says how late it would be pressed. Charlie's waits for Bravo's as planned: Reprisal is ready
        // again at 216, so it is pressed 1 s before its hit.
        var fight = Fight(80, ("Alpha", 100), ("Bravo", 159), ("Charlie", 218));
        var r = Import(Sheet("P1", "Warrior", "Alpha\tReprisal", "Bravo\tReprisal on CD", "Charlie\tReprisal"), fight);

        var bravo = At(r, "p1.bravo", "Reprisal");
        Assert.True(bravo.WhenReady);
        Assert.Null(bravo.Offset);
        Assert.Equal(-1.0, At(r, "p1.charlie", "Reprisal").Offset);
        Assert.StartsWith("Reprisal at p1.charlie is planned 59.0 s after the Reprisal at p1.bravo", Assert.Single(Fits(r)).Message, StringComparison.Ordinal);
        Assert.EndsWith("(planned when ready: it would be pressed 1 s late).", Assert.Single(Conflicts(r, fight, Job.WAR)).Message, StringComparison.Ordinal);
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

    [Fact]
    public void WhenTheMoveWouldPressAnotherItemEarlier_ThePressStays()
    {
        // TOP's Wave Cannon 2 (protean 1269.9, Wild Charge 1278.2): "Rampart + Reprisal" presses the Rampart 6 s before
        // the hit and the Reprisal on its own 0.5 s before it, so that it lasts through the Wild Charge. The Rampart pressed
        // for Cosmo Arrow 1 (1176.2) is ready again at 1266.2: a move to 2.2 s before the hit would press the Reprisal
        // 1.7 s earlier, short of the Wild Charge. The entry keeps its presses, and check reports the Rampart.
        var top = PlanImports.Top;
        var r = Import(Sheet("P6", "MT", "Cosmo Arrow 1\tRampart", "WC2\tRampart + Reprisal"), top);

        var entry = At(r, "p6.wave_cannon_2", "Rampart");
        Assert.Null(entry.Offset);
        Assert.Empty(Fits(r));
        var timing = Timing(entry, top);
        Assert.Equal([-6.0, -0.5], timing.Items.Select(i => Math.Round(i.PressAt - timing.Hit, 3)));
        Assert.Contains(Conflicts(r, top, Job.PLD, "MT"), f => f.ActionId == RepoFiles.Action("Rampart").Id);
    }

    [Fact]
    public void WhenTheMoveWouldPressAFollowUpAfterItsTriggersWindow_ThePressStays()
    {
        // "Soil + Seraph" on Charlie reads its Seraph as the Consolation for the Seraph summoned for Bravo (104.5), 20 s
        // before the entry's press: Seraph is out for 22 s. The Soil pressed for Alpha (95.5) is ready again at 125.5, so
        // the fit would press the entry 2 s before its hit (127), the Consolation 22.5 s after the summon, when Seraph is
        // gone. The entry keeps its press, and check reports the Soil.
        var fight = Fight(90, ("Alpha", 100), ("Bravo", 107.5), ("Charlie", 129));
        var r = Import(Sheet("P1", "Scholar", "Alpha\tSoil", "Bravo\tSeraph", "Charlie\tSoil + Seraph"), fight);

        var charlie = At(r, "p1.charlie", "Sacred Soil");
        Assert.Equal(["Sacred Soil", "Consolation"], charlie.Names());
        Assert.Null(charlie.Offset);
        Assert.Empty(Fits(r));
        var findings = PlanChecker.Check(fight, RepoFiles.Actions, r.Plan, new PlayerContext(Job.SCH, fight.SyncLevel));
        Assert.DoesNotContain(findings, f => f.Code == "follow-up-without-trigger");
        Assert.Contains(Conflicts(r, fight, Job.SCH), f => f.ActionId == RepoFiles.Action("Sacred Soil").Id);
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

    [Theory]
    [InlineData(128, "Kerachole", -1.0)]                 // ready at 125.5, 2.5 s before the hit: pressed 1 s before it
    [InlineData(128, "EukProg + Kerachole", null)]       // Eukrasia (a 2.5 s GCD) must come first: 3.5 s before the hit at the latest, not yet ready
    [InlineData(129.2, "EukProg + Kerachole", -3.5)]     // ready 3.7 s before the hit: pressed 3.5 s before it, the shield up 1 s before
    public void AFollowUpOfAGcd_LeavesTimeForTheGcdBeforeIt(double bravo, string cell, double? offset)
    {
        // Level 90, a Sage: Kerachole on Alpha (100) at 95.5 is ready again at 125.5. Bravo's Eukrasian Prognosis (a shield,
        // 4 s before the hit) needs Eukrasia first: pressed at "(Late)"'s -1 s, Eukrasia would take the last GCD before the
        // hit and the shield would come after it.
        var fight = Fight(90, ("Alpha", 100), ("Bravo", bravo));
        var r = Import(Sheet("P1", "Sage", "Alpha\tKerachole", "Bravo\t" + cell), fight);

        Assert.Equal(offset, At(r, "p1.bravo", "Kerachole").Offset);
        Assert.Equal(offset is null ? 1 : 0, Conflicts(r, fight, Job.SGE).Count);
    }

    [Theory]
    [InlineData("Neutral", 219.0)] // ready 3.5 s before the hit: its Aspected Helios needs 4.5 s
    [InlineData("Star", 159.0)]    // ready 9 s before the hit: the star needs 10 s to mature
    public void AnActionWithALeadOfItsOwn_IsNeverPressedLaterThanThatLead(string cell, double bravo)
    {
        // Neutral Sect (recast 120 s, pressed 4.5 s before the hit: time to cast the Aspected Helios that puts up its
        // barrier) and Earthly Star (recast 60 s, 10 s before: time to mature), on Alpha (100) and again on Bravo before
        // they are ready: pressed when ready, they would not do their job for Bravo's hit. The press stays, and check
        // reports the recast.
        var fight = Fight(80, ("Alpha", 100), ("Bravo", bravo));
        var r = Import(Sheet("P1", "Astrologian", "Alpha\t" + cell, "Bravo\t" + cell), fight);

        Assert.Null(Assert.Single(r.Plan.Entries, e => e.Mechanic == "p1.bravo").Offset);
        Assert.Empty(Fits(r));
        Assert.Single(Conflicts(r, fight, Job.AST));
    }

    [Fact]
    public void AMoveStopsAtTheLeadOfAnActionOfTheEntry()
    {
        // A Paladin's Reprisal on Alpha (100, at 97) is ready again at 157. "Reprisal + Wings" on Bravo (159.2), timed by
        // its Reprisal (-3 s), waits for it, but Passage of Arms, pressed last, comes 2 s before the hit at the latest: the
        // entry is pressed then, 0.2 s after Reprisal is ready, not 1 s before the hit.
        var fight = Fight(80, ("Alpha", 100), ("Bravo", 159.2));
        var r = Import(Sheet("P1", "Paladin", "Alpha\tReprisal", "Bravo\tReprisal + Wings"), fight);

        var bravo = At(r, "p1.bravo", "Reprisal");
        Assert.Equal(["Reprisal", "Passage of Arms"], bravo.Names());
        Assert.Equal(-2.0, bravo.Offset);
        Assert.EndsWith(
            "it is pressed 2.0 s before its hit instead of 3.0 s, as late as the 2.0 s lead of Passage of Arms allows, once Reprisal is ready again.",
            Assert.Single(Fits(r)).Message,
            StringComparison.Ordinal);
        Assert.Empty(Conflicts(r, fight, Job.PLD));
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
        Assert.Empty(Conflicts(r, fight, Job.WHM));

        // Each info gives the gap as the sheet plans both presses (88 s), and Charlie's says that Bravo's moved too.
        Assert.Equal(
            [
                "Asylum at p1.bravo is planned 88.0 s after the Asylum at p1.alpha (recast 90.0 s): it is pressed 3.7 s before its hit instead of 7.2 s, so that its NOW cue comes when Asylum is ready again.",
                "Asylum at p1.charlie is planned 88.0 s after the Asylum at p1.bravo, which is pressed later too (recast 90.0 s): it is pressed 1.0 s before its hit instead of 7.2 s, as late as \"(Late)\" would press it, once Asylum is ready again.",
            ],
            Fits(r).Select(d => d.Message));
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

    [Fact]
    public void ATeamCompositionColumn_IsReplayedForThePartyThatPlaysIt()
    {
        // Two tank-pair tabs, "WAR/DRK" and "WAR/PLD". Without a party the plugin gives a Warrior the first pair's column;
        // next to a Paladin it gives "WAR/PLD WAR", whose Reprisal on Alpha (100, at 97) is ready again at 157: Bravo's
        // (159) waits until then and is pressed 1 s before its hit.
        var fight = Fight(80, ("Alpha", 100), ("Bravo", 159));
        var r = Import(fight, SlashReading.Auto,
            ("WAR/DRK", "P1\tWAR\tDRK\nAlpha\tRampart\tRampart\nBravo\tVengeance\tShadow Wall"),
            ("WAR/PLD", "P1\tWAR\tPLD\nAlpha\tReprisal\tRampart\nBravo\tReprisal\tSentinel"));

        Assert.Equal("WAR/DRK WAR", ColumnSuggestion.Suggest(r.Plan, Job.WAR, null).AssigneeKey);
        var pick = ColumnSuggestion.Suggest(r.Plan, Job.WAR, null, [Job.WAR, Job.PLD]);
        Assert.Equal("WAR/PLD WAR", pick.AssigneeKey);
        Assert.Equal(-1.0, Assert.Single(r.Plan.Entries, e => e.Mechanic == "p1.bravo" && e.Who.Assignee == "WAR/PLD WAR").Offset);
        Assert.DoesNotContain(
            PlanChecker.Check(fight, RepoFiles.Actions, r.Plan, ColumnSuggestion.Context(pick, Job.WAR, null, fight.SyncLevel)),
            f => f.Code == "recast-conflict");
    }

    [Fact]
    public void TheJobsOwnColumn_IsReplayedForAPartyThatPlaysNoComposition()
    {
        // A combined "WHM + SCH" tab first, then a "White Mage" tab with the same mechanics (as in Carry the Clear). Without
        // a party, or next to a Scholar, the plugin gives a White Mage the combined tab's column, and the White Mage tab is
        // the same seat written twice; next to a Sage the party plays no composition of the sheet, and it gives the White
        // Mage tab, whose Asylum on Alpha (100, at 92.8) is ready again at 182.8: Bravo's (188) waits until then.
        var fight = Fight(80, ("Alpha", 100), ("Bravo", 188));
        var r = Import(fight, SlashReading.Auto,
            ("WHM + SCH", "P1\tWhite Mage\tScholar\nAlpha\tTemperance\tSoil\nBravo\tBenison\tSoil"),
            ("White Mage", "P1\tWhite Mage\nAlpha\tAsylum\nBravo\tAsylum"));

        Assert.Equal("WHM + SCH White Mage", ColumnSuggestion.Suggest(r.Plan, Job.WHM, null).AssigneeKey);
        var pick = ColumnSuggestion.Suggest(r.Plan, Job.WHM, null, [Job.WHM, Job.SGE]);
        Assert.Equal("White Mage", pick.AssigneeKey);
        Assert.Equal(-3.7, Assert.Single(r.Plan.Entries, e => e.Mechanic == "p1.bravo" && e.Who.Assignee == "White Mage").Offset);
        Assert.DoesNotContain(
            PlanChecker.Check(fight, RepoFiles.Actions, r.Plan, ColumnSuggestion.Context(pick, Job.WHM, null, fight.SyncLevel)),
            f => f.Code == "recast-conflict");
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

    [Theory]
    [InlineData("Sage", "Zoe", "Zoe → Holos", -2.5)]                      // Holos is woven right after Zoe: the NOW cue comes when Zoe is ready
    [InlineData("Sage", "Zoe", "Zoe → EukProg → Holos → Kerachole", null)] // Eukrasia and Eukrasian Prognosis, two GCDs, come after Zoe
    [InlineData("Scholar", "Recitation", "Recitation → Succor", null)]     // a 2 s cast after Recitation
    public void ASequenceWithAGcdOrACastAfterItsFirstStep_KeepsItsPress(string column, string first, string cell, double? offset)
    {
        // Zoe and Recitation (buffs, 3 s before the hit, recast 90 s) on Alpha (100, at 97) are ready again at 187. Bravo's
        // (191) sequence is pressed before that, 6 s before its hit (Holos's lead) or 4.5 s (Succor's). Moved for its first
        // step, its later steps must still come before the hit: a woven oGCD does, a GCD or a cast after it would not.
        var fight = Fight(90, ("Alpha", 100), ("Bravo", 191));
        var r = Import(Sheet("P1", column, "Alpha\t" + first, "Bravo\t" + cell), fight);

        var action = RepoFiles.Action(first == "Zoe" ? "Zoe" : "Recitation");
        var bravo = At(r, "p1.bravo", action.Name);
        Assert.True(bravo.Seq);
        Assert.Equal(offset, bravo.Offset);
        Assert.Equal(offset is null ? 1 : 0, Conflicts(r, fight, action.Jobs[0]).Count(f => f.ActionId == action.Id));
    }
}
