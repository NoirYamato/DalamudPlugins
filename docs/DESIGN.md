# MitCue — design

MitCue is a Dalamud plugin for Final Fantasy XIV. It reads a raid mitigation plan
(written in Google Sheets or Docs by the community, in whatever layout the author
liked), converts it into one standard format, and during the fight shows a small
window telling *you* which of *your* actions to press next, synced to the boss.

It is **reminder-only**: it never presses, queues or targets anything.

Status: requirements v0.2 (agreed 2026-11-11), implemented as MitCue 0.1. This page
describes the design as built. Where the build differs from a decision or requirement,
the original text stays and the difference is marked **As built** or **Changed**, with
its date. How the plugin works in detail: [PLUGIN.md](PLUGIN.md). Research notes:
`docs/research/*`.

---

## 1. Requirements

### 1.1 Decisions from the requirements discussion

| # | Topic | Decision |
|---|-------|----------|
| D1 | Audience | Private: the author and their static. Distributed through a **custom plugin repository** (not the official Dalamud repo), so the official repo's combat-plugin pre-approval and AI-usage policy do not apply. We still follow the official rules' spirit: informational only, no automation, no FFLogs integration inside the plugin. **Changed (2026-11-11):** the custom repository is the one of the author's other plugin, NoirMech: `https://raw.githubusercontent.com/NoirYamato/DalamudPlugins/main/repo.json` serves both. MitCue's source is the `mitcue` branch of NoirYamato/DalamudPlugins, and `publish-mitcue.ps1` on its `main` branch publishes builds ([PUBLISHING.md](PUBLISHING.md)). The earlier plan (tag releases, a `repo` branch in MitCue's own repository) is retired. |
| D2 | First users | **WHM (H1)** following the *Carry the Clear* TOP sheet; **SCH (H2)** following the *LPDU TOP Mitigation Sheet*. Different players in one party may load different plans. **As built:** both plans ship with the plugin and are the automatic choice for those jobs (§7). **Changed (2026-11-11, MitCue 0.1.0.2 and 0.1.0.3):** the White Mage's plan, still made from Carry the Clear's White Mage tab, is named *NoirMit - WHM* (*Carry the Clear – WHM* in 0.1.0.1; same id). Carry the Clear's combined tab (*Carry the Clear – WHM + SCH*) no longer ships. Its Scholar tab ships as *Carry the Clear - SCH*, used only when a Scholar chooses it: the Scholar's automatic plan stays LPDU. |
| D3 | Build order | Our choice: runtime first (sync + overlay) with the two plans converted by our own importer, then the in-game importer UI. Plugin dependencies are allowed if useful. **As built:** runtime first, then the importer front half and the in-game import window, then the CLI, the importer back half and the converted plans; no plugin dependencies. |
| D4 | Import | Public Google Sheets/Docs **links** (the plugin downloads them) **and paste**. A "copy conversion prompt" for any LLM is the fallback for layouts the importer cannot read. **As built:** all three routes are in the in-game import window (§6). |
| D5 | Reminder behaviour | Defaults below, all changeable in a config window: show reminders 15 s ahead, sound when an action becomes due, track done/missed, own calls only (co-healer's calls optional), post-pull summary optional, no hotbar glow in v1. **As built:** each of these is a setting (§7); see F6 for the timings that are not. |
| D6 | Plans without times | Timing is derived from each action's effect duration and kind; the user can adjust any entry during review. |
| D7 | Fights | TOP first (level 90). Next: **TEA** (The Epic of Alexander (Ultimate), level 80) and **DMU** (Dancing Mad (Ultimate), level 100). **As built (2026-11-11):** the fight data of all three ships. TEA and DMU are first drafts: no mitigation sheet has been mapped onto them, and no plans for them ship. TEA follows phase pushes; DMU's P2 and P4 starts use fallback syncs that are not yet checked against logs (§4). |
| D8 | BossMod / cactbot as dependencies | **No runtime dependency.** cactbot is a browser overlay inside ACT/IINACT, so a Dalamud plugin cannot read its state; BossMod's IPC only exposes autorotation/AI/config controls, not fight timelines. Instead MitCue **bundles cactbot's timeline files** (Apache-2.0, attributed) and parses them itself. |

### 1.2 Functional requirements

- **F1 Plans.** Import, store, select and share mitigation plans. One active plan per
  (fight, job). A plan can contain every player's assignments; each player picks which
  column is theirs, auto-suggested from slot + job.
- **F2 Sync.** Follow the fight in real time using the fight's timeline and observed
  game events (combat start, boss cast starts, resolved boss abilities, wipes). Tolerate
  variable phase pushes; reset on wipe and zone change.
- **F3 Reminders.** Compact overlay listing your next actions, each with icon, name,
  countdown, the mechanic it is for, optional target/note; states *upcoming → due (NOW)
  → done / missed / skipped*. Cooldown and resource awareness (e.g. "on cooldown, ready
  in 6 s", "no Lily").
- **F4 Usage tracking.** Detect your own action uses; mark entries done; optional
  post-pull chat summary.
- **F5 Import pipeline.** Paste or link → layout detection → cell parsing → action and
  mechanic resolution → timing → review screen → saved plan. Unresolvable text becomes a
  timed text note rather than being dropped.
- **F6 Config.** All timings, sound, visibility and overlay options configurable; preview
  mode to see/position the overlay out of combat; simulation of a fight for testing.
  **Changed (final review, 2026-11-11):** of the timings, only the two lead times are
  settings: when a reminder appears (3–60 s) and when it turns NOW (0–10 s). Fixed are
  the end of the due window (0.3 s before the hit for mitigation, shields and pre-placed
  heals, 5 s after the press for heals used after the hit, at least 8 s after a phase-end
  transition; §5.2), how long finished rows stay listed (3 s), and the pull rules (10 s
  out of combat ends a pull, a countdown stopped with more than 1 s left is cancelled,
  pre-pull reminders wait 20 s after a countdown; §4, PLUGIN.md §1.5). Sound, visibility
  (done/missed tracking, the co-healer's calls, skipped calls) and every overlay option
  are settings, as are the preview speed and pull recording.
- **F7 Multi-fight.** Fights are data (timeline + mechanic catalog); adding one requires
  no code.

### 1.3 Non-functional requirements

- No automation of any game action; no network traffic except user-initiated plan downloads.
- Works with the English client first; action identity is by game action ID so display
  follows the client language; source-name matching in timelines is optional.
- Core logic (parsing, sync, import, scheduling) is plain .NET with no Dalamud
  dependency and is unit-tested in CI/the container; Dalamud-specific code is a thin layer.
- Respect level sync: validate plans against the fight's sync level (80/90/100). **As
  built:** the import review checks every entry (`ReviewValidator`); share codes and plan
  files are checked with the review's rules when they load, reporting only calls that can
  never work (a mechanic the fight lacks, an unknown action, an action the entry's job or
  slot cannot use, or not at the sync level; also an offset that is not a number and a
  job-less call for a slot that is no party slot); the bundled plans are checked in the
  tests (the core's `PlanValidator`); the CLI's `check` and `validate` check plans too
  (`PlanChecker`).

---

## 2. Architecture

As built (class names as in the code):

```
MitCue.Core (net10.0, no Dalamud)
  Import      SheetReaders (xlsx, TSV/CSV, fixture JSON; GoogleHtmlView: tab names) → SheetDocument
              → LayoutDetector → CellParser → PlanImporter: MechanicResolver, ActionResolver, HintParser,
                RowTiming (phase zero fit, calibration presets, row offsets) → ImportResult (MitPlan + diagnostics)
              PlanValidator: any plan against a fight and the action catalog
  Data        FightLibrary, FightLoader (data/fights/*.yaml) · TimelineParser (data/timelines/*.txt)
              · ActionCatalog (data/actions/actions.yaml) · AliasDictionary (data/actions/aliases.yaml)
              · EmbeddedDataSource (data/** and plans/** are built into the DLL), DirectoryDataSource
  Plans       MitPlan · PlanSelection · ColumnSuggestion (the automatic column, for the plugin and the CLI)
              · PlanSerializer (YAML, JSON, share codes) · PlanChecker (recasts, gauges)
  Runtime     GameEvent ─► TimelinePlayer (sync) ─► timeline time ─► ReminderScheduler (TimingRules) ─► Reminder[]
              own action uses ─► ReminderScheduler.OnOwnAction;  ICooldownProvider ─► "ready in 6s", "no Lily"
  Simulation  FightSimulator (a generated pull from a timeline) · GameVisibleEvents (the kinds seen in game)

MitCue (Dalamud plugin, Dalamud.NET.Sdk 15)
  Game/       GameEventSource (cast polling, ActionEffectHook, InCombat, IDutyState, territory, countdown)
              · CooldownProvider (ActionManager, job gauges) · OwnUseDeduper, PetActions · PullRecorder · SoundPlayer
  Data/       CoreData (the core's embedded data + personal aliases) · GameData (Lumina: names, icons, zones)
  Plans/      PlanStore (bundled and user plans, active plan, column choice, share codes)
              · PlanChecks (the review's checks for share codes and plan files)
  Runtime/    SessionManager ─► EncounterSession (TimelinePlayer + ReminderScheduler) · PreviewDriver
              · PartnerSelection (the co-healer's calls) · OverlayModel · ReminderText (labels, pull summary)
  Import/     ImportPipeline · BackgroundJobs (thread pool) · SheetFetcher, GoogleLinks · DocHtmlReader, DocTextReader
              · ImportAnalysis · ReviewModel, ReviewValidator · ImportColumns · LlmPrompt · PersonalAliases
  UI/         OverlayWindow · ConfigWindow · ImportWindow (+ review) · DebugReport

tools/MitCue.Cli: fights, mechanics, timeline, import, plan, simulate, check, validate, share, unshare.
```

Repository layout:

```
src/MitCue.Core/           core library
src/MitCue/                Dalamud plugin
tests/MitCue.Core.Tests/   core tests (+ tests/fixtures/sheets/*: snapshots of the example sheets)
tests/MitCue.Plugin.Tests/ plugin tests (built against Dalamud's assemblies; no game needed)
tests/MitCue.Cli.Tests/    CLI tests
tools/MitCue.Cli/          developer CLI
tools/RepoJsonCheck/       checks a repo.json and the plugin zip with Dalamud's own manifest types
tools/make-repo-json.py    writes a repo.json from the plugin manifest (CI checks a sample)
tools/tests/               Python tests of make-repo-json.py and of the CI workflow's Dalamud step
data/timelines/            cactbot timelines (Apache-2.0, see data/timelines/NOTICE; DSR and FRU only for parser tests)
data/fights/               fight catalogs (top.yaml, tea.yaml, dmu.yaml)
data/actions/              action knowledge base + shorthand aliases
plans/                     converted plans shipped with the plugin (top-noirmit-whm, top-ctc-sch, top-lpdu)
assets/                    the icon (SVG source, PNG renders)
docs/                      this design, PLUGIN.md, INSTALL.md, PUBLISHING.md, research notes
.github/workflows/ci.yml   CI: build, every test project, the tools' tests, plugin zip and repo.json checks
```

---

## 3. Data formats

### 3.1 Fight catalog (`data/fights/<key>.yaml`)

```yaml
fight: top
name: The Omega Protocol (Ultimate)
territory: 1122
sync_level: 90
timeline: the_omega_protocol.txt          # file in data/timelines
phases:
  - { id: p1, name: "P1: Omega", start: 0.0 }       # timeline seconds of the phase's sync point
mechanics:
  - id: p6.cosmo_memory                   # stable id used by plans
    name: Cosmo Memory                    # short display name
    phase: p6                             # optional: else the phase its hit falls in
    hit: 1167.8                           # timeline seconds of the (first) damage event
    hits: [1167.8]                        # all damage times for multi-hit mechanics
    entry: "Cosmo Memory"                 # cactbot entry at that time (validated in tests)
    kind: raidwide                        # raidwide|tankbuster|multihit|stack|spread|mixed|lb_check|enrage|other|marker
    aliases: ["Cosmo Memory", "CM"]
calibration:                              # known sheets' phase zero points (timeline seconds of sheet 0:00)
  carry_the_clear: { p1: 0.9, p2: 201.8, ... }
row_offsets:                              # per sheet: seconds from a mechanic's first hit to what its row time marks
  carry_the_clear: { p5.blind_faith: -1.2, ... }
```

Pseudo-mechanics (`kind: marker`) exist for anchors that are not damage (`p1.pull`,
phase starts, the boss becoming targetable).

`calibration` and `row_offsets` are keyed by sheet (the importer's calibration preset,
§6). `row_offsets` names mechanics whose rows in that sheet time another event than the
mechanic's hit (Carry the Clear times Blind Faith at the cast end, 1.2 s before the hit);
the importer adds the value to row-relative offsets (§6 step 7).

Optional `timeline_additions`: cactbot sync lines appended to the timeline when it loads (the bundled file stays
cactbot's). Each line must have a sync and may not jump to a label. Label and hideall statements are refused (they
would change the cactbot file's): the fight file then does not load. The additions are fallbacks for phase starts
that cactbot syncs only on event types the plugin does not see (§4). `dmu.yaml` syncs DMU's P2 and P4 starts on
cast starts, since cactbot uses director updates there:

```yaml
timeline_additions: |
  315.1 "--sync--" StartsUsing { id: "C24C", source: "Kefka" } window 320,5
  1095.0 "--sync--" StartsUsing { id: "C2DC", source: "Kefka" } window 1095,14000
```

### 3.2 Plan (YAML; stored as JSON, shared as a compressed share code)

```yaml
format: mitcue-plan/1                      # missing = this format; any other format is refused
id: 6f1c…                                  # GUID
name: NoirMit - WHM
fight: top
source: { kind: google-sheet, url: "https://docs.google.com/…#gid=777100", tabs: ["TOP – White Mage"], imported: 2026-11-11 }
assignees:                                 # one per source column; a player picks theirs
  - { key: "White Mage", slot: null, job: WHM }
entries:
  - id: e012
    mechanic: p1.pantokrator               # anchor (always a mechanic id)
    hit_index: null                        # which hit of a multi-hit mechanic (0-based); omitted = the first
    offset: -3.3                           # seconds relative to that hit; omitted = automatic (§5.1)
    who: { job: WHM }                      # slot and/or job (and/or assignee key)
    do:                                    # what to press; order matters when seq: true
      - { action: 16536, name: Temperance }
      - { any: [ { action: 3570, name: Tetragrammaton }, { action: 7432, name: Divine Benison } ] }   # either/or
      - { note: "Everything" }             # unresolved text, shown but not tracked
    seq: false
    target: null                           # e.g. "MT", "your 3rd/4th-in-line player"
    note: null                             # item hint ("after the 1st stack")
    detail: null                           # row notes column
    optional: false                        # grey/optional in the source: never counted as missed
    when_ready: false                      # "⏱"/"on CD": press at the planned time or as soon as it is ready
    at_phase_end: false                    # "as P1 dies", "just before P3 ends": due at the transition (§5.2)
    severity: 3                            # source lethality (★ count), if any
    src: "'TOP – White Mage'!C18"          # provenance for review
```

The bundled plans use both, e.g. NoirMit - WHM's Asylum before Program Loop's third set
(`hit_index: 2`) and LPDU's "Rampart as P1 dies" (`at_phase_end: true`).

Forms (`PlanSerializer`): YAML with snake_case keys, written without nulls, `false` and
empty lists (`plans/*.yaml`, the CLI, the AI route); JSON with camelCase keys for user
plans (`pluginConfigs/MitCue/plans/<id>.json`); share codes, `MITCUE1:` + base64url of
the raw-deflated compact JSON. Readers ignore unknown fields and refuse repeated keys,
YAML aliases and jobs that are not combat jobs.

Selection rule for the local player (`PlanSelection`): an entry applies when

- its `who.job` is empty or equals the current job;
- its `who.slot` is empty or equals the player's slot; while the player's slot is
  unknown, an entry with a slot applies only if it also names the job;
- when the player picked a column (or it was suggested), its assignee is empty or that
  column. An entry of another column still applies when it names the player's slot and
  either their job, or no job when the slot is one of the job's role (MT/OT for tanks,
  H1/H2 for healers, M1/M2 for melee, R1/R2 for ranged): a Warrior in MT who follows
  "WAR/DRK WAR" gets LPDU's job-less "MT" Reprisals.

Slots compare by name, so every plan reader (`PlanSerializer`: plan files, share codes,
the AI's YAML, the CLI) writes the synonyms `T1`, `T2`/`ST` and `D1`–`D4`, in any letter
case and with spaces around them, as `MT`, `OT`, `M1`, `M2`, `R1` and `R2`, and the plugin
does the same when it saves a plan.

### 3.3 Actions (`data/actions/actions.yaml`) and aliases (`data/actions/aliases.yaml`)

`actions.yaml`: id, name, jobs, level, recast, charges, resource cost, effect duration per
sync level (80/90/100), kind (party_mit, shield, regen, heal, tank_mit, invuln, buff, lb,
movement, other), upgrades/replacements. Built from game data; in-game, Lumina provides
names/icons and ActionManager the real (trait-adjusted) recast and charges.

`aliases.yaml`: lowercase shorthand → action(s) (optionally per job), role phrases with
per-job expansion ("party mit", "short cd"), and note-only phrases ("everything",
"kitchen sink"). User corrections made during review are stored as personal aliases
(**as built:** in the plugin's settings, `MitCue.json`, and added to the dictionary when
the data loads).

---

## 4. Timeline and sync engine

- Timelines are cactbot `.txt` files parsed by `TimelineParser` (grammar and semantics:
  `docs/research/cactbot-timelines.md`). Entries have a time, a name, an optional sync
  (event type + field matchers), a sync window, and optional jump/forcejump/label. A fight
  file's `timeline_additions` (§3.1) are parsed with the same grammar and appended.
- `TimelinePlayer` consumes `GameEvent`s with monotonic timestamps. While running, timeline
  time advances with the clock. When an event matches an entry's sync and the current time
  lies within that entry's window, time snaps to the entry (or to its jump target). Phase
  pushes are handled by the large windows on phase-start syncs.
- Supported event types for v1: `InCombat`, `StartsUsing` (boss cast start, from cast bars),
  `Ability` (resolved action, from the ActionEffect hook), wipe/reset (from duty state).
  Other types (`MapEffect`, `HeadMarker`, `GameLog`, …) are parsed and ignored unless a
  fight needs them and has no fallback (see research notes per fight). **As built:** no
  fight needs them. TOP enters P2 on the Firewall cast (`StartsUsing 7B40`, 203.4) instead
  of the `MapEffect` at 200.0; TEA's phase starts are all casts and abilities; DMU's P2 and
  P4 starts are cactbot `ActorControl` syncs, so `dmu.yaml` adds cast-start fallbacks
  (Ultimate Embrace at 315.1, Kefka Says at 1095.0), derived from cast data and not yet
  checked against logs.
- Matching uses ability IDs; `source` names are matched only when the client language
  matches the timeline's (English), otherwise ignored.
- Reset on wipe, duty recommence, territory change, or prolonged out-of-combat. **As
  built:** also on a clear, on a sync that stops the timeline (cactbot's reset), and on
  combat end once the timeline has stopped; prolonged out-of-combat is 10 s without combat
  and without enemy casts or abilities. During a pull, combat edges are not passed to the
  timeline, because a dead player loses the flag while the fight goes on.
- A sync that moves time by more than 5 s is a jump: the scheduler skips the reminders it
  passes, except phase-end reminders (§5.2). A timeline that starts mid-fight (a pull
  joined in combat) counts as a jump from −∞.
- Simulation: `FightSimulator` generates a pull from a timeline (the events its syncs
  expect, with jitter and phase pushes). `GameVisibleEvents` keeps only the kinds the
  plugin sees in game, for `/mitcue preview` and `mitcue simulate`.

---

## 5. Reminder logic

### 5.1 Planned press time

For each applicable entry: `hit` = the anchor mechanic's hit time (or the n-th hit when
an "after the n-th hit" hint was parsed); `press = hit + offset`.

When `offset` is not given (plans without times), it is derived from the action: its `when`
field (`actions.yaml`, docs/research/actions.md §6) decides the side of the hit when present,
otherwise its kind does (code: `TimingRules`).

| action | default press time |
|--------|--------------------|
| `when: before` — heals placed before the damage that act on a later event: Liturgy of the Bell, Excogitation, Earthly Star, Horoscope, Macrocosmos | `hit − lead` when the action has a `lead` (Earthly Star: 10 s to mature); otherwise a heal is timed like mitigation of the same duration, `hit − clamp(0.3·D, 2, 8)` (Bell −6, Excogitation −8, Macrocosmos −4.5, Horoscope −3), and other kinds keep their row below |
| `when: after` (healer LBs) | `hit + 2` for limit breaks, else `hit + 1` |
| party_mit, tank_mit, regen (duration D) | `hit − clamp(0.3·D, 2, 8)`; `hit − 3` when D is unknown |
| shield (GCD, cast time C) | `hit − max(4, C + 2.5)` |
| invuln | `hit − 2` |
| heal / instant heal | `hit + 1` (after the damage) |
| tank LB3 / healer LB3 (no `when`: by the role of the LB's jobs) | `hit − 3` / `hit + 2` |
| buff, movement, other, notes, actions not in the catalog | `hit − 3` |

### 5.2 States

- **Upcoming:** `press − lookahead ≤ now < press − dueLead` (defaults: lookahead 15 s, dueLead 1.5 s).
- **Due (NOW):** from `press − dueLead` until `latest`, where `latest = hit − 0.3` for
  mitigation, shields and pre-placed heals (`when: before`), and `press + 5` for heals used
  after the damage and anything planned after the hit (never before `press + 0.5`). Sound
  plays once on entering Due.
- **Done:** the action (or any alternative) was used between `earliest` and `latest`,
  with `earliest = hit − D + 1` for duration effects, else `press − 10`. A use before
  `earliest` is flagged *early*. For `when: before` actions D is the longest of their
  effects (Horoscope Helios 30 s), so a pre-placed heal still active at the hit (Bell up to
  19 s, Excogitation up to 44 s before it) is on time, not early.
- **Missed:** `latest` passed without a matching use (not counted for optional entries).
- **Skipped:** the timeline jumped past the entry (phase push) before it became due.
  Exception — **phase-end reminders**: entries anchored to a phase's enrage/end mechanic
  (kind `enrage`, or flagged `at_phase_end` by the importer, e.g. "Deploy just before P3
  ends", "Rampart as P1 dies") are not skipped by the jump that ends their phase: they
  become Due at the jump, because the transition is exactly when they should be pressed.
  They stay Due until `jump target + max(heal late window, 8 s)` (8 s with the defaults), then
  Missed as usual; one already Due when the push comes is extended the same way, without a
  second sound. The jump that ends their phase passes their hit (or planned window), starts in
  the phase their press time belongs to and lands in that phase or the next one: a phase push,
  or a push inside a phase that ends a stage of it before its enrage (TEA P3: killing the DPS
  check adds jumps over the Eternal Darkness enrage to 771.9, still in P3). Other jumps skip
  them like any other reminder: one that starts in another phase, one that lands two or more
  phases on (a preview that cuts whole phases), and a timeline started mid-fight (a pull
  joined in combat: the plugin passes the start as a jump from −∞, never from 0).
- `when_ready` entries stay Due past `press` until the action comes off cooldown, then
  until used or `latest`.

Finished reminders (Done, Missed, and Skipped when shown) stay listed for 3 s; notes
(and optional entries) whose time has passed become *Passed* and are not listed. **As
built:** lookahead and dueLead are settings; the 0.3 s, 5 s ("heal late window") and 3 s
are fixed: `ReminderOptions` defaults that `Configuration` does not expose. The 8 s is the
constant `ReminderScheduler.MinPhaseEndGrace` (see F6). With done/missed tracking off, the
overlay drops Done and Missed rows at once.

### 5.3 Cooldown and resource awareness

At `press − lookahead` the plugin checks the action's remaining recast/charges and
required resource (Lily, Blood Lily, Aetherflow, Addersgall, …). If it will not be ready
by `press`, the reminder shows "ready in N s" in warning colour. **As built:** not in
previews, which have no cooldown provider.

---

## 6. Import pipeline

1. **Source → `SheetDocument`.** Tabs → rows → cells `{text, bg, fg, bold, runs, note}`.
   Readers: xlsx (downloaded from a public link, keeps colours and rich-text runs),
   TSV/CSV paste (text only), JSON fixtures (tests). **As built:** the xlsx export renames
   tabs Excel does not allow (`P1/P2` → `P1P2`); the CLI and the plugin restore Google's
   names from the sheet's `htmlview` page (the plugin asks for it with the workbook and
   waits at most 3 s more for it). Google Docs are read from their web page export
   (each table becomes a tab, the other paragraphs a "Text" tab) or, when that is too
   large, their plain-text export; colours are not kept.
2. **Layout detection.** Find the header row; classify columns: time, mechanic label,
   assignee (slot/job/name headers such as `MT`, `H2 SCH`, `White Mage`), notes, ignored
   (statistics). Detect phase from tab names (`P3`), section rows (`P2 — Omega-M/F (from
   phase start)`), or label cells (`P2`).
3. **Cell parsing.** Split into items: ` / ` separates independent actions (each may have
   its own time), `+` and `,` join simultaneous actions, `→`/`->` make a sequence, `X/Y`
   without spaces means either/or (often across jobs: `Kera/Soil`). Extract `(1:18)`,
   `~1:22`, `≈3:04`, ranges `1:47 – 1:56`, multi-times `0:28 / 0:37`, targets
   (`on MT`, `(your 3rd/4th-in-line player)`), hints (`after 1st Hit`, `on CD`), markers
   (`*`, `⏱`), and formatting flags (grey = optional).
4. **Action resolution.** Alias dictionary → exact name → fuzzy match (typos such as
   *Nascant*, *Vengance*), restricted to the assignee's job and the fight's sync level.
   Cross-job alternatives are split per job. Role phrases expand per job. Anything else
   becomes a note.
5. **Mechanic resolution.** Per-fight aliases + phase context + monotonic row order +
   timestamps. Same label in different phases (`Hello World`, `Solar Ray`, `Magic Number`)
   is disambiguated by phase and order.
6. **Phase calibration.** For timestamped plans, fit each phase's zero point as the median
   of `timeline hit − sheet time` over confidently matched rows (falling back to the fight's
   `calibration` presets); report the spread. (TOP / Carry the Clear: P1 0.9, P2 201.8,
   P3 399.4, P4 606.7, P5 714.7, P6 1099.3; spread ≤ 1 s in P1–P4, up to 2.4 s in P5–P6
   because the sheet's reference log drifts against cactbot — docs/research/mechanics.md.)
7. **Timing.** Prefer **row-relative** offsets: when an item has its own time and its row
   has a damage time, `offset = t_item − t_row + δ`, where δ corrects rows whose time marks
   the boss cast end rather than the damage (mechanics.md lists them: P2 Cosmo Memory
   +1.4 s, Hello World rounds +1.0 s, Blind Faith, P6 enrage…). This needs no phase zero and
   is immune to drift. Only items without a usable row time use `offset = (zero + t) − hit`.
   Otherwise §5.1 defaults; parsed hints adjust the anchor (e.g. after the 1st hit). Items
   described as happening at a phase's end are anchored to that phase's enrage/end mechanic
   and flagged `at_phase_end` (§5.2). **As built:** δ per sheet and mechanic is the fight
   file's `row_offsets` (§3.1). For Carry the Clear they are Blue Screen +1.0 s, the
   Omega row +1.1 s, Blind Faith −1.2 s and the P6 enrage −14.0 s; P2 Cosmo Memory and
   the Hello World rounds need none, because the catalog anchors them at the cast end
   too. A timed item counts from the hit it belongs to (the first hit at or after its
   press; for heals the last one at or before it), stored as `hit_index`.
8. **Review.** Every entry with a confidence level and diagnostics; the user fixes items,
   fixes become personal aliases; save. **As built** (the import window, PLUGIN.md §1.7):
   each entry with the importer's diagnostics and the plugin's checks; the user can change
   the mechanic, hit, offset, flags, target, note and items, and delete entries; replacing
   a note with an action offers to save the shorthand as a personal alias; the plan is
   saved as a user plan, made the active plan when *Make it my active plan …* is ticked
   (it is by default when entries apply to the player), with the column choice that has
   all of the player's calls when one has them without another column's (otherwise the
   Save step names the columns of the calls left out).

LLM fallback: the plugin generates a prompt containing the plan schema, the fight's
mechanic catalog and the job's alias list; the user runs it with their source text in any
LLM and pastes the YAML back, which goes through the same validation and review. **As
built:** the prompt also lists the chosen jobs' actions usable at the fight's sync level,
and holds the loaded sheet's text when one is loaded; the YAML is read out of the answer
(text around its code block is ignored).

Code: steps 1–7 are the core's `SheetReaders`, `LayoutDetector`, `CellParser` and
`PlanImporter` (`src/MitCue.Core/Import/README.md`); the CLI's `import` runs them on
files and links; the plugin's import window adds downloading, the review and the AI route.

---

## 7. Plugin

- **Events:** cast polling of hostile battle NPCs each framework tick (emit cast start once
  per cast, time-corrected by elapsed cast time); ActionEffect hook for resolved abilities
  (boss syncs and own action uses); `InCombat` condition edges; duty wipe/recommence;
  territory changes.
- **Overlay:** compact list (max N rows), icon + name + countdown + mechanic + target;
  colours per state; optional click-through and lock; scale and opacity.
- **Config defaults:** lookahead 15 s; due lead 1.5 s; max rows 5; sound on due (game SE);
  own calls only; co-healer calls off; show skipped off; post-pull summary on; overlay
  locked off; hotbar glow not in v1. **As built:** also track done/missed on, sound effect
  `<se.6>`, click-through off, preview at 1×, pull recording off.
- **Plans tab:** list/import/export/share code, active plan per fight and job, "which
  column is me". **As built:** without a choice, the active plan for a fight and job is the
  bundled plan meant for the job (TOP: WHM → *NoirMit - WHM*, SCH → *LPDU TOP
  Mitigation*; *Carry the Clear - SCH* only when chosen), else the first bundled plan by
  name that names the job in a column or entry, else the player's first own plan by name
  that names it (TEA and DMU ship no plans). A plan whose calls for the player's seat are
  only in job-less slot columns (LPDU's M1/M2 for melee jobs) is not chosen automatically:
  the player chooses it in the table. An imported plan or a share code replaces a bundled
  plan only when the player chooses it: in the table, or with the "make it my active plan"
  checkbox of the import window (ticked when entries apply to the player) or of the
  share-code box (unticked each time the plugin loads). The exception is the share code
  of a bundled plan: it has that plan's id and takes its place until it is deleted.
  "My column" without a choice is the job's column, which also shows the job-less calls
  of the player's slot (LPDU's H2 Scholar gets "H2 SCH" with the job-less "H2" calls of
  P1/P2), or, when the job's calls are in several of its columns (one per phase or tab),
  the narrowest choice that has them all and no call of another column
  (`ColumnSuggestion`). Columns for team compositions (keys naming two or more jobs) are
  alternatives, never combined: a Warrior on LPDU gets the tank pair with the party's
  co-tank ("WAR/PLD WAR" next to a Paladin), else the first, "WAR/DRK WAR". Plans are
  exported as share codes, not as files.
- **Co-healer's calls** (as built): the plan's other columns of the player's role, except
  the player's own seat in another composition and the partner's columns for compositions
  without the player's job (a Warrior sees "WAR/DRK DRK", not "DRK/GNB DRK"), and in a
  party only columns of jobs someone in it plays. The partner's columns that are not a
  composition are kept (a general "DRK" next to "WAR/DRK DRK"); a combined-tab column that
  repeats the partner's other column is shown once.
- **Preview/simulation:** run the fight timeline with simulated events at 1×/4× from any
  phase to check the overlay and the plan. **As built:** only the event kinds the plugin
  sees in game are played, so a preview syncs like a pull.
- **Data loading** (as built): fight data and plans load on the first framework tick;
  **Reload data** (Debug tab) reloads the fight data on the next tick. The live session
  keeps the data it was built with until it is rebuilt (`/mitcue reset`, or a change of
  zone, job, plan or column).

---

## 8. Testing

- Unit tests for the parser (all bundled timelines parse; constructs covered), the engine
  (simulated pulls with jitter and variable phase pushes stay within tolerance), importers
  (both example sheets are acceptance fixtures; every row is accounted for), scheduler
  states, plan round-trips.
- Data validation tests: every mechanic `entry`/`hit` exists in its timeline; every alias
  resolves to a real action available to its job at the fight's sync level.
- Later: replay real ACT/IINACT network logs from the static's pulls through the engine.
- In-game testing happens on the user's PC (dev plugin path), using the preview mode.

**As built:**

- `tests/MitCue.Core.Tests`: the above, plus the bundled plans: each is the importer's
  output for its sheet fixture and valid at level 90.
- `tests/MitCue.Plugin.Tests`: sessions and pulls, the plan store with the automatic plan
  and column choices on the bundled plans, the co-healer's calls, the import pipeline
  (link parsing, downloads through a fake HTTP handler, pasted cells, the AI prompt and
  YAML), the review model, background jobs. Built against Dalamud's assemblies; no game
  needed.
- `tests/MitCue.Cli.Tests`: every CLI command.
- `tools/tests` (Python): `make-repo-json.py` and the CI workflow's Dalamud download step.
- CI (`.github/workflows/ci.yml`) runs all of these on every push to the `mitcue` branch
  and every pull request into it, and checks the plugin zip against Dalamud's manifest
  types.
- Real pulls: the plugin records them on request (`Record pulls`, JSON lines); replaying
  recordings through the engine is still to come.
- In-game test checklist: [PLUGIN.md](PLUGIN.md) §2.

---

## 9. Milestones

1. Core: data, timeline parser, sync engine, plan model, scheduler, tests. **Done.**
2. Importer (core) + CLI; convert both TOP plans; acceptance tests on both sheets.
   **Done** (three plans: Carry the Clear's White Mage tab as *NoirMit - WHM* and its
   Scholar tab, LPDU; see D2).
3. Plugin: events, overlay, config, plan store, preview; packaging; custom repo.
   **Done**; MitCue is published through the shared repository since 0.1.0.1 (D1,
   [PUBLISHING.md](PUBLISHING.md)).
4. In-game importer/review UI and link download; LLM prompt fallback. **Done.**
5. TEA and DMU data; plan checks (cooldown conflicts); polish. **Partly done:** TEA and
   DMU fight data ship as first drafts without plans; the recast and gauge checks are in
   the CLI (`mitcue check`), and the plugin checks plans that skip the review against the
   fight and the action data.
