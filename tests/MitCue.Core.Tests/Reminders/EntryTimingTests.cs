using MitCue.Core.Actions;
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
