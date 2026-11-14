using System.Globalization;
using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Core.Tests.Data;

namespace MitCue.Core.Tests.Import;

/// <summary>
/// Layout rules found on LPDU's TEA sheet ("[LPDU] TEA Heal/Mit"), on small sheets for the TOP fight: phase titles over the
/// player columns, role and "Extras" columns, cells that point up, cells in brackets, remarks beside the label and notes
/// merged across columns that name actions.
/// </summary>
public class ImportLayoutRulesTests
{
    private static SheetTab Tab(string name, IReadOnlyList<CellRange> merges, params string[][] rows) => new()
    {
        Name = name,
        Rows = rows.Select(r => (IReadOnlyList<SheetCell>)r.Select(t => t.Length == 0 ? SheetCell.Empty : new SheetCell { Text = t }).ToList()).ToList(),
        Merges = merges,
    };

    private static string Kinds(TabLayout t) => new(t.Rows.Select(r => r.Kind switch
    {
        RowKind.Header => 'H',
        RowKind.Data => 'D',
        RowKind.PhaseLabel => 'L',
        RowKind.Section => 'S',
        RowKind.Note => 'N',
        RowKind.Footnote => 'F',
        RowKind.Empty => 'E',
        RowKind.Title => 'T',
        _ => '?',
    }).ToArray());

    private static string Phases(TabLayout t) => string.Concat(t.Rows.Select(r => r.Phase is { } p ? p.Number.ToString(CultureInfo.InvariantCulture) : "-"));

    private static string Roles(TabLayout t) => new(t.Columns.Select(c => c.Role switch
    {
        ColumnRole.Mechanic => 'M',
        ColumnRole.Assignee => 'A',
        ColumnRole.Notes => 'N',
        ColumnRole.Time => 'T',
        ColumnRole.Unknown => 'U',
        ColumnRole.Empty => 'E',
        _ => '?',
    }).ToArray());

    private static ImportResult Import(SheetTab tab, IReadOnlyList<string>? columns = null) => new PlanImporter().Import(
        new SheetDocument { Title = "Test sheet", Tabs = [tab] },
        new ImportOptions { Fight = PlanImports.Top, Actions = RepoFiles.Actions, Aliases = RepoFiles.Aliases, ImportedDate = PlanImports.Date, Assignees = columns });

    // ------------------------------------------------------------------ phase titles over the player columns

    [Fact]
    public void PhaseTitleMergedOverThePlayerColumns_StartsThePhase()
    {
        // TEA 'LL/BJCC'!D28 "P2: Brute Justice / Cruise Chaser", merged over the player columns above a repeated header.
        var layout = new LayoutDetector().Detect(Tab(
            "Mit",
            [new CellRange(0, 1, 0, 3), new CellRange(4, 1, 4, 3)],
            ["", "P1: Omega", "", ""],
            ["", "MT", "OT", "H1"],
            ["Program Loop", "Rep", "", "Kera"],
            ["", "", "", ""],
            ["", "P2: Omega-M/F", "", ""],
            ["", "MT", "OT", "H1"],
            ["Solar Ray", "", "Rep", "Soil"]));

        Assert.Equal("THDESHD", Kinds(layout));
        Assert.Equal("-111222", Phases(layout));
        Assert.Equal([(1, PhaseSource.SectionRow, "'Mit'!B1"), (2, PhaseSource.SectionRow, "'Mit'!B5")], layout.Sections.Select(s => (s.Number, s.Source, s.Src)));
        Assert.Equal("Omega-M/F", layout.Sections[1].Name);
        Assert.DoesNotContain(layout.Diagnostics, d => d.Code is "note-in-table" or "missing-mechanic-label");
    }

    [Fact]
    public void PhaseTitleInAPlayerColumnAboveARepeatedHeader_StartsThePhase()
    {
        var layout = new LayoutDetector().Detect(Tab(
            "P1/P2",
            [],
            ["", "MT", "OT"],
            ["Program Loop", "Rep", ""],
            ["", "P2 — Omega-M/F", ""],
            ["", "MT", "OT"],
            ["Solar Ray", "", "Rep"]));

        Assert.Equal("HDSHD", Kinds(layout));
        Assert.Equal("11222", Phases(layout));
    }

    [Fact]
    public void PhaseLikeRemarkInAPlayerColumn_IsNoTitle()
    {
        // Neither a banner nor above a header: a remark that starts with a phase stays a note.
        var layout = new LayoutDetector().Detect(Tab(
            "P1/P2",
            [],
            ["P1", "MT", "OT"],
            ["Program Loop", "Rep", ""],
            ["", "P2: Rampart as the clone jumps", ""],
            ["P2", "", ""],
            ["Solar Ray", "", "Rep"]));

        Assert.Equal("HDNLD", Kinds(layout));
        Assert.Equal("11122", Phases(layout));
    }

    [Fact]
    public void EarlierPhaseTitleOverThePlayerColumns_IsNoNewSection()
    {
        var layout = new LayoutDetector().Detect(Tab(
            "P2",
            [new CellRange(2, 1, 2, 2)],
            ["P2", "MT", "OT"],
            ["Solar Ray", "Rep", ""],
            ["", "P1: Omega", ""],
            ["Limitless Synergy", "", "Rep"]));

        Assert.Equal("2222", Phases(layout));
        Assert.Single(layout.Sections);
    }

    [Fact]
    public void PhaseTitles_GiveNoEntries()
    {
        var r = Import(Tab(
            "Mit",
            [new CellRange(3, 1, 3, 2)],
            ["P1", "MT", "OT"],
            ["Program Loop", "Rep", ""],
            ["", "", ""],
            ["", "P2: Omega-M/F", ""],
            ["", "MT", "OT"],
            ["Solar Ray", "", "Rep"]));

        Assert.Equal([("'Mit'!B2", "p1.program_loop"), ("'Mit'!C6", "p2.solar_ray")], r.Plan.Entries.Select(e => (e.Src!, e.Mechanic)).Order());
    }

    // ------------------------------------------------------------------ role and "Extras" columns

    [Theory]
    [InlineData("Phys Range", AssigneeKind.Custom, Role.PhysicalRanged)]
    [InlineData("Physical Range", AssigneeKind.Custom, Role.PhysicalRanged)]
    [InlineData("Phys Ranged", AssigneeKind.Custom, Role.PhysicalRanged)]
    [InlineData("Caster", AssigneeKind.Custom, Role.Caster)]
    [InlineData("Extras", AssigneeKind.Anyone, Role.None)]
    [InlineData("Other", AssigneeKind.Anyone, Role.None)]
    [InlineData("Misc.", AssigneeKind.Anyone, Role.None)]
    [InlineData("Utility", AssigneeKind.Anyone, Role.None)]
    public void RoleAndAnyoneHeaders(string header, AssigneeKind kind, Role role)
    {
        Assert.True(AssigneeHeader.TryParse(header, out var a));
        Assert.Equal((kind, role, (string?)null, (Job?)null), (a.Kind, a.Role, a.Slot, a.Job));
    }

    [Theory]
    [InlineData("Time Range")]
    [InlineData("Range")]
    [InlineData("Other notes")]
    [InlineData("Extras for P2")]
    public void NotPlayerHeaders(string header) => Assert.False(AssigneeHeader.TryParse(header, out _));

    [Fact]
    public void PhysRangeColumn_IsARoleColumnForThePhysicalRangedJobs()
    {
        var r = Import(Tab("P1", [], ["P1", "Phys Range", "Caster"], ["Program Loop", "Party Mit", "Addle"]));

        Assert.Equal(["Phys Range", "Caster"], r.Plan.Assignees.Select(a => a.Key));
        Assert.All(r.Plan.Assignees, a => Assert.Null(a.Slot));
        Assert.Equal(
            [(Job.BRD, "Troubadour"), (Job.MCH, "Tactician"), (Job.DNC, "Shield Samba")],
            r.Plan.Entries.Where(e => e.Who.Assignee == "Phys Range").OrderBy(e => e.Who.Job).Select(e => (e.Who.Job!.Value, e.Names().Single())));
        Assert.Equal([Job.BLM, Job.SMN, Job.RDM, Job.PCT], r.Plan.Entries.Where(e => e.Who.Assignee == "Caster").Select(e => e.Who.Job!.Value).Order());
    }

    [Fact]
    public void ExtrasColumn_CallsAreForTheJobsWhoseActionsTheyName_WithoutAColumn()
    {
        var r = Import(Tab(
            "P1",
            [],
            ["P1", "MT", "Extras"],
            ["Loop 1", "Rep", "PLD Wings"],
            ["Loop 2", "", "Use Personals"],
            ["Pantokrator", "", "(Minne/Mantra)"]));

        Assert.Equal(["MT"], r.Plan.Assignees.Select(a => a.Key));
        Assert.Equal([new AnyoneColumn("P1", 2, "Extras")], r.AnyoneColumns);
        var wings = Assert.Single(r.Plan.At("'P1'!C2"));
        Assert.Equal((Job.PLD, (string?)null, (string?)null, "Passage of Arms"), (wings.Who.Job!.Value, wings.Who.Slot, wings.Who.Assignee, wings.Names()[0]));

        // A note-only call is one entry for everyone.
        var personals = Assert.Single(r.Plan.At("'P1'!C3"));
        Assert.Equal(((Job?)null, (string?)null, (string?)null), (personals.Who.Job, personals.Who.Slot, personals.Who.Assignee));
        Assert.Equal(["note:Use Personals"], personals.Names());

        // Each job's own action, written as alternatives: an entry per job, optional (the whole cell is in brackets).
        var minne = r.Plan.At("'P1'!C4").OrderBy(e => e.Who.Job).ToList();
        Assert.Equal([(Job.MNK, "Mantra"), (Job.BRD, "Nature's Minne")], minne.Select(e => (e.Who.Job!.Value, e.Names()[0])));
        Assert.All(minne, e => Assert.True(e.Optional));
        Assert.DoesNotContain(r.Diagnostics, d => d.Severity != DiagnosticSeverity.Info);

        // A Paladin who follows the MT column gets the Paladin's call of the Extras column; a Warrior does not.
        Assert.Contains(wings, PlanSelection.For(r.Plan, new PlayerContext(Job.PLD, 90, "MT", "MT")));
        Assert.DoesNotContain(wings, PlanSelection.For(r.Plan, new PlayerContext(Job.WAR, 90, "MT", "MT")));
    }

    [Theory]
    [InlineData("Feint", "MNK/DRG/NIN/SAM/RPR/VPR")]
    [InlineData("Party Mit", "PLD/WAR/BRD/MCH/DRK/RDM/GNB/DNC/PCT")]
    public void ExtrasColumn_ActionSeveralJobsShare_IsANoteForEveryone(string cell, string jobs)
    {
        var r = Import(Tab("P1", [], ["P1", "MT", "Extras"], ["Program Loop", "", cell], ["Pantokrator", "", "DRK Reprisal"]));

        var shared = Assert.Single(r.Plan.At("'P1'!C2"));
        Assert.Equal(("note:" + cell, (Job?)null), (shared.Names().Single(), shared.Who.Job));
        var warning = Assert.Single(r.Diagnostics, d => d.Code == "anyone-role-action");
        Assert.Equal((DiagnosticSeverity.Warning, "'P1'!C2"), (warning.Severity, warning.Src));
        Assert.Contains(jobs, warning.Message, StringComparison.Ordinal);

        // A job prefix says whose it is.
        var reprisal = Assert.Single(r.Plan.At("'P1'!C3"));
        Assert.Equal((Job.DRK, "Reprisal"), (reprisal.Who.Job!.Value, reprisal.Names().Single()));
    }

    [Theory]
    [InlineData("PLD Wings + Addle", Job.PLD, "Passage of Arms", "Addle")]
    [InlineData("Dismantle + Feint", Job.MCH, "Dismantle", "Feint")]
    [InlineData("Feint + Dismantle", Job.MCH, "Dismantle", "Feint")]
    [InlineData("Feint + Addle + Dismantle", Job.MCH, "Dismantle", "Feint + Addle")]
    public void ExtrasColumn_SharedActionBesideAJobsOwnCall_OnlyTheSharedOneIsANote(string cell, Job job, string action, string shared)
    {
        // The call that names its job keeps its entry; only the actions several jobs share are a note for everyone.
        var r = Import(Tab("P1", [], ["P1", "MT", "Extras"], ["Program Loop", "", cell]));

        var entries = r.Plan.At("'P1'!C2");
        var own = Assert.Single(entries, e => e.Who.Job is not null);
        Assert.Equal((job, action), (own.Who.Job!.Value, own.Names().Single()));
        var note = Assert.Single(entries, e => e.Who.Job is null);
        Assert.Equal("note:" + shared, note.Names().Single());
        Assert.Equal(shared.Split(" + ").Select(s => "'" + s + "'"), r.Diagnostics.Where(d => d.Code == "anyone-role-action").Select(d => d.Message.Split(" in ")[0]));
    }

    [Fact]
    public void ExtrasColumn_SequenceWithASharedStep_StaysOneNote()
    {
        // The steps of a sequence are one player's: one that does not say whose makes the whole sequence a note.
        var note = Assert.Single(Import(Tab("P1", [], ["P1", "MT", "Extras"], ["Program Loop", "", "PLD Wings → Addle"])).Plan.At("'P1'!C2"));
        Assert.Equal(((Job?)null, "note:PLD Wings → Addle"), (note.Who.Job, note.Names().Single()));
    }

    [Fact]
    public void ExtrasColumn_ActionsNamedPerJobInParentheses_SayWhoseTheyAre()
    {
        var r = Import(Tab("P1", [], ["P1", "MT", "Extras"], ["Program Loop", "", "Party Mit (DM/HoL)"]));

        Assert.Equal([(Job.DRK, "Dark Missionary"), (Job.GNB, "Heart of Light")], r.Plan.Entries.OrderBy(e => e.Who.Job).Select(e => (e.Who.Job!.Value, e.Names().Single())));
        Assert.DoesNotContain(r.Diagnostics, d => d.Code == "anyone-role-action");
    }

    [Fact]
    public void ExtrasColumn_IsImportedWhicheverColumnsAreChosen()
    {
        var r = Import(Tab("P1", [], ["P1", "MT", "OT", "Extras"], ["Program Loop", "Rep", "Rampart", "PLD Wings"]), ["MT"]);

        Assert.Equal(["'P1'!B2", "'P1'!D2"], r.Plan.Entries.Select(e => e.Src!).Order());
        Assert.Equal(["MT"], r.Plan.Assignees.Select(a => a.Key));
    }

    [Fact]
    public void ExtrasColumn_NoteBelowTheTable_IsOneEntryForEveryone()
    {
        var r = Import(Tab("P1", [], ["P1", "MT", "Extras"], ["Program Loop", "Rep", ""], ["", "", "Everyone uses their personals for the towers."]));

        var note = Assert.Single(r.Plan.At("'P1'!C3"));
        Assert.Equal(((Job?)null, (string?)null), (note.Who.Job, note.Who.Assignee));
    }

    // ------------------------------------------------------------------ cells that point up

    [Fact]
    public void CaretCell_IsARemarkAboutTheCellAboveIt()
    {
        var r = Import(Tab(
            "P1",
            [],
            ["P1", "Caster", "Phys Range"],
            ["Program Loop", "Addle", "(Party Mit)"],
            ["Pantokrator", "(^on Omega)", "(^ if MCH)"]));

        var addle = r.Plan.At("'P1'!B2");
        Assert.Equal(4, addle.Count);
        Assert.All(addle, e => Assert.Equal(("p1.program_loop", "on Omega"), (e.Mechanic, e.Note)));
        var tactician = Assert.Single(r.Plan.At("'P1'!C2"));
        Assert.Equal((Job.MCH, "Tactician", true), (tactician.Who.Job!.Value, tactician.Names().Single(), tactician.Optional));
        Assert.DoesNotContain(r.Plan.Entries, e => e.Src is "'P1'!B3" or "'P1'!C3");
        Assert.Equal(["'P1'!B3", "'P1'!C3"], r.Diagnostics.Where(d => d.Code == "caret-note" && d.Severity == DiagnosticSeverity.Info).Select(d => d.Src!));
    }

    [Theory]
    [InlineData("^ on Omega")]
    [InlineData("(^ on Omega)")]
    public void CaretCell_IsARemarkAboutEveryItemOfTheCellAboveIt(string caret)
    {
        // "Soil / Expedient" is two items: the remark speaks for both, not only for the last ("(Addle)" is one call in
        // brackets: the remark goes inside them).
        var r = Import(Tab("P3", [], ["P3", "SCH", "Caster"], ["Hello World", "Soil / Expedient", "(Addle)"], ["Latent 1", caret, "(^ if RDM)"]));

        var sch = r.Plan.At("'P3'!B2");
        Assert.Equal(["Expedient", "Sacred Soil"], sch.Select(e => e.Names().Single()).Order());
        Assert.All(sch, e => Assert.Equal(("p3.hello_world", "on Omega"), (e.Mechanic, e.Note)));
        var addle = Assert.Single(r.Plan.At("'P3'!C2"));
        Assert.Equal((Job.RDM, "Addle", true), (addle.Who.Job!.Value, addle.Names().Single(), addle.Optional));
        Assert.Contains(r.Diagnostics, d => d.Code == "caret-note" && d.Message.Contains("read as 'Soil (on Omega) / Expedient (on Omega)'", StringComparison.Ordinal));
        Assert.Contains(r.Diagnostics, d => d.Code == "caret-note" && d.Message.Contains("read as '(Addle (if RDM))'", StringComparison.Ordinal));
    }

    [Fact]
    public void CaretCell_WithATimingWord_TimesEveryItemOfTheCellAbove()
    {
        // "(late)" presses each of the cell's calls just before the hit, not only the last one.
        var r = Import(Tab("P3", [], ["P3", "SCH"], ["Hello World", "Soil / Expedient"], ["Latent 1", "^ late"]));

        Assert.Equal([PlanImporter.LateOffset, PlanImporter.LateOffset], r.Plan.At("'P3'!B2").Select(e => e.Offset));
    }

    [Theory]
    [InlineData("(^on Omega)", "its column has no cell above it")]
    [InlineData("^", "says nothing about it")]
    public void CaretCell_WithNothingToSay_OrNothingAbove_IsReported(string caret, string why)
    {
        var r = Import(Tab("P1", [], ["P1", "Caster", "MT"], ["Program Loop", caret, "Rep"], ["Pantokrator", caret == "^" ? "^" : string.Empty, ""]));

        Assert.DoesNotContain(r.Plan.Entries, e => e.Src!.StartsWith("'P1'!B", StringComparison.Ordinal));
        var d = Assert.Single(r.Diagnostics, x => x.Code == "caret-note" && x.Src == "'P1'!B2");
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Contains(why, d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CaretCell_DoesNotReachAcrossAHeaderWithOtherPlayers_OrAnotherPhase()
    {
        var r = Import(Tab(
            "Mit",
            [],
            ["P1", "Caster", "MT"],
            ["Program Loop", "Addle", "Rep"],
            ["P2", "", ""],
            ["Solar Ray", "(^ on Omega)", ""],
            ["", "Phys Range", "MT"],
            ["Limitless Synergy", "(^ if MCH)", "Rep"]));

        Assert.Equal(2, r.Diagnostics.Count(d => d.Code == "caret-note" && d.Severity == DiagnosticSeverity.Warning));
        Assert.All(r.Plan.At("'Mit'!B2"), e => Assert.Null(e.Note));
    }

    [Theory]
    [InlineData("(Shake)", "Shake", true)]
    [InlineData("[Shake]", "Shake", true)]
    [InlineData("(Minne/Mantra)", "Minne/Mantra", true)]
    [InlineData("Shake", "Shake", false)]
    public void CellInBrackets_NamesOptionalActions(string text, string head, bool optional)
    {
        var cell = CellParser.Default.Parse(text);
        var token = Assert.Single(cell.Tokens);
        Assert.Equal((head, optional, optional), (token.Head, token.Optional, cell.Bracketed));
        Assert.Equal(text, cell.Text);
    }

    [Theory]
    [InlineData("(1:18)")] // a time stays a time
    [InlineData("(MT)")] // a target stays a target
    [InlineData("(Shake) (Veil)")] // two pairs of brackets
    public void CellInBrackets_NotAroundWords_IsWhatItWas(string text)
    {
        var cell = CellParser.Default.Parse(text);
        Assert.False(cell.Bracketed);
        Assert.DoesNotContain(cell.Tokens, t => t.Optional);
    }

    [Fact]
    public void CellInBrackets_IsAnOptionalCallForTheJobItNames()
    {
        var r = Import(Tab("P1", [], ["P1", "Tank 1", "Tank 2"], ["Program Loop", "(Shake)", "(Veil)"]));

        var shake = Assert.Single(r.Plan.At("'P1'!B2"));
        Assert.Equal(("MT", Job.WAR, "Shake It Off", true), (shake.Who.Slot, shake.Who.Job!.Value, shake.Names().Single(), shake.Optional));
        var veil = Assert.Single(r.Plan.At("'P1'!C2"));
        Assert.Equal(("OT", Job.PLD, "Divine Veil", true), (veil.Who.Slot, veil.Who.Job!.Value, veil.Names().Single(), veil.Optional));
    }

    // ------------------------------------------------------------------ notes

    [Fact]
    public void UnlabelledRemarksBesideTheLabel_AreTheRowsNotes()
    {
        var layout = new LayoutDetector().Detect(Tab(
            "P1",
            [],
            ["", "P1", "MT", "OT"],
            ["the one after MT invuln", "Program Loop", "", "Rep"],
            ["", "Pantokrator", "Rep", ""]));

        Assert.Equal("NMAA", Roles(layout));
        Assert.Equal("the one after MT invuln", layout.Rows[1].Notes);
        Assert.Contains(layout.Diagnostics, d => d.Code == "unlabelled-column" && d.Message.Contains("rows' notes", StringComparison.Ordinal));

        // Short words beside the label (a boss name) are not remarks.
        Assert.Equal("UMAA", Roles(new LayoutDetector().Detect(Tab("P1", [], ["", "P1", "MT", "OT"], ["LL", "Program Loop", "", "Rep"], ["Hand", "Pantokrator", "Rep", ""]))));
    }

    [Fact]
    public void UnlabelledRemarksBesideTheLabel_AreTheDetailOfTheRowsEntries()
    {
        var r = Import(Tab("P1", [], ["", "P1", "MT", "OT"], ["the one after MT invuln", "Program Loop", "", "Rep"]));

        Assert.Equal("the one after MT invuln", Assert.Single(r.Plan.Entries).Detail);
    }

    [Fact]
    public void NoteMergedAcrossColumns_GoesToTheEntriesThatPressTheActionsItNames()
    {
        const string Tip = "Press Soil as the first tether resolves and cover the stacks!";
        var r = Import(Tab(
            "P1",
            [new CellRange(3, 1, 3, 3)],
            ["P1", "MT", "H1 WHM", "H2 SCH"],
            ["Program Loop", "Rep", "Temperance", "Soil"],
            ["Pantokrator", "Rep", "Asylum", "Succor"],
            ["", Tip, "", ""]));

        // Sacred Soil only: not the tanks' entries ("cover" names Cover, which no entry presses).
        Assert.Equal(["'P1'!D2"], r.Plan.Entries.Where(e => e.Detail == Tip).Select(e => e.Src!));
        var d = Assert.Single(r.Diagnostics, x => x.Code == "note-row");
        Assert.EndsWith("that press Sacred Soil.", d.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2, "Tanks: Rampart the first tower set, then OT provokes and takes the boss after Pantokrator")]
    [InlineData(3, "Tanks: Rampart the first tower set, then OT provokes and takes the boss after Pantokrator")]
    [InlineData(3, "Temperance as the first debuff hits 14s, the Scholar covers the rest")]
    public void NoteMergedAcrossColumns_NamingPlayers_GoesToEveryEntryOfItsColumns(int lastColumn, string note)
    {
        // It speaks to players, not only about the actions it names: the OT needs "OT provokes" without a Rampart.
        var r = Import(Tab(
            "P1",
            [new CellRange(1, 1, 1, lastColumn)],
            ["P1", "MT", "OT", "H1 WHM"],
            ["", note, "", ""],
            ["Program Loop", "Rampart", "Reprisal", "Temperance"],
            ["Pantokrator", "Reprisal", "Vengeance", "Asylum"]));

        var columns = "BCD"[..lastColumn];
        Assert.Equal(
            r.Plan.Entries.Where(e => columns.Contains(e.Src!["'P1'!".Length])).Select(e => e.Src!).Order(),
            r.Plan.Entries.Where(e => e.Detail == note).Select(e => e.Src!).Order());
        Assert.Equal(lastColumn, r.Diagnostics.Count(d => d.Code == "note-row" && d.Message.Contains("kept as the detail of the '", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("Watch the clone positions before you move!!")] // names no action
    [InlineData("Discuss the LB3 order during the cutscene!!")] // names one that no entry of its columns presses
    public void NoteMergedAcrossColumns_ThatNoEntryPresses_GoesToEveryColumn(string note)
    {
        var r = Import(Tab(
            "P1",
            [new CellRange(2, 1, 2, 2)],
            ["P1", "MT", "H1 WHM"],
            ["Program Loop", "Rep", "Temperance"],
            ["", note, ""]));

        Assert.Equal(2, r.Plan.Entries.Count);
        Assert.All(r.Plan.Entries, e => Assert.Equal(note, e.Detail));
    }
}
