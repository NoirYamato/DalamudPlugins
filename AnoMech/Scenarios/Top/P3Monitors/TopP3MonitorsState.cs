using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Top.TopP3Constants;

namespace AnoMech.Scenarios.Top.P3Monitors;

public static class MonitorSides
{
    // CharacterFind.OnSideN's sideMul, as TopP5DeltaState's Side carries it: +1 beams to the
    // owner's left, -1 to its right.
    public static int Mul(this MonitorSide side) => side == MonitorSide.Left ? 1 : -1;

    public static ushort StatusId(this MonitorSide side)
        => side == MonitorSide.Left ? TopConstants.StatusId.PlayerMonitorLeft : TopConstants.StatusId.PlayerMonitorRight;
}

public sealed class TopP3MonitorsState
{
    public const int PlayerMonitors = 3;

    public OmegaMonitor OmegaMonitor { get; }

    // Omega faces north for the whole cast, so its left hand is west.
    public MonitorSide OmegaSide => OmegaMonitor == OmegaMonitor.West ? MonitorSide.Left : MonitorSide.Right;

    public uint OmegaCastId => OmegaMonitor == OmegaMonitor.West
        ? P3ActionId.OversampledWaveCannonWest
        : P3ActionId.OversampledWaveCannonEast;

    // The players with a monitor, in no particular order.
    public RoleList Monitors { get; }

    private readonly Dictionary<PartyRole, MonitorSide> sides;

    public bool HasMonitor(PartyRole role) => sides.ContainsKey(role);

    public MonitorSide SideOf(PartyRole role) => sides[role];

    public TopP3MonitorsState(Rng rng, SimParty party, TopP3MonitorsStateOverrides overrides)
    {
        // Every roll is drawn whether or not it is overridden, so pinning one leaves the others as
        // the seed had them.
        var omegaMonitor = rng.NextObj(OmegaMonitor.West, OmegaMonitor.East);
        OmegaMonitor = overrides.OmegaMonitor ?? omegaMonitor;
        Monitors = new RoleListBuilder
        {
            Size = PlayerMonitors,
            Membership = overrides.Monitor.Resolve(party.PlayerRole).ToDictionary(r => r.Role, r => r.Value),
        }.Build(rng, party);
        var rolledSides = Monitors.List.Select(_ => rng.NextObj(MonitorSide.Left, MonitorSide.Right)).ToList();
        var wantedSides = overrides.Side.Resolve(party.PlayerRole).ToDictionary(r => r.Role, r => r.Value);
        sides = Monitors.List
            .Select((role, i) => (role, side: wantedSides.TryGetValue(role, out var wanted) ? wanted : rolledSides[i]))
            .ToDictionary(t => t.role, t => t.side);
    }
}
