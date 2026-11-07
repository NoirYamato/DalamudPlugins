using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;

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

    private TopP5OmegaState state = null!;
    private PartyRole[] firstHelloWorldOrder = [];

    public void Run(TopP5OmegaState s, SimWorld world)
    {
        state = s;
        firstHelloWorldOrder = FirstHelloWorldOrder(s);
        var ai = new AiManager(world);
        ai.Move(0f, TanksNorthOfOmegaTheRestSouth);
        ai.Move(20f, StackOnTheFirstOmegaPairsSafeSpot, arrivalTime: 24f);
        ai.Move(24f, StackOnTheSecondOmegaPairsSafeSpot, arrivalTime: 28f);
        ai.Automarker(28f, () => MarkInHelloWorldOrder(firstHelloWorldOrder));
        ai.Move(32f, FirstHelloWorldSpotsOnTheMonitorsSide, jitter: 0.1f, arrivalTime: 41f);
        ai.Automarker(47f, () => MarkInHelloWorldOrder(state.HelloWorld2));
        ai.Move(48f, GatherInTheMiddleWithTheTetherTakersTowardTheBeetle, jitter: 0f);
        ai.Move(53f, SecondHelloWorldSpotsTurnedWithTheBeetle, arrivalTime: 57f);
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

    private void TurnWithTheBeetle(IAiPositions move) => state.BettleSpawnDirection.Apply(move);
}
