using MitCue.Core.Reminders;
using MitCue.Data;
using MitCue.PluginTests.Support;

namespace MitCue.PluginTests;

/// <summary>Settings defaults (DESIGN D5, §7) and the ranges the runtime relies on.</summary>
public class ConfigurationTests
{
    [Fact]
    public void DefaultsFollowTheDesign()
    {
        var config = new Configuration();

        Assert.Equal(15f, config.Lookahead);
        Assert.Equal(1.5f, config.DueLead);
        Assert.Equal(5, config.MaxRows);
        Assert.True(config.SoundEnabled);
        Assert.Equal(6, config.SoundEffect);
        Assert.False(config.ShowOtherAssignees);
        Assert.False(config.ShowSkipped);
        Assert.True(config.TrackDoneMissed);
        Assert.True(config.PostPullSummary);
        Assert.False(config.OverlayLocked);
    }

    [Fact]
    public void SanitizeKeepsTheDueLeadWithinTheLookahead()
    {
        var config = new Configuration { Lookahead = 5, DueLead = 8 };
        config.Sanitize();

        Assert.Equal(5f, config.Lookahead);
        Assert.Equal(5f, config.DueLead);
    }

    [Fact]
    public void ApplyToNeverGivesTheSchedulerADueLeadLongerThanTheLookahead()
    {
        // A live edit (no Sanitize) leaving due lead 8 with lookahead 5.
        var config = new Configuration { Lookahead = 5, DueLead = 8 };
        var options = new ReminderOptions();
        config.ApplyTo(options);

        Assert.Equal(5, options.Lookahead);
        Assert.Equal(5, options.DueLead);
    }

    [Fact]
    public void APressMadeWhenNowShowsCountsEvenWithADueLeadAboveTheLookahead()
    {
        // Review finding: lookahead 5 s and due lead 8 s made a reminder turn NOW 8 s early, while presses only count
        // from 5 s before; the press made at NOW was ignored and the reminder ended missed.
        var config = new Configuration { Lookahead = 5, DueLead = 8 };
        var scheduler = new ReminderScheduler(
            Kit.Fight(),
            [Kit.Entry("p1.raid2", -6, Kit.Temperance, "Temperance")], // press 54
            EmptyActionCatalog.Instance,
            config.BuildReminderOptions());
        var reminder = Assert.Single(scheduler.All);

        double? nowShownAt = null;
        for (var t = 40.0; t < 62 && nowShownAt == null; t += 0.1)
        {
            scheduler.Update(t);
            if (reminder.State == ReminderState.Due)
            {
                nowShownAt = t;
                scheduler.OnOwnAction(Kit.Temperance, t);
            }
        }

        Assert.NotNull(nowShownAt);
        Assert.Equal(ReminderState.Done, reminder.State);
        scheduler.Update(70);
        Assert.Equal(0, scheduler.Summarize().Missed);
    }

    [Fact]
    public void ClampValuesRepairsTypedOutOfRangeValues()
    {
        // Ctrl+click on an ImGui slider accepts any typed value.
        var config = new Configuration
        {
            Lookahead = 100,
            DueLead = -5,
            OverlayScale = 0,
            OverlayOpacity = float.NaN,
            MaxRows = 0,
            SoundEffect = 40,
        };
        config.ClampValues();

        Assert.Equal(Configuration.MaxLookahead, config.Lookahead);
        Assert.Equal(0f, config.DueLead);
        Assert.Equal(Configuration.MinOverlayScale, config.OverlayScale);
        Assert.Equal(0.85f, config.OverlayOpacity);
        Assert.Equal(Configuration.MinRows, config.MaxRows);
        Assert.Equal(Configuration.MaxSoundEffect, config.SoundEffect);
    }

    [Fact]
    public void LoweringTheLookaheadBelowTheDueLeadLowersTheDueLead()
    {
        var config = new Configuration { Lookahead = 15, DueLead = 8 };
        config.Lookahead = 4;
        config.ClampValues();

        Assert.Equal(4f, config.DueLead);
    }
}
