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
| D2 | First users | **WHM (H1)** following the *Carry the Clear* TOP sheet; **SCH (H2)** following the *LPDU TOP Mitigation Sheet*. Different players in one party may load different plans. **As built:** both plans ship with the plugin and are the automatic choice for those jobs (§7). **Changed (2026-11-11, MitCue 0.1.0.2 and 0.1.0.3):** the White Mage's plan, still made from Carry the Clear's White Mage tab, is named *NoirMit - WHM* (*Carry the Clear – WHM* in 0.1.0.1; same id). Carry the Clear's combined tab (*Carry the Clear – WHM + SCH*) no longer ships. Its Scholar tab ships as *Carry the Clear - SCH*, never automatic: the Scholar's automatic plan stays LPDU. As the first bundled TOP plan by name, it is what `/mitcue preview top` plays, as SCH, on a job without a TOP plan (a crafter, a melee job without a chosen plan) while the player has no TOP plan of their own. |
| D3 | Build order | Our choice: runtime first (sync + overlay) with the two plans converted by our own importer, then the in-game importer UI. Plugin dependencies are allowed if useful. **As built:** runtime first, then the importer front half and the in-game import window, then the CLI, the importer back half and the converted plans; no plugin dependencies. |
| D4 | Import | Public Google Sheets/Docs **links** (the plugin downloads them) **and paste**. A "copy conversion prompt" for any LLM is the fallback for layouts the importer cannot read. **As built:** all three routes are in the in-game import window (§6). |
| D5 | Reminder behaviour | Defaults below, all changeable in a config window: show reminders 15 s ahead, sound when an action becomes due, track done/missed, own calls only (co-healer's calls optional), post-pull summary optional, no hotbar glow in v1. **As built:** each of these is a setting (§7); see F6 for the timings that are not. |
| D6 | Plans without times | Timing is derived from each action's effect duration and kind; the user can adjust any entry during review. **Changed (2026-11-14):** also from the entry's other actions, the mechanic's later hits and the fight file's timing fields, in one function that the reminders, the importer, the CLI and the review share (§5.1). **Changed (2026-11-15):** at import, a call without a time in the sheet that would find its action not ready yet is pressed 1.5 s after the action is ready again, so that its NOW cue (the default due lead) comes then, but no later than `(Late)` and the actions' own leads allow (the recast fit, §6 step 7). |
| D7 | Fights | TOP first (level 90). Next: **TEA** (The Epic of Alexander (Ultimate), level 80) and **DMU** (Dancing Mad (Ultimate), level 100). **As built (2026-11-11):** the fight data of all three ships. TEA and DMU are first drafts: no mitigation sheet has been mapped onto them, and no plans for them ship. TEA follows phase pushes; DMU's P2 and P4 starts use fallback syncs that are not yet checked against logs (§4). **Changed (2026-11-14):** TEA's data has been mapped onto LPDU's sheet *[LPDU] TEA Heal/Mit* (mechanics and aliases for its rows; the 16 J Waves of the P3 DPS check and the first Inception crystal explosion added to the timeline; timing fields for the Photons, Double Rocket Punch and the J Waves, §3.1, §5.1), and the sheet ships as *LPDU TEA Heal/Mit*, TEA's only plan and so the automatic one of every job it names (§7). The TEA data is still not checked against logs; DMU has no plan yet. |
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
  heals planned before it; 5 s after the press for heals used after the hit and anything
  planned after the hit, such as TEA's Photon heal-up; at least 8 s after a phase-end
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
  never work as planned (a mechanic the fight lacks, an unknown action, an action the
  entry's job or slot cannot use, or not at the sync level; also an offset that is not a
  number, never shown, or for −∞ shown at the start of the countdown or pull, and a
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
              · RecastReplay (check's recast model, which the importer's recast fit uses too)
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
tests/MitCue.Plugin.Tests/ plugin tests (built against Dalamud's assemblies; no game needed; references
                           tools/MitCue.Cli for the invariant sweep)
tests/MitCue.Cli.Tests/    CLI tests
tools/MitCue.Cli/          developer CLI
tools/RepoJsonCheck/       checks a repo.json and the plugin zip with Dalamud's own manifest types
tools/make-repo-json.py    writes a repo.json from the plugin manifest (CI checks a sample)
tools/tests/               Python tests of make-repo-json.py and of the CI workflow's Dalamud step
data/timelines/            cactbot timelines (Apache-2.0, see data/timelines/NOTICE; DSR and FRU only for parser tests)
data/fights/               fight catalogs (top.yaml, tea.yaml, dmu.yaml)
data/actions/              action knowledge base + shorthand aliases
plans/                     converted plans shipped with the plugin (top-noirmit-whm, top-ctc-sch, top-lpdu, tea-lpdu)
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
    # optional timing fields, for the calls planned here without a time (§5.1; top.yaml has only sub_hits):
    # press: after                        # pressed after the hit, like heals (TEA's Photons)
    # heal_lead: 4                        # heals this many s before the hit (TEA's Double Rocket Punch)
    # series: true                        # a later hit's calls wait for the hit before (TEA's J Waves)
    # sub_hits: [110.1, 112.1, …]         # later damage that is no timeline entry (TOP's Kyrios cleaves)
calibration:                              # known sheets' phase zero points (timeline seconds of sheet 0:00)
  carry_the_clear: { p1: 0.9, p2: 201.8, ... }
row_offsets:                              # per sheet: seconds from a mechanic's first hit to what its row time marks
  carry_the_clear: { p5.blind_faith: -1.2, ... }
```

Pseudo-mechanics (`kind: marker`) exist for anchors that are not damage (`p1.pull`,
phase starts, the boss becoming targetable).

The timing fields change the automatic press time of the calls planned on the mechanic
without a time (§5.1). `press: after` is for a hit that makes what was pressed before it
pointless, `heal_lead` (seconds) for a hit the heals must top the targets up for, and
`series: true` for hits that are one raidwide repeated a few seconds apart. `sub_hits`
lists later damage times of the mechanic that are no timeline entries (cactbot shows them
as one entry): they have no hit index, plans and the importer anchor on `hits` only, and
only multi-hit coverage counts them (§5.1 rule 3). The loader accepts only `after` for
`press`, a positive `heal_lead` and never both on one mechanic, `series` only with two or
more hits, and `sub_hits` only after `hit`, in time order and none of them one of `hits`;
`mitcue validate` reports any of them on a marker. `tea.yaml` has `press: after` on both
Photons, `heal_lead: 4` on Double Rocket Punch, `series: true` on `p3.j_waves` and
`sub_hits` on the first Splash (the five Splashes after the first, 1.1 s apart, the last
as Drainage hits at 86.6: BossMod's six casts in cactbot's 5.6 s), `top.yaml` `sub_hits` on
`p1.wave_cannon_kyrios` (the four Diffuse Wave Cannon Kyrios cleaves after the first,
2.1, 4.1, 6.2 and 8.3 s later: BossMod).

`calibration` and `row_offsets` are keyed by sheet (the importer's calibration preset,
§6). `row_offsets` names mechanics whose rows in that sheet time another event than the
mechanic's hit (Carry the Clear times Blind Faith at the cast end, 1.2 s before the hit);
the importer adds the value to row-relative offsets (§6 step 7).

Optional `timeline_additions`: cactbot sync lines appended to the timeline when it loads (the bundled file stays
cactbot's). Each line must have a sync and may not jump to a label. Label and hideall statements are refused (they
would change the cactbot file's): the fight file then does not load. The additions are fallbacks for phase starts
that cactbot syncs only on event types the plugin does not see (§4), or entries for damage an anchor or a later hit
needs that cactbot folds into one entry or does not list. `dmu.yaml` syncs DMU's P2 and P4 starts on cast starts,
since cactbot uses director updates there:

```yaml
timeline_additions: |
  315.1 "--sync--" StartsUsing { id: "C24C", source: "Kefka" } window 320,5
  1095.0 "--sync--" StartsUsing { id: "C2DC", source: "Kefka" } window 1095,14000
```

`tea.yaml` adds the 16 J Waves of TEA's P3 DPS check (`Ability 4877`, every 3 s from 710.9 to 755.9, timed from
BossMod and not yet checked against a log), which cactbot folds into one entry, "J Storm + Waves x16" at 705.7. Each
wave is then an entry that a mechanic anchors on (`p3.j_wave_1` to `p3.j_wave_16`; a mechanic's `entry` is checked
against the additions too) and a sync within 1.4 s of it. LPDU's TEA sheet names them by Brute Justice's Damage Up
stacks: "Jwaves (N)" is the (N+1)th wave. It also adds the first explosion of the Inception crystals (`Ability
4A88` Tetrashatter, 564.5, the anchor of `p3.inception_crystals`), which cactbot does not list. The explosions are
player-timed (~564–568 s), so the sync only catches an early one: its window of 2 s before and none after can move the
clock forward by at most 2 s, once, and the next boss sync (Inception, 581.0) corrects any error.

```yaml
timeline_additions: |
  564.5 "Tetrashatter" Ability { id: "4A88", source: "Judgment Crystal" } window 2,0
  710.9 "J Wave 1" Ability { id: "4877" } window 1.4,1.4
  713.9 "J Wave 2" Ability { id: "4877" } window 1.4,1.4
  …
  755.9 "J Wave 16" Ability { id: "4877" } window 1.4,1.4
```

`top.yaml` adds the P2 Cosmo Memory raidwide (`Ability 7B24`, the Optical Unit's 1.5 s cast, at 320.2: 10.2 s after
Pile Pitch in BossMod's states; not yet checked against a log), which cactbot leaves on its ignore line: its "Cosmo
Memory" (318.8) is the 7B22 cast end, where `p2.cosmo_memory` stays, the time the sheets give it. The raidwide is the
second hit of `p2.limitless`, so that 15 s mitigation planned on LPDU's "Limitless" rows without a time is pressed
2.5 s before the flares and lasts through it (§5.1 rule 3). 7B24 is cast nowhere else in the fight, and its window,
1 s either way, can move the clock by at most 1 s.

```yaml
timeline_additions: |
  320.2 "Cosmo Memory raidwide" Ability { id: "7B24" } window 1,1
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
  "WAR/DRK WAR" gets LPDU's job-less "MT" Reprisals. So does an entry for their job in a
  column of the same team composition in another tab ("WHM/SCH P3 WHM" for the player who
  follows "WHM/SCH P1 WHM": one composition split over per-phase tabs is one seat).

The player's slot is the column's slot, else the configured slot (the plugin's "My usual
party slot", the CLI's `--slot`) when it is a slot of the job's role (`Slots.ForJob`: one
usual slot serves every job, and a melee player's M1 is not their slot on a tank), else,
without a saved choice and for a plan without a column for the job, the slot of the job's
calls (§7). A saved "any column for my job" (the CLI's `--assignee "*"`) has no slot then
and shows every seat's calls (LPDU's Red Mage: R1 and R2).

Slots compare by name, so every plan reader (`PlanSerializer`: plan files, share codes,
the AI's YAML, the CLI) writes the synonyms `T1`, `T2`/`ST` and `D1`–`D4`, in any letter
case and with spaces around them, as `MT`, `OT`, `M1`, `M2`, `R1` and `R2`, and the plugin
does the same when it saves a plan. They also give an entry that names no job the job of
its column when the column has one (`PlanNormalizer.JobsFromColumns`): a job-less call in
"H1 AST" is the Astrologian's, never shown to the White Mage who follows "H1 WHM" in H1,
while the job-less calls of a job-less column ("MT", "H2") stay for whoever plays the slot.

### 3.3 Actions (`data/actions/actions.yaml`) and aliases (`data/actions/aliases.yaml`)

`actions.yaml`: id, name, jobs, level, recast, charges, resource cost, effect duration per
sync level (80/90/100), kind (party_mit, shield, regen, heal, tank_mit, invuln, buff, lb,
movement, other), upgrades/replacements. Built from game data; in-game, Lumina provides
names/icons and ActionManager the real (trait-adjusted) recast and charges. **As built:**
also the timing fields of §5.1: `when` (the side of the hit when it differs from the
kind's: pre-placed heals before, healer LBs after; also `before` on Neutral Sect and
Passage of Arms, whose kinds are pressed before the hit anyway, so that their `lead`
counts), `lead` (seconds before the hit for a `when: before` action: Earthly Star 10,
Neutral Sect 4.5, Passage of Arms 2), `barrier_cast` (the cast of the GCD that puts up
the action's barrier after it: Neutral Sect's 1.5 s Aspected Helios or Helios
Conjunction) and `instant_next_cast` (`true` for an action that makes the next spell
instant: Swiftcast, so that a call that lists it counts no cast before a series floor,
§5.1 rule 4). `lead` and `barrier_cast` must be positive numbers of seconds, or the
catalog does not load (`mitcue validate` reports it). Eukrasia's `recast` is hand-set to
1 s, the GCD the game locks after it (in cactbot's sample log of a Sage, 36 of 37 Eukrasia
→ Eukrasian action pairs are 0.98–1.07 s apart), not the game data's 2.5 s: `(Late)` and
multi-hit coverage leave a Eukrasian action 1 s for it (§5.1 rule 3, §6 step 3).
The fields and the loader's rules: docs/research/actions.md §6.

`aliases.yaml`: lowercase shorthand → action(s) (optionally per job), role phrases with
per-job expansion ("party mit", "short cd"), and note-only phrases ("everything",
"kitchen sink"). User corrections made during review are stored as personal aliases
(**as built:** in the plugin's settings, `MitCue.json`, and added to the dictionary when
the data loads). **As built (2026-11-14):** an action name may list options separated by
`|`, one item that any of them does: LPDU TEA's "defensive Cards" are `the Arrow|the Bole`
and `the Spire|the Ewer`, whichever was drawn (`AliasExpansion.OptionsOf`).

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
an "after the n-th hit" hint was parsed); `press = hit + offset`, the press of every item
of the entry. Each item has its own use window (§5.2).

When `offset` is not given (plans without times), the press time is automatic. One
function computes it (`TimingRules.ForEntry`), and the scheduler, the importer, the CLI's
`plan` and `check` and the import review all use it. It starts from the default of the
entry's timing action, the first item action the catalog knows (of an either/or, its
first known option): the action's `when` field (`actions.yaml`, docs/research/actions.md
§6) decides the side of the hit when present, otherwise its kind does.

| action | default press time |
|--------|--------------------|
| `when: before` — heals placed before the damage that act on a later event (Liturgy of the Bell, Excogitation, Earthly Star, Horoscope, Macrocosmos), and actions with a lead of their own (Neutral Sect, Passage of Arms) | `hit − lead` when the action has a `lead`: Earthly Star 10 s (to mature), Neutral Sect 4.5 s (time to cast the Aspected Helios that puts up its barrier), Passage of Arms 2 s (pressed last: any other action ends its channel); otherwise a heal is timed like mitigation of the same duration, `hit − clamp(0.3·D, 2, 8)` (Bell −6, Excogitation −8, Macrocosmos −4.5, Horoscope −3), and other kinds keep their row below |
| `when: after` (healer LBs) | `hit + 2` for limit breaks, else `hit + 1` |
| party_mit, tank_mit, regen (duration D) | `hit − clamp(0.3·D, 2, 8)`; `hit − 3` when D is unknown |
| shield (GCD, cast time C) | `hit − max(4, C + 2.5)` |
| invuln | `hit − 2` |
| heal / instant heal | `hit + 1` (after the damage) |
| tank LB3 / healer LB3 (no `when`: by the role of the LB's jobs) | `hit − 3` / `hit + 2` |
| buff, movement, other, notes, actions not in the catalog | `hit − 3` |

Then, in this order:

1. **The mechanic and the other actions.** With `press: after` (§3.1), the press is
   `hit + max(default, 1)`: the calls are the heal-up after a hit that makes what was
   pressed before it pointless (TEA's Photons set HP to 1), mitigation and shields too; a
   healer LB keeps its +2, and no rule below applies. The reminder still turns due
   `dueLead` before the press (§5.2), so with the default 1.5 s a +1 press turns due 0.5 s
   before the hit (Photon: 262.0, about 0.8 s before its HP drop). Otherwise, with
   `heal_lead: L`, an entry timed by a heal used after the hit is pressed at `hit − L`
   (Double Rocket Punch: 4 s, to top up the tanks the second Photon left at 1 HP).
   Otherwise an entry timed by a heal used after the hit, or by a buff, movement or other
   action, takes the earliest default of its actions that are pressed before the hit, when
   that is earlier, and that action times it: "Bless/Whisper/Succor" is pressed with
   Whispering Dawn's −6.3 s, so that the Succor lands before the hit, and
   "Reci/Deploy/Soil/Fey Illum" with Fey Illumination's −6 s. An entry timed by mitigation,
   a shield, a regen or an invuln keeps its action's default.
2. **Own leads.** An item with a lead of its own (`when: before` with a `lead`, §3.3)
   pulls the press to `hit − lead` wherever the cell lists it, when that is earlier: "CU +
   Neutral Sect" is pressed with Neutral Sect's −4.5 s, time for its Helios, like "Neutral
   Sect + CU". No lead, a pulled one or the timing action's own, presses the entry earlier
   than `−(D − 2.5)` for another item with a lasting effect of D seconds (mitigation, a
   shield, a regen, an invuln, a tank LB, or a pre-placed heal's longest effect), which
   then, used on the NOW cue, still outlasts the hit by 1 s: "CU + Earthly Star" and
   "Earthly Star + CU" are pressed at −7.5 s beside the 10 s Collective Unconscious, and the
   star matures 2.5 s after the hit, for its heal; Neutral Sect keeps its 4.5 s beside
   anything that lasts 7 s or more. The action whose lead pulled the press then times the
   entry (its series floor, rule 4).
3. **Several hits.** An entry for the whole of a mechanic with several hits (no hit index,
   or the first) that is pressed before the hit also covers the later hits its mitigation
   can reach: its `hits` and its `sub_hits` (§3.1), damage that is no timeline entry (the
   four later cleaves of TOP's Diffuse Wave Cannon Kyrios, the five later Splashes of TEA's
   first). A party or tank mitigation or invuln item of duration D (its longest effect
   for `when: before`) covers the last later hit `hR` with `hR − hit ≤ D − 1.5`: it is
   pressed at `hR − D + 2`, but no later than 0.5 s before the first hit, and its NOW cue
   never comes before `hR − D + 0.5`, whatever the due lead (§5.2): a use on the NOW cue
   then outlasts `hR` by 0.5 s, and still comes at least 1 s before the first hit. Within
   `hR − hit ≤ D − 2.5` that press is the entry's, and the item that needs the latest press
   times the entry (Optical Sight, hits 6.1 s apart: a 10 s Reprisal at −1.9 s instead of
   −3 s; TEA's first Splash, its sixth hit 5.5 s after the first: a 10 s Reprisal, Feint or
   Addle at −2.5 s). An item that reaches a later hit only pressed 0.5 s before the first
   is pressed then on its own, while the entry's other items keep their press (TOP's P6
   Wave Cannon, the Wild Charge 8.3 s after the first protean: a 10 s Oblation at −0.5 s
   turns due 1.2 s before the first protean, and the Shadow Wall and The Blackest Night of
   its cell keep −4.5 s; a 10 s invuln on Diffuse Wave Cannon Kyrios, the fifth cleave
   8.3 s after the first, is pressed at −0.5 s and turns due 1.2 s before the first cleave
   too). Coverage presses no item later than the entry's other items allow: an item pressed
   before the hit that takes time to its effect (a cast, or the GCD a follow-up needs first)
   has it in place 1 s before the first hit, as `(Late)` does (§6 step 3: "Soil + Succor"
   −3 s at the latest, "Kerachole + Eukrasian Prognosis" −2 s, after Eukrasia's 1 s), and an
   item with a lead of its own that is no mitigation keeps it (Earthly Star 10 s, Neutral
   Sect 4.5 s). A later hit that would need a later press, the entry's or the item's own,
   is not covered ("Soil + Succor" on TOP's Pantokrator keeps the Succor's −4.5 s and
   covers the second line stack, not the third).
4. **Series.** When the hit is a later hit of a mechanic with `series: true` (on that
   mechanic with a hit index, or on one whose hit is that hit: `p3.j_wave_5` is the fifth
   J Wave), the press is no earlier than the hit before it + 0.3 s, minus the time the
   timing action's effect takes to land after the press: a shield's cast, or the cast of
   the GCD that puts up its barrier (`barrier_cast`, §3.3: Neutral Sect's 1.5 s Helios),
   so that the shield or barrier lands then; nor does the reminder turn due earlier
   (§5.2). A cell that lists an action that makes the next spell instant
   (`instant_next_cast`, §3.3: Swiftcast) counts no cast: "Swiftcast/Succor" on J Wave 5
   is pressed and due at 720.2, both items, so that the instant Succor is not spent on
   wave 4. The J Waves come 3 s apart, closer than most default leads: a shield pressed
   earlier would be spent on the wave before, and mitigation would end before the last,
   strongest waves it should cover. An item with a `barrier_cast` lowers the entry's floor
   to its own wherever the cell lists it (Neutral Sect beside Earthly Star, Temperance or
   Excogitation, which time the entry: on J Wave 5, Neutral Sect at 718.7, the other
   action at 720.2); a shield's cast does not. Each item also has the floor of its own
   action, never earlier than the entry's, and an automatic press presses it, and turns it
   due, no earlier: in "Succor/Soil/Seraph" on J Wave 5, timed by the Succor (a 2 s cast:
   pressed at 718.4, due at its floor, 718.2), the instant Sacred Soil and Consolation
   wait for J Wave 4, at 720.2, as they would alone. A buff, movement or other action
   without a lead of its own (Recitation, Emergency Tactics, Dissipation, Protraction,
   Swiftcast), a note and an action the catalog does not know have no effect the wave
   before could spend: they keep the entry's floor and come with the cast they change or
   go with ("Reci → Succor", "Emergency Tactics/Succor", "Stack middle → Succor": both
   items at 718.4, due at 718.2), but the trigger of a follow-up the entry lists waits with
   it (the Summon Seraph of "Succor/Soil/Seraph" with its Consolation, at 720.2). With an
   automatic press, a step of a sequence has no later floor than the step after it: in
   "Indom → Succor" the Indomitability, a heal that alone waits for wave 4 + 0.3 s, comes
   with the Succor at 718.4, so that the Succor's cast still lands between the waves.

Every item is pressed at the entry's press, except that with an automatic press an item
waits for its own series floor (rule 4) or for a later hit it reaches on its own (rule 3),
and a step of a sequence (`seq: true`) for the step before it, and that mitigation with a
lead of its own (Passage of Arms: any other action ends its channel) is pressed with the
entry's last item, turns due no earlier than any other item, and is listed after them, in
the overlay and in `mitcue plan`: "Passage of Arms + Reprisal" on Wave Cannon is pressed
0.5 s before the first protean, turns due with the Reprisal, whose NOW cue waits for the
Wild Charge, and is listed after it. A step of a sequence also turns due no earlier than
the step before it, whatever the press (§5.2). The entry's automatic press is the
earliest of its items' presses: the plan's order (§6 step 7) and the review (step 8) use
it, and the review lists the items pressed later.

A written press is every item's, and keeps each series floor it is at or after: its
reminders turn due no earlier, and a use before the floor is early (§5.2). One before the
entry's floor keeps none.

The importer writes no offset for these rules, so a plan's untimed entries follow any
change of them. Besides the sheet's times, it writes one only for a hint that names the
press (`after 1st Hit`, `after Solar Ray`: that hit + 0.5 s, §6 step 7), for a heal on one
player's hit of a multi-hit mechanic, pressed before that hit with the lead of the entry's
earliest action pressed before the hit, else 3 s before it (*NoirMit - WHM*'s `Solace +
Benison` for `your 3rd/4th-in-line player`: Program Loop's fourth set, −4 s), for
`(Late)` (§6 step 3) and where a step after the cells presses an entry later than they
do (`still-active`, step 4, and the recast fit, step 7).

### 5.2 States

- **Upcoming:** `press − lookahead ≤ now < due` (defaults: lookahead 15 s, dueLead 1.5 s).
- **Due (NOW):** from `due` until `latest`, where `due = press − dueLead` (the item's
  press, §5.1), but never before the item's due floor: its series floor when its press,
  automatic or written, is at or after it (§5.1 rule 4); with an automatic press,
  `hR − D + 0.5` for an item that covers a later hit `hR` (rule 3); its earliest use that
  is not early (`hit − D + 1` for its known duration D, below, never after the press) when
  the default due lead (1.5 s) would turn it due before then, so that a use on the NOW cue
  is not early, for an automatic press and for a written one at or after `hit − D + 1` (a
  written press before it asks for an early press and keeps `press − dueLead`); for
  mitigation with a lead of its own (Passage of Arms), the due floors of its entry's other
  items; and for a step of a sequence, the due floor of the step before it, up to its own
  press. On LPDU TOP's Cosmo Dive, The Blackest Night (7 s) beside a Rampart pressed at
  −6 s turns due 6 s before the hit, not 7.5 s, and the 8 s Bloodwhetting, Heart of
  Corundum or Holy Sheltron beside it 7 s before; at TEA's level 80, a 6 s Raw Intuition
  beside a Rampart at −6 s turns due at its press; *NoirMit - WHM*'s Sigma Temperance,
  written 18 s before the hit, turns due 19 s before it, not 19.5 s. The earliest-use
  floor counts the default due lead only: a longer due lead of the player's still turns
  the other items due that much earlier.
  `latest = hit − 0.3` for mitigation, shields and pre-placed heals (`when: before`), and
  `press + 5` for heals used after the damage and anything planned after the hit (never
  before `press + 0.5`). Sound plays once on entering Due. A press that the series floor
  sets turns due at the press itself, without the lead (TEA's Temperance for J Wave 5, 0.3 s
  after J Wave 4).
- **Done:** the action (or any alternative) was used between `earliest` and `latest`,
  with `earliest = hit − D + 1` for duration effects, else `press − 10`. A use before
  `earliest` is flagged *early*. For `when: before` actions D is the longest of their
  effects (Horoscope Helios 30 s, Neutral Sect's Helios barrier 30 s), so a pre-placed heal
  still active at the hit (Bell up to 19 s, Excogitation up to 44 s before it) is on time,
  not early. `earliest` is also no earlier than the item's due floor (above), but never
  after the press: a use before the series floor, or one that ends before `hR + 0.5`, is
  early. A step of a sequence is early only by its own window, not by the step before it.
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
   phase start)`), or label cells (`P2`). **As built (2026-11-14), for LPDU's TEA sheet:**
   `Phys Range` is a role column like `Caster` (calls for each job of the role), and a
   column headed `Extras`, `Other`, `Misc` or `Utility` holds calls for anyone, imported
   whichever columns are chosen. Its cells are read call by call: a call that names a job's
   own action is an entry for those jobs (`PLD Wings`, `Dismantle`), with no assignee; the
   cell's other calls, notes and actions several jobs share (`Feint`), are one note for
   everyone (`Dismantle + Feint`: the Machinist's Dismantle and the note "Feint"); a
   sequence with such a step stays one note for everyone, written with its arrows
   (`PLD Wings → Addle`: no entry for the Paladin). The cell's remark in parentheses, or in
   a cell below it after a caret, stays on every entry the cell splits into (`Dismantle +
   Feint (on BJ)`: the Machinist's Dismantle is on BJ too); a call with words of its own (an
   action's notes, a note's own parentheses or target) keeps them, and takes from the last
   call's parentheses only a timing or a condition and what a caret below the cell says
   (`Dismantle (on BJ) + Feint (on CC)`: the Dismantle is on BJ only; `Dismantle (on BJ) +
   Feint (late)`: late too). A phase title over the player columns starts its phase when a
   header row follows it (`P2: Brute Justice / Cruise Chaser`), or, as a merged banner
   without one, when it reads as a title: the fight's name for the phase, or at most four
   words with none of `,` `;` `.` `!` `?`, and so does its part in parentheses, unless that
   is a timing hint (`(from phase start)`). A merged banner of a later phase whose name
   reads as a title also starts that phase, whatever its part in parentheses holds, when
   the next labelled row is a mechanic of that phase (its label, simplified or a prefix of
   it, not a near miss) and none of the current phase's (TOP's `P2 (Party Synergy,
   Limitless Synergy)`; TEA's `P3: Alexander Prime (Inception, Wormhole, J Waves)` above
   Inception, not above `JKick`, which P2 has too). A merged remark that starts with a
   phase stays a note (`P1: MT invulns the second Wave Cannon, OT takes boss after`,
   `P1 (MT invulns …)`), and a title whose name, or part in parentheses other than a timing
   hint, is not the fight's name for its phase gets a `phase-title` info. An unlabelled
   column of prose beside the labels holds the rows' notes.
3. **Cell parsing.** Split into items: ` / ` separates independent actions (each may have
   its own time), `+` and `,` join simultaneous actions, `→`/`->` make a sequence, `X/Y`
   without spaces means either/or (often across jobs: `Kera/Soil`). Extract `(1:18)`,
   `~1:22`, `≈3:04`, ranges `1:47 – 1:56`, multi-times `0:28 / 0:37`, targets
   (`on MT`, `(your 3rd/4th-in-line player)`), hints (`after 1st Hit`, `on CD`), markers
   (`*`, `⏱`), and formatting flags (grey = optional). **Changed (2026-11-14):** what `X/Y`
   between two actions of one job means is read per document. It is either one
   (`TBN/Oblation`, next to `Living Dead + …` in LPDU's TOP sheet) unless the document
   joins actions no other way (`+`, `,`, `&`, `;`, `|`, arrows, ` / `, line breaks, gaps of
   three or more spaces) and lists several actions of one job with `/` in two or more
   cells, three or more in one: then it is all of them, like `+` (LPDU's TEA sheet:
   `Soil/Reci/Deploy/Whisper`). `ImportOptions.Slashes` (the import window's *Slashes*, the
   CLI's `--slashes auto|or|and`) overrides the reading, and the `slash-reading` info says
   which was used and why. Every tab's cells count, whichever are imported. "All of them"
   never joins at a `/` between slots, jobs, target or mechanic words (`MT/Co-tank`,
   `Stack/Spread`) or in `and/or`. In both readings, a `/` between two single letters is
   one word, spaced or not, which joins nothing (`N/A`, `N / A`); `MT/Co-tank` or `MT/OT`
   after an action is one target (either of them); other words joined by `/` after an
   action that each name a player or a part of a mechanic (slots, jobs, target words such
   as `tank` or `melee`, mechanic words such as `Stack`, `TB` or `Tower`) are its remark
   (`Kera Stack/Spread`: Kerachole, noted "Stack/Spread"; `Rampart MT/TB`); any other word
   after a `/` is read like an action: under "all of them" a call of its own, a note when
   it names no action (`Kera Left/Right`: Kerachole and a note item "Right", noted "Kera
   Left", the call's action words and its remark, as each call of a cell of several calls
   is noted), under "either one" an alternative, left out with a warning when it names
   none (`Kera Left/Right`: Kerachole, noted "Left"; `Kera Left/Right + Holos`: noted
   "Kera Left"); and `X and/or Y` is either one. Also read since then: a cell that is one
   pair of brackets (`(Shake)`: optional), a cell that points up (`(^on BJ)`: a remark on
   every item of the cell above) and `(Late)`: an entry pressed before its hit (not one on
   a mechanic with `press: after`, §5.1) gets the offset that presses it just before it, so
   that its longest cast, or the GCD a follow-up needs first, ends 1 s before the hit
   (`Reprisal (Late)` −1 s, `Succor (Late)` −3 s, `Eukrasian Prognosis (Late)` and
   `Eukrasian Diagnosis (Late)` −2 s after Eukrasia's 1 s, §3.3), but only when that presses
   no item earlier than without it (`Rampart + Reprisal (Late)` on TOP's Wave Cannon 2
   keeps the Reprisal's −0.5 s, for the Wild Charge, and the Rampart's −6 s); the offset is
   written into the plan, unlike §5.1's automatic times. A target in parentheses goes only
   to actions used on a player; elsewhere it stays in the note (`Asylum (melee)`: where to
   place it). The full grammar: `src/MitCue.Core/Import/README.md`.
4. **Action resolution.** Alias dictionary → exact name → fuzzy match (typos such as
   *Nascant*, *Vengance*), restricted to the assignee's job and the fight's sync level.
   Cross-job alternatives are split per job. Role phrases expand per job. Anything else
   becomes a note. **As built (2026-11-14):** a sheet that numbers its Seraph calls
   (`Seraph1`: Summon Seraph → Consolation) means the same by a plain `Seraph` (LPDU's TEA
   sheet; a `seraph-reading` info says so); elsewhere a plain `Seraph` is Summon Seraph
   alone. After the cells, an action the same player uses on the party, an area or
   themselves (a party buff or party mitigation such as Temperance, Sacred Soil or Summon
   Seraph, or a tank's own Rampart; one charge at the level, a recast longer than its
   effect), planned again from another row less than its own duration after the earlier
   press (item press to item press: Temperance, Neutral Sect and Rampart 20 s, Summon
   Seraph 22 s), while the earlier press still serves the later call, becomes its
   follow-up (Summon Seraph → Consolation; at level 100 Temperance → Divine Caress,
   Neutral Sect → Sun Sign) or, for an action without one, a note ("Temperance (still
   active)", "Rampart (still active)"). Both calls are timed as the reminders time them
   (§5.1), each at its own item's press, used on that item's NOW cue (§5.2, the default due
   lead). A note needs the earlier effect, lasting as long as the reminders count it
   covering a hit (a `when: before` action's longest effect: Neutral Sect's 30 s barrier),
   to outlast the later hit, or the later press when that comes after the hit, by 1 s. A
   follow-up needs the action only when it is used: it is pressed after the earlier press
   (a `Seraph` 2 s after a `Seraph (Late)` stays a second summon, as its Consolation would
   come before that Summon Seraph), within `check`'s follow-up window after the earlier
   press's NOW cue, the action's longest effect (Seraph's 22 s: LPDU TEA's plain "Seraph"
   at J Wave 7, 6 s after the one at J Wave 5, is its Consolation alone; at level 100
   Divine Grace's and Suntouched's 30 s). An earlier press without a time in the sheet
   that falls short gets an offset that presses it later, so that it serves both (LPDU
   TEA's Hidden Mines Temperance: `offset: -3.5`, through Water 2), no later than `(Late)`
   would press it, never after the lead of a `when: before` action that is no heal (Neutral
   Sect 4.5 s, Passage of Arms 2 s), never so that another of its items is pressed earlier
   than without it, and never so that a seat that sees it loses what the recast fit keeps
   for it (step 7: a Passage of Arms of another cell stays the last press before its hit,
   a follow-up stays in its trigger's window); like `(Late)`'s, the offset is written into
   the plan, so the review and `mitcue plan` show it as set, not automatic. Otherwise both
   stay presses, and `check` reports the recast. A `still-active` info says which, with the
   gap between the two items' presses ("… but its follow-up Consolation would be used
   after it is over …: both stay presses"; "… and pressed later it would come after the
   Passage of Arms at …, which is pressed last: both stay presses"; a move ends "… so that
   it still lasts when Consolation is used"). After each round of the recast fit (step 7),
   a call after a press the fit moved that this press now serves is read as still active
   too, its info in place of the one that kept it a press (an `Asylum (Late)` at Double
   Rocket Punch, 18.8 s after a Gavel Asylum the fit presses 1.5 s before its hit), unless
   a later call was read as still active from that call's own press: the moved press need
   not last through that one, so both stay presses ("… as the recast fit presses that one
   1.5 s before its hit, but the Asylum at … is read as still active from this one: both
   stay presses"), and `check` reports the recast. A call planned again at or after
   the action's own duration stays a press without an info, whatever the barrier or the
   follow-up window: a Neutral Sect named again 20–30 s later is no note, and at level 100
   a Temperance or Neutral Sect then is no Divine Caress or Sun Sign. An optional earlier
   press (grey, in brackets, with a condition, or one choice of an either/or) keeps no buff
   up. A limit break written for one role in several seats' columns at one mechanic is
   optional in each, also when only some of those columns are imported. A target taken
   from the row (its notes, or "tank" on a tankbuster) goes to
   single-target actions only, and the item's other actions get an entry of their own. A
   note over several columns that names actions and speaks to no other player ("A tip for
   Soil/Kera timing …") is the detail of the entries in its columns and section that press
   one of them; one that speaks to players by role, slot or job ("Tanks: …, then OT
   provokes …") is the detail of all their entries, and a note entry of its own in a column
   with no entry there. A role or slot word that says where or for whom to press speaks to
   no one ("on the melee", "under the tanks", "on melee and ranged", "on the MT/OT", "under
   the tanks/healers", "on the co-tank", "the tank buster", "the OT's buster": a `/`, `&`
   or `+` between such words lists them as "or" does), unless a colon follows it and no
   place word but "for" or "to" places it: then it heads the note, as in "Tanks: …"
   ("For the tanks: …", "Tip for melee: …", "To the melee: …", "For the MT and OT: …";
   "Soil on the melee: …" and "WHM on the tank: …" place the actions). Nor does the job
   whose own action the note names ("SCH: … press Soil"); a job named beside a word for
   several jobs' actions (Rampart, "Invuln") does: "WAR Rampart the first tower set, then
   PLD provokes …" speaks to the PLD. Either note reads the same whichever of its columns
   are imported.
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
   Otherwise the offset stays automatic (§5.1); parsed hints adjust the anchor (e.g. after
   the 1st hit), and one that names the press writes it (`after 1st Hit`: that hit + 0.5 s;
   `after Solar Ray`: the mechanic's last hit + 0.5 s, stored as `hit_index`). Items
   described as happening at a phase's end are anchored to that phase's enrage/end
   mechanic and flagged `at_phase_end` (§5.2). **As built:** δ per sheet and
   mechanic is the fight file's `row_offsets` (§3.1). For Carry the Clear they are Blue
   Screen +1.0 s, the Omega row +1.1 s, Blind Faith −1.2 s and the P6 enrage −14.0 s; P2
   Cosmo Memory and the Hello World rounds need none, because the catalog anchors them at
   the cast end too. A timed item counts from the hit it belongs to (the first hit at or
   after its press; for heals the last one at or before it), stored as `hit_index`.
   **As built (2026-11-15): the recast fit** (a `recast-fit` info), after step 4. An entry
   without a time in the sheet (none in the cell, no hint that names its press) that is
   pressed before its hit, and one of whose actions would find no charge at its press, as
   `check` replays the plan (`RecastReplay`: recasts and charges at the sync level, one
   charge recharging at a time, recast groups, recasts under 3 s left out; each item at its
   own press, §5.1) for any seat (every job, in each slot of its role and in none, with the
   column the plugin suggests without a party and with each party that changes it: the jobs
   of a team-composition column, and every two-job party), is pressed when that action is
   ready again plus the default due lead (1.5 s), rounded up to 0.1 s, so that its NOW cue
   comes when the action is ready: LPDU TEA's Gavel Asylum, planned 85.8 s after the
   Asylum pressed after Photon (recast 90 s), is pressed 1.5 s before its hit instead of
   7.2 s. Never later than `(Late)` would press it (step 3) nor than an action's own lead
   allows (Neutral Sect 4.5 s, Earthly Star 10 s, Passage of Arms 2 s): then just after the
   action is ready (the J Wave 7 Plenary, 1 s before its wave). The press stays, and
   `check` reports the recast, when the action is ready only later than that, when the
   move would press another item of the entry earlier than without it (an item that waits
   for a later hit or a series floor of its own, or Passage of Arms with the last item),
   and when a seat that sees the entry needs it where it is (the move is every seat's): a
   follow-up of the seat, the entry's own or one whose trigger the entry presses, would
   leave a window after its trigger that it is in now, the trigger's longest effect counted
   from its press, as `check` counts it, or from its NOW cue, as step 4 reads it (a
   Consolation more than 22 s after the Summon Seraph before it, or before it). Nor is it
   pressed at or after a Passage of Arms of another cell that a seat sees with it and that
   is not before it now, up to that Passage of Arms' hit, whichever hit the entry is for
   (TEA's Hand of Pain 4 hits 0.1 s before Protean 2): once nothing else moves (that
   Passage of Arms may wait for its own recast first), it is pressed 0.1 s before the
   Passage of Arms, for every seat, if its action is ready by then (in LPDU TEA's layout,
   a Tank 1 Reprisal beside `PLD Wings` in Extras: 2.1 s before the hit at the latest, for
   every MT, a Warrior too); else it stays. Only firm presses keep an action on
   recast: not an optional entry (grey, in brackets, with a condition, one choice of an
   either/or), an either/or item or a note; an optional call itself still waits (LPDU
   TEA's "CU (if up)" on Irresistible Grace 1). A press with a time in the sheet counts but
   never moves; an offset the importer wrote itself (step 4's) may move. A when-ready call
   ("on CD") keeps its press and still counts: its reminder already waits for the action in
   game. A sequence moves for its first step only (`check` times every step at its own
   press, §5.1: the entry's, unless the step waits for a series floor or a later hit of its
   own; in game the later steps follow the first), and only when its later steps are
   instant oGCDs woven in right after it (`Kerachole → Holos`): one with a later GCD or
   cast (`Zoe → Eukrasian Prognosis → Holos → Kerachole`, `Recitation → Succor`) keeps its
   press. An entry several seats see takes the latest press any of them needs. The
   earliest press without a charge moves first, then the plan is replayed again, until
   nothing moves; a moved press is replayed at its press time, so in a chain of moves each
   link keeps its due lead. The offset is written into the plan: `mitcue plan` shows it
   without `*`, and the review as set, not automatic. The info gives the gap between the
   two items' presses before the fit, with "which is pressed later too" when the earlier
   press moved as well, and "just before the Passage of Arms at …, which is pressed last"
   when one stops it. After each round, step 4 looks again at the calls after the presses
   moved so far. In the bundled plans the fit moves LPDU TEA's Gavel Asylum
   (−1.5 s), J Wave 7 Plenary (−1 s), Water 2 Sacred Soil (−2.4 s), the Sage's Protean 2
   "EukProg/Kerachole" (−2 s, the `(Late)` bound, when Kerachole is ready again) and
   Irresistible Grace 1 "CU (if up)" (−1 s), and LPDU TOP's M2 Feint at Omega (−1 s).
   The plan lists its entries in press order, as §5.1 times them (ties in sheet order),
   also when step 4 changed an entry's actions or the recast fit moved its press, and
   numbers them `e001`, `e002` … in that order: a change of the timing rules renumbers a
   plan imported again, as it did the bundled LPDU plans' entries (2026-11-14 and
   2026-11-15). No setting refers to an entry id, so the renumbering changes nothing for
   players.
8. **Review.** Every entry with a confidence level and diagnostics; the user fixes items,
   fixes become personal aliases; save. **As built** (the import window, PLUGIN.md §1.7):
   each entry with the importer's diagnostics and the plugin's checks, and the automatic
   offset and press time the overlay will use (§5.1), with the press of each item pressed
   later than the entry ("Pressed later: …"); an offset the importer wrote (`(Late)`, step
   4, the recast fit) shows as set, not automatic, step 4's and the fit's with their info.
   The user can change the mechanic, hit, offset, flags, target, note and items, and delete
   entries; replacing a note with an action offers to save the shorthand as a personal
   alias; the plan is saved as a user plan, made the active plan when *Make it my active
   plan …* is ticked (it is by default when calls of the player's apply, counted as the
   overlay will select them in the player's party; notes for everyone do not count), with
   the column choice that has all of the player's calls when one has them without another
   column's (otherwise the Save step names the columns of the calls left out).

LLM fallback: the plugin generates a prompt containing the plan schema, the fight's
mechanic catalog and the job's alias list; the user runs it with their source text in any
LLM and pastes the YAML back, which goes through the same validation and review. **As
built:** the prompt also lists the chosen jobs' actions usable at the fight's sync level,
and holds the loaded sheet's text when one is loaded; the YAML is read out of the answer
(text around its code block is ignored). Its rule for `X/Y` between one job's actions is
the importer's per-document reading (step 3), and with the sheet's text it says which
reading MitCue's importer uses for that sheet: the import options' *Slashes* choice, else
the sheet's own. When the prompt holds the sheet's text, the trial import found that the
sheet numbers its Seraph calls (`ImportResult.SeraphNumbered`) and the prompt converts the
Scholar's calls (or every job's), it also states the per-document Seraph reading (step 4):
a plain `Seraph` there is `Seraph1`, Summon Seraph then Consolation, and one while the
Seraph summoned before is still out (22 s) is `Seraph2`, the other Consolation; its alias
list alone reads a plain `Seraph` as Summon Seraph.

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
  name that names the job in a column or entry (a share code of a bundled plan counts as
  that plan), else the player's first own plan by name that names it. TEA's only bundled
  plan, *LPDU TEA Heal/Mit*, is thus the automatic one of every job it names, with no entry
  in `PlanStore.BundledDefaults`, and a TEA plan the player imported without choosing it
  gives way to it while it is bundled; DMU ships no plan. A plan whose calls for the
  player's seat are only in job-less slot columns (LPDU's M1/M2 for melee jobs, and the TEA
  plan's "Melee 1"/"Melee 2" for melee jobs other than the Monk, whose Mantra calls name
  it) is not chosen automatically: the player chooses it in the table. An imported plan or
  a share code replaces a bundled plan only when the player chooses it: in the table, or
  with the "make it my active plan" checkbox of the import window (ticked when calls of the
  player's apply) or of the share-code box (unticked each time the plugin loads). The
  exception is the share code of a bundled plan: it has that plan's id and takes its place
  until it is deleted, also in the automatic choice (`PlanStore` remembers the bundled
  ids): a copy of the TEA plan stays the automatic plan of every job it names. The
  share-code box sets a plan only when it has calls for the job as the overlay selects
  them, not counting notes for everyone (entries with no job, slot or column, such as the
  TEA plan's "Use Personals": `PlanSelection.ForEveryone`); for a role sheet whose calls
  for the job are only in other slots' job-less columns, the message names those slots and
  says to choose the plan and then the slot's column under "My column" (or, only while no
  usual slot is set at all, to set it first; a usual slot of another role gets the column
  hint too): one usual slot serves every job of its role, so changing it would take the
  slot calls of the player's main job away. **Changed (2026-11-14):** settings version 2.
  On the first load of version 1 settings (MitCue up to 0.1.0.6) that reads every plan
  file, every fight and job without a choice whose automatic plan differs from the one
  version 1 gave it is set to the version 1 plan (`PlanStore.MigrateFromVersion1`): above
  all the player's own TEA plan, the first by name that names the job, which the bundled
  TEA plan would otherwise replace unasked. While a plan file cannot be read (another
  program holds it, its cloud folder is offline, a link's target is gone) or the plans
  folder cannot be listed, the update waits: nothing is chosen or saved, the file is listed
  under Problems, and every load (a start, "Reload plans") tries again. Meanwhile each job
  keeps the plan version 1 gave it from the plans that can be read, so the bundled TEA plan
  goes only to jobs that had none. A file that reads but is no valid plan does not hold the
  update up.
  "My column" without a choice is the job's column, which also shows the job-less calls
  of the player's slot (LPDU's H2 Scholar gets "H2 SCH" with the job-less "H2" calls of
  P1/P2), or, when the job's calls are in several of its columns (one per phase or tab),
  the narrowest choice that has them all and no call of another column
  (`ColumnSuggestion`). For the column and the calls, the usual party slot counts only on a
  job of its role. Without a column for the job and a slot (LPDU's ranged jobs, with calls
  per job in "R1" and "R2"), the job's calls give the seat: the first slot of its role they
  name, passing over a slot the party gives another job, one whose calls are all in it (a
  Red Mage gets R1, or R2 next to a Bard; on the TEA plan, with calls per job in the seats
  "Tank 1" (MT) and "Tank 2" (OT), every tank gets MT, while the calls of the role columns
  "Phys Range" and "Caster" name no slot, so their jobs get them all). When the seat is a
  guess like this, between slots of the role that the party does not settle, or the choice
  gives no slot while the plan has calls for whoever plays a slot of the role (the TEA
  Monk's seat's Feints), the choice stays, and the status line, chat (once per plan and
  job each time the plugin loads), the preview and the Plans tab say so and name the
  setting that gives the seat: "My usual party slot", or the slot's column under "My
  column" (`ColumnSuggestion.SeatUnknown`). Columns for team compositions (keys naming two
  or more jobs) are alternatives, never combined, and one composition split over per-phase
  tabs is one seat: a Warrior on LPDU gets the tank pair with the party's co-tank
  ("WAR/PLD WAR" next to a Paladin), else the first, "WAR/DRK WAR"; a party that plays none
  of the job's compositions gets the job's column that is no composition, if there is one
  (Carry the Clear's White Mage tab for a White Mage next to a Sage). A job's column that
  repeats the calls of its composition column is the same seat written twice, whichever tab
  comes first (Carry the Clear's White Mage tab and its combined tab). Calls in no column
  (the TEA plan's "Extras": "PLD Wings", "Dismantle") belong to the jobs they name,
  whatever the column. Plans are exported as share codes, not as files.
- **Co-healer's calls** (as built): the plan's other columns of the player's role, except
  the player's own seat in another composition and the partner's columns for compositions
  without the player's job (a Warrior sees "WAR/DRK DRK", not "DRK/GNB DRK"), and in a
  party the columns of jobs nobody in it plays and a job's calls in a slot the party
  gives another job (LPDU's Red Mage R1 calls next to a Bard). The partner's columns
  that are not a composition are kept (a general "DRK" next to "WAR/DRK DRK"); a
  combined-tab column that repeats the partner's other column is shown once. Columns for
  a role without a seat (LPDU's "CM LB Tank" and "CM Rep Tank") are no one's: not shown,
  unless the player follows one of them, and then the other one is the co-tank's; but a
  job without a column of its own has its calls there as its seat (the TEA plan's "Phys
  Range" and "Caster": a DPS sees the other ranged jobs' calls).
- **Preview/simulation:** run the fight timeline with simulated events at 1×/4× from any
  phase to check the overlay and the plan. **As built:** only the event kinds the plugin
  sees in game are played, so a preview syncs like a pull, and it shows the calls the
  live session would show in the player's party.
- **Data loading** (as built): fight data and plans load on the first framework tick;
  **Reload data** (Debug tab) reloads the fight data on the next tick. The live session
  keeps the data it was built with until it is rebuilt (`/mitcue reset`, or a change of
  zone, job, plan or column; a change of plan or column waits while a pull, the hold after
  a countdown, or a countdown with a call already shown is on: PLUGIN.md §1.5).

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

- `tests/MitCue.Core.Tests`: the above, with LPDU's TEA sheet as a third fixture (every
  row mapped to its mechanic), plus the bundled plans: each is the importer's output for
  its sheet fixture, lists its entries in the scheduler's press order and is valid at its
  fight's sync level (90 for TOP, 80 for TEA); the timing rules (§5.1) are checked at the
  TEA and LPDU TOP plans' mechanics, and the recast fit (§6 step 7) and its replay of
  `check` (`RecastReplay`) on test cases and on the bundled plans' moves.
- `tests/MitCue.Plugin.Tests`: sessions and pulls, the plan store with the automatic plan
  and column choices on the bundled plans, the co-healer's calls, the import pipeline
  (link parsing, downloads through a fake HTTP handler, pasted cells, the AI prompt and
  YAML), the review model, background jobs, and an invariant sweep (`Invariants/`): for
  each job, usual slot and party, the overlay's calls are the player's seat's, once each,
  the co-healer rows are the partners' only, and the Plans tab, the import window, the
  preview and the CLI show the overlay's calls (the project references `tools/MitCue.Cli`
  for it). Built against Dalamud's assemblies; no game needed.
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
   DMU fight data ship as first drafts; TEA's is mapped onto LPDU's TEA sheet, which ships
   as *LPDU TEA Heal/Mit* (D7), and DMU has no plan yet; the recast and gauge checks are in
   the CLI (`mitcue check`), and the plugin checks plans that skip the review against the
   fight and the action data.
