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

/// <summary>A fight file that <see cref="FightLibrary"/> left out, or loaded with a warning.</summary>
/// <param name="Path">Data path of the file, e.g. "fights/top.yaml" ("fights/" when the folder could not be listed).</param>
/// <param name="Message">What is wrong, naming the file (loader messages also name the item, field and line).</param>
/// <param name="Exception">The exception the file failed with; null for a file that loaded but was ignored or kept with a warning.</param>
public sealed record FightFileProblem(string Path, string Message, Exception? Exception = null)
{
    public override string ToString() => this.Message;
}

/// <summary>Loads fight definitions (data/fights/*.yaml) and their timelines.</summary>
/// <remarks>
/// Every file is loaded on its own, in path order, so one bad file never costs the others: a file that fails
/// (unreadable, invalid YAML or invalid data) is left out and reported in <see cref="Problems"/>. A file whose
/// fight key (case-insensitive) an earlier file already defined is left out too, because plans name fights by key.
/// A fight whose territory an earlier fight already has is kept (it is still available by key) and reported;
/// <see cref="ForTerritory"/> returns the earlier one.
/// </remarks>
public sealed class FightLibrary
{
    private readonly IDataSource data;
    private readonly Dictionary<string, Timeline> timelines = new(StringComparer.OrdinalIgnoreCase);

    public FightLibrary(IDataSource data)
    {
        ArgumentNullException.ThrowIfNull(data);
        this.data = data;

        var problems = new List<FightFileProblem>();
        var paths = new List<string>();
        try
        {
            paths.AddRange(data.List("fights").Where(p => p.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex)
        {
            problems.Add(new FightFileProblem("fights/", $"fights/: the fight files could not be listed: {ex.Message}", ex));
        }

        var fights = new List<FightDefinition>();
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // fight key → file
        var territories = new Dictionary<ushort, FightDefinition>();
        foreach (var path in paths)
        {
            FightDefinition fight;
            try
            {
                fight = FightLoader.Load(data.ReadText(path), path);
            }
            catch (Exception ex)
            {
                // Loader messages start with the file name; others (I/O) get it here.
                var message = ex is FormatException && ex.Message.StartsWith(path, StringComparison.Ordinal) ? ex.Message : $"{path}: {ex.Message}";
                problems.Add(new FightFileProblem(path, message, ex));
                continue;
            }

            if (keys.TryGetValue(fight.Key, out var first))
            {
                problems.Add(new FightFileProblem(path, $"{path}: fight '{fight.Key}' is already defined by {first}; this file is ignored."));
                continue;
            }

            keys[fight.Key] = path;
            if (territories.TryGetValue(fight.Territory, out var owner))
            {
                problems.Add(new FightFileProblem(
                    path,
                    $"{path}: territory {fight.Territory} also belongs to fight '{owner.Key}' ({keys[owner.Key]}); that zone opens '{owner.Key}', '{fight.Key}' is only available by its key."));
            }
            else
            {
                territories[fight.Territory] = fight;
            }

            fights.Add(fight);
        }

        this.Fights = fights.AsReadOnly();
        this.Problems = problems.AsReadOnly();
    }

    /// <summary>The fights that loaded, in file path order.</summary>
    public IReadOnlyList<FightDefinition> Fights { get; }

    /// <summary>Fight files that were left out (or kept with a warning), one entry each, in file path order.</summary>
    public IReadOnlyList<FightFileProblem> Problems { get; }

    public FightDefinition? Get(string key) => this.Fights.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

    public FightDefinition? ForTerritory(ushort territory) => this.Fights.FirstOrDefault(f => f.Territory == territory);

    /// <summary>Parses (and caches) the fight's timeline.</summary>
    public Timeline LoadTimeline(FightDefinition fight)
    {
        ArgumentNullException.ThrowIfNull(fight);
        if (!this.timelines.TryGetValue(fight.Key, out var t))
        {
            t = TimelineParser.Parse(this.data.ReadText("timelines/" + fight.TimelineFile), fight.TimelineFile);
            this.timelines[fight.Key] = t;
        }
        return t;
    }
}
