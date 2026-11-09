using System.Numerics;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AnoMech.Tests;

// The human pressing a limit break as their client does: the cast bar's start (none for an instant one), then its
// resolve or interruption, each through IUserActions.Acted; and, as it resolves, what the client's own module does next:
// the action's JobActions effects (a tank's limit break 3 statuses) and the resolve that empties the HUD gauge. A dead
// player presses nothing, and a cast its player dies in is interrupted. While another seat's limit break casts, the
// client greys the player's out: a press then goes nowhere, and the test says so in its output. A ground-targeted one
// goes down at `groundPoint` (scenario-local), or without one on its target, as a <t> macro puts it.
internal static class TopP6HumanLimitBreak
{
    public static void Press(SimWorld world, float at, uint actionId, Func<SimCharacter?>? target = null, float? interruptedAfter = null,
        Vector3? groundPoint = null)
        => world.Events.Add(at, () =>
        {
            if (!world.Party.Player.IsAlive()) return;
            if (AnotherSeatsLimitBreakCasts(world))
            {
                TestContext.Out.WriteLine($"The human's {actionId} at {world.Events.Elapsed:F2} went nowhere: another seat's limit break was casting.");
                return;
            }
            var userActions = (FakeUserActions)Natives.UserActions;
            var aimedAt = target?.Invoke();
            var targetId = aimedAt is null ? 0ul : (ulong)aimedAt.GameObjectId;
            PlaceOnTheGround(world, actionId, groundPoint ?? aimedAt?.Position);
            var castSeconds = Natives.Data.Action(actionId)?.CastSeconds ?? 0f;
            if (castSeconds <= 0f)
            {
                Resolve(world, userActions, actionId, targetId);
                return;
            }
            userActions.Act(new PlayerAction(PlayerActionStep.CastStarted, ActionType.Action, actionId, targetId, castSeconds));
            if (interruptedAfter is { } cut && cut < castSeconds)
                world.Events.Add(cut, () => userActions.Act(new PlayerAction(PlayerActionStep.Interrupted, ActionType.Action, actionId, targetId)));
            else
                world.Events.Add(castSeconds, () =>
                {
                    if (world.Party.Player.IsAlive()) Resolve(world, userActions, actionId, targetId);
                    else userActions.Act(new PlayerAction(PlayerActionStep.Interrupted, ActionType.Action, actionId, targetId));
                });
        });

    // The client's own cast of a ground-targeted action records where it goes down; with no `point` it reports none.
    internal static void PlaceOnTheGround(SimWorld world, uint actionId, Vector3? point)
    {
        if (Natives.Data.Action(actionId)?.TargetArea != true) return;
        ((FakeBattleCharas)Natives.BattleCharas).Player.CastTargetLocation = point is { } p ? world.Coordinates.ToGlobal(p) : default;
    }

    private static bool AnotherSeatsLimitBreakCasts(SimWorld world)
        => world.Party.AllMembers().Any(member => member != world.Party.Player && member.Casting.IsCasting
                                                  && LimitBreakHandler.IsLimitBreak(member.Casting.ActionId));

    internal static void Resolve(SimWorld world, FakeUserActions userActions, uint actionId, ulong targetId)
    {
        userActions.Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, actionId, targetId));
        if (world.Party.Player is not { } player) return;
        JobActions.ApplyEffects(player, actionId, targetId, new Random(1));
        LimitBreakHandler.Resolve(world.Party, player, actionId);
    }
}

// The human's client pressing a limit break between the sim's frames, where the game hears it (Natives.Tick runs before
// Game.Tick), rather than from an event. Called once a frame after the run's own tick, given the run time, it starts the
// cast on the first frame `startsOn` accepts and resolves it on the first after that `resolvesOn` accepts, by default
// once the cast has run. The run reads a cast's start as the frame before it, so the client may report its resolve up to
// a frame before the run's LandsAt.
internal sealed class TopP6HumanLimitBreakBetweenFrames(uint actionId, Func<float, bool> startsOn, Func<float, bool>? resolvesOn = null)
{
    private bool resolved;

    // The run time its cast started, as the run hears it.
    public float? StartedAt { get; private set; }

    public float CastSeconds => Natives.Data.Action(actionId)?.CastSeconds ?? 0f;

    public void Hear(SimWorld world, SimCharacter? target)
    {
        var now = world.Events.Elapsed;
        var userActions = (FakeUserActions)Natives.UserActions;
        var targetId = target is null ? 0ul : (ulong)target.GameObjectId;
        if (StartedAt is null && startsOn(now))
        {
            StartedAt = now;
            TopP6HumanLimitBreak.PlaceOnTheGround(world, actionId, target?.Position);
            userActions.Act(new PlayerAction(PlayerActionStep.CastStarted, ActionType.Action, actionId, targetId, CastSeconds));
        }
        else if (StartedAt is { } at && !resolved && (resolvesOn?.Invoke(now) ?? now >= at + CastSeconds))
        {
            resolved = true;
            TopP6HumanLimitBreak.Resolve(world, userActions, actionId, targetId);
        }
    }
}
