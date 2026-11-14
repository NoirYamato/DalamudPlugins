namespace AnoMech.Tests;

public class TeaPullLogReportTests
{
    [Test]
    public void WithNoPullNothingIsSettled()
    {
        var findings = TeaPullLogReport.Measures().SelectMany(m => m.Findings()).ToList();

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
        var findings = TeaPullLogReport.Measures().SelectMany(m => m.Findings()).ToList();

        Assert.Multiple(() =>
        {
            foreach (var finding in findings)
            {
                Assert.That(finding.Constant, Does.Not.Contain(":?"), finding.Name);
                Assert.That(finding.Sim, Does.Not.Contain("not found in the"), finding.Name);
            }
        });
    }
}
