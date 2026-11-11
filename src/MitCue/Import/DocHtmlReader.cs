using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MitCue.Core.Import;

namespace MitCue.Import;

/// <summary>
/// Turns a Google Docs HTML export (<c>/document/d/&lt;id&gt;/export?format=html</c>, or a published <c>/pub</c> page)
/// into a <see cref="SheetDocument"/>: every top-level table becomes a tab ("Table 1", ...) with its grid (colspan and
/// rowspan become merges; the anchor cell keeps the text), and the paragraphs outside tables become a "Text" tab with
/// one row per paragraph (column A = text, column B = the HTML tag, e.g. "h2", so headings such as "P3" can mark phases).
/// Text only: Docs styles live in CSS classes, so colours are not kept.
/// </summary>
internal static partial class DocHtmlReader
{
    /// <summary>Name of the tab that holds the paragraphs outside tables.</summary>
    public const string TextTabName = "Text";

    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "li", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "pre", "ul", "ol", "hr", "section", "article",
    };

    private static readonly HashSet<string> SkippedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "head", "style", "script", "title", "noscript", "template", "svg",
    };

    /// <summary>Reads the HTML. Never throws on malformed markup (it reads what it can).</summary>
    /// <param name="html">The page.</param>
    /// <param name="title">Document title (from the download); the page's &lt;title&gt; is used when empty.</param>
    public static SheetDocument Read(string html, string? title)
    {
        ArgumentNullException.ThrowIfNull(html);
        var builder = new Builder();
        Scan(html, builder);
        builder.FlushParagraph();
        while (builder.Tables.Count > 0)
            builder.EndTable();

        var tabs = new List<SheetTab>();
        var tableNumber = 0;
        foreach (var table in builder.Finished)
        {
            tableNumber++;
            if (table.Rows.Exists(r => r.Exists(c => !string.IsNullOrWhiteSpace(c.Text))))
                tabs.Add(table.ToTab("Table " + tableNumber.ToString(CultureInfo.InvariantCulture)));
        }

        if (builder.Paragraphs.Count > 0)
        {
            var rows = builder.Paragraphs
                .Select(p => (IReadOnlyList<SheetCell>)[new SheetCell { Text = p.Text }, new SheetCell { Text = p.Tag }])
                .ToList();
            tabs.Add(new SheetTab { Name = TextTabName, Rows = rows });
        }

        var name = string.IsNullOrWhiteSpace(title) ? ExtractTitle(html) : title.Trim();
        return new SheetDocument { Title = string.IsNullOrWhiteSpace(name) ? "Google Doc" : name, Tabs = tabs };
    }

    /// <summary>The text of the page's &lt;title&gt;, or null.</summary>
    public static string? ExtractTitle(string html)
    {
        var m = TitleRegex().Match(html.Length > 64 * 1024 ? html[..(64 * 1024)] : html);
        if (!m.Success)
            return null;
        var text = CollapseSpaces(WebUtility.HtmlDecode(m.Groups[1].Value)).Trim();
        return text.Length == 0 ? null : text;
    }

    private static void Scan(string html, Builder b)
    {
        var i = 0;
        var n = html.Length;
        while (i < n)
        {
            var lt = html.IndexOf('<', i);
            if (lt < 0)
            {
                b.Text(html[i..]);
                break;
            }

            if (lt > i)
                b.Text(html[i..lt]);

            if (string.CompareOrdinal(html, lt, "<!--", 0, 4) == 0)
            {
                var end = html.IndexOf("-->", lt + 4, StringComparison.Ordinal);
                i = end < 0 ? n : end + 3;
                continue;
            }

            var gt = html.IndexOf('>', lt + 1);
            if (gt < 0)
            {
                b.Text(html[lt..]);
                break;
            }

            var tag = html.AsSpan(lt + 1, gt - lt - 1);
            i = gt + 1;
            if (tag.Length == 0 || tag[0] == '!' || tag[0] == '?')
                continue;

            var closing = tag[0] == '/';
            var nameSpan = closing ? tag[1..] : tag;
            var nameEnd = 0;
            while (nameEnd < nameSpan.Length && (char.IsAsciiLetterOrDigit(nameSpan[nameEnd]) || nameSpan[nameEnd] == '-'))
                nameEnd++;
            if (nameEnd == 0)
            {
                b.Text(html[lt..i]);
                continue;
            }

            var name = nameSpan[..nameEnd].ToString().ToLowerInvariant();
            if (!closing && SkippedTags.Contains(name) && !tag.EndsWith("/"))
            {
                // Skip the element's content (CSS, scripts, the head with its title).
                var close = html.IndexOf("</" + name, i, StringComparison.OrdinalIgnoreCase);
                if (close < 0)
                {
                    i = n;
                }
                else
                {
                    var closeEnd = html.IndexOf('>', close);
                    i = closeEnd < 0 ? n : closeEnd + 1;
                }

                continue;
            }

            if (closing)
                b.Close(name);
            else
                b.Open(name, name is "td" or "th" ? tag.ToString() : string.Empty); // img tags can hold megabytes of base64
        }
    }

    private static string CollapseSpaces(string text) => SpacesRegex().Replace(text, " ");

    private static int SpanAttribute(string tag, string attribute)
    {
        var m = SpanRegex().Match(tag);
        while (m.Success)
        {
            if (string.Equals(m.Groups[1].Value, attribute, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                return Math.Clamp(value, 1, 1000);
            m = m.NextMatch();
        }

        return 1;
    }

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"[ \t\r\n\f ]+")]
    private static partial Regex SpacesRegex();

    [GeneratedRegex(@"\b(colspan|rowspan)\s*=\s*[""']?(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SpanRegex();

    private sealed record Paragraph(string Text, string Tag);

    private sealed class CellText
    {
        public CellText(string? text = null)
        {
            if (text != null)
                this.Builder.Append(text);
        }

        public StringBuilder Builder { get; } = new();

        public string Text => Clean(this.Builder.ToString());

        private static string Clean(string raw)
        {
            var lines = raw.Split('\n').Select(l => l.Trim()).ToList();
            var result = new List<string>(lines.Count);
            foreach (var line in lines)
            {
                if (line.Length == 0 && (result.Count == 0 || result[^1].Length == 0))
                    continue;
                result.Add(line);
            }

            while (result.Count > 0 && result[^1].Length == 0)
                result.RemoveAt(result.Count - 1);
            return string.Join('\n', result);
        }
    }

    private sealed class TableBuilder
    {
        private readonly List<int> rowspanLeft = [];
        private List<CellText>? row;
        private int column;

        public List<List<CellText>> Rows { get; } = [];

        public List<CellRange> Merges { get; } = [];

        public CellText? Cell { get; private set; }

        public void StartRow()
        {
            this.EndRow();
            this.row = [];
            this.column = 0;
        }

        public void StartCell(string tag)
        {
            if (this.row == null)
                this.StartRow();
            this.EndCell();

            var row = this.row!;
            this.SkipOccupied(row);

            var colspan = SpanAttribute(tag, "colspan");
            var rowspan = SpanAttribute(tag, "rowspan");
            var r = this.Rows.Count;
            var c = this.column;
            var cell = new CellText();
            row.Add(cell);
            this.SetRowspan(c, rowspan - 1);
            for (var k = 1; k < colspan; k++)
            {
                row.Add(new CellText());
                this.SetRowspan(c + k, rowspan - 1);
            }

            if (colspan > 1 || rowspan > 1)
                this.Merges.Add(new CellRange(r, c, r + rowspan - 1, c + colspan - 1));
            this.column = c + colspan;
            this.Cell = cell;
        }

        public void EndCell() => this.Cell = null;

        public void EndRow()
        {
            this.EndCell();
            if (this.row == null)
                return;

            // Columns still covered by a rowspan from above, after the last cell of this row.
            while (this.column < this.rowspanLeft.Count)
            {
                if (this.rowspanLeft[this.column] > 0)
                {
                    this.rowspanLeft[this.column]--;
                    this.row.Add(new CellText());
                }
                else if (this.rowspanLeft.Skip(this.column).Any(left => left > 0))
                {
                    this.row.Add(new CellText());
                }
                else
                {
                    break;
                }

                this.column++;
            }

            this.Rows.Add(this.row);
            this.row = null;
        }

        public SheetTab ToTab(string name)
        {
            this.EndRow();
            var rows = this.Rows
                .Select(r => (IReadOnlyList<SheetCell>)r.Select(c => c.Text is { Length: > 0 } t ? new SheetCell { Text = t } : SheetCell.Empty).ToList())
                .ToList();
            return new SheetTab { Name = name, Rows = rows, Merges = this.Merges.ToList() };
        }

        private void SkipOccupied(List<CellText> row)
        {
            while (this.column < this.rowspanLeft.Count && this.rowspanLeft[this.column] > 0)
            {
                this.rowspanLeft[this.column]--;
                row.Add(new CellText());
                this.column++;
            }
        }

        private void SetRowspan(int column, int left)
        {
            while (this.rowspanLeft.Count <= column)
                this.rowspanLeft.Add(0);
            this.rowspanLeft[column] = Math.Max(0, left);
        }
    }

    private sealed class Builder
    {
        private readonly StringBuilder paragraph = new();
        private string paragraphTag = "p";

        public List<TableBuilder> Tables { get; } = [];

        public List<TableBuilder> Finished { get; } = [];

        public List<Paragraph> Paragraphs { get; } = [];

        public void Text(string raw)
        {
            if (raw.Length == 0)
                return;
            var text = CollapseSpaces(WebUtility.HtmlDecode(raw));
            if (text.Length == 0)
                return;

            if (this.Tables.Count > 0)
            {
                // Text between cells (whitespace in <tr>) is dropped; nested tables add to the outer cell.
                this.Tables[0].Cell?.Builder.Append(text);
                return;
            }

            this.paragraph.Append(text);
        }

        public void Open(string name, string tag)
        {
            switch (name)
            {
                case "table":
                    if (this.Tables.Count == 0)
                        this.FlushParagraph();
                    else
                        this.CellBreak();
                    this.Tables.Add(new TableBuilder());
                    return;
                case "tr" when this.Tables.Count == 1:
                    this.Tables[0].StartRow();
                    return;
                case "td" or "th" when this.Tables.Count == 1:
                    this.Tables[0].StartCell(tag);
                    return;
                case "td" or "th" when this.Tables.Count > 1:
                    this.Tables[0].Cell?.Builder.Append(' ');
                    return;
                case "br":
                    if (this.Tables.Count > 0)
                        this.CellBreak();
                    else
                        this.FlushParagraph(keepTag: true);
                    return;
            }

            if (BlockTags.Contains(name))
            {
                if (this.Tables.Count > 0)
                {
                    this.CellBreak();
                }
                else
                {
                    this.FlushParagraph();
                    this.paragraphTag = name;
                }
            }
        }

        public void Close(string name)
        {
            switch (name)
            {
                case "table":
                    this.EndTable();
                    return;
                case "tr" when this.Tables.Count == 1:
                    this.Tables[0].EndRow();
                    return;
                case "td" or "th" when this.Tables.Count == 1:
                    this.Tables[0].EndCell();
                    return;
            }

            if (BlockTags.Contains(name))
            {
                if (this.Tables.Count > 0)
                    this.CellBreak();
                else
                    this.FlushParagraph();
            }
        }

        public void EndTable()
        {
            if (this.Tables.Count == 0)
                return;
            var table = this.Tables[^1];
            this.Tables.RemoveAt(this.Tables.Count - 1);
            if (this.Tables.Count == 0)
            {
                table.EndRow();
                this.Finished.Add(table);
            }
            else
            {
                this.CellBreak();
            }
        }

        public void FlushParagraph(bool keepTag = false)
        {
            var text = this.paragraph.ToString().Trim();
            this.paragraph.Clear();
            if (text.Length > 0)
                this.Paragraphs.Add(new Paragraph(text, this.paragraphTag));
            if (!keepTag)
                this.paragraphTag = "p";
        }

        private void CellBreak()
        {
            var cell = this.Tables[0].Cell;
            if (cell != null && cell.Builder.Length > 0 && cell.Builder[^1] != '\n')
                cell.Builder.Append('\n');
        }
    }
}
