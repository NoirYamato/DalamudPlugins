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

    private static readonly float[] SetStarts = [19.1f, 40.1f, 61.2f, 82.5f];
    private static readonly float[] RegressionStarts = [28f, 49f, 70f, 91f];
    private static readonly float[] Resolves = [29.1f, 50.1f, 71.2f, 92.5f];

    private static readonly Spots DefamationOuterTowerEdge = Spots.Mirrored(45f, 18.1f);
    private static readonly Spots DefamationTowerCentre = Spots.Mirrored(45f, 13.7f);
    private static readonly Spots StackInnerTowerEdge = Spots.Mirrored(154f, 12f);
    private static readonly Spots RemoteInsideStack = Spots.Mirrored(166f, 11.4f);
    private static readonly Spots LocalStretchedPastDefamation = Spots.Mirrored(74f, 14.2f);
    private static readonly Spots LastLocalInsideStack = new(new(-165f, 10.5f), new(165f, 11.2f));

    private static readonly Spots RemotePopped = new(new(-141f, 17f), new(149f, 11.6f));
    private static readonly Spots StackSpreadingRot = new(new(-120f, 10f), new(112f, 10f));
    private static readonly Spots LocalWaitingForVulnsToFade = Spots.Mirrored(42f, 7.7f);
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
    private const float TowerEdgeMargin = 0.2f;

    private const float SummedWalksThisCloseAreATie = 0.5f;
    private const float SoakerInPlace = 1f;
    private const float PollSeconds = 0.1f;
    private const float LastLineUpPoll = 105f;
    private const float ArriveBeforeTethersGoLive = 0.3f;
    private const float TetherBreakMargin = 0.3f;
    private const float TowersAt = 1f, TetheredPairsLatestAt = 5f, PassAt = 10.3f, RemotePopsAt = 12.3f, LocalPopsAt = 15f;
    private const float RemoteHasPoppedBy = 14f, LocalHasPoppedBy = 17.5f;

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
    private int towersUp;
    private bool linedUp;

    public void Run(TopP3HelloWorldState s, SimWorld w)
    {
        state = s;
        world = w;
        traffic = new TopP3HelloWorldTraffic(world, SoakersStayInTheirOwnTowerOnly);
        onRightSide.Clear();
        stretchedSets.Clear();
        towersUp = 0;
        linedUp = false;
        world.Events.Add(0.5f, LooseSpread);
        for (var set = 1; set <= TopP3HelloWorldState.Sets; set++)
        {
            var n = set;
            var start = SetStarts[n - 1];
            world.Events.Add(start + TowersAt, () => SoakersTakeTheirTowers(n));
            for (var t = start + TowersAt + PollSeconds; t < start + TetheredPairsLatestAt; t += PollSeconds)
                world.Events.Add(t, () => TetheredPairsStretchOnceTheSoakersAreInPlace(n));
            world.Events.Add(start + TetheredPairsLatestAt, () => TetheredPairsStretch(n));
            world.Events.Add(start + PassAt, () => PassRotsOnOrStartSpreading(n));
            world.Events.Add(start + RemotePopsAt, () => PopRemoteTetherAndSpreadOldRots(n));
            world.Events.Add(start + LocalPopsAt, () => PopLocalTetherClearOfExplodingRots(n));
        }
        for (var t = 100f; t < LastLineUpPoll; t += PollSeconds)
            world.Events.Add(t, LineUpForMonitorsOnceTheLastRotsHaveGoneOff);
    }

    private void LooseSpread()
        => Send(0, role => OnRing(LooseSpreadBearings[role], 8.5f), deadline: 8f);

    private void SoakersTakeTheirTowers(int set)
    {
        towersUp = set;
        TakeTheClosestSideOfEachPair(set);
        Send(set, role => IsSoaker(set, role) ? ASpot(set, role) : null, deadline: SetStarts[set - 1] + 8f);
    }

    private void TetheredPairsStretchOnceTheSoakersAreInPlace(int set)
    {
        if (SoakersAreInPlace(set)) TetheredPairsStretch(set);
    }

    private void TetheredPairsStretch(int set)
    {
        if (!stretchedSets.Add(set)) return;
        Send(set, role => IsSoaker(set, role) ? null : ASpot(set, role), deadline: RegressionStarts[set - 1] - ArriveBeforeTethersGoLive,
             tether: role => TetherHoldsUntilItsPop(set, role, from: RegressionStarts[set - 1]));
    }

    private bool SoakersAreInPlace(int set)
        => PerRole.All.Where(role => IsSoaker(set, role) && Member(role).IsAlive())
                      .All(role => Vector2.Distance(Flat(Member(role).Position), ASpot(set, role)) <= SoakerInPlace);

    private bool IsSoaker(int set, PartyRole role) => state.RoleIn(set, role) is HelloWorldRole.Defamation or HelloWorldRole.Stack;

    private Vector2 ASpot(int set, PartyRole role) => At(set, role, SpotsOf(set, state.RoleIn(set, role)));

    private void PassRotsOnOrStartSpreading(int set)
        => Send(set, role => state.RoleIn(set, role) switch
        {
            HelloWorldRole.Defamation => At(set, role, DefamationTowerCentre),
            HelloWorldRole.Local when !IsLastSet(set) => At(set, role, DefamationTowerCentre),
            HelloWorldRole.Remote when !IsLastSet(set) => Flat(SameSide(set, HelloWorldRole.Stack, role).Position),
            HelloWorldRole.Stack when IsLastSet(set) => At(set, role, LastStackStartingToSpread),
            _ => null,
        }, deadline: SetStarts[set - 1] + 12f, tether: role => TetherHoldsUntilItsPop(set, role, from: SetStarts[set - 1] + PassAt));

    private void PopRemoteTetherAndSpreadOldRots(int set)
        => Send(set, role => (state.RoleIn(set, role), IsLastSet(set)) switch
        {
            (HelloWorldRole.Remote, false) => At(set, role, RemotePopped),
            (HelloWorldRole.Stack, false) => At(set, role, StackSpreadingRot),
            (HelloWorldRole.Local, false) => At(set, role, LocalWaitingForVulnsToFade),
            (HelloWorldRole.Remote, true) => At(set, role, LastRemotePopped),
            _ => null,
        }, deadline: SetStarts[set - 1] + 14.9f, tether: role => state.RoleIn(set, role) == HelloWorldRole.Remote
            ? RemoteTetherHasPopped(set, role)
            : TetherHoldsUntilItsPop(set, role, from: SetStarts[set - 1] + RemotePopsAt));

    private void PopLocalTetherClearOfExplodingRots(int set)
        => Send(set, role => (state.RoleIn(set, role), IsLastSet(set)) switch
        {
            (HelloWorldRole.Local, false) => At(set, role, LocalPopped),
            (HelloWorldRole.Remote, false) => At(set, role, RemoteBackInStackLane),
            (HelloWorldRole.Stack, true) => At(set, role, LastStackSpreadRot),
            (HelloWorldRole.Remote, true) => At(set, role, LastRemoteWaiting),
            (HelloWorldRole.Local, true) => At(set, role, LastLocalWaiting),
            _ => null,
        }, deadline: SetStarts[set - 1] + 17f, tether: role => LocalTetherHasPopped(set, role));

    private void LineUpForMonitorsOnceTheLastRotsHaveGoneOff()
    {
        var last = TopP3HelloWorldState.Sets;
        if (linedUp || !new[] { false, true }.All(right => RotHasGoneOff(last, HelloWorldRole.Defamation, right)
                                                          && RotHasGoneOff(last, HelloWorldRole.Stack, right)))
            return;
        linedUp = true;
        Send(0, role => OnRing(320f - 15.1f * Array.IndexOf(MonitorsLineUpNorthToSouth, role), 13.15f), deadline: LastLineUpPoll);
    }

    private Tether? TetherHoldsUntilItsPop(int set, PartyRole role, float from) => (state.RoleIn(set, role), IsLastSet(set)) switch
    {
        (HelloWorldRole.Remote, _) => new Tether(Partner(role), 0f, P3Geometry.RemoteRegressionBreakDistance - TetherBreakMargin, from, SetStarts[set - 1] + RemotePopsAt),
        (HelloWorldRole.Local, false) => new Tether(Partner(role), P3Geometry.LocalRegressionBreakDistance + TetherBreakMargin, float.MaxValue, from, SetStarts[set - 1] + LocalPopsAt),
        _ => null,
    };

    private Tether? RemoteTetherHasPopped(int set, PartyRole role)
        => new Tether(Partner(role), P3Geometry.RemoteRegressionBreakDistance + TetherBreakMargin, float.MaxValue,
                      SetStarts[set - 1] + RemoteHasPoppedBy, SetStarts[set - 1] + RemoteHasPoppedBy);

    private Tether? LocalTetherHasPopped(int set, PartyRole role)
        => state.RoleIn(set, role) == HelloWorldRole.Local && !IsLastSet(set)
            ? new Tether(Partner(role), 0f, P3Geometry.LocalRegressionBreakDistance - TetherBreakMargin,
                         SetStarts[set - 1] + LocalHasPoppedBy, SetStarts[set - 1] + LocalHasPoppedBy)
            : null;

    private PartyRole Partner(PartyRole role) => state.WithKit(state.KitOf(role)).Single(r => r != role);

    private bool RotHasGoneOff(int set, HelloWorldRole holderRole, bool right)
    {
        var holder = world.Party.Get(Side(set, holderRole, right));
        var colour = holderRole == HelloWorldRole.Defamation ? state.DefamationColour : state.StackColour;
        return !holder.IsAlive() || holder!.HasStatus(colour.DebuggerStatusId);
    }

    private bool SoakersStayInTheirOwnTowerOnly(PartyRole role, Vector2 spot)
    {
        var set = towersUp;
        if (set == 0 || world.Events.Elapsed > Resolves[set - 1] || !IsSoaker(set, role)) return true;
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

    private void Send(int set, Func<PartyRole, Vector2?> destination, float deadline, Func<PartyRole, Tether?>? tether = null)
    {
        var orders = new Dictionary<PartyRole, Order>();
        foreach (var role in PerRole.All)
            if (destination(role) is { } spot)
                orders[role] = new Order(spot, set == 0 ? Lane.Middle : LaneOf(set, role), tether?.Invoke(role));
        traffic.Send(orders, deadline);
    }

    private Lane LaneOf(int set, PartyRole role) => state.RoleIn(set, role) switch
    {
        HelloWorldRole.Defamation => Lane.DefamationRing,
        HelloWorldRole.Stack => Lane.StackRing,
        _ => Lane.Middle,
    };

    private SimCharacter SameSide(int set, HelloWorldRole role, PartyRole of)
        => Member(Side(set, role, onRightSide[of]));

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
