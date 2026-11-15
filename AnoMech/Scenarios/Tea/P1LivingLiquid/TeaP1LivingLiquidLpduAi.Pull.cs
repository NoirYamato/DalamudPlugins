using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Scenarios.Tea.P1LivingLiquid;

public partial class TeaP1LivingLiquidLpduAi
{
    private const float ThePullLooksEvery = 0.25f;
    private const float ThePullLastLooksAt = 60.5f;
    private const float TheOffTankLastLooksForTheHandToLeaveAt = 42.25f;
    private const float TheMainTankLastLooksToProvokeAt = 34.25f;
    private const float TheOffTankLastLooksToProvokeBackAt = 55.0f;
    private const float AMeleeLeavesADollOnTheBossesWayInAloneFrom = 38.5f;
    private const float TheTanksHaveLedTheBossesInBy = 45.0f;
    private const float ABotTakesAnothersDollFrom = 36.0f;
    private const float AMeleeTakesTheDollOfOneWhoDrewItsOwnAwayFrom = 38.75f;
    private const float AMeleeTakesNoDollFrom = 47.0f;
    private const float EachTankTakesItsHoldBy = 49.0f;

    private const float AMeleeReaches = 3.5f;
    private const float ARangedReaches = 25f;
    private const float NoBossThisCloseToADollBeingHit = 5.2f;
    private const float BesideItsDoll = 0.5f;
    private const float PastTheBossADollIsDrawnThrough = 2.5f;
    private const float AFeederKeepsFromEveryTornado = 8.3f;
    private const float AFeederStaysWithinOfTheMiddle = 19.3f;
    private const float ABossHasMovedBeyond = 0.25f;
    private static readonly float[] TurnedRoundTheBossBy = [0f, 20f, -20f, 40f, -40f, 60f, -60f, 80f, -80f, 100f, -100f];

    private static readonly TeaP1TornadoLayout TrueNorth = new(TeaP1OpenQuarter.North);
    private static readonly PartyRole[] Melee = [MeleeDpsA, MeleeDpsB];
    private static readonly PartyRole[] Ranged = [PhysRangedDps, CasterDps];
    private static readonly PartyRole[] DamageDealers = [MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps];
    private static readonly Vector2[] Tornadoes = [WestTornado, MiddleTornado, EastTornado];

    private static readonly Vector2 TheMainTanksBaitBesideTheMiddleTornado = Off(MiddleTornado, 8.4f, 258f);
    private static readonly Vector2 TheOffTanksSplit = Off(Middle, 11.5f, 39f);
    private static readonly Vector2 TheLiquidsFlank = new(-9.5f, 9.0f);
    private static readonly Vector2 TheFirstRangedsBait = Off(EastTornado, 8.4f, 10f);
    private static readonly Vector2 TheSecondRangedsBait = Off(WestTornado, 8.4f, 350f);

    private readonly HashSet<PartyRole> wentToAnothersDoll = [];
    private readonly Dictionary<PartyRole, Vector2> drawsItsDollThroughABossAt = [];
    private readonly HashSet<PartyRole> doneWithItsDoll = [];
    private readonly HashSet<PartyRole> holdsItsBoss = [];
    private bool theOffTankHasLeftForTheFlank;
    private bool theMainTankHasSeenToTheHand;
    private bool theOffTankHasSeenToTheHand;

    private void ForgetThePull()
    {
        wentToAnothersDoll.Clear();
        drawsItsDollThroughABossAt.Clear();
        doneWithItsDoll.Clear();
        holdsItsBoss.Clear();
        theOffTankHasLeftForTheFlank = false;
        theMainTankHasSeenToTheHand = false;
        theOffTankHasSeenToTheHand = false;
    }

    private void ThePullIntoTheCentre()
    {
        ai.Move(2.0f, TheMainTankWalksThroughTheLiquidToTurnItNorth, BaitJitter);
        ai.Move(14.3f, TheMainTankStepsInsideTheReachOfAnyNewPuddleAsCascadeShows, BaitJitter);
        ai.Move(24.0f, TheMainTankSprintsToTheRimBesideTheMiddleTornado, BaitJitter, sprint: true);
        ai.Move(24.0f, TheOffTankTakesTheHandFarNorthEastAndTheRangedBaitTheSideTornadoes, BaitJitter);
        ai.Move(24.0f, OneHealerFarNorthTheOtherFarSouthEastAndTheMeleeSouthWestOfTheMiddle);
        EveryLook(30.0f, TheMainTankLastLooksToProvokeAt, at => world.Events.Add(at, TheMainTankProvokesTheHandOnceItShowsAPalmWithinItsReach));
        EveryLook(30.0f, TheOffTankLastLooksForTheHandToLeaveAt, at => ai.Move(at, TheOffTankGoesToTheLiquidsFlankOnceTheHandHasLeftIt));
        ai.Move(32.5f, TheRangedStepOutOfTheirTornadoesTelegraphs, BaitJitter);
        ai.Move(33.5f, EachMeleeStandsHalfAYalmOffItsDollAwayFromTheTornadoBesideIt, BaitJitter);
        world.Events.Add(34.0f, TheMainTankTurnsInvulnerableForTheWaveThatFollowsTheBusters);
        EveryLook(34.25f, ThePullLastLooksAt, at => world.Events.Add(at, EachDamageDealerHitsItsDollOnceNoBossStandsByIt));
        ai.Move(35.15f, TheRangedStepBackOntoTheirBaitsOnceTheTelegraphsHaveFired, BaitJitter);
        EveryLook(ABotTakesAnothersDollFrom, ThePullLastLooksAt, at => ai.Move(at, AMeleeWhoseDollSomeoneElseTookGoesToThatOnesDoll, BaitJitter));
        EveryLook(38.0f, TheOffTankLastLooksToProvokeBackAt, at => world.Events.Add(at, TheOffTankProvokesTheHandBackOnceItIsWithinItsReach));
        EveryLook(38.75f, ThePullLastLooksAt, at => ai.Move(at, EachMeleeDrawsItsDollThroughTheNearerBossOnceItIsUnderAQuarterThenGoesNorth, BaitJitter));
        ai.Move(42.5f, EachTankCrossesTheMiddleSoThatItsBossFollowsOntoIt, BaitJitter);
        ai.Move(42.5f, HealersAndRangedTakeTheirSpotsForProteanOne);
        EveryLook(46.0f, EachTankTakesItsHoldBy, at => ai.Move(at, EachTankTakesItsHoldOnceItsBossHasComeToRest, BaitJitter));
        EveryLook(49.3f, ThePullLastLooksAt, at => ai.Move(at, EachRangedDrawsItsDollThroughTheNearerBossAfterTheSecondExhaustThenGoesBackOut, BaitJitter));
    }

    private static void EveryLook(float from, float until, Action<float> schedule)
    {
        for (var look = 0; from + look * ThePullLooksEvery <= until + 0.001f; look++)
            schedule(from + look * ThePullLooksEvery);
    }

    private IAiMove EveryoneBehindTheMainTankWithTheLiquidFarNorth()
        => Turned(TrueNorth,
            (MainTank, new(0f, 2.0f)), (OffTank, new(0f, 0f)), (MeleeDpsA, new(2.2f, 0.6f)), (MeleeDpsB, new(-2.7f, 0.6f)),
            (RegenHealer, new(0.2f, 2.9f)), (ShieldHealer, new(2.3f, 2.7f)), (PhysRangedDps, new(-3.4f, 3.2f)), (CasterDps, new(-1.8f, 2.9f)));

    private IAiMove TheMainTankWalksThroughTheLiquidToTurnItNorth() => Turned(TrueNorth, (MainTank, new(0f, -7.9f)));

    private IAiMove TheMainTankStepsInsideTheReachOfAnyNewPuddleAsCascadeShows() => Turned(TrueNorth, (MainTank, new(0f, -6.0f)));

    private IAiMove TheMainTankSprintsToTheRimBesideTheMiddleTornado()
        => Turned(state.Tornadoes1, (MainTank, TheMainTanksBaitBesideTheMiddleTornado));

    private IAiMove TheOffTankTakesTheHandFarNorthEastAndTheRangedBaitTheSideTornadoes()
        => Turned(state.Tornadoes1, (OffTank, TheOffTanksSplit), (PhysRangedDps, TheFirstRangedsBait), (CasterDps, TheSecondRangedsBait));

    private IAiMove OneHealerFarNorthTheOtherFarSouthEastAndTheMeleeSouthWestOfTheMiddle()
        => Turned(state.Tornadoes1,
            (RegenHealer, new(0f, -14.3f)), (ShieldHealer, new(9.6f, 10.8f)), (MeleeDpsA, new(-5.5f, 4.5f)), (MeleeDpsB, new(-3.2f, 5.2f)));

    private void TheMainTankProvokesTheHandOnceItShowsAPalmWithinItsReach()
    {
        if (theMainTankHasSeenToTheHand || fight.HandShowsAPalm != true || fight.Hand is not { } hand || state.Presses is not { } presses) return;
        if (TheRunPressesFor(MainTank, aHumanInItCouldPress: state.Plan.HumanTanks) is not { } mainTank || !presses.ProvokeReaches(mainTank, hand)) return;
        theMainTankHasSeenToTheHand = true;
        if (!ReferenceEquals(fight.Target(TeaP1Boss.LiquidHand), mainTank)) presses.Provoke(mainTank, hand);
    }

    private IAiMove TheOffTankGoesToTheLiquidsFlankOnceTheHandHasLeftIt()
    {
        if (theOffTankHasLeftForTheFlank || fight.Hand is null) return Nobody();
        if (world.Party.Get(OffTank) is not { } offTank || ReferenceEquals(fight.Target(TeaP1Boss.LiquidHand), offTank)) return Nobody();
        theOffTankHasLeftForTheFlank = true;
        return Turned(state.Tornadoes1, (OffTank, TheLiquidsFlank));
    }

    private IAiMove TheRangedStepOutOfTheirTornadoesTelegraphs()
        => Turned(state.Tornadoes1, (PhysRangedDps, new(11.5f, -8.4f)), (CasterDps, new(-11.5f, -8.4f)));

    private IAiMove TheRangedStepBackOntoTheirBaitsOnceTheTelegraphsHaveFired()
        => Turned(state.Tornadoes1, (PhysRangedDps, TheFirstRangedsBait), (CasterDps, TheSecondRangedsBait));

    private IAiMove EachMeleeStandsHalfAYalmOffItsDollAwayFromTheTornadoBesideIt()
        => Turned(state.Tornadoes1, Melee
            .Where(seat => TheDollOfItsQuarter(seat) is not null)
            .Select(seat => (seat, Beside(TheDollOfItsQuarter(seat)!)))
            .ToArray());

    private Vector2 Beside(TeaP1JagdDoll doll)
    {
        var appeared = state.Tornadoes1.ToRelative(doll.Spot);
        var nearest = Tornadoes.MinBy(tornado => Vector2.Distance(tornado, appeared));
        return appeared + BesideItsDoll * Vector2.Normalize(appeared - nearest);
    }

    private void TheMainTankTurnsInvulnerableForTheWaveThatFollowsTheBusters()
    {
        if (TheRunPressesFor(MainTank, aHumanInItCouldPress: state.Plan.HumanTanks) is { } mainTank) state.Presses?.Invulnerability(mainTank);
    }

    private void EachDamageDealerHitsItsDollOnceNoBossStandsByIt()
    {
        foreach (var seat in DamageDealers)
        {
            if (TheRunPressesFor(seat, aHumanInItCouldPress: true) is not { } member) continue;
            if (TheDollLeftToHitFor(seat, member) is { } doll && InReachWithNoBossBy(seat, member, doll)) state.Presses?.Strike(member, doll.Enemy);
        }
    }

    private bool ItIsTooLateToTakeADoll(PartyRole seat) => Melee.Contains(seat) && world.Events.Elapsed >= AMeleeTakesNoDollFrom - 0.001f;

    private TeaP1JagdDoll? TheDollOfItsQuarter(PartyRole seat)
        => state.Plan.DollOf(seat) is { } quarter ? fight.Dolls.In(state.Tornadoes1, quarter) : null;

    private TeaP1JagdDoll? TheDollLeftToHitFor(PartyRole seat, SimCharacter member)
    {
        if (ItIsTooLateToTakeADoll(seat) || fight.Dolls.All.Any(doll => ReferenceEquals(doll.Holder, member))) return null;
        var toHit = TheDollOfItsQuarter(seat);
        if (toHit is { HasBeenHit: true })
            toHit = world.Events.Elapsed < WhenItTakesTheDollOfWhoeverTook(toHit, seat) - 0.001f ? null : TheDollOfWhoeverTook(toHit);
        return toHit is { IsUp: true, HasBeenHit: false } ? toHit : null;
    }

    private static float WhenItTakesTheDollOfWhoeverTook(TeaP1JagdDoll itsOwn, PartyRole seat)
        => Melee.Contains(seat) && !(itsOwn.Holder is ISimPartyMember { Role: var taker } && Melee.Contains(taker))
            ? AMeleeTakesTheDollOfOneWhoDrewItsOwnAwayFrom
            : ABotTakesAnothersDollFrom;

    private TeaP1JagdDoll? TheDollOfWhoeverTook(TeaP1JagdDoll doll)
        => doll.Holder is ISimPartyMember { Role: var taker } ? TheDollOfItsQuarter(taker) : null;

    private bool InReachWithNoBossBy(PartyRole seat, SimCharacter member, TeaP1JagdDoll doll)
        => TeaP1LivingLiquid.Apart(member.Position, doll.Enemy.Position) <= (Melee.Contains(seat) ? AMeleeReaches : ARangedReaches)
           && TheBosses().All(boss => TeaP1LivingLiquid.Apart(boss.Position, doll.Enemy.Position) >= NoBossThisCloseToADollBeingHit)
           && !TheTanksAreAboutToLeadTheBossesPast(doll);

    private bool TheTanksAreAboutToLeadTheBossesPast(TeaP1JagdDoll doll)
        => world.Events.Elapsed >= AMeleeLeavesADollOnTheBossesWayInAloneFrom - 0.001f
           && world.Events.Elapsed < TheTanksHaveLedTheBossesInBy - 0.001f
           && Melee.Any(melee => ReferenceEquals(TheDollOfItsQuarter(melee), doll));

    private IEnumerable<SimEnemy> TheBosses()
        => new[] { fight.Liquid, fight.Hand }.Where(boss => boss is not null && boss.IsAlive()).Select(boss => boss!);

    private IAiMove AMeleeWhoseDollSomeoneElseTookGoesToThatOnesDoll()
    {
        var going = new List<(PartyRole, Vector2)>();
        foreach (var seat in Melee)
        {
            if (wentToAnothersDoll.Contains(seat) || world.Party.Get(seat) is not { } member || !member.IsAlive()) continue;
            if (TheDollOfItsQuarter(seat) is not { HasBeenHit: true } || TheDollLeftToHitFor(seat, member) is not { } anothers) continue;
            wentToAnothersDoll.Add(seat);
            going.Add((seat, Beside(anothers)));
        }
        return Turned(state.Tornadoes1, going.ToArray());
    }

    private void TheOffTankProvokesTheHandBackOnceItIsWithinItsReach()
    {
        if (theOffTankHasSeenToTheHand || fight.Hand is not { } hand || state.Presses is not { } presses) return;
        if (TheRunPressesFor(OffTank, aHumanInItCouldPress: state.Plan.HumanTanks) is not { } offTank || !presses.ProvokeReaches(offTank, hand)) return;
        theOffTankHasSeenToTheHand = true;
        if (!ReferenceEquals(fight.Target(TeaP1Boss.LiquidHand), offTank)) presses.Provoke(offTank, hand);
    }

    private IAiMove EachMeleeDrawsItsDollThroughTheNearerBossOnceItIsUnderAQuarterThenGoesNorth()
        => TheFeedersAmong(Melee, thoseLeftWithoutADollGoToo: true);

    private IAiMove EachRangedDrawsItsDollThroughTheNearerBossAfterTheSecondExhaustThenGoesBackOut()
        => TheFeedersAmong(Ranged, thoseLeftWithoutADollGoToo: false);

    private IAiMove TheFeedersAmong(PartyRole[] seats, bool thoseLeftWithoutADollGoToo)
    {
        var going = new List<(PartyRole, Vector2)>();
        foreach (var seat in seats)
        {
            if (doneWithItsDoll.Contains(seat) || world.Party.Get(seat) is not { } member || !member.IsAlive()) continue;
            if (fight.Dolls.HeldBy(member) is { } doll)
            {
                if (WhereToDrawItThroughABossAtRest(seat, member, doll) is { } past) going.Add((seat, past));
                continue;
            }
            if (TheDollLeftToHitFor(seat, member) is not null) continue;
            doneWithItsDoll.Add(seat);
            if (thoseLeftWithoutADollGoToo || drawsItsDollThroughABossAt.ContainsKey(seat))
                going.AddRange(WhereProteanOneStarts().Where(spot => spot.Seat == seat));
        }
        return Turned(state.Tornadoes1, going.ToArray());
    }

    private Vector2? WhereToDrawItThroughABossAtRest(PartyRole seat, SimCharacter member, TeaP1JagdDoll doll)
    {
        if (doll.Health(world.Events.Elapsed) >= P1Rules.DollIsReadyUnder) return null;
        var nearest = TheBosses().MinBy(boss => TeaP1LivingLiquid.Apart(boss.Position, member.Position));
        if (nearest is not { MoveDestination: null }) return null;
        var (boss, from) = (state.Tornadoes1.ToRelative(nearest.Position), state.Tornadoes1.ToRelative(doll.Enemy.Position));
        if (drawsItsDollThroughABossAt.TryGetValue(seat, out var before) && Vector2.Distance(before, boss) < ABossHasMovedBeyond) return null;
        var straightOn = MathF.Atan2(boss.X - from.X, from.Y - boss.Y) * 180f / MathF.PI;
        foreach (var turn in TurnedRoundTheBossBy)
        {
            var past = Off(boss, PastTheBossADollIsDrawnThrough, straightOn + turn);
            if (past.Length() > AFeederStaysWithinOfTheMiddle || Tornadoes.Any(tornado => Vector2.Distance(tornado, past) < AFeederKeepsFromEveryTornado)) continue;
            drawsItsDollThroughABossAt[seat] = boss;
            return past;
        }
        return null;
    }

    private IAiMove EachTankCrossesTheMiddleSoThatItsBossFollowsOntoIt()
        => Turned(state.Tornadoes1,
            (MainTank, AcrossTheMiddleFrom(TheLiquidIn(state.Tornadoes1), fight.StopsFromItsTarget(TeaP1Boss.LivingLiquid))),
            (OffTank, AcrossTheMiddleFrom(TheHandIn(state.Tornadoes1), fight.StopsFromItsTarget(TeaP1Boss.LiquidHand))));

    private IAiMove HealersAndRangedTakeTheirSpotsForProteanOne()
        => Turned(state.Tornadoes1, WhereProteanOneStarts().Where(spot => Healers.Contains(spot.Seat) || Ranged.Contains(spot.Seat)).ToArray());

    private IAiMove EachTankTakesItsHoldOnceItsBossHasComeToRest()
        => Turned(state.Tornadoes1, WhereProteanOneStarts()
            .Where(spot => Tanks.Contains(spot.Seat) && TakesItsHoldNow(spot.Seat, spot.Seat == MainTank ? fight.Liquid : fight.Hand))
            .ToArray());

    private bool TakesItsHoldNow(PartyRole tank, SimEnemy? itsBoss)
    {
        if (holdsItsBoss.Contains(tank) || world.Party.Get(tank) is not { } member) return false;
        var bothAtRest = member.MoveDestination is null && itsBoss is null or { MoveDestination: null };
        if (!bothAtRest && world.Events.Elapsed < EachTankTakesItsHoldBy - 0.001f) return false;
        holdsItsBoss.Add(tank);
        return true;
    }

    private SimCharacter? TheRunPressesFor(PartyRole seat, bool aHumanInItCouldPress)
    {
        if (world.Party.Get(seat) is not { } member || !member.IsAlive()) return null;
        if (!ReferenceEquals(member, world.Party.Player) || !aHumanInItCouldPress) return member;
        return world.Party.IsBotDriven(member) && !state.PlayerPressesItsOwnActions ? member : null;
    }
}
