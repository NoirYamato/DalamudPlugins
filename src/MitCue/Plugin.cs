using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MitCue.Core;
using MitCue.Data;
using MitCue.Game;
using MitCue.Plans;
using MitCue.Runtime;
using MitCue.UI;
using MitCue.Util;

namespace MitCue;

/// <summary>
/// MitCue entry point: owns the services and components, samples the game once per framework tick,
/// feeds the live session and the preview, and hosts the windows and the /mitcue command.
/// Reminder-only: nothing here uses actions, targets or sends chat commands.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/mitcue";

    private readonly WindowSystem windows = new("MitCue");
    private readonly MonotonicClock clock = new();
    private readonly OwnUseDeduper deduper = new();
    private readonly List<OwnUse> fallbackUses = new(8);
    private readonly ConfigWindow configWindow;
    private readonly OverlayWindow overlayWindow;
    private uint lastTerritory;
    private bool soundRequested;
    private bool disposed;

    /// <summary>Creates the plugin. Runs off the main thread: no object-table or texture access here.</summary>
    public Plugin()
    {
        this.Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.Config.Sanitize();
        this.Version = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "?";

        this.Errors = new ErrorReporter(Log);
        this.GameData = new GameData(DataManager);
        this.Data = new CoreData(this.Errors, () => this.Config.UserAliases);
        this.Plans = new PlanStore(PluginInterface.ConfigDirectory, this.Config, this.SaveConfig, this.Errors);
        this.Cooldowns = new CooldownProvider(this.Data, this.GameData, JobGauges, this.Errors);
        this.Events = new GameEventSource(Interop, ObjectTable, Condition, DutyState, ClientState, this.clock, this.Errors);
        this.Recorder = new PullRecorder(PluginInterface.ConfigDirectory, this.Errors, this.Version);
        this.Sessions = new SessionManager(this.Config, this.Data, this.GameData, this.Plans, this.Cooldowns, this.Errors, Chat, this.RequestSound);
        this.Preview = new PreviewDriver(this.Config, this.Data, this.GameData, this.Plans, this.Errors, Chat, this.RequestSound);

        this.configWindow = new ConfigWindow(this);
        this.overlayWindow = new OverlayWindow(this);
        this.windows.AddWindow(this.configWindow);
        this.windows.AddWindow(this.overlayWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open settings. Subcommands: overlay (toggle the reminder window), preview [fight] [phase] (start/stop a simulated pull), reset (reset the live session), debug (open the Debug tab).",
            ShowInHelp = true,
        });

        PluginInterface.UiBuilder.Draw += this.DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += this.ToggleConfig;
        PluginInterface.UiBuilder.OpenMainUi += this.ToggleConfig;
        Framework.Update += this.OnFrameworkUpdate;
    }

    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static IFramework Framework { get; private set; } = null!;

    [PluginService]
    internal static IClientState ClientState { get; private set; } = null!;

    [PluginService]
    internal static IObjectTable ObjectTable { get; private set; } = null!;

    [PluginService]
    internal static IPlayerState PlayerState { get; private set; } = null!;

    [PluginService]
    internal static ICondition Condition { get; private set; } = null!;

    [PluginService]
    internal static IDutyState DutyState { get; private set; } = null!;

    [PluginService]
    internal static IDataManager DataManager { get; private set; } = null!;

    [PluginService]
    internal static ITextureProvider TextureProvider { get; private set; } = null!;

    [PluginService]
    internal static IGameInteropProvider Interop { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IChatGui Chat { get; private set; } = null!;

    [PluginService]
    internal static IJobGauges JobGauges { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    internal Configuration Config { get; }

    internal string Version { get; }

    internal ErrorReporter Errors { get; }

    internal GameData GameData { get; }

    internal CoreData Data { get; }

    internal PlanStore Plans { get; }

    internal CooldownProvider Cooldowns { get; }

    internal GameEventSource Events { get; }

    internal PullRecorder Recorder { get; }

    internal SessionManager Sessions { get; }

    internal PreviewDriver Preview { get; }

    /// <summary>What the overlay shows this frame (built on the framework thread, drawn in Draw).</summary>
    internal OverlayModel Overlay { get; } = new();

    /// <summary>Game state sampled this frame.</summary>
    internal FrameContext Frame { get; } = new();

    /// <summary>Action ids of the live and preview reminders (for the cooldown-based usage fallback).</summary>
    internal HashSet<uint> TrackedActionIds { get; } = [];

    /// <summary>Recently detected own action uses, for the Debug tab.</summary>
    internal RingBuffer<OwnUse> RecentOwnUses { get; } = new(30);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (this.disposed)
            return;
        this.disposed = true;

        Framework.Update -= this.OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= this.DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= this.ToggleConfig;
        PluginInterface.UiBuilder.OpenMainUi -= this.ToggleConfig;
        CommandManager.RemoveHandler(CommandName);
        this.windows.RemoveAllWindows();

        this.Preview.Stop();
        this.Events.Dispose();
        this.Recorder.Dispose();
        this.SaveConfig();
    }

    /// <summary>Saves the configuration and tells running sessions to pick up changes.</summary>
    internal void SaveConfig()
    {
        this.Config.Touch();
        PluginInterface.SavePluginConfig(this.Config);
    }

    internal void ResetOverlayPosition() => this.overlayWindow.ResetPosition();

    internal void ResetLiveSession() => this.Sessions.Reset();

    /// <summary>Plays the reminder sound on the next framework tick (also used by the Test button).</summary>
    internal void RequestTestSound() => this.soundRequested = true;

    internal void ReloadData()
    {
        this.Preview.Stop();
        this.Data.Reload();
        this.Sessions.Invalidate();
    }

    /// <summary>Hook for the sheet import window (milestone 4).</summary>
    internal void OpenImport() =>
        Chat.Print("[MitCue] Importing from a sheet is coming in the next update. For now, use a share code or a bundled plan.");

    private void RequestSound()
    {
        if (this.Config.SoundEnabled)
            this.soundRequested = true;
    }

    private void ToggleConfig() => this.configWindow.Toggle();

    private void DrawUi()
    {
        try
        {
            this.windows.Draw();
        }
        catch (Exception ex)
        {
            this.Errors.Report("draw", "Drawing MitCue windows", ex);
        }
    }

    private void OnCommand(string command, string arguments)
    {
        var args = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sub = args.Length > 0 ? args[0].ToLowerInvariant() : string.Empty;
        switch (sub)
        {
            case "":
                this.ToggleConfig();
                break;
            case "overlay":
                this.Config.OverlayEnabled = !this.Config.OverlayEnabled;
                this.SaveConfig();
                Chat.Print($"[MitCue] Reminder window {(this.Config.OverlayEnabled ? "shown" : "hidden")}.");
                break;
            case "preview":
                if (this.Preview.IsActive && args.Length == 1)
                {
                    this.Preview.Stop();
                    Chat.Print("[MitCue] Preview stopped.");
                    break;
                }

                var fight = args.Length > 1 ? args[1] : null;
                var phase = args.Length > 2 ? args[2] : null;
                this.Preview.Start(fight, phase, this.Frame.Territory, this.Frame.Job, this.clock.Now, out var message);
                Chat.Print("[MitCue] " + message);
                break;
            case "reset":
                this.Sessions.Reset();
                Chat.Print("[MitCue] Live session reset.");
                break;
            case "debug":
                this.configWindow.IsOpen = true;
                this.configWindow.OpenDebug();
                break;
            default:
                Chat.Print("[MitCue] Usage: /mitcue [overlay | preview [fight] [phase] | reset | debug]");
                break;
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            this.Tick();
        }
        catch (Exception ex)
        {
            this.Errors.Report("frame", "Updating MitCue", ex);
        }
    }

    /// <summary>One framework tick: sample → events → own uses → record → sessions → preview → overlay → sound.</summary>
    private void Tick()
    {
        var ctx = this.Frame;
        var now = this.clock.Now;
        ctx.Now = now;
        this.SampleGameState(ctx);

        if (ctx.Territory != this.lastTerritory)
        {
            this.lastTerritory = ctx.Territory;
            this.deduper.Clear();
            this.Cooldowns.ResetTracking();
        }

        // Event capture is only needed while something consumes it.
        var needEvents = this.Sessions.Session != null || this.Preview.IsActive || this.Config.RecordPulls;
        this.Events.PollCasts = needEvents;
        this.Events.Capture = needEvents;
        this.Events.Poll(now, ctx.LocalEntityId);
        ctx.Countdown = this.Events.Countdown;
        ctx.Events = this.Events.FrameEvents;
        ctx.PartyUses = this.Events.FramePartyUses;

        // Own uses: ActionEffect path first, then the cooldown fallback, de-duplicated across both.
        ctx.OwnUses.Clear();
        foreach (var use in this.Events.FrameOwnUses)
        {
            if (this.deduper.Accept(use))
                ctx.OwnUses.Add(use);
        }

        this.RebuildTrackedActions();
        this.Cooldowns.Job = ctx.Job;
        this.Cooldowns.Available = ctx.LoggedIn && ctx.LocalEntityId != 0;
        this.fallbackUses.Clear();
        this.Cooldowns.DetectUses(now, this.TrackedActionIds, this.fallbackUses);
        foreach (var use in this.fallbackUses)
        {
            if (this.deduper.Accept(use))
                ctx.OwnUses.Add(use);
        }

        foreach (var use in ctx.OwnUses)
            this.RecentOwnUses.Add(use);

        this.Recorder.Update(this.Config.RecordPulls, ctx.Territory, now, ctx.ClientLanguage);
        if (this.Recorder.Active)
            this.Recorder.Write(now, ctx.Events, ctx.OwnUses, ctx.Countdown);

        this.Sessions.Update(ctx);
        if (this.Preview.IsActive)
            this.Preview.Update(now, ctx.OwnUses);

        this.Overlay.Build(this.Config, this.Sessions.ActiveSession, this.Preview.Session, this.Preview.Speed, this.GameData, this.Cooldowns.ResourceName);

        if (this.soundRequested)
        {
            this.soundRequested = false;
            SoundPlayer.Play(this.Config.SoundEffect, this.Errors);
        }
    }

    private void SampleGameState(FrameContext ctx)
    {
        ctx.LoggedIn = ClientState.IsLoggedIn;
        ctx.Territory = ClientState.TerritoryType;
        ctx.ClientIsEnglish = ClientState.ClientLanguage == ClientLanguage.English;
        ctx.ClientLanguage = ClientState.ClientLanguage.ToString();

        var local = ctx.LoggedIn ? ObjectTable.LocalPlayer : null;
        ctx.LocalEntityId = local?.EntityId ?? 0;

        uint classJob = 0;
        var level = 0;
        if (PlayerState.IsLoaded)
        {
            classJob = PlayerState.ClassJob.RowId;
            level = PlayerState.EffectiveLevel;
        }
        else if (local != null)
        {
            classJob = local.ClassJob.RowId;
            level = local.Level;
        }

        ctx.ClassJobId = classJob;
        ctx.Job = classJob is > 0 and < 256 && Enum.IsDefined(typeof(Job), (byte)classJob) ? (Job)(byte)classJob : Job.None;
        ctx.Level = level;
        ctx.InCombat = Condition[ConditionFlag.InCombat];
        ctx.BoundByDuty = Condition.Any(ConditionFlag.BoundByDuty, ConditionFlag.BoundByDuty56, ConditionFlag.BoundByDuty95);
    }

    private void RebuildTrackedActions()
    {
        var tracked = this.TrackedActionIds;
        tracked.Clear();
        if (this.Sessions.ActiveSession is { } live)
            tracked.UnionWith(live.TrackedActionIds);
        if (this.Preview.Session is { } preview)
            tracked.UnionWith(preview.TrackedActionIds);
    }
}
