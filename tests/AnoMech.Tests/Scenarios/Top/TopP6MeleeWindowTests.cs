using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Top;
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

        AssertDied(run, Closes, Died(TopP6LimitBreaks.MovingCancelledYours));
    }

    // Started at 5.5 and cut at 7.0, after the window closed: the death waits for the interruption.
    [Test]
    public void OneInterruptedAfterTheWindowClosedKillsThen()
    {
        var run = Window((world, alphaOmega, _) => TopP6HumanLimitBreak.Press(world, 5.5f, LimitBreak(MeleeDpsA, 3), () => alphaOmega.Enemy, interruptedAfter: 1.5f));

        AssertDied(run, 7f, Died(TopP6LimitBreaks.MovingCancelledYours));
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

    // A window that opens on another seat's melee limit break 3: M2's bot press resolves at 5.5 and opens it, until 9.0.
    [TestCase(6f, true)]
    [TestCase(3f, false)]
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

    private static TimelineRunResult Window(Action<SimWorld, TopP6AlphaOmega, TopP6LimitBreaks> timeline, TimelineRunOptions? options = null)
        => TopP6LimitBreakRun.Execute((world, alphaOmega, limitBreaks) =>
        {
            world.Events.Add(0.5f, () => limitBreaks.SetGauge(3f));
            limitBreaks.HoldTheHumanTo(new TopP6MeleeWindow(MeleeDpsA, Opens, Closes, TopP6LimitBreaks.FirstDiveMeleeRule), () => alphaOmega.Enemy);
            timeline(world, alphaOmega, limitBreaks);
            world.Events.Add(12f, () => { });
        }, options ?? Human);
}
