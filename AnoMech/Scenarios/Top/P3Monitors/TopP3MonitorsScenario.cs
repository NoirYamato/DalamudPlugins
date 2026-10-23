using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Top.TopConstants;
using Actions = AnoMech.Scenarios.Top.TopActions;

namespace AnoMech.Scenarios.Top.P3Monitors;

public sealed class TopP3MonitorsScenario : IScenario
{
    public string Name => "Monitors";
    public IPhase Phase => TopZone.P3;

    public void DrawSettings() => settingsWindow.Draw();
    public bool HasPerPlayerSettings => true;
    public void DrawPerPlayerSettings() => settingsWindow.DrawPerPlayer();
    public object SettingsOverrides => settingsWindow.Overrides;
    public IReadOnlyList<string> SettingsConflicts => settingsWindow.Overrides.Validate().Problems;
    private readonly TopP3MonitorsSettingsWindow settingsWindow = new();

    public IReadOnlyList<IScenarioAi> AiStrats => [new TopP3MonitorsLpduAi()];

    private const int TargetsPerMonitor = 2;

    private static readonly Placement OmegaSpot = new(Vector3.Zero, MathF.PI);

    private SimWorld world = null!;
    private SimParty party = null!;
    private TopP3MonitorsState state = null!;
    private SimEnemy? omega;
    private List<SimEnemy?> monitorHelpers = [];

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        world = worldParam;
        party = worldParam.Party;
        state = new TopP3MonitorsState(world.Rng, party, settingsWindow.Overrides);
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<TopP3MonitorsState>)AiStrats[idx]).Run(state, world);

        world.Events.Add(0f, SpawnOmega);
        world.Events.Add(0f, SpawnHelpers);
        world.Events.Add(0.1f, () => omega?.Cast(TopP3Actions.CriticalError));
        // Omega's teleport just before the cast (31558) would only move him onto the spot he already holds.
        world.Events.Add(19.4f, StartMonitors);
        world.Events.Add(29.5f, FireMonitors);
    }

    private void SpawnOmega()
    {
        omega = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: TopP3Constants.OmegaP3,
            NameId: BNpcNameId.OmegaFinal,
            Level: 90,
            Targetable: true,
            Placement: OmegaSpot));
    }

    // Casts on a helper spawned the same frame play no animation, so every helper exists well
    // before the hits and is moved onto its target when they fire.
    private void SpawnHelpers()
        => monitorHelpers = Enumerable.Range(0, (1 + TopP3MonitorsState.PlayerMonitors) * TargetsPerMonitor)
                                      .Select(_ => world.SpawnEnemy(new EnemySpawnConfig(
                                          BNpcBaseId: BNpcBaseId.OmegaHelper,
                                          Visibility: SpawnVisibility.InvisibleHelper,
                                          EnemyList: EnemyListMode.Never,
                                          Placement: new Placement(Vector3.Zero, 0f))))
                                      .ToList();

    private void StartMonitors()
    {
        omega?.Cast(state.OmegaCastId, animationLock: 3.1f);
        // UNVERIFIED: the Loading statuses land with the cast's start.
        foreach (var role in state.Monitors.List)
            party.Get(role)?.AddStatus(state.SideOf(role).StatusId());
    }

    // Every target is picked before the first circle lands, so a death can't change the later picks.
    private void FireMonitors()
    {
        var targets = MonitorTargets(OmegaSpot, state.OmegaSide, owner: null).ToList();
        foreach (var role in state.Monitors.List)
        {
            if (party.Get(role) is not { } owner || !owner.IsAlive()) continue;
            var side = state.SideOf(role);
            targets.AddRange(MonitorTargets(owner.Placement(), side, owner));
            owner.RemoveStatus(side.StatusId());
        }

        foreach (var (target, helper) in targets.Zip(monitorHelpers))
        {
            if (helper is null) continue;
            helper.SetPosition(target.Position);
            helper.Cast(Actions.OversampledWaveCannon, target);
        }
    }

    private IReadOnlyList<SimCharacter> MonitorTargets(Placement source, MonitorSide side, SimCharacter? owner)
        => party.Find.OnSideN(world.Rng, source, side.Mul(), TargetsPerMonitor, exclude: owner);
}
