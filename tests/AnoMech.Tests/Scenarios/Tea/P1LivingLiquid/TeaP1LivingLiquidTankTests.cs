using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea;
using AnoMech.Scenarios.Tea.P1LivingLiquid;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.P1LivingLiquid.TeaP1LivingLiquidPart;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using Stage = AnoMech.Tests.TeaP1LivingLiquidStage;

namespace AnoMech.Tests;

// What the tanks play: each boss follows the one target the run gives it and cleaves it, the Living Liquid holds its
// facing through a Protean sequence, and a human tank's own cooldown is asked for under Drainage.
public class TeaP1LivingLiquidTankTests
{
    private const string Died = " died: ";

    // The bosses spawned in the middle with nobody near; each tank is put 10 y off at 1.0.
    [TestCase(false)]
    [TestCase(true)]
    public void EachBossWalksToItsTargetAndStopsAHitboxAndHalfAYalmShort(bool human)
    {
        float liquidStops = 0f, handStops = 0f, liquidLooks = 0f;
        var run = Bosses((world, fight) =>
        {
            world.Events.Add(0.5f, fight.TakeTargets);
            world.Events.Add(1f, () =>
            {
                TimelineRun.Place(world, MainTank, 10f, 0f);
                TimelineRun.Place(world, OffTank, -8f, 6f);
            });
            world.Events.Add(5f, () =>
            {
                var (mainTank, offTank) = (world.Party.Get(MainTank)!, world.Party.Get(OffTank)!);
                liquidStops = Vector3.Distance(fight.Position(TeaP1Boss.LivingLiquid), mainTank.Position);
                handStops = Vector3.Distance(fight.Position(TeaP1Boss.LiquidHand), offTank.Position);
                liquidLooks = TeaP1LivingLiquidScenarioTests.Turn(fight.Liquid!.Rotation, fight.Liquid.Placement().Face(mainTank.Position).Rotation);
            });
        }, human);

        Assert.That(run.Passed, run.ToString);
        Assert.That(liquidStops, Is.EqualTo(4.0f).Within(0.1f), "the Living Liquid, hitbox 3.5");
        Assert.That(handStops, Is.EqualTo(2.1f).Within(0.1f), "the Liquid Hand, hitbox 1.6");
        Assert.That(liquidLooks, Is.Zero.Within(0.02f), "it faces its target");
    }

    [Test]
    public void ABossWithNoTargetStaysWhereItIs()
    {
        Vector3 liquid = default, hand = default;
        var run = Bosses((world, fight) =>
        {
            world.Events.Add(1f, () => TimelineRun.Place(world, MainTank, 10f, 0f));
            world.Events.Add(4f, () => (liquid, hand) = (fight.Position(TeaP1Boss.LivingLiquid), fight.Position(TeaP1Boss.LiquidHand)));
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That((liquid, hand), Is.EqualTo((Vector3.Zero, new Vector3(-2f, 0f, 2f))));
    }

    // The target given last is the one followed: what the pull's Provoke will call.
    [Test]
    public void ANewTargetIsFollowedAtOnce()
    {
        float toTheOffTank = 0f;
        PartyRole? target = null;
        var run = Bosses((world, fight) =>
        {
            world.Events.Add(0.5f, fight.TakeTargets);
            world.Events.Add(1f, () =>
            {
                TimelineRun.Place(world, MainTank, 10f, 0f);
                TimelineRun.Place(world, OffTank, -8f, 6f);
            });
            world.Events.Add(3f, () => fight.SetTarget(TeaP1Boss.LivingLiquid, world.Party.Get(OffTank)!));
            world.Events.Add(8f, () =>
            {
                toTheOffTank = Vector3.Distance(fight.Position(TeaP1Boss.LivingLiquid), world.Party.Get(OffTank)!.Position);
                target = TeaP1LivingLiquidScenarioTests.Seat(fight.Target(TeaP1Boss.LivingLiquid));
            });
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(target, Is.EqualTo(OffTank));
        Assert.That(toTheOffTank, Is.EqualTo(4.0f).Within(0.1f));
    }

    // Each boss starts a cast at 2.0 (Cascade, 4 s; Hand of Pain, 3 s) and its tank walks 10 y off at 2.5.
    [Test]
    public void NeitherBossWalksWhileItCasts()
    {
        var liquid = new List<Vector3>();
        var hand = new List<Vector3>();
        var run = Bosses((world, fight) =>
        {
            world.Events.Add(0.5f, fight.TakeTargets);
            world.Events.Add(0.6f, () =>
            {
                TimelineRun.Place(world, MainTank, 3.9f, 0f);
                TimelineRun.Place(world, OffTank, -3.4f, 3.4f);
            });
            fight.Cascade(2f, null);
            fight.HandOfPain(2f);
            world.Events.Add(2.5f, () =>
            {
                TimelineRun.Place(world, MainTank, 14f, 0f);
                TimelineRun.Place(world, OffTank, -10f, 10f);
            });
            foreach (var at in new[] { 1.9f, 4.9f, 5.9f, 9.5f })
                world.Events.Add(at, () =>
                {
                    liquid.Add(fight.Position(TeaP1Boss.LivingLiquid));
                    hand.Add(fight.Position(TeaP1Boss.LiquidHand));
                });
        });

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(liquid[1], Is.EqualTo(liquid[0]), "the Living Liquid, 2.9 s into Cascade");
            Assert.That(liquid[2], Is.EqualTo(liquid[0]), "the Living Liquid, 3.9 s into Cascade");
            Assert.That(Vector3.Distance(liquid[3], new Vector3(10f, 0f, 0f)), Is.LessThan(0.1f), $"once it has cast, the Living Liquid at {liquid[3]}");
            Assert.That(hand[1], Is.EqualTo(hand[0]), "the Liquid Hand, 2.9 s into Hand of Pain");
            Assert.That(Vector3.Distance(hand[3], new Vector3(-10f, 0f, 10f)), Is.EqualTo(2.1f).Within(0.1f), $"once it has cast, the Liquid Hand at {hand[3]}");
        });
    }

    // The main tank walks round to the east-south-east of the Living Liquid during the sequence, 4.3 y from it to bait
    // the first set and 7.4 y for the second. The Liquid faced south as its telegraphed cast started at 4.8; its last
    // set is at 13.0.
    [Test]
    public void TheLiquidNeitherTurnsNorWalksFromItsTelegraphedCastUntilASecondAfterItsLastSet([Values(TeaP1OpenQuarter.North, TeaP1OpenQuarter.Southeast)] TeaP1OpenQuarter open)
    {
        var stage = new Stage { Open1 = open, Last = Protean1 };
        var watch = new TeaP1LivingLiquidScenarioTests.Watch();
        var seen = new List<(float At, bool Locked, float? LockedFacing, float Rotation, Vector3 Position)>();
        var (bait, backedOut) = (Stage.Off(Vector2.Zero, 4.3f, 125f), Stage.Off(Vector2.Zero, 7.4f, 125f));
        var run = stage.Run(MainTank, stopAt: 16f, takeovers: [new Takeover(6f, stage.Arena1(bait.X, bait.Y)), new Takeover(10.5f, stage.Arena1(backedOut.X, backedOut.Y))], probe: p =>
        {
            watch.See(p);
            var fight = Stage.Fight(p);
            foreach (var at in new[] { 4.7f, 4.9f, 9.8f, 13.9f, 15.5f })
                if (p.Crossed(at)) seen.Add((at, fight.FacingLocked, fight.LockedFacing, fight.Liquid!.Rotation, fight.Position(TeaP1Boss.LivingLiquid)));
        });

        Assert.That(run.Passed, run.ToString);
        var (south, toItsTank) = (stage.Frame1.Facing(180f), stage.Frame1.Facing(125f));
        Assert.Multiple(() =>
        {
            Assert.That(seen.Select(s => s.Locked), Is.EqualTo(new[] { false, true, true, true, false }));
            Assert.That(seen[0].LockedFacing, Is.Null);
            foreach (var locked in seen.Skip(1).Take(3))
            {
                Assert.That(TeaP1LivingLiquidScenarioTests.Turn(locked.Rotation, south), Is.Zero.Within(0.01f), $"at {locked.At} it still faces south");
                Assert.That(TeaP1LivingLiquidScenarioTests.Turn(locked.LockedFacing!.Value, south), Is.Zero.Within(0.01f), $"at {locked.At}");
                Assert.That(locked.Position.Length(), Is.LessThan(0.01f), $"at {locked.At} it has not walked");
            }
            Assert.That(TeaP1LivingLiquidScenarioTests.Turn(seen[4].Rotation, toItsTank), Is.Zero.Within(0.05f), "released, it turns to its tank");
            Assert.That(seen[4].Position.Length(), Is.EqualTo(3.4f).Within(0.1f), "and walks up to it");
            Assert.That(watch.Sent("ability", P1ActionId.ProteanWaveStraightAhead).Select(w => TeaP1LivingLiquidScenarioTests.Turn(w.Rotation, south)),
                        Is.EqualTo(new[] { 0f, 0f }).Within(0.01f), "both sets' cone straight ahead goes south");
        });
    }

    // Given the off tank at 8.0, the Living Liquid goes to it only once its facing is released, at 14.0.
    [Test]
    public void ATargetGivenDuringTheSequenceIsFollowedOnceItEnds()
    {
        var seen = new List<(PartyRole? Target, Vector3 Position)>();
        var run = new Stage { Last = Protean1 }.Run(MainTank, stopAt: 18f, probe: p =>
        {
            var fight = Stage.Fight(p);
            if (p.Crossed(8f)) fight.SetTarget(TeaP1Boss.LivingLiquid, p.Member(OffTank)!);
            foreach (var at in new[] { 8.1f, 13.9f, 17f })
                if (p.Crossed(at)) seen.Add((TeaP1LivingLiquidScenarioTests.Seat(fight.Target(TeaP1Boss.LivingLiquid)), fight.Position(TeaP1Boss.LivingLiquid)));
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(seen.Select(s => s.Target), Is.All.EqualTo(OffTank));
        Assert.That(seen[0].Position.Length(), Is.LessThan(0.01f));
        Assert.That(seen[1].Position.Length(), Is.LessThan(0.01f));
        Assert.That(Vector3.Distance(seen[2].Position, new Vector3(-5.4f, 0f, 5.4f)), Is.EqualTo(4.0f).Within(0.1f), $"the Living Liquid at {seen[2].Position}");
    }

    // -- The cleaves --
    // No cleave falls in the parts built; the opening schedules them. The main tank stands east of the Living Liquid
    // and the off tank south-west of the Liquid Hand, each outside the other boss's quarter circle.

    [Test]
    public void EachBossCleavesTheTargetItWasLastGiven()
    {
        Dictionary<string, PartyRole> seats = [];
        var run = Bosses((world, fight) =>
        {
            world.Events.Add(0.4f, () => seats = TimelineRun.Seats(world));
            world.Events.Add(0.5f, fight.TakeTargets);
            fight.FluidSwing(2f);
            fight.FluidStrike(2f);
            world.Events.Add(4f, () => fight.SetTarget(TeaP1Boss.LivingLiquid, world.Party.Get(OffTank)!));
            fight.FluidSwing(7f);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(run.Sent("ability", P1ActionId.FluidSwing).Select(swing => swing.Hits.Select(id => seats[id]).Single()), Is.EqualTo(new[] { MainTank, OffTank }));
        Assert.That(run.Sent("ability", P1ActionId.FluidSwing).Select(swing => swing.Time), Is.EqualTo(new[] { 2f, 7f }).Within(TimelineRun.FrameLate));
        Assert.That(run.Sent("ability", P1ActionId.FluidStrike).Select(strike => strike.Hits.Select(id => seats[id]).Single()), Is.EqualTo(new[] { OffTank }));
    }

    // Neither has a target at 1.0. At 4.0 the off tank, the Hand's, has been dead a second.
    [Test]
    public void ABossWithNoTargetOrADeadOneDoesNotCleave()
    {
        var run = Bosses((world, fight) =>
        {
            fight.FluidSwing(1f);
            fight.FluidStrike(1f);
            world.Events.Add(2f, fight.TakeTargets);
            world.Events.Add(3f, () => world.Party.Get(OffTank)!.Die(P1ActionId.Splash));
            fight.FluidSwing(4f);
            fight.FluidStrike(4f);
        });

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(run.Deaths.Select(d => d.Role), Is.EqualTo(new[] { OffTank }), run.ToString);
        Assert.That(run.Sent("ability", P1ActionId.FluidSwing).Select(swing => swing.Time), Is.EqualTo(new[] { 4f }).Within(TimelineRun.FrameLate));
        Assert.That(run.Sent("ability", P1ActionId.FluidStrike), Is.Empty);
    }

    // -- The Liquid Hand's three distances --
    // Neither boss has a target here, so each stays where it is put: the Living Liquid on the centre and the Liquid
    // Hand that far east of it, centre to centre. The Hand glows at 2.0 and slams at 4.0.

    [TestCase(11.5f, false)]
    [TestCase(12.5f, true)]
    public void TheHandShowsAPalmOnlyWithTheBossesMoreThanTwelveYalmsApartAsItGlows(float apart, bool palm)
    {
        var (run, shown) = GlowAndSlam(apartAtTheGlow: apart, apartAtTheSlam: palm ? 0.5f : 18f);

        Assert.That(run.Passed, run.ToString);
        Assert.That(shown, Is.EqualTo(palm));
    }

    [TestCase(4.9f, false)]
    [TestCase(5.3f, true)]
    public void APalmsSlamNeedsTheBossesWithinFivePointOneYalms(float apart, bool kills)
    {
        var (run, shown) = GlowAndSlam(apartAtTheGlow: 18f, apartAtTheSlam: apart);

        Assert.That(shown, Is.True);
        if (kills) AssertTheSlamKilledEveryone(run, P1ActionId.HandOfPrayer, TeaP1LivingLiquid.PalmFoundThemApart);
        else Assert.That(run.Passed, run.ToString);
    }

    [TestCase(11.5f, true)]
    [TestCase(12.5f, false)]
    public void AFistsSlamNeedsTheBossesMoreThanTwelveYalmsApart(float apart, bool kills)
    {
        var (run, shown) = GlowAndSlam(apartAtTheGlow: 3f, apartAtTheSlam: apart);

        Assert.That(shown, Is.False);
        if (kills) AssertTheSlamKilledEveryone(run, P1ActionId.HandOfParting, TeaP1LivingLiquid.FistFoundThemTogether);
        else Assert.That(run.Passed, run.ToString);
    }

    private static (TimelineRunResult Run, bool? Shown) GlowAndSlam(float apartAtTheGlow, float apartAtTheSlam)
    {
        bool? shown = null;
        var run = Bosses((world, fight) =>
        {
            world.Events.Add(1f, () => fight.Hand!.SetPosition(new Vector3(apartAtTheGlow, 0f, 0f)));
            fight.TheHandGlows(2f);
            world.Events.Add(2.5f, () => shown = fight.HandShowsAPalm);
            world.Events.Add(3f, () => fight.Hand!.SetPosition(new Vector3(apartAtTheSlam, 0f, 0f)));
            fight.TheHandSlams(4f);
        });
        return (run, shown);
    }

    private static void AssertTheSlamKilledEveryone(TimelineRunResult run, uint actionId, string because)
    {
        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(run.Deaths.Select(d => d.Role), Is.EquivalentTo(PerRole.All), run.ToString);
            Assert.That(run.Deaths.Select(d => d.ActionId), Is.All.EqualTo((uint?)actionId), run.ToString);
            Assert.That(run.Deaths.Select(d => d.Time), Is.All.EqualTo(4f).Within(0.1f), run.ToString);
            Assert.That(run.Deaths.Select(d => d.Cause), Is.All.Contain(because), run.ToString);
        });
    }

    // -- A human tank's own mitigation under Drainage --
    // The human holds its own tether, alone in its circle, with "Resolve your own actions" on.

    private static readonly JobId[] TankJobs = [JobId.Warrior, JobId.DarkKnight, JobId.Paladin, JobId.Gunbreaker];

    private static Stage HumanTankThroughDrainage => new() { Last = Protean1, HumanUntil = 60f, UserActionsEnabled = true };

    [Test]
    public void DrainageKillsAHumanTankOnATanksJobWithoutACooldown([Values(MainTank, OffTank)] PartyRole seat,
        [ValueSource(nameof(TankJobs))] JobId job)
    {
        var run = ThroughDrainage(HumanTankThroughDrainage, seat, job);

        TeaP1LimitCutScenarioTests.AssertFirstDeath(run, seat, P1ActionId.Drainage, 24.6f, TeaP1LivingLiquid.DrainageWithoutMitigation);
    }

    [Test]
    public void ACooldownTakesAHumanTankThroughDrainage([Values(MainTank, OffTank)] PartyRole seat, [ValueSource(nameof(TankJobs))] JobId job)
    {
        var run = ThroughDrainage(HumanTankThroughDrainage, seat, job, TopP6HumanMitigation.Rampart);

        Assert.That(run.Passed, run.ToString);
    }

    // Camouflage and Oblation each take a tenth off, half of what Drainage asks.
    [TestCase(JobId.Gunbreaker, TopP6HumanMitigation.Camouflage, (ushort)1832)]
    [TestCase(JobId.DarkKnight, TopP6HumanMitigation.Oblation, (ushort)2682)]
    public void AShortCooldownAloneDoesNotTakeAHumanTankThroughDrainage(JobId job, uint shortCooldown, ushort itsStatus)
    {
        var up = false;
        var run = ThroughDrainage(HumanTankThroughDrainage, MainTank, job, shortCooldown, probe: p =>
        {
            if (p.Crossed(24.5f)) up = p.Member(MainTank)!.HasStatus(itsStatus);
        });

        Assert.That(up, "the cooldown is up as Drainage goes off");
        TeaP1LimitCutScenarioTests.AssertFirstDeath(run, MainTank, P1ActionId.Drainage, 24.6f, TeaP1LivingLiquid.DrainageWithoutMitigation);
    }

    [Test]
    public void DrainageAsksNoCooldownOfAHumanOnAnotherJobInATankSeat([Values(MainTank, OffTank)] PartyRole seat,
        [Values(JobId.WhiteMage, JobId.Monk, JobId.Machinist, JobId.Summoner)] JobId job)
    {
        var run = ThroughDrainage(HumanTankThroughDrainage, seat, job);

        Assert.That(run.Passed, run.ToString);
    }

    [Test]
    public void DrainageAsksNoCooldownWithTheSettingOrTheModuleOffOrOfABot([Values(MainTank, OffTank)] PartyRole seat)
    {
        var settingOff = ThroughDrainage(new Stage { Last = Protean1, HumanUntil = 60f, UserActionsEnabled = true, Configure = config => config.EnableTankMitigation = false }, seat, JobId.Warrior);
        var moduleOff = ThroughDrainage(new Stage { Last = Protean1, HumanUntil = 60f }, seat, JobId.Warrior);
        var bot = ThroughDrainage(new Stage { Last = Protean1, UserActionsEnabled = true }, seat, JobId.Warrior);

        Assert.Multiple(() =>
        {
            Assert.That(settingOff.Passed, settingOff.ToString);
            Assert.That(moduleOff.Passed, moduleOff.ToString);
            Assert.That(bot.Passed, bot.ToString);
        });
    }

    // A tank in the wrong spot hears of its spot, not of its cooldowns.
    [Test]
    public void ATankInTwoCirclesIsToldSoWhateverItsCooldowns()
    {
        var run = ThroughDrainage(HumanTankThroughDrainage, OffTank, JobId.Paladin, press: null, new Takeover(23f, new Vector2(-2.24f, 2.76f)));

        Assert.That(run.Deaths.Select(d => d.Role), Is.EquivalentTo(new[] { MainTank, OffTank }), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Cause), Is.All.Contain(TeaP1LivingLiquid.TwoDrainages));
    }

    [Test]
    public void GodmodeReportsAHumanTankShortOfMitigationOnce()
    {
        IReadOnlyList<string> chat = [];
        var stage = new Stage { Last = Protean1, HumanUntil = 60f, UserActionsEnabled = true, GodMode = true };
        var run = ThroughDrainage(stage, MainTank, JobId.Warrior, probe: p => chat = p.Chat.ToList());

        Assert.That(run.Deaths, Is.Empty, run.ToString);
        Assert.That(chat.Where(line => line.Contains(Died)).ToList(),
                    Has.Count.EqualTo(1).And.All.EndsWith($"{Died}Drainage ({TeaP1LivingLiquid.DrainageWithoutMitigation})"), () => string.Join(Environment.NewLine, chat));
    }

    // Protean 1 on the stage with the human in `seat` on `job`, as its client has it; `press` is pressed at 20.0.
    private static ScenarioRun ThroughDrainage(Stage stage, PartyRole seat, JobId job, uint? press = null, Takeover? takeover = null, Action<ScenarioProbe>? probe = null)
        => stage.Run(seat, takeovers: takeover is null ? [] : [takeover], probe: p =>
        {
            if (p.Crossed(0.1f)) ((FakeBattleCharas)Natives.BattleCharas).Player.ClassJob = (byte)job;
            if (press is { } pressed && p.Crossed(20f)) TopP6HumanMitigation.PressNow(p.World, [pressed]);
            probe?.Invoke(p);
        });

    // The Living Liquid spawned on the centre and the Liquid Hand at (-2, 2), both facing south, with everyone but
    // the tanks far off; the boss piece ticks as a scenario ticks it.
    private static TimelineRunResult Bosses(Action<SimWorld, TeaP1LivingLiquid> schedule, bool human = false)
    {
        TeaP1LivingLiquid? piece = null;
        TeaP1Presses? presses = null;
        var run = TimelineRun.Execute(world =>
        {
            presses = new TeaP1Presses(world);
            var fight = piece = new TeaP1LivingLiquid(world, presses);
            fight.Spawn(new Placement(Vector3.Zero, 0f), new Placement(new Vector3(-2f, 0f, 2f), 0f));
            world.Events.Add(0.1f, () =>
            {
                TimelineRun.Place(world, PerRole.All.ToDictionary(role => role, _ => new Vector2(0f, -18f)));
                TimelineRun.Place(world, MainTank, 3.9f, 0f);
                TimelineRun.Place(world, OffTank, -3.4f, 3.4f);
            });
            schedule(world, fight);
        }, new TimelineRunOptions { Phase = TeaZone.P1, HumanPlayer = human, Tick = () => piece?.Tick() });
        presses?.Dispose();
        return run;
    }
}
