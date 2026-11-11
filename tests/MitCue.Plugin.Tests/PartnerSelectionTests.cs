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
}
