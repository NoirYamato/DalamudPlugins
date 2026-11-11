using System.Text;
using MitCue.Core.Data;
using YamlDotNet.RepresentationModel;

namespace MitCue.Core.Fights;

/// <summary>Reads the fight YAML schema documented in docs/DESIGN.md §3.1.</summary>
/// <remarks>
/// <para>Keys are snake_case; unknown keys are ignored so fight files can carry extra notes.</para>
/// <para>
/// Required: <c>fight</c>, <c>name</c>, <c>territory</c>, <c>sync_level</c>, <c>timeline</c>; per phase <c>id</c> and
/// <c>start</c>; per mechanic <c>id</c> and <c>hit</c> (or <c>hits</c>). A mechanic without <c>phase</c> belongs to the
/// phase containing its hit; without <c>name</c> it is named after its <c>entry</c> (or id); without <c>hits</c> it has
/// the single hit <c>[hit]</c>; without <c>kind</c> it is <see cref="MechanicKind.Other"/>.
/// </para>
/// <para>
/// Checked: unique phase and mechanic ids (case-insensitive), mechanics only reference defined phases, <c>hits</c> are
/// in time order and the first of them is <c>hit</c>. Mechanics are returned sorted by hit time (ties keep file order).
/// </para>
/// <para>
/// Optional sheet presets: <c>calibration</c> (sheet key → phase id → timeline seconds of sheet 0:00) and
/// <c>row_offsets</c> (sheet key → mechanic id → seconds from the mechanic's first hit to the event the sheet's row time
/// marks, see <see cref="FightDefinition.RowOffsets"/>; the mechanic ids must exist).
/// </para>
/// </remarks>
public static class FightLoader
{
    private static readonly string KindNames = string.Join(", ", Enum.GetNames<MechanicKind>().Select(ToSnakeCase));

    /// <summary>Parses one fight YAML document. Throws <see cref="FormatException"/> with a helpful message on invalid data.</summary>
    /// <param name="yaml">The document.</param>
    /// <param name="sourceName">File name for messages (e.g. "fights/top.yaml"); optional.</param>
    public static FightDefinition Load(string yaml, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        var root = YamlNodes.LoadMapping(yaml, sourceName ?? "fight file");

        var key = YamlNodes.RequireString(root, "fight", sourceName ?? "fight file");
        var context = sourceName ?? $"fight '{key}'";

        var territory = YamlNodes.RequireInteger(root, "territory", context);
        if (territory is < 1 or > ushort.MaxValue)
            throw new FormatException($"{context}: 'territory' must be a TerritoryType id between 1 and {ushort.MaxValue}, not {territory}.");
        var syncLevel = YamlNodes.RequireInteger(root, "sync_level", context);
        if (syncLevel is < 1 or > 200)
            throw new FormatException($"{context}: 'sync_level' must be a level such as 80, 90 or 100, not {syncLevel}.");

        var phases = ReadPhases(root, context);
        var mechanics = ReadMechanics(root, context, phases);

        return new FightDefinition
        {
            Key = key,
            Name = YamlNodes.RequireString(root, "name", context),
            Territory = (ushort)territory,
            SyncLevel = (int)syncLevel,
            TimelineFile = YamlNodes.RequireString(root, "timeline", context),
            Phases = phases,
            Mechanics = mechanics.OrderBy(m => m.Hit).ToList(),
            Calibration = ReadCalibration(root, context),
            RowOffsets = ReadRowOffsets(root, context, mechanics),
        };
    }

    /// <summary>Parses a mechanic kind written in snake_case ("lb_check"); also accepts PascalCase and dashes/spaces.</summary>
    public static bool TryParseKind(string? text, out MechanicKind kind)
    {
        kind = MechanicKind.Other;
        var compact = new string((text ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
        return compact.Length > 0 && char.IsLetter(compact[0]) &&
               Enum.TryParse(compact, ignoreCase: true, out kind) && Enum.IsDefined(kind);
    }

    /// <summary>The snake_case spelling used in fight files ("LbCheck" → "lb_check").</summary>
    public static string KindName(MechanicKind kind) => ToSnakeCase(kind.ToString());

    private static List<Phase> ReadPhases(YamlMappingNode root, string context)
    {
        var phases = new List<Phase>();
        var lines = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var list = YamlNodes.GetList(root, "phases", context);
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is not YamlMappingNode node)
                throw new FormatException($"{context}: phases[{i}] must be a mapping such as {{id: p1, name: \"P1\", start: 0}} ({YamlNodes.Where(list[i])}).");
            var id = YamlNodes.RequireString(node, "id", $"{context}: phases[{i}]");
            var phaseContext = $"{context}: phase '{id}'";
            if (lines.TryGetValue(id, out var firstLine))
                throw new FormatException($"{phaseContext}: duplicate phase id ({YamlNodes.Where(node)}; first defined on line {firstLine}).");
            lines[id] = node.Start.Line;

            phases.Add(new Phase
            {
                Id = id,
                Name = YamlNodes.GetString(node, "name", phaseContext)?.Trim() is { Length: > 0 } name ? name : id,
                Start = YamlNodes.RequireDouble(node, "start", phaseContext),
            });
        }
        return phases;
    }

    private static List<Mechanic> ReadMechanics(YamlMappingNode root, string context, List<Phase> phases)
    {
        var mechanics = new List<Mechanic>();
        var lines = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var phaseIds = new HashSet<string>(phases.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
        var list = YamlNodes.GetList(root, "mechanics", context);
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is not YamlMappingNode node)
                throw new FormatException($"{context}: mechanics[{i}] must be a mapping ({YamlNodes.Where(list[i])}).");
            var id = YamlNodes.RequireString(node, "id", $"{context}: mechanics[{i}]");
            var m = $"{context}: mechanic '{id}' ({YamlNodes.Where(node)})";
            if (lines.TryGetValue(id, out var firstLine))
                throw new FormatException($"{m}: duplicate mechanic id (first defined on line {firstLine}).");
            lines[id] = node.Start.Line;

            var (hit, hits) = ReadHits(node, m);

            var phase = YamlNodes.GetString(node, "phase", m)?.Trim();
            if (string.IsNullOrEmpty(phase))
            {
                phase = phases.Where(p => p.Start <= hit).OrderBy(p => p.Start).LastOrDefault()?.Id
                    ?? throw new FormatException($"{m}: 'phase' is missing and no phase starts at or before its hit ({hit}).");
            }
            else if (!phaseIds.Contains(phase))
            {
                throw new FormatException($"{m}: unknown phase '{phase}' (phases: {(phaseIds.Count == 0 ? "none defined" : string.Join(", ", phases.Select(p => p.Id)))}).");
            }
            else
            {
                phase = phases.First(p => string.Equals(p.Id, phase, StringComparison.OrdinalIgnoreCase)).Id;
            }

            var kind = MechanicKind.Other;
            if (YamlNodes.GetString(node, "kind", m) is { } kindText && !TryParseKind(kindText, out kind))
                throw new FormatException($"{m}: unknown kind '{kindText}' (expected {KindNames}).");

            var entry = YamlNodes.GetString(node, "entry", m);
            var name = YamlNodes.GetString(node, "name", m)?.Trim();
            mechanics.Add(new Mechanic
            {
                Id = id,
                Name = !string.IsNullOrEmpty(name) ? name : !string.IsNullOrWhiteSpace(entry) ? entry.Trim() : id,
                Phase = phase,
                Hit = hit,
                Hits = hits,
                Entry = string.IsNullOrEmpty(entry) ? null : entry,
                Kind = kind,
                Aliases = YamlNodes.GetStringList(node, "aliases", m),
            });
        }
        return mechanics;
    }

    /// <summary>hit and hits: hits default to [hit]; hit defaults to hits[0]; hits must be in time order and start with hit.</summary>
    private static (double Hit, IReadOnlyList<double> Hits) ReadHits(YamlMappingNode node, string context)
    {
        const double tolerance = 1e-6;
        var hit = YamlNodes.GetDouble(node, "hit", context);
        var hits = YamlNodes.GetDoubleList(node, "hits", context);
        if (hits.Count == 0)
        {
            if (hit is not { } single)
                throw new FormatException($"{context}: 'hit' is missing.");
            return (single, [single]);
        }

        for (var i = 1; i < hits.Count; i++)
        {
            if (hits[i] < hits[i - 1])
                throw new FormatException($"{context}: 'hits' must be in time order ({hits[i - 1]} comes before {hits[i]}).");
        }

        if (hit is { } first && Math.Abs(first - hits[0]) > tolerance)
        {
            throw new FormatException(hits.Any(h => Math.Abs(h - first) <= tolerance)
                ? $"{context}: 'hit' ({first}) must be the first of 'hits' ({hits[0]}): 'hit' is the first damage."
                : $"{context}: 'hit' ({first}) is not one of 'hits' ([{string.Join(", ", hits)}]).");
        }
        return (hits[0], hits);
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> ReadCalibration(YamlMappingNode root, string context)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        var node = YamlNodes.Get(root, "calibration");
        if (node == null)
            return result;
        if (node is not YamlMappingNode presets)
            throw new FormatException($"{context}: 'calibration' must be a mapping of sheet keys to phase zero points ({YamlNodes.Where(node)}).");

        foreach (var (sheet, sheetNode, value) in YamlNodes.Entries(presets))
        {
            var c = $"{context}: calibration '{sheet}'";
            if (YamlNodes.IsNull(value))
                continue;
            if (value is not YamlMappingNode zeros)
                throw new FormatException($"{c}: must be a mapping of phase ids to timeline seconds, e.g. {{p1: 0.0, p2: 202.0}} ({YamlNodes.Where(sheetNode)}).");
            var points = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var (phase, _, zero) in YamlNodes.Entries(zeros))
            {
                if (!YamlNodes.IsNull(zero))
                    points[phase] = YamlNodes.ToDouble(zero, $"the zero point of '{phase}'", c);
            }
            result[sheet] = points;
        }
        return result;
    }

    /// <summary>Optional <c>row_offsets</c>: sheet key → {mechanic id: seconds}; the mechanic ids must exist.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> ReadRowOffsets(YamlMappingNode root, string context, List<Mechanic> mechanics)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        var node = YamlNodes.Get(root, "row_offsets");
        if (node == null || YamlNodes.IsNull(node))
            return result;
        if (node is not YamlMappingNode sheets)
            throw new FormatException($"{context}: 'row_offsets' must be a mapping of sheet keys to {{mechanic id: seconds}} ({YamlNodes.Where(node)}).");

        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mechanics)
            ids[m.Id] = m.Id;
        foreach (var (sheet, sheetNode, value) in YamlNodes.Entries(sheets))
        {
            var c = $"{context}: row_offsets '{sheet}'";
            if (YamlNodes.IsNull(value))
                continue;
            if (value is not YamlMappingNode offsets)
                throw new FormatException($"{c}: must be a mapping of mechanic ids to seconds, e.g. {{p5.blind_faith: -1.2}} ({YamlNodes.Where(sheetNode)}).");
            var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, idNode, seconds) in YamlNodes.Entries(offsets))
            {
                if (!ids.TryGetValue(id, out var canonical))
                    throw new FormatException($"{c}: unknown mechanic '{id}' ({YamlNodes.Where(idNode)}).");
                if (!YamlNodes.IsNull(seconds))
                    map[canonical] = YamlNodes.ToDouble(seconds, $"the row offset of '{id}'", c);
            }
            result[sheet] = map;
        }
        return result;
    }

    private static string ToSnakeCase(string pascal)
    {
        var sb = new StringBuilder(pascal.Length + 4);
        for (var i = 0; i < pascal.Length; i++)
        {
            if (char.IsUpper(pascal[i]) && i > 0)
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(pascal[i]));
        }
        return sb.ToString();
    }
}
