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
    public void ForwardJump_SkipsPendingAndUpcomingRemindersInTheJumpedSpan()
    {
        var s = Kit.Scheduler(At("p1.raidwide"), At("p1.buster"), At("p1.triple"), At("p2.raidwide"));
        var due = Kit.RecordDue(s);
        var (a, b, c, d) = (s.All[0], s.All[1], s.All[2], s.All[3]);

        s.Update(90);
        Assert.Equal((ReminderState.Upcoming, ReminderState.Pending), (a.State, b.State));

        s.OnTimelineJump(90, 125);

        Assert.Equal(ReminderState.Skipped, a.State);
        Assert.Equal(125, a.FinishedAt);
        Assert.Equal(ReminderState.Skipped, b.State);     // press 125 == jump target: inclusive
        Assert.Equal(ReminderState.Pending, c.State);
        Assert.Equal(ReminderState.Pending, d.State);

        Assert.Empty(s.Update(125));                       // skipped reminders are hidden by default
        for (var t = 125.0; t < 150; t += 0.5)
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
        shown.OnTimelineJump(90, 125);
        Assert.Equal(shown.All, shown.Update(125));
        Assert.Equal(2, shown.Update(128).Count);          // KeepFinishedFor = 3 after the jump target
        Assert.Empty(shown.Update(128.01));
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
    public void BackwardJump_OnlyRestoresRemindersAfterTheTarget()
    {
        var s = Kit.Scheduler(At("p1.buster"), At("p1.triple"));
        var (b, c) = (s.All[0], s.All[1]);

        s.Update(140);                                     // b missed, c upcoming
        s.OnTimelineJump(140, 170);                        // c skipped
        s.OnTimelineJump(170, 126);                        // 44 s back, but b's press time (125) is before 126

        Assert.Equal(ReminderState.Missed, b.State);
        Assert.Equal(ReminderState.Pending, c.State);
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
