using MitCue.Core.Fights;
using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;
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

    private static ImportResult Import(string tsv, SlashReading slashes = SlashReading.Auto, FightDefinition? fight = null, IReadOnlyList<string>? assignees = null)
    {
        var tab = SheetReaders.FromDelimited(tsv.ReplaceLineEndings("\n"), '\t', "Pasted");
        return Import(new SheetDocument { Title = "Test sheet", Tabs = [tab] }, slashes, fight, assignees);
    }

    private static ImportResult Import(SheetDocument document, SlashReading slashes = SlashReading.Auto, FightDefinition? fight = null, IReadOnlyList<string>? assignees = null) =>
        new PlanImporter().Import(document, new ImportOptions
        {
            Fight = fight ?? PlanImports.Top,
            Actions = RepoFiles.Actions,
            Aliases = RepoFiles.Aliases,
            ImportedDate = PlanImports.Date,
            Slashes = slashes,
            Assignees = assignees,
        });

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
    [InlineData("Benison MT/Co-tank")] // a hyphenated target word
    [InlineData("Excog MT/Off-tank")]
    [InlineData("Haima MT/Co-Tank")]
    [InlineData("Intersection Main-tank/OT")]
    [InlineData("Kera after Stack/Spread")] // mechanic words
    [InlineData("Soil and/or Expedient")]
    public void SlashJoins_DoesNotJoinJobsPlayersOrMechanicWords_NorAndOr(string text) => Assert.Single(And.Parse(text).Tokens);

    [Fact]
    public void SlashJoins_AHyphenatedActionWordStillJoins()
    {
        // TEA 'Alex Prime/PA'!E17: Spreadlo (Adloquium → Deployment Tactics), then Sacred Soil.
        Assert.Equal(["Spread-Lo", "Soil"], And.Parse("Spread-Lo/Soil").Tokens.Select(t => t.Head));
        Assert.Equal(["Kera", "Spread"], And.Parse("Kera/Spread").Tokens.Select(t => t.Head)); // one side an action: joined
    }

    [Theory]
    [InlineData("Benison MT/Co-tank", "Benison", "MT/Co-tank")]
    [InlineData("Intersection Co-tank/MT", "Intersection", "Co-tank/MT")]
    [InlineData("Excog MT/Offtank", "Excog", "MT/Offtank")]
    [InlineData("Haima MT/Co-tank after 1st hit", "Haima", "MT/Co-tank")]
    public void TankTargetsJoinedBySlash_AreOneTarget_InEitherReading(string text, string head, string target)
    {
        foreach (var parser in new[] { And, CellParser.Default })
        {
            var token = Assert.Single(parser.Parse(text).Tokens);
            Assert.Equal((head, target), (token.Head, token.Target?.Value));
            Assert.Equal([head], token.Alternatives);
        }
    }

    [Theory]
    [InlineData(SlashReading.All)]
    [InlineData(SlashReading.Either)]
    public void AndOr_IsEitherOne_InEitherReading(SlashReading slashes)
    {
        Assert.Equal(["Soil", "Expedient"], Assert.Single(new CellParser(new CellParserOptions { SlashJoins = slashes == SlashReading.All }).Parse("Soil and/or Expedient").Tokens).Alternatives);

        var entry = Assert.Single(Import(Sheet("P3", "Scholar", "Hello World\tSoil and/or Expedient"), slashes).Plan.Entries);
        Assert.Equal(["Sacred Soil|Expedient"], entry.Names());
        Assert.Null(entry.Note);
    }

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
    [InlineData(SlashReading.Auto, SlashReading.All)]
    [InlineData(SlashReading.Either, SlashReading.All)] // the option chose the other reading; the sheet's own stays known
    public void SlashReading_TheDocumentsOwnReadingIsInTheResult(SlashReading option, SlashReading document)
    {
        // The AI prompt says which reading MitCue's importer used for the sheet it includes (the import window's trial import).
        var r = Import(Sheet("P1", "Scholar", "Loop 1\tSoil/Reci/Deploy/Whisper", "Loop 2\tSoil/Succor"), option);
        Assert.Equal(document, r.DocumentSlashes);
        Assert.Equal(SlashReading.Either, Import(Sheet("P1", "Scholar", "Loop 1\tSoil/Reci/Deploy/Whisper", "Loop 2\tExcog + Succor"), option).DocumentSlashes);
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
    public void PlainSeraph_InASheetThatNumbersItsSeraphCalls_IsSummonSeraphThenConsolation()
    {
        // LPDU TEA writes "Seraph1" in P1, then a plain "Seraph" at J Wave 5 and again at J Wave 7 ('Alex Prime/PA'!E21, E23)
        // and a lone one at the last Irresistible Grace (E47): the package "Seraph1" is (Summon Seraph and a Consolation);
        // the second one while Seraph is out is the other Consolation (still-active).
        static SheetTab Tab(string name, string tsv) => SheetReaders.FromDelimited(tsv.ReplaceLineEndings("\n"), '\t', name);
        var document = new SheetDocument
        {
            Title = "Test sheet",
            Tabs =
            [
                Tab("LL", "P1\tScholar\tSage\nSplashes\tSoil/Illum/Seraph1\tKerachole"),
                Tab("Prime", "P3\tScholar\nJwaves (4)\tSuccor/Soil/Seraph\nJwaves (6)\tSeraph/Fey Illum"),
                Tab("PA", "P4\tScholar\nIrresistable Grace 2\tSoil/Succor/Seraph"),
            ],
        };
        var r = Import(document, fight: PlanImports.Tea);

        PlanEntry At(string mechanic) => Assert.Single(r.Plan.Entries, e => e.Mechanic == mechanic && e.Who.Job == Job.SCH);
        Assert.Equal(["Succor", "Sacred Soil", "Summon Seraph", "Consolation"], At("p3.j_wave_5").Names());
        Assert.Equal(["Consolation", "Fey Illumination"], At("p3.j_wave_7").Names());
        Assert.Equal(["Sacred Soil", "Succor", "Summon Seraph", "Consolation"], At("p4.irresistible_grace_2").Names());
        var d = Assert.Single(r.Diagnostics, d => d.Code == "seraph-reading");
        Assert.Equal((DiagnosticSeverity.Info, (string?)null), (d.Severity, d.Src));
        Assert.Contains("'LL'!B2 'Soil/Illum/Seraph1'", d.Message, StringComparison.Ordinal);
        Assert.Contains("'Prime'!B2 'Succor/Soil/Seraph'", d.Message, StringComparison.Ordinal);
        Assert.True(r.SeraphNumbered); // the AI prompt says so for the sheet it includes (the import window's trial import)

        // Every tab counts, whichever are imported (a re-import of one tab reads the sheet the same way); the info is for an
        // import that has a plain "Seraph".
        var prime = new PlanImporter().Import(document, new ImportOptions { Fight = PlanImports.Tea, Actions = RepoFiles.Actions, Aliases = RepoFiles.Aliases, Tabs = ["Prime"] });
        Assert.Equal(["Succor", "Sacred Soil", "Summon Seraph", "Consolation"], prime.Plan.Entries.Single(e => e.Mechanic == "p3.j_wave_5").Names());
        Assert.Single(prime.Diagnostics, d => d.Code == "seraph-reading");
        Assert.True(prime.SeraphNumbered);
        var sage = Import(document, fight: PlanImports.Tea, assignees: ["Sage"]);
        Assert.DoesNotContain(sage.Diagnostics, d => d.Code == "seraph-reading");
        Assert.True(sage.SeraphNumbered);

        // Without numbered Seraph calls, a plain "Seraph" is the summon alone (Carry the Clear writes its Consolations apart).
        var plain = Import(Sheet("P3", "Scholar", "Jwaves (4)\tSuccor/Soil/Seraph"), SlashReading.All, PlanImports.Tea);
        Assert.Equal(["Succor", "Sacred Soil", "Summon Seraph"], Assert.Single(plain.Plan.Entries).Names());
        Assert.DoesNotContain(plain.Diagnostics, d => d.Code == "seraph-reading");
        Assert.False(plain.SeraphNumbered);
    }

    [Fact]
    public void AnInferredTarget_IsForTheSingleTargetActionsOnly()
    {
        // 'LL/BJCC'!F54 "EukDiag/Kerachole" on Double Rocket Punch, a tankbuster: the tank is Eukrasian Diagnosis's target;
        // Kerachole (an area) gets an entry without one, as "Soil/Excog (Tank)" splits by its written target.
        var r = Import(Sheet("P2", "Sage\tScholar", "Double Rocket Punch\tEukDiag/Kerachole\tSoil/Excog (Tank)"), SlashReading.All, PlanImports.Tea);

        var sage = r.Plan.Entries.Where(e => e.Who.Job == Job.SGE).ToList();
        Assert.Equal([("Eukrasian Diagnosis", "tank"), ("Kerachole", null)], sage.Select(e => (string.Join("+", e.Names()), e.Target)).OrderBy(x => x.Item1));
        var sch = r.Plan.Entries.Where(e => e.Who.Job == Job.SCH).ToList();
        Assert.Equal([("Excogitation", "Tank"), ("Sacred Soil", null)], sch.Select(e => (string.Join("+", e.Names()), e.Target)).OrderBy(x => x.Item1));

        // A sequence stays one entry with the target ("ZoeEukProg" is Zoe → Eukrasian Prognosis).
        var seq = Assert.Single(Import(Sheet("P2", "Sage", "Double Rocket Punch\tEukDiag/ZoeEukProg"), SlashReading.All, PlanImports.Tea).Plan.Entries);
        Assert.True(seq.Seq);
        Assert.Equal("tank", seq.Target);
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

    [Theory]
    [InlineData("White Mage", "Asylum (melee)", "Asylum", "melee", null)] // where to place it
    [InlineData("Sage", "Kera (party)", "Kerachole", "party", null)]
    [InlineData("White Mage", "Liturgy (ranged)", "Liturgy of the Bell", "ranged", null)]
    [InlineData("Scholar", "Soil (on melee)", "Sacred Soil", "on melee", null)]
    [InlineData("MT", "Rampart (self)", "Rampart", "self", null)]
    [InlineData("White Mage", "Asylum (melee) + Benison", "Asylum+Divine Benison", "Asylum melee", null)] // a token's own
    [InlineData("White Mage", "Benison (Range)", "Divine Benison", null, "Range")] // used on a player: the target
    [InlineData("White Mage", "Asylum + Benison (MT)", "Divine Benison", null, "MT")]
    public void ATargetInParentheses_ThatNoActionIsUsedOn_StaysInTheNote(string column, string cell, string actions, string? note, string? target)
    {
        var r = Import(Sheet("P1", column, "Pantokrator\t" + cell));

        var e = Assert.Single(r.Plan.Entries, e => e.Target == target);
        Assert.Equal((actions, note), (string.Join("+", e.Names()), e.Note));
        Assert.All(r.Plan.Entries, x => Assert.False(target is not null && x.Note?.Contains(target, StringComparison.Ordinal) == true));
    }

    [Fact]
    public void ATargetInParentheses_OnANote_StaysInIt()
    {
        // Another job's action keeps its words and parentheses; in a column for anyone, the words are the note item and the
        // parentheses the entry's note, as for an action.
        var r = Import(Sheet("P1", "White Mage\tExtras", "Pantokrator\tEarthly Star (melee)\tFeint (melee)"));

        Assert.Equal(["note:Earthly Star (melee)"], Assert.Single(r.Plan.Entries, e => e.Who.Job == Job.WHM).Names());
        var feint = Assert.Single(r.Plan.Entries, e => e.Who.Job is null);
        Assert.Equal(["note:Feint"], feint.Names());
        Assert.Equal("melee", feint.Note);
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

    [Theory]
    [InlineData("White Mage", "(Temperance)", "Temperance", "Temperance", SlashReading.Auto)] // in brackets: optional
    [InlineData("Scholar", "Seraph (if needed)", "Seraph", "Summon Seraph", SlashReading.Auto)] // a condition
    [InlineData("Scholar", "Seraph1/Soil", "Seraph", "Summon Seraph+Consolation", SlashReading.Either)] // one choice of an either/or (beside "Seraph1", "Seraph" is "Seraph1")
    public void PartyBuffNamedAgain_AfterAPressThatMayNotHappen_StaysAPress(string column, string first, string again, string actions, SlashReading slashes)
    {
        // A player who skips the earlier press has no buff up: the later call is the one that puts it up (a Consolation
        // needs a Seraph out).
        var r = Import(Sheet("P2", column, "Limitless\t" + first, "Cosmo Memory\t" + again), slashes);

        Assert.Equal(actions.Split('+'), r.Plan.Entries.Single(e => e.Mechanic == "p2.cosmo_memory").Names());
        Assert.All(r.Plan.Entries.Where(e => e.Mechanic == "p2.limitless"), e => Assert.True(e.Optional));
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
    public void PartyBuffNamedAgain_EndingBeforeTheLaterHit_IsPressedLaterToCoverIt()
    {
        // TEA 'LL/BJCC'!G44/G46: Temperance at Hidden Mines (292.7) and at Water 2 (306.7). At its automatic -6 s, used on
        // the NOW cue (1.5 s before the press), the 20 s effect ends 1.5 s before Water 2. Pressed 3.5 s before the mines
        // it lasts 1 s past Water 2 (the use window's expiry margin), and still comes before the mines.
        var tea = PlanImports.Tea;
        var r = Import(Sheet("P2", "White Mage", "Mines\tTemperance", "Water 2\tTemperance"), fight: tea);

        var mines = r.Plan.Entries.Single(e => e.Mechanic == "p2.hidden_mines");
        Assert.Equal(-3.5, mines.Offset);
        var timing = TimingRules.ForEntry(mines, tea.GetMechanic(mines.Mechanic)!, tea, RepoFiles.Actions);
        Assert.Equal(306.7 + 1, timing.DueAt(new ReminderOptions()) + 20, 3);
        Assert.Equal(["note:Temperance (still active)"], r.Plan.Entries.Single(e => e.Mechanic == "p2.water_thunder_2").Names());
        Assert.Contains("pressed 3.5 s before its hit instead of 6.0 s", Assert.Single(r.Diagnostics, d => d.Code == "still-active").Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PartyBuffNamedAgain_TwiceMore_TheFirstPressCoversThemAll()
    {
        // TEA P1: Hand of Prayer/Parting (35.8), Hand of Pain (42.8), Doll Feeds (50.3). The first Temperance covers the
        // second call as it is, and pressed 3 s before its hit instead of 6 s also the third.
        var r = Import(Sheet("P1", "White Mage", "Hand of Prayer\tTemperance", "Hand of Pain\tTemperance", "Doll Feeds\tTemperance"), fight: PlanImports.Tea);

        Assert.Equal(-3.0, r.Plan.Entries.Single(e => e.Mechanic == "p1.hand_1").Offset);
        Assert.Equal(
            [("p1.hand_of_pain_1", "note:Temperance (still active)"), ("p1.doll_feeds", "note:Temperance (still active)")],
            r.Plan.Entries.Where(e => e.Mechanic != "p1.hand_1").Select(e => (e.Mechanic, e.Names().Single())));
    }

    [Theory]
    [InlineData("P1\tWhite Mage\nDoll Feeds\tTemperance\nHand of Pain\tTemperance", "even pressed just before its own")] // 19.7 s apart
    [InlineData("Time\tMechanic\tWhite Mage\n\tP2\t\n1:00\tMines\tTemperance (0:54)\n1:14\tWater 2\tTemperance", "at the time the sheet gives it")] // -6 s, written
    public void PartyBuffNamedAgain_ThatCannotLastThroughTheLaterHit_StaysAPress(string tsv, string why)
    {
        // The sheet asks for a second press within the recast: both stay presses, and `check` reports the recast.
        var r = Import(tsv, fight: PlanImports.Tea);

        Assert.Equal(2, r.Plan.Entries.Count);
        Assert.All(r.Plan.Entries, e => Assert.Equal(["Temperance"], e.Names()));
        Assert.Contains("both stay presses", Assert.Single(r.Diagnostics, d => d.Code == "still-active").Message, StringComparison.Ordinal);
        Assert.Contains(why, Assert.Single(r.Diagnostics, d => d.Code == "still-active").Message, StringComparison.Ordinal);
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

    [Fact]
    public void LimitBreakInTwoColumns_OneColumnImported_StaysOptional()
    {
        // A tank who imports only their own column reads the sheet as the whole import does.
        var r = Import(Sheet("P5", "Tank 1\tTank 2\tH1", "Run: Dynamis\tLB3\tLB3\tLB3"), assignees: ["Tank 1"]);

        Assert.Equal(4, r.Plan.Entries.Count);
        Assert.All(r.Plan.Entries, e => Assert.Equal(("MT", true, "or Tank 2"), (e.Who.Slot, e.Optional, e.Note)));
        Assert.Equal(DiagnosticSeverity.Info, Assert.Single(r.Diagnostics, d => d.Code == "shared-limit-break").Severity);
    }

    [Fact]
    public void LimitBreakInOneColumn_OtherColumnsNotImported_StaysRequired()
    {
        // Tank 2 writes Rampart, and H1's LB3 is the healers' limit break.
        var r = Import(Sheet("P5", "Tank 1\tTank 2\tH1", "Run: Dynamis\tLB3\tRampart\tLB3"), assignees: ["Tank 1"]);

        Assert.Equal(4, r.Plan.Entries.Count);
        Assert.All(r.Plan.Entries, e => Assert.Equal((false, (string?)null), (e.Optional, e.Note)));
        Assert.DoesNotContain(r.Diagnostics, d => d.Code == "shared-limit-break");
    }

    [Theory]
    [InlineData("Tank 1", "'Alex Prime/PA'!C27")]
    [InlineData("Tank 2", "'Alex Prime/PA'!D27")]
    public void LimitBreakInTwoColumns_TeaOneSeatImport_ReadsAsTheWholeSheet(string column, string cell)
    {
        // LPDU TEA's transition raidwide: "LB3" in both tank columns, imported one seat at a time.
        var seat = Import(Fixtures.Doc("lpdu-tea"), fight: PlanImports.Tea, assignees: [column]).Plan.At(cell);

        Assert.Equal(4, seat.Count);
        Assert.Equal(
            PlanImports.LpduTea.Plan.At(cell).Select(e => (e.Who.Job, e.Names().Single(), e.Optional, e.Note)),
            seat.Select(e => (e.Who.Job, e.Names().Single(), e.Optional, e.Note)));
        Assert.All(seat, e => Assert.True(e.Optional));
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

    [Theory]
    [InlineData("Scholar", "Succor (Late)", -3.0)] // a 2 s cast
    [InlineData("White Mage", "Medica II (Late)", -3.0)]
    [InlineData("Astrologian", "Aspected Helios (Late)", -2.5)] // a 1.5 s cast
    [InlineData("Scholar", "Indom + Succor (Late)", -3.0)] // pressed before the hit for the Succor
    public void Late_ACastEndsJustBeforeTheHit(string column, string cell, double offset)
    {
        // Pressed 1 s before the hit, a 2 s Succor would land after it: the cast ends 1 s before the hit instead.
        var entry = Assert.Single(Import(Sheet("P3", column, "Mega Holy\t" + cell), fight: PlanImports.Tea).Plan.Entries);

        Assert.Equal(offset, entry.Offset);
        var cast = entry.Do.SelectMany(i => i.ActionIds()).Max(id => RepoFiles.Actions.Get(id)!.CastTime);
        Assert.Equal(PlanImporter.LateOffset, entry.Offset!.Value + cast, 3);
    }

    [Fact]
    public void Late_NeverPressesEarlierThanWithoutIt()
    {
        // TOP Cosmo Meteor (hits 1335.6 … 1343.7): a 10 s Reprisal is already pressed 0.9 s before the first hit, so that it
        // lasts through the spreads at 1342.7; "(Late)" (-1 s) would be earlier.
        var entry = Assert.Single(Import(Sheet("P6", "MT", "Cosmo Meteor\tReprisal (Late)")).Plan.Entries);

        Assert.Null(entry.Offset);
        Assert.Equal(-0.9, TimingRules.ForEntry(entry, PlanImports.Top.GetMechanic(entry.Mechanic)!, PlanImports.Top, RepoFiles.Actions).Offset, 3);
    }

    [Fact]
    public void Late_OnAMechanicWhoseCallsArePressedAfterIt_ChangesNothing()
    {
        // TEA's Photon sets everyone to 1 HP: its calls are the heal-up after it (press: after), "(Late)" or not.
        var entry = Assert.Single(Import(Sheet("P2", "White Mage", "Photon\tTemperance (Late)"), fight: PlanImports.Tea).Plan.Entries);

        Assert.Equal(("p2.photon_1", (double?)null), (entry.Mechanic, entry.Offset));
    }

    [Fact]
    public void OtherTimingWords_StayNotes()
    {
        var entry = Assert.Single(Import(Sheet("P1", "MT", "Pantokrator\tReprisal (End of cast)")).Plan.Entries);
        Assert.Equal(((double?)null, "End of cast"), (entry.Offset, entry.Note));
    }
}
