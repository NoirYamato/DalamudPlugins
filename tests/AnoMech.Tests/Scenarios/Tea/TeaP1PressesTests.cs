using System.Numerics;
using System.Reflection;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Tests;

// The human's own Arm's Length, Surecast or Inner Release, its Esuna or Warden's Paean, its Provoke, its invulnerability
// and its hits on an enemy, as the client reports the press. The player sits in the main tank's seat.
public class TeaP1PressesTests
{
    private const uint Provoke = 7533;
    private const ulong CurrentTargetStandIn = 0xE0000000;

    // -- Provoke --
    // Each test presses at 1.0; the player sits in the main tank's seat and one enemy stands in the arena.

    [Test]
    public void AProvokeOnAnEnemyIsReportedWithItsTargetOnceTheRunLooks()
    {
        var insideThePress = -1;
        List<TeaP1Provoke> raised = [];
        ulong enemyId = 0;
        Presses((world, presses, enemy) =>
        {
            presses.Provoked += raised.Add;
            enemyId = (ulong)enemy.GameObjectId;
            world.Events.Add(1f, () =>
            {
                Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, Provoke, enemyId));
                insideThePress = raised.Count;
            });
            world.Events.Add(2f, () => { });
        });

        Assert.That(insideThePress, Is.Zero, "nothing settles inside the client's own call");
        Assert.That(raised.Select(p => (((ISimPartyMember)p.By).Role, p.TargetId, p.ByHuman)), Is.EqualTo(new[] { (MainTank, enemyId, true) }));
        Assert.That(raised[0].At, Is.EqualTo(1f).Within(TimelineRun.FrameLate), "it carries the time it was heard");
    }

    [TestCase(true, TestName = "{m}(at a party member)")]
    [TestCase(false, TestName = "{m}(at nothing)")]
    public void AProvokeAimedAtNoEnemyIsNone(bool atAPartyMember)
    {
        List<TeaP1Provoke> provokes = [];
        var strikes = 0;
        Presses((world, presses, _) =>
        {
            presses.Provoked += provokes.Add;
            presses.Struck += _ => strikes++;
            world.Events.Add(1f, () => Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, Provoke,
                atAPartyMember ? (ulong)world.Party.Get(OffTank)!.GameObjectId : 0ul)));
            world.Events.Add(2f, () => { });
        });

        Assert.That((provokes.Count, strikes), Is.EqualTo((0, 0)));
    }

    // -- A tank's invulnerability --
    // Its status is there as the client's call returns, where the sim's own resolving of the player's actions would put
    // it: a press in the gap before a frame counts in that frame.

    [TestCase(P1PlayerActionId.HallowedGround, P1StatusId.HallowedGround, false)]
    [TestCase(P1PlayerActionId.Holmgang, P1StatusId.Holmgang, false)]
    [TestCase(P1PlayerActionId.LivingDead, P1StatusId.LivingDead, false)]
    [TestCase(P1PlayerActionId.Superbolide, P1StatusId.Superbolide, false)]
    [TestCase(P1PlayerActionId.Holmgang, P1StatusId.Holmgang, true)]
    public void AnInvulnerabilityPutsItsStatusOnThePlayerThereAndThenForTenSeconds(uint press, int status, bool resolved)
    {
        var carries = new List<bool>();
        var others = 0;
        Presses((world, presses, _) =>
        {
            var player = world.Party.Player!;
            world.Events.Add(1f, () =>
            {
                carries.Add(player.HasStatus((ushort)status));
                if (resolved) TopP6HumanMitigation.PressNow(world, [press]);
                else Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, press, (ulong)world.Party.Get(OffTank)!.GameObjectId));
                carries.Add(player.HasStatus((ushort)status));
                others = PerRole.All.Count(role => role != MainTank && world.Party.Get(role)!.HasStatus((ushort)status));
            });
            world.Events.Add(10.9f, () => carries.Add(player.HasStatus((ushort)status)));
            world.Events.Add(11.1f, () => carries.Add(player.HasStatus((ushort)status)));
        }, resolved);

        Assert.That(carries, Is.EqualTo(new[] { false, true, true, false }));
        Assert.That(others, Is.Zero, "on the player alone, whatever the press was aimed at");
    }

    [TestCase(true, TestName = "{m}(the listener disposed)")]
    [TestCase(false, TestName = "{m}(the player dead)")]
    public void AnInvulnerabilityHeardTooLateGivesNothing(bool disposed)
    {
        var carries = true;
        Presses((world, presses, _) =>
        {
            var player = world.Party.Player!;
            world.Events.Add(0.5f, () =>
            {
                if (disposed) presses.Dispose();
                else player.Die(P1ActionId.Splash);
            });
            world.Events.Add(1f, () =>
            {
                Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, P1PlayerActionId.Holmgang, 0));
                carries = player.HasStatus(P1StatusId.Holmgang);
            });
        }, expectDeaths: disposed ? 0 : 1);

        Assert.That(carries, Is.False);
    }

    [TestCase(P1PlayerActionId.HallowedGround, "Hallowed Ground", 82)]
    [TestCase(P1PlayerActionId.Holmgang, "Holmgang", 409)]
    [TestCase(P1PlayerActionId.LivingDead, "Living Dead", 810)]
    [TestCase(P1PlayerActionId.Superbolide, "Superbolide", 1836)]
    public void EachInvulnerabilityIsTheActionAndTheStatusItIsNamedFor(uint press, string name, int status)
    {
        FakeGame.Install();

        Assert.That(Natives.Data.Action(press)?.Name, Is.EqualTo(name));
        Assert.That(Natives.Data.Status((ushort)status)?.Name, Is.EqualTo(name));
        Assert.That(P1StatusId.Invulnerabilities, Does.Contain((ushort)status));
    }

    // -- A hit on an enemy --
    // A press comes aimed at the client's target whatever the button is, so what is a hit is read off what the action
    // can be aimed at: one target, and neither its user nor the ground. One that can also land on a party member, as a
    // monk's Thunderclap can, is a hit on the enemy it is pressed at. An auto-attack is one too: the client's swings come
    // as Attack or Shot.

    [TestCase(7u, "attack", true)]
    [TestCase(8u, "Shot", true)]
    [TestCase(31u, "Heavy Swing", true)]
    [TestCase(16u, "Shield Bash", true)]
    [TestCase(9u, "Fast Blade", true)]
    [TestCase(3617u, "Hard Slash", true)]
    [TestCase(7533u, "Provoke", true)]
    [TestCase(119u, "Stone", true)]
    [TestCase(141u, "Fire", true)]
    [TestCase(97u, "Heavy Shot", true)]
    [TestCase(2866u, "Split Shot", true)]
    [TestCase(7384u, "Holy Spirit", true)]
    [TestCase(16143u, "Lightning Shot", true)]
    [TestCase(25762u, "Thunderclap", true)]
    [TestCase(7531u, "Rampart", false)]
    [TestCase(7548u, "Arm's Length", false)]
    [TestCase(7568u, "Esuna", false)]
    [TestCase(3541u, "Clemency", false)]
    [TestCase(7535u, "Reprisal", false)]
    [TestCase(41u, "Overpower", false)]
    [TestCase(7381u, "Total Eclipse", false)]
    [TestCase(3569u, "Asylum", false)]
    [TestCase(188u, "Sacred Soil", false)]
    [TestCase(7439u, "Earthly Star", false)]
    [TestCase(3573u, "Ley Lines", false)]
    [TestCase(2262u, "Shukuchi", false)]
    [TestCase(30u, "Hallowed Ground", false)]
    [TestCase(43u, "Holmgang", false)]
    [TestCase(3638u, "Living Dead", false)]
    [TestCase(16152u, "Superbolide", false)]
    public void OnlyAnActionAimedAtOneTargetOtherThanItsUserIsAHitOnTheEnemyItIsPressedAt(uint press, string name, bool hits)
    {
        List<TeaP1Strike> strikes = [];
        ulong enemyId = 0;
        string? pressed = null;
        Presses((world, presses, enemy) =>
        {
            presses.Struck += strikes.Add;
            enemyId = (ulong)enemy.GameObjectId;
            pressed = Natives.Data.Action(press)?.Name;
            world.Events.Add(1f, () => Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, press, enemyId)));
            world.Events.Add(2f, () => { });
        });

        Assert.That(pressed, Is.EqualTo(name));
        if (hits) Assert.That(strikes.Select(s => (((ISimPartyMember)s.By).Role, s.TargetId, s.ByHuman)), Is.EqualTo(new[] { (MainTank, enemyId, true) }));
        else Assert.That(strikes, Is.Empty);
    }

    [Test]
    public void OnlyAResolvedActionIsAHit()
    {
        var strikes = new List<float>();
        Presses((world, presses, enemy) =>
        {
            presses.Struck += strike => strikes.Add(strike.At);
            var enemyId = (ulong)enemy.GameObjectId;
            world.Events.Add(0.5f, () => Act(new PlayerAction(PlayerActionStep.CastStarted, ActionType.Action, 119, enemyId, 1.5f)));
            world.Events.Add(0.7f, () => Act(new PlayerAction(PlayerActionStep.Interrupted, ActionType.Action, 119, enemyId)));
            world.Events.Add(0.8f, () => Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Item, 31, enemyId)));
            world.Events.Add(1f, () => Act(new PlayerAction(PlayerActionStep.CastStarted, ActionType.Action, 119, enemyId, 1.5f)));
            world.Events.Add(2.5f, () => Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, 119, enemyId)));
            world.Events.Add(3f, () => { });
        });

        Assert.That(strikes, Is.EqualTo(new[] { 2.5f }).Within(TimelineRun.FrameLate));
    }

    [TestCase(true, TestName = "{m}(at a party member)")]
    [TestCase(false, TestName = "{m}(at nothing)")]
    public void AHitAimedAtNoEnemyIsNone(bool atAPartyMember)
    {
        var strikes = 0;
        Presses((world, presses, _) =>
        {
            presses.Struck += _ => strikes++;
            world.Events.Add(1f, () => Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, 31,
                atAPartyMember ? (ulong)world.Party.Get(OffTank)!.GameObjectId : 0ul)));
            world.Events.Add(2f, () => { });
        });

        Assert.That(strikes, Is.Zero);
    }

    // A client may report a press aimed at its "current target" stand-in rather than at the target's own id.
    [TestCase(Provoke)]
    [TestCase(31u)]
    public void APressAimedAtTheClientsCurrentTargetGoesToThePlayersTarget(uint press)
    {
        var leftInstalled = Plugin.TargetManager;
        var targetManager = DispatchProxy.Create<ITargetManager, TopP6MeleeWindowTests.TargetStandIn>();
        DalamudServices.Install(nameof(Plugin.TargetManager), targetManager);
        try
        {
            var targets = new List<ulong>();
            ulong enemyId = 0;
            Presses((world, presses, enemy) =>
            {
                presses.Provoked += provoke => targets.Add(provoke.TargetId);
                presses.Struck += strike => targets.Add(strike.TargetId);
                enemyId = (ulong)enemy.GameObjectId;
                world.Events.Add(1f, () =>
                {
                    ((TopP6MeleeWindowTests.TargetStandIn)(object)targetManager).Target = enemyId;
                    Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, press, CurrentTargetStandIn));
                });
                world.Events.Add(2f, () => { });
            });

            Assert.That(targets, Is.Not.Empty.And.All.EqualTo(enemyId));
        }
        finally
        {
            DalamudServices.Install(nameof(Plugin.TargetManager), leftInstalled);
        }
    }

    [Test]
    public void ADisposedListenerReportsNoProvokeAndNoHit()
    {
        var raised = 0;
        Presses((world, presses, enemy) =>
        {
            presses.Provoked += _ => raised++;
            presses.Struck += _ => raised++;
            var enemyId = (ulong)enemy.GameObjectId;
            world.Events.Add(0.5f, () => Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, Provoke, enemyId)));
            world.Events.Add(0.5f, presses.Dispose);
            world.Events.Add(1f, () =>
            {
                Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, Provoke, enemyId));
                presses.Provoke(world.Party.Get(OffTank)!, enemy);
                presses.Strike(world.Party.Get(MeleeDpsA)!, enemy);
                presses.Invulnerability(world.Party.Get(OffTank)!);
            });
            world.Events.Add(2f, () => raised += PerRole.All.Count(role => P1StatusId.Invulnerabilities.Any(world.Party.Get(role)!.HasStatus)));
        });

        Assert.That(raised, Is.Zero);
    }

    // A scenario has no hook for its run's end, so a listener can outlive its run and hear a press made in another
    // scenario's. Each run replaces the world's Rng, here at 0.5.
    [Test]
    public void AListenerWhoseRunHasBeenReplacedHearsNothing()
    {
        var raised = 0;
        var covered = true;
        Presses((world, presses, enemy) =>
        {
            presses.Provoked += _ => raised++;
            presses.Struck += _ => raised++;
            var enemyId = (ulong)enemy.GameObjectId;
            world.Events.Add(0.5f, () => world.Reseed(7));
            world.Events.Add(1f, () =>
            {
                Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, P1PlayerActionId.Holmgang, 0));
                Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, P1PlayerActionId.ArmsLength, 0));
                Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, Provoke, enemyId));
                covered = world.Party.Player!.HasStatus(P1StatusId.Holmgang) || presses.AntiKnockbackUp(world.Party.Player!);
            });
            world.Events.Add(2f, () => { });
        });

        Assert.That((covered, raised), Is.EqualTo((false, 0)));
    }

    // -- The run's own presses, for a seat nobody presses for --

    [Test]
    public void TheRunsOwnProvokeAndHitAreReportedAtOnceAndPlayedOnABotOnly()
    {
        List<(PartyRole By, ulong Target, bool ByHuman, string Kind)> raised = [];
        var offTanksId = "";
        ulong enemyId = 0;
        var run = Presses((world, presses, enemy) =>
        {
            presses.Provoked += provoke => raised.Add((((ISimPartyMember)provoke.By).Role, provoke.TargetId, provoke.ByHuman, "provoke"));
            presses.Struck += strike => raised.Add((((ISimPartyMember)strike.By).Role, strike.TargetId, strike.ByHuman, "hit"));
            enemyId = (ulong)enemy.GameObjectId;
            world.Events.Add(1f, () =>
            {
                offTanksId = world.Party.Get(OffTank)!.EntityId.ToString("X8");
                presses.Provoke(world.Party.Get(OffTank)!, enemy);
                presses.Provoke(world.Party.Get(MainTank)!, enemy);
                presses.Strike(world.Party.Get(MeleeDpsA)!, enemy);
                raised.Add((MainTank, 0, false, $"settled {raised.Count}"));
            });
            world.Events.Add(2f, () => { });
        });

        Assert.That(raised, Is.EqualTo(new[]
        {
            (OffTank, enemyId, false, "provoke"), (MainTank, enemyId, false, "provoke"), (MeleeDpsA, enemyId, false, "hit"), (MainTank, 0ul, false, "settled 3"),
        }));
        Assert.That(run.Sent("ability", Provoke).Select(sent => sent.Actor), Is.EqualTo(new[] { offTanksId }),
                    "never on the player: an effect the client sees itself cast breaks its own cast state");
    }

    // Provoke's 25 y are counted past the two hitboxes, a tank's half yalm and the Liquid Hand's 1.6 y: 27.1 y between
    // their centres.
    [TestCase(27.0f, true)]
    [TestCase(27.2f, false)]
    public void TheRunsOwnProvokeReachesTwentyFiveYalmsPastTheTwoHitboxes(float apart, bool reaches)
    {
        var (hitboxes, inReach, pressed, raised) = (float.NaN, false, false, 0);
        Presses((world, presses, _) =>
        {
            var hand = world.SpawnEnemy(new EnemySpawnConfig(BNpcBaseId: P1BNpcBaseId.LiquidHand, NameId: P1BNpcNameId.LiquidHand, Level: Level,
                Placement: new Placement(new Vector3(0f, 0f, 10f), 0f)))!;
            presses.Provoked += _ => raised++;
            world.Events.Add(1f, () =>
            {
                var offTank = world.Party.Get(OffTank)!;
                TimelineRun.Place(world, OffTank, 0f, 10f - apart);
                hitboxes = offTank.HitboxRadius + hand.HitboxRadius;
                inReach = presses.ProvokeReaches(offTank, hand);
                pressed = presses.Provoke(offTank, hand);
            });
            world.Events.Add(2f, () => { });
        });

        Assert.That(hitboxes, Is.EqualTo(2.1f).Within(0.001f));
        Assert.That((inReach, pressed, raised), Is.EqualTo((reaches, reaches, reaches ? 1 : 0)));
    }

    // A bot's is its job's; the player's seat gets the one the bot of that seat would press.
    [Test]
    public void TheRunsOwnInvulnerabilityIsTheSeatsJobs()
    {
        var offTanksId = "";
        var carries = new List<bool>();
        var run = Presses((world, presses, _) =>
        {
            var (player, offTank) = (world.Party.Get(MainTank)!, world.Party.Get(OffTank)!);
            world.Events.Add(1f, () =>
            {
                offTanksId = offTank.EntityId.ToString("X8");
                presses.Invulnerability(offTank);
                presses.Invulnerability(player);
                carries.AddRange([offTank.HasStatus(P1StatusId.HallowedGround), player.HasStatus(P1StatusId.Holmgang)]);
            });
            world.Events.Add(10.9f, () => carries.Add(offTank.HasStatus(P1StatusId.HallowedGround)));
            world.Events.Add(11.1f, () => carries.Add(offTank.HasStatus(P1StatusId.HallowedGround)));
        });

        Assert.That(carries, Is.EqualTo(new[] { true, true, true, false }));
        Assert.That(run.Sent("ability", P1PlayerActionId.HallowedGround, P1PlayerActionId.Holmgang).Select(sent => (sent.ActionId, sent.Actor)),
                    Is.EqualTo(new[] { (P1PlayerActionId.HallowedGround, offTanksId) }));
    }

    // One enemy in the arena and the listener ticked as a scenario ticks it; the run ends once its events run dry.
    private static TimelineRunResult Presses(Action<SimWorld, TeaP1Presses, SimEnemy> schedule, bool resolved = false, int expectDeaths = 0)
    {
        TeaP1Presses? listener = null;
        var run = TimelineRun.Execute(world =>
        {
            var presses = listener = new TeaP1Presses(world);
            var enemy = world.SpawnEnemy(new EnemySpawnConfig(BNpcBaseId: P1BNpcBaseId.CruiseChaser, NameId: P1BNpcNameId.CruiseChaser, Level: Level))!;
            schedule(world, presses, enemy);
        }, new TimelineRunOptions { Phase = TeaZone.P1, HumanPlayer = true, UserActionsEnabled = resolved, Tick = () => listener?.Tick() });

        listener?.Dispose();
        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(run.Deaths, Has.Count.EqualTo(expectDeaths), run.ToString);
        return run;
    }

    [TestCase(P1PlayerActionId.ArmsLength)]
    [TestCase(P1PlayerActionId.Surecast)]
    public void AnAntiKnockbackCoversThePlayerForSixSecondsFromThePress(uint press)
    {
        var covered = Covered(new[] { 0.9f, 1.1f, 6.9f, 7.1f }, (world, _) =>
            world.Events.Add(1f, () => Press(press)));

        Assert.That(covered, Is.EqualTo(new[] { false, true, true, false }));
    }

    // A press comes as the id its button is adjusted to, so a button placed as Berserk reports Inner Release at level
    // 80; the Berserk case holds the arm left over for P2 to drop.
    [TestCase(P1PlayerActionId.InnerRelease)]
    [TestCase(P1PlayerActionId.Berserk)]
    public void AWarriorsInnerReleaseCoversItForFifteenSeconds(uint press)
    {
        var covered = Covered(new[] { 0.9f, 1.1f, 15.9f, 16.1f }, (world, _) =>
            world.Events.Add(1f, () => Press(press)));

        Assert.That(covered, Is.EqualTo(new[] { false, true, true, false }));
    }

    // The ids are the client's: a wrong one would leave that press unheard in game with every other test green.
    [TestCase(P1PlayerActionId.ArmsLength, "Arm's Length")]
    [TestCase(P1PlayerActionId.Surecast, "Surecast")]
    [TestCase(P1PlayerActionId.InnerRelease, "Inner Release")]
    [TestCase(P1PlayerActionId.Berserk, "Berserk")]
    public void EachPressIsTheActionItIsNamedFor(uint press, string name)
    {
        FakeGame.Install();

        Assert.That(Natives.Data.Action(press)?.Name, Is.EqualTo(name));
    }

    // Inner Strength, from 1.0, outlasts the Arm's Length pressed at 3.0.
    [Test]
    public void AShorterPressDoesNotCutALongerOneShort()
    {
        var covered = Covered(new[] { 9.5f, 15.9f, 16.1f }, (world, _) =>
        {
            world.Events.Add(1f, () => Press(P1PlayerActionId.InnerRelease));
            world.Events.Add(3f, () => Press(P1PlayerActionId.ArmsLength));
        });

        Assert.That(covered, Is.EqualTo(new[] { true, true, false }));
    }

    [Test]
    public void ASecondPressCoversAgain()
    {
        var covered = Covered(new[] { 7.5f, 9.5f, 15.5f }, (world, _) =>
        {
            world.Events.Add(1f, () => Press(P1PlayerActionId.ArmsLength));
            world.Events.Add(9f, () => Press(P1PlayerActionId.Surecast));
        });

        Assert.That(covered, Is.EqualTo(new[] { false, true, false }));
    }

    [Test]
    public void NothingButTheResolvedActionCounts()
    {
        var covered = Covered(new[] { 2f }, (world, _) => world.Events.Add(1f, () =>
        {
            Press(Provoke);
            Act(new PlayerAction(PlayerActionStep.CastStarted, ActionType.Action, P1PlayerActionId.Surecast, 0));
            Act(new PlayerAction(PlayerActionStep.Interrupted, ActionType.Action, P1PlayerActionId.Surecast, 0));
            Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Item, P1PlayerActionId.ArmsLength, 0));
        }));

        Assert.That(covered, Is.EqualTo(new[] { false }));
    }

    [Test]
    public void ADisposedListenerHearsNothing()
    {
        var covered = Covered(new[] { 2f }, (world, presses) =>
        {
            world.Events.Add(0.5f, presses.Dispose);
            world.Events.Add(1f, () => Press(P1PlayerActionId.ArmsLength));
        });

        Assert.That(covered, Is.EqualTo(new[] { false }));
    }

    // Pressed as the run loads, before its listener exists.
    [Test]
    public void APressBeforeTheRunsListenerExistedIsNotHeard()
    {
        var covered = false;
        var run = TimelineRun.Execute(world =>
        {
            Press(P1PlayerActionId.ArmsLength);
            var presses = new TeaP1Presses(world);
            world.Events.Add(1f, () => covered = presses.AntiKnockbackUp(world.Party.Player!));
            world.Events.Add(1.1f, presses.Dispose);
        }, new TimelineRunOptions { Phase = TeaZone.P1, HumanPlayer = true });

        Assert.That(run.Passed, run.ToString);
        Assert.That(covered, Is.False);
    }

    [Test]
    public void ThePressCoversThePlayerAlone()
    {
        var bot = true;
        var player = false;
        var run = TeaP1ActionsTests.Cast((world, _, presses) =>
        {
            world.Events.Add(1f, () => Press(P1PlayerActionId.ArmsLength));
            world.Events.Add(2f, () =>
            {
                bot = presses.AntiKnockbackUp(world.Party.Get(OffTank)!);
                player = presses.AntiKnockbackUp(world.Party.Get(MainTank)!);
            });
        }, out _, human: true);

        Assert.That(run.Passed, run.ToString);
        Assert.That((player, bot), Is.EqualTo((true, false)));
    }

    // Cruise Chaser stands by the north rim with the human 3 y in front of him and the rest behind; the press is at 1.0.
    [TestCase(true, 6.9f, false)]
    [TestCase(true, 7.1f, true)]
    [TestCase(false, 6.9f, false)]
    [TestCase(false, 7.1f, true)]
    public void AHitWithinSixSecondsOfThePressDoesNotPushThePlayer(bool cleave, float hitAt, bool pushed)
    {
        var from = new Vector2(0f, -15f);
        Vector2? stands = null;
        var run = TeaP1ActionsTests.Cast((world, cruiseChaser, presses) =>
        {
            world.Events.Add(0.5f, () =>
            {
                cruiseChaser.SetPosition(new Placement(new Vector3(0f, 0f, -18f), 0f));
                TimelineRun.Place(world, PerRole.All.ToDictionary(role => role, _ => new Vector2(0f, -19.5f)));
                TimelineRun.Place(world, MainTank, from.X, from.Y);
            });
            world.Events.Add(1f, () => Press(P1PlayerActionId.ArmsLength));
            world.Events.Add(hitAt, () => cruiseChaser.Cast(cleave ? TeaP1Actions.AlphaSword(presses) : TeaP1Actions.SuperBlasstyCharge(presses)));
            world.Events.Add(hitAt + 1f, () => stands = new Vector2(world.Party.Get(MainTank)!.Position.X, world.Party.Get(MainTank)!.Position.Z));
        }, out _, human: true);

        Assert.That(run.Passed, run.ToString);
        Assert.That(Vector2.Distance(stands!.Value, from) > 1f, Is.EqualTo(pushed), $"at {stands}");
    }

    // -- Cleanses --
    // Each test presses at 1.0 and reads the listener at 2.0; the player sits in the main tank's seat.

    [TestCase(P1PlayerActionId.Esuna)]
    [TestCase(P1PlayerActionId.WardensPaean)]
    public void ACleanseOnASeatIsReportedOnceTheRunLooks(uint press)
    {
        var raisedInsideThePress = -1;
        List<TeaP1Cleanse> reported = [];
        var (cleanses, raised) = Cleanses((world, presses, heard) => world.Events.Add(1f, () =>
        {
            reported = heard;
            Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, press, (ulong)world.Party.Get(OffTank)!.GameObjectId));
            raisedInsideThePress = heard.Count + presses.Cleanses.Count;
        }));

        Assert.That(raisedInsideThePress, Is.Zero, "nothing settles inside the client's own call");
        Assert.That(cleanses.Select(c => (c.By, c.Target, c.ByHuman)), Is.EqualTo(new[] { (MainTank, OffTank, true) }));
        Assert.That(cleanses[0].At, Is.EqualTo(1f).Within(TimelineRun.FrameLate));
        Assert.That(raised, Is.EqualTo(1));
        Assert.That(reported.Select(c => c.ActionId), Is.EqualTo(new[] { press }), "it says which of the two it was");
    }

    [Test]
    public void ARuleThatDecidesNowHearsACleanseAtOnce()
    {
        var settled = 0;
        Cleanses((world, presses, _) => world.Events.Add(1f, () =>
        {
            Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, P1PlayerActionId.Esuna, (ulong)world.Party.Get(OffTank)!.GameObjectId));
            presses.CatchUp();
            settled = presses.Cleanses.Count;
        }));

        Assert.That(settled, Is.EqualTo(1));
    }

    [Test]
    public void OnlyAResolvedCleanseCounts()
    {
        var (cleanses, raised) = Cleanses((world, _, _) => world.Events.Add(1f, () =>
        {
            var offTank = (ulong)world.Party.Get(OffTank)!.GameObjectId;
            Act(new PlayerAction(PlayerActionStep.CastStarted, ActionType.Action, P1PlayerActionId.Esuna, offTank, 1f));
            Act(new PlayerAction(PlayerActionStep.Interrupted, ActionType.Action, P1PlayerActionId.Esuna, offTank));
            Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Item, P1PlayerActionId.Esuna, offTank));
            Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, Provoke, offTank));
        }));

        Assert.That(cleanses, Is.Empty);
        Assert.That(raised, Is.Zero);
    }

    // A client that still casts Esuna reports the cast's start, then its resolve.
    [Test]
    public void ACastCleanseCountsAsItResolves()
    {
        var (cleanses, _) = Cleanses((world, _, _) =>
        {
            world.Events.Add(0.5f, () => Act(new PlayerAction(PlayerActionStep.CastStarted, ActionType.Action, P1PlayerActionId.Esuna, (ulong)world.Party.Get(OffTank)!.GameObjectId, 1f)));
            world.Events.Add(1.5f, () => Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, P1PlayerActionId.Esuna, (ulong)world.Party.Get(OffTank)!.GameObjectId)));
        });

        Assert.That(cleanses.Select(c => c.Target), Is.EqualTo(new[] { OffTank }));
        Assert.That(cleanses[0].At, Is.EqualTo(1.5f).Within(TimelineRun.FrameLate));
    }

    [TestCase(true, TestName = "{m}(at an enemy)")]
    [TestCase(false, TestName = "{m}(at nothing)")]
    public void ACleanseAimedAtNobodyInThePartyIsThePlayersOwn(bool atAnEnemy)
    {
        var (cleanses, _) = Cleanses((world, _, _) => world.Events.Add(1f, () =>
        {
            var enemy = world.Children.OfType<SimEnemy>().First();
            Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, P1PlayerActionId.Esuna, atAnEnemy ? (ulong)enemy.GameObjectId : 0ul));
        }));

        Assert.That(cleanses.Select(c => (c.By, c.Target)), Is.EqualTo(new[] { (MainTank, MainTank) }));
    }

    // A client may report a press aimed at its "current target" stand-in rather than at the target's own id.
    [Test]
    public void ACleanseAimedAtTheClientsCurrentTargetGoesToThePlayersTarget()
    {
        const ulong CurrentTarget = 0xE0000000;
        var leftInstalled = Plugin.TargetManager;
        var targetManager = DispatchProxy.Create<ITargetManager, TopP6MeleeWindowTests.TargetStandIn>();
        DalamudServices.Install(nameof(Plugin.TargetManager), targetManager);
        try
        {
            var (cleanses, _) = Cleanses((world, _, _) => world.Events.Add(1f, () =>
            {
                ((TopP6MeleeWindowTests.TargetStandIn)(object)targetManager).Target = (ulong)world.Party.Get(RegenHealer)!.GameObjectId;
                Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, P1PlayerActionId.Esuna, CurrentTarget));
            }));

            Assert.That(cleanses.Select(c => c.Target), Is.EqualTo(new[] { RegenHealer }));
        }
        finally
        {
            DalamudServices.Install(nameof(Plugin.TargetManager), leftInstalled);
        }
    }

    [Test]
    public void ACleanseOnTheDeadIsNone()
    {
        var (cleanses, raised) = Cleanses((world, _, _) =>
        {
            world.Events.Add(0.5f, () => world.Party.Get(OffTank)!.Die(P1ActionId.Splash));
            world.Events.Add(1f, () => Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, P1PlayerActionId.Esuna, (ulong)world.Party.Get(OffTank)!.GameObjectId)));
        }, expectDeaths: 1);

        Assert.That(cleanses, Is.Empty);
        Assert.That(raised, Is.Zero);
    }

    [Test]
    public void ACleanseTheRunMakesForASeatIsReportedAtOnce()
    {
        List<TeaP1Cleanse> reported = [];
        var (cleanses, raised) = Cleanses((world, presses, heard) => world.Events.Add(1f, () =>
        {
            reported = heard;
            presses.Cleanse(world.Party.Get(RegenHealer)!, world.Party.Get(MeleeDpsA)!);
        }));

        Assert.That(cleanses.Select(c => (c.By, c.Target, c.ByHuman)), Is.EqualTo(new[] { (RegenHealer, MeleeDpsA, false) }));
        Assert.That(reported.Select(c => c.ActionId), Is.EqualTo(new[] { P1PlayerActionId.Esuna }));
        Assert.That(cleanses[0].At, Is.EqualTo(1f).Within(TimelineRun.FrameLate));
        Assert.That(raised, Is.EqualTo(1));
    }

    [Test]
    public void ADisposedListenerReportsNoCleanse()
    {
        var (cleanses, raised) = Cleanses((world, presses, _) =>
        {
            world.Events.Add(0.5f, () => Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, P1PlayerActionId.Esuna, 0)));
            world.Events.Add(0.5f, presses.Dispose);
            world.Events.Add(1f, () =>
            {
                Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, P1PlayerActionId.Esuna, 0));
                presses.Cleanse(world.Party.Get(RegenHealer)!, world.Party.Get(MeleeDpsA)!);
            });
        });

        Assert.That(cleanses, Is.Empty);
        Assert.That(raised, Is.Zero);
    }

    [TestCase(P1PlayerActionId.Esuna, "Esuna")]
    [TestCase(P1PlayerActionId.WardensPaean, "the Warden's Paean")]
    public void EachCleanseIsTheActionItIsNamedFor(uint press, string name)
    {
        FakeGame.Install();

        Assert.That(Natives.Data.Action(press)?.Name, Is.EqualTo(name).IgnoreCase);
    }

    // The cleanses the listener holds at 2.0, the run's own tick having looked, as seats, and how often Cleansed was
    // raised by then.
    private static (IReadOnlyList<(PartyRole By, PartyRole Target, float At, bool ByHuman)> Cleanses, int Raised) Cleanses(
        Action<SimWorld, TeaP1Presses, List<TeaP1Cleanse>> schedule, int expectDeaths = 0)
    {
        var heard = new List<TeaP1Cleanse>();
        TeaP1Presses? listener = null;
        List<(PartyRole, PartyRole, float, bool)> cleanses = [];
        var raised = 0;
        var run = TimelineRun.Execute(world =>
        {
            var presses = listener = new TeaP1Presses(world);
            presses.Cleansed += heard.Add;
            world.SpawnEnemy(new EnemySpawnConfig(BNpcBaseId: P1BNpcBaseId.CruiseChaser, NameId: P1BNpcNameId.CruiseChaser, Level: Level));
            schedule(world, presses, heard);
            world.Events.Add(2f, () =>
            {
                cleanses = presses.Cleanses.Select(c => (((ISimPartyMember)c.By).Role, ((ISimPartyMember)c.Target).Role, c.At, c.ByHuman)).ToList();
                raised = heard.Count;
            });
        }, new TimelineRunOptions { Phase = TeaZone.P1, HumanPlayer = true, Tick = () => listener?.Tick() });

        listener?.Dispose();
        Assert.That(run.Failure, Is.Null, run.ToString);
        Assert.That(run.Deaths, Has.Count.EqualTo(expectDeaths), run.ToString);
        return (cleanses, raised);
    }

    // Whether the press covers the player at each of `times`.
    private static bool[] Covered(float[] times, Action<SimWorld, TeaP1Presses> schedule)
    {
        var covered = new bool[times.Length];
        var run = TeaP1ActionsTests.Cast((world, _, presses) =>
        {
            schedule(world, presses);
            for (var i = 0; i < times.Length; i++)
            {
                var index = i;
                world.Events.Add(times[i], () => covered[index] = presses.AntiKnockbackUp(world.Party.Player!));
            }
        }, out _, human: true);

        Assert.That(run.Passed, run.ToString);
        return covered;
    }

    private static void Press(uint actionId) => Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, actionId, 0));

    private static void Act(PlayerAction action) => ((FakeUserActions)Natives.UserActions).Act(action);
}
