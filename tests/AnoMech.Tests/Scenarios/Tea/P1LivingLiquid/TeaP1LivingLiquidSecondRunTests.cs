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

    // The pull twice, the player a human off tank that Provokes the Hand back at 38.0 itself. The first run is left at
    // 41.0: the Hand its own again, four dolls up and tethered, the melee walking theirs into the Living Liquid.
    [Test]
    public void ASecondPullStartsWithNoDollNoTetherAndTheHandOnItsFirstTarget()
    {
        var stage = new Stage { First = Pull, Last = Pull, HumanUntil = 60f, PlayerPressesItself = true };
        using var stay = new Stay(stage, OffTank);
        void ProvokeBack(ScenarioProbe p)
        {
            if (p.Crossed(38f))
                p.Fake.UserActions.Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, P1PlayerActionId.Provoke, (ulong)stay.Scenario.Fight!.Hand!.GameObjectId));
        }

        var first = stay.PlayThePull(until: 41f, ProvokeBack);
        var (firstRunsPresses, firstRunsFight) = (stay.Scenario.Presses!, stay.Scenario.Fight!);
        var second = stay.PlayThePull(until: 38.9f, ProvokeBack);
        firstRunsPresses.CatchUp();

        Assert.Multiple(() =>
        {
            Assert.That(first.AtTheStart, Is.EqualTo(new SeenOfThePull(Dolls: 0, Tethered: 0, Hand: null, HumanProvokes: 0)), "the first run's start");
            Assert.That(first.AtTheEnd, Is.EqualTo(new SeenOfThePull(Dolls: 4, Tethered: 4, Hand: OffTank, HumanProvokes: 1)), "the first run at 41.0");
            Assert.That(second.AtTheStart, Is.EqualTo(new SeenOfThePull(Dolls: 0, Tethered: 0, Hand: null, HumanProvokes: 0)), "the second run's start");
            Assert.That(second.AtTheEnd, Is.EqualTo(new SeenOfThePull(Dolls: 4, Tethered: 4, Hand: OffTank, HumanProvokes: 1)), "the second run at 38.9");
            Assert.That(firstRunsPresses.Provokes.Count(provoke => provoke.ByHuman), Is.EqualTo(1), "the first run's listener heard nothing of the second");
            Assert.That(firstRunsFight.Dolls.All.Count(doll => doll.Enemy.IsActive), Is.Zero, "the first run's dolls went with it");
            Assert.That(stay.Scenario.Fight, Is.Not.SameAs(firstRunsFight));
        });
    }

    // The Jagd Dolls standing in the world, how many of the run's own are tethered, whom the Hand is on, and the human's
    // Provokes the run's listener has reported.
    private sealed record SeenOfThePull(int Dolls, int Tethered, PartyRole? Hand, int HumanProvokes);

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

        // The same for the pull, with `each` called every frame for the human's own presses.
        public (SeenOfThePull AtTheStart, SeenOfThePull AtTheEnd) PlayThePull(float until, Action<ScenarioProbe> each)
        {
            game.StartScenario(new RunScenarioParams(Scenario, new LocalPartyProvider { RoleOverride = seat }, Stage.NoStrat, 0, ++runs));
            fake.Frame(ScenarioRun.FrameSeconds, game.Tick);
            Assert.That(game.Status, Is.EqualTo(SessionStatus.Running), $"run {runs} started");

            SeenOfThePull? atTheStart = null;
            var probe = new ScenarioProbe(game, fake, log, chat, () => []);
            while (probe.Time < until)
            {
                Assert.That(game.Status, Is.EqualTo(SessionStatus.Running), $"run {runs} at {probe.Time:F2}: {string.Join("; ", log.Warnings)}");
                fake.Frame(ScenarioRun.FrameSeconds, game.Tick);
                stage.Follow(probe);
                each(probe);
                if (probe.Crossed(ReadTheStartAt)) atTheStart = SeeThePull();
                probe.EndFrame();
            }
            return (atTheStart!, SeeThePull());
        }

        private SeenOfThePull SeeThePull()
        {
            var fight = Scenario.Fight!;
            return new SeenOfThePull(
                game.World.Children.OfType<SimEnemy>().Count(enemy => enemy.IsActive && enemy.BNpcBaseId == P1BNpcBaseId.JagdDoll),
                fight.Dolls.All.Count(doll => doll.TetheredAt is not null),
                fight.Hand is null ? null : (fight.Target(TeaP1Boss.LiquidHand) as ISimPartyMember)?.Role,
                Scenario.Presses!.Provokes.Count(provoke => provoke.ByHuman));
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
