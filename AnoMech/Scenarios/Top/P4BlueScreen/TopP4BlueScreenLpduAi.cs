using System;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Scenarios.Top.P4BlueScreen;

public class TopP4BlueScreenLpduAi : IScenarioAi<TopP4BlueScreenState>
{
    public string Name => "[LPDU] Light PF";
    public string? Group => "EU";

    private const float QuickmarchRadius = 13.5f;
    private const float WestNorthernmostBearing = 292.5f;
    private const float RowStepDegrees = 22.5f;
    private const float StepInRadius = 11f;
    private const float OuterStackRadius = 13f;
    private const float OuterStackDegreesFromSouth = 22.5f;
    private const float InnerStackRadius = 11f;
    private const float InnerStackDegreesFromSouth = 20f;
    private const float FinalStepInRadius = 10.5f;
    private const float UptimeRadius = 6f;

    private static readonly PartyRole[] WestNorthToSouth = [MainTank, PhysRangedDps, RegenHealer, MeleeDpsA];
    private static readonly PartyRole[] EastNorthToSouth = [OffTank, CasterDps, ShieldHealer, MeleeDpsB];

    private TopP4BlueScreenState state = null!;

    public void Run(TopP4BlueScreenState s, SimWorld world)
    {
        state = s;
        var ai = new AiManager(world);
        ai.Move(0.5f, () => EveryoneOnTheirRow(QuickmarchRadius));
        ai.Move(8.7f, () => LightPartiesOnTheirStackSpot(0, OuterStackRadius, OuterStackDegreesFromSouth));
        ai.Move(13.7f, () => EveryoneOnTheirRow(QuickmarchRadius));
        ai.Move(18.6f, () => EveryoneOnTheirRow(StepInRadius));
        ai.Move(19.2f, () => LightPartiesOnTheirStackSpot(1, InnerStackRadius, InnerStackDegreesFromSouth));
        ai.Move(23.5f, () => EveryoneOnTheirRow(QuickmarchRadius));
        ai.Move(28.7f, () => LightPartiesOnTheirStackSpot(2, OuterStackRadius, OuterStackDegreesFromSouth));
        ai.Move(35.3f, () => LightPartiesOnTheirStackSpot(2, FinalStepInRadius, OuterStackDegreesFromSouth));
        ai.Move(39.3f, () => EveryoneOnTheirRow(UptimeRadius));
    }

    private static IAiMove EveryoneOnTheirRow(float radius)
        => AiMove.Create(PerRole.All.Select(role => (Vector2?)OnRing(RowBearing(role), radius)).ToArray())
                 .Assignments(PerRole.All);

    private IAiMove LightPartiesOnTheirStackSpot(int set, float radius, float degreesFromSouth)
        => AiMove.Create(PerRole.All.Select(role => (Vector2?)OnRing(180f + degreesFromSouth * (StandsWest(role, set) ? 1f : -1f), radius)).ToArray())
                 .Assignments(PerRole.All);

    private bool StandsWest(PartyRole role, int set)
    {
        var west = WestNorthToSouth.Contains(role);
        return SouthernmostMarkerAndTheOtherSidesMelee(set) is { } flex && (role == flex.Marker || role == flex.Melee) ? !west : west;
    }

    private (PartyRole Marker, PartyRole Melee)? SouthernmostMarkerAndTheOtherSidesMelee(int set)
    {
        var markers = state.StackTargets(set).List;
        var markedWest = markers.Count(WestNorthToSouth.Contains);
        if (markedWest == 1) return null;
        var markedSide = markedWest == 2 ? WestNorthToSouth : EastNorthToSouth;
        var otherSide = markedWest == 2 ? EastNorthToSouth : WestNorthToSouth;
        return (markers.MaxBy(r => Array.IndexOf(markedSide, r)), otherSide[^1]);
    }

    private static float RowBearing(PartyRole role)
    {
        var westRow = Array.IndexOf(WestNorthToSouth, role);
        return westRow >= 0
            ? WestNorthernmostBearing - RowStepDegrees * westRow
            : 360f - WestNorthernmostBearing + RowStepDegrees * Array.IndexOf(EastNorthToSouth, role);
    }

    private static Vector2 OnRing(float bearingDegrees, float radius)
    {
        var radians = bearingDegrees * MathF.PI / 180f;
        return new Vector2(radius * MathF.Sin(radians), -radius * MathF.Cos(radians));
    }
}
