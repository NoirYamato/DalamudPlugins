using System.Numerics;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Tests;

// Cruise Chaser's two attacks on their own. He is spawned in the middle facing south (+z) and casts at nobody, so
// each shape runs along his facing; everyone is put on a spot of the test's a second before the first cast. A shape
// longer than the arena is measured from its north rim.
public class TeaP1ActionsTests
{
    private static readonly Vector2 OutOfReach = new(0f, -12f);
    private static readonly Vector2 NearTheNorthRim = new(0f, -18f);


    // 90 degrees wide, and as long as the sheet's 25 y plus his hitbox.
    [Test]
    public void AlphaSwordIsAQuarterCircleAheadOfHim()
    {
        var run = Cast((world, cruiseChaser, presses) =>
        {
            var reach = 25f + cruiseChaser.HitboxRadius;
            world.Events.Add(0.5f, () => cruiseChaser.SetPosition(new Placement(new Vector3(NearTheNorthRim.X, 0f, NearTheNorthRim.Y), 0f)));
            world.Events.Add(1f, () => TimelineRun.Place(world, new Dictionary<PartyRole, Vector2>
            {
                [MainTank] = NearTheNorthRim + OffAxis(44f, 10f),
                [OffTank] = NearTheNorthRim + OffAxis(46f, 10f),
                [RegenHealer] = NearTheNorthRim + new Vector2(0f, reach - 0.5f),
                [ShieldHealer] = NearTheNorthRim + new Vector2(0f, reach + 0.5f),
                [MeleeDpsA] = NearTheNorthRim + new Vector2(0f, -1.5f),
                [MeleeDpsB] = NearTheNorthRim + OffAxis(-44f, 20f),
                [PhysRangedDps] = NearTheNorthRim + OffAxis(-46f, 20f),
                [CasterDps] = NearTheNorthRim + new Vector2(2f, 0f),
            }));
            world.Events.Add(2f, () => cruiseChaser.Cast(TeaP1Actions.AlphaSword(presses)));
        }, out var hit);

        Assert.That(run.Passed, run.ToString);
        Assert.That(hit(P1ActionId.AlphaSword), Is.EquivalentTo(new[] { MainTank, RegenHealer, MeleeDpsB }));
    }

    // 10 y wide, and nothing behind him. The sheet's 50 y plus his hitbox is longer than the arena, so it reaches the
    // far rim.
    [Test]
    public void SuperBlasstyChargeIsALineAheadOfHim()
    {
        var run = Cast((world, cruiseChaser, presses) =>
        {
            world.Events.Add(0.5f, () => cruiseChaser.SetPosition(new Placement(new Vector3(NearTheNorthRim.X, 0f, NearTheNorthRim.Y), 0f)));
            world.Events.Add(1f, () => TimelineRun.Place(world, new Dictionary<PartyRole, Vector2>
            {
                [MainTank] = NearTheNorthRim + new Vector2(4.5f, 10f),
                [OffTank] = NearTheNorthRim + new Vector2(5.5f, 10f),
                [RegenHealer] = NearTheNorthRim + new Vector2(-4.5f, 30f),
                [ShieldHealer] = new(0f, 19.5f),
                [MeleeDpsA] = NearTheNorthRim + new Vector2(0f, -1.5f),
                [MeleeDpsB] = NearTheNorthRim + new Vector2(-5.5f, 30f),
                [PhysRangedDps] = NearTheNorthRim + new Vector2(-5.5f, 3f),
                [CasterDps] = NearTheNorthRim + new Vector2(0f, 0.5f),
            }));
            world.Events.Add(2f, () => cruiseChaser.Cast(TeaP1Actions.SuperBlasstyCharge(presses)));
        }, out var hit);

        Assert.That(run.Passed, run.ToString);
        Assert.That(hit(P1ActionId.SuperBlasstyCharge), Is.EquivalentTo(new[] { MainTank, RegenHealer, ShieldHealer, CasterDps }));
    }

    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public void ASecondHitKillsWhicheverTheTwoAre(bool cleaveFirst, bool cleaveSecond)
    {
        var run = Cast((world, cruiseChaser, presses) =>
        {
            world.Events.Add(1f, () => EveryoneOutOfReachBut(world, MainTank, new Vector2(0f, 3f)));
            world.Events.Add(2f, () => cruiseChaser.Cast(Attack(cleaveFirst, presses)));
            world.Events.Add(4f, () => cruiseChaser.Cast(Attack(cleaveSecond, presses)));
        }, out _);

        Assert.That(run.Deaths.Select(d => (d.Role, d.ActionId)),
                    Is.EqualTo(new[] { (MainTank, (uint?)(cleaveSecond ? P1ActionId.AlphaSword : P1ActionId.SuperBlasstyCharge)) }), run.ToString);
        Assert.That(run.Deaths[0].Time, Is.EqualTo(5f).Within(0.05f));
        Assert.That(run.Deaths[0].Cause, Does.Contain(TeaP1Actions.SecondCruiseChaserHit).And.Contain("had vuln up debuff"));
    }

    [TestCase(18.5f, true)]
    [TestCase(19.5f, false)]
    public void TheVulnerabilityLastsSeventeenSeconds(float secondHitAt, bool kills)
    {
        var run = Cast((world, cruiseChaser, presses) =>
        {
            world.Events.Add(1f, () => EveryoneOutOfReachBut(world, MainTank, new Vector2(0f, 3f)));
            world.Events.Add(2f, () => cruiseChaser.Cast(TeaP1Actions.AlphaSword(presses)));
            world.Events.Add(secondHitAt, () => cruiseChaser.Cast(TeaP1Actions.AlphaSword(presses)));
        }, out _);

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(run.Deaths.Count, Is.EqualTo(kills ? 1 : 0), run.ToString);
    }

    // He stands at (0, chaserZ) facing south, the human main tank straight ahead of him and the off tank 1.5 y beside
    // it: both are hit, and only the human is pushed, away from him, as the hit lands.
    [TestCase(true, 0f, 3f, 8f, TestName = "{m}(Alpha Sword, 5 y)")]
    [TestCase(false, -19f, -16f, 4f, TestName = "{m}(Super Blassty Charge, 20 y)")]
    public void AHitPushesAHumanAndNoBot(bool cleave, float chaserZ, float atZ, float landsZ)
    {
        Vector2? human = null;
        Vector2? bot = null;
        var run = Cast((world, cruiseChaser, presses) =>
        {
            world.Events.Add(0.5f, () => cruiseChaser.SetPosition(new Placement(new Vector3(0f, 0f, chaserZ), 0f)));
            world.Events.Add(1f, () =>
            {
                TimelineRun.Place(world, PerRole.All.ToDictionary(role => role, _ => new Vector2(0f, chaserZ - 0.5f)));
                TimelineRun.Place(world, MainTank, 0f, atZ);
                TimelineRun.Place(world, OffTank, 1.5f, atZ);
            });
            world.Events.Add(2f, () => cruiseChaser.Cast(Attack(cleave, presses)));
            world.Events.Add(3f, () =>
            {
                human = Flat(world.Party.Get(MainTank)!.Position);
                bot = Flat(world.Party.Get(OffTank)!.Position);
            });
        }, out var hit, human: true);

        Assert.That(run.Passed, run.ToString);
        Assert.That(hit(cleave ? P1ActionId.AlphaSword : P1ActionId.SuperBlasstyCharge), Is.EquivalentTo(new[] { MainTank, OffTank }));
        Assert.That(Vector2.Distance(human!.Value, new Vector2(0f, landsZ)), Is.LessThan(0.05f), $"the human at {human}");
        Assert.That(Vector2.Distance(bot!.Value, new Vector2(1.5f, atZ)), Is.LessThan(0.05f), $"the bot at {bot}");
    }

    // The arena has no wall: the charge throws the human 20 y from 10 y south of the middle, and the rim is 10 y on.
    [Test]
    public void AThrowPastTheRimKills()
    {
        var run = Cast((world, cruiseChaser, presses) =>
        {
            world.Events.Add(0.5f, () => cruiseChaser.SetPosition(new Placement(new Vector3(NearTheNorthRim.X, 0f, NearTheNorthRim.Y), 0f)));
            world.Events.Add(1f, () =>
            {
                TimelineRun.Place(world, PerRole.All.ToDictionary(role => role, _ => NearTheNorthRim + new Vector2(0f, -1.5f)));
                TimelineRun.Place(world, MainTank, 0f, 10f);
            });
            world.Events.Add(2f, () => cruiseChaser.Cast(TeaP1Actions.SuperBlasstyCharge(presses)));
        }, out _, human: true);

        Assert.That(run.Deaths.Select(d => d.Role), Is.EqualTo(new[] { MainTank }), run.ToString);
        Assert.That(run.Deaths[0].Cause, Does.Contain(TeaZone.WentPastTheRim));
    }

    // A headless run drives the player's seat as a bot until a test takes it over.
    [Test]
    public void ThePlayersSeatDrivenAsABotIsNotPushed()
    {
        Vector2? stands = null;
        var run = Cast((world, cruiseChaser, presses) =>
        {
            world.Events.Add(1f, () => EveryoneOutOfReachBut(world, MainTank, new Vector2(0f, 3f)));
            world.Events.Add(2f, () => cruiseChaser.Cast(TeaP1Actions.SuperBlasstyCharge(presses)));
            world.Events.Add(3f, () => stands = Flat(world.Party.Get(MainTank)!.Position));
        }, out _);

        Assert.That(run.Passed, run.ToString);
        Assert.That(Vector2.Distance(stands!.Value, new Vector2(0f, 3f)), Is.LessThan(0.05f), $"at {stands}");
    }

    [Test]
    public void AHawkBlasterKillsWithinTenYalmsOfWhereItGoesOff()
    {
        var centre = new Vector3(14f, 0f, 0f);
        var run = Cast((world, cruiseChaser, _) =>
        {
            world.Events.Add(1f, () =>
            {
                EveryoneOutOfReachBut(world, MainTank, new Vector2(4.5f, 0f));
                TimelineRun.Place(world, OffTank, 3.5f, 0f);
                cruiseChaser.SetPosition(new Placement(centre, 0f));
            });
            world.Events.Add(2f, () => cruiseChaser.Cast(TeaP1Actions.HawkBlaster, centre));
        }, out _);

        Assert.That(run.Deaths.Select(d => (d.Role, d.ActionId)), Is.EqualTo(new[] { (MainTank, (uint?)P1ActionId.HawkBlaster) }), run.ToString);
        Assert.That(run.Deaths[0].Cause, Does.Contain("Hawk Blasters go off unmarked"));
    }

    private static EnemyAction Attack(bool cleave, TeaP1Presses presses)
        => cleave ? TeaP1Actions.AlphaSword(presses) : TeaP1Actions.SuperBlasstyCharge(presses);

    private static void EveryoneOutOfReachBut(SimWorld world, PartyRole role, Vector2 spot)
    {
        TimelineRun.Place(world, PerRole.All.ToDictionary(r => r, _ => OutOfReach));
        TimelineRun.Place(world, role, spot.X, spot.Y);
    }

    // `degrees` off his facing, to his left when positive.
    private static Vector2 OffAxis(float degrees, float distance)
    {
        var radians = degrees * MathF.PI / 180f;
        return new Vector2(distance * MathF.Sin(radians), distance * MathF.Cos(radians));
    }

    // Cruise Chaser spawned shown in the middle, facing south; `hit` gives the seats an action's one ActionEffect hit.
    internal static TimelineRunResult Cast(Action<SimWorld, SimEnemy, TeaP1Presses> schedule, out Func<uint, IEnumerable<PartyRole>> hit, bool human = false)
    {
        Dictionary<string, PartyRole> seats = [];
        TeaP1Presses? presses = null;
        var run = TimelineRun.Execute(world =>
        {
            presses = new TeaP1Presses(world);
            var cruiseChaser = world.SpawnEnemy(new EnemySpawnConfig(
                BNpcBaseId: P1BNpcBaseId.CruiseChaser,
                NameId: P1BNpcNameId.CruiseChaser,
                Level: Level,
                Placement: new Placement(Vector3.Zero, 0f)))!;
            world.Events.Add(0.5f, () => seats = TimelineRun.Seats(world));
            schedule(world, cruiseChaser, presses);
        }, new TimelineRunOptions { Phase = TeaZone.P1, HumanPlayer = human });
        presses?.Dispose();
        hit = actionId => run.Sent("ability", actionId).Single().Hits.Select(id => seats[id]);
        return run;
    }

    private static Vector2 Flat(Vector3 position) => new(position.X, position.Z);
}
