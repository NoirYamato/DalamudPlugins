using MitCue.Core.Fights;
using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Core.Tests.Data;

namespace MitCue.Core.Tests.Import;

/// <summary>The importer's rules (docs/DESIGN.md §6 steps 4–8) on small pasted sheets.</summary>
public class PlanImporterTests
{
    private static ImportResult Import(string tsv, string? calibration = null, FightDefinition? fight = null, string title = "Test sheet", IReadOnlyList<string>? assignees = null)
    {
        var tab = SheetReaders.FromDelimited(tsv.ReplaceLineEndings("\n"), '\t', "Pasted");
        var doc = new SheetDocument { Title = title, Tabs = [tab] };
        var options = new ImportOptions
        {
            Fight = fight ?? PlanImports.Top,
            Actions = RepoFiles.Actions,
            Aliases = RepoFiles.Aliases,
            CalibrationKey = calibration,
            Assignees = assignees,
            ImportedDate = PlanImports.Date,
        };
        return new PlanImporter().Import(doc, options);
    }

    /// <summary>A sheet with a mechanic column and the given player columns; rows are "label\tcell\tcell…".</summary>
    private static string Sheet(string phase, string columns, params string[] rows) =>
        $"{phase}\t{columns}\n" + string.Join("\n", rows);

    private static PlanEntry Only(ImportResult r) => Assert.Single(r.Plan.Entries);

    // ------------------------------------------------------------------ actions

    [Fact]
    public void Aliases_ExactNames_AndTypos_Resolve()
    {
        var r = Import(Sheet("P5", "PLD", "Solar Ray 1\tHS + Divine Veil + Bulwerk"));
        Assert.Equal(["Holy Sheltron", "Divine Veil", "Bulwark"], Only(r).Names());
        var fuzzy = Assert.Single(r.Diagnostics, d => d.Code == "fuzzy-match");
        Assert.Equal(DiagnosticSeverity.Info, fuzzy.Severity);
        Assert.Contains("'bulwerk'", fuzzy.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SameJobEitherOr_IsAnAnyOfItem()
    {
        var entry = Only(Import(Sheet("P6", "DRK", "WC2\tTBN/Oblation Co-Tank")));
        var any = Assert.Single(entry.Do);
        Assert.Equal(["The Blackest Night", "Oblation"], any.Any!.Select(i => i.Name));
        Assert.Equal("Co-Tank", entry.Target);
        Assert.Null(entry.Note);
    }

    [Fact]
    public void CrossJobAlternatives_InASlotColumn_SplitPerJob()
    {
        var r = Import(Sheet("P2", "H1\tH2", "Solar Ray\t\tSoil/Kera"));
        Assert.Equal(2, r.Plan.Entries.Count);
        Assert.Equal(["Sacred Soil"], Assert.Single(r.Plan.Entries, e => e.Who.Job == Job.SCH).Names());
        Assert.Equal(["Kerachole"], Assert.Single(r.Plan.Entries, e => e.Who.Job == Job.SGE).Names());
        Assert.All(r.Plan.Entries, e => Assert.Equal("H2", e.Who.Slot));
    }

    [Fact]
    public void RolePhrases_ExpandPerJob_RoleActionsStayWithTheSlot()
    {
        var r = Import(Sheet("P1", "MT\tR1", "Pantokrator\tParty Mit + Rep\tParty Mit", "1st Stack\tRep\t"));
        var mt = r.Plan.Entries.Where(e => e.Who.Slot == "MT" && e.Mechanic == "p1.pantokrator").ToList();
        Assert.Equal(["Divine Veil+Reprisal", "Shake It Off+Reprisal", "Dark Missionary+Reprisal", "Heart of Light+Reprisal"], mt.OrderBy(e => e.Who.Job).Select(e => string.Join("+", e.Names())));
        var r1 = r.Plan.Entries.Where(e => e.Who.Slot == "R1").ToList();
        Assert.Equal([Job.BRD, Job.MCH, Job.RDM, Job.DNC, Job.PCT], r1.Select(e => e.Who.Job!.Value).OrderBy(j => j));
        var rep = Assert.Single(r.Plan.Entries, e => e.Mechanic == "p1.panto_stack1");
        Assert.Equal(("MT", (Job?)null, "Reprisal"), (rep.Who.Slot, rep.Who.Job, rep.Names()[0]));
    }

    [Fact]
    public void TargetedForm_IsUsedWhenTheCellNamesAPlayer()
    {
        var r = Import(Sheet("P4", "WAR\tPLD", "Proteans\tShort CD to R1\tShort CD"));
        Assert.Equal(["Nascent Flash"], Assert.Single(r.Plan.Entries, e => e.Who.Job == Job.WAR).Names());
        Assert.Equal("R1", Assert.Single(r.Plan.Entries, e => e.Who.Job == Job.WAR).Target);
        Assert.Equal(["Holy Sheltron"], Assert.Single(r.Plan.Entries, e => e.Who.Job == Job.PLD).Names());
    }

    [Fact]
    public void Sequences_KeepOrder()
    {
        var entry = Only(Import(Sheet("P3", "SCH", "Latent 1\tRecit -> Adlo -> Deploy")));
        Assert.True(entry.Seq);
        Assert.Equal(["Recitation", "Adloquium", "Deployment Tactics"], entry.Names());
        var spreadlo = Only(Import(Sheet("P3", "SCH", "Latent 1\tSpreadlo")));
        Assert.True(spreadlo.Seq);
        Assert.Equal(["Adloquium", "Deployment Tactics"], spreadlo.Names());
    }

    [Fact]
    public void NoteOnly_FreeText_AndUnknownWords_BecomeNotes()
    {
        var r = Import(Sheet("P5", "SCH", "Delta\tEverything", "Sigma\tPress it whenever the boss jumps", "Omega\tXyzzy"));
        Assert.Equal(["note:Everything"], r.Plan.Entries[0].Names());
        var diags = r.Diagnostics.Where(d => d.Code == "unresolved-token").ToList();
        Assert.Equal(
            [DiagnosticSeverity.Info, DiagnosticSeverity.Info, DiagnosticSeverity.Warning],
            diags.OrderBy(d => d.Src, StringComparer.Ordinal).Select(d => d.Severity));
        Assert.Equal("'Pasted'!B4", Assert.Single(diags, d => d.Severity == DiagnosticSeverity.Warning).Src);
        Assert.Contains(r.Plan.Entries, e => e.Names().SequenceEqual(["note:Xyzzy"]));
    }

    [Fact]
    public void AnotherJobsAction_IsANoteWithADiagnostic()
    {
        var r = Import(Sheet("P5", "WAR", "Delta\tRep + PLD Wings"));
        Assert.Equal(["Reprisal", "note:PLD Wings"], Only(r).Names());
        Assert.Equal(DiagnosticSeverity.Info, Assert.Single(r.Diagnostics, d => d.Code == "other-job-action").Severity);
    }

    [Fact]
    public void ActionsAboveTheSyncLevel_AreReported()
    {
        var r = Import(Sheet("P5", "SCH", "Delta\tSeraphism + Soil"));
        Assert.Equal(["note:Seraphism", "Sacred Soil"], Only(r).Names());
        Assert.Equal(DiagnosticSeverity.Warning, Assert.Single(r.Diagnostics, d => d.Code == "not-available-at-level").Severity);
    }

    [Fact]
    public void Shorthand_IsResolvedAtTheFightsSyncLevel()
    {
        var at100 = Copy(PlanImports.Top, 100);
        Assert.Equal(["Concitation", "Physis II", "Guardian"], Import(Sheet("P5", "SCH\tSGE\tPLD", "Delta\tSuccor\tPhysis\t30%"), fight: at100).Plan.Entries.SelectMany(e => e.Names()).ToList());
        var at80 = Copy(PlanImports.Top, 80);
        Assert.Equal(["Heart of Stone"], Only(Import(Sheet("P5", "GNB", "Delta\tHoC"), fight: at80)).Names());
    }

    // ------------------------------------------------------------------ mechanics

    [Fact]
    public void RepeatedLabels_FollowRowOrder_UnknownLabelsAreReported()
    {
        var r = Import(Sheet("P5", "SCH", "Hello World\tSoil", "Omega\tSoil", "Hello World\tSoil", "Not a mechanic\tSoil"));
        Assert.Equal(["p5.delta_hello_world", "p5.omega", "p5.omega_hello_world_1", "p5.omega_hello_world_1"], r.Plan.Entries.Select(e => e.Mechanic));
        var unknown = Assert.Single(r.Diagnostics, d => d.Code == "unknown-mechanic");
        Assert.Equal((DiagnosticSeverity.Warning, "'Pasted'!A5"), (unknown.Severity, unknown.Src));
        Assert.Equal(2, r.Diagnostics.Count(d => d.Code == "ambiguous-mechanic" && d.Severity == DiagnosticSeverity.Info));
    }

    [Fact]
    public void LabelsWithExtraText_AndTypos_Resolve()
    {
        var r = Import(Sheet("P1", "SCH", "Pantokrator: line stacks (65k)\tSoil", "Wave Canon Kyrios\tSoil"));
        Assert.Equal(["p1.pantokrator", "p1.wave_cannon_kyrios"], r.Plan.Entries.Select(e => e.Mechanic));
        Assert.Contains(r.Diagnostics, d => d.Code == "fuzzy-mechanic");
    }

    // ------------------------------------------------------------------ timing

    [Fact]
    public void RowRelativeOffsets_AddTheSheetsRowOffset()
    {
        const string tsv = "Time\tMechanic\tWhite Mage\n\tP5 — Run: Dynamis  (from boss targetable)\t\n4:45\tBlind Faith (72k + knockback, phase end) ★\tPlenary (4:42)";
        Assert.Equal(-3.0, Only(Import(tsv)).Offset); // no preset: the row time is taken as the hit
        Assert.Equal(-4.2, Only(Import(tsv, "carry_the_clear")).Offset!.Value, 3); // Carry the Clear times the 7B87 cast end, 1.2 s early
        Assert.Equal(-4.2, Only(Import(tsv, title: "Carry the Clear: TOP Healing Plan")).Offset!.Value, 3); // preset found by the title
    }

    [Fact]
    public void ItemsOnRowsWithoutTime_UseThePhaseZeroPoint()
    {
        // Two timed rows fit P1's zero point: median of 29.1 − 28 and 108.0 − 107 = 1.05. The untimed Pantokrator row
        // converts through it.
        const string tsv = "Time\tMechanic\tWhite Mage\n\tP1 — Omega  (timer from pull)\t\n0:28\tProgram Loop\t\n\tPantokrator\tTemperance (1:18)\n1:47\tWave Cannon Kyrios\t";
        var r = Import(tsv);
        Assert.Equal(1.05, r.PhaseZeros["p1"], 2);
        Assert.Equal(0.05, r.PhaseSpread["p1"], 2);
        var entry = Only(r);
        Assert.Equal("p1.pantokrator", entry.Mechanic);
        Assert.Equal(-2.25, entry.Offset!.Value, 2); // 1.05 + 78 − 81.3

        // Without timed rows, the preset is the fallback.
        var preset = Import("Time\tMechanic\tWhite Mage\n\tP1\t\n\tPantokrator\tTemperance (1:18)", "carry_the_clear");
        Assert.Equal(0.9, preset.PhaseZeros["p1"]);
        Assert.Equal(-2.4, Only(preset).Offset!.Value, 2);
        Assert.False(preset.PhaseSpread.ContainsKey("p1"));

        var none = Import("Time\tMechanic\tWhite Mage\n\tP1\t\n\tPantokrator\tTemperance (1:18)");
        Assert.Null(Only(none).Offset);
        Assert.Contains(none.Diagnostics, d => d.Code == "untimed" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Hints_SetHitIndexAnchorWhenReadyAndPhaseEnd()
    {
        var r = Import(Sheet("P6", "SGE\tPLD", "Meteors\tKera after 2nd Hit + Eprog\tRampart on CD", "Flares\tHolos as P6 dies\tPassage after Meteors"));
        var kera = Assert.Single(r.Plan.Entries, e => e.Names()[0] == "Kerachole");
        Assert.Equal((1, PlanImporter.AfterHitOffset), (kera.HitIndex!.Value, kera.Offset!.Value));
        Assert.Null(Assert.Single(r.Plan.Entries, e => e.Names()[0] == "Eukrasian Prognosis").Offset);
        Assert.True(Assert.Single(r.Plan.Entries, e => e.Names()[0] == "Rampart").WhenReady);
        var holos = Assert.Single(r.Plan.Entries, e => e.Names()[0] == "Holos");
        Assert.Equal(("p6.run_mi_enrage", true), (holos.Mechanic, holos.AtPhaseEnd));
        var passage = Assert.Single(r.Plan.Entries, e => e.Names()[0] == "Passage of Arms");
        Assert.Equal(("p6.cosmo_meteor", 4, PlanImporter.AfterHitOffset), (passage.Mechanic, passage.HitIndex!.Value, passage.Offset!.Value));
    }

    [Fact]
    public void PressesAfterAPhasesLastHit_AreFlagged()
    {
        // The Blue Screen hit makes the timeline jump to P5 (docs/research/mechanics.md §5.2): 0:58 is never reached in P4.
        var r = Import("Time\tMechanic\tScholar\n\tP4\t\n0:56\tBlue Screen\tSoil (0:45) / Succor (0:58)");
        var flagged = Assert.Single(r.Diagnostics, d => d.Code == "after-phase-end");
        Assert.Equal(DiagnosticSeverity.Warning, flagged.Severity);
        Assert.Contains("Succor", flagged.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NotesTargets_ForSingleTargetActions()
    {
        const string tsv = "Time\tMechanic\tWhite Mage\tNotes\n\tP2\t\t\n0:12\tSolar Ray\tBenison (0:07) / Temperance (0:08)\tOn the tanks.";
        var r = Import(tsv);
        Assert.Equal("the tanks", Assert.Single(r.Plan.Entries, e => e.Names()[0] == "Divine Benison").Target);
        Assert.Null(Assert.Single(r.Plan.Entries, e => e.Names()[0] == "Temperance").Target); // party-wide
        Assert.All(r.Plan.Entries, e => Assert.Equal("On the tanks.", e.Detail));
    }

    // ------------------------------------------------------------------ plan

    [Fact]
    public void Plan_Header_Assignees_AndFilters()
    {
        const string tsv = "P5\tWhite Mage\tScholar\nDelta\tPlenary\tSoil";
        var r = Import(tsv);
        Assert.Equal("paste", r.Plan.Source!.Kind);
        Assert.Equal(["Pasted"], r.Plan.Source.Tabs);
        Assert.Equal("Test sheet", r.Plan.Name);
        Assert.Equal(["White Mage", "Scholar"], r.Plan.Assignees.Select(a => a.Key));
        Assert.Equal(r.Plan.Id, Import(tsv).Plan.Id); // name-based: stable across imports
        Assert.NotEqual(r.Plan.Id, Import(tsv, title: "Another sheet").Plan.Id);

        var filtered = Import(tsv, assignees: ["Scholar"]);
        Assert.Equal(["Scholar"], filtered.Plan.Assignees.Select(a => a.Key));
        Assert.Equal(["Sacred Soil"], Only(filtered).Names());
    }

    [Fact]
    public void UnknownTab_IsAnError()
    {
        var doc = new SheetDocument { Title = "x", Tabs = [SheetReaders.FromDelimited("P5\tSCH\nDelta\tSoil", '\t', "P5")] };
        var options = PlanImports.Options(null, ["P6"], null, null);
        var r = new PlanImporter().Import(doc, options);
        Assert.Contains(r.Diagnostics, d => d.Code == "unknown-tab" && d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(r.Plan.Entries);
    }

    private static FightDefinition Copy(FightDefinition f, int level) => new()
    {
        Key = f.Key,
        Name = f.Name,
        Territory = f.Territory,
        SyncLevel = level,
        TimelineFile = f.TimelineFile,
        Phases = f.Phases,
        Mechanics = f.Mechanics,
        Calibration = f.Calibration,
        RowOffsets = f.RowOffsets,
    };
}
