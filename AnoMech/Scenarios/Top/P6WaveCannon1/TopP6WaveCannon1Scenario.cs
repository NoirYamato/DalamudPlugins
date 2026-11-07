using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;
using Actions = AnoMech.Scenarios.Top.TopP6Actions;

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

    private const int LasersPerSet = 4;

    private SimWorld world = null!;
    private TopP6WaveCannon1State state = null!;
    private TopP6AlphaOmega alphaOmega = null!;

    // Brilliant Dynamis gives back the bars each player's first limit break spends, so the block starts with the gauge full.
    public void RunInstanceEvents(SimWorld instanceWorld) => instanceWorld.Party.LimitBreak.Set(3f);

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        world = worldParam;
        state = new TopP6WaveCannon1State(world.Rng, world.Party, settingsWindow.Overrides);
        alphaOmega = new TopP6AlphaOmega(world);
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<TopP6WaveCannon1State>)AiStrats[idx]).Run(state, world);

        world.Events.Add(0f, alphaOmega.Spawn);
        world.Events.Add(1f, alphaOmega.ApplyCosmoMemoryStatuses);
        // Its exaflares go off from 13.9 to 23.5 and its six puddle sets drop from 11.9 to 21.4.
        alphaOmega.UnlimitedWaveCannon(new TopP6UnlimitedWaveCannon(state.FirstExaflare, state.Clockwise, 1.9f, state.ExaflareStep));
        world.Events.Add(24.5f, () => alphaOmega.Cast(TopActions.AlphaOmegaWaveCannon, alphaOmega.Position));
        world.Events.Add(27.5f, () => FireLasers(0));
        world.Events.Add(29.5f, () => FireLasers(1));
        world.Events.Add(35.8f, WildCharge);
        alphaOmega.Autos(39.9f);
    }

    // Each from Alpha Omega toward where its target stands now.
    private void FireLasers(int set)
    {
        foreach (var role in state.ProteanOrder.List.Skip(set * LasersPerSet).Take(LasersPerSet))
            if (world.Party.Get(role) is { } target && target.IsAlive())
                alphaOmega.CastFromHelper(Actions.WaveCannonLaser, target);
    }

    private void WildCharge()
    {
        if (world.Party.Get(state.WildChargeTarget) is { } target && target.IsAlive())
            alphaOmega.Cast(Actions.WildCharge, target);
    }
}
