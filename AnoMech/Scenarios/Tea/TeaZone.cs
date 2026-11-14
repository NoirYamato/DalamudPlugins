using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Map;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Tea;

public sealed class TeaZone : IZone
{
    public const string WentPastTheRim = "Went past the rim: the arena has no wall, and whoever leaves it dies";

    public static readonly TeaZone Instance = new();
    public static readonly Phase P1 = new(Instance, "P1", TeaP1Constants.Weather, TeaP1Constants.Bgm);

    public string Name => "The Epic of Alexander";
    public uint TerritoryId => 887;
    public Vector3 Origin => new(100f, 0f, 100f);

    public IReadOnlyList<WaymarkLayout> WaymarkPresets { get; } =
        [new WaymarkLayout("EU", TeaP1Constants.EuWaymarks)];

    // The object and its spot are the fight's. UNVERIFIED: its two layout ids, read from the territory's layout and
    // not from a zone-in packet.
    public EntranceBarrier EntranceBarrier => new(2000182, 0x7C1907, 0x7C190B, new Vector3(0f, 0f, 15f));

    public void Run(SimWorld world) => world.EnforceArenaBoundary(TeaP1Constants.P1Geometry.ArenaRadius, WentPastTheRim);
}
