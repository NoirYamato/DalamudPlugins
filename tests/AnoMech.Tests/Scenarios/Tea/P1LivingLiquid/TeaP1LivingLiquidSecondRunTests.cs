using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Tea;
using AnoMech.Scenarios.Tea.P1LivingLiquid;
using FFXIVClientStructs.FFXIV.Client.Game;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.P1LivingLiquid.TeaP1LivingLiquidPart;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using Stage = AnoMech.Tests.TeaP1LivingLiquidStage;

namespace AnoMech.Tests;

// The plugin keeps one scenario instance for the session, so a second Start, a Retry, Auto-restart and Drill all play
// it again, on the same world; every other test plays a fresh instance once. Here the catalog's own instance plays
// Cascade 2 twice in one stay, on the strat-free stage (TeaP1LivingLiquidStage), the player a human H1 that presses one
// Esuna of its own, on itself at 11.5, beside the six the stage's healers make by 16.2. The first run is left at 34.0,
// mid-fight: its tornadoes up, the Hand showing its palm, two Emboli out.
public class TeaP1LivingLiquidSecondRunTests
{
    [Test]
    public void ASecondRunStartsCleanAndHearsEachPressOnce()
    {
        var stage = new Stage { First = Cascade2, HumanUntil = 60f };
        using var stay = new Stay(stage, RegenHealer);

        var first = stay.Play(until: 34f);
        var firstRunsPresses = stay.Scenario.Presses!;
        var second = stay.Play(until: 12f);
        // Whatever the first run's listener still heard of the second run's press would settle now.
        firstRunsPresses.CatchUp();

        Assert.Multiple(() =>
        {
            Assert.That(first.AtTheStart, Is.EqualTo(new Seen(Tornadoes: 0, Puddles: 0, Emboli: 0, Locked: false, Palm: null, Throttled: 0, Cleanses: 0)), "the first run's start");
            Assert.That(first.AtTheEnd, Is.EqualTo(new Seen(Tornadoes: 3, Puddles: 3, Emboli: 2, Locked: false, Palm: true, Throttled: 0, Cleanses: 7)), "the first run at 34.0");
            Assert.That(second.AtTheStart, Is.EqualTo(new Seen(Tornadoes: 0, Puddles: 0, Emboli: 0, Locked: false, Palm: null, Throttled: 0, Cleanses: 0)), "the second run's start");
            Assert.That(second.AtTheEnd, Is.EqualTo(new Seen(Tornadoes: 3, Puddles: 3, Emboli: 0, Locked: false, Palm: null, Throttled: 4, Cleanses: 3)), "the second run at 12.0");
            Assert.That(firstRunsPresses.Cleanses, Has.Count.EqualTo(7), "the first run's listener heard nothing of the second");
            Assert.That(stay.Scenario.Presses, Is.Not.SameAs(firstRunsPresses));
        });
    }

    // What a run holds: the Liquid Rages standing in the world, the puddles bots walk round, the Emboli out, and the
    // boss piece's own state.
    private sealed record Seen(int Tornadoes, int Puddles, int Emboli, bool Locked, bool? Palm, int Throttled, int Cleanses);

    // One fake game and one zone-in, as ScenarioRun sets a run up, with the player a human from the first frame.
    private sealed class Stay : IDisposable
    {
        private const float PressAt = 11.5f;
        private const float ReadTheStartAt = 1f;

        private readonly Stage stage;
        private readonly PartyRole seat;
        private readonly FakeGame fake;
        private readonly TraceLog log;
        private readonly ChatLog chat;
        private readonly Game game;
        private int runs;

        public TeaP1LivingLiquidScenario Scenario { get; }

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
            Scenario = game.Catalog.Scenarios.OfType<TeaP1LivingLiquidScenario>().Single();
            stage.Pin((TeaP1LivingLiquidStateOverrides)Scenario.SettingsOverrides);
            game.ZoneIn(Scenario.Phase.Zone, SessionMode.Local);
        }

        // Starts the scenario, over the run before it if that is still going, and plays it until `until`.
        public (Seen AtTheStart, Seen AtTheEnd) Play(float until)
        {
            game.StartScenario(new RunScenarioParams(Scenario, new LocalPartyProvider { RoleOverride = seat }, Stage.NoStrat, 0, ++runs));
            fake.Frame(ScenarioRun.FrameSeconds, game.Tick);
            Assert.That(game.Status, Is.EqualTo(SessionStatus.Running), $"run {runs} started");

            Seen? atTheStart = null;
            var probe = new ScenarioProbe(game, fake, log, chat, () => []);
            while (probe.Time < until)
            {
                Assert.That(game.Status, Is.EqualTo(SessionStatus.Running), $"run {runs} at {probe.Time:F2}: {string.Join("; ", log.Warnings)}");
                fake.Frame(ScenarioRun.FrameSeconds, game.Tick);
                stage.Follow(probe);
                if (probe.Crossed(ReadTheStartAt)) atTheStart = See();
                if (probe.Crossed(PressAt))
                    fake.UserActions.Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, P1PlayerActionId.Esuna, (ulong)probe.Member(seat)!.GameObjectId));
                probe.EndFrame();
            }
            return (atTheStart!, See());
        }

        private Seen See()
        {
            var fight = Scenario.Fight!;
            var enemies = game.World.Children.OfType<SimEnemy>().Where(enemy => enemy.IsActive).ToList();
            return new Seen(
                enemies.Count(enemy => enemy.BNpcBaseId == P1BNpcBaseId.LiquidRage),
                game.World.Obstacles.All.Count,
                enemies.Count(enemy => enemy.BNpcBaseId == P1BNpcBaseId.Embolus),
                fight.FacingLocked, fight.HandShowsAPalm, fight.Throttled.Count, Scenario.Presses!.Cleanses.Count);
        }

        public void Dispose() => game.Dispose();
    }
}
