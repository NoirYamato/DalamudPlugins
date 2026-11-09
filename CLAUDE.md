@AGENTS.md

# NoirMech

NoirMech is a fork of [AnoMech](https://github.com/anomek/AnoMech). `AGENTS.md` above is upstream's guide and
stays byte-identical to upstream; this file adds only what NoirMech changes. `git diff v0.5.1.0` shows every
NoirMech change:
- the rename (below);
- TOP P2 Party Synergy: the EU `[LPDU] BPOG/GOPB` strat (`TopP2PartySynergyLpduAi`), listed first in
  `AiStrats` so EU and it are the default pick (upstream's NA strat is index 1), `Group => "NA"` on upstream's
  strat, and no Spotlight in solo runs (a solo stack target stood alone and died). Its eye-spread spots are
  Alninio's: on Remote Glitch the corners stand on the diagonal shurikens (45° and 135° from the eye) and the
  middle rows 15° either side of the side shuriken, all 19.4y out; on Mid Glitch the corners are 35° off the
  eye line at the rim and the middle rows sit halfway between waymarks;
- the user pause (below);
- TOP P3 (below): the Intermission, Hello World and Monitors scenarios with `[LPDU] Light PF` strats, the
  `TopZone.P3` phase, `TopP3Constants.cs` and `TopP3Actions.cs` (P3 ids and actions, kept out of upstream's
  `TopConstants.cs` and `TopActions.cs`);
- TOP P4 (below): the Blue Screen scenario with an `[LPDU] Light PF` strat, the `TopZone.P4` phase,
  `TopP4Constants.cs` and `TopP4Actions.cs` (P4 ids and actions, kept out of upstream's `TopConstants.cs` and
  `TopActions.cs`);
- TOP P5 Delta (below):
  - the EU `[LPDU] Light PF` strat (`TopP5DeltaLpduAi`), listed first in `AiStrats` so EU and it are the default
    pick; upstream's strat is index 1 and gains `Group => "NA"`, the automarkers and a turn by the eye's `Turn` in
    `AdjustEyePosition`;
  - `TopP5DeltaCloseJobs.cs` (below), called once each from upstream's `SeatTethers` and `Validate`;
  - small edits to upstream's Delta State, overrides, settings window and scenario and to
    `Multiplayer/Protocol.cs`: the Automarkers setting, the State's forced-band flags and their replay fields, the
    tether seating order, and the eye on all four cardinals. For the eye, `NorthSouth` gains `Turn`, `East`,
    `West`, `Cardinals` and `WithEffectIndex`; the State rolls one of the four; the scenario turns the beetle, the
    Final, the eye unit and the arm units by `Turn` and, for eye S, mirrors the arms' facing with their spots; and
    the replay's `EyeSpawnIsNorth` became `EyeSpawnEffectIndex`;
- TOP P5 Omega (below): the EU `[LPDU] Light PF` strat (`TopP5OmegaLpduAi`), listed first in `AiStrats` so EU and
  it are the default pick; upstream's strat is index 1 and gains `Group => "NA"`, and its scenario tests pass
  `NaStrat = 1`;
- TOP P6 (below), in upstream's `TopZone.P6` phase:
  - the P6 Gauntlet (`Scenarios/Top/P6Gauntlet/`), all of P6 as one scenario with an `[LPDU] Light PF` strat. It
    takes the place of every P6 entry in `ScenarioCatalog`, upstream's Exasquares/WC2 included: upstream's
    `P6WaveCannon2/` files are as v0.5.1.0 has them, off the menu, and their tests still run because
    `tests/AnoMech.Tests/Support/ScenarioRun.cs` constructs a scenario the catalog lacks;
  - `TopP6Constants.cs` and `TopP6Actions.cs` (P6 ids and actions, kept out of upstream's `TopConstants.cs` and
    `TopActions.cs`), `TopP6Mechanics.cs` (Alpha Omega and the attacks it plays), `TopP6LimitBreaks.cs` (a run's
    limit breaks and their gauge), `TopP6LpduAi.cs` (strat pieces) and `TopP6UnlimitedWaveCannonRows.cs` (settings
    rows);
  - new Core pieces: `SpawnedHelperPool`, `HelperCast`, `ExaflareLane`, `BaitedPuddles` and `Verdicts` in
    `Core/Game/`, `PartyLimitBreakCast` in `Core/Game/Party/`, and `EnemyActionTimes` in `Core/EnemyActions/`;
  - one check in upstream's `RunController.StartRefusal`: a local Start is refused, with the reason, while the
    selected scenario reports `SettingsConflicts`, as the host's Start already is (the Gauntlet's module check is the
    only one a local run can report);
  - `IBattleCharaProxy.CastTargetLocation`, the client's `CastInfo.TargetLocation`, for where the player's own caster
    LB3 goes down: one member there, a getter in `BattleCharaProxy` like its other cast getters, and one line each in
    `PacketRecorder`, `PeerNatives`, `FakeBattleChara` and `FakeCharacter`;
  - a test harness for timelines no catalog scenario plays: `tests/AnoMech.Tests/Support/TimelineRun.cs` and
    `tests/AnoMech.Tests/Scenarios/Fakes/TimelineScenario.cs`;
- the core of upstream's unreleased `30e27cc`, the player's own presses as events, backported line for line so the
  release that carries it merges clean: `Core/Native/Interfaces/PlayerAction.cs`, `IUserActions.Acted`, its four
  `Acted` lines in `UserActions.cs` and `FakeUserActions.Act`. Its multiplayer half and its `ActionRow`/`GameData`
  `IsPlayerAction` change are left out;
- the Automarkers setting (`Scenarios/Automarkers.cs`, On/Off, default On): a party-setup option rather than a
  roll, which the P3 Intermission, P3 Monitors and both P5 Delta strats read to place signs;
- Retry and Drill (below): `Core/Game/RollReplay.cs`, which upstream's `PracticeLoop` owns and reports every run to
  (it keeps a replayed roll off the streak and lets Drill make the restart); upstream's `RunController` is `partial`,
  with Retry in `RunController.Retry.cs` and a `checkSettings` flag on its `StartRefusal`; in upstream's `Plugin`,
  `/noirmech retry` and the Drill tick in `OnFrameworkUpdate`. With them, the fix for Auto-restart replaying one roll:
  upstream's `Game.EnterAndStart` raises `RunStarted` with the params as asked, not with the seed it draws to seat the
  party before a zone-in;
- the refusal to run while AnoMech is loaded (below): `AnoMechPresence.cs` and `AnoMechWatch.cs` (one line each in
  upstream's `Plugin` starts and disposes the watch), and a first check in `RunController.StartRefusal` and in
  `ZoneControl`'s `StartBlockedReason` and `HoldBlockedReason`;
- the setup kept across game restarts and the Role pick Rotate (below): `SetupMemory.cs`,
  `Core/Game/Party/SeatRotation.cs`, `ScenarioSelection.RotateSeats`, and `RunController.Rotate.cs`, whose `PartyFor`
  upstream's `RunController.Start` takes its party from. In upstream's `MainWindow.cs`: "Rotate" is `RoleLabels[1]`,
  which moves every seat's label up one (index 2 is MT; `RolePick` maps both ways), `DrawRoleSelector` goes through
  `RolePick`, `ReconcileSetup` clears Rotate in a session, and `SelectScenario` and `Dispose` keep the setup;
- the main window is `partial`: what Retry, Drill, the AnoMech warning and the kept setup add sits in
  `MainWindow.RunControls.cs` and `MainWindow.Setup.cs`, with one-line hooks in upstream's `MainWindow.cs` (the older
  pause button sits in `MainWindow.cs` itself);
- upstream's FRU P4 Crystallize Time settings window reads its "Your debuffs" pick back from the overrides every
  frame, as every other settings window does, so a kept setup, a replay or a host's settings show there (one line and
  a `using`);
- test support for runs back to back: `FakeFramework` models `RunOnTick` on a clock `FakeGame.Frame` moves,
  `TimelineScenario` takes settings overrides and conflicts, and `tests/AnoMech.Tests/Support/PracticeRun.cs`;
- tests for all of these;
- comment-only edits in six upstream files that dropped mentions of BossMod (expect comment conflicts when
  merging upstream releases).

Keep upstream files as close to upstream as possible so release merges stay easy.

## Rename
`AssemblyName` is `NoirMech`, which gives `NoirMech.dll`, the `NoirMech.json` manifest (InternalName `NoirMech`)
and a Dalamud config separate from AnoMech's. `Plugin.DisplayName` holds the user-facing name; commands are
`/noirmech` and `/noir`. These deliberately keep the AnoMech name:
- `RootNamespace`, every namespace, the solution, project and folder names (`publish.ps1` builds
  `AnoMech\AnoMech.csproj`), and both `InternalsVisibleTo` entries;
- the relay protocol (`X-AnoMech-*` headers, `RelayWire.Version`, capability names, `anomech-probe://`,
  `ANOMECH_RELAY_*`), so NoirMech players can use AnoMech relays;
- log file names and the `# AnoMech build=` header the log rotation parses, ImGui `###AnoMech*` ids, and the
  `.github/workflows/*` (upstream's: `pr-build.yml` runs, harmlessly, on every push to the plugin repo; the
  release workflows can't run there).

NoirMech must not run alongside AnoMech: they install the same hooks and share ImGui window ids. While AnoMech is
loaded NoirMech still loads and hooks, but refuses every zone-in and start and leaves a stay already up (see AnoMech
alongside).

## Repos, branches, releases
- Upstream is `anomek/AnoMech`. Merge only its **release tags** into `noirmech-stable`; its `master` holds
  unreleased work. Take upstream's side of engine conflicts and re-port NoirMech's changes on top (see the
  `Merge AnoMech v0.5.0.0` commit).
- `noirmech-stable` lives on `NoirYamato/DalamudPlugins` as its own branch. The private fork
  `NoirYamato/AnoMech` holds `master` and old PR branches only; never push `noirmech-stable` there.
- Both are public through the plugin repo, so commits use the GitHub noreply address
  `65841618+NoirYamato@users.noreply.github.com` (set as `user.email` in both checkouts).
- Releases come from a sibling checkout of `NoirYamato/DalamudPlugins` (`main`) with its `publish.ps1`. It
  expects this repo at `..\Anomech` on `noirmech-stable` with a clean tree, builds Release with
  `-p:Version=<csproj major.minor.build>.<n>` (`n` = previous revision + 1, reset to 1 when the base changes),
  copies the zip, manifest and a `git archive` source zip into `plugins/NoirMech/`, then commits and pushes
  `main`. It never pushes `noirmech-stable`: push source commits separately. Don't bump `<Version>` by hand; it
  tracks the upstream base.

## Build and test
- .NET 10 SDK. `dotnet build` from the repo root gives `AnoMech/bin/x64/Debug/NoirMech.dll`; load it through
  Dalamud's Dev Plugin Locations with any installed NoirMech or AnoMech disabled.
- `dotnet test` runs both test projects. For a soak, pass
  `-- 'TestRunParameters.Parameter(name="SunnySeeds", value="2000")'` from Git Bash (PowerShell 5.1 strips the
  inner quotes) and filter with `Name~Synergy` (NUnit filters can't contain spaces). The soak runs under the
  catalog name, so a two-word scenario needs each word: `Name~Blue&Name~Screen`, not `Name~BlueScreen`, which
  only matches the scenario's own test classes.

- Heavy runs (a 2000-seed soak, the P3 Hello World human tests) take 5-15 minutes each. Run one at a time and
  cap them (`-- NUnit.NumberOfTestWorkers=4`, low process priority): several in parallel have frozen the
  development PC.

## LPDU strat timing
Moves are timed like upstream's NA strat; the symbol flex at 8.5s is the only extra step. The flex has no
`arrivalTime` so it is done before the dodge is planned: `AiManager`'s deferred `MoveTo` can't be cancelled and
measures its delay from where the bot stands when the move fires, so a flex still walking at 11s made the
dodge's 14.8s deadline lose a bot in 21-29% of seeds (a 10s flex). Keep each `arrivalTime` before the next move,
and give every strat a `Group`. `selectedAi` is an index into `AiStrats`, and the main window opens a scenario on
the first strat and its region unless `SetupMemory` kept a pick for it, so the order sets the default. Reorder only
after updating every test that passes a strat index (the scenario's own tests, `GamePauseTests`); the index isn't
saved across sessions (a kept pick goes by name), but it is sent to multiplayer peers, so both sides need the same
build.

## Pause
`/noirmech pause|resume|toggle`, the main-window Pause/Resume button and the title-bar icon toggle
`Game.UserPaused` through `Game.Pause`/`Resume`/`TogglePause` (all via `Framework.Run`).
- Refused with a chat line (`Game.PauseRefusal`) when there is no run, in a multiplayer session (only `Local`
  runs pause), or during the death freeze (`Paused`). Redundant requests do nothing.
- While halted (`IsHalted` = user pause or death freeze) the scheduler, children and `IScenario.Tick` don't
  run. In Local runs `SimWorld.BeginHalt`/`HoldHalted`/`EndHalt` hold the scene: `SimCast` writes the native
  cast time back every frame (near the end it holds 0.45s short so the omen stays drawn), statuses are resent so
  their timers stand still, bots and NPCs get one settle move to where the sim stopped them, and
  `ActorFreeze` stops the animations of every enemy and doppel (never the local player). Both halts hold the
  scene; only the user pause also locks the player.
- The player can walk but not act (`ILocalPlayerInput.ActionsBlocked` in the UseAction detours); auto-attacks
  stop. `UserActions` skips handler timers except those with `RunsWhilePaused` (`CastInterruptHandler`). On
  resume the player's activity is sampled before any event runs; standing outside the arena ring kills on the
  first resumed tick.
- `ActorFreeze` hooks the engine's animation-speed recompute. The hook is created on the first freeze and
  enabled only while its entity-id set is non-empty; its signature is optional, so a miss just leaves actors
  animating.
- A paused run doesn't count toward the streak. Known gaps: knockbacks and carries finish during the pause,
  BGM runs on, short head-marker VFX can expire, and GCD and cooldowns keep running.
- `tests/AnoMech.Tests/Core/Game/GamePauseTests.cs` covers this headlessly.

## Retry and Drill
`Core/Game/RollReplay.cs`, owned by upstream's `PracticeLoop` (`Plugin.Practice.Rolls`), which reports each run's
start to it, and its end while Drill is on. `MainWindow.RunControls.cs` draws both.
- Auto-restart rolls fresh. `Game.EnterAndStart` draws a seed before a zone-in to seat the party on the run's stream,
  and `RunStarted` reports the params as asked (`StartScenarioNow`'s `asked`), so that seed no longer sticks to every
  Auto-restart after a Start from the inn. A seed the player asked for (the debug build's Seed field) still repeats.
- `RollReplay.Last` is the last local run as it played (`PlayedRun`): `Fresh`, what it started from; `AsPlayed`, the
  same with the seed it played (`World.Rng.Seed` at `RunStarted`) and the player's seat pinned, Solo kept; `Settings`,
  its scenario's overrides as `ScenarioSettingsSync` serialized them as it started. `Replay()` puts those settings
  back on the scenario, where its panel shows them, and returns a copy of `AsPlayed` that `RunStarted` hands back by
  reference, which makes the run a replay (`Replaying`). A fresh roll after a replay starts from its `Fresh`.
- Retry: "Retry this roll" beside Start (or Stop and Pause during a run), `/noirmech retry` and `/noir retry`, in
  `RunController.Retry.cs`. `RetryRefusal` refuses, with a chat line from the command and as the button's tooltip:
  first while AnoMech is loaded, then in a multiplayer session or stay, with no local run yet this session, in another
  zone's stay, and wherever Start would, asking `StartRefusal` with the last run's selection.
- Retry puts the run's settings back before it starts, so a conflict in the live settings refuses it only when they
  are the run's own (`StartRefusal`'s `checkSettings`). `Retry` asks `StartRefusal` again once `Replay()` has put them
  back, so a conflict they don't fix (the P6 Gauntlet's module check) still refuses before anything zones in, and
  leaves the run's settings in place. It starts through `EnterAndStart` as Start does, zoning in from the inn and
  waiting out a settle, and the window first selects the retried scenario.
- Drill (beside Auto-restart, in every build; forced off in a session): a run in which anyone died plays its roll
  again once it freezes or, when godmode kept everyone up, once it ends; a clean run moves on to a fresh roll. The
  freeze is polled (`Rolls.Tick`, after `Game.Tick` in `Plugin.OnFrameworkUpdate`), so Drill ticked on during a
  freeze replays at once. Deaths switch Auto-restart off but not Drill, and with Drill on Drill makes the restart. A
  run stopped before it froze isn't played again.
- A replayed roll (Retry, or Drill's repeat) doesn't add to the streak; a death still resets it.
- `PracticeLoopTests` and `RollReplayTests` play runs back to back on a `TimelineScenario`
  (`tests/AnoMech.Tests/Support/PracticeRun.cs`), Drill in a host's stay included; `RunControllerRetryTests` covers
  Retry's refusals and their chat lines. `ZoneControl.StartBlockedReason` can't pass headless (it reads "not logged
  in"), so no test reaches Retry's second ask, after the settings are back.

## AnoMech alongside
- `AnoMechPresence` decides from plain data (`InstalledPlugin(InternalName, IsLoaded)`) whether AnoMech is loaded.
  `AnoMechWatch` reads `PluginInterface.InstalledPlugins` at load and on `ActivePluginsChanged` (on the framework
  thread) and prints one chat line each time it finds AnoMech newly loaded; the main window shows the reason in red.
- While AnoMech is loaded, `RunController.StartRefusal` and `ZoneControl.StartBlockedReason` refuse first, with that
  reason (`Refusal`, and `StartBlock` worded as the start gate's other reasons). Every zone-in and start asks the
  latter: Start, Retry, Drill, Auto-restart, the multiplayer start check, `ScenarioSession` and `ZoneControl.Enter`.
  The debug send hold is refused the same way (`ZoneControl.HoldBlockedReason`).
- NoirMech's guard and firewall act only in its own stays. Every trip check, `GuardTerritoryChanged` and
  `GuardLogout` need `guardArmed`, which only `ZoneControl.Enter` sets; the rest of `TickGuard` times NoirMech's own
  start gate; the firewall hooks are on only in a stay and under the debug send hold. Its always-on hooks act only
  for its own sim (the UseAction block in its user pause, `UserActions` while its own map is in an instance, steering
  when its sim steers) or only log. So while AnoMech runs its sim and NoirMech has no stay, NoirMech stays inert. The
  overlap is a NoirMech stay already up when AnoMech loads: a zone load of AnoMech's could trip NoirMech's guard (a
  zone transition or a territory drift under its firewall), which stops the game.
- On detection the watch ends such a stay and releases a debug send hold. A local stay, or a host's, ends through
  `RunController.Leave` (the inn reload and the verified lift); a host's party goes back to the inn with it, since the
  host's sim is theirs. A peer leaves its session and its own stay at once, as Disconnect does: its Leave would only
  ask the host to end the run, and its own stay would stay up until the host answered. The host then ends the run it
  lost a player from, as it does for any peer that leaves mid-run.
- `AnoMechPresenceTests`, `AnoMechWatchTests`, `RunControllerTests`.

## Setup memory and Rotate
- `SetupMemory` keeps the main window's setup in `setup.json` in the plugin's config folder
  (`%APPDATA%\XIVLauncher\pluginConfigs\NoirMech\`), not in upstream's `Configuration`: the Role pick by label, and
  per scenario (by `FullName`) its region, its strat by name and its settings overrides as `ScenarioSettingsSync`
  serializes them, which leaves out the internal test pins. At load `MainWindow.RestoreSetup` puts back the Role pick
  and each scenario's region and settings, before the last scenario is selected again; a scenario's strat comes back
  by name as it is selected, and one it no longer has in that region leaves the region's first.
- `MainWindow.RememberSetup` runs every frame from `PreOpenCheck` and saves what changed (the settings are read every
  0.5s), and before `SelectScenario` switches scenario, so a setting changed just before is kept for its own. On
  unload and after a session it also reads every kept scenario's settings again (`SetupMemory.RememberSettings`); a
  scenario never shown has no entry, since only the shown one's settings can be changed.
- A multiplayer session overrides part of the setup, and `SetupMemory` keeps that part out of the file (`Track` tells
  it the window's session every frame): the Role pick, which a session resets, for everyone; and everything of a
  scenario for a guest, who plays the host's scenario, strat and settings. A host's region, strat and settings are
  kept as they change. When the session ends `Track` says so once, and the window puts back the kept Role pick and,
  for a guest, the selected scenario's region and strat; a guest's own settings come back through
  `MultiplayerManager`'s restore, as before.
- Every settings window reads its picks back from the overrides every frame, so a kept setup, a replay or a host's
  settings show there. Upstream's FRU P4 Crystallize Time window alone kept its "Your debuffs" pick in a field and
  showed "Random" over restored debuffs; it reads the pick back now too.
- Rotate, a Role pick: tanks alternate MT and OT and melee M1 and M2 on each local run, from the seat Auto gives;
  healers and ranged keep their job's seat. Start seats the run through a `RotatingPartyProvider` on
  `RunController.Seats`, asked again for every run started from it, so Auto-restart and Drill's fresh rolls rotate
  too; the rotation moves on as such a run starts (`RollReplay.Started`). A replay keeps the seat it played and leaves
  the rotation alone. Multiplayer seats stay the Party panel's. Beside the pick the window shows the seat the next
  Start gives on the current job ("You: M2").
- The P6 Gauntlet builds its limit break plan each run from the seats and the human's client job, so a rotated tank
  or melee gets its duties by job priority against the bot beside it (a PLD: Cosmo Memory and Magic Number 1 in MT
  beside the OT PLD bot, Magic Number 2 in OT beside the MT WAR bot). Every seat Rotate gives fits the job, so the
  chat line names the duties (`TopP6GauntletLimitBreakPlanTests`).
- `SetupMemoryTests` (every catalog scenario's settings, off their defaults, survive a save and a load; what a
  session keeps) and `SeatRotationTests`.

## TOP P3
`Scenarios/Top/P3Intermission/`, `Scenarios/Top/P3HelloWorld/` and `Scenarios/Top/P3Monitors/` in the
`TopZone.P3` phase. Their ids are in `TopP3Constants.cs`. Values nobody has measured (weather, tower ring
radius, rot contact radius, tether break distances, the void zone's radius and spawn args, some durations) are
marked UNVERIFIED there; tune them in that one place.
- Intermission runs from the end of P2 until Omega turns targetable: Wave Repeater rings, Colossal Blow arms
  (the set that warps in first goes off first), Sniper Cannon spreads and stacks, and the void zone. Every
  mistake kills on the spot with a reason. Its one strat has an Automarkers On/Off setting (default On); it
  lives in the overrides and the State, and only `TopP3IntermissionLpduAi` reads it, to add signs: both
  settings put everyone on the same spots. Change bot movement only with `TopP3IntermissionLpduHumanTests`
  and `TopP3IntermissionLpduMarginTests` green.
- Hello World tracks each player's debuffs itself (pairs rotating through defamation, remote tether, stack and
  local tether; rots spreading on contact; Debuggers as immunity) and decides every kill with a death
  explanation. Fixed events are absolute time literals in `Run`; expiries that depend on play run from
  `IScenario.Tick`.
- Its bots (`TopP3HelloWorldLpduAi` with the `TopP3HelloWorldTraffic` planner) rest only on LPDU strat spots.
  They plan each walk round everyone's live positions and debuffs, give the human more room than each other
  (`HumanSwapClearance`), step aside for it, and hold their spots through each resolve. The bots-only soak
  can't see human conflicts, so change bot movement only with `TopP3HelloWorldLpduHumanTests` green: it
  scripts the player's seat through every kit, seat, walking style and reaction delay.
- Monitors: every monitor, the boss's included, hits 2 random players on its side (`CharacterFind.OnSideN`,
  as P5 does); a monitor with nobody on its side hits random players. Bots aim theirs by turning after they
  arrive. The run starts at Critical Error's cast, as the fight's lead-in does: bots stack west of Omega, line
  up on the west arc once it resolves, and hold the line until the monitors appear at 19.4s. With Automarkers
  On its strat marks at 19.5s: Bind 1-3 on the monitor players and Attack 1-5 on the rest, each in line-up
  order.

## TOP P4
`Scenarios/Top/P4BlueScreen/` in the `TopZone.P4` phase, which keeps P3's weather and BGM 950. Its ids are in
`TopP4Constants.cs` (it reuses P3's Omega and Wave Repeater ids); unmeasured values and rules are marked
UNVERIFIED there and in the scenario.
- The run starts 3s before Omega's Wave Cannon cast and ends after the last Wave Repeater ring; the Blue Screen
  enrage isn't played. Three rounds: each aims a line from the centre at every player, fires the beams down
  those fixed lines 0.6s later, resolves the repeats 5.3s after the aim, then the two line stacks toward their
  marked players; Wave Repeater rings run through rounds 2 and 3. The repeats cast from the aim, but their orange
  lines only flash, 4.4-4.8s into the cast (`P4Duration.RepeatOmenDelay`, UNVERIFIED; past 4.55s a pause would
  hide them).
- Every mistake kills on the spot with a reason: two beams or a repeat line, both line stacks, a line stack with
  fewer than 4 unless only tanks are in it (UNVERIFIED), a ring band, or the arena edge. A player dead since the
  aim fires no beam, and a dead marker fires no line stack.
- The markers are rolled per set. The settings force or forbid a marker per seat per set (at most 2 Yes and 6 No
  in a set), and a row per set puts both markers in one light party (forcing the strat's flex) or one in each. A
  row on Auto rolls like the seat rows alone, and no row changes another set's roll.
- One strat, EU `[LPDU] Light PF`; no solo or multiplayer. The bots' spot radii (13.5 / 11 / 13 / 11 / 10.5)
  and the 20° stack-2 bearing in `TopP4BlueScreenLpduAi` are UNVERIFIED. Change bot movement only with
  `TopP4BlueScreenLpduHumanTests` and `TopP4BlueScreenLpduMarginTests` green.

## TOP P5 Delta
Upstream's `Scenarios/Top/P5Delta/` with NoirMech's EU strat in front; tests pass upstream's as `NaStrat = 1`.
- Inner pairs: in each colour the only tether pair holding a healer lines up inner. A tie (both pairs or
  neither) keeps the roll's inner pair, so the bots' pick is random and a human follows them. A seat that forced
  an inner or outer band in a colour (Close/Far Inner/Outer; Beyond Defence = Yes counts as Close Inner) hands
  that colour to the roll (the State's `CloseInnerOuterForced` / `FarInnerOuterForced`); Any choices and the
  Monitor and Hello World requests (which become Close any) force nothing. `SeatTethers` seats inner and outer
  requests before Any ones, so an Any seat can't take a forced band's slots.
- The monitor and both Hello World tethers only go to the four close seats. When seats refusing them (Monitor or
  Hello World = No, or the other Hello tether's request) would fill the close four, `TopP5DeltaCloseJobs` swaps
  seats that asked for no band between the close and far slots until every job has a taker, drawing from the
  roll only then, and Validate flags settings no seating can honour.
- Beyond Defence = No is not a request: when both members of the pair the rule puts inner refused, one of them
  still takes Beyond Defense, because the scenario honours a refusal only while one of the two closest can take
  it.
- N/S inside a pair is the roll's (even slot north), since the sources disagree. The inner pair swaps N/S when
  its N member's fist matches the outer N member's, compared by role.
- Blue variant B: the inner blues stretch 13y apart, pop as the tethers go live (28.1s) and drop onto the outer
  blues; the outer blues break walking out to the NW/SW arms. Upstream's strat is variant A.
- The eye spawns on any cardinal, each a quarter of the time (how often the game picks each is UNVERIFIED).
  - E and W: the scenario turns the beetle, the Final, the eye unit and the arm units a quarter
    (`NorthSouth.Turn`), and both strats turn their moves with them.
  - S keeps upstream's `Mul`. The scenario negates the beetle's, the Final's and the eye unit's spawns with it (a
    half turn) but mirrors the arm units east to west, which lands them on the same six spots. Upstream's strat
    mirrors x and multiplies its side flips by `Mul`.
  - `TopP5DeltaLpduAi` writes its spots with the eye north and turns them by the eye's bearing
    (`IAiPositions.Rotate`), picking the arms by their bearing in that frame.
  - The E/W eye MapEffect slots (3 and 7, P2's scheme) are UNVERIFIED. `TopP5DeltaEyeTests` checks each eye's
    spawns, hit areas and replay.
- The moves follow the fight's cues rather than upstream's timings. The line-up goes at 11s, about a second after
  the tethers appear, so a human in a healer tie sees the bots' pick long before the fists (18.25s).
- With Automarkers On both strats mark at 10.5s, just after the Hello World debuffs land: a Triangle on Near
  World and a Cross (the + marker) on Distant World.
- The forced-band flags, Automarkers and the eye's `EffectIndex` travel in `TopP5DeltaAiReplayStateMessage`, so
  a peer's replay decides the same way. Upstream's unreleased `4bb8b6c` removes that replay layer: drop those
  fields with it, and rework the tests that read `LastState`.
- UNVERIFIED: the 10y tether break distance (P3 uses 12 / 9; the 13y stretch pops under both), the Swivel
  Cannon's apex on the beetle, and Omega-M's facing relative to the eye. Omega-M keeps its absolute facings for
  every eye (north at spawn, south at 28.2s, north at 54s), and no mechanic reads them.
- No source gives these spots, in the eye frame as `TopP5DeltaLpduAi` writes them: the unsafe inner green's wait
  (-9.6, -3.4), the green staging (0, ±12) / (9, ±10), the outer greens' walk-together (±4, -0.5), the Beyond
  Defense target's spot 13y toward the Final from its stack spot, and the opening and end clumps (upstream's).
- Not wanted: a kill for moving at the fist snapshot, keeping the Hello holders on different blue pairs, and
  renaming the settings' Close/Far.
- Change bot movement only with `TopP5DeltaLpduHumanTests` and `TopP5DeltaLpduMarginTests` green.

## TOP P5 Omega
Upstream's `Scenarios/Top/P5Omega/` with NoirMech's EU strat in front; tests pass upstream's as `NaStrat = 1`.
- `TopP5OmegaLpduAi` is upstream's strat written out again with its two Hello World layouts moved onto the user's
  strategy board (its OMEGA pt 1 and pt 2 panels), and with the tether takers fetching the Blaster tethers (below).
  Everything else is upstream's: the moves and their times, arrival times and jitters, the assignment lists, the marks
  and their times, the opening clump and both Omega dodges.
- First Hello World: Distant World stands south, (0.2, 10) for the monitors firing west and mirrored for east,
  where upstream's stands north. That reflects upstream's layout north to south, with monitors 1 and 2, jumps 1
  and 4 and jumps 2 and 3 trading places, so it resolves the same: the far jumps now go to jump 1 and then jump 4
  (upstream's: 4, then 1), and the 0.2 still keeps the holder off the monitors' half.
- Second Hello World: upstream's spots mirrored across the beetle's line. With the beetle north: Near (0, 10),
  Distant (-10, 0), tethers 1 (9.5, -16.5) and 2 (-9.5, -16.5), jumps (19, 0), (4, 19), (-4, 19), (-19, 0). Every
  jump goes to the nearest or farthest member, and the tether takers still pick up the passable Blaster tethers
  in the middle at 48s, so the mirror resolves as upstream's spots do.
- The takers also fetch a Blaster tether a non-taker still holds. A tether passes only to someone within 0.5y of its
  line and ahead of its holder, so a holder more than about 0.6y off the beetle's axis keeps it, and Blaster on a
  non-taker wipes the party.
  - Every 0.25s from 48.25 to 56.75, a tether on a non-taker gets a fetcher unless a free taker stands in its line or
    is walking into it. The fetcher is the free bot taker (never the player's own seat) with the shortest fetch plus
    walk to its rim spot. It heads 1.5y in front of the holder toward the beetle (halfway when the holder is within 3y
    of it), aimed where the holder will be up to 2s on and kept within 19.95y of the middle (the arena kills beyond
    20y), and sprints from more than 5y.
  - The gather lasts until upstream's 53s walk-out, which runs after that time's fetch. Until then a bot holder counts
    only once it stands on its gather spot; a human or remote player counts wherever it is.
  - The walk-out gives a fetcher no spot. As soon as it holds a tether it walks to its rim spot, sprinting if it must
    to arrive by 56.75. It goes round the beetle's side of the middle when its straight walk would cross the beetle's
    line less than 1y in front of the middle (through or behind the stack), and doesn't set out while a free non-taker
    stands within 0.8y of the line from its rim spot to the beetle, unless it must leave; one already on its way keeps
    going. Without a tether it goes back to its gather spot until the walk-out, then to its rim spot.
  - Once either taker has been sent, the walk-out makes both bot takers fetchers: a taker sent from their shared spot
    usually takes its partner's tether as it steps off, and the partner must then be free to fetch. When neither has
    been sent, both keep upstream's walk-out and can be sent only once it has set them off for their rim spots, at
    about 54.2, so a tether a human takes from a taker after the walk-out waits for its fetcher until then.
  - With bots alone no taker is ever sent. A peer's replay mirrors the tethers but seats no bots, so only the host's
    bots fetch and a debug-bot peer keeps upstream's moves.
  - The human suite gives a non-taker human a tether and gathers it 0.5-3y from the middle in any direction. Scripted
    rolls add a human holding its tether beside the beetle or against the wall near it until 55, one standing with
    its tether in the line from a fetcher's rim spot until 53, one holding it behind the middle so its fetcher must go
    round the stack, and one taking a taker's tether as the gather ends or after it. In every human test no fetcher
    may take upstream's walk-out. A human who stays on its first Hello World spot until 53 or walks straight to its
    second isn't covered in general: their own walks still fail about 1 roll in 30 and 1 in 300 (late to the second
    Hello World, through a Blaster or a taker's line, or a curving walk off the rim), and now and then a taker bringing
    a tether back from the far rim after 53 loses it to a stack bot walking out and misses its rim spot at Blaster.
- The board draws pt 1 with the monitors east and pt 2 with the beetle west. The strat keeps upstream's distances
  (the user's pick); the board's own spots lie within a few degrees of them and up to about 1y closer in.
- Upstream's dodges put the two stacks 19.7y apart when one pair is Staff and Sword and the other Staff and Shield,
  with 3.9s between the first set's snapshot and the second's: a human who walks it must leave within about 0.9s
  (from 1.1s it can be caught). The human suite's walkers react up to 0.9s late and its sprinter 1.2s.
- Change bot movement only with `TopP5OmegaLpduHumanTests` and `TopP5OmegaLpduMarginTests` green.

## TOP P6
`Scenarios/Top/P6Gauntlet/` in upstream's `TopZone.P6` phase: all of P6 as one scenario, "P6 Gauntlet", from 1.9s
before Cosmo Memory's cast (Alpha Omega already targetable) through Run: ****mi*, the enrage. One strat, EU
`[LPDU] Light PF` (`TopP6GauntletLpduAi`), following the raidplan mh3bX4KJ9d8rT9nk; party-only, no solo or
multiplayer.
- One clock, G = abs - 1159.9, played in parts (`TopP6GauntletParts`), each on its own clock from its start with its
  first boss cast 1.9s in: Cosmo Memory 0, Exaflares 1 48.6, Cosmo Arrow 2 90.9, Exaflares 2 124.9, Cosmo Meteor
  164.8, Magic Number 199.9. In G:
  - Cosmo Memory 1.9 (decides 7.6), autos 12.0, Cosmo Arrow 16.3, Cosmo Dive 31.4 (hits 39.4), autos 46.2;
  - Unlimited Wave Cannon 50.5 (first explosion 62.5, puddles 60.5 to 70.0), Wave Cannon 73.1 (lasers 76.1 / 78.1,
    Wild Charge 84.4), autos 88.5;
  - Cosmo Arrow 92.8, Wave Cannon 107.0 (lasers 110.0 / 112.0, Wild Charge 118.3), autos 122.4;
  - Unlimited Wave Cannon 126.8 (first explosion 138.8, puddles 136.8 to 146.3), Cosmo Dive 144.5 (its bar ends
    149.8, hits 152.5), autos 159.3;
  - Cosmo Meteor 166.7: baits 171.7, adds shown 174.9, spreads 176.8 / 177.8 / 182.8 / 183.8, flare markers 186.7,
    the comets' enrage 187.0, flares and stack 194.8, the meteors' enrage 197.1;
  - Magic Number 201.8 (decides 206.5, its mark 208.1 to 214.1) and 217.9 (222.6, 224.2 to 230.2); Run: ****mi*
    232.0, its bar ending 247.7 and its damage at 262.0.
- `TopP6AlphaOmega` (`TopP6Mechanics.cs`) is the phase's one Alpha Omega and plays every attack; Cosmo Memory's
  statuses and the gauge come once. The Cosmo Arrow 2 part is upstream's Exasquares/WC2 rebuilt from its
  `CosmoArrow` and `WaveCannon` steps.
- `TopP6AlphaOmega` spawns a pool of 36 invisible 9020 helpers; every helper cast goes through its `CastFromHelper`.
  Once upstream's zone owns a helper pool and `SimEnemy.CastFromHelper`, that method's body becomes
  `Enemy?.CastFromHelper(...)` and the pool field and `helpers.Spawn()` go. Keep the 9020 id in `TopP6Constants` and
  stay within 36 busy helpers (`TopP6HelperBudgetTests`, the whole Gauntlet included).
- Start from (default: the whole phase) picks the first part. A later start spawns Alpha Omega, puts the party, the
  human too, on the autos' spots, leaves Cosmo Memory's statuses as the run so far would (Spark of Dynamis on the
  seats whose limit break the plan pressed before, Brilliant Dynamis on the rest, both from the first frame, with the
  gauge, so a press in the first second gives it back; Code: ****mi* on Alpha Omega a second in), sets the gauge the
  rebuild has made by then, and plays the part on its usual clock. Tests pin a part range with
  the internal `Parts` override. The other settings force both Cosmo Arrows, both Unlimited Wave Cannons, each
  seat's flare and the human's own limit break row; only the row of the local player's role shows.
- `TopP6GauntletLimitBreakPlan` says who presses each of the phase's ten limit breaks 3, from the eight seats' jobs
  (the bots' and the human's client job) and the human's rows:
  - tanks by WAR > DRK > PLD > GNB: the higher takes Cosmo Memory and Magic Number 1, the other Magic Number 2;
  - melee by SAM > DRG > MNK > VPR > RPR > NIN (VPR's place UNVERIFIED): the higher, the enrage holder, goes first
    after Cosmo Dive 2's bar and takes the enrage; the other goes second, from the middle, while the MT steps back;
  - H1 Magic Number 1, H2 Magic Number 2; R1 the physical ranged LB3 through both meteors, R2 the caster LB3 on the
    comets;
  - a tie goes to the human (a DRG player in M2, or a MNK in M1, is the enrage holder), between two bots to the
    earlier seat. A row (tank, melee, healer; Auto by default) forces the human's share, and the pair's other seat
    takes the rest; every split keeps the two dive melees, the two Magic Number tanks and the two Magic Number
    healers on different players, since each player's Brilliant Dynamis gives the gauge back once;
  - a seat whose job can't press its limit break (through the Role setting) counts as its bot's job: the run presses
    its duties at the bot's times and holds the human to none of them;
  - the start's chat line names the human's duties in the parts the run plays.
- The plan is the one piece of strat the scenario reads, for three things: the human's melee windows, the chat line
  and, on a later start, which seats already carry Spark of Dynamis (`SparkedBefore`). The strat reads it for its
  presses and the second melee's walk to the middle. Nothing else in the scenario or the State branches on the strat.
- `TopP6LimitBreaks` holds one run's limit breaks: every press, bot or human, and the gauge, which it owns and sends
  to the HUD (`LimitBreakGauge.Spend` empties it whatever the level).
  - The gauge holds 3 bars at Cosmo Memory's cast, rebuilds evenly from Cosmo Memory's bar end to full by Cosmo Dive
    2's (UNVERIFIED), and nothing else fills it. Cosmo Memory's hit gives everyone Brilliant Dynamis (3446): a
    player's first limit break, any level, resolving with it gives back what it spent and becomes Spark of Dynamis
    (3448); death takes both. Every duty after Cosmo Memory's but the enrage's refills the gauge, so a wasted press
    (twice, at another seat's duty, or at the wrong level) leaves a later duty without one, and its mechanic kills.
  - The human's presses come through `IUserActions.Acted`, outside `Game.Tick` and before the client's module spends
    the gauge, so they settle on the next `Tick`, or sooner when an event asks; every mechanic that decides or stops
    listening at a deadline settles them first (`CatchUp`), so a press heard just before still counts. A press the
    client reports aimed at its "current target" stand-in (0xE0000000; UNVERIFIED that one does) is read as aimed at
    the player's target (`Plugin.TargetManager`). Each run makes its own service, hearing that run's
    `Natives.UserActions`.
  - Bots press only their own duties, at the plan's times: Cosmo Memory 4.9, the first melee 150.2, the second from
    the middle once the first's has landed (156.9 at the earliest, at the latest as the first's window closes at
    161.0, then waiting out one still casting), R2 178.3, R1 188.8 after turning south, Magic Number 1's tank 202.5
    and healer 208.8, Magic Number 2's 218.6 and 224.9, the enrage holder 233.0. They cast for real through
    `PartyLimitBreakCast`, whatever "Require tank mitigation" says: the tank instant, the healer 2s, the rest 4.5s,
    0x47 as the cast starts and the resolve as it lands, aimed at Alpha Omega when the limit break takes a target. A
    press waits while another limit break casts or the gauge isn't full and gives up once its mechanic has gone off.
    A bot stands still through its cast; the lock after it isn't played.
- The checks count whoever pressed; only the melee rule is the human's alone. The tank, mark and Cosmo Meteor hit
  checks are dealt through `Verdicts`, the adds' and Run: ****mi*'s enrages through `SimParty.WipeAllPlayers` and the
  melee rule through `SimPlayer.Die`; godmode reports each player once per hit, and when a waiting press found the
  gauge short the reason says so.
  - Cosmo Memory and both Magic Numbers kill everyone without a tank LB3 status (196, 863, 864, 1931) up 1.0s
    (UNVERIFIED) before the hit decides. Not `Severity.MinMit`, which checks only a human, and only with "Require tank
    mitigation" on.
  - Magic Number's mark (3532) lands on everyone 1.3s after the hit and runs 6s (both UNVERIFIED). `TopP6AlphaOmega`
    holds who carries it, since a status that runs out vanishes silently: a healer LB3 resolving while it is on lifts
    it from all, and as it runs out it kills whoever still carries it.
  - The comets: a caster LB3 resolving while they are shown kills each comet its r 15 circle holds, counting their 1y
    hitbox, so one centred within about 3y of Alpha Omega holds all six. The human's goes down where its client put it,
    read from the cast as it is heard (`CastTargetLocation`; UNVERIFIED that the client fills it while the sim runs);
    with no point, or one more than 26y from the player, it holds all six. The run's own goes down on its target, Alpha
    Omega. One alive at 187.0 enrages and kills everyone.
  - The meteors: a physical ranged LB3 kills each meteor its 30 x 8 line holds, drawn from the presser toward its
    target with the presser's hitbox and counting the meteors' 3y. The run's own press for the player's seat (a human
    whose job can't press it) reaches both, wherever the player stands. One alive at 197.1 casts 31669 and kills
    everyone.
  - Either enrage's reason says how many of its adds the human's own LB3s caught in all, once one went off while they
    showed.
  - The enrage: a melee LB3 on Alpha Omega whose own cast bar ends while Run: ****mi*'s is up, 232.0 to 247.7
    (`TopP6LimitBreakPress.LandsAMeleeLimitBreak3On`, by `LandsAt`, since the human's resolve is heard a frame or two
    later), defeats Alpha Omega once it resolves (`SimEnemy.Defeat`); otherwise its damage kills everyone at 262.0. One
    started up to a cast before the bar counts, one landing before it is wasted, and one the bar's own frame settled
    before Run: ****mi* listens is counted as it starts listening. Whether the gauge was short is read as the bar ends.
  - Melee (the user's rule, the human only): for each of its duties the human's melee LB3 must start in the window,
    aim at Alpha Omega and land, or the human dies as the window closes, or as the cast is cut off if that is later.
    The first: from Cosmo Dive 2's bar end (149.8) to 161.0; the second: from the first's landing to 165.5. The
    enrage's (`TopP6MeleeWindow.LandsBy`) counts the presses that defeat Alpha Omega, by the same call: started 227.5
    to 243.2, landing while the bar is up. A press landing before the bar is wasted, so the window kills at 243.2 and
    Run: ****mi* then wipes. The bar's end is the Gauntlet's cut-off (the fight's is the damage). A window remembers its
    target's id, since Alpha Omega despawns 9.7s after falling, which can be before the enrage's window closes. Bots'
    melee LB3s aren't held to windows.
  - Death reasons state the fight's rule or the Gauntlet's, never a strat spot.
- Every other mistake also kills on the spot with a reason. Exaflares, puddles, the bait, exasquares, lasers and Wild
  Charge kill through their actions. The rest is decided in code from where everyone stands as the hits land, those
  actions only showing them: Flash Gale (a non-tank target, or within 5y of one), Cosmo Dive (a non-tank buster
  target, within 8y of one, one in the stack, a stack under 6), the meteor spreads (in another's, or one's own while
  still vulnerable), and the flares and stack (within 20y of a flare, a flare in the stack, a stack under 5).
  `Verdicts` keeps each player's first reason.
- The Gauntlet always needs limit breaks. It refuses to start with "Resolve your own actions" off (a
  `SettingsConflicts` entry, which greys out Start with the reason as its tooltip and shows in red atop its
  settings): only that module plays the human's own limit break in the sim. A headless run the debug driver walks is
  exempt, and there the strat presses the player's seat's duties too, unless a test sets the internal
  `PlayerPressesItsOwnLimitBreaks` and scripts them through `FakeUserActions.Act`.
- The strat plays the old blocks' LPDU spots and times on the parts' clocks, changed only at the seams:
  - no opening walk to the autos' spots in Exaflares 1, Exaflares 2 and Cosmo Meteor (a later start puts the party
    there); Cosmo Arrow 2 starts from the autos' spots;
  - both Unlimited Wave Cannon dodges are timed from their wave's `FirstExplosionAt` (62.5, 138.8);
  - at 119.0, after Wild Charge 2, everyone walks back to the autos' spots: the line leaves a non-tank farthest for
    the 123.3 Flash Gale;
  - after Cosmo Dive 2 the plan's second melee, not a fixed M2, waits in the middle, and presses once the first's has
    landed, so a human first melee may use its whole window. The OT takes (16, 0) for those autos, 2y past the
    party's hold (r 14), so a melee still casting on the hold, or standing a little outward of it, is never the
    farthest; when the hold is within 5.5y of that (the last exaflare east) it takes (-16, 0), so its Flash Gale
    misses that melee. The first melee leaves the hold as its cast ends (154.7), not at 152.9;
  - at 195.8, after the flares, everyone takes the autos' spots for both Magic Numbers and the enrage.
- UNVERIFIED in `TopP6Constants`: the exaflare step (1.1s; tests also play 1.0s), the arrows' omen delay, the
  puddles' 2.7s blast, the adds' distances, the comets' bearings and the time they show, the flare's 20y kill radius,
  its 8.1s marker and when the markers show, the meteor stack's 5, the spreads' 3s vulnerability, Cosmo Dive taking
  its targets as it hits, both adds' enrages and 31669 from both kinds, the tank LB3's 1.0s and its status's 8s
  (upstream's `JobActions`; the game's tooltip says 12s), the mark's 1.3s and 6s, Run: ****mi*'s damage 30s after its
  cast, the gauge's rebuild and the human's melee windows. In `TopP6LimitBreaks`: the client's current-target
  stand-in, and that the client fills its own cast's ground point, within the sheet's 25y plus a yalm. In the State,
  the actions and the mechanics: the odds of every roll but the flares' and the meteor stack's (the sources call those
  random), upstream's laser vulnerability (2.5s) and Wild Charge rules, and that the bait, Flash Gale, Cosmo Dive, the
  spreads, the flares and the stack kill as they do.
- No source fixes these strat values, so they are UNVERIFIED (the user's picks):
  - autos: MT inside north (0, -5), the party inside south (0, 6), OT outside east (14.5, 0), after Cosmo Dive 2
    (16, 0) or (-16, 0);
  - Cosmo Arrow 1 side-steps south; dive tanks at r 6.5, MT toward 4 and OT toward 1 at Cosmo Dive 1, a Y at the
    party's bearing + 135 (MT) and + 225 (OT) at Cosmo Dive 2;
  - Unlimited Wave Cannon: a compass point per puddle at r 14. Exaflares 1 then waits at r 4 on each waymark's
    bearing until puddle 6 has gone off and spreads at r 10.5; Exaflares 2's party holds on the fourth exaflare's
    point, and for the autos its MT steps back to (0, -7);
  - the Wild Charge line in front of C, tanks first: (0, 6.5) and (0, 10.5); Wave Cannon 2's laser spot (3.55, 8.55);
  - Cosmo Meteor: centre at 2.6, the clock at r 13, H2 to the middle after the first 4 + 4 spreads, the flares at
    r 18.5 on the cardinals the stack leaves, in order clockwise from north (R1 stays on A);
  - the bots' press times above, and the autos' spots for both Magic Numbers and the enrage.
- Change bot movement only with that part's `TopP6Gauntlet<Part>LpduHumanTests` and `LpduMarginTests` green, and
  `TopP6GauntletMagicNumberLpduHumanTests` for the Magic Number part. The human suites press the seat's own limit
  breaks at the guides' cues. Change the limit breaks only with the `TopP6GauntletLimitBreak*Tests` green: each
  seat with each of its role's jobs, pressed right, late, never, twice, at another seat's duty, at the wrong level,
  cut off, with the rows and with a job that can't press. Their scripted presses (`TopP6HumanLimitBreak`) go nowhere
  while another seat's limit break casts, as the client greys the human's out. The soak filter is
  `FullyQualifiedName~ScenarioCatalogTests&Name~Gauntlet`.
