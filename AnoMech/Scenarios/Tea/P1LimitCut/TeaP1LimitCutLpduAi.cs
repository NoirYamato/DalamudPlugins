using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Tea.P1LimitCut;

public class TeaP1LimitCutLpduAi : IScenarioAi<TeaP1LimitCutState>
{
    public string Name => "[LPDU] Light PF";
    public string? Group => "EU";

    public void Run(TeaP1LimitCutState state, SimWorld world)
    {
    }
}
