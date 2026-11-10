using System.Numerics;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Top;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP3Constants;
using static AnoMech.Scenarios.Top.TopP4Constants;

namespace AnoMech.Tests;

// What a hit did to its target. A target already dead, or under an invulnerability, shows nothing of the hit's damage.
internal enum HitOutcome { Killed, Survived, Invulnerable, AlreadyDead }

// A player's death: from the first 0-HP sample (its death line comes about 2 s later), or from the death line when no
// sample shows it, until a raise shows hit points again; Raised is null when the pull ends first.
internal readonly record struct PlayerDeath(uint Player, DateTimeOffset Died, DateTimeOffset? Raised);

// One TOP pull, read into typed events. Players are named by their job, never by the name the log holds.
internal sealed class TopPull
{
    // How long after a hit its target's death counts as the hit's, when no effect result names the hit; an effect result
    // lands its damage up to about 2 s after the hit.
    public const double DeathAfterHit = 3;

    private static readonly Dictionary<ushort, string> Invulnerabilities = new()
    {
        [82] = "Hallowed Ground", [409] = "Holmgang", [810] = "Living Dead", [811] = "Walking Dead", [1836] = "Superbolide",
        [3255] = "Undead Rebirth",
    };

    public static readonly Vector2 Centre = new(TopZone.Instance.Origin.X, TopZone.Instance.Origin.Z);

    // Wave Repeater's middle, then its rings outward; P3 and P4 play the same.
    private static readonly uint[] WaveRepeaterRings =
        [P3ActionId.WaveRepeater, P3ActionId.WaveRepeaterRing2, P3ActionId.WaveRepeaterRing3, P3ActionId.WaveRepeaterRing4];

    public static bool IsWaveRepeaterRing(uint action) => WaveRepeaterRings.Contains(action);

    public static string WaveRepeaterRing(uint action) => $"ring {Array.IndexOf(WaveRepeaterRings, action) + 1}";

    // The mechanics that show how far a pull got, in fight order, each by a cast only it has.
    public static readonly IReadOnlyList<(string Name, uint[] Casts)> Milestones =
    [
        ("P1", []),
        ("P2", [ActionId.PartySynergyM, ActionId.PartySynergyF]),
        ("P3 Intermission", [P3ActionId.WaveRepeater]),
        ("P3 Hello World", [P3ActionId.HelloWorld]),
        ("P3 Monitors", [P3ActionId.OversampledWaveCannonEast, P3ActionId.OversampledWaveCannonWest]),
        ("P4 Blue Screen", [P4ActionId.WaveCannonCast]),
        ("P5", [ActionId.RunMiDeltaVersion]),
        ("P6", [ActionId.CosmoMemory]),
    ];

    public string Name { get; }
    public IReadOnlyList<ActCast> Casts { get; }
    public IReadOnlyList<ActCastAt> CastsAt { get; }
    public IReadOnlyList<ActHit> Hits { get; }
    public IReadOnlyList<ActStatus> Statuses { get; }
    public IReadOnlyList<PlayerDeath> Deaths { get; }
    public IReadOnlyList<ActMove> Moves { get; }
    public IReadOnlyList<ActTargetable> Targetables { get; }
    public IReadOnlyList<ActCombatant> Combatants { get; }
    public IReadOnlyList<ActTimeline> Timelines { get; }
    public IReadOnlyList<ActObject> Objects { get; }
    public ActPositions Positions { get; }
    public IReadOnlyDictionary<uint, JobId> Jobs { get; }

    // The player who logged the pull, whose logged position is its client's own rather than a smoothed copy of the server's.
    public uint? Self { get; }
    public string Reached { get; }

    // Each hit's effect results by target and sequence, with the target's hit points after them.
    private readonly Dictionary<(uint Target, uint Sequence), List<ActHp>> effectResults = [];
    private readonly List<(uint Holder, string Status, DateTimeOffset From, DateTimeOffset To)> invulnerable;

    public TopPull(ActPull pull)
    {
        Name = pull.ToString();
        var lines = pull.Lines.OrderBy(l => l.Time).ToList();
        Casts = Read(lines, ActCast.From);
        CastsAt = Read(lines, ActCastAt.From);
        Hits = Read(lines, ActHit.From);
        Statuses = Read(lines, ActStatus.From);
        Moves = Read(lines, ActMove.From);
        Targetables = Read(lines, ActTargetable.From);
        Combatants = Read(lines, ActCombatant.From);
        Timelines = Read(lines, ActTimeline.From);
        Objects = Read(lines, ActObject.From);
        Positions = new ActPositions(lines);
        Jobs = pull.Jobs.ToDictionary(j => j.Key, j => (JobId)j.Value);
        Self = pull.Self;
        var hitPoints = Read(lines, ActHp.From);
        foreach (var result in hitPoints.Where(h => h.Sequence is not null))
        {
            var key = (result.Actor, result.Sequence!.Value);
            if (!effectResults.TryGetValue(key, out var results)) effectResults[key] = results = [];
            results.Add(result);
        }
        Deaths = ReadDeaths(lines, hitPoints);
        invulnerable = Invulnerabilities.SelectMany(i => Holdings(i.Key).Select(h => (h.Holder, i.Value, h.From, h.To))).ToList();
        Reached = Milestones.Last(m => m.Casts.Length == 0 || Casts.Any(c => m.Casts.Contains(c.Action))).Name;
    }

    public IEnumerable<uint> Players => Jobs.Keys;

    public bool IsAlive(uint player, DateTimeOffset at)
        => Deaths.Where(d => d.Player == player && d.Died <= at).Select(d => (PlayerDeath?)d).LastOrDefault() is not { } death
           || death.Raised <= at;

    // When the player died within the seconds after the time, if they did.
    public DateTimeOffset? DiedWithin(uint player, DateTimeOffset from, double seconds)
        => Deaths.Where(d => d.Player == player && Seconds(from, d.Died) is var s && s >= 0 && s <= seconds)
                 .Select(d => (DateTimeOffset?)d.Died).FirstOrDefault();

    // The invulnerability the player holds at the time, if any.
    public string? Invulnerability(uint player, DateTimeOffset at)
        => invulnerable.Where(i => i.Holder == player && i.From <= at && at < i.To).Select(i => i.Status).FirstOrDefault();

    // The target's hit points once the hit's damage landed: its effect result, which names the hit's sequence.
    public int? HpAfter(ActHit hit)
        => hit.Sequence is { } sequence && effectResults.TryGetValue((hit.Target, sequence), out var results)
            ? results.Where(r => Seconds(hit.Time, r.Time) is >= 0 and < DeathAfterHit).Select(r => (int?)r.Hp).FirstOrDefault()
            : null;

    // Killed when the hit's effect result leaves its target at 0 HP or, with no effect result, when the target died
    // within DeathAfterHit.
    public HitOutcome Outcome(ActHit hit)
        => !IsAlive(hit.Target, hit.Time) ? HitOutcome.AlreadyDead
            : Invulnerability(hit.Target, hit.Time) is not null ? HitOutcome.Invulnerable
            : HpAfter(hit) is { } hp ? hp == 0 ? HitOutcome.Killed : HitOutcome.Survived
            : DiedWithin(hit.Target, hit.Time, DeathAfterHit) is not null ? HitOutcome.Killed
            : HitOutcome.Survived;

    // What the hit did, for a report's details.
    public string Describe(ActHit hit) => Outcome(hit) switch
    {
        HitOutcome.Killed => "killed",
        HitOutcome.Survived => HpAfter(hit) is { } hp ? $"survived at {hp} HP" : "survived",
        HitOutcome.Invulnerable => $"under {Invulnerability(hit.Target, hit.Time)}",
        _ => "already dead",
    };

    public string Who(uint player) => Jobs.TryGetValue(player, out var job) ? job.ToString() : "an unknown player";

    public bool IsTank(uint player) => Jobs.TryGetValue(player, out var job) && job is JobId.Paladin or JobId.Warrior or JobId.DarkKnight or JobId.Gunbreaker;

    public ActCast? FirstCast(params uint[] actions) => Casts.Where(c => actions.Contains(c.Action)).Cast<ActCast?>().FirstOrDefault();

    public ActHit? FirstHit(Func<ActHit, bool> where) => Hits.Where(where).Cast<ActHit?>().FirstOrDefault();

    public ActHit? LastHit(Func<ActHit, bool> where) => Hits.Where(where).Cast<ActHit?>().LastOrDefault();

    public ActStatus? FirstStatus(Func<ActStatus, bool> where) => Statuses.Where(where).Cast<ActStatus?>().FirstOrDefault();

    public double? Distance(uint a, uint b, DateTimeOffset at)
        => Positions.At(a, at) is { } pa && Positions.At(b, at) is { } pb ? Vector2.Distance(pa.Position, pb.Position) : null;

    // Who holds a status from when, until when: a holding ends where the status is lost or the holder dies.
    public List<(uint Holder, DateTimeOffset From, DateTimeOffset To)> Holdings(uint status)
    {
        var end = DateTimeOffset.MaxValue;
        var holdings = new List<(uint, DateTimeOffset, DateTimeOffset)>();
        foreach (var gain in Statuses.Where(s => s.Gained && s.Status == status))
        {
            var lost = Statuses.Where(s => !s.Gained && s.Status == status && s.Target == gain.Target && s.Time >= gain.Time).Select(s => s.Time)
                               .Concat(Deaths.Where(d => d.Player == gain.Target && d.Died >= gain.Time).Select(d => d.Died))
                               .DefaultIfEmpty(end).Min();
            holdings.Add((gain.Target, gain.Time, lost));
        }
        return holdings;
    }

    public ActCastAt? CastAt(ActCast cast)
        => CastsAt.Where(c => c.Source == cast.Source && c.Action == cast.Action && Seconds(cast.Time, c.Time) is >= 0 and < 0.1)
                  .Cast<ActCastAt?>().FirstOrDefault();

    public static double Seconds(DateTimeOffset from, DateTimeOffset to) => (to - from).TotalSeconds;

    // Items in time order, a new group starting wherever one comes more than the window after its group's first.
    public static List<List<T>> Clusters<T>(IEnumerable<T> items, Func<T, DateTimeOffset> time, double window)
    {
        var groups = new List<List<T>>();
        foreach (var item in items.OrderBy(time))
        {
            if (groups.Count == 0 || Seconds(time(groups[^1][0]), time(item)) > window) groups.Add([]);
            groups[^1].Add(item);
        }
        return groups;
    }

    // Degrees clockwise from north, 0 to 360.
    public static double Bearing(Vector2 position)
    {
        var offset = position - Centre;
        var degrees = Math.Atan2(offset.X, -offset.Y) * 180 / Math.PI;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    // The heading that faces from one point to another.
    public static double HeadingTo(Vector2 from, Vector2 to) => Math.Atan2(to.X - from.X, to.Y - from.Y);

    // The smallest difference between two angles in radians, 0 to pi.
    public static double AngleBetween(double a, double b)
    {
        var d = Math.Abs(a - b) % (2 * Math.PI);
        return d > Math.PI ? 2 * Math.PI - d : d;
    }

    private static List<T> Read<T>(List<ActLine> lines, Func<ActLine, T?> parse) where T : struct
        => lines.Select(parse).OfType<T>().ToList();

    // A 0-HP sample or a death line opens a death, and a sample showing hit points more than a second after the later of
    // the two closes it: one logged with the death line can still carry the hit points from before.
    private static List<PlayerDeath> ReadDeaths(List<ActLine> lines, List<ActHp> hitPoints)
    {
        var events = hitPoints.Where(h => ActorIds.IsPlayer(h.Actor)).Select(h => (h.Time, Player: h.Actor, Hp: (int?)h.Hp))
                              .Concat(Read(lines, ActDeath.From).Where(d => ActorIds.IsPlayer(d.Target)).Select(d => (d.Time, Player: d.Target, Hp: (int?)null)))
                              .OrderBy(e => e.Time);
        var deaths = new List<PlayerDeath>();
        var open = new Dictionary<uint, (DateTimeOffset Died, DateTimeOffset Confirmed)>();
        foreach (var (time, player, hp) in events)
        {
            if (open.TryGetValue(player, out var death))
            {
                if (hp is null) open[player] = death with { Confirmed = time };
                else if (hp > 0 && time > death.Confirmed.AddSeconds(1))
                {
                    deaths.Add(new PlayerDeath(player, death.Died, time));
                    open.Remove(player);
                }
            }
            else if (hp is null or 0) open[player] = (time, time);
        }
        deaths.AddRange(open.Select(o => new PlayerDeath(o.Key, o.Value.Died, null)));
        return deaths.OrderBy(d => d.Died).ToList();
    }
}
