using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.DutyState;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using MitCue.Core.Events;
using MitCue.Util;

namespace MitCue.Game;

/// <summary>
/// Produces <see cref="GameEvent"/>s for the sync engine, stamped with the plugin's monotonic clock, and
/// drained once per <c>IFramework.Update</c> (main thread):
/// <list type="bullet">
/// <item><see cref="CombatStateChanged"/> on InCombat condition edges (sampled each frame);</item>
/// <item><see cref="CastStarted"/> from polling hostile battle NPCs' cast bars (once per cast, time-corrected);</item>
/// <item><see cref="AbilityResolved"/> from the ActionEffect hook (names and kinds resolved here, not in the detour);</item>
/// <item><see cref="DutyReset"/> from IDutyState (wipe/recommence/completed) and territory changes.</item>
/// </list>
/// Also reads the /countdown agent and splits out the local player's (and their pets') action uses.
/// Game objects are never kept across frames; only entity ids are stored.
/// </summary>
internal sealed unsafe class GameEventSource : IDisposable
{
    /// <summary>IObjectTable character-manager range [0, 199]: players, battle NPCs, pets.</summary>
    private const int CharacterObjectSlots = 200;

    private const byte ActionTypeAction = 1;
    private const uint InvalidEntityId = 0xE0000000;

    private readonly IObjectTable objects;
    private readonly ICondition condition;
    private readonly IDutyState dutyState;
    private readonly IClientState clientState;
    private readonly MonotonicClock clock;
    private readonly ErrorReporter errors;
    private readonly ConcurrentQueue<(DutyResetKind Kind, double Clock)> resets = new();
    private readonly Dictionary<uint, CastTrack> casts = [];
    private readonly List<uint> castRemovals = [];
    private readonly Dictionary<uint, string> npcNames = [];
    private readonly uint[] recentSequences = new uint[64];
    private int sequenceCursor;
    private int frame;
    private bool combatInitialized;
    private volatile bool territoryChanged;

    /// <summary>Subscribes to duty/territory events and installs the ActionEffect hook.</summary>
    public GameEventSource(
        IGameInteropProvider interop,
        IObjectTable objects,
        ICondition condition,
        IDutyState dutyState,
        IClientState clientState,
        MonotonicClock clock,
        ErrorReporter errors)
    {
        this.objects = objects;
        this.condition = condition;
        this.dutyState = dutyState;
        this.clientState = clientState;
        this.clock = clock;
        this.errors = errors;

        this.Hook = new ActionEffectHook(interop, clock, errors);

        // These may be raised from packet-handler detours: only enqueue here, convert on the framework thread.
        dutyState.DutyWiped += this.OnDutyWiped;
        dutyState.DutyRecommenced += this.OnDutyRecommenced;
        dutyState.DutyCompleted += this.OnDutyCompleted;
        clientState.TerritoryChanged += this.OnTerritoryChanged;
    }

    /// <summary>The ActionEffect hook.</summary>
    public ActionEffectHook Hook { get; }

    /// <summary>This frame's events in time order (cleared by <see cref="Poll"/>).</summary>
    public List<GameEvent> FrameEvents { get; } = new(64);

    /// <summary>This frame's own action uses from ActionEffects (local player and their pets).</summary>
    public List<OwnUse> FrameOwnUses { get; } = new(8);

    /// <summary>This frame's action uses by other players (and their pets).</summary>
    public List<OwnUse> FramePartyUses { get; } = new(16);

    /// <summary>Recent events for the Debug tab (other players' actions are left out).</summary>
    public RingBuffer<GameEvent> RecentEvents { get; } = new(50);

    /// <summary>Remaining seconds of an active /countdown, or null.</summary>
    public float? Countdown { get; private set; }

    /// <summary>The InCombat condition as last sampled.</summary>
    public bool InCombat { get; private set; }

    /// <summary>Whether to poll cast bars (only needed with a session, a preview or a recording).</summary>
    public bool PollCasts { get; set; }

    /// <summary>Whether the hook should queue ActionEffects.</summary>
    public bool Capture
    {
        get => this.Hook.Capture;
        set => this.Hook.Capture = value;
    }

    /// <summary>Cast starts emitted since load.</summary>
    public long CastsSeen { get; private set; }

    /// <summary>Samples game state and drains queues into <see cref="FrameEvents"/>. Framework thread.</summary>
    /// <param name="now">Current monotonic clock.</param>
    /// <param name="localEntityId">Local player's entity id (0 when unknown).</param>
    public void Poll(double now, uint localEntityId)
    {
        this.FrameEvents.Clear();
        this.FrameOwnUses.Clear();
        this.FramePartyUses.Clear();
        this.frame++;

        if (this.territoryChanged)
        {
            this.territoryChanged = false;
            this.casts.Clear();
            this.npcNames.Clear();
            Array.Clear(this.recentSequences);
        }

        // Each step is guarded separately so one failing source does not starve the others.
        try
        {
            this.PollCombat(now);
        }
        catch (Exception ex)
        {
            this.errors.Report("events.combat", "Reading the combat state", ex);
        }

        try
        {
            this.DrainResets();
        }
        catch (Exception ex)
        {
            this.errors.Report("events.duty", "Reading duty resets", ex);
        }

        try
        {
            this.Countdown = ReadCountdown();
        }
        catch (Exception ex)
        {
            this.Countdown = null;
            this.errors.Report("events.countdown", "Reading the countdown", ex);
        }

        if (this.PollCasts)
        {
            try
            {
                this.PollCastBars(now);
            }
            catch (Exception ex)
            {
                this.errors.Report("events.casts", "Reading enemy cast bars", ex);
            }
        }
        else if (this.casts.Count > 0)
        {
            this.casts.Clear();
        }

        try
        {
            this.DrainEffects(localEntityId);
        }
        catch (Exception ex)
        {
            this.errors.Report("events.effects", "Reading ActionEffects", ex);
        }

        SortByTime(this.FrameEvents);
        foreach (var e in this.FrameEvents)
        {
            if (e is not AbilityResolved { SourceIsPlayer: true, SourceIsLocalPlayer: false })
                this.RecentEvents.Add(e);
        }
    }

    /// <summary>Queues a manual reset (from <c>/mitcue reset</c>).</summary>
    public void QueueReset(DutyResetKind kind) => this.resets.Enqueue((kind, this.clock.Now));

    /// <inheritdoc/>
    public void Dispose()
    {
        this.dutyState.DutyWiped -= this.OnDutyWiped;
        this.dutyState.DutyRecommenced -= this.OnDutyRecommenced;
        this.dutyState.DutyCompleted -= this.OnDutyCompleted;
        this.clientState.TerritoryChanged -= this.OnTerritoryChanged;
        this.Hook.Dispose();
    }

    private static bool IsPlayerId(uint entityId) => (entityId & 0xF0000000) == 0x10000000;

    /// <summary>Battle NPC kinds that are not player companions (pets, chocobos, trusts, minions).</summary>
    private static bool IsEnemyLike(BattleNpcSubKind kind) =>
        kind is not (BattleNpcSubKind.Pet or BattleNpcSubKind.Buddy or BattleNpcSubKind.NpcPartyMember
            or BattleNpcSubKind.RaceChocobo or BattleNpcSubKind.LovmMinion);

    private static float? ReadCountdown()
    {
        // BossMod's approach (clientstructs.md §4): poll the agent; ignore TimeRemaining while inactive.
        AgentCountDownSettingDialog* agent = AgentCountDownSettingDialog.Instance();
        return agent != null && agent->Active ? agent->TimeRemaining : null;
    }

    /// <summary>Stable insertion sort by <see cref="GameEvent.Time"/> (a handful of events per frame).</summary>
    private static void SortByTime(List<GameEvent> list)
    {
        for (var i = 1; i < list.Count; i++)
        {
            var item = list[i];
            var j = i - 1;
            while (j >= 0 && list[j].Time > item.Time)
            {
                list[j + 1] = list[j];
                j--;
            }

            list[j + 1] = item;
        }
    }

    private void PollCombat(double now)
    {
        var inCombat = this.condition[ConditionFlag.InCombat];
        if (!this.combatInitialized)
        {
            // Loaded mid-combat: take the state without inventing a combat start.
            this.combatInitialized = true;
            this.InCombat = inCombat;
            return;
        }

        if (inCombat != this.InCombat)
        {
            this.InCombat = inCombat;
            this.FrameEvents.Add(new CombatStateChanged(now, inCombat));
        }
    }

    private void DrainResets()
    {
        while (this.resets.TryDequeue(out var reset))
            this.FrameEvents.Add(new DutyReset(reset.Clock, reset.Kind));
    }

    private void PollCastBars(double now)
    {
        var length = Math.Min(CharacterObjectSlots, this.objects.Length);
        for (var i = 0; i < length; i++)
        {
            var obj = this.objects[i];
            if (obj == null || obj.ObjectKind != ObjectKind.BattleNpc || obj is not IBattleNpc npc)
                continue;
            if (!IsEnemyLike(npc.BattleNpcKind) || !npc.IsCasting || npc.CastActionType != ActionTypeAction)
                continue;

            var entityId = npc.EntityId;
            if (entityId == 0 || entityId == InvalidEntityId)
                continue;

            var actionId = npc.CastActionId;
            var start = now - npc.CurrentCastTime;
            ref var track = ref CollectionsMarshal.GetValueRefOrAddDefault(this.casts, entityId, out var exists);
            if (!exists || track.ActionId != actionId || Math.Abs(track.Start - start) > 0.5)
            {
                // New cast: emit once, with the time corrected back to the cast start.
                var name = npc.Name.TextValue;
                if (name.Length > 0)
                    this.npcNames[entityId] = name;
                this.FrameEvents.Add(new CastStarted(start, actionId, entityId, name, npc.TotalCastTime, (uint)npc.CastTargetObjectId));
                this.CastsSeen++;
                track.ActionId = actionId;
                track.Start = start;
            }

            track.LastFrame = this.frame;
        }

        // Forget casters that stopped casting so their next cast of the same action is seen as new.
        this.castRemovals.Clear();
        foreach (var (id, track) in this.casts)
        {
            if (track.LastFrame != this.frame)
                this.castRemovals.Add(id);
        }

        foreach (var id in this.castRemovals)
            this.casts.Remove(id);
    }

    private void DrainEffects(uint localEntityId)
    {
        while (this.Hook.TryDequeue(out var fx))
        {
            if (fx.Sequence != 0 && this.SeenSequence(fx.Sequence))
                continue;

            var isPc = fx.HasCasterInfo ? fx.CasterKind == (byte)ObjectKind.Pc : IsPlayerId(fx.CasterId);
            var owner = fx.HasCasterInfo ? fx.CasterOwnerId : 0u;
            var isPlayerPet = !isPc && owner != 0 && owner != InvalidEntityId && IsPlayerId(owner);
            var isLocal = localEntityId != 0 && fx.CasterId == localEntityId;
            var isLocalPet = isPlayerPet && localEntityId != 0 && owner == localEntityId;
            var sourceIsPlayer = isPc || isPlayerPet;

            var name = sourceIsPlayer ? string.Empty : this.NpcName(fx.CasterId);
            this.FrameEvents.Add(new AbilityResolved(fx.Clock, fx.ActionId, fx.CasterId, name, sourceIsPlayer, fx.Targets, isLocal));

            if (isLocal)
                this.FrameOwnUses.Add(new OwnUse(fx.ActionId, fx.Clock, OwnUseSource.ActionEffect));
            else if (isLocalPet)
                this.FrameOwnUses.Add(new OwnUse(PetActions.CommandFor(fx.ActionId) ?? fx.ActionId, fx.Clock, OwnUseSource.Pet));
            else if (sourceIsPlayer)
                this.FramePartyUses.Add(new OwnUse(isPlayerPet ? PetActions.CommandFor(fx.ActionId) ?? fx.ActionId : fx.ActionId, fx.Clock, OwnUseSource.Party));
        }
    }

    private bool SeenSequence(uint sequence)
    {
        foreach (var s in this.recentSequences)
        {
            if (s == sequence)
                return true;
        }

        this.recentSequences[this.sequenceCursor] = sequence;
        this.sequenceCursor = (this.sequenceCursor + 1) % this.recentSequences.Length;
        return false;
    }

    private string NpcName(uint entityId)
    {
        if (this.npcNames.TryGetValue(entityId, out var cached))
            return cached;
        var name = this.objects.SearchByEntityId(entityId)?.Name.TextValue ?? string.Empty;
        if (name.Length > 0)
            this.npcNames[entityId] = name;
        return name;
    }

    private void OnDutyWiped(IDutyStateEventArgs args) => this.resets.Enqueue((DutyResetKind.Wipe, this.clock.Now));

    private void OnDutyRecommenced(IDutyStateEventArgs args) => this.resets.Enqueue((DutyResetKind.Recommence, this.clock.Now));

    private void OnDutyCompleted(IDutyStateEventArgs args) => this.resets.Enqueue((DutyResetKind.Completed, this.clock.Now));

    private void OnTerritoryChanged(uint territory)
    {
        this.resets.Enqueue((DutyResetKind.LeftZone, this.clock.Now));
        this.territoryChanged = true;
    }

    private struct CastTrack
    {
        public uint ActionId;
        public double Start;
        public int LastFrame;
    }
}
