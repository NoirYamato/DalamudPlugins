# MitCue — design

MitCue is a Dalamud plugin for Final Fantasy XIV. It reads a raid mitigation plan
(written in Google Sheets or Docs by the community, in whatever layout the author
liked), converts it into one standard format, and during the fight shows a small
window telling *you* which of *your* actions to press next, synced to the boss.

It is **reminder-only**: it never presses, queues or targets anything.

Status: draft v0.2 (requirements agreed 2026-11-11; implementation details are
filled in from `docs/research/*` as they land).

---

## 1. Requirements

### 1.1 Decisions from the requirements discussion

| # | Topic | Decision |
|---|-------|----------|
| D1 | Audience | Private: the author and their static. Distributed through a **custom plugin repository** (not the official Dalamud repo), so the official repo's combat-plugin pre-approval and AI-usage policy do not apply. We still follow the official rules' spirit: informational only, no automation, no FFLogs integration inside the plugin. |
| D2 | First users | **WHM (H1)** following the *Carry the Clear* TOP sheet; **SCH (H2)** following the *LPDU TOP Mitigation Sheet*. Different players in one party may load different plans. |
| D3 | Build order | Our choice: runtime first (sync + overlay) with the two plans converted by our own importer, then the in-game importer UI. Plugin dependencies are allowed if useful. |
| D4 | Import | Public Google Sheets/Docs **links** (the plugin downloads them) **and paste**. A "copy conversion prompt" for any LLM is the fallback for layouts the importer cannot read. |
| D5 | Reminder behaviour | Defaults below, all changeable in a config window: show reminders 15 s ahead, sound when an action becomes due, track done/missed, own calls only (co-healer's calls optional), post-pull summary optional, no hotbar glow in v1. |
| D6 | Plans without times | Timing is derived from each action's effect duration and kind; the user can adjust any entry during review. |
| D7 | Fights | TOP first (level 90). Next: **TEA** (The Epic of Alexander (Ultimate), level 80) and **DMU** (Dancing Mad (Ultimate), level 100). |
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
- **F7 Multi-fight.** Fights are data (timeline + mechanic catalog); adding one requires
  no code.

### 1.3 Non-functional requirements

- No automation of any game action; no network traffic except user-initiated plan downloads.
- Works with the English client first; action identity is by game action ID so display
  follows the client language; source-name matching in timelines is optional.
- Core logic (parsing, sync, import, scheduling) is plain .NET with no Dalamud
  dependency and is unit-tested in CI/the container; Dalamud-specific code is a thin layer.
- Respect level sync: validate plans against the fight's sync level (80/90/100).

---

## 2. Architecture

```
            ┌────────────────────────── MitCue.Core (net10.0, no Dalamud) ──────────────────────────┐
 Sheet/Doc  │  Import: SheetDocument → LayoutDetector → CellParser → ActionResolver                 │
 link/paste ─►          → MechanicResolver → PhaseCalibrator → TimingAssigner → MitPlan + diagnostics│
            │                                                                                       │
            │  Data: FightCatalog (data/fights/*.yaml) · Timeline (cactbot *.txt) · ActionCatalog    │
            │        (data/actions/actions.yaml) · AliasDictionary (data/actions/aliases.yaml)       │
            │                                                                                       │
 GameEvents ─► TimelinePlayer (sync engine) ──► timeline time ──► ReminderScheduler ──► Reminder[]   │
            │                                    UsageTracker ◄── own action uses                    │
            └───────────────────────────────────────────────────────────────────────────────────────┘
            ┌────────────────────────── MitCue (Dalamud plugin, net10.0-windows) ────────────────────┐
            │ GameEventSource (cast polling, ActionEffect hook, InCombat, IDutyState, territory)     │
            │ CooldownProvider (ActionManager) · ResourceProvider (job gauges) · LuminaActionCatalog │
            │ OverlayWindow · ConfigWindow · ImportWindow (+ review) · PlanStore · SummaryReporter    │
            └───────────────────────────────────────────────────────────────────────────────────────┘
 tools/MitCue.Cli: import a sheet/fixture → plan YAML; simulate a fight → print reminders over time.
```

Repository layout:

```
src/MitCue.Core/        core library
src/MitCue/             Dalamud plugin
tests/MitCue.Core.Tests xUnit tests (+ tests/fixtures/sheets/* snapshots of the example sheets)
tools/MitCue.Cli/       developer CLI
data/timelines/         cactbot timelines (Apache-2.0, see data/timelines/NOTICE)
data/fights/            fight catalogs (top.yaml, tea.yaml, dmu.yaml)
data/actions/           action knowledge base + shorthand aliases
plans/                  converted plans shipped with the plugin
docs/                   this design + research notes
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
    phase: p6
    hit: 1167.8                           # timeline seconds of the (first) damage event
    hits: [1167.8]                        # all damage times for multi-hit mechanics
    entry: "Cosmo Memory"                 # cactbot entry at that time (validated in tests)
    kind: raidwide                        # raidwide|tankbuster|multihit|stack|spread|mixed|lb_check|enrage|other
    aliases: ["Cosmo Memory", "CM"]
calibration:                              # known sheets' phase zero points (timeline seconds of sheet 0:00)
  carry_the_clear: { p1: 0.0, p2: 202.0, ... }
```

Pseudo-mechanics exist for anchors that are not damage (`p1.pull`, phase starts).

### 3.2 Plan (`*.mitplan.yaml`, also stored as JSON and shared as a compressed share code)

```yaml
format: mitcue-plan/1
id: 6f1c…                                  # GUID
name: Carry the Clear – WHM
fight: top
source: { kind: google-sheet, url: "https://docs.google.com/…#gid=777100", tabs: ["TOP – White Mage"], imported: 2026-11-11 }
assignees:                                 # one per source column; a player picks theirs
  - { key: "White Mage", slot: null, job: WHM }
entries:
  - id: e012
    mechanic: p1.pantokrator               # anchor (always a mechanic id)
    offset: -3.3                           # seconds relative to the mechanic's hit; omitted = automatic (§5.1)
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
    severity: 3                            # source lethality (★ count), if any
    src: "'TOP – White Mage'!C18"          # provenance for review
```

Selection rule for the local player: an entry applies when its `who.job` is empty or
equals the current job, its `who.slot` is empty or equals the player's slot, and its
assignee (if any) is the one the player picked for this plan.

### 3.3 Actions (`data/actions/actions.yaml`) and aliases (`data/actions/aliases.yaml`)

`actions.yaml`: id, name, jobs, level, recast, charges, resource cost, effect duration per
sync level (80/90/100), kind (party_mit, shield, regen, heal, tank_mit, invuln, buff, lb,
movement, other), upgrades/replacements. Built from game data; in-game, Lumina provides
names/icons and ActionManager the real (trait-adjusted) recast and charges.

`aliases.yaml`: lowercase shorthand → action(s) (optionally per job), role phrases with
per-job expansion ("party mit", "short cd"), and note-only phrases ("everything",
"kitchen sink"). User corrections made during review are stored as personal aliases.

---

## 4. Timeline and sync engine

- Timelines are cactbot `.txt` files parsed by `TimelineParser` (grammar and semantics:
  `docs/research/cactbot-timelines.md`). Entries have a time, a name, an optional sync
  (event type + field matchers), a sync window, and optional jump/forcejump/label.
- `TimelinePlayer` consumes `GameEvent`s with monotonic timestamps. While running, timeline
  time advances with the clock. When an event matches an entry's sync and the current time
  lies within that entry's window, time snaps to the entry (or to its jump target). Phase
  pushes are handled by the large windows on phase-start syncs.
- Supported event types for v1: `InCombat`, `StartsUsing` (boss cast start, from cast bars),
  `Ability` (resolved action, from the ActionEffect hook), wipe/reset (from duty state).
  Other types (`MapEffect`, `HeadMarker`, `GameLog`, …) are parsed and ignored unless a
  fight needs them and has no fallback (see research notes per fight).
- Matching uses ability IDs; `source` names are matched only when the client language
  matches the timeline's (English), otherwise ignored.
- Reset on wipe, duty recommence, territory change, or prolonged out-of-combat.

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
  second sound. The jump that ends their phase starts in the phase their press time belongs
  to and lands in the next one; any other jump (a timeline started mid-fight, a preview that
  cuts whole phases, an enrage branch) skips them like any other reminder.
- `when_ready` entries stay Due past `press` until the action comes off cooldown, then
  until used or `latest`.

### 5.3 Cooldown and resource awareness

At `press − lookahead` the plugin checks the action's remaining recast/charges and
required resource (Lily, Blood Lily, Aetherflow, Addersgall, …). If it will not be ready
by `press`, the reminder shows "ready in N s" in warning colour.

---

## 6. Import pipeline

1. **Source → `SheetDocument`.** Tabs → rows → cells `{text, bg, fg, bold, runs, note}`.
   Readers: xlsx (downloaded from a public link, keeps colours and rich-text runs),
   TSV/CSV paste (text only), JSON fixtures (tests).
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
   and flagged `at_phase_end` (§5.2).
8. **Review.** Every entry with a confidence level and diagnostics; the user fixes items,
   fixes become personal aliases; save.

LLM fallback: the plugin generates a prompt containing the plan schema, the fight's
mechanic catalog and the job's alias list; the user runs it with their source text in any
LLM and pastes the YAML back, which goes through the same validation and review.

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
  locked off; hotbar glow not in v1.
- **Plans tab:** list/import/export/share code, active plan per fight and job, "which
  column is me".
- **Preview/simulation:** run the fight timeline with simulated events at 1×/4× from any
  phase to check the overlay and the plan.

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

---

## 9. Milestones

1. Core: data, timeline parser, sync engine, plan model, scheduler, tests.
2. Importer (core) + CLI; convert both TOP plans; acceptance tests on both sheets.
3. Plugin: events, overlay, config, plan store, preview; packaging; custom repo.
4. In-game importer/review UI and link download; LLM prompt fallback.
5. TEA and DMU data; plan checks (cooldown conflicts); polish.
