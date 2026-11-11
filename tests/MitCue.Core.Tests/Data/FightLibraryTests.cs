using MitCue.Core.Data;
using MitCue.Core.Fights;

namespace MitCue.Core.Tests.Data;

/// <summary><see cref="FightLibrary"/>: every fight file loads on its own; bad files are reported, not fatal.</summary>
public class FightLibraryTests
{
    private static string Fight(string key, int territory) =>
        $"fight: {key}\nname: {key.ToUpperInvariant()}\nterritory: {territory}\nsync_level: 90\ntimeline: {key}.txt\n" +
        "phases:\n  - { id: p1, start: 0 }\nmechanics:\n  - { id: p1.pull, hit: 0, kind: marker }\n";

    [Fact]
    public void BadFiles_AreLeftOut_AndTheOthersStillLoad()
    {
        var data = new MemoryData(new()
        {
            ["fights/a.yaml"] = Fight("alpha", 10),
            ["fights/b.yaml"] = "fight: beta\nphases: [ {id: p1\n",                 // invalid YAML
            ["fights/c.yaml"] = Fight("gamma", 30),
            ["fights/d.yaml"] = null,                                               // unreadable
            ["fights/e.yaml"] = Fight("epsilon", 50).Replace("territory: 50\n", string.Empty, StringComparison.Ordinal),
            ["fights/notes.md"] = "not a fight",
        });

        var library = new FightLibrary(data);

        Assert.Equal(["alpha", "gamma"], library.Fights.Select(f => f.Key));
        Assert.Equal(["fights/b.yaml", "fights/d.yaml", "fights/e.yaml"], library.Problems.Select(p => p.Path));
        Assert.All(library.Problems, p => Assert.StartsWith(p.Path + ":", p.Message, StringComparison.Ordinal));
        Assert.All(library.Problems, p => Assert.Equal(p.Message, p.ToString()));

        var (yaml, io, missing) = (library.Problems[0], library.Problems[1], library.Problems[2]);
        Assert.IsType<FormatException>(yaml.Exception);
        Assert.Contains("invalid YAML", yaml.Message, StringComparison.Ordinal);
        Assert.IsType<IOException>(io.Exception);
        Assert.Contains("cannot read fights/d.yaml", io.Message, StringComparison.Ordinal);
        Assert.IsType<FormatException>(missing.Exception);
        Assert.Contains("'territory' is missing", missing.Message, StringComparison.Ordinal);

        Assert.NotNull(library.Get("gamma"));
        Assert.Null(library.Get("beta"));
        Assert.NotNull(library.ForTerritory(30));
    }

    [Fact]
    public void AllFilesBad_GivesAnEmptyLibrary()
    {
        var library = new FightLibrary(new MemoryData(new() { ["fights/x.yaml"] = "- a list", ["fights/y.yaml"] = string.Empty }));
        Assert.Empty(library.Fights);
        Assert.Equal(2, library.Problems.Count);
        Assert.Null(library.ForTerritory(1));
    }

    [Fact]
    public void DuplicateKey_KeepsTheFirstFile()
    {
        var library = new FightLibrary(new MemoryData(new()
        {
            ["fights/a.yaml"] = Fight("top", 1122),
            ["fights/b.yaml"] = Fight("TOP", 2000),
        }));

        var fight = Assert.Single(library.Fights);
        Assert.Equal(1122, fight.Territory);
        Assert.Null(library.ForTerritory(2000));
        var problem = Assert.Single(library.Problems);
        Assert.Equal("fights/b.yaml", problem.Path);
        Assert.Null(problem.Exception);
        Assert.Contains("already defined by fights/a.yaml", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateTerritory_KeepsBoth_TheZoneOpensTheFirst()
    {
        var library = new FightLibrary(new MemoryData(new()
        {
            ["fights/one.yaml"] = Fight("one", 777),
            ["fights/two.yaml"] = Fight("two", 777),
        }));

        Assert.Equal(["one", "two"], library.Fights.Select(f => f.Key));
        Assert.Equal("one", library.ForTerritory(777)!.Key);
        Assert.Equal("two", library.Get("two")!.Key);
        var problem = Assert.Single(library.Problems);
        Assert.Equal("fights/two.yaml", problem.Path);
        Assert.Null(problem.Exception);
        Assert.Contains("territory 777", problem.Message, StringComparison.Ordinal);
        Assert.Contains("'one' (fights/one.yaml)", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnlistableFolder_GivesAnEmptyLibraryWithAProblem()
    {
        var library = new FightLibrary(new MemoryData([], failList: true));

        Assert.Empty(library.Fights);
        var problem = Assert.Single(library.Problems);
        Assert.Equal("fights/", problem.Path);
        Assert.IsType<IOException>(problem.Exception);
    }

    [Fact]
    public void NoFightFiles_GivesAnEmptyLibraryWithoutProblems()
    {
        var library = new FightLibrary(new MemoryData(new() { ["timelines/x.txt"] = "0.0 \"--sync--\"" }));
        Assert.Empty(library.Fights);
        Assert.Empty(library.Problems);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new FightLibrary(null!));
        Assert.Throws<ArgumentNullException>(() => new FightLibrary(new MemoryData([])).LoadTimeline(null!));
    }

    [Fact]
    public void BundledFights_LoadWithoutProblems()
    {
        Assert.Empty(new FightLibrary(new EmbeddedDataSource()).Problems);
        Assert.Empty(new FightLibrary(RepoFiles.Data).Problems);
    }

    /// <summary>In-memory data: path → text, or null for a file that cannot be read.</summary>
    private sealed class MemoryData(Dictionary<string, string?> files, bool failList = false) : IDataSource
    {
        public IEnumerable<string> List(string folder) =>
            failList
                ? throw new IOException("the folder cannot be listed")
                : files.Keys.Where(k => k.StartsWith(folder + "/", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();

        public bool Exists(string path) => files.ContainsKey(path);

        public string ReadText(string path) => files[path] ?? throw new IOException($"cannot read {path}");
    }
}
