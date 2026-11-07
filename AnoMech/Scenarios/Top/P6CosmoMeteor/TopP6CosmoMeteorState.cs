using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Top.P6CosmoMeteor;

public sealed class TopP6CosmoMeteorState
{
    public const int SpreadSets = 2;
    public const int SpreadsPerWave = 4;
    public const int FlareTargets = 3;

    private readonly IReadOnlyList<PartyRole[]> firstSpreads;

    // The players a spread wave hits: wave 0 is four of them, wave 1 the other four a second later. set: 0 for the
    // first. UNVERIFIED: each set's four are drawn on their own, at random.
    public PartyRole[] SpreadWave(int set, int wave)
        => wave == 0 ? firstSpreads[set] : PerRole.All.Except(firstSpreads[set]).ToArray();

    // The three players marked for a flare, in no particular order.
    public RoleList Flares { get; }

    public bool HasFlare(PartyRole role) => Flares.Contains(role);

    // Who the unmarked stack lands on: one of the five without a flare.
    public PartyRole StackTarget { get; }

    public TopP6CosmoMeteorState(Rng rng, SimParty party, TopP6CosmoMeteorStateOverrides overrides)
    {
        // Every roll is drawn whether or not it is overridden, so pinning one leaves the others as
        // the seed had them.
        var rolledSpreads = Enumerable.Range(0, SpreadSets)
            .Select(_ => rng.Shuffle(PerRole.All).Take(SpreadsPerWave).ToArray())
            .ToList();
        firstSpreads = overrides.FirstSpreads ?? rolledSpreads;

        var rolledFlares = new RoleListBuilder
        {
            Size = FlareTargets,
            Membership = overrides.Flare.Resolve(party.PlayerRole).ToDictionary(r => r.Role, r => r.Value),
        }.Build(rng, party);
        Flares = overrides.Flares is { } pinned ? new RoleList(party, pinned) : rolledFlares;

        var withoutAFlare = PerRole.All.Where(r => !HasFlare(r)).ToList();
        var rolledStack = withoutAFlare[rng.Next(withoutAFlare.Count)];
        StackTarget = overrides.StackTarget is { } stack && !HasFlare(stack) ? stack : rolledStack;
    }
}
