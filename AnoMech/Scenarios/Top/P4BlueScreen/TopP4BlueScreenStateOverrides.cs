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

    // True puts both of a set's markers in one light party, false one in each. The seat rows still pick who
    // within that; a forced-in seat the shape has no room for is dropped.
    public bool? OneLightPartyInSet1 { get; set; }
    public bool? OneLightPartyInSet2 { get; set; }
    public bool? OneLightPartyInSet3 { get; set; }

    // Headless runs pin every set's two targets, in set order. Internal, so settings sync and the summary skip it.
    internal IReadOnlyList<PartyRole[]>? StackTargets { get; set; }

    // set: 0 for the first set.
    public PerRoleSetting<bool> MarkerInSet(int set) => set switch
    {
        0 => MarkerInSet1,
        1 => MarkerInSet2,
        _ => MarkerInSet3,
    };

    public bool? OneLightPartyInSet(int set) => set switch
    {
        0 => OneLightPartyInSet1,
        1 => OneLightPartyInSet2,
        _ => OneLightPartyInSet3,
    };

    public void SetOneLightPartyInSet(int set, bool? value)
    {
        switch (set)
        {
            case 0: OneLightPartyInSet1 = value; break;
            case 1: OneLightPartyInSet2 = value; break;
            default: OneLightPartyInSet3 = value; break;
        }
    }

    public SettingsConflicts Validate()
    {
        var conflicts = new SettingsConflicts();
        if (!PerRole.SeatsActive) return conflicts;
        for (var set = 0; set < TopP4BlueScreenState.Sets; set++)
        {
            var marker = MarkerInSet(set);
            var what = $"a marker in set {set + 1}";
            var forcedIn = PerRole.All.Where(r => marker[r] == true).ToList();
            var forcedOut = PerRole.All.Where(r => marker[r] == false).ToList();
            conflicts.AtMost(TopP4BlueScreenState.TargetsPerSet, forcedIn, what);
            conflicts.AtLeast(TopP4BlueScreenState.TargetsPerSet, forcedOut, 8, what);
            switch (OneLightPartyInSet(set))
            {
                case true:
                    LightPartyConflicts(conflicts, set, forcedIn, forcedOut);
                    break;
                case false:
                    SplitConflicts(conflicts, set, forcedIn, forcedOut);
                    break;
            }
        }
        return conflicts;
    }

    private static void LightPartyConflicts(SettingsConflicts conflicts, int set, List<PartyRole> forcedIn, List<PartyRole> forcedOut)
    {
        var shape = $"set {set + 1}'s markers both go to one light party";
        var wanted = TopP4BlueScreenState.LightParties.Where(lp => lp.Any(forcedIn.Contains)).ToList();
        if (wanted.Count > 1)
            conflicts.Add($"{SettingsConflicts.Seats(forcedIn)} all want a marker in set {set + 1}, but {shape}.");
        var allowed = wanted.Count > 0 ? wanted : TopP4BlueScreenState.LightParties.ToList();
        if (allowed.All(lp => lp.Count(r => !forcedOut.Contains(r)) < TopP4BlueScreenState.TargetsPerSet))
            conflicts.Add($"{SettingsConflicts.Seats(forcedOut.Where(r => allowed.Any(lp => lp.Contains(r))))} all refuse a marker in set {set + 1}, but {shape}, so {TopP4BlueScreenState.TargetsPerSet} of its players have to take them.");
    }

    private static void SplitConflicts(SettingsConflicts conflicts, int set, List<PartyRole> forcedIn, List<PartyRole> forcedOut)
    {
        var shape = $"set {set + 1}'s markers go one to each light party";
        foreach (var lp in TopP4BlueScreenState.LightParties)
        {
            if (forcedIn.Count(lp.Contains) > 1)
                conflicts.Add($"{SettingsConflicts.Seats(forcedIn.Where(lp.Contains))} all want a marker in set {set + 1}, but {shape}.");
            if (lp.All(forcedOut.Contains))
                conflicts.Add($"{SettingsConflicts.Seats(lp)} all refuse a marker in set {set + 1}, but {shape}, so one of them has to take it.");
        }
    }
}
