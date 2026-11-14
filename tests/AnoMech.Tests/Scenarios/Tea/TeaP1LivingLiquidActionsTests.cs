using System.Numerics;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea;
using FFXIVClientStructs.FFXIV.Client.Game;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Tests;

// The Living Liquid's and the Liquid Hand's attacks on their own. The boss is spawned in the middle facing south (+z)
// unless a test says otherwise, and casts along its facing; everyone is put on a spot of the test's a second before
// the first cast.
public class TeaP1LivingLiquidActionsTests
{
    private static readonly Vector2 OutOfTheWay = new(-12f, -12f);

    private static readonly TimelineRunOptions Bots = new() { Phase = TeaZone.P1 };
    private static readonly TimelineRunOptions HumanMainTank = Bots with { HumanPlayer = true };

    // 30 degrees wide and the sheet's 40 y long, from the caster's own spot. The caster stands on the north rim.
    [Test]
    public void AProteanWaveIsAThirtyDegreeConeFortyYalmsLong()
    {
        var from = new Vector2(0f, -19.5f);
        var run = Cast(new Placement(new Vector3(from.X, 0f, from.Y), 0f), (world, liquid, _) =>
        {
            world.Events.Add(1f, () => TimelineRun.Place(world, new Dictionary<PartyRole, Vector2>
            {
                [MainTank] = from + OffAxis(14f, 10f),
                [OffTank] = from + OffAxis(16f, 10f),
                [RegenHealer] = from + OffAxis(-14f, 30f),
                [ShieldHealer] = from + OffAxis(-16f, 30f),
                [MeleeDpsA] = from + new Vector2(0f, 39f),
                [MeleeDpsB] = from + new Vector2(0f, 41f),
                [PhysRangedDps] = from + OffAxis(90f, 5f),
                [CasterDps] = from + new Vector2(0f, 0.5f),
            }));
            world.Events.Add(2f, () => liquid.Cast(TeaP1Actions.ProteanWaveStraightAhead));
        }, out var hit);

        Assert.That(run.Passed, run.ToString);
        Assert.That(hit(P1ActionId.ProteanWaveStraightAhead).Single(), Is.EquivalentTo(new[] { MainTank, RegenHealer, MeleeDpsA, CasterDps }));
    }

    // The sheet gives these two rows no cone: unshaped, each would catch everyone within 40 y.
    [TestCase(false)]
    [TestCase(true)]
    public void TheUnseenWavesAreConesToo(bool fromATornado)
    {
        var wave = fromATornado ? TeaP1Actions.TornadoWaveUnseen : TeaP1Actions.ProteanWaveAtTheClosest;
        var run = Cast((world, liquid, _) =>
        {
            world.Events.Add(1f, () =>
            {
                for (var seat = 0; seat < PerRole.All.Length; seat++)
                    TimelineRun.Place(world, PerRole.All[seat], OffAxis(45f * seat, 8f).X, OffAxis(45f * seat, 8f).Y);
            });
            world.Events.Add(2f, () => liquid.Cast(wave));
        }, out var hit);

        Assert.That(run.Passed, run.ToString);
        Assert.That(hit(wave.ActionId).Single(), Is.EqualTo(new[] { MainTank }));
    }

    // A telegraphed wave is cast at nobody, along its caster's facing: turned to the main tank as the cast starts, it
    // goes off down that line though the main tank has walked out and the off tank in.
    [Test]
    public void ATelegraphedWaveGoesWhereItsCasterFacedAsTheCastStarted()
    {
        var run = Cast((world, liquid, presses) =>
        {
            world.Events.Add(1f, () => EveryoneOutOfTheWayBut(world, (MainTank, new Vector2(5f, 5f)), (OffTank, new Vector2(-5f, 5f))));
            world.Events.Add(2f, () =>
            {
                liquid.SetPosition(liquid.Placement().Face(world.Party.Get(MainTank)!.Position));
                liquid.Cast(TeaP1Actions.ProteanWaveAtAPlayer(presses));
            });
            world.Events.Add(3f, () => EveryoneOutOfTheWayBut(world, (MainTank, new Vector2(-5f, 5f)), (OffTank, new Vector2(7f, 7f))));
        }, out var hit);

        Assert.That(run.Passed, run.ToString);
        Assert.That(hit(P1ActionId.ProteanWaveAtAPlayer).Single(), Is.EqualTo(new[] { OffTank }));
        Assert.That(run.Sent("ability", P1ActionId.ProteanWaveAtAPlayer).Single().Time, Is.EqualTo(5f).Within(TimelineRun.FrameLate));
    }

    [Test]
    public void AFirstWaveLeavesWaterResistanceDownAndKillsNobody()
    {
        var carries = false;
        var run = Cast((world, liquid, _) =>
        {
            world.Events.Add(1f, () => EveryoneOutOfTheWayBut(world, (MainTank, new Vector2(0f, 5f))));
            world.Events.Add(2f, () => liquid.Cast(TeaP1Actions.ProteanWaveStraightAhead));
            world.Events.Add(2.5f, () => carries = world.Party.Get(MainTank)!.HasStatus(P1StatusId.WaterResistanceDown));
        }, out _);

        Assert.That(run.Passed, run.ToString);
        Assert.That(carries, "the main tank carries Water Resistance Down");
    }

    private static IEnumerable<TestCaseData> EveryWave()
    {
        yield return new TestCaseData(P1ActionId.ProteanWaveFromTheLiquid).SetName("{m}(the Liquid's own, telegraphed)");
        yield return new TestCaseData(P1ActionId.ProteanWaveAtAPlayer).SetName("{m}(at a player, telegraphed)");
        yield return new TestCaseData(P1ActionId.ProteanWaveStraightAhead).SetName("{m}(straight ahead)");
        yield return new TestCaseData(P1ActionId.ProteanWaveAtTheClosest).SetName("{m}(at the closest)");
        yield return new TestCaseData(P1ActionId.TornadoWave).SetName("{m}(a tornado's, telegraphed)");
        yield return new TestCaseData(P1ActionId.TornadoWaveUnseen).SetName("{m}(a tornado's, unseen)");
    }

    private static EnemyAction Wave(uint actionId, TeaP1Presses presses) => actionId switch
    {
        P1ActionId.ProteanWaveFromTheLiquid => TeaP1Actions.ProteanWaveFromTheLiquid(presses),
        P1ActionId.ProteanWaveAtAPlayer => TeaP1Actions.ProteanWaveAtAPlayer(presses),
        P1ActionId.ProteanWaveStraightAhead => TeaP1Actions.ProteanWaveStraightAhead,
        P1ActionId.ProteanWaveAtTheClosest => TeaP1Actions.ProteanWaveAtTheClosest,
        P1ActionId.TornadoWave => TeaP1Actions.TornadoWave(presses),
        _ => TeaP1Actions.TornadoWaveUnseen,
    };

    // The first wave is the unseen one straight ahead at 2.0; whichever follows it decides 4 s after it.
    [TestCaseSource(nameof(EveryWave))]
    public void AnyWaveOnACarrierKills(uint second)
    {
        var run = Cast((world, liquid, presses) =>
        {
            var wave = Wave(second, presses);
            world.Events.Add(1f, () => EveryoneOutOfTheWayBut(world, (MainTank, new Vector2(0f, 5f))));
            world.Events.Add(2f, () => liquid.Cast(TeaP1Actions.ProteanWaveStraightAhead));
            world.Events.Add(6f - wave.ResolvesAfter(), () => liquid.Cast(wave));
        }, out _);

        Assert.That(run.Deaths.Select(d => (d.Role, d.ActionId)), Is.EqualTo(new[] { (MainTank, (uint?)second) }), run.ToString);
        Assert.That(run.Deaths[0].Time, Is.EqualTo(7f).Within(0.05f));
        Assert.That(run.Deaths[0].Cause, Does.Contain(TeaP1Actions.SecondWave).And.Contain("had vuln up debuff"));
    }

    [TestCase(4.5f, true)]
    [TestCase(5.5f, false)]
    public void WaterResistanceDownLastsFiveSeconds(float secondWaveAfter, bool kills)
    {
        var run = Cast((world, liquid, _) =>
        {
            world.Events.Add(1f, () => EveryoneOutOfTheWayBut(world, (MainTank, new Vector2(0f, 5f))));
            world.Events.Add(2f, () => liquid.Cast(TeaP1Actions.ProteanWaveStraightAhead));
            world.Events.Add(2f + secondWaveAfter, () => liquid.Cast(TeaP1Actions.ProteanWaveStraightAhead));
        }, out _);

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(run.Deaths.Count, Is.EqualTo(kills ? 1 : 0), run.ToString);
    }

    // Two cones that catch one player in one frame: the second finds the first's Water Resistance Down.
    [Test]
    public void TwoWavesAtOnceKill()
    {
        var run = Cast((world, liquid, _) =>
        {
            var helper = world.SpawnEnemy(new EnemySpawnConfig(BNpcBaseId: P1BNpcBaseId.Helper, Visibility: SpawnVisibility.InvisibleHelper))!;
            world.Events.Add(1f, () => EveryoneOutOfTheWayBut(world, (MainTank, new Vector2(0f, 5f))));
            world.Events.Add(2f, () =>
            {
                liquid.Cast(TeaP1Actions.ProteanWaveStraightAhead);
                helper.Cast(TeaP1Actions.ProteanWaveAtTheClosest);
            });
        }, out _);

        Assert.That(run.Deaths.Select(d => d.Role), Is.EqualTo(new[] { MainTank }), run.ToString);
        Assert.That(run.Deaths[0].Cause, Does.Contain(TeaP1Actions.SecondWave));
    }

    [TestCase((ushort)82, TestName = "{m}(Hallowed Ground)")]
    [TestCase((ushort)409, TestName = "{m}(Holmgang)")]
    [TestCase((ushort)810, TestName = "{m}(Living Dead)")]
    [TestCase((ushort)1836, TestName = "{m}(Superbolide)")]
    public void ATanksInvulnerabilityLivesThroughASecondWave(ushort invulnerability)
    {
        var run = Cast((world, liquid, _) =>
        {
            world.Events.Add(1f, () =>
            {
                EveryoneOutOfTheWayBut(world, (MainTank, new Vector2(0f, 5f)), (OffTank, new Vector2(0.5f, 6f)));
                world.Party.Get(MainTank)!.AddStatus(invulnerability, 10f);
            });
            world.Events.Add(2f, () => liquid.Cast(TeaP1Actions.ProteanWaveStraightAhead));
            world.Events.Add(3f, () => liquid.Cast(TeaP1Actions.ProteanWaveStraightAhead));
        }, out _);

        Assert.That(run.Deaths.Select(d => d.Role), Is.EqualTo(new[] { OffTank }), run.ToString);
        Assert.That(P1StatusId.Invulnerabilities, Does.Contain(invulnerability));
    }

    // The human main tank straight ahead of the Liquid and the off tank beside it, both in the cone: only the human is
    // thrown, 10 y straight away from the wave's source, as the hit lands.
    [TestCase(P1ActionId.ProteanWaveFromTheLiquid, 5f, 15f, TestName = "{m}(the Liquid's own, 10 y)")]
    [TestCase(P1ActionId.ProteanWaveAtAPlayer, 5f, 15f, TestName = "{m}(at a player, 10 y)")]
    [TestCase(P1ActionId.TornadoWave, 5f, 15f, TestName = "{m}(a tornado's, 10 y)")]
    [TestCase(P1ActionId.ProteanWaveFromTheLiquid, 14f, 20f, TestName = "{m}(to the rim)")]
    public void ATelegraphedWaveThrowsAHumanAndNoBot(uint actionId, float atZ, float landsZ)
    {
        var (human, bot) = ThrownBy(actionId, atZ, press: null);

        Assert.That(Vector2.Distance(human, new Vector2(0f, landsZ)), Is.LessThan(0.05f), $"the human at {human}");
        Assert.That(Vector2.Distance(bot, new Vector2(1f, atZ)), Is.LessThan(0.05f), $"the bot at {bot}");
    }

    [TestCase(P1PlayerActionId.ArmsLength)]
    [TestCase(P1PlayerActionId.Surecast)]
    public void AnAntiKnockbackHoldsAHumanInATelegraphedWave(uint press)
    {
        var (human, _) = ThrownBy(P1ActionId.ProteanWaveFromTheLiquid, 5f, press);

        Assert.That(Vector2.Distance(human, new Vector2(0f, 5f)), Is.LessThan(0.05f), $"the human at {human}");
    }

    [TestCase(P1ActionId.ProteanWaveStraightAhead)]
    [TestCase(P1ActionId.ProteanWaveAtTheClosest)]
    [TestCase(P1ActionId.TornadoWaveUnseen)]
    public void AnUnseenWaveThrowsNobody(uint actionId)
    {
        var (human, _) = ThrownBy(actionId, 5f, press: null);

        Assert.That(Vector2.Distance(human, new Vector2(0f, 5f)), Is.LessThan(0.05f), $"the human at {human}");
    }

    // Where the human main tank at (0, atZ) and the bot off tank at (1, atZ) stand a second after the wave, cast at
    // 2.0, has landed on both.
    private static (Vector2 Human, Vector2 Bot) ThrownBy(uint actionId, float atZ, uint? press)
    {
        Vector2? human = null;
        Vector2? bot = null;
        var run = Cast((world, liquid, presses) =>
        {
            var wave = Wave(actionId, presses);
            world.Events.Add(1f, () => EveryoneOutOfTheWayBut(world, (MainTank, new Vector2(0f, atZ)), (OffTank, new Vector2(1f, atZ))));
            if (press is { } pressed)
                world.Events.Add(1.5f, () => ((FakeUserActions)Natives.UserActions).Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, pressed, 0)));
            world.Events.Add(2f, () => liquid.Cast(wave));
            world.Events.Add(3f + wave.ResolvesAfter(), () =>
            {
                human = Flat(world.Party.Get(MainTank)!.Position);
                bot = Flat(world.Party.Get(OffTank)!.Position);
            });
        }, out var hit, HumanMainTank);

        Assert.That(run.Passed, run.ToString);
        Assert.That(hit(actionId).Single(), Is.EquivalentTo(new[] { MainTank, OffTank }));
        return (human!.Value, bot!.Value);
    }

    // Laid at (8, 0): 4.5 y from it dies, 5.5 y from it does not.
    [Test]
    public void ASluiceKillsWithinFiveYalmsOfWhereItWasLaid()
    {
        var laidAt = new Vector3(8f, 0f, 0f);
        var run = Cast((world, liquid, _) =>
        {
            world.Events.Add(1f, () => EveryoneOutOfTheWayBut(world, (MainTank, new Vector2(12.5f, 0f)), (OffTank, new Vector2(13.5f, 0f)), (RegenHealer, new Vector2(2.5f, 0f))));
            world.Events.Add(2f, () => liquid.Cast(TeaP1Actions.Sluice, laidAt));
        }, out _);

        Assert.That(run.Deaths.Select(d => (d.Role, d.ActionId)), Is.EqualTo(new[] { (MainTank, (uint?)P1ActionId.Sluice) }), run.ToString);
        Assert.That(run.Deaths[0].Time, Is.EqualTo(5.7f).Within(0.05f));
        Assert.That(run.Deaths[0].Cause, Does.Contain("Stood in a Sluice as it went off"));
    }

    [TestCase(P1ActionId.Cascade)]
    [TestCase(P1ActionId.Splash)]
    [TestCase(P1ActionId.HandOfPain)]
    [TestCase(P1ActionId.HandOfPrayer)]
    [TestCase(P1ActionId.HandOfParting)]
    [TestCase(P1ActionId.Outburst)]
    public void ARaidwideHitsEveryoneAndKillsNobody(uint actionId)
    {
        var raidwide = new[] { TeaP1Actions.Cascade, TeaP1Actions.Splash, TeaP1Actions.HandOfPain, TeaP1Actions.HandOfPrayer, TeaP1Actions.HandOfParting, TeaP1Actions.Outburst }
            .Single(action => action.ActionId == actionId);
        var run = Cast((world, liquid, _) =>
        {
            world.Events.Add(1f, () =>
            {
                for (var seat = 0; seat < PerRole.All.Length; seat++)
                    TimelineRun.Place(world, PerRole.All[seat], OffAxis(45f * seat, 19f).X, OffAxis(45f * seat, 19f).Y);
            });
            world.Events.Add(2f, () => liquid.Cast(raidwide));
        }, out var hit);

        Assert.That(run.Passed, run.ToString);
        Assert.That(hit(actionId).Single(), Is.EquivalentTo(PerRole.All));
    }

    // 90 degrees wide, and as deep as the sheet's range plus the boss's hitbox: 11.5 y from the Living Liquid and
    // 11.6 y from the Liquid Hand. A tank seat's bot lives through it and a non-tank seat dies.
    [TestCase(true)]
    [TestCase(false)]
    public void ACleaveIsAQuarterCircleAtItsTargetThatKillsANonTank(bool swing)
    {
        var run = Cleave(swing, Bots, (world, boss) =>
        {
            var reach = (swing ? 8f : 10f) + boss.HitboxRadius;
            world.Events.Add(1f, () => TimelineRun.Place(world, new Dictionary<PartyRole, Vector2>
            {
                [MainTank] = new(0f, 5f),
                [OffTank] = OffAxis(44f, 10f),
                [RegenHealer] = OffAxis(46f, 10f),
                [ShieldHealer] = new(0f, reach - 0.5f),
                [MeleeDpsA] = new(0f, reach + 0.5f),
                [MeleeDpsB] = OffAxis(-46f, 6f),
                [PhysRangedDps] = new(0f, -3f),
                [CasterDps] = OffAxis(90f, 6f),
            }));
        }, out var hit, out var reachWas);

        Assert.That(reachWas, Is.EqualTo(swing ? 11.5f : 11.6f).Within(0.05f));
        Assert.That(hit, Is.EquivalentTo(new[] { MainTank, OffTank, ShieldHealer }));
        Assert.That(run.Deaths.Select(d => (d.Role, d.ActionId)),
                    Is.EqualTo(new[] { (ShieldHealer, (uint?)(swing ? P1ActionId.FluidSwing : P1ActionId.FluidStrike)) }), run.ToString);
        Assert.That(run.Deaths[0].Cause, Does.Contain(swing ? "Caught in Fluid Swing" : "Caught in Fluid Strike").And.Contain("tank buster"));
    }

    private static readonly TimelineRunOptions HumanTankWithItsOwnActions = HumanMainTank with { UserActionsEnabled = true };

    [Test]
    public void ACleaveKillsAHumanOnATanksJobWithoutACooldown([Values] bool swing,
        [Values(JobId.Warrior, JobId.DarkKnight, JobId.Paladin, JobId.Gunbreaker)] JobId job)
    {
        var run = Cleave(swing, HumanTankWithItsOwnActions, (_, _) => { }, out _, out _, job);

        Assert.That(run.Deaths.Select(d => d.Role), Is.EqualTo(new[] { MainTank }), run.ToString);
        Assert.That(run.Deaths[0].Cause, Does.Contain("not enough mitigation"));
    }

    [Test]
    public void ACooldownTakesAHumanTankThroughACleave([Values] bool swing,
        [Values(JobId.Warrior, JobId.DarkKnight, JobId.Paladin, JobId.Gunbreaker)] JobId job)
    {
        var run = Cleave(swing, HumanTankWithItsOwnActions,
            (world, _) => TopP6HumanMitigation.Press(world, 1.5f, [TopP6HumanMitigation.Rampart]), out _, out _, job);

        Assert.That(run.Passed, run.ToString);
    }

    // The Role setting can seat a human on any job in a tank's seat: it has none of a tank's cooldowns, and is asked
    // for none.
    [Test]
    public void ACleaveAsksNoCooldownOfAHumanOnAnotherJobInATankSeat([Values] bool swing,
        [Values(JobId.WhiteMage, JobId.Dragoon, JobId.Bard, JobId.BlackMage)] JobId job)
    {
        var run = Cleave(swing, HumanTankWithItsOwnActions, (_, _) => { }, out _, out _, job);

        Assert.That(run.Passed, run.ToString);
    }

    [Test]
    public void ACleaveAsksNoCooldownOfABotTankOrWithTheSettingOrTheModuleOff([Values] bool swing)
    {
        var bot = Cleave(swing, Bots with { UserActionsEnabled = true }, (_, _) => { }, out _, out _);
        var settingOff = Cleave(swing, HumanTankWithItsOwnActions with { Configure = config => config.EnableTankMitigation = false }, (_, _) => { }, out _, out _, JobId.Warrior);
        var moduleOff = Cleave(swing, HumanMainTank, (_, _) => { }, out _, out _, JobId.Warrior);

        Assert.Multiple(() =>
        {
            Assert.That(bot.Passed, bot.ToString);
            Assert.That(settingOff.Passed, settingOff.ToString);
            Assert.That(moduleOff.Passed, moduleOff.ToString);
        });
    }

    // Fluid Swing from the Living Liquid, or Fluid Strike from the Liquid Hand, at 2.0, at the main tank 5 y south of
    // it; everyone else is out of it unless `place` says otherwise. `job` is the human's, as its client has it.
    private static TimelineRunResult Cleave(bool swing, TimelineRunOptions options, Action<SimWorld, SimEnemy> place,
        out IEnumerable<PartyRole> hit, out float reach, JobId? job = null)
    {
        var cleave = swing ? TeaP1Actions.FluidSwing : TeaP1Actions.FluidStrike;
        var reached = 0f;
        var run = Cast(new Placement(Vector3.Zero, 0f), (world, boss, _) =>
        {
            if (job is { } humans) ((FakeBattleCharas)Natives.BattleCharas).Player.ClassJob = (byte)humans;
            world.Events.Add(0.9f, () => EveryoneOutOfTheWayBut(world, (MainTank, new Vector2(0f, 5f))));
            place(world, boss);
            world.Events.Add(2f, () =>
            {
                reached = (swing ? 8f : 10f) + boss.HitboxRadius;
                boss.Cast(cleave, world.Party.Get(MainTank));
            });
        }, out var hits, options, swing ? P1BNpcBaseId.LivingLiquid : P1BNpcBaseId.LiquidHand);
        hit = hits(cleave.ActionId).Single();
        reach = reached;
        return run;
    }

    private static void EveryoneOutOfTheWayBut(SimWorld world, params (PartyRole Role, Vector2 Spot)[] placed)
    {
        TimelineRun.Place(world, PerRole.All.ToDictionary(r => r, _ => OutOfTheWay));
        foreach (var (role, spot) in placed)
            TimelineRun.Place(world, role, spot.X, spot.Y);
    }

    // `degrees` off the boss's facing south, to its left when positive.
    private static Vector2 OffAxis(float degrees, float distance)
    {
        var radians = degrees * MathF.PI / 180f;
        return new Vector2(distance * MathF.Sin(radians), distance * MathF.Cos(radians));
    }

    private static TimelineRunResult Cast(Action<SimWorld, SimEnemy, TeaP1Presses> schedule, out Func<uint, IReadOnlyList<IEnumerable<PartyRole>>> hit,
        TimelineRunOptions? options = null)
        => Cast(new Placement(Vector3.Zero, 0f), schedule, out hit, options);

    // A boss spawned at `at`; `hit` gives the seats each ActionEffect of an action hit, in the order they went out.
    private static TimelineRunResult Cast(Placement at, Action<SimWorld, SimEnemy, TeaP1Presses> schedule,
        out Func<uint, IReadOnlyList<IEnumerable<PartyRole>>> hit, TimelineRunOptions? options = null, uint bnpcBaseId = P1BNpcBaseId.LivingLiquid)
    {
        Dictionary<string, PartyRole> seats = [];
        TeaP1Presses? presses = null;
        var run = TimelineRun.Execute(world =>
        {
            presses = new TeaP1Presses(world);
            var boss = world.SpawnEnemy(new EnemySpawnConfig(
                BNpcBaseId: bnpcBaseId,
                NameId: bnpcBaseId == P1BNpcBaseId.LivingLiquid ? P1BNpcNameId.LivingLiquid : P1BNpcNameId.LiquidHand,
                Level: Level,
                Placement: at))!;
            world.Events.Add(0.5f, () => seats = TimelineRun.Seats(world));
            schedule(world, boss, presses);
        }, options ?? Bots);
        presses?.Dispose();
        hit = actionId => run.Sent("ability", actionId).Select(sent => sent.Hits.Select(id => seats[id])).ToList();
        return run;
    }

    private static Vector2 Flat(Vector3 position) => new(position.X, position.Z);
}
