using System.Globalization;
using System.Text.RegularExpressions;

namespace MitCue.Cli.Tests;

/// <summary><c>simulate</c>: generated pulls through the session logic, with pushes, misses, phase starts and wipes.</summary>
public partial class SimulateCommandTests : IDisposable
{
    private readonly CliKit kit = new();
    private readonly string whm;

    public SimulateCommandTests()
    {
        this.whm = this.kit.Write("whm.yaml", SamplePlans.Whm);
    }

    public void Dispose() => this.kit.Dispose();

    /// <summary>A log line: real time, timeline time, event, the rest.</summary>
    private sealed record Line(double Real, double Timeline, string Event, string Rest);

    [GeneratedRegex(@"^\s+(?<real>-?\d+:\d\d\.\d)\s+(?<t>-?\d+\.\d)\s+(?<event>\S+)\s+(?<rest>.*)$")]
    private static partial Regex LogLine();

    private static List<Line> Log(CliResult r) =>
        r.OutLines
            .Select(l => LogLine().Match(l))
            .Where(m => m.Success)
            .Select(m => new Line(ParseClock(m.Groups["real"].Value), double.Parse(m.Groups["t"].Value, CultureInfo.InvariantCulture), m.Groups["event"].Value, m.Groups["rest"].Value))
            .ToList();

    private static double ParseClock(string text)
    {
        var negative = text.StartsWith('-');
        var parts = text.TrimStart('-').Split(':');
        var seconds = (int.Parse(parts[0], CultureInfo.InvariantCulture) * 60) + double.Parse(parts[1], CultureInfo.InvariantCulture);
        return negative ? -seconds : seconds;
    }

    private static List<Line> For(List<Line> log, string item) => log.Where(l => l.Rest.StartsWith(item + " ", StringComparison.Ordinal)).ToList();

    [Fact]
    public void FullPull_EveryActionGoesUpcomingDueDone_AtItsPressTime()
    {
        var r = this.kit.Run("simulate", this.whm, "--job", "WHM", "--slot", "H1");
        Assert.True(r.ExitCode == 0, r.ToString());
        Assert.Equal("Simulating \"Test WHM\" (top, level 90) as WHM, slot H1: 9 entries, 10 reminders (9 actions, 1 note)", r.OutLines[0]);
        Assert.Matches(@"^Pull: seed 1, jitter ±0\.15 s, countdown 10 s; \d+ of \d+ generated events \(the types the plugin sees in game\)$", r.OutLines[1]);

        var log = Log(r);
        Assert.Equal("countdown", log[0].Event);
        Assert.Contains(log, l => l.Event == "start" && l.Real == 0 && l.Timeline == 0);
        Assert.Equal(log.OrderBy(l => l.Real).Select(l => l.Real), log.Select(l => l.Real)); // time-ordered

        // Pre-pull Medica II during the countdown: shown at -10 (lookahead 15 s), due 1.5 s before, pressed at -5.
        Assert.Equal(["Upcoming", "Due", "Done"], For(log, "Medica II").Select(l => l.Event));
        var medica = For(log, "Medica II").Single(l => l.Event == "Done");
        Assert.Equal(-5.0, medica.Timeline);
        Assert.Equal(-5.0, medica.Real);

        // Temperance three times, each done at its press time.
        var temperance = For(log, "Temperance").Where(l => l.Event == "Done").Select(l => l.Timeline).ToList();
        Assert.Equal([78.1, 299.7, 435.3], temperance);
        Assert.Contains(log, l => l.Event == "Done" && l.Rest.Contains("used at 207.0 as Divine Benison", StringComparison.Ordinal));

        // Notes pass once their hit comes.
        Assert.Equal(["Upcoming", "Due", "Passed"], For(log, "note: Medica II + Rapture straight after").Select(l => l.Event));

        Assert.Contains("Summary: 8 done, 0 missed (of 8), 0 skipped; counted: actions of non-optional entries (8).", r.Out);
        Assert.Contains("simulation finished", r.Out);
    }

    [Fact]
    public void Output_IsDeterministic_AndTheSeedChangesTheJitter()
    {
        var a = this.kit.Run("simulate", this.whm, "--job", "WHM", "--seed", "4");
        var b = this.kit.Run("simulate", this.whm, "--job", "WHM", "--seed", "4");
        Assert.Equal(a.Out, b.Out);
        var c = this.kit.Run("simulate", this.whm, "--job", "WHM", "--seed", "5");
        Assert.NotEqual(a.Out, c.Out);
        Assert.Contains("Summary: 8 done, 0 missed (of 8)", c.Out);
    }

    [Fact]
    public void Miss_LeavesThoseActionsUnpressed()
    {
        var r = this.kit.Run("simulate", this.whm, "--job", "WHM", "--miss", "temperance,7432");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("Not pressed: Temperance (16536), Divine Benison (7432)", r.Out);

        var log = Log(r);
        Assert.Equal(3, For(log, "Temperance").Count(l => l.Event == "Missed"));
        Assert.DoesNotContain(For(log, "Temperance"), l => l.Event == "Done");

        // The choice is still done with its other option.
        Assert.Contains(log, l => l.Event == "Done" && l.Rest.Contains("as Tetragrammaton", StringComparison.Ordinal));

        Assert.Contains("Summary: 5 done, 3 missed (of 8), 0 skipped", r.Out);
        Assert.Contains("Missed (3):", r.Out);
        Assert.Matches(@"P2 1:39\.7\s+Temperance\s+Optimized Meteor flares \+ Pile Pitch \(p2\.flares\)\s+e6", r.Out);
    }

    [Fact]
    public void Push_JumpsTheTimeline_AndSkipsWhatItPassed()
    {
        var r = this.kit.Run("simulate", this.whm, "--job", "WHM", "--push", "p2=1:30,p3=280");
        Assert.True(r.ExitCode == 0, r.ToString());
        Assert.Contains("Push: p2 starts at 1:30.0 (timeline 200.0); p1 ends at timeline 87.0", r.Out);
        Assert.Contains("Push: p3 starts at 4:40.0 (timeline 400.0); p2 ends at timeline 387.0", r.Out);

        var log = Log(r);
        var jumps = log.Where(l => l.Event == "jump").ToList();
        Assert.Equal(2, jumps.Count);
        Assert.Equal(203.4, jumps[0].Timeline); // P2 is entered with the 7B40 cast (the MapEffect at 200.0 is not a game event)
        Assert.InRange(jumps[0].Real, 93.0, 94.0);
        Assert.Equal(400.0, jumps[1].Timeline);

        // Benediction (1:49 on the timeline) was jumped over: skipped, at the jump.
        var bene = Assert.Single(For(log, "Benediction"));
        Assert.Equal("Skipped", bene.Event);
        Assert.Equal(jumps[0].Real, bene.Real);

        // P2 and P3 run sooner in real time: Temperance for Hello World at 4:40 + 35.3.
        var hello = For(log, "Temperance").Last(l => l.Event == "Done");
        Assert.Equal(435.3, hello.Timeline);
        Assert.InRange(hello.Real, 315.0, 315.6);

        Assert.Contains("Summary: 7 done, 0 missed (of 7), 1 skipped", r.Out);
        Assert.Matches(@"Skipped \(1\):\s+time\s+item\s+mechanic\s+entry\s+P1 1:49\.0\s+Benediction", r.Out);
    }

    /// <summary>One action per DMU phase after P2: P3 Knock Down, P4 Grand Cross, P5 Ultima Upsurge.</summary>
    private const string DmuPlan = """
        format: mitcue-plan/1
        name: DMU
        fight: dmu
        entries:
          - { id: d1, mechanic: p3.knock_down, who: { job: WHM }, do: [ { action: 7433, name: Plenary Indulgence } ] }
          - { id: d2, mechanic: p4.grand_cross_1, who: { job: WHM }, do: [ { action: 16536, name: Temperance } ] }
          - { id: d3, mechanic: p5.ultima_upsurge, who: { job: WHM }, do: [ { action: 7433, name: Plenary Indulgence } ] }
        """;

    private CliResult Dmu(params string[] options) => this.kit.Run(["simulate", this.kit.Write("dmu.yaml", DmuPlan), "--job", "WHM", "--all-events", .. options]);

    private static void AssertAllInSync(CliResult r) =>
        Assert.Matches(@"Sync: the timeline was within 0\.8 s of (\d+) of \1 boss events\.\n", r.Out);

    [Fact]
    public void Push_Dmu_PhaseTimesComeFromThePullAsItPlays_NotTheTimelineSpacing()
    {
        // Unpushed, the P2 success jump (483.2 -> 597.0) brings P3, P4 and P5 113.8 s sooner than the timeline's spacing,
        // and the P4 success jump (1215.3 -> 1355.6) brings P5 another 140.3 s sooner: P4 from 16:13.9, P5 at 18:21.5.
        var unpushed = Log(this.Dmu());
        Assert.InRange(unpushed.Single(l => l.Event == "jump" && l.Timeline == 597.0).Real, 482.9, 483.4);
        Assert.InRange(unpushed.Single(l => l.Event == "jump" && l.Timeline == 1355.6).Real, 1101.3, 1101.8);

        // Review regression (a): an early P5 push while P4 runs was refused as "not after p4 starts (18:07.7)".
        var early = this.Dmu("--push", "p5=18:00");
        Assert.True(early.ExitCode == 0, early.ToString());
        Assert.Contains("Push: p5 starts at 18:00.0 (timeline 1355.6); p4 ends at timeline 1190.8", early.Out); // 3 s before, on P4's offset
        var jump = Log(early).Single(l => l.Event == "jump" && l.Timeline == 1355.6);
        Assert.InRange(jump.Real, 1079.8, 1080.2); // P4 leaves through its success cast, moved to the push
        Assert.Contains(Log(early), l => l.Event == "Done" && l.Rest.Contains("Ultima Upsurge", StringComparison.Ordinal));
        AssertAllInSync(early);

        // (b): P4 was cut at its own start ("p4 ends at timeline 1087.7") and P5 not followed until 18:50.6.
        var late = this.Dmu("--push", "p5=18:10");
        Assert.Contains("Push: p5 starts at 18:10.0 (timeline 1355.6); p4 ends at timeline 1200.8", late.Out);
        Assert.Contains(Log(late), l => l.Event == "Done" && l.Rest.Contains("Grand Cross", StringComparison.Ordinal));
        Assert.Contains("Summary: 3 done, 0 missed (of 3), 0 skipped", late.Out);
        AssertAllInSync(late);

        // (c): a P4 push cut P3 at timeline 897.0 (real 13:03), dropping its last minute; it ends 3 s before the push.
        var p4 = this.Dmu("--push", "p4=15:00", "--syncs");
        Assert.Contains("Push: p4 starts at 15:00.0 (timeline 1087.7); p3 ends at timeline 1010.8", p4.Out);
        Assert.Contains(Log(p4), l => l.Event == "sync" && l.Rest.Contains("\"Knock Down 2\"", StringComparison.Ordinal));
        Assert.Contains(Log(p4), l => l.Event == "sync" && l.Rest.Contains("\"Big Bang\"", StringComparison.Ordinal));
        Assert.InRange(Log(p4).Single(l => l.Event == "jump" && l.Timeline == 1087.7).Real, 899.8, 900.2);
        AssertAllInSync(p4);
    }

    [Theory]
    [InlineData("p3=8:40", "p3 is entered through the timeline jump at 483.2 (line 155), which starts it at 8:03.2 in this pull", "p3 can start from about 4:57.4 to 8:06.2.")]
    [InlineData("p5=21:00", "p5 is entered through the timeline jump at 1215.3 (line 396), which starts it at 18:21.5 in this pull", "p5 can start from about 16:51.5 to 18:26.5.")]
    [InlineData("p5=16:30", "p5 is entered through the timeline jump at 1215.3 (line 396)", "p5 can start from about 16:51.5 to 18:26.5.")]
    [InlineData("p2=4:00,p3=7:30", "p3 is entered through the timeline jump at 483.2 (line 155), which starts it at 7:05.8 in this pull", "p3 can start from about 4:00.0 to 7:08.8.")]
    public void Push_TheSimulatorCannotApply_IsAnError(string push, string why, string range)
    {
        // Review regression (d): pushes outside the entry jump's sync window were announced but did not happen.
        var r = this.Dmu("--push", push);
        Assert.True(r.ExitCode == 2, r.ToString());
        Assert.Contains("cannot be simulated: " + why, r.Err);
        Assert.Contains(range, r.Err);
    }

    [Fact]
    public void Push_WithFrom_LeavesTheSkippedPhasesOut()
    {
        // Cut phases leave through their jumps for a push, but the phases --from skips must still produce no events.
        var from = this.Dmu("--from", "p4");
        var pushed = this.Dmu("--from", "p4", "--push", "p5=2:00");
        Assert.True(pushed.ExitCode == 0, pushed.ToString());
        Assert.Contains("Push: p5 starts at 2:00.0 (timeline 1355.6); p4 ends at timeline 1202.7", pushed.Out);
        Assert.Equal(
            Log(from).Where(l => l.Real < 115).Select(l => (l.Real, l.Event, l.Timeline)),
            Log(pushed).Where(l => l.Real < 115).Select(l => (l.Real, l.Event, l.Timeline)));
        Assert.DoesNotContain(Log(pushed), l => l.Event == "jump" && l.Timeline == 597.0);
        AssertAllInSync(pushed);
    }

    [Fact]
    public void Press_SendsTheActionTheGameReportsAtTheSyncLevel()
    {
        var path = this.kit.Write("variants.yaml", """
            format: mitcue-plan/1
            name: Variants
            fight: top
            entries:
              - { id: w1, mechanic: p1.panto_stack1, who: { job: WAR }, do: [ { action: 36923, name: Damnation } ] }
              - { id: w2, mechanic: p1.loop_set1, who: { job: WAR }, do: [ { action: 3551, name: Raw Intuition } ] }
              - { id: w3, mechanic: p1.loop_set3, who: { job: WAR }, do: [ { any: [ { action: 3551, name: Raw Intuition }, { action: 25751, name: Bloodwhetting } ] } ] }
              - { id: w4, mechanic: p3.hello_world, who: { job: WAR }, do: [ { action: 99999, name: Nope } ] }
            """);

        // Review regression: the plan's ids were pressed as written, so uses the plugin cannot track counted as done.
        var r = this.kit.Run("simulate", path, "--job", "WAR");
        Assert.True(r.ExitCode == 0, r.ToString());
        var log = Log(r);
        Assert.Contains(log, l => l.Event == "press" && l.Rest.StartsWith("Raw Intuition ", StringComparison.Ordinal) && l.Rest.Contains("pressed Bloodwhetting (25751), the action the game reports at level 90", StringComparison.Ordinal));
        Assert.Contains(log, l => l.Event == "press" && l.Rest.StartsWith("Damnation ", StringComparison.Ordinal) && l.Rest.Contains("pressed Vengeance (44)", StringComparison.Ordinal));
        Assert.Contains(log, l => l.Event == "press" && l.Rest.StartsWith("Nope ", StringComparison.Ordinal) && l.Rest.Contains("not pressed: no WAR action for it at level 90", StringComparison.Ordinal));
        static IEnumerable<string> Outcome(List<Line> log, string item) =>
            log.Where(l => l.Rest.Split("  ")[0] == item && l.Event is "Done" or "Missed").Select(l => l.Event);
        Assert.Equal(["Missed"], Outcome(log, "Raw Intuition"));
        Assert.Equal(["Missed"], Outcome(log, "Damnation"));

        // A choice that names the reported action is done with it.
        Assert.Equal(["Done"], Outcome(log, "Raw Intuition / Bloodwhetting"));
        Assert.Contains(log, l => l.Event == "Done" && l.Rest.EndsWith(" as Bloodwhetting", StringComparison.Ordinal));
        Assert.Contains("Summary: 1 done, 3 missed (of 4), 0 skipped", r.Out);

        // --miss names the button as well as the plan's action.
        var miss = this.kit.Run("simulate", path, "--job", "WAR", "--miss", "Bloodwhetting");
        Assert.DoesNotContain(Log(miss), l => l.Rest.Contains("pressed Bloodwhetting", StringComparison.Ordinal));
        Assert.Contains("Summary: 0 done, 4 missed (of 4), 0 skipped", miss.Out);
    }

    [Fact]
    public void From_StartsAtAPhase_LikeThePreview()
    {
        var r = this.kit.Run("simulate", this.whm, "--job", "WHM", "--from", "p2");
        Assert.True(r.ExitCode == 0, r.ToString());
        Assert.Contains("from p2 (P2: Omega-M/F) at 0:02", r.OutLines[1]);
        var log = Log(r);
        Assert.DoesNotContain(log, l => l.Event == "countdown");

        // As in the preview, the earlier phases' reminders are skipped before the pull (never shown, not missed).
        var skipped = log.Where(l => l.Event == "Skipped").ToList();
        Assert.Equal(["Medica II", "Asylum", "Temperance", "Liturgy of the Bell", "Benediction"], skipped.Select(l => l.Rest.Split("  ")[0]));
        Assert.All(skipped, l => Assert.Equal(-2.0, l.Real));
        Assert.Contains("Summary: 3 done, 0 missed (of 3), 5 skipped", r.Out);
    }

    [Fact]
    public void Wipe_EndsThePull()
    {
        var r = this.kit.Run("simulate", this.whm, "--job", "WHM", "--from", "p2", "--wipe", "2:00");
        var log = Log(r);
        var end = Assert.Single(log, l => l.Event == "end");
        Assert.Equal(120.0, end.Real);
        Assert.Contains("pull ended: wipe", end.Rest);
        Assert.Equal(end, log[^1]);
        Assert.Contains("Pull ended at 2:00.0", r.Out);
    }

    [Fact]
    public void AllEventsAndSyncs_AreOptional()
    {
        var game = this.kit.Run("simulate", this.whm, "--job", "WHM", "--from", "p2");
        var all = this.kit.Run("simulate", this.whm, "--job", "WHM", "--from", "p2", "--all-events", "--syncs");
        Assert.Matches(@"; all \d+ generated events$", all.OutLines[1]);
        var log = Log(all);
        Assert.Contains(log, l => l.Event == "sync" && l.Rest.Contains("\"Solar Ray\"", StringComparison.Ordinal));

        // With every event the MapEffect at 200.0 enters P2; in game the 7B40 cast at 203.4 does.
        Assert.Equal(200.0, log.First(l => l.Event == "jump").Timeline);
        Assert.Equal(203.4, Log(game).First(l => l.Event == "jump").Timeline);
    }

    [Fact]
    public void SyncLine_ShowsWhereTheEngineDidNotFollow()
    {
        Assert.Contains("Sync: the timeline was within 0.8 s of ", this.kit.Run("simulate", this.whm, "--job", "WHM").Out);
        Assert.Matches(@"Sync: the timeline was within 0\.8 s of (\d+) of \1 boss events\.\n", this.kit.Run("simulate", this.whm, "--job", "WHM", "--push", "p2=1:30").Out);

        // cactbot enters DMU's P2 through an ActorControl line, which the plugin does not see in game. Without the fight
        // file's timeline_additions (a copy of the data without them) a P1 push is not followed.
        var dmu = this.kit.Write("dmu.yaml", """
            format: mitcue-plan/1
            name: DMU probe
            fight: dmu
            entries:
              - { id: d1, mechanic: p1.pull, offset: 10, who: { job: WHM }, do: [ { action: 7433, name: Plenary Indulgence } ] }
            """);
        var bare = this.CopyDataWithoutTimelineAdditions("dmu.yaml");
        var game = this.kit.Run("simulate", dmu, "--job", "WHM", "--push", "p2=200", "--data", bare);
        Assert.Matches(@"; off: p2 \d+ of \d+ \(the sync engine did not follow there", game.Out);
        Assert.DoesNotContain(Log(game), l => l.Event == "jump" && l.Timeline is 297.4 or 315.1);

        var all = this.kit.Run("simulate", dmu, "--job", "WHM", "--push", "p2=200", "--all-events", "--data", bare);
        Assert.Matches(@"Sync: the timeline was within 0\.8 s of (\d+) of \1 boss events\.\n", all.Out);
        Assert.Contains(Log(all), l => l.Event == "jump" && l.Timeline == 297.4);
    }

    [Theory]
    [InlineData("p2=200", 315.1, null)]
    [InlineData("p2=3:20,p4=14:00", 315.1, 1095.0)]
    [InlineData("p2=3:20,p4=13:00", 315.1, 1095.0)]
    [InlineData("p4=15:00", null, 1095.0)]
    public void Push_Dmu_TheFallbackSyncsFollowP2AndP4WithTheEventsSeenInGame(string push, double? p2Jump, double? p4Jump)
    {
        // Final review finding: DMU's P2 and P4 starts were synced only by ActorControl lines, so in game (and in this
        // command without --all-events) a P1 push left P2 about 100 s behind and a P3 push never reached P4. The fight
        // file's timeline_additions sync them on the Ultimate Embrace (C24C) and Kefka Says (C2DC) cast starts.
        var dmu = this.kit.Write("dmu.yaml", """
            format: mitcue-plan/1
            name: DMU probe
            fight: dmu
            entries:
              - { id: d1, mechanic: p2.ultimate_embrace_1, who: { job: WHM }, do: [ { action: 7433, name: Plenary Indulgence } ] }
            """);
        var r = this.kit.Run("simulate", dmu, "--job", "WHM", "--push", push);
        Assert.True(r.ExitCode == 0, r.ToString());
        AssertAllInSync(r);
        var jumps = Log(r).Where(l => l.Event == "jump").Select(l => l.Timeline).ToList();
        if (p2Jump is { } p2)
            Assert.Contains(p2, jumps);
        if (p4Jump is { } p4)
            Assert.Contains(p4, jumps);
        Assert.Contains("Summary: 1 done, 0 missed (of 1), 0 skipped", r.Out);
    }

    /// <summary>A copy of data/ whose fight file has no timeline_additions; returns the copy's path.</summary>
    private string CopyDataWithoutTimelineAdditions(string fightFile)
    {
        var source = Path.Combine(CliKit.RepoRoot, "data");
        var target = Path.Combine(this.kit.WorkDir, "bare-data");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest);
        }

        var path = Path.Combine(target, "fights", fightFile);
        var lines = File.ReadAllLines(path).ToList();
        var start = lines.FindIndex(l => l.StartsWith("timeline_additions:", StringComparison.Ordinal));
        Assert.True(start >= 0, $"{fightFile} has no timeline_additions");
        var end = start + 1;
        while (end < lines.Count && lines[end].StartsWith("  ", StringComparison.Ordinal))
            end++;
        lines.RemoveRange(start, end - start);
        File.WriteAllLines(path, lines);
        return target;
    }

    [Theory]
    [InlineData(new[] { "--push", "p1=20" }, "the first phase starts at the pull")]
    [InlineData(new[] { "--push", "p2" }, "needs phase=time pairs")]
    [InlineData(new[] { "--push", "p2=soon" }, "needs a time after the pull")]
    [InlineData(new[] { "--push", "p9=100" }, "has no phase 'p9'")]
    [InlineData(new[] { "--push", "p3=1:00,p2=2:00" }, "is not after p2 starts")]
    [InlineData(new[] { "--push", "p2=100,p2=120" }, "names p2 twice")]
    [InlineData(new[] { "--from", "p2", "--push", "p2=100" }, "the simulation starts at p2")]
    [InlineData(new[] { "--from", "p9" }, "has no phase 'p9'")]
    [InlineData(new[] { "--from", "p2", "--countdown", "5" }, "not with --from")]
    [InlineData(new[] { "--miss", "Nothing" }, "unknown action 'Nothing'")]
    [InlineData(new[] { "--seed", "x" }, "needs a whole number")]
    [InlineData(new[] { "--jitter", "-1" }, "cannot be negative")]
    [InlineData(new[] { "--fight", "nope" }, "unknown fight 'nope'")]
    public void BadOptions_AreUsageErrors(string[] options, string message)
    {
        var r = this.kit.Run(["simulate", this.whm, "--job", "WHM", .. options]);
        Assert.True(r.ExitCode == 2, r.ToString());
        Assert.Contains(message, r.Err);
    }
}
