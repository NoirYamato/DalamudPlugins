using MitCue.Core;
using MitCue.Core.Data;
using MitCue.Core.Events;
using MitCue.Core.Fights;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;
using MitCue.Core.Simulation;
using MitCue.Core.Timelines;
using MitCue.Data;
using MitCue.PluginTests.Support;
using MitCue.Runtime;

namespace MitCue.PluginTests.Integration;

/// <summary>
/// Phase-end reminders (DESIGN §5.2) through the plugin's <see cref="EncounterSession"/> on the real TOP timeline: the
/// session forwards the sync of a phase push (<see cref="SyncResult.IsJump"/>) to the scheduler, which makes the
/// phase-end calls of the phase it ends due at the jump, and the sound request (BecameDue) comes in that frame. A sync
/// that starts the stopped timeline is forwarded from −∞, so it never ends a phase.
/// </summary>
public class PhaseEndJumpTests
{
    private const double Pull = 1000;
    private const double Frame = 0.05;
    private const uint Rampart = 7531;
    private const uint DeploymentTactics = 3585;

    [CoreFact]
    public void APushOutOfP3_MakesTheP3PhaseEndCallDueInTheJumpFrame_AndSkipsTheMechanicItJumpedOver()
    {
        var (top, timeline) = Top();
        var deploy = Kit.Entry("p3.ion_efflux", -4, DeploymentTactics, "Deployment Tactics", Job.SCH); // press 583.4
        deploy.AtPhaseEnd = true;                                                                     // "just before P3 ends"
        var soil = Kit.Entry("p3.monitors", -4.5, Kit.SacredSoil, "Sacred Soil", Job.SCH);             // press 566.9, hit 571.4
        var session = Session(top, timeline, deploy, soil);
        var (rDeploy, rSoil) = (Reminder(session, deploy), Reminder(session, soil));

        var options = new SimulationOptions { PullClock = Pull, Seed = 11 };
        options.PhaseCutoff["p3"] = 560;            // P3 dies at 560...
        options.PhaseRealStart["p4"] = 565;         // ...and the P4 sync (600.0) comes 5 s later
        var due = new List<(double TimelineTime, Reminder Reminder)>();
        session.BecameDue += (s, r) => due.Add((s.TimelineTime, r));
        ReminderState? deployAtJump = null, soilAtJump = null;
        SyncResult? push = null;

        Run(session, FightSimulator.Generate(timeline, top, options), until: Pull + 580, afterEvents: () =>
        {
            // Right after the frame's events, before the frame's update: the state the jump itself left.
            if (session.Jumps == 1 && push == null)
            {
                push = Enumerable.Range(0, session.Syncs.Count).Select(i => session.Syncs[i]).Last(s => s.IsJump);
                (deployAtJump, soilAtJump) = (rDeploy.State, rSoil.State);
            }
        });

        Assert.NotNull(push);
        Assert.Equal(600, push.ToTime);
        Assert.InRange(push.FromTime, 564.5, 565.5);
        Assert.Equal(1, session.Jumps);
        Assert.Equal(ReminderState.Due, deployAtJump);      // due as soon as the session forwarded the jump
        Assert.Equal(ReminderState.Skipped, soilAtJump);
        Assert.Equal(600, rDeploy.PhaseEndDueAt);
        Assert.Equal(608, rDeploy.Latest, 1e-9);

        var sound = Assert.Single(due, d => d.Reminder == rDeploy);
        Assert.InRange(sound.TimelineTime, 600, 600 + Frame);
        Assert.DoesNotContain(due, d => d.Reminder == rSoil);
        Assert.Equal(ReminderState.Missed, rDeploy.State); // nobody pressed it within the grace
        Assert.InRange(rDeploy.FinishedAt!.Value, 608, 608.7);
    }

    [CoreFact]
    public void APhasePreviewFromP3_NeverAnnouncesTheEarlierPhasesPhaseEndCalls()
    {
        // The preview skips everything before P3 (SkipBefore, a jump from −∞), then jumps from P1 to the P3 sync.
        // Neither jump ends P1 or P2, so their phase-end calls are skipped, not made due at the preview's start.
        var (top, timeline) = Top();
        var p1End = Kit.Entry("p1.atomic_ray", -6, Rampart, "Rampart", Job.SCH);          // enrage anchor: press 121.7
        var p2End = Kit.Entry("p2.laser_shower", -6, Rampart, "Rampart", Job.SCH);        // enrage anchor: press 341.4
        p2End.AtPhaseEnd = true;
        var p3 = Kit.Entry("p3.hello_world", -5, Kit.SacredSoil, "Sacred Soil", Job.SCH); // press 436.3
        var session = Session(top, timeline, p1End, p2End, p3);
        var due = new List<Reminder>();
        session.BecameDue += (_, r) => due.Add(r);
        session.SkipBefore(400);

        var options = new SimulationOptions { PullClock = Pull, Seed = 12 }; // as PreviewDriver starts a preview at P3
        options.PhaseCutoff["p1"] = 0.5;
        options.PhaseCutoff["p2"] = 199.999;
        options.PhaseRealStart["p3"] = 2;
        Run(session, FightSimulator.Generate(timeline, top, options), until: Pull + 40, afterEvents: null);

        Assert.True(session.Jumps >= 1);
        Assert.InRange(session.TimelineTime, 430, 445);
        Assert.Equal(ReminderState.Skipped, Reminder(session, p1End).State);
        Assert.Equal(ReminderState.Skipped, Reminder(session, p2End).State);
        Assert.All(session.Scheduler.All, r => Assert.Null(r.PhaseEndDueAt));
        Assert.Equal([Reminder(session, p3)], due);         // only the P3 call is announced
    }

    [CoreFact]
    public void APullJoinedDuringP1_TheP2SyncStartsTheTimeline_AndNoP1CallIsAnnouncedOrMissed()
    {
        // Loaded in combat during P1 (a reconnect or a plugin reload: no InCombat edge), the stopped timeline is started by
        // the first sync whose window contains 0: the P2 Firewall cast (7B40 at 203.4, window 210,5). Its SyncResult says
        // FromTime 0, but the session forwards a start as a jump from −∞: P1 did not end there, so the P1 enrage call is
        // skipped instead of turning NOW with a sound, and the pre-pull call is skipped instead of counted missed.
        var (top, timeline) = Top();
        var p1End = Kit.Entry("p1.atomic_ray", -6, Rampart, "Rampart", Job.SCH);          // enrage anchor: press 121.7
        var prePull = Kit.Entry("p1.pull", -5, Kit.SacredSoil, "Sacred Soil", Job.SCH);    // press −5
        var p2 = Kit.Entry("p2.solar_ray", -4.5, Kit.SacredSoil, "Sacred Soil", Job.SCH);  // press 210: after the start
        var session = Session(top, timeline, p1End, prePull, p2);
        var due = new List<(double TimelineTime, Reminder Reminder)>();
        session.BecameDue += (s, r) => due.Add((s.TimelineTime, r));

        for (var clock = Pull; clock < Pull + 1; clock += Frame)
            session.Update(clock, null, inCombat: true, null);     // in combat, timeline stopped
        session.Process(new CastStarted(Pull + 1, 0x7B40, 0x40000001, "Omega", 3f, 0x40000001));
        for (var clock = Pull + 1; clock <= Pull + 10; clock += Frame)
            session.Update(clock, null, inCombat: true, null);

        var start = Enumerable.Range(0, session.Syncs.Count).Select(i => session.Syncs[i]).Single();
        Assert.True(start.Started);
        Assert.Equal((0.0, 203.4), (start.FromTime, start.ToTime));
        Assert.Equal(1, session.Jumps);
        Assert.True(session.PullActive);
        Assert.InRange(session.TimelineTime, 212, 213);

        var rP1End = Reminder(session, p1End);
        Assert.Equal(ReminderState.Skipped, rP1End.State);
        Assert.Null(rP1End.PhaseEndDueAt);
        Assert.Equal(ReminderState.Skipped, Reminder(session, prePull).State);
        var sound = Assert.Single(due);                            // only the P2 call, at its own time
        Assert.Equal(Reminder(session, p2), sound.Reminder);
        Assert.InRange(sound.TimelineTime, 208.5, 208.5 + Frame);
        var summary = session.Scheduler.Summarize();
        Assert.Equal((2, 0), (summary.Skipped, summary.Missed));
    }

    private static (FightDefinition Top, Timeline Timeline) Top()
    {
        var library = new FightLibrary(new EmbeddedDataSource());
        Assert.Empty(library.Problems);
        var top = library.Get("top")!;
        return (top, library.LoadTimeline(top));
    }

    private static EncounterSession Session(FightDefinition top, Timeline timeline, params PlanEntry[] own)
    {
        var plan = Kit.Plan(own);
        plan.Fight = "top";
        return new EncounterSession(new SessionSetup
        {
            Fight = top,
            Timeline = timeline,
            Plan = plan,
            Me = new PlayerContext(Job.SCH, 90),
            OwnEntries = own,
            Actions = EmptyActionCatalog.Instance,
            Options = new ReminderOptions(),
            SyncOptions = new SyncMatchOptions { MatchNames = true }, // simulated names come from the timeline
        });
    }

    private static Reminder Reminder(EncounterSession session, PlanEntry entry) => session.Scheduler.All.Single(r => r.Entry == entry);

    /// <summary>Frames like SessionManager: the frame's events first, then <see cref="EncounterSession.Update"/>.</summary>
    private static void Run(EncounterSession session, IReadOnlyList<GameEvent> events, double until, Action? afterEvents)
    {
        var next = 0;
        var inCombat = false;
        for (var clock = Pull - 1; clock <= until; clock += Frame)
        {
            while (next < events.Count && events[next].Time <= clock)
            {
                var e = events[next++];
                if (e is CombatStateChanged c)
                    inCombat = c.InCombat;
                session.Process(e);
            }

            afterEvents?.Invoke();
            session.Update(clock, null, inCombat, null);
        }
    }
}
