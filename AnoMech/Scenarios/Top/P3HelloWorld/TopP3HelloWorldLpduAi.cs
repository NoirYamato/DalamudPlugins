using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.Game.Party.PartyRole;

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

    private const float SummedWalksThisCloseAreATie = 0.5f;
    private const float CardinalCrossRadius = 10f;
    private static readonly Spots NextDefamationCross = new(new(-45f, CardinalCrossRadius), new(135f, CardinalCrossRadius));
    private static readonly Spots NextStackCross = new(new(-135f, CardinalCrossRadius), new(45f, CardinalCrossRadius));

    private static readonly PartyRole[] MonitorsLineUpNorthToSouth =
        [RegenHealer, ShieldHealer, MainTank, OffTank, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps];

    private static readonly Dictionary<PartyRole, float> LooseSpreadBearings = new()
    {
        [MainTank] = 0f, [PhysRangedDps] = 45f, [ShieldHealer] = 90f, [MeleeDpsB] = 135f,
        [OffTank] = 180f, [MeleeDpsA] = 225f, [RegenHealer] = 270f, [CasterDps] = 315f,
    };

    private TopP3HelloWorldState state = null!;
    private SimWorld world = null!;
    private readonly Dictionary<PartyRole, bool> onRightSide = new();

    public void Run(TopP3HelloWorldState s, SimWorld w)
    {
        state = s;
        world = w;
        onRightSide.Clear();
        var ai = new AiManager(world);
        ai.Move(0.5f, LooseSpread);
        ai.Move(5f, SplitIntoCardinalCrossByDebuffs);
        ai.Move(5f, ToUnderTheBoss(PairsWithoutRots), jitter: 0f);
        for (var set = 1; set <= TopP3HelloWorldState.Sets; set++)
        {
            var n = set;
            var start = SetStarts[n - 1];
            ai.Move(start + 1f, () => SoakersTakeTheirTowers(n));
            ai.Move(start + 5f, () => TetheredPairsFanOutFromUnderTheBoss(n));
            ai.Move(start + 10.3f, () => PassRotsOnOrStartSpreading(n));
            ai.Move(start + 12.3f, () => PopRemoteTetherAndSpreadOldRots(n));
            ai.Move(start + 15f, () => PopLocalTetherClearOfExplodingRots(n));
            if (n == TopP3HelloWorldState.Sets) continue;
            ai.Move(start + 16.6f, () => NextDefamationOnTheRightCrossesToTheStackSide(n));
            ai.Move(start + 17.4f, () => NextHoldersOnTheLeftTakeTheCardinalCross(n));
            ai.Move(start + 17.4f, ToUnderTheBoss(() => ExplodedRotsExceptTheRightStack(n)), jitter: 0f);
            ai.Move(start + 18.3f, () => NextStackOnTheRightCrossesToTheDefamationSide(n));
            ai.Move(start + 18.6f, ToUnderTheBoss(() => [Side(n, HelloWorldRole.Stack, right: true)]), jitter: 0f);
        }
        ai.Move(100f, LineUpForMonitors);
    }

    private IAiMove LooseSpread()
        => ByRole(role => OnRing(LooseSpreadBearings[role], 8.5f));

    private IAiMove SplitIntoCardinalCrossByDebuffs()
    {
        var defamations = state.WithKit(HelloWorldKit.Defamation);
        var stacks = state.WithKit(HelloWorldKit.Stack);
        var defamationAxis = new[] { 0f, 90f }.MinBy(axis => Math.Max(LongestWalkOntoAxis(defamations, axis), LongestWalkOntoAxis(stacks, axis + 90f)));
        var destinations = new Dictionary<PartyRole, Vector2>();
        PlaceOntoAxisEnds(defamations, defamationAxis, destinations);
        PlaceOntoAxisEnds(stacks, defamationAxis + 90f, destinations);
        return ByRole(role => destinations.TryGetValue(role, out var spot) ? spot : null);
    }

    private IReadOnlyList<PartyRole> PairsWithoutRots()
        => [.. state.WithKit(HelloWorldKit.RemoteFirst), .. state.WithKit(HelloWorldKit.LocalFirst)];

    private IAiMove SoakersTakeTheirTowers(int set)
    {
        TakeTheClosestSideOfEachPair(set);
        return ByRole(role => state.RoleIn(set, role) is HelloWorldRole.Defamation or HelloWorldRole.Stack
            ? ASpot(set, role)
            : null);
    }

    private IAiMove TetheredPairsFanOutFromUnderTheBoss(int set)
        => ByRole(role => state.RoleIn(set, role) is HelloWorldRole.Remote or HelloWorldRole.Local
            ? ASpot(set, role)
            : null);

    private Vector2 ASpot(int set, PartyRole role) => state.RoleIn(set, role) switch
    {
        HelloWorldRole.Defamation => At(set, role, DefamationOuterTowerEdge),
        HelloWorldRole.Stack => At(set, role, StackInnerTowerEdge),
        HelloWorldRole.Remote => At(set, role, RemoteInsideStack),
        _ => At(set, role, IsLastSet(set) ? LastLocalInsideStack : LocalStretchedPastDefamation),
    };

    private IAiMove PassRotsOnOrStartSpreading(int set)
        => ByRole(role => state.RoleIn(set, role) switch
        {
            HelloWorldRole.Defamation => At(set, role, DefamationTowerCentre),
            HelloWorldRole.Local when !IsLastSet(set) => At(set, role, DefamationTowerCentre),
            HelloWorldRole.Remote when !IsLastSet(set) => Flat(SameSide(set, HelloWorldRole.Stack, role).Position),
            HelloWorldRole.Stack when IsLastSet(set) => At(set, role, LastStackStartingToSpread),
            _ => null,
        });

    private IAiMove PopRemoteTetherAndSpreadOldRots(int set)
        => ByRole(role => (state.RoleIn(set, role), IsLastSet(set)) switch
        {
            (HelloWorldRole.Remote, false) => At(set, role, RemotePopped),
            (HelloWorldRole.Stack, false) => At(set, role, StackSpreadingRot),
            (HelloWorldRole.Local, false) => At(set, role, LocalWaitingForVulnsToFade),
            (HelloWorldRole.Remote, true) => At(set, role, LastRemotePopped),
            _ => null,
        });

    private IAiMove PopLocalTetherClearOfExplodingRots(int set)
        => ByRole(role => (state.RoleIn(set, role), IsLastSet(set)) switch
        {
            (HelloWorldRole.Local, false) => At(set, role, LocalPopped),
            (HelloWorldRole.Remote, false) => At(set, role, RemoteBackInStackLane),
            (HelloWorldRole.Stack, true) => At(set, role, LastStackSpreadRot),
            (HelloWorldRole.Remote, true) => At(set, role, LastRemoteWaiting),
            (HelloWorldRole.Local, true) => At(set, role, LastLocalWaiting),
            _ => null,
        });

    private IAiMove NextDefamationOnTheRightCrossesToTheStackSide(int set)
        => ByRole(role => state.RoleIn(set, role) == HelloWorldRole.Local && onRightSide[role]
            ? At(set, role, NextDefamationCross)
            : null);

    private IAiMove NextHoldersOnTheLeftTakeTheCardinalCross(int set)
        => ByRole(role => (state.RoleIn(set, role), onRightSide[role]) switch
        {
            (HelloWorldRole.Local, false) => At(set, role, NextDefamationCross),
            (HelloWorldRole.Remote, false) => At(set, role, NextStackCross),
            _ => null,
        });

    private IReadOnlyList<PartyRole> ExplodedRotsExceptTheRightStack(int set)
        => PerRole.All.Where(role => state.RoleIn(set, role) == HelloWorldRole.Defamation
                                     || (state.RoleIn(set, role) == HelloWorldRole.Stack && !onRightSide[role]))
                      .ToList();

    private IAiMove NextStackOnTheRightCrossesToTheDefamationSide(int set)
        => ByRole(role => state.RoleIn(set, role) == HelloWorldRole.Remote && onRightSide[role]
            ? At(set, role, NextStackCross)
            : null);

    private IAiMove LineUpForMonitors()
        => ByRole(role => OnRing(320f - 15.1f * Array.IndexOf(MonitorsLineUpNorthToSouth, role), 13.15f));

    private Func<IAiMove> ToUnderTheBoss(Func<IReadOnlyList<PartyRole>> who)
        => () =>
        {
            var roles = who();
            return ByRole(role => roles.Contains(role) ? Vector2.Zero : null);
        };

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

    private Spots SpotsOf(int set, HelloWorldRole role) => role switch
    {
        HelloWorldRole.Defamation => DefamationOuterTowerEdge,
        HelloWorldRole.Stack => StackInnerTowerEdge,
        HelloWorldRole.Remote => RemoteInsideStack,
        _ => IsLastSet(set) ? LastLocalInsideStack : LocalStretchedPastDefamation,
    };

    private float LongestWalkOntoAxis(IReadOnlyList<PartyRole> pair, float axis)
    {
        var ends = AxisEnds(axis);
        var first = Flat(Member(pair[0]).Position);
        var second = Flat(Member(pair[1]).Position);
        return Math.Min(Math.Max(Vector2.Distance(first, ends[0]), Vector2.Distance(second, ends[1])),
                        Math.Max(Vector2.Distance(first, ends[1]), Vector2.Distance(second, ends[0])));
    }

    private void PlaceOntoAxisEnds(IReadOnlyList<PartyRole> pair, float axis, Dictionary<PartyRole, Vector2> destinations)
    {
        var ends = AxisEnds(axis);
        var first = Flat(Member(pair[0]).Position);
        var second = Flat(Member(pair[1]).Position);
        var keep = Math.Max(Vector2.Distance(first, ends[0]), Vector2.Distance(second, ends[1]));
        var swap = Math.Max(Vector2.Distance(first, ends[1]), Vector2.Distance(second, ends[0]));
        destinations[pair[0]] = keep <= swap ? ends[0] : ends[1];
        destinations[pair[1]] = keep <= swap ? ends[1] : ends[0];
    }

    private static Vector2[] AxisEnds(float axis) => [OnRing(axis, CardinalCrossRadius), OnRing(axis + 180f, CardinalCrossRadius)];

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

    private static IAiMove ByRole(Func<PartyRole, Vector2?> destination)
        => AiMove.Create(PerRole.All.Select(destination).ToArray()).NaturalOrder();
}
