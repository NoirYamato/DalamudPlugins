using System;
using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Scenarios.Tea.P1LivingLiquid;

// Whom the two Drainage tethers start on, forced.
public enum TeaP1DrainageStartsOn { TheTanks, Others }

// One run's rolls, each drawn in the order the fight shows it and whether or not it is forced, so forcing one leaves
// the others as the seed had them. UNVERIFIED: that the game draws each evenly, and that Cascade 2's tornadoes can
// stand where the first did.
public sealed class TeaP1LivingLiquidState
{
    public const int DrainageTethers = 2;
    public const int ThrottleTargets = 6;

    public TeaP1LivingLiquidParts Parts { get; }

    // The tornadoes standing through Protean 1, and the set Cascade 2 brings.
    public TeaP1TornadoLayout Tornadoes1 { get; }
    public TeaP1TornadoLayout Tornadoes2 { get; }

    // The two tornadoes that tether, west before middle before east, each with the seat its tether starts on.
    public IReadOnlyList<TeaP1DrainageRoll> Drainage { get; }

    public RoleList Throttled { get; }

    public bool HasThrottle(PartyRole role) => Throttled.Contains(role);

    // The second pair: the two tornadoes that cast Pressurize, each with the boss whose spot its Embolus heads for.
    public IReadOnlyList<TeaP1EmbolusRoll> Emboli { get; }

    // UNVERIFIED (P1Rules.EmbolusSpeed); headless runs also play faster ones.
    internal float EmbolusSpeed { get; }

    // What the strat asks of the human's seat, from the seats' jobs.
    public TeaP1LivingLiquidPlan Plan { get; }

    // Headless runs that press the player's own actions themselves, while the debug driver still walks it.
    internal bool PlayerPressesItsOwnActions { get; }

    // The run's bosses and presses, which the strat reads and cleanses through; the scenario sets them.
    internal TeaP1LivingLiquid? Fight { get; init; }
    internal TeaP1Presses? Presses { get; init; }

    public TeaP1LivingLiquidState(Rng rng, SimParty party, TeaP1LivingLiquidStateOverrides overrides)
    {
        Parts = TeaP1LivingLiquidParts.From(overrides);

        var tornadoes1 = (TeaP1OpenQuarter)rng.NextInt(8);
        Tornadoes1 = new TeaP1TornadoLayout(overrides.Tornadoes1 ?? tornadoes1);

        var tethering = AllBut((TeaP1Tornado)rng.NextInt(3));
        var firstTargets = FirstTargets(rng.Shuffle(PerRole.All), overrides.DrainageStartsOn);
        Drainage = overrides.Drainage
                   ?? Enumerable.Range(0, DrainageTethers).Select(k => new TeaP1DrainageRoll(tethering[k], firstTargets[k])).ToList();

        var tornadoes2 = (TeaP1OpenQuarter)rng.NextInt(8);
        Tornadoes2 = new TeaP1TornadoLayout(overrides.Tornadoes2 ?? tornadoes2);

        var throttled = new RoleListBuilder
        {
            Size = ThrottleTargets,
            Membership = overrides.Throttle.Resolve(party.PlayerRole).ToDictionary(r => r.Role, r => r.Value),
        }.Build(rng, party);
        Throttled = overrides.Throttled is { } pinnedThrottled ? new RoleList(party, pinnedThrottled) : throttled;

        var pressurizing = AllBut((TeaP1Tornado)rng.NextInt(3));
        var firstAimsAtTheLiquid = rng.NextBool();
        Emboli = overrides.Emboli ??
        [
            new TeaP1EmbolusRoll(pressurizing[0], firstAimsAtTheLiquid ? TeaP1Boss.LivingLiquid : TeaP1Boss.LiquidHand),
            new TeaP1EmbolusRoll(pressurizing[1], firstAimsAtTheLiquid ? TeaP1Boss.LiquidHand : TeaP1Boss.LivingLiquid),
        ];

        EmbolusSpeed = overrides.EmbolusSpeed ?? P1Rules.EmbolusSpeed;
        Plan = TeaP1LivingLiquidPlan.For(party);
        PlayerPressesItsOwnActions = overrides.PlayerPressesItsOwnActions;
    }

    private static TeaP1Tornado[] AllBut(TeaP1Tornado left) => Enum.GetValues<TeaP1Tornado>().Where(tornado => tornado != left).ToArray();

    // UNVERIFIED whom the fight picks: any two players here, unless the setting says the tanks or the rest.
    private static IReadOnlyList<PartyRole> FirstTargets(IReadOnlyList<PartyRole> shuffled, TeaP1DrainageStartsOn? startsOn) => startsOn switch
    {
        TeaP1DrainageStartsOn.TheTanks => shuffled.Where(role => role.IsTank()).ToList(),
        TeaP1DrainageStartsOn.Others => shuffled.Where(role => !role.IsTank()).Take(DrainageTethers).ToList(),
        _ => shuffled.Take(DrainageTethers).ToList(),
    };
}
