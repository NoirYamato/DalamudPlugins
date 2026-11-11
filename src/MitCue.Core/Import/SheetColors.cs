using System.Globalization;

namespace MitCue.Core.Import;

/// <summary>Helpers for the "#rrggbb" colours of <see cref="SheetCell"/> and <see cref="TextRun"/>.</summary>
public static class SheetColors
{
    /// <summary>Parses "#rrggbb" (or "rrggbb", any case).</summary>
    public static bool TryParse(string? hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (hex is null)
            return false;
        var s = hex.AsSpan().Trim();
        if (s.Length > 0 && s[0] == '#')
            s = s[1..];
        if (s.Length != 6)
            return false;
        return byte.TryParse(s[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
            && byte.TryParse(s[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
            && byte.TryParse(s[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
    }

    /// <summary>
    /// A neutral mid grey, the usual "optional" text colour (Carry the Clear uses #808080; Google's palette greys
    /// #999999, #b7b7b7 and #cccccc also count). Near-black greys such as #545454 or #666666, used for dimmed but
    /// not optional text, and near-white ones do not.
    /// </summary>
    public static bool IsGrey(string? hex)
    {
        if (!TryParse(hex, out var r, out var g, out var b))
            return false;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var avg = (r + g + b) / 3.0;
        return max - min <= 24 && avg >= 0x70 && avg <= 0xe0;
    }

    /// <summary>Lower-case "#rrggbb", or null when the input is not a colour.</summary>
    public static string? Normalize(string? hex) =>
        TryParse(hex, out var r, out var g, out var b) ? string.Create(CultureInfo.InvariantCulture, $"#{r:x2}{g:x2}{b:x2}") : null;
}
