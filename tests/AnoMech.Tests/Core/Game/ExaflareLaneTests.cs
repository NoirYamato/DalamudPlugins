using System.Numerics;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.EnemyActions.EnemyActionEffects;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// A lane from (0, -24) heading south in 8 y steps, seven explosions 1.1 s apart, its cast started at 2.0. The first is
// 31661 (a 12 s cast, r 8: its bar ends at 13.7, its ActionEffect at 14.0), the rest 31662 (instant, r 8), each cast
// by a free helper standing where it lands: 15.1 at (0, -16), 16.2 at (0, -8), 17.3 at the centre, and so on to
// (0, 24) at 20.6.
public class ExaflareLaneTests
{
    private const uint Helper = 9020;
    private static readonly EnemyAction First = new(31661) { Effects = [Damage(Severity.Lethal)] };
    private static readonly EnemyAction Rest = new(31662) { Effects = [Damage(Severity.Lethal)] };

    private static readonly ExaflareLane Lane = new(new Placement(new Vector3(0f, 0f, -24f), 0f), 8f, 7, 2f, 1.1f, First, Rest);

    // Everyone else waits out of the lane, far west.
    private static readonly Vector2 OutOfTheLane = new(-15f, 0f);

    [SetUp]
    public void InstallGame() => FakeGame.Install();

    [Test]
    public void EachExplosionIsOneAdvanceFurtherAlongTheFacing()
    {
        Assert.That(Enumerable.Range(0, 7).Select(step => Lane.Position(step).Z), Is.EqualTo(new[] { -24f, -16f, -8f, 0f, 8f, 16f, 24f }));
        Assert.That(Enumerable.Range(0, 7).Select(step => Lane.Position(step).X), Has.All.EqualTo(0f));
    }

    [Test]
    public void TheCastReleasesTheFirstAndEachLaterOneFollowsAnIntervalOn()
    {
        Assert.That(Enumerable.Range(0, 7).Select(Lane.ReleaseAt),
                    Is.EqualTo(new[] { 14f, 15.1f, 16.2f, 17.3f, 18.4f, 19.5f, 20.6f }).Within(1e-4f));
    }

    // The first decides its hits as its bar ends; an instant at its ActionEffect.
    [Test]
    public void TheFirstHitsAsItsBarEndsTheRestAsTheyGoOff()
    {
        Assert.That(Lane.HitAt(0), Is.EqualTo(13.7f).Within(1e-4f));
        Assert.That(Lane.HitAt(3), Is.EqualTo(17.3f).Within(1e-4f));
    }

    [Test]
    public void EachExplosionIsCastWhereItLandsAsItGoesOff()
    {
        var run = RunTheLane(stayAt: OutOfTheLane);
        var first = run.Sent("cast", First.ActionId).Single();
        var explosions = run.Sent("ability", First.ActionId, Rest.ActionId);

        Assert.That(first.Time, Is.EqualTo(2f).Within(TimelineRun.FrameLate));
        Assert.That(first.Position, Is.EqualTo(new Vector3(0f, 0f, -24f)));
        Assert.That(explosions.Select(e => e.Time), Is.EqualTo(Enumerable.Range(0, 7).Select(Lane.ReleaseAt)).Within(TimelineRun.FrameLate));
        Assert.That(explosions.Select(e => e.Position), Is.EqualTo(Enumerable.Range(0, 7).Select(Lane.Position)));
    }

    // (0, -17) is inside only the first explosion; leaving 0.05 s before its bar ends escapes it, 0.05 s after does not.
    [TestCase(-0.05f, false)]
    [TestCase(0.05f, true)]
    public void TheFirstExplosionTakesWhoeverIsInItAsItsBarEnds(float leaveAfterHit, bool dies)
    {
        var run = RunTheLane(stayAt: new Vector2(0f, -17f), leaveAt: Lane.HitAt(0) + leaveAfterHit);

        Assert.That(run.Deaths.Select(d => (d.Role, d.ActionId)), dies ? Is.EqualTo(new[] { (MainTank, (uint?)First.ActionId) }) : Is.Empty, run.ToString);
    }

    // (2, -8) is inside only the third explosion.
    [TestCase(-0.05f, false)]
    [TestCase(0.05f, true)]
    public void ALaterExplosionTakesWhoeverIsInItAsItGoesOff(float leaveAfterHit, bool dies)
    {
        var run = RunTheLane(stayAt: new Vector2(2f, -8f), leaveAt: Lane.HitAt(2) + leaveAfterHit);

        Assert.That(run.Deaths.Select(d => (d.Role, d.ActionId)), dies ? Is.EqualTo(new[] { (MainTank, (uint?)Rest.ActionId) }) : Is.Empty, run.ToString);
    }

    // The main tank stands at `stayAt` until `leaveAt`, then joins the others.
    private static TimelineRunResult RunTheLane(Vector2 stayAt, float? leaveAt = null)
    {
        return TimelineRun.Execute(world =>
        {
            var pool = new SpawnedHelperPool(world, new EnemySpawnConfig(Helper, Level: 1, EnemyList: EnemyListMode.Never,
                Visibility: SpawnVisibility.InvisibleHelper), 8);
            world.Events.Add(0f, pool.Spawn);
            world.Events.Add(0.5f, () =>
            {
                foreach (var role in PerRoleExcept(MainTank))
                    TimelineRun.Place(world, role, OutOfTheLane.X, OutOfTheLane.Y);
                TimelineRun.Place(world, MainTank, stayAt.X, stayAt.Y);
            });
            if (leaveAt is { } at)
                world.Events.Add(at, () => TimelineRun.Place(world, MainTank, OutOfTheLane.X, OutOfTheLane.Y));
            Lane.Schedule(world.Events, (action, target, spot, variation) => pool.Cast(action, target, spot!.Value, variation));
        });
    }

    private static IEnumerable<PartyRole> PerRoleExcept(PartyRole role) => Enum.GetValues<PartyRole>().Where(r => r != role);
}
