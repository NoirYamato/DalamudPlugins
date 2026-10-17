using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Scenarios.Top.P3Monitors;

public class TopP3MonitorsLpduAi : IScenarioAi<TopP3MonitorsState>
{
    public string Name => "[LPDU] Light PF";
    public string? Group => "EU";

    private const float LineUpRadius = 13.15f;
    private const float LineUpFirstBearingDegrees = 320f;
    private const float LineUpStepDegrees = 15.1f;

    private static readonly PartyRole[] NorthToSouthLineUp =
        [RegenHealer, ShieldHealer, MainTank, OffTank, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps];

    private static readonly float[] MonitorBeamRotationsWhenOmegaBeamsWest = [MathF.PI / 2, MathF.PI, 0f];

    private TopP3MonitorsState state = null!;

    public void Run(TopP3MonitorsState s, SimWorld world)
    {
        state = s;
        var ai = new AiManager(world);
        ai.Move(0.3f, WestArcLineUp);
        ai.Move(2f, MonitorAndNonMonitorSpots);
        world.Events.Add(9f, () => TurnMonitorsToAimTheirBeams(world));
    }

    private IAiMove WestArcLineUp()
    {
        return AiMove.Create(Enumerable.Range(0, 8).Select(LineUpSpot).ToArray())
                     .Assignments(NorthToSouthLineUp);
    }

    private IAiMove MonitorAndNonMonitorSpots()
    {
        return AiMove.Create(
                         new(10.05f, -12.18f),
                         new(-9.84f, -8.35f),
                         new(-9.92f, 9.24f),
                         new(1.98f, -16.60f),
                         new(13.54f, -4.44f),
                         new(13.71f, 4.99f),
                         new(6.14f, 11.20f),
                         new(2.23f, 18.34f)
                     )
                     .Assignments(MonitorsThenOthersInLineUpOrder())
                     .ApplyPositions(MirrorWhenOmegaBeamsEast);
    }

    private void TurnMonitorsToAimTheirBeams(SimWorld world)
    {
        var monitors = MonitorsInLineUpOrder();
        for (var i = 0; i < monitors.Count; i++)
        {
            if (world.Party.Get(monitors[i]) is not { } member || !member.IsAlive()) continue;
            member.TurnTo(BodyRotationAiming(MonitorBeamRotation(i), state.SideOf(monitors[i])));
        }
    }

    private void MirrorWhenOmegaBeamsEast(IAiPositions move)
    {
        if (state.OmegaMonitor == OmegaMonitor.East)
            move.MultiplyX(-1f);
    }

    private float MonitorBeamRotation(int monitorIndex)
    {
        var rotation = MonitorBeamRotationsWhenOmegaBeamsWest[monitorIndex];
        return state.OmegaMonitor == OmegaMonitor.East ? -rotation : rotation;
    }

    private static float BodyRotationAiming(float beamRotation, MonitorSide side)
        => beamRotation - side.Mul() * MathF.PI / 2;

    private List<PartyRole> MonitorsInLineUpOrder()
        => NorthToSouthLineUp.Where(state.HasMonitor).ToList();

    private List<PartyRole> MonitorsThenOthersInLineUpOrder()
        => MonitorsInLineUpOrder().Concat(NorthToSouthLineUp.Where(r => !state.HasMonitor(r))).ToList();

    private static Vector2? LineUpSpot(int index)
    {
        var bearing = (LineUpFirstBearingDegrees - LineUpStepDegrees * index) * MathF.PI / 180f;
        return new Vector2(LineUpRadius * MathF.Sin(bearing), -LineUpRadius * MathF.Cos(bearing));
    }
}
