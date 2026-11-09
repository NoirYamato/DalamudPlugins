using System;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AnoMech.Core.Game.Party;

// A limit break the sim presses for a party seat, shown as the server shows a player's: the cast ActorControl to the
// party and the cast bar as it starts; then, as it resolves, the ActionEffect at its target, the action's JobActions
// effects (a tank's limit break 3 statuses) and the resolve ActorControl, which empties the gauge. Unlike
// SimParty.UseLimitBreak it doesn't wait for "Require tank mitigation". The local player's seat only takes the
// effects: its client plays its own actions, and an effect it sees itself cast would end its cast bar mid-cast.
public static class PartyLimitBreakCast
{
    private const float AnimationLock = 0.6f;

    // A ground-targeted limit break goes down where `target` stands; any other is aimed at it.
    public static void Start(SimParty party, SimCharacter user, uint actionId, SimCharacter target, float castSeconds)
    {
        if (user is SimPlayer) return;
        foreach (var member in party.AllMembers()) member.ActorControl.LimitBreakCast(actionId, member == user);
        var ground = Natives.Data.Action(actionId)?.TargetArea == true;
        user.Casting.NativeCast(actionId, ActionType.Action, 0f, castSeconds, interruptible: false,
            rotation: target == user ? null : user.Placement().Face(target.Position).Rotation,
            position: ground ? target.Position : null,
            targetId: ground ? null : target.GameObjectId);
    }

    public static void Resolve(SimParty party, SimCharacter user, uint actionId, SimCharacter target)
    {
        if (user is not SimPlayer)
        {
            if (target != user) user.Face(target.Position);
            user.Casting.NativeActionEffect(actionId, AnimationLock, (ushort)actionId, 0, ActionType.Action, 0,
                position: target.Position, animationTargetId: target.GameObjectId, targets: [new ActionEffectTarget(target.GameObjectId)]);
        }
        JobActions.ApplyEffects(user, actionId, (ulong)target.GameObjectId, Random.Shared);
        if (user is not SimPlayer) LimitBreakHandler.Resolve(party, user, actionId);
    }

    public static void Interrupt(SimCharacter user)
    {
        if (user is not SimPlayer) user.Casting.Cancel(CastCancelReason.Interrupted);
    }
}
