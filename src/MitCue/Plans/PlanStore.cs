using MitCue.Core;
using MitCue.Core.Data;
using MitCue.Core.Plans;
using MitCue.Import;
using MitCue.Util;

namespace MitCue.Plans;

/// <summary>Where a stored plan came from.</summary>
internal enum PlanOrigin
{
    /// <summary>Shipped inside the plugin (read-only).</summary>
    Bundled,

    /// <summary>Imported or saved by the user (ConfigDirectory/plans/&lt;id&gt;.json).</summary>
    User,
}

/// <summary>A plan known to the <see cref="PlanStore"/>.</summary>
internal sealed class StoredPlan
{
    /// <summary>Wraps a loaded plan.</summary>
    public StoredPlan(MitPlan plan, PlanOrigin origin, string location)
    {
        this.Plan = plan;
        this.Origin = origin;
        this.Location = location;
    }

    /// <summary>The plan itself.</summary>
    public MitPlan Plan { get; }

    /// <summary>Bundled or user plan.</summary>
    public PlanOrigin Origin { get; }

    /// <summary>File path (user plans) or embedded resource path (bundled plans).</summary>
    public string Location { get; }

    /// <summary>Plan id.</summary>
    public string Id => this.Plan.Id;

    /// <summary>Bundled plans cannot be deleted or overwritten in place.</summary>
    public bool IsReadOnly => this.Origin == PlanOrigin.Bundled;

    /// <summary>Name for lists and combos.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(this.Plan.Name) ? this.Plan.Id : this.Plan.Name;

    /// <summary>"bundled" / "user", plus the import source kind when known.</summary>
    public string SourceLabel
    {
        get
        {
            var origin = this.Origin == PlanOrigin.Bundled ? "bundled" : "user";
            return this.Plan.Source?.Kind is { Length: > 0 } kind ? $"{origin}, {kind}" : origin;
        }
    }
}

/// <summary>
/// User plans (JSON files in ConfigDirectory/plans) plus read-only bundled plans (embedded YAML),
/// the active plan per fight and job, and the "which column is me" mapping per plan and job.
/// Used from the framework thread and from Draw (both the game's main thread).
/// </summary>
internal sealed class PlanStore
{
    /// <summary>
    /// The bundled plan a job gets when the player has chosen none, keyed like <see cref="ActiveKey"/> (plan ids of
    /// plans/*.yaml). DESIGN D2: the White Mage (H1) follows "NoirMit - WHM" (made from Carry the Clear's White Mage
    /// tab) and the Scholar (H2) follows LPDU. Explicit, so a bundled plan added later cannot change what they get.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> BundledDefaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["top/WHM"] = "335b6c4f-3684-55e0-a8d0-3e78724ef200", // plans/top-noirmit-whm.yaml: NoirMit - WHM (the id it had as Carry the Clear – WHM)
        ["top/SCH"] = "1f75e1e8-7321-566c-ae86-8568019fe914", // plans/top-lpdu.yaml: LPDU TOP Mitigation
    };

    private readonly DirectoryInfo configDirectory;
    private readonly Configuration config;
    private readonly Action saveConfig;
    private readonly ErrorReporter errors;
    private readonly List<StoredPlan> plans = [];
    private readonly Dictionary<(string PlanId, Job Job, string? Slot), (MitPlan Plan, PlayerMapping Mapping)> suggestions = [];
    private int suggestionsRevision = -1;

    /// <summary>Creates the store; nothing is read until <see cref="EnsureLoaded"/>.</summary>
    public PlanStore(DirectoryInfo configDirectory, Configuration config, Action saveConfig, ErrorReporter errors)
    {
        this.configDirectory = configDirectory;
        this.config = config;
        this.saveConfig = saveConfig;
        this.errors = errors;
    }

    /// <summary>True after the first load.</summary>
    public bool Loaded { get; private set; }

    /// <summary>Incremented on every change to plans, active plans or mappings.</summary>
    public int Revision { get; private set; }

    /// <summary>
    /// Checks a user plan against its fight and the action catalog (the plugin sets <see cref="PlanChecks"/>), or null
    /// for no checks. Share codes and plan files in the plans folder reach the store without the import window's review:
    /// their problems go to the Problems list and the share-code message.
    /// </summary>
    public Func<MitPlan, IReadOnlyList<string>>? Check { get; set; }

    /// <summary>Folder holding user plans.</summary>
    public string PlansDirectory => Path.Combine(this.configDirectory.FullName, "plans");

    /// <summary>Every known plan (user plans first, then bundled; each group by fight and name).</summary>
    public IReadOnlyList<StoredPlan> All => this.plans;

    /// <summary>Config key for the active plan of a fight and job, e.g. "top/WHM".</summary>
    public static string ActiveKey(string fightKey, Job job) => $"{fightKey.ToLowerInvariant()}/{job}";

    /// <summary>Config key for the column choice of a plan and job, e.g. "6f1c…/SCH".</summary>
    public static string MappingKey(string planId, Job job) => $"{planId}/{job}";

    /// <summary>Loads plans on first use (framework thread).</summary>
    public void EnsureLoaded()
    {
        if (!this.Loaded)
            this.Reload();
    }

    /// <summary>Re-reads bundled and user plans.</summary>
    public void Reload()
    {
        this.Loaded = true;
        this.Revision++;
        this.plans.Clear();
        this.errors.ClearPrefix("plans.load");

        this.LoadBundled();
        this.LoadUser();
        this.SortPlans();
    }

    /// <summary>The plan with this id, or null.</summary>
    public StoredPlan? Get(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        foreach (var p in this.plans)
        {
            if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
                return p;
        }

        return null;
    }

    /// <summary>Plans for a fight key (e.g. "top").</summary>
    public IEnumerable<StoredPlan> ForFight(string fightKey) =>
        this.plans.Where(p => string.Equals(p.Plan.Fight, fightKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>Jobs mentioned by a plan's columns and entries.</summary>
    public static IEnumerable<Job> JobsOf(MitPlan plan)
    {
        var jobs = new HashSet<Job>();
        foreach (var a in plan.Assignees)
        {
            if (a.Job is { } j && j != Job.None)
                jobs.Add(j);
        }

        foreach (var e in plan.Entries)
        {
            if (e.Who?.Job is { } j && j != Job.None)
                jobs.Add(j);
        }

        return jobs;
    }

    /// <summary>
    /// Saves (adds or replaces) a user plan as JSON. A user plan with the id of a bundled plan (its share code imported)
    /// takes the bundled plan's place, as when the plans are loaded. The column choices saved for a plan it replaces
    /// follow their columns (<see cref="CarryOver"/>).
    /// </summary>
    public bool Save(MitPlan plan, out string? error)
    {
        error = null;
        try
        {
            Normalize(plan);
            var json = PlanSerializer.ToJson(plan);
            Directory.CreateDirectory(this.PlansDirectory);
            var path = Path.Combine(this.PlansDirectory, SafeFileName(plan.Id) + ".json");
            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);

            var replaced = this.plans.RemoveAll(p => string.Equals(p.Id, plan.Id, StringComparison.OrdinalIgnoreCase)) > 0;
            this.plans.Add(new StoredPlan(plan, PlanOrigin.User, path));
            this.SortPlans();
            this.errors.Clear(CheckKey(plan.Id)); // problems of the plan this one replaces (it is checked again on load)
            this.Revision++;
            if (replaced)
                this.CarryOverMappings(plan);
            return true;
        }
        catch (Exception ex)
        {
            error = ErrorReporter.Describe(ex);
            this.errors.Report("plans.save", "Saving a plan", ex);
            return false;
        }
    }

    /// <summary>
    /// Deletes a user plan (bundled plans cannot be deleted) and forgets its settings, unless a bundled plan with the
    /// same id comes back in its place (the user plan was a copy of it): the settings then stay with that plan.
    /// </summary>
    public bool Delete(string id, out string? error)
    {
        error = null;
        var stored = this.plans.FirstOrDefault(p => p.Origin == PlanOrigin.User && string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        if (stored == null)
        {
            error = "Only imported plans can be deleted.";
            return false;
        }

        try
        {
            if (File.Exists(stored.Location))
                File.Delete(stored.Location);
        }
        catch (Exception ex)
        {
            error = ErrorReporter.Describe(ex);
            this.errors.Report("plans.delete", "Deleting a plan", ex);
            return false;
        }

        // Reload so a bundled plan with the same id (if the user copy overrode it) comes back.
        this.Reload();
        if (this.Get(id) == null)
        {
            foreach (var key in this.config.ActivePlans.Where(kv => string.Equals(kv.Value, id, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList())
                this.config.ActivePlans.Remove(key);
            this.RemoveMappings(id);
            this.SaveConfig();
        }

        return true;
    }

    /// <summary>Imports a plan from a share code and saves it as a user plan (it becomes active only when chosen).</summary>
    public bool ImportShareCode(string code, out MitPlan? plan, out string message) =>
        this.ImportShareCode(code, Job.None, false, out plan, out message);

    /// <summary>
    /// Imports a plan from a share code and saves it as a user plan. With <paramref name="makeActive"/> it becomes the
    /// active plan for its fight as <paramref name="job"/> (the player's current job) when it has calls for that job;
    /// the message says which plan is active for that job afterwards.
    /// </summary>
    public bool ImportShareCode(string code, Job job, bool makeActive, out MitPlan? plan, out string message)
    {
        if (!this.ImportShareCodeCore(code, out plan, out message, out var problems))
            return false;

        if (job != Job.None)
        {
            var fightName = plan!.Fight.ToUpperInvariant();
            if (!JobsOf(plan).Contains(job))
            {
                message += $" It has no calls for {job}: choose it in the table above for the job it is for.";
            }
            else
            {
                if (makeActive)
                    this.SetActive(plan.Fight, job, plan.Id);
                var active = this.ActivePlanFor(plan.Fight, job);
                message += active?.Id == plan.Id
                    ? $" It is your active plan for {fightName} as {job}."
                    : $" Your active plan for {fightName} as {job} stays \"{active?.DisplayName ?? "none"}\": choose this one in the table above to use it.";
            }
        }

        if (problems.Count > 0)
            message += " " + PlanChecks.Summary(problems, max: 1) + " See Problems in /mitcue."; // each problem says what it does to the call
        return true;
    }

    private bool ImportShareCodeCore(string code, out MitPlan? plan, out string message, out IReadOnlyList<string> problems)
    {
        problems = [];
        plan = null;
        code = code.Trim();
        if (code.Length == 0)
        {
            message = "Paste a share code first.";
            return false;
        }

        try
        {
            plan = PlanSerializer.FromShareCode(code);
        }
        catch (NotImplementedException ex)
        {
            message = ErrorReporter.Describe(ex);
            return false;
        }
        catch (FormatException ex)
        {
            // PlanSerializer's messages are written for players ("not a share code", "made by a newer MitCue", "damaged").
            message = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            message = "That is not a valid MitCue share code (" + ErrorReporter.Describe(ex) + ").";
            return false;
        }

        if (string.IsNullOrWhiteSpace(plan.Fight))
        {
            message = "The share code has no fight; it cannot be used.";
            return false;
        }

        if (!this.Save(plan, out var error))
        {
            message = "Import failed: " + error;
            return false;
        }

        message = $"Imported \"{plan.Name}\" ({plan.Entries.Count} entries, fight {plan.Fight}).";
        problems = this.CheckUserPlan(plan, $"share code \"{plan.Name}\"");
        return true;
    }

    /// <summary>
    /// Runs <see cref="Check"/> on a user plan and lists its problems as one warning in the Problems list (cleared on
    /// reload and when the plan is deleted). Never throws.
    /// </summary>
    private IReadOnlyList<string> CheckUserPlan(MitPlan plan, string what)
    {
        var key = CheckKey(plan.Id);
        this.errors.Clear(key);
        if (this.Check is not { } check)
            return [];
        try
        {
            var problems = check(plan);
            if (problems.Count > 0)
                this.errors.Warn(key, $"Checking the plan \"{plan.Name}\" ({what})", PlanChecks.Summary(problems, max: 5));
            return problems;
        }
        catch (Exception ex)
        {
            this.errors.Report(key, $"Checking the plan \"{plan.Name}\" ({what})", ex);
            return [];
        }
    }

    /// <summary>The share code of a plan, or null with an error message.</summary>
    public string? ExportShareCode(MitPlan plan, out string? error)
    {
        error = null;
        try
        {
            return PlanSerializer.ToShareCode(plan);
        }
        catch (Exception ex)
        {
            error = ErrorReporter.Describe(ex);
            this.errors.Report("plans.export", "Creating a share code", ex);
            return null;
        }
    }

    /// <summary>
    /// The active plan for a fight and job: the user's choice when set, otherwise (<paramref name="automatic"/> = true)
    /// the bundled plan meant for the job (<see cref="BundledDefaults"/>), else the first other bundled plan with a column
    /// or entries for the job, else the first of the user's own plans for it. Null when there is none or the user chose
    /// "no plan".
    /// </summary>
    /// <remarks>
    /// A plan the user imports (or receives as a share code) replaces a bundled plan only when they choose it: the import
    /// window's "Make it my active plan" and the share-code import's checkbox do that, and so does the Plans tab. So a
    /// White Mage who imports the co-healer's LPDU share code to look at it keeps NoirMit - WHM. Where no bundled plan
    /// covers the job (TEA and DMU ship none), the user's own plan is the automatic one.
    /// </remarks>
    public StoredPlan? ActivePlanFor(string fightKey, Job job, out bool automatic)
    {
        automatic = false;
        var key = ActiveKey(fightKey, job);
        if (this.config.ActivePlans.TryGetValue(key, out var id))
        {
            if (id.Length == 0)
                return null; // explicitly "no plan"

            var chosen = this.Get(id);
            if (chosen != null && string.Equals(chosen.Plan.Fight, fightKey, StringComparison.OrdinalIgnoreCase))
                return chosen;
        }

        automatic = true;
        var candidates = this.ForFight(fightKey).Where(p => JobsOf(p.Plan).Contains(job)).ToList();

        // By id: a share code of a bundled plan replaces it in the list (same id) and stays the plan meant for the job.
        if (BundledDefaults.TryGetValue(key, out var defaultId) && candidates.FirstOrDefault(p => string.Equals(p.Id, defaultId, StringComparison.OrdinalIgnoreCase)) is { } recommended)
            return recommended;
        return candidates.FirstOrDefault(p => p.Origin == PlanOrigin.Bundled) ?? candidates.FirstOrDefault();
    }

    /// <summary>The active plan for a fight and job (see <see cref="ActivePlanFor(string, Job, out bool)"/>).</summary>
    public StoredPlan? ActivePlanFor(string fightKey, Job job) => this.ActivePlanFor(fightKey, job, out _);

    /// <summary>Sets the active plan: an id, <c>""</c> for "no plan", or null for automatic.</summary>
    public void SetActive(string fightKey, Job job, string? planId)
    {
        var key = ActiveKey(fightKey, job);
        if (planId == null)
            this.config.ActivePlans.Remove(key);
        else
            this.config.ActivePlans[key] = planId;
        this.SaveConfig();
    }

    /// <summary>
    /// The user's saved column choice for a plan when playing <paramref name="job"/>, or null. A choice saved by an
    /// older version for the whole plan still counts, unless its column belongs to another job.
    /// </summary>
    public PlayerMapping? SavedMapping(MitPlan plan, Job job)
    {
        var mappings = this.config.PlayerMappings;
        if (mappings.TryGetValue(MappingKey(plan.Id, job), out var saved))
            return saved;
        return mappings.TryGetValue(plan.Id, out var legacy) && Fits(plan, legacy, job) ? legacy : null;
    }

    /// <summary>The saved mapping for this job, or the automatic suggestion (<see cref="PlayerMapping.IsSuggestion"/> = true).</summary>
    public PlayerMapping MappingFor(MitPlan plan, Job job)
    {
        if (this.SavedMapping(plan, job) is { } saved)
            return saved;

        // The suggestion reads every entry of the plan a few times and the Plans tab asks for it every frame: cache it
        // until the plans change (stored plans are replaced, not edited in place) or the configured slot does.
        if (this.suggestionsRevision != this.Revision)
        {
            this.suggestions.Clear();
            this.suggestionsRevision = this.Revision;
        }

        var key = (plan.Id, job, this.config.DefaultSlot);
        if (!this.suggestions.TryGetValue(key, out var suggestion) || !ReferenceEquals(suggestion.Plan, plan))
        {
            suggestion = (plan, SuggestedMapping(plan, job, this.config.DefaultSlot));
            this.suggestions[key] = suggestion;
        }

        return new PlayerMapping { AssigneeKey = suggestion.Mapping.AssigneeKey, Slot = suggestion.Mapping.Slot, IsSuggestion = true };
    }

    /// <summary>
    /// The automatic column choice for a plan and job (<see cref="PlayerMapping.IsSuggestion"/> = true): what
    /// <see cref="MappingFor"/> returns when nothing is saved (<see cref="ColumnSuggestion.Suggest"/>). It is the job's
    /// column (<see cref="ColumnMapping"/>), unless that leaves out the job-less calls of the player's slot (LPDU's "H2"
    /// calls for a Scholar); then the narrowest choice that has them too and no call of another column, the same one an
    /// import saves. Other columns of the job are alternatives (LPDU's tank pairs), never added to it.
    /// </summary>
    public static PlayerMapping SuggestedMapping(MitPlan plan, Job job, string? defaultSlot) =>
        Suggestion(ColumnSuggestion.Suggest(plan, job, defaultSlot));

    /// <summary>
    /// The job's own column as a mapping (<see cref="PlayerMapping.IsSuggestion"/> = true), from
    /// <see cref="SuggestAssignee"/>; <see cref="SuggestedMapping"/> starts from it.
    /// </summary>
    public static PlayerMapping ColumnMapping(MitPlan plan, Job job, string? defaultSlot) =>
        Suggestion(ColumnSuggestion.ForJob(plan, job, defaultSlot));

    /// <summary>A column choice of the core as a mapping (<see cref="PlayerMapping.IsSuggestion"/> = true).</summary>
    public static PlayerMapping Suggestion(ColumnPick pick) => new() { AssigneeKey = pick.AssigneeKey, Slot = pick.Slot, IsSuggestion = true };

    /// <summary>A mapping as a column choice of the core.</summary>
    public static ColumnPick Pick(PlayerMapping mapping) => new(mapping.AssigneeKey, mapping.Slot);

    /// <summary>
    /// Saves the column choice for a plan and job (null = back to the automatic suggestion). The other jobs keep
    /// theirs: a whole-plan choice saved by an older version is first copied to each of the plan's jobs it fits.
    /// </summary>
    public void SetMapping(MitPlan plan, Job job, PlayerMapping? mapping)
    {
        var mappings = this.config.PlayerMappings;
        if (mappings.Remove(plan.Id, out var legacy))
        {
            foreach (var other in JobsOf(plan))
            {
                if (other != job && Fits(plan, legacy, other))
                    mappings.TryAdd(MappingKey(plan.Id, other), Copy(legacy));
            }
        }

        if (mapping == null)
            mappings.Remove(MappingKey(plan.Id, job));
        else
            mappings[MappingKey(plan.Id, job)] = Copy(mapping);
        this.SaveConfig();
    }

    /// <summary>
    /// Suggests "my column": the assignee whose job is the current job (preferring the configured slot when
    /// several match), else the assignee whose slot is the configured slot (<see cref="ColumnSuggestion.JobColumn"/>).
    /// </summary>
    public static Assignee? SuggestAssignee(MitPlan plan, Job job, string? slot) => ColumnSuggestion.JobColumn(plan, job, slot);

    /// <summary>
    /// A column choice made for an earlier version of <paramref name="plan"/> (a re-import that replaces it), with its
    /// column's key as <paramref name="plan"/> names it when the key changed only by the characters an xlsx export removes
    /// from tab names: an import that kept the export's tab names has LPDU's "WARDRK WAR", one that restored Google's has
    /// "WAR/DRK WAR". The same mapping otherwise (also when it is null or its column is gone).
    /// </summary>
    public static PlayerMapping? CarryOver(MitPlan plan, PlayerMapping? mapping)
    {
        if (mapping?.AssigneeKey is not { } key || plan.Assignees.Exists(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase)))
            return mapping;
        var form = ImportPipeline.WithoutExcelInvalidCharacters(key);
        var same = plan.Assignees.Where(a => ImportPipeline.WithoutExcelInvalidCharacters(a.Key) == form).ToList();
        return same.Count == 1 ? new PlayerMapping { AssigneeKey = same[0].Key, Slot = mapping.Slot, IsSuggestion = mapping.IsSuggestion } : mapping;
    }

    /// <summary>The saved column choices of a plan (every job, and an older whole-plan one) after it was replaced: <see cref="CarryOver"/>.</summary>
    private void CarryOverMappings(MitPlan plan)
    {
        var prefix = plan.Id + "/";
        var changed = false;
        foreach (var key in this.config.PlayerMappings.Keys.Where(k => string.Equals(k, plan.Id, StringComparison.OrdinalIgnoreCase) || k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            var mapping = this.config.PlayerMappings[key];
            if (CarryOver(plan, mapping) is { } carried && !ReferenceEquals(carried, mapping))
            {
                this.config.PlayerMappings[key] = Copy(carried);
                changed = true;
            }
        }

        if (changed)
            this.SaveConfig();
    }

    /// <summary>A mapping can be <paramref name="job"/>'s unless its column belongs to another job.</summary>
    private static bool Fits(MitPlan plan, PlayerMapping mapping, Job job)
    {
        if (mapping.AssigneeKey == null)
            return true;
        var column = plan.Assignees.FirstOrDefault(a => string.Equals(a.Key, mapping.AssigneeKey, StringComparison.OrdinalIgnoreCase));
        return column?.Job is not { } columnJob || columnJob == Job.None || columnJob == job;
    }

    private static PlayerMapping Copy(PlayerMapping mapping) => new() { AssigneeKey = mapping.AssigneeKey, Slot = mapping.Slot };

    /// <summary>The Problems-list key of a user plan's check results (under "plans.load", so a reload clears them).</summary>
    private static string CheckKey(string planId) => "plans.load.check." + planId;

    /// <summary>User plans first, then bundled plans; each group by fight and name.</summary>
    private void SortPlans() =>
        this.plans.Sort((a, b) =>
        {
            var c = a.Origin.CompareTo(b.Origin) * -1; // User (1) before Bundled (0)
            if (c != 0)
                return c;
            c = string.Compare(a.Plan.Fight, b.Plan.Fight, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase);
        });

    /// <summary>
    /// Repairs a plan read from text or a share code, and writes slot synonyms ("D3", "T1") as the standard slot names:
    /// <see cref="PlanSelection"/> compares slots by name, and the configured slot and a column picked in /mitcue > Plans
    /// are standard names, so an entry for "D3" would never be shown to the R1 player (the plan checks do not flag it).
    /// </summary>
    private static void Normalize(MitPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.Id))
            plan.Id = Guid.NewGuid().ToString("D");
        plan.Name ??= string.Empty;
        plan.Fight ??= string.Empty;
        plan.Assignees ??= [];
        plan.Entries ??= [];
        plan.Assignees.RemoveAll(a => a == null);
        plan.Entries.RemoveAll(e => e == null);
        foreach (var a in plan.Assignees)
        {
            if (Slots.TryNormalize(a.Slot, out var slot))
                a.Slot = slot;
        }

        foreach (var e in plan.Entries)
        {
            e.Who ??= new WhoSpec();
            e.Do ??= [];
            if (Slots.TryNormalize(e.Who.Slot, out var slot))
                e.Who.Slot = slot;
        }
    }

    private static string SafeFileName(string id)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = id.Select(c => invalid.Contains(c) || c == '.' ? '_' : c).ToArray();
        var name = new string(chars).Trim();
        return name.Length == 0 ? Guid.NewGuid().ToString("N") : name;
    }

    private void SaveConfig()
    {
        this.Revision++;
        this.saveConfig();
    }

    /// <summary>Forgets every column choice of a plan (all jobs, and an older whole-plan one).</summary>
    private void RemoveMappings(string planId)
    {
        var prefix = planId + "/";
        foreach (var key in this.config.PlayerMappings.Keys.Where(k => string.Equals(k, planId, StringComparison.OrdinalIgnoreCase) || k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            this.config.PlayerMappings.Remove(key);
    }

    private void LoadBundled()
    {
        EmbeddedDataSource source;
        List<string> paths;
        try
        {
            source = EmbeddedDataSource.Plans();
            paths = source.List(string.Empty).Where(p => p.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)).ToList();
        }
        catch (Exception ex)
        {
            this.errors.Report("plans.load.bundled", "Listing bundled plans", ex);
            return;
        }

        foreach (var path in paths)
        {
            try
            {
                var plan = PlanSerializer.FromYaml(source.ReadText(path));
                Normalize(plan);
                if (this.Get(plan.Id) == null)
                    this.plans.Add(new StoredPlan(plan, PlanOrigin.Bundled, path));
            }
            catch (Exception ex)
            {
                this.errors.Report("plans.load.bundled." + path, $"Reading bundled plan {path}", ex);
            }
        }
    }

    private void LoadUser()
    {
        string[] files;
        try
        {
            files = Directory.Exists(this.PlansDirectory) ? Directory.GetFiles(this.PlansDirectory, "*.json") : [];
        }
        catch (Exception ex)
        {
            this.errors.Report("plans.load.user", "Listing saved plans", ex);
            return;
        }

        Array.Sort(files, StringComparer.Ordinal);
        foreach (var file in files)
        {
            try
            {
                var plan = PlanSerializer.FromJson(File.ReadAllText(file));
                Normalize(plan);

                // A user plan overrides a bundled plan with the same id; duplicate user ids keep the first file.
                var existing = this.Get(plan.Id);
                if (existing is { Origin: PlanOrigin.User })
                {
                    this.errors.Warn("plans.load.dup." + plan.Id, "Reading saved plans", $"{Path.GetFileName(file)} has the same plan id as {Path.GetFileName(existing.Location)}; ignored.");
                    continue;
                }

                if (existing != null)
                    this.plans.Remove(existing);
                this.plans.Add(new StoredPlan(plan, PlanOrigin.User, file));
                this.CheckUserPlan(plan, Path.GetFileName(file));
            }
            catch (Exception ex)
            {
                this.errors.Report("plans.load.user." + Path.GetFileName(file), $"Reading saved plan {Path.GetFileName(file)}", ex);
            }
        }
    }
}
