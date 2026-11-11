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
| `GoogleHtmlView.TabNames(html)` | The tab names a spreadsheet's `htmlview` page lists, as Google shows them ("P1/P2"). The CLI passes them as `XlsxReadOptions.TabNames`; the plugin's link import renames the tabs the export changed. |

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

# Import — back half

Steps 4–7 (`docs/DESIGN.md` §6; step 8, the review, is the plugin's import window): `new PlanImporter().Import(document,
options)` → `ImportResult` (`Plan`, `Diagnostics`, `PhaseZeros`, `PhaseSpread`). Check any plan (imported, hand-written,
LLM) with `PlanValidator.Validate(plan, fight, actions)`.

```
DocumentLayout ─► rows ─► MechanicResolver (alias in phase; repeated labels by time or row order)
                     └─► RowTiming.Align (row times ↔ hits, + δ) ─► phase zero fit (median; preset fallback)
assignee cells ─► CellParser ─► ActionResolver (per candidate job) + HintParser ─► entries ─► time order, ids e001…
```

| File | Role |
|---|---|
| `PlanImporter.cs` | Orchestration: tab selection and assignee keys, presets, mechanics, calibration, entries, plan. |
| `ActionResolver.cs` | Token → actions for one job at the sync level: alias (longest key) → leading job ("WHM Bell") → exact name → close spelling; upgrades applied; targeted forms ("Short CD to R1"). |
| `MechanicResolver.cs` | Row label → mechanic candidates (mechanics.md §2 normalization; simplified, prefix and fuzzy fallbacks); phase ends. |
| `HintParser.cs` | "after 1st Hit", "after &lt;mechanic&gt;", "just before P3 ends" / "as P1 dies", "if MCH", a bare mechanic name. |
| `RowTiming.cs` | Row times matched to hits (lists, ranges, times listed in a range row's label), sheet time → timeline, hit of a press. |
| `PlanValidator.cs` | Mechanics exist, hit indexes valid, actions exist and are usable by the entry's job(s) at the sync level, ids unique. The tests run it on the bundled plans; the plugin checks share codes and plan files with its import review's rules instead (`PlanChecks`). |

Rules worth knowing:

- **Jobs.** A column with a job resolves for it; a slot column ("H2") or a role column ("CM LB Tank") resolves for
  every job of its role. When the jobs' actions differ, each job gets its own entry (`who: {slot: H2, job: SGE}`);
  the same role action for *every* job of the slot stays one slot entry (`who: {slot: MT}`), while an action only some
  of them have gets per-job entries ("Addle" in R2: the casters, not a Bard). A role column without a slot always gets
  per-job entries, so no other player picks them up. "if MCH" restricts an item to that job. A column whose action words
  are mostly another job's (a header naming the wrong job) gets one `column-job` warning.
- **Actions.** Aliases and exact names first; close spellings only for words and keys of 5+ letters (so "Hold", "Zone",
  "Well" never become Holmgang, Zoe or Bell), reported as `fuzzy-match` warnings. Text after an action is checked: another
  action of the job written without a '+' joins the item ("Temp Bell", `joined-actions` info); hint words, targets,
  times, mechanic names and note-only phrases stay hint text; anything else is an `unresolved-token` warning and stays in
  the note.
- **Items.** Inline text speaks for its token, parentheses after an item's last token for the whole item: "Kera after
  1st Hit", "Short CD to R1", "Neutralsect if P4 skip" make their token its own entry (same anchor), while "Recit + Adlo
  (just before P3 ends)" is one entry. A target in parentheses only goes to actions used on a player. A condition ("if P4
  skip", "no skip", "(if needed)") makes the entry optional. Part words after an action ("Succor Proteans + Soil +
  Spreadlo Stack") name groups of the mechanic's hits when the item names as many parts as the hits form groups (gaps at
  least twice the smallest): Spreadlo is timed for the Wild Charge. A target naming players by order on a multi-hit row
  ("your 3rd/4th-in-line player" on Program Loop) anchors to the last such hit, pressed before it. Later step times of a
  sequence are kept in the note ("Adlo 2:50"). A '*' pairs with the footnote whose '*' has its colour (else the one in its
  column, else the only one); a footnote's lower-case continuation lines are part of it.
- **Notes.** Note-only phrases and unresolved text become note items with `unresolved-token` (info for note-only phrases
  and free text such as "Debuff at ~3 secs", warning for action-like words). Another job's action ("PLD Wings" for a
  Warrior) is `other-job-action`; an action above the sync level is `not-available-at-level`. Note rows in a player's
  column: one that names an action and a timing hint ("WAR can use Shake as P3 dies …") is imported as an entry (for the
  job it names, optional for "can"/"may"); any other becomes the detail of that column's entries in its section ("Shields
  bevor the boss is targetable" under H2 SCH in P4), or a note entry of its own when the column has no entry there
  (anchored to the mechanic it names, else the row above). Footnotes go to the marked items.
- **Rows.** Untimed rows follow the order rule (mechanics.md §4.5); a row earlier in the fight than the row above
  (`row-order`) or a repeated label with no unused candidate after it (`ambiguous-mechanic`) is a warning. An unlabelled
  first row of a phase takes the next row's mechanic. An exact label of another phase beats a near miss in the row's
  phase, and labels that differ in a number are never typos of each other. Timed rows are checked against the fitted
  phase clock (zero = median of the largest group of rows that agree within 5 s): a row more than 5 s off its mechanic is
  a `row-time` warning; it is re-anchored to the hit its time matches among the mechanics its label can mean under any
  match tier ("Solar Ray ×2" at the first Solar Ray, cactbot's per-hit "Revolting Ruin III 2"), else its items are timed
  from the phase zero point instead of its time, and it is left out of the fit.
- **Timing.** Timed items: `offset = press − hit(k)`, where the press is the item time through the row's nearest matched
  time plus the sheet's δ (fight file `row_offsets`, e.g. Carry the Clear's Blind Faith −1.2 s), else through the phase
  zero point; `k` is the first hit at or after the press (heals: the last at or before it). Untimed items are automatic
  unless a hint applies (`after 1st Hit` → that hit +0.5 s; `after <mechanic>` → its last hit +0.5 s; phase-end phrases →
  that phase's enrage/end mechanic with `at_phase_end`). Hint text stays in `note`. Presses after a phase's last hit get
  `after-phase-end` for review. Times that cannot be placed (no row time, no phase zero) give one automatic entry and one
  `untimed` warning.
- **Presets.** `ImportOptions.CalibrationKey`, else a preset whose words all occur in the document title.
- **Tank-pair tabs** ("WAR/DRK" with columns WAR and DRK) are variants: when their column names collide with other tabs',
  they are keyed "&lt;tab&gt; &lt;column&gt;" ("WAR/DRK WAR").
- **Plans in `plans/`** are the importer's output for the fixtures (`tests/…/Import/PlanFilesTests.cs`); regenerate with
  `MITCUE_UPDATE_PLANS=1 dotnet test --filter FullyQualifiedName~PlanFilesTests`.
