using System.Text.RegularExpressions;

namespace MitCue.Core.Import;

/// <summary>Options for <see cref="CellParser"/>.</summary>
public sealed class CellParserOptions
{
    public static CellParserOptions Default { get; } = new();

    /// <summary>
    /// Text colours that mark an action as optional. Default: neutral mid greys (<see cref="SheetColors.IsGrey"/>),
    /// Carry the Clear's "Grey = optional" (#808080).
    /// </summary>
    public Func<string, bool> IsOptionalColor { get; init; } = SheetColors.IsGrey;
}

/// <summary>
/// Step 3 of the import pipeline (docs/DESIGN.md §6): splits an assignment cell into items, sequence steps and
/// simultaneous tokens, and extracts times, targets, hints, markers and formatting, without resolving actions.
/// </summary>
/// <remarks>
/// Grammar, outermost first (parentheses and brackets are never split):
/// <list type="bullet">
/// <item><c> / </c> (a slash with whitespace on a side), <c>;</c> and <c>|</c> separate independent <b>items</b>, each possibly with its
/// own time: "Temp (1:18) / Bell (right after the 1st stack, ~1:22) / Plenary* (1:31)". An item that is only a time
/// joins the item before it ("Kera 0:28 / 0:37").</item>
/// <item>Arrows separate the <b>steps</b> of a sequence: "Protraction + Recit → Adlo → Deploy (1:07)". That is any
/// arrow character (→ ⇒ ⟶ ➔ ⇨ ➝ ⟹ …, the Unicode arrow blocks), "»", "-&gt;", "=&gt;", "--&gt;", "—&gt;", "~&gt;" and a
/// bare "&gt;" or "&gt;&gt;" (but not "&gt;50%").</item>
/// <item><c>+</c>, <c>,</c>, <c>&amp;</c>, and runs of 3+ spaces or line breaks separate simultaneous <b>tokens</b>
/// ("Party Mit        Dismantle"), except that a whitespace run is only padding when the word before it expects a
/// continuation ("Party Mit after          Solar Ray") or the word after it is a connector or mechanic word, or (for
/// spaces, not line breaks) a time ("Soil      0:12").</item>
/// <item>Tokens of one item that carry different times of their own become separate items, one per time
/// ("Soil (0:12)\nDeploy (0:17)", "0:12 Soil\n0:17 Deploy"): a token without a time goes with the timed token on its
/// line whose time covers it ("Kera + Soil (0:12)", "0:12 Soil + Kera"). Sequences keep their steps.</item>
/// <item>Inside a token: "X/Y" between letters is an either/or (<see cref="ParsedToken.Alternatives"/>); parentheticals
/// are times, targets or hints; bare times (<c>1:18</c>, <c>~1:22</c>, <c>at 0:40</c>, <c>2:44–2:46</c>) are times;
/// <c>*</c> marks a footnote and <c>⏱</c> "when ready"; the words after the head are <see cref="ParsedToken.Trailing"/>.</item>
/// </list>
/// Formatting: text colours are kept per token (<see cref="ParsedToken.Runs"/>, <see cref="ParsedToken.Fg"/>); a grey
/// head makes the token optional. No word of the cell is dropped: every letter/digit run ends up in a token's phrase,
/// a hint, a target or a time text. An unclosed "(" or "[" ends at the next item separator, arrow or line break and is
/// reported in <see cref="ParsedCell.Diagnostics"/>.
/// </remarks>
public sealed partial class CellParser
{
    private static readonly HashSet<string> Connectors = new(StringComparer.OrdinalIgnoreCase)
    {
        "after", "before", "befor", "bevor", "b4", "on", "to", "onto", "into", "in", "if", "for", "when", "whenever",
        "as", "at", "during", "until", "til", "till", "once", "pop", "pops", "popped", "use", "used", "using", "then",
        "with", "w/", "no", "not", "only", "right", "just", "from", "by", "around", "about", "between", "while",
        "unless", "except", "instead", "so", "but", "also", "asap", "early", "late", "later", "prepop", "pre-pop",
        "prepull", "pre-pull", "twice",

        // Placement of ground-targeted actions ("Asylum middle", "Soil under boss").
        "under", "behind", "near", "inside", "outside", "middle", "mid", "center", "centre", "north", "south", "east",
        "west",
    };

    // Words after which a long whitespace run is padding inside one phrase, not a token break.
    private static readonly HashSet<string> Continuations = new(StringComparer.OrdinalIgnoreCase)
    {
        "after", "before", "befor", "bevor", "b4", "to", "on", "onto", "into", "in", "if", "for", "when", "whenever",
        "as", "at", "during", "until", "til", "till", "with", "w/", "from", "by", "between", "while", "unless",
        "except", "and", "or", "then", "of", "the", "a", "an", "&",
    };

    private static readonly HashSet<string> MechanicWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "protean", "proteans", "stack", "stacks", "spread", "spreads", "auto", "autos", "aa", "hit", "hits", "uptime",
        "cleave", "cleaves", "buster", "busters", "tankbuster", "tankbusters", "tb", "raidwide", "raidwides", "aoe",
        "aoes", "tower", "towers", "puddle", "puddles", "shields", "debuff", "debuffs", "dodge", "knockback", "kb",
        "phase",
    };

    private static readonly HashSet<string> TargetWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "co-tank", "cotank", "co-tanks", "offtank", "off-tank", "maintank", "main-tank",
    };

    private static readonly HashSet<string> TargetNouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "tank", "tanks", "healer", "healers", "co-healer", "cohealer", "player", "players", "partner", "buddy", "self",
        "yourself", "me", "you", "melee", "ranged", "caster", "party", "person", "target",
    };

    private static readonly HashSet<string> TargetIntro = new(StringComparer.OrdinalIgnoreCase) { "on", "to", "onto", "for" };

    private static readonly HashSet<string> TargetLeadIns = new(StringComparer.OrdinalIgnoreCase)
    {
        "on", "to", "onto", "for", "your", "the", "my", "other", "a", "each", "both",
    };

    private readonly CellParserOptions options;

    public CellParser(CellParserOptions? options = null)
    {
        this.options = options ?? CellParserOptions.Default;
    }

    public static CellParser Default { get; } = new();

    public CellParserOptions Options => this.options;

    private enum LexKind
    {
        Word,
        Space,
        Gap,
        ItemSep,
        SeqSep,
        JoinSep,
        Paren,
        Marker,
    }

    [GeneratedRegex(@"^(?:mt|ot|st|t1|t2|h1|h2|m1|m2|r1|r2|d1|d2|d3|d4)(?:/(?:mt|ot|st|t1|t2|h1|h2|m1|m2|r1|r2|d1|d2|d3|d4|\d))*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SlotWord();

    [GeneratedRegex(@"^(?:#?\d+|\d+(?:st|nd|rd|th))$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumberWord();

    [GeneratedRegex(@"^[x×]\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CountWord();

    [GeneratedRegex(@"\bon\s*(?:cd|cooldown)\b|\bwhen\s*(?:ever)?\s*(?:it'?s\s+)?(?:ready|up|available|back)\b|\basap\b|\bas soon as\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WhenReadyText();

    [GeneratedRegex(@"\boptional\b|^opt\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OptionalText();

    [GeneratedRegex(@"[\p{L}\p{N}]")]
    private static partial Regex HasWordChar();

    /// <summary>
    /// True for a word the parser reads as part of a hint rather than an action: a connector ("after", "if", "use"), a
    /// mechanic or target word ("Proteans", "Co-Tank", "tank"), a slot ("R1"), a count ("×2") or a number ("4", "1st").
    /// </summary>
    internal static bool IsHintWord(string word)
    {
        var w = word.Trim('.', ',', ':', ';', '!', '?', '"', '\'', '(', ')', '[', ']');
        return w.Length > 0 && (Connectors.Contains(w) || Continuations.Contains(w) || MechanicWords.Contains(w) || TargetWords.Contains(w)
            || TargetNouns.Contains(w) || TargetLeadIns.Contains(w) || SlotWord().IsMatch(w) || NumberWord().IsMatch(w) || CountWord().IsMatch(w));
    }

    /// <summary>Parses plain text (no formatting).</summary>
    public ParsedCell Parse(string? text, string? src = null) => this.Parse(new SheetCell { Text = text }, src);

    /// <summary>Parses a cell, using its colours and rich-text runs for <see cref="ParsedToken.Optional"/>, <see cref="ParsedToken.Fg"/> and <see cref="ParsedToken.Runs"/>.</summary>
    public ParsedCell Parse(SheetCell cell, string? src = null)
    {
        ArgumentNullException.ThrowIfNull(cell);
        var text = cell.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return new ParsedCell { Text = text, Src = src, Items = [] };
        return new Run(this, cell, text, src).Parse();
    }

    private readonly record struct Lex(LexKind Kind, int Start, int End);

    private sealed record Word(string Text, int Start, int End)
    {
        public string Clean => this.Text.Trim(TrimChars);

        private static readonly char[] TrimChars = ['.', ',', ':', ';', '!', '?', '"', '\'', '(', ')', '[', ']', '“', '”', '‘', '’'];
    }

    /// <summary>Mutable token while parsing.</summary>
    private sealed class TokenBuilder
    {
        public int Start;
        public int End;
        public List<Word> Words = [];
        public List<string> Hints = [];
        public ParsedTarget? ParenTarget;
        public ParsedTime? Time;

        /// <summary>The time is written before the words ("0:12 Soil"), not after them ("Soil (0:12)").</summary>
        public bool TimeFirst;
        public bool WhenReady;
        public bool Footnote;
        public bool Optional;

        public bool HasPhrase => this.Words.Count > 0;

        /// <summary>Nothing worth keeping: no words, time, hint or target (markers alone have nothing to mark).</summary>
        public bool IsBlank => this.Words.Count == 0 && this.Time is null && this.Hints.Count == 0 && this.ParenTarget is null;

        /// <summary>Takes over a phrase-less token's time, hints, target and markers; <paramref name="otherFirst"/> keeps writing order.</summary>
        public void Absorb(TokenBuilder other, bool otherFirst)
        {
            this.Start = Math.Min(this.Start, other.Start);
            this.End = Math.Max(this.End, other.End);
            if (otherFirst)
                this.Hints.InsertRange(0, other.Hints);
            else
                this.Hints.AddRange(other.Hints);
            if (other.ParenTarget is not null)
            {
                if (this.ParenTarget is null)
                    this.ParenTarget = other.ParenTarget;
                else
                    this.Hints.Add(other.ParenTarget.Text);
            }

            if (other.Time is not null)
            {
                if (this.Time is null)
                    this.TimeFirst = otherFirst;
                this.Time = this.Time is null ? other.Time : otherFirst ? ParsedTime.Merge(other.Time, this.Time) : ParsedTime.Merge(this.Time, other.Time);
            }

            this.WhenReady |= other.WhenReady;
            this.Footnote |= other.Footnote;
            this.Optional |= other.Optional;
        }
    }

    /// <summary>One parse of one cell.</summary>
    private sealed class Run
    {
        private readonly CellParser owner;
        private readonly string s;
        private readonly string? src;
        private readonly string?[] fg;
        private readonly bool[] bold;
        private readonly bool[] strike;
        private readonly bool guillemets;
        private readonly List<Lex> lexemes = [];
        private readonly List<ImportDiagnostic> diagnostics = [];
        private int[]? lineOf;

        public Run(CellParser owner, SheetCell cell, string text, string? src)
        {
            this.owner = owner;
            this.s = text;
            this.src = src;
            (this.fg, this.bold, this.strike) = CharFormats(cell, text);
            this.guillemets = text.Contains('«'); // «quoted» text: "»" closes a quote, it is not an arrow
        }

        public ParsedCell Parse()
        {
            this.Lex();
            var itemBuilders = new List<List<List<TokenBuilder>>>();
            foreach (var itemLex in SplitBy(this.lexemes, LexKind.ItemSep))
            {
                var steps = new List<List<TokenBuilder>>();
                foreach (var stepLex in SplitBy(itemLex, LexKind.SeqSep))
                {
                    var tokens = new List<TokenBuilder>();
                    foreach (var segment in this.SplitTokens(stepLex))
                    {
                        if (this.BuildToken(segment) is { } t)
                            tokens.Add(t);
                    }

                    if (tokens.Count > 0)
                        steps.Add(tokens);
                }

                AttachLoose(steps);
                foreach (var step in steps)
                    step.RemoveAll(t => t.IsBlank);
                steps.RemoveAll(x => x.Count == 0);
                if (steps.Count > 0)
                    itemBuilders.Add(steps);
            }

            // An item that is only a time/hint (no words) belongs to the item before it ("Kera 0:28 / 0:37"), or after it.
            for (var i = 0; i < itemBuilders.Count; i++)
            {
                var only = itemBuilders[i].SelectMany(x => x).ToList();
                if (only.Any(t => t.HasPhrase) || itemBuilders.Count == 1)
                    continue;
                var into = i > 0 ? itemBuilders[i - 1][^1][^1] : itemBuilders[i + 1][0][0];
                foreach (var t in i > 0 ? only : Enumerable.Reverse(only))
                    into.Absorb(t, otherFirst: i == 0);
                itemBuilders.RemoveAt(i);
                i--;
            }

            // Tokens with different times of their own are separate items ("Soil (0:12)\nDeploy (0:17)").
            for (var i = 0; i < itemBuilders.Count; i++)
            {
                if (itemBuilders[i].Count != 1)
                    continue; // a sequence keeps its steps; per-step and per-token times stay on ParsedStep/ParsedToken
                var groups = this.SplitByTime(itemBuilders[i][0]);
                if (groups.Count <= 1)
                    continue;
                itemBuilders.RemoveAt(i);
                itemBuilders.InsertRange(i, groups.Select(g => new List<List<TokenBuilder>> { g }));
                i += groups.Count - 1;
            }

            var items = new List<ParsedItem>(itemBuilders.Count);
            foreach (var steps in itemBuilders)
            {
                var parsedSteps = new List<ParsedStep>(steps.Count);
                foreach (var tokens in steps)
                {
                    var parsedTokens = tokens.Select((t, k) => this.Freeze(t, k)).ToList();
                    var start = tokens.Min(t => t.Start);
                    var end = tokens.Max(t => t.End);
                    parsedSteps.Add(new ParsedStep { Index = parsedSteps.Count, Text = this.s[start..end], Start = start, Src = this.src, Tokens = parsedTokens });
                }

                var itemStart = parsedSteps.Min(p => p.Start);
                var itemEnd = steps.SelectMany(x => x).Max(t => t.End);
                items.Add(new ParsedItem { Index = items.Count, Text = this.s[itemStart..itemEnd], Start = itemStart, Steps = parsedSteps, Src = this.src });
            }

            return new ParsedCell { Text = this.s, Src = this.src, Items = items, Diagnostics = this.diagnostics };
        }

        /// <summary>
        /// Splits a step whose tokens carry different times into groups, one per time, in writing order. A token without
        /// a time joins a timed token on its line: the next one when that one's time follows its words ("Kera + Soil
        /// (0:12)"), the previous one when its time comes first ("0:12 Soil + Kera"); otherwise the nearest one before
        /// it (or after it, at the start). Neighbouring groups with the same time stay one group.
        /// </summary>
        private List<List<TokenBuilder>> SplitByTime(List<TokenBuilder> tokens)
        {
            if (tokens.Where(t => t.Time is not null).Select(t => TimeKey(t.Time!)).Distinct().Count() <= 1)
                return [tokens];
            var line = tokens.Select(t => this.LineOf(t.Start)).ToArray();
            var anchor = new int[tokens.Count];
            for (var i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Time is not null)
                {
                    anchor[i] = i;
                    continue;
                }

                var prev = Nearest(i, -1);
                var next = Nearest(i, +1);
                int? prevSame = prev is { } p && line[p] == line[i] ? p : null;
                int? nextSame = next is { } q && line[q] == line[i] ? q : null;
                anchor[i] = nextSame is { } a && !tokens[a].TimeFirst ? a
                    : prevSame is { } b && tokens[b].TimeFirst ? b
                    : prevSame ?? nextSame ?? prev ?? next!.Value;
            }

            var groups = new List<List<TokenBuilder>>();
            var groupTimes = new List<string>();
            for (var i = 0; i < tokens.Count; i++)
            {
                var key = TimeKey(tokens[anchor[i]].Time!);
                if (i == 0 || anchor[i] != anchor[i - 1])
                {
                    if (groups.Count == 0 || groupTimes[^1] != key)
                    {
                        groups.Add([]);
                        groupTimes.Add(key);
                    }
                }

                groups[^1].Add(tokens[i]);
            }

            return groups;

            int? Nearest(int from, int step)
            {
                for (var k = from + step; k >= 0 && k < tokens.Count; k += step)
                {
                    if (tokens[k].Time is not null)
                        return k;
                }

                return null;
            }

            static string TimeKey(ParsedTime time) => string.Join(",", time.All.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        /// <summary>The 0-based line of a position in the cell.</summary>
        private int LineOf(int position)
        {
            if (this.lineOf is null)
            {
                this.lineOf = new int[this.s.Length + 1];
                var line = 0;
                for (var k = 0; k < this.s.Length; k++)
                {
                    this.lineOf[k] = line;
                    if (this.s[k] == '\n' || (this.s[k] == '\r' && (k + 1 >= this.s.Length || this.s[k + 1] != '\n')))
                        line++;
                }

                this.lineOf[this.s.Length] = line;
            }

            return this.lineOf[Math.Clamp(position, 0, this.s.Length)];
        }

        /// <summary>Within an item: tokens that are only a time/hint/marker join the token before them (or after, at the start).</summary>
        private static void AttachLoose(List<List<TokenBuilder>> steps)
        {
            var all = steps.SelectMany(x => x).ToList();
            if (all.All(t => t.HasPhrase) || all.All(t => !t.HasPhrase))
            {
                if (all.Count > 1 && all.All(t => !t.HasPhrase))
                {
                    // Nothing but times/hints: keep one token holding everything.
                    var first = all[0];
                    foreach (var t in all.Skip(1))
                        first.Absorb(t, otherFirst: false);
                    steps.Clear();
                    steps.Add([first]);
                }

                return;
            }

            TokenBuilder? previous = null;
            var pending = new List<TokenBuilder>();
            foreach (var step in steps)
            {
                for (var k = 0; k < step.Count; k++)
                {
                    var t = step[k];
                    if (t.HasPhrase)
                    {
                        for (var p = pending.Count - 1; p >= 0; p--)
                            t.Absorb(pending[p], otherFirst: true);
                        pending.Clear();
                        previous = t;
                        continue;
                    }

                    if (previous is not null)
                        previous.Absorb(t, otherFirst: false);
                    else
                        pending.Add(t);
                    step.RemoveAt(k);
                    k--;
                }
            }

            steps.RemoveAll(x => x.Count == 0);
        }

        private void Lex()
        {
            var s = this.s;
            var n = s.Length;
            var closeOfParen = MatchBrackets(s, '(', ')');
            var closeOfBracket = MatchBrackets(s, '[', ']');
            var unclosed = 0;
            string? firstUnclosed = null;
            var i = 0;
            while (i < n)
            {
                var c = s[i];
                if (char.IsWhiteSpace(c))
                {
                    var j = i;
                    var lineBreak = false;
                    while (j < n && char.IsWhiteSpace(s[j]))
                    {
                        lineBreak |= s[j] is '\n' or '\r';
                        j++;
                    }

                    if (j < n && s[j] == '/' && (j + 1 >= n || char.IsWhiteSpace(s[j + 1])))
                    {
                        var k = j + 1;
                        while (k < n && char.IsWhiteSpace(s[k]))
                            k++;
                        this.Add(LexKind.ItemSep, i, k);
                        i = k;
                        continue;
                    }

                    this.Add(lineBreak || j - i >= 3 ? LexKind.Gap : LexKind.Space, i, j);
                    i = j;
                    continue;
                }

                if (this.IsArrowAt(i))
                {
                    var e = i + 1;
                    while (e < n && (IsArrowChar(s[e]) || (s[e] == '»' && !this.guillemets)))
                        e++;
                    e = SkipVariation(s, e);
                    this.Add(LexKind.SeqSep, i, e);
                    i = e;
                    continue;
                }

                switch (c)
                {
                    case '(' or '[':
                    {
                        var close = c == '(' ? ')' : ']';
                        var closed = (c == '(' ? closeOfParen : closeOfBracket).TryGetValue(i, out var j);
                        var end = closed ? j + 1 : this.UnclosedEnd(i + 1);
                        if (!closed)
                        {
                            while (end > i + 1 && char.IsWhiteSpace(s[end - 1]))
                                end--; // minus trailing whitespace
                            if (unclosed++ == 0)
                                firstUnclosed = $"'{c}' at character {i + 1} has no closing '{close}'; it is read up to {(end < n ? "the next separator" : "the end of the cell")}: '{s[i..end]}'.";
                        }

                        this.Add(LexKind.Paren, i, end);
                        i = end;
                        continue;
                    }

                    case '/' when i == 0 || this.LastKind is LexKind.Paren or LexKind.Marker || i + 1 >= n || char.IsWhiteSpace(s[i + 1]):
                    case ';' or '|':
                    {
                        var k = i + 1;
                        while (k < n && char.IsWhiteSpace(s[k]))
                            k++;
                        this.Add(LexKind.ItemSep, i, k);
                        i = k;
                        continue;
                    }

                    case '+' or ',' or '&':
                        this.Add(LexKind.JoinSep, i, i + 1);
                        i++;
                        continue;

                    case '-' or '=' or '–' or '—' or '~' or '>' when IsAsciiArrow(s, i, out var arrowEnd):
                        this.Add(LexKind.SeqSep, i, arrowEnd);
                        i = arrowEnd;
                        continue;

                    case '*':
                        this.Add(LexKind.Marker, i, i + 1);
                        i++;
                        continue;

                    case '⏱' or '⏲' or '⌛' or '⏳':
                        this.Add(LexKind.Marker, i, SkipVariation(s, i + 1));
                        i = SkipVariation(s, i + 1);
                        continue;
                }

                var w = i;
                while (w < n && !this.EndsWord(w, i))
                    w++;
                if (w == i)
                    w++; // safety: always advance
                this.Add(LexKind.Word, i, w);
                i = w;
            }

            if (firstUnclosed is not null)
            {
                var more = unclosed > 1 ? $" {unclosed - 1} more bracket(s) in the cell are not closed either." : string.Empty;
                this.diagnostics.Add(new ImportDiagnostic(DiagnosticSeverity.Warning, "unclosed-bracket", firstUnclosed + more, this.src));
            }
        }

        /// <summary>The matching close of every opening bracket that has one (nesting counted), in one pass.</summary>
        private static Dictionary<int, int> MatchBrackets(string s, char open, char close)
        {
            var result = new Dictionary<int, int>();
            var stack = new Stack<int>();
            for (var k = 0; k < s.Length; k++)
            {
                if (s[k] == open)
                    stack.Push(k);
                else if (s[k] == close && stack.Count > 0)
                    result[stack.Pop()] = k;
            }

            return result;
        }

        private LexKind? LastKind => this.lexemes.Count == 0 ? null : this.lexemes[^1].Kind;

        private void Add(LexKind kind, int start, int end) => this.lexemes.Add(new Lex(kind, start, end));

        private bool EndsWord(int j, int wordStart)
        {
            var c = this.s[j];
            if (char.IsWhiteSpace(c) || this.IsArrowAt(j))
                return true;
            switch (c)
            {
                case '(' or '[' or '+' or ',' or '&' or ';' or '|' or '*' or '⏱' or '⏲' or '⌛' or '⏳':
                    return true;
                case '-' or '=' or '–' or '—' or '~' or '>':
                    return IsAsciiArrow(this.s, j, out _);
                case '/':
                    // "Kera/Soil" stays one word; "Soil/ Kera" and a final "/" end it, except "w/" (with).
                    var atEnd = j + 1 >= this.s.Length || char.IsWhiteSpace(this.s[j + 1]);
                    return atEnd && !(j - wordStart == 1 && char.ToLowerInvariant(this.s[wordStart]) == 'w');
                default:
                    return false;
            }
        }

        /// <summary>
        /// An arrow character: the Unicode arrow blocks (→ ⇒ ↦ ⇨, ⟶ ⟹, ⤍, ➔ ➜ ➝ ➡, ⬅ ⮕ …). Left-pointing arrows also
        /// separate steps: merging the actions around them would lose one. An arrow may start a line ("Recit\n→ Adlo").
        /// </summary>
        private static bool IsArrowChar(char c) => c switch
        {
            >= '\u2190' and <= '\u21FF' => true, // Arrows
            >= '\u27F0' and <= '\u27FF' => true, // Supplemental Arrows-A
            >= '\u2900' and <= '\u297F' => true, // Supplemental Arrows-B
            >= '\u2794' and <= '\u27AF' or >= '\u27B1' and <= '\u27BE' => true, // Dingbat arrows (not the loops ➰ ➿)
            >= '\u2B00' and <= '\u2B11' or >= '\u2B30' and <= '\u2B4F' or >= '\u2B5A' and <= '\u2B73' or >= '\u2B76' and <= '\u2B95' or >= '\u2B98' and <= '\u2BB9' => true, // arrows among the Misc Symbols and Arrows
            _ => false,
        };

        /// <summary>
        /// An arrow character at <paramref name="i"/>, or a "»" that follows text on its line (not a bullet, and not a
        /// closing quote in a cell that quotes with «»).
        /// </summary>
        private bool IsArrowAt(int i)
        {
            var c = this.s[i];
            if (c != '»')
                return IsArrowChar(c);
            return !this.guillemets && (i == 0 || this.s[i - 1] != '»') && FollowsText(this.s, i);
        }

        /// <summary>Some text comes before <paramref name="i"/> on its line.</summary>
        private static bool FollowsText(string s, int i)
        {
            var k = i - 1;
            while (k >= 0 && s[k] is ' ' or '\t')
                k--;
            return k >= 0 && s[k] is not ('\n' or '\r');
        }

        /// <summary>
        /// A typed arrow at <paramref name="i"/>: up to three shaft characters (- = – — ~) and one to three "&gt;" ("-&gt;",
        /// "==&gt;", "—&gt;", "~&gt;"), or a bare "&gt;"/"&gt;&gt;" that follows text on its line (not a quote marker or a
        /// "&gt;&gt;note&lt;&lt;" decoration) and is not a comparison ("&gt;50%", "&gt;= 2", "&gt; 3 stacks").
        /// Scans at most a few characters, so long rules ("-----") stay linear.
        /// </summary>
        private static bool IsAsciiArrow(string s, int i, out int end)
        {
            end = i;
            var j = i;
            while (j < s.Length && j - i <= 3 && s[j] is '-' or '=' or '–' or '—' or '~')
                j++;
            if (j - i > 3)
                return false; // a rule ("-----"), not an arrow
            var k = j;
            while (k < s.Length && k - j <= 3 && s[k] == '>')
                k++;
            if (k == j || k - j > 3)
                return false;
            if (j == i)
            {
                if ((i > 0 && s[i - 1] == '>') || !FollowsText(s, i))
                    return false;
                var next = k;
                while (next < s.Length && s[next] == ' ')
                    next++;
                if (next < s.Length && (char.IsDigit(s[next]) || s[next] is '=' or '%'))
                    return false;
            }

            end = k;
            return true;
        }

        /// <summary>Where an unclosed "(" or "[" ends: at the next item separator, arrow or line break, else at the end.</summary>
        private int UnclosedEnd(int from)
        {
            var s = this.s;
            for (var k = from; k < s.Length; k++)
            {
                var c = s[k];
                if (c is ';' or '|' or '\n' or '\r' || this.IsArrowAt(k) || (c is '-' or '=' or '–' or '—' or '~' or '>' && IsAsciiArrow(s, k, out _)))
                    return k;
                if (c == '/' && k > 0 && char.IsWhiteSpace(s[k - 1]) && (k + 1 >= s.Length || char.IsWhiteSpace(s[k + 1])))
                    return k;
            }

            return s.Length;
        }

        private static int SkipVariation(string s, int i) => i < s.Length && s[i] == '\uFE0F' ? i + 1 : i; // emoji variation selector after a marker

        private static IEnumerable<List<Lex>> SplitBy(List<Lex> lexemes, LexKind separator)
        {
            var current = new List<Lex>();
            foreach (var l in lexemes)
            {
                if (l.Kind == separator)
                {
                    yield return current;
                    current = [];
                }
                else
                {
                    current.Add(l);
                }
            }

            yield return current;
        }

        /// <summary>Splits a step at "+", ",", "&amp;" and at whitespace gaps that separate tokens.</summary>
        private IEnumerable<List<Lex>> SplitTokens(List<Lex> step)
        {
            var current = new List<Lex>();
            for (var i = 0; i < step.Count; i++)
            {
                var l = step[i];
                if (l.Kind == LexKind.JoinSep || (l.Kind == LexKind.Gap && this.GapSeparates(step, i)))
                {
                    yield return current;
                    current = [];
                    continue;
                }

                current.Add(l);
            }

            yield return current;
        }

        private bool GapSeparates(List<Lex> step, int i)
        {
            var prev = i > 0 ? step[i - 1] : (Lex?)null;
            var next = i + 1 < step.Count ? step[i + 1] : (Lex?)null;
            if (prev is null || next is null || prev.Value.Kind == LexKind.JoinSep || next.Value.Kind == LexKind.JoinSep)
                return false;
            if (next.Value.Kind is LexKind.Paren or LexKind.Marker)
                return false; // "Soil      (0:12)": the parenthetical belongs to the word before
            if (prev.Value.Kind == LexKind.Word && Continuations.Contains(this.Text(prev.Value).Trim('.', ',', ':')))
                return false;
            if (next.Value.Kind == LexKind.Word)
            {
                var w = this.Text(next.Value).Trim('.', ',', ':');
                if (Connectors.Contains(w) || TargetWords.Contains(w))
                    return false; // "Kera      after 1st Hit", "Nascent      Co-Tank"
                if (HasTimeAt(w) && !this.HasLineBreak(step[i]))
                    return false; // "Soil      0:12"; a line that starts with a time starts a token ("0:12 Soil\n0:17 Deploy")
            }

            return true;
        }

        private bool HasLineBreak(Lex l) => this.s.AsSpan(l.Start, l.End - l.Start).IndexOfAny('\n', '\r') >= 0;

        private static bool HasTimeAt(string w) => ParsedTime.FindAll(w) is { Count: > 0 } m && m[0].Index == 0;

        private string Text(Lex l) => this.s[l.Start..l.End];

        /// <summary>Builds a token from a segment (words, parentheticals, markers and spaces); null when it is blank.</summary>
        private TokenBuilder? BuildToken(List<Lex> segment)
        {
            var content = segment.Where(l => l.Kind is not (LexKind.Space or LexKind.Gap)).ToList();
            if (content.Count == 0)
                return null;
            var t = new TokenBuilder { Start = content[0].Start, End = content[^1].End };

            // Markers.
            foreach (var m in content.Where(l => l.Kind == LexKind.Marker))
            {
                if (this.s[m.Start] == '*')
                    t.Footnote = true;
                else
                    t.WhenReady = true;
            }

            // Bare times among the words: search the segment with parentheticals and markers blanked out.
            var masked = new char[t.End - t.Start];
            for (var k = 0; k < masked.Length; k++)
                masked[k] = this.s[t.Start + k];
            foreach (var l in content.Where(l => l.Kind is LexKind.Paren or LexKind.Marker))
            {
                for (var k = l.Start; k < l.End; k++)
                    masked[k - t.Start] = ' ';
            }

            var maskedText = new string(masked);
            var timeSpans = new List<(int Start, int End)>();
            var times = new List<(int Position, ParsedTime Time)>();
            foreach (var tm in ParsedTime.FindAll(maskedText))
            {
                times.Add((t.Start + tm.Index, tm.Time with { Text = tm.Time.Text.Trim() }));
                timeSpans.Add((t.Start + tm.Index, t.Start + tm.Index + tm.Length));
            }

            // Words, minus the parts inside time expressions.
            foreach (var l in content.Where(l => l.Kind == LexKind.Word))
            {
                foreach (var (ws, we) in Subtract(l.Start, l.End, timeSpans))
                {
                    var text = this.s[ws..we];
                    if (HasWordChar().IsMatch(text))
                        t.Words.Add(new Word(text, ws, we));
                }
            }

            // Parentheticals: times, targets, hints.
            foreach (var p in content.Where(l => l.Kind == LexKind.Paren))
            {
                var innerStart = p.Start + 1;
                var innerEnd = p.End > p.Start + 1 && this.s[p.End - 1] is ')' or ']' ? p.End - 1 : p.End;
                var inner = this.s[innerStart..innerEnd];
                var innerTimes = ParsedTime.FindAll(inner);
                var rest = inner;
                foreach (var tm in innerTimes.OrderByDescending(x => x.Index))
                    rest = rest.Remove(tm.Index, tm.Length).Insert(tm.Index, " ");
                foreach (var tm in innerTimes)
                    times.Add((innerStart + tm.Index, tm.Time));
                rest = Regex.Replace(rest, @"\s+", " ").Trim(' ', ',', ';', ':', '-', '–', '—', '/');
                if (!HasWordChar().IsMatch(rest))
                {
                    if (rest.Length > 0 && innerTimes.Count == 0)
                        t.Hints.Add(rest); // keep odd symbols such as "(?)"
                    continue;
                }

                if (t.ParenTarget is null && IsTargetText(rest))
                    t.ParenTarget = new ParsedTarget(StripIntro(rest), rest);
                else
                    t.Hints.Add(rest);
            }

            // All times of the token, bare or in parentheses, in writing order.
            foreach (var (_, time) in times.OrderBy(x => x.Position))
                t.Time = t.Time is null ? time : ParsedTime.Merge(t.Time, time);
            t.TimeFirst = times.Count > 0 && t.Words.Count > 0 && times.Min(x => x.Position) < t.Words[0].Start;
            return t;
        }

        private static IEnumerable<(int Start, int End)> Subtract(int start, int end, List<(int Start, int End)> spans)
        {
            var pieces = new List<(int, int)> { (start, end) };
            foreach (var (a, b) in spans)
            {
                var next = new List<(int, int)>();
                foreach (var (x, y) in pieces)
                {
                    if (b <= x || a >= y)
                    {
                        next.Add((x, y));
                        continue;
                    }

                    if (a > x)
                        next.Add((x, a));
                    if (b < y)
                        next.Add((b, y));
                }

                pieces = next;
            }

            return pieces;
        }

        private ParsedToken Freeze(TokenBuilder t, int index)
        {
            var words = t.Words;
            var split = words.Count;
            for (var i = 1; i < words.Count; i++)
            {
                var w = words[i].Clean;
                if (Connectors.Contains(w) || MechanicWords.Contains(w) || TargetWords.Contains(w) || SlotWord().IsMatch(w) || CountWord().IsMatch(w)
                    || (w.Equals("co", StringComparison.OrdinalIgnoreCase) && i + 1 < words.Count && words[i + 1].Clean.Equals("tank", StringComparison.OrdinalIgnoreCase)))
                {
                    split = i;
                    break;
                }
            }

            var headWords = words.Take(split).ToList();
            var trailWords = words.Skip(split).ToList();
            var head = string.Join(' ', headWords.Select(w => w.Text));
            var trailing = trailWords.Count > 0 ? string.Join(' ', trailWords.Select(w => w.Text)) : null;
            var phrase = string.Join(' ', words.Select(w => w.Text));

            Job? jobPrefix = null;
            if (headWords.Count > 1 && headWords[0].Clean is { Length: 3 } j3 && j3.All(char.IsLetter) && Jobs.TryParse(j3, out var job))
                jobPrefix = job;

            var target = TrailingTarget(trailWords) ?? t.ParenTarget;
            var hints = new List<string>(t.Hints);
            if (t.ParenTarget is not null && target != t.ParenTarget)
                hints.Add(t.ParenTarget.Text);

            var flagText = string.Join(' ', hints.Prepend(trailing ?? string.Empty));
            var whenReady = t.WhenReady || WhenReadyText().IsMatch(flagText);
            var optional = t.Optional || hints.Any(h => OptionalText().IsMatch(h)) || (trailing is not null && OptionalText().IsMatch(trailing));

            // Formatting over the head (or the whole phrase / token when there is no head).
            var spans = headWords.Count > 0 ? headWords.Select(w => (w.Start, w.End)).ToList()
                : words.Count > 0 ? words.Select(w => (w.Start, w.End)).ToList()
                : [(t.Start, t.End)];
            var chars = spans.SelectMany(sp => Enumerable.Range(sp.Start, sp.End - sp.Start)).Where(i => !char.IsWhiteSpace(this.s[i])).ToList();
            string? dominant = null;
            var isBold = false;
            var struck = false;
            if (chars.Count > 0)
            {
                dominant = chars.GroupBy(i => this.fg[i]).OrderByDescending(g => g.Count()).First().Key;
                isBold = chars.Count(i => this.bold[i]) * 2 > chars.Count;
                struck = chars.All(i => this.strike[i]);
                optional |= chars.All(i => this.fg[i] is { } c && this.owner.options.IsOptionalColor(c));
            }

            return new ParsedToken
            {
                Index = index,
                Text = this.s[t.Start..t.End],
                Start = t.Start,
                Src = this.src,
                Phrase = phrase,
                Head = head,
                Alternatives = SplitAlternatives(head),
                Trailing = trailing,
                JobPrefix = jobPrefix,
                Target = target,
                Hints = hints,
                Time = t.Time,
                Optional = optional,
                WhenReady = whenReady,
                Footnote = t.Footnote,
                Struck = struck,
                Fg = dominant,
                Bold = isBold,
                Runs = this.RunsOver(t.Start, t.End),
            };
        }

        private List<TextRun> RunsOver(int start, int end)
        {
            var runs = new List<TextRun>();
            for (var i = start; i < end; i++)
            {
                if (runs.Count == 0 || runs[^1].Fg != this.fg[i] || runs[^1].Bold != this.bold[i] || runs[^1].Strike != this.strike[i])
                    runs.Add(new TextRun(i - start, this.fg[i], this.bold[i]) { Strike = this.strike[i] });
            }

            return runs;
        }

        /// <summary>"to R1/2", "on MT", "to 4", "Co-Tank", "co tank": the first target in the trailing words.</summary>
        private static ParsedTarget? TrailingTarget(List<Word> trail)
        {
            for (var i = 0; i < trail.Count; i++)
            {
                var w = trail[i].Clean;
                if (TargetIntro.Contains(w) && i + 1 < trail.Count)
                {
                    var phrase = new List<Word>();
                    for (var k = i + 1; k < trail.Count && phrase.Count < 4; k++)
                    {
                        var x = trail[k].Clean;
                        if (Connectors.Contains(x) && !TargetLeadIns.Contains(x))
                            break;
                        phrase.Add(trail[k]);
                    }

                    if (phrase.Count > 0 && phrase.Any(p => IsTargetNoun(p.Clean)))
                    {
                        // Keep the words up to and including the last target noun.
                        var last = phrase.FindLastIndex(p => IsTargetNoun(p.Clean));
                        var value = string.Join(' ', phrase.Take(last + 1).Select(p => p.Text));
                        return new ParsedTarget(value, trail[i].Text + " " + value);
                    }

                    continue;
                }

                if (TargetWords.Contains(w) || SlotWord().IsMatch(w))
                    return new ParsedTarget(trail[i].Text, trail[i].Text);
                if (w.Equals("co", StringComparison.OrdinalIgnoreCase) && i + 1 < trail.Count && trail[i + 1].Clean.Equals("tank", StringComparison.OrdinalIgnoreCase))
                    return new ParsedTarget(trail[i].Text + " " + trail[i + 1].Text, trail[i].Text + " " + trail[i + 1].Text);
            }

            return null;
        }

        private static bool IsTargetNoun(string w) =>
            TargetWords.Contains(w) || TargetNouns.Contains(w) || SlotWord().IsMatch(w) || NumberWord().IsMatch(w);

        /// <summary>A parenthetical naming a player: "(MT)", "(your 3rd/4th-in-line player)", "(on the Bladedance tank)".</summary>
        private static bool IsTargetText(string text)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim('.', ',', ':', '!', '?')).ToList();
            if (words.Count == 0)
                return false;
            if (words.Count <= 2 && words.All(w => TargetWords.Contains(w) || SlotWord().IsMatch(w) || w.Equals("co", StringComparison.OrdinalIgnoreCase) || w.Equals("tank", StringComparison.OrdinalIgnoreCase)))
                return true;
            return TargetLeadIns.Contains(words[0]) && words.Skip(1).Any(w => w.Split('-', '/').Any(IsTargetNoun) || IsTargetNoun(w));
        }

        private static string StripIntro(string text)
        {
            var space = text.IndexOf(' ');
            return space > 0 && TargetIntro.Contains(text[..space]) ? text[(space + 1)..].Trim() : text;
        }

        /// <summary>"Kera/Soil" → [Kera, Soil]; "Kera or Soil" → [Kera, Soil]; a "/" next to a digit does not split.</summary>
        private static IReadOnlyList<string> SplitAlternatives(string head)
        {
            if (head.Length == 0)
                return [];
            var parts = new List<string>();
            var start = 0;
            for (var i = 0; i < head.Length; i++)
            {
                if (head[i] == '/' && i > 0 && i + 1 < head.Length && char.IsLetter(head[i - 1]) && char.IsLetter(head[i + 1])
                    && !(i == 1 && char.ToLowerInvariant(head[0]) == 'w'))
                {
                    parts.Add(head[start..i]);
                    start = i + 1;
                }
            }

            parts.Add(head[start..]);
            var result = new List<string>();
            foreach (var p in parts)
                result.AddRange(Regex.Split(p, @"\s+or\s+", RegexOptions.IgnoreCase));
            result = result.Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            return result.Count == 0 ? [head] : result;
        }

        /// <summary>Effective text colour, bold and strike of every character (runs override the cell format).</summary>
        private static (string?[] Fg, bool[] Bold, bool[] Strike) CharFormats(SheetCell cell, string text)
        {
            var fg = new string?[text.Length];
            var bold = new bool[text.Length];
            var strike = new bool[text.Length];
            var runs = cell.Runs.OrderBy(r => r.Start).ToList();
            var r = -1;
            for (var i = 0; i < text.Length; i++)
            {
                while (r + 1 < runs.Count && runs[r + 1].Start <= i)
                    r++;
                if (r >= 0)
                {
                    fg[i] = SheetColors.Normalize(runs[r].Fg);
                    bold[i] = runs[r].Bold;
                    strike[i] = runs[r].Strike || cell.Strike;
                }
                else
                {
                    fg[i] = SheetColors.Normalize(cell.Fg);
                    bold[i] = cell.Bold;
                    strike[i] = cell.Strike;
                }
            }

            return (fg, bold, strike);
        }
    }
}
