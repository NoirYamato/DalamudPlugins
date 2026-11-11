using System.Globalization;
using System.Text;
using MitCue.Core.Import;

namespace MitCue.Import;

/// <summary>A parsed A1 source reference such as <c>'TOP – White Mage'!C18</c> (0-based row/column).</summary>
/// <param name="Tab">Tab name.</param>
/// <param name="Row">0-based row.</param>
/// <param name="Column">0-based column.</param>
internal sealed record CellRef(string Tab, int Row, int Column)
{
    /// <summary>Parses <c>'Tab'!C18</c> (the form <see cref="SheetTab.Ref"/> writes) or <c>Tab!C18</c>; null when it is not one.</summary>
    public static CellRef? TryParse(string? src)
    {
        if (string.IsNullOrWhiteSpace(src))
            return null;
        var s = src.Trim();
        var bang = s.LastIndexOf('!');
        if (bang <= 0 || bang == s.Length - 1)
            return null;

        var tab = s[..bang];
        if (tab.Length >= 2 && tab[0] == '\'' && tab[^1] == '\'')
            tab = tab[1..^1].Replace("''", "'", StringComparison.Ordinal);

        var cell = s[(bang + 1)..].Replace("$", string.Empty, StringComparison.Ordinal);
        var i = 0;
        var column = 0;
        while (i < cell.Length && char.IsAsciiLetter(cell[i]))
        {
            column = (column * 26) + (char.ToUpperInvariant(cell[i]) - 'A' + 1);
            i++;
            if (column > 100_000)
                return null;
        }

        if (i == 0 || i == cell.Length || !int.TryParse(cell.AsSpan(i), NumberStyles.None, CultureInfo.InvariantCulture, out var row) || row < 1)
            return null;
        return new CellRef(tab, row - 1, column - 1);
    }

    /// <summary>"C18".</summary>
    public string Address => SheetText.ColumnLetters(this.Column) + (this.Row + 1).ToString(CultureInfo.InvariantCulture);
}

/// <summary>Text views of a <see cref="SheetDocument"/>: the source text for the AI prompt and per-tab statistics.</summary>
internal static class SheetText
{
    /// <summary>Spreadsheet column letters for a 0-based column (0 → "A", 26 → "AA").</summary>
    public static string ColumnLetters(int column)
    {
        var letters = string.Empty;
        var c = column + 1;
        while (c > 0)
        {
            var m = (c - 1) % 26;
            letters = (char)('A' + m) + letters;
            c = (c - 1) / 26;
        }

        return letters;
    }

    /// <summary>Rows that have at least one non-empty cell.</summary>
    public static int NonEmptyRows(SheetTab tab) => tab.Rows.Count(r => r.Any(c => !c.IsEmpty));

    /// <summary>The text of a source cell, or null when the reference does not resolve.</summary>
    public static string? CellText(SheetDocument? document, string? src)
    {
        if (document == null || CellRef.TryParse(src) is not { } cellRef)
            return null;
        var tab = document.Tab(cellRef.Tab);
        var text = tab?.Cell(cellRef.Row, cellRef.Column).Text;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>
    /// The document as compact text for an AI prompt: per tab, one line per non-empty row,
    /// <c>R5 | A: Latent 2 | B: Party Mit | D: Wings</c>. Line breaks inside a cell become " ⏎ ". Text runs in another
    /// colour are marked <c>[#808080: …]</c> and struck-through cells <c>[struck: …]</c>, because sheets use colour for
    /// meaning (grey = optional).
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="tabs">Tabs to include (null = all), by name.</param>
    /// <param name="maxChars">Upper bound of the result; the rest is cut with a note.</param>
    public static string ForPrompt(SheetDocument document, IReadOnlyCollection<string>? tabs = null, int maxChars = 300_000)
    {
        var sb = new StringBuilder();
        sb.Append("Source: ").Append(document.Title).Append('\n');
        foreach (var tab in document.Tabs)
        {
            if (tabs != null && !tabs.Contains(tab.Name, StringComparer.OrdinalIgnoreCase))
                continue;

            sb.Append('\n').Append("### Tab \"").Append(tab.Name).Append("\"\n");
            for (var r = 0; r < tab.Rows.Count; r++)
            {
                var row = tab.Rows[r];
                var any = false;
                for (var c = 0; c < row.Count; c++)
                {
                    var cell = row[c];
                    if (cell.IsEmpty)
                        continue;
                    if (!any)
                    {
                        sb.Append('R').Append((r + 1).ToString(CultureInfo.InvariantCulture));
                        any = true;
                    }

                    sb.Append(" | ").Append(ColumnLetters(c)).Append(": ").Append(CellForPrompt(cell));
                }

                if (any)
                    sb.Append('\n');
                if (sb.Length > maxChars)
                {
                    sb.Length = maxChars;
                    sb.Append("\n[... the rest of the source was cut to keep the prompt small ...]\n");
                    return sb.ToString();
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>One cell for the prompt: text with colour/strike annotations, on one line.</summary>
    public static string CellForPrompt(SheetCell cell)
    {
        var text = cell.Text ?? string.Empty;
        string body;
        if (cell.Runs.Count > 1)
        {
            var sb = new StringBuilder(text.Length + 32);
            var runs = cell.Runs.OrderBy(r => r.Start).ToList();
            for (var i = 0; i < runs.Count; i++)
            {
                var start = Math.Clamp(runs[i].Start, 0, text.Length);
                var end = i + 1 < runs.Count ? Math.Clamp(runs[i + 1].Start, start, text.Length) : text.Length;
                if (end <= start)
                    continue;
                var piece = text[start..end];
                var colour = runs[i].Fg;
                if (colour != null && !string.Equals(colour, cell.Fg, StringComparison.OrdinalIgnoreCase) && piece.Trim().Length > 0)
                    sb.Append('[').Append(colour).Append(": ").Append(piece).Append(']');
                else
                    sb.Append(piece);
            }

            body = sb.ToString();
        }
        else
        {
            body = text;
        }

        body = body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace("\n", " ⏎ ", StringComparison.Ordinal).Replace('\t', ' ');
        return cell.Strike ? "[struck: " + body + "]" : body;
    }
}
