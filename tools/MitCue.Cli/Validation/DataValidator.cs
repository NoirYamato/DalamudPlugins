using System.Globalization;
using MitCue.Core;
using MitCue.Core.Actions;
using MitCue.Core.Data;
using MitCue.Core.Fights;
using MitCue.Core.Plans;
using MitCue.Core.Timelines;
using YamlDotNet.RepresentationModel;

namespace MitCue.Cli.Validation;

/// <summary>One checked file (or group): problems make validation fail, warnings do not.</summary>
internal sealed class CheckedItem(string name)
{
    public string Name { get; } = name;

    /// <summary>What was found, e.g. "6 phases, 150 mechanics".</summary>
    public string Summary { get; set; } = string.Empty;

    public List<string> Problems { get; } = [];

    public List<string> Warnings { get; } = [];
}

/// <summary>A section of the report: fights, timelines, actions, aliases, plans.</summary>
internal sealed class CheckedSection(string title)
{
    public string Title { get; } = title;

    public List<CheckedItem> Items { get; } = [];

    /// <summary>Shown when the section has no items.</summary>
    public string? EmptyNote { get; set; }
}

/// <summary>
/// Loads every data file through the real loaders and runs the consistency checks of the data tests
/// (tests/MitCue.Core.Tests/Data: FightDataTests, ActionCatalogTests, AliasDictionaryTests; Timelines/BundledTimelineTests):
/// fight files against their timelines, the action knowledge base against itself, aliases against the actions, and
/// the embedded copy against the folder. Plans in a plans/ folder next to data/ are read and checked as well.
/// </summary>
internal sealed class DataValidator(IDataSource data)
{
    /// <summary>How far (seconds) a mechanic's hit may be from its timeline entry (as in FightDataTests).</summary>
    public const double EntryTolerance = 0.05;

    private static readonly int[] SyncLevels = [80, 90, 100];

    private readonly Dictionary<string, Timeline> timelines = new(StringComparer.Ordinal);
    private ActionCatalog? catalog;

    public List<CheckedSection> Sections { get; } = [];

    public int ProblemCount => this.Sections.Sum(s => s.Items.Sum(i => i.Problems.Count));

    public int WarningCount => this.Sections.Sum(s => s.Items.Sum(i => i.Warnings.Count));

    public int FileCount => this.Sections.Sum(s => s.Items.Count);

    public DataValidator Run()
    {
        var timelineSection = this.CheckTimelines();
        this.Sections.Add(this.CheckFights());
        this.Sections.Add(timelineSection);
        this.Sections.Add(this.CheckActions());
        this.Sections.Add(this.CheckAliases());
        this.Sections.Add(this.CheckPlans());
        return this;
    }

    private static string Inv(double value) => value.ToString(CultureInfo.InvariantCulture);

    private CheckedSection CheckTimelines()
    {
        var section = new CheckedSection("Timelines") { EmptyNote = "no timelines/*.txt" };
        foreach (var path in data.List("timelines").Where(p => p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
        {
            var item = new CheckedItem(path);
            section.Items.Add(item);
            try
            {
                var timeline = TimelineParser.Parse(data.ReadText(path), Path.GetFileName(path));
                this.timelines[path.StartsWith("timelines/", StringComparison.Ordinal) ? path["timelines/".Length..] : path] = timeline;
                item.Summary = $"{timeline.Entries.Count} entries, {timeline.Entries.Count(e => e.Sync != null)} syncs, {timeline.Labels.Count} labels";
                item.Warnings.AddRange(timeline.Warnings);
                var unresolved = timeline.Entries.Where(e => e.JumpLabel != null && e.JumpTime == null).ToList();
                foreach (var e in unresolved)
                    item.Problems.Add($"line {e.Line}: jump to unknown label \"{e.JumpLabel}\"");
            }
            catch (FormatException ex)
            {
                item.Problems.Add(ex.Message);
            }
        }

        return section;
    }

    private CheckedSection CheckFights()
    {
        var section = new CheckedSection("Fights") { EmptyNote = "no fights/*.yaml" };
        var loaded = new List<(CheckedItem Item, FightDefinition Fight, string Path)>();
        foreach (var path in data.List("fights").Where(p => p.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)))
        {
            var item = new CheckedItem(path);
            section.Items.Add(item);
            try
            {
                var fight = FightLoader.Load(data.ReadText(path), path);
                loaded.Add((item, fight, path));
            }
            catch (FormatException ex)
            {
                item.Problems.Add(ex.Message);
            }
        }

        foreach (var group in loaded.GroupBy(f => f.Fight.Key.ToLowerInvariant()).Where(g => g.Count() > 1))
        {
            foreach (var (item, _, _) in group)
                item.Problems.Add($"fight key '{group.Key}' is used by {group.Count()} files");
        }

        foreach (var group in loaded.GroupBy(f => f.Fight.Territory).Where(g => g.Count() > 1))
        {
            foreach (var (item, fight, _) in group)
                item.Problems.Add($"territory {fight.Territory} is used by {group.Count()} fights");
        }

        foreach (var (item, fight, path) in loaded)
            this.CheckFight(item, fight, path);

        // The plugin uses the copy embedded in MitCue.Core: it must match the folder (rebuild after editing data/).
        if (data is DirectoryDataSource && loaded.Count > 0)
        {
            try
            {
                var embedded = new FightLibrary(new EmbeddedDataSource()).Fights.Select(f => f.Key).Order(StringComparer.Ordinal).ToList();
                var here = loaded.Select(f => f.Fight.Key).Order(StringComparer.Ordinal).ToList();
                if (!embedded.SequenceEqual(here, StringComparer.OrdinalIgnoreCase))
                    loaded[0].Item.Warnings.Add($"the copy built into MitCue.Core has fights {string.Join(", ", embedded)}, the folder {string.Join(", ", here)}: rebuild");
            }
            catch (FormatException ex)
            {
                loaded[0].Item.Warnings.Add("the copy built into MitCue.Core does not load: " + ex.Message);
            }
        }

        return section;
    }

    private void CheckFight(CheckedItem item, FightDefinition fight, string path)
    {
        item.Summary = $"{fight.Key}, {fight.Name}: level {fight.SyncLevel}, {fight.Phases.Count} phases, {fight.Mechanics.Count} mechanics";
        if (!string.Equals(Path.GetFileNameWithoutExtension(path), fight.Key, StringComparison.OrdinalIgnoreCase))
            item.Problems.Add($"fight key '{fight.Key}' does not match the file name");
        if (fight.SyncLevel is not (80 or 90 or 100))
            item.Problems.Add($"sync level {fight.SyncLevel} is not an ultimate's (80, 90 or 100)");

        if (fight.Phases.Count == 0)
            item.Problems.Add("no phases");
        if (!fight.Phases.Zip(fight.Phases.Skip(1)).All(p => p.First.Start < p.Second.Start))
            item.Problems.Add("phases are not sorted by start");
        foreach (var m in fight.Mechanics)
        {
            if (fight.GetPhase(m.Phase) == null)
                item.Problems.Add($"{m.Id} is in unknown phase {m.Phase}");
            if (!m.Id.StartsWith(m.Phase + ".", StringComparison.OrdinalIgnoreCase))
                item.Problems.Add($"{m.Id} is not prefixed with its phase {m.Phase}");
            if (m.Hits.Count == 0)
                item.Problems.Add($"{m.Id} has no hits");
            else if (m.Hits[0] != m.Hit || !m.Hits.Zip(m.Hits.Skip(1)).All(p => p.First <= p.Second))
                item.Problems.Add($"{m.Id}: hits must be in time order and start with hit {Inv(m.Hit)}");
        }

        foreach (var (sheet, zeros) in fight.Calibration)
        {
            foreach (var phase in zeros.Keys.Where(p => fight.GetPhase(p) == null))
                item.Problems.Add($"calibration '{sheet}' names unknown phase '{phase}'");
        }

        if (!this.timelines.TryGetValue(fight.TimelineFile, out var timeline))
        {
            item.Problems.Add(data.Exists("timelines/" + fight.TimelineFile)
                ? $"timeline {fight.TimelineFile} does not parse (see Timelines)"
                : $"timeline {fight.TimelineFile} is missing");
            return;
        }

        var checkedEntries = 0;
        foreach (var m in fight.Mechanics.Where(m => m.Entry != null))
        {
            checkedEntries++;
            var named = timeline.Entries.Where(e => e.Name == m.Entry).ToList();
            if (named.Any(e => Math.Abs(e.Time - m.Hit) <= EntryTolerance))
                continue;
            item.Problems.Add(named.Count > 0
                ? $"{m.Id} expects \"{m.Entry}\" at {Inv(m.Hit)}; the timeline has it at {string.Join(", ", named.Select(e => Inv(e.Time)))}"
                : $"{m.Id} expects \"{m.Entry}\" at {Inv(m.Hit)}; the timeline has no such entry");
        }

        item.Summary += $"; {checkedEntries} entries checked against {fight.TimelineFile}";
    }

    private CheckedSection CheckActions()
    {
        var section = new CheckedSection("Actions");
        var item = new CheckedItem(CliContext.ActionsPath);
        section.Items.Add(item);
        if (!data.Exists(CliContext.ActionsPath))
        {
            item.Problems.Add("missing");
            return section;
        }

        var text = data.ReadText(CliContext.ActionsPath);
        try
        {
            this.catalog = ActionCatalog.Load(text);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            item.Problems.Add(ex.Message);
            return section;
        }

        var catalog = this.catalog;
        var all = catalog.All.ToList();
        item.Summary = $"{all.Count} actions (game version {catalog.GameVersion ?? "?"})";

        foreach (var g in all.Where(a => !a.IsFollowUp).GroupBy(a => AliasDictionary.Normalize(a.Name)).Where(g => g.Count() > 1))
            item.Problems.Add($"name '{g.Key}' is used by {string.Join(", ", g.Select(a => a.Id))} (names must be unique among non-follow-up rows)");

        foreach (var a in all)
        {
            if (a.Jobs.Count == 0 || a.Jobs.Contains(Job.None))
                item.Problems.Add($"{a}: no jobs");
            if (a.Target == ActionTarget.Unknown)
                item.Problems.Add($"{a}: no target");
            if (a.Icon == 0)
                item.Problems.Add($"{a}: no icon");
            if ((a.Kind == ActionKind.LimitBreak) != (a.LimitBreakTier != null))
                item.Problems.Add($"{a}: limit break kind and tier disagree");

            if (a.ReplacedBy is { } up)
            {
                var upgrade = catalog.Get(up);
                if (upgrade == null)
                    item.Problems.Add($"{a}: replaced_by {up} is not in the catalog");
                else if (upgrade.UpgradeOf != a.Id || upgrade.Level != a.ReplacedAtLevel || !upgrade.Jobs.SequenceEqual(a.Jobs))
                    item.Problems.Add($"{a}: replaced_by {upgrade} does not point back (upgrade_of, level, jobs)");
            }

            if (a.UpgradeOf is { } down && catalog.Get(down)?.ReplacedBy != a.Id)
                item.Problems.Add($"{a}: upgrade_of {down} does not point back (replaced_by)");
            if (a.FollowUpOf is { } trigger && catalog.Get(trigger) == null)
                item.Problems.Add($"{a}: follow_up_of {trigger} is not in the catalog");
            foreach (var f in a.FollowUps)
            {
                var target = catalog.Get(f.Id);
                if (target == null || target.Name != f.Name)
                    item.Problems.Add($"{a}: follow-up {f.Id} {f.Name} is not in the catalog under that name");
            }
        }

        this.CheckSyncMap(item, text, catalog);

        if (data is DirectoryDataSource)
        {
            try
            {
                var embedded = ActionCatalog.Load(new EmbeddedDataSource().ReadText(CliContext.ActionsPath));
                if (!embedded.All.Select(a => (a.Id, a.Name)).SequenceEqual(all.Select(a => (a.Id, a.Name))))
                    item.Warnings.Add("the copy built into MitCue.Core differs from the folder: rebuild");
            }
            catch (Exception ex) when (ex is FormatException or FileNotFoundException)
            {
                item.Warnings.Add("the copy built into MitCue.Core does not load: " + ex.Message);
            }
        }

        return section;
    }

    /// <summary>The YAML's <c>sync</c> map (duration at 80/90/100, null = not usable) must agree with the catalog.</summary>
    private void CheckSyncMap(CheckedItem item, string text, ActionCatalog catalog)
    {
        YamlMappingNode root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            root = (YamlMappingNode)stream.Documents[0].RootNode;
        }
        catch (Exception ex)
        {
            item.Problems.Add("could not read the sync maps: " + ex.Message);
            return;
        }

        if (!root.Children.TryGetValue(new YamlScalarNode("actions"), out var list) || list is not YamlSequenceNode actions)
            return;
        foreach (var node in actions.Children.OfType<YamlMappingNode>())
        {
            if (!node.Children.TryGetValue(new YamlScalarNode("id"), out var idNode) || idNode is not YamlScalarNode { Value: { } idText }
                || !uint.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || catalog.Get(id) is not { } action)
            {
                continue;
            }

            if (!node.Children.TryGetValue(new YamlScalarNode("sync"), out var syncNode) || syncNode is not YamlMappingNode sync)
            {
                item.Problems.Add($"{action}: no sync map");
                continue;
            }

            foreach (var level in SyncLevels)
            {
                if (!sync.Children.TryGetValue(new YamlScalarNode(level.ToString(CultureInfo.InvariantCulture)), out var valueNode) || valueNode is not YamlScalarNode scalar)
                {
                    item.Problems.Add($"{action}: sync has no {level}");
                    continue;
                }

                var isNull = scalar.Value is null or "" or "~" or "null";
                if (isNull)
                {
                    if (action.AvailableAt(level))
                        item.Problems.Add($"{action} is usable at {level} but sync says null");
                }
                else if (!action.AvailableAt(level))
                {
                    item.Problems.Add($"{action} is not usable at {level} but sync says {scalar.Value}");
                }
                else if (!double.TryParse(scalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var expected) || Math.Abs((action.DurationAt(level) ?? 0) - expected) > 1e-9)
                {
                    item.Problems.Add($"{action} lasts {Inv(action.DurationAt(level) ?? 0)} s at {level} but sync says {scalar.Value}");
                }
            }
        }
    }

    private CheckedSection CheckAliases()
    {
        var section = new CheckedSection("Aliases");
        var item = new CheckedItem(CliContext.AliasesPath);
        section.Items.Add(item);
        if (!data.Exists(CliContext.AliasesPath))
        {
            item.Problems.Add("missing");
            return section;
        }

        var text = data.ReadText(CliContext.AliasesPath);
        AliasDictionary aliases;
        try
        {
            aliases = AliasDictionary.Load(text, this.catalog);
        }
        catch (FormatException ex)
        {
            item.Problems.Add(ex.Message);
            return section;
        }

        var keys = aliases.Keys.ToList();
        item.Summary = $"{keys.Count} keys";
        if (this.catalog is not { } catalog)
        {
            item.Warnings.Add("not checked against the actions (actions.yaml did not load)");
            return section;
        }

        item.Problems.AddRange(aliases.Validate(catalog));

        foreach (var key in keys)
        {
            var applies = false;
            foreach (var job in Jobs.All)
            {
                var expansions = aliases.Lookup(key, job);
                applies |= expansions.Count > 0;
                foreach (var expansion in expansions.Where(e => !e.NoteOnly))
                {
                    foreach (var name in expansion.Actions.Concat(expansion.Targeted))
                    {
                        foreach (var level in SyncLevels)
                        {
                            var action = catalog.FindAtLevel(name, level, job);
                            if (action == null)
                                item.Problems.Add($"{key} ({job}): '{name}' is unknown");
                            else if (!action.UsableBy(job))
                                item.Problems.Add($"{key} ({job}): {action} is not a {job} action");
                            else if (!action.AvailableAt(level) && (level == 100 || action.Level <= level))
                                item.Problems.Add($"{key} ({job}) at {level}: {action} is not usable");
                        }
                    }
                }
            }

            if (!applies)
                item.Problems.Add($"alias '{key}' applies to no job");
        }

        // Keys in the file must already be normalized (the loader normalizes, so a key that is not would never match as written).
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            if (stream.Documents[0].RootNode is YamlMappingNode root
                && root.Children.TryGetValue(new YamlScalarNode("aliases"), out var node) && node is YamlMappingNode map)
            {
                foreach (var raw in map.Children.Keys.OfType<YamlScalarNode>().Select(k => k.Value ?? string.Empty))
                {
                    if (AliasDictionary.Normalize(raw) != raw)
                        item.Problems.Add($"key '{raw}' is not normalized (write '{AliasDictionary.Normalize(raw)}')");
                }
            }
        }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or InvalidCastException)
        {
            item.Problems.Add("could not read the raw keys: " + ex.Message);
        }

        item.Summary += item.Problems.Count == 0 ? "; every alias resolves for its jobs at levels 80, 90 and 100" : string.Empty;

        if (data is DirectoryDataSource)
        {
            try
            {
                var embedded = AliasDictionary.Load(new EmbeddedDataSource().ReadText(CliContext.AliasesPath));
                if (!embedded.Keys.SequenceEqual(keys))
                    item.Warnings.Add("the copy built into MitCue.Core differs from the folder: rebuild");
            }
            catch (Exception ex) when (ex is FormatException or FileNotFoundException)
            {
                item.Warnings.Add("the copy built into MitCue.Core does not load: " + ex.Message);
            }
        }

        return section;
    }

    /// <summary>
    /// Plans shipped with the plugin (plans/*.yaml next to data/): readable, for a known fight, and referring only to
    /// mechanics and actions that exist and that each column's job can use at the sync level.
    /// </summary>
    private CheckedSection CheckPlans()
    {
        var section = new CheckedSection("Plans");
        var folder = data is DirectoryDataSource dir ? Path.Combine(Path.GetDirectoryName(dir.Root) ?? dir.Root, "plans") : null;
        if (folder == null || !Directory.Exists(folder))
        {
            section.EmptyNote = folder == null ? "not checked (built-in data)" : "none (no plans/ folder)";
            return section;
        }

        section.EmptyNote = "none (plans/ is empty)";
        var fights = new Dictionary<string, FightDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in data.List("fights").Where(p => p.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var fight = FightLoader.Load(data.ReadText(path), path);
                fights.TryAdd(fight.Key, fight);
            }
            catch (FormatException)
            {
                // reported under Fights
            }
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.yaml", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var item = new CheckedItem("plans/" + Path.GetRelativePath(folder, file).Replace('\\', '/'));
            section.Items.Add(item);
            MitPlan plan;
            try
            {
                plan = PlanSerializer.FromYaml(File.ReadAllText(file));
            }
            catch (FormatException ex)
            {
                item.Problems.Add(ex.Message);
                continue;
            }

            item.Summary = $"\"{plan.Name}\" ({plan.Fight}): {plan.Entries.Count} entries";
            if (!fights.TryGetValue(plan.Fight, out var planFight))
            {
                item.Problems.Add($"unknown fight '{plan.Fight}'");
                continue;
            }

            if (this.catalog == null)
                continue;

            // Each player's view (a column with a job), each finding once: references to things that do not exist or
            // cannot be used are problems; recast, gauge and follow-up findings are advice for the plan's author.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (label, me) in PlayersOf(plan, planFight.SyncLevel))
            {
                foreach (var f in PlanChecker.Check(planFight, this.catalog, plan, me))
                {
                    if (f.Severity == PlanFindingSeverity.Info || !seen.Add(f.Code + "|" + f.Message))
                        continue;
                    var line = $"{label}: {f.Code}: {f.Message}";
                    if (f.Severity == PlanFindingSeverity.Error && StructuralCodes.Contains(f.Code))
                        item.Problems.Add(line);
                    else
                        item.Warnings.Add(line);
                }
            }
        }

        return section;
    }

    /// <summary>Plan-check codes that mean the plan refers to something that does not exist or cannot be used.</summary>
    private static readonly HashSet<string> StructuralCodes = new(StringComparer.Ordinal)
    {
        "unknown-mechanic", "unknown-action", "wrong-job", "not-available-at-level",
    };

    /// <summary>
    /// The players a plan is written for: one per column (assignee) and job (a slot-only column such as "MT" for every job
    /// its entries name), as the plugin selects entries for a player who picked that column; plans without columns are
    /// read per job.
    /// </summary>
    private static IEnumerable<(string Label, PlayerContext Me)> PlayersOf(MitPlan plan, int level)
    {
        var players = new List<(string, PlayerContext)>();
        foreach (var assignee in plan.Assignees.Where(a => !string.IsNullOrWhiteSpace(a.Key)))
        {
            var jobs = assignee.Job is { } j && j != Job.None
                ? [j]
                : plan.Entries.Where(e => string.Equals(e.Who?.Assignee, assignee.Key, StringComparison.OrdinalIgnoreCase))
                    .Select(e => e.Who?.Job).OfType<Job>().Where(x => x != Job.None).Distinct().Order().ToList();
            foreach (var job in jobs)
                players.Add(($"\"{assignee.Key}\" {job}", new PlayerContext(job, level, assignee.Slot, assignee.Key)));
        }

        if (players.Count == 0)
        {
            foreach (var job in plan.Entries.Select(e => e.Who?.Job).OfType<Job>().Where(x => x != Job.None).Distinct().Order())
                players.Add((job.ToString(), new PlayerContext(job, level)));
        }

        return players;
    }
}
