using System.Globalization;
using System.Text;
using MitCue.Core.Events;
using MitCue.Core.Timelines;

namespace MitCue.Core.Simulation;

/// <summary>Builds the game event that satisfies a timeline entry's sync (for <see cref="FightSimulator"/>).</summary>
internal static class SimulatedEvents
{
    /// <summary>Entity id used as the target of simulated boss actions (a player).</summary>
    public const uint PlayerId = 0x10000001;

    private const string PlayerName = "Player";

    /// <summary>
    /// An event of the sync's type at <paramref name="clock"/> whose fields match every field of the
    /// sync, or null when no matching value can be produced. Uses the dedicated event record for the
    /// type when there is one, else a <see cref="GenericSyncEvent"/>.
    /// </summary>
    public static GameEvent? Create(TimelineEntry entry, double clock, Random rng, string defaultSource, float castTime)
    {
        var sync = entry.Sync;
        if (sync == null || sync.LegacyRegex != null)
            return null;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (field, matcher) in sync.Fields)
        {
            var value = Example(matcher, rng);
            if (value == null)
                return null;
            values[field] = value;
        }

        var typed = Typed(sync.Type, values, clock, defaultSource, castTime);
        if (typed != null && sync.Matches(typed))
            return typed;

        var generic = new GenericSyncEvent(clock, sync.Type, values);
        return sync.Matches(generic) ? generic : null;
    }

    /// <summary>A value the matcher accepts: one of its values, with any regex syntax resolved.</summary>
    public static string? Example(FieldMatcher matcher, Random rng)
    {
        var count = matcher.Values.Count;
        if (count == 0)
            return string.Empty;
        var first = rng.Next(count);
        for (var i = 0; i < count; i++)
        {
            var source = matcher.Values[(first + i) % count];
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var candidate = FieldMatcher.HasRegexSyntax(source) ? RegexExample.Generate(source, rng) : source;
                if (candidate != null && matcher.IsMatch(candidate))
                    return candidate;
            }
        }

        return null;
    }

    /// <summary>A stable fake entity id for an actor name.</summary>
    public static uint IdFor(string name)
    {
        var hash = 2166136261u;
        foreach (var c in name.ToUpperInvariant())
            hash = (hash ^ c) * 16777619u;
        return 0x40000000u | (hash & 0x00FFFFFFu);
    }

    private static GameEvent? Typed(string type, Dictionary<string, string> v, double clock, string defaultSource, float castTime)
    {
        var source = v.GetValueOrDefault("source") ?? defaultSource;
        var target = v.GetValueOrDefault("target") ?? PlayerName;
        try
        {
            switch (type)
            {
                case "Ability":
                case "NetworkAOEAbility":
                    var targets = new List<uint> { Hex(v, "targetId", PlayerId) };
                    if (type == "NetworkAOEAbility" || v.GetValueOrDefault("type") == "22")
                        targets.Add(PlayerId + 1);
                    return new AbilityResolved(clock, Hex(v, "id", 0x07), Hex(v, "sourceId", IdFor(source)), source, false, targets);
                case "StartsUsing":
                    return new CastStarted(clock, Hex(v, "id", 0x07), Hex(v, "sourceId", IdFor(source)), source, castTime, Hex(v, "targetId", PlayerId), target);
                case "InCombat":
                    return new CombatStateChanged(clock, v.GetValueOrDefault("inGameCombat") != "0");
                case "ActorControl":
                    return new DirectorCommand(clock, Hex(v, "command", 0), Hex(v, "data0", 0), Hex(v, "data1", 0), Hex(v, "data2", 0), Hex(v, "data3", 0));
                case "MapEffect":
                    return new MapEffectOccurred(clock, Hex(v, "location", 0), Hex(v, "flags", 0));
                case "AddedCombatant":
                    {
                        var name = v.GetValueOrDefault("name") ?? defaultSource;
                        return new CombatantAdded(clock, Hex(v, "id", IdFor(name)), name, Dec(v, "npcNameId"), Dec(v, "npcBaseId"));
                    }

                case "RemovedCombatant":
                    {
                        var name = v.GetValueOrDefault("name") ?? defaultSource;
                        return new CombatantRemoved(clock, Hex(v, "id", IdFor(name)), name, Dec(v, "npcNameId"), Dec(v, "npcBaseId"));
                    }

                case "WasDefeated":
                    {
                        var victim = v.GetValueOrDefault("target") ?? defaultSource;
                        return new CombatantDefeated(clock, Hex(v, "targetId", IdFor(victim)), victim, Hex(v, "sourceId", PlayerId), v.GetValueOrDefault("source") ?? PlayerName);
                    }

                case "NetworkCancelAbility":
                    return new CastCancelled(clock, Hex(v, "id", 0x07), Hex(v, "sourceId", IdFor(source)), source, v.GetValueOrDefault("reason") ?? "Cancelled");
                case "HeadMarker":
                    return new HeadMarkerShown(clock, Hex(v, "targetId", PlayerId), target, Hex(v, "id", 0));
                case "Tether":
                    return new TetherShown(clock, Hex(v, "sourceId", IdFor(source)), source, Hex(v, "targetId", PlayerId), target, Hex(v, "id", 0));
                case "GainsEffect":
                    return new StatusGained(clock, Hex(v, "effectId", 0), Hex(v, "targetId", PlayerId), target, Hex(v, "sourceId", IdFor(source)), source, Seconds(v, "duration"), Hex(v, "count", 0), v.GetValueOrDefault("effect"));
                case "LosesEffect":
                    return new StatusLost(clock, Hex(v, "effectId", 0), Hex(v, "targetId", PlayerId), target, Hex(v, "sourceId", IdFor(source)), source, Hex(v, "count", 0), v.GetValueOrDefault("effect"));
                case "SpawnNpcExtra":
                    return new NpcSpawnExtra(clock, Hex(v, "id", IdFor(defaultSource)), Hex(v, "parentId", 0), checked((ushort)Hex(v, "tetherId", 0)), checked((byte)Hex(v, "animationState", 0)));
                case "ActorControlExtra":
                    return new ActorControlExtraCommand(clock, Hex(v, "id", IdFor(defaultSource)), checked((ushort)Hex(v, "category", 0)), Hex(v, "param1", 0), Hex(v, "param2", 0), Hex(v, "param3", 0), Hex(v, "param4", 0));
                default:
                    return null;
            }
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return null; // a value the record cannot hold: use a generic event
        }
    }

    private static uint Hex(Dictionary<string, string> v, string key, uint fallback)
    {
        if (!v.TryGetValue(key, out var s))
            return fallback;
        return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException($"{key} is not hex");
    }

    private static uint Dec(Dictionary<string, string> v, string key)
    {
        if (!v.TryGetValue(key, out var s))
            return 0;
        return uint.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException($"{key} is not a number");
    }

    private static float Seconds(Dictionary<string, string> v, string key)
    {
        if (!v.TryGetValue(key, out var s))
            return 0;
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException($"{key} is not a number");
    }
}

/// <summary>
/// Produces a string matching a simple regex: literals, escapes, <c>.</c>, character classes, groups,
/// alternation and quantifiers (the minimum count is used). Returns null for unsupported syntax such
/// as lookarounds or backreferences. The caller verifies the result.
/// </summary>
internal sealed class RegexExample
{
    private readonly string pattern;
    private readonly Random rng;
    private int pos;

    private RegexExample(string pattern, Random rng)
    {
        this.pattern = pattern;
        this.rng = rng;
    }

    public static string? Generate(string pattern, Random rng)
    {
        try
        {
            var g = new RegexExample(pattern, rng);
            var result = g.Alternation();
            return g.pos == pattern.Length ? result : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private char Peek => this.pos < this.pattern.Length ? this.pattern[this.pos] : '\0';

    private bool AtEnd => this.pos >= this.pattern.Length;

    private string Alternation()
    {
        var branches = new List<string> { this.Sequence() };
        while (!this.AtEnd && this.Peek == '|')
        {
            this.pos++;
            branches.Add(this.Sequence());
        }

        return branches[this.rng.Next(branches.Count)];
    }

    private string Sequence()
    {
        var sb = new StringBuilder();
        while (!this.AtEnd && this.Peek is not ('|' or ')'))
        {
            var atom = this.Atom();
            var count = this.QuantifierMin();
            for (var i = 0; i < count; i++)
                sb.Append(atom);
        }

        return sb.ToString();
    }

    private string Atom()
    {
        var c = this.pattern[this.pos++];
        switch (c)
        {
            case '(':
                if (this.Peek == '?')
                {
                    this.pos++;
                    if (this.Peek == ':')
                    {
                        this.pos++;
                    }
                    else if (this.Peek == '<' && this.pos + 1 < this.pattern.Length && this.pattern[this.pos + 1] is not ('=' or '!'))
                    {
                        var close = this.pattern.IndexOf('>', this.pos);
                        if (close < 0)
                            throw new FormatException();
                        this.pos = close + 1;
                    }
                    else
                    {
                        throw new FormatException(); // lookarounds and other constructs
                    }
                }

                var inner = this.Alternation();
                if (this.Peek != ')')
                    throw new FormatException();
                this.pos++;
                return inner;
            case '[':
                return this.Class();
            case '.':
                return "0";
            case '^' or '$':
                return string.Empty;
            case '\\':
                return this.Escape(inClass: false);
            case '*' or '+' or '?' or ')':
                throw new FormatException();
            default:
                return c.ToString();
        }
    }

    private int QuantifierMin()
    {
        int min;
        switch (this.Peek)
        {
            case '?' or '*':
                this.pos++;
                min = 0;
                break;
            case '+':
                this.pos++;
                min = 1;
                break;
            case '{':
                var close = this.pattern.IndexOf('}', this.pos);
                if (close < 0)
                    return 1;
                var body = this.pattern[(this.pos + 1)..close].Split(',')[0];
                if (!int.TryParse(body, NumberStyles.None, CultureInfo.InvariantCulture, out min))
                    return 1; // not a quantifier: '{' was taken as a literal already
                this.pos = close + 1;
                break;
            default:
                return 1;
        }

        if (this.Peek is '?' or '+')
            this.pos++; // lazy / possessive
        return min;
    }

    private string Class()
    {
        var negated = this.Peek == '^';
        if (negated)
            this.pos++;
        var members = new List<char>();
        var first = true;
        while (!this.AtEnd && (this.Peek != ']' || first))
        {
            first = false;
            var c = this.pattern[this.pos++];
            if (c == '\\')
                c = this.Escape(inClass: true)[0];
            if (this.Peek == '-' && this.pos + 1 < this.pattern.Length && this.pattern[this.pos + 1] != ']')
            {
                this.pos++;
                var hi = this.pattern[this.pos++];
                if (hi == '\\')
                    hi = this.Escape(inClass: true)[0];
                for (var x = c; x <= hi && members.Count < 4096; x++)
                    members.Add(x);
            }
            else
            {
                members.Add(c);
            }
        }

        if (this.Peek != ']')
            throw new FormatException();
        this.pos++;
        if (!negated)
            return members.Count == 0 ? throw new FormatException() : members[this.rng.Next(members.Count)].ToString();
        foreach (var candidate in "0Aa x-_")
        {
            if (!members.Contains(candidate) && !members.Contains(char.ToUpperInvariant(candidate)) && !members.Contains(char.ToLowerInvariant(candidate)))
                return candidate.ToString();
        }

        throw new FormatException();
    }

    private string Escape(bool inClass)
    {
        if (this.AtEnd)
            throw new FormatException();
        var c = this.pattern[this.pos++];
        switch (c)
        {
            case 'd': return "0";
            case 'D': return "a";
            case 'w': return "a";
            case 'W': return "-";
            case 's': return " ";
            case 'S': return "a";
            case 't': return "\t";
            case 'b' when !inClass: return string.Empty;
            case 'B' when !inClass: return string.Empty;
            case 'x' when this.pos + 2 <= this.pattern.Length:
                var hex = this.pattern.Substring(this.pos, 2);
                this.pos += 2;
                return ((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString();
            case 'u' when this.pos + 4 <= this.pattern.Length:
                var u = this.pattern.Substring(this.pos, 4);
                this.pos += 4;
                return ((char)int.Parse(u, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString();
            default:
                if (char.IsAsciiDigit(c) && !inClass)
                    throw new FormatException(); // backreference
                return c.ToString();
        }
    }
}
