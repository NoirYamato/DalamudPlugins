using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// At 2.0 the main tank is found by two checks, the off tank by one, and the deaths are dealt after both. 31655 and
// 31656 are Cosmo Dive's buster and stack.
public class VerdictsTests
{
    private const uint Buster = 31655, Stack = 31656;

    [Test]
    public void APlayerFoundByTwoChecksDiesOfTheFirst()
    {
        var run = Judge(new TimelineRunOptions());

        Assert.That(run.Deaths.Select(d => (d.Role, d.ActionId, Why(d.Cause))),
                    Is.EquivalentTo(new[] { (MainTank, (uint?)Buster, "first"), (OffTank, (uint?)Stack, "third") }), run.ToString);
    }

    [Test]
    public void NobodyDiesUntilTheyAreDealt()
    {
        var aliveBetween = false;
        Judge(new TimelineRunOptions(), world => aliveBetween = world.Party.Get(MainTank).IsAlive());

        Assert.That(aliveBetween, Is.True);
    }

    // 32223, Flash Gale, has no name in the sheet.
    [Test]
    public void ANameStandsInForAnUnnamedAction()
    {
        var run = TimelineRun.Execute(world => world.Events.Add(2f, () =>
        {
            var verdicts = new Verdicts();
            verdicts.Kill(world.Party.Get(MainTank)!, 32223, "why", "Flash Gale");
            verdicts.Deal();
        }));

        Assert.That(run.Deaths.Select(d => (d.Role, d.ActionId, d.Cause)), Is.EqualTo(new[] { (MainTank, (uint?)32223, "Flash Gale (why)") }), run.ToString);
    }

    [Test]
    public void GodmodeReportsEachPlayerOnce()
    {
        var run = Judge(new TimelineRunOptions { GodMode = true });

        Assert.That(run.Deaths, Is.Empty, run.ToString);
        Assert.That(run.Chat.Where(line => line.Contains(" died: ")).Select(Why), Is.EquivalentTo(new[] { "first", "third" }),
                    () => string.Join(Environment.NewLine, run.Chat));
    }

    private static TimelineRunResult Judge(TimelineRunOptions options, Action<SimWorld>? betweenChecksAndDeal = null)
        => TimelineRun.Execute(world => world.Events.Add(2f, () =>
        {
            var verdicts = new Verdicts();
            verdicts.Kill(world.Party.Get(MainTank)!, Buster, "first");
            verdicts.Kill(world.Party.Get(MainTank)!, Stack, "second");
            verdicts.Kill(world.Party.Get(OffTank)!, Stack, "third");
            betweenChecksAndDeal?.Invoke(world);
            verdicts.Deal();
        }), options);

    // The reason Die puts in parentheses after the action's name.
    private static string Why(string cause) => cause[(cause.IndexOf('(') + 1)..^1];
}
