using System.Globalization;
using System.Numerics;

namespace AnoMech.Tests;

// Synthetic ACT network log lines for the parser's tests, laid out as the plugin writes them. Times are seconds after
// 20:00 on a made-up day; every name is the placeholder NAME.
internal static class ActLogLines
{
    public const string Name = "NAME";
    public static readonly DateTimeOffset Origin = new(2026, 1, 1, 20, 0, 0, TimeSpan.FromHours(3));

    public static string Time(double seconds) => Origin.AddSeconds(seconds).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture);

    // A line of the given number of fields, blank but for its type, time, trailing hash and the fields given.
    public static string Line(int type, double seconds, int count, params (int Index, string Value)[] fields)
    {
        var parts = Enumerable.Repeat("", count).ToArray();
        parts[0] = type.ToString("00", CultureInfo.InvariantCulture);
        parts[1] = Time(seconds);
        parts[^1] = "0123456789abcdef";
        foreach (var (index, value) in fields) parts[index] = value;
        return string.Join('|', parts);
    }

    public static string Zone(double seconds, uint territory) => Line(1, seconds, 5, (2, Hex(territory)), (3, "Somewhere"));

    public static string Primary(double seconds, uint player) => Line(2, seconds, 5, (2, Hex(player)), (3, Name));

    public static string Combat(double seconds, bool act, bool game) => Line(260, seconds, 7, (2, act ? "1" : "0"), (3, game ? "1" : "0"));

    // Field 8 of a 03 or 04 line is a player's world.
    public static string Player(double seconds, uint player, uint job, Vector2 at)
        => Line(3, seconds, 22, (2, Hex(player)), (3, Name), (4, Hex(job)), (5, "5A"), (8, Name), (17, F(at.X)), (18, F(at.Y)), (20, "0.00"));

    public static string Enemy(double seconds, uint actor, uint bnpcBase, Vector2 at, float heading)
        => Line(3, seconds, 22, (2, Hex(actor)), (3, Name), (9, "1000"), (10, bnpcBase.ToString(CultureInfo.InvariantCulture)),
                (17, F(at.X)), (18, F(at.Y)), (20, F(heading)));

    public static string Removed(double seconds, uint player, uint job)
        => Line(4, seconds, 22, (2, Hex(player)), (3, Name), (4, Hex(job)), (5, "5A"), (8, Name), (17, "100.00"), (18, "100.00"), (20, "0.00"));

    public static string Cast(double seconds, uint source, uint action, float castSeconds)
        => Line(20, seconds, 14, (2, Hex(source)), (3, Name), (4, Hex(action)), (5, "Action"), (6, Hex(source)), (7, Name), (8, F(castSeconds)));

    public static string CastAt(double seconds, uint source, uint action, Vector2 at, float heading)
        => Line(263, seconds, 9, (2, Hex(source)), (3, Hex(action)), (4, F(at.X)), (5, F(at.Y)), (6, "0.00"), (7, F(heading)));

    // A 21 line (22 for an area ability) of the 55 fields a newer plugin writes, rotation (the effect's raw 16-bit heading)
    // second to last; the sequence names the effect result (37) that lands it.
    public static string Hit(double seconds, uint source, uint action, uint target, Vector2? targetAt = null, Vector2? sourceAt = null,
                             ushort rotation = 0x7FFF, int index = 0, int count = 1, uint sequence = 0, bool aoe = false)
        => Line(aoe ? 22 : 21, seconds, 55, (2, Hex(source)), (3, Name), (4, Hex(action)), (5, "Action"), (6, Hex(target)), (7, Name),
                (30, targetAt is { } t ? F(t.X) : ""), (31, targetAt is { } t2 ? F(t2.Y) : ""), (33, "0.00"),
                (40, sourceAt is { } s ? F(s.X) : ""), (41, sourceAt is { } s2 ? F(s2.Y) : ""), (43, "0.00"),
                (44, Sequence(sequence)), (45, index.ToString(CultureInfo.InvariantCulture)),
                (46, count.ToString(CultureInfo.InvariantCulture)), (48, Name), (53, rotation.ToString("X4", CultureInfo.InvariantCulture)));

    // A 37 line: the hit points a hit of the sequence left its target with.
    public static string EffectResult(double seconds, uint actor, uint sequence, int hp, Vector2 at)
        => Line(37, seconds, 16, (2, Hex(actor)), (3, Name), (4, Sequence(sequence)), (5, hp.ToString(CultureInfo.InvariantCulture)),
                (6, "100000"), (11, F(at.X)), (12, F(at.Y)), (13, "0.00"), (14, "0.00"));

    public static string UpdateHp(double seconds, uint actor, int hp, Vector2 at)
        => Line(39, seconds, 15, (2, Hex(actor)), (3, Name), (4, hp.ToString(CultureInfo.InvariantCulture)), (5, "100000"), (6, "10000"),
                (7, "10000"), (10, F(at.X)), (11, F(at.Y)), (12, "0.00"), (13, "0.00"));

    public static string NameToggle(double seconds, uint actor, bool shown)
        => Line(34, seconds, 8, (2, Hex(actor)), (3, Name), (4, Hex(actor)), (5, Name), (6, shown ? "01" : "00"));

    public static string Gains(double seconds, uint status, float duration, uint source, uint target)
        => Line(26, seconds, 13, (2, Hex(status)), (3, "Status"), (4, F(duration)), (5, Hex(source)), (6, Name), (7, Hex(target)), (8, Name));

    public static string Loses(double seconds, uint status, uint source, uint target)
        => Line(30, seconds, 11, (2, Hex(status)), (3, "Status"), (4, "0.00"), (5, Hex(source)), (6, Name), (7, Hex(target)), (8, Name));

    public static string Death(double seconds, uint target, uint source) => Line(25, seconds, 7, (2, Hex(target)), (3, Name), (4, Hex(source)), (5, Name));

    public static string Director(double seconds, uint code) => Line(33, seconds, 9, (2, "800375AC"), (3, Hex(code)), (4, "00"));

    public static string Placed(double seconds, uint actor, Vector2 at, float heading)
        => Line(271, seconds, 10, (2, Hex(actor)), (3, F(heading)), (4, "00"), (5, "00"), (6, F(at.X)), (7, F(at.Y)), (8, "0.0000"));

    public static string Moved(double seconds, uint actor, Vector2 at, float heading)
        => Line(270, seconds, 10, (2, Hex(actor)), (3, F(heading)), (4, "0000"), (5, "00"), (6, F(at.X)), (7, F(at.Y)), (8, "0.0000"));

    public static string StatusList(double seconds, uint actor, int hp, Vector2 at)
        => Line(38, seconds, 20, (2, Hex(actor)), (3, Name), (5, hp.ToString(CultureInfo.InvariantCulture)), (6, "100000"),
                (11, F(at.X)), (12, F(at.Y)), (13, "0.00"), (14, "0.00"));

    // A 261 line: its mode (Add, Change or Remove), the actor and key/value pairs.
    public static string Memory(double seconds, string mode, uint actor, params (string Key, string Value)[] pairs)
        => $"261|{Time(seconds)}|{mode}|{Hex(actor)}|{string.Concat(pairs.Select(p => $"{p.Key}|{p.Value}|"))}0123456789abcdef";

    public static string Chat(double seconds) => $"00|{Time(seconds)}|0839||{Name} says something|0123456789abcdef";

    public static string Hex(uint value) => value.ToString("X", CultureInfo.InvariantCulture);

    private static string Sequence(uint value) => value.ToString("X8", CultureInfo.InvariantCulture);

    private static string F(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
