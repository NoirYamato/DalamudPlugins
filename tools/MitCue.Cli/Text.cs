using System.Globalization;
using System.Text;
using MitCue.Core.Fights;
using MitCue.Core.Util;

namespace MitCue.Cli;

/// <summary>Invariant, deterministic formatting of times and numbers.</summary>
internal static class Text
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>"m:ss" (decimals = 0) or "m:ss.f"; rounding never produces ":60"; a minus sign before zero.</summary>
    public static string Clock(double seconds, int decimals = 0)
    {
        var scale = Math.Pow(10, decimals);
        var units = (long)Math.Round(Math.Abs(seconds) * scale, MidpointRounding.AwayFromZero);
        var sign = seconds < 0 && units > 0 ? "-" : string.Empty;
        var perMinute = (long)(60 * scale);
        var minutes = units / perMinute;
        var rest = units % perMinute;
        var secondsPart = (rest / scale).ToString(decimals == 0 ? "00" : "00." + new string('0', decimals), Inv);
        return sign + minutes.ToString(Inv) + ":" + secondsPart;
    }

    /// <summary>Seconds with one decimal, e.g. "1167.8", "-5.0".</summary>
    public static string Sec(double seconds) => seconds.ToString("0.0", Inv);

    /// <summary>Seconds with one decimal and a sign, e.g. "+1.0", "-3.3", "+0.0".</summary>
    public static string Signed(double seconds) => (seconds < 0 && Math.Round(seconds, 1) != 0 ? "-" : "+") + Math.Abs(seconds).ToString("0.0", Inv);

    /// <summary>A number with up to two decimals ("120", "2.5").</summary>
    public static string Num(double value) => value.ToString("0.##", Inv);

    public static string Int(long value) => value.ToString(Inv);

    /// <summary>"1 entry" / "3 entries".</summary>
    public static string Count(int n, string singular, string? plural = null) =>
        n.ToString(Inv) + " " + (n == 1 ? singular : plural ?? singular + "s");

    /// <summary>A timeline time relative to the start of a phase, e.g. "P2 1:39" (whole seconds) or "P2 1:39.4".</summary>
    public static string PhaseTime(FightDefinition fight, double time, string? phaseId = null, int decimals = 0)
    {
        var phase = (phaseId != null ? fight.GetPhase(phaseId) : null) ?? fight.PhaseAt(time) ?? fight.Phases.OrderBy(p => p.Start).FirstOrDefault();
        return phase == null ? Clock(time, decimals) : phase.Id.ToUpperInvariant() + " " + Clock(time - phase.Start, decimals);
    }

    /// <summary>Parses "12.5", "-3" or a time "1:50" / "-0:05" into seconds.</summary>
    public static bool TryParseSeconds(string text, out double seconds)
    {
        seconds = 0;
        var t = text.Trim();
        var negative = t.StartsWith('-');
        var body = negative ? t[1..] : t;
        if (body.Contains(':'))
        {
            if (!TimeText.TryParse(body, out var s))
                return false;
            seconds = negative ? -s : s;
            return true;
        }

        return double.TryParse(t, NumberStyles.Float, Inv, out seconds) && double.IsFinite(seconds);
    }

    /// <summary>Text on one line: line breaks become " / ", runs of spaces collapse.</summary>
    public static string OneLine(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var sb = new StringBuilder(text.Length);
        var space = false;
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;
            if (sb.Length > 0)
                sb.Append(" / ");
            foreach (var c in trimmed)
            {
                if (char.IsWhiteSpace(c))
                {
                    space = true;
                    continue;
                }

                if (space)
                    sb.Append(' ');
                space = false;
                sb.Append(c);
            }

            space = false;
        }

        return sb.ToString();
    }

    /// <summary>Shortens text to <paramref name="max"/> characters with an ellipsis.</summary>
    public static string Clip(string text, int max) => text.Length <= max ? text : text[..Math.Max(0, max - 1)].TrimEnd() + "…";
}

/// <summary>A plain text table: columns padded to their widest cell, two spaces apart, the last column unpadded.</summary>
internal sealed class Table
{
    private readonly string[] headers;
    private readonly bool[] right;
    private readonly List<string[]> rows = [];

    /// <param name="headers">Column headers; a header starting with '&gt;' is right-aligned (the '&gt;' is not printed).</param>
    public Table(params string[] headers)
    {
        this.right = headers.Select(h => h.StartsWith('>')).ToArray();
        this.headers = headers.Select(h => h.TrimStart('>')).ToArray();
    }

    public int Count => this.rows.Count;

    public void Add(params string?[] cells)
    {
        var row = new string[this.headers.Length];
        for (var i = 0; i < row.Length; i++)
            row[i] = i < cells.Length ? cells[i] ?? string.Empty : string.Empty;
        this.rows.Add(row);
    }

    public void Write(TextWriter output, string indent = "  ", bool header = true)
    {
        var all = header ? this.rows.Prepend(this.headers).ToList() : this.rows;
        var widths = new int[this.headers.Length];
        foreach (var row in all)
        {
            for (var i = 0; i < row.Length; i++)
                widths[i] = Math.Max(widths[i], row[i].Length);
        }

        // Columns that are empty everywhere are left out.
        var shown = Enumerable.Range(0, widths.Length).Where(i => this.rows.Any(r => r[i].Length > 0)).ToArray();
        foreach (var row in all)
        {
            var sb = new StringBuilder(indent);
            for (var k = 0; k < shown.Length; k++)
            {
                var i = shown[k];
                var last = k == shown.Length - 1;
                var cell = row[i];
                if (this.right[i])
                    sb.Append(cell.PadLeft(widths[i]));
                else
                    sb.Append(last ? cell : cell.PadRight(widths[i]));
                if (!last)
                    sb.Append("  ");
            }

            output.WriteLine(sb.ToString().TrimEnd());
        }
    }
}
