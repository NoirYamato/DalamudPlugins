using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Tea.P1LivingLiquid;

public class TeaP1LivingLiquidLpduAi : IScenarioAi<TeaP1LivingLiquidState>
{
    public string Name => "[LPDU] Light PF";
    public string? Group => "EU";

    public void Run(TeaP1LivingLiquidState state, SimWorld world)
    {
    }
}
