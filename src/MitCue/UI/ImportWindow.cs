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
/// 3. Review: every entry with its mechanic, timing, actions, who, flags, problems and source cell; mechanic, offset and
///    flags can be edited and entries deleted;
/// 4. Save: stored with <see cref="PlanStore.Save"/>, optionally made the active plan for the current job.
/// Downloads, workbook parsing and imports run on background tasks (<see cref="BackgroundJobs"/>); results come back on
/// the framework thread. Nothing here throws out of <see cref="Draw"/>.
/// </summary>
internal sealed partial class ImportWindow : Window, IDisposable
{
    /// <summary>Byte capacity of the paste boxes (the ImGui default of 512 bytes is far too small for a sheet).</summary>
    private const int PasteBoxBytes = 1024 * 1024;

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

    // Source step.
    private string linkText = string.Empty;
    private char separator = '\t';
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
    private string? analysisKey;
    private readonly HashSet<string> selectedTabs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> selectedColumns = new(StringComparer.OrdinalIgnoreCase);
    private bool tabsTouched;
    private bool columnsTouched;
    private string planName = string.Empty;
    private bool planNameTouched;
    private string calibrationKey = string.Empty;

    // Save step.
    private bool setActive = true;
    private string? replaceId;
    private string saveMessage = string.Empty;

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
        // No background work while the window is closed; what was loaded stays for the next time.
        if (this.jobs.Busy)
            this.SetStatus($"\"{this.jobs.Current?.TrimEnd('.')}\" was cancelled when the window closed.", true);
        this.jobs.Cancel();
    }

    /// <inheritdoc/>
    public override void Draw()
    {
        try
        {
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

    private void PostToFramework(Action action) =>
        _ = Plugin.Framework.RunOnFrameworkThread(() =>
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

    private void SetStatus(string message, bool isError)
    {
        this.status = message;
        this.statusIsError = isError;
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
                this.jobs.Cancel();
                this.SetStatus("Cancelled.", false);
            }
        }
        else if (this.status.Length > 0)
        {
            ImGui.TextColoredWrapped(this.statusIsError ? ColorError : ColorOk, this.status);
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
                this.StartFetch(link!);
        }

        ImGui.SameLine();
        if (ImGui.Button("Paste link"))
            this.linkText = (ImGui.GetClipboardText() ?? string.Empty).Trim();

        if (this.linkText.Trim().Length > 0)
        {
            if (parsed && link != null)
            {
                var what = link.IsSheet
                    ? "The whole workbook is downloaded as .xlsx, with its colours (grey = optional is kept)."
                    : "The document is downloaded as a web page: its tables become tabs, the rest a \"Text\" tab.";
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
            "and copy them (Ctrl+C). Then press \"Paste from clipboard\", or click into the box and press Ctrl+V.");
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
        ImGui.SameLine();
        using (ImRaii.Disabled(this.pasteBox.Text.Trim().Length == 0 || this.jobs.Busy))
        {
            if (ImGui.Button("Read the cells"))
                this.StartReadPaste();
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

    private void StartReadPaste()
    {
        this.SetStatus(string.Empty, false);
        var text = this.pasteBox.Text;
        var sep = this.separator;
        this.jobs.RunSync(
            "Reading the pasted cells...",
            _ => ImportPipeline.LoadPasted(text, sep),
            this.OnSourceLoaded,
            ex => this.SetStatus(ImportPipeline.Explain(ex, "Reading pasted cells"), true));
    }

    private void OnSourceLoaded(LoadedSource loaded)
    {
        this.source = loaded;
        this.sourceVersion++;
        this.analysis = null;
        this.analysisKey = null;
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
        using (ImRaii.Disabled(this.yamlBox.Text.Trim().Length == 0 || this.jobs.Busy))
        {
            if (ImGui.Button("Check the YAML and review"))
                this.StartParseYaml();
        }

        ImGui.SameLine();
        ImGui.TextDisabled("Text around the ```yaml block of the answer is ignored.");
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

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Source: " + src.Describe());
        ImGui.SameLine();
        if (ImGui.SmallButton("Change source"))
            this.step = Step.Source;
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

        ImGui.SetNextItemWidth(Scale(280));
        if (this.DrawFightCombo("Fight", ref this.fightKey))
        {
            fight = data.GetFight(this.fightKey)!;
            this.calibrationKey = ImportPipeline.GuessCalibration(fight, doc.Title) ?? string.Empty;
        }

        ImGui.SameLine();
        ImGui.TextDisabled($"level {fight.SyncLevel.ToString(CultureInfo.InvariantCulture)}, {Plural(fight.Mechanics.Count, "mechanic")}");

        // Trial import of everything, for the tab and column lists (again when the fight changes).
        var key = fight.Key + "|" + this.sourceVersion.ToString(CultureInfo.InvariantCulture);
        if (this.analysisKey != key && !this.jobs.Busy)
            this.StartAnalysis(src, fight, key);

        ImGui.Spacing();
        var bottom = ImGui.GetFrameHeightWithSpacing() * 4 + Scale(16);
        using (var lists = ImRaii.Child("##optionLists", new Vector2(-1, -bottom), false))
        {
            if (lists)
            {
                this.DrawTabChoices(doc);
                ImGui.Spacing();
                this.DrawColumnChoices();
            }
        }

        ImGui.Separator();
        ImGui.SetNextItemWidth(Scale(420));
        if (ImGui.InputText("Plan name", ref this.planName, 200))
            this.planNameTouched = true;

        ImGui.SetNextItemWidth(Scale(260));
        var calibrationLabel = this.calibrationKey.Length == 0 ? "(automatic)" : this.calibrationKey;
        using (var combo = ImRaii.Combo("Calibration preset", calibrationLabel))
        {
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
                this.StartImport(src, fight);
        }

        ImGui.SameLine();
        if (!canImport)
        {
            ImGui.TextColored(ColorWarning, why);
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

    private (bool Ok, string Why) CanImport(Data.CoreData data)
    {
        if (data.Aliases == null)
            return (false, "The alias dictionary did not load (see Problems in /mitcue).");
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

    private void DrawTabChoices(SheetDocument doc)
    {
        ImGui.TextColored(ColorAccent, "Tabs to import");
        var a = this.analysis;
        if (a == null && this.jobs.Busy)
        {
            ImGui.TextDisabled("Looking at the tabs...");
        }
        else if (a == null)
        {
            ImGui.TextDisabled("The tabs have not been checked yet.");
            ImGui.SameLine();
            if (ImGui.SmallButton("Check the tabs"))
                this.analysisKey = null;
        }
        else if (!a.Available)
            ImGui.TextColoredWrapped(ColorWarning, a.Reason ?? "The tabs could not be analysed.");

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
        this.analysisKey = key;
        this.analysis = null;
        var data = this.plugin.Data;
        if (data.Aliases == null)
        {
            this.analysis = ImportAnalysis.Unavailable(src.Document, "The alias dictionary did not load, so the importer cannot run (see Problems in /mitcue).");
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
            "Looking at the tabs...",
            _ => ImportPipeline.Analyze(src.Document, options),
            result =>
            {
                if (!ReferenceEquals(this.source, src) || this.analysisKey != key)
                    return;
                this.analysis = result;
                this.ApplyDefaults(result);
            },
            ex => this.analysis = ImportAnalysis.Unavailable(src.Document, ImportPipeline.Explain(ex, "The sheet importer")));
    }

    private void ApplyDefaults(ImportAnalysis result)
    {
        var job = this.plugin.Frame.Job;
        var slot = this.plugin.Config.DefaultSlot;
        if (!this.tabsTouched)
        {
            this.selectedTabs.Clear();
            foreach (var name in result.DefaultTabs(job, slot, this.source?.LinkedTab))
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

        this.SetStatus(string.Empty, false);
        this.jobs.RunSync(
            "Importing...",
            _ => ImportPipeline.Import(doc, options),
            result =>
            {
                if (!ReferenceEquals(this.source, src))
                    return;
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
            var mine = model.CountFor(job, this.plugin.Config.DefaultSlot);
            ImGui.TextColored(mine > 0 ? ColorOk : ColorWarning, $"{Plural(mine, "entry applies", "entries apply")} to you as {job}" + (mine == 0 ? ": check the columns you imported." : "."));
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

    /// <summary>The earlier import of the same sheet, tabs and columns, if there is exactly one: replaced by default.</summary>
    private static string? DefaultReplacement(PlanStore store, MitPlan plan)
    {
        var columns = plan.Assignees.Select(a => a.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var same = store.All
            .Where(p => !p.IsReadOnly && string.Equals(p.Plan.Fight, plan.Fight, StringComparison.OrdinalIgnoreCase) && SameSource(p.Plan.Source, plan.Source))
            .Where(p => p.Plan.Source?.Tabs is { Count: > 0 } && plan.Source?.Tabs is { Count: > 0 })
            .Where(p => columns.SetEquals(p.Plan.Assignees.Select(a => a.Key)))
            .ToList();
        return same.Count == 1 ? same[0].Id : null;
    }

    private static bool SameSource(PlanSource? a, PlanSource? b)
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
        return ta.Count == 0 || tb.Count == 0 || ta.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(tb);
    }

    private void SavePlan(ReviewModel model, Job job)
    {
        var plan = model.Plan;
        var store = this.plugin.Plans;
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

        if (this.setActive && job != Job.None)
            store.SetActive(model.Fight.Key, job, plan.Id);

        var where = this.setActive && job != Job.None ? $", active for {model.Fight.Key.ToUpperInvariant()} as {job}" : string.Empty;
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
        this.analysisKey = null;
        this.selectedTabs.Clear();
        this.selectedColumns.Clear();
        this.replaceId = null;
        this.saveMessage = string.Empty;
        this.promptMessage = string.Empty;
        this.yamlBox.Set(string.Empty);
        this.SetStatus(string.Empty, false);
        this.step = Step.Source;
    }

    /// <summary>A multi-line text box over a persistent UTF-8 buffer (no re-encoding of a whole sheet every frame).</summary>
    private sealed class TextBox
    {
        private readonly int capacity;
        private byte[]? buffer;

        public TextBox(int capacity)
        {
            this.capacity = capacity;
        }

        public string Text { get; private set; } = string.Empty;

        public bool Truncated { get; private set; }

        /// <summary>Lines of <see cref="Text"/> (counted when it changes, not every frame).</summary>
        public int Lines { get; private set; }

        /// <summary>UTF-8 size of <see cref="Text"/>.</summary>
        public int Bytes { get; private set; }

        public void Set(string value)
        {
            var buf = this.Buffer();
            var bytes = Encoding.UTF8.GetBytes(value);
            var length = bytes.Length;
            this.Truncated = length >= this.capacity;
            if (this.Truncated)
            {
                length = this.capacity - 1;
                while (length > 0 && (bytes[length] & 0xC0) == 0x80)
                    length--; // do not cut a UTF-8 sequence
            }

            Array.Copy(bytes, buf, length);
            Array.Clear(buf, length, buf.Length - length);
            this.SetText(Encoding.UTF8.GetString(buf, 0, length), length);
        }

        public bool Draw(string id, Vector2 size, ImGuiInputTextFlags flags)
        {
            var buf = this.Buffer();
            if (!ImGui.InputTextMultiline(id, buf, size, flags))
                return false;
            var end = Array.IndexOf(buf, (byte)0);
            var length = end < 0 ? buf.Length : end;
            this.SetText(Encoding.UTF8.GetString(buf, 0, length), length);
            this.Truncated = false;
            return true;
        }

        private void SetText(string text, int bytes)
        {
            this.Text = text;
            this.Bytes = bytes;
            this.Lines = text.Length == 0 ? 0 : text.Count(c => c == '\n') + 1;
        }

        private byte[] Buffer() => this.buffer ??= new byte[this.capacity];
    }
}
