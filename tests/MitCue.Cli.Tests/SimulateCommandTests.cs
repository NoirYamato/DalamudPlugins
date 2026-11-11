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

    [Fact]
    public void From_StartsAtAPhase_LikeThePreview()
    {
        var r = this.kit.Run("simulate", this.whm, "--job", "WHM", "--from", "p2");
        Assert.True(r.ExitCode == 0, r.ToString());
        Assert.Contains("from p2 (P2: Omega-M/F) at 0:02", r.OutLines[1]);
        var log = Log(r);
        Assert.DoesNotContain(log, l => l.Event == "countdown");
        Assert.Equal(4, log.Count(l => l.Event == "Skipped")); // Asylum, Temperance, Bell, Benediction
        Assert.Contains("Summary: 3 done, 1 missed (of 4), 4 skipped", r.Out); // the pre-pull Medica II cannot be pressed
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
