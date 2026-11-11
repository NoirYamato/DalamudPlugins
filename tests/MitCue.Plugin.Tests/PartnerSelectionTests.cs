using MitCue.Core;
using MitCue.Core.Plans;
using MitCue.PluginTests.Support;
using MitCue.Runtime;

namespace MitCue.PluginTests;

/// <summary>"Also show my co-healer's calls" mirrors the co-healer (the other columns of the player's role), not everyone.</summary>
public class PartnerSelectionTests
{
    private static readonly string[] SlotNames = ["MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2"];

    /// <summary>An LPDU-style plan: one column per party slot, one call each.</summary>
    private static MitPlan EightSlotPlan() => Kit.Plan(
        SlotNames.Select(s => Kit.Entry("p1.raid", -5, Kit.Reprisal, "Call for " + s, job: null, slot: s, assignee: s)),
        SlotNames.Select(s => new Assignee { Key = s, Slot = s }).ToArray());

    [Fact]
    public void AHealerSeesOnlyTheCoHealersColumn()
    {
        // Review finding: with eight role columns the option mirrored all seven other players.
        var plan = EightSlotPlan();
        var me = new PlayerContext(Job.SCH, 90, "H2", "H2");

        var partners = PartnerSelection.For(plan, me);

        var entry = Assert.Single(partners);
        Assert.Equal("H1", entry.Who.Slot);
    }

    [Fact]
    public void ATankSeesTheCoTank()
    {
        var partners = PartnerSelection.For(EightSlotPlan(), new PlayerContext(Job.WAR, 90, "MT", "MT"));

        Assert.Equal(["OT"], partners.Select(e => e.Who.Slot));
    }

    [Fact]
    public void ADpsSeesTheOtherDps()
    {
        var partners = PartnerSelection.For(EightSlotPlan(), new PlayerContext(Job.SAM, 90, "M1", "M1"));

        Assert.Equal(["M2", "R1", "R2"], partners.Select(e => e.Who.Slot));
    }

    [Fact]
    public void JobColumnsWork()
    {
        // Carry the Clear's combined healer tab: "White Mage" and "Scholar" columns.
        var plan = Kit.Plan(
            [
                Kit.Entry("p1.raid", -5, Kit.Temperance, "Temperance", job: Job.WHM, assignee: "White Mage"),
                Kit.Entry("p1.raid", -5, Kit.SacredSoil, "Sacred Soil", job: Job.SCH, assignee: "Scholar"),
            ],
            new Assignee { Key = "White Mage", Job = Job.WHM },
            new Assignee { Key = "Scholar", Job = Job.SCH });

        var partners = PartnerSelection.For(plan, new PlayerContext(Job.WHM, 90, null, "White Mage"));

        Assert.Equal([Job.SCH], partners.Select(e => e.Who.Job));
    }

    [Fact]
    public void ColumnsKnownOnlyByTheirKeyOrAssigneeAreRecognised()
    {
        var plan = Kit.Plan(
            [
                Kit.Entry("p1.raid", -5, Kit.SacredSoil, "Sacred Soil", job: null, assignee: "Scholar"),
                Kit.Entry("p1.raid", -5, Kit.Consolation, "Consolation", job: null, assignee: "Healer 2"),
                Kit.Entry("p1.raid", -5, Kit.Reprisal, "Reprisal", job: null, assignee: "Alice"),
            ],
            new Assignee { Key = "Healer 2", Slot = "H2" },
            new Assignee { Key = "Alice" });

        var partners = PartnerSelection.For(plan, new PlayerContext(Job.WHM, 90, "H1", "Healer 1"));

        Assert.Equal(["Scholar", "Healer 2"], partners.Select(e => e.Who.Assignee));
    }

    [Fact]
    public void TheirOwnCallsAreNeverAPartnersCalls()
    {
        var plan = EightSlotPlan();
        var me = new PlayerContext(Job.WHM, 90, "H1", "H1");

        Assert.DoesNotContain(PartnerSelection.For(plan, me), e => PlanSelection.Applies(e, me));
    }

    [Fact]
    public void NoRoleNoPartners()
    {
        Assert.Empty(PartnerSelection.For(EightSlotPlan(), new PlayerContext(Job.None, 90)));
    }

    /// <summary>LPDU's healer columns: job-less slot columns (P1/P2) and one column per job for each slot (P3–P6).</summary>
    private static MitPlan LpduHealers() => Kit.Plan(
        [
            Kit.Entry("p1.raid", -5, Kit.Temperance, "H1 note", job: null, slot: "H1", assignee: "H1"),
            Kit.Entry("p1.raid", -5, Kit.SacredSoil, "H2 note", job: null, slot: "H2", assignee: "H2"),
            Kit.Entry("p1.raid2", -5, Kit.SacredSoil, "Soil", job: Job.SCH, slot: "H2", assignee: "H2"),
            Kit.Entry("p1.raid2", -5, Kit.Reprisal, "Kerachole", job: Job.SGE, slot: "H2", assignee: "H2"),
            Kit.Entry("p2.raid", -5, Kit.Temperance, "Temperance", job: Job.WHM, slot: "H1", assignee: "H1 WHM"),
            Kit.Entry("p2.raid", -5, Kit.Reprisal, "Neutral Sect", job: Job.AST, slot: "H1", assignee: "H1 AST"),
            Kit.Entry("p2.raid", -5, Kit.SacredSoil, "Expedient", job: Job.SCH, slot: "H2", assignee: "H2 SCH"),
            Kit.Entry("p2.raid", -5, Kit.Reprisal, "Holos", job: Job.SGE, slot: "H2", assignee: "H2 SGE"),
        ],
        new Assignee { Key = "H1", Slot = "H1" },
        new Assignee { Key = "H2", Slot = "H2" },
        new Assignee { Key = "H1 WHM", Slot = "H1", Job = Job.WHM },
        new Assignee { Key = "H1 AST", Slot = "H1", Job = Job.AST },
        new Assignee { Key = "H2 SCH", Slot = "H2", Job = Job.SCH },
        new Assignee { Key = "H2 SGE", Slot = "H2", Job = Job.SGE });

    private static List<string> Names(IEnumerable<PlanEntry> entries) => entries.Select(e => e.Do[0].Name!).ToList();

    [Fact]
    public void TheirOwnSeatInAnotherCompositionIsNotACoHealer()
    {
        // Final review finding: for the H2 Scholar on LPDU the "co-healer" calls were every other healer column: the H2
        // Sage column (the same seat with another job), the player's own job-less H2 notes, and both H1 columns.
        var plan = LpduHealers();
        var me = new PlayerContext(Job.SCH, 90, "H2", "H2 SCH"); // the job's column: the job-less H2 calls are not theirs

        Assert.Equal(["H1 note", "Temperance", "Neutral Sect"], Names(PartnerSelection.For(plan, me)));
    }

    [Fact]
    public void InAPartyTheCoHealerIsTheOneWhoseJobIsThere()
    {
        var plan = LpduHealers();
        var me = new PlayerContext(Job.SCH, 90, "H2");
        Job[] party = [Job.WAR, Job.DRK, Job.WHM, Job.SCH, Job.SAM, Job.RPR, Job.DNC, Job.PCT];

        Assert.Equal(["H1 note", "Temperance"], Names(PartnerSelection.For(plan, me, party)));
        Assert.Equal(["H1 note", "Temperance", "Neutral Sect"], Names(PartnerSelection.For(plan, me, [])));
    }

    [Fact]
    public void ATankDoesNotSeeTheirJobInTheOtherTankPairs()
    {
        // LPDU's tank tabs: one column per job and tank pair ("WAR/DRK WAR", "WAR/GNB WAR", ...), without slots.
        var plan = Kit.Plan(
            [
                Kit.Entry("p1.raid", -5, Kit.Reprisal, "WAR in WAR/DRK", job: Job.WAR, assignee: "WAR/DRK WAR"),
                Kit.Entry("p1.raid", -5, Kit.Reprisal, "DRK in WAR/DRK", job: Job.DRK, assignee: "WAR/DRK DRK"),
                Kit.Entry("p1.raid", -5, Kit.Reprisal, "WAR in WAR/GNB", job: Job.WAR, assignee: "WAR/GNB WAR"),
                Kit.Entry("p1.raid", -5, Kit.Reprisal, "GNB in WAR/GNB", job: Job.GNB, assignee: "WAR/GNB GNB"),
            ],
            new Assignee { Key = "WAR/DRK WAR", Job = Job.WAR },
            new Assignee { Key = "WAR/DRK DRK", Job = Job.DRK },
            new Assignee { Key = "WAR/GNB WAR", Job = Job.WAR },
            new Assignee { Key = "WAR/GNB GNB", Job = Job.GNB });
        var me = new PlayerContext(Job.WAR, 90, "MT", "WAR/DRK WAR");

        Assert.Equal(["DRK in WAR/DRK", "GNB in WAR/GNB"], Names(PartnerSelection.For(plan, me)));
        Assert.Equal(["DRK in WAR/DRK"], Names(PartnerSelection.For(plan, me, [Job.WAR, Job.DRK, Job.WHM, Job.SCH])));
    }

    [Fact]
    public void PartyJobMasksRoundTrip()
    {
        var mask = PartyJobs.Mask([Job.WHM, Job.SCH, Job.WAR]);
        Assert.Equal([Job.WAR, Job.WHM, Job.SCH], PartyJobs.Of(mask));
        Assert.Equal(mask, PartyJobs.Add(PartyJobs.Add(mask, 0), 7)); // no job (0) and a base class (7) are ignored
        Assert.Empty(PartyJobs.Of(0));
    }
}
