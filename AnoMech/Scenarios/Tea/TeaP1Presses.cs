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
    private float antiKnockbackUntil = float.NegativeInfinity;

    public TeaP1Presses(SimWorld world)
    {
        this.world = world;
        userActions = Natives.UserActions;
        userActions.Acted += Hear;
    }

    public void Dispose() => userActions.Acted -= Hear;

    // The sim shows none of the statuses, so each press is timed on the run's clock.
    public bool AntiKnockbackUp(SimCharacter member)
        => ReferenceEquals(member, world.Party.Player) && world.Events.Elapsed < antiKnockbackUntil;

    // A press comes with its button's own id, so a warrior's Inner Release can come as Berserk.
    private void Hear(PlayerAction action)
    {
        if (action is not { Step: PlayerActionStep.Resolved, ActionType: ActionType.Action }) return;
        float? holds = action.ActionId switch
        {
            P1PlayerActionId.ArmsLength or P1PlayerActionId.Surecast => P1Duration.AntiKnockback,
            P1PlayerActionId.InnerRelease or P1PlayerActionId.Berserk => P1Duration.InnerStrength,
            _ => null,
        };
        if (holds is { } seconds)
            antiKnockbackUntil = MathF.Max(antiKnockbackUntil, world.Events.Elapsed + seconds);
    }
}
