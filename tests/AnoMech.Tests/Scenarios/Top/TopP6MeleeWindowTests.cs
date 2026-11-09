using System.Reflection;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Top;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Top.TopConstants;

namespace AnoMech.Tests;

// The human M1, a DRG, held to a melee limit break 3 window from 2.0 to 6.0: Dragonsong Dive (4.5 s) started in it at
// Alpha Omega and resolved fills it; otherwise the human dies as it closes, or at the interruption if that is later.
// Every death states the window's rule, then what went wrong with the human's own press, if they made one.
public class TopP6MeleeWindowTests
{
    private const float Opens = 2f;
    private const float Closes = 6f;

    private static uint LimitBreak(PartyRole seat, int level) => LimitBreakHandler.ActionId(PartyPresets.Standard[(int)seat].ClassJob, level);

    private static string Died(string why = "") => TopP6LimitBreaks.FirstDiveMeleeRule + why;

    [SetUp]
    public void InstallGame() => FakeGame.Install();

    [TestCase(2.1f)]
    [TestCase(5.9f)]
    public void OneStartedInTheWindowFillsIt(float pressAt)
    {
        var run = Window((world, alphaOmega, _) => TopP6HumanLimitBreak.Press(world, pressAt, LimitBreak(MeleeDpsA, 3), () => alphaOmega.Enemy));

        Assert.That(run.Passed, run.ToString);
    }

    [Test]
    public void OneStartedTooLateKillsAsTheWindowCloses()
    {
        var run = Window((world, alphaOmega, _) => TopP6HumanLimitBreak.Press(world, 6.2f, LimitBreak(MeleeDpsA, 3), () => alphaOmega.Enemy));

        AssertDied(run, Closes, Died());
    }

    [Test]
    public void NoneKillsAsTheWindowCloses()
        => AssertDied(Window((_, _, _) => { }), Closes, Died());

    [Test]
    public void OneAimedElsewhereDoesntFillIt()
    {
        var run = Window((world, _, _) => TopP6HumanLimitBreak.Press(world, 3f, LimitBreak(MeleeDpsA, 3), () => world.Party.Get(MainTank)));

        AssertDied(run, Closes, Died(TopP6LimitBreaks.YoursMissedAlphaOmega));
    }

    [Test]
    public void OneInterruptedInsideTheWindowKillsAsItCloses()
    {
        var run = Window((world, alphaOmega, _) => TopP6HumanLimitBreak.Press(world, 3f, LimitBreak(MeleeDpsA, 3), () => alphaOmega.Enemy, interruptedAfter: 1f));

        AssertDied(run, Closes, Died(TopP6LimitBreaks.YoursWasCutOff));
    }

    // Started at 5.5 and cut at 7.0, after the window closed: the death waits for the interruption.
    [Test]
    public void OneInterruptedAfterTheWindowClosedKillsThen()
    {
        var run = Window((world, alphaOmega, _) => TopP6HumanLimitBreak.Press(world, 5.5f, LimitBreak(MeleeDpsA, 3), () => alphaOmega.Enemy, interruptedAfter: 1.5f));

        AssertDied(run, 7f, Died(TopP6LimitBreaks.YoursWasCutOff));
    }

    // Cut short, then pressed again inside the window: the second fills it.
    [Test]
    public void APressAgainAfterAnInterruptionStillFillsIt()
    {
        var run = Window((world, alphaOmega, _) =>
        {
            TopP6HumanLimitBreak.Press(world, 2.5f, LimitBreak(MeleeDpsA, 3), () => alphaOmega.Enemy, interruptedAfter: 0.5f);
            TopP6HumanLimitBreak.Press(world, 3.5f, LimitBreak(MeleeDpsA, 3), () => alphaOmega.Enemy);
        });

        Assert.That(run.Passed, run.ToString);
    }

    [Test]
    public void ALowerLimitBreakDoesntFillIt()
    {
        var run = Window((world, alphaOmega, _) => TopP6HumanLimitBreak.Press(world, 3f, LimitBreak(MeleeDpsA, 2), () => alphaOmega.Enemy));

        AssertDied(run, Closes, Died(string.Format(TopP6LimitBreaks.YoursWasNotALimitBreak3, 2)));
    }

    [Test]
    public void OneStartedBeforeTheWindowOpensDoesntFillIt()
    {
        var run = Window((world, alphaOmega, _) => TopP6HumanLimitBreak.Press(world, 1f, LimitBreak(MeleeDpsA, 3), () => alphaOmega.Enemy));

        AssertDied(run, Closes, Died(TopP6LimitBreaks.YoursStartedTooEarly));
    }

    // A window that opens on another seat's melee limit break 3: M2's bot press at 1.0 resolves at 5.5 and opens it, until
    // 9.0. A press at 0.8 goes ahead of the bot's, which waits for it and then for a gauge it spent: the window never opens.
    [TestCase(6f, true)]
    [TestCase(0.8f, false)]
    public void AWindowOpenedByAnotherSeatsPress(float pressAt, bool lives)
    {
        var run = TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () =>
            {
                limitBreaks.SetGauge(3f);
                world.Party.Get(MeleeDpsB)!.AddStatus(StatusId.BrilliantDynamis);
            });
            limitBreaks.HoldTheHumanTo(new TopP6MeleeWindow(MeleeDpsA, Opens, 9f, TopP6LimitBreaks.SecondDiveMeleeRule, AfterAnotherMeleeLimitBreak3: true),
                () => alphaOmega.Enemy);
            world.Events.Add(1f, () => limitBreaks.Press(MeleeDpsB, PartyPresets.Standard[(int)MeleeDpsB].ClassJob, alphaOmega.Enemy));
            TopP6HumanLimitBreak.Press(world, pressAt, LimitBreak(MeleeDpsA, 3), () => alphaOmega.Enemy);
            world.Events.Add(12f, () => { });
        }, Human);

        if (lives) Assert.That(run.Passed, run.ToString);
        else AssertDied(run, 9f, TopP6LimitBreaks.SecondDiveMeleeRule + TopP6LimitBreaks.YoursStartedTooEarly);
    }

    // A client may report a press aimed at its "current target" stand-in (0xE0000000) rather than at the target's own id:
    // the run reads it as aimed at the player's target as it hears it, here Alpha Omega, or nothing.
    [TestCase(true)]
    [TestCase(false)]
    public void OneAimedAtTheClientsCurrentTargetIsAimedAtThePlayersTarget(bool targetingAlphaOmega)
    {
        const ulong CurrentTarget = 0xE0000000;
        var leftInstalled = Plugin.TargetManager;
        var targetManager = DispatchProxy.Create<ITargetManager, TargetStandIn>();
        DalamudServices.Install(nameof(Plugin.TargetManager), targetManager);
        try
        {
            var run = Window((world, alphaOmega, _) => world.Events.Add(3f, () =>
            {
                ((TargetStandIn)(object)targetManager).Target = targetingAlphaOmega ? (ulong)alphaOmega.Enemy!.GameObjectId : null;
                var userActions = (FakeUserActions)Natives.UserActions;
                var limitBreak3 = LimitBreak(MeleeDpsA, 3);
                userActions.Act(new PlayerAction(PlayerActionStep.CastStarted, ActionType.Action, limitBreak3, CurrentTarget, 4.5f));
                world.Events.Add(4.5f, () => userActions.Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, limitBreak3, CurrentTarget)));
            }));

            if (targetingAlphaOmega) Assert.That(run.Passed, run.ToString);
            else AssertDied(run, Closes, Died(TopP6LimitBreaks.YoursMissedAlphaOmega));
        }
        finally
        {
            DalamudServices.Install(nameof(Plugin.TargetManager), leftInstalled);
        }
    }

    // Dalamud's target manager, and the object it targets, down to that object's GameObjectId.
    public class TargetStandIn : DispatchProxy
    {
        public ulong? Target { get; set; }

        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "get_Target" => Target is { } id ? Targeting(id) : null,
            "get_GameObjectId" => Target ?? 0ul,
            _ => method is { ReturnType.IsValueType: true } && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null,
        };

        private static IGameObject Targeting(ulong id)
        {
            var target = DispatchProxy.Create<IGameObject, TargetStandIn>();
            ((TargetStandIn)(object)target).Target = id;
            return target;
        }
    }

    // The client is heard between the sim's frames, as in the game: a press started in the last frame before the window
    // closes reaches the run only as the next frame's events run, the window's judgement among them. It started in time,
    // so it fills the window.
    [Test]
    public void APressHeardInTheFrameBeforeTheWindowClosesFillsIt()
    {
        var client = new TopP6HumanLimitBreakBetweenFrames(LimitBreak(MeleeDpsA, 3), now => now < Closes && now + ScenarioRun.FrameSeconds >= Closes);
        var run = Window((_, _, _) => { }, betweenFrames: (world, alphaOmega) => client.Hear(world, alphaOmega.Enemy));

        Assert.That(client.StartedAt, Is.LessThan(Closes));
        Assert.That(run.Passed, run.ToString);
    }

    // A window that counts what lands, as the enrage's does: from 2.0 until before 12.0, its latest press 7.5. Pressed at
    // 7.4, the human's lands at 11.9 and fills it once it resolves; pressed at 7.6, it would land at 12.1, and the human
    // dies as the window closes.
    [TestCase(7.4f, true)]
    [TestCase(7.6f, false)]
    public void AWindowThatCountsWhatLandsTakesOnlyWhatLandsBeforeItsEnd(float pressAt, bool fills)
    {
        var run = TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () => limitBreaks.SetGauge(3f));
            limitBreaks.HoldTheHumanTo(new TopP6MeleeWindow(MeleeDpsA, Opens, 7.5f, TopP6LimitBreaks.EnrageMeleeRule, LandsBy: 12f), () => alphaOmega.Enemy);
            TopP6HumanLimitBreak.Press(world, pressAt, LimitBreak(MeleeDpsA, 3), () => alphaOmega.Enemy);
        }, Human);

        if (fills) Assert.That(run.Passed, run.ToString);
        else AssertDied(run, 7.5f, TopP6LimitBreaks.EnrageMeleeRule);
    }

    // An open window keeps the run going only until it is filled: one open to 20.0, filled as the press resolves at 7.5,
    // lets a run with nothing else to play end a second or two later.
    [Test]
    public void AWindowFilledEarlyLetsTheRunEnd()
    {
        var run = TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () => limitBreaks.SetGauge(3f));
            limitBreaks.HoldTheHumanTo(new TopP6MeleeWindow(MeleeDpsA, Opens, 20f, TopP6LimitBreaks.FirstDiveMeleeRule), () => alphaOmega.Enemy);
            TopP6HumanLimitBreak.Press(world, 3f, LimitBreak(MeleeDpsA, 3), () => alphaOmega.Enemy);
        }, Human);

        Assert.That(run.Passed, run.ToString);
        Assert.That(run.Elapsed, Is.InRange(8f, 10f), run.ToString);
    }

    // The run's own press for a bot-driven player seat fills the seat's window as the human's would.
    [Test]
    public void TheRunsPressForABotDrivenPlayerSeatFillsIt()
    {
        var run = Window((world, alphaOmega, limitBreaks) => world.Events.Add(3f,
            () => limitBreaks.Press(MeleeDpsA, PartyPresets.Standard[(int)MeleeDpsA].ClassJob, alphaOmega.Enemy)), new TimelineRunOptions { PlayerRole = MeleeDpsA, UserActionsEnabled = true });

        Assert.That(run.Passed, run.ToString);
    }

    private static readonly TimelineRunOptions Human = new() { PlayerRole = MeleeDpsA, HumanPlayer = true, UserActionsEnabled = true };

    private static void AssertDied(TimelineRunResult run, float at, string why)
    {
        Assert.That(run.Deaths.Select(d => (d.Role, d.Cause)), Is.EqualTo(new[] { (MeleeDpsA, why) }), run.ToString);
        Assert.That(run.Deaths[0].Time, Is.EqualTo(at).Within(TimelineRun.FrameLate), run.ToString);
    }

    private static TimelineRunResult Window(Action<SimWorld, TopP6AlphaOmega, TopP6LimitBreaks> timeline, TimelineRunOptions? options = null,
        Action<SimWorld, TopP6AlphaOmega>? betweenFrames = null)
        => TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () => limitBreaks.SetGauge(3f));
            limitBreaks.HoldTheHumanTo(new TopP6MeleeWindow(MeleeDpsA, Opens, Closes, TopP6LimitBreaks.FirstDiveMeleeRule), () => alphaOmega.Enemy);
            timeline(world, alphaOmega, limitBreaks);
            world.Events.Add(12f, () => { });
        }, options ?? Human, betweenFrames);
}
