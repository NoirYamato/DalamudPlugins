using MitCue.Core.Import;
using MitCue.Core.Plans;

namespace MitCue.Core.Tests.Import;

/// <summary>Carry the Clear's 'TOP – WHM + SCH' tab: both columns, sequences, per-step times and a phase-end action.</summary>
public class CarryTheClearWhmSchImportTests
{
    private const string Tab = "'TOP – WHM + SCH'!";

    private static MitPlan Plan => PlanImports.WhmSch.Plan;

    [Fact]
    public void BothColumns_AreAssignees()
    {
        Assert.Equal(["Scholar", "White Mage"], Plan.Assignees.Select(a => a.Key));
        Assert.Equal([Job.SCH, Job.WHM], Plan.Assignees.Select(a => a.Job!.Value));
        Assert.All(Plan.Entries, e => Assert.Equal(e.Who.Assignee == "Scholar" ? Job.SCH : Job.WHM, e.Who.Job));
        Assert.Equal(["TOP – WHM + SCH"], Plan.Source!.Tabs);
        Assert.Equal(PlanImports.CtcUrl + "#gid=777300", Plan.Source.Url);
    }

    [Fact]
    public void Sequence_KeepsOrderAndUsesItsTime()
    {
        // "Protraction + Recit → Adlo → Deploy (1:07)" on Pantokrator (1:21): one entry, pressed in order, 14 s before.
        var deploy = Assert.Single(Plan.At(Tab + "C18"), e => e.Seq);
        Assert.Equal(["Protraction", "Recitation", "Adloquium", "Deployment Tactics"], deploy.Names());
        Assert.Equal("p1.pantokrator", deploy.Mechanic);
        Assert.Equal(-14, deploy.Offset!.Value, 1);
    }

    [Fact]
    public void Sequence_WithStepTimes_StartsAtItsFirstStep()
    {
        // "Protraction + Recit (2:44–2:46) → Adlo (2:50)" on the third Solar Ray (3:00): the sequence starts at 2:44.
        var entry = Assert.Single(Plan.At(Tab + "C51"), e => e.Who.Job == Job.SCH);
        Assert.True(entry.Seq);
        Assert.Equal(["Protraction", "Recitation", "Adloquium"], entry.Names());
        Assert.Equal("p5.solar_ray_3", entry.Mechanic);
        Assert.Equal(-16, entry.Offset!.Value, 1);
        Assert.Equal("a non-tank", entry.Target); // "SCH Protraction + Recit → Adlo (2:50) on a non-tank"
    }

    [Fact]
    public void JustBeforeP3Ends_IsAPhaseEndEntry()
    {
        // "Protraction + Recit → Adlo → Deploy (≈3:04, just before P3 ends)" on the Ion Efflux enrage row (3:08).
        var entry = Assert.Single(Plan.At(Tab + "C38"));
        Assert.True(entry.AtPhaseEnd);
        Assert.True(entry.Seq);
        Assert.Equal("p3.ion_efflux", entry.Mechanic);
        Assert.Equal(Fights.MechanicKind.Enrage, PlanImports.Top.GetMechanic("p3.ion_efflux")!.Kind);
        Assert.Equal(-4, entry.Offset!.Value, 1);
        Assert.Equal("Deploy just before P3 ends", entry.Note);
    }

    [Theory]
    [InlineData("C17", "Excogitation", "p1.program_loop", 0, -8.0)]
    [InlineData("C22", "Protraction", "p2.solar_ray", 0, -5.0)]
    [InlineData("C33", "Summon Seraph", "p3.latent_2", 0, -10.0)]
    [InlineData("C40", "Expedient", "p4.wave_cannon", 1, -1.0)]
    [InlineData("C41", "Sacred Soil", "p4.blue_screen", 0, -10.0)]       // Blue Screen is timed at the hit, 1.0 s after its anchor: δ = +1
    [InlineData("C45", "Summon Seraph", "p5.delta_all", 5, -3.0)]
    [InlineData("C55", "Sacred Soil", "p5.blind_faith", 0, -10.2)]       // δ = −1.2
    [InlineData("C57", "Whispering Dawn", "p6.cosmo_memory", 0, 3.0)]
    [InlineData("C67", "Angel Feathers", "p6.magic_number_2", 0, 4.0)]   // 'Healer LB3 (≈4:48, after the hit)' for a Scholar
    public void ScholarEntries(string cell, string action, string mechanic, int hit, double offset)
    {
        var entry = Assert.Single(Plan.At(Tab + cell), e => e.Who.Job == Job.SCH && e.Names()[0] == action);
        Assert.Equal(mechanic, entry.Mechanic);
        Assert.Equal(hit, entry.HitIndex ?? 0);
        Assert.InRange(entry.Offset!.Value, offset - 0.6, offset + 0.6);
    }

    [Fact]
    public void WhenReady_AndOptional_FollowTheMarks()
    {
        Assert.All(Plan.At(Tab + "C33"), e => Assert.True(e.WhenReady)); // every item of row 33 has ⏱
        var omega = Plan.At(Tab + "C52").Where(e => e.Who.Job == Job.SCH).ToList();
        Assert.Equal(["Fey Illumination", "Summon Seraph"], omega.Where(e => e.Optional).Select(e => e.Names()[0]).Order()); // grey
        Assert.Equal(["Deployment Tactics", "Sacred Soil"], omega.Where(e => !e.Optional).Select(e => e.Names()[0]).Order());
    }

    [Fact]
    public void NoWarnings()
    {
        Assert.DoesNotContain(PlanImports.WhmSch.Diagnostics, d => d.Severity != DiagnosticSeverity.Info);
    }
}
