using MitCue.Core.Events;
using MitCue.Core.Timelines;
using static MitCue.Core.Tests.Timelines.TimelineKit;

namespace MitCue.Core.Tests.Timelines;

/// <summary>
/// Grammar edge cases (docs/research/cactbot-timelines.md §2, §2.10). Each expectation is what cactbot's
/// own TimelineParser does with the same line, except the documented differences: a rejected entry also
/// loses its sync (Q4), and problems come with a readable message.
/// </summary>
public class TimelineGrammarTests
{
    private static TimelineEntry Only(Timeline t) => Assert.Single(t.Entries);

    private static void AssertWarning(Timeline t, int line, string contains) =>
        Assert.Contains(t.Warnings, w => w.StartsWith($"line {line}: ", StringComparison.Ordinal) && w.Contains(contains, StringComparison.Ordinal));

    [Fact]
    public void Times_are_unsigned_decimals_with_optional_fraction()
    {
        var t = Parse("0 \"Start\"", "1 \"Int\"", "1.25 \"Two decimals\"", ".5 \"No leading digit\"", "-1 \"Negative\"", "1e3 \"Exponent\"");
        Assert.Equal([0, 1, 1.25], t.Entries.Select(e => e.Time));
        Assert.Equal(3, t.Warnings.Count);
        AssertWarning(t, 4, "not a timeline statement");
        AssertWarning(t, 5, "not a timeline statement");
        AssertWarning(t, 6, "not a timeline statement");
    }

    [Fact]
    public void Entry_names_may_be_empty_or_contain_a_hash_but_need_both_quotes()
    {
        var t = Parse("2.1 \"\"", "2.2 \"Name with # hash\" Ability { id: \"1\" }", "12 \"Unclosed", "13 \"Name\" \"Second string\"");
        Assert.Equal(["", "Name with # hash"], t.Entries.Select(e => e.Name));
        Assert.NotNull(t.Entries[1].Sync); // the name is taken before the comment is cut
        AssertWarning(t, 3, "not a timeline statement");
        AssertWarning(t, 4, "unexpected text \"\"Second string\"\"");
    }

    [Fact]
    public void Whitespace_tabs_and_crlf_are_accepted()
    {
        var t = TimelineParser.Parse("9    \"Multiple spaces\"   Ability {  id: \"1\"  }   window 1,1\r\n10\t\"Tab\"\tAbility { id: \"1\" }\r\n  11 \"CRLF\"  \r\n");
        Assert.Equal(["Multiple spaces", "Tab", "CRLF"], t.Entries.Select(e => e.Name));
        Assert.Equal(new SyncWindow(1, 1), t.Entries[0].Window);
        Assert.Equal(["1"], t.Entries[1].Sync!.Fields["id"].Values);
        Assert.Empty(t.Warnings);
    }

    [Fact]
    public void Comments_are_cut_from_entries_and_disable_syncs()
    {
        var t = Parse(
            "# full-line comment",
            "   # indented comment",
            "2.3 \"Inline\" Ability { id: \"1\" } # inline comment",
            "2.4 \"Disabled\" #Ability { id: \"1\" } commented sync",
            "3 \"Hash in value\" Ability { id: \"1#2\" }");
        Assert.Equal(["Inline", "Disabled"], t.Entries.Select(e => e.Name));
        Assert.NotNull(t.Entry("Inline").Sync);
        Assert.Null(t.Entry("Disabled").Sync);
        AssertWarning(t, 5, "unexpected text"); // the '#' cuts the closing brace
        Assert.Single(t.Warnings);
    }

    [Fact]
    public void Duration_may_come_before_or_after_the_sync()
    {
        var t = Parse(
            "2.5 \"Dur\" duration 5",
            "2.6 \"After\" Ability { id: \"2\" } duration 3",
            "2.7 \"Before\" duration 3 Ability { id: \"3\" }",
            "30 \"Word in value\" Ability { id: \"1\", source: \"duration 9\" }",
            "635.2 \"Decimal\" duration 10.0");
        Assert.Equal([5.0, 3.0, 3.0, null, 10.0], t.Entries.Select(e => e.Duration));
        Assert.Equal(["duration 9"], t.Entry("Word in value").Sync!.Fields["source"].Values);
        Assert.Null(t.Entry("Dur").Sync);
        Assert.Empty(t.Warnings);
    }

    [Fact]
    public void Window_forms()
    {
        var t = Parse(
            "10 \"Default\" Ability { id: \"1\" }",
            "10 \"Single\" Ability { id: \"5\" } window 10",
            "10 \"Both\" Ability { id: \"7\" } window 1.5,0.5",
            "10 \"Phase\" Ability { id: \"8\" } window 10,0",
            "29 \"Keyword in value\" Ability { id: \"1\", source: \"window 5,5\" }");
        Assert.Equal(
            [new SyncWindow(2.5, 2.5), new SyncWindow(5, 5), new SyncWindow(1.5, 0.5), new SyncWindow(10, 0), SyncWindow.Default],
            t.Entries.Select(e => e.Window));
        Assert.Equal((0.0, 10.0), (t.Entry("Phase").SyncStart, t.Entry("Phase").SyncEnd));
        Assert.Empty(t.Warnings);
    }

    [Fact]
    public void Window_errors_reject_the_entry_and_its_sync()
    {
        var t = Parse(
            "2.8 \"Window before sync\" window 1,1 Ability { id: \"4\" }",
            "3.0 \"Window space\" Ability { id: \"6\" } window 10, 10",
            "14 \"Zero window\" Ability { id: \"1\" } window 0,0");
        var zero = Only(t);
        Assert.True(zero.Window.IsEmpty);
        Assert.False(zero.CanSync);
        AssertWarning(t, 1, "unexpected text \"window 1,1\"");
        AssertWarning(t, 2, "unexpected text \"window 10, 10\"");
        AssertWarning(t, 3, "window is empty");
    }

    [Fact]
    public void Jumps_numbers_labels_and_forcejumps()
    {
        var t = Parse(
            "3.2 \"Jump number\" Ability { id: \"8\" } jump 100",
            "3.3 \"Jump label\" Ability { id: \"9\" } jump \"lbl\"",
            "3.4 \"Forcejump label\" Ability { id: \"A\" } forcejump \"lbl\"",
            "3.6 \"Jump before window\" Ability { id: \"C\" } jump 50 window 20,20",
            "32 \"Forcejump number\" Ability { id: \"1\" } window 5,5 forcejump 2",
            "31 \"Label at zero\" Ability { id: \"1\" } jump \"zero\"",
            "6 label \"lbl\"",
            "0 label \"zero\"");
        Assert.Equal((100.0, (string?)null, false), (t.Entry("Jump number").JumpTime!.Value, t.Entry("Jump number").JumpLabel, t.Entry("Jump number").ForceJump));
        Assert.Equal((6.0, "lbl", false), (t.Entry("Jump label").JumpTime!.Value, t.Entry("Jump label").JumpLabel, t.Entry("Jump label").ForceJump));
        Assert.Equal((6.0, true), (t.Entry("Forcejump label").JumpTime!.Value, t.Entry("Forcejump label").ForceJump));
        Assert.Equal((50.0, new SyncWindow(20, 20)), (t.Entry("Jump before window").JumpTime!.Value, t.Entry("Jump before window").Window));
        Assert.Equal((2.0, true, new SyncWindow(5, 5)), (t.Entry("Forcejump number").JumpTime!.Value, t.Entry("Forcejump number").ForceJump, t.Entry("Forcejump number").Window));
        Assert.Equal(0, t.Entry("Label at zero").JumpTime); // a label at 0 means "stop"
        Assert.Equal(new Dictionary<string, double> { ["lbl"] = 6, ["zero"] = 0 }, t.Labels);
        Assert.Equal(6, t.Entries.Count); // labels are not entries
        Assert.Empty(t.Warnings);
    }

    [Fact]
    public void Jump_errors()
    {
        var t = Parse(
            "3.5 \"Both\" Ability { id: \"B\" } jump 1 forcejump 2",
            "3.7 \"Uppercase\" Ability { id: \"D\" } JUMP 50",
            "3.8 \"Missing label\" Ability { id: \"E\" } jump \"nope\"",
            "3.9 \"Missing force label\" Ability { id: \"F\" } forcejump \"nope\"",
            "33 \"No sync\" forcejump 2");
        AssertWarning(t, 1, "unexpected text \"forcejump 2\"");
        AssertWarning(t, 2, "unexpected text \"JUMP 50\"");
        AssertWarning(t, 5, "unexpected text \"forcejump 2\"");

        // Unknown labels: the sync stays, as a plain sync; a forcejump loses its forcejump.
        var missing = t.Entry("Missing label");
        Assert.Equal(("nope", (double?)null, false), (missing.JumpLabel, missing.JumpTime, missing.ForceJump));
        AssertWarning(t, 3, "jump to unknown label \"nope\"");
        var force = t.Entry("Missing force label");
        Assert.Equal(((double?)null, false), (force.JumpTime, force.ForceJump));
        Assert.NotNull(force.Sync);
        AssertWarning(t, 4, "forcejump to unknown label \"nope\"");
        Assert.Equal(2, t.Entries.Count);
    }

    [Fact]
    public void Labels_last_definition_wins_and_nothing_may_follow_them()
    {
        var t = Parse(
            "1 \"J\" Ability { id: \"1\" } jump \"lbl\"",
            "6 label \"lbl\"",
            "7 label \"lbl2\" # trailing comment",
            "8 label \"lbl\"",
            "9 label \"\"");
        Assert.Equal(new Dictionary<string, double> { ["lbl"] = 8, [""] = 9 }, t.Labels);
        Assert.Equal(8, t.Entry("J").JumpTime);
        AssertWarning(t, 3, "not a timeline statement");
        AssertWarning(t, 4, "duplicate label \"lbl\" (already at 6 on line 2); this definition (8) is used");
    }

    [Fact]
    public void Hideall_hides_exact_names_anywhere_in_the_file()
    {
        var t = Parse(
            "1 \"Foo\"",
            "2 \"foo\"",
            "3 \"--sync--\" Ability { id: \"1\" }",
            "hideall \"Foo\" # trailing comment",
            "hideall 'Single'",
            "hideall \"\"",
            "HIDEALL \"X\"",
            "4 \"Plain\"");
        Assert.Equal([true, false, true, false], t.Entries.Select(e => e.IsHidden));
        Assert.Equal(["Foo"], t.HiddenNames);
        AssertWarning(t, 5, "not a timeline statement");
        AssertWarning(t, 6, "not a timeline statement");
        AssertWarning(t, 7, "not a timeline statement");
    }

    [Fact]
    public void Sync_parameters_are_json5()
    {
        var t = Parse(
            "4.1 \"Single quotes\" Ability { id: '1', source: 'X' }",
            "4.2 \"Quoted keys\" Ability { \"id\": \"1\", 'source': \"X\" }",
            "4.3 \"Trailing comma\" Ability { id: \"1\", }",
            "4.4 \"Arrays\" Ability { id: [\"1\", \"2\",], source: [\"A\", \"B\"] }",
            "5.0 \"No spaces\" Ability{id:\"1\"}",
            "5.3 \"Type key\" Ability { type: \"22\", id: \"1\" }",
            "5.6 \"Comment\" Ability { /* any */ id: \"1\" }",
            "5.7 \"Repeated key\" Ability { id: \"1\", id: \"2\" }");
        Assert.Empty(t.Warnings);
        Assert.Equal(["X"], t.Entry("Single quotes").Sync!.Fields["source"].Values);
        Assert.Equal(["X"], t.Entry("Quoted keys").Sync!.Fields["source"].Values);
        Assert.Equal(["id"], t.Entry("Trailing comma").Sync!.Fields.Keys);
        Assert.Equal(["1", "2"], t.Entry("Arrays").Sync!.Fields["id"].Values);
        Assert.Equal(["A", "B"], t.Entry("Arrays").Sync!.Fields["source"].Values);
        Assert.Equal(["1"], t.Entry("No spaces").Sync!.Fields["id"].Values);
        Assert.Equal(["type", "id"], t.Entry("Type key").Sync!.Fields.Keys);
        Assert.Equal(["1"], t.Entry("Comment").Sync!.Fields["id"].Values);
        Assert.Equal(["2"], t.Entry("Repeated key").Sync!.Fields["id"].Values);
    }

    [Fact]
    public void Json5_escapes_need_two_backslashes_for_a_regex_escape()
    {
        var t = Parse(
            "4.7 \"Escapes\" Ability { id: \"1\", source: \"E\\\\.D\\\\.D\\\\.\" }",
            "4.8 \"Single backslash\" Ability { id: \"1\", source: \"E\\.D\" }",
            "4.9 \"Unicode\" Ability { id: \"\\u0031\", source: \"A\\x42\\tC\" }");
        var escaped = t.Entry("Escapes").Sync!.Fields["source"];
        Assert.Equal(["E\\.D\\.D\\."], escaped.Values);
        Assert.True(escaped.IsMatch("E.D.D."));
        Assert.False(escaped.IsMatch("EXDXDX"));
        var single = t.Entry("Single backslash").Sync!.Fields["source"];
        Assert.Equal(["E.D"], single.Values); // JSON5 drops the backslash: a regex "any character"
        Assert.True(single.IsMatch("EXD"));
        Assert.Equal(["1"], t.Entry("Unicode").Sync!.Fields["id"].Values);
        Assert.Equal(["AB\tC"], t.Entry("Unicode").Sync!.Fields["source"].Values);
    }

    [Fact]
    public void Invalid_parameters_keep_the_entry_without_a_sync()
    {
        var t = Parse(
            "4.5 \"Number value\" Ability { id: 1 }",
            "4.6 \"Bad key\" Ability { foo: \"1\" }",
            "5.1 \"Capture key\" Ability { id: \"1\", capture: \"x\" }",
            "5.2 \"Timestamp key\" Ability { timestamp: \"x\" }",
            "2 \"Bad JSON\" Ability { id: \"1\", }}",
            "6 \"Not a list of strings\" Ability { id: [\"1\", 2] }",
            "7 \"Bad regex\" Ability { id: \"7B0[45\" }",
            "8 \"Args after bad params\" Ability { id: 1 } window 5,5");
        Assert.Equal(
            ["Number value", "Bad key", "Capture key", "Timestamp key", "Bad JSON", "Not a list of strings", "Bad regex"],
            t.Entries.Select(e => e.Name));
        Assert.All(t.Entries, e => Assert.Null(e.Sync));
        AssertWarning(t, 1, "the value of \"id\" must be a string or an array of strings");
        AssertWarning(t, 2, "unknown field \"foo\"");
        AssertWarning(t, 3, "unknown field \"capture\"");
        AssertWarning(t, 4, "\"timestamp\" cannot be used");
        AssertWarning(t, 5, "invalid sync parameters { id: \"1\", }}");
        AssertWarning(t, 6, "must be a string or an array of strings");
        AssertWarning(t, 7, "not a valid regular expression");
        AssertWarning(t, 8, "unexpected text \"window 5,5\""); // like cactbot: the arguments are left over
    }

    [Fact]
    public void Types_are_case_sensitive_known_names_and_one_sync_per_entry()
    {
        var t = Parse(
            "1 \"Bad JSON\" Ability { id: \"1\"",
            "4.9 \"Unknown type\" Abilityy { id: \"1\" }",
            "35 \"lowercase type\" ability { id: \"1\" }",
            "36 \"two syncs\" Ability { id: \"1\" } StartsUsing { id: \"2\" }",
            "5.5 \"Extra junk\" Ability { id: \"1\" } foo",
            "99 \"ok\"");
        Assert.Equal("ok", Only(t).Name); // rejected entries leave no sync behind either (Q4)
        Assert.Equal(5, t.Warnings.Count);
        Assert.All(t.Warnings, w => Assert.Contains("unexpected text", w));
    }

    [Fact]
    public void Every_network_line_type_used_by_the_timelines_parses()
    {
        var t = Parse(
            "15 \"GameLog\" GameLog { code: \"0044\", line: \"Foo.*\" }",
            "16 \"InCombat\" InCombat { inGameCombat: \"1\" } window 0,1",
            "17 \"Repeating\" CombatantMemory { change: \"Add\", pair: [{ key: \"BNpcID\", value: \"1234\" }] }",
            "18 \"StartsUsing\" StartsUsing { id: \"7B40\" }",
            "19 \"HeadMarker\" HeadMarker { id: \"0103\" }",
            "20 \"ActorControl\" ActorControl { command: \"80000027\", data0: \"00\" }",
            "21 \"MapEffect\" MapEffect { flags: \"00020001\", location: \"0A\" }",
            "22 \"RemovedCombatant\" RemovedCombatant { name: \"Liquid Hand\" }",
            "23 \"WasDefeated\" WasDefeated { target: \"Ice Veil\" }",
            "24 \"SpawnNpcExtra\" SpawnNpcExtra { tetherId: \"0054\" }",
            "25 \"ActorControlExtra\" ActorControlExtra { category: \"019D\", param1: \"10\", param2: \"20\" }",
            "26 \"GainsEffect\" GainsEffect { effectId: [\"1061\", \"1063\"] }",
            "27 \"Cancel\" NetworkCancelAbility { id: \"63C3\", source: \"King Thordan\" }",
            "28 \"SystemLogMessage\" SystemLogMessage { id: \"7DE\" } window 0,100000 jump 0",
            "34 \"AbilityExtra\" AbilityExtra { id: \"1\" }",
            "35 \"Tether\" Tether { id: \"0054\" }",
            "36 \"LosesEffect\" LosesEffect { effectId: \"306\" }",
            "37 \"Aoe\" NetworkAOEAbility { id: \"1\" }");
        Assert.Empty(t.Warnings);
        Assert.Equal(
            ["GameLog", "InCombat", "CombatantMemory", "StartsUsing", "HeadMarker", "ActorControl", "MapEffect", "RemovedCombatant", "WasDefeated",
             "SpawnNpcExtra", "ActorControlExtra", "GainsEffect", "NetworkCancelAbility", "SystemLogMessage", "AbilityExtra", "Tether", "LosesEffect", "NetworkAOEAbility"],
            t.Entries.Select(e => e.Sync!.Type));
        Assert.Equal(["change", "pair.BNpcID"], t.Entry("Repeating").Sync!.Fields.Keys);
        Assert.Equal(0, t.Entry("SystemLogMessage").JumpTime);
    }

    [Fact]
    public void Old_style_regex_syncs_are_parsed_but_never_match()
    {
        var t = Parse(
            "3.9 \"Old sync\" sync / 15:........:Foo:1234:/ window 5,5 jump 10",
            "4.0 \"Old sync dur first\" duration 2 sync /abc/");
        var old = t.Entry("Old sync");
        Assert.Equal(SyncSpec.LegacyType, old.Sync!.Type);
        Assert.Equal(" 15:........:Foo:1234:", old.Sync.LegacyRegex);
        Assert.Equal((new SyncWindow(5, 5), 10.0), (old.Window, old.JumpTime!.Value));
        Assert.False(old.CanSync);
        Assert.False(old.Sync.Matches(new GenericSyncEvent(0, SyncSpec.LegacyType, new Dictionary<string, string>())));
        Assert.Equal(("abc", 2.0), (t.Entry("Old sync dur first").Sync!.LegacyRegex, t.Entry("Old sync dur first").Duration!.Value));
        AssertWarning(t, 1, "old-style \"sync /.../\"");
        AssertWarning(t, 2, "old-style \"sync /.../\"");
    }

    [Fact]
    public void Legacy_alert_commands_are_accepted_and_ignored()
    {
        var t = Parse(
            "alertall \"Foo\" before 1 sound \"x\"",
            "alertall \"Foo\" before 1 speak \"voice\" \"text\"",
            "infotext \"Foo\" before 2 \"Info\"",
            "alerttext \"Foo\" before 2",
            "alarmtext \"Foo\" before -1.5 \"Alarm\"",
            "define soundalert \"a\" \"b\"",
            "define speaker \"a\" \"b\" 1 2",
            "10 \"Foo\"");
        Assert.Equal("Foo", Only(t).Name);
        Assert.Equal(7, t.Warnings.Count);
        Assert.All(t.Warnings, w => Assert.Contains("legacy alert command", w));
    }

    [Fact]
    public void Header_hints_are_read_and_otherwise_ignored()
    {
        var t = Parse(
            "### Title",
            "# ZoneId: 1363",
            "# -p 49B0:11.4 4830:200 -ii 4978 4979",
            "#cactbot-timeline-lint-disable-sync-order",
            "1 \"A\"");
        Assert.Equal(1363, t.ZoneId);
        Assert.Equal([new PhaseAnchor("49B0", 11.4), new PhaseAnchor("4830", 200)], t.PhaseAnchors);
        Assert.Empty(t.Warnings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("# only a comment\nhideall \"--sync--\"\n10 label \"x\"")]
    [InlineData("this is not a timeline\nat all")]
    public void Text_without_entries_is_not_a_timeline(string text)
    {
        var ex = Assert.Throws<FormatException>(() => TimelineParser.Parse(text, "bad.txt"));
        Assert.Contains("bad.txt", ex.Message);
    }

    [Fact]
    public void Mangled_timelines_produce_warnings_never_exceptions()
    {
        const string Noise = "\"'{}[]()\\/#:,.|*+?^$ \t-0123456789abcdefABCDEFxjumpwindowdurationlabelforcejumpAbility";
        var rng = new Random(20261111);
        var sources = new[] { Top, Tea, Dmu, Dsr, Fru }.Select(Text).ToArray();
        for (var round = 0; round < 100; round++)
        {
            var lines = sources[round % sources.Length].Split('\n').ToList();
            for (var i = 0; i < lines.Count; i++)
            {
                if (rng.NextDouble() > 0.3 || lines[i].Length == 0)
                    continue;
                var chars = lines[i].ToCharArray().ToList();
                for (var k = rng.Next(1, 4); k > 0 && chars.Count > 0; k--)
                {
                    var at = rng.Next(chars.Count);
                    switch (rng.Next(3))
                    {
                        case 0: chars.RemoveAt(at); break;
                        case 1: chars.Insert(at, Noise[rng.Next(Noise.Length)]); break;
                        default: chars[at] = Noise[rng.Next(Noise.Length)]; break;
                    }
                }

                lines[i] = new string(chars.ToArray());
            }

            lines.Add("99999 \"guaranteed entry\"");
            lines.Add("1 \"Huge\" Ability { id: 0xFFFFFFFFFFFFFFFFFFFFFFFF }");
            var t = TimelineParser.Parse(string.Join("\n", lines), "fuzz");
            Assert.Contains(t.Entries, e => e.Name == "guaranteed entry");

            // ... and the result can be simulated and played.
            var sims = MitCue.Core.Simulation.FightSimulator.Simulate(t, null, new MitCue.Core.Simulation.SimulationOptions { Seed = round });
            Assert.All(sims.Skip(1), s => Assert.True(s.Entry!.Sync!.Matches(s.Event)));
            var player = new TimelinePlayer(t);
            foreach (var s in sims)
            {
                player.Tick(s.Event.Time);
                player.Process(s.Event);
            }
        }
    }

    [Fact]
    public void A_leading_byte_order_mark_is_whitespace()
    {
        var t = TimelineParser.Parse("\uFEFF### Title\n1 \"A\"");
        Assert.Empty(t.Warnings);
        Assert.Equal("A", Only(t).Name);
    }

    [Fact]
    public void Entries_keep_file_order_and_ByTime_sorts_stably()
    {
        var t = Parse("5 \"B\"", "1 \"A\"", "5 \"C\"", "3 \"D\"");
        Assert.Equal(["B", "A", "C", "D"], t.Entries.Select(e => e.Name));
        Assert.Equal([0, 1, 2, 3], t.Entries.Select(e => e.Index));
        Assert.Equal([1, 2, 3, 4], t.Entries.Select(e => e.Line));
        Assert.Equal(["A", "D", "B", "C"], t.ByTime.Select(e => e.Name));
        Assert.Equal(5, t.EndTime);
    }
}
