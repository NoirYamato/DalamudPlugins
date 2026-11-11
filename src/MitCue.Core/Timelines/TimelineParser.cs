namespace MitCue.Core.Timelines;

/// <summary>
/// Parses cactbot timeline text (format: docs/research/cactbot-timelines.md, reference
/// implementation cactbot ui/raidboss/timeline_parser.ts).
/// </summary>
public static class TimelineParser
{
    /// <summary>
    /// Parses a timeline. Never throws for unknown syntax: unsupported lines are skipped and
    /// reported in <see cref="Timeline.Warnings"/>. Throws <see cref="FormatException"/> only
    /// for input that is not a timeline at all (e.g. no entries).
    /// </summary>
    public static Timeline Parse(string text, string name = "timeline") =>
        throw new NotImplementedException();
}
