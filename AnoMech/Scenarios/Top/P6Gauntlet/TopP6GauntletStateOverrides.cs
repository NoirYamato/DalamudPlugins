using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.Top.P6Gauntlet;

// User-forced rolls and setup for TopP6GauntletState, bound by the scenario's settings UI; null leaves a roll random.
public sealed class TopP6GauntletStateOverrides
{
    // Where the run starts; null plays the whole phase.
    public TopP6GauntletPart? StartFrom { get; set; }

    // Cosmo Arrow 1 and 2: true = in first, false = out first.
    public bool? Arrow1InFirst { get; set; }

    // Each Unlimited Wave Cannon's first exaflare (0 north, then clockwise every 45°) and the way the others follow it.
    public int? Exaflares1First { get; set; }
    public bool? Exaflares1Clockwise { get; set; }

    public bool? Arrow2InFirst { get; set; }

    public int? Exaflares2First { get; set; }
    public bool? Exaflares2Clockwise { get; set; }

    // Three seats get a Cosmo Meteor flare; a seat arriving at a full set keeps the roll.
    public PerRoleSetting<bool> Flare { get; set; } = new();

    // The human's own limit breaks 3 in its seat's pair; null follows LPDU's sheet: tanks and melee by job priority,
    // healers by seat.
    public TopP6TankLimitBreaks? TankLimitBreaks { get; set; }
    public TopP6MeleeLimitBreaks? MeleeLimitBreaks { get; set; }
    public TopP6HealerLimitBreaks? HealerLimitBreaks { get; set; }

    // Headless runs pin these. Internal, so settings sync and the summary skip them.
    internal (TopP6GauntletPart First, TopP6GauntletPart Last)? Parts { get; set; }
    internal float? Exaflares1Step { get; set; }
    internal float? Exaflares2Step { get; set; }
    internal IReadOnlyList<PartyRole>? Lasers1 { get; set; }
    internal PartyRole? WildCharge1Target { get; set; }
    internal IReadOnlyList<PartyRole>? Lasers2 { get; set; }
    internal PartyRole? WildCharge2Target { get; set; }
    // Each Cosmo Meteor spread set's first four, in set order.
    internal IReadOnlyList<PartyRole[]>? FirstSpreads { get; set; }
    internal IReadOnlyList<PartyRole>? Flares { get; set; }
    internal PartyRole? StackTarget { get; set; }
    internal bool PlayerPressesItsOwnLimitBreaks { get; set; }

    public SettingsConflicts Validate()
    {
        var conflicts = new SettingsConflicts();
        if (!PerRole.SeatsActive) return conflicts;
        conflicts.AtMost(TopP6GauntletState.FlareTargets, PerRole.All.Where(r => Flare[r] == true).ToList(), "a flare");
        conflicts.AtLeast(TopP6GauntletState.FlareTargets, PerRole.All.Where(r => Flare[r] == false).ToList(), 8, "a flare");
        return conflicts;
    }
}
