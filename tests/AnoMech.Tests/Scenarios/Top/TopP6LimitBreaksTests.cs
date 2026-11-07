using AnoMech.Core.Game.Party;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Top;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// The first melee's limit break 3 at 2.0, for show: a bot plays it (only while tank mitigation is required, as for any
// bot), a human's seat fires nothing, and the gauge is full either side of it.
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

    [TestCase(true)]
    [TestCase(false)]
    public void TheGaugeIsFullBeforeAndAfter(bool human)
    {
        var (_, _, bars) = LimitBreak3(new TimelineRunOptions { PlayerRole = MeleeDpsA, HumanPlayer = human, UserActionsEnabled = true });

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
            world.Events.Add(2f, () => TopP6LimitBreaks.LimitBreak3(world, MeleeDpsA));
            world.Events.Add(2.5f, () => bars = world.Party.LimitBreak.FilledBars);
        }, options);
        return (run, seats, bars);
    }
}
