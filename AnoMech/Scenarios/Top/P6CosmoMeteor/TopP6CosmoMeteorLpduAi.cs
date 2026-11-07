using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Scenarios.Top.P6CosmoMeteor;

public sealed class TopP6CosmoMeteorLpduAi : IScenarioAi<TopP6CosmoMeteorState>
{
    public string Name => "[LPDU] Light PF";
    public string? Group => "EU";

    internal const float ClockRadius = 13f;
    internal const float FlareRadius = 18.5f;
    internal const float NorthCardinal = 0f;
    internal const float SouthCardinal = 180f;
    internal const float FacingSouth = 0f;

    internal static readonly Vector2 MainTankAutoSpotInsideNorth = new(0f, -5f);
    internal static readonly Vector2 OffTankAutoSpotOutsideEast = new(14.5f, 0f);
    internal static readonly Vector2 PartyAutoSpotInsideSouth = new(0f, 6f);
    internal static readonly Vector2 UnderAlphaOmega = Vector2.Zero;

    internal static readonly IReadOnlyDictionary<PartyRole, float> ClockBearings = new Dictionary<PartyRole, float>
    {
        [PhysRangedDps] = 0f, [CasterDps] = 45f, [ShieldHealer] = 90f, [MeleeDpsB] = 135f,
        [OffTank] = 180f, [MeleeDpsA] = 225f, [RegenHealer] = 270f, [MainTank] = 315f,
    };

    internal static readonly float[] CardinalsClockwiseFromNorth = [0f, 90f, 180f, 270f];

    private TopP6CosmoMeteorState state = null!;

    public void Run(TopP6CosmoMeteorState s, SimWorld world)
    {
        state = s;
        var ai = new AiManager(world);
        ai.Move(0.3f, MainTankInsideNorthOffTankOutsideEastTheRestInsideSouthForTheAutos);
        ai.Move(2.6f, EveryoneUnderAlphaOmegaToBaitTheMeteorsInTheMiddle);
        ai.Move(7f, EveryoneOutToTheirMeteorClockSpotOnceTheBaitsDrop);
        ai.Move(13.3f, ShieldHealerIntoTheMiddleAfterTheFirstEightSpreads);
        world.Events.Add(13.5f, () => TopP6LimitBreaks.LimitBreak3(world, CasterDps));
        ai.Move(22.4f, FlaresToTheFreeCardinalsTheRestStackWhileThePhysRangedHoldsNorth);
        world.Events.Add(23.5f, () => PhysRangedTurnsSouthToLineUpBothMeteorsThroughAlphaOmega(world));
        world.Events.Add(24f, () => TopP6LimitBreaks.LimitBreak3(world, PhysRangedDps));
    }

    internal static Vector2 AutoSpot(PartyRole role) => role switch
    {
        MainTank => MainTankAutoSpotInsideNorth,
        OffTank => OffTankAutoSpotOutsideEast,
        _ => PartyAutoSpotInsideSouth,
    };

    internal static Vector2 ClockSpot(PartyRole role) => OnRing(ClockBearings[role], ClockRadius);

    internal static float StackCardinal(IReadOnlyCollection<PartyRole> flares)
        => flares.Contains(PhysRangedDps) ? SouthCardinal : NorthCardinal;

    internal static IReadOnlyDictionary<PartyRole, float> FlareCardinalsKeepingTheirClockOrderFromNorth(IReadOnlyCollection<PartyRole> flares)
    {
        var freeCardinals = CardinalsClockwiseFromNorth.Where(c => c != StackCardinal(flares));
        return flares.OrderBy(role => ClockBearings[role]).Zip(freeCardinals).ToDictionary(t => t.First, t => t.Second);
    }

    internal static Vector2 FinalSpot(PartyRole role, IReadOnlyCollection<PartyRole> flares)
    {
        if (role == PhysRangedDps) return ClockSpot(PhysRangedDps);
        return flares.Contains(role)
            ? OnRing(FlareCardinalsKeepingTheirClockOrderFromNorth(flares)[role], FlareRadius)
            : OnRing(StackCardinal(flares), ClockRadius);
    }

    private static IAiMove MainTankInsideNorthOffTankOutsideEastTheRestInsideSouthForTheAutos()
        => ByRole(AutoSpot);

    private static IAiMove EveryoneUnderAlphaOmegaToBaitTheMeteorsInTheMiddle()
        => ByRole(_ => UnderAlphaOmega);

    private static IAiMove EveryoneOutToTheirMeteorClockSpotOnceTheBaitsDrop()
        => ByRole(ClockSpot);

    private static IAiMove ShieldHealerIntoTheMiddleAfterTheFirstEightSpreads()
        => AiMove.Single(ShieldHealer, UnderAlphaOmega);

    private IAiMove FlaresToTheFreeCardinalsTheRestStackWhileThePhysRangedHoldsNorth()
    {
        var flares = state.Flares.List;
        return AiMove.Create(PerRole.All.Select(role => role == PhysRangedDps ? null : (Vector2?)FinalSpot(role, flares)).ToArray())
                     .NaturalOrder();
    }

    private static void PhysRangedTurnsSouthToLineUpBothMeteorsThroughAlphaOmega(SimWorld world)
    {
        if (world.Party.Get(PhysRangedDps) is { } physRanged && physRanged.IsAlive())
            physRanged.TurnTo(FacingSouth);
    }

    private static IAiMove ByRole(Func<PartyRole, Vector2> spot)
        => AiMove.Create(PerRole.All.Select(role => (Vector2?)spot(role)).ToArray()).NaturalOrder();

    private static Vector2 OnRing(float bearingDegrees, float radius)
    {
        var radians = bearingDegrees * MathF.PI / 180f;
        return new Vector2(radius * MathF.Sin(radians), -radius * MathF.Cos(radians));
    }
}
