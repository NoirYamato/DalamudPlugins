using AnoMech.Core.Game;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// Retry and Drill: a local run's roll played again, with the seed it played, the seat it gave the player, its strat and
// waymarks and its scenario's settings as they were. PracticeRun.Clean ends clean about 2s in; PracticeRun.PlayerDies
// freezes at 6s.
public class RollReplayTests
{
    public sealed class Knobs
    {
        public int Value { get; set; }
    }

    [Test]
    public void RetryPlaysTheLastRunAgainAsItWasSetUp()
    {
        var knobs = new Knobs { Value = 1 };
        var seen = new List<int>();
        using var run = new PracticeRun(world =>
        {
            seen.Add(knobs.Value);
            PracticeRun.Clean(world);
        }, knobs);
        run.StartInZone(run.Params(seat: MeleeDpsB, strat: 1, waymark: 1));
        run.Until(() => run.Game.Status == SessionStatus.Succeeded);
        knobs.Value = 2;

        run.Game.StartScenario(run.Practice.Rolls.Replay());
        run.Until(() => run.Started.Count == 2);

        Assert.That(run.Started[0], Is.EqualTo((run.Started[0].Seed, MeleeDpsB, (int?)1, 1)));
        Assert.That(run.Started[1], Is.EqualTo(run.Started[0]));
        Assert.That(seen, Is.EqualTo(new[] { 1, 1 }));
        Assert.That(knobs.Value, Is.EqualTo(1));
        Assert.That(run.Practice.Rolls.Replaying);
    }

    // Retry after a Leave zones in again, on the roll it played.
    [Test]
    public void RetryFromTheInnPlaysTheRollAgain()
    {
        using var run = new PracticeRun(PracticeRun.Clean);
        run.Game.EnterAndStart(run.Params(), SessionMode.Local);
        run.Until(() => run.Game.Status == SessionStatus.Succeeded);
        run.Game.Leave();
        run.Until(() => run.Game.Zone is null);

        run.Game.EnterAndStart(run.Practice.Rolls.Replay(), SessionMode.Local);
        run.Until(() => run.Started.Count == 2);

        Assert.That(run.Started[1], Is.EqualTo(run.Started[0]));
        Assert.That(run.Practice.Rolls.Replaying);
    }

    [Test]
    public void RetryWithoutARunIsRefused()
    {
        using var run = new PracticeRun(PracticeRun.Clean);

        Assert.That(run.Practice.Rolls.RetryRefusal, Is.EqualTo("No run to retry yet."));
    }

    // Only a local run is remembered, and nothing retries in a multiplayer stay.
    [Test]
    public void RetryIsRefusedInMultiplayer()
    {
        using var run = new PracticeRun(PracticeRun.Clean);
        run.Game.ZoneIn(run.Scenario.Phase.Zone, SessionMode.Host);
        run.Game.StartScenario(run.Params());
        run.Until(() => run.Started.Count == 1);

        Assert.That(run.Practice.Rolls.Last, Is.Null);
        Assert.That(run.Practice.Rolls.RetryRefusal, Is.EqualTo(RollReplay.MultiplayerRefusal));
    }

    [Test]
    public void AReplayedRollDoesNotExtendTheStreak()
    {
        using var run = new PracticeRun(PracticeRun.Clean);
        run.StartInZone(run.Params());
        run.Until(() => run.Practice.Streak == 1);

        run.Game.StartScenario(run.Practice.Rolls.Replay());
        run.Until(() => run.Started.Count == 2 && run.Game.Status == SessionStatus.Succeeded);
        var afterReplay = run.Practice.Streak;
        run.Game.StartScenario(run.Params());
        run.Until(() => run.Started.Count == 3 && run.Game.Status == SessionStatus.Succeeded);

        Assert.That(afterReplay, Is.EqualTo(1));
        Assert.That(run.Practice.Streak, Is.EqualTo(2));
    }

    [Test]
    public void AutoRestartAfterARetryRollsFresh()
    {
        using var run = new PracticeRun(PracticeRun.Clean);
        run.StartInZone(run.Params());
        run.Until(() => run.Game.Status == SessionStatus.Succeeded);
        run.Practice.AutoRestart = true;

        run.Game.StartScenario(run.Practice.Rolls.Replay());
        run.Until(() => run.Started.Count == 4);

        Assert.That(run.Started[1].Seed, Is.EqualTo(run.Started[0].Seed));
        Assert.That(run.Started.Skip(1).Select(s => s.Seed).Distinct().Count(), Is.EqualTo(3));
    }

    // Two deaths, then the roll is cleared and Drill moves on to a fresh one. A death switches Auto-restart off, not
    // Drill, and the cleared replay doesn't count toward the streak.
    [Test]
    public void DrillPlaysTheRollAgainUntilItIsClearedThenMovesOn()
    {
        var runs = 0;
        using var run = new PracticeRun(world =>
        {
            if (++runs <= 2) PracticeRun.PlayerDies(world);
            else PracticeRun.Clean(world);
        });
        run.Practice.Rolls.Drill = true;
        run.Practice.AutoRestart = true;

        run.StartInZone(run.Params());
        run.Until(() => run.Started.Count == 4 && run.Game.Status == SessionStatus.Succeeded);

        var seeds = run.Started.Select(s => s.Seed).ToList();
        Assert.That(seeds.Take(3), Is.All.EqualTo(seeds[0]));
        Assert.That(seeds[3], Is.Not.EqualTo(seeds[0]));
        Assert.That(run.Started.Select(s => s.Seat).Distinct().Count(), Is.EqualTo(1));
        Assert.That(run.Practice.Rolls.Drill);
        Assert.That(run.Practice.AutoRestart, Is.False);
        Assert.That(run.Practice.Streak, Is.EqualTo(1));
    }

    // Godmode keeps everyone up, so the run plays to its end with the death only reported.
    [Test]
    public void DrillPlaysTheRollAgainAfterADeathGodmodeKeptUp()
    {
        using var run = new PracticeRun(PracticeRun.PlayerDies);
        run.Game.GodMode = true;
        run.Practice.Rolls.Drill = true;

        run.StartInZone(run.Params());
        run.Until(() => run.Started.Count == 2);

        Assert.That(run.Started[1], Is.EqualTo(run.Started[0]));
    }

    [Test]
    public void DrillRollsFreshAfterACleanRun()
    {
        using var run = new PracticeRun(PracticeRun.Clean);
        run.Practice.Rolls.Drill = true;

        run.StartInZone(run.Params());
        run.Until(() => run.Started.Count == 3);

        Assert.That(run.Started.Select(s => s.Seed).Distinct().Count(), Is.EqualTo(3));
        Assert.That(run.Practice.Streak, Is.EqualTo(2));
    }

    // Stopped before it froze, the run leaves nothing for Drill to play again.
    [Test]
    public void DrillLeavesAStoppedRunAlone()
    {
        using var run = new PracticeRun(PracticeRun.PlayerDies);
        run.Practice.Rolls.Drill = true;
        run.StartInZone(run.Params());
        run.Until(() => run.Game.World.Events.Elapsed >= 3f);

        run.Game.Reset();
        run.Until(() => run.Game.ActiveScenario is null);
        run.For(10f);

        Assert.That(run.Started, Has.Count.EqualTo(1));
    }

    // Drill ticked on while the run stands frozen plays its roll again at once.
    [Test]
    public void DrillTurnedOnDuringTheFreezePlaysTheRollAgain()
    {
        using var run = new PracticeRun(PracticeRun.PlayerDies);
        run.StartInZone(run.Params());
        run.Until(() => run.Game.Paused);

        run.Practice.Rolls.Drill = true;
        run.Until(() => run.Started.Count == 2);

        Assert.That(run.Started[1], Is.EqualTo(run.Started[0]));
    }

    // A session's run plays on the host for everyone: a host left with Drill on plays neither that run nor its last local
    // one again, whether the session's run froze or godmode kept it going to its end.
    [TestCase(false)]
    [TestCase(true)]
    public void DrillLeavesAMultiplayerRunAlone(bool godMode)
    {
        var runs = 0;
        using var run = new PracticeRun(world =>
        {
            if (++runs == 1) PracticeRun.Clean(world);
            else PracticeRun.PlayerDies(world);
        });
        run.Game.GodMode = godMode;
        run.StartInZone(run.Params());
        run.Until(() => run.Game.Status == SessionStatus.Succeeded);
        run.Game.Leave();
        run.Until(() => run.Game.Zone is null);
        run.Practice.Rolls.Drill = true;

        run.Game.ZoneIn(run.Scenario.Phase.Zone, SessionMode.Host);
        run.Game.StartScenario(run.Params());
        run.Until(() => godMode ? run.Game.Status == SessionStatus.CompletedWithMistake : run.Game.Paused);
        run.For(10f);

        Assert.That(run.Practice.Rolls.Last, Is.Not.Null);
        Assert.That(run.Started, Has.Count.EqualTo(2));
    }

    // What the window's seat label reads: with Drill on, a run in which someone died keeps its seat for the next.
    [TestCase(true)]
    [TestCase(false)]
    public void DrillSaysTheRollPlaysAgainOnceSomeoneDied(bool drill)
    {
        using var run = new PracticeRun(PracticeRun.PlayerDies);
        run.Practice.Rolls.Drill = drill;
        run.StartInZone(run.Params());
        run.Until(() => run.Started.Count == 1);
        var beforeTheDeath = run.Practice.Rolls.DrillReplays;

        run.Until(() => run.Game.Status == SessionStatus.Failed);

        Assert.That(beforeTheDeath, Is.False);
        Assert.That(run.Practice.Rolls.DrillReplays, Is.EqualTo(drill));
    }
}
