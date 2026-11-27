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

// The phase's fight from the pull to the Living Liquid's death, which the timeline brings: no health is
// played, so the raidwides are harmless, Hand of Pain never fails and a Jagd Doll's health falls by itself. It plays
// in parts (TeaP1LivingLiquidParts), each on its own clock from its start, so a run can start from any.
// A mistake kills by the fight's own rule, with the reason.
//
// Neither boss's auto-attacks are played, nor the dolls': nothing reads them, and the animation of one every 3 s would
// stall a boss's walk.
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
                           + string.Join(", ", state.Emboli.Select(roll => $"{roll.From} to the {roll.AimsAt}"))
                           + "; the pull's Emboli " + string.Join(", ", state.FirstEmboli.Select(roll => $"{roll.From} to the {roll.AimsAt}")) + ".");

        // Before the strat's Run: its first move reads where the Living Liquid stands, and fires ahead of anything
        // scheduled here.
        var parts = state.Parts;
        if (parts.First == Pull) fight.SpawnTheLiquid(new Placement(P1Geometry.LiquidBeforeThePull, 0f));
        else SpawnTheBosses(state.Tornadoes1);
        if (parts.First == Protean1) fight.Tornadoes.Appear(state.Tornadoes1);
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<TeaP1LivingLiquidState>)AiStrats[idx]).Run(state, world);

        foreach (var duties in state.Plan.ChatLines(parts.Plays)) world.Events.Add(0f, () => world.Announce(duties));
        world.Events.Add(0.5f, fight.TakeTargets);
        if (parts.Plays(Pull)) ThePullIntoTheCentre();
        if (parts.Plays(Protean1)) ProteanOneIntoDrainage(parts.Start(Protean1));
        if (parts.Plays(Cascade2)) CascadeTwoIntoTheKill(parts.Start(Cascade2));
    }

    public void Tick(float delta, float elapsed) => fight?.Tick();

    public void OnStatusLost(StatusLost lost) => fight?.OnStatusLost(lost);

    // A part after the pull starts mid-fight, with the bosses where the tanks would have them: in the frame of the
    // tornadoes standing through Protean 1, which a run from Cascade 2 never shows.
    private void SpawnTheBosses(TeaP1TornadoLayout frame)
        => fight.Spawn(
            new Placement(Vector3.Zero, frame.Facing(P1Geometry.LiquidStartsFacing)),
            new Placement(frame.At(P1Geometry.HandStartsOnBearing, P1Geometry.HandStartsFromTheCentre), frame.Facing(P1Geometry.HandStartsOnBearing)));

    // The fight's own clock, from the first hit on the Living Liquid, so it takes no start: nothing comes before the
    // pull. The Liquid Hand and the first tornadoes appear 1.4 s after Cascade lands, at 19.6.
    private void ThePullIntoTheCentre()
    {
        fight.FluidSwing(10.1f);
        fight.Cascade(14.2f, state.Tornadoes1, theLiquidSplits: true);
        fight.TheHandGlows(29.3f);
        fight.TornadoWaves(32.4f);
        fight.JagdDollsAppear(32.6f);
        fight.JagdDollsTurnTargetable(33.4f);
        fight.TheHandSlams(34.4f);
        fight.FluidSwing(36.4f);
        fight.FluidStrike(36.5f);
        fight.UntelegraphedTornadoWaves(37.5f);
        fight.Exhausts(38.5f);
        fight.HandOfPain(38.6f);
        fight.Pressurize(40.4f, state.FirstEmboli);
        fight.FluidSwing(55.55f);
        fight.FluidStrike(55.8f);
    }

    // From the fight's clock at 60.9. Drainage and the last Splash come 0.8 s after Cascade 2's part begins: they belong
    // to this one, since a run that starts there has no first tornadoes to cast Drainage; it goes without that Splash
    // too.
    private void ProteanOneIntoDrainage(float start)
    {
        fight.ProteanWave(start + 4.8f);
        fight.HandOfPain(start + 5.0f);
        fight.UntelegraphedWaves(start + 9.9f, sluice: true);
        fight.LastUntelegraphedWaves(start + 13.0f);
        fight.DrainageTethers(start + 17.5f, state.Drainage);
        fight.Splashes(start + 19.1f);
        fight.Drainage(start + 24.7f);
    }

    // From the fight's clock at 84.8. When the Living Liquid falls is the party's doing: here after the slam, so a run
    // plays the slam.
    private void CascadeTwoIntoTheKill(float start)
    {
        fight.HandOfPain(start + 0.25f);
        fight.Cascade(start + 1.9f, state.Tornadoes2);
        fight.LiquidGaol(start + 10.05f, state.Throttled.List);
        fight.ProteanWave(start + 18.15f);
        fight.HandOfPain(start + 20.1f);
        fight.UntelegraphedWaves(start + 23.25f, sluice: true);
        fight.TornadoWaves(start + 24.2f);
        fight.LastUntelegraphedWaves(start + 26.3f);
        fight.UntelegraphedTornadoWaves(start + 29.25f);
        fight.TheHandGlows(start + 33.15f);
        fight.Pressurize(start + 33.2f, state.Emboli);
        fight.TheHandSlams(start + 38.25f);
        fight.TheLiquidFalls(start + 39.7f);
    }
}
