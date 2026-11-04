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
- TOP P5 Delta (below): the EU `[LPDU] Light PF` strat (`TopP5DeltaLpduAi`), listed first in `AiStrats` so EU
  and it are the default pick (upstream's strat is index 1, with `Group => "NA"`, the automarkers and a turn by
  the eye's `Turn` in `AdjustEyePosition`), and small edits to upstream's Delta State, overrides, settings window
  and scenario and to `Multiplayer/Protocol.cs`: the Automarkers setting, the State's forced-band flags and their
  replay fields, the tether seating order, and the eye on all four cardinals (`NorthSouth` gains `Turn`, `East`,
  `West`, `Cardinals` and `WithEffectIndex`, the State rolls one of the four, the scenario turns the beetle, the
  Final, the eye unit and the arm units by `Turn` and mirrors the arms' facing with their spots for eye S, and the
  replay's `EyeSpawnIsNorth` became `EyeSpawnEffectIndex`);
- the Automarkers setting (`Scenarios/Automarkers.cs`, On/Off, default On): a party-setup option rather than a
  roll, which the P3 Intermission, P3 Monitors and both P5 Delta strats read to place signs;
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
- Beyond Defence = No is not a request: when both members of the pair the rule puts inner refused, one of them
  still takes Beyond Defense, because the scenario honours a refusal only while one of the two closest can take
  it.
- N/S inside a pair is the roll's (even slot north), since the sources disagree. The inner pair swaps N/S when
  its N member's fist matches the outer N member's, compared by role.
- Blue variant B: the inner blues stretch 13y apart, pop as the tethers go live (28.1s) and drop onto the outer
  blues; the outer blues break walking out to the NW/SW arms. Upstream's strat is variant A.
- The eye spawns on any cardinal, each a quarter of the time (how often the game picks each is UNVERIFIED). For E
  and W the scenario turns the beetle, the Final, the eye unit and the arm units a quarter (`NorthSouth.Turn`),
  and both strats turn their moves with them. S keeps upstream's `Mul`: the scenario negates the beetle's, the
  Final's and the eye unit's spawns with it (a half turn) but mirrors the arm units east to west, which lands them
  on the same six spots, and upstream's strat mirrors x and multiplies its side flips by it. `TopP5DeltaLpduAi`
  writes its spots with the eye north and turns them by the eye's bearing (`IAiPositions.Rotate`), picking the
  arms by their bearing in that frame. The E/W eye MapEffect slots (3 and 7, P2's scheme) are UNVERIFIED.
  `TopP5DeltaEyeTests` checks each eye's spawns, hit areas and replay.
- The moves follow the fight's cues rather than upstream's timings. The line-up goes at 11s, about a second after
  the tethers appear, so a human in a healer tie sees the bots' pick long before the fists (18.25s).
- With Automarkers On both strats mark at 10.5s, just after the Hello World debuffs land: a Triangle on Near
  World and a Cross (the + marker) on Distant World.
- The forced-band flags, Automarkers and the eye's `EffectIndex` travel in `TopP5DeltaAiReplayStateMessage`, so
  a peer's replay decides the same way. Upstream's unreleased `4bb8b6c` removes that replay layer: drop those
  fields with it, and rework the tests that read `LastState`.
- UNVERIFIED: the 10y tether break distance (P3 uses 12 / 9; the 13y stretch pops under both), the Swivel
  Cannon's apex on the beetle, and Omega-M's facing relative to the eye: it keeps its absolute facings for every
  eye (north at spawn, south at 28.2s, north at 54s), and no mechanic reads them. No source gives these spots, in
  the eye frame as `TopP5DeltaLpduAi` writes them: the unsafe inner green's wait (-9.6, -3.4), the green staging
  (0, ±12) / (9, ±10), the outer greens' walk-together (±4, -0.5), the Beyond Defense target's spot 13y toward the
  Final from its stack spot, and the opening and end clumps (upstream's).
- Not wanted: a kill for moving at the fist snapshot, keeping the Hello holders on different blue pairs, and
  renaming the settings' Close/Far.
- Change bot movement only with `TopP5DeltaLpduHumanTests` and `TopP5DeltaLpduMarginTests` green.
