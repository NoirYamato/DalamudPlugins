using MitCue.Core.Events;
using MitCue.Core.Reminders;
using MitCue.PluginTests.Support;
using MitCue.Runtime;

namespace MitCue.PluginTests;

/// <summary>How a live session starts and ends pulls (DESIGN §4) and how phase previews start mid-fight.</summary>
public class EncounterSessionTests
{
    private const double Pull = 1000;

    private static (EncounterSession Session, ScriptedSyncEngine Engine, SessionDriver Driver) Pulled(params Core.Plans.PlanEntry[] own)
    {
        var engine = new ScriptedSyncEngine();
        var session = Kit.Session(Kit.Fight(), own, engine);
        var driver = new SessionDriver(session, Pull);
        driver.CombatEdge(true);
        Assert.True(session.PullActive);
        return (session, engine, driver);
    }

    [Fact]
    public void CombatFlagDropWhileTheBossFightsOnKeepsThePull()
    {
        // Review finding: a dead player loses InCombat while the fight goes on. The pull must not end, the timeline
        // must not stop (and restart at 0 after the raise), and later calls must still come on time.
        var (session, engine, driver) = Pulled(
            Kit.Entry("p1.raid", -5, Kit.Temperance, "Temperance"),
            Kit.Entry("p2.late", -5, Kit.PlenaryIndulgence, "Plenary Indulgence"));
        driver.BossCastEvery = 3;
        driver.RunTo(Pull + 450);

        driver.CombatEdge(false); // dead
        driver.RunTo(Pull + 458);
        driver.CombatEdge(true); // raised
        driver.RunTo(Pull + 462);

        Assert.Empty(driver.Ended);
        Assert.True(session.PullActive);
        Assert.True(engine.IsRunning);
        Assert.InRange(session.TimelineTime, 461.5, 462.5);
        Assert.DoesNotContain(engine.Processed, e => e is CombatStateChanged { InCombat: false });

        // The late call (press 455) turned NOW on time while the flag was down; the P1 call never came back.
        Assert.Contains(driver.Due, d => d.Reminder.Mechanic.Id == "p2.late" && d.Clock >= Pull + 453 && d.Clock < Pull + 454);
        Assert.Single(driver.Due, d => d.Reminder.Mechanic.Id == "p1.raid");
    }

    [Fact]
    public void ProlongedOutOfCombatWithoutEnemyActivityEndsThePullAfterTenSeconds()
    {
        var (session, _, driver) = Pulled(Kit.Entry("p1.raid", -5, Kit.Temperance, "Temperance"));
        driver.BossCastEvery = 3;
        driver.RunTo(Pull + 100);
        driver.BossCastEvery = null;

        driver.CombatEdge(false);
        driver.RunTo(Pull + 109);
        Assert.Empty(driver.Ended);

        driver.RunTo(Pull + 115);
        var ended = Assert.Single(driver.Ended);
        Assert.Equal("out of combat", ended.Reason);
        Assert.InRange(ended.Clock, Pull + 110, Pull + 110.5);
        Assert.False(session.PullActive);
    }

    [Fact]
    public void WipeEndsThePullAtOnceWithOneSummary()
    {
        var (session, _, driver) = Pulled(Kit.Entry("p1.raid", -5, Kit.Temperance, "Temperance"));
        driver.RunTo(Pull + 50);

        driver.Event(new DutyReset(driver.Clock, DutyResetKind.Wipe));
        driver.CombatEdge(false);
        driver.RunTo(Pull + 70);

        var ended = Assert.Single(driver.Ended);
        Assert.Equal("wipe", ended.Reason);
        Assert.Equal(Pull + 50, ended.Clock, 3);
        Assert.False(session.PullActive);
        Assert.Equal(SessionMode.Idle, session.Mode);
    }

    [Fact]
    public void CombatEndAfterTheTimelineStoppedByItselfEndsThePull()
    {
        var (session, engine, driver) = Pulled(Kit.Entry("p1.raid", -5, Kit.Temperance, "Temperance"));
        engine.EndTime = 100;
        driver.RunTo(Pull + 120);

        // Past the timeline's end but still fighting: the pull goes on with the time frozen.
        Assert.False(engine.IsRunning);
        Assert.True(session.PullActive);
        Assert.Equal(SessionMode.Running, session.Mode);
        Assert.Empty(driver.Ended);

        driver.CombatEdge(false);
        var ended = Assert.Single(driver.Ended);
        Assert.Equal("combat ended", ended.Reason);
    }

    [Fact]
    public void CombatStartAfterAPullEndedStartsTheNextPull()
    {
        var (session, engine, driver) = Pulled(Kit.Entry("p1.raid", -5, Kit.Temperance, "Temperance"));
        driver.RunTo(Pull + 20);
        driver.Event(new DutyReset(driver.Clock, DutyResetKind.Wipe));
        driver.CombatEdge(false);
        driver.RunTo(Pull + 60);

        driver.CombatEdge(true);
        Assert.True(session.PullActive);
        Assert.True(engine.IsRunning);
        Assert.InRange(session.TimelineTime, 0, 0.01);
        Assert.Equal(2, engine.Processed.Count(e => e is CombatStateChanged { InCombat: true }));
    }

    [Fact]
    public void PrePullCallsStayListedBetweenCountdownZeroAndThePull()
    {
        // A pre-pull Divine Benison (press 1 s before the pull) is still NOW when the countdown reaches 0.
        var engine = new ScriptedSyncEngine();
        var session = Kit.Session(Kit.Fight(), [Kit.Entry("p1.raid", -31, Kit.DivineBenison, "Divine Benison")], engine);
        for (var clock = 990.0; clock < 999.85; clock += 0.1)
            session.Update(clock, (float)(1000 - clock), false, null);
        session.Update(1000.0, null, false, null);

        Assert.Equal(SessionMode.Idle, session.Mode);
        var reminder = Assert.Single(session.Visible);
        Assert.Equal(ReminderState.Due, reminder.State);
    }

    [Fact]
    public void ACountdownKeepsTheSessionOnceACallIsShown()
    {
        // Review round 4: a countdown held every rebuild until the pull ended, also when nothing had been shown yet, so a
        // co-tank who showed up after /countdown left the pull on the first tank pair's column. Pre-pull Divine Benison:
        // press 1 s before the pull, shown 15 s before that.
        var engine = new ScriptedSyncEngine();
        var session = Kit.Session(Kit.Fight(), [Kit.Entry("p1.raid", -31, Kit.DivineBenison, "Divine Benison")], engine);
        Assert.False(session.IsBusy);

        session.Update(980, 20f, false, null);
        Assert.Equal(SessionMode.Countdown, session.Mode);
        Assert.Empty(session.Visible);
        Assert.False(session.IsBusy); // nothing to lose

        for (var clock = 980.1; clock < 984.5; clock += 0.1)
            session.Update(clock, (float)(1000 - clock), false, null);
        Assert.Single(session.Visible);
        Assert.True(session.IsBusy); // the call is shown: changes wait for the pull to end

        // Cancelled: nothing is held any more.
        session.Update(985, null, false, null);
        Assert.False(session.IsBusy);
    }

    [Fact]
    public void SkipBeforeMarksEarlierCallsSkippedSoNothingIsMissedAtThePull()
    {
        // Review finding: a phase preview starts the fight at that phase; the earlier calls were never shown.
        var engine = new ScriptedSyncEngine();
        var session = Kit.Session(
            Kit.Fight(),
            [
                Kit.Entry("p1.raid", -31, Kit.DivineBenison, "Divine Benison"), // pre-pull, press -1
                Kit.Entry("p1.raid", -5, Kit.Temperance, "Temperance"), // press 25
                Kit.Entry("p2.raid", -5, Kit.PlenaryIndulgence, "Plenary Indulgence"), // press 225
            ],
            engine);
        session.SkipBefore(200);

        var driver = new SessionDriver(session, Pull);
        driver.CombatEdge(true);
        driver.RunTo(Pull + 2);

        Assert.DoesNotContain(session.Visible, r => r.State == ReminderState.Missed);
        var states = session.Scheduler.All.ToDictionary(r => r.PressAt, r => r.State);
        Assert.Equal(ReminderState.Skipped, states[-1]);
        Assert.Equal(ReminderState.Skipped, states[25]);
        Assert.Equal(ReminderState.Pending, states[225]);
        var summary = session.Scheduler.Summarize();
        Assert.Equal(0, summary.Missed);
        Assert.Equal(2, summary.Skipped);
    }

    [Fact]
    public void SkipBeforeKeepsCallsPlannedAtThePhaseStart()
    {
        var session = Kit.Session(
            Kit.Fight(),
            [
                Kit.Entry("p1.pull", -5, Kit.DivineBenison, "Divine Benison"),
                Kit.Entry("p1.pull", 0, Kit.Temperance, "Temperance"),
            ],
            new ScriptedSyncEngine());
        session.SkipBefore(0);

        var states = session.Scheduler.All.ToDictionary(r => r.PressAt, r => r.State);
        Assert.Equal(ReminderState.Skipped, states[-5]);
        Assert.Equal(ReminderState.Pending, states[0]);
    }
}
