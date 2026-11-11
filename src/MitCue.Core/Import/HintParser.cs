using System.Globalization;
using System.Text.RegularExpressions;
using MitCue.Core.Fights;

namespace MitCue.Core.Import;

/// <summary>What the hint words of a token say about when to press it (docs/DESIGN.md §6 step 7).</summary>
internal sealed record HintTiming
{
    public static readonly HintTiming None = new();

    /// <summary>Another mechanic to anchor to ("after Solar Ray", "Invuln Diffuse Wave Cannon", "as P1 dies"), or null for the row's.</summary>
    public Mechanic? Anchor { get; init; }

    /// <summary>The hit the hint names ("after 1st Hit" → 0), or null.</summary>
    public int? HitIndex { get; init; }

    /// <summary>The hint names the last hit ("after the last hit", "after Solar Ray" on a two-hit mechanic).</summary>
    public bool LastHit { get; init; }

    /// <summary>Press shortly after the hit ("after 1st Hit", "after Solar Ray", "after the hit").</summary>
    public bool After { get; init; }

    /// <summary>The action belongs to the end of a phase ("just before P3 ends", "as P1 dies"); <see cref="Anchor"/> is that phase's end.</summary>
    public bool AtPhaseEnd { get; init; }

    /// <summary>"if MCH": the action is only for this job.</summary>
    public Job? JobCondition { get; init; }

    /// <summary>The hint is only a mechanic name (the cell names another mechanic of the phase than its row).</summary>
    public bool Reanchored { get; init; }

    /// <summary>Groups tokens of one item that share their timing.</summary>
    public (string? Anchor, int? Hit, bool Last, bool After, bool PhaseEnd) Key => (this.Anchor?.Id, this.HitIndex, this.LastHit, this.After, this.AtPhaseEnd);

    public bool IsNone => this.Key == None.Key;
}

/// <summary>Reads timing hints: "after 1st Hit", "after &lt;mechanic&gt;", "just before P3 ends", "as P1 dies", "if MCH", a bare mechanic name.</summary>
internal static partial class HintParser
{
    private static readonly HashSet<string> HitNouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "hit", "hits", "stack", "stacks", "set", "sets", "tower", "towers", "protean", "proteans", "cleave", "cleaves",
        "jump", "jumps", "laser", "lasers", "buster", "busters", "raidwide", "raidwides", "wave", "waves", "round",
        "rounds", "spread", "spreads", "meteor", "meteors", "explosion", "explosions", "aoe", "aoes",
    };

    private static readonly Dictionary<string, int> OrdinalWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["first"] = 1, ["second"] = 2, ["third"] = 3, ["fourth"] = 4, ["fifth"] = 5, ["sixth"] = 6, ["seventh"] = 7, ["eighth"] = 8,
    };

    [GeneratedRegex(
        @"\bend\s+of\s+(?:the\s+)?(?:P|phase\s*)(?<n>\d{1,2})\b" +
        @"|\b(?:P|phase\s*)(?<n>\d{1,2})\s+(?:ends?|dies|died|is\s+(?:over|dying|ending|pushed)|finishes|gets\s+pushed|pushes)\b" +
        @"|\b(?<cur>end\s+of\s+(?:the\s+)?phase|(?:boss|phase)\s+(?:dies|ends))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PhaseEnd();

    [GeneratedRegex(@"\bafter\s+(?:the\s+)?(?<ord>\d{1,2}(?:st|nd|rd|th)|first|second|third|fourth|fifth|sixth|seventh|eighth|last)\s+(?<noun>[\p{L}-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AfterOrdinal();

    [GeneratedRegex(@"\bafter\s+(?:the\s+)?(?:hit|hits|damage|it|that|them)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AfterGeneric();

    [GeneratedRegex(@"\b(?<word>after|before|befor|bevor|b4|for)\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Preposition();

    [GeneratedRegex(@"\bif\s+(?<job>[A-Za-z]{3})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JobCondition();

    [GeneratedRegex(@"\b(?:if|unless|in\s+case|otherwise)\b(?:\s+(?<next>[\p{L}\p{N}]+))?|\bno\s+skip\b|\bskip(?:ped|s)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConditionWords();

    [GeneratedRegex(@"^\d{1,2}(?:st|nd|rd|th)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OrdinalNumber();

    /// <summary>A hit noun ("Proteans", "stack", "towers") or an ordinal ("1st", "second", "last").</summary>
    internal static bool IsHintWord(string word) =>
        HitNouns.Contains(word) || OrdinalWords.ContainsKey(word) || word.Equals("last", StringComparison.OrdinalIgnoreCase) || OrdinalNumber().IsMatch(word);

    /// <summary>
    /// The noun of a hint that only names a part of a mechanic ("Proteans", "Stack", "stack hits"), singular and lower-case
    /// ("protean", "stack"), or null.
    /// </summary>
    internal static string? PartNoun(string? text)
    {
        var words = (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => w.Trim('.', ',', ':', ';', '!', '?')).Where(w => w.Length > 0).ToList();
        if (words.Count is 0 or > 2 || !HitNouns.Contains(words[0]) || words[0].StartsWith("hit", StringComparison.OrdinalIgnoreCase))
            return null;
        if (words.Count == 2 && !words[1].Equals("hit", StringComparison.OrdinalIgnoreCase) && !words[1].Equals("hits", StringComparison.OrdinalIgnoreCase))
            return null;
        var noun = words[0].ToLowerInvariant();
        return noun.EndsWith('s') && HitNouns.Contains(noun[..^1]) ? noun[..^1] : noun;
    }

    /// <summary>
    /// A condition that makes an action optional: "if P4 skip", "no skip", "unless …", "if needed" (an "if" followed by a
    /// job abbreviation is a job filter, see <see cref="HintTiming.JobCondition"/>), or null.
    /// </summary>
    internal static string? Condition(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        foreach (Match m in ConditionWords().Matches(text))
        {
            if (m.Value.StartsWith("if", StringComparison.OrdinalIgnoreCase) && m.Groups["next"].Value is { Length: 3 } job && job.All(char.IsAsciiLetter) && Jobs.TryParse(job, out _))
                continue;
            return text.Trim();
        }

        return null;
    }

    /// <summary>
    /// Reads the hints of one token. <paramref name="rest"/> is the source text after the action words ("after 1st Hit",
    /// "Diffuse Wave Cannon"); <paramref name="hints"/> are its parentheticals ("just before P3 ends").
    /// </summary>
    /// <param name="rest">Text after the matched action words, or null.</param>
    /// <param name="hints">Parenthetical hints of the token.</param>
    /// <param name="mechanics">The fight's mechanics.</param>
    /// <param name="phaseId">The row's phase.</param>
    /// <param name="rowMechanic">The row's mechanic.</param>
    /// <param name="phaseIdOf">Maps a phase number ("P3" → 3) to the fight's phase id.</param>
    public static HintTiming Parse(string? rest, IReadOnlyList<string> hints, MechanicResolver mechanics, string? phaseId, Mechanic? rowMechanic, Func<int, string> phaseIdOf)
    {
        var texts = new List<string>();
        if (!string.IsNullOrWhiteSpace(rest))
            texts.Add(rest.Trim());
        texts.AddRange(hints.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim()));
        if (texts.Count == 0)
            return HintTiming.None;

        Job? condition = null;
        foreach (var t in texts)
        {
            foreach (Match m in JobCondition().Matches(t))
            {
                if (m.Groups["job"].Value.All(char.IsAsciiLetter) && Jobs.TryParse(m.Groups["job"].Value, out var j))
                {
                    condition = j;
                    break;
                }
            }
        }

        // 1. The end of a phase.
        foreach (var t in texts)
        {
            if (PhaseEnd().Match(t) is not { Success: true } m)
                continue;
            var id = m.Groups["n"].Success ? phaseIdOf(int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture)) : phaseId;
            if (id is not null && mechanics.PhaseEnd(id) is { } end)
                return new HintTiming { Anchor = end, AtPhaseEnd = true, JobCondition = condition };
        }

        // 2. "after 1st Hit", "right after the 2nd stack", "after the last hit".
        var anchor = rowMechanic;
        foreach (var t in texts)
        {
            if (AfterOrdinal().Match(t) is not { Success: true } m || !HitNouns.Contains(m.Groups["noun"].Value))
                continue;
            var ord = m.Groups["ord"].Value;
            if (ord.Equals("last", StringComparison.OrdinalIgnoreCase))
                return new HintTiming { After = true, LastHit = true, JobCondition = condition };
            var n = OrdinalWords.TryGetValue(ord, out var w) ? w : int.Parse(new string(ord.TakeWhile(char.IsDigit).ToArray()), CultureInfo.InvariantCulture);
            var hits = anchor?.Hits.Count ?? 1;
            if (n >= 1 && n <= Math.Max(hits, 1))
                return new HintTiming { After = true, HitIndex = n - 1, JobCondition = condition };
        }

        // 3. "after <mechanic>", "before <mechanic>", "for <mechanic>"; then "after the hit".
        foreach (var t in texts)
        {
            foreach (Match m in Preposition().Matches(t))
            {
                if (mechanics.FindInText(t[(m.Index + m.Length)..], phaseId, rowMechanic, whole: false) is not { } named)
                    continue;
                var after = m.Groups["word"].Value.Equals("after", StringComparison.OrdinalIgnoreCase);
                return new HintTiming
                {
                    Anchor = named.Equals(rowMechanic) ? null : named,
                    After = after,
                    LastHit = after,
                    JobCondition = condition,
                };
            }
        }

        foreach (var t in texts)
        {
            if (AfterGeneric().IsMatch(t))
                return new HintTiming { After = true, JobCondition = condition };
        }

        // 4. A bare mechanic name: the cell names another mechanic of the phase ("Invuln Diffuse Wave Cannon").
        foreach (var t in texts)
        {
            if (mechanics.FindInText(t, phaseId, rowMechanic, whole: true) is { } named && !named.Equals(rowMechanic))
                return new HintTiming { Anchor = named, Reanchored = true, JobCondition = condition };
        }

        return condition is null ? HintTiming.None : new HintTiming { JobCondition = condition };
    }
}
