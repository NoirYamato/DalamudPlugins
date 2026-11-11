using MitCue.Core.Import;

namespace MitCue.Core.Tests.Import;

public class AssigneeHeaderTests
{
    [Theory]
    // LPDU
    [InlineData("MT", "MT", AssigneeKind.Slot, "MT", Job.None, Role.Tank)]
    [InlineData("H2", "H2", AssigneeKind.Slot, "H2", Job.None, Role.Healer)]
    [InlineData("R1", "R1", AssigneeKind.Slot, "R1", Job.None, Role.None)] // physical ranged or caster (LPDU's R1 assigns Dismantle)
    [InlineData("R2", "R2", AssigneeKind.Slot, "R2", Job.None, Role.None)]
    [InlineData("H2 SCH", "H2 SCH", AssigneeKind.SlotAndJob, "H2", Job.SCH, Role.Healer)]
    [InlineData("H1 WHM", "H1 WHM", AssigneeKind.SlotAndJob, "H1", Job.WHM, Role.Healer)]
    [InlineData("CM LB Tank", "CM LB Tank", AssigneeKind.Custom, null, Job.None, Role.Tank)]
    [InlineData("CM Rep Tank", "CM Rep Tank", AssigneeKind.Custom, null, Job.None, Role.Tank)]
    [InlineData("WAR", "WAR", AssigneeKind.Job, null, Job.WAR, Role.Tank)]
    [InlineData("DRK", "DRK", AssigneeKind.Job, null, Job.DRK, Role.Tank)]
    // Carry the Clear
    [InlineData("White Mage", "White Mage", AssigneeKind.Job, null, Job.WHM, Role.Healer)]
    [InlineData("Scholar", "Scholar", AssigneeKind.Job, null, Job.SCH, Role.Healer)]
    // other common spellings
    [InlineData("  H2   SCH ", "H2 SCH", AssigneeKind.SlotAndJob, "H2", Job.SCH, Role.Healer)]
    [InlineData("SCH (H2)", "SCH (H2)", AssigneeKind.SlotAndJob, "H2", Job.SCH, Role.Healer)]
    [InlineData("H2 - Sage", "H2 - Sage", AssigneeKind.SlotAndJob, "H2", Job.SGE, Role.Healer)]
    [InlineData("D3", "D3", AssigneeKind.Slot, "R1", Job.None, Role.None)]
    [InlineData("R1 MCH", "R1 MCH", AssigneeKind.SlotAndJob, "R1", Job.MCH, Role.PhysicalRanged)]
    [InlineData("R2 - Black Mage", "R2 - Black Mage", AssigneeKind.SlotAndJob, "R2", Job.BLM, Role.Caster)]
    [InlineData("Healer #1", "Healer #1", AssigneeKind.Slot, "H1", Job.None, Role.Healer)] // "#" numbering, not statistics
    [InlineData("Tank #2", "Tank #2", AssigneeKind.Slot, "OT", Job.None, Role.Tank)]
    [InlineData("T1", "T1", AssigneeKind.Slot, "MT", Job.None, Role.Tank)]
    [InlineData("Main Tank", "Main Tank", AssigneeKind.Slot, "MT", Job.None, Role.Tank)]
    [InlineData("Off Tank", "Off Tank", AssigneeKind.Slot, "OT", Job.None, Role.Tank)]
    [InlineData("Healer 2", "Healer 2", AssigneeKind.Slot, "H2", Job.None, Role.Healer)]
    [InlineData("Dark Knight", "Dark Knight", AssigneeKind.Job, null, Job.DRK, Role.Tank)]
    [InlineData("pld", "pld", AssigneeKind.Job, null, Job.PLD, Role.Tank)]
    [InlineData("Shield Healer", "Shield Healer", AssigneeKind.Custom, null, Job.None, Role.Healer)]
    [InlineData("Tanks", "Tanks", AssigneeKind.Custom, null, Job.None, Role.Tank)]
    [InlineData("MT:", "MT", AssigneeKind.Slot, "MT", Job.None, Role.Tank)]
    public void TryParse_ReadsSlotsJobsAndRoleNames(string header, string key, AssigneeKind kind, string? slot, Job job, Role role)
    {
        Assert.True(AssigneeHeader.TryParse(header, out var a));
        Assert.Equal(key, a.Key);
        Assert.Equal(kind, a.Kind);
        Assert.Equal(slot, a.Slot);
        Assert.Equal(job == Job.None ? null : job, a.Job);
        Assert.Equal(role, a.Role);
        Assert.Equal(job == Job.None ? Array.Empty<Job>() : [job], a.Jobs);
    }

    [Fact]
    public void TryParse_ReadsSeveralJobs()
    {
        Assert.True(AssigneeHeader.TryParse("H1 WHM/AST", out var a));
        Assert.Equal(AssigneeKind.SlotAndJob, a.Kind);
        Assert.Equal("H1", a.Slot);
        Assert.Null(a.Job);
        Assert.Equal([Job.WHM, Job.AST], a.Jobs);
        Assert.Equal(Role.Healer, a.Role);

        Assert.True(AssigneeHeader.TryParse("SCH/SGE", out var b));
        Assert.Equal(AssigneeKind.Job, b.Kind);
        Assert.Equal([Job.SCH, Job.SGE], b.Jobs);

        // Jobs of different roles in a ranged slot: the slot does not say which role either.
        Assert.True(AssigneeHeader.TryParse("R1 MCH/BLM", out var c));
        Assert.Equal([Job.MCH, Job.BLM], c.Jobs);
        Assert.Equal(Role.None, c.Role);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Notes")]
    [InlineData("P1 ")]
    [InlineData("P3")]
    [InlineData("Top WHMs (of 36)")]
    [InlineData("TOP — White Mage")]
    [InlineData("Damage Events")]
    [InlineData("Mits Suggested to Live")]
    [InlineData("Healer LB3")]
    [InlineData("Tank LB3")]
    [InlineData("Kitchen Sink")]
    [InlineData("PLD Wings")]
    [InlineData("Party Mit")]
    [InlineData("24")]
    [InlineData("H1 H2")]
    [InlineData("WHM WHM")]
    [InlineData("WAR > DRK > PLD > GNB")]
    [InlineData("Credits to:")]
    [InlineData("# of clears")]
    [InlineData("Healer #")]
    [InlineData("Talk About LB3 order during the cutscene!!")]
    public void TryParse_RejectsEverythingElse(string? header) => Assert.False(AssigneeHeader.TryParse(header, out _));

    [Fact]
    public void ToAssignee_CopiesKeySlotAndJob()
    {
        Assert.True(AssigneeHeader.TryParse("H2 SCH", out var a));
        var assignee = a.ToAssignee();
        Assert.Equal("H2 SCH", assignee.Key);
        Assert.Equal("H2", assignee.Slot);
        Assert.Equal(Job.SCH, assignee.Job);
    }
}
