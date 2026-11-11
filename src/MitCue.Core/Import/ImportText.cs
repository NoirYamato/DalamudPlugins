using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MitCue.Core.Actions;

namespace MitCue.Core.Import;

/// <summary>Text helpers of the back half of the importer (label normalization, word handling, distances).</summary>
internal static partial class ImportText
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\([^()]*\)|\[[^\[\]]*\]")]
    private static partial Regex Parenthetical();

    // Damage numbers and counts in labels: "65k", "100–220k", "×2", "x5", "(multi-hit)" is removed with the parentheses.
    [GeneratedRegex(@"\b\d+(?:[.,]\d+)?\s*(?:[–—-]\s*\d+(?:[.,]\d+)?\s*)?k\b|(?:×|\bx)\s*\d+\b|\b\d+\s*[×x]\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DamageNumber();

    [GeneratedRegex(@"[^\p{L}\p{N}%*/]+")]
    private static partial Regex NonWord();

    /// <summary>
    /// The label normalization of docs/research/mechanics.md §2: EN/EM dashes become '-', ★/☆ are dropped, whitespace
    /// collapses, the text is trimmed and lower-cased, and a trailing ':' is dropped.
    /// </summary>
    public static string NormalizeLabel(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var s = text.Replace('–', '-').Replace('—', '-').Replace("★", string.Empty, StringComparison.Ordinal).Replace("☆", string.Empty, StringComparison.Ordinal);
        s = Whitespace().Replace(s, " ").Trim().ToLowerInvariant();
        while (s.EndsWith(':'))
            s = s[..^1].TrimEnd();
        return s;
    }

    /// <summary>
    /// A looser form for fallback matching: parenthesized parts, stars, damage numbers ("65k") and counts ("×2") are
    /// removed and every run of other punctuation becomes a single space ("Pantokrator: line stacks + missiles (65k) ★★★"
    /// → "pantokrator line stacks missiles").
    /// </summary>
    public static string SimplifyLabel(string? text)
    {
        var s = NormalizeLabel(text);
        if (s.Length == 0)
            return s;
        string previous;
        do
        {
            previous = s;
            s = Parenthetical().Replace(s, " ");
        }
        while (s != previous);
        s = DamageNumber().Replace(s, " ");
        s = NonWord().Replace(s, " ");
        return Whitespace().Replace(s, " ").Trim();
    }

    /// <summary>Words of an action phrase normalized like alias keys (<see cref="AliasDictionary.Normalize"/>).</summary>
    public static string[] NormalizedWords(string? text)
    {
        var n = AliasDictionary.Normalize(text ?? string.Empty);
        return n.Length == 0 ? [] : n.Split(' ');
    }

    /// <summary>
    /// The original text after the first <paramref name="count"/> words in the sense of <see cref="AliasDictionary.Normalize"/>
    /// (runs of letters, digits and '%'; apostrophes do not split), trimmed of separators; null when nothing is left.
    /// "Short CD to R1", 2 → "to R1"; "Co-Tank now", 2 → "now".
    /// </summary>
    public static string? SkipNormalizedWords(string text, int count)
    {
        var i = 0;
        var seen = 0;
        while (i < text.Length && seen < count)
        {
            while (i < text.Length && !IsWordChar(text, i))
                i++;
            if (i >= text.Length)
                break;
            while (i < text.Length && (IsWordChar(text, i) || text[i] is '\'' or '’'))
                i++;
            seen++;
        }

        var rest = text[i..].Trim().TrimStart(',', ';', ':', '-', '–', '—', '+', '/').Trim();
        return rest.Length == 0 ? null : rest;

        static bool IsWordChar(string s, int k)
        {
            if (char.IsHighSurrogate(s[k]) && k + 1 < s.Length)
                return Rune.IsLetterOrDigit(new Rune(s[k], s[k + 1]));
            return char.IsLetterOrDigit(s[k]) || s[k] == '%';
        }
    }

    /// <summary>Optimal string alignment distance (Levenshtein with adjacent transpositions), capped at <paramref name="max"/> + 1.</summary>
    public static int Distance(string a, string b, int max)
    {
        if (Math.Abs(a.Length - b.Length) > max)
            return max + 1;
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
            d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++)
            d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            var rowMin = int.MaxValue;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var v = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    v = Math.Min(v, d[i - 2, j - 2] + 1);
                d[i, j] = v;
                rowMin = Math.Min(rowMin, v);
            }

            if (rowMin > max)
                return max + 1;
        }

        return Math.Min(d[a.Length, b.Length], max + 1);
    }

    /// <summary>How many edits a fuzzy match may need: none below 4 characters, 1 up to 7, else 2.</summary>
    public static int FuzzyBudget(int length) => length < 4 ? 0 : length < 8 ? 1 : 2;

    /// <summary>Seconds with one decimal, for messages ("−3.3", "201.8").</summary>
    public static string Seconds(double s) => s.ToString("0.0##", CultureInfo.InvariantCulture);

    /// <summary>Rounds to 0.01 s so that plan files do not carry floating-point noise.</summary>
    public static double Round(double s) => Math.Round(s, 2, MidpointRounding.AwayFromZero);

    /// <summary>A name-based (RFC 4122 version 5) GUID: equal names give equal ids.</summary>
    public static Guid NameGuid(string name)
    {
        // Namespace: a fixed GUID for MitCue plan imports.
        var ns = new Guid("6d1f2c3a-9a1e-4c55-8f3e-2b7d4e0c9a11").ToByteArray(bigEndian: true);
        var bytes = Encoding.UTF8.GetBytes(name);
        var buffer = new byte[ns.Length + bytes.Length];
        ns.CopyTo(buffer, 0);
        bytes.CopyTo(buffer, ns.Length);
        var hash = SHA1.HashData(buffer);
        var g = hash.AsSpan(0, 16).ToArray();
        g[6] = (byte)((g[6] & 0x0F) | 0x50);
        g[8] = (byte)((g[8] & 0x3F) | 0x80);
        return new Guid(g, bigEndian: true);
    }
}
