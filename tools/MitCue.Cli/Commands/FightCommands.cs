using MitCue.Core.Fights;

namespace MitCue.Cli.Commands;

/// <summary><c>fights</c> and <c>mechanics &lt;fight&gt;</c>.</summary>
internal static class FightCommands
{
    public static CommandSpec Fights { get; } = new()
    {
        Name = "fights",
        Synopsis = string.Empty,
        Summary = "List the fights: key, name, territory, sync level, phases, mechanics and timeline file.",
        MaxArgs = 0,
        Examples = ["fights"],
        Run = RunFights,
    };

    public static CommandSpec Mechanics { get; } = new()
    {
        Name = "mechanics",
        Synopsis = "<fight> [--phase <id>] [--kind <kind>] [--find <text>]",
        Summary = "List a fight's phases and mechanics: id, phase, hit (phase m:ss and timeline seconds), kind, name, entry, hits, aliases.",
        Details = """
            The hit is shown relative to the start of the mechanic's phase (m:ss.f) and as timeline seconds. The second
            line of a mechanic gives the cactbot entry it is checked against, every hit of a multi-hit mechanic, and the
            aliases plans may use for it.
            """,
        Options =
        [
            new("phase", "id", "Only this phase, e.g. p3."),
            new("kind", "kind", "Only this kind: raidwide, tankbuster, multihit, stack, spread, mixed, lb_check, enrage, other, marker."),
            new("find", "text", "Only mechanics whose id, name, entry or an alias contains the text (case-insensitive)."),
        ],
        MinArgs = 1,
        MaxArgs = 1,
        Examples = ["mechanics top", "mechanics top --phase p6", "mechanics top --find \"cosmo memory\""],
        Run = RunMechanics,
    };

    private static int RunFights(CliContext ctx, ParsedArgs args)
    {
        var fights = ctx.Fights.Fights;
        var table = new Table("key", "name", ">territory", ">level", ">phases", ">mechanics", "timeline");
        foreach (var f in fights)
            table.Add(f.Key, f.Name, Text.Int(f.Territory), Text.Int(f.SyncLevel), Text.Int(f.Phases.Count), Text.Int(f.Mechanics.Count), f.TimelineFile);
        ctx.Out.WriteLine($"{Text.Count(fights.Count, "fight")}:");
        table.Write(ctx.Out);
        return ExitCodes.Ok;
    }

    private static int RunMechanics(CliContext ctx, ParsedArgs args)
    {
        var fight = ctx.Fight(args.Positionals[0]);
        var phaseFilter = args.Get("phase");
        if (phaseFilter != null && fight.GetPhase(phaseFilter) == null)
            throw new UsageException($"mechanics: {fight.Key} has no phase '{phaseFilter}'. Phases: {string.Join(", ", fight.Phases.Select(p => p.Id))}.");
        MechanicKind? kindFilter = null;
        if (args.Get("kind") is { } kindText)
        {
            if (!FightLoader.TryParseKind(kindText, out var kind))
                throw new UsageException($"mechanics: unknown kind '{kindText}'. Kinds: {string.Join(", ", Enum.GetValues<MechanicKind>().Select(FightLoader.KindName))}.");
            kindFilter = kind;
        }

        var find = args.Get("find")?.Trim();

        ctx.Out.WriteLine($"{fight.Key.ToUpperInvariant()}  {fight.Name} · territory {fight.Territory} · level {fight.SyncLevel} · timeline {fight.TimelineFile}");
        ctx.Out.WriteLine();
        ctx.Out.WriteLine("Phases:");
        var phases = new Table("id", "name", ">start");
        foreach (var p in fight.Phases)
            phases.Add(p.Id, p.Name, Text.Sec(p.Start));
        phases.Write(ctx.Out, "  ", header: false);
        if (fight.Calibration.Count > 0)
        {
            ctx.Out.WriteLine();
            ctx.Out.WriteLine("Calibration presets (timeline seconds of each phase's sheet 0:00):");
            foreach (var (key, zeros) in fight.Calibration.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                ctx.Out.WriteLine($"  {key}: {string.Join(", ", zeros.Select(z => $"{z.Key} {Text.Sec(z.Value)}"))}");
        }

        var mechanics = fight.Mechanics
            .Where(m => phaseFilter == null || string.Equals(m.Phase, phaseFilter, StringComparison.OrdinalIgnoreCase))
            .Where(m => kindFilter == null || m.Kind == kindFilter)
            .Where(m => string.IsNullOrEmpty(find) || Matches(m, find))
            .ToList();

        ctx.Out.WriteLine();
        ctx.Out.WriteLine($"Mechanics ({mechanics.Count}{(mechanics.Count != fight.Mechanics.Count ? " of " + fight.Mechanics.Count : string.Empty)}):");
        if (mechanics.Count == 0)
            return ExitCodes.Ok;

        var rows = mechanics.Select(m => new[]
        {
            m.Id,
            m.Phase,
            fight.GetPhase(m.Phase) is { } phase ? Text.Clock(m.Hit - phase.Start, 1) : "?",
            Text.Sec(m.Hit),
            FightLoader.KindName(m.Kind),
            m.Name,
        }).ToList();
        var headers = new[] { "id", "phase", "hit", "timeline", "kind", "name" };
        var right = new[] { false, false, true, true, false, false };
        var widths = headers.Select((h, i) => Math.Max(h.Length, rows.Max(r => r[i].Length))).ToArray();
        string Line(string[] cells) => "  " + string.Join("  ", cells.Select((c, i) => i == cells.Length - 1 ? c : right[i] ? c.PadLeft(widths[i]) : c.PadRight(widths[i]))).TrimEnd();
        var indent = new string(' ', 2 + widths.Take(widths.Length - 1).Sum() + (2 * (widths.Length - 1)));

        ctx.Out.WriteLine(Line(headers));
        for (var i = 0; i < mechanics.Count; i++)
        {
            var m = mechanics[i];
            ctx.Out.WriteLine(Line(rows[i]));
            var details = new List<string>();
            if (m.Entry != null)
                details.Add($"entry \"{m.Entry}\"");
            if (m.Hits.Count > 1)
                details.Add($"{m.Hits.Count} hits: {string.Join(", ", m.Hits.Select(Text.Sec))}");
            if (m.Aliases.Count > 0)
                details.Add("aliases: " + string.Join(", ", m.Aliases.Select(a => "\"" + a + "\"")));
            if (details.Count > 0)
                ctx.Out.WriteLine(indent + string.Join(" · ", details));
        }

        return ExitCodes.Ok;
    }

    private static bool Matches(Mechanic m, string text) =>
        m.Id.Contains(text, StringComparison.OrdinalIgnoreCase)
        || m.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
        || (m.Entry?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
        || m.Aliases.Any(a => a.Contains(text, StringComparison.OrdinalIgnoreCase));
}
