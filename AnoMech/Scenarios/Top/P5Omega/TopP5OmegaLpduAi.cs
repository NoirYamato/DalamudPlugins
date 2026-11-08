using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Top.TopConstants;

namespace AnoMech.Scenarios.Top.P5Omega;

public sealed class TopP5OmegaLpduAi : IScenarioAi<TopP5OmegaState>
{
    public string Name => "[LPDU] Light PF";
    public string? Group => "EU";

    internal static readonly Vector2?[] FirstHelloWorldSpotsWhenTheMonitorsFireWest =
    [
        new(10f, 0f), new(0.2f, 10f),
        new(-10f, -10f), new(-10f, 10f),
        new(0.2f, -19f), new(19f, -4f), new(19f, 4f), new(0.2f, 19f),
    ];

    internal static readonly Vector2?[] GatherWithTheTetherTakersTowardTheBeetleWhenItIsNorth =
    [
        new(0f, 0f), new(0f, 0f),
        new(0f, -3f), new(0f, -3f),
        new(0f, 0f), new(0f, 0f), new(0f, 0f), new(0f, 0f),
    ];

    internal static readonly Vector2?[] SecondHelloWorldSpotsWhenTheBeetleIsNorth =
    [
        new(0f, 10f), new(-10f, 0f),
        new(9.5f, -16.5f), new(-9.5f, -16.5f),
        new(19f, 0f), new(4f, 19f), new(-4f, 19f), new(-19f, 0f),
    ];

    internal const float FirstFetch = 48.25f, LastFetchBeforeTheWalkOut = 52.75f;
    private const float OnTheSecondSpotsBy = 56.75f;
    private const float FetchEvery = 0.25f;
    private const float FetchAheadOfTheHolder = 1.5f, FetchWithinRadius = 19.95f, FetchSprintsFartherThan = 5f;
    private const float LeadTheHolderAtMost = 2f;
    private const float SurelyAheadOfTheHolder = 0.3f, SurelyWithinThePassLane = 0.35f;
    private const float PassesFromThisFarAhead = 0.1f, NearlyWithinThePassLane = 0.8f;
    private const float StandingOnTheGatherSpot = 0.25f, SetOffForItsSecondSpotWithin = 0.5f;
    private const float CrossTheBeetlesLineAtLeastThisFarTowardIt = 1f;
    private const float BesideTheMiddleTowardTheBeetle = 2f, BesideTheMiddleAside = 2.5f;
    private const float RunSpeed = 6.5f, SprintLasts = 10f;

    private TopP5OmegaState state = null!;
    private SimWorld world = null!;
    private PartyRole[] firstHelloWorldOrder = [];
    private readonly Dictionary<PartyRole, PartyRole> fetcherOfEachHolder = new();
    private readonly HashSet<PartyRole> sentToFetch = [];
    private readonly Dictionary<PartyRole, float> sprintingUntil = new();
    private readonly Dictionary<PartyRole, (Vector2 At, float Time)> lastSeen = new();

    public void Run(TopP5OmegaState s, SimWorld w)
    {
        state = s;
        world = w;
        firstHelloWorldOrder = FirstHelloWorldOrder(s);
        fetcherOfEachHolder.Clear();
        sentToFetch.Clear();
        sprintingUntil.Clear();
        lastSeen.Clear();
        var ai = new AiManager(world);
        ai.Move(0f, TanksNorthOfOmegaTheRestSouth);
        ai.Move(20f, StackOnTheFirstOmegaPairsSafeSpot, arrivalTime: 24f);
        ai.Move(24f, StackOnTheSecondOmegaPairsSafeSpot, arrivalTime: 28f);
        ai.Automarker(28f, () => MarkInHelloWorldOrder(firstHelloWorldOrder));
        ai.Move(32f, FirstHelloWorldSpotsOnTheMonitorsSide, jitter: 0.1f, arrivalTime: 41f);
        ai.Automarker(47f, () => MarkInHelloWorldOrder(state.HelloWorld2));
        ai.Move(48f, GatherInTheMiddleWithTheTetherTakersTowardTheBeetle, jitter: 0f);
        for (var t = FirstFetch; t <= OnTheSecondSpotsBy; t += FetchEvery)
            world.Events.Add(t, TakersFetchTheTethersNonTakersStillHold);
        ai.Move(53f, SecondHelloWorldSpotsTurnedWithTheBeetleForAllButTheFetchers, arrivalTime: 57f);
        ai.Move(62f, TanksNorthOfOmegaTheRestSouth);
    }

    private static PartyRole[] FirstHelloWorldOrder(TopP5OmegaState state)
    {
        var jumps = state.HelloWorld1JumpOrder;
        return
        [
            state.HelloWorldTargets[0], state.HelloWorldTargets[1], state.MonitorTargets[0], state.MonitorTargets[1],
            jumps[0], jumps[1], jumps[2], jumps[3],
        ];
    }

    private static Dictionary<PartyRole, Sign> MarkInHelloWorldOrder(IReadOnlyList<PartyRole>? order)
    {
        if (order == null) return [];
        return new Dictionary<PartyRole, Sign>
        {
            [order[0]] = Sign.Triangle,
            [order[1]] = Sign.Cross,
            [order[2]] = Sign.Bind1,
            [order[3]] = Sign.Bind2,
            [order[4]] = Sign.Attack1,
            [order[5]] = Sign.Attack2,
            [order[6]] = Sign.Attack3,
            [order[7]] = Sign.Attack4,
        };
    }

    private static IAiMove TanksNorthOfOmegaTheRestSouth()
        => AiMove.Create(
                     new(-2.1f, -5.08f), new(2.1f, -5.08f),
                     new(-0.7f, 5.7f), new(-0.7f, 6.5f), new(-0.7f, 7.3f),
                     new(0.7f, 5.7f), new(0.7f, 6.5f), new(0.7f, 7.3f))
                 .NaturalOrder();

    private IAiMove StackOnTheFirstOmegaPairsSafeSpot() => StackOnTheSafeSpotOfOmegaPair(0);

    private IAiMove StackOnTheSecondOmegaPairsSafeSpot() => StackOnTheSafeSpotOfOmegaPair(1);

    private IAiMove StackOnTheSafeSpotOfOmegaPair(int pair)
        => AiMove.All(new(0f, -1f))
                 .ApplyPositions(TurnOntoTheCardinalBesideFThatThePairsWaveCannonsSpare(pair),
                                 StretchToTheYalmsTowardFThatFAndMLeaveSafe(pair));

    private Action<IAiPositions> TurnOntoTheCardinalBesideFThatThePairsWaveCannonsSpare(int pair)
    {
        var f = state.AttackDirections[pair * 2];
        var eighthsTowardNorthOrSouth = f.Index() % 4 == 1 ? -1 : +1;
        var eighthsTowardTheSparedCardinal = ThePairsWaveCannonsFireNorthAndSouth(pair) ? -eighthsTowardNorthOrSouth : eighthsTowardNorthOrSouth;
        return f.Rotate(eighthsTowardTheSparedCardinal).Apply;
    }

    private bool ThePairsWaveCannonsFireNorthAndSouth(int pair) => state.FirstWaveCannonFront == (pair == 0);

    private Action<IAiPositions> StretchToTheYalmsTowardFThatFAndMLeaveSafe(int pair)
    {
        var f = state.OmegaAttacks[pair * 2];
        var m = state.OmegaAttacks[pair * 2 + 1];
        float yalmsTowardF;
        if (f == OmegaAttack.Legs)
            yalmsTowardF = m == OmegaAttack.Sword ? 2.5f : -2.5f;
        else
            yalmsTowardF = m == OmegaAttack.Sword ? -17f : -10f;
        return move => move.Multiply(yalmsTowardF);
    }

    private IAiMove FirstHelloWorldSpotsOnTheMonitorsSide()
        => AiMove.Create(FirstHelloWorldSpotsWhenTheMonitorsFireWest)
                 .Assignments(firstHelloWorldOrder)
                 .ApplyPositions(MirrorWhenTheMonitorsFireEast);

    private void MirrorWhenTheMonitorsFireEast(IAiPositions move) => move.MultiplyX(state.MonitorSide.Mul);

    private IAiMove GatherInTheMiddleWithTheTetherTakersTowardTheBeetle()
        => AiMove.Create(GatherWithTheTetherTakersTowardTheBeetleWhenItIsNorth)
                 .Assignments(state.HelloWorld2)
                 .ApplyPositions(TurnWithTheBeetle);

    private IAiMove SecondHelloWorldSpotsTurnedWithTheBeetle()
        => AiMove.Create(SecondHelloWorldSpotsWhenTheBeetleIsNorth)
                 .Assignments(state.HelloWorld2)
                 .ApplyPositions(TurnWithTheBeetle);

    private IAiMove SecondHelloWorldSpotsTurnedWithTheBeetleForAllButTheFetchers()
        => AiMove.Create([.. SecondHelloWorldSpotsWhenTheBeetleIsNorth.Select((spot, i) => state.HelloWorld2 is { } order && sentToFetch.Contains(order[i]) ? null : spot)])
                 .Assignments(state.HelloWorld2)
                 .ApplyPositions(TurnWithTheBeetle);

    private void TurnWithTheBeetle(IAiPositions move) => state.BettleSpawnDirection.Apply(move);

    private void TakersFetchTheTethersNonTakersStillHold()
    {
        if (state.HelloWorld2 is not { } order) return;
        PartyRole[] takers = [order[2], order[3]];
        var tethers = BlasterTethersAndTheirHolders();
        var holdersMove = HowFastEachHolderMoves(tethers);
        ForgetFetchesThatAreOver(tethers);
        foreach (var (holder, tether) in tethers)
        {
            if (takers.Contains(holder) || !CanBeFetchedFrom(holder)) continue;
            var moving = holdersMove.GetValueOrDefault(holder);
            if (fetcherOfEachHolder.TryGetValue(holder, out var fetcher))
                GoToTheTether(fetcher, tether, moving);
            else if (!takers.Any(taker => IsFree(taker, tethers) && StandsOrHeadsIntoThePassLane(taker, tether))
                     && TheFreeTakerDoneSoonest(takers, tethers, tether, moving) is { } taker)
                SendToFetch(taker, holder, tether, moving);
        }
        foreach (var fetcher in FetchersNotChasingATether())
            if (Holds(fetcher, tethers)) WalkOutWithTheTether(fetcher, takers, tethers);
            else if (TheGatherIsOver()) WalkToTheSecondSpotInTime(fetcher);
            else GoBackToTheGatherSpot(fetcher);
    }

    private IEnumerable<PartyRole> FetchersNotChasingATether()
        => sentToFetch.Where(fetcher => world.Party.Get(fetcher).IsAlive() && !fetcherOfEachHolder.ContainsValue(fetcher));

    private void WalkToTheSecondSpotInTime(PartyRole fetcher)
        => GoUnlessGoing(fetcher, SecondHelloWorldSpot(fetcher), MustSprintToArriveInTime(At(fetcher), SecondHelloWorldSpot(fetcher)));

    private void GoBackToTheGatherSpot(PartyRole fetcher)
        => GoUnlessGoing(fetcher, GatherSpot(fetcher), sprint: Vector2.Distance(At(fetcher), GatherSpot(fetcher)) > FetchSprintsFartherThan);

    private bool TheGatherIsOver() => world.Events.Elapsed >= LastFetchBeforeTheWalkOut + FetchEvery / 2f;

    private List<(PartyRole Holder, SimTether Tether)> BlasterTethersAndTheirHolders()
        => world.Children.OfType<SimTether>()
                .Where(t => t is { IsActive: true, TetherId: TetherId.PassableTether, A: ISimPartyMember, B: not null } && t.A.IsAlive())
                .Select(t => (((ISimPartyMember)t.A!).Role, t))
                .ToList();

    private Dictionary<PartyRole, Vector2> HowFastEachHolderMoves(List<(PartyRole Holder, SimTether Tether)> tethers)
    {
        var now = world.Events.Elapsed;
        var moves = new Dictionary<PartyRole, Vector2>();
        foreach (var (holder, _) in tethers)
            if (lastSeen.TryGetValue(holder, out var seen) && now - seen.Time is > 0f and <= FetchEvery + 0.05f)
                moves[holder] = (At(holder) - seen.At) / (now - seen.Time);
        lastSeen.Clear();
        foreach (var (holder, _) in tethers) lastSeen[holder] = (At(holder), now);
        return moves;
    }

    private static bool Holds(PartyRole role, List<(PartyRole Holder, SimTether Tether)> tethers)
        => tethers.Any(t => t.Holder == role);

    private bool IsFree(PartyRole role, List<(PartyRole Holder, SimTether Tether)> tethers)
        => world.Party.Get(role).IsAlive() && !Holds(role, tethers);

    private void ForgetFetchesThatAreOver(List<(PartyRole Holder, SimTether Tether)> tethers)
    {
        foreach (var (holder, fetcher) in fetcherOfEachHolder.ToList())
            if (!Holds(holder, tethers) || !IsFree(fetcher, tethers))
                fetcherOfEachHolder.Remove(holder);
    }

    private bool CanBeFetchedFrom(PartyRole holder)
        => world.Party.Get(holder) is { } member
           && (!world.Party.IsBotDriven(member) || TheGatherIsOver() || StandsOnItsGatherSpot(holder));

    private bool StandsOnItsGatherSpot(PartyRole role)
        => world.Party.Get(role)!.MoveDestination is null && Vector2.Distance(At(role), GatherSpot(role)) <= StandingOnTheGatherSpot;

    private bool StandsOrHeadsIntoThePassLane(PartyRole taker, SimTether tether)
        => SurelyInThePassLane(tether, At(taker))
           || world.Party.Get(taker)!.MoveDestination is { } going && SurelyInThePassLane(tether, Flat(going));

    private static bool SurelyInThePassLane(SimTether tether, Vector2 at)
        => InTheLaneToTheBeetle(Flat(tether.A!.Position), Flat(tether.B!.Position), at, SurelyAheadOfTheHolder, SurelyWithinThePassLane);

    private static bool InTheLaneToTheBeetle(Vector2 from, Vector2 beetle, Vector2 at, float ahead, float within)
    {
        var length = Vector2.Distance(from, beetle);
        if (length < 0.01f) return false;
        var toward = (beetle - from) / length;
        var offset = at - from;
        var along = Vector2.Dot(offset, toward);
        return along >= ahead && along <= length && MathF.Abs(toward.X * offset.Y - toward.Y * offset.X) <= within;
    }

    private PartyRole? TheFreeTakerDoneSoonest(PartyRole[] takers, List<(PartyRole Holder, SimTether Tether)> tethers, SimTether tether, Vector2 holderMoves)
        => takers.Where(taker => IsFree(taker, tethers) && IsABotOtherThanThePlayer(taker)
                                 && !fetcherOfEachHolder.ContainsValue(taker) && NoWalkOutIsPendingFor(taker))
                 .Select(taker => (PartyRole?)taker)
                 .MinBy(taker =>
                 {
                     var meet = WhereToMeetTheHolder(taker!.Value, tether, holderMoves);
                     return Vector2.Distance(At(taker.Value), meet) + Vector2.Distance(meet, SecondHelloWorldSpot(taker.Value));
                 });

    private bool IsABotOtherThanThePlayer(PartyRole role)
        => world.Party.Get(role) is { } member && world.Party.IsBotDriven(member) && !ReferenceEquals(member, world.Party.Player);

    private bool NoWalkOutIsPendingFor(PartyRole taker)
        => !TheGatherIsOver() || sentToFetch.Contains(taker) || HasSetOffForItsSecondSpot(taker);

    private bool HasSetOffForItsSecondSpot(PartyRole taker)
        => Vector2.Distance(world.Party.Get(taker)!.MoveDestination is { } going ? Flat(going) : At(taker), SecondHelloWorldSpot(taker))
           <= SetOffForItsSecondSpotWithin;

    private void SendToFetch(PartyRole taker, PartyRole holder, SimTether tether, Vector2 holderMoves)
    {
        fetcherOfEachHolder[holder] = taker;
        sentToFetch.Add(taker);
        GoToTheTether(taker, tether, holderMoves);
    }

    private void GoToTheTether(PartyRole fetcher, SimTether tether, Vector2 holderMoves)
    {
        var meet = WhereToMeetTheHolder(fetcher, tether, holderMoves);
        Go(fetcher, meet, sprint: Vector2.Distance(At(fetcher), meet) > FetchSprintsFartherThan);
    }

    private Vector2 WhereToMeetTheHolder(PartyRole fetcher, SimTether tether, Vector2 holderMoves)
    {
        var holder = Flat(tether.A!.Position);
        var beetle = Flat(tether.B!.Position);
        var meet = FetchPoint(holder, beetle);
        for (var guess = 0; guess < 3; guess++)
        {
            var seconds = MathF.Min(Vector2.Distance(At(fetcher), meet) / AiManager.SprintSpeed, LeadTheHolderAtMost);
            meet = FetchPoint(InsideTheRim(holder + holderMoves * seconds), beetle);
        }
        return meet;
    }

    private static Vector2 FetchPoint(Vector2 holder, Vector2 beetle)
    {
        var toTheBeetle = beetle - holder;
        var length = toTheBeetle.Length();
        if (length < 0.01f) return holder;
        return InsideTheRim(holder + toTheBeetle / length * MathF.Min(FetchAheadOfTheHolder, length / 2f));
    }

    private static Vector2 InsideTheRim(Vector2 at) => at.Length() > FetchWithinRadius ? at * (FetchWithinRadius / at.Length()) : at;

    private void WalkOutWithTheTether(PartyRole fetcher, PartyRole[] takers, List<(PartyRole Holder, SimTether Tether)> tethers)
    {
        var spot = SecondHelloWorldSpot(fetcher);
        var beetle = Flat(tethers.First(t => t.Holder == fetcher).Tether.B!.Position);
        var next = CrossesTheBeetlesLineBehindTheMiddle(At(fetcher), spot) ? BesideTheMiddleOnItsSide(At(fetcher)) : spot;
        if (AFreeNonTakerWouldTakeItFrom(spot, beetle, takers, tethers) && ThereIsTimeToWait(At(fetcher), next, spot)) return;
        GoUnlessGoing(fetcher, next, MustSprintToArriveInTime(At(fetcher), next, spot));
    }

    private bool AFreeNonTakerWouldTakeItFrom(Vector2 spot, Vector2 beetle, PartyRole[] takers, List<(PartyRole Holder, SimTether Tether)> tethers)
        => Enum.GetValues<PartyRole>().Any(role => !takers.Contains(role) && IsFree(role, tethers)
                                                 && InTheLaneToTheBeetle(spot, beetle, At(role), PassesFromThisFarAhead, NearlyWithinThePassLane));

    private bool ThereIsTimeToWait(params Vector2[] path)
        => world.Events.Elapsed + FetchEvery + Length(path) / AiManager.SprintSpeed < OnTheSecondSpotsBy;

    private bool MustSprintToArriveInTime(params Vector2[] path)
        => Length(path) > RunSpeed * (OnTheSecondSpotsBy - world.Events.Elapsed);

    private static float Length(Vector2[] path) => path.Zip(path.Skip(1), Vector2.Distance).Sum();

    private bool CrossesTheBeetlesLineBehindTheMiddle(Vector2 from, Vector2 to)
    {
        var (fromAside, toAside) = (Aside(from), Aside(to));
        if (fromAside >= 0f == toAside >= 0f) return false;
        var crossing = TowardTheBeetle(from) + (TowardTheBeetle(to) - TowardTheBeetle(from)) * fromAside / (fromAside - toAside);
        return crossing < CrossTheBeetlesLineAtLeastThisFarTowardIt;
    }

    private Vector2 BesideTheMiddleOnItsSide(Vector2 at)
        => TheBeetlesBearing() * BesideTheMiddleTowardTheBeetle
           + TheBeetlesAside() * (Aside(at) >= 0f ? BesideTheMiddleAside : -BesideTheMiddleAside);

    private float TowardTheBeetle(Vector2 at) => Vector2.Dot(at, TheBeetlesBearing());

    private float Aside(Vector2 at) => Vector2.Dot(at, TheBeetlesAside());

    private Vector2 TheBeetlesBearing() => Flat(state.BettleSpawnDirection.Apply(new Vector3(0f, 0f, -1f)));

    private Vector2 TheBeetlesAside() => new(-TheBeetlesBearing().Y, TheBeetlesBearing().X);

    private void GoUnlessGoing(PartyRole role, Vector2 to, bool sprint)
    {
        var heading = world.Party.Get(role)!.MoveDestination is { } going ? Flat(going) : At(role);
        if (Vector2.Distance(heading, to) >= 0.01f) Go(role, to, sprint);
    }

    private void Go(PartyRole role, Vector2 to, bool sprint)
    {
        var member = world.Party.Get(role)!;
        var now = world.Events.Elapsed;
        if (sprint && sprintingUntil.GetValueOrDefault(role) <= now)
        {
            (member as ISimPartyMember)?.UseSprint(SprintLasts);
            sprintingUntil[role] = now + SprintLasts;
        }
        member.MoveTo(new Vector3(to.X, 0f, to.Y), sprintingUntil.GetValueOrDefault(role) > now ? AiManager.SprintSpeed : RunSpeed);
    }

    private Vector2 GatherSpot(PartyRole role) => GatherInTheMiddleWithTheTetherTakersTowardTheBeetle()[(int)role]!.Value;

    private Vector2 SecondHelloWorldSpot(PartyRole role) => SecondHelloWorldSpotsTurnedWithTheBeetle()[(int)role]!.Value;

    private Vector2 At(PartyRole role) => Flat(world.Party.Get(role)!.Position);

    private static Vector2 Flat(Vector3 position) => new(position.X, position.Z);
}
