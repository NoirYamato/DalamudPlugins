using AnoMech.Core.EnemyActions;

namespace AnoMech.Tests;

// A cast decides its hits as its bar ends, CastSpec.ActionEffectOffset before its ActionEffect; an instant does both
// at once. 31661 is a 12 s cast, 31662 an instant.
public class EnemyActionTimesTests
{
    [SetUp]
    public void InstallGame() => FakeGame.Install();

    [Test]
    public void ACastResolvesAsItsBarEndsAndReleasesAtTheSheetsCastTime()
    {
        var cast = new EnemyAction(31661);

        Assert.That(cast.ResolvesAfter(), Is.EqualTo(11.7f).Within(1e-4f));
        Assert.That(cast.ReleasesAfter(), Is.EqualTo(12f).Within(1e-4f));
    }

    [Test]
    public void AnInstantResolvesAndReleasesAtOnce()
    {
        var instant = new EnemyAction(31662);

        Assert.That(instant.ResolvesAfter(), Is.EqualTo(0f));
        Assert.That(instant.ReleasesAfter(), Is.EqualTo(0f));
    }

    [Test]
    public void AnOverriddenBarAndALaterSnapshotMoveBoth()
    {
        var action = new EnemyAction(31661)
        {
            Cast = new() { CastSecondsOverride = 5f },
            Timing = new() { ResolveSnapshotOffset = 0.2f },
        };

        Assert.That(action.ResolvesAfter(), Is.EqualTo(5.2f).Within(1e-4f));
        Assert.That(action.ReleasesAfter(), Is.EqualTo(5.3f).Within(1e-4f));
    }
}
