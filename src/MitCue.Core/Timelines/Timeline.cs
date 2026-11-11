using System.Text.RegularExpressions;
using MitCue.Core.Events;

namespace MitCue.Core.Timelines;

/// <summary>Sync window around an entry's time: a sync can match while now ∈ [time − Before, time + After].</summary>
public readonly record struct SyncWindow(double Before, double After)
{
    /// <summary>cactbot's default window (see docs/research/cactbot-timelines.md).</summary>
    public static SyncWindow Default { get; } = new(2.5, 2.5);
}

/// <summary>Matches one field value of a game event: a literal, any of several literals, or a regex.</summary>
public sealed class FieldMatcher
{
    private readonly Regex? regex;

    private FieldMatcher(string display, IReadOnlyList<string>? literals, Regex? regex)
    {
        this.Display = display;
        this.Literals = literals;
        this.regex = regex;
    }

    /// <summary>Literal alternatives, or null when this is a regex matcher.</summary>
    public IReadOnlyList<string>? Literals { get; }

    public string Display { get; }

    public bool IsRegex => this.regex != null;

    public static FieldMatcher Literal(string value) => new(value, [value], null);

    public static FieldMatcher AnyOf(IEnumerable<string> values)
    {
        var list = values.ToList();
        return new FieldMatcher("[" + string.Join(", ", list) + "]", list, null);
    }

    /// <summary>A regex matcher; the pattern must match the whole value (anchored, case-insensitive).</summary>
    public static FieldMatcher Pattern(string pattern) =>
        new(pattern, null, new Regex("^(?:" + pattern + ")$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    public bool IsMatch(string? value)
    {
        if (value == null)
            return false;
        if (this.regex != null)
            return this.regex.IsMatch(value);
        foreach (var lit in this.Literals!)
        {
            if (string.Equals(lit, value, StringComparison.OrdinalIgnoreCase))
                return true;
            // ids: compare hex numerically so "7B03" matches "07B03"
            if (TryHex(lit, out var a) && TryHex(value, out var b) && a == b && LooksHex(lit) && LooksHex(value))
                return true;
        }
        return false;
    }

    public override string ToString() => this.Display;

    private static bool LooksHex(string s) => s.Length is > 0 and <= 8 && s.All(Uri.IsHexDigit);

    private static bool TryHex(string s, out uint v) =>
        uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out v);
}

/// <summary>Options controlling how syncs match events.</summary>
public sealed class SyncMatchOptions
{
    /// <summary>
    /// When false, "source"/"target" name fields are ignored (use for non-English clients:
    /// timelines are written with English names). Ability ids still have to match.
    /// </summary>
    public bool MatchNames { get; init; } = true;

    public static SyncMatchOptions Default { get; } = new();
}

/// <summary>A parsed sync condition, e.g. <c>Ability { id: "7B03", source: "Omega" }</c>.</summary>
public sealed class SyncSpec
{
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
        if (this.LegacyRegex != null || e.SyncType == null || !string.Equals(e.SyncType, this.Type, StringComparison.Ordinal))
            return false;
        foreach (var (field, matcher) in this.Fields)
        {
            if (!options.MatchNames && field is "source" or "target")
                continue;
            if (!matcher.IsMatch(e.GetField(field)))
                return false;
        }
        return true;
    }

    public override string ToString() =>
        this.LegacyRegex != null ? $"sync /{this.LegacyRegex}/" : $"{this.Type} {{ {string.Join(", ", this.Fields.Select(f => $"{f.Key}: {f.Value}"))} }}";
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

    /// <summary>Numeric jump target (timeline seconds), if the entry jumps.</summary>
    public double? JumpTime { get; init; }

    /// <summary>Label jump target, if the entry jumps to a label.</summary>
    public string? JumpLabel { get; init; }

    /// <summary>True for <c>forcejump</c>: jump when time reaches the entry even without a sync.</summary>
    public bool ForceJump { get; init; }

    public double? Duration { get; init; }

    /// <summary>True when the entry is hidden from display (hideall names, "--sync--" etc.).</summary>
    public bool IsHidden { get; init; }

    public override string ToString() => $"{this.Time:0.0} \"{this.Name}\"{(this.Sync != null ? " " + this.Sync : string.Empty)}";
}

/// <summary>A parsed cactbot timeline.</summary>
public sealed class Timeline
{
    public required string Name { get; init; }

    /// <summary>Entries in file order (cactbot keeps file order; times are not necessarily sorted).</summary>
    public required IReadOnlyList<TimelineEntry> Entries { get; init; }

    /// <summary>Label name → timeline time.</summary>
    public required IReadOnlyDictionary<string, double> Labels { get; init; }

    /// <summary>Names hidden by <c>hideall</c>.</summary>
    public IReadOnlySet<string> HiddenNames { get; init; } = new HashSet<string>();

    /// <summary>Non-fatal parse problems (unknown keywords, unsupported syntax).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IEnumerable<TimelineEntry> Named(string name) =>
        this.Entries.Where(e => string.Equals(e.Name, name, StringComparison.Ordinal));
}
