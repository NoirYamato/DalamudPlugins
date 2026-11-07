using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Scenarios.Top.P6CosmoDive2;

public sealed class TopP6CosmoDive2LpduAi : IScenarioAi<TopP6CosmoDive2State>
{
    public string Name => "[LPDU] Light PF";
    public string? Group => "EU";

    internal const int TheLastExaflare = 3;
    internal const float DiveTankRadius = 6.5f;
    internal const float MainTankDegreesPastTheParty = 135f;
    internal const float OffTankDegreesPastTheParty = 225f;

    internal static readonly Vector2 MainTankMakingRoomForTheSecondMelee = new(0f, -7f);
    internal static readonly Vector2 Middle = Vector2.Zero;

    private TopP6CosmoDive2State state = null!;
    private SimWorld world = null!;

    public void Run(TopP6CosmoDive2State s, SimWorld w)
    {
        state = s;
        world = w;
        var ai = new AiManager(world);
        ai.Move(0f, TopP6LpduAi.EveryoneOnTheirAutoSpot);
        TopP6LpduAi.StepOutOfTheHitboxToReadTheArrowsThenStackInTheMiddle(ai);
        TopP6LpduAi.FollowTheExaflaresRoundFromBesideTheFirst(ai, state.FirstExaflare, state.Clockwise);
        ai.Move(21.5f, PartyHoldsOnTheLastExaflaresPointTanksCrossTheBossIntoAY);
        world.Events.Add(25.3f, FirstMeleeLimitBreakRightAfterTheCosmoDiveBar);
        ai.Move(28f, BackForTheAutosTheSecondMeleeInTheMiddleTheMainTankMakingRoom);
        world.Events.Add(32f, SecondMeleeLimitBreakFromTheMiddleOnceTheGaugeRefills);
    }

    internal static Vector2 PartyDiveHold(TopP6CosmoDive2State state)
        => TopP6LpduAi.OnTheExaflarePath(state.FirstExaflare, state.Clockwise, TheLastExaflare);

    internal static Vector2 MainTankDiveSpot(TopP6CosmoDive2State state)
        => TopP6LpduAi.OnRing(BearingOfTheLastExaflare(state) + MainTankDegreesPastTheParty, DiveTankRadius);

    internal static Vector2 OffTankDiveSpot(TopP6CosmoDive2State state)
        => TopP6LpduAi.OnRing(BearingOfTheLastExaflare(state) + OffTankDegreesPastTheParty, DiveTankRadius);

    private static float BearingOfTheLastExaflare(TopP6CosmoDive2State state)
        => TopP6LpduAi.BearingOfExaflare(state.FirstExaflare, state.Clockwise, TheLastExaflare);

    private IAiMove PartyHoldsOnTheLastExaflaresPointTanksCrossTheBossIntoAY()
        => TanksAndParty(MainTankDiveSpot(state), OffTankDiveSpot(state), PartyDiveHold(state));

    private static IAiMove BackForTheAutosTheSecondMeleeInTheMiddleTheMainTankMakingRoom()
        => TopP6LpduAi.ByRole(role => role switch
        {
            MainTank => MainTankMakingRoomForTheSecondMelee,
            MeleeDpsB => Middle,
            _ => TopP6LpduAi.AutoSpot(role),
        });

    private void FirstMeleeLimitBreakRightAfterTheCosmoDiveBar() => TopP6LpduAi.LimitBreak3RefilledByBrilliantDynamis(world, MeleeDpsA);

    private void SecondMeleeLimitBreakFromTheMiddleOnceTheGaugeRefills() => TopP6LpduAi.LimitBreak3RefilledByBrilliantDynamis(world, MeleeDpsB);

    private static IAiMove TanksAndParty(Vector2 mainTank, Vector2 offTank, Vector2 party)
        => TopP6LpduAi.ByRole(role => role switch { MainTank => mainTank, OffTank => offTank, _ => party });
}
