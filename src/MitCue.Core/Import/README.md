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
| `SheetReaders.FromXlsx(stream, title[, XlsxReadOptions])` | Google `export?format=xlsx`. Text as displayed, fills, font colours (RGB, theme+tint, indexed), bold/strike, rich-text runs (with strike), merges, notes, hidden rows/cols, conditional-format colours (Google's first-match rule). Excel names "P1/P2" `P1P2`; pass `TabNames` to restore. Throws `InvalidDataException`. |
| `SheetReaders.FromDelimited(text, '\t' or ',')` | Tab = Google clipboard rule (quoted only with a line break/tab inside); other separators = RFC 4180. `GuessSeparator(text)` picks one. Text only. |
| `SheetReaders.FromFixtureJson` / `FromFixtureDirectory` | `tests/fixtures/sheets` format. |

`SheetTab` gained `Index`, `HiddenRows`, `HiddenCols`, `MergeAt`; `TextRun` gained `Strike`; `CellRange.Contains`.

## Layout (`LayoutDetector.cs`, `Layout.cs`)

`new LayoutDetector(fight).Detect(document)` → `DocumentLayout` (`PlanTabs`, `Assignees`, `Diagnostics`).
Per tab (`TabLayout`): `Kind` (Plan / NotAPlan), `HeaderRow`, `Columns` (`ColumnRole`: Time, Mechanic,
Assignee, Notes, Phase, Ignored, Unknown, Empty), `Sections` (`PhaseSection`: number, id, name, `ZeroHint`,
source), `Rows` (`RowLayout` for every row: `Kind`, `Phase`, `Label`, `Time`, `Severity` (★), `Notes`,
`Assignments`, `Texts`, `Src`), `Footnotes`, `Title`, `NamePhases`, `NameJobs`.

- Phase ids: the fight's N-th phase id when a `FightDefinition` is given, else `pN`.
- Phase sources: tab name (`P3`, `P1/P2`, `P1P2`), the label column's header (`P1 `), section rows
  (`P2 — Omega-M/F  (from phase start)`), bare label rows (`P2`), a `Phase` column, or a phase title above the header.
- `RowKind.Data` rows include mechanic rows with nothing assigned (keep them for mechanic order) and assignment
  rows without a label (LPDU `General Tank Mit til P5` row 3; reported as `missing-mechanic-label`).
- `AssigneeCell.Spread`: a merged cell spanning several assignee columns applies to each of them.
- Diagnostic codes: `not-a-plan`, `unknown-column`, `unlabelled-column`, `duplicate-assignee`,
  `no-mechanic-column`, `missing-mechanic-label`, `unparsed-time`, `no-phase`, `phase-not-in-tab-name`,
  `phase-order`, `unknown-phase`, `hidden-content`, `merged-rows`.

## Cells (`CellParser.cs`, `ParsedCell.cs`, `ParsedTime.cs`)

`CellParser.Default.Parse(assignment.Cell, assignment.Src)` → `ParsedCell.Items` (` / `-separated, independent) →
`ParsedItem.Steps` (`→`/`->` sequence) → `ParsedStep.Tokens` (`+`, `,`, wide gaps: pressed together).

`ParsedToken`: `Head` (action-name guess), `Alternatives` (`Kera/Soil`), `Trailing` (`after 1st Hit`, `Co-Tank`,
`to R1/2`, `if P4 skip`), `Target`, `Hints` (parentheticals), `Time` (`ParsedTime`: seconds, range end, list,
approx flag, source text), `Optional` (grey), `WhenReady` (`⏱`, "on CD"), `Footnote` (`*`), `JobPrefix`
(`PLD Wings`), `Fg`/`Bold`/`Runs` (per-token formatting), `Text`/`Start`/`Src`. Steps and items aggregate
`Time`/`Target`; items aggregate `Optional`/`WhenReady`/`Footnote`.

## Recipe for the back half

```csharp
var layout = new LayoutDetector(options.Fight).Detect(document);
foreach (var tab in layout.PlanTabs)
foreach (var row in tab.DataRows)            // row.Phase?.Id, row.Label, row.Time, row.Severity, row.Notes
foreach (var cell in row.Assignments)        // cell.Assignee: Key, Slot, Job/Jobs, Role
foreach (var item in CellParser.Default.Parse(cell.Cell, cell.Src).Items)
{
    // one entry per item; item.IsSequence → seq; item.Time ?? row.Time; item.Target; item.Optional; item.WhenReady
    foreach (var step in item.Steps)
    foreach (var token in step.Tokens)
    foreach (var alt in token.Alternatives)  // either/or (often across jobs)
    {
        // longest alias key that is a prefix of token.WithTrailing(alt); the rest is a hint/target/count
    }
}
```

Heuristic limits: the head/trailing split cuts at connector words (after, on, to, if, …) and mechanic words
(Proteans, Stack, Co-Tank, …) only, so "Invuln Diffuse Wave Cannon" stays whole — match by longest prefix.
A wide whitespace gap is a token break unless the word before expects a continuation ("after", "to", …) or the word
after is a connector/target/time. "and" is never a separator.
