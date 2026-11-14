using System.Globalization;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea;
using AnoMech.Scenarios.Tea.P1LivingLiquid;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.P1LivingLiquid.TeaP1LivingLiquidPart;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using Stage = AnoMech.Tests.TeaP1LivingLiquidStage;

namespace AnoMech.Tests;

// The fight's rules, played with no strat (TeaP1LivingLiquidStage, whose header has the timeline): everyone is put
// where the fight asks, and a test moves the player's seat into one mistake. Spots are relative to the tornadoes
// standing then (x east, z south, the open quarter north), and both sets leave the north open unless a test says
// otherwise, so a relative spot is also the arena's. A hit an action decides kills a second after it; one the fight
// decides in code (a puddle, Drainage, Throttle, the slam, an Embolus) kills at once.
public class TeaP1LivingLiquidScenarioTests
{
    private const string Died = " died: ";

    private static readonly Stage ProteanOne = new() { Last = Protean1 };
    private static readonly Stage CascadeTwo = new() { First = Cascade2 };

    [Test]
    public void EveryoneWhereTheFightAsksSurvives([Values] TeaP1OpenQuarter open1, [Values] TeaP1OpenQuarter open2)
    {
        var run = new Stage { Open1 = open1, Open2 = open2 }.Run(MainTank);

        Assert.That(run.Passed, run.ToString);
        Assert.That(run.Elapsed, Is.InRange(68f, 70f), "the run ends by itself after the Liquid falls");
    }

    [Test]
    public void EachPartPlaysOnItsOwn([Values] TeaP1OpenQuarter open1, [Values(TeaP1OpenQuarter.North, TeaP1OpenQuarter.Southeast, TeaP1OpenQuarter.West)] TeaP1OpenQuarter open2)
    {
        var proteanOne = new Stage { Open1 = open1, Last = Protean1 }.Run(OffTank);
        var cascadeTwo = new Stage { Open1 = open1, Open2 = open2, First = Cascade2 }.Run(RegenHealer);

        Assert.Multiple(() =>
        {
            Assert.That(proteanOne.Passed, proteanOne.ToString);
            Assert.That(proteanOne.Elapsed, Is.InRange(27f, 30f), "Protean 1 ends after its last Hand of Pain");
            Assert.That(cascadeTwo.Passed, cascadeTwo.ToString);
            Assert.That(cascadeTwo.Elapsed, Is.InRange(44.2f, 46.2f), "Cascade 2 ends after the Liquid falls");
        });
    }

    // -- Puddles --

    [TestCase(7.4f, true)]
    [TestCase(7.8f, false)]
    public void APuddleReachesSevenPointSixYalmsFromItsTornado(float fromItsCentre, bool kills)
    {
        var run = ProteanOne.Run(CasterDps, stopAt: 4.5f, takeovers: new Takeover(3f, ProteanOne.Arena1(-14.5f + fromItsCentre, 0f)));

        if (kills) TeaP1LimitCutScenarioTests.AssertFirstDeath(run, CasterDps, SimCharacterDeathExtensions.Environment, 3.0f, TeaP1Tornadoes.StoodInThePuddle);
        else Assert.That(run.Passed, run.ToString);
    }

    [Test]
    public void EachOfTheThreeTornadoesKeepsAPuddle([Values] TeaP1OpenQuarter open, [Values] TeaP1Tornado tornado)
    {
        var stage = new Stage { Open1 = open, Last = Protean1 };
        var centre = stage.Frame1.Centre(tornado);
        var run = stage.Run(MeleeDpsB, takeovers: new Takeover(3f, new Vector2(centre.X, centre.Z) * 0.8f));

        TeaP1LimitCutScenarioTests.AssertFirstDeath(run, MeleeDpsB, SimCharacterDeathExtensions.Environment, 3.0f, TeaP1Tornadoes.StoodInThePuddle);
    }

    // The death is its own whole message: no action is named.
    [Test]
    public void GodmodeHearsOfAPuddleOnceHoweverLongItIsStoodIn()
    {
        IReadOnlyList<string> chat = [];
        var run = new Stage { Last = Protean1, GodMode = true }.Run(CasterDps, stopAt: 9f,
            probe: p => chat = p.Chat.ToList(), takeovers: new Takeover(3f, new Vector2(-12f, 0f)));

        Assert.That(run.Deaths, Is.Empty, run.ToString);
        Assert.That(chat.Where(line => line.Contains(Died)).ToList(),
                    Has.Count.EqualTo(1).And.All.EndsWith(Died + TeaP1Tornadoes.StoodInThePuddle), () => string.Join(Environment.NewLine, chat));
    }

    // Cascade 2 lands at 5.9. Its tornadoes can stand anywhere, so their puddles harm nobody for a second.
    [TestCase(null, 6.9f)]
    [TestCase(6.5f, null)]
    public void ANewPuddleKillsWhoeverIsStillInItASecondAfterItAppears(float? leavesAt, float? diesAt)
    {
        Takeover[] takeovers = leavesAt is { } at
            ? [new(1f, new Vector2(-12f, 0f)), new(at, new Vector2(0f, -3f))]
            : [new(1f, new Vector2(-12f, 0f))];
        var run = CascadeTwo.Run(MeleeDpsB, stopAt: 9f, takeovers: takeovers);

        if (diesAt is { } dies) TeaP1LimitCutScenarioTests.AssertFirstDeath(run, MeleeDpsB, SimCharacterDeathExtensions.Environment, dies, TeaP1Tornadoes.StoodInThePuddle);
        else Assert.That(run.Passed, run.ToString);
    }

    [Test]
    public void BotsWalkRoundAPuddleWhileItStands()
    {
        var obstacles = new List<(float At, int Count)>();
        var run = new Stage().Run(MainTank, probe: p =>
        {
            foreach (var at in new[] { 1f, 26f, Stage.CascadeTwo + 5f, Stage.CascadeTwo + 7f })
                if (p.Crossed(at)) obstacles.Add((at, p.World.Obstacles.All.Count));
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(obstacles.Select(o => o.Count), Is.EqualTo(new[] { 3, 0, 0, 3 }));
    }

    // -- The Living Liquid's Protean Waves --

    // Six cones overlap on the six who stacked north.
    [Test]
    public void StayingInTheStackThroughTheTelegraphKills()
    {
        var run = new Stage { Last = Protean1, HumanUntil = 5f }.Run(ShieldHealer);

        TeaP1LimitCutScenarioTests.AssertFirstDeath(run, ShieldHealer, P1ActionId.ProteanWaveAtAPlayer, 8.5f, TeaP1Actions.SecondWave);
    }

    // R1 stands apart north-east as the cast starts and stays in the one cone aimed at it.
    [Test]
    public void ATelegraphedWaveAloneLeavesItsDebuffAndThrowsAHumanToTheRim()
    {
        var carries = false;
        Vector3 stands = default;
        var run = ProteanOne.Run(PhysRangedDps, stopAt: 9.5f, takeovers: new Takeover(4f, new Vector2(8f, -8f)), probe: p =>
        {
            if (!p.Crossed(9f)) return;
            carries = p.Member(PhysRangedDps)!.HasStatus(P1StatusId.WaterResistanceDown);
            stands = p.Member(PhysRangedDps)!.Position;
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(carries, "it carries Water Resistance Down");
        Assert.That(Vector2.Distance(new Vector2(stands.X, stands.Z), new Vector2(14.14f, -14.14f)), Is.LessThan(0.1f), $"thrown to {stands}");
    }

    // M1 baits the first set and never backs out.
    [Test]
    public void AFirstSetBaiterStillAmongTheFourClosestForTheSecondSetDies()
    {
        var run = new Stage { Last = Protean1, HumanUntil = 9f }.Run(MeleeDpsA);

        TeaP1LimitCutScenarioTests.AssertFirstDeath(run, MeleeDpsA, P1ActionId.ProteanWaveAtTheClosest, 14.0f, TeaP1Actions.SecondWave);
    }

    // South of the Liquid, 6.5 y out: in the cone it fires straight ahead, and not among its four closest.
    [Test]
    public void TheConeStraightAheadAloneLeavesItsDebuffAndKillsNobody()
    {
        var carries = false;
        var run = ProteanOne.Run(CasterDps, stopAt: 12f, takeovers: new Takeover(9f, new Vector2(0f, 6.5f)), probe: p =>
        {
            if (p.Crossed(10.5f)) carries = p.Member(CasterDps)!.HasStatus(P1StatusId.WaterResistanceDown);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(carries, "it carries Water Resistance Down");
    }

    // M2, one of the four closest, baits its own cone down the one the Liquid fires straight ahead.
    [Test]
    public void BaitingInFrontOfTheLiquidTakesTwoWaves()
    {
        var run = ProteanOne.Run(MeleeDpsB, takeovers: new Takeover(9f, new Vector2(0f, 4f)));

        TeaP1LimitCutScenarioTests.AssertFirstDeath(run, MeleeDpsB, P1ActionId.ProteanWaveAtTheClosest, 10.9f, TeaP1Actions.SecondWave);
    }

    // M2 baits 10 degrees from the main tank: each stands in the other's cone.
    [Test]
    public void TwoBaitersTenDegreesApartBothDie()
    {
        var run = ProteanOne.Run(MeleeDpsB, takeovers: new Takeover(9f, Stage.Off(Vector2.Zero, 4f, 127.8f)));

        Assert.That(run.Deaths.Select(d => d.Role), Is.EquivalentTo(new[] { MainTank, MeleeDpsB }), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Time), Is.All.EqualTo(10.9f).Within(0.1f));
        Assert.That(run.Deaths.Select(d => d.Cause), Is.All.Contain(TeaP1Actions.SecondWave));
    }

    // Each wave goes where the fight aims it: the telegraphed ones at 4.8 at where everyone stands then, each set
    // at the four closest as it fires.
    [Test]
    public void TheWavesAreAimedAtEveryPlayerThenAtTheFourClosest([Values(TeaP1OpenQuarter.North, TeaP1OpenQuarter.Southwest)] TeaP1OpenQuarter open)
    {
        var stage = new Stage { Open1 = open, Last = Protean1 };
        var watch = new Watch();
        var run = stage.Run(MainTank, probe: watch.See);

        Assert.That(run.Passed, run.ToString);
        var south = stage.Frame1.Facing(180f);
        var telegraphed = watch.Sent("cast", P1ActionId.ProteanWaveAtAPlayer);
        Assert.Multiple(() =>
        {
            Assert.That(watch.Sent("cast", P1ActionId.ProteanWaveFromTheLiquid).Select(w => (w.At, Turn(w.Rotation, south))),
                        Is.EqualTo(new[] { (4.8f, 0f) }).Within(0.05f), "the Liquid's own, along its facing");
            Assert.That(telegraphed.Select(w => w.At), Is.EqualTo(Enumerable.Repeat(4.8f, 8)).Within(0.05f));
            Assert.That(telegraphed.Count(w => MathF.Abs(Turn(w.Rotation, south)) < 0.01f), Is.EqualTo(2), "two at the tanks, stacked south");
            Assert.That(telegraphed.Count(w => MathF.Abs(Turn(w.Rotation, stage.Frame1.Facing(0f))) < 0.3f), Is.EqualTo(6), "six at the line north");
            Assert.That(watch.Sent("ability", P1ActionId.ProteanWaveStraightAhead).Select(w => (w.At, Turn(w.Rotation, south))),
                        Is.EqualTo(new[] { (9.9f, 0f), (13.0f, 0f) }).Within(0.05f));
            var sets = watch.Sent("ability", P1ActionId.ProteanWaveAtTheClosest);
            Assert.That(sets.Where(w => w.At < 11f).Select(w => w.Hits.Single()), Is.EquivalentTo(new[] { MainTank, OffTank, MeleeDpsA, MeleeDpsB }));
            Assert.That(sets.Where(w => w.At > 11f).Select(w => w.Hits.Single()), Is.EquivalentTo(new[] { RegenHealer, ShieldHealer, PhysRangedDps, CasterDps }));
            Assert.That(sets.Select(w => w.Position.Length()), Is.All.LessThan(0.05f), "from the Living Liquid");
        });
    }

    // -- Sluice --

    [Test]
    public void StayingOnASluiceKills()
    {
        var run = new Stage { Last = Protean1, HumanUntil = 9f }.Run(ShieldHealer);

        TeaP1LimitCutScenarioTests.AssertFirstDeath(run, ShieldHealer, P1ActionId.Sluice, 13.6f, "Stood in a Sluice as it went off");
    }

    // It is laid at 9.9 under where H2 stands then and goes off there: H2 walks in to bait the second set a second
    // late, 11.8 y off.
    [Test]
    public void ASluiceStaysWhereItWasLaid()
    {
        var watch = new Watch();
        var run = ProteanOne.Run(ShieldHealer, probe: watch.See,
            takeovers: [new Takeover(9f, null), new Takeover(11f, new Vector2(2.4f, -2.2f)), new Takeover(14f, new Vector2(0f, -8f))]);

        Assert.That(run.Passed, run.ToString);
        var sluices = watch.Sent("ability", P1ActionId.Sluice);
        Assert.That(sluices.Select(s => s.At), Is.EqualTo(Enumerable.Repeat(12.9f, 4)).Within(0.05f));
        Assert.That(sluices.Select(s => (MathF.Round(s.Position.X, 1), MathF.Round(s.Position.Y, 1))),
                    Is.EquivalentTo(new[] { (10.1f, -8.7f), (11.8f, -9.4f), (-11.0f, -9.5f), (-8.9f, -8.4f) }), "under the four farthest");
    }

    // -- The tornadoes' waves --

    // Each tornado aims at its closest player as its cast starts: the off tank steps closer to the west one than H1.
    [Test]
    public void ATornadoAimsItsTelegraphAtWhoeverIsClosestAsTheCastStarts()
    {
        var watch = new Watch();
        var offTank = Stage.Off(Stage.West, 8f, 90f);
        var run = CascadeTwo.Run(OffTank, stopAt: 24.6f, probe: watch.See, takeovers: new Takeover(24f, offTank));

        Assert.That(run.Passed, run.ToString);
        var casts = watch.Sent("cast", P1ActionId.TornadoWave);
        Assert.That(casts.Select(c => c.At), Is.EqualTo(Enumerable.Repeat(24.2f, 3)).Within(0.05f));
        var fromTheWest = casts.Single(c => Vector2.Distance(c.Position, Stage.West) < 0.1f);
        Assert.That(Turn(fromTheWest.Rotation, Facing(Stage.West, offTank)), Is.Zero.Within(0.01f));
        var fromTheMiddle = casts.Single(c => Vector2.Distance(c.Position, Stage.South) < 0.1f);
        Assert.That(Turn(fromTheMiddle.Rotation, Facing(Stage.South, Stage.Off(Stage.South, 8.5f, 270f))), Is.Zero.Within(0.01f), "at H2");
    }

    // H1 has taken the second set at 26.3 and steps back into the west tornado's telegraph.
    [Test]
    public void ATornadosTelegraphedWaveKillsWhoeverTheLiquidsLastSetHit()
    {
        var run = CascadeTwo.Run(RegenHealer, takeovers: new Takeover(26.5f, Stage.Off(Stage.West, 8.4f, 14f)));

        TeaP1LimitCutScenarioTests.AssertFirstDeath(run, RegenHealer, P1ActionId.TornadoWave, 27.9f, TeaP1Actions.SecondWave);
    }

    // R1 has taken the second set at 26.3 and stands closer to the east tornado than M2, south-east of it, as the unseen
    // waves go off.
    [Test]
    public void ATornadosUnseenWaveGoesToWhoeverIsClosestThen()
    {
        var run = CascadeTwo.Run(PhysRangedDps, takeovers: new Takeover(28f, Stage.Off(Stage.East, 8f, 150f)));

        TeaP1LimitCutScenarioTests.AssertFirstDeath(run, PhysRangedDps, P1ActionId.TornadoWaveUnseen, 30.3f, TeaP1Actions.SecondWave);
    }

    [Test]
    public void TheUnseenTornadoWavesGoToTheThreeBesideThem([Values(TeaP1OpenQuarter.North, TeaP1OpenQuarter.East)] TeaP1OpenQuarter open)
    {
        var watch = new Watch();
        var run = new Stage { First = Cascade2, Open2 = open }.Run(MainTank, probe: watch.See);

        Assert.That(run.Passed, run.ToString);
        var waves = watch.Sent("ability", P1ActionId.TornadoWaveUnseen);
        Assert.That(waves.Select(w => w.At), Is.EqualTo(Enumerable.Repeat(29.3f, 3)).Within(0.05f));
        Assert.That(waves.Select(w => w.Hits.Single()), Is.EquivalentTo(new[] { OffTank, MeleeDpsA, MeleeDpsB }));
        Assert.That(watch.Sent("ability", P1ActionId.TornadoWave).Select(w => w.Hits.Count), Is.EqualTo(new[] { 0, 0, 0 }), "the telegraphed ones are dodged");
    }

    // -- Drainage --

    [Test]
    public void TheTethersComeFromTheirTornadoesAndAreDeclaredFromTheirHolders()
    {
        (SimCharacter? A, SimCharacter? B, TeaP1Tornado From)[] ends = [];
        SimEnemy?[] tornadoes = [];
        bool[] carried = [];
        var none = false;
        var run = ProteanOne.Run(MainTank, probe: p =>
        {
            var fight = Stage.Fight(p);
            if (p.Crossed(19.5f)) none = fight.DrainageTethersOut.Count == 0;
            if (!p.Crossed(21f)) return;
            ends = fight.DrainageTethersOut.Select(t => (t.Tether.A, t.Tether.B, t.From)).ToArray();
            tornadoes = ends.Select(end => fight.Tornadoes[end.From]).ToArray();
            carried = PerRole.All.Select(role => p.Member(role)!.HasTetherInSlot0(P1TetherId.Drainage)).ToArray();
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(none, "no tether before 20.0");
        Assert.Multiple(() =>
        {
            Assert.That(ends.Select(end => (Seat(end.A), end.From)), Is.EqualTo(new[] { ((PartyRole?)OffTank, TeaP1Tornado.West), ((PartyRole?)MainTank, TeaP1Tornado.East) }));
            Assert.That(ends.Select(end => end.B), Is.EqualTo(tornadoes));
            Assert.That(PerRole.All.Where(role => carried[(int)role]), Is.EquivalentTo(new[] { MainTank, OffTank }), "only a holder carries the channel");
        });
    }

    // R2 walks into a puddle at 16.0; the west tornado's tether, rolled onto it, comes out at 20.0 on someone alive whom
    // no roll names, so the east one's still starts on the main tank. The seed picks whom, of six.
    [Test]
    public void ATetherRolledOntoTheDeadStartsOnSomeoneNoRollNames()
    {
        var picked = new HashSet<PartyRole>();
        for (var seed = 1; seed <= 40; seed++)
        {
            var stage = new Stage { Last = Protean1, Seed = seed, Drainage = [new(TeaP1Tornado.West, CasterDps), new(TeaP1Tornado.East, MainTank)] };
            PartyRole? west = null, east = null;
            var run = stage.Run(CasterDps, takeovers: new Takeover(16f, new Vector2(-12f, 0f)), probe: p =>
            {
                if (!p.Crossed(20.5f)) return;
                west = Holder(p, TeaP1Tornado.West);
                east = Holder(p, TeaP1Tornado.East);
            });

            Assert.That(run.Deaths.Select(d => d.Role), Is.EqualTo(new[] { CasterDps }), $"seed {seed}: {run}");
            Assert.That(east, Is.EqualTo(MainTank), $"seed {seed}");
            Assert.That(west, Is.Not.Null.And.Not.EqualTo(CasterDps).And.Not.EqualTo(MainTank), $"seed {seed}");
            picked.Add(west!.Value);
        }

        Assert.That(picked, Has.Count.GreaterThan(3), "the seed picks whom");
    }

    // R2 starts with the west tornado's tether and keeps it, apart from everyone.
    [Test]
    public void ANonTankThatHoldsATetherDies()
    {
        var stage = new Stage { Last = Protean1, Drainage = [new(TeaP1Tornado.West, CasterDps), new(TeaP1Tornado.East, MainTank)] };
        var run = stage.Run(CasterDps, takeovers: new Takeover(19f, new Vector2(-9f, -9.5f)));

        TeaP1LimitCutScenarioTests.AssertFirstDeath(run, CasterDps, P1ActionId.Drainage, 24.6f, TeaP1LivingLiquid.NonTankHoldsATether);
    }

    // M1 stands 5 y from the off tank, which holds its tether.
    [Test]
    public void StandingInAHoldersCircleKills()
    {
        var run = ProteanOne.Run(MeleeDpsA, takeovers: new Takeover(23f, new Vector2(-5.4f, 0.4f)));

        TeaP1LimitCutScenarioTests.AssertFirstDeath(run, MeleeDpsA, P1ActionId.Drainage, 24.6f, TeaP1LivingLiquid.InSomeoneElsesDrainage);
    }

    [TestCase(5.5f, true)]
    [TestCase(6.5f, false)]
    public void TwoHoldingTanksMustStandMoreThanSixYalmsApart(float apart, bool kills)
    {
        var run = ProteanOne.Run(OffTank, takeovers: new Takeover(23f, new Vector2(2.76f - apart, 2.76f)));

        if (!kills)
        {
            Assert.That(run.Passed, run.ToString);
            return;
        }
        Assert.That(run.Deaths.Select(d => (d.Role, d.ActionId)), Is.EquivalentTo(new[] { (MainTank, (uint?)P1ActionId.Drainage), (OffTank, (uint?)P1ActionId.Drainage) }), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Time), Is.All.EqualTo(24.6f).Within(0.1f));
        Assert.That(run.Deaths.Select(d => d.Cause), Is.All.Contain(TeaP1LivingLiquid.TwoDrainages));
    }

    // The main tank holds one; R2 keeps the other in the knot north; the off tank, holding none, stands 5 y from the
    // main tank. The main tank is in its own circle alone and lives.
    [Test]
    public void ATankThatHoldsNoTetherDiesInAHoldersCircleToo()
    {
        var stage = new Stage { Last = Protean1, Drainage = [new(TeaP1Tornado.West, CasterDps), new(TeaP1Tornado.East, MainTank)] };
        var run = stage.Run(OffTank, takeovers: new Takeover(23f, new Vector2(-2.24f, 2.76f)));

        var causes = run.Deaths.ToDictionary(d => d.Role, d => d.Cause);
        Assert.Multiple(() =>
        {
            Assert.That(causes.Keys, Is.EquivalentTo(PerRole.All.Except([MainTank])), run.ToString);
            Assert.That(causes[OffTank], Does.Contain(TeaP1LivingLiquid.InSomeoneElsesDrainage));
            Assert.That(causes[CasterDps], Does.Contain(TeaP1LivingLiquid.NonTankHoldsATether));
            Assert.That(causes[RegenHealer], Does.Contain(TeaP1LivingLiquid.InSomeoneElsesDrainage));
        });
    }

    // R2, in the knot north, starts with the west tornado's tether; the off tank steps into its line.
    [Test]
    public void ATankThatStepsIntoATetherTakesIt()
    {
        var stage = new Stage { Last = Protean1, Drainage = [new(TeaP1Tornado.West, CasterDps), new(TeaP1Tornado.East, MainTank)] };
        PartyRole? before = null, after = null;
        bool[] carried = [];
        var run = stage.Run(OffTank, takeovers: new Takeover(22f, new Vector2(-7.85f, -4.7f)), probe: p =>
        {
            if (p.Crossed(21.5f)) before = Holder(p, TeaP1Tornado.West);
            if (!p.Crossed(22.5f)) return;
            after = Holder(p, TeaP1Tornado.West);
            carried = PerRole.All.Select(role => p.Member(role)!.HasTetherInSlot0(P1TetherId.Drainage)).ToArray();
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That((before, after), Is.EqualTo(((PartyRole?)CasterDps, (PartyRole?)OffTank)));
        Assert.That(PerRole.All.Where(role => carried[(int)role]), Is.EquivalentTo(new[] { MainTank, OffTank }), "the last holder's channel is cleared");
    }

    // Nobody can hold both: a holder that walks through the other tether's line, ahead of its holder, leaves it there.
    [TestCase(MainTank, OffTank, -6.69f, 4.63f, TestName = "{m}(a tank through a tank's)")]
    [TestCase(CasterDps, CasterDps, 4.22f, 2.42f, TestName = "{m}(a non-tank through a tank's)")]
    [TestCase(MainTank, CasterDps, -4.5f, -7.07f, TestName = "{m}(a tank through a non-tank's)")]
    public void AHolderWalkingThroughTheOtherTetherKeepsItsOwnOnly(PartyRole walker, PartyRole west, float x, float z)
    {
        var stage = new Stage { Last = Protean1, Drainage = [new(TeaP1Tornado.West, west), new(TeaP1Tornado.East, MainTank)] };
        PartyRole? westHolder = null, eastHolder = null;
        var run = stage.Run(walker, stopAt: 24f, takeovers: new Takeover(22f, new Vector2(x, z)), probe: p =>
        {
            if (!p.Crossed(23.5f)) return;
            westHolder = Holder(p, TeaP1Tornado.West);
            eastHolder = Holder(p, TeaP1Tornado.East);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That((westHolder, eastHolder), Is.EqualTo(((PartyRole?)west, (PartyRole?)MainTank)));
    }

    // The main tank starts with the west tornado's tether and H2 with the east one's. The off tank, free, steps into
    // the main tank's and takes it; the main tank, free now, steps into H2's.
    [Test]
    public void AFreeTankTakesATetherFromTheOtherTankWhichCanThenTakeTheOtherOne()
    {
        var stage = new Stage { Last = Protean1, Drainage = [new(TeaP1Tornado.West, MainTank), new(TeaP1Tornado.East, ShieldHealer)] };
        var holders = new List<(PartyRole? West, PartyRole? East)>();
        var run = stage.Run(OffTank, stopAt: 24f,
            takeovers: [new Takeover(21f, new Vector2(-0.2f, 2.29f)), new Takeover(22f, new Vector2(2.63f, -6.55f), Bot: MainTank)],
            probe: p =>
            {
                foreach (var at in new[] { 20.5f, 21.5f, 22.5f })
                    if (p.Crossed(at)) holders.Add((Holder(p, TeaP1Tornado.West), Holder(p, TeaP1Tornado.East)));
            });

        Assert.That(run.Passed, run.ToString);
        Assert.That(holders, Is.EqualTo(new (PartyRole?, PartyRole?)[] { (MainTank, ShieldHealer), (OffTank, ShieldHealer), (OffTank, MainTank) }));
    }

    [Test]
    public void DrainageHitsBothHoldersAndTheFirstTornadoesLeave()
    {
        var watch = new Watch();
        TeaP1TornadoLayout? before = null, after = null;
        var tethers = -1;
        var run = ProteanOne.Run(MainTank, probe: p =>
        {
            watch.See(p);
            if (p.Crossed(24.5f)) before = Stage.Fight(p).Tornadoes.Standing;
            if (!p.Crossed(24.7f)) return;
            after = Stage.Fight(p).Tornadoes.Standing;
            tethers = Stage.Fight(p).DrainageTethersOut.Count;
        });

        Assert.That(run.Passed, run.ToString);
        var hits = watch.Sent("ability", P1ActionId.Drainage);
        Assert.Multiple(() =>
        {
            Assert.That(hits.Select(h => h.At), Is.EqualTo(new[] { 24.6f, 24.6f }).Within(0.05f));
            Assert.That(hits.Select(h => h.Hits.Single()), Is.EquivalentTo(new[] { MainTank, OffTank }));
            Assert.That(hits.Select(h => (MathF.Round(h.Position.X, 1), MathF.Round(h.Position.Y, 1))), Is.EquivalentTo(new[] { (-14.5f, 0f), (14.5f, 0f) }));
            Assert.That(before, Is.EqualTo(ProteanOne.Frame1));
            Assert.That(after, Is.Null);
            Assert.That(tethers, Is.Zero);
        });
    }

    // -- Throttle --

    [Test]
    public void ThrottleLandsOnItsSixAtTenPointFive()
    {
        PartyRole[] before = [], carriers = [], withTheStatus = [];
        var run = CascadeTwo.Run(MainTank, stopAt: 11f, probe: p =>
        {
            if (p.Crossed(10.4f)) before = Throttled(p);
            if (!p.Crossed(10.6f)) return;
            carriers = Throttled(p);
            withTheStatus = PerRole.All.Where(role => p.Member(role)!.HasStatus(P1StatusId.Throttle)).ToArray();
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(before, Is.Empty);
        Assert.That(carriers, Is.EquivalentTo(Stage.DefaultThrottled));
        Assert.That(withTheStatus, Is.EquivalentTo(Stage.DefaultThrottled));
    }

    [Test]
    public void AnUncleansedThrottleKillsAsItRunsOut()
    {
        var run = new Stage { First = Cascade2, NotCleansed = [MeleeDpsA] }.Run(MainTank);

        TeaP1LimitCutScenarioTests.AssertFirstDeath(run, MeleeDpsA, P1ActionId.LiquidGaol, 19.5f, TeaP1LivingLiquid.ThrottleRanOut);
    }

    [Test]
    public void ACleanseTakesThrottleOffAndASecondOneDoesNothingMore()
    {
        var states = new List<(bool Counted, bool Status)>();
        var run = new Stage { First = Cascade2, NotCleansed = [MeleeDpsA] }.Run(MainTank, stopAt: 21f, probe: p =>
        {
            var scenario = Stage.Scenario(p);
            var m1 = p.Member(MeleeDpsA)!;
            foreach (var at in new[] { 17.9f, 18.1f, 18.6f })
                if (p.Crossed(at)) states.Add((scenario.Fight!.IsThrottled(m1), m1.HasStatus(P1StatusId.Throttle)));
            if (p.Crossed(18f) || p.Crossed(18.5f)) scenario.Presses!.Cleanse(p.Member(RegenHealer)!, m1);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(states, Is.EqualTo(new[] { (true, true), (false, false), (false, false) }));
    }

    [Test]
    public void ACleanseATenthOfASecondBeforeItRunsOutSaves()
    {
        var run = new Stage { First = Cascade2, NotCleansed = [MeleeDpsA] }.Run(MainTank, stopAt: 21f, probe: p =>
        {
            if (p.Crossed(19.4f)) Stage.Scenario(p).Presses!.Cleanse(p.Member(ShieldHealer)!, p.Member(MeleeDpsA)!);
        });

        Assert.That(run.Passed, run.ToString);
    }

    // What a long frame does: the status leaves a frame before its end, and nothing sees it go.
    [Test]
    public void AThrottleWhoseStatusLeftEarlyStillKills()
    {
        var run = new Stage { First = Cascade2, NotCleansed = [MeleeDpsA] }.Run(MainTank, probe: p =>
        {
            if (p.Crossed(19.4f)) p.Member(MeleeDpsA)!.RemoveStatus(P1StatusId.Throttle);
        });

        TeaP1LimitCutScenarioTests.AssertFirstDeath(run, MeleeDpsA, P1ActionId.LiquidGaol, 19.5f, TeaP1LivingLiquid.ThrottleRanOut);
    }

    [Test]
    public void ACleansePressedBeforeThrottleLandsCleansesNothing()
    {
        var run = new Stage { First = Cascade2, NotCleansed = [MeleeDpsA] }.Run(MainTank, probe: p =>
        {
            if (p.Crossed(10.3f)) Stage.Scenario(p).Presses!.Cleanse(p.Member(RegenHealer)!, p.Member(MeleeDpsA)!);
        });

        TeaP1LimitCutScenarioTests.AssertFirstDeath(run, MeleeDpsA, P1ActionId.LiquidGaol, 19.5f, TeaP1LivingLiquid.ThrottleRanOut);
    }

    // -- The Liquid Hand's form and slam --

    [Test]
    public void BossesFarApartAtTheGlowShowAPalmThatSlamsHarmlesslyOnceTheyAreTogether()
    {
        var watch = new Watch();
        var forms = new List<bool?>();
        var apart = new List<float>();
        var run = CascadeTwo.Run(MainTank, probe: p =>
        {
            watch.See(p);
            var fight = Stage.Fight(p);
            foreach (var at in new[] { 33.1f, 33.3f, 38.3f, 38.5f })
                if (p.Crossed(at))
                {
                    forms.Add(fight.HandShowsAPalm);
                    apart.Add(Vector3.Distance(fight.Position(TeaP1Boss.LivingLiquid), fight.Position(TeaP1Boss.LiquidHand)));
                }
        });

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(forms, Is.EqualTo(new bool?[] { null, true, true, null }));
            Assert.That(apart[1], Is.GreaterThan(P1Rules.HandShowsAPalmBeyond), "at the glow");
            Assert.That(apart[2], Is.LessThan(P1Rules.PalmNeedsThemWithin), "at the slam");
            Assert.That(watch.ModelStates(P1BNpcBaseId.LiquidHand), Is.EqualTo(new[] { (33.2f, P1ModelState.HandPalm), (38.4f, (byte)0) }).Within(0.05f));
            Assert.That(watch.Sent("cast", P1ActionId.HandOfPrayer, P1ActionId.HandOfParting), Is.Empty, "the slam has no cast bar");
            Assert.That(watch.Sent("ability", P1ActionId.HandOfPrayer).Select(s => (s.At, s.Hits.Count)), Is.EqualTo(new[] { (38.4f, 8) }).Within(0.05f));
            Assert.That(watch.Sent("ability", P1ActionId.HandOfParting), Is.Empty);
        });
    }

    // The off tank never brings the Hand in.
    [Test]
    public void APalmThatFindsTheBossesApartKillsEveryone()
    {
        var run = new Stage { First = Cascade2, HumanUntil = 33f }.Run(OffTank);

        AssertEveryoneDied(run, P1ActionId.HandOfPrayer, 38.4f, TeaP1LivingLiquid.PalmFoundThemApart);
    }

    // It is the slam that reads the distance, not its lead-up: the Hand, brought in late, arrives half a second
    // before it.
    [Test]
    public void APalmOnlyNeedsThemTogetherAsItLands()
    {
        var run = new Stage { First = Cascade2, HumanUntil = 33f }.Run(OffTank, takeovers: new Takeover(35f, new Vector2(1.5f, -1.5f)));

        Assert.That(run.Passed, run.ToString);
    }

    // The off tank brings the Hand beside the Living Liquid before the glow. A fist that finds them together kills
    // everyone; taken far off again in time, it kills nobody.
    [TestCase(false)]
    [TestCase(true)]
    public void BossesCloseAtTheGlowShowAFistThatNeedsThemApart(bool apartAgain)
    {
        var watch = new Watch();
        Takeover[] takeovers = apartAgain
            ? [new(30f, new Vector2(3f, -4f)), new(34f, new Vector2(-13f, 10.8f))]
            : [new(30f, new Vector2(3f, -4f))];
        var run = CascadeTwo.Run(OffTank, probe: watch.See, takeovers: takeovers);

        Assert.That(watch.ModelStates(P1BNpcBaseId.LiquidHand).First(), Is.EqualTo((33.2f, P1ModelState.HandFist)).Within(0.05f));
        Assert.That(watch.Sent("ability", P1ActionId.HandOfParting).Select(s => s.At), Is.EqualTo(new[] { 38.4f }).Within(0.05f));
        if (apartAgain) Assert.That(run.Passed, run.ToString);
        else AssertEveryoneDied(run, P1ActionId.HandOfParting, 38.4f, TeaP1LivingLiquid.FistFoundThemTogether);
    }

    // -- Emboli --

    // Pressurize at 33.3 from the west and east tornadoes; at 33.9 an orb leaves each for where its boss stood at 33.3.
    [TestCase(TeaP1Tornado.West, TeaP1Boss.LiquidHand, TeaP1Tornado.East, TeaP1Boss.LivingLiquid)]
    [TestCase(TeaP1Tornado.West, TeaP1Boss.LiquidHand, TeaP1Tornado.Middle, TeaP1Boss.LivingLiquid)]
    [TestCase(TeaP1Tornado.Middle, TeaP1Boss.LivingLiquid, TeaP1Tornado.East, TeaP1Boss.LiquidHand)]
    public void EachEmbolusLeavesItsTornadoForWhereItsBossStoodAtPressurize(TeaP1Tornado first, TeaP1Boss firstAt, TeaP1Tornado second, TeaP1Boss secondAt)
    {
        var stage = new Stage { First = Cascade2, Emboli = [new(first, firstAt), new(second, secondAt)], EmbolusSpeed = 6f };
        var watch = new Watch();
        var bosses = new Dictionary<TeaP1Boss, Vector3>();
        Vector3[] left = [], reached = [];
        int before = -1, after = -1;
        var run = stage.Run(MeleeDpsA, probe: p =>
        {
            watch.See(p);
            var fight = Stage.Fight(p);
            if (p.Crossed(33.3f))
                foreach (var boss in Enum.GetValues<TeaP1Boss>()) bosses[boss] = fight.Position(boss);
            if (p.Crossed(33.8f)) before = fight.Emboli.Count;
            if (p.Crossed(33.95f)) left = fight.Emboli.Select(e => e.Position).ToArray();
            if (p.Crossed(39.5f)) reached = fight.Emboli.Select(e => e.Position).ToArray();
            if (p.Crossed(42f)) after = fight.Emboli.Count(e => e.IsActive);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(before, Is.Zero);
            Assert.That(Vector3.Distance(left[0], stage.Frame2.Centre(first)), Is.LessThan(0.5f), $"the first left from {left[0]}");
            Assert.That(Vector3.Distance(left[1], stage.Frame2.Centre(second)), Is.LessThan(0.5f), $"the second left from {left[1]}");
            Assert.That(Vector3.Distance(reached[0], bosses[firstAt]), Is.LessThan(0.2f), $"the first reached {reached[0]}");
            Assert.That(Vector3.Distance(reached[1], bosses[secondAt]), Is.LessThan(0.2f), $"the second reached {reached[1]}");
            Assert.That(after, Is.Zero, "they go as the Liquid falls");
            var pressurizes = watch.Sent("ability", P1ActionId.Pressurize);
            Assert.That(pressurizes.Select(s => s.At), Is.EqualTo(new[] { 33.3f, 33.3f }).Within(0.05f));
            Assert.That(pressurizes.Select(s => s.Position), Is.EquivalentTo(new[] { Flat(stage.Frame2.Centre(first)), Flat(stage.Frame2.Centre(second)) }).Using<Vector2>((a, b) => Vector2.Distance(a, b) < 0.1f));
        });
    }

    // M2 stands on the east orb's way, outside its tornado's puddle.
    [Test]
    public void TouchingAnEmbolusKillsEveryone()
    {
        var run = new Stage { First = Cascade2, EmbolusSpeed = 3f }.Run(MeleeDpsB, takeovers: new Takeover(35.5f, new Vector2(7.9f, -4.6f)));

        AssertEveryoneDied(run, P1ActionId.Outburst, 36.2f, TeaP1LivingLiquid.EmbolusTouched, within: 0.3f);
    }

    // The main tank never takes the Living Liquid off the spot it stood on at Pressurize.
    [Test]
    public void ABossAnEmbolusReachesKillsEveryone()
    {
        var run = new Stage { First = Cascade2, EmbolusSpeed = 4f, HumanUntil = 33f }.Run(MainTank);

        AssertEveryoneDied(run, P1ActionId.Outburst, 36.4f, TeaP1LivingLiquid.EmbolusTouched, within: 0.3f);
    }

    [Test]
    public void GodmodeHearsOfATouchedEmbolusOncePerPlayer()
    {
        IReadOnlyList<string> chat = [];
        var run = new Stage { First = Cascade2, EmbolusSpeed = 3f, GodMode = true }.Run(MeleeDpsB,
            probe: p => chat = p.Chat.ToList(), takeovers: new Takeover(35.5f, new Vector2(7.9f, -4.6f)));

        Assert.That(run.Deaths, Is.Empty, run.ToString);
        Assert.That(chat.Where(line => line.Contains(TeaP1LivingLiquid.EmbolusTouched)).ToList(), Has.Count.EqualTo(8), () => string.Join(Environment.NewLine, chat));
    }

    // -- The raidwides, and the end --

    [Test]
    public void TheRaidwidesGoOutOnTheFightsClockAndKillNobody()
    {
        var watch = new Watch();
        var run = new Stage().Run(MainTank, probe: watch.See);

        Assert.That(run.Passed, run.ToString);
        const float c2 = Stage.CascadeTwo;
        Assert.Multiple(() =>
        {
            Assert.That(watch.Sent("cast", P1ActionId.HandOfPain).Select(s => s.At), Is.EqualTo(new[] { 5.0f, 24.1f, c2 + 20.2f }).Within(0.05f));
            Assert.That(watch.Sent("ability", P1ActionId.HandOfPain).Select(s => (s.At, s.Hits.Count)),
                        Is.EqualTo(new[] { (8.0f, 8), (27.1f, 8), (c2 + 23.2f, 8) }).Within(0.05f));
            Assert.That(watch.Sent("ability", P1ActionId.Splash).Select(s => (s.At, s.Hits.Count)),
                        Is.EqualTo(new[] { (19.1f, 8), (20.2f, 8), (21.3f, 8), (22.4f, 8), (23.5f, 8), (24.6f, 8) }).Within(0.05f));
            Assert.That(watch.Sent("cast", P1ActionId.Cascade).Select(s => s.At), Is.EqualTo(new[] { c2 + 1.9f }).Within(0.05f));
            Assert.That(watch.Sent("ability", P1ActionId.Cascade).Select(s => (s.At, s.Hits.Count)), Is.EqualTo(new[] { (c2 + 5.9f, 8) }).Within(0.05f));
            Assert.That(watch.Sent("ability", P1ActionId.LiquidGaol).Select(s => s.At), Is.EqualTo(Enumerable.Repeat(c2 + 10.1f, 3)).Within(0.05f));
            Assert.That(watch.Sent("cast", P1ActionId.ProteanWaveFromTheLiquid).Select(s => s.At), Is.EqualTo(new[] { 4.8f, c2 + 18.2f }).Within(0.05f));
            Assert.That(watch.Sent("cast", P1ActionId.Sluice).Select(s => s.At), Is.EqualTo(new[] { 9.9f, 9.9f, 9.9f, 9.9f, c2 + 23.3f, c2 + 23.3f, c2 + 23.3f, c2 + 23.3f }).Within(0.05f));
            Assert.That(watch.Sent("ability", P1ActionId.FluidSwing, P1ActionId.FluidStrike), Is.Empty, "no cleave falls in these two parts");
        });
    }

    // M2 walks into the west puddle once the Liquid is down.
    [Test]
    public void TheLiquidFallsOnTheTimelineAndNothingKillsAfterIt()
    {
        var states = new List<(bool LiquidUp, bool HandUp)>();
        var positions = new List<Vector3>();
        var tornadoes = new List<bool>();
        var run = CascadeTwo.Run(MeleeDpsB, takeovers: new Takeover(40.2f, new Vector2(-12f, 0f)), probe: p =>
        {
            var fight = Stage.Fight(p);
            foreach (var at in new[] { 39.6f, 39.8f, 40.8f, 42.5f })
                if (p.Crossed(at))
                {
                    states.Add((fight.Liquid.IsAlive(), fight.Hand is { IsActive: true }));
                    positions.Add(fight.Position(TeaP1Boss.LiquidHand));
                    tornadoes.Add(fight.Tornadoes.Standing is not null);
                }
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(states, Is.EqualTo(new[] { (true, true), (false, true), (false, true), (false, false) }));
        Assert.That(tornadoes, Is.EqualTo(new[] { true, false, false, false }), "the tornadoes and their water go with the Liquid");
        Assert.That(positions[3], Is.EqualTo(positions[2]), "the Hand's last spot is still answered once it has left");
    }

    // -- Starts and parts --

    [Test]
    public void EitherStartPutsTheBossesInTheMiddleOnTheTanks([Values] TeaP1LivingLiquidPart first, [Values] TeaP1OpenQuarter open)
    {
        var stage = new Stage { First = first, Open1 = open };
        Vector3 liquid = default, hand = default;
        float facing = 0f;
        (PartyRole?, PartyRole?) targets = default, targetsBefore = default;
        TeaP1TornadoLayout? standing = null;
        var run = stage.Run(MainTank, stopAt: 1.2f, probe: p =>
        {
            var fight = Stage.Fight(p);
            if (p.Crossed(0.3f))
            {
                liquid = fight.Position(TeaP1Boss.LivingLiquid);
                hand = fight.Position(TeaP1Boss.LiquidHand);
                facing = fight.Liquid!.Rotation;
                standing = fight.Tornadoes.Standing;
                targetsBefore = (Seat(fight.Target(TeaP1Boss.LivingLiquid)), Seat(fight.Target(TeaP1Boss.LiquidHand)));
            }
            if (p.Crossed(1f)) targets = (Seat(fight.Target(TeaP1Boss.LivingLiquid)), Seat(fight.Target(TeaP1Boss.LiquidHand)));
        });

        Assert.That(run.Passed, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(liquid.Length(), Is.LessThan(0.01f), $"the Living Liquid at {liquid}");
            Assert.That(Turn(facing, stage.Frame1.Facing(135f)), Is.Zero.Within(0.01f), "it looks relative south-east");
            Assert.That(Vector3.Distance(hand, stage.Frame1.At(225f, 2.1f)), Is.LessThan(0.01f), $"the Liquid Hand at {hand}");
            Assert.That(targetsBefore, Is.EqualTo(((PartyRole?)null, (PartyRole?)null)));
            Assert.That(targets, Is.EqualTo(((PartyRole?)MainTank, (PartyRole?)OffTank)));
            Assert.That(standing, Is.EqualTo(first == Protean1 ? stage.Frame1 : null));
        });
    }

    [Test]
    public void ARunFromCascadeTwoHasNoTornadoUntilItLandsAndNoDrainage()
    {
        var watch = new Watch();
        var standing = new List<TeaP1TornadoLayout?>();
        var stage = new Stage { First = Cascade2, Open1 = TeaP1OpenQuarter.East, Open2 = TeaP1OpenQuarter.Southwest };
        var run = stage.Run(MainTank, stopAt: 8f, probe: p =>
        {
            watch.See(p);
            foreach (var at in new[] { 3f, 5.8f, 6f })
                if (p.Crossed(at)) standing.Add(Stage.Fight(p).Tornadoes.Standing);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(standing, Is.EqualTo(new[] { null, null, stage.Frame2 }));
        Assert.That(watch.Sent("cast", P1ActionId.Cascade).Select(s => s.At), Is.EqualTo(new[] { 1.9f }).Within(0.05f));
        Assert.That(watch.Spawned(P1BNpcBaseId.LiquidRage), Has.Count.EqualTo(3));
    }

    // The strat's first move reads where the Living Liquid stands, before any frame has played.
    [Test]
    public void TheBossesAndAProteanOneStartsTornadoesExistAsTheRunStarts()
    {
        foreach (var first in Enum.GetValues<TeaP1LivingLiquidPart>())
        {
            var fake = FakeGame.Install();
            DalamudServices.Install(nameof(Plugin.Log), TraceLog.Create(() => 0f));
            DalamudServices.Install(nameof(Plugin.Config), new Configuration());
            DalamudServices.Install(nameof(Plugin.ChatGui), ChatLog.Create());
            using var game = new Game();
            DalamudServices.Install(nameof(Plugin.Game), game);
            var scenario = game.Catalog.Scenarios.OfType<TeaP1LivingLiquidScenario>().Single();
            new Stage { First = first, Open1 = TeaP1OpenQuarter.West }.Pin((TeaP1LivingLiquidStateOverrides)scenario.SettingsOverrides);
            game.ZoneIn(scenario.Phase.Zone, SessionMode.Local);
            game.StartScenario(new RunScenarioParams(scenario, new LocalPartyProvider { RoleOverride = MainTank }, Stage.NoStrat, 0, 1));
            while (scenario.Fight is null) fake.Frame(ScenarioRun.FrameSeconds, game.Tick);

            var fight = scenario.Fight!;
            Assert.Multiple(() =>
            {
                Assert.That(game.World.Events.Elapsed, Is.LessThan(0.05f), $"{first}: read as the run starts");
                Assert.That(fight.Liquid, Is.Not.Null, $"{first}");
                Assert.That(fight.Hand, Is.Not.Null, $"{first}");
                Assert.That(fight.Position(TeaP1Boss.LivingLiquid), Is.EqualTo(Vector3.Zero), $"{first}");
                Assert.That(fight.Position(TeaP1Boss.LiquidHand).Length(), Is.EqualTo(2.1f).Within(0.01f), $"{first}");
                Assert.That(fight.Tornadoes.Standing, first == Protean1 ? Is.Not.Null : Is.Null, $"{first}");
                Assert.That(fight.Tornadoes[TeaP1Tornado.Middle], first == Protean1 ? Is.Not.Null : Is.Null, $"{first}");
            });
        }
    }

    [Test]
    public void ABossThatWasNeverSpawnedIsAnsweredForFromTheMiddle()
    {
        Vector3? liquid = null, hand = null;
        var run = TimelineRun.Execute(world =>
        {
            var presses = new TeaP1Presses(world);
            var fight = new TeaP1LivingLiquid(world, presses);
            liquid = fight.Position(TeaP1Boss.LivingLiquid);
            hand = fight.Position(TeaP1Boss.LiquidHand);
            fight.Tick();
            fight.TakeTargets();
            presses.Dispose();
        }, new TimelineRunOptions { Phase = TeaZone.P1 });

        Assert.That(run.Passed, run.ToString);
        Assert.That((liquid, hand), Is.EqualTo(((Vector3?)Vector3.Zero, (Vector3?)Vector3.Zero)));
    }

    [Test]
    public void TheTrackStartsWhereTheFightsClockStandsAsThePartStarts()
    {
        var scenario = new TeaP1LivingLiquidScenario();
        var overrides = (TeaP1LivingLiquidStateOverrides)scenario.SettingsOverrides;
        IScenario asScenario = scenario;

        var whole = asScenario.BgmSecondsAtStart;
        overrides.StartFrom = Cascade2;
        var later = asScenario.BgmSecondsAtStart;

        Assert.That((whole, later), Is.EqualTo((62f, 85.8f)));
    }

    // -- The chat line, the settings, the catalog --

    [TestCase(MainTank, Protean1, "Your Drainage tether: from the tornado right of the open quarter", "Your Living Liquid: it walks after you whenever you are more than 4 y from it, so fetching your tether drags it off the middle. After Drainage walk it back onto the middle")]
    [TestCase(OffTank, Protean1, "Your Drainage tether: from the tornado left of the open quarter")]
    [TestCase(MainTank, Cascade2, "Your Living Liquid: it walks after you whenever you are more than 4 y from it. Keep it on the middle, and turn it to the open quarter before Protean 2")]
    [TestCase(OffTank, Cascade2)]
    [TestCase(RegenHealer, Protean1, "Your Throttle cleanses: yourself first, then MT, OT, H2, M1, M2, R1, R2")]
    [TestCase(ShieldHealer, Cascade2, "Your Throttle cleanses: yourself first, then R2, R1, M2, M1, H1, OT, MT")]
    [TestCase(MeleeDpsA, Protean1)]
    [TestCase(CasterDps, Cascade2)]
    public void TheStartNamesTheHumansDutiesInThePartsItPlays(PartyRole seat, TeaP1LivingLiquidPart first, params string[] lines)
    {
        IReadOnlyList<string> chat = [];
        var run = new Stage { First = first }.Run(seat, stopAt: 1f, probe: p => chat = p.Chat.ToList());

        Assert.That(run.Passed, run.ToString);
        var duties = chat.Where(said => said.Contains("Your ")).ToList();
        Assert.That(duties, Has.Count.EqualTo(lines.Length), () => string.Join(Environment.NewLine, chat));
        for (var i = 0; i < lines.Length; i++) Assert.That(duties[i], Does.Contain(lines[i]));
    }

    [Test]
    public void SevenSeatsAskingForThrottleRefuseTheStart()
    {
        var run = ScenarioRun.Execute(typeof(TeaP1LivingLiquidScenario), Stage.NoStrat, 1, new ScenarioRunOptions
        {
            PlayerRole = MainTank,
            WriteArtifactsOnFailure = false,
            Overrides = o =>
            {
                var overrides = (TeaP1LivingLiquidStateOverrides)o;
                foreach (var role in PerRole.All.Take(7)) overrides.Throttle[role] = true;
            },
        });

        Assert.That(run.Failure, Is.EqualTo("did not start"));
    }

    [Test]
    public void ItIsAPartyOnlyScenarioInTheP1PhaseAheadOfLimitCut()
    {
        IScenario scenario = new TeaP1LivingLiquidScenario();
        var catalog = ScenarioCatalog.Create().Scenarios.Select(s => s.FullName()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(scenario.FullName(), Is.EqualTo("The Epic of Alexander — P1 Living Liquid"));
            Assert.That(scenario.Phase, Is.SameAs(TeaZone.P1));
            Assert.That(scenario.SupportsSolo, Is.False);
            Assert.That(scenario.SupportsMultiplayer, Is.False);
            Assert.That(scenario.AiStrats.Select(strat => (strat.Name, strat.Group)), Is.EqualTo(new[] { ("[LPDU] Light PF", "EU") }));
            Assert.That(scenario.SettingsConflicts, Is.Empty);
            Assert.That(catalog.IndexOf("The Epic of Alexander — P1 Living Liquid"), Is.EqualTo(catalog.IndexOf("The Epic of Alexander — P1 Limit Cut") - 1));
        });
    }

    // -- Support --

    internal static void AssertEveryoneDied(ScenarioRun run, uint actionId, float at, string because, float within = 0.1f)
    {
        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.Multiple(() =>
        {
            Assert.That(run.Deaths.Select(d => d.Role), Is.EquivalentTo(PerRole.All), run.ToString);
            Assert.That(run.Deaths.Select(d => d.ActionId), Is.All.EqualTo((uint?)actionId), run.ToString);
            Assert.That(run.Deaths.Select(d => d.Time), Is.All.EqualTo(at).Within(within), run.ToString);
            Assert.That(run.Deaths.Select(d => d.Cause), Is.All.Contain(because), run.ToString);
        });
    }

    internal static PartyRole? Seat(SimCharacter? member) => (member as ISimPartyMember)?.Role;

    internal static PartyRole? Holder(ScenarioProbe p, TeaP1Tornado from)
    {
        var fight = Stage.Fight(p);
        return Seat(fight.Holder(fight.DrainageTethersOut.Single(tether => tether.From == from).Tether));
    }

    internal static PartyRole[] Throttled(ScenarioProbe p)
        => Stage.Fight(p).Throttled.Select(member => ((ISimPartyMember)member).Role).ToArray();

    // The signed turn from `to` to `rotation`, in radians.
    internal static float Turn(float rotation, float to)
    {
        var turn = (rotation - to) % MathF.Tau;
        return turn > MathF.PI ? turn - MathF.Tau : turn < -MathF.PI ? turn + MathF.Tau : turn;
    }

    // The rotation of someone at `from` looking at `at`, in Placement's convention.
    internal static float Facing(Vector2 from, Vector2 at) => MathF.Atan2(at.X - from.X, at.Y - from.Y);

    internal static Vector2 Flat(Vector3 position) => new(position.X, position.Z);

    // One cast or ActionEffect the run sent: when, from which scenario-local spot facing where, and (an ActionEffect)
    // the seats it hit.
    internal sealed record Sent(float At, uint ActionId, Vector2 Position, float Rotation, IReadOnlyList<PartyRole> Hits);

    // Reads what a run sent to the fakes, once it has ended.
    internal sealed class Watch
    {
        private FakeGame? fake;
        private Dictionary<string, PartyRole> seats = [];

        public void See(ScenarioProbe p)
        {
            fake ??= p.Fake;
            if (seats.Count == 0) seats = PerRole.All.ToDictionary(role => p.Member(role)!.EntityId.ToString("X8"), role => role);
        }

        // "cast" rows are bars starting, "ability" rows ActionEffects; in time order.
        public IReadOnlyList<Sent> Sent(string kind, params uint[] actionIds)
        {
            var origin = TeaZone.Instance.Origin;
            return fake!.Timeline.Rows
                .Where(row => row["kind"] == kind && actionIds.Any(id => row["id"] == id.ToString()))
                .Select(row => new Sent(Float(row["t"]), uint.Parse(row["id"]),
                    new Vector2(Float(row["x"]) - origin.X, Float(row["z"]) - origin.Z), Float(row["rot"]),
                    row.GetValueOrDefault("targets", "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(id => seats[id]).ToList()))
                .ToList();
        }

        public IReadOnlyList<string> Spawned(uint bnpcBaseId)
            => fake!.Timeline.Rows.Where(row => row["kind"] == "spawn" && row["id"] == bnpcBaseId.ToString()).Select(row => row["actor"]).ToList();

        // Each model state the first actor of `bnpcBaseId` was sent, and when.
        public IReadOnlyList<(float At, byte State)> ModelStates(uint bnpcBaseId)
        {
            var actor = Spawned(bnpcBaseId).First();
            return fake!.Timeline.Rows
                .Where(row => row["kind"] == "ac" && row["actor"] == actor && row["id"] == "0x3F")
                .Select(row => (Float(row["t"]), row["p"].Length == 0 ? (byte)0 : Convert.ToByte(row["p"].Split(',')[0], 16)))
                .ToList();
        }

        private static float Float(string value) => float.Parse(value, CultureInfo.InvariantCulture);
    }
}
