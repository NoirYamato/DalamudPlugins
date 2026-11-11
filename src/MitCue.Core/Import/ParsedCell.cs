namespace MitCue.Core.Import;

/// <summary>A target named in a cell: "(your 3rd/4th-in-line player)", "to R1/2", "Co-Tank", "on MT".</summary>
/// <param name="Value">Who: "R1/2", "Co-Tank", "MT", "your 3rd/4th-in-line player" (a leading on/to/onto/for is removed).</param>
/// <param name="Text">The source text it was taken from ("to R1/2").</param>
public sealed record ParsedTarget(string Value, string Text);

/// <summary>
/// One action-like token of a cell, not yet resolved to actions (docs/DESIGN.md §6 step 3). Tokens of one
/// <see cref="ParsedStep"/> are pressed together ("Protraction + Recit").
/// </summary>
/// <remarks>
/// Resolving a token (the back half's job): for each of <see cref="Alternatives"/> take the longest alias key that is
/// a prefix of <see cref="WithTrailing"/>(alternative); the rest is trailing text (a hint such as "after 1st Hit", a
/// count, a condition). The <see cref="Head"/>/<see cref="Trailing"/> split is a heuristic (it cuts at connector words
/// like after/on/to/if and at mechanic words like Proteans/Stack/Co-Tank), so it may stop early ("Invuln Diffuse Wave
/// Cannon" has no cue and stays whole) — matching against the head plus trailing text tolerates both.
/// </remarks>
public sealed record ParsedToken
{
    /// <summary>Position within its step (0-based).</summary>
    public required int Index { get; init; }

    /// <summary>The token's source text, trimmed, with markers, parentheticals and times ("Plenary* (1:31)").</summary>
    public required string Text { get; init; }

    /// <summary>Index of <see cref="Text"/> in the cell text.</summary>
    public required int Start { get; init; }

    /// <summary>The cell reference.</summary>
    public string? Src { get; init; }

    /// <summary>The words of the token without parentheticals, times and markers, single-spaced ("Kera after 1st Hit").</summary>
    public required string Phrase { get; init; }

    /// <summary>Best guess of the action part: <see cref="Phrase"/> up to the first connector or mechanic word ("Kera", "TBN/Oblation", "Short CD"). Empty when the token is only a time or hint.</summary>
    public required string Head { get; init; }

    /// <summary>
    /// Either/or alternatives of the head: "Kera/Soil" → [Kera, Soil], "TBN/Oblation" → [TBN, Oblation], "Kera or Soil"
    /// → [Kera, Soil]. A "/" is split only between letters, so "R1/2" and "3rd/4th" stay whole. [Head] when there is no
    /// alternative, empty when <see cref="Head"/> is empty.
    /// </summary>
    public required IReadOnlyList<string> Alternatives { get; init; }

    /// <summary>The rest of <see cref="Phrase"/> after the head ("after 1st Hit", "Co-Tank", "to R1/2", "if P4 skip"), or null.</summary>
    public string? Trailing { get; init; }

    /// <summary>A job abbreviation that starts the head ("PLD Wings" → PLD), else null. The head still contains it.</summary>
    public Job? JobPrefix { get; init; }

    /// <summary>A target taken from <see cref="Trailing"/> ("to R1/2", "Co-Tank") or from a parenthetical ("(your 3rd/4th-in-line player)").</summary>
    public ParsedTarget? Target { get; init; }

    /// <summary>
    /// Parenthetical text that is neither a time nor the target, including what is left of a parenthetical once its time
    /// is taken out: "(right after the 1st stack, ~1:22)" → "right after the 1st stack".
    /// </summary>
    public IReadOnlyList<string> Hints { get; init; } = [];

    /// <summary>
    /// The time written with this token (in a parenthetical or bare), or null. Within an item that is not a sequence,
    /// every token has this time or none (tokens with different times are split into separate items).
    /// </summary>
    public ParsedTime? Time { get; init; }

    /// <summary>Optional: the head is in a grey colour (see <see cref="CellParserOptions.IsOptionalColor"/>) or the text says "optional".</summary>
    public bool Optional { get; init; }

    /// <summary>Press when ready: marked "⏱" or written "on CD" / "when ready" / "whenever ready" / "asap".</summary>
    public bool WhenReady { get; init; }

    /// <summary>Marked with "*" (a footnote or legend reference, e.g. Carry the Clear's "Plenary*").</summary>
    public bool Footnote { get; init; }

    /// <summary>The head is struck through.</summary>
    public bool Struck { get; init; }

    /// <summary>Dominant text colour of the head ("#1154cc"), null for the default colour.</summary>
    public string? Fg { get; init; }

    /// <summary>The head is mostly bold.</summary>
    public bool Bold { get; init; }

    /// <summary>Formatting runs over <see cref="Text"/> (Start is relative to <see cref="Text"/>; null colour = default).</summary>
    public IReadOnlyList<TextRun> Runs { get; init; } = [];

    /// <summary>An alternative followed by the trailing text, for longest-prefix alias matching.</summary>
    public string WithTrailing(string alternative) => this.Trailing is null ? alternative : alternative + " " + this.Trailing;

    public override string ToString() => this.Text;
}

/// <summary>Tokens pressed together. Steps of an item are pressed in order ("Protraction + Recit → Adlo → Deploy").</summary>
public sealed record ParsedStep
{
    /// <summary>Order in the sequence (0-based).</summary>
    public required int Index { get; init; }

    public required string Text { get; init; }

    public required int Start { get; init; }

    /// <summary>The cell reference.</summary>
    public string? Src { get; init; }

    public required IReadOnlyList<ParsedToken> Tokens { get; init; }

    /// <summary>
    /// The step's time: its last token's time, else the first time among its tokens ("Protraction + Recit (2:44)": the
    /// step, Protraction included, is at 2:44). Null when no token has a time.
    /// </summary>
    public ParsedTime? Time => this.Tokens.Count == 0 ? null : this.Tokens[^1].Time ?? this.Tokens.Select(t => t.Time).FirstOrDefault(t => t is not null);

    /// <summary>The step's target: its last token's target, else the first among its tokens.</summary>
    public ParsedTarget? Target => this.Tokens.Count == 0 ? null : this.Tokens[^1].Target ?? this.Tokens.Select(t => t.Target).FirstOrDefault(t => t is not null);

    public override string ToString() => this.Text;
}

/// <summary>
/// An independent part of a cell (cells are split at " / "; Carry the Clear writes one action per item, each with its
/// own time). An item with several steps is a sequence.
/// </summary>
public sealed record ParsedItem
{
    /// <summary>Position in the cell (0-based).</summary>
    public required int Index { get; init; }

    public required string Text { get; init; }

    public required int Start { get; init; }

    public required IReadOnlyList<ParsedStep> Steps { get; init; }

    /// <summary>The cell reference.</summary>
    public string? Src { get; init; }

    /// <summary>The steps are pressed in order (more than one step).</summary>
    public bool IsSequence => this.Steps.Count > 1;

    public IEnumerable<ParsedToken> Tokens => this.Steps.SelectMany(s => s.Tokens);

    /// <summary>
    /// The item's time: the time of its last step ("Protraction + Recit → Adlo → Deploy (1:07)" → 1:07), else the first
    /// time written anywhere in it; null when the item has no time of its own (then the row's mechanic decides, not the
    /// row's time). For a sequence, per-step times are on <see cref="ParsedStep.Time"/>; an item that is not a sequence
    /// has at most one time (tokens with different times become separate items).
    /// </summary>
    public ParsedTime? Time => this.Steps.Count == 0 ? null : this.Steps[^1].Time ?? this.Steps.Select(s => s.Time).FirstOrDefault(t => t is not null);

    /// <summary>The item's target: the last step's, else the first found.</summary>
    public ParsedTarget? Target => this.Steps.Count == 0 ? null : this.Steps[^1].Target ?? this.Steps.Select(s => s.Target).FirstOrDefault(t => t is not null);

    /// <summary>Every token is optional (grey).</summary>
    public bool Optional => this.Tokens.Any() && this.Tokens.All(t => t.Optional);

    public bool WhenReady => this.Tokens.Any(t => t.WhenReady);

    public bool Footnote => this.Tokens.Any(t => t.Footnote);

    public override string ToString() => this.Text;
}

/// <summary>The result of <see cref="CellParser.Parse(SheetCell, string?)"/>.</summary>
public sealed record ParsedCell
{
    /// <summary>The cell text ("" for an empty cell).</summary>
    public required string Text { get; init; }

    /// <summary>The cell reference given to the parser.</summary>
    public string? Src { get; init; }

    public required IReadOnlyList<ParsedItem> Items { get; init; }

    /// <summary>Problems met while parsing ("unclosed-bracket"), with <see cref="Src"/> as their source; empty for a clean cell.</summary>
    public IReadOnlyList<ImportDiagnostic> Diagnostics { get; init; } = [];

    public bool IsEmpty => this.Items.Count == 0;

    public IEnumerable<ParsedToken> Tokens => this.Items.SelectMany(i => i.Tokens);

    public override string ToString() => this.Text;
}
