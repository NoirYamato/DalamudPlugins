using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Scenarios.Top.P6WaveCannon1;

public sealed class TopP6WaveCannon1LpduAi : IScenarioAi<TopP6WaveCannon1State>
{
    public string Name => "[LPDU] Light PF";
    public string? Group => "EU";

    internal const float CompassStepDegrees = 45f;
    internal const float ExaflarePathRadius = 14f;
    internal const float InsideTheLastPuddlesRadius = 4f;
    internal const float SpreadRadius = 10.5f;

    internal const int BesideTheFirstExaflareAwayFromTheSecond = -1;
    internal const int TheFirstExaflaresPoint = 0;
    internal const int TheSecondExaflaresPoint = 1;
    internal const int TheThirdExaflaresPoint = 2;

    internal static readonly Vector2 MainTankAutoSpot = new(0f, -5f);
    internal static readonly Vector2 PartyAutoSpot = new(0f, 6f);
    internal static readonly Vector2 OffTankAutoSpot = new(14.5f, 0f);
    internal static readonly Vector2 OutsideTheHitboxSouth = new(0f, 14f);
    internal static readonly Vector2 TheMiddle = Vector2.Zero;
    internal static readonly Vector2 TanksFrontOfTheLine = new(0f, 6.5f);
    internal static readonly Vector2 PartyBehindTheTanks = new(0f, 10.5f);

    internal static readonly IReadOnlyDictionary<PartyRole, float> WaymarkBearing = new Dictionary<PartyRole, float>
    {
        [MainTank] = 0f, [CasterDps] = 45f, [OffTank] = 90f, [MeleeDpsB] = 135f,
        [ShieldHealer] = 180f, [MeleeDpsA] = 225f, [RegenHealer] = 270f, [PhysRangedDps] = 315f,
    };

    private TopP6WaveCannon1State state = null!;

    public void Run(TopP6WaveCannon1State s, SimWorld world)
    {
        state = s;
        var ai = new AiManager(world);
        ai.Move(0f, EveryoneOnTheirAutoSpot);
        ai.Move(2f, StepOutOfTheHitboxSouthToReadTheArrows);
        ai.Move(8f, StackInTheMiddleForTheFirstPuddle);
        ai.Move(12.9f, () => EveryoneOnTheExaflarePath(BesideTheFirstExaflareAwayFromTheSecond));
        ai.Move(16.2f, () => EveryoneOnTheExaflarePath(TheFirstExaflaresPoint));
        ai.Move(17.9f, () => EveryoneOnTheExaflarePath(TheSecondExaflaresPoint));
        ai.Move(19.8f, () => EveryoneOnTheExaflarePath(TheThirdExaflaresPoint));
        ai.Move(21.6f, InTowardTheirWaymarkClearOfTheLastPuddles);
        ai.Move(24.7f, OutToTheirWaveCannonSpreadOnceTheLastPuddleIsGone);
        ai.Move(30f, LineUpInFrontOfCWithTheTanksNearestTheBoss);
        ai.Move(36.5f, EveryoneOnTheirAutoSpot);
    }

    internal static Vector2 AutoSpot(PartyRole role)
        => role switch
        {
            MainTank => MainTankAutoSpot,
            OffTank => OffTankAutoSpot,
            _ => PartyAutoSpot,
        };

    internal static Vector2 ExaflarePathPoint(TopP6WaveCannon1State state, int pointsPastTheFirst)
        => OnRing(CompassStepDegrees * (state.FirstExaflare + (state.Clockwise ? 1 : -1) * pointsPastTheFirst), ExaflarePathRadius);

    internal static Vector2 InsideTheLastPuddles(PartyRole role) => OnRing(WaymarkBearing[role], InsideTheLastPuddlesRadius);

    internal static Vector2 SpreadSpot(PartyRole role) => OnRing(WaymarkBearing[role], SpreadRadius);

    internal static Vector2 WildChargeLineSpot(PartyRole role) => role.IsTank() ? TanksFrontOfTheLine : PartyBehindTheTanks;

    private static IAiMove EveryoneOnTheirAutoSpot() => Everyone(AutoSpot);

    private static IAiMove StepOutOfTheHitboxSouthToReadTheArrows() => Everyone(_ => OutsideTheHitboxSouth);

    private static IAiMove StackInTheMiddleForTheFirstPuddle() => Everyone(_ => TheMiddle);

    private IAiMove EveryoneOnTheExaflarePath(int pointsPastTheFirst) => Everyone(_ => ExaflarePathPoint(state, pointsPastTheFirst));

    private static IAiMove InTowardTheirWaymarkClearOfTheLastPuddles() => Everyone(InsideTheLastPuddles);

    private static IAiMove OutToTheirWaveCannonSpreadOnceTheLastPuddleIsGone() => Everyone(SpreadSpot);

    private static IAiMove LineUpInFrontOfCWithTheTanksNearestTheBoss() => Everyone(WildChargeLineSpot);

    private static IAiMove Everyone(Func<PartyRole, Vector2> spotOf)
        => AiMove.Create(PerRole.All.Select(role => (Vector2?)spotOf(role)).ToArray()).Assignments(PerRole.All);

    private static Vector2 OnRing(float bearingDegrees, float radius)
    {
        var radians = bearingDegrees * MathF.PI / 180f;
        return new Vector2(radius * MathF.Sin(radians), -radius * MathF.Cos(radians));
    }
}
