namespace MitCue.Core.Actions;

public enum ActionKind
{
    PartyMit,
    Shield,
    Regen,
    Heal,
    TankMit,
    Invuln,
    Buff,
    LimitBreak,
    Movement,
    Other,
}

/// <summary>A job resource an action consumes, e.g. ("Lily", 1), ("Aetherflow", 1), ("Addersgall", 1).</summary>
public sealed record ResourceCost(string Resource, int Amount);

/// <summary>What MitCue knows about a player action (from data/actions/actions.yaml).</summary>
public sealed class ActionInfo
{
    /// <summary>Game Action row id.</summary>
    public required uint Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Jobs that can use it (role actions list every job of the role).</summary>
    public IReadOnlyList<Job> Jobs { get; init; } = [];

    /// <summary>Level at which it is learned.</summary>
    public int Level { get; init; }

    /// <summary>Recast in seconds at max level (in game, ActionManager gives the real value).</summary>
    public double Recast { get; init; }

    /// <summary>Max charges by level threshold (level → charges); empty means 1.</summary>
    public IReadOnlyDictionary<int, int> ChargesByLevel { get; init; } = new Dictionary<int, int>();

    /// <summary>Cast time in seconds (0 for instant).</summary>
    public double CastTime { get; init; }

    public bool IsGcd { get; init; }

    public ResourceCost? Cost { get; init; }

    public ActionKind Kind { get; init; } = ActionKind.Other;

    /// <summary>Effect duration in seconds by level threshold (level → seconds), from tooltips.</summary>
    public IReadOnlyDictionary<int, double> DurationByLevel { get; init; } = new Dictionary<int, double>();

    /// <summary>Action that replaces this one at <see cref="ReplacedAtLevel"/> (e.g. Succor → Concitation at 96).</summary>
    public uint? ReplacedBy { get; init; }

    public int? ReplacedAtLevel { get; init; }

    /// <summary>Free-form notes from the data file.</summary>
    public string? Notes { get; init; }

    /// <summary>Effect duration at a sync level: the value of the highest threshold ≤ level, or null.</summary>
    public double? DurationAt(int level) => PickAtLevel(this.DurationByLevel, level);

    public int ChargesAt(int level) => PickAtLevel(this.ChargesByLevel, level) ?? 1;

    /// <summary>True if learned at or below <paramref name="level"/> and not yet replaced by an upgrade.</summary>
    public bool AvailableAt(int level) =>
        this.Level <= level && (this.ReplacedAtLevel is not { } r || level < r);

    public bool UsableBy(Job job) => this.Jobs.Contains(job);

    public override string ToString() => $"{this.Name} ({this.Id})";

    private static T? PickAtLevel<T>(IReadOnlyDictionary<int, T> byLevel, int level)
        where T : struct
    {
        T? best = null;
        var bestLevel = int.MinValue;
        foreach (var (lv, v) in byLevel)
        {
            if (lv <= level && lv > bestLevel)
            {
                best = v;
                bestLevel = lv;
            }
        }
        if (best == null && byLevel.Count > 0)
            best = byLevel.OrderBy(kv => kv.Key).First().Value;
        return best;
    }
}

/// <summary>Lookup of player actions.</summary>
public interface IActionCatalog
{
    IEnumerable<ActionInfo> All { get; }

    ActionInfo? Get(uint id);

    /// <summary>Exact (case/punctuation-insensitive) name lookup, optionally preferring an action of <paramref name="job"/>.</summary>
    ActionInfo? FindByName(string name, Job? job = null);

    /// <summary>Actions the job can use at the sync level (upgrades applied).</summary>
    IEnumerable<ActionInfo> ForJob(Job job, int syncLevel);
}

/// <summary>One possible meaning of a shorthand token (from data/actions/aliases.yaml or user corrections).</summary>
public sealed class AliasExpansion
{
    /// <summary>Action names (resolved through <see cref="IActionCatalog"/>), in order.</summary>
    public IReadOnlyList<string> Actions { get; init; } = [];

    /// <summary>Only applies to this job (null = any job that has the actions).</summary>
    public Job? Job { get; init; }

    /// <summary>The actions are pressed in order (e.g. "spreadlo" = Adloquium then Deployment Tactics).</summary>
    public bool Sequence { get; init; }

    /// <summary>The phrase is shown as a note rather than resolved to actions (e.g. "everything").</summary>
    public bool NoteOnly { get; init; }

    public string? Note { get; init; }

    /// <summary>True for user-defined aliases (persisted by the plugin, highest priority).</summary>
    public bool IsUser { get; init; }
}
