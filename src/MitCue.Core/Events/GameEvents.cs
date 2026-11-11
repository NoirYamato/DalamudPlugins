using System.Globalization;

namespace MitCue.Core.Events;

/// <summary>
/// Something observed in the game. <see cref="Time"/> is a monotonic clock in seconds
/// (the plugin uses a Stopwatch; tests and the simulator use synthetic values).
/// Events that can satisfy a cactbot timeline sync expose a <see cref="SyncType"/> (the
/// cactbot netregex type name, e.g. "Ability") and field values under cactbot field names.
/// </summary>
public abstract record GameEvent(double Time)
{
    /// <summary>cactbot netregex type this event corresponds to, or null if it cannot sync a timeline.</summary>
    public virtual string? SyncType => null;

    /// <summary>Value of a cactbot field (e.g. "id", "source") for sync matching, or null if unknown.</summary>
    public virtual string? GetField(string name) => null;

    /// <summary>Uppercase hex without prefix, at least 4 digits for ids (cactbot style, e.g. "7B03").</summary>
    public static string Hex(uint value, int minDigits = 4) => value.ToString("X" + minDigits, CultureInfo.InvariantCulture);
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
        _ => null,
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
