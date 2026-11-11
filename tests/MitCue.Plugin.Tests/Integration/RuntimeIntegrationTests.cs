using Dalamud.Plugin.Services;
using MitCue.Core;
using MitCue.Core.Data;
using MitCue.Core.Events;
using MitCue.Core.Fights;
using MitCue.Core.Plans;
using MitCue.Core.Reminders;
using MitCue.Core.Simulation;
using MitCue.Core.Timelines;
using MitCue.Data;
using MitCue.Game;
using MitCue.PluginTests.Support;
using MitCue.Runtime;

namespace MitCue.PluginTests.Integration;

/// <summary>
/// The plugin runtime on the real MitCue.Core: the TOP catalog and timeline, the timeline player, the fight simulator
/// and the plan serializer. Skipped until those are in the build (see <see cref="CoreReadiness"/>).
/// </summary>
public class RuntimeIntegrationTests
{
    private const uint TopTerritory = 1122;

    /// <summary>A WHM plan for TOP: a pre-pull call, a P1 call and a P3 call.</summary>
    private static MitPlan TopWhmPlan() => new()
    {
        Id = "test-top-whm-" + Guid.NewGuid().ToString("N"),
        Name = "Test TOP WHM",
        Fight = "top",
        Assignees = [new Assignee { Key = "White Mage", Job = Job.WHM }],
        Entries =
        [
            Kit.Entry("p1.pull", -5, Kit.DivineBenison, "Divine Benison", assignee: "White Mage"),
            Kit.Entry("p1.loop_set1", -5, Kit.Temperance, "Temperance", assignee: "White Mage"),
            Kit.Entry("p3.hello_world", -5, Kit.PlenaryIndulgence, "Plenary Indulgence", assignee: "White Mage"),
        ],
    };

    private static SessionManagerTests.Parts ManagerWithPlan(MitPlan plan, Configuration? config = null)
    {
        var parts = SessionManagerTests.NewManager(config);
        parts.Plans.EnsureLoaded();
        Assert.True(parts.Plans.Save(plan, out var error), error);
        return parts;
    }

    [CoreFact]
    public void TheLiveSessionStartsInTopOnTheFirstFrame()
    {
        var parts = ManagerWithPlan(TopWhmPlan());

        parts.Manager.Update(SessionManagerTests.InTop());

        Assert.NotNull(parts.Manager.Session);
        Assert.Equal("top", parts.Manager.Fight?.Key);
        Assert.StartsWith("Ready:", parts.Manager.Status, StringComparison.Ordinal);
    }

    [CoreFact]
    public void AStoppedLiveSessionSaysSoInChatOnce()
    {
        var parts = ManagerWithPlan(TopWhmPlan());
        parts.Manager.Update(SessionManagerTests.InTop());
        Assert.NotNull(parts.Manager.ActiveSession);

        // The timeline player rejects a null event: stands in for any exception inside the session.
        var broken = SessionManagerTests.InTop(2);
        broken.Events = [null!];
        parts.Manager.Update(broken);
        parts.Manager.Update(SessionManagerTests.InTop(3));

        Assert.True(parts.Manager.Faulted);
        Assert.Null(parts.Manager.ActiveSession);
        var message = Assert.Single(parts.Chat.FirstArgs("PrintError"));
        Assert.Contains("/mitcue reset", message, StringComparison.Ordinal);
    }

    [CoreFact]
    public void TheCombatFlagDroppingMidFightDoesNotRestartTheTopTimeline()
    {
        // Review finding (harness B): InCombat false from 450 to 458 while the boss keeps casting ended the pull,
        // restarted the timeline at 0 after the raise and replayed P1's calls in the middle of P3.
        var library = new FightLibrary(new EmbeddedDataSource());
        var top = library.Get("top")!;
        var timeline = library.LoadTimeline(top);
        var plan = TopWhmPlan();
        var me = new PlayerContext(Job.WHM, 90, null, "White Mage");
        var session = new EncounterSession(new SessionSetup
        {
            Fight = top,
            Timeline = timeline,
            Plan = plan,
            Me = me,
            OwnEntries = PlanSelection.For(plan, me).ToList(),
            Actions = EmptyActionCatalog.Instance,
            Options = new ReminderOptions(),
            SyncOptions = new SyncMatchOptions { MatchNames = true },
        });
        var ended = new List<string>();
        session.PullEnded += (_, _, reason) => ended.Add(reason);

        const double pull = 1000;
        var events = FightSimulator.Generate(timeline, top, new SimulationOptions { PullClock = pull, Seed = 7 })
            .Append(new CombatStateChanged(pull + 450, false))
            .Append(new CombatStateChanged(pull + 458, true))
            .OrderBy(e => e.Time)
            .ToList();

        var next = 0;
        var inCombat = false;
        for (var clock = pull - 1; clock <= pull + 462; clock += 0.1)
        {
            while (next < events.Count && events[next].Time <= clock)
            {
                var e = events[next++];
                if (e is CombatStateChanged c)
                    inCombat = c.InCombat;
                session.Process(e);
            }

            session.Update(clock, null, inCombat, null);
        }

        Assert.Empty(ended);
        Assert.True(session.PullActive);
        Assert.InRange(session.TimelineTime, 455, 470);
    }

    [CoreFact]
    public void APhasePreviewNeverJudgesTheEarlierPhasesCalls()
    {
        // Review finding (harness F): "/mitcue preview top p3" listed the pre-pull call as missed at the pull and in the
        // preview summary.
        var config = new Configuration { PreviewFast = true };
        var parts = ManagerWithPlan(TopWhmPlan(), config);
        var (chat, chatLog) = ServiceFake.Create<IChatGui>();
        var preview = new PreviewDriver(config, parts.Data, Kit.GameData(), parts.Plans, parts.Errors, chat, () => { });

        Assert.True(preview.Start("top", "p3", TopTerritory, Job.WHM, 0, out var message), message);
        var session = preview.Session!;
        var earlyMissed = false;
        for (var real = 0.0; real < 15; real += 0.05)
        {
            preview.Update(real, []);
            earlyMissed |= session.Visible.Any(r => r.State == ReminderState.Missed && r.PressAt < 400);
        }

        Assert.False(earlyMissed);
        Assert.All(session.Scheduler.All.Where(r => r.PressAt < 400), r => Assert.Equal(ReminderState.Skipped, r.State));
        Assert.All(session.Scheduler.Summarize().MissedReminders, r => Assert.True(r.PressAt >= 400));
        Assert.Empty(chatLog.FirstArgs("PrintError"));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("party")]
    [InlineData("settings")]
    public void AChangeDuringTheCountdownKeepsThePrePullCallDone(string change)
    {
        // Review finding (41d2893): a party change during /countdown (the co-healer swaps job) was held until the countdown
        // reached 0, then rebuilt the session before combat started and dropped the pre-pull call already pressed:
        // "0 done, 3 missed. Missed: Divine Benison". Settings edits had the same gap. The rebuild now waits for the pull.
        const double pull = 1000;
        var config = new Configuration { ShowOtherAssignees = true };
        var plan = TopWhmPlan();
        plan.Assignees.Add(new Assignee { Key = "Scholar", Job = Job.SCH });
        plan.Assignees.Add(new Assignee { Key = "Astrologian", Job = Job.AST });
        plan.Entries.Add(Kit.Entry("p1.loop_set3", -3, Kit.SacredSoil, "Sacred Soil", Job.SCH, assignee: "Scholar"));
        plan.Entries.Add(Kit.Entry("p1.loop_set3", -3, 3613, "Collective Unconscious", Job.AST, assignee: "Astrologian"));
        var parts = ManagerWithPlan(plan, config);
        parts.Plans.SetActive("top", Job.WHM, plan.Id);
        parts.Data.EnsureLoaded();
        var top = parts.Data.GetFight("top")!;
        var events = FightSimulator.Generate(parts.Data.TryLoadTimeline(top)!, top, new SimulationOptions { PullClock = pull, Seed = 3 })
            .Where(GameVisibleEvents.Contains)
            .Append(new DutyReset(pull + 140, DutyResetKind.Wipe))
            .OrderBy(e => e.Time)
            .ToList();
        var withSch = PartyJobs.Mask([Job.WAR, Job.DRK, Job.WHM, Job.SCH, Job.SAM, Job.RPR, Job.DNC, Job.PCT]);
        var withAst = PartyJobs.Mask([Job.WAR, Job.DRK, Job.WHM, Job.AST, Job.SAM, Job.RPR, Job.DNC, Job.PCT]);

        var next = 0;
        var inCombat = false;
        var party = withSch;
        uint? press = null;
        var generation = -1;
        for (var clock = pull - 30; clock <= pull + 141; clock += 0.1)
        {
            if (Math.Abs(clock - (pull - 7)) < 0.05)
            {
                press = Kit.DivineBenison; // the pre-pull call (pull -5), pressed during the countdown
                generation = parts.Manager.Generation;
            }

            if (Math.Abs(clock - (pull - 6)) < 0.05)
            {
                if (change == "party")
                    party = withAst;
                else if (change == "settings")
                {
                    config.DefaultSlot = "H1"; // changes the session's shape (its slot)
                    config.Touch();
                }
            }

            var frame = SessionManagerTests.InTop(clock);
            var list = new List<GameEvent>();
            while (next < events.Count && events[next].Time <= clock)
            {
                var e = events[next++];
                if (e is CombatStateChanged c)
                    inCombat = c.InCombat;
                list.Add(e);
            }

            frame.Events = list;
            frame.InCombat = inCombat;
            frame.Countdown = clock < pull - 1.5 ? (float)(pull - 1.5 - clock) : null; // "0" 1.5 s before the boss is engaged
            frame.PartyJobMask = party;
            if (press is { } id)
                frame.OwnUses.Add(new OwnUse(id, clock, OwnUseSource.ActionEffect));
            press = null;
            parts.Manager.Update(frame);

            if (clock < pull + 1)
                Assert.True(parts.Manager.Generation == generation || generation < 0, $"rebuilt at {clock - pull:0.0} s");
        }

        var summary = Assert.Single(parts.Chat.FirstArgs("Print"));
        Assert.Contains("1 done", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Divine Benison", summary, StringComparison.Ordinal);
        if (change != "none")
            Assert.True(parts.Manager.Generation > generation); // rebuilt after the pull
    }

    [CoreFact]
    public void TimingEditsReachARunningPreview()
    {
        var config = new Configuration();
        var parts = ManagerWithPlan(TopWhmPlan(), config);
        var preview = new PreviewDriver(config, parts.Data, Kit.GameData(), parts.Plans, parts.Errors, ServiceFake.Of<IChatGui>(), () => { });
        Assert.True(preview.Start("top", null, TopTerritory, Job.WHM, 0, out var message), message);

        config.Lookahead = 25;
        config.DueLead = 3;
        config.Touch();
        preview.Update(0.1, []);

        Assert.Equal(25, preview.Session!.Options.Lookahead);
        Assert.Equal(3, preview.Session.Options.DueLead);
    }
}
