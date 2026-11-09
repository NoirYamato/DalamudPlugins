using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Top;

namespace AnoMech.Tests;

// Runs one after another on one fake game with PracticeLoop watching, for what spans runs: the streak, Auto-restart,
// Retry, Drill and the seat rotation. Every run plays `timeline` on a stand-in scenario; Started holds each run's seed and
// the seat it gave the player, in start order.
internal sealed class PracticeRun : IDisposable
{
    public FakeGame Fake { get; }
    public Game Game { get; }
    public PracticeLoop Practice { get; }
    public ChatLog Chat { get; }
    public TimelineScenario Scenario { get; }
    public List<(int Seed, PartyRole Seat)> Started { get; } = [];

    public PracticeRun(Action<SimWorld> timeline, object? overrides = null, JobId job = JobId.Warrior)
    {
        Fake = FakeGame.Install();
        Fake.BattleCharas.Player.ClassJob = (byte)job;
        Game? game = null;
        DalamudServices.Install(nameof(Plugin.Log), TraceLog.Create(() => game?.World.Events.Elapsed ?? 0f));
        DalamudServices.Install(nameof(Plugin.Config), new Configuration());
        Chat = ChatLog.Create();
        DalamudServices.Install(nameof(Plugin.ChatGui), Chat);
        Game = game = new Game();
        DalamudServices.Install(nameof(Plugin.Game), Game);
        Practice = new PracticeLoop(Game);
        DalamudServices.Install(nameof(Plugin.Practice), Practice);
        Game.RunStarted += (_, _) => Started.Add((Game.World.Rng.Seed, Game.World.Party.PlayerRole));
        Scenario = new TimelineScenario(TopZone.P6, timeline, overrides: overrides);
    }

    // A local run of the stand-in, every seat filled. No seat leaves the player's to their job, no seed draws a fresh one.
    public RunScenarioParams Params(PartyRole? seat = null, int? seed = null)
        => new(Scenario, new LocalPartyProvider { RoleOverride = seat }, null, 0, seed);

    // A Start from inside the zone: the zone-in first, then the run on its own.
    public void StartInZone(RunScenarioParams p)
    {
        Game.ZoneIn(Scenario.Phase.Zone, SessionMode.Local);
        Game.StartScenario(p);
    }

    // Frames in Plugin.OnFrameworkUpdate's order until `done`.
    public void Until(Func<bool> done, float maxSeconds = 120f)
    {
        for (var elapsed = 0f; !done(); elapsed += ScenarioRun.FrameSeconds)
        {
            if (elapsed > maxSeconds) Assert.Fail($"still waiting after {maxSeconds} s; runs started: {Started.Count}");
            Fake.Frame(ScenarioRun.FrameSeconds, delta =>
            {
                Game.Tick(delta);
                Practice.Rolls.Tick();
            });
        }
    }

    public void Dispose() => Game.Dispose();

    // Nothing happens but an event at 1s: the run finishes clean about a second later.
    public static void Clean(SimWorld world) => world.Events.Add(1f, () => { });

    // The player dies at 1s; the run freezes 5s later.
    public static void PlayerDies(SimWorld world)
        => world.Events.Add(1f, () => world.Party.Player?.Die(SimCharacterDeathExtensions.Environment, "a test death"));
}
