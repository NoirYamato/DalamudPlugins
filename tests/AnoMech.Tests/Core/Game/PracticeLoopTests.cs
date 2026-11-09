using AnoMech.Core.Game;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// The streak and Auto-restart, across runs on one game. Each run of PracticeRun.Clean ends clean about 2s in.
public class PracticeLoopTests
{
    // A Start from the inn zones in and seats the party on a seed drawn for that, before the run starts with it: an
    // Auto-restart rolls a fresh one all the same, as it does after a Start from inside the zone.
    [Test]
    public void AutoRestartAfterAStartFromTheInnRollsFreshEveryTime()
    {
        using var run = new PracticeRun(PracticeRun.Clean);
        run.Practice.AutoRestart = true;

        run.Game.EnterAndStart(run.Params(), SessionMode.Local);
        run.Until(() => run.Started.Count == 4);

        Assert.That(run.Started.Select(s => s.Seed).Distinct().Count(), Is.EqualTo(4));
    }

    [Test]
    public void AutoRestartAfterAStartFromTheZoneRollsFreshEveryTime()
    {
        using var run = new PracticeRun(PracticeRun.Clean);
        run.Practice.AutoRestart = true;

        run.StartInZone(run.Params());
        run.Until(() => run.Started.Count == 4);

        Assert.That(run.Started.Select(s => s.Seed).Distinct().Count(), Is.EqualTo(4));
    }

    // The debug build's Seed field: the player asked for that roll, every time.
    [Test]
    public void AutoRestartKeepsASeedThePlayerAskedFor()
    {
        using var run = new PracticeRun(PracticeRun.Clean);
        run.Practice.AutoRestart = true;

        run.Game.EnterAndStart(run.Params(seed: 1234), SessionMode.Local);
        run.Until(() => run.Started.Count == 3);

        Assert.That(run.Started.Select(s => s.Seed), Is.All.EqualTo(1234));
    }

    [Test]
    public void CleanRunsExtendTheStreakAndADeathEndsItAndAutoRestart()
    {
        var deathOnRun = 3;
        var runs = 0;
        using var run = new PracticeRun(world =>
        {
            if (++runs == deathOnRun) PracticeRun.PlayerDies(world);
            else PracticeRun.Clean(world);
        });
        run.Practice.AutoRestart = true;

        run.StartInZone(run.Params());
        run.Until(() => run.Practice.Streak == 2);
        run.Until(() => run.Game.Paused);

        Assert.That(run.Practice.Streak, Is.Zero);
        Assert.That(run.Practice.AutoRestart, Is.False);
        Assert.That(run.Started, Has.Count.EqualTo(3));
    }

    [Test]
    public void AutoRestartKeepsThePlayersSeat()
    {
        using var run = new PracticeRun(PracticeRun.Clean);
        run.Practice.AutoRestart = true;

        run.StartInZone(run.Params(seat: OffTank));
        run.Until(() => run.Started.Count == 3);

        Assert.That(run.Started.Select(s => s.Seat), Is.All.EqualTo(OffTank));
    }
}
