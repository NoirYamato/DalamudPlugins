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
