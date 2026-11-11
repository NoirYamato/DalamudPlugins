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
    }
}
