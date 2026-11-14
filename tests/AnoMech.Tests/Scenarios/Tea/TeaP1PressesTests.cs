using System.Numerics;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Tea;
using FFXIVClientStructs.FFXIV.Client.Game;
using static AnoMech.Core.Game.Party.PartyRole;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Tests;

// The human's own Arm's Length, Surecast or Inner Release, as the client reports the press. The player sits in the
// main tank's seat.
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

    [Test]
    public void AWarriorsInnerReleaseCoversItForFifteenSeconds()
    {
        var covered = Covered(new[] { 0.9f, 1.1f, 15.9f, 16.1f }, (world, _) =>
            world.Events.Add(1f, () => Press(P1PlayerActionId.InnerRelease)));

        Assert.That(covered, Is.EqualTo(new[] { false, true, true, false }));
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
