using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios.Tea;

namespace AnoMech.Tests;

// The weather each of TEA's phases sets as a run starts: retail's settled weather on the first run after the zone-in, a
// change on every later one.
public class TeaZoneTests
{
    // In one stay a run of P3 (Wormhole's Beyond Time) and then one of P1: the P1 run brings back its Fair Skies, which a
    // phase that set no weather would have left as Beyond Time.
    [Test]
    public void P1BringsBackFairSkiesAfterAP3RunInTheSameStay()
    {
        using var run = new PracticeRun(PracticeRun.Clean, phase: TeaZone.P3);
        run.StartInZone(run.Params());
        run.Until(() => run.Game.Status == SessionStatus.Succeeded);

        run.Game.StartScenario(new RunScenarioParams(new TimelineScenario(TeaZone.P1, PracticeRun.Clean), new LocalPartyProvider(), null, 0, null));
        run.Until(() => run.Started.Count == 2 && run.Game.Status == SessionStatus.Succeeded);

        var weathers = run.Fake.Timeline.Rows.Where(row => row["kind"] == "weather").Select(row => (row["id"], row["detail"])).ToList();
        Assert.That(weathers, Is.EqualTo(new[]
        {
            ($"{TeaConstants.Weather.BeyondTime}", "apply"),
            ($"{TeaConstants.Weather.FairSkies}", "transition=0.5"),
        }));
    }
}
