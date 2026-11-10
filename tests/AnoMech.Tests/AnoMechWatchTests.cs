using AnoMech.Core.Game;
using AnoMech.Multiplayer;

namespace AnoMech.Tests;

// AnoMech found loaded beside NoirMech: one chat line each time, and a NoirMech stay already up ends at once, a peer's
// without waiting on its host.
public class AnoMechWatchTests
{
    private static readonly InstalledPlugin[] AnoMechLoaded = [new("NoirMech", true), new("AnoMech", true)];
    private static readonly InstalledPlugin[] AnoMechUnloaded = [new("NoirMech", true), new("AnoMech", false)];

    private static readonly string Found = $"[{Plugin.DisplayName}] {AnoMechPresence.LoadedRefusal}";
    private static readonly string FoundInAStay = $"[{Plugin.DisplayName}] {AnoMechPresence.LoadedRefusal} Leaving the sim zone.";

    [SetUp]
    [TearDown]
    public void ForgetAnoMech() => DalamudServices.Install(nameof(Plugin.AnoMechPresence), new AnoMechPresence());

    private static AnoMechWatch Watch(PracticeRun run, MultiplayerManager? multiplayer = null)
    {
        multiplayer ??= new MultiplayerManager();
        return new AnoMechWatch(run.Game, multiplayer, new RunController(run.Game, multiplayer));
    }

    [Test]
    public void AnoMechIsReportedOnceEachTimeItLoads()
    {
        using var run = new PracticeRun(PracticeRun.Clean);
        var watch = Watch(run);

        watch.Check(AnoMechLoaded);
        watch.Check(AnoMechLoaded);
        watch.Check(AnoMechUnloaded);
        watch.Check(AnoMechLoaded);

        Assert.That(run.Chat.Lines, Is.EqualTo(new[] { Found, Found }));
        Assert.That(run.Game.Zone, Is.Null);
    }

    [Test]
    public void ALocalStayEndsWhenAnoMechLoads()
    {
        using var run = new PracticeRun(PracticeRun.Clean);
        var watch = Watch(run);
        run.StartInZone(run.Params());
        run.Until(() => run.Started.Count == 1);

        var before = run.Chat.Lines.Count;
        watch.Check(AnoMechLoaded);
        run.Until(() => run.Game.Zone is null, maxSeconds: 1f);

        Assert.That(run.Chat.Lines.Skip(before), Is.EqualTo(new[] { FoundInAStay }));
        Assert.That(run.Started, Has.Count.EqualTo(1));
    }

    // A peer's Leave would only ask the host to end the run, and its own stay would stay up until the host answered.
    [Test]
    public void APeerLeavesItsSessionAndItsStayAtOnce()
    {
        using var run = new PracticeRun(PracticeRun.Clean);
        var relay = new TestRelay(1, _ => TestRelay.Perfect);
        var multiplayer = new MultiplayerManager { RelayFactory = relay.Connect, Time = relay.Time };
        multiplayer.JoinSession(TestRelay.Address, "TESTRM");
        run.Game.ZoneIn(run.Scenario.Phase.Zone, SessionMode.Peer);
        run.Until(() => run.Game.Zone is not null);
        try
        {
            Watch(run, multiplayer).Check(AnoMechLoaded);
            run.Until(() => run.Game.Zone is null, maxSeconds: 1f);

            Assert.That(multiplayer.InSession, Is.False);
            Assert.That(run.Chat.Lines, Is.EqualTo(new[] { FoundInAStay }));
        }
        finally
        {
            multiplayer.LeaveSession();
        }
    }
}
