# Research: importing Google Sheets / Docs plans

Status: research note, 2026-11-11. Feeds `docs/DESIGN.md` §6 step 1 ("Source → `SheetDocument`")
and the `SheetReaders` contract in `src/MitCue.Core/Import/SheetModel.cs`.

Every statement below was checked in this container unless it is marked **UNVERIFIED**. Evidence is
one of: an HTTP request made here, a compiled and executed probe, a compiled Dalamud plugin, or a
primary source that is quoted (Google's shipped Sheets client code, Dalamud/ImGui/Chromium/ClosedXML
source, NuGet package metadata).

Environment used: .NET SDK 10.0.404; Dalamud dev assemblies in `~/.xlcore/dalamud/Hooks/dev`
(`Dalamud.dll` assembly version 15.0.4.0, native `cimgui.dll` = Dear ImGui 1.88); Dalamud.NET.Sdk 15.0.0.

---

## 0. Decisions

| Need | Approach | Evidence |
|------|----------|----------|
| Import from a link (public Sheet) | `GET https://docs.google.com/spreadsheets/d/<ID>/export?format=xlsx` (add `&gid=<gid>` for one tab). Parse in MitCue.Core with **DocumentFormat.OpenXml 3.5.1** (MIT), plus **ExcelNumberFormat 1.2.0** (MIT) for numeric cells. | §1, §4: all 16 tabs (2,163 cells) of both sheets come out **identical** to the Sheets API: values, fills, font colours, bold, strikethrough, rich-text runs, merges and hidden rows, with conditional formatting applied. |
| Import by paste (plain) | A "Paste from clipboard" button that calls `ImGui.GetClipboardText()`, then a Google-specific TSV parser. A cell is quoted only when it contains `\n` or `\t`. | §3: Google's client code, Dalamud's source, parser tests. |
| Import by paste (with colours, optional v2) | Read the Windows `"HTML Format"` clipboard flavour with P/Invoke (user32/kernel32) and parse the `<google-sheets-html-origin>` table. | §3: compiles in a Dalamud 15 plugin. Fragment extraction is tested against Chromium's encoder. |
| Google Docs | `GET https://docs.google.com/document/d/<ID>/export?format=docx`, parsed with the Wordprocessing part of the same OpenXml package: tables become grids and paragraphs become prose rows. `format=md` is a text-only fallback. | §5: tested on 4 public Docs. |
| Tests | JSON fixtures taken from the Sheets API, plus the raw xlsx and per-tab CSV exports, in `tests/fixtures/sheets/` | §2 |

---

## 1. Export endpoints (step 1)

Google **is reachable from this container**. These were unauthenticated `curl -L` requests through the
agent proxy on 2026-11-11, against both example sheets. The plugin will make the same requests from the
user's Windows PC.

### 1.1 Results

`lpdu` = `1Dlnye-i7SZ0ZZ7TY7r8SKMu3nMj_PZI45zy2QqDzuCU`, `ctc` = `1chKttd7POMf-aRLMAzQjNHDIgVzOKmLk9igSt-o2X6s`;
prefix `https://docs.google.com/spreadsheets/d/<ID>/`. Status is the final status after redirects.
"redir" is the number of redirects.

| Endpoint | Sheet | Status | Content-Type | Bytes | redir |
|----------|-------|--------|--------------|-------|-------|
| `export?format=xlsx` | lpdu | 200 | `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` | 420,485 (390 KB of it is one embedded PNG) | 1 (307) |
| `export?format=xlsx` | ctc | 200 | same | 188,206 | 1 (307) |
| `export?format=xlsx&gid=777200` | ctc | 200 | same; **one-tab workbook** ("TOP – Scholar" only) | 68,139 | 1 |
| `export?format=csv&gid=<gid>` | lpdu (13 tabs) | 200 | `text/csv; charset=utf-8` | 422–1,735 | 1 |
| `export?format=csv&gid=<gid>` | ctc (3 tabs) | 200 | `text/csv; charset=utf-8` | 8,440–10,225 | 1 |
| `export?format=tsv&gid=<gid>` | lpdu / ctc | 200 | `text/tab-separated-values; charset=utf-8` | 420–1,731 / 8,390–10,173 | 1 |
| `export?format=csv` (no gid) | both | 200 | `text/csv` | byte-identical to the first tab | 1 |
| `gviz/tq?tqx=out:csv&sheet=<name>` | lpdu / ctc | 200 | `text/csv; charset=utf-8` | 486–2,092 / 11,889–13,735 | 0 |
| `gviz/tq?tqx=out:csv&gid=<gid>` | both (first tab tested) | 200 | `text/csv` | same as by name (497 / 11,889) | 0 |
| `gviz/tq?tqx=out:json&sheet=…` | lpdu / ctc | 200 | `application/javascript` | 1,481 / 17,322 | 0 |
| `gviz/tq?tqx=out:html&sheet=…` | lpdu / ctc | 200 | `text/html` | 2,155 / 29,594 | 0 |
| `htmlview` (`?gid=` too) | lpdu / ctc | 200 | `text/html` | 73,425 / 71,313 | 0 |
| `pubhtml` | both | **401** | `text/html` (not published to the web) | ~9,090 | 0 |
| `export?format=html&gid=…` | both | **400** | `text/html` | 7,982 | 0 |
| `export?format=zip` | lpdu / ctc | 200 | `application/zip` (one HTML file per tab + `resources/sheet.css` + images) | 472,916 / 19,869 | 1 |
| `export?format=ods` / `pdf` | lpdu | 200 | `application/vnd.oasis.opendocument.spreadsheet` / `application/pdf` | 408,587 / 532,607 | 1 |
| `export?format=xlsx` with an unknown ID | – | **404** | `text/html` | 8,007 | 0 |
| `export?format=csv&gid=123` (gid that does not exist) | ctc | **400** | `text/html` | 7,982 | 0 |

The same xlsx downloads were repeated with .NET `HttpClient` (default handler, console probe
`--download` mode). The 307 redirect to `doc-…-sheets.googleusercontent.com` is followed
automatically, the body parses, and an unknown ID gives `404 text/html`.

### 1.2 Observations that matter for the importer

- **Success check:** treat the response as a workbook only if it is 200 **and** the Content-Type is
  `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`. Every failure seen came back as
  `text/html`. A *private* (not link-shared) sheet could not be tested: **UNVERIFIED**. Expect an HTML
  login page or a 401/403, which the Content-Type check also catches. In that case show "the sheet is
  not shared publicly; use File → Share → Anyone with the link, or paste instead".
- **Title and tab name:** the xlsx has no `docProps`, so the spreadsheet title is not inside it. It is in
  `Content-Disposition`. `HttpResponseMessage.Content.Headers.ContentDisposition.FileNameStar` returns
  `"Carry the Clear: TOP Healing Plan.xlsx"` for the whole workbook and
  `"Carry the Clear: TOP Healing Plan - TOP – Scholar.xlsx"` for the `&gid=777200` download. This was
  verified with .NET, which decodes the RFC 5987 `filename*`.
- **Sheet names inside the xlsx are sanitised:** Excel does not allow `/`, so Google writes `P1/P2` as
  **`P1P2`** and `WAR/DRK` as **`WARDRK`**. The en dash is kept (`TOP – White Mage`). The tab order is the
  same as in Google. **gids are not in the xlsx.** The original names and gids are in the `htmlview` page as
  `items.push({name: "P1\/P2", pageUrl: "…", gid: "871196336", …})`, one entry per tab in tab order. This
  was verified on both sheets, but the page is undocumented, so use it only as a best-effort extra.
- **CSV/TSV exports:** one tab per request (`gid`). Line endings are CRLF and there is no final newline.
  Trailing empty rows and columns are dropped, leading ones are kept, so A1 positions stay valid. Values
  are byte-identical to the API's `formattedValue` for all 16 tabs (checked). Colours, rich text and
  merges are lost. Fields containing a comma are quoted. Quotes and newlines did not occur in the samples,
  so quoting for those is **UNVERIFIED**.
- **`gviz` is unsuitable:** it **drops empty rows**, so row positions move: ALOO 22→6 rows, WAR/DRK 13→10,
  Carry the Clear 68→62/63. It also quotes every field and returns all grid columns, including the hidden
  I:U. Lookup by `sheet=<name>` works for names with `/` and with the en dash (URL-encoded).
- **`htmlview`** is now a JavaScript shell. The static HTML has no cell data (0 `<table>`, 0 `<td>`).
- **Published-to-web links** (`/spreadsheets/d/e/2PACX-…/pubhtml`, export `…/pub?output=xlsx`):
  **UNVERIFIED**, because neither sample is published.
- **Link parsing:** take the ID from `/spreadsheets/d/<ID>/` and an optional `gid=<n>` from the query
  *or* the fragment (`…/edit?gid=777100#gid=777100`). Browsers do not send the fragment, so read it
  client-side before building the export URL.

---

## 2. Test fixtures (step 2)

### 2.1 Layout

```
tests/fixtures/sheets/
  README.md                 provenance (ids, capture date, how to regenerate)
  normalize_sheets.py       Sheets API response -> fixture JSON (the generator used)
  .gitattributes            "* -text": keep bytes exact (CSV exports are CRLF, no final newline)
  lpdu/<tab>.json           13 tabs, LPDU TOP Mitigation Sheet
  lpdu/export/lpdu.xlsx     export?format=xlsx (whole workbook)
  lpdu/export/csv/<tab>.csv export?format=csv&gid=<gid>
  carry-the-clear/<tab>.json            3 tabs, "Carry the Clear: TOP Healing Plan"
  carry-the-clear/export/carry-the-clear.xlsx
  carry-the-clear/export/csv/<tab>.csv
```

`<tab>` = tab name in lower case, with every run of characters outside `[a-z0-9]` replaced by `-` and
leading/trailing `-` removed (`P1/P2` → `p1-p2`, `TOP – WHM + SCH` → `top-whm-sch`). Each per-sheet
directory holds only tab JSON files plus the `export/` subfolder, so
`SheetReaders.FromFixtureDirectory` can read `*.json` (non-recursive) and sort by `index`.

| File | `tab` | `gid` | `index` | rows×cols | merges | hiddenRows |
|------|-------|-------|---------|-----------|--------|------------|
| `lpdu/aloo.json` | ALOO | 0 | 0 | 22×7 | – | – |
| `lpdu/p1-p2.json` | P1/P2 | 871196336 | 1 | 12×9 | – | – |
| `lpdu/p3.json` | P3 | 984942427 | 2 | 10×11 | – | – |
| `lpdu/p4.json` | P4 | 147571683 | 3 | 9×11 | – | – |
| `lpdu/p5.json` | P5 | 1797012887 | 4 | 13×11 | – | – |
| `lpdu/p6.json` | P6 | 197143206 | 5 | 15×11 | – | – |
| `lpdu/general-tank-mit-til-p5.json` | General Tank Mit til P5 | 745722413 | 6 | 13×4 | – | – |
| `lpdu/war-drk.json` | WAR/DRK | 985316283 | 7 | 13×4 | – | – |
| `lpdu/war-gnb.json` | WAR/GNB | 559379235 | 8 | 13×4 | – | – |
| `lpdu/war-pld.json` | WAR/PLD | 124315180 | 9 | 13×4 | – | – |
| `lpdu/drk-gnb.json` | DRK/GNB | 643482148 | 10 | 13×4 | – | – |
| `lpdu/drk-pld.json` | DRK/PLD | 297208824 | 11 | 13×4 | – | – |
| `lpdu/gnb-pld.json` | GNB/PLD | 968846640 | 12 | 13×4 | – | – |
| `carry-the-clear/top-white-mage.json` | TOP – White Mage | 777100 | 0 | 68×5 | 8 | 0–4 |
| `carry-the-clear/top-scholar.json` | TOP – Scholar | 777200 | 1 | 68×5 | 8 | 0–4 |
| `carry-the-clear/top-whm-sch.json` | TOP – WHM + SCH | 777300 | 2 | 68×5 | 8 | 0–4 |

Totals: LPDU has 1,143 cells (513 with a value, 5 with rich text). Carry the Clear has 1,020 cells
(676 with a value, 24 with rich text). Neither sheet has notes, strikethrough or formulas.
LPDU has one numeric cell. Sheet locales are `en_GB` (LPDU) and `en_US` (Carry the Clear).

### 2.2 JSON format

```json
{
  "tab": "TOP – White Mage",
  "gid": 777100,
  "index": 0,
  "rows": [
    [
      {"v":"1:21 / 1:27 / 1:33 / 1:39","bg":null,"fg":"#545454","bold":false,"strike":false,"runs":[],"note":null},
      {"v":"Pantokrator: line stacks + missiles (65k) ★★★ (multi-hit)","bg":null,"fg":"#d92f24","bold":false,"strike":false,"runs":[],"note":null},
      {"v":"Temp (1:18) / Bell (right after the 1st stack, ~1:22) / Plenary* (1:31)","bg":"#ebd2d3","fg":null,"bold":false,"strike":false,
       "runs":[{"start":0,"fg":null,"bold":false},{"start":56,"fg":"#1154cc","bold":true}],"note":null}
    ]
  ],
  "merges": [{"r0":5,"c0":0,"r1":5,"c1":1}, {"r0":8,"c0":2,"r1":10,"c1":4}],
  "hiddenRows": [0,1,2,3,4],
  "hiddenCols": []
}
```

The real files put one cell per line so diffs stay readable. They are UTF-8 with no `\u` escaping and LF
line endings. Rules, as implemented in `normalize_sheets.py`:

1. **Grid.** `rows[r][c]` is A1 cell (column c+1, row r+1), 0-based and rectangular. **Leading** empty
   rows/columns are kept so references stay valid. **Trailing** rows/columns are trimmed when no cell in
   them has a non-empty `formattedValue` or a note. A whitespace-only value counts as content, as in
   Google's CSV export (LPDU P3 rows 9–10 hold only spaces). Trimmed sizes equal the CSV export sizes for
   all 16 tabs.
2. **`v`** is `formattedValue` exactly as returned, including leading and trailing spaces and ★ – ⏱ × ≈
   ·. It is `null` when the cell has no value.
3. **`bg` / `fg`** come from `effectiveFormat.backgroundColorStyle` and
   `effectiveFormat.textFormat.foregroundColorStyle`. Theme colours are resolved through
   `spreadsheetTheme`, falling back to the deprecated `backgroundColor`/`foregroundColor`. The value is
   `#rrggbb` (lower case), with each channel = round(component × 255). **A missing component means 0**, so
   `{"rgbColor":{}}` is black (proto3 default). `bg` is `null` when the colour is `#ffffff`; `fg` is `null`
   when it is `#000000`. *Effective* means the colour **includes conditional formatting**: Carry the Clear's
   column-B colours exist only as CF rules (§4.4).
4. **`bold` / `strike`** come from `effectiveFormat.textFormat`.
5. **`runs`** come from `textFormatRuns` and are `[]` when the API returns none. `start` is the API
   `startIndex` in **UTF-16 code units**, which is the C# string index; a missing value means 0.
   Each run's `fg`/`bold` is the run's own value when set, otherwise the cell's effective value, because
   API runs inherit the cell format. `fg: null` again means black.
6. **`note`** is the cell note. Both sheets have 0 notes.
7. **`merges`** come from `sheets.merges`. The API `GridRange` has exclusive end indexes; the fixture uses
   **inclusive** `{r0,c0,r1,c1}`, matching `CellRange`. Merges are clipped to the trimmed grid and sorted.
8. **Extras** beyond the original spec: `index` (tab position) and `hiddenRows`/`hiddenCols`
   (`hiddenByUser` or `hiddenByFilter`, 0-based, inside the grid). `SheetTab` has no hidden-row fields
   yet. Add them if the layout detector should skip hidden rows. In these two sheets, hidden rows and
   columns are empty.

### 2.3 How the JSON was captured

These are Google Sheets MCP `get_spreadsheet` calls. LPDU had no `ranges` (all 13 tabs). Carry the Clear
used `ranges` `'TOP – White Mage'!A1:U72`, `'TOP – Scholar'!A1:U72` and `'TOP – WHM + SCH'!A1:U72`.
A whole-spreadsheet scan of values and notes (1,032 grid rows) showed nothing below row 68 and nothing
right of column E.

```
includeGridData: true
fields: spreadsheetId, properties.title, properties.locale, properties.timeZone, properties.spreadsheetTheme,
  sheets.properties.sheetId, sheets.properties.title, sheets.properties.index, sheets.properties.hidden,
  sheets.properties.gridProperties, sheets.merges, sheets.data.startRow, sheets.data.startColumn,
  sheets.data.rowMetadata.hiddenByUser, sheets.data.rowMetadata.hiddenByFilter, sheets.data.columnMetadata.hiddenByUser,
  sheets.data.rowData.values.formattedValue, sheets.data.rowData.values.note, sheets.data.rowData.values.hyperlink,
  sheets.data.rowData.values.textFormatRuns, sheets.data.rowData.values.effectiveFormat.backgroundColor,
  sheets.data.rowData.values.effectiveFormat.backgroundColorStyle,
  sheets.data.rowData.values.effectiveFormat.textFormat.{bold,italic,strikethrough,foregroundColor,foregroundColorStyle}
```

The MCP tool rejects parenthesised field masks (`properties(title,locale)` returns "Invalid field"), so
dotted paths are required. Large responses (388 KB and 1.28 MB here) are written by the harness to a
file under `~/.claude/projects/…/tool-results/`. That file was copied and passed to
`python3 -I normalize_sheets.py <file> <out-dir>`.

### 2.4 Cross-checks

- **API vs CSV export:** every `v` in all 16 tabs is identical to the per-tab CSV export (same dimensions).
- **API vs xlsx export:** the OpenXml probe (§4) read both xlsx files and compared every cell field
  (`v`, `bg`, `fg`, `bold`, `strike`, `runs`, `note`), `merges`, `hiddenRows` and `hiddenCols` with the JSON.
  Result: **2,163 cells, 0 mismatches**. Without conditional-format evaluation there were 141
  mismatches, all in Carry the Clear column B (`fg`).

### 2.5 What the fixtures show (input for layout detection and cell parsing)

- **LPDU:** the header row and phase column are black (`bg #000000`) with white text (`fg #ffffff`). Each
  role column has its own fill. In P1/P2: MT/OT `#98bfff`, H1/H2 `#b6d7a8`, M1/M2 `#ea9999`, R1 `#dd7e6b`,
  R2 `#c27ba0`. Footnote markers are **red rich-text runs** (`#ff0000`): P6 `Rep*` with only the `*` red,
  and `Party Mit after 6th puddle + Rep*`. The footnote sits in a later row: P6 B11
  `*WAR/PLD use Mit after Protean befor Stack`, red text on black with a white `*` run. Some cells hold only spaces, or long runs of spaces
  before the text. The only number is `'General Tank Mit til P5'!B9` = 0.3, shown as `30%`. ALOO has a
  hyperlink cell (discord.gg) and P6 has an imgur link. The "General Tank Mit til P5" tab carries a large
  image, which is not part of the fixture.
- **Carry the Clear:** the legend in C12:E14 is merged. **Grey runs (`#808080`) = optional**, and **blue
  bold runs (`#1154cc`) = the author's own placement ("Plenary\*")**; both are run-level and inside cells.
  Column A times are `#545454` italic. Column B mechanic colour = lethality via conditional formatting:
  ★★★★ `#d100d1`, ★★★ `#d92f24`, ★★ `#c25200`, ★ `#926c02`, text matching `enrage|dodge` `#4974b9`,
  anything else `#178038`. Column C is filled `#ebd2d3` (WHM) or `#c3d7cd` (SCH), and the combined tab has
  both. Header cells C15:C16, D15:D16 and E15:E16 are merged. Rows 1–5 and columns I–U are hidden and
  empty.

---

## 3. Clipboard: what Sheets copies and what a Dalamud plugin can read (step 3)

### 3.1 What Google Sheets writes when you copy a range

Source: Google's shipped Sheets web client, module `m=edit` of
`/_/spreadsheets/_/js/k=spreadsheets.waffle_js_prod.en.hG5jdPl7x0c.es5.O/…`, fetched 2026-11-11. The
code is minified, so function names change with every Google build; the quotes are the evidence.

- **Formats on copy-range:** `text/plain`, `text/html`, and Google-internal types
  (`application/x-vnd.google-docs-internal-clip-id`, `application/x-vnd.google-spreadsheet-compact-table+json`,
  `application/x-vnd.google-apps.spreadsheet-anchor-rect`, …). Quote:
  `k.push(new xV("text/plain",h.iB("text/vnd.google-apps.spreadsheet")));h.iB("text/vnd.google-apps.spreadsheet+html")!=null&&k.push(new xV("text/html",…))`.
- **`text/plain` = TSV with Google's own quoting rule**, from the copy loop and the per-cell writer:
  ```js
  function rBm(a,c,e,f){a=sCm(a,e,f);ji(a,10)>=0||ji(a,9)>=0?rZ(rZ(rZ(c,'"'),a.replace(bCm,'""')),'"'):rZ(c,a)}
  // bCm=RegExp('"',"g");  ji(a,c) = a.indexOf(String.fromCodePoint(c))  -> 10 = "\n", 9 = "\t"
  Rd.ga&&!Kd&&rZ(oe,Rd.Bb?"\r\n":"\n");   // between rows
  zf||rZ(oe,"\t");                        // between cells
  ```
  This gives the following rules:
  - Cells are separated by `\t` and rows by `\n` or `\r\n` (a flag; Windows behaviour **UNVERIFIED**,
    and it does not matter after Dalamud's normalisation). There is no trailing newline.
  - A cell is wrapped in `"…"`, with inner `"` doubled, **only if it contains a newline or a tab**.
  - **A cell that contains quotes but no newline or tab is written raw**: `"Kera" on MT` stays exactly
    that. A standard RFC 4180 parser would misread it.
  - From reading the loop: in a merged range the anchor carries the value and covered cells are empty
    fields.
- **`text/html`** comes from the same loop and from the `fCm`/`lCm`/`zyk` functions. It contains a
  `<google-sheets-html-origin>` wrapper and `<style type="text/css"><!--td {border: 1px solid …;}br {mso-data-placement:same-cell;}--></style>`.
  It has a `<table … data-sheets-root="1">` with `<colgroup><col width="…"/>`, rows
  `<tr style="height:Npx;">`, and cells `<td style="background-color:…;color:…;font-weight:bold;font-style:italic;text-decoration:line-through;…" rowspan=… colspan=…>`.
  Rich-text runs are `<span style="…">` and links are `<a class="in-cell-link" href=…>`. A newline in a
  cell becomes `<br/>`, and `" ' & < >` are escaped. The attribute registry includes `data-sheets-value`,
  `data-sheets-formula`, `data-sheets-numberformat`, `data-sheets-userformat`, `data-sheets-note`,
  `data-sheets-textstyleruns` and `data-sheets-hyperlink`. Which of these appear on a plain copy is
  **UNVERIFIED**. There is a "Table is too large to copy." path; the limit is **UNVERIFIED**.

### 3.2 How the browser hands it to Windows

Source: Chromium `main`, `ui/base/clipboard/clipboard_format_type_win.cc` and `clipboard_util_win.cc`.

- `text/plain` becomes `CF_UNICODETEXT`.
- `text/html` becomes the registered format **`"HTML Format"`** (CF_HTML). The payload is UTF-8: the
  header `Version:0.9\r\nStartHTML:%010zu\r\nEndHTML:%010zu\r\nStartFragment:%010zu\r\nEndFragment:%010zu\r\n`,
  an optional `SourceURL:…\r\n`, then `<html>\r\n<body>\r\n<!--StartFragment-->` + html +
  `<!--EndFragment-->\r\n</body>\r\n</html>`. The offsets are **byte** offsets.
- Custom MIME types go into `"Chromium Web Custom MIME Data Format"`, which is not useful here.
- Edge is Chromium. Firefox behaviour is **UNVERIFIED**.

### 3.3 What a Dalamud plugin can read

- **`ImGui.GetClipboardText()`** (static, returns `string`; there is also `GetClipboardTextU8()`) works.
  It is backed by Dalamud's internal `ImGuiClipboardFunctionProvider` (source: goatcorp/Dalamud `master`
  @43b34118, matching the remark in the installed `Dalamud.xml`). It calls `OpenClipboard`, reads
  **`CF_UNICODETEXT` only**, applies `str.ReplaceLineEndings("\r\n")` and passes UTF-8 to ImGui. So the
  plugin gets **plain text with every line ending converted to `\r\n`**, including those inside quoted
  cells. .NET's `ReplaceLineEndings` also turns U+0085, U+2028, U+2029 and U+000C into `\r\n` but keeps
  U+000B (tested). The HTML flavour is not exposed. Dalamud's OLE helper `StaThreadService` is `internal`
  (checked by metadata), so plugins cannot use it.
- **Do not depend on pasting into an ImGui text box.** ImGui 1.88's `InputTextFilterCharacter`
  (gc-imgui @33d28f5, the version in Dalamud's `cimgui.dll`) drops `\t` unless
  `ImGuiInputTextFlags.AllowTabInput` is set, always drops `\r`, and keeps `\n` only with `Multiline`.
  Pasting TSV into a normal box therefore **destroys the columns**. Use a button that calls
  `ImGui.GetClipboardText()`. If a paste box is wanted, use
  `ImGui.InputTextMultiline(label, ref buf, maxLength, size, ImGuiInputTextFlags.AllowTabInput)` with a
  large `maxLength`. Both calls compile against Dalamud 15.
- **HTML flavour:** plain P/Invoke works from any thread, and no STA thread or WinForms is needed
  (Dalamud.NET.Sdk does not set `UseWindowsForms`). This code compiled inside a Dalamud.NET.Sdk 15.0.0
  plugin with 0 warnings. File:
  `/tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/probes/sheets-plugin/Probe/Win32Clipboard.cs`:
  ```csharp
  [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
  private static partial uint RegisterClipboardFormatW(string lpszFormat);          // "HTML Format"
  [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool IsClipboardFormatAvailable(uint format);
  [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool OpenClipboard(nint hWndNewOwner);                     // 0, as Dalamud does
  [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool CloseClipboard();
  [LibraryImport("user32.dll", SetLastError = true)] private static partial nint GetClipboardData(uint uFormat);
  [LibraryImport("kernel32.dll", SetLastError = true)] private static partial nint GlobalLock(nint hMem);
  [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool GlobalUnlock(nint hMem);
  [LibraryImport("kernel32.dll", SetLastError = true)] private static partial nuint GlobalSize(nint hMem);
  ```
  The method copies `GlobalSize` bytes, cuts at the first NUL, reads `StartFragment`/`EndFragment` from
  the ASCII header and decodes that byte range as UTF-8. The extraction was tested against a C# port of
  Chromium's `HtmlToCFHtml`, with and without `SourceURL`, using non-ASCII text (★ – ⏱). The calls
  themselves have not been run on Windows: **UNVERIFIED** at runtime.

### 3.4 Parsing the plain-text flavour

`/tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/probes/sheets/GoogleTsv.cs`
(95 lines) is ready to move into `SheetReaders.FromDelimited(text, '\t')`:

- At the start of each field, if the next character is `"`, try a quoted field. Read until a `"` that is
  not doubled. Accept the quoted field only if the closing quote is followed by `\t`, `\r`, `\n` or the
  end, **and** the unescaped body contains `\n`, `\r` or `\t` (Google's condition). Otherwise read the
  field raw up to the next `\t`, `\r` or `\n`.
- Rows end at `\r\n`, `\n` or `\r`. A trailing newline is tolerated. Inside cells, normalise `\r\n` to `\n`.

Tests (`--selftest`, all pass): simple grid, quoted multi-line cell, raw quotes left alone, doubled
quotes plus newline, empty trailing cells, quoted tab, the line-ending behaviour above, and a **round
trip of all 16 fixture tabs plus a tricky grid**. The round trip is: Google's writer rule → Dalamud's
`ReplaceLineEndings("\r\n")` → parser; 17/17 grids are identical.

### 3.5 Recommendation for paste

- **v1:** a plain-text paste button. Plain text keeps every cell and its position, but loses colours. For
  Carry the Clear that means grey = optional and blue = Plenary\* are lost, so the review screen should
  let the user mark optional entries, or suggest link import, which keeps everything.
- **v2 (optional):** HTML paste for sheets that are not public. It carries colours, runs, merges and
  notes. It needs a tolerant HTML table parser: inline `style=` and spans. HtmlAgilityPack and AngleSharp
  were not evaluated (**UNVERIFIED** sizes and licences). The markup is machine-generated and regular, so
  a small hand-written tokenizer is also an option.

---

## 4. Reading xlsx in the plugin (step 4)

### 4.1 Comparison

Latest stable versions on api.nuget.org as of 2026-11-11. Licences and dependencies are from the
nuspecs. Sizes are the DLL for the TFM used and, where noted, the full set of DLLs actually copied to
`bin`. API capabilities were checked by compiling and running (OpenXml, ClosedXML) or by metadata
inspection of the public API (ExcelDataReader, MiniExcel, ExcelNumberFormat).

| | **DocumentFormat.OpenXml** | **ClosedXML** | **ExcelDataReader** | **MiniExcel** |
|---|---|---|---|---|
| Version | 3.5.1 | 0.105.1 | 3.9.0 | 1.46.0 |
| Licence | MIT (Microsoft, dotnet/Open-XML-SDK) | MIT | MIT | Apache-2.0 |
| Dependencies (net10) | DocumentFormat.OpenXml.Framework 3.5.1 → System.IO.Packaging 10.0.2 | ClosedXML.Parser 2.0.0, DocumentFormat.OpenXml **[3.1.1, 4.0) → resolves 3.1.1**, ExcelNumberFormat 1.1.0, RBush.Signed 4.0.0, SixLabors.Fonts **[1.0.0, 3.0) → resolves 1.0.0** | none (net8.0 lib) | none (net10.0 lib) |
| Size in output | 7,985,120 + 478,688 + 159,504 B ≈ **8.6 MB** (+36 KB ExcelNumberFormat) | **10.06 MB** total (ClosedXML 1.69 MB, OpenXml 3.1.1 6.35 MB, SixLabors.Fonts 1.14 MB, …) | 209 KB | 442 KB |
| Rich-text runs | yes: `SharedStringItem` → `Run` → `RunProperties` (`Color`, `Bold`, `Strike`) | yes: `cell.HasRichText`, `GetRichText()` → `IXLRichString.FontColor/Bold/Strikethrough` | **no** | **no** |
| Font colour, fill | yes (Stylesheet `Fonts`/`Fills`/`CellFormats`; `ColorType.Rgb/Theme/Tint/Indexed`) | yes (`Style.Font.FontColor`, `Style.Fill.BackgroundColor`, `wb.Theme.ResolveThemeColor`) | **no**: `CellStyle` has only `FontIndex`, `NumberFormatIndex`, `IndentLevel`, alignment, `Hidden`, `Locked`, with no font table | **no**: style types are write-side only (`OpenXmlStyleOptions`, `OpenXmlHeaderStyle.BackgroundColor`) |
| Merges | yes (`MergeCells`) | yes (`ws.MergedRanges`) | yes (`MergeCells`) | only `FillMergedCells` on read |
| Notes | yes (`WorksheetCommentsPart`) | yes | no | no |
| Conditional formatting | rules only (`ConditionalFormatting`, `DifferentialFormats`); evaluate yourself | rules only (`ws.ConditionalFormats`); not evaluated | no | no |
| Number display | none (raw value); add ExcelNumberFormat | `GetFormattedString()` gives `"30%"` | `GetNumberFormatString` | values only |
| Also reads .docx (Google Docs) | **yes**, same package (`WordprocessingDocument`) | no | no | no |
| Result on the two Google exports | **0 mismatches vs the API** (2,163 cells), §2.4 | loads both and prints the same runs, fills, merges and `30%`; reports 6 CF rules per CtC tab | – | – |

Notes:
- **ClosedXML** pins an older OpenXml (3.1.1) and pulls in SixLabors.Fonts. NuGet resolves the lowest
  allowed version, 1.0.0, which is Apache-2.0. SixLabors.Fonts 2.x/3.x use the "Six Labors Split License"
  (3.2.0's LICENSE file was read), so a later bump would change the licence terms. It also does not
  evaluate conditional formatting, so it only saves the style lookups and number formatting.
- **ExcelDataReader** and **MiniExcel** read values only. They cannot see grey = optional, blue
  placements or lethality colours, so they are ruled out.
- Not prototyped: a BCL-only reader (`System.IO.Compression.ZipArchive` + `XmlReader`) needs no packages
  but means more code to own. Consider it only if the 2.3 MB compressed plugin size (§4.3) becomes a
  problem.

### 4.2 Recommendation: DocumentFormat.OpenXml 3.5.1 + ExcelNumberFormat 1.2.0, in MitCue.Core

**Proof:** `/tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/probes/sheets`
(console, net10.0):

```
export PATH=$HOME/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1
cd /tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/probes/sheets && dotnet build -c Release
F=/home/user/MitCue/tests/fixtures/sheets
dotnet bin/Release/net10.0/SheetsProbe.dll $F/carry-the-clear/export/carry-the-clear.xlsx --fixtures $F/carry-the-clear
dotnet bin/Release/net10.0/SheetsProbe.dll $F/lpdu/export/lpdu.xlsx --fixtures $F/lpdu
dotnet bin/Release/net10.0/SheetsProbe.dll --selftest $F          # TSV + CF_HTML tests (§3)
dotnet bin/Release/net10.0/SheetsProbe.dll --download "<export url>"  # HttpClient + Content-Disposition
dotnet bin/Release/net10.0/SheetsProbe.dll --docx <file.docx>       # Google Docs (§5)
```

Output excerpt:

```
carry-the-clear.xlsx: 3 tabs read in 618 ms
colours: rgb=310 theme=0 indexed=0 auto=0; rich strings=24 runsWithoutRPr=0 runsWithoutColor=0; numeric=0 …; conditional-format hits=141 unsupportedRuleEvals=0
== [0] "TOP – White Mage" 68x5 merges=A6:B6,C9:E11,C12:E12,C13:E13,C14:E14,C15:C16,D15:D16,E15:E16 hiddenRows=[0,1,2,3,4] hiddenCols=[]
  C12  RICH bg=- cellFg=- [0:default,bold "Built from "] [11:#1154cc,bold "M-Spec"] [17:default,bold "'s top-rDPS White Mage k…"]
  C18  RICH bg=#ebd2d3 cellFg=- [0:default "Temp (1:18) / Bell (righ…"] [56:#1154cc,bold "Plenary* (1:31)"]
  C43  RICH bg=#ebd2d3 cellFg=- [0:default "Benison (0:07)"] [14:#808080 " / Aquaveil (0:07)"]
COMPARED 1020 cells in 3 tabs: 0 mismatches
lpdu.xlsx: 13 tabs read in 278 ms
colours: rgb=157 theme=3 …; numeric=1
  C4   RICH bg=#98bfff cellFg=- [0:default "Party Mit after 6th pudd…"] [32:#ff0000 "*"]
  B11  RICH bg=#000000 cellFg=#ff0000 [0:#ffffff "*"] [1:#ff0000 "WAR/PLD use Mit after Pr…"]
COMPARED 1143 cells in 13 tabs: 0 mismatches
```

The reader is `XlsxSheetReader.cs` (485 lines including conditional formatting), written to be
lifted into `SheetReaders.FromXlsx`. Every API it uses compiled against 3.5.1:

- `SpreadsheetDocument.Open(Stream, false)` → `WorkbookPart`: `.Workbook.Sheets.Elements<Sheet>()`
  (`Sheet.Name`, `Sheet.Id`), `.GetPartById(id)` → `WorksheetPart.Worksheet`, `.ThemePart.Theme.ThemeElements.ColorScheme`,
  `.WorkbookStylesPart.Stylesheet` (`Fonts`, `Fills`, `CellFormats`, `NumberingFormats`, `DifferentialFormats`),
  `.SharedStringTablePart.SharedStringTable.Elements<SharedStringItem>()`.
- Cells: `SheetData` → `Row` (`RowIndex`, `Hidden`, `CustomFormat`, `StyleIndex`) → `Cell` (`CellReference`,
  `DataType` vs `CellValues.SharedString/InlineString/Number/Boolean`, `CellValue.Text`, `StyleIndex`,
  `InlineString`, `CellFormula`).
- Text: `SharedStringItem.Elements<Run>()` (`Run.RunProperties`, `Run.Text`) or `GetFirstChild<Text>()`.
- Styles: `CellFormat.FontId/FillId/NumberFormatId`; `Font.Color/Bold/Strike` (on/off elements derive from
  `BooleanPropertyType`; a present element with no `val` means true); `Fill.PatternFill.PatternType ==
  PatternValues.Solid` → `ForegroundColor`; `ColorType.Rgb/Theme/Tint/Indexed/Auto`.
- Sheet extras: `Columns`/`Column` (`Min`, `Max`, `Hidden`, `Style`), `MergeCells`/`MergeCell.Reference`,
  `WorksheetCommentsPart.Comments.CommentList` (`Comment.Reference`, `CommentText.InnerText`),
  `ConditionalFormatting.SequenceOfReferences` and `ConditionalFormattingRule`
  (`Type`, `Priority`, `Text`, `FormatId`, child `Formula`).
- `ExcelNumberFormat.NumberFormat(string)` with `.IsValid` and `.Format(object, CultureInfo, bool isDate1904)`.

### 4.3 It works inside a Dalamud plugin (compile and packaging)

`/tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/probes/sheets-plugin/` contains
`CoreLib` (net10.0, holds the packages, like MitCue.Core) and `Probe` (Dalamud.NET.Sdk 15.0.0 plugin,
like MitCue). The plugin uses `ImGui.GetClipboardText()`, `InputTextMultiline(..., AllowTabInput)`,
`Win32Clipboard`, and `HttpClient` download → `XlsxSheetReader.Read`. `dotnet build -c Release`:
**0 warnings, 0 errors**. `packages.lock.json` lists DocumentFormat.OpenXml 3.5.1,
DocumentFormat.OpenXml.Framework 3.5.1, ExcelNumberFormat 1.2.0 and System.IO.Packaging 10.0.2 as
transitive. **DalamudPackager's `latest.zip` contains them all:** 2,282,175 B compressed, 8.74 MB
unpacked. Dalamud ships none of these assemblies (checked in `Hooks/dev`), so there is nothing to
conflict with. Loading them inside the game is **UNVERIFIED** (the game cannot run here). On Windows the
shared `Microsoft.WindowsDesktop.App` framework, which Dalamud's runtimeconfig loads, may supply its own
System.IO.Packaging; either copy works.

### 4.4 Gotchas found with the real files (all handled in the probe)

1. **Conditional formatting.** The API's `effectiveFormat` includes CF results; the xlsx only has the
   rules. Carry the Clear column B has 6 rules on `B17:B20 B22:B28 B30:B38 B40:B41 B43:B55 B57:B68`, all
   `stopIfTrue="1"`, in this priority order:
   `containsText "★★★★"` → dxf font `FFD100D1`; `"★★★"` → `FFD92F24`; `"★★"` → `FFC25200`; `"★"` → `FF926C02`;
   `expression REGEXMATCH(B17,"enrage|dodge")` → `FF4974B9` (a Google-only function that Excel cannot
   evaluate); `notContainsBlanks LEN(TRIM(B17))>0` → `FF178038`. Applying the **first matching rule**
   (Google semantics) closes all 141 differences. Supported: containsText, notContainsText, beginsWith and
   endsWith (case-insensitive, like `SEARCH`), containsBlanks, notContainsBlanks, and
   `REGEXMATCH(<ref>,"<re>")`. Other rule types are counted and ignored. For MitCue the ★ count in the
   text already carries the lethality, but another sheet could mark "optional" through CF, so keep the
   evaluator.
2. **Theme colours.** LPDU fonts use `<color theme="1"/>` (336 cells). Excel's index order swaps
   0↔1 and 2↔3 relative to `<a:clrScheme>`, so theme 1 = `dk1` (#000000). This mapping matches the API
   on every compared cell, including ALOO row 1; the naive order would give white. Everything else in both files is ARGB `rgb="FFrrggbb"`.
   There are no indexed or auto colours.
3. **Rich text.** Every run in the Google exports has an `rPr` with an explicit `<color>`. 0 runs lack
   either. Run `start` = cumulative `string.Length` of the earlier runs (UTF-16), which equals the API
   `startIndex`. If a run has no `rPr`, the probe gives it the cell font; Excel's exact semantics here
   are **UNVERIFIED** and irrelevant for Google exports.
4. **Numbers.** Cells with no `t` attribute are numbers and need number formats. LPDU `B9` = `0.3` with
   built-in `numFmtId 9` → `"30%"`, matching the API. The built-in ID table is copied from ClosedXML
   `XLPredefinedFormat` (ids 1–4, 7, 9–22, 37–40, 45–49). Time and date cells, and locale-dependent
   output (Google formats with the sheet locale; the probe uses `InvariantCulture`), are **UNVERIFIED**
   because there are no such cells. Watch for sheets where times like `1:21` are real time values.
5. **Hidden rows and columns:** `<row hidden="1">` and `<col min max hidden="1">`. Carry the Clear has
   965 hidden rows (rows 1–5 and 73–1,032) and hidden columns I–U.
6. **Cells missing from `<sheetData>`** take the row style (when `customFormat="1"`), then the column
   style, then xf 0. This reproduces the API's colours for empty cells.
7. **Merges:** `<mergeCell ref="C9:E11"/>` → inclusive `CellRange(8,2,10,4)`.
8. **Fills in conditional formats (dxf)** use `bgColor` for solid fills. This is Excel's convention and
   was not exercised here (the CtC dxfs have `patternType="none"`): **UNVERIFIED** with Google data.
9. **Notes:** none in the samples. That Google exports notes as legacy comments (`xl/commentsN.xml`) is
   **UNVERIFIED**; the reader handles `WorksheetCommentsPart` anyway. Images (drawings) and hyperlinks
   are ignored.
10. **Performance:** 0.28–0.62 s per workbook on a cold start in this container, mostly JIT. Parse
    off the framework thread (`Task.Run`) and cap the download size (for example 20 MB).

---

## 5. Google Docs (step 5)

### 5.1 Endpoints

The prefix is `https://docs.google.com/document/d/<ID>/`. Tested with Google's public
"Docs API Quickstart" document `195j9eDD3ccgjQRttHhJPymLJUCOUjs-jmwTrekvdjFE` (title per
`Content-Disposition`; its body is "Sample doc"):

| Endpoint | Status | Content-Type | Bytes | redir |
|----------|--------|--------------|-------|-------|
| `export?format=txt` | 200 | `text/plain; charset=utf-8` | 13 (UTF-8 BOM + text) | 1 |
| `export?format=md` | 200 | `text/x-markdown; charset=utf-8` | 10 | 1 |
| `export?format=html` | 200 | `text/html; charset=utf-8` | 2,002 | 0 |
| `export?format=docx` | 200 | `application/vnd.openxmlformats-officedocument.wordprocessingml.document` | 6,547 | 1 |
| `export?format=odt` / `rtf` / `pdf` / `epub` / `zip` | 200 | odt / `application/rtf` / pdf / `application/epub+zip` / zip | 3,108,052 / 10,033 / 13,567 / 2,025 / 651 | 1 |
| `pub` | 401 | `text/html` (not published) | 9,075 | 0 |
| `mobilebasic`, `preview`, `edit` | 200 | `text/html` | 59,952 / 146,055 / 162,175 | 0 |

Three public FFXIV Docs found by web search were exported as html, txt, md and docx:
`1Dsmp2i8Nm5_1oaPvZstFISSA2ThGgu2gZVshNONpE30` ("An In Depth look at Paladin": 1 table, 347 paragraphs,
39 images), `1HwRiYvvRYPFD6uh4VM-R1eN42ozLz12MamFTdQVe0jQ` ("World Prog Logistics": 2 tables,
212 paragraphs) and `1wKE4Hj7asv5PsJT4w4mL5chDRMTtgUbdPCsJWqGsuJk` ("FFXIV The Epic of Alexander Raid
Strategy Guide": 40 images, no tables).
`Content-Disposition` `filename*` holds the document title (`World Prog Logistics.docx`).

### 5.2 How tables and prose come out

| Format | Prose | Tables | Formatting | Size |
|--------|-------|--------|------------|------|
| `txt` | UTF-8 **BOM**, **CRLF**, one paragraph per line | flattened: each cell's lines in order, and the next cell starts with `\t` on its first line. **Cannot be rebuilt into a grid reliably** | none | small (27–103 KB) |
| `md` | headings `#`, bold `**`, lists | GitHub pipe tables; **the first row becomes the header row**, with a `\| :---: \|` line inserted after it; **a line break inside a cell becomes U+000B** (vertical tab) | no colours | images are inlined as base64 `[imageN]: <data:image/png;base64,…>`: 10 MB for the TEA guide |
| `html` | `<p class="cN"><span class="cN">` with styles in **CSS classes** in `<head><style>` (`.c1{color:#…;font-weight:700}`) | `<table><tr><td colspan rowspan><p><span>`; line breaks `<br>` | colours via class rules; links rewritten through `https://www.google.com/url?q=…` | images inline as base64: 12–13.6 MB |
| `docx` | `w:body` → `w:p` in order, style in `w:pPr/w:pStyle` (`Title`, `Subtitle`, `Heading1`, …) | `w:tbl/w:tr/w:tc`; `w:tcPr/w:shd/@w:fill` (cell fill, `auto` = none), `w:gridSpan`, `w:vMerge`; line break `<w:br w:type="textWrapping"/>` | runs `w:r/w:rPr`: `w:color w:val="1155cc"`, `w:b`, `w:strike`, `w:highlight`, `w:shd`; hyperlinks via relationships (direct URLs) | images are separate parts (7.8–9.8 MB with images, 23 KB without) |

### 5.3 Recommendation

- **Primary:** `export?format=docx`, parsed with `DocumentFormat.OpenXml.Wordprocessing` (the same package
  as §4, no extra dependency). Verified with `SheetsProbe --docx`, using `WordprocessingDocument.Open`,
  `MainDocumentPart.Document.Body`, `Paragraph`/`ParagraphProperties.ParagraphStyleId`,
  `Run`/`RunProperties.Color/Bold/Strike/Shading/Highlight`, `Text`, `TabChar`, `Break`, `CarriageReturn`,
  `Table`/`TableGrid`/`TableRow`/`TableCell`, `TableCellProperties.Shading.Fill/GridSpan/VerticalMerge`
  (`MergedCellValues.Restart`) and `MainDocumentPart.ImageParts`. Output:
  ```
  P [Title] runs=[0:default] "An In Depth look at Paladin"
  TABLE #1: 2 rows, grid cols=2
    r0c0 span=1 vMerge=- shading=auto runs=[0:default,b][13:#1155cc] "2.40 GCD set\nhttp://ffxiv.ariyala.com/17C3Y"
  => 347 paragraphs, 1 tables, 39 images (separate parts)
  ```
  Map the document onto the existing model: **each `w:tbl` becomes one `SheetTab`** ("Table 1", …). A
  cell's text is its paragraphs joined by `\n` (`w:br` → `\n`, `w:tab` → `\t`), with runs from `rPr`, `bg`
  from `w:shd/@w:fill` (`auto` → null), and merges from `gridSpan` (horizontal) and `vMerge`
  restart/continue (vertical). **All paragraphs outside tables become one `SheetTab` named "Text"**, one
  row per paragraph: column A = text with runs, column B = style id (so headings such as "P3" can mark
  phases). The same layout detector and cell parser then handle Docs written as prose lines
  ("1:21 Pantokrator: Temperance").
- **Fallback:** `export?format=md` (text only). Split pipe tables on unescaped `|`, turn U+000B into `\n`,
  and **remove the trailing base64 image definitions before parsing**.
- **Avoid:** `txt` (tables are ambiguous) and `html` (class-based CSS, base64 images, redirected links).
- **UNVERIFIED:** Docs with multiple **tabs** (whether `export` includes every tab and how `?tab=t.…`
  behaves); suggestion-mode text and comments; private Docs (expect HTML instead of the docx MIME type,
  so check Content-Type as in §1.2); published Docs (`/document/d/e/2PACX…/pub`).

---

## 6. Mapping to `src/MitCue.Core/Import/SheetModel.cs`

| Contract | Implementation notes |
|----------|---------------------|
| `FromFixtureJson(json)` | `tab`→`SheetTab.Name`, `gid`→`Gid`, `rows[][]`→`SheetCell{Text=v, Bg=bg, Fg=fg, Bold=bold, Strike=strike, Runs=runs.Select(r=>new TextRun(r.start, r.fg, r.bold)), Note=note}`, `merges`→`CellRange(r0,c0,r1,c1)` (already inclusive). Ignore unknown properties (`index`, `hiddenRows`, `hiddenCols`) or add matching fields to `SheetTab`. |
| `FromFixtureDirectory(dir, title)` | `*.json` in `dir` (non-recursive; `export/` is a subfolder), ordered by `index`. |
| `FromDelimited(text, '\t', …)` | `GoogleTsv` rules (§3.4). For `','`, use RFC 4180 (Google's CSV export quotes fields that contain commas; other cases are **UNVERIFIED**). Pasted text has only `Text`; colours are null. |
| `FromXlsx(stream, title)` | `XlsxSheetReader` (§4.2/§4.4). `Gid` is null unless the download used `&gid=` (then use that gid). `Name` is the sanitised xlsx name; prefer the original names from `htmlview` or `Content-Disposition` (§1.2) so plan provenance (`src`) matches what the user sees in Sheets. Title comes from `Content-Disposition` `FileNameStar` without `.xlsx`. |
| *(new)* `FromDocx(stream, title)` | §5.3. |
| Download (plugin layer) | One static `HttpClient` (timeout about 30 s), only on user request (DESIGN NFR). Check status and Content-Type (§1.2), parse on a worker task, and keep the downloaded bytes for re-import and diagnostics. |

---

## 7. Open items (UNVERIFIED)

- Responses for a private sheet or doc (expected: HTML login page or 401/403 → "not shared" message).
- Published-to-web links (`/d/e/2PACX…`) for Sheets and Docs.
- On Windows: running the `"HTML Format"` P/Invoke reader and the actual Sheets clipboard HTML. Which
  `data-sheets-*` attributes a plain copy emits, and the size limit for HTML copies.
- Whether the text/plain row separator is `\r\n` or `\n` on Windows Chrome (irrelevant after Dalamud's
  normalisation).
- Firefox clipboard flavours.
- Google xlsx export of notes, of time/date values, and of locale-specific number formatting.
- Loading DocumentFormat.OpenXml inside the running game (packaging is verified; runtime is not).
- Google Docs multi-tab documents; comments and suggestions in Docs exports.

## Appendix: files produced by this research

- Report: `docs/research/sheets-import.md` (this file).
- Fixtures: `tests/fixtures/sheets/` (README.md, normalize_sheets.py, .gitattributes, 16 tab JSONs,
  2 xlsx, 16 CSVs).
- Probes (scratchpad, not in the repo):
  - `/tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/probes/sheets/`:
    `XlsxSheetReader.cs`, `GoogleTsv.cs`, `DocxProbe.cs`, `SelfTest.cs`, `Program.cs`, `SheetsProbe.csproj`
  - `/tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/probes/sheets-closedxml/`:
    ClosedXML comparison
  - `/tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/probes/sheets-plugin/`:
    Dalamud plugin plus core library (packaging proof, `Win32Clipboard.cs`)
  - `/tmp/claude-0/-home-user/0206fee7-d20d-52fb-82e7-16c1ec9b7da1/scratchpad/probes/apisurface/`:
    metadata dumper used for ExcelDataReader, MiniExcel, ExcelNumberFormat and the Dalamud/ImGui clipboard APIs
- Raw evidence (scratchpad `dl/`): endpoint probe bodies and `results.tsv`, Sheets client JS
  (`dl/sheetsjs/edit.js`), public Docs exports, NuGet packages. Sources: `research/repos2/dalamud`
  (goatcorp/Dalamud @43b34118), `research/imgui_widgets.cpp` (gc-imgui @33d28f5), `research/chromium/*.cc`.
