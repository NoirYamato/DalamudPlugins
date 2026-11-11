using MitCue.Game;
using MitCue.PluginTests.Support;

namespace MitCue.PluginTests;

/// <summary>"Record pulls" records pulls: in a supported fight's zone or a duty, never in towns or the open world.</summary>
public class PullRecorderTests
{
    [Theory]
    [InlineData(true, false, false, false)] // town, housing, open world
    [InlineData(true, true, false, true)] // any duty
    [InlineData(true, false, true, true)] // a supported fight's zone
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)] // setting off
    public void RecordsOnlyFightsAndDuties(bool enabled, bool boundByDuty, bool fightZone, bool expected)
    {
        Assert.Equal(expected, PullRecorder.ShouldRecord(enabled, boundByDuty, fightZone));
    }

    [Fact]
    public void VisitingTownsWithRecordingOnWritesNoFile()
    {
        // Review finding: with "Record pulls" on, every zone visit opened a recording of nearby players' actions.
        var config = Kit.TempDirectory();
        using var recorder = new PullRecorder(config, Kit.Errors(), "test");
        foreach (var town in new uint[] { 129, 132 })
            recorder.Update(PullRecorder.ShouldRecord(true, boundByDuty: false, fightZone: false), town, 10, "English");

        Assert.False(recorder.Active);
        Assert.False(Directory.Exists(recorder.Directory) && Directory.EnumerateFiles(recorder.Directory).Any());

        recorder.Update(PullRecorder.ShouldRecord(true, boundByDuty: true, fightZone: true), 1122, 20, "English");
        Assert.True(recorder.Active);
        Assert.StartsWith("1122-", Path.GetFileName(recorder.CurrentFile));
    }
}
