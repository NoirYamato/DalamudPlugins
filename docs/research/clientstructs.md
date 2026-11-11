# FFXIVClientStructs reference for MitCue (Dalamud 15.0.3.9)

Reference for the people writing MitCue. It covers the FFXIVClientStructs ("CS") APIs MitCue needs, at the
exact CS version bundled with the installed Dalamud.

How to read it:

- **VERIFIED** means the item was checked three ways: in the CS source at the bundled commit, in the bundled
  `FFXIVClientStructs.dll` (decompiled), and by compiling a plugin that uses it.
- **UNVERIFIED** means runtime behaviour that needs the running game. The game is not available in this container,
  so these items were not tested. Each is marked where it appears and collected in section 8.
- Code fences tagged `csharp` are complete files. All of them were extracted from this document and compiled as
  written (0 errors, 0 warnings, see section 0). Fences tagged `cs` are declarations quoted from the CS source and
  are not meant to be compiled.

---

## 0. Provenance and how this was verified

| Item | Value |
|---|---|
| Installed Dalamud | `15.0.3.9-31-g12e318c19` = goatcorp/Dalamud commit `12e318c1936e449e5883faa61214eb2ced478349` ("Merge pull request #3052 from KazWolfe/patch_7.58", 2026-11-10), revision 7696. Read from `~/.xlcore/dalamud/Hooks/dev/{TEMP_gitver,commit_hash,revision}.txt`. |
| Bundled CS commit | `lib/FFXIVClientStructs` submodule = **`80cb4a7b7e11a6702e2eb8839afd301a918c638e`** ("Update Format", 2026-11-10). This is the same pointer at the installed commit and at Dalamud `master` HEAD `43b34118` (#3050 csupdate-master). |
| CS source | aers/FFXIVClientStructs checked out at `80cb4a7b` in `scratchpad/research/repos/clientstructs` |
| Bundled DLL | `~/.xlcore/dalamud/Hooks/dev/FFXIVClientStructs.dll` (14,271,560 bytes, 2026-11-10 21:28), decompiled with ilspycmd 11.2 into `scratchpad/decomp/*.cs`. The generated members (`Addresses`, `Delegates`, `MemberFunctionPointers`, `Instance()`, span accessors) match the source. |
| XML docs | `FFXIVClientStructs.xml` lists only members that have doc comments. For example, `ActionManager.GetRecastTime` is missing from the XML but present in the DLL. Use it to cross-check, not as a complete list. Members spot-checked against it, all matching: `ActionEffectHandler.Receive` and the Header bit properties; `ActionManager.GetCurrentCharges`, `GetAdjustedRecastTime`, `GetRecastTimeForGroup`, `GetAdditionalRecastGroup`, `IsActionHighlighted`; `RaptureHotbarModule.GetSlotById`; `AddonActionBarBase.PulseActionBarSlot`; `ContentDirector.ApplyMapEffect` and `ProcessCommonDirectorUpdate`. |
| Compile probe | `scratchpad/probes/clientstructs` (`CsProbe.csproj`, `Sdk="Dalamud.NET.Sdk/15.0.0"`). `dotnet build -c Release --no-incremental` gives **0 warnings, 0 errors**. |
| Negative control | `scratchpad/probes/clientstructs-neg/Negative.cs` makes 8 deliberately wrong calls and fails with exactly the 8 expected errors (CS1503, CS0176 ×2, CS0120 ×2, CS1061, CS0019, CS1503). This shows the build really type-checks against the bundled assemblies. |
| Snippets in this file | Every `csharp` fence was extracted and compiled verbatim in `scratchpad/probes/clientstructs-doc` (result in section 9). |
| Cross-checked against | BossMod `6e410ef8` (BSD-3): `BossMod/Framework/ActionManagerEx.cs`, `WorldStateGameSync.cs`, `Util/Hook.cs`. ECommons `bc38dc71` (MIT): `ECommons/Hooks/ActionEffect.cs`, `ActionEffectTypes/EffectHeader.cs`, `MapEffect.cs`, `DirectorUpdate.cs`. Dalamud source at `12e318c19`: `Dalamud/Game/DutyState/DutyState.cs`, `Hooking/Internal/GameInteropProviderPluginScoped.cs`, `Dalamud.cs`. cactbot `docs/LogGuide.md`. |
| Game data | `research/gamedata/Action.csv`, plus `LogMessage.csv` and `Trait.csv` fetched from xivapi/ffxiv-datamining into `scratchpad/probes/clientstructs-data/` |

`scratchpad` = `/tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad`.

---

## 1. Ground rules for using CS in MitCue (VERIFIED unless noted)

1. **Project setup: nothing to add.** `Dalamud.NET.Sdk/15.0.0` (`Sdk.props`) already does the following:
   - adds `<Reference Include="FFXIVClientStructs" Private="false"/>` and `<Reference Include="InteropGenerator.Runtime" Private="false"/>`;
   - sets `AllowUnsafeBlocks=true`, `LangVersion=14.0` and `TargetFramework=net10.0-windows`.
2. **Addresses are already resolved when the plugin loads.** At startup Dalamud runs `SetupClientStructsResolver`
   (`Dalamud.cs`): `InteropGenerator.Runtime.Resolver.GetInstance.Setup(...)`, then
   `FFXIVClientStructs.Interop.Generated.Addresses.Register()`, then `Resolve()`. The results are cached per game
   version in `cs.json`. The plugin references CS with `Private=false`, so it uses Dalamud's loaded copy, and the
   addresses are set before the plugin constructor runs. No plugin-side initialisation is needed.
3. **What the source generator emits** (seen in the decompiled DLL). It differs by attribute:
   - For each `[MemberFunction]`:
     - `T.Addresses.Name` is an `InteropGenerator.Runtime.Address` (a sealed class). `.Value` is a **public `nint` field**
       holding the resolved address. `.String` holds the signature.
     - `T.Delegates.Name` is a managed delegate with the native signature. Instance methods get `T* thisPtr` as the
       first parameter. Use it as the type parameter of `Hook<T>`.
     - `T.MemberFunctionPointers.Name` is a `delegate* unmanaged<...>` property that reads `Addresses.Name.Value`.
     - The wrapper method (static or instance) null-checks the address and calls through the pointer.
   - For each `[StaticAddress]` (for example `Instance()`): `T.Addresses.Name` plus `T.StaticAddressPointers.pName`
     (`ppName` when `isPointer: true`, e.g. `Framework.StaticAddressPointers.ppInstance`). There is no `Delegates` or
     `MemberFunctionPointers` entry.
   - For each `[VirtualFunction(n)]` (for example `Character.GetCastInfo`, `AddonActionBarBase.PulseActionBarSlot`):
     `T.Delegates.Name` plus a `delegate* unmanaged<...>` field `Name` in the generated `T.<T>VirtualTable` struct at
     offset `n * 8` (read it as `obj->VirtualTable->Name`). There is **no** `Addresses` or `MemberFunctionPointers`
     entry (using one is compile error CS0117), and the wrapper calls through the object's vtable, so it cannot
     fail because a signature broke.
4. **If a signature fails to resolve after a patch:**
   - `Addresses.X.Value == 0`;
   - every `[MemberFunction]` or `[StaticAddress]` wrapper call, including `ActionManager.Instance()`, throws
     `InvalidOperationException("Address for … is null. The resolver was either uninitialized or failed to resolve
     address with signature …")` from `InteropGenerator.Runtime.ThrowHelper.ThrowNullAddress`;
   - `ActionManager.StaticAddressPointers.pInstance` returns `null` instead of throwing.

   So check `.Value != 0` before hooking, and turn the feature off cleanly if it is 0. Hooking address 0 does not
   fail softly: `Hook<T>.FromAddress` passes the address to `HookManager.FollowJmp`, which throws
   `InvalidOperationException` ("Address was <= 0 …"), so an unchecked hook created in the plugin constructor fails
   the whole plugin load. The short skeletons in 2.5 (variant) and 6.5 leave the check out for brevity.
5. **Hooks** (Dalamud `IGameInteropProvider`, decompiled from `Dalamud.dll`):
   - `Hook<T> HookFromAddress<T>(nint procAddress, T detour, HookBackend backend = Automatic) where T : Delegate`.
     There are also `nuint` and `void*` overloads.
   - The hook starts disabled; call `.Enable()`.
   - `Hook<T>` members: `Original`, `OriginalDisposeSafe`, `Enable()`, `Disable()`, `IsEnabled`, `IsDisposed`,
     `Address`, `Dispose()`.
   - The plugin-scoped provider tracks every hook it creates and disposes leftovers when the plugin unloads
     (`GameInteropProviderPluginScoped.trackedHooks`). Still dispose your hooks in `Dispose()`.
   - Do not pass `backend`: the `Reloaded` and `MinHook` values are `[Obsolete]`.
   - Prefer CS addresses over `HookFromSignature`. Dalamud updates the CS signatures every patch; signatures you
     write yourself you have to maintain.
   - With Dalamud's Developer Mode on (`Dalamud.cs` calls `HookVerifier.Initialize()` only when
     `configuration.DevMode`), every hook is checked against CS (`Hook<T>.FromAddress` → `HookVerifier.TryVerify<T>`):
     if the address is a resolved CS function and your delegate's parameter count differs, or a parameter or return
     type differs in size from the CS signature, it logs "Hook verification failed - this may cause crashes and
     subtle bugs". It only logs; the hook is still created. A CS-generated `T.Delegates.Name` passes trivially
     (same type). Parameters and returns of identical type also pass without a size comparison (`CheckParam`
     returns true when both types are equal), so an unannotated `bool` in your own delegate, checked against a CS
     `bool`, is never flagged: the verifier cannot catch the marshalling risk described in the next bullet.
   - `FFXIVClientStructs.dll` is compiled with `[assembly: DisableRuntimeMarshalling]` (read from its assembly
     metadata); a plugin built with `Dalamud.NET.Sdk` is not. Dalamud's `HookVerifier` models `bool` as 1 byte in a
     delegate declared in such an assembly and as 4 bytes otherwise, and the generator adds
     `[return: MarshalAs(UnmanagedType.U1)]` to generated `bool` returns. If you ever declare your own delegate with
     a `bool`, mark it `[MarshalAs(UnmanagedType.U1)]` (the native `bool` is 1 byte), or better, use the generated
     delegate. How the runtime treats an unannotated `bool` in a plugin-declared delegate on Windows is
     **UNVERIFIED** (a Linux .NET 10 test in this container was inconclusive).
6. **Threading.**
   - Call CS functions and read game memory only on the framework thread. `IFramework.Update`
     (`void (IFramework framework)`) is the safe place.
   - Take a snapshot there and let the ImGui `UiBuilder.Draw` handler render the snapshot.
   - Packet-handler detours (ActionEffect, ActorControl) are treated as main-thread code by BossMod and by Dalamud's
     own `DutyState`, which do no locking. **UNVERIFIED here.** The skeleton below still uses a `ConcurrentQueue`,
     which costs almost nothing.
7. **Detours must never throw into native code.** Wrap the body in `try/catch` and always call `Original`.
8. **Reminder-only policy.** Never call `ActionManager.UseAction`, `ActionManager.UseActionLocation`,
   `RaptureHotbarModule.ExecuteSlot`, `RaptureHotbarModule.ExecuteSlotById` or
   `AddonActionBarBase.PulseActionBarSlot`. Everything else in this document only reads game state, apart from the
   optional hooks.
9. **Name clashes.** `FFXIVClientStructs.FFXIV.Client.Game.Character` is both a namespace and a struct
   (`…Character.Character`). Alias it: `using CsCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;`.

---

## 2. (a) `ActionEffectHandler`: resolved abilities

This one hook gives MitCue:

- cactbot `Ability` syncs (network lines 21/22);
- the player's own action uses;
- the co-healer's action uses.

### 2.1 The `Receive` function (VERIFIED: source, DLL, XML, compile)

```cs
// namespace FFXIVClientStructs.FFXIV.Client.Game.Character
[GenerateInterop]
[StructLayout(LayoutKind.Explicit, Size = 0xF00)]
public unsafe partial struct ActionEffectHandler {
    /// The main entry point, called when receiving ActionEffectN server packets.
    [MemberFunction("E8 ?? ?? ?? ?? 48 8B 8D ?? ?? ?? ?? 48 33 CC E8 ?? ?? ?? ?? 48 81 C4 00 05 00 00")]
    public static partial void Receive(
        uint casterEntityId,          // Caster entity id
        Character* casterPtr,         // FFXIVClientStructs.FFXIV.Client.Game.Character.Character*
        Vector3* targetPos,           // System.Numerics.Vector3*; "only relevant for area-targeted actions"
        Header* header,               // details of the action
        TargetEffects* effects,       // per-target effect list array, header->NumTargets elements
        GameObjectId* targetEntityIds // per-target id array, header->NumTargets elements
    );
}
```

- **Static**, so there is no `thisPtr`.
- The parameter order is exactly as shown. The XML doc id agrees:
  `M:…ActionEffectHandler.Receive(System.UInt32,…Character*,System.Numerics.Vector3*,…Header*,…TargetEffects*,…GameObjectId*)`.
- Generated members (decompiled):
  - `ActionEffectHandler.Addresses.Receive`: an `Address` whose signature is followed relatively at byte 1, so it
    resolves to the target of the `E8` call.
  - `ActionEffectHandler.Delegates.Receive`:
    `public unsafe delegate void Receive(uint casterEntityId, Character* casterPtr, Vector3* targetPos, Header* header, TargetEffects* effects, GameObjectId* targetEntityIds);`
    Every parameter is blittable, so delegate marshalling is trivial.
  - `ActionEffectHandler.MemberFunctionPointers.Receive`:
    `delegate* unmanaged<uint, Character*, Vector3*, Header*, TargetEffects*, GameObjectId*, void>`.
- **Address to hook:** `ActionEffectHandler.Addresses.Receive.Value` (`nint`).
  - This is what BossMod uses: `new HookAddress<ActionEffectHandler.Delegates.Receive>(ActionEffectHandler.Addresses.Receive, …)`,
    which calls `Service.Hook.HookFromAddress(address.Value, detour)` and then `Enable()`.
  - Equivalent, also compiles: `(nint)ActionEffectHandler.MemberFunctionPointers.Receive`.

### 2.2 `ActionEffectHandler.Header` (Size 0x28, `Header.StructSize = 40`): VERIFIED layout

| Off | Type | Field | Meaning (CS comments) |
|---|---|---|---|
| 0x00 | `GameObjectId` | `AnimationTargetId` | "primary" target; the animation plays on this object |
| 0x08 | `uint` | `ActionId` | action row id. Which sheet it refers to depends on `ActionType` (1 = Action sheet). |
| 0x0C | `uint` | `GlobalSequence` | unique id of this action instance; increases monotonically while the server runs |
| 0x10 | `float` | `AnimationLock` | caster's animation lock |
| 0x14 | `uint` | `BallistaEntityId` | Artillery actions only |
| 0x18 | `ushort` | `SourceSequence` | 0 unless the client started the action; otherwise the client's increasing sequence |
| 0x1A | `ushort` | `RotationInt` | quantised rotation: 0 = −π, 65535 = +π |
| 0x1C | `ushort` | `SpellId` | ECommons calls this `AnimationId` |
| 0x1E | `byte` | `AnimationVariation` | |
| 0x1F | `byte` | `ActionType` | **a byte, not the enum**: cast with `(ActionType)header->ActionType` (`ActionType` is in section 3.3) |
| 0x20 | `byte` | `Flags` | bit 0 = `ShowInLog`, bit 1 = `ForceAnimationLock` (generated `bool` properties) |
| 0x21 | `byte` | `NumTargets` | element count of `effects` and `targetEntityIds` |

ECommons declares its own `EffectHeader` with the same offsets (`ActionID@8`, `AnimationId@28`, `ActionTypeByte@31`,
`TargetCount@33`), which independently confirms this layout.

### 2.3 `TargetEffects` and `Effect`: VERIFIED layout

| Struct | Size | Fields |
|---|---|---|
| `ActionEffectHandler.TargetEffects` | 0x40 | `internal FixedSizeArray8<Effect> _effects`; public `Span<Effect> Effects` (`[UnscopedRef]`) |
| `ActionEffectHandler.Effect` | 0x08 | `byte Type`@0, `byte Param0`@1, `byte Param1`@2, `byte Param2`@3, `byte Param3`@4, `byte Param4`@5, `ushort Value`@6 |

`effects` points to an array of `NumTargets` `TargetEffects`. Iterate it like this:
`for (i < NumTargets) foreach (ref var e in effects[i].Effects)`.

CS has **no enum for `Effect.Type`**. BossMod's `ActionEffectType` (BSD-3, `BossMod/Data/ActionEffect.cs`) uses
these values. The table is a subset: BossMod also defines 9 FailMissingStatus, 12/13 TpLoss/TpGain, 25/26
EnmityAmountUp/Down, 34–36 AttractCustom1–3 and 39 Mount.

| Value | Name | Value | Name |
|---|---|---|---|
| 0 | Nothing | 17 | LoseStatusEffectTarget |
| 1 | Miss | 18 | LoseStatusEffectSource |
| 2 | FullResist | 20 | StatusNoEffect |
| 3 | Damage | 24 | ThreatPosition |
| 4 | Heal | 27 | StartActionCombo |
| 5 | BlockedDamage | 28 | Retaliation |
| 6 | ParriedDamage | 31 | Knockback |
| 7 | Invulnerable | 32/33 | Attract1/2 |
| 8 | NoEffectText | 50 | ReviveLB |
| 10/11 | MpLoss/MpGain | 55 | FullResistStatus |
| 14 | ApplyStatusEffectTarget | 59 | VFX |
| 15 | ApplyStatusEffectSource | 62 | Resource |
| 16 | RecoveredFromStatusEffect | 73/74/75/76 | SetModelState/SetHP/PartialInvulnerable/Interrupt |

What `Value` and the `Param` bytes mean for each type is **UNVERIFIED**. MitCue does not need them.

### 2.4 Related structs (VERIFIED)

`GameObjectId` (namespace `FFXIVClientStructs.FFXIV.Client.Game.Object`, Size 8):

- fields: `ulong Id`@0, `uint ObjectId`@0, `byte Type`@4;
- implicit conversions to and from `ulong`;
- per the CS comment: for an entity, `Type == 0` and `ObjectId == EntityId`. An object whose `EntityId` is
  `0xE0000000` has no server entity id; its `GameObjectId` is built from `BaseId` (`Type` 1), the object index
  (`Type` 2) or the owner (`Type` 4) instead, so `ObjectId` is not an entity id for those;
- as a target id, `0xE0000000` means "no target" (default `targetId` of `UseAction`/`GetActionStatus`; cactbot
  logs it as target `E0000000`).

Per-character pending effects (not needed by MitCue):

- `Character.GetActionEffectHandler()` is `[VirtualFunction(82)]` and returns `ActionEffectHandler*`.
- `ActionEffectHandler.IncomingEffects` is a `Span<EffectEntry>` of 32 entries.
- `EffectEntry` (0x78): `GlobalSequence`, `TargetIndex`, `ActionType`(byte), `ActionId`, `SpellId`,
  `Source`/`Target` (`GameObjectId`), `SourceConfirmed`/`TargetConfirmed`, `Effects`.
- BossMod uses this to see when an effect actually lands, which is confirmed by the separate EffectResult packet.

### 2.5 Hook skeleton (compiled verbatim)

```csharp
// MitCue: ActionEffectHandler.Receive hook (Dalamud 15.0.3.9 / FFXIVClientStructs 80cb4a7b).
using System;
using System.Collections.Concurrent;
using System.Numerics;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;            // ActionType (enum : uint)
using FFXIVClientStructs.FFXIV.Client.Game.Character;  // ActionEffectHandler (+ nested Header/TargetEffects/Effect)
using FFXIVClientStructs.FFXIV.Client.Game.Object;     // GameObjectId
using CsCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace MitCue.Interop;

/// <summary>One resolved action (one ActionEffect packet == one cactbot "Ability", for all its targets).</summary>
public sealed record ObservedActionEffect(
    uint CasterEntityId,
    uint ActionId,
    ActionType ActionType,
    ushort SpellId,
    uint GlobalSequence,
    ulong AnimationTargetId,
    uint[] TargetEntityIds);

public sealed unsafe class ActionEffectWatcher : IDisposable
{
    private readonly IPluginLog log;
    private readonly Hook<ActionEffectHandler.Delegates.Receive>? receiveHook;

    /// <summary>Filled by the detour; drain it in IFramework.Update.</summary>
    public ConcurrentQueue<ObservedActionEffect> Pending { get; } = new();

    public bool Available => this.receiveHook is { IsEnabled: true };

    public ActionEffectWatcher(IGameInteropProvider interop, IPluginLog log)
    {
        this.log = log;

        // Resolved by Dalamud at startup; 0 if the CS signature broke after a patch.
        nint address = ActionEffectHandler.Addresses.Receive.Value;
        if (address == 0)
        {
            log.Error("ActionEffectHandler.Receive did not resolve; ability sync disabled.");
            return;
        }

        this.receiveHook = interop.HookFromAddress<ActionEffectHandler.Delegates.Receive>(address, this.ReceiveDetour);
        this.receiveHook.Enable();
    }

    private void ReceiveDetour(
        uint casterEntityId,
        CsCharacter* casterPtr,
        Vector3* targetPos,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        // Copy what we need BEFORE calling the original: the pointers reference the packet buffer.
        try
        {
            var count = header->NumTargets;
            var targets = new uint[count];
            for (var i = 0; i < count; i++)
                targets[i] = targetEntityIds[i].ObjectId; // == EntityId for normal entities (Type 0)

            this.Pending.Enqueue(new ObservedActionEffect(
                casterEntityId,
                header->ActionId,
                (ActionType)header->ActionType, // Header.ActionType is a byte
                header->SpellId,
                header->GlobalSequence,
                header->AnimationTargetId,      // GameObjectId -> ulong (implicit)
                targets));

            // Per-target effects if ever needed (8 x 8-byte Effect per target):
            for (var i = 0; i < count; i++)
            {
                foreach (ref var e in effects[i].Effects)
                {
                    if (e.Type == 0) continue; // 0 = nothing
                    _ = e.Value;
                }
            }

            _ = targetPos != null ? *targetPos : default;               // only meaningful for ground-targeted actions
            _ = casterPtr != null ? casterPtr->GameObject.BaseId : 0u;  // BNpcBase row id of the caster (bosses)
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "ActionEffect detour failed");
        }

        // Never skip the original.
        this.receiveHook!.Original(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);
    }

    public void Dispose() => this.receiveHook?.Dispose();
}
```

Variant, also compiled: your own delegate type, with the address taken from `MemberFunctionPointers`. It does
the same thing. The generated delegate above is preferred because it changes automatically if CS changes the
signature.

```csharp
using System;
using System.Numerics;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using CsCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace MitCue.Interop;

public sealed unsafe class ActionEffectWatcherAlt : IDisposable
{
    public delegate void ReceiveDelegate(
        uint casterEntityId, CsCharacter* casterPtr, Vector3* targetPos,
        ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targetEntityIds);

    private readonly Hook<ReceiveDelegate> hook;

    public ActionEffectWatcherAlt(IGameInteropProvider interop)
    {
        nint address = (nint)ActionEffectHandler.MemberFunctionPointers.Receive; // delegate* unmanaged<...> -> nint
        this.hook = interop.HookFromAddress<ReceiveDelegate>(address, this.Detour);
        this.hook.Enable();
    }

    private void Detour(uint casterEntityId, CsCharacter* casterPtr, Vector3* targetPos,
        ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targetEntityIds)
        => this.hook.Original(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);

    public void Dispose() => this.hook.Dispose();
}
```

### 2.6 Semantics and pitfalls

- **Which actions trigger it.** It runs once for every incoming ActionEffectN packet whose caster exists on the
  client (see "Missing caster" below). BossMod's comment notes there are 5 packet functions behind this one entry
  point. Calls come from any caster the client knows about: bosses,
  invisible helper actors, party members, pets and the local player. An action that hits nobody should still be
  one call, with `NumTargets` 0. That is **UNVERIFIED**; cactbot logs such a use as a single line with target
  `E0000000`, `targetIndex` 0 and `targetCount` 1 (LogGuide example "Seed Of The Sky", which is a `21` line).
  Handle `NumTargets == 0` either way.
- **How it relates to cactbot lines.** One call is one action use, which cactbot logs as a group of 21/22 lines:
  - line 21 when exactly one target was hit;
  - line 22 when several targets were hit;
  - for a use that hits nobody, cactbot's docs disagree with themselves: the LogGuide prose says ground AoEs that
    hit nobody are `22` lines, but its "Seed Of The Sky" example is a `21` line. Do not depend on 21 vs 22;
    cactbot's `Ability` regex matches both (`2[12]`);
  - one line per target, with `targetIndex`/`targetCount` (cactbot `LogGuide.md`).

  So treat one call as one cactbot `Ability` event, and use `GlobalSequence` to de-duplicate.
- **Field correspondence** with the cactbot 21/22 field names. Names and offsets are confirmed from source:
  Machina's open-source ActionEffect header (`Machina.FFXIV/Headers/Global/Server_ActionEffect.cs`, GPL-3; Machina
  is the packet library of the ACT plugin that writes these lines) uses the cactbot names at the CS offsets:
  `actionId` 0x08, `globalEffectCounter` 0x0C, `animationLockTime` 0x10, `rotation` 0x1A, `actionAnimationId`
  0x1C, `effectDisplayType` 0x1F and `effectCount` 0x21. Its `effectDisplayType` enum (0 hide name, 1 action,
  2 item, 0x0D mount) has the same numbering as `ActionType`. The ACT plugin itself is closed source, so whether
  the logged values are exactly these fields is still **UNVERIFIED**:

  | cactbot 21/22 field | CS |
  |---|---|
  | `id` / `actionId` | `Header.ActionId` |
  | `actionAnimationId` | `Header.SpellId` |
  | `effectDisplayType` (01 in cactbot's examples) | `Header.ActionType` byte at 0x1F |
  | `sequence` ("sequential ID") | `Header.GlobalSequence` |
  | `animationLockTime` | `Header.AnimationLock` |
  | `rotationHex` | `Header.RotationInt` |
  | `targetIndex` / `targetCount` | `i` / `NumTargets` (not for a zero-target use: cactbot shows 0 / 1 with target `E0000000`) |
  | `ownerId` | the caster's `OwnerId` |
- **Missing caster.** It is **not called if the caster object does not exist on the client**. BossMod's comment
  says so and BossMod accepts it ("deemed to be minor"). Bosses and helper actors are expected to exist as
  objects, so boss syncs should work (**UNVERIFIED** in-game).
- **Action ids are already unscrambled** by the time this function runs (BossMod comment). Use `header->ActionId`,
  which is what cactbot timeline `Ability { id: … }` values refer to.
- **Identify the caster** with `casterEntityId`, compared against Dalamud `IGameObject.EntityId`, or with
  `IObjectTable.SearchByEntityId(uint)`. For bosses, `casterPtr->GameObject.BaseId` is the BNpcBase id. For pets,
  `GameObject.OwnerId` (or Dalamud `IGameObject.OwnerId`) maps a fairy, Seraph or bell back to its owner.
- **Pet actions arrive separately.** SCH fairy and Seraph actions come as their own ActionEffects with the pet as
  caster and pet action ids. Ids checked in `Action.csv`:

  | Player command (id) | Pet / bell action (id) |
  |---|---|
  | Whispering Dawn 16537 | Whispering Dawn 803; Angel's Whisper 16550 (Seraph) |
  | Fey Illumination 16538 | Fey Illumination 805; Seraphic Illumination 16551 (Seraph) |
  | Fey Blessing 16543 | Fey Blessing 16544 |
  | Consolation 16546 | Consolation 16547 |
  | Liturgy of the Bell 25862 (place); 28509 (manual detonate) | Liturgy of the Bell 25863 / 25864 (bell actions) |

  It is **UNVERIFIED** whether the player's command action always produces its own ActionEffect, and in what
  order relative to the pet's. Match a plan entry against both the player id and the pet id.
- **Items:** `ActionType == 2`, and `ActionId > 1_000_000` means HQ (`id - 1_000_000`), per ECommons
  `ActionEffectSet`. **Mounts:** `ActionType == 13`.
- **Copy data before calling `Original`** (BossMod reads before it as well). Keep the detour cheap; it runs for
  every action in the area.
- **Two independent implementations confirm the approach:**
  - BossMod (`ActionManagerEx.ProcessPacketActionEffectDetour`) hooks `ActionEffectHandler.Addresses.Receive` with
    `ActionEffectHandler.Delegates.Receive`.
  - ECommons (`ECommons/Hooks/ActionEffect.cs`) hooks a function of the same 6-parameter shape
    `(uint sourceId, Character* sourceCharacter, Vector3* pos, EffectHeader*, EffectEntry*, ulong* effectTail)`
    through its own prologue signature `40 55 53 56 57 41 54 41 55 41 56 41 57 48 8D AC 24 ?? ?? ?? ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 70 4C 8B BD`.
  - Whether ECommons' signature and the CS signature resolve to the same address is **UNVERIFIED**: there is no
    game binary here.
- **Dalamud 15.0.3.9 has no ActionEffect service.** `Dalamud.xml` contains 0 matches for "ActionEffect", so a
  hook is required.

---

## 3. (b) `ActionManager`: cooldowns, charges, status, replacements

Namespace `FFXIVClientStructs.FFXIV.Client.Game`. Struct `ActionManager`, Size 0x800.
**It only tracks the local player.** The co-healer's cooldowns are not available from the client and have to be
inferred from their ActionEffects plus recast data. See section 3.8.

### 3.1 Signatures (VERIFIED: source, decompiled DLL, compile, and the negative control for static vs instance)

| Member | Static? | Exact C# signature | CS signature |
|---|---|---|---|
| `Instance` | static | `ActionManager* Instance()` | `[StaticAddress("48 8D 0D ?? ?? ?? ?? F3 0F 10 13", 3)]`, with `isPointer` false in the DLL. The resolved address *is* the object (it lives in the binary's data), so the pointer is never null once resolved. Throws if unresolved. |
| `GetRecastTime` | instance | `float GetRecastTime(ActionType actionType, uint actionId)` | `E8 ?? ?? ?? ?? 83 7F 58 01` |
| `GetRecastTimeElapsed` | instance | `float GetRecastTimeElapsed(ActionType actionType, uint actionId)` | `E8 ?? ?? ?? ?? F3 0F 5C F0 33 F6` |
| `IsRecastTimerActive` | instance | `bool IsRecastTimerActive(ActionType actionType, uint actionId)` | `E8 ?? ?? ?? ?? 3C 01 74 1E FF C3` |
| `GetAdjustedRecastTime` | **static** | `int GetAdjustedRecastTime(ActionType actionType, uint actionId, bool applyClassMechanics = true)`: **milliseconds** | `E8 ?? ?? ?? ?? 85 C0 7E 1C` |
| `GetCurrentCharges` | instance | `uint GetCurrentCharges(uint actionId)`: "for actions that do not use charges, this method will cap at 1" | `48 89 6C 24 ?? 48 89 74 24 ?? 41 56 48 83 EC ?? 8B EA 48 89 5C 24` |
| `GetMaxCharges` | **static** | `ushort GetMaxCharges(uint actionId, uint level)`: source comment "0 for current level" | `E9 ?? ?? ?? ?? 41 B0 ?? 8B D3` |
| `GetActionStatus` | instance | `uint GetActionStatus(ActionType actionType, uint actionId, ulong targetId = 0xE000_0000, bool checkRecastActive = true, bool checkCastingActive = true, uint* outOptExtraInfo = null)` | `E8 ?? ?? ?? ?? 4C 8B 6C 24 ?? 85 C0 74` |
| `GetAdjustedActionId` | instance | `uint GetAdjustedActionId(uint actionId)` | `E8 ?? ?? ?? ?? 89 03 8B 03` |
| `GetRecastGroup` | instance | `int GetRecastGroup(int type, uint actionId)`: **the first parameter is `int`, not `ActionType`**. Pass `(int)ActionType.Action`. | `E8 ?? ?? ?? ?? 8B D0 48 8B CD 8B F0` |
| `GetAdditionalRecastGroup` | instance | `int GetAdditionalRecastGroup(ActionType actionType, uint actionId)`: "-1 if invalid" | `E8 ?? ?? ?? ?? 8B 4F 48 33 D2` |
| `GetRecastGroupDetail` | instance | `RecastDetail* GetRecastGroupDetail(int recastGroup)` | `40 53 48 83 EC 20 48 63 DA 85 D2` |
| `GetRecastTimeForGroup` | instance | `float GetRecastTimeForGroup(int recastGroupId)`: corrects multi-charge actions that are still limited to one charge at the current level | `E8 ?? ?? ?? ?? 0F 28 F8 F3 0F 5C FE` |
| `IsActionOffCooldown` | instance | `bool IsActionOffCooldown(ActionType actionType, uint actionId)`: accounts for the slidecast window | `E8 ?? ?? ?? ?? 3C ?? 75 ?? 88 45` |
| `GetSpellIdForAction` | **static** | `uint GetSpellIdForAction(ActionType actionType, uint actionId)` | `40 53 48 83 EC ?? FF C9` |
| `GetAdjustedCastTime` | **static** | `int GetAdjustedCastTime(ActionType actionType, uint actionId, bool applyProcs = true, ActionManager.CastTimeProc* outOptProc = null)`: milliseconds | `E8 ?? ?? ?? ?? 8B D0 48 8B CF E8 ?? ?? ?? ?? 0F B7 55` |
| `GetHotbarCooldownInfo` | instance | `void GetHotbarCooldownInfo(HotbarCooldownInfo* outCooldownInfo, ActionType actionType, uint actionId)` | `48 89 5C 24 ?? 55 56 57 41 55 41 57 48 83 EC 30 45 8B E8` |
| `IsActionHighlighted` | instance | `bool IsActionHighlighted(ActionType actionType, uint actionId)`: whether the hotbar "ants" are drawn | `E8 ?? ?? ?? ?? 88 47 ?? 80 BB` |

Useful fields (read-only for MitCue):

- `float AnimationLock`@0x08
- `uint CastSpellId`@0x24
- `ActionType CastActionType`@0x28
- `uint CastActionId`@0x2C
- `float CastTimeElapsed`@0x30
- `float CastTimeTotal`@0x34
- `GameObjectId CastTargetId`@0x38
- `ComboDetail Combo`@0x60 (`float Timer`, `uint Action`)
- `bool ActionQueued`@0x68
- `uint QueuedActionId`@0x70
- `Span<RecastDetail> Cooldowns` (80 entries starting at 0x188)

`HotbarCooldownInfo` (0x1A): `PrimaryElapsedTime`, `PrimaryTotalTime`, `AdditionalElapsedTime`,
`AdditionalTotalTime`, `CooldownDisplayType DisplayType`, `int CurrentMaxCharges`, `bool CooldownActive`, `bool IsGcd`.
This is a one-call alternative to reading each timer separately. Compiled in section 9.

### 3.2 `RecastDetail` (Size 0x14): VERIFIED

`bool IsActive`@0, `uint ActionId`@4 (the last action that updated this group), `float Elapsed`@8, `float Total`@0xC.

The CS docs describe the recast as a charge gauge:

- `Elapsed` grows by 1 per second up to `Total`.
- Using a normal action resets `Elapsed` to 0.
- Using a multi-charge action subtracts one charge's worth from `Elapsed`.
- `Total` is the adjusted recast multiplied by the number of charges at max level. CS recommends
  `GetRecastTime(...)` over reading `Total`, because it fixes the level-locked-charges edge case.

BossMod reads `RecastDetail` directly: `IsActive ? (Elapsed, Total) : (0, 0)`.

### 3.3 `ActionType` enum: VERIFIED (`FFXIVClientStructs.FFXIV.Client.Game.ActionType : uint`)

`None=0, Action=1, Item=2, EventItem=3, EventAction=4, GeneralAction=5, BuddyAction=6, MainCommand=7, Companion=8,
CraftAction=9, Unk10=10, PetAction=11, Unk12=12, Mount=13, PvPAction=14, FieldMarker=15, ChocoboRaceAbility=16,
ChocoboRaceItem=17, Unk18=18, BgcArmyAction=19, Ornament=20`.

`ActionEffectHandler.Header.ActionType` uses the same numbering, stored as a byte.

### 3.4 Recast groups

- `GetRecastGroup((int)ActionType.Action, id)` returns an index into `Cooldowns`.
- BossMod's data model (`ActionDefinitions`) uses **index = Action sheet `CooldownGroup` − 1**, and the
  additional group = `AdditionalCooldownGroup` − 1. GCD = group **57** (sheet value 58).
- `GetRecastGroupDetail(n)` (per the BossMod comments):
  - n = 0–79: entries in `ActionManager`;
  - n = 80–81: `DutyActionManager`;
  - n = 82–86: `MassivePcContentDirector`;
  - returns `null` when the owning manager does not exist.
- The "−1" mapping comes from BossMod and is **UNVERIFIED in-game**. At runtime, prefer calling
  `GetRecastGroup`/`GetAdditionalRecastGroup`.

Healer examples from the current `Action.csv` (CooldownGroup / AdditionalCooldownGroup / sheet MaxCharges / Recast100ms):

| Job | Action (id) | CooldownGroup | AdditionalCooldownGroup | MaxCharges (sheet) | Recast100ms |
|---|---|---|---|---|---|
| WHM | Divine Benison (7432) | 10 | 72 | 1 | 300 |
| WHM | Tetragrammaton (3570) | 20 | 73 | 1 | 600 |
| WHM | Thin Air (7430) | 19 | 71 | 2 | 600 |
| WHM | Temperance (16536) | 22 | – | – | 1200 |
| WHM | Liturgy of the Bell (25862) | 23 | – | – | 1800 |
| WHM | Aquaveil (25861) | 12 | – | – | 600 |
| WHM | Plenary Indulgence (7433) | 11 | – | – | 600 |
| WHM | Asylum (3569) | 15 | – | – | 900 |
| WHM | Benediction (140) | 24 | – | – | 1800 |
| WHM | Presence of Mind (136) | 21 | – | – | 1200 |
| SCH | Consolation (16546) | 11 | 71 | 2 | 300 |
| SCH | Summon Seraph (16545) | 23 | – | – | 1200 |
| SCH | Sacred Soil (188) | 8 | – | – | 300 |
| SCH | Expedient (25868) | 19 | – | – | 1200 |
| SCH | Fey Illumination (16538) | 22 | – | – | 1200 |
| SCH | Seraphism (37014) | 24 | – | – | 1800 |
| SCH | Recitation (16542) | 16 | – | – | 900 |
| SCH | Deployment Tactics (3585) | 20 | – | – | 1200 |
| SCH | Protraction (25867) | 13 | – | – | 600 |
| SCH | Dissipation (3587) | 25 | – | – | 1800 |
| SCH | Indomitability (3583) | 9 | – | – | 300 |
| SCH | Whispering Dawn (16537) | 15 | – | – | 600 |
| SCH | Fey Blessing (16543) | 12 | – | – | 600 |
| SCH | Excogitation (7434) | 10 | – | – | 450 |
| SCH | Aetherflow (166) | 14 | – | – | 600 |
| SCH | Chain Stratagem (7436) | 21 | – | – | 1200 |
| SCH | Emergency Tactics (3586) | 7 | – | – | 150 |
| Role | Swiftcast (7561) | 44 | – | – | 600 |
| Role | Lucid Dreaming (7562) | 45 | – | – | 600 |
| Role | Rescue (7571) | 50 | – | – | 1200 |

`Recast100ms` is the base value. Like `MaxCharges` (section 3.6), it does not include traits. Traits in the current
`Trait.csv` that shorten the recast of an action above (Value in 100 ms):

| Trait (row) | Job, level | Value | Effect at the ultimates |
|---|---|---|---|
| Enhanced Deployment Tactics (494) | SCH, 88 | −300 | Deployment Tactics 120 s → 90 s in TOP and DMU; 120 s in TEA |
| Enhanced Recitation (629) | SCH, 98 | −300 | Recitation 90 s → 60 s in DMU only |
| Enhanced Swiftcast (644) | ClassJobCategory 199 (includes WHM and SCH), 94 | −200 | Swiftcast 60 s → 40 s in DMU only |

The local player's runtime APIs (`GetAdjustedRecastTime`, `GetRecastTime`) should already include these
(**UNVERIFIED**). MitCue's own co-healer table (section 3.8) has to apply them by level.

### 3.5 `GetActionStatus` return value

- `0` means the action can be used now.
- Any other value is a **`LogMessage` sheet row id**. BossMod logs it as
  `LuminaRow<LogMessage>(status).Text`, and BossMod notes that pomanders always return 579.
- Rows from the current `LogMessage.csv`:

| Row | Text |
|---|---|
| 562 | "Target not in line of sight." |
| 565, 566 | "Target is not in range." |
| 572 | "Cannot use yet." |
| 573 | "Action not yet learned." |
| 574 | "Cannot use as current class." |
| 579 | "Cannot execute at this time." |
| 580 | "Cannot use while casting." |
| 581 | "Cannot use while under status affliction." |
| 582 | "Not yet ready." |
| 593 | "Cannot be used here." |

- The default `targetId = 0xE000_0000` means "no target". For targeted actions (for example Divine Benison) this
  may report a target error even though the cooldown is ready. **UNVERIFIED.**
- For "is it ready", use the recast and charge APIs. Use `GetActionStatus` only as a secondary hint.

### 3.6 Charges and level sync

- Call `ActionManager.GetMaxCharges(id, 0)` at runtime. **The sheet's `MaxCharges` column does not include
  charges added by traits.** Evidence from `Trait.csv`:
  - Divine Benison: sheet value 1. Trait 490 "Enhanced Divine Benison" (WHM, level 88, Value 2) adds a charge.
    Expected: **1 charge in TEA (L80), 2 in TOP (L90) and DMU (L100)**.
  - Tetragrammaton: sheet value 1. Trait 625 "Enhanced Tetragrammaton" (level 98, Value 2). Expected: 2 charges in
    DMU only.
  - The trait data is VERIFIED. The runtime return values are **UNVERIFIED**.
- `GetCurrentCharges(id)` is an instance method.
- Time until the next charge, derived from the CS gauge description and **UNVERIFIED at runtime**:
  - `perCharge = GetRecastTime(...) / GetMaxCharges(id, 0)`, or `GetAdjustedRecastTime(...) / 1000f`;
  - `next = perCharge - (GetRecastTimeElapsed(...) % perCharge)` while `cur < max`.
- `GetMaxCharges` is static and takes an action id and a level. Whether it gives correct results for **another
  job's** action (the co-healer's) is **UNVERIFIED**: trait checks may depend on the local player. For the
  co-healer, use MitCue's own data table (level → charges).

### 3.7 `GetAdjustedActionId`: replacements and upgrades

It is an instance method that returns what the button resolves to right now (upgrade, replacement or combo). Use it:

1. to show the name and icon that is really on the button;
2. to know which action will actually fire (but read cooldowns from the base id; see the caveat below the table);
3. when matching an ActionEffect `ActionId`: compare against both the base id and the adjusted id.

Expected mappings for MitCue. The ids are VERIFIED in `Action.csv`; the runtime mapping is **UNVERIFIED** and needs
an in-game check:

| Base action (id) | Expected result while… | Adjusted (id) |
|---|---|---|
| Summon Seraph (16545) | Seraph is out | Consolation (16546) |
| Liturgy of the Bell (25862) | the bell is placed | Liturgy of the Bell (28509, manual detonation; ClassJob 0, not a player action) |
| Temperance (16536) | L100, after use | Divine Caress (37011) |
| Adloquium (185) | L100, under Seraphism | Manifestation (37015) |
| Concitation (37013) | L100, under Seraphism | Accession (37016) |
| Aetherpact (7437) | tethered | Dissolve Union (7869) |
| Succor (186) | L96+ trait upgrade (trait 628 "Succor Mastery"; applies in DMU, not TEA or TOP) | Concitation (37013) |
| Medica II (133) | L96+ trait upgrade (trait 624 "Medica Mastery"; applies in DMU, not TEA or TOP) | Medica III (37010) |

**Caveat: read "is the planned action ready?" from the base id, not the adjusted id.** Four follow-ups in this
table have a recast group of their own in `Action.csv`: Divine Caress 37011 (CooldownGroup 2, 1 s), the bell
detonation 28509 (CooldownGroup 1, 1 s), Consolation 16546 (CooldownGroup 11, two charges) and Dissolve Union 7869
(CooldownGroup 2, 1 s; Aetherpact 7437 is group 6, 3 s). The first three matter: while one of them is
on the button, which is right after Temperance, Liturgy of the Bell or Summon Seraph was used, the adjusted id's
timers describe the follow-up (usually ready), not the base action, which is on its 120–180 s cooldown. So read
recast, charges and status for the planned (base) id, and use the adjusted id only for the name and icon and for
matching. BossMod's action definitions likewise take the recast group from each action's own sheet row
(`CooldownGroup − 1`); in `ActionManagerEx` it calls `GetAdjustedActionId` only on actions it is about to queue or
execute (`NormalizeActionForQueue`, the imminent-action recast), and its public `AdjustAction` wrapper is used by
`UIRotationWindow` only to pick the icon and name it displays. That `GetRecastTime(baseId)` reports the base
action's own group while the button shows the follow-up is **UNVERIFIED** in-game.

### 3.8 Local player only

`ActionManager` describes only the local player (Instance, `Cooldowns`, charges, `GetAdjustedActionId`).

- **H1 (WHM)** can read their own WHM cooldowns.
- **H2's (SCH) cooldowns are not readable on H1's client**, and the reverse is also true.
- If MitCue shows the co-healer's calls (optional per DESIGN D5), it has to track them from ActionEffects with
  `caster == co-healer EntityId` (and pets via `OwnerId`) plus recast and charge data.

### 3.9 Snippet (compiled verbatim)

```csharp
// MitCue: ActionManager cooldown / charge / status / adjustment reads.
using FFXIVClientStructs.FFXIV.Client.Game; // ActionManager, ActionType, RecastDetail

namespace MitCue.Interop;

public readonly record struct CooldownSnapshot(
    uint BaseActionId,
    uint AdjustedActionId,
    bool RecastActive,
    float RecastTotal,
    float RecastElapsed,
    float RecastRemaining,
    int AdjustedRecastMs,
    uint CurrentCharges,
    ushort MaxCharges,
    float NextChargeIn,
    uint ActionStatus,
    int RecastGroup,
    int AdditionalRecastGroup);

public static unsafe class CooldownReader
{
    /// <summary>GCD recast group index (sheet CooldownGroup 58 -> index 57, per BossMod ActionDefinitions.GCDGroup).</summary>
    public const int GcdRecastGroup = 57;

    /// <summary>True when the ActionManager singleton resolved (does not throw).</summary>
    public static bool Available => ActionManager.StaticAddressPointers.pInstance != null;

    // Call on the framework thread (IFramework.Update), never from a worker thread.
    public static CooldownSnapshot Read(uint actionId)
    {
        ActionManager* am = ActionManager.Instance(); // throws InvalidOperationException if unresolved

        // Upgrade/replacement resolution (instance method; Action-type ids). Use it for the name/icon and for
        // matching ActionEffects only: a follow-up such as Divine Caress has its own short recast group (section 3.7).
        uint adjusted = am->GetAdjustedActionId(actionId);

        // Readiness of the PLANNED action: everything below is read for actionId, not for adjusted.
        // Recast timers (instance). For multi-charge actions these describe the whole charge gauge.
        bool active = am->IsRecastTimerActive(ActionType.Action, actionId);
        float total = am->GetRecastTime(ActionType.Action, actionId);
        float elapsed = am->GetRecastTimeElapsed(ActionType.Action, actionId);
        float remaining = active ? System.MathF.Max(0f, total - elapsed) : 0f; // clamp: timer edge cases are UNVERIFIED

        // Static: per-use recast in milliseconds (trait/haste adjusted).
        int adjustedRecastMs = ActionManager.GetAdjustedRecastTime(ActionType.Action, actionId, true);

        // Charges: GetCurrentCharges is instance; GetMaxCharges is static (level 0 = current level).
        uint cur = am->GetCurrentCharges(actionId);
        ushort max = ActionManager.GetMaxCharges(actionId, 0);
        float nextChargeIn = 0f;
        if (max > 1 && cur < max && total > 0f)
        {
            float perCharge = total / max;
            nextChargeIn = perCharge - (elapsed % perCharge);
        }

        // 0 = usable now; otherwise a LogMessage row id (582 "Not yet ready.", 572 "Cannot use yet.", ...).
        uint status = am->GetActionStatus(ActionType.Action, actionId); // targetId defaults to 0xE000_0000

        // Recast groups: GetRecastGroup takes an *int* type, not ActionType.
        int group = am->GetRecastGroup((int)ActionType.Action, actionId);
        int extraGroup = am->GetAdditionalRecastGroup(ActionType.Action, actionId); // -1 if none
        RecastDetail* detail = am->GetRecastGroupDetail(group);                    // may be null
        if (detail != null && detail->IsActive)
        {
            _ = detail->ActionId; // last action that started this group
        }

        return new CooldownSnapshot(actionId, adjusted, active, total, elapsed, remaining, adjustedRecastMs,
            cur, max, nextChargeIn, status, group, extraGroup);
    }

    public static float GcdRemaining()
    {
        var am = ActionManager.Instance();
        var gcd = am->GetRecastGroupDetail(GcdRecastGroup);
        return gcd != null && gcd->IsActive ? gcd->Total - gcd->Elapsed : 0f;
    }

    /// <summary>Spell id lookup (static).</summary>
    public static uint SpellId(uint actionId) => ActionManager.GetSpellIdForAction(ActionType.Action, actionId);
}
```

---

## 4. (c) Countdown (`/countdown`)

### 4.1 Where the state lives (VERIFIED)

```cs
// namespace FFXIVClientStructs.FFXIV.Client.UI.Agent
// Client::UI::Agent::AgentCountDownSettingDialog : AgentInterface
[Agent(AgentId.CountDownSettingDialog)]          // AgentId.CountDownSettingDialog = 251
[StructLayout(LayoutKind.Explicit, Size = 0x48)]
public unsafe partial struct AgentCountDownSettingDialog {
    [FieldOffset(0x28)] public float TimeRemaining;
    [FieldOffset(0x2C)] public float PrevTimeRemaining;
    [FieldOffset(0x30)] public uint ConfirmAddonId;
    [FieldOffset(0x34)] public uint CountdownAddonId;
    [FieldOffset(0x38)] public bool Active;          // NOT "IsActive" (negative control: CS1061)
    [FieldOffset(0x3C)] public uint InitiatorId;
    [FieldOffset(0x40)] public bool ShowingCountdown;
}
```

- The `[Agent]` attribute generates `public static AgentCountDownSettingDialog* Instance()` (decompiled). It
  returns `(AgentCountDownSettingDialog*)AgentModule.Instance()->GetAgentByInternalId(AgentId.CountDownSettingDialog)`,
  or `null` when `AgentModule` is null.
- `AgentModule.Instance()` → `UIModule.Instance()` → `Framework.Instance()`. Each step returns `null` if its
  object does not exist yet.
  - `Framework.Instance()` is `[StaticAddress("48 8B 1D ?? ?? ?? ?? 8B 7C 24", 3, isPointer: true)]`. It throws
    only if that signature failed to resolve. Dalamud itself cannot run without it, so in practice it does not
    throw.
- This layout has not changed since 2024-06 (CS git log: `d8250499`, `ad4c8451`).
- No other countdown struct exists in CS. `ContentDirector.ContentTimeLeft` is the duty timer, not `/countdown`.
- Dalamud 15.0.3.9 has no countdown service.
- **BossMod reads the countdown exactly this way** (`WorldStateGameSync.UpdateClient`):
  `var a = AgentCountDownSettingDialog.Instance(); float? cd = a != null && a->Active ? a->TimeRemaining : null;`

### 4.2 Reading it safely

- Poll in `IFramework.Update`.
- Fetch the agent pointer every frame; it is cheap. Null-check it and read `Active` and `TimeRemaining`.
- No hooks and no writes.
- Ignore `TimeRemaining` while `Active == false`, as BossMod does (that it keeps its last value is likely but
  **UNVERIFIED**).
- `InitiatorId` is presumably the entity id of the player who started the countdown (the name suggests it).
  **UNVERIFIED.**
- Exactly when `Active` turns false relative to 0.0 s is **UNVERIFIED**. The tracker below uses a heuristic
  threshold.
- MitCue's actual pull time should still come from the `InCombat` condition edge or the first boss ability. The
  countdown only drives pre-pull reminders.
- Fallback signal if ever needed: the chat log messages LogMessage 5255/5260/5261/5262/5263 "Battle commencing
  in …", 5256/5264 "Engage!", 5257 "Countdown canceled by you." (the CSV export renders the player-name macro as
  "you"), and 2086 "…'s countdown completes.". Which of these rows the game prints for `/countdown` is
  **UNVERIFIED** (only the sheet text was checked).

```csharp
// MitCue: /countdown state.
using FFXIVClientStructs.FFXIV.Client.UI.Agent; // AgentCountDownSettingDialog, AgentModule, AgentId

namespace MitCue.Interop;

public readonly record struct CountdownState(bool Active, float TimeRemaining, uint InitiatorId);

public static unsafe class CountdownReader
{
    /// <summary>Poll once per frame from IFramework.Update (main thread). Null when the UI is not up.</summary>
    public static CountdownState? Read()
    {
        // Generated helper: AgentModule.Instance()->GetAgentByInternalId(AgentId.CountDownSettingDialog), null-safe.
        AgentCountDownSettingDialog* agent = AgentCountDownSettingDialog.Instance();
        if (agent == null)
            return null;

        return new CountdownState(agent->Active, agent->TimeRemaining, agent->InitiatorId);
    }

    /// <summary>Equivalent explicit lookup via AgentModule.</summary>
    public static AgentCountDownSettingDialog* ViaAgentModule()
    {
        var module = AgentModule.Instance();
        return module == null ? null : (AgentCountDownSettingDialog*)module->GetAgentByInternalId(AgentId.CountDownSettingDialog);
    }
}

/// <summary>Edge detector over CountdownReader; call Update() once per framework tick.</summary>
public sealed class CountdownTracker
{
    private bool wasActive;
    private float lastRemaining;

    public event System.Action<float>? Started;   // arg: initial seconds
    public event System.Action? ReachedZero;      // countdown ran out (heuristic, see doc)
    public event System.Action? Cancelled;        // stopped early

    public void Update()
    {
        var s = CountdownReader.Read();
        bool active = s is { Active: true };
        float remaining = s?.TimeRemaining ?? 0f;

        if (active && !this.wasActive)
            this.Started?.Invoke(remaining);
        if (!active && this.wasActive)
        {
            if (this.lastRemaining <= 0.5f) this.ReachedZero?.Invoke(); // threshold is a heuristic (UNVERIFIED)
            else this.Cancelled?.Invoke();
        }

        this.wasActive = active;
        this.lastRemaining = remaining;
    }
}
```

---

## 5. (d) Sound

### 5.1 API (VERIFIED)

```cs
// namespace FFXIVClientStructs.FFXIV.Client.UI
public unsafe partial struct UIGlobals {
    [MemberFunction("E8 ?? ?? ?? ?? 45 0F B7 C5")]
    public static partial void PlaySoundEffect(uint effectId, SoundData** outSoundDataPad = null, SoundData** outSoundData = null, byte a4 = 0);

    public static void PlayChatSoundEffect(uint effectId) {
        if (effectId is < 1 or > 16)
            throw new ArgumentException("Valid chat sfx values are 1 through 16.");
        PlaySoundEffect(effectId + 36);
    }
}
```

`SoundData` is `FFXIVClientStructs.FFXIV.Client.Sound.SoundData`. Generated members:
`UIGlobals.Delegates.PlaySoundEffect(uint, SoundData**, SoundData**, byte)` and
`UIGlobals.MemberFunctionPointers.PlaySoundEffect`. The decompiled DLL body of `PlayChatSoundEffect` is
`PlaySoundEffect(effectId + 36, null, null, 0)`.

### 5.2 `<se.N>` ids (VERIFIED from CS code)

**Chat `<se.N>` = `PlaySoundEffect(N + 36)`**:

| `<se.N>` | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 13 | 14 | 15 | 16 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| effectId | 37 | 38 | 39 | 40 | 41 | 42 | 43 | 44 | 45 | 46 | 47 | 48 | 49 | 50 | 51 | 52 |

Dalamud itself uses this mapping:

- Its SeString creator plays the `<se>` macro (Fixed, subtype 5, value 0-based) with
  `UIGlobals.PlayChatSoundEffect(u32 + 1)`.
- `Window` uses `UIGlobals.PlaySoundEffect(OnOpenSfxId = 23 / OnCloseSfxId = 24)`. MitCue's overlay window should
  set `DisableWindowSounds = true` (an `IWindow` property) so opening and closing it is silent.

**UNVERIFIED:**

- which volume bus these sounds use (System Sounds vs Sound Effects) and whether they respect the mute settings;
- whether they play while the game window is inactive. Per the CS source, this follows the user's sound
  setting: `SoundManager.PlaySoundsWhenWindowInactive`@0x24A is commented "inverted config option IsSoundAlways"
  (despite its name), and `SoundManager`'s effective-volume function returns 0 for a bus when `WindowInactive` is
  set and that flag (or the bus's own entry in the array at 0x1F5) is set. The field's existence says nothing
  about the default.

Call these only from the framework thread.

```csharp
// MitCue: game sound effects.
using FFXIVClientStructs.FFXIV.Client.UI; // UIGlobals

namespace MitCue.Interop;

public static unsafe class SoundPlayer
{
    /// <summary>Plays chat sound &lt;se.N&gt; (N = 1..16). Framework thread only.</summary>
    public static void PlayChatSe(int n)
    {
        if (n is < 1 or > 16)
            return; // UIGlobals.PlayChatSoundEffect throws ArgumentException outside 1..16
        UIGlobals.PlayChatSoundEffect((uint)n); // == UIGlobals.PlaySoundEffect((uint)n + 36)
    }

    /// <summary>Raw UI sound effect id (37..52 are &lt;se.1&gt;..&lt;se.16&gt;).</summary>
    public static void PlayRaw(uint effectId)
        => UIGlobals.PlaySoundEffect(effectId); // optional: SoundData** outSoundDataPad, SoundData** outSoundData, byte a4
}
```

---

## 6. (e) Notes: hotbar highlighting, wipe detection, MapEffect, casts

### 6.1 Hotbar data (VERIFIED names and types; v1 has no hotbar glow per DESIGN)

**`RaptureHotbarModule`** (namespace `FFXIVClientStructs.FFXIV.Client.UI.Misc`)

- `static RaptureHotbarModule* Instance()` is a hand-written helper: `UIModule.Instance()->GetRaptureHotbarModule()`, null-safe.
- `bool ModuleReady`@0x58 and `byte ActiveHotbarClassJobId`@0x59.
- `Span<Hotbar> Hotbars` has 18 entries: 0–9 are standard bars, 10–17 cross bars. `StandardHotbars` and
  `CrossHotbars` are sub-span helpers. `PetHotbar` and `PetCrossHotbar` also exist.
- `Hotbar` has `Span<HotbarSlot> Slots` (16) and `HotbarSlot* GetHotbarSlot(uint id)`.
- `HotbarSlot* GetSlotById(uint hotbarId /*0..17*/, uint slotId /*0..15*/)` returns `ScratchSlot` when out of bounds.

**`RaptureHotbarModule.HotbarSlot`** (0xE8)

- `uint CommandId`@0xB8: what the slot executes.
- `uint OriginalApparentActionId`@0xBC.
- `uint ApparentActionId`@0xC0: the icon or display action, which follows replacements.
- `HotbarSlotType CommandType`@0xC7, `OriginalApparentSlotType`@0xC8, `ApparentSlotType`@0xC9.
- `uint IconId`@0xD0.
- `bool IsEmpty` (true when `CommandId == 0`).
- `bool IsActionHighlighted(HotbarSlotType, uint)`.

**`RaptureHotbarModule.HotbarSlotType`** (byte): `Empty=0, Action=1, Item=2, …, Macro=7, …, GeneralAction=10, …,
PetAction=16, …`. Not the same numbering as `ActionType`.

**`AddonActionBarBase`** (namespace `FFXIVClientStructs.FFXIV.Client.UI`; addons `_ActionBar`,
`_ActionBar01`–`_ActionBar09`, `_ActionCross`, …)

- `StdVector<ActionBarSlot> ActionBarSlotVector`@0x238.
- `short CurrentPulsingSlots`@0x250.
- `byte RaptureHotbarId`@0x254: which `Hotbars` index the addon shows.
- `byte SlotCount`@0x256 and `bool IsCrossHotbar`@0x25A.
- `[VirtualFunction(81)] void PulseActionBarSlot(int slotIndex)` (a UI write; do not use).

**`ActionBarSlot`** (0xC8)

- `AtkComponentDragDrop* ComponentDragDrop`, `AtkImageNode* ChargeIcon`, `AtkResNode* RecastOverlayContainer`,
  `AtkResNode* IconFrame`.
- `int HotbarId`@0x30.
- `int ActionId`@0x34 ("not cleared when slot is emptied").
- `AtkComponentNode* Icon`@0xB8.

**Two ways to highlight later:**

1. **ImGui overlay.** Find the slot (snippet below) and draw a rectangle at the icon node's `ScreenX`/`ScreenY`
   with `Width`/`Height` × addon `Scale`. Read-only and safe. The exact scale handling is **UNVERIFIED**.
2. **The game's own "ants".** Hook `ActionManager.IsActionHighlighted`
   (`ActionManager.Delegates.IsActionHighlighted(ActionManager* thisPtr, ActionType actionType, uint actionId)`,
   `[return: MarshalAs(UnmanagedType.U1)]`) and return `true` for the planned action. It compiles (section 9).
   Whether hotbars pick this up every frame is **UNVERIFIED**.

```csharp
// MitCue (notes): locating hotbar slots and their screen rectangles. Read-only.
using System;
using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;      // AddonActionBarBase
using FFXIVClientStructs.FFXIV.Client.UI.Misc; // RaptureHotbarModule

namespace MitCue.Interop;

public static unsafe class HotbarLocator
{
    /// <summary>Find (hotbar, slot) pairs whose slot executes or displays the given action id.</summary>
    public static int FindSlots(uint actionId, Span<(int Bar, int Slot)> output)
    {
        var hbm = RaptureHotbarModule.Instance(); // UIModule.Instance()->GetRaptureHotbarModule(), null-safe
        if (hbm == null || !hbm->ModuleReady)
            return 0;

        var n = 0;
        for (var bar = 0; bar < hbm->Hotbars.Length; bar++) // 18 = 10 standard + 8 cross
        {
            ref var hotbar = ref hbm->Hotbars[bar];
            for (var slot = 0; slot < hotbar.Slots.Length; slot++) // 16
            {
                ref var s = ref hotbar.Slots[slot];
                if (s.CommandType == RaptureHotbarModule.HotbarSlotType.Action
                    && (s.CommandId == actionId || s.ApparentActionId == actionId))
                {
                    if (n < output.Length) output[n] = (bar, slot);
                    n++;
                }
            }
        }

        return n;
    }

    /// <param name="addonName">"_ActionBar" (bar 1), "_ActionBar01".."_ActionBar09", "_ActionCross", ...</param>
    public static bool TryGetSlotRect(IGameGui gameGui, string addonName, int slotIndex, out Vector2 min, out Vector2 max)
    {
        min = max = default;
        AddonActionBarBase* bar = gameGui.GetAddonByName<AddonActionBarBase>(addonName); // T* overload
        if (bar == null || !bar->IsVisible || slotIndex < 0 || slotIndex >= bar->ActionBarSlotVector.Count)
            return false;
        ref var slot = ref bar->ActionBarSlotVector[slotIndex];
        if (slot.Icon == null) return false;
        var node = slot.Icon; // AtkComponentNode* (inherits AtkResNode fields)
        float w = node->Width * bar->Scale, h = node->Height * bar->Scale; // approximate (UNVERIFIED)
        min = new Vector2(node->ScreenX, node->ScreenY);
        max = new Vector2(node->ScreenX + w, node->ScreenY + h);
        return true;
    }
}
```

Dalamud's `IGameGui.GetAddonByName(string name, int index = 1)` returns `AtkUnitBasePtr` (use `.Address`). The
generic `T* GetAddonByName<T>(string name, int index = 1) where T : unmanaged` overload also exists. Both VERIFIED
by decompile.

### 6.2 Wipe, commence and victory: use Dalamud `IDutyState`; CS has the underlying handler

**CS exposes it:** `PacketDispatcher.HandleActorControlPacket` (namespace `FFXIVClientStructs.FFXIV.Client.Network`),
VERIFIED. It is static, with generated `Addresses`, `Delegates` and `MemberFunctionPointers`:

```cs
[MemberFunction("40 55 53 57 41 54 41 56 48 8D AC 24 ?? ?? ?? ?? B8 ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 2B E0 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 85 ?? ?? ?? ?? 8B 85")]
public static partial void HandleActorControlPacket(uint entityId, uint category, uint arg1, uint arg2, uint arg3,
    uint arg4, uint arg5, uint arg6, uint arg7, uint arg8, GameObjectId targetId, bool isRecorded);
```

- Category `0x6D` (109) is DirectorUpdate (BossMod `ActorControlCategory.DirectorUpdate = 109`). `arg1` is the
  instance or content id and `arg2` is the command:

  | `arg2` | Meaning | Source |
  |---|---|---|
  | `0x40000001` | commence (`arg3` = lockout seconds) | Dalamud, cactbot |
  | `0x40000005` | wipe / fade out | Dalamud `DutyWiped`; ECommons `DirectorUpdateCategory.Wipe` |
  | `0x40000006` | recommence | Dalamud, cactbot |
  | `0x4000000F` | fade in. cactbot uses this as its wipe trigger since 6.2 and notes "probably should switch to fade out". | cactbot |
  | `0x40000011` | barrier up | cactbot |
  | `0x40000002` | victory (variant/criterion) | cactbot |
  | `0x40000003` | victory | cactbot |
  | `0x80000004` | lockout time adjust | cactbot |
  | `0x8000000C` | limit-break charge | cactbot |

- **Dalamud's `IDutyState` already hooks this function** (`DutyState.cs`, using
  `Hook<PacketDispatcher.Delegates.HandleActorControlPacket>`). It raises:

  | Event | Fires on |
  |---|---|
  | `DutyStarted` | `0x40000001` |
  | `DutyWiped` | `0x40000005` |
  | `DutyRecommenced` | `0x40000006` |
  | `DutyCompleted` | `0x40000002` / `0x40000003`, once per territory |

  - Each event is a `delegate void (IDutyStateEventArgs args)` (`Dalamud.Game.DutyState.IDutyStateEventArgs`).
  - `args` has `RowRef<TerritoryType> TerritoryType`, `RowRef<ContentFinderCondition> ContentFinderCondition` and
    `uint EventHandlerId` (= `arg1`). Use `args.TerritoryType.RowId` for the territory id (1122 for TOP).
  - `IsDutyStarted` is also available. When the commence packet was missed (joining in progress, reconnect),
    Dalamud sets `IsDutyStarted = true` from a framework-tick fallback (bound by duty and in combat) **without**
    raising `DutyStarted`.
- **Recommendation:** use `IDutyState` (no hook). Add your own `HandleActorControlPacket` hook only if you also want
  `0x4000000F` or `0x40000011`. Note that the generated delegate's `bool isRecorded` parameter has no
  `MarshalAs(U1)`; the delegate is declared in `FFXIVClientStructs.dll`, which is compiled with
  `[assembly: DisableRuntimeMarshalling]` (section 1.5). Dalamud uses the same delegate, and MitCue would not use
  that argument anyway.
- **Other director entry points** (exist in CS; not needed):
  - `ContentDirector.ProcessCommonDirectorUpdate(uint category, uint arg1, uint arg2, uint arg3, uint arg4)`
    handles categories ≥ 0x80000000.
  - `[VirtualFunction(325)] ContentDirector.ProcessDirectorSpecificDirectorUpdate(uint category, uint* parameters)`
    handles categories < 0x80000000.
  - `ContentDirector.ContentTimeLeft` (float).
  - `EventFramework.Instance()->GetContentDirector()` and `GetInstanceContentDirector()`.
  - `EventFramework.SetDirectorData(EventId, byte, byte, byte*, ulong)`. BossMod hooks this for "legacy" map
    effects.

### 6.3 MapEffect (not used by MitCue v1)

- `PacketDispatcher.HandleMapEffectPacket(MapEffectPacket* packet)` (static). `MapEffectPacket`
  (`…Client.Game.Network`, 0x10): `uint EventId`@0, `ushort State`@4, `ushort TimelineIndex`@6, `byte Index`@8.
- `ContentDirector.ApplyMapEffect(uint index, ushort state, ushort timelineIndex)` is an instance method.
  ECommons hooks it with `Hook<ContentDirector.Delegates.ApplyMapEffect>`. BossMod hooks the same function through
  its own signature.
- The **multi-MapEffect packet handlers are not in CS**. BossMod and ECommons find all three with
  `ScanAllText("40 55 41 57 48 83 EC ?? 48 83 B9")`, which must return exactly 3 matches.

### 6.4 Boss cast start (`StartsUsing`): no hook needed

- **Poll** hostile battle NPCs every tick through Dalamud `IBattleChara`: `IsCasting`, `CastActionId`,
  `CastActionType`, `CurrentCastTime`, `TotalCastTime`, `BaseCastTime`, `CastTargetObjectId`,
  `IsCastInterruptible`.
- The CS equivalent is `Character.GetCastInfo()` (`[VirtualFunction(80)]`, declared on `Character` and callable
  on a `BattleChara*`), or the `BattleChara.CastInfo` field at 0x2790. It returns a `CastInfo*` with `IsCasting`,
  `ActionType` (a `byte`, like `Header.ActionType`), `ActionId`, `CurrentCastTime`, `BaseCastTime`,
  `TotalCastTime`, `TargetId`. Compiled in `ExtraProbe2.cs`.
- **Optional hook:** `PacketDispatcher.HandleActorCastPacket(uint entityId, ActorCastPacket* packet)` (static).
  `ActorCastPacket` (0x20) has `ushort SpellId`, `byte ActionType`, `uint ActionId`@4, `float CastTime`@8,
  `uint TargetEntityId`@0xC, `bool Interruptible`, and quantised position and rotation.
  - BossMod hooks it only to record the cast position.
  - Whether `ActionId` in this raw packet can be scrambled is **UNVERIFIED**, so polling is safer.

### 6.5 Optional hooks (compiled verbatim)

```csharp
// MitCue (notes): optional low-level hooks. IDutyState already covers commence/wipe/recommence/complete.
using System;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;                 // ActionManager, ActionType
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent; // ContentDirector
using FFXIVClientStructs.FFXIV.Client.Game.Network;         // MapEffectPacket, ActorCastPacket
using FFXIVClientStructs.FFXIV.Client.Game.Object;          // GameObjectId
using FFXIVClientStructs.FFXIV.Client.Network;              // PacketDispatcher

namespace MitCue.Interop;

public sealed unsafe class DirectorUpdateHook : IDisposable
{
    private readonly Hook<PacketDispatcher.Delegates.HandleActorControlPacket> hook;

    public event Action<uint /*command*/, uint /*arg3*/>? DirectorUpdate;

    public DirectorUpdateHook(IGameInteropProvider interop)
    {
        this.hook = interop.HookFromAddress<PacketDispatcher.Delegates.HandleActorControlPacket>(
            PacketDispatcher.Addresses.HandleActorControlPacket.Value, this.Detour);
        this.hook.Enable();
    }

    private void Detour(uint entityId, uint category, uint arg1, uint arg2, uint arg3, uint arg4,
        uint arg5, uint arg6, uint arg7, uint arg8, GameObjectId targetId, bool isRecorded)
    {
        this.hook.Original(entityId, category, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, targetId, isRecorded);
        try
        {
            if (category == 0x6D) // DirectorUpdate; arg2: 0x40000001 commence, 0x40000005 wipe, 0x40000006 recommence,
                                  // 0x4000000F fade-in, 0x40000011 barrier up, 0x40000002/3 victory
                this.DirectorUpdate?.Invoke(arg2, arg3);
        }
        catch
        {
            // never throw into native code
        }
    }

    public void Dispose() => this.hook.Dispose();
}

public sealed unsafe class MapEffectAndCastHooks : IDisposable
{
    private readonly Hook<PacketDispatcher.Delegates.HandleMapEffectPacket> mapEffectHook;
    private readonly Hook<ContentDirector.Delegates.ApplyMapEffect> applyMapEffectHook;
    private readonly Hook<PacketDispatcher.Delegates.HandleActorCastPacket> actorCastHook;

    public MapEffectAndCastHooks(IGameInteropProvider interop)
    {
        this.mapEffectHook = interop.HookFromAddress<PacketDispatcher.Delegates.HandleMapEffectPacket>(
            PacketDispatcher.Addresses.HandleMapEffectPacket.Value, this.MapEffectDetour);
        this.applyMapEffectHook = interop.HookFromAddress<ContentDirector.Delegates.ApplyMapEffect>(
            ContentDirector.Addresses.ApplyMapEffect.Value, this.ApplyMapEffectDetour);
        this.actorCastHook = interop.HookFromAddress<PacketDispatcher.Delegates.HandleActorCastPacket>(
            PacketDispatcher.Addresses.HandleActorCastPacket.Value, this.ActorCastDetour);
        this.mapEffectHook.Enable();
        this.applyMapEffectHook.Enable();
        this.actorCastHook.Enable();
    }

    private void MapEffectDetour(MapEffectPacket* packet)
    {
        this.mapEffectHook.Original(packet);
        _ = packet->Index; _ = packet->State; _ = packet->TimelineIndex; _ = packet->EventId;
    }

    private void ApplyMapEffectDetour(ContentDirector* thisPtr, uint index, ushort state, ushort timelineIndex)
        => this.applyMapEffectHook.Original(thisPtr, index, state, timelineIndex);

    private void ActorCastDetour(uint entityId, ActorCastPacket* packet)
    {
        this.actorCastHook.Original(entityId, packet);
        _ = packet->ActionId; _ = packet->ActionType; _ = packet->CastTime; _ = packet->TargetEntityId;
    }

    public void Dispose()
    {
        this.mapEffectHook.Dispose();
        this.applyMapEffectHook.Dispose();
        this.actorCastHook.Dispose();
    }
}

/// <summary>Native hotbar "ants" on the planned action. Compiles; in-game effect UNVERIFIED.</summary>
public sealed unsafe class NativeHighlightHook : IDisposable
{
    private readonly Hook<ActionManager.Delegates.IsActionHighlighted> hook;

    public volatile uint HighlightActionId; // 0 = none; set from the reminder engine

    public NativeHighlightHook(IGameInteropProvider interop)
    {
        this.hook = interop.HookFromAddress<ActionManager.Delegates.IsActionHighlighted>(
            ActionManager.Addresses.IsActionHighlighted.Value, this.Detour);
        this.hook.Enable();
    }

    private bool Detour(ActionManager* thisPtr, ActionType actionType, uint actionId)
    {
        var original = this.hook.Original(thisPtr, actionType, actionId);
        var target = this.HighlightActionId;
        if (target != 0 && actionType == ActionType.Action
            && (actionId == target || thisPtr->GetAdjustedActionId(actionId) == target))
            return true;
        return original;
    }

    public void Dispose() => this.hook.Dispose();
}
```

---

## 7. Wiring example (compiled verbatim)

```csharp
// MitCue: minimal wiring of the pieces above (framework-thread polling + drained hook queue).
using Dalamud.Bindings.ImGui;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MitCue.Interop;

namespace MitCue.ProbeWiring;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider Interop { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;

    private readonly ActionEffectWatcher watcher;
    private readonly CountdownTracker countdown = new();
    private CooldownSnapshot benison; // snapshot taken on the framework thread, drawn by ImGui

    public Plugin()
    {
        this.watcher = new ActionEffectWatcher(Interop, Log);
        this.countdown.ReachedZero += () => SoundPlayer.PlayChatSe(1);
        DutyState.DutyWiped += this.OnWipe;
        Framework.Update += this.OnUpdate;
        PluginInterface.UiBuilder.Draw += this.Draw;
    }

    private void OnWipe(Dalamud.Game.DutyState.IDutyStateEventArgs args) => Log.Info($"wipe in {args.TerritoryType.RowId}");

    private void OnUpdate(IFramework framework)
    {
        while (this.watcher.Pending.TryDequeue(out var e))
            Log.Debug($"{e.CasterEntityId:X8} used {e.ActionType}/{e.ActionId} on {e.TargetEntityIds.Length} targets");
        this.countdown.Update();
        if (CooldownReader.Available)
            this.benison = CooldownReader.Read(7432); // Divine Benison
    }

    private void Draw()
    {
        var b = this.benison;
        ImGui.TextUnformatted($"Benison {b.CurrentCharges}/{b.MaxCharges} rem {b.RecastRemaining:F1}s status {b.ActionStatus}");
    }

    public void Dispose()
    {
        Framework.Update -= this.OnUpdate;
        PluginInterface.UiBuilder.Draw -= this.Draw;
        DutyState.DutyWiped -= this.OnWipe;
        this.watcher.Dispose();
    }
}
```

---

## 8. UNVERIFIED items (need an in-game check; everything else above is VERIFIED)

1. The thread that packet-handler detours (ActionEffect, ActorControl) run on. Believed to be the main thread
   (BossMod and Dalamud `DutyState` assume so).
2. ActionEffect event order and coverage:
   - whether a player's pet command (e.g. Whispering Dawn 16537) produces its own ActionEffect next to the pet's
     (803/16550);
   - bell actions 25863/25864;
   - zero-target boss abilities;
   - meaning of `Effect.Value` and the `Param` bytes for each type.
3. Whether ECommons' prologue signature and the CS `Receive` signature resolve to the same address. Also whether
   the values in cactbot's 21/22 fields are exactly the `Header` fields (section 2.6; the names and offsets match
   Machina's header, but the ACT plugin that writes the lines is closed source).
4. Runtime results of:
   - `GetAdjustedActionId` mappings (section 3.7);
   - `GetMaxCharges(id, 0)` under level sync (expected: Benison 1 in TEA, 2 in TOP/DMU; Tetragrammaton 2 only in DMU);
   - `GetMaxCharges` for another job's action;
   - `GetRecastTime` while `IsRecastTimerActive` is false;
   - the "group index = CooldownGroup − 1" mapping and the 80–86 group ranges (both from BossMod);
   - `GetActionStatus` with the default (no) target for targeted actions;
   - recast, charge and status reads for a base id whose button currently shows a follow-up (Temperance during
     Divine Caress, Summon Seraph while Seraph is out, Liturgy while the bell is placed), and `GetActionStatus` for
     a base id that a trait upgraded (Succor or Medica II at L96+);
   - local-player recast values including trait reductions (Enhanced Deployment Tactics/Recitation/Swiftcast).
5. Countdown:
   - when `Active` turns false relative to `TimeRemaining` reaching 0 (`CountdownTracker` threshold);
   - what `InitiatorId` contains, and whether `TimeRemaining` keeps its last value while inactive;
   - which LogMessage rows the game prints for `/countdown`.
6. Sound: which volume bus `PlaySoundEffect` uses, whether it honours mute, and whether it plays while the window
   is inactive.
7. Hotbar highlight approaches: ImGui rect scaling, and whether the `IsActionHighlighted` hook shows ants.
8. Whether `ActorCastPacket.ActionId` can be scrambled.

## 9. Snippet compile record

- `scratchpad/probes/clientstructs`: the original probe. It holds earlier drafts of the snippets above (namespace
  `CsProbe`; they differ from the fences in this file) plus extra checks:
  `Character.GetActionEffectHandler()`/`IncomingEffects`, `RaptureHotbarModule.GetSlotById`, `IDutyState` event
  signatures, `GetHotbarCooldownInfo`, `Cooldowns` span, `BattleChara.GetCastInfo()`/`CastInfo.IsCasting`,
  `GetAdjustedCastTime`. Result: Build succeeded, 0 warnings, 0 errors. Its `CooldownReader.cs` still reads
  timers and charges for the adjusted id, which section 3.7 rejects: copy code from this document, not from that
  directory.
- `scratchpad/probes/clientstructs-neg`: negative control. It fails with the 8 expected errors.
- `scratchpad/probes/clientstructs-doc`: every `csharp` fence in this document (8 fences), extracted with
  `scratchpad/scripts/extract_csharp.py` into one file per fence and compiled with `Dalamud.NET.Sdk/15.0.0`,
  `dotnet build -c Release --no-incremental`. Result: **Build succeeded, 0 warnings, 0 errors**.
- The output assembly contains every type named in the snippets: `ActionEffectWatcher`, `ActionEffectWatcherAlt`,
  `ObservedActionEffect`, `CooldownReader`, `CountdownReader`, `CountdownTracker`, `SoundPlayer`, `HotbarLocator`,
  `DirectorUpdateHook`, `MapEffectAndCastHooks`, `NativeHighlightHook` and `ProbeWiring.Plugin`.
- After the verifier's corrections, the 8 fences were extracted again and compiled verbatim in
  `scratchpad/probes/verify-clientstructs` (0 warnings, 0 errors). See the Verification section.

---

## Verification

Independent check of this report, done after it was written. The verifier re-derived every claim below from
primary sources instead of reusing the researcher's decompile or data copies. `scratchpad` is the same directory as
in section 0.

### How it was checked

| Source | What the verifier did |
|---|---|
| Installed Dalamud | Read `TEMP_gitver.txt`, `commit_hash.txt` and `revision.txt`, and the DLL sizes and dates. |
| Source trees | `lib/FFXIVClientStructs` pointer in both Dalamud clones (`12e318c1` and `43b34118`) is `80cb4a7b`. The CS clone is at `80cb4a7b` with a clean tree. Read the CS, Dalamud (`DutyState.cs`, `Hook.cs`, `HookVerifier.cs`, `GameInteropProviderPluginScoped.cs`, `Window.cs`/`WindowHost.cs`, `SeStringCreatorWidget.cs`, `IGameGui.cs`, `BattleChara.cs`), BossMod `6e410ef8`, ECommons `bc38dc71` and cactbot sources directly. |
| Bundled DLLs | New ilspycmd decompiles of `ActionEffectHandler`, `ActionManager`, `AgentCountDownSettingDialog`, `UIGlobals`, `PacketDispatcher`, `Character`, `AddonActionBarBase`, `Framework`, `InteropGenerator.Runtime.Address` and `ThrowHelper` into `scratchpad/probes/verify-clientstructs-decomp`. Assembly-level attributes of `FFXIVClientStructs.dll`, `Dalamud.dll` and a probe plugin read with `System.Reflection.Metadata` (`scratchpad/probes/verify-clientstructs-meta`). |
| Compiling | New probe `scratchpad/probes/verify-clientstructs` (copy of `buildtest/Probe`). `VerifyExact.cs` proves exact signatures by assignment to function pointers: `&Static.Method` for static members and typed `T.MemberFunctionPointers.X` for instance members. A function-pointer conversion compiles only when parameter and return types are identical. It also proves exact field types (`T* p = &s->Field`) and checks enum values and struct sizes with compile-time constant asserts. `VerifyGenerated.cs` and `VerifyDuty.cs` check the generated vtable members and the `IDutyStateEventArgs` types. The 8 `csharp` fences of this file were extracted with the verifier's own script and compiled verbatim, before and after the corrections: 0 warnings, 0 errors. |
| Negative control | `scratchpad/probes/verify-clientstructs-neg`: 13 deliberately wrong variants. All fail with the expected error: CS0020 (wrong `AgentId` value in a constant assert), CS0221 (negative `ActionType` constant), CS0407 ×2 (wrong return type for `GetMaxCharges` and `GetAdjustedRecastTime`), CS0266 ×2 (wrong function-pointer return type, `bool*` vs `float*`), CS0176, CS0120, CS1503, CS1061 (`IsActive`), and CS0117 ×3 (`Character.Addresses.GetCastInfo`, `Character.MemberFunctionPointers.GetCastInfo`, `ActionManager.MemberFunctionPointers.Instance`). Copies of the researcher's probe and negative control also rebuild: 0/0, and exactly the 8 errors claimed in section 0. |
| Game data | `Action.csv`, `ClassJobCategory.csv` from `research/gamedata`. `Trait.csv` and `LogMessage.csv` fetched again from xivapi/ffxiv-datamining into `scratchpad/probes/verify-clientstructs-data`. |

### What was checked (about 111 API and source claims, plus 64 data entries)

- **Sections 0–1:** provenance, SDK props, the resolver order in `Dalamud.cs`, `Address` and `ThrowHelper`, the
  generated members per attribute, the `IGameInteropProvider` overloads, `HookBackend` `[Obsolete]`, the `Hook<T>`
  members and `trackedHooks`.
- **Section 2:** the exact `Receive` signature and its generated members; all 12 `Header` fields and the 2 bit
  properties; the `Effect`, `TargetEffects` and `EffectEntry` layouts; `GameObjectId`; the BossMod
  `ActionEffectType` values (all 31 listed); and the BossMod and ECommons hook code and comments.
- **Section 3:**
  - all 18 rows of the 3.1 table (static or instance, exact parameter and return types, signature strings);
  - the `ActionManager` fields, `RecastDetail`, `HotbarCooldownInfo` and the `ActionType` values;
  - BossMod's GCD group 57, `CooldownGroup − 1` and the group ranges 0–79, 80–81 and 82–86.
- **Section 4:** the countdown layout, `AgentId.CountDownSettingDialog = 251`, the generated `Instance()`, the
  `Framework`/`UIModule`/`AgentModule` chain, the git history (`d8250499`, `ad4c8451` only), and BossMod's read.
- **Section 5:** `PlaySoundEffect` and `PlayChatSoundEffect` (`+ 36`), the Dalamud window sound ids 23/24 and
  `IWindow.DisableWindowSounds`, and `PlayChatSoundEffect(u32 + 1)` in `SeStringCreatorWidget`.
- **Section 6:**
  - the hotbar module, slot, slot type and addon offsets, and vtable index 81;
  - the `GetAddonByName` overloads;
  - the three `PacketDispatcher` handlers and both packet layouts;
  - `DutyState` (events, arguments, fallback), the cactbot director table and the `wipe` regex;
  - the `ContentDirector` and `EventFramework` members;
  - `GetCastInfo` VF80, `GetActionEffectHandler` VF82 (vtable offsets 0x280/0x290), `BattleChara.CastInfo`@0x2790,
    the `IBattleChara` properties and `SearchByEntityId`;
  - the multi-MapEffect signature with its "exactly 3 matches" check in both BossMod and ECommons.
- **Data (64 entries, all match):**
  - all 30 rows of the 3.4 table (5 columns each);
  - the 9 pet, bell and detonation ids;
  - the 7 adjusted ids in 3.7;
  - traits 490 and 625;
  - all 16 LogMessage rows quoted.
- **Also checked:** 8 sampled TOP timeline `Ability` ids, read as hex Action row ids. The 7 named ones match the
  Action names (for example `7B03` = 31491 "Program Loop"); the 8th is an unnamed `--sync--` row. This supports
  "timeline ids are `Header.ActionId`".

### Errors found and corrected in place

1. **Section 1.3 (generated members) was wrong for `[VirtualFunction]` and incomplete for `[StaticAddress]`.**
   - It said every `[MemberFunction]`, `[StaticAddress]` or `[VirtualFunction]` gets `Addresses`, `Delegates` and
     `MemberFunctionPointers`, and that the wrapper null-checks the address.
   - In fact a `[VirtualFunction(n)]` gets only `Delegates.Name` plus a slot in `T.<T>VirtualTable`. The decompile
     of `Character` shows `CharacterVirtualTable.GetCastInfo` at offset 640 = 80 × 8. Its wrapper calls
     `VirtualTable->Name`, with no address check.
   - A `[StaticAddress]` gets `Addresses` plus `StaticAddressPointers` (`p`/`pp`) and no delegate or function
     pointer.
   - Proven by compiling: the vtable slots compile, and `Character.Addresses.GetCastInfo`,
     `Character.MemberFunctionPointers.GetCastInfo` and `ActionManager.MemberFunctionPointers.Instance` are CS0117.
   - Section 1.4 now says only `[MemberFunction]`/`[StaticAddress]` wrappers throw for a missing address.
2. **Section 2.6 said cactbot logs a use that hits nobody as a line 22.**
   - cactbot's own LogGuide example of exactly that case ("Seed Of The Sky", target `E0000000`) is a **21** line
     with `targetIndex` 0 and `targetCount` 1 (parsed field by field). The LogGuide prose says such ground AoEs are
     22 lines.
   - Corrected: the docs disagree, so don't depend on 21 vs 22 (cactbot's `Ability` regex matches `2[12]`). The
     `targetCount`/`NumTargets` correspondence is now marked as not holding for zero-target uses.
3. **Sections 3.7 and 3.9: the `CooldownReader` snippet read readiness from the adjusted id.**
   - It read timers, charges, status and recast groups for `GetAdjustedActionId(id)`, and 3.7 recommended this
     ("query the cooldown of the action that will actually fire").
   - For three mappings in the report's own table, the follow-up has its own short recast group (`Action.csv`):
     Divine Caress 37011 (group 2, 1 s), detonation 28509 (group 1, 1 s) and Consolation 16546 (group 11, own
     charges). Right after Temperance, Liturgy of the Bell or Summon Seraph, the snippet would report the planned
     action as ready while it is on a 120–180 s cooldown.
   - Corrected: the snippet reads everything for the planned (base) id and keeps the adjusted id for name, icon and
     matching. `remaining` is clamped at 0. 3.7 explains why, and section 8 lists the runtime check this needs.
   - The corrected fence compiles verbatim.
4. **Section 5.2 inferred** from the existence of `SoundManager.PlaySoundsWhenWindowInactive` that sounds probably
   don't play in the background by default.
   - The CS source comments that field as the *inverted* config option `IsSoundAlways`. The effective-volume
     function zeroes a bus when `WindowInactive` and that flag (or the bus entry at 0x1F5, `_busAlwaysOn`) are set.
   - So it is a user setting, and the field says nothing about the default. Reworded.
5. **Section 2.4: "`0xE0000000` means 'no object'" was imprecise.**
   - Per the CS comment it is the "no entity id" value. Such objects get a `GameObjectId` built from `BaseId`, the
     object index or the owner (`Type` 1/2/4), so `ObjectId` is not an entity id for them.
   - As a target id it means "no target". Reworded.

### Omissions added

- **3.4:** `Recast100ms` excludes traits, just as `MaxCharges` does.
  - Enhanced Deployment Tactics (trait 494, SCH L88, −30 s) applies in TOP and DMU.
  - Enhanced Recitation (629, SCH L98, −30 s) and Enhanced Swiftcast (644, L94, ClassJobCategory 199 includes WHM
    and SCH, −20 s) apply in DMU.
  - MitCue's co-healer table needs these.
- **1.5:** Dalamud's `HookVerifier` only runs with Developer Mode on (`Dalamud.cs`), and it only logs.
  `FFXIVClientStructs.dll` carries `[assembly: DisableRuntimeMarshalling]` and the plugin assembly does not. Advice
  added: use `[MarshalAs(U1)]` on `bool` in any self-declared delegate. A Linux .NET 10 runtime test of
  unannotated `bool` marshalling was inconclusive: results depended on JIT codegen for non-normalised values. The
  runtime behaviour is therefore marked UNVERIFIED rather than asserted.
- **6.2:**
  - the `IDutyStateEventArgs` member types (`RowRef<TerritoryType>`, `RowRef<ContentFinderCondition>`, `uint`);
    the wiring example now logs `args.TerritoryType.RowId`;
  - the `IsDutyStarted` framework-tick fallback, which does **not** raise `DutyStarted`;
  - why the CS delegate has no `MarshalAs`.
- **4.2:**
  - LogMessage rows 5262/5263 ("Battle commencing in …") and 5256/5264 ("Engage!");
  - which rows the game actually prints is UNVERIFIED;
  - "keeps its last value" is now marked UNVERIFIED.
- **Smaller notes:**
  - `CastInfo.ActionType` is a `byte`;
  - "boss and helper actors exist as objects" is marked UNVERIFIED;
  - trait 628 "Succor Mastery" is cited for Succor → Concitation.

### Confirmed as written (no change)

- Every other API name, signature, static/instance split, field offset and type, enum value, struct size and
  signature string listed above.
- All 30 cooldown-table rows, the pet and bell ids, the trait levels and values, and all quoted LogMessage texts.
- The cactbot and Dalamud director-command mapping; the BossMod and ECommons citations, including the "5 functions"
  and "deemed to be minor" comments, pomander 579 (`BossMod/Framework/ActionManagerEx.cs`) and
  `LuminaRow<LogMessage>` (`ActionManagerEx.cs` and `BossMod/Debug/DebugAction.cs`).
- The BSD-3 (BossMod) and MIT (ECommons) licences.

### Still UNVERIFIED (needs the running game)

Everything in section 8, including the two items added there:

- reads for a base id whose button shows a follow-up, and status for a trait-upgraded base id;
- whether local-player recast values include the trait reductions.

Also the runtime marshalling of unannotated `bool` in plugin-declared delegates on Windows. Unchanged UNVERIFIED
items include the next-charge formula (BossMod documents that the game sets `Total` = recast × max charges at the
level cap and `Elapsed` = recast × (max charges at the current level − 1) on first use, which is consistent with it)
and every in-game behaviour listed in section 8.

---

## Verification (second pass)

A second, independent check of the whole report, including the first verifier's corrections and their Verification
section. Every claim below was re-derived from primary sources with this pass's own probes, scripts and downloads;
nothing in the researcher's or the first verifier's working directories was used as evidence. `scratchpad` is the
same directory as in section 0.

### How it was checked

| Source | What this pass did |
|---|---|
| Source trees | Read CS at `80cb4a7b`. Read Dalamud at `12e318c1`: `DutyState.cs`, `IDutyState.cs`, `DutyStateEventArgs.cs`, `IGameInteropProvider.cs`, `GameInteropProviderPluginScoped.cs`, `Hook.cs`, `ReloadedHook.cs`, `HookManager.cs`, `HookVerifier.cs`, `Window.cs`/`IWindow.cs`/`WindowHost.cs`, `SeStringCreatorWidget.cs`, `IGameGui.cs`, `IObjectTable.cs`, `BattleChara.cs`, `GameObject.cs`, `IFramework.cs`, `Dalamud.cs`. Also read `Dalamud.NET.Sdk/15.0.0` `Sdk.props`, ECommons `bc38dc71`, cactbot `e79ea7f1` (`LogGuide.md`, `netregexes.ts`), OverlayPlugin `43afdd59` (`LineAbilityExtra.cs`) and Machina `2bd9a7d2` (`Server_ActionEffect.cs`, `Server_MessageHeader.cs`). BossMod: a full depth-1 clone at the same commit `6e410ef8` in `scratchpad/probes/verify-clientstructs-pass2-bossmod-full`, because the sparse clone lacks `BossMod/Debug`, `ActionQueue` and `Data`. |
| Bundled DLLs | New ilspycmd decompiles in `scratchpad/probes/verify-clientstructs-pass2-decomp`: `ActionManager`, `ActionEffectHandler`, `AgentCountDownSettingDialog`, `UIGlobals`, `PacketDispatcher`, `RaptureHotbarModule`, `ContentDirector`, and `InteropGenerator.Runtime.Address`/`ThrowHelper`. Assembly attributes read with `MetadataLoadContext` (`scratchpad/probes/verify-clientstructs-pass2-attr`): `DisableRuntimeMarshalling` is on `FFXIVClientStructs.dll`, and on neither `Dalamud.dll` nor a `Dalamud.NET.Sdk` plugin. XML member entries looked up by full name. |
| Compile probe | `scratchpad/probes/verify-clientstructs-pass2` (copy of `buildtest/Probe`; `Pass2Exact.cs`, `Pass2VirtualFn.cs`). It has 39 typed function-pointer assignments (`&Type.Method` for static members, `T.MemberFunctionPointers.X` for instance members; both compile only on an identical signature) and 130 `Exact<T>(ref x)` assertions (a `ref` argument needs an identity conversion, so field and return types are exact). It also has 5 method-group conversions to generated delegates, calls that rely on the default and named parameters, and every Dalamud service member the report uses. 0 warnings, 0 errors. |
| Negative control | `scratchpad/probes/verify-clientstructs-pass2-neg`: 16 deliberately wrong variants, each failing with the expected error: CS0407 ×3, CS1503 ×3, CS0266 ×2, CS0117 ×4 (no `Addresses` or `MemberFunctionPointers` for a virtual function, no `Delegates` for a static address), CS0176, CS0120, CS8757, CS1061. Copies of the researcher's probe and negative control (`scratchpad/probes/verify-clientstructs-pass2-rcopy`) rebuild with 0/0 and with exactly the 8 errors claimed in section 0. |
| Runtime layout | `scratchpad/probes/verify-clientstructs-pass2-layout`: a net10.0 console app that loads the bundled `FFXIVClientStructs.dll`. Without calling game code, it checks 143 items: struct sizes, field offsets, enum values, bit-field properties, span lengths, vtable slot offsets, `Address` metadata and the unresolved-address behaviour. 143 of 143 pass. |
| Fences | The 8 `csharp` fences, extracted with this pass's own script (`scratchpad/scripts-verify-pass2/extract_fences.py`) into `scratchpad/probes/verify-clientstructs-pass2-fences`. Compiled verbatim before and after the edits below: 0 warnings, 0 errors, and all 13 named types are in the output assembly. |
| Game data | `research/gamedata/{Action,ClassJob,ClassJobCategory,TerritoryType,ContentFinderCondition}.csv`. `Trait.csv`, `TraitTransient.csv` and `LogMessage.csv` downloaded again into `scratchpad/probes/verify-clientstructs-pass2-data`. |

### What was checked

- **API surface (sections 1–7).** Every name the report states, with its static or instance split, exact
  parameter and return types, default values, field offsets and types, struct sizes, enum values, vtable indexes
  and generated members.
  - ActionEffect: the 12 `Header` fields and its 2 bit properties, `GameObjectId`, `EffectEntry`.
  - ActionManager: all 18 rows of the 3.1 table, `RecastDetail`, `HotbarCooldownInfo`, the 21 `ActionType` values.
  - Countdown and sound: the countdown agent and `AgentId` 251, `SoundManager`.
  - Hotbars: the hotbar module, slot, slot-type and addon structs.
  - Packets and directors: the three `PacketDispatcher` handlers and both packet layouts, `ContentDirector` (VF325),
    `EventFramework`.
  - Characters: `Character` (VF80 and VF82) and `BattleChara.CastInfo`.
- **Behaviour without the game.**
  - `ActionManager.Instance()` and the static `GetMaxCharges` throw `InvalidOperationException` with the exact
    `ThrowNullAddress` text quoted in 1.4, and `StaticAddressPointers.pInstance` is null.
  - `Addresses.Receive` follows the relative call at byte 1, and its `.Value` is 0 until resolved.
  - `UIGlobals.PlayChatSoundEffect(0)` and `(17)` throw `ArgumentException`. `(1)` passes the range check and then
    reaches the null-address check of `PlaySoundEffect`.
  - Bits 0 and 1 of `Header.Flags` drive `ShowInLog` and `ForceAnimationLock`.
  - `AgentCountDownSettingDialog.Instance()` throws (from `Framework.Instance()`) only while the Framework
    signature is unresolved, as 4.1 says.
- **Dalamud.**
  - Hooks: the `HookFromAddress` overloads and the `Hook<T>` members. The `backend` argument is only warned about
    (`WarnForDeprecatedBackend`). Hooks start disabled, because the default Reloaded backend calls `Activate()` and
    then `Disable()`. Also plugin-scoped tracking, and `HookVerifier` (Developer Mode only, logs only, size rules).
  - Duty state: the `IDutyState` events and their delegate and argument types, the `DutyState` command switch,
    once-per-territory completion, and the framework-tick fallback.
  - Other services: the `IGameGui.GetAddonByName` overloads, `IObjectTable.SearchByEntityId`, and the
    `IBattleChara` and `IGameObject` members.
  - Window and sound: `IWindow.DisableWindowSounds` and sound ids 23/24; the `PlayChatSoundEffect(u32 + 1)` call in
    `SeStringCreatorWidget`.
  - Startup and SDK: the resolver order in `Dalamud.cs`, `Sdk.props`, and that `Dalamud.xml` has no ActionEffect
    or countdown service.
- **BossMod, ECommons, cactbot.**
  - BossMod hooks and comments: the hook construction (`HookAddress` → `HookFromAddress`, then `Enable`); the three
    comments in `ProcessPacketActionEffectDetour`; the countdown read; the cast-position hook; the map-effect hooks
    and the "exactly 3" multisig; `SetDirectorData`.
  - BossMod data and citations: GCD group 57, `CooldownGroup − 1` and the 80–81 and 82–86 ranges; the
    first-charge comment; pomander 579 and `LuminaRow<LogMessage>`; the `IncomingEffects` read;
    `DirectorUpdate = 109`; the 31 listed `ActionEffectType` values.
  - ECommons: the signature, delegate shape and `EffectHeader` offsets; HQ items and mounts; `Wipe`; the
    `ApplyMapEffect` hook.
  - cactbot: the 21/22 prose and the "Seed Of The Sky" line; the `2[12]` regex; the director command list and the
    `wipe` regex (`40000010`/`4000000F`).
  - Licences: BSD-3 (BossMod) and MIT (ECommons).
- **Data (96 entries, all match):**
  - all 30 rows of the 3.4 table (5 columns each);
  - 19 action ids: the 9 pet, bell and detonation ids, and the base and follow-up ids of 3.7, including Medica
    II/III;
  - traits 490, 494, 624, 625, 628, 629, 644 and ClassJobCategory 199;
  - the 20 LogMessage rows quoted in 3.5 and 4.2;
  - TerritoryType 1122/887/1363, with ContentFinderCondition sync levels 90/80/100;
  - 16 randomly sampled TOP timeline `Ability` ids. 15 resolve to Action rows named like the label (without its
    suffix, for example "Blaster 4" → Blaster); the 16th is an unnamed `--sync--` row.
- **Completeness of the 3.4 trait table.**
  - Read every Trait row for WHM, SCH, CNJ and ACN, and for every role category that includes them, together with
    its `TraitTransient` description.
  - Only 490, 494, 625, 629 and 644 change a recast or a charge count.
  - The traits with `Value 0` (for example 310 Enhanced Asylum, 313 Enhanced Sacred Soil, 623, 626, 627, 656)
    change neither.

### Errors found and corrected in place

1. **Section 9 called the original probe "all files above plus extra checks".**
   - Its files are earlier drafts (namespace `CsProbe`) that differ from every fence.
   - Its `CooldownReader.cs` still reads timers and charges for `GetAdjustedActionId(id)`, the logic section 3.7
     rejects.
   - Reworded, with a note to copy code from this document only.
2. **Section 3.7 said three follow-ups have a recast group of their own.** There are four in the table: Dissolve
   Union 7869 is CooldownGroup 2 (1 s), while Aetherpact 7437 is group 6 (3 s). Corrected; the paragraph now says
   which three matter.
3. **The first-pass Verification section had two wrong details.**
   - It said "all 17 rows of the 3.1 table"; the table has 18. Corrected.
   - It placed the pomander-579 comment in `BossMod/Debug/DebugAction.cs`. The comment is in
     `BossMod/Framework/ActionManagerEx.cs` (`LuminaRow<LogMessage>` is in both files). Corrected.

### Omissions and precisions added

1. **3.7:** Medica II (133) → Medica III (37010), from trait 624 "Medica Mastery". It is WHM L96
   (`TraitTransient`: "Upgrades Medica II to Medica III"), so it applies in DMU only. Added to the table and to the
   runtime check in section 8. Also cited BossMod's `UIRotationWindow`, which uses the adjusted id only for the
   displayed icon and name.
2. **1.4:** what happens when the `.Value != 0` check is skipped.
   - `Hook<T>.FromAddress` calls `HookManager.FollowJmp`, which throws `InvalidOperationException` for an
     address ≤ 0, so the plugin fails to load.
   - Added that the 2.5 variant and the 6.5 skeletons leave the check out.
3. **1.5:** `HookVerifier.CheckParam` accepts identical types before comparing sizes. An unannotated `bool` checked
   against a CS `bool` is therefore never flagged, so the verifier is no safety net for the marshalling advice.
4. **2.3:** the `ActionEffectType` table is a subset. Named the values it leaves out (9, 12, 13, 25, 26, 34–36, 39)
   and the source file.
5. **2.6 and section 8 (upgraded from fully UNVERIFIED):** the names and offsets of the cactbot 21/22 extra fields
   match Machina's open-source ActionEffect header.
   - Machina's header follows a 0x20-byte message header, so its offsets line up with `Header`.
   - Its `effectDisplayType` enum (0/1/2/0x0D) has the `ActionType` numbering, which also supports the
     "same numbering" note in 3.3.
   - The ACT plugin that writes the lines is closed source, so whether the logged values match stays UNVERIFIED.

### Checked and left unchanged

- **Everything else.** Every other API statement in sections 1–7 and every data value listed above is correct. No
  signature, offset, enum value, vtable index or data value had to change.
- **1.5, "disposes leftovers".** The plugin-scoped `trackedHooks` is a weak collection (`ConditionalWeakTable`).
  But Dalamud's global `HookManager.TrackedHooks` holds each hook and its detour strongly until the hook is
  disposed, so the statement holds.
- **2.4, "BossMod uses this".** `WorldStateGameSync` reads `GetActionEffectHandler()->IncomingEffects` every frame,
  and BossMod also hooks the EffectResult packets.
- **4.1, "No other countdown struct exists in CS".** The other countdown-named members in CS are not `/countdown`
  state: `ReadyCheckModule.CountdownLength`, the `ContentDirector.ContentTimeLeft` remark, two spawn-packet timers
  and a replay chapter type.

### Still UNVERIFIED (needs the running game)

Everything in section 8, which now includes the Medica II → Medica III mapping. Also the runtime marshalling of an
unannotated `bool` in a plugin-declared delegate on Windows.
