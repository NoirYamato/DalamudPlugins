using MitCue.Core.Actions;

namespace MitCue.Core.Import;

/// <summary>What a token means for one job.</summary>
internal enum TokenKind
{
    /// <summary>Nothing to resolve (the token is only a time or hint).</summary>
    Empty,

    /// <summary>One or more actions of the job (see <see cref="TokenResolution.Alternatives"/>).</summary>
    Actions,

    /// <summary>A note-only phrase ("Everything", "Kitchen Sink").</summary>
    NoteOnly,

    /// <summary>Names actions the job has, but none of them is available at the sync level.</summary>
    NotAvailable,

    /// <summary>Another job's action or shorthand ("PLD Wings" for a Warrior).</summary>
    OtherJob,

    /// <summary>No alias, action name or close spelling.</summary>
    Unresolved,
}

/// <summary>One either/or alternative of a token, resolved for a job.</summary>
internal sealed record ResolvedAlternative
{
    /// <summary>The actions, at the sync level (upgrades applied), in order.</summary>
    public required IReadOnlyList<ActionInfo> Actions { get; init; }

    /// <summary>The actions are pressed in this order (an alias with <c>sequence: true</c>, e.g. "Spreadlo").</summary>
    public bool Sequence { get; init; }

    /// <summary>The source text after the matched words ("to R1", "after 1st Hit", "Diffuse Wave Cannon"), or null.</summary>
    public string? Rest { get; init; }

    /// <summary>The alias key or action name that matched.</summary>
    public required string Matched { get; init; }

    /// <summary>Matched by spelling distance (a typo); <see cref="Matched"/> is what it was taken for.</summary>
    public bool Fuzzy { get; init; }

    /// <summary>The source words that were matched by spelling distance.</summary>
    public string? FuzzyText { get; init; }

    /// <summary>Actions the shorthand names that are not available at the sync level (left out of <see cref="Actions"/>).</summary>
    public IReadOnlyList<string> NotAvailable { get; init; } = [];
}

/// <summary>A token resolved for one job (docs/DESIGN.md §6 step 4).</summary>
internal sealed record TokenResolution
{
    public static readonly TokenResolution EmptyToken = new() { Kind = TokenKind.Empty };

    public required TokenKind Kind { get; init; }

    /// <summary>For <see cref="TokenKind.Actions"/>: the alternatives that resolved (one, or several for "TBN/Oblation").</summary>
    public IReadOnlyList<ResolvedAlternative> Alternatives { get; init; } = [];

    /// <summary>The source text after the matched words of the first alternative that matched anything, or null.</summary>
    public string? Rest { get; init; }

    /// <summary>For <see cref="TokenKind.OtherJob"/>: whose action it is.</summary>
    public Job? OtherJob { get; init; }

    /// <summary>For <see cref="TokenKind.NotAvailable"/> (and partly available tokens): the actions missing at the level.</summary>
    public IReadOnlyList<string> NotAvailable { get; init; } = [];

    /// <summary>The alias key or action name that matched (for messages).</summary>
    public string? Matched { get; init; }
}

/// <summary>
/// Resolves cell tokens to actions for a job at a sync level: aliases first (longest key at the start of the token,
/// docs/research/actions.md §11), then a leading job abbreviation ("WHM Bell"), then exact action names, then close
/// spellings of the job's aliases and action names. Upgrades and downgrades for the level are applied
/// (<see cref="ActionCatalogExtensions.ResolveAt"/>).
/// </summary>
internal sealed class ActionResolver
{
    private readonly IActionCatalog actions;
    private readonly AliasDictionary aliases;
    private readonly int level;
    private readonly Dictionary<Job, List<(string Text, string Key, bool IsAlias)>> fuzzyCache = [];

    public ActionResolver(IActionCatalog actions, AliasDictionary aliases, int level)
    {
        this.actions = actions;
        this.aliases = aliases;
        this.level = level;
    }

    public int Level => this.level;

    public IActionCatalog Actions => this.actions;

    /// <summary>Resolves a token for a job. Each either/or alternative is tried with the token's trailing text.</summary>
    public TokenResolution Resolve(ParsedToken token, Job job)
    {
        var alternatives = token.Alternatives.Count > 0 ? token.Alternatives : token.Phrase.Length > 0 ? [token.Phrase] : [];
        if (alternatives.Count == 0)
            return TokenResolution.EmptyToken;

        var targeted = token.Target is not null;
        var results = alternatives.Select(a => this.ResolvePhrase(token.WithTrailing(a), job, targeted, allowJobPrefix: true, ImportText.NormalizedWords(a).Length)).ToList();
        var resolved = results.Where(r => r.Kind == TokenKind.Actions).ToList();
        var first = resolved.FirstOrDefault() ?? results.FirstOrDefault(r => r.Kind != TokenKind.Unresolved) ?? results[0];
        var notAvailable = results.SelectMany(r => r.NotAvailable).Distinct().ToList();
        if (resolved.Count > 0)
        {
            return new TokenResolution
            {
                Kind = TokenKind.Actions,
                Alternatives = resolved.SelectMany(r => r.Alternatives).ToList(),
                Rest = first.Rest,
                Matched = first.Matched,
                NotAvailable = notAvailable,
            };
        }

        var kind = results.Any(r => r.Kind == TokenKind.NoteOnly) ? TokenKind.NoteOnly
            : results.Any(r => r.Kind == TokenKind.NotAvailable) ? TokenKind.NotAvailable
            : results.Any(r => r.Kind == TokenKind.OtherJob) ? TokenKind.OtherJob
            : TokenKind.Unresolved;
        var pick = results.FirstOrDefault(r => r.Kind == kind) ?? first;
        return pick with { Kind = kind, NotAvailable = notAvailable };
    }

    /// <param name="phrase">The alternative followed by the token's trailing text.</param>
    /// <param name="job">The job to resolve for.</param>
    /// <param name="targeted">The token names another player (use an alias's targeted form).</param>
    /// <param name="allowJobPrefix">Try a leading job abbreviation ("WHM Bell").</param>
    /// <param name="headWords">Normalized words of the alternative itself: another job's alias or action only counts as
    /// such when it covers them all ("Kera" is a Sage action; "Co-Tank" is not Celestial Opposition's "co" + "tank").</param>
    private TokenResolution ResolvePhrase(string phrase, Job job, bool targeted, bool allowJobPrefix, int headWords)
    {
        var words = ImportText.NormalizedWords(phrase);
        if (words.Length == 0)
            return TokenResolution.EmptyToken;

        // 1. Aliases: the longest key at the start of the phrase; the rest is a hint, target or count.
        AliasMatch? otherJobAlias = null;
        if (this.aliases.MatchLongest(phrase, job) is { } match)
        {
            var keyWords = match.Key.Split(' ').Length;
            if (match.Expansions.Count > 0)
                return this.FromExpansion(match.Expansions[0], match.Key, ImportText.SkipNormalizedWords(phrase, keyWords), job, targeted);
            if (keyWords >= headWords)
                otherJobAlias = match;
        }

        // 2. A leading job abbreviation ("WHM Bell", "SCH Soil") says whose action it is.
        if (allowJobPrefix && words.Length >= 2 && words[0].Length == 3 && words[0].All(char.IsAsciiLetter) && Jobs.TryParse(words[0], out var prefixJob)
            && ImportText.SkipNormalizedWords(phrase, 1) is { } afterPrefix)
        {
            if (prefixJob == job)
            {
                var own = this.ResolvePhrase(afterPrefix, job, targeted, allowJobPrefix: false, Math.Max(1, headWords - 1));
                if (own.Kind is TokenKind.Actions or TokenKind.NotAvailable)
                    return own;
            }
            else if (this.ResolvePhrase(afterPrefix, prefixJob, targeted, allowJobPrefix: false, Math.Max(1, headWords - 1)).Kind == TokenKind.Actions)
            {
                return new TokenResolution { Kind = TokenKind.OtherJob, OtherJob = prefixJob, Matched = phrase, Rest = null };
            }
        }

        // 3. Exact action names ("Divine Benison on MT"): the longest leading run of words that names an action of the job.
        ActionInfo? otherJobAction = null;
        for (var n = words.Length; n >= 1; n--)
        {
            var name = string.Join(' ', words, 0, n);
            if (this.actions.FindByName(name, job) is not { } action)
                continue;
            if (!action.UsableBy(job))
            {
                if (n >= headWords)
                    otherJobAction ??= action;
                continue;
            }

            return this.FromActions([action], sequence: false, action.Name, ImportText.SkipNormalizedWords(phrase, n), fuzzy: null);
        }

        if (otherJobAlias is not null && this.OwnerOf(otherJobAlias.Key) is { } aliasOwner && aliasOwner != job)
            return new TokenResolution { Kind = TokenKind.OtherJob, OtherJob = aliasOwner, Matched = otherJobAlias.Key, Rest = string.IsNullOrEmpty(otherJobAlias.Rest) ? null : otherJobAlias.Rest };
        if (otherJobAction is not null)
            return new TokenResolution { Kind = TokenKind.OtherJob, OtherJob = otherJobAction.Jobs.FirstOrDefault(), Matched = otherJobAction.Name };

        // 4. Typos ("Nascant", "Vengance"): the closest alias or action name of the job within a small edit distance.
        if (this.Fuzzy(words, job) is { } close)
        {
            var rest = ImportText.SkipNormalizedWords(phrase, close.Words);
            if (close.IsAlias)
            {
                var expansions = this.aliases.Lookup(close.Key, job);
                if (expansions.Count > 0)
                {
                    var r = this.FromExpansion(expansions[0], close.Key, rest, job, targeted);
                    return r with { Alternatives = r.Alternatives.Select(a => a with { Fuzzy = true, FuzzyText = close.Text }).ToList() };
                }
            }
            else if (this.actions.FindByName(close.Key, job) is { } named && named.UsableBy(job))
            {
                return this.FromActions([named], sequence: false, named.Name, rest, fuzzy: close.Text);
            }
        }

        return new TokenResolution { Kind = TokenKind.Unresolved, Matched = null, Rest = null };
    }

    private TokenResolution FromExpansion(AliasExpansion e, string key, string? rest, Job job, bool targeted)
    {
        if (e.NoteOnly)
            return new TokenResolution { Kind = TokenKind.NoteOnly, Matched = key, Rest = rest };

        var names = targeted && e.Targeted.Count > 0 ? e.Targeted : e.Actions;
        var found = new List<ActionInfo>();
        foreach (var name in names)
        {
            if (this.actions.FindByName(name, job) is { } action && action.UsableBy(job))
                found.Add(action);
        }

        return this.FromActions(found, e.Sequence, key, rest, fuzzy: null);
    }

    private TokenResolution FromActions(IReadOnlyList<ActionInfo> named, bool sequence, string matched, string? rest, string? fuzzy)
    {
        var available = new List<ActionInfo>();
        var missing = new List<string>();
        foreach (var action in named)
        {
            var atLevel = this.actions.ResolveAt(action, this.level);
            if (atLevel.AvailableAt(this.level))
                available.Add(atLevel);
            else
                missing.Add(atLevel.Name);
        }

        if (available.Count == 0)
        {
            return new TokenResolution
            {
                Kind = missing.Count > 0 ? TokenKind.NotAvailable : TokenKind.Unresolved,
                Matched = matched,
                Rest = rest,
                NotAvailable = missing,
            };
        }

        var alternative = new ResolvedAlternative
        {
            Actions = available,
            Sequence = sequence && available.Count > 1,
            Rest = rest,
            Matched = matched,
            Fuzzy = fuzzy is not null,
            FuzzyText = fuzzy,
            NotAvailable = missing,
        };
        return new TokenResolution { Kind = TokenKind.Actions, Alternatives = [alternative], Rest = rest, Matched = matched, NotAvailable = missing };
    }

    /// <summary>The job an alias belongs to when it does not apply to the asking job (its first expansion's job, or the job of its first action).</summary>
    private Job? OwnerOf(string key)
    {
        foreach (var e in this.aliases.Lookup(key))
        {
            if (e.Job is { } j && j != Job.None)
                return j;
            foreach (var name in e.Actions)
            {
                if (this.actions.FindByName(name) is { Jobs.Count: > 0 } action)
                    return action.Jobs[0];
            }
        }

        return null;
    }

    /// <summary>
    /// The closest alias key or action name of the job to the first one to three words, when it is within
    /// <see cref="ImportText.FuzzyBudget"/> edits and unique (two different meanings at the same distance match nothing).
    /// </summary>
    private (string Key, bool IsAlias, int Words, string Text)? Fuzzy(string[] words, Job job)
    {
        var candidates = this.FuzzyCandidates(job);
        for (var n = Math.Min(3, words.Length); n >= 1; n--)
        {
            var text = string.Join(' ', words, 0, n);
            var budget = ImportText.FuzzyBudget(text.Length);
            if (budget == 0)
                continue;
            var best = budget + 1;
            var hits = new List<(string Key, bool IsAlias)>();
            foreach (var (candidate, key, isAlias) in candidates)
            {
                if (candidate.Split(' ').Length != n)
                    continue;
                var d = ImportText.Distance(text, candidate, budget);
                if (d > budget || d > best)
                    continue;
                if (d < best)
                {
                    best = d;
                    hits.Clear();
                }

                hits.Add((key, isAlias));
            }

            if (hits.Count == 0)
                continue;
            var meanings = hits.Select(h => this.Meaning(h.Key, h.IsAlias, job)).Distinct().ToList();
            if (meanings.Count == 1)
                return (hits[0].Key, hits[0].IsAlias, n, text);
        }

        return null;
    }

    /// <summary>What a fuzzy candidate stands for, so that an alias and the action name it expands to count as one meaning.</summary>
    private string Meaning(string key, bool isAlias, Job job)
    {
        if (!isAlias)
            return this.actions.FindByName(key, job)?.Name ?? key;
        var e = this.aliases.Lookup(key, job).FirstOrDefault();
        return e is null ? key : e.NoteOnly ? "note:" + key : string.Join("+", e.Actions);
    }

    private List<(string Text, string Key, bool IsAlias)> FuzzyCandidates(Job job)
    {
        if (this.fuzzyCache.TryGetValue(job, out var cached))
            return cached;
        var list = new List<(string, string, bool)>();
        foreach (var key in this.aliases.Keys)
        {
            if (this.aliases.Lookup(key, job).Any(e => !e.NoteOnly))
                list.Add((key, key, true));
        }

        foreach (var action in this.actions.ForJob(job, this.level))
        {
            var normalized = AliasDictionary.Normalize(action.Name);
            if (normalized.Length > 0)
                list.Add((normalized, action.Name, false));
        }

        this.fuzzyCache[job] = list;
        return list;
    }
}
