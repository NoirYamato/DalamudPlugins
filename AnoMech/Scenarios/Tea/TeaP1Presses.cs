using System;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using FFXIVClientStructs.FFXIV.Client.Game;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Scenarios.Tea;

// The human's own presses a TEA P1 run reads, heard through IUserActions.Acted whether or not the player's actions
// are resolved in the sim. Construct one per run, as the run starts; a scenario has no hook for its run's end, so the
// next run disposes of the last one's.
public sealed class TeaP1Presses : IDisposable
{
    private readonly SimWorld world;
    private readonly IUserActions userActions;
    private float? antiKnockbackPressedAt;

    public TeaP1Presses(SimWorld world)
    {
        this.world = world;
        userActions = Natives.UserActions;
        userActions.Acted += Hear;
    }

    public void Dispose() => userActions.Acted -= Hear;

    // The sim shows neither status, so the press is timed on the run's clock.
    public bool AntiKnockbackUp(SimCharacter member)
    {
        if (!ReferenceEquals(member, world.Party.Player)) return false;
        return antiKnockbackPressedAt is { } at && world.Events.Elapsed - at < P1Duration.AntiKnockback;
    }

    private void Hear(PlayerAction action)
    {
        if (action is { Step: PlayerActionStep.Resolved, ActionType: ActionType.Action, ActionId: P1PlayerActionId.ArmsLength or P1PlayerActionId.Surecast })
            antiKnockbackPressedAt = world.Events.Elapsed;
    }
}
