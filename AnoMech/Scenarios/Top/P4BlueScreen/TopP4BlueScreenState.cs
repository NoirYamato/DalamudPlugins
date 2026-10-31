using System;
using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Scenarios.Top.P4BlueScreen;

public sealed class TopP4BlueScreenState
{
    public const int Sets = 3;
    public const int TargetsPerSet = 2;

    // The party list's light parties.
    public static readonly IReadOnlyList<PartyRole[]> LightParties =
    [
        [MainTank, RegenHealer, MeleeDpsA, PhysRangedDps],
        [OffTank, ShieldHealer, MeleeDpsB, CasterDps],
    ];

    public static PartyRole[] LightPartyOf(PartyRole role) => LightParties.First(lp => lp.Contains(role));

    private readonly IReadOnlyList<RoleList> stackTargets;

    // The two line-stack targets of a set (0 for the first), in no particular order. UNVERIFIED: each set is
    // rolled on its own, so a player can be marked in consecutive sets and any pair can be picked.
    public RoleList StackTargets(int set) => stackTargets[set];

    public TopP4BlueScreenState(Rng rng, SimParty party, TopP4BlueScreenStateOverrides overrides)
    {
        // Every roll is drawn whether or not it is overridden, so pinning one leaves the others as
        // the seed had them.
        var rolled = Enumerable.Range(0, Sets).Select(set => RollSet(rng, party, overrides, set)).ToList();
        stackTargets = overrides.StackTargets is { } pinned
            ? pinned.Select(targets => new RoleList(party, targets)).ToList()
            : rolled;
    }

    private static RoleList RollSet(Rng rng, SimParty party, TopP4BlueScreenStateOverrides overrides, int set)
    {
        var membership = overrides.MarkerInSet(set).Resolve(party.PlayerRole).ToDictionary(r => r.Role, r => r.Value);
        if (overrides.OneLightPartyInSet(set) is not { } oneLightParty)
            return new RoleListBuilder { Size = TargetsPerSet, Membership = membership }.Build(rng, party);

        // The same single draw RoleListBuilder makes, so a set reads the seed the same whichever way it is rolled.
        var pool = rng.Shuffle(Enum.GetValues<PartyRole>()).ToList();
        var forcedIn = membership.Where(m => m.Value).Select(m => m.Key).OrderBy(r => (int)r).ToList();
        bool Out(PartyRole role) => membership.TryGetValue(role, out var wanted) && !wanted;
        IEnumerable<PartyRole> InPoolOrder(PartyRole[] lightParty) => pool.Where(lightParty.Contains);

        if (oneLightParty)
        {
            var lightParty = forcedIn.Count > 0
                ? LightPartyOf(forcedIn[0])
                : pool.Select(LightPartyOf).FirstOrDefault(lp => lp.Count(r => !Out(r)) >= TargetsPerSet) ?? LightPartyOf(pool[0]);
            var targets = forcedIn.Where(lightParty.Contains)
                                  .Concat(InPoolOrder(lightParty).Where(r => !Out(r)))
                                  .Concat(InPoolOrder(lightParty))
                                  .Distinct()
                                  .Take(TargetsPerSet)
                                  .ToList();
            return new RoleList(party, targets);
        }

        return new RoleList(party, LightParties
            .Select(lp => forcedIn.Where(lp.Contains).Cast<PartyRole?>().FirstOrDefault()
                          ?? InPoolOrder(lp).Where(r => !Out(r)).Cast<PartyRole?>().FirstOrDefault()
                          ?? InPoolOrder(lp).First())
            .ToList());
    }
}
