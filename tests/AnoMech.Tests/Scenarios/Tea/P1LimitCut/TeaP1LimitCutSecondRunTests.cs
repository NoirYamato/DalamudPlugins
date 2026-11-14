using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Tea.P1LimitCut;
using FFXIVClientStructs.FFXIV.Client.Game;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using Stage = AnoMech.Tests.TeaP1LimitCutStage;

namespace AnoMech.Tests;

// The plugin keeps one scenario instance for the session, so a second Start, a Retry, Auto-restart and Drill all play
// it again, on the same world; every other test plays a fresh instance once. Here the catalog's own instance plays
// twice in one stay, on the strat-free stage (TeaP1LimitCutStage), the player a human #2 (R1), charged on the north
// spot at 16.9 and left where that leaves it.
public class TeaP1LimitCutSecondRunTests
{
    private const float ReadAt = 17.6f;

    [Test]
    public void ASecondRunShowsCruiseChaserAgainAndForgetsTheFirstRunsPress()
    {
        var stage = new Stage { HumanUntil = 16.5f };
        using var stay = new Stay(stage, PhysRangedDps);

        var pressing = stay.Play(pressAt: 14.9f);
        var thrown = stay.Play(pressAt: null);

        Assert.Multiple(() =>
        {
            Assert.That(pressing.WarpIns, Is.EqualTo(new[] { 14.9f }).Within(0.05f), "the first run's warp-in");
            Assert.That(Vector2.Distance(pressing.Stands, stage.HitSpot(2)), Is.LessThan(0.05f), $"pressing, the human stands at {pressing.Stands}");
            Assert.That(thrown.WarpIns, Is.EqualTo(new[] { 14.9f }).Within(0.05f), "the second run's warp-in");
            Assert.That(thrown.Stands.Length(), Is.EqualTo(P1Geometry.ArenaRadius).Within(0.05f), $"not pressing, the human stands at {thrown.Stands}");
        });
    }

    // One fake game and one zone-in, as ScenarioRun sets a run up, with the player a human from the first frame.
    private sealed class Stay : IDisposable
    {
        private readonly Stage stage;
        private readonly PartyRole seat;
        private readonly FakeGame fake;
        private readonly TraceLog log;
        private readonly ChatLog chat;
        private readonly Game game;
        private readonly TeaP1LimitCutScenario scenario;
        private int runs;

        public Stay(Stage stage, PartyRole seat)
        {
            this.stage = stage;
            this.seat = seat;
            fake = FakeGame.Install();
            fake.BattleCharas.Player.ClassJob = PartyPresets.Standard[(int)seat].ClassJob;
            Game? starting = null;
            log = TraceLog.Create(() => starting?.World.Events.Elapsed ?? 0f);
            fake.Timeline.Clock = () => starting?.World.Events.Elapsed ?? 0f;
            fake.Timeline.Source = () => log.LastFired;
            DalamudServices.Install(nameof(Plugin.Log), log);
            DalamudServices.Install(nameof(Plugin.Config), new Configuration());
            chat = ChatLog.Create();
            DalamudServices.Install(nameof(Plugin.ChatGui), chat);
            game = starting = new Game();
            DalamudServices.Install(nameof(Plugin.Game), game);
            scenario = game.Catalog.Scenarios.OfType<TeaP1LimitCutScenario>().Single();
            stage.Pin((TeaP1LimitCutStateOverrides)scenario.SettingsOverrides);
            game.ZoneIn(scenario.Phase.Zone, SessionMode.Local);
        }

        // Starts the scenario, over the run before it if that is still going, and plays it until ReadAt.
        public (float[] WarpIns, Vector2 Stands) Play(float? pressAt)
        {
            var rowsBefore = fake.Timeline.Rows.Count;
            game.StartScenario(new RunScenarioParams(scenario, new LocalPartyProvider { RoleOverride = seat }, Stage.NoStrat, 0, ++runs));
            fake.Frame(ScenarioRun.FrameSeconds, game.Tick);
            Assert.That(game.Status, Is.EqualTo(SessionStatus.Running), $"run {runs} started");

            var probe = new ScenarioProbe(game, fake, log, chat, () => []);
            while (probe.Time < ReadAt)
            {
                Assert.That(game.Status, Is.EqualTo(SessionStatus.Running), $"run {runs} at {probe.Time:F2}: {string.Join("; ", log.Warnings)}");
                fake.Frame(ScenarioRun.FrameSeconds, game.Tick);
                stage.Follow(probe);
                if (pressAt is { } at && probe.Crossed(at))
                    fake.UserActions.Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, P1PlayerActionId.ArmsLength, 0));
                probe.EndFrame();
            }

            var cruiseChaser = TeaP1LimitCutScenarioTests.CruiseChaser(probe).EntityId.ToString("X8");
            var warpIns = fake.Timeline.Rows.Skip(rowsBefore)
                .Where(row => row["kind"] == "ac" && row["actor"] == cruiseChaser && row["id"] == "0x197"
                              && Convert.ToUInt32(row["p"].Split(',')[0], 16) == ActionTimelineId.WarpEnd)
                .Select(row => float.Parse(row["t"], CultureInfo.InvariantCulture))
                .ToArray();
            var position = probe.Member(seat)!.Position;
            return (warpIns, new Vector2(position.X, position.Z));
        }

        public void Dispose() => game.Dispose();
    }
}
