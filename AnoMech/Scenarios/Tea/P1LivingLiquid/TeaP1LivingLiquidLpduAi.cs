using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.P1LivingLiquid.TeaP1LivingLiquidPart;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Scenarios.Tea.P1LivingLiquid;

public class TeaP1LivingLiquidLpduAi : IScenarioAi<TeaP1LivingLiquidState>
{
    public string Name => "[LPDU] Light PF";
    public string? Group => "EU";

    private const float BaitJitter = 0.15f;
    private const float FirstFetch = 20.25f;
    private const float LastFetch = 24.5f;
    private const float FetchEvery = 0.25f;
    private static readonly float[] CleansesAt = [11.2f, 13.7f, 16.2f, 18.7f];
    private const float TheStacksAreTakenAt = 12.0f;
    private const float TheStacksLastFollowTheLiquidAt = 17.75f;
    private const float TheStacksLookForTheLiquidEvery = 0.25f;
    private const float TheStacksFollowTheLiquidBeyond = 0.25f;

    private const float WalkSpeed = 6.5f;
    private const float FetchShortOfEitherEnd = 1.5f;
    private const float TheLiquidStopsThisFarFromItsTank = 4.0f;
    private const float TheHandStopsThisFarFromItsTank = 2.1f;
    private const float JustInsideTheLiquidsReach = 3.9f;
    private const float TheLiquidIsOnTheMiddleWithin = 0.3f;

    private static readonly PartyRole[] Tanks = [MainTank, OffTank];
    private static readonly PartyRole[] Healers = [RegenHealer, ShieldHealer];

    private static readonly Vector2 Middle = Vector2.Zero;
    private static readonly Vector2 WestTornado = new(-P1Geometry.TornadoRingRadius, 0f);
    private static readonly Vector2 MiddleTornado = new(0f, P1Geometry.TornadoRingRadius);
    private static readonly Vector2 EastTornado = new(P1Geometry.TornadoRingRadius, 0f);

    private static readonly Vector2 TheMainTanksHold = Off(Middle, JustInsideTheLiquidsReach, 135f);
    private static readonly Vector2 TheOffTanksDrainageSpot = new(-5.4f, 5.4f);

    private readonly HashSet<PartyRole> walkingItsTetherBack = [];
    private Vector2? theStacksWereTakenRound;

    private TeaP1LivingLiquidState state = null!;
    private SimWorld world = null!;
    private TeaP1LivingLiquid fight = null!;
    private AiManager ai = null!;

    public void Run(TeaP1LivingLiquidState s, SimWorld w)
    {
        state = s;
        world = w;
        fight = s.Fight!;
        ai = new AiManager(world);
        walkingItsTetherBack.Clear();
        theStacksWereTakenRound = null;
        var parts = state.Parts;
        if (parts.First == Protean1) world.Events.Add(0f, () => PutEveryoneOn(TheTanksBesideTheBossesWithFourNorthAndTheRangedFarOut()));
        else world.Events.Add(0f, () => PutEveryoneOn(EveryoneInsideTheReachOfAnyNewPuddle()));
        if (parts.Plays(Protean1)) ProteanOneIntoDrainage(parts.Start(Protean1));
        if (parts.Plays(Cascade2)) CascadeTwoIntoTheKill(parts.Start(Cascade2), afterDrainage: parts.Plays(Protean1));
    }

    private void ProteanOneIntoDrainage(float start)
    {
        ai.Move(start + 1.5f, TheSixLineUpNorthOfTheLiquid);
        ai.Move(start + 1.5f, BothTanksStackSouthOfTheLiquidToTurnItSouth, BaitJitter);
        ai.Move(start + 5.3f, TanksAndMeleeStepOutOfTheTelegraphsOntoTheFirstSetsBaits, BaitJitter);
        ai.Move(start + 5.3f, HealersAndRangedRunOutInPairsToTakeTheSluices);
        ai.Move(start + 9.95f, HealersAndRangedComeInOntoTheSecondSetsBaits, BaitJitter);
        ai.Move(start + 9.95f, TanksAndMeleeBackOutSouth);
        ai.Move(start + 13.05f, TheMainTankStepsBackOntoItsHoldBesideTheLiquid, BaitJitter);
        ai.Move(start + 13.05f, TheSixGatherNorthForDrainage);
        for (var poll = 0; FirstFetch + poll * FetchEvery <= LastFetch + 0.001f; poll++)
            world.Events.Add(start + FirstFetch + poll * FetchEvery, EachTankFetchesItsOwnTetherAndWalksItBackToItsSpot);
    }

    private void CascadeTwoIntoTheKill(float start, bool afterDrainage)
    {
        if (afterDrainage) ai.Move(start + 0.9f, EveryoneInsideTheReachOfAnyNewPuddle);
        ai.Move(start + 6.4f, TheMainTankStepsOntoTheMiddleWhenItsFetchHasDraggedTheLiquidOffIt, BaitJitter);
        ai.Move(start + 7.2f, TheMainTankWalksOnStraightAwayFromTheLiquidWhichFollowsOntoTheMiddle, BaitJitter);
        ai.Move(start + 9.5f, TheMainTankTurnsTheLiquidNorth, BaitJitter);
        foreach (var at in CleansesAt)
            world.Events.Add(start + at, EachHealerCleansesTheNextThrottleInItsOrder);
        ai.Move(start + TheStacksAreTakenAt, TwoStackBehindTheMainTankAndFiveSouthOfTheLiquid);
        for (var look = 1; TheStacksAreTakenAt + look * TheStacksLookForTheLiquidEvery <= TheStacksLastFollowTheLiquidAt + 0.001f; look++)
            ai.Move(start + TheStacksAreTakenAt + look * TheStacksLookForTheLiquidEvery, TheStacksFollowTheLiquidWhenItsTankHasMovedItSince);
        ai.Move(start + 18.7f, TanksAndMeleeStepOutOfTheTelegraphsWithTheFirstMeleeAside, BaitJitter);
        ai.Move(start + 18.7f, HealersAndRangedGoOutToTheirTornadoes);
        ai.Move(start + 21.3f, TheFirstMeleeStepsOntoItsSouthBaitOnceTheTelegraphsHaveFired, BaitJitter);
        ai.Move(start + 23.35f, TheMainTankBacksOutNorthEastOnceItIsHit);
        ai.Move(start + 24.25f, HealersAndRangedRunInOntoTheSecondSetsBaitsOnceTheirTornadoHasAimed, BaitJitter, sprint: true);
        ai.Move(start + 24.25f, BothMeleeGoToTheFarSideOfTheMiddleAndEastTornadoes);
        ai.Move(start + 25.2f, TheOffTankFollowsToTheFarSideOfTheWestTornado);
        ai.Move(start + 29.35f, TheTanksPullTheBossesFarApartWithTheRestInTheMiddle);
        ai.Move(start + 33.5f, EachTankCrossesTheMiddleToBringItsBossOntoItOnceThePalmShows, BaitJitter);
        ai.Move(start + 33.5f, TheDamageDealersCloseInForTheKill);
    }

    private void PutEveryoneOn(IAiMove spots)
    {
        foreach (var role in PerRole.All)
        {
            if (world.Party.Get(role) is not { } member || spots[(int)role] is not { } spot) continue;
            member.StopMoving();
            member.SetPosition(new Placement(new Vector3(spot.X, 0f, spot.Y), 0f).Face(Vector3.Zero));
        }
    }

    private IAiMove TheTanksBesideTheBossesWithFourNorthAndTheRangedFarOut()
    {
        var liquid = TheLiquidIn(state.Tornadoes1);
        return Turned(state.Tornadoes1,
            (MainTank, Off(liquid, JustInsideTheLiquidsReach, 135f)), (OffTank, new(-3.0f, 3.0f)),
            (RegenHealer, new(-1.1f, -4.2f)), (ShieldHealer, new(1.1f, -5.2f)), (MeleeDpsA, new(-0.3f, -5.6f)), (MeleeDpsB, new(0.4f, -4.3f)),
            (PhysRangedDps, new(11.0f, -7.8f)), (CasterDps, new(-10.3f, -8.1f)));
    }

    private IAiMove TheSixLineUpNorthOfTheLiquid()
    {
        var liquid = TheLiquidIn(state.Tornadoes1);
        return Turned(state.Tornadoes1,
            (MeleeDpsB, liquid + new Vector2(0.3f, -4.2f)), (RegenHealer, liquid + new Vector2(-1.1f, -4.2f)),
            (MeleeDpsA, liquid + new Vector2(-0.6f, -5.1f)), (ShieldHealer, liquid + new Vector2(0.6f, -5.6f)),
            (CasterDps, liquid + new Vector2(-0.6f, -6.8f)), (PhysRangedDps, liquid + new Vector2(-0.4f, -7.8f)));
    }

    private IAiMove BothTanksStackSouthOfTheLiquidToTurnItSouth()
    {
        var liquid = TheLiquidIn(state.Tornadoes1);
        return Turned(state.Tornadoes1, (MainTank, Off(liquid, 3.3f, 180f)), (OffTank, Off(liquid, 4.6f, 180f)));
    }

    private IAiMove TanksAndMeleeStepOutOfTheTelegraphsOntoTheFirstSetsBaits()
    {
        var liquid = TheLiquidIn(state.Tornadoes1);
        return Turned(state.Tornadoes1,
            (MainTank, liquid + new Vector2(2.9f, 3.2f)), (OffTank, liquid + new Vector2(-3.1f, 3.0f)),
            (MeleeDpsA, liquid + new Vector2(-4.7f, -0.1f)), (MeleeDpsB, liquid + new Vector2(4.0f, 0.1f)));
    }

    private IAiMove HealersAndRangedRunOutInPairsToTakeTheSluices()
        => Turned(state.Tornadoes1,
            (PhysRangedDps, new(10.1f, -8.7f)), (ShieldHealer, new(11.8f, -9.4f)),
            (CasterDps, new(-11.0f, -9.5f)), (RegenHealer, new(-8.9f, -8.4f)));

    private IAiMove HealersAndRangedComeInOntoTheSecondSetsBaits()
    {
        var liquid = TheLiquidIn(state.Tornadoes1);
        return Turned(state.Tornadoes1,
            (RegenHealer, liquid + new Vector2(-3.5f, -2.2f)), (ShieldHealer, liquid + new Vector2(2.4f, -2.2f)),
            (CasterDps, liquid + new Vector2(-5.0f, 0.6f)), (PhysRangedDps, liquid + new Vector2(3.7f, 0.3f)));
    }

    private IAiMove TanksAndMeleeBackOutSouth()
        => Turned(state.Tornadoes1,
            (MainTank, new(5.0f, 5.5f)), (OffTank, TheOffTanksDrainageSpot), (MeleeDpsA, new(-6.7f, 7.1f)), (MeleeDpsB, new(6.4f, 7.4f)));

    private IAiMove TheMainTankStepsBackOntoItsHoldBesideTheLiquid()
        => Turned(state.Tornadoes1, (MainTank, Off(TheLiquidIn(state.Tornadoes1), JustInsideTheLiquidsReach, 135f)));

    private IAiMove TheSixGatherNorthForDrainage()
        => Turned(state.Tornadoes1,
            (RegenHealer, new(-1.3f, -6.9f)), (ShieldHealer, new(0f, -8.0f)), (MeleeDpsA, new(-1.5f, -7.5f)), (MeleeDpsB, new(-0.2f, -8.9f)),
            (PhysRangedDps, new(-0.6f, -9.0f)), (CasterDps, new(-1.2f, -9.4f)));

    private void EachTankFetchesItsOwnTetherAndWalksItBackToItsSpot()
    {
        var tethers = fight.DrainageTethersOut;
        var tethering = tethers.Select(tether => tether.From).ToList();
        foreach (var tank in Tanks)
        {
            if (world.Party.Get(tank) is not { } member || !member.IsAlive() || !world.Party.IsBotDriven(member)) continue;
            var held = tethers.Select(tether => tether.Tether).FirstOrDefault(tether => ReferenceEquals(fight.Holder(tether), member));
            if (held is not null)
            {
                if (walkingItsTetherBack.Add(tank)) WalkTo(member, state.Tornadoes1.ToArena(DrainageSpot(tank)));
                continue;
            }
            walkingItsTetherBack.Remove(tank);
            var itsOwn = state.Plan.TetherOf(tank, tethering);
            var toFetch = tethers.Where(tether => tether.From == itsOwn).Select(tether => tether.Tether).FirstOrDefault();
            if (toFetch is not null && !member.IsIntercepting) member.Intercept(toFetch, FetchShortOfEitherEnd);
        }
    }

    private static Vector2 DrainageSpot(PartyRole tank) => tank == MainTank ? TheMainTanksHold : TheOffTanksDrainageSpot;

    private static void WalkTo(SimCharacter member, Vector2 spot) => member.MoveTo(new Vector3(spot.X, 0f, spot.Y), WalkSpeed);

    private IAiMove EveryoneInsideTheReachOfAnyNewPuddle()
        => Turned(state.Tornadoes1,
            (RegenHealer, new(-1.1f, -4.2f)), (ShieldHealer, new(1.1f, -5.2f)), (MeleeDpsA, new(-0.3f, -5.6f)), (MeleeDpsB, new(0.4f, -4.3f)),
            (PhysRangedDps, new(1.6f, -3.4f)), (CasterDps, new(-1.6f, -3.2f)), (OffTank, new(-3.0f, 3.0f)), (MainTank, TheMainTanksHold));

    private IAiMove TheMainTankStepsOntoTheMiddleWhenItsFetchHasDraggedTheLiquidOffIt()
        => TheLiquidStandsOffTheMiddle() ? Turned(state.Tornadoes1, (MainTank, Middle)) : Nobody();

    private IAiMove TheMainTankWalksOnStraightAwayFromTheLiquidWhichFollowsOntoTheMiddle()
        => TheLiquidStandsOffTheMiddle()
            ? Turned(state.Tornadoes1, (MainTank, AcrossTheMiddleFrom(TheLiquidIn(state.Tornadoes1), TheLiquidStopsThisFarFromItsTank)))
            : Nobody();

    private bool TheLiquidStandsOffTheMiddle() => TheLiquidIn(state.Tornadoes1).Length() > TheLiquidIsOnTheMiddleWithin;

    private IAiMove TheMainTankTurnsTheLiquidNorth()
        => Turned(state.Tornadoes2, (MainTank, Off(TheLiquidIn(state.Tornadoes2), JustInsideTheLiquidsReach, 0f)));

    private void EachHealerCleansesTheNextThrottleInItsOrder()
    {
        if (state.Presses is not { } presses) return;
        var taken = new List<SimCharacter>();
        foreach (var healer in Healers)
        {
            if (world.Party.Get(healer) is not { } by || !by.IsAlive() || !TheRunCleansesFor(by)) continue;
            var next = state.Plan.CleanseOrder(healer)
                .Select(seat => world.Party.Get(seat))
                .FirstOrDefault(member => member is not null && member.IsAlive() && fight.IsThrottled(member) && !taken.Contains(member));
            if (next is null) continue;
            taken.Add(next);
            presses.Cleanse(by, next);
        }
    }

    private bool TheRunCleansesFor(SimCharacter healer)
        => !ReferenceEquals(healer, world.Party.Player)
           || !state.Plan.HumanCleanses
           || (world.Party.IsBotDriven(healer) && !state.PlayerPressesItsOwnActions);

    private IAiMove TwoStackBehindTheMainTankAndFiveSouthOfTheLiquid()
    {
        var liquid = TheLiquidIn(state.Tornadoes2);
        theStacksWereTakenRound = liquid;
        return Turned(state.Tornadoes2,
            (RegenHealer, Off(liquid, 5.7f, 0f)), (CasterDps, Off(liquid, 7.5f, 0f)),
            (ShieldHealer, Off(liquid, 3.2f, 180f)), (MeleeDpsA, Off(liquid, 3.8f, 180f)), (PhysRangedDps, Off(liquid, 4.4f, 180f)),
            (MeleeDpsB, Off(liquid, 5.0f, 180f)), (OffTank, Off(liquid, 5.6f, 180f)));
    }

    private IAiMove TheStacksFollowTheLiquidWhenItsTankHasMovedItSince()
        => theStacksWereTakenRound is { } taken && Vector2.Distance(TheLiquidIn(state.Tornadoes2), taken) > TheStacksFollowTheLiquidBeyond
            ? TwoStackBehindTheMainTankAndFiveSouthOfTheLiquid()
            : Nobody();

    private IAiMove TanksAndMeleeStepOutOfTheTelegraphsWithTheFirstMeleeAside()
    {
        var liquid = TheLiquidIn(state.Tornadoes2);
        return Turned(state.Tornadoes2,
            (MainTank, Off(liquid, 4.3f, 40f)), (MeleeDpsB, Off(liquid, 3.4f, 85f)), (OffTank, Off(liquid, 4.0f, 270f)),
            (MeleeDpsA, Off(liquid, 4.0f, 215f)));
    }

    private IAiMove HealersAndRangedGoOutToTheirTornadoes()
        => Turned(state.Tornadoes2,
            (RegenHealer, Off(WestTornado, 8.4f, 14f)), (CasterDps, Off(WestTornado, 9.9f, 15f)),
            (ShieldHealer, Off(MiddleTornado, 8.5f, 270f)), (PhysRangedDps, Off(EastTornado, 8.4f, 172f)));

    private IAiMove TheFirstMeleeStepsOntoItsSouthBaitOnceTheTelegraphsHaveFired()
        => Turned(state.Tornadoes2, (MeleeDpsA, Off(TheLiquidIn(state.Tornadoes2), 4.0f, 180f)));

    private IAiMove TheMainTankBacksOutNorthEastOnceItIsHit()
        => Turned(state.Tornadoes2, (MainTank, Off(TheLiquidIn(state.Tornadoes2), 8.5f, 43f)));

    private IAiMove HealersAndRangedRunInOntoTheSecondSetsBaitsOnceTheirTornadoHasAimed()
    {
        var liquid = TheLiquidIn(state.Tornadoes2);
        return Turned(state.Tornadoes2,
            (RegenHealer, Off(liquid, 4.95f, 317f)), (CasterDps, Off(liquid, 4.0f, 275f)),
            (PhysRangedDps, Off(liquid, 3.2f, 95f)), (ShieldHealer, Off(liquid, 3.8f, 180f)));
    }

    private IAiMove BothMeleeGoToTheFarSideOfTheMiddleAndEastTornadoes()
        => Turned(state.Tornadoes2, (MeleeDpsA, Off(MiddleTornado, 8.4f, 90f)), (MeleeDpsB, Off(EastTornado, 8.5f, 12f)));

    private IAiMove TheOffTankFollowsToTheFarSideOfTheWestTornado()
        => Turned(state.Tornadoes2, (OffTank, Off(WestTornado, 8.5f, 191f)));

    private IAiMove TheTanksPullTheBossesFarApartWithTheRestInTheMiddle()
        => Turned(state.Tornadoes2,
            (MainTank, new(8.1f, -9.4f)), (OffTank, new(-13.0f, 10.8f)),
            (MeleeDpsA, new(0.8f, -5.7f)), (MeleeDpsB, new(4.5f, -3.7f)), (RegenHealer, new(-0.4f, -2.3f)), (ShieldHealer, new(-0.2f, 0.4f)),
            (PhysRangedDps, new(1.8f, -1.4f)), (CasterDps, new(-2.2f, -0.8f)));

    private IAiMove EachTankCrossesTheMiddleToBringItsBossOntoItOnceThePalmShows()
        => fight.HandShowsAPalm == true
            ? Turned(state.Tornadoes2,
                (MainTank, AcrossTheMiddleFrom(TheLiquidIn(state.Tornadoes2), TheLiquidStopsThisFarFromItsTank)),
                (OffTank, AcrossTheMiddleFrom(TheHandIn(state.Tornadoes2), TheHandStopsThisFarFromItsTank)))
            : Nobody();

    private IAiMove TheDamageDealersCloseInForTheKill()
        => Turned(state.Tornadoes2,
            (MeleeDpsA, new(-0.3f, -3.8f)), (MeleeDpsB, new(2.3f, -2.3f)), (PhysRangedDps, new(0.9f, -1.4f)), (CasterDps, new(-0.7f, -1.0f)));

    private Vector2 TheLiquidIn(TeaP1TornadoLayout frame) => frame.ToRelative(fight.Position(TeaP1Boss.LivingLiquid));

    private Vector2 TheHandIn(TeaP1TornadoLayout frame) => frame.ToRelative(fight.Position(TeaP1Boss.LiquidHand));

    private static Vector2 AcrossTheMiddleFrom(Vector2 boss, float distance)
        => boss.LengthSquared() < 1e-4f ? new Vector2(0f, distance) : -Vector2.Normalize(boss) * distance;

    private static Vector2 Off(Vector2 from, float distance, float bearingDegrees)
    {
        var radians = bearingDegrees * MathF.PI / 180f;
        return from + distance * new Vector2(MathF.Sin(radians), -MathF.Cos(radians));
    }

    private static IAiMove Nobody() => AiMove.Create().NaturalOrder();

    private static IAiMove Turned(TeaP1TornadoLayout frame, params (PartyRole Seat, Vector2 Spot)[] spots)
        => AiMove.Create(PerRole.All.Select(role => spots.Where(spot => spot.Seat == role).Select(spot => (Vector2?)spot.Spot).FirstOrDefault()).ToArray())
                 .Assignments(PerRole.All)
                 .ApplyPositions(positions => positions.Rotate(frame.Radians));
}
