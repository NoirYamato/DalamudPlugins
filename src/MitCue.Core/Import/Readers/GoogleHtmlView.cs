using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MitCue.Core.Import;

/// <summary>
/// A Google spreadsheet's htmlview page (<c>https://docs.google.com/spreadsheets/d/&lt;id&gt;/htmlview</c>), which lists the
/// tab names as Google shows them: one <c>items.push({name: "P1\/P2", ...})</c> per tab, in tab order
/// (docs/research/sheets-import.md §1.2). The xlsx export renames the tabs Excel does not allow ("P1/P2" becomes "P1P2");
/// <see cref="XlsxReadOptions.TabNames"/> restores them. Used by the CLI and the plugin's link import.
/// </summary>
public static partial class GoogleHtmlView
{
    /// <summary>The tab names the page lists, in order, with JavaScript string escapes decoded; empty when it lists none.</summary>
    public static List<string> TabNames(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return TabName().Matches(html).Select(m => UnescapeJs(m.Groups["name"].Value)).ToList();
    }

    /// <summary>Decodes the escapes of a JavaScript string literal (\/ \" \\ \n \uXXXX \xHH ...).</summary>
    private static string UnescapeJs(string s)
    {
        if (!s.Contains('\\', StringComparison.Ordinal))
            return s;
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c != '\\' || i + 1 >= s.Length)
            {
                sb.Append(c);
                continue;
            }

            var e = s[++i];
            if ((e is 'u' or 'x') && TryHex(s, i + 1, e == 'u' ? 4 : 2, out var code))
            {
                sb.Append((char)code);
                i += e == 'u' ? 4 : 2;
                continue;
            }

            sb.Append(e switch { 'n' => '\n', 't' => '\t', 'r' => '\r', 'b' => '\b', 'f' => '\f', _ => e });
        }

        return sb.ToString();
    }

    private static bool TryHex(string s, int start, int length, out int value)
    {
        value = 0;
        return start + length <= s.Length
               && int.TryParse(s.AsSpan(start, length), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }

    // The name runs to the first unescaped quote.
    [GeneratedRegex("""items\.push\(\{name: "(?<name>(?:[^"\\]|\\.)*)""")]
    private static partial Regex TabName();
}
