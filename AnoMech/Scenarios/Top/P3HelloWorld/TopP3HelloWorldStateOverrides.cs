using System;
using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.Top.P3HelloWorld;

// The debuffs a pair gets from Hello, World; the kit's index is its pair's index in the state's Kits.
public enum HelloWorldKit { Defamation, Stack, RemoteFirst, LocalFirst }

public enum RotColour { Red, Blue }

// User-controlled overrides for TopP3HelloWorldState's randomized fields. Bound by the scenario's
// settings UI; null values leave the field randomized at scenario start.
public sealed class TopP3HelloWorldStateOverrides
{
    // The defamation pair's rot colour; the stack pair gets the other.
    public RotColour? DefamationColour { get; set; }

    // Null rolls a new tower layout every set.
    public bool? SameLayoutEverySet { get; set; }

    // Two seats per kit; a seat arriving at a full one keeps the roll.
    public PerRoleSetting<HelloWorldKit> Kit { get; set; } = new();

    // Headless runs pin the rest of the roll. Internal, so settings sync and the summary skip them.
    internal IReadOnlyList<PartyRole>? KitOrder { get; set; }
    internal IReadOnlyList<Direction>? Layouts { get; set; }

    public SettingsConflicts Validate()
    {
        var conflicts = new SettingsConflicts();
        if (!PerRole.SeatsActive) return conflicts;
        foreach (var kit in Enum.GetValues<HelloWorldKit>())
            conflicts.AtMost(2, PerRole.All.Where(r => Kit[r] == kit).ToList(), $"the {KitName(kit)} kit");
        return conflicts;
    }

    public static string KitName(HelloWorldKit kit) => kit switch
    {
        HelloWorldKit.Defamation => "defamation",
        HelloWorldKit.Stack => "stack",
        HelloWorldKit.RemoteFirst => "remote first",
        _ => "local first",
    };
}
