using MitCue.Core.Timelines;
using static MitCue.Core.Tests.Timelines.TimelineKit;

namespace MitCue.Core.Tests.Timelines;

/// <summary>
/// The five cactbot timelines in data/timelines parse completely. The counts are cactbot's own (its
/// TimelineParser run on the same files, docs/research/cactbot-timelines.md §4.1); an entry-by-entry
/// comparison with cactbot's parse output (times, names, durations, sync parameters, windows, jumps,
/// sync order) matched for all five files when this parser was written.
/// </summary>
public class BundledTimelineTests
{
    [Theory]
    [InlineData(Top, 282, 207, 0)]
    [InlineData(Tea, 225, 144, 0)]
    [InlineData(Dmu, 417, 250, 8)]
    [InlineData(Dsr, 367, 225, 0)]
    [InlineData(Fru, 227, 181, 0)]
    public void Parses_with_cactbots_entry_and_sync_counts(string file, int entries, int syncs, int labels)
    {
        var t = Load(file);
        Assert.Equal(file, t.Name);
        Assert.Equal(entries, t.Entries.Count);
        Assert.Equal(syncs, t.Entries.Count(e => e.Sync != null));
        Assert.Equal(labels, t.Labels.Count);
        Assert.Equal(["--Reset--", "--sync--"], t.HiddenNames.Order(StringComparer.Ordinal));
        Assert.All(t.Entries, e => Assert.Equal(e, t.Entries[e.Index]));
        Assert.Equal(t.Entries.Select(e => e.Line).Order(), t.Entries.Select(e => e.Line)); // file order
        Assert.All(t.Entries.Where(e => e.Sync != null), e => Assert.Null(e.Sync!.LegacyRegex));
    }

    [Theory]
    [InlineData(Top)]
    [InlineData(Dmu)]
    [InlineData(Dsr)]
    [InlineData(Fru)]
    public void Has_no_warnings(string file) => Assert.Empty(Load(file).Warnings);

    [Fact]
    public void Tea_warns_only_about_the_trailing_space_in_liquid_rage()
    {
        var t = Load(Tea);
        var warning = Assert.Single(t.Warnings);
        Assert.StartsWith("line 39: value \"Liquid Rage \" of field \"source\" has leading or trailing spaces", warning);

        // Kept as written: it still only matches a name with the space (quirk Q7).
        var rage = t.AtLine(39);
        Assert.True(rage.Sync!.Matches(Ability(0, 0x49B5, "Liquid Rage ")));
        Assert.False(rage.Sync.Matches(Ability(0, 0x49B5, "Liquid Rage")));
    }

    [Fact]
    public void Top_entries()
    {
        var t = Load(Top);

        var reset = t.AtLine(9);
        Assert.Equal("--Reset--", reset.Name);
        Assert.True(reset.IsHidden);
        Assert.Equal("ActorControl", reset.Sync!.Type);
        Assert.Equal(["4000000F"], reset.Sync.Fields["command"].Values);
        Assert.Equal(new SyncWindow(0, 100000), reset.Window);
        Assert.Equal(0, reset.JumpTime);
        Assert.False(reset.ForceJump);

        var p3 = t.AtLine(86);
        Assert.Equal((400.0, "--sync--", "Ability"), (p3.Time, p3.Name, p3.Sync!.Type));
        Assert.Equal(new SyncWindow(400, 0), p3.Window);
        Assert.Equal((0.0, 400.0), (p3.SyncStart, p3.SyncEnd));
        Assert.Null(p3.JumpTime);

        var mapEffect = t.AtLine(48);
        Assert.Equal("MapEffect { flags: \"00020001\", location: \"0A\" }", mapEffect.Sync!.ToString());

        var either = t.AtLine(58);
        Assert.Equal(["7B2A", "7B2D"], either.Sync!.Fields["id"].Values);
        Assert.Equal(["Omega-F"], either.Sync.Fields["source"].Values);
        Assert.Equal(SyncWindow.Default, either.Window);

        var display = t.AtLine(39);
        Assert.Equal(("Diffuse Wave Cannon Kyrios x5", 108.0, 8.0), (display.Name, display.Time, display.Duration));
        Assert.Null(display.Sync); // "#Ability { ... }" is a comment

        var colossal = t.AtLine(95);
        Assert.Equal(new SyncWindow(1, 1), colossal.Window);
        Assert.Equal(["Left Arm Unit", "Right Arm Unit"], colossal.Sync!.Fields["source"].Values);

        var storage = t.AtLine(230);
        Assert.True(storage.Sync!.Fields["id"].IsRegex);
        Assert.Equal(["7B0[45]"], storage.Sync.Fields["id"].Values);

        var enrage = t.AtLine(369);
        Assert.Equal((1421.9, "Run: ****mi* Enrage"), (enrage.Time, enrage.Name));
        Assert.Equal(1421.9, t.EndTime);

        Assert.Equal(
            [("7B03", 15.0), ("7B40", 206.4), ("7B13", 400.0), ("7B47", 600.0), ("7B7C", 700.0), ("7F72", 1100.0)],
            t.PhaseAnchors.Select(a => (a.AbilityId, a.Time)));
        Assert.Null(t.ZoneId);
    }

    [Fact]
    public void Tea_entries()
    {
        var t = Load(Tea);

        var start = t.AtLine(9);
        Assert.Equal((0.0, "Start"), (start.Time, start.Name));
        Assert.Null(start.Sync);
        Assert.False(start.IsHidden);

        var fluidSwing = t.AtLine(11);
        Assert.Equal(new SyncWindow(10, 2.5), fluidSwing.Window);
        Assert.Equal((1.4, 13.9), (fluidSwing.SyncStart, fluidSwing.SyncEnd), new ToleranceComparer());

        var hands = t.AtLine(13);
        Assert.Equal(["482[BC]"], hands.Sync!.Fields["id"].Values);
        Assert.True(hands.Sync.Matches(Ability(0, 0x482B, "Liquid Hand")));
        Assert.True(hands.Sync.Matches(Ability(0, 0x482C, "liquid hand")));
        Assert.False(hands.Sync.Matches(Ability(0, 0x482D, "Liquid Hand")));

        var splash = t.AtLine(29);
        Assert.Equal(("Splash x6", 5.6), (splash.Name, splash.Duration));
        Assert.Null(splash.Sync);

        var removed = t.AtLine(49);
        Assert.Equal("RemovedCombatant { name: \"Liquid Hand\" }", removed.Sync!.ToString());
        Assert.Equal(new SyncWindow(50, 0), removed.Window);

        Assert.Equal(new SyncWindow(67, 5), t.AtLine(171).Window);
        Assert.Equal(new SyncWindow(900, 0), t.AtLine(177).Window);
        Assert.Equal([("49B0", 11.4), ("4830", 200.0)], t.PhaseAnchors.Select(a => (a.AbilityId, a.Time)));
        Assert.Empty(t.Labels);
        Assert.DoesNotContain(t.Entries, e => e.JumpTime != null || e.JumpLabel != null);
    }

    [Fact]
    public void Dmu_entries()
    {
        var t = Load(Dmu);

        Assert.Equal(
            new Dictionary<string, double>
            {
                ["p2-success"] = 597, ["p4-success"] = 1355.6, ["p1-and-p4-enrage"] = 10000, ["p2-enrage"] = 11000,
                ["p3-enrage"] = 12000, ["p3-exdeath-enrage"] = 13000, ["p3-chaos-enrage"] = 14000, ["p4-enrage"] = 15000,
            },
            t.Labels);

        // 10 normal jumps (one is --Reset--'s jump 0) and one forcejump, all resolved.
        var jumps = t.Entries.Where(e => e.JumpTime != null).ToList();
        Assert.Equal(11, jumps.Count);
        Assert.Single(jumps, e => e.ForceJump);
        Assert.DoesNotContain(t.Entries, e => e.JumpLabel != null && e.JumpTime == null);

        var force = t.AtLine(157);
        Assert.Equal(("Aero III Assault?", 486.2), (force.Name, force.Time));
        Assert.True(force.ForceJump);
        Assert.Equal(("p2-success", 597.0), (force.JumpLabel, force.JumpTime!.Value));

        var p4 = t.AtLine(338);
        Assert.Equal(new SyncWindow(1088, 14000), p4.Window);
        Assert.Equal(["80000027"], p4.Sync!.Fields["command"].Values);
        Assert.Equal(["2E"], p4.Sync.Fields["data0"].Values);

        var marker = t.AtLine(106);
        Assert.Equal("HeadMarker { id: \"0103\" }", marker.Sync!.ToString()); // single-quoted in the file

        var down = t.AtLine(161);
        Assert.Equal(("GainsEffect", 32.5), (down.Sync!.Type, down.Duration!.Value));

        var twisters = t.AtLine(379);
        Assert.Equal(("StartsUsing", 5.0), (twisters.Sync!.Type, twisters.Duration!.Value)); // duration after the sync
        Assert.Equal(["BB22", "BB23"], twisters.Sync.Fields["id"].Values);

        var surge = t.AtLine(371);
        Assert.False(surge.Sync!.Fields.ContainsKey("source"));

        var tower = t.AtLine(448);
        Assert.Equal("ActorControlExtra { category: \"019D\", param1: \"10\", param2: \"20\" }", tower.Sync!.ToString());

        var enrages = t.Entries.Where(e => e.Time == 941.8 && e.JumpTime != null).Select(e => (e.JumpLabel, e.JumpTime)).ToList();
        Assert.Equal([("p3-chaos-enrage", 14000.0), ("p3-exdeath-enrage", 13000.0), ("p3-enrage", 12000.0), ("p3-enrage", 12000.0)], enrages);

        var success = t.AtLine(396);
        Assert.Equal((new SyncWindow(90, 5), 1355.6), (success.Window, success.JumpTime!.Value));

        Assert.Equal(1363, t.ZoneId);
        Assert.Equal(["C403", "C24C", "C3F7", "C2DC", "BB40"], t.PhaseAnchors.Select(a => a.AbilityId));
        Assert.Equal(15005, t.EndTime);
    }

    [Fact]
    public void Dsr_and_fru_entries()
    {
        var dsr = Load(Dsr);
        var loop = dsr.AtLine(223);
        Assert.Equal((new SyncWindow(60, 200), 550.0, (string?)null), (loop.Window, loop.JumpTime!.Value, loop.JumpLabel));
        Assert.Equal(("NetworkCancelAbility", new SyncWindow(300, 0)), (dsr.AtLine(156).Sync!.Type, dsr.AtLine(156).Window));
        Assert.Equal(4248.7, dsr.AtLine(476).JumpTime);
        Assert.Equal(("StartsUsing", 36.0), (dsr.AtLine(53).Sync!.Type, dsr.AtLine(53).Duration!.Value));
        Assert.Equal("63C8", dsr.PhaseAnchors[0].AbilityId); // "# phase 1 logs: -p ..." is not a hint line

        var fru = Load(Fru);
        var defeated = fru.AtLine(138);
        Assert.Equal("WasDefeated { target: \"Ice Veil\" }", defeated.Sync!.ToString());
        Assert.Equal((0.0, 493.8), (defeated.SyncStart, defeated.SyncEnd), new ToleranceComparer());
        Assert.Equal(new SyncWindow(424.4, 2.5), fru.AtLine(121).Window);
        Assert.Equal(["1043"], fru.AtLine(150).Sync!.Fields["effectId"].Values);
        Assert.Equal(new SyncWindow(150, 5), fru.AtLine(222).Window);
        Assert.Equal(["00020001"], fru.AtLine(66).Sync!.Fields["flags"].Values);
        Assert.Equal(1238, fru.ZoneId);
    }

    private sealed class ToleranceComparer : IEqualityComparer<(double, double)>
    {
        public bool Equals((double, double) x, (double, double) y) => Math.Abs(x.Item1 - y.Item1) < 1e-9 && Math.Abs(x.Item2 - y.Item2) < 1e-9;

        public int GetHashCode((double, double) obj) => 0;
    }
}
