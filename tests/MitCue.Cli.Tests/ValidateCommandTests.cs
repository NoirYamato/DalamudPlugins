namespace MitCue.Cli.Tests;

/// <summary><c>validate</c> on the repository's data and on broken copies of it.</summary>
public class ValidateCommandTests : IDisposable
{
    private readonly CliKit kit = new();

    public void Dispose() => this.kit.Dispose();

    /// <summary>Copies data/ into the work directory (or a folder of it) and returns the copy's path.</summary>
    private string CopyData(string? under = null)
    {
        var source = Path.Combine(CliKit.RepoRoot, "data");
        var target = Path.Combine(this.kit.WorkDir, under ?? string.Empty, "data");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest);
        }

        return target;
    }

    private static void Replace(string path, string find, string replace)
    {
        var text = File.ReadAllText(path);
        Assert.Contains(find, text);
        File.WriteAllText(path, text.Replace(find, replace, StringComparison.Ordinal));
    }

    [Fact]
    public void RepositoryData_IsValid()
    {
        var r = this.kit.Run("validate");
        Assert.True(r.ExitCode == 0, r.ToString());
        Assert.StartsWith("Data: " + Path.Combine(CliKit.RepoRoot, "data"), r.Out);
        Assert.Matches(@"ok\s+fights/top\.yaml: top, The Omega Protocol \(Ultimate\): level 90, 6 phases, \d+ mechanics; \d+ entries checked against the_omega_protocol\.txt", r.Out);
        Assert.Matches(@"warn\s+timelines/the_epic_of_alexander\.txt: 225 entries, 144 syncs, 0 labels \(1 warning\)", r.Out);
        Assert.Matches(@"ok\s+actions/actions\.yaml: \d+ actions \(game version 7\.58\)", r.Out);
        Assert.Matches(@"ok\s+actions/aliases\.yaml: \d+ keys; every alias resolves for its jobs at levels 80, 90 and 100", r.Out);
        Assert.Contains("Result: 0 problems, ", r.Out);
    }

    [Fact]
    public void BrokenData_IsReported()
    {
        var data = this.CopyData();
        Replace(Path.Combine(data, "fights", "top.yaml"), "entry: \"Solar Ray\"", "entry: \"Solar Rays\"");
        Replace(Path.Combine(data, "actions", "aliases.yaml"), "aliases:\n", "aliases:\n  zzz test: Not An Action\n");

        var r = this.kit.Run("validate", "--data", data);
        Assert.True(r.ExitCode == 1, r.ToString());
        Assert.Matches(@"FAIL\s+fights/top\.yaml: .*\(1 problem\)", r.Out);
        Assert.Contains("problem: p2.solar_ray expects \"Solar Rays\" at 214.5; the timeline has no such entry", r.Out);
        Assert.Contains("problem: alias 'zzz test': unknown action 'Not An Action'.", r.Out);
        Assert.Contains("warning: actions/aliases.yaml: the copy built into MitCue.Core differs from the folder: rebuild", r.Out);
        Assert.Contains("warning: fights/top.yaml: the copy built into MitCue.Core differs from the folder: rebuild", r.Out);
        Assert.Matches(@"Result: [2-9]\d* problems", r.Out);
    }

    [Fact]
    public void BuiltInCopy_IsComparedFileByFile()
    {
        var same = this.kit.Run("validate");
        Assert.Matches(@"ok\s+MitCue\.Core: \d+ data files, the same as the folder", same.Out);

        // Review regression: a changed number (Temperance's recast) left the action ids and names alone, and a stale
        // build was reported as ok.
        var data = this.CopyData();
        Replace(Path.Combine(data, "actions", "actions.yaml"), "    name: \"Temperance\"\n    jobs: [WHM]\n    level: 80\n    kind: party_mit\n    target: party\n    gcd: false\n    cast: 0\n    recast: 120\n", "    name: \"Temperance\"\n    jobs: [WHM]\n    level: 80\n    kind: party_mit\n    target: party\n    gcd: false\n    cast: 0\n    recast: 60\n");
        File.WriteAllText(Path.Combine(data, "timelines", "extra.txt"), File.ReadAllText(Path.Combine(data, "timelines", "the_omega_protocol.txt")));
        File.Delete(Path.Combine(data, "timelines", "NOTICE"));
        File.WriteAllText(Path.Combine(data, "fights", "README.md"), "Markdown is not built in.\n");

        var r = this.kit.Run("validate", "--data", data);
        Assert.True(r.ExitCode == 0, r.ToString()); // a stale build is a warning
        Assert.Matches(@"warn\s+MitCue\.Core: \d+ data files \(3 warnings\)", r.Out);
        Assert.Contains("warning: actions/actions.yaml: the copy built into MitCue.Core differs from the folder: rebuild", r.Out);
        Assert.Contains("warning: timelines/extra.txt is not in the copy built into MitCue.Core: rebuild", r.Out);
        Assert.Contains("warning: timelines/NOTICE is in the copy built into MitCue.Core but not in the folder: rebuild", r.Out);
        Assert.DoesNotContain("README.md", r.Out);

        // Line endings are not a difference (a Windows checkout).
        var crlf = this.CopyData("crlf");
        var top = Path.Combine(crlf, "fights", "top.yaml");
        File.WriteAllText(top, File.ReadAllText(top).Replace("\n", "\r\n", StringComparison.Ordinal));
        Assert.Matches(@"ok\s+MitCue\.Core: \d+ data files, the same as the folder", this.kit.Run("validate", "--data", crlf).Out);
    }

    [Fact]
    public void FilesThatDoNotLoad_AreProblems()
    {
        var data = this.CopyData();
        Replace(Path.Combine(data, "fights", "tea.yaml"), "sync_level: 80", "sync_level: eighty");
        File.WriteAllText(Path.Combine(data, "timelines", "broken.txt"), "# no entries\n");

        var r = this.kit.Run("validate", "--data", data);
        Assert.Equal(1, r.ExitCode);
        Assert.Matches(@"FAIL\s+fights/tea\.yaml \(1 problem\)", r.Out);
        Assert.Matches(@"FAIL\s+timelines/broken\.txt \(1 problem\)", r.Out);
        Assert.Contains("is not a cactbot timeline", r.Out);
    }

    [Fact]
    public void PlansNextToTheData_AreChecked()
    {
        var data = this.CopyData();
        this.kit.Write(Path.Combine("plans", "good.yaml"), SamplePlans.Whm);
        this.kit.Write(Path.Combine("plans", "broken.yaml"), SamplePlans.Broken);

        var r = this.kit.Run("validate", "--data", data);
        Assert.Equal(1, r.ExitCode);
        Assert.Matches(@"ok\s+plans/good\.yaml: ""Test WHM"" \(top\): 10 entries", r.Out);
        Assert.Matches(@"FAIL\s+plans/broken\.yaml: ""Broken WHM"" \(top\): 4 entries \(3 problems, 1 warning\)", r.Out);
        Assert.Contains("problem: WHM: unknown-mechanic: Entry b3: unknown mechanic 'p2.cosmo_memry'", r.Out);
        Assert.Contains("problem: WHM: wrong-job: Kerachole is a SGE action, not WHM.", r.Out);
        Assert.Contains("problem: WHM: not-available-at-level: Divine Caress at P3 0:41 is learned at level 100", r.Out);

        // Recast conflicts are advice for the plan's author, not broken data.
        Assert.Contains("warning: WHM: recast-conflict: Temperance planned at P2 0:10 and P2 0:48", r.Out);
    }
}
