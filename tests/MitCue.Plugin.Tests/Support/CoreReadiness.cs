using MitCue.Core.Data;
using MitCue.Core.Fights;
using MitCue.Core.Plans;
using MitCue.Core.Simulation;
using MitCue.Core.Timelines;

namespace MitCue.PluginTests.Support;

/// <summary>
/// Whether this build of MitCue.Core has what the integration tests drive: the TOP fight catalog, the timeline parser
/// and player, the fight simulator and the plan serializer. While a piece is still a stub (it throws
/// <see cref="NotImplementedException"/>), those tests are skipped with the reason. Anything else is a failure, never a
/// skip: a bundled fight file that does not load (<see cref="FightLibrary.Problems"/>; the library leaves such a file
/// out instead of throwing) or a missing TOP fight makes the check throw, so every integration test fails with the
/// message.
/// </summary>
internal static class CoreReadiness
{
    private static readonly Lazy<string?> Missing = new(() => Check(new EmbeddedDataSource()));

    /// <summary>Why the integration tests cannot run, or null when they can.</summary>
    /// <exception cref="InvalidOperationException">The bundled fight data is broken (cached: thrown on every read).</exception>
    public static string? MissingReason => Missing.Value;

    /// <summary>The check behind <see cref="MissingReason"/>, on the given data.</summary>
    /// <exception cref="InvalidOperationException">A fight file did not load, or there is no TOP fight.</exception>
    internal static string? Check(IDataSource data)
    {
        try
        {
            var library = new FightLibrary(data);
            if (library.Problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "The bundled fight data did not load cleanly: " + string.Join(" | ", library.Problems.Select(p => p.Message)));
            }

            var top = library.Get("top")
                ?? throw new InvalidOperationException("The bundled fight data has no TOP fight (data/fights/top.yaml).");
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

/// <summary>
/// A test that needs the full MitCue.Core (see <see cref="CoreReadiness"/>): skipped while a piece of it is a stub, and
/// failed (an error while the attribute is read) when the bundled fight data is broken.
/// </summary>
public sealed class CoreFactAttribute : FactAttribute
{
    public CoreFactAttribute()
    {
        if (CoreReadiness.MissingReason is { } reason)
            this.Skip = "Needs the full MitCue.Core: " + reason + ".";
    }
}
