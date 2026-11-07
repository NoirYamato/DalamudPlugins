using System.Collections.Generic;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Top.P6CosmoDive1;

// From 1.9 s before Cosmo Memory's cast, Alpha Omega already targetable, to the autos after Cosmo Dive. A mistake kills
// whoever made it on the spot, with the reason.
public sealed class TopP6CosmoDive1Scenario : IScenario
{
    public string Name => "Exasquares/Dive";
    public IPhase Phase => TopZone.P6;

    public void DrawSettings() => settingsWindow.Draw();
    public object SettingsOverrides => settingsWindow.Overrides;
    private readonly TopP6CosmoDive1SettingsWindow settingsWindow = new();

    public IReadOnlyList<IScenarioAi> AiStrats => [new TopP6CosmoDive1LpduAi()];

    public void Run(SimWorld world, int? selectedAi)
    {
        var state = new TopP6CosmoDive1State(world.Rng, settingsWindow.Overrides);
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<TopP6CosmoDive1State>)AiStrats[idx]).Run(state, world);

        var alphaOmega = new TopP6AlphaOmega(world);
        world.Events.Add(0f, alphaOmega.Spawn);
        alphaOmega.CosmoMemory(1.9f);
        alphaOmega.Autos(12.0f);
        alphaOmega.CosmoArrow(new TopP6Exasquares(state.InFirst, 16.3f));
        alphaOmega.CosmoDive(31.4f);
        alphaOmega.Autos(46.2f);
    }
}
