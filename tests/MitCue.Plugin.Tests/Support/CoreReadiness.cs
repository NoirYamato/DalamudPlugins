using MitCue.Core.Data;
using MitCue.Core.Fights;
using MitCue.Core.Plans;
using MitCue.Core.Simulation;
using MitCue.Core.Timelines;

namespace MitCue.PluginTests.Support;

/// <summary>
/// Whether this build of MitCue.Core has what the integration tests drive: the TOP fight catalog, the timeline parser
/// and player, the fight simulator and the plan serializer. Until every piece is in, those tests are skipped (with
/// the reason); any other failure is left to the tests themselves.
/// </summary>
internal static class CoreReadiness
{
    private static readonly Lazy<string?> Missing = new(Check);

    /// <summary>Why the integration tests cannot run, or null when they can.</summary>
    public static string? MissingReason => Missing.Value;

    private static string? Check()
    {
        try
        {
            var library = new FightLibrary(new EmbeddedDataSource());
            if (library.Get("top") is not { } top)
                return "the TOP fight catalog (data/fights/top.yaml) is not in this build";

            var timeline = library.LoadTimeline(top);
            _ = new TimelinePlayer(timeline).TimeAt(0);
            _ = FightSimulator.Generate(timeline, top, new SimulationOptions());
            _ = PlanSerializer.FromJson(PlanSerializer.ToJson(new MitPlan { Fight = "top" }));
            return null;
        }
        catch (NotImplementedException ex)
        {
            var site = ex.TargetSite is { } method ? $"{method.DeclaringType?.Name}.{method.Name}" : "a MitCue.Core method";
            return $"{site} is not implemented in this build of MitCue.Core";
        }
    }
}

/// <summary>A test that needs the full MitCue.Core (see <see cref="CoreReadiness"/>); skipped until it is in.</summary>
public sealed class CoreFactAttribute : FactAttribute
{
    public CoreFactAttribute()
    {
        if (CoreReadiness.MissingReason is { } reason)
            this.Skip = "Needs the full MitCue.Core: " + reason + ".";
    }
}
