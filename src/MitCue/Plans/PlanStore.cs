using MitCue.Core;
using MitCue.Core.Data;
using MitCue.Core.Plans;
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
    private readonly DirectoryInfo configDirectory;
    private readonly Configuration config;
    private readonly Action saveConfig;
    private readonly ErrorReporter errors;
    private readonly List<StoredPlan> plans = [];

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

        this.plans.Sort((a, b) =>
        {
            var c = a.Origin.CompareTo(b.Origin) * -1; // User (1) before Bundled (0)
            if (c != 0)
                return c;
            c = string.Compare(a.Plan.Fight, b.Plan.Fight, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase);
        });
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

    /// <summary>Saves (adds or replaces) a user plan as JSON.</summary>
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

            this.plans.RemoveAll(p => p.Origin == PlanOrigin.User && string.Equals(p.Id, plan.Id, StringComparison.OrdinalIgnoreCase));
            this.plans.Insert(0, new StoredPlan(plan, PlanOrigin.User, path));
            this.Revision++;
            return true;
        }
        catch (Exception ex)
        {
            error = ErrorReporter.Describe(ex);
            this.errors.Report("plans.save", "Saving a plan", ex);
            return false;
        }
    }

    /// <summary>Deletes a user plan (bundled plans cannot be deleted) and forgets its settings.</summary>
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

        foreach (var key in this.config.ActivePlans.Where(kv => string.Equals(kv.Value, id, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList())
            this.config.ActivePlans.Remove(key);
        this.RemoveMappings(id);
        this.SaveConfig();

        // Reload so a bundled plan with the same id (if the user copy overrode it) comes back.
        this.Reload();
        return true;
    }

    /// <summary>Imports a plan from a share code and saves it as a user plan.</summary>
    public bool ImportShareCode(string code, out MitPlan? plan, out string message)
    {
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
        return true;
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
    /// The active plan for a fight and job: the user's choice when set, otherwise the first plan for the
    /// fight that has a column or entries for the job (<paramref name="automatic"/> = true). Null when there is
    /// none or the user chose "no plan".
    /// </summary>
    public StoredPlan? ActivePlanFor(string fightKey, Job job, out bool automatic)
    {
        automatic = false;
        if (this.config.ActivePlans.TryGetValue(ActiveKey(fightKey, job), out var id))
        {
            if (id.Length == 0)
                return null; // explicitly "no plan"

            var chosen = this.Get(id);
            if (chosen != null && string.Equals(chosen.Plan.Fight, fightKey, StringComparison.OrdinalIgnoreCase))
                return chosen;
        }

        automatic = true;
        foreach (var p in this.ForFight(fightKey))
        {
            if (JobsOf(p.Plan).Contains(job))
                return p;
        }

        return null;
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

        var suggested = SuggestAssignee(plan, job, this.config.DefaultSlot);
        return new PlayerMapping
        {
            AssigneeKey = suggested?.Key,
            Slot = suggested?.Slot ?? this.config.DefaultSlot,
            IsSuggestion = true,
        };
    }

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
    /// several match), else the assignee whose slot is the configured slot.
    /// </summary>
    public static Assignee? SuggestAssignee(MitPlan plan, Job job, string? slot)
    {
        Assignee? byJob = null;
        Assignee? bySlot = null;
        foreach (var a in plan.Assignees)
        {
            var slotMatches = slot != null && Slots.TryNormalize(a.Slot, out var s) && s == slot;
            if (a.Job == job && job != Job.None)
            {
                if (slotMatches)
                    return a;
                byJob ??= a;
            }
            else if (slotMatches)
            {
                bySlot ??= a;
            }
        }

        return byJob ?? bySlot;
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
        foreach (var e in plan.Entries)
        {
            e.Who ??= new WhoSpec();
            e.Do ??= [];
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
            }
            catch (Exception ex)
            {
                this.errors.Report("plans.load.user." + Path.GetFileName(file), $"Reading saved plan {Path.GetFileName(file)}", ex);
            }
        }
    }
}
