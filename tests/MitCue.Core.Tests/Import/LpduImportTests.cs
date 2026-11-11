using MitCue.Core.Import;
using MitCue.Core.Plans;

namespace MitCue.Core.Tests.Import;

/// <summary>
/// Acceptance test of the importer on the LPDU sheet (no times: automatic timing, hints, per-job expansion of slot and
/// role columns, tank-pair tabs). The rows and their mechanics are docs/research/mechanics.md §4.4.
/// </summary>
public class LpduImportTests
{
    private static MitPlan Plan => PlanImports.Lpdu.Plan;

    private static List<PlanEntry> For(Job job, string? slot = null, string? assignee = null) =>
        PlanSelection.For(Plan, new PlayerContext(job, 90, slot, assignee)).ToList();

    /// <summary>
    /// Every LPDU row with content in the H2 or H2 SCH column, as a Scholar in H2 gets it: mechanic, then the items
    /// ("A+B"; "note:" for notes; "+seq" when pressed in order). P1/P2's H2 column is slot-only, so "Kera/Soil" becomes
    /// Sacred Soil for a Scholar (and Kerachole for a Sage).
    /// </summary>
    public static TheoryData<string, string> ScholarH2 => new()
    {
        { "p1.loop_set3", "Sacred Soil" },                                     // 'P1/P2'!E2 Kera/Soil
        { "p1.pantokrator", "note:Everything" },                                // E4
        { "p2.solar_ray", "Sacred Soil" },                                     // E10 Soil/Kera
        { "p2.limitless", "note:Everything" },                                  // E11
        { "p3.hello_world", "Sacred Soil" },                                   // 'P3'!F2
        { "p3.latent_1", "Adloquium+Deployment Tactics+seq" },                 // Spreadlo
        { "p3.latent_2", "Expedient+Summon Seraph" },
        { "p3.latent_3", "Fey Illumination+Consolation" },                     // FeyIllum + 2nd Seraph Charge
        { "p3.latent_4", "Sacred Soil" },                                      // Soil (Debuffs at 13 secs/when rots explode)
        { "p3.critical_error", "Sacred Soil+Succor" },
        { "p4.protean_1", "Adloquium+Deployment Tactics+seq" },
        { "p4.stack_1", "Expedient+Sacred Soil" },
        { "p4.protean_2", "Summon Seraph" },
        { "p4.stack_3", "Fey Illumination" },
        { "p4.blue_screen", "Sacred Soil" },
        { "p5.delta", "Sacred Soil+Adloquium+Deployment Tactics+seq" },
        { "p5.delta_hello_world", "Summon Seraph" },                           // the first 'Hello World' (after Patch)
        { "p5.sigma", "Sacred Soil+Expedient+Fey Illumination+Summon Seraph" },
        { "p5.omega", "Sacred Soil+Summon Seraph+Adloquium+Deployment Tactics+seq" },
        { "p5.omega_hello_world_1", "Expedient" },                             // the second 'Hello World' (after Omega)
        { "p5.blind_faith", "Sacred Soil" },
        { "p6.cosmo_memory", "Sacred Soil+Dissipation" },
        { "p6.cosmo_dive_1", "Sacred Soil+Succor+Expedient" },                 // CD1
        { "p6.wave_cannon_1", "Succor+Sacred Soil+Adloquium+Deployment Tactics+seq" }, // Succor Proteans + Soil + Spreadlo Stack
        { "p6.wave_cannon_2", "Summon Seraph+Fey Illumination+Sacred Soil+Succor" },
        { "p6.cosmo_dive_2", "Sacred Soil+Succor" },
        { "p6.cosmo_meteor", "Expedient+Sacred Soil+Succor" },
        { "p6.flares", "Adloquium+Deployment Tactics+seq" },
        { "p6.magic_number_1", "Dissipation" },                               // the first 'Magic Number'
        { "p6.magic_number_2", "Angel Feathers" },                            // Healer LB3
    };

    [Fact]
    public void ScholarInH2_GetsExactlyTheH2Entries()
    {
        var mine = For(Job.SCH, "H2");
        var expected = ScholarH2.Select(r => ((string)r[0], (string)r[1])).ToList();
        var actual = mine.Select(e => (e.Mechanic, Describe(e))).ToList();
        Assert.Equal(expected, actual); // in time order, nothing else (no tank, DPS, other-healer or other-job entries)
        Assert.All(mine, e => Assert.Equal("H2", e.Who.Slot));
        Assert.All(mine, e => Assert.True(e.Offset is null, $"{e.Id} {e.Mechanic}: LPDU has no times, so timing is automatic"));
    }

    [Fact]
    public void ScholarInH2_KeepsHintsAsNotes()
    {
        var mine = For(Job.SCH, "H2");
        Assert.Equal("Debuffs at 13 secs/when rots explode", Assert.Single(mine, e => e.Mechanic == "p3.latent_4").Note);
        Assert.Equal("Succor Proteans; Spreadlo Stack", Assert.Single(mine, e => e.Mechanic == "p6.wave_cannon_1").Note);
    }

    [Fact]
    public void CrossJobAlternative_InASlotColumn_BecomesOneEntryPerJob()
    {
        var cell = Plan.At("'P1/P2'!E2");
        Assert.Equal(2, cell.Count);
        Assert.Equal(["Sacred Soil"], Assert.Single(cell, e => e.Who.Job == Job.SCH).Names());
        Assert.Equal(["Kerachole"], Assert.Single(cell, e => e.Who.Job == Job.SGE).Names());
        Assert.All(cell, e => Assert.Equal(("H2", "H2"), (e.Who.Slot, e.Who.Assignee)));
    }

    [Fact]
    public void Sage()
    {
        var mine = For(Job.SGE, "H2");
        Assert.Equal(["Kerachole"], Assert.Single(mine, e => e.Mechanic == "p1.loop_set3").Names());
        Assert.Equal(["Kerachole", "Zoe", "Eukrasian Prognosis"], Assert.Single(mine, e => e.Mechanic == "p3.latent_1").Names());
        Assert.True(Assert.Single(mine, e => e.Mechanic == "p3.latent_1").Seq); // Kera + Zoe Eprog
        Assert.Equal(["Physis II", "Panhaima"], Assert.Single(mine, e => e.Mechanic == "p4.stack_1").Names()); // Physis → Physis II at 90

        // "Eprog + Panhaima + Kera after 1st Hit": Kerachole gets its own entry, just after the first meteor hit.
        var meteors = mine.Where(e => e.Mechanic == "p6.cosmo_meteor").ToList();
        Assert.Equal(2, meteors.Count);
        Assert.Equal(["Eukrasian Prognosis", "Panhaima"], meteors[0].Names());
        Assert.Null(meteors[0].Offset);
        Assert.Equal(["Kerachole"], meteors[1].Names());
        Assert.Equal((0, PlanImporter.AfterHitOffset), (meteors[1].HitIndex ?? 0, meteors[1].Offset!.Value));
        Assert.Equal("after 1st Hit", meteors[1].Note);
        Assert.Equal(["Techne Makre"], Assert.Single(mine, e => e.Mechanic == "p6.magic_number_2").Names());
    }

    [Fact]
    public void WhiteMageInH1()
    {
        var mine = For(Job.WHM, "H1");
        Assert.Equal(["note:Everything"], Assert.Single(mine, e => e.Mechanic == "p1.pantokrator").Names());
        Assert.Equal(["Temperance"], Assert.Single(mine, e => e.Mechanic == "p3.latent_2").Names()); // Wings
        Assert.Equal(["Liturgy of the Bell"], Assert.Single(mine, e => e.Mechanic == "p3.latent_3").Names()); // Lilybell
        Assert.Equal(["Liturgy of the Bell", "note:Debuff at ~3 secs"], Assert.Single(mine, e => e.Mechanic == "p5.delta_patch").Names());
        Assert.Equal(["Liturgy of the Bell", "Temperance"], Assert.Single(mine, e => e.Mechanic == "p5.omega_hello_world_1").Names());

        var bell = Assert.Single(mine, e => e.Mechanic == "p6.cosmo_meteor"); // "Lilybell pop after 1st Hit"
        Assert.Equal(PlanImporter.AfterHitOffset, bell.Offset);
        Assert.Equal("pop after 1st Hit", bell.Note);
        Assert.Equal(["Pulse of Life"], Assert.Single(mine, e => e.Mechanic == "p6.magic_number_1").Names()); // Healer LB3
        Assert.DoesNotContain(mine, e => e.Who.Job is { } j && j != Job.WHM);
    }

    [Fact]
    public void AstrologianInH1()
    {
        var mine = For(Job.AST, "H1");
        var patch = Assert.Single(mine, e => e.Mechanic == "p5.delta_patch");
        Assert.Equal(["Macrocosmos", "Neutral Sect"], patch.Names());
        Assert.Equal("Neutralsect if P4 skip", patch.Note);
        Assert.Equal(["Collective Unconscious", "Neutral Sect"], Assert.Single(mine, e => e.Mechanic == "p6.wave_cannon_2").Names());
        Assert.Equal(["Astral Stasis"], Assert.Single(mine, e => e.Mechanic == "p6.magic_number_1").Names());
    }

    [Fact]
    public void WarriorMainTank()
    {
        var mine = For(Job.WAR, "MT");

        // Role phrases expand per job: "Party Mit" is Shake It Off for a Warrior.
        Assert.Equal(["Shake It Off"], Assert.Single(mine, e => e.Mechanic == "p1.pantokrator").Names());

        // 'General Tank Mit til P5' "Invuln Diffuse Wave Cannon" on the Pantokrator row names another mechanic.
        var invuln = Assert.Single(mine, e => e.Src == "'General Tank Mit til P5'!B2");
        Assert.Equal(("p1.wave_cannon_kyrios", "Holmgang"), (invuln.Mechanic, invuln.Names()[0]));

        // "Rampart as P1 dies" (a row without a label): anchored to P1's enrage and flagged for the phase end.
        var rampart = Assert.Single(mine, e => e.Src == "'General Tank Mit til P5'!B3");
        Assert.Equal(("p1.atomic_ray", true), (rampart.Mechanic, rampart.AtPhaseEnd));

        // "Rep + Short CD to R1": the targeted short cooldown.
        var protean = Assert.Single(mine, e => e.Src == "'P4'!B2");
        Assert.Equal(["Reprisal", "Nascent Flash"], protean.Names());
        Assert.Equal("R1", protean.Target);

        // "Rep + Party Mit after Solar Ray": Reprisal before the buster, the party mitigation just after its last hit.
        var solarRay = mine.Where(e => e.Src == "'P5'!B2").ToList();
        Assert.Equal(["Reprisal"], solarRay[0].Names());
        Assert.Null(solarRay[0].Offset);
        Assert.Equal(["Shake It Off"], solarRay[1].Names());
        Assert.Equal((1, PlanImporter.AfterHitOffset), (solarRay[1].HitIndex ?? 0, solarRay[1].Offset!.Value));
        Assert.Equal("after Solar Ray", solarRay[1].Note);

        Assert.True(Assert.Single(mine, e => e.Src == "'P5'!B7").WhenReady); // Party Mit on CD
        Assert.True(Assert.Single(mine, e => e.Src == "'General Tank Mit til P5'!B11").WhenReady); // Rampart when ever ready
        Assert.DoesNotContain(mine, e => e.Who.Slot is { } s && s != "MT");
    }

    [Fact]
    public void TankPairTabs_AreKeyedByPairAndJob()
    {
        var pairs = Plan.Assignees.Where(a => a.Key.Contains('/', StringComparison.Ordinal)).ToList();
        Assert.Equal(12, pairs.Count);
        Assert.Contains(pairs, a => a.Key == "WAR/DRK WAR" && a.Job == Job.WAR && a.Slot is null);
        Assert.Contains(pairs, a => a.Key == "GNB/PLD PLD" && a.Job == Job.PLD);

        // A Dark Knight paired with a Warrior picks "WAR/DRK DRK".
        var drk = Plan.Entries.Where(e => e.Who.Assignee == "WAR/DRK DRK").ToList();
        Assert.All(drk, e => Assert.Equal(Job.DRK, e.Who.Job));
        var wc2 = Assert.Single(drk, e => e.Mechanic == "p6.wave_cannon_2"); // Living Dead + TBN/Oblation Co-Tank
        Assert.Equal(["Living Dead", "The Blackest Night|Oblation"], wc2.Names());
        Assert.Equal("Co-Tank", wc2.Target);
        Assert.Equal(["note:Kitchen Sink"], Assert.Single(drk, e => e.Mechanic == "p5.solar_ray_1").Names());

        var war = Plan.Entries.Where(e => e.Who.Assignee == "WAR/GNB WAR").ToList();
        var wc1 = Assert.Single(war, e => e.Mechanic == "p6.wave_cannon_1"); // Holmgang + Nascant Co-Tank
        Assert.Equal(["Holmgang", "Nascent Flash"], wc1.Names());
        Assert.Equal("Co-Tank", wc1.Target);
        Assert.DoesNotContain(PlanImports.Lpdu.Diagnostics, d => d.Code == "tab-skipped");
    }

    [Fact]
    public void RoleColumnsWithoutSlot_AreExpandedPerJob()
    {
        // P6 'CM LB Tank' has no slot: every entry names its job, so other players never get it.
        var lb = Plan.Entries.Where(e => e.Who.Assignee == "CM LB Tank").ToList();
        Assert.All(lb, e => Assert.NotNull(e.Who.Job));
        Assert.Equal(["Dark Force", "Gunmetal Soul", "Land Waker", "Last Bastion"], lb.Where(e => e.Mechanic == "p6.cosmo_memory").Select(e => e.Names()[0]).Order());
        Assert.DoesNotContain(For(Job.SCH, "H2"), e => e.Who.Assignee == "CM LB Tank");
        Assert.DoesNotContain(For(Job.WHM), e => e.Who.Assignee == "CM LB Tank");
    }

    [Fact]
    public void RangedSlot_JobConditionAndRoleActions()
    {
        // "Party Mit if MCH" (R1): only a Machinist gets it.
        var stack2 = Plan.At("'P4'!J5");
        var mch = Assert.Single(stack2);
        Assert.Equal((Job.MCH, "Tactician"), (mch.Who.Job!.Value, mch.Names()[0]));

        // "Magick Barrier" (R2) is a Red Mage action; "Addle" is every caster's, so it stays with the slot.
        Assert.Equal(Job.RDM, Assert.Single(Plan.At("'P1/P2'!I4")).Who.Job);
        var addle = Assert.Single(Plan.At("'P1/P2'!I7"));
        Assert.Equal(("R2", (Job?)null), (addle.Who.Slot, addle.Who.Job));
        Assert.Equal(["Feint"], Assert.Single(For(Job.SAM, "M1"), e => e.Mechanic == "p1.panto_stack1").Names());
    }

    [Fact]
    public void RepeatedLabels_FollowRowOrder()
    {
        Assert.Equal("p5.delta_hello_world", Assert.Single(Plan.At("'P5'!F5")).Mechanic);
        Assert.Equal("p5.omega_hello_world_1", Assert.Single(Plan.At("'P5'!F10")).Mechanic);
        Assert.Equal("p6.magic_number_1", Assert.Single(Plan.At("'P6'!F9")).Mechanic);
        Assert.Equal("p6.magic_number_2", Assert.Single(Plan.At("'P6'!F10"), e => e.Who.Job == Job.SCH).Mechanic);
        Assert.Empty(PlanImports.Lpdu.PhaseZeros); // no times anywhere, and no calibration preset for LPDU
    }

    private static string Describe(PlanEntry e) => string.Join("+", e.Names()) + (e.Seq ? "+seq" : string.Empty);
}
