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

    [Theory]
    [InlineData("WAR/DRK", "WAR/GNB", "DRK/GNB")]
    [InlineData("WARDRK", "WARGNB", "DRKGNB")] // the tab names of an xlsx export
    public void ATanksCoTankIsTheirJobsColumnForTheTanksPair(string warDrk, string warGnb, string drkGnb)
    {
        // Review finding (41d2893): the co-tank rows of a Warrior were the Dark Knight's (and Gunbreaker's) columns of every
        // tank pair, e.g. three Living Deads at one mechanic. A Warrior's co-tank plays the pair with the Warrior in it.
        var plan = Kit.Plan(
            [
                Kit.Entry("p1.raid", -5, Kit.Reprisal, "WAR in WAR/DRK", job: Job.WAR, assignee: warDrk + " WAR"),
                Kit.Entry("p1.raid", -5, Kit.Reprisal, "DRK in WAR/DRK", job: Job.DRK, assignee: warDrk + " DRK"),
                Kit.Entry("p1.raid", -5, Kit.Reprisal, "WAR in WAR/GNB", job: Job.WAR, assignee: warGnb + " WAR"),
                Kit.Entry("p1.raid", -5, Kit.Reprisal, "GNB in WAR/GNB", job: Job.GNB, assignee: warGnb + " GNB"),
                Kit.Entry("p1.raid", -5, Kit.Reprisal, "DRK in DRK/GNB", job: Job.DRK, assignee: drkGnb + " DRK"),
                Kit.Entry("p1.raid", -5, Kit.Reprisal, "GNB in DRK/GNB", job: Job.GNB, assignee: drkGnb + " GNB"),
                Kit.Entry("p1.raid", -5, Kit.Reprisal, "OT call", job: Job.DRK, slot: "OT", assignee: "OT"),
                Kit.Entry("p1.raid", -5, Kit.Reprisal, "LB tank", job: Job.DRK, assignee: "CM LB Tank"),
            ],
            new Assignee { Key = warDrk + " WAR", Job = Job.WAR },
            new Assignee { Key = warDrk + " DRK", Job = Job.DRK },
            new Assignee { Key = warGnb + " WAR", Job = Job.WAR },
            new Assignee { Key = warGnb + " GNB", Job = Job.GNB },
            new Assignee { Key = drkGnb + " DRK", Job = Job.DRK },
            new Assignee { Key = drkGnb + " GNB", Job = Job.GNB },
            new Assignee { Key = "OT", Slot = "OT" },
            new Assignee { Key = "CM LB Tank" });

        // Review round 5: the Cosmo Memory role column ("LB tank") is no longer the co-tank's: which role they play is not
        // known, as it is not for the player, whose own selection leaves those columns out.
        var war = new PlayerContext(Job.WAR, 90, "MT", warDrk + " WAR");
        Assert.Equal(["DRK in WAR/DRK", "GNB in WAR/GNB", "OT call"], Names(PartnerSelection.For(plan, war)));
        Assert.Equal(["DRK in WAR/DRK", "OT call"], Names(PartnerSelection.For(plan, war, [Job.WAR, Job.DRK, Job.WHM, Job.SCH])));

        // A Gunbreaker with a Dark Knight co-tank: the DRK/GNB pair.
        var gnb = new PlayerContext(Job.GNB, 90, "MT", drkGnb + " GNB");
        Assert.Equal(["WAR in WAR/GNB", "DRK in DRK/GNB", "OT call"], Names(PartnerSelection.For(plan, gnb)));
    }

    [Fact]
    public void TheCoTanksRoleWithoutASeatIsShownOnlyOppositeThePlayersOwn()
    {
        // Review round 5: LPDU's Cosmo Memory columns "CM LB Tank" and "CM Rep Tank" have each tank job's calls and no slot.
        // The co-tank rows held both (Dark Missionary twice at Wave Cannon 1 and Cosmo Meteor). The co-tank plays one role:
        // the other one than the player's when the player follows one of them in /mitcue > Plans, else unknown.
        var plan = Kit.Plan(
            [
                Kit.Entry("p6.raid", -5, Kit.Reprisal, "DRK pair", job: Job.DRK, assignee: "WAR/DRK DRK"),
                Kit.Entry("p6.raid", -5, Kit.Reprisal, "DRK LB", job: Job.DRK, assignee: "CM LB Tank"),
                Kit.Entry("p6.raid", -5, Kit.Reprisal, "DRK Rep", job: Job.DRK, assignee: "CM Rep Tank"),
                Kit.Entry("p6.raid", -5, Kit.Reprisal, "WAR LB", job: Job.WAR, assignee: "CM LB Tank"),
                Kit.Entry("p6.raid", -5, Kit.Reprisal, "DRK LB in OT", job: Job.DRK, slot: "OT", assignee: "CM LB Tank"),
            ],
            new Assignee { Key = "WAR/DRK WAR", Job = Job.WAR },
            new Assignee { Key = "WAR/DRK DRK", Job = Job.DRK },
            new Assignee { Key = "CM LB Tank" },
            new Assignee { Key = "CM Rep Tank" });
        Job[] party = [Job.WAR, Job.DRK, Job.WHM, Job.SCH];

        Assert.Equal(["DRK pair", "DRK LB in OT"], Names(PartnerSelection.For(plan, new PlayerContext(Job.WAR, 90, "MT", "WAR/DRK WAR"), party)));
        Assert.Equal(["DRK pair", "DRK Rep", "DRK LB in OT"], Names(PartnerSelection.For(plan, new PlayerContext(Job.WAR, 90, "MT", "CM LB Tank"), party)));
    }

    [Fact]
    public void APartnerJobWithCallsInTwoSlotsIsNotInASlotThePartyGivesAnotherJob()
    {
        // Review round 5: LPDU has Red Mage calls in R1 and R2; next to a Bard (all of whose calls are in R1) the Red Mage is
        // R2, but a Samurai's co-DPS rows held both seats' calls (Magick Barrier twice at Pantokrator).
        var plan = Kit.Plan(
            [
                Kit.Entry("p1.raid", -5, Kit.Reprisal, "BRD R1", job: Job.BRD, slot: "R1", assignee: "R1"),
                Kit.Entry("p1.raid", -5, Kit.Reprisal, "RDM R1", job: Job.RDM, slot: "R1", assignee: "R1"),
                Kit.Entry("p1.raid", -5, Kit.Reprisal, "RDM R2", job: Job.RDM, slot: "R2", assignee: "R2"),
                Kit.Entry("p1.raid", -5, Kit.Reprisal, "PCT R1", job: Job.PCT, slot: "R1", assignee: "R1"),
                Kit.Entry("p1.raid", -5, Kit.Reprisal, "PCT R2", job: Job.PCT, slot: "R2", assignee: "R2"),
            ],
            new Assignee { Key = "R1", Slot = "R1" },
            new Assignee { Key = "R2", Slot = "R2" });
        var sam = new PlayerContext(Job.SAM, 90, "M1");

        Assert.Equal(["BRD R1", "RDM R2"], Names(PartnerSelection.For(plan, sam, [Job.SAM, Job.BRD, Job.RDM])));
        Assert.Equal(["RDM R1", "RDM R2", "PCT R1", "PCT R2"], Names(PartnerSelection.For(plan, sam, [Job.SAM, Job.PCT, Job.RDM]))); // not settled
        Assert.Equal(["BRD R1", "RDM R1", "RDM R2", "PCT R1", "PCT R2"], Names(PartnerSelection.For(plan, sam))); // party unknown
    }

    [Fact]
    public void ColumnsOfAJobThatNameNoOtherJobAreAllKept()
    {
        // Two columns of the co-healer's job that are not team compositions (e.g. one per phase): both are theirs.
        var plan = Kit.Plan(
            [
                Kit.Entry("p1.raid", -5, Kit.Temperance, "P1 White Mage", job: Job.WHM, assignee: "White Mage"),
                Kit.Entry("p2.raid", -5, Kit.Temperance, "P2 WHM", job: Job.WHM, assignee: "WHM"),
            ],
            new Assignee { Key = "White Mage", Job = Job.WHM },
            new Assignee { Key = "WHM", Job = Job.WHM },
            new Assignee { Key = "Scholar", Job = Job.SCH });

        Assert.Equal(["P1 White Mage", "P2 WHM"], Names(PartnerSelection.For(plan, new PlayerContext(Job.SCH, 90, "H2", "Scholar"))));
    }

    [Fact]
    public void ACoTanksGeneralColumnIsKeptNextToTheirPairColumn()
    {
        // Review round 3: a "General" tab headed WAR/DRK next to a "WAR/DRK" pair tab (rekeyed "WAR/DRK DRK"). As soon as
        // one Dark Knight column named the Warrior, every other one was dropped, the general "DRK" column too.
        var plan = Kit.Plan(
            [
                Kit.Entry("p1.pantokrator", -3, Kit.Reprisal, "Dark Missionary", job: Job.DRK, assignee: "DRK"),
                Kit.Entry("p2.solar_ray", -3, Kit.Reprisal, "Shadow Wall", job: Job.DRK, assignee: "DRK"),
                Kit.Entry("p5.solar_ray_1", -3, Kit.Reprisal, "Living Dead", job: Job.DRK, assignee: "WAR/DRK DRK"),
                Kit.Entry("p5.solar_ray_2", -3, Kit.Reprisal, "Living Dead (DRK/GNB)", job: Job.DRK, assignee: "DRK/GNB DRK"),
                Kit.Entry("p5.solar_ray_1", -3, Kit.Reprisal, "Holmgang", job: Job.WAR, assignee: "WAR/DRK WAR"),
            ],
            new Assignee { Key = "WAR", Job = Job.WAR },
            new Assignee { Key = "DRK", Job = Job.DRK },
            new Assignee { Key = "WAR/DRK WAR", Job = Job.WAR },
            new Assignee { Key = "WAR/DRK DRK", Job = Job.DRK },
            new Assignee { Key = "DRK/GNB DRK", Job = Job.DRK });

        var war = new PlayerContext(Job.WAR, 90, "MT", "WAR/DRK WAR");

        Assert.Equal(["Dark Missionary", "Shadow Wall", "Living Dead"], Names(PartnerSelection.For(plan, war)));
    }

    [Fact]
    public void ACombinedTabThatRepeatsTheCoHealersTabIsNotShownTwice()
    {
        // Carry the Clear imported with every tab: the combined tab's Scholar column ("TOP – WHM + SCH Scholar", which
        // names the White Mage) has the Scholar tab's calls again, at the same mechanics.
        var plan = Kit.Plan(
            [
                Kit.Entry("p1.pantokrator", -3, Kit.Temperance, "Temperance", job: Job.WHM, assignee: "White Mage"),
                Kit.Entry("p1.pantokrator", -3, Kit.SacredSoil, "Sacred Soil", job: Job.SCH, assignee: "Scholar"),
                Kit.Entry("p2.solar_ray", -3, Kit.SacredSoil, "Expedient", job: Job.SCH, assignee: "Scholar"),
                Kit.Entry("p1.pantokrator", -3, Kit.SacredSoil, "Sacred Soil again", job: Job.SCH, assignee: "TOP – WHM + SCH Scholar"),
                Kit.Entry("p2.solar_ray", -3, Kit.SacredSoil, "Expedient again", job: Job.SCH, assignee: "TOP – WHM + SCH Scholar"),
            ],
            new Assignee { Key = "White Mage", Job = Job.WHM },
            new Assignee { Key = "Scholar", Job = Job.SCH },
            new Assignee { Key = "TOP – WHM + SCH Scholar", Job = Job.SCH },
            new Assignee { Key = "TOP – WHM + SCH White Mage", Job = Job.WHM });

        var whm = new PlayerContext(Job.WHM, 90, "H1", "White Mage");

        Assert.Equal(["Sacred Soil", "Expedient"], Names(PartnerSelection.For(plan, whm)));

        // A White Mage who follows the combined tab sees the Scholar's column of that tab.
        var combined = new PlayerContext(Job.WHM, 90, "H1", "TOP – WHM + SCH White Mage");
        Assert.Equal(["Sacred Soil again", "Expedient again"], Names(PartnerSelection.For(plan, combined)));
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
