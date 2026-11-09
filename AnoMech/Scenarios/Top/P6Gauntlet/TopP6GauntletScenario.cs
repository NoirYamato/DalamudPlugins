using System.Collections.Generic;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using static AnoMech.Scenarios.Top.P6Gauntlet.TopP6GauntletPart;
using static AnoMech.Scenarios.Top.TopP6Constants;
using Actions = AnoMech.Scenarios.Top.TopP6Actions;

namespace AnoMech.Scenarios.Top.P6Gauntlet;

// All of P6, from 1.9 s before Cosmo Memory's cast, Alpha Omega already targetable, to Run: ****mi*, the enrage. It plays
// in parts (TopP6GauntletParts), each on its own clock from its start, so a run can start from any of them. A mistake
// kills whoever made it on the spot, with the reason.
//
// Every limit break goes through the run's TopP6LimitBreaks: the human presses its own, the strat the other seats', and
// the fight's checks count whoever pressed, so a missing or wasted one plays out as in the game. The scenario reads the
// strat's plan (TopP6GauntletLimitBreakPlan), its only read of the strat, for three things: the windows the Gauntlet
// holds the human's own melee limit breaks to, the start's chat line naming the human's duties, and, on a later start,
// which seats already carry Spark of Dynamis (SparkedBefore).
public sealed class TopP6GauntletScenario : IScenario
{
    public string Name => "Gauntlet";
    public IPhase Phase => TopZone.P6;

    public void DrawSettings() => settingsWindow.Draw();
    public bool HasPerPlayerSettings => true;
    public void DrawPerPlayerSettings() => settingsWindow.DrawPerPlayer();
    public object SettingsOverrides => settingsWindow.Overrides;
    public IReadOnlyList<string> SettingsConflicts => [.. settingsWindow.Overrides.Validate().Problems, .. ModuleProblems()];
    private readonly TopP6GauntletSettingsWindow settingsWindow = new();

    public IReadOnlyList<IScenarioAi> AiStrats => [new TopP6GauntletLpduAi()];

    internal const string ResolveYourOwnActionsIsOff =
        "The Gauntlet needs \"Resolve your own actions\" on in the settings: only it plays your limit breaks in the sim.";

    private const float CosmoMemoryAt = 1.9f;
    private const float UnlimitedWaveCannonAt = 1.9f;

    private SimWorld world = null!;
    private TopP6GauntletState state = null!;
    private TopP6AlphaOmega alphaOmega = null!;
    private TopP6LimitBreaks limitBreaks = null!;

    internal TopP6GauntletState? State => state;
    internal TopP6AlphaOmega? AlphaOmega => alphaOmega;
    internal TopP6LimitBreaks? LimitBreaks => limitBreaks;

    // A debug-driven player presses none of its own limit breaks, so a headless run needs no module.
    internal static IEnumerable<string> ModuleProblems()
    {
        if (!Natives.UserActions.Enabled && !DebugBotControl.Enabled) yield return ResolveYourOwnActionsIsOff;
    }

    internal static TopP6UnlimitedWaveCannon UnlimitedWaveCannon1(TopP6GauntletState state)
        => new(state.Exaflares1First, state.Exaflares1Clockwise, state.Parts.Start(Exaflares1) + UnlimitedWaveCannonAt, state.Exaflares1Step);

    internal static TopP6UnlimitedWaveCannon UnlimitedWaveCannon2(TopP6GauntletState state)
        => new(state.Exaflares2First, state.Exaflares2Clockwise, state.Parts.Start(Exaflares2) + UnlimitedWaveCannonAt, state.Exaflares2Step);

    public void Run(SimWorld worldParam, int? selectedAi)
    {
        world = worldParam;
        limitBreaks?.Dispose();
        limitBreaks = new TopP6LimitBreaks(world);
        alphaOmega = new TopP6AlphaOmega(world);
        state = new TopP6GauntletState(world.Rng, world.Party, settingsWindow.Overrides) { LimitBreaks = limitBreaks, AlphaOmega = alphaOmega };
        if (selectedAi is { } idx && idx < AiStrats.Count)
            ((IScenarioAi<TopP6GauntletState>)AiStrats[idx]).Run(state, world);

        var parts = state.Parts;
        world.Events.Add(0f, alphaOmega.Spawn);
        world.Events.Add(0f, () => world.Announce(state.LimitBreakPlan.ChatLine(duty => parts.Plays(PartOf(duty)))));
        if (parts.First != CosmoMemory) PickUpAfterCosmoMemory(parts.First);
        if (parts.Plays(CosmoMemory)) CosmoMemoryIntoCosmoDive(parts.Start(CosmoMemory));
        if (parts.Plays(Exaflares1)) ExaflaresIntoWaveCannon(parts.Start(Exaflares1));
        if (parts.Plays(CosmoArrow2)) CosmoArrowIntoWaveCannon(parts.Start(CosmoArrow2));
        if (parts.Plays(Exaflares2)) ExaflaresIntoCosmoDive(parts.Start(Exaflares2));
        if (parts.Plays(CosmoMeteor)) alphaOmega.CosmoMeteor(parts.Start(CosmoMeteor) + 1.9f, state.MeteorRolls, limitBreaks);
        if (parts.Plays(MagicNumber)) MagicNumbersIntoTheEnrage(parts.Start(MagicNumber));
    }

    public void Tick(float delta, float elapsed) => limitBreaks?.Tick();

    private void CosmoMemoryIntoCosmoDive(float start)
    {
        alphaOmega.CosmoMemory(start + CosmoMemoryAt, limitBreaks);
        alphaOmega.Autos(start + 12.0f);
        alphaOmega.CosmoArrow(new TopP6Exasquares(state.Arrow1InFirst, start + 16.3f));
        alphaOmega.CosmoDive(start + 31.4f);
        alphaOmega.Autos(start + 46.2f);
    }

    private void ExaflaresIntoWaveCannon(float start)
    {
        alphaOmega.UnlimitedWaveCannon(UnlimitedWaveCannon1(state));
        alphaOmega.WaveCannon(start + 24.5f, state.Lasers1.List, state.WildCharge1Target);
        alphaOmega.Autos(start + 39.9f);
    }

    private void CosmoArrowIntoWaveCannon(float start)
    {
        alphaOmega.CosmoArrow(new TopP6Exasquares(state.Arrow2InFirst, start + 1.9f));
        alphaOmega.WaveCannon(start + 16.1f, state.Lasers2.List, state.WildCharge2Target);
        alphaOmega.Autos(start + 31.5f);
    }

    private void ExaflaresIntoCosmoDive(float start)
    {
        alphaOmega.UnlimitedWaveCannon(UnlimitedWaveCannon2(state));
        alphaOmega.CosmoDive(start + 19.6f);
        alphaOmega.Autos(start + 34.4f);
        var barEnds = start + 19.6f + Actions.CosmoDive.ResolvesAfter();
        HoldTheHumanTo(TopP6LimitBreakDuty.FirstDiveMelee, barEnds, barEnds + P6Duration.FirstDiveMeleeLatestPress, TopP6LimitBreaks.FirstDiveMeleeRule);
        HoldTheHumanTo(TopP6LimitBreakDuty.SecondDiveMelee, barEnds, barEnds + P6Duration.SecondDiveMeleeLatestPress,
            TopP6LimitBreaks.SecondDiveMeleeRule, afterAnotherMeleeLimitBreak3: true);
    }

    private void MagicNumbersIntoTheEnrage(float start)
    {
        alphaOmega.MagicNumber(start + 1.9f, limitBreaks);
        alphaOmega.MagicNumber(start + 18.0f, limitBreaks);
        var runMiAt = start + 32.1f;
        alphaOmega.RunMi(runMiAt, limitBreaks);
        var runMiLands = TopP6AlphaOmega.RunMiLandsAt(runMiAt);
        HoldTheHumanTo(TopP6LimitBreakDuty.Enrage, runMiAt, runMiLands - HumansLimitBreak3Cast(), TopP6LimitBreaks.EnrageMeleeRule, landsBy: runMiLands);
    }

    // Cosmo Memory's hit's statuses, as the limit breaks the plan has pressed by then leave them, and the gauge as the
    // party's own generation has it then, on the run's own clock so the rebuild still ends exactly full. The gauge shows
    // from the first frame, so the party's Brilliant and Spark of Dynamis come with it, before anyone can press; Alpha
    // Omega's Code: ****mi* waits a second, for an actor spawned in the first frame.
    private void PickUpAfterCosmoMemory(TopP6GauntletPart first)
    {
        var startsAt = TopP6GauntletParts.GauntletStart(first);
        var (from, fullBy) = TopP6AlphaOmega.GaugeRebuildAfterCosmoMemory(TopP6GauntletParts.GauntletStart(CosmoMemory) + CosmoMemoryAt);
        limitBreaks.Rebuild(from - startsAt, fullBy - startsAt);
        limitBreaks.SetGauge(TopP6LimitBreaks.RebuiltBars(0f, from - startsAt, fullBy - startsAt));
        var sparked = state.LimitBreakPlan.SparkedBefore(TheNextDuty(first));
        world.Events.Add(0f, () => alphaOmega.ApplyCosmoMemorysDynamis(sparked));
        world.Events.Add(1f, alphaOmega.ApplyCodeMi);
    }

    private static TopP6LimitBreakDuty TheNextDuty(TopP6GauntletPart part) => part switch
    {
        CosmoMemory => TopP6LimitBreakDuty.CosmoMemory,
        Exaflares1 or CosmoArrow2 or Exaflares2 => TopP6LimitBreakDuty.FirstDiveMelee,
        CosmoMeteor => TopP6LimitBreakDuty.Comets,
        _ => TopP6LimitBreakDuty.MagicNumber1Tank,
    };

    private static TopP6GauntletPart PartOf(TopP6LimitBreakDuty duty) => duty switch
    {
        TopP6LimitBreakDuty.CosmoMemory => CosmoMemory,
        TopP6LimitBreakDuty.FirstDiveMelee or TopP6LimitBreakDuty.SecondDiveMelee => Exaflares2,
        TopP6LimitBreakDuty.Comets or TopP6LimitBreakDuty.Meteors => CosmoMeteor,
        _ => MagicNumber,
    };

    private void HoldTheHumanTo(TopP6LimitBreakDuty duty, float opensAt, float latestPress, string rule, bool afterAnotherMeleeLimitBreak3 = false,
        float? landsBy = null)
    {
        var plan = state.LimitBreakPlan;
        if (!plan.HumanPresses(duty)) return;
        limitBreaks.HoldTheHumanTo(new TopP6MeleeWindow(plan.HumanSeat, opensAt, latestPress, rule, afterAnotherMeleeLimitBreak3, landsBy),
            () => alphaOmega.Enemy);
    }

    private float HumansLimitBreak3Cast()
        => Natives.Data.Action(LimitBreakHandler.ActionId(state.LimitBreakPlan.HumanJob, TopP6LimitBreaks.Bars))?.CastSeconds ?? 0f;
}
