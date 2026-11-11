using MitCue.Core.Timelines;

namespace MitCue.Cli.Commands;

/// <summary><c>timeline &lt;fight|file.txt&gt; [--syncs]</c>: a parsed cactbot timeline.</summary>
internal static class TimelineCommand
{
    public static CommandSpec Spec { get; } = new()
    {
        Name = "timeline",
        Synopsis = "<fight|file.txt> [--syncs]",
        Summary = "Dump a parsed cactbot timeline: entries with their syncs, windows and jumps, labels and parser warnings.",
        Details = """
            The argument is a fight key (its timeline from the data folder) or a timeline file. Entries are listed in file
            order with their line numbers, as cactbot keeps them. A sync window is shown when it is not cactbot's default
            2.5,2.5 (seconds before,after the entry's time); "jump 0" stops the timeline (a reset).
            """,
        Options = [new("syncs", null, "List only the entries with a sync (what the sync engine matches events against).")],
        MinArgs = 1,
        MaxArgs = 1,
        Examples = ["timeline top", "timeline top --syncs", "timeline data/timelines/dancing_mad.txt"],
        Run = Run,
    };

    private static int Run(CliContext ctx, ParsedArgs args)
    {
        var target = args.Positionals[0];
        var full = ctx.ResolvePath(target);
        Timeline timeline;
        string? fightKey = null;
        if (File.Exists(full))
        {
            timeline = TimelineParser.Parse(File.ReadAllText(full), Path.GetFileName(full));
        }
        else if (ctx.Fights.Get(target) is { } fight)
        {
            timeline = ctx.Fights.LoadTimeline(fight);
            fightKey = fight.Key;
        }
        else
        {
            throw new UsageException($"timeline: '{target}' is neither a fight ({string.Join(", ", ctx.Fights.Fights.Select(f => f.Key))}) nor a file.");
        }

        var syncs = timeline.Entries.Count(e => e.Sync != null);
        var forceJumps = timeline.Entries.Count(e => e.ForceJump);
        var jumps = timeline.Entries.Count(e => e.JumpTime != null || e.JumpLabel != null);
        ctx.Out.WriteLine(
            $"{timeline.Name}{(fightKey != null ? $" (fight {fightKey})" : string.Empty)}: {Text.Count(timeline.Entries.Count, "entry", "entries")}, " +
            $"{syncs} with a sync, {Text.Count(jumps, "jump")} ({forceJumps} forced), {Text.Count(timeline.Labels.Count, "label")}; ends at {Text.Sec(timeline.EndTime)}");
        if (timeline.ZoneId is { } zone)
            ctx.Out.WriteLine($"Zone: {zone}");
        if (timeline.PhaseAnchors.Count > 0)
            ctx.Out.WriteLine("Phase anchors (# -p): " + string.Join(", ", timeline.PhaseAnchors.Select(a => $"{a.AbilityId} at {Text.Sec(a.Time)}")));
        if (timeline.HiddenNames.Count > 0)
            ctx.Out.WriteLine("Hidden (hideall): " + string.Join(", ", timeline.HiddenNames.Order(StringComparer.Ordinal).Select(n => "\"" + n + "\"")));

        var shown = args.Has("syncs") ? timeline.Entries.Where(e => e.Sync != null).ToList() : timeline.Entries.ToList();
        ctx.Out.WriteLine();
        ctx.Out.WriteLine(args.Has("syncs") ? $"Entries with a sync ({shown.Count}):" : $"Entries ({shown.Count}):");
        var table = new Table(">line", ">time", "name", "sync", "window", "jump");
        foreach (var e in shown)
        {
            var name = "\"" + e.Name + "\"" + (e.IsHidden ? " (hidden)" : string.Empty) + (e.Duration is { } d ? $" duration {Text.Num(d)}" : string.Empty);
            string sync;
            if (e.Sync == null)
                sync = string.Empty;
            else if (e.Sync.LegacyRegex != null)
                sync = e.Sync + " (old-style, never matches)";
            else
                sync = e.Sync.ToString();
            var window = e.Sync != null && e.Window != SyncWindow.Default ? $"{Text.Num(e.Window.Before)},{Text.Num(e.Window.After)}" : string.Empty;
            table.Add(Text.Int(e.Line), Text.Sec(e.Time), name, sync, window, Jump(e));
        }

        table.Write(ctx.Out);

        ctx.Out.WriteLine();
        if (timeline.Labels.Count > 0)
        {
            ctx.Out.WriteLine($"Labels ({timeline.Labels.Count}):");
            var labels = new Table("label", ">time");
            foreach (var (label, time) in timeline.Labels.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
                labels.Add("\"" + label + "\"", Text.Sec(time));
            labels.Write(ctx.Out, "  ", header: false);
        }
        else
        {
            ctx.Out.WriteLine("Labels: none");
        }

        if (timeline.Warnings.Count > 0)
        {
            ctx.Out.WriteLine($"Warnings ({timeline.Warnings.Count}):");
            foreach (var w in timeline.Warnings)
                ctx.Out.WriteLine("  " + w);
        }
        else
        {
            ctx.Out.WriteLine("Warnings: none");
        }

        return ExitCodes.Ok;
    }

    private static string Jump(TimelineEntry e)
    {
        if (e.JumpTime == null && e.JumpLabel == null)
            return string.Empty;
        var verb = e.ForceJump ? "forcejump" : "jump";
        var target = e.JumpLabel != null ? $"\"{e.JumpLabel}\"" : string.Empty;
        if (e.JumpTime is not { } time)
            return $"{verb} {target} (unknown label)";
        var at = time == 0 ? "0 (stop)" : Text.Sec(time);
        return e.JumpLabel != null ? $"{verb} {target} = {at}" : $"{verb} {at}";
    }
}
