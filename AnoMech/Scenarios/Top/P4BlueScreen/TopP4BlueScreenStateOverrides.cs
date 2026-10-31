using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.Top.P4BlueScreen;

// User-controlled overrides for TopP4BlueScreenState's randomized fields. Bound by the scenario's
// settings UI; null values leave the field randomized at scenario start.
public sealed class TopP4BlueScreenStateOverrides
{
    // Two seats get a line-stack marker in each set; a seat arriving at a full set keeps the roll.
    public PerRoleSetting<bool> MarkerInSet1 { get; set; } = new();
    public PerRoleSetting<bool> MarkerInSet2 { get; set; } = new();
    public PerRoleSetting<bool> MarkerInSet3 { get; set; } = new();

    // Headless runs pin every set's two targets, in set order. Internal, so settings sync and the summary skip it.
    internal IReadOnlyList<PartyRole[]>? StackTargets { get; set; }

    // set: 0 for the first set.
    public PerRoleSetting<bool> MarkerInSet(int set) => set switch
    {
        0 => MarkerInSet1,
        1 => MarkerInSet2,
        _ => MarkerInSet3,
    };

    public SettingsConflicts Validate()
    {
        var conflicts = new SettingsConflicts();
        if (!PerRole.SeatsActive) return conflicts;
        for (var set = 0; set < TopP4BlueScreenState.Sets; set++)
        {
            var marker = MarkerInSet(set);
            var what = $"a marker in set {set + 1}";
            conflicts.AtMost(TopP4BlueScreenState.TargetsPerSet, PerRole.All.Where(r => marker[r] == true).ToList(), what);
            conflicts.AtLeast(TopP4BlueScreenState.TargetsPerSet, PerRole.All.Where(r => marker[r] == false).ToList(), 8, what);
        }
        return conflicts;
    }
}
