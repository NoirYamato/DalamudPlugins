using MitCue.Core.Data;
using MitCue.Core.Timelines;

namespace MitCue.Core.Fights;

public enum MechanicKind
{
    Raidwide,
    Tankbuster,
    Multihit,
    Stack,
    Spread,
    Mixed,
    LbCheck,
    Enrage,
    Other,

    /// <summary>Not damage: pull, phase start, targetable... (anchors only).</summary>
    Marker,
}

public sealed class Phase
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Timeline seconds where the phase begins (its cactbot sync point).</summary>
    public required double Start { get; init; }
}

/// <summary>A damage event (or marker) that plan entries anchor to.</summary>
public sealed class Mechanic
{
    /// <summary>Stable id, phase-prefixed, e.g. "p6.cosmo_memory".</summary>
    public required string Id { get; init; }

    /// <summary>Short display name.</summary>
    public required string Name { get; init; }

    public required string Phase { get; init; }

    /// <summary>Timeline seconds of the (first) damage.</summary>
    public required double Hit { get; init; }

    /// <summary>All damage times when multi-hit (contains <see cref="Hit"/>).</summary>
    public IReadOnlyList<double> Hits { get; init; } = [];

    /// <summary>cactbot entry name at <see cref="Hit"/> (validated against the timeline in tests).</summary>
    public string? Entry { get; init; }

    public MechanicKind Kind { get; init; } = MechanicKind.Other;

    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>Hit time of the n-th hit (0-based), falling back to the last/first available hit.</summary>
    public double HitAt(int index)
    {
        if (this.Hits.Count == 0)
            return this.Hit;
        return this.Hits[Math.Clamp(index, 0, this.Hits.Count - 1)];
    }
}

/// <summary>A fight: territory, sync level, its cactbot timeline and the mechanics plans anchor to.</summary>
public sealed class FightDefinition
{
    /// <summary>Short key, e.g. "top".</summary>
    public required string Key { get; init; }

    public required string Name { get; init; }

    public required ushort Territory { get; init; }

    /// <summary>Level the content syncs players to (80/90/100).</summary>
    public required int SyncLevel { get; init; }

    /// <summary>Timeline file name under data/timelines.</summary>
    public required string TimelineFile { get; init; }

    public IReadOnlyList<Phase> Phases { get; init; } = [];

    /// <summary>Mechanics ordered by hit time.</summary>
    public IReadOnlyList<Mechanic> Mechanics { get; init; } = [];

    /// <summary>Known sheets' phase zero points: sheet key → phase id → timeline seconds of sheet time 0:00.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Calibration { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, double>>();

    public Mechanic? GetMechanic(string id) => this.Mechanics.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    public Phase? GetPhase(string id) => this.Phases.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The phase containing timeline time <paramref name="t"/> (the last phase whose start ≤ t).</summary>
    public Phase? PhaseAt(double t) => this.Phases.Where(p => p.Start <= t).OrderBy(p => p.Start).LastOrDefault();
}

/// <summary>Loads fight definitions (data/fights/*.yaml) and their timelines.</summary>
public sealed class FightLibrary
{
    private readonly IDataSource data;
    private readonly Dictionary<string, Timeline> timelines = new(StringComparer.OrdinalIgnoreCase);

    public FightLibrary(IDataSource data)
    {
        this.data = data;
        this.Fights = data.List("fights")
            .Where(p => p.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            .Select(p => FightLoader.Load(data.ReadText(p)))
            .ToList();
    }

    public IReadOnlyList<FightDefinition> Fights { get; }

    public FightDefinition? Get(string key) => this.Fights.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

    public FightDefinition? ForTerritory(ushort territory) => this.Fights.FirstOrDefault(f => f.Territory == territory);

    /// <summary>Parses (and caches) the fight's timeline.</summary>
    public Timeline LoadTimeline(FightDefinition fight)
    {
        if (!this.timelines.TryGetValue(fight.Key, out var t))
        {
            t = TimelineParser.Parse(this.data.ReadText("timelines/" + fight.TimelineFile), fight.TimelineFile);
            this.timelines[fight.Key] = t;
        }
        return t;
    }
}

/// <summary>Reads the fight YAML schema documented in docs/DESIGN.md §3.1.</summary>
public static class FightLoader
{
    /// <summary>Parses one fight YAML document. Throws <see cref="FormatException"/> with a helpful message on invalid data.</summary>
    public static FightDefinition Load(string yaml) => throw new NotImplementedException();
}
