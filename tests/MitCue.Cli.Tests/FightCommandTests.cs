namespace MitCue.Cli.Tests;

/// <summary><c>fights</c>, <c>mechanics</c> and <c>timeline</c>.</summary>
public class FightCommandTests : IDisposable
{
    private readonly CliKit kit = new();

    public void Dispose() => this.kit.Dispose();

    [Fact]
    public void Fights_ListsTheThreeUltimates()
    {
        var r = this.kit.Run("fights");
        Assert.Equal(0, r.ExitCode);
        Assert.Equal("3 fights:", r.OutLines[0]);
        var top = Assert.Single(r.OutLines, l => l.TrimStart().StartsWith("top ", StringComparison.Ordinal));
        Assert.Contains("The Omega Protocol (Ultimate)", top);
        Assert.Contains("1122", top);
        Assert.Contains(" 90 ", top);
        Assert.Contains("the_omega_protocol.txt", top);
        Assert.Single(r.OutLines, l => l.TrimStart().StartsWith("tea ", StringComparison.Ordinal) && l.Contains(" 80 ", StringComparison.Ordinal) && l.Contains("887", StringComparison.Ordinal));
        Assert.Single(r.OutLines, l => l.TrimStart().StartsWith("dmu ", StringComparison.Ordinal) && l.Contains(" 100 ", StringComparison.Ordinal) && l.Contains("1363", StringComparison.Ordinal));
    }

    [Fact]
    public void Mechanics_ShowPhaseRelativeAndTimelineHits()
    {
        var r = this.kit.Run("mechanics", "top");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("TOP  The Omega Protocol (Ultimate) · territory 1122 · level 90", r.Out);
        Assert.Contains("carry_the_clear: p1 0.9, p2 201.8", r.Out);

        // P2 Cosmo Memory: 318.8 on the timeline, 1:58.8 after the P2 start at 200.
        var cosmo = Assert.Single(r.OutLines, l => l.TrimStart().StartsWith("p2.cosmo_memory ", StringComparison.Ordinal));
        Assert.Matches(@"^\s+p2\.cosmo_memory\s+p2\s+1:58\.8\s+318\.8\s+raidwide\s+Cosmo Memory", cosmo);
        var details = r.OutLines[Array.IndexOf(r.OutLines, cosmo) + 1];
        Assert.Contains("entry \"Cosmo Memory\"", details);
        Assert.Contains("aliases: ", details);
        Assert.Contains("\"CM\"", details);

        // Multi-hit mechanics list every hit.
        Assert.Contains("4 hits: 29.1, 38.1, 47.1, 56.1", r.Out);
    }

    [Fact]
    public void Mechanics_CanBeFiltered()
    {
        var r = this.kit.Run("mechanics", "top", "--phase", "p6", "--kind", "lb_check");
        Assert.Equal(0, r.ExitCode);
        var ids = r.OutLines.Select(l => l.Trim().Split(' ')[0]).Where(id => System.Text.RegularExpressions.Regex.IsMatch(id, @"^p\d\.")).ToList();
        Assert.NotEmpty(ids);
        Assert.All(ids, id => Assert.StartsWith("p6.", id));
        Assert.Equal(["p6.cosmo_memory", "p6.magic_number_1", "p6.magic_number_2"], ids);
        Assert.Contains("Mechanics (3 of ", r.Out);

        var found = this.kit.Run("mechanics", "top", "--find", "cosmo memory");
        Assert.Contains("p2.cosmo_memory", found.Out);
        Assert.Contains("p6.cosmo_memory", found.Out);
        Assert.DoesNotContain("p1.pantokrator", found.Out);
    }

    [Fact]
    public void Timeline_OfAFight()
    {
        var r = this.kit.Run("timeline", "top");
        Assert.Equal(0, r.ExitCode);
        Assert.StartsWith("the_omega_protocol.txt (fight top): 282 entries, 207 with a sync", r.Out);
        Assert.Contains("ends at 1421.9", r.Out);
        Assert.Contains("Phase anchors (# -p): 7B03 at 15.0, 7B40 at 206.4", r.Out);

        var reset = Assert.Single(r.OutLines, l => l.Contains("\"--Reset--\" (hidden)", StringComparison.Ordinal));
        Assert.Matches(@"^\s+9\s+0\.0\s+""--Reset--"" \(hidden\)\s+ActorControl \{ command: ""4000000F"" \}\s+0,100000\s+jump 0 \(stop\)$", reset);
        var p2 = Assert.Single(r.OutLines, l => l.Contains("MapEffect", StringComparison.Ordinal) && l.Contains("200.0", StringComparison.Ordinal));
        Assert.Matches(@"^\s+\d+\s+200\.0\s+""--sync--"" \(hidden\)\s+MapEffect \{ flags: ""00020001"", location: ""0A"" \}\s+200,0$", p2);
        var display = Assert.Single(r.OutLines, l => l.Contains("Diffuse Wave Cannon Kyrios x5", StringComparison.Ordinal));
        Assert.Contains("duration 8", display);
        Assert.Contains("Warnings: none", r.Out);
    }

    [Fact]
    public void Timeline_SyncsOnly()
    {
        var all = this.kit.Run("timeline", "top");
        var syncs = this.kit.Run("timeline", "top", "--syncs");
        Assert.Equal(0, syncs.ExitCode);
        Assert.Contains("Entries with a sync (207):", syncs.Out);
        Assert.DoesNotContain("Diffuse Wave Cannon Kyrios x5", syncs.Out);
        Assert.True(syncs.OutLines.Length < all.OutLines.Length);
    }

    [Fact]
    public void Timeline_ShowsLabelsJumpsAndWarnings()
    {
        var dmu = this.kit.Run("timeline", "dmu");
        Assert.Contains("Labels (8):", dmu.Out);
        Assert.Matches(@"""p2-success""\s+597\.0", dmu.Out);
        Assert.Contains("forcejump \"p2-success\" = 597.0", dmu.Out);
        Assert.Contains("Zone: 1363", dmu.Out);

        var tea = this.kit.Run("timeline", Path.Combine(CliKit.RepoRoot, "data", "timelines", "the_epic_of_alexander.txt"));
        Assert.Equal(0, tea.ExitCode);
        Assert.StartsWith("the_epic_of_alexander.txt: 225 entries", tea.Out);
        Assert.Contains("Warnings (1):", tea.Out);
        Assert.Contains("line 39: value \"Liquid Rage \"", tea.Out);
    }

    [Fact]
    public void Timeline_OfAnInvalidFile_IsAFailure()
    {
        var path = this.kit.Write("empty.txt", "# nothing here\n");
        var r = this.kit.Run("timeline", path);
        Assert.Equal(3, r.ExitCode);
        Assert.Contains("is not a cactbot timeline", r.Err);
    }

    [Fact]
    public void Output_IsDeterministic()
    {
        foreach (var args in new[] { new[] { "fights" }, ["mechanics", "dmu"], ["timeline", "tea"] })
            Assert.Equal(this.kit.Run(args).Out, this.kit.Run(args).Out);
    }
}
