using MitCue.Core.Events;
using MitCue.Core.Timelines;
using static MitCue.Core.Tests.Timelines.TimelineKit;

namespace MitCue.Core.Tests.Timelines;

/// <summary>Field values are whole-field, case-insensitive regexes; ids also compare numerically.</summary>
public class SyncMatchingTests
{
    private static SyncSpec Sync(string line) => Parse(line).Entries[0].Sync!;

    [Theory]
    [InlineData("7B03", "7B03", true)]
    [InlineData("7B03", "7b03", true)]
    [InlineData("7B0", "7B03", false)] // whole field, not a substring
    [InlineData("B03", "7B03", false)]
    [InlineData("7B0[45]", "7B04", true)]
    [InlineData("7B0[45]", "7B06", false)]
    [InlineData("Omega", "OMEGA", true)]
    [InlineData("Omega", "Omega-M", false)]
    [InlineData("Omega.*", "Omega-M", true)]
    [InlineData("1|2", "2", true)] // wrapped in (?:...), unlike cactbot (Q3)
    [InlineData("1|2", "12", false)]
    [InlineData("left arm unit", "Left Arm Unit", true)]
    public void Single_value(string value, string field, bool expected) =>
        Assert.Equal(expected, FieldMatcher.Literal(value).IsMatch(field));

    [Theory]
    [InlineData("0103", "103", true)] // HeadMarker ids, padding differs
    [InlineData("00", "0", true)] // DirectorUpdate data0
    [InlineData("0D", "D", true)]
    [InlineData("7B03", "07B03", true)]
    [InlineData("306", "0306", true)]
    [InlineData("0103", "0104", false)]
    [InlineData("0A", "A0", false)]
    public void Plain_hex_values_compare_numerically(string value, string field, bool expected) =>
        Assert.Equal(expected, FieldMatcher.Literal(value).IsMatch(field));

    [Fact]
    public void Arrays_match_any_value()
    {
        var m = FieldMatcher.AnyOf(["7B2A", "7B2D"]);
        Assert.True(m.IsMatch("7b2a"));
        Assert.True(m.IsMatch("7B2D"));
        Assert.False(m.IsMatch("7B2B"));
        Assert.False(m.IsMatch(null));
        Assert.False(m.IsRegex);
        Assert.Equal(["7B2A", "7B2D"], m.Literals);
        Assert.Equal(["7B2A", "7B2D"], m.Values);
        Assert.Equal("[\"7B2A\", \"7B2D\"]", m.Display);

        var mixed = FieldMatcher.AnyOf(["482[BC]", "4830"]);
        Assert.True(mixed.IsRegex);
        Assert.Null(mixed.Literals);
        Assert.True(mixed.IsMatch("482C"));
        Assert.True(mixed.IsMatch("04830"));
        Assert.False(mixed.IsMatch("482D"));
    }

    [Fact]
    public void Pattern_is_an_anchored_case_insensitive_regex()
    {
        var m = FieldMatcher.Pattern("Omega(-[MF])?");
        Assert.True(m.IsRegex);
        Assert.True(m.IsMatch("omega-f"));
        Assert.True(m.IsMatch("Omega"));
        Assert.False(m.IsMatch("Alpha Omega"));
        Assert.Equal(["Omega(-[MF])?"], m.Values);
    }

    [Fact]
    public void Sync_type_must_match_and_every_field_must_match()
    {
        var sync = Sync("15 \"X\" Ability { id: \"7B03\", source: \"Omega\" }");
        Assert.True(sync.Matches(Ability(0, 0x7B03, "Omega")));
        Assert.False(sync.Matches(Ability(0, 0x7B03, "Omega-M")));
        Assert.False(sync.Matches(Ability(0, 0x7B04, "Omega")));
        Assert.False(sync.Matches(Cast(0, 0x7B03, "Omega"))); // StartsUsing is another type
        Assert.False(sync.Matches(new AbilityResolved(0, 0x7B03, PlayerId, "Omega", true, [PlayerId]))); // players never sync
        Assert.False(sync.Matches(new DutyReset(0, DutyResetKind.Wipe)));
    }

    [Fact]
    public void A_field_the_event_does_not_have_never_matches()
    {
        var sync = Sync("15 \"X\" Ability { id: \"7B03\", target: \"Somebody\" }");
        Assert.False(sync.Matches(Ability(0, 0x7B03)));
    }

    [Fact]
    public void Ability_covers_lines_21_and_22_and_NetworkAOEAbility_only_22()
    {
        var single = Ability(0, 0x10);
        var aoe = new AbilityResolved(0, 0x10, BossId, "Boss", false, [PlayerId, PlayerId + 1]);
        var ability = Sync("1 \"A\" Ability { id: \"10\" }");
        var aoeOnly = Sync("1 \"A\" NetworkAOEAbility { id: \"10\" }");
        var typed21 = Sync("1 \"A\" Ability { type: \"21\", id: \"10\" }");
        Assert.True(ability.Matches(single));
        Assert.True(ability.Matches(aoe));
        Assert.False(aoeOnly.Matches(single));
        Assert.True(aoeOnly.Matches(aoe));
        Assert.True(typed21.Matches(single));
        Assert.False(typed21.Matches(aoe));
    }

    [Fact]
    public void MatchNames_false_ignores_name_fields_but_not_ids()
    {
        var noNames = new SyncMatchOptions { MatchNames = false };
        var sync = Sync("15 \"X\" Ability { id: \"7B03\", source: \"Omega\" }");
        Assert.True(sync.Matches(Ability(0, 0x7B03, "オメガ"), noNames));
        Assert.False(sync.Matches(Ability(0, 0x7B03, "オメガ")));
        Assert.False(sync.Matches(Ability(0, 0x7B04, "Omega"), noNames));

        // A sync with nothing but a name cannot be evaluated without names: it never matches.
        var nameOnly = Sync("196 \"X\" RemovedCombatant { name: \"Liquid Hand\" }");
        Assert.True(nameOnly.Matches(new CombatantRemoved(0, 1, "Liquid Hand")));
        Assert.False(nameOnly.Matches(new CombatantRemoved(0, 1, "Liquid Hand"), noNames));

        // Without any fields the sync matches every event of its type either way.
        var any = Sync("1 \"X\" Ability {}");
        Assert.True(any.Matches(Ability(0, 1), noNames));
    }

    [Fact]
    public void Event_fields_use_cactbot_formats()
    {
        var director = new DirectorCommand(0, 0x80000027, 0x2E);
        Assert.Equal(("80000027", "2E", "33"), (director.GetField("command"), director.GetField("data0"), director.GetField("type")));
        Assert.True(Sync("1 \"X\" ActorControl { command: \"80000027\", data0: \"2E\" }").Matches(director));
        Assert.True(Sync("1 \"X\" ActorControl { command: \"80000027\", data0: \"00\" }").Matches(new DirectorCommand(0, 0x80000027)));

        var marker = new HeadMarkerShown(0, PlayerId, "Player", 0x103);
        Assert.Equal("0103", marker.GetField("id"));
        Assert.True(Sync("1 \"X\" HeadMarker { id: '0103' }").Matches(marker));

        var spawn = new NpcSpawnExtra(0, 0x40000001, 0, 0x54);
        Assert.Equal(("40000001", "0054", "00"), (spawn.GetField("id"), spawn.GetField("tetherId"), spawn.GetField("animationState")));
        Assert.True(Sync("1 \"X\" SpawnNpcExtra { tetherId: \"0054\" }").Matches(spawn));

        var tower = new ActorControlExtraCommand(0, 0x40000001, 0x19D, 0x10, 0x20);
        Assert.Equal(("019D", "10", "20", "0"), (tower.GetField("category"), tower.GetField("param1"), tower.GetField("param2"), tower.GetField("param3")));
        Assert.True(Sync("1 \"X\" ActorControlExtra { category: \"019D\", param1: \"10\", param2: \"20\" }").Matches(tower));

        var gains = new StatusGained(0, 0x306, PlayerId, "Player");
        Assert.Equal("306", gains.GetField("effectId"));
        Assert.True(Sync("1 \"X\" GainsEffect { effectId: '306' }").Matches(gains));
        Assert.True(Sync("1 \"X\" LosesEffect { effectId: ['1061', '1063'] }").Matches(new StatusLost(0, 0x1063, PlayerId, "Player")));

        var map = new MapEffectOccurred(0, 0x0A, 0x00020001);
        Assert.True(Sync("1 \"X\" MapEffect { flags: \"00020001\", location: \"0A\" }").Matches(map));

        Assert.True(Sync("1 \"X\" InCombat { inGameCombat: \"1\" }").Matches(Combat(0)));
        Assert.False(Sync("1 \"X\" InCombat { inGameCombat: \"1\" }").Matches(Combat(0, false)));
        Assert.True(Sync("1 \"X\" WasDefeated { target: 'Ice Veil' }").Matches(new CombatantDefeated(0, 1, "Ice Veil")));
        Assert.True(Sync("1 \"X\" NetworkCancelAbility { id: \"63C3\", source: \"King Thordan\" }").Matches(new CastCancelled(0, 0x63C3, 1, "King Thordan")));
        Assert.True(Sync("1 \"X\" AddedCombatant { name: \"Omega\", npcNameId: \"7635\" }").Matches(new CombatantAdded(0, 1, "Omega", 7635)));
        Assert.True(Sync("1 \"X\" Tether { id: \"0054\", source: \"Black Hole\" }").Matches(new TetherShown(0, 1, "Black Hole", PlayerId, "Player", 0x54)));
        Assert.True(Sync("1 \"X\" GameLog { code: \"0044\", line: \"Foo.*\" }").Matches(
            new GenericSyncEvent(0, "GameLog", new Dictionary<string, string> { ["code"] = "0044", ["line"] = "Foo bar" })));
    }
}
