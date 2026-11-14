using System.Text;

namespace AnoMech.Tests;

// The text of a pull-log report: its logs, the pulls found in them, how far to trust the numbers, then every finding
// under its section.
internal static class PullLogReportText
{
    public static string Write(string title, IEnumerable<string> logs, IEnumerable<string> pulls, IEnumerable<string> notes,
                               IReadOnlyList<LogFinding> findings)
    {
        var text = new StringBuilder();
        text.AppendLine(title);
        text.AppendLine($"Logs: {string.Join(", ", logs.Select(Path.GetFileName))}");
        foreach (var line in pulls) text.AppendLine(line);
        text.AppendLine();
        foreach (var line in notes) text.AppendLine(line);
        text.AppendLine();
        text.AppendLine($"{findings.Count} values: " + string.Join(", ", Enum.GetValues<Verdict>()
            .Where(v => v != Verdict.NotInTheSim || findings.Any(f => f.Verdict == v))
            .Select(v => $"{findings.Count(f => f.Verdict == v)} {LogFinding.Describe(v)}")));
        foreach (var finding in findings.Where(f => f.Verdict == Verdict.Differs))
            text.AppendLine($"  differs: [{finding.Section}] {finding.Name}");
        foreach (var section in findings.GroupBy(f => f.Section))
        {
            text.AppendLine();
            text.AppendLine($"== {section.Key} ==");
            foreach (var finding in section)
            {
                text.AppendLine();
                text.AppendLine(finding.Name);
                text.AppendLine($"  constant  {finding.Constant}");
                text.AppendLine($"  sim       {finding.Sim}");
                text.AppendLine($"  measured  {Indent(finding.Measured)}");
                text.AppendLine($"  verdict   {LogFinding.Describe(finding.Verdict)}");
                foreach (var detail in finding.Details)
                    text.AppendLine($"    - {detail}");
            }
        }
        return text.ToString();
    }

    private static string Indent(string text) => text.ReplaceLineEndings(Environment.NewLine + "            ");
}
