# mitcue (developer CLI)

Command-line access to MitCue.Core: fights and timelines, plan import, a player's checklist, simulated pulls, plan
checks and data validation. It never talks to the game.

```sh
dotnet run --project tools/MitCue.Cli -- <command> [arguments] [options]
dotnet run --project tools/MitCue.Cli -- <command> --help
```

Data comes from the repository's `data/` (found above the current or program directory), or from `--data <dir>`;
without either, the copy built into MitCue.Core is used. Plans can be YAML, JSON or share codes (`-` reads stdin).

| Command | What it does |
|---|---|
| `fights` | The fights: key, name, territory, sync level, phases, mechanics, timeline file. |
| `mechanics <fight> [--phase p2] [--kind raidwide] [--find text]` | Phases, calibration presets and mechanics (hit as phase m:ss and timeline seconds, kind, cactbot entry, hits, aliases). |
| `timeline <fight\|file.txt> [--syncs]` | The parsed cactbot timeline, with a fight's `timeline_additions` appended (TOP: 283 entries, 208 with a sync): entries with syncs, non-default windows, jumps; labels; parser warnings. |
| `import <source> --fight <key> [...]` | A sheet (.xlsx, fixture folder, .csv/.tsv/.json, or a public Google Sheets link) to plan YAML, with the importer's diagnostics by severity and each phase's fitted zero point and spread. |
| `plan <plan> --job <job> [--slot] [--assignee]` | The player's checklist in press-time order: phase time, offset from the hit (`*` = no time in the plan: the automatic press time, `docs/DESIGN.md` §5.1; an offset the importer wrote, such as its recast fit's, is a time in the plan), action, mechanic, target and notes, flags. |
| `simulate <plan> --job <job> [--push p2=110,...] [--seed n] [--miss a,b] [--from p3] [--wipe t]` | A generated pull through the timeline player and the reminder scheduler, set up like the plugin's encounter session; logs every reminder state change and prints the pull summary. |
| `check <plan> --job <job> [--slot] [--assignee]` | Plan checks (`PlanChecker`): unknown mechanics/actions, actions the job cannot use at the sync level, recast conflicts and tight margins, gauges and follow-ups (heuristic). |
| `validate` | Every data file through the real loaders, with the consistency checks of the data tests; plans in `plans/` too. |
| `share <plan>` / `unshare <code> [-o file]` | Share codes (`MITCUE1:...`), which players paste into `/mitcue` > Plans > Share code. |

Exit codes: 0 ok; 1 problems found (`check` errors, `validate` problems, `import` error diagnostics); 2 wrong command
line; 3 failure (missing or unreadable input, download error, a core part not implemented yet).

Examples:

```sh
mitcue import tests/fixtures/sheets/carry-the-clear --fight top --tabs "TOP – White Mage" \
       --calibration carry_the_clear -o ctc-whm.yaml
mitcue check ctc-whm.yaml --job WHM --slot H1
mitcue simulate ctc-whm.yaml --job WHM --push p2=2:10,p3=5:20 --miss Temperance
mitcue plan plans/top-lpdu.yaml --job SCH --slot H2      # a bundled plan, as the plugin's H2 Scholar gets it
mitcue import tests/fixtures/sheets/lpdu-tea --fight tea -o tea.yaml   # LPDU's TEA sheet: "/" read as "and"
mitcue plan plans/tea-lpdu.yaml --job BRD                 # the "Phys Range" calls for BRD and its "Extras"
```

`plans/` holds the plans bundled with the plugin (every `.yaml` there is built into MitCue.Core), so write your own
imports elsewhere.

Notes:

- `plan`, `simulate` and `check` take the player's entries the way the plugin does without a saved column choice
  (the core's `ColumnSuggestion`): the job's column, or, when the job's calls are in several columns (one per phase
  or tab), the narrowest choice that has them all (any column for the job in the slot, or one column). A `--slot`
  that is not a slot of the job's role is not used, as the plugin reads "My usual party slot"; a note says so
  (`--job WAR --slot M1` reads like `--job WAR`). The slot is the job's column's slot when it has one, even when
  `--slot` names another (`--job SCH --slot H1` on `plans/top-lpdu.yaml` reads "H2 SCH" in slot H2), else
  `--slot`, else the slot of another column of the job, else, only when the plan has no column for the job, the
  first slot of the job's role that the job's calls name. LPDU has no column for its ranged jobs, so `--job RDM`
  reads slot R1, 9 entries (R2 with `--slot R2`, 13); its tanks have columns without a slot, so `--job WAR`
  reads none (9 entries, below). Calls that name no job give no slot: `--job MNK` gets LPDU's melee calls (the
  job-less "M1" and "M2" columns) only with `--slot M1` or `M2` (or `--assignee "M1"` or `"M2"`). A column also
  gets other columns' calls for the slot that are for the job, or for no job when the slot is one of the job's
  role, and the job's calls in the same team composition's other tabs ("WHM/SCH P3 WHM" with "WHM/SCH P1 WHM":
  one composition split over per-phase tabs is one seat). Of a job's team-composition columns (LPDU's tank pairs)
  the CLI takes the first, and of a ranged job's seats the first: it knows no party. On `plans/top-lpdu.yaml`,
  `--job SCH` gets the column "H2 SCH" (31 entries, with the job-less "H2" calls of P1/P2) and `--job WAR` the
  column "WAR/DRK WAR" (35 entries with `--slot MT`, 28 with `OT`, 9 without). The header names the slot and
  column used, and a note on stderr names the choice when it differs from every column for the job and slot
  (`--job RDM`: "any column for RDM in slot R1 (the slot the plan gives RDM; --slot sets yours)").
  `--assignee <key>` picks a column with its slot, as /mitcue > Plans saves a picked column
  (`--job SCH --assignee "H2 SCH"`: slot H2, 31 entries; a `--slot` naming another slot is not used, with a
  note), and other columns' calls apply as above (for that slot, or for the job in the column's composition's
  other tabs); `--assignee "*"` takes every column for the job and slot (the note then lists the columns whose
  entries apply). A column for a role holds calls per job of the role, and calls in no column are for the jobs
  they name, whatever the column: on `plans/tea-lpdu.yaml`, `--job BRD` (no `--slot` needed) gets the Bard's
  calls in "Phys Range" ("Caster" for the casters) and in "Extras" (Nature's Minne), and `--job PLD` gets the
  "Extras" calls "PLD Wings" in either seat. That plan's tank calls are per job in the seats "Tank 1" (MT) and
  "Tank 2" (OT), so `--job PLD` without `--slot` reads slot MT.
- `import --slashes auto|or|and` sets what "X/Y" between two actions of one job means: `or` either one (LPDU
  TOP's "TBN/Oblation"), `and` all of them, like "+" (LPDU TEA's "Soil/Reci/Deploy/Whisper"). `auto`, the
  default, reads "and" when no cell of the sheet (every tab counts, whichever `--tabs` are imported) joins actions
  another way ("+", ",", "&", ";", "|", an arrow, a spaced " / ", a line break or a gap of three or more spaces) and
  two or more cells list several actions of one job with "/", three or more in one; the `slash-reading` diagnostic
  says which reading was used and why. Under "and", a "/" between slots, jobs, target or mechanic words
  ("MT/Co-tank") or in "and/or" never joins. In both readings, slots, jobs, target or mechanic words joined by "/"
  after an action are its note ("Kera Stack/Spread", "Rampart MT/TB"), or its target when they are all slots or
  tank words ("Benison MT/Co-tank"); other words are not ("Kera Left/Right": a note item "Right" under `and`,
  "Right" left out of the either/or under `or`, with a warning). "N/A" (also "N / A") is one word that joins
  nothing.
- Plans are read as the plugin reads them: slot synonyms (`T1`/`T2`, `ST`, `D1`-`D4`) become the standard slots,
  and an entry with no job in a column with a job gets that job (a job-less call in "H1 AST" is the Astrologian's).
- `simulate` plays only the event types the plugin sees in game (combat start/end, cast starts, resolved boss
  abilities, wipes) unless `--all-events` is given; TOP's P2, for example, is then entered by the 7B40 cast at 203.4
  rather than the MapEffect at 200.0. `--push p2=110` starts P2 110 s (real time) after the pull and ends P1's events
  3 s before; phase times are those of the pull as it plays, so after DMU's P2 success jump P3 and every later phase
  come 113.8 s before the timeline's spacing puts them. A phase entered through a jump (DMU's P3 and P5) is pushed by
  moving the jump, only within its sync window: a push the simulation cannot apply is an error that gives the window.
  `--from p3` starts at P3 like `/mitcue preview top p3`. The player presses every action at its press time except the
  `--miss` ones, with the button the job has at the sync level, and the session gets the action the game reports: a
  plan naming Raw Intuition at level 90 is reported as Bloodwhetting, so that reminder ends Missed, as in game (`check`
  warns about it). The `Sync:` line says how many boss events the timeline was in sync with, and where it was not.
  cactbot syncs DMU's P2 and P4 starts only on ActorControl lines, which the plugin does not see; `dmu.yaml`'s
  `timeline_additions` sync them on cast starts instead, so pushes there are followed without `--all-events` too.
- `validate` compares each file of the data copy built into MitCue.Core with the folder's; a difference (a stale
  build) is a warning to rebuild. A fight file's timing field (`press`, `heal_lead`, `series`) on a marker is a
  problem: a marker has no damage to time the calls against. So is an `actions.yaml` `lead` or `barrier_cast` that
  is not a positive number of seconds: the action catalog does not load.
- `import` of a Google Sheets link downloads the whole workbook (`export?format=xlsx`; pick tabs with `--tabs`) and
  restores the tab names the xlsx export changes ("P1/P2" becomes "P1P2") from the sheet's htmlview page, so plan
  provenance and the importer's column names (LPDU's "WAR/DRK" pairs) match the sheet. `--tabs` matches names ignoring
  case and punctuation either way.
- `check` compares planned uses on the timeline's nominal times, each item at its own press time (`docs/DESIGN.md`
  §5.1). Within a phase these are exact, so a conflict is an error; across a phase transition the real gap depends on
  when the earlier phase ends, so it is a warning. Gauge and follow-up findings are marked `(h)`: they rest on stated
  assumptions (e.g. Lilies full at the pull, +1 every 20 s). `import` replays the plan with the same recast model and
  presses an automatic call that would find its action not ready yet when the action is ready plus 1.5 s (the recast
  fit, `docs/DESIGN.md` §6 step 7, a `recast-fit` diagnostic); `check` then reports it as tight, and still reports
  a recast the fit leaves: a time in the sheet, an "on CD" call, a later step of a sequence or one whose later steps
  are GCDs or casts, an action ready only after "(Late)" or the lead of an action of the call (Neutral Sect 4.5 s,
  Earthly Star 10 s, Passage of Arms 2 s) would press it, a wait that would press another action of the call
  earlier or a follow-up out of its trigger's window, and a conflict with an earlier optional call, which the fit
  does not count (`check` warns that "an optional use is involved").
