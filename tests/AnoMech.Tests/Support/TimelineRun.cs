using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Top;

namespace AnoMech.Tests;

internal sealed record TimelineRunOptions
{
    public PartyRole PlayerRole { get; init; } = PartyRole.MainTank;

    // The player presses their own buttons: hits check their mitigation, and no bot acts for their seat.
    public bool HumanPlayer { get; init; }

    // Hits check a human player's mitigation only with the UserActions module on.
    public bool UserActionsEnabled { get; init; }

    public Action<Configuration>? Configure { get; init; }

    // Deaths are only reported in chat, not dealt, so the run plays to its end.
    public bool GodMode { get; init; }

    public int Seed { get; init; } = 1;

    public IPhase Phase { get; init; } = TopZone.P6;
}

// One cast or ActionEffect the sim sent: who sent it, from or at which scenario-local spot, facing where, at whom, and
// (an ActionEffect) the entity ids it hit.
internal sealed record SentAction(float Time, string Kind, string Actor, uint ActionId, Vector3 Position, float Rotation, string Target,
    IReadOnlyList<string> Hits);

// Who died, and every packet-shaped call the sim made on the fakes (SimTimeline's rows, world coordinates).
internal sealed record TimelineRunResult(
    IReadOnlyList<Death> Deaths, string? Failure, IReadOnlyList<string> Warnings,
    IReadOnlyList<Dictionary<string, string>> Rows, IReadOnlyList<string> Chat, float Elapsed, Vector3 Origin)
{
    public bool Passed => Failure is null && Deaths.Count == 0;

    // "cast" rows are bars starting, "ability" rows ActionEffects; in time order.
    public IReadOnlyList<SentAction> Sent(string kind, params uint[] actionIds)
        => Rows.Where(row => row["kind"] == kind && actionIds.Any(id => row["id"] == id.ToString()))
               .Select(row => new SentAction(
                   Float(row["t"]), kind, row["actor"], uint.Parse(row["id"]),
                   new Vector3(Float(row["x"]) - Origin.X, 0f, Float(row["z"]) - Origin.Z),
                   Float(row["rot"]), row["target"],
                   row.GetValueOrDefault("targets", "").Split(' ', StringSplitOptions.RemoveEmptyEntries)))
               .ToList();

    // The entity ids of what was spawned of `bnpcBaseId`, in spawn order.
    public IReadOnlyList<string> Spawned(uint bnpcBaseId)
        => Rows.Where(row => row["kind"] == "spawn" && row["id"] == bnpcBaseId.ToString()).Select(row => row["actor"]).ToList();

    private static float Float(string value) => float.Parse(value, CultureInfo.InvariantCulture);

    public override string ToString()
    {
        var lines = new List<string> { $"timeline run (t={Elapsed:F2}):" };
        if (Failure is not null) lines.Add($"  {Failure}");
        lines.AddRange(Deaths.Select(d => $"  {d.Role} died at t={d.Time:F2}: {d.Cause}"));
        lines.AddRange(Warnings.Distinct().Select(w => $"  warning: {w}"));
        return string.Join(Environment.NewLine, lines);
    }
}

// A headless run of a timeline the test writes on a stand-in scenario (TimelineScenario), for engine and family pieces
// no catalog scenario plays on their own. Every seat is filled; nobody moves unless the timeline moves them. The run
// ends as a scenario's does: once its events run dry, or a death freezes it.
internal static class TimelineRun
{
    // How late a row can be stamped: an event fires on the first frame at or after its time, a cast's ActionEffect is
    // timed from the frame its cast went out, and rows carry three decimals.
    public const float FrameLate = 2 * ScenarioRun.FrameSeconds + 0.001f;

    public static TimelineRunResult Execute(Action<SimWorld> timeline, TimelineRunOptions? options = null)
    {
        options ??= new TimelineRunOptions();
        var fake = FakeGame.Install();
        fake.UserActions.Enabled = options.UserActionsEnabled;
        Game? game = null;
        var log = TraceLog.Create(() => game?.World.Events.Elapsed ?? 0f);
        fake.Timeline.Clock = () => game?.World.Events.Elapsed ?? 0f;
        fake.Timeline.Source = () => log.LastFired;
        fake.BattleCharas.Player.ClassJob = PartyPresets.Standard[(int)options.PlayerRole].ClassJob;
        DalamudServices.Install(nameof(Plugin.Log), log);
        var config = new Configuration();
        options.Configure?.Invoke(config);
        DalamudServices.Install(nameof(Plugin.Config), config);
        var chat = ChatLog.Create();
        DalamudServices.Install(nameof(Plugin.ChatGui), chat);
        DebugBotControl.Enabled = !options.HumanPlayer;
        game = new Game { GodMode = options.GodMode };
        DalamudServices.Install(nameof(Plugin.Game), game);
        var deaths = new List<Death>();
        string? failure = null;
        try
        {
            game.PartyMemberKilled += (role, cause, actionId) => deaths.Add(new Death(role, cause, actionId, game.World.Events.Elapsed,
                WorldSnapshot.Describe(game.World, [], game.World.Party.Get(role), includeHidden: true)));
            var scenario = new TimelineScenario(options.Phase, timeline);
            game.ZoneIn(scenario.Phase.Zone, SessionMode.Local);
            game.StartScenario(new RunScenarioParams(scenario, new LocalPartyProvider { RoleOverride = options.PlayerRole }, null, 0, options.Seed));
            fake.Frame(ScenarioRun.FrameSeconds, game.Tick);
            if (game.Status < SessionStatus.Running)
                failure = "did not start";
            var elapsed = 0f;
            while (failure is null && game.Status is not (SessionStatus.Succeeded or SessionStatus.CompletedWithMistake or SessionStatus.Frozen))
            {
                if (elapsed >= ScenarioRun.TimeoutSeconds)
                {
                    failure = $"not finished after {ScenarioRun.TimeoutSeconds} s";
                    break;
                }
                fake.Frame(ScenarioRun.FrameSeconds, game.Tick);
                elapsed += ScenarioRun.FrameSeconds;
            }
        }
        catch (Exception e)
        {
            failure = $"threw at t={game.World.Events.Elapsed:F2}: {e}";
        }
        var result = new TimelineRunResult(deaths, failure, log.Warnings.ToList(), fake.Timeline.Rows.ToList(), chat.Lines.ToList(),
            game.World.Events.Elapsed, options.Phase.Zone.Origin);
        DebugBotControl.Enabled = false;
        game.Dispose();
        return result;
    }

    // Stops `role` and puts it at (x, z), scenario-local.
    public static void Place(SimWorld world, PartyRole role, float x, float z)
    {
        if (world.Party.Get(role) is not { } member) return;
        member.StopMoving();
        member.SetPosition(new Vector3(x, 0f, z));
    }

    // Everyone at once, each on their own spot.
    public static void Place(SimWorld world, IReadOnlyDictionary<PartyRole, Vector2> spots)
    {
        foreach (var (role, spot) in spots)
            Place(world, role, spot.X, spot.Y);
    }

    // Each seat's entity id as rows write it, to read who an ActionEffect hit.
    public static Dictionary<string, PartyRole> Seats(SimWorld world)
        => Enum.GetValues<PartyRole>()
               .Where(role => world.Party.Get(role) is not null)
               .ToDictionary(role => world.Party.Get(role)!.EntityId.ToString("X8"), role => role);
}
