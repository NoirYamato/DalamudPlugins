using MitCue.Core.Plans;
using MitCue.Core.Reminders;

namespace MitCue.Core.Tests.Reminders;

/// <summary>Own action uses (OnOwnAction): which reminder a use satisfies.</summary>
public class ReminderUsageTests
{
    private static readonly uint TemperanceId = Kit.Temperance.Id;

    /// <summary>
    /// Temperance at the 100 s raidwide with the default offset: press 94, earliest 81, latest 99.7;
    /// uses count from min(earliest, press − max(lookahead 15, due lead 1.5)) = 79.
    /// </summary>
    private static PlanEntry Temperance() => Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Temperance));

    [Fact]
    public void UseInTheWindow_MarksTheReminderDone()
    {
        var s = Kit.Scheduler(Temperance());
        var r = s.All[0];

        s.Update(90);
        s.OnOwnAction(TemperanceId, 93);

        Assert.Equal(ReminderState.Done, r.State);
        Assert.Equal(93, r.UsedAt);
        Assert.Equal(TemperanceId, r.UsedActionId);
        Assert.False(r.UsedEarly);
        Assert.Equal(93, r.FinishedAt);
        Assert.Equal(new ActionUse(TemperanceId, 93, r), Assert.Single(s.RecentUses));

        s.Update(150);
        Assert.Equal(ReminderState.Done, r.State);
    }

    [Fact]
    public void UseBeforeEarliest_IsDoneButFlaggedEarly()
    {
        var s = Kit.Scheduler(Temperance());
        var r = s.All[0];

        s.OnOwnAction(TemperanceId, 80);                   // effect (20 s) would end at 100, before the hit + 1

        Assert.Equal(ReminderState.Done, r.State);
        Assert.True(r.UsedEarly);
        Assert.Equal(80, r.FinishedAt);
    }

    [Fact]
    public void UseAtEarliest_IsNotEarly()
    {
        var s = Kit.Scheduler(Temperance());
        var r = s.All[0];
        s.OnOwnAction(TemperanceId, r.Earliest);
        Assert.Equal(ReminderState.Done, r.State);
        Assert.False(r.UsedEarly);
    }

    [Fact]
    public void WindowBounds_AreInclusive()
    {
        var s = Kit.Scheduler(Temperance(), Temperance());
        var (first, second) = (s.All[0], s.All[1]);

        s.OnOwnAction(TemperanceId, 79);                   // press − lookahead
        Assert.Equal(ReminderState.Done, first.State);
        Assert.True(first.UsedEarly);

        s.OnOwnAction(TemperanceId, second.Latest);
        Assert.Equal(ReminderState.Done, second.State);
        Assert.False(second.UsedEarly);
    }

    [Fact]
    public void UseBeforeTheWindow_IsIgnoredButRecorded()
    {
        var s = Kit.Scheduler(Temperance());
        var r = s.All[0];

        s.OnOwnAction(TemperanceId, 78.99);

        Assert.Equal(ReminderState.Pending, r.State);
        Assert.Null(r.UsedAt);
        Assert.Equal(new ActionUse(TemperanceId, 78.99, null), Assert.Single(s.RecentUses));
    }

    [Fact]
    public void ShortLookahead_UseFromEarliestOn_StillCounts()
    {
        var s = Kit.Scheduler(new ReminderOptions { Lookahead = 3 }, Temperance()); // shown from 91, earliest 81
        var r = s.All[0];

        s.OnOwnAction(TemperanceId, 80.99);                // before earliest and before it is shown
        Assert.Equal(ReminderState.Pending, r.State);

        s.Update(88);
        s.OnOwnAction(TemperanceId, 89);                   // not shown yet, but the 20 s effect covers the hit
        Assert.Equal(ReminderState.Done, r.State);
        Assert.False(r.UsedEarly);

        s.Update(100);
        Assert.Equal(ReminderState.Done, r.State);         // not missed
    }

    [Fact]
    public void DueLeadLongerThanLookahead_PressOnNow_Counts()
    {
        // Hallowed Ground (10 s) at the 100 s raidwide: press 98, earliest 91; NOW from 98 − 8 = 90.
        var s = Kit.Scheduler(new ReminderOptions { Lookahead = 3, DueLead = 8 }, Kit.Entry("p1.raidwide", null, Kit.Do(Kit.HallowedGround)));
        var due = Kit.RecordDue(s);
        var r = s.All[0];

        s.Update(90);
        Assert.Equal(ReminderState.Due, r.State);
        Assert.Equal([r], due);

        s.OnOwnAction(Kit.HallowedGround.Id, 90.5);        // the player obeys the NOW
        Assert.Equal(ReminderState.Done, r.State);
        Assert.True(r.UsedEarly);                          // before earliest: flagged, but counted
    }

    [Fact]
    public void LongEffect_UsedBeforeItIsShown_ButCoveringTheHit_Counts()
    {
        // Adloquium (30 s shield, 2 s cast) at the 100 s raidwide: press 95.5, shown from 80.5, earliest 71.
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Adloquium)));
        var r = s.All[0];
        Assert.Equal(71, r.Earliest, Kit.Tolerance);

        s.OnOwnAction(Kit.Adloquium.Id, 70.99);            // would expire before the hit
        Assert.Null(Assert.Single(s.RecentUses).Matched);

        s.OnOwnAction(Kit.Adloquium.Id, 78);               // the shield lasts until 108
        Assert.Equal(ReminderState.Done, r.State);
        Assert.False(r.UsedEarly);
    }

    [Fact]
    public void UseAfterLatest_IsIgnored()
    {
        var s = Kit.Scheduler(Temperance());
        var r = s.All[0];

        s.Update(99);
        s.OnOwnAction(TemperanceId, r.Latest + 0.01);

        Assert.Equal(ReminderState.Due, r.State);
        Assert.Null(Assert.Single(s.RecentUses).Matched);
        s.Update(r.Latest + 0.01);
        Assert.Equal(ReminderState.Missed, r.State);
    }

    [Fact]
    public void UnrelatedAction_IsIgnored()
    {
        var s = Kit.Scheduler(Temperance());
        s.Update(90);
        s.OnOwnAction(Kit.Tetragrammaton.Id, 93);

        Assert.Equal(ReminderState.Upcoming, s.All[0].State);
        Assert.Null(Assert.Single(s.RecentUses).Matched);
    }

    [Fact]
    public void Use_DoesNotNeedAnUpdateFirst()
    {
        var s = Kit.Scheduler(Temperance());
        s.OnOwnAction(TemperanceId, 93);                   // reminder still Pending: Update was never called
        Assert.Equal(ReminderState.Done, s.All[0].State);
    }

    [Fact]
    public void OneUse_SatisfiesOnlyOneOfTwoIdenticalReminders()
    {
        var s = Kit.Scheduler(Temperance(), Temperance());
        var (first, second) = (s.All[0], s.All[1]);
        s.Update(90);

        s.OnOwnAction(TemperanceId, 93);
        Assert.Equal(ReminderState.Done, first.State);
        Assert.Equal(ReminderState.Upcoming, second.State);

        s.OnOwnAction(TemperanceId, 94);
        Assert.Equal(ReminderState.Done, second.State);
        Assert.Equal(94, second.UsedAt);

        s.OnOwnAction(TemperanceId, 95);                   // nothing left to satisfy
        Assert.Null(s.RecentUses[^1].Matched);
        Assert.Equal([first, second, null], s.RecentUses.Select(u => u.Matched));
    }

    [Fact]
    public void Use_SatisfiesTheReminderWithTheSmallestPressTime()
    {
        var fight = Kit.Fight(90, Kit.Mech("a", 100), Kit.Mech("b", 105));
        var later = Kit.Entry("b", -6, Kit.Do(Kit.Temperance));   // press 99, window [84, 104.7]
        var earlier = Kit.Entry("a", -6, Kit.Do(Kit.Temperance)); // press 94, window [79, 99.7]
        var s = Kit.Scheduler(fight, new ReminderOptions(), later, earlier);
        var byEntry = s.All.ToDictionary(r => r.Entry);

        s.OnOwnAction(TemperanceId, 95);
        Assert.Equal(ReminderState.Done, byEntry[earlier].State);
        Assert.Equal(ReminderState.Pending, byEntry[later].State);

        s.OnOwnAction(TemperanceId, 100);
        Assert.Equal(ReminderState.Done, byEntry[later].State);
    }

    [Fact]
    public void Use_SkipsRemindersWhoseWindowDoesNotContainIt()
    {
        var fight = Kit.Fight(90, Kit.Mech("a", 100), Kit.Mech("b", 105));
        var s = Kit.Scheduler(fight, new ReminderOptions(), Kit.Entry("a", -6, Kit.Do(Kit.Temperance)), Kit.Entry("b", -6, Kit.Do(Kit.Temperance)));

        s.OnOwnAction(TemperanceId, 104);                  // past the first window (99.7), inside the second

        Assert.Equal(ReminderState.Pending, s.All[0].State);
        Assert.Equal(ReminderState.Done, s.All[1].State);
    }

    [Fact]
    public void EitherOr_IsDoneByAnyOption()
    {
        var s = Kit.Scheduler(
            Kit.Entry("p1.raidwide", -3, Kit.Either(Kit.Tetragrammaton, Kit.DivineBenison)),
            Kit.Entry("p1.buster", -3, Kit.Either(Kit.Tetragrammaton, Kit.DivineBenison)));
        var (first, second) = (s.All[0], s.All[1]);

        s.OnOwnAction(Kit.DivineBenison.Id, 96);
        Assert.Equal(ReminderState.Done, first.State);
        Assert.Equal(Kit.DivineBenison.Id, first.UsedActionId);

        s.OnOwnAction(Kit.Tetragrammaton.Id, 126);
        Assert.Equal(ReminderState.Done, second.State);
        Assert.Equal(Kit.Tetragrammaton.Id, second.UsedActionId);
    }

    [Fact]
    public void SequenceItems_CanBeDoneInAnyOrder()
    {
        var entry = Kit.Entry("p1.raidwide", -5, Kit.Do(Kit.Temperance), Kit.Do(Kit.Tetragrammaton));
        entry.Seq = true;
        var s = Kit.Scheduler(entry);

        s.OnOwnAction(Kit.Tetragrammaton.Id, 95);

        Assert.Equal(ReminderState.Pending, s.All[0].State);
        Assert.Equal(ReminderState.Done, s.All[1].State);
    }

    [Fact]
    public void Notes_AreNeverSatisfied()
    {
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", -3, Kit.Note("Everything")));
        s.OnOwnAction(TemperanceId, 96);
        s.OnOwnAction(0, 96);

        Assert.Equal(ReminderState.Pending, s.All[0].State);
        Assert.All(s.RecentUses, u => Assert.Null(u.Matched));
    }

    [Fact]
    public void FinishedReminders_AreNotSatisfiedAgain()
    {
        var fight = Kit.Fight(90, Kit.Mech("a", 100), Kit.Mech("b", 200), Kit.Mech("c", 300));
        var s = Kit.Scheduler(
            fight,
            new ReminderOptions(),
            Kit.Entry("a", -6, Kit.Do(Kit.Temperance)),    // missed
            Kit.Entry("b", -6, Kit.Do(Kit.Temperance)),    // skipped
            Kit.Entry("c", -6, Kit.Do(Kit.Temperance)));   // done
        var (missed, skipped, done) = (s.All[0], s.All[1], s.All[2]);

        s.Update(99.8);
        s.OnTimelineJump(100, 250);
        s.OnOwnAction(TemperanceId, 290);
        Assert.Equal((ReminderState.Missed, ReminderState.Skipped, ReminderState.Done), (missed.State, skipped.State, done.State));

        s.OnOwnAction(TemperanceId, 99);                   // inside the missed window, but it is finished
        s.OnOwnAction(TemperanceId, 190);                  // inside the skipped window
        s.OnOwnAction(TemperanceId, 291);                  // inside the done window

        Assert.Equal((ReminderState.Missed, ReminderState.Skipped, ReminderState.Done), (missed.State, skipped.State, done.State));
        Assert.Equal(290, done.UsedAt);
        Assert.Equal([done, null, null, null], s.RecentUses.Select(u => u.Matched));
    }

    [Fact]
    public void DoneBeforeDue_NeverRaisesBecameDue()
    {
        var s = Kit.Scheduler(Temperance());
        var due = Kit.RecordDue(s);

        s.Update(85);
        s.OnOwnAction(TemperanceId, 86);
        for (var t = 86.0; t < 120; t += 0.5)
            s.Update(t);

        Assert.Empty(due);
        Assert.Equal(ReminderState.Done, s.All[0].State);
    }

    [Fact]
    public void RecentUses_KeepsTheLast50()
    {
        var s = Kit.Scheduler(Temperance());
        s.OnOwnAction(TemperanceId, 90);
        for (var i = 0; i < 60; i++)
            s.OnOwnAction(Kit.Swiftcast.Id, 100 + i);

        Assert.Equal(ReminderScheduler.RecentUsesCapacity, s.RecentUses.Count);
        Assert.Equal(50, s.RecentUses.Count);
        Assert.Equal(110, s.RecentUses[0].Time);           // oldest kept
        Assert.Equal(159, s.RecentUses[^1].Time);
        Assert.DoesNotContain(s.RecentUses, u => u.Matched != null);
        Assert.Equal(ReminderState.Done, s.All[0].State);  // trimming the log does not touch reminders
    }
}
