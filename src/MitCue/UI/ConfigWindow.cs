using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using MitCue.Core;
using MitCue.Core.Fights;
using MitCue.Core.Plans;
using MitCue.Plans;
using MitCue.Util;

namespace MitCue.UI;

/// <summary>
/// Settings window: General, Overlay, Plans, Debug and About tabs. Values apply immediately (the runtime
/// watches <see cref="Configuration.Revision"/>) and are saved once no widget is being edited.
/// </summary>
internal sealed class ConfigWindow : Window
{
    /// <summary>Byte limit of the share-code box (the default 512 bytes is far too small).</summary>
    private const int ShareCodeMaxBytes = 512 * 1024;

    private static readonly Vector4 ColorError = new(1f, 0.45f, 0.40f, 1f);
    private static readonly Vector4 ColorWarning = new(1f, 0.75f, 0.30f, 1f);
    private static readonly Vector4 ColorOk = new(0.50f, 0.88f, 0.50f, 1f);

    private readonly Plugin plugin;
    private string shareCode = string.Empty;
    private string shareMessage = string.Empty;
    private bool shareMessageIsError;
    private string? confirmDeleteId;
    private bool selectDebugTab;
    private string debugText = string.Empty;
    private double debugBuiltAt = double.NegativeInfinity;
    private bool dirty;

    /// <summary>Creates the window (closed).</summary>
    public ConfigWindow(Plugin plugin)
        : base("MitCue###MitCueConfig")
    {
        this.plugin = plugin;
        this.Size = new Vector2(720, 560);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 360),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    /// <summary>Opens the window on the Debug tab.</summary>
    public void OpenDebug()
    {
        this.IsOpen = true;
        this.selectDebugTab = true;
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
            this.plugin.Errors.Report("ui.config", "Drawing the settings window", ex);
        }

        // Save once editing is over (not every frame while a slider is dragged).
        if (this.dirty && !ImGui.IsAnyItemActive())
        {
            this.dirty = false;
            this.plugin.SaveConfig();
        }
    }

    /// <inheritdoc/>
    public override void OnClose()
    {
        if (this.dirty)
        {
            this.dirty = false;
            this.plugin.SaveConfig();
        }
    }

    private static string AssigneeLabel(Assignee a)
    {
        var details = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(a.Slot))
            details.Add(a.Slot);
        if (a.Job is { } job && job != Job.None)
            details.Add(job.ToString());
        return details.Count > 0 && !details.Contains(a.Key, StringComparer.OrdinalIgnoreCase)
            ? $"{a.Key} ({string.Join(", ", details)})"
            : a.Key;
    }

    private void Changed()
    {
        this.plugin.Config.Touch();
        this.dirty = true;
    }

    private void DrawContent()
    {
        this.DrawStatus();

        using var tabs = ImRaii.TabBar("##mitcueTabs");
        if (!tabs)
            return;

        this.Tab("General", this.DrawGeneral);
        this.Tab("Overlay", this.DrawOverlay);
        this.Tab("Plans", this.DrawPlans);

        var debugFlags = this.selectDebugTab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        this.selectDebugTab = false;
        using (var debug = ImRaii.TabItem("Debug", debugFlags))
        {
            if (debug)
                this.Guarded("ui.config.debug", "Drawing the Debug tab", this.DrawDebug);
        }

        this.Tab("About", this.DrawAbout);
    }

    private void Tab(string label, Action body)
    {
        using var tab = ImRaii.TabItem(label);
        if (tab)
            this.Guarded("ui.config." + label, $"Drawing the {label} tab", body);
    }

    private void Guarded(string key, string context, Action body)
    {
        try
        {
            body();
        }
        catch (Exception ex)
        {
            this.plugin.Errors.Report(key, context, ex);
            ImGui.TextColored(ColorError, "This tab hit an error: " + ErrorReporter.Describe(ex));
        }
    }

    private void DrawStatus()
    {
        var sessions = this.plugin.Sessions;
        ImGui.TextWrapped("Status: " + sessions.Status);
        if (this.plugin.Preview.IsActive)
            ImGui.TextColored(ColorOk, "Preview running: " + this.plugin.Preview.Description + " (/mitcue preview to stop)");

        var problems = this.plugin.Errors.Snapshot();
        if (problems.Length > 0)
        {
            using var color = ImRaii.PushColor(ImGuiCol.Text, ColorWarning);
            if (ImGui.CollapsingHeader($"Problems ({problems.Length})###mitcueProblems"))
            {
                using var reset = ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
                foreach (var p in problems)
                    ImGui.TextWrapped($"{p.Context}: {p.Message}");
            }
        }

        ImGui.Separator();
    }

    // ---- General ------------------------------------------------------------------------

    private void DrawGeneral()
    {
        var cfg = this.plugin.Config;

        ImGui.TextDisabled("Timing");
        var lookahead = cfg.Lookahead;
        if (ImGui.SliderFloat("Show reminders this early", ref lookahead, 3f, 60f, "%.0f s"))
        {
            cfg.Lookahead = lookahead;
            this.Changed();
        }

        var dueLead = cfg.DueLead;
        if (ImGui.SliderFloat("Turn \"NOW\" this early", ref dueLead, 0f, 10f, "%.1f s"))
        {
            cfg.DueLead = dueLead;
            this.Changed();
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Sound");
        var sound = cfg.SoundEnabled;
        if (ImGui.Checkbox("Play a sound when a reminder turns NOW", ref sound))
        {
            cfg.SoundEnabled = sound;
            this.Changed();
        }

        var effect = cfg.SoundEffect;
        ImGui.SetNextItemWidth(160 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt("Sound effect <se.N>", ref effect, Configuration.MinSoundEffect, Configuration.MaxSoundEffect))
        {
            cfg.SoundEffect = Math.Clamp(effect, Configuration.MinSoundEffect, Configuration.MaxSoundEffect);
            this.Changed();
        }

        ImGui.SameLine();
        if (ImGui.Button("Test"))
            this.plugin.RequestTestSound();

        ImGui.Spacing();
        ImGui.TextDisabled("Reminders");
        var summary = cfg.PostPullSummary;
        if (ImGui.Checkbox("Print a done/missed summary after each pull", ref summary))
        {
            cfg.PostPullSummary = summary;
            this.Changed();
        }

        var others = cfg.ShowOtherAssignees;
        if (ImGui.Checkbox("Also show the plan's other columns (e.g. the co-healer), dimmed", ref others))
        {
            cfg.ShowOtherAssignees = others;
            this.Changed();
        }

        var skipped = cfg.ShowSkipped;
        if (ImGui.Checkbox("Show reminders skipped by a phase push", ref skipped))
        {
            cfg.ShowSkipped = skipped;
            this.Changed();
        }

        var slotLabel = cfg.DefaultSlot ?? "(not set)";
        ImGui.SetNextItemWidth(160 * ImGuiHelpers.GlobalScale);
        using (var combo = ImRaii.Combo("My usual party slot", slotLabel))
        {
            if (combo)
            {
                if (ImGui.Selectable("(not set)", cfg.DefaultSlot == null))
                {
                    cfg.DefaultSlot = null;
                    this.Changed();
                }

                foreach (var slot in Configuration.SlotChoices)
                {
                    if (ImGui.Selectable(slot, cfg.DefaultSlot == slot))
                    {
                        cfg.DefaultSlot = slot;
                        this.Changed();
                    }
                }
            }
        }

        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Used to suggest which column of a plan is yours when your job alone is not enough.");

        var fast = cfg.PreviewFast;
        if (ImGui.Checkbox("Run /mitcue preview at 4x speed", ref fast))
        {
            cfg.PreviewFast = fast;
            this.Changed();
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Recording");
        var record = cfg.RecordPulls;
        if (ImGui.Checkbox("Record pulls (event log for tuning the sync; no player names)", ref record))
        {
            cfg.RecordPulls = record;
            this.Changed();
        }

        ImGui.TextDisabled("Folder: " + this.plugin.Recorder.Directory);
        if (this.plugin.Recorder.CurrentFile is { } file)
            ImGui.TextColored(ColorOk, $"Recording to {Path.GetFileName(file)} ({this.plugin.Recorder.LinesWritten} lines)");
    }

    // ---- Overlay ------------------------------------------------------------------------

    private void DrawOverlay()
    {
        var cfg = this.plugin.Config;

        this.Check("Show the overlay (/mitcue overlay)", cfg.OverlayEnabled, v => cfg.OverlayEnabled = v);
        this.Check("Lock position", cfg.OverlayLocked, v => cfg.OverlayLocked = v);
        this.Check("Click-through (mouse clicks pass through; implies locked)", cfg.OverlayClickThrough, v => cfg.OverlayClickThrough = v);
        this.Check("Always show (sample rows when idle, to position it)", cfg.AlwaysShowOverlay, v => cfg.AlwaysShowOverlay = v);

        var scale = cfg.OverlayScale;
        if (ImGui.SliderFloat("Scale", ref scale, 0.5f, 3f, "%.2fx"))
        {
            cfg.OverlayScale = scale;
            this.Changed();
        }

        var opacity = cfg.OverlayOpacity;
        if (ImGui.SliderFloat("Background opacity", ref opacity, 0f, 1f, "%.2f"))
        {
            cfg.OverlayOpacity = opacity;
            this.Changed();
        }

        var rows = cfg.MaxRows;
        if (ImGui.SliderInt("Maximum rows", ref rows, 1, 15))
        {
            cfg.MaxRows = Math.Clamp(rows, 1, 15);
            this.Changed();
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Columns");
        this.Check("Action icons", cfg.ShowIcons, v => cfg.ShowIcons = v);
        this.Check("Mechanic name", cfg.ShowMechanic, v => cfg.ShowMechanic = v);
        this.Check("Target and notes", cfg.ShowTargetAndNotes, v => cfg.ShowTargetAndNotes = v);
        this.Check("Cooldown / resource warnings (\"ready in 6s\", \"no Lily\")", cfg.ShowCooldownWarnings, v => cfg.ShowCooldownWarnings = v);
        this.Check("Header line (fight, phase, clock)", cfg.ShowHeader, v => cfg.ShowHeader = v);

        ImGui.Spacing();
        if (ImGui.Button("Reset position"))
            this.plugin.ResetOverlayPosition();
        ImGui.SameLine();
        ImGui.TextDisabled("Unlock it and drag it anywhere; the position is remembered.");
    }

    private void Check(string label, bool value, Action<bool> set)
    {
        if (ImGui.Checkbox(label, ref value))
        {
            set(value);
            this.Changed();
        }
    }

    // ---- Plans --------------------------------------------------------------------------

    private void DrawPlans()
    {
        var store = this.plugin.Plans;
        var data = this.plugin.Data;
        if (!store.Loaded || !data.Loaded)
        {
            ImGui.TextDisabled("Loading...");
            return;
        }

        ImGui.TextDisabled("Active plan per fight and job");
        if (data.FightList.Count == 0)
        {
            ImGui.TextWrapped("No fight definitions are loaded yet, so plans cannot be activated. See Problems above.");
        }
        else
        {
            this.DrawActivePlans(store, data.FightList);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextDisabled($"Plans ({store.All.Count})");
        this.DrawPlanList(store);

        ImGui.Spacing();
        ImGui.Separator();
        this.DrawImport(store);
    }

    private void DrawActivePlans(PlanStore store, IReadOnlyList<FightDefinition> fights)
    {
        var currentJob = this.plugin.Frame.Job;
        using var table = ImRaii.Table("##activePlans", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;

        ImGui.TableSetupColumn("Fight", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed, 50 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Active plan", ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableSetupColumn("My column", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        ImGui.TableHeadersRow();

        foreach (var fight in fights)
        {
            var plansForFight = store.ForFight(fight.Key).ToList();
            var jobs = new SortedSet<Job>(plansForFight.SelectMany(p => PlanStore.JobsOf(p.Plan)));
            if (currentJob != Job.None)
                jobs.Add(currentJob);
            if (jobs.Count == 0)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(fight.Name);
                ImGui.TableNextColumn();
                ImGui.TableNextColumn();
                ImGui.TextDisabled("no plans for this fight yet");
                ImGui.TableNextColumn();
                continue;
            }

            foreach (var job in jobs)
            {
                using var id = ImRaii.PushId($"{fight.Key}/{job}");
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(fight.Name);
                ImGui.TableNextColumn();
                if (job == currentJob)
                    ImGui.TextColored(ColorOk, job.ToString());
                else
                    ImGui.TextUnformatted(job.ToString());

                ImGui.TableNextColumn();
                var active = this.DrawActivePlanCombo(store, fight, job, plansForFight);

                ImGui.TableNextColumn();
                if (active != null)
                    this.DrawColumnCombo(store, active, job);
                else
                    ImGui.TextDisabled("-");
            }
        }
    }

    private StoredPlan? DrawActivePlanCombo(PlanStore store, FightDefinition fight, Job job, List<StoredPlan> plansForFight)
    {
        var active = store.ActivePlanFor(fight.Key, job, out var automatic);
        var explicitNone = this.plugin.Config.ActivePlans.TryGetValue(PlanStore.ActiveKey(fight.Key, job), out var chosenId) && chosenId.Length == 0;
        var preview = explicitNone ? "(no plan)"
            : active == null ? "(automatic: none)"
            : automatic ? $"(automatic) {active.DisplayName}"
            : active.DisplayName;

        ImGui.SetNextItemWidth(-1);
        using var combo = ImRaii.Combo("##plan", preview);
        if (!combo)
            return active;

        if (ImGui.Selectable("(automatic: first plan with a column for this job)", automatic && !explicitNone))
            store.SetActive(fight.Key, job, null);
        if (ImGui.Selectable("(no plan: no reminders for this job)", explicitNone))
            store.SetActive(fight.Key, job, string.Empty);
        foreach (var p in plansForFight)
        {
            if (ImGui.Selectable($"{p.DisplayName} [{p.SourceLabel}]###{p.Id}", !automatic && active == p))
                store.SetActive(fight.Key, job, p.Id);
        }

        return active;
    }

    private void DrawColumnCombo(PlanStore store, StoredPlan stored, Job job)
    {
        var plan = stored.Plan;
        var mapping = store.MappingFor(plan, job);
        var current = mapping.AssigneeKey == null
            ? "(any column for my job)"
            : plan.Assignees.FirstOrDefault(a => string.Equals(a.Key, mapping.AssigneeKey, StringComparison.OrdinalIgnoreCase)) is { } a
                ? AssigneeLabel(a)
                : mapping.AssigneeKey;
        var preview = mapping.IsSuggestion ? $"(suggested) {current}" : current;

        ImGui.SetNextItemWidth(-1);
        using var combo = ImRaii.Combo("##column", preview);
        if (!combo)
            return;

        var suggestion = PlanStore.SuggestAssignee(plan, job, this.plugin.Config.DefaultSlot);
        if (ImGui.Selectable($"(suggested: {(suggestion != null ? AssigneeLabel(suggestion) : "any column for my job")})", mapping.IsSuggestion))
            store.SetMapping(plan.Id, null);
        if (ImGui.Selectable("(any column for my job)", !mapping.IsSuggestion && mapping.AssigneeKey == null))
            store.SetMapping(plan.Id, new PlayerMapping { AssigneeKey = null, Slot = null });
        foreach (var assignee in plan.Assignees)
        {
            var selected = !mapping.IsSuggestion && string.Equals(mapping.AssigneeKey, assignee.Key, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable($"{AssigneeLabel(assignee)}###{assignee.Key}", selected))
            {
                var slot = Slots.TryNormalize(assignee.Slot, out var s) ? s : null;
                store.SetMapping(plan.Id, new PlayerMapping { AssigneeKey = assignee.Key, Slot = slot });
            }
        }
    }

    private void DrawPlanList(PlanStore store)
    {
        if (store.All.Count == 0)
        {
            ImGui.TextWrapped("No plans yet. Paste a share code below (or use \"Import from sheet...\" once it is available).");
            return;
        }

        using var table = ImRaii.Table("##plans", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;

        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 2.5f);
        ImGui.TableSetupColumn("Fight", ImGuiTableColumnFlags.WidthStretch, 0.6f);
        ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableSetupColumn("Columns", ImGuiTableColumnFlags.WidthStretch, 0.7f);
        ImGui.TableSetupColumn("Entries", ImGuiTableColumnFlags.WidthStretch, 0.6f);
        ImGui.TableSetupColumn("##actions", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        ImGui.TableHeadersRow();

        StoredPlan? toDelete = null;
        foreach (var p in store.All)
        {
            using var id = ImRaii.PushId(p.Id);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(p.DisplayName);
            if (ImGui.IsItemHovered() && p.Plan.Source?.Url is { Length: > 0 } url)
                ImGui.SetTooltip(url);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(p.Plan.Fight);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(p.SourceLabel);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(p.Plan.Assignees.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (ImGui.IsItemHovered() && p.Plan.Assignees.Count > 0)
                ImGui.SetTooltip(string.Join("\n", p.Plan.Assignees.Select(AssigneeLabel)));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(p.Plan.Entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ImGui.TableNextColumn();

            if (ImGui.SmallButton("Copy share code"))
            {
                var code = store.ExportShareCode(p.Plan, out var error);
                if (code != null)
                {
                    ImGui.SetClipboardText(code);
                    this.SetShareMessage($"Share code for \"{p.DisplayName}\" copied to the clipboard ({code.Length} characters).", false);
                }
                else
                {
                    this.SetShareMessage("Could not create a share code: " + error, true);
                }
            }

            if (!p.IsReadOnly)
            {
                ImGui.SameLine();
                var confirming = this.confirmDeleteId == p.Id;
                using (ImRaii.PushColor(ImGuiCol.Text, ColorError, confirming))
                {
                    if (ImGui.SmallButton(confirming ? "Click again to delete" : "Delete"))
                    {
                        if (confirming)
                            toDelete = p;
                        else
                            this.confirmDeleteId = p.Id;
                    }
                }
            }
        }

        if (toDelete != null)
        {
            this.confirmDeleteId = null;
            if (store.Delete(toDelete.Id, out var error))
                this.SetShareMessage($"Deleted \"{toDelete.DisplayName}\".", false);
            else
                this.SetShareMessage("Delete failed: " + error, true);
        }
    }

    private void DrawImport(PlanStore store)
    {
        ImGui.TextDisabled("Import");
        if (ImGui.Button("Import from sheet..."))
            this.plugin.OpenImport();
        ImGui.SameLine();
        ImGui.TextDisabled("(Google Sheets import: coming soon)");

        ImGui.TextUnformatted("Share code:");
        ImGui.InputTextMultiline("##shareCode", ref this.shareCode, ShareCodeMaxBytes, new Vector2(-1, 70 * ImGuiHelpers.GlobalScale));
        if (ImGui.Button("Import share code"))
        {
            var ok = store.ImportShareCode(this.shareCode, out _, out var message);
            this.SetShareMessage(message, !ok);
            if (ok)
                this.shareCode = string.Empty;
        }

        ImGui.SameLine();
        if (ImGui.Button("Paste from clipboard"))
            this.shareCode = ImGui.GetClipboardText() ?? string.Empty;
        ImGui.SameLine();
        if (ImGui.Button("Reload plans"))
        {
            store.Reload();
            this.SetShareMessage($"Reloaded {store.All.Count} plans.", false);
        }

        if (this.shareMessage.Length > 0)
            ImGui.TextColored(this.shareMessageIsError ? ColorError : ColorOk, this.shareMessage);
    }

    private void SetShareMessage(string message, bool isError)
    {
        this.shareMessage = message;
        this.shareMessageIsError = isError;
    }

    // ---- Debug --------------------------------------------------------------------------

    private void DrawDebug()
    {
        var now = ImGui.GetTime();
        if (now - this.debugBuiltAt > 0.25)
        {
            this.debugBuiltAt = now;
            this.debugText = DebugReport.Build(this.plugin);
        }

        if (ImGui.Button("Copy to clipboard"))
            ImGui.SetClipboardText(DebugReport.Build(this.plugin));
        ImGui.SameLine();
        if (ImGui.Button("Reset live session"))
            this.plugin.ResetLiveSession();
        ImGui.SameLine();
        if (ImGui.Button("Reload data"))
            this.plugin.ReloadData();
        ImGui.SameLine();
        if (ImGui.Button("Clear problems"))
            this.plugin.Errors.ClearPrefix(string.Empty);

        using var child = ImRaii.Child("##debugText", new Vector2(-1, -1), true, ImGuiWindowFlags.HorizontalScrollbar);
        if (child)
            ImGui.TextUnformatted(this.debugText);
    }

    // ---- About --------------------------------------------------------------------------

    private void DrawAbout()
    {
        ImGui.TextWrapped(
            "MitCue imports your raid's mitigation plan and, during the fight, shows a small window telling you " +
            "which of YOUR actions to press next, synced to the boss using the fight's timeline.");
        ImGui.Spacing();
        ImGui.TextWrapped(
            "MitCue is reminder-only: it never presses, queues or targets anything, and it never changes your hotbars. " +
            "It only reads what the game shows you (cast bars, resolved actions, your cooldowns) to time its reminders.");
        ImGui.Spacing();
        ImGui.TextWrapped("Commands:");
        ImGui.BulletText("/mitcue - open or close this window");
        ImGui.BulletText("/mitcue overlay - show or hide the overlay");
        ImGui.BulletText("/mitcue preview [fight] [phase] - simulate a pull (again to stop), e.g. /mitcue preview top p3");
        ImGui.BulletText("/mitcue reset - reset the live session");
        ImGui.BulletText("/mitcue debug - open the Debug tab");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextWrapped("Fight timelines from cactbot (https://github.com/OverlayPlugin/cactbot), Apache License 2.0.");
        ImGui.TextDisabled($"Version {this.plugin.Version}");
    }
}
