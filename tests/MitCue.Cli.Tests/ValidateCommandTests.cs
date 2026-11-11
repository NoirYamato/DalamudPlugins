namespace MitCue.Cli.Tests;

/// <summary><c>validate</c> on the repository's data and on broken copies of it.</summary>
public class ValidateCommandTests : IDisposable
{
    private readonly CliKit kit = new();

    public void Dispose() => this.kit.Dispose();

    /// <summary>Copies data/ into the work directory and returns the copy's path.</summary>
    private string CopyData()
    {
        var source = Path.Combine(CliKit.RepoRoot, "data");
        var target = Path.Combine(this.kit.WorkDir, "data");
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
        Assert.Contains("Result: 0 problems, 1 warning in ", r.Out);
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
        Assert.Contains("warning: the copy built into MitCue.Core differs from the folder: rebuild", r.Out);
        Assert.Matches(@"Result: [2-9]\d* problems", r.Out);
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
        Assert.Matches(@"FAIL\s+plans/broken\.yaml: ""Broken WHM"" \(top\): 4 entries \(4 problems\)", r.Out);
        Assert.Contains("problem: WHM: recast-conflict: Temperance planned at P2 0:10 and P2 0:48", r.Out);
    }
}
