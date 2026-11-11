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

/// <summary>Who or what an action is aimed at (actions.yaml <c>target</c>).</summary>
public enum ActionTarget
{
    /// <summary>Not given (actions built in code rather than loaded from actions.yaml).</summary>
    Unknown,

    /// <summary>Only yourself.</summary>
    Self,

    /// <summary>Yourself or one party member (the reminder may name who).</summary>
    Single,

    /// <summary>An area around you or around the target: the whole party in range.</summary>
    Party,

    /// <summary>A placed area (Asylum, Sacred Soil, Earthly Star, Liturgy of the Bell): the reminder should say where.</summary>
    Ground,

    /// <summary>A debuff on enemies (Reprisal, Feint, Addle, Dismantle).</summary>
    Enemy,
}

/// <summary>When an action is pressed relative to the damage, when it differs from its kind's default (actions.yaml <c>when</c>).</summary>
public enum UseTiming
{
    /// <summary>The kind's default: heals after the hit, everything else before it (docs/research/actions.md §6).</summary>
    Default,

    /// <summary>Pressed before the damage even though it heals (pre-placed heals: Liturgy of the Bell, Earthly Star, Excogitation, Horoscope, Macrocosmos).</summary>
    Before,

    /// <summary>Pressed after the damage (healer limit breaks).</summary>
    After,
}

/// <summary>A job resource an action consumes, e.g. ("Lily", 1), ("Aetherflow", 1), ("Addersgall", 1).</summary>
public sealed record ResourceCost(string Resource, int Amount);

/// <summary>One lasting effect of an action (actions.yaml <c>effects</c>): a hand-written label and its duration by level threshold.</summary>
/// <param name="Name">Label, e.g. "Divine Grace (enables Divine Caress)".</param>
/// <param name="DurationByLevel">Seconds by level threshold (level → seconds); 0 = the effect does not exist yet at that level.</param>
public sealed record ActionEffect(string Name, IReadOnlyDictionary<int, double> DurationByLevel)
{
    /// <summary>Duration at a level: the value of the highest threshold ≤ level (the first value below every threshold), or null when unknown.</summary>
    public double? DurationAt(int level) => ActionInfo.PickAtLevel(this.DurationByLevel, level);
}

/// <summary>A reference to another action row (follow-ups, pet executions, re-press rows).</summary>
/// <param name="Id">Game Action row id (it may not be in the catalog, e.g. pet actions).</param>
/// <param name="Name">The row's name.</param>
/// <param name="Detail">Data-file explanation: when the follow-up becomes usable, or what the related row is.</param>
public sealed record ActionLink(uint Id, string Name, string? Detail = null);

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

    /// <summary>Who or what the action is aimed at.</summary>
    public ActionTarget Target { get; init; } = ActionTarget.Unknown;

    /// <summary>Game icon id (Lumina <c>Action.Icon</c>).</summary>
    public uint Icon { get; init; }

    /// <summary>
    /// Recast group (Action sheet <c>CooldownGroup</c>; 58 = the GCD). Off-GCD actions of one job that share a
    /// group share a recast (Raw Intuition, Bloodwhetting and Nascent Flash: group 7).
    /// </summary>
    public int CooldownGroup { get; init; }

    /// <summary>Second recast group (Action sheet <c>AdditionalCooldownGroup</c>), when not 0; charge systems use 71–73.</summary>
    public int? AdditionalCooldownGroup { get; init; }

    /// <summary>Recast by level threshold (level → seconds), only when a trait changes it; <see cref="Recast"/> is the level-100 value.</summary>
    public IReadOnlyDictionary<int, double> RecastByLevel { get; init; } = new Dictionary<int, double>();

    /// <summary>MP cost, when the action costs MP.</summary>
    public int? MpCost { get; init; }

    /// <summary>Status (game Status row id) that must be active to use the action, e.g. Divine Grace for Divine Caress.</summary>
    public uint? RequiresStatus { get; init; }

    /// <summary>What must be true to use the action, in words ("faerie summoned", "Seraphism active").</summary>
    public string? Requires { get; init; }

    /// <summary>Press before or after the damage, when it differs from the kind's default.</summary>
    public UseTiming When { get; init; } = UseTiming.Default;

    /// <summary>Minimum seconds between the press and the damage (Earthly Star: 10, the star must mature), or null.</summary>
    public double? Lead { get; init; }

    /// <summary>Limit break tier (1, 2 or 3) for limit breaks, else null.</summary>
    public int? LimitBreakTier { get; init; }

    /// <summary>
    /// Every lasting effect with its duration by level, when the action has several or a level-dependent one
    /// (e.g. Temperance: the mitigation, plus Divine Grace from level 100). Empty when <see cref="DurationByLevel"/> says it all.
    /// </summary>
    public IReadOnlyList<ActionEffect> Effects { get; init; } = [];

    /// <summary>The base action this one upgrades (inverse of <see cref="ReplacedBy"/>; e.g. Concitation → Succor).</summary>
    public uint? UpgradeOf { get; init; }

    /// <summary>The action that makes this one usable (Divine Caress after Temperance, Consolation after Summon Seraph), or null.</summary>
    public uint? FollowUpOf { get; init; }

    /// <summary>Actions that become usable (or replace a button) after this one.</summary>
    public IReadOnlyList<ActionLink> FollowUps { get; init; } = [];

    /// <summary>
    /// Other game rows for the same thing that are not in the catalog: pet executions (Eos's Whispering Dawn 803,
    /// Seraph's Angel's Whisper 16550) and re-press rows (Liturgy of the Bell 28509).
    /// </summary>
    public IReadOnlyList<ActionLink> Related { get; init; } = [];

    /// <summary>True for follow-up rows that only exist after another action (<see cref="FollowUpOf"/> is set).</summary>
    public bool IsFollowUp => this.FollowUpOf != null;

    /// <summary>Effect duration at a sync level: the value of the highest threshold ≤ level, or null.</summary>
    public double? DurationAt(int level) => PickAtLevel(this.DurationByLevel, level);

    public int ChargesAt(int level) => PickAtLevel(this.ChargesByLevel, level) ?? 1;

    /// <summary>Recast in seconds at a level: <see cref="RecastByLevel"/> when a trait changes it, else <see cref="Recast"/>.</summary>
    public double RecastAt(int level) => PickAtLevel(this.RecastByLevel, level) ?? this.Recast;

    /// <summary>True if learned at or below <paramref name="level"/> and not yet replaced by an upgrade.</summary>
    public bool AvailableAt(int level) =>
        this.Level <= level && (this.ReplacedAtLevel is not { } r || level < r);

    public bool UsableBy(Job job) => this.Jobs.Contains(job);

    /// <summary>
    /// True when using one of the two actions puts the other on cooldown: they share a job and a recast group
    /// other than the GCD (group 58, which every GCD shares).
    /// </summary>
    public bool SharesRecastWith(ActionInfo other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Id == this.Id)
            return true;
        if (!this.Jobs.Any(other.Jobs.Contains))
            return false;
        foreach (var mine in this.RecastGroups())
        {
            foreach (var theirs in other.RecastGroups())
            {
                if (mine == theirs)
                    return true;
            }
        }
        return false;
    }

    public override string ToString() => $"{this.Name} ({this.Id})";

    /// <summary>Recast groups other than "none" (0) and the GCD (58).</summary>
    private IEnumerable<int> RecastGroups()
    {
        const int gcdGroup = 58;
        if (this.CooldownGroup is not (0 or gcdGroup))
            yield return this.CooldownGroup;
        if (this.AdditionalCooldownGroup is { } extra && extra is not (0 or gcdGroup))
            yield return extra;
    }

    /// <summary>The value of the highest threshold ≤ level; below every threshold the first value; null when the map is empty.</summary>
    internal static T? PickAtLevel<T>(IReadOnlyDictionary<int, T> byLevel, int level)
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

/// <summary>Level-sync helpers that work with any <see cref="IActionCatalog"/>.</summary>
public static class ActionCatalogExtensions
{
    /// <summary>
    /// The action a player presses for <paramref name="action"/> at a sync level (docs/research/actions.md §6):
    /// an action replaced by an upgrade at or below the level resolves to the upgrade (repeatedly; Succor → Concitation
    /// at 96+), and an upgrade not learned yet at the level resolves back to its base action (Heart of Corundum →
    /// Heart of Stone in TEA). Anything else is returned unchanged, even when it is not available at the level
    /// (check <see cref="ActionInfo.AvailableAt"/>).
    /// </summary>
    public static ActionInfo ResolveAt(this IActionCatalog catalog, ActionInfo action, int syncLevel)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(action);

        var current = action;
        for (var guard = 0; guard < 16; guard++)
        {
            if (current.ReplacedBy is { } upgradeId && current.ReplacedAtLevel is { } at && at <= syncLevel &&
                catalog.Get(upgradeId) is { } upgrade && upgrade.Id != current.Id)
            {
                current = upgrade;
                continue;
            }

            if (current.UpgradeOf is { } baseId && current.Level > syncLevel &&
                catalog.Get(baseId) is { } baseAction && baseAction.Id != current.Id)
            {
                current = baseAction;
                continue;
            }

            break;
        }
        return current;
    }

    /// <summary><see cref="IActionCatalog.FindByName"/> followed by <see cref="ResolveAt"/>; null when the name is unknown.</summary>
    public static ActionInfo? FindAtLevel(this IActionCatalog catalog, string name, int syncLevel, Job? job = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog.FindByName(name, job) is { } action ? catalog.ResolveAt(action, syncLevel) : null;
    }
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

    /// <summary>
    /// Actions to use instead of <see cref="Actions"/> when the item names another player ("Short CD to R1",
    /// "Nascant Co-Tank"): e.g. WAR "short cd" is Bloodwhetting on yourself but Nascent Flash on someone else.
    /// Empty when the phrase has no targeted form.
    /// </summary>
    public IReadOnlyList<string> Targeted { get; init; } = [];

    /// <summary>True for user-defined aliases (persisted by the plugin, highest priority).</summary>
    public bool IsUser { get; init; }

    public override string ToString()
    {
        var what = this.NoteOnly
            ? "note: " + (this.Note ?? string.Empty)
            : string.Join(this.Sequence ? " → " : " + ", this.Actions);
        if (this.Targeted.Count > 0)
            what += " (targeted: " + string.Join(" + ", this.Targeted) + ")";
        return this.Job is { } job ? $"{job}: {what}" : what;
    }
}

/// <summary>Result of <see cref="AliasDictionary.MatchLongest"/>: the longest alias key at the start of an item, and what follows it.</summary>
/// <param name="Key">The matched (normalized) alias key, e.g. "short cd".</param>
/// <param name="Rest">The normalized text after the key (a hint, target or count, e.g. "to r1"); empty when the key is the whole item.</param>
/// <param name="Expansions">The key's expansions applicable to the requested job (user aliases first); may be empty when none applies.</param>
public sealed record AliasMatch(string Key, string Rest, IReadOnlyList<AliasExpansion> Expansions);
