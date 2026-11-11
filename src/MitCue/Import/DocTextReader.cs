using MitCue.Core.Import;

namespace MitCue.Import;

/// <summary>
/// Turns a Google Docs plain-text export (<c>/document/d/&lt;id&gt;/export?format=txt</c>: UTF-8 with a BOM, CRLF, one
/// paragraph per line) into a <see cref="SheetDocument"/> with one <see cref="TabName"/> tab: one row per non-empty
/// line, split at its tabs. Used when the web page export is too large to download (it inlines every image), because
/// the text export of such a document is small. Tables are flattened in this export (a cell's later lines lose their
/// column), so the rows only approximate them; colours are not kept.
/// </summary>
internal static class DocTextReader
{
    /// <summary>Name of the one tab.</summary>
    public const string TabName = DocHtmlReader.TextTabName;

    /// <summary>Reads the text. Never throws on odd input.</summary>
    /// <param name="text">The export.</param>
    /// <param name="title">Document title (from the download), or null.</param>
    public static SheetDocument Read(string text, string? title)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rows = new List<IReadOnlyList<SheetCell>>();
        foreach (var raw in text.TrimStart('﻿').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var parts = raw.TrimEnd().Split('\t');
            var cells = new List<SheetCell>(parts.Length);
            foreach (var part in parts)
            {
                var cell = part.Trim();
                cells.Add(cell.Length == 0 ? SheetCell.Empty : new SheetCell { Text = cell });
            }

            rows.Add(cells);
        }

        var tabs = rows.Count > 0 ? new List<SheetTab> { new() { Name = TabName, Rows = rows } } : [];
        return new SheetDocument { Title = string.IsNullOrWhiteSpace(title) ? "Google Doc" : title.Trim(), Tabs = tabs };
    }
}
