using System.Collections.Generic;
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

    internal const float TheFirstExaflareGoesOff = 13.9f;
    internal const float InsideTheLastPuddlesRadius = 4f;
    internal const float SpreadRadius = 10.5f;

    internal static readonly IReadOnlyDictionary<PartyRole, float> WaymarkBearing = new Dictionary<PartyRole, float>
    {
        [MainTank] = 0f, [CasterDps] = 45f, [OffTank] = 90f, [MeleeDpsB] = 135f,
        [ShieldHealer] = 180f, [MeleeDpsA] = 225f, [RegenHealer] = 270f, [PhysRangedDps] = 315f,
    };

    public void Run(TopP6WaveCannon1State state, SimWorld world)
    {
        var ai = new AiManager(world);
        ai.Move(0f, TopP6LpduAi.EveryoneOnTheirAutoSpot);
        TopP6LpduAi.StepOutOfTheHitboxToReadTheArrowsThenStackInTheMiddle(ai, TheFirstExaflareGoesOff);
        TopP6LpduAi.FollowTheExaflaresRoundFromBesideTheFirst(ai, TheFirstExaflareGoesOff, state.FirstExaflare, state.Clockwise);
        ai.Move(21.6f, InTowardTheirWaymarkClearOfTheLastPuddles);
        ai.Move(24.7f, OutToTheirWaveCannonSpreadOnceTheLastPuddleIsGone);
        ai.Move(30f, TopP6LpduAi.LineUpInFrontOfCWithTheTanksFirst);
        ai.Move(36.5f, TopP6LpduAi.EveryoneOnTheirAutoSpot);
    }

    internal static Vector2 InsideTheLastPuddles(PartyRole role) => TopP6LpduAi.OnRing(WaymarkBearing[role], InsideTheLastPuddlesRadius);

    internal static Vector2 SpreadSpot(PartyRole role) => TopP6LpduAi.OnRing(WaymarkBearing[role], SpreadRadius);

    private static IAiMove InTowardTheirWaymarkClearOfTheLastPuddles() => TopP6LpduAi.ByRole(role => InsideTheLastPuddles(role));

    private static IAiMove OutToTheirWaveCannonSpreadOnceTheLastPuddleIsGone() => TopP6LpduAi.ByRole(role => SpreadSpot(role));
}
