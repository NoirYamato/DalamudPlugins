using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AnoMech.Tests;

// Points the report at the line of the plugin's source that holds a value, found by text so a moved line still
// resolves, and reads an event time straight from a world.Events.Add line so the report follows the timeline's edits.
internal static partial class SimSource
{
    private static readonly Lazy<string?> Root = new(FindRoot);
    private static readonly ConcurrentDictionary<string, string[]> Files = [];

    // "path:line"; the line is "?" when the text is gone.
    public static string Line(string path, string text, int occurrence = 1)
        => $"{path}:{(Find(path, text, occurrence) is { } found ? found.Number.ToString(CultureInfo.InvariantCulture) : "?")}";

    // The time of the occurrence-th world.Events.Add line holding the text.
    public static (string Where, float? Seconds) EventTime(string path, string text, int occurrence = 1)
    {
        var found = Find(path, text, occurrence);
        var match = found is null ? null : EventsAdd().Match(found.Value.Text);
        var seconds = match is { Success: true } ? float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : (float?)null;
        return (Line(path, text, occurrence), seconds);
    }

    private static (int Number, string Text)? Find(string path, string text, int occurrence)
    {
        var lines = Files.GetOrAdd(path, Read);
        var seen = 0;
        for (var i = 0; i < lines.Length; i++)
            if (lines[i].Contains(text, StringComparison.Ordinal) && ++seen == occurrence)
                return (i + 1, lines[i]);
        return null;
    }

    private static string[] Read(string path)
        => Root.Value is { } root && Path.Combine(root, path) is var full && File.Exists(full) ? File.ReadAllLines(full) : [];

    // The checkout the tests were built in: a run from elsewhere finds no source and leaves lines and times unknown.
    private static string? FindRoot()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "AnoMech.sln")))
                return dir.FullName;
        return null;
    }

    [GeneratedRegex(@"Events\.Add\(\s*(\d+(?:\.\d+)?)f")]
    private static partial Regex EventsAdd();
}
