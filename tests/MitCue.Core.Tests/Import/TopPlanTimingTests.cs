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
    /// The cells that list a 10 s call with other calls on Wave Cannon 1 and 2: the DRK's 'WAR/DRK'!C11 "Shadow Wall +
    /// Oblation + TBN" and 'DRK/GNB'!B12 "Rampart + Dark Mind + Oblation + TBN", and the Reprisal tank's 'P6'!C4 party
    /// mitigation + Reprisal. Only the 10 s calls wait for the Wild Charge, 0.5 s before the first protean; the others keep
    /// their own press, which already reaches it or is not meant for it (The Blackest Night, 7 s), so that a DRK's three or
    /// four presses do not all turn due in the last 2 s before the first protean.
    /// </summary>
    [Theory]
    [InlineData("'WAR/DRK'!C11", 1244.3, "Oblation", "Shadow Wall=-4.5;The Blackest Night=-4.5")]
    [InlineData("'DRK/GNB'!B12", 1278.2, "Dark Mind,Oblation", "Rampart=-6;The Blackest Night=-6")]
    [InlineData("'P6'!C4", 1244.3, "Reprisal", "Divine Veil=-4;Shake It Off=-4;Dark Missionary=-4.5;Heart of Light=-4.5")]
    public void WaveCannon_OnlyTheTenSecondCallsOfACellWaitForTheWildCharge(string cell, double wildCharge, string waiting, string others)
    {
        var calls = Reminders(cell);
        foreach (var r in calls.Where(r => waiting.Split(',').Contains(r.Item.Name)))
        {
            Assert.Equal(r.Hit - 0.5, r.PressAt, 1e-9);
            Assert.True(NowCue(r) + 10 >= wildCharge + 0.5 - 1e-9, $"{cell} {r.Item.Name}: from the NOW cue ({NowCue(r)}) it ends at {NowCue(r) + 10}");
        }

        var own = others.Split(';').Select(o => o.Split('=')).ToDictionary(o => o[0], o => double.Parse(o[1], System.Globalization.CultureInfo.InvariantCulture));
        var rest = calls.Where(r => !waiting.Split(',').Contains(r.Item.Name)).ToList();
        Assert.Equal(own.Keys.Order(), rest.Select(r => r.Item.Name!).Distinct().Order());
        Assert.All(rest, r => Assert.Equal(own[r.Item.Name!], r.PressAt - r.Hit, 1e-9));
    }

    /// <summary>
    /// Limitless Synergy ('P1/P2'!B11/C11/H11 party mitigation, 'General Tank Mit til P5'!B6/C6 tank cooldowns, "15 sec Mit
    /// is used as the clone jumps"): the 15 s calls cover the flares (309.7) and the Cosmo Memory raidwide itself (7B24,
    /// 320.2), 1.4 s after the 7B22 cast end (318.8): pressed at 320.2 − 15 + 2, so that even on the NOW cue they last
    /// 0.5 s past it.
    /// </summary>
    [Fact]
    public void Limitless_FifteenSecondMitigationLastsThroughTheCosmoMemoryRaidwide()
    {
        Assert.Equal([309.7, 320.2], PlanImports.Top.GetMechanic("p2.limitless")!.Hits);
        var fifteen = Reminders("'P1/P2'!B11", "'P1/P2'!C11", "'P1/P2'!H11", "'General Tank Mit til P5'!B6", "'General Tank Mit til P5'!C6")
            .Where(r => r.Mechanic.Id == "p2.limitless" && RepoFiles.Action(r.Item.Name!).DurationAt(90) == 15).ToList();
        Assert.Contains(fifteen, r => r.Item.Name == "Dark Missionary");
        Assert.Contains(fifteen, r => r.Item.Name == "Troubadour");
        Assert.Contains(fifteen, r => r.Item.Name == "Shadow Wall");
        Assert.All(fifteen, r =>
        {
            Assert.Equal(307.2, r.PressAt, 1e-9);
            Assert.True(NowCue(r) + 15 >= 320.2 + 0.5 - 1e-9, $"{r.Entry.Src} {r.Item.Name}: from the NOW cue ({NowCue(r)}) it ends at {NowCue(r) + 15}");
        });
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
