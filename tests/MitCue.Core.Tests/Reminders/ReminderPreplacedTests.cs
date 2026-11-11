using MitCue.Core.Plans;
using MitCue.Core.Reminders;

namespace MitCue.Core.Tests.Reminders;

/// <summary>
/// Heals placed before the damage (actions.yaml <c>when: before</c>: Liturgy of the Bell, Excogitation, Earthly Star,
/// Horoscope) and actions used after it (<c>when: after</c>) in the scheduler.
/// </summary>
public class ReminderPreplacedTests
{
    [Theory]
    [InlineData(25862u, "p1.raidwide", 94.0, 81.0, 99.7)]    // Liturgy of the Bell: 20 s → 6 s before
    [InlineData(7434u, "p1.buster", 122.0, 86.0, 129.7)]     // Excogitation: 45 s → 8 s before, on time from 44 s before
    [InlineData(7439u, "p1.raidwide", 90.0, 81.0, 99.7)]     // Earthly Star: 10 s lead to mature
    [InlineData(16557u, "p1.raidwide", 97.0, 71.0, 99.7)]    // Horoscope: 3 s before, Horoscope Helios lasts 30 s
    [InlineData(208u, "p1.raidwide", 102.0, 92.0, 107.0)]    // Pulse of Life: healer LB, after the damage
    public void WithoutOffset_TheSideOfTheHitComesFromWhen(uint actionId, string mechanic, double pressAt, double earliest, double latest)
    {
        var r = Assert.Single(Kit.Scheduler(Kit.Entry(mechanic, null, PlanItem.ForAction(actionId, "x"))).All);
        Assert.Equal(pressAt, r.PressAt, Kit.Tolerance);
        Assert.Equal(earliest, r.Earliest, Kit.Tolerance);
        Assert.Equal(latest, r.Latest, Kit.Tolerance);
    }

    [Fact]
    public void ExplicitOffset_KeepsThePreplacedWindow()
    {
        var r = Assert.Single(Kit.Scheduler(Kit.Entry("p1.raidwide", -15, Kit.Do(Kit.Bell))).All); // "Bell (1:49)" well before the hit
        Assert.Equal(85, r.PressAt, Kit.Tolerance);
        Assert.Equal(81, r.Earliest, Kit.Tolerance);
        Assert.Equal(99.7, r.Latest, Kit.Tolerance);
    }

    [Fact]
    public void Excogitation_PlacedLongBeforeTheBuster_IsDoneOnTime_NotEarly_NeverMissed()
    {
        // Buster at 130: press 122, on time from 86 (it waits up to 45 s for the tank to drop below 50 %).
        var s = Kit.Scheduler(Kit.Entry("p1.buster", null, Kit.Do(Kit.Excogitation)));
        var due = Kit.RecordDue(s);
        var r = s.All[0];

        s.Update(95);
        s.OnOwnAction(Kit.Excogitation.Id, 100);           // 30 s before the buster, before the reminder is even shown

        Assert.Equal(ReminderState.Done, r.State);
        Assert.False(r.UsedEarly);
        for (var t = 100.0; t <= 150; t += 0.5)
            s.Update(t);
        Assert.Equal(ReminderState.Done, r.State);
        Assert.Empty(due);
        var summary = s.Summarize();
        Assert.Equal((1, 1, 0), (summary.Total, summary.Done, summary.Missed));
    }

    [Theory]
    [InlineData(81.0, false)]   // the bell lasts 20 s: placed 19 s before, it still catches the hit
    [InlineData(90.0, false)]
    [InlineData(99.7, false)]   // the last moment before the damage
    [InlineData(80.99, true)]   // would expire with the hit: counted, but flagged early
    [InlineData(79.0, true)]    // the start of the matching window (press − lookahead)
    public void Bell_UseBeforeTheHit_IsDone(double usedAt, bool early)
    {
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Bell))); // press 94, window [81, 99.7]
        var r = s.All[0];

        s.OnOwnAction(Kit.Bell.Id, usedAt);

        Assert.Equal(ReminderState.Done, r.State);
        Assert.Equal(early, r.UsedEarly);
    }

    [Fact]
    public void Bell_NotPlacedBeforeTheDamage_IsMissedAtTheHit_NotAfterAHealWindow()
    {
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Bell)));
        var due = Kit.RecordDue(s);
        var r = s.All[0];

        s.Update(92.4);
        Assert.Equal(ReminderState.Upcoming, r.State);
        s.Update(92.5);                                    // due 1.5 s before the press time (94)
        Assert.Equal(ReminderState.Due, r.State);
        Assert.Equal([r], due);
        s.Update(99.7);
        Assert.Equal(ReminderState.Due, r.State);

        s.Update(99.71);
        Assert.Equal(ReminderState.Missed, r.State);
        s.OnOwnAction(Kit.Bell.Id, 100.5);                 // a bell after the damage does not count for it
        Assert.Null(s.RecentUses[^1].Matched);
    }

    [Fact]
    public void Bell_UsedTooEarly_IsIgnored_AndTheReminderStillComes()
    {
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Bell)));
        var r = s.All[0];

        s.OnOwnAction(Kit.Bell.Id, 78.99);                 // gone by 98.99: does not cover the hit
        Assert.Null(Assert.Single(s.RecentUses).Matched);
        s.Update(93);
        Assert.Equal(ReminderState.Due, r.State);
    }

    [Fact]
    public void EarthlyStar_PlacedLate_IsStillDoneBeforeTheHit()
    {
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", null, Kit.Do(Kit.EarthlyStar))); // press 90, due from 88.5
        var due = Kit.RecordDue(s);
        var r = s.All[0];

        s.Update(88.5);
        Assert.Equal(ReminderState.Due, r.State);
        s.OnOwnAction(Kit.EarthlyStar.Id, 92);             // 2 s late: it matures at 102 and is detonated after the hit
        Assert.Equal(ReminderState.Done, r.State);
        Assert.False(r.UsedEarly);
        Assert.Equal([r], due);
    }

    [Fact]
    public void Horoscope_WithHelios_PlacedEarly_IsNotEarly()
    {
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Horoscope))); // press 97, on time from 71
        var r = s.All[0];

        s.OnOwnAction(Kit.Horoscope.Id, 75);
        Assert.Equal(ReminderState.Done, r.State);
        Assert.False(r.UsedEarly);
    }

    [Fact]
    public void PreplacedHeal_WithAHealAfterTheHit_EachItemKeepsItsSide()
    {
        // "Bell + Tetragrammaton": the entry is timed by its first action (Bell, 6 s before); the heal keeps its late window.
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", null, Kit.Do(Kit.Bell), Kit.Do(Kit.Tetragrammaton)));
        var (bell, tetra) = (s.All[0], s.All[1]);

        Assert.Equal(94, bell.PressAt, Kit.Tolerance);
        Assert.Equal(94, tetra.PressAt, Kit.Tolerance);
        Assert.Equal(99.7, bell.Latest, Kit.Tolerance);
        Assert.Equal(99, tetra.Latest, Kit.Tolerance);     // press + 5
    }

    [Fact]
    public void HealerLimitBreak_IsUsableUntilTheLateWindowAfterThePress()
    {
        var s = Kit.Scheduler(Kit.Entry("p1.raidwide", null, Kit.Do(Kit.PulseOfLife))); // press 102, window [92, 107]
        var r = s.All[0];

        s.Update(101);
        Assert.Equal(ReminderState.Due, r.State);
        s.Update(107);
        Assert.Equal(ReminderState.Due, r.State);
        s.OnOwnAction(Kit.PulseOfLife.Id, 106.5);
        Assert.Equal(ReminderState.Done, r.State);
    }
}
