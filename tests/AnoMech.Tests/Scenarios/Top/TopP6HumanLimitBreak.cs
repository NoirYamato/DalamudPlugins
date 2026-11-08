using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AnoMech.Tests;

// The human pressing a limit break as their client does: the cast bar's start (none for an instant one), then its
// resolve or interruption, each through IUserActions.Acted; and, as it resolves, what the client's own module does next:
// the action's JobActions effects (a tank's limit break 3 statuses) and the resolve that empties the HUD gauge. A dead
// player presses nothing, and a cast its player dies in is interrupted. While another seat's limit break casts, the
// client greys the player's out: a press then goes nowhere, and the test says so in its output.
internal static class TopP6HumanLimitBreak
{
    public static void Press(SimWorld world, float at, uint actionId, Func<SimCharacter?>? target = null, float? interruptedAfter = null)
        => world.Events.Add(at, () =>
        {
            if (!world.Party.Player.IsAlive()) return;
            if (AnotherSeatsLimitBreakCasts(world))
            {
                TestContext.Out.WriteLine($"The human's {actionId} at {world.Events.Elapsed:F2} went nowhere: another seat's limit break was casting.");
                return;
            }
            var userActions = (FakeUserActions)Natives.UserActions;
            var targetId = target?.Invoke() is { } aimedAt ? (ulong)aimedAt.GameObjectId : 0ul;
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

    private static bool AnotherSeatsLimitBreakCasts(SimWorld world)
        => world.Party.AllMembers().Any(member => member != world.Party.Player && member.Casting.IsCasting
                                                  && LimitBreakHandler.IsLimitBreak(member.Casting.ActionId));

    private static void Resolve(SimWorld world, FakeUserActions userActions, uint actionId, ulong targetId)
    {
        userActions.Act(new PlayerAction(PlayerActionStep.Resolved, ActionType.Action, actionId, targetId));
        if (world.Party.Player is not { } player) return;
        JobActions.ApplyEffects(player, actionId, targetId, new Random(1));
        LimitBreakHandler.Resolve(world.Party, player, actionId);
    }
}
