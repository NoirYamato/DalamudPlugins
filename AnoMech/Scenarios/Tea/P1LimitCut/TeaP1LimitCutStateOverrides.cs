using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.Tea.P1LimitCut;

// User-controlled overrides for TeaP1LimitCutState's randomized fields. Bound by the scenario's settings UI; null
// values leave the field randomized at scenario start.
public sealed class TeaP1LimitCutStateOverrides
{
    public TeaP1FirstBlasters? FirstBlasters { get; set; }

    // 1..8. Two seats can't hold the same number; the later one keeps the roll.
    public PerRoleSetting<int> Number { get; set; } = new();

    // Headless runs pin all eight, number 1 first. Internal, so settings sync and the summary skip it.
    internal IReadOnlyList<PartyRole>? Numbers { get; set; }

    // Headless runs try the landing distances the game might use.
    internal float? CruiseChaserLandsBehind { get; set; }

    public SettingsConflicts Validate()
    {
        var conflicts = new SettingsConflicts();
        if (!PerRole.SeatsActive) return conflicts;
        foreach (var number in Enumerable.Range(1, 8))
            conflicts.AtMost(1, PerRole.All.Where(r => Number[r] == number).ToList(), $"number {number}");
        return conflicts;
    }
}
