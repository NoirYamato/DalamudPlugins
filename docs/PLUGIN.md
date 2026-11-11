# MitCue plugin: how it works and how to test it

This page describes the Dalamud side of MitCue (`src/MitCue`) as it is implemented, and gives the in-game
test checklist. The requirements and the core logic (plan format, sync engine, timing rules) are in
[DESIGN.md](DESIGN.md). Installing is covered in [INSTALL.md](INSTALL.md), and everyday use in the
[README](../README.md).

- [1. Runtime architecture](#1-runtime-architecture)
- [2. In-game test checklist](#2-in-game-test-checklist)

---

## 1. Runtime architecture

### 1.1 Overview

```
 game client ──────────────────────────────────────────────────────────────────────────────────────────────
   enemy cast bars     ActionEffect packets      InCombat     duty wipe / recommence /     /countdown agent
   (object table)      (hook, read-only)         condition    complete, zone change        ActionManager, gauges
        │                     │                      │                   │                         │
        ▼                     ▼                      ▼                   ▼                         │
 ┌─ GameEventSource (Game/) ────────────────────────────────────────────────────────────┐          │
 │  CastStarted · AbilityResolved · CombatStateChanged · DutyReset · countdown          │          │
 │  + own action uses (you and your pet) · other players' uses                          │          │
 └──────────────────────────────┬───────────────────────────────────────────────────────┘          │
                                │ FrameContext (once per framework tick)        CooldownProvider ◄─┘
          ┌─────────────────────┼─────────────────────────┐   (recast, charges, Lily/Aetherflow…,
          ▼                     ▼                         ▼    usage fallback)
   PullRecorder          SessionManager             PreviewDriver
   (.jsonl, optional)    live EncounterSession      simulated EncounterSession (FightSimulator)
                         ┌────────────────────────────────────────────────────┐
                         │ EncounterSession = TimelinePlayer (cactbot sync)   │  MitCue.Core
                         │   → timeline time → ReminderScheduler → Reminder[] │  (no Dalamud)
                         └────────────────────────────────────────────────────┘
                                │                         │
                                ▼                         ▼
                         OverlayModel.Build (snapshot, framework thread) ──► OverlayWindow.Draw (UiBuilder.Draw)
                         chat summary · sound (<se.N>)                      ConfigWindow (General/Overlay/Plans/Debug/About)
```

Everything runs on the game's main thread, either in `IFramework.Update` or in Draw. The two exceptions are
the ActionEffect hook detour and Dalamud's duty and territory events: both only put data into a concurrent
queue, which the next framework tick empties.

### 1.2 Components

| Component | File | Responsibility |
|-----------|------|----------------|
| `Plugin` | `Plugin.cs` | Entry point (`IDalamudPlugin`). Gets Dalamud services through `[PluginService]`, creates every component, registers `/mitcue`, runs the per-frame `Tick` and hosts the windows |
| `Configuration` | `Configuration.cs` | Settings, saved by Dalamud as `pluginConfigs/MitCue.json`. `Sanitize()` clamps values after loading. Collections use `ObjectCreationHandling.Replace`. Every change bumps `Revision`, and the runtime watches that number |
| `FrameContext` | `Runtime/FrameContext.cs` | Game state sampled once per tick: logged in, territory, client language, local entity id, job, effective (synced) level from `IPlayerState`, InCombat, bound by duty, countdown, this frame's events and uses |
| `GameEventSource` | `Game/GameEventSource.cs` | Turns game state into core `GameEvent`s stamped with MitCue's monotonic clock (§1.4) |
| `ActionEffectHook` | `Game/ActionEffectHook.cs` | Read-only hook on `ActionEffectHandler.Receive`. Copies the caster, action, targets and sequence, then always calls the original |
| `CooldownProvider` | `Game/CooldownProvider.cs` | `ICooldownProvider` for the scheduler: seconds until an action is ready (`ActionManager`, including upgraded actions), job resources from the gauges (Lily, Blood Lily, Aetherflow, Addersgall, Addersting), and the usage fallback (§1.5) |
| `OwnUse`, `OwnUseDeduper`, `PetActions` | `Game/` | Detected action uses. Merges duplicates found by different paths. Maps fairy and Seraph actions to the Scholar command that triggered them |
| `PullRecorder` | `Game/PullRecorder.cs` | Optional JSON-lines log of every event, one file per zone visit (§2.6) |
| `SoundPlayer` | `Game/SoundPlayer.cs` | Plays chat sound effect `<se.N>` through `UIGlobals.PlaySoundEffect` |
| `CoreData` | `Data/CoreData.cs` | Loads the fight library (`data/fights`, cactbot timelines), action catalog and alias dictionary embedded in `MitCue.Core.dll`, and adds the personal aliases. Loading is lazy and every call is guarded |
| `GameData` | `Data/GameData.cs` | Lumina lookups: action names in the client language, icons, the GCD flag, job abbreviations, zone names (cached) |
| `PlanStore` | `Plans/PlanStore.cs` | Bundled plans (embedded, read-only) plus user plans (`pluginConfigs/MitCue/plans/*.json`). Works out the active plan per fight and job (the user's choice, otherwise the first plan with a column for the job) and "my column" (the saved choice, otherwise a suggestion from job and slot). Imports and exports share codes |
| `SessionManager` | `Runtime/SessionManager.cs` | Owns the live `EncounterSession`: builds it, rebuilds it when something changes, feeds it every frame, prints the pull summary and asks for the sound |
| `EncounterSession` | `Runtime/EncounterSession.cs` | One fight, plan and player: a core `TimelinePlayer` plus a `ReminderScheduler` for your entries and an optional one for other columns. Handles the countdown, pull start and pull end |
| `PreviewDriver` | `Runtime/PreviewDriver.cs` | `/mitcue preview`: the core `FightSimulator` produces a pull, which plays through a separate `EncounterSession` |
| `OverlayModel`, `ReminderText` | `Runtime/` | The overlay snapshot built each tick, with display strings cached per reminder |
| `OverlayWindow`, `ConfigWindow`, `DebugReport` | `UI/` | The overlay, the settings window and the Debug tab text |
| `ErrorReporter` | `Util/ErrorReporter.cs` | Collects problems. Each distinct problem is logged once to Dalamud's log and listed under **Problems** and in the debug report |

### 1.3 One framework tick

`Plugin.Tick` runs once per `IFramework.Update`, inside a try/catch:

1. **Sample** the game state into `FrameContext`. A zone change clears the use de-duplicator and the cooldown
   tracking.
2. **Events.** `GameEventSource.Poll` collects this frame's events in time order. Cast polling and ActionEffect
   capture only run while something needs them: a live session, a preview, or **Record pulls**.
3. **Own uses.** It takes the ActionEffect uses first, then the cooldown fallback for the actions the live
   session and the preview track. A use counts once: the same action within 1 s, or within 5 s for pet
   actions, is a duplicate.
4. **Recorder.** If recording is on, the frame's events, own uses and countdown changes are written.
5. **Live session.** `SessionManager.Update` feeds the events and uses to the session, rebuilds it if needed,
   and advances the reminders to now.
6. **Preview.** `PreviewDriver.Update` plays the simulated events up to the simulated clock.
7. **Overlay snapshot.** `OverlayModel.Build` picks what to show: the preview first, then the live session,
   then the sample rows (with **Always show**), otherwise nothing.
8. **Sound.** If a reminder turned NOW this frame, or **Test** was clicked, the sound plays.

`UiBuilder.Draw` then draws the windows from the snapshot. Icons are fetched every frame with
`ITextureProvider.GetFromGameIcon(...).GetWrapOrEmpty()` and never cached.

### 1.4 Game events

| Event | Source | Details |
|-------|--------|---------|
| `CombatStateChanged` | `ICondition[InCombat]` | Emitted on changes only. The state at plugin load is taken as is, without inventing a combat start |
| `CastStarted` | Object table slots 0–199, hostile battle NPCs (not pets, chocobos, trusts or minions) | Emitted once per cast. The time is moved back by the cast time already elapsed, so it matches the cast start. Carries the NPC name, total cast time and target |
| `AbilityResolved` | ActionEffect hook | Queued by the detour (at most 4096 waiting), emptied every tick, de-duplicated by the server sequence number. The caster is classified as player, a player's pet, or NPC. NPC names are filled in on the framework thread |
| `DutyReset` | `IDutyState.DutyWiped` / `DutyRecommenced` / `DutyCompleted`, `IClientState.TerritoryChanged` | Ends the pull. The reason (*wipe*, *duty recommenced*, *cleared*, *left the zone*) appears in the chat summary |
| countdown | `AgentCountDownSettingDialog` (`Active`, `TimeRemaining`) | Read every tick. No chat parsing |

The core `TimelinePlayer` matches these against the fight's cactbot timeline syncs (`StartsUsing`, `Ability`,
`InCombat`). It matches by ability ID always, and by source name only when the client is English, because
cactbot's names are English. When an event matches a sync inside that sync's window, the fight clock snaps
to it, or jumps for phase pushes. In TOP, the phases are entered through these syncs in
`data/timelines/the_omega_protocol.txt`:

| Phase | Sync | Timeline time |
|-------|------|---------------|
| P1 | `InCombat` | 0.0 |
| P2 | `StartsUsing 7B40` (Firewall cast). The real P2 start is a `MapEffect`, which v1 does not use | 203.4 (window 210 s back, 5 s ahead) |
| P3 | `Ability 7B13` | 400.0 |
| P4 | `Ability 7B47` | 600.0 |
| P5 | `Ability 7B7C` (the Blue Screen hit) | 700.0 |
| P6 | `Ability 7F72` (the Blind Faith hit) | 1100.0 |

### 1.5 Sessions, pulls and reminders

**When there is a live session.** You must be logged in, in a zone that has a fight definition (for now
TOP, territory 1122), on a combat job, and there must be an active plan for that fight and job. Otherwise the
Status line says what is missing.

**Rebuilds.** The session is rebuilt when the zone, job, level, plan, settings, data or client language
changes:

- a change of zone, job or login rebuilds it straight away;
- other changes wait until the current countdown or pull is over;
- the timing sliders (*Show reminders this early*, *Turn "NOW" this early*) and *Show skipped* apply
  immediately, even in the middle of a pull.

**Countdown.** While `/countdown` runs, the session uses a timeline time of minus the time left, so pre-pull
entries such as shields count down too. A restarted countdown starts again from a clean state. A countdown
stopped with more than 1 s left counts as cancelled and clears everything. When the countdown reaches zero,
the pre-pull reminders wait at time 0 for up to 20 s for the pull to start.

**Pull.** The pull starts when the timeline starts, at the `InCombat` sync. It ends on any of these:

- a `DutyReset` event;
- the timeline has stopped and you are out of combat (*combat ended*);
- more than 10 s out of combat while the timeline still runs, with no enemy cast or ability for 10 s
  either. Enemy activity keeps the pull alive while you are dead.

At the end, if anything was done, missed or skipped, the summary goes to chat when **Print a done/missed
summary** is on. Then the session rewinds for the next pull.

**Reminders.** The core `ReminderScheduler` turns your plan entries into reminders: Pending, then Upcoming,
then Due (NOW), and finally Done, Missed, Skipped, or Passed for notes. The timing rules are in
[DESIGN.md](DESIGN.md) §5. Done, missed and skipped rows stay visible for 3 s. At the lookahead time the
cooldown provider is asked whether the action will be ready, which produces *ready in N s* and *no Lily*.

**Recognising your presses.**

1. **ActionEffect:** you are the caster. Your pet counts as well, so the fairy's Whispering Dawn (803) and
   Seraph's Angel's Whisper (16550) both count as the Scholar command Whispering Dawn (16537). The same
   mapping covers Fey Illumination, Fey Blessing and Consolation.
2. **Cooldown fallback:** for the actions of the current reminders (GCDs excluded), a recast that starts, an
   elapsed time that resets, or a charge that drops counts as a use. This covers anything the first path
   misses.

Other players' uses only mark the dimmed rows of other columns.

**Errors.** Every call into MitCue.Core and every game read is guarded. A failure is listed under **Problems**
and does not break the frame. If the live session itself throws, it stops ("Stopped after an error … Use
/mitcue reset to retry.") until `/mitcue reset` or the next rebuild.

### 1.6 Preview

`PreviewDriver.Start(fight, phase)` checks the fight, phase, timeline and plan, then generates a pull with
`FightSimulator`: the cactbot timeline, with the casts and abilities that satisfy its syncs. Then:

- **Clock.** The pull happens at simulated clock 1000. A whole-fight preview starts 10 s earlier, with a
  simulated countdown. A phase preview drops the earlier phases' events, so the phase's own sync jumps the
  timeline, and starts 2 s before the phase.
- **Separate session.** The preview has its own `EncounterSession`, with English name matching, because the
  simulated names come from the timeline. Simulated events never reach the live session.
- **Your presses.** Your real action uses are forwarded at the simulated time, so you can test usage tracking
  on a striking dummy. The cooldown warnings are off in previews.
- **End.** It stops 5 s after the last event once the timeline has stopped, or 60 s after the last event at
  the latest. Then it prints *Preview finished.* and the summary.

### 1.7 Safety and threading rules

- **Reminder-only.** Nothing in `src/` uses actions, sends chat commands or targets. `IChatGui.Print` only
  writes to your own chat log. The ActionEffect detour only reads the packet, copies what it needs first, and
  always calls the original.
- **Constructor.** The constructor runs off the main thread, so it touches neither the object table nor
  textures.
- **No game objects kept.** No `IGameObject` survives from one frame to the next; only entity IDs are stored,
  because the object table reuses its wrapper objects.
- **Dev mode.** Running as a dev plugin turns on Dalamud's dev-mode main-thread checks. A violation shows as a
  Dalamud error, which is worth reporting (Dalamud plans to make these checks mandatory in API 16).

### 1.8 Data, files and packaging

- **Embedded data.** `MitCue.Core.dll` contains `data/**` (fight catalogs, cactbot timelines, action data,
  aliases) as `MitCue.data/...`, and `plans/**/*.yaml` (bundled plans) as `MitCue.plans/...`. **Reload data**
  in the Debug tab reloads them.
- **Files.** Settings are in `pluginConfigs/MitCue.json`. User plans are in `pluginConfigs/MitCue/plans/`.
  Recordings are in `pluginConfigs/MitCue/recordings/`. See [INSTALL.md](INSTALL.md#where-mitcue-keeps-its-files).
- **Package.** `src/MitCue/MitCue.csproj` uses `Dalamud.NET.Sdk/15.0.0`. DalamudPackager turns the csproj
  properties into the manifest (`InternalName` = assembly name `MitCue`, `DalamudApiLevel` 15). A Release
  build writes `bin/Release/MitCue/MitCue.json` and `bin/Release/MitCue/latest.zip`. The zip holds
  `MitCue.dll`, `MitCue.json`, `MitCue.deps.json`, `MitCue.Core.dll` (with its `.pdb` and `.xml`) and
  `YamlDotNet.dll`. Dalamud's own assemblies are not included.
- **Repository file.** `repo.json` is written by `tools/make-repo-json.py` and checked by
  `tools/RepoJsonCheck` (see [README > Releasing](../README.md#releasing)). Dalamud 15.0.4.0 downloads
  `DownloadLinkInstall` for installs and updates alike. After download it requires the zip's `MitCue.json`
  to have exactly the same `InternalName` and `AssemblyVersion` as `repo.json`.

---

## 2. In-game test checklist

Work through the sections in order. Write down anything that differs from the expected result: what you
saw, the phase, and roughly the fight time. Attach the debug report and, for TOP, the recording (§2.6).

### 2.0 Before you start

- [ ] Install MitCue (either route in [INSTALL.md](INSTALL.md)). In `/mitcue` > **About**, note the version.
- [ ] In `/mitcue` > **Debug**, click **Clear problems**, so that new problems stand out.
- [ ] **Known issue in early test builds:** fight data and plans are loaded the first time a preview starts.
      If the Plans tab only says *Loading...* or the Status line says *Fight data did not load*, run
      `/mitcue preview` once and stop it with `/mitcue preview`. Report whether you needed this.

### 2.1 Loading and settings (5 minutes, anywhere)

- [ ] `/mitcue` opens the settings window with the tabs **General, Overlay, Plans, Debug, About**. `/mitcue`
      again closes it. The settings button in `/xlplugins` opens it too.
- [ ] The **Status** line makes sense for where you are. In town: *No supported fight in this zone (…)*.
- [ ] **Plans** lists the plans. Bundled plans show *bundled*. The table *Active plan per fight and job* has a
      row for TOP and your current job, with a suggested **My column**.
- [ ] **General** > **Test** plays the sound. Try two different *Sound effect* numbers.
- [ ] Change a few settings, switch MitCue off and on in `/xlplugins` (or restart the game), and check they
      are kept.
- [ ] **Problems** stays empty. If not, copy the text.

### 2.2 `/mitcue preview top` (15–20 minutes, near a striking dummy)

- [ ] `/mitcue preview top` prints *Preview started: TOP, "<plan>" as <JOB>, column "<column>", 1x; N entries
      are yours*. N must not be 0. If chat says *No entries match*, fix **My column** first.
- [ ] The overlay shows `PREVIEW 1x TOP - pull in 10.0s` and counts down. Any pre-pull entries appear during
      the countdown.
- [ ] At zero the header changes to `TOP - P1: … - 0:00` and the clock runs in real time.
- [ ] Each reminder appears about 15 s before its press time and counts down. About 1.5 s before, it turns
      **NOW** on a pulsing orange row and the sound plays once.
- [ ] Icons, action names in your client language, mechanic names, and targets/notes (*on MT*) look right
      for your plan.
- [ ] At the striking dummy, use an action while it shows **NOW**: it turns into a green check mark. Let
      another one pass: it turns into a red cross after its hit. Both disappear after about 3 s.
- [ ] Use an action long before its reminder. Note whether the reminder later shows *(early)* or stays open.
- [ ] `/mitcue preview top p3` (also try `p5` and `p6`): the preview restarts, and about 2 s after the
      simulated pull the clock jumps to the phase (P3 starts at 6:40). Earlier phases' reminders are skipped.
- [ ] **General** > tick **Run /mitcue preview at 4x speed**, then restart: the header shows `PREVIEW 4x`.
      Let one preview run to the end: chat says *Preview finished.* and `[Preview] TOP pull ended (preview
      finished): … done, … missed …`.
- [ ] `/mitcue preview` while a preview runs prints *Preview stopped.*, and the overlay goes back to what it
      showed before (usually nothing).
- [ ] **Overlay** tab:
  - [ ] dragging works and the position is kept after a reload;
  - [ ] **Lock position** stops dragging;
  - [ ] **Click-through** lets clicks reach the game;
  - [ ] **Scale**, **Background opacity**, **Maximum rows** and each **Columns** checkbox change the overlay;
  - [ ] **Reset position** moves it back;
  - [ ] **Always show** shows `SAMPLE - drag to position` when nothing runs.
- [ ] **General** > tick **Also show the plan's other columns**, then restart the preview: the other columns'
      calls appear dimmed with a tag like `[H2]`, and your own rows still come first.
- [ ] **Debug** during a preview: the *Preview* section shows *sim clock …; events played/total*, and the
      reminder list changes state as the preview runs.

Cooldown and resource warnings (*ready in N s*, *no Lily*) never appear in previews. They are checked in §2.4.

### 2.3 A simple fight (10 minutes: a dungeon boss, trial, or a striking dummy pull)

This checks the game event capture outside TOP. MitCue shows no reminders here.

- [ ] Before you pull, tick **General** > **Record pulls**. This also switches event capture on.
- [ ] Status: *No supported fight in this zone (…)*. The overlay stays hidden unless a preview runs.
- [ ] **Debug** > *Game*:
  - [ ] `ActionEffect hook: enabled at 0x…`. Anything else (*unavailable*, *failed*) is a bug to report;
  - [ ] `observed` goes up as people use actions;
  - [ ] `cast polling True; casts seen …` goes up when enemies cast;
  - [ ] `effective level` shows the synced level in a level-synced duty;
  - [ ] `in combat` follows the fight.
- [ ] **Debug** > *Last 50 events*:
  - [ ] boss casts appear as `StartsUsing <id> <action> by "<enemy>" 3.0s`;
  - [ ] your own actions appear as `Ability … by you`;
  - [ ] pulls show `InCombat 1` / `InCombat 0`;
  - [ ] a wipe or leaving the zone shows `Reset Wipe` / `Reset LeftZone`.
- [ ] **Debug** > *Recent own uses* lists your actions `via ActionEffect`. As Scholar, the fairy's Whispering
      Dawn shows up as Whispering Dawn `via Pet`.
- [ ] Type `/countdown 10`: **Debug** shows `countdown 9.6` counting down, then `-` after the pull.
- [ ] The **General** tab shows *Recording to <zone>-<date>-<time>.jsonl (N lines)*, and N goes up. Open the
      file in a text editor: the first line is a `Header` with the zone id and MitCue version. Check that no
      player names appear anywhere in it.
- [ ] **Problems** stays empty.

### 2.4 TOP

Before the first pull (tick **Record pulls** before you zone in):

- [ ] Status: *Ready: <plan> (N of M entries are yours).* The overlay shows `TOP - waiting for pull (<plan>)`.
- [ ] **Debug** > *Data*: `fights: top (territory 1122, level 90, 6 phases, … mechanics)`, `action catalog:
      loaded; aliases: loaded`.
- [ ] **Debug** > *Live session*: `player: <JOB> level 90, slot <slot>, column <column>; entries N own`.
- [ ] Type `/countdown 20`: the header shows `TOP - pull in 19.x s`, and pre-pull reminders (such as shields)
      count down. Cancel the countdown: the reminders clear. Start it again: they start over.

During pulls:

- [ ] At the pull, the header switches to `TOP - P1: … - 0:00` within about a second of combat starting.
      **Debug** > *last syncs* shows a line ending in `(start)`.
- [ ] P1 reminders line up with the mechanics. For any reminder that feels early or late, note the mechanic
      and the difference in seconds.
- [ ] At each phase change (P2 at the Firewall cast, P3, P4, P5 at Blue Screen, P6 at Blind Faith) the clock
      jumps to the new phase. *last syncs* shows `(jump)` lines and the `jumps` count goes up. Reminders from
      the skipped stretch do not fire (with **Show reminders skipped by a phase push** on, they show `skip`).
- [ ] Holding a long cooldown so that it is not back in time shows orange *ready in N s*. Running out of
      Lilies or Aetherflow shows *no Lily* / *no Aetherflow*.
- [ ] Your presses turn rows into check marks. With **Also show the plan's other columns** on, the
      co-healer's dimmed rows are ticked off when they press.
- [ ] If you die, the reminders keep running until the wipe.
- [ ] After a wipe: chat shows `TOP pull ended (wipe): N done, M missed. Missed: …`, the overlay goes back to
      *waiting for pull*, and the next pull starts cleanly.
- [ ] Switch plan or column between pulls: the Status line updates. Switching during a pull only takes effect
      after it.
- [ ] `/mitcue reset` during a pull clears the reminders without a summary. Note whether MitCue picks the
      fight up again before the pull ends. The next pull must work normally.
- [ ] Optional: `/xlstats` (Dalamud's plugin statistics) shows MitCue's draw and update times. Note anything
      noticeably high.

After the session:

- [ ] Untick **Record pulls**, or leave the zone, so the recording file is closed.
- [ ] Send the recording and a debug report copied right after a problem pull (§2.6), with your notes.

### 2.5 Reading the Debug tab

The Debug tab rebuilds its text four times a second. **Copy to clipboard** copies the whole report.

| Section | What good looks like |
|---------|----------------------|
| **Game** | Logged in, the right zone name, job and synced level. `ActionEffect hook: enabled`. `observed` and `casts seen` go up in fights |
| **Data** | Every fight listed with its zone, level, phases and mechanics. Action catalog and aliases *loaded*. The plan count and the plans folder |
| **Live session** | The fight, plan, player (job, level, slot, column), own and other entry counts. Then `mode` (Idle, Countdown, Running), `timeline running`, `time`, `phase`. `faulted False`; `rebuild pending True` only while a change waits for the pull to end |
| **last syncs** | One line per sync: `[clock] from -> to line N "entry" <sync>`, with `(start)` at the pull and `(jump)` at phase pushes. Large `from -> to` differences outside phase changes mean the sync is off. Report them |
| **reminders** | Every reminder with its state, `press`, `hit`, the `[earliest, latest]` window, the action, the mechanic id and, if used, `used <time>` with `(early)`. `readyIn` and `no-resource` show the warnings |
| **Preview** | `not running`, or the preview's description, simulated clock and events played |
| **Recorder** | Whether it is enabled, the current file, the line count and the folder |
| **Last 50 events** | Enemy casts and abilities, your own abilities, combat changes and resets, with their clock times. Other players' and pets' actions are left out; your pet's show under *Recent own uses* |
| **Recent own uses** | Each detected use of yours and how it was found: `ActionEffect`, `Pet` or `Recast` |
| **Problems** | `none`, or each problem with when it last happened and how often |

Buttons:

- **Reset live session**: the same as `/mitcue reset`.
- **Reload data**: reloads the fights, timelines, actions and aliases.
- **Clear problems**: empties the list.

### 2.6 Sending a recording

1. Tick **Record pulls** *before* you enter the zone. A file starts when you zone in, and every pull of that
   visit goes into it.
2. When you're done, untick **Record pulls** or leave the zone. This closes the file. MitCue also writes to
   it every couple of seconds while it is open.
3. Open the folder shown under the checkbox, or under *Recorder* in the debug report. On Windows that is
   `%AppData%\XIVLauncher\pluginConfigs\MitCue\recordings\`.
4. Pick the file by zone and time: `1122-20261111-203015.jsonl` is TOP, entered at 20:30:15. TEA files start
   with `887-`, DMU files with `1363-`.
5. Zip it, since these files compress well, and send it together with:
   - the debug report copied right after the pull in question;
   - which pull(s) and roughly what fight time;
   - what you expected and what you saw.

Format: one JSON object per line. The first line is a `Header` (format version, zone id, start time, client
language, MitCue version). After that come `CombatStateChanged`, `CastStarted` (action id, caster id and
name, cast time, target id), `AbilityResolved` (action id, caster id, player/local flags, NPC name only for
non-players, target ids), `DutyReset`, `CountdownStarted`/`CountdownEnded` and `OwnUse` (action id and how
it was detected), each with the monotonic time `t` in seconds. Player names are never written.
