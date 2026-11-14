using MitCue.Core.Plans;
using MitCue.Core.Reminders;
using MitCue.Core.Tests.Data;

namespace MitCue.Core.Tests.Import;

/// <summary>
/// The automatic press times of LPDU's TEA plan (plans/tea-lpdu.yaml, no times in the sheet) where TEA's mechanics need
/// more than each action's default (data/fights/tea.yaml timing fields, <see cref="TimingRules.ForEntry"/>). Each test
/// names the cells; the times are the reminders' as the overlay, <c>plan</c> and <c>check</c> use them.
/// </summary>
public class TeaPlanTimingTests
{
    private static IReadOnlyList<Reminder> Reminders(params string[] cells) =>
        new ReminderScheduler(PlanImports.Tea, Entries(cells), RepoFiles.Actions, new ReminderOptions()).All;

    private static List<PlanEntry> Entries(params string[] cells) =>
        cells.Length == 0 ? PlanImports.LpduTea.Plan.Entries : PlanImports.LpduTea.Plan.Entries.Where(e => cells.Contains(e.Src)).ToList();

    private static ReminderScheduler Scheduler(params string[] cells) =>
        new(PlanImports.Tea, Entries(cells), RepoFiles.Actions, new ReminderOptions());

    // ---- Photon: every player (the second time: both tanks) to 1 HP; the row's calls are the heal-up ---------------------

    /// <summary>
    /// 'LL/BJCC' row 40 (Photon): Indom/Reci/Deploy/Dissipation, ZoeEukProg/Physis/Kerachole/Holos, Asylum/Plenary and the
    /// extras are the heal-up after Photon sets everyone to 1 HP (4837 PhotonAOE, 0.3 s after the 262.5 cast end): every
    /// call is pressed after it, whatever its first action.
    /// </summary>
    [Fact]
    public void Photon_EveryCallIsPressedAfterTheHit()
    {
        var photon = Reminders().Where(r => r.Mechanic.Id is "p2.photon_1" or "p2.photon_2").ToList();
        Assert.Contains(photon, r => r.Entry.Src == "'LL/BJCC'!F40");
        Assert.Contains(photon, r => r.Entry.Src == "'LL/BJCC'!G40");
        Assert.All(photon, r => Assert.True(r.PressAt >= r.Hit + 1, $"{r.Entry.Id} ({r.Entry.Src}) {r.Item.Name}: press {r.PressAt}, hit {r.Hit}"));
    }

    /// <summary>
    /// 'LL/BJCC'!F40 "ZoeEukProg/Physis/Kerachole/Holos": the Sage presses Kerachole after Photon (the sheet's C32 tip:
    /// "immediately after Photon"); that is on time, not missed at the hit.
    /// </summary>
    [Fact]
    public void Photon_TheSagesKeracholeAfterTheHitIsDone()
    {
        var s = Scheduler("'LL/BJCC'!F40");
        var kerachole = Assert.Single(s.All, r => r.Item.Name == "Kerachole");
        s.Update(262.5);
        s.Update(263.0);
        Assert.Equal(ReminderState.Due, kerachole.State);
        s.OnOwnAction(kerachole.ActionIds[0], 264.0);
        Assert.Equal(ReminderState.Done, kerachole.State);
        Assert.False(kerachole.UsedEarly);
    }

    /// <summary>
    /// 'LL/BJCC'!G40 "Asylum/Plenary": Plenary Indulgence (10 s, Confession on the heal-up GCDs) is pressed after the drop
    /// to 1 HP, not 7.2 s before it with Asylum's regen lead.
    /// </summary>
    [Fact]
    public void Photon_ThePlenaryIsForTheHealUp()
    {
        var plenary = Assert.Single(Reminders("'LL/BJCC'!G40"), r => r.Item.Name == "Plenary Indulgence");
        Assert.Equal(263.5, plenary.PressAt, 1e-9);
        Assert.True(plenary.Latest >= 268.5);
    }
}
