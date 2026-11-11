using MitCue.Core.Import;
using MitCue.Core.Plans;

namespace MitCue.Core.Tests.Import;

/// <summary>
/// Carry the Clear's 'TOP – Scholar' tab, shipped as "Carry the Clear - SCH" (the Scholar's automatic TOP plan): the
/// Scholar's calls, the same as the Scholar column of the sheet's combined 'TOP – WHM + SCH' tab.
/// </summary>
public class CarryTheClearSchImportTests
{
    private static MitPlan Plan => PlanImports.Sch.Plan;

    [Fact]
    public void ImportsTheScholarColumn()
    {
        Assert.Equal("Carry the Clear - SCH", Plan.Name);
        Assert.Equal(PlanImports.CarryTheClearSchId, Plan.Id);
        Assert.Equal(["TOP – Scholar"], Plan.Source!.Tabs);
        var column = Assert.Single(Plan.Assignees);
        Assert.Equal(("Scholar", Job.SCH), (column.Key, column.Job));
        Assert.Equal(69, Plan.Entries.Count);
        Assert.All(Plan.Entries, e => Assert.Equal((Job?)Job.SCH, e.Who.Job));
        Assert.All(Plan.Entries, e => Assert.StartsWith("'TOP – Scholar'!", e.Src, StringComparison.Ordinal));
    }

    [Fact]
    public void HasTheCallsOfTheCombinedTabsScholarColumn()
    {
        // The combined tab repeats the Scholar tab's column next to the White Mage's, so both imports must agree call for
        // call: mechanic, hit, timing, actions, sequence, flags and notes. Only targets may differ: each tab's notes word
        // them in their own way, and the Scholar tab is the more precise one.
        static string Key(PlanEntry e) =>
            $"{e.Mechanic}|{e.HitIndex}|{e.Offset}|{string.Join("+", e.Names())}|{e.Seq}|{e.Optional}|{e.WhenReady}|{e.AtPhaseEnd}|{e.Severity}|{e.Note}";

        var combined = PlanImports.WhmSch.Plan.Entries.Where(e => e.Who.Job == Job.SCH).ToList();
        Assert.Equal(combined.Select(Key).ToList(), Plan.Entries.Select(Key).ToList());

        var targets = combined.Zip(Plan.Entries).Where(p => p.First.Target != p.Second.Target).Select(p => (p.Second.Src, p.First.Target, p.Second.Target)).ToList();
        Assert.Equal(
            [
                ("'TOP – Scholar'!C22", "tanks", "tank"),
                ("'TOP – Scholar'!C51", "a non-tank", "you or another non-tank"),
                ("'TOP – Scholar'!C53", null, "a tank"),
                ("'TOP – Scholar'!C54", "tank", "the Solar Ray tank"),
            ],
            targets);
    }

    [Fact]
    public void ImportsWithoutErrors()
    {
        Assert.DoesNotContain(PlanImports.Sch.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}
