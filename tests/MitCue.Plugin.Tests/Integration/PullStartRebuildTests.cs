using MitCue.Core;
using MitCue.Core.Data;
using MitCue.Core.Events;
using MitCue.Core.Fights;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;
using MitCue.Core.Simulation;
using MitCue.PluginTests.Support;
using MitCue.Runtime;

namespace MitCue.PluginTests.Integration;

/// <summary>
/// Review round 5 (runtime): a change of the session's shape (party list, column choice, a plan save) in the very frame
/// the pull starts must not lose the pull. SessionManager feeds the frame's events to the old session first, so its
/// timeline is started by the InCombat edge before its update marks the pull; a session rebuilt then never sees the edge
/// and does not follow the pull until a sync whose window holds time 0 comes (TEA: Hawk Blaster at 200 s, all of P1
/// lost). SessionManager is driven frame by frame like Plugin.Tick, with the simulated pull's events.
/// </summary>
public class PullStartRebuildTests
{
    private const double Pull = 1000;

    private static List<GameEvent> Simulate(string fightKey)
    {
        var library = new FightLibrary(new EmbeddedDataSource());
        var fight = library.Get(fightKey)!;
        return FightSimulator.Generate(library.LoadTimeline(fight), fight, new SimulationOptions { PullClock = Pull, Seed = 7 })
            .Where(GameVisibleEvents.Contains)
            .OrderBy(e => e.Time)
            .ToList();
    }

    /// <summary>Plugin.Tick for one frame: this frame's events, the countdown, InCombat and the party list.</summary>
    private sealed class Live(SessionManagerTests.Parts parts, List<GameEvent> events, uint territory, Job job, int level)
    {
        private int next;
        private bool inCombat;

        public void Frame(double now, float? countdown, ulong party)
        {
            var frameEvents = new List<GameEvent>();
            while (this.next < events.Count && events[this.next].Time <= now)
            {
                var e = events[this.next++];
                if (e is CombatStateChanged c)
                    this.inCombat = c.InCombat;
                frameEvents.Add(e);
            }

            parts.Manager.Update(new FrameContext
            {
                Now = now,
                LoggedIn = true,
                Territory = territory,
                Job = job,
                ClassJobId = (uint)job,
                Level = level,
                PartyJobMask = party,
                Countdown = countdown,
                InCombat = this.inCombat,
                Events = frameEvents,
            });
        }
    }

    /// <summary>
    /// TEA, a White Mage plan with nothing shown during the countdown (the first call 16.5 s in), the co-healer's calls
    /// shown. Combat starts while the countdown still shows 0.3 s; with <paramref name="partyChange"/> the co-healer's job
    /// changes in that frame.
    /// </summary>
    private static EncounterSession TeaPull(bool partyChange)
    {
        var parts = SessionManagerTests.NewManager(new Configuration { ShowOtherAssignees = true });
        parts.Plans.EnsureLoaded();
        var plan = new MitPlan
        {
            Id = "tea-whm",
            Name = "TEA WHM",
            Fight = "tea",
            Assignees = [new Assignee { Key = "White Mage", Job = Job.WHM }],
            Entries =
            [
                Kit.Entry("p1.cascade_1", -3, Kit.PlenaryIndulgence, "Plenary Indulgence", assignee: "White Mage"),
                Kit.Entry("p1.hand_1", -3, Kit.DivineBenison, "Divine Benison", assignee: "White Mage"),
                Kit.Entry("p1.hand_of_pain_1", -3, Kit.Temperance, "Temperance", assignee: "White Mage"),
            ],
        };
        Assert.True(parts.Plans.Save(plan, out var error), error);

        var before = PartyJobs.Mask([Job.WHM, Job.SCH, Job.WAR, Job.PLD, Job.SAM, Job.RPR, Job.DNC, Job.PCT]);
        var after = PartyJobs.Mask([Job.WHM, Job.SGE, Job.WAR, Job.PLD, Job.SAM, Job.RPR, Job.DNC, Job.PCT]);
        var live = new Live(parts, Simulate("tea"), 887, Job.WHM, 80);
        for (var now = Pull - 15; now <= Pull + 60; now = Math.Round(now + 0.05, 3))
            live.Frame(now, now < Pull + 0.3 ? (float)(Pull + 0.3 - now) : null, partyChange && now >= Pull ? after : before);
        return parts.Manager.Session!;
    }

    [Fact]
    public void ARebuildInTheFrameCombatStartsKeepsThePull()
    {
        var control = TeaPull(partyChange: false);
        var raced = TeaPull(partyChange: true);

        Assert.True(control.PullActive);
        Assert.True(raced.PullActive); // 6df1f20: false, the three P1 calls Pending (then Skipped once Hawk Blaster starts the timeline)
        Assert.Equal(control.Scheduler.All.Select(r => r.State), raced.Scheduler.All.Select(r => r.State));
    }

    [Fact]
    public void AFlickeringPartyListThroughThePullKeepsThePull()
    {
        // A Warrior (usual slot MT) on the bundled LPDU, default settings: the co-tank's party entry flickers every other
        // frame from the countdown until 20 s into the pull, and the tank pair's column with it. 6df1f20 first saw the pull
        // at P2's Solar Ray (203 s), with all five P1 calls Skipped.
        var parts = SessionManagerTests.NewManager(new Configuration { DefaultSlot = "MT" });
        var noCoTank = PartyJobs.Mask([Job.WAR, Job.WHM, Job.SCH, Job.SAM, Job.RPR, Job.DNC, Job.PCT]);
        var full = PartyJobs.Mask([Job.WAR, Job.PLD, Job.WHM, Job.SCH, Job.SAM, Job.RPR, Job.DNC, Job.PCT]);
        var live = new Live(parts, Simulate("top"), 1122, Job.WAR, 90);
        double? seen = null;
        var frame = 0;
        for (var now = Pull - 15; now <= Pull + 25; now = Math.Round(now + 1.0 / 60, 4))
        {
            live.Frame(now, now < Pull ? (float)(Pull - now) : null, now < Pull + 20 && frame++ % 2 == 1 ? noCoTank : full);
            if (seen == null && parts.Manager.Session!.PullActive)
                seen = now;
        }

        Assert.NotNull(seen);
        Assert.InRange(seen.Value, Pull, Pull + 0.1);
        Assert.InRange(parts.Manager.Session!.TimelineTime, 24, 26);
        Assert.DoesNotContain(parts.Manager.Session.Scheduler.All, r => r.State == ReminderState.Skipped);
    }
}
