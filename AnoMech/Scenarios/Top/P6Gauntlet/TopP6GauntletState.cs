using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Top.TopP6Constants;

namespace AnoMech.Scenarios.Top.P6Gauntlet;

// The phase's parts, in order, each starting 1.9 s before its first boss cast.
public enum TopP6GauntletPart
{
    CosmoMemory,
    Exaflares1,
    CosmoArrow2,
    Exaflares2,
    CosmoMeteor,
    MagicNumber,
}

// The parts a run plays, First to Last, each on its own clock: Start says when on the run's clock it begins.
public sealed record TopP6GauntletParts(TopP6GauntletPart First, TopP6GauntletPart Last)
{
    // On the Gauntlet's clock, which starts 1.9 s before Cosmo Memory's cast.
    private static readonly float[] Starts = [0f, 48.6f, 90.9f, 124.9f, 164.8f, 199.9f];

    public static readonly TopP6GauntletParts Whole = new(TopP6GauntletPart.CosmoMemory, TopP6GauntletPart.MagicNumber);

    public bool Plays(TopP6GauntletPart part) => part >= First && part <= Last;

    public float Start(TopP6GauntletPart part) => GauntletStart(part) - GauntletStart(First);

    // Where `part` starts on the whole Gauntlet's clock.
    public static float GauntletStart(TopP6GauntletPart part) => Starts[(int)part];
}

// One run's rolls, each drawn in the order the fight shows it and whether or not it is forced, so forcing one leaves the
// others as the seed had them. UNVERIFIED: that the game draws each evenly.
public sealed class TopP6GauntletState
{
    public const int SpreadSets = 2;
    public const int SpreadsPerWave = 4;
    public const int FlareTargets = 3;

    public TopP6GauntletParts Parts { get; }

    // Cosmo Arrow 1: the cross lands first, else the outer lines do.
    public bool Arrow1InFirst { get; }

    // Unlimited Wave Cannon 1: the compass point the first exaflare starts on (0 north, then clockwise every 45°) and
    // the way the other three follow it round.
    public int Exaflares1First { get; }
    public bool Exaflares1Clockwise { get; }

    // Wave Cannon 1's lasers, the first four, then the other four, and Wild Charge's target.
    public RoleList Lasers1 { get; }
    public PartyRole WildCharge1Target { get; }

    public bool Arrow2InFirst { get; }
    public RoleList Lasers2 { get; }
    public PartyRole WildCharge2Target { get; }

    public int Exaflares2First { get; }
    public bool Exaflares2Clockwise { get; }

    // Cosmo Meteor: each spread set's first four (the other four are hit a second later), the three flares, and the
    // unmarked stack on one of the five without a flare.
    public IReadOnlyList<PartyRole[]> FirstSpreads { get; }
    public RoleList Flares { get; }
    public PartyRole StackTarget { get; }

    public TopP6CosmoMeteorRolls MeteorRolls => new(FirstSpreads, Flares.List, StackTarget);

    public bool HasFlare(PartyRole role) => Flares.Contains(role);

    // UNVERIFIED (P6Duration.ExaflareStep); headless runs also play the other candidate.
    internal float Exaflares1Step { get; }
    internal float Exaflares2Step { get; }

    // Who presses each limit break 3, from the seats' jobs and the human's own rows.
    public TopP6GauntletLimitBreakPlan LimitBreakPlan { get; }

    // Headless runs that press the player's own limit breaks themselves, while the debug driver still walks it.
    internal bool PlayerPressesItsOwnLimitBreaks { get; }

    // The run's limit breaks and Alpha Omega, which the strat presses through and aims at; the scenario sets them.
    internal TopP6LimitBreaks? LimitBreaks { get; init; }
    internal TopP6AlphaOmega? AlphaOmega { get; init; }

    public TopP6GauntletState(Rng rng, SimParty party, TopP6GauntletStateOverrides overrides)
    {
        var first = overrides.Parts?.First ?? overrides.StartFrom ?? TopP6GauntletParts.Whole.First;
        var last = overrides.Parts?.Last ?? TopP6GauntletParts.Whole.Last;
        Parts = new TopP6GauntletParts(first, last < first ? first : last);

        var arrow1InFirst = rng.NextBool();
        Arrow1InFirst = overrides.Arrow1InFirst ?? arrow1InFirst;

        (Exaflares1First, Exaflares1Clockwise) = TopP6UnlimitedWaveCannon.Roll(rng, overrides.Exaflares1First, overrides.Exaflares1Clockwise);
        var lasers1 = RoleList.Random(rng, party);
        var wildCharge1Target = rng.NextRole();
        Lasers1 = overrides.Lasers1 is { } pinnedLasers1 ? new RoleList(party, pinnedLasers1) : lasers1;
        WildCharge1Target = overrides.WildCharge1Target ?? wildCharge1Target;

        var arrow2InFirst = rng.NextBool();
        Arrow2InFirst = overrides.Arrow2InFirst ?? arrow2InFirst;
        var lasers2 = RoleList.Random(rng, party);
        var wildCharge2Target = rng.NextRole();
        Lasers2 = overrides.Lasers2 is { } pinnedLasers2 ? new RoleList(party, pinnedLasers2) : lasers2;
        WildCharge2Target = overrides.WildCharge2Target ?? wildCharge2Target;

        (Exaflares2First, Exaflares2Clockwise) = TopP6UnlimitedWaveCannon.Roll(rng, overrides.Exaflares2First, overrides.Exaflares2Clockwise);

        var firstSpreads = Enumerable.Range(0, SpreadSets)
            .Select(_ => rng.Shuffle(PerRole.All).Take(SpreadsPerWave).ToArray())
            .ToList();
        FirstSpreads = overrides.FirstSpreads ?? firstSpreads;
        var flares = new RoleListBuilder
        {
            Size = FlareTargets,
            Membership = overrides.Flare.Resolve(party.PlayerRole).ToDictionary(r => r.Role, r => r.Value),
        }.Build(rng, party);
        Flares = overrides.Flares is { } pinnedFlares ? new RoleList(party, pinnedFlares) : flares;
        var withoutAFlare = PerRole.All.Where(r => !HasFlare(r)).ToList();
        var stackTarget = withoutAFlare[rng.Next(withoutAFlare.Count)];
        StackTarget = overrides.StackTarget is { } stack && !HasFlare(stack) ? stack : stackTarget;

        Exaflares1Step = overrides.Exaflares1Step ?? P6Duration.ExaflareStep;
        Exaflares2Step = overrides.Exaflares2Step ?? P6Duration.ExaflareStep;
        LimitBreakPlan = TopP6GauntletLimitBreakPlan.For(party, overrides.TankLimitBreaks, overrides.MeleeLimitBreaks, overrides.HealerLimitBreaks);
        PlayerPressesItsOwnLimitBreaks = overrides.PlayerPressesItsOwnLimitBreaks;
    }
}
