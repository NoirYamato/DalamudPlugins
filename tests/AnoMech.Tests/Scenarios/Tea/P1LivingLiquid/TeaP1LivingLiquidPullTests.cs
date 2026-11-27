using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea;
using AnoMech.Scenarios.Tea.P1LivingLiquid;
using FFXIVClientStructs.FFXIV.Client.Game;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.P1LivingLiquid.TeaP1LivingLiquidPart;
using static AnoMech.Scenarios.Tea.TeaP1Constants;
using Stage = AnoMech.Tests.TeaP1LivingLiquidStage;
using Rules = AnoMech.Tests.TeaP1LivingLiquidScenarioTests;

namespace AnoMech.Tests;

// The pull's rules, played with no strat (TeaP1LivingLiquidStage, whose header has the timeline): everyone is put where
// the fight asks and the stage presses the tanks' Provokes, the main tank's invulnerability and each damage dealer's
// hit on its doll. A test moves one seat into a mistake, withholds one press, or plays a seat as a human that presses
// its own. Spots are relative to the first tornadoes (x east, z south, the open quarter north), which leave the north
// open unless a test says otherwise. A hit an action decides kills a second after it; one the fight decides in code
// (a puddle, the slam, an explosion, an Embolus) kills at once.
public class TeaP1LivingLiquidPullTests
{
    private const string Died = " died: ";
    private const uint HeavySwing = 31;

    private static readonly Stage ThePull = new() { First = Pull, Last = Pull };

    private static Stage Human(Stage stage) => new()
    {
        First = stage.First, Last = stage.Last, Open1 = stage.Open1, Open2 = stage.Open2, FirstEmboli = stage.FirstEmboli, EmbolusSpeed = stage.EmbolusSpeed,
        NoProvoke = stage.NoProvoke, NoInvulnerability = stage.NoInvulnerability, NoProvokeBack = stage.NoProvokeBack, NoHitBy = stage.NoHitBy,
        NoFeedBy = stage.NoFeedBy, GodMode = stage.GodMode, UserActionsEnabled = stage.UserActionsEnabled,
        HumanUntil = 999f, PlayerPressesItself = true,
    };

    private static void Press(uint actionId, ulong targetId = 0)
        => ((FakeUserActions)Natives.UserActions).Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, actionId, targetId));

    private static ulong Id(SimCharacter? character) => character is null ? 0ul : (ulong)character.GameObjectId;

    private static TeaP1JagdDoll Doll(ScenarioProbe p, Stage stage, PartyRole of) => Stage.Fight(p).Dolls.In(stage.Frame1, Stage.DollOf[of])!;

    // What was sent, in order: when, and the seats each hit.
    private static void AssertSent(IEnumerable<Rules.Sent> sent, params (float At, string Hits)[] expected)
    {
        var all = sent.ToList();
        Assert.That(all.Select(one => string.Join(" ", one.Hits)), Is.EqualTo(expected.Select(one => one.Hits)));
        Assert.That(all.Select(one => one.At), Is.EqualTo(expected.Select(one => one.At)).Within(0.05f));
    }

    // -- The whole pull --

    [Test]
    public void EveryoneWhereTheFightAsksSurvivesThePull([Values] TeaP1OpenQuarter open)
    {
        var fed = new List<TeaP1Boss?>();
        var run = new Stage { First = Pull, Last = Pull, Open1 = open }.Run(MainTank, probe: p =>
        {
            if (p.Crossed(55f)) fed = Stage.Fight(p).Dolls.All.Select(doll => doll.FedTo).ToList();
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(run.Elapsed, Is.InRange(56.8f, 59f), "the part ends by itself after the second cleaves");
        Assert.That(fed, Has.Count.EqualTo(4).And.None.Null, "every doll was fed");
    }

    [Test]
    public void ARunFromThePullPlaysOnToTheKill([Values(TeaP1OpenQuarter.North, TeaP1OpenQuarter.Southwest)] TeaP1OpenQuarter open1,
        [Values(TeaP1OpenQuarter.East, TeaP1OpenQuarter.Northwest)] TeaP1OpenQuarter open2)
    {
        var run = new Stage { First = Pull, Open1 = open1, Open2 = open2 }.Run(OffTank);

        Assert.That(run.Passed, run.ToString);
        Assert.That(run.Elapsed, Is.InRange(Stage.ProteanOne + 68f, Stage.ProteanOne + 70f), "the run ends by itself after the Liquid falls");
    }

    // -- The Living Liquid alone --

    [Test]
    public void TheLiquidWaitsNorthFacingSouthAndWalksToSixYalmsFromTheMainTank()
    {
        (Vector3 Position, float Rotation) waits = default;
        var others = -1;
        var listed = -1;
        float apart = 0f;
        var run = ThePull.Run(MainTank, stopAt: 3f, probe: p =>
        {
            var fight = Stage.Fight(p);
            if (p.Crossed(0.3f))
            {
                waits = (fight.Liquid!.Position, fight.Liquid.Rotation);
                others = (fight.Hand is null ? 0 : 1) + (fight.Tornadoes.Standing is null ? 0 : 1) + fight.Dolls.All.Count;
                listed = p.World.Children.OfType<SimEnemy>().Count(enemy => enemy.InEnemyList);
            }
            if (p.Crossed(1.95f)) apart = Vector3.Distance(fight.Liquid!.Position, p.Member(MainTank)!.Position);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(waits, Is.EqualTo((new Vector3(0f, 0f, -15f), 0f)));
        Assert.That(others, Is.Zero, "no Liquid Hand, no tornado and no doll before the split");
        Assert.That(listed, Is.EqualTo(1), "the enemy list holds the Living Liquid alone");
        Assert.That(apart, Is.EqualTo(Stage.LiquidStopsFromItsTank).Within(0.1f));
    }

    // The Liquid stands 4 y north of the middle, turned north to its tank.
    [Test]
    public void TheFirstFluidSwingKillsANonTankInIt()
    {
        var run = ThePull.Run(CasterDps, stopAt: 13f, takeovers: new Takeover(9f, new Vector2(1f, -9f)));

        TeaP1LimitCutScenarioTests.AssertFirstDeath(run, CasterDps, P1ActionId.FluidSwing, 11.1f, "Caught in Fluid Swing");
    }

    // -- The split --

    [Test]
    public void TheHandAppearsOnTheMiddleFacingSouthWithTheTornadoesOnTheOffTank([Values(TeaP1OpenQuarter.North, TeaP1OpenQuarter.Southeast)] TeaP1OpenQuarter open)
    {
        var stage = new Stage { First = Pull, Last = Pull, Open1 = open };
        var watch = new Rules.Watch();
        bool before = true;
        (Vector3 Position, float Rotation, PartyRole? Target, TeaP1TornadoLayout? Standing) after = default;
        var run = stage.Run(MainTank, stopAt: 20f, probe: p =>
        {
            watch.See(p);
            var fight = Stage.Fight(p);
            if (p.Crossed(19.55f)) before = fight.Hand is not null || fight.Tornadoes.Standing is not null;
            if (p.Crossed(19.7f)) after = (fight.Hand!.Position, fight.Hand.Rotation, Rules.Seat(fight.Target(TeaP1Boss.LiquidHand)), fight.Tornadoes.Standing);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(before, Is.False, "neither is there at 19.55");
        Assert.That(after, Is.EqualTo((Vector3.Zero, 0f, (PartyRole?)OffTank, stage.Frame1)));
        Assert.That(watch.Targets(P1BNpcBaseId.LiquidHand).Select(t => t.Target), Is.EqualTo(new[] { OffTank }));
    }

    // The client has the new actor only some frames after its spawn and drops what is written to it before. The off
    // tank stands 4 y from the middle: inside the Hand's reach, and outside what its reach reads as with no hitbox.
    [Test]
    public void AHandTheClientDoesNotHaveYetIsShownItsTargetOnceItIsThereAndDoesNotWalkMeanwhile()
    {
        var watch = new Rules.Watch();
        var positions = new List<Vector3>();
        var run = ThePull.Run(OffTank, stopAt: 21f, takeovers: new Takeover(19f, new Vector2(4f, 0f)), probe: p =>
        {
            watch.See(p);
            if (p.Crossed(1f)) p.Fake.BattleCharas.PacketSpawnFrames = (8, 8);
            foreach (var at in new[] { 19.65f, 19.9f, 20.9f })
                if (p.Crossed(at)) positions.Add(Stage.Fight(p).Position(TeaP1Boss.LiquidHand));
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(positions, Is.All.EqualTo(Vector3.Zero), "it never walked");
        var shown = watch.Targets(P1BNpcBaseId.LiquidHand);
        Assert.That(shown.Select(t => t.Target), Is.EqualTo(new[] { OffTank }));
        Assert.That(shown[0].At, Is.EqualTo(19.6f + 8 * ScenarioRun.FrameSeconds).Within(0.05f), "on the first frame the client had it");
    }

    // A tornado can land on any side, and its water reaches in to 7.4 y from the middle.
    [Test]
    public void TheNewTornadoesWaterKillsWhoeverIsStillInItASecondOn([Values(TeaP1OpenQuarter.South, TeaP1OpenQuarter.East, TeaP1OpenQuarter.West)] TeaP1OpenQuarter open)
    {
        var run = new Stage { First = Pull, Last = Pull, Open1 = open }.Run(MainTank, stopAt: 22f, takeovers: new Takeover(14.5f, new Vector2(0f, -8f)));

        TeaP1LimitCutScenarioTests.AssertFirstDeath(run, MainTank, SimCharacterDeathExtensions.Environment, 20.6f, TeaP1Tornadoes.StoodInThePuddle);
    }

    // -- The glow, the Provoke and the slam --

    [Test]
    public void TheHandShowsAPalmWithTheBossesSplitAndAFistWithTheOffTankHoldingItNearTheLiquid([Values] bool split)
    {
        bool? palm = null;
        float apart = 0f;
        Takeover[] takeovers = split ? [] : [new Takeover(24.1f, new Vector2(0.5f, 0f))];
        var run = ThePull.Run(OffTank, stopAt: 29.5f, takeovers: takeovers, probe: p =>
        {
            if (!p.Crossed(29.4f)) return;
            var fight = Stage.Fight(p);
            palm = fight.HandShowsAPalm;
            apart = Vector3.Distance(fight.Position(TeaP1Boss.LivingLiquid), fight.Position(TeaP1Boss.LiquidHand));
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(palm, Is.EqualTo(split), $"the bosses {apart:F1} y apart");
        Assert.That(apart, split ? Is.GreaterThan(18f) : Is.InRange(10f, 15f));
    }

    // The human main tank presses at 30.0; the Hand is its own 0.67 s later, whichever frame the press settled in.
    [Test]
    public void AProvokeGivesTheHandToWhoeverPressedItTwoThirdsOfASecondLater()
    {
        var targets = new List<PartyRole?>();
        Vector3 handAtThePress = default, handLater = default;
        IReadOnlyList<TeaP1Provoke> provokes = [];
        var run = Human(ThePull).Run(MainTank, stopAt: 34.6f, probe: p =>
        {
            var fight = Stage.Fight(p);
            if (p.Crossed(30f))
            {
                Press(P1PlayerActionId.Provoke, Id(fight.Hand));
                handAtThePress = fight.Position(TeaP1Boss.LiquidHand);
            }
            foreach (var at in new[] { 29.9f, 30.6f, 30.75f })
                if (p.Crossed(at)) targets.Add(Rules.Seat(fight.Target(TeaP1Boss.LiquidHand)));
            if (p.Crossed(31.5f)) handLater = fight.Position(TeaP1Boss.LiquidHand);
            provokes = Stage.Scenario(p).Presses!.Provokes;
        });

        Assert.That(run.Passed, $"the palm's slam finds them together{Environment.NewLine}{run}");
        Assert.That(targets, Is.EqualTo(new PartyRole?[] { OffTank, OffTank, MainTank }));
        Assert.That(Vector3.Distance(handAtThePress, handLater), Is.EqualTo((31.5f - 30.67f) * P1Rules.BossWalkSpeed).Within(0.5f), "it set off as the Provoke took hold");
        Assert.That(provokes.Select(provoke => (Rules.Seat(provoke.By), provoke.ByHuman)), Is.EqualTo(new[] { ((PartyRole?)MainTank, true) }));
    }

    [Test]
    public void AProvokeOnTheLivingLiquidGivesItToTheOffTank()
    {
        PartyRole? before = null, after = null;
        float apart = 0f;
        var run = Human(ThePull).Run(OffTank, stopAt: 8f, probe: p =>
        {
            var fight = Stage.Fight(p);
            if (p.Crossed(4f)) Press(P1PlayerActionId.Provoke, Id(fight.Liquid));
            if (p.Crossed(4.6f)) before = Rules.Seat(fight.Target(TeaP1Boss.LivingLiquid));
            if (p.Crossed(4.75f)) after = Rules.Seat(fight.Target(TeaP1Boss.LivingLiquid));
            if (p.Crossed(7.9f)) apart = Vector3.Distance(fight.Position(TeaP1Boss.LivingLiquid), p.Member(OffTank)!.Position);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That((before, after), Is.EqualTo(((PartyRole?)MainTank, (PartyRole?)OffTank)));
        Assert.That(apart, Is.LessThanOrEqualTo(7.05f), "it stands within its reach of its new target");
    }

    // Hand of Pain is cast from 38.6 to 41.6. The human off tank's Provoke at 38.9 takes hold inside it: the Hand is
    // its own at once, and turns and walks only once the cast has gone out.
    [Test]
    public void AProvokeThatTakesHoldInsideACastChangesTheTargetAtOnceAndMovesTheBossOnlyAfter()
    {
        PartyRole? during = null;
        Vector3 atThePress = default, duringTheCast = default, after = default;
        var run = Human(ThePull).Run(OffTank, stopAt: 45f, probe: p =>
        {
            var fight = Stage.Fight(p);
            if (p.Crossed(38.9f))
            {
                Press(P1PlayerActionId.Provoke, Id(fight.Hand));
                atThePress = fight.Position(TeaP1Boss.LiquidHand);
            }
            if (p.Crossed(39.7f)) during = Rules.Seat(fight.Target(TeaP1Boss.LiquidHand));
            if (p.Crossed(41.4f)) duringTheCast = fight.Position(TeaP1Boss.LiquidHand);
            if (p.Crossed(44.9f)) after = fight.Position(TeaP1Boss.LiquidHand);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(during, Is.EqualTo(OffTank));
        Assert.That(duringTheCast, Is.EqualTo(atThePress), "it holds still through Hand of Pain");
        Assert.That(Vector3.Distance(after, atThePress), Is.GreaterThan(3f), "and follows its new target once it has cast");
    }

    // The human main tank's one Provoke at `pressedAt`: too early and the Hand has walked close by the glow, so it shows
    // a fist and they come together; too late and the palm's slam finds the Hand still on its way. Nobody hits a doll
    // here: a Hand that stays beside one, or walks past one late, feeds none.
    [TestCase(null, true, TeaP1LivingLiquid.PalmFoundThemApart)]
    [TestCase(27.8f, false, TeaP1LivingLiquid.FistFoundThemTogether)]
    [TestCase(28.4f, true, null)]
    [TestCase(31.8f, true, null)]
    [TestCase(32.4f, true, TeaP1LivingLiquid.PalmFoundThemApart)]
    public void TheMainTanksProvokeHasToComeBetweenTheGlowAndTheSlam(float? pressedAt, bool palm, string? everyoneDiesOf)
    {
        foreach (var open in new[] { TeaP1OpenQuarter.North, TeaP1OpenQuarter.Northeast })
        {
            bool? shown = null;
            var fed = 0;
            var untouched = new Stage { First = Pull, Last = Pull, Open1 = open, NoHitBy = [MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps] };
            var run = Human(untouched).Run(MainTank, stopAt: 34.6f, probe: p =>
            {
                var fight = Stage.Fight(p);
                if (pressedAt is { } at && p.Crossed(at)) Press(P1PlayerActionId.Provoke, Id(fight.Hand));
                if (p.Crossed(29.4f)) shown = fight.HandShowsAPalm;
                fed = fight.Dolls.All.Count(doll => doll.FedTo is not null);
            });

            Assert.That(shown, Is.EqualTo(palm), $"open {open}");
            if (everyoneDiesOf is null) Assert.That(run.Passed, $"open {open}: {run}");
            else
            {
                Assert.That(run.Failure, Is.Null, run.ToString);
                Assert.That(run.Deaths.Select(d => d.Role), Is.EquivalentTo(PerRole.All), $"open {open}: {run}");
                Assert.That(run.Deaths.Select(d => d.Cause), Is.All.Contain(everyoneDiesOf), $"open {open}: {run}");
                Assert.That(run.Deaths.Select(d => d.Time), Is.All.EqualTo(34.4f).Within(0.05f), $"open {open}: {run}");
            }
            Assert.That(fed, Is.Zero, $"open {open}: no doll was fed");
        }
    }

    [TestCase("nothing")]
    [TestCase("a party member")]
    [TestCase("a doll")]
    public void AProvokeMovesABossOnlyWhenItIsAimedAtOne(string aimedAt)
    {
        (PartyRole? Liquid, PartyRole? Hand) targets = default;
        var provokes = -1;
        PartyRole? holder = null;
        var stage = Human(new Stage { First = Pull, Last = Pull, NoHitBy = [MeleeDpsA] });
        var run = stage.Run(OffTank, stopAt: 34.35f, probe: p =>
        {
            var fight = Stage.Fight(p);
            if (p.Crossed(33.6f))
                Press(P1PlayerActionId.Provoke, aimedAt switch
                {
                    "a party member" => Id(p.Member(MeleeDpsB)),
                    "a doll" => Id(Doll(p, stage, MeleeDpsA).Enemy),
                    _ => 0ul,
                });
            if (!p.Crossed(34.3f)) return;
            targets = (Rules.Seat(fight.Target(TeaP1Boss.LivingLiquid)), Rules.Seat(fight.Target(TeaP1Boss.LiquidHand)));
            provokes = Stage.Scenario(p).Presses!.Provokes.Count(provoke => provoke.ByHuman);
            holder = Rules.Seat(Doll(p, stage, MeleeDpsA).Holder);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(targets, Is.EqualTo(((PartyRole?)MainTank, (PartyRole?)MainTank)), "the stage's main tank has both");
        Assert.That(provokes, Is.EqualTo(aimedAt == "a doll" ? 1 : 0));
        Assert.That(holder, Is.EqualTo(aimedAt == "a doll" ? OffTank : (PartyRole?)null), "a Provoke on a doll is a hit on it");
    }

    // -- The first busters and the invulnerability --
    // Both cleaves land on the main tank, which holds both bosses, and leave Water Resistance Down; the unseen wave of
    // the tornado it stands beside then finds a carrier.

    [Test]
    public void BothCleavesLandOnTheMainTankAlone()
    {
        var watch = new Rules.Watch();
        var run = ThePull.Run(RegenHealer, stopAt: 37f, probe: watch.See);

        Assert.That(run.Passed, run.ToString);
        AssertSent(watch.Sent("ability", P1ActionId.FluidSwing), (10.1f, "MainTank"), (36.4f, "MainTank"));
        AssertSent(watch.Sent("ability", P1ActionId.FluidStrike), (36.5f, "MainTank"));
    }

    [TestCase(MainTank)]
    [TestCase(RegenHealer)]
    public void WithoutItsInvulnerabilityTheMainTankDiesOfTheTornadosUnseenWave(PartyRole player)
    {
        var run = new Stage { First = Pull, Last = Pull, NoInvulnerability = true }.Run(player, stopAt: 40f);

        TeaP1LimitCutScenarioTests.AssertFirstDeath(run, MainTank, P1ActionId.TornadoWaveUnseen, 38.5f, TeaP1Actions.SecondWave);
        Assert.That(run.Deaths, Has.Count.EqualTo(1), run.ToString);
    }

    // The human main tank's own press, heard with the sim resolving its actions and without. It holds its tornado's
    // telegraph with Arm's Length.
    [TestCase(34.0f, false, true)]
    [TestCase(34.0f, true, true)]
    [TestCase(27.0f, false, false)]
    [TestCase(37.6f, false, false)]
    [TestCase(37.6f, true, false)]
    public void TheMainTanksOwnInvulnerabilityCountsFromThePressForTenSeconds(float pressedAt, bool resolved, bool lives)
    {
        var stage = Human(new Stage { First = Pull, Last = Pull, UserActionsEnabled = resolved });
        var run = stage.Run(MainTank, stopAt: 40f, probe: p =>
        {
            var fight = Stage.Fight(p);
            if (p.Crossed(30f)) Press(P1PlayerActionId.Provoke, Id(fight.Hand));
            if (p.Crossed(33f)) Press(P1PlayerActionId.ArmsLength);
            if (!p.Crossed(pressedAt)) return;
            if (resolved) TopP6HumanMitigation.PressNow(p.World, [P1PlayerActionId.Holmgang]);
            else Press(P1PlayerActionId.Holmgang);
        });

        if (lives) Assert.That(run.Passed, run.ToString);
        else TeaP1LimitCutScenarioTests.AssertFirstDeath(run, MainTank, P1ActionId.TornadoWaveUnseen, 38.5f, TeaP1Actions.SecondWave);
    }

    // Pressed in the gap before the frame the wave goes out in: the client's input comes outside the run's tick.
    [Test]
    public void AnInvulnerabilityPressedJustBeforeTheWaveCountsWhetherOrNotTheSimResolvesThePlayersActions([Values] bool resolved,
        [Values(P1PlayerActionId.HallowedGround, P1PlayerActionId.Holmgang, P1PlayerActionId.LivingDead, P1PlayerActionId.Superbolide)] uint press)
    {
        var stage = Human(new Stage { First = Pull, Last = Pull, UserActionsEnabled = resolved });
        var run = stage.Run(MainTank, stopAt: 40f, probe: p =>
        {
            var fight = Stage.Fight(p);
            if (p.Crossed(30f)) Press(P1PlayerActionId.Provoke, Id(fight.Hand));
            if (p.Crossed(33f)) Press(P1PlayerActionId.ArmsLength);
            if (p.Time >= 37.5f || p.Time + ScenarioRun.FrameSeconds < 37.5f) return;
            if (resolved) TopP6HumanMitigation.PressNow(p.World, [press]);
            else Press(press);
        });

        Assert.That(run.Passed, run.ToString);
    }

    // A human on another job in the main tank's seat has no Provoke and no invulnerability: the stage presses for it,
    // as a run does.
    [Test]
    public void AMainTanksSeatOnAnotherJobIsPressedFor()
    {
        var run = new Stage { First = Pull, Last = Pull, HumanUntil = 999f }.Run(MainTank, stopAt: 40f, probe: p =>
        {
            if (p.Crossed(0.1f)) ((FakeBattleCharas)Natives.BattleCharas).Player.ClassJob = (byte)JobId.Bard;
            if (p.Crossed(33f)) Press(P1PlayerActionId.ArmsLength);
        });

        Assert.That(run.Passed, run.ToString);
    }

    // No rule kills for both cleaves alone. The main tank leaves its tornado once both have landed: the unseen wave
    // then goes at the melee beside that tornado, for whom it is a first wave.
    [Test]
    public void AMainTankThatTakesBothCleavesAndThenLeavesItsTornadoNeedsNoInvulnerability()
    {
        bool tankCarries = false, meleeCarries = false;
        var run = new Stage { First = Pull, Last = Pull, NoInvulnerability = true }.Run(MainTank, stopAt: 39.6f,
            takeovers: new Takeover(36.6f, new Vector2(-11f, 12f)), probe: p =>
            {
                if (!p.Crossed(38f)) return;
                tankCarries = p.Member(MainTank)!.HasStatus(P1StatusId.WaterResistanceDown);
                meleeCarries = p.Member(MeleeDpsB)!.HasStatus(P1StatusId.WaterResistanceDown);
            });

        Assert.That(run.Passed, run.ToString);
        Assert.That((tankCarries, meleeCarries), Is.EqualTo((true, true)), "the tank still carried the cleaves' debuff, and the wave went at the melee");
    }

    // -- A human tank under the cleaves --
    // A tank seat lives through each cleave bare, as the sim has it. With the sim resolving its actions and tank
    // mitigation required, a human main tank on a tank's job that keeps both bosses takes all five of the pull's with
    // no cooldown but the invulnerability the tornado's wave asks for.
    [Test]
    public void TheCleavesAskAHumanTankOnATanksJobForNoCooldown()
    {
        var watch = new Rules.Watch();
        var run = Human(new Stage { First = Pull, Last = Pull, UserActionsEnabled = true, NoProvokeBack = true }).Run(MainTank, probe: p =>
        {
            watch.See(p);
            var fight = Stage.Fight(p);
            if (p.Crossed(30f)) Press(P1PlayerActionId.Provoke, Id(fight.Hand));
            if (p.Crossed(33f)) Press(P1PlayerActionId.ArmsLength);
            if (p.Crossed(37f)) TopP6HumanMitigation.PressNow(p.World, [P1PlayerActionId.Holmgang]);
        });

        Assert.That(run.Passed, run.ToString);
        AssertSent(watch.Sent("ability", P1ActionId.FluidSwing), (10.1f, "MainTank"), (36.4f, "MainTank"), (55.55f, "MainTank"));
        AssertSent(watch.Sent("ability", P1ActionId.FluidStrike), (36.5f, "MainTank"), (55.8f, "MainTank"));
    }

    // -- The tornadoes' waves --

    [Test]
    public void EachTornadosTwoWavesGoAtItsClosestPlayer()
    {
        var watch = new Rules.Watch();
        var run = ThePull.Run(ShieldHealer, stopAt: 38f, probe: watch.See);

        Assert.That(run.Passed, run.ToString);
        Assert.That(watch.Sent("cast", P1ActionId.TornadoWave).Select(sent => sent.At), Is.All.EqualTo(32.4f).Within(0.05f));
        Assert.That(watch.Sent("ability", P1ActionId.TornadoWave).SelectMany(sent => sent.Hits), Is.EquivalentTo(new[] { MainTank }),
                    "the ranged stepped out of theirs; the main tank stays under its invulnerability");
        AssertSent(watch.Sent("ability", P1ActionId.TornadoWaveUnseen).OrderBy(sent => string.Join(" ", sent.Hits)),
                   (37.5f, "CasterDps"), (37.5f, "MainTank"), (37.5f, "PhysRangedDps"));
    }

    // H2 steps in to 8.0 y from the east tornado, closer than R1, and stays: both of its waves are H2's.
    [Test]
    public void ASeatThatStepsCloserToATornadoTakesBothOfItsWaves()
    {
        var run = ThePull.Run(MainTank, stopAt: 40f,
            takeovers: new Takeover(32f, Stage.Off(Stage.East, 8.0f, 200f), Bot: ShieldHealer));

        TeaP1LimitCutScenarioTests.AssertFirstDeath(run, ShieldHealer, P1ActionId.TornadoWaveUnseen, 38.5f, TeaP1Actions.SecondWave);
        Assert.That(run.Deaths, Has.Count.EqualTo(1), run.ToString);
    }

    // R1 is put back on its bait spot as the stage steps it out, and stays in its tornado's telegraph.
    [Test]
    public void ABotThatStaysInItsTornadosTelegraphDiesOfTheUnseenWave()
    {
        var run = ThePull.Run(MainTank, stopAt: 40f, takeovers: new Takeover(32.6f, Stage.FirstRangedsBait, Bot: PhysRangedDps));

        TeaP1LimitCutScenarioTests.AssertFirstDeath(run, PhysRangedDps, P1ActionId.TornadoWaveUnseen, 38.5f, TeaP1Actions.SecondWave);
    }

    // The telegraph throws a human 10 y straight away from the tornado, from 18.4 y out: past the rim.
    [TestCase(false)]
    [TestCase(true)]
    public void AHumanThatStaysInItsTornadosTelegraphIsThrownPastTheRimUnlessItHoldsItsGround(bool armsLength)
    {
        var stage = new Stage { First = Pull, Last = Pull, HumanUntil = 32.45f, PlayerPressesItself = true };
        var run = stage.Run(PhysRangedDps, stopAt: 40f, probe: p =>
        {
            if (armsLength && p.Crossed(33f)) Press(P1PlayerActionId.ArmsLength);
        });

        if (armsLength) TeaP1LimitCutScenarioTests.AssertFirstDeath(run, PhysRangedDps, P1ActionId.TornadoWaveUnseen, 38.5f, TeaP1Actions.SecondWave);
        else
        {
            Assert.That(run.Deaths, Is.Not.Empty, run.ToString);
            Assert.That(run.Deaths[0].Role, Is.EqualTo(PhysRangedDps), run.ToString);
            Assert.That(run.Deaths[0].Cause, Does.Contain(TeaZone.WentPastTheRim), run.ToString);
            Assert.That(run.Deaths[0].Time, Is.InRange(35.4f, 36.1f), run.ToString);
        }
    }

    // -- The Jagd Dolls --

    [Test]
    public void FourDollsAppearOnTheirSpotsOneInEachQuarterAndTurnTargetableLater([Values(TeaP1OpenQuarter.North, TeaP1OpenQuarter.Northeast, TeaP1OpenQuarter.West)] TeaP1OpenQuarter open)
    {
        var stage = new Stage { First = Pull, Last = Pull, Open1 = open };
        var count = new List<int>();
        List<(Vector3 Spot, Vector3 Stands, bool Targetable, NpcAppearance? Appearance, TeaP1Quarter Quarter)> dolls = [];
        var targetable = new List<bool>();
        var run = stage.Run(MainTank, stopAt: 33.6f, probe: p =>
        {
            var all = Stage.Fight(p).Dolls.All;
            if (p.Crossed(32.5f) || p.Crossed(32.7f)) count.Add(all.Count);
            if (p.Crossed(33.3f))
                dolls = all.Select(doll => (doll.Spot, doll.Enemy.Position, doll.Enemy.Targetable || doll.IsUp, doll.Enemy.SpawnConfig.Appearance, doll.QuarterIn(stage.Frame1))).ToList();
            if (p.Crossed(33.5f)) targetable = all.Select(doll => doll.Enemy.Targetable && doll.IsUp).ToList();
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(count, Is.EqualTo(new[] { 0, 4 }));
        Assert.Multiple(() =>
        {
            Assert.That(dolls.Select(doll => doll.Spot), Is.EqualTo(new[] { new Vector3(6.47f, 0f, -2.68f), new Vector3(2.68f, 0f, 6.47f), new Vector3(-6.47f, 0f, 2.68f), new Vector3(-2.68f, 0f, -6.47f) })
                .Using<Vector3>((a, b) => Vector3.Distance(a, b) < 0.01f), "7 y out on 67.5, 157.5, 247.5 and 337.5 degrees, whatever the layout");
            Assert.That(dolls.Select(doll => doll.Stands), Is.EqualTo(dolls.Select(doll => doll.Spot)).Using<Vector3>((a, b) => Vector3.Distance(a, b) < 0.01f));
            Assert.That(dolls.Select(doll => doll.Targetable), Is.All.False, "at 33.3");
            Assert.That(dolls.Select(doll => doll.Appearance), Is.All.SameAs(P1Appearance.JagdDoll));
            Assert.That(dolls.Select(doll => doll.Quarter), Is.EquivalentTo(Enum.GetValues<TeaP1Quarter>()), "each quarter of the tornadoes' frame holds one");
            Assert.That(targetable, Has.Count.EqualTo(4).And.All.True, "at 33.5");
        });
    }

    // The stage's damage dealers hit at 34.3. R1 stands beside its tornado, 11 y from its doll's spot.
    [Test]
    public void ADollTethersWhoeverHitItFirstAndWalksAfterThem()
    {
        var stage = ThePull;
        var tethered = new List<bool>();
        PartyRole?[] holders = [];
        float standsOff = 0f;
        Vector3 meleesDoll = default, itsSpot = default;
        var midWalk = new List<Vector3>();
        var run = stage.Run(MainTank, stopAt: 38.2f, probe: p =>
        {
            if (p.Time < 34f) return;
            var doll = Doll(p, stage, PhysRangedDps);
            if (p.Crossed(34.8f) || p.Crossed(35f)) tethered.Add(doll.TetheredAt is not null);
            if (p.Crossed(34.6f)) Stage.Scenario(p).Presses!.Strike(p.Member(RegenHealer)!, doll.Enemy);
            if (p.Crossed(35.1f) || p.Crossed(35.5f)) midWalk.Add(doll.Enemy.Position);
            if (!p.Crossed(38f)) return;
            holders = Stage.DollOf.Keys.Select(seat => Rules.Seat(Doll(p, stage, seat).Holder) == seat ? seat : (PartyRole?)null).ToArray();
            standsOff = Vector3.Distance(doll.Enemy.Position, p.Member(PhysRangedDps)!.Position);
            (meleesDoll, itsSpot) = (Doll(p, stage, MeleeDpsA).Enemy.Position, Doll(p, stage, MeleeDpsA).Spot);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(tethered, Is.EqualTo(new[] { false, true }), "0.6 s after the hit at 34.3");
        Assert.That(holders, Is.EqualTo(Stage.DollOf.Keys.Cast<PartyRole?>()), "each kept its first hitter: H1's later hit on R1's changed nothing");
        Assert.That(Vector3.Distance(midWalk[0], midWalk[1]), Is.EqualTo(0.4f * 9f).Within(0.3f), "it walks at 9 y/s");
        Assert.That(standsOff, Is.EqualTo(3.3f).Within(0.1f), "it stops 2 y past their hitboxes");
        Assert.That(Vector3.Distance(meleesDoll, itsSpot), Is.LessThan(0.01f), "a doll whose holder stands beside it stays where it appeared");
    }

    // M1 hits its own doll at 34.3 and M2's at 34.4. The second walks to 3.3 y from it, so both reach it as they first
    // Exhaust: it explodes.
    [Test]
    public void OneSeatThatHitsTwoDollsHoldsBothAndExplodesAtTheFirstExhaustAndAHitBeforeTheyTurnTargetableCountsForNothing()
    {
        var stage = new Stage { First = Pull, Last = Pull, NoHitBy = [MeleeDpsB] };
        bool early = true;
        PartyRole?[] holders = [];
        var run = stage.Run(MainTank, stopAt: 38.6f, probe: p =>
        {
            if (p.Time < 32.7f) return;
            var presses = Stage.Scenario(p).Presses!;
            if (p.Crossed(33.2f)) presses.Strike(p.Member(RegenHealer)!, Doll(p, stage, MeleeDpsA).Enemy);
            if (p.Crossed(33.35f)) early = Doll(p, stage, MeleeDpsA).HasBeenHit;
            if (p.Crossed(34.4f)) presses.Strike(p.Member(MeleeDpsA)!, Doll(p, stage, MeleeDpsB).Enemy);
            if (p.Crossed(35.1f)) holders = [Rules.Seat(Doll(p, stage, MeleeDpsA).Holder), Rules.Seat(Doll(p, stage, MeleeDpsB).Holder)];
        });

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(early, Is.False);
        Assert.That(holders, Is.EqualTo(new PartyRole?[] { MeleeDpsA, MeleeDpsA }));
        Assert.That(run.Deaths.Select(d => d.Role), Does.Contain(MeleeDpsA), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Cause), Is.All.Contain(TeaP1JagdDolls.LuminousAetheroplasmBurst), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Time), Is.All.EqualTo(38.5f).Within(0.05f), run.ToString);
    }

    // The client's input comes outside the run's tick: a press heard before the run's own hit of the same frame
    // settles first, and the doll is the human's.
    [Test]
    public void AHumansHitHeardJustBeforeTheRunsOwnOfTheSameFrameTakesTheDoll()
    {
        var stage = Human(new Stage { First = Pull, Last = Pull, NoHitBy = [MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps] });
        PartyRole? holder = null;
        var run = stage.Run(CasterDps, stopAt: 35f, probe: p =>
        {
            if (p.Time < 32.7f) return;
            var doll = Doll(p, stage, MeleeDpsA);
            if (p.Crossed(34.3f))
            {
                Press(HeavySwing, Id(doll.Enemy));
                Stage.Scenario(p).Presses!.Strike(p.Member(MeleeDpsA)!, doll.Enemy);
            }
            if (p.Crossed(34.9f)) holder = Rules.Seat(doll.Holder);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(holder, Is.EqualTo(CasterDps));
    }

    // The human off tank Provokes the Hand in the gap before the frame the run's main tank does: both are heard, the
    // human's first.
    [Test]
    public void AHumansProvokeHeardJustBeforeTheRunsOwnOfTheSameFrameSettlesFirst()
    {
        IReadOnlyList<TeaP1Provoke> provokes = [];
        var run = Human(new Stage { First = Pull, Last = Pull, NoProvoke = true }).Run(OffTank, stopAt: 30.3f, probe: p =>
        {
            var scenario = Stage.Scenario(p);
            if (p.Crossed(30f))
            {
                Press(P1PlayerActionId.Provoke, Id(scenario.Fight!.Hand));
                scenario.Presses!.Provoke(p.Member(MainTank)!, scenario.Fight.Hand!);
            }
            provokes = scenario.Presses!.Provokes;
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(provokes.Select(provoke => (Rules.Seat(provoke.By), provoke.ByHuman)), Is.EqualTo(new[] { ((PartyRole?)OffTank, true), ((PartyRole?)MainTank, false) }));
    }

    // Provoke reaches 25 y past the two hitboxes. The run's own, for a seat nobody presses for, is held to that as the
    // client holds a human's: the main tank's at 30.0 goes out with the Hand 25.9 y away, and not with the off tank
    // and the Hand taken to the far rim.
    [TestCase(false, true)]
    [TestCase(true, false)]
    public void TheRunsOwnProvokeIsHeldToItsReach(bool theHandIsTakenToTheFarRim, bool provoked)
    {
        var stage = new Stage { First = Pull, Last = Pull, GodMode = true };
        var apart = float.NaN;
        var provokes = -1;
        Takeover[] takeovers = theHandIsTakenToTheFarRim ? [new Takeover(24.1f, stage.Arena1(6f, -18f))] : [];
        var run = stage.Run(OffTank, stopAt: 30.3f, takeovers: takeovers, probe: p =>
        {
            var fight = Stage.Fight(p);
            if (p.Crossed(29.95f)) apart = TeaP1LivingLiquid.Apart(p.Member(MainTank)!.Position, fight.Position(TeaP1Boss.LiquidHand));
            provokes = Stage.Scenario(p).Presses!.Provokes.Count;
        });

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(apart, provoked ? Is.InRange(25f, 27.1f) : Is.GreaterThan(27.1f));
        Assert.That(provokes, Is.EqualTo(provoked ? 1 : 0));
    }

    // The human M1 presses one button with its doll targeted at 34.3. Only an action that is aimed at one target other
    // than its user is a hit, be it a weaponskill, a spell or an ability such as Provoke: a press comes with the
    // client's target whatever the button. A button that turns into an attack on the target comes as the attack it
    // became; the cases of its own id, whose row is aimed at its user, hold BecomeAHitOnTheTarget, which no press
    // reaches and P2 drops.
    [TestCase(31u, true, TestName = "{m}(Heavy Swing)")]
    [TestCase(16u, true, TestName = "{m}(Shield Bash)")]
    [TestCase(7533u, true, TestName = "{m}(Provoke)")]
    [TestCase(119u, true, TestName = "{m}(Stone)")]
    [TestCase(25762u, true, TestName = "{m}(Thunderclap)")]
    [TestCase(2260u, true, TestName = "{m}(Ninjutsu)")]
    [TestCase(7867u, true, TestName = "{m}(Iaijutsu)")]
    [TestCase(16483u, true, TestName = "{m}(Tsubame-gaeshi)")]
    [TestCase(25764u, true, TestName = "{m}(Masterful Blitz)")]
    [TestCase(16155u, true, TestName = "{m}(Continuation)")]
    [TestCase(35920u, true, TestName = "{m}(Serpent's Tail)")]
    [TestCase(35921u, true, TestName = "{m}(Twinfang)")]
    [TestCase(35922u, true, TestName = "{m}(Twinblood)")]
    [TestCase(25822u, true, TestName = "{m}(Astral Flow)")]
    [TestCase(35347u, true, TestName = "{m}(Living Muse)")]
    [TestCase(7531u, false, TestName = "{m}(Rampart)")]
    [TestCase(7548u, false, TestName = "{m}(Arm's Length)")]
    [TestCase(7568u, false, TestName = "{m}(Esuna)")]
    [TestCase(3541u, false, TestName = "{m}(Clemency)")]
    [TestCase(7535u, false, TestName = "{m}(Reprisal)")]
    [TestCase(41u, false, TestName = "{m}(Overpower)")]
    [TestCase(3569u, false, TestName = "{m}(Asylum)")]
    [TestCase(188u, false, TestName = "{m}(Sacred Soil)")]
    [TestCase(30u, false, TestName = "{m}(Hallowed Ground)")]
    [TestCase(43u, false, TestName = "{m}(Holmgang)")]
    [TestCase(3638u, false, TestName = "{m}(Living Dead)")]
    [TestCase(16152u, false, TestName = "{m}(Superbolide)")]
    public void OnlyAnActionAimedAtADollAndNotAtItsUserIsAHitOnIt(uint press, bool hits)
    {
        var stage = Human(ThePull);
        bool hit = false;
        float? tetheredAt = null;
        var run = stage.Run(MeleeDpsA, stopAt: 35.2f, probe: p =>
        {
            if (p.Time < 32.7f) return;
            var doll = Doll(p, stage, MeleeDpsA);
            if (p.Crossed(34.3f)) Press(press, Id(doll.Enemy));
            if (p.Crossed(35.1f)) (hit, tetheredAt) = (Rules.Seat(doll.Holder) == MeleeDpsA, doll.TetheredAt);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(hit, Is.EqualTo(hits));
        if (hits) Assert.That(tetheredAt, Is.EqualTo(34.3f + P1Duration.DollTethersAfterItsFirstHit).Within(0.05f));
    }

    // A cast is reported as it starts and again as it resolves.
    [Test]
    public void AHardCastOnADollIsAHitAsItResolves()
    {
        var stage = Human(ThePull);
        var hit = new List<bool>();
        var run = stage.Run(CasterDps, stopAt: 37f, probe: p =>
        {
            if (p.Time < 32.7f) return;
            var doll = Doll(p, stage, CasterDps);
            var userActions = (FakeUserActions)Natives.UserActions;
            if (p.Crossed(34f)) userActions.Act(new PlayerAction(PlayerActionStep.CastStarted, ActionType.Action, 141, Id(doll.Enemy), 2.8f));
            if (p.Crossed(36.5f)) hit.Add(doll.HasBeenHit);
            if (p.Crossed(36.8f)) userActions.Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, 141, Id(doll.Enemy)));
            if (p.Crossed(36.9f)) hit.Add(doll.HasBeenHit);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(hit, Is.EqualTo(new[] { false, true }));
    }

    // A doll's health is scripted from its tether, at 34.9: a quarter 4 s later, and no lower than a tenth.
    [Test]
    public void ADollsHealthFallsFromItsTetherThroughAQuarterToItsFloorAndItsBarShowsIt()
    {
        var stage = new Stage { First = Pull, Last = Pull, NoFeedBy = [MeleeDpsA] };
        var health = new List<float>();
        var bar = new List<float>();
        var reprisal = new List<bool>();
        const ushort Reprisal = 1193;
        var run = stage.Run(MainTank, stopAt: 42f, probe: p =>
        {
            if (p.Time < 34f) return;
            var doll = Doll(p, stage, MeleeDpsA);
            if (p.Crossed(34.5f)) doll.Enemy.AddStatus(Reprisal, 15f);
            foreach (var at in new[] { 34.8f, 36.9f, 38.85f, 38.95f, 41f })
            {
                if (!p.Crossed(at)) continue;
                health.Add(doll.Health(p.Time));
                bar.Add(doll.Enemy.Proxy!.Health / (float)doll.Enemy.Proxy.MaxHealth);
                reprisal.Add(p.Fake.BattleCharas.Find(doll.Enemy.EntityId)!.Statuses.Keys.Any(key => key.StatusId == Reprisal));
            }
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(health, Is.EqualTo(new[] { 1f, 0.625f, 0.26f, 0.24f, 0.1f }).Within(0.02f));
        Assert.That(bar, Is.EqualTo(new[] { 1f, 0.65f, 0.3f, 0.25f, 0.1f }).Within(0.001f), "in steps of a twentieth, never below what it has");
        Assert.That(reprisal, Is.All.True, "what a player's action left on the doll is still on it after each step");
    }

    // -- Exhaust --

    [Test]
    public void EveryDollThatIsUpExhaustsOnTheTimelineAndOneExhaustLeavesItsStatusAndKillsNobody()
    {
        var watch = new Rules.Watch();
        var carries = new List<bool>();
        var run = ThePull.Run(MainTank, probe: p =>
        {
            watch.See(p);
            foreach (var at in new[] { 38.4f, 39f, 43.4f, 43.6f })
                if (p.Crossed(at)) carries.Add(p.Member(MeleeDpsA)!.HasStatus(P1StatusId.LuminousAetheroplasm));
        });

        Assert.That(run.Passed, run.ToString);
        var exhausts = watch.Sent("ability", P1ActionId.Exhaust);
        Assert.That(exhausts.Select(sent => sent.At), Is.EqualTo(new[] { 38.5f, 38.5f, 38.5f, 38.5f, 49.14f, 49.14f }).Within(TimelineRun.FrameLate),
                    "all four, then the two that are not fed yet; none past the part's last line at 55.8");
        Assert.That(carries, Is.EqualTo(new[] { false, true, true, false }), "Luminous Aetheroplasm, 4.96 s from 38.5");
    }

    // Nobody has hit a doll, so each stands on its spot. H2 is put on the line between the south-east and south-west
    // dolls, a yalm from the one and just inside or outside the other's reach.
    [TestCase(-0.1f, true)]
    [TestCase(0.1f, false)]
    public void WhoeverTwoExhaustsReachAtOnceExplodes(float pastTheSecondsReach, bool explodes)
    {
        var stage = new Stage { First = Pull, Last = Pull, NoHitBy = [MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps] };
        var watch = new Rules.Watch();
        var run = stage.Run(MainTank, stopAt: 40f, probe: p =>
        {
            watch.See(p);
            if (!p.Crossed(38f)) return;
            var (near, far) = (Doll(p, stage, MeleeDpsA).Enemy, Doll(p, stage, MeleeDpsB).Enemy);
            var reach = 8f + far.HitboxRadius + pastTheSecondsReach;
            p.Member(ShieldHealer)!.SetPosition(far.Position + Vector3.Normalize(near.Position - far.Position) * reach);
        });

        Assert.That(run.Failure, Is.Null, run.ToString);
        var bursts = watch.Sent("ability", P1ActionId.ExhaustFromAPlayer);
        if (!explodes)
        {
            Assert.That(run.Deaths, Is.Empty, run.ToString);
            Assert.That(bursts, Is.Empty);
            Assert.That(watch.Sent("ability", P1ActionId.Exhaust).Count(sent => sent.Hits.Contains(ShieldHealer)), Is.EqualTo(1), "one Exhaust's status only");
            return;
        }
        Assert.That(run.Deaths.Select(d => d.Role), Does.Contain(ShieldHealer), run.ToString);
        Assert.That(run.Deaths.Select(d => d.ActionId), Is.All.EqualTo((uint?)P1ActionId.ExhaustFromAPlayer), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Cause), Is.All.Contain(TeaP1JagdDolls.LuminousAetheroplasmBurst), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Time), Is.All.EqualTo(38.5f).Within(0.05f), run.ToString);
        Assert.That(bursts, Has.Count.EqualTo(1), "the explosion shows round the player");
        Assert.That(watch.Sent("ability", P1ActionId.Exhaust).Count(sent => sent.Hits.Contains(ShieldHealer)), Is.EqualTo(2), "the casts hit whom the rule counted");
    }

    // H2 explodes north of the middle, in reach of the two northern dolls. H1 is put 8.4 y west of it and dies with
    // it; M2, put 8.6 y east of it, does not. Each of those stands in one doll's reach only.
    private static void PutOneWhereTwoDollsReachWithOthersJustInsideAndJustOutsideItsExplosion(ScenarioProbe p)
    {
        p.Member(ShieldHealer)!.SetPosition(new Vector3(1.9f, 0f, -9.5f));
        p.Member(RegenHealer)!.SetPosition(new Vector3(-6.5f, 0f, -9.5f));
        p.Member(MeleeDpsB)!.SetPosition(new Vector3(10.5f, 0f, -9.5f));
    }

    [Test]
    public void AnExplosionKillsEveryoneWithinEightAndAHalfYalms()
    {
        var stage = new Stage { First = Pull, Last = Pull, NoHitBy = [MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps] };
        var run = stage.Run(MainTank, stopAt: 40f, probe: p =>
        {
            if (p.Crossed(38f)) PutOneWhereTwoDollsReachWithOthersJustInsideAndJustOutsideItsExplosion(p);
        });

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(run.Deaths.Select(d => d.Role), Is.EquivalentTo(new[] { ShieldHealer, RegenHealer }), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Cause), Is.All.Contain(TeaP1JagdDolls.LuminousAetheroplasmBurst), run.ToString);
    }

    [Test]
    public void GodmodeHearsOfAnExplosionOncePerPlayer()
    {
        IReadOnlyList<string> chat = [];
        var stage = new Stage { First = Pull, Last = Pull, NoHitBy = [MeleeDpsA, MeleeDpsB, PhysRangedDps, CasterDps], GodMode = true };
        var run = stage.Run(MainTank, stopAt: 40f, probe: p =>
        {
            chat = p.Chat.ToList();
            if (p.Crossed(38f)) PutOneWhereTwoDollsReachWithOthersJustInsideAndJustOutsideItsExplosion(p);
        });

        Assert.That(run.Deaths, Is.Empty, run.ToString);
        Assert.That(chat.Where(line => line.Contains(Died)).ToList(),
                    Has.Count.EqualTo(2).And.All.Contain(TeaP1JagdDolls.LuminousAetheroplasmBurst), () => string.Join(Environment.NewLine, chat));
    }

    // -- Feeding --
    // M1's doll, tethered at 34.9 and under a quarter from 38.9, is put beside a boss at 40.0, on its far side from the
    // other boss, with M1 near enough that it does not walk: toward the Living Liquid, or on the main tank's spot,
    // since the Hand's far side is the middle tornado's water. The line is 4.6 y between their centres.

    [TestCase(TeaP1Boss.LivingLiquid, 4.4f, true)]
    [TestCase(TeaP1Boss.LivingLiquid, 4.8f, false)]
    [TestCase(TeaP1Boss.LiquidHand, 4.4f, true)]
    [TestCase(TeaP1Boss.LiquidHand, 4.8f, false)]
    public void AHitDollThatComesWithinTheFeedLineOfABossIsFedToIt(TeaP1Boss boss, float fromItsCentre, bool fed)
    {
        var stage = new Stage { First = Pull, Last = Pull, NoFeedBy = [MeleeDpsA, MeleeDpsB] };
        var watch = new Rules.Watch();
        (TeaP1Boss? FedTo, bool Targetable, bool Active) soon = default, later = default;
        var run = stage.Run(MainTank, stopAt: 42.48f, probe: p =>
        {
            watch.See(p);
            if (p.Time < 34f) return;
            var fight = Stage.Fight(p);
            var doll = Doll(p, stage, MeleeDpsA);
            if (p.Crossed(40f))
            {
                var (to, other) = (fight.Position(boss), fight.Position(boss == TeaP1Boss.LivingLiquid ? TeaP1Boss.LiquidHand : TeaP1Boss.LivingLiquid));
                var away = Vector3.Normalize(to - other);
                doll.Enemy.SetPosition(to + away * fromItsCentre);
                p.Member(MeleeDpsA)!.SetPosition(boss == TeaP1Boss.LivingLiquid ? to + away * (fromItsCentre - 2f) : p.Member(MainTank)!.Position);
            }
            if (p.Crossed(40.2f)) soon = (doll.FedTo, doll.Enemy.Targetable, doll.Enemy.IsActive);
            if (p.Crossed(42.45f)) later = (doll.FedTo, doll.Enemy.Targetable, doll.Enemy.IsActive);
        });

        Assert.That(run.Passed, run.ToString);
        var complexity = watch.Sent("ability", P1ActionId.ReducibleComplexity);
        if (!fed)
        {
            Assert.That((soon, later), Is.EqualTo(((null as TeaP1Boss?, true, true), (null as TeaP1Boss?, true, true))));
            Assert.That(complexity, Is.Empty);
            return;
        }
        Assert.That(soon, Is.EqualTo(((TeaP1Boss?)boss, false, true)), "fed: no longer targetable");
        Assert.That(later.Active, Is.False, "gone after its Reducible Complexity");
        Assert.That(complexity.Select(sent => sent.At), Is.EqualTo(new[] { 41.07f }).Within(0.05f), "1.07 s after the feed");
    }

    [Test]
    public void ADollFedWithAQuarterOfItsHealthOrMoreKillsEveryone([Values] bool godMode)
    {
        var stage = new Stage { First = Pull, Last = Pull, NoFeedBy = [MeleeDpsA], GodMode = godMode };
        IReadOnlyList<string> chat = [];
        var run = stage.Run(MainTank, stopAt: 39f, probe: p =>
        {
            chat = p.Chat.ToList();
            if (!p.Crossed(36.9f)) return;
            var liquid = Stage.Fight(p).Position(TeaP1Boss.LivingLiquid);
            Doll(p, stage, MeleeDpsA).Enemy.SetPosition(liquid + new Vector3(0f, 0f, -3f));
            p.Member(MeleeDpsA)!.SetPosition(liquid + new Vector3(0f, 0f, -5f));
        });

        Assert.That(run.Failure, Is.Null, run.ToString);
        if (godMode)
        {
            Assert.That(run.Deaths, Is.Empty, run.ToString);
            Assert.That(chat.Where(line => line.Contains(Died)).ToList(),
                        Has.Count.EqualTo(8).And.All.Contain(TeaP1JagdDolls.ADollWasFedTooSoon), () => string.Join(Environment.NewLine, chat));
            return;
        }
        Assert.That(run.Deaths.Select(d => d.Role), Is.EquivalentTo(PerRole.All), run.ToString);
        Assert.That(run.Deaths.Select(d => d.ActionId), Is.All.EqualTo((uint?)P1ActionId.ReducibleComplexity), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Cause), Is.All.Contain(TeaP1JagdDolls.ADollWasFedTooSoon), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Time), Is.All.EqualTo(37.97f).Within(0.05f), run.ToString);
    }

    // The line is a quarter of its health. M1's doll, tethered at 34.9, holds 26 % at 38.85 and 24 % at 38.95.
    [TestCase(38.85f, true)]
    [TestCase(38.95f, false)]
    public void ADollFedJustOverAQuarterKillsEveryoneAndOneJustUnderNobody(float fedAt, bool everyoneDies)
    {
        var stage = new Stage { First = Pull, Last = Pull, NoFeedBy = [MeleeDpsA] };
        TeaP1Boss? fedTo = null;
        var run = stage.Run(MainTank, stopAt: 40.5f, probe: p =>
        {
            if (p.Time < 34f) return;
            var doll = Doll(p, stage, MeleeDpsA);
            if (p.Crossed(fedAt))
            {
                var liquid = Stage.Fight(p).Position(TeaP1Boss.LivingLiquid);
                doll.Enemy.SetPosition(liquid + new Vector3(0f, 0f, -3f));
                p.Member(MeleeDpsA)!.SetPosition(liquid + new Vector3(0f, 0f, -5f));
            }
            if (p.Crossed(fedAt + 0.1f)) fedTo = doll.FedTo;
        });

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(fedTo, Is.EqualTo(TeaP1Boss.LivingLiquid));
        if (!everyoneDies)
        {
            Assert.That(run.Deaths, Is.Empty, run.ToString);
            return;
        }
        Assert.That(run.Deaths.Select(d => d.Role), Is.EquivalentTo(PerRole.All), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Cause), Is.All.Contain(TeaP1JagdDolls.ADollWasFedTooSoon), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Time), Is.All.EqualTo(fedAt + 1.07f).Within(0.05f), run.ToString);
    }

    // The bar's stand-in until it is seen to follow in the client: the human's own doll says in chat when it is ready,
    // once, as it passes a quarter 4 s after its tether. M1 hits at 34.3; the stage feeds its doll at 40.0.
    [TestCase("its own doll", true)]
    [TestCase("another seat's doll", false)]
    [TestCase("as a bot", false)]
    public void AHumansOwnDollSaysOnceInChatWhenItIsUnderAQuarter(string holds, bool said)
    {
        const string ready = "Your Jagd Doll is under a quarter of its health";
        var asAHuman = holds != "as a bot";
        var stage = asAHuman ? Human(ThePull) : ThePull;
        var lines = new List<(float At, string Line)>();
        var seen = 0;
        var run = stage.Run(asAHuman && holds == "its own doll" ? MeleeDpsA : asAHuman ? RegenHealer : MeleeDpsA, stopAt: 42.4f, probe: p =>
        {
            if (p.Crossed(34.3f) && holds == "its own doll") Press(HeavySwing, Id(Doll(p, stage, MeleeDpsA).Enemy));
            lines.AddRange(p.Chat.Skip(seen).Select(line => (p.Time, line)));
            seen = p.Chat.Count;
        });

        Assert.That(run.Passed, run.ToString);
        var readies = lines.Where(line => line.Line.Contains(ready)).ToList();
        if (!said)
        {
            Assert.That(readies, Is.Empty);
            return;
        }
        Assert.That(readies, Has.Count.EqualTo(1));
        Assert.That(readies[0].At, Is.EqualTo(34.3f + 0.6f + 4f).Within(0.1f));
    }

    // The south-west doll stands 3 y from the Living Liquid from 35.0, untouched: nothing feeds it. Hit at 36.0, it is
    // fed there and then, before its tether shows, with its health whole.
    [Test]
    public void ADollHitWithABossBesideItIsFedAtOnce()
    {
        var stage = new Stage { First = Pull, Last = Pull, NoHitBy = [MeleeDpsA] };
        var fed = new List<TeaP1Boss?>();
        float? tetheredAt = 0f;
        var run = stage.Run(MainTank, stopAt: 39f, probe: p =>
        {
            if (p.Time < 34f) return;
            var doll = Doll(p, stage, MeleeDpsA);
            if (p.Crossed(35f)) doll.Enemy.SetPosition(Stage.Fight(p).Position(TeaP1Boss.LivingLiquid) + new Vector3(0f, 0f, -3f));
            if (p.Crossed(35.95f)) fed.Add(doll.FedTo);
            if (p.Crossed(36f)) Stage.Scenario(p).Presses!.Strike(p.Member(MeleeDpsA)!, doll.Enemy);
            if (!p.Crossed(36.1f)) return;
            tetheredAt = doll.TetheredAt;
            fed.Add(doll.FedTo);
        });

        Assert.That(fed, Is.EqualTo(new TeaP1Boss?[] { null, TeaP1Boss.LivingLiquid }));
        Assert.That(tetheredAt, Is.Null);
        Assert.That(run.Deaths.Select(d => d.Role), Is.EquivalentTo(PerRole.All), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Cause), Is.All.Contain(TeaP1JagdDolls.ADollWasFedTooSoon), run.ToString);
    }

    // Nobody hits the melee's dolls. One is parked on the Living Liquid from 33.5, and the other is put on the middle
    // at 42.0, where both bosses walk onto it.
    [Test]
    public void ADollNobodyHasHitIsNeverFed()
    {
        var stage = new Stage { First = Pull, Last = Pull, NoHitBy = [MeleeDpsA, MeleeDpsB] };
        var nearest = float.MaxValue;
        var up = new List<bool>();
        var run = stage.Run(MainTank, stopAt: 48f, probe: p =>
        {
            if (p.Time < 33.45f) return;
            var fight = Stage.Fight(p);
            var (parked, walkedOnto) = (Doll(p, stage, MeleeDpsA), Doll(p, stage, MeleeDpsB));
            if (p.Crossed(33.5f)) parked.Enemy.SetPosition(fight.Position(TeaP1Boss.LivingLiquid));
            if (p.Crossed(42f)) walkedOnto.Enemy.SetPosition(Vector3.Zero);
            if (p.Time > 42f) nearest = MathF.Min(nearest, Vector3.Distance(fight.Position(TeaP1Boss.LivingLiquid), walkedOnto.Enemy.Position));
            if (p.Crossed(47.9f)) up = [parked.IsUp && parked.FedTo is null, walkedOnto.IsUp && walkedOnto.FedTo is null];
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(nearest, Is.LessThan(1f), "the Living Liquid came to stand on the second");
        Assert.That(up, Is.EqualTo(new[] { true, true }));
    }

    // -- A doll nobody feeds --
    // R1 never hits its doll, which stands on its spot to the end and Exhausts from there, alone from 59.78.

    [Test]
    public void ADollLeftUpExhaustsWhileTheTimelineLastsAndNeverKeepsTheRunGoing()
    {
        var watch = new Rules.Watch();
        var run = new Stage { First = Pull, Last = Protean1, NoHitBy = [PhysRangedDps] }.Run(MainTank, probe: watch.See);

        Assert.That(run.Passed, run.ToString);
        Assert.That(watch.Sent("ability", P1ActionId.Exhaust).Select(sent => sent.At),
                    Is.EqualTo(new[] { 38.5f, 38.5f, 38.5f, 38.5f, 49.14f, 49.14f, 59.78f, 70.42f, 81.06f }).Within(TimelineRun.FrameLate),
                    "every 10.64 s to the frame, and none past Drainage at 85.6");
        Assert.That(run.Elapsed, Is.InRange(Stage.ProteanOne + 26f, Stage.ProteanOne + 28f), "the run ends by itself after Drainage");
    }

    [Test]
    public void NoDollExhaustsOnceTheLivingLiquidHasFallen()
    {
        var watch = new Rules.Watch();
        var dollsLeft = -1;
        var run = new Stage { First = Pull, NoHitBy = [PhysRangedDps] }.Run(MainTank, probe: p =>
        {
            watch.See(p);
            if (p.Crossed(Stage.ProteanOne + 67f))
                dollsLeft = p.World.Children.OfType<SimEnemy>().Count(enemy => enemy.IsActive && enemy.BNpcBaseId == P1BNpcBaseId.JagdDoll);
        });

        Assert.That(run.Passed, run.ToString);
        var exhausts = watch.Sent("ability", P1ActionId.Exhaust).Select(sent => sent.At).ToList();
        Assert.That(exhausts[^1], Is.EqualTo(38.5f + 8 * P1Duration.ExhaustEvery).Within(TimelineRun.FrameLate), "the last before it falls at 124.5");
        Assert.That(exhausts, Has.Count.EqualTo(4 + 2 + 7));
        Assert.That(dollsLeft, Is.Zero, "the doll left with the Liquid");
    }

    // -- The first Emboli --

    [Test]
    public void TheFirstEmboliWaitOnTheirTornadoesAndSetOffThroughWhereTheBossesStoodAtPressurize()
    {
        var stage = ThePull;
        var seen = new List<Vector3[]>();
        (Vector3 Liquid, Vector3 Hand) stood = default;
        var count = -1;
        var run = stage.Run(MainTank, stopAt: 48.1f, probe: p =>
        {
            var fight = Stage.Fight(p);
            if (p.Crossed(40.4f)) stood = (fight.Position(TeaP1Boss.LivingLiquid), fight.Position(TeaP1Boss.LiquidHand));
            if (p.Crossed(41f)) count = fight.Emboli.Count;
            foreach (var at in new[] { 41.2f, 45.9f, 48f })
                if (p.Crossed(at)) seen.Add(fight.Emboli.Select(embolus => embolus.Position).ToArray());
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(count, Is.Zero, "none before 41.1");
        var (west, east) = (stage.Frame1.Centre(TeaP1Tornado.West), stage.Frame1.Centre(TeaP1Tornado.East));
        Assert.That(seen[0], Is.EqualTo(new[] { west, east }).Using<Vector3>((a, b) => Vector3.Distance(a, b) < 0.01f), "one on each of the roll's two tornadoes");
        Assert.That(seen[1], Is.EqualTo(seen[0]), "still until 45.95");
        var walked = (48f - 45.95f) * P1Rules.EmbolusSpeed;
        Assert.That(Vector3.Distance(seen[2][0], west + Vector3.Normalize(stood.Hand - west) * walked), Is.LessThan(0.15f), "the west one toward where the Hand stood");
        Assert.That(Vector3.Distance(seen[2][1], east + Vector3.Normalize(stood.Liquid - east) * walked), Is.LessThan(0.15f), "the east one toward where the Liquid stood");
    }

    // At 20 y/s here. H1 is put on the west orb's way, on the spot the off tank left.
    [Test]
    public void ATouchedFirstEmbolusKillsEveryone()
    {
        var stage = new Stage { First = Pull, Last = Pull, EmbolusSpeed = 20f };
        var run = stage.Run(MainTank, stopAt: 50f, takeovers: new Takeover(45f, Stage.OffTanksFlank, Bot: RegenHealer));

        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(run.Deaths.Select(d => d.Role), Is.EquivalentTo(PerRole.All), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Cause), Is.All.Contain(TeaP1LivingLiquid.EmbolusTouched), run.ToString);
        Assert.That(run.Deaths.Select(d => d.Time), Is.All.InRange(45.95f, 46.8f), run.ToString);
    }

    // At 6 y/s here: both are out of the arena by 51.5, and each stays active through the 1.3 s of its fade.
    [Test]
    public void AnEmbolusThatLeavesTheArenaIsFadedOutOnce()
    {
        var watch = new Rules.Watch();
        var active = -1;
        var run = new Stage { First = Pull, Last = Pull, EmbolusSpeed = 6f }.Run(MainTank, stopAt: 54f, probe: p =>
        {
            watch.See(p);
            if (p.Crossed(53.5f)) active = Stage.Fight(p).Emboli.Count(embolus => embolus.IsActive);
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(active, Is.Zero, "both have left");
        Assert.That(watch.Spawned(P1BNpcBaseId.Embolus).Select(watch.Fades), Is.EqualTo(new[] { 1, 1 }));
    }

    // -- The second busters --

    [TestCase(false, "OffTank")]
    [TestCase(true, "MainTank")]
    public void TheSecondCleavesGoAtEachBossesOwnTarget(bool noProvokeBack, string theHandCleaves)
    {
        var watch = new Rules.Watch();
        var run = new Stage { First = Pull, Last = Pull, NoProvokeBack = noProvokeBack }.Run(RegenHealer, probe: watch.See);

        Assert.That(run.Passed, $"a main tank left with both takes both and lives{Environment.NewLine}{run}");
        AssertSent(watch.Sent("ability", P1ActionId.FluidSwing).Where(sent => sent.At > 50f), (55.55f, "MainTank"));
        AssertSent(watch.Sent("ability", P1ActionId.FluidStrike).Where(sent => sent.At > 50f), (55.8f, theHandCleaves));
    }
}
