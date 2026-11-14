using System;
using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using FFXIVClientStructs.FFXIV.Client.Game;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Scenarios.Tea;

// One Esuna or Warden's Paean that reached `Target`: the human's own, or one the run made for a seat. `ActionId` says
// which of the two it was.
public sealed record TeaP1Cleanse(SimCharacter By, SimCharacter Target, float At, bool ByHuman, uint ActionId);

// The human's own presses a TEA P1 run reads, heard through IUserActions.Acted whether or not the player's actions
// are resolved in the sim. Construct one per run, as the run starts; a scenario has no hook for its run's end, so the
// next run disposes of the last one's.
//
// It only reports a cleanse; what the cleanse takes off is its listener's to say.
public sealed class TeaP1Presses : IDisposable
{
    // What the client reports a press aimed at when it went to "the current target". UNVERIFIED that one arrives so.
    private const uint CurrentTarget = 0xE0000000;

    private readonly SimWorld world;
    private readonly IUserActions userActions;
    private readonly List<(ulong TargetId, float At, uint ActionId)> heard = [];
    private readonly List<TeaP1Cleanse> cleanses = [];
    private float antiKnockbackUntil = float.NegativeInfinity;
    private bool disposed;

    public TeaP1Presses(SimWorld world)
    {
        this.world = world;
        userActions = Natives.UserActions;
        userActions.Acted += Hear;
    }

    public void Dispose()
    {
        userActions.Acted -= Hear;
        disposed = true;
        heard.Clear();
    }

    // The sim shows none of the statuses, so each press is timed on the run's clock.
    public bool AntiKnockbackUp(SimCharacter member)
        => ReferenceEquals(member, world.Party.Player) && world.Events.Elapsed < antiKnockbackUntil;

    // Every cleanse so far, the human's and the run's, in the order they settled.
    public IReadOnlyList<TeaP1Cleanse> Cleanses => cleanses;

    public event Action<TeaP1Cleanse>? Cleansed;

    public void Tick() => CatchUp();

    // Acted is raised from the client's input, outside the run's tick, so a cleanse heard there settles here: on the
    // next tick, or sooner for a rule that decides or stops listening now.
    internal void CatchUp()
    {
        if (heard.Count == 0) return;
        var pending = heard.ToList();
        heard.Clear();
        foreach (var (targetId, at, actionId) in pending)
        {
            if (world.Party.Player is not { } player || !player.IsAlive()) continue;
            // UNVERIFIED: the client turns a cleanse aimed at an enemy, or at nothing, onto its caster.
            var target = world.Party.AllMembers().FirstOrDefault(member => (ulong)member.GameObjectId == targetId) ?? player;
            Settle(new TeaP1Cleanse(player, target, at, ByHuman: true, actionId));
        }
    }

    // A cleanse the run makes for a seat nobody presses for. Never played on the player: an effect the client sees
    // itself cast breaks its own cast state.
    public void Cleanse(SimCharacter by, SimCharacter target)
    {
        if (disposed) return;
        (by as SimNpc)?.PlayAction(P1PlayerActionId.Esuna);
        var cast = Natives.Data.Action(P1PlayerActionId.Esuna)?.CastSeconds ?? 0f;
        if (cast <= 0f) Settle(new TeaP1Cleanse(by, target, world.Events.Elapsed, ByHuman: false, P1PlayerActionId.Esuna));
        else world.Events.Add(cast, () => Settle(new TeaP1Cleanse(by, target, world.Events.Elapsed, ByHuman: false, P1PlayerActionId.Esuna)));
    }

    private void Settle(TeaP1Cleanse cleanse)
    {
        if (disposed || !cleanse.Target.IsAlive()) return;
        cleanses.Add(cleanse);
        Cleansed?.Invoke(cleanse);
    }

    // A press comes with its button's own id, so a warrior's Inner Release can come as Berserk. Only a healer's
    // client can report Esuna and only a bard's the Warden's Paean, so neither the job nor the seat is asked.
    private void Hear(PlayerAction action)
    {
        if (action is not { Step: PlayerActionStep.Resolved, ActionType: ActionType.Action }) return;
        switch (action.ActionId)
        {
            case P1PlayerActionId.ArmsLength or P1PlayerActionId.Surecast:
                Cover(P1Duration.AntiKnockback);
                break;
            case P1PlayerActionId.InnerRelease or P1PlayerActionId.Berserk:
                Cover(P1Duration.InnerStrength);
                break;
            case P1PlayerActionId.Esuna or P1PlayerActionId.WardensPaean:
                heard.Add(((uint)action.TargetId == CurrentTarget ? ClientsTarget() : action.TargetId, world.Events.Elapsed, action.ActionId));
                break;
        }
    }

    private void Cover(float seconds) => antiKnockbackUntil = MathF.Max(antiKnockbackUntil, world.Events.Elapsed + seconds);

    private static ulong ClientsTarget() => Plugin.TargetManager?.Target?.GameObjectId ?? 0ul;
}
