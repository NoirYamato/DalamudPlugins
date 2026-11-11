using Dalamud.Plugin.Services;
using MitCue.Core;
using MitCue.Data;
using MitCue.Game;
using MitCue.Plans;
using MitCue.PluginTests.Support;
using MitCue.Runtime;
using MitCue.Util;

namespace MitCue.PluginTests;

/// <summary>The live session manager loads what it needs on the framework thread and explains its status.</summary>
public class SessionManagerTests
{
    internal sealed record Parts(SessionManager Manager, CoreData Data, PlanStore Plans, ErrorReporter Errors, ServiceFake Chat);

    internal static Parts NewManager(Configuration? config = null)
    {
        config ??= new Configuration();
        var errors = Kit.Errors();
        var data = new CoreData(errors, () => config.UserAliases);
        var plans = new PlanStore(Kit.TempDirectory(), config, () => { }, errors);
        var gameData = Kit.GameData();
        var cooldowns = new CooldownProvider(data, gameData, ServiceFake.Of<IJobGauges>(), errors);
        var (chat, chatLog) = ServiceFake.Create<IChatGui>();
        var manager = new SessionManager(config, data, gameData, plans, cooldowns, errors, chat, () => { });
        return new Parts(manager, data, plans, errors, chatLog);
    }

    internal static FrameContext InTop(double now = 1) => new()
    {
        Now = now,
        LoggedIn = true,
        Territory = 1122,
        Job = Job.WHM,
        ClassJobId = (uint)Job.WHM,
        Level = 90,
    };

    [Fact]
    public void TheFirstFrameLoadsFightDataAndPlans()
    {
        // Review finding: only /mitcue preview ever loaded them, so no live session was built and the Plans tab
        // stayed on "Loading...".
        var parts = NewManager();
        Assert.False(parts.Data.Loaded);
        Assert.False(parts.Plans.Loaded);

        parts.Manager.Update(InTop());

        Assert.True(parts.Data.Loaded);
        Assert.True(parts.Plans.Loaded);
    }

    [Fact]
    public void FightDataLoadsAgainAfterReloadData()
    {
        var parts = NewManager();
        parts.Manager.Update(InTop());
        var revision = parts.Data.Revision;

        parts.Data.Reload(); // Debug > Reload data
        parts.Manager.Invalidate();
        parts.Manager.Update(InTop(2));

        Assert.True(parts.Data.Loaded);
        Assert.Equal(revision + 1, parts.Data.Revision);
    }

    [Fact]
    public void ADataProblemIsListedWheneverTheStatusPointsToIt()
    {
        var parts = NewManager();
        parts.Manager.Update(InTop());

        // Review finding: "Fight data did not load (see Problems)" was shown with an empty Problems list.
        if (parts.Manager.Status.Contains("did not load", StringComparison.Ordinal))
            Assert.Contains(parts.Errors.Snapshot(), p => p.Key.StartsWith("data.", StringComparison.Ordinal));
        if (parts.Data.Fights == null)
            Assert.Contains(parts.Errors.Snapshot(), p => p.Key == "data.fights");
    }
}
