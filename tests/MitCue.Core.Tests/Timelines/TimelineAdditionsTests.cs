using MitCue.Core.Data;
using MitCue.Core.Events;
using MitCue.Core.Fights;
using MitCue.Core.Simulation;
using MitCue.Core.Tests.Data;
using MitCue.Core.Timelines;
using static MitCue.Core.Tests.Timelines.TimelineKit;

namespace MitCue.Core.Tests.Timelines;

/// <summary>
/// Fight files' <c>timeline_additions</c> (MitCue-side fallback syncs appended to the cactbot timeline), and DMU pulls
/// played with only the events the plugin sees in game (<see cref="GameVisibleEvents"/>), as the live session and the
/// preview play them.
/// </summary>
/// <remarks>
/// Final review finding: cactbot syncs DMU's P2 and P4 starts only on ActorControl 80000027 (a director update the plugin
/// does not capture), so in game a P1 push left the timeline in P1's tail and P2's reminders about 100 s late, and a P3
/// push or a one-boss-dead branch never came back to P4. data/fights/dmu.yaml now adds the cast-start syncs of
/// docs/research/cactbot-timelines.md §6.
/// </remarks>
public class TimelineAdditionsTests
{
    private const double Tolerance = 0.6;

    private static (FightDefinition Fight, Timeline Timeline) Dmu()
    {
        var library = new FightLibrary(RepoFiles.Data);
        var fight = library.Get("dmu")!;
        return (fight, library.LoadTimeline(fight));
    }

    [Fact]
    public void Dmu_adds_cast_start_syncs_for_the_P2_and_P4_starts()
    {
        var (fight, timeline) = Dmu();
        Assert.NotNull(fight.TimelineAdditions);
        var cactbotWarnings = Load(TimelineKit.Dmu).Warnings;
        Assert.DoesNotContain(timeline.Warnings, w => !cactbotWarnings.Contains(w)); // the added lines parse cleanly

        var p2 = Assert.Single(timeline.Entries, e => e.Time == 315.1 && e.Sync?.Type == "StartsUsing");
        Assert.True(p2.CanSync);
        Assert.True(p2.Sync!.Matches(new CastStarted(0, 0xC24C, 1, "Kefka", 5f, 2)));
        Assert.True(p2.SyncStart <= 0, "active from the pull (and while stopped)");
        Assert.Equal(320.1, p2.SyncEnd, 1e-9); // before the second Ultimate Embrace (cast start about 471.7)

        var p4 = timeline.Entries.Where(e => e.Time == 1095.0 && e.Sync?.Type == "StartsUsing").ToList();
        Assert.Equal(2, p4.Count); // cactbot's own line (default window) and the wide one
        Assert.Contains(p4, e => e.SyncStart <= 0 && e.SyncEnd >= 15000); // reaches back from the enrage blocks at 12000-14000

        // The cactbot file itself is unchanged: the lines exist only in the loaded timeline.
        Assert.DoesNotContain(Load(TimelineKit.Dmu).Entries, e => e.Time == 315.1 && e.Sync?.Type == "StartsUsing");
    }

    public static TheoryData<int, double> Seeds() => new() { { 1, 0.15 }, { 2, 0.25 }, { 3, 0.15 }, { 4, 0.0 }, { 5, 0.25 } };

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Dmu_pulls_with_phase_pushes_stay_in_sync_with_the_events_seen_in_game(int seed, double jitter)
    {
        var (fight, timeline) = Dmu();
        var rng = new Random(seed * 15485863);
        var o = new SimulationOptions { Seed = seed, Jitter = jitter, CutPhasesLeaveByJump = true };
        var c1 = Between(rng, 100, 185);                            // Kefka < 15%
        var r2 = c1 + Between(rng, 3, 297.4 - c1 - 1);
        var c2 = Between(rng, 350, 470);                            // Kefka at 0%: C3F7 jumps to 597
        var r3 = r2 + (c2 - 297.4) + Between(rng, 3, 6);
        var c3 = Between(rng, 700, 925);                            // Chaos and Exdeath die
        var r4 = r3 + (c3 - 600) + Between(rng, 5, 60);
        var c4 = Between(rng, 1130, 1210);                          // Kefka < 20%: C3FD (window 90,5)
        var r5 = r4 + (c4 - 1087.7) + Between(rng, 1, 1220.3 - c4 - 1);
        o.PhaseCutoff["p1"] = c1;
        o.PhaseRealStart["p2"] = r2;
        o.PhaseCutoff["p2"] = c2;
        o.PhaseRealStart["p3"] = r3;
        o.PhaseCutoff["p3"] = c3;
        o.PhaseRealStart["p4"] = r4;
        o.PhaseCutoff["p4"] = c4;
        o.PhaseRealStart["p5"] = r5;

        var steps = PlayInGame(timeline, fight, o);
        AssertAfterInSync(steps);
        Assert.Contains(steps, s => s.Result?.Entry.Time == 315.1);   // P2 by Ultimate Embrace's cast
        Assert.Contains(steps, s => s.Result is { ToTime: 597.0, IsJump: true });
        Assert.Contains(steps, s => s.Result?.Entry.Time == 1095.0);  // P4 by Kefka Says' cast
        Assert.Contains(steps, s => s.Result is { ToTime: 1355.6, IsJump: true });
    }

    [Fact]
    public void Dmu_p3_single_boss_enrage_branch_returns_to_p4_with_the_events_seen_in_game()
    {
        var (fight, timeline) = Dmu();
        var o = new SimulationOptions { Seed = 5, Jumps = { ["p3-exdeath-enrage"] = true }, PhaseRealStart = { ["p4"] = 860 } };
        var steps = PlayInGame(timeline, fight, o);
        var branch = steps.FindIndex(s => s.Result is { ToTime: 13000.0 });
        Assert.True(branch > 0);
        Assert.Contains(steps.Skip(branch), s => s.Result?.Entry.Time == 1095.0);
        AssertAfterInSync(steps.Skip(branch + 1).Where(s => s.Sim.SyncTime < 10000).ToList());
        Assert.True(steps[^1].After >= 1355.6);
    }

    [Fact]
    public void Without_the_additions_a_P1_push_is_not_followed_in_game()
    {
        // What the finding saw: cactbot's file alone, played with the events seen in game.
        var (fight, _) = Dmu();
        var bare = Load(TimelineKit.Dmu);
        var o = new SimulationOptions { Seed = 1, PhaseCutoff = { ["p1"] = 150 }, PhaseRealStart = { ["p2"] = 160 } };
        var steps = PlayInGame(bare, fight, o);
        var p2 = steps.Where(s => s.Sim.SyncTime is { } t && t >= 297.4 && t < 483).ToList();
        Assert.NotEmpty(p2);
        Assert.All(p2, s => Assert.True(Math.Abs(s.After - s.Sim.SyncTime!.Value) > 50, $"{s.Sim.Entry} synced"));
    }

    /// <summary>
    /// <c>/mitcue preview</c> plays only the events seen in game (it played every generated event, so it followed DMU's
    /// P2 and P4 where a live pull could not). From every phase it reaches the phase's first sync and stays in sync.
    /// </summary>
    [Theory]
    [InlineData("top", "p2")]
    [InlineData("top", "p5")]
    [InlineData("tea", "p2")]
    [InlineData("tea", "p4")]
    [InlineData("dmu", "p2")]
    [InlineData("dmu", "p3")]
    [InlineData("dmu", "p4")]
    [InlineData("dmu", "p5")]
    public void Previews_from_a_phase_with_the_events_seen_in_game_follow_the_fight(string key, string phaseId)
    {
        var library = new FightLibrary(RepoFiles.Data);
        var fight = library.Get(key)!;
        var timeline = library.LoadTimeline(fight);
        var options = FightSimulatorTests.PreviewFrom(fight, phaseId, new SimulationOptions { PullClock = 1000, Seed = 9, Jitter = 0.15 });

        var steps = PlayInGame(timeline, fight, options);

        var start = fight.GetPhase(phaseId)!.Start;
        var first = steps.FindIndex(s => s.Result != null && s.Sim.SyncTime >= start);
        Assert.True(first >= 0, $"{key} from {phaseId}: never synced into the phase");
        Assert.InRange(steps[first].Clock - 1000, 0, 60); // within a minute of the preview's start
        AssertAfterInSync(steps.Skip(first).ToList());
    }

    [Fact]
    public void A_line_that_repeats_a_cactbot_line_with_a_wider_window_is_one_game_event()
    {
        var timeline = Parse(
            "0.0 \"--sync--\" InCombat { inGameCombat: \"1\" } window 0,1",
            "10.0 \"Cast\" StartsUsing { id: \"1234\", source: \"Boss\" }",
            "15.0 \"Hit\" Ability { id: \"1234\", source: \"Boss\" }",
            "10.0 \"--sync--\" StartsUsing { id: \"1234\", source: \"Boss\" } window 10,1000");
        var events = FightSimulator.Generate(timeline, null, new SimulationOptions { Jitter = 0 });
        Assert.Single(events, e => e is CastStarted);
        Assert.Single(events, e => e is AbilityResolved);
    }

    /// <summary>
    /// TEA's Inception crystals explode when players bring the True Heart to them, so the added 4A88 Tetrashatter sync
    /// (564.5, the first explosion, p3.inception_crystals' anchor) is a narrow catch: an explosion from 562.5 until 564.5
    /// moves the timeline forward to 564.5, once; no explosion before it, after it or of the other crystals moves it.
    /// </summary>
    [Fact]
    public void Tea_tetrashatter_moves_the_timeline_forward_by_at_most_two_seconds_once()
    {
        var library = new FightLibrary(RepoFiles.Data);
        var tea = library.Get("tea")!;
        var timeline = library.LoadTimeline(tea);
        var entry = Assert.Single(timeline.Entries, e => e.Name == "Tetrashatter");
        Assert.Equal((564.5, 562.5, 564.5), (entry.Time, entry.SyncStart, entry.SyncEnd));
        Assert.Equal(entry.Time, tea.GetMechanic("p3.inception_crystals")!.Hit);
        Assert.DoesNotContain(timeline.Warnings, w => !Load(TimelineKit.Tea).Warnings.Contains(w));

        static AbilityResolved Boom(double clock) => Ability(clock, 0x4A88, "Judgment Crystal");

        // Combat at clock 1000: timeline time = clock − 1000 until a sync moves it.
        var early = new TimelinePlayer(timeline);
        Assert.True(early.Process(Combat(1000))!.Started);
        Assert.Null(early.Process(Boom(1562.0)));                          // before the window
        var synced = early.Process(Boom(1563.0));
        Assert.Equal((563.0, 564.5), (synced!.FromTime, synced.ToTime));   // forward, 1.5 s
        Assert.Null(early.Process(Boom(1563.4)));                          // the other crystals: never again
        Assert.Null(early.Process(Boom(1566.0)));
        Assert.Equal(568.0, early.TimeAt(1566.5), 1e-9);

        var late = new TimelinePlayer(timeline);
        Assert.True(late.Process(Combat(1000))!.Started);
        Assert.Null(late.Process(Boom(1564.5)));                           // from 564.5 on: no sync, nothing moves back
        Assert.Null(late.Process(Boom(1567.0)));
        Assert.Equal(567.0, late.TimeAt(1567.0), 1e-9);
    }

    /// <summary>
    /// TOP's P2 Cosmo Memory raidwide (7B24, cast by the Optical Unit only there; cactbot's -ii line drops it) is added at
    /// 320.2, p2.limitless' second hit: a narrow catch, active from 319.2 until 321.2, where a 7B24 syncs the timeline to
    /// 320.2, a move of at most 1 s; a 7B24 outside the window moves nothing.
    /// </summary>
    [Fact]
    public void Top_cosmo_memory_raidwide_moves_the_timeline_by_at_most_one_second()
    {
        var library = new FightLibrary(RepoFiles.Data);
        var top = library.Get("top")!;
        var timeline = library.LoadTimeline(top);
        var entry = Assert.Single(timeline.Entries, e => e.Sync?.Matches(Ability(0, 0x7B24, "Optical Unit")) == true);
        Assert.Equal(("Cosmo Memory raidwide", 320.2, 319.2, 321.2), (entry.Name, entry.Time, entry.SyncStart, entry.SyncEnd));
        Assert.Equal(entry.Time, top.GetMechanic("p2.limitless")!.Hits[1]);
        Assert.Equal(318.8, top.GetMechanic("p2.cosmo_memory")!.Hit);
        Assert.DoesNotContain(timeline.Warnings, w => !Load(TimelineKit.Top).Warnings.Contains(w));
        Assert.DoesNotContain(Load(TimelineKit.Top).Entries, e => e.Sync?.Matches(Ability(0, 0x7B24, "Optical Unit")) == true);

        // Combat at clock 1000: timeline time = clock − 1000 until a sync moves it.
        var early = new TimelinePlayer(timeline);
        Assert.True(early.Process(Combat(1000))!.Started);
        Assert.Null(early.Process(Ability(1319.0, 0x7B24, "Optical Unit")));                     // before the window
        var synced = early.Process(Ability(1319.4, 0x7B24, "Optical Unit"));
        Assert.Equal(319.4, synced!.FromTime, 1e-9);                                           // forward, 0.8 s
        Assert.Equal(320.2, synced.ToTime, 1e-9);
        Assert.Equal(321.0, early.TimeAt(1320.2), 1e-9);
        var again = early.Process(Ability(1320.2, 0x7B24, "Optical Unit"));                     // a later line of it, in the window
        Assert.Equal(320.2, again!.ToTime, 1e-9);
        Assert.True(again.FromTime - again.ToTime <= 1 + 1e-9);

        var late = new TimelinePlayer(timeline);
        Assert.True(late.Process(Combat(1000))!.Started);
        var back = late.Process(Ability(1321.0, 0x7B24, "Optical Unit"));
        Assert.Equal(321.0, back!.FromTime, 1e-9);                                             // back, 0.8 s
        Assert.Equal(320.2, back.ToTime, 1e-9);

        var after = new TimelinePlayer(timeline);
        Assert.True(after.Process(Combat(1000))!.Started);
        Assert.Null(after.Process(Ability(1321.3, 0x7B24, "Optical Unit")));                     // after the window
    }

    [Fact]
    public void FightLoader_reads_timeline_additions_and_rejects_lines_that_are_not_syncs()
    {
        const string Head = "fight: x\nname: X\nterritory: 1\nsync_level: 90\ntimeline: x.txt\nphases:\n  - { id: p1, start: 0 }\nmechanics:\n  - { id: p1.a, hit: 10 }\n";
        var fight = FightLoader.Load(Head + "timeline_additions: |\n  # a comment\n  12.0 \"--sync--\" StartsUsing { id: \"AB\" } window 12,5\n", "fights/x.yaml");
        Assert.Contains("StartsUsing { id: \"AB\" }", fight.TimelineAdditions, StringComparison.Ordinal);
        Assert.Null(FightLoader.Load(Head, "fights/x.yaml").TimelineAdditions);

        var noSync = Assert.Throws<FormatException>(() => FightLoader.Load(Head + "timeline_additions: |\n  12.0 \"Display only\"\n", "fights/x.yaml"));
        Assert.Contains("fights/x.yaml: timeline_additions: line 1 (\"Display only\") has no sync", noSync.Message, StringComparison.Ordinal);

        var label = Assert.Throws<FormatException>(() => FightLoader.Load(Head + "timeline_additions: |\n  12.0 \"--sync--\" Ability { id: \"AB\" } jump \"p2\"\n", "fights/x.yaml"));
        Assert.Contains("jumps to a label", label.Message, StringComparison.Ordinal);

        Assert.Throws<FormatException>(() => FightLoader.Load(Head + "timeline_additions: |\n  12.0 \"--sync--\" Ability { id: \"AB\" window 5\n", "fights/x.yaml"));
    }

    [Fact]
    public void FightLoader_rejects_labels_and_hideall_in_timeline_additions()
    {
        // Review finding (41d2893): a label line passed the loader and, appended to the cactbot file, redefined the
        // file's label of that name (the parser only warns and keeps the last one), so every jump to it went elsewhere.
        const string Head = "fight: x\nname: X\nterritory: 1\nsync_level: 90\ntimeline: x.txt\nphases:\n  - { id: p1, start: 0 }\nmechanics:\n  - { id: p1.a, hit: 10 }\n";
        const string Sync = "  8.0 \"--sync--\" StartsUsing { id: \"AB\" } window 8,100\n";

        var label = Assert.Throws<FormatException>(() => FightLoader.Load(Head + "timeline_additions: |\n  50.0 label \"p2\"\n" + Sync, "fights/x.yaml"));
        Assert.Contains("fights/x.yaml: timeline_additions: label \"p2\"", label.Message, StringComparison.Ordinal);

        var hide = Assert.Throws<FormatException>(() => FightLoader.Load(Head + "timeline_additions: |\n  hideall \"A\"\n" + Sync, "fights/x.yaml"));
        Assert.Contains("hideall \"A\"", hide.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fight_whose_additions_define_a_label_is_not_loaded_and_the_cactbot_jumps_stay()
    {
        var data = new MemoryData(new Dictionary<string, string>
        {
            ["fights/x.yaml"] = "fight: x\nname: X\nterritory: 1\nsync_level: 90\ntimeline: x.txt\nphases:\n  - { id: p1, start: 0 }\nmechanics:\n  - { id: p1.a, hit: 10, entry: \"A\" }\n" +
                                "timeline_additions: |\n  50.0 label \"p2\"\n  8.0 \"--sync--\" StartsUsing { id: \"AB\" } window 8,100\n",
            ["timelines/x.txt"] = "0.0 \"--sync--\" InCombat { inGameCombat: \"1\" } window 0,1\n10.0 \"A\" Ability { id: \"AB\" } jump \"p2\"\n200.0 label \"p2\"\n205.0 \"B\" Ability { id: \"CD\" }\n",
        });

        var library = new FightLibrary(data);

        Assert.Null(library.Get("x"));
        Assert.Contains(library.Problems, p => p.Message.Contains("label \"p2\"", StringComparison.Ordinal));
    }

    [Fact]
    public void FightLibrary_appends_the_additions_to_the_timeline_it_loads()
    {
        var data = new MemoryData(new Dictionary<string, string>
        {
            ["fights/x.yaml"] = "fight: x\nname: X\nterritory: 1\nsync_level: 90\ntimeline: x.txt\nphases:\n  - { id: p1, start: 0 }\nmechanics:\n  - { id: p1.a, hit: 10, entry: \"A\" }\n" +
                                "timeline_additions: |\n  8.0 \"--sync--\" StartsUsing { id: \"AB\" } window 8,100\n",
            ["timelines/x.txt"] = "0.0 \"--sync--\" InCombat { inGameCombat: \"1\" } window 0,1\n10.0 \"A\" Ability { id: \"AB\" }\n",
        });
        var library = new FightLibrary(data);
        var timeline = library.LoadTimeline(library.Get("x")!);
        Assert.Equal(3, timeline.Entries.Count);
        var added = timeline.Entries.Single(e => e.Time == 8.0);
        Assert.Equal(108.0, added.SyncEnd, 1e-9);
        Assert.Empty(timeline.Warnings);
    }

    private static List<Step> PlayInGame(Timeline timeline, FightDefinition fight, SimulationOptions options)
    {
        var sims = FightSimulator.Simulate(timeline, fight, options).Where(s => GameVisibleEvents.Contains(s.Event)).ToList();
        return Play(timeline, sims).Steps;
    }

    private static void AssertAfterInSync(IReadOnlyList<Step> steps)
    {
        var off = steps.Where(s => s.Sim.SyncTime is { } t && Math.Abs(s.After - t) > Tolerance).ToList();
        Assert.True(off.Count == 0, $"{off.Count} of {steps.Count} events out of tolerance, e.g. {off.FirstOrDefault()?.Sim.Entry}: engine {off.FirstOrDefault()?.After:0.0}");
    }

    private static double Between(Random rng, double min, double max) => min + ((max - min) * rng.NextDouble());

    /// <summary>Data files held in memory.</summary>
    private sealed class MemoryData(Dictionary<string, string> files) : IDataSource
    {
        public IEnumerable<string> List(string folder) => files.Keys.Where(k => k.StartsWith(folder.TrimEnd('/') + "/", StringComparison.Ordinal)).Order(StringComparer.Ordinal);

        public bool Exists(string path) => files.ContainsKey(path);

        public string ReadText(string path) => files.TryGetValue(path, out var text) ? text : throw new FileNotFoundException(path);
    }
}
