using System.Collections.Generic;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Top.P6CosmoMeteor;

// From 1.9 s before Alpha Omega's Cosmo Meteor cast to the flares and the stack (TopP6CosmoMeteor); Magic Number isn't
// played. The adds have no HP and die when the limit breaks that kill them in the fight would land, whether or not
// anyone presses: the comets at 13.5 (the caster's), the meteors at 24.0 (the physical ranged's), so their enrage never
// comes. A mistake kills whoever made it on the spot, with the reason.
public sealed class TopP6CosmoMeteorScenario : IScenario
{
    public string Name => "Cosmo Meteor";
    public IPhase Phase => TopZone.P6;

    public void DrawSettings() => settingsWindow.Draw();
    public bool HasPerPlayerSettings => true;
    public void DrawPerPlayerSettings() => settingsWindow.DrawPerPlayer();
    public object SettingsOverrides => settingsWindow.Overrides;
    public IReadOnlyList<string> SettingsConflicts => settingsWindow.Overrides.Validate().Problems;
    private readonly TopP6CosmoMeteorSettingsWindow settingsWindow = new();

    public IReadOnlyList<IScenarioAi> AiStrats => [new TopP6CosmoMeteorLpduAi()];

    // Brilliant Dynamis gives back the bars each player's first limit break spends, so the block starts with the gauge full.
    public void RunInstanceEvents(SimWorld instanceWorld) => instanceWorld.Party.LimitBreak.Set(3f);

    public void Run(SimWorld world, int? selectedAi)
    {
        var state = new TopP6CosmoMeteorState(world.Rng, world.Party, settingsWindow.Overrides);
        var alphaOmega = new TopP6AlphaOmega(world);
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<TopP6CosmoMeteorState>)AiStrats[idx]).Run(state, world);

        world.Events.Add(0f, alphaOmega.Spawn);
        world.Events.Add(1.0f, alphaOmega.ApplyCosmoMemoryStatuses);
        var cosmoMeteor = alphaOmega.CosmoMeteor(1.9f, state.Rolls);
        world.Events.Add(13.5f, cosmoMeteor.DefeatTheComets);
        world.Events.Add(24.0f, cosmoMeteor.DefeatTheMeteors);
    }
}
