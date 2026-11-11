using MitCue.Core;
using MitCue.Plans;
using MitCue.Runtime;

namespace MitCue.PluginTests.Integration;

/// <summary>
/// A tank on the bundled LPDU plan, which has one column per tank pair for each tank job ("WAR/DRK WAR", "WAR/GNB WAR",
/// ...) next to the slot columns "MT" and "OT": regression tests for review round 3.
/// </summary>
public class TankPairTests
{
    /// <summary>LPDU's job-less "MT" calls: eight Reprisals and three Ramparts.</summary>
    private static readonly string[] MtCalls = ["e027", "e044", "e083", "e089", "e115", "e121", "e145", "e172", "e216", "e245", "e279"];

    private static FrameContext Frame(Job job, double now, params Job[] party)
    {
        var frame = SessionManagerTests.InTop(now);
        frame.Job = job;
        frame.ClassJobId = (uint)job;
        frame.PartyJobMask = PartyJobs.Mask(party);
        return frame;
    }

    private static List<string> PairColumns(SessionManagerTests.Parts parts, bool partner)
    {
        var session = parts.Manager.Session!;
        var reminders = partner ? session.Others!.All : session.Scheduler.All;
        return reminders.Select(r => r.Entry.Who.Assignee).OfType<string>().Where(k => k.Contains('/', StringComparison.Ordinal)).Distinct().ToList();
    }

    [Theory]
    [InlineData(Job.WAR)]
    [InlineData(Job.DRK)]
    public void TheMainTankKeepsTheMtColumnsReprisalAndRampartCalls(Job job)
    {
        // Round 2 made the tank's column one pair ("WAR/DRK WAR") and the job-less MT calls were neither the player's nor
        // shown dimmed as a partner's: 11 calls gone without a word, and no choice in /mitcue > Plans had them and one pair.
        var parts = SessionManagerTests.NewManager(new Configuration { DefaultSlot = "MT", ShowOtherAssignees = true });
        parts.Manager.Update(Frame(job, 1));

        var session = parts.Manager.Session!;
        var own = session.Scheduler.All.Select(r => r.Entry.Id).ToHashSet();
        Assert.Subset(own, MtCalls.ToHashSet());
        Assert.DoesNotContain(session.Others!.All, r => MtCalls.Contains(r.Entry.Id));
        Assert.Single(PairColumns(parts, partner: false));
    }

    [Theory]
    [InlineData(Job.PLD, "MT", new[] { Job.PLD, Job.DRK, Job.WHM, Job.SCH }, "DRK/PLD PLD", "DRK/PLD DRK")]
    [InlineData(Job.WAR, "MT", new[] { Job.WAR, Job.PLD, Job.WHM, Job.SCH }, "WAR/PLD WAR", "WAR/PLD PLD")]
    [InlineData(Job.DRK, "OT", new[] { Job.WAR, Job.DRK, Job.WHM, Job.SCH }, "WAR/DRK DRK", "WAR/DRK WAR")]
    public void TheTankPairFollowsTheCoTankForOwnAndPartnerCalls(Job job, string slot, Job[] party, string own, string partner)
    {
        // Review round 3: a Paladin next to a Dark Knight followed "WAR/PLD PLD" (the first pair) while the co-tank's
        // dimmed calls came from "DRK/PLD DRK".
        var parts = SessionManagerTests.NewManager(new Configuration { DefaultSlot = slot, ShowOtherAssignees = true });
        parts.Manager.Update(Frame(job, 1, party));

        Assert.Equal(own, parts.Manager.Mapping?.AssigneeKey);
        Assert.True(parts.Manager.Mapping!.IsSuggestion);
        Assert.Equal([own], PairColumns(parts, partner: false));
        Assert.Equal([partner], PairColumns(parts, partner: true));
    }

    [Fact]
    public void ACoTankChangeRebuildsWithTheirPairAlsoWhenTheirCallsAreHidden()
    {
        var parts = SessionManagerTests.NewManager(new Configuration { DefaultSlot = "MT" });
        parts.Manager.Update(Frame(Job.WAR, 1, Job.WAR, Job.DRK));
        Assert.Equal("WAR/DRK WAR", parts.Manager.Mapping?.AssigneeKey);
        var generation = parts.Manager.Generation;

        parts.Manager.Update(Frame(Job.WAR, 2, Job.WAR, Job.GNB));

        Assert.True(parts.Manager.Generation > generation);
        Assert.Equal("WAR/GNB WAR", parts.Manager.Mapping?.AssigneeKey);
        Assert.Equal(["WAR/GNB WAR"], PairColumns(parts, partner: false));

        // Another party change that keeps the pair (a healer swap) rebuilds nothing.
        generation = parts.Manager.Generation;
        parts.Manager.Update(Frame(Job.WAR, 3, Job.WAR, Job.GNB, Job.AST));
        Assert.Equal(generation, parts.Manager.Generation);
    }

    [Fact]
    public void AColumnChosenInThePlansTabIsKeptWhateverTheParty()
    {
        var parts = SessionManagerTests.NewManager(new Configuration { DefaultSlot = "MT" });
        parts.Plans.EnsureLoaded();
        var lpdu = parts.Plans.ActivePlanFor("top", Job.WAR)!.Plan;
        parts.Plans.SetMapping(lpdu, Job.WAR, new PlayerMapping { AssigneeKey = "WAR/GNB WAR", Slot = null });

        parts.Manager.Update(Frame(Job.WAR, 1, Job.WAR, Job.PLD));

        Assert.False(parts.Manager.Mapping!.IsSuggestion);
        Assert.Equal(["WAR/GNB WAR"], PairColumns(parts, partner: false));
        Assert.Subset(parts.Manager.Session!.Scheduler.All.Select(r => r.Entry.Id).ToHashSet(), MtCalls.ToHashSet());
    }

    [Fact]
    public void ThePlansTabShowsThePairTheSessionUses()
    {
        var parts = SessionManagerTests.NewManager(new Configuration { DefaultSlot = "MT" });
        parts.Plans.EnsureLoaded();
        var lpdu = parts.Plans.ActivePlanFor("top", Job.WAR)!.Plan;

        Assert.Equal("WAR/PLD WAR", parts.Plans.MappingFor(lpdu, Job.WAR, [Job.WAR, Job.PLD]).AssigneeKey);
        Assert.Equal("WAR/DRK WAR", parts.Plans.MappingFor(lpdu, Job.WAR).AssigneeKey); // the cache keeps them apart
        Assert.Equal("WAR/PLD WAR", PlanStore.SuggestedMapping(lpdu, Job.WAR, "MT", [Job.WAR, Job.PLD]).AssigneeKey);
    }
}
