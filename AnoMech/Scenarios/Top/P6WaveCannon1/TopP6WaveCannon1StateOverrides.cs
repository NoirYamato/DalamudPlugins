using System.Collections.Generic;
using AnoMech.Core.Game.Party;

namespace AnoMech.Scenarios.Top.P6WaveCannon1;

// User-controlled overrides for TopP6WaveCannon1State's randomized fields. Bound by the scenario's settings UI;
// null values leave the field randomized at scenario start.
public sealed class TopP6WaveCannon1StateOverrides
{
    // The first exaflare's compass point: 0 north, then clockwise every 45°.
    public int? FirstExaflare { get; set; }

    // True: the other three exaflares follow the first clockwise.
    public bool? Clockwise { get; set; }

    // Headless runs pin these. Internal, so settings sync and the summary skip them.
    internal IReadOnlyList<PartyRole>? ProteanOrder { get; set; }
    internal PartyRole? WildChargeTarget { get; set; }
    internal float? ExaflareStep { get; set; }
}
