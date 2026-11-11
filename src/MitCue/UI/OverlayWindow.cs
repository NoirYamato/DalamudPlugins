using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using MitCue.Core.Reminders;
using MitCue.Runtime;

namespace MitCue.UI;

/// <summary>
/// The small reminder window: up to MaxRows rows (icon, action, countdown, mechanic, target/note, warnings),
/// coloured per state, due rows pulsing. Renders the <see cref="OverlayModel"/> snapshot built in
/// Framework.Update. Position is saved in MitCue's config (NoSavedSettings), Dalamud's pin/click-through
/// toggles are disabled, and flags are switched in <see cref="PreDraw"/> (dalamud-api.md §13-14).
/// </summary>
internal sealed class OverlayWindow : Window
{
    private const ImGuiWindowFlags BaseFlags =
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse
        | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing
        | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoNav;

    private const ImGuiWindowFlags MovableFlags = BaseFlags;

    private const ImGuiWindowFlags LockedFlags = BaseFlags | ImGuiWindowFlags.NoMove;

    private const ImGuiWindowFlags ClickThroughFlags = BaseFlags | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoMove;

    private static readonly Vector4 ColorUpcoming = new(0.93f, 0.93f, 0.93f, 1f);
    private static readonly Vector4 ColorDue = new(1f, 0.80f, 0.20f, 1f);
    private static readonly Vector4 ColorDone = new(0.45f, 0.86f, 0.45f, 1f);
    private static readonly Vector4 ColorMissed = new(0.96f, 0.36f, 0.36f, 1f);
    private static readonly Vector4 ColorMuted = new(0.62f, 0.62f, 0.62f, 1f);
    private static readonly Vector4 ColorWarning = new(1f, 0.58f, 0.18f, 1f);
    private static readonly Vector4 ColorHeader = new(0.70f, 0.82f, 1f, 1f);
    private static readonly string CheckGlyph = FontAwesomeIcon.Check.ToIconString();
    private static readonly string CrossGlyph = FontAwesomeIcon.Times.ToIconString();

    private readonly Plugin plugin;
    private Vector2? pendingPosition;

    /// <summary>Creates the overlay at the saved position.</summary>
    public OverlayWindow(Plugin plugin)
        : base("MitCue###MitCueOverlay", MovableFlags, forceMainWindow: true)
    {
        this.plugin = plugin;
        this.IsOpen = true;
        this.RespectCloseHotkey = false;
        this.ShowCloseButton = false;
        this.AllowPinning = false;
        this.AllowClickthrough = false;
        this.DisableWindowSounds = true;
        this.DisableFadeInFadeOut = true;
        var cfg = plugin.Config;
        this.pendingPosition = new Vector2(cfg.OverlayX, cfg.OverlayY);
    }

    /// <summary>Moves the overlay back to the default position.</summary>
    public void ResetPosition()
    {
        var cfg = this.plugin.Config;
        cfg.OverlayX = 100f;
        cfg.OverlayY = 300f;
        cfg.HasOverlayPosition = false;
        this.plugin.SaveConfig();
        this.pendingPosition = new Vector2(cfg.OverlayX, cfg.OverlayY);
    }

    /// <inheritdoc/>
    public override bool DrawConditions()
    {
        try
        {
            return Plugin.ClientState.IsLoggedIn && this.plugin.Config.OverlayEnabled && this.plugin.Overlay.Visible;
        }
        catch (Exception ex)
        {
            this.plugin.Errors.Report("ui.overlay.conditions", "Showing the overlay", ex);
            return false;
        }
    }

    /// <inheritdoc/>
    public override void PreDraw()
    {
        var cfg = this.plugin.Config;
        this.Flags = cfg.OverlayClickThrough ? ClickThroughFlags : cfg.OverlayLocked ? LockedFlags : MovableFlags;
        this.BgAlpha = cfg.OverlayOpacity;

        if (this.pendingPosition is { } position)
        {
            this.Position = position;
            this.PositionCondition = ImGuiCond.Always;
            this.pendingPosition = null;
        }
        else
        {
            this.Position = null;
        }
    }

    /// <inheritdoc/>
    public override void Draw()
    {
        try
        {
            this.DrawContent();
        }
        catch (Exception ex)
        {
            this.plugin.Errors.Report("ui.overlay", "Drawing the overlay", ex);
        }
    }

    private static Vector4 StateColor(ReminderState state) => state switch
    {
        ReminderState.Due => ColorDue,
        ReminderState.Done => ColorDone,
        ReminderState.Missed => ColorMissed,
        ReminderState.Skipped or ReminderState.Passed or ReminderState.Pending => ColorMuted,
        _ => ColorUpcoming,
    };

    private static Vector4 WithAlpha(Vector4 color, float alpha) => new(color.X, color.Y, color.Z, color.W * alpha);

    private void DrawContent()
    {
        var cfg = this.plugin.Config;
        var model = this.plugin.Overlay;
        ImGui.SetWindowFontScale(cfg.OverlayScale);

        if (cfg.ShowHeader || model.Rows.Count == 0)
            this.DrawHeader(model);

        if (model.Rows.Count > 0)
            this.DrawRows(cfg, model);

        if (!cfg.OverlayClickThrough && !cfg.OverlayLocked)
            this.TrackPosition(cfg);

        ImGui.SetWindowFontScale(1f);
    }

    private void DrawHeader(OverlayModel model)
    {
        using var color = ImRaii.PushColor(ImGuiCol.Text, ColorHeader);
        if (model.Source == OverlaySource.Preview)
        {
            ImGui.TextUnformatted($"PREVIEW {model.PreviewSpeed:0}x");
            ImGui.SameLine();
        }

        switch (model.Mode)
        {
            case SessionMode.Countdown:
                ImGui.TextUnformatted($"{model.FightShort} - pull in {model.Countdown:0.0}s");
                break;
            case SessionMode.Running:
                var t = Math.Max(0, model.TimelineTime);
                var minutes = (int)(t / 60);
                var seconds = (int)(t - (minutes * 60));
                if (model.PhaseName != null)
                    ImGui.TextUnformatted($"{model.FightShort} - {model.PhaseName} - {minutes}:{seconds:00}");
                else
                    ImGui.TextUnformatted($"{model.FightShort} - {minutes}:{seconds:00}");
                break;
            default:
                ImGui.TextUnformatted($"{model.FightShort} - waiting for pull ({model.PlanName})");
                break;
        }
    }

    private void DrawRows(Configuration cfg, OverlayModel model)
    {
        var columns = 3 + (cfg.ShowIcons ? 1 : 0) + (cfg.ShowMechanic ? 1 : 0) + (cfg.ShowTargetAndNotes ? 1 : 0);
        using var table = ImRaii.Table("##mitcueRows", columns, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX);
        if (!table)
            return;

        var iconSize = new Vector2(ImGui.GetFrameHeight());
        var pulse = 0.55f + (0.45f * MathF.Sin((float)ImGui.GetTime() * 6f));
        var textures = Plugin.TextureProvider;

        // Fixed widths for the icon and countdown columns so rows do not jitter as numbers change.
        if (cfg.ShowIcons)
            ImGui.TableSetupColumn("##icon", ImGuiTableColumnFlags.WidthFixed, iconSize.X);
        ImGui.TableSetupColumn("##time", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("00.0s").X);

        foreach (var row in model.Rows)
        {
            var labels = row.Labels;
            var alpha = row.IsOther ? 0.6f : 1f;
            var color = WithAlpha(StateColor(row.State), alpha);
            var finished = row.State is ReminderState.Done or ReminderState.Missed or ReminderState.Skipped or ReminderState.Passed;

            ImGui.TableNextRow();
            if (row.State == ReminderState.Due && !row.IsOther)
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(1f, 0.55f, 0.1f, 0.30f * pulse)));

            // Icon (fetched every frame, never cached or disposed).
            if (cfg.ShowIcons)
            {
                ImGui.TableNextColumn();
                if (labels.IconId != 0)
                {
                    var wrap = textures.GetFromGameIcon(new GameIconLookup(labels.IconId)).GetWrapOrEmpty();
                    var tint = new Vector4(1f, 1f, 1f, finished ? 0.45f * alpha : alpha);
                    ImGui.Image(wrap.Handle, iconSize, Vector2.Zero, Vector2.One, tint);
                }
                else
                {
                    ImGui.Dummy(iconSize);
                }
            }

            // Countdown / state.
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            this.DrawTimeCell(row, color, pulse);

            // Action name (with the other assignee's tag).
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            if (row.IsOther && labels.Tag != null)
                ImGui.TextColored(WithAlpha(ColorMuted, alpha), $"[{labels.Tag}] {labels.Name}");
            else if (row.UsedEarly && row.State == ReminderState.Done)
                ImGui.TextColored(color, $"{labels.Name} (early)");
            else
                ImGui.TextColored(color, labels.Name);

            if (cfg.ShowMechanic)
            {
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(WithAlpha(ColorMuted, alpha), labels.Mechanic);
            }

            if (cfg.ShowTargetAndNotes)
            {
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                if (labels.TargetAndNote != null)
                    ImGui.TextColored(WithAlpha(ColorUpcoming, 0.85f * alpha), labels.TargetAndNote);
            }

            // Cooldown/resource warning.
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            if (cfg.ShowCooldownWarnings && !finished && !row.IsOther)
            {
                if (row.ReadyIn is { } readyIn && readyIn > 0)
                    ImGui.TextColored(ColorWarning, $"ready in {Math.Ceiling(readyIn):0}s");
                else if (row.MissingResources)
                    ImGui.TextColored(ColorWarning, labels.Resource != null ? $"no {labels.Resource}" : "no resource");
            }
        }
    }

    private void DrawTimeCell(in OverlayRow row, Vector4 color, float pulse)
    {
        switch (row.State)
        {
            case ReminderState.Due:
                ImGui.TextColored(WithAlpha(color, 0.5f + (0.5f * pulse)), "NOW");
                break;
            case ReminderState.Done:
                using (ImRaii.PushFont(UiBuilder.IconFont))
                    ImGui.TextColored(color, CheckGlyph);
                break;
            case ReminderState.Missed:
                using (ImRaii.PushFont(UiBuilder.IconFont))
                    ImGui.TextColored(color, CrossGlyph);
                break;
            case ReminderState.Skipped:
                ImGui.TextColored(color, "skip");
                break;
            case ReminderState.Passed:
                ImGui.TextColored(color, "-");
                break;
            default:
                var s = Math.Max(0, row.Seconds);
                if (s < 10)
                    ImGui.TextColored(color, $"{s:0.0}s");
                else
                    ImGui.TextColored(color, $"{s:0}s");
                break;
        }
    }

    private void TrackPosition(Configuration cfg)
    {
        // Save the position once a drag ends (relative to the main viewport, as Position is applied).
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            return;
        var position = ImGui.GetWindowPos() - ImGuiHelpers.MainViewport.Pos;
        if (Math.Abs(position.X - cfg.OverlayX) > 0.5f || Math.Abs(position.Y - cfg.OverlayY) > 0.5f)
        {
            cfg.OverlayX = position.X;
            cfg.OverlayY = position.Y;
            cfg.HasOverlayPosition = true;
            this.plugin.SaveConfig();
        }
    }
}
