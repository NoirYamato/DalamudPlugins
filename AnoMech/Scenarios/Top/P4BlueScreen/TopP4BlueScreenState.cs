using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Top.P4BlueScreen;

public sealed class TopP4BlueScreenState
{
    public const int Sets = 3;
    public const int TargetsPerSet = 2;

    private readonly IReadOnlyList<RoleList> stackTargets;

    // The two line-stack targets of a set (0 for the first), in no particular order. UNVERIFIED: each set is
    // rolled on its own, so a player can be marked in consecutive sets and any pair can be picked.
    public RoleList StackTargets(int set) => stackTargets[set];

    public TopP4BlueScreenState(Rng rng, SimParty party, TopP4BlueScreenStateOverrides overrides)
    {
        // Every roll is drawn whether or not it is overridden, so pinning one leaves the others as
        // the seed had them.
        var rolled = Enumerable.Range(0, Sets)
            .Select(set => new RoleListBuilder
            {
                Size = TargetsPerSet,
                Membership = overrides.MarkerInSet(set).Resolve(party.PlayerRole).ToDictionary(r => r.Role, r => r.Value),
            }.Build(rng, party))
            .ToList();
        stackTargets = overrides.StackTargets is { } pinned
            ? pinned.Select(targets => new RoleList(party, targets)).ToList()
            : rolled;
    }
}
