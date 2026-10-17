using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.P3HelloWorld.TopP3HelloWorldTraffic;
using static AnoMech.Scenarios.Top.TopP3Constants;

namespace AnoMech.Scenarios.Top.P3HelloWorld;

public class TopP3HelloWorldLpduAi : IScenarioAi<TopP3HelloWorldState>
{
    public string Name => "[LPDU] Light PF";
    public string? Group => "EU";

    private readonly record struct Polar(float Degrees, float Radius);

    private readonly record struct Spots(Polar L, Polar R)
    {
        public static Spots Mirrored(float degrees, float radius) => new(new(-degrees, radius), new(degrees, radius));
    }

    private readonly record struct Cues(float Towers, float TetheredPairsLatest, float TethersGoLive, float Resolve, float Pass,
                                        float RemotePops, float RemotePopsLatest, float LocalPops, float LocalPopsLatest);

    private static readonly Cues[] SetCues =
    [
        new(Towers: 20.1f, TetheredPairsLatest: 24.1f, TethersGoLive: 28f, Resolve: 29.1f, Pass: 29.4f,
            RemotePops: 31.4f, RemotePopsLatest: 32.6f, LocalPops: 34.1f, LocalPopsLatest: 36.1f),
        new(Towers: 41.1f, TetheredPairsLatest: 45.1f, TethersGoLive: 49f, Resolve: 50.1f, Pass: 50.4f,
            RemotePops: 52.4f, RemotePopsLatest: 53.6f, LocalPops: 55.1f, LocalPopsLatest: 57.1f),
        new(Towers: 62.2f, TetheredPairsLatest: 66.2f, TethersGoLive: 70f, Resolve: 71.2f, Pass: 71.5f,
            RemotePops: 73.5f, RemotePopsLatest: 74.7f, LocalPops: 76.2f, LocalPopsLatest: 78.2f),
        new(Towers: 83.5f, TetheredPairsLatest: 87.5f, TethersGoLive: 91f, Resolve: 92.5f, Pass: 92.8f,
            RemotePops: 94.8f, RemotePopsLatest: 96f, LocalPops: 97.5f, LocalPopsLatest: 99.5f),
    ];

    private static readonly Spots DefamationOuterTowerEdge = Spots.Mirrored(45f, 18.1f);
    private static readonly Spots DefamationTowerCentre = Spots.Mirrored(45f, 13.7f);
    private static readonly Spots StackInnerTowerEdge = Spots.Mirrored(154f, 12f);
    private static readonly Spots RemoteInsideStack = Spots.Mirrored(166f, 11.4f);
    private static readonly Spots LocalStretchedPastDefamation = Spots.Mirrored(74f, 14.2f);
    private static readonly Spots LastLocalInsideStack = new(new(-165f, 10.5f), new(165f, 11.2f));

    private static readonly Spots RemotePopped = new(new(-141f, 17f), new(149f, 11.6f));
    private static readonly Spots StackSpreadingRot = new(new(-120f, 10f), new(112f, 10f));
    private static readonly Spots LocalWaitingForVulnsToFade = new(new(-42f, 7.7f), new(49f, 7.3f));
    private static readonly Spots LocalPopped = Spots.Mirrored(22f, 7.4f);
    private static readonly Spots RemoteBackInStackLane = new(new(-158f, 12f), new(165f, 12f));

    private static readonly Spots LastStackStartingToSpread = new(new(-137f, 8.4f), new(127f, 9.8f));
    private static readonly Spots LastRemotePopped = new(new(-141f, 16f), new(146f, 13.1f));
    private static readonly Spots LastStackSpreadRot = new(new(-126f, 14.2f), new(113f, 14.3f));
    private static readonly Spots LastRemoteWaiting = new(new(-154f, 14.6f), new(153f, 11.9f));
    private static readonly Spots LastLocalWaiting = new(new(-167f, 12.9f), new(175f, 12.3f));

    private static readonly Spots DefamationColourTowers = Spots.Mirrored(45f, P3Geometry.TowerRingRadius);
    private static readonly Spots StackColourTowers = Spots.Mirrored(135f, P3Geometry.TowerRingRadius);
    private const float TowerRadius = 6f;
    private const float TowerEdgeMargin = 0.2f, BackInTheTowerAhead = 1.5f;

    private const float SummedWalksThisCloseAreATie = 0.5f;
    private const float SoakerInPlace = 1f;
    private const float PollSeconds = 0.1f;
    private const float RotsGoLive = 8f, WatchTheHumanSeconds = 0.1f;
    private const float LineUpFrom = 100f, LastLineUpPoll = 105f;
    private const float SoakersArriveWithin = 7f, ArriveBeforeTethersGoLive = 0.3f, PassesLandWithin = 1.7f;
    private const float RemotePopsArriveBeforeTheLocalPops = 0.1f;
    private const float TetherBreakMargin = 0.3f;
    private const float RemotePopsWithin = 1.7f, LocalPopsWithin = 2.5f, RemotePopVulnLasts = 1.2f;

    private static readonly PartyRole[] MonitorsLineUpNorthToSouth =
        [RegenHealer, ShieldHealer, MainTank, OffTank, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps];

    private static readonly Dictionary<PartyRole, float> LooseSpreadBearings = new()
    {
        [MainTank] = 0f, [PhysRangedDps] = 45f, [ShieldHealer] = 90f, [MeleeDpsB] = 135f,
        [OffTank] = 180f, [MeleeDpsA] = 225f, [RegenHealer] = 270f, [CasterDps] = 315f,
    };

    private TopP3HelloWorldState state = null!;
    private SimWorld world = null!;
    private TopP3HelloWorldTraffic traffic = null!;
    private readonly Dictionary<PartyRole, bool> onRightSide = new();
    private readonly HashSet<int> stretchedSets = [];
    private readonly HashSet<(int Set, PartyRole Role)> remotePopsSent = [], localPopsSent = [];
    private readonly Dictionary<int, float> remoteTetherBrokeAt = new();
    private int towersUp;
    private bool linedUp;

    public void Run(TopP3HelloWorldState s, SimWorld w)
    {
        state = s;
        world = w;
        traffic = new TopP3HelloWorldTraffic(world, SoakersAreBackInTheirOwnTowerAsItGoesOff);
        onRightSide.Clear();
        stretchedSets.Clear();
        remotePopsSent.Clear();
        localPopsSent.Clear();
        remoteTetherBrokeAt.Clear();
        towersUp = 0;
        linedUp = false;
        world.Events.Add(0.5f, LooseSpread);
        for (var set = 1; set <= TopP3HelloWorldState.Sets; set++)
        {
            var n = set;
            var cues = SetCues[n - 1];
            world.Events.Add(cues.Towers, () => SoakersTakeTheirTowers(n));
            Poll(cues.Towers + PollSeconds, cues.TetheredPairsLatest, () => TetheredPairsStretchOnceTheSoakersAreInPlace(n));
            world.Events.Add(cues.TetheredPairsLatest, () => TetheredPairsStretch(n));
            world.Events.Add(cues.Pass, () => PassRotsOnOrStartSpreading(n));
            Poll(cues.RemotePops, cues.RemotePopsLatest, () => PopRemoteTetherAndSpreadOldRotsOnceTheirPassIsDone(n));
            world.Events.Add(cues.RemotePopsLatest, () => PopRemoteTetherAndSpreadOldRots(n, ready: _ => true));
            Poll(cues.RemotePops, cues.LocalPopsLatest, () => NoteWhenTheRemoteTetherBroke(n));
            Poll(cues.LocalPops, cues.LocalPopsLatest, () => PopLocalTetherOnceTheRemoteVulnHasWornOff(n));
            world.Events.Add(cues.LocalPopsLatest, () => PopLocalTetherClearOfExplodingRots(n, ready: _ => true));
        }
        Poll(LineUpFrom, LastLineUpPoll, LineUpForMonitorsOnceTheLastRotsHaveGoneOff);
        Poll(RotsGoLive, LastLineUpPoll, traffic.KeepWatchingTheHuman, every: WatchTheHumanSeconds);
    }

    private void Poll(float from, float until, Action check, float every = PollSeconds)
    {
        for (var t = from; t < until; t += every)
            world.Events.Add(t, check);
    }

    private void LooseSpread()
        => Send(0, role => OnRing(LooseSpreadBearings[role], 8.5f), deadline: RotsGoLive);

    private void SoakersTakeTheirTowers(int set)
    {
        towersUp = set;
        TakeTheClosestSideOfEachPair(set);
        traffic.ExpectTheHumanToVisit(TheHumansSpots(set));
        Send(set, role => IsSoaker(set, role) ? TowerSpot(set, role) : null, deadline: SetCues[set - 1].Towers + SoakersArriveWithin);
    }

    private IEnumerable<Vector2> TheHumansSpots(int set)
    {
        var human = world.Party.PlayerRole;
        return new[] { TowerSpot(set, human), PassSpot(set, human, stackStandsAt: stack => TowerSpot(set, stack)),
                       RemotePopSpot(set, human), LocalPopSpot(set, human) }.OfType<Vector2>();
    }

    private void TetheredPairsStretchOnceTheSoakersAreInPlace(int set)
    {
        if (SoakersAreInPlace(set)) TetheredPairsStretch(set);
    }

    private void TetheredPairsStretch(int set)
    {
        if (!stretchedSets.Add(set)) return;
        var cues = SetCues[set - 1];
        Send(set, role => IsSoaker(set, role) ? null : TowerSpot(set, role), deadline: cues.TethersGoLive - ArriveBeforeTethersGoLive,
             tether: role => TetherHoldsUntilItsPop(set, role, from: cues.TethersGoLive));
    }

    private bool SoakersAreInPlace(int set)
        => PerRole.All.Where(role => IsSoaker(set, role) && Member(role).IsAlive())
                      .All(role => Vector2.Distance(Flat(Member(role).Position), TowerSpot(set, role)) <= SoakerInPlace);

    private bool IsSoaker(int set, PartyRole role) => state.RoleIn(set, role) is HelloWorldRole.Defamation or HelloWorldRole.Stack;

    private void PassRotsOnOrStartSpreading(int set)
    {
        var cues = SetCues[set - 1];
        Send(set, role => PassSpot(set, role, stackStandsAt: stack => Flat(Member(stack).Position)), deadline: cues.Pass + PassesLandWithin,
             tether: role => TetherHoldsUntilItsPop(set, role, from: cues.Pass), passesWith: role => PassPartner(set, role));
    }

    private PartyRole? PassPartner(int set, PartyRole role) => IsLastSet(set) ? null : state.RoleIn(set, role) switch
    {
        HelloWorldRole.Remote => Side(set, HelloWorldRole.Stack, onRightSide[role]),
        HelloWorldRole.Local => Side(set, HelloWorldRole.Defamation, onRightSide[role]),
        HelloWorldRole.Defamation => Side(set, HelloWorldRole.Local, onRightSide[role]),
        _ => null,
    };

    private void PopRemoteTetherAndSpreadOldRotsOnceTheirPassIsDone(int set)
        => PopRemoteTetherAndSpreadOldRots(set, ready: role => HasDoneItsPass(set, role));

    private void PopRemoteTetherAndSpreadOldRots(int set, Func<PartyRole, bool> ready)
        => SendOnce(set, remotePopsSent, ready, role => RemotePopSpot(set, role),
                    deadline: SetCues[set - 1].LocalPops - RemotePopsArriveBeforeTheLocalPops,
                    tether: role => state.RoleIn(set, role) == HelloWorldRole.Remote
                        ? RemoteTetherHasPopped(role)
                        : TetherHoldsUntilItsPop(set, role, from: SetCues[set - 1].RemotePops));

    private void PopLocalTetherOnceTheRemoteVulnHasWornOff(int set)
        => PopLocalTetherClearOfExplodingRots(set, ready: role => IsTheHuman(role) || state.RoleIn(set, role) != HelloWorldRole.Local
                                                                  || IsLastSet(set) || RemoteVulnHasWornOff(set));

    private void PopLocalTetherClearOfExplodingRots(int set, Func<PartyRole, bool> ready)
        => SendOnce(set, localPopsSent, ready, role => LocalPopSpot(set, role), deadline: SetCues[set - 1].LocalPopsLatest,
                    tether: role => LocalTetherHasPopped(set, role));

    private void LineUpForMonitorsOnceTheLastRotsHaveGoneOff()
    {
        var last = TopP3HelloWorldState.Sets;
        if (linedUp || !new[] { false, true }.All(right => RotHasGoneOff(last, HelloWorldRole.Defamation, right)
                                                          && RotHasGoneOff(last, HelloWorldRole.Stack, right)))
            return;
        linedUp = true;
        Send(0, role => OnRing(320f - 15.1f * Array.IndexOf(MonitorsLineUpNorthToSouth, role), 13.15f), deadline: LastLineUpPoll);
    }

    private Vector2 TowerSpot(int set, PartyRole role) => At(set, role, SpotsOf(set, state.RoleIn(set, role)));

    private Vector2? PassSpot(int set, PartyRole role, Func<PartyRole, Vector2> stackStandsAt) => state.RoleIn(set, role) switch
    {
        HelloWorldRole.Defamation => At(set, role, DefamationTowerCentre),
        HelloWorldRole.Local when !IsLastSet(set) => At(set, role, DefamationTowerCentre),
        HelloWorldRole.Remote when !IsLastSet(set) => stackStandsAt(Side(set, HelloWorldRole.Stack, onRightSide[role])),
        HelloWorldRole.Stack when IsLastSet(set) => At(set, role, LastStackStartingToSpread),
        _ => null,
    };

    private Vector2? RemotePopSpot(int set, PartyRole role) => (state.RoleIn(set, role), IsLastSet(set)) switch
    {
        (HelloWorldRole.Remote, false) => At(set, role, RemotePopped),
        (HelloWorldRole.Stack, false) => At(set, role, StackSpreadingRot),
        (HelloWorldRole.Local, false) => At(set, role, LocalWaitingForVulnsToFade),
        (HelloWorldRole.Remote, true) => At(set, role, LastRemotePopped),
        _ => null,
    };

    private Vector2? LocalPopSpot(int set, PartyRole role) => (state.RoleIn(set, role), IsLastSet(set)) switch
    {
        (HelloWorldRole.Local, false) => At(set, role, LocalPopped),
        (HelloWorldRole.Remote, false) => At(set, role, RemoteBackInStackLane),
        (HelloWorldRole.Stack, true) => At(set, role, LastStackSpreadRot),
        (HelloWorldRole.Remote, true) => At(set, role, LastRemoteWaiting),
        (HelloWorldRole.Local, true) => At(set, role, LastLocalWaiting),
        _ => null,
    };

    private Tether? TetherHoldsUntilItsPop(int set, PartyRole role, float from) => (state.RoleIn(set, role), IsLastSet(set)) switch
    {
        (HelloWorldRole.Remote, _) => new Tether(Partner(role), 0f, P3Geometry.RemoteRegressionBreakDistance - TetherBreakMargin, from, SetCues[set - 1].RemotePops),
        (HelloWorldRole.Local, false) => new Tether(Partner(role), P3Geometry.LocalRegressionBreakDistance + TetherBreakMargin, float.MaxValue, from, SetCues[set - 1].LocalPops),
        _ => null,
    };

    private Tether? RemoteTetherHasPopped(PartyRole role)
        => new Tether(Partner(role), P3Geometry.RemoteRegressionBreakDistance + TetherBreakMargin, float.MaxValue,
                      world.Events.Elapsed + RemotePopsWithin, world.Events.Elapsed + RemotePopsWithin);

    private Tether? LocalTetherHasPopped(int set, PartyRole role)
        => state.RoleIn(set, role) == HelloWorldRole.Local && !IsLastSet(set)
            ? new Tether(Partner(role), 0f, P3Geometry.LocalRegressionBreakDistance - TetherBreakMargin,
                         world.Events.Elapsed + LocalPopsWithin, world.Events.Elapsed + LocalPopsWithin)
            : null;

    private bool HasDoneItsPass(int set, PartyRole role) => (state.RoleIn(set, role), IsLastSet(set)) switch
    {
        (HelloWorldRole.Local, false) => HoldsItsRot(role, state.DefamationColour),
        (HelloWorldRole.Remote, false) => HoldsItsRot(role, state.StackColour),
        (HelloWorldRole.Stack, false) => HoldsItsRot(Side(set, HelloWorldRole.Remote, onRightSide[role]), state.StackColour),
        _ => true,
    };

    private bool HoldsItsRot(PartyRole role, HelloWorldColour colour) => !Member(role).IsAlive() || Member(role).HasStatus(colour.RotStatusId);

    private void NoteWhenTheRemoteTetherBroke(int set)
    {
        if (!remoteTetherBrokeAt.ContainsKey(set)
            && state.WithRole(set, HelloWorldRole.Remote).All(role => !Member(role).IsAlive() || !Member(role).HasStatus(P3StatusId.RemoteRegression)))
            remoteTetherBrokeAt[set] = world.Events.Elapsed;
    }

    private bool RemoteVulnHasWornOff(int set)
        => remoteTetherBrokeAt.TryGetValue(set, out var brokeAt) && world.Events.Elapsed >= brokeAt + RemotePopVulnLasts;

    private bool IsTheHuman(PartyRole role) => role == world.Party.PlayerRole;

    private void SendOnce(int set, HashSet<(int Set, PartyRole Role)> sent, Func<PartyRole, bool> ready, Func<PartyRole, Vector2?> destination,
                          float deadline, Func<PartyRole, Tether?> tether)
    {
        var going = PerRole.All.Where(role => !sent.Contains((set, role)) && destination(role) is not null && ready(role)).ToList();
        if (going.Count == 0) return;
        sent.UnionWith(going.Select(role => (set, role)));
        Send(set, role => going.Contains(role) ? destination(role) : null, deadline, tether);
    }

    private PartyRole Partner(PartyRole role) => state.WithKit(state.KitOf(role)).Single(r => r != role);

    private bool RotHasGoneOff(int set, HelloWorldRole holderRole, bool right)
    {
        var holder = world.Party.Get(Side(set, holderRole, right));
        var colour = holderRole == HelloWorldRole.Defamation ? state.DefamationColour : state.StackColour;
        return !holder.IsAlive() || holder!.HasStatus(colour.DebuggerStatusId);
    }

    private bool SoakersAreBackInTheirOwnTowerAsItGoesOff(PartyRole role, Vector2 spot, float until)
    {
        var set = towersUp;
        if (set == 0 || !IsSoaker(set, role)) return true;
        var resolve = SetCues[set - 1].Resolve;
        if (world.Events.Elapsed > resolve || until < resolve - BackInTheTowerAhead) return true;
        var soaked = TowerCentreOf(set, role);
        return TowerCentres(set).All(centre => centre == soaked
            ? Vector2.Distance(spot, centre) <= TowerRadius - TowerEdgeMargin
            : Vector2.Distance(spot, centre) >= TowerRadius + TowerEdgeMargin);
    }

    private IEnumerable<Vector2> TowerCentres(int set)
        => [OnLayout(set, DefamationColourTowers.L), OnLayout(set, DefamationColourTowers.R), OnLayout(set, StackColourTowers.L), OnLayout(set, StackColourTowers.R)];

    private Vector2 TowerCentreOf(int set, PartyRole role)
        => At(set, role, state.RoleIn(set, role) == HelloWorldRole.Defamation ? DefamationColourTowers : StackColourTowers);

    private void TakeTheClosestSideOfEachPair(int set)
    {
        foreach (var kit in Enum.GetValues<HelloWorldKit>())
        {
            var pair = state.WithKit(kit).OrderBy(role => (int)role).ToArray();
            var spots = SpotsOf(set, state.RoleIn(set, pair[0]));
            var first = Flat(Member(pair[0]).Position);
            var second = Flat(Member(pair[1]).Position);
            var left = OnLayout(set, spots.L);
            var right = OnLayout(set, spots.R);
            var keep = Vector2.Distance(first, left) + Vector2.Distance(second, right);
            var swap = Vector2.Distance(first, right) + Vector2.Distance(second, left);
            var higherPriorityTakesRight = swap < keep - SummedWalksThisCloseAreATie;
            onRightSide[pair[0]] = higherPriorityTakesRight;
            onRightSide[pair[1]] = !higherPriorityTakesRight;
        }
    }

    private static Spots SpotsOf(int set, HelloWorldRole role) => role switch
    {
        HelloWorldRole.Defamation => DefamationOuterTowerEdge,
        HelloWorldRole.Stack => StackInnerTowerEdge,
        HelloWorldRole.Remote => RemoteInsideStack,
        _ => IsLastSet(set) ? LastLocalInsideStack : LocalStretchedPastDefamation,
    };

    private void Send(int set, Func<PartyRole, Vector2?> destination, float deadline, Func<PartyRole, Tether?>? tether = null,
                      Func<PartyRole, PartyRole?>? passesWith = null)
    {
        var orders = new Dictionary<PartyRole, Order>();
        foreach (var role in PerRole.All)
            if (destination(role) is { } spot)
                orders[role] = new Order(spot, set == 0 ? Lane.Middle : LaneOf(set, role), tether?.Invoke(role), passesWith?.Invoke(role));
        traffic.Send(orders, deadline);
    }

    private Lane LaneOf(int set, PartyRole role) => state.RoleIn(set, role) switch
    {
        HelloWorldRole.Defamation => Lane.DefamationRing,
        HelloWorldRole.Stack => Lane.StackRing,
        _ => Lane.Middle,
    };

    private PartyRole Side(int set, HelloWorldRole role, bool right)
        => state.WithRole(set, role).Single(r => onRightSide[r] == right);

    private Vector2 At(int set, PartyRole role, Spots spots) => OnLayout(set, onRightSide[role] ? spots.R : spots.L);

    private Vector2 OnLayout(int set, Polar spot)
        => OnRing(state.Layout(set).RadiansFromNorth * 180f / MathF.PI + spot.Degrees, spot.Radius);

    private static Vector2 OnRing(float bearingDegrees, float radius)
    {
        var radians = bearingDegrees * MathF.PI / 180f;
        return new Vector2(radius * MathF.Sin(radians), -radius * MathF.Cos(radians));
    }

    private static bool IsLastSet(int set) => set == TopP3HelloWorldState.Sets;

    private SimCharacter Member(PartyRole role) => world.Party.Get(role)!;

    private static Vector2 Flat(Vector3 position) => new(position.X, position.Z);
}
