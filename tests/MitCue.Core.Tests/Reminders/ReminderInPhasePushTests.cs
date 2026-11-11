using MitCue.Core.Actions;
using MitCue.Core.Events;
using MitCue.Core.Fights;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;
using MitCue.Core.Tests.Data;
using MitCue.Core.Timelines;

namespace MitCue.Core.Tests.Reminders;

/// <summary>
/// Pushes that stay inside a phase of the fight file, on the bundled fights and timelines, driven like the plugin's
/// <c>EncounterSession</c>: a frame every 50 ms (tick, own presses, scheduler update) and every sync with
/// <see cref="SyncResult.IsJump"/> forwarded to <see cref="ReminderScheduler.OnTimelineJump"/> (a start from −∞).
/// </summary>
public class ReminderInPhasePushTests
{
    private const double Pull = 5000;
    private const double Frame = 0.05;
    private const uint Boss = 0x40000002;
    private const uint Tank = 0x10000001;

    /// <summary>Rounding of timeline times computed from the clock.</summary>
    private const double Eps = 1e-6;

    private static ActionInfo Rampart => RepoFiles.Action("Rampart");

    /// <summary>
    /// TEA P3 DPS check: killing Cruise Chaser and Brute Justice before the Eternal Darkness enrage (731.7, kind enrage)
    /// shows Alexander Prime's 4879 visual (BossMod DivineJudgmentSuccess), whose sync (window 67,5) makes the timeline
    /// jump to 771.9, still in the fight file's P3 (P4 starts at 900). That push ends the stage the enrage belongs to, so
    /// a call anchored to the enrage becomes Due at the jump (DESIGN §5.2): not skipped, and not missed when the adds die
    /// after it turned NOW.
    /// </summary>
    [Theory]
    [InlineData(705.0, false)]     // the adds die early: the call (press 725.7) is not even upcoming
    [InlineData(720.0, false)]     // upcoming
    [InlineData(720.0, true)]      // ...and the player presses Rampart after the push
    [InlineData(727.0, false)]     // the call turned NOW at 724.2: extended, not missed, no second sound
    public void TeaP3_KillingTheDpsCheckAdds_MakesTheEnrageCallDueAtThePush(double addsDie, bool press)
    {
        var (tea, timeline) = Load("tea");
        Assert.Equal(MechanicKind.Enrage, tea.GetMechanic("p3.eternal_darkness")!.Kind);
        Assert.Equal("p3", tea.PhaseAt(771.9)!.Id);
        var entry = Entry("p3.eternal_darkness", Rampart);                 // enrage anchor, not flagged
        var rig = new Rig(tea, timeline, entry);
        var r = rig.Scheduler.All[0];
        Assert.Equal(725.7, r.PressAt, 1e-9);                              // level 80: hit − 6
        Assert.Equal(731.4, r.Latest, 1e-9);
        if (press)
            rig.PressAt(775, Rampart);

        rig.Process(c => new CombatStateChanged(c, true));                 // the pull: timeline 0
        rig.RunTo(470);
        rig.Process(c => new CastStarted(c, 0x485A, Boss, "Alexander Prime", 8f, Boss)); // P2 → P3 push to 492.0
        rig.RunTo(addsDie);
        rig.Process(c => new AbilityResolved(c, 0x4879, Boss, "Alexander Prime", false, [Tank]));
        var push = rig.Jumps[^1];
        rig.RunTo(790);

        Assert.Equal(2, rig.Jumps.Count);
        Assert.Equal("Divine Judgment Enrage", push.Result.Entry.Name);
        InFrame(addsDie, push.Result.FromTime);
        Assert.Equal(771.9, push.Result.ToTime, 1e-9);
        Assert.Equal(ReminderState.Due, push.States[r]);                   // due at the push itself
        Assert.Equal(771.9, r.PhaseEndDueAt);
        Assert.Equal(779.9, r.Latest, 1e-9);                               // 771.9 + max(heal late window 5, 8)

        var sound = Assert.Single(rig.Due);
        var dueFrom = addsDie < 724.2 ? 771.9 : 724.2;
        InFrame(dueFrom, sound.Time);
        var summary = rig.Scheduler.Summarize();
        Assert.Equal(0, summary.Skipped);
        if (press)
        {
            Assert.Equal(ReminderState.Done, r.State);
            InFrame(775, r.UsedAt!.Value);
            Assert.False(r.UsedEarly);
            Assert.Equal((1, 0), (summary.Done, summary.Missed));
        }
        else
        {
            Assert.Equal(ReminderState.Missed, r.State);
            InFrame(779.9, r.FinishedAt!.Value);
            Assert.Equal((0, 1), (summary.Done, summary.Missed));
        }
    }

    /// <summary>
    /// TOP P1: an early Atomic Ray cast (7AF8, sync 122.7, window 20,20) moves the timeline forward inside P1, past the
    /// press time of the enrage call but not past the enrage hit (127.7) or the call's window (127.4). Nothing ended: the
    /// call keeps its planned window and is announced as usual.
    /// </summary>
    [Fact]
    public void TopP1_AnEarlyAtomicRayCastResync_LeavesTheEnrageCallAsPlanned()
    {
        var (top, timeline) = Load("top");
        var entry = Entry("p1.atomic_ray", Rampart);                       // enrage anchor: press 121.7
        var rig = new Rig(top, timeline, entry);
        var r = rig.Scheduler.All[0];

        rig.Process(c => new CombatStateChanged(c, true));
        rig.RunTo(104);
        rig.Process(c => new CastStarted(c, 0x7AF8, 0x40000001, "Omega", 5f, 0x40000001));
        var resync = Assert.Single(rig.Jumps);
        rig.RunTo(130);

        Assert.Equal(122.7, resync.Result.ToTime, 1e-9);
        Assert.Equal(ReminderState.Pending, resync.States[r]);
        Assert.Null(r.PhaseEndDueAt);
        Assert.Equal(127.4, r.Latest, 1e-9);
        InFrame(122.7, Assert.Single(rig.Due).Time);                       // the update of the resync's frame
        Assert.Equal(ReminderState.Missed, r.State);
        InFrame(127.4, r.FinishedAt!.Value);
    }

    /// <summary><paramref name="actual"/> is the time of the first frame at or after <paramref name="expected"/>.</summary>
    private static void InFrame(double expected, double actual) => Assert.InRange(actual, expected - Eps, expected + Frame + Eps);

    private static (FightDefinition Fight, Timeline Timeline) Load(string key)
    {
        var library = new FightLibrary(RepoFiles.Data);
        Assert.Empty(library.Problems);
        var fight = library.Get(key) ?? throw new InvalidOperationException($"data/fights/{key}.yaml did not load");
        return (fight, library.LoadTimeline(fight));
    }

    private static PlanEntry Entry(string mechanic, ActionInfo action) => new()
    {
        Id = $"{mechanic}:{action.Name}",
        Mechanic = mechanic,
        Do = [PlanItem.ForAction(action.Id, action.Name)],
    };

    /// <summary>One forwarded jump, with every reminder's state right after it.</summary>
    private sealed record Jump(SyncResult Result, IReadOnlyDictionary<Reminder, ReminderState> States);

    /// <summary>A timeline player and a scheduler wired like the plugin's EncounterSession, fed event by event.</summary>
    private sealed class Rig
    {
        private readonly TimelinePlayer player;
        private readonly List<(double Time, ActionInfo Action)> presses = [];
        private double clock = Pull - Frame;
        private double now;

        public Rig(FightDefinition fight, Timeline timeline, params PlanEntry[] entries)
        {
            this.player = new TimelinePlayer(timeline) { StopOnCombatEnd = false };
            this.Scheduler = new ReminderScheduler(fight, entries, RepoFiles.Actions, new ReminderOptions());
            Assert.Empty(this.Scheduler.Problems);
            this.player.Synced += r =>
            {
                if (r.Stopped || !r.IsJump)
                    return;
                this.Scheduler.OnTimelineJump(r.Started ? double.NegativeInfinity : r.FromTime, r.ToTime);
                this.Jumps.Add(new Jump(r, this.Scheduler.All.ToDictionary(x => x, x => x.State)));
            };
            this.Scheduler.BecameDue += r => this.Due.Add((r, this.now));
        }

        public ReminderScheduler Scheduler { get; }

        public List<Jump> Jumps { get; } = [];

        public List<(Reminder Reminder, double Time)> Due { get; } = [];

        /// <summary>The player presses <paramref name="action"/> at the first frame at or after timeline time <paramref name="time"/>.</summary>
        public void PressAt(double time, ActionInfo action) => this.presses.Add((time, action));

        /// <summary>The next frame, with this event first (the frame's events come before its update).</summary>
        public void Process(Func<double, GameEvent> make)
        {
            this.clock += Frame;
            this.player.Process(make(this.clock));
            this.EndFrame();
        }

        /// <summary>Frames up to the last one before timeline time <paramref name="until"/> (the next frame reaches it).</summary>
        public void RunTo(double until)
        {
            Assert.True(this.player.IsRunning);
            while (this.player.TimeAt(this.clock + Frame) < until)
            {
                this.clock += Frame;
                this.EndFrame();
            }
        }

        /// <summary>The rest of a frame: tick, own presses, scheduler update.</summary>
        private void EndFrame()
        {
            this.player.Tick(this.clock);
            if (!this.player.IsRunning)
                return;
            this.now = this.player.TimeAt(this.clock);
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
