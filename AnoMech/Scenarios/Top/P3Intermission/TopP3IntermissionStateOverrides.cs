using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.Top.P3Intermission;

// What a player's Sniper Cannon Fodder makes them: a spread (3425), half of a two-player stack (3426), or nothing.
public enum SniperDebuff { Spread, Stack, Nothing }

// Which three arms warp in first: the triangle with an arm at N (N, SE, SW) or the one with an arm at S.
public enum FirstArms { NorthTriangle, SouthTriangle }

// A party-setup option rather than a roll: the fight is the same either way, only a strat reads it.
public enum Automarkers { On, Off }

// User-controlled overrides for TopP3IntermissionState's randomized fields. Bound by the scenario's
// settings UI; null values leave the field randomized at scenario start.
public sealed class TopP3IntermissionStateOverrides
{
    public FirstArms? FirstArms { get; set; }

    public Automarkers Automarkers { get; set; } = Automarkers.On;

    // Four seats spread, two stack and two get nothing; a seat arriving at a full one keeps the roll.
    public PerRoleSetting<SniperDebuff> Debuff { get; set; } = new();

    // Headless runs pin the whole roll: [0-3] spread, [4, 5] stack, [6, 7] nothing. Internal, so
    // settings sync and the summary skip it.
    internal IReadOnlyList<PartyRole>? DebuffOrder { get; set; }

    public SettingsConflicts Validate()
    {
        var conflicts = new SettingsConflicts();
        if (!PerRole.SeatsActive) return conflicts;
        conflicts.AtMost(TopP3IntermissionState.Spreads, PerRole.All.Where(r => Debuff[r] == SniperDebuff.Spread).ToList(), "a spread");
        conflicts.AtMost(TopP3IntermissionState.Stacks, PerRole.All.Where(r => Debuff[r] == SniperDebuff.Stack).ToList(), "a stack");
        conflicts.AtMost(TopP3IntermissionState.Unmarked, PerRole.All.Where(r => Debuff[r] == SniperDebuff.Nothing).ToList(), "no debuff");
        return conflicts;
    }
}
