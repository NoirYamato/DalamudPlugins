# MitCue plugin: how it works and how to test it

This page describes the Dalamud side of MitCue (`src/MitCue`) as it is implemented, and gives the in-game
test checklist. The requirements and the core logic (plan format, sync engine, timing rules, importer) are in
[DESIGN.md](DESIGN.md). Installing is covered in [INSTALL.md](INSTALL.md), and everyday use in the
[README](../README.md).

- [1. How it works](#1-how-it-works)
- [2. In-game test checklist](#2-in-game-test-checklist)

---

## 1. How it works

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

 CoreData (fights, timelines, actions, aliases) and PlanStore (bundled and user plans) load on the first tick.

 ImportWindow (UI/, main thread) ──── job ────► BackgroundJobs (thread pool, one job at a time)
   1. Source: link, pasted cells, plan YAML        ImportPipeline: download (SheetFetcher), read the workbook or
   2. Options: fight, tabs, columns, preset        the pasted cells, trial import and import (core PlanImporter),
   3. Review: ReviewModel, ReviewValidator         read the AI's YAML
   4. Save: PlanStore         ◄──── result or error, through IFramework.RunOnFrameworkThread ────┘
```

Everything runs on the game's main thread, in `IFramework.Update` or in Draw, with these exceptions:

- the ActionEffect hook detour and Dalamud's duty and territory events only put data into a concurrent queue,
  which the next framework tick empties;
- the import window's slow steps (downloads, reading a workbook or a document, the trial import, the import,
  reading plan YAML) run on the thread pool, and their results come back on the main thread (§1.7).

### 1.2 Components

| Component | File | Responsibility |
|-----------|------|----------------|
| `Plugin` | `Plugin.cs` | Entry point (`IDalamudPlugin`). Gets Dalamud services through `[PluginService]`, creates every component, registers `/mitcue` (`import`, `overlay`, `preview`, `reset`, `debug`), runs the per-frame `Tick` and hosts the three windows |
| `Configuration` | `Configuration.cs` | Settings, saved by Dalamud as `pluginConfigs/MitCue.json`, including the active plans, column choices and personal aliases. `Sanitize()` clamps values after loading. Collections use `ObjectCreationHandling.Replace`. Every change bumps `Revision`, and the runtime watches that number |
| `FrameContext` | `Runtime/FrameContext.cs` | Game state sampled once per tick: logged in, territory, client language, local entity id, job, effective (synced) level from `IPlayerState`, InCombat, bound by duty, countdown, this frame's events and uses, and the party's jobs (`IPartyList`, read only while the co-healer's calls are shown) |
| `GameEventSource` | `Game/GameEventSource.cs` | Turns game state into core `GameEvent`s stamped with MitCue's monotonic clock (§1.4) |
| `ActionEffectHook` | `Game/ActionEffectHook.cs` | Read-only hook on `ActionEffectHandler.Receive`. Copies the caster, action, targets and sequence, then always calls the original |
| `CooldownProvider` | `Game/CooldownProvider.cs` | `ICooldownProvider` for the scheduler: seconds until an action is ready (`ActionManager`, including upgraded actions), job resources from the gauges (Lily, Blood Lily, Aetherflow, Addersgall, Addersting), and the usage fallback (§1.5) |
| `OwnUse`, `OwnUseDeduper`, `PetActions` | `Game/` | Detected action uses. Merges duplicates found by different paths. Maps fairy and Seraph actions to the Scholar command that triggered them |
| `PullRecorder` | `Game/PullRecorder.cs` | Optional JSON-lines log of every event, one file per zone visit, only in a duty or a fight zone (§2.7) |
| `SoundPlayer` | `Game/SoundPlayer.cs` | Plays chat sound effect `<se.N>` through `UIGlobals.PlaySoundEffect` |
| `CoreData` | `Data/CoreData.cs` | Loads the fight library (`data/fights`, cactbot timelines), action catalog and alias dictionary embedded in `MitCue.Core.dll`, and adds the personal aliases. Loads on the first tick, and again on the tick after **Reload data**. Every call is guarded |
| `GameData` | `Data/GameData.cs` | Lumina lookups: action names in the client language, icons, the GCD flag, job abbreviations, zone names (cached) |
| `PlanStore` | `Plans/PlanStore.cs` | The bundled plans (embedded, read-only) plus user plans (`pluginConfigs/MitCue/plans/*.json`); a user plan with a bundled plan's id takes its place. Stores slot synonyms (`T1`, `T2`, `ST`, `D1`–`D4`) as the standard slots. Works out the active plan per fight and job and "my column" (§1.5, with the core's `ColumnSuggestion`). Imports and exports share codes. Loads on the first tick, after `CoreData`, and again with **Reload plans** |
| `PlanChecks` | `Plans/PlanChecks.cs` | Checks plans that skip the import review (share codes and plan files in the plans folder) with the review's `ReviewValidator`, and keeps only what makes a call impossible: a mechanic the fight lacks, an unknown action, an action (or every option of an either/or) the entry's job or slot cannot use, or not at the sync level |
| `SessionManager` | `Runtime/SessionManager.cs` | Owns the live `EncounterSession`: builds it, rebuilds it when something changes, feeds it every frame, prints the pull summary and asks for the sound |
| `EncounterSession` | `Runtime/EncounterSession.cs` | One fight, plan and player: a core `TimelinePlayer` plus a `ReminderScheduler` for your entries and an optional one for the co-healer's. Handles the countdown, pull start and pull end |
| `TimelineSyncEngine` | `Runtime/SyncEngine.cs` | The core `TimelinePlayer` behind an interface, so tests can drive a session with a scripted engine |
| `PartnerSelection`, `PartyJobs` | `Runtime/` | The co-healer's calls (§1.5); the party's jobs as a bit mask |
| `PreviewDriver` | `Runtime/PreviewDriver.cs` | `/mitcue preview`: the core `FightSimulator` produces a pull, which plays through a separate `EncounterSession` |
| `OverlayModel`, `ReminderText` | `Runtime/` | The overlay snapshot built each tick, with display strings cached per reminder; the chat summary |
| `OverlayWindow`, `ConfigWindow`, `DebugReport` | `UI/` | The overlay, the settings window and the Debug tab text |
| `ImportWindow` | `UI/ImportWindow.cs`, `UI/ImportWindow.Review.cs` | The import window's four steps (§1.7) |
| `TextBox` | `UI/TextBox.cs` | The large paste boxes (pasted cells, the AI's answer) |
| `BackgroundJobs` | `Import/BackgroundJobs.cs` | Runs one slow import step at a time on the thread pool and hands its result or error to the main thread |
| `ImportPipeline` | `Import/ImportPipeline.cs` | The slow steps: download and read a link, read pasted cells, the trial import, the import, read plan YAML. Every failure becomes a message for the player |
| `GoogleLinks`, `SheetFetcher` | `Import/` | Parse the Google Sheets and Docs links people paste and download their exports from `docs.google.com`: 20 s time limit, 25 MB cap, cancellable. A sign-in page instead of the file means the file is not shared |
| `DocHtmlReader`, `DocTextReader` | `Import/` | Google Docs: the web page export (each table becomes a tab, the other paragraphs a "Text" tab), or the plain-text export when the page is too large or too slow |
| `ImportAnalysis`, `TrialImportTracker` | `Import/` | The Options step's trial import: tabs, columns and entry counts, and the tabs and columns ticked by default |
| `ReviewModel`, `ReviewValidator` | `Import/` | The review's rows, edits and undo; the checks of each entry (unknown mechanics or actions, actions the job can't use at the sync level, missing who, odd offsets) |
| `ImportColumns` | `Import/ImportColumns.cs` | The column choice saved with an imported plan, how many entries the overlay will show, and which of the player's calls the choice leaves out, by column |
| `LlmPrompt`, `SheetText` | `Import/` | The AI prompt: the plan format and rules, the fight's mechanics, the chosen jobs' actions and aliases, and the loaded sheet as text. Reads the YAML out of the AI's answer |
| `PersonalAliases` | `Import/PersonalAliases.cs` | Review fixes remembered as personal aliases |
| `ErrorReporter` | `Util/ErrorReporter.cs` | Collects problems. Each distinct problem is logged once to Dalamud's log and listed under **Problems** and in the debug report |

### 1.3 One framework tick

`Plugin.Tick` runs once per `IFramework.Update`, inside a try/catch:

1. **Load.** `CoreData.EnsureLoaded` and `PlanStore.EnsureLoaded`. They load the fight data and the plans on
   the first tick, and the fight data again after **Reload data**; otherwise they return at once.
2. **Sample** the game state into `FrameContext`. A zone change clears the use de-duplicator and the cooldown
   tracking.
3. **Events.** `GameEventSource.Poll` collects this frame's events in time order. Cast polling and ActionEffect
   capture only run while something needs them: a live session, a preview, or a recording.
4. **Own uses.** It takes the ActionEffect uses first, then the cooldown fallback for the actions the live
   session and the preview track. A use counts once: reports of one use by different paths (your
   ActionEffect, your pet's ActionEffect, the recast change) within 1 s, or 5 s for the pet's, are merged
   (`OwnUseDeduper`). Two uses seen by the same path always count, such as both charges of Divine Benison
   0.6 s apart.
5. **Recorder.** With **Record pulls** on, in a duty or a fight zone, the frame's events, own uses and
   countdown changes are written.
6. **Live session.** `SessionManager.Update` feeds the events and uses to the session, rebuilds it if needed,
   and advances the reminders to now.
7. **Preview.** `PreviewDriver.Update` plays the simulated events up to the simulated clock.
8. **Overlay snapshot.** `OverlayModel.Build` picks what to show: the preview first, then the live session,
   then the sample rows (with **Always show**), otherwise nothing.
9. **Sound.** If a reminder turned NOW this frame, or **Test** was clicked, the sound plays.

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

TEA's phases also start on syncs MitCue sees: `Ability 4830` (Hawk Blaster) at 200.0, `StartsUsing 485A`
(Temporal Stasis) at 492.0 and `Ability 4A8B` at 900.0. In DMU, cactbot syncs the P2 (297.4) and P4 (1087.7)
starts only on `ActorControl` lines, which MitCue does not see. `data/fights/dmu.yaml` therefore adds two
fallback syncs (`timeline_additions`, appended to the timeline when it loads): `StartsUsing C24C` (Ultimate
Embrace) at 315.1 for P2 and `StartsUsing C2DC` (Kefka Says) at 1095.0 for P4. They are derived from cast
data and have not been checked against logs yet. DMU's P3 (597.0) and P5 (1355.6) are entered through
cactbot's own jumps.

### 1.5 Sessions, pulls and reminders

**When there is a live session.** You must be logged in, in a zone that has a fight definition (TOP 1122,
TEA 887, DMU 1363), on a combat job, and there must be an active plan for that fight and job. Otherwise the
Status line says what is missing.

**The plan.** `PlanStore.ActivePlanFor` takes the plan chosen in the Plans tab (`ActivePlans["top/WHM"]`; an
empty value means "no plan"). Without a choice it is automatic: the bundled plan meant for the job
(`PlanStore.BundledDefaults`, by plan id: `top/WHM` → *Carry the Clear – WHM*, `top/SCH` → *LPDU TOP
Mitigation*), else the first bundled plan for the job by name, else the player's first own plan for the
fight that has a column or entries for the job (TEA and DMU ship no plans). An imported plan replaces a
bundled plan only when chosen: the import window's **Make it my active plan …**, the Plans tab's **Make an
imported share code my active plan as …** (`PlanStore.ImportShareCode` with the current job; it sets the plan
only when it has calls for that job), or the **Active plan** combo. A share code of a bundled plan has that
plan's id and takes its place, so it stays the automatic plan of the job the bundled plan is meant for.

**The column.** `PlanStore.MappingFor` takes the column chosen for that plan and job. Without a choice it
uses the core's `ColumnSuggestion.Suggest`, which the CLI shares: the job's column (`JobColumn`: the job's
column, the one in the configured slot when there are several, else the first; else the slot's column). When
that column leaves out the job-less calls of the player's slot, it suggests the narrowest choice that has
them too and no call of another column (`Complete`: "any column for my job in my slot", or one of the
player's columns). Several columns of the job are alternatives, never combined. On the bundled LPDU plan the
H2 Scholar gets "any column for SCH in slot H2": 31 entries, including the job-less "H2" calls of P1 and P2
(the H1 White Mage gets 11 the same way). A Warrior gets the first tank-pair column, "WAR/DRK WAR", and the
MT or OT calls for WAR (24 entries in MT, 23 in OT); the job-less "MT"/"OT" calls (Reprisal, Rampart) and
the "CM LB Tank" and "CM Rep Tank" columns are left out, so tanks pick their column.

**The co-healer's calls.** With **Also show my co-healer's calls** on, `PartnerSelection` adds the entries
that are not the player's, from the player's role group (tank, healer or DPS), but not those for the player's
own seat (their job or their slot in another column, such as LPDU's "H2 SGE" for the H2 Scholar), nor the
partner job's columns for team compositions without the player's job (a Warrior sees "WAR/DRK DRK", not
"DRK/GNB DRK" or "DRK/PLD DRK"). When the party is known, columns of jobs nobody in the party plays are left
out too (LPDU's "H1 AST" next to a White Mage). A change in the party's jobs rebuilds the session only while
the option is on.

**Rebuilds.** The session is rebuilt when what it was built from changes: the zone, job, synced level or
client language; the active plan, or the column and slot used for the player (**My column**, **My usual
party slot**); the plans themselves (a save, a delete, **Reload plans**); **Also show my co-healer's
calls**; and, while that option is on, the party's jobs.

- A change of zone, job or login rebuilds it straight away.
- The other changes wait until the current countdown or pull is over. After a countdown that reaches zero
  they also wait through the hold before the pull and through the pull (or until 20 s after "0" when no pull
  starts), so a pre-pull call already pressed stays done.
- The timing sliders (*Show reminders this early*, *Turn "NOW" this early*) and *Show skipped* apply
  immediately, even in the middle of a pull. The other settings don't need a rebuild.
- **Reload data** (Debug tab) does not rebuild it: the session keeps the fight data it was built with, and a
  running pull carries on. The next rebuild, or `/mitcue reset`, uses the reloaded data.

**Countdown.** While `/countdown` runs, the session uses a timeline time of minus the time left, so pre-pull
entries such as shields count down too. A restarted countdown starts again from a clean state. A countdown
stopped with more than 1 s left counts as cancelled and clears everything. When the countdown reaches zero,
the pre-pull reminders wait at time 0 for up to 20 s for the pull to start.

**Pull.** The pull starts when the timeline starts, at the `InCombat` sync. It ends on any of these:

- a `DutyReset` event;
- a sync that stops the timeline (cactbot's reset);
- the timeline has stopped and you are out of combat (*combat ended*);
- more than 10 s out of combat while the timeline still runs, with no enemy cast or ability for 10 s
  either. Enemy activity keeps the pull alive while you are dead.

At the end, if anything was done, missed or skipped, the summary goes to chat when **Track done/missed** and
**Print a done/missed summary** are on. Then the session rewinds for the next pull.

**Reminders.** The core `ReminderScheduler` turns your plan entries into reminders: Pending, then Upcoming,
then Due (NOW), and finally Done, Missed, Skipped, or Passed for notes and optional entries. The timing rules
are in [DESIGN.md](DESIGN.md) §5. Done and missed rows stay visible for 3 s, or vanish at once with **Track
done/missed** off; skipped rows show for 3 s only with **Show reminders skipped by a phase push**. At the
lookahead time the cooldown provider is asked whether the action will be ready, which produces *ready in N s*
and *no Lily*.

**Recognising your presses.**

1. **ActionEffect:** you are the caster. Your pet counts as well, so the fairy's Whispering Dawn (803) and
   Seraph's Angel's Whisper (16550) both count as the Scholar command Whispering Dawn (16537). The same
   mapping covers Fey Illumination, Fey Blessing and Consolation.
2. **Cooldown fallback:** for the actions of the current reminders (GCDs excluded), a recast that starts, an
   elapsed time that resets, or a charge that drops counts as a use. This covers anything the first path
   misses.

Other players' uses only mark the co-healer's dimmed rows.

**Errors.** Every call into MitCue.Core and every game read is guarded. A failure is listed under **Problems**
and does not break the frame. If the live session itself throws, it stops ("Stopped after an error … Use
/mitcue reset to retry.") until `/mitcue reset` or the next rebuild.

### 1.6 Preview

`PreviewDriver.Start(fight, phase)` checks the fight, phase, timeline and plan, then generates a pull with
`FightSimulator`. It keeps only the event kinds MitCue sees in game (`GameVisibleEvents`: combat changes, cast
starts, resolved abilities, duty resets), so the preview syncs and follows phases the way a live pull does:
TOP's P2 is entered by the 7B40 cast at 203.4, not by the `MapEffect` at 200.0. Then:

- **Plan.** The plan and column the live session would use for the current job. Without one, the first plan
  for the fight (user plans first, then bundled, each by name), as the job of the column saved for it, else
  as its first job. The co-healer's calls come from every other column of the role except the player's seat:
  the preview does not know the party.
- **Clock.** The pull happens at simulated clock 1000. A whole-fight preview starts 10 s earlier, with a
  simulated countdown. A phase preview starts 2 s before the pull, without a countdown. It drops the earlier
  phases' events, so P1 starts at the pull and the chosen phase begins 2 s after it, where the phase's own
  sync jumps the timeline. The earlier phases' reminders are marked skipped, not missed.
- **Separate session.** The preview has its own `EncounterSession`, with English name matching, because the
  simulated names come from the timeline. Simulated events never reach the live session.
- **Your presses.** Your real action uses are forwarded at the simulated time, so you can test usage tracking
  on a striking dummy. The cooldown warnings are off in previews.
- **End.** It stops 5 s after the last event once the timeline has stopped, or 60 s after the last event at
  the latest. Then it prints the summary (with the summary settings on) and *Preview finished.*

### 1.7 Import window

`/mitcue import` (or **Import from sheet...** in the Plans tab) opens *MitCue: import a plan*. Its four steps:

1. **Source.**
   - *Google Sheets / Docs link*: `GoogleLinks.TryParse` accepts the shapes people paste (address bar, *Copy
     link*, `/u/0/`, `htmlview`, `gid=`, published-to-web links, bare ids). **Fetch** runs
     `ImportPipeline.LoadLinkAsync`. A sheet is downloaded whole as `.xlsx` (`export?format=xlsx`) and read
     by the core `SheetReaders.FromXlsx` with its colours. When the link names a tab (`gid=`), the one-tab
     export finds which one. The `htmlview` page gives back the tab names the export changes (`P1P2` →
     `P1/P2`, `WARDRK` → `WAR/DRK`), best effort: it is requested together with the workbook, and once the
     workbook is in, the import waits at most 3 s more for it (`ImportPipeline.TabListGrace`), then keeps
     the export's names. A doc is read from its web page export, or from its plain-text export when the page
     is too large or too slow (it inlines every image).
   - *Paste cells*: `ImportPipeline.LoadPasted` reads tab- or comma-separated text, one tab per paste; **Add
     as another tab** adds the next tab to the same document.
   - *Paste plan YAML (AI route)*: **Copy AI prompt** puts `LlmPrompt.Build`'s prompt on the clipboard: the
     plan format and rules, the fight's mechanics, the chosen jobs' actions at the sync level and their
     aliases, and, when a sheet is loaded and *Include the text of …* is ticked, its chosen tabs as text
     (`SheetText`). **Check the YAML and review** reads the answer (`ImportPipeline.ParseYaml`: the YAML block
     of the answer, `PlanSerializer.FromYaml`, `ReviewValidator.Repair`) and goes straight to the review.
2. **Options.** A trial import of every tab and column (`ImportPipeline.Analyze` → `ImportAnalysis`, once per
   source and fight, `TrialImportTracker`) fills the tab and column lists and ticks the player's. **Import
   and review** runs the core `PlanImporter` with the chosen fight, tabs, columns, plan name and calibration
   preset.
3. **Review.** `ReviewModel` holds one row per entry with the importer's diagnostics and `ReviewValidator`'s
   checks, which run again after each edit. The player can change the mechanic, hit, offset, flags, target,
   note and items, and delete entries (with undo). Replacing a note with an action offers to remember it:
   `PersonalAliases` saves it in `Configuration.UserAliases`, and `CoreData.ApplyUserAliases` adds it to the
   alias dictionary for later imports.
4. **Save.** `PlanStore.Save` writes the user plan. **Save as** offers the earlier imports of the same sheet
   and tabs, and pre-selects one when it is the only one with the same columns; tab names and column keys
   compare in their xlsx export form (`P1P2` = `P1/P2`, `WARDRK WAR` = `WAR/DRK WAR`), and the replaced
   plan's column choices follow a renamed column (`PlanStore.CarryOver`). When the job's column leaves out
   the job-less calls of the player's slot, `ImportColumns` saves the suggestion that has them too
   (`PlanStore.SetMapping`). When a column choice leaves out some of the player's calls (the replaced plan's,
   or a tank's pair column on LPDU: no choice has the job-less "MT" calls without the other pairs'), the
   step warns and names the columns of the calls left out. With **Make it my active plan …** ticked,
   `PlanStore.SetActive` makes it the active plan. Chat says *Saved plan "…" (N entries, active for TOP as
   WHM)*, or *…; your active plan for TOP as WHM stays "…", choose this one in /mitcue > Plans to use it)*,
   and the window closes.

**Threading.** The slow steps (download, reading a workbook or a document, reading pasted cells, the trial
import, the import, reading YAML) run through `BackgroundJobs`:

- one job at a time, on the thread pool (`Task.Run`); starting a job cancels the running one;
- **Cancel** in the status line, and closing the window, cancel the running job; results of a cancelled or
  replaced job are dropped (their exceptions are still observed);
- the result or the error comes back through `IFramework.RunOnFrameworkThread`, so the window's state is only
  touched on the main thread. The work itself touches no UI or game state;
- the Options step's choices are locked while a job runs, so an import always matches the options shown.

### 1.8 Safety and threading rules

- **Reminder-only.** Nothing in `src/` uses actions, sends chat commands or targets. `IChatGui.Print` only
  writes to your own chat log. The ActionEffect detour only reads the packet, copies what it needs first, and
  always calls the original.
- **Network.** Only `SheetFetcher` connects to anything: after **Fetch**, to `docs.google.com` (following
  Google's redirects to its download servers). Nothing else in MitCue uses the network.
- **Constructor.** The constructor runs off the main thread, so it touches neither the object table nor
  textures.
- **No game objects kept.** No `IGameObject` survives from one frame to the next; only entity IDs are stored,
  because the object table reuses its wrapper objects.
- **Dev mode.** Running as a dev plugin turns on Dalamud's dev-mode main-thread checks. A violation shows as a
  Dalamud error, which is worth reporting (Dalamud plans to make these checks mandatory in API 16).

### 1.9 Data, files and packaging

- **Embedded data.** `MitCue.Core.dll` contains `data/**` (fight catalogs, cactbot timelines, action data,
  aliases) as `MitCue.data/...`, and `plans/**/*.yaml`, the three bundled TOP plans, as `MitCue.plans/...`.
  **Reload data** in the Debug tab stops a running preview and drops the fight data, actions and aliases;
  the next tick loads them again. It does not rebuild the live session (§1.5), so a running pull carries on.
  It does not touch plans: **Plans** > **Reload plans** re-reads them.
- **Files.** Settings are in `pluginConfigs/MitCue.json`. User plans are in `pluginConfigs/MitCue/plans/`.
  Recordings are in `pluginConfigs/MitCue/recordings/`. See [INSTALL.md](INSTALL.md#where-mitcue-keeps-its-files).
- **Package.** `src/MitCue/MitCue.csproj` uses `Dalamud.NET.Sdk/15.0.0`. DalamudPackager turns the csproj
  properties into the manifest (`InternalName` = assembly name `MitCue`, `DalamudApiLevel` 15). A Release
  build writes `bin/Release/MitCue/MitCue.json` and `bin/Release/MitCue/latest.zip`. The zip holds
  `MitCue.dll`, `MitCue.json`, `MitCue.deps.json`, `MitCue.Core.dll` (with its `.pdb` and `.xml`),
  `YamlDotNet.dll`, and the four libraries of the `.xlsx` reader: `DocumentFormat.OpenXml.dll`,
  `DocumentFormat.OpenXml.Framework.dll`, `System.IO.Packaging.dll` and `ExcelNumberFormat.dll`
  ([INSTALL.md](INSTALL.md#what-the-plugin-zip-contains)). Dalamud's own assemblies are not included.
- **Publishing.** Players install MitCue from the shared repository file
  `https://raw.githubusercontent.com/NoirYamato/DalamudPlugins/main/repo.json`. `publish-mitcue.ps1` in that
  repository publishes a build: [PUBLISHING.md](PUBLISHING.md). CI checks the zip and a sample `repo.json`
  written by `tools/make-repo-json.py` with `tools/RepoJsonCheck`, which uses Dalamud's own manifest types.
  Dalamud 15.0.4.0 downloads `DownloadLinkInstall` for installs and updates alike. After download it requires
  the zip's `MitCue.json` to have exactly the same `InternalName` and `AssemblyVersion` as `repo.json`.

---

## 2. In-game test checklist

Work through the sections in order. Write down anything that differs from the expected result: what you
saw, the phase, and roughly the fight time. Attach the debug report and, for TOP, the recording (§2.7).

### 2.0 Before you start

- [ ] Install MitCue (either route in [INSTALL.md](INSTALL.md)). In `/mitcue` > **About**, note the version.
- [ ] In `/mitcue` > **Debug**, click **Clear problems**, so that new problems stand out.

### 2.1 Loading and settings (5 minutes, anywhere)

- [ ] `/mitcue` opens the settings window with the tabs **General, Overlay, Plans, Debug, About**. `/mitcue`
      again closes it. MitCue's buttons in `/xlplugins` open it too.
- [ ] The **Status** line makes sense for where you are. In town: *No supported fight in this zone (…)*, also
      right after a game start or a plugin reload.
- [ ] **Debug** > *Data*: `fights: dmu (territory 1363, level 100, 5 phases, 63 mechanics), tea (territory
      887, level 80, 4 phases, 61 mechanics), top (territory 1122, level 90, 6 phases, 79 mechanics)`,
      `action catalog: loaded; aliases: loaded` and `plans: 3 (0 user) in …` (before any import).
- [ ] **Plans** lists the three bundled plans, marked *bundled, google-sheet*. In *Active plan per fight and
      job*, with **My usual party slot** set to your seat (H1 as White Mage, H2 as Scholar; for the Scholar,
      no slot gives the same result):
  - [ ] as White Mage, the TOP / WHM row: *(automatic) Carry the Clear – WHM*, column *(suggested) White
        Mage (WHM)*;
  - [ ] as Scholar, the TOP / SCH row: *(automatic) LPDU TOP Mitigation*, column *(suggested) (any column for
        my job in slot H2)*. With H1 set instead, the suggestion is *H2 SCH (H2, SCH)*, which leaves out
        LPDU's job-less "H2" calls of P1 and P2;
  - [ ] the TEA and DMU rows for your current job: *(automatic: none)*.
- [ ] **General** > **Test** plays the sound. Try two different *Sound effect* numbers.
- [ ] Untick **Track done/missed**: **Print a done/missed summary after each pull** is greyed out. Tick it
      again.
- [ ] Change a few settings, switch MitCue off and on in `/xlplugins` (or restart the game), and check they
      are kept.
- [ ] **Problems** stays empty. If not, copy the text.

### 2.2 `/mitcue preview top` (15–20 minutes, near a striking dummy)

- [ ] `/mitcue preview top` prints *Preview started: TOP, "<plan>" as <JOB>, column "<column>", 1x; N entries
      are yours.* As White Mage: `"Carry the Clear – WHM" as WHM, column "White Mage"`, 57 entries. As
      Scholar with slot H2 (or none): `"LPDU TOP Mitigation" as SCH` (no column: any column for the job), 31
      entries. N must not be 0. If chat says *No entries match*, fix **My column** first.
- [ ] The overlay shows `PREVIEW 1x TOP - pull in 10.0s` and counts down. Any pre-pull entries appear during
      the countdown.
- [ ] At zero the header changes to `TOP - P1: … - 0:00` and the clock runs in real time.
- [ ] Each reminder appears about 15 s before its press time and counts down. About 1.5 s before, it turns
      **NOW** on a pulsing orange row and the sound plays once.
- [ ] Icons, action names in your client language, mechanic names, and targets/notes (*on MT*) look right
      for your plan.
- [ ] At the striking dummy, use an action while it shows **NOW**: it turns into a green check mark. Let
      another one pass: it turns into a red cross when its window ends: just before the hit for mitigation,
      shields and heals placed before the damage (Liturgy of the Bell, Excogitation), 5 s after the press for
      heals used after the hit. Both disappear after about 3 s.
- [ ] Use an action long before its reminder. Note whether the reminder later shows *(early)* or stays open.
- [ ] `/mitcue preview top p3` (also try `p5` and `p6`): the preview restarts, and about 2 s after the
      simulated pull the clock jumps to the phase (P3 starts at 6:40). Earlier phases' reminders are skipped.
- [ ] **General** > tick **Run /mitcue preview at 4x speed**, then restart: the header shows `PREVIEW 4x`.
      Let one preview run to the end: chat says `[Preview] TOP pull ended (preview finished): … done, …
      missed …`, then *Preview finished.*
- [ ] `/mitcue preview` while a preview runs prints *Preview stopped.*, and the overlay goes back to what it
      showed before (usually nothing).
- [ ] `/mitcue preview tea` says *No plan for The Epic of Alexander (Ultimate). Import one in /mitcue >
      Plans.*
- [ ] **Overlay** tab:
  - [ ] dragging works and the position is kept after a reload;
  - [ ] **Lock position** stops dragging;
  - [ ] **Click-through** lets clicks reach the game;
  - [ ] **Scale**, **Background opacity**, **Maximum rows** and each **Columns** checkbox change the overlay;
  - [ ] **Reset position** moves it back;
  - [ ] **Always show** shows `SAMPLE - drag to position - 0:00` when nothing runs. Untick it again: while
        it is on, the overlay shows these sample rows in §2.4 and instead of *waiting for pull* in §2.5.
- [ ] **General** > tick **Also show my co-healer's calls**, then restart the preview. As Scholar on LPDU,
      the H1 columns' calls appear dimmed with tags like `[H1 WHM]` (the preview doesn't know your party, so
      `[H1 AST]` too), never `[H2 SGE]`. As White Mage, *Carry the Clear – WHM* has no Scholar column, so
      first choose *Carry the Clear – WHM + SCH* under **Active plan**: the `[Scholar]` calls appear. All rows
      are in press-time order, and your own rows are never pushed out. White Mage: afterwards, set **Active
      plan** back to *(automatic: the bundled plan for this job, else my own plan for it)*.
- [ ] **Debug** during a preview: the *Preview* section shows *sim clock …; events played/total*, and the
      reminder list changes state as the preview runs.

Cooldown and resource warnings (*ready in N s*, *no Lily*) never appear in previews. They are checked in §2.5.

### 2.3 Import (20 minutes, anywhere)

- [ ] `/mitcue import` opens *MitCue: import a plan* at **1. Source**. **Import from sheet...** in the Plans tab
      does the same.
- [ ] **Link**: paste the link of the LPDU sheet and click **Fetch**. The status line shows *Downloading the
      sheet... (N s)* with **Cancel**, then *Loaded Google Sheet "…" (N tabs).*, and **2. Options** opens.
      The tab list shows `P1/P2` and `WAR/DRK`, not `P1P2` and `WARDRK` (those mean Google's tab list took
      more than 3 s after the sheet: note it).
- [ ] A link to a sheet that is not shared publicly gives a message about sharing (*Anyone with the link*).
- [ ] **Options**: after *Looking at the tabs...*, your tabs and columns are ticked, and your columns are
      marked *(you: <JOB>)*. **Import and review**: the review lists the entries, with counts of entries with
      errors and warnings.
- [ ] **Review**: filter with the search box, **Only problems** and the column filter (*My calls (<JOB>)*).
      Drag an offset (right-click sets it back to automatic), pick another mechanic, toggle a flag, delete an
      entry and **Undo delete**. Each change re-checks the row.
- [ ] Replace a note with an action (**Replace with an action**): **Remember "…" = …** appears. Click it, then
      import again: that shorthand is now read as the action. **Forget** removes it.
- [ ] **Save**: *N entries apply to you as <JOB>*; **Make it my active plan for … as …** is ticked. **Save
      plan**: chat says *[MitCue] Saved plan "…" (N entries, active for TOP as <JOB>…)* and the window
      closes. The Plans tab lists the plan as *user, google-sheet* and shows it, without *(automatic)*, for
      your job.
- [ ] Import the same sheet again: **Save** offers **Save as** *replace "…"*.
- [ ] **Paste cells**: copy one tab's cells (with the header row) in Google Sheets, **Paste from clipboard**,
      **Read the cells**; then another tab and **Add as another tab**. Pasting two tabs at once warns about
      several header rows.
- [ ] **AI route**: choose the fight and your job, **Copy AI prompt**, paste it into an AI chat with the plan,
      paste the answer back, **Check the YAML and review**. Unknown mechanics or actions show as errors in the
      review.
- [ ] Close the window during a download: it is cancelled, and reopening shows the window as you left it.
- [ ] **Share codes**: **Make an imported share code my active plan as <JOB>** shows under the box, ticked.
      **Copy share code** of your job's bundled plan, paste it into **Share code**, **Import share code**: it
      is listed as *user* instead of *bundled*, and the message ends *It is your active plan for TOP as
      <JOB>.* **Delete** it: the bundled plan is back. A damaged code gives a message instead of a plan.
- [ ] Untick **Make an imported share code my active plan as <JOB>** and import the co-healer's plan's code.
      As White Mage, *LPDU TOP Mitigation*'s: the message ends *Your active plan for TOP as WHM stays "Carry
      the Clear – WHM": choose this one in the table above to use it.*, and the TOP / WHM row still shows
      *Carry the Clear – WHM*. As Scholar, *Carry the Clear – WHM*'s: *It has no calls for SCH: choose it in
      the table above for the job it is for.* Tick the box again.
- [ ] **Problems** stays empty, apart from plan-check warnings you caused on purpose.
- [ ] Clean up before §2.5: **Delete** every plan you saved in this section (**Delete**, then **Click again
      to delete**). The TOP row for your job must then read *(automatic) Carry the Clear – WHM* (White Mage)
      or *(automatic) LPDU TOP Mitigation* (Scholar); if it doesn't, choose *(automatic: the bundled plan for
      this job, else my own plan for it)*. Deleting a plan you made active sets the row back to automatic,
      except for the share code of a bundled plan: that bundled plan then stays chosen, without *(automatic)*.

### 2.4 A simple fight (10 minutes: a dungeon boss or a trial)

This checks the game event capture outside MitCue's fights. MitCue shows no reminders here. Event capture
for a recording, and the recording itself, only run in a duty or a fight zone: a striking dummy in a town or
the open world records nothing.

- [ ] Before you pull, tick **General** > **Record pulls**. Inside the duty this also switches event capture
      on.
- [ ] Status: *No supported fight in this zone (…)*. The overlay stays hidden unless a preview runs (with
      **Always show** off).
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
- [ ] **Debug** > *Recent own uses* lists your actions `via ActionEffect`. As Scholar, use Whispering Dawn:
      it is listed once, never twice. Usually that is `via ActionEffect`, because your own command's effect
      arrives before the fairy's, and the fairy's then counts as the same use. If the command itself sends no
      effect, it is `via Pet`. Note which one you saw.
- [ ] Type `/countdown 10`: **Debug** shows `countdown 9.6` counting down, then `-` after the pull.
- [ ] The **General** tab shows *Recording to <zone>-<date>-<time>.jsonl (N lines)*, and N goes up. Open the
      file in a text editor: the first line is a `Header` with the zone id and MitCue version. Check that no
      player names appear anywhere in it.
- [ ] **Problems** stays empty.

### 2.5 TOP

Before the first pull (tick **Record pulls** before you zone in):

- [ ] Status: *Ready: Carry the Clear – WHM (57 of 57 entries are yours).* as White Mage, *Ready: LPDU TOP
      Mitigation (31 of 475 entries are yours).* as Scholar. Other values mean another plan or column is in
      use (see the clean-up at the end of §2.3). The overlay shows `TOP - waiting for pull (<plan>)`.
- [ ] **Debug** > *Live session*: `player: WHM level 90, slot H1, column White Mage; entries 57 own` (as
      Scholar: `player: SCH level 90, slot H2, column (any); entries 31 own`), and `plan automatic True`.
- [ ] Type `/countdown 20`: the header shows `TOP - pull in 19.x s` and counts down. The bundled plans have no
      pre-pull calls, so no reminder is listed yet; the first ones appear about 25 s after the pull. Cancel
      the countdown: the header goes back to *waiting for pull*. On a plan with pre-pull calls (such as
      shields), they count down with the countdown, clear when it is cancelled and start over with a new one.

During pulls:

- [ ] At the pull, the header switches to `TOP - P1: … - 0:00` within about a second of combat starting.
      **Debug** > *last syncs* shows a line ending in `(start)`.
- [ ] P1 reminders line up with the mechanics. For any reminder that feels early or late, note the mechanic
      and the difference in seconds.
- [ ] At each phase change (P2 at the Firewall cast, P3, P4, P5 at Blue Screen, P6 at Blind Faith) the clock
      jumps to the new phase. *last syncs* shows `(jump)` lines and the `jumps` count goes up. Reminders from
      the skipped stretch do not fire (with **Show reminders skipped by a phase push** on, they show `skip`).
      A call for the end of a phase turns NOW at the jump that cuts that phase short: on *Carry the Clear –
      WHM + SCH*, the Scholar's *Deploy just before P3 ends*.
- [ ] Holding a long cooldown so that it is not back in time shows orange *ready in N s*. Running out of
      Lilies or Aetherflow shows *no Lily* / *no Aetherflow*.
- [ ] Your presses turn rows into check marks. With **Also show my co-healer's calls** on: as Scholar on LPDU
      next to a White Mage, only the White Mage's columns show (`[H1]` and `[H1 WHM]`, no `[H1 AST]`), and
      those rows are ticked off when the White Mage presses. As White Mage on *Carry the Clear – WHM*, no
      co-healer rows show: that plan has no Scholar column.
- [ ] If you die, the reminders keep running until the wipe.
- [ ] After a wipe: chat shows `TOP pull ended (wipe): N done, M missed. Missed: …`, the overlay goes back to
      *waiting for pull*, and the next pull starts cleanly.
- [ ] Switch plan or column between pulls: the Status line updates. Switching during a countdown or a pull
      only takes effect after the pull. Switch back to the automatic plan and the suggested column afterwards.
- [ ] `/mitcue reset` during a pull clears the reminders without a summary. Note whether MitCue picks the
      fight up again before the pull ends. The next pull must work normally.
- [ ] Optional: `/xlstats` opens Dalamud's plugin statistics. It lists no plugins until you switch tracking
      on: tick **Enable Draw Time Tracking** on the **Draw times** tab and **Enable Framework Update
      Tracking** on the **Framework times** tab. Play for a few minutes, then read MitCue's rows and note
      anything noticeably high.

After the session:

- [ ] Untick **Record pulls**, or leave the zone, so the recording file is closed.
- [ ] Send the recording and a debug report copied right after a problem pull (§2.7), with your notes.

### 2.6 Reading the Debug tab

The Debug tab rebuilds its text four times a second. **Copy to clipboard** copies the whole report.

| Section | What good looks like |
|---------|----------------------|
| **Game** | Logged in, the right zone name, job and synced level. `ActionEffect hook: enabled`. `observed` and `casts seen` go up in fights |
| **Data** | Every fight listed with its zone, level, phases and mechanics. Action catalog and aliases *loaded*. The plan count and the plans folder |
| **Live session** | The fight, plan, player (job, level, slot, column), own and other entry counts. `faulted False`; `rebuild pending True` only while a change waits for the pull to end; `plan automatic` tells whether the plan was chosen for you. Then `mode` (Idle, Countdown, Running), `timeline running`, `time`, `phase` |
| **last syncs** | One line per sync: `[clock] from -> to line N "entry" <sync>`, with `(start)` at the pull and `(jump)` at phase pushes. Large `from -> to` differences outside phase changes mean the sync is off. Report them |
| **reminders** | Every reminder with its state, `press`, `hit`, the `[earliest, latest]` window, the action, the mechanic id and, if used, `used <time>` with `(early)`. `readyIn` and `no-resource` show the warnings. Then `entries not scheduled` (entries whose mechanic the fight doesn't have) and `own uses seen by the scheduler`, with the reminder each use counted for |
| **Preview** | `not running`, or the preview's description, simulated clock and events played |
| **Recorder** | Whether it is enabled and active, the current file, the line count and the folder |
| **Last 50 events** | Enemy casts and abilities, your own abilities, combat changes and resets, with their clock times. Other players' and pets' actions are left out; your pet's show under *Recent own uses* |
| **Recent own uses** | Each detected use of yours and how it was found: `ActionEffect`, `Pet` or `Recast` |
| **Problems** | `none`, or each problem with when it last happened and how often |

Buttons:

- **Reset live session**: the same as `/mitcue reset`.
- **Reload data**: drops the fights, timelines, actions and aliases; they load again on the next frame. It
  stops a running preview. The live session keeps the data it was built with and a running pull carries on;
  `/mitcue reset` rebuilds the session with the reloaded data. Plans are not affected: **Plans** > **Reload
  plans** re-reads them.
- **Clear problems**: empties the list.

### 2.7 Sending a recording

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
