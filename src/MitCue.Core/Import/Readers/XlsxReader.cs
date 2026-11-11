using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using D = DocumentFormat.OpenXml.Drawing;
using X = DocumentFormat.OpenXml.Spreadsheet;

namespace MitCue.Core.Import;

/// <summary>
/// xlsx → <see cref="SheetDocument"/> with DocumentFormat.OpenXml (docs/research/sheets-import.md §4). Produces the same
/// shape as the JSON fixtures: text as displayed, effective fill/font colour (including conditional formatting, which
/// the Google Sheets API reports as part of the effective format), bold/strike, rich-text runs, notes, merges and
/// hidden rows/columns; trailing rows/columns without a value or note are trimmed. Hidden tabs are read and flagged
/// (<see cref="SheetTab.Hidden"/>); chart sheets are skipped. OOXML string escapes (<c>_x000D_</c>) are decoded and
/// line breaks become <c>\n</c>.
/// </summary>
internal static partial class XlsxReader
{
    // Built-in number formats (ids without a <numFmt> element). Same table as ClosedXML's XLPredefinedFormat.
    private static readonly Dictionary<uint, string> BuiltInFormats = new()
    {
        [1] = "0", [2] = "0.00", [3] = "#,##0", [4] = "#,##0.00", [7] = "$#,##0.00_);($#,##0.00)",
        [9] = "0%", [10] = "0.00%", [11] = "0.00E+00", [12] = "# ?/?", [13] = "# ??/??",
        [14] = "M/d/yyyy", [15] = "d-MMM-yy", [16] = "d-MMM", [17] = "MMM-yy", [18] = "h:mm AM/PM",
        [19] = "h:mm:ss AM/PM", [20] = "H:mm", [21] = "H:mm:ss", [22] = "M/d/yyyy H:mm",
        [37] = "#,##0 ;(#,##0)", [38] = "#,##0 ;[Red](#,##0)", [39] = "#,##0.00;(#,##0.00)",
        [40] = "#,##0.00;[Red](#,##0.00)", [45] = "mm:ss", [46] = "[h]:mm:ss", [47] = "mm:ss.0",
        [48] = "##0.0E+0", [49] = "@",
    };

    // Excel's default indexed palette (0-63); 64 = system foreground, 65 = system background.
    private static readonly string[] DefaultIndexedColors =
    [
        "000000", "ffffff", "ff0000", "00ff00", "0000ff", "ffff00", "ff00ff", "00ffff",
        "000000", "ffffff", "ff0000", "00ff00", "0000ff", "ffff00", "ff00ff", "00ffff",
        "800000", "008000", "000080", "808000", "800080", "008080", "c0c0c0", "808080",
        "9999ff", "993366", "ffffcc", "ccffff", "660066", "ff8080", "0066cc", "ccccff",
        "000080", "ff00ff", "ffff00", "00ffff", "800080", "800000", "008080", "0000ff",
        "00ccff", "ccffff", "ccffcc", "ffff99", "99ccff", "ff99cc", "cc99ff", "ffcc99",
        "3366ff", "33cccc", "99cc00", "ffcc00", "ff9900", "ff6600", "666699", "969696",
        "003366", "339966", "003300", "333300", "993300", "993366", "333399", "333333",
        "000000", "ffffff",
    ];

    // Excel's theme index order swaps 0/1 and 2/3 relative to <a:clrScheme> (dk1, lt1, dk2, lt2, accent1-6, hlink, folHlink).
    private static readonly int[] ThemeIndexMap = [1, 0, 3, 2, 4, 5, 6, 7, 8, 9, 10, 11];

    private readonly record struct Style(string? Bg, string? Fg, bool Bold, bool Strike, uint NumFmtId);

    private sealed record XCell(string? Text, string? Bg, string? Fg, bool Bold, bool Strike, IReadOnlyList<TextRun> Runs, string? Note);

    [GeneratedRegex("^REGEXMATCH\\(\\$?[A-Z]+\\$?[0-9]+,\"(.*)\"\\)$")]
    private static partial Regex RegexMatchCall();

    // ST_Xstring escape of a UTF-16 code unit: "_x000D_" is a carriage return, "_x005F_" a literal underscore.
    [GeneratedRegex("_x([0-9A-Fa-f]{4})_")]
    private static partial Regex XmlEscape();

    public static SheetDocument Read(Stream stream, string title, XlsxReadOptions? options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek)
        {
            var copy = new MemoryStream();
            stream.CopyTo(copy);
            copy.Position = 0;
            stream = copy;
        }

        try
        {
            using var doc = SpreadsheetDocument.Open(stream, false);
            return ReadDocument(doc, title, options);
        }
        catch (Exception e) when (e is OpenXmlPackageException or FileFormatException or InvalidOperationException or System.Xml.XmlException
            or IOException and not FileNotFoundException or FormatException or ArgumentException or KeyNotFoundException or OverflowException
            or IndexOutOfRangeException or InvalidCastException)
        {
            throw new InvalidDataException("Not a readable .xlsx workbook: " + e.Message, e);
        }
    }

    private static SheetDocument ReadDocument(SpreadsheetDocument doc, string title, XlsxReadOptions? options)
    {
        var wb = doc.WorkbookPart ?? throw new InvalidDataException("The xlsx has no workbook part.");
        var theme = ReadThemeColors(wb.ThemePart);
        var stylesheet = wb.WorkbookStylesPart?.Stylesheet;
        var fonts = stylesheet?.Fonts?.Elements<X.Font>().ToList() ?? [];
        var fills = stylesheet?.Fills?.Elements<X.Fill>().ToList() ?? [];
        var xfs = stylesheet?.CellFormats?.Elements<X.CellFormat>().ToList() ?? [];
        var dxfs = stylesheet?.DifferentialFormats?.Elements<X.DifferentialFormat>().ToList() ?? [];
        var indexed = ReadIndexedColors(stylesheet);
        var customFormats = new Dictionary<uint, string>();
        foreach (var nf in stylesheet?.NumberingFormats?.Elements<X.NumberingFormat>() ?? [])
        {
            if (nf.NumberFormatId?.Value is { } id && nf.FormatCode?.Value is { } code)
                customFormats[id] = code;
        }

        var shared = wb.SharedStringTablePart?.SharedStringTable?.Elements<X.SharedStringItem>().ToList() ?? [];

        string? Hex(X.ColorType? c)
        {
            if (c is null)
                return null;
            var tint = c.Tint?.Value ?? 0;
            if (c.Rgb?.Value is { Length: >= 6 } rgb)
                return ApplyTint("#" + rgb[^6..].ToLowerInvariant(), tint); // null when it is not hex
            if (c.Theme?.Value is { } t)
            {
                var i = t < ThemeIndexMap.Length ? ThemeIndexMap[t] : -1;
                return i >= 0 && i < theme.Count ? ApplyTint(theme[i], tint) : null;
            }

            if (c.Indexed?.Value is { } ix && ix < indexed.Count)
                return ApplyTint("#" + indexed[(int)ix], tint);
            return null; // auto: the default colour
        }

        var styleCache = new Dictionary<uint, Style>();
        Style StyleOf(uint xfIndex)
        {
            if (styleCache.TryGetValue(xfIndex, out var s))
                return s;
            var xf = xfIndex < xfs.Count ? xfs[(int)xfIndex] : null;
            var font = xf?.FontId?.Value is { } fi && fi < fonts.Count ? fonts[(int)fi] : null;
            var fill = xf?.FillId?.Value is { } li && li < fills.Count ? fills[(int)li] : null;
            string? bg = null;
            if (fill?.PatternFill is { } pf && pf.PatternType?.Value == X.PatternValues.Solid)
                bg = Hex(pf.ForegroundColor);
            var fg = Hex(font?.Color);
            s = new Style(
                bg == "#ffffff" ? null : bg,
                fg == "#000000" ? null : fg,
                On(font?.Bold),
                On(font?.Strike),
                xf?.NumberFormatId?.Value ?? 0);
            styleCache[xfIndex] = s;
            return s;
        }

        (string Text, IReadOnlyList<TextRun> Runs) ReadRich(OpenXmlElement item, Style cellStyle)
        {
            var runs = item.Elements<X.Run>().ToList();
            if (runs.Count == 0)
                return (NormalizeBreaks(Unescape(item.GetFirstChild<X.Text>()?.Text ?? string.Empty)), []);
            var sb = new StringBuilder();
            var result = new List<TextRun>(runs.Count);
            foreach (var run in runs)
            {
                string? fg;
                bool bold, strike;
                if (run.RunProperties is { } rp)
                {
                    fg = Hex(rp.GetFirstChild<X.Color>());
                    fg = fg == "#000000" ? null : fg;
                    bold = On(rp.GetFirstChild<X.Bold>());
                    strike = On(rp.GetFirstChild<X.Strike>());
                }
                else
                {
                    fg = cellStyle.Fg;
                    bold = cellStyle.Bold;
                    strike = cellStyle.Strike;
                }

                result.Add(new TextRun(sb.Length, fg, bold) { Strike = strike }); // UTF-16 offset == C# string index
                sb.Append(Unescape(run.Text?.Text ?? string.Empty));
            }

            return NormalizeBreaks(sb.ToString(), result);
        }

        string FormatNumber(string raw, uint numFmtId)
        {
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return raw;
            var code = customFormats.TryGetValue(numFmtId, out var c) ? c
                : BuiltInFormats.TryGetValue(numFmtId, out var b) ? b : null;
            if (code is null)
                return d.ToString(CultureInfo.InvariantCulture); // 0 = General
            try
            {
                var nf = new ExcelNumberFormat.NumberFormat(code);
                return nf.IsValid ? nf.Format(d, CultureInfo.InvariantCulture, false) : raw;
            }
            catch (Exception)
            {
                return raw;
            }
        }

        var tabs = new List<SheetTab>();
        var sheets = wb.Workbook?.Sheets?.Elements<X.Sheet>().ToList() ?? [];
        var dangling = 0;
        for (var index = 0; index < sheets.Count; index++)
        {
            // The index counts every <sheet>, chart sheets included, so it lines up with TabNames and Google's tab order.
            var sheet = sheets[index];
            if (sheet.Id?.Value is not { } rid || !wb.TryGetPartById(rid, out var anyPart))
            {
                dangling++; // points to a part that is not in the file: skip it, keep the others
                continue;
            }

            if (anyPart is not WorksheetPart part)
                continue; // a chart or dialog sheet
            var ws = part.Worksheet;

            // Column default styles and hidden columns (<cols><col min max hidden style/>; 1-based inclusive).
            var colStyle = new Dictionary<int, uint>();
            var hiddenCols = new SortedSet<int>();
            foreach (var col in ws?.GetFirstChild<X.Columns>()?.Elements<X.Column>() ?? [])
            {
                var min = (int)(col.Min?.Value ?? 1) - 1;
                var max = (int)(col.Max?.Value ?? (uint)(min + 1)) - 1;
                for (var c = Math.Max(0, min); c <= max && c < 16384; c++)
                {
                    if (col.Style?.Value is { } st)
                        colStyle[c] = st;
                    if (col.Hidden?.Value == true)
                        hiddenCols.Add(c);
                }
            }

            var cells = new Dictionary<(int R, int C), XCell>();
            var rowStyle = new Dictionary<int, uint>();
            var hiddenRows = new SortedSet<int>();
            var nextRow = 0;
            foreach (var row in ws?.GetFirstChild<X.SheetData>()?.Elements<X.Row>() ?? [])
            {
                var r = row.RowIndex?.Value is { } ri ? (int)ri - 1 : nextRow;
                nextRow = r + 1;
                if (row.Hidden?.Value == true)
                    hiddenRows.Add(r);
                if (row.CustomFormat?.Value == true && row.StyleIndex?.Value is { } rs)
                    rowStyle[r] = rs;
                var nextCol = 0;
                foreach (var cell in row.Elements<X.Cell>())
                {
                    var (cr, cc) = cell.CellReference?.Value is { } a1 && TryParseA1(a1, out var pos) ? pos : (r, nextCol);
                    nextCol = cc + 1;
                    var style = StyleOf(cell.StyleIndex?.Value ?? 0);
                    string? text = null;
                    IReadOnlyList<TextRun> runs = [];
                    var type = cell.DataType?.Value;
                    if (type == X.CellValues.SharedString)
                    {
                        if (int.TryParse(cell.CellValue?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var si) && si >= 0 && si < shared.Count)
                            (text, runs) = ReadRich(shared[si], style);
                    }
                    else if (type == X.CellValues.InlineString)
                    {
                        if (cell.InlineString is { } inl)
                            (text, runs) = ReadRich(inl, style);
                    }
                    else if (cell.CellValue?.Text is { } raw)
                    {
                        if (type is null || type == X.CellValues.Number)
                            text = FormatNumber(raw, style.NumFmtId);
                        else if (type == X.CellValues.Boolean)
                            text = raw == "1" ? "TRUE" : "FALSE";
                        else if (type == X.CellValues.String)
                            text = NormalizeBreaks(Unescape(raw)); // formula result string
                        else
                            text = raw; // e (error), d (ISO date)
                    }

                    cells[(cr, cc)] = new XCell(string.IsNullOrEmpty(text) ? null : text, style.Bg, style.Fg, style.Bold, style.Strike, runs, null);
                }
            }

            // Notes: Google exports cell notes as legacy comments (xl/commentsN.xml).
            foreach (var cm in part.WorksheetCommentsPart?.Comments?.CommentList?.Elements<X.Comment>() ?? [])
            {
                if (cm.Reference?.Value is not { } refA1 || !TryParseA1(refA1, out var key))
                    continue;
                var note = cm.CommentText?.InnerText is { } inner ? NormalizeBreaks(Unescape(inner)) : null;
                if (string.IsNullOrEmpty(note))
                    continue;
                cells[key] = cells.TryGetValue(key, out var existing)
                    ? existing with { Note = note }
                    : new XCell(null, null, null, false, false, [], note);
            }

            // Trim trailing rows/columns without a value or note (same rule as the JSON fixtures and Google's CSV export).
            var nRows = 0;
            var nCols = 0;
            foreach (var ((r, c), cell) in cells)
            {
                if (cell.Text is not null || cell.Note is not null)
                {
                    nRows = Math.Max(nRows, r + 1);
                    nCols = Math.Max(nCols, c + 1);
                }
            }

            var rows = new List<List<XCell>>(nRows);
            for (var r = 0; r < nRows; r++)
            {
                var list = new List<XCell>(nCols);
                for (var c = 0; c < nCols; c++)
                {
                    if (cells.TryGetValue((r, c), out var cell))
                    {
                        list.Add(cell);
                        continue;
                    }

                    // Cell element absent: row style (customFormat) > column style > default style 0.
                    var st = StyleOf(rowStyle.TryGetValue(r, out var rsi) ? rsi : colStyle.TryGetValue(c, out var csi) ? csi : 0);
                    list.Add(new XCell(null, st.Bg, st.Fg, st.Bold, st.Strike, [], null));
                }

                rows.Add(list);
            }

            ApplyConditionalFormats(ws, rows, dxfs, Hex);

            var merges = new List<CellRange>();
            foreach (var mc in ws?.GetFirstChild<X.MergeCells>()?.Elements<X.MergeCell>() ?? [])
            {
                if (mc.Reference?.Value is not { } refs)
                    continue;
                var parts = refs.Split(':');
                if (!TryParseA1(parts[0], out var p0))
                    continue;
                var p1 = parts.Length > 1 && TryParseA1(parts[1], out var q) ? q : p0;
                var (r0, c0) = p0;
                var (r1, c1) = p1;
                if (r0 >= nRows || c0 >= nCols)
                    continue;
                r1 = Math.Min(r1, nRows - 1);
                c1 = Math.Min(c1, nCols - 1);
                if (r0 != r1 || c0 != c1)
                    merges.Add(new CellRange(r0, c0, r1, c1));
            }

            merges.Sort((a, b) => a.R0 != b.R0 ? a.R0.CompareTo(b.R0) : a.C0.CompareTo(b.C0));

            var name = sheet.Name?.Value ?? $"Sheet{index + 1}";
            if (options?.TabNames is { } names && index < names.Count && !string.IsNullOrWhiteSpace(names[index]))
                name = names[index];
            var state = sheet.State?.Value;
            tabs.Add(new SheetTab
            {
                Name = name,
                Index = index,
                Hidden = state is { } sheetState && (sheetState == X.SheetStateValues.Hidden || sheetState == X.SheetStateValues.VeryHidden),
                Rows = rows.Select(IReadOnlyList<SheetCell> (row) => row.Select(ToSheetCell).ToList()).ToList(),
                Merges = merges,
                HiddenRows = hiddenRows.Where(r => r < nRows).ToList(),
                HiddenCols = hiddenCols.Where(c => c < nCols).ToList(),
            });
        }

        if (sheets.Count > 0 && dangling == sheets.Count)
            throw new InvalidDataException("Not a readable .xlsx workbook: none of its sheets is in the file.");

        if (options?.Gid is { } gid && tabs.Count == 1)
        {
            var t = tabs[0];
            tabs[0] = new SheetTab { Name = t.Name, Gid = gid, Index = t.Index, Hidden = t.Hidden, Rows = t.Rows, Merges = t.Merges, HiddenRows = t.HiddenRows, HiddenCols = t.HiddenCols };
        }

        return new SheetDocument { Title = title, Tabs = tabs };
    }

    private static SheetCell ToSheetCell(XCell c) => new()
    {
        Text = c.Text,
        Bg = c.Bg,
        Fg = c.Fg,
        Bold = c.Bold,
        Strike = c.Strike,
        Runs = c.Runs,
        Note = c.Note,
    };

    /// <summary>
    /// Google Sheets semantics: rules in priority order, the first matching rule wins (Google exports every rule
    /// with stopIfTrue="1"). Supported: containsText/notContainsText/beginsWith/endsWith (case-insensitive, like
    /// SEARCH), containsBlanks/notContainsBlanks, and expression rules of the form REGEXMATCH(ref,"re"). Other rule
    /// types are ignored.
    /// </summary>
    private static void ApplyConditionalFormats(X.Worksheet? ws, List<List<XCell>> rows, List<X.DifferentialFormat> dxfs, Func<X.ColorType?, string?> hex)
    {
        if (ws is null || rows.Count == 0)
            return;
        var rules = new List<(int Priority, X.ConditionalFormattingRule Rule, List<CellRange> Ranges)>();
        foreach (var cf in ws.Elements<X.ConditionalFormatting>())
        {
            var ranges = new List<CellRange>();
            foreach (var sq in cf.SequenceOfReferences?.Items ?? [])
            {
                var p = (sq.Value ?? string.Empty).Split(':');
                if (!TryParseA1(p[0], out var a))
                    continue;
                var b = p.Length > 1 && TryParseA1(p[1], out var q) ? q : a;
                ranges.Add(new CellRange(a.Row, a.Col, b.Row, b.Col));
            }

            foreach (var rule in cf.Elements<X.ConditionalFormattingRule>())
                rules.Add((rule.Priority?.Value ?? int.MaxValue, rule, ranges));
        }

        if (rules.Count == 0)
            return;
        rules.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        var regexCache = new Dictionary<string, Regex?>();
        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < rows[r].Count; c++)
            {
                var cell = rows[r][c];
                var text = cell.Text ?? string.Empty;
                foreach (var (_, rule, ranges) in rules)
                {
                    if (!ranges.Any(g => g.Contains(r, c)))
                        continue;
                    var type = rule.Type?.Value;
                    var needle = rule.Text?.Value ?? string.Empty;
                    bool? match =
                        type == X.ConditionalFormatValues.ContainsText ? text.Contains(needle, StringComparison.OrdinalIgnoreCase)
                        : type == X.ConditionalFormatValues.NotContainsText ? !text.Contains(needle, StringComparison.OrdinalIgnoreCase)
                        : type == X.ConditionalFormatValues.BeginsWith ? text.StartsWith(needle, StringComparison.OrdinalIgnoreCase)
                        : type == X.ConditionalFormatValues.EndsWith ? text.EndsWith(needle, StringComparison.OrdinalIgnoreCase)
                        : type == X.ConditionalFormatValues.ContainsBlanks ? string.IsNullOrWhiteSpace(text)
                        : type == X.ConditionalFormatValues.NotContainsBlanks ? !string.IsNullOrWhiteSpace(text)
                        : type == X.ConditionalFormatValues.Expression ? EvalRegexMatch(rule.GetFirstChild<X.Formula>()?.Text, text, regexCache)
                        : null;
                    if (match is not true)
                        continue;
                    var dxf = rule.FormatId?.Value is { } di && di < dxfs.Count ? dxfs[(int)di] : null;
                    var fg = dxf?.Font?.Color is { } fc ? hex(fc) : cell.Fg;
                    var bg = cell.Bg;
                    if (dxf?.Fill?.PatternFill is { } pf && pf.PatternType?.Value != X.PatternValues.None)
                        bg = hex(pf.BackgroundColor ?? (X.ColorType?)pf.ForegroundColor); // dxf solid fills use bgColor
                    rows[r][c] = cell with
                    {
                        Fg = fg == "#000000" ? null : fg,
                        Bg = bg == "#ffffff" ? null : bg,
                        Bold = dxf?.Font?.Bold is { } b ? On(b) : cell.Bold,
                        Strike = dxf?.Font?.Strike is { } s ? On(s) : cell.Strike,
                    };
                    break; // first match wins
                }
            }
        }
    }

    private static bool? EvalRegexMatch(string? formula, string text, Dictionary<string, Regex?> cache)
    {
        if (formula is null || RegexMatchCall().Match(formula) is not { Success: true } m)
            return null;
        var pattern = m.Groups[1].Value.Replace("\"\"", "\"", StringComparison.Ordinal);
        if (!cache.TryGetValue(pattern, out var re))
        {
            try
            {
                re = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
            }
            catch (ArgumentException)
            {
                re = null;
            }

            cache[pattern] = re;
        }

        if (re is null)
            return null;
        try
        {
            return re.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    /// <summary>Decodes ST_Xstring escapes: "_x000D_" → CR, "_x005F_" → "_" (Excel writes CR LF line breaks this way).</summary>
    internal static string Unescape(string text) =>
        text.Contains("_x", StringComparison.Ordinal)
            ? XmlEscape().Replace(text, m => ((char)int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString())
            : text;

    private static string NormalizeBreaks(string text) => NormalizeBreaks(text, []).Text;

    /// <summary>CR LF and lone CR → LF, moving rich-text run starts with the text.</summary>
    private static (string Text, IReadOnlyList<TextRun> Runs) NormalizeBreaks(string text, List<TextRun> runs)
    {
        if (text.IndexOf('\r') < 0)
            return (text, runs);
        var map = new int[text.Length + 1];
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            map[i] = sb.Length;
            if (text[i] != '\r')
                sb.Append(text[i]);
            else if (i + 1 >= text.Length || text[i + 1] != '\n')
                sb.Append('\n');
        }

        map[text.Length] = sb.Length;
        var moved = new List<TextRun>(runs.Count);
        foreach (var run in runs)
        {
            var start = map[run.Start];
            if (moved.Count > 0 && moved[^1].Start == start)
                moved[^1] = run with { Start = start }; // the run before was only the removed CR
            else
                moved.Add(run with { Start = start });
        }

        return (sb.ToString(), moved);
    }

    /// <summary>On/off element semantics: present without val = true; val="0"/"false" = false.</summary>
    private static bool On(X.BooleanPropertyType? e) => e is not null && (e.Val?.Value ?? true);

    internal static bool TryParseA1(string a1, out (int Row, int Col) pos)
    {
        pos = default;
        var col = 0;
        var i = 0;
        while (i < a1.Length && a1[i] == '$')
            i++;
        var letters = i;
        while (i < a1.Length && char.IsAsciiLetter(a1[i]))
        {
            col = (col * 26) + (char.ToUpperInvariant(a1[i]) - 'A' + 1);
            i++;
            if (col > 16384)
                return false;
        }

        if (i == letters)
            return false;
        while (i < a1.Length && a1[i] == '$')
            i++;
        if (!int.TryParse(a1.AsSpan(i), NumberStyles.None, CultureInfo.InvariantCulture, out var row) || row < 1)
            return false;
        pos = (row - 1, col - 1);
        return true;
    }

    private static List<string> ReadIndexedColors(X.Stylesheet? stylesheet)
    {
        var custom = stylesheet?.Colors?.IndexedColors?.Elements<X.RgbColor>()
            .Select(c => c.Rgb?.Value is { Length: >= 6 } v && SheetColors.TryParse(v[^6..], out _, out _, out _) ? v[^6..].ToLowerInvariant() : "000000")
            .ToList();
        if (custom is null || custom.Count == 0)
            return [.. DefaultIndexedColors];
        while (custom.Count < DefaultIndexedColors.Length)
            custom.Add(DefaultIndexedColors[custom.Count]);
        return custom;
    }

    private static List<string> ReadThemeColors(ThemePart? part)
    {
        var scheme = part?.Theme?.ThemeElements?.ColorScheme;
        if (scheme is null)
            return [];
        OpenXmlCompositeElement?[] order =
        [
            scheme.Dark1Color, scheme.Light1Color, scheme.Dark2Color, scheme.Light2Color,
            scheme.Accent1Color, scheme.Accent2Color, scheme.Accent3Color, scheme.Accent4Color,
            scheme.Accent5Color, scheme.Accent6Color, scheme.Hyperlink, scheme.FollowedHyperlinkColor,
        ];
        return order.Select(e =>
            e?.GetFirstChild<D.RgbColorModelHex>()?.Val?.Value is { } hex && SheetColors.Normalize(hex) is { } h1 ? h1
            : e?.GetFirstChild<D.SystemColor>()?.LastColor?.Value is { } last && SheetColors.Normalize(last) is { } h2 ? h2
            : "#000000").ToList();
    }

    /// <summary>Applies an OOXML tint to "#rrggbb"; null when <paramref name="hex"/> is not a colour.</summary>
    private static string? ApplyTint(string hex, double tint)
    {
        if (!SheetColors.TryParse(hex, out var r8, out var g8, out var b8))
            return null;
        if (tint == 0)
            return SheetColors.Normalize(hex);
        var r = r8 / 255.0;
        var g = g8 / 255.0;
        var b = b8 / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        double h = 0, s = 0;
        if (max != min)
        {
            var d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? ((g - b) / d) + (g < b ? 6 : 0) : max == g ? ((b - r) / d) + 2 : ((r - g) / d) + 4;
            h /= 6;
        }

        l = tint < 0 ? l * (1 + tint) : (l * (1 - tint)) + tint;
        static double Hue(double p, double q, double t)
        {
            if (t < 0)
                t += 1;
            if (t > 1)
                t -= 1;
            return t < 1.0 / 6 ? p + ((q - p) * 6 * t) : t < 0.5 ? q : t < 2.0 / 3 ? p + ((q - p) * ((2.0 / 3) - t) * 6) : p;
        }

        double rr = l, gg = l, bb = l;
        if (s != 0)
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
            var p = (2 * l) - q;
            rr = Hue(p, q, h + (1.0 / 3));
            gg = Hue(p, q, h);
            bb = Hue(p, q, h - (1.0 / 3));
        }

        return string.Create(CultureInfo.InvariantCulture, $"#{(int)Math.Round(rr * 255):x2}{(int)Math.Round(gg * 255):x2}{(int)Math.Round(bb * 255):x2}");
    }
}
