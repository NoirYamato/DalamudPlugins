using MitCue.Core.Actions;
using MitCue.Core.Fights;
using MitCue.Core.Plans;

namespace MitCue.Core.Tests.Plans;

/// <summary><see cref="PlanChecker"/> on small synthetic fights, catalogs and plans: one test group per rule.</summary>
public class PlanCheckerTests
{
    // ---- synthetic data ------------------------------------------------------------------------------
    //
    // Fight "test" (sync level 90): P1 from 0, P2 from 600 (a long P1 so that "P1 3:10" and "P1 4:50" exist),
    // P3 from 900. Every mechanic is named after its phase and its hit time.

    private static readonly ActionInfo Holos = Act(24310, "Holos", [Job.SGE], level: 76, recast: 120, group: 19, kind: ActionKind.PartyMit, duration: 20);
    private static readonly ActionInfo Kerachole = Act(24298, "Kerachole", [Job.SGE], level: 50, recast: 30, group: 3, kind: ActionKind.PartyMit, duration: 15, cost: new ResourceCost("Addersgall", 1));
    private static readonly ActionInfo Ixochole = Act(24299, "Ixochole", [Job.SGE], level: 52, recast: 30, group: 5, kind: ActionKind.Heal, cost: new ResourceCost("Addersgall", 1));
    private static readonly ActionInfo Taurochole = Act(24303, "Taurochole", [Job.SGE], level: 62, recast: 45, group: 4, kind: ActionKind.TankMit, duration: 15, cost: new ResourceCost("Addersgall", 1));
    private static readonly ActionInfo Druochole = Act(24296, "Druochole", [Job.SGE], level: 45, recast: 1, group: 2, kind: ActionKind.Heal, cost: new ResourceCost("Addersgall", 1));
    private static readonly ActionInfo Rhizomata = Act(24309, "Rhizomata", [Job.SGE], level: 74, recast: 90, group: 6, kind: ActionKind.Other);
    private static readonly ActionInfo Temperance = Act(16536, "Temperance", [Job.WHM], level: 80, recast: 120, group: 20, kind: ActionKind.PartyMit, duration: 20);
    private static readonly ActionInfo DivineCaress = Act(37011, "Divine Caress", [Job.WHM], level: 100, recast: 1, group: 21, kind: ActionKind.Shield, duration: 10, followUpOf: 16536);
    private static readonly ActionInfo LiturgyOfTheBell = Act(25862, "Liturgy of the Bell", [Job.WHM], level: 90, recast: 180, group: 22, kind: ActionKind.Heal, duration: 20);
    private static readonly ActionInfo DivineBenison = Act(7432, "Divine Benison", [Job.WHM], level: 66, recast: 30, group: 23, kind: ActionKind.Shield, duration: 15, charges: new() { [66] = 1, [88] = 2 });
    private static readonly ActionInfo Tetragrammaton = Act(3570, "Tetragrammaton", [Job.WHM], level: 60, recast: 60, group: 24, kind: ActionKind.Heal);
    private static readonly ActionInfo AfflatusRapture = Act(16534, "Afflatus Rapture", [Job.WHM], level: 76, recast: 2.5, group: 58, kind: ActionKind.Heal, gcd: true, cost: new ResourceCost("Lily", 1));
    private static readonly ActionInfo MedicaII = Act(133, "Medica II", [Job.WHM], level: 50, recast: 2.5, group: 58, kind: ActionKind.Regen, gcd: true, duration: 15, replacedBy: 37010, replacedAt: 96);
    private static readonly ActionInfo MedicaIII = Act(37010, "Medica III", [Job.WHM], level: 96, recast: 2.5, group: 58, kind: ActionKind.Regen, gcd: true, duration: 15, upgradeOf: 133);
    private static readonly ActionInfo Bloodwhetting = Act(25751, "Bloodwhetting", [Job.WAR], level: 82, recast: 25, group: 7, kind: ActionKind.TankMit, duration: 8);
    private static readonly ActionInfo NascentFlash = Act(16464, "Nascent Flash", [Job.WAR], level: 76, recast: 25, group: 7, kind: ActionKind.TankMit, duration: 8);
    private static readonly ActionInfo Succor = Act(186, "Succor", [Job.SCH], level: 35, recast: 2.5, group: 58, kind: ActionKind.Shield, gcd: true, duration: 30, castTime: 2);
    private static readonly ActionInfo Indomitability = Act(3583, "Indomitability", [Job.SCH], level: 52, recast: 30, group: 9, kind: ActionKind.Heal, cost: new ResourceCost("Aetherflow", 1));
    private static readonly ActionInfo Excogitation = Act(7434, "Excogitation", [Job.SCH], level: 62, recast: 45, group: 10, kind: ActionKind.Heal, duration: 45, cost: new ResourceCost("Aetherflow", 1));
    private static readonly ActionInfo SacredSoil = Act(188, "Sacred Soil", [Job.SCH], level: 50, recast: 30, group: 11, kind: ActionKind.PartyMit, duration: 15, cost: new ResourceCost("Aetherflow", 1));
    private static readonly ActionInfo Lustrate = Act(189, "Lustrate", [Job.SCH], level: 45, recast: 1, group: 12, kind: ActionKind.Heal, cost: new ResourceCost("Aetherflow", 1));
    private static readonly ActionInfo Aetherflow = Act(166, "Aetherflow", [Job.SCH], level: 45, recast: 60, group: 13, kind: ActionKind.Other);
    private static readonly ActionInfo Dissipation = Act(3587, "Dissipation", [Job.SCH], level: 60, recast: 180, group: 14, kind: ActionKind.Buff, duration: 30);
    private static readonly ActionInfo Recitation = Act(16542, "Recitation", [Job.SCH], level: 74, recast: 90, group: 15, kind: ActionKind.Buff, duration: 15);
    private static readonly ActionInfo SummonSeraph = Act(16545, "Summon Seraph", [Job.SCH], level: 80, recast: 120, group: 16, kind: ActionKind.Buff, duration: 22);
    private static readonly ActionInfo Consolation = Act(16546, "Consolation", [Job.SCH], level: 80, recast: 30, group: 17, kind: ActionKind.Shield, duration: 30, followUpOf: 16545, charges: new() { [80] = 2 });

    private static readonly ActionCatalog Catalog = new(
    [
        Holos, Kerachole, Ixochole, Taurochole, Druochole, Rhizomata, Temperance, DivineCaress, LiturgyOfTheBell, DivineBenison,
        Tetragrammaton, AfflatusRapture, MedicaII, MedicaIII, Bloodwhetting, NascentFlash, Succor, Indomitability, Excogitation,
        SacredSoil, Lustrate, Aetherflow, Dissipation, Recitation, SummonSeraph, Consolation,
    ]);

    private static ActionInfo Act(
        uint id, string name, Job[] jobs, int level, double recast, int group, ActionKind kind, double? duration = null, bool gcd = false,
        double castTime = 0, ResourceCost? cost = null, Dictionary<int, int>? charges = null, uint? followUpOf = null,
        uint? replacedBy = null, int? replacedAt = null, uint? upgradeOf = null) => new()
    {
        Id = id,
        Name = name,
        Jobs = jobs,
        Level = level,
        Recast = recast,
        CooldownGroup = group,
        Kind = kind,
        IsGcd = gcd,
        CastTime = castTime,
        Cost = cost,
        ChargesByLevel = charges ?? [],
        DurationByLevel = duration is { } d ? new Dictionary<int, double> { [level] = d } : [],
        FollowUpOf = followUpOf,
        ReplacedBy = replacedBy,
        ReplacedAtLevel = replacedAt,
        UpgradeOf = upgradeOf,
    };

    /// <summary>Mechanics at the given timeline times, named "pN.tT" after their phase and hit time.</summary>
    private static FightDefinition Fight(int syncLevel = 90, params double[] hits)
    {
        var phases = new[]
        {
            new Phase { Id = "p1", Name = "P1", Start = 0 },
            new Phase { Id = "p2", Name = "P2", Start = 600 },
            new Phase { Id = "p3", Name = "P3", Start = 900 },
        };
        var mechanics = hits
            .Concat([0.0, 10, 30, 100, 190, 290, 590, 610, 900, 1000])
            .Distinct()
            .Order()
            .Select(t =>
            {
                var phase = phases.Last(p => p.Start <= t).Id;
                return new Mechanic { Id = $"{phase}.t{t:0}", Name = $"Hit at {t:0}", Phase = phase, Hit = t, Hits = [t] };
            })
            .ToList();
        mechanics.Add(new Mechanic { Id = "p1.triple", Name = "Triple", Phase = "p1", Hit = 50, Hits = [50, 53, 56] });
        return new FightDefinition
        {
            Key = "test",
            Name = "Test",
            Territory = 1,
            SyncLevel = syncLevel,
            TimelineFile = "test.txt",
            Phases = phases,
            Mechanics = mechanics.OrderBy(m => m.Hit).ToList(),
        };
    }

    private static string M(double t) => (t >= 900 ? "p3" : t >= 600 ? "p2" : "p1") + $".t{t:0}";

    private static PlanItem Do(ActionInfo a) => PlanItem.ForAction(a.Id, a.Name);

    /// <summary>An entry anchored at the mechanic at <paramref name="hit"/> with offset 0 (pressed at the hit).</summary>
    private static PlanEntry At(double hit, params PlanItem[] items) => new()
    {
        Id = $"e{hit:0}",
        Mechanic = M(hit),
        Offset = 0,
        Do = [.. items],
    };

    private static IReadOnlyList<PlanFinding> Check(Job job, params PlanEntry[] entries) => Check(Fight(), job, entries);

    private static IReadOnlyList<PlanFinding> Check(FightDefinition fight, Job job, params PlanEntry[] entries) =>
        PlanChecker.Check(fight, Catalog, entries, job, fight.SyncLevel, new PlanCheckOptions { CheckFollowUps = true, CheckResources = true });

    private static PlanFinding Single(IEnumerable<PlanFinding> findings, string code) => Assert.Single(findings, f => f.Code == code);

    // ---- a clean plan --------------------------------------------------------------------------------

    [Fact]
    public void CleanPlan_HasNoFindings()
    {
        var findings = Check(
            Job.SGE,
            At(10, Do(Holos)),
            At(190, Do(Holos), Do(Kerachole)),
            At(610, Do(Holos)),
            At(900, PlanItem.ForNote("Everything")));
        Assert.Empty(findings);
    }

    // ---- mechanics ------------------------------------------------------------------------------------

    [Fact]
    public void UnknownMechanic_IsAnErrorWithoutATime_AndSuggestsANearMiss()
    {
        var entry = new PlanEntry { Id = "e1", Mechanic = "p1.t19", Src = "'Tab'!C3", Do = [Do(Holos)] };
        var f = Single(Check(Job.SGE, entry), "unknown-mechanic");
        Assert.Equal(PlanFindingSeverity.Error, f.Severity);
        Assert.Null(f.Time);
        Assert.Same(entry, f.Entry);
        Assert.Contains("'p1.t19'", f.Message);
        Assert.Contains("Entry e1 ('Tab'!C3)", f.Message);
        Assert.Contains("did you mean p1.t10?", f.Message);
    }

    [Fact]
    public void UnknownMechanic_WithoutPhasePrefix_SuggestsTheFullId()
    {
        var f = Single(Check(Job.SGE, new PlanEntry { Mechanic = "triple", Do = [Do(Holos)] }), "unknown-mechanic");
        Assert.Contains("did you mean p1.triple?", f.Message);
        Assert.Contains("Entry #1", f.Message);
    }

    [Fact]
    public void HitIndex_OutOfRange_IsAWarning()
    {
        var entry = new PlanEntry { Id = "e1", Mechanic = "p1.triple", HitIndex = 3, Offset = 0, Do = [Do(Holos)] };
        var f = Single(Check(Job.SGE, entry), "hit-index");
        Assert.Equal(PlanFindingSeverity.Warning, f.Severity);
        Assert.Contains("4th hit of Triple", f.Message);
        Assert.Contains("3 hits; the last hit is used", f.Message);
        Assert.Equal(56, f.Time);

        Assert.DoesNotContain(Check(Job.SGE, new PlanEntry { Mechanic = "p1.triple", HitIndex = 2, Do = [Do(Holos)] }), x => x.Code == "hit-index");
    }

    // ---- items: ids, job, level ------------------------------------------------------------------------

    [Fact]
    public void UnknownAction_IsAnError_AlsoInsideAChoice()
    {
        var findings = Check(
            Job.WHM,
            At(10, PlanItem.ForAction(424242, "Mystery")),
            At(30, PlanItem.AnyOf([Do(Tetragrammaton), PlanItem.ForAction(434343, "Other")])));
        var unknown = findings.Where(f => f.Code == "unknown-action").ToList();
        Assert.Equal(2, unknown.Count);
        Assert.All(unknown, f => Assert.Equal(PlanFindingSeverity.Error, f.Severity));
        Assert.Contains("Action 424242 (Mystery) at P1 0:10 is not in the action catalog", unknown[0].Message);
        Assert.Equal(434343u, unknown[1].ActionId);
    }

    [Fact]
    public void ActionOfAnotherJob_IsAnError()
    {
        var f = Single(Check(Job.WHM, At(30, Do(Kerachole))), "wrong-job");
        Assert.Equal(PlanFindingSeverity.Error, f.Severity);
        Assert.Equal("Kerachole at P1 0:30 is a SGE action, not WHM.", f.Message);
        Assert.Equal(30, f.Time);
    }

    [Fact]
    public void Choice_NeedsOneOptionOfTheJob()
    {
        // Cross-job alternatives are normal ("Kera/Soil"): fine as long as one option is the player's.
        Assert.Empty(Check(Job.SCH, At(30, PlanItem.AnyOf([Do(Kerachole), Do(SacredSoil)]))));

        var f = Single(Check(Job.WHM, At(30, PlanItem.AnyOf([Do(Kerachole), Do(SacredSoil)]))), "wrong-job");
        Assert.Contains("No option of Kerachole / Sacred Soil", f.Message);
    }

    [Fact]
    public void ActionLearnedAboveTheSyncLevel_IsAnError()
    {
        var tea = Fight(syncLevel: 80);
        var f = Single(Check(tea, Job.WHM, At(30, Do(LiturgyOfTheBell))), "not-available-at-level");
        Assert.Equal(PlanFindingSeverity.Error, f.Severity);
        Assert.Equal("Liturgy of the Bell at P1 0:30 is learned at level 90; TEST is synced to level 80.", f.Message);

        var choice = Single(Check(tea, Job.WHM, At(30, PlanItem.AnyOf([Do(LiturgyOfTheBell)]))), "not-available-at-level");
        Assert.Contains("can be used at level 80", choice.Message);
        Assert.Empty(Check(tea, Job.WHM, At(30, PlanItem.AnyOf([Do(LiturgyOfTheBell), Do(Tetragrammaton)]))));
    }

    [Fact]
    public void ActionWhoseButtonIsAnotherActionAtTheLevel_IsAWarning()
    {
        // Medica II is Medica III at 100 (the game reports Medica III) ...
        var dmu = Fight(syncLevel: 100);
        var up = Single(Check(dmu, Job.WHM, At(30, Do(MedicaII))), "level-variant");
        Assert.Equal(PlanFindingSeverity.Warning, up.Severity);
        Assert.Contains("Medica II at P1 0:30 is replaced by Medica III (37010) at level 100", up.Message);

        // ... and Medica III is not learned at 90: the button is Medica II.
        var down = Single(Check(Job.WHM, At(30, Do(MedicaIII))), "level-variant");
        Assert.Contains("Medica III at P1 0:30 is not learned at level 90: the button is Medica II (133)", down.Message);

        Assert.Empty(Check(Job.WHM, At(30, Do(MedicaII))));
    }

    [Fact]
    public void LevelVariantInAChoiceThatAlsoNamesTheReportedAction_IsNotAWarning()
    {
        // Review regression: {any: [Medica III, Medica II]} at 90 warned that Medica III's use is not tracked, though
        // the game reports Medica II, one of the choice's ids (TOP SCH: {any: [Concitation, Succor]}).
        Assert.Empty(Check(Job.WHM, At(30, PlanItem.AnyOf([Do(MedicaIII), Do(MedicaII)]))));
        Assert.Empty(Check(Fight(syncLevel: 100), Job.WHM, At(30, PlanItem.AnyOf([Do(MedicaII), Do(MedicaIII)]))));

        // A choice without the reported action is still flagged, for the option whose use is lost.
        var f = Single(Check(Job.WHM, At(30, PlanItem.AnyOf([Do(MedicaIII), Do(Tetragrammaton)]))), "level-variant");
        Assert.Contains("Medica III at P1 0:30 is not learned at level 90: the button is Medica II (133)", f.Message);
    }

    [Fact]
    public void ItemNameThatDoesNotMatchItsId_IsAWarning()
    {
        var f = Single(Check(Job.WHM, At(30, PlanItem.ForAction(Temperance.Id, "Asylum"))), "name-mismatch");
        Assert.Contains("named \"Asylum\" but action 16536 is Temperance", f.Message);
        Assert.Empty(Check(Job.WHM, At(30, PlanItem.ForAction(Temperance.Id, "temperance"))));
    }

    [Fact]
    public void EmptyEntriesAndItems_AreReported()
    {
        var findings = Check(Job.WHM, new PlanEntry { Id = "e1", Mechanic = "p1.t10" }, At(30, new PlanItem()), At(100, PlanItem.AnyOf([])));
        Assert.Equal(PlanFindingSeverity.Info, Single(findings, "empty-entry").Severity);
        Assert.Equal(2, findings.Count(f => f.Code == "empty-item"));
    }

    // ---- recasts -----------------------------------------------------------------------------------------

    [Fact]
    public void RecastConflictWithinAPhase_IsAnError()
    {
        var first = At(190, Do(Holos));
        var second = At(290, Do(Holos));
        var f = Single(Check(Job.SGE, first, second), "recast-conflict");
        Assert.Equal(PlanFindingSeverity.Error, f.Severity);
        Assert.Equal("Holos planned at P1 3:10 and P1 4:50 (100 s apart), recast 120 s.", f.Message);
        Assert.Equal(290, f.Time);
        Assert.Same(second, f.Entry);
        Assert.Same(first, Assert.Single(f.Related));
        Assert.Equal(Holos.Id, f.ActionId);
    }

    [Fact]
    public void RecastConflict_UsesThePressTimes_IncludingDefaultOffsets()
    {
        // Without an offset Holos (party mitigation, 20 s) is pressed 6 s before its hit: 184 and 294 → 110 s apart.
        var first = new PlanEntry { Mechanic = "p1.t190", Do = [Do(Holos)] };
        var second = new PlanEntry { Mechanic = "p1.t290", Offset = 4, Do = [Do(Holos)] };
        var f = Single(Check(Job.SGE, first, second), "recast-conflict");
        Assert.Contains("at P1 3:04 and P1 4:54 (110 s apart)", f.Message);
    }

    [Fact]
    public void EveryConsecutivePairCloserThanTheRecast_IsReported()
    {
        var findings = Check(Job.SGE, At(10, Do(Holos)), At(100, Do(Holos)), At(190, Do(Holos))).Where(f => f.Code == "recast-conflict").ToList();
        Assert.Equal(2, findings.Count);
        Assert.Contains("P1 0:10 and P1 1:40 (90 s apart)", findings[0].Message);
        Assert.Contains("P1 1:40 and P1 3:10 (90 s apart)", findings[1].Message);
    }

    [Fact]
    public void RecastConflictAcrossPhases_IsAWarning()
    {
        var f = Single(Check(Job.SGE, At(590, Do(Holos)), At(610, Do(Holos))), "recast-conflict");
        Assert.Equal(PlanFindingSeverity.Warning, f.Severity);
        Assert.StartsWith("Holos planned at P1 9:50 and P2 0:10 (20 s apart), recast 120 s", f.Message);
        Assert.Contains("spans the P1 → P2 transition, so the real gap depends on when P1 ends", f.Message);
    }

    [Fact]
    public void LessThanFiveSecondsToSpare_IsTight()
    {
        var f = Single(Check(Job.SGE, At(190, Do(Holos)), new PlanEntry { Mechanic = "p1.t290", Offset = 22, Do = [Do(Holos)] }), "recast-tight");
        Assert.Equal(PlanFindingSeverity.Warning, f.Severity);
        Assert.Equal("Holos planned at P1 3:10 and P1 5:12 (122 s apart), recast 120 s: only 2 s to spare.", f.Message);

        Assert.Empty(Check(Job.SGE, At(190, Do(Holos)), new PlanEntry { Mechanic = "p1.t290", Offset = 25, Do = [Do(Holos)] }));
    }

    [Fact]
    public void TightAcrossPhases_MentionsTheTransition()
    {
        var f = Single(Check(Job.SGE, At(10, Do(Holos)), new PlanEntry { Mechanic = "p2.t610", Offset = -478, Do = [Do(Holos)] }), "recast-tight");
        Assert.Contains("(122 s apart), recast 120 s: only 2 s to spare", f.Message);
        Assert.Contains("P1 → P2 transition", f.Message);
    }

    [Fact]
    public void Charges_AllowQuickUses_UpToTheirCount()
    {
        // Divine Benison: 2 charges at 90, one every 30 s.
        Assert.Empty(Check(Job.WHM, At(10, Do(DivineBenison)), At(10, Do(DivineBenison)), new PlanEntry { Mechanic = "p1.t30", Offset = 15, Do = [Do(DivineBenison)] }));

        var f = Single(Check(Job.WHM, At(10, Do(DivineBenison)), At(10, Do(DivineBenison)), At(30, Do(DivineBenison))), "recast-conflict");
        Assert.Equal(PlanFindingSeverity.Error, f.Severity);
        Assert.Equal("Divine Benison planned 3 times between P1 0:10 and P1 0:30 (20 s), 2 charges, recast 30 s: the use at P1 0:30 is 10 s early.", f.Message);
        Assert.Equal(2, f.Related.Count);
    }

    [Fact]
    public void Charges_RechargeOneAtATime()
    {
        // Two uses at 0:10 empty the charges; the first comes back at 0:40, the second at 1:10.
        var findings = Check(
            Job.WHM,
            At(10, Do(DivineBenison)),
            At(10, Do(DivineBenison)),
            new PlanEntry { Mechanic = "p1.t30", Offset = 12, Do = [Do(DivineBenison)] },  // 0:42 ok (charge since 0:40: tight)
            new PlanEntry { Mechanic = "p1.t30", Offset = 30, Do = [Do(DivineBenison)] }); // 1:00, the second charge is back at 1:10
        var tight = Single(findings, "recast-tight");
        Assert.Contains("the charge for the use at P1 0:42 is ready only 2 s before", tight.Message);
        var conflict = Single(findings, "recast-conflict");
        Assert.Contains("the use at P1 1:00 is 10 s early", conflict.Message);
        Assert.Contains("planned 4 times between P1 0:10 and P1 1:00 (50 s)", conflict.Message);

        // At level 80 Divine Benison has one charge.
        var level80 = Check(Fight(syncLevel: 80), Job.WHM, At(10, Do(DivineBenison)), At(30, Do(DivineBenison)));
        Assert.Equal("Divine Benison planned at P1 0:10 and P1 0:30 (20 s apart), recast 30 s.", Single(level80, "recast-conflict").Message);
    }

    [Fact]
    public void ActionsSharingARecastGroup_Conflict()
    {
        var f = Single(Check(Job.WAR, At(10, Do(Bloodwhetting)), At(30, Do(NascentFlash))), "recast-conflict");
        Assert.Equal("Bloodwhetting at P1 0:10 and Nascent Flash at P1 0:30 share a recast (20 s apart), recast 25 s.", f.Message);
        Assert.Empty(Check(Job.WAR, At(10, Do(Bloodwhetting)), At(100, Do(NascentFlash))));
    }

    [Fact]
    public void OptionalAndWhenReadyUses_DowngradeAConflictToAWarning()
    {
        var optional = At(290, Do(Holos));
        optional.Optional = true;
        var f = Single(Check(Job.SGE, At(190, Do(Holos)), optional), "recast-conflict");
        Assert.Equal(PlanFindingSeverity.Warning, f.Severity);
        Assert.EndsWith("(an optional use is involved).", f.Message);

        var whenReady = At(290, Do(Holos));
        whenReady.WhenReady = true;
        var g = Single(Check(Job.SGE, At(190, Do(Holos)), whenReady), "recast-conflict");
        Assert.Equal(PlanFindingSeverity.Warning, g.Severity);
        Assert.Contains("(planned when ready: it would be pressed 20 s late)", g.Message);
    }

    [Fact]
    public void ShortRecastActionsAndChoices_AreNotRecastChecked()
    {
        // A double Succor (GCD) and two Druocholes (1 s recast) are fine; a choice is whichever option is ready.
        Assert.Empty(Check(Job.SCH, At(30, Do(Succor), Do(Succor))));
        Assert.DoesNotContain(Check(Job.SGE, At(30, Do(Druochole), Do(Druochole))), f => f.Code.StartsWith("recast", StringComparison.Ordinal));
        Assert.Empty(Check(Job.WHM, At(10, PlanItem.AnyOf([Do(Tetragrammaton), Do(DivineBenison)])), At(30, PlanItem.AnyOf([Do(Tetragrammaton), Do(DivineBenison)]))));
    }

    [Fact]
    public void ActionsTheJobCannotUse_AreNotRecastChecked()
    {
        var findings = Check(Job.WHM, At(190, Do(Holos)), At(290, Do(Holos)));
        Assert.All(findings, f => Assert.Equal("wrong-job", f.Code));
    }

    // ---- gauges (heuristic) ------------------------------------------------------------------------------

    [Fact]
    public void Lily_FourSpendersWithinTwentySeconds_AreOneTooMany()
    {
        // Lilies start full (3) and come back one every 20 s.
        var spends = new[] { 30.0, 33, 36, 39 }.Select(t => new PlanEntry { Mechanic = "p1.t30", Offset = t - 30, Do = [Do(AfflatusRapture)] }).ToArray();
        var f = Single(Check(Job.WHM, spends), "resource-shortage");
        Assert.True(f.Heuristic);
        Assert.Equal(PlanFindingSeverity.Warning, f.Severity);
        Assert.Equal(39, f.Time);
        Assert.StartsWith("Afflatus Rapture at P1 0:39 needs 1 Lily, but none is left: 4 Lily spenders since P1 0:30 (Lily: 3 at most", f.Message);
        Assert.Equal(3, f.Related.Count);

        // The 0:40 tick brings one back.
        Assert.Empty(Check(Job.WHM, [.. spends.Take(3), new PlanEntry { Mechanic = "p1.t30", Offset = 11, Do = [Do(AfflatusRapture)] }]));
    }

    [Fact]
    public void Lily_TicksWhileFullAreLost()
    {
        // Full until 1:40, then three spends empty it; the next Lily comes at the next 20 s tick (2:00), not 20 s after 1:40.
        var findings = Check(
            Job.WHM,
            At(100, Do(AfflatusRapture), Do(AfflatusRapture), Do(AfflatusRapture)),
            new PlanEntry { Mechanic = "p1.t100", Offset = 19, Do = [Do(AfflatusRapture)] },
            new PlanEntry { Mechanic = "p1.t100", Offset = 20, Do = [Do(AfflatusRapture)] });
        var f = Single(findings, "resource-shortage");
        Assert.Equal(119, f.Time);
    }

    [Fact]
    public void Aetherflow_IsRefilledByAetherflowOnCooldown_AndByPlannedDissipations()
    {
        // Empty at the pull; Aetherflow gives 3 at 0:00 and again at 1:00.
        var spenders = new[] { Indomitability, Excogitation, SacredSoil, Lustrate };
        var four = spenders.Select((a, i) => new PlanEntry { Mechanic = "p1.t30", Offset = i, Do = [Do(a)] }).ToArray();
        var f = Single(Check(Job.SCH, four), "resource-shortage");
        Assert.Equal(33, f.Time);
        Assert.Contains("Lustrate at P1 0:33 needs 1 Aetherflow, but none is left: 4 Aetherflow spenders since P1 0:30", f.Message);

        Assert.Empty(Check(Job.SCH, [.. four, new PlanEntry { Mechanic = "p1.t30", Offset = 2.5, Do = [Do(Dissipation)] }]));
        Assert.Empty(Check(Job.SCH, [.. four.Take(3), new PlanEntry { Mechanic = "p1.t30", Offset = 31, Do = [Do(Lustrate)] }]));
    }

    [Fact]
    public void Aetherflow_IsEmptyBeforeThePull()
    {
        var f = Single(Check(Job.SCH, new PlanEntry { Mechanic = "p1.t0", Offset = -5, Do = [Do(SacredSoil)] }), "resource-shortage");
        Assert.Contains("Sacred Soil at P1 -0:05 needs 1 Aetherflow, but none is left", f.Message);
    }

    [Fact]
    public void Recitation_MakesTheNextIndomitabilityOrExcogitationFree()
    {
        var spenders = new[] { Indomitability, Excogitation, SacredSoil, Lustrate };
        var entries = spenders.Select((a, i) => new PlanEntry { Mechanic = "p1.t30", Offset = i, Do = [Do(a)] }).ToList();
        entries.Insert(0, new PlanEntry { Mechanic = "p1.t30", Offset = -1, Do = [Do(Recitation)] });
        Assert.Empty(Check(Job.SCH, [.. entries]));

        // Recitation lasts 15 s.
        entries[0] = new PlanEntry { Mechanic = "p1.t10", Offset = 0, Do = [Do(Recitation)] };
        Assert.Single(Check(Job.SCH, [.. entries]), x => x.Code == "resource-shortage");
    }

    [Fact]
    public void Addersgall_IsRefilledOverTime_AndByRhizomata()
    {
        var spenders = new[] { Kerachole, Ixochole, Taurochole, Druochole };
        var four = spenders.Select((a, i) => new PlanEntry { Mechanic = "p1.t30", Offset = i * 3, Do = [Do(a)] }).ToArray();

        // Full at the pull (3) and +1 at 0:40, but Rhizomata (+1) is ready as soon as the gauge has room.
        Assert.Empty(Check(Job.SGE, four));

        // One Druochole more before the 0:40 tick is too many: Rhizomata (90 s recast) was used at 0:30.
        var five = four.Append(new PlanEntry { Mechanic = "p1.t30", Offset = 9.5, Do = [Do(Druochole)] }).ToArray();
        var f = Single(Check(Job.SGE, five), "resource-shortage");
        Assert.Equal(39.5, f.Time);
        Assert.Contains("needs 1 Addersgall, but none is left: 4 Addersgall spenders since P1 0:33", f.Message);

        // After the 0:40 tick it is fine again.
        Assert.Empty(Check(Job.SGE, [.. four, new PlanEntry { Mechanic = "p1.t30", Offset = 10.5, Do = [Do(Druochole)] }]));
    }

    [Fact]
    public void ChoiceWithAFreeOption_SpendsNothing()
    {
        var entries = Enumerable.Range(0, 5).Select(i => new PlanEntry { Mechanic = "p1.t30", Offset = i, Do = [PlanItem.AnyOf([Do(AfflatusRapture), Do(MedicaII)])] }).ToArray();
        Assert.Empty(Check(Job.WHM, entries));
        var allSpend = Enumerable.Range(0, 5).Select(i => new PlanEntry { Mechanic = "p1.t30", Offset = i, Do = [PlanItem.AnyOf([Do(AfflatusRapture)])] }).ToArray();
        Assert.Equal(2, Check(Job.WHM, allSpend).Count(f => f.Code == "resource-shortage"));
    }

    [Fact]
    public void Gauges_CanBeSwitchedOff()
    {
        var spends = Enumerable.Range(0, 5).Select(i => new PlanEntry { Mechanic = "p1.t30", Offset = i, Do = [Do(AfflatusRapture)] }).ToArray();
        var fight = Fight();
        Assert.Empty(PlanChecker.Check(fight, Catalog, spends, Job.WHM, 90, new PlanCheckOptions { CheckResources = false }));
    }

    // ---- follow-ups (heuristic) ---------------------------------------------------------------------------

    [Fact]
    public void FollowUp_NeedsItsTriggerShortlyBefore()
    {
        var f = Single(Check(Job.SCH, At(100, Do(Consolation))), "follow-up-without-trigger");
        Assert.True(f.Heuristic);
        Assert.Equal("Consolation at P1 1:40 can only be used within 22 s after Summon Seraph; the plan has none before it.", f.Message);

        var late = Single(Check(Job.SCH, At(30, Do(SummonSeraph)), At(100, Do(Consolation))), "follow-up-without-trigger");
        Assert.Contains("the last one before it is planned at P1 0:30 (70 s before)", late.Message);

        Assert.Empty(Check(Job.SCH, new PlanEntry { Mechanic = "p1.t100", Offset = -10, Do = [Do(SummonSeraph)] }, At(100, Do(Consolation), Do(Consolation))));
        Assert.Empty(Check(Job.SCH, At(100, Do(SummonSeraph), Do(Consolation))));
    }

    [Fact]
    public void FollowUpNotLearnedAtTheLevel_IsOnlyReportedOnce()
    {
        var findings = Check(Job.WHM, At(100, Do(DivineCaress)));
        Assert.Equal("not-available-at-level", Assert.Single(findings).Code);
    }

    // ---- ordering and formatting --------------------------------------------------------------------------

    [Fact]
    public void Findings_AreInTimeOrder_UntimedFirst_ErrorsFirstAtEqualTimes()
    {
        var findings = Check(
            Job.WHM,
            At(290, Do(Kerachole)),
            At(100, PlanItem.ForAction(Temperance.Id, "Asylum")),
            new PlanEntry { Mechanic = "nope", Do = [Do(Temperance)] },
            At(100, Do(Holos)));
        Assert.Equal(["unknown-mechanic", "wrong-job", "name-mismatch", "wrong-job"], findings.Select(f => f.Code));
        Assert.Equal(new double?[] { null, 100, 100, 290 }, findings.Select(f => f.Time));
    }

    [Fact]
    public void Check_WithAPlayer_SelectsTheirEntries()
    {
        var plan = new MitPlan
        {
            Fight = "test",
            Entries =
            [
                new PlanEntry { Mechanic = "p1.t190", Offset = 0, Who = new WhoSpec { Job = Job.SGE }, Do = [Do(Holos)] },
                new PlanEntry { Mechanic = "p1.t290", Offset = 0, Who = new WhoSpec { Job = Job.SGE }, Do = [Do(Holos)] },
                new PlanEntry { Mechanic = "p1.t290", Offset = 0, Who = new WhoSpec { Job = Job.WHM }, Do = [Do(Kerachole)] },
            ],
        };
        var sge = PlanChecker.Check(Fight(), Catalog, plan, new PlayerContext(Job.SGE, 90));
        Assert.Equal("recast-conflict", Assert.Single(sge).Code);
        var whm = PlanChecker.Check(Fight(), Catalog, plan, new PlayerContext(Job.WHM, 90));
        Assert.Equal("wrong-job", Assert.Single(whm).Code);
    }

    [Theory]
    [InlineData(0, null, "P1 0:00")]
    [InlineData(-5, null, "P1 -0:05")]
    [InlineData(59.6, null, "P1 1:00")]
    [InlineData(190, null, "P1 3:10")]
    [InlineData(610.4, null, "P2 0:10")]
    [InlineData(598, "p2", "P2 -0:02")]
    [InlineData(1000, null, "P3 1:40")]
    public void FormatTime_IsPhaseRelative(double time, string? phase, string expected) =>
        Assert.Equal(expected, PlanChecker.FormatTime(Fight(), time, phase));

    [Fact]
    public void FormatTime_WithoutPhases_IsFromThePull()
    {
        var fight = new FightDefinition { Key = "x", Name = "X", Territory = 1, SyncLevel = 90, TimelineFile = "x.txt" };
        Assert.Equal("2:05", PlanChecker.FormatTime(fight, 125));
    }
}
