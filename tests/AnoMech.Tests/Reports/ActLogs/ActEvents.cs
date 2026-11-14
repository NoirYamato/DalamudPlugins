using System.Numerics;

namespace AnoMech.Tests;

// Typed views of the ACT lines the reports read. Field positions follow the FFXIV ACT plugin's network log; a line too
// short for its type reads as null. Positions are world X and Z, headings radians with 0 facing south.
internal readonly record struct ActCast(DateTimeOffset Time, uint Source, uint Action, uint Target, float Seconds)
{
    public static ActCast? From(ActLine line)
        => line.Type == ActLineType.StartsCasting && line.Hex(2) is { } source && line.Hex(4) is { } action
            ? new ActCast(line.Time, source, action, line.Hex(6) ?? ActorIds.None, line.Float(8) ?? 0f)
            : null;
}

// Where a cast is aimed: its location and rotation, logged right after the cast starts.
internal readonly record struct ActCastAt(DateTimeOffset Time, uint Source, uint Action, Vector2 Position, float Heading)
{
    public static ActCastAt? From(ActLine line)
        => line.Type == ActLineType.StartsCastingExtra && line.Hex(2) is { } source && line.Hex(3) is { } action
           && line.Point(4) is { } position && line.Float(7) is { } heading
            ? new ActCastAt(line.Time, source, action, position, heading)
            : null;
}

// One target of an ability; an ability that hits nobody logs one line with no target. Rotation is the direction the
// ability went off in, from the effect packet, where a newer plugin logs it. Sequence ties the hit to the effect result
// (37) that lands its damage a moment later. Damage is what the hit's damage effect carries, before shields.
internal readonly record struct ActHit(
    DateTimeOffset Time, uint Source, uint Action, uint Target, Vector2? TargetPosition, float? TargetHeading,
    float? Rotation, int TargetIndex, int TargetCount, uint? Sequence, int? Damage)
{
    private const int FieldsWithRotation = 55;
    private const int FirstEffect = 8, Effects = 8;

    public bool HitSomeone => Target != ActorIds.None && Target != 0;

    public static ActHit? From(ActLine line)
        => line.Type is ActLineType.Ability or ActLineType.AoeAbility && line.Hex(2) is { } source && line.Hex(4) is { } action
            ? new ActHit(line.Time, source, action, line.Hex(6) ?? ActorIds.None, line.Point(30), line.Float(33),
                         line.FieldCount >= FieldsWithRotation && line.Hex(line.FieldCount - 2) is { } raw ? Heading(raw) : null,
                         line.Int(45) ?? 0, line.Int(46) ?? 0, line.Hex(44), DamageOf(line))
            : null;

    // The packet's rotation spreads -pi..pi over 0..0xFFFF.
    public static float Heading(uint raw) => raw / 65535f * 2f * MathF.PI - MathF.PI;

    // The first of the line's eight effects that is damage (3), blocked (5) or parried (6): its value's upper two bytes,
    // and under the 0x40 flag of the third a fourth byte above them, which the second then carries added on.
    private static int? DamageOf(ActLine line)
    {
        for (var at = FirstEffect; at < FirstEffect + 2 * Effects; at += 2)
        {
            if (line.Hex(at) is not { } kind || line.Hex(at + 1) is not { } value || (kind & 0xFF) is not (3 or 5 or 6)) continue;
            var (first, second, flags, last) = ((value >> 24) & 0xFF, (value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
            return (flags & 0x40) != 0 ? (int)((last << 16) | (first << 8) | (second - last)) : (int)(value >> 16);
        }
        return null;
    }
}

internal readonly record struct ActStatus(DateTimeOffset Time, bool Gained, uint Status, float Duration, uint Source, uint Target)
{
    public static ActStatus? From(ActLine line)
        => line.Type is ActLineType.GainsEffect or ActLineType.LosesEffect && line.Hex(2) is { } status && line.Hex(7) is { } target
            ? new ActStatus(line.Time, line.Type == ActLineType.GainsEffect, status, line.Float(4) ?? 0f, line.Hex(5) ?? ActorIds.None, target)
            : null;
}

// An actor's hit points: from an effect result (37), which names the sequence of the hit it lands, a status list (38) or
// an update (39). A death shows here as 0 about 2 s before its death line.
internal readonly record struct ActHp(DateTimeOffset Time, uint Actor, int Hp, uint? Sequence)
{
    public static ActHp? From(ActLine line)
        => line.Hex(2) is not { } actor ? null
            : line.Type switch
            {
                ActLineType.EffectResult when line.Int(5) is { } hp => new ActHp(line.Time, actor, hp, line.Hex(4)),
                ActLineType.StatusList when line.Int(5) is { } hp => new ActHp(line.Time, actor, hp, null),
                ActLineType.UpdateHp when line.Int(4) is { } hp => new ActHp(line.Time, actor, hp, null),
                _ => null,
            };
}

internal readonly record struct ActDeath(DateTimeOffset Time, uint Target, uint Source)
{
    public static ActDeath? From(ActLine line)
        => line.Type == ActLineType.Death && line.Hex(2) is { } target
            ? new ActDeath(line.Time, target, line.Hex(4) ?? ActorIds.None)
            : null;
}

// The server placing an actor (271) or moving it (270).
internal readonly record struct ActMove(DateTimeOffset Time, uint Actor, Vector2 Position, float Heading, bool Placed)
{
    public static ActMove? From(ActLine line)
        => line.Type is ActLineType.ActorMove or ActLineType.ActorSetPosition && line.Hex(2) is { } actor
           && line.Point(6) is { } position && line.Float(3) is { } heading
            ? new ActMove(line.Time, actor, position, heading, line.Type == ActLineType.ActorSetPosition)
            : null;
}

internal readonly record struct ActTargetable(DateTimeOffset Time, uint Actor, bool Targetable)
{
    public static ActTargetable? From(ActLine line)
        => line.Type == ActLineType.NameToggle && line.Hex(2) is { } actor && line.Int(6) is { } shown
            ? new ActTargetable(line.Time, actor, shown != 0)
            : null;
}

// A combatant joining (03) or leaving (04). Job is the ClassJob row of a player, BNpcBase the row of an enemy.
internal readonly record struct ActCombatant(
    DateTimeOffset Time, bool Added, uint Actor, uint Job, uint BNpcBase, Vector2? Position, float? Heading)
{
    public static ActCombatant? From(ActLine line)
        => line.Type is ActLineType.AddCombatant or ActLineType.RemoveCombatant && line.Hex(2) is { } actor
            ? new ActCombatant(line.Time, line.Type == ActLineType.AddCombatant, actor, line.Hex(4) ?? 0,
                               (uint)(line.Int(10) ?? 0), line.Point(17), line.Float(20))
            : null;
}

// An action timeline the server plays on an actor (273 with control 0x197).
internal readonly record struct ActTimeline(DateTimeOffset Time, uint Actor, uint Timeline)
{
    private const uint PlayActionTimeline = 0x197;

    public static ActTimeline? From(ActLine line)
        => line.Type == ActLineType.ActorControlExtra && line.Hex(3) == PlayActionTimeline && line.Hex(2) is { } actor
           && line.Hex(4) is { } timeline
            ? new ActTimeline(line.Time, actor, timeline)
            : null;
}

// An object entering (Add) or leaving (Remove) the client's object table, from the 261 lines; the only trace an event
// object such as a void zone leaves. BaseId is its BNpcID key, the EObj row for an event object.
internal readonly record struct ActObject(DateTimeOffset Time, bool Added, uint Actor, uint? BaseId)
{
    public static ActObject? From(ActLine line)
    {
        if (line.Type != ActLineType.CombatantMemory || line.Hex(3) is not { } actor) return null;
        return line.Text(2) switch
        {
            "Add" => new ActObject(line.Time, true, actor, line.PairHex("BNpcID")),
            "Remove" => new ActObject(line.Time, false, actor, null),
            _ => null,
        };
    }
}

// A channel the server draws from one actor to another (35).
internal readonly record struct ActTether(DateTimeOffset Time, uint Source, uint Target, uint Id)
{
    public static ActTether? From(ActLine line)
        => line.Type == ActLineType.Tether && line.Hex(2) is { } source && line.Hex(4) is { } target && line.Hex(8) is { } id
            ? new ActTether(line.Time, source, target, id)
            : null;
}

// The model state the server sets on an actor (273 with control 0x3F).
internal readonly record struct ActModelState(DateTimeOffset Time, uint Actor, uint State)
{
    private const uint SetModelState = 0x3F;

    public static ActModelState? From(ActLine line)
        => line.Type == ActLineType.ActorControlExtra && line.Hex(3) == SetModelState && line.Hex(2) is { } actor && line.Hex(4) is { } state
            ? new ActModelState(line.Time, actor, state)
            : null;
}

// An actor's hit points beside their maximum, wherever a line carries both: joining (03), an effect result (37), a
// status list (38) or an update (39).
internal readonly record struct ActHealth(DateTimeOffset Time, uint Actor, int Hp, int MaxHp)
{
    public static ActHealth? From(ActLine line)
    {
        var at = line.Type switch
        {
            ActLineType.AddCombatant => 11,
            ActLineType.EffectResult or ActLineType.StatusList => 5,
            ActLineType.UpdateHp => 4,
            _ => -1,
        };
        return at > 0 && line.Hex(2) is { } actor && line.Int(at) is { } hp && line.Int(at + 1) is { } max
            ? new ActHealth(line.Time, actor, hp, max)
            : null;
    }
}

// Whom an enemy turns to: the NPCTargetID a 261 line carries when it changes. No target reads as ActorIds.None.
internal readonly record struct ActTargetChange(DateTimeOffset Time, uint Actor, uint Target)
{
    public static ActTargetChange? From(ActLine line)
        => line.Type == ActLineType.CombatantMemory && line.Text(2) is "Add" or "Change" && line.Hex(3) is { } actor
           && line.PairHex("NPCTargetID") is { } target
            ? new ActTargetChange(line.Time, actor, target)
            : null;
}

// A change to one of the arena's layout slots (257).
internal readonly record struct ActMapEffect(DateTimeOffset Time, uint Flags, uint Slot)
{
    public static ActMapEffect? From(ActLine line)
        => line.Type == ActLineType.MapEffect && line.Hex(3) is { } flags && line.Hex(4) is { } slot
            ? new ActMapEffect(line.Time, flags, slot)
            : null;
}

// A marker the server puts over an actor's head (27).
internal readonly record struct ActHeadMarker(DateTimeOffset Time, uint Target, uint Id)
{
    public static ActHeadMarker? From(ActLine line)
        => line.Type == ActLineType.HeadMarker && line.Hex(2) is { } target && line.Hex(6) is { } id
            ? new ActHeadMarker(line.Time, target, id)
            : null;
}

// Where an ability aimed at the ground went off (264 with its location flag set). An ability on its own caster carries
// the flag too, with a location of no meaning.
internal readonly record struct ActAbilityAt(DateTimeOffset Time, uint Source, uint Action, Vector2 Position, float Heading)
{
    public static ActAbilityAt? From(ActLine line)
        => line.Type == ActLineType.AbilityExtra && line.Hex(2) is { } source && line.Hex(3) is { } action && line.Int(5) == 1
           && line.Point(6) is { } position && line.Float(9) is { } heading
            ? new ActAbilityAt(line.Time, source, action, position, heading)
            : null;
}
