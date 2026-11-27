namespace AnoMech.Tests;

// A timeline run reads its outcome as ScenarioRun reads a scenario's: a mechanic SimWorld.Fail failed fails it, with
// nobody dead, whether the run then played on to its end or froze.
public class TimelineRunTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void AFailedMechanicFailsTheRun(bool freeze)
    {
        var run = TimelineRun.Execute(world => world.Events.Add(1f, () => world.Fail("a test failure", freeze)));

        Assert.That(run.Passed, Is.False, run.ToString);
        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(run.Deaths, Is.Empty, run.ToString);
        Assert.That(run.MechanicFailures.Select(f => f.Reason), Is.EqualTo(new[] { "a test failure" }));
        Assert.That(run.MechanicFailures[0].Time, Is.EqualTo(1f).Within(TimelineRun.FrameLate));
        Assert.That(run.ToString(), Does.Contain("mechanic failed at t=1.0").And.Contain("a test failure"));
    }

    [Test]
    public void ATimelineThatFailsNothingPasses()
    {
        var run = TimelineRun.Execute(world => world.Events.Add(1f, () => { }));

        Assert.That(run.Passed, run.ToString);
        Assert.That(run.MechanicFailures, Is.Empty);
    }
}
