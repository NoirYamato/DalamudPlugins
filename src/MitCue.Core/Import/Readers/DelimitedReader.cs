using System.Text;

namespace MitCue.Core.Import;

/// <summary>
/// Parser for delimited text (docs/research/sheets-import.md §3.4).
/// <list type="bullet">
/// <item><b>Tab</b> (Google Sheets / Excel clipboard): the writer quotes a field (doubling inner quotes) only when it
/// contains a line break or a tab, and writes every other field raw, even one that starts with a quote. So a field
/// starting with <c>"</c> is read as quoted only if a closing quote follows it directly before a tab/line end/end of
/// text <b>and</b> the unescaped body contains a line break or tab; otherwise it is read raw.</item>
/// <item><b>Any other separator</b> (CSV): RFC 4180. A field that starts with <c>"</c> is quoted; <c>""</c> is an
/// escaped quote; the field may contain separators and line breaks. Characters after the closing quote and before the
/// next separator are kept (lenient). A quote that is never closed does not start a quoted field: the field is read
/// raw, like the tab rule does, so the rest of the text keeps its rows.</item>
/// </list>
/// Rows end at <c>\r\n</c>, <c>\n</c> or <c>\r</c>; line breaks inside quoted fields become <c>\n</c>. A trailing line
/// break does not add a row. A leading BOM is skipped.
/// </summary>
internal static class DelimitedReader
{
    public static SheetTab ReadTab(string text, char separator, string tabName)
    {
        ArgumentNullException.ThrowIfNull(text);
        var grid = Parse(text, separator);
        var rows = new List<IReadOnlyList<SheetCell>>(grid.Count);
        foreach (var row in grid)
            rows.Add(row.Select(f => f.Length == 0 ? SheetCell.Empty : new SheetCell { Text = f }).ToList());
        return new SheetTab { Name = tabName, Rows = rows };
    }

    public static List<List<string>> Parse(string text, char separator)
    {
        if (separator is '"' or '\r' or '\n')
            throw new ArgumentException("The separator cannot be a quote or a line break.", nameof(separator));
        var rows = new List<List<string>>();
        var i = text.Length > 0 && text[0] == '\uFEFF' ? 1 : 0;
        var n = text.Length;
        if (i >= n)
            return rows;

        var row = new List<string>();
        var google = separator == '\t';
        while (true)
        {
            string field;
            if (i < n && text[i] == '"' && (google ? TryGoogleQuoted(text, i, separator, out field, out var next) : TryRfcQuoted(text, i, separator, out field, out next)))
            {
                i = next;
            }
            else
            {
                var start = i;
                while (i < n && text[i] != separator && text[i] != '\r' && text[i] != '\n')
                    i++;
                field = text[start..i];
            }

            row.Add(field);
            if (i >= n)
            {
                rows.Add(row);
                return rows;
            }

            if (text[i] == separator)
            {
                i++;
                if (i >= n)
                {
                    row.Add(string.Empty); // "a,b," ends with an empty field
                    rows.Add(row);
                    return rows;
                }

                continue;
            }

            // End of row: \r\n, \n or \r.
            i += text[i] == '\r' && i + 1 < n && text[i + 1] == '\n' ? 2 : 1;
            rows.Add(row);
            row = [];
            if (i >= n)
                return rows; // a trailing line break does not start a row
        }
    }

    /// <summary>Google's clipboard rule: quoted only if the body holds a line break or tab and the closing quote ends the field.</summary>
    private static bool TryGoogleQuoted(string s, int i, char separator, out string value, out int next)
    {
        var sb = new StringBuilder();
        var j = i + 1;
        while (j < s.Length)
        {
            if (s[j] == '"')
            {
                if (j + 1 < s.Length && s[j + 1] == '"')
                {
                    sb.Append('"');
                    j += 2;
                    continue;
                }

                var closes = j + 1 >= s.Length || s[j + 1] == separator || s[j + 1] is '\r' or '\n';
                if (closes && sb.ToString().AsSpan().IndexOfAny('\n', '\r', '\t') >= 0)
                {
                    value = NormalizeBreaks(sb.ToString());
                    next = j + 1;
                    return true;
                }

                break;
            }

            sb.Append(s[j]);
            j++;
        }

        value = string.Empty;
        next = i;
        return false;
    }

    /// <summary>RFC 4180 quoted field; text between the closing quote and the next separator is appended raw.</summary>
    private static bool TryRfcQuoted(string s, int i, char separator, out string value, out int next)
    {
        var sb = new StringBuilder();
        var j = i + 1;
        while (j < s.Length)
        {
            if (s[j] == '"')
            {
                if (j + 1 < s.Length && s[j + 1] == '"')
                {
                    sb.Append('"');
                    j += 2;
                    continue;
                }

                j++; // closing quote
                while (j < s.Length && s[j] != separator && s[j] != '\r' && s[j] != '\n')
                    sb.Append(s[j++]);
                value = NormalizeBreaks(sb.ToString());
                next = j;
                return true;
            }

            sb.Append(s[j]);
            j++;
        }

        // Unterminated quote: not a quoted field; the caller reads it raw up to the next separator or line break.
        value = string.Empty;
        next = i;
        return false;
    }

    private static string NormalizeBreaks(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
