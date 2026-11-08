using AnoMech.Core.Game.Party;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Top.P6Gauntlet;

namespace AnoMech.Tests;

// What a test reads off the Gauntlet a ScenarioRun is playing, and how it plays one part on its own.
internal static class TopP6GauntletRun
{
    public static TopP6GauntletScenario Scenario(ScenarioProbe p) => p.Game.Catalog.Scenarios.OfType<TopP6GauntletScenario>().Single();

    public static TopP6GauntletState State(ScenarioProbe p) => Scenario(p).State!;

    public static TopP6LimitBreaks LimitBreaks(ScenarioProbe p) => Scenario(p).LimitBreaks!;

    // The seat's own duties, as the run's plan has them, for a guide pressing as a human would.
    public static bool TheHumanPresses(ScenarioProbe p, TopP6LimitBreakDuty duty) => State(p).LimitBreakPlan.HumanPresses(duty);

    // Only `part`, from t 0 on its own clock.
    public static void Only(TopP6GauntletStateOverrides o, TopP6GauntletPart part) => o.Parts = (part, part);

    public static uint LimitBreak3(PartyRole seat) => AnoMech.Core.UserActions.LimitBreakHandler.ActionId(PartyPresets.Standard[(int)seat].ClassJob, 3);
}
