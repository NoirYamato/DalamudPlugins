using System.Globalization;

namespace AnoMech.Tests;

// One combat segment in one territory, with the lines logged while it ran (only the types ActLog keeps), the job of
// every player the log had seen in the territory by then, and Self, the player who logged it.
internal sealed class ActPull
{
    public required string File { get; init; }
    public required int Number { get; init; }
    public required DateTimeOffset Start { get; init; }
    public DateTimeOffset? End { get; set; }
    public required Dictionary<uint, uint> Jobs { get; init; }
    public uint? Self { get; init; }
    public List<ActLine> Lines { get; } = [];
    public List<uint> DirectorCodes { get; } = [];

    public bool Cleared => DirectorCodes.Contains(ActLog.DirectorClear);

    // A combat segment where no enemy cast or used an ability is no pull: the plugin's own runs open such segments,
    // since their enemies' casts never cross the network.
    public bool EnemyActed
        => Lines.Any(l => l.Type is ActLineType.StartsCasting or ActLineType.Ability or ActLineType.AoeAbility && ActorIds.IsEnemy(l.Hex(2)));

    public override string ToString() => $"{File} pull {Number}";
}

// Splits an ACT network log into pulls in one territory. A pull runs while the game's own combat flag (field 3 of a 260
// line) is up: ACT's flag (field 2) bridges a quick wipe and re-pull. A log with no 260 line at all takes its pulls from
// the duty director's codes instead: commence opens one, a clear or a wipe closes it, and after a wipe the first enemy
// ability opens the next once the fade-out is done, so the fade-out's last hits can't open one.
internal sealed class ActLog
{
    public const uint DirectorCommence = 0x40000001;
    public const uint DirectorClear = 0x40000003;
    public const uint DirectorWipe = 0x40000005;
    public const uint DirectorFadeOutDone = 0x40000006;

    private static readonly HashSet<int> KeptTypes =
    [
        ActLineType.AddCombatant, ActLineType.RemoveCombatant, ActLineType.StartsCasting, ActLineType.Ability,
        ActLineType.AoeAbility, ActLineType.Death, ActLineType.GainsEffect, ActLineType.LosesEffect,
        ActLineType.ActorControl, ActLineType.NameToggle, ActLineType.EffectResult, ActLineType.StatusList,
        ActLineType.UpdateHp, ActLineType.CombatantMemory, ActLineType.StartsCastingExtra, ActLineType.ActorMove,
        ActLineType.ActorSetPosition, ActLineType.ActorControlExtra,
    ];

    private readonly string file;
    private readonly uint territory;
    private readonly Dictionary<uint, uint> jobs = [];
    private uint? self;
    private uint? zone;
    private ActPull? open;
    private int count;
    private bool fadingOut;

    private ActLog(string name, uint territory)
    {
        file = Path.GetFileName(name);
        this.territory = territory;
    }

    public static IEnumerable<ActPull> Pulls(string path, uint territory) => Pulls(path, () => ReadShared(path), territory);

    // ACT holds its current log open for writing, so the log is opened shared and read to the end written so far.
    private static IEnumerable<string> ReadShared(string path)
    {
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        while (reader.ReadLine() is { } line)
            yield return line;
    }

    // Lines is read twice: once to find whether the log has 260 lines at all.
    public static IEnumerable<ActPull> Pulls(string name, Func<IEnumerable<string>> lines, uint territory)
    {
        var log = new ActLog(name, territory);
        var byCombatFlag = lines().Any(l => l.StartsWith("260|", StringComparison.Ordinal));
        foreach (var text in lines())
        {
            if (Read(text) is not { } line) continue;
            if (PlayerJob(line) is var (player, job)) log.jobs[player] = job;
            if (line.Type == ActLineType.ChangePrimaryPlayer) log.self = line.Hex(2);
            var closed = byCombatFlag ? log.ByCombatFlag(line) : log.ByDirector(line);
            if (closed is not null) yield return closed;
        }
        if (log.open is not null) yield return log.open;
    }

    private static (uint Player, uint Job)? PlayerJob(ActLine line)
        => line.Type == ActLineType.AddCombatant && line.Hex(2) is { } id && ActorIds.IsPlayer(id) && line.Hex(4) is { } job
            ? (id, job)
            : null;

    private ActPull? ByCombatFlag(ActLine line)
    {
        if (line.Type == ActLineType.ChangeZone)
        {
            zone = line.Hex(2);
            jobs.Clear();
            return Close(line.Time);
        }
        if (zone != territory) return null;
        if (line.Type == ActLineType.InCombat && line.Int(3) is { } inGameCombat)
        {
            if (inGameCombat == 1 && open is null) Open(line.Time);
            else if (inGameCombat == 0) return Close(line.Time);
            return null;
        }
        Keep(line);
        return null;
    }

    private ActPull? ByDirector(ActLine line)
    {
        if (line.Type == ActLineType.ChangeZone)
        {
            zone = line.Hex(2);
            jobs.Clear();
            fadingOut = false;
            return Close(line.Time);
        }
        if (zone != territory) return null;
        if (line.Type == ActLineType.ActorControl && line.Hex(3) is { } code)
        {
            Keep(line);
            if (code == DirectorFadeOutDone)
            {
                fadingOut = false;
                return Close(line.Time);
            }
            if (open is null && code == DirectorCommence)
            {
                fadingOut = false;
                Open(line.Time);
            }
            else if (open is not null && code is DirectorClear or DirectorWipe)
            {
                fadingOut = true;
                return Close(line.Time);
            }
            return null;
        }
        if (open is null && !fadingOut && line.Type is ActLineType.Ability or ActLineType.AoeAbility && ActorIds.IsEnemy(line.Hex(2)))
            Open(line.Time);
        Keep(line);
        return null;
    }

    private static ActLine? Read(string text)
    {
        var bar = text.IndexOf('|');
        if (bar <= 0 || !int.TryParse(text.AsSpan(0, bar), NumberStyles.None, CultureInfo.InvariantCulture, out var type)) return null;
        return type is ActLineType.ChangeZone or ActLineType.InCombat or ActLineType.ChangePrimaryPlayer || KeptTypes.Contains(type)
            ? ActLine.Parse(text)
            : null;
    }

    private void Open(DateTimeOffset at)
        => open = new ActPull { File = file, Number = ++count, Start = at, Jobs = new Dictionary<uint, uint>(jobs), Self = self };

    private void Keep(ActLine line)
    {
        if (open is null || !KeptTypes.Contains(line.Type)) return;
        if (line.Type == ActLineType.ActorControl && line.Hex(3) is { } code) open.DirectorCodes.Add(code);
        if (PlayerJob(line) is var (player, job)) open.Jobs[player] = job;
        open.Lines.Add(line);
    }

    private ActPull? Close(DateTimeOffset at)
    {
        if (open is null) return null;
        var closed = open;
        closed.End = at;
        open = null;
        return closed;
    }
}

internal static class ActorIds
{
    public const uint None = 0xE0000000;

    public static bool IsPlayer(uint? id) => id is >= 0x10000000 and < 0x20000000;

    public static bool IsEnemy(uint? id) => id is >= 0x40000000 and < 0x50000000;
}
