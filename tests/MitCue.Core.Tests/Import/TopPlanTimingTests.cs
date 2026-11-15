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
