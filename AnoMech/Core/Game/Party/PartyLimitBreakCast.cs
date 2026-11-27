using System;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AnoMech.Core.Game.Party;

// A limit break the sim presses for a party seat, shown as the server shows a player's: the cast ActorControl to the
// party and the cast bar as it starts; then, as it resolves, the ActionEffect at its target and the resolve ActorControl,
// which empties the gauge. Unlike SimParty.UseLimitBreak it doesn't wait for "Require tank mitigation". With "Resolve
// your own actions" on, JobActions.ApplyEffects sends the resolve and plays the action's effects (a tank's limit break 3
// statuses); with it off ApplyEffects plays nothing, so the resolve goes out alone and the effects are the caller's.
// The local player's seat is sent nothing and takes nothing, its effects being the caller's too: its client plays its
// own actions, an effect it sees itself cast would end its cast bar mid-cast, and ApplyEffects can't play the effects
// without sending that client the resolve.
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
        if (user is SimPlayer) return;
        if (target != user) user.Face(target.Position);
        user.Casting.NativeActionEffect(actionId, AnimationLock, (ushort)actionId, 0, ActionType.Action, 0,
            position: target.Position, animationTargetId: target.GameObjectId, targets: [new ActionEffectTarget(target.GameObjectId)]);
        if (Natives.UserActions.Enabled) JobActions.ApplyEffects(user, actionId, (ulong)target.GameObjectId, Random.Shared);
        else LimitBreakHandler.Resolve(party, user, actionId);
    }

    public static void Interrupt(SimCharacter user)
    {
        if (user is not SimPlayer) user.Casting.Cancel(CastCancelReason.Interrupted);
    }
}
