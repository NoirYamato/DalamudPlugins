using MitCue.Core.Plans;
using MitCue.PluginTests.Support;

namespace MitCue.PluginTests;

/// <summary>Review finding: a single null plan item stopped the whole session from starting.</summary>
public class NullPlanItemTests
{
    [Fact]
    public void ASessionStartsWhenAnEntryContainsNullItems()
    {
        var entry = Kit.Entry("p1.raid", -5, Kit.Temperance, "Temperance");
        entry.Do.Insert(0, null!);
        entry.Do.Add(new PlanItem { Any = [null!, PlanItem.ForAction(Kit.DivineBenison, "Divine Benison")] });

        var session = Kit.Session(Kit.Fight(), [entry], new ScriptedSyncEngine());

        Assert.Contains(Kit.Temperance, session.TrackedActionIds);
        Assert.Contains(Kit.DivineBenison, session.TrackedActionIds);
    }
}
