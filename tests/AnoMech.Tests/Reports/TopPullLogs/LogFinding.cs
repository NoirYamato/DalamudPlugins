using System.Globalization;

namespace AnoMech.Tests;

internal enum Verdict { Matches, Differs, Inconclusive, NotInTheseLogs }

// One value the report checks: where the plugin keeps it, what the sim does, what the pulls show and whether they agree.
// Details lists the samples behind the measurement, one per line.
internal sealed record LogFinding(string Section, string Name, string Constant, string Sim, string Measured, Verdict Verdict)
{
    public IReadOnlyList<string> Details { get; init; } = [];

    public static string Describe(Verdict verdict) => verdict switch
    {
        Verdict.Matches => "matches",
        Verdict.Differs => "DIFFERS",
        Verdict.Inconclusive => "inconclusive",
        _ => "not in these logs",
    };

    // Whether the mean lies within the tolerance of the sim's value.
    public static Verdict Compare(Spread measured, double sim, double tolerance)
        => measured.Count == 0 ? Verdict.NotInTheseLogs : Math.Abs(measured.Mean - sim) <= tolerance ? Verdict.Matches : Verdict.Differs;
}

// A set of measured values: their count, mean, median and range.
internal sealed class Spread(IEnumerable<double> values)
{
    private readonly double[] sorted = values.Order().ToArray();

    public int Count => sorted.Length;
    public double Mean => sorted.Length == 0 ? double.NaN : sorted.Average();
    public double Min => sorted.Length == 0 ? double.NaN : sorted[0];
    public double Max => sorted.Length == 0 ? double.NaN : sorted[^1];
    public double Median => sorted.Length == 0 ? double.NaN : sorted.Length % 2 == 1
        ? sorted[sorted.Length / 2]
        : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;

    public double StdDev => sorted.Length < 2 ? 0 : Math.Sqrt(sorted.Sum(v => (v - Mean) * (v - Mean)) / (sorted.Length - 1));

    // "14.00 y (n=40, 13.97 to 14.04, sd 0.02)"
    public string Describe(string unit, string format = "0.00")
        => Count == 0
            ? "no samples"
            : Count == 1
                ? $"{F(Mean, format)}{unit} (n=1)"
                : $"{F(Mean, format)}{unit} (n={Count}, {F(Min, format)} to {F(Max, format)}, sd {F(StdDev, format)})";

    public static string F(double value, string format = "0.00") => value.ToString(format, CultureInfo.InvariantCulture);
}
