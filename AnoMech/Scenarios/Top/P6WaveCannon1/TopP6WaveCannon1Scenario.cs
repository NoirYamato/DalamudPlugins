using System.Collections.Generic;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Top.P6WaveCannon1;

// From 1.9 s before Unlimited Wave Cannon's cast to Alpha Omega's two autos after Wave Cannon 1. A mistake kills
// whoever made it on the spot, with the reason.
public sealed class TopP6WaveCannon1Scenario : IScenario
{
    public string Name => "Exaflares/WC1";
    public IPhase Phase => TopZone.P6;

    public void DrawSettings() => settingsWindow.Draw();
    public object SettingsOverrides => settingsWindow.Overrides;
    private readonly TopP6WaveCannon1SettingsWindow settingsWindow = new();

    public IReadOnlyList<IScenarioAi> AiStrats => [new TopP6WaveCannon1LpduAi()];

    private const float UnlimitedWaveCannonAt = 1.9f;

    // Brilliant Dynamis gives back the bars each player's first limit break spends, so the block starts with the gauge full.
    public void RunInstanceEvents(SimWorld instanceWorld) => instanceWorld.Party.LimitBreak.Set(3f);

    // Its exaflares go off from 13.9 to 23.5 and its six puddle sets drop from 11.9 to 21.4.
    internal static TopP6UnlimitedWaveCannon UnlimitedWaveCannon(TopP6WaveCannon1State state)
        => new(state.FirstExaflare, state.Clockwise, UnlimitedWaveCannonAt, state.ExaflareStep);

    public void Run(SimWorld world, int? selectedAi)
    {
        var state = new TopP6WaveCannon1State(world.Rng, world.Party, settingsWindow.Overrides);
        var alphaOmega = new TopP6AlphaOmega(world);
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<TopP6WaveCannon1State>)AiStrats[idx]).Run(state, world);

        world.Events.Add(0f, alphaOmega.Spawn);
        world.Events.Add(1f, alphaOmega.ApplyCosmoMemoryStatuses);
        alphaOmega.UnlimitedWaveCannon(UnlimitedWaveCannon(state));
        alphaOmega.WaveCannon(24.5f, state.ProteanOrder.List, state.WildChargeTarget);
        alphaOmega.Autos(39.9f);
    }
}
