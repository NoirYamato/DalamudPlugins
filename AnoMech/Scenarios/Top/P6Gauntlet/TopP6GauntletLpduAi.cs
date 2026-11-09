using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.P6Gauntlet.TopP6GauntletPart;
using static AnoMech.Scenarios.Top.TopP6Constants;

namespace AnoMech.Scenarios.Top.P6Gauntlet;

public sealed class TopP6GauntletLpduAi : IScenarioAi<TopP6GauntletState>
{
    public string Name => "[LPDU] Light PF";
    public string? Group => "EU";

    internal const float HalfwayThroughCosmoMemory = 4.9f;
    internal const float CosmoMemoryDecides = 7.6f;

    internal static readonly Vector2 InFrontOfWaymarkTwo = new(7.5f, 7.5f);
    internal static readonly Vector2 InTheCentreSquare = new(3f, 3f);
    internal static readonly Vector2 OutOnTheCorner = new(12f, 12f);
    internal static readonly Vector2 SideStepSouth = new(3f, 12f);
    internal static readonly Vector2 InFromTheSideStep = new(3f, 8f);
    internal static readonly Vector2 MainTankDiveSpotTowardWaymarkFour = new(-4.6f, -4.6f);
    internal static readonly Vector2 OffTankDiveSpotTowardWaymarkOne = new(4.6f, -4.6f);

    internal const float InsideTheLastPuddlesRadius = 4f;
    internal const float SpreadRadius = 10.5f;

    internal static readonly IReadOnlyDictionary<PartyRole, float> WaymarkBearing = new Dictionary<PartyRole, float>
    {
        [MainTank] = 0f, [CasterDps] = 45f, [OffTank] = 90f, [MeleeDpsB] = 135f,
        [ShieldHealer] = 180f, [MeleeDpsA] = 225f, [RegenHealer] = 270f, [PhysRangedDps] = 315f,
    };

    internal static readonly Vector2 SupportHomeBesideItsIntercardinal = new(6.4f, 8.4f);
    internal static readonly Vector2 PairStackedInTheCentreSquare = new(3.4f, 3.4f);
    internal static readonly Vector2 SupportBackOutPastHome = new(6.4f, 11.4f);
    internal static readonly Vector2 PairStackedOutInTheCorner = new(11.3f, 11.3f);
    internal static readonly Vector2 PairStackedBackHome = new(7.3f, 7.3f);
    internal static readonly Vector2 SupportSidestepTowardTheNorthSouthAxis = new(3.5f, 12f);
    internal static readonly Vector2 SupportLaserSpotBesideItsCardinal = new(3.55f, 8.55f);
    internal const float BackForTheAutosOnceWildChargeHasHit = 28.1f;

    internal const int TheLastExaflare = 3;
    internal const float DiveTankRadius = 6.5f;
    internal const float MainTankDegreesPastTheParty = 135f;
    internal const float OffTankDegreesPastTheParty = 225f;
    internal static readonly Vector2 MainTankMakingRoomForTheSecondMelee = new(0f, -7f);
    internal static readonly Vector2 Middle = Vector2.Zero;
    internal static readonly Vector2 OffTankOutsideEastFarthestBeyondTheMeleeCastingOnTheDiveHold = new(16f, 0f);
    internal const float FlashGaleReachAndAStepFromTheMeleeCastingAtTheDiveHold = 5.5f;
    internal const float TheCosmoDiveBarEnds = 24.9f;
    internal const float RightAfterTheCosmoDiveBar = 25.3f;
    internal const float TheFirstMeleeWindowCloses = TheCosmoDiveBarEnds + P6Duration.FirstDiveMeleeLatestPress;
    internal const float FromTheMiddleOnceTheFirstMeleeHasRefilledTheGauge = 32f;
    internal const float TheSecondMeleeWindowCloses = TheCosmoDiveBarEnds + P6Duration.SecondDiveMeleeLatestPress;

    internal const float ClockRadius = 13f;
    internal const float FlareRadius = 18.5f;
    internal const float NorthCardinal = 0f;
    internal const float SouthCardinal = 180f;
    internal const float FacingSouth = 0f;
    internal static readonly Vector2 UnderAlphaOmega = Vector2.Zero;

    internal static readonly IReadOnlyDictionary<PartyRole, float> ClockBearings = new Dictionary<PartyRole, float>
    {
        [PhysRangedDps] = 0f, [CasterDps] = 45f, [ShieldHealer] = 90f, [MeleeDpsB] = 135f,
        [OffTank] = 180f, [MeleeDpsA] = 225f, [RegenHealer] = 270f, [MainTank] = 315f,
    };

    internal static readonly float[] CardinalsClockwiseFromNorth = [0f, 90f, 180f, 270f];
    internal const float CosmoMeteorCast = 1.9f;
    internal const float OnArrivingAtTheMeteorClock = 13.5f;
    internal const float TheCometsEnrage = CosmoMeteorCast + P6Duration.CometsEnrage;
    internal const float OnceTheCasterHasRefilledTheGauge = 24f;
    internal const float TheMeteorsEnrage = CosmoMeteorCast + P6Duration.MeteorsEnrage;
    internal const float BackForTheMagicNumbersOnceTheFlaresHaveLanded = 31f;

    internal const float MagicNumber1Cast = 1.9f;
    internal const float MagicNumber2Cast = 18f;
    internal const float MagicNumberBar = 4.7f;
    internal const float MagicNumberHits = 5f;
    internal const float EarlyInTheCast = 0.7f;
    internal const float ASecondAfterTheMark = 0.7f;
    internal const float EarlyInMagicNumber1 = MagicNumber1Cast + EarlyInTheCast;
    internal const float MagicNumber1Decides = MagicNumber1Cast + MagicNumberBar;
    internal const float ASecondAfterMagicNumber1sMark = MagicNumber1Cast + MagicNumberHits + P6Duration.MagicNumberMarkAfterTheHit + ASecondAfterTheMark;
    internal const float MagicNumber1sMarkRunsOut = MagicNumber1Cast + MagicNumberHits + P6Duration.MagicNumberMarkAfterTheHit + P6Duration.MagicNumberMark;
    internal const float EarlyInMagicNumber2 = MagicNumber2Cast + EarlyInTheCast;
    internal const float MagicNumber2Decides = MagicNumber2Cast + MagicNumberBar;
    internal const float ASecondAfterMagicNumber2sMark = MagicNumber2Cast + MagicNumberHits + P6Duration.MagicNumberMarkAfterTheHit + ASecondAfterTheMark;
    internal const float MagicNumber2sMarkRunsOut = MagicNumber2Cast + MagicNumberHits + P6Duration.MagicNumberMarkAfterTheHit + P6Duration.MagicNumberMark;
    internal const float RunMiCast = 32.1f;
    internal const float EarlyInRunMi = RunMiCast + 1f;

    private TopP6GauntletState state = null!;
    private SimWorld world = null!;
    private AiManager ai = null!;

    public void Run(TopP6GauntletState s, SimWorld w)
    {
        state = s;
        world = w;
        ai = new AiManager(world);
        var parts = state.Parts;
        if (parts.First == CosmoMemory) ai.Move(0f, TopP6LpduAi.EveryoneOnTheirAutoSpot);
        else world.Events.Add(0f, PutEveryoneOnTheirAutoSpot);
        if (parts.Plays(CosmoMemory)) CosmoMemoryIntoCosmoDive(parts.Start(CosmoMemory));
        if (parts.Plays(Exaflares1)) ExaflaresIntoWaveCannon(parts.Start(Exaflares1));
        if (parts.Plays(CosmoArrow2)) CosmoArrowIntoWaveCannon(parts.Start(CosmoArrow2));
        if (parts.Plays(Exaflares2)) ExaflaresIntoCosmoDive(parts.Start(Exaflares2));
        if (parts.Plays(CosmoMeteor)) TheCosmoMeteor(parts.Start(CosmoMeteor));
        if (parts.Plays(MagicNumber)) MagicNumbersIntoTheEnrage(parts.Start(MagicNumber));
    }

    private void CosmoMemoryIntoCosmoDive(float start)
    {
        PressOnceTheGaugeAllows(TopP6LimitBreakDuty.CosmoMemory, start + HalfwayThroughCosmoMemory, start + CosmoMemoryDecides);
        ai.Move(start + 16.3f, EveryoneInFrontOfWaymarkTwo);
        if (state.Arrow1InFirst)
        {
            ai.Move(start + 24.1f, EveryoneIntoTheCentreSquare);
            ai.Move(start + 28.4f, EveryoneInFrontOfWaymarkTwo);
            ai.Move(start + 32.4f, EveryoneSideStepsSouth);
            ai.Move(start + 34.4f, TheSixInFromTheSideStepAsTheTanksLeaveForTheirDiveSpots);
            ai.Move(start + 36.4f, TheSixBackInFrontOfWaymarkTwo);
        }
        else
        {
            ai.Move(start + 24.1f, EveryoneOutToTheCorner);
            ai.Move(start + 28.4f, EveryoneInFrontOfWaymarkTwo);
            ai.Move(start + 30.4f, EveryoneSideStepsSouth);
            ai.Move(start + 32.4f, TheSixInFromTheSideStepAsTheTanksLeaveForTheirDiveSpots);
            ai.Move(start + 34.4f, TheSixBackInFrontOfWaymarkTwo);
        }
        ai.Move(start + 40f, TopP6LpduAi.EveryoneOnTheirAutoSpot);
    }

    private void ExaflaresIntoWaveCannon(float start)
    {
        var unlimitedWaveCannon = TopP6GauntletScenario.UnlimitedWaveCannon1(state);
        TopP6LpduAi.StepOutOfTheHitboxToReadTheArrowsThenStackInTheMiddle(ai, unlimitedWaveCannon);
        TopP6LpduAi.FollowTheExaflaresRoundFromBesideTheFirst(ai, unlimitedWaveCannon);
        ai.Move(start + 21.6f, InTowardTheirWaymarkClearOfTheLastPuddles);
        ai.Move(start + 24.7f, OutToTheirWaveCannonSpreadOnceTheLastPuddleIsGone);
        ai.Move(start + 30f, TopP6LpduAi.LineUpInFrontOfCWithTheTanksFirst);
        ai.Move(start + 36.5f, TopP6LpduAi.EveryoneOnTheirAutoSpot);
    }

    private void CosmoArrowIntoWaveCannon(float start)
    {
        ai.Move(start + 2f, () => EachPairInItsQuadrant(SupportHomeBesideItsIntercardinal));
        if (state.Arrow2InFirst)
        {
            ai.Move(start + 9.7f, () => EachPairInItsQuadrant(PairStackedInTheCentreSquare));
            ai.Move(start + 14f, () => EachPairInItsQuadrant(SupportBackOutPastHome));
            ai.Move(start + 18f, () => EachPairInItsQuadrant(SupportSidestepTowardTheNorthSouthAxis));
            ai.Move(start + 20f, () => EachPairInItsQuadrant(SupportLaserSpotBesideItsCardinal));
            ai.Move(start + 22f, TopP6LpduAi.LineUpInFrontOfCWithTheTanksFirst);
        }
        else
        {
            ai.Move(start + 9.7f, () => EachPairInItsQuadrant(PairStackedOutInTheCorner));
            ai.Move(start + 14f, () => EachPairInItsQuadrant(PairStackedBackHome));
            ai.Move(start + 16f, () => EachPairInItsQuadrant(SupportSidestepTowardTheNorthSouthAxis));
            ai.Move(start + 18f, () => EachPairInItsQuadrant(SupportLaserSpotBesideItsCardinal));
            ai.Move(start + 21.2f, TopP6LpduAi.LineUpInFrontOfCWithTheTanksFirst);
        }
        ai.Move(start + BackForTheAutosOnceWildChargeHasHit, TopP6LpduAi.EveryoneOnTheirAutoSpot);
    }

    private void ExaflaresIntoCosmoDive(float start)
    {
        var unlimitedWaveCannon = TopP6GauntletScenario.UnlimitedWaveCannon2(state);
        TopP6LpduAi.StepOutOfTheHitboxToReadTheArrowsThenStackInTheMiddle(ai, unlimitedWaveCannon);
        TopP6LpduAi.FollowTheExaflaresRoundFromBesideTheFirst(ai, unlimitedWaveCannon);
        ai.Move(start + 21.5f, PartyHoldsOnTheLastExaflaresPointTanksCrossTheBossIntoAY);
        PressOnceTheGaugeAllows(TopP6LimitBreakDuty.FirstDiveMelee, start + RightAfterTheCosmoDiveBar, start + TheFirstMeleeWindowCloses);
        ai.Move(start + 28f, BackForTheAutosTheSecondMeleeInTheMiddleTheMainTankMakingRoom);
        TheSecondMeleePressesFromTheMiddleOnceTheFirstHasLandedOrItsWindowHasClosed(start);
    }

    private void TheSecondMeleePressesFromTheMiddleOnceTheFirstHasLandedOrItsWindowHasClosed(float start)
    {
        var pressed = false;

        void PressTheSecondMelee()
        {
            if (pressed) return;
            pressed = true;
            if (state.LimitBreaks is { } limitBreaks) limitBreaks.Resolved -= PressOnceTheFirstMeleeLands;
            Press(TopP6LimitBreakDuty.SecondDiveMelee, start + TheSecondMeleeWindowCloses);
        }

        void PressOnceTheFirstMeleeLands(TopP6LimitBreakPress press)
        {
            if (IsTheFirstMeleesLimitBreak3(press, start)) PressTheSecondMelee();
        }

        world.Events.Add(start + FromTheMiddleOnceTheFirstMeleeHasRefilledTheGauge, () =>
        {
            if (state.LimitBreaks is not { } limitBreaks) return;
            if (limitBreaks.Presses.Any(press => IsTheFirstMeleesLimitBreak3(press, start))) PressTheSecondMelee();
            else limitBreaks.Resolved += PressOnceTheFirstMeleeLands;
        });
        world.Events.Add(start + TheFirstMeleeWindowCloses, PressTheSecondMelee);
    }

    private bool IsTheFirstMeleesLimitBreak3(TopP6LimitBreakPress press, float start)
        => press.Seat == state.LimitBreakPlan.SeatOf(TopP6LimitBreakDuty.FirstDiveMelee) && press.Is(TopP6LimitBreakKind.Melee)
           && press.ResolvedAt >= start + TheCosmoDiveBarEnds;

    private void TheCosmoMeteor(float start)
    {
        ai.Move(start + 2.6f, EveryoneUnderAlphaOmegaToBaitTheMeteorsInTheMiddle);
        ai.Move(start + 7f, EveryoneOutToTheirMeteorClockSpotOnceTheBaitsDrop);
        ai.Move(start + 13.3f, ShieldHealerIntoTheMiddleAfterTheFirstEightSpreads);
        PressOnceTheGaugeAllows(TopP6LimitBreakDuty.Comets, start + OnArrivingAtTheMeteorClock, start + TheCometsEnrage);
        ai.Move(start + 22.4f, FlaresToTheFreeCardinalsTheRestStackWhileThePhysRangedHoldsNorth);
        world.Events.Add(start + 23.5f, PhysRangedTurnsSouthToLineUpBothMeteorsThroughAlphaOmega);
        PressOnceTheGaugeAllows(TopP6LimitBreakDuty.Meteors, start + OnceTheCasterHasRefilledTheGauge, start + TheMeteorsEnrage);
        ai.Move(start + BackForTheMagicNumbersOnceTheFlaresHaveLanded, TopP6LpduAi.EveryoneOnTheirAutoSpot);
    }

    private void MagicNumbersIntoTheEnrage(float start)
    {
        PressOnceTheGaugeAllows(TopP6LimitBreakDuty.MagicNumber1Tank, start + EarlyInMagicNumber1, start + MagicNumber1Decides);
        PressOnceTheGaugeAllows(TopP6LimitBreakDuty.MagicNumber1Healer, start + ASecondAfterMagicNumber1sMark, start + MagicNumber1sMarkRunsOut);
        PressOnceTheGaugeAllows(TopP6LimitBreakDuty.MagicNumber2Tank, start + EarlyInMagicNumber2, start + MagicNumber2Decides);
        PressOnceTheGaugeAllows(TopP6LimitBreakDuty.MagicNumber2Healer, start + ASecondAfterMagicNumber2sMark, start + MagicNumber2sMarkRunsOut);
        PressOnceTheGaugeAllows(TopP6LimitBreakDuty.Enrage, start + EarlyInRunMi, TopP6AlphaOmega.RunMiFinishedBy(start + RunMiCast));
    }

    private void PressOnceTheGaugeAllows(TopP6LimitBreakDuty duty, float at, float givingUpAfter)
        => world.Events.Add(at, () => Press(duty, givingUpAfter));

    private void Press(TopP6LimitBreakDuty duty, float givingUpAfter)
    {
        var plan = state.LimitBreakPlan;
        var seat = plan.SeatOf(duty);
        if (world.Party.Get(seat) is not { } member || TheHumanPressesItThemselves(plan, duty, member)) return;
        state.LimitBreaks?.Press(seat, plan.JobOf(seat), AimedAt(duty), givingUpAfter);
    }

    private bool TheHumanPressesItThemselves(TopP6GauntletLimitBreakPlan plan, TopP6LimitBreakDuty duty, SimCharacter member)
        => plan.HumanPresses(duty) && (state.PlayerPressesItsOwnLimitBreaks || !world.Party.IsBotDriven(member));

    private SimCharacter? AimedAt(TopP6LimitBreakDuty duty) => duty switch
    {
        TopP6LimitBreakDuty.CosmoMemory or TopP6LimitBreakDuty.MagicNumber1Tank or TopP6LimitBreakDuty.MagicNumber2Tank
            or TopP6LimitBreakDuty.MagicNumber1Healer or TopP6LimitBreakDuty.MagicNumber2Healer => null,
        _ => state.AlphaOmega?.Enemy,
    };

    private void PutEveryoneOnTheirAutoSpot()
    {
        foreach (var role in PerRole.All)
        {
            if (world.Party.Get(role) is not { } member) continue;
            var spot = TopP6LpduAi.AutoSpot(role);
            member.StopMoving();
            member.SetPosition(new Placement(new Vector3(spot.X, 0f, spot.Y), 0f).Face(Vector3.Zero));
        }
    }

    private static IAiMove EveryoneInFrontOfWaymarkTwo() => AiMove.All(InFrontOfWaymarkTwo);

    private static IAiMove EveryoneIntoTheCentreSquare() => AiMove.All(InTheCentreSquare);

    private static IAiMove EveryoneOutToTheCorner() => AiMove.All(OutOnTheCorner);

    private static IAiMove EveryoneSideStepsSouth() => AiMove.All(SideStepSouth);

    private static IAiMove TheSixInFromTheSideStepAsTheTanksLeaveForTheirDiveSpots()
        => TopP6LpduAi.ByRole(role => role switch
        {
            MainTank => MainTankDiveSpotTowardWaymarkFour,
            OffTank => OffTankDiveSpotTowardWaymarkOne,
            _ => InFromTheSideStep,
        });

    private static IAiMove TheSixBackInFrontOfWaymarkTwo()
        => TopP6LpduAi.ByRole(role => role.IsTank() ? null : InFrontOfWaymarkTwo);

    internal static Vector2 InsideTheLastPuddles(PartyRole role) => TopP6LpduAi.OnRing(WaymarkBearing[role], InsideTheLastPuddlesRadius);

    internal static Vector2 SpreadSpot(PartyRole role) => TopP6LpduAi.OnRing(WaymarkBearing[role], SpreadRadius);

    private static IAiMove InTowardTheirWaymarkClearOfTheLastPuddles() => TopP6LpduAi.ByRole(role => InsideTheLastPuddles(role));

    private static IAiMove OutToTheirWaveCannonSpreadOnceTheLastPuddleIsGone() => TopP6LpduAi.ByRole(role => SpreadSpot(role));

    internal static Vector2 EachPairsSpot(PartyRole role, Vector2 supportSpot)
        => QuadrantSigns(role) * (role.IsDps() ? MirroredAcrossTheDiagonal(supportSpot) : supportSpot);

    private static IAiMove EachPairInItsQuadrant(Vector2 supportSpot) => TopP6LpduAi.ByRole(role => EachPairsSpot(role, supportSpot));

    private static Vector2 MirroredAcrossTheDiagonal(Vector2 spot) => new(spot.Y, spot.X);

    private static Vector2 QuadrantSigns(PartyRole role) => role switch
    {
        MainTank or PhysRangedDps => new(-1f, -1f),
        OffTank or CasterDps => new(1f, -1f),
        RegenHealer or MeleeDpsA => new(-1f, 1f),
        _ => new(1f, 1f),
    };

    internal static Vector2 PartyDiveHold(TopP6GauntletState state)
        => TopP6LpduAi.OnTheExaflarePath(state.Exaflares2First, state.Exaflares2Clockwise, TheLastExaflare);

    internal static Vector2 MainTankDiveSpot(TopP6GauntletState state)
        => TopP6LpduAi.OnRing(BearingOfTheLastExaflare(state) + MainTankDegreesPastTheParty, DiveTankRadius);

    internal static Vector2 OffTankDiveSpot(TopP6GauntletState state)
        => TopP6LpduAi.OnRing(BearingOfTheLastExaflare(state) + OffTankDegreesPastTheParty, DiveTankRadius);

    internal static Vector2 OffTankAutoSpotClearOfThePartysDiveHold(Vector2 partyDiveHold)
        => Vector2.Distance(partyDiveHold, OffTankOutsideEastFarthestBeyondTheMeleeCastingOnTheDiveHold) < FlashGaleReachAndAStepFromTheMeleeCastingAtTheDiveHold
            ? -OffTankOutsideEastFarthestBeyondTheMeleeCastingOnTheDiveHold
            : OffTankOutsideEastFarthestBeyondTheMeleeCastingOnTheDiveHold;

    internal static Vector2 AutoSpotAfterTheDive(PartyRole role, TopP6GauntletState state) => role switch
    {
        MainTank => MainTankMakingRoomForTheSecondMelee,
        OffTank => OffTankAutoSpotClearOfThePartysDiveHold(PartyDiveHold(state)),
        _ when role == state.LimitBreakPlan.SeatOf(TopP6LimitBreakDuty.SecondDiveMelee) => Middle,
        _ => TopP6LpduAi.AutoSpot(role),
    };

    private static float BearingOfTheLastExaflare(TopP6GauntletState state)
        => TopP6LpduAi.BearingOfExaflare(state.Exaflares2First, state.Exaflares2Clockwise, TheLastExaflare);

    private IAiMove PartyHoldsOnTheLastExaflaresPointTanksCrossTheBossIntoAY()
        => TopP6LpduAi.ByRole(role => role switch
        {
            MainTank => MainTankDiveSpot(state),
            OffTank => OffTankDiveSpot(state),
            _ => PartyDiveHold(state),
        });

    private IAiMove BackForTheAutosTheSecondMeleeInTheMiddleTheMainTankMakingRoom()
        => TopP6LpduAi.ByRole(role => AutoSpotAfterTheDive(role, state));

    internal static Vector2 ClockSpot(PartyRole role) => TopP6LpduAi.OnRing(ClockBearings[role], ClockRadius);

    internal static float StackCardinal(IReadOnlyCollection<PartyRole> flares)
        => flares.Contains(PhysRangedDps) ? SouthCardinal : NorthCardinal;

    internal static IReadOnlyDictionary<PartyRole, float> FlareCardinalsKeepingTheirClockOrderFromNorth(IReadOnlyCollection<PartyRole> flares)
    {
        var freeCardinals = CardinalsClockwiseFromNorth.Where(c => c != StackCardinal(flares));
        return flares.OrderBy(role => ClockBearings[role]).Zip(freeCardinals).ToDictionary(t => t.First, t => t.Second);
    }

    internal static Vector2 FinalSpot(PartyRole role, IReadOnlyCollection<PartyRole> flares)
    {
        if (role == PhysRangedDps) return ClockSpot(PhysRangedDps);
        return flares.Contains(role)
            ? TopP6LpduAi.OnRing(FlareCardinalsKeepingTheirClockOrderFromNorth(flares)[role], FlareRadius)
            : TopP6LpduAi.OnRing(StackCardinal(flares), ClockRadius);
    }

    private static IAiMove EveryoneUnderAlphaOmegaToBaitTheMeteorsInTheMiddle() => TopP6LpduAi.Everyone(UnderAlphaOmega);

    private static IAiMove EveryoneOutToTheirMeteorClockSpotOnceTheBaitsDrop() => TopP6LpduAi.ByRole(role => ClockSpot(role));

    private static IAiMove ShieldHealerIntoTheMiddleAfterTheFirstEightSpreads() => AiMove.Single(ShieldHealer, UnderAlphaOmega);

    private IAiMove FlaresToTheFreeCardinalsTheRestStackWhileThePhysRangedHoldsNorth()
    {
        var flares = state.Flares.List;
        return TopP6LpduAi.ByRole(role => role == PhysRangedDps ? null : FinalSpot(role, flares));
    }

    private void PhysRangedTurnsSouthToLineUpBothMeteorsThroughAlphaOmega()
    {
        if (world.Party.Get(PhysRangedDps) is { } physRanged && physRanged.IsAlive())
            physRanged.TurnTo(FacingSouth);
    }
}
