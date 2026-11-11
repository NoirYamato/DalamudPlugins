using MitCue.Core.Data;
using MitCue.PluginTests.Support;

namespace MitCue.PluginTests;

/// <summary>
/// <see cref="CoreReadiness"/>: the integration tests are skipped only while a piece of MitCue.Core is a stub. Broken
/// bundled fight data fails them instead: the fight library leaves a bad file out rather than throwing, so a "no TOP
/// fight" skip would hide it and the suite would pass.
/// </summary>
public class CoreReadinessTests
{
    [Fact]
    public void TheBundledData_IsReady()
    {
        Assert.Null(CoreReadiness.Check(new EmbeddedDataSource()));
    }

    [Fact]
    public void ABrokenFightFile_FailsTheCheck_WithTheProblem()
    {
        var top = new EmbeddedDataSource().ReadText("fights/top.yaml");
        Assert.Contains("territory: 1122", top, StringComparison.Ordinal);
        var data = new MemoryData(new() { ["fights/top.yaml"] = top.Replace("territory: 1122", "territory: [1122", StringComparison.Ordinal) });

        var ex = Assert.Throws<InvalidOperationException>(() => CoreReadiness.Check(data));

        Assert.Contains("fights/top.yaml", ex.Message, StringComparison.Ordinal);
        Assert.Contains("invalid YAML", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NoTopFight_FailsTheCheck()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CoreReadiness.Check(new MemoryData([])));

        Assert.Contains("no TOP fight", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Files held in memory, by data path.</summary>
    private sealed class MemoryData(Dictionary<string, string> files) : IDataSource
    {
        public IEnumerable<string> List(string folder)
        {
            var prefix = folder.TrimEnd('/') + "/";
            return files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal);
        }

        public bool Exists(string path) => files.ContainsKey(path);

        public string ReadText(string path) =>
            files.TryGetValue(path, out var text) ? text : throw new FileNotFoundException("Not in the test data: " + path);
    }
}
