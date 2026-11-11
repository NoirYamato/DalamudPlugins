using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MitCue.Core.Events;

namespace MitCue.Core.Timelines;

/// <summary>Sync window around an entry's time: a sync can match while time − Before ≤ now &lt; time + After.</summary>
public readonly record struct SyncWindow(double Before, double After)
{
    /// <summary>cactbot's default window (see docs/research/cactbot-timelines.md).</summary>
    public static SyncWindow Default { get; } = new(2.5, 2.5);

    /// <summary>True when the window can never be active (cactbot's <c>window 0,0</c>).</summary>
    public bool IsEmpty => this.Before + this.After <= 0;
}

/// <summary>
/// Matches one field value of a game event against cactbot sync parameter values.
/// </summary>
/// <remarks>
/// Every value is a cactbot field value: a case-insensitive regular expression that must match the
/// whole field (cactbot puts each value between <c>|</c> delimiters). An array of values matches when
/// any of them does. Values without regex metacharacters (the usual "7B03" or "Omega") are compared as
/// text, which is equivalent and faster. Values that are plain hex numbers (1 to 8 hex digits) also
/// match a plain hex field value with the same number, so "0103" matches "103" and "00" matches "0":
/// padding of ids does not matter.
/// </remarks>
public sealed class FieldMatcher
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private readonly Alternative[] alternatives;

    private FieldMatcher(string display, Alternative[] alternatives)
    {
        this.Display = display;
        this.alternatives = alternatives;
        this.Values = Array.AsReadOnly(Array.ConvertAll(alternatives, a => a.Source));
        this.IsRegex = alternatives.Any(a => a.Regex != null);
        this.Literals = this.IsRegex ? null : this.Values;
    }

    /// <summary>The values that need no regex (all of <see cref="Values"/>), or null when any value is a regex.</summary>
    public IReadOnlyList<string>? Literals { get; }

    /// <summary>The values as written in the timeline (each a cactbot regex), in order.</summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>The values in cactbot notation, e.g. <c>"7B03"</c> or <c>["7B2A", "7B2D"]</c>.</summary>
    public string Display { get; }

    /// <summary>True when at least one value contains regex syntax.</summary>
    public bool IsRegex { get; }

    /// <summary>A single value (cactbot semantics: a whole-field, case-insensitive regex).</summary>
    public static FieldMatcher Literal(string value) => AnyOf([value]);

    /// <summary>Any of several values (a cactbot array; each value is a whole-field, case-insensitive regex).</summary>
    public static FieldMatcher AnyOf(IEnumerable<string> values)
    {
        var list = values.ToArray();
        var display = list.Length == 1 ? Quote(list[0]) : "[" + string.Join(", ", list.Select(Quote)) + "]";
        return new FieldMatcher(display, Array.ConvertAll(list, v => Alternative.Create(v, forceRegex: false)));
    }

    /// <summary>A regex matcher; the pattern must match the whole value (anchored, case-insensitive).</summary>
    public static FieldMatcher Pattern(string pattern) =>
        new(Quote(pattern), [Alternative.Create(pattern, forceRegex: true)]);

    public bool IsMatch(string? value)
    {
        if (value == null)
            return false;
        foreach (var alternative in this.alternatives)
        {
            if (alternative.IsMatch(value))
                return true;
        }

        return false;
    }

    public override string ToString() => this.Display;

    /// <summary>True when the text contains a character with a special meaning in a regex.</summary>
    internal static bool HasRegexSyntax(string s)
    {
        foreach (var c in s)
        {
            if (c is '\\' or '^' or '$' or '.' or '|' or '?' or '*' or '+' or '(' or ')' or '[' or ']' or '{' or '}')
                return true;
        }

        return false;
    }

    /// <summary>1–8 hex digits, nothing else.</summary>
    internal static bool TryPlainHex(string s, out uint value)
    {
        value = 0;
        if (s.Length is 0 or > 8)
            return false;
        foreach (var c in s)
        {
            if (!Uri.IsHexDigit(c))
                return false;
        }

        return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static string Quote(string value) => "\"" + value + "\"";

    private sealed class Alternative
    {
        private Alternative(string source, Regex? regex, uint? hex)
        {
            this.Source = source;
            this.Regex = regex;
            this.Hex = hex;
        }

        public string Source { get; }

        public Regex? Regex { get; }

        public uint? Hex { get; }

        public static Alternative Create(string value, bool forceRegex)
        {
            Regex? regex = null;
            if (forceRegex || HasRegexSyntax(value))
                regex = new Regex("^(?:" + value + ")$", Options);
            return new Alternative(value, regex, TryPlainHex(value, out var hex) ? hex : null);
        }

        public bool IsMatch(string value)
        {
            if (this.Regex != null ? this.Regex.IsMatch(value) : string.Equals(this.Source, value, StringComparison.OrdinalIgnoreCase))
                return true;

            // ids: compare plain hex numerically so "7B03" matches "07B03" and "00" matches "0"
            return this.Hex is { } hex && TryPlainHex(value, out var other) && other == hex;
        }
    }
}

/// <summary>Options controlling how syncs match events.</summary>
public sealed class SyncMatchOptions
{
    /// <summary>
    /// When false, "source"/"target" name fields are ignored (use for non-English clients:
    /// timelines are written with English names). Ability ids still have to match.
    /// </summary>
    /// <remarks>
    /// All localized display-name fields are ignored (<see cref="SyncSpec.NameFields"/>: source, target,
    /// name, ability, effect, ...). A sync that constrains nothing but names cannot be evaluated then and
    /// never matches (for example TEA's <c>RemovedCombatant { name: "Liquid Hand" }</c>).
    /// </remarks>
    public bool MatchNames { get; init; } = true;

    public static SyncMatchOptions Default { get; } = new();
}

/// <summary>A parsed sync condition, e.g. <c>Ability { id: "7B03", source: "Omega" }</c>.</summary>
public sealed class SyncSpec
{
    /// <summary>Type name used for old-style <c>sync /regex/</c> syncs.</summary>
    public const string LegacyType = "LegacyRegex";

    /// <summary>
    /// Fields holding localized display names (cactbot translates source, target, name, ability and
    /// line; effect names are localized as well). Ignored when <see cref="SyncMatchOptions.MatchNames"/> is false.
    /// </summary>
    public static readonly IReadOnlySet<string> NameFields =
        new HashSet<string>(StringComparer.Ordinal) { "source", "target", "name", "ability", "line", "effect", "targetName", "ownerName" };

    public SyncSpec(string type, IReadOnlyDictionary<string, FieldMatcher> fields, string? legacyRegex = null)
    {
        this.Type = type;
        this.Fields = fields;
        this.LegacyRegex = legacyRegex;
    }

    /// <summary>cactbot netregex type name ("Ability", "StartsUsing", "InCombat", ...), or "LegacyRegex".</summary>
    public string Type { get; }

    public IReadOnlyDictionary<string, FieldMatcher> Fields { get; }

    /// <summary>The raw pattern of an old-style <c>sync /.../</c> sync; such syncs never match (not supported).</summary>
    public string? LegacyRegex { get; }

    /// <summary>True when the event has the same sync type and every field matcher accepts the event's value.</summary>
    public bool Matches(GameEvent e, SyncMatchOptions? options = null)
    {
        options ??= SyncMatchOptions.Default;
        if (this.LegacyRegex != null || e.SyncType == null || !e.MatchesSyncType(this.Type))
            return false;

        var checkedAny = false;
        foreach (var (field, matcher) in this.Fields)
        {
            if (!options.MatchNames && NameFields.Contains(field))
                continue;
            if (!matcher.IsMatch(e.GetField(field)))
                return false;
            checkedAny = true;
        }

        // Only names were specified and names are ignored: the sync cannot be evaluated.
        return checkedAny || this.Fields.Count == 0;
    }

    public override string ToString()
    {
        if (this.LegacyRegex != null)
            return $"sync /{this.LegacyRegex}/";
        if (this.Fields.Count == 0)
            return this.Type + " {}";
        var sb = new StringBuilder(this.Type).Append(" { ");
        var first = true;
        foreach (var (key, matcher) in this.Fields)
        {
            if (!first)
                sb.Append(", ");
            sb.Append(key).Append(": ").Append(matcher.Display);
            first = false;
        }

        return sb.Append(" }").ToString();
    }
}

/// <summary>One line of a cactbot timeline.</summary>
public sealed class TimelineEntry
{
    /// <summary>Position in <see cref="Timeline.Entries"/>.</summary>
    public required int Index { get; init; }

    /// <summary>1-based line number in the source file.</summary>
    public required int Line { get; init; }

    public required double Time { get; init; }

    public required string Name { get; init; }

    public SyncSpec? Sync { get; init; }

    public SyncWindow Window { get; init; } = SyncWindow.Default;

    /// <summary>
    /// Jump target (timeline seconds) when the entry's sync jumps: the number of <c>jump N</c>, or the
    /// resolved time of <c>jump "label"</c>. Null when there is no jump or its label does not exist.
    /// A target of 0 stops the timeline.
    /// </summary>
    public double? JumpTime { get; init; }

    /// <summary>Label jump target, if the entry jumps to a label.</summary>
    public string? JumpLabel { get; init; }

    /// <summary>True for <c>forcejump</c>: jump when time reaches the entry even without a sync.</summary>
    public bool ForceJump { get; init; }

    public double? Duration { get; init; }

    /// <summary>True when the entry is hidden from display (hideall names, "--sync--" etc.).</summary>
    public bool IsHidden { get; init; }

    /// <summary>True when the entry has a sync that can match game events (not an old-style regex sync, not an empty window).</summary>
    public bool CanSync => this.Sync is { LegacyRegex: null } && !this.Window.IsEmpty;

    /// <summary>Start of the sync window (inclusive).</summary>
    public double SyncStart => this.Time - this.Window.Before;

    /// <summary>End of the sync window (exclusive).</summary>
    public double SyncEnd => this.Time + this.Window.After;

    public override string ToString() =>
        this.Time.ToString("0.0##", CultureInfo.InvariantCulture) + " \"" + this.Name + "\"" + (this.Sync != null ? " " + this.Sync : string.Empty);
}

/// <summary>A phase anchor hint: the ability (hex id) that starts a phase and its timeline time.</summary>
public sealed record PhaseAnchor(string AbilityId, double Time);

/// <summary>A parsed cactbot timeline.</summary>
public sealed class Timeline
{
    private IReadOnlyList<TimelineEntry>? byTime;
    private double? endTime;

    public required string Name { get; init; }

    /// <summary>Entries in file order (cactbot keeps file order; times are not necessarily sorted).</summary>
    public required IReadOnlyList<TimelineEntry> Entries { get; init; }

    /// <summary>Label name → timeline time.</summary>
    public required IReadOnlyDictionary<string, double> Labels { get; init; }

    /// <summary>Names hidden by <c>hideall</c>.</summary>
    public IReadOnlySet<string> HiddenNames { get; init; } = new HashSet<string>();

    /// <summary>Non-fatal parse problems (unknown keywords, unsupported syntax).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// The authors' phase anchors from the header hint <c># -p ID:time ...</c> (make_timeline.ts
    /// arguments), e.g. TOP's 7B13 at 400. Metadata only; empty when the file has none.
    /// </summary>
    public IReadOnlyList<PhaseAnchor> PhaseAnchors { get; init; } = [];

    /// <summary>The zone from a <c># ZoneId: N</c> header comment, if present.</summary>
    public int? ZoneId { get; init; }

    /// <summary>Entries ordered by time, equal times in file order (cactbot's playback order).</summary>
    public IReadOnlyList<TimelineEntry> ByTime =>
        this.byTime ??= this.Entries.OrderBy(e => e.Time).ThenBy(e => e.Index).ToArray();

    /// <summary>Latest time any entry is shown until: the maximum of entry time plus duration.</summary>
    public double EndTime => this.endTime ??= this.Entries.Count == 0 ? 0 : this.Entries.Max(e => e.Time + (e.Duration ?? 0));

    public IEnumerable<TimelineEntry> Named(string name) =>
        this.Entries.Where(e => string.Equals(e.Name, name, StringComparison.Ordinal));
}
