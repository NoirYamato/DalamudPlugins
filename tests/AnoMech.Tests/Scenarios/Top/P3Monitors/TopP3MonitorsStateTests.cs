using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top.P3Monitors;

namespace AnoMech.Tests;

public class TopP3MonitorsStateTests
{
    // A party-setup choice, carried as set; it draws nothing, so every roll is the seed's either way.
    [Test]
    public void AutomarkersIsCarriedAsSetAndRollsNothing()
    {
        Assert.That(new TopP3MonitorsStateOverrides().Automarkers, Is.EqualTo(Automarkers.On));
        var party = Party();
        Assert.Multiple(() =>
        {
            foreach (var seed in Enumerable.Range(1, 50))
            {
                var on = new TopP3MonitorsState(new Rng(seed), party, new TopP3MonitorsStateOverrides());
                var off = new TopP3MonitorsState(new Rng(seed), party, new TopP3MonitorsStateOverrides { Automarkers = Automarkers.Off });
                Assert.That(on.Automarkers, Is.EqualTo(Automarkers.On));
                Assert.That(off.Automarkers, Is.EqualTo(Automarkers.Off));
                Assert.That(off.OmegaMonitor, Is.EqualTo(on.OmegaMonitor), $"seed {seed}");
                Assert.That(off.Monitors.List, Is.EqualTo(on.Monitors.List), $"seed {seed}");
                Assert.That(off.Monitors.List.Select(off.SideOf), Is.EqualTo(on.Monitors.List.Select(on.SideOf)), $"seed {seed}");
            }
        });
    }

    private static SimParty Party()
    {
        SimParty? party = null;
        var run = ScenarioRun.Execute(typeof(TopP3MonitorsScenario), 0, 1, new ScenarioRunOptions
        {
            StopAt = 0.1f,
            Probe = p => party ??= p.World.Party,
        });
        Assert.That(run.Failure, Is.Null, run.ToString);
        return party!;
    }
}
