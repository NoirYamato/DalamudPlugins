using MitCue.Core.Data;
using MitCue.Core.Fights;

namespace MitCue.Core.Tests.Data;

/// <summary><see cref="FightLoader"/> against the schema of docs/DESIGN.md §3.1.</summary>
public class FightLoaderTests
{
    private const string Sample = """
        # A made-up fight in the DESIGN §3.1 format.
        fight: test
        name: Test Fight (Ultimate)
        territory: 1234
        sync_level: 90
        timeline: test_fight.txt
        notes: "unknown top-level keys are ignored"
        phases:
          - { id: p1, name: "P1: Start", start: 0.0 }
          - { id: p2, start: 200.0, sync: "ignored too" }
        mechanics:
          - id: p2.later
            name: Later
            phase: p2
            hit: 250.5
            entry: "Later"
            kind: raidwide
            comment: ignored
          - id: p1.multi
            name: Multi
            hit: 20.0
            hits: [20.0, 22.5, 25.0]
            entry: "Multi 1"
            kind: multihit
            aliases: ["Multi", "MH"]
          - id: p1.check
            phase: p1
            hit: 30
            kind: lb_check
            entry: "Check"
          - id: p1.pull
            hit: 0
            kind: marker
          - id: p2.buster
            hits: [210.2, 213.4]
            kind: tank_buster
            aliases: Buster
        calibration:
          carry_the_clear: { p1: 0.0, p2: 202.0 }
          lpdu: { p1: 0.5, p2: ~ }
          unknown_sheet: ~
        """;

    private static FightDefinition LoadSample() => FightLoader.Load(Sample);

    [Fact]
    public void Load_ReadsTheFightHeader()
    {
        var fight = LoadSample();
        Assert.Equal("test", fight.Key);
        Assert.Equal("Test Fight (Ultimate)", fight.Name);
        Assert.Equal((ushort)1234, fight.Territory);
        Assert.Equal(90, fight.SyncLevel);
        Assert.Equal("test_fight.txt", fight.TimelineFile);
    }

    [Fact]
    public void Load_ReadsPhasesInFileOrder()
    {
        var fight = LoadSample();
        Assert.Equal(["p1", "p2"], fight.Phases.Select(p => p.Id));
        Assert.Equal("P1: Start", fight.Phases[0].Name);
        Assert.Equal("p2", fight.Phases[1].Name); // name defaults to the id
        Assert.Equal(200.0, fight.Phases[1].Start);
        Assert.Equal("p2", fight.PhaseAt(250)!.Id);
    }

    [Fact]
    public void Load_SortsMechanicsByHit()
    {
        var fight = LoadSample();
        Assert.Equal(["p1.pull", "p1.multi", "p1.check", "p2.buster", "p2.later"], fight.Mechanics.Select(m => m.Id));
        Assert.Equal(fight.Mechanics.Select(m => m.Hit).Order(), fight.Mechanics.Select(m => m.Hit));
    }

    [Fact]
    public void Load_ReadsMechanicFields()
    {
        var fight = LoadSample();

        var multi = fight.GetMechanic("P1.MULTI")!;
        Assert.Equal("Multi", multi.Name);
        Assert.Equal("p1", multi.Phase); // inferred from the hit time
        Assert.Equal(20.0, multi.Hit);
        Assert.Equal([20.0, 22.5, 25.0], multi.Hits);
        Assert.Equal(22.5, multi.HitAt(1));
        Assert.Equal("Multi 1", multi.Entry);
        Assert.Equal(MechanicKind.Multihit, multi.Kind);
        Assert.Equal(["Multi", "MH"], multi.Aliases);

        var later = fight.GetMechanic("p2.later")!;
        Assert.Equal([250.5], later.Hits); // hits default to [hit]
        Assert.Equal(MechanicKind.Raidwide, later.Kind);
        Assert.Empty(later.Aliases);

        var check = fight.GetMechanic("p1.check")!;
        Assert.Equal(MechanicKind.LbCheck, check.Kind);
        Assert.Equal("Check", check.Name); // name defaults to the entry

        var pull = fight.GetMechanic("p1.pull")!;
        Assert.Equal(MechanicKind.Marker, pull.Kind);
        Assert.Equal("p1.pull", pull.Name); // ... or the id
        Assert.Null(pull.Entry);

        var buster = fight.GetMechanic("p2.buster")!;
        Assert.Equal(210.2, buster.Hit); // hit defaults to the first of hits
        Assert.Equal("p2", buster.Phase);
        Assert.Equal(MechanicKind.Tankbuster, buster.Kind);
        Assert.Equal(["Buster"], buster.Aliases);
    }

    [Fact]
    public void Load_ReadsCalibrationPresets()
    {
        var fight = LoadSample();
        Assert.Equal(2, fight.Calibration.Count);
        Assert.Equal(202.0, fight.Calibration["carry_the_clear"]["p2"]);
        Assert.Equal(202.0, fight.Calibration["Carry_The_Clear"]["P2"]);
        Assert.Equal(["p1"], fight.Calibration["lpdu"].Keys); // null zero points are left out
    }

    [Fact]
    public void Load_WithoutOptionalSections()
    {
        var fight = FightLoader.Load("fight: x\nname: X\nterritory: 1\nsync_level: 100\ntimeline: x.txt\n");
        Assert.Empty(fight.Phases);
        Assert.Empty(fight.Mechanics);
        Assert.Empty(fight.Calibration);
    }

    [Theory]
    [InlineData("raidwide", MechanicKind.Raidwide)]
    [InlineData("tankbuster", MechanicKind.Tankbuster)]
    [InlineData("multihit", MechanicKind.Multihit)]
    [InlineData("stack", MechanicKind.Stack)]
    [InlineData("spread", MechanicKind.Spread)]
    [InlineData("mixed", MechanicKind.Mixed)]
    [InlineData("lb_check", MechanicKind.LbCheck)]
    [InlineData("enrage", MechanicKind.Enrage)]
    [InlineData("other", MechanicKind.Other)]
    [InlineData("marker", MechanicKind.Marker)]
    [InlineData("LbCheck", MechanicKind.LbCheck)]
    [InlineData("multi-hit", MechanicKind.Multihit)]
    public void Kinds_AreReadFromSnakeCase(string text, MechanicKind expected)
    {
        Assert.True(FightLoader.TryParseKind(text, out var kind));
        Assert.Equal(expected, kind);
        Assert.True(FightLoader.TryParseKind(FightLoader.KindName(expected), out var back));
        Assert.Equal(expected, back);
    }

    [Theory]
    [InlineData("")]
    [InlineData("3")]
    [InlineData("boss")]
    public void Kinds_RejectUnknownValues(string text) => Assert.False(FightLoader.TryParseKind(text, out _));

    /// <summary>Each broken variant of the sample must fail with a message naming the mechanic/phase and field.</summary>
    [Theory]
    [InlineData("hit: 20.0\n", "hit: abc\n", "p1.multi", "'hit'")]
    [InlineData("kind: multihit", "kind: spicy", "p1.multi", "spicy")]
    [InlineData("kind: multihit", "kind: spicy", "p1.multi", "lb_check")]
    [InlineData("id: p1.check", "id: p1.multi", "p1.multi", "duplicate")]
    [InlineData("phase: p1\n", "phase: p9\n", "p1.check", "p9")]
    [InlineData("hits: [20.0, 22.5, 25.0]", "hits: [20.0, 25.0, 22.5]", "p1.multi", "order")]
    [InlineData("hits: [20.0, 22.5, 25.0]", "hits: [19.0, 20.0, 25.0]", "p1.multi", "first")]
    [InlineData("hits: [20.0, 22.5, 25.0]", "hits: [21.0, 25.0]", "p1.multi", "not one of")]
    [InlineData("hits: [20.0, 22.5, 25.0]", "hits: [20.0, x]", "p1.multi", "'hits'")]
    [InlineData("    hit: 0\n", "    when: 0\n", "p1.pull", "'hit' is missing")]
    [InlineData("aliases: [\"Multi\", \"MH\"]", "aliases: {a: b}", "p1.multi", "aliases")]
    [InlineData("{ id: p2, start: 200.0,", "{ id: p1, start: 200.0,", "p1", "duplicate phase")]
    [InlineData("start: 200.0", "begin: 200.0", "p2", "'start' is missing")]
    [InlineData("territory: 1234", "territory: 70000", "test", "territory")]
    [InlineData("territory: 1234", "territory: TOP", "test", "territory")]
    [InlineData("sync_level: 90", "sync_level: 0", "test", "sync_level")]
    [InlineData("timeline: test_fight.txt", "timeline_file: test_fight.txt", "test", "'timeline' is missing")]
    [InlineData("name: Test Fight (Ultimate)", "title: Test Fight", "test", "'name' is missing")]
    [InlineData("p2: 202.0", "p2: soon", "carry_the_clear", "p2")]
    [InlineData("lpdu: { p1: 0.5, p2: ~ }", "lpdu: [0.5]", "lpdu", "calibration")]
    public void Load_ReportsTheOffendingItemAndField(string find, string replace, string item, string field)
    {
        Assert.Contains(find, Sample, StringComparison.Ordinal);
        var ex = Assert.Throws<FormatException>(() => FightLoader.Load(Sample.Replace(find, replace, StringComparison.Ordinal)));
        Assert.Contains(item, ex.Message, StringComparison.Ordinal);
        Assert.Contains(field, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ReportsMissingFightKeyAndBrokenYaml()
    {
        var noKey = Assert.Throws<FormatException>(() => FightLoader.Load(Sample.Replace("fight: test", "key: test", StringComparison.Ordinal), "fights/test.yaml"));
        Assert.Contains("fights/test.yaml", noKey.Message, StringComparison.Ordinal);
        Assert.Contains("'fight' is missing", noKey.Message, StringComparison.Ordinal);

        var broken = Assert.Throws<FormatException>(() => FightLoader.Load("fight: x\nphases: [ {id: p1\n", "fights/x.yaml"));
        Assert.Contains("fights/x.yaml", broken.Message, StringComparison.Ordinal);

        Assert.Throws<FormatException>(() => FightLoader.Load(string.Empty));
        Assert.Throws<FormatException>(() => FightLoader.Load("- just\n- a list\n"));
    }

    [Fact]
    public void Load_ToleratesAByteOrderMark() => Assert.Equal("test", FightLoader.Load("\uFEFF" + Sample).Key);

    [Fact]
    public void FightLibrary_LoadsEveryFileAndNamesTheBrokenOne()
    {
        var root = Path.Combine(Path.GetTempPath(), "mitcue-fights-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "fights"));
            File.WriteAllText(Path.Combine(root, "fights", "test.yaml"), Sample);
            File.WriteAllText(Path.Combine(root, "fights", "README.txt"), "not a fight");

            var library = new FightLibrary(new DirectoryDataSource(root));
            var fight = Assert.Single(library.Fights);
            Assert.Same(fight, library.Get("TEST"));
            Assert.Same(fight, library.ForTerritory(1234));
            Assert.Null(library.ForTerritory(1));
            Assert.Empty(library.Problems);

            // A broken file no longer stops the library: it is left out and named in Problems.
            File.WriteAllText(Path.Combine(root, "fights", "broken.yaml"), Sample.Replace("hit: 30", "hit: thirty", StringComparison.Ordinal));
            library = new FightLibrary(new DirectoryDataSource(root));
            Assert.Equal("test", Assert.Single(library.Fights).Key);
            var problem = Assert.Single(library.Problems);
            Assert.Equal("fights/broken.yaml", problem.Path);
            Assert.Contains("fights/broken.yaml", problem.Message, StringComparison.Ordinal);
            Assert.Contains("p1.check", problem.Message, StringComparison.Ordinal);
            Assert.IsType<FormatException>(problem.Exception);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
