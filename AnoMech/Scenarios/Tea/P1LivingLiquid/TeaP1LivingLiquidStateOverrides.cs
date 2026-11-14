using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.Tea.P1LivingLiquid;

// User-forced rolls and setup for TeaP1LivingLiquidState, bound by the scenario's settings UI; null leaves a roll
// random.
public sealed class TeaP1LivingLiquidStateOverrides
{
    // Where the run starts; null plays everything built.
    public TeaP1LivingLiquidPart? StartFrom { get; set; }

    // The quarter each set of tornadoes leaves open: the set standing through Protean 1, and Cascade 2's.
    public TeaP1OpenQuarter? Tornadoes1 { get; set; }
    public TeaP1OpenQuarter? Tornadoes2 { get; set; }

    public TeaP1DrainageStartsOn? DrainageStartsOn { get; set; }

    // Six seats get Throttle; a seat arriving at a full set keeps the roll.
    public PerRoleSetting<bool> Throttle { get; set; } = new();

    // Headless runs pin these. Internal, so settings sync and the summary skip them.
    internal (TeaP1LivingLiquidPart First, TeaP1LivingLiquidPart Last)? Parts { get; set; }
    internal IReadOnlyList<TeaP1DrainageRoll>? Drainage { get; set; }
    internal IReadOnlyList<PartyRole>? Throttled { get; set; }
    internal IReadOnlyList<TeaP1EmbolusRoll>? Emboli { get; set; }
    internal float? EmbolusSpeed { get; set; }
    internal bool PlayerPressesItsOwnActions { get; set; }

    public SettingsConflicts Validate()
    {
        var conflicts = new SettingsConflicts();
        if (!PerRole.SeatsActive) return conflicts;
        conflicts.AtMost(TeaP1LivingLiquidState.ThrottleTargets, PerRole.All.Where(r => Throttle[r] == true).ToList(), "Throttle");
        conflicts.AtLeast(TeaP1LivingLiquidState.ThrottleTargets, PerRole.All.Where(r => Throttle[r] == false).ToList(), 8, "Throttle");
        return conflicts;
    }
}
