using System.Numerics;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using static AnoMech.Core.EnemyActions.EnemyActionEffects;
using static AnoMech.Core.Game.Party.PartyRole;

namespace AnoMech.Tests;

// The puddle is 31663 (a 3 s cast, r 6) made lethal, dropped at 2.0, so it hits at 4.7. The main tank stands at the
// centre; everyone else on a ring at r 12, 45° apart, from where they walk out to r 19 at 3.0.
public class BaitedPuddlesTests
{
    private const uint Helper = 9020;
    private static readonly EnemyAction Puddle = new(31663) { Effects = [Damage(Severity.Lethal)] };

    private static readonly IReadOnlyList<PartyRole> OnTheRing = [OffTank, RegenHealer, ShieldHealer, MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps];

    [Test]
    public void EachLivingMemberGetsOneWhereTheyStand()
    {
        IReadOnlyList<Vector3> spots = [];
        var run = Drop(dropped => spots = dropped);
        var casts = run.Sent("cast", Puddle.ActionId);

        Assert.That(casts, Has.Count.EqualTo(8));
        Assert.That(spots, Has.Count.EqualTo(8));
        Assert.That(Spots().Values, Has.All.Matches<Vector2>(spot => casts.Any(cast => Near(cast.Position, spot))));
        Assert.That(Spots().Values, Has.All.Matches<Vector2>(spot => spots.Any(dropped => Near(dropped, spot))));
        Assert.That(casts.Select(c => c.Time), Has.All.EqualTo(2f).Within(TimelineRun.FrameLate));
    }

    // Rows carry two decimals.
    private static bool Near(Vector3 position, Vector2 spot) => Vector2.Distance(new Vector2(position.X, position.Z), spot) < 0.01f;

    [Test]
    public void ItStaysWhereItDroppedSoOnlyWhoeverStaysOnItDies()
    {
        var run = Drop(_ => { });

        Assert.That(run.Deaths.Select(d => (d.Role, d.ActionId)), Is.EqualTo(new[] { (MainTank, (uint?)Puddle.ActionId) }), run.ToString);
    }

    [Test]
    public void TheDeadGetNone()
    {
        var run = Drop(_ => { }, killFirst: CasterDps);

        Assert.That(run.Sent("cast", Puddle.ActionId), Has.Count.EqualTo(7));
    }

    private static TimelineRunResult Drop(Action<IReadOnlyList<Vector3>> dropped, PartyRole? killFirst = null)
    {
        return TimelineRun.Execute(world =>
        {
            var pool = new SpawnedHelperPool(world, new EnemySpawnConfig(Helper, Level: 1, EnemyList: EnemyListMode.Never,
                Visibility: SpawnVisibility.InvisibleHelper), 8);
            world.Events.Add(0f, pool.Spawn);
            world.Events.Add(0.5f, () => TimelineRun.Place(world, Spots()));
            if (killFirst is { } dead)
                world.Events.Add(1f, () => world.Party.Get(dead)!.Die(SimCharacterDeathExtensions.Environment, "test"));
            world.Events.Add(2f, () => dropped(BaitedPuddles.Drop(world.Party, Puddle, (action, target, at, variation) => pool.Cast(action, target, at!.Value, variation))));
            world.Events.Add(3f, () =>
            {
                foreach (var role in OnTheRing.Where(r => r != killFirst))
                    TimelineRun.Place(world, role, OnRing(role, 19f).X, OnRing(role, 19f).Y);
            });
        });
    }

    private static Dictionary<PartyRole, Vector2> Spots()
        => OnTheRing.ToDictionary(role => role, role => OnRing(role, 12f)).Append(new(MainTank, Vector2.Zero)).ToDictionary();

    private static Vector2 OnRing(PartyRole role, float radius)
    {
        var bearing = (int)role * MathF.PI / 4f;
        return new Vector2(MathF.Sin(bearing), -MathF.Cos(bearing)) * radius;
    }
}
