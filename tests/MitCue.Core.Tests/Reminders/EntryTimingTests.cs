using MitCue.Core.Actions;
using MitCue.Core.Fights;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;
using MitCue.Core.Tests.Data;
using MitCue.Core.Tests.Import;

namespace MitCue.Core.Tests.Reminders;

/// <summary>
/// <see cref="TimingRules.ForEntry"/>: one plan entry's press time and its items' use windows (docs/DESIGN.md §5.1, §5.2),
/// the one function the scheduler, the importer's estimates and the import review use.
/// </summary>
public class EntryTimingTests
{
    [Fact]
    public void WithoutOffset_TheFirstResolvableActionTimesEveryItem_EachItemKeepsItsOwnWindow()
    {
        var t = Kit.Timing(Kit.Entry("p1.raidwide", null, Kit.Note("Everything"), Kit.Do(Kit.Unlisted), Kit.Do(Kit.Temperance), Kit.Do(Kit.Tetragrammaton)));

        Assert.Equal(100, t.Hit, Kit.Tolerance);
        Assert.True(t.IsAutomatic);
        Assert.Equal(-6, t.AutomaticOffset, Kit.Tolerance);       // Temperance: the note and the unknown action do not count
        Assert.Equal(94, t.PressAt, Kit.Tolerance);
        Assert.Equal(-6, t.Offset, Kit.Tolerance);

        Assert.Equal(4, t.Items.Count);
        Assert.Equal(new ActionInfo?[] { null, null, Kit.Temperance, Kit.Tetragrammaton }, t.Items.Select(i => i.Action));
        Assert.Equal((84, 99.7), (t.Items[0].Earliest, t.Items[0].Latest));    // note: press − 10, hit − 0.3
        Assert.Equal((81, 99.7), (t.Items[2].Earliest, t.Items[2].Latest));    // 20 s mitigation: hit − 19
        Assert.Equal((84, 99), (t.Items[3].Earliest, t.Items[3].Latest));      // heal: press + 5
    }

    [Fact]
    public void ExplicitOffset_IsThePressTime_TheAutomaticOffsetIsStillReported()
    {
        var t = Kit.Timing(Kit.Entry("p1.raidwide", -2.5, Kit.Do(Kit.Temperance)));

        Assert.False(t.IsAutomatic);
        Assert.Equal(97.5, t.PressAt, Kit.Tolerance);
        Assert.Equal(-2.5, t.Offset, Kit.Tolerance);
        Assert.Equal(-6, t.AutomaticOffset, Kit.Tolerance);
        Assert.Equal((81, 99.7), (t.Items[0].Earliest, t.Items[0].Latest));
    }

    [Fact]
    public void EitherOr_IsTimedByItsFirstResolvableOption()
    {
        var t = Kit.Timing(Kit.Entry("p1.raidwide", null, Kit.Either(Kit.Unlisted, Kit.DivineBenison)));
        Assert.Equal(Kit.DivineBenison, t.Items[0].Action);
        Assert.Equal(96, t.PressAt, Kit.Tolerance);
    }

    [Theory]
    [InlineData(null, 160.0)]
    [InlineData(1, 163.0)]
    [InlineData(5, 166.0)]   // beyond the last hit: the last hit
    public void HitIndex_PicksTheHit(int? hitIndex, double hit)
    {
        var t = Kit.Timing(Kit.Entry("p1.triple", -2, Kit.Do(Kit.Temperance)).AtHit(hitIndex));
        Assert.Equal(hit, t.Hit, Kit.Tolerance);
        Assert.Equal(hit - 2, t.PressAt, Kit.Tolerance);
    }

    [Fact]
    public void NoItems_OnlyTheHitAndThePressTime()
    {
        var t = Kit.Timing(Kit.Entry("p1.raidwide", null));
        Assert.Empty(t.Items);
        Assert.Equal(97, t.PressAt, Kit.Tolerance);
    }

    [Fact]
    public void TheSchedulerBuildsItsRemindersFromIt()
    {
        var entry = Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Adloquium), Kit.Do(Kit.Tetragrammaton));
        var t = Kit.Timing(entry);
        var s = Kit.Scheduler(entry);

        Assert.Equal(2, s.All.Count);
        foreach (var r in s.All)
        {
            Assert.Equal(t.Hit, r.Hit, Kit.Tolerance);
            Assert.Equal(t.PressAt, r.PressAt, Kit.Tolerance);
            Assert.Equal(t.Items[r.ItemIndex].PressAt, r.PressAt, Kit.Tolerance);
            Assert.Equal(t.Items[r.ItemIndex].Earliest, r.Earliest, Kit.Tolerance);
            Assert.Equal(t.Items[r.ItemIndex].Latest, r.Latest, Kit.Tolerance);
            Assert.Equal(t.Items[r.ItemIndex].DueNotBefore, r.DueNotBefore);
        }
    }

    [Fact]
    public void UsesTheOptions()
    {
        var fight = Kit.TestFight();
        var entry = Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Temperance), Kit.Do(Kit.Tetragrammaton));
        var t = TimingRules.ForEntry(entry, fight.GetMechanic("p1.raidwide")!, fight, new FakeCatalog(Kit.Catalogued), new ReminderOptions { MitigationLatestBeforeHit = 1, HealLateWindow = 8 });
        Assert.Equal(99, t.Items[0].Latest, Kit.Tolerance);
        Assert.Equal(102, t.Items[1].Latest, Kit.Tolerance);
    }

    // ---- press: after (the hit sets HP to 1: the calls are the heal-up) ----------------------------------------

    /// <summary>Phase 1: a raidwide at 100 and an HP-to-1 hit at 130 whose calls are pressed after it (sync 90).</summary>
    private static FightDefinition PressAfterFight() => Kit.Fight(
        90,
        Kit.Mech("p1.raidwide", 100),
        new Mechanic { Id = "p1.drop", Name = "drop", Phase = "p1", Hit = 130, Hits = [130], PressAfter = true });

    [Theory]
    [InlineData(16536u, 1.0)]    // Temperance: mitigation before the hit elsewhere
    [InlineData(7432u, 1.0)]     // Divine Benison: a shield
    [InlineData(133u, 1.0)]      // Medica II: a regen
    [InlineData(7434u, 1.0)]     // Excogitation: a heal placed before the damage elsewhere
    [InlineData(3570u, 1.0)]     // Tetragrammaton: a heal, as anywhere
    [InlineData(208u, 2.0)]      // Pulse of Life: a healer LB keeps its later +2
    [InlineData(999999u, 1.0)]   // an action the catalog does not know, like a note
    public void PressAfter_EveryCallWithoutATimeIsPressedAfterTheHit(uint actionId, double offset)
    {
        var t = Kit.Timing(Kit.Entry("p1.drop", null, PlanItem.ForAction(actionId, "x")), PressAfterFight());
        Assert.Equal(offset, t.AutomaticOffset, Kit.Tolerance);
        Assert.Equal(130 + offset, t.PressAt, Kit.Tolerance);
        Assert.Equal(130 + offset + 5, t.Items[0].Latest, Kit.Tolerance);  // usable for the heal window after the press
    }

    [Fact]
    public void PressAfter_AllItemsOfAnEntry_AndNotes_AreAfterTheHit()
    {
        var t = Kit.Timing(Kit.Entry("p1.drop", null, Kit.Do(Kit.MedicaII), Kit.Do(Kit.Temperance), Kit.Note("Heal up")), PressAfterFight());
        Assert.Equal(131, t.PressAt, Kit.Tolerance);
        Assert.All(t.Items, i => Assert.Equal(136, i.Latest, Kit.Tolerance));

        Assert.Equal(131, Kit.Timing(Kit.Entry("p1.drop", null, Kit.Note("Heal up")), PressAfterFight()).PressAt, Kit.Tolerance);
    }

    [Fact]
    public void PressAfter_AWrittenTimeStays_OtherMechanicsAreNotAffected()
    {
        var fight = PressAfterFight();
        var timed = Kit.Timing(Kit.Entry("p1.drop", -3, Kit.Do(Kit.Temperance)), fight);
        Assert.Equal(127, timed.PressAt, Kit.Tolerance);
        Assert.Equal(1, timed.AutomaticOffset, Kit.Tolerance);

        Assert.Equal(94, Kit.Timing(Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Temperance)), fight).PressAt, Kit.Tolerance);
    }

    // ---- heal_lead (heals top the targets up for the hit) ------------------------------------------------------

    /// <summary>A raidwide at 100 and a tankbuster at 130 whose heals go 4 s before it (sync 90).</summary>
    private static FightDefinition HealLeadFight() => Kit.Fight(
        90,
        Kit.Mech("p1.raidwide", 100),
        new Mechanic { Id = "p1.buster", Name = "buster", Phase = "p1", Hit = 130, Hits = [130], HealLead = 4 });

    [Theory]
    [InlineData(3570u, -4.0)]     // Tetragrammaton: a heal goes before the hit here
    [InlineData(208u, -4.0)]      // Pulse of Life: so does a healer LB
    [InlineData(7434u, -8.0)]     // Excogitation: a heal placed before the damage keeps its own lead
    [InlineData(16536u, -6.0)]    // Temperance: mitigation keeps its own lead
    [InlineData(7432u, -4.0)]     // Divine Benison: a shield, its own −4
    [InlineData(999999u, -3.0)]   // not in the catalog: like a note
    public void HealLead_HealsArePressedThatLongBeforeTheHit(uint actionId, double offset)
    {
        var t = Kit.Timing(Kit.Entry("p1.buster", null, PlanItem.ForAction(actionId, "x")), HealLeadFight());
        Assert.Equal(offset, t.AutomaticOffset, Kit.Tolerance);
        Assert.Equal(130 + offset, t.PressAt, Kit.Tolerance);
    }

    [Fact]
    public void HealLead_TheEntryFollowsItsFirstAction_AWrittenTimeStays_OtherMechanicsAreNotAffected()
    {
        var fight = HealLeadFight();
        var healFirst = Kit.Timing(Kit.Entry("p1.buster", null, Kit.Do(Kit.Tetragrammaton), Kit.Do(Kit.Temperance)), fight);
        Assert.Equal(126, healFirst.PressAt, Kit.Tolerance);
        Assert.Equal(131, healFirst.Items[0].Latest, Kit.Tolerance);   // the heal's window: press + 5
        Assert.Equal(129.7, healFirst.Items[1].Latest, Kit.Tolerance); // the mitigation's: before the hit

        Assert.Equal(124, Kit.Timing(Kit.Entry("p1.buster", null, Kit.Do(Kit.Temperance), Kit.Do(Kit.Tetragrammaton)), fight).PressAt, Kit.Tolerance);
        Assert.Equal(131, Kit.Timing(Kit.Entry("p1.buster", 1, Kit.Do(Kit.Tetragrammaton)), fight).PressAt, Kit.Tolerance);
        Assert.Equal(101, Kit.Timing(Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Tetragrammaton)), fight).PressAt, Kit.Tolerance);
    }

    // ---- several actions: a heal or a buff first ------------------------------------------------------------------

    /// <summary>A 10 s party mitigation at level 90 (Reprisal): −3 s.</summary>
    private static readonly ActionInfo TenSecondMit = Kit.Act(7535, "Reprisal", ActionKind.PartyMit, duration: 10, jobs: [Job.PLD]);

    /// <summary>Passage of Arms: party mitigation with a lead of its own (2 s, a channel any action ends, pressed last).</summary>
    private static readonly ActionInfo PassageOfArms = Kit.Act(7385, "Passage of Arms", ActionKind.PartyMit, duration: 18, jobs: [Job.PLD], when: UseTiming.Before, lead: 2);

    private static EntryTiming Several(params ActionInfo[] actions)
    {
        var fight = Kit.TestFight();
        return TimingRules.ForEntry(Kit.Entry("p1.raidwide", null, [.. actions.Select(Kit.Do)]), fight.GetMechanic("p1.raidwide")!, fight, new FakeCatalog([.. Kit.Catalogued, TenSecondMit, PassageOfArms]));
    }

    [Fact]
    public void Several_AHealFirst_IsPressedWithTheEarliestActionBeforeTheHit()
    {
        Assert.Equal(-6, Several(Kit.Tetragrammaton, Kit.DivineBenison, Kit.Temperance).AutomaticOffset, Kit.Tolerance); // "Bless/Whisper/Succor"
        Assert.Equal(-2, Several(Kit.PulseOfLife, Kit.HallowedGround).AutomaticOffset, Kit.Tolerance);
        Assert.Equal(1, Several(Kit.Tetragrammaton, Kit.PulseOfLife).AutomaticOffset, Kit.Tolerance);                    // only heals: after the hit
    }

    [Fact]
    public void Several_ABuffFirst_IsPressedWithTheEarliestActionBeforeTheHit()
    {
        Assert.Equal(-4.5, Several(Kit.Swiftcast, Kit.DivineBenison, Kit.Adloquium).AutomaticOffset, Kit.Tolerance);    // "Reci/Deploy/Soil"
        Assert.Equal(-8, Several(Kit.Swiftcast, Kit.Tetragrammaton, Kit.Excogitation).AutomaticOffset, Kit.Tolerance);   // a pre-placed heal counts
        Assert.Equal(-3, Several(Kit.Swiftcast, Kit.Tetragrammaton).AutomaticOffset, Kit.Tolerance);                     // nothing earlier: the buff's −3
    }

    [Fact]
    public void Several_MitigationShieldsRegensAndInvulnsFirst_KeepTheirLead()
    {
        Assert.Equal(-3, Several(TenSecondMit, Kit.DivineBenison).AutomaticOffset, Kit.Tolerance);       // "Reprisal/Party Mit": still covers a later hit
        Assert.Equal(-4, Several(Kit.DivineBenison, Kit.Temperance).AutomaticOffset, Kit.Tolerance);
        Assert.Equal(-4.5, Several(Kit.MedicaII, Kit.Temperance).AutomaticOffset, Kit.Tolerance);
        Assert.Equal(-2, Several(Kit.HallowedGround, Kit.Rampart).AutomaticOffset, Kit.Tolerance);
        Assert.Equal(-6, Several(Kit.Bell, Kit.Excogitation).AutomaticOffset, Kit.Tolerance);           // a pre-placed heal first keeps its own
    }

    [Fact]
    public void Several_AnItemsOwnLeadCountsWhereverTheCellListsIt()
    {
        // "CU + Neutral Sect": Neutral Sect's 4.5 s for its Helios, as "Neutral Sect + CU"; the 10 s mitigation pressed then
        // still lasts through the hit from the NOW cue.
        Assert.Equal(-4.5, Several(TenSecondMit, Kit.NeutralSect).AutomaticOffset, Kit.Tolerance);
        Assert.Equal(-4.5, Several(Kit.NeutralSect, TenSecondMit).AutomaticOffset, Kit.Tolerance);
        Assert.Equal(-4.5, Several(Kit.Swiftcast, Kit.NeutralSect).AutomaticOffset, Kit.Tolerance);   // a buff first, as before
        Assert.Equal(-3, Several(TenSecondMit, PassageOfArms).AutomaticOffset, Kit.Tolerance);       // a later lead pulls nothing

        // Never so early that another item, used on the NOW cue, no longer lasts through the hit: −(10 − 2.5) for the 10 s
        // mitigation beside Earthly Star, also when the star's lead times the entry (it comes first).
        foreach (var star in new[] { Several(TenSecondMit, Kit.EarthlyStar), Several(Kit.EarthlyStar, TenSecondMit) })
        {
            Assert.Equal(-7.5, star.AutomaticOffset, Kit.Tolerance);
            Assert.Equal(TimingRules.EarliestCoveringUse(100, 10), star.DueAt(new ReminderOptions()), Kit.Tolerance);
            Assert.All(star.Items, i => Assert.True(i.Earliest <= i.DueAt(new ReminderOptions()) + Kit.Tolerance));
        }

        Assert.Equal(-10, Several(Kit.Tetragrammaton, Kit.EarthlyStar).AutomaticOffset, Kit.Tolerance); // a heal has no effect to keep

        // After heal_lead too, but not on a press: after mechanic (the calls are the heal-up).
        Assert.Equal(-4.5, Kit.Timing(Kit.Entry("p1.buster", null, Kit.Do(Kit.Tetragrammaton), Kit.Do(Kit.NeutralSect)), HealLeadFight()).AutomaticOffset, Kit.Tolerance);
        Assert.Equal(1, Kit.Timing(Kit.Entry("p1.drop", null, Kit.Do(Kit.Temperance), Kit.Do(Kit.NeutralSect)), PressAfterFight()).AutomaticOffset, Kit.Tolerance);
    }

    [Fact]
    public void Several_PressAfterAndHealLeadComeFirst()
    {
        var afterFight = PressAfterFight();
        Assert.Equal(1, Kit.Timing(Kit.Entry("p1.drop", null, Kit.Do(Kit.Tetragrammaton), Kit.Do(Kit.Temperance)), afterFight).AutomaticOffset, Kit.Tolerance);
        Assert.Equal(1, Kit.Timing(Kit.Entry("p1.drop", null, Kit.Do(Kit.Swiftcast), Kit.Do(Kit.Adloquium)), afterFight).AutomaticOffset, Kit.Tolerance);

        var leadFight = HealLeadFight();
        Assert.Equal(-4, Kit.Timing(Kit.Entry("p1.buster", null, Kit.Do(Kit.Tetragrammaton), Kit.Do(Kit.Temperance)), leadFight).AutomaticOffset, Kit.Tolerance);
        Assert.Equal(-6, Kit.Timing(Kit.Entry("p1.buster", null, Kit.Do(Kit.Swiftcast), Kit.Do(Kit.Temperance)), leadFight).AutomaticOffset, Kit.Tolerance);
    }

    // ---- the NOW cue never comes before the earliest use ------------------------------------------------------------

    /// <summary>The Blackest Night: a 7 s shield (−4 s on its own).</summary>
    private static readonly ActionInfo SevenSecondShield = Kit.Act(7393, "The Blackest Night", ActionKind.Shield, duration: 7, jobs: [Job.DRK]);

    /// <summary>Heart of Corundum: 8 s tank mitigation (−2.4 s on its own).</summary>
    private static readonly ActionInfo EightSecondMit = Kit.Act(25758, "Heart of Corundum", ActionKind.TankMit, duration: 8, jobs: [Job.GNB]);

    private static EntryTiming BesideRampart(double? offset, params ActionInfo[] actions)
    {
        var fight = Kit.TestFight();
        return TimingRules.ForEntry(Kit.Entry("p1.raidwide", offset, [.. actions.Select(Kit.Do)]), fight.GetMechanic("p1.raidwide")!, fight, new FakeCatalog([.. Kit.Catalogued, SevenSecondShield, EightSecondMit]));
    }

    [Fact]
    public void NowCue_NeverBeforeTheEarliestUse_WhenThePressIsAtOrAfterIt()
    {
        // "Rampart + TBN" on a raidwide at 100: Rampart's −6 s times both. A 7 s shield pressed at 94 is on time from 94
        // (100 − 7 + 1), so it turns due then, not at 92.5, and a press on its NOW cue lasts 1 s past the hit; Rampart (20 s)
        // keeps its NOW cue. An 8 s call turns due at 93, 1 s before its press.
        var options = new ReminderOptions();
        var t = BesideRampart(null, Kit.Rampart, SevenSecondShield, EightSecondMit);
        Assert.Equal(-6, t.AutomaticOffset, Kit.Tolerance);
        Assert.Equal([94.0, 94.0, 94.0], t.Items.Select(i => i.PressAt));
        Assert.Equal([92.5, 94.0, 93.0], t.Items.Select(i => i.DueAt(options)));
        Assert.Equal([81.0, 94.0, 93.0], t.Items.Select(i => i.Earliest));
        Assert.Null(t.Items[0].DueNotBefore);
        Assert.All(t.Items, i => Assert.True(i.Earliest <= i.DueAt(options) + Kit.Tolerance));

        // The same for a written press at or after that use; one before it (the sheet asks for an early press) keeps its
        // plain NOW cue.
        Assert.Equal(94, BesideRampart(-6, Kit.Rampart, SevenSecondShield).Items[1].DueAt(options), Kit.Tolerance);
        Assert.Equal(94, BesideRampart(-5.5, Kit.Rampart, SevenSecondShield).Items[1].DueAt(options), Kit.Tolerance);
        var early = BesideRampart(-8, Kit.Rampart, SevenSecondShield).Items[1];
        Assert.Null(early.DueNotBefore);
        Assert.Equal(90.5, early.DueAt(options), Kit.Tolerance);

        // Whatever the player's due lead: a longer one still turns Rampart due earlier, but not the shield.
        var longLead = new ReminderOptions { DueLead = 3 };
        Assert.Equal([91.0, 94.0, 93.0], t.Items.Select(i => i.DueAt(longLead)));

        // The scheduler: the shield is due at 94, and a press on that cue is on time.
        var fight = Kit.TestFight();
        var s = new ReminderScheduler(fight, [Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Rampart), Kit.Do(SevenSecondShield))], new FakeCatalog([.. Kit.Catalogued, SevenSecondShield]), options);
        var shield = Assert.Single(s.All, r => r.ItemIndex == 1);
        s.Update(93.9);
        Assert.Equal(ReminderState.Upcoming, shield.State);
        s.Update(94);
        Assert.Equal(ReminderState.Due, shield.State);
        s.OnOwnAction(SevenSecondShield.Id, 94);
        Assert.Equal((ReminderState.Done, false), (shield.State, shield.UsedEarly));
    }

    // ---- several hits: mitigation covers the later hits it can reach ------------------------------------------------

    /// <summary>Two hits 6.1 s apart at 100 (Optical Sight), two 7 s apart at 200, two 12 s apart at 300 (sync 90).</summary>
    private static FightDefinition MultiHitFight() => Kit.Fight(
        90,
        Kit.Mech("p1.two", 100, 100, 106.1),
        Kit.Mech("p1.seven", 200, 200, 207),
        Kit.Mech("p1.far", 300, 300, 312),
        Kit.Mech("p1.one", 400));

    private static EntryTiming Multi(PlanEntry entry) =>
        TimingRules.ForEntry(entry, MultiHitFight().GetMechanic(entry.Mechanic)!, MultiHitFight(), new FakeCatalog([.. Kit.Catalogued, TenSecondMit]));

    [Fact]
    public void Coverage_TenSecondMitigation_AlsoCoversTheSecondHit_EvenPressedOnTheNowCue()
    {
        var t = Multi(Kit.Entry("p1.two", null, Kit.Do(TenSecondMit)));
        Assert.Equal(-1.9, t.AutomaticOffset, Kit.Tolerance);      // 106.1 − 10 + 2 − 100: not −3
        Assert.Equal(96.6, t.Items[0].Earliest, Kit.Tolerance);    // ends 0.5 s after the second hit: the NOW cue (98.1 − 1.5)
        Assert.Equal(99.7, t.Items[0].Latest, Kit.Tolerance);      // still before the first hit
    }

    [Fact]
    public void Coverage_OnlyHitsTheEffectReachesWithTheMargins()
    {
        Assert.Equal(-3, Multi(Kit.Entry("p1.far", null, Kit.Do(TenSecondMit))).AutomaticOffset, Kit.Tolerance);  // 12 s: out of reach
        Assert.Equal(-3, Multi(Kit.Entry("p1.one", null, Kit.Do(TenSecondMit))).AutomaticOffset, Kit.Tolerance);  // one hit
        Assert.Equal(-6, Multi(Kit.Entry("p1.far", null, Kit.Do(Kit.Temperance))).AutomaticOffset, Kit.Tolerance); // 20 s from −6 reaches it anyway
        Assert.Equal(-1, Multi(Kit.Entry("p1.seven", null, Kit.Do(Kit.HallowedGround))).AutomaticOffset, Kit.Tolerance); // an invuln: 207 − 10 + 2 − 200
    }

    [Fact]
    public void Coverage_TheItemThatNeedsTheLatestPressTimesTheEntry_ShieldsRegensAndHealsDoNotCount()
    {
        Assert.Equal(-1.9, Multi(Kit.Entry("p1.two", null, Kit.Do(Kit.Temperance), Kit.Do(TenSecondMit))).AutomaticOffset, Kit.Tolerance);
        Assert.Equal(-4, Multi(Kit.Entry("p1.two", null, Kit.Do(Kit.DivineBenison))).AutomaticOffset, Kit.Tolerance);
        Assert.Equal(-4.5, Multi(Kit.Entry("p1.two", null, Kit.Do(Kit.MedicaII))).AutomaticOffset, Kit.Tolerance);
        Assert.Equal(-6, Multi(Kit.Entry("p1.two", null, Kit.Do(Kit.Tetragrammaton), Kit.Do(Kit.Temperance))).AutomaticOffset, Kit.Tolerance);

        var mixed = Multi(Kit.Entry("p1.two", null, Kit.Do(TenSecondMit), Kit.Do(Kit.DivineBenison)));
        Assert.Equal(96.6, mixed.Items[0].Earliest, Kit.Tolerance);
        Assert.Equal(86, mixed.Items[1].Earliest, Kit.Tolerance);   // the shield's own window: 100 − 15 + 1
    }

    [Fact]
    public void Coverage_NotForAHitIndexOrAWrittenTime()
    {
        Assert.Equal(-3, Multi(Kit.Entry("p1.two", null, Kit.Do(TenSecondMit)).AtHit(1)).AutomaticOffset, Kit.Tolerance);

        var timed = Multi(Kit.Entry("p1.two", -3, Kit.Do(TenSecondMit)));
        Assert.Equal(97, timed.PressAt, Kit.Tolerance);
        Assert.Equal(91, timed.Items[0].Earliest, Kit.Tolerance);  // the plain window: 100 − 10 + 1
    }

    [Fact]
    public void Coverage_APressThatEndsBeforeTheSecondHitIsEarly()
    {
        var fight = MultiHitFight();
        var s = new ReminderScheduler(fight, [Kit.Entry("p1.two", null, Kit.Do(TenSecondMit))], new FakeCatalog([TenSecondMit]), new ReminderOptions());
        var r = Assert.Single(s.All);
        s.OnOwnAction(TenSecondMit.Id, 96.0);                      // would end at 106.0, before the second hit
        Assert.True(r.UsedEarly);

        s = new ReminderScheduler(fight, [Kit.Entry("p1.two", null, Kit.Do(TenSecondMit))], new FakeCatalog([TenSecondMit]), new ReminderOptions());
        r = Assert.Single(s.All);
        s.Update(96.6);
        Assert.Equal(ReminderState.Due, r.State);
        s.OnOwnAction(TenSecondMit.Id, 96.6);                      // on the NOW cue: on time
        Assert.Equal(ReminderState.Done, r.State);
        Assert.False(r.UsedEarly);
    }

    /// <summary>
    /// A first hit at 100 and a later one 8.3 s after it (TOP's Wave Cannon: two proteans, then the Wild Charge), one 8.8 s
    /// after it, one 8.5 s and one 8.6 s after it (sync 90).
    /// </summary>
    private static FightDefinition WildChargeFight() => Kit.Fight(
        90,
        Kit.Mech("p1.charge", 100, 100, 102, 108.3),
        Kit.Mech("p1.beyond", 300, 300, 308.8),
        Kit.Mech("p1.edge", 500, 500, 508.5),
        Kit.Mech("p1.short", 700, 700, 708.6));

    private static EntryTiming Charge(string mechanic, ReminderOptions? options, params ActionInfo[] actions)
    {
        var fight = WildChargeFight();
        return TimingRules.ForEntry(Kit.Entry(mechanic, null, [.. actions.Select(Kit.Do)]), fight.GetMechanic(mechanic)!, fight, ChargeCatalog, options);
    }

    private static FakeCatalog ChargeCatalog => new([.. Kit.Catalogued, TenSecondMit, PassageOfArms]);

    [Fact]
    public void Coverage_ALaterHitUpToD15_IsReachedWithAHeldNowCue()
    {
        // 108.3 is 8.3 s after the first hit: beyond 10 − 2.5 s, within 10 − 1.5 s. The press would have to wait until
        // 108.3 − 10 + 2 = 100.3, after the first hit: it is 0.5 s before it instead, and the NOW cue waits until
        // 108.3 − 10 + 0.5 = 98.8, so that a press on it still lasts 0.5 s past 108.3.
        var options = new ReminderOptions();
        foreach (var action in new[] { TenSecondMit, Kit.HallowedGround })
        {
            var t = Charge("p1.charge", null, action);
            Assert.Equal(-0.5, t.AutomaticOffset, Kit.Tolerance);
            Assert.Equal(98.8, t.Items[0].DueNotBefore!.Value, Kit.Tolerance);
            Assert.Equal(98.8, t.Items[0].DueAt(options), Kit.Tolerance);
            Assert.Equal(98.8, t.Items[0].Earliest, Kit.Tolerance);
            Assert.Equal(100, t.Items[0].Latest, Kit.Tolerance);                     // press + 0.5: due for 1.2 s
            Assert.True(t.Items[0].DueAt(options) + 10 >= 108.3 + 0.5 - Kit.Tolerance);
        }

        // 8.8 s is out of reach (10 − 1.5 = 8.5): mitigation keeps −3 s, the invuln −2 s, and neither waits.
        Assert.Equal(-3, Charge("p1.beyond", null, TenSecondMit).AutomaticOffset, Kit.Tolerance);
        Assert.Equal(-2, Charge("p1.beyond", null, Kit.HallowedGround).AutomaticOffset, Kit.Tolerance);
        Assert.Null(Charge("p1.beyond", null, Kit.HallowedGround).Items[0].DueNotBefore);

        // The reach leaves the latest use before the hit: 1 s of it puts 8.3 s out of reach of 10 s.
        Assert.Equal(-3, Charge("p1.charge", new ReminderOptions { MitigationLatestBeforeHit = 1 }, TenSecondMit).AutomaticOffset, Kit.Tolerance);
    }

    [Fact]
    public void Coverage_ALaterHitIsReachedOnlyWhenTheNowCueStillLeavesASecond()
    {
        // Pressed 0.5 s before the first hit, the call is due until the hit itself. 8.5 s after it: due from
        // 508.5 − 10 + 0.5 = 499, a second before the hit. 8.6 s after it (within 10 − 1.3 s): due from 499.1 it would leave
        // 0.9 s, so that hit is out of reach, and the calls keep their own leads without waiting.
        var options = new ReminderOptions();
        foreach (var action in new[] { TenSecondMit, Kit.HallowedGround })
        {
            var edge = Charge("p1.edge", null, action).Items[0];
            Assert.Equal(499.5, edge.PressAt, Kit.Tolerance);
            Assert.Equal(499, edge.DueAt(options), Kit.Tolerance);
            Assert.Equal(1, edge.Latest - edge.DueAt(options), Kit.Tolerance);

            var tooShort = Charge("p1.short", null, action);
            Assert.Equal(TimingRules.DefaultOffset(action, 90), tooShort.AutomaticOffset, Kit.Tolerance);
            Assert.Null(tooShort.Items[0].DueNotBefore);
        }
    }

    /// <summary>
    /// Real cells: TEA's Wormhole, whose #5 Alpha Sword comes 8.6 s after the Super Jump (650.9), DMU's first Maddening
    /// Orchestra (1431.9, 1435.1, 1438.6: 6.7 s, for 8 s calls), and TOP's Omega (927.8 … 945.8, 946.4: 18.0 s and 18.6 s,
    /// for 20 s calls). The calls reach what they can with their NOW cue at least a second before the first hit.
    /// </summary>
    [Theory]
    [InlineData("tea", "p3.wormhole", "Reprisal", -2.2)]                    // covers 656.7, not 659.5
    [InlineData("tea", "p3.wormhole", "Plenary Indulgence", -2.2)]
    [InlineData("tea", "p3.wormhole", "Feint", -2.2)]
    [InlineData("tea", "p3.wormhole", "Holmgang", -2.0)]                    // an invuln's default, later than −2.2 s
    [InlineData("dmu", "p5.maddening_orchestra_1", "Exaltation", -2.4)]     // its default covers 1435.1, not 1438.6
    [InlineData("dmu", "p5.maddening_orchestra_1", "Aquaveil", -2.4)]
    [InlineData("dmu", "p5.maddening_orchestra_1", "Heart of Corundum", -2.4)]
    [InlineData("top", "p5.omega_all", "Temperance", -0.5)]                 // covers 945.8, not 946.4: due 1.5 s before
    [InlineData("top", "p5.omega_all", "Fey Illumination", -0.5)]
    [InlineData("top", "p5.omega_all", "Expedient", -0.5)]
    [InlineData("top", "p5.omega_all", "Holos", -0.5)]
    [InlineData("top", "p5.omega_all", "Rampart", -0.5)]
    public void Coverage_RealCells_TheNowCueComesASecondBeforeTheFirstHit(string fightKey, string mechanic, string action, double offset)
    {
        var fight = fightKey switch
        {
            "top" => PlanImports.Top,
            "tea" => PlanImports.Tea,
            _ => FightLoader.Load(RepoFiles.Data.ReadText($"fights/{fightKey}.yaml"), $"fights/{fightKey}.yaml"),
        };
        var t = TimingRules.ForEntry(Kit.Entry(mechanic, null, Kit.Do(RepoFiles.Action(action))), fight.GetMechanic(mechanic)!, fight, RepoFiles.Actions);
        Assert.Equal(offset, t.AutomaticOffset, 6);
        var options = new ReminderOptions();
        Assert.True(t.Items[0].Latest - t.Items[0].DueAt(options) >= 1 - 1e-9, $"due from {t.Items[0].DueAt(options)} to {t.Items[0].Latest}");
    }

    [Fact]
    public void Coverage_AnItemThatReachesALaterHitOnlyPressedLate_IsPressedAlone_OthersKeepTheirPress()
    {
        // "Rampart + Reprisal" on 100/102/108.3: the 10 s Reprisal reaches 108.3 only pressed at −0.5 s and waits until
        // 98.8; the 20 s Rampart keeps the entry's −6 s (Rampart's own, and what the Reprisal needs for 102), from which it
        // reaches 108.3 anyway, and turns due by the due lead.
        var options = new ReminderOptions();
        var t = Charge("p1.charge", null, Kit.Rampart, TenSecondMit);
        Assert.Equal(94, t.PressAt, Kit.Tolerance);                                 // the first item's
        Assert.Equal(-6, t.AutomaticOffset, Kit.Tolerance);
        Assert.Equal((94.0, 99.5), (t.Items[0].PressAt, t.Items[1].PressAt));
        Assert.Equal(92.5, t.Items[0].DueAt(options), Kit.Tolerance);
        Assert.Equal(98.8, t.Items[1].DueAt(options), Kit.Tolerance);
        Assert.Equal(88.8, t.Items[0].Earliest, Kit.Tolerance);                      // 108.3 − 20 + 0.5
        Assert.Equal(98.8, t.Items[1].Earliest, Kit.Tolerance);

        // The scheduler: the Rampart is due first; a Reprisal on its NOW cue is on time, one before it is early (it would
        // end before 108.3 + 0.5).
        var fight = WildChargeFight();
        var s = new ReminderScheduler(fight, [Kit.Entry("p1.charge", null, Kit.Do(Kit.Rampart), Kit.Do(TenSecondMit))], ChargeCatalog, options);
        var rampart = Assert.Single(s.All, r => r.ItemIndex == 0);
        var reprisal = Assert.Single(s.All, r => r.ItemIndex == 1);
        s.Update(92.5);
        Assert.Equal((ReminderState.Due, ReminderState.Upcoming), (rampart.State, reprisal.State));
        s.OnOwnAction(Kit.Rampart.Id, 93.0);
        Assert.Equal((ReminderState.Done, false), (rampart.State, rampart.UsedEarly));
        s.Update(98.8);
        Assert.Equal(ReminderState.Due, reprisal.State);
        s.OnOwnAction(TenSecondMit.Id, 98.8);
        Assert.Equal(ReminderState.Done, reprisal.State);
        Assert.False(reprisal.UsedEarly);

        s = new ReminderScheduler(fight, [Kit.Entry("p1.charge", null, Kit.Do(TenSecondMit))], ChargeCatalog, options);
        s.OnOwnAction(TenSecondMit.Id, 98.0);
        Assert.True(Assert.Single(s.All).UsedEarly);
    }

    [Fact]
    public void Coverage_TheCallsOfACellThatWaitForALaterHitAreOnlyThose()
    {
        // A tank's "Rampart + Benison + Reprisal + Hallowed Ground" on 100/102/108.3 (like a DRK's Rampart, The Blackest
        // Night, Oblation and Dark Mind on TOP's Wave Cannon): the 10 s calls wait for 108.3 on their own, the Rampart and
        // the shield are pressed and due before them, so that the four presses do not all come in the last 2 s.
        var options = new ReminderOptions();
        var t = Charge("p1.charge", null, Kit.Rampart, Kit.DivineBenison, TenSecondMit, Kit.HallowedGround);
        Assert.Equal([94.0, 94.0, 99.5, 99.5], t.Items.Select(i => i.PressAt));
        Assert.Equal([92.5, 92.5, 98.8, 98.8], t.Items.Select(i => i.DueAt(options)));
        Assert.Equal(94, t.PressAt, Kit.Tolerance);

        // Calls that all wait are pressed together, and the entry with them.
        var late = Charge("p1.charge", null, TenSecondMit, Kit.HallowedGround);
        Assert.Equal(-0.5, late.AutomaticOffset, Kit.Tolerance);
        Assert.All(late.Items, i => Assert.Equal(99.5, i.PressAt, Kit.Tolerance));

        // Passage of Arms, pressed last, comes with the Reprisal, after the shield.
        var paladin = Charge("p1.charge", null, Kit.DivineBenison, TenSecondMit, PassageOfArms);
        Assert.Equal([96.0, 99.5, 99.5], paladin.Items.Select(i => i.PressAt));
        Assert.Equal([94.5, 98.8, 98.8], paladin.Items.Select(i => i.DueAt(options)));

        // A written press is every item's, and holds no NOW cue.
        var fight = WildChargeFight();
        var written = TimingRules.ForEntry(Kit.Entry("p1.charge", -3, Kit.Do(Kit.Rampart), Kit.Do(TenSecondMit)), fight.GetMechanic("p1.charge")!, fight, ChargeCatalog);
        Assert.All(written.Items, i => Assert.Equal(97, i.PressAt, Kit.Tolerance));
        Assert.All(written.Items, i => Assert.Null(i.DueNotBefore));
        Assert.Equal(-6, written.AutomaticOffset, Kit.Tolerance);                   // what the review offers as automatic
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(3.0)]
    [InlineData(0.5)]
    public void Coverage_PassageOfArms_IsNotDueBeforeTheItemsPressedBeforeIt(double dueLead)
    {
        // "Reprisal + Passage of Arms" on 100/102/108.3, either way round: the Reprisal's NOW cue waits until 98.8 to reach
        // 108.3; any action ends the channel, so Passage of Arms (pressed last) turns due with it, not when its own NOW cue
        // (press − due lead) would come, and a use before then is early.
        var options = new ReminderOptions { DueLead = dueLead };
        foreach (var items in new[] { new[] { TenSecondMit, PassageOfArms }, new[] { PassageOfArms, TenSecondMit } })
        {
            var t = Charge("p1.charge", null, items);
            var reprisal = t.Items[Array.IndexOf(items, TenSecondMit)];
            var passage = t.Items[Array.IndexOf(items, PassageOfArms)];
            Assert.Equal(99.5, reprisal.PressAt, Kit.Tolerance);
            Assert.Equal(reprisal.PressAt, passage.PressAt, Kit.Tolerance);
            Assert.Equal(Math.Max(99.5 - dueLead, 98.8), reprisal.DueAt(options), Kit.Tolerance);
            Assert.Equal(reprisal.DueAt(options), passage.DueAt(options), Kit.Tolerance);
            Assert.Equal(98.8, passage.Earliest, Kit.Tolerance);
        }

        // The scheduler turns both due at once and lists the Reprisal first, whichever the cell lists first, so that a
        // player who presses down the list keeps the channel.
        foreach (var items in new[] { new[] { TenSecondMit, PassageOfArms }, new[] { PassageOfArms, TenSecondMit } })
        {
            var s = new ReminderScheduler(WildChargeFight(), [Kit.Entry("p1.charge", null, [.. items.Select(Kit.Do)])], ChargeCatalog, options);
            Assert.Equal(["Reprisal", "Passage of Arms"], s.All.Select(r => r.Item.Name));
            var due = Kit.RecordDue(s);
            s.Update(98.75);
            Assert.Empty(due);
            s.Update(Math.Max(99.5 - dueLead, 98.8));
            Assert.Equal(["Reprisal", "Passage of Arms"], due.Select(r => r.Item.Name));
        }
    }

    [Fact]
    public void PassageOfArms_IsListedAfterTheOtherItemsOfItsEntry_AlsoWithAWrittenPress()
    {
        // A single hit: "Passage of Arms + Divine Benison" is timed by Passage of Arms' −2 s (it comes first), a
        // written "Passage of Arms + Rampart" at −1 s presses both at once; either way Passage of Arms is listed after
        // the other item, while entries keep the plan's order between them.
        var fight = Kit.Fight(90, Kit.Mech("p1.raidwide", 100, 100), Kit.Mech("p1.buster", 200, 200));
        var entries = new[]
        {
            Kit.Entry("p1.raidwide", null, Kit.Do(PassageOfArms), Kit.Do(Kit.DivineBenison)),
            Kit.Entry("p1.raidwide", -2, Kit.Do(Kit.Rampart)),
            Kit.Entry("p1.buster", -1, Kit.Do(PassageOfArms), Kit.Do(Kit.Rampart)),
        };
        var s = new ReminderScheduler(fight, entries, ChargeCatalog, new ReminderOptions());
        Assert.Equal(
            [(98.0, "Divine Benison"), (98.0, "Passage of Arms"), (98.0, "Rampart"), (199.0, "Rampart"), (199.0, "Passage of Arms")],
            s.All.Select(r => (Math.Round(r.PressAt, 3), r.Item.Name)));
    }

    /// <summary>
    /// Real cells: "Reprisal + Passage of Arms" either way round on TOP's Wave Cannon 1 and 2 (the Wild Charge 8.3 s after
    /// the first protean) and Cosmo Meteor (the last spreads 8.1 s after the first hit), and on TEA's Inception (the third
    /// Alpha Sword 8.4 s after the first hit): the Reprisal is pressed 0.5 s before the first hit, with its NOW cue held
    /// for the later hit; Passage of Arms comes with it, turns due with it and is listed after it.
    /// </summary>
    [Theory]
    [InlineData("top", "p6.wave_cannon_1")]
    [InlineData("top", "p6.wave_cannon_2")]
    [InlineData("top", "p6.cosmo_meteor")]
    [InlineData("tea", "p3.inception")]
    public void Coverage_RealCells_PassageOfArmsTurnsDueWithTheReprisal(string fightKey, string mechanic)
    {
        var fight = fightKey == "top" ? PlanImports.Top : PlanImports.Tea;
        var options = new ReminderOptions();
        foreach (var cell in new[] { new[] { "Reprisal", "Passage of Arms" }, new[] { "Passage of Arms", "Reprisal" } })
        {
            var entry = Kit.Entry(mechanic, null, [.. cell.Select(a => Kit.Do(RepoFiles.Action(a)))]);
            var s = new ReminderScheduler(fight, [entry], RepoFiles.Actions, options);
            Assert.Equal(["Reprisal", "Passage of Arms"], s.All.Select(r => r.Item.Name));
            var (reprisal, passage) = (s.All[0], s.All[1]);
            Assert.Equal(reprisal.Hit - 0.5, reprisal.PressAt, 6);
            Assert.Equal(reprisal.PressAt, passage.PressAt, 6);
            var due = TimingRules.DueAt(reprisal.PressAt, reprisal.DueNotBefore, options);
            Assert.True(due > reprisal.PressAt - options.DueLead, $"{mechanic}: the Reprisal's NOW cue ({due}) is not held");
            Assert.Equal(due, TimingRules.DueAt(passage.PressAt, passage.DueNotBefore, options), 6);
        }
    }

    [Fact]
    public void Coverage_TheNowCueNeverComesBeforeTheEarliestUseThatReachesTheLaterHit_WhateverTheDueLead()
    {
        // Optical Sight's 6.1 s: a 10 s Reprisal at −1.9 s is due 1.5 s before by default (96.6, which still reaches 106.1);
        // with a 3 s due lead it still turns due at 96.6, not at 95.1.
        var fight = MultiHitFight();
        var entry = Kit.Entry("p1.two", null, Kit.Do(TenSecondMit));
        var t = Multi(entry);
        Assert.Equal(96.6, t.Items[0].DueAt(new ReminderOptions()), Kit.Tolerance);
        Assert.Equal(96.6, t.Items[0].DueAt(new ReminderOptions { DueLead = 3 }), Kit.Tolerance);
        Assert.Equal(97.6, t.Items[0].DueAt(new ReminderOptions { DueLead = 0.5 }), Kit.Tolerance);  // a shorter lead is kept

        var s = new ReminderScheduler(fight, [entry], new FakeCatalog([TenSecondMit]), new ReminderOptions { DueLead = 3 });
        s.Update(95.2);
        Assert.Equal(ReminderState.Upcoming, Assert.Single(s.All).State);
        s.Update(96.6);
        Assert.Equal(ReminderState.Due, Assert.Single(s.All).State);
    }

    /// <summary>
    /// A first hit at 100 with sub hits (damage times that are no timeline entries) 2.1, 4.1, 6.2 and 8.3 s later and a hit
    /// 8.8 s later (TOP's Diffuse Wave Cannon Kyrios), and a hit at 200 with five sub hits 1.1 s apart (TEA's first
    /// Splash) (sync 90).
    /// </summary>
    private static FightDefinition SubHitFight() => Kit.Fight(
        90,
        new Mechanic { Id = "p1.cleaves", Name = "cleaves", Phase = "p1", Hit = 100, Hits = [100, 108.8], SubHits = [102.1, 104.1, 106.2, 108.3] },
        new Mechanic { Id = "p1.splash", Name = "splash", Phase = "p1", Hit = 200, Hits = [200], SubHits = [201.1, 202.2, 203.3, 204.4, 205.5] });

    [Fact]
    public void Coverage_SubHitsAreLaterHitsToo_WithoutAHitIndex()
    {
        // The fifth cleave (108.3) is 8.3 s after the first, within 10 − 1.5 s: the invuln is pressed 0.5 s before the first
        // and turns due at 108.3 − 10 + 0.5, so that a press on its NOW cue still lasts 0.5 s past it; 108.8 is out of reach.
        var options = new ReminderOptions();
        var fight = SubHitFight();
        EntryTiming Timing(PlanEntry entry) => TimingRules.ForEntry(entry, fight.GetMechanic(entry.Mechanic)!, fight, ChargeCatalog);
        var invuln = Timing(Kit.Entry("p1.cleaves", null, Kit.Do(Kit.HallowedGround)));
        Assert.Equal(-0.5, invuln.AutomaticOffset, Kit.Tolerance);
        Assert.Equal(98.8, invuln.Items[0].DueAt(options), Kit.Tolerance);
        Assert.Equal(98.8, invuln.Items[0].Earliest, Kit.Tolerance);

        // The sixth splash 5.5 s after the first: a 10 s call at 205.5 − 10 + 2, early before 205.5 − 10 + 0.5.
        var splash = Timing(Kit.Entry("p1.splash", null, Kit.Do(TenSecondMit)));
        Assert.Equal(-2.5, splash.AutomaticOffset, Kit.Tolerance);
        Assert.Equal(196, splash.Items[0].Earliest, Kit.Tolerance);

        // Sub hits have no index: hit index 1 is the second hit (108.8).
        Assert.Equal(108.8, Timing(Kit.Entry("p1.cleaves", -2, Kit.Do(Kit.HallowedGround)).AtHit(1)).Hit, Kit.Tolerance);

        // Without them only the hits count: the invuln keeps its own −2 s.
        var bare = Kit.Fight(90, new Mechanic { Id = "p1.cleaves", Name = "cleaves", Phase = "p1", Hit = 100, Hits = [100, 108.8] });
        Assert.Equal(-2, TimingRules.ForEntry(Kit.Entry("p1.cleaves", null, Kit.Do(Kit.HallowedGround)), bare.GetMechanic("p1.cleaves")!, bare, ChargeCatalog).AutomaticOffset, Kit.Tolerance);
    }

    /// <summary>Sacred Soil: 15 s party mitigation (−4.5 s).</summary>
    private static readonly ActionInfo Soil = Kit.Act(188, "Sacred Soil", ActionKind.PartyMit, duration: 15, jobs: [Job.SCH]);

    /// <summary>Eukrasia: a GCD without a cooldown of its own (the game locks it for 1 s), which Eukrasian Prognosis needs first.</summary>
    private static readonly ActionInfo Eukrasia = new() { Id = 24290, Name = "Eukrasia", Kind = ActionKind.Other, Jobs = [Job.SGE], IsGcd = true, Recast = 1 };

    /// <summary>Eukrasian Prognosis: an instant shield (−4 s), usable after Eukrasia.</summary>
    private static readonly ActionInfo EukrasianPrognosis = new()
    {
        Id = 24292,
        Name = "Eukrasian Prognosis",
        Kind = ActionKind.Shield,
        Jobs = [Job.SGE],
        IsGcd = true,
        DurationByLevel = new Dictionary<int, double> { [1] = 30 },
        FollowUpOf = Eukrasia.Id,
    };

    /// <summary>
    /// Hits 6.1 s apart at 100, 12 s apart at 300, 8.4 s apart at 500, 9.5 s apart at 700, 11 s apart at 900 and 11.5 s apart
    /// at 1100 (sync 90).
    /// </summary>
    private static EntryTiming Limited(string mechanic, params ActionInfo[] actions)
    {
        var fight = Kit.Fight(
            90,
            Kit.Mech("p1.two", 100, 100, 106.1),
            Kit.Mech("p1.far", 300, 300, 312),
            Kit.Mech("p1.near", 500, 500, 508.4),
            Kit.Mech("p1.mid", 700, 700, 709.5),
            Kit.Mech("p1.late", 900, 900, 911),
            Kit.Mech("p1.later", 1100, 1100, 1111.5));
        var catalog = new FakeCatalog([.. Kit.Catalogued, TenSecondMit, Soil, Eukrasia, EukrasianPrognosis]);
        return TimingRules.ForEntry(Kit.Entry(mechanic, null, [.. actions.Select(Kit.Do)]), fight.GetMechanic(mechanic)!, fight, catalog);
    }

    [Fact]
    public void Coverage_NeverPressesLaterThanTheEntrysCastsAndLeadsAllow()
    {
        Assert.Equal(-1, Limited("p1.far", Soil).AutomaticOffset, Kit.Tolerance);                        // alone: 312 − 15 + 2 − 300

        // "Adloquium + Soil", either way round: at −1 s the 2 s cast would end after the first hit, so the 12 s later hit
        // is not covered and the cast ends 1 s or more before the first hit; every item's NOW cue is on time.
        foreach (var t in new[] { Limited("p1.far", Kit.Adloquium, Soil), Limited("p1.far", Soil, Kit.Adloquium) })
        {
            Assert.Equal(-4.5, t.AutomaticOffset, Kit.Tolerance);
            Assert.True(t.PressAt + Kit.Adloquium.CastTime <= t.Hit - 1 + Kit.Tolerance);
            Assert.All(t.Items, i => Assert.True(i.Earliest <= i.DueAt(new ReminderOptions()) + Kit.Tolerance));
        }

        Assert.Equal(286, Limited("p1.far", Kit.Adloquium, Soil).Items[1].Earliest, Kit.Tolerance);       // Soil's own window: 300 − 15 + 1

        // A later hit within that limit is still covered: 9.5 s away, −3.5 s (709.5 − 15 + 2 − 700) with the cast ending at −1.5 s.
        var mid = Limited("p1.mid", Soil, Kit.Adloquium);
        Assert.Equal(-3.5, mid.AutomaticOffset, Kit.Tolerance);
        Assert.Equal(695, mid.Items[0].Earliest, Kit.Tolerance);                                        // 709.5 − 15 + 0.5
        Assert.Equal(-4.6, Limited("p1.near", Kit.Temperance, Soil, Kit.Adloquium).AutomaticOffset, Kit.Tolerance);

        // A follow-up of a GCD (Eukrasian Prognosis after Eukrasia's 1 s) is in place 1 s before the hit too: −2 s at the
        // latest, so the Soil covers a hit 11 s later (911 − 15 + 2 − 900), not one 11.5 s later.
        Assert.Equal(-4.5, Limited("p1.far", Soil, EukrasianPrognosis).AutomaticOffset, Kit.Tolerance);
        Assert.Equal(-3.5, Limited("p1.mid", Soil, EukrasianPrognosis).AutomaticOffset, Kit.Tolerance);
        Assert.Equal(-2, Limited("p1.late", Soil, EukrasianPrognosis).AutomaticOffset, Kit.Tolerance);
        Assert.Equal(-4.5, Limited("p1.later", Soil, EukrasianPrognosis).AutomaticOffset, Kit.Tolerance);
        Assert.Equal(-1.5, Limited("p1.later", Soil).AutomaticOffset, Kit.Tolerance);                        // alone it would

        // An item with a lead of its own is not pressed later for a later hit: Earthly Star matures (beside the 10 s
        // mitigation as late as that still lasts through the first hit, −7.5 s), Neutral Sect leaves time for its Helios.
        Assert.Equal(-7.5, Limited("p1.two", Kit.EarthlyStar, TenSecondMit).AutomaticOffset, Kit.Tolerance);
        Assert.Equal(-4.5, Limited("p1.two", Kit.NeutralSect, TenSecondMit).AutomaticOffset, Kit.Tolerance);
        Assert.Equal(-4.5, Limited("p1.two", TenSecondMit, Kit.NeutralSect).AutomaticOffset, Kit.Tolerance);

        // An instant shield does not hold coverage back, nor does a heal used after the hit.
        Assert.Equal(-1.9, Limited("p1.two", TenSecondMit, Kit.DivineBenison).AutomaticOffset, Kit.Tolerance);
        Assert.Equal(-1.9, Limited("p1.two", TenSecondMit, Kit.Tetragrammaton).AutomaticOffset, Kit.Tolerance);
    }

    /// <summary>
    /// Real cells: "Sacred Soil + Succor" on TOP's Pantokrator (line stacks at 81.3, 87.3, 93.3 and 99.3), either way round,
    /// and "Collective Unconscious + Aspected Helios" on TEA's Fate Calibration β (1081.5, 1082.5, 1088.6): the cast still
    /// ends before the first hit.
    /// </summary>
    [Theory]
    [InlineData("top", "p1.pantokrator", -4.5, "Sacred Soil", "Succor")]
    [InlineData("top", "p1.pantokrator", -4.5, "Succor", "Sacred Soil")]
    [InlineData("tea", "p4.fate_beta", -3.0, "Collective Unconscious", "Aspected Helios")]
    public void Coverage_RealCells_TheCastEndsBeforeTheFirstHit(string fightKey, string mechanic, double offset, params string[] actions)
    {
        var fight = fightKey == "top" ? PlanImports.Top : PlanImports.Tea;
        var entry = Kit.Entry(mechanic, null, [.. actions.Select(a => Kit.Do(RepoFiles.Action(a)))]);
        var t = TimingRules.ForEntry(entry, fight.GetMechanic(mechanic)!, fight, RepoFiles.Actions);
        Assert.Equal(offset, t.AutomaticOffset, Kit.Tolerance);
        var cast = actions.Max(a => RepoFiles.Action(a).CastTime);
        Assert.True(t.PressAt + cast <= t.Hit - 1 + Kit.Tolerance);
    }

    /// <summary>
    /// Real cells: "Earthly Star + CU" on TOP's Latent Defect 2 (485.4), and on mechanics of several hits, where a
    /// coverage push used to hide the star's −10 s (TOP's Wave Cannon 1 and Pantokrator, DMU's Ultima Blaster), either
    /// way round. Collective Unconscious lasts 10 s: at the star's −10 s it would end at the hit, so both are pressed at
    /// −7.5 s, and CU's NOW cue still lasts through the hit.
    /// </summary>
    [Theory]
    [InlineData("top", "p3.latent_2", "Earthly Star", "Collective Unconscious")]
    [InlineData("top", "p3.latent_2", "Collective Unconscious", "Earthly Star")]
    [InlineData("top", "p6.wave_cannon_1", "Earthly Star", "Collective Unconscious")]
    [InlineData("top", "p6.wave_cannon_1", "Collective Unconscious", "Earthly Star")]
    [InlineData("top", "p1.pantokrator", "Earthly Star", "Collective Unconscious")]
    [InlineData("top", "p1.pantokrator", "Collective Unconscious", "Earthly Star")]
    [InlineData("dmu", "p3.ultima_blaster", "Earthly Star", "Collective Unconscious")]
    [InlineData("dmu", "p3.ultima_blaster", "Collective Unconscious", "Earthly Star")]
    public void OwnLead_RealCells_EarthlyStarBesideCu(string fightKey, string mechanic, params string[] actions)
    {
        var fight = fightKey == "top" ? PlanImports.Top : FightLoader.Load(RepoFiles.Data.ReadText($"fights/{fightKey}.yaml"), $"fights/{fightKey}.yaml");
        var entry = Kit.Entry(mechanic, null, [.. actions.Select(a => Kit.Do(RepoFiles.Action(a)))]);
        var t = TimingRules.ForEntry(entry, fight.GetMechanic(mechanic)!, fight, RepoFiles.Actions);
        Assert.Equal(-7.5, t.AutomaticOffset, Kit.Tolerance);
        var options = new ReminderOptions();
        Assert.All(t.Items, i => Assert.Equal(t.PressAt, i.PressAt, Kit.Tolerance));
        Assert.All(t.Items, i => Assert.True(i.Earliest <= i.DueAt(options) + Kit.Tolerance, $"{i.Action!.Name}: due {i.DueAt(options)}, earliest {i.Earliest}"));
        var cu = t.Items[Array.IndexOf(actions, "Collective Unconscious")];
        Assert.True(cu.DueAt(options) + 10 >= t.Hit + 1 - Kit.Tolerance);
    }

    // ---- series (a raidwide repeated a few seconds apart) ------------------------------------------------------

    /// <summary>
    /// Waves 3 s apart at 100, 103, 106 and 109 (a series), wave 1 and wave 3 as mechanics of their own, and a mechanic at
    /// 103.5 that is not a wave (sync 90).
    /// </summary>
    private static FightDefinition SeriesFight() => Kit.Fight(
        90,
        new Mechanic { Id = "p1.waves", Name = "waves", Phase = "p1", Hit = 100, Hits = [100, 103, 106, 109], Series = true },
        Kit.Mech("p1.wave_1", 100),
        Kit.Mech("p1.wave_3", 106),
        Kit.Mech("p1.other", 103.5));

    [Theory]
    [InlineData(16536u, 103.3)]   // Temperance: −6 would be 100, before wave 2 (103): after it + 0.3
    [InlineData(7432u, 103.3)]    // Divine Benison: an instant shield at −4 would be spent on wave 2
    [InlineData(185u, 101.5)]     // Adloquium: its 2 s cast from −4.5 lands at 103.5, after wave 2: kept
    [InlineData(16559u, 101.8)]   // Neutral Sect: its −4.5 s lead (101.5) would put the Helios barrier up before wave 2
    [InlineData(30u, 104.0)]      // Hallowed Ground: −2 is after wave 2 anyway
    [InlineData(3570u, 107.0)]    // a heal after the hit
    [InlineData(999999u, 103.3)]  // an unknown action, like a note
    public void Series_ALaterHitIsPressedAfterTheHitBeforeIt(uint actionId, double press)
    {
        var t = Kit.Timing(Kit.Entry("p1.wave_3", null, PlanItem.ForAction(actionId, "x")), SeriesFight());
        Assert.Equal(press, t.PressAt, Kit.Tolerance);
        Assert.Equal(press - 106, t.AutomaticOffset, Kit.Tolerance);
    }

    [Fact]
    public void Series_TheReminderIsNotDueBeforeTheHitBeforeIt_NorUsedOnTimeBeforeIt()
    {
        var fight = SeriesFight();
        var shield = Kit.Timing(Kit.Entry("p1.wave_3", null, Kit.Do(Kit.DivineBenison)), fight);
        Assert.Equal(103.3, shield.DueNotBefore!.Value, Kit.Tolerance);
        Assert.Equal(103.3, shield.Items[0].Earliest, Kit.Tolerance);   // a Benison before wave 2 is spent on it: early

        var cast = Kit.Timing(Kit.Entry("p1.wave_3", null, Kit.Do(Kit.Adloquium)), fight);
        Assert.Equal(101.3, cast.DueNotBefore!.Value, Kit.Tolerance);   // the cast lands after wave 2 from 101.3 on
        Assert.Equal(101.3, cast.Items[0].Earliest, Kit.Tolerance);

        var mitigation = Kit.Timing(Kit.Entry("p1.wave_3", null, Kit.Do(Kit.Temperance), Kit.Do(Kit.Adloquium)), fight);
        Assert.Equal(103.3, mitigation.DueNotBefore!.Value, Kit.Tolerance); // timed by Temperance: no cast to start early
        Assert.All(mitigation.Items, i => Assert.Equal(103.3, i.Earliest, Kit.Tolerance));
    }

    [Fact]
    public void Series_AnActionWhoseBarrierComesFromACastAfterIt_IsPressedThatCastBeforeTheFloor()
    {
        // Neutral Sect on wave 3 (106): the barrier comes with the 1.5 s Helios cast after the press, so pressed (and due)
        // at wave 2 (103) + 0.3 − 1.5 it lands just after wave 2, and even after a whole GCD before the cast, before wave 3.
        var t = Kit.Timing(Kit.Entry("p1.wave_3", null, Kit.Do(Kit.NeutralSect)), SeriesFight());
        Assert.Equal(101.8, t.PressAt, Kit.Tolerance);
        Assert.Equal(101.8, t.DueNotBefore!.Value, Kit.Tolerance);
        Assert.Equal(101.8, t.Items[0].Earliest, Kit.Tolerance);
        Assert.True(t.DueAt(new ReminderOptions()) + Kit.NeutralSect.BarrierCast!.Value >= 103 + 0.3 - Kit.Tolerance);
        Assert.True(t.DueAt(new ReminderOptions()) + 2.5 + Kit.NeutralSect.BarrierCast!.Value < 106);

        // On the first wave it keeps its lead.
        Assert.Equal(95.5, Kit.Timing(Kit.Entry("p1.wave_1", null, Kit.Do(Kit.NeutralSect)), SeriesFight()).PressAt, Kit.Tolerance);
    }

    [Fact]
    public void Series_NeutralSectBesideEarthlyStar_KeepsItsBarrierCast()
    {
        // "Earthly Star + Neutral Sect" on wave 3 (106), either way round, is timed by the star (its −10 s; Neutral Sect is
        // a buff): Neutral Sect still leaves its Helios the cast, pressed and due at wave 2 (103) + 0.3 − 1.5, as alone; the
        // star waits for wave 2 + 0.3.
        foreach (var items in new[] { new[] { Kit.EarthlyStar, Kit.NeutralSect }, new[] { Kit.NeutralSect, Kit.EarthlyStar } })
        {
            var t = Kit.Timing(Kit.Entry("p1.wave_3", null, [.. items.Select(Kit.Do)]), SeriesFight());
            var neutral = t.Items[Array.IndexOf(items, Kit.NeutralSect)];
            var star = t.Items[Array.IndexOf(items, Kit.EarthlyStar)];
            Assert.Equal((101.8, 101.8), (neutral.PressAt, neutral.DueAt(new ReminderOptions())));
            Assert.Equal(101.8, neutral.DueNotBefore!.Value, Kit.Tolerance);
            Assert.Equal((103.3, 103.3), (star.PressAt, star.DueNotBefore!.Value));
            Assert.Equal(101.8, t.PressAt, Kit.Tolerance);                       // the first item's
        }

        // A shield's cast does not: beside Temperance, the Adloquium waits for wave 2 + 0.3 (see above).
        var shield = Kit.Timing(Kit.Entry("p1.wave_3", null, Kit.Do(Kit.Temperance), Kit.Do(Kit.Adloquium)), SeriesFight());
        Assert.All(shield.Items, i => Assert.Equal(103.3, i.PressAt, Kit.Tolerance));
    }

    [Fact]
    public void Series_EachItemWaitsForTheFloorOfItsOwnAction()
    {
        // "Adloquium + Divine Benison" on wave 3 (106), timed by the Adloquium: its 2 s cast may start 1.7 s before wave 2
        // (103) + 0.3, the instant Benison may not (it would be spent on wave 2).
        var t = Kit.Timing(Kit.Entry("p1.wave_3", null, Kit.Do(Kit.Adloquium), Kit.Do(Kit.DivineBenison)), SeriesFight());
        Assert.Equal(101.5, t.PressAt, Kit.Tolerance);                     // the entry: the Adloquium's own −4.5 s
        Assert.Equal(101.3, t.DueNotBefore!.Value, Kit.Tolerance);
        Assert.Equal(101.5, t.Items[0].PressAt, Kit.Tolerance);
        Assert.Equal(101.3, t.Items[0].DueNotBefore!.Value, Kit.Tolerance);
        Assert.Equal(101.3, t.Items[0].Earliest, Kit.Tolerance);
        Assert.Equal(103.3, t.Items[1].PressAt, Kit.Tolerance);
        Assert.Equal(103.3, t.Items[1].DueNotBefore!.Value, Kit.Tolerance);
        Assert.Equal(103.3, t.Items[1].Earliest, Kit.Tolerance);
        Assert.Equal(103.3, t.Items[1].DueAt(new ReminderOptions()), Kit.Tolerance);

        // The scheduler turns the Benison due only after wave 2, and a Benison before that is early.
        var s = Kit.Scheduler(SeriesFight(), new ReminderOptions(), Kit.Entry("p1.wave_3", null, Kit.Do(Kit.Adloquium), Kit.Do(Kit.DivineBenison)));
        var adloquium = Assert.Single(s.All, r => r.ItemIndex == 0);
        var benison = Assert.Single(s.All, r => r.ItemIndex == 1);
        s.Update(101.3);
        Assert.Equal((ReminderState.Due, ReminderState.Upcoming), (adloquium.State, benison.State));
        s.OnOwnAction(Kit.Adloquium.Id, 101.4);
        s.OnOwnAction(Kit.DivineBenison.Id, 102.0);
        Assert.False(adloquium.UsedEarly);
        Assert.True(benison.UsedEarly);
    }

    [Theory]
    [InlineData(-4.0, 101.3, 101.3)]  // after the entry's floor (101.3), before the Benison's (103.3): the entry's for both
    [InlineData(-2.5, 101.3, 103.3)]  // after both: each its own
    [InlineData(-5.0, null, null)]    // before the entry's floor: none
    public void Series_AWrittenPressKeepsEachFloorItIsAtOrAfter(double offset, double? adloquium, double? benison)
    {
        var t = Kit.Timing(Kit.Entry("p1.wave_3", offset, Kit.Do(Kit.Adloquium), Kit.Do(Kit.DivineBenison)), SeriesFight());
        Assert.All(t.Items, i => Assert.Equal(106 + offset, i.PressAt, Kit.Tolerance));   // a written press is not moved
        Assert.Equal(adloquium, t.Items[0].DueNotBefore);
        Assert.Equal(benison, t.Items[1].DueNotBefore);
        if (benison is { } floor)
            Assert.Equal(floor, t.Items[1].Earliest, Kit.Tolerance);
    }

    [Fact]
    public void Series_TheSchedulerTurnsTheReminderDueOnlyAfterTheHitBeforeIt()
    {
        // Hallowed Ground on wave 3 (106): pressed at 104 (−2), due from 102.5 by the due lead, but not before 103.3.
        var s = Kit.Scheduler(SeriesFight(), new ReminderOptions(), Kit.Entry("p1.wave_3", null, Kit.Do(Kit.HallowedGround)));
        var due = Kit.RecordDue(s);
        var r = Assert.Single(s.All);
        Assert.Equal((104.0, 103.3), (r.PressAt, r.DueNotBefore!.Value));

        s.Update(103.2);
        Assert.Equal(ReminderState.Upcoming, r.State);
        s.Update(103.3);
        Assert.Equal(ReminderState.Due, r.State);
        Assert.Equal([r], due);

        // A rewind to before 103.3 makes it wait again; a press on "NOW" is on time.
        s.OnTimelineJump(109.0, 103.0);
        s.Update(103.0);
        Assert.Equal(ReminderState.Upcoming, r.State);
        s.Update(103.4);
        s.OnOwnAction(Kit.HallowedGround.Id, 103.4);
        Assert.Equal(ReminderState.Done, r.State);
        Assert.False(r.UsedEarly);
    }

    [Fact]
    public void DueAt_IsWhenTheEntrysRemindersTurnDue()
    {
        var fight = SeriesFight();
        var options = new ReminderOptions();
        Assert.Equal(92.5, Kit.Timing(Kit.Entry("p1.wave_1", null, Kit.Do(Kit.Temperance)), fight).DueAt(options), Kit.Tolerance);       // 94 − 1.5
        Assert.Equal(103.3, Kit.Timing(Kit.Entry("p1.wave_3", null, Kit.Do(Kit.HallowedGround)), fight).DueAt(options), Kit.Tolerance); // not before the floor

        // The scheduler turns the reminder due then.
        var s = Kit.Scheduler(fight, options, Kit.Entry("p1.wave_3", null, Kit.Do(Kit.HallowedGround)));
        s.Update(103.29);
        Assert.Equal(ReminderState.Upcoming, Assert.Single(s.All).State);
        s.Update(103.3);
        Assert.Equal(ReminderState.Due, Assert.Single(s.All).State);
    }

    [Fact]
    public void Series_OnTheSeriesItself_ByTheHitIndex()
    {
        var fight = SeriesFight();
        Assert.Equal(103.3, Kit.Timing(Kit.Entry("p1.waves", null, Kit.Do(Kit.Temperance)).AtHit(2), fight).PressAt, Kit.Tolerance);
        Assert.Equal(100.3, Kit.Timing(Kit.Entry("p1.waves", null, Kit.Do(Kit.Temperance)).AtHit(1), fight).PressAt, Kit.Tolerance);
    }

    [Fact]
    public void Series_TheFirstHit_OtherMechanics_AndAWrittenTime_AreNotHeldBack()
    {
        var fight = SeriesFight();
        var first = Kit.Timing(Kit.Entry("p1.wave_1", null, Kit.Do(Kit.Temperance)), fight);
        Assert.Equal((94.0, (double?)null), (first.PressAt, first.DueNotBefore));
        Assert.Equal(94, Kit.Timing(Kit.Entry("p1.waves", null, Kit.Do(Kit.Temperance)), fight).PressAt, Kit.Tolerance);
        Assert.Equal(97.5, Kit.Timing(Kit.Entry("p1.other", null, Kit.Do(Kit.Temperance)), fight).PressAt, Kit.Tolerance);

        var timed = Kit.Timing(Kit.Entry("p1.wave_3", -6, Kit.Do(Kit.Temperance)), fight);
        Assert.Equal(100, timed.PressAt, Kit.Tolerance);
        Assert.Null(timed.DueNotBefore);
        Assert.Equal(87, timed.Items[0].Earliest, Kit.Tolerance);
        Assert.Equal(-2.7, timed.AutomaticOffset, Kit.Tolerance);      // what the review offers as automatic
    }

    [Theory]
    [InlineData(-2.7, 103.3)] // at the floor (wave 2 at 103 + 0.3)
    [InlineData(-2.5, 103.5)] // after it: due by the due lead at 102, but not before the floor
    [InlineData(-1.0, 105.0)]
    public void Series_AWrittenPressAtOrAfterTheFloor_KeepsIt(double offset, double press)
    {
        // A press the importer moved later on wave 3 (106), so that the action is ready again: its reminder still does not
        // turn due before wave 2 (103) + 0.3, and a use before that is early, as for the automatic press.
        var fight = SeriesFight();
        var t = Kit.Timing(Kit.Entry("p1.wave_3", offset, Kit.Do(Kit.Temperance)), fight);
        Assert.Equal(press, t.PressAt, Kit.Tolerance);
        Assert.Equal(103.3, t.DueNotBefore!.Value, Kit.Tolerance);
        Assert.Equal(Math.Max(press - new ReminderOptions().DueLead, 103.3), t.DueAt(new ReminderOptions()), Kit.Tolerance);
        Assert.Equal(103.3, t.Items[0].Earliest, Kit.Tolerance);       // a 20 s Temperance from 87 on would be spent on wave 2

        var s = Kit.Scheduler(fight, new ReminderOptions(), Kit.Entry("p1.wave_3", offset, Kit.Do(Kit.Temperance)));
        var r = Assert.Single(s.All);
        s.Update(103.29);
        Assert.Equal(ReminderState.Upcoming, r.State);
        s.Update(Math.Max(press - new ReminderOptions().DueLead, 103.3));
        Assert.Equal(ReminderState.Due, r.State);
    }

    // ---- EarliestPreHitOffset ------------------------------------------------------------------

    [Fact]
    public void EarliestPreHitOffset_IsTheEarliestDefaultBeforeTheHit()
    {
        Assert.Equal(-6, TimingRules.EarliestPreHitOffset([Kit.Tetragrammaton, Kit.DivineBenison, null, Kit.Temperance], 90)!.Value, Kit.Tolerance);
        Assert.Equal(-8, TimingRules.EarliestPreHitOffset([Kit.Excogitation, Kit.Swiftcast], 90)!.Value, Kit.Tolerance); // pre-placed heals count
        Assert.Null(TimingRules.EarliestPreHitOffset([Kit.Tetragrammaton, Kit.PulseOfLife, null], 90));                     // heals after the hit do not
        Assert.Null(TimingRules.EarliestPreHitOffset(Array.Empty<ActionInfo?>(), 90));
    }
}
