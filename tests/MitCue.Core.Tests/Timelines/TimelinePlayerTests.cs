using MitCue.Core.Events;
using MitCue.Core.Timelines;
using static MitCue.Core.Tests.Timelines.TimelineKit;

namespace MitCue.Core.Tests.Timelines;

/// <summary>
/// Playback semantics (docs/research/cactbot-timelines.md §3). The scenarios follow the ones the research
/// ran through cactbot's own Timeline class (simA/simB/simC); where MitCue fixes a cactbot bug (Q1, Q2)
/// the test says so.
/// </summary>
public class TimelinePlayerTests
{
    private const double Tol = 1e-9;

    /// <summary>simA: windows, priorities, jumps and the reset sync.</summary>
    private static readonly Timeline SimA = Parse(
        "hideall \"--sync--\"",
        "0.0 \"--Reset--\" ActorControl { command: \"4000000F\" } window 0,100000 jump 0",
        "0.0 \"--sync--\" InCombat { inGameCombat: \"1\" } window 0,1",
        "10.0 \"A\" Ability { id: \"AAAA\", source: \"Boss\" }",
        "50.0 \"Wide\" Ability { id: \"CCCC\" } window 100,100",
        "52.0 \"Narrow\" Ability { id: \"CCCC\" }",
        "60.0 \"J\" Ability { id: \"DDDD\" } jump 200",
        "200.0 \"AfterJump\" Ability { id: \"EEEE\" }",
        "400.0 \"--sync--\" Ability { id: \"7B13\", source: \"Omega\" } window 400,0",
        "410.0 \"TieA\" Ability { id: \"9999\" } window 5,5",
        "410.0 \"TieB\" Ability { id: \"9999\" } window 5,5");

    /// <summary>Starts a player on <paramref name="timeline"/> with combat starting at <paramref name="clock"/>.</summary>
    private static TimelinePlayer Started(Timeline timeline, double clock = 1000, SyncMatchOptions? options = null)
    {
        var p = new TimelinePlayer(timeline, options);
        var r = p.Process(Combat(clock));
        Assert.NotNull(r);
        Assert.True(r.Started);
        return p;
    }

    [Fact]
    public void A_new_player_is_stopped_at_zero_and_only_syncs_containing_zero_are_active()
    {
        var p = new TimelinePlayer(SimA);
        Assert.False(p.IsRunning);
        Assert.Equal(0, p.TimeAt(123));
        Assert.Equal(["--sync--", "Wide", "--sync--", "--Reset--"], p.ActiveSyncs(123).Select(e => e.Name)); // by window end

        Assert.Null(p.Process(Ability(1000, 0xAAAA))); // 10.0's window [7.5, 12.5) is not active at 0
        Assert.False(p.IsRunning);
    }

    [Fact]
    public void The_combat_start_starts_the_timeline_at_zero()
    {
        var p = new TimelinePlayer(SimA);
        SyncResult? seen = null;
        p.Synced += r => seen = r;
        var result = p.Process(Combat(1000));

        Assert.NotNull(result);
        Assert.Same(result, seen);
        Assert.Equal((1000.0, 0.0, 0.0, true), (result.Clock, result.FromTime, result.ToTime, result.Started));
        Assert.Equal("--sync--", result.Entry.Name);
        Assert.True(p.IsRunning);
        Assert.Equal(12.5, p.TimeAt(1012.5), Tol);
    }

    [Fact]
    public void Window_start_is_inclusive_and_end_exclusive()
    {
        var p = Started(SimA);
        Assert.Null(p.Process(Ability(1007.499, 0xAAAA))); // timeline 7.499 < 7.5
        var r = p.Process(Ability(1007.5, 0xAAAA));     // exactly 7.5
        Assert.NotNull(r);
        Assert.Equal((7.5, 10.0), (r.FromTime, r.ToTime), new PairComparer());
        Assert.Equal(10.0, p.TimeAt(1007.5), Tol);

        // Now 10.0 at clock 1007.5, so timeline 12.5 is clock 1010.0: the window has ended.
        Assert.Null(p.Process(Ability(1010.0, 0xAAAA)));
        Assert.NotNull(p.Process(Ability(1009.99, 0xAAAA)));
    }

    [Fact]
    public void Source_names_match_case_insensitively_and_must_match()
    {
        var p = Started(SimA);
        Assert.Null(p.Process(Ability(1009.5, 0xAAAA, "Boss2")));
        Assert.NotNull(p.Process(Ability(1009.5, 0xAAAA, "BOSS")));
    }

    [Fact]
    public void Syncs_are_tried_by_window_end_so_the_narrow_window_wins()
    {
        var p = Started(SimA);
        var r = p.Process(Ability(1051, 0xCCCC)); // Narrow [49.5, 54.5) ends before Wide [-50, 150)
        Assert.Equal(("Narrow", 52.0), (r!.Entry.Name, r.ToTime));

        var q = Started(SimA);
        r = q.Process(Ability(1040, 0xCCCC)); // only Wide is active at 40
        Assert.Equal(("Wide", 50.0), (r!.Entry.Name, r.ToTime));
    }

    [Fact]
    public void Equal_window_ends_go_in_file_order_and_one_sync_fires_per_event()
    {
        var p = Started(SimA);
        Assert.NotNull(p.Process(Ability(1400 - 5, 0x7B13, "Omega")));
        var r = p.Process(Ability(1400 - 5 + 10, 0x9999));
        Assert.Equal("TieA", r!.Entry.Name);
        Assert.Equal(410, p.TimeAt(1405), Tol);
    }

    [Fact]
    public void A_jump_lands_exactly_on_its_target()
    {
        var p = Started(SimA);
        var r = p.Process(Ability(1060.4, 0xDDDD)); // 0.4 s late
        Assert.Equal((60.4, 200.0), (r!.FromTime, r.ToTime), new PairComparer());
        Assert.True(r.IsJump);
        Assert.Equal(200.0, p.TimeAt(1060.4), Tol); // not 200.4
        Assert.Equal(205.0, p.TimeAt(1065.4), Tol);
    }

    [Fact]
    public void A_sync_moves_time_forward_or_backward_by_any_amount()
    {
        var p = Started(SimA);
        p.Process(Ability(1060, 0xDDDD)); // now 200
        var r = p.Process(Ability(1061, 0x7B13, "Omega")); // phase push: [0, 400) contains 201
        Assert.Equal(400.0, r!.ToTime);
        Assert.False(r.Started);

        var back = p.Process(Ability(1062, 0xCCCC)); // 401: Wide ended at 150, Narrow at 54.5
        Assert.Null(back);
    }

    [Fact]
    public void Jump_zero_stops_the_timeline()
    {
        var p = Started(SimA);
        var stopped = 0;
        var synced = new List<SyncResult>();
        p.Stopped += () => stopped++;
        p.Synced += synced.Add;
        p.Process(Ability(1060, 0xDDDD)); // 200

        var r = p.Process(new DirectorCommand(1100, 0x4000000F));
        Assert.NotNull(r);
        Assert.Equal(("--Reset--", 240.0, 0.0, false), (r.Entry.Name, r.FromTime, r.ToTime, r.Started));
        Assert.False(p.IsRunning);
        Assert.Equal(0, p.TimeAt(1200));
        Assert.Equal(1, stopped);
        Assert.Equal(2, synced.Count);

        // Stopped again: only syncs containing 0 are active; the combat start restarts at 0.
        Assert.Null(p.Process(Ability(1201, 0xAAAA)));
        Assert.True(p.Process(Combat(1300))!.Started);
        Assert.Equal(5, p.TimeAt(1305), Tol);
    }

    [Fact]
    public void While_stopped_a_wide_phase_sync_starts_the_timeline_mid_fight()
    {
        var top = Load(Top);
        var p = new TimelinePlayer(top);

        Assert.Null(p.Process(Ability(5000, 0x7B4A, "Omega"))); // 409.3, default window: not active at 0
        var r = p.Process(Ability(5001, 0x7B13, "Omega"));      // 400.0 window 400,0 -> [0, 400)
        Assert.NotNull(r);
        Assert.Equal((0.0, 400.0, true), (r.FromTime, r.ToTime, r.Started));
        Assert.True(r.IsJump);
        Assert.Equal(409.3, p.TimeAt(5010.3), Tol);
        Assert.Equal(409.3, p.Process(Ability(5010.4, 0x7B4A, "Omega"))!.ToTime);

        // The P1 cast sync 11.0 window 20,20 also contains 0: a late join during P1 starts at 11.0.
        var q = new TimelinePlayer(top);
        Assert.Equal(11.0, q.Process(Cast(7000, 0x7B03, "Omega"))!.ToTime);

        // TEA: P1's first ability sync (11.4 window 10,2.5 = [1.4, 13.9)) cannot start the timeline.
        var tea = new TimelinePlayer(Load(Tea));
        Assert.Null(tea.Process(Ability(10, 0x49B0, "Living Liquid")));
        Assert.True(tea.Process(Combat(11))!.Started);
    }

    [Fact]
    public void Stops_on_duty_reset_and_on_combat_end_with_time_frozen()
    {
        var p = Started(SimA);
        var stops = 0;
        p.Stopped += () => stops++;
        p.Process(Ability(1060, 0xDDDD)); // 200 at 1060

        Assert.Null(p.Process(new DutyReset(1070, DutyResetKind.Wipe)));
        Assert.False(p.IsRunning);
        Assert.Equal(210, p.TimeAt(1070), Tol);
        Assert.Equal(210, p.TimeAt(5000), Tol); // frozen
        Assert.Equal(1, stops);
        Assert.Null(p.Process(new DutyReset(1080, DutyResetKind.Wipe))); // already stopped: nothing new
        Assert.Equal(1, stops);

        var q = Started(SimA);
        q.Process(Ability(1009.9, 0xAAAA));
        Assert.Null(q.Process(Combat(1020, false)));
        Assert.False(q.IsRunning);
        Assert.Equal(20.1, q.TimeAt(1020), Tol);
        Assert.Equal(20.1, q.TimeAt(1100), Tol);

        // After a stop the syncs that contain 0 can start it again (here the wide P3 sync).
        var r = q.Process(Ability(1200, 0x7B13, "Omega"));
        Assert.Equal((0.0, 400.0, true), (r!.FromTime, r.ToTime, r.Started));
    }

    [Fact]
    public void Reset_rewinds_to_zero()
    {
        var p = Started(SimA);
        var stops = 0;
        p.Stopped += () => stops++;
        p.Process(Ability(1060, 0xDDDD));
        p.Reset();
        Assert.False(p.IsRunning);
        Assert.Equal(0, p.TimeAt(2000));
        Assert.Equal(1, stops);
    }

    [Fact]
    public void Player_abilities_and_events_without_a_sync_type_are_ignored()
    {
        var p = Started(SimA);
        var synced = 0;
        p.Synced += _ => synced++;
        Assert.Null(p.Process(new AbilityResolved(1009, 0xAAAA, PlayerId, "Boss", true, [BossId], true)));
        Assert.Null(p.Process(new CastStarted(1009, 0xAAAA, BossId, "Boss", 1, PlayerId))); // a cast is not an Ability
        Assert.Equal(0, synced);
        Assert.Equal(9, p.TimeAt(1009), Tol);
    }

    [Fact]
    public void MatchNames_false_syncs_on_ids_alone()
    {
        var p = Started(SimA, options: new SyncMatchOptions { MatchNames = false });
        var r = p.Process(Ability(1009, 0xAAAA, "ボス"));
        Assert.Equal("A", r!.Entry.Name);

        var strict = Started(SimA);
        Assert.Null(strict.Process(Ability(1009, 0xAAAA, "ボス")));
    }

    [Fact]
    public void Resyncs_within_two_milliseconds_are_ignored()
    {
        var p = Started(SimA);
        p.Process(Ability(1009.0, 0xAAAA)); // 10.0 at 1009.0
        var again = p.Process(Ability(1009.001, 0xAAAA)); // would set 10.0 at 1009.001 (1 ms shift)
        Assert.NotNull(again);
        Assert.Equal(10.001, p.TimeAt(1009.001), 1e-6);
        p.Process(Ability(1009.004, 0xAAAA)); // 4 ms: resync
        Assert.Equal(10.0, p.TimeAt(1009.004), 1e-6);
    }

    [Fact]
    public void Unknown_label_jumps_empty_windows_and_old_style_syncs()
    {
        var t = Parse(
            "0.0 \"--sync--\" InCombat { inGameCombat: \"1\" } window 0,1",
            "10 \"Missing\" Ability { id: \"1\" } jump \"nope\"",
            "20 \"Empty\" Ability { id: \"2\" } window 0,0",
            "30 \"Old\" sync /foo/ window 5,5");
        var p = Started(t);
        Assert.Equal(10.0, p.Process(Ability(1010.2, 0x1))!.ToTime); // a plain sync
        Assert.Null(p.Process(Ability(1020, 0x2)));
        Assert.Null(p.Process(new GenericSyncEvent(1030, SyncSpec.LegacyType, new Dictionary<string, string>())));
    }

    /// <summary>simC: a forcejump loop with its overhang window.</summary>
    private static readonly Timeline SimC = Parse(
        "0.0 \"--sync--\" InCombat { inGameCombat: \"1\" } window 0,1",
        "290.0 \"--sync--\" Ability { id: \"0001\" } window 290,0",
        "300.0 label \"loop\"",
        "300.0 \"LoopStart\" Ability { id: \"EEEE\" }",
        "310.0 \"LoopEnd\" Ability { id: \"FFFF\" } window 5,5 forcejump \"loop\"");

    [Fact]
    public void A_forcejump_jumps_on_time_keeping_the_overshoot_and_leaves_an_overhang()
    {
        var p = Started(SimC);
        p.Process(Ability(1001, 0x0001)); // 290 at 1001
        var synced = new List<SyncResult>();
        p.Synced += synced.Add;

        p.Tick(1021.5); // timeline would be 310.5
        Assert.True(p.IsRunning);
        Assert.Equal(300.5, p.TimeAt(1021.5), Tol); // cactbot would land on 300.0 here (Q1)
        var jump = Assert.Single(synced);
        Assert.Equal((1021.0, 310.0, 300.0, false), (jump.Clock, jump.FromTime, jump.ToTime, jump.Started));
        Assert.Equal("LoopEnd", jump.Entry.Name);
        Assert.Equal("LoopEnd", p.ActiveSyncs(1021.5).Last().Name); // the overhang [300, 305), tried last

        // The real event arrives late, inside the overhang: resync to the jump target.
        var late = p.Process(Ability(1021.6, 0xFFFF));
        Assert.Equal((300.6, 300.0), (late!.FromTime, late.ToTime), new PairComparer());
        Assert.Equal(300.0, p.TimeAt(1021.6), Tol);
        Assert.Null(p.Process(Ability(1021.7, 0xFFFF))); // the overhang is gone after any sync
    }

    [Fact]
    public void A_forcejump_sync_that_matches_early_is_a_normal_jump_and_the_loop_stays_armed()
    {
        var p = Started(SimC);
        p.Process(Ability(1001, 0x0001));                 // 290 at 1001
        var r = p.Process(Ability(1019, 0xFFFF));         // 308: [305, 315)
        Assert.Equal((308.0, 300.0), (r!.FromTime, r.ToTime), new PairComparer());

        p.Tick(1029.5);                                   // passes 310 again
        Assert.Equal(300.5, p.TimeAt(1029.5), Tol);
        p.Tick(1039.5);                                   // and again
        Assert.Equal(300.5, p.TimeAt(1039.5), Tol);
    }

    [Fact]
    public void Process_performs_due_forcejumps_before_matching_the_event()
    {
        var p = Started(SimC);
        p.Process(Ability(1001, 0x0001)); // 290 at 1001; 310 is reached at 1021
        var r = p.Process(Ability(1022, 0xEEEE)); // no Tick in between: jump first (301 at 1022), then LoopStart [297.5, 302.5)
        Assert.Equal(("LoopStart", 300.0), (r!.Entry.Name, r.ToTime));
    }

    [Fact]
    public void A_forcejump_is_re_armed_after_time_moves_back_before_it()
    {
        // simB: cactbot leaves F1 unarmed here (Q2); MitCue re-arms it.
        var t = Parse(
            "0.0 \"--sync--\" InCombat { inGameCombat: \"1\" } window 0,1",
            "100.0 \"F1\" Ability { id: \"1111\" } forcejump 1000",
            "150.0 \"S150\" Ability { id: \"2222\" } window 100,100",
            "160.0 \"Back\" Ability { id: \"3333\" } window 110,100 jump 50",
            "1000.0 \"Dest\"");
        var p = Started(t, 1000);
        Assert.Equal(150.0, p.Process(Ability(1090, 0x2222))!.ToTime); // skips F1 (correct)
        Assert.Equal(50.0, p.Process(Ability(1091, 0x3333))!.ToTime);
        p.Tick(1147);                                                   // 106: F1 at 100 was passed
        Assert.Equal(1006.0, p.TimeAt(1147), Tol);
    }

    [Fact]
    public void A_forcejump_to_zero_stops()
    {
        var t = Parse(
            "0.0 \"--sync--\" InCombat { inGameCombat: \"1\" } window 0,1",
            "10 \"End\" Ability { id: \"1\" } forcejump 0");
        var p = Started(t);
        var stops = 0;
        p.Stopped += () => stops++;
        p.Tick(1011);
        Assert.False(p.IsRunning);
        Assert.Equal(0, p.TimeAt(1011));
        Assert.Equal(1, stops);
    }

    [Fact]
    public void The_timeline_stops_by_itself_after_its_end()
    {
        var t = Parse(
            "0.0 \"--sync--\" InCombat { inGameCombat: \"1\" } window 0,1",
            "100 \"Last\" duration 5");
        Assert.Equal(105, t.EndTime);
        var p = Started(t);
        var stops = 0;
        p.Stopped += () => stops++;
        p.Tick(1000 + 105 + TimelinePlayer.EndGrace - 0.01);
        Assert.True(p.IsRunning);
        p.Tick(1000 + 105 + TimelinePlayer.EndGrace + 3);
        Assert.False(p.IsRunning);
        Assert.Equal(105 + TimelinePlayer.EndGrace, p.TimeAt(9999), Tol);
        Assert.Equal(1, stops);
    }

    [Fact]
    public void Dmu_p4_sync_pulls_the_timeline_back_from_a_p3_enrage_branch()
    {
        var dmu = Load(Dmu);
        var p = Started(dmu, 0);
        Assert.Equal(600.0, p.Process(Ability(10, 0xC3F7, "Kefka"))!.ToTime); // P3 start, window 600,20

        var branch = p.Process(Cast(350, 0xC61F, "Chaos"));                   // 940: Exdeath died first
        Assert.Equal(("--sync--", 14000.0), (branch!.Entry.Name, branch.ToTime));
        Assert.Equal(14000, branch.Entry.JumpTime);

        var p4 = p.Process(new DirectorCommand(360, 0x80000027, 0x2E));       // window 1088,14000
        Assert.Equal(1087.7, p4!.ToTime);
    }

    [Fact]
    public void Dmu_forcejump_takes_the_timeline_to_phase_three_when_the_cast_is_missed()
    {
        var dmu = Load(Dmu);
        var p = Started(dmu, 0);
        Assert.Equal(476.7, p.Process(Ability(476.7, 0xC24C, "Kefka"))!.ToTime); // second Ultimate Embrace
        p.Tick(487.0); // no C3F7 cast seen: 486.2 forcejumps to "p2-success" (597)
        Assert.Equal(597.8, p.TimeAt(487.0), 1e-6);

        // A late Aero III Assault hit at 598.3 is inside the overhang [597, 599.5) too, but regular syncs are
        // tried first: it syncs to its own entry, 600.0 (window 600,20).
        var active = p.ActiveSyncs(487.5);
        Assert.Equal(("Aero III Assault?", 486.2), (active[^1].Name, active[^1].Time)); // the overhang, last
        Assert.Contains(active, e => e.Name == "Aero III Assault" && e.Time == 600.0);
        var hit = p.Process(Ability(487.5, 0xC3F7, "Kefka"));
        Assert.Equal(("Aero III Assault", 600.0), (hit!.Entry.Name, hit.ToTime));
    }

    private sealed class PairComparer : IEqualityComparer<(double, double)>
    {
        public bool Equals((double, double) x, (double, double) y) => Math.Abs(x.Item1 - y.Item1) < 1e-6 && Math.Abs(x.Item2 - y.Item2) < 1e-6;

        public int GetHashCode((double, double) obj) => 0;
    }
}
