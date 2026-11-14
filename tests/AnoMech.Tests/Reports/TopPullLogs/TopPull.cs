using System.Numerics;
using AnoMech.Scenarios.Top;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP3Constants;
using static AnoMech.Scenarios.Top.TopP4Constants;

namespace AnoMech.Tests;

// One TOP pull.
internal sealed class TopPull(ActPull pull) : PullLog(pull, standing: false)
{
    public static readonly Vector2 Centre = new(TopZone.Instance.Origin.X, TopZone.Instance.Origin.Z);

    // Wave Repeater's middle, then its rings outward; P3 and P4 play the same.
    private static readonly uint[] WaveRepeaterRings =
        [P3ActionId.WaveRepeater, P3ActionId.WaveRepeaterRing2, P3ActionId.WaveRepeaterRing3, P3ActionId.WaveRepeaterRing4];

    public static bool IsWaveRepeaterRing(uint action) => WaveRepeaterRings.Contains(action);

    public static string WaveRepeaterRing(uint action) => $"ring {Array.IndexOf(WaveRepeaterRings, action) + 1}";

    // The mechanics that show how far a pull got, in fight order, each by a cast only it has.
    public static readonly IReadOnlyList<(string Name, uint[] Casts)> Milestones =
    [
        ("P1", []),
        ("P2", [ActionId.PartySynergyM, ActionId.PartySynergyF]),
        ("P3 Intermission", [P3ActionId.WaveRepeater]),
        ("P3 Hello World", [P3ActionId.HelloWorld]),
        ("P3 Monitors", [P3ActionId.OversampledWaveCannonEast, P3ActionId.OversampledWaveCannonWest]),
        ("P4 Blue Screen", [P4ActionId.WaveCannonCast]),
        ("P5", [ActionId.RunMiDeltaVersion]),
        ("P6", [ActionId.CosmoMemory]),
    ];

    public string Reached => Milestones.Last(m => m.Casts.Length == 0 || Casts.Any(c => m.Casts.Contains(c.Action))).Name;

    // Degrees clockwise from north, 0 to 360.
    public static double Bearing(Vector2 position) => Bearing(position, Centre);
}
