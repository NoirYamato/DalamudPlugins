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
    /// 'LL/BJCC'!G54 "Benison (Tank)/Benediction" on Double Rocket Punch (374.8): the second Photon (366.8) left both tanks
    /// at 1 HP, so the Benediction tops the tank up before the buster, with the Benison, instead of a second after it; it
    /// turns due only after Photon's drop (0.3 s after the cast end).
    /// </summary>
    [Fact]
    public void DoubleRocketPunch_TheBenedictionTopsTheTankUpBeforeTheBuster()
    {
        var g54 = Reminders("'LL/BJCC'!G54");
        var benediction = Assert.Single(g54, r => r.Item.Name == "Benediction");
        var benison = Assert.Single(g54, r => r.Item.Name == "Divine Benison");
        Assert.Equal(370.8, benediction.PressAt, 1e-9);
        Assert.Equal(benison.PressAt, benediction.PressAt, 1e-9);
        var photon2 = PlanImports.Tea.GetMechanic("p2.photon_2")!;
        Assert.Equal("Photon (tanks to 1 HP)", photon2.Name);
        Assert.True(benediction.PressAt - new ReminderOptions().DueLead > photon2.Hit + 0.3);
        Assert.True(benediction.PressAt < benediction.Hit);

        // The row's Excogitation is placed for the buster and keeps its own lead.
        Assert.Equal(366.8, Assert.Single(Reminders("'LL/BJCC'!E54"), r => r.Item.Name == "Excogitation").PressAt, 1e-9);
    }

    // ---- cells that list several actions -------------------------------------------------------------------------

    /// <summary>
    /// 'LL/BJCC'!E38 "Bless/Whisper/Succor" on Whirlwind 1 (238.3): Fey Blessing is a heal, but the cell is the Scholar's
    /// heal and shield for the raidwide: pressed with Whispering Dawn's lead (−6.3 s), so the Succor (2 s cast) lands
    /// before the hit. 'Alex Prime/PA'!E45 "Reci/Deploy/Soil/Fey Illum" on Irresistible Grace 1 (1142.5): Recitation is a
    /// buff, the combo needs Fey Illumination's lead (−6 s).
    /// </summary>
    [Fact]
    public void SeveralActions_AHealOrABuffFirst_IsPressedWithTheEarliestPreHitAction()
    {
        var e38 = Reminders("'LL/BJCC'!E38");
        Assert.Equal(["Fey Blessing", "Whispering Dawn", "Succor"], e38.Select(r => r.Item.Name));
        Assert.All(e38, r => Assert.Equal(238.3 - 6.3, r.PressAt, 1e-9));
        Assert.All(Reminders("'Alex Prime/PA'!E45"), r => Assert.Equal(1142.5 - 6, r.PressAt, 1e-9));

        // An entry that starts with mitigation keeps its lead: the Paladin's Reprisal in "Party Mit/Reprisal" on Optical
        // Sight is not moved earlier by the Divine Veil (it would end before the second hit).
        var reprisal = Assert.Single(Reminders("'Alex Prime/PA'!D39"), r => r.Item.Name == "Reprisal" && r.Entry.Who.Job == Job.PLD);
        Assert.True(reprisal.PressAt >= reprisal.Hit - 3, $"press {reprisal.PressAt}");
    }

    // ---- the J Waves: 3 s apart, a series ------------------------------------------------------------------------

    /// <summary>
    /// "Jwaves (N)" rows are for wave N+1, at N stacks ('Alex Prime/PA' rows 21, 23, 25: waves 5, 7 and 9 at 722.9, 728.9,
    /// 734.9). Their calls are pressed after wave N (+0.3 s), and turn due only then: shields are not spent on wave N, 15-20 s
    /// mitigation keeps its last waves. A Succor's 2 s cast may start 1.7 s before wave N: it lands after it.
    /// </summary>
    [Theory]
    [InlineData("'Alex Prime/PA'!G21", "Temperance", 720.2, 720.2)]          // −6 s would be 716.9, wave 3
    [InlineData("'Alex Prime/PA'!C21", "Divine Veil", 720.2, 720.2)]         // instant shields
    [InlineData("'Alex Prime/PA'!F21", "Eukrasian Prognosis", 720.2, 720.2)]
    [InlineData("'Alex Prime/PA'!H21", "Neutral Sect", 720.2, 720.2)]
    [InlineData("'Alex Prime/PA'!E21", "Succor", 718.4, 718.2)]              // its own −4.5 s lands at 720.4
    [InlineData("'Alex Prime/PA'!E23", "Consolation", 726.2, 726.2)]
    [InlineData("'Alex Prime/PA'!F23", "Eukrasian Prognosis", 726.2, 726.2)]
    [InlineData("'Alex Prime/PA'!M23", "Passage of Arms", 726.9, 726.2)]     // its −2 s is after wave 6 anyway
    [InlineData("'Alex Prime/PA'!F25", "Eukrasian Prognosis", 732.2, 732.2)]
    [InlineData("'Alex Prime/PA'!E25", "Succor", 730.4, 730.2)]
    public void JWaves_CallsForAWaveArePressedAfterTheWaveBeforeIt(string cell, string action, double press, double dueFrom)
    {
        var s = Scheduler(cell);
        var r = Assert.Single(s.All, x => x.Item.Name == action);
        Assert.Equal(press, r.PressAt, 1e-9);
        Assert.Equal(dueFrom, r.DueNotBefore!.Value, 1e-9);

        s.Update(dueFrom - 0.05);
        Assert.Equal(ReminderState.Upcoming, r.State);
        s.Update(Math.Max(dueFrom, press - new ReminderOptions().DueLead));
        Assert.Equal(ReminderState.Due, r.State);
    }

    /// <summary>'Alex Prime/PA'!G19 "Asylum" on "Jwaves (0-3)", wave 1: J Storm, 5.2 s before it, is no wave of the series.</summary>
    [Fact]
    public void JWaves_TheFirstWaveKeepsItsLead()
    {
        var asylum = Assert.Single(Reminders("'Alex Prime/PA'!G19"));
        Assert.Equal(710.9 - 7.2, asylum.PressAt, 1e-9);
        Assert.Null(asylum.DueNotBefore);
    }

    // ---- actions with a lead of their own (actions.yaml) -------------------------------------------------------

    /// <summary>
    /// 'LL/BJCC'!H48 "Neutral" on Whirlwind 2 (327.6) and 'Alex Prime/PA'!H47 "Neutral/CU" on Irresistible Grace 2 (1187.9):
    /// Neutral Sect is pressed 4.5 s before the hit, in time for the Neutral Aspected Helios (1.5 s cast) to put up its
    /// barrier before the damage.
    /// </summary>
    [Fact]
    public void NeutralSect_LeavesTimeForTheHeliosItsBarrierComesWith()
    {
        var neutral = Reminders("'LL/BJCC'!H48", "'Alex Prime/PA'!H47").Where(r => r.Item.Name == "Neutral Sect").ToList();
        Assert.Equal(2, neutral.Count);
        Assert.All(neutral, r => Assert.Equal(r.Hit - 4.5, r.PressAt, 1e-9));
    }

    /// <summary>
    /// "PLD Wings" in Extras with the Paladin's own calls for the same hit ('LL/BJCC' C11/M11 Cascade and C54/M54 Double
    /// Rocket Punch as MT; 'Alex Prime/PA' D23/M23 J Wave 7 and D47/M47 Irresistible Grace 2 as OT): Passage of Arms is a
    /// channel any other action ends, so it is the Paladin's last press before the hit, after the Reprisal and Divine Veil.
    /// </summary>
    [Theory]
    [InlineData("MT", "p1.cascade_1", "p2.double_rocket_punch")]
    [InlineData("OT", "p3.j_wave_7", "p4.irresistible_grace_2")]
    public void PassageOfArms_IsThePaladinsLastPressBeforeTheHit(string slot, string first, string second)
    {
        var plan = PlanImports.LpduTea.Plan;
        var me = ColumnSuggestion.Context(ColumnSuggestion.Suggest(plan, Job.PLD, slot), Job.PLD, slot, 80);
        var mine = new ReminderScheduler(PlanImports.Tea, PlanSelection.For(plan, me).ToList(), RepoFiles.Actions, new ReminderOptions()).All;
        foreach (var mechanic in new[] { first, second })
        {
            var atHit = mine.Where(r => r.Mechanic.Id == mechanic).ToList();
            var wings = Assert.Single(atHit, r => r.Item.Name == "Passage of Arms");
            var others = atHit.Where(r => r != wings).ToList();
            Assert.NotEmpty(others);
            Assert.All(others, r => Assert.True(r.PressAt < wings.PressAt, $"{mechanic}: {r.Item.Name} at {r.PressAt}, Passage of Arms at {wings.PressAt}"));
            Assert.True(wings.PressAt < wings.Hit);
        }
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
