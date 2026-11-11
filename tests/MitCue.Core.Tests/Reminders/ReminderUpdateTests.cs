using MitCue.Core.Plans;
using MitCue.Core.Reminders;

namespace MitCue.Core.Tests.Reminders;

/// <summary>State progression and the visible list (defaults: lookahead 15 s, due lead 1.5 s, keep 3 s).</summary>
public class ReminderUpdateTests
{
    /// <summary>Temperance at the 100 s raidwide with the default offset: press 94, window [81, 99.7].</summary>
    private static PlanEntry Temperance() => Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Temperance));

    [Fact]
    public void ScriptedPull_MovesThroughUpcomingDueAndMissed()
    {
        var s = Kit.Scheduler(Temperance());
        var due = Kit.RecordDue(s);
        var r = s.All[0];

        Assert.Empty(s.Update(0));
        Assert.Equal(ReminderState.Pending, r.State);

        Assert.Empty(s.Update(78.9));                      // appears at 94 − 15 = 79
        Assert.Equal(ReminderState.Pending, r.State);

        Assert.Same(r, Assert.Single(s.Update(79)));
        Assert.Equal(ReminderState.Upcoming, r.State);

        s.Update(92.4);                                    // due at 94 − 1.5 = 92.5
        Assert.Equal(ReminderState.Upcoming, r.State);
        Assert.Empty(due);

        Assert.Same(r, Assert.Single(s.Update(92.5)));
        Assert.Equal(ReminderState.Due, r.State);
        Assert.Equal([r], due);

        s.Update(96);
        s.Update(r.Latest);                                // 99.7 is the last useful moment
        Assert.Equal(ReminderState.Due, r.State);
        Assert.Null(r.FinishedAt);

        var missedAt = r.Latest + 0.05;
        Assert.Same(r, Assert.Single(s.Update(missedAt))); // missed reminders stay listed for a while
        Assert.Equal(ReminderState.Missed, r.State);
        Assert.Equal(missedAt, r.FinishedAt);

        Assert.Same(r, Assert.Single(s.Update(missedAt + 3)));
        Assert.Empty(s.Update(missedAt + 3.01));
        Assert.Equal(ReminderState.Missed, r.State);
        Assert.Equal([r], due);                            // raised exactly once
        Assert.Null(r.UsedAt);
    }

    [Fact]
    public void BigStep_FromPendingStraightToDue_RaisesBecameDueOnce()
    {
        var s = Kit.Scheduler(Temperance());
        var due = Kit.RecordDue(s);
        var r = s.All[0];

        s.Update(0);
        s.Update(93);
        Assert.Equal(ReminderState.Due, r.State);
        Assert.Equal([r], due);

        s.Update(93.5);
        s.Update(95);
        Assert.Equal([r], due);
    }

    [Fact]
    public void BigStep_StraightPastLatest_IsMissedWithoutBecameDue()
    {
        var s = Kit.Scheduler(Temperance(), Kit.Entry("p1.buster", null, Kit.Do(Kit.Rampart)));
        var due = Kit.RecordDue(s);

        s.Update(0);
        s.Update(118);                                     // Temperance: Pending → Missed; Rampart (press 124) upcoming
        Assert.Equal(ReminderState.Missed, s.All[0].State);
        Assert.Equal(118, s.All[0].FinishedAt);
        Assert.Equal(ReminderState.Upcoming, s.All[1].State);

        s.Update(150);                                     // Rampart: Upcoming → Missed
        Assert.Equal(ReminderState.Missed, s.All[1].State);
        Assert.Empty(due);
    }

    [Fact]
    public void BecameDue_IsRaisedOncePerReminder_InPressOrder()
    {
        var s = Kit.Scheduler(
            Kit.Entry("p1.buster", null, Kit.Do(Kit.Rampart)),
            Temperance(),
            Kit.Entry("p1.raidwide", null, Kit.Note("Stack")));
        var due = Kit.RecordDue(s);

        for (var t = 0.0; t < 200; t += 0.1)
            s.Update(t);

        Assert.Equal(s.All, due);                         // Temperance 94, note 97, Rampart 124
        Assert.Equal(["Temperance", "Stack", "Rampart"], due.Select(r => r.Item.ToString()));
    }

    [Fact]
    public void BecameDue_HandlerSeesTheUpdatedReminder()
    {
        var s = Kit.Scheduler(Temperance());
        var cooldowns = new FakeCooldowns().Set(Kit.Temperance, 10);
        ReminderState? stateInHandler = null;
        double? readyInInHandler = null;
        s.BecameDue += r =>
        {
            stateInHandler = r.State;
            readyInInHandler = r.ReadyIn;
        };

        s.Update(93, cooldowns);

        Assert.Equal(ReminderState.Due, stateInHandler);
        Kit.Close(9, readyInInHandler);                    // ready at 103, 9 s after the press time
    }

    [Fact]
    public void Note_IsPassedNotMissed_AndDisappears()
    {
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", null, Kit.Note("Stack")));
        var due = Kit.RecordDue(s);
        var note = s.All[0];                               // press 97, latest 99.7

        Assert.Same(note, Assert.Single(s.Update(90)));
        Assert.Equal(ReminderState.Upcoming, note.State);
        s.Update(96);
        Assert.Equal(ReminderState.Due, note.State);
        Assert.Equal([note], due);

        Assert.Empty(s.Update(99.8));
        Assert.Equal(ReminderState.Passed, note.State);
        Assert.Equal(99.8, note.FinishedAt);
        Assert.Empty(s.Update(100));
    }

    [Fact]
    public void OptionalEntry_IsPassedNotMissed()
    {
        var s = Kit.Scheduler(Temperance().AsOptional());
        var r = s.All[0];

        s.Update(93);
        Assert.Equal(ReminderState.Due, r.State);
        Assert.Empty(s.Update(99.8));
        Assert.Equal(ReminderState.Passed, r.State);
        Assert.Equal(99.8, r.FinishedAt);
    }

    [Fact]
    public void ItemWithoutActions_IsTreatedLikeANote()
    {
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", -3, new PlanItem { Any = [] }));
        var r = s.All[0];

        Assert.False(r.IsAction);
        s.Update(99.8);
        Assert.Equal(ReminderState.Passed, r.State);
    }

    [Fact]
    public void DoneReminder_IsListedForKeepFinishedFor_ThenHidden()
    {
        var s = Kit.Scheduler(Temperance());
        var r = s.All[0];

        s.Update(90);
        s.OnOwnAction(Kit.Temperance.Id, 93);
        Assert.Same(r, Assert.Single(s.Update(93)));
        Assert.Same(r, Assert.Single(s.Update(96)));       // 93 + 3
        Assert.Empty(s.Update(96.01));
        Assert.Empty(s.Update(150));
        Assert.Equal(ReminderState.Done, r.State);
    }

    [Fact]
    public void FinishedReminders_StayListedAcrossSmallBackwardCorrections()
    {
        var s = Kit.Scheduler(Temperance());
        var r = s.All[0];

        s.Update(90);
        s.OnOwnAction(Kit.Temperance.Id, 93);
        Assert.Same(r, Assert.Single(s.Update(91.5)));     // the clock snapped back 1.5 s
        Assert.Empty(s.Update(80));                        // far before the use (a rewind): hidden
    }

    [Fact]
    public void Update_NeverMovesStatesBack()
    {
        var s = Kit.Scheduler(Temperance(), Kit.Entry("p1.buster", null, Kit.Do(Kit.Rampart)));
        var due = Kit.RecordDue(s);

        s.Update(109.5);                                   // Temperance missed; Rampart (press 124) upcoming from 109
        s.Update(92.6);                                    // only OnTimelineJump/Reset move reminders back
        Assert.Equal(ReminderState.Missed, s.All[0].State);
        Assert.Equal(ReminderState.Upcoming, s.All[1].State);

        s.Update(122.6);                                   // Rampart due at 122.5
        s.Update(122);                                     // a 0.6 s sync correction: stays due, no flicker
        Assert.Equal(ReminderState.Due, s.All[1].State);
        s.Update(122.6);
        Assert.Equal([s.All[1]], due);
    }

    [Fact]
    public void Visible_IsOrderedByPressTime_ThenEntry_ThenItem()
    {
        var fight = Kit.Fight(90, Kit.Mech("a", 100), Kit.Mech("b", 104));
        var e1 = Kit.Entry("b", -3, Kit.Do(Kit.Rampart));                                   // 101
        var e2 = Kit.Entry("a", -2, Kit.Do(Kit.Temperance), Kit.Do(Kit.Tetragrammaton));    // 98, 98
        var e3 = Kit.Entry("a", -2, Kit.Do(Kit.Swiftcast));                                 // 98
        var s = Kit.Scheduler(fight, new ReminderOptions(), e1, e2, e3);

        var visible = s.Update(90);

        Assert.Equal([(e2, 0), (e2, 1), (e3, 0), (e1, 0)], visible.Select(r => (r.Entry, r.ItemIndex)));
    }

    [Fact]
    public void Visible_HoldsUpcomingDueAndRecentlyFinished_ButNotPendingOrPassed()
    {
        var fight = Kit.Fight(90, Kit.Mech("a", 100), Kit.Mech("b", 108), Kit.Mech("c", 140));
        var done = Kit.Entry("a", -6, Kit.Do(Kit.Temperance));         // press 94
        var missed = Kit.Entry("a", -6, Kit.Do(Kit.Swiftcast));        // press 94, latest 99.7
        var passed = Kit.Entry("a", -6, Kit.Note("Stack"));            // press 94, latest 99.7
        var dueSoon = Kit.Entry("b", -7, Kit.Do(Kit.Rampart));         // press 101, due from 99.5
        var upcoming = Kit.Entry("b", 0, Kit.Do(Kit.Tetragrammaton));  // press 108, upcoming from 93
        var pending = Kit.Entry("c", -6, Kit.Do(Kit.Temperance));      // press 134, upcoming from 119
        var s = Kit.Scheduler(fight, new ReminderOptions(), done, missed, passed, dueSoon, upcoming, pending);

        s.Update(90);
        s.OnOwnAction(Kit.Temperance.Id, 98);
        var visible = s.Update(100);

        Assert.Equal([done, missed, dueSoon, upcoming], visible.Select(r => r.Entry));
        Assert.Equal(
            [ReminderState.Done, ReminderState.Missed, ReminderState.Due, ReminderState.Upcoming],
            visible.Select(r => r.State));
        Assert.Equal(ReminderState.Passed, s.All.Single(r => r.Entry == passed).State);
        Assert.Equal(ReminderState.Pending, s.All.Single(r => r.Entry == pending).State);
    }

    [Fact]
    public void Update_ReturnsASnapshot()
    {
        var s = Kit.Scheduler(Temperance());
        var first = s.Update(90);
        var second = s.Update(200);

        Assert.Same(s.All[0], Assert.Single(first));
        Assert.Empty(second);
    }

    [Fact]
    public void TimingOptions_AreReadLive()
    {
        var options = new ReminderOptions { Lookahead = 5, DueLead = 0 };
        var s = Kit.Scheduler(options, Temperance());
        var r = s.All[0];

        s.Update(85);
        Assert.Equal(ReminderState.Pending, r.State);
        options.Lookahead = 15;
        s.Update(85);
        Assert.Equal(ReminderState.Upcoming, r.State);
        s.Update(93);
        Assert.Equal(ReminderState.Upcoming, r.State);
        options.DueLead = 1.5;
        s.Update(93);
        Assert.Equal(ReminderState.Due, r.State);
    }

    [Fact]
    public void KeepFinishedFor_IsReadLive()
    {
        var options = new ReminderOptions();
        var s = Kit.Scheduler(options, Temperance());

        s.Update(99.8);                                    // missed at 99.8
        Assert.Single(s.Update(102));
        options.KeepFinishedFor = 1;
        Assert.Empty(s.Update(102));
    }

    [Fact]
    public void EmptyPlan_UpdatesToNothing()
    {
        var s = Kit.Scheduler();
        Assert.Empty(s.All);
        Assert.Empty(s.Update(100));
        var summary = s.Summarize();
        Assert.Equal((0, 0, 0, 0), (summary.Total, summary.Done, summary.Missed, summary.Skipped));
        Assert.Empty(summary.MissedReminders);
    }
}
