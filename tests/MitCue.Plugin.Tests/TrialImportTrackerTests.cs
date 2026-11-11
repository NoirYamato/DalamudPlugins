using MitCue.Import;

namespace MitCue.PluginTests;

/// <summary>When the Options step's trial import (the tab check that picks the default tabs and columns) runs.</summary>
public class TrialImportTrackerTests
{
    private const string Key = "top|1";

    [Fact]
    public void RunsOncePerSourceAndFight()
    {
        var trial = new TrialImportTracker();
        Assert.True(trial.ShouldStart(Key, busy: false));
        Assert.False(trial.ShouldStart(Key, busy: true));

        trial.Start(Key);
        Assert.True(trial.Running);
        Assert.False(trial.ShouldStart(Key, busy: false));
        Assert.True(trial.IsCurrent(Key));

        trial.Finished(Key);
        Assert.False(trial.Running);
        Assert.False(trial.ShouldStart(Key, busy: false));
        Assert.True(trial.ShouldStart("tea|1", busy: false));
    }

    [Fact]
    public void RunsAgainWhenTheWindowClosedWhileItRan()
    {
        // Review finding: closing the window during the tab check left it marked as done, so the player's default tabs
        // and columns were never applied and "Import and review" imported every tab and column.
        var trial = new TrialImportTracker();
        trial.Start(Key);

        trial.JobCancelled(byPlayer: false);

        Assert.False(trial.Running);
        Assert.False(trial.IsCurrent(Key));
        Assert.False(trial.CancelledByPlayer(Key));
        Assert.True(trial.ShouldStart(Key, busy: false));
    }

    [Fact]
    public void WaitsForThePlayerAfterTheyCancelledIt()
    {
        // A Cancel that restarted the check at once would do nothing; the window offers "Check the tabs" instead.
        var trial = new TrialImportTracker();
        trial.Start(Key);

        trial.JobCancelled(byPlayer: true);

        Assert.True(trial.CancelledByPlayer(Key));
        Assert.False(trial.ShouldStart(Key, busy: false));
        Assert.True(trial.ShouldStart("tea|1", busy: false));

        trial.Retry();
        Assert.False(trial.CancelledByPlayer(Key));
        Assert.True(trial.ShouldStart(Key, busy: false));
    }

    [Fact]
    public void CancellingAnotherJobLeavesTheCheckAlone()
    {
        var trial = new TrialImportTracker();
        trial.Start(Key);
        trial.Finished(Key);

        trial.JobCancelled(byPlayer: true); // e.g. the import, not the tab check

        Assert.False(trial.CancelledByPlayer(Key));
        Assert.False(trial.ShouldStart(Key, busy: false));
        Assert.True(trial.IsCurrent(Key));
    }
}
