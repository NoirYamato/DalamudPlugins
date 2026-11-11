using MitCue.Core.Plans;
using MitCue.Core.Reminders;

namespace MitCue.Core.Tests.Reminders;

/// <summary>Timeline jumps (phase pushes, rewinds) and Reset.</summary>
public class ReminderJumpTests
{
    private static readonly uint TemperanceId = Kit.Temperance.Id;

    // Temperance 5 s before each hit: press 95 / 125 / 155 / 295, latest hit − 0.3, upcoming from press − 15.
    private static PlanEntry At(string mechanic) => Kit.Entry(mechanic, -5, Kit.Do(Kit.Temperance));

    [Fact]
    public void ForwardJump_SkipsRemindersWhoseHitItJumpedOver()
    {
        var s = Kit.Scheduler(At("p1.raidwide"), At("p1.buster"), At("p1.triple"), At("p2.raidwide"));
        var due = Kit.RecordDue(s);
        var (a, b, c, d) = (s.All[0], s.All[1], s.All[2], s.All[3]);

        s.Update(90);
        Assert.Equal((ReminderState.Upcoming, ReminderState.Pending), (a.State, b.State));

        s.OnTimelineJump(90, 130);                         // phase push past the raidwide (100) and the buster (130)

        Assert.Equal(ReminderState.Skipped, a.State);
        Assert.Equal(130, a.FinishedAt);
        Assert.Equal(ReminderState.Skipped, b.State);     // hit 130 == jump target: inclusive
        Assert.Equal(ReminderState.Pending, c.State);
        Assert.Equal(ReminderState.Pending, d.State);

        Assert.Empty(s.Update(130));                       // skipped reminders are hidden by default
        for (var t = 130.0; t < 150; t += 0.5)
            s.Update(t);
        Assert.Empty(due);                                 // skipped reminders never become due
        Assert.Equal(ReminderState.Skipped, a.State);
    }

    [Fact]
    public void ForwardJump_LowerBoundIsExclusive()
    {
        var s = Kit.Scheduler(At("p1.raidwide"), At("p1.buster"));

        s.OnTimelineJump(95, 120);

        Assert.Equal(ReminderState.Pending, s.All[0].State); // press 95 == jump origin
        Assert.Equal(ReminderState.Pending, s.All[1].State); // press 125 after the target
    }

    /// <summary>
    /// Dancing Mad: on P2 success the timeline jumps from the 483.2 cast-start sync to 597.0, and
    /// "Aero III Assault" hits at 600.0. Temperance with the default offset: press 594, latest 599.7.
    /// </summary>
    [Fact]
    public void ForwardJump_PastThePressTimeButBeforeTheHit_ReminderBecomesDue()
    {
        var fight = Kit.Fight(100, Kit.Mech("p2.trine", 469.5), Kit.Mech("p3.aero", 600));
        var s = Kit.Scheduler(fight, new ReminderOptions(), Kit.Entry("p3.aero", null, Kit.Do(Kit.Temperance)));
        var due = Kit.RecordDue(s);
        var r = s.All[0];
        Assert.Equal(594, r.PressAt, Kit.Tolerance);

        s.Update(483.2);
        s.OnTimelineJump(483.2, 597);                      // lands 3 s before the hit

        Assert.Equal(ReminderState.Pending, r.State);      // not skipped: the mechanic is still to come
        Assert.Same(r, Assert.Single(s.Update(597)));
        Assert.Equal(ReminderState.Due, r.State);          // NOW at once...
        Assert.Equal([r], due);                            // ...with its sound

        s.OnOwnAction(TemperanceId, 598);
        Assert.Equal(ReminderState.Done, r.State);
        var summary = s.Summarize();
        Assert.Equal((1, 1, 0, 0), (summary.Total, summary.Done, summary.Missed, summary.Skipped));
    }

    [Fact]
    public void ForwardJump_LandingOnThePressTime_ReminderBecomesDue()
    {
        var fight = Kit.Fight(100, Kit.Mech("p3.aero", 600));
        var s = Kit.Scheduler(fight, new ReminderOptions(), Kit.Entry("p3.aero", -3, Kit.Do(Kit.Temperance))); // press 597
        var due = Kit.RecordDue(s);

        s.OnTimelineJump(483.2, 597);
        s.Update(597);

        Assert.Equal(ReminderState.Due, s.All[0].State);
        Assert.Equal([s.All[0]], due);
    }

    [Fact]
    public void ForwardJump_BeforeTheHit_UseCountsForThatHit_NotTheNextOne()
    {
        var fight = Kit.Fight(90, Kit.Mech("a", 100), Kit.Mech("b", 110));
        var s = Kit.Scheduler(fight, new ReminderOptions(), Kit.Entry("a", null, Kit.Do(Kit.DivineBenison)), Kit.Entry("b", null, Kit.Do(Kit.DivineBenison)));
        var (first, second) = (s.All[0], s.All[1]);       // press 96 (hit 100) / 106 (hit 110)

        s.Update(88);
        s.OnTimelineJump(88, 97);
        s.Update(97);
        s.OnOwnAction(Kit.DivineBenison.Id, 98);           // pressed for the 100 s buster

        Assert.Equal(ReminderState.Done, first.State);
        Assert.Equal(ReminderState.Upcoming, second.State);
        Assert.Contains(second, s.Update(105));            // the 110 s buster is still reminded
        Assert.Equal(ReminderState.Due, second.State);
    }

    [Fact]
    public void ForwardJump_PastTheUseWindow_SkipsEvenBeforeTheHit()
    {
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Temperance))); // press 94, latest 99.7, hit 100
        var r = s.All[0];

        s.Update(90);
        s.OnTimelineJump(90, 99.8);                        // too late to cover the hit

        Assert.Equal(ReminderState.Skipped, r.State);
        Assert.Equal(99.8, r.FinishedAt);
    }

    [Fact]
    public void ForwardJump_PastTheHit_SkipsTheHealForItToo()
    {
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Tetragrammaton))); // press 101, latest 106
        var r = s.All[0];

        s.Update(95);
        s.OnTimelineJump(95, 105.5);                       // the raidwide at 100 was jumped over

        Assert.Equal(ReminderState.Skipped, r.State);
    }

    [Fact]
    public void ForwardJump_AfterTheHit_HealWithItsWindowOpen_BecomesDue()
    {
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", 4, Kit.Do(Kit.Tetragrammaton))); // press 104, latest 109
        var due = Kit.RecordDue(s);
        var r = s.All[0];

        s.Update(100.5);
        s.OnTimelineJump(100.5, 106);                      // the raidwide landed before the jump

        Assert.Equal(ReminderState.Upcoming, r.State);
        s.Update(106);
        Assert.Equal(ReminderState.Due, r.State);
        Assert.Equal([r], due);
    }

    [Fact]
    public void ForwardJump_LeavesDueAndFinishedRemindersAlone()
    {
        var swiftcast = Kit.Entry("p1.raidwide", -5, Kit.Do(Kit.Swiftcast));
        var s = Kit.Scheduler(At("p1.raidwide"), swiftcast, At("p1.buster"));
        var (dueOne, doneOne, later) = (s.All[0], s.All[1], s.All[2]);

        s.Update(94);                                      // both press-95 reminders are due
        s.OnOwnAction(Kit.Swiftcast.Id, 94);
        s.OnTimelineJump(94, 140);

        Assert.Equal(ReminderState.Due, dueOne.State);
        Assert.Equal(ReminderState.Done, doneOne.State);
        Assert.Equal(94, doneOne.FinishedAt);
        Assert.Equal(ReminderState.Skipped, later.State);

        s.Update(140);
        Assert.Equal(ReminderState.Missed, dueOne.State);  // was already due: missed, not skipped
    }

    [Fact]
    public void ForwardJump_SkipsNotesAndOptionalEntriesToo()
    {
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", -5, Kit.Note("Stack")), At("p1.raidwide").AsOptional());
        s.OnTimelineJump(50, 120);
        Assert.All(s.All, r => Assert.Equal(ReminderState.Skipped, r.State));
    }

    [Fact]
    public void SkippedReminders_AreListedOnlyWithShowSkipped()
    {
        var hidden = Kit.Scheduler(At("p1.raidwide"));
        hidden.Update(90);
        hidden.OnTimelineJump(90, 125);
        Assert.Empty(hidden.Update(125));

        var shown = Kit.Scheduler(new ReminderOptions { ShowSkipped = true }, At("p1.raidwide"), At("p1.buster"));
        shown.Update(90);
        shown.OnTimelineJump(90, 130);
        Assert.Equal(shown.All, shown.Update(130));
        Assert.Equal(2, shown.Update(128).Count);          // a small correction back does not hide them (no flicker)
        Assert.Equal(2, shown.Update(133).Count);          // KeepFinishedFor = 3 after the jump target
        Assert.Empty(shown.Update(133.01));
    }

    [Fact]
    public void BackwardJump_RestoresMissedSkippedAndPassed_KeepsDone()
    {
        var note = Kit.Entry("p1.buster", -5, Kit.Note("Stack"));
        var s = Kit.Scheduler(At("p1.raidwide"), At("p1.buster"), note, At("p1.triple"), At("p2.raidwide"));
        var byPress = s.All;
        var (a, b, bNote, c, d) = (byPress[0], byPress[1], byPress[2], byPress[3], byPress[4]);

        s.Update(90);
        s.OnOwnAction(TemperanceId, 94);                   // a done
        s.Update(140);                                     // b missed, note passed, c upcoming
        s.OnTimelineJump(140, 170);                        // c skipped
        s.Update(170);
        Assert.Equal(
            [ReminderState.Done, ReminderState.Missed, ReminderState.Passed, ReminderState.Skipped, ReminderState.Pending],
            s.All.Select(r => r.State));

        s.OnTimelineJump(170, 90);                         // rewind 80 s (e.g. a timeline loop)

        Assert.Equal(ReminderState.Done, a.State);         // press 95 > 90, but done stays done
        Assert.Equal(94, a.UsedAt);
        Assert.Equal(94, a.FinishedAt);
        foreach (var r in new[] { b, bNote, c })
        {
            Assert.Equal(ReminderState.Pending, r.State);
            Assert.Null(r.FinishedAt);
        }
        Assert.Equal(ReminderState.Pending, d.State);

        s.Update(112);
        Assert.Equal(ReminderState.Upcoming, b.State);     // replays normally
        Assert.Equal(ReminderState.Done, a.State);
    }

    [Fact]
    public void BackwardJump_OnlyRestoresRemindersWhoseWindowIsOpenAtTheTarget()
    {
        var s = Kit.Scheduler(At("p1.buster"), At("p1.triple"));
        var (b, c) = (s.All[0], s.All[1]);

        s.Update(140);                                     // b missed, c upcoming
        s.OnTimelineJump(140, 170);                        // c skipped
        s.OnTimelineJump(170, 131);                        // 39 s back, but b's window closed at 129.7

        Assert.Equal(ReminderState.Missed, b.State);
        Assert.Equal(140, b.FinishedAt);
        Assert.Equal(ReminderState.Pending, c.State);
    }

    [Theory]
    [InlineData(129.7, ReminderState.Pending)]           // the target is the last useful moment: reopened
    [InlineData(129.71, ReminderState.Missed)]
    public void BackwardJump_WindowBoundIsInclusive(double to, ReminderState expected)
    {
        var s = Kit.Scheduler(At("p1.buster"));            // press 125, latest 129.7

        s.Update(140);
        s.OnTimelineJump(140, to);

        Assert.Equal(expected, s.All[0].State);
    }

    [Fact]
    public void BackwardJump_IntoAnOpenWindow_ReopensAMissedReminder()
    {
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Temperance))); // press 94, latest 99.7, hit 100
        var due = Kit.RecordDue(s);
        var r = s.All[0];

        s.Update(93);
        s.Update(106);                                     // missed: the timeline ran ahead
        s.OnTimelineJump(106, 95);                         // resync 11 s back: the hit is ahead again

        Assert.Equal(ReminderState.Pending, r.State);      // although its press time (94) is before the target
        Assert.Null(r.FinishedAt);
        s.Update(95);
        Assert.Equal(ReminderState.Due, r.State);
        Assert.Equal([r, r], due);                         // a second chance, announced again

        s.OnOwnAction(TemperanceId, 96);
        Assert.Equal(ReminderState.Done, r.State);
        var summary = s.Summarize();
        Assert.Equal((1, 1, 0), (summary.Total, summary.Done, summary.Missed));
    }

    [Fact]
    public void BackwardJump_ReopensSkippedRemindersInsideTheirWindowToo()
    {
        var s = Kit.Scheduler(At("p1.raidwide"));          // press 95, latest 99.7
        var r = s.All[0];

        s.Update(90);
        s.OnTimelineJump(90, 110);                         // skipped (hit 100 jumped over)
        s.OnTimelineJump(110, 97);                         // the push was a false alarm: back before the hit

        Assert.Equal(ReminderState.Pending, r.State);
        s.Update(97);
        Assert.Equal(ReminderState.Due, r.State);
    }

    [Fact]
    public void BackwardJump_ReturnsUpcomingAndDueRemindersToPending()
    {
        var s = Kit.Scheduler(At("p1.buster"), At("p1.triple"));
        var (b, c) = (s.All[0], s.All[1]);

        s.Update(124);                                     // b due (from 123.5)
        s.OnTimelineJump(124, 100);                        // 24 s back
        Assert.Equal(ReminderState.Pending, b.State);
        s.Update(100);
        Assert.Equal(ReminderState.Pending, b.State);      // 125 − 15 = 110 > 100
        s.Update(111);
        Assert.Equal(ReminderState.Upcoming, b.State);

        s.Update(141);                                     // b missed, c upcoming (from 140)
        s.OnTimelineJump(141, 120);
        Assert.Equal(ReminderState.Pending, c.State);
        Assert.Equal(ReminderState.Pending, b.State);
    }

    [Fact]
    public void BackwardJump_TakingADueReminderOutOfDue_AnnouncesItAgainWhenDue()
    {
        var s = Kit.Scheduler(At("p1.buster"));            // press 125, due from 123.5
        var due = Kit.RecordDue(s);
        var b = s.All[0];

        s.Update(124);                                     // NOW (and sound) came too early: the timeline ran ahead
        s.OnTimelineJump(124, 118);                        // 6 s back

        Assert.Equal(ReminderState.Upcoming, Assert.Single(s.Update(118)).State); // counts down again, no stale NOW
        s.Update(123.4);
        Assert.Equal([b], due);
        s.Update(123.5);
        Assert.Equal(ReminderState.Due, b.State);
        Assert.Equal([b, b], due);                         // announced again at the right moment
    }

    [Fact]
    public void BackwardJump_ReminderStillDueAtTheTarget_StaysDue_WithoutASecondBecameDue()
    {
        var s = Kit.Scheduler(Kit.Entry("p1.buster", null, Kit.Do(Kit.Rampart))); // press 124, due from 122.5, latest 129.7
        var due = Kit.RecordDue(s);
        var r = s.All[0];

        s.Update(123);
        s.Update(129.5);
        s.OnTimelineJump(129.5, 123);                      // 6.5 s back, still inside the due span

        Assert.Equal(ReminderState.Due, r.State);
        Assert.Same(r, Assert.Single(s.Update(123)));
        Assert.Equal(ReminderState.Due, r.State);
        Assert.Equal([r], due);                            // no second sound for an unbroken NOW

        s.OnOwnAction(Kit.Rampart.Id, 124);
        Assert.Equal(ReminderState.Done, r.State);
    }

    [Fact]
    public void BackwardJump_StillDueWhenReadyEntry_ObservesTheCooldownAgain()
    {
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Temperance)).AsWhenReady()); // press 94, latest 99.7
        var r = s.All[0];
        var cooldowns = new FakeCooldowns();

        s.Update(93, cooldowns.Set(Kit.Temperance, 10));   // due, waiting for the cooldown
        s.Update(103, cooldowns.Set(Kit.Temperance, 0));   // ready at 103: due until 108
        s.Update(106, cooldowns);
        s.OnTimelineJump(106, 99);                         // 7 s back: still due

        s.Update(99, cooldowns);                           // ready "since 99" now: due until max(99.7, 99 + 5) = 104
        Assert.Equal(ReminderState.Due, r.State);
        s.Update(104, cooldowns);
        Assert.Equal(ReminderState.Due, r.State);
        s.Update(104.01, cooldowns);
        Assert.Equal(ReminderState.Missed, r.State);
    }

    [Fact]
    public void BackwardJump_HidesFinishedRowsFromBeforeTheRewind()
    {
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Temperance)), At("p1.buster")); // press 94 / 125
        var (a, b) = (s.All[0], s.All[1]);

        s.Update(90);
        s.OnOwnAction(TemperanceId, 94);                   // a done at 94
        s.Update(129.8);                                   // b missed at 129.8
        s.Update(136);
        s.OnTimelineJump(136, 130);                        // 6 s back: b's window (129.7) stays closed

        Assert.Equal(ReminderState.Missed, b.State);       // outcomes stand...
        Assert.Empty(s.Update(130));                       // ...but the rows are not listed again (b would be "0.2 s ago")

        s.OnTimelineJump(130, 90);                         // a rewind before a's use
        Assert.Equal(ReminderState.Done, a.State);
        Assert.Equal(94, a.FinishedAt);
        Assert.Empty(s.Update(90));                        // not listed as done 4 s before its use
        Assert.Empty(s.Update(95));
        Assert.Equal(ReminderState.Done, a.State);

        s.Reset();                                         // the next pull lists finished rows again
        s.Update(90);
        s.OnOwnAction(TemperanceId, 94);
        Assert.Same(a, Assert.Single(s.Update(94)));
    }

    [Fact]
    public void BackwardJump_RestoredRemindersRaiseBecameDueAgain()
    {
        var s = Kit.Scheduler(At("p1.buster"));
        var due = Kit.RecordDue(s);
        var b = s.All[0];

        s.Update(124);
        s.Update(130);
        Assert.Equal(ReminderState.Missed, b.State);
        Assert.Equal([b], due);

        s.OnTimelineJump(130, 110);
        s.Update(115);
        Assert.Equal(ReminderState.Upcoming, b.State);
        s.Update(124);
        Assert.Equal(ReminderState.Due, b.State);
        Assert.Equal([b, b], due);
    }

    [Theory]
    [InlineData(137)]   // 3 s back: sync jitter
    [InlineData(135)]   // exactly 5 s back is still a correction
    [InlineData(140)]   // no movement at all
    public void SmallBackwardCorrection_ChangesNothing(double to)
    {
        var s = Kit.Scheduler(At("p1.buster"), At("p1.triple"));
        var (b, c) = (s.All[0], s.All[1]);

        s.Update(140);
        s.OnTimelineJump(140, to);

        Assert.Equal(ReminderState.Missed, b.State);
        Assert.Equal(140, b.FinishedAt);
        Assert.Equal(ReminderState.Upcoming, c.State);
    }

    [Fact]
    public void Reset_ReturnsEverythingToPending_AndClearsUsage()
    {
        var s = Kit.Scheduler(At("p1.raidwide"), Kit.Entry("p1.buster", -5, Kit.Do(Kit.Rampart)), Kit.Entry("p1.triple", -5, Kit.Note("Stack")), At("p2.raidwide"));
        var due = Kit.RecordDue(s);
        var (a, b, note, d) = (s.All[0], s.All[1], s.All[2], s.All[3]);
        var cooldowns = new FakeCooldowns().Set(Kit.Temperance, 30);
        cooldowns.Resources[TemperanceId] = false;

        s.OnOwnAction(TemperanceId, 80);                   // a done early
        s.Update(124);                                     // b due
        s.Update(140);                                     // b missed, note upcoming
        s.OnTimelineJump(140, 200);                        // note skipped
        s.Update(285, cooldowns);                          // d upcoming, on cooldown, no resources
        Assert.True(a.UsedEarly);
        Assert.NotNull(d.ReadyIn);
        Assert.True(d.MissingResources);
        Assert.Equal([b], due);

        s.Reset();

        Assert.All(s.All, r =>
        {
            Assert.Equal(ReminderState.Pending, r.State);
            Assert.Null(r.FinishedAt);
            Assert.Null(r.UsedAt);
            Assert.Null(r.UsedActionId);
            Assert.False(r.UsedEarly);
            Assert.Null(r.ReadyIn);
            Assert.False(r.MissingResources);
        });
        Assert.Empty(s.RecentUses);
        Assert.Equal(ReminderState.Pending, note.State);

        s.Update(124);                                     // the next pull: due again, sound again
        Assert.Equal([b, b], due);
        Assert.Equal(ReminderState.Due, b.State);
        Assert.Equal(ReminderState.Missed, a.State);       // big step from the reset straight past a's window
    }

    [Fact]
    public void Reset_ForgetsWhenReadyWaiting()
    {
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Temperance)).AsWhenReady());
        var r = s.All[0];

        s.Update(98, new FakeCooldowns().Set(Kit.Temperance, 10)); // waiting for the cooldown
        s.Reset();

        s.Update(95);
        s.Update(r.Latest + 0.01);                         // no cooldown information this time
        Assert.Equal(ReminderState.Missed, r.State);
    }
}
