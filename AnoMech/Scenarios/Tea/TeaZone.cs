using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Map;

namespace AnoMech.Scenarios.Tea;

// No arena boundary: the rim is a wall to stand against, not a death zone.
public sealed class TeaZone : IZone
{
    public static readonly TeaZone Instance = new();
    public static readonly Phase P1 = new(Instance, "P1", TeaP1Constants.Weather, TeaP1Constants.Bgm);

    public string Name => "The Epic of Alexander";
    public uint TerritoryId => 887;
    public Vector3 Origin => new(100f, 0f, 100f);

    public IReadOnlyList<WaymarkLayout> WaymarkPresets { get; } =
        [new WaymarkLayout("EU", TeaP1Constants.EuWaymarks)];

    // UNVERIFIED in the client: read from the territory's layout, not from a zone-in packet.
    public EntranceBarrier EntranceBarrier => new(2000182, 0x7C1907, 0x7C190B, new Vector3(0f, 0f, 15f));
}
