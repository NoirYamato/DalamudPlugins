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
  phase title above the header, a phase title over the player columns that has a header row below it (TEA's
  `P2: Brute Justice / Cruise Chaser`) or is a merged banner that reads as a title (the fight's name for the phase, or
  at most four words without `,` `;` `.` `!` `?`; its part in parentheses too, unless it is a timing hint such as
  `(from phase start)`), a marker in front of a row's label (`P2 - Solar Ray`: `Label` is `Solar Ray`), and labels
  like `P5 Delta` (or a header like `P1 Healers`) that move the phase forward (reported as `guessed-phase`). A merged
  remark that starts with a phase (`P1: MT invulns the second Wave Cannon, OT takes boss after`, `P1 (MT invulns …)`)
  stays a note; a title over the player columns whose name, or part in parentheses other than a timing hint, is not
  the fight's is reported (`phase-title`).
- `RowKind.Data` rows include mechanic rows with nothing assigned (keep them for mechanic order) and assignment
  rows without a label (LPDU `General Tank Mit til P5` row 3; reported as `missing-mechanic-label`).
- `AssigneeCell.Spread`: a merged cell spanning several assignee columns applies to each of them.
- A header row repeated further down (stacked tables) with other players re-maps the player columns for the rows
  below it: their `AssigneeCell.Column` is one of `RepeatedHeaderColumns` (`header-changed`). A row whose label is
  not a header but whose cells name players ("Tether partner | OT | MT") stays a data row (`header-like-row`).
- `AssigneeHeader.Role` is `Role.None` for a ranged slot alone (`R1`, `R2`, `D3`, `D4`): physical ranged or caster.
  `Phys Range` (a physical word before `Range`) is a physical ranged role column like `Caster`; a whole header
  `Extras`, `Other`, `Misc` or `Utility` is `AssigneeKind.Anyone`, a column of calls for whoever can press them.
- An unlabelled column of prose (4 or more words on average) beside the label column holds the rows' `Notes`.
- `PlanTabs` leaves out hidden tabs (`hidden-tab`); look tabs the user names up in `Tabs`.
- Time column cells that all read `h:mm:00` (`1:21:00`, Sheets' display of `1:21` typed into a time-formatted cell)
  are read as m:ss (`time-as-hms`); row times of an hour or more are reported (`implausible-time`).
- Diagnostic codes: `not-a-plan`, `unknown-column`, `unlabelled-column`, `ignored-column`, `duplicate-assignee`,
  `multiple-label-columns`, `multiple-time-columns`, `no-mechanic-column`, `missing-mechanic-label`,
  `unparsed-time`, `time-as-hms`, `implausible-time`, `no-phase`, `phase-not-in-tab-name`, `phase-not-found`,
  `phase-order`, `unknown-phase`, `guessed-phase`, `phase-title`, `header-changed`, `header-like-row`, `note-in-table`,
  `hidden-content`, `hidden-tab`, `merged-rows`.

## Cells (`CellParser.cs`, `ParsedCell.cs`, `ParsedTime.cs`)

`CellParser.Default.Parse(assignment.Cell, assignment.Src)` → `ParsedCell.Items` (` / `-separated, independent) →
`ParsedItem.Steps` (arrow-separated sequence: `→`, `->`, `=>`, `—>`, `>`, `»`, `⇨`, …) → `ParsedStep.Tokens` (`+`, `,`,
line breaks, wide gaps: pressed together). Tokens with different times of their own become separate items
(`Soil (0:12)\nDeploy (0:17)`, `0:12 Soil\n0:17 Deploy`); sequences keep their steps. `ParsedCell.Diagnostics` holds
`unclosed-bracket` warnings (an unclosed `(` ends at the next separator, arrow or line break). With
`CellParserOptions.SlashJoins` a `/` between words joins tokens like `+` (not between slots, jobs, target or mechanic
words, hyphens included: `MT/Co-tank`, `Stack/Spread`; not in `and/or`, before a digit, or inside brackets);
`ParsedCell.Joins` counts the other separators between words. A cell that is one pair of brackets around words
(`(Shake)`) is its inside with every token optional (`Bracketed`); a cell that points up (`(^on LL)`) has no items and
its remark in `Above`.

`ParsedToken`: `Head` (action-name guess), `Alternatives` (`Kera/Soil`, `Seraph1/Soil`, `Kera or Soil`, `Kera and/or
Soil`; not `R1/R2`), `Trailing` (`after 1st Hit`, `Co-Tank`, `to R1/2`, `MT/Co-tank`, `if P4 skip`), `Target` (slots
and tank target words joined by `/` are one: `MT/Co-tank`), `Hints` (parentheticals), `Time` (`ParsedTime`: seconds,
range end, list, approx flag, source text), `Optional` (grey, or the cell in brackets), `WhenReady` (`⏱`, "on CD"),
`Footnote` (`*`), `JobPrefix` (`PLD Wings`), `Fg`/`Bold`/`Runs` (per-token formatting), `Text`/`Start`/`Src`.
Steps and items aggregate `Time`/`Target`; items aggregate `Optional`/`WhenReady`/`Footnote`.

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
        //   no item.Time           → leave the offset automatic (§5.1: TimingRules.ForEntry, as the scheduler times it)
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
options)` → `ImportResult`: `Plan`, `Diagnostics`, `PhaseZeros`, `PhaseSpread`, the document's own readings that the
plugin's AI prompt states for the sheet it includes (`DocumentSlashes`: "/" between one job's actions, also when
`ImportOptions.Slashes` overrides it; `SeraphNumbered`: a plain "Seraph" means "Seraph1"), and for the import window
`Rows` (each row's mechanic) and `AnyoneColumns` ("Extras"). Check any plan (imported, hand-written, LLM) with
`PlanValidator.Validate(plan, fight, actions)`.

```
DocumentLayout ─► rows ─► MechanicResolver (alias in phase; repeated labels by time or row order)
                     └─► RowTiming.Align (row times ↔ hits, + δ) ─► phase zero fit (median; preset fallback)
assignee cells ─► CellParser ─► ActionResolver (per candidate job) + HintParser ─► entries ─► press order, ids e001…
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
  (just before P3 ends)" is one entry. A target in parentheses only goes to actions used on a player; elsewhere it
  stays in the note like any other parenthetical ("Asylum (melee)", "Kera (party)": where to place it, "Earthly Star
  (melee)" in a White Mage's column, "Feint (melee)" under "Extras": the note "Feint" with "melee"). Without a written
  target, actions used on a player take one from the row's notes, else "tank" on a tankbuster; the item's other actions
  then get an entry of their own ("EukDiag/Kerachole" on Double Rocket Punch). A condition ("if P4 skip", "no skip",
  "(if needed)") makes the entry optional. Part words after an action ("Succor Proteans + Soil +
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
  `untimed` warning. Automatic times are the scheduler's (`TimingRules.ForEntry`: the actions' defaults, a cell's several
  actions and their own leads, a mechanic's hits and the fight file's `press`, `heal_lead` and `series`); the importer
  writes no offset for them, unless a step after the cells presses one later (`still-active`, `recast-fit`). The plan
  lists its entries in that press order (ties in sheet order), also when `still-active` changed an entry's action or a
  press moved.
- **Slashes.** "X/Y" between two actions of one job is either one (`TBN/Oblation`), unless the document never joins
  actions another way (`+`, `,`, `&`, `;`, `|`, arrows, ` / `, line breaks, gaps of three or more spaces) and lists
  several actions of one job with `/` in two or more cells, three or more in one (`Soil/Reci/Deploy/Whisper`): then all
  of them, lexed like `+`. Every plan tab counts, whatever is imported; `ImportOptions.Slashes` overrides it, and the
  `slash-reading` info says which and why (`ImportResult.DocumentSlashes` is the document's own reading). "All of
  them" never joins at a `/` between slots, jobs, target or mechanic words (`MT/Co-tank`, `Kera after Stack/Spread`) or in
  `and/or`. In both readings, a `/` between two single letters is one word, spaced or not (`N/A`, and `N / A`, which
  joins nothing), slots and tank target words joined by `/` after an action are one target, either of them (`Benison
  MT/Co-tank`), other words that name no action joined by `/` after an action are its remark (`Kera Stack/Spread`:
  Kerachole, noted "Stack/Spread"; `Rampart MT/TB`), and `and/or` is either one (`Soil and/or Expedient`). An
  alternative that means nothing is an `unresolved-token` warning; an either/or with a choice of several actions
  (`Spread-Lo/Soil`) gives one optional entry per choice (`either-or-split`).
- **Seraph.** A sheet that numbers its Seraph calls (`Seraph1`: Summon Seraph → Consolation, `Seraph2`: the second
  Consolation) means the same by a plain `Seraph` (LPDU's TEA: `Succor/Soil/Seraph` at J Wave 5, `Seraph/Fey Illum` at
  J Wave 7): it is read like `Seraph1`, and a second one while Seraph is out is the other Consolation (`still-active`).
  Otherwise a plain `Seraph` is Summon Seraph alone (Carry the Clear writes its Consolations apart). Every plan tab
  counts, whatever is imported; the `seraph-reading` info names a numbered call and an imported plain one, and
  `ImportResult.SeraphNumbered` says that the document numbers its Seraph calls.
- **Columns for anyone** ("Extras"): a call is an entry for the jobs whose own action it names (`PLD Wings`,
  `Dismantle`, `(Minne/Mantra)`), with no assignee; a note-only call is one entry for everyone; an action several jobs
  share (`Feint`, `Party Mit`) is a note for everyone (`anyone-role-action`). A cell is read call by call: the calls that
  name a job's own action keep their jobs' entries, the others are one entry for everyone (`Dismantle + Feint`: a
  Machinist's Dismantle and the note "Feint"; `PLD Wings + Use Personals`: a Paladin's Passage of Arms and the note
  "Use Personals"). The cell's remark in parentheses or below a caret stays on every entry it splits into (`Dismantle +
  Feint (on BJ)`: the Dismantle is on BJ too). The column is imported whatever columns are chosen, is not in
  `plan.assignees`, and is listed in `ImportResult.AnyoneColumns`.
- **Cells that point up** (`(^on LL)`, `(^ if MCH)`) are read as a parenthetical of the nearest cell above in the same
  column and section, after each of its items, so that it speaks for all of the cell (`Soil / Expedient` with `^ late`
  below it: both late; `(Party Mit)` with `(^ if MCH)`: `(Party Mit (if MCH))`) (`caret-note` info, a warning when
  nothing is above); a parenthetical that names the token's own per-job actions limits the jobs (`Party Mit (DM/HoL)`:
  DRK and GNB, `named-actions`); one or two target words in brackets (`(Range)`) are a target; `(Late)` on an entry
  pressed before its hit presses it just before it, so that its longest cast, or the GCD a follow-up needs first, ends
  1 s before the hit (`Reprisal (Late)` −1 s, `Succor (Late)` −3 s, `Eukrasian Prognosis (Late)` −3.5 s after
  Eukrasia's 2.5 s), but never an item earlier than without it (`Reprisal (Late)` on TOP's Wave Cannon stays 0.5 s
  before the first protean, so that it lasts through the Wild Charge, and so does `Rampart + Reprisal (Late)`, whose
  Rampart keeps its own −6 s).
- **Notes over several columns** that name actions and speak to no other player ("A tip for Soil/Kera timing …") are
  about using those actions: the detail of the entries in their columns and section that press one of them (all their
  columns' entries when none does). A note that speaks to players by role, slot or job ("Tanks: Rampart the first tower
  set, then OT provokes …", "Healers: …, WHM covers the rest") is for its columns' players: the detail of all their
  entries, as a note in each column. A role or slot word that says where or for whom to press speaks to no one ("on the
  melee", "under the tanks", "on melee and ranged", "on the co-tank", "the tank buster", "the OT's buster"), nor does
  the job whose own action the note names ("SCH: … press Soil …"). A word that names actions of several jobs does not
  say whose it is: "WAR Rampart the first tower set, then PLD provokes …" speaks to the PLD (Rampart is every tank's, as
  "Invuln" names one action of each). A merged note reads the same whichever of its columns are imported: imported
  alone, the Scholar column gets the Soil/Kera tip on its Soil entries, the Tank 1 column none (the columns not imported
  name Soil and Kera; a `note-row` info says so).
- **After the cells.** A party buff planned again by the same player while it still lasts is its follow-up (Summon
  Seraph → Consolation) or a note "Temperance (still active)" (`still-active`), when the earlier press still serves the
  later call as the reminders time both (`TimingRules.ForEntry`), used on its NOW cue (the default due lead). A note says
  that the earlier effect covers the later hit: it outlasts that hit by 1 s (`EarliestCoveringUse`), lasting as long as
  the reminders count it covering one (`CoverageDuration`: a `when: before` action's longest effect, Neutral Sect's 30 s
  barrier). A follow-up needs the action only when it is used, its own effect covering the hit: it is pressed within
  `check`'s follow-up window, the action's longest effect (Seraph's 22 s for a Consolation at Whirlwind 2, 20.9 s after
  Water 2). An earlier press without a time in the sheet that falls short is pressed later so that it serves both
  (TEA's Hidden Mines Temperance −3.5 s, through Water 2), no later than `(Late)` would press it, never after the lead
  of a `when: before` action that is no heal (Neutral Sect −4.5 s, Passage of Arms −2 s), and never so that another of
  its items is pressed earlier than without it (the offset is every item's press: a Reprisal that waits for a later
  hit keeps its press); otherwise both stay presses (a `still-active` info), and `check` reports the recast. An
  optional earlier press (in brackets, with a condition, one choice of an either/or) may not happen, so it keeps no buff
  up. A limit break written for one role in columns of different slots at one mechanic is optional, noted "or Tank 2"
  (`shared-limit-break`), whichever of those columns are imported: a tank who imports only their own column gets it
  optional too.
- **Recast fit**, last (`recast-fit` info). An entry without a time in the sheet that is pressed before its hit while
  one of its actions has no charge yet, as `check` replays the plan (`RecastReplay`: recasts and charges at the sync
  level, one charge recharging at a time, recast groups, recasts under 3 s left out; each item at its own press time,
  later than its entry's for an item that waits for a series floor or a later hit of its own, and the info gives the gap
  between the two items' presses) for any seat (every job, in each
  slot of its role and in none, with the column the plugin chooses without a party and with any party that changes it:
  the jobs of a team-composition column, `WAR/PLD WAR` next to a Paladin, and every two-job party), is pressed when
  that action is ready again plus the default due lead (1.5 s), so that its NOW cue comes then, rounded up to 0.1 s:
  TEA's Gavel Asylum, 85.8 s after the Asylum pressed after Photon (recast 90 s), 1.5 s before its hit instead of
  7.2 s. Never later than `(Late)` would press it (1 s before the hit, minus the entry's longest cast or the GCD a
  follow-up needs first: a Sage's Eukrasian Prognosis stays 3.5 s before the hit at the latest), nor than an action's
  own lead before the hit allows (Neutral Sect 4.5 s, Earthly Star 10 s, Passage of Arms 2 s), so that it still does
  its job: then just after the action is ready (the J Wave 7 Plenary, 1 s before its wave, which still turns due only
  after the wave before it); when the action is ready only after that, the press stays and `check` reports the recast.
  It stays too when the move would press another item of the entry earlier than without it, since the offset is every
  item's press: an item that waits for a later hit or a series floor of its own, or Passage of Arms after the others
  (`Rampart + Reprisal` on TOP's Wave Cannon 2 keeps the Reprisal 0.5 s before the first protean, for the Wild Charge).
  Only firm presses keep an action on recast: not an optional entry (in brackets, with a condition, one choice of an
  either/or), an either/or item or a note ("Temperance (still active)"); an optional call itself still waits
  ("CU (if up)"). A press with a time in the sheet counts but never moves; an offset the importer wrote
  (`still-active`'s) may move. A when-ready call ("on CD") is left to the runtime: it keeps its press and counts,
  since its reminder already waits for the action in game (LPDU TOP's P5 party mitigation on cooldown, which a move
  would only push into the Blind Faith calls). A sequence moves for its first step only (`check` times every step at
  its press; in game the later steps follow), and only when its later steps are instant oGCDs woven in right after it
  (`Kerachole → Holos`): a later GCD or cast (`Zoe → Eukrasian Prognosis`, `Recitation → Succor`) would land after the
  hit, so such a sequence keeps its press. An entry several seats see takes the latest press any of them needs; the
  earliest press without a charge moves first, then the plan is replayed again, until nothing moves. A press that
  moved is replayed at its press time, as `check` replays every press: in a chain of moves each NOW cue still comes
  when the action is ready for a player who presses at the press times (each link keeps its due lead).
- **Presets.** `ImportOptions.CalibrationKey`, else a preset whose words all occur in the document title.
- **Tank-pair tabs** ("WAR/DRK" with columns WAR and DRK) are variants: when their column names collide with other tabs',
  they are keyed "&lt;tab&gt; &lt;column&gt;" ("WAR/DRK WAR").
- **Plans in `plans/`** are the importer's output for the fixtures (`tests/…/Import/PlanFilesTests.cs`); regenerate with
  `MITCUE_UPDATE_PLANS=1 dotnet test --filter FullyQualifiedName~PlanFilesTests`.
