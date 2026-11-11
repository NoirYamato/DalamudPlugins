using MitCue.Core.Fights;

namespace MitCue.Core.Import;

/// <summary>How a row label was matched to mechanics.</summary>
internal enum MechanicMatch
{
    /// <summary>No mechanic matches.</summary>
    None,

    /// <summary>The normalized label equals an alias (mechanics.md §2 normalization).</summary>
    Exact,

    /// <summary>Equal after removing parenthesized parts, stars, damage numbers and punctuation.</summary>
    Simplified,

    /// <summary>The label starts with an alias ("Pantokrator: line stacks …").</summary>
    Prefix,

    /// <summary>Within a small edit distance of an alias (typos).</summary>
    Fuzzy,

    /// <summary>Matched only in another phase than the row's.</summary>
    OtherPhase,
}

/// <summary>Mechanics a row label may mean, ordered by hit time.</summary>
internal sealed record MechanicCandidates(IReadOnlyList<Mechanic> Mechanics, MechanicMatch Match)
{
    public static readonly MechanicCandidates None = new([], MechanicMatch.None);
}

/// <summary>
/// Step 5 of the import pipeline (docs/DESIGN.md §6): maps row labels to the fight's mechanics through their aliases
/// (docs/research/mechanics.md §2 and §4.5). The order rule that picks among several candidates lives in
/// <see cref="PlanImporter"/>, which tracks the rows of a tab.
/// </summary>
internal sealed class MechanicResolver
{
    private readonly FightDefinition fight;
    private readonly Dictionary<string, List<Mechanic>> exact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Mechanic>> simplified = new(StringComparer.Ordinal);

    public MechanicResolver(FightDefinition fight)
    {
        this.fight = fight;
        foreach (var m in fight.Mechanics)
        {
            foreach (var alias in Names(m))
            {
                Add(this.exact, ImportText.NormalizeLabel(alias), m);
                Add(this.simplified, ImportText.SimplifyLabel(alias), m);
            }
        }
    }

    public FightDefinition Fight => this.fight;

    /// <summary>The candidates for a row label in a phase (null phase: every phase), best match kind first.</summary>
    public MechanicCandidates Candidates(string? label, string? phaseId)
    {
        var normalized = ImportText.NormalizeLabel(label);
        if (normalized.Length == 0)
            return MechanicCandidates.None;

        if (this.Lookup(this.exact, normalized, phaseId) is { Count: > 0 } byAlias)
            return new MechanicCandidates(byAlias, MechanicMatch.Exact);

        var simple = ImportText.SimplifyLabel(label);
        if (simple.Length > 0 && this.Lookup(this.simplified, simple, phaseId) is { Count: > 0 } bySimple)
            return new MechanicCandidates(bySimple, MechanicMatch.Simplified);

        // The label starts with an alias: keep the longest such alias (and every mechanic that has it).
        if (simple.Length > 0)
        {
            var best = 0;
            var found = new List<Mechanic>();
            foreach (var (alias, mechanics) in this.simplified)
            {
                if (alias.Length < 3 || alias.Length < best || !simple.StartsWith(alias, StringComparison.Ordinal) || (simple.Length > alias.Length && simple[alias.Length] != ' '))
                    continue;
                var inPhase = mechanics.Where(m => InPhase(m, phaseId)).ToList();
                if (inPhase.Count == 0)
                    continue;
                if (alias.Length > best)
                {
                    best = alias.Length;
                    found.Clear();
                }

                found.AddRange(inPhase);
            }

            if (found.Count > 0)
                return new MechanicCandidates(Order(found), MechanicMatch.Prefix);
        }

        // The exact label of a mechanic of another phase (a misplaced row or a missing section): an exact name elsewhere
        // beats a near miss in the phase ("P5 End", an alias of p6.start, is not a typo of p5.start's "P4 End").
        if (phaseId is not null && this.Lookup(this.exact, normalized, null) is { Count: > 0 } elsewhere)
            return new MechanicCandidates(elsewhere, MechanicMatch.OtherPhase);

        if (simple.Length > 0)
        {
            // Typos: the closest alias of the phase within the edit budget, when it is unique. A different number is a
            // different mechanic ("Solar Ray 2" vs "Solar Ray 3"), not a typo.
            var budget = ImportText.FuzzyBudget(simple.Length);
            var digits = Digits(simple);
            if (budget > 0)
            {
                var bestDistance = budget + 1;
                var fuzzy = new List<Mechanic>();
                foreach (var (alias, mechanics) in this.simplified)
                {
                    var inPhase = mechanics.Where(m => InPhase(m, phaseId)).ToList();
                    if (inPhase.Count == 0 || Digits(alias) != digits)
                        continue;
                    var d = ImportText.Distance(simple, alias, budget);
                    if (d > budget || d > bestDistance)
                        continue;
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        fuzzy.Clear();
                    }

                    fuzzy.AddRange(inPhase);
                }

                if (fuzzy.Count > 0)
                    return new MechanicCandidates(Order(fuzzy), MechanicMatch.Fuzzy);
            }
        }

        return MechanicCandidates.None;
    }

    /// <summary>
    /// Every mechanic of the phase the label may mean under any match tier (exact, simplified, prefix and spelling, each
    /// taken on its own), in hit order. For a timed row whose best-tier candidates contradict its time: "Solar Ray ×2" is
    /// exactly an alias of the second to fourth P5 Solar Rays, but simplified ("Solar Ray") also of the first.
    /// </summary>
    public IReadOnlyList<Mechanic> AllCandidates(string? label, string? phaseId)
    {
        var normalized = ImportText.NormalizeLabel(label);
        if (normalized.Length == 0)
            return [];
        var simple = ImportText.SimplifyLabel(label);
        var all = new List<Mechanic>(this.Lookup(this.exact, normalized, phaseId));
        if (simple.Length > 0)
        {
            all.AddRange(this.Lookup(this.simplified, simple, phaseId));
            var digits = Digits(simple);
            var budget = ImportText.FuzzyBudget(simple.Length);
            foreach (var (alias, mechanics) in this.simplified)
            {
                var prefix = alias.Length >= 3 && simple.StartsWith(alias, StringComparison.Ordinal) && (simple.Length == alias.Length || simple[alias.Length] == ' ');
                var close = budget > 0 && Digits(alias) == digits && ImportText.Distance(simple, alias, budget) <= budget;
                if (prefix || close)
                    all.AddRange(mechanics.Where(m => InPhase(m, phaseId)));
            }
        }

        return Order(all);
    }

    /// <summary>The digits of a label in order ("p5 end" → "5"), to tell numbered mechanics apart.</summary>
    private static string Digits(string s) => string.Concat(s.Where(char.IsAsciiDigit));

    /// <summary>
    /// A mechanic named in free text ("Solar Ray", "Diffuse Wave Cannon", "the Blind Faith cast"): the longest run of leading
    /// words that is an alias in the phase (with <paramref name="whole"/>, all of the words). Among several mechanics with
    /// that alias, <paramref name="prefer"/> when it is one of them, else the first at or after it, else the first. Null when
    /// no alias matches.
    /// </summary>
    public Mechanic? FindInText(string? text, string? phaseId, Mechanic? prefer, bool whole)
    {
        var words = ImportText.NormalizeLabel(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 0 && words[0] is "the" or "a" or "an")
            words.RemoveAt(0);
        for (var n = words.Count; n >= (whole ? words.Count : 1) && n >= 1; n--)
        {
            var phrase = string.Join(' ', words.Take(n));
            var list = this.Lookup(this.exact, phrase, phaseId);
            if (list.Count == 0)
                list = this.Lookup(this.simplified, ImportText.SimplifyLabel(phrase), phaseId);
            if (list.Count == 0)
                continue;
            if (prefer is not null && list.Contains(prefer))
                return prefer;
            return list.FirstOrDefault(m => prefer is null || m.Hit >= prefer.Hit) ?? list[0];
        }

        return null;
    }

    /// <summary>
    /// The mechanic that ends a phase, for "just before P3 ends" / "as P1 dies": the phase's last enrage, else its last
    /// mechanic that is not a marker.
    /// </summary>
    public Mechanic? PhaseEnd(string phaseId)
    {
        var inPhase = this.fight.Mechanics.Where(m => string.Equals(m.Phase, phaseId, StringComparison.OrdinalIgnoreCase)).ToList();
        return inPhase.LastOrDefault(m => m.Kind == MechanicKind.Enrage) ?? inPhase.LastOrDefault(m => m.Kind != MechanicKind.Marker);
    }

    /// <summary>The first mechanic of a phase (its start marker in TOP), used when a row's label cannot be resolved.</summary>
    public Mechanic? PhaseStart(string phaseId) =>
        this.fight.Mechanics.FirstOrDefault(m => string.Equals(m.Phase, phaseId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The aliases and the display name. Timeline entry names are not used: cactbot reuses them for different mechanics
    /// ("Solar Ray 1" is the first hit of every P5 Solar Ray), so the catalog lists them as aliases only where they are unique.
    /// </summary>
    private static IEnumerable<string> Names(Mechanic m)
    {
        foreach (var a in m.Aliases)
            yield return a;
        yield return m.Name;
    }

    private static void Add(Dictionary<string, List<Mechanic>> map, string key, Mechanic m)
    {
        if (key.Length == 0)
            return;
        if (!map.TryGetValue(key, out var list))
            map[key] = list = [];
        if (!list.Contains(m))
            list.Add(m);
    }

    private static bool InPhase(Mechanic m, string? phaseId) => phaseId is null || string.Equals(m.Phase, phaseId, StringComparison.OrdinalIgnoreCase);

    private static List<Mechanic> Order(IEnumerable<Mechanic> mechanics) => mechanics.Distinct().OrderBy(m => m.Hit).ToList();

    private List<Mechanic> Lookup(Dictionary<string, List<Mechanic>> map, string key, string? phaseId) =>
        map.TryGetValue(key, out var list) ? Order(list.Where(m => InPhase(m, phaseId))) : [];
}
