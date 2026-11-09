using System.Numerics;
using AnoMech.Core.UserActions;
using static AnoMech.Tests.ActLogLines;

namespace AnoMech.Tests;

public class TopPullLogReportTests
{
    [Test]
    public void APullThatNeverReachesP3SettlesNothing()
    {
        var pull = new ActPull { File = "Network_test.log", Number = 1, Start = Origin, Jobs = new() { [0x10000001] = (uint)JobId.WhiteMage } };
        pull.Lines.AddRange(new[] { Cast(1, 0x40000010, 0x7B0C, 2.7f), StatusList(2, 0x10000001, 1000, new Vector2(100f, 110f)) }
                                .Select(l => ActLine.Parse(l)!));
        var measures = TopPullLogReport.Measures();
        foreach (var measure in measures) measure.Observe(new TopPull(pull));

        var findings = measures.SelectMany(m => m.Findings()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(findings.Select(f => f.Verdict), Is.All.EqualTo(Verdict.NotInTheseLogs));
            Assert.That(findings.Select(f => f.Measured), Has.None.Contains("NaN"));
        });
    }

    // Each finding finds its line in the plugin's source by text: a sim edit that rewords or drops that text fails here,
    // naming the finding, rather than leaving the report to print "file:?".
    [Test]
    public void EveryFindingFindsItsLineInTheSource()
    {
        var findings = TopPullLogReport.Measures().SelectMany(m => m.Findings()).ToList();

        Assert.Multiple(() =>
        {
            foreach (var finding in findings)
            {
                Assert.That(finding.Constant, Does.Not.Contain(":?"), finding.Name);
                Assert.That(finding.Name, Does.Not.Contain(":?"), finding.Name);
                Assert.That(finding.Sim, Does.Not.Contain("not found in the scenario"), finding.Name);
            }
        });
    }
}
