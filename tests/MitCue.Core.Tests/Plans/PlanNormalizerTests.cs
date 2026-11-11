using MitCue.Core.Plans;

namespace MitCue.Core.Tests.Plans;

/// <summary>
/// Slot synonyms become the standard slots when a plan is read (<see cref="PlanNormalizer"/>), so every reader selects
/// the same calls: the plugin's plan store and AI route, and the CLI (review round 3: the CLI showed "D3" calls to no
/// one, while the plugin showed them to the R1 player).
/// </summary>
public class PlanNormalizerTests
{
    private const string Yaml = """
        format: mitcue-plan/1
        id: synonyms
        name: Synonym slots
        fight: top
        assignees:
          - {key: D3, slot: D3}
          - {key: Tanks, slot: t1}
          - {key: Odd, slot: H3}
        entries:
          - id: e001
            mechanic: p1.pantokrator
            who: {slot: D3, assignee: D3}
            do:
              - {action: 7560, name: Addle}
          - id: e002
            mechanic: p2.solar_ray
            who: {slot: ' T1 '}
            do:
              - {action: 7535, name: Reprisal}
          - id: e003
            mechanic: p2.solar_ray
            who: {slot: ST, job: WAR}
            do:
              - {action: 7535, name: Reprisal}
          - id: e004
            mechanic: p2.solar_ray
            who: {slot: H3}
            do:
              - {action: 16536, name: Temperance}
        """;

    public static TheoryData<string> Forms => new() { "yaml", "json", "share code", "parse" };

    private static MitPlan Read(string form)
    {
        var plan = PlanSerializer.FromYaml(Yaml);
        return form switch
        {
            "yaml" => plan,
            "json" => PlanSerializer.FromJson(PlanSerializer.ToJson(Unnormalized(plan))),
            "share code" => PlanSerializer.FromShareCode(PlanSerializer.ToShareCode(Unnormalized(plan))),
            _ => PlanSerializer.Parse(Yaml),
        };
    }

    /// <summary>The plan as an older MitCue or another tool wrote it: with the synonyms.</summary>
    private static MitPlan Unnormalized(MitPlan plan)
    {
        plan.Assignees[0].Slot = "D3";
        plan.Entries[1].Who.Slot = "T1";
        plan.Entries[2].Who.Slot = "ST";
        return plan;
    }

    [Theory]
    [MemberData(nameof(Forms))]
    public void EveryReaderWritesSlotSynonymsAsTheStandardSlots(string form)
    {
        var plan = Read(form);

        Assert.Equal(["R1", "MT", "H3"], plan.Assignees.Select(a => a.Slot));
        Assert.Equal(["R1", "MT", "OT", "H3"], plan.Entries.Select(e => e.Who.Slot)); // "H3" is no slot: kept as is
        Assert.Equal(["e001"], PlanSelection.For(plan, new PlayerContext(Job.BRD, 90, "R1")).Select(e => e.Id));
        Assert.Equal(["e002"], PlanSelection.For(plan, new PlayerContext(Job.WAR, 90, "MT")).Select(e => e.Id));
        Assert.Equal(["e003"], PlanSelection.For(plan, new PlayerContext(Job.WAR, 90, "OT")).Select(e => e.Id));
    }

    /// <summary>Healer columns as an AI answer or a hand-edited plan may write them: the Astrologian's call names no job.</summary>
    private const string HealerColumnsYaml = """
        format: mitcue-plan/1
        id: healer-columns
        name: Healer columns
        fight: top
        assignees:
          - {key: H1 WHM, slot: H1, job: WHM}
          - {key: H1 AST, slot: H1, job: AST}
          - {key: H1, slot: H1}
        entries:
          - id: whm
            mechanic: p1.pantokrator
            who: {slot: H1, job: WHM, assignee: H1 WHM}
            do:
              - {action: 16536, name: Temperance}
          - id: ast
            mechanic: p1.pantokrator
            who: {slot: H1, assignee: h1 ast}
            do:
              - {action: 3613, name: Collective Unconscious}
          - id: h1
            mechanic: p2.solar_ray
            who: {slot: H1, assignee: H1}
            do:
              - {note: Everything}
        """;

    [Theory]
    [MemberData(nameof(Forms))]
    public void AnEntryOfAColumnWithAJobIsThatJobsCall(string form)
    {
        // Review round 4: the job-less call of "H1 AST" was shown to the White Mage who follows "H1 WHM" in H1 (another
        // column's call for their slot), although the review and the plan checks read it as an Astrologian's call.
        var plan = PlanSerializer.FromYaml(HealerColumnsYaml);
        plan = form switch
        {
            "yaml" => plan,
            "json" => PlanSerializer.FromJson(PlanSerializer.ToJson(WithoutJob(plan))),
            "share code" => PlanSerializer.FromShareCode(PlanSerializer.ToShareCode(WithoutJob(plan))),
            _ => PlanSerializer.Parse(HealerColumnsYaml),
        };

        Assert.Equal([Job.WHM, Job.AST, null], plan.Entries.Select(e => e.Who.Job)); // the job-less "H1" column's call stays job-less
        Assert.Equal(["whm", "h1"], PlanSelection.For(plan, new PlayerContext(Job.WHM, 90, "H1", "H1 WHM")).Select(e => e.Id));
        Assert.Equal(["ast", "h1"], PlanSelection.For(plan, new PlayerContext(Job.AST, 90, "H1", "H1 AST")).Select(e => e.Id));
        Assert.Equal(["whm", "h1"], PlanSelection.For(plan, new PlayerContext(Job.WHM, 90, "H1")).Select(e => e.Id));

        static MitPlan WithoutJob(MitPlan plan)
        {
            plan.Entries[1].Who.Job = null;
            return plan;
        }
    }

    [Fact]
    public void PlansBuiltInCodeAreNormalizedOnRequest()
    {
        var plan = new MitPlan
        {
            Assignees = [new Assignee { Key = "D1", Slot = "d1" }, null!],
            Entries = [new PlanEntry { Id = "e1", Who = new WhoSpec { Slot = "D4" } }, null!, new PlanEntry { Id = "e2", Who = null! }],
        };

        PlanNormalizer.NormalizeSlots(plan);

        Assert.Equal("M1", plan.Assignees[0].Slot);
        Assert.Equal("R2", plan.Entries[0].Who.Slot);

        // Jobs from columns: only for an entry that names a column with a job and no job of its own.
        var columns = new MitPlan
        {
            Assignees = [new Assignee { Key = "SCH", Job = Job.SCH }, new Assignee { Key = "Notes", Job = Job.None }, null!],
            Entries =
            [
                new PlanEntry { Id = "e1", Who = new WhoSpec { Assignee = "sch" } },
                new PlanEntry { Id = "e2", Who = new WhoSpec { Assignee = "SCH", Job = Job.SGE } },
                new PlanEntry { Id = "e3", Who = new WhoSpec { Assignee = "Notes" } },
                new PlanEntry { Id = "e4", Who = new WhoSpec { Assignee = "Unlisted" } },
                null!,
                new PlanEntry { Id = "e5", Who = null! },
            ],
        };

        PlanNormalizer.Normalize(columns);

        Assert.Equal([Job.SCH, Job.SGE, null, null], columns.Entries.Take(4).Select(e => e.Who.Job));
    }
}
