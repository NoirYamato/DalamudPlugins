using MitCue.Core.Fights;
using MitCue.Core.Simulation;
using MitCue.Core.Timelines;
using static MitCue.Core.Tests.Timelines.TimelineKit;

namespace MitCue.Core.Tests.Timelines;

/// <summary>
/// Simulated pulls played through the sync engine: with phase pushes and jitter the engine stays within
/// <see cref="Tolerance"/> of every synced entry's time, and every fight reaches its final phase.
/// </summary>
public class EndToEndSyncTests
{
    private const double Tolerance = 0.6;

    public static TheoryData<int, double> Seeds() => new()
    {
        { 1, 0.15 }, { 2, 0.15 }, { 3, 0.15 }, { 4, 0.15 }, { 5, 0.15 }, { 6, 0.15 },
        { 7, 0.25 }, { 8, 0.25 }, { 9, 0.25 }, { 10, 0.25 }, { 11, 0.0 }, { 12, 0.25 },
    };

    [Fact]
    public void Top_pull_without_pushes_stays_in_sync()
    {
        var steps = AssertInSync(Load(Top), TopFight(), new SimulationOptions { Seed = 1 });
        Assert.Equal(1421.9, steps[^1].Sim.SyncTime);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Top_pulls_with_phase_pushes_and_jitter_stay_within_tolerance(int seed, double jitter)
    {
        var timeline = Load(Top);
        var fight = TopFight();
        var options = Pushes(timeline, fight, seed, jitter);
        var steps = AssertInSync(timeline, fight, options);

        // Every phase start synced (the wide window N,0 syncs) and the pull reached P6.
        foreach (var start in new[] { 200.0, 400.0, 600.0, 700.0, 1100.0 })
            Assert.Contains(steps, s => s.Result?.Entry.Time == start);
        Assert.True(steps[^1].After >= 1100);
    }

    [Fact]
    public void Top_stays_in_sync_without_source_names()
    {
        var timeline = Load(Top);
        var fight = TopFight();
        AssertInSync(timeline, fight, Pushes(timeline, fight, 21, 0.15), new SyncMatchOptions { MatchNames = false });
    }

    [Fact]
    public void Tea_pull_reaches_the_final_phase()
    {
        // Unpushed, the P4 sync (900.0 window 900,0) arrives right at its exclusive end and may not fire;
        // the engine is in sync there anyway, which AssertInSync checks.
        var steps = AssertInSync(Load(Tea), TeaFight(), new SimulationOptions { Seed = 2 });
        Assert.Contains(steps, s => s.Sim.SyncTime == 900.0);
        Assert.Equal(1247.6, steps[^1].Sim.SyncTime);
        Assert.True(steps[^1].After >= 1247);
    }

    [Theory]
    [InlineData(1, 0.15)]
    [InlineData(2, 0.25)]
    [InlineData(3, 0.15)]
    [InlineData(4, 0.0)]
    public void Tea_pulls_with_phase_pushes_reach_the_final_phase(int seed, double jitter)
    {
        var rng = new Random(seed * 104729);
        var o = new SimulationOptions { Seed = seed, Jitter = jitter };
        var c1 = Between(rng, 60, 135);                       // Living Liquid dies
        var r2 = c1 + Between(rng, 3, 200 - c1 - 1);         // Hawk Blaster: window 200,0
        var c2 = Between(rng, 260, 415);                      // Cruise Chaser and Brute Justice die
        var r3 = r2 + (c2 - 200) + Between(rng, 3, 492 - c2 - 1); // Temporal Stasis cast: window 500,0 = [-8, 492)
        var r4 = r3 + (787.4 - 492) + Between(rng, 10, 900 - 787.4 - 1); // Perfect Alexander: window 900,0
        o.PhaseCutoff["p1"] = c1;
        o.PhaseRealStart["p2"] = r2;
        o.PhaseCutoff["p2"] = c2;
        o.PhaseRealStart["p3"] = r3;
        o.PhaseRealStart["p4"] = r4;

        var steps = AssertInSync(Load(Tea), TeaFight(), o);
        foreach (var start in new[] { 200.0, 492.0, 900.0 })
            Assert.Contains(steps, s => s.Result?.Entry.Time == start);
        Assert.True(steps[^1].After >= 1200);
    }

    [Theory]
    [InlineData(600)]
    [InlineData(597)]
    public void Dmu_pull_reaches_the_final_phase(double p3Start)
    {
        var steps = AssertInSync(Load(Dmu), DmuFight(p3Start), new SimulationOptions { Seed = 3 });
        Assert.Contains(steps, s => s.Result is { ToTime: 597.0, IsJump: true }); // P2 success
        Assert.Contains(steps, s => s.Result is { ToTime: 1355.6, IsJump: true }); // P4 success
        Assert.Equal(1613.4, steps[^1].Sim.SyncTime); // Forsaken Null (Enrage)
        Assert.True(steps[^1].After >= 1355.6);
    }

    [Theory]
    [InlineData(1, 0.15)]
    [InlineData(2, 0.25)]
    [InlineData(3, 0.15)]
    [InlineData(4, 0.0)]
    [InlineData(5, 0.25)]
    public void Dmu_pulls_with_phase_pushes_reach_the_final_phase(int seed, double jitter)
    {
        var rng = new Random(seed * 15485863);
        var o = new SimulationOptions { Seed = seed, Jitter = jitter, CutPhasesLeaveByJump = true }; // the HP-gated casts leave P2 and P4
        var c1 = Between(rng, 100, 185);                            // Kefka < 15%
        var r2 = c1 + Between(rng, 3, 297.4 - c1 - 1);              // dialogue sync: window 300,5 = [-2.6, 302.4)
        var c2 = Between(rng, 350, 470);                            // Kefka at 0%: C3F7 jumps to 597
        var r3 = r2 + (c2 - 297.4) + Between(rng, 3, 6);            // the cast ends 3 s after it starts
        var c3 = Between(rng, 700, 925);                            // Chaos and Exdeath die
        var r4 = r3 + (c3 - 600) + Between(rng, 5, 60);             // dialogue sync: window 1088,14000
        var c4 = Between(rng, 1130, 1210);                          // Kefka < 20%: C3FD (window 90,5 = [1125.3, 1220.3))
        var r5 = r4 + (c4 - 1087.7) + Between(rng, 1, 1220.3 - c4 - 1);
        o.PhaseCutoff["p1"] = c1;
        o.PhaseRealStart["p2"] = r2;
        o.PhaseCutoff["p2"] = c2;
        o.PhaseRealStart["p3"] = r3;
        o.PhaseCutoff["p3"] = c3;
        o.PhaseRealStart["p4"] = r4;
        o.PhaseCutoff["p4"] = c4;
        o.PhaseRealStart["p5"] = r5;

        var steps = AssertInSync(Load(Dmu), DmuFight(), o);
        Assert.Contains(steps, s => s.Result is { ToTime: 597.0, IsJump: true });
        Assert.Contains(steps, s => s.Result is { ToTime: 1087.7, IsJump: true });
        Assert.Contains(steps, s => s.Result is { ToTime: 1355.6, IsJump: true });
        Assert.True(steps[^1].After >= 1355.6);
    }

    [Theory]
    [InlineData(Top)]
    [InlineData(Tea)]
    [InlineData(Dmu)]
    [InlineData(Dsr)]
    [InlineData(Fru)]
    public void Jitter_free_simulation_lands_exactly_on_every_entry_that_syncs(string file)
    {
        var timeline = Load(file);
        var fight = file switch { Top => TopFight(), Tea => TeaFight(), Dmu => DmuFight(), _ => null };
        var (_, steps) = Play(timeline, FightSimulator.Simulate(timeline, fight, new SimulationOptions { Seed = 3, Jitter = 0 }));
        foreach (var s in steps.Where(s => s.Sim.SyncTime != null))
        {
            Assert.Equal(s.Sim.SyncTime!.Value, s.After, 0.003);
            if (s.Result != null)
                Assert.Same(s.Sim.Entry, s.Result.Entry);
            else
                Assert.Equal(0.0, s.Sim.Entry!.Window.After); // only "window N,0" syncs arriving at their exclusive end
        }
    }

    /// <summary>
    /// Review regression: the plugin's former preview options from DMU P2 (every later phase listed at its timeline
    /// spacing from P2, so P3 at 2 + 302.6 s) moved the 483.2 success cast past the 486.2 forcejump, and all of
    /// P3 ran about 111 s ahead of the engine.
    /// </summary>
    [Fact]
    public void Dmu_preview_from_p2_keeps_phase_three_in_sync()
    {
        var timeline = Load(Dmu);
        var fight = DmuFight();
        var phases = fight.Phases.OrderBy(p => p.Start).ToList();
        var from = phases[1];
        var o = new SimulationOptions { PullClock = 1000, Seed = 5, Jitter = 0.15 };
        o.PhaseCutoff[phases[0].Id] = phases[0].Start + 0.5;
        for (var i = 1; i < phases.Count; i++)
            o.PhaseRealStart[phases[i].Id] = 2 + (phases[i].Start - from.Start);

        var (_, steps) = Play(timeline, FightSimulator.Simulate(timeline, fight, o));
        var p3 = steps.Where(s => s.Sim.SyncTime is { } t && t >= 597 && t < 1087.7).ToList();
        var off = p3.Where(s => Math.Abs(s.After - s.Sim.SyncTime!.Value) > 0.6).ToList();
        Assert.True(
            off.Count == 0,
            $"{off.Count}/{p3.Count} P3 events out of sync, e.g. {off.FirstOrDefault()?.Sim.Entry}: engine {off.FirstOrDefault()?.After:0.0}");
    }

    /// <summary>
    /// The plugin's preview from a phase (<c>PreviewDriver.ConfigurePhaseStart</c>: earlier phases cut, only the
    /// chosen phase listed) is followed to the end of the fight, through DMU's jumps.
    /// </summary>
    [Theory]
    [InlineData(Top, "p2", 1421.9)]
    [InlineData(Top, "p5", 1421.9)]
    [InlineData(Tea, "p3", 1247.6)]
    [InlineData(Dmu, "p2", 1613.4)]
    [InlineData(Dmu, "p3", 1613.4)]
    [InlineData(Dmu, "p4", 1613.4)]
    public void Previews_from_a_phase_stay_in_sync_to_the_end(string file, string phaseId, double lastSync)
    {
        var timeline = Load(file);
        var fight = file switch { Top => TopFight(), Tea => TeaFight(), _ => DmuFight() };
        var options = FightSimulatorTests.PreviewFrom(fight, phaseId, new SimulationOptions { Seed = 9, Jitter = 0.15 });
        var start = fight.GetPhase(phaseId)!.Start;

        var (_, steps) = Play(timeline, FightSimulator.Simulate(timeline, fight, options));
        var chosen = steps.Where(s => s.Sim.SyncTime >= start).ToList();
        Assert.All(chosen, s => Assert.InRange(s.After - s.Sim.SyncTime!.Value, -Tolerance, Tolerance));
        Assert.Equal(lastSync, chosen[^1].Sim.SyncTime!.Value, 1e-9);
    }

    [Fact]
    public void Dmu_preview_from_p5_syncs_from_its_first_wide_sync()
    {
        // P5 has no wide sync at its start (1355.6 is a label reached by P4's success jump): the preview's first
        // P5 events cannot sync until 1396.2 BB40 (window 1400,0). From there on it stays in sync.
        var timeline = Load(Dmu);
        var fight = DmuFight();
        var (_, steps) = Play(timeline, FightSimulator.Simulate(timeline, fight, FightSimulatorTests.PreviewFrom(fight, "p5", new SimulationOptions { Seed = 9 })));
        Assert.Contains(steps, s => s.Result?.Entry.Time == 1396.2);
        Assert.All(steps.Where(s => s.Sim.SyncTime >= 1396.2), s => Assert.InRange(s.After - s.Sim.SyncTime!.Value, -Tolerance, Tolerance));
    }

    [Fact]
    public void Dmu_p3_single_boss_enrage_branch_returns_to_p4()
    {
        var o = new SimulationOptions { Seed = 5, Jumps = { ["p3-exdeath-enrage"] = true }, PhaseRealStart = { ["p4"] = 860 } };
        var steps = AssertInSync(Load(Dmu), DmuFight(), o);
        var branch = steps.FindIndex(s => s.Result is { ToTime: 13000.0 });
        Assert.True(branch > 0);
        Assert.Contains(steps.Skip(branch), s => s.Result is { ToTime: 1087.7 });
        Assert.True(steps[^1].After >= 1355.6);
    }

    [Fact]
    public void A_wipe_stops_the_engine_and_the_next_pull_syncs_again()
    {
        var timeline = Load(Top);
        var fight = TopFight();
        var first = FightSimulator.Simulate(timeline, fight, new SimulationOptions { Seed = 8, WipeAt = 250 });
        var second = FightSimulator.Simulate(timeline, fight, new SimulationOptions { Seed = 9, PullClock = 2000 });
        var (player, steps) = Play(timeline, first.Concat(second).ToList());

        var wipe = steps.FindIndex(s => s.Sim.Event is MitCue.Core.Events.DutyReset);
        Assert.True(wipe > 0);
        Assert.Equal(250, steps[wipe].After, 0.6); // stopped, time frozen where the wipe happened
        var restart = steps.FindIndex(wipe, s => s.Result?.Started == true);
        Assert.Equal(2000, steps[restart].Clock);
        foreach (var s in steps.Skip(restart).Where(s => s.Sim.SyncTime != null))
            Assert.InRange(s.After - s.Sim.SyncTime!.Value, -Tolerance, Tolerance);
        Assert.True(player.IsRunning);
        player.Tick(2000 + timeline.EndTime + TimelinePlayer.EndGrace + 1);
        Assert.False(player.IsRunning); // stops by itself after the end
        Assert.Equal(timeline.EndTime + TimelinePlayer.EndGrace, player.TimeAt(5000), 1e-9);
    }

    /// <summary>
    /// Random but realistic phase pushes: each phase dies somewhere in the second half of its synced entries and
    /// the next phase starts a few seconds later, before the timeline could reach that phase's start by itself
    /// (otherwise a <c>window N,0</c> phase sync has already closed, in cactbot too).
    /// </summary>
    private static SimulationOptions Pushes(Timeline timeline, FightDefinition fight, int seed, double jitter)
    {
        var rng = new Random(seed * 7919);
        var o = new SimulationOptions { Seed = seed, Jitter = jitter };
        var phases = fight.Phases;
        var realStart = phases[0].Start;
        for (var i = 1; i < phases.Count; i++)
        {
            var prev = phases[i - 1];
            var cur = phases[i];
            var last = timeline.Entries.Where(e => e.CanSync && e.Time >= prev.Start && e.Time < cur.Start).Max(e => e.Time);
            var cut = prev.Start + ((last - prev.Start) * Between(rng, 0.5, 0.95));
            var gap = Between(rng, 3, cur.Start - cut - 1);
            o.PhaseCutoff[prev.Id] = cut;
            realStart = realStart + (cut - prev.Start) + gap;
            o.PhaseRealStart[cur.Id] = realStart;
        }

        return o;
    }

    private static double Between(Random rng, double min, double max) => min + ((max - min) * rng.NextDouble());

    /// <summary>
    /// Plays the simulated pull and checks every event that should sync: right after it the engine's time is
    /// within <see cref="Tolerance"/> of the entry's time (or jump target), and right before it too when the
    /// previous event was in the same phase without a jump in between (no push to absorb).
    /// </summary>
    private static List<Step> AssertInSync(Timeline timeline, FightDefinition fight, SimulationOptions options, SyncMatchOptions? match = null)
    {
        var sims = FightSimulator.Simulate(timeline, fight, options);
        var (_, steps) = Play(timeline, sims, match);
        var problems = new List<string>();
        Step? previous = null;
        foreach (var step in steps)
        {
            if (step.Sim.SyncTime is not { } expected)
            {
                previous = step;
                continue;
            }

            if (Math.Abs(step.After - expected) > Tolerance)
                problems.Add($"after  {step.Sim.Entry} at clock {step.Clock:0.00}: engine {step.After:0.00}, expected {expected}");

            var samePhase = previous?.Sim.SyncTime is { } p
                            && previous.Sim.Entry?.JumpTime == null
                            && step.Sim.Entry?.JumpTime == null
                            && fight.PhaseAt(p) == fight.PhaseAt(expected);
            if (samePhase && Math.Abs(step.Before - expected) > Tolerance)
                problems.Add($"before {step.Sim.Entry} at clock {step.Clock:0.00}: engine {step.Before:0.00}, expected {expected}");
            previous = step;
        }

        Assert.True(problems.Count == 0, $"{problems.Count} of {steps.Count} events out of tolerance:\n" + string.Join("\n", problems.Take(25)));
        return steps;
    }
}
