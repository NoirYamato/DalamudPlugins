using MitCue.Core.Actions;
using MitCue.Core.Fights;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;

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
            Assert.Equal(t.Items[r.ItemIndex].Earliest, r.Earliest, Kit.Tolerance);
            Assert.Equal(t.Items[r.ItemIndex].Latest, r.Latest, Kit.Tolerance);
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

    private static EntryTiming Several(params ActionInfo[] actions)
    {
        var fight = Kit.TestFight();
        return TimingRules.ForEntry(Kit.Entry("p1.raidwide", null, [.. actions.Select(Kit.Do)]), fight.GetMechanic("p1.raidwide")!, fight, new FakeCatalog([.. Kit.Catalogued, TenSecondMit]));
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
    public void Several_PressAfterAndHealLeadComeFirst()
    {
        var afterFight = PressAfterFight();
        Assert.Equal(1, Kit.Timing(Kit.Entry("p1.drop", null, Kit.Do(Kit.Tetragrammaton), Kit.Do(Kit.Temperance)), afterFight).AutomaticOffset, Kit.Tolerance);
        Assert.Equal(1, Kit.Timing(Kit.Entry("p1.drop", null, Kit.Do(Kit.Swiftcast), Kit.Do(Kit.Adloquium)), afterFight).AutomaticOffset, Kit.Tolerance);

        var leadFight = HealLeadFight();
        Assert.Equal(-4, Kit.Timing(Kit.Entry("p1.buster", null, Kit.Do(Kit.Tetragrammaton), Kit.Do(Kit.Temperance)), leadFight).AutomaticOffset, Kit.Tolerance);
        Assert.Equal(-6, Kit.Timing(Kit.Entry("p1.buster", null, Kit.Do(Kit.Swiftcast), Kit.Do(Kit.Temperance)), leadFight).AutomaticOffset, Kit.Tolerance);
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
