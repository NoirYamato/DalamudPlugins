using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using MitCue.Core.Util;

namespace MitCue.Core.Import;

/// <summary>
/// A time written in a sheet, in seconds of the sheet's own clock (usually the phase timer): a single time
/// (<c>1:18</c>), an approximate one (<c>~1:22</c>, <c>≈3:04</c>, <c>about 0:40</c>), a range (<c>2:44–2:46</c>,
/// <c>1:47 – 1:56</c>) or a list (<c>0:28 / 0:37 / 0:46</c>). Seconds may be negative for pre-pull times (<c>-0:05</c>).
/// </summary>
public sealed partial record ParsedTime
{
    /// <summary>The (first) time in seconds; for a range, its start.</summary>
    public required double Seconds { get; init; }

    /// <summary>End of a range ("2:44–2:46" → 166), else null.</summary>
    public double? End { get; init; }

    /// <summary>Further times of a list ("0:28 / 0:37 / 0:46" → [37, 46]), in writing order.</summary>
    public IReadOnlyList<double> More { get; init; } = [];

    /// <summary>Marked as approximate (<c>~</c>, <c>≈</c>, <c>±</c>, "ca.", "approx", "about", "around", "roughly", "circa").</summary>
    public bool Approx { get; init; }

    /// <summary>The source text of the expression, e.g. "≈3:04" or "at ~1:22" or "0:28 / 0:37".</summary>
    public required string Text { get; init; }

    public bool IsRange => this.End.HasValue;

    /// <summary>Every time mentioned: <see cref="Seconds"/>, then <see cref="End"/> (if a range), then <see cref="More"/>.</summary>
    public IReadOnlyList<double> All
    {
        get
        {
            var list = new List<double>(2 + this.More.Count) { this.Seconds };
            if (this.End is { } e)
                list.Add(e);
            list.AddRange(this.More);
            return list;
        }
    }

    // One time with optional "at"/"@" and approximation prefix, optional sign, optional range end. A time may be
    // followed by a colon ("0:40: Rep"), but not by another time component ("1:21:00" is one time).
    [GeneratedRegex(
        @"(?:(?<=^|[^\p{L}\p{N}])at\s+|@\s*)?" +
        @"(?:(?<ax>[~≈±])\s*|(?<=^|[^\p{L}\p{N}])(?<ax>ca\.?|approx\.?|about|around|roughly|circa)\s+)?" +
        @"(?<neg>(?<=^|[\s(\[~≈@])[-−])?" +
        @"(?<![\d:.])(?<t1>(?:\d{1,2}:)?\d{1,2}:\d{2}(?:\.\d+)?)(?!\d|:\d)" +
        @"(?:\s*(?:[–—-]|(?<=\s)(?:to|until|till)(?=\s))\s*(?<ax2>[~≈])?\s*(?<t2>(?:\d{1,2}:)?\d{1,2}:\d{2}(?:\.\d+)?)(?!\d|:\d))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimeRegex();

    // What may separate the members of a time list.
    [GeneratedRegex(@"^\s*(?:/|,|&|\+|and)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ListGapRegex();

    /// <summary>Parses text that is only a time expression (surrounding whitespace, brackets and punctuation allowed).</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out ParsedTime? time)
    {
        time = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var all = FindAll(text);
        if (all.Count != 1)
            return false;
        var m = all[0];
        if (!IsFiller(text.AsSpan(0, m.Index)) || !IsFiller(text.AsSpan(m.Index + m.Length)))
            return false;
        time = m.Time;
        return true;

        static bool IsFiller(ReadOnlySpan<char> s)
        {
            foreach (var c in s)
            {
                if (!char.IsWhiteSpace(c) && c is not ('(' or ')' or '[' or ']' or ',' or '.' or ';' or ':'))
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Every time expression in free text, in order. Times separated only by "/", ",", "&amp;", "+" or "and" are
    /// merged into one list ("0:28 / 0:37" → one match with <see cref="More"/> = [37]).
    /// </summary>
    public static IReadOnlyList<TimeMatch> FindAll(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf(':') < 0)
            return [];
        var singles = new List<TimeMatch>();
        foreach (Match m in TimeRegex().Matches(text))
            singles.Add(new TimeMatch(FromMatch(m), m.Index, m.Length));
        if (singles.Count <= 1)
            return singles;

        var result = new List<TimeMatch>(singles.Count);
        var current = singles[0];
        for (var i = 1; i < singles.Count; i++)
        {
            var next = singles[i];
            var gapStart = current.Index + current.Length;
            var gap = text.AsSpan(gapStart, next.Index - gapStart);
            if (ListGapRegex().IsMatch(gap))
            {
                var merged = Merge(current.Time, next.Time) with { Text = text.Substring(current.Index, next.Index + next.Length - current.Index) };
                current = new TimeMatch(merged, current.Index, next.Index + next.Length - current.Index);
            }
            else
            {
                result.Add(current);
                current = next;
            }
        }

        result.Add(current);
        return result;
    }

    /// <summary>Formats seconds as m:ss (with a sign when negative), e.g. 82 → "1:22".</summary>
    public static string Format(double seconds) => TimeText.Format(seconds);

    /// <summary>Compact form for logs and tests: "~1:22", "2:44-2:46", "0:28,0:37".</summary>
    public override string ToString()
    {
        var s = (this.Approx ? "~" : string.Empty) + Format(this.Seconds);
        if (this.End is { } e)
            s += "-" + Format(e);
        foreach (var t in this.More)
            s += "," + Format(t);
        return s;
    }

    /// <summary>A list made of <paramref name="a"/> followed by <paramref name="b"/> (texts joined with " / ").</summary>
    internal static ParsedTime Merge(ParsedTime a, ParsedTime b) => a with
    {
        More = [.. a.More, .. b.All],
        Approx = a.Approx || b.Approx,
        Text = a.Text + " / " + b.Text,
    };

    private static ParsedTime FromMatch(Match m)
    {
        var neg = m.Groups["neg"].Success;
        var t1 = ToSeconds(m.Groups["t1"].Value) * (neg ? -1 : 1);
        double? t2 = m.Groups["t2"].Success ? ToSeconds(m.Groups["t2"].Value) : null;
        return new ParsedTime
        {
            Seconds = t1,
            End = t2,
            Approx = m.Groups["ax"].Success || m.Groups["ax2"].Success,
            Text = m.Value,
        };
    }

    private static double ToSeconds(string hms)
    {
        var parts = hms.Split(':');
        double total = 0;
        foreach (var p in parts)
            total = (total * 60) + double.Parse(p, NumberStyles.Float, CultureInfo.InvariantCulture);
        return total;
    }
}

/// <summary>A time expression found in text: the time and where it is.</summary>
public readonly record struct TimeMatch(ParsedTime Time, int Index, int Length);
