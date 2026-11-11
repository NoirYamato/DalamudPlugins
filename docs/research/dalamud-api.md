# Dalamud API 15 reference for MitCue

> Compiled by the research agent against Dalamud 15.0.4.0 (AssemblyVersion/FileVersion of the shipped Dalamud.dll, ProductVersion `15.0.4.0+12e318c1`; `git describe` prints `15.0.3.9-31-g12e318c19` only because 15.0.3.9 is the nearest tag; commit 12e318c1, API level 15) [version corrected in verification, see V1], Dalamud.NET.Sdk 15.0.0, .NET SDK 10.0.404. Saved from the agent's structured result (the agent could not write files). Probe sources: `/tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/probes/dalamud-api/` (builds with 0 errors/0 warnings).

## Summary

(Historical note: the research agent could not write this file itself; the parent saved it from the agent's structured result, and an independent verifier then corrected it in place — see the Verification section at the end.) The full reference is in the Facts below, which follow the task's sections a–l and gives exact signatures and Dalamud source citations (file:line). The probe source that belongs in the report's appendix is on disk (see artifacts).

**How it was checked.** I worked against Dalamud 15.0.4.0 (commit 12e318c1, API level 15; originally written as 15.0.3.9, which is only the nearest git tag — see V1) using Dalamud.NET.Sdk 15.0.0 and DalamudPackager 15.0.0 on .NET SDK 10.0.404 (net10.0-windows, C# 14). The Dalamud source is checked out at the same commit as the DLLs.
- The probe has 8 files and 1086 lines. It builds with 0 errors and 0 warnings and uses every API in the task.
- A separate probe that should fail captures the exact compiler errors for 20 old-API patterns.
- Signatures were read straight from the shipped DLLs with a reflection tool.
- Four behaviours were run outside the game against the shipped DLLs: the config save/load round trip, the chat LogInfo bit layout, reading pluginmaster JSON with Dalamud's own RemotePluginManifest type, and ReadOnlySeString text extraction.

**What implementers most need to know:**
1. **Local player.** `IClientState` no longer has `LocalPlayer` or `LocalContentId` (CS1061). Use `IObjectTable.LocalPlayer`, or the new `IPlayerState`; its `EffectiveLevel` is the synced level.
2. **Chat.** `IChatGui.ChatMessage` is now `void(IHandleableChatMessage message)`, and Dalamud reuses the same message object for every line, so copy what you need. Countdown lines are `XivChatType.SystemMessage` (57). The "started by" relation is in `TargetKind`, not `SourceKind` (raw 0x00B9 decoded at runtime). The tick lines ("Battle commencing in 10 seconds!", no name) and "Engage!" are 0x0039.
3. **Countdown without parsing text.** Poll `AgentCountDownSettingDialog.Active` and `TimeRemaining`, as BossMod does. Another option is `IChatGui.LogMessage` with LogMessage rows 5255/5256/5257, but which rows actually fire is unconfirmed.
4. **ImGui.** The namespace is `Dalamud.Bindings.ImGui` and string parameters are `ImU8String`.
   - `ImGui.Image` takes an `ImTextureID`, which you get from `IDalamudTextureWrap.Handle`.
   - `InputText` / `InputTextMultiline` take `maxLength` as an int byte count that defaults to 512, so a paste box for a whole sheet needs a large buffer. The string overload re-encodes the text every frame.
   - The fourth int argument of `Combo` is now the popup height, not the item count.
5. **Game data.** Lumina rows are structs in `Lumina.Excel.Sheets`; alias `Action` because it clashes with `System.Action`. Convert names with `ExtractText()`. `RowRef` has `RowId`, `IsValid`, `Value` and `ValueNullable`.
6. **Icons.** Call `GetFromGameIcon(new GameIconLookup(id)).GetWrapOrEmpty()` every frame. Do not cache or dispose the result.
7. **Threading.**
   - `IFramework.Update` and `UiBuilder.Draw` both run on the game's main thread; Draw runs inside the swap-chain Present call. Dalamud's own source shows this indirectly, and a one-line in-game log is worth adding. So reading the object table, conditions and client state from Draw is allowed.
   - The plugin constructor does not run on the main thread, so object-table reads there throw. `Dispose` runs on the framework thread.
   - The object table reuses one wrapper object per slot, so never keep an `IGameObject` from one frame to the next; store its ID instead.
8. **Config.** It is saved with Newtonsoft.Json. A list that starts with default items comes back with those items duplicated after each save/load, unless you mark it `ObjectCreationHandling.Replace` (confirmed by running it). A dictionary with default entries does not duplicate (same keys are overwritten), but default keys the user deleted come back on the next load, so mark it `Replace` too (V4). `GetPluginConfig` deserialises into the first `IPluginConfiguration` type it finds in your assembly.
9. **Windows.** `Window.Flags` is read after `PreDraw`, so switch click-through there. `Size` and `SizeConstraints` are multiplied by the global UI scale. Set `AllowPinning` and `AllowClickthrough` to false so Dalamud's own saved toggles don't override MitCue's.
10. **Hooks.** Create them with `IGameInteropProvider.HookFromAddress`, using ClientStructs' `Addresses` and `Delegates` types (ActionEffectHandler.Receive and PacketDispatcher.HandleActorCastPacket both compile). Hooks start disabled, and the backend parameter should not be passed.
11. **Packaging.**
   - csproj properties become the manifest; Name, Author, Description and Punchline are required.
   - A Release build writes `bin/Release/<AssemblyName>/<AssemblyName>.json` and `bin/Release/<AssemblyName>/latest.zip` (named after `$(AssemblyName)` = InternalName, not the display `<Name>`; V5).
   - A pluginmaster file is a JSON array of `RemotePluginManifest`.
   - In this build (15.0.4.0, commit 12e318c1) both install and update download from `DownloadLinkInstall` (or `DownloadLinkTesting`).
   - Manifests have no dependency field, so BossMod or cactbot can only be used through IPC and the plugin must cope when they're missing.

## Facts (each verified by compiling or by source citation unless marked UNVERIFIED)

### 1. Provenance: the shipped Dalamud.dll is AssemblyVersion/FileVersion **15.0.4.0** (ProductVersion `15.0.4.0+12e318c1936e449e5883faa61214eb2ced478349`, commit 12e318c1 'Merge PR #3052 KazWolfe/patch_7.58'); `git describe` gives 15.0.3.9-31-g12e318c19 only because 15.0.3.9 is the nearest tag. API level 15 = Dalamud assembly major version (Dalamud/Plugin/Internal/PluginManager.cs:83). Source checked out at that exact commit. Commit 43b34118 (where the dalamud-cs-src clone sits) is an OLDER ancestor of 12e318c1; the only content difference is Dalamud/Dalamud.csproj `<DalamudVersion>` (15.0.3.9 at 43b34118, 15.0.4.0 at 12e318c1 via commit 51b4552 'build: 15.0.4.0'). FFXIVClientStructs.dll is 7.57.0.9480 (commit 80cb4a7b = the clientstructs clone). [Corrected in verification, V1: the original said the DLLs are 15.0.3.9 and that 43b34118 was the newer master.]

Evidence: $HOME/.xlcore/dalamud/Hooks/dev/commit_hash.txt + TEMP_gitver.txt; git fetch of 12e318c1 + git diff vs HEAD; runtime: RemotePluginManifest.IsAvailableForTesting true with TestingDalamudApiLevel 15

### 2. Toolchain: Sdk="Dalamud.NET.Sdk/15.0.0" pulls DalamudPackager 15.0.0 (commit 827d88dc) + DotNet.ReproducibleBuilds 1.2.39 and sets TargetFramework net10.0-windows, LangVersion 14.0, Nullable enable, AllowUnsafeBlocks, PlatformTarget x64, RestorePackagesWithLockFile (packages.lock.json), references Dalamud, Dalamud.Bindings.ImGui/ImPlot/ImGuizmo, FFXIVClientStructs, InteropGenerator.Runtime, Newtonsoft.Json, Lumina, Lumina.Excel, Serilog, Microsoft.Extensions.ObjectPool with Private=false. DalamudLibPath: Windows %AppData%\XIVLauncher\addon\Hooks\dev\, Linux $HOME/.xlcore/dalamud/Hooks/dev/, macOS ~/Library/Application Support/XIV on Mac/dalamud/Hooks/dev/, overridden by $DALAMUD_HOME. ImplicitUsings is NOT enabled (write using System; etc.).

Evidence: ~/.nuget/packages/dalamud.net.sdk/15.0.0/Sdk/Sdk.props, SdkPackageVersions.props, Sdk.targets

### 3. The probe at /tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/probes/dalamud-api (Probe.csproj + Plugin.cs, Configuration.cs, OverlayWindow.cs, ConfigWindow.cs, GameStateProbe.cs, DataProbe.cs, ChatProbe.cs, HookProbe.cs; 1086 lines) compiles from clean with `dotnet build -c Release`: Build succeeded, 0 Warning(s), 0 Error(s). It exercises every API in sections a-l; it is the appendix source for the report.

Evidence: clean rebuild after rm -rf bin obj; output bin/Release/MitCueProbe.dll

### 4. (a) Entry point: `public sealed class Plugin : IDalamudPlugin` where `public interface IDalamudPlugin : IDisposable { }` (Dalamud.Plugin). Alternative `IAsyncDalamudPlugin : IAsyncDisposable { Task LoadAsync(CancellationToken cancellationToken); }`. Constructor may be parameterless or take service-typed parameters; `public void Dispose()` cleans up.

Evidence: Dalamud/Plugin/IDalamudPlugin.cs, IAsyncDalamudPlugin.cs; compiled

### 5. (a) [PluginService] = Dalamud.IoC.PluginServiceAttribute ([AttributeUsage(AttributeTargets.Property)], no args). Injected into static AND instance properties of any visibility on the plugin type (GetProperties(Static|Instance|Public|NonPublic)), BEFORE the constructor body runs (object created uninitialized, InjectProperties, then ctor). Only the plugin type is scanned; other classes get services via ctor args or PluginInterface.Inject(obj)/Create<T>(). Pattern: `[PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;`

Evidence: Dalamud/IoC/PluginServiceAttribute.cs; Dalamud/IoC/Internal/ServiceContainer.cs:125-150 (ctor lookup, InjectProperties at :138, ctor invoke :145-150), :173-178; compiled

### 6. (a) IDalamudPluginInterface members: `IUiBuilder UiBuilder { get; }` (type IUiBuilder), `DirectoryInfo ConfigDirectory` (= <dalamud config dir>/pluginConfigs/<InternalName>), `FileInfo ConfigFile` (= pluginConfigs/<InternalName>.json), `FileInfo AssemblyLocation` (loaded DLL), `string GetPluginConfigDirectory()` (creates dir), `void SavePluginConfig(IPluginConfiguration? currentConfig)` (null ignored), `IPluginConfiguration? GetPluginConfig()` (null if no file), `string InternalName`, `bool IsDev`, `bool IsTesting`, `T? Create<T>(params object[])`, `bool Inject(object, params object[])`, `ICallGateSubscriber<...,TRet> GetIpcSubscriber<...>(string name)`, GetOrCreateData/TryGetData. The concrete `Dalamud.Plugin.DalamudPluginInterface` is internal (CS0122).

Evidence: Dalamud/Plugin/IDalamudPluginInterface.cs:129,134,139,144,306,312; DalamudPluginInterface.cs:143-149,341-374; PluginManager.cs:120; PluginConfigurations.cs:92-103,143,150; compiled; negative N21

### 7. (a) IUiBuilder events/switches: `event Action? Draw;` `event Action? OpenConfigUi;` `event Action? OpenMainUi;` `event Action? ShowUi;` `event Action? HideUi;` `bool DisableAutomaticUiHide/DisableUserUiHide/DisableCutsceneUiHide/DisableGposeUiHide { get; set; }`. Usage: `PluginInterface.UiBuilder.Draw += DrawUi; ...OpenConfigUi += ToggleConfigUi; ...OpenMainUi += ToggleMainUi;`. Draw is skipped while the game UI is hidden / in cutscenes / in gpose (per user settings) unless the Disable*UiHide flag is set; if a Draw handler throws, Dalamud shows an error window and stops calling Draw until the user clicks 'Attempt to retry'.

Evidence: Dalamud/Interface/UiBuilder.cs:30-51 (interface), :739-794 (OnDraw: hide logic :747-761, exception :777-786); compiled

### 8. (a) Dev-plugin validator flags: no OpenConfigUi, no OpenMainUi, a ShowInHelp command without HelpMessage, empty Tags, single-line Description, missing Punchline/Name/Author, outdated API level.

Evidence: Dalamud/Plugin/Internal/PluginValidator.cs:67-96

### 9. (a) Config: `public interface IPluginConfiguration { int Version { get; set; } }` (Dalamud.Configuration). Pattern: `Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration(); PluginInterface.SavePluginConfig(Configuration);`. Serializer is Newtonsoft.Json: save = TypeNameHandling.Objects + TypeNameAssemblyFormatHandling.Simple, indented, via ReliableFileStorage; load = JsonConvert.DeserializeObject<T> without type handling. GetPluginConfig scans the CALLING assembly and uses the FIRST type assignable to IPluginConfiguration, so keep exactly one. Use Newtonsoft attributes ([JsonIgnore], [JsonProperty]).

Evidence: Dalamud/Configuration/IPluginConfiguration.cs; PluginConfigurations.cs:39-43,119-135,157-164; DalamudPluginInterface.cs:350-371; compiled

### 10. (a) Config gotcha: with Dalamud's exact serializer settings, `public List<string> EnabledJobs { get; set; } = ["WHM","SCH"];` comes back as [WHM,SCH,WHM,SCH] after one save/load (Newtonsoft reuses the initialised list); `[JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]` keeps [WHM,SCH]. Apply Replace to every collection with non-empty defaults (MitCue's default job/role/plan mappings) or initialise empty and fill defaults only for a fresh config. System.Numerics.Vector4 round-trips as {X,Y,Z,W}.

Evidence: Runtime test with Dalamud's own Newtonsoft.Json.dll: /tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/tools/jsontest (output: 'Default list after round trip: [WHM,SCH,WHM,SCH]', 'Replace list after round trip: [WHM,SCH]')

### 11. (a) WindowSystem (Dalamud.Interface.Windowing): `public WindowSystem(string? imNamespace = null)`, `void AddWindow(IWindow window)` (throws ArgumentException 'A window with this name/ID already exists.'), `void RemoveWindow(IWindow)`, `void RemoveAllWindows()`, `void Draw()` (call from UiBuilder.Draw), `IReadOnlyList<IWindow> Windows`, `bool HasAnyFocus`.

Evidence: Dalamud/Interface/Windowing/WindowSystem.cs (AddWindow throw at :96); compiled

### 12. (a) Window base class: `protected Window(string name, ImGuiWindowFlags flags = ImGuiWindowFlags.None, bool forceMainWindow = false)` and `protected Window(string name)`; `public abstract void Draw()` (body only, no Begin/End); virtual `PreOpenCheck()`, `bool DrawConditions()`, `PreDraw()`, `PostDraw()`, `OnOpen()`, `OnClose()`, `OnSafeToRemove()`, `Update()`; `void Toggle()`, `void BringToFront()`. Properties: `string WindowName`, `string? Namespace`, `bool IsOpen`, `ImGuiWindowFlags Flags`, `bool ForceMainWindow`, `float? BgAlpha`, `Vector2? Position` + `ImGuiCond PositionCondition`, `Vector2? Size` + `ImGuiCond SizeCondition`, `WindowSizeConstraints? SizeConstraints` (struct with `Vector2 MinimumSize`, `Vector2 MaximumSize`), `bool? Collapsed` + `CollapsedCondition`, `bool RespectCloseHotkey`(true), `ShowCloseButton`(true), `AllowPinning`(true), `AllowClickthrough`(true), `AllowBackgroundBlur`(true), `IsPinned`, `IsClickthrough`, `IsTopMost`, `InhibitAtkCollision`(true), `DisableWindowSounds`(false), `OnOpenSfxId`(23), `OnCloseSfxId`(24), `DisableFadeInFadeOut`(false), `List<TitleBarButton> TitleBarButtons`, `IsFocused`, `IsHovered`, `RequestFocus`.

Evidence: Dalamud/Interface/Windowing/Window.cs:20-198, IWindow.cs, WindowSizeConstraints.cs; reflection dump; compiled

### 13. (a) Per-frame order (WindowHost.DrawInternal): PreOpenCheck -> (closed: OnClose once, return) -> Update -> DrawConditions (false = skip, no OnClose) -> OnOpen on transition -> PreDraw (:222) -> ApplyConditionals: Position (+main viewport pos if ForceMainWindow, :629-630), Size*GlobalScale (:637), SizeConstraints*GlobalScale (:648-650), BgAlpha -> ImGui.SetNextWindowBgAlpha (:653-667) -> Flags read (:271) -> ImGui.Begin (:310) -> Draw() only if Begin returned true (:358) -> ImGui.End (:570) -> PostDraw (:614). Consequences: assign `this.Flags` in PreDraw to toggle click-through; BgAlpha is moot with NoBackground (:301-304). IsPinned or IsClickthrough adds NoMove|NoResize, and IsClickthrough additionally adds NoInputs|NoNav|NoCollapse|NoScrollWithMouse|NoMouseInputs (:273-281); both are user title-bar toggles persisted per window ID (PreHandlePreset :682-705) and their getters are gated by AllowPinning/AllowClickthrough, so set both false for a self-managed overlay. Close button hidden if !ShowCloseButton or click-through (:129); ESC closes focused windows if RespectCloseHotkey (:547-548). Exceptions in Draw switch the window to an error view (:360-365).

Evidence: Dalamud/Interface/Windowing/WindowHost.cs line refs given

### 14. (a) Compiled click-through overlay recipe: flags `ImGuiWindowFlags.NoDecoration | NoInputs | NoBackground | NoFocusOnAppearing | NoNav | NoSavedSettings | AlwaysAutoResize | NoDocking`; ctor `base("MitCue Overlay###MitCueOverlay", ClickThroughFlags, forceMainWindow: true)`; set IsOpen=true, RespectCloseHotkey=false, ShowCloseButton=false, AllowPinning=false, AllowClickthrough=false, DisableWindowSounds=true, BgAlpha=0.35f, Position + PositionCondition=ImGuiCond.FirstUseEver, SizeConstraints; `public override bool DrawConditions() => Plugin.ClientState.IsLoggedIn;` `public override void PreDraw() => this.Flags = cfg.OverlayClickThrough ? ClickThroughFlags : MovableFlags;`. With NoSavedSettings, persist ImGui.GetWindowPos() in MitCue config when unlocked and feed it back via Position.

Evidence: probe OverlayWindow.cs; compiled

### 15. (a) Commands (Dalamud.Game.Command): `public sealed class CommandInfo : IReadOnlyCommandInfo { public CommandInfo(IReadOnlyCommandInfo.HandlerDelegate handler); string HelpMessage {get;set;} bool ShowInHelp {get;set;} int DisplayOrder {get;set;} bool AllowedInMacros {get;set;} }`, `delegate void HandlerDelegate(string command, string arguments)`. `bool ICommandManager.AddHandler(string command, CommandInfo info)` returns false if already registered; `bool RemoveHandler(string command)`. Example: `CommandManager.AddHandler("/mitcue", new CommandInfo(OnCommand) { HelpMessage = "Toggle the MitCue overlay.", ShowInHelp = true });` with `void OnCommand(string command, string args)`. The plugin-scoped manager removes the plugin's commands on unload, but remove explicitly in Dispose.

Evidence: Dalamud/Game/Command/CommandInfo.cs; CommandManager.cs:174-192,316-326,337-346; compiled

### 16. (a)(b) Lifecycle threads: the plugin constructor is invoked via Task.Factory.StartNew(..., TaskCreationOptions.LongRunning, TaskScheduler.Default) -> NOT the game main thread, even with LoadSync (LoadSync only moves the create call). Do not read IObjectTable/textures in the ctor (main-thread asserts throw); use `_ = Framework.RunOnFrameworkThread(() => ...)`. IDalamudPlugin.Dispose runs via framework.RunOnFrameworkThread unless manifest CanUnloadAsync=true. Plugin-scoped services drop event subscriptions on unload and leaked hooks are disposed with a warning, but unsubscribe/dispose explicitly.

Evidence: ServiceContainer.cs:145-150; ServiceScope.cs:82-88; LocalPlugin.cs:641-642,782-787; ClientState.cs:545-557; GameInteropProviderPluginScoped.cs:98-110

### 17. (b) IFramework.Update runs on the game main thread: Dalamud hooks CSFramework Tick, RunFrameworkTick calls ThreadSafety.MarkMainThread() and then raises Update. `delegate void OnUpdateDelegate(IFramework framework); event OnUpdateDelegate Update;`

Evidence: Dalamud/Game/Framework.cs:63 (hook), :418-429 (MarkMainThread), :483 (ProfileAndInvoke(this.Update, this)); Dalamud/Plugin/Services/IFramework.cs

### 18. (b) UiBuilder.Draw / Window.Draw run on the game main thread inside the IDXGISwapChain::Present detour, so reading IObjectTable/ICondition/IClientState/IPartyList/IJobGauges and textures from Draw is allowed. Evidence is source-level and indirect: InterfaceManager.IsMainThreadInPresent ('whether the main thread is executing DxgiSwapChainPresentDetour'), RenderDalamudDraw -> Draw?.Invoke; Dalamud's own Data window ObjectTableWidget.Draw indexes the object table during the ImGui frame although the indexer hard-asserts the main thread; TextureManager treats 'in Present && ThreadSafety.IsMainThread' as normal. Probe logs a warning if ThreadSafety.IsMainThread is false in Draw (one-time in-game check recommended).

Evidence: InterfaceManager.cs:252-254,670-690,1289; UiBuilder.cs:739-779; Interface/Internal/Windows/Data/Widgets/ObjectTableWidget.cs:50,81 + ObjectTable.cs:89; Textures/Internal/TextureManager.cs:403

### 19. (b) Main-thread assertions (Dalamud.Utility.ThreadSafety: public `bool IsMainThread`, `AssertMainThread(string? message = null)`, `AssertNotMainThread()`, `DebugAssertMainThread()`): HARD assert (throws InvalidOperationException 'Not on main thread!') on every IObjectTable accessor incl. LocalPlayer, indexer, enumeration, SearchById, SearchByEntityId, GetObjectAddress, CreateObjectReference, Address; on StatusList; ISharedImmediateTexture.GetWrapOrEmpty/TryGetWrap 'Calling outside the main thread will fail'. DEV-MODE-ONLY assert on IGameObject/ICharacter/IBattleChara/IPartyMember/IStatus member reads (`ThreadSafety.DevModeAssertMainThread`, internal: throws when DalamudConfiguration.DevMode is true, i.e. whenever 'Enable Developer Mode' is ticked, which is also required to load a dev plugin; it carries `[Api16ToDo("Replace all calls with AssertMainThread")]`, so these become hard asserts in API 16 — V7). NO assert: ICondition indexer (raw memory read), IClientState.TerritoryType (cached) and IsLoggedIn (AgentLobby), IPlayerState.

Evidence: ObjectTable.cs:54,89,98,115,132,140,238; StatusList.cs:50; GameObject.cs:282; Character.cs:230; BattleChara.cs:110; PartyMember.cs:176; Status.cs:92; Condition.cs:56-65; ISharedImmediateTexture.cs:22-24; Utility/ThreadSafety.cs

### 20. (b) Event sources: ICondition.ConditionChange raised from Framework.Update; IClientState.Login from Framework.Update; IClientState.ZoneInit/TerritoryChanged/ClassJobChanged/LevelChanged raised from the UIModule.HandlePacket detour; IDutyState events from the PacketDispatcher.HandleActorControlPacket detour (IsDutyStarted also corrected in Framework.Update); IChatGui.ChatMessage from the RaptureLogModule.PrintMessage detour; IChatGui.LogMessage from the RaptureLogModule.Update detour. Dalamud does not assert the thread for detour-raised events (game packet processing; main thread in practice but not proven here).

Evidence: Condition.cs:151-173; ClientState.cs:69,275-330,391; DutyState.cs:78-174,226-243; ChatGui.cs:67,70,385-466,524-566

### 21. (b) Marshalling helpers on IFramework: `Task RunOnFrameworkThread(Action action)`, `Task<T> RunOnFrameworkThread<T>(Func<T> func)`, `Task RunOnTick(Action action, TimeSpan delay = default, int delayTicks = default, CancellationToken cancellationToken = default)` (+Func overloads), `Task Run(Action action, CancellationToken cancellationToken = default)` (+Func<T>, Func<Task>, Func<Task<T>>), `Task DelayTicks(long numTicks, CancellationToken = default)`, `bool IsInFrameworkUpdateThread`, `TimeSpan UpdateDelta`, `DateTime LastUpdate`, `DateTime LastUpdateUTC`. RunOnFrameworkThread(Func<Task>) overloads are [Obsolete] (use RunOnTick). Recommended MitCue architecture: sample state once per tick in Framework.Update into a plain snapshot, render the snapshot in Draw.

Evidence: Dalamud/Plugin/Services/IFramework.cs; compiled (RunOnFrameworkThread, RunOnTick)

### 22. (c) IClientState: `uint TerritoryType {get;}`, `uint MapId`, `uint Instance`, `ClientLanguage ClientLanguage`, `bool IsLoggedIn`, `bool IsPvP`, `bool IsPvPExcludingDen`, `bool IsGPosing`, `bool IsClientIdle(out ConditionFlag blockingFlag)`, `bool IsClientIdle()`; events `event Action<uint> TerritoryChanged` (fires only on change, after ZoneInit), `event Action<ZoneInitEventArgs> ZoneInit` (ZoneInitEventArgs: RowRef<TerritoryType> TerritoryType, ushort Instance, RowRef<ContentFinderCondition> ContentFinderCondition, RowRef<Weather> Weather, IReadOnlyList<FestivalEntry> ActiveFestivals), `event Action<uint> MapIdChanged`, `event Action<uint> InstanceChanged`, `delegate void ClassJobChangeDelegate(uint classJobId); event ClassJobChangeDelegate? ClassJobChanged`, `delegate void LevelChangeDelegate(uint classJobId, uint level); event LevelChangeDelegate? LevelChanged` (any job's level), `event Action Login`, `delegate void LogoutDelegate(int type, int code); event LogoutDelegate Logout`, `event Action EnterPvP/LeavePvP`, `event Action<Lumina.Excel.Sheets.ContentFinderCondition> CfPop`. Old `(ushort t)` TerritoryChanged lambda fails (CS1678/CS1661).

Evidence: Dalamud/Plugin/Services/IClientState.cs; ClientState.cs:122-137,321-326; ZoneInitEventArgs.cs; compiled; negative N10

### 23. (c) IClientState has NO LocalPlayer and NO LocalContentId in API 15 (not obsolete: removed; CS1061). Local player = `IObjectTable.LocalPlayer` (`IPlayerCharacter?`, implemented as this[0] as IPlayerCharacter, not obsolete) or IPlayerState (ContentId, EntityId, ClassJob, levels).

Evidence: negative N01/N02 CS1061; ObjectTable.cs:64; compiled

### 24. (c) IObjectTable : IEnumerable<IGameObject>: `nint Address`, `int Length`, `IPlayerCharacter? LocalPlayer`, `IEnumerable<IBattleChara> PlayerObjects`, `IEnumerable<IGameObject> CharacterManagerObjects` [0,199], `ClientObjects` [200,448], `EventObjects` [449,488], `StandObjects` [489,628], `ReactionEventObjects` [629,728], `IGameObject? this[int index]`, `IGameObject? SearchById(ulong gameObjectId)` (compares GameObjectId), `IGameObject? SearchByEntityId(uint entityId)` (compares EntityId; 0 and 0xE0000000 return null), `nint GetObjectAddress(int index)`, `IGameObject? CreateObjectReference(nint address)`. SearchById(5u) compiles (uint widens) but searches GameObjectId.

Evidence: Dalamud/Plugin/Services/IObjectTable.cs; ObjectTable.cs:95-125; compiled

### 25. (c) Wrapper-reuse gotcha: ObjectTable keeps pre-allocated wrapper objects per slot (CachedEntry) and re-points the same instance at whatever occupies the slot on every access (`activeObject.Address = (nint)address;`). A stored IGameObject silently changes identity when the slot is reused or dangles when emptied. Store EntityId/GameObjectId and re-resolve each frame.

Evidence: Dalamud/Game/ClientState/Objects/ObjectTable.cs:189-226 (line 224)

### 26. (c) IPlayerState exists (Dalamud.Plugin.Services): `bool IsLoaded`, `string CharacterName`, `uint EntityId`, `ulong ContentId`, `RowRef<ClassJob> ClassJob`, `short Level` (current job level, NOT synced), `bool IsLevelSynced`, `short EffectiveLevel` (synced level when synced), `short GetClassJobLevel(ClassJob)`, `RowRef<World> CurrentWorld/HomeWorld`, plus stats. Values are defaults while !IsLoaded; IsLoaded can be true while the player game object does not exist. For actions available under level sync use EffectiveLevel (or LocalPlayer.Level).

Evidence: Dalamud/Plugin/Services/IPlayerState.cs; Dalamud/Game/Player/PlayerState.cs:81,84,87; compiled

### 27. (c) IPartyList : IReadOnlyCollection<IPartyMember>: `int Length` (= GroupManager MainGroup.MemberCount), `IPartyMember? this[int index]`, `uint PartyLeaderIndex`, `bool IsAlliance`, `long PartyId`, address helpers. IPartyMember: `uint EntityId`, `[Obsolete("Renamed to EntityId")] uint ObjectId`, `ulong ContentId`, `SeString Name` (.TextValue), `RowRef<Lumina.Excel.Sheets.ClassJob> ClassJob`, `byte Level`, `IGameObject? GameObject` (ObjectTable.SearchById(EntityId)), `uint CurrentHP/MaxHP`, `ushort CurrentMP/MaxMP`, `StatusList Statuses`, `Vector3 Position`, `RowRef<TerritoryType> Territory`, `RowRef<World> World`, `byte Sex`, `nint Address`. PartyMember is a struct over a raw address: re-enumerate each frame.

Evidence: Dalamud/Plugin/Services/IPartyList.cs; Party/PartyList.cs:37; Party/PartyMember.cs:20-202 (GameObject :137); compiled

### 28. (c) ICondition: `bool this[ConditionFlag flag]`, `bool this[int flag]`, `delegate void ConditionChangeDelegate(ConditionFlag flag, bool value); event ConditionChangeDelegate? ConditionChange`, `bool Any()`, `bool Any(params ConditionFlag[] flags)`, `AnyExcept`, `OnlyAny`, `EqualTo`, `IReadOnlySet<ConditionFlag> AsReadOnlySet()`, `int MaxEntries`, `nint Address`. ConditionFlag (Dalamud.Game.ClientState.Conditions) values from DLL: InCombat=26, BoundByDuty=34, BoundByDuty56=56, BoundByDuty95=95, BetweenAreas=45, BetweenAreas51=51, WatchingCutscene=58, WatchingCutscene78=78, OccupiedInCutSceneEvent=35, Unconscious=2, InDutyQueue=91. Dalamud's own in-duty test: Any(BoundByDuty, BoundByDuty56, BoundByDuty95).

Evidence: Dalamud/Plugin/Services/ICondition.cs; Conditions/ConditionFlag.cs; DutyState.cs:245-248; reflection dump; compiled

### 29. (c) IDutyState: `delegate void DutyStartedDelegate(IDutyStateEventArgs args); event DutyStartedDelegate DutyStarted;` same shape for DutyWiped/DutyRecommenced/DutyCompleted; `bool IsDutyStarted`; `RowRef<ContentFinderCondition> ContentFinderCondition`. IDutyStateEventArgs (Dalamud.Game.DutyState): `RowRef<TerritoryType> TerritoryType`, `RowRef<ContentFinderCondition> ContentFinderCondition`, `uint EventHandlerId`. Semantics: DutyStarted = ActorControl category 0x6D arg 0x40000001 ('Duty Commenced', barrier drop; not boss pull; not on rejoin); DutyWiped 0x40000005; DutyRecommenced 0x40000006; DutyCompleted 0x40000002/0x40000003 once per territory; IsDutyStarted also set by fallback (bound by duty && in combat) and reset on territory change. Not a pull clock. Old `(object? sender, ushort territory)` handler fails (CS1593).

Evidence: Dalamud/Plugin/Services/IDutyState.cs; DutyState/DutyState.cs:78-174 (cases at :85,:107,:129,:151,:174), :215-243; DutyStateEventArgs.cs; compiled; negative N09

### 30. (c)(e) Ultimate IDs from current-patch CSVs: TOP TerritoryType 1122 -> ContentFinderCondition 908 'the Omega Protocol (Ultimate)', ClassJobLevelSync 90, ItemLevelSync 635; TEA 887 -> CFC 694, sync 80, ilvl 475; DMU 1363 -> CFC 1094 'Dancing Mad (Ultimate)', sync 100, ilvl 0. CFC.TerritoryType links back to the same territory.

Evidence: scratchpad/research/gamedata/TerritoryType.csv + ContentFinderCondition.csv (python csv lookup)

### 31. (d) IBattleChara (Dalamud.Game.ClientState.Objects.Types): `StatusList StatusList`, `bool IsCasting`, `bool IsCastInterruptible`, `byte CastActionType` (ClientStructs ActionType as byte; 1 = Action), `uint CastActionId`, `ulong CastTargetObjectId` (a GameObjectId), `float CurrentCastTime` (elapsed s), `float BaseCastTime` ('can only be a portion of the total cast for some actions'), `float TotalCastTime` (BaseCastTime + game adjustments such as Action offset 2B, 'used for display purposes'). No AdjustedTotalCastTime (CS1061). Remaining = TotalCastTime - CurrentCastTime; start ~ now - CurrentCastTime; detect a new cast when IsCasting rises or CastActionId changes per EntityId.

Evidence: Objects/Types/BattleChara.cs:9-101; reflection dump; compiled; negative N15

### 32. (d) Identifiers: IGameObject `SeString Name` (.TextValue), `ulong GameObjectId` (client-unique, targeting), `uint EntityId` (networked id; 0xE0000000 = not networked), `uint BaseId`, `[Obsolete("Renamed to BaseId")] uint DataId` (CS0618 warning), `uint OwnerId`, `ushort ObjectIndex`, `ObjectKind ObjectKind`, `byte SubKind`, `bool IsDead`, `bool IsTargetable`, `Vector3 Position`, `float Rotation`, `float HitboxRadius`, `ulong TargetObjectId`, `IGameObject? TargetObject`, `nint Address`, `bool IsValid()`, CurrentDistance/NextDistance (YalmDistanceX/Z obsolete). No IGameObject.ObjectId (CS1061). ICharacter: `uint NameId`, `byte Level`, `RowRef<ClassJob> ClassJob`, `uint CurrentHp/MaxHp/CurrentMp/MaxMp`, `byte ShieldPercentage`, `StatusFlags StatusFlags`. IBattleNpc: `BattleNpcSubKind BattleNpcKind`. IPlayerCharacter adds `RowRef<World> CurrentWorld/HomeWorld`.

Evidence: Objects/Types/GameObject.cs:13-139, Character.cs:88-101,162,179, BattleNpc.cs; reflection dump; compiled; negatives N05/N06/N16

### 33. (d) Enums (generated copies of ClientStructs enums, Dalamud.Game.ClientState.Objects.Enums): ObjectKind None 0, Pc 1, BattleNpc 2, EventNpc 3, Treasure 4, Aetheryte 5, GatheringPoint 6, EventObj 7, Mount 8, Companion 9, Retainer 10, AreaObject 11, HousingEventObject 12, Cutscene 13, ReactionEventObject 14, Ornament 15, CardStand 16, FollowMount 17. BattleNpcSubKind None 0, BNpcPart 1, Pet 2, Buddy 3, Player 4, Combatant 5, RaceChocobo 6, LovmMinion 7, NpcPartyMember 9. Boss filter: obj.ObjectKind == ObjectKind.BattleNpc && obj is IBattleNpc { BattleNpcKind: BattleNpcSubKind.Combatant }.

Evidence: Dalamud/EnumCloneMap.txt; reflection dump of Dalamud.dll; compiled

### 34. (d) StatusList (Dalamud.Game.ClientState.Statuses) : IReadOnlyCollection<IStatus>, `int Length`, `IStatus? this[int index]`, static CreateStatusListReference(nint). IStatus: `uint StatusId`, `RowRef<Lumina.Excel.Sheets.Status> GameData`, `ushort Param`, `float RemainingTime`, `uint SourceId`, `IGameObject? SourceObject`, `nint Address`.

Evidence: reflection dump; compiled

### 35. (d) Mapping BaseId <-> cactbot/ACT 'BNpcID' (BNpcBase row) and ICharacter.NameId <-> 'BNpcNameID' (BNpcName row) for TOP/TEA/DMU bosses. **(UNVERIFIED)**

Evidence: Naming consistent with cactbot docs/LogGuide.md line-261 fields (BNpcID, BNpcNameID); not observed in game

### 36. (e) `ExcelSheet<T> IDataManager.GetExcelSheet<T>(ClientLanguage? language = null, string? name = null) where T : struct, IExcelRow<T>` (+ GetSubrowExcelSheet<T> for subrow sheets; `ClientLanguage Language`). Dalamud.Game.ClientLanguage: Japanese 0, English 1, German 2, French 3. Row types are STRUCTS in Lumina.Excel.Sheets; Lumina.Excel.GeneratedSheets no longer exists (CS0234). Lumina.Excel.Sheets.Action clashes with System.Action (CS0104) -> `using LuminaAction = Lumina.Excel.Sheets.Action;`. Same CS0104 trap when FFXIVClientStructs namespaces are imported next to Dalamud/Lumina ones (verified by compiling): `ObjectKind` and `BattleNpcSubKind` exist in both `FFXIVClientStructs.FFXIV.Client.Game.Object` and `Dalamud.Game.ClientState.Objects.Enums`, and `Status` in both `FFXIVClientStructs.FFXIV.Client.Game` and `Lumina.Excel.Sheets` -> alias them (`using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;` etc.) (V8, added in verification).

Evidence: Dalamud/Plugin/Services/IDataManager.cs:55,73; Data/DataManager.cs:147-152; compiled; negatives N22, N31

### 37. (e) ExcelSheet<T> (Lumina.dll): `T GetRow(uint rowId)` (throws ArgumentOutOfRangeException if missing), `T? GetRowOrDefault(uint rowId)`, `bool TryGetRow(uint rowId, out T row)`, `bool HasRow(uint)`, `T this[uint rowId]`, `int Count`, enumerable. RowRef<T>: `uint RowId`, `bool IsValid`, `T Value` (throws InvalidOperationException if invalid), `T? ValueNullable`, `bool TryGetValue(out T)`. No `.Id`/`.GameData` on RowRef (CS1061).

Evidence: Lumina.xml docs; reflection dump; compiled; negatives N11/N12

### 38. (e) ReadOnlySeString -> string: `ExtractText()` (also `ExtractText(bool useSoftHyphen, string? macroPlaceholder = null)`); ToString() returned the same text in an off-game test, ToMacroString() returns macro form. Dalamud SeString (game object / party names) -> `.TextValue`. Dalamud.Utility.SeStringExtensions.ToDalamudString(this ReadOnlySeString) -> SeString.

Evidence: Lumina.xml; runtime test /tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/tools/sestest; reflection dump; compiled

### 39. (e) Lumina.Excel.Sheets.Action members: `uint RowId`, `ReadOnlySeString Name`, `ushort Icon`, `RowRef<ClassJob> ClassJob`, `byte ClassJobLevel`, `RowRef<ClassJobCategory> ClassJobCategory`, `ushort Recast100ms` (tenths of s), `ushort Cast100ms`, `byte MaxCharges`, `bool IsPlayerAction`, `bool IsRoleAction`, `bool IsPvP`, `RowRef<ActionCategory> ActionCategory`, `byte CooldownGroup`, `byte AdditionalCooldownGroup`, `sbyte Range`, `byte EffectRange`, `RowRef<Status> StatusGainSelf`, `bool CanTargetSelf/CanTargetParty/CanTargetAlly/CanTargetHostile`. ActionTransient (same row id): `ReadOnlySeString Description`. ActionCategory: `ReadOnlySeString Name`.

Evidence: reflection dump of Lumina.Excel.dll; compiled (DataProbe.cs)

### 40. (e) ClassJob: `ReadOnlySeString Name/Abbreviation/NameEnglish`, `RowRef<ClassJob> ClassJobParent`, `RowRef<ClassJobCategory> ClassJobCategory`, `byte Role`, `byte JobIndex`, `sbyte BattleClassIndex`. ClassJobCategory: `ReadOnlySeString Name` + one bool per abbreviation (ADV GLA PGL MRD LNC ARC CNJ THM CRP BSM ARM GSM LTW WVR ALC CUL MIN BTN FSH PLD MNK WAR DRG BRD WHM BLM ACN SMN SCH ROG NIN MCH DRK AST SAM RDM BLU GNB DNC RPR SGE VPR PCT) + Unknown0..2 (newest CSV names a BST column this Lumina build does not). Job-can-use test: action.ClassJobCategory.Value.<JOB> (test the job's own flag; only class-learned actions also set the base-class flag, e.g. Cure 120 / Medica 124 use CJC 53 'CNJ WHM', while job actions such as Plenary Indulgence use CJC 25 'WHM' only, and every SCH action incl. Physick 190 uses CJC 29 'SCH' only — 15 of 43 non-PvP WHM player actions set CNJ, so CNJ/ACN must not be used to infer WHM/SCH access) && IsPlayerAction && !IsPvP && ClassJobLevel <= synced level. [Corrected in verification, V2: the original said base-class flags CNJ/ACN are also set for WHM/SCH actions.]

Evidence: reflection dump; ClassJobCategory.csv header; compiled CanJobUse in DataProbe.cs

### 41. (e) TerritoryType: `ReadOnlySeString Name`, `RowRef<PlaceName> PlaceName`, `RowRef<ContentFinderCondition> ContentFinderCondition`, `RowRef<Map> Map`, `RowRef<TerritoryIntendedUse> TerritoryIntendedUse`, `bool IsPvpZone`. ContentFinderCondition: `ReadOnlySeString Name`, `RowRef<TerritoryType> TerritoryType`, `byte ClassJobLevelRequired`, `byte ClassJobLevelSync`, `ushort ItemLevelRequired`, `ushort ItemLevelSync`, `bool HighEndDuty`, `RowRef<ContentType> ContentType`.

Evidence: reflection dump; compiled GetDutyForTerritory in DataProbe.cs

### 42. (e) Data rows (current CSVs): ClassJob CNJ 6, WHM 24 (parent 6), ACN 26, SCH 28 (parent 26), AST 33, SGE 40. Plenary Indulgence 7433 (Icon 2639, ClassJob 24, Lv70, ClassJobCategory 25 = WHM only, Recast100ms 600, ActionCategory 4); Temperance 16536 (Lv80, 1200); Liturgy of the Bell 25862 (Lv90, 1800); Sacred Soil 188 (ClassJob 28, CJC 29 = SCH, 300); Expedient 25868 (Lv90, 1200); Lucid Dreaming 7562 (ClassJob -1, IsRoleAction True, CJC 120 incl. CNJ/WHM/ACN/SCH/AST/SGE). MaxCharges is the sheet base value (0 = no charges; Divine Benison 7432 shows 1 though a trait raises it) -> real charges via ActionManager.GetMaxCharges.

Evidence: Action.csv, ClassJob.csv, ClassJobCategory.csv python lookups

### 43. (f) Textures: ITextureProvider `ISharedImmediateTexture GetFromGameIcon(in GameIconLookup lookup)`, `bool TryGetFromGameIcon(in GameIconLookup lookup, out ISharedImmediateTexture texture)`, `string GetIconPath(in GameIconLookup)`. GameIconLookup(uint iconId, bool itemHq = false, bool hiRes = true, ClientLanguage? language = null) with implicit conversion from uint/int. ISharedImmediateTexture: `IDalamudTextureWrap GetWrapOrEmpty()`, `GetWrapOrDefault(IDalamudTextureWrap? defaultWrap = null)`, `bool TryGetWrap(out IDalamudTextureWrap? texture, out Exception? exception)`, `Task<IDalamudTextureWrap> RentAsync(CancellationToken = default)`. IDalamudTextureWrap: `ImTextureID Handle`, `int Width`, `int Height`, `Vector2 Size`. Usage: `var wrap = TextureProvider.GetFromGameIcon(new GameIconLookup(row.Icon)).GetWrapOrEmpty(); ImGui.Image(wrap.Handle, ImGuiHelpers.ScaledVector2(32, 32));` Call every frame in Draw, never cache the wrap across frames, never dispose it (ignored), main thread only. ImGuiHandle and GetIcon are gone (CS1061); ImGui.Image(nint) fails (CS1503).

Evidence: Dalamud/Plugin/Services/ITextureProvider.cs; Interface/Textures/ISharedImmediateTexture.cs:13-60; reflection dump (op_Implicit); compiled OverlayWindow.cs; negatives N03/N08/N13

### 44. (g) ImGui namespace is `Dalamud.Bindings.ImGui` (static class ImGui; `using ImGuiNET;` -> CS0246). String params are `ImU8String`, a ref struct + interpolated-string handler with implicit conversions from string, ReadOnlySpan<char>, ReadOnlySpan<byte>, byte[], char[], Span/Memory variants, byte*, char*; interpolation formats straight to UTF-8 (512-byte stack buffer, AllocFreeBufferSize). ImU8String cannot be a field of a class or ordinary struct (CS8345); it is only allowed as an instance field of a `ref struct` (V6). Helpers: Dalamud.Interface.Utility.ImGuiHelpers (GlobalScale, ScaledVector2, ScaledDummy, ForceNextWindowMainViewport, MainViewport, SetNextWindowPosRelativeMainViewport).

Evidence: reflection dump; imgui/Dalamud.Bindings.ImGui/ImU8String.cs:13; compiled; negatives N20, N30

### 45. (g) Signatures: `bool Begin(ImU8String name, ref bool open, ImGuiWindowFlags flags = 0)`, `bool Begin(ImU8String name, ImGuiWindowFlags flags = 0)`, `void End()` (always call); `void Image(ImTextureID userTextureId, Vector2 size)` + overloads adding uv0, uv1, Vector4 tintCol, Vector4 borderCol (and (id,size,tint)); `void ProgressBar(float fraction, Vector2 sizeArg, ImU8String overlay = default)`, `void ProgressBar(float fraction, ImU8String overlay = default)` (fill colour ImGuiCol.PlotHistogram); `PushStyleColor(ImGuiCol idx, Vector4 col)`, `PushStyleColor(ImGuiCol idx, uint col)`, `PopStyleColor()`, `PopStyleColor(int count)`; `TextColored(in Vector4 col, ImU8String text)`, `TextColored(uint col, ImU8String text)`; `uint ColorConvertFloat4ToU32(Vector4)`; `string GetClipboardText()`, `void SetClipboardText(ImU8String text)`; `Text/TextUnformatted/TextWrapped/TextDisabled(ImU8String)`; `PushStyleVar(ImGuiStyleVar, float|Vector2)`, `PopStyleVar()`/`PopStyleVar(int)`; `SetNextWindowPos(Vector2 pos, ImGuiCond cond[, Vector2 pivot])`, `SetNextWindowSize(Vector2, ImGuiCond)`, `SetNextWindowBgAlpha(float)`; `ImDrawListPtr GetWindowDrawList()`, `GetForegroundDrawList()`.

Evidence: reflection dump of Dalamud.Bindings.ImGui.dll; compiled OverlayWindow.cs/ConfigWindow.cs

### 46. (g) Text input: `bool InputText(ImU8String label, ref string buf, int maxLength = 512, ImGuiInputTextFlags flags = 0, ImGuiInputTextCallbackDelegate? callback = null)` and `InputText(label, Span<byte> buf, flags = 0, callback = null)`; `bool InputTextMultiline(ImU8String label, ref string buf, int maxLength = 512, Vector2 size = default, ImGuiInputTextFlags flags = 0, callback = null)` and `(label, Span<byte> buf, Vector2 size = default, flags = 0, callback = null)`; `InputTextWithHint(label, ImU8String hint, ref string buf, int maxLength = 512, ...)`. maxLength is an INT UTF-8 BYTE count defaulting to 512 (uint argument fails CS1503); longer text is truncated. The string overload re-encodes the whole string into a maxLength+1 buffer every frame -> for pasting a whole spreadsheet use a persistent byte[] with the Span<byte> overload (compiled) or pass a large maxLength (compiled with 200_000).

Evidence: reflection dump; imgui/Dalamud.Bindings.ImGui/Custom/ImGui.Manual.cs:385-467 (string overload :431-449); compiled ConfigWindow.cs; negative N07

### 47. (g) Widgets: `Combo(ImU8String label, ref int currentItem, IReadOnlyList<string> items, int popupMaxHeightInItems = -1)`, `(label, ref int, ReadOnlySpan<string> items, int popupMaxHeightInItems = -1)`, `(label, ref int, ImU8String itemsSeparatedByZeros, int popupMaxHeightInItems = -1)`, generic `Combo<T>(label, ref int, IReadOnlyList<T> items, Func<T,string> toString, int popupMaxHeightInItems = -1)`; ImGui.NET-style `Combo(label, ref i, arr, count)` still compiles but the int means popup height. `BeginCombo(ImU8String label, ImU8String previewValue, ImGuiComboFlags flags = 0)`/`EndCombo()`. `bool Selectable(ImU8String label, bool selected = false, ImGuiSelectableFlags flags = 0, Vector2 size = default)` + ref bool variant. `DragFloat(label, ref float v, float vSpeed = 1, float vMin = 0, float vMax = 0, ImU8String format = default, ImGuiSliderFlags flags = 0)`; `SliderFloat(label, ref float v, float vMin = 0, float vMax = 0, ImU8String format = default, ImGuiSliderFlags flags = 0)`; `SliderInt(label, ref int v, int vMin = 0, int vMax = 0, ...)`; `InputInt(label, ref int data, int step = 0, int stepFast = 0, ...)`; `InputFloat(label, ref float data, float step = 0, float stepFast = 0, ...)`; `ColorEdit4(ImU8String label, ref Vector4 col, ImGuiColorEditFlags flags = 0)`, `ColorEdit3(label, ref Vector3, flags)`; `bool Checkbox(ImU8String label, ref bool v)`; `bool Button(ImU8String label, Vector2 size = default)`, `SmallButton`; `PushID(ImU8String|nint|nuint|void*)` (an int binds to nint), `PopID()`. ref arguments need locals, not properties.

Evidence: reflection dump; compiled ConfigWindow.cs; negative N17

### 48. (g) Tables/containers: `bool BeginTable(ImU8String strId, int column, ImGuiTableFlags flags = 0, Vector2 outerSize = default, float innerWidth = 0)`, `TableSetupColumn(ImU8String label, ImGuiTableColumnFlags flags = 0, float initWidthOrWeight = 0, uint userId = 0)`, `TableSetupScrollFreeze(int cols, int rows)`, `TableHeadersRow()`, `TableNextRow()`/`(ImGuiTableRowFlags)`/`(float minRowHeight)`, `bool TableNextColumn()`, `bool TableSetColumnIndex(int)`, `EndTable()` only if BeginTable returned true. `bool BeginChild(ImU8String strId, Vector2 size = default, bool border = false, ImGuiWindowFlags flags = 0)` + `EndChild()` always. Popups/tabs/tooltips: OpenPopup, BeginPopup, BeginPopupModal, EndPopup, BeginTabBar/BeginTabItem, SetTooltip(ImU8String), IsItemHovered(), IsItemDeactivatedAfterEdit(), CollapsingHeader. ImRaii (Dalamud.Interface.Utility.Raii): Table/Combo/TabBar/TabItem/Child/Tooltip/Popup/ListBox/Group return ref-struct disposables with implicit bool (`using var t = ImRaii.Table("id", 3, flags); if (t) {...}`); PushColor(ImGuiCol, Vector4|uint, bool condition = true), PushStyle, PushId(ImU8String|int|nint), Disabled(bool), ItemWidth, PushFont return class disposables.

Evidence: reflection dump; compiled ConfigWindow.cs

### 49. (g) Flag values (DLL): ImGuiWindowFlags NoTitleBar 1, NoResize 2, NoMove 4, NoScrollbar 8, NoScrollWithMouse 16, NoCollapse 32, AlwaysAutoResize 64, NoBackground 128, NoSavedSettings 256, NoMouseInputs 512, MenuBar 1024, NoFocusOnAppearing 4096, NoBringToFrontOnFocus 8192, NoNavInputs 262144, NoNavFocus 524288, NoDocking 2097152, composites NoNav 786432 (NoNavInputs|NoNavFocus), NoDecoration 43 (NoTitleBar|NoResize|NoScrollbar|NoCollapse), NoInputs 786944 (NoMouseInputs|NoNavInputs|NoNavFocus). ImGuiCond None 0, Always 1, Once 2, FirstUseEver 4, Appearing 8. ImGuiCol names unchanged (Text 0, WindowBg 2, FrameBg 7, Button 21, Header 24, PlotHistogram 42, TableRowBg 47 ...). ImGuiTableFlags include Borders, RowBg, ScrollY, SizingStretchProp, Resizable; ImGuiTableColumnFlags WidthFixed/WidthStretch.

Evidence: reflection dump of enums; compiled

### 50. (h) `T IJobGauges.Get<T>() where T : JobGaugeBase` (instance cached per type, reads live shared gauge memory -> check the current job first). WHMGauge: `byte Lily`, `byte BloodLily` (times nourished), `short LilyTimer` (ms to next lily). SCHGauge: `byte Aetherflow`, `byte FairyGauge`, `short SeraphTimer` (ms), `DismissedFairy DismissedFairy` (Eos 6, Selene 7). SGEGauge: `byte Addersgall`, `short AddersgallTimer` (0..20000 ms), `byte Addersting`, `bool Eukrasia`. ASTGauge: `CardType Card1/Card2/Card3`, `CardType DrawnCrownCard`, `DrawType ActiveDraw`, `[Obsolete] CardType[] DrawnCards`. CardType None 0, Balance 1, Bole 2, Arrow 3, Spear 4, Ewer 5, Spire 6, Lord 7, Lady 8; DrawType Astral 0, Umbral 1 (Dalamud.Game.ClientState.JobGauge.Enums).

Evidence: Dalamud/Plugin/Services/IJobGauges.cs; JobGauge/JobGauges.cs:34-44; JobGauge/Types/*.cs; reflection dump; compiled GameStateProbe.cs

### 51. Cooldowns (FFXIVClientStructs.FFXIV.Client.Game.ActionManager, unsafe, main thread): `static ActionManager* Instance()`, `uint GetAdjustedActionId(uint actionId)`, `float GetRecastTime(ActionType actionType, uint actionId)`, `float GetRecastTimeElapsed(ActionType, uint)`, `bool IsRecastTimerActive(ActionType, uint)`, `bool IsActionOffCooldown(ActionType, uint)`, `uint GetCurrentCharges(uint actionId)`, `static ushort GetMaxCharges(uint actionId, uint level)`, `uint GetActionStatus(ActionType, uint actionId, ulong targetId = 0xE0000000, bool checkRecastActive = true, bool checkCastingActive = true, uint* outOptExtraInfo = null)` (0 = usable), `int GetRecastGroup(int type, uint actionId)`, `RecastDetail* GetRecastGroupDetail(int recastGroup)` (RecastDetail {bool IsActive; uint ActionId; float Elapsed; float Total}). ActionType enum: Action = 1.

Evidence: reflection dump of FFXIVClientStructs.dll; compiled ReadCooldowns in GameStateProbe.cs

### 52. (i) IGameInteropProvider: `Hook<T> HookFromAddress<T>(nint procAddress, T detour, HookBackend backend = HookBackend.Automatic) where T : Delegate` (+ nuint and void* overloads), `Hook<T> HookFromSignature<T>(string signature, T detour, HookBackend backend = Automatic)` (= HookFromAddress(scanner.ScanText(signature)); throws KeyNotFoundException if not found), `HookFromSymbol<T>(string moduleName, string exportName, T detour, ...)`, `HookFromFunctionPointerVariable<T>(nint address, T detour)`, `HookFromImport<T>(ProcessModule? module, string moduleName, string functionName, uint hintOrOrdinal, T detour)`, `void InitializeFromAttributes(object self)` (Dalamud.Utility.Signatures.SignatureAttribute(string signature) {DetourName, Offset, ScanType, UseFlags, Fallibility}). HookBackend.Reloaded/MinHook are [Obsolete] -> pass no backend. Hook<T> (Dalamud.Hooking, abstract class): `T Original`, `T OriginalDisposeSafe` (usable after Dispose), `Enable()`, `Disable()`, `Dispose()`, `nint Address`, `bool IsEnabled`, `bool IsDisposed`, `string BackendName`. (`Guid HookId` exists but is `protected` (Dalamud/Hooking/Hook.cs:77): `hook.HookId` from plugin code fails with CS0122 — corrected in verification, V3.) Hooks are created disabled (ReloadedHook ctor calls Activate() then Disable()). `Hook<T>.FromAddress` is not public (CS0117).

Evidence: Dalamud/Plugin/Services/IGameInteropProvider.cs:15-35,80; Hooking/Internal/GameInteropProviderPluginScoped.cs:94-95,113-121; Game/SigScanner.cs:126; Hooking/Hook.cs; reflection dump; compiled; negative N14

### 53. (i) Read-only observer hooks compiled (same functions BossMod hooks): `Plugin.GameInterop.HookFromAddress<ActionEffectHandler.Delegates.Receive>(ActionEffectHandler.Addresses.Receive.Value, ActionEffectDetour)` with `void Receive(uint casterEntityId, Character* casterPtr, Vector3* targetPos, ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targetEntityIds)` (Header: uint ActionId, byte ActionType, byte NumTargets, uint GlobalSequence, float AnimationLock, GameObjectId AnimationTargetId ...; GameObjectId: ulong Id, uint ObjectId, byte Type, implicit to/from ulong); and `HookFromAddress<PacketDispatcher.Delegates.HandleActorCastPacket>((nint)PacketDispatcher.MemberFunctionPointers.HandleActorCastPacket, ActorCastDetour)` with `void (uint entityId, ActorCastPacket* packet)` (ActorCastPacket: uint ActionId, byte ActionType, float CastTime, uint TargetEntityId, bool Interruptible, ushort RotationInt, Span<ushort> PositionInt). Address objects are InteropGenerator.Runtime.Address {nint Value; string String (signature)}. Detours: try/catch around observation, always call hook.Original(...). Cast starts can also be polled via IBattleChara; ability resolution (cactbot Ability syncs) needs the ActionEffect hook.

Evidence: reflection dump; compiled HookProbe.cs; BossMod/Framework/ActionManagerEx.cs:83,105,747-765 and WorldStateGameSync.cs:67,128,1078-1081

### 54. (j) IChatGui API 15: `delegate void OnHandleableChatMessageDelegate(IHandleableChatMessage message); event OnHandleableChatMessageDelegate ChatMessage;` also `CheckMessageHandled` (same delegate), `ChatMessageHandled`/`ChatMessageUnhandled` (`delegate void OnChatMessageDelegate(IChatMessage message)`), `delegate void OnLogMessageDelegate(ILogMessage message); event OnLogMessageDelegate LogMessage;`. IChatMessage (Dalamud.Game.Chat): `XivChatType LogKind`, `XivChatRelationKind SourceKind`, `XivChatRelationKind TargetKind`, `ReadOnlySeString OriginalSender/OriginalMessage`, `SeString Sender/Message`, `int Timestamp`, `bool IsHandled`; IMutableChatMessage adds settable Sender/Message + SenderModified/MessageModified; IHandleableChatMessage adds `void PreventOriginal()`. The old `(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)` handler fails (CS1593). The message and ILogMessage objects are single shared instances reused for every line: copy values inside the handler, never store them.

Evidence: Dalamud/Plugin/Services/IChatGui.cs (dump); Game/Chat/ChatMessage.cs; Game/Gui/ChatGui.cs:62,528; compiled ChatProbe.cs; negative N04

### 55. (j) Countdown in chat: cactbot sample logs show the per-tick lines 'Battle commencing in 10 seconds!' / '... 5 seconds!' (no player name) and 'Engage!' with raw code 0039; the initial 'Battle commencing in 5 seconds! (Your Name)' line is code 00B9. Executing ClientStructs' own LogInfo getters: 0x0039 -> LogKind 57 (XivChatType.SystemMessage), no relation; 0x00B9 -> LogKind 57, TargetKind=LocalPlayer, SourceKind=None (bit layout: kind = low 7 bits, target = bits 7-10, source = bits 11-14). So filter `message.LogKind == XivChatType.SystemMessage` and match text `^Battle commencing in (\d+) seconds?!( \(.*\))?$` / `Engage!`; the self/party relation is in TargetKind, not SourceKind. XivChatType has no 185/313 member; the channel arrives already masked.

Evidence: cactbot docs/FAQ-Troubleshooting.md:200, docs/logs/TheAbyssalFractureExtreme.log:484,535,592; cactbot resources/translations.ts:16-33; runtime decode /tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/tools/logkind

### 56. (j) Language-independent countdown option A: IChatGui.LogMessage with LogMessage sheet rows (EN CSV): 5255 'Battle commencing in N second(s)! (Name)' (LogKind 57), 5256 'Engage!', 5257 'Countdown canceled by ...', variants 5260-5264 with other display flags. ILogMessage: `uint LogMessageId`, `RowRef<LogMessage> GameData`, `int ParameterCount`, `bool TryGetIntParameter(int index, out int value)`, `bool TryGetStringParameter(int index, out ReadOnlySeString value)`, `ILogMessageEntity? SourceEntity/TargetEntity` (Name, IsPlayer, HomeWorld), `void PreventOriginal()`. Which row ids actually fire for /countdown and which parameter index carries the seconds are not confirmed. **(UNVERIFIED)**

Evidence: /tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/research/gamedata-extra/LogMessage.csv rows 5255-5264; reflection dump; compiled OnLogMessage in ChatProbe.cs

### 57. (j) Language-independent countdown option B (BossMod's approach, compiled): `AgentCountDownSettingDialog* agent = AgentCountDownSettingDialog.Instance(); float? remaining = agent != null && agent->Active ? agent->TimeRemaining : null;` (FFXIVClientStructs.FFXIV.Client.UI.Agent; fields bool Active, float TimeRemaining, float PrevTimeRemaining, uint InitiatorId, bool ShowingCountdown). Poll in Framework.Update; combine with ConditionFlag.InCombat rising and the first boss cast/ActionEffect for pulls without countdown.

Evidence: reflection dump; BossMod/Framework/WorldStateGameSync.cs:773-776; compiled PollCountdown in ChatProbe.cs

### 58. (j) Output: `void IChatGui.Print(string message, string? messageTag = null, ushort? tagColor = null)` (+ SeString, ReadOnlySpan<byte>, XivChatEntry overloads), `void PrintError(string message, string? messageTag = null, ushort? tagColor = null)`. Notifications: `IActiveNotification INotificationManager.AddNotification(Notification notification)`; `Notification` (Dalamud.Interface.ImGuiNotification, record) props Title, Content, Type (NotificationType None 0/Success 1/Warning 2/Error 3/Info 4), InitialDuration, HardExpiry, Minimized, MinimizedText, Progress, UserDismissable, RespectUiHidden, Icon, IconTexture, static DefaultDuration; IActiveNotification adds long Id, CreatedAt, DismissNow(), ExtendBy(TimeSpan), events Click/Dismiss/DrawActions. Old Dalamud.Interface.Internal.Notifications namespace is gone (CS0234).

Evidence: reflection dump; compiled ChatProbe.Examples; negative N23

### 59. (k) IPluginLog: Verbose, Debug, Information, Info (alias), Warning, Error, Fatal each as `(string messageTemplate, params object[] values)` and `(Exception? exception, string messageTemplate, params object[] values)`; `void Write(Serilog.Events.LogEventLevel level, Exception? exception, string messageTemplate, params object[] values)`; `LogEventLevel MinimumLogLevel { get; set; }`; `Serilog.ILogger Logger { get; }`. Uses Serilog message templates ({Name}), not string.Format.

Evidence: reflection dump; compiled ChatProbe.Examples

### 60. (l) DalamudPackager runs AfterTargets=Build (Debug: manifest only, MakeZip=false; Release: MakeZip=true). Manifest source precedence: <AssemblyName>.json in project dir -> <AssemblyName>.yaml -> csproj properties. csproj -> manifest: Author, Name, Description, Punchline (all four REQUIRED, otherwise build error), Tags / CategoryTags / ImageUrls (';'-separated lists), RepoUrl, IconUrl, ApplicableVersion (default 'any'), DalamudApiLevel (default 15), MinimumDalamudVersion, LoadRequiredState (0), LoadSync (false), CanUnloadAsync (false), LoadPriority (0), Changelog, AcceptsFeedback (default true), FeedbackMessage. AssemblyVersion comes from the built DLL (4 components; VersionComponents attribute changes it) and InternalName = AssemblyName (keep stable: it names the config file and the repo key).

Evidence: ~/.nuget/packages/dalamudpackager/15.0.0/build/DalamudPackager.targets; DalamudPackager source (commit 827d88dc) DalamudPackager/DalamudPackager.cs:276-301,358-414,530-555; observed probe manifest

### 61. (l) Observed Release outputs: bin/Release/MitCueProbe.dll, MitCueProbe.deps.json, MitCueProbe.json (manifest beside the DLL, used for dev-plugin loading); bin/Release/MitCueProbe/MitCueProbe.json + bin/Release/MitCueProbe/latest.zip (contains MitCueProbe.deps.json, MitCueProbe.json, MitCueProbe.dll). The zip contains the whole output dir (Dalamud refs excluded by Private=false; extra NuGet DLLs would be included). Generated manifest keys: Author, Name, InternalName, AssemblyVersion ('0.1.2.3' from <Version>), Description, ApplicableVersion 'any', RepoUrl, Tags, CategoryTags, DalamudApiLevel 15, LoadRequiredState 0, LoadSync false, CanUnloadAsync false, LoadPriority 0, IconUrl, Punchline, Changelog, AcceptsFeedback true. Dev testing: /xlsettings > Experimental > tick 'Enable Developer Mode' first — the 'Dev Plugin Locations' entry is only shown while DevMode is on (SettingsTabExperimental.cs: `new DevPluginsSettingsEntry(visibility: () => this.devModeEntry.Value)`) — then 'Dev Plugin Locations' takes the DLL path; manifest = <dll name>.json beside it. DevMode also arms the dev-mode main-thread asserts of fact 19 (V7).

Evidence: ls/unzip of probe outputs; DevPluginsSettingsEntry.cs:38,117; LocalPluginManifest.cs:85

### 62. (l) Custom repo 'pluginmaster': a URL returning a JSON ARRAY deserialized with Newtonsoft as List<RemotePluginManifest>, added via /xlsettings > Experimental > 'Custom Plugin Repositories'. JSON fields (property names): Author, Name, InternalName, AssemblyVersion, Punchline, Description, Changelog, Tags[], CategoryTags[], RepoUrl, IconUrl, ImageUrls[], ApplicableVersion ('any'), MinimumDalamudVersion, DalamudApiLevel, TestingAssemblyVersion, TestingDalamudApiLevel, IsTestingExclusive, TestingChangelog, DownloadLinkInstall, DownloadLinkUpdate, DownloadLinkTesting, DownloadCount, LastUpdate (unix s), LoadRequiredState, LoadSync, LoadPriority, CanUnloadAsync, SupportsProfiles (default true), AcceptsFeedback, FeedbackMessage, IsHide, _Dip17Channel (JsonProperty name). Validation: InternalName and Name non-blank, AssemblyVersion present; third-party entries may not reuse an official-repo InternalName; ApplicableVersion older than the game -> ineligible; API level < 14 hidden; update candidates require DalamudApiLevel == 15 (or TestingDalamudApiLevel for testers). Dalamud 15.0.4.0 (commit 12e318c1) downloads DownloadLinkInstall (or DownloadLinkTesting) for BOTH install and update (PluginManager.cs:820 install and :1038 update both call DownloadPluginAsync, :1318); DownloadLinkUpdate is declared but unused (set it equal anyway). A sample array with these fields deserialized successfully through Dalamud's own internal RemotePluginManifest type.

Evidence: Plugin/Internal/Types/PluginManifest.cs, Manifest/RemotePluginManifest.cs, PluginRepository.cs:121,143,160,181-211; PluginManager.cs:1133,1136-1146,1767-1772,1318; SettingsTabExperimental.cs:114, ThirdRepoSettingsEntry.cs:66; runtime /tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/tools/pmtest/pluginmaster.json

### 63. (l) Dalamud manifests have NO dependency field: a plugin cannot declare or auto-install BossMod/cactbot. Cross-plugin access is only IPC (IDalamudPluginInterface.GetIpcSubscriber<...>(name) / GetIpcProvider) or data shares (GetOrCreateData/TryGetData), and must tolerate the other plugin being absent. Dalamud re-reads repos periodically (doc: 'Reloads happen at periodic intervals (10 minutes)').

Evidence: PluginManifest.cs full field list; IDalamudPluginInterface.cs (IPC methods, :361)

### 64. Negative probe (exact errors, Dalamud 15.0.4.0 / commit 12e318c1): N01 ClientState.LocalPlayer CS1061; N02 ClientState.LocalContentId CS1061; N03 wrap.ImGuiHandle CS1061; N04 5-arg ref ChatMessage lambda CS1593; N05 obj.ObjectId CS1061; N06 obj.DataId warning CS0618 'Renamed to BaseId'; N07 InputText(..., 100u) CS1615+CS1503; N08 ITextureProvider.GetIcon CS1061; N09 DutyStarted (object?, ushort) CS1593; N10 TerritoryChanged (ushort) CS1678+CS1661; N11/N12 RowRef.Id/.GameData CS1061; N13 ImGui.Image(nint) CS1503; N14 Hook<T>.FromAddress CS0117; N15 AdjustedTotalCastTime CS1061; N17 Combo(label, ref i, arr, 2) compiles (4th = popup height); N20 using ImGuiNET CS0246; N21 DalamudPluginInterface CS0122; N22 Lumina.Excel.GeneratedSheets CS0234; N23 Dalamud.Interface.Internal.Notifications CS0234; N30 ImU8String field CS8345; N31 Action ambiguity CS0104.

Evidence: /tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/probes/dalamud-api-negative/NegA.cs (+ ../neg-hold/NegB_decl.cs, NegC.cs, NegD.cs compiled one at a time because declaration-level errors suppress body errors)

## Open questions

- (Resolved.) The parent saved this report and appended the eight probe .cs files and Probe.csproj; the verifier confirmed the appendix is identical to the files in /tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/probes/dalamud-api and that a copy rebuilds with 0 errors / 0 warnings.
- Unverified code comment: GameStateProbe.cs says `Length is 0 when not in a party`; that is the same open question as the IPartyList bullet below, not a verified fact.
- In-game check: is ThreadSafety.IsMainThread true inside UiBuilder.Draw? The source strongly implies it; OverlayWindow.Draw in the probe already logs a warning if it is false.
- In-game check: which thread raises IClientState.ZoneInit / TerritoryChanged / ClassJobChanged / LevelChanged? They come from the UIModule.HandlePacket detour. Same question for custom hook detours (ActionEffect, ActorCast).
- Which LogMessage row ids fire for /countdown started by yourself vs by a party member (5255/5256/5257 vs the 5260-5264 variants), which row the 10s/5s tick lines use, and which TryGetIntParameter index holds the seconds.
- Does a countdown started by a party member arrive as LogKind 57 with TargetKind=PartyMember (raw 0x0139)? Only the self-started code 0x00B9 is documented (cactbot FAQ).
- AgentCountDownSettingDialog: how Active and TimeRemaining behave at zero, after a cancel, and when a countdown is restarted. BossMod relies on these fields, but they were not observed in game here.
- Is IPartyList.Length 0 when solo, and is the slot order stable within a duty?
- Does ActionManager.GetMaxCharges(actionId, 0) return the charge count at the current (synced) level?
- Do BaseId and NameId of the TOP/TEA/DMU bosses equal cactbot's BNpcID / BNpcNameID values?
- When exactly does IDutyState.DutyStarted fire in ultimates, relative to the barrier drop and the first boss cast?

## Appendix: probe sources

### Plugin.cs

```csharp
// (a) Plugin entry, service injection, UiBuilder events, config, WindowSystem, commands.
using System;
using System.IO;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace MitCueProbe;

public sealed class Plugin : IDalamudPlugin
{
    // [PluginService] works on static or instance properties of ANY visibility on the plugin class.
    // They are injected BEFORE the constructor body runs (ServiceContainer.CreateAsync).
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IJobGauges JobGauges { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInterop { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/mitcue";

    // WindowSystem(string? imNamespace = null): namespace is pushed as an ImGui ID around all windows.
    public readonly WindowSystem WindowSystem = new("MitCueProbe");

    internal Configuration Configuration { get; }
    private readonly OverlayWindow overlay;
    private readonly ConfigWindow configWindow;
    private readonly GameStateProbe gameState;
    private readonly ChatProbe chat;
    private readonly HookProbe hooks;

    public Plugin()
    {
        // GetPluginConfig() returns IPluginConfiguration? (null when no file yet).
        this.Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // Directory / file locations exposed by the plugin interface.
        DirectoryInfo cfgDir = PluginInterface.ConfigDirectory;    // .../pluginConfigs/<InternalName>
        FileInfo cfgFile = PluginInterface.ConfigFile;             // .../pluginConfigs/<InternalName>.json
        FileInfo dll = PluginInterface.AssemblyLocation;           // the loaded plugin dll
        string cfgDirString = PluginInterface.GetPluginConfigDirectory(); // creates the directory if missing
        Log.Information("cfgDir={0} cfgFile={1} dll={2} {3}", cfgDir.FullName, cfgFile.FullName, dll.FullName, cfgDirString);

        this.overlay = new OverlayWindow(this);
        this.configWindow = new ConfigWindow(this);
        this.WindowSystem.AddWindow(this.overlay);
        this.WindowSystem.AddWindow(this.configWindow);

        // CommandInfo(IReadOnlyCommandInfo.HandlerDelegate handler); HandlerDelegate = void (string command, string arguments)
        bool added = CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Toggle the MitCue overlay. '/mitcue config' opens settings.",
            ShowInHelp = true,
            DisplayOrder = 0,
        });
        Log.Debug("AddHandler returned {0}", added);

        // IUiBuilder events are plain System.Action events.
        PluginInterface.UiBuilder.Draw += this.DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += this.ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += this.ToggleMainUi;
        PluginInterface.UiBuilder.DisableCutsceneUiHide = false;

        this.gameState = new GameStateProbe();
        this.chat = new ChatProbe();
        this.hooks = new HookProbe();

        // The constructor does NOT run on the framework thread: marshal game-state reads.
        _ = Framework.RunOnFrameworkThread(() =>
        {
            var job = ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0;
            Log.Debug("job at load: {0}", job);
        });
        _ = Framework.RunOnTick(() => Log.Debug("one second later"), TimeSpan.FromSeconds(1));
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= this.DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= this.ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= this.ToggleMainUi;

        this.WindowSystem.RemoveAllWindows();
        this.hooks.Dispose();
        this.chat.Dispose();
        this.gameState.Dispose();

        bool removed = CommandManager.RemoveHandler(CommandName);
        Log.Debug("RemoveHandler returned {0}", removed);
    }

    public void SaveConfig() => PluginInterface.SavePluginConfig(this.Configuration);

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("config", StringComparison.OrdinalIgnoreCase))
            this.configWindow.Toggle();
        else
            this.overlay.Toggle();
    }

    private void DrawUi() => this.WindowSystem.Draw();

    public void ToggleConfigUi() => this.configWindow.Toggle();

    public void ToggleMainUi() => this.overlay.Toggle();
}
```

### Configuration.cs

```csharp
// (a) IPluginConfiguration. Serialized by Dalamud with Newtonsoft.Json (TypeNameHandling.Objects on save).
using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;
using Newtonsoft.Json;

namespace MitCueProbe;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    // The only interface member: int Version { get; set; }
    public int Version { get; set; } = 1;

    public bool OverlayClickThrough { get; set; } = true;
    public float OverlayBgAlpha { get; set; } = 0.35f;
    public Vector4 AccentColor { get; set; } = new(1f, 0.8f, 0.2f, 1f);
    public float LeadTimeSeconds { get; set; } = 8f;
    public string SelectedPlanId { get; set; } = string.Empty;

    // Newtonsoft reuses existing collection instances on deserialize (ObjectCreationHandling.Auto):
    // without Replace, default items would be duplicated after a save/load round trip.
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<string> EnabledJobs { get; set; } = ["WHM", "SCH"];

    // Ignored by the serializer.
    [JsonIgnore]
    public bool Dirty { get; set; }
}
```

### OverlayWindow.cs

```csharp
// (a) Window base class + (f) textures + (g) overlay-related ImGui calls.
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace MitCueProbe;

public sealed class OverlayWindow : Window
{
    private const ImGuiWindowFlags ClickThroughFlags =
        ImGuiWindowFlags.NoDecoration          // = NoTitleBar | NoResize | NoScrollbar | NoCollapse
        | ImGuiWindowFlags.NoInputs            // = NoMouseInputs | NoNavInputs | NoNavFocus
        | ImGuiWindowFlags.NoBackground
        | ImGuiWindowFlags.NoFocusOnAppearing
        | ImGuiWindowFlags.NoNav
        | ImGuiWindowFlags.NoSavedSettings
        | ImGuiWindowFlags.AlwaysAutoResize
        | ImGuiWindowFlags.NoDocking;

    private const ImGuiWindowFlags MovableFlags =
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize
        | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoCollapse;

    private readonly Plugin plugin;

    // protected Window(string name, ImGuiWindowFlags flags = ImGuiWindowFlags.None, bool forceMainWindow = false)
    public OverlayWindow(Plugin plugin)
        : base("MitCue Overlay###MitCueOverlay", ClickThroughFlags, forceMainWindow: true)
    {
        this.plugin = plugin;

        this.IsOpen = true;
        this.RespectCloseHotkey = false;    // do not close on ESC
        this.ShowCloseButton = false;
        this.AllowPinning = false;          // hide Dalamud's title-bar pin/clickthrough menu
        this.AllowClickthrough = false;
        this.DisableWindowSounds = true;
        this.DisableFadeInFadeOut = true;
        this.BgAlpha = 0.35f;               // float? -> ImGui.SetNextWindowBgAlpha before Begin
        this.Position = new Vector2(100, 300);
        this.PositionCondition = ImGuiCond.FirstUseEver;
        this.Size = new Vector2(320, 120);  // multiplied by ImGuiHelpers.GlobalScale
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(200, 60),
            MaximumSize = new Vector2(800, 600),
        };
        this.Collapsed = false;
        this.CollapsedCondition = ImGuiCond.Always;
    }

    // Called every frame before IsOpen is checked.
    public override void PreOpenCheck()
    {
    }

    // Extra visibility predicate; false => window not drawn this frame (no OnClose).
    public override bool DrawConditions() => Plugin.ClientState.IsLoggedIn;

    // Called before ImGui.Begin (after Update / DrawConditions). Good place to toggle Flags.
    public override void PreDraw()
    {
        this.Flags = this.plugin.Configuration.OverlayClickThrough ? ClickThroughFlags : MovableFlags;
        this.BgAlpha = this.plugin.Configuration.OverlayBgAlpha;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6, 6));
    }

    // Called after ImGui.End.
    public override void PostDraw()
    {
        ImGui.PopStyleVar();
    }

    public override void OnOpen() { }

    public override void OnClose() { }

    public override void Update() { }

    // Body only; WindowHost calls ImGui.Begin/End around it.
    public override void Draw()
    {
        // (f) icon: ITextureProvider.GetFromGameIcon(in GameIconLookup) -> ISharedImmediateTexture
        //     -> GetWrapOrEmpty() (per frame, never cache, never dispose) -> IDalamudTextureWrap.Handle (ImTextureID)
        uint iconId = 0;
        if (Plugin.DataManager.GetExcelSheet<LuminaAction>().TryGetRow(7433u, out var plenary)) // Plenary Indulgence
            iconId = plenary.Icon; // ushort widened to uint
        ISharedImmediateTexture shared = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId));
        IDalamudTextureWrap wrap = shared.GetWrapOrEmpty();
        var iconSize = ImGuiHelpers.ScaledVector2(32, 32);
        ImGui.Image(wrap.Handle, iconSize);
        ImGui.SameLine();

        // implicit uint -> GameIconLookup conversion also compiles
        if (Plugin.TextureProvider.GetFromGameIcon(iconId).TryGetWrap(out var maybeWrap, out _) && maybeWrap != null)
        {
            ImGui.Image(maybeWrap.Handle, iconSize, Vector2.Zero, Vector2.One, new Vector4(1, 1, 1, 0.5f));
            ImGui.SameLine();
        }

        // Text with an interpolated string (ImU8String handler, no string allocation)
        float remaining = 4.2f;
        ImGui.PushStyleColor(ImGuiCol.Text, this.plugin.Configuration.AccentColor);
        ImGui.TextUnformatted($"Plenary Indulgence in {remaining:0.0}s");
        ImGui.PopStyleColor();

        // ProgressBar(float fraction, Vector2 sizeArg, ImU8String overlay = default); fill color = ImGuiCol.PlotHistogram
        using (ImRaii.PushColor(ImGuiCol.PlotHistogram, new Vector4(0.2f, 0.8f, 0.3f, 1f)))
        {
            ImGui.ProgressBar(1f - (remaining / 8f), new Vector2(200 * ImGuiHelpers.GlobalScale, 6 * ImGuiHelpers.GlobalScale), string.Empty);
        }

        // uint color overload
        uint red = ImGui.ColorConvertFloat4ToU32(new Vector4(1, 0, 0, 1));
        ImGui.TextColored(red, "!");

        // Window draw list
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        dl.AddRectFilled(p, p + new Vector2(4, 4), red);

        // Draw runs on the framework (main) thread: ThreadSafety.IsMainThread is expected to be true here.
        if (!Dalamud.Utility.ThreadSafety.IsMainThread)
            Plugin.Log.Warning("Draw not on main thread");
    }

    // Alternative without WindowSystem: raw Begin/End inside UiBuilder.Draw.
    public static void DrawRawOverlay(bool clickThrough)
    {
        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(new Vector2(200, 200), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.3f);
        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing
                    | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoSavedSettings;
        if (clickThrough)
            flags |= ImGuiWindowFlags.NoInputs;
        if (ImGui.Begin("##MitCueRawOverlay", flags))
        {
            ImGui.TextUnformatted("raw overlay");
        }

        ImGui.End(); // End() must be called even when Begin() returns false
    }
}
```

### ConfigWindow.cs

```csharp
// (a) normal window + (g) ImGui widget surface used by a config / import UI.
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace MitCueProbe;

public sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;

    // Large paste buffer for TSV/CSV imports: the string overloads default to maxLength = 512 BYTES.
    private string importText = string.Empty;
    private readonly byte[] importBuffer = new byte[256 * 1024];
    private string searchText = string.Empty;
    private int selectedPlan;
    private int comboIndex;
    private bool showDebug;
    private float leadTime = 8f;
    private int offsetMs;
    private Vector4 color = new(1, 1, 1, 1);
    private readonly List<string> plans = ["TOP - WHM (Carry the Clear)", "TOP - SCH (LPDU)"];

    public ConfigWindow(Plugin plugin)
        : base("MitCue Settings###MitCueConfig")
    {
        this.plugin = plugin;
        this.Flags = ImGuiWindowFlags.NoCollapse;
        this.SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(420, 300), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
        this.Size = new Vector2(640, 480);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.RespectCloseHotkey = true; // default true: ESC closes it
    }

    public override void Draw()
    {
        var cfg = this.plugin.Configuration;

        // Checkbox(ImU8String label, ref bool v). Properties cannot be passed by ref: copy to a local.
        var clickThrough = cfg.OverlayClickThrough;
        if (ImGui.Checkbox("Click-through overlay", ref clickThrough))
        {
            cfg.OverlayClickThrough = clickThrough;
            this.plugin.SaveConfig();
        }

        // SliderFloat(label, ref float v, float vMin, float vMax, ImU8String format = default, ImGuiSliderFlags flags = 0)
        var alpha = cfg.OverlayBgAlpha;
        if (ImGui.SliderFloat("Background alpha", ref alpha, 0f, 1f, "%.2f"))
            cfg.OverlayBgAlpha = alpha;
        if (ImGui.IsItemDeactivatedAfterEdit())
            this.plugin.SaveConfig();

        // DragFloat(label, ref float v, float vSpeed = 1, float vMin = 0, float vMax = 0, ImU8String format = default, flags)
        ImGui.DragFloat("Lead time (s)", ref this.leadTime, 0.1f, 0f, 30f, "%.1f s");
        ImGui.SliderInt("Offset (ms)", ref this.offsetMs, -2000, 2000);
        ImGui.InputInt("Offset exact", ref this.offsetMs, 50, 500);

        // ColorEdit4(label, ref Vector4 col, ImGuiColorEditFlags flags = 0)
        if (ImGui.ColorEdit4("Accent", ref this.color, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar))
            cfg.AccentColor = this.color;

        // Combo overloads: IReadOnlyList<string>, ReadOnlySpan<string>, "a\0b\0" string, or generic with toString.
        ImGui.Combo("Plan", ref this.selectedPlan, this.plans);
        ImGui.Combo("Job", ref this.comboIndex, "WHM\0SCH\0AST\0SGE\0");

        // BeginCombo / Selectable / EndCombo
        if (ImGui.BeginCombo("Plan (manual)", this.plans[this.selectedPlan]))
        {
            for (var i = 0; i < this.plans.Count; i++)
            {
                if (ImGui.Selectable(this.plans[i], i == this.selectedPlan))
                    this.selectedPlan = i;
            }

            ImGui.EndCombo();
        }

        // ImRaii version (TableDisposable / ComboDisposable are ref structs with implicit bool)
        using (var combo = ImRaii.Combo("##planRaii", this.plans[this.selectedPlan]))
        {
            if (combo)
            {
                foreach (var name in this.plans)
                    ImGui.Selectable(name);
            }
        }

        // InputText(label, ref string buf, int maxLength = 512, flags, callback)
        ImGui.InputText("Search", ref this.searchText, 128);
        ImGui.InputTextWithHint("##hint", "Spreadsheet URL or id", ref this.searchText, 512);

        // InputTextMultiline(label, ref string buf, int maxLength = 512, Vector2 size = default, flags, callback)
        ImGui.InputTextMultiline("##import", ref this.importText, 200_000, new Vector2(-1, 120 * ImGuiHelpers.GlobalScale));

        // Span<byte> overload with a persistent buffer avoids re-encoding a huge string every frame.
        if (ImGui.InputTextMultiline("##importBytes", this.importBuffer, new Vector2(-1, 80), ImGuiInputTextFlags.AllowTabInput))
        {
            var len = Array.IndexOf(this.importBuffer, (byte)0);
            this.importText = Encoding.UTF8.GetString(this.importBuffer, 0, len < 0 ? this.importBuffer.Length : len);
        }

        // Clipboard: GetClipboardText() returns string; SetClipboardText(ImU8String)
        if (ImGui.Button("Paste from clipboard"))
            this.importText = ImGui.GetClipboardText();
        ImGui.SameLine();
        if (ImGui.Button("Copy"))
            ImGui.SetClipboardText(this.importText);
        ImGui.SameLine();
        ImGui.Checkbox("Debug", ref this.showDebug);

        // Tables
        if (ImGui.BeginTable("plan", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp, new Vector2(0, 150)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Mechanic", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();
            for (var row = 0; row < 3; row++)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{row * 10}");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("Program Loop");
                ImGui.TableSetColumnIndex(2);
                ImGui.PushID(row); // int binds to PushID(nint)
                if (ImGui.SmallButton("x"))
                    this.plugin.SaveConfig();
                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        using (var table = ImRaii.Table("raiiTable", 2, ImGuiTableFlags.Borders))
        {
            if (table)
            {
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("a");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("b");
            }
        }

        // Tabs / tooltips / popups / child windows
        using (var tabBar = ImRaii.TabBar("tabs"))
        {
            if (tabBar)
            {
                using (var tab = ImRaii.TabItem("General"))
                {
                    if (tab)
                        ImGui.TextWrapped("General settings");
                }
            }
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("tooltip");

        if (ImGui.BeginChild("child", new Vector2(0, 40), true))
            ImGui.TextDisabled("child");
        ImGui.EndChild(); // always call EndChild, regardless of BeginChild's return

        if (ImGui.CollapsingHeader("Advanced"))
            ImGui.BulletText("advanced");
    }
}
```

### GameStateProbe.cs

```csharp
// (b) threading, (c) game state, (d) battle NPC cast info, (h) job gauges, ActionManager recasts.
using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.JobGauge.Enums;
using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.ClientState.Statuses;
using Dalamud.Game.DutyState;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace MitCueProbe;

public sealed class GameStateProbe : IDisposable
{
    private readonly Dictionary<uint, (uint ActionId, float Total)> lastCast = new();

    public GameStateProbe()
    {
        // IFramework.Update: event OnUpdateDelegate Update; delegate void OnUpdateDelegate(IFramework framework)
        Plugin.Framework.Update += this.OnFrameworkUpdate;

        // IClientState events
        Plugin.ClientState.TerritoryChanged += this.OnTerritoryChanged;           // event Action<uint>
        Plugin.ClientState.ClassJobChanged += this.OnClassJobChanged;             // delegate void ClassJobChangeDelegate(uint classJobId)
        Plugin.ClientState.LevelChanged += this.OnLevelChanged;                   // delegate void LevelChangeDelegate(uint classJobId, uint level)
        Plugin.ClientState.Login += this.OnLogin;                                 // event Action
        Plugin.ClientState.Logout += this.OnLogout;                               // delegate void LogoutDelegate(int type, int code)
        Plugin.ClientState.ZoneInit += this.OnZoneInit;                           // event Action<ZoneInitEventArgs>

        // ICondition.ConditionChange: delegate void ConditionChangeDelegate(ConditionFlag flag, bool value)
        Plugin.Condition.ConditionChange += this.OnConditionChange;

        // IDutyState events: delegate void XxxDelegate(IDutyStateEventArgs args)
        Plugin.DutyState.DutyStarted += this.OnDutyStarted;
        Plugin.DutyState.DutyWiped += this.OnDutyWiped;
        Plugin.DutyState.DutyRecommenced += this.OnDutyRecommenced;
        Plugin.DutyState.DutyCompleted += this.OnDutyCompleted;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= this.OnFrameworkUpdate;
        Plugin.ClientState.TerritoryChanged -= this.OnTerritoryChanged;
        Plugin.ClientState.ClassJobChanged -= this.OnClassJobChanged;
        Plugin.ClientState.LevelChanged -= this.OnLevelChanged;
        Plugin.ClientState.Login -= this.OnLogin;
        Plugin.ClientState.Logout -= this.OnLogout;
        Plugin.ClientState.ZoneInit -= this.OnZoneInit;
        Plugin.Condition.ConditionChange -= this.OnConditionChange;
        Plugin.DutyState.DutyStarted -= this.OnDutyStarted;
        Plugin.DutyState.DutyWiped -= this.OnDutyWiped;
        Plugin.DutyState.DutyRecommenced -= this.OnDutyRecommenced;
        Plugin.DutyState.DutyCompleted -= this.OnDutyCompleted;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        // (b) threading helpers
        bool onFrameworkThread = framework.IsInFrameworkUpdateThread;   // true here
        bool isMain = ThreadSafety.IsMainThread;                        // true here (Dalamud.Utility.ThreadSafety)
        TimeSpan dt = framework.UpdateDelta;
        DateTime last = framework.LastUpdateUTC;

        // (c) client state
        uint territory = Plugin.ClientState.TerritoryType;              // uint (TerritoryType row id)
        bool loggedIn = Plugin.ClientState.IsLoggedIn;
        uint map = Plugin.ClientState.MapId;
        bool pvp = Plugin.ClientState.IsPvP;
        if (!loggedIn || territory is not (1122 or 887 or 1363) || pvp)
            return;

        // IObjectTable.LocalPlayer (IClientState has no LocalPlayer in API 15)
        IPlayerCharacter? me = Plugin.ObjectTable.LocalPlayer;
        uint myJob = me?.ClassJob.RowId ?? 0;                           // RowRef<ClassJob>
        byte myLevel = me?.Level ?? 0;                                  // ICharacter.Level (byte)
        uint myEntityId = me?.EntityId ?? 0;

        // IPlayerState (no game object needed)
        if (Plugin.PlayerState.IsLoaded)
        {
            RowRef<ClassJob> job = Plugin.PlayerState.ClassJob;
            short level = Plugin.PlayerState.Level;                     // unsynced job level
            short effective = Plugin.PlayerState.EffectiveLevel;        // synced level when level-synced
            bool synced = Plugin.PlayerState.IsLevelSynced;
            uint entityId = Plugin.PlayerState.EntityId;
            string name = Plugin.PlayerState.CharacterName;
            string abbr = job.ValueNullable?.Abbreviation.ExtractText() ?? "?";
            _ = (level, effective, synced, entityId, name, abbr, myJob, myLevel, myEntityId);
        }

        // ICondition
        bool inCombat = Plugin.Condition[ConditionFlag.InCombat];
        bool boundByDuty = Plugin.Condition[ConditionFlag.BoundByDuty]
                           || Plugin.Condition[ConditionFlag.BoundByDuty56]
                           || Plugin.Condition[ConditionFlag.BoundByDuty95];
        bool anyDuty = Plugin.Condition.Any(ConditionFlag.BoundByDuty, ConditionFlag.BoundByDuty56, ConditionFlag.BoundByDuty95);
        bool betweenAreas = Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51];
        bool cutscene = Plugin.Condition[ConditionFlag.WatchingCutscene] || Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent];

        // IDutyState
        bool started = Plugin.DutyState.IsDutyStarted;
        RowRef<ContentFinderCondition> cfc = Plugin.DutyState.ContentFinderCondition;
        _ = (onFrameworkThread, isMain, dt, last, map, inCombat, boundByDuty, anyDuty, betweenAreas, cutscene, started, cfc);

        // IPartyList: IReadOnlyCollection<IPartyMember>, Length is 0 when not in a party
        foreach (IPartyMember member in Plugin.PartyList)
        {
            uint id = member.EntityId;
            string name = member.Name.TextValue;                        // SeString -> string
            uint jobId = member.ClassJob.RowId;                         // RowRef<ClassJob>
            byte lvl = member.Level;
            ulong contentId = member.ContentId;
            IGameObject? go = member.GameObject;                        // via ObjectTable lookup
            _ = (id, name, jobId, lvl, contentId, go);
        }

        int partySize = Plugin.PartyList.Length;
        IPartyMember? first = Plugin.PartyList[0];
        _ = (partySize, first);

        // (d) enumerate enemies and read cast bars
        foreach (IGameObject obj in Plugin.ObjectTable)
        {
            if (obj.ObjectKind != ObjectKind.BattleNpc || obj is not IBattleNpc npc)
                continue;
            if (npc.BattleNpcKind != BattleNpcSubKind.Combatant)       // excludes pets/buddies/parts
                continue;

            uint baseId = npc.BaseId;                                   // BNpcBase row (was DataId)
            uint nameId = npc.NameId;                                   // BNpcName row (ICharacter.NameId)
            uint entityId = npc.EntityId;                               // 0xE0000000 = not networked
            ulong gameObjectId = npc.GameObjectId;
            string name = npc.Name.TextValue;
            bool targetable = npc.IsTargetable;
            bool dead = npc.IsDead;
            uint hp = npc.CurrentHp;
            uint maxHp = npc.MaxHp;

            if (npc.IsCasting)
            {
                uint actionId = npc.CastActionId;
                byte actionType = npc.CastActionType;                   // byte; 1 = ActionType.Action
                float elapsed = npc.CurrentCastTime;
                float total = npc.TotalCastTime;                        // BaseCastTime + adjustments (display)
                float baseCast = npc.BaseCastTime;
                ulong castTarget = npc.CastTargetObjectId;
                bool interruptible = npc.IsCastInterruptible;
                this.lastCast[entityId] = (actionId, total);
                _ = (actionType, elapsed, baseCast, castTarget, interruptible);
            }

            // StatusList: IReadOnlyCollection<IStatus>
            foreach (IStatus status in npc.StatusList)
            {
                uint statusId = status.StatusId;
                float remaining = status.RemainingTime;
                uint sourceId = status.SourceId;
                ushort param = status.Param;
                _ = (statusId, remaining, sourceId, param);
            }

            _ = (baseId, nameId, gameObjectId, name, targetable, dead, hp, maxHp);
        }

        // lookups
        IGameObject? byGameObjectId = Plugin.ObjectTable.SearchById(0x40001234UL);
        IGameObject? byEntityId = Plugin.ObjectTable.SearchByEntityId(0x40001234u);
        IGameObject? slot0 = Plugin.ObjectTable[0];
        IEnumerable<IBattleChara> players = Plugin.ObjectTable.PlayerObjects;
        _ = (byGameObjectId, byEntityId, slot0, players.Count());

        // (h) job gauges: check the job first; the gauge memory is shared between jobs.
        switch (myJob)
        {
            case 24: // WHM
            {
                WHMGauge whm = Plugin.JobGauges.Get<WHMGauge>();
                byte lilies = whm.Lily;
                byte blood = whm.BloodLily;
                short lilyTimerMs = whm.LilyTimer;
                _ = (lilies, blood, lilyTimerMs);
                break;
            }

            case 28: // SCH
            {
                SCHGauge sch = Plugin.JobGauges.Get<SCHGauge>();
                byte aetherflow = sch.Aetherflow;
                byte fairy = sch.FairyGauge;
                short seraphMs = sch.SeraphTimer;
                DismissedFairy dismissed = sch.DismissedFairy;
                _ = (aetherflow, fairy, seraphMs, dismissed);
                break;
            }

            case 40: // SGE
            {
                SGEGauge sge = Plugin.JobGauges.Get<SGEGauge>();
                byte gall = sge.Addersgall;
                short gallTimer = sge.AddersgallTimer;
                byte sting = sge.Addersting;
                bool eukrasia = sge.Eukrasia;
                _ = (gall, gallTimer, sting, eukrasia);
                break;
            }

            case 33: // AST
            {
                ASTGauge ast = Plugin.JobGauges.Get<ASTGauge>();
                CardType c1 = ast.Card1;
                CardType c2 = ast.Card2;
                CardType c3 = ast.Card3;
                CardType crown = ast.DrawnCrownCard;
                DrawType draw = ast.ActiveDraw;
                _ = (c1, c2, c3, crown, draw);
                break;
            }
        }

        this.ReadCooldowns();
    }

    // FFXIVClientStructs ActionManager: recast state of the local player's actions (main thread only).
    private unsafe void ReadCooldowns()
    {
        ActionManager* am = ActionManager.Instance();
        if (am == null)
            return;

        const uint Temperance = 16536;
        uint adjusted = am->GetAdjustedActionId(Temperance);
        float total = am->GetRecastTime(ActionType.Action, adjusted);              // 0 when not on cooldown
        float elapsed = am->GetRecastTimeElapsed(ActionType.Action, adjusted);
        bool active = am->IsRecastTimerActive(ActionType.Action, adjusted);
        bool offCd = am->IsActionOffCooldown(ActionType.Action, adjusted);
        uint charges = am->GetCurrentCharges(adjusted);
        ushort maxCharges = ActionManager.GetMaxCharges(adjusted, 0);              // level 0 = current level
        uint status = am->GetActionStatus(ActionType.Action, adjusted);           // 0 = usable
        int recastGroup = am->GetRecastGroup((int)ActionType.Action, adjusted);
        RecastDetail* detail = am->GetRecastGroupDetail(recastGroup);
        float groupElapsed = detail != null ? detail->Elapsed : 0;
        _ = (total, elapsed, active, offCd, charges, maxCharges, status, groupElapsed);
    }

    private void OnTerritoryChanged(uint territoryId) { }

    private void OnClassJobChanged(uint classJobId) { }

    private void OnLevelChanged(uint classJobId, uint level) { }

    private void OnLogin() { }

    private void OnLogout(int type, int code) { }

    private void OnZoneInit(Dalamud.Game.ClientState.ZoneInitEventArgs args)
    {
        RowRef<TerritoryType> tt = args.TerritoryType;
        RowRef<ContentFinderCondition> cfc = args.ContentFinderCondition;
        ushort instance = args.Instance;
        _ = (tt, cfc, instance);
    }

    private void OnConditionChange(ConditionFlag flag, bool value)
    {
        if (flag == ConditionFlag.InCombat && value)
            Plugin.Log.Debug("combat started");
    }

    private void OnDutyStarted(IDutyStateEventArgs args)
    {
        uint territory = args.TerritoryType.RowId;
        uint cfc = args.ContentFinderCondition.RowId;
        uint handler = args.EventHandlerId;
        _ = (territory, cfc, handler);
    }

    private void OnDutyWiped(IDutyStateEventArgs args) { }

    private void OnDutyRecommenced(IDutyStateEventArgs args) { }

    private void OnDutyCompleted(IDutyStateEventArgs args) { }
}
```

### DataProbe.cs

```csharp
// (e) Excel data through IDataManager + Lumina.Excel.Sheets row structs.
using System.Collections.Generic;
using Dalamud.Game;
using Dalamud.Utility;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using LuminaAction = Lumina.Excel.Sheets.Action; // 'Action' clashes with System.Action

namespace MitCueProbe;

public static class DataProbe
{
    public sealed record ActionInfo(
        uint Id, string Name, uint IconId, uint ClassJobId, byte ClassJobLevel, uint ClassJobCategoryId,
        float RecastSeconds, byte MaxCharges, bool IsPlayerAction, bool IsRoleAction, bool IsPvP,
        uint ActionCategoryId, string ActionCategoryName, byte CooldownGroup, byte AdditionalCooldownGroup,
        string Description);

    public static ActionInfo? GetAction(uint actionId)
    {
        // GetExcelSheet<T>(ClientLanguage? language = null, string? name = null) where T : struct, IExcelRow<T>
        ExcelSheet<LuminaAction> actions = Plugin.DataManager.GetExcelSheet<LuminaAction>();
        if (!actions.TryGetRow(actionId, out var row))
            return null;

        ExcelSheet<ActionTransient> transient = Plugin.DataManager.GetExcelSheet<ActionTransient>();
        string description = transient.TryGetRow(actionId, out var t) ? t.Description.ExtractText() : string.Empty;

        return new ActionInfo(
            row.RowId,
            row.Name.ExtractText(),                   // ReadOnlySeString -> string
            row.Icon,                                 // ushort
            row.ClassJob.RowId,                       // RowRef<ClassJob>
            row.ClassJobLevel,                        // byte
            row.ClassJobCategory.RowId,               // RowRef<ClassJobCategory>
            row.Recast100ms / 10f,                    // ushort, tenths of a second
            row.MaxCharges,                           // byte
            row.IsPlayerAction,
            row.IsRoleAction,
            row.IsPvP,
            row.ActionCategory.RowId,                 // RowRef<ActionCategory> (2 = Spell, 4 = Ability ...)
            row.ActionCategory.ValueNullable?.Name.ExtractText() ?? string.Empty,
            row.CooldownGroup,
            row.AdditionalCooldownGroup,
            description);
    }

    // ClassJobCategory has one bool per job abbreviation.
    public static bool CanJobUse(in LuminaAction action, uint classJobId)
    {
        if (!action.ClassJobCategory.IsValid)
            return false;
        ClassJobCategory cat = action.ClassJobCategory.Value;
        return classJobId switch
        {
            24 => cat.WHM,
            28 => cat.SCH,
            33 => cat.AST,
            40 => cat.SGE,
            19 => cat.PLD,
            21 => cat.WAR,
            32 => cat.DRK,
            37 => cat.GNB,
            6 => cat.CNJ,
            26 => cat.ACN,
            _ => false,
        };
    }

    public static IEnumerable<LuminaAction> HealerPlayerActions(uint classJobId)
    {
        foreach (var a in Plugin.DataManager.GetExcelSheet<LuminaAction>())
        {
            if (a.IsPlayerAction && !a.IsPvP && CanJobUse(a, classJobId))
                yield return a;
        }
    }

    public static (string Name, string Abbrev, uint ParentId, byte Role) GetJob(uint classJobId)
    {
        var job = Plugin.DataManager.GetExcelSheet<ClassJob>().GetRow(classJobId); // throws if missing
        return (job.Name.ExtractText(), job.Abbreviation.ExtractText(), job.ClassJobParent.RowId, job.Role);
    }

    public static (string Duty, byte LevelSync, ushort ILvlSync, uint TerritoryId, string PlaceName)? GetDutyForTerritory(uint territoryId)
    {
        if (Plugin.DataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId) is not { } territory)
            return null;

        RowRef<ContentFinderCondition> cfcRef = territory.ContentFinderCondition;
        if (!cfcRef.IsValid)
            return null;
        ContentFinderCondition cfc = cfcRef.Value;
        return (cfc.Name.ExtractText(), cfc.ClassJobLevelSync, cfc.ItemLevelSync, cfc.TerritoryType.RowId,
                territory.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty);
    }

    public static string EnglishActionName(uint id)
    {
        // explicit language
        var en = Plugin.DataManager.GetExcelSheet<LuminaAction>(ClientLanguage.English);
        return en.HasRow(id) ? en[id].Name.ExtractText() : string.Empty;
    }

    public static string StatusName(uint statusId)
        => Plugin.DataManager.GetExcelSheet<Status>().GetRowOrDefault(statusId)?.Name.ExtractText() ?? string.Empty;

    public static string ToDalamud(uint id)
        => Plugin.DataManager.GetExcelSheet<LuminaAction>().GetRow(id).Name.ToDalamudString().TextValue;

    public static ClientLanguage GameLanguage => Plugin.DataManager.Language;
}
```

### ChatProbe.cs

```csharp
// (j) chat + notifications + countdown, (k) logging.
using System;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Text.ReadOnly;

namespace MitCueProbe;

public sealed class ChatProbe : IDisposable
{
    // LogMessage sheet rows (EN): 5255 "Battle commencing in N second(s)! (Name)", 5256 "Engage!", 5257 "Countdown canceled by ..."
    private const uint LogMsgCountdownStart = 5255;
    private const uint LogMsgEngage = 5256;
    private const uint LogMsgCountdownCancel = 5257;

    public float? CountdownRemaining { get; private set; }

    public ChatProbe()
    {
        // event OnHandleableChatMessageDelegate ChatMessage; delegate void (IHandleableChatMessage message)
        Plugin.ChatGui.ChatMessage += this.OnChatMessage;
        // event OnLogMessageDelegate LogMessage; delegate void (ILogMessage message)
        Plugin.ChatGui.LogMessage += this.OnLogMessage;
        Plugin.Framework.Update += this.PollCountdown;
    }

    public void Dispose()
    {
        Plugin.ChatGui.ChatMessage -= this.OnChatMessage;
        Plugin.ChatGui.LogMessage -= this.OnLogMessage;
        Plugin.Framework.Update -= this.PollCountdown;
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        // The same instance is reused for every message: copy what you need, never store `message`.
        XivChatType kind = message.LogKind;                 // channel only (low 7 bits of the raw log kind)
        XivChatRelationKind source = message.SourceKind;    // relation of the sender (LocalPlayer, PartyMember ...)
        XivChatRelationKind target = message.TargetKind;
        int timestamp = message.Timestamp;
        string text = message.Message.TextValue;            // SeString -> plain text
        string sender = message.Sender.TextValue;
        ReadOnlySeString original = message.OriginalMessage;
        bool handled = message.IsHandled;

        // ACT shows the countdown start as code 0x00B9 (self) / 0x0139 (party member): LogKind 57 + TargetKind bits.
        if (kind == XivChatType.SystemMessage && target is XivChatRelationKind.LocalPlayer or XivChatRelationKind.PartyMember)
            Plugin.Log.Verbose("system message with player relation");

        if (kind == XivChatType.SystemMessage)
        {
            if (text.StartsWith("Battle commencing in ", StringComparison.Ordinal))
                Plugin.Log.Debug("countdown started: {0}", text);
            else if (text == "Engage!")
                Plugin.Log.Debug("engage");
        }

        _ = (source, target, timestamp, sender, original, handled);
    }

    private void OnLogMessage(ILogMessage message)
    {
        // Language independent: LogMessage sheet row id + typed parameters.
        uint id = message.LogMessageId;
        if (id is LogMsgCountdownStart or LogMsgEngage or LogMsgCountdownCancel)
        {
            int count = message.ParameterCount;
            if (message.TryGetIntParameter(0, out int seconds))
                Plugin.Log.Debug("log message {0} param0={1} ({2} params)", id, seconds, count);
            ILogMessageEntity? src = message.SourceEntity;
            string who = src?.Name.ExtractText() ?? string.Empty;
            _ = who;
        }
    }

    // Countdown state straight from the agent (what BossMod does): Active + TimeRemaining (float seconds).
    private unsafe void PollCountdown(IFramework framework)
    {
        AgentCountDownSettingDialog* agent = AgentCountDownSettingDialog.Instance();
        this.CountdownRemaining = agent != null && agent->Active ? agent->TimeRemaining : null;
    }

    public static void Examples()
    {
        // IChatGui.Print(string message, string? messageTag = null, ushort? tagColor = null)
        Plugin.ChatGui.Print("MitCue loaded", "MitCue", 45);
        Plugin.ChatGui.PrintError("Plan failed to parse");
        Plugin.ChatGui.Print(new XivChatEntry { Message = new SeString(), Type = XivChatType.Echo });

        // INotificationManager.AddNotification(Notification) -> IActiveNotification
        IActiveNotification n = Plugin.NotificationManager.AddNotification(new Notification
        {
            Title = "MitCue",
            Content = "Imported 42 entries.",
            Type = NotificationType.Success,
            InitialDuration = TimeSpan.FromSeconds(5),
            Minimized = false,
        });
        n.Progress = 1f;

        // (k) IPluginLog: Serilog message templates
        Plugin.Log.Verbose("verbose {Value}", 1);
        Plugin.Log.Debug("debug {Value}", 2);
        Plugin.Log.Information("info {Value}", 3);
        Plugin.Log.Info("info alias {Value}", 3);
        Plugin.Log.Warning("warning {Value}", 4);
        Plugin.Log.Error(new InvalidOperationException("x"), "error {Value}", 5);
        Plugin.Log.Fatal("fatal");
        Plugin.Log.Write(Serilog.Events.LogEventLevel.Information, null, "write {A}", 6);
    }
}
```

### HookProbe.cs

```csharp
// (i) hooks via IGameInteropProvider (read-only observation of boss ability effects and casts).
using System;
using System.Numerics;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Network;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Network;

namespace MitCueProbe;

public sealed unsafe class HookProbe : IDisposable
{
    private readonly Hook<ActionEffectHandler.Delegates.Receive> actionEffectHook;
    private readonly Hook<PacketDispatcher.Delegates.HandleActorCastPacket> actorCastHook;

    public HookProbe()
    {
        // HookFromAddress<T>(nint procAddress, T detour, HookBackend backend = Automatic) -> Hook<T>
        this.actionEffectHook = Plugin.GameInterop.HookFromAddress<ActionEffectHandler.Delegates.Receive>(
            ActionEffectHandler.Addresses.Receive.Value, // InteropGenerator.Runtime.Address.Value (nint), resolved by ClientStructs
            this.ActionEffectDetour);

        // void* overload (MemberFunctionPointers are delegate* unmanaged<...>)
        this.actorCastHook = Plugin.GameInterop.HookFromAddress<PacketDispatcher.Delegates.HandleActorCastPacket>(
            (nint)PacketDispatcher.MemberFunctionPointers.HandleActorCastPacket,
            this.ActorCastDetour);

        // Hooks are created disabled.
        this.actionEffectHook.Enable();
        this.actorCastHook.Enable();
    }

    public void Dispose()
    {
        this.actionEffectHook.Disable();
        this.actionEffectHook.Dispose();
        this.actorCastHook.Dispose();
    }

    public Hook<ActionEffectHandler.Delegates.Receive> CreateFromSignature()
    {
        // HookFromSignature<T>(string signature, T detour, HookBackend backend = Automatic): scans .text, throws if not found.
        // Address.String holds ClientStructs' own signature for the function.
        string sig = ActionEffectHandler.Addresses.Receive.String;
        return Plugin.GameInterop.HookFromSignature<ActionEffectHandler.Delegates.Receive>(sig, this.ActionEffectDetour, IGameInteropProvider.HookBackend.Automatic);
    }

    private void ActionEffectDetour(uint casterEntityId, Character* casterPtr, Vector3* targetPos,
                                    ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects,
                                    GameObjectId* targetEntityIds)
    {
        try
        {
            uint actionId = header->ActionId;
            byte actionType = header->ActionType;   // 1 = Action
            byte numTargets = header->NumTargets;
            uint globalSeq = header->GlobalSequence;
            ulong firstTarget = numTargets > 0 ? targetEntityIds[0] : 0UL; // GameObjectId -> ulong (implicit)
            uint firstTargetObjectId = numTargets > 0 ? targetEntityIds[0].ObjectId : 0u;
            _ = (casterEntityId, actionId, actionType, numTargets, globalSeq, firstTarget, firstTargetObjectId);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "ActionEffectDetour");
        }

        // Always call the original (Hook<T>.Original is the trampoline delegate of type T).
        this.actionEffectHook.Original(casterEntityId, casterPtr, targetPos, header, effects, targetEntityIds);
    }

    private void ActorCastDetour(uint entityId, ActorCastPacket* packet)
    {
        this.actorCastHook.Original(entityId, packet);
        try
        {
            uint actionId = packet->ActionId;
            float castTime = packet->CastTime;
            byte actionType = packet->ActionType;
            uint target = packet->TargetEntityId;
            _ = (entityId, actionId, castTime, actionType, target);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "ActorCastDetour");
        }
    }

    public string Describe() =>
        $"{this.actionEffectHook.Address:X} enabled={this.actionEffectHook.IsEnabled} disposed={this.actionEffectHook.IsDisposed} backend={this.actionEffectHook.BackendName}";
}
```

### Probe.csproj

```xml
<Project Sdk="Dalamud.NET.Sdk/15.0.0">
  <PropertyGroup>
    <AssemblyName>MitCueProbe</AssemblyName>
    <RootNamespace>MitCueProbe</RootNamespace>
    <Version>0.1.2.3</Version>
    <Author>Static</Author>
    <Name>MitCue Probe</Name>
    <Punchline>Mitigation reminders synced to fight timelines.</Punchline>
    <Description>Shows which mitigation / healing actions to press next. Reminder-only.</Description>
    <Tags>raid;mitigation;healer</Tags>
    <CategoryTags>ui;jobs</CategoryTags>
    <RepoUrl>https://example.invalid/mitcue</RepoUrl>
    <IconUrl>https://example.invalid/mitcue/icon.png</IconUrl>
    <ApplicableVersion>any</ApplicableVersion>
    <DalamudApiLevel>15</DalamudApiLevel>
    <Changelog>Probe build.</Changelog>
  </PropertyGroup>
</Project>
```

## Verification

Independent verification pass (verifier agent, 2026-11-11) against the same primary sources: the shipped assemblies in `$HOME/.xlcore/dalamud/Hooks/dev/`, Dalamud source at commit 12e318c1 (`research/repos/dalamud-api-src`), ClientStructs at 80cb4a7b (the exact submodule commit), DalamudPackager 827d88dc, the game-data CSVs, cactbot and BossMod clones. The aim was to find errors, not to agree. Verifier artifacts (all under `/tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/`):

- `probes/verify-dalamud-api/` — own compiling probe (Plugin.cs, VWindow.cs, VState.cs), copied from `buildtest/Probe`. It checks exact static types with `static void Is<T>(ref T v)` (a `ref` argument must match the type exactly, so no implicit widening hides a wrong type) and delegate signatures with explicitly-typed lambdas. `dotnet build -c Release`: 0 errors (1 nullability warning from `TryGetFromGameIcon`'s nullable out parameter).
- `probes/verify-dalamud-api-neg/` and `probes/verify-dalamud-api-neg2/` — negative probes.
- `verify-tools/refl/` — own MetadataLoadContext reflection dumper (enum values, parameter defaults, `[Obsolete]`, visibility).
- `verify-tools/cfgtest/` — config round trip with Dalamud's own Newtonsoft.Json.dll and Dalamud's exact save settings.
- `verify-tools/asmver/` — assembly/file/product versions of the shipped DLLs.
- `verify-tools/researcher-probe-copy/` — clean copy of the researcher's probe, rebuilt.

### What was checked (and confirmed)

| Area | Facts | How | Result |
|---|---|---|---|
| Plugin entry, `[PluginService]` (static + instance), `IDalamudPluginInterface` members, `IUiBuilder` events, config API | 4-7, 9 | compiled exact types; ServiceContainer.cs:117-150 (uninitialized object -> InjectProperties -> ctor via `Task.Factory.StartNew(LongRunning)`); DalamudPluginInterface.cs:341-371; UiBuilder.cs:739-787 | confirmed |
| Config list duplication | 10 | re-run: `List` default `[WHM,SCH]` -> `[WHM,SCH,WHM,SCH]`; with `Replace` -> `[WHM,SCH]`; `Dictionary` default keeps count 1; `Vector4` -> `{X,Y,Z,W}` | confirmed (dictionary nuance added, V4) |
| WindowSystem / Window members and defaults, per-frame order, click-through recipe | 11-14 | reflection + compile; Window.cs:52-137 defaults; WindowHost.cs:129,138,146,183,185,197,222-223,271-281,310,358,364,548,570,614,623-667,682-705 | confirmed (one precision fix in 13) |
| Commands | 15 | compiled; CommandManager.cs:174-192 | confirmed |
| Threads: ctor off main thread even with LoadSync, Dispose on framework thread, Update on main thread, Draw inside Present | 16-18 | LocalPlugin.cs:641-645, 782-787; Framework.cs:63, 418-429, 483; InterfaceManager.cs:252-254, 669-690; InterfaceManager.AsHook.cs:94-99 | confirmed (18 stays source-level/indirect; with ReShade installed Draw is called from ReShade's present/overlay callbacks, InterfaceManager.AsReShadeAddon.cs:36-54, still inside Present) |
| Main-thread asserts | 19 | ObjectTable.cs:54,89,98,115,132,140,238; StatusList.cs:50; DevModeAssert sites; Condition.cs:56-69 (no assert); ClientState.cs:179-186 (IsLoggedIn via AgentLobby) | confirmed (dev-mode/API 16 note added, V7) |
| Event sources | 20, 22 | ClientState.cs:122-137 (TerritoryChanged only on change), 275-330 (ZoneInit raised before TerritoryType is set), 380-396 (Login from Framework.Update); DutyState.cs | confirmed |
| `IFramework` | 21 | reflection + compiled `RunOnFrameworkThread`, `RunOnTick`, `DelayTicks`, `Update` delegate; `RunOnFrameworkThread(Func<Task>)` overloads are `[Obsolete("Use RunOnTick instead.")]` | confirmed |
| `IClientState` (no `LocalPlayer`/`LocalContentId`), `IObjectTable`, wrapper reuse, inclusive index ranges | 22-25 | compiled + negatives; ObjectTable.cs:64, 95-125, 175-184 (`index <= range.End.Value`, so `[0,199]` etc. are inclusive), 189-226 | confirmed |
| `IPlayerState` (`Level` unsynced, `EffectiveLevel` = `SyncedLevel` when `IsLevelSynced`) | 26 | compiled exact types; PlayerState.cs:80-87 | confirmed |
| `IPartyList`/`IPartyMember`, `ICondition` + ConditionFlag values, `IDutyState` (0x6D, 0x40000001/2/3/5/6, fallback) | 27-29 | compiled exact types; enum values by reflection; PartyMember.cs:116,137; DutyState.cs | confirmed |
| `IBattleChara` cast members, identifiers, `IBattleNpc.BattleNpcKind`, enums, `StatusList`/`IStatus` | 31-34 | compiled exact types (`byte CastActionType`, `uint CastActionId`, `float Current/Base/TotalCastTime`, `ulong CastTargetObjectId`, `uint BaseId/EntityId/NameId`, `ulong GameObjectId` ...); BattleChara.cs | confirmed |
| `IDataManager.GetExcelSheet<T>(ClientLanguage?, string?)`, `ExcelSheet<T>`, `RowRef<T>`, `ExtractText` | 36-38 | compiled; Lumina.xml (GetRow throws ArgumentOutOfRangeException, RowRef.Value throws InvalidOperationException) | confirmed (collision note added, V8) |
| Lumina row members (Action, ActionTransient, ActionCategory, ClassJob, ClassJobCategory, TerritoryType, ContentFinderCondition) | 39-41 | compiled exact types + reflection | confirmed (one wrong parenthetical in 40, V2) |
| CSV data | 30, 42 | python csv lookups: 1122 -> CFC 908 'the Omega Protocol (Ultimate)' sync 90 ilvl 635; 887 -> CFC 694 sync 80 ilvl 475; 1363 -> CFC 1094 'Dancing Mad (Ultimate)' sync 100 ilvl 0 (all CFC.TerritoryType link back); ClassJob 6/24/26/28/33/40 + parents; Action 7433, 16536, 25862, 188, 25868, 7562, 7432 (Icon, ClassJob, level, CJC, Recast100ms, ActionCategory, MaxCharges) plus 12 more healer actions; CJC 25/29/53/120 | all 16 sampled values in facts 30/42 match |
| Textures | 43 | compiled `GetFromGameIcon(in GameIconLookup)`, implicit uint/int -> `GameIconLookup`, `GetWrapOrEmpty().Handle` (`ImTextureID`) -> `ImGui.Image`; ISharedImmediateTexture.cs:13-60 | confirmed |
| ImGui bindings: `ImU8String` conversions, `Begin`/`End`, `Image`, `ProgressBar`, `PushStyleColor`, clipboard, `InputText*` (`int maxLength = 512`, string overload re-encodes into a `maxLength+1` buffer, ImGui.Manual.cs:431-449), `Combo` (`popupMaxHeightInItems = -1`), widgets, tables, `ImRaii` (struct vs class disposables), flag/cond/col values | 44-49 | compiled every listed call; reflection for defaults and enum values | confirmed (field nuance in 44, V6) |
| Job gauges | 50 | compiled exact types; reflection (`ASTGauge.Card1/2/3` exist, `DrawnCards` obsolete); WHMGauge.cs/SGEGauge.cs docs | confirmed |
| `ActionManager` | 51 | compiled exact types; `GetActionStatus` default `targetId = 0xE0000000`; ClientStructs ActionManager.cs:234 (`GetMaxCharges` level 0 = current level) | confirmed |
| Hooks | 52-53 | compiled `HookFromAddress` (Address.Value and `(nint)MemberFunctionPointers`), `HookFromSignature`, `Original`, `Enable/Disable`; SigScanner.cs:120-127, 270-290 (ScanText resolves E8/E9); ReloadedHook ctor (created disabled); HookBackend `[Obsolete]` (IGameInteropProvider.cs:28,35) | confirmed except `HookId` (V3) |
| Chat, countdown, notifications, logging | 54-59 | compiled delegate types and members; ChatGui.cs:62 (single shared `ChatMessage` instance), 385-396; ClientStructs RaptureLogModule.cs:174-179 (`LogKind` bits 0-6, `TargetKind` bits 7-10, `SourceKind` bits 11-14, so 0x00B9 -> kind 57, target 1 = LocalPlayer); `XivChatType` has no member above 107; cactbot FAQ-Troubleshooting.md:200 (00B9 line), TheAbyssalFractureExtreme.log:484/535/592 (0039 lines), translations.ts:16-33; LogMessage.csv 5255-5264 (LogKind 57); BossMod WorldStateGameSync.cs:773-776 | confirmed (56 stays UNVERIFIED as marked) |
| Packaging / pluginmaster | 2, 60-63 | Sdk.props/Sdk.targets/SdkPackageVersions.props; DalamudPackager.targets and DalamudPackager.cs (json -> yaml -> csproj, the four required fields, `ApplicableVersion` 'any', `DalamudApiLevel` 15, `InternalName` = assembly name, `AssemblyVersion` from the DLL); own Release build output; PluginManifest.cs/RemotePluginManifest.cs fields; PluginRepository.cs validation; PluginManager.cs:1126-1147, 1760-1772, 820/1038/1316-1318 | confirmed (output-path wording fixed, V5) |
| Negative claims | 64 + others | re-compiled N01, N02, N04, N05, N06 (CS0618 warning), N07 (CS1615+CS1503), N08, N09, N10 (CS1678+CS1661), N13, N14 (CS0117), N15, N17 (compiles), N20, N21 (CS0122), N22, N23, N30 (CS8345), N31 (CS0104) | all error codes reproduced exactly |
| Appendix | 3 | extracted the appendix code blocks and diffed them against `probes/dalamud-api/*`; rebuilt a clean copy | identical; 0 errors / 0 warnings |

### What was wrong, and what was corrected in place

- **V1 — Dalamud version (header, summary, facts 1, 62, 64).** The shipped Dalamud.dll is AssemblyVersion/FileVersion **15.0.4.0**, ProductVersion `15.0.4.0+12e318c1…` (also `Dalamud/15.0.4.0` in Dalamud.deps.json); at 12e318c1 Dalamud.csproj sets `<DalamudVersion>15.0.4.0</DalamudVersion>`. "15.0.3.9" was taken from `git describe` (`15.0.3.9-31-g12e318c19`), which only names the nearest tag. The claim that "current master (43b34118) differs only by the 15.0.4.0 version bump" had the direction reversed: `git merge-base --is-ancestor` shows 43b34118 is an older ancestor of 12e318c1 (43b34118 has 15.0.3.9; 12e318c1 adds commit 51b4552 "build: 15.0.4.0"). API level 15 and every API fact are unaffected. If MitCue sets `MinimumDalamudVersion`, use a value no higher than 15.0.4.0.
- **V2 — Fact 40, ClassJobCategory.** "base-class flags CNJ/ACN are also set for WHM/SCH actions" is false as a rule. Only class-learned actions also set the base-class flag (Cure 120 / Medica 124 / Medica II 133: CJC 53 "CNJ WHM"). Job actions such as Plenary Indulgence, Temperance, Liturgy, Cure III and Regen are CJC 25 "WHM" only. All SCH actions, including Physick 190 (level 4), are CJC 29 "SCH" only. Only 15 of 43 non-PvP WHM player actions set CNJ. Corrected so the rule is "test the job's own flag".
- **V3 — Fact 52, `Hook<T>.HookId`.** Listed as a usable member, but it is `protected Guid HookId` (Dalamud/Hooking/Hook.cs:77). `hook.HookId` from plugin code gives CS0122. Corrected.
- **V4 — Summary item 8 (config).** "A list or dictionary … comes back with those items duplicated": verified for `List`. A `Dictionary` with default entries does not duplicate (same keys are overwritten), but default keys the user deleted reappear on reload. Corrected; the advice (use `ObjectCreationHandling.Replace`) is unchanged.
- **V5 — Summary item 11 (packaging).** "`bin/Release/<Name>/<Name>.json`": the folder and file are named after `$(AssemblyName)` (= InternalName), not the display `<Name>`. Probe proof: Name "Verify Probe", AssemblyName VerifyProbe gave `bin/Release/VerifyProbe/VerifyProbe.json` + `latest.zip`; DalamudPackager.cs `CreateZip` uses `Path.Combine(OutputPath, AssemblyName)`. Corrected.
- **V6 — Fact 44.** "ImU8String cannot be a field (CS8345)" is too broad. It cannot be a field of a class or ordinary struct, but it compiles as an instance field of a `ref struct`. Corrected.
- **V7 — Omission (facts 19, 61).** The "Dev Plugin Locations" setting only appears after ticking "Enable Developer Mode" in /xlsettings > Experimental (SettingsTabExperimental.cs: `new DevPluginsSettingsEntry(visibility: () => this.devModeEntry.Value)`, bound to `DalamudConfiguration.DevMode`). DevMode is exactly what arms `ThreadSafety.DevModeAssertMainThread`, so anyone running MitCue as a dev plugin gets the IGameObject/ICharacter/IBattleChara/IPartyMember/IStatus main-thread asserts. That helper is marked `[Api16ToDo("Replace all calls with AssertMainThread")]`, so they become hard asserts for everyone in API 16. Added to both facts.
- **V8 — Omission (fact 36).** Importing FFXIVClientStructs namespaces (needed for ActionManager, hooks, AgentCountDownSettingDialog) together with Dalamud/Lumina namespaces causes CS0104. `ObjectKind` and `BattleNpcSubKind` exist in both `FFXIVClientStructs.FFXIV.Client.Game.Object` and `Dalamud.Game.ClientState.Objects.Enums`, and `Status` in both `FFXIVClientStructs.FFXIV.Client.Game` and `Lumina.Excel.Sheets`. Found while compiling the verification probe. Added with the alias fix.
- **Minor fixes.** Fact 13 now says that IsClickthrough also adds NoMove|NoResize (WindowHost.cs:273-276: `if (IsPinned || IsClickthrough)`). The stale "I did NOT write this file" summary text and the matching open question were replaced with a historical note.

### Left as is / still unverified

- Fact 18: Draw runs on the main thread. The source evidence is still indirect; the in-game `ThreadSafety.IsMainThread` check remains recommended.
- Fact 26 says "(or LocalPlayer.Level)". That `ICharacter.Level` equals the synced level was not verified (no game client). Prefer `IPlayerState.EffectiveLevel`, which is source-verified.
- Facts 35 and 56 and the Open questions remain UNVERIFIED as marked. They need the game client.
- Appendix code comments that make the same unverified claims (`Length is 0 when not in a party`, the `0x0139 (party member)` code) were left untouched, so the appendix stays identical to the compiled probe.
- Not errors, but useful:
  - `ImGui.Image` also accepts a `ulong`, because `ImTextureID` has an implicit conversion from `ulong`. Only `nint` fails.
  - `IDalamudPluginInterface.CheckForUpdateAsync()` exists (IDalamudPluginInterface.cs:358-364). It could tell the static that a MitCue update is available.
  - `ITextureProvider.TryGetFromGameIcon` declares `[NotNullWhen(true)] out ISharedImmediateTexture? texture`.
