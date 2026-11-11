using System.Globalization;
using System.Text.RegularExpressions;

namespace MitCue.Core.Util;

/// <summary>Parsing and formatting of "m:ss" style times used in plans.</summary>
public static partial class TimeText
{
    [GeneratedRegex(@"(?<![\d:])(?:(?<h>\d{1,2}):)?(?<m>\d{1,2}):(?<s>\d{2})(?:\.(?<f>\d+))?(?![\d:])")]
    private static partial Regex TimeRegex();

    /// <summary>Parses "1:18", "01:18", "1:02:03" or "1:18.5" into seconds. Surrounding text is not allowed.</summary>
    public static bool TryParse(string? text, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var m = TimeRegex().Match(text.Trim());
        if (!m.Success || m.Index != 0 || m.Length != text.Trim().Length)
            return false;
        seconds = ToSeconds(m);
        return true;
    }

    /// <summary>All m:ss times found in free text, in order, with their positions.</summary>
    public static IReadOnlyList<(double Seconds, int Index, int Length)> FindAll(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [];
        return TimeRegex().Matches(text).Select(m => (ToSeconds(m), m.Index, m.Length)).ToList();
    }

    public static string Format(double seconds)
    {
        var neg = seconds < 0;
        var t = Math.Abs(seconds);
        var m = (int)(t / 60);
        var s = t - (m * 60);
        return (neg ? "-" : string.Empty) + m.ToString(CultureInfo.InvariantCulture) + ":" + s.ToString("00", CultureInfo.InvariantCulture);
    }

    private static double ToSeconds(Match m)
    {
        var h = m.Groups["h"].Success ? int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture) : 0;
        var min = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
        var s = int.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture);
        var f = m.Groups["f"].Success ? double.Parse("0." + m.Groups["f"].Value, CultureInfo.InvariantCulture) : 0;
        return (h * 3600) + (min * 60) + s + f;
    }
}
