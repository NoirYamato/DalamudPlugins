using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Top.TopP6Constants;

namespace AnoMech.Scenarios.Top.P6WaveCannon1;

public sealed class TopP6WaveCannon1State
{
    // The compass point the first exaflare starts on: 0 north, then clockwise every 45°.
    public int FirstExaflare { get; }

    // The way the other three exaflares follow the first round the arena.
    public bool Clockwise { get; }

    // Wave Cannon's laser targets: the first four, then the other four.
    public RoleList ProteanOrder { get; }

    public PartyRole WildChargeTarget { get; }

    // UNVERIFIED (P6Duration.ExaflareStep); headless runs also play the other candidate.
    internal float ExaflareStep { get; }

    // UNVERIFIED: that the game draws the lasers' sets and the Wild Charge's target evenly; they are drawn as upstream's
    // Wave Cannon 2 draws them.
    public TopP6WaveCannon1State(Rng rng, SimParty party, TopP6WaveCannon1StateOverrides overrides)
    {
        // Every roll is drawn whether or not it is overridden, so pinning one leaves the others as the seed had them.
        (FirstExaflare, Clockwise) = TopP6UnlimitedWaveCannon.Roll(rng, overrides.FirstExaflare, overrides.Clockwise);
        var proteanOrder = RoleList.Random(rng, party);
        var wildChargeTarget = rng.NextRole();

        ProteanOrder = overrides.ProteanOrder is { } pinned ? new RoleList(party, pinned) : proteanOrder;
        WildChargeTarget = overrides.WildChargeTarget ?? wildChargeTarget;
        ExaflareStep = overrides.ExaflareStep ?? P6Duration.ExaflareStep;
    }
}
