@AGENTS.md

# NoirMech

NoirMech is a fork of [AnoMech](https://github.com/anomek/AnoMech). `AGENTS.md` above is upstream's guide and
stays byte-identical to upstream; this file adds only what NoirMech changes. `git diff v0.5.0.0` shows every
NoirMech change:
- the rename (below);
- TOP P2 Party Synergy: the EU `[LPDU] BPOG/GOPB` strat (`TopP2PartySynergyLpduAi`), listed first in
  `AiStrats` so EU and it are the default pick (upstream's NA strat is index 1), `Group => "NA"` on upstream's
  strat, and no Spotlight in solo runs (a solo stack target stood alone and died);
- the user pause (below);
- TOP P3 (below): the Intermission, Hello World and Monitors scenarios with `[LPDU] Light PF` strats, the
  `TopZone.P3` phase, `TopP3Constants.cs` and `TopP3Actions.cs` (P3 ids and actions, kept out of upstream's
  `TopConstants.cs` and `TopActions.cs`);
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

NoirMech must not run alongside AnoMech: they install the same hooks and share ImGui window ids.

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
  inner quotes) and filter with `Name~Synergy` (NUnit filters can't contain spaces).

- Heavy runs (a 2000-seed soak, the P3 Hello World human tests) take 5-15 minutes each. Run one at a time and
  cap them (`-- NUnit.NumberOfTestWorkers=4`, low process priority): several in parallel have frozen the
  development PC.

## LPDU strat timing
Moves are timed like upstream's NA strat; the symbol flex at 8.5s is the only extra step. The flex has no
`arrivalTime` so it is done before the dodge is planned: `AiManager`'s deferred `MoveTo` can't be cancelled and
measures its delay from where the bot stands when the move fires, so a flex still walking at 11s made the
dodge's 14.8s deadline lose a bot in 21-29% of seeds (a 10s flex). Keep each `arrivalTime` before the next move,
and give every strat a `Group`. `selectedAi` is an index into `AiStrats`, and the main window opens a scenario on
the first strat and its region, so the order sets the default. Reorder only after updating every test that passes
a strat index (the scenario's own tests, `GamePauseTests`); the index isn't saved across sessions, but it is sent
to multiplayer peers, so both sides need the same build.

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
  arrive.
