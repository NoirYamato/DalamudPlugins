using MitCue.Core.Plans;
using MitCue.Core.Reminders;
using MitCue.Core.Tests.Data;

namespace MitCue.Core.Tests.Import;

/// <summary>
/// The automatic press times of LPDU's TOP plan (plans/top-lpdu.yaml, no times in the sheet) where an entry needs more
/// than its first action's default (<see cref="TimingRules.ForEntry"/>). Each test names the cells; the times are the
/// reminders' as the overlay, <c>plan</c> and <c>check</c> use them.
/// </summary>
public class TopPlanTimingTests
{
    private static IReadOnlyList<Reminder> Reminders(params string[] cells) =>
        new ReminderScheduler(PlanImports.Top, PlanImports.Lpdu.Plan.Entries.Where(e => cells.Contains(e.Src)).ToList(), RepoFiles.Actions, new ReminderOptions()).All;

    private static double NowCue(Reminder r) => TimingRules.DueAt(r.PressAt, r.DueNotBefore, new ReminderOptions());

    /// <summary>
    /// Wave Cannon 1 and 2 in P6 (proteans at 1236.0/1238.0 and 1269.9/1271.9, the Wild Charge 8.3 s after the first, at
    /// 1244.3 and 1278.2): the 10 s calls are for the Wild Charge, 100-220k on the front player (the sheet's note: "Press 10s
    /// Mit ... to catch both Proteans and the Stack ... never too early or you won't catch the Stack!"). The tank invulns
    /// ('WAR/DRK'!B11 Holmgang, 'WAR/PLD'!C12 Hallowed Ground), the Feint ('P6'!I5) and the Reprisals ('P6'!B5) are pressed
    /// 0.5 s before the first protean, and turn due only when a press still lasts 0.5 s past the Wild Charge; a press on
    /// the NOW cue is on time, and the reminder stays due at least a second.
    /// </summary>
    [Theory]
    [InlineData("'WAR/DRK'!B11", "Holmgang", 1244.3)]
    [InlineData("'WAR/PLD'!C12", "Hallowed Ground", 1278.2)]
    [InlineData("'P6'!I5", "Feint", 1278.2)]
    [InlineData("'P6'!B5", "Reprisal", 1278.2)]
    public void WaveCannon_TenSecondCallsLastThroughTheWildCharge(string cell, string action, double wildCharge)
    {
        var calls = Reminders(cell).Where(r => r.Item.Name == action).ToList();
        Assert.NotEmpty(calls);
        foreach (var r in calls)
        {
            Assert.Equal(10, RepoFiles.Action(action).DurationAt(90));
            Assert.Equal(r.Hit - 0.5, r.PressAt, 1e-9);
            Assert.True(NowCue(r) + 10 >= wildCharge + 0.5 - 1e-9, $"{cell} {action}: from the NOW cue ({NowCue(r)}) it ends at {NowCue(r) + 10}");
            Assert.True(r.Latest - NowCue(r) >= 1, $"{cell} {action}: due from {NowCue(r)} to {r.Latest}");

            var s = new ReminderScheduler(PlanImports.Top, [r.Entry], RepoFiles.Actions, new ReminderOptions());
            var mine = s.All[r.ItemIndex];
            s.Update(NowCue(r));
            Assert.Equal(ReminderState.Due, mine.State);
            s.OnOwnAction(mine.ActionIds[0], NowCue(r));
            Assert.Equal((ReminderState.Done, false), (mine.State, mine.UsedEarly));
        }
    }

    /// <summary>
    /// 'P3'!E4 "CU + Neutralsect" on Latent Defect 2 (485.4) and 'P6'!E5 "CU + Neutralsect shields for proteans" on Wave
    /// Cannon 2 (proteans 1269.9 and 1271.9): Neutral Sect's 4.5 s lead counts although CU comes first, time for the Neutral
    /// Aspected Helios to put up its barrier; CU pressed then still lasts through the second hit or protean.
    /// </summary>
    [Fact]
    public void NeutralSectAfterCu_KeepsItsLead()
    {
        foreach (var (cell, secondHit) in new[] { ("'P3'!E4", 487.9), ("'P6'!E5", 1271.9) })
        {
            var entry = Reminders(cell);
            var neutral = Assert.Single(entry, r => r.Item.Name == "Neutral Sect");
            var cu = Assert.Single(entry, r => r.Item.Name == "Collective Unconscious");
            Assert.Equal(neutral.Hit - 4.5, neutral.PressAt, 1e-9);
            Assert.Equal(neutral.PressAt, cu.PressAt, 1e-9);
            Assert.True(NowCue(cu) + 10 >= secondHit + 0.5, $"{cell}: CU from the NOW cue ({NowCue(cu)}) ends at {NowCue(cu) + 10}");
            Assert.True(cu.Earliest <= NowCue(cu));
        }
    }
}
