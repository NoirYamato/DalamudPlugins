"""Normalize Google Sheets API grid data (spreadsheets.get with includeGridData) into MitCue fixture JSON.

Usage: python3 -I normalize_sheets.py <api-dump.json> <out-dir>

<api-dump.json> is either a raw spreadsheets.get response (a Spreadsheet object) or the MCP tool-result file
that wraps it as {"content": <Spreadsheet>}.

Output: one <sanitized-tab-name>.json per tab, format documented in docs/research/sheets-import.md.
"""
import json
import re
import sys


def sanitize(name: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-")


def hex_color(rgb: dict) -> str:
    # Sheets API Color: float components 0..1, missing component == 0 (proto3 default).
    def ch(k):
        return max(0, min(255, round(float(rgb.get(k, 0.0)) * 255)))
    return "#{:02x}{:02x}{:02x}".format(ch("red"), ch("green"), ch("blue"))


def resolve(style: dict | None, legacy: dict | None, theme: dict) -> str | None:
    """ColorStyle -> '#rrggbb'. Prefers the *Style field, resolves themeColor, falls back to the deprecated Color field."""
    if style:
        if "rgbColor" in style:
            return hex_color(style["rgbColor"])
        if "themeColor" in style:
            t = theme.get(style["themeColor"])
            if t is not None:
                return t
    if legacy is not None:
        return hex_color(legacy)
    return None


def main(src: str, out_dir: str) -> None:
    raw = json.load(open(src, encoding="utf-8"))
    # The MCP harness saves the Spreadsheet wrapped as {"content": {...}}; a raw spreadsheets.get response is not wrapped.
    doc = raw["content"] if "sheets" not in raw and "content" in raw else raw
    theme = {}
    for tc in doc.get("properties", {}).get("spreadsheetTheme", {}).get("themeColors", []):
        theme[tc["colorType"]] = resolve(tc["color"], None, {})

    for sheet in doc["sheets"]:
        props = sheet["properties"]
        name = props["title"]
        if len(sheet.get("data", [])) != 1:
            raise SystemExit(f"{name}: expected exactly one GridData block, got {len(sheet.get('data', []))}")
        data = sheet["data"][0]
        if data.get("startRow", 0) or data.get("startColumn", 0):
            raise SystemExit(f"{name}: grid data must start at A1")

        raw_rows = [r.get("values", []) for r in data.get("rowData", [])]

        # Content extent: trailing rows/columns in which no cell has a value (non-empty string,
        # whitespace counts, as in Google's own CSV export) or a note are trimmed.
        def has_content(v):
            return bool(v.get("formattedValue")) or bool(v.get("note"))

        n_rows = 0
        n_cols = 0
        for i, vals in enumerate(raw_rows):
            for j, v in enumerate(vals):
                if has_content(v):
                    n_rows = max(n_rows, i + 1)
                    n_cols = max(n_cols, j + 1)

        rows = []
        for i in range(n_rows):
            vals = raw_rows[i] if i < len(raw_rows) else []
            row = []
            for j in range(n_cols):
                v = vals[j] if j < len(vals) else {}
                ef = v.get("effectiveFormat", {})
                tf = ef.get("textFormat", {})
                bg = resolve(ef.get("backgroundColorStyle"), ef.get("backgroundColor"), theme)
                fg = resolve(tf.get("foregroundColorStyle"), tf.get("foregroundColor"), theme)
                bg = None if bg in (None, "#ffffff") else bg
                fg = None if fg in (None, "#000000") else fg
                bold = bool(tf.get("bold", False))
                runs = []
                for run in v.get("textFormatRuns", []):
                    f = run.get("format", {})
                    rfg = resolve(f.get("foregroundColorStyle"), f.get("foregroundColor"), theme)
                    if rfg is None:
                        rfg = fg  # run inherits the cell's effective colour
                    elif rfg == "#000000":
                        rfg = None
                    runs.append({
                        "start": int(run.get("startIndex", 0)),  # UTF-16 code units, as in the API (== C# string index)
                        "fg": rfg,
                        "bold": bool(f["bold"]) if "bold" in f else bold,
                    })
                row.append({
                    "v": v.get("formattedValue"),
                    "bg": bg,
                    "fg": fg,
                    "bold": bold,
                    "strike": bool(tf.get("strikethrough", False)),
                    "runs": runs,
                    "note": v.get("note"),
                })
            rows.append(row)

        merges = []
        for m in sheet.get("merges", []):
            r0, r1 = m.get("startRowIndex", 0), m.get("endRowIndex", 0) - 1   # API end index is exclusive
            c0, c1 = m.get("startColumnIndex", 0), m.get("endColumnIndex", 0) - 1
            if r0 >= n_rows or c0 >= n_cols:
                continue  # anchored outside the trimmed grid
            r1, c1 = min(r1, n_rows - 1), min(c1, n_cols - 1)
            if r0 == r1 and c0 == c1:
                continue
            merges.append({"r0": r0, "c0": c0, "r1": r1, "c1": c1})
        merges.sort(key=lambda m: (m["r0"], m["c0"]))

        hidden_rows = [i for i, md in enumerate(data.get("rowMetadata", []))
                       if i < n_rows and (md.get("hiddenByUser") or md.get("hiddenByFilter"))]
        hidden_cols = [j for j, md in enumerate(data.get("columnMetadata", []))
                       if j < n_cols and md.get("hiddenByUser")]

        out = {
            "tab": name,
            "gid": props["sheetId"],
            "index": props["index"],
            "rows": rows,
            "merges": merges,
            "hiddenRows": hidden_rows,
            "hiddenCols": hidden_cols,
        }
        path = f"{out_dir}/{sanitize(name)}.json"
        with open(path, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(dumps(out))
        print(f"{path}: {n_rows} rows x {n_cols} cols, {len(merges)} merges, hiddenRows={hidden_rows}, hiddenCols={hidden_cols}")


def dumps(obj: dict) -> str:
    """Stable, diff-friendly layout: one cell per line, rows as nested arrays."""
    c = lambda x: json.dumps(x, ensure_ascii=False, separators=(",", ":"))
    lines = ["{"]
    lines.append(f'  "tab": {c(obj["tab"])},')
    lines.append(f'  "gid": {c(obj["gid"])},')
    lines.append(f'  "index": {c(obj["index"])},')
    if obj["rows"]:
        lines.append('  "rows": [')
        for i, row in enumerate(obj["rows"]):
            lines.append("    [")
            for j, cell in enumerate(row):
                lines.append("      " + c(cell) + ("," if j < len(row) - 1 else ""))
            lines.append("    ]" + ("," if i < len(obj["rows"]) - 1 else ""))
        lines.append("  ],")
    else:
        lines.append('  "rows": [],')
    lines.append(f'  "merges": {c(obj["merges"])},')
    lines.append(f'  "hiddenRows": {c(obj["hiddenRows"])},')
    lines.append(f'  "hiddenCols": {c(obj["hiddenCols"])}')
    lines.append("}")
    text = "\n".join(lines) + "\n"
    json.loads(text)  # self-check
    return text


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
