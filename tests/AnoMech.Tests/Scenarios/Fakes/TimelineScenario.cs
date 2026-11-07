using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;

namespace AnoMech.Tests;

// A scenario whose whole timeline is the test's own, for pieces no catalog scenario plays on their own.
internal sealed class TimelineScenario(IPhase phase, Action<SimWorld> timeline) : IScenario
{
    public string Name => "Timeline";
    public IPhase Phase => phase;
    public IReadOnlyList<IScenarioAi> AiStrats => [];

    public void Run(SimWorld world, int? selectedAi) => timeline(world);
}
