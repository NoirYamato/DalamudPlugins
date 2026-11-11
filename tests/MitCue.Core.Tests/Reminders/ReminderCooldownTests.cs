using MitCue.Core.Plans;
using MitCue.Core.Reminders;

namespace MitCue.Core.Tests.Reminders;

/// <summary>Cooldown/resource awareness and when-ready entries.</summary>
public class ReminderCooldownTests
{
    private static readonly uint TemperanceId = Kit.Temperance.Id;

    /// <summary>Temperance at the 100 s raidwide with the default offset: press 94, window [81, 99.7], due from 92.5.</summary>
    private static PlanEntry Temperance() => Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Temperance));

    // ---- ReadyIn / MissingResources ------------------------------------------------------------

    [Theory]
    [InlineData(84, 20, 10)]       // ready at 104: 10 s after the press time
    [InlineData(84, 10.3, 0.3)]
    [InlineData(90, 4.3, 0.3)]
    [InlineData(93, 3, 2)]         // due, still before the press time
    public void ReadyIn_IsHowLateTheActionIsAgainstThePressTime(double t, double secondsUntilReady, double expected)
    {
        var s = Kit.Scheduler(Temperance());
        s.Update(t, new FakeCooldowns().Set(Kit.Temperance, secondsUntilReady));
        Kit.Close(expected, s.All[0].ReadyIn);
    }

    [Theory]
    [InlineData(84, 10.2)]         // 0.2 s late: below the 0.25 s threshold
    [InlineData(84, 10.25)]        // exactly the threshold
    [InlineData(84, 3)]            // ready well before the press time
    [InlineData(84, 0)]
    public void ReadyIn_IsNullWhenTheActionIsReadyInTime(double t, double secondsUntilReady)
    {
        var s = Kit.Scheduler(Temperance());
        s.Update(t, new FakeCooldowns().Set(Kit.Temperance, secondsUntilReady));
        Assert.Null(s.All[0].ReadyIn);
    }

    [Fact]
    public void ReadyIn_AfterThePressTime_IsTheRemainingCooldown()
    {
        var s = Kit.Scheduler(Temperance());
        var r = s.All[0];
        var cooldowns = new FakeCooldowns();

        s.Update(96, cooldowns.Set(Kit.Temperance, 0));    // 2 s past the press time but ready: no warning
        Assert.Null(r.ReadyIn);
        s.Update(96, cooldowns.Set(Kit.Temperance, 2));
        Kit.Close(2, r.ReadyIn);
        s.Update(96, cooldowns.Set(Kit.Temperance, 0.2));
        Assert.Null(r.ReadyIn);
    }

    [Fact]
    public void ReadyIn_IsNullWithoutCooldownInformation()
    {
        var s = Kit.Scheduler(Temperance());
        var r = s.All[0];

        s.Update(84, new FakeCooldowns().Set(Kit.Temperance, 20));
        Assert.NotNull(r.ReadyIn);
        s.Update(85, new FakeCooldowns());                 // unknown action
        Assert.Null(r.ReadyIn);
        s.Update(86, new FakeCooldowns().Set(Kit.Temperance, 20));
        Assert.NotNull(r.ReadyIn);
        s.Update(87);                                      // no provider
        Assert.Null(r.ReadyIn);
    }

    [Fact]
    public void MissingResources_ComesFromTheProvider()
    {
        var s = Kit.Scheduler(Temperance());
        var r = s.All[0];
        var cooldowns = new FakeCooldowns();

        cooldowns.Resources[TemperanceId] = false;
        s.Update(90, cooldowns);
        Assert.True(r.MissingResources);

        cooldowns.Resources[TemperanceId] = true;
        s.Update(90.5, cooldowns);
        Assert.False(r.MissingResources);

        cooldowns.Resources[TemperanceId] = false;
        s.Update(91, cooldowns);
        cooldowns.Resources[TemperanceId] = null;
        s.Update(91.5, cooldowns);
        Assert.False(r.MissingResources);

        cooldowns.Resources[TemperanceId] = false;
        s.Update(92, cooldowns);
        s.Update(92.5);
        Assert.False(r.MissingResources);
    }

    [Fact]
    public void OnlyVisibleActionReminders_AreQueried()
    {
        var s = Kit.Scheduler(Temperance(), Kit.Entry("p1.raidwide", null, Kit.Note("Stack")), Kit.Entry("p1.buster", null, Kit.Do(Kit.Rampart)));
        var cooldowns = new FakeCooldowns();
        cooldowns.Resources[Kit.Rampart.Id] = false;

        s.Update(50, cooldowns);                           // everything pending
        Assert.Equal(0, cooldowns.SecondsQueries);
        Assert.Equal(0, cooldowns.ResourceQueries);

        s.Update(90, cooldowns);                           // Temperance and the note upcoming; Rampart (press 124) pending
        Assert.Equal(1, cooldowns.SecondsQueries);
        Assert.Equal(1, cooldowns.ResourceQueries);
        Assert.False(s.All[2].MissingResources);
        Assert.False(s.All[1].MissingResources);
        Assert.Null(s.All[1].ReadyIn);
    }

    [Fact]
    public void EitherOr_ChecksItsFirstOption()
    {
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", null, Kit.Either(Kit.DivineBenison, Kit.Temperance)));
        var r = s.All[0];                                  // timed by Divine Benison: press 96
        var cooldowns = new FakeCooldowns().Set(Kit.DivineBenison, 30).Set(Kit.Temperance, 0);
        cooldowns.Resources[Kit.DivineBenison.Id] = false;

        s.Update(90, cooldowns);

        Kit.Close(24, r.ReadyIn);                          // 30 − (96 − 90), from Divine Benison
        Assert.True(r.MissingResources);
    }

    [Fact]
    public void FinishedReminders_DropTheirCooldownInformation()
    {
        var s = Kit.Scheduler(Temperance(), Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Temperance)));
        var (first, second) = (s.All[0], s.All[1]);
        var cooldowns = new FakeCooldowns().Set(Kit.Temperance, 20);
        cooldowns.Resources[TemperanceId] = false;

        s.Update(90, cooldowns);
        Assert.All(s.All, r => Kit.Close(16, r.ReadyIn));
        Assert.All(s.All, r => Assert.True(r.MissingResources));

        s.OnOwnAction(TemperanceId, 91);                   // satisfies the first one
        Assert.Equal(ReminderState.Done, first.State);
        Assert.Null(first.ReadyIn);
        Assert.False(first.MissingResources);

        s.Update(99.8, cooldowns);
        Assert.Equal(ReminderState.Missed, second.State);
        Assert.Null(second.ReadyIn);
        Assert.False(second.MissingResources);

        var queries = cooldowns.SecondsQueries;
        s.Update(100, cooldowns);
        Assert.Equal(queries, cooldowns.SecondsQueries);   // finished reminders are not queried
    }

    // ---- when_ready entries --------------------------------------------------------------------

    [Fact]
    public void WhenReady_StaysDueWhileOnCooldown_ThenGetsTheLateWindowOnceReady()
    {
        var s = Kit.Scheduler(Temperance().AsWhenReady());
        var due = Kit.RecordDue(s);
        var r = s.All[0];
        var cooldowns = new FakeCooldowns();

        s.Update(90, cooldowns.Set(Kit.Temperance, 30));
        Assert.Equal(ReminderState.Upcoming, r.State);
        Kit.Close(26, r.ReadyIn);
        s.Update(93, cooldowns);
        Assert.Equal(ReminderState.Due, r.State);

        s.Update(r.Latest + 1, cooldowns.Set(Kit.Temperance, 20));
        Assert.Equal(ReminderState.Due, r.State);          // past latest, still on cooldown
        Kit.Close(20, r.ReadyIn);
        s.Update(115, cooldowns.Set(Kit.Temperance, 5));
        Assert.Equal(ReminderState.Due, r.State);

        s.Update(120, cooldowns.Set(Kit.Temperance, 0));   // ready: max(99.7 − 120, 5) = 5 s more
        Assert.Equal(ReminderState.Due, r.State);
        Assert.Null(r.ReadyIn);
        s.Update(125, cooldowns);
        Assert.Equal(ReminderState.Due, r.State);

        s.Update(125.01, cooldowns);
        Assert.Equal(ReminderState.Missed, r.State);
        Assert.Equal(125.01, r.FinishedAt);
        Assert.Equal([r], due);
    }

    [Fact]
    public void WhenReady_ReadyShortlyBeforeLatest_GetsTheLateWindow()
    {
        var s = Kit.Scheduler(Temperance().AsWhenReady());
        var r = s.All[0];
        var cooldowns = new FakeCooldowns();

        s.Update(95, cooldowns.Set(Kit.Temperance, 3));
        s.Update(98, cooldowns.Set(Kit.Temperance, 0));    // max(99.7 − 98, 5): due until 103
        s.Update(102.9, cooldowns);
        Assert.Equal(ReminderState.Due, r.State);
        s.Update(103.01, cooldowns);
        Assert.Equal(ReminderState.Missed, r.State);
    }

    [Fact]
    public void WhenReady_ReadyAllAlong_IsMissedAtLatest()
    {
        var s = Kit.Scheduler(Temperance().AsWhenReady());
        var r = s.All[0];
        var cooldowns = new FakeCooldowns().Set(Kit.Temperance, 0);

        s.Update(90, cooldowns);
        s.Update(95, cooldowns);
        s.Update(r.Latest, cooldowns);
        Assert.Equal(ReminderState.Due, r.State);
        s.Update(r.Latest + 0.01, cooldowns);
        Assert.Equal(ReminderState.Missed, r.State);
    }

    [Fact]
    public void WhenReady_ReadyStreakRestartsWhenTheActionGoesBackOnCooldown()
    {
        var s = Kit.Scheduler(Temperance().AsWhenReady());
        var r = s.All[0];
        var cooldowns = new FakeCooldowns();

        s.Update(90, cooldowns.Set(Kit.Temperance, 0));
        s.Update(95, cooldowns.Set(Kit.Temperance, 5));    // e.g. used for something else
        s.Update(101, cooldowns.Set(Kit.Temperance, 0));   // ready again: due until 106
        s.Update(105.9, cooldowns);
        Assert.Equal(ReminderState.Due, r.State);
        s.Update(106.1, cooldowns);
        Assert.Equal(ReminderState.Missed, r.State);
    }

    [Fact]
    public void WhenReady_WithoutAProvider_IsMissedAtLatest()
    {
        var s = Kit.Scheduler(Temperance().AsWhenReady());
        var r = s.All[0];

        s.Update(95);
        s.Update(r.Latest);
        Assert.Equal(ReminderState.Due, r.State);
        s.Update(r.Latest + 0.01);
        Assert.Equal(ReminderState.Missed, r.State);
    }

    [Fact]
    public void WhenReady_WithAnUnknownCooldown_IsMissedAtLatest()
    {
        var s = Kit.Scheduler(Temperance().AsWhenReady());
        var r = s.All[0];
        var cooldowns = new FakeCooldowns();               // does not know Temperance

        s.Update(95, cooldowns);
        s.Update(r.Latest, cooldowns);
        Assert.Equal(ReminderState.Due, r.State);
        s.Update(r.Latest + 0.01, cooldowns);
        Assert.Equal(ReminderState.Missed, r.State);
    }

    [Fact]
    public void WhenReady_UnknownCooldownKeepsWaiting()
    {
        var s = Kit.Scheduler(Temperance().AsWhenReady());
        var r = s.All[0];

        s.Update(98, new FakeCooldowns().Set(Kit.Temperance, 5));
        s.Update(120, new FakeCooldowns().Set(Kit.Temperance, null));
        s.Update(121);
        Assert.Equal(ReminderState.Due, r.State);
    }

    [Fact]
    public void WhenReady_UseWhileWaiting_IsDone()
    {
        var s = Kit.Scheduler(Temperance().AsWhenReady());
        var r = s.All[0];

        s.Update(98, new FakeCooldowns().Set(Kit.Temperance, 10));
        s.Update(105, new FakeCooldowns().Set(Kit.Temperance, 2));
        s.OnOwnAction(TemperanceId, 107);

        Assert.Equal(ReminderState.Done, r.State);
        Assert.Equal(107, r.UsedAt);
        Assert.False(r.UsedEarly);
        Assert.Same(r, s.RecentUses[^1].Matched);
    }

    [Fact]
    public void WhenReady_UseAfterTheLateWindow_IsIgnored()
    {
        var s = Kit.Scheduler(Temperance().AsWhenReady());
        var r = s.All[0];
        var cooldowns = new FakeCooldowns();

        s.Update(103, cooldowns.Set(Kit.Temperance, 2));
        s.Update(105, cooldowns.Set(Kit.Temperance, 0));   // due until 110
        s.Update(109, cooldowns);
        s.OnOwnAction(TemperanceId, 110.5);

        Assert.Equal(ReminderState.Due, r.State);
        Assert.Null(s.RecentUses[^1].Matched);
        s.Update(110.5, cooldowns);
        Assert.Equal(ReminderState.Missed, r.State);
    }

    [Fact]
    public void NotWhenReady_CooldownDoesNotExtendTheWindow()
    {
        var s = Kit.Scheduler(Temperance());
        var r = s.All[0];
        var cooldowns = new FakeCooldowns().Set(Kit.Temperance, 30);

        s.Update(95, cooldowns);
        s.Update(r.Latest + 0.01, cooldowns);
        Assert.Equal(ReminderState.Missed, r.State);
    }

    [Fact]
    public void WhenReady_OptionalEntry_IsPassedAfterTheLateWindow()
    {
        var s = Kit.Scheduler(Temperance().AsWhenReady().AsOptional());
        var r = s.All[0];

        s.Update(99, new FakeCooldowns().Set(Kit.Temperance, 10));
        s.Update(110, new FakeCooldowns().Set(Kit.Temperance, 0));
        Assert.Equal(ReminderState.Due, r.State);
        s.Update(115.01, new FakeCooldowns().Set(Kit.Temperance, 0));
        Assert.Equal(ReminderState.Passed, r.State);
    }

    [Fact]
    public void WhenReady_BigStepPastLatestWhileOnCooldown_BecomesDue()
    {
        var s = Kit.Scheduler(Temperance().AsWhenReady());
        var due = Kit.RecordDue(s);
        var r = s.All[0];

        s.Update(0);
        s.Update(150, new FakeCooldowns().Set(Kit.Temperance, 10));

        Assert.Equal(ReminderState.Due, r.State);
        Assert.Equal([r], due);
    }

    [Fact]
    public void WhenReady_Note_IsPassedAtLatest()
    {
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", null, Kit.Note("Stack")).AsWhenReady());
        s.Update(99.8, new FakeCooldowns());
        Assert.Equal(ReminderState.Passed, s.All[0].State);
    }
}
