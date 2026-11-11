using MitCue.Core.Events;
using MitCue.Core.Simulation;
using MitCue.Core.Timelines;
using static MitCue.Core.Tests.Timelines.TimelineKit;

namespace MitCue.Core.Tests.Timelines;

/// <summary>What the simulator emits: one valid event per synced entry on one path, timed by phase.</summary>
public class FightSimulatorTests
{
    private static SimulationOptions Exact(Action<SimulationOptions>? configure = null)
    {
        var o = new SimulationOptions { Jitter = 0 };
        configure?.Invoke(o);
        return o;
    }

    [Theory]
    [InlineData(Top)]
    [InlineData(Tea)]
    [InlineData(Dmu)]
    [InlineData(Dsr)]
    [InlineData(Fru)]
    public void Events_are_ordered_and_each_matches_the_entry_it_stands_for(string file)
    {
        var timeline = Load(file);
        var sims = FightSimulator.Simulate(timeline, null, new SimulationOptions { Seed = 7 });
        Assert.Equal(sims.Select(s => s.Event.Time).Order(), sims.Select(s => s.Event.Time));
        Assert.IsType<CombatStateChanged>(sims[0].Event);
        Assert.True(((CombatStateChanged)sims[0].Event).InCombat);
        Assert.Equal(1000, sims[0].Event.Time);
        foreach (var s in sims.Skip(1))
        {
            Assert.NotNull(s.Entry);
            Assert.True(s.Entry.Sync!.Matches(s.Event), $"{s.Entry} does not match {s.Event}");
            Assert.NotNull(s.Event.SyncType);
            Assert.Equal(s.Entry.JumpTime ?? s.Entry.Time, s.SyncTime);
        }

        Assert.Equal(sims.Select(s => Key(s.Event)), FightSimulator.Generate(timeline, null, new SimulationOptions { Seed = 7 }).Select(Key));
    }

    /// <summary>Comparable summary of an event (records holding lists do not compare by content).</summary>
    private static (string, double, string?, string?, string?) Key(GameEvent e) =>
        (e.GetType().Name, e.Time, e.SyncType, e.GetField("id") ?? e.GetField("command") ?? e.GetField("effectId"), e.GetField("source"));

    [Fact]
    public void Linear_timeline_without_jitter_emits_every_synced_entry_at_its_time()
    {
        var top = Load(Top);
        var sims = FightSimulator.Simulate(top, TopFight(), Exact());
        var expected = top.ByTime.Where(e => e.CanSync && e.Sync!.Type != "InCombat" && e.JumpTime == null).ToList();
        Assert.Equal(expected, sims.Skip(1).Select(s => s.Entry));
        Assert.All(sims.Skip(1), s => Assert.Equal(1000 + s.Entry!.Time, s.Event.Time, 1e-9));
        Assert.Equal(top.Entry("--Reset--"), top.AtLine(9)); // the wipe sync (jump 0) is not part of a pull
        Assert.DoesNotContain(sims, s => s.Entry?.Line == 9);
    }

    [Fact]
    public void Typed_events_for_every_sync_type_the_ultimates_use()
    {
        var dmu = FightSimulator.Simulate(Load(Dmu), DmuFight(), new SimulationOptions { Seed = 3 });
        Assert.Contains(dmu, s => s.Event is AbilityResolved { SourceIsPlayer: false });
        Assert.Contains(dmu, s => s.Event is CastStarted);
        Assert.Contains(dmu, s => s.Event is DirectorCommand { Command: 0x80000027 });
        Assert.Contains(dmu, s => s.Event is HeadMarkerShown { MarkerId: 0x103 });
        Assert.Contains(dmu, s => s.Event is StatusGained { EffectId: 0x306 });
        Assert.Contains(dmu, s => s.Event is StatusLost { EffectId: 0x1061 or 0x1063 });
        Assert.Contains(dmu, s => s.Event is NpcSpawnExtra { TetherId: 0x54 });
        Assert.Contains(dmu, s => s.Event is ActorControlExtraCommand { Category: 0x19D, Param1: 0x10, Param2: 0x20 });
        Assert.DoesNotContain(dmu, s => s.Event is GenericSyncEvent);

        var tea = FightSimulator.Simulate(Load(Tea), TeaFight(), new SimulationOptions());
        Assert.Contains(tea, s => s.Event is CombatantRemoved { Name: "Liquid Hand" });
        Assert.Contains(tea, s => s.Event is AbilityResolved { ActionId: 0x482B or 0x482C }); // "482[BC]"

        var top = FightSimulator.Simulate(Load(Top), TopFight(), new SimulationOptions());
        Assert.Contains(top, s => s.Event is MapEffectOccurred { Location: 0x0A, Flags: 0x00020001 });
        var cast = top.Select(s => s.Event).OfType<CastStarted>().First(c => c.ActionId == 0x7B03);
        Assert.Equal(4.0f, cast.CastTime); // 11.0 cast start, 15.0 Program Loop
        Assert.Equal("Omega", cast.SourceName);

        var fru = FightSimulator.Simulate(Load(Fru), null, new SimulationOptions());
        Assert.Contains(fru, s => s.Event is CombatantDefeated { TargetName: "Ice Veil" });
        var dsr = FightSimulator.Simulate(Load(Dsr), null, new SimulationOptions());
        Assert.Contains(dsr, s => s.Event is CastCancelled { ActionId: 0x63C3 });
    }

    [Fact]
    public void Syncs_without_a_source_use_the_default_source()
    {
        var t = Parse("0.0 \"--sync--\" InCombat { inGameCombat: \"1\" } window 0,1", "5 \"X\" Ability { id: \"10\" }");
        var e = (AbilityResolved)FightSimulator.Generate(t, null, Exact(o => o.DefaultSource = "Kefka"))[1];
        Assert.Equal(("Kefka", 0x10u), (e.SourceName, e.ActionId));
    }

    [Fact]
    public void Same_seed_same_events_and_jitter_stays_within_bounds()
    {
        var top = Load(Top);
        var a = FightSimulator.Simulate(top, TopFight(), new SimulationOptions { Seed = 11, Jitter = 0.3 });
        var b = FightSimulator.Simulate(top, TopFight(), new SimulationOptions { Seed = 11, Jitter = 0.3 });
        Assert.Equal(a.Select(s => Key(s.Event)), b.Select(s => Key(s.Event)));
        Assert.All(a.Skip(1), s => Assert.InRange(s.Event.Time - 1000 - s.Entry!.Time, -0.3 - 1e-9, 0.3 + 1e-9));
        var c = FightSimulator.Simulate(top, TopFight(), new SimulationOptions { Seed = 12, Jitter = 0.3 });
        Assert.NotEqual(a.Select(s => s.Event.Time), c.Select(s => s.Event.Time));
    }

    [Fact]
    public void Phase_real_start_shifts_the_phase_and_cutoff_drops_the_rest_of_the_previous_one()
    {
        var sims = FightSimulator.Simulate(Load(Top), TopFight(), Exact(o =>
        {
            o.PhaseCutoff["p1"] = 100;
            o.PhaseRealStart["p2"] = 150;
        }));
        Assert.DoesNotContain(sims, s => s.Entry != null && s.Entry.Time > 100 && s.Entry.Time < 200);
        var mapEffect = Assert.Single(sims, s => s.Event is MapEffectOccurred);
        Assert.Equal(1150, mapEffect.Event.Time, 1e-9);
        Assert.Equal(1150 + 3.4, sims.Single(s => s.Event is CastStarted { ActionId: 0x7B40 }).Event.Time, 1e-9);

        // P3 has no real start of its own: it keeps P2's push (the timeline's spacing from P2).
        Assert.Equal(1350, sims.Single(s => s.Entry?.Time == 400.0).Event.Time, 1e-9);
    }

    [Fact]
    public void Preview_style_options_skip_to_a_phase()
    {
        // PreviewDriver: earlier phases keep only the pull, the chosen phase starts 2 s after it and the
        // later phases follow it (not listed).
        var fight = TopFight();
        var sims = FightSimulator.Simulate(Load(Top), fight, Exact(o =>
        {
            o.PhaseCutoff["p1"] = 0.5;
            o.PhaseCutoff["p2"] = 199.999;
            o.PhaseRealStart["p3"] = 2;
        }));
        Assert.Equal(400.0, sims[1].Entry!.Time);
        Assert.Equal(1002, sims[1].Event.Time, 1e-9);
        Assert.Equal(1002 + 700 + 0, sims.Single(s => s.Entry?.Time == 1100.0).Event.Time, 1e-9);
    }

    [Theory]
    [InlineData("p3")]
    [InlineData("p4")]
    [InlineData("p5")]
    public void Phases_skipped_by_a_cutoff_produce_no_events(string from)
    {
        // A preview from a later DMU phase cuts every earlier phase at its start: none of their events may
        // happen, not even the jump out of the phase (it used to land in the middle of the chosen phase).
        var dmu = Load(Dmu);
        var fight = DmuFight();
        var sims = FightSimulator.Simulate(dmu, fight, PreviewFrom(fight, from, new SimulationOptions { Seed = 5 }));
        var start = fight.GetPhase(from)!.Start;
        Assert.DoesNotContain(sims, s => s.Entry != null && s.Entry.Time > 0.5 && s.Entry.Time < start);
        var first = sims.First(s => s.Entry?.Time >= start);
        Assert.Equal(1002 + (first.Entry!.Time - start), first.Event.Time, 0.2);
    }

    /// <summary>The options <c>PreviewDriver.ConfigurePhaseStart</c> builds for a preview from <paramref name="phaseId"/>.</summary>
    internal static SimulationOptions PreviewFrom(MitCue.Core.Fights.FightDefinition fight, string phaseId, SimulationOptions o)
    {
        var ordered = fight.Phases.OrderBy(p => p.Start).ToList();
        var index = ordered.FindIndex(p => p.Id == phaseId);
        for (var i = 0; i < index; i++)
            o.PhaseCutoff[ordered[i].Id] = i == 0 ? ordered[i].Start + 0.5 : ordered[i].Start - 0.001;
        o.PhaseRealStart[phaseId] = 2;
        return o;
    }

    [Fact]
    public void Wipe_ends_the_pull()
    {
        var sims = FightSimulator.Simulate(Load(Top), TopFight(), Exact(o => o.WipeAt = 100));
        Assert.All(sims, s => Assert.True(s.Event.Time <= 1100));
        Assert.Equal(
            [typeof(CombatStateChanged), typeof(DutyReset)],
            sims.TakeLast(2).Select(s => s.Event.GetType()));
        Assert.False(((CombatStateChanged)sims[^2].Event).InCombat);
        Assert.Equal(DutyResetKind.Wipe, ((DutyReset)sims[^1].Event).Kind);
        Assert.Null(sims[^1].SyncTime);
        Assert.Contains(sims, s => s.Entry?.Time == 99.3);
    }

    [Fact]
    public void Dmu_default_path_takes_the_success_jumps_and_skips_the_enrage_branches()
    {
        var dmu = Load(Dmu);
        var sims = FightSimulator.Simulate(dmu, DmuFight(), Exact());
        var jumps = sims.Where(s => s.Entry?.JumpTime != null).Select(s => (s.Entry!.Time, s.SyncTime)).ToList();
        Assert.Equal([(483.2, (double?)597.0), (1215.3, 1355.6)], jumps);
        Assert.DoesNotContain(sims, s => s.Entry?.Time >= 10000);
        Assert.Contains(sims, s => s.Entry?.Name == "Forsaken Null (Enrage)");

        // The jump happens at the source's time; the destination keeps that spacing.
        var success = sims.Single(s => s.Entry?.Time == 483.2);
        Assert.Equal(1483.2, success.Event.Time, 1e-9);
        Assert.Equal(1483.2 + 3, sims.Single(s => s.Entry?.Time == 600.0).Event.Time, 1e-9);
        Assert.DoesNotContain(sims, s => s.Entry?.Time == 486.2); // the forcejump is never reached
    }

    [Fact]
    public void Dmu_branches_can_be_chosen()
    {
        var dmu = Load(Dmu);

        // P1 enrage: Kefka casts Light of Judgment at 197.5 and the pull jumps to 10000; it ends there.
        var enrage = FightSimulator.Simulate(dmu, DmuFight(), Exact(o => o.Jumps["L85"] = true));
        Assert.Equal(10005.0, enrage[^1].Entry!.Time);
        Assert.Equal(1197.5 + 5, enrage[^1].Event.Time, 1e-9);

        // P3 single-boss enrage, then P4 when it has a real start.
        var chaos = FightSimulator.Simulate(dmu, DmuFight(), Exact(o =>
        {
            o.Jumps["p3-chaos-enrage"] = true;
            o.PhaseRealStart["p4"] = 900;
        }));
        var path = chaos.Where(s => s.Entry != null).Select(s => s.Entry!.Time).ToList();
        var jump = path.IndexOf(941.8);
        Assert.Equal([941.8, 14010.0, 1087.7], path.Skip(jump).Take(3));
        Assert.Equal(1355.6, chaos.Single(s => s.Entry?.Time == 1215.3).SyncTime);

        // Explicitly refusing the P2 success jump: the forcejump at 486.2 still moves the pull to P3.
        var refused = FightSimulator.Simulate(dmu, DmuFight(), Exact(o => o.Jumps["L155"] = false));
        Assert.DoesNotContain(refused, s => s.Entry?.Time == 483.2);
        Assert.Equal(597.0, refused.Single(s => s.Entry?.Time == 486.2).SyncTime);
        Assert.Contains(refused, s => s.Entry?.Time == 600.0);
    }

    [Fact]
    public void A_cut_phase_drops_its_exit_jump_unless_asked_to_leave_through_it()
    {
        var dmu = Load(Dmu);

        // Documented behaviour: everything after the cutoff is dropped, the success jump included; P5 follows
        // P4 with the timeline's spacing (the engine runs on through P4's tail into P5).
        var dropped = FightSimulator.Simulate(dmu, DmuFight(), Exact(o => o.PhaseCutoff["p4"] = 1180));
        var shift = dropped.Single(s => s.Entry?.Time == 1087.7).Event.Time - 1000 - 1087.7; // -113.8 after the P2 -> P3 jump
        Assert.Equal(483.2 - 597, shift, 1e-9);
        Assert.DoesNotContain(dropped, s => s.Entry != null && s.Entry.Time > 1180 && s.Entry.Time < 1355.6);
        Assert.Equal(1000 + 1366.1 + shift, dropped.Single(s => s.Entry?.Time == 1366.1).Event.Time, 1e-9);

        // CutPhasesLeaveByJump: the jump's event (C3FD, "Kefka <20% HP") happens right at the cutoff ...
        var atCut = FightSimulator.Simulate(dmu, DmuFight(), Exact(o =>
        {
            o.PhaseCutoff["p4"] = 1180;
            o.CutPhasesLeaveByJump = true;
        }));
        var exit = atCut.Single(s => s.Entry?.Time == 1215.3);
        Assert.Equal(1355.6, exit.SyncTime);
        Assert.Equal(1000 + 1180 + shift, exit.Event.Time, 1e-9);
        Assert.DoesNotContain(atCut, s => s.Entry != null && s.Entry.Time > 1180 && s.Entry.Time < 1355.6 && s.Entry.Time != 1215.3);
        Assert.Equal(1000 + 1180 + shift + 10.5, atCut.Single(s => s.Entry?.Time == 1366.1).Event.Time, 1e-9);

        // ... or when the next phase's real start asks for it, while the engine can still sync it (at 1185) ...
        var listed = FightSimulator.Simulate(dmu, DmuFight(), Exact(o =>
        {
            o.PhaseCutoff["p4"] = 1180;
            o.PhaseRealStart["p5"] = 1185 + shift;
            o.CutPhasesLeaveByJump = true;
        }));
        Assert.Equal(1000 + 1185 + shift, listed.Single(s => s.Entry?.Time == 1215.3).Event.Time, 1e-9);

        // ... but not once its window [1125.3, 1220.3) has closed (at 1300): P5 then starts from the jump at the cutoff.
        var late = FightSimulator.Simulate(dmu, DmuFight(), Exact(o =>
        {
            o.PhaseCutoff["p4"] = 1180;
            o.PhaseRealStart["p5"] = 1300 + shift;
            o.CutPhasesLeaveByJump = true;
        }));
        Assert.Equal(1000 + 1180 + shift, late.Single(s => s.Entry?.Time == 1215.3).Event.Time, 1e-9);
        Assert.Equal(1000 + 1180 + shift + 10.5, late.Single(s => s.Entry?.Time == 1366.1).Event.Time, 1e-9);

        // A cutoff before the window opens: the jump waits for the window (plus a margin).
        var early = FightSimulator.Simulate(dmu, DmuFight(), Exact(o =>
        {
            o.PhaseCutoff["p4"] = 1100;
            o.CutPhasesLeaveByJump = true;
        }));
        Assert.InRange(early.Single(s => s.Entry?.Time == 1215.3).Event.Time - 1000 - shift, 1125.3, 1126);
    }

    [Fact]
    public void Dmu_listed_p3_start_does_not_outrun_the_486_2_forcejump()
    {
        // Review regression: P3 listed at its own timeline start (600) in an un-pushed pull used to move the
        // 483.2 success cast to 597, after the engine had force-jumped at 486.2: P3 could not be followed.
        var timeline = Load(Dmu);
        var o = new SimulationOptions { Seed = 1, Jitter = 0 };
        o.PhaseRealStart["p3"] = 600;
        var sims = FightSimulator.Simulate(timeline, DmuFight(), o);

        var cast = sims.Single(s => s.Entry?.Line == 155); // 483.2 StartsUsing C3F7 ... jump "p2-success"
        var forcejumpClock = 1000 + 486.2;                 // un-pushed P2: the engine reaches 486.2 "Aero III Assault?" forcejump here
        Assert.True(cast.Event.Time <= forcejumpClock, $"the jump's cast is emitted at clock {cast.Event.Time:0.0}, after the engine force-jumps at {forcejumpClock:0.0}");
        Assert.Equal(1483.2, cast.Event.Time, 1e-9); // the listed start cannot be kept: P3 starts from the jump

        var (_, steps) = Play(timeline, sims);
        Assert.All(
            steps.Where(s => s.Sim.SyncTime is { } t && t >= 597 && t < 1087.7),
            s => Assert.InRange(s.After - s.Sim.SyncTime!.Value, -0.6, 0.6));
    }

    [Fact]
    public void A_listed_start_moves_the_jump_only_within_the_range_the_engine_can_sync()
    {
        var dmu = Load(Dmu);

        // P2 cut at 400 and P3 listed 5 s later: the success cast moves to 3 s before P3 (it lands at 597).
        var near = FightSimulator.Simulate(dmu, DmuFight(), Exact(o =>
        {
            o.PhaseCutoff["p2"] = 400;
            o.PhaseRealStart["p3"] = 405;
            o.CutPhasesLeaveByJump = true;
        }));
        Assert.Equal(1000 + 402, near.Single(s => s.Entry?.Line == 155).Event.Time, 1e-9);
        Assert.Equal(1000 + 405, near.Single(s => s.Entry?.Time == 600.0).Event.Time, 1e-9);

        // Not past the 486.2 forcejump, margin included (twice the jitter + 0.1 s): with jitter 0.2 the latest
        // is 486.2 - 0.5 = 485.7, so a P3 listed at 488.8 (cast at 485.8) starts from the jump at 483.2 instead.
        var edge = FightSimulator.Simulate(dmu, DmuFight(), new SimulationOptions { Seed = 2, Jitter = 0.2, PhaseRealStart = { ["p3"] = 488.8 } });
        Assert.InRange(edge.Single(s => s.Entry?.Line == 155).Event.Time - 1000, 483.0, 483.4);
        var inside = FightSimulator.Simulate(dmu, DmuFight(), new SimulationOptions { Seed = 2, Jitter = 0.2, PhaseRealStart = { ["p3"] = 488.6 } });
        Assert.InRange(inside.Single(s => s.Entry?.Line == 155).Event.Time - 1000, 485.4, 485.8);
    }

    [Fact]
    public void Jumps_and_forcejump_loops_follow_the_timeline()
    {
        var t = Parse(
            "0.0 \"--sync--\" InCombat { inGameCombat: \"1\" } window 0,1",
            "5 \"Back\" Ability { id: \"1\" } jump 2",
            "10 \"Loop\" Ability { id: \"2\" } forcejump 8",
            "12 \"After\" Ability { id: \"3\" }");
        var plain = FightSimulator.Simulate(t, null, Exact());
        // The backward jump is not taken by default, so its event does not happen; the forcejump loops
        // MaxJumpRepeats times, then the pull goes on.
        Assert.DoesNotContain(plain, s => s.Entry?.Name == "Back");
        Assert.Equal(FightSimulator.MaxJumpRepeats, plain.Count(s => s.Entry?.Name == "Loop"));
        Assert.Equal("After", plain[^1].Entry!.Name);
        Assert.Equal(1000 + 12 + (FightSimulator.MaxJumpRepeats * 2), plain[^1].Event.Time, 1e-9); // each loop adds 2 s

        var back = FightSimulator.Simulate(t, null, Exact(o => o.Jumps["2"] = true));
        Assert.Equal(FightSimulator.MaxJumpRepeats, back.Count(s => s.Entry?.Name == "Back"));
    }

    [Fact]
    public void Dsr_loop_back_does_not_repeat_the_jump_event_at_its_target()
    {
        var dsr = Load(Dsr);
        var sims = FightSimulator.Simulate(dsr, null, Exact(o => o.Jumps["L223"] = true));
        var loop = sims.Where(s => s.Entry?.Line == 223).ToList();
        Assert.NotEmpty(loop);
        // 2055.9 StartsUsing 63C8 jumps to 550, where 550.0 is the same cast: it is not emitted again right after.
        var first = sims.ToList().FindIndex(s => s.Entry?.Line == 223);
        Assert.NotEqual(550.0, sims[first + 1].Entry!.Time);
    }
}
