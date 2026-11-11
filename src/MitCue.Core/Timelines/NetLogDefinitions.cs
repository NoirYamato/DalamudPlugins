namespace MitCue.Core.Timelines;

/// <summary>
/// cactbot's network log line types (resources/netlog_defs.ts at cactbot e79ea7f): the type names a
/// timeline sync may use, their log line type ids and their field names. Used to validate sync
/// parameters the way cactbot does (an unknown field is an error).
/// </summary>
internal static class NetLogDefinitions
{
    /// <summary>The repeating field of CombatantMemory (an array of { key, value } objects).</summary>
    public const string RepeatingPairField = "pair";

    private static readonly Definition[] All =
    [
        Def("GameLog", "00", "code", "name", "line"),
        Def("ChangeZone", "01", "id", "name"),
        Def("ChangedPlayer", "02", "id", "name"),
        Def("AddedCombatant", "03", "id", "name", "job", "level", "ownerId", "worldId", "world", "npcNameId", "npcBaseId", "currentHp", "hp", "currentMp", "mp", "x", "y", "z", "heading"),
        Def("RemovedCombatant", "04", "id", "name", "job", "level", "owner", "world", "npcNameId", "npcBaseId", "currentHp", "hp", "currentMp", "mp", "x", "y", "z", "heading"),
        Def("PartyList", "11", "partyCount", "id0", "id1", "id2", "id3", "id4", "id5", "id6", "id7", "id8", "id9", "id10", "id11", "id12", "id13", "id14", "id15", "id16", "id17", "id18", "id19", "id20", "id21", "id22", "id23"),
        Def("PlayerStats", "12", "job", "strength", "dexterity", "vitality", "intelligence", "mind", "piety", "attackPower", "directHit", "criticalHit", "attackMagicPotency", "healMagicPotency", "determination", "skillSpeed", "spellSpeed", "tenacity", "localContentId"),
        Def("StartsUsing", "20", "sourceId", "source", "id", "ability", "targetId", "target", "castTime", "x", "y", "z", "heading"),
        Def("Ability", "21", "sourceId", "source", "id", "ability", "targetId", "target", "flags", "damage", "targetCurrentHp", "targetMaxHp", "targetCurrentMp", "targetMaxMp", "targetX", "targetY", "targetZ", "targetHeading", "currentHp", "maxHp", "currentMp", "maxMp", "x", "y", "z", "heading", "sequence", "targetIndex", "targetCount", "ownerId", "ownerName", "effectDisplayType", "actionId", "actionAnimationId", "animationLockTime", "rotationHex"),
        Def("NetworkAOEAbility", "22", "sourceId", "source", "id", "ability", "targetId", "target", "flags", "damage", "targetCurrentHp", "targetMaxHp", "targetCurrentMp", "targetMaxMp", "targetX", "targetY", "targetZ", "targetHeading", "currentHp", "maxHp", "currentMp", "maxMp", "x", "y", "z", "heading", "sequence", "targetIndex", "targetCount", "ownerId", "ownerName", "effectDisplayType", "actionId", "actionAnimationId", "animationLockTime", "rotationHex"),
        Def("NetworkCancelAbility", "23", "sourceId", "source", "id", "name", "reason"),
        Def("NetworkDoT", "24", "id", "name", "which", "effectId", "damage", "currentHp", "maxHp", "currentMp", "maxMp", "x", "y", "z", "heading", "sourceId", "source", "damageType", "sourceCurrentHp", "sourceMaxHp", "sourceCurrentMp", "sourceMaxMp", "sourceX", "sourceY", "sourceZ", "sourceHeading"),
        Def("WasDefeated", "25", "targetId", "target", "sourceId", "source"),
        Def("GainsEffect", "26", "effectId", "effect", "duration", "sourceId", "source", "targetId", "target", "count", "targetMaxHp", "sourceMaxHp"),
        Def("HeadMarker", "27", "targetId", "target", "id", "data0"),
        Def("NetworkRaidMarker", "28", "operation", "waymark", "id", "name", "x", "y", "z"),
        Def("NetworkTargetMarker", "29", "operation", "waymark", "id", "name", "targetId", "targetName"),
        Def("LosesEffect", "30", "effectId", "effect", "sourceId", "source", "targetId", "target", "count"),
        Def("NetworkGauge", "31", "id", "data0", "data1", "data2", "data3"),
        Def("NetworkWorld", "32"),
        Def("ActorControl", "33", "instance", "command", "data0", "data1", "data2", "data3"),
        Def("NameToggle", "34", "id", "name", "targetId", "targetName", "toggle"),
        Def("Tether", "35", "sourceId", "source", "targetId", "target", "id"),
        Def("LimitBreak", "36", "valueHex", "bars"),
        Def("NetworkEffectResult", "37", "id", "name", "sequenceId", "currentHp", "maxHp", "currentMp", "maxMp", "currentShield", "x", "y", "z", "heading"),
        Def("StatusEffect", "38", "targetId", "target", "jobLevelData", "hp", "maxHp", "mp", "maxMp", "currentShield", "x", "y", "z", "heading", "data0", "data1", "data2", "data3", "data4", "data5"),
        Def("NetworkUpdateHP", "39", "id", "name", "currentHp", "maxHp", "currentMp", "maxMp", "x", "y", "z", "heading"),
        Def("Map", "40", "id", "regionName", "placeName", "placeNameSub"),
        Def("SystemLogMessage", "41", "instance", "id", "param0", "param1", "param2"),
        Def("StatusList3", "42", "id", "name", "data0", "data1", "data2"),
        Def("StatusEffectListForay3", "43", "id", "name", "jobLevelData", "currentHp", "maxHp", "currentMp", "maxMp", "currentShield", "x", "y", "z", "heading", "data0", "data1", "data2"),
        Def("ParserInfo", "249"),
        Def("ProcessInfo", "250"),
        Def("Debug", "251"),
        Def("PacketDump", "252"),
        Def("Version", "253"),
        Def("Error", "254"),
        Def("None", "[0-9]+"),
        Def("LineRegistration", "256", "id", "source", "name", "version"),
        Def("MapEffect", "257", "instance", "flags", "location", "data0", "data1"),
        Def("FateDirector", "258", "category", "fateId", "progress"),
        Def("CEDirector", "259", "popTime", "timeRemaining", "ceKey", "numPlayers", "status", "progress"),
        Def("InCombat", "260", "inACTCombat", "inGameCombat", "isACTChanged", "isGameChanged"),
        Def("CombatantMemory", "261", "change", "id"),
        Def("RSVData", "262", "locale", "key", "value"),
        Def("StartsUsingExtra", "263", "sourceId", "id", "x", "y", "z", "heading"),
        Def("AbilityExtra", "264", "sourceId", "id", "globalEffectCounter", "dataFlag", "x", "y", "z", "heading", "animationTargetId"),
        Def("ContentFinderSettings", "265", "zoneId", "zoneName", "inContentFinderContent", "unrestrictedParty", "minimalItemLevel", "silenceEcho", "explorerMode", "levelSync"),
        Def("NpcYell", "266", "npcId", "npcNameId", "npcYellId"),
        Def("BattleTalk2", "267", "npcId", "instance", "npcNameId", "instanceContentTextId", "displayMs"),
        Def("Countdown", "268", "id", "worldId", "countdownTime", "result", "name"),
        Def("CountdownCancel", "269", "id", "worldId", "name"),
        Def("ActorMove", "270", "id", "heading", "moveType", "x", "y", "z"),
        Def("ActorSetPos", "271", "id", "heading", "x", "y", "z"),
        Def("SpawnNpcExtra", "272", "id", "parentId", "tetherId", "animationState"),
        Def("ActorControlExtra", "273", "id", "category", "param1", "param2", "param3", "param4"),
        Def("ActorControlSelfExtra", "274", "id", "category", "param1", "param2", "param3", "param4", "param5", "param6"),
    ];

    private static readonly Dictionary<string, Definition> ByName = All.ToDictionary(d => d.Name, StringComparer.Ordinal);

    /// <summary>Type names in cactbot's declaration order (the order of its sync regex alternation).</summary>
    public static IEnumerable<string> Names => All.Select(d => d.Name);

    public static bool TryGet(string name, out Definition definition) => ByName.TryGetValue(name, out definition!);

    private static Definition Def(string name, string lineType, params string[] fields) =>
        new(name, lineType, new HashSet<string>(fields.Prepend("timestamp").Prepend("type"), StringComparer.Ordinal));

    /// <summary>One log line type.</summary>
    /// <param name="Name">cactbot type name, e.g. "Ability".</param>
    /// <param name="LineType">Log line type id, e.g. "21".</param>
    /// <param name="Fields">Field names, including the pseudo-fields "type" and "timestamp".</param>
    public sealed record Definition(string Name, string LineType, IReadOnlySet<string> Fields)
    {
        public bool HasRepeatingPairs => this.Name == "CombatantMemory";
    }
}
