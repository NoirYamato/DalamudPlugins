using MitCue.Core.Actions;
using MitCue.Core.Import;
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

    /// <summary>A TEA sheet of one pasted tab ("phase\tcolumn" then "row\tcell" lines), imported with the repository's data.</summary>
    private static ImportResult Import(string tsv) =>
        new PlanImporter().Import(
            new SheetDocument { Title = "Test sheet", Tabs = [SheetReaders.FromDelimited(tsv.ReplaceLineEndings("\n"), '\t', "Pasted")] },
            new ImportOptions { Fight = PlanImports.Tea, Actions = RepoFiles.Actions, Aliases = RepoFiles.Aliases, ImportedDate = PlanImports.Date });

    /// <summary>The (press, NOW cue) of a reminder with the default options, to 1e-6 s.</summary>
    private static (double Press, double Due) PressAndDue(Reminder r) =>
        (Math.Round(r.PressAt, 6), Math.Round(TimingRules.DueAt(r.PressAt, r.DueNotBefore, new ReminderOptions()), 6));

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
    [InlineData("'Alex Prime/PA'!H21", "Neutral Sect", 718.7, 718.7)]          // its Helios (1.5 s) lands at 720.2
    [InlineData("'Alex Prime/PA'!E21", "Succor", 718.4, 718.2)]              // its own −4.5 s lands at 720.4
    [InlineData("'Alex Prime/PA'!E21", "Sacred Soil", 720.2, 720.2)]         // instant: not with the Succor's cast lead
    [InlineData("'Alex Prime/PA'!E21", "Summon Seraph", 720.2, 720.2)]
    [InlineData("'Alex Prime/PA'!E21", "Consolation", 720.2, 720.2)]
    [InlineData("'Alex Prime/PA'!E23", "Consolation", 726.2, 726.2)]
    [InlineData("'Alex Prime/PA'!F23", "Eukrasian Prognosis", 726.2, 726.2)]
    [InlineData("'Alex Prime/PA'!M23", "Passage of Arms", 726.9, 726.2)]     // its −2 s is after wave 6 anyway
    [InlineData("'Alex Prime/PA'!G23", "Plenary Indulgence", 727.9, 726.2)] // waits for its recast (written -1 s), keeps the floor
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

    /// <summary>
    /// An Astrologian's "Earthly Star + Neutral Sect" on J Wave 5 (722.9), either way round, as a sheet may write it (LPDU
    /// has 'Alex Prime/PA'!H21 "Neutral" alone), and a healers' cell with Temperance or Excogitation: the other action
    /// times the cell, but Neutral Sect is still pressed and due at 718.7, so that the Helios it gives a barrier lands
    /// after wave 4 (719.9) and before wave 5; the other action waits for wave 4 + 0.3 s.
    /// </summary>
    [Theory]
    [InlineData("Neutral Sect", "Earthly Star")]
    [InlineData("Earthly Star", "Neutral Sect")]
    [InlineData("Temperance", "Neutral Sect")]
    [InlineData("Neutral Sect", "Temperance")]
    [InlineData("Excogitation", "Neutral Sect")]
    [InlineData("Neutral Sect", "Excogitation")]
    public void JWave5_NeutralSectBesideAnActionThatTimesTheCell_IsDueAt7187(string first, string second)
    {
        var fight = PlanImports.Tea;
        var entry = new PlanEntry
        {
            Mechanic = "p3.j_wave_5",
            Who = new WhoSpec { Job = Job.AST },
            Do = [PlanItem.ForAction(RepoFiles.Action(first).Id, first), PlanItem.ForAction(RepoFiles.Action(second).Id, second)],
        };
        var s = new ReminderScheduler(fight, [entry], RepoFiles.Actions, new ReminderOptions());
        var neutral = Assert.Single(s.All, r => r.Item.Name == "Neutral Sect");
        var other = Assert.Single(s.All, r => r.Item.Name != "Neutral Sect");
        Assert.Equal(718.7, neutral.PressAt, 1e-9);
        Assert.Equal(718.7, TimingRules.DueAt(neutral.PressAt, neutral.DueNotBefore, new ReminderOptions()), 1e-9);
        Assert.Equal(720.2, other.PressAt, 1e-9);
    }

    /// <summary>
    /// 'Alex Prime/PA'!E21 "Succor/Soil/Seraph" ("Seraph" = Summon Seraph → Consolation) on J Wave 5: the Succor's cast may
    /// start before wave 4 (719.9), the instant Soil, Summon Seraph and Consolation may not: a Consolation woven before
    /// wave 4 is spent on it, and is early.
    /// </summary>
    [Fact]
    public void JWaves_TheInstantItemsBesideACastShieldWaitForTheWaveBeforeIt()
    {
        var s = Scheduler("'Alex Prime/PA'!E21");
        var succor = Assert.Single(s.All, r => r.Item.Name == "Succor");
        var consolation = Assert.Single(s.All, r => r.Item.Name == "Consolation");
        s.Update(718.4);
        Assert.Equal((ReminderState.Due, ReminderState.Upcoming), (succor.State, consolation.State));
        s.OnOwnAction(succor.ActionIds[0], 718.4);
        s.OnOwnAction(consolation.ActionIds[0], 718.9);
        Assert.False(succor.UsedEarly);
        Assert.True(consolation.UsedEarly);
    }

    /// <summary>
    /// A Scholar's buff with the Succor or Adloquium it changes on J Wave 5 (722.9), as a sheet may write it (LPDU's E21 has
    /// none): "Reci/Succor", "Reci → Succor", "Succor/Reci", "Emergency Tactics/Succor", "Swiftcast/Succor" and
    /// "Reci/Adlo/Deploy". The buff has nothing wave 4 could spend: it comes with the cast, both pressed at 718.4 and due at
    /// 718.2, so that the cast starts before wave 4 (719.9) and lands after it; Deployment Tactics, which spreads the
    /// Adloquium's shield, waits for wave 4 + 0.3 s.
    /// </summary>
    [Theory]
    [InlineData(false, "Recitation", "Succor")]
    [InlineData(true, "Recitation", "Succor")]
    [InlineData(false, "Succor", "Recitation")]
    [InlineData(false, "Emergency Tactics", "Succor")]
    [InlineData(false, "Swiftcast", "Succor")]
    [InlineData(false, "Recitation", "Adloquium", "Deployment Tactics")]
    public void JWave5_ABuffIsPressedWithTheCastItChanges(bool seq, params string[] cell)
    {
        var options = new ReminderOptions();
        var entry = new PlanEntry { Mechanic = "p3.j_wave_5", Who = new WhoSpec { Job = Job.SCH }, Seq = seq, Do = [.. cell.Select(a => PlanItem.ForAction(RepoFiles.Action(a).Id, a))] };
        var s = new ReminderScheduler(PlanImports.Tea, [entry], RepoFiles.Actions, options);
        Assert.Equal(cell.Length, s.All.Count);
        foreach (var r in s.All)
        {
            var (press, due) = r.Item.Name == "Deployment Tactics" ? (720.2, 720.2) : (718.4, 718.2);
            Assert.Equal(press, r.PressAt, 1e-9);
            Assert.Equal(due, TimingRules.DueAt(r.PressAt, r.DueNotBefore, options), 1e-9);
        }

        s.Update(718.2);
        Assert.All(s.All.Where(r => r.Item.Name != "Deployment Tactics"), r => Assert.Equal(ReminderState.Due, r.State));
        var buff = Assert.Single(s.All, r => r.Item.Name is "Recitation" or "Emergency Tactics" or "Swiftcast");
        var cast = Assert.Single(s.All, r => r.Item.Name is "Succor" or "Adloquium");
        s.OnOwnAction(buff.ActionIds[0], 718.2);
        s.OnOwnAction(cast.ActionIds[0], 718.3);
        Assert.Equal((false, false), (buff.UsedEarly, cast.UsedEarly));
    }

    /// <summary>
    /// "Stack middle → Succor" on J Wave 5, a sequence: the note has nothing wave 4 could spend, so it comes with the Succor,
    /// both pressed at 718.4 and due at 718.2, and does not hold the Succor back until after wave 4 (720.2), when its 2 s
    /// cast would land 0.7 s before wave 5.
    /// </summary>
    [Fact]
    public void JWave5_ANoteStepComesWithTheSuccor()
    {
        var entry = new PlanEntry
        {
            Mechanic = "p3.j_wave_5",
            Who = new WhoSpec { Job = Job.SCH },
            Seq = true,
            Do = [PlanItem.ForNote("Stack middle"), PlanItem.ForAction(RepoFiles.Action("Succor").Id, "Succor")],
        };
        var s = new ReminderScheduler(PlanImports.Tea, [entry], RepoFiles.Actions, new ReminderOptions());
        Assert.Equal(2, s.All.Count);
        Assert.All(s.All, r => Assert.Equal((718.4, 718.2), PressAndDue(r)));
    }

    /// <summary>
    /// "Recitation > Succor" on J Wave 5 and "Recitation > Adloquium" on J Wave 7: the importer reads the second Recitation,
    /// planned while the first lasts, as the note "Recitation (still active)", which as the first step does not hold the
    /// Adloquium back either: both pressed at 724.4 and due at 724.2, the cast landing between waves 6 and 7.
    /// </summary>
    [Fact]
    public void JWave7_AStillActiveNoteStepComesWithTheAdloquium()
    {
        var r = Import("P3\tScholar\nJwaves (4)\tRecitation > Succor\nJwaves (6)\tRecitation > Adloquium");
        var wave7 = Assert.Single(r.Plan.Entries, e => e.Mechanic == "p3.j_wave_7");
        Assert.True(wave7.Seq);
        Assert.Equal("Recitation (still active)", wave7.Do[0].Note);
        var s = new ReminderScheduler(PlanImports.Tea, [wave7], RepoFiles.Actions, new ReminderOptions());
        Assert.Equal(2, s.All.Count);
        Assert.All(s.All, x => Assert.Equal((724.4, 724.2), PressAndDue(x)));
    }

    /// <summary>
    /// "Indom → Succor" on J Wave 5, a sequence: the Indomitability heals after wave 4 (720.2); the Succor, which alone could
    /// be cast from 718.4 on, comes after it and turns due with it, as the cell orders them.
    /// </summary>
    [Fact]
    public void JWave5_TheStepsOfASequenceComeInTheCellsOrder()
    {
        var options = new ReminderOptions();
        var entry = new PlanEntry
        {
            Mechanic = "p3.j_wave_5",
            Who = new WhoSpec { Job = Job.SCH },
            Seq = true,
            Do = [PlanItem.ForAction(RepoFiles.Action("Indomitability").Id, "Indomitability"), PlanItem.ForAction(RepoFiles.Action("Succor").Id, "Succor")],
        };
        var s = new ReminderScheduler(PlanImports.Tea, [entry], RepoFiles.Actions, options);
        Assert.Equal(["Indomitability", "Succor"], s.All.Select(r => r.Item.Name));
        Assert.All(s.All, r => Assert.Equal((720.2, 720.2), (Math.Round(r.PressAt, 6), Math.Round(TimingRules.DueAt(r.PressAt, r.DueNotBefore, options), 6))));
    }

    /// <summary>'Alex Prime/PA'!G19 "Asylum" on "Jwaves (0-3)", wave 1: J Storm, 5.2 s before it, is no wave of the series.</summary>
    [Fact]
    public void JWaves_TheFirstWaveKeepsItsLead()
    {
        var asylum = Assert.Single(Reminders("'Alex Prime/PA'!G19"));
        Assert.Equal(710.9 - 7.2, asylum.PressAt, 1e-9);
        Assert.Null(asylum.DueNotBefore);
    }

    // ---- Inception: the crystals explode at ~564.5-568.5 ----------------------------------------------------------

    /// <summary>
    /// 'Alex Prime/PA' row 11 "Inception (Crystals Boom)": the 15 s party mitigation (C11/D11 Dark Missionary and Heart of
    /// Light, K11 Troubadour, Tactician, Shield Samba) is pressed for the first explosion (564.5), so even pressed on the
    /// NOW cue it lasts through the fourth (~568.5), not for the crystal drop at 558.2.
    /// </summary>
    [Fact]
    public void InceptionCrystals_FifteenSecondMitigationLastsThroughTheFourExplosions()
    {
        var dueLead = new ReminderOptions().DueLead;
        var mitigation = Reminders("'Alex Prime/PA'!C11", "'Alex Prime/PA'!D11", "'Alex Prime/PA'!K11")
            .Where(r => r.Item.Name is "Dark Missionary" or "Heart of Light" or "Troubadour" or "Tactician" or "Shield Samba").ToList();
        Assert.Equal(7, mitigation.Count);
        foreach (var r in mitigation)
        {
            var duration = RepoFiles.Action(r.Item.Name!).DurationAt(80)!.Value;
            Assert.Equal(15, duration);
            Assert.Equal(564.5, r.Hit, 1e-9);
            Assert.True(r.PressAt < r.Hit);
            Assert.True(r.PressAt - dueLead + duration >= 568.5, $"{r.Item.Name}: pressed on NOW at {r.PressAt - dueLead}, ends at {r.PressAt - dueLead + duration}");
        }
    }

    // ---- several hits ---------------------------------------------------------------------------------------------

    /// <summary>
    /// 10 s mitigation on rows that cover a mechanic's later hits: 'Alex Prime/PA' row 39 on Optical Sight (938.6, 944.7)
    /// and row 43 on Fate Calibration β (1081.5, 1082.5, 1088.6), 'LL/BJCC' row 23 on Protean 2 (109.1, 112.1, 115.1) and
    /// row 19 on the first Splash (81.1, then five more 1.1 s apart, the fight file's sub_hits: no timeline entries).
    /// Pressed so that even on the NOW cue it lasts 0.5 s past the last hit it can reach, and still before the first.
    /// </summary>
    [Theory]
    [InlineData("p4.optical_sight", 944.7)]
    [InlineData("p4.fate_beta", 1088.6)]
    [InlineData("p1.protean_2", 115.1)]
    [InlineData("p1.splash_1", 86.6)]
    public void SeveralHits_TenSecondMitigationLastsThroughTheLaterHit(string mechanic, double lastHit)
    {
        var dueLead = new ReminderOptions().DueLead;
        var tenSeconds = Reminders().Where(r => r.Mechanic.Id == mechanic && r.Item.Action is { } id && RepoFiles.Actions.Get(id) is { Kind: ActionKind.PartyMit } a && a.DurationAt(80) == 10).ToList();
        Assert.NotEmpty(tenSeconds);
        Assert.All(tenSeconds, r =>
        {
            Assert.True(r.PressAt - dueLead + 10 >= lastHit + 0.5 - 1e-9, $"{r.Entry.Src} {r.Item.Name}: from the NOW cue ({r.PressAt - dueLead}) it ends at {r.PressAt - dueLead + 10}");
            Assert.True(r.PressAt <= r.Hit - 0.5 + 1e-9, $"{r.Entry.Src} {r.Item.Name}: press {r.PressAt}, first hit {r.Hit}");
        });
    }

    /// <summary>
    /// 'LL/BJCC' row 19 on the first Splash (six raidwide hits, 81.1 to 86.6): the 10 s Reprisal, Feint, Addle and Dismantle
    /// are pressed 2.5 s before the first, so that from the NOW cue they last 0.5 s past the sixth, and a use before that
    /// cue is early; the 15 s calls keep their −4.5 s, which reaches the sixth anyway, and only count as early before
    /// 86.6 − 15 + 0.5.
    /// </summary>
    [Fact]
    public void Splash1_TheCallsLastThroughTheSixthSplash()
    {
        Assert.Equal([82.2, 83.3, 84.4, 85.5, 86.6], PlanImports.Tea.GetMechanic("p1.splash_1")!.SubHits);
        var options = new ReminderOptions();
        var row = Reminders("'LL/BJCC'!C19", "'LL/BJCC'!J19", "'LL/BJCC'!L19", "'LL/BJCC'!M19", "'LL/BJCC'!K19", "'LL/BJCC'!D19");
        var ten = row.Where(r => RepoFiles.Action(r.Item.Name!).DurationAt(80) == 10).ToList();
        Assert.Equal(["Addle", "Dismantle", "Feint", "Reprisal"], ten.Select(r => r.Item.Name!).Distinct().Order());
        Assert.All(ten, r =>
        {
            Assert.Equal(81.1 - 2.5, r.PressAt, 1e-9);
            Assert.Equal(77.1, TimingRules.DueAt(r.PressAt, r.DueNotBefore, options), 1e-9);
            Assert.Equal(77.1, r.Earliest, 1e-9);
        });

        var fifteen = row.Where(r => r.Item.Name is "Troubadour" or "Tactician" or "Dark Missionary" or "Heart of Light").ToList();
        Assert.Equal(4, fifteen.Count);
        Assert.All(fifteen, r =>
        {
            Assert.Equal(81.1 - 4.5, r.PressAt, 1e-9);
            Assert.Equal(86.6 - 15 + 0.5, r.Earliest, 1e-9);
        });
    }

    // ---- actions with a lead of their own (actions.yaml) -------------------------------------------------------

    /// <summary>
    /// 'LL/BJCC'!H48 "Neutral" on Whirlwind 2 (327.6) and 'Alex Prime/PA'!H47 "Neutral/CU" on Irresistible Grace 2 (1187.9):
    /// Neutral Sect is pressed 4.5 s before the hit, in time for the Neutral Aspected Helios (1.5 s cast) to put up its
    /// barrier before the damage. 'Alex Prime/PA'!H21 "Neutral" on J Wave 5 (722.9): pressed and due 1.5 s before wave 4
    /// (719.9) + 0.3 s, so that the Helios cast from its NOW cue puts the barrier up after wave 4, and even after a whole
    /// 2.5 s GCD before it, before wave 5.
    /// </summary>
    [Fact]
    public void NeutralSect_LeavesTimeForTheHeliosItsBarrierComesWith()
    {
        var neutral = Reminders("'LL/BJCC'!H48", "'Alex Prime/PA'!H47").Where(r => r.Item.Name == "Neutral Sect").ToList();
        Assert.Equal(2, neutral.Count);
        Assert.All(neutral, r => Assert.Equal(r.Hit - 4.5, r.PressAt, 1e-9));

        var wave5 = Assert.Single(Reminders("'Alex Prime/PA'!H21"), r => r.Item.Name == "Neutral Sect");
        var helios = RepoFiles.Action("Aspected Helios").CastTime;
        var now = TimingRules.DueAt(wave5.PressAt, wave5.DueNotBefore, new ReminderOptions());
        Assert.Equal(719.9 + 0.3 - helios, wave5.PressAt, 1e-9);
        Assert.Equal(719.9 + 0.3 - helios, now, 1e-9);
        Assert.True(now + helios >= 719.9 + 0.3 - 1e-9);
        Assert.True(now + 2.5 + helios < wave5.Hit);
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

    // ---- a buff the sheet names again while it lasts -----------------------------------------------------------

    /// <summary>
    /// 'LL/BJCC'!G44 "Benison (OT)/Temperance" on Hidden Mines (292.7) and G46 "Temperance" on Water 2 (306.7): one
    /// Temperance (20 s, recast 120 s) covers both rows. It is pressed 3.5 s before the mines, not 6 s, so that even used
    /// on its NOW cue it lasts 1 s past Water 2, where the call is the note "Temperance (still active)".
    /// </summary>
    [Fact]
    public void HiddenMines_TheTemperanceLastsThroughWater2()
    {
        var temperance = Assert.Single(Reminders("'LL/BJCC'!G44"), r => r.Item.Name == "Temperance");
        Assert.Equal(289.2, temperance.PressAt, 1e-9);
        Assert.True(temperance.PressAt < temperance.Hit - 0.3);
        var water2 = Assert.Single(Reminders("'LL/BJCC'!G46"));
        Assert.Equal(("p2.water_thunder_2", "Temperance (still active)"), (water2.Mechanic.Id, water2.Item.Note));
        Assert.Equal(water2.Hit + 1, temperance.PressAt - new ReminderOptions().DueLead + 20, 9);
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

    // ---- an action planned again before it is ready (the importer's recast fit) --------------------------------

    /// <summary>
    /// 'LL/BJCC'!G52 "Asylum" on Gavel (356.5), 85.8 s after G40's Asylum pressed after Photon (263.5), recast 90 s: it is
    /// pressed 1.5 s before Gavel, so that its reminder turns due when Asylum is ready again (353.5), not 7.2 s before
    /// it. 'Alex Prime/PA'!G23 "Plenary" on J Wave 7 (728.9), 59.2 s after G13's at Incinerating Heat (667.0), recast
    /// 60 s: ready at 727.0, it is pressed 1 s before the wave, as late as "(Late)" would press it, and still turns due
    /// only after wave 6 (726.2).
    /// </summary>
    [Fact]
    public void RecastFit_TheGavelAsylumAndTheJWave7PlenaryWaitForTheirRecast()
    {
        var options = new ReminderOptions();
        var photon = Assert.Single(Reminders("'LL/BJCC'!G40"), r => r.Item.Name == "Asylum");
        var gavel = Assert.Single(Reminders("'LL/BJCC'!G52"), r => r.Item.Name == "Asylum");
        Assert.Equal(355.0, gavel.PressAt, 1e-9);
        Assert.Equal(photon.PressAt + 90, gavel.PressAt - options.DueLead, 1e-9);

        var heat = Assert.Single(Reminders("'Alex Prime/PA'!G13"), r => r.Item.Name == "Plenary Indulgence");
        var wave7 = Assert.Single(Reminders("'Alex Prime/PA'!G23"), r => r.Item.Name == "Plenary Indulgence");
        Assert.Equal(667.0, heat.PressAt, 1e-9);
        Assert.Equal((727.9, 728.9 - 1), (wave7.PressAt, wave7.Hit + PlanImporter.LateOffset));
        Assert.True(wave7.PressAt >= heat.PressAt + 60);
        Assert.Equal(726.2, wave7.DueNotBefore!.Value, 1e-9);
    }

    /// <summary>
    /// 'LL/BJCC'!F23 "EukProg/Kerachole" on Protean 2 (109.1), 28 s after F19's Kerachole at Splash (77.1), recast 30 s:
    /// ready at 107.1, the cell is pressed 2 s before the hit, as late as "(Late)" would press it (Eukrasia, a GCD the game
    /// locks for 1 s, then the Eukrasian Prognosis 1 s before the hit), so that Kerachole is ready.
    /// </summary>
    [Fact]
    public void RecastFit_TheSagesProtean2KeracholeWaitsForItsRecast()
    {
        var splash = Assert.Single(Reminders("'LL/BJCC'!F19"), r => r.Item.Name == "Kerachole");
        var f23 = Reminders("'LL/BJCC'!F23");
        Assert.Equal(["Eukrasian Prognosis", "Kerachole"], f23.Select(r => r.Item.Name));
        Assert.All(f23, r => Assert.Equal(109.1 - 2, r.PressAt, 1e-9));
        Assert.Equal(splash.PressAt + 30, f23[1].PressAt, 1e-9);
        Assert.Equal(-2.0, f23[0].Entry.Offset);
    }

    /// <summary>
    /// <c>check --job WHM --slot H1</c>: no recast conflict for Asylum or Plenary Indulgence. The one left is the sheet's
    /// Divine Benison at Doll Feeds and Protean 1, 21.6 s apart with one charge at level 80, which is ready again only
    /// after Protean 1.
    /// </summary>
    [Fact]
    public void RecastFit_TheWhiteMagesCheckReportsNoConflictForThem()
    {
        var plan = PlanImports.LpduTea.Plan;
        var me = ColumnSuggestion.Context(ColumnSuggestion.Suggest(plan, Job.WHM, "H1"), Job.WHM, "H1", 80);
        var conflicts = PlanChecker.Check(PlanImports.Tea, RepoFiles.Actions, plan, me).Where(f => f.Code == "recast-conflict").ToList();

        Assert.DoesNotContain(conflicts, f => f.ActionId == RepoFiles.Action("Asylum").Id || f.ActionId == RepoFiles.Action("Plenary Indulgence").Id);
        Assert.Equal(RepoFiles.Action("Divine Benison").Id, Assert.Single(conflicts).ActionId);
    }
}
