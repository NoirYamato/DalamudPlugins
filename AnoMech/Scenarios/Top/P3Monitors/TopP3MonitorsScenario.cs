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
        world.Events.Add(1f, StartMonitors);
        world.Events.Add(11.1f, FireMonitors);
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
        foreach (var role in state.Monitors.List)
            party.Get(role)?.AddStatus(state.SideOf(role).StatusId());
    }

    // Every target is picked before the first circle lands, so a death can't change the later picks.
    private void FireMonitors()
    {
        var alive = party.ActiveMembers().ToList();
        var targets = MonitorTargets(alive, OmegaSpot, state.OmegaSide, owner: null, world.Rng).ToList();
        foreach (var role in state.Monitors.List)
        {
            if (party.Get(role) is not { } owner || !owner.IsAlive()) continue;
            var side = state.SideOf(role);
            targets.AddRange(MonitorTargets(alive, owner.Placement(), side, owner, world.Rng));
            owner.RemoveStatus(side.StatusId());
        }

        foreach (var (target, helper) in targets.Zip(monitorHelpers))
        {
            if (helper is null) continue;
            helper.SetPosition(target.Position);
            helper.Cast(Actions.OversampledWaveCannon, target);
        }
    }

    // The two nearest the owner in its beam; a beam with fewer inside fills up with anyone else
    // alive, at random. UNVERIFIED which two the game takes when more stand inside: P5's monitors
    // take two at random from the side instead.
    internal static IReadOnlyList<T> MonitorTargets<T>(IReadOnlyList<T> alive, Placement ownerPlacement, MonitorSide side, T? owner, Rng rng)
        where T : class, IPositioned
    {
        var candidates = alive.Where(m => !ReferenceEquals(m, owner)).ToList();
        var picked = candidates.Where(m => InBeam(ownerPlacement, side, m.Position))
                               .OrderBy(m => ownerPlacement.DistanceSq(m.Position))
                               .Take(TargetsPerMonitor)
                               .ToList();
        if (picked.Count < TargetsPerMonitor)
            picked.AddRange(rng.Shuffle(candidates.Except(picked)).Take(TargetsPerMonitor - picked.Count));
        return picked;
    }

    // The half-plane on `side` of the line through the owner along its facing, with the owner's
    // right hand where CharacterFind.OnSideN puts it, so P3's monitors aim as P5's do.
    internal static bool InBeam(Placement owner, MonitorSide side, Vector3 point)
    {
        var rightX = -MathF.Cos(owner.Rotation);
        var rightZ = MathF.Sin(owner.Rotation);
        var dot = (point.X - owner.Position.X) * rightX + (point.Z - owner.Position.Z) * rightZ;
        return dot * side.Mul() < 0f;
    }
}
