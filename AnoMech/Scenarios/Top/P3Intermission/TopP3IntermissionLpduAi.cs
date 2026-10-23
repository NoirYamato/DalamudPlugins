using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Scenarios.Top.P3Intermission;

public class TopP3IntermissionLpduAi : IScenarioAi<TopP3IntermissionState>
{
    public string Name => "[LPDU] Light PF";
    public string? Group => "EU";

    private const float CongaLineZ = 9.6f;
    private const float CongaLineWestX = -9f;
    private const float CongaLineStep = 2.57f;
    private const float SpreadReadStepZ = 13.6f;
    private const float StackReadStepZ = 5.6f;

    private const float WallRadius = 18.73f;
    private const float StepInRadius = 16.77f;
    private const float SpentCircleRadius = 16.62f;
    private const float WaitLeanDegrees = 10.8f;
    private const float StepInLeanDegrees = 10.3f;
    private const float SpentCircleLeanDegrees = 15.7f;
    private const float WallToNeighbourArmDegrees = 30f;
    private const float LooseSpreadRadius = 8.5f;

    private static readonly PartyRole[] WestToEastCongaLine =
        [PhysRangedDps, MeleeDpsA, MainTank, RegenHealer, ShieldHealer, OffTank, MeleeDpsB, CasterDps];

    private static readonly float[] WallBearingsBySpreadThenStackThenNothing = [270f, 210f, 150f, 90f, 330f, 30f, 330f, 30f];

    private static readonly Sign[] SignsBySpreadThenStackThenNothing =
        [Sign.Attack1, Sign.Attack2, Sign.Attack3, Sign.Attack4, Sign.Bind1, Sign.Bind2, Sign.Ignore1, Sign.Ignore2];

    private static readonly Dictionary<PartyRole, float> HelloWorldLooseSpreadBearings = new()
    {
        [MainTank] = 0f, [PhysRangedDps] = 45f, [ShieldHealer] = 90f, [MeleeDpsB] = 135f,
        [OffTank] = 180f, [MeleeDpsA] = 225f, [RegenHealer] = 270f, [CasterDps] = 315f,
    };

    private TopP3IntermissionState state = null!;

    public void Run(TopP3IntermissionState s, SimWorld world)
    {
        state = s;
        var ai = new AiManager(world);
        ai.Move(0.5f, CongaLine);
        ai.Move(9.4f, SpreadsStepBackStacksStepForward);
        if (state.Automarkers == Automarkers.On)
            ai.Automarker(9.7f, MarkInCongaLineOrder);
        ai.Move(10.9f, () => GroupSpots(WallSpot));
        ai.Move(12.6f, () => GroupSpots(WaitLeaningAwayFromTheFirstArms));
        ai.Move(18.6f, () => GroupSpots(StepInBehindTheThirdRing));
        ai.Move(21f, () => GroupSpots(WaitLeaningAwayFromTheFirstArms));
        ai.Move(26.7f, () => GroupSpots(IntoTheFirstArmsSpentCircle), arrivalTime: 28f);
        ai.Move(29.2f, () => GroupSpots(StraightInBesideTheVoidZone));
        ai.Move(30.8f, HelloWorldLooseSpread);
    }

    private IAiMove CongaLine()
        => AiMove.Create(WestToEastCongaLine.Select((_, slot) => (Vector2?)CongaSpot(slot, CongaLineZ)).ToArray())
                 .Assignments(WestToEastCongaLine);

    private IAiMove SpreadsStepBackStacksStepForward()
        => AiMove.Create(WestToEastCongaLine.Select((role, slot) => state.DebuffOf(role) switch
                 {
                     SniperDebuff.Spread => CongaSpot(slot, SpreadReadStepZ),
                     SniperDebuff.Stack => CongaSpot(slot, StackReadStepZ),
                     _ => (Vector2?)null,
                 }).ToArray())
                 .Assignments(WestToEastCongaLine);

    private Dictionary<PartyRole, Sign> MarkInCongaLineOrder()
        => SpreadsThenStacksThenNothingsInCongaLineOrder()
           .Select((role, i) => (role, sign: SignsBySpreadThenStackThenNothing[i]))
           .ToDictionary(t => t.role, t => t.sign);

    private IAiMove GroupSpots(Func<float, Vector2> spotFromWallBearing)
        => AiMove.Create(WallBearingsBySpreadThenStackThenNothing.Select(b => (Vector2?)spotFromWallBearing(b)).ToArray())
                 .Assignments(SpreadsThenStacksThenNothingsInCongaLineOrder());

    private static Vector2 WallSpot(float wallBearing) => OnRing(wallBearing, WallRadius);

    private Vector2 WaitLeaningAwayFromTheFirstArms(float wallBearing)
        => OnRing(wallBearing + WaitLeanDegrees * TowardsTheSecondArms(wallBearing), WallRadius);

    private Vector2 StepInBehindTheThirdRing(float wallBearing)
        => OnRing(wallBearing + StepInLeanDegrees * TowardsTheSecondArms(wallBearing), StepInRadius);

    private Vector2 IntoTheFirstArmsSpentCircle(float wallBearing)
        => OnRing(wallBearing - SpentCircleLeanDegrees * TowardsTheSecondArms(wallBearing), SpentCircleRadius);

    private Vector2 StraightInBesideTheVoidZone(float wallBearing)
        => OnRing(wallBearing - SpentCircleLeanDegrees * TowardsTheSecondArms(wallBearing), LooseSpreadRadius);

    private float TowardsTheSecondArms(float wallBearing)
        => FirstArmsStandCounterClockwiseOf(wallBearing) ? 1f : -1f;

    private bool FirstArmsStandCounterClockwiseOf(float wallBearing)
    {
        var counterClockwiseArm = ((wallBearing - WallToNeighbourArmDegrees) % 360f + 360f) % 360f;
        return state.FirstSetBearings.Any(b => MathF.Abs(b - counterClockwiseArm) < 1f);
    }

    private static IAiMove HelloWorldLooseSpread()
        => AiMove.Create(PerRole.All.Select(role => (Vector2?)OnRing(HelloWorldLooseSpreadBearings[role], LooseSpreadRadius)).ToArray())
                 .Assignments(PerRole.All);

    private List<PartyRole> SpreadsThenStacksThenNothingsInCongaLineOrder()
        => new[] { SniperDebuff.Spread, SniperDebuff.Stack, SniperDebuff.Nothing }
           .SelectMany(debuff => WestToEastCongaLine.Where(role => state.DebuffOf(role) == debuff))
           .ToList();

    private static Vector2 CongaSpot(int slot, float z) => new(CongaLineWestX + CongaLineStep * slot, z);

    private static Vector2 OnRing(float bearingDegrees, float radius)
    {
        var radians = bearingDegrees * MathF.PI / 180f;
        return new Vector2(radius * MathF.Sin(radians), -radius * MathF.Cos(radians));
    }
}
