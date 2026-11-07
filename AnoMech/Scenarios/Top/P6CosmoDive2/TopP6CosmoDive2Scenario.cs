using System.Collections.Generic;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Top.P6CosmoDive2;

// From 1.9 s before Unlimited Wave Cannon's cast, with the party on its spots for the autos after Wave Cannon 2, to the
// autos after Cosmo Dive. A mistake kills whoever made it on the spot, with the reason.
public sealed class TopP6CosmoDive2Scenario : IScenario
{
    public string Name => "Exaflares/Dive";
    public IPhase Phase => TopZone.P6;

    public void DrawSettings() => settingsWindow.Draw();
    public object SettingsOverrides => settingsWindow.Overrides;
    private readonly TopP6CosmoDive2SettingsWindow settingsWindow = new();

    public IReadOnlyList<IScenarioAi> AiStrats => [new TopP6CosmoDive2LpduAi()];

    // Brilliant Dynamis gives back the bars each player's first limit break spends, so the block starts with the gauge full.
    public void RunInstanceEvents(SimWorld instanceWorld) => instanceWorld.Party.LimitBreak.Set(3f);

    public void Run(SimWorld world, int? selectedAi)
    {
        var state = new TopP6CosmoDive2State(world.Rng, settingsWindow.Overrides);
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<TopP6CosmoDive2State>)AiStrats[idx]).Run(state, world);

        var alphaOmega = new TopP6AlphaOmega(world);
        world.Events.Add(0f, alphaOmega.Spawn);
        world.Events.Add(1f, alphaOmega.ApplyCosmoMemoryStatuses);
        alphaOmega.UnlimitedWaveCannon(new TopP6UnlimitedWaveCannon(state.FirstExaflare, state.Clockwise, 1.9f, state.ExaflareStep));
        alphaOmega.CosmoDive(19.6f);
        alphaOmega.Autos(34.4f);
    }
}
