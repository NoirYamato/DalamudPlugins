using MitCue.Core.Fights;
using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Core.Tests.Data;

namespace MitCue.Core.Tests.Import;

/// <summary>
/// Cell rules found on LPDU's TEA sheet ("[LPDU] TEA Heal/Mit"): "/" as "and" when the sheet joins actions no other way,
/// either/or alternatives that mean nothing, the sheet's shorthands, parentheticals that name the token's own actions or
/// a target, a party buff named again while it lasts, a limit break in two columns and "(Late)".
/// </summary>
public class ImportCellRulesTests
{
    private static readonly CellParser And = new(new CellParserOptions { SlashJoins = true });

    private static ImportResult Import(string tsv, SlashReading slashes = SlashReading.Auto, FightDefinition? fight = null)
    {
        var tab = SheetReaders.FromDelimited(tsv.ReplaceLineEndings("\n"), '\t', "Pasted");
        return new PlanImporter().Import(new SheetDocument { Title = "Test sheet", Tabs = [tab] }, new ImportOptions
        {
            Fight = fight ?? PlanImports.Top,
            Actions = RepoFiles.Actions,
            Aliases = RepoFiles.Aliases,
            ImportedDate = PlanImports.Date,
            Slashes = slashes,
        });
    }

    /// <summary>A sheet with a mechanic column and the given player columns; rows are "label\tcell\tcell…".</summary>
    private static string Sheet(string phase, string columns, params string[] rows) => $"{phase}\t{columns}\n" + string.Join("\n", rows);

    private static ImportDiagnostic SlashReadingOf(ImportResult r) => Assert.Single(r.Diagnostics, d => d.Code == "slash-reading");

    // ------------------------------------------------------------------ the parser

    [Fact]
    public void SlashJoins_SplitsSlashedActionsIntoTokens()
    {
        var cell = And.Parse("Soil/Reci/Deploy/Whisper");
        Assert.Equal(["Soil", "Reci", "Deploy", "Whisper"], Assert.Single(cell.Items).Tokens.Select(t => t.Head));
        Assert.Equal((0, 3), (cell.Joins, cell.SlashJoins));

        // Without SlashJoins it is one token of four alternatives.
        Assert.Equal(["Soil", "Reci", "Deploy", "Whisper"], Assert.Single(CellParser.Default.Parse("Soil/Reci/Deploy/Whisper").Tokens).Alternatives);
    }

    [Theory]
    [InlineData("Short CD to R1/2", "Short CD", "R1/2")]
    [InlineData("Rampart on MT/OT", "Rampart", "MT/OT")]
    [InlineData("Party Mit (DM/HoL)", "Party Mit", null)]
    [InlineData("Kitchen Sink w/ Rampart", "Kitchen Sink", null)]
    public void SlashJoins_LeavesSlotListsDigitsAndBracketsAlone(string text, string head, string? target)
    {
        var token = Assert.Single(And.Parse(text).Tokens);
        Assert.Equal((head, target), (token.Head, token.Target?.Value));
    }

    [Theory]
    [InlineData("WAR/DRK Rampart")]
    [InlineData("Tank/Healer LB")]
    public void SlashJoins_DoesNotJoinJobsOrPlayers(string text) => Assert.Single(And.Parse(text).Tokens);

    [Fact]
    public void SlashJoins_ATargetBindsToTheTokenItFollows()
    {
        var tokens = Assert.Single(And.Parse("Excog(OT)/Soil/Bless/Succor").Items).Tokens.ToList();
        Assert.Equal(["Excog", "Soil", "Bless", "Succor"], tokens.Select(t => t.Head));
        Assert.Equal("OT", tokens[0].Target?.Value);
        var last = And.Parse("Plenary/Benison (MT)").Tokens.ToList();
        Assert.Equal(((string?)null, "MT"), (last[0].Target?.Value, last[1].Target?.Value));
        var first = And.Parse("Benison (Tank)/Plenary").Tokens.ToList();
        Assert.Equal(("Tank", (string?)null), (first[0].Target?.Value, first[1].Target?.Value));
    }

    [Theory]
    [InlineData("Soil/Succor", 0)]
    [InlineData("Soil/Succor/Seraph\n", 0)]
    [InlineData("Intersection/defensive Cards (OT) ", 0)]
    [InlineData("Benison (Tank)/Plenary", 0)]
    [InlineData("Kera 0:28 / 0:37", 0)]
    [InlineData("Kera + Zoe Eprog", 1)]
    [InlineData("Soil (0:12) / Deploy (0:17)", 1)]
    [InlineData("Recit → Adlo → Deploy", 2)]
    [InlineData("Rampart\nReprisal", 1)]
    [InlineData("Soil; Kera", 1)]
    public void Joins_CountSeparatorsOtherThanSlashesBetweenWords(string text, int joins) => Assert.Equal(joins, CellParser.Default.Parse(text).Joins);

    [Theory]
    [InlineData("(^on LL)", "on LL")]
    [InlineData("(^ if MCH)", "if MCH")]
    [InlineData("↑ on BJ", "on BJ")]
    [InlineData("^", "")]
    public void CaretCells_AreRemarksAboutTheCellAbove(string text, string above)
    {
        var cell = CellParser.Default.Parse(text);
        Assert.Empty(cell.Items);
        Assert.Equal(above, cell.Above);
    }

    [Theory]
    [InlineData("Benison (Range)", "Range")]
    [InlineData("defensive Cards (Ranged)", "Ranged")]
    [InlineData("Excog (Tank)", "Tank")]
    [InlineData("Haima(OT)", "OT")]
    [InlineData("Excog (casters)", "casters")]
    public void TargetWordParentheticals_AreTargets(string text, string target) =>
        Assert.Equal(target, Assert.Single(CellParser.Default.Parse(text).Tokens).Target?.Value);

    [Theory]
    [InlineData("Seraph1/Soil", new[] { "Seraph1", "Soil" })]
    [InlineData("R1/R2", new[] { "R1/R2" })]
    [InlineData("R1/2", new[] { "R1/2" })]
    [InlineData("3rd/4th", new[] { "3rd/4th" })]
    public void ADigitBeforeASlash_SplitsWords_ButNotSlots(string text, string[] alternatives) =>
        Assert.Equal(alternatives, Assert.Single(CellParser.Default.Parse(text).Tokens).Alternatives);

    // ------------------------------------------------------------------ the slash reading

    [Fact]
    public void SlashReading_Auto_IsAnd_WhenNoCellJoinsActionsAnotherWay()
    {
        var r = Import(Sheet("P1", "Scholar", "Loop 1\tSoil/Reci/Deploy/Whisper", "Loop 2\tSoil/Succor", "Pantokrator\tIllum/Seraph"));

        Assert.Equal(["Sacred Soil", "Recitation", "Deployment Tactics", "Whispering Dawn"], r.Plan.Entries.Single(e => e.Mechanic == "p1.loop_set1").Names());
        Assert.DoesNotContain(r.Plan.Entries, e => e.Do.Any(i => i.Any is not null));
        var d = SlashReadingOf(r);
        Assert.Equal((DiagnosticSeverity.Info, (string?)null), (d.Severity, d.Src));
        Assert.Contains("read as 'and'", d.Message, StringComparison.Ordinal);
        Assert.Contains("3 cells list several actions of one job with '/', up to 4 (e.g. 'Pasted'!B2 'Soil/Reci/Deploy/Whisper')", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SlashReading_Auto_IsEitherOne_WhenTheSheetJoinsActionsWithPlus()
    {
        var r = Import(Sheet("P6", "DRK", "WC1\tRampart + Shadow Wall", "WC2\tLiving Dead + TBN/Oblation Co-Tank", "Cosmo Arrow\tTBN/Oblation/Dark Mind"));

        Assert.Contains(r.Plan.Entries.Where(e => e.Mechanic == "p6.wave_cannon_2"), e => e.Names().SequenceEqual(["The Blackest Night|Oblation"]) && e.Target == "Co-Tank");
        Assert.Contains("read as 'either one'", SlashReadingOf(r).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SlashReading_Auto_IsEitherOne_WithoutLongLists()
    {
        // Two actions per cell at most: no evidence that "/" means "and".
        var r = Import(Sheet("P1", "Scholar", "Loop 1\tSoil/Succor", "Loop 2\tExcog/Indom"));

        Assert.All(r.Plan.Entries, e => Assert.NotNull(Assert.Single(e.Do).Any));
        Assert.Contains("read as 'either one'", SlashReadingOf(r).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SlashReading_IsReported_EvenWithoutSlashes()
    {
        var d = SlashReadingOf(Import(Sheet("P1", "MT", "Loop 1\tRampart", "Loop 2\tReprisal")));
        Assert.Contains("no cell lists several actions of one job with '/'", d.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SlashReading.Either)]
    [InlineData(SlashReading.All)]
    public void SlashReading_Option_Overrides(SlashReading slashes)
    {
        var r = Import(Sheet("P1", "Scholar", "Loop 1\tSoil/Reci/Deploy/Whisper", "Loop 2\tSoil/Succor/Indom", "Loop 3\tExcog + Succor"), slashes);

        var loop1 = r.Plan.Entries.Single(e => e.Mechanic == "p1.loop_set1");
        if (slashes == SlashReading.All)
            Assert.Equal(["Sacred Soil", "Recitation", "Deployment Tactics", "Whispering Dawn"], loop1.Names());
        else
            Assert.NotNull(Assert.Single(loop1.Do).Any);
        Assert.Contains("as chosen in the import options", SlashReadingOf(r).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SlashReading_And_KeepsASequenceAlias()
    {
        var entry = Assert.Single(Import(Sheet("P1", "Scholar", "Loop 1\tSpread-Lo/Soil", "Loop 2\tSoil/Reci/Deploy"), SlashReading.All).Plan.Entries, e => e.Mechanic == "p1.loop_set1");
        Assert.Equal(["Adloquium", "Deployment Tactics", "Sacred Soil"], entry.Names());
        Assert.True(entry.Seq);
    }

    [Fact]
    public void SlashReading_EitherOne_GivesAChoiceOfSeveralActionsAnEntryOfItsOwn()
    {
        var r = Import(Sheet("P1", "Scholar", "Loop 1\tSpread-Lo/Soil"), SlashReading.Either);

        var spreadlo = Assert.Single(r.Plan.Entries, e => e.Names().Contains("Adloquium"));
        Assert.Equal(["Adloquium", "Deployment Tactics"], spreadlo.Names());
        Assert.Equal((true, true, "or Soil"), (spreadlo.Seq, spreadlo.Optional, spreadlo.Note));
        var soil = Assert.Single(r.Plan.Entries, e => e.Names().Contains("Sacred Soil"));
        Assert.Equal(["Sacred Soil"], soil.Names());
        Assert.Equal((true, "or Spread-Lo"), (soil.Optional, soil.Note));
        Assert.Equal(DiagnosticSeverity.Warning, Assert.Single(r.Diagnostics, d => d.Code == "either-or-split").Severity);
    }

    [Fact]
    public void UnresolvedAlternative_IsReported()
    {
        var r = Import(Sheet("P6", "DRK", "WC1\tRampart + Shadow Wall", "WC2\tTBN/Xyzzyx Co-Tank"));

        Assert.Contains(r.Diagnostics, d => d.Code == "unresolved-token" && d.Severity == DiagnosticSeverity.Warning
            && d.Message.Contains("'Xyzzyx'", StringComparison.Ordinal) && d.Message.Contains("either/or", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ shorthands and parentheticals

    [Fact]
    public void TeaShorthands_Resolve()
    {
        var r = Import(Sheet(
            "P1",
            "Sage\tAstrologian\tScholar\tTank 1",
            "Loop 1\tZoeEukProg/Holos/Kerachole\tNeutral/defensive Cards (Ranged)\tSoil/Illum/Seraph1\tAll CD's",
            "Loop 2\tEukDiag/EukProg\tCU\tSeraph2/Bless/Reci/Whisper\tParty Mit (DM)"));

        Assert.DoesNotContain(r.Diagnostics, d => d.Code is "unresolved-token" or "fuzzy-match" && d.Severity == DiagnosticSeverity.Warning);
        Assert.Equal(["Zoe", "Eukrasian Prognosis", "Holos", "Kerachole"], r.Plan.Entries.Single(e => e.Who.Job == Job.SGE && e.Mechanic == "p1.loop_set1").Names());
        Assert.Equal(["Eukrasian Diagnosis", "Eukrasian Prognosis"], r.Plan.Entries.Single(e => e.Who.Job == Job.SGE && e.Mechanic == "p1.loop_set2").Names());
        Assert.Equal(["Neutral Sect"], r.Plan.Entries.Single(e => e.Who.Job == Job.AST && e.Mechanic == "p1.loop_set1" && e.Target is null).Names());
        var cards = r.Plan.Entries.Single(e => e.Who.Job == Job.AST && e.Target == "Ranged");
        Assert.Equal(["the Arrow|the Bole", "the Spire|the Ewer"], cards.Names());
        Assert.Equal(["Sacred Soil", "Fey Illumination", "Summon Seraph", "Consolation"], r.Plan.Entries.Single(e => e.Who.Job == Job.SCH && e.Mechanic == "p1.loop_set1").Names());
        Assert.Equal(["Consolation", "Fey Blessing", "Recitation", "Whispering Dawn"], r.Plan.Entries.Single(e => e.Who.Job == Job.SCH && e.Mechanic == "p1.loop_set2").Names());
        Assert.Equal(["note:All CD's"], r.Plan.Entries.Single(e => e.Who.Slot == "MT" && e.Mechanic == "p1.loop_set1").Names());
        Assert.Equal((Job.DRK, "Dark Missionary"), r.Plan.Entries.Where(e => e.Who.Slot == "MT" && e.Mechanic == "p1.loop_set2").Select(e => (e.Who.Job!.Value, e.Names().Single())).Single());
    }

    [Fact]
    public void ParentheticalNamingTheTokensOwnActions_LimitsTheJobs()
    {
        var r = Import(Sheet("P1", "Tank 1", "Pantokrator\tParty Mit (DM/HoL)"));

        Assert.Equal([(Job.DRK, "Dark Missionary"), (Job.GNB, "Heart of Light")], r.Plan.Entries.Select(e => (e.Who.Job!.Value, e.Names().Single())).OrderBy(x => x.Item1));
        Assert.All(r.Plan.Entries, e => Assert.Null(e.Note));
        Assert.Equal(DiagnosticSeverity.Info, Assert.Single(r.Diagnostics, d => d.Code == "named-actions").Severity);
    }

    [Theory]
    [InlineData("Party Mit (+ Rampart)", "+ Rampart")] // another action: a hint
    [InlineData("Party Mit (if 7 people)", "if 7 people")] // not an action
    public void ParentheticalNamingOtherThings_StaysANote(string cell, string note)
    {
        var r = Import(Sheet("P1", "Tank 1", "Pantokrator\t" + cell));

        Assert.Equal(4, r.Plan.Entries.Count);
        Assert.All(r.Plan.Entries, e => Assert.Equal(note, e.Note));
    }

    // ------------------------------------------------------------------ post-passes

    [Fact]
    public void PartyBuffNamedAgainWhileItLasts_IsANote_OrItsFollowUp()
    {
        var r = Import(Sheet("P2", "White Mage\tScholar\tWAR", "Limitless\tTemperance\tSeraph\tShake", "Cosmo Memory\tTemperance\tSeraph + Soil\tShake"));

        var cm = r.Plan.Entries.Where(e => e.Mechanic == "p2.cosmo_memory").ToList();
        Assert.Equal(["note:Temperance (still active)"], cm.Single(e => e.Who.Job == Job.WHM).Names());
        Assert.Equal(["Consolation", "Sacred Soil"], cm.Single(e => e.Who.Job == Job.SCH).Names());
        Assert.Equal(["note:Shake It Off (still active)"], cm.Single(e => e.Who.Job == Job.WAR).Names());
        Assert.Equal(3, r.Diagnostics.Count(d => d.Code == "still-active" && d.Severity == DiagnosticSeverity.Info));
    }

    [Theory]
    [InlineData("Temperance (if needed)")] // optional
    [InlineData("Benison on MT")] // used on one player
    public void PartyBuffNamedAgain_OptionalOrTargeted_StaysAPress(string again)
    {
        var r = Import(Sheet("P2", "White Mage", "Limitless\tTemperance + Benison on MT", "Cosmo Memory\t" + again));

        Assert.DoesNotContain(r.Diagnostics, d => d.Code == "still-active");
    }

    [Fact]
    public void PartyBuffNamedAgain_AfterItEnds_StaysAPress()
    {
        // Program Loop (first tower 33.1) to Pantokrator (first stack ~67): Temperance's 20 s are over.
        var r = Import(Sheet("P1", "White Mage", "Program Loop\tTemperance", "Pantokrator\tTemperance"));

        Assert.All(r.Plan.Entries, e => Assert.Equal(["Temperance"], e.Names()));
        Assert.DoesNotContain(r.Diagnostics, d => d.Code == "still-active");
    }

    [Fact]
    public void LimitBreakInTwoColumns_IsOptional_ForWhoeverHasIt()
    {
        var r = Import(Sheet("P5", "Tank 1\tTank 2\tH1", "Run: Dynamis\tLB3\tLB3\tLB3"));

        var tanks = r.Plan.Entries.Where(e => e.Who.Slot is "MT" or "OT").ToList();
        Assert.Equal(8, tanks.Count);
        Assert.All(tanks, e => Assert.True(e.Optional));
        Assert.All(tanks.Where(e => e.Who.Slot == "MT"), e => Assert.Equal("or Tank 2", e.Note));
        Assert.All(tanks.Where(e => e.Who.Slot == "OT"), e => Assert.Equal("or Tank 1", e.Note));
        Assert.All(r.Plan.Entries.Where(e => e.Who.Slot == "H1"), e => Assert.False(e.Optional)); // the only healer column
        Assert.Equal(DiagnosticSeverity.Info, Assert.Single(r.Diagnostics, d => d.Code == "shared-limit-break").Severity);
    }

    // ------------------------------------------------------------------ "(Late)"

    [Fact]
    public void Late_PressesAMitigationJustBeforeItsHit()
    {
        // TEA Mega Holy: a 10 s Reprisal pressed 1 s before the first Mega Holy (682.9) still covers the second (690.0).
        var tea = PlanImports.Tea;
        var r = Import(Sheet("P3", "Tank 1\tMelee 1\tScholar", "Mega Holy\tReprisal (Late)\tFeint (later)\tIndom (Late)"), fight: tea);

        var reprisal = Assert.Single(r.Plan.Entries, e => e.Names().Contains("Reprisal"));
        Assert.Equal(("p3.mega_holy_1", PlanImporter.LateOffset, "Late"), (reprisal.Mechanic, reprisal.Offset!.Value, reprisal.Note));
        var press = tea.GetMechanic("p3.mega_holy_1")!.Hit + reprisal.Offset!.Value;
        Assert.True(press + RepoFiles.Action("Reprisal").DurationAt(80)!.Value > tea.GetMechanic("p3.mega_holy_2")!.Hit);
        Assert.Equal(PlanImporter.LateOffset, Assert.Single(r.Plan.Entries, e => e.Names().Contains("Feint")).Offset);

        // A heal is pressed after its hit: "(Late)" changes nothing.
        Assert.Null(Assert.Single(r.Plan.Entries, e => e.Names().Contains("Indomitability")).Offset);
    }

    [Fact]
    public void OtherTimingWords_StayNotes()
    {
        var entry = Assert.Single(Import(Sheet("P1", "MT", "Pantokrator\tReprisal (End of cast)")).Plan.Entries);
        Assert.Equal(((double?)null, "End of cast"), (entry.Offset, entry.Note));
    }
}
