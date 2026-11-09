using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Top.P6Gauntlet;

namespace AnoMech.Tests;

// The main window's Start, refused with its reason as the button's tooltip.
public class RunControllerTests
{
    // Settings the scenario can't run with refuse Start before anything zones in: the P6 Gauntlet with "Resolve your own
    // actions" off. ScenarioSession would refuse the run only once the party stood in the arena.
    [Test]
    public void StartIsRefusedWhileTheScenariosSettingsConflict()
    {
        var fake = FakeGame.Install();
        fake.UserActions.Enabled = false;
        var debugDriver = DebugBotControl.Enabled;
        DebugBotControl.Enabled = false;
        try
        {
            var selection = new ScenarioSelection(new TopP6GauntletScenario(), 0, 0, null, null);

            Assert.That(new RunController(null!, null!).StartRefusal(selection, solo: false), Is.EqualTo(TopP6GauntletScenario.ResolveYourOwnActionsIsOff));
        }
        finally
        {
            DebugBotControl.Enabled = debugDriver;
        }
    }
}
