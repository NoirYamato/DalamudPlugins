using System.Globalization;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using MitCue.Core;
using MitCue.Core.Fights;
using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Import;
using MitCue.Plans;
using MitCue.Util;

namespace MitCue.UI;

/// <summary>
/// The import window (DESIGN §6, milestone 4), step by step:
/// 1. Source: a Google Sheets/Docs link (downloaded on "Fetch"), cells pasted from Google Sheets, or plan YAML pasted back
///    from an AI chat (with a "Copy AI prompt" button for that route);
/// 2. Options: fight, tabs, columns, plan name, calibration preset (defaults from a trial import);
/// 3. Review: every entry with its mechanic, timing, actions, who, flags, problems and source cell; mechanic, offset,
///    flags and what to press can be edited (a fixed note can be remembered as a personal alias) and entries deleted;
/// 4. Save: stored with <see cref="PlanStore.Save"/>, optionally made the active plan for the current job, with the
///    column choice that shows every call of the player's columns (<see cref="ImportColumns"/>).
/// Downloads, workbook parsing and imports run on background tasks (<see cref="BackgroundJobs"/>); results come back on
/// the framework thread, and the options cannot change while a job runs. Nothing here throws out of <see cref="Draw"/>.
/// </summary>
internal sealed partial class ImportWindow : Window, IDisposable
{
    /// <summary>Byte capacity of the paste boxes (the ImGui default of 512 bytes is far too small for a sheet).</summary>
    private const int PasteBoxBytes = 1024 * 1024;

    /// <summary>Label of the trial import job (the Options step's tab check).</summary>
    private const string AnalysisLabel = "Looking at the tabs...";

    /// <summary>ID of the "replace the review?" confirmation.</summary>
    private const string ReplacePopupId = "Replace the review?###mitcueReplaceReview";

    private static readonly Vector4 ColorError = new(1f, 0.45f, 0.40f, 1f);
    private static readonly Vector4 ColorWarning = new(1f, 0.75f, 0.30f, 1f);
    private static readonly Vector4 ColorOk = new(0.50f, 0.88f, 0.50f, 1f);
    private static readonly Vector4 ColorInfo = new(0.62f, 0.78f, 1f, 1f);
    private static readonly Vector4 ColorAccent = new(1f, 0.85f, 0.40f, 1f);

    private readonly Plugin plugin;
    private readonly BackgroundJobs jobs;
    private readonly SheetFetcher fetcher = new();
    private readonly TextBox pasteBox = new(PasteBoxBytes);
    private readonly TextBox yamlBox = new(PasteBoxBytes);

    private Step step = Step.Source;
    private SourceMode mode = SourceMode.Link;
    private string? drawError;
    private string status = string.Empty;
    private bool statusIsError;
    private bool statusIsWarning;

    // "Replace the review?" confirmation.
    private Action? pendingReplace;
    private string pendingReplaceWhat = string.Empty;
    private bool openReplacePopup;

    // Source step.
    private string linkText = string.Empty;
    private char separator = '\t';
    private string pasteTabName = string.Empty;
    private LoadedSource? source;
    private int sourceVersion;

    // AI route.
    private string promptFightKey = string.Empty;
    private readonly SortedSet<Job> promptJobs = [];
    private bool promptIncludeSource = true;
    private string promptMessage = string.Empty;

    // Options step.
    private string fightKey = string.Empty;
    private ImportAnalysis? analysis;
    private readonly TrialImportTracker trial = new();
    private readonly HashSet<string> selectedTabs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> selectedColumns = new(StringComparer.OrdinalIgnoreCase);
    private bool tabsTouched;
    private bool columnsTouched;
    private string planName = string.Empty;
    private bool planNameTouched;
    private string calibrationKey = string.Empty;
    private string? defaultTabsBasis;

    // Save step.
    private bool setActive = true;
    private string? replaceId;
    private string saveMessage = string.Empty;
    private ColumnChoice? columnChoice;
    private (ReviewModel? Model, int Revision, Job Job, string? ReplaceId, int StoreRevision, string? Slot) columnChoiceKey;

    /// <summary>Creates the (closed) window.</summary>
    public ImportWindow(Plugin plugin)
        : base("MitCue: import a plan###MitCueImport")
    {
        this.plugin = plugin;
        this.jobs = new BackgroundJobs(this.PostToFramework, ex => plugin.Errors.Report("import.callback", "Finishing an import step", ex));
        this.Size = new Vector2(980, 700);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(640, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    private enum Step
    {
        Source,
        Options,
        Review,
        Save,
    }

    private enum SourceMode
    {
        Link,
        Paste,
        Yaml,
    }

    /// <summary>Opens the window (and brings it to the front).</summary>
    public void Open()
    {
        this.IsOpen = true;
        this.BringToFront();
    }

    /// <inheritdoc/>
    public override void OnOpen()
    {
        try
        {
            this.plugin.Data.EnsureLoaded();
            this.plugin.Plans.EnsureLoaded();
            if (this.promptJobs.Count == 0 && this.plugin.Frame.Job != Job.None)
                this.promptJobs.Add(this.plugin.Frame.Job);
        }
        catch (Exception ex)
        {
            this.plugin.Errors.Report("ui.import.open", "Opening the import window", ex);
        }
    }

    /// <inheritdoc/>
    public override void OnClose()
    {
        // No background work while the window is closed; what was loaded stays for the next time (a cancelled tab check
        // runs again when the window opens).
        if (this.jobs.Busy && !this.trial.Running)
            this.SetStatus($"\"{this.jobs.Current?.TrimEnd('.')}\" was cancelled when the window closed.", true);
        this.CancelJob(byPlayer: false);
    }

    /// <inheritdoc/>
    public override void Draw()
    {
        try
        {
            // Returns at once when loaded; reloads after Debug > Reload data while the window is open.
            this.plugin.Data.EnsureLoaded();
            this.plugin.Plans.EnsureLoaded();
            this.DrawContent();
            this.drawError = null;
        }
        catch (Exception ex)
        {
            this.plugin.Errors.Report("ui.import", "Drawing the import window", ex);
            this.drawError = ErrorReporter.Describe(ex);
        }

        if (this.drawError != null)
            ImGui.TextColoredWrapped(ColorError, "Something went wrong in this window: " + this.drawError);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.jobs.Dispose();
        this.fetcher.Dispose();
    }

    private static float Scale(float value) => value * ImGuiHelpers.GlobalScale;

    private static string Plural(int count, string singular, string? plural = null) =>
        count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? singular : plural ?? singular + "s");

    private static Vector4 SeverityColor(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => ColorError,
        DiagnosticSeverity.Warning => ColorWarning,
        _ => ColorInfo,
    };

    private static void HelpMarker(string text)
    {
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(text);
    }

    private void PostToFramework(Action action)
    {
        var queued = Plugin.Framework.RunOnFrameworkThread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                this.plugin.Errors.Report("import.callback", "Finishing an import step", ex);
            }
        });

        // The action never throws; a queue that fails (the plugin unloading) must not leave an unobserved task exception.
        queued.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void SetStatus(string message, bool isError)
    {
        this.status = message;
        this.statusIsError = isError;
        this.statusIsWarning = false;
    }

    private void SetWarning(string message)
    {
        this.status = message;
        this.statusIsError = false;
        this.statusIsWarning = true;
    }

    /// <summary>Cancels the running job; a cancelled tab check runs again (see <see cref="TrialImportTracker"/>).</summary>
    private void CancelJob(bool byPlayer)
    {
        this.jobs.Cancel();
        this.trial.JobCancelled(byPlayer);
    }

    /// <summary>
    /// Runs <paramref name="action"/>, which replaces the review, after asking when the review has changes the player
    /// would lose (going back to Options and importing again starts a new review).
    /// </summary>
    private void ConfirmReplaceReview(string what, Action action)
    {
        if (this.review is not { EditCount: > 0 })
        {
            action();
            return;
        }

        this.pendingReplace = action;
        this.pendingReplaceWhat = what;
        this.openReplacePopup = true;
    }

    private void DrawReplacePopup()
    {
        // Opened here, at the window's top level, so OpenPopup and BeginPopupModal share one ID stack.
        if (this.openReplacePopup)
        {
            this.openReplacePopup = false;
            ImGui.OpenPopup(ReplacePopupId);
        }

        if (!ImGui.BeginPopupModal(ReplacePopupId, ImGuiWindowFlags.AlwaysAutoResize))
            return;
        try
        {
            var edits = this.review?.EditCount ?? 0;
            ImGui.TextUnformatted($"{this.pendingReplaceWhat} starts a new review.");
            ImGui.TextUnformatted($"Your changes to {Plural(edits, "entry", "entries")} in the current review would be lost.");
            ImGui.Spacing();
            if (ImGui.Button("Continue and lose the changes"))
            {
                var action = this.pendingReplace;
                this.pendingReplace = null;
                ImGui.CloseCurrentPopup();
                action?.Invoke();
            }

            ImGui.SameLine();
            if (ImGui.Button("Keep my review"))
            {
                this.pendingReplace = null;
                ImGui.CloseCurrentPopup();
                if (this.review != null)
                    this.step = Step.Review;
            }
        }
        finally
        {
            ImGui.EndPopup();
        }
    }

    private void DrawContent()
    {
        this.DrawStepBar();
        this.DrawStatusLine();
        ImGui.Separator();

        switch (this.step)
        {
            case Step.Source:
                this.DrawSource();
                break;
            case Step.Options when this.source != null:
                this.DrawOptions();
                break;
            case Step.Review when this.review != null:
                this.DrawReview();
                break;
            case Step.Save when this.review != null:
                this.DrawSave();
                break;
            default:
                this.step = Step.Source;
                break;
        }

        this.DrawReplacePopup();
    }

    // ---- Step bar and status ---------------------------------------------------------------

    private bool CanGoTo(Step target) => target switch
    {
        Step.Source => true,
        Step.Options => this.source != null,
        Step.Review or Step.Save => this.review != null,
        _ => false,
    };

    private void DrawStepBar()
    {
        string[] labels = ["1. Source", "2. Options", "3. Review", "4. Save"];
        for (var i = 0; i < labels.Length; i++)
        {
            var target = (Step)i;
            if (i > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled(">");
                ImGui.SameLine();
            }

            if (target == this.step)
            {
                ImGui.TextColored(ColorAccent, labels[i]);
            }
            else if (this.CanGoTo(target) && !this.jobs.Busy)
            {
                if (ImGui.SmallButton(labels[i] + "##stepbar"))
                    this.step = target;
            }
            else
            {
                ImGui.TextDisabled(labels[i]);
            }
        }

        ImGui.SameLine(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - Scale(24));
        ImGuiComponents.HelpMarker(
            "Import your raid's mitigation plan.\n\n" +
            "Link: paste the link of a Google Sheet or Doc that is shared as \"Anyone with the link\" and press Fetch. " +
            "MitCue downloads it only when you press Fetch.\n" +
            "Paste cells: select the plan's cells in Google Sheets, copy them, and paste.\n" +
            "AI route: if MitCue cannot read the layout, copy the AI prompt into any AI chat with your plan, and paste the YAML answer back.\n\n" +
            "You check every entry before saving, and you can fix the mechanic, the timing and the flags.");
    }

    private void DrawStatusLine()
    {
        if (this.jobs.Busy)
        {
            var elapsed = (DateTime.UtcNow - this.jobs.StartedUtc).TotalSeconds;
            ImGui.TextColored(ColorAccent, $"{this.jobs.Current} ({elapsed.ToString("0", CultureInfo.InvariantCulture)} s)");
            ImGui.SameLine();
            if (ImGui.SmallButton("Cancel##job"))
            {
                var wasTabCheck = this.trial.Running;
                this.CancelJob(byPlayer: true);
                if (wasTabCheck)
                    this.SetWarning("The tab check was cancelled, so your tabs and columns were not chosen for you. Press \"Check the tabs\" below to run it.");
                else
                    this.SetStatus("Cancelled.", false);
            }
        }
        else if (this.status.Length > 0)
        {
            ImGui.TextColoredWrapped(this.statusIsError ? ColorError : this.statusIsWarning ? ColorWarning : ColorOk, this.status);
        }
    }

    // ---- Step 1: source ------------------------------------------------------------------

    private void DrawSource()
    {
        ImGui.TextUnformatted("Where is the plan?");
        if (ImGui.RadioButton("Google Sheets / Docs link", this.mode == SourceMode.Link))
            this.mode = SourceMode.Link;
        ImGui.SameLine();
        if (ImGui.RadioButton("Paste cells", this.mode == SourceMode.Paste))
            this.mode = SourceMode.Paste;
        ImGui.SameLine();
        if (ImGui.RadioButton("Paste plan YAML (AI route)", this.mode == SourceMode.Yaml))
            this.mode = SourceMode.Yaml;
        ImGui.Spacing();

        var footer = this.source != null && this.mode != SourceMode.Yaml ? ImGui.GetFrameHeightWithSpacing() + Scale(8) : 0;
        using (var body = ImRaii.Child("##sourceBody", new Vector2(-1, -footer), false))
        {
            if (body)
            {
                switch (this.mode)
                {
                    case SourceMode.Link:
                        this.DrawLinkSource();
                        break;
                    case SourceMode.Paste:
                        this.DrawPasteSource();
                        break;
                    default:
                        this.DrawYamlSource();
                        break;
                }
            }
        }

        if (footer > 0 && this.source != null)
        {
            ImGui.Separator();
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(ColorOk, "Loaded: " + this.source.Describe());
            ImGui.SameLine();
            using (ImRaii.Disabled(this.jobs.Busy))
            {
                if (ImGui.Button("Next: options >"))
                    this.step = Step.Options;
            }
        }
    }

    private void DrawLinkSource()
    {
        ImGui.TextWrapped(
            "Paste the link of the plan: from the browser's address bar, or Share > Copy link. The sheet or doc must be shared as " +
            "\"Anyone with the link\" (Viewer). MitCue downloads it only when you press Fetch, and only from docs.google.com.");
        ImGui.Spacing();

        var parsed = GoogleLinks.TryParse(this.linkText, out var link, out var linkError);
        ImGui.SetNextItemWidth(-Scale(190));
        var enter = ImGui.InputTextWithHint("##link", "https://docs.google.com/spreadsheets/d/...", ref this.linkText, 4096, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        using (ImRaii.Disabled(!parsed || this.jobs.Busy))
        {
            if ((ImGui.Button("Fetch") || enter) && parsed && !this.jobs.Busy)
            {
                var target = link!;
                this.ConfirmReplaceReview("Loading the link", () => this.StartFetch(target));
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Paste link"))
            this.linkText = (ImGui.GetClipboardText() ?? string.Empty).Trim();
        if (parsed && link != null)
        {
            ImGui.SameLine();
            if (ImGui.Button("Open in browser"))
                Dalamud.Utility.Util.OpenLink(link.BrowserUrl);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Check the sharing settings or which tabs hold the plan.");
        }

        if (!string.IsNullOrWhiteSpace(this.linkText))
        {
            if (parsed && link != null)
            {
                var what = link.IsSheet
                    ? "The whole workbook is downloaded as .xlsx, with its colours (grey = optional is kept)."
                    : "The document is downloaded as a web page: its tables become tabs, the rest a \"Text\" tab (its plain text when the page is too large).";
                ImGui.TextDisabled(GoogleLinks.Describe(link) + ". " + what);
            }
            else
            {
                ImGui.TextColoredWrapped(ColorWarning, linkError);
            }
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Not shared publicly? Copy the cells in Google Sheets and use \"Paste cells\" instead.");
    }

    private void DrawPasteSource()
    {
        ImGui.TextWrapped(
            "In Google Sheets, select the plan's cells including the header row (the row with the column names, e.g. MT, OT, H1 WHM) " +
            "and copy them (Ctrl+C). Then press \"Paste from clipboard\", or click into the box and press Ctrl+V. " +
            "A plan over several tabs: paste one tab at a time, \"Read the cells\" for the first and \"Add as another tab\" for the others.");
        ImGui.Spacing();

        if (ImGui.Button("Paste from clipboard"))
        {
            var text = (ImGui.GetClipboardText() ?? string.Empty).ReplaceLineEndings("\n");
            this.pasteBox.Set(text);
            this.separator = ImportPipeline.GuessSeparator(text);
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear##paste"))
            this.pasteBox.Set(string.Empty);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled($"{Plural(this.pasteBox.Lines, "line")}, {(this.pasteBox.Bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture)} KB");
        if (this.pasteBox.Truncated)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorWarning, "(cut: too long for the box)");
        }

        var boxHeight = Math.Max(Scale(120), ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing() * 2 - Scale(8));
        if (this.pasteBox.Draw("##pasteBox", new Vector2(-1, boxHeight), ImGuiInputTextFlags.AllowTabInput))
            this.separator = ImportPipeline.GuessSeparator(this.pasteBox.Text);

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Columns separated by:");
        ImGui.SameLine();
        if (ImGui.RadioButton("Tab (copied from Google Sheets)", this.separator == '\t'))
            this.separator = '\t';
        ImGui.SameLine();
        if (ImGui.RadioButton("Comma (CSV)", this.separator == ','))
            this.separator = ',';

        ImGui.SetNextItemWidth(Scale(140));
        ImGui.InputTextWithHint("Tab name##pasteTab", "optional, e.g. P3", ref this.pasteTabName, 64);
        HelpMarker("The name of the tab these cells come from. It shows in the review's Source column; a phase name such as P3 also helps to find the phase.");
        ImGui.SameLine();
        var pasted = this.source is { Kind: "paste" } ? this.source : null;
        using (ImRaii.Disabled(this.pasteBox.IsBlank || this.jobs.Busy))
        {
            if (ImGui.Button("Read the cells"))
                this.ConfirmReplaceReview("Reading the cells", () => this.StartReadPaste(null));
            if (pasted != null)
            {
                ImGui.SameLine();
                if (ImGui.Button("Add as another tab"))
                    this.ConfirmReplaceReview("Adding a tab", () => this.StartReadPaste(pasted));
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"Adds these cells to the {Plural(pasted.Document.Tabs.Count, "tab")} read before ({string.Join(", ", pasted.Document.Tabs.Select(t => t.Name))}).");
            }
        }
    }

    private void StartFetch(GoogleLink link)
    {
        this.SetStatus(string.Empty, false);
        var fetcher = this.fetcher;
        this.jobs.Run(
            link.IsSheet ? "Downloading the sheet..." : "Downloading the document...",
            ct => ImportPipeline.LoadLinkAsync(fetcher, link, ct),
            this.OnSourceLoaded,
            ex => this.SetStatus(ImportPipeline.Explain(ex, "Downloading"), true));
    }

    private void StartReadPaste(LoadedSource? addTo)
    {
        this.SetStatus(string.Empty, false);
        var text = this.pasteBox.Text;
        var sep = this.separator;
        var name = this.pasteTabName;
        this.jobs.RunSync(
            addTo != null ? "Adding the pasted tab..." : "Reading the pasted cells...",
            _ => ImportPipeline.LoadPasted(text, sep, addTo, name),
            loaded =>
            {
                this.pasteTabName = string.Empty;
                this.OnSourceLoaded(loaded);
            },
            ex => this.SetStatus(ImportPipeline.Explain(ex, "Reading pasted cells"), true));
    }

    private void OnSourceLoaded(LoadedSource loaded)
    {
        this.source = loaded;
        this.sourceVersion++;
        this.analysis = null;
        this.trial.Retry();
        this.selectedTabs.Clear();
        this.selectedColumns.Clear();
        this.tabsTouched = false;
        this.columnsTouched = false;
        this.planNameTouched = false;
        this.planName = DefaultPlanName(loaded, null);
        this.review = null;
        this.replaceId = null;
        this.saveMessage = string.Empty;

        var data = this.plugin.Data;
        var fight = data.FightForTerritory(this.plugin.Frame.Territory) ?? data.GetFight(this.fightKey) ?? data.GetFight("top") ?? data.FightList.FirstOrDefault();
        this.fightKey = fight?.Key ?? string.Empty;
        this.calibrationKey = fight != null ? ImportPipeline.GuessCalibration(fight, loaded.Document.Title) ?? string.Empty : string.Empty;
        foreach (var tab in loaded.Document.Tabs)
            this.selectedTabs.Add(tab.Name);

        var message = $"Loaded {loaded.Describe()}.";
        if (loaded.LinkedTab != null && loaded.Document.Tabs.Count > 1)
            message += $" Your link points at the tab \"{loaded.LinkedTab}\".";
        if (loaded.Warning != null)
            this.SetWarning(message + " " + loaded.Warning);
        else
            this.SetStatus(message, false);
        this.step = Step.Options;
    }

    private static string DefaultPlanName(LoadedSource loaded, string? column)
    {
        var title = loaded.Kind == "paste"
            ? "Pasted plan " + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : loaded.Document.Title;
        return string.IsNullOrWhiteSpace(column) ? title : $"{title} ({column})";
    }

    // ---- AI route ------------------------------------------------------------------------

    private void DrawYamlSource()
    {
        var data = this.plugin.Data;
        ImGui.TextWrapped(
            "For layouts MitCue cannot read: let any AI chat (ChatGPT, Claude, Gemini, ...) convert the plan, then paste its answer here. " +
            "The answer is checked like an import: unknown mechanics or actions, and actions your job cannot use at the fight's level, are flagged in the review.");
        ImGui.Spacing();
        ImGui.TextColored(ColorAccent, "Step 1: copy the prompt");

        if (this.promptFightKey.Length == 0 || data.GetFight(this.promptFightKey) == null)
        {
            var fight = (this.fightKey.Length > 0 ? data.GetFight(this.fightKey) : null)
                        ?? data.FightForTerritory(this.plugin.Frame.Territory) ?? data.GetFight("top") ?? data.FightList.FirstOrDefault();
            this.promptFightKey = fight?.Key ?? string.Empty;
        }

        ImGui.SetNextItemWidth(Scale(260));
        this.DrawFightCombo("Fight##promptFight", ref this.promptFightKey);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Scale(240));
        this.DrawJobPicker();

        if (this.source != null)
        {
            ImGui.Checkbox($"Include the text of {this.source.Describe()}##promptSource", ref this.promptIncludeSource);
            HelpMarker("The prompt then contains the plan itself (the tabs chosen in Options), so you only paste one thing into the chat. Otherwise paste the plan into the chat after the prompt.");
        }
        else
        {
            ImGui.TextDisabled("Tip: load the sheet first (link or paste) to include its text in the prompt; otherwise paste the plan into the chat after the prompt.");
        }

        var promptFight = data.GetFight(this.promptFightKey);
        using (ImRaii.Disabled(promptFight == null || this.promptJobs.Count == 0))
        {
            if (ImGui.Button("Copy AI prompt") && promptFight != null && this.promptJobs.Count > 0)
                this.CopyPrompt(promptFight);
        }

        if (promptFight == null)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorWarning, data.FightList.Count == 0 ? "Fight data is not available in this build." : "Choose a fight.");
        }
        else if (this.promptJobs.Count == 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorWarning, "Choose at least one job.");
        }
        else if (this.promptMessage.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextColoredWrapped(ColorOk, this.promptMessage);
        }

        ImGui.Spacing();
        ImGui.TextColored(ColorAccent, "Step 2: paste the AI's answer");
        if (ImGui.Button("Paste from clipboard##yaml"))
            this.yamlBox.Set((ImGui.GetClipboardText() ?? string.Empty).ReplaceLineEndings("\n"));
        ImGui.SameLine();
        if (ImGui.Button("Clear##yaml"))
            this.yamlBox.Set(string.Empty);
        if (this.yamlBox.Truncated)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorWarning, "(cut: too long for the box)");
        }

        var boxHeight = Math.Max(Scale(100), ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing() - Scale(6));
        this.yamlBox.Draw("##yamlBox", new Vector2(-1, boxHeight), ImGuiInputTextFlags.AllowTabInput);
        using (ImRaii.Disabled(this.yamlBox.IsBlank || this.jobs.Busy))
        {
            if (ImGui.Button("Check the YAML and review"))
                this.ConfirmReplaceReview("Reading the YAML", this.StartParseYaml);
        }

        ImGui.SameLine();
        ImGui.TextDisabled("Text around the ```yaml block of the answer is ignored. MitCue plan files (YAML or JSON) can be pasted here too.");
    }

    private void DrawJobPicker()
    {
        var label = this.promptJobs.Count == 0 ? "(choose jobs)" : string.Join(", ", this.promptJobs);
        using var combo = ImRaii.Combo("Jobs##promptJobs", label, ImGuiComboFlags.HeightLarge);
        if (!combo)
            return;

        foreach (var role in new[] { Role.Healer, Role.Tank, Role.Melee, Role.PhysicalRanged, Role.Caster })
        {
            ImGui.TextDisabled(role switch
            {
                Role.PhysicalRanged => "Physical ranged",
                Role.Caster => "Caster",
                _ => role.ToString(),
            });
            foreach (var job in Jobs.All.Where(j => Jobs.RoleOf(j) == role && j is not (Job.BLU or Job.BST)).OrderBy(j => j.ToString(), StringComparer.Ordinal))
            {
                var on = this.promptJobs.Contains(job);
                if (ImGui.Checkbox($"{job} ({Jobs.FullName(job)})##pj{(int)job}", ref on))
                {
                    if (on)
                        this.promptJobs.Add(job);
                    else
                        this.promptJobs.Remove(job);
                    this.promptMessage = string.Empty;
                }
            }
        }
    }

    private void CopyPrompt(FightDefinition fight)
    {
        var data = this.plugin.Data;
        var jobsList = this.promptJobs.ToList();
        var notes = new List<string>();

        IReadOnlyList<PromptAction> actions = [];
        if (data.ActionsLoaded)
        {
            try
            {
                actions = LlmPrompt.ActionsFor(data.Actions, jobsList, fight.SyncLevel);
            }
            catch (Exception ex)
            {
                notes.Add("(The action list is not available: " + ImportPipeline.Explain(ex, "The action list") + ")");
            }
        }

        IReadOnlyList<PromptAlias> aliases = [];
        if (data.Aliases != null)
        {
            try
            {
                aliases = LlmPrompt.AliasesFor(data.Aliases, jobsList);
            }
            catch (Exception ex)
            {
                notes.Add("(The alias list is not available: " + ImportPipeline.Explain(ex, "The alias list") + ")");
            }
        }
        else
        {
            notes.Add("(The alias list is not available in this build: use the action names.)");
        }

        string? sourceText = null;
        if (this.source != null && this.promptIncludeSource)
        {
            var tabs = this.selectedTabs.Count > 0 ? this.selectedTabs.ToList() : null;
            sourceText = SheetText.ForPrompt(this.source.Document, tabs);
        }

        var prompt = LlmPrompt.Build(new LlmPromptInput
        {
            Fight = fight,
            Jobs = jobsList,
            Actions = actions,
            Aliases = aliases,
            SourceText = sourceText,
            SourceUrl = this.source?.Url,
            PlanName = this.source != null ? DefaultPlanName(this.source, null) : null,
            Slot = this.plugin.Config.DefaultSlot,
            DataNote = notes.Count > 0 ? string.Join(' ', notes) : null,
        });

        ImGui.SetClipboardText(prompt);
        var kb = (Encoding.UTF8.GetByteCount(prompt) / 1024.0).ToString("0", CultureInfo.InvariantCulture);
        var tokens = (prompt.Length / 4000.0).ToString("0", CultureInfo.InvariantCulture);
        this.promptMessage = sourceText != null
            ? $"Copied ({kb} KB, about {tokens}k tokens, plan included). Paste it into an AI chat."
            : $"Copied ({kb} KB, about {tokens}k tokens). Paste it into an AI chat, then paste your plan after it.";
        if (notes.Count > 0)
            this.promptMessage += " Some data was missing: " + string.Join(' ', notes);
    }

    private void StartParseYaml()
    {
        this.SetStatus(string.Empty, false);
        var text = this.yamlBox.Text;
        this.jobs.RunSync(
            "Reading the YAML...",
            _ => ImportPipeline.ParseYaml(text),
            this.OnYamlParsed,
            ex => this.SetStatus(ImportPipeline.Explain(ex, "Reading plan YAML"), true));
    }

    private void OnYamlParsed(MitPlan plan)
    {
        var data = this.plugin.Data;
        var fromPlan = string.IsNullOrWhiteSpace(plan.Fight) ? null : data.GetFight(plan.Fight);
        var fight = fromPlan ?? data.GetFight(this.promptFightKey);
        if (fight == null)
        {
            this.SetStatus(
                data.FightList.Count == 0
                    ? "Fight data is not available in this build, so the plan cannot be checked."
                    : $"The plan is for an unknown fight \"{plan.Fight}\". Choose the fight above and try again.",
                true);
            return;
        }

        var model = new ReviewModel(plan, fight, data.Actions, data.ActionsLoaded, ReviewOrigin.Yaml);
        plan.Fight = fight.Key;
        if (string.IsNullOrWhiteSpace(plan.Name))
            plan.Name = $"AI-converted plan ({fight.Key.ToUpperInvariant()})";
        plan.Source ??= new PlanSource();
        if (string.IsNullOrWhiteSpace(plan.Source.Kind) || plan.Source.Kind == "paste")
            plan.Source.Kind = "llm";
        plan.Source.Url ??= this.promptIncludeSource ? this.source?.Url : null;
        plan.Source.Imported ??= DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        this.StartReview(model, plan.Name);
        var errors = model.CountWorst(DiagnosticSeverity.Error);
        this.SetStatus(
            $"Read {Plural(plan.Entries.Count, "entry", "entries")} for {fight.Name}" +
            (errors > 0 ? $"; {Plural(errors, "entry has", "entries have")} errors to fix." : "."),
            errors > 0);
    }

    // ---- Step 2: options -----------------------------------------------------------------

    private void DrawOptions()
    {
        var src = this.source!;
        var data = this.plugin.Data;
        var doc = src.Document;

        // While a job runs (the tab check or the import), the options stay as they are: an import uses the options it
        // started with, and its result would not match changed ones.
        var busy = this.jobs.Busy;
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Source: " + src.Describe());
        ImGui.SameLine();
        using (ImRaii.Disabled(busy))
        {
            if (ImGui.SmallButton("Change source"))
                this.step = Step.Source;
        }

        if (src.Note != null)
            ImGui.TextDisabled(src.Note);

        if (data.FightList.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextColoredWrapped(ColorError, "Fight data (data/fights) is not available in this build, so plans cannot be imported yet. See Problems in /mitcue.");
            return;
        }

        if (data.GetFight(this.fightKey) == null)
            this.fightKey = (data.GetFight("top") ?? data.FightList[0]).Key;
        var fight = data.GetFight(this.fightKey)!;

        using (ImRaii.Disabled(busy))
        {
            ImGui.SetNextItemWidth(Scale(280));
            if (this.DrawFightCombo("Fight", ref this.fightKey))
            {
                fight = data.GetFight(this.fightKey)!;
                this.calibrationKey = ImportPipeline.GuessCalibration(fight, doc.Title) ?? string.Empty;
            }
        }

        ImGui.SameLine();
        ImGui.TextDisabled($"level {fight.SyncLevel.ToString(CultureInfo.InvariantCulture)}, {Plural(fight.Mechanics.Count, "mechanic")}");

        // Trial import of everything, for the tab and column lists (again when the fight changes).
        var key = fight.Key + "|" + this.sourceVersion.ToString(CultureInfo.InvariantCulture);
        if (this.trial.ShouldStart(key, busy))
            this.StartAnalysis(src, fight, key);

        ImGui.Spacing();
        var bottom = ImGui.GetFrameHeightWithSpacing() * 4 + Scale(16);
        using (var lists = ImRaii.Child("##optionLists", new Vector2(-1, -bottom), false))
        {
            if (lists)
            {
                using (ImRaii.Disabled(this.jobs.Busy))
                {
                    this.DrawTabChoices(doc, key);
                    ImGui.Spacing();
                    this.DrawColumnChoices();
                }
            }
        }

        ImGui.Separator();
        using (ImRaii.Disabled(this.jobs.Busy))
        {
            ImGui.SetNextItemWidth(Scale(420));
            if (ImGui.InputText("Plan name", ref this.planName, 200))
                this.planNameTouched = true;

            ImGui.SetNextItemWidth(Scale(260));
            var calibrationLabel = this.calibrationKey.Length == 0 ? "(automatic)" : this.calibrationKey;
            using var combo = ImRaii.Combo("Calibration preset", calibrationLabel);
            if (combo)
            {
                if (ImGui.Selectable("(automatic)", this.calibrationKey.Length == 0))
                    this.calibrationKey = string.Empty;
                foreach (var preset in fight.Calibration.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    if (ImGui.Selectable(preset, this.calibrationKey == preset))
                        this.calibrationKey = preset;
                }
            }
        }

        HelpMarker(
            "Sheets write times from the pull or from each phase's start. MitCue fits each phase's start from the rows it recognises; " +
            "for known sheets (e.g. carry_the_clear) measured start times are used when the fit is not possible. Leave it on automatic unless the review shows odd times.");

        var (canImport, why) = this.CanImport(data);
        using (ImRaii.Disabled(!canImport || this.jobs.Busy))
        {
            if (ImGui.Button("Import and review >") && canImport)
                this.ConfirmReplaceReview("Importing again", () => this.StartImport(src, fight));
        }

        ImGui.SameLine();
        if (!canImport)
        {
            ImGui.TextColored(ColorWarning, why);
            ImGui.SameLine();
        }

        using (ImRaii.Disabled(this.jobs.Busy))
        {
            if (this.review is { Origin: ReviewOrigin.Importer })
            {
                if (ImGui.Button("Back to the review"))
                    this.step = Step.Review;
                ImGui.SameLine();
            }

            if (ImGui.Button("Not reading it right? Use the AI route"))
            {
                this.mode = SourceMode.Yaml;
                this.promptFightKey = fight.Key;
                this.promptIncludeSource = true;
                this.step = Step.Source;
            }
        }
    }

    private (bool Ok, string Why) CanImport(Data.CoreData data)
    {
        if (data.Aliases == null)
            return (false, "The alias dictionary did not load (see Problems in /mitcue).");
        if (this.analysis == null)
            return (false, this.trial.Running ? "Looking at the tabs first..." : "Check the tabs first (\"Check the tabs\" above).");
        if (this.selectedTabs.Count == 0)
            return (false, "Choose at least one tab.");
        if (this.analysis is { Available: true } a && a.ColumnsIn(this.selectedTabs).Count > 0 && this.ChosenColumns().Count == 0)
            return (false, "Choose at least one column.");
        return (true, string.Empty);
    }

    private List<string> ChosenColumns()
    {
        if (this.analysis is not { Available: true } a)
            return [];
        return a.ColumnsIn(this.selectedTabs).Where(c => this.selectedColumns.Contains(c.Key)).Select(c => c.Key).ToList();
    }

    private void DrawTabChoices(SheetDocument doc, string key)
    {
        ImGui.TextColored(ColorAccent, "Tabs to import");
        var a = this.analysis;
        if (a == null && this.trial.Running)
        {
            ImGui.TextDisabled(AnalysisLabel);
        }
        else if (a == null)
        {
            if (this.trial.CancelledByPlayer(key))
                ImGui.TextColored(ColorWarning, "The tab check was cancelled: your tabs and columns were not chosen for you.");
            else
                ImGui.TextDisabled("The tabs have not been checked yet.");
            ImGui.SameLine();
            if (ImGui.SmallButton("Check the tabs"))
                this.trial.Retry();
        }
        else if (!a.Available)
        {
            ImGui.TextColoredWrapped(ColorWarning, a.Reason ?? "The tabs could not be analysed.");
            if (!this.tabsTouched)
            {
                ImGui.TextDisabled(this.defaultTabsBasis != null
                    ? $"Chosen from the tabs' header rows: {this.defaultTabsBasis}. Tick other tabs if your plan continues there."
                    : "Every tab with content is ticked: untick the tabs that are not part of your plan, so the AI prompt holds each call once.");
            }
        }

        var repeated = a is { Available: true } ? a.RepeatedTabs(this.selectedTabs) : new Dictionary<string, string>();
        using var table = ImRaii.Table("##tabs", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;

        ImGui.TableSetupColumn("Tab", ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableSetupColumn("Rows", ImGuiTableColumnFlags.WidthFixed, Scale(60));
        ImGui.TableSetupColumn("Plan entries", ImGuiTableColumnFlags.WidthFixed, Scale(90));
        ImGui.TableSetupColumn("Columns found", ImGuiTableColumnFlags.WidthStretch, 3f);
        ImGui.TableHeadersRow();

        for (var i = 0; i < doc.Tabs.Count; i++)
        {
            var tab = doc.Tabs[i];
            var stats = a?.Tabs.FirstOrDefault(t => string.Equals(t.Name, tab.Name, StringComparison.OrdinalIgnoreCase));
            using var id = ImRaii.PushId(i);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var on = this.selectedTabs.Contains(tab.Name);
            if (ImGui.Checkbox(tab.Name + "##tab", ref on))
            {
                if (on)
                    this.selectedTabs.Add(tab.Name);
                else
                    this.selectedTabs.Remove(tab.Name);
                this.tabsTouched = true;
                this.ApplyDefaultColumns();
            }

            if (this.source?.LinkedTab != null && string.Equals(this.source.LinkedTab, tab.Name, StringComparison.OrdinalIgnoreCase) && doc.Tabs.Count > 1)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(your link)");
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted((stats?.NonEmptyRows ?? SheetText.NonEmptyRows(tab)).ToString(CultureInfo.InvariantCulture));
            ImGui.TableNextColumn();
            if (stats != null && a!.Available)
                ImGui.TextUnformatted(stats.Entries.ToString(CultureInfo.InvariantCulture));
            else
                ImGui.TextDisabled("?");
            ImGui.TableNextColumn();
            if (stats is { Columns.Count: > 0 })
                ImGui.TextUnformatted(string.Join(", ", stats.Columns));
            else if (a is { Available: true })
                ImGui.TextDisabled("no plan rows found");
            if (on && repeated.TryGetValue(tab.Name, out var other))
                ImGui.TextColored(ColorWarning, $"Repeats most calls of \"{other}\": choose one of them to avoid duplicates.");
        }
    }

    private void DrawColumnChoices()
    {
        ImGui.TextColored(ColorAccent, "Columns to import (whose calls)");
        var a = this.analysis;
        if (a is not { Available: true })
        {
            ImGui.TextDisabled("Columns are found during the import; every column is imported.");
            return;
        }

        var columns = a.ColumnsIn(this.selectedTabs);
        if (columns.Count == 0)
        {
            ImGui.TextDisabled("No columns with calls in the chosen tabs.");
            return;
        }

        var job = this.plugin.Frame.Job;
        var mine = ImportAnalysis.PlayerColumns(columns, job, this.plugin.Config.DefaultSlot).Select(c => c.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ImGui.TextDisabled("A plan can hold every player's calls; in game you pick which column is yours. Tip: also import your co-healer's column to see their calls (dimmed).");
        foreach (var column in columns)
        {
            var on = this.selectedColumns.Contains(column.Key);
            var label = AssigneeLabel(column) + " - " + Plural(a.EntriesOf(column.Key, this.selectedTabs), "entry", "entries");
            if (ImGui.Checkbox(label + "##col" + column.Key, ref on))
            {
                if (on)
                    this.selectedColumns.Add(column.Key);
                else
                    this.selectedColumns.Remove(column.Key);
                this.columnsTouched = true;
                this.UpdateDefaultPlanName();
            }

            if (mine.Contains(column.Key))
            {
                ImGui.SameLine();
                ImGui.TextColored(ColorOk, job != Job.None ? $"(you: {job})" : "(your slot)");
            }
        }
    }

    private static string AssigneeLabel(Assignee a)
    {
        var details = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(a.Slot))
            details.Add(a.Slot);
        if (a.Job is { } job && job != Job.None)
            details.Add(job.ToString());
        return details.Count > 0 && !string.Equals(string.Join(" ", details), a.Key, StringComparison.OrdinalIgnoreCase)
            ? $"{a.Key} ({string.Join(", ", details)})"
            : a.Key;
    }

    private void StartAnalysis(LoadedSource src, FightDefinition fight, string key)
    {
        this.trial.Start(key);
        this.analysis = null;
        var data = this.plugin.Data;
        if (data.Aliases == null)
        {
            this.OnAnalysis(src, key, ImportAnalysis.Unavailable(src.Document, "The alias dictionary did not load, so the importer cannot run (see Problems in /mitcue)."));
            return;
        }

        var options = new ImportOptions
        {
            Fight = fight,
            Actions = data.Actions,
            Aliases = data.Aliases,
            SourceUrl = src.Url,
            CalibrationKey = this.calibrationKey.Length > 0 ? this.calibrationKey : null,
            ImportedDate = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
        this.jobs.RunSync(
            AnalysisLabel,
            _ => ImportPipeline.Analyze(src.Document, options),
            result => this.OnAnalysis(src, key, result),
            ex => this.OnAnalysis(src, key, ImportAnalysis.Unavailable(src.Document, ImportPipeline.Explain(ex, "The sheet importer"))));
    }

    private void OnAnalysis(LoadedSource src, string key, ImportAnalysis result)
    {
        if (!ReferenceEquals(this.source, src) || !this.trial.IsCurrent(key))
            return;
        this.trial.Finished(key);
        this.analysis = result;
        this.ApplyDefaults(result);
    }

    private void ApplyDefaults(ImportAnalysis result)
    {
        var job = this.plugin.Frame.Job;
        var slot = this.plugin.Config.DefaultSlot;
        if (!this.tabsTouched)
        {
            this.selectedTabs.Clear();
            string? basis = null;
            var tabs = result.Available
                ? result.DefaultTabs(job, slot, this.source?.LinkedTab)
                : result.HeaderDefaultTabs(job, slot, this.source?.LinkedTab, out basis);
            this.defaultTabsBasis = basis;
            foreach (var name in tabs)
                this.selectedTabs.Add(name);
        }

        if (!this.columnsTouched)
            this.ApplyDefaultColumns();
    }

    private void ApplyDefaultColumns()
    {
        if (this.columnsTouched || this.analysis is not { Available: true } a)
            return;
        this.selectedColumns.Clear();
        foreach (var key in a.DefaultColumns(this.selectedTabs, this.plugin.Frame.Job, this.plugin.Config.DefaultSlot))
            this.selectedColumns.Add(key);
        this.UpdateDefaultPlanName();
    }

    private void UpdateDefaultPlanName()
    {
        if (this.planNameTouched || this.source == null)
            return;
        var chosen = this.ChosenColumns();
        this.planName = DefaultPlanName(this.source, chosen.Count == 1 ? chosen[0] : null);
    }

    private void StartImport(LoadedSource src, FightDefinition fight)
    {
        var data = this.plugin.Data;
        var doc = src.Document;
        var tabs = doc.Tabs.Where(t => this.selectedTabs.Contains(t.Name)).Select(t => t.Name).ToList();
        var columns = this.analysis is { Available: true } a && a.ColumnsIn(this.selectedTabs).Count > 0 ? this.ChosenColumns() : null;
        var name = this.planName.Trim().Length > 0 ? this.planName.Trim() : DefaultPlanName(src, null);
        var date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var options = new ImportOptions
        {
            Fight = fight,
            Actions = data.Actions,
            Aliases = data.Aliases!,
            PlanName = name,
            SourceUrl = src.Url,
            CalibrationKey = this.calibrationKey.Length > 0 ? this.calibrationKey : null,
            Tabs = tabs,
            Assignees = columns,
            ImportedDate = date,
        };

        var choices = this.OptionsKey();
        this.SetStatus(string.Empty, false);
        this.jobs.RunSync(
            "Importing...",
            _ => ImportPipeline.Import(doc, options),
            result =>
            {
                if (!ReferenceEquals(this.source, src))
                    return;
                if (this.OptionsKey() != choices)
                {
                    // Cannot happen through the window (the options are locked while importing): never show a review
                    // for other options than the ones on screen.
                    this.SetStatus("The options changed while importing, so that import was dropped: press \"Import and review\" again.", true);
                    return;
                }

                var plan = result.Plan;
                plan.Fight = fight.Key;
                if (string.IsNullOrWhiteSpace(plan.Name))
                    plan.Name = name;
                plan.Source ??= new PlanSource();
                if (string.IsNullOrWhiteSpace(plan.Source.Kind) || (plan.Source.Kind == "paste" && src.Kind != "paste"))
                    plan.Source.Kind = src.Kind;
                plan.Source.Url ??= src.Url;
                if (plan.Source.Tabs == null || plan.Source.Tabs.Count == 0)
                    plan.Source.Tabs = tabs;
                plan.Source.Imported ??= date;

                var model = new ReviewModel(plan, fight, data.Actions, data.ActionsLoaded, ReviewOrigin.Importer, result.Diagnostics, result.PhaseZeros, result.PhaseSpread);
                this.StartReview(model, plan.Name);
                var errors = model.CountWorst(DiagnosticSeverity.Error);
                var warnings = model.CountWorst(DiagnosticSeverity.Warning);
                this.SetStatus(
                    $"Imported {Plural(plan.Entries.Count, "entry", "entries")}" +
                    (errors + warnings > 0 ? $": {errors.ToString(CultureInfo.InvariantCulture)} with errors, {warnings.ToString(CultureInfo.InvariantCulture)} with warnings. Check them below." : ". Check them below."),
                    false);
            },
            ex => this.SetStatus(ImportPipeline.Explain(ex, "The sheet importer"), true));
    }

    /// <summary>The Options step's choices as one string, to tell whether they changed while an import ran.</summary>
    private string OptionsKey() => string.Join(
        '\u0001',
        this.fightKey,
        this.calibrationKey,
        this.planName,
        string.Join('\u0002', this.selectedTabs.Order(StringComparer.OrdinalIgnoreCase)),
        string.Join('\u0002', this.ChosenColumns().Order(StringComparer.OrdinalIgnoreCase)));

    private bool DrawFightCombo(string label, ref string key)
    {
        var fights = this.plugin.Data.FightList;
        var currentKey = key;
        var current = fights.FirstOrDefault(f => string.Equals(f.Key, currentKey, StringComparison.OrdinalIgnoreCase));
        var changed = false;
        using var combo = ImRaii.Combo(label, current?.Name ?? "(choose a fight)");
        if (!combo)
            return false;
        var here = this.plugin.Data.FightForTerritory(this.plugin.Frame.Territory);
        foreach (var fight in fights)
        {
            var text = fight == here ? $"{fight.Name} (this zone)" : fight.Name;
            if (ImGui.Selectable($"{text}##{fight.Key}", fight == current) && fight != current)
            {
                key = fight.Key;
                changed = true;
            }
        }

        return changed;
    }

    // ---- Step 4: save --------------------------------------------------------------------

    private void DrawSave()
    {
        var model = this.review!;
        var plan = model.Plan;
        var fight = model.Fight;
        var store = this.plugin.Plans;
        var job = this.plugin.Frame.Job;

        ImGui.SetNextItemWidth(Scale(420));
        var name = plan.Name;
        if (ImGui.InputText("Plan name##save", ref name, 200))
            plan.Name = name;

        var columns = plan.Assignees.Count > 0 ? string.Join(", ", plan.Assignees.Select(AssigneeLabel)) : "none (by job/slot only)";
        ImGui.TextWrapped($"{Plural(plan.Entries.Count, "entry", "entries")} for {fight.Name}. Columns: {columns}.");
        if (job != Job.None)
        {
            // Counted the way the overlay selects them, with the column choice the plan is saved with.
            var choice = this.ColumnChoiceFor(model);
            ImGui.TextColored(choice.Mine > 0 ? ColorOk : ColorWarning, $"{Plural(choice.Mine, "entry applies", "entries apply")} to you as {job}" + (choice.Mine == 0 ? ": check the columns you imported." : "."));
            if (choice.Save)
            {
                ImGui.TextDisabled(
                    $"Your calls are in the columns {string.Join(", ", choice.Columns)}, so the plan is saved with \"{ImportColumns.Describe(choice.Mapping, job)}\" as your column " +
                    "(change it in /mitcue > Plans).");
            }
            else if (choice.Missed > 0)
            {
                // A kept choice, or the job's column when no single choice has every call without another column's (LPDU's
                // tank pairs: the job-less "MT" calls are left out rather than showing every pair's calls).
                var whose = choice.Kept ? "Your column choice for the plan this replaces" : "The column the plan gets for you";
                ImGui.TextColoredWrapped(
                    ColorWarning,
                    $"{whose} ({ImportColumns.Describe(choice.Mapping, job)}) leaves out {Plural(choice.Missed, "of your calls", "of your calls")} " +
                    $"(in {string.Join(", ", choice.MissedIn)}): after saving, check \"My column\" in /mitcue > Plans.");
            }
        }

        var errors = model.CountWorst(DiagnosticSeverity.Error);
        if (errors > 0)
        {
            ImGui.TextColoredWrapped(
                ColorWarning,
                $"{Plural(errors, "entry has", "entries have")} errors: entries with an unknown mechanic are skipped and unknown actions are not tracked. " +
                "Fix them in the review, or save anyway.");
        }

        ImGui.Spacing();

        // Replace an earlier import of the same source, so re-imports keep the active-plan and column settings.
        var replaceable = store.All
            .Where(p => !p.IsReadOnly && string.Equals(p.Plan.Fight, fight.Key, StringComparison.OrdinalIgnoreCase) && SameSource(p.Plan.Source, plan.Source))
            .ToList();
        if (this.replaceId != null && replaceable.All(p => p.Id != this.replaceId))
            this.replaceId = null;
        if (replaceable.Count > 0)
        {
            var current = replaceable.FirstOrDefault(p => p.Id == this.replaceId);
            ImGui.SetNextItemWidth(Scale(420));
            using (var combo = ImRaii.Combo("Save as", current == null ? "a new plan" : $"replace \"{current.DisplayName}\""))
            {
                if (combo)
                {
                    if (ImGui.Selectable("a new plan", current == null))
                        this.replaceId = null;
                    foreach (var p in replaceable)
                    {
                        if (ImGui.Selectable($"replace \"{p.DisplayName}\" ({Plural(p.Plan.Entries.Count, "entry", "entries")})##{p.Id}", current == p))
                            this.replaceId = p.Id;
                    }
                }
            }

            HelpMarker("You imported this sheet before. Replacing that plan keeps its place as your active plan and your column choice.");
        }

        if (job != Job.None)
            ImGui.Checkbox($"Make it my active plan for {fight.Name} as {job}", ref this.setActive);
        else
            ImGui.TextDisabled("Switch to a combat job to make it your active plan right away (or choose it later in /mitcue > Plans).");

        ImGui.Spacing();
        if (ImGui.Button("< Back to the review"))
            this.step = Step.Review;
        ImGui.SameLine();
        using (ImRaii.Disabled(this.jobs.Busy || plan.Entries.Count == 0))
        {
            if (ImGui.Button("Save plan"))
                this.SavePlan(model, job);
        }

        if (this.saveMessage.Length > 0)
            ImGui.TextColoredWrapped(ColorError, this.saveMessage);
    }

    /// <summary>
    /// The column choice the plan gets for the player's job and the entries the overlay will then show (cached until the
    /// review, the replaced plan, the job or the store changes).
    /// </summary>
    private ColumnChoice ColumnChoiceFor(ReviewModel model)
    {
        var job = this.plugin.Frame.Job;
        var store = this.plugin.Plans;
        var slot = this.plugin.Config.DefaultSlot;
        var key = (model, model.Revision, job, this.replaceId, store.Revision, slot);
        if (this.columnChoice != null && this.columnChoiceKey.Equals(key))
            return this.columnChoice;

        // The replaced plan's choice, its column named as this import names it: PlanStore.Save carries the saved choices over
        // the same way ("WARDRK WAR" of an import that kept the xlsx export's tab names is "WAR/DRK WAR" here).
        var kept = this.replaceId != null && store.Get(this.replaceId) is { } replaced ? PlanStore.CarryOver(model.Plan, store.SavedMapping(replaced.Plan, job)) : null;
        this.columnChoice = ImportColumns.Choose(model.Plan, job, model.Fight.SyncLevel, slot, kept);
        this.columnChoiceKey = key;
        return this.columnChoice;
    }

    /// <summary>
    /// The earlier import of the same sheet, tabs and columns, if there is exactly one: replaced by default. Column keys
    /// compare in their xlsx-export form, like tab names (<see cref="SameSource"/>): LPDU's "WAR/DRK WAR" is "WARDRK WAR"
    /// in an import that kept the export's tab names.
    /// </summary>
    internal static string? DefaultReplacement(PlanStore store, MitPlan plan)
    {
        var columns = plan.Assignees.Select(a => ImportPipeline.WithoutExcelInvalidCharacters(a.Key)).ToHashSet(StringComparer.Ordinal);
        var same = store.All
            .Where(p => !p.IsReadOnly && string.Equals(p.Plan.Fight, plan.Fight, StringComparison.OrdinalIgnoreCase) && SameSource(p.Plan.Source, plan.Source))
            .Where(p => p.Plan.Source?.Tabs is { Count: > 0 } && plan.Source?.Tabs is { Count: > 0 })
            .Where(p => columns.SetEquals(p.Plan.Assignees.Select(a => ImportPipeline.WithoutExcelInvalidCharacters(a.Key))))
            .ToList();
        return same.Count == 1 ? same[0].Id : null;
    }

    /// <summary>
    /// True for two imports of the same file and tabs. Tab names compare as the xlsx export has them
    /// (<see cref="ImportPipeline.ExcelTabName"/>): the link import restores Google's names ("P1/P2") when it can read the
    /// sheet's tab list, and keeps the export's ("P1P2") when it cannot or the plan was imported before it could.
    /// </summary>
    internal static bool SameSource(PlanSource? a, PlanSource? b)
    {
        if (a?.Url is not { Length: > 0 } ua || b?.Url is not { Length: > 0 } ub)
            return false;
        var la = GoogleLinks.Parse(ua);
        var lb = GoogleLinks.Parse(ub);
        var sameFile = la != null && lb != null ? la.Kind == lb.Kind && la.Id == lb.Id : string.Equals(ua, ub, StringComparison.Ordinal);
        if (!sameFile)
            return false;
        var ta = a.Tabs ?? [];
        var tb = b.Tabs ?? [];
        return ta.Count == 0 || tb.Count == 0 || ta.Select(ImportPipeline.ExcelTabName).ToHashSet(StringComparer.Ordinal).SetEquals(tb.Select(ImportPipeline.ExcelTabName));
    }

    private void SavePlan(ReviewModel model, Job job)
    {
        var plan = model.Plan;
        var store = this.plugin.Plans;
        var choice = job != Job.None ? this.ColumnChoiceFor(model) : null;
        if (string.IsNullOrWhiteSpace(plan.Name))
            plan.Name = "Imported plan";
        plan.Fight = model.Fight.Key;
        plan.Format = MitPlan.CurrentFormat;
        if (this.replaceId != null)
            plan.Id = this.replaceId;
        else if (store.Get(plan.Id) != null)
            plan.Id = Guid.NewGuid().ToString("D"); // never overwrite an unrelated plan that happens to share the id

        if (!store.Save(plan, out var error))
        {
            this.saveMessage = "The plan was not saved: " + (error ?? "unknown error") +
                               (error != null && error.StartsWith("Not implemented", StringComparison.Ordinal) ? " Saving plans is not available in this build yet." : string.Empty);
            return;
        }

        // The automatic column suggestion would leave out some of the player's calls: save the choice that has them all.
        if (choice is { Save: true } && store.SavedMapping(plan, job) == null)
            store.SetMapping(plan, job, choice.Mapping);

        if (this.setActive && job != Job.None)
            store.SetActive(model.Fight.Key, job, plan.Id);

        var where = this.setActive && job != Job.None ? $", active for {model.Fight.Key.ToUpperInvariant()} as {job}" : string.Empty;
        if (choice is { Save: true })
            where += $", your column: {ImportColumns.Describe(choice.Mapping, job)}";
        Plugin.Chat.Print($"[MitCue] Saved plan \"{plan.Name}\" ({Plural(plan.Entries.Count, "entry", "entries")}{where}).");
        this.ResetAfterSave();
        this.IsOpen = false;
    }

    private void ResetAfterSave()
    {
        this.jobs.Cancel();
        this.review = null;
        this.source = null;
        this.analysis = null;
        this.trial.Retry();
        this.selectedTabs.Clear();
        this.selectedColumns.Clear();
        this.replaceId = null;
        this.saveMessage = string.Empty;
        this.promptMessage = string.Empty;
        this.columnChoice = null;
        this.defaultTabsBasis = null;
        this.yamlBox.Set(string.Empty);
        this.SetStatus(string.Empty, false);
        this.step = Step.Source;
    }
}
