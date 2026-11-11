using MitCue.Core.Actions;
using MitCue.Core.Events;
using MitCue.Core.Fights;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;
using MitCue.Core.Simulation;
using MitCue.Core.Tests.Data;
using MitCue.Core.Timelines;

namespace MitCue.Core.Tests.Reminders;

/// <summary>
/// Simulated TOP pulls with a phase push, played through the sync engine and the scheduler the way the plugin's
/// <c>EncounterSession</c> drives them: the frame's events first, then a tick and a scheduler update every 50 ms, and
/// every sync with <see cref="SyncResult.IsJump"/> forwarded to <see cref="ReminderScheduler.OnTimelineJump"/> (a sync
/// that started the stopped timeline from −∞).
/// The fight, its timeline and the action catalog are the bundled data files.
/// </summary>
public class ReminderTopPhasePushTests
{
    private const double Frame = 0.05;

    private static readonly Lazy<(FightDefinition Fight, Timeline Timeline)> TopData = new(() =>
    {
        var library = new FightLibrary(RepoFiles.Data);
        Assert.Empty(library.Problems);
        var fight = library.Get("top") ?? throw new InvalidOperationException("data/fights/top.yaml did not load");
        return (fight, library.LoadTimeline(fight));
    });

    private static ActionInfo Deploy => RepoFiles.Action("Deployment Tactics");

    private static ActionInfo SacredSoil => RepoFiles.Action("Sacred Soil");

    private static ActionInfo Expedient => RepoFiles.Action("Expedient");

    private static ActionInfo FeyIllumination => RepoFiles.Action("Fey Illumination");

    /// <summary>
    /// P3 dies at 560 and the P4 sync (Ability 7B47, 600.0) comes 5 s later, so the timeline jumps from about 565 to
    /// 600 over Oversampled Wave Cannon (571.4) and the Ion Efflux enrage (587.4).
    /// </summary>
    [Theory]
    [InlineData(1, 0.15, false)]
    [InlineData(2, 0.25, false)]
    [InlineData(3, 0.0, false)]
    [InlineData(4, 0.15, true)]
    public void AtPhaseEndReminderInP3_BecomesDueExactlyAtThePushToP4_WhileANormalOneIsSkipped(int seed, double jitter, bool pressDeploy)
    {
        var (fight, timeline) = TopData.Value;
        var deploy = Entry("p3.ion_efflux", -4, Deploy).AsPhaseEnd();     // Carry the Clear: "Deploy (≈3:04, just before P3 ends)", press 583.4
        var shake = Entry("p3.ion_efflux", -2, null, "Shake as P3 dies"); // LPDU P3 note, anchored to the enrage (not flagged): press 585.4
        var soil = Entry("p3.monitors", null, SacredSoil);                 // normal: press 566.9 for the 571.4 monitors
        var expedient = Entry("p3.critical_error", null, Expedient);       // normal, before the push: press 544.1
        var fey = Entry("p4.wave_cannon", null, FeyIllumination);          // normal, after the push: press 615.4
        var options = new SimulationOptions { Seed = seed, Jitter = jitter };
        options.PhaseCutoff["p3"] = 560;
        options.PhaseRealStart["p4"] = 565;

        var pull = new Pull(fight, timeline, FightSimulator.Simulate(timeline, fight, options), deploy, shake, soil, expedient, fey);
        pull.PressAt(544.5, Expedient);
        if (pressDeploy)
            pull.PressAt(603, Deploy);
        pull.Run(until: 640);

        var (rDeploy, rShake, rSoil, rExpedient, rFey) = (pull[deploy], pull[shake], pull[soil], pull[expedient], pull[fey]);
        Assert.Equal(583.4, rDeploy.PressAt, 1e-9);
        Assert.Equal(566.9, rSoil.PressAt, 1e-9);

        // The push is one jump, from about 565 to 600, forwarded like EncounterSession does.
        var push = Assert.Single(pull.Jumps);
        Assert.Equal(600, push.Result.ToTime);
        Assert.InRange(push.Result.FromTime, 564.4, 565.6);
        Assert.Equal("Ability", push.Result.Entry.Sync!.Type);                       // 7B47 (BossMod P3End), window 600,0
        Assert.Equal(["7B47"], push.Result.Entry.Sync.Fields["id"].Values);

        // At the jump itself: the phase-end reminders are due, the normal one in the skipped span is skipped.
        Assert.Equal(ReminderState.Due, push.States[rDeploy]);
        Assert.Equal(ReminderState.Due, push.States[rShake]);
        Assert.Equal(ReminderState.Skipped, push.States[rSoil]);
        Assert.Equal(ReminderState.Done, push.States[rExpedient]);
        Assert.Equal(ReminderState.Pending, push.States[rFey]);
        Assert.Equal(600, rDeploy.PhaseEndDueAt);
        Assert.Equal(608, rDeploy.Latest, 1e-9);           // 600 + max(heal late window 5, 8)
        Assert.Equal(600, rSoil.FinishedAt);

        // Announced once, by the first update after the jump (timeline 600.00–600.05).
        var deployDue = Assert.Single(pull.Due, d => d.Reminder == rDeploy);
        Assert.InRange(deployDue.Time, 600, 600 + Frame);
        Assert.InRange(Assert.Single(pull.Due, d => d.Reminder == rShake).Time, 600, 600 + Frame);
        Assert.DoesNotContain(pull.Due, d => d.Reminder == rSoil);

        // Normal reminders before and after the push work as usual.
        Assert.Equal(ReminderState.Done, rExpedient.State);
        Assert.InRange(Assert.Single(pull.Due, d => d.Reminder == rExpedient).Time, 542.6, 542.6 + Frame + 0.6);
        Assert.InRange(Assert.Single(pull.Due, d => d.Reminder == rFey).Time, 613.9, 613.9 + Frame + 0.6);
        Assert.Equal(ReminderState.Missed, rFey.State);

        if (pressDeploy)
        {
            Assert.Equal(ReminderState.Done, rDeploy.State);
            Assert.InRange(rDeploy.UsedAt!.Value, 603, 603 + Frame);
            Assert.False(rDeploy.UsedEarly);
        }
        else
        {
            Assert.Equal(ReminderState.Missed, rDeploy.State);
            Assert.InRange(rDeploy.FinishedAt!.Value, 608, 608 + Frame + 0.6); // the 607.1 sync may correct the time a little
        }

        Assert.Equal(ReminderState.Passed, rShake.State);   // a note: passed, never missed
        Assert.Equal(ReminderState.Skipped, rSoil.State);
        var summary = pull.Scheduler.Summarize();
        Assert.Equal(1, summary.Skipped);
        Assert.Equal(pressDeploy ? 2 : 1, summary.Done);
        Assert.Equal(pressDeploy ? 1 : 2, summary.Missed);
    }

    /// <summary>The push comes after the reminder became due (P3 dies at 583): it stays due through the transition.</summary>
    [Fact]
    public void AtPhaseEndReminderAlreadyDue_StaysDueThroughThePush_WithoutASecondSound()
    {
        var (fight, timeline) = TopData.Value;
        var deploy = Entry("p3.ion_efflux", -4, Deploy).AsPhaseEnd();
        var normal = Entry("p3.monitors", 12, SacredSoil);                // also press 583.4, not a phase-end entry (latest 588.4)
        var options = new SimulationOptions { Seed = 5 };
        options.PhaseCutoff["p3"] = 583;
        options.PhaseRealStart["p4"] = 584.5;

        var pull = new Pull(fight, timeline, FightSimulator.Simulate(timeline, fight, options), deploy, normal);
        pull.Run(until: 620);

        var push = Assert.Single(pull.Jumps);
        Assert.Equal(600, push.Result.ToTime);
        Assert.Equal(ReminderState.Due, push.States[pull[deploy]]);
        Assert.Equal(ReminderState.Due, push.States[pull[normal]]); // left alone by the jump...

        Assert.InRange(Assert.Single(pull.Due, d => d.Reminder == pull[deploy]).Time, 581.9, 581.9 + Frame + 0.6); // the only sound
        Assert.Equal(608, pull[deploy].Latest, 1e-9);
        Assert.Equal(ReminderState.Missed, pull[deploy].State);
        Assert.InRange(pull[deploy].FinishedAt!.Value, 608, 608 + Frame + 0.6);
        Assert.Equal(ReminderState.Missed, pull[normal].State);
        Assert.InRange(pull[normal].FinishedAt!.Value, 600, 600 + Frame); // ...and missed by the first update after it
    }

    /// <summary>
    /// A P1 push: the "Rampart as P1 dies" reminder (LPDU, anchored to the Atomic Ray enrage) becomes due where the
    /// timeline enters P2. That is the P2 start (200.0, a MapEffect sync) when map effects are seen, as in the simulation,
    /// or the Firewall cast (203.4, window 210,5) without them, as with the plugin's v1 event source.
    /// </summary>
    [Theory]
    [InlineData(true, 200.0)]
    [InlineData(false, 203.4)]
    public void P1Push_MakesTheP1PhaseEndReminderDueWhereTheTimelineEntersP2(bool mapEffects, double p2Sync)
    {
        var (fight, timeline) = TopData.Value;
        var rampart = Entry("p1.atomic_ray", null, RepoFiles.Action("Rampart"));          // enrage anchor, not flagged: press 121.7
        var normal = Entry("p1.wave_cannon_kyrios", null, RepoFiles.Action("Reprisal"));  // press 105 for the 108.0 hit
        var options = new SimulationOptions { Seed = 6 };
        options.PhaseCutoff["p1"] = 90;
        options.PhaseRealStart["p2"] = 100;
        var events = FightSimulator.Simulate(timeline, fight, options).Where(e => mapEffects || e.Event is not MapEffectOccurred).ToList();

        var pull = new Pull(fight, timeline, events, rampart, normal);
        pull.Run(until: 230);

        var push = Assert.Single(pull.Jumps);
        Assert.Equal(p2Sync, push.Result.ToTime, 1e-9);
        Assert.Equal(ReminderState.Due, push.States[pull[rampart]]);
        Assert.Equal(ReminderState.Skipped, push.States[pull[normal]]);
        Assert.Equal(p2Sync + 8, pull[rampart].Latest, 1e-9);
        Assert.InRange(Assert.Single(pull.Due, d => d.Reminder == pull[rampart]).Time, p2Sync, p2Sync + Frame);
        Assert.Equal(ReminderState.Missed, pull[rampart].State);
    }

    private static PlanEntry Entry(string mechanic, double? offset, ActionInfo? action, string? note = null) => new()
    {
        Id = $"{mechanic}@{offset}:{action?.Name ?? note}",
        Mechanic = mechanic,
        Offset = offset,
        Do = [action != null ? PlanItem.ForAction(action.Id, action.Name) : PlanItem.ForNote(note ?? "note")],
    };

    /// <summary>One jump forwarded to the scheduler, with every reminder's state right after it.</summary>
    private sealed record Jump(SyncResult Result, IReadOnlyDictionary<Reminder, ReminderState> States);

    /// <summary>A pull driven like the plugin's EncounterSession, recording jumps and BecameDue (with the timeline time).</summary>
    private sealed class Pull
    {
        private readonly IReadOnlyList<SimulatedEvent> events;
        private readonly TimelinePlayer player;
        private readonly List<(double Time, ActionInfo Action)> presses = [];
        private double now;

        public Pull(FightDefinition fight, Timeline timeline, IReadOnlyList<SimulatedEvent> events, params PlanEntry[] entries)
        {
            this.events = events;
            this.player = new TimelinePlayer(timeline) { StopOnCombatEnd = false };
            this.Scheduler = new ReminderScheduler(fight, entries, RepoFiles.Actions, new ReminderOptions());
            Assert.Empty(this.Scheduler.Problems);
            Assert.Equal(entries.Length, this.Scheduler.All.Count);

            this.player.Synced += r =>
            {
                if (r.Stopped || !r.IsJump)
                    return;
                this.Scheduler.OnTimelineJump(r.Started ? double.NegativeInfinity : r.FromTime, r.ToTime); // a start comes from −∞
                this.Jumps.Add(new Jump(r, this.Scheduler.All.ToDictionary(x => x, x => x.State)));
            };
            this.Scheduler.BecameDue += r => this.Due.Add((r, this.now));
        }

        public ReminderScheduler Scheduler { get; }

        public List<Jump> Jumps { get; } = [];

        public List<(Reminder Reminder, double Time)> Due { get; } = [];

        public Reminder this[PlanEntry entry] => this.Scheduler.All.Single(r => r.Entry == entry);

        /// <summary>The player presses <paramref name="action"/> at the first frame at or after timeline time <paramref name="time"/>.</summary>
        public void PressAt(double time, ActionInfo action) => this.presses.Add((time, action));

        public void Run(double until)
        {
            var next = 0;
            var clock = this.events[0].Event.Time - 1;
            while (!(this.player.IsRunning && this.player.TimeAt(clock) >= until))
            {
                clock += Frame;
                for (; next < this.events.Count && this.events[next].Event.Time <= clock; next++)
                    this.player.Process(this.events[next].Event);
                Assert.True(next < this.events.Count || this.player.IsRunning, "the simulation ended before the pull did");

                this.player.Tick(clock);
                if (!this.player.IsRunning)
                    continue;
                this.now = this.player.TimeAt(clock);
                for (var i = this.presses.Count - 1; i >= 0; i--)
                {
                    if (this.presses[i].Time <= this.now)
                    {
                        this.Scheduler.OnOwnAction(this.presses[i].Action.Id, this.now);
                        this.presses.RemoveAt(i);
                    }
                }

                this.Scheduler.Update(this.now);
            }
        }
    }
}
