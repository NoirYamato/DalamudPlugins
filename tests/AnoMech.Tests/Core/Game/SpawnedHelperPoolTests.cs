using System.Numerics;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game;
using AnoMech.Core.SimObjects;

namespace AnoMech.Tests;

// A pool of two. Every cast is 31663 with no effects: a 3 s visual whose bar ends 2.7 s in and whose ActionEffect goes
// out 3 s in, then a 0.6 s lock, so a helper taken at 1.0 is busy until 4.6.
//
//   1.0  cast at (5, 5)    the last spawned helper takes it
//   2.0  cast at (-5, 5)   the other one, busy until 5.6
//   3.0  cast              both busy: dropped
//   4.8  cast at (0, -5)   the first is free again, busy until 8.4
public class SpawnedHelperPoolTests
{
    private const uint Helper = 9020;
    private static readonly EnemyAction ThreeSecondVisual = new(31663);

    private sealed record Outcome(TimelineRunResult Run, IReadOnlyList<bool> Taken, IReadOnlyDictionary<float, int> Busy, int Peak);

    private static Outcome RunThePool()
    {
        var taken = new List<bool>();
        var busy = new Dictionary<float, int>();
        SpawnedHelperPool? pool = null;
        var run = TimelineRun.Execute(world =>
        {
            pool = new SpawnedHelperPool(world, new EnemySpawnConfig(Helper, Level: 1, EnemyList: EnemyListMode.Never,
                Visibility: SpawnVisibility.InvisibleHelper), 2);
            world.Events.Add(0f, pool.Spawn);
            world.Events.Add(1f, () => taken.Add(pool.Cast(ThreeSecondVisual, default, At(5, 5))));
            world.Events.Add(2f, () => taken.Add(pool.Cast(ThreeSecondVisual, default, At(-5, 5))));
            world.Events.Add(3f, () => taken.Add(pool.Cast(ThreeSecondVisual, default, At(0, 0))));
            world.Events.Add(4.8f, () => taken.Add(pool.Cast(ThreeSecondVisual, default, At(0, -5))));
            foreach (var at in new[] { 1.5f, 2.5f, 3.85f, 4.3f, 4.7f, 8.5f })
                world.Events.Add(at, () => busy[at] = pool.Busy);
        });
        return new Outcome(run, taken, busy, pool!.PeakBusy);
    }

    private static Placement At(float x, float z) => new(new Vector3(x, 0f, z), 0f);

    [Test]
    public void TheLastSpawnedFreeHelperTakesEachCast()
    {
        var outcome = RunThePool();
        var casts = outcome.Run.Sent("cast", ThreeSecondVisual.ActionId);
        var helpers = outcome.Run.Spawned(Helper);

        Assert.That(outcome.Run.Failure, Is.Null, outcome.Run.ToString);
        Assert.That(casts.Select(c => c.Actor), Is.EqualTo(new[] { helpers[1], helpers[0], helpers[1] }));
    }

    // At 3.85 the first is past its bar but before its ActionEffect, at 4.3 in its lock.
    [Test]
    public void AHelperIsBusyThroughItsBarItsActionEffectAndItsLock()
    {
        var outcome = RunThePool();

        Assert.That(outcome.Busy,
            Is.EqualTo(new Dictionary<float, int> { [1.5f] = 1, [2.5f] = 2, [3.85f] = 2, [4.3f] = 2, [4.7f] = 1, [8.5f] = 0 }),
            () => string.Join(", ", outcome.Busy));
    }

    [Test]
    public void ACastWithEveryHelperBusyIsDroppedWithAWarning()
    {
        var outcome = RunThePool();

        Assert.That(outcome.Taken, Is.EqualTo(new[] { true, true, false, true }));
        Assert.That(outcome.Run.Sent("cast", ThreeSecondVisual.ActionId), Has.Count.EqualTo(3));
        Assert.That(outcome.Run.Warnings, Has.Some.Contains("dropped"));
    }

    [Test]
    public void EachHelperCastsWhereItIsSent()
    {
        var casts = RunThePool().Run.Sent("cast", ThreeSecondVisual.ActionId);

        Assert.That(casts.Select(c => (c.Position.X, c.Position.Z)), Is.EqualTo(new[] { (5f, 5f), (-5f, 5f), (0f, -5f) }));
    }

    [Test]
    public void ThePeakCountsTheMostBusyAtOnce() => Assert.That(RunThePool().Peak, Is.EqualTo(2));
}
