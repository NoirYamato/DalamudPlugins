using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Scenarios.Tea.P1LimitCut;

public class TeaP1LimitCutLpduAi : IScenarioAi<TeaP1LimitCutState>
{
    public string Name => "[LPDU] Light PF";
    public string? Group => "EU";

    private enum Lane { InnerOdd, InnerEven, OuterOdd, OuterEven }

    private const float InnerLaneRadius = 14f;
    private const float OuterLaneRadius = 19.3f;
    private const float InnerOddDegreesBehindTheSpot = 2f;
    private const float InnerEvenDegreesBehindTheSpot = 30f;
    private const float OuterOddDegreesBehindTheSpot = 4f;
    private const float OuterEvenDegreesBehindTheSpot = 8f;
    private const float RunOutOddDegreesBehindTheSpot = 12f;
    private const float RunOutOuterEvenDegreesBehindTheSpot = 16f;
    private const float DegreesBetweenSpots = 45f;
    private const float WaitRadius = 1f;
    private const float JKickRadius = 2.5f;

    private const int OneAndTwoAreHitOnSpot = 2;
    private const int ThreeAndFourAreHitOnSpot = 3;
    private const int FiveAndSixAreHitOnSpot = 5;
    private const int SevenAndEightAreHitOnSpot = 6;

    private static readonly PartyRole[] ClockwiseFromNorthInTheMiddle =
        [MainTank, MeleeDpsB, ShieldHealer, CasterDps, OffTank, PhysRangedDps, RegenHealer, MeleeDpsA];

    private static readonly Sign[] AttackSigns =
        [Sign.Attack1, Sign.Attack2, Sign.Attack3, Sign.Attack4, Sign.Attack5, Sign.Attack6, Sign.Attack7, Sign.Attack8];

    private TeaP1LimitCutState state = null!;
    private SimWorld world = null!;

    public void Run(TeaP1LimitCutState s, SimWorld w)
    {
        state = s;
        world = w;
        var ai = new AiManager(world);
        ai.Move(0.5f, () => EveryoneInTheMiddle(WaitRadius));
        if (state.Automarkers == Automarkers.On)
            ai.Automarker(6.3f, AttackSignsOnTheNumbers);
        ai.Move(8.1f, BothGroupsRunOutWellBehindTheirFirstBlaster);
        ai.Move(10.3f, () => BothGroupsFollowTheBlastersTheirFirstPairInsideTheirSecondOutside(1));
        ai.Move(12.5f, () => BothGroupsFollowTheBlastersTheirFirstPairInsideTheirSecondOutside(2));
        world.Events.Add(14.6f, () => TheOddFacesClockwiseAlongTheRim(1, OneAndTwoAreHitOnSpot));
        ai.Move(14.7f, ThreeAndFourGoOnToTheirHitSpotWithSevenAndEightOutside);
        ai.Move(15.5f, OneWalksOnOutsideAsSoonAsItIsCleaved);
        ai.Move(17.0f, TwoGoesOutsideOnceChargedAndFiveAndSixComeInside);
        ai.Move(19.1f, () => OneAndTwoFollowTheBlastersOutsideFiveAndSixInside(4));
        world.Events.Add(19.3f, () => TheOddFacesClockwiseAlongTheRim(3, ThreeAndFourAreHitOnSpot));
        ai.Move(20.2f, ThreeWalksOnOutsideAsSoonAsItIsCleavedAndSevenAndEightComeInside);
        ai.Move(21.3f, () => OneAndTwoFollowTheBlastersOutsideFiveAndSixInside(5));
        ai.Move(21.7f, FourLeavesOnTheInnerLaneOnceCharged);
        ai.Move(22.5f, ThreeSevenAndEightFollowTheBlastersOn);
        ai.Move(23.6f, FourGoesOutsideOnceTheNextSpotHasBlown);
        world.Events.Add(24.0f, () => TheOddFacesClockwiseAlongTheRim(5, FiveAndSixAreHitOnSpot));
        ai.Move(24.25f, SevenAndEightGoOnToTheirHitSpot);
        ai.Move(24.9f, FiveWalksOnOutsideAsSoonAsItIsCleavedAndThreeGoesOnToItsLastSpot);
        ai.Move(26.4f, OneTwoAndSixGoOnToTheirLastSpotOnceSixIsCharged);
        ai.Move(26.6f, FourGoesOnToItsLastSpot);
        world.Events.Add(28.6f, () => TheOddFacesClockwiseAlongTheRim(7, SevenAndEightAreHitOnSpot));
        ai.Move(29.5f, SevenWalksOutsideAsSoonAsItIsCleaved);
        ai.Move(31.0f, () => EveryoneInTheMiddle(JKickRadius));
    }

    private static IAiMove EveryoneInTheMiddle(float radius)
        => AiMove.Create(ClockwiseFromNorthInTheMiddle.Select((_, i) => (Vector2?)OnRing(DegreesBetweenSpots * i, radius)).ToArray())
                 .Assignments(ClockwiseFromNorthInTheMiddle);

    private Dictionary<PartyRole, Sign> AttackSignsOnTheNumbers()
        => state.Numbers.Select((role, k) => (role, Sign: AttackSigns[k])).ToDictionary(n => n.role, n => n.Sign);

    private IAiMove BothGroupsRunOutWellBehindTheirFirstBlaster()
        => ByNumber(n => n % 2 == 0 && n <= 4
            ? LaneSpot(n, Lane.InnerEven, 0)
            : OnRing(GroupBearing(n) - (n % 2 == 1 ? RunOutOddDegreesBehindTheSpot : RunOutOuterEvenDegreesBehindTheSpot),
                     n <= 4 ? InnerLaneRadius : OuterLaneRadius));

    private IAiMove BothGroupsFollowTheBlastersTheirFirstPairInsideTheirSecondOutside(int spot)
        => ByNumber(n => LaneSpot(n, n <= 4 ? InnerLaneOf(n) : OuterLaneOf(n), spot));

    private IAiMove ThreeAndFourGoOnToTheirHitSpotWithSevenAndEightOutside()
        => Numbers((3, Lane.InnerOdd, ThreeAndFourAreHitOnSpot), (4, Lane.InnerEven, ThreeAndFourAreHitOnSpot),
                   (7, Lane.OuterOdd, ThreeAndFourAreHitOnSpot), (8, Lane.OuterEven, ThreeAndFourAreHitOnSpot));

    private IAiMove OneWalksOnOutsideAsSoonAsItIsCleaved()
        => Numbers((1, Lane.OuterOdd, 3));

    private IAiMove TwoGoesOutsideOnceChargedAndFiveAndSixComeInside()
        => Numbers((2, Lane.OuterEven, 3), (5, Lane.InnerOdd, 3), (6, Lane.InnerEven, 3));

    private IAiMove OneAndTwoFollowTheBlastersOutsideFiveAndSixInside(int spot)
        => Numbers((1, Lane.OuterOdd, spot), (2, Lane.OuterEven, spot), (5, Lane.InnerOdd, spot), (6, Lane.InnerEven, spot));

    private IAiMove ThreeWalksOnOutsideAsSoonAsItIsCleavedAndSevenAndEightComeInside()
        => Numbers((3, Lane.OuterOdd, 4), (7, Lane.InnerOdd, 4), (8, Lane.InnerEven, 4));

    private IAiMove FourLeavesOnTheInnerLaneOnceCharged()
        => Numbers((4, Lane.InnerEven, 4));

    private IAiMove ThreeSevenAndEightFollowTheBlastersOn()
        => Numbers((3, Lane.OuterOdd, 5), (7, Lane.InnerOdd, 5), (8, Lane.InnerEven, 5));

    private IAiMove FourGoesOutsideOnceTheNextSpotHasBlown()
        => Numbers((4, Lane.OuterEven, 5));

    private IAiMove SevenAndEightGoOnToTheirHitSpot()
        => Numbers((7, Lane.InnerOdd, SevenAndEightAreHitOnSpot), (8, Lane.InnerEven, SevenAndEightAreHitOnSpot));

    private IAiMove FiveWalksOnOutsideAsSoonAsItIsCleavedAndThreeGoesOnToItsLastSpot()
        => Numbers((5, Lane.OuterOdd, 6), (3, Lane.OuterOdd, 6));

    private IAiMove OneTwoAndSixGoOnToTheirLastSpotOnceSixIsCharged()
        => Numbers((1, Lane.OuterOdd, 6), (2, Lane.OuterEven, 6), (6, Lane.OuterEven, 6));

    private IAiMove FourGoesOnToItsLastSpot()
        => Numbers((4, Lane.OuterEven, 6));

    private IAiMove SevenWalksOutsideAsSoonAsItIsCleaved()
        => Numbers((7, Lane.OuterOdd, SevenAndEightAreHitOnSpot));

    private void TheOddFacesClockwiseAlongTheRim(int number, int spot)
    {
        if (world.Party.Get(state.Numbers[number - 1]) is not { } odd || !odd.IsAlive()) return;
        odd.TurnTo(ClockwiseAlongTheRimAt(SpotBearing(number, spot) - InnerOddDegreesBehindTheSpot));
    }

    private IAiMove Numbers(params (int Number, Lane Lane, int Spot)[] steps)
        => ByNumber(n => steps.Where(step => step.Number == n).Select(step => (Vector2?)LaneSpot(n, step.Lane, step.Spot)).FirstOrDefault());

    private IAiMove ByNumber(Func<int, Vector2?> spot)
        => AiMove.Create(Enumerable.Range(1, 8).Select(spot).ToArray()).Assignments(state.Numbers);

    private Vector2 LaneSpot(int number, Lane lane, int spot)
        => LaneSpot(SpotBearing(number, spot), lane);

    private float SpotBearing(int number, int spot) => GroupBearing(number) + DegreesBetweenSpots * spot;

    private float GroupBearing(int number) => GroupBearing(state.FirstBearing, number);

    private static float GroupBearing(float firstBearing, int number)
        => firstBearing + (number is 3 or 4 or 7 or 8 ? 180f : 0f);

    private static Vector2 LaneSpot(float spotBearing, Lane lane) => lane switch
    {
        Lane.InnerOdd => OnRing(spotBearing - InnerOddDegreesBehindTheSpot, InnerLaneRadius),
        Lane.InnerEven => OnRing(spotBearing - InnerEvenDegreesBehindTheSpot, InnerLaneRadius),
        Lane.OuterOdd => OnRing(spotBearing - OuterOddDegreesBehindTheSpot, OuterLaneRadius),
        _ => OnRing(spotBearing - OuterEvenDegreesBehindTheSpot, OuterLaneRadius),
    };

    private static Lane InnerLaneOf(int number) => number % 2 == 1 ? Lane.InnerOdd : Lane.InnerEven;

    private static Lane OuterLaneOf(int number) => number % 2 == 1 ? Lane.OuterOdd : Lane.OuterEven;

    private static float ClockwiseAlongTheRimAt(float bearingDegrees) => MathF.PI / 2f - bearingDegrees * MathF.PI / 180f;

    private static Vector2 OnRing(float bearingDegrees, float radius)
    {
        var radians = bearingDegrees * MathF.PI / 180f;
        return new Vector2(radius * MathF.Sin(radians), -radius * MathF.Cos(radians));
    }
}
