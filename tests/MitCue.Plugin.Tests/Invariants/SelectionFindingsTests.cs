using MitCue.Core;
using MitCue.Core.Data;
using MitCue.Core.Import;
using MitCue.Core.Plans;
using MitCue.Import;
using MitCue.PluginTests.Support;
using MitCue.Runtime;

namespace MitCue.PluginTests.Invariants;

/// <summary>Review round 5: one test per invariant violation found, on the smallest input that showed it.</summary>
public class SelectionFindingsTests
{
    private const string Lpdu = "1f75e1e8-7321-566c-ae86-8568019fe914";

    private static readonly Job[] PartyWarDrk = [Job.WAR, Job.DRK, Job.WHM, Job.SCH, Job.SAM, Job.RPR, Job.DNC, Job.PCT];

    private static FrameContext Frame(Job job, double now, IReadOnlyList<Job>? party = null)
    {
        var frame = SessionManagerTests.InTop(now);
        frame.Job = job;
        frame.ClassJobId = (uint)job;
        frame.PartyJobMask = party == null ? 0 : PartyJobs.Mask(party);
        return frame;
    }

    private static List<string> Twice(IEnumerable<PlanEntry> entries) =>
        entries.SelectMany(e => e.Do.SelectMany(i => i.ActionIds()).Distinct().Select(id => (e.Mechanic, Id: id, e.Id)))
            .GroupBy(x => (x.Mechanic, x.Id))
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.Mechanic}/{g.Key.Id}: {string.Join("+", g.Select(x => x.Item3))}")
            .ToList();

    [Theory]
    [InlineData(null)]
    [InlineData("M1")] // a melee main's usual slot: not the player's slot on a caster (Slots.ForJob)
    public void ARedMageOnLpduWithoutARangedSlotGetsOneSeatsCalls(string? usualSlot)
    {
        // LPDU is the Red Mage's automatic plan (it has RDM calls in R1 and R2). Without a ranged usual slot the overlay
        // showed both ranged seats' calls: Magick Barrier twice at Pantokrator, Limitless Synergy, Latent Defect 3, Sigma.
        var parts = SessionManagerTests.NewManager(new Configuration { DefaultSlot = usualSlot });
        parts.Manager.Update(Frame(Job.RDM, 1));

        Assert.Equal(Lpdu, parts.Manager.Plan?.Id);
        Assert.True(parts.Manager.PlanIsAutomatic);
        var own = parts.Manager.Session!.Scheduler.All.Select(r => r.Entry).Distinct().ToList();
        Assert.Empty(Twice(own));
        Assert.Equal(["R1"], own.Select(e => e.Who.Slot).Distinct());
        Assert.Equal(9, own.Count);
    }

    [Fact]
    public void ARedMageNextToABardTakesTheOtherRangedSeat()
    {
        // The Bard's calls are all in R1: the Red Mage plays R2 (13 calls), and the Bard's calls are their co-DPS rows.
        var parts = SessionManagerTests.NewManager(new Configuration { ShowOtherAssignees = true });
        parts.Manager.Update(Frame(Job.RDM, 1, [Job.GNB, Job.PLD, Job.AST, Job.SGE, Job.MNK, Job.DRG, Job.BRD, Job.RDM]));

        var own = parts.Manager.Session!.Scheduler.All.Select(r => r.Entry).Distinct().ToList();
        Assert.Equal(13, own.Count);
        Assert.All(own, e => Assert.Equal("R2", e.Who.Slot));
        var others = parts.Manager.Session.Others!.All.Select(r => r.Entry).Distinct().ToList();
        Assert.Contains(others, e => e.Who.Job == Job.BRD);
        Assert.DoesNotContain(others, e => e.Who.Slot == "R2");
    }

    [Fact]
    public void ABardWhoseCallsAreAllInR1HasNoR1CallsOfOtherJobsAsPartnerRows()
    {
        // LPDU has the Bard's calls in R1 only, so R1 is the Bard's seat; without a usual slot the "co-DPS" rows held the
        // Machinist's, Dancer's, Pictomancer's and Red Mage's R1 calls: the player's own seat played by another job.
        var parts = SessionManagerTests.NewManager(new Configuration { ShowOtherAssignees = true });
        parts.Manager.Update(Frame(Job.BRD, 1));

        var own = parts.Manager.Session!.Scheduler.All.Select(r => r.Entry).Distinct().ToList();
        Assert.All(own, e => Assert.Equal("R1", e.Who.Slot));
        var others = parts.Manager.Session.Others!.All.Select(r => r.Entry).Distinct().ToList();
        Assert.DoesNotContain(others, e => e.Who.Slot == "R1");
    }

    [Fact]
    public void TheSameCompositionInTwoPhaseTabsIsOneSeat()
    {
        // The importer keys the columns "WHM/SCH P1 WHM" and "WHM/SCH P3 WHM" (composition tabs whose headers collide). Both
        // are the White Mage's seat in the one composition, but every composition column other than the chosen one was taken
        // for an alternative: the P3 calls were never shown, and the import's Save step reported nothing missing.
        var data = new MitCue.Data.CoreData(Kit.Errors(), () => []);
        data.EnsureLoaded();
        var plan = SelectionInvariantTests.PerPhaseCompositions(data).Raw;
        Assert.Equal(
            ["WHM/SCH P1 WHM", "WHM/SCH P1 SCH", "WHM/SCH P3 WHM", "WHM/SCH P3 SCH"],
            plan.Assignees.Select(a => a.Key).Where(k => k.StartsWith("WHM/SCH", StringComparison.Ordinal)));
        var whm = plan.Entries.Where(e => e.Who.Job == Job.WHM).ToList();
        Assert.Equal(4, whm.Count);

        Job[] party = [Job.WAR, Job.DRK, Job.WHM, Job.SCH];
        var choice = ImportColumns.Choose(plan, Job.WHM, 90, "H1", party: party);
        var pick = ColumnSuggestion.Suggest(plan, Job.WHM, "H1", party);
        var shown = PlanSelection.For(plan, ColumnSuggestion.Context(pick, Job.WHM, "H1", 90)).ToList();

        Assert.Equal(whm.Select(e => e.Id), shown.Select(e => e.Id));
        Assert.Equal((4, 0), (choice.Mine, choice.Missed));
        Assert.Equal(["WHM/SCH P1 WHM", "WHM/SCH P3 WHM"], choice.Columns);

        // The co-healer rows: both of the Scholar's tabs, and none of the White Mage's.
        var partners = PartnerSelection.For(plan, ColumnSuggestion.Context(pick, Job.WHM, "H1", 90), party);
        Assert.Equal(plan.Entries.Where(e => e.Who.Job == Job.SCH).Select(e => e.Id), partners.Select(e => e.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("H1")]
    public void TheClisColumnIsThePlansTabsColumn(string? slot)
    {
        // /mitcue > Plans saves a picked column with its slot (ConfigWindow.DrawColumnCombo) and the overlay uses it, but
        // `mitcue plan lpdu.yaml --job SCH --assignee "H2 SCH"` (the CLI help's own example) read the column without it:
        // 27 entries without --slot, and 2 with --slot H1 (the H1 seat's notes).
        var parts = SessionManagerTests.NewManager();
        parts.Plans.EnsureLoaded();
        var plan = parts.Plans.Get(Lpdu)!.Plan;
        var column = plan.Assignees.Single(a => a.Key == "H2 SCH");
        var mapping = new PlayerMapping { AssigneeKey = column.Key, Slot = column.Slot };
        var overlay = PlanSelection.For(plan, new PlayerContext(Job.SCH, 90, mapping.Slot ?? Slots.ForJob(slot, Job.SCH), mapping.AssigneeKey)).Select(e => e.Id).ToList();
        Assert.Equal(31, overlay.Count);

        var output = new StringWriter();
        var cli = new MitCue.Cli.CliContext(output, output, new MitCue.Cli.CliEnvironment { CurrentDirectory = Kit.TempDirectory().FullName, ProgramDirectory = AppContext.BaseDirectory, Input = () => new StringReader(string.Empty) });
        var file = Path.Combine(cli.Environment.CurrentDirectory, "lpdu.yaml");
        File.WriteAllText(file, EmbeddedDataSource.Plans().ReadText("top-lpdu.yaml"));
        List<string> tokens = [file, "--job", "SCH", "--assignee", "H2 SCH"];
        if (slot != null)
            tokens.AddRange(["--slot", slot]);
        var player = MitCue.Cli.PlayerSelection.Select(cli, MitCue.Cli.ArgParser.Parse(MitCue.Cli.Commands.PlanCommand.Spec, tokens), cli.ReadPlan(file));

        Assert.Equal(overlay, player.Entries.Select(e => e.Id).ToList());
    }

    [Fact]
    public void TheCoTanksRowsHoldNeitherCosmoMemoryRole()
    {
        // The player's own selection leaves out "CM LB Tank" and "CM Rep Tank" (their role is not known), but the co-tank
        // rows held the Dark Knight's calls of both roles: Dark Missionary twice at Wave Cannon 1 and Cosmo Meteor.
        var parts = SessionManagerTests.NewManager(new Configuration { ShowOtherAssignees = true, DefaultSlot = "MT" });
        parts.Manager.Update(Frame(Job.WAR, 1, PartyWarDrk));

        var others = parts.Manager.Session!.Others!.All.Select(r => r.Entry).Distinct().ToList();
        Assert.Empty(Twice(others));
        Assert.DoesNotContain(others, e => e.Who.Assignee is "CM LB Tank" or "CM Rep Tank");
    }

    [Fact]
    public void ThePartyTellsWhichRangedSeatTheRedMagePlays()
    {
        // LPDU has Red Mage calls in R1 and R2. Next to a Bard (R1 only) the Red Mage is R2, but the "co-DPS" rows of the
        // Samurai held both seats' calls: Magick Barrier twice at Pantokrator, Limitless Synergy, Latent Defect 3, Sigma.
        var parts = SessionManagerTests.NewManager(new Configuration { ShowOtherAssignees = true, DefaultSlot = "M1" });
        parts.Plans.EnsureLoaded();
        parts.Plans.SetActive("top", Job.SAM, Lpdu);
        parts.Manager.Update(Frame(Job.SAM, 1, [Job.GNB, Job.PLD, Job.AST, Job.SGE, Job.SAM, Job.DRG, Job.BRD, Job.RDM]));

        var others = parts.Manager.Session!.Others!.All.Select(r => r.Entry).Distinct().ToList();
        Assert.Empty(Twice(others));
        Assert.DoesNotContain(others, e => e.Who.Job == Job.RDM && e.Who.Slot == "R1");
        Assert.Contains(others, e => e.Who.Job == Job.RDM && e.Who.Slot == "R2");
    }

    [Fact]
    public void TheSaveStepDoesNotCountAnotherSeatsCallsAsLeftOut()
    {
        // A Scholar whose usual slot is H1 (the White Mage player on their Scholar) imports LPDU: the overlay follows
        // "H2 SCH" in H2 (31 calls), but the Save step warned that the column "leaves out 2 of your calls (in H1)", the H1
        // seat's job-less "Everything" notes, and sent the player to change "My column".
        var parts = SessionManagerTests.NewManager(new Configuration { DefaultSlot = "H1" });
        parts.Plans.EnsureLoaded();
        var plan = parts.Plans.Get(Lpdu)!.Plan;

        var choice = ImportColumns.Choose(plan, Job.SCH, 90, "H1");
        var overlay = parts.Plans.MappingFor(plan, Job.SCH);

        Assert.Equal(("H2 SCH", "H2"), (overlay.AssigneeKey, overlay.Slot));
        Assert.Equal((31, 0), (choice.Mine, choice.Missed));
    }

    [Fact]
    public void TheImportWindowCountsTheCallsOfTheTankPairTheOverlayShows()
    {
        // A Paladin next to a Gunbreaker: the overlay, the preview and the Plans tab take "GNB/PLD PLD" (36 calls in MT); the
        // import window had no party, said 34 calls apply and filtered "My calls" to "WAR/PLD PLD".
        var parts = SessionManagerTests.NewManager(new Configuration { DefaultSlot = "MT" });
        parts.Plans.EnsureLoaded();
        var plan = parts.Plans.Get(Lpdu)!.Plan;
        Job[] party = [Job.GNB, Job.PLD, Job.AST, Job.SGE, Job.MNK, Job.DRG, Job.BRD, Job.RDM];

        var overlay = parts.Plans.MappingFor(plan, Job.PLD, party);
        var shown = PlanSelection.For(plan, new PlayerContext(Job.PLD, 90, overlay.Slot ?? "MT", overlay.AssigneeKey)).Count();
        var choice = ImportColumns.Choose(plan, Job.PLD, 90, "MT", party: party);

        Assert.Equal(("GNB/PLD PLD", 36), (overlay.AssigneeKey, shown));
        Assert.Equal((overlay.AssigneeKey, shown), (choice.Mapping.AssigneeKey, choice.Mine));
        Assert.Equal("WAR/PLD PLD", ImportColumns.Choose(plan, Job.PLD, 90, "MT").Mapping.AssigneeKey); // party unknown: the first pair
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AWhiteMageNextToASageFollowsTheWhiteMageTabNotTheWhmSchTab(bool combinedFirst)
    {
        // Carry the Clear imported with the combined "TOP – WHM + SCH" tab first: next to a Sage the White Mage followed the
        // combined tab's column (a composition the party does not play), while with the combined tab last the same party
        // got the White Mage tab, the seat for any co-healer.
        var data = new MitCue.Data.CoreData(Kit.Errors(), () => []);
        data.EnsureLoaded();
        var ctc = FixtureSheets.CarryTheClear();
        var combined = ctc.Tabs.Single(t => t.Name == "TOP – WHM + SCH");
        var document = combinedFirst ? new SheetDocument { Title = ctc.Title, Tabs = [combined, .. ctc.Tabs.Where(t => t != combined)] } : ctc;
        var plan = ImportPipeline.Import(document, new ImportOptions { Fight = data.GetFight("top")!, Actions = data.Actions, Aliases = data.Aliases!, ImportedDate = "2026-11-11" }).Plan;
        Job[] party = [Job.GNB, Job.PLD, Job.WHM, Job.SGE, Job.MNK, Job.DRG, Job.BRD, Job.RDM];

        Assert.Equal("White Mage", ColumnSuggestion.Suggest(plan, Job.WHM, "H1", party).AssigneeKey);
        Assert.Equal("White Mage", ImportColumns.Choose(plan, Job.WHM, 90, "H1", party: party).Mapping.AssigneeKey);
    }
}
