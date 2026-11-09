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
        ITopPullLogMeasures[] measures =
            [new TopP3IntermissionPullLogs(), new TopP3HelloWorldPullLogs(), new TopP3MonitorsPullLogs(), new TopP4BlueScreenPullLogs()];
        foreach (var measure in measures) measure.Observe(new TopPull(pull));

        var findings = measures.SelectMany(m => m.Findings()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(findings.Select(f => f.Verdict), Is.All.EqualTo(Verdict.NotInTheseLogs));
            Assert.That(findings.Select(f => f.Measured), Has.None.Contains("NaN"));
        });
    }
}
