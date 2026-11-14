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
  - a human tank's own mitigation at Cosmo Dive and Wild Charge, asked only of a tank's job (below): Cosmo Dive calls
    upstream's `DamageCheck.LethalCause`, and `TopP6Actions.WildCharge` copies upstream's Wild Charge effects (and
    `TopActions`' private `MagicVulns`) with a `MinMit` on its front hit, wrapping upstream's Wild Charge split in a
    `Distribution` of its own that drops the `MinMit` for a player on another job;
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
- TEA P1 (below), the first family NoirMech adds: `Scenarios/Tea/` with `TeaZone` and its `P1` phase,
  `TeaP1Constants.cs`, `TeaP1Actions.cs`, `TeaP1Presses.cs` (the human's own anti-knockback presses and cleanses),
  `TeaP1Mechanics.cs` (the Living Liquid, the Liquid Hand, the tornadoes and the attacks they play), and two
  scenarios, each with an `[LPDU] Light PF` strat: `P1LivingLiquid/`, the fight from Protean 1 to the kill, and
  `P1LimitCut/`, the intermission; in upstream's `ScenarioCatalog.cs`, a `using` and one entry for each;
- the core of upstream's unreleased `30e27cc`, the player's own presses as events, backported line for line so the
  release that carries it merges clean: `Core/Native/Interfaces/PlayerAction.cs`, `IUserActions.Acted`, its four
  `Acted` lines in `UserActions.cs` and `FakeUserActions.Act`. Its multiplayer half and its `ActionRow`/`GameData`
  `IsPlayerAction` change are left out;
- upstream's unreleased `89aa318` and `e0c03f0`, backported line for line so the release that carries them merges
  clean: Black Hole's bots grab each new set of tethers once their client shows it (`UmadP3BlackHoleAi.cs`), and a
  status keeps its source once its applier despawns (`Multiplayer/PacketRecorder.cs` and `PacketReplay.cs`). The
  13 Dancing Mad P3 Black Hole multiplayer tests (`PeerParityTests`, `LinkInterruptionTests`) fail without
  `e0c03f0`, as on upstream's v0.5.1.0; `89aa318` alone fixes none of them;
- the Automarkers setting (`Scenarios/Automarkers.cs`, On/Off, default On): a party-setup option rather than a
  roll, which the P3 Intermission, P3 Monitors and both P5 Delta strats read to place signs;
- the pull-log report (below): an [Explicit] test in `tests/AnoMech.Tests/Reports/` that checks TOP's values,
  UNVERIFIED or settled, against the user's ACT logs, and the ACT log parser it reads them with;
- Retry and Drill (below): `Core/Game/RollReplay.cs`, which upstream's `PracticeLoop` owns and reports every run to
  (it keeps a replayed roll off the streak and lets Drill make the restart); upstream's `RunController` is `partial`,
  with Retry in `RunController.Retry.cs` and a `checkSettings` flag on its `StartRefusal`; in upstream's `Plugin`,
  `/noirmech retry` and the Drill tick in `OnFrameworkUpdate`. With them, the fix for Auto-restart replaying one roll:
  upstream's `Game.EnterAndStart` raises `RunStarted` with the params as asked, not with the seed it draws to seat the
  party before a zone-in;
- the refusal to run while AnoMech is loaded (below): `AnoMechPresence.cs` and `AnoMechWatch.cs`; upstream's `Plugin`
  holds the presence (`Plugin.AnoMechPresence`) and the watch, which it starts and disposes with one line each; and a
  first check in `RunController.StartRefusal` and in `ZoneControl`'s `StartBlockedReason` and `HoldBlockedReason`;
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
- `tools/upstream-check.ps1`, a preview of merging an AnoMech ref (below);
- tests for all of these;
- the test gate (below): `tools/test.ps1`, `StratLintTests`, and a `PauseSeeds` run parameter in upstream's
  `test.runsettings` for `GamePauseTests`' sweep;
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
  `main`. It never pushes `noirmech-stable`: push source commits separately, and first, since it publishes only
  a commit in `plugins/noirmech-stable` as last fetched (`-Fetch` fetches it). Don't bump `<Version>` by hand;
  it tracks the upstream base.
- `publish.ps1` also refuses without `-Changelog`, without `TestResults\green-stamp.txt` saying
  `green <HEAD> full` (below), and unless its identity scan comes back clean. The scan reads the built plugin zip
  and the source zip, entry names, DLL strings and each DLL's embedded PDB included, as ASCII, UTF-8 and UTF-16,
  for the terms in `%APPDATA%\NoirMech\identity-terms.txt` in any case (a missing or empty file refuses too), and
  must find its controls, so a scan that reads nothing can't pass: NAudio's own build path, UTF-16 strings and
  `/_/` paths in `NoirMech.dll`, a NoirMech file name and `/_/` in its PDB, and NoirMech source. The terms live
  outside both repos; never write them into either. `-DryRun` runs every gate, the build and the scan, and stops
  before copying anything. It also refuses unless its own checkout is on `main` with nothing uncommitted or
  untracked, and its commit takes only `plugins/NoirMech` and `repo.json`.
- `publish.ps1` is gentle the way `tools\test.ps1` is (below): before it starts any process it drops itself to
  Idle priority on its `-Affinity` CPUs (`0xF0`), and its build skips the build servers, so all of it runs there.
  Run it plainly: `cmd /c start /wait` hands back `start`'s exit code, 0, so a refusal there reads as a pass.

## Merging AnoMech's next release
`powershell -ExecutionPolicy Bypass -File tools\upstream-check.ps1 [-Ref <tag>]` (default `upstream/master`)
previews a merge without making one: the files that would conflict, and NoirMech's own lines (changed or added
since the last merged release) that use a declaration the ref deletes. It greps, so also build and test a trial
merge in a throwaway detached worktree. Against `upstream/master` at `1646a70` (36 commits past v0.5.1.0) it finds
six conflicts and no such use: P3 and P4 keep their own 9020 helper id (`TopP3Constants.OmegaHelper`,
`TopP4Constants.OmegaHelper`) and Hello World checks its regressions' ends itself. A trial merge of it, its
conflicts resolved as below but for `MainWindow.cs` (master's side), builds once `TimelineRun.cs` is fixed, and its
P2, P3, P4, P5, P6 and pause tests pass. At the merge:
- `ScenarioSession.cs` and `SimWorld.cs`: keep both sides, the pause's members and halt methods beside master's
  `Issues`, `Ended` and `SimEvents`.
- `LocalPlayerInputHooks.cs`: keep the `ActionsBlocked` return first, ahead of master's `GetAdjustedActionId` line.
- `MainWindow.cs`: master rebuilds the title (`Title(failed)`, set every frame), the Stop buttons and the status
  line (`RunStatus`, where a frozen run reads Failed) round its failure banner. Re-port the Pause button, the
  title-bar icon, `PausedTitle` (ahead of `Title(failed)`) and a Paused label for the user pause onto it; master's
  Failed covers NoirMech's Frozen.
- The rename: master adds user-facing text that names AnoMech, `TitleText` in `MainWindow.cs`, the `[AnoMech]
  Mechanic failed` and `[AnoMech] Run failed` chat lines in `ScenarioSession.cs` and the safety stop's reason in
  `ZoneControl.cs` (`AnoMech was unloaded during a run`). Write `Plugin.DisplayName` there, as the rename does.
- `TopP2PartySynergyScenario.cs`: master casts every helper action from the zone's pool; take its side of both
  conflicts and put the solo skip into its `Run_Spotlight` (no Spotlight when `solo`), which
  `TopP2PartySynergySoloTests` checks. Resolve the conflicts, not the file: `AiStrats` (LPDU first) merges clean,
  and master's whole file would put its NA strat back at index 0, which the LPDU and NA strat tests catch.
- `tests/AnoMech.Tests/Support/ScenarioRun.cs`: keep NoirMech's catalog-less construction
  (`SingleOrDefault(...) ?? Activator.CreateInstance`), `RunUntilStopped` and `probe.Stopped` beside master's
  four-argument `PartyMemberKilled`, `MechanicFailed`, `HelperPoolExhausted` and `SessionStatus.CompletedFailed`.
- `tests/AnoMech.Tests/Support/TimelineRun.cs` merges clean but won't build: subscribe with master's four-argument
  `Game.PartyMemberKilled` and fill its `Death.StatusId`. Its loop already ends on master's
  `SessionStatus.CompletedFailed`, which comes after `Frozen`.
- Helpers: master's TOP `DutyInitData` spawns `HelperPool(9020, 36)` at every run's start, and
  `SimEnemy.CastFromHelper` casts from it.
  - P6: `TopP6AlphaOmega` spawns its own 36 (`SpawnedHelperPool`), so a Gauntlet run would hold 72. Make its
    `CastFromHelper` body `Enemy?.CastFromHelper(...)` (`HelperCast`'s signature), drop the pool field,
    `helpers.Spawn()` and `BusyHelperPeak`, and rebuild `TopP6HelperBudgetTests` on master's `HelperPoolExhausted`,
    which master's `ScenarioRun` already fails a run on. `SpawnedHelperPool` and its tests stay: TEA's two
    scenarios each spawn one (below). `HelperCast` stays for `ExaflareLane` and `BaitedPuddles`.
  - P3 and P4 spawn their own 9020 helpers beside the pool (Intermission 10, Hello World 12, Monitors 14, Blue
    Screen 19) and play the same with it. Casting through `omega.CastFromHelper(action, target, at: ...)` instead
    would drop them (Hello World would then keep its towers' placements itself, which its resolve reads off the
    tower helpers); that is optional.
  - TEA: master's `IZone` asks for a `DutyInitData` (the entrance barrier, which gains a rotation, a `HelperPool`,
    a `MapEffectSnapshot` and the `Rsv` and `Rsf` strings) where v0.5.1.0's asks for an `EntranceBarrier`, so
    `TeaZone` won't build until it has one. `tools\upstream-check.ps1` doesn't report that (a changed member, not
    a deleted name), and no trial merge has been built with TEA in it. Upstream generates each zone's from a replay
    (`tools/replay_export_duty_init.py`); with none of 887 at hand TEA's is written by hand: the barrier as now,
    `HelperPool(9020, 17)` (Living Liquid keeps 11 busy at once, Limit Cut 2) and the rest empty (UNVERIFIED).
    Limit Cut can then cast its Hawk Blasters through Cruise Chaser's `CastFromHelper(action, target, at: ...)` and
    `TeaP1LivingLiquid.CastFromHelper` become `Liquid?.CastFromHelper(...)`, each dropping its `SpawnedHelperPool`,
    which then has no user left; that is optional.
- Master's `SimTether` ends itself when an end dies (`TetherEnded`), without the scenario asking: Hello World's
  ghost tethers then go with a death, as its regressions already do.

## Build and test
- .NET 10 SDK. `dotnet build` from the repo root gives `AnoMech/bin/x64/Debug/NoirMech.dll`; load it through
  Dalamud's Dev Plugin Locations with any installed NoirMech or AnoMech disabled.
- `dotnet test` runs both test projects. For a soak, pass
  `-- 'TestRunParameters.Parameter(name="SunnySeeds", value="2000")'` from Git Bash (PowerShell 5.1 strips the
  inner quotes) and filter with `Name~Synergy` (NUnit filters can't contain spaces). The soak runs under the
  catalog name, so a two-word scenario needs each word: `Name~Blue&Name~Screen`, not `Name~BlueScreen`, which
  only matches the scenario's own test classes.
- `tools\test.ps1` is the gate (`powershell -ExecutionPolicy Bypass -File tools\test.ps1`; `-Plan` prints its
  shards and their filters and runs nothing). It builds once, then runs AnoMech.Tests as `-Shards` parallel
  `dotnet test --no-build` processes and AnoMech.Relay.Tests as one more, prints each process's counts and time
  and one summary, and exits non-zero on any failure.
  - A run is green only when no test failed or ran twice, every process exited 0 with a readable trx, and
    AnoMech.Tests ran tests; in a full or quick run, where every process holds tests, each must have run some.
    The NUnit adapter only logs it when it can't load the test assembly (a dependency missing, say), and every
    shard then exits 0 with a trx of no tests, so a count over all processes isn't enough: the relay's tests alone
    would pass it.
  - Gentle by default: before it starts any process, git included, it drops itself to Idle priority on the
    `-Affinity` CPUs (`0xF0`, 4 of the 16), so the build, every test process and the test hosts those start are
    born there, and `-Shards` defaults to one per CPU in the mask (4); it restores its own priority on its way
    out. The build passes `--disable-build-servers`: Roslyn starts its shared compiler server at normal priority
    whatever its parent runs at. `-Affinity 0xFF00` (8 CPUs, so 8 shards) is faster when nobody needs the PC.
  - The shards are dealt from `$Buckets` by each bucket's single-process time, heaviest onto the lightest shard. A
    shard takes what its buckets match that no earlier shard took and the shard holding the rest takes everything
    else, so every test runs once, a new one included. When the shards' times drift apart, refresh the bucket
    times or split a bucket; keep `[Explicit]` tests out of every bucket, since a filter that selects one can run
    it.
  - `-Preset full` (the default) runs everything at the run settings' seeds: 12 minutes here with the defaults,
    the PC's CPU load staying under a quarter, against about 37 for a plain `dotnet test` and 28 for one capped
    process. `-Preset quick` drops the `*HumanTests` suites and runs 4 sunny-day seeds and 1 pause seed, about 3
    minutes; `-Phase P3` runs one TOP phase's `TopP3*` classes and the catalog-wide tests' cases for its
    scenarios, with `-Preset quick` too if wanted; `-SunnySeeds 2000` makes any of them a soak.
- A green full run of a clean tree writes `green <HEAD> full` to `TestResults\green-stamp.txt` (gitignored), which
  `publish.ps1` requires. A full run deletes it first, and one with uncommitted changes, `-NoBuild` or
  `-SunnySeeds`, or one HEAD moved under, writes none.
- `test.runsettings` sets the seeds: `SunnySeeds` (40) for each catalog strat's sunny day and `PauseSeeds` (5) for
  `GamePauseTests`' sweep of every catalog strat with pauses; override either as above.
- `StratLintTests` runs every catalog strat on 3 fixed seeds (`LintSeeds`) and reads `AiManager`'s move lines in
  each trace. A `Move` with an `arrivalTime` measures its bots' departures from where they stand as it fires, and
  the deferred `MoveTo` can't be cancelled, so the lint fails when a bot's deferred departure lands at or after
  that bot's next move, or when a deferred move is planned while its bot still walks from the one before (the 10s
  Party Synergy flex), naming both sites. `Known` holds upstream's FRU P3 Apocalypse walk at 36.05, which two
  bots finish up to 0.19s after the 37.0 deadline move plans their departures, inside `AiManager`'s 0.25s margin;
  an entry fails the test once it's no longer found.

- Heavy runs (a 2000-seed soak, the P3 Hello World human tests) take 5-15 minutes each. Several test processes
  at normal priority on every CPU have frozen the development PC: run them through `tools\test.ps1`, or one
  `dotnet test` at a time at Idle priority on a few CPUs, and never two heavy runs at once.

## Pull-log report
`tests/AnoMech.Tests/Reports/TopPullLogs/TopPullLogReport.cs` (`TopUnverifiedValues`, [Explicit]) reads every
`Network_*.log` in the folder the `PullLogs` parameter names (ACT's is `%APPDATA%\Advanced Combat Tracker\FFXIVLogs`),
splits out the TOP pulls and checks NoirMech's TOP values against them, UNVERIFIED or settled. Without the parameter
it is skipped. From Git Bash, after a build:
`dotnet test tests/AnoMech.Tests --no-build --filter Name=TopUnverifiedValues -- 'TestRunParameters.Parameter(name="PullLogs", value="<folder>")'`.
It takes seconds and writes `pull-logs/top-unverified.txt` in the test run's output folder (`bin/`, ignored by git).
It reads the logs where they are, the one ACT is still writing included, so ACT can stay open; never copy the logs
anywhere, since they hold other players' names.
- Each value prints its constant (file:line, found by the line's text), the sim's value, the measured values with
  their count and spread, and a verdict: matches, differs, inconclusive or not in these logs. Constants and event
  times (from the scenario's `world.Events.Add` lines) are read from the code of the checkout the tests were built
  in, so the report follows their edits; a rule or layout the measure class describes in words has to be updated
  with the sim. The report changes no sim value.
- `TopPullLogReportTests` fails, naming the finding, when an edit drops the text a finding looks for. A finding on a
  constant looks for its declaration and one on an event time for its `world.Events.Add` line, so dropping an
  UNVERIFIED mark beside them fails nothing; the finding on the void zone's spawn arguments looks for their
  UNVERIFIED note instead. A finding on a rule described in words looks for the rule's UNVERIFIED note while it has
  one and for the code line that plays it once settled, so settling the rule fails there and changing a settled one
  usually does. The findings on the Sniper Cannon and two-beam kills look for their death reasons and the one on the
  line stacks' aim for its comment, so a change to those rules can pass with their wording stale.
- `Reports/ActLogs/` is the parser: pulls split as upstream's `tools/parser.py` splits them (the game's combat flag
  on 260 lines, else the duty director's codes), and the line types the report reads as typed events. It blanks
  every name field as it reads a line and the report names players by job; never copy log lines into the repo.
  A combat segment where no enemy cast or used an ability (the plugin's own runs make them) is listed and left out.
- Deaths: a player is dead from its first 0-HP line (37/38/39); the death line (25) comes about 2 s later. A hit
  killed when its effect result (the 37 line with its sequence) left the target at 0 HP, or, with none logged, when
  the target died within 3 s. A hit on a player under an invulnerability (Hallowed Ground, Holmgang, Living Dead,
  Walking Dead, Superbolide, Undead Rebirth) is listed but left out of every kill rule.
- When something stops killing is told from players who stood in it and lived on, not from when its object leaves
  the client's object table: the P3 void zone's object stands until about 24.5 s, but players lived within the
  sim's 6 y from about 23.2 s.
- Positions: the logging player's own run a little ahead of the server's and the other players' trail it by a few
  tenths of a second, so a distance between players is good to about a yalm. Server placements (271 lines) and cast
  locations (263 lines) are exact to about 0.03 y, and the report measures from them where it can.
- The measures cover what the logs reach (P3 and P4 Blue Screen); a phase's values need a measure class beside
  the others, listed in `TopPullLogReport.Measures`.

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
  again 3 s after it freezes (`RollReplay.FreezeHoldSeconds`), so the player sees the frozen scene, or, when godmode
  kept everyone up, as soon as it ends; a clean run moves on to a fresh roll. Deaths switch Auto-restart off but not
  Drill, and with Drill on Drill makes the restart. A run stopped before it froze isn't played again.
- The freeze is polled: `Rolls.Tick` runs after `Game.Tick` in `Plugin.OnFrameworkUpdate` and counts the hold on the
  frame's own seconds, since the sim's clock stands still through the freeze. Drill ticked on during a freeze replays
  3 s after the tick. Whatever ends or replaces the frozen run within the 3 s drops the replay (Stop or Reset, Start,
  Retry, Leave, the zone going away), and so does ticking Drill off; Pause is refused through the freeze.
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
  (`%APPDATA%\XIVLauncher\pluginConfigs\NoirMech\`), not in upstream's `Configuration`: the Role pick by label, the
  seat Rotate last gave (below), and per scenario (by `FullName`) its region, its strat by name and its settings
  overrides as `ScenarioSettingsSync` serializes them, which leaves out the internal test pins. At load
  `MainWindow.RestoreSetup` puts back the Role pick, Rotate's seat and each scenario's region and settings, before the
  last scenario is selected again; a scenario's strat comes back by name as it is selected, and one it no longer has in
  that region leaves the region's first.
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
  the rotation alone. Multiplayer seats stay the Party panel's.
- Rotate goes on across game restarts. `SetupMemory` keeps the tank or melee seat the rotation last gave
  (`SeatRotation.Last`, by its label in `RotateSeat`; a healer or ranged run after it leaves it be), and `RestoreSetup`
  hands it back to `RunController.Seats` as a seat played, so that role goes on alternating from it, on any job of the
  role. The other role starts from the seat Auto gives, even if it had a turn before the restart.
- Beside the pick the window shows the player's seat: the one the next Start gives on the current job ("You: M2"),
  or during a run that run's, with Rotate followed by the next fresh roll's ("You: MT (next OT)") unless Drill will
  play the roll again (`RollReplay.DrillReplays`). Retry plays the seat its run gave.
- The P6 Gauntlet builds its limit break plan each run from the seats and the human's client job, so a rotated tank
  or melee gets its duties by job priority against the bot beside it (a PLD: Cosmo Memory and Magic Number 1 in MT
  beside the OT PLD bot, Magic Number 2 in OT beside the MT WAR bot). Every seat Rotate gives fits the job, so the
  chat line names the duties (`TopP6GauntletLimitBreakPlanTests`).
- `SetupMemoryTests` (every catalog scenario's settings, off their defaults, survive a save and a load; what a
  session keeps; Rotate's seat across a restart) and `SeatRotationTests`.

## TOP P3
`Scenarios/Top/P3Intermission/`, `Scenarios/Top/P3HelloWorld/` and `Scenarios/Top/P3Monitors/` in the
`TopZone.P3` phase. Their ids are in `TopP3Constants.cs`. Values nobody has measured (weather, rot contact
radius, the void zone's radius and spawn args) are marked UNVERIFIED there; tune them in that one place.
- Intermission runs from the end of P2 until Omega turns targetable: Wave Repeater rings, Colossal Blow arms
  (the set that warps in first goes off first; the State rolls three left and three right arms onto random
  spots, so a set can be all one kind, which changes only how an arm looks and warps in), Sniper Cannon spreads
  and stacks (their 19s statuses run out 0.1s before both go off), and the void zone, shown from 5.46s after the
  first Wave Repeater's cast until its model changes 21.4s after it (when it stops killing is UNVERIFIED). Every
  mistake kills on the spot with a reason; the first ring's deaths show 0.6s after its bar (UNVERIFIED), ahead of
  the void zone in the same circle. Its one strat has an Automarkers On/Off setting (default On); it lives in the
  overrides and the State, and only `TopP3IntermissionLpduAi` reads it, to add signs: both settings put everyone
  on the same spots. Change bot movement only with `TopP3IntermissionLpduHumanTests` and
  `TopP3IntermissionLpduMarginTests` green.
- Hello World tracks each player's debuffs itself (pairs rotating through defamation, remote tether, stack and
  local tether; rots spreading on contact; Debuggers as immunity) and decides every kill with a death
  explanation. Fixed events are absolute time literals in `Run`, except the tower debuffs, which `ResolveSet`
  schedules `TowerDebuffDelay` after each resolve for that set's soakers; expiries that depend on play run from
  `IScenario.Tick`.
- Each set's four towers stand 14y out, each colour's two side by side, on the cardinals or on the
  intercardinals. The State rolls each set's layout (the bearing between the two defamation-colour towers) from
  all eight directions, each equally often (how often the game picks each is UNVERIFIED). The Towers setting
  (Auto, Cardinals, Intercardinals) picks the ring, and Tower layout's Same every set repeats the first set's. A
  tower's debuff lands 0.62s after it goes off and runs 10s; Latent Defect runs 27s from Hello World's hit, so
  whoever no defamation cleansed dies at 32s.
- Both regression tethers break at 9y, the remote one past it and the local one inside it, from the moment they
  go live. Standing on its stack partners, where the strat's slideshow takes the pass, the remote pair would be
  about 10.5y apart and break its tether. So it takes its rots 1.3y inside its stacks, toward each other, and
  stands 7.9y apart until its pop. No source gives that spot (UNVERIFIED), and it reaches the rot only while
  `RotContactRadius` (1.5y, UNVERIFIED) stays over 1.3y. A bot walks to a pass's spot exactly, without the 0.3y
  jitter, so it stays within the rot's reach.
- Its bots (`TopP3HelloWorldLpduAi` with the `TopP3HelloWorldTraffic` planner) rest only on LPDU strat spots and
  that remote pass. Every spot is placed from the set's layout, so they play both rings alike; only the loose
  spread before the first towers and the Monitors line-up stand fixed in the arena. They plan each walk round
  everyone's live positions and debuffs, give the human more room than each other (`HumanSwapClearance`), step
  aside for it, and hold their spots through each resolve. The bots-only soak can't see human conflicts, so change
  bot movement only with `TopP3HelloWorldLpduHumanTests` green: it scripts the player's seat through every kit,
  seat, walking style and reaction delay, on layouts from all eight directions. Like a player, it walks round a
  soaker holding its tower spot in its way, unless it takes the guides' lanes.
- Monitors: Omega's monitor hits every player on its half of the arena, however many (nobody when the half is
  empty, UNVERIFIED). Each player's monitor hits 2 random players on its side (`CharacterFind.OnSideN`, as P5
  does), filling up with random others when fewer stand there. Bots aim theirs by turning after they arrive.
  The run starts at Critical Error's cast, as the fight's lead-in does: bots stack west of Omega, line up on the
  west arc once it resolves, and hold the line until the monitors appear at 19.4s. With Automarkers On its strat
  marks at 19.5s: Bind 1-3 on the monitor players and Attack 1-5 on the rest, each in line-up order.

## TOP P4
`Scenarios/Top/P4BlueScreen/` in the `TopZone.P4` phase, which keeps P3's weather and BGM 950. Its ids are in
`TopP4Constants.cs` (it reuses P3's Omega and Wave Repeater ids); unmeasured values and rules are marked
UNVERIFIED there and in the scenario.
- The run starts 3s before Omega's Wave Cannon cast and ends after the last Wave Repeater ring; the Blue Screen
  enrage isn't played. Three rounds: each aims a line from the centre at every player; just after, each player's
  beam fires from the centre toward where that player stands then, so it can leave the aimed line; the repeats
  resolve down the aimed lines 5.3s after the aim, then the two line stacks toward their marked players; Wave
  Repeater rings run through rounds 2 and 3. In the fight the beams fire 0.53-0.54s after the aim in every round;
  the sim aims round 1 at 8.0s, 0.08s early, and fires its beams at 8.6s. Rounds 2 and 3 are timed from one pull:
  aims at 18.28 and 28.53s, each landing with one of Omega's visuals (32534, then 31619 with the second Wave
  Repeater's cast), beams at 18.82 and 29.06s, line stacks at 23.71 and 33.96s, the second Wave Repeater's rings at
  35.65, 37.7 and 39.75s. The repeats cast from the aim, but their orange lines only flash, 4.4-4.8s into the cast
  (`P4Duration.RepeatOmenDelay`, UNVERIFIED; past 4.55s a pause would hide them).
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
  a peer's replay decides the same way. Upstream's `4bb8b6c`, on its `mp-host-driven-bots` branch and not on
  `master`, removes that replay layer: if a release brings it, drop those fields with it, and rework the tests that
  read `LastState`.
- UNVERIFIED: the 10y tether break distance (P3 uses 9 both ways; the 13y stretch pops under both), the Swivel
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
  - The enrage: a melee LB3 on Alpha Omega whose own cast bar ends from Run: ****mi*'s cast start until before its
    damage, 232.0 to 262.0 (`TopP6LimitBreakPress.LandsAMeleeLimitBreak3On`, by `LandsAt`, since the human's resolve is
    heard a frame or two later), defeats Alpha Omega once it resolves (`SimEnemy.Defeat`); otherwise the damage kills
    everyone at 262.0. The "Carry the Clear" TOP healing plan has top groups killing it from the bar's end (247.7)
    until just before the damage (5:08 to 5:22 on its P6 timer). One started up to a cast before the cast counts, one
    landing before it is wasted, one landing as the damage does or later is too late, and one the cast's own frame
    settled before Run: ****mi* listens is counted as it starts listening. The damage waits up to 0.25s for the resolve
    of a human's that landed before it. It is queued only if Alpha Omega still stands as the bar ends, so a run that
    finishes it in the cast ends without waiting it out. Whether the gauge was short is read as the damage lands, when
    the strat's press gives up.
  - Melee (the user's rule, the human only): for each of its duties the human's melee LB3 must start in the window,
    aim at Alpha Omega and land, or the human dies as the window closes, or as the cast is cut off if that is later.
    The first: from Cosmo Dive 2's bar end (149.8) to 161.0; the second: from the first's landing to 165.5. The
    enrage's (`TopP6MeleeWindow.LandsBy`) counts the presses that defeat Alpha Omega, by the same call: started 227.5
    to 257.5 (the damage less the 4.5s cast), landing from the cast's start until before the damage. A press landing
    before the cast is wasted, so the window kills at 257.5 and the damage then kills the rest at 262.0, inside the 5s
    before a death freezes the run. A window remembers its target's id, since Alpha Omega despawns 9.7s after falling,
    which can be before the enrage's window closes. An open window keeps the run going only until it is filled, looking
    every second, so a run that fills the enrage's early still ends about 9.7s after Alpha Omega falls, as its other
    events do. Bots' melee LB3s aren't held to windows.
  - Death reasons state the fight's rule or the Gauntlet's, never a strat spot.
- A human tank's own mitigation is checked under each Cosmo Dive buster (39.4, 152.5) and at the front of each Wild
  Charge (84.4, 118.3) as an EnemyAction's `MinMit` checks it: the human's alone (bots and a debug-driven player always
  pass), only with "Require tank mitigation" and "Resolve your own actions" on, and the shields it counted are spent.
  Only a human on a tank's job (WAR, DRK, PLD or GNB, as its client has it) is held to it: one the Role setting seats
  in a tank seat on another job has none of a tank's cooldowns and passes, as a bot does, just as
  `TopP6GauntletLimitBreakPlan` holds it to none of its seat's limit breaks (`HumanFits`). Its seat's spots still
  count, and a non-tank seat in Wild Charge's front two dies whatever its job. In `Mitigation.Effective`'s terms,
  against the hits' magic damage, a buster needs 0.38 and the front 0.45 (`P6Rules`, UNVERIFIED: the sim has no damage
  data). LPDU's TOP mitigation sheet gives each tank a personal set at each hit, and every one reaches at least 0.388
  and 0.49 (an invuln 1), while Rampart and one short cooldown reaches 0.28 to 0.36, or up to 0.42 inside the 4s of
  Stem the Flow, Clarity of Corundum or Knight's Resolve, which passes a buster only. Cosmo Dive's bar ends 2.4s before
  its busters, inside those 4s, so a warrior, gunbreaker or paladin pressing Rampart and its short cooldown as the bar
  ends lives through its buster; only a dark knight, or a short cooldown pressed over 4s ahead, needs more there. Cosmo
  Dive decides it in `LandCosmoDive` through `DamageCheck.LethalCause`, last of its `Verdicts`, so a tank in the wrong
  spot hears of its spot; Wild Charge through its front hit's `TankBuster.MinMit`, which the engine asks of the human
  whatever its job, so `TopP6Actions`' `MinMitOnTankJobsOnly` drops it for a player on another job. Both read the job
  through `TopP6AlphaOmega.OnATankJob`. Limit breaks count whatever the setting says, as the settings' Limit breaks
  tooltip tells.
- Every other mistake also kills on the spot with a reason. Exaflares, puddles, the bait, exasquares, lasers and Wild
  Charge kill through their actions. The rest is decided in code from where everyone stands as the hits land, those
  actions only showing them: Flash Gale (a non-tank target, or within 5y of one), Cosmo Dive (a non-tank buster
  target, within 8y of one, one in the stack, a stack under 6, then a human tank's mitigation), the meteor spreads (in
  another's, or one's own while still vulnerable), and the flares and stack (within 20y of a flare, a flare in the
  stack, a stack under 5). `Verdicts` keeps each player's first reason.
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
  cast (consistent with the healing plan's kill times, but unmeasured), the gauge's rebuild, the human's melee windows
  and the tank mitigation a buster and Wild Charge's front need. In `TopP6LimitBreaks`: the client's current-target
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
  breaks at the guides' cues and, with "Resolve your own actions" on, a human tank's own LPDU set before its Cosmo Dive
  or Wild Charge (`TopP6HumanMitigation`). Change the tank mitigation only with `TopP6TankMitigationTests` green: each
  job's sets, Rampart and its short cooldown pressed early and as Cosmo Dive's bar ends, an invuln, the setting or the
  module off, godmode, a bot tank, a human on another job in a tank seat through every hit, and a non-tank seat at Wild
  Charge's front. Change the limit breaks only with the `TopP6GauntletLimitBreak*Tests` green: each seat with each of
  its role's jobs, pressed right, late, never, twice, at another seat's duty, at the wrong level, cut off, with the
  rows and with a job that can't press. Their scripted presses (`TopP6HumanLimitBreak`) go nowhere while another seat's
  limit break casts, as the client greys the human's out. The soak filter is
  `FullyQualifiedName~ScenarioCatalogTests&Name~Gauntlet`.

## TEA P1
`Scenarios/Tea/`: `TeaZone` (The Epic of Alexander, territory 887, one EU waymark preset) with a `P1` phase,
`P1LivingLiquid/`, the phase's fight from Protean 1 to the kill (next section; its opening isn't built), and
`P1LimitCut/`, the intermission that ends the phase, which the rest of this section is about. Ids and tunables are in
`TeaP1Constants.cs`; what nobody has measured is marked UNVERIFIED there, in `TeaP1Actions.cs` and in the scenarios.
- The zone has no arena boundary: the rim (r 20) is taken for a wall (UNVERIFIED), so nobody dies past it. Its
  weather is left to the territory and its entrance barrier's ids are read from the layout, both UNVERIFIED.
- Limit Cut starts 8 s before the first Hawk Blaster, the party in the middle and no boss up, and ends after J Kick;
  Brute Justice and Cruise Chaser turning targetable isn't played. Party-only: no solo, no multiplayer.
  - The numbers show from 5.9, 0.05 s apart.
  - Hawk Blasters (r 10, no omen, from 3 invisible 9020 helpers in a `SpawnedHelperPool`) go off in pairs on
    opposite spots at 8.0, 10.2, 12.4, 14.6 and 19.0, 21.2, 23.5, 25.7, each pair 45 degrees clockwise of the last
    and the fifth on the first's spots, and in the middle at 16.8 and 27.9.
  - Cruise Chaser, hidden until a warp-in shows him as he first lands, cleaves the odd numbers (Alpha Sword at
    15.4, 20.1, 24.8, 29.4) and charges the even ones (Super Blassty Charge at 16.9, 21.6, 26.3, 30.9). He lands
    3 y behind the odd number, by the reverse of that player's facing, 0.5 s before the cleave, and turns to that
    player again 0.1 s before it, so a number can't step out of its own cleave; he charges from the same spot,
    turned to the even number 0.1 s before. The turn 0.1 s ahead is for his model, which shows a turn a flush
    late; he is turned once more as each hit goes off, so a number that crosses his centre inside that 0.1 s is
    still hit. A dead number's hit goes to a random living player. Dead means fallen:
    a blast's death shows 1 s after it (`P1Duration.HawkBlasterDamageDelay`), and every round has a blast inside
    the second before he lands or aims, so a number that blast killed still draws its own hit.
  - J Kick at 34.1 hurts nobody. Brute Justice casts it himself in the fight; he isn't drawn here, so an unseen
    helper under his name casts it.
- Rolls: the numbers (a permutation of the party) and the first pair's axis, one of four. The settings pin the
  axis and the player's number; Automarkers is Off by default here, unlike TOP's, and On has the strat put Attack
  1-8 on numbers 1-8 at 6.3.
- A number for another seat needs a host's session, which the scenario doesn't support: a local run shows only
  "Your number". The per-seat rows, `HasPerPlayerSettings` and the one-seat-per-number conflict are there for that
  day and are reached only by tests (`PerRole.ForceSeats`).
- A Hawk Blaster kills. A hit from Cruise Chaser leaves a 20 s vulnerability and a second one kills, so each cleave
  and charge may catch only its own number. Each death's reason states the fight's rule.
- Both of his hits knock back straight away from him, the cleave 5 y and the charge 20 y, through a `Push` whose
  distance `TeaP1Actions.Thrown` caps at the rim, since the engine stops only NPCs at a wall. Bots are taken to be
  immune and hold their spots, a headless run's debug-driven player among them. A human is thrown unless its own
  Arm's Length or Surecast was pressed in the 6 s before, or a warrior's Inner Release, whose Inner Strength holds
  15 s: `TeaP1Presses` hears the press through `IUserActions.Acted`, with "Resolve your own actions" on or off, and
  times it on the run's clock, since the sim shows none of the statuses. It doesn't ask the job: only a warrior's
  client can report Inner Release. A press comes with its button's own id, so Inner Release is heard as 7389 and
  as 38, the Berserk a button may have been placed as; once a release brings upstream's `283e864`, which reports
  the id a button is adjusted to, 38 no longer comes and its arm does nothing.
- One scenario instance plays every run of a session. `Run` clears what the last run left on it and replaces the
  press listener, which a scenario can't dispose of as its run ends (`TeaP1LimitCutSecondRunTests`).
- One strat, EU `[LPDU] Light PF` (`TeaP1LimitCutLpduAi`). Numbers 1, 2, 5, 6 take the first pair's blaster in the
  west-to-north-east half and 3, 4, 7, 8 the opposite one, and each group follows the blasts round clockwise, its
  spot k 45k degrees on from its first. On a spot at bearing b the pair hit next stands 14 y out, the odd at b - 2
  turned clockwise along the rim and the even at b - 30, and the group's other pair waits 19.3 y out at b - 4 and
  b - 8. #1 and #2 are hit on their group's spot 2, #5 and #6 on its spot 5, #3 and #4 on their group's spot 3 and
  #7 and #8 on its spot 6. A cleaved odd walks on at once, onto the outer lane.
- The strat turns its odd bots along the rim, but nothing turns a human or puts it exactly on a spot, and both of
  Cruise Chaser's hits pass close to the group's other pair, waiting at the rim:
  - Landing 3 y behind, an odd number's cleave reaches its group's waiting odd bot from about 20 degrees outward of
    the rim, and #5's reaches #4, crossing ahead of it, from about 17 degrees inward (the other odds have 35 or
    more). A step sideways after he lands turns the cleave the same way, 1.1 y for 20 degrees. The waiting odd's
    b - 4 sets the outward limit.
  - A human that stops facing the way it walked in, along the chord from its last spot, faces 22.5 degrees
    outward: on its spot exactly, its cleave still clears the waiting odd bot, by 0.2 y.
  - A bot caught carries the vulnerability and dies at its own cleave, or at once if it had been hit already.
  - An even number's room is a wedge round its spot, and the other group sets its near and inner edges. He lands
    at about b - 14 on the lane and turns to the even for the charge, so an even within about a yalm of where he
    lands, or inside the lane near it, turns the charge across the arena into the other group (group A, leaving
    its spot 4, for #4's). On the lane it is clear from 18 degrees behind the spot's bearing; on its spot's own
    bearing from 12 y out to about 16, where the charge reaches the group's waiting even bot; 13 y out at b - 20
    is not clear and 13.5 is. Landing further behind closes the wedge from the odd's side (at 4.5 y, b - 22 on
    the lane is caught in most runs). The human suite holds the lane at b - 20, where the guides draw the even,
    and 12.5 y out on the spot's bearing; the other edges come from replaying the strat's geometry, not from a
    test. Thrown from 1.5 y wide of its spot, #4 lands inside the next pair's blast (below).
- UNVERIFIED in `TeaP1Constants`: the 3 y landing (at most about 6) and its 0.5 s, the knockback distances and
  speeds and that both push straight away from him, the vulnerability's status (695) and its 20 s, the numbers'
  0.05 s, the 1 s after which each hit's damage shows and the 0.6 s a blast keeps its helper (the engine's
  placeholders), where the track stands as the run starts (122.7 s in: 8 s before the first blaster of a pull whose
  Liquid dies 125.5 s in), and that an anti-knockback holds from the press. In the
  State and the scenario: how often each axis comes up, that he turns to his number again before the cleave, that
  he charges from where he cleaved, that a dead number's hit goes to anyone living, his warp-in, and where Brute
  Justice stands for J Kick.
- No source fixes these strat values, so they are UNVERIFIED picks: the run-out from the middle 12 degrees behind
  the first spot for the odds and 16 for the waiting even, the 1 y ring everyone waits on in the middle and the
  2.5 y one for J Kick (from 31.0), when each odd turns along the rim (14.6, 19.3, 24.0, 28.6) and every move's
  time. The turns are literals 0.3 s before he lands, and #1 only arrives at about 14.3:
  `P1Duration.CruiseChaserLandsBeforeAlphaSword` can't pass 0.8 s without moving the turns, nor about 1.1 s
  without #1's 12.5 walk leaving sooner (`EachOddFacesClockwiseAlongTheRimAsCruiseChaserLandsBehindIt` fails).
- Change bot movement only with `TeaP1LimitCutLpduHumanTests` and `TeaP1LimitCutLpduMarginTests` green.
  - The human suite plays every number as a human from the first frame, walking or sprinting, reacting 0, 0.25 or
    0.5 s late, pressing its anti-knockback or thrown, over every seat. On the spot it is hit on, an odd number
    faces along the rim, 10 degrees inward or outward of it, or the way it walked in, and an even number stands on
    its spot, 1.5 y outward or inward of it, or on the lane at b - 20. Nobody may die, except where a thrown even
    number's way out is too narrow; those cases may lose no more runs on the usual rolls than they do today, of
    40, and nobody but the human, to a Hawk Blaster:
    - a #2 thrown from its spot that walks and reacts 0.5 s late (22);
    - a #2 thrown from 1.5 y inside its spot, which goes through its group's first spot to the rim past it, inside
      the blast that goes off there again on the other group's side (40 at every pace);
    - a #2 thrown from b - 20, 1.4 y from him, whose throw the bots' jitter turns by up to 12 degrees: walking 11,
      19 and 28 at 0, 0.25 and 0.5 s late, sprinting 6 and 14 at 0.25 and 0.5 s (none sprinting at once);
    - a #4 thrown from 1.5 y wide, which the rim stops inside the next pair's blast, that walks and reacts late
      (40) or sprints 0.5 s late (19).
  - The margin suite measures the bots' clearance from every blast, cleave and charge on every axis, with him
    landing 1.5, 3 and 4.5 y behind and the odd number turned 5 degrees either way. Its `Usual` rows hold at
    `P1Geometry.CruiseChaserLandsBehind` and were measured at 3 y: retune them with it. At that distance it also
    turns the odd 15 degrees either way, held only to catching nobody. A turned case fails unless he cleaves
    along the turn it gave the odd, so one the strat's own turn overrode can't pass as measured.
  - The rule tests play no strat (`TeaP1LimitCutStage` puts every seat where the fight asks) and pin the landing
    at 3 y, whatever the constant says.
- The gate has no `-Phase` for TEA; its human suite is the `tea-p1-human` bucket. The soak filter is
  `FullyQualifiedName~ScenarioCatalogTests&Name~Alexander&Name~Limit&Name~Cut`.

## TEA P1 Living Liquid
`Scenarios/Tea/P1LivingLiquid/`, "Living Liquid", listed ahead of Limit Cut: the fight from the Living Liquid's first
Protean Wave to its death, in two parts with a Start from pick (`TeaP1LivingLiquidParts`), each on its own clock:
Protean 1 (the fight's clock at 62.0) and Cascade 2 (85.8, so 23.8 s into a run of both). The opening before them
(the split, Provoke, the invulnerability, the dolls, the first Emboli, the busters at 56.7) isn't built: it comes as a
third, earlier part, and the part enum keeps 0 for it, since kept setups hold a part as a number. One strat, EU
`[LPDU] Light PF`; party-only, no solo or multiplayer.
- Script and check: no enmity and no health are played. The raidwides (Cascade, Splash, both Hands) hurt nobody,
  Hand of Pain never fails, and the Living Liquid falls on the timeline, 39.7 s into Cascade 2, which ends the run;
  the enrage isn't played. Positions are played for real and the human's own presses are heard. A mistake kills by
  the fight's own rule, with the reason, never a strat spot.
- `TeaP1LivingLiquid` (`TeaP1Mechanics.cs`) is the one piece that plays both bosses and every attack, as
  `TopP6AlphaOmega` does for P6; the scenario's two methods read as the timeline:
  - Protean 1: the telegraphed Protean Wave's cast 4.8 (lands 7.8), Hand of Pain 5.0, the first unseen set and the
    Sluices 9.9 (the Sluices land 12.9), the second set 13.0, six Splashes from 19.1, the Drainage tethers 20.0, Hand
    of Pain 24.1, Drainage 24.6, with which the first tornadoes leave;
  - Cascade 2: Cascade 1.9, landing at 5.9 with the second tornadoes; Liquid Gaol 10.1 and Throttle 10.5, running out
    at 19.5; Protean Wave 18.2 (lands 21.2), Hand of Pain 20.2, the first set and the Sluices 23.3, the tornadoes'
    telegraphed waves aimed 24.2 (land 27.2), the second set 26.3, the tornadoes' unseen waves 29.3, the Hand's glow
    33.2, Pressurize 33.3 and two Emboli 33.9, the slam 38.4, the Liquid falls 39.7.
  - A cast with a bar decides whom it hits as the bar ends, 0.3 s before the time it lands. No Fluid Swing or Fluid
    Strike falls in these parts (the cleaves are at 11.4, 37.7, 56.7 and 134.1): both actions and their tank-job
    split are defined and tested for the opening, and scheduled by nothing.
  - Its 17 invisible 9020 helpers are a `SpawnedHelperPool`, every helper cast going through `CastFromHelper`; the
    most busy at once is 11, as Cascade 2's Sluices land with its second set (`TeaP1LivingLiquidHelperBudgetTests`).
  - The bosses, and a Protean 1 start's tornadoes, are spawned in `Run` itself, ahead of the strat's `Run`: the
    strat's first move reads where the Liquid stands, and events of one time fire in the order they were added.
- The tornadoes (`TeaP1Tornadoes`, `TeaP1TornadoLayout`): three Liquid Rages 14.5 y out on three quarters, each with
  a 7.6 y puddle, and the quarter without one names the frame everything is written in (relative north; the
  tornadoes are then West, Middle and East). A puddle kills whoever enters it, from 1 s after it appears, since
  Cascade 2's set can land under someone who had no way to know where; bots walk round each as an obstacle.
- Targets: the Living Liquid on the main tank's seat and the Liquid Hand on the off tank's, from 0.5. Each follows
  and faces its target through the engine's `Follow`, which stops 4.0 y (the Liquid) and 2.1 y (the Hand) from it
  and holds still through a cast, so a human tank drags its boss. `SetTarget` is public for the opening's Provoke;
  nothing here calls it again.
  - A boss walks after a tank that steps more than its stopping distance away, where the fight's stays put while its
    tank is within reach. So a main tank's tether fetch drags the Liquid up to 3.4 y off the middle, and Protean 2's
    spots, round the Liquid and beside the tornadoes, only fit with it on the middle. The bot leads it back; no
    move of the strat's reaches a human, so the start's chat line asks a human main tank to, and one who only turns
    it north where it stands loses bots to the tornadoes' waves from about 2.3 y off (4 of the human suite's 40
    runs). A boss that held still within reach would end the drag; that is a change to the mechanics, not made.
- The facing lock: from a telegraphed Protean Wave's cast until 1 s after its second unseen set the Liquid keeps its
  facing whatever its target does. The engine turns a walking enemy along its path, so the lock is the follow
  released, and the Liquid also stands still meanwhile (UNVERIFIED that the fight's does).
- Protean Waves (15 degrees either side, 40 y): the telegraphed cast aims one along the Liquid's facing and one at
  every player as it starts; an unseen set has one straight ahead and one at each of the four closest; the
  tornadoes aim one each at their closest player, telegraphed at 24.2 and unseen at 29.3. Every wave leaves Water
  Resistance Down for 5 s, and a wave on someone who carries it kills, bots included, short of a tank's
  invulnerability; a first wave never does. Two waves that catch one player in one frame are a first and a second.
  The telegraphed rows also knock back 10 y straight away from their source, through Limit Cut's rim-capped
  `Thrown`: bots are not pushed, nor is a human whose anti-knockback is up.
- Sluice: a 5 y circle under each of the four farthest from the Liquid as the first set goes off, lethal 3 s later.
- Drainage: two of the three tornadoes tether a player each at 20.0. The tethers are passable and declared player
  first, as P5 Omega's Blaster tethers are: only a tether's source carries it, and a pass skips whoever carries one,
  so nobody can hold both, a free tank can take a tether from a tank that holds it, and two holding tanks can't
  swap. The fight draws the channel from the tornado to the player; declared this way it shows from the player to
  the tornado. At 24.6 a non-tank seat that holds one dies, so does anyone but the holder within 6 y of a holder (a
  tank seat that holds none too, where the fight's one buster would likely leave a tank alive), and a
  tank seat in two circles; a human tank on a tank's job needs a cooldown under its own
  (`P1Rules.DrainageMitigation`, asked through `DamageCheck.LethalCause` only with "Require tank mitigation" and
  "Resolve your own actions" on, as the Gauntlet asks Cosmo Dive's). In the stack a tether that starts on one of the
  six passes at once to whoever of them stands in its line.
- Throttle: Liquid Gaol puts it on six for 9 s, and whoever still carries it then dies. An Esuna or a Warden's Paean
  heard on a carrier as it resolves takes it off (`TeaP1Presses` reports the cleanse, with "Resolve your own
  actions" on or off; one aimed at no party member counts for the player). The piece keeps its own list of
  carriers: a status that runs out is gone before anything scheduled at its end can see it, a frame early on a long
  frame. The order of the cleanses is nobody's rule.
- The Hand's last slam: as it glows it shows a palm when the bosses' centres are more than 12 y apart, else a fist.
  The slam, an instant cast, kills everyone when a palm finds them more than 5.1 y apart or a fist within 12 y.
- Emboli: only the second pair is played. Two orbs leave two tornadoes 0.6 s after Pressurize for where the two
  bosses stood at the cast, at 1.5 y/s; a player or a boss within 1 y of one kills everyone. At that speed an orb is
  still inside its own tornado's puddle until 0.7 s before the Liquid falls, so it can hardly be touched: the rule
  tests pin faster ones. The first pair belongs to the opening, which decides whether it outlives the fight's 62.0.
- Rolls, each drawn whether or not it is forced: both tornado layouts (8 each; the second may repeat the first), the
  tornado without a tether and the two seats the tethers start on, Throttle's six, the tornado without an Embolus
  and which orb heads for which boss. Settings: Start from; each part's Tornadoes (Auto or the open quarter);
  Drainage tethers start on (Auto, The tanks, Others); Your Throttle (Auto, Yes, No). The per-seat Throttle rows
  need a host's session, as Limit Cut's numbers do, and are reached only by tests.
- A start puts both bosses in the middle, the Liquid turned to relative south-east and the Hand 2.1 y to the
  south-west, in the first tornadoes' frame, which a run from Cascade 2 never shows; the strat's 0 s teleport places
  the party, the player's seat included.
- The strat (`TeaP1LivingLiquidLpduAi`) writes every spot with the open quarter north and turns it with the
  tornadoes standing then; a bait round the Liquid hangs off where it stands as the move is called.
  - The main tank holds the Liquid by standing within its 4.0 y, and at the guides' 7.4 y only while the facing is
    locked. Its Drainage spot is an arena point 3.9 y south-east of the middle, since its own fetch drags the Liquid;
    at Cascade 2's 6.4 it steps onto the middle and at 7.2 walks on 4 y straight away from the Liquid, which follows
    onto the middle, and at 9.5 it turns the Liquid north.
  - Each tank fetches its own tether (`Intercept`, every 0.25 s from 20.25) and walks it to its spot: the east
    tornado's is the main tank's and the west one's the off tank's, and the middle one's goes to the tank whose side
    has none. A free tank takes its own even off the other tank; only both tanks crossed from the start stay so.
  - Before Cascade 2 lands everyone gathers within 6 y of the middle, inside any new puddle's reach.
  - The healers cleanse at 11.2, 13.7, 16.2 and 18.7, each itself first, then H1 down the party list and H2 up it.
  - In Protean 2 the healers and ranged bait the tornadoes' aim 8.4 to 8.5 y from a tornado and then sprint in to be
    the second set's four closest, the tightest move of the strat; the off tank leaves for its tornado a second
    after the melee, clear of the middle tornado's telegraph.
  - For the last hand the tanks hold the bosses 24 y apart, and as the palm shows each walks through the middle and
    stops its boss's stopping distance beyond it.
- `TeaP1LivingLiquidPlan` is the one piece of strat the scenario reads, for the start's chat lines: a human tank's
  tether, and that two tanks who start with each other's keep them, each on its own spot; a human main tank's
  leading the Liquid back onto the middle and turning it to the open quarter before Protean 2; a human healer's
  cleanse order. A healer's seat on a job without Esuna is held to no cleanse: the
  run cleanses for it. The strat presses nothing for a human in its seat. In headless runs the debug driver walks
  the player's seat and presses nothing, so the strat cleanses for it too, unless a test sets the internal
  `PlayerPressesItsOwnActions`.
- UNVERIFIED in `TeaP1Constants`: the ring's 14.5 y and the puddle's 7.6 y and its object, the waves' and cleaves'
  half-angles, Water Resistance Down's row (2144) and its 5 s, the knockback's 10 y and speed, Throttle's 9 s, the
  puddle's first harmless second, the 1 s the Liquid holds its facing after its last set, the bosses' walk speed,
  the Hand's three distances and which model state and which action is the palm's, Drainage's 0.2 mitigation, the
  Embolus's speed and 1 y, the 17 helpers, and where a start puts the bosses. In the State and the mechanics: the
  odds of every roll, 20.0 for the tethers and whom they pick, that a holder can't take the second tether, that one
  Drainage circle kills a tank that holds no tether, that the Liquid's own telegraphed wave is a fixed cone ahead,
  which tornado casts Liquid Gaol, and that each Embolus heads for one boss's spot and stops there. In the plan:
  that the client's party list shows the seats in the order the cleanses go by (MT, OT, H1, H2, M1, M2, R1, R2); it
  may sort each role by job.
- Change bot movement only with `TeaP1LivingLiquidLpduMarginTests` and both human suites green.
  - The rule tests play no strat (`TeaP1LivingLiquidStage` teleports every seat cue by cue; its header has the
    timeline). `TeaP1LivingLiquidLpduAiTests` reads the strat's spots against the stage's on every layout, and the
    fetch from all 21 starts.
  - The margin suite holds every pick by distance, every cone, Sluice and puddle, Drainage and the last hand to
    floors derived from the spots (`MarginSeeds` asks for more runs). The thinnest is the tornadoes' aim, the next
    closest 0.94 y behind the baiter. An Embolus would first touch a bot (M1) from 2.33 y/s: move M1's kill spot
    before raising `P1Rules.EmbolusSpeed` past 2.
  - The human suites (`TeaP1LivingLiquidProtean1LpduHumanTests`, `...Cascade2LpduHumanTests`, on
    `TeaP1LivingLiquidHuman`) seat a human from the first frame who reads its north off the tornadoes and sets off
    from what a player sees (each telegraph's bar, each unseen set landing, the tethers, the Throttle icons, the
    palm), the strat's beat and 0, 0.25 or 0.5 s later, walking or sprinting, round the water, on every layout. A
    tank walks into its own tether wherever both start and presses Rampart; a healer presses Esuna every 2.5 s;
    the main tank also plays the guides' edge-of-reach spots, dragging the Liquid 3.4 y, and turns it up to 20
    degrees off south for Protean 1 and off north for Protean 2. In both parts a walking human also stands a yalm
    farther from the Liquid, or a yalm aside, on every spot it baits from, short of the water. Nobody may die, but
    in these cases, each its own test: a tank that never takes its tether
    (the six die), a healer that never cleanses (two die) or starts three GCDs late (one), an off tank that never
    brings the Hand in (everyone), a main tank that never leads the Liquid back onto the middle (4 of 40 runs), and
    the run in walked 0.5 s late: R1 from its spot and H2 from a yalm farther out are 0.2 to 0.7 y farther from the
    Liquid than the main tank as the second set goes off (the main tank dies; 0.25 s late they are in time), and R2
    from a yalm aside is still on its way in, along H1's wave, on 2 of 24 rolls (both die).
- The gate's buckets are `tea-p1-protean-human`, `tea-p1-cascade-human` and `tea-p1-liquid` (the rest of the
  scenario's tests). The soak filter is
  `FullyQualifiedName~ScenarioCatalogTests&Name~Alexander&Name~Living&Name~Liquid`.
