using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MitCue.Core;
using MitCue.Core.Actions;
using MitCue.Core.Fights;
using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Import;
using MitCue.Runtime;

namespace MitCue.UI;

/// <summary>
/// Step 3 of the import window: the review table and the details of the selected entry, where what to press can be
/// changed (a note the importer could not resolve replaced by the action it stands for, which can be remembered as a
/// personal alias for later imports).
/// </summary>
internal sealed partial class ImportWindow
{
    private const string MineFilter = "\u0001mine";

    private readonly Dictionary<int, RowLabels> labels = [];
    private readonly List<ReviewRow> visible = [];
    private ReviewModel? review;
    private int visibleRevision = -1;
    private string visibleFilterKey = string.Empty;
    private bool visibleDirty = true;
    private string filterText = string.Empty;
    private bool problemsOnly;
    private string columnFilter = string.Empty;
    private bool sortByTime;
    private int selectedUid = -1;
    private string mechanicFilter = string.Empty;
    private string actionFilter = string.Empty;

    // A note replaced by an action in the details panel, offered as a personal alias (row uid, alias, saved yet).
    private (int Uid, UserAliasEntry Alias, bool Saved)? aliasOffer;

    private static string FormatClock(double seconds)
    {
        var sign = seconds < 0 ? "-" : string.Empty;
        var tenths = (long)Math.Round(Math.Abs(seconds) * 10, MidpointRounding.AwayFromZero);
        var minutes = tenths / 600;
        var rest = (tenths % 600) / 10.0;
        return sign + minutes.ToString(CultureInfo.InvariantCulture) + ":" + rest.ToString("00.0", CultureInfo.InvariantCulture);
    }

    private static string FormatOffset(double offset) => offset.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " s";

    private static string WhoText(WhoSpec? who)
    {
        if (who == null)
            return "everyone";
        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(who.Slot))
            parts.Add(who.Slot.Trim());
        if (who.Job is { } job && job != Job.None)
            parts.Add(job.ToString());
        var text = string.Join(" ", parts);
        if (!string.IsNullOrWhiteSpace(who.Assignee) && !string.Equals(who.Assignee.Trim(), text, StringComparison.OrdinalIgnoreCase))
            text = text.Length > 0 ? $"{text} ({who.Assignee.Trim()})" : who.Assignee.Trim();
        return text.Length > 0 ? text : "everyone";
    }

    private static string ShortSource(string? src)
    {
        if (string.IsNullOrWhiteSpace(src))
            return string.Empty;
        if (CellRef.TryParse(src) is not { } cell)
            return src;
        var tab = cell.Tab.Length > 14 ? cell.Tab[..13] + "." : cell.Tab;
        return tab + "!" + cell.Address;
    }

    private void StartReview(ReviewModel model, string name)
    {
        this.review = model;
        this.labels.Clear();
        this.visible.Clear();
        this.visibleRevision = -1;
        this.visibleDirty = true;
        this.filterText = string.Empty;
        this.problemsOnly = false;
        this.columnFilter = string.Empty;
        this.sortByTime = false;
        this.selectedUid = model.Rows.Count > 0 ? model.Rows[0].Uid : -1;
        this.saveMessage = string.Empty;
        this.aliasOffer = null;
        model.Plan.Name = string.IsNullOrWhiteSpace(model.Plan.Name) ? name : model.Plan.Name;
        this.replaceId = DefaultReplacement(this.plugin.Plans, model.Plan);
        var job = this.plugin.Frame.Job;
        this.setActive = job != Job.None && this.ColumnChoiceFor(model).Mine > 0;
        this.step = Step.Review;
    }

    private void DrawReview()
    {
        var model = this.review!;
        var fight = model.Fight;

        // Summary.
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted($"{Plural(model.Rows.Count, "entry", "entries")} for {fight.Name}");
        var errors = model.CountWorst(DiagnosticSeverity.Error);
        var warnings = model.CountWorst(DiagnosticSeverity.Warning);
        if (errors > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorError, Plural(errors, "with errors", "with errors"));
        }

        if (warnings > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorWarning, Plural(warnings, "with warnings", "with warnings"));
        }

        if (model.UndoCount > 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"Undo delete ({model.UndoCount.ToString(CultureInfo.InvariantCulture)})"))
            {
                model.UndoDelete();
                this.visibleDirty = true;
            }
        }

        this.DrawGlobalIssues(model);
        this.DrawCalibration(model);
        this.DrawReviewFilters(model);
        this.RefreshVisible(model);
        if (model.Rows.Count > 0 && this.visible.Count < model.Rows.Count)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"Showing {this.visible.Count.ToString(CultureInfo.InvariantCulture)} of {model.Rows.Count.ToString(CultureInfo.InvariantCulture)}");
        }

        var detailsHeight = (ImGui.GetTextLineHeightWithSpacing() * 4) + (ImGui.GetFrameHeightWithSpacing() * 5) + (ImGui.GetStyle().WindowPadding.Y * 2);
        var navHeight = ImGui.GetFrameHeightWithSpacing() + Scale(6);
        var tableHeight = Math.Max(Scale(120), ImGui.GetContentRegionAvail().Y - detailsHeight - navHeight);
        this.DrawReviewTable(model, tableHeight);

        var selected = model.Rows.FirstOrDefault(r => r.Uid == this.selectedUid);
        this.DrawDetails(model, selected, detailsHeight);

        if (ImGui.Button("< Back"))
            this.step = model.Origin == ReviewOrigin.Importer && this.source != null ? Step.Options : Step.Source;
        ImGui.SameLine();
        if (ImGui.Button("Next: save >"))
            this.step = Step.Save;
        ImGui.SameLine();
        ImGui.TextDisabled("Drag an offset to change the timing (Ctrl+click to type, right-click for automatic).");
    }

    private void DrawGlobalIssues(ReviewModel model)
    {
        if (model.GlobalIssues.Count == 0)
            return;
        var flags = model.GlobalIssues.Any(i => i.Severity == DiagnosticSeverity.Error) ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
        using var header = ImRaii.Header($"Notes about the whole import ({model.GlobalIssues.Count.ToString(CultureInfo.InvariantCulture)})###globalIssues", flags);
        if (!header)
            return;
        using var child = ImRaii.Child("##globalIssueList", new Vector2(-1, Math.Min(model.GlobalIssues.Count, 5) * ImGui.GetTextLineHeightWithSpacing() + Scale(8)), false);
        if (!child)
            return;
        foreach (var issue in model.GlobalIssues)
        {
            var where = string.IsNullOrWhiteSpace(issue.Src) ? string.Empty : $" [{ShortSource(issue.Src)}]";
            ImGui.TextColoredWrapped(SeverityColor(issue.Severity), issue.Message + where);
        }
    }

    private void DrawCalibration(ReviewModel model)
    {
        if (model.PhaseZeros.Count == 0 && model.PhaseSpread.Count == 0)
            return;
        using var header = ImRaii.Header("Phase calibration (where each phase's 0:00 of the sheet falls on the fight timeline)###calibration", ImGuiTreeNodeFlags.None);
        if (!header)
            return;

        var fight = model.Fight;
        var preset = this.calibrationKey.Length > 0 && fight.Calibration.TryGetValue(this.calibrationKey, out var values) ? values : null;
        using var table = ImRaii.Table("##calibration", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;
        ImGui.TableSetupColumn("Phase");
        ImGui.TableSetupColumn("Sheet 0:00 at");
        ImGui.TableSetupColumn(preset != null ? $"Preset ({this.calibrationKey})" : "Preset");
        ImGui.TableSetupColumn("Spread (largest misfit)");
        ImGui.TableHeadersRow();
        var phases = model.PhaseZeros.Keys.Union(model.PhaseSpread.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => fight.GetPhase(p)?.Start ?? double.MaxValue)
            .ToList();
        foreach (var phase in phases)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(fight.GetPhase(phase)?.Name ?? phase);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(model.PhaseZeros.TryGetValue(phase, out var zero) ? FormatClock(zero) : "-");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(preset != null && preset.TryGetValue(phase, out var p) ? FormatClock(p) : "-");
            ImGui.TableNextColumn();
            if (model.PhaseSpread.TryGetValue(phase, out var spread))
                ImGui.TextColored(spread > 3 ? ColorWarning : ColorOk, spread.ToString("0.0", CultureInfo.InvariantCulture) + " s");
            else
                ImGui.TextDisabled("-");
        }
    }

    private void DrawReviewFilters(ReviewModel model)
    {
        ImGui.SetNextItemWidth(Scale(220));
        ImGui.InputTextWithHint("##reviewFilter", "Search mechanic, action, column...", ref this.filterText, 128);
        ImGui.SameLine();
        ImGui.Checkbox("Only problems", ref this.problemsOnly);
        ImGui.SameLine();
        ImGui.Checkbox("Sort by time", ref this.sortByTime);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Scale(200));
        var job = this.plugin.Frame.Job;
        var preview = this.columnFilter.Length == 0 ? "All columns" : this.columnFilter == MineFilter ? $"My calls ({job})" : this.columnFilter;
        using var combo = ImRaii.Combo("##columnFilter", preview);
        if (!combo)
            return;
        if (ImGui.Selectable("All columns", this.columnFilter.Length == 0))
            this.columnFilter = string.Empty;
        if (job != Job.None && ImGui.Selectable($"My calls ({job})", this.columnFilter == MineFilter))
            this.columnFilter = MineFilter;
        var keys = model.Plan.Assignees.Select(a => a.Key)
            .Concat(model.Rows.Select(r => ImportAnalysis.ColumnKey(r.Entry)))
            .Where(k => k.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (ImGui.Selectable(key + "##colf", string.Equals(this.columnFilter, key, StringComparison.OrdinalIgnoreCase)))
                this.columnFilter = key;
        }
    }

    private void RefreshVisible(ReviewModel model)
    {
        // "My calls" are the entries the overlay will show: with the column choice the plan is saved with.
        var job = this.plugin.Frame.Job;
        var mineMapping = this.columnFilter == MineFilter && job != Job.None ? this.ColumnChoiceFor(model).Mapping : null;
        var filterKey = string.Join(
            '\u0002',
            this.filterText,
            this.problemsOnly,
            this.columnFilter,
            this.sortByTime,
            mineMapping?.AssigneeKey ?? "*",
            mineMapping?.Slot ?? "*",
            job);

        // Do not reorder rows under the mouse while a value is being dragged; structural changes always refresh.
        var stale = model.Revision != this.visibleRevision && !ImGui.IsAnyItemActive();
        if (!this.visibleDirty && filterKey == this.visibleFilterKey && !stale)
            return;

        this.visibleDirty = false;
        this.visibleFilterKey = filterKey;
        this.visibleRevision = model.Revision;
        this.visible.Clear();

        var text = this.filterText.Trim().ToLowerInvariant();
        var me = mineMapping != null ? ImportColumns.Context(mineMapping, job, model.Fight.SyncLevel, this.plugin.Config.DefaultSlot) : null;
        foreach (var row in model.Rows)
        {
            if (this.problemsOnly && row.Worst is not (DiagnosticSeverity.Warning or DiagnosticSeverity.Error))
                continue;
            if (this.columnFilter == MineFilter)
            {
                if (me == null || !PlanSelection.Applies(row.Entry, me))
                    continue;
            }
            else if (this.columnFilter.Length > 0 && !string.Equals(ImportAnalysis.ColumnKey(row.Entry), this.columnFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (text.Length > 0 && !this.Labels(model, row).Search.Contains(text, StringComparison.Ordinal))
                continue;
            this.visible.Add(row);
        }

        if (this.sortByTime)
        {
            var order = model.Rows.Select((r, i) => (r, i)).ToDictionary(x => x.r.Uid, x => x.i);
            this.visible.Sort((a, b) =>
            {
                var ta = this.Labels(model, a).Press ?? double.MaxValue;
                var tb = this.Labels(model, b).Press ?? double.MaxValue;
                var c = ta.CompareTo(tb);
                return c != 0 ? c : order[a.Uid].CompareTo(order[b.Uid]);
            });
        }
    }

    private void DrawReviewTable(ReviewModel model, float height)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.Resizable;
        using var table = ImRaii.Table("##review", 9, flags, new Vector2(-1, height));
        if (!table)
            return;

        var frame = ImGui.GetFrameHeight();
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Phase", ImGuiTableColumnFlags.WidthFixed, Scale(44));
        ImGui.TableSetupColumn("Mechanic", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        ImGui.TableSetupColumn("Offset", ImGuiTableColumnFlags.WidthFixed, Scale(92));
        ImGui.TableSetupColumn("Press", ImGuiTableColumnFlags.WidthStretch, 2.4f);
        ImGui.TableSetupColumn("Who", ImGuiTableColumnFlags.WidthStretch, 0.9f);
        ImGui.TableSetupColumn("Opt / Rdy / End", ImGuiTableColumnFlags.WidthFixed, (frame * 3) + Scale(22));
        ImGui.TableSetupColumn("Problems", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthFixed, Scale(84));
        ImGui.TableSetupColumn("##delete", ImGuiTableColumnFlags.WidthFixed, frame + Scale(4));
        ImGui.TableHeadersRow();

        ReviewRow? deleted = null;
        var clipper = ImGui.ImGuiListClipper();
        try
        {
            clipper.Begin(this.visible.Count);
            while (clipper.Step())
            {
                for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    if (i < 0 || i >= this.visible.Count)
                        continue;
                    if (this.DrawReviewRow(model, this.visible[i], frame))
                        deleted = this.visible[i];
                }
            }

            clipper.End();
        }
        finally
        {
            clipper.Destroy();
        }

        if (deleted != null)
        {
            var index = this.visible.IndexOf(deleted);
            model.Delete(deleted);
            this.labels.Remove(deleted.Uid);
            this.visibleDirty = true;
            if (this.selectedUid == deleted.Uid)
            {
                var next = index + 1 < this.visible.Count ? this.visible[index + 1] : index > 0 ? this.visible[index - 1] : null;
                this.selectedUid = next?.Uid ?? -1;
            }
        }
    }

    /// <summary>Draws one row; returns true when its delete button was pressed.</summary>
    private bool DrawReviewRow(ReviewModel model, ReviewRow row, float frame)
    {
        var entry = row.Entry;
        var l = this.Labels(model, row);
        using var id = ImRaii.PushId(row.Uid);
        ImGui.TableNextRow(ImGuiTableRowFlags.None, frame);

        // Phase (the selectable spans the row and selects it for the details panel).
        ImGui.TableNextColumn();
        using (ImRaii.PushStyle(ImGuiStyleVar.SelectableTextAlign, new Vector2(0, 0.5f)))
        {
            if (ImGui.Selectable(l.Phase + "##select", this.selectedUid == row.Uid, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap, new Vector2(0, frame)))
                this.selectedUid = row.Uid;
        }

        if (ImGui.IsItemHovered() && l.PhaseName.Length > 0)
            ImGui.SetTooltip(l.PhaseName);

        // Mechanic.
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
        using (ImRaii.PushColor(ImGuiCol.Text, ColorError, !l.MechanicKnown))
        {
            using var combo = ImRaii.Combo("##mechanic", l.Mechanic, ImGuiComboFlags.HeightLarge);
            if (combo)
            {
                using (ImRaii.DefaultColors())
                    this.DrawMechanicChoices(model, row);
            }
        }

        // Offset.
        ImGui.TableNextColumn();
        var automatic = entry.Offset == null;
        var value = (float)(entry.Offset ?? l.AutoOffset);
        ImGui.SetNextItemWidth(-1);
        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled], automatic))
        {
            if (ImGui.DragFloat("##offset", ref value, 0.1f, -120f, 120f, automatic ? "auto %+.1f" : "%+.1f s"))
            {
                model.SetOffset(row, value);
                this.selectedUid = row.Uid;
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                (automatic ? "Automatic timing for this action. " : string.Empty) +
                "Seconds from the hit (negative = before). Drag, or Ctrl+click to type.\nRight-click: automatic timing or 0 s." +
                (l.Press is { } press ? $"\nPress at {FormatClock(press)} on the fight timeline." : string.Empty));
        }

        using (var popup = ImRaii.ContextPopupItem("##offsetMenu"))
        {
            if (popup)
            {
                if (ImGui.Selectable("Automatic timing (from the action)", automatic))
                    model.SetOffset(row, null);
                if (ImGui.Selectable("At the hit (0 s)"))
                    model.SetOffset(row, 0);
            }
        }

        // Press: icons and names.
        ImGui.TableNextColumn();
        var iconSize = new Vector2(frame, frame);
        foreach (var icon in l.Icons)
        {
            var wrap = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrEmpty();
            ImGui.Image(wrap.Handle, iconSize);
            ImGui.SameLine(0, Scale(2));
        }

        ImGui.AlignTextToFramePadding();
        if (l.OnlyNotes)
            ImGui.TextDisabled(l.Items);
        else
            ImGui.TextUnformatted(l.Items);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(l.ItemsLong);

        // Who.
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(l.Who);

        // Flags.
        ImGui.TableNextColumn();
        var optional = entry.Optional;
        if (ImGui.Checkbox("##optional", ref optional))
            model.SetOptional(row, optional);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Optional: shown, but never counted as missed.");
        ImGui.SameLine(0, Scale(4));
        var whenReady = entry.WhenReady;
        if (ImGui.Checkbox("##whenReady", ref whenReady))
            model.SetWhenReady(row, whenReady);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When ready: press at the planned time, or as soon as it comes off cooldown.");
        ImGui.SameLine(0, Scale(4));
        var atPhaseEnd = entry.AtPhaseEnd;
        if (ImGui.Checkbox("##atPhaseEnd", ref atPhaseEnd))
            model.SetAtPhaseEnd(row, atPhaseEnd);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Phase end: a call for the end of its phase (\"as P3 dies\"). When the phase is pushed early, it is shown at the transition instead of being skipped.");

        // Problems.
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        if (l.Worst is { } worst)
        {
            ImGui.TextColored(SeverityColor(worst), l.Problems);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(l.ProblemsLong);
        }
        else if (row.Edited)
        {
            ImGui.TextDisabled("edited");
        }

        // Source.
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(l.Src);
        if (ImGui.IsItemHovered() && entry.Src is { Length: > 0 } src)
        {
            var text = SheetText.CellText(this.source?.Document, src);
            ImGui.SetTooltip(text != null ? $"{src}:\n{text}" : src);
        }

        // Delete.
        ImGui.TableNextColumn();
        var delete = ImGuiComponents.IconButton(FontAwesomeIcon.Trash);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Delete this entry (Undo delete brings it back).");
        return delete;
    }

    private void DrawMechanicChoices(ReviewModel model, ReviewRow row)
    {
        if (ImGui.IsWindowAppearing())
        {
            this.mechanicFilter = string.Empty;
            ImGui.SetKeyboardFocusHere();
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##mechanicFilter", "Search mechanics...", ref this.mechanicFilter, 64);
        var filter = this.mechanicFilter.Trim();
        var fight = model.Fight;
        string? lastPhase = null;
        foreach (var m in fight.Mechanics)
        {
            if (filter.Length > 0 && !MechanicMatches(m, filter))
                continue;
            if (!string.Equals(m.Phase, lastPhase, StringComparison.OrdinalIgnoreCase))
            {
                lastPhase = m.Phase;
                ImGui.TextDisabled(fight.GetPhase(m.Phase)?.Name ?? m.Phase);
            }

            var selected = string.Equals(m.Id, row.Entry.Mechanic, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable($"{FormatClock(m.Hit)}  {m.Name}##{m.Id}", selected))
            {
                model.SetMechanic(row, m.Id);
                this.selectedUid = row.Uid;
            }

            if (selected && ImGui.IsWindowAppearing())
                ImGui.SetScrollHereY();
            if (ImGui.IsItemHovered())
            {
                var aliases = m.Aliases.Count > 0 ? "\nAlso called: " + string.Join("; ", m.Aliases.Take(8)) : string.Empty;
                ImGui.SetTooltip($"{m.Id} ({LlmPrompt.KindName(m.Kind)}){aliases}");
            }
        }
    }

    private static bool MechanicMatches(Mechanic m, string filter) =>
        m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        m.Id.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        m.Aliases.Any(a => a.Contains(filter, StringComparison.OrdinalIgnoreCase));

    private void DrawDetails(ReviewModel model, ReviewRow? row, float height)
    {
        using var child = ImRaii.Child("##details", new Vector2(-1, height), true);
        if (!child)
            return;
        if (row == null)
        {
            ImGui.TextDisabled("Select a row to see its details.");
            return;
        }

        var entry = row.Entry;
        var l = this.Labels(model, row);
        var mechanic = model.MechanicOf(row);
        if (mechanic != null)
        {
            var hits = mechanic.Hits.Count > 0 ? mechanic.Hits : [mechanic.Hit];
            ImGui.TextUnformatted($"{mechanic.Name} [{mechanic.Id}], {l.PhaseName}, {LlmPrompt.KindName(mechanic.Kind)}; hit{(hits.Count > 1 ? "s" : string.Empty)} at {string.Join(", ", hits.Select(FormatClock))}");

            var hitIndex = Math.Clamp(entry.HitIndex ?? 0, 0, Math.Max(0, hits.Count - 1));
            var offset = entry.Offset ?? l.AutoOffset;
            var hit = mechanic.HitAt(hitIndex);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted($"Press at {FormatClock(hit + offset)}: {FormatOffset(offset)}{(entry.Offset == null ? " (automatic)" : string.Empty)} from hit #{(hitIndex + 1).ToString(CultureInfo.InvariantCulture)} at {FormatClock(hit)}");
            if (hits.Count > 1)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(Scale(130));
                using var combo = ImRaii.Combo("Relative to##hit", $"hit #{(hitIndex + 1).ToString(CultureInfo.InvariantCulture)} ({FormatClock(hit)})");
                if (combo)
                {
                    for (var i = 0; i < hits.Count; i++)
                    {
                        if (ImGui.Selectable($"hit #{(i + 1).ToString(CultureInfo.InvariantCulture)} ({FormatClock(hits[i])})##hit{i}", i == hitIndex))
                            model.SetHitIndex(row, i == 0 ? null : i);
                    }
                }
            }
        }
        else
        {
            ImGui.TextColored(ColorError, string.IsNullOrWhiteSpace(entry.Mechanic) ? "No mechanic: choose one in the table." : $"Unknown mechanic \"{entry.Mechanic}\": choose one in the table.");
        }

        ImGui.TextUnformatted("Who: " + l.Who + (entry.AtPhaseEnd ? "; at the end of its phase" : string.Empty));
        this.DrawItemEditor(model, row);

        var target = entry.Target ?? string.Empty;
        ImGui.SetNextItemWidth(Scale(180));
        if (ImGui.InputText("Target##detail", ref target, 128))
            model.SetTarget(row, target);
        ImGui.SameLine();
        var note = entry.Note ?? string.Empty;
        ImGui.SetNextItemWidth(Scale(260));
        if (ImGui.InputText("Note##detail", ref note, 256))
            model.SetNote(row, note);

        if (!string.IsNullOrWhiteSpace(entry.Detail))
            ImGui.TextWrapped("Notes: " + entry.Detail);

        if (entry.Src is { Length: > 0 } src)
        {
            var text = SheetText.CellText(this.source?.Document, src);
            ImGui.TextDisabled(text != null ? $"Source {src}: {text.ReplaceLineEndings(" / ")}" : $"Source {src}");
        }

        foreach (var issue in row.Issues)
            ImGui.TextColoredWrapped(SeverityColor(issue.Severity), (issue.FromImporter ? "Import: " : "Check: ") + issue.Message);
    }

    /// <summary>
    /// What to press, item by item: an action can be changed, a note replaced by the action it stands for (and then
    /// remembered as a personal alias), items removed and actions added, from the actions of the entry's job.
    /// </summary>
    private void DrawItemEditor(ReviewModel model, ReviewRow row)
    {
        var entry = row.Entry;
        var job = model.JobOf(row, this.plugin.Frame.Job);
        var actions = model.ActionsFor(job);
        var gameData = this.plugin.GameData;
        ImGui.TextUnformatted(entry.Do.Count == 0 ? "Press: nothing yet" : entry.Seq && entry.Do.Count > 1 ? "Press, in this order:" : "Press:");
        for (var i = 0; i < entry.Do.Count; i++)
        {
            var item = entry.Do[i];
            using var id = ImRaii.PushId(i);
            ImGui.Indent(Scale(12));
            ImGui.AlignTextToFramePadding();
            if (item.IsNote)
                ImGui.TextColored(ColorWarning, $"\"{item.Note}\" (note: not tracked)");
            else
                ImGui.TextUnformatted(ReminderText.ItemName(item, gameData) + (item.Action is { } a ? $" [{a.ToString(CultureInfo.InvariantCulture)}]" : string.Empty));
            ImGui.SameLine();
            if (this.DrawActionPicker(item.IsNote ? "Replace with an action" : "Change", actions, out var chosen))
            {
                model.SetItem(row, i, PlanItem.ForAction(chosen.Id, chosen.Name));
                this.aliasOffer = item.IsNote && PersonalAliases.From(item.Note, chosen.Name, job) is { } alias ? (row.Uid, alias, false) : null;
            }

            ImGui.SameLine();
            var remove = ImGui.SmallButton("Remove");
            ImGui.Unindent(Scale(12));
            if (remove)
            {
                model.RemoveItem(row, i);
                break;
            }
        }

        ImGui.Indent(Scale(12));
        if (this.DrawActionPicker("+ Add an action", actions, out var added))
            model.AddItem(row, PlanItem.ForAction(added.Id, added.Name));
        if (actions.Count == 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(job == Job.None
                ? "(unknown job: switch to the job whose calls these are to pick its actions)"
                : "(the action list did not load: see Problems in /mitcue)");
        }

        ImGui.Unindent(Scale(12));
        this.DrawAliasOffer(row);
    }

    /// <summary>A combo listing <paramref name="actions"/> with a search box; true when one was picked.</summary>
    private bool DrawActionPicker(string label, IReadOnlyList<ActionInfo> actions, out ActionInfo chosen)
    {
        chosen = null!;
        if (actions.Count == 0)
            return false;
        ImGui.SetNextItemWidth(Scale(190));
        using var combo = ImRaii.Combo("##pick" + label, label, ImGuiComboFlags.HeightLarge);
        if (!combo)
            return false;

        if (ImGui.IsWindowAppearing())
        {
            this.actionFilter = string.Empty;
            ImGui.SetKeyboardFocusHere();
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##actionFilter", "Search actions...", ref this.actionFilter, 64);
        var filter = this.actionFilter.Trim();
        var picked = false;
        foreach (var action in actions)
        {
            if (filter.Length > 0 && !action.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (ImGui.Selectable($"{action.Name}  ({LlmPrompt.KindName(action.Kind)})##{action.Id.ToString(CultureInfo.InvariantCulture)}"))
            {
                chosen = action;
                picked = true;
            }
        }

        return picked;
    }

    /// <summary>After a note was replaced: offer to remember the shorthand, then say it was saved (with an undo).</summary>
    private void DrawAliasOffer(ReviewRow row)
    {
        if (this.aliasOffer is not { } offer || offer.Uid != row.Uid)
            return;

        var alias = offer.Alias;
        var meaning = $"\"{alias.Token}\" = {string.Join(" + ", alias.Actions)}{(alias.Job != null ? $" for {alias.Job}" : string.Empty)}";
        var aliases = this.plugin.Config.UserAliases;
        if (!offer.Saved)
        {
            if (ImGui.SmallButton($"Remember {meaning}"))
            {
                PersonalAliases.Upsert(aliases, alias);
                this.ApplyPersonalAliases();
                this.aliasOffer = (offer.Uid, alias, true);
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Saves this shorthand as a personal alias: later imports read it as this action.");
            return;
        }

        ImGui.TextColored(ColorOk, $"Remembered {meaning}: later imports read it.");
        ImGui.SameLine();
        if (ImGui.SmallButton("Forget"))
        {
            PersonalAliases.Remove(aliases, alias);
            this.plugin.Data.Aliases?.RemoveUser(alias.Token);
            this.ApplyPersonalAliases();
            this.aliasOffer = (offer.Uid, alias, false);
        }
    }

    private void ApplyPersonalAliases()
    {
        this.plugin.SaveConfig();
        this.plugin.Data.ApplyUserAliases();
    }

    private RowLabels Labels(ReviewModel model, ReviewRow row)
    {
        if (this.labels.TryGetValue(row.Uid, out var cached) && cached.Version == row.Version)
            return cached;

        var entry = row.Entry;
        var gameData = this.plugin.GameData;
        var mechanic = model.MechanicOf(row);
        var names = entry.Do.Select(i => ReminderText.ItemName(i, gameData)).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        var joiner = entry.Seq ? " -> " : " + ";
        var issues = row.Issues.OrderByDescending(i => i.Severity).ToList();
        var who = WhoText(entry.Who);
        var phaseName = mechanic != null ? model.Fight.GetPhase(mechanic.Phase)?.Name ?? mechanic.Phase : string.Empty;

        var mechanicText = mechanic != null
            ? mechanic.Name + (entry.HitIndex is > 0 ? $" (hit #{(entry.HitIndex.Value + 1).ToString(CultureInfo.InvariantCulture)})" : string.Empty)
            : string.IsNullOrWhiteSpace(entry.Mechanic) ? "(choose)" : "? " + entry.Mechanic;

        var itemsLong = string.Join(
            joiner,
            entry.Do.Select(i => i.IsNote ? $"\"{i.Note}\" (note)" : ReminderText.ItemName(i, gameData) + (i.Action is { } a ? $" [{a.ToString(CultureInfo.InvariantCulture)}]" : string.Empty)));
        var extras = ReminderText.TargetAndNote(entry);
        if (extras != null)
            itemsLong += "\n" + extras;

        var autoOffset = model.AutoOffset(row);
        var hit = mechanic?.HitAt(entry.HitIndex ?? 0);
        var labels = new RowLabels
        {
            Version = row.Version,
            Phase = mechanic?.Phase ?? "?",
            PhaseName = phaseName,
            Mechanic = mechanicText,
            MechanicKnown = mechanic != null,
            Items = names.Count > 0 ? string.Join(joiner, names) : "(nothing)",
            ItemsLong = itemsLong.Length > 0 ? itemsLong : "(nothing to press)",
            OnlyNotes = entry.Do.Count > 0 && entry.Do.All(i => i.IsNote),
            Icons = entry.Do.Select(i => ReminderText.ItemIcon(i, gameData)).Where(i => i != 0).Distinct().Take(3).ToList(),
            Who = who,
            Src = ShortSource(entry.Src),
            Worst = row.Worst,
            Problems = issues.Count == 0 ? string.Empty : issues[0].Message + (issues.Count > 1 ? $" (+{(issues.Count - 1).ToString(CultureInfo.InvariantCulture)})" : string.Empty),
            ProblemsLong = string.Join("\n", issues.Select(i => $"{i.Severity}: {i.Message}{(i.FromImporter ? " (import)" : string.Empty)}")),
            AutoOffset = autoOffset,
            Press = hit is { } h ? h + (entry.Offset ?? autoOffset) : null,
            Search = string.Join(
                '\n',
                mechanicText,
                entry.Mechanic,
                phaseName,
                string.Join(' ', names),
                who,
                entry.Src ?? string.Empty,
                entry.Target ?? string.Empty,
                entry.Note ?? string.Empty,
                string.Join(' ', issues.Select(i => i.Message))).ToLowerInvariant(),
        };
        this.labels[row.Uid] = labels;
        return labels;
    }

    /// <summary>Display strings of one review row, rebuilt when the row changes.</summary>
    private sealed class RowLabels
    {
        public int Version { get; init; }

        public string Phase { get; init; } = string.Empty;

        public string PhaseName { get; init; } = string.Empty;

        public string Mechanic { get; init; } = string.Empty;

        public bool MechanicKnown { get; init; }

        public string Items { get; init; } = string.Empty;

        public string ItemsLong { get; init; } = string.Empty;

        public bool OnlyNotes { get; init; }

        public List<uint> Icons { get; init; } = [];

        public string Who { get; init; } = string.Empty;

        public string Src { get; init; } = string.Empty;

        public DiagnosticSeverity? Worst { get; init; }

        public string Problems { get; init; } = string.Empty;

        public string ProblemsLong { get; init; } = string.Empty;

        public double AutoOffset { get; init; }

        public double? Press { get; init; }

        public string Search { get; init; } = string.Empty;
    }
}
