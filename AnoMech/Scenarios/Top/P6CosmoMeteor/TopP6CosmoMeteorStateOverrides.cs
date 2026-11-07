using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.Top.P6CosmoMeteor;

// User-controlled overrides for TopP6CosmoMeteorState's randomized fields. Bound by the scenario's
// settings UI; null values leave the field randomized at scenario start.
public sealed class TopP6CosmoMeteorStateOverrides
{
    // Three seats get a flare; a seat arriving at a full set keeps the roll.
    public PerRoleSetting<bool> Flare { get; set; } = new();

    // Headless runs pin these. Internal, so settings sync and the summary skip them.
    internal IReadOnlyList<PartyRole>? Flares { get; set; }
    internal PartyRole? StackTarget { get; set; }
    // Each set's first four, in set order.
    internal IReadOnlyList<PartyRole[]>? FirstSpreads { get; set; }

    public SettingsConflicts Validate()
    {
        var conflicts = new SettingsConflicts();
        if (!PerRole.SeatsActive) return conflicts;
        conflicts.AtMost(TopP6CosmoMeteorState.FlareTargets, PerRole.All.Where(r => Flare[r] == true).ToList(), "a flare");
        conflicts.AtLeast(TopP6CosmoMeteorState.FlareTargets, PerRole.All.Where(r => Flare[r] == false).ToList(), 8, "a flare");
        return conflicts;
    }
}
