using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;

namespace AnoMech.Tests;

// A scenario whose whole timeline is the test's own, for pieces no catalog scenario plays on their own. `tick` runs as a
// scenario's Tick does, once a frame after the sim; `overrides` stands in for a scenario's settings.
internal sealed class TimelineScenario(IPhase phase, Action<SimWorld> timeline, Action? tick = null, object? overrides = null) : IScenario
{
    public string Name => "Timeline";
    public IPhase Phase => phase;
    public IReadOnlyList<IScenarioAi> AiStrats => [];
    public object? SettingsOverrides => overrides;

    public void Run(SimWorld world, int? selectedAi) => timeline(world);

    public void Tick(float delta, float elapsed) => tick?.Invoke();
}
