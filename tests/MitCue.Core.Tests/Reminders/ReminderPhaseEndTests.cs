using MitCue.Core.Plans;
using MitCue.Core.Reminders;

namespace MitCue.Core.Tests.Reminders;

/// <summary>
/// Phase-end reminders (docs/DESIGN.md §5.2): entries flagged <see cref="PlanEntry.AtPhaseEnd"/> or anchored to an enrage
/// become Due at the jump that ends their phase instead of being skipped.
/// </summary>
/// <remarks>
/// <see cref="Kit.PhasedTestFight"/>: phases p1 0, p2 200, p3 400; P1 raidwide 100, late raidwide 140, enrage 150; P2 start
/// marker 200, raidwide 250, enrage 350; P3 raidwide 450. The P1 boss dies at 120 and the P2 sync jumps to 200.
/// </remarks>
public class ReminderPhaseEndTests
{
    private const double Grace = ReminderScheduler.MinPhaseEndGrace;

    private static ReminderScheduler Scheduler(params PlanEntry[] entries) => Scheduler(new ReminderOptions(), entries);

    private static ReminderScheduler Scheduler(ReminderOptions options, params PlanEntry[] entries) =>
        Kit.Scheduler(Kit.PhasedTestFight(), options, entries);

    /// <summary>Rampart for the late raidwide (140), "as P1 dies": press 134, latest 139.7.</summary>
    private static PlanEntry RampartAsP1Dies() => Kit.Entry("p1.late", null, Kit.Do(Kit.Rampart)).AsPhaseEnd();

    [Fact]
    public void PhasePush_MakesPhaseEndRemindersDueAtTheJump_AndSkipsTheOthers()
    {
        var normal = Kit.Entry("p1.late", null, Kit.Do(Kit.Temperance));                      // press 134
        var flagged = RampartAsP1Dies();                                                       // press 134
        var enrage = Kit.Entry("p1.enrage", null, Kit.Do(Kit.DivineBenison));                  // press 146, enrage anchor
        var note = Kit.Entry("p1.enrage", -2, Kit.Note("Shake as P1 dies"));                   // press 148
        var s = Scheduler(normal, flagged, enrage, note);
        var due = Kit.RecordDue(s);
        var byEntry = s.All.ToDictionary(r => r.Entry);

        s.Update(118);
        s.OnTimelineJump(120, 200);

        var skipped = byEntry[normal];
        Assert.Equal(ReminderState.Skipped, skipped.State);
        Assert.Equal(200, skipped.FinishedAt);
        Assert.Null(skipped.PhaseEndDueAt);
        Assert.False(skipped.IsPhaseEnd);

        foreach (var entry in new[] { flagged, enrage, note })
        {
            var r = byEntry[entry];
            Assert.True(r.IsPhaseEnd);
            Assert.Equal(ReminderState.Due, r.State);       // due at the jump itself
            Assert.Equal(200, r.PhaseEndDueAt);
            Assert.Equal(200 + Grace, r.Latest, Kit.Tolerance);
            Assert.Null(r.FinishedAt);
        }
        Assert.Empty(due);                                  // announced by the next update

        var visible = s.Update(200);
        Assert.Equal([byEntry[flagged], byEntry[enrage], byEntry[note]], due);
        Assert.Equal(due, visible);
        s.Update(203);
        s.Update(200 + Grace);
        Assert.Equal(3, due.Count);                         // once each
        Assert.All(due, r => Assert.Equal(ReminderState.Due, r.State));

        s.Update(208.01);
        Assert.Equal(ReminderState.Missed, byEntry[flagged].State);
        Assert.Equal(ReminderState.Missed, byEntry[enrage].State);
        Assert.Equal(ReminderState.Passed, byEntry[note].State);
        Assert.Equal(208.01, byEntry[flagged].FinishedAt);

        var summary = s.Summarize();
        Assert.Equal((2, 0, 2, 1), (summary.Total, summary.Done, summary.Missed, summary.Skipped));
    }

    [Theory]
    [InlineData(200.0, false)]
    [InlineData(204.5, false)]
    [InlineData(208.0, false)]     // the grace end is inclusive
    [InlineData(208.01, true)]
    public void UseAfterTheTransition_IsDoneWithinTheGrace(double usedAt, bool ignored)
    {
        var s = Scheduler(RampartAsP1Dies());
        var r = s.All[0];

        s.Update(118);
        s.OnTimelineJump(120, 200);
        s.OnOwnAction(Kit.Rampart.Id, usedAt);

        if (ignored)
        {
            Assert.Null(s.RecentUses[^1].Matched);
            s.Update(usedAt);
            Assert.Equal(ReminderState.Missed, r.State);
        }
        else
        {
            Assert.Equal(ReminderState.Done, r.State);
            Assert.Equal(usedAt, r.UsedAt);
            Assert.False(r.UsedEarly);
            Assert.Equal((1, 1), (s.Summarize().Total, s.Summarize().Done));
        }
    }

    [Fact]
    public void UseRightAtTheTransition_BeforeTheNextUpdate_IsDoneWithoutASound()
    {
        var s = Scheduler(RampartAsP1Dies());
        var due = Kit.RecordDue(s);
        var r = s.All[0];

        s.OnTimelineJump(120, 200);
        s.OnOwnAction(Kit.Rampart.Id, 200);                 // same frame as the jump
        s.Update(200);

        Assert.Equal(ReminderState.Done, r.State);
        Assert.Empty(due);
    }

    [Fact]
    public void EveryItemOfAPhaseEndEntry_BecomesDue_EachWithItsOwnWindowExtended()
    {
        // "Protraction + Recit → Adlo → Deploy (just before P3 ends)": a sequence of items sharing the press time.
        var entry = Kit.Entry("p1.enrage", -10, Kit.Do(Kit.Swiftcast), Kit.Do(Kit.Adloquium), Kit.Do(Kit.Tetragrammaton)).AsPhaseEnd();
        entry.Seq = true;
        var s = Scheduler(entry);
        var due = Kit.RecordDue(s);

        Assert.Equal([149.7, 149.7, 145], s.All.Select(r => Math.Round(r.Latest, 6)));   // the heal: press 140 + 5
        s.OnTimelineJump(120, 200);
        s.Update(200);

        Assert.All(s.All, r => Assert.Equal(ReminderState.Due, r.State));
        Assert.All(s.All, r => Assert.Equal(208, r.Latest, Kit.Tolerance));
        Assert.Equal(s.All, due);
    }

    [Theory]
    [InlineData(3.0, 208.0)]       // the grace is at least 8 s...
    [InlineData(5.0, 208.0)]       // ...the default heal late window is shorter
    [InlineData(8.0, 208.0)]
    [InlineData(12.0, 212.0)]      // ...or the heal late window when longer
    public void Grace_IsTheLongerOfTheHealLateWindowAndEightSeconds(double healLateWindow, double expectedLatest)
    {
        var options = new ReminderOptions { HealLateWindow = healLateWindow };
        var s = Scheduler(options, RampartAsP1Dies());

        s.OnTimelineJump(120, 200);

        Assert.Equal(expectedLatest, s.All[0].Latest, Kit.Tolerance);
        s.Update(expectedLatest);
        Assert.Equal(ReminderState.Due, s.All[0].State);
        s.Update(expectedLatest + 0.01);
        Assert.Equal(ReminderState.Missed, s.All[0].State);
    }

    [Fact]
    public void Grace_ReadsTheHealLateWindowAtTheJump()
    {
        var options = new ReminderOptions();
        var s = Scheduler(options, RampartAsP1Dies());
        options.HealLateWindow = 10;                        // edited after the scheduler was built

        s.OnTimelineJump(120, 200);

        Assert.Equal(210, s.All[0].Latest, Kit.Tolerance);
    }

    [Fact]
    public void AlreadyDueAtThePush_StaysDue_WithTheGrace_AndNoSecondSound()
    {
        var normal = Kit.Entry("p1.late", null, Kit.Do(Kit.Temperance));
        var s = Scheduler(normal, RampartAsP1Dies());
        var due = Kit.RecordDue(s);
        var (temperance, rampart) = (s.All[0], s.All[1]);

        s.Update(133);                                      // both due from 132.5
        Assert.Equal([temperance, rampart], due);
        s.OnTimelineJump(135, 200);                         // the boss dies while they are due

        Assert.Equal(ReminderState.Due, temperance.State);  // normal reminders are left alone...
        Assert.Equal(139.7, temperance.Latest, Kit.Tolerance);
        Assert.Equal(ReminderState.Due, rampart.State);     // ...the phase-end one gets the transition
        Assert.Equal(208, rampart.Latest, Kit.Tolerance);

        s.Update(200);
        Assert.Equal(ReminderState.Missed, temperance.State); // ...and is missed at once
        Assert.Equal(ReminderState.Due, rampart.State);
        Assert.Equal([temperance, rampart], due);           // no second BecameDue
    }

    [Fact]
    public void PressTimeBetweenTheLastUpdateAndThePush_IsCaughtToo()
    {
        var normal = Kit.Entry("p1.late", null, Kit.Do(Kit.Temperance));
        var s = Scheduler(normal, RampartAsP1Dies());
        var due = Kit.RecordDue(s);
        var (temperance, rampart) = (s.All[0], s.All[1]);

        s.Update(130);                                      // both upcoming (press 134)
        s.OnTimelineJump(134.5, 200);                       // the jump starts after their press time

        Assert.Equal(ReminderState.Upcoming, temperance.State); // normal rule: the press time is not in (from, to]
        Assert.Equal(ReminderState.Due, rampart.State);
        s.Update(200);
        Assert.Equal(ReminderState.Missed, temperance.State);
        Assert.Equal([rampart], due);
    }

    [Theory]
    [InlineData(-6.0, 200.0, true)]    // press 194
    [InlineData(-0.01, 200.0, true)]
    [InlineData(0.0, 200.0, true)]     // a press exactly at the next phase's start belongs to the phase ending there
    [InlineData(0.0, 203.4, true)]     // TOP's v1 engine enters P2 at 203.4
    [InlineData(3.4, 203.4, false)]    // press 203.4 is in P2 already: not a P1 phase end (skipped, the 200 marker was jumped over)
    public void PressTimeAndJumpTarget_DecideWhichPhaseEnds(double offset, double to, bool caught)
    {
        var s = Scheduler(Kit.Entry("p2.start", offset, Kit.Do(Kit.Rampart)).AsPhaseEnd());
        var r = s.All[0];

        s.OnTimelineJump(120, to);

        Assert.Equal(caught ? ReminderState.Due : ReminderState.Skipped, r.State);
        Assert.Equal(caught ? (double?)to : null, r.PhaseEndDueAt);
    }

    [Theory]
    [InlineData(199.99)]   // still in P1: the phase did not end
    [InlineData(400.0)]    // past P2 as well (an enrage branch, or a pull joined later): not this reminder's phase end
    [InlineData(450.0)]
    public void JumpThatDoesNotEndTheReminderPhase_SkipsItLikeAnyOther(double to)
    {
        var s = Scheduler(RampartAsP1Dies());

        s.OnTimelineJump(120, to);

        Assert.Equal(ReminderState.Skipped, s.All[0].State);
        Assert.Null(s.All[0].PhaseEndDueAt);
        Assert.Equal(139.7, s.All[0].Latest, Kit.Tolerance);
    }

    [Fact]
    public void JumpOverAWholePhase_SkipsThePhaseEndRemindersOfEveryPhaseItLeaves()
    {
        // e.g. a preview from P3: P1 is cut at once and the timeline jumps from P1 straight to the P3 sync.
        var p1 = RampartAsP1Dies();
        var p2 = Kit.Entry("p2.enrage", null, Kit.Do(Kit.Rampart));    // press 344, enrage anchor
        var s = Scheduler(p1, p2);
        var byEntry = s.All.ToDictionary(r => r.Entry);

        s.OnTimelineJump(0.5, 400);

        Assert.Equal(ReminderState.Skipped, byEntry[p1].State);    // lands two phases on
        Assert.Equal(ReminderState.Skipped, byEntry[p2].State);    // starts before its phase
        Assert.All(s.All, r => Assert.Null(r.PhaseEndDueAt));
    }

    [Theory]
    [InlineData(0.0)]      // a timeline started from stopped (a pull joined in P2): SyncResult.FromTime is 0
    [InlineData(150.0)]
    [InlineData(199.99)]
    public void JumpThatStartsBeforeTheReminderPhase_SkipsIt(double from)
    {
        var s = Scheduler(Kit.Entry("p2.enrage", null, Kit.Do(Kit.Rampart)));   // press 344

        s.OnTimelineJump(from, 400);

        Assert.Equal(ReminderState.Skipped, s.All[0].State);
    }

    [Fact]
    public void BulkSkipFromMinusInfinity_SkipsPhaseEndRemindersToo()
    {
        // A preview starting at P3 skips everything planned before it with a jump from −∞ (EncounterSession.SkipBefore).
        var p1 = RampartAsP1Dies();
        var p2 = Kit.Entry("p2.enrage", null, Kit.Do(Kit.Rampart));
        var p3 = Kit.Entry("p3.raidwide", null, Kit.Do(Kit.Rampart)).AsPhaseEnd();
        var s = Scheduler(p1, p2, p3);
        var byEntry = s.All.ToDictionary(r => r.Entry);

        s.OnTimelineJump(double.NegativeInfinity, Math.BitDecrement(400.0));

        Assert.Equal(ReminderState.Skipped, byEntry[p1].State);
        Assert.Equal(ReminderState.Skipped, byEntry[p2].State);
        Assert.Equal(ReminderState.Pending, byEntry[p3].State);
        Assert.All(s.All, r => Assert.Null(r.PhaseEndDueAt));

        var noPhases = Kit.Scheduler(Kit.Entry("p1.buster", null, Kit.Do(Kit.Rampart)).AsPhaseEnd());
        noPhases.OnTimelineJump(double.NegativeInfinity, 200);
        Assert.Equal(ReminderState.Skipped, noPhases.All[0].State);
    }

    [Fact]
    public void DueReminderOfAnEarlierPhase_IsNotCaughtByTheNextPhasesJump()
    {
        // P1 ended without a jump (a push of under 5 s); the when-ready reminder is still waiting when P2 is pushed.
        var s = Scheduler(RampartAsP1Dies().AsWhenReady());
        var r = s.All[0];
        var cooldowns = new FakeCooldowns().Set(Kit.Rampart, 120);

        s.Update(133, cooldowns);
        s.Update(210, cooldowns);
        Assert.Equal(ReminderState.Due, r.State);
        s.OnTimelineJump(215, 400);

        Assert.Null(r.PhaseEndDueAt);
        Assert.Equal(139.7, r.Latest, Kit.Tolerance);
        Assert.Equal(ReminderState.Due, r.State);           // left alone, like any due reminder
    }

    [Fact]
    public void PhaseEndReminder_ThatCanStillBeDoneInTime_IsLeftAsPlanned()
    {
        // Anchored to the P2 raidwide (250) but pressed at 198: the jump to 200 neither passes its hit nor its window.
        var s = Scheduler(Kit.Entry("p2.raidwide", -52, Kit.Do(Kit.Rampart)).AsPhaseEnd());
        var due = Kit.RecordDue(s);
        var r = s.All[0];

        s.OnTimelineJump(120, 200);
        Assert.Equal(ReminderState.Pending, r.State);
        Assert.Null(r.PhaseEndDueAt);

        s.Update(200);
        Assert.Equal(ReminderState.Due, r.State);
        Assert.Equal(249.7, r.Latest, Kit.Tolerance);       // its own window, not the grace
        Assert.Equal([r], due);
    }

    [Fact]
    public void JumpThatDoesNotReachThePressTime_LeavesItAlone()
    {
        var s = Scheduler(Kit.Entry("p2.start", 3.4, Kit.Do(Kit.Rampart)).AsPhaseEnd()); // press 203.4
        s.OnTimelineJump(120, 201);
        Assert.Equal(ReminderState.Pending, s.All[0].State);
        Assert.Null(s.All[0].PhaseEndDueAt);
    }

    [Fact]
    public void FightWithoutPhases_AnyJumpPastItCatchesIt()
    {
        var s = Kit.Scheduler(Kit.Entry("p1.buster", null, Kit.Do(Kit.Rampart)).AsPhaseEnd(), Kit.Entry("p1.buster", null, Kit.Do(Kit.Temperance)));
        var (rampart, temperance) = (s.All[0], s.All[1]);

        s.OnTimelineJump(100, 140);                         // past the 130 buster

        Assert.Equal(ReminderState.Due, rampart.State);
        Assert.Equal(148, rampart.Latest, Kit.Tolerance);
        Assert.Equal(ReminderState.Skipped, temperance.State);
    }

    [Fact]
    public void FinishedPhaseEndReminders_AreNotTouched()
    {
        var done = RampartAsP1Dies();
        var missed = Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Temperance)).AsPhaseEnd(); // press 94, latest 99.7
        var s = Scheduler(done, missed);
        var byEntry = s.All.ToDictionary(r => r.Entry);

        s.Update(110);                                      // missed at 110
        s.OnOwnAction(Kit.Rampart.Id, 119);                 // done early
        s.OnTimelineJump(120, 200);

        Assert.Equal(ReminderState.Done, byEntry[done].State);
        Assert.Equal(ReminderState.Missed, byEntry[missed].State);
        Assert.All(s.All, r => Assert.Null(r.PhaseEndDueAt));
    }

    [Fact]
    public void OptionalPhaseEndEntry_IsPassedAfterTheGrace()
    {
        var s = Scheduler(RampartAsP1Dies().AsOptional());
        var r = s.All[0];

        s.OnTimelineJump(120, 200);
        s.Update(200);
        Assert.Equal(ReminderState.Due, r.State);
        s.Update(208.5);
        Assert.Equal(ReminderState.Passed, r.State);
        Assert.Equal(0, s.Summarize().Total);
    }

    [Fact]
    public void AReminderIsCaughtOnce_ALaterJumpCatchesTheNextPhase()
    {
        var p2 = Kit.Entry("p2.enrage", null, Kit.Do(Kit.Rampart)); // press 344
        var s = Scheduler(RampartAsP1Dies(), p2);
        var due = Kit.RecordDue(s);
        var (p1End, p2End) = (s.All[0], s.All[1]);

        s.OnTimelineJump(120, 200);
        s.Update(200);
        s.OnTimelineJump(201, 400);                         // P2 pushed right away: P1's reminder is not extended again

        Assert.Equal(208, p1End.Latest, Kit.Tolerance);
        Assert.Equal(200, p1End.PhaseEndDueAt);
        Assert.Equal(ReminderState.Due, p2End.State);
        Assert.Equal(400, p2End.PhaseEndDueAt);

        s.Update(400);
        Assert.Equal(ReminderState.Missed, p1End.State);
        Assert.Equal(ReminderState.Due, p2End.State);
        Assert.Equal([p1End, p2End], due);
    }

    [Fact]
    public void WhenReadyPhaseEndEntry_KeepsWaitingForTheCooldownAfterTheTransition()
    {
        var s = Scheduler(RampartAsP1Dies().AsWhenReady());
        var r = s.All[0];
        var cooldowns = new FakeCooldowns();

        s.Update(133, cooldowns.Set(Kit.Rampart, 60));     // due, on cooldown
        s.OnTimelineJump(135, 200);
        Assert.Equal(208, r.Latest, Kit.Tolerance);

        s.Update(215, cooldowns.Set(Kit.Rampart, 5));      // past the grace, still waiting
        Assert.Equal(ReminderState.Due, r.State);
        s.Update(220, cooldowns.Set(Kit.Rampart, 0));      // ready: max(208, 220 + 5)
        s.Update(225, cooldowns);
        Assert.Equal(ReminderState.Due, r.State);
        s.Update(225.01, cooldowns);
        Assert.Equal(ReminderState.Missed, r.State);
    }

    [Fact]
    public void Reset_RestoresThePlannedWindow_AndTheNextPullCatchesItAgain()
    {
        var s = Scheduler(RampartAsP1Dies());
        var due = Kit.RecordDue(s);
        var r = s.All[0];

        s.OnTimelineJump(120, 200);
        s.Update(200);
        s.Reset();

        Assert.Equal(ReminderState.Pending, r.State);
        Assert.Equal(139.7, r.Latest, Kit.Tolerance);
        Assert.Null(r.PhaseEndDueAt);

        s.Update(125);                                      // the next pull, pushed later
        s.OnTimelineJump(126, 200);
        Assert.Equal(ReminderState.Due, r.State);
        Assert.Equal(208, r.Latest, Kit.Tolerance);
        s.Update(200);
        Assert.Equal([r, r], due);
    }

    // ---- backward jumps after a transition -------------------------------------------------------

    [Fact]
    public void Rewind_StillAfterItsDueTime_KeepsItDueWithTheGrace()
    {
        var s = Scheduler(RampartAsP1Dies());
        var due = Kit.RecordDue(s);
        var r = s.All[0];

        s.OnTimelineJump(120, 200);
        s.Update(207);
        s.OnTimelineJump(207, 190);                         // 17 s back, after press − due lead (132.5)

        Assert.Equal(ReminderState.Due, r.State);
        Assert.Equal(208, r.Latest, Kit.Tolerance);
        Assert.Equal(200, r.PhaseEndDueAt);
        s.Update(190);
        s.Update(208);
        Assert.Equal(ReminderState.Due, r.State);
        Assert.Equal([r], due);                             // unbroken NOW, no second sound
    }

    [Fact]
    public void Rewind_BeforeItsDueTime_RestoresThePlan_AndTheTransitionCatchesItAgain()
    {
        var s = Scheduler(RampartAsP1Dies());
        var due = Kit.RecordDue(s);
        var r = s.All[0];

        s.OnTimelineJump(120, 200);
        s.Update(205);
        s.OnTimelineJump(205, 125);                         // back before 132.5

        Assert.Equal(ReminderState.Pending, r.State);
        Assert.Equal(139.7, r.Latest, Kit.Tolerance);
        Assert.Null(r.PhaseEndDueAt);

        s.Update(125);
        Assert.Equal(ReminderState.Upcoming, r.State);
        s.OnTimelineJump(127, 200);                         // the transition again
        Assert.Equal(ReminderState.Due, r.State);
        Assert.Equal(208, r.Latest, Kit.Tolerance);
        s.Update(200);
        Assert.Equal([r, r], due);
    }

    [Fact]
    public void Rewind_IntoTheGrace_ReopensAMissedPhaseEndReminder()
    {
        var s = Scheduler(RampartAsP1Dies());
        var due = Kit.RecordDue(s);
        var r = s.All[0];

        s.OnTimelineJump(120, 200);
        s.Update(200);
        s.Update(214);                                      // missed at 214
        Assert.Equal(ReminderState.Missed, r.State);
        s.OnTimelineJump(214, 202);                         // back into the grace

        Assert.Equal(ReminderState.Pending, r.State);
        Assert.Equal(208, r.Latest, Kit.Tolerance);         // the transition stands
        s.Update(202);
        Assert.Equal(ReminderState.Due, r.State);
        Assert.Equal([r, r], due);                          // a second chance, announced again
    }

    [Fact]
    public void Rewind_PastTheGrace_KeepsTheOutcome()
    {
        var s = Scheduler(RampartAsP1Dies());
        var r = s.All[0];

        s.OnTimelineJump(120, 200);
        s.Update(200);
        s.Update(220);
        s.OnTimelineJump(220, 210);

        Assert.Equal(ReminderState.Missed, r.State);
        Assert.Equal(208, r.Latest, Kit.Tolerance);
        Assert.Empty(s.Update(210));                        // its row is from before the rewind
    }
}
