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
