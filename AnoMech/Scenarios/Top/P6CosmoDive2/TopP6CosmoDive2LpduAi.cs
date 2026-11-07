using System;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Scenarios.Top.P6CosmoDive2;

public sealed class TopP6CosmoDive2LpduAi : IScenarioAi<TopP6CosmoDive2State>
{
    public string Name => "[LPDU] Light PF";
    public string? Group => "EU";

    internal const float DodgeRadius = 14f;
    internal const float StartPointRadius = 14.5f;
    internal const float DiveTankRadius = 6.5f;
    internal const float MainTankDegreesPastTheParty = 135f;
    internal const float OffTankDegreesPastTheParty = 225f;

    internal static readonly Vector2 MainTankInsideNorth = new(0f, -5f);
    internal static readonly Vector2 MainTankMakingRoomForTheSecondMelee = new(0f, -7f);
    internal static readonly Vector2 OffTankOutsideEast = new(14.5f, 0f);
    internal static readonly Vector2 PartyInsideSouth = new(0f, 6f);
    internal static readonly Vector2 MainTankOutsideNorth = new(0f, -14f);
    internal static readonly Vector2 PartyOutsideSouth = new(0f, 14f);
    internal static readonly Vector2 Middle = Vector2.Zero;

    private TopP6CosmoDive2State state = null!;
    private SimWorld world = null!;

    public void Run(TopP6CosmoDive2State s, SimWorld w)
    {
        state = s;
        world = w;
        var ai = new AiManager(world);
        ai.Move(0.5f, MainTankInsideNorthOffTankOutsideEastPartyInsideSouth);
        ai.Move(2f, StepOutOfTheHitboxToSeeTheArrows);
        ai.Move(7.5f, StackInTheMiddleToDropTheFirstPuddle);
        ai.Move(12.9f, ToThePointBesideTheFirstExaflareAwayFromTheSecond);
        ai.Move(16.2f, () => FollowOntoThePointOfExaflare(0));
        ai.Move(17.9f, () => FollowOntoThePointOfExaflare(1));
        ai.Move(19.8f, () => FollowOntoThePointOfExaflare(2));
        ai.Move(21.5f, PartyHoldsOnTheLastExaflaresPointTanksCrossTheBossIntoAY);
        world.Events.Add(25.3f, FirstMeleeLimitBreakRightAfterTheCosmoDiveBar);
        ai.Move(28f, BackForTheAutosTheSecondMeleeInTheMiddleTheMainTankMakingRoom);
        world.Events.Add(32f, SecondMeleeLimitBreakFromTheMiddleOnceTheGaugeRefills);
    }

    internal static float BearingOfExaflare(TopP6CosmoDive2State state, int exaflare)
        => 45f * (state.FirstExaflare + (state.Clockwise ? exaflare : -exaflare));

    internal static Vector2 PointBesideTheFirstExaflareAwayFromTheSecond(TopP6CosmoDive2State state)
        => OnRing(BearingOfExaflare(state, -1), StartPointRadius);

    internal static Vector2 PointOfExaflare(TopP6CosmoDive2State state, int exaflare)
        => OnRing(BearingOfExaflare(state, exaflare), DodgeRadius);

    internal static Vector2 PartyDiveHold(TopP6CosmoDive2State state) => PointOfExaflare(state, 3);

    internal static Vector2 MainTankDiveSpot(TopP6CosmoDive2State state)
        => OnRing(BearingOfExaflare(state, 3) + MainTankDegreesPastTheParty, DiveTankRadius);

    internal static Vector2 OffTankDiveSpot(TopP6CosmoDive2State state)
        => OnRing(BearingOfExaflare(state, 3) + OffTankDegreesPastTheParty, DiveTankRadius);

    private static IAiMove MainTankInsideNorthOffTankOutsideEastPartyInsideSouth()
        => TanksAndParty(MainTankInsideNorth, OffTankOutsideEast, PartyInsideSouth);

    private static IAiMove StepOutOfTheHitboxToSeeTheArrows()
        => TanksAndParty(MainTankOutsideNorth, OffTankOutsideEast, PartyOutsideSouth);

    private static IAiMove StackInTheMiddleToDropTheFirstPuddle() => Everyone(Middle);

    private IAiMove ToThePointBesideTheFirstExaflareAwayFromTheSecond() => Everyone(PointBesideTheFirstExaflareAwayFromTheSecond(state));

    private IAiMove FollowOntoThePointOfExaflare(int exaflare) => Everyone(PointOfExaflare(state, exaflare));

    private IAiMove PartyHoldsOnTheLastExaflaresPointTanksCrossTheBossIntoAY()
        => TanksAndParty(MainTankDiveSpot(state), OffTankDiveSpot(state), PartyDiveHold(state));

    private static IAiMove BackForTheAutosTheSecondMeleeInTheMiddleTheMainTankMakingRoom()
        => ByRole(role => role switch
        {
            MainTank => MainTankMakingRoomForTheSecondMelee,
            OffTank => OffTankOutsideEast,
            MeleeDpsB => Middle,
            _ => PartyInsideSouth,
        });

    private void FirstMeleeLimitBreakRightAfterTheCosmoDiveBar() => TopP6LimitBreaks.LimitBreak3(world, MeleeDpsA);

    private void SecondMeleeLimitBreakFromTheMiddleOnceTheGaugeRefills() => TopP6LimitBreaks.LimitBreak3(world, MeleeDpsB);

    private static IAiMove Everyone(Vector2 spot) => ByRole(_ => spot);

    private static IAiMove TanksAndParty(Vector2 mainTank, Vector2 offTank, Vector2 party)
        => ByRole(role => role switch { MainTank => mainTank, OffTank => offTank, _ => party });

    private static IAiMove ByRole(Func<PartyRole, Vector2> spotOf)
        => AiMove.Create(PerRole.All.Select(role => (Vector2?)spotOf(role)).ToArray()).Assignments(PerRole.All);

    private static Vector2 OnRing(float bearingDegrees, float radius)
    {
        var radians = bearingDegrees * MathF.PI / 180f;
        return new Vector2(radius * MathF.Sin(radians), -radius * MathF.Cos(radians));
    }
}
