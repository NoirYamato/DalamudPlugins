using System.Numerics;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Top;
using static AnoMech.Scenarios.Top.TopConstants;
using static AnoMech.Scenarios.Top.TopP3Constants;
using static AnoMech.Scenarios.Top.TopP4Constants;

namespace AnoMech.Tests;

// One TOP pull, read into typed events. Players are named by their job, never by the name the log holds.
internal sealed class TopPull
{
    public static readonly Vector2 Centre = new(TopZone.Instance.Origin.X, TopZone.Instance.Origin.Z);

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
    public IReadOnlyList<ActDeath> Deaths { get; }
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

    private readonly Dictionary<uint, List<(DateTimeOffset Time, int Hp)>> hitPoints = [];

    public TopPull(ActPull pull)
    {
        Name = pull.ToString();
        var lines = pull.Lines.OrderBy(l => l.Time).ToList();
        Casts = Read(lines, ActCast.From);
        CastsAt = Read(lines, ActCastAt.From);
        Hits = Read(lines, ActHit.From);
        Statuses = Read(lines, ActStatus.From);
        Deaths = Read(lines, ActDeath.From);
        Moves = Read(lines, ActMove.From);
        Targetables = Read(lines, ActTargetable.From);
        Combatants = Read(lines, ActCombatant.From);
        Timelines = Read(lines, ActTimeline.From);
        Objects = Read(lines, ActObject.From);
        Positions = new ActPositions(lines);
        Jobs = pull.Jobs.ToDictionary(j => j.Key, j => (JobId)j.Value);
        Self = pull.Self;
        foreach (var line in lines)
            if (HitPoints(line) is var (actor, hp) && ActorIds.IsPlayer(actor))
            {
                if (!hitPoints.TryGetValue(actor, out var samples)) hitPoints[actor] = samples = [];
                samples.Add((line.Time, hp));
            }
        Reached = Milestones.Last(m => m.Casts.Length == 0 || Casts.Any(c => m.Casts.Contains(c.Action))).Name;
    }

    public IEnumerable<uint> Players => Jobs.Keys;

    // Dead from a death line until a later line shows hit points again, as a raise brings them back.
    public bool IsAlive(uint player, DateTimeOffset at)
    {
        var died = Deaths.Where(d => d.Target == player && d.Time <= at).Select(d => (DateTimeOffset?)d.Time).Max();
        return died is not { } death
               || hitPoints.TryGetValue(player, out var samples) && samples.Any(s => s.Time > death.AddSeconds(1) && s.Time <= at && s.Hp > 0);
    }

    public string Who(uint player) => Jobs.TryGetValue(player, out var job) ? job.ToString() : "an unknown player";

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
                               .Concat(Deaths.Where(d => d.Target == gain.Target && d.Time >= gain.Time).Select(d => d.Time))
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

    private static (uint Actor, int Hp)? HitPoints(ActLine line)
        => line.Hex(2) is not { } actor ? null
            : line.Type switch
            {
                ActLineType.EffectResult or ActLineType.StatusList when line.Int(5) is { } hp => (actor, hp),
                ActLineType.UpdateHp when line.Int(4) is { } hp => (actor, hp),
                _ => null,
            };
}
