using System.Text;
using MitCue.Core.Data;
using YamlDotNet.RepresentationModel;

namespace MitCue.Core.Actions;

/// <summary>Action catalog loaded from data/actions/actions.yaml.</summary>
public sealed class ActionCatalog : IActionCatalog
{
    private readonly List<ActionInfo> actions;
    private readonly Dictionary<uint, ActionInfo> byId;
    private readonly Dictionary<string, List<ActionInfo>> byName = new(StringComparer.Ordinal);

    /// <summary>Indexes the actions by id and normalized name.</summary>
    /// <exception cref="ArgumentException">Two actions share an id.</exception>
    public ActionCatalog(IEnumerable<ActionInfo> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        this.actions = actions.ToList();
        this.byId = new Dictionary<uint, ActionInfo>(this.actions.Count);
        foreach (var action in this.actions)
        {
            if (action == null)
                throw new ArgumentException("The action list contains a null entry.", nameof(actions));
            if (!this.byId.TryAdd(action.Id, action))
                throw new ArgumentException($"Duplicate action id {action.Id} ({this.byId[action.Id].Name} and {action.Name}).", nameof(actions));

            var key = AliasDictionary.Normalize(action.Name);
            if (key.Length == 0)
                continue;
            if (!this.byName.TryGetValue(key, out var sameName))
                this.byName[key] = sameName = [];
            sameName.Add(action);
        }
    }

    public IEnumerable<ActionInfo> All => this.actions;

    /// <summary>Game version the knowledge base was generated from (actions.yaml <c>game_version</c>), when loaded from the file.</summary>
    public string? GameVersion { get; init; }

    /// <summary>Parses the actions YAML (schema in docs/research/actions.md).</summary>
    /// <exception cref="FormatException">Invalid YAML, an unknown key or value, a duplicate id, or an unknown job.</exception>
    public static ActionCatalog Load(string yaml)
    {
        var file = ActionsFile.Parse(yaml);
        return new ActionCatalog(file.ToActionInfos()) { GameVersion = file.GameVersion };
    }

    public ActionInfo? Get(uint id) => this.byId.GetValueOrDefault(id);

    /// <summary>
    /// Exact name lookup, ignoring case, apostrophes and other punctuation ("natures minne" finds Nature's Minne).
    /// When several actions share the name, prefers one usable by <paramref name="job"/>, then the one that is not
    /// a follow-up row (Horoscope 16557 rather than its second-press row 16558).
    /// </summary>
    public ActionInfo? FindByName(string name, Job? job = null)
    {
        if (string.IsNullOrWhiteSpace(name) || !this.byName.TryGetValue(AliasDictionary.Normalize(name), out var candidates))
            return null;

        ActionInfo? best = null;
        var bestScore = -1;
        foreach (var action in candidates)
        {
            var score = (job is { } j && j != Job.None && action.UsableBy(j) ? 2 : 0) + (action.IsFollowUp ? 0 : 1);
            if (score > bestScore)
            {
                best = action;
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>
    /// Actions the job can use at the sync level, in file order: listed for the job, learned at or below the level and
    /// not replaced by an upgrade at that level (Succor at 90, Concitation at 100; never Physis at 60+).
    /// </summary>
    public IEnumerable<ActionInfo> ForJob(Job job, int syncLevel) =>
        this.actions.Where(a => a.UsableBy(job) && a.AvailableAt(syncLevel)).ToList();
}

/// <summary>Shorthand → action(s) dictionary loaded from data/actions/aliases.yaml plus user aliases.</summary>
/// <remarks>
/// Format and matching rules: docs/research/actions.md §11. Keys are normalized with <see cref="Normalize"/>.
/// An expansion with a <see cref="AliasExpansion.Job"/> applies only to that job; one without applies to every job
/// that can use all of its actions (checked against the catalog given to <see cref="Load"/>; without a catalog it
/// applies to every job). Names are not level-resolved here: use <see cref="ActionCatalogExtensions.FindAtLevel"/>.
/// </remarks>
public sealed class AliasDictionary
{
    /// <summary>The supported aliases.yaml <c>schema</c>.</summary>
    public const string CurrentSchema = "mitcue-aliases/1";

    private static readonly string[] ExpansionKeys = ["actions", "job", "sequence", "note_only", "note", "targeted"];

    private readonly Dictionary<string, List<AliasExpansion>> builtIn;
    private readonly Dictionary<string, List<AliasExpansion>> user = new(StringComparer.Ordinal);
    private readonly IActionCatalog? actions;

    private AliasDictionary(Dictionary<string, List<AliasExpansion>> builtIn, IActionCatalog? actions)
    {
        this.builtIn = builtIn;
        this.actions = actions;
    }

    /// <summary>All normalized alias keys (built-in and user), sorted.</summary>
    public IEnumerable<string> Keys =>
        this.builtIn.Keys.Union(this.user.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    /// <summary>The catalog used to decide which jobs an expansion without a job applies to (null = every job).</summary>
    public IActionCatalog? Actions => this.actions;

    /// <summary>Parses the aliases YAML (schema in docs/research/actions.md).</summary>
    /// <param name="yaml">The aliases.yaml text.</param>
    /// <param name="actions">
    /// Optional action catalog: when given, <see cref="Lookup"/> only returns expansions without a job for jobs that can
    /// use every action they name (so "rep" does not apply to healers).
    /// </param>
    /// <exception cref="FormatException">Invalid YAML, an unknown key, a duplicate alias, an unknown job, or an expansion without actions.</exception>
    public static AliasDictionary Load(string yaml, IActionCatalog? actions = null)
    {
        const string what = "aliases.yaml";
        var root = YamlNodes.LoadMapping(yaml, what);
        var schema = YamlNodes.GetString(root, "schema", what);
        if (schema != null && schema.Trim() != CurrentSchema)
            throw new FormatException($"{what}: unsupported schema '{schema}' (expected '{CurrentSchema}').");

        var aliasesNode = YamlNodes.Get(root, "aliases");
        if (aliasesNode is not YamlMappingNode aliases)
        {
            throw new FormatException(aliasesNode == null
                ? $"{what}: 'aliases' is missing."
                : $"{what}: 'aliases' must be a mapping of shorthand to actions ({YamlNodes.Where(aliasesNode)}).");
        }

        var map = new Dictionary<string, List<AliasExpansion>>(StringComparer.Ordinal);
        var lines = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (rawKey, keyNode, value) in YamlNodes.Entries(aliases))
        {
            var key = Normalize(rawKey);
            var context = $"{what}: alias '{rawKey}' ({YamlNodes.Where(keyNode)})";
            if (key.Length == 0)
                throw new FormatException($"{context}: the key has no letters or digits.");
            if (lines.TryGetValue(key, out var firstLine))
                throw new FormatException($"{context}: duplicate alias '{key}' (first defined on line {firstLine}).");
            lines[key] = keyNode.Start.Line;
            map[key] = ParseValue(value, context);
        }

        return new AliasDictionary(map, actions);
    }

    /// <summary>True when <paramref name="token"/> (normalized) is a built-in or user alias.</summary>
    public bool ContainsKey(string token)
    {
        var key = Normalize(token);
        return this.builtIn.ContainsKey(key) || this.user.ContainsKey(key);
    }

    /// <summary>
    /// Expansions for a token (normalized: lowercase, punctuation and repeated spaces removed),
    /// filtered to those applicable to <paramref name="job"/> when given. User aliases first.
    /// </summary>
    public IReadOnlyList<AliasExpansion> Lookup(string token, Job? job = null) =>
        token == null ? [] : this.LookupNormalized(Normalize(token), job);

    /// <summary>
    /// The longest alias key that equals the item or is followed by a space (docs/research/actions.md §11), e.g.
    /// "Short CD to R1" → key "short cd", rest "to r1"; "Lilybell pop after 1st Hit" → "lilybell", "pop after 1st hit".
    /// The key is chosen without regard to the job; <see cref="AliasMatch.Expansions"/> holds the expansions applicable
    /// to <paramref name="job"/> (empty when the phrase is another job's, e.g. "PLD Wings" for a WHM).
    /// </summary>
    /// <param name="item">One item of a cell, already split on the cell separators (" / ", "+", ",", "→", runs of 3+ spaces).</param>
    /// <param name="job">The assignee's job, if known.</param>
    /// <returns>The match, or null when the item does not start with any alias.</returns>
    public AliasMatch? MatchLongest(string item, Job? job = null)
    {
        var normalized = Normalize(item ?? string.Empty);
        if (normalized.Length == 0)
            return null;

        var words = normalized.Split(' ');
        for (var count = words.Length; count >= 1; count--)
        {
            var key = string.Join(' ', words, 0, count);
            if (this.builtIn.ContainsKey(key) || this.user.ContainsKey(key))
                return new AliasMatch(key, string.Join(' ', words, count, words.Length - count), this.LookupNormalized(key, job));
        }
        return null;
    }

    /// <summary>
    /// Adds or replaces a user alias: a user expansion with the same token and job is replaced, otherwise the expansion
    /// is added (a token may have one user expansion per job). User aliases come before built-in ones in <see cref="Lookup"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The token has no letters or digits.</exception>
    public void AddUser(string token, AliasExpansion expansion)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(expansion);
        var key = Normalize(token);
        if (key.Length == 0)
            throw new ArgumentException($"The alias '{token}' has no letters or digits.", nameof(token));

        var stored = new AliasExpansion
        {
            Actions = expansion.Actions?.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToArray() ?? [],
            Job = expansion.Job is Job.None ? null : expansion.Job,
            Sequence = expansion.Sequence,
            NoteOnly = expansion.NoteOnly,
            Note = expansion.Note,
            Targeted = expansion.Targeted?.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToArray() ?? [],
            IsUser = true,
        };

        if (!this.user.TryGetValue(key, out var list))
            this.user[key] = list = [];
        list.RemoveAll(e => e.Job == stored.Job);
        list.Add(stored);
    }

    /// <summary>Removes every user expansion of a token; returns false when there was none.</summary>
    public bool RemoveUser(string token) => token != null && this.user.Remove(Normalize(token));

    /// <summary>Removes all user aliases (built-in ones stay).</summary>
    public void ClearUser() => this.user.Clear();

    /// <summary>
    /// Problems in the built-in and user aliases against a catalog: unknown or misspelled action names, job-scoped
    /// expansions naming actions the job cannot use, expansions whose actions no single job has, and expansions
    /// without actions. Empty when everything resolves.
    /// </summary>
    public IReadOnlyList<string> Validate(IActionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var problems = new List<string>();
        foreach (var (source, map) in new[] { ("user alias", this.user), ("alias", this.builtIn) })
        {
            foreach (var (key, expansions) in map.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                foreach (var e in expansions)
                {
                    var label = $"{source} '{key}'" + (e.Job is { } j ? $" ({j})" : string.Empty);
                    if (e.NoteOnly)
                    {
                        if (e.Actions.Count > 0 || e.Targeted.Count > 0)
                            problems.Add($"{label}: a note-only alias must not name actions.");
                        continue;
                    }

                    if (e.Actions.Count == 0)
                        problems.Add($"{label}: names no actions.");

                    var resolved = new List<ActionInfo>();
                    foreach (var name in e.Actions.Concat(e.Targeted))
                    {
                        var action = catalog.FindByName(name, e.Job);
                        if (action == null)
                        {
                            problems.Add($"{label}: unknown action '{name}'.");
                            continue;
                        }
                        if (!e.IsUser && !string.Equals(action.Name, name, StringComparison.Ordinal))
                            problems.Add($"{label}: '{name}' should be spelled '{action.Name}' (data files use exact names).");
                        if (e.Job is { } job && !action.UsableBy(job))
                            problems.Add($"{label}: {action.Name} is not a {job} action.");
                        resolved.Add(action);
                    }

                    if (e.Job == null && resolved.Count > 0 && !Jobs.All.Any(job => resolved.All(a => a.UsableBy(job))))
                        problems.Add($"{label}: no job can use all of {string.Join(", ", resolved.Select(a => a.Name))}.");
                }
            }
        }
        return problems;
    }

    /// <summary>
    /// Normalizes text the same way keys are normalized: lowercase (invariant culture); apostrophes (' and ’) deleted;
    /// every other character that is not a letter, digit or % becomes a space; runs of spaces collapse; trimmed.
    /// "Nature's Minne" → "natures minne", "Co-Tank" → "co tank", "30%" → "30%".
    /// </summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value is '\'' or '\u2019')
                continue;
            if (Rune.IsLetterOrDigit(rune) || rune.Value == '%')
            {
                if (pendingSpace && sb.Length > 0)
                    sb.Append(' ');
                pendingSpace = false;
                sb.Append(Rune.ToLowerInvariant(rune).ToString());
            }
            else
            {
                pendingSpace = true;
            }
        }
        return sb.ToString();
    }

    private IReadOnlyList<AliasExpansion> LookupNormalized(string key, Job? job)
    {
        if (key.Length == 0)
            return [];
        var userList = this.user.GetValueOrDefault(key);
        var builtInList = this.builtIn.GetValueOrDefault(key);
        if (userList == null && builtInList == null)
            return [];

        var result = new List<AliasExpansion>();
        foreach (var e in (userList ?? []).Concat(builtInList ?? []))
        {
            if (job is not { } j || j == Job.None || this.AppliesTo(e, j))
                result.Add(e);
        }
        return result;
    }

    private bool AppliesTo(AliasExpansion e, Job job)
    {
        if (e.Job is { } only && only != Job.None)
            return only == job;
        if (this.actions == null)
            return true;
        foreach (var name in e.Actions.Concat(e.Targeted))
        {
            // Unknown names do not exclude the expansion: the resolver reports them.
            if (this.actions.FindByName(name, job) is { } action && !action.UsableBy(job))
                return false;
        }
        return true;
    }

    private static List<AliasExpansion> ParseValue(YamlNode value, string context)
    {
        switch (value)
        {
            case YamlScalarNode scalar when !YamlNodes.IsNull(scalar):
                return [new AliasExpansion { Actions = [scalar.Value!.Trim()] }];
            case YamlMappingNode mapping:
                return [ParseExpansion(mapping, context)];
            case YamlSequenceNode sequence when sequence.Children.Count > 0:
                var list = new List<AliasExpansion>();
                for (var i = 0; i < sequence.Children.Count; i++)
                {
                    if (sequence.Children[i] is not YamlMappingNode item)
                        throw new FormatException($"{context}: item {i + 1} of the list must be a mapping such as {{job: WHM, actions: [Temperance]}} ({YamlNodes.Where(sequence.Children[i])}).");
                    list.Add(ParseExpansion(item, $"{context}, item {i + 1}"));
                }
                return list;
            default:
                throw new FormatException($"{context}: the value must be an action name, a mapping or a list of mappings.");
        }
    }

    private static AliasExpansion ParseExpansion(YamlMappingNode map, string context)
    {
        foreach (var (key, keyNode, _) in YamlNodes.Entries(map))
        {
            if (!ExpansionKeys.Contains(key))
                throw new FormatException($"{context}: unknown key '{key}' ({YamlNodes.Where(keyNode)}); expected {string.Join(", ", ExpansionKeys)}.");
        }

        Job? job = null;
        if (YamlNodes.GetString(map, "job", context) is { } jobText)
        {
            if (!Jobs.TryParse(jobText, out var parsed))
                throw new FormatException($"{context}: unknown job '{jobText}'.");
            job = parsed;
        }

        var actions = YamlNodes.GetStringList(map, "actions", context);
        var noteOnly = YamlNodes.GetBool(map, "note_only", context) ?? false;
        if (noteOnly && actions.Count > 0)
            throw new FormatException($"{context}: a note_only alias must not list actions.");
        if (!noteOnly && actions.Count == 0)
            throw new FormatException($"{context}: 'actions' is missing (or set note_only: true).");

        return new AliasExpansion
        {
            Actions = actions,
            Job = job,
            Sequence = YamlNodes.GetBool(map, "sequence", context) ?? false,
            NoteOnly = noteOnly,
            Note = YamlNodes.GetString(map, "note", context),
            Targeted = YamlNodes.GetStringList(map, "targeted", context),
        };
    }
}
