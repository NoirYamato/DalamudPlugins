using System.Text;
using AnoMech.Scenarios.Top;

namespace AnoMech.Tests;

// Checks NoirMech's TOP values, UNVERIFIED or settled, against the pulls of a folder of ACT network logs (CLAUDE.md's
// "Pull-log report" says how to run it). The report goes to the test run's output folder, never the repo.
public class TopPullLogReport
{
    [Explicit]
    [Test]
    public void TopUnverifiedValues()
    {
        if (TestContext.Parameters.Get("PullLogs") is not { Length: > 0 } folder)
        {
            Assert.Ignore("Set the PullLogs parameter to a folder of ACT network logs.");
            return;
        }
        var logs = Directory.GetFiles(folder, "Network_*.log").Order().ToList();
        Assert.That(logs, Is.Not.Empty, $"No Network_*.log in {folder}.");

        var measures = Measures();
        var reached = new List<(string Pull, string Reached)>();
        var quiet = new List<string>();
        foreach (var pull in logs.SelectMany(log => ActLog.Pulls(log, TopZone.Instance.TerritoryId)))
        {
            if (!pull.EnemyActed)
            {
                quiet.Add(pull.ToString());
                continue;
            }
            var top = new TopPull(pull);
            reached.Add((top.Name, top.Reached));
            foreach (var measure in measures) measure.Observe(top);
        }

        var report = Write(logs, reached, quiet, measures.SelectMany(m => m.Findings()).ToList());
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "pull-logs", "top-unverified.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, report);
        TestContext.Out.WriteLine(report);
        TestContext.Out.WriteLine($"Written to {path}");
    }

    internal static ITopPullLogMeasures[] Measures() =>
    [
        new TopP3IntermissionPullLogs(),
        new TopP3HelloWorldPullLogs(),
        new TopP3MonitorsPullLogs(),
        new TopP4BlueScreenPullLogs(),
    ];

    private static string Write(List<string> logs, List<(string Pull, string Reached)> reached, List<string> quiet, List<LogFinding> findings)
    {
        var text = new StringBuilder();
        text.AppendLine("TOP pull-log report: NoirMech's values, UNVERIFIED or settled, against real pulls");
        text.AppendLine($"Logs: {string.Join(", ", logs.Select(Path.GetFileName))}");
        text.AppendLine($"TOP pulls: {reached.Count}. Furthest mechanic reached:");
        foreach (var (name, _) in TopPull.Milestones)
            if (reached.Count(r => r.Reached == name) is var n and > 0)
                text.AppendLine($"  {name,-16} {n} ({string.Join(", ", reached.Where(r => r.Reached == name).Select(r => r.Pull))})");
        if (quiet.Count > 0)
            text.AppendLine($"Left out, {quiet.Count} combat segments where no enemy cast or used an ability: {string.Join(", ", quiet)}");
        text.AppendLine();
        text.AppendLine("Times are seconds from the cast that starts the mechanic (its 20 line). Positions logged by the client trail");
        text.AppendLine("the server's for other players by a few tenths of a second and are sampled unevenly, so a distance between");
        text.AppendLine("players is good to about a yalm; a server placement (271 line) or a cast's location (263 line) is exact to");
        text.AppendLine("the packet's precision, about 0.03 y.");
        text.AppendLine();
        text.AppendLine($"{findings.Count} values: " + string.Join(", ", Enum.GetValues<Verdict>()
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

internal interface ITopPullLogMeasures
{
    void Observe(TopPull pull);
    IEnumerable<LogFinding> Findings();
}
