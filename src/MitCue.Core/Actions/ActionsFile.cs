using MitCue.Core.Data;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MitCue.Core.Actions;

/// <summary>
/// Typed form of data/actions/actions.yaml (schema in docs/research/actions.md §6). Deserialized strictly:
/// an unknown key is an error, and so is a repeated key (YamlDotNet would keep the last value), so typos and
/// copy-paste slips in the knowledge base are caught by the data tests.
/// </summary>
internal sealed class ActionsFile
{
    public const string CurrentSchema = "mitcue-actions/1";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .WithDuplicateKeyChecking()
        .Build();

    public string Schema { get; set; } = string.Empty;

    public string GameVersion { get; set; } = string.Empty;

    public List<ActionDto> Actions { get; set; } = [];

    /// <summary>Deserializes the file; YAML and type errors become <see cref="FormatException"/>s with the line.</summary>
    public static ActionsFile Parse(string yaml, string what = "actions.yaml")
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ActionsFile? file;
        try
        {
            file = Deserializer.Deserialize<ActionsFile>(YamlNodes.StripBom(yaml));
        }
        catch (YamlException ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            throw new FormatException($"{what}: {message} ({YamlNodes.Where(ex.Start)}).", ex);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            throw new FormatException($"{what}: invalid YAML (check brackets, quotes and indentation): {ex.Message}", ex);
        }

        if (file == null)
            throw new FormatException($"{what}: the document is empty.");
        if (file.Schema != CurrentSchema)
            throw new FormatException($"{what}: unsupported schema '{file.Schema}' (expected '{CurrentSchema}').");
        file.Actions ??= [];
        return file;
    }

    /// <summary>Converts every entry to <see cref="ActionInfo"/>, validating ids, jobs and enum values.</summary>
    public List<ActionInfo> ToActionInfos(string what = "actions.yaml")
    {
        var result = new List<ActionInfo>(this.Actions.Count);
        var seen = new Dictionary<uint, string>();
        for (var i = 0; i < this.Actions.Count; i++)
        {
            var dto = this.Actions[i] ?? throw new FormatException($"{what}: actions[{i}] is empty.");
            var context = $"{what}: action {dto.Id} '{dto.Name}'";
            if (dto.Id == 0)
                throw new FormatException($"{what}: actions[{i}] ('{dto.Name}') has no id.");
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new FormatException($"{what}: action {dto.Id} has no name.");
            if (!seen.TryAdd(dto.Id, dto.Name))
                throw new FormatException($"{context}: duplicate id (also used by '{seen[dto.Id]}').");
            result.Add(dto.ToActionInfo(context));
        }
        return result;
    }
}

internal sealed class ActionDto
{
    public uint Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<string> Jobs { get; set; } = [];

    public int Level { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    public bool Gcd { get; set; }

    public double Cast { get; set; }

    public double Recast { get; set; }

    public int CooldownGroup { get; set; }

    public int? AdditionalCooldownGroup { get; set; }

    public Dictionary<int, double>? RecastByLevel { get; set; }

    public Dictionary<int, int>? Charges { get; set; }

    public CostDto? Cost { get; set; }

    public int? Mp { get; set; }

    public uint? RequiresStatus { get; set; }

    public Dictionary<int, double>? Duration { get; set; }

    /// <summary>Main duration at the sync levels 80/90/100; null = not usable there. Redundant with <see cref="Duration"/> and the levels (checked by tests).</summary>
    public Dictionary<int, double?> Sync { get; set; } = [];

    public string? When { get; set; }

    public double? Lead { get; set; }

    public int? Lb { get; set; }

    public List<EffectDto>? Effects { get; set; }

    public RefDto? ReplacedBy { get; set; }

    public RefDto? UpgradeOf { get; set; }

    public List<RefDto>? FollowUps { get; set; }

    public RefDto? FollowUpOf { get; set; }

    public string? Requires { get; set; }

    public List<RefDto>? Related { get; set; }

    public int Icon { get; set; }

    public string? Notes { get; set; }

    public ActionInfo ToActionInfo(string context)
    {
        if (this.Jobs == null || this.Jobs.Count == 0)
            throw new FormatException($"{context}: 'jobs' is missing.");
        var jobs = new List<Job>(this.Jobs.Count);
        foreach (var text in this.Jobs)
        {
            if (!MitCue.Core.Jobs.TryParse(text, out var job))
                throw new FormatException($"{context}: unknown job '{text}' in 'jobs'.");
            if (!jobs.Contains(job))
                jobs.Add(job);
        }

        if (this.ReplacedBy is { } replaced && replaced.Level is null)
            throw new FormatException($"{context}: 'replaced_by' needs the 'level' of the upgrade.");

        return new ActionInfo
        {
            Id = this.Id,
            Name = this.Name.Trim(),
            Jobs = jobs,
            Level = this.Level,
            Kind = ParseKind(this.Kind, context),
            Target = ParseTarget(this.Target, context),
            IsGcd = this.Gcd,
            CastTime = this.Cast,
            Recast = this.Recast,
            CooldownGroup = this.CooldownGroup,
            AdditionalCooldownGroup = this.AdditionalCooldownGroup is 0 ? null : this.AdditionalCooldownGroup,
            RecastByLevel = this.RecastByLevel ?? [],
            ChargesByLevel = this.Charges ?? [],
            Cost = this.Cost is { } cost ? new ResourceCost(cost.Resource, cost.Amount) : null,
            MpCost = this.Mp,
            RequiresStatus = this.RequiresStatus,
            DurationByLevel = this.Duration ?? [],
            When = ParseWhen(this.When, context),
            Lead = this.Lead,
            LimitBreakTier = this.Lb,
            Effects = this.Effects?.Select(e => new ActionEffect(e.Name, e.Duration ?? [])).ToList() ?? [],
            ReplacedBy = this.ReplacedBy?.Id,
            ReplacedAtLevel = this.ReplacedBy?.Level,
            UpgradeOf = this.UpgradeOf?.Id,
            FollowUpOf = this.FollowUpOf?.Id,
            FollowUps = this.FollowUps?.Select(r => new ActionLink(r.Id, r.Name, r.When)).ToList() ?? [],
            Related = this.Related?.Select(r => new ActionLink(r.Id, r.Name, r.What)).ToList() ?? [],
            Requires = this.Requires,
            Icon = this.Icon > 0 ? (uint)this.Icon : 0,
            Notes = this.Notes,
        };
    }

    /// <summary>snake_case kind → <see cref="ActionKind"/> (<c>party_mit</c> → PartyMit, <c>lb</c> → LimitBreak).</summary>
    internal static ActionKind ParseKind(string? text, string context)
    {
        var compact = Compact(text);
        if (compact == "lb")
            return ActionKind.LimitBreak;
        if (compact.Length > 0 && char.IsLetter(compact[0]) && Enum.TryParse<ActionKind>(compact, ignoreCase: true, out var kind))
            return kind;
        throw new FormatException(
            $"{context}: unknown kind '{text}' (expected party_mit, shield, regen, heal, tank_mit, invuln, buff, lb, movement or other).");
    }

    internal static ActionTarget ParseTarget(string? text, string context)
    {
        var compact = Compact(text);
        if (compact.Length > 0 && char.IsLetter(compact[0]) && Enum.TryParse<ActionTarget>(compact, ignoreCase: true, out var target) &&
            target != ActionTarget.Unknown)
            return target;
        throw new FormatException($"{context}: unknown target '{text}' (expected self, single, party, ground or enemy).");
    }

    internal static UseTiming ParseWhen(string? text, string context) => Compact(text) switch
    {
        "" => UseTiming.Default,
        "before" => UseTiming.Before,
        "after" => UseTiming.After,
        _ => throw new FormatException($"{context}: unknown 'when' value '{text}' (expected before or after)."),
    };

    /// <summary>Lowercase letters and digits only: "party_mit" → "partymit".</summary>
    private static string Compact(string? text) =>
        text == null ? string.Empty : new string(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}

internal sealed class CostDto
{
    public string Resource { get; set; } = string.Empty;

    public int Amount { get; set; }
}

/// <summary>A reference to another action: replaced_by {id, name, level, trait}, follow_ups {id, name, when}, related {id, name, what}.</summary>
internal sealed class RefDto
{
    public uint Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int? Level { get; set; }

    public string? Trait { get; set; }

    public string? When { get; set; }

    public string? What { get; set; }
}

internal sealed class EffectDto
{
    public string Name { get; set; } = string.Empty;

    public Dictionary<int, double>? Duration { get; set; }
}
