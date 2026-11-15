using System;
using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using FFXIVClientStructs.FFXIV.Client.Game;
using static AnoMech.Scenarios.Tea.TeaP1Constants;

namespace AnoMech.Scenarios.Tea;

// One Esuna or Warden's Paean that reached `Target`: the human's own, or one the run made for a seat. `ActionId` says
// which of the two it was.
public sealed record TeaP1Cleanse(SimCharacter By, SimCharacter Target, float At, bool ByHuman, uint ActionId);

// One Provoke, aimed at whatever carries `TargetId`.
public sealed record TeaP1Provoke(SimCharacter By, ulong TargetId, float At, bool ByHuman);

// One weaponskill or spell aimed at an enemy, the one that carries `TargetId`.
public sealed record TeaP1Strike(SimCharacter By, ulong TargetId, float At, bool ByHuman);

// The human's own presses a TEA P1 run reads, heard through IUserActions.Acted whether or not the player's actions
// are resolved in the sim. Construct one per run, as the run starts; a scenario has no hook for its run's end, so the
// next run disposes of the last one's. Acted is raised for every scenario's runs, so one that outlives its run to hear
// a press of another's, told by the world's Rng, which each run replaces, lets go by itself.
//
// It only reports a press; what the press does is its listener's to say. A tank's invulnerability is the one thing it
// applies itself, there and then: its status is what every rule reads, and the sim's own resolving of the player's
// actions, when that is on, adds it from the same call.
public sealed class TeaP1Presses : IDisposable
{
    // What the client reports a press aimed at when it went to "the current target". UNVERIFIED that one arrives so.
    private const uint CurrentTarget = 0xE0000000;

    private enum Kind { Cleanse, Provoke, Strike }

    private readonly SimWorld world;
    private readonly Rng itsRuns;
    private readonly IUserActions userActions;
    private readonly List<(Kind Kind, ulong TargetId, float At, uint ActionId)> heard = [];
    private readonly List<TeaP1Cleanse> cleanses = [];
    private readonly List<TeaP1Provoke> provokes = [];
    private float antiKnockbackUntil = float.NegativeInfinity;
    private bool disposed;

    public TeaP1Presses(SimWorld world)
    {
        this.world = world;
        itsRuns = world.Rng;
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

    public IReadOnlyList<TeaP1Provoke> Provokes => provokes;

    public event Action<TeaP1Cleanse>? Cleansed;
    public event Action<TeaP1Provoke>? Provoked;
    public event Action<TeaP1Strike>? Struck;

    public void Tick() => CatchUp();

    // Acted is raised from the client's input, outside the run's tick, so a press heard there settles here: on the
    // next tick, or sooner for a rule that decides or stops listening now. Each carries the time it was heard.
    internal void CatchUp()
    {
        if (heard.Count == 0) return;
        var pending = heard.ToList();
        heard.Clear();
        foreach (var (kind, targetId, at, actionId) in pending)
        {
            if (world.Party.Player is not { } player || !player.IsAlive()) continue;
            var member = world.Party.AllMembers().FirstOrDefault(member => (ulong)member.GameObjectId == targetId);
            switch (kind)
            {
                // UNVERIFIED: the client turns a cleanse aimed at an enemy, or at nothing, onto its caster.
                case Kind.Cleanse:
                    Settle(new TeaP1Cleanse(player, member ?? player, at, ByHuman: true, actionId));
                    break;
                case Kind.Provoke when member is null && targetId != 0:
                    Settle(new TeaP1Provoke(player, targetId, at, ByHuman: true));
                    break;
                case Kind.Strike when member is null && targetId != 0:
                    Settle(new TeaP1Strike(player, targetId, at, ByHuman: true));
                    break;
            }
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

    // Whether `by` stands within Provoke's reach of `boss`, past their two hitboxes: the client holds a human's press
    // to it.
    public bool ProvokeReaches(SimCharacter by, SimEnemy boss)
        => TeaP1LivingLiquid.Apart(by.Position, boss.Position) <= P1Rules.ProvokeReach + by.HitboxRadius + boss.HitboxRadius;

    // The run's own Provoke, invulnerability and hit on a Jagd Doll, for a seat nobody presses for. A Provoke or a
    // hit settles after whatever the human pressed before it, and a Provoke out of reach is none: false.
    public bool Provoke(SimCharacter by, SimEnemy boss)
    {
        if (disposed || !by.IsAlive() || !boss.IsActive || !ProvokeReaches(by, boss)) return false;
        CatchUp();
        (by as SimNpc)?.PlayAction(P1PlayerActionId.Provoke);
        Settle(new TeaP1Provoke(by, (ulong)boss.GameObjectId, world.Events.Elapsed, ByHuman: false));
        return true;
    }

    // Its job's own: a bot's, or for the player's seat the job a bot would have in it.
    public void Invulnerability(SimCharacter by)
    {
        if (disposed || !by.IsAlive()) return;
        var job = by switch
        {
            SimPartyNpc bot => bot.ClassJob,
            ISimPartyMember { Role: var seat } => PartyPresets.Standard[(int)seat].ClassJob,
            _ => (byte)0,
        };
        var (actionId, statusId) = (JobId)job switch
        {
            JobId.Paladin => (P1PlayerActionId.HallowedGround, P1StatusId.HallowedGround),
            JobId.DarkKnight => (P1PlayerActionId.LivingDead, P1StatusId.LivingDead),
            JobId.Gunbreaker => (P1PlayerActionId.Superbolide, P1StatusId.Superbolide),
            _ => (P1PlayerActionId.Holmgang, P1StatusId.Holmgang),
        };
        (by as SimNpc)?.PlayAction(actionId);
        by.AddStatus(statusId, P1Duration.TankInvulnerability);
    }

    public void Strike(SimCharacter by, SimEnemy doll)
    {
        if (disposed || !by.IsAlive() || !doll.IsActive) return;
        CatchUp();
        Settle(new TeaP1Strike(by, (ulong)doll.GameObjectId, world.Events.Elapsed, ByHuman: false));
    }

    private void Settle(TeaP1Cleanse cleanse)
    {
        if (disposed || !cleanse.Target.IsAlive()) return;
        cleanses.Add(cleanse);
        Cleansed?.Invoke(cleanse);
    }

    private void Settle(TeaP1Provoke provoke)
    {
        if (disposed) return;
        provokes.Add(provoke);
        Provoked?.Invoke(provoke);
    }

    private void Settle(TeaP1Strike strike)
    {
        if (disposed) return;
        Struck?.Invoke(strike);
    }

    // A press comes with its button's own id, so a warrior's Inner Release can come as Berserk. Only a healer's
    // client can report Esuna, only a bard's the Warden's Paean and only a tank's Provoke or its invulnerability, so
    // neither the job nor the seat is asked. A press comes aimed at the client's target whatever the button is, so a
    // cooldown pressed with an enemy targeted is told from a hit on it by what its action can be aimed at.
    private void Hear(PlayerAction action)
    {
        if (!ReferenceEquals(world.Rng, itsRuns))
        {
            Dispose();
            return;
        }
        if (action is not { Step: PlayerActionStep.Resolved, ActionType: ActionType.Action }) return;
        var targetId = (uint)action.TargetId == CurrentTarget ? ClientsTarget() : action.TargetId;
        switch (action.ActionId)
        {
            case P1PlayerActionId.ArmsLength or P1PlayerActionId.Surecast:
                Cover(P1Duration.AntiKnockback);
                break;
            case P1PlayerActionId.InnerRelease or P1PlayerActionId.Berserk:
                Cover(P1Duration.InnerStrength);
                break;
            case P1PlayerActionId.Esuna or P1PlayerActionId.WardensPaean:
                heard.Add((Kind.Cleanse, targetId, world.Events.Elapsed, action.ActionId));
                break;
            case P1PlayerActionId.HallowedGround:
                TakeNoDamage(P1StatusId.HallowedGround);
                break;
            case P1PlayerActionId.Holmgang:
                TakeNoDamage(P1StatusId.Holmgang);
                break;
            case P1PlayerActionId.LivingDead:
                TakeNoDamage(P1StatusId.LivingDead);
                break;
            case P1PlayerActionId.Superbolide:
                TakeNoDamage(P1StatusId.Superbolide);
                break;
            case P1PlayerActionId.Provoke:
                heard.Add((Kind.Provoke, targetId, world.Events.Elapsed, action.ActionId));
                break;
        }
        if (IsAimedAtOneTarget(action.ActionId)) heard.Add((Kind.Strike, targetId, world.Events.Elapsed, action.ActionId));
    }

    // Aimed at one target that is not its user; CatchUp counts it as a hit when that target is no party member. Some
    // such buttons can land on a party member or on an enemy (a monk's Thunderclap), and the sheet's columns here do
    // not say which can land on an enemy. UNVERIFIED: that the client refuses one that cannot before it is heard,
    // that every weaponskill or spell aimed at an enemy counts as a hit on it, and that one which only clips it, laid
    // on the ground or round its user, does not. The fight's enemies also count an auto-attack; none is heard here.
    //
    // A button that turns into another action comes with its own id, and its own row is aimed at its user (Ninjutsu
    // for Raiton, Iaijutsu for Higanbana): those are let through by id, so one that became something laid round its
    // user, pressed with a doll targeted, counts as a hit too.
    private static bool IsAimedAtOneTarget(uint actionId)
        => P1PlayerActionId.BecomeAHitOnTheTarget.Contains(actionId) || Natives.Data.Action(actionId) is { CanTargetSelf: false, TargetArea: false };

    private void Cover(float seconds) => antiKnockbackUntil = MathF.Max(antiKnockbackUntil, world.Events.Elapsed + seconds);

    private void TakeNoDamage(ushort statusId)
    {
        if (world.Party.Player is { } player && player.IsAlive()) player.AddStatus(statusId, P1Duration.TankInvulnerability);
    }

    private static ulong ClientsTarget() => Plugin.TargetManager?.Target?.GameObjectId ?? 0ul;
}
