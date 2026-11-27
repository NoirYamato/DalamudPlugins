using System.Text;
using System.Text.RegularExpressions;

namespace AnoMech.Tests;

// The rename: whatever a player reads names the plugin through Plugin.DisplayName, and AnoMech stays only where NoirMech
// keeps it on purpose. So every string literal in the plugin's source that names AnoMech has to be one of those, and an
// upstream release that brings new text naming AnoMech fails here until it is renamed or kept.
public class PluginDisplayNameTests
{
    private static readonly (string Kept, Regex Text)[] KeptNames =
    [
        ("an ImGui id", new(@"##AnoMech")),
        ("a log file's name, which the log rotation reads", new(@"^AnoMech-")),
        ("a log's header", new(@"^(# AnoMech |AnoMech damage debug dump )")),
        ("a log line's tag, its class's name", new(@"^\[AnoMechWatch\] ")),
        ("a texture's internal name", new(@"^AnoMech\.DamageDebug$")),
        ("a relay protocol header", new(@"^X-AnoMech-")),
        ("upstream's repository, home of the relay host NoirMech players use", new(@"^https://github\.com/anomek/AnoMech/")),
    ];

    // AnoMech itself: the plugin NoirMech refuses to run beside, its InternalName and the reasons that name it.
    private const string ThePresence = "AnoMechPresence.cs";

    [Test]
    public void EveryStringThatNamesAnoMechIsOneTheRenameKeeps()
    {
        var plugin = Path.Combine(RepoRoot(), "AnoMech");
        var files = Directory.EnumerateFiles(plugin, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(plugin, file).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .ToList();
        var named = files
            .Where(file => Path.GetFileName(file) != ThePresence)
            .SelectMany(file => StringLiterals(File.ReadAllText(file))
                .Where(text => text.Contains("AnoMech", StringComparison.Ordinal))
                .Select(text => (File: Path.GetRelativePath(plugin, file), Text: text)))
            .ToList();

        Assert.That(files, Has.Count.GreaterThan(400), "the plugin's source files");
        Assert.That(named, Has.Count.GreaterThan(15), "the kept names this reads, so a scan that finds none can't pass");
        Assert.That(named.Where(literal => !KeptNames.Any(kept => kept.Text.IsMatch(literal.Text))).Select(literal => $"{literal.File}: \"{literal.Text}\""),
            Is.Empty, "user-facing text names the plugin through Plugin.DisplayName");
    }

    [Test]
    public void TheScanReadsEveryLiteralAndOnlyLiterals()
    {
        const string source = """"
            // "AnoMech" in a comment
            /* "AnoMech" in a block */
            var a = "plain AnoMech";
            var b = $"[{Plugin.DisplayName}] {(x ? "inner AnoMech" : "")} {{AnoMech}}";
            var c = @"verbatim ""AnoMech""";
            var d = '"';
            var e = "after the char \" AnoMech";
            """";

        Assert.That(StringLiterals(source), Is.EqualTo(new[]
        {
            "plain AnoMech", "inner AnoMech", "", "[ ]   {AnoMech}", "verbatim \"AnoMech\"", "after the char \\\" AnoMech",
        }));
    }

    // The text of every string literal in C# `source`, in the order each ends: an interpolation hole's own literals
    // come before the one they sit in, which keeps a space where each hole was.
    internal static List<string> StringLiterals(string source)
    {
        var found = new List<string>();
        var i = 0;
        ScanCode(source, ref i, found, inHole: false);
        return found;
    }

    private static void ScanCode(string s, ref int i, List<string> found, bool inHole)
    {
        var depth = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '/' && At(s, i + 1, '/')) { i = s.IndexOf('\n', i) is var end and >= 0 ? end : s.Length; continue; }
            if (c == '/' && At(s, i + 1, '*')) { i = s.IndexOf("*/", i + 2, StringComparison.Ordinal) is var end and >= 0 ? end + 2 : s.Length; continue; }
            if (c == '\'') { i = AfterChar(s, i); continue; }
            if (StringStarts(s, i, out var body, out var interpolated, out var verbatim, out var rawQuotes))
            {
                i = body;
                found.Add(ScanString(s, ref i, found, interpolated, verbatim, rawQuotes));
                continue;
            }
            if (inHole && c == '{') depth++;
            if (inHole && c == '}' && depth-- == 0) { i++; return; }
            i++;
        }
    }

    private static string ScanString(string s, ref int i, List<string> found, bool interpolated, bool verbatim, int rawQuotes)
    {
        var text = new StringBuilder();
        while (i < s.Length)
        {
            var c = s[i];
            if (rawQuotes > 0)
            {
                if (Quotes(s, i) >= rawQuotes) { i += rawQuotes; return text.ToString(); }
            }
            else if (c == '"')
            {
                if (verbatim && At(s, i + 1, '"')) { text.Append('"'); i += 2; continue; }
                i++;
                return text.ToString();
            }
            else if (!verbatim && c == '\\')
            {
                text.Append(s, i, Math.Min(2, s.Length - i));
                i += 2;
                continue;
            }
            if (interpolated && (c is '{' or '}') && At(s, i + 1, c)) { text.Append(c); i += 2; continue; }
            if (interpolated && c == '{')
            {
                i++;
                ScanCode(s, ref i, found, inHole: true);
                text.Append(' ');
                continue;
            }
            text.Append(c);
            i++;
        }
        return text.ToString();
    }

    // At `i`: $ or @ prefixes, then the opening quotes; three or more open a raw string.
    private static bool StringStarts(string s, int i, out int body, out bool interpolated, out bool verbatim, out int rawQuotes)
    {
        var j = i;
        while (j < s.Length && s[j] is '$' or '@') j++;
        var prefix = s[i..j];
        interpolated = prefix.Contains('$');
        verbatim = prefix.Contains('@');
        var quotes = Quotes(s, j);
        rawQuotes = !verbatim && quotes >= 3 ? quotes : 0;
        body = j + (rawQuotes > 0 ? quotes : 1);
        if (quotes == 0 || (prefix.Length > 0 && i > 0 && (char.IsLetterOrDigit(s[i - 1]) || s[i - 1] == '_'))) return false;
        return true;
    }

    private static int AfterChar(string s, int i)
    {
        var j = i + 1;
        if (At(s, j, '\\')) j += 2;
        else j++;
        while (j < s.Length && s[j] != '\'' && s[j] != '\n') j++;
        return Math.Min(j + 1, s.Length);
    }

    private static int Quotes(string s, int i)
    {
        var n = 0;
        while (i + n < s.Length && s[i + n] == '"') n++;
        return n;
    }

    private static bool At(string s, int i, char c) => i < s.Length && s[i] == c;

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "AnoMech.sln")))
                return dir.FullName;
        throw new InvalidOperationException("No checkout above the test directory.");
    }
}
