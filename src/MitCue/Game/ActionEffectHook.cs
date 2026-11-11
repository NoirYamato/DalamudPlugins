using System.Collections.Concurrent;
using System.Numerics;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using MitCue.Util;
using CsCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace MitCue.Game;

/// <summary>One ActionEffect copied out of the packet handler (one resolved action, all targets).</summary>
/// <param name="Clock">Monotonic clock when the effect was received.</param>
/// <param name="CasterId">Caster entity id.</param>
/// <param name="ActionId">Action sheet row id.</param>
/// <param name="Sequence">Server GlobalSequence (0 when unknown).</param>
/// <param name="HasCasterInfo">True when the caster object was available in the detour.</param>
/// <param name="CasterKind">Caster ObjectKind byte (1 = player, 2 = battle NPC), valid with <paramref name="HasCasterInfo"/>.</param>
/// <param name="CasterOwnerId">Caster OwnerId (pets: their owner), valid with <paramref name="HasCasterInfo"/>.</param>
/// <param name="Targets">Target entity ids.</param>
internal readonly record struct ObservedEffect(
    double Clock,
    uint CasterId,
    uint ActionId,
    uint Sequence,
    bool HasCasterInfo,
    byte CasterKind,
    uint CasterOwnerId,
    uint[] Targets);

/// <summary>
/// Read-only hook on <c>ActionEffectHandler.Receive</c> (docs/research/clientstructs.md §2.5): copies the
/// caster, action and targets of every resolved action into a queue drained on the framework thread.
/// The detour never throws, copies before calling the original (the pointers reference the packet
/// buffer) and always calls the original.
/// </summary>
internal sealed unsafe class ActionEffectHook : IDisposable
{
    /// <summary>ActionEffect header ActionType for Action sheet actions.</summary>
    private const byte ActionTypeAction = 1;

    /// <summary>Queue limit, in case the framework stops draining (e.g. while loading screens).</summary>
    private const int MaxPending = 4096;

    private readonly Hook<ActionEffectHandler.Delegates.Receive>? hook;
    private readonly MonotonicClock clock;
    private readonly ErrorReporter errors;
    private readonly ConcurrentQueue<ObservedEffect> pending = new();
    private volatile bool capture;
    private int pendingCount;
    private int detourFailures;
    private long observed;

    /// <summary>Creates and enables the hook (failures are reported, not thrown).</summary>
    public ActionEffectHook(IGameInteropProvider interop, MonotonicClock clock, ErrorReporter errors)
    {
        this.clock = clock;
        this.errors = errors;

        try
        {
            // Resolved by Dalamud's ClientStructs resolver at startup; 0 if the signature broke after a patch.
            nint address = ActionEffectHandler.Addresses.Receive.Value;
            if (address == 0)
            {
                this.Status = "unavailable (ActionEffectHandler.Receive did not resolve)";
                errors.Warn("hook.actioneffect", "Hooking ActionEffect", "ActionEffectHandler.Receive did not resolve; ability syncs and own-use detection from ActionEffects are off.");
                return;
            }

            this.hook = interop.HookFromAddress<ActionEffectHandler.Delegates.Receive>(address, this.Detour);
            this.hook.Enable();
            this.Status = $"enabled at 0x{address:X}";
        }
        catch (Exception ex)
        {
            this.hook = null;
            this.Status = "failed: " + ErrorReporter.Describe(ex);
            errors.Report("hook.actioneffect", "Hooking ActionEffect", ex);
        }
    }

    /// <summary>Human-readable hook state for the Debug tab.</summary>
    public string Status { get; private set; } = "not created";

    /// <summary>True while the hook is installed and enabled.</summary>
    public bool Available => this.hook is { IsEnabled: true, IsDisposed: false };

    /// <summary>When false the detour only forwards to the original (nothing to observe, e.g. in towns).</summary>
    public bool Capture
    {
        get => this.capture;
        set => this.capture = value;
    }

    /// <summary>Total effects queued since load.</summary>
    public long Observed => Interlocked.Read(ref this.observed);

    /// <summary>Takes the next queued effect (framework thread).</summary>
    public bool TryDequeue(out ObservedEffect effect)
    {
        if (!this.pending.TryDequeue(out effect))
            return false;
        Interlocked.Decrement(ref this.pendingCount);
        return true;
    }

    /// <summary>Drops everything queued.</summary>
    public void Clear()
    {
        while (this.TryDequeue(out _))
        {
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            this.hook?.Disable();
            this.hook?.Dispose();
        }
        catch (Exception ex)
        {
            this.errors.Report("hook.actioneffect.dispose", "Removing the ActionEffect hook", ex);
        }

        this.Status = "disposed";
        this.Clear();
    }

    private void Detour(
        uint casterEntityId,
        CsCharacter* casterPtr,
        Vector3* targetPos,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        if (this.capture)
        {
            try
            {
                // Copy what we need BEFORE calling the original: the pointers reference the packet buffer.
                if (header != null && header->ActionType == ActionTypeAction && Volatile.Read(ref this.pendingCount) < MaxPending)
                {
                    int count = header->NumTargets;
                    var targets = count == 0 || targetEntityIds == null ? Array.Empty<uint>() : new uint[count];
                    for (var i = 0; i < targets.Length; i++)
                        targets[i] = targetEntityIds[i].ObjectId; // == EntityId for normal entities

                    var hasCaster = casterPtr != null;
                    byte kind = hasCaster ? (byte)casterPtr->GameObject.ObjectKind : (byte)0;
                    uint owner = hasCaster ? casterPtr->GameObject.OwnerId : 0u;

                    this.pending.Enqueue(new ObservedEffect(
                        this.clock.Now,
                        casterEntityId,
                        header->ActionId,
                        header->GlobalSequence,
                        hasCaster,
                        kind,
                        owner,
                        targets));
                    Interlocked.Increment(ref this.pendingCount);
                    Interlocked.Increment(ref this.observed);
                }
            }
            catch (Exception ex)
            {
                if (Interlocked.Increment(ref this.detourFailures) == 1)
                    this.errors.Report("hook.actioneffect.detour", "Reading an ActionEffect", ex);
            }
        }

        // Never skip the original (called last, after the copy above).
        try
        {
            this.hook!.Original(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);
        }
        catch (Exception ex)
        {
            this.errors.Report("hook.actioneffect.original", "Forwarding an ActionEffect to the game", ex);
        }
    }
}
