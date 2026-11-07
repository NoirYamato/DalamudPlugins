using AnoMech.Core.Game.Party;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Top;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// The strats' limit break 3 for the first melee at 2.0, on a gauge emptied at 1.5, for show: a bot plays it at level 3
// (only while tank mitigation is required, as for any bot), a human's seat fires nothing, and the gauge is full after
// it either way.
public class TopP6LimitBreaksTests
{
    private static uint MeleeLimitBreak3 => LimitBreakHandler.ActionId(PartyPresets.Standard[(int)MeleeDpsA].ClassJob, 3);

    [SetUp]
    public void InstallGame() => FakeGame.Install();

    [Test]
    public void ABotSeatFiresItsLimitBreak3()
    {
        var (run, seats, _) = LimitBreak3(new TimelineRunOptions { UserActionsEnabled = true });

        Assert.That(MeleeLimitBreak3, Is.Not.Zero);
        Assert.That(run.Sent("ability", MeleeLimitBreak3).Select(a => seats[a.Actor]), Is.EqualTo(new[] { MeleeDpsA }));
    }

    [Test]
    public void AHumanSeatFiresNothing()
    {
        var (run, _, _) = LimitBreak3(new TimelineRunOptions { PlayerRole = MeleeDpsA, HumanPlayer = true, UserActionsEnabled = true });

        Assert.That(run.Sent("ability", MeleeLimitBreak3), Is.Empty);
    }

    [Test]
    public void NoBotFiresWhileTankMitigationIsNotRequired()
    {
        var (run, _, _) = LimitBreak3(new TimelineRunOptions());

        Assert.That(run.Sent("ability", MeleeLimitBreak3), Is.Empty);
    }

    // The bot's press spends the gauge, and Brilliant Dynamis gives it back.
    [Test]
    public void ABotsPressLeavesTheGaugeFull()
    {
        var (run, _, bars) = LimitBreak3(new TimelineRunOptions { UserActionsEnabled = true });

        Assert.That(run.Sent("ability", MeleeLimitBreak3), Has.Count.EqualTo(1));
        Assert.That(bars, Is.EqualTo(3));
    }

    [Test]
    public void AHumanSeatFindsTheGaugeFull()
    {
        var (_, _, bars) = LimitBreak3(new TimelineRunOptions { PlayerRole = MeleeDpsA, HumanPlayer = true, UserActionsEnabled = true });

        Assert.That(bars, Is.EqualTo(3));
    }

    private static (TimelineRunResult Run, Dictionary<string, PartyRole> Seats, int BarsAfter) LimitBreak3(TimelineRunOptions options)
    {
        var seats = new Dictionary<string, PartyRole>();
        var bars = -1;
        var run = TimelineRun.Execute(world =>
        {
            world.Events.Add(1f, () =>
            {
                foreach (var (id, role) in TimelineRun.Seats(world)) seats[id] = role;
            });
            world.Events.Add(1.5f, () => world.Party.LimitBreak.Set(0f));
            world.Events.Add(2f, () => TopP6LpduAi.LimitBreak3RefilledByBrilliantDynamis(world, MeleeDpsA));
            world.Events.Add(2.5f, () => bars = world.Party.LimitBreak.FilledBars);
        }, options);
        return (run, seats, bars);
    }
}
