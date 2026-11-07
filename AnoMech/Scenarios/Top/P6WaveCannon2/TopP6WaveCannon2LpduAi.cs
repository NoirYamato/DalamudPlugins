using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Scenarios.Top.P6WaveCannon2;

public sealed class TopP6WaveCannon2LpduAi : IScenarioAi<TopP6WaveCannon2State>
{
    public string Name => "[LPDU] Light PF";
    public string? Group => "EU";

    private static readonly Vector2 SupportHomeBesideItsIntercardinal = new(6.4f, 8.4f);
    private static readonly Vector2 PairStackedInTheCentreSquare = new(3.4f, 3.4f);
    private static readonly Vector2 SupportBackOutPastHome = new(6.4f, 11.4f);
    private static readonly Vector2 PairStackedOutInTheCorner = new(11.3f, 11.3f);
    private static readonly Vector2 PairStackedBackHome = new(7.3f, 7.3f);
    private static readonly Vector2 SupportSidestepTowardTheNorthSouthAxis = new(3.5f, 12f);
    private static readonly Vector2 SupportLaserSpotBesideItsCardinal = new(3.55f, 8.55f);

    public void Run(TopP6WaveCannon2State state, SimWorld world)
    {
        var ai = new AiManager(world);
        ai.Move(2f, () => EachPairInItsQuadrant(SupportHomeBesideItsIntercardinal));
        if (state.InFirst)
        {
            ai.Move(9.7f, () => EachPairInItsQuadrant(PairStackedInTheCentreSquare));
            ai.Move(14f, () => EachPairInItsQuadrant(SupportBackOutPastHome));
            ai.Move(18f, () => EachPairInItsQuadrant(SupportSidestepTowardTheNorthSouthAxis));
            ai.Move(20f, () => EachPairInItsQuadrant(SupportLaserSpotBesideItsCardinal));
            ai.Move(22f, TopP6LpduAi.LineUpInFrontOfCWithTheTanksFirst);
        }
        else
        {
            ai.Move(9.7f, () => EachPairInItsQuadrant(PairStackedOutInTheCorner));
            ai.Move(14f, () => EachPairInItsQuadrant(PairStackedBackHome));
            ai.Move(16f, () => EachPairInItsQuadrant(SupportSidestepTowardTheNorthSouthAxis));
            ai.Move(18f, () => EachPairInItsQuadrant(SupportLaserSpotBesideItsCardinal));
            ai.Move(21.2f, TopP6LpduAi.LineUpInFrontOfCWithTheTanksFirst);
        }
    }

    private static IAiMove EachPairInItsQuadrant(Vector2 supportSpot)
        => TopP6LpduAi.ByRole(role => QuadrantSigns(role) * (role.IsDps() ? MirroredAcrossTheDiagonal(supportSpot) : supportSpot));

    private static Vector2 MirroredAcrossTheDiagonal(Vector2 spot) => new(spot.Y, spot.X);

    private static Vector2 QuadrantSigns(PartyRole role) => role switch
    {
        MainTank or PhysRangedDps => new(-1f, -1f),
        OffTank or CasterDps => new(1f, -1f),
        RegenHealer or MeleeDpsA => new(-1f, 1f),
        _ => new(1f, 1f),
    };
}
