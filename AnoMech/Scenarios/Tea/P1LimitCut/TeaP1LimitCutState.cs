using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Scenarios.Tea.P1LimitCut;

// The line the first two Hawk Blasters stand on.
public enum TeaP1FirstBlasters { WestEast, NorthwestSoutheast, NorthSouth, NortheastSouthwest }

// Per-run rolls: the numbers, a plain permutation of the party, and the first Hawk Blaster pair's axis. UNVERIFIED:
// how often the game picks each axis.
public sealed class TeaP1LimitCutState
{
    private readonly Rng rng;

    // Numbers[k] holds number k + 1.
    public IReadOnlyList<PartyRole> Numbers { get; }
    public TeaP1FirstBlasters FirstBlasters { get; }
    public Automarkers Automarkers { get; }
    public float CruiseChaserLandsBehind { get; }

    // Degrees clockwise from north of the first pair's blaster in the west-to-north-east half.
    public float FirstBearing => (270f + 45f * (int)FirstBlasters) % 360f;

    // Each pair blows 45 degrees clockwise of the last; the fifth lands on the first's spots.
    public (Vector3 A, Vector3 B) HawkBlasterPair(int index)
    {
        var bearing = FirstBearing + 45f * (index % 4);
        return (HawkBlasterCentre(bearing), HawkBlasterCentre(bearing + 180f));
    }

    public static Vector3 HawkBlasterCentre(float bearingDegrees)
    {
        var radians = bearingDegrees * MathF.PI / 180f;
        return new Vector3(MathF.Sin(radians), 0f, -MathF.Cos(radians)) * P1Geometry.HawkBlasterRadius;
    }

    // The run's own dice, for the one choice made mid-run: whom a dead number's hit goes to.
    public T PickRandom<T>(T[] values) => rng.NextObj(values);

    public TeaP1LimitCutState(Rng rng, SimParty party, TeaP1LimitCutStateOverrides overrides)
    {
        this.rng = rng;
        // Both rolls are drawn whether or not they are overridden, so pinning one leaves the other as the seed had it.
        var axis = (TeaP1FirstBlasters)rng.NextInt(4);
        var numbers = RollNumbers(party.PlayerRole, overrides);
        FirstBlasters = overrides.FirstBlasters ?? axis;
        Numbers = overrides.Numbers ?? numbers;
        Automarkers = overrides.Automarkers;
        CruiseChaserLandsBehind = overrides.CruiseChaserLandsBehind ?? P1Geometry.CruiseChaserLandsBehind;
    }

    // A forced number takes its slot outright; a second seat asking for the same one keeps the roll, since the eight
    // numbers are a permutation.
    private List<PartyRole> RollNumbers(PartyRole localPlayerRole, TeaP1LimitCutStateOverrides overrides)
    {
        var numbers = rng.Shuffle(PerRole.All).ToList();
        var taken = new bool[8];
        foreach (var (role, wanted) in overrides.Number.Resolve(localPlayerRole))
        {
            if (wanted is < 1 or > 8) continue;
            var index = wanted - 1;
            if (taken[index])
            {
                DiagnosticLog.Warn($"[TeaP1LimitCut] {role} asked for number {wanted}, already forced for {numbers[index]} -- leaving {role} to the roll.");
                continue;
            }
            var current = numbers.IndexOf(role);
            if (current != index) (numbers[current], numbers[index]) = (numbers[index], numbers[current]);
            taken[index] = true;
        }
        return numbers;
    }
}
