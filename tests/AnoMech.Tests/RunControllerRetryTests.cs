using System.Reflection;
using AnoMech.Core.Game;
using AnoMech.Multiplayer;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;
using AnoMech.Scenarios.Ucob;
using Dalamud.Plugin.Services;

namespace AnoMech.Tests;

// "Retry this roll" and /noir retry: refused with a chat line, as Pause is, before anything zones in. Past its own
// refusals Retry asks Start's, whose last gate (ZoneControl's) can't pass headless: it says "not logged in" here.
public class RunControllerRetryTests
{
    private const string NotLoggedIn = "Cannot start: not logged in.";

    public sealed class Knobs
    {
        public int Value { get; set; } = 1;
    }

    [SetUp]
    public void LogOut() => DalamudServices.Install(nameof(Plugin.ClientState), DispatchProxy.Create<IClientState, DalamudServices.NullService>());

    [TearDown]
    public void Restore()
    {
        DalamudServices.Install(nameof(Plugin.ClientState), null!);
        DalamudServices.Install(nameof(Plugin.AnoMechPresence), new AnoMechPresence());
    }

    private static string Chat(string line) => $"[{Plugin.DisplayName}] {line}";

    // The chat lines `act` and the 3s after it print.
    private static List<string> ChatFrom(PracticeRun run, Action act)
    {
        var before = run.Chat.Lines.Count;
        act();
        run.For(3f);
        return run.Chat.Lines.Skip(before).ToList();
    }

    private static void PlayOneRun(PracticeRun run)
    {
        run.StartInZone(run.Params(strat: 0));
        run.Until(() => run.Game.Status == SessionStatus.Succeeded);
    }

    [Test]
    public void RetryBeforeAnyRunIsRefused()
    {
        using var run = new PracticeRun(PracticeRun.Clean);
        var runs = new RunController(run.Game, new MultiplayerManager());

        Assert.That(ChatFrom(run, runs.Retry), Is.EqualTo(new[] { Chat("No run to retry yet.") }));
    }

    // The roll plays in its own zone, and EnterAndStart would only find that out after the click.
    [Test]
    public void RetryInAnotherZonesStayIsRefused()
    {
        using var run = new PracticeRun(PracticeRun.Clean);
        var runs = new RunController(run.Game, new MultiplayerManager());
        PlayOneRun(run);
        run.Game.Leave();
        run.Until(() => run.Game.Zone is null);
        run.Game.ZoneIn(UcobZone.Instance, SessionMode.Local);
        run.Until(() => run.Game.Zone is not null);

        Assert.That(ChatFrom(run, runs.Retry), Is.EqualTo(new[] { Chat($"Leave {UcobZone.Instance.Name} first: the roll plays in {TopZone.Instance.Name}.") }));
        Assert.That(run.Started, Has.Count.EqualTo(1));
        Assert.That(run.Game.Zone, Is.SameAs(UcobZone.Instance));
    }

    [Test]
    public void RetryWhileAnoMechIsLoadedIsRefused()
    {
        using var run = new PracticeRun(PracticeRun.Clean);
        var runs = new RunController(run.Game, new MultiplayerManager());
        PlayOneRun(run);
        var presence = new AnoMechPresence();
        presence.Update([new InstalledPlugin("AnoMech", true)]);
        DalamudServices.Install(nameof(Plugin.AnoMechPresence), presence);

        Assert.That(ChatFrom(run, runs.Retry), Is.EqualTo(new[] { Chat(AnoMechPresence.LoadedRefusal) }));
        Assert.That(run.Started, Has.Count.EqualTo(1));
    }

    // Back in the inn and hosting a lobby: the stand-in doesn't support multiplayer, so Start's own check for a session
    // passes it, and no stay is up to say it is a session's.
    [Test]
    public void RetryInASessionLobbyIsRefusedAndZonesNowhere()
    {
        using var run = new PracticeRun(PracticeRun.Clean);
        run.Game.EnterAndStart(run.Params(strat: 0), SessionMode.Local);
        run.Until(() => run.Game.Status == SessionStatus.Succeeded);
        run.Game.Leave();
        run.Until(() => run.Game.Zone is null);
        var relay = new TestRelay(1, _ => TestRelay.Perfect);
        var multiplayer = new MultiplayerManager { RelayFactory = relay.Connect, Time = relay.Time };
        multiplayer.HostSession(TestRelay.Address);
        try
        {
            Assert.That(multiplayer.InSession && run.Game.Mode is null && !((IScenario)run.Scenario).SupportsMultiplayer);

            Assert.That(ChatFrom(run, new RunController(run.Game, multiplayer).Retry), Is.EqualTo(new[] { Chat(RollReplay.MultiplayerRefusal) }));
            Assert.That(run.Game.Zone, Is.Null);
            Assert.That(run.Started, Has.Count.EqualTo(1));
        }
        finally
        {
            multiplayer.LeaveSession();
        }
    }

    // The replay puts the run's own settings back before it starts, so a conflict only the live ones have passes: Retry
    // goes on to Start's next gate.
    [Test]
    public void AConflictOnlyTheLiveSettingsHaveDoesNotRefuseRetry()
    {
        var knobs = new Knobs();
        using var run = new PracticeRun(PracticeRun.Clean, knobs, conflicts: () => knobs.Value == 2 ? ["Two can't be played."] : []);
        var runs = new RunController(run.Game, new MultiplayerManager());
        PlayOneRun(run);

        knobs.Value = 2;

        Assert.That(run.Scenario.SettingsConflicts, Is.Not.Empty);
        Assert.That(runs.RetryRefusal, Is.EqualTo(NotLoggedIn));
    }

    // A conflict the run's own settings have refuses: the P6 Gauntlet's module check, say, which no setting fixes.
    [Test]
    public void AConflictTheRunsOwnSettingsHaveRefusesRetry()
    {
        var moduleOff = false;
        using var run = new PracticeRun(PracticeRun.Clean, new Knobs(), conflicts: () => moduleOff ? ["The module is off."] : []);
        var runs = new RunController(run.Game, new MultiplayerManager());
        PlayOneRun(run);

        moduleOff = true;

        Assert.That(runs.RetryRefusal, Is.EqualTo("The module is off."));
        Assert.That(ChatFrom(run, runs.Retry), Is.EqualTo(new[] { Chat("The module is off.") }));
    }
}
