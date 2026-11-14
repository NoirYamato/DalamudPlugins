using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Tea.P1LivingLiquid.TeaP1LivingLiquidPart;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Scenarios.Tea.P1LivingLiquid;

// The phase's fight from the Living Liquid's first Protean Wave to its death, which the timeline brings: no health is
// played, so the raidwides are harmless and Hand of Pain never fails. It plays in parts (TeaP1LivingLiquidParts), each
// on its own clock from its start, so a run can start from either; the pull before them isn't played. A mistake kills
// by the fight's own rule, with the reason.
//
// The scenario reads the strat's plan (TeaP1LivingLiquidPlan), its only read of the strat, for the start's chat lines
// naming the human's duties.
public sealed class TeaP1LivingLiquidScenario : IScenario
{
    public string Name => "Living Liquid";
    public IPhase Phase => TeaZone.P1;
    public float BgmSecondsAtStart => TeaP1LivingLiquidParts.FightClock(TeaP1LivingLiquidParts.From(settingsWindow.Overrides).First);

    public void DrawSettings() => settingsWindow.Draw();
    public bool HasPerPlayerSettings => true;
    public void DrawPerPlayerSettings() => settingsWindow.DrawPerPlayer();
    public object SettingsOverrides => settingsWindow.Overrides;
    public IReadOnlyList<string> SettingsConflicts => settingsWindow.Overrides.Validate().Problems;
    private readonly TeaP1LivingLiquidSettingsWindow settingsWindow = new();

    public IReadOnlyList<IScenarioAi> AiStrats => [new TeaP1LivingLiquidLpduAi()];

    private SimWorld world = null!;
    private TeaP1LivingLiquidState state = null!;
    private TeaP1LivingLiquid fight = null!;
    private TeaP1Presses? presses;

    internal TeaP1LivingLiquidState? State => state;
    internal TeaP1LivingLiquid? Fight => fight;
    internal TeaP1Presses? Presses => presses;

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        world = worldParam;
        presses?.Dispose();
        presses = new TeaP1Presses(world);
        fight = new TeaP1LivingLiquid(world, presses);
        state = new TeaP1LivingLiquidState(world.Rng, world.Party, settingsWindow.Overrides) { Fight = fight, Presses = presses };
        fight.EmbolusSpeed = state.EmbolusSpeed;
        DiagnosticLog.Info($"[TeaP1LivingLiquid] Roll: tornadoes open {state.Tornadoes1.Open}, then {state.Tornadoes2.Open}; Drainage "
                           + string.Join(", ", state.Drainage.Select(roll => $"{roll.From} on {roll.FirstTarget}"))
                           + $"; Throttle on {string.Join(" ", state.Throttled.List)}; Emboli "
                           + string.Join(", ", state.Emboli.Select(roll => $"{roll.From} to the {roll.AimsAt}")) + ".");

        // Before the strat's Run: its first move reads where the Living Liquid stands, and fires ahead of anything
        // scheduled here.
        var parts = state.Parts;
        SpawnTheBosses(state.Tornadoes1);
        if (parts.First == Protean1) fight.Tornadoes.Appear(state.Tornadoes1);
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<TeaP1LivingLiquidState>)AiStrats[idx]).Run(state, world);

        foreach (var duties in state.Plan.ChatLines(parts.Plays)) world.Events.Add(0f, () => world.Announce(duties));
        world.Events.Add(0.5f, fight.TakeTargets);
        if (parts.Plays(Protean1)) ProteanOneIntoDrainage(parts.Start(Protean1));
        if (parts.Plays(Cascade2)) CascadeTwoIntoTheKill(parts.Start(Cascade2));
    }

    public void Tick(float delta, float elapsed) => fight?.Tick();

    // Both parts start mid-fight, with the bosses where the tanks would have them: in the frame of the tornadoes
    // standing through Protean 1, which a run from Cascade 2 never shows.
    private void SpawnTheBosses(TeaP1TornadoLayout frame)
        => fight.Spawn(
            new Placement(Vector3.Zero, frame.Facing(P1Geometry.LiquidStartsFacing)),
            new Placement(frame.At(P1Geometry.HandStartsOnBearing, P1Geometry.HandStartsFromTheCentre), frame.Facing(P1Geometry.HandStartsOnBearing)));

    // From the fight's clock at 62.0. Hand of Pain's cast and Drainage end a moment after Cascade 2's part begins: they
    // belong to this one, since a run that starts there has no first tornadoes.
    private void ProteanOneIntoDrainage(float start)
    {
        fight.ProteanWave(start + 4.8f);
        fight.HandOfPain(start + 5.0f);
        fight.UntelegraphedWaves(start + 9.9f, sluice: true);
        fight.LastUntelegraphedWaves(start + 13.0f);
        fight.Splashes(start + 19.1f);
        fight.DrainageTethers(start + 20.0f, state.Drainage);
        fight.HandOfPain(start + 24.1f);
        fight.Drainage(start + 24.6f);
    }

    // From the fight's clock at 85.8.
    private void CascadeTwoIntoTheKill(float start)
    {
        fight.Cascade(start + 1.9f, state.Tornadoes2);
        fight.LiquidGaol(start + 10.1f, state.Throttled.List);
        fight.ProteanWave(start + 18.2f);
        fight.HandOfPain(start + 20.2f);
        fight.UntelegraphedWaves(start + 23.3f, sluice: true);
        fight.TornadoWaves(start + 24.2f);
        fight.LastUntelegraphedWaves(start + 26.3f);
        fight.UntelegraphedTornadoWaves(start + 29.3f);
        fight.TheHandGlows(start + 33.2f);
        fight.Pressurize(start + 33.3f, state.Emboli);
        fight.TheHandSlams(start + 38.4f);
        fight.TheLiquidFalls(start + 39.7f);
    }
}
