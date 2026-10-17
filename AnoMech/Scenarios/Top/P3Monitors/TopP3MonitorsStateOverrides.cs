using System.Linq;

namespace AnoMech.Scenarios.Top.P3Monitors;

// Which half of the arena Omega's own monitor beams into.
public enum OmegaMonitor { West, East }

// Which hand a player's monitor sits on: Oversampled Wave Cannon Loading 3453 or 3452.
public enum MonitorSide { Left, Right }

// User-controlled overrides for TopP3MonitorsState's randomized fields. Bound by the scenario's
// settings UI; null values leave the field randomized at scenario start.
public sealed class TopP3MonitorsStateOverrides
{
    public OmegaMonitor? OmegaMonitor { get; set; }

    // Three seats get a monitor; a seat arriving at a full set keeps the roll. A side only applies
    // to a seat that ends up with one.
    public PerRoleSetting<bool> Monitor { get; set; } = new();
    public PerRoleSetting<MonitorSide> Side { get; set; } = new();

    public SettingsConflicts Validate()
    {
        var conflicts = new SettingsConflicts();
        if (!PerRole.SeatsActive) return conflicts;
        conflicts.AtMost(TopP3MonitorsState.PlayerMonitors, PerRole.All.Where(r => Monitor[r] == true).ToList(), "a monitor");
        conflicts.AtLeast(TopP3MonitorsState.PlayerMonitors, PerRole.All.Where(r => Monitor[r] == false).ToList(), 8, "a monitor");
        return conflicts;
    }
}
