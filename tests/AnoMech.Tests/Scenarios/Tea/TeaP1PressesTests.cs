using System.Numerics;
using System.Reflection;
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

// The human's own Arm's Length, Surecast or Inner Release, and its Esuna or Warden's Paean, as the client reports the
// press. The player sits in the main tank's seat.
public class TeaP1PressesTests
{
    private const uint Provoke = 7533;

    [TestCase(P1PlayerActionId.ArmsLength)]
    [TestCase(P1PlayerActionId.Surecast)]
    public void AnAntiKnockbackCoversThePlayerForSixSecondsFromThePress(uint press)
    {
        var covered = Covered(new[] { 0.9f, 1.1f, 6.9f, 7.1f }, (world, _) =>
            world.Events.Add(1f, () => Press(press)));

        Assert.That(covered, Is.EqualTo(new[] { false, true, true, false }));
    }

    // A press comes with its button's own id: a button placed as Berserk reports Berserk at level 80, where it is
    // Inner Release.
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

    // The human stands 3 y in front of Cruise Chaser; the press is at 1.0.
    [TestCase(true, 6.9f, false)]
    [TestCase(true, 7.1f, true)]
    [TestCase(false, 6.9f, false)]
    [TestCase(false, 7.1f, true)]
    public void AHitWithinSixSecondsOfThePressDoesNotPushThePlayer(bool cleave, float hitAt, bool pushed)
    {
        var from = new Vector2(0f, 3f);
        Vector2? stands = null;
        var run = TeaP1ActionsTests.Cast((world, cruiseChaser, presses) =>
        {
            world.Events.Add(0.5f, () =>
            {
                TimelineRun.Place(world, PerRole.All.ToDictionary(role => role, _ => new Vector2(0f, -12f)));
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
