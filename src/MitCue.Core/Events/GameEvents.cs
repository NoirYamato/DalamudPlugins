using System.Globalization;

namespace MitCue.Core.Events;

/// <summary>
/// Something observed in the game. <see cref="Time"/> is a monotonic clock in seconds
/// (the plugin uses a Stopwatch; tests and the simulator use synthetic values).
/// Events that can satisfy a cactbot timeline sync expose a <see cref="SyncType"/> (the
/// cactbot netregex type name, e.g. "Ability") and field values under cactbot field names.
/// </summary>
/// <remarks>
/// Field values use the formats of cactbot's network log lines (docs/research/cactbot-timelines.md
/// §2.4.3): ids are uppercase hex without prefix. Timeline values that are plain hex numbers are also
/// compared numerically, so the exact zero padding does not matter. The pseudo-field "type" returns
/// the log line type (e.g. "21" for a single-target ability, "22" for an AoE one).
/// </remarks>
public abstract record GameEvent(double Time)
{
    /// <summary>cactbot netregex type this event corresponds to, or null if it cannot sync a timeline.</summary>
    public virtual string? SyncType => null;

    /// <summary>Value of a cactbot field (e.g. "id", "source") for sync matching, or null if unknown.</summary>
    public virtual string? GetField(string name) => null;

    /// <summary>
    /// True when a timeline sync of cactbot type <paramref name="type"/> can match this event. By default
    /// the type must equal <see cref="SyncType"/>; an AoE ability also answers to "NetworkAOEAbility".
    /// </summary>
    public virtual bool MatchesSyncType(string type) =>
        this.SyncType != null && string.Equals(this.SyncType, type, StringComparison.Ordinal);

    /// <summary>Uppercase hex without prefix, at least 4 digits for ids (cactbot style, e.g. "7B03").</summary>
    public static string Hex(uint value, int minDigits = 4) => value.ToString("X" + minDigits, CultureInfo.InvariantCulture);

    /// <summary>Decimal formatting used for counts and BNpc ids.</summary>
    private protected static string Dec(uint value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Seconds with two decimals (cactbot's cast times and durations).</summary>
    private protected static string Seconds(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>The game's combat flag for the local player changed (cactbot "InCombat", line 260).</summary>
public sealed record CombatStateChanged(double Time, bool InCombat) : GameEvent(Time)
{
    public override string? SyncType => "InCombat";

    public override string? GetField(string name) => name switch
    {
        "inGameCombat" => InCombat ? "1" : "0",
        "inACTCombat" => InCombat ? "1" : "0",
        "isGameChanged" => "1",
        "isACTChanged" => "1",
        "type" => "260",
        _ => null,
    };
}

/// <summary>A non-player actor started casting (cactbot "StartsUsing", line 20). Time is the cast start.</summary>
public sealed record CastStarted(
    double Time,
    uint ActionId,
    uint SourceId,
    string SourceName,
    float CastTime,
    uint TargetId,
    string? TargetName = null) : GameEvent(Time)
{
    public override string? SyncType => "StartsUsing";

    public override string? GetField(string name) => name switch
    {
        "id" => Hex(ActionId),
        "source" => SourceName,
        "sourceId" => Hex(SourceId, 8),
        "target" => TargetName,
        "targetId" => Hex(TargetId, 8),
        "castTime" => CastTime.ToString("0.00", CultureInfo.InvariantCulture),
        "type" => "20",
        _ => null,
    };
}

/// <summary>
/// An action resolved (cactbot "Ability", lines 21/22), observed once per ActionEffect
/// regardless of target count. Also used for the local player's own action uses.
/// </summary>
public sealed record AbilityResolved(
    double Time,
    uint ActionId,
    uint SourceId,
    string SourceName,
    bool SourceIsPlayer,
    IReadOnlyList<uint> TargetIds,
    bool SourceIsLocalPlayer = false) : GameEvent(Time)
{
    public override string? SyncType => SourceIsPlayer ? null : "Ability";

    public override string? GetField(string name) => name switch
    {
        "id" => Hex(ActionId),
        "source" => SourceName,
        "sourceId" => Hex(SourceId, 8),
        "targetId" => TargetIds.Count > 0 ? Hex(TargetIds[0], 8) : null,
        "type" => TargetIds.Count > 1 ? "22" : "21",
        _ => null,
    };

    /// <summary>"Ability" covers lines 21 and 22; "NetworkAOEAbility" only line 22 (more than one target).</summary>
    public override bool MatchesSyncType(string type) => !SourceIsPlayer && type switch
    {
        "Ability" => true,
        "NetworkAOEAbility" => TargetIds.Count > 1,
        _ => false,
    };
}

/// <summary>A map effect fired (cactbot "MapEffect", line 257). Optional; only some fights need it.</summary>
public sealed record MapEffectOccurred(double Time, uint Location, uint Flags) : GameEvent(Time)
{
    public override string? SyncType => "MapEffect";

    public override string? GetField(string name) => name switch
    {
        "location" => Hex(Location, 2),
        "flags" => Hex(Flags, 8),
        "type" => "257",
        _ => null,
    };
}

/// <summary>A director/actor-control command (cactbot "ActorControl", line 33), e.g. 4000000F on wipe fade-out.</summary>
public sealed record DirectorCommand(double Time, uint Command, uint Data0 = 0, uint Data1 = 0, uint Data2 = 0, uint Data3 = 0) : GameEvent(Time)
{
    public override string? SyncType => "ActorControl";

    public override string? GetField(string name) => name switch
    {
        "command" => Hex(Command, 8),
        "data0" => Hex(Data0, 1),
        "data1" => Hex(Data1, 1),
        "data2" => Hex(Data2, 1),
        "data3" => Hex(Data3, 1),
        "type" => "33",
        _ => null,
    };
}

public enum DutyResetKind
{
    Wipe,
    Recommence,
    Completed,
    LeftZone,
}

/// <summary>The encounter reset (from Dalamud's duty state, territory changes, or manual reset).</summary>
public sealed record DutyReset(double Time, DutyResetKind Kind) : GameEvent(Time);

/// <summary>A combatant appeared (cactbot "AddedCombatant", line 03).</summary>
/// <param name="Time">Clock of the event.</param>
/// <param name="Id">Entity id.</param>
/// <param name="Name">Display name (English for the bundled timelines).</param>
/// <param name="NpcNameId">BNpcName row id (decimal in the log), 0 if unknown.</param>
/// <param name="NpcBaseId">BNpcBase row id (decimal in the log), 0 if unknown.</param>
public sealed record CombatantAdded(double Time, uint Id, string Name, uint NpcNameId = 0, uint NpcBaseId = 0) : GameEvent(Time)
{
    public override string? SyncType => "AddedCombatant";

    public override string? GetField(string name) => name switch
    {
        "id" => Hex(Id, 8),
        "name" => Name,
        "npcNameId" => Dec(NpcNameId),
        "npcBaseId" => Dec(NpcBaseId),
        "type" => "03",
        _ => null,
    };
}

/// <summary>A combatant left the scene: removed, died, out of range (cactbot "RemovedCombatant", line 04).</summary>
public sealed record CombatantRemoved(double Time, uint Id, string Name, uint NpcNameId = 0, uint NpcBaseId = 0) : GameEvent(Time)
{
    public override string? SyncType => "RemovedCombatant";

    public override string? GetField(string name) => name switch
    {
        "id" => Hex(Id, 8),
        "name" => Name,
        "npcNameId" => Dec(NpcNameId),
        "npcBaseId" => Dec(NpcBaseId),
        "type" => "04",
        _ => null,
    };
}

/// <summary>An actor was defeated (cactbot "WasDefeated", line 25).</summary>
public sealed record CombatantDefeated(double Time, uint TargetId, string TargetName, uint SourceId = 0, string SourceName = "") : GameEvent(Time)
{
    public override string? SyncType => "WasDefeated";

    public override string? GetField(string name) => name switch
    {
        "targetId" => Hex(TargetId, 8),
        "target" => TargetName,
        "sourceId" => Hex(SourceId, 8),
        "source" => SourceName,
        "type" => "25",
        _ => null,
    };
}

/// <summary>A cast was cancelled or interrupted (cactbot "NetworkCancelAbility", line 23).</summary>
public sealed record CastCancelled(double Time, uint ActionId, uint SourceId, string SourceName, string Reason = "Cancelled") : GameEvent(Time)
{
    public override string? SyncType => "NetworkCancelAbility";

    public override string? GetField(string name) => name switch
    {
        "id" => Hex(ActionId),
        "sourceId" => Hex(SourceId, 8),
        "source" => SourceName,
        "reason" => Reason,
        "type" => "23",
        _ => null,
    };
}

/// <summary>A head marker appeared over an actor (cactbot "HeadMarker", line 27; marker id as 4 hex digits).</summary>
public sealed record HeadMarkerShown(double Time, uint TargetId, string TargetName, uint MarkerId) : GameEvent(Time)
{
    public override string? SyncType => "HeadMarker";

    public override string? GetField(string name) => name switch
    {
        "id" => Hex(MarkerId, 4),
        "targetId" => Hex(TargetId, 8),
        "target" => TargetName,
        "type" => "27",
        _ => null,
    };
}

/// <summary>A tether appeared between two actors (cactbot "Tether", line 35; tether id as 4 hex digits).</summary>
public sealed record TetherShown(double Time, uint SourceId, string SourceName, uint TargetId, string TargetName, uint TetherId) : GameEvent(Time)
{
    public override string? SyncType => "Tether";

    public override string? GetField(string name) => name switch
    {
        "id" => Hex(TetherId, 4),
        "sourceId" => Hex(SourceId, 8),
        "source" => SourceName,
        "targetId" => Hex(TargetId, 8),
        "target" => TargetName,
        "type" => "35",
        _ => null,
    };
}

/// <summary>A status effect was applied or refreshed (cactbot "GainsEffect", line 26).</summary>
public sealed record StatusGained(
    double Time,
    uint EffectId,
    uint TargetId,
    string TargetName,
    uint SourceId = 0,
    string SourceName = "",
    float Duration = 0,
    uint Count = 0,
    string? EffectName = null) : GameEvent(Time)
{
    public override string? SyncType => "GainsEffect";

    public override string? GetField(string name) => name switch
    {
        "effectId" => Hex(EffectId, 2),
        "effect" => EffectName,
        "duration" => Seconds(Duration),
        "sourceId" => Hex(SourceId, 8),
        "source" => SourceName,
        "targetId" => Hex(TargetId, 8),
        "target" => TargetName,
        "count" => Hex(Count, 2),
        "type" => "26",
        _ => null,
    };
}

/// <summary>A status effect was removed (cactbot "LosesEffect", line 30).</summary>
public sealed record StatusLost(
    double Time,
    uint EffectId,
    uint TargetId,
    string TargetName,
    uint SourceId = 0,
    string SourceName = "",
    uint Count = 0,
    string? EffectName = null) : GameEvent(Time)
{
    public override string? SyncType => "LosesEffect";

    public override string? GetField(string name) => name switch
    {
        "effectId" => Hex(EffectId, 2),
        "effect" => EffectName,
        "sourceId" => Hex(SourceId, 8),
        "source" => SourceName,
        "targetId" => Hex(TargetId, 8),
        "target" => TargetName,
        "count" => Hex(Count, 2),
        "type" => "30",
        _ => null,
    };
}

/// <summary>
/// Extra data of an NPC spawn packet (OverlayPlugin "SpawnNpcExtra", line 272): id and parent id as 8 hex
/// digits, tether id as 4, animation state as 2.
/// </summary>
public sealed record NpcSpawnExtra(double Time, uint Id, uint ParentId, ushort TetherId, byte AnimationState = 0) : GameEvent(Time)
{
    public override string? SyncType => "SpawnNpcExtra";

    public override string? GetField(string name) => name switch
    {
        "id" => Hex(Id, 8),
        "parentId" => Hex(ParentId, 8),
        "tetherId" => Hex(TetherId, 4),
        "animationState" => Hex(AnimationState, 2),
        "type" => "272",
        _ => null,
    };
}

/// <summary>
/// An actor-control packet of one of OverlayPlugin's extra categories (OverlayPlugin "ActorControlExtra",
/// line 273), e.g. category 0x19D EObjAnimation or 0x197 PlayActionTimeline: id as 8 hex digits, category
/// as 4, params as unpadded hex.
/// </summary>
public sealed record ActorControlExtraCommand(double Time, uint Id, ushort Category, uint Param1 = 0, uint Param2 = 0, uint Param3 = 0, uint Param4 = 0) : GameEvent(Time)
{
    public override string? SyncType => "ActorControlExtra";

    public override string? GetField(string name) => name switch
    {
        "id" => Hex(Id, 8),
        "category" => Hex(Category, 4),
        "param1" => Hex(Param1, 1),
        "param2" => Hex(Param2, 1),
        "param3" => Hex(Param3, 1),
        "param4" => Hex(Param4, 1),
        "type" => "273",
        _ => null,
    };
}

/// <summary>
/// Any other cactbot log line type given as raw field values (e.g. replayed logs, or simulated
/// events for sync types without a dedicated record). Field names are cactbot's, case-sensitive.
/// </summary>
public sealed record GenericSyncEvent(double Time, string Type, IReadOnlyDictionary<string, string> Fields) : GameEvent(Time)
{
    public override string? SyncType => Type;

    public override string? GetField(string name) => Fields.TryGetValue(name, out var value) ? value : null;
}
