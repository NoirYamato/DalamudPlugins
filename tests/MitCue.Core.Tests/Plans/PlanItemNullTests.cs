using MitCue.Core.Plans;

namespace MitCue.Core.Tests.Plans;

/// <summary>Plans built in code (importer, review edits) may contain null items; they must not crash consumers.</summary>
public class PlanItemNullTests
{
    [Fact]
    public void ActionIdsSkipsANullEitherOrOption()
    {
        var item = new PlanItem { Any = [null!, PlanItem.ForAction(7432, "Divine Benison")] };

        Assert.Equal([7432u], item.ActionIds().ToList());
    }

    [Fact]
    public void ActionIdsOfNestedGroupsWithNullsAreFlattened()
    {
        var item = new PlanItem
        {
            Any = [PlanItem.AnyOf([null!, PlanItem.ForAction(3570, "Tetragrammaton")]), null!, PlanItem.ForAction(140, "Benediction")],
        };

        Assert.Equal([3570u, 140u], item.ActionIds().ToList());
    }
}
