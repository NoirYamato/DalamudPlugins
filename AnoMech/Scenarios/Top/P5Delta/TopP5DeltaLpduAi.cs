using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants.Geometry;

namespace AnoMech.Scenarios.Top.P5Delta;

public sealed class TopP5DeltaLpduAi : IScenarioAi<TopP5DeltaState>
{
    public string Name => "[LPDU] Light PF";
    public string? Group => "EU";

    internal const int BlueInnerNorth = 0;
    internal const int BlueInnerSouth = 1;
    internal const int BlueOuterNorth = 2;
    internal const int BlueOuterSouth = 3;
    internal const int GreenInnerNorth = 4;
    internal const int GreenInnerSouth = 5;
    internal const int GreenOuterNorth = 6;
    internal const int GreenOuterSouth = 7;

    internal const float ArmBaitRadius = 19.5f;
    internal const float ArmBaitTurnRadians = 0.15f;
    internal const float MonitorHolderSidestep = 1.2f;

    internal static readonly Vector2?[] LineUpSpots =
    [
        new(-8.6f, -4f), new(-8.6f, 4f),
        new(-13.5f, -2.5f), new(-13.5f, 2.5f),
        new(6.2f, -4f), new(6.2f, 4f),
        new(11.3f, -9.3f), new(11.3f, 9.3f),
    ];

    internal static readonly Vector2?[] FistSetUpSpots =
    [
        new(-13.5f, -6.5f), new(-13.5f, 6.5f),
        new(-13.5f, -2.5f), new(-13.5f, 2.5f),
        new(9.64f, -9.64f), new(9.64f, 9.64f),
        new(9.64f, -9.64f), new(9.64f, 9.64f),
    ];

    internal static readonly Vector2?[] BeyondDefenseBaits = [new(0f, -5f), new(0f, 5f)];

    internal static readonly Dictionary<int, float> ArmBearingByBaiterPosition = new()
    {
        [BlueOuterNorth] = 300f, [BlueOuterSouth] = 240f,
        [GreenInnerNorth] = 0f, [GreenInnerSouth] = 180f,
        [GreenOuterNorth] = 60f, [GreenOuterSouth] = 120f,
    };

    internal static readonly Vector2?[] GreenStagingInsideEveryFirstPulse =
        [null, null, null, null, new(0f, -12f), new(0f, 12f), new(9f, -10f), new(9f, 10f)];

    internal static readonly Vector2?[] GreenMonitorCorners =
        [null, null, null, null, new(-9.3f, -13f), new(-9.3f, 13f), new(9.3f, -13f), new(9.3f, 13f)];

    internal static readonly Vector2 MonitorHolderSpotWhenOmegasMonitorSparesTheSouth = new(0f, 5.1f);
    internal static readonly Vector2[] PlainBlueStackRowWhenOmegasMonitorSparesTheSouth = [new(-0.4f, 3.2f), new(0.4f, 3.2f), new(0f, 3.2f)];
    internal static readonly Vector2 PlainBlueStackRowCentreWhenOmegasMonitorSparesTheSouth = new(0f, 3.2f);
    internal static readonly Vector2 BeyondDefenseTargetRunTowardTheFinal = new(13f, 0f);

    internal static readonly Vector2 HelloNearWorldSpotWhenTheSouthIsSafe = new(0f, 6f);
    internal static readonly Vector2 HelloDistantWorldSpotWhenTheSouthIsSafe = new(0f, 18.5f);
    internal static readonly Vector2 UnmarkedBlueSpotWhenTheSouthIsSafe = new(9.5f, 16f);
    internal static readonly Vector2 SafeInnerGreenSpotWhenTheSouthIsSafe = new(-9.64f, 9.64f);
    internal static readonly Vector2 UnsafeInnerGreenWaitWhenTheSouthIsSafe = new(-9.6f, -3.4f);
    internal static readonly Vector2 UnsafeInnerGreenCleaveEdgeWhenTheSouthIsSafe = new(-9.6f, 4f);
    internal static readonly Vector2 SafeOuterGreenPlantWhenTheSouthIsSafe = new(15.5f, 10.7f);
    internal static readonly Vector2 UnsafeOuterGreenUnderTheBeetleWhenTheSouthIsSafe = new(-18.5f, 1.5f);

    internal static readonly Vector2[] BackToTheBossWhenTheSouthWasSafe =
        [new(-0.7f, 5.7f), new(-0.7f, 6.5f), new(-0.7f, 7.3f), new(0.7f, 5.7f), new(0.7f, 6.5f), new(0.7f, 7.3f)];
    internal static readonly Vector2 PlantMaxMeleeWhenTheSouthWasSafe = new(8.8f, -0.5f);
    internal static readonly Vector2 BeetleSideMaxMeleeWhenTheSouthWasSafe = new(-8.8f, -0.5f);
    internal static readonly Vector2 PlantWalksInWhenTheSouthWasSafe = new(4f, -0.5f);
    internal static readonly Vector2 BeetleSideWalksInWhenTheSouthWasSafe = new(-4f, -0.5f);
    internal static readonly Vector2 MainTankSolarRaySpotWhenTheSouthWasSafe = new(0f, -5.7f);

    private TopP5DeltaState state = null!;
    private PartyRole[] lineUp = [];
    private PartyRole[] afterTheFlex = [];

    public void Run(TopP5DeltaState s, SimWorld world)
    {
        state = s;
        lineUp = LineUpOrder(s);
        afterTheFlex = OrderAfterTheFistFlex(s);
        var ai = new AiManager(world);
        if (state.Automarkers == Automarkers.On)
            ai.Automarker(10.5f, MarkTheHelloWorldHolders);
        ai.Move(0.5f, TanksNorthOfOmegaTheRestSouth);
        ai.Move(11f, LineUpBlueBesideTheBeetleGreenBesideTheFinal);
        ai.Move(19.5f, FlexThenStretchTheInnerBluesAndStackTheGreens);
        ai.Move(28.3f, PoppedInnerBluesDropOntoTheOuterBlues);
        ai.Move(30.3f, BaitBeyondDefenseAndTheArms);
        ai.Move(35.7f, GreensStageInsideEveryFirstPulse);
        ai.Move(36.2f, InnerBluesToTheirMonitorSpots, jitter: 0f);
        ai.Move(36.2f, OuterBluesSprintToTheirMonitorSpots, jitter: 0f, sprint: true);
        ai.Move(38f, GreensToTheirMonitorCornersBehindTheFirstPulses);
        ai.Move(40f, MonitorHolderSidestepsToTurnItsCleaveOutward, jitter: 0f);
        ai.Move(46f, SwivelCannonSpots);
        ai.Move(48f, UnsafeInnerGreenCollapsesOnceTheRuinsAreGone);
        ai.Move(56.2f, BackToTheBossWithTheOuterGreensAtMaxMelee);
        ai.Move(61.2f, OuterGreensWalkTogetherOnceTheHelloVulnerabilitiesAreGone);
        ai.Move(62f, MainTankForwardForSolarRay);
    }

    internal static PartyRole[] LineUpOrder(TopP5DeltaState state)
    {
        var order = state.TetherOrder.ToArray();
        if (!state.CloseInnerOuterForced) PutTheOnlyPairWithAHealerInside(order, BlueInnerNorth);
        if (!state.FarInnerOuterForced) PutTheOnlyPairWithAHealerInside(order, GreenInnerNorth);
        return order;
    }

    internal static PartyRole[] OrderAfterTheFistFlex(TopP5DeltaState state)
    {
        var order = LineUpOrder(state);
        FlexTheInnerPairWhenItsNorthFistMatchesTheOuterNorths(state, order, BlueInnerNorth);
        FlexTheInnerPairWhenItsNorthFistMatchesTheOuterNorths(state, order, GreenInnerNorth);
        return order;
    }

    private static void PutTheOnlyPairWithAHealerInside(PartyRole[] order, int innerNorth)
    {
        var outerNorth = innerNorth + 2;
        if (PairHasAHealer(order, innerNorth) || !PairHasAHealer(order, outerNorth)) return;
        (order[innerNorth], order[outerNorth]) = (order[outerNorth], order[innerNorth]);
        (order[innerNorth + 1], order[outerNorth + 1]) = (order[outerNorth + 1], order[innerNorth + 1]);
    }

    private static bool PairHasAHealer(PartyRole[] order, int north) => IsHealer(order[north]) || IsHealer(order[north + 1]);

    private static bool IsHealer(PartyRole role) => role is RegenHealer or ShieldHealer;

    private static void FlexTheInnerPairWhenItsNorthFistMatchesTheOuterNorths(TopP5DeltaState state, PartyRole[] order, int innerNorth)
    {
        if (FistOf(state, order[innerNorth]) == FistOf(state, order[innerNorth + 2]))
            (order[innerNorth], order[innerNorth + 1]) = (order[innerNorth + 1], order[innerNorth]);
    }

    private static uint FistOf(TopP5DeltaState state, PartyRole role)
        => state.FistColors[Enumerable.Range(0, state.TetherOrder.Count).First(slot => state.TetherOrder[slot] == role)];

    private Dictionary<PartyRole, Sign> MarkTheHelloWorldHolders()
        => new() { [state.NearWorldRole] = Sign.Triangle, [state.FarWorldRole] = Sign.Cross };

    private static IAiMove TanksNorthOfOmegaTheRestSouth()
        => AiMove.Create(
                     new(-2.1f, -5.08f), new(2.1f, -5.08f),
                     new(-0.7f, 5.7f), new(-0.7f, 6.5f), new(-0.7f, 7.3f),
                     new(0.7f, 5.7f), new(0.7f, 6.5f), new(0.7f, 7.3f))
                 .NaturalOrder();

    private IAiMove LineUpBlueBesideTheBeetleGreenBesideTheFinal() => InEyeFrame(lineUp, LineUpSpots);

    private IAiMove FlexThenStretchTheInnerBluesAndStackTheGreens() => InEyeFrame(afterTheFlex, FistSetUpSpots);

    private IAiMove PoppedInnerBluesDropOntoTheOuterBlues()
        => InEyeFrame(afterTheFlex, FistSetUpSpots[BlueOuterNorth], FistSetUpSpots[BlueOuterSouth]);

    private IAiMove BaitBeyondDefenseAndTheArms()
        => InEyeFrame(afterTheFlex,
                      [
                          .. BeyondDefenseBaits,
                          .. Enumerable.Range(BlueOuterNorth, 6).Select(position => (Vector2?)BaitOppositeTheTurnOfTheArmAt(ArmBearingByBaiterPosition[position])),
                      ]);

    private IAiMove GreensStageInsideEveryFirstPulse() => InEyeFrame(afterTheFlex, GreenStagingInsideEveryFirstPulse);

    private IAiMove InnerBluesToTheirMonitorSpots() => BluesToTheirMonitorSpots(BlueInnerNorth, BlueInnerSouth);

    private IAiMove OuterBluesSprintToTheirMonitorSpots() => BluesToTheirMonitorSpots(BlueOuterNorth, BlueOuterSouth);

    private IAiMove BluesToTheirMonitorSpots(params int[] positions)
        => InEyeFrame(role => positions.Contains(PositionOf(role)) ? MonitorStageSpot(role) : null);

    private IAiMove GreensToTheirMonitorCornersBehindTheFirstPulses() => InEyeFrame(afterTheFlex, GreenMonitorCorners);

    private IAiMove MonitorHolderSidestepsToTurnItsCleaveOutward()
        => InEyeFrame(role => role == state.PlayerMonitorRole
                          ? MonitorStageSpot(role) + new Vector2(MonitorHolderSidestep * MonitorHolderStepsEastToCleaveOutward, 0f)
                          : null);

    private IAiMove SwivelCannonSpots() => InEyeFrame(SwivelCannonSpotOf);

    private IAiMove UnsafeInnerGreenCollapsesOnceTheRuinsAreGone()
        => InEyeFrame(role => role == UnsafeSideOf(GreenInnerNorth)
                          ? OnTheSwivelSafeHalf(UnsafeInnerGreenCleaveEdgeWhenTheSouthIsSafe)
                          : null);

    private IAiMove BackToTheBossWithTheOuterGreensAtMaxMelee() => InEyeFrame(EndSpotOf);

    private IAiMove OuterGreensWalkTogetherOnceTheHelloVulnerabilitiesAreGone()
        => InEyeFrame(role => role == SafeSideOf(GreenOuterNorth) ? OnTheSwivelSafeHalf(PlantWalksInWhenTheSouthWasSafe)
                          : role == UnsafeSideOf(GreenOuterNorth) ? OnTheSwivelSafeHalf(BeetleSideWalksInWhenTheSouthWasSafe)
                          : null);

    private IAiMove MainTankForwardForSolarRay()
        => InEyeFrame(role => role == MainTank ? OnTheSwivelSafeHalf(MainTankSolarRaySpotWhenTheSouthWasSafe) : null);

    private Vector2 BaitOppositeTheTurnOfTheArmAt(float eyeFrameBearingDegrees)
    {
        var arm = ArmAt(eyeFrameBearingDegrees);
        return OnRing(BearingOf(ArmInEyeFrame(arm)) + ArmBaitTurnRadians * state.ArmRotations[arm].Mul, ArmBaitRadius);
    }

    private int ArmAt(float eyeFrameBearingDegrees)
        => Enumerable.Range(0, ArmUnitPlacements.Length)
                     .MinBy(arm => RadiansApart(BearingOf(ArmInEyeFrame(arm)), eyeFrameBearingDegrees * MathF.PI / 180f));

    private Vector2 ArmInEyeFrame(int arm)
    {
        var spawned = ArmUnitPlacements[arm].Position;
        return Turned(new Vector2(spawned.X * state.EyeSpawn.Mul, spawned.Z), -EyeTurnFromNorth);
    }

    private Vector2 MonitorStageSpot(PartyRole role)
    {
        var ownSpot = role == state.PlayerMonitorRole
            ? MonitorHolderSpotWhenOmegasMonitorSparesTheSouth
            : StackRowSpotOf(role);
        var spot = role == state.BeyondDefenseTarget ? ownSpot + BeyondDefenseTargetRunTowardTheFinal : ownSpot;
        return spot with { Y = spot.Y * HalfOmegasMonitorSpares };
    }

    private Vector2 StackRowSpotOf(PartyRole role)
    {
        var plain = PlainBlues().IndexOf(role);
        return plain >= 0
            ? PlainBlueStackRowWhenOmegasMonitorSparesTheSouth[plain]
            : PlainBlueStackRowCentreWhenOmegasMonitorSparesTheSouth;
    }

    private List<PartyRole> PlainBlues()
        => afterTheFlex.Take(4).Where(role => role != state.PlayerMonitorRole && role != state.BeyondDefenseTarget).ToList();

    private Vector2? SwivelCannonSpotOf(PartyRole role)
    {
        var spot = role == state.NearWorldRole ? HelloNearWorldSpotWhenTheSouthIsSafe
            : role == state.FarWorldRole ? HelloDistantWorldSpotWhenTheSouthIsSafe
            : role == SafeSideOf(GreenInnerNorth) ? SafeInnerGreenSpotWhenTheSouthIsSafe
            : role == UnsafeSideOf(GreenInnerNorth) ? UnsafeInnerGreenWaitWhenTheSouthIsSafe
            : role == SafeSideOf(GreenOuterNorth) ? SafeOuterGreenPlantWhenTheSouthIsSafe
            : role == UnsafeSideOf(GreenOuterNorth) ? UnsafeOuterGreenUnderTheBeetleWhenTheSouthIsSafe
            : UnmarkedBlueSpotWhenTheSouthIsSafe;
        return OnTheSwivelSafeHalf(spot);
    }

    private Vector2? EndSpotOf(PartyRole role)
    {
        var spot = role == SafeSideOf(GreenOuterNorth) ? PlantMaxMeleeWhenTheSouthWasSafe
            : role == UnsafeSideOf(GreenOuterNorth) ? BeetleSideMaxMeleeWhenTheSouthWasSafe
            : BackToTheBossWhenTheSouthWasSafe[PositionOf(role)];
        return OnTheSwivelSafeHalf(spot);
    }

    private PartyRole SafeSideOf(int northPosition) => afterTheFlex[SwivelSafeHalf > 0 ? northPosition + 1 : northPosition];

    private PartyRole UnsafeSideOf(int northPosition) => afterTheFlex[SwivelSafeHalf > 0 ? northPosition : northPosition + 1];

    private Vector2 OnTheSwivelSafeHalf(Vector2 spotWhenTheSouthIsSafe)
        => spotWhenTheSouthIsSafe with { Y = spotWhenTheSouthIsSafe.Y * SwivelSafeHalf };

    private int SwivelSafeHalf => state.SwivelCannonSide.Mul;

    private int HalfOmegasMonitorSpares => -state.OmegaMonitorSide.Mul;

    private int MonitorHolderStepsEastToCleaveOutward => -state.PlayerMonitorSide.Mul * HalfOmegasMonitorSpares;

    private float EyeTurnFromNorth => state.EyeSpawn == NorthSouth.North ? 0f : MathF.PI;

    private int PositionOf(PartyRole role) => Array.IndexOf(afterTheFlex, role);

    private IAiMove InEyeFrame(IReadOnlyList<PartyRole> order, params Vector2?[] spots)
        => AiMove.Create(spots).Assignments(order).ApplyPositions(TurnWithTheEye);

    private IAiMove InEyeFrame(Func<PartyRole, Vector2?> spotOf)
        => AiMove.Create(PerRole.All.Select(spotOf).ToArray()).Assignments(PerRole.All).ApplyPositions(TurnWithTheEye);

    private void TurnWithTheEye(IAiPositions move) => move.Rotate(EyeTurnFromNorth);

    private static Vector2 Turned(Vector2 v, float radiansFromNorth)
    {
        var cos = MathF.Cos(radiansFromNorth);
        var sin = MathF.Sin(radiansFromNorth);
        return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }

    private static float BearingOf(Vector2 v) => MathF.Atan2(v.X, -v.Y);

    private static float RadiansApart(float a, float b) => MathF.Abs(MathF.IEEERemainder(a - b, 2f * MathF.PI));

    private static Vector2 OnRing(float bearingRadians, float radius)
        => new(radius * MathF.Sin(bearingRadians), -radius * MathF.Cos(bearingRadians));
}
