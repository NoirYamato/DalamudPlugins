using AnoMech.Core.Game;

namespace AnoMech.Tests;

// A run's chat lines name the plugin by its display name, never upstream's.
public class ScenarioSessionChatTests
{
    [Test]
    public void AFailedRunsChatLinesNameThePlugin()
    {
        using var run = new PracticeRun(PracticeRun.MechanicFails);
        run.StartInZone(run.Params());
        run.Until(() => run.Game.Status == SessionStatus.CompletedFailed);

        Assert.Multiple(() =>
        {
            Assert.That(run.Chat.Lines, Has.Some.EqualTo($"[{Plugin.DisplayName}] Mechanic failed: a test failure"));
            Assert.That(run.Chat.Lines, Has.Some.StartsWith($"[{Plugin.DisplayName}] Run failed: a test failure"));
            Assert.That(run.Chat.Lines, Has.None.Contains("AnoMech"));
        });
    }
}
