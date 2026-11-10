namespace AnoMech.Tests;

// AnoMech loaded beside NoirMech: the list Dalamud gives of installed plugins, and what it bars.
public class AnoMechPresenceTests
{
    private static readonly InstalledPlugin AnoMechLoaded = new("AnoMech", true);
    private static readonly InstalledPlugin AnoMechInstalled = new("AnoMech", false);
    private static readonly InstalledPlugin NoirMechLoaded = new("NoirMech", true);
    private static readonly InstalledPlugin OtherLoaded = new("SomethingElse", true);

    [Test]
    public void ALoadedAnoMechRefusesRunsAndIsDetectedOnce()
    {
        var presence = new AnoMechPresence();

        Assert.That(presence.Update([NoirMechLoaded, AnoMechLoaded]), Is.True);
        Assert.That(presence.Update([NoirMechLoaded, AnoMechLoaded]), Is.False);
        Assert.That(presence.Refusal, Is.EqualTo(AnoMechPresence.LoadedRefusal));
        Assert.That(presence.StartBlock, Is.EqualTo(AnoMechPresence.LoadedStartBlock));
    }

    [Test]
    public void AnInstalledButUnloadedAnoMechBarsNothing()
    {
        var presence = new AnoMechPresence();

        Assert.That(presence.Update([NoirMechLoaded, AnoMechInstalled]), Is.False);
        Assert.That(presence.Refusal, Is.Null);
        Assert.That(presence.StartBlock, Is.Null);
    }

    [Test]
    public void OtherPluginsAndNoirMechItselfBarNothing()
    {
        var presence = new AnoMechPresence();

        Assert.That(presence.Update([NoirMechLoaded, OtherLoaded]), Is.False);
        Assert.That(presence.Update(null), Is.False);
        Assert.That(presence.Refusal, Is.Null);
    }

    // Unloading lifts the bar; loading it again is a new detection, with its own chat line.
    [Test]
    public void AnUnloadLiftsTheBarAndAReloadIsDetectedAgain()
    {
        var presence = new AnoMechPresence();
        presence.Update([AnoMechLoaded]);

        Assert.That(presence.Update([AnoMechInstalled]), Is.False);
        Assert.That(presence.Refusal, Is.Null);
        Assert.That(presence.Update([AnoMechLoaded]), Is.True);
        Assert.That(presence.Refusal, Is.Not.Null);
    }
}
