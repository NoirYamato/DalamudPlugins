using AnoMech.Scenarios.Tea;

namespace AnoMech.Tests;

// Checks NoirMech's TEA values, UNVERIFIED or settled, against the pulls of a folder of ACT network logs, and measures
// what the fight does that no scenario plays yet (CLAUDE.md's "Pull-log report" says how to run it). The report goes to
// the test run's output folder, never the repo.
public class TeaPullLogReport
{
    [Explicit]
    [Test]
    public void TeaUnverifiedValues()
    {
        if (TestContext.Parameters.Get("PullLogs") is not { Length: > 0 } folder)
        {
            Assert.Ignore("Set the PullLogs parameter to a folder of ACT network logs.");
            return;
        }
        var logs = Directory.GetFiles(folder, "Network_*.log").Order().ToList();
        Assert.That(logs, Is.Not.Empty, $"No Network_*.log in {folder}.");

        var measures = Measures();
        var pulls = new List<TeaPull>();
        var quiet = new List<string>();
        foreach (var pull in logs.SelectMany(log => ActLog.Pulls(log, TeaZone.Instance.TerritoryId)))
        {
            var tea = new TeaPull(pull);
            if (!tea.TheFightActed)
            {
                quiet.Add(pull.ToString());
                continue;
            }
            pulls.Add(tea);
            foreach (var measure in measures) measure.Observe(tea);
        }

        var report = PullLogReportText.Write("TEA pull-log report: NoirMech's values, UNVERIFIED or settled, against real pulls",
                                             logs, PullsFound(pulls, quiet), Notes, measures.SelectMany(m => m.Findings()).ToList());
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "pull-logs", "tea-unverified.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, report);
        TestContext.Out.WriteLine(report);
        TestContext.Out.WriteLine($"Written to {path}");
    }

    internal static ITeaPullLogMeasures[] Measures() =>
    [
        new TeaP1TimelinePullLogs(),
        new TeaP1ActorsPullLogs(),
        new TeaP1ProteanPullLogs(),
        new TeaP1HandPullLogs(),
        new TeaP1DollsPullLogs(),
        new TeaP1DrainagePullLogs(),
        new TeaP1LimitCutPullLogs(),
        new TeaP1DeathsPullLogs(),
    ];

    private static readonly string[] Notes =
    [
        "Times are seconds from the game's combat flag coming up (the 260 line), or from the cast a row names. A pull the",
        "log joined late is left out of the times from the flag. A boss's own position and facing on a cast (263 line), an",
        "ability or a move is the server's, exact to about 0.03 y; so is a server placement (271 line). Positions of the",
        "players are the logging client's: its own run a little ahead of the server's and the others' trail it by a few",
        "tenths of a second, so a distance between players, or a player's bearing from a boss a few yalms away, is good to",
        "about a yalm. Hit points are read from the last line that carries them, up to a second old.",
    ];

    private static IEnumerable<string> PullsFound(List<TeaPull> pulls, List<string> quiet)
    {
        yield return $"TEA pulls: {pulls.Count}.";
        foreach (var pull in pulls)
            yield return $"  {pull.Name}: {pull.Lasted}{(pull.LoggedFromItsStart ? "" : ", joined after its start")}, reached {pull.Reached}";
        if (quiet.Count > 0)
            yield return $"Left out, {quiet.Count} combat segments where nothing of the fight cast or used an ability: {string.Join(", ", quiet)}";
    }
}

internal interface ITeaPullLogMeasures
{
    void Observe(TeaPull pull);
    IEnumerable<LogFinding> Findings();
}
