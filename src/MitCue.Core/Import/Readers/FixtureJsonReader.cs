using System.Text.Json;

namespace MitCue.Core.Import;

/// <summary>
/// Reads the JSON fixture format of docs/research/sheets-import.md §2.2: one tab per file with
/// <c>tab</c>, <c>gid</c>, <c>index</c>, <c>rows[][]</c> of <c>{v,bg,fg,bold,strike,runs,note}</c>,
/// inclusive <c>merges</c> and optional <c>hiddenRows</c>/<c>hiddenCols</c>. Unknown properties are ignored.
/// </summary>
internal static class FixtureJsonReader
{
    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static SheetTab ReadTab(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, Options);
        }
        catch (JsonException e)
        {
            throw new FormatException("Not a sheet fixture: " + e.Message, e);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new FormatException("Not a sheet fixture: the root must be an object.");
            if (!root.TryGetProperty("rows", out var rowsEl) || rowsEl.ValueKind != JsonValueKind.Array)
                throw new FormatException("Not a sheet fixture: missing \"rows\" array.");

            var rows = new List<IReadOnlyList<SheetCell>>(rowsEl.GetArrayLength());
            foreach (var rowEl in rowsEl.EnumerateArray())
            {
                if (rowEl.ValueKind != JsonValueKind.Array)
                {
                    rows.Add([]);
                    continue;
                }

                var row = new List<SheetCell>(rowEl.GetArrayLength());
                foreach (var cellEl in rowEl.EnumerateArray())
                    row.Add(ReadCell(cellEl));
                rows.Add(row);
            }

            var merges = new List<CellRange>();
            if (root.TryGetProperty("merges", out var mergesEl) && mergesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in mergesEl.EnumerateArray())
                {
                    if (m.ValueKind != JsonValueKind.Object)
                        continue;
                    merges.Add(new CellRange(Int(m, "r0"), Int(m, "c0"), Int(m, "r1"), Int(m, "c1")));
                }
            }

            return new SheetTab
            {
                Name = Str(root, "tab") ?? "Sheet1",
                Gid = root.TryGetProperty("gid", out var gidEl) && gidEl.ValueKind == JsonValueKind.Number && gidEl.TryGetInt64(out var gid) ? gid : null,
                Index = IndexOf(root) ?? 0,
                Rows = rows,
                Merges = merges,
                HiddenRows = IntList(root, "hiddenRows"),
                HiddenCols = IntList(root, "hiddenCols"),
            };
        }
    }

    public static SheetDocument ReadDirectory(string directory, string title)
    {
        ArgumentNullException.ThrowIfNull(directory);
        var files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        var tabs = new List<(int? Index, string File, SheetTab Tab)>(files.Length);
        foreach (var file in files)
        {
            var json = File.ReadAllText(file);
            int? index;
            try
            {
                using var doc = JsonDocument.Parse(json, Options);
                index = doc.RootElement.ValueKind == JsonValueKind.Object ? IndexOf(doc.RootElement) : null;
            }
            catch (JsonException e)
            {
                throw new FormatException($"Not a sheet fixture: {Path.GetFileName(file)}: {e.Message}", e);
            }

            tabs.Add((index, Path.GetFileName(file), ReadTab(json)));
        }

        return new SheetDocument
        {
            Title = title,
            Tabs = tabs
                .OrderBy(t => t.Index ?? int.MaxValue)
                .ThenBy(t => t.File, StringComparer.Ordinal)
                .Select(t => t.Tab)
                .ToList(),
        };
    }

    private static SheetCell ReadCell(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return SheetCell.Empty;
        var runs = new List<TextRun>();
        if (el.TryGetProperty("runs", out var runsEl) && runsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in runsEl.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object)
                    continue;
                runs.Add(new TextRun(Int(r, "start"), Str(r, "fg"), Bool(r, "bold")) { Strike = Bool(r, "strike") });
            }
        }

        return new SheetCell
        {
            Text = Str(el, "v"),
            Bg = Str(el, "bg"),
            Fg = Str(el, "fg"),
            Bold = Bool(el, "bold"),
            Strike = Bool(el, "strike"),
            Runs = runs,
            Note = Str(el, "note"),
        };
    }

    private static int? IndexOf(JsonElement root) =>
        root.TryGetProperty("index", out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i) ? i : null;

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static int Int(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    private static IReadOnlyList<int> IntList(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<int>();
        foreach (var x in v.EnumerateArray())
        {
            if (x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var i))
                list.Add(i);
        }

        list.Sort();
        return list;
    }
}
