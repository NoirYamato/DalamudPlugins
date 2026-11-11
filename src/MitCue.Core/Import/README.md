# Import — front half

Steps 1–3 of the import pipeline (`docs/DESIGN.md` §6): read a sheet, detect each tab's layout, split cells
into structured parts. Nothing here knows about actions or mechanics; the back half (`PlanImporter.Import`:
action/mechanic resolution, phase calibration, timing) builds on these types. Every type has XML docs.

```
SheetReaders ──► SheetDocument ──► LayoutDetector ──► DocumentLayout ──► (per assignee cell) CellParser ──► ParsedCell
 xlsx / TSV / CSV / JSON           tabs, rows, cells      header, columns, phases, rows        items → steps → tokens
```

## Readers (`SheetModel.cs`, `Readers/`)

| Entry point | Notes |
|---|---|
| `SheetReaders.FromXlsx(stream, title[, XlsxReadOptions])` | Google `export?format=xlsx`. Text as displayed (`_x000D_` escapes decoded, line breaks `\n`), fills, font colours (RGB, theme+tint, indexed; malformed ones → null), bold/strike, rich-text runs (with strike), merges, notes, hidden rows/cols, hidden tabs (`SheetTab.Hidden`), conditional-format colours (Google's first-match rule). Chart sheets are skipped but keep their place: `TabNames` and `SheetTab.Index` count every `<sheet>`. Excel names "P1/P2" `P1P2` and "WAR/DRK" `WARDRK`; pass `TabNames` to restore. Throws `InvalidDataException`. |
| `SheetReaders.FromDelimited(text, '\t' or ',')` | Tab = Google clipboard rule (quoted only with a line break/tab inside); other separators = RFC 4180, except that a quote never closed is read raw (the rest of the text keeps its rows). `GuessSeparator(text)` picks one. Text only. |
| `SheetReaders.FromFixtureJson` / `FromFixtureDirectory` | `tests/fixtures/sheets` format. |

`SheetTab` gained `Index`, `Hidden`, `HiddenRows`, `HiddenCols`, `MergeAt`; `TextRun` gained `Strike`; `CellRange.Contains`.

## Layout (`LayoutDetector.cs`, `Layout.cs`)

`new LayoutDetector(fight).Detect(document)` → `DocumentLayout` (`PlanTabs`, `Assignees`, `Diagnostics`).
Per tab (`TabLayout`): `Kind` (Plan / NotAPlan), `Hidden`, `HeaderRow`, `Columns` (`ColumnRole`: Time, Mechanic,
Assignee, Notes, Phase, Ignored, Unknown, Empty), `RepeatedHeaderColumns`, `Sections` (`PhaseSection`: number, id,
name, `ZeroHint`, source), `Rows` (`RowLayout` for every row: `Kind`, `Phase`, `Label`, `Time`, `Severity` (★),
`Notes`, `Assignments`, `Texts`, `Src`), `Footnotes`, `Title`, `NamePhases`, `NameJobs`.

- Phase ids: the fight's N-th phase id when a `FightDefinition` is given, else `pN`.
- Phase sources: tab name (`P3`, `P1/P2`, `P1P2`), the header cell of the label (or time) column (`P1 `), section rows
  (`P2 — Omega-M/F  (from phase start)`, `Phase Two`), bare label rows (`P2`), a `Phase` column (`P2` or `2`), a
  phase title above the header, a marker in front of a row's label (`P2 - Solar Ray`: `Label` is `Solar Ray`), and
  labels like `P5 Delta` (or a header like `P1 Healers`) that move the phase forward (reported as `guessed-phase`).
- `RowKind.Data` rows include mechanic rows with nothing assigned (keep them for mechanic order) and assignment
  rows without a label (LPDU `General Tank Mit til P5` row 3; reported as `missing-mechanic-label`).
- `AssigneeCell.Spread`: a merged cell spanning several assignee columns applies to each of them.
- A header row repeated further down (stacked tables) with other players re-maps the player columns for the rows
  below it: their `AssigneeCell.Column` is one of `RepeatedHeaderColumns` (`header-changed`). A row whose label is
  not a header but whose cells name players ("Tether partner | OT | MT") stays a data row (`header-like-row`).
- `AssigneeHeader.Role` is `Role.None` for a ranged slot alone (`R1`, `R2`, `D3`, `D4`): physical ranged or caster.
- `PlanTabs` leaves out hidden tabs (`hidden-tab`); look tabs the user names up in `Tabs`.
- Time column cells that all read `h:mm:00` (`1:21:00`, Sheets' display of `1:21` typed into a time-formatted cell)
  are read as m:ss (`time-as-hms`); row times of an hour or more are reported (`implausible-time`).
- Diagnostic codes: `not-a-plan`, `unknown-column`, `unlabelled-column`, `ignored-column`, `duplicate-assignee`,
  `multiple-label-columns`, `multiple-time-columns`, `no-mechanic-column`, `missing-mechanic-label`,
  `unparsed-time`, `time-as-hms`, `implausible-time`, `no-phase`, `phase-not-in-tab-name`, `phase-not-found`,
  `phase-order`, `unknown-phase`, `guessed-phase`, `header-changed`, `header-like-row`, `note-in-table`,
  `hidden-content`, `hidden-tab`, `merged-rows`.

## Cells (`CellParser.cs`, `ParsedCell.cs`, `ParsedTime.cs`)

`CellParser.Default.Parse(assignment.Cell, assignment.Src)` → `ParsedCell.Items` (` / `-separated, independent) →
`ParsedItem.Steps` (arrow-separated sequence: `→`, `->`, `=>`, `—>`, `>`, `»`, `⇨`, …) → `ParsedStep.Tokens` (`+`, `,`,
line breaks, wide gaps: pressed together). Tokens with different times of their own become separate items
(`Soil (0:12)\nDeploy (0:17)`, `0:12 Soil\n0:17 Deploy`); sequences keep their steps. `ParsedCell.Diagnostics` holds
`unclosed-bracket` warnings (an unclosed `(` ends at the next separator, arrow or line break).

`ParsedToken`: `Head` (action-name guess), `Alternatives` (`Kera/Soil`), `Trailing` (`after 1st Hit`, `Co-Tank`,
`to R1/2`, `if P4 skip`), `Target`, `Hints` (parentheticals), `Time` (`ParsedTime`: seconds, range end, list,
approx flag, source text), `Optional` (grey), `WhenReady` (`⏱`, "on CD"), `Footnote` (`*`), `JobPrefix`
(`PLD Wings`), `Fg`/`Bold`/`Runs` (per-token formatting), `Text`/`Start`/`Src`. Steps and items aggregate
`Time`/`Target`; items aggregate `Optional`/`WhenReady`/`Footnote`.

## Recipe for the back half

```csharp
var layout = new LayoutDetector(options.Fight).Detect(document);
var diagnostics = new List<ImportDiagnostic>(layout.Diagnostics);
foreach (var tab in layout.PlanTabs)             // hidden tabs left out; tabs the user names: layout.Tab(name)
foreach (var row in tab.DataRows)                // row.Phase?.Id, row.Label, row.Time, row.Severity, row.Notes
foreach (var cell in row.Assignments)            // cell.Assignee: Key, Slot, Job/Jobs, Role
{
    var parsed = CellParser.Default.Parse(cell.Cell, cell.Src);
    diagnostics.AddRange(parsed.Diagnostics);    // "unclosed-bracket"
    foreach (var item in parsed.Items)
    {
        // One entry per item; item.IsSequence → seq; item.Target; item.Optional; item.WhenReady.
        // Timing (DESIGN.md §6 step 7, §5.1). An item without its own time never takes row.Time:
        //   item.Time and row.Time → offset = item.Time − (the row's matching hit, one of row.Time.All) + δ
        //   item.Time only         → offset = (phase zero + item.Time) − hit
        //   no item.Time           → leave the offset automatic (§5.1 defaults from the action kind)
        // A sequence's item.Time is its last step's; per-step times are step.Time ("Recit (2:44) → Adlo (2:50)").
        foreach (var step in item.Steps)
        foreach (var token in step.Tokens)
        foreach (var alt in token.Alternatives)  // either/or (often across jobs)
        {
            // longest alias key that is a prefix of token.WithTrailing(alt); the rest is a hint/target/count
        }
    }
}
```

Heuristic limits: the head/trailing split cuts at connector words (after, on, to, if, …) and mechanic words
(Proteans, Stack, Co-Tank, …) only, so "Invuln Diffuse Wave Cannon" stays whole — match by longest prefix.
A wide whitespace gap is a token break unless the word before expects a continuation ("after", "to", …) or the word
after is a connector/target, or a time on the same line ("Soil      0:12"). "and" is never a separator.
