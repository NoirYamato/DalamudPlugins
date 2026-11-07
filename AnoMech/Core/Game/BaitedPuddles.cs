using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.SimObjects;

namespace AnoMech.Core.Game;

// Puddles the party baits: one cast under each living member, fixed where they stand as it starts, so walking away
// before it goes off escapes it.
public static class BaitedPuddles
{
    // The spots, in party order.
    public static IReadOnlyList<Vector3> Drop(SimParty party, EnemyAction puddle, HelperCast cast)
    {
        var spots = party.ActiveMembers().Select(member => member.Position).ToList();
        foreach (var spot in spots)
            cast(puddle, spot, new Placement(spot, 0f));
        return spots;
    }
}
