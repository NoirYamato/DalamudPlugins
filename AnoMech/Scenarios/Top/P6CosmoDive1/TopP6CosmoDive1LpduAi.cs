using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Scenarios.Top.P6CosmoDive1;

public sealed class TopP6CosmoDive1LpduAi : IScenarioAi<TopP6CosmoDive1State>
{
    public string Name => "[LPDU] Light PF";
    public string? Group => "EU";

    internal const float HalfwayThroughCosmoMemory = 4.9f;

    internal static readonly Vector2 InFrontOfWaymarkTwo = new(7.5f, 7.5f);
    internal static readonly Vector2 InTheCentreSquare = new(3f, 3f);
    internal static readonly Vector2 OutOnTheCorner = new(12f, 12f);
    internal static readonly Vector2 SideStepSouth = new(3f, 12f);
    internal static readonly Vector2 InFromTheSideStep = new(3f, 8f);
    internal static readonly Vector2 MainTankDiveSpotTowardWaymarkFour = new(-4.6f, -4.6f);
    internal static readonly Vector2 OffTankDiveSpotTowardWaymarkOne = new(4.6f, -4.6f);

    public void Run(TopP6CosmoDive1State state, SimWorld world)
    {
        var ai = new AiManager(world);
        ai.Move(0f, TopP6LpduAi.EveryoneOnTheirAutoSpot);
        world.Events.Add(HalfwayThroughCosmoMemory, () => TopP6LpduAi.LimitBreak3RefilledByBrilliantDynamis(world, MainTank));
        ai.Move(16.3f, EveryoneInFrontOfWaymarkTwo);
        if (state.InFirst)
        {
            ai.Move(24.1f, EveryoneIntoTheCentreSquare);
            ai.Move(28.4f, EveryoneInFrontOfWaymarkTwo);
            ai.Move(32.4f, EveryoneSideStepsSouth);
            ai.Move(34.4f, TheSixInFromTheSideStepAsTheTanksLeaveForTheirDiveSpots);
            ai.Move(36.4f, TheSixBackInFrontOfWaymarkTwo);
        }
        else
        {
            ai.Move(24.1f, EveryoneOutToTheCorner);
            ai.Move(28.4f, EveryoneInFrontOfWaymarkTwo);
            ai.Move(30.4f, EveryoneSideStepsSouth);
            ai.Move(32.4f, TheSixInFromTheSideStepAsTheTanksLeaveForTheirDiveSpots);
            ai.Move(34.4f, TheSixBackInFrontOfWaymarkTwo);
        }
        ai.Move(40f, TopP6LpduAi.EveryoneOnTheirAutoSpot);
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
}
