using MitCue.Core.Reminders;
using MitCue.PluginTests.Support;
using MitCue.Runtime;

namespace MitCue.PluginTests;

/// <summary>What the overlay shows: live rows, sample rows for positioning, and finished rows.</summary>
public class OverlayModelTests
{
    private static EncounterSession HeldAfterCountdown()
    {
        // A pre-pull Divine Benison (press 1 s before the pull) is still NOW when the countdown reaches 0.
        var session = Kit.Session(Kit.Fight(), [Kit.Entry("p1.raid", -31, Kit.DivineBenison, "Divine Benison")], new ScriptedSyncEngine());
        for (var clock = 990.0; clock < 999.85; clock += 0.1)
            session.Update(clock, (float)(1000 - clock), false, null);
        session.Update(1000.0, null, false, null);
        return session;
    }

    private static OverlayModel Build(Configuration config, EncounterSession? live)
    {
        var model = new OverlayModel();
        model.Build(config, live, null, 1, Kit.GameData(), _ => null);
        return model;
    }

    [Fact]
    public void AlwaysShowKeepsRealPrePullCallsBetweenCountdownZeroAndThePull()
    {
        // Review finding: with "Always show" on, the held pre-pull calls gave way to the fake sample rows.
        var session = HeldAfterCountdown();
        Assert.Equal(SessionMode.Idle, session.Mode);

        var model = Build(new Configuration { AlwaysShowOverlay = true }, session);

        Assert.Equal(OverlaySource.Live, model.Source);
        Assert.True(model.Visible);
        var row = Assert.Single(model.Rows);
        Assert.Equal(ReminderState.Due, row.State);
        Assert.Equal("Divine Benison", row.Labels.Name);
    }

    [Fact]
    public void AlwaysShowFallsBackToSampleRowsWhenNothingIsListed()
    {
        var session = Kit.Session(Kit.Fight(), [Kit.Entry("p1.raid", -5, Kit.Temperance, "Temperance")], new ScriptedSyncEngine());
        session.Update(10, null, false, null);

        var model = Build(new Configuration { AlwaysShowOverlay = true }, session);

        Assert.Equal(OverlaySource.Sample, model.Source);
        Assert.NotEmpty(model.Rows);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AStoppedLiveSessionSaysWhyInsteadOfVanishing(bool alwaysShow)
    {
        // Review finding: after a session error mid-pull the overlay disappeared (or turned into sample rows).
        var model = new OverlayModel();
        model.Build(new Configuration { AlwaysShowOverlay = alwaysShow }, null, null, 1, Kit.GameData(), _ => null, "Reminders stopped.");

        Assert.Equal(OverlaySource.Error, model.Source);
        Assert.True(model.Visible);
        Assert.Equal("Reminders stopped.", model.Message);
        Assert.Empty(model.Rows);
    }

    [Fact]
    public void WithoutAlwaysShowTheHeldCallsAreShownToo()
    {
        var model = Build(new Configuration(), HeldAfterCountdown());

        Assert.Equal(OverlaySource.Live, model.Source);
        Assert.Single(model.Rows);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DoneAndMissedRowsAreListedOnlyWhileTrackingDoneMissed(bool track)
    {
        var session = Kit.Session(
            Kit.Fight(),
            [
                Kit.Entry("p1.raid", -5, Kit.Temperance, "Temperance"), // press 25, pressed at 29
                Kit.Entry("p1.raid", -4, Kit.DivineBenison, "Divine Benison"), // press 26, never pressed: missed after 29.7
                Kit.Entry("p1.raid2", -20, Kit.PlenaryIndulgence, "Plenary Indulgence"), // press 40, upcoming from 25
            ],
            new ScriptedSyncEngine());
        var driver = new SessionDriver(session, 1000);
        driver.CombatEdge(true);
        driver.RunTo(1029);
        session.OnOwnUse(Kit.Temperance, driver.Clock);
        driver.RunTo(1030.5);

        var model = Build(new Configuration { TrackDoneMissed = track }, session);

        var states = model.Rows.Select(r => r.State).ToList();
        if (track)
            Assert.Equal([ReminderState.Done, ReminderState.Missed, ReminderState.Upcoming], states);
        else
            Assert.Equal([ReminderState.Upcoming], states);
    }

    [Fact]
    public void OwnRowsAreNeverPushedOutByHiddenFinishedRows()
    {
        var session = Kit.Session(
            Kit.Fight(),
            [
                Kit.Entry("p1.raid", -5, Kit.Temperance, "Temperance"), // press 25, done at 25
                Kit.Entry("p1.raid2", -20, Kit.PlenaryIndulgence, "Plenary Indulgence"), // press 40
                Kit.Entry("p1.raid2", -22, Kit.DivineBenison, "Divine Benison"), // press 38
            ],
            new ScriptedSyncEngine());
        var driver = new SessionDriver(session, 1000);
        driver.CombatEdge(true);
        driver.RunTo(1025);
        session.OnOwnUse(Kit.Temperance, driver.Clock);
        driver.RunTo(1026);

        var model = Build(new Configuration { TrackDoneMissed = false, MaxRows = 2 }, session);

        Assert.Equal(2, model.Rows.Count);
        Assert.All(model.Rows, r => Assert.Equal(ReminderState.Upcoming, r.State));
    }
}
