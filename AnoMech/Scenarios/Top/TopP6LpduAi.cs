using System;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Scenarios.Top;

internal static class TopP6LpduAi
{
    internal static readonly Vector2 MainTankInsideNorthForTheAutos = new(0f, -5f);
    internal static readonly Vector2 OffTankOutsideEastAndFarthestForTheAutos = new(14.5f, 0f);
    internal static readonly Vector2 TheRestInsideSouthForTheAutos = new(0f, 6f);

    internal static readonly Vector2 TanksAtTheFrontOfTheWildChargeLineInFrontOfC = new(0f, 6.5f);
    internal static readonly Vector2 TheRestBehindTheTanksInTheWildChargeLine = new(0f, 10.5f);

    internal const float ExaflarePathRadius = 14f;
    internal const int BesideTheFirstExaflareAwayFromTheSecond = -1;

    private const float LeaveTheMiddleThisLongBeforeTheFirstExplosion = 1f;
    private static readonly float[] OnToEachOfTheFirstThreeExaflaresPointsThisLongAfterTheFirstExplosion = [2.3f, 4f, 5.9f];

    internal static Vector2 AutoSpot(PartyRole role) => role switch
    {
        MainTank => MainTankInsideNorthForTheAutos,
        OffTank => OffTankOutsideEastAndFarthestForTheAutos,
        _ => TheRestInsideSouthForTheAutos,
    };

    internal static Vector2 WildChargeLineSpot(PartyRole role)
        => role.IsTank() ? TanksAtTheFrontOfTheWildChargeLineInFrontOfC : TheRestBehindTheTanksInTheWildChargeLine;

    internal static IAiMove EveryoneOnTheirAutoSpot() => ByRole(role => AutoSpot(role));

    internal static IAiMove LineUpInFrontOfCWithTheTanksFirst() => ByRole(role => WildChargeLineSpot(role));

    internal static float BearingOfExaflare(int firstExaflare, bool clockwise, int exaflare)
        => 45f * (firstExaflare + (clockwise ? exaflare : -exaflare));

    internal static Vector2 OnTheExaflarePath(int firstExaflare, bool clockwise, int exaflare, float radius = ExaflarePathRadius)
        => OnRing(BearingOfExaflare(firstExaflare, clockwise, exaflare), radius);

    internal static void FollowTheExaflaresRoundFromBesideTheFirst(AiManager ai, float firstExplosionAt, int firstExaflare, bool clockwise,
                                                                   float startRadius = ExaflarePathRadius)
    {
        ai.Move(firstExplosionAt - LeaveTheMiddleThisLongBeforeTheFirstExplosion,
                () => Everyone(OnTheExaflarePath(firstExaflare, clockwise, BesideTheFirstExaflareAwayFromTheSecond, startRadius)));
        for (var exaflare = 0; exaflare < OnToEachOfTheFirstThreeExaflaresPointsThisLongAfterTheFirstExplosion.Length; exaflare++)
        {
            var point = OnTheExaflarePath(firstExaflare, clockwise, exaflare);
            ai.Move(firstExplosionAt + OnToEachOfTheFirstThreeExaflaresPointsThisLongAfterTheFirstExplosion[exaflare], () => Everyone(point));
        }
    }

    internal static Vector2 OnRing(float bearingDegrees, float radius)
    {
        var radians = bearingDegrees * MathF.PI / 180f;
        return new Vector2(radius * MathF.Sin(radians), -radius * MathF.Cos(radians));
    }

    internal static IAiMove Everyone(Vector2 spot) => ByRole(_ => spot);

    internal static IAiMove ByRole(Func<PartyRole, Vector2?> spotOf)
        => AiMove.Create(PerRole.All.Select(spotOf).ToArray()).Assignments(PerRole.All);
}
