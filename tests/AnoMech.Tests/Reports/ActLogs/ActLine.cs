using System.Globalization;
using System.Numerics;

namespace AnoMech.Tests;

// One line of an ACT network log: '|'-separated fields, the decimal line type first and the timestamp second. The name
// fields of the types below, and a 261 line's Name, are blanked as the line is read, so no report can print a name.
internal sealed class ActLine
{
    private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz";

    private static readonly Dictionary<int, int[]> NameFields = new()
    {
        [ActLineType.ChangePrimaryPlayer] = [3],
        [ActLineType.AddCombatant] = [3, 8],
        [ActLineType.RemoveCombatant] = [3, 8],
        [ActLineType.StartsCasting] = [3, 7],
        [ActLineType.Ability] = [3, 7, 48],
        [ActLineType.AoeAbility] = [3, 7, 48],
        [ActLineType.CancelCast] = [3],
        [ActLineType.DotTick] = [3, 18],
        [ActLineType.Death] = [3, 5],
        [ActLineType.GainsEffect] = [6, 8],
        [ActLineType.HeadMarker] = [3],
        [ActLineType.LosesEffect] = [6, 8],
        [ActLineType.NameToggle] = [3, 5],
        [ActLineType.Tether] = [3, 5],
        [ActLineType.EffectResult] = [3],
        [ActLineType.StatusList] = [3],
        [ActLineType.UpdateHp] = [3],
    };

    private readonly string[] fields;

    public int Type { get; }
    public DateTimeOffset Time { get; }

    private ActLine(int type, DateTimeOffset time, string[] fields)
    {
        Type = type;
        Time = time;
        this.fields = fields;
    }

    public static ActLine? Parse(string text)
    {
        var fields = text.Split('|');
        if (fields.Length < 2
            || !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var type)
            || !DateTimeOffset.TryParseExact(fields[1], TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return null;
        foreach (var index in NameFields.GetValueOrDefault(type, []))
            if (index < fields.Length) fields[index] = "";
        if (type == ActLineType.CombatantMemory)
            for (var i = 4; i + 2 < fields.Length; i += 2)
                if (fields[i] == "Name") fields[i + 1] = "";
        return new ActLine(type, time, fields);
    }

    public int FieldCount => fields.Length;

    public string Text(int index) => index < fields.Length ? fields[index] : "";

    public uint? Hex(int index)
        => uint.TryParse(Text(index), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value) ? value : null;

    public float? Float(int index)
        => float.TryParse(Text(index), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    public int? Int(int index)
        => int.TryParse(Text(index), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    // The log's x and y are the world's X and Z.
    public Vector2? Point(int xIndex) => Float(xIndex) is { } x && Float(xIndex + 1) is { } y ? new Vector2(x, y) : null;

    // The key/value pairs of a 261 line, between the actor id and the trailing hash.
    public IEnumerable<(string Key, string Value)> Pairs()
    {
        for (var i = 4; i + 2 < fields.Length; i += 2)
            yield return (fields[i], fields[i + 1]);
    }

    public uint? PairHex(string key)
        => Pair(key) is { } text && uint.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value) ? value : null;

    public float? PairFloat(string key)
        => Pair(key) is { } text && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private string? Pair(string key) => Pairs().Where(p => p.Key == key).Select(p => p.Value).FirstOrDefault();
}

internal static class ActLineType
{
    public const int ChangeZone = 1;
    public const int ChangePrimaryPlayer = 2;
    public const int AddCombatant = 3;
    public const int RemoveCombatant = 4;
    public const int StartsCasting = 20;
    public const int Ability = 21;
    public const int AoeAbility = 22;
    public const int CancelCast = 23;
    public const int DotTick = 24;
    public const int Death = 25;
    public const int GainsEffect = 26;
    public const int HeadMarker = 27;
    public const int LosesEffect = 30;
    public const int ActorControl = 33;
    public const int NameToggle = 34;
    public const int Tether = 35;
    public const int EffectResult = 37;
    public const int StatusList = 38;
    public const int UpdateHp = 39;
    public const int MapEffect = 257;
    public const int InCombat = 260;
    public const int CombatantMemory = 261;
    public const int StartsCastingExtra = 263;
    public const int ActorMove = 270;
    public const int ActorSetPosition = 271;
    public const int ActorControlExtra = 273;
}
