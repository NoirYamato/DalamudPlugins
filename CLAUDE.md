@AGENTS.md

# NoirMech

NoirMech is a fork of [AnoMech](https://github.com/anomek/AnoMech). `AGENTS.md` above is upstream's guide and
stays byte-identical to upstream; this file adds only what NoirMech changes. NoirMech stands on upstream's
unreleased `master` at `bd24935`, merged ahead of a release (see Repos), so `git diff bd24935` shows every NoirMech
change:
- the rename (below);
- TOP P2 Party Synergy: the EU `[LPDU] BPOG/GOPB` strat (`TopP2PartySynergyLpduAi`), listed first in
  `AiStrats` so EU and it are the default pick (upstream's NA strat is index 1), `Group => "NA"` on upstream's
  strat, and no Spotlight in solo runs (upstream's `Run_Spotlight` takes `solo` and returns at once; a solo stack
  target stood alone and died). Its eye-spread spots are
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
    `AdjustEyePosition`, all of which upstream's Tuufless strat, its subclass at index 2, inherits;
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
    `P6WaveCannon2/` files are as bd24935 has them, off the menu, and their tests still run because
    `tests/AnoMech.Tests/Support/ScenarioRun.cs` constructs a scenario the catalog lacks;
  - in upstream's `TopActions.WaveCannonProtean`, `delayToEffectResult` on the lasers' Magic Vulnerability Up, so a
    laser of the next set still finds it (below);
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
- TEA P1 (below): in upstream's `TeaZone.cs`, NoirMech's `P1` phase (Fair Skies, BGM 706) beside upstream's `I1`
  and `P3`, its EU waymark preset ahead of upstream's PF Trines, and its reason for a death past the rim
  (`WentPastTheRim`); `TeaP1Constants.cs`, `TeaP1Actions.cs`, `TeaP1Presses.cs` (the human's own anti-knockback
  presses, cleanses, Provokes, invulnerability and hits on an enemy), `TeaP1Mechanics.cs` (the Living Liquid, the
  Liquid Hand, the tornadoes and the attacks they play), `TeaP1JagdDolls.cs` (the pull's four dolls), and two
  scenarios, each with an `[LPDU] Light PF` strat: `P1LivingLiquid/`, the fight from the pull to the kill (the
  strat's pull is in `TeaP1LivingLiquidLpduAi.Pull.cs`, and the scenario's `OnStatusLost` hears a Throttle the
  engine takes off), and `P1LimitCut/`, the intermission. In upstream's `ScenarioCatalog.cs` TEA comes last: these
  two, then upstream's P3 Wormhole; upstream's I1 Limit Cut is left off the menu, its files and tests kept (they run
  through the catalog-less `ScenarioRun`);
- TEA P3 Wormhole (below): the EU `[LPDU] Light PF` strat (`TeaP3WormholeLpduAi`), listed first in upstream's
  `AiStrats` so EU and it are the default pick; upstream's NAUR strat is index 1 and gains `Group => "NA"`, and
  upstream's `TeaP3WormholeScenarioTests` pass `NaStrat = 1`;
- edits the merge of `bd24935` made in upstream's files (Merging): `ZoneControl.cs`'s unload safety stop,
  `ScenarioSession.cs`'s two chat lines and `MainWindow.cs`'s title name `Plugin.DisplayName`; the main window's
  Pause, Retry and paused title re-ported onto upstream's rebuilt run controls; and in multiplayer,
  `SpawnNpcPacket` (`GamePackets.cs`, `PacketRecorder.cs`, `PacketReplay.cs`) carries the spawn's `AnimationState`,
  which upstream's `af36202` gave `EnemySpawnConfig` and the native spawn but not the packet, so a peer spawned
  Dancing Mad P3 Limit Cut's Kefka and clones and the UWU primals of Ultimate Predation and Suppression in their
  default state (21 `PeerParityTests` cases failed on bd24935 itself). In upstream's `ProtocolTests` the peer types
  drop `peerDispelled`, whose message bd24935 deleted (`RelayWire.PeerMessageTypes` still lists it, harmlessly);
- the Automarkers setting (`Scenarios/Automarkers.cs`, On/Off, default On): a party-setup option rather than a
  roll, which the P3 Intermission, P3 Monitors and both P5 Delta strats read to place signs;
- the pull-log reports (below): two [Explicit] tests in `tests/AnoMech.Tests/Reports/`, one for TOP and one for TEA,
  that check their values, UNVERIFIED or settled, against the user's ACT logs, and the ACT log parser they read them
  with;
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
  `ANOMECH_RELAY_*`), so NoirMech players can use AnoMech relays, and upstream's relay pieces:
  `MultiplayerUi.RelayReadmeUrl` (its relay host's README) and `DefaultRelay` (upstream's public server, which the
  user kept as NoirMech's default; a saved custom relay must be picked again under "Custom server");
- log file names and the `# AnoMech build=` header the log rotation parses, the other log headers (the event log's,
  the damage debug dump's), the `[AnoMechWatch]` log tag, the damage debug texture's name, ImGui `###AnoMech*` and
  `##AnoMech*` ids (`##AnoMechFailureBanner` among them), and the `.github/workflows/*` (upstream's: `pr-build.yml`
  runs, harmlessly, on every push to the plugin repo; the release workflows can't run there).

`PluginDisplayNameTests` reads every string literal in `AnoMech/` and fails on one that names AnoMech outside these
kinds (`AnoMechPresence.cs`, about AnoMech itself, aside), so a merge that brings new text naming AnoMech fails until
it is renamed or kept on purpose.

NoirMech must not run alongside AnoMech: they install the same hooks and share ImGui window ids. While AnoMech is
loaded NoirMech still loads and hooks, but refuses every zone-in and start and leaves a stay already up (see AnoMech
alongside).

## Repos, branches, releases
- Upstream is `anomek/AnoMech`. Merge only its **release tags** into `noirmech-stable`; its `master` holds
  unreleased work. Take upstream's side of engine conflicts and re-port NoirMech's changes on top (see the
  `Merge AnoMech v0.5.0.0` commit). One recorded exception: at the user's request (2026-11-26), `master` at
  `bd24935`, 74 commits past v0.5.1.0, was merged ahead of a release to ship upstream's TEA Wormhole in 0.5.1.15
  (`Merge AnoMech master at bd24935 ahead of its release`). The next upstream tag that contains bd24935 merges as
  usual and brings only the commits after it.
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
`powershell -ExecutionPolicy Bypass -File tools\upstream-check.ps1 [-Ref <tag>] [-Base <commit>]` (default
`upstream/master`) previews a merge without making one: the files that would conflict, and NoirMech's own lines
(changed or added since the AnoMech commit last merged) that use a declaration the ref deletes. Its base defaults to
the merge base of HEAD and the ref, `bd24935` for any ref that contains it (`-Base bd24935` says so outright); the
newest tag HEAD contains, v0.5.1.0, would count upstream's 74 commits up to bd24935 as NoirMech's lines. It greps,
so also build and test a trial merge in a throwaway detached worktree.
- Conflicts come wherever upstream edits, after bd24935, the NoirMech lines in its files (the list above). The
  bd24935 merge settled its twelve so, the record for the next:
  - `Game.cs`: upstream's `TickZoneLoad` first, then NoirMech's fall check (`!UserPaused`);
  - `ScenarioSession.cs` and `SimWorld.cs`: both sides, the pause's members and halt methods beside upstream's
    `Issues`, `Ended` and `SimEvents`;
  - `LocalPlayerInputHooks.cs`: the `ActionsBlocked` return first, ahead of upstream's `GetAdjustedActionId` line;
  - `MainWindow.cs`: upstream's run controls, title (`Title(failed)`, set every frame) and status (`RunStatus`), with
    NoirMech's Pause beside the solo Stop, Retry after the start buttons, `PausedTitle` over upstream's title and a
    Paused status; upstream's Failed covers NoirMech's Frozen;
  - `TopP2PartySynergyScenario.cs`: upstream's side of both hunks, with the solo skip in its `Run_Spotlight`.
    Resolve the conflicts, not the file: `AiStrats` (LPDU first) merges clean, and upstream's whole file would put
    its NA strat back at index 0;
  - `TopP5DeltaAi.cs`: upstream's `virtual` `Name` and NoirMech's `Group => "NA"`; `TopP5DeltaScenario.cs`: LPDU,
    Standard, Tuufless;
  - `ScenarioCatalog.cs`: TEA last, without upstream's I1 Limit Cut; `TeaZone.cs`, which both sides added:
    upstream's file with NoirMech's P1 phase, EU preset first and rim reason;
  - `README.md`: NoirMech's blocks with upstream's credits; `ScenarioRun.cs`: NoirMech's catalog-less construction,
    `RunUntilStopped` and `probe.Stopped` beside upstream's four-argument `PartyMemberKilled`, `MechanicFailed`,
    `HelperPoolExhausted` and `CompletedFailed`.
- What merged clean but needed NoirMech's hand, each in its section: `TimelineRun.cs` (upstream's four-argument
  `PartyMemberKilled`, and a run fails on `HelperPoolExhausted`), the P6 Gauntlet's limit breaks on upstream's
  `JobActions` and its helpers on TOP's zone pool, the lasers' vulnerability, Drill on a run that ends failed without
  a freeze, Retry ending the speed buffs, the TEA report's barrier and map-effect findings, the lint's `Known`, a
  Throttle the engine's Esuna takes off, and the multiplayer spawn's animation state.
- Still optional: P3 and P4 spawn their own 9020 helpers beside TOP's zone pool (Intermission 10, Hello World 12,
  Monitors 14, Blue Screen 19) and play the same with it; casting through `omega.CastFromHelper(action, target,
  at: ...)` would drop them (Hello World would then keep its towers' placements itself, which its resolve reads off
  the tower helpers). TEA P1's own `SpawnedHelperPool`s (Living Liquid 17, Limit Cut 3) stand beside the zone's 17:
  `TeaP1LivingLiquid.CastFromHelper` can become `Liquid?.CastFromHelper(...)` and the Hawk Blasters Cruise Chaser's,
  which leaves `SpawnedHelperPool` no user. That stops being optional once P2, with a pool of its own, lands.
- `LinkInterruptionTests`: upstream's `d1554f5` marks the whole fixture `[Ignore("Too slow")]`. NoirMech keeps the
  skip, so no gate runs the dropped-link coverage of every multiplayer message, NoirMech's P5 Delta replay fields
  and the Wormhole strats' included; `[Explicit("Too slow")]` and a bucket of its own would bring it back.

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
    it. The catalog's sunny days are split by family (`catalog-top`, `catalog-dancing`, `catalog-black-hole`,
    `catalog-fru`, `catalog-tea`, `catalog-other`); `catalog-fru`'s, `catalog-tea`'s and `catalog-other`'s times
    were scaled from a quick run after the bd24935 merge, and every bucket's want refreshing from the first full
    run's summary since. Upstream's ignored `LinkInterruptionTests` (Merging) have no bucket: they fall to the rest,
    skipped, and the Wormhole human suite runs there too.
  - `-Preset full` (the default) runs everything at the run settings' seeds: 12 minutes here with the defaults
    before the bd24935 merge, the PC's CPU load staying under a quarter, against about 37 for a plain `dotnet test`
    and 28 for one capped process. `-Preset quick` drops the `*HumanTests` suites and runs 4 sunny-day seeds and 1
    pause seed, about 3 minutes then, and about 8 with `-Shards 2` since; `-Phase P3` runs one TOP phase's `TopP3*`
    classes and the catalog-wide tests' cases for its scenarios, with `-Preset quick` too if wanted; `-SunnySeeds
    2000` makes any of them a soak.
- A green full run of a clean tree writes `green <HEAD> full` to `TestResults\green-stamp.txt` (gitignored), which
  `publish.ps1` requires. A full run deletes it first, and one with uncommitted changes, `-NoBuild` or
  `-SunnySeeds`, or one HEAD moved under, writes none.
- `test.runsettings` sets the seeds: `SunnySeeds` (40) for each catalog strat's sunny day and `PauseSeeds` (5) for
  `GamePauseTests`' sweep of every catalog strat with pauses; override either as above.
- `StratLintTests` runs every catalog strat on 3 fixed seeds (`LintSeeds`) and reads `AiManager`'s move lines in
  each trace. A `Move` with an `arrivalTime` measures its bots' departures from where they stand as it fires, and
  the deferred `MoveTo` can't be cancelled, so the lint fails when a bot's deferred departure lands at or after
  that bot's next move, or when a deferred move is planned while its bot still walks from the one before (the 10s
  Party Synergy flex), naming both sites. `Known` holds two of upstream's strats, left as they are: FRU P3
  Apocalypse's walk at 36.05, which two bots finish up to 0.19s after the 37.0 deadline move plans their
  departures, inside `AiManager`'s 0.25s margin; and FRU P2 Light Rampant [JP · Mana], whose 33.85 move sends every
  bot to the banish spot its 32.9 move sent some to, with the same 37.1 deadline, so a departure still deferred goes
  to the same spot in time. An entry fails the test once it's no longer found.
- `TimelineRun` fails a run on `HelperPoolExhausted`, as `ScenarioRun` does, so a timeline test on a zone's pool
  can't drop a cast unseen.

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
- TEA's report is `Reports/TeaPullLogs/TeaPullLogReport.cs` (`TeaUnverifiedValues`, run the same way with
  `--filter Name=TeaUnverifiedValues`, writing `pull-logs/tea-unverified.txt`). Its measure classes
  (`TeaPullLogReport.Measures`) cover P1: the timeline, the actors, the Protean Waves, the Liquid Hand and the cleaves,
  the dolls and Emboli, Drainage and Throttle, Limit Cut and the deaths. `TeaPullLogReportTests` is its companion.
  - What both reports share is in `Reports/PullLogs/`: `PullLog` (a pull as typed events; `TopPull` and `TeaPull`
    derive from it), `LogFinding`, `SimSource`, `LethalHits` and the report's text.
  - A fifth verdict, "measured, not in the sim", is for what the fight does that no scenario plays: the bosses' and
    the dolls' auto-attacks, a doll killed, health (maximum hit points, the Liquid's halving at the split, Hand of
    Pain's gap) and the like. Such a finding points at the scenario's own note that it isn't played, so building it,
    which rewords the note, fails `TeaPullLogReportTests` for every finding that then has a value to check.
  - A part's literals are read from its `fight.X(start + 4.8f)` lines (`SimSource.PartTime`) and held against the
    event's time after the cast the part is timed by: the Liquid's telegraphed cast for Protean 1, Cascade 2's cast for
    Cascade 2, the first Hawk Blaster pair for Limit Cut. The pull's lines take no `start` (`fight.X(38.6f)`,
    `SimSource.FightTime`) and are held against the combat flag, which also gives each part's place on the fight's
    clock; a pull the log joined after its start (the Living Liquid not at full health as it opened) is left out of
    those.
  - The pull's findings are held against the sim: its timeline, the Liquid before the pull and the Hand's appearing,
    a Provoke taking hold (the Hand's set-off after the main tank's Provoke), the dolls' spots, tethers, Exhausts,
    stand-off and walking speed, an untouched doll a boss passes, and the feed. A doll's tether is held to its delay
    after the first player's action on it (the median within 0.15 s of `DollTethersAfterItsFirstHit`). The feed's
    distance is held as a line, not a mean: dolls walk in across it, so the finding differs when a feed went out
    from beyond `DollIsFedWithin`, and, with 20 feeds or more, when none came within a quarter yalm of it. The
    cleaves' damage is listed with what covered it and reads inconclusive: a tank lived through a bare Fluid Swing,
    no Fluid Strike was taken bare and no non-tank was hit by either, so the sim asks for no cooldown.
  - A pull carries what stood as it opened (`ActPull.Standing`: each combatant's 03 line, each object's 261 Add and
    the map effects since the last pull), which is where the Liquid's spot before the pull, the helpers and the
    entrance barrier are read from; a combatant of the pull before can linger there, so the report takes what joined
    with the pull's own Living Liquid. The plugin's own runs are left out by asking for a cast or an ability of the
    fight's own actors: a fairy's ids sit among the enemies'. The barrier's and the map effects' findings read
    upstream's zone files by path (`TeaReplayData.cs`, `TeaConstants.ArenaStates`), since `TeaSource` holds
    NoirMech's: each pull's resets are held against the seven slots every run sets as it starts.
  - A boss's own spot and facing on a cast, an ability or a move line are the server's. "Closest" and "farthest"
    among players are told apart no finer than a yalm, and a player's bearing from a boss a few yalms away turns by
    ten degrees for a yalm of error. A player just raised (Transcendent) is left out of kill rules as one under a
    tank's invulnerability is; the wave and Cruise Chaser rules also leave out a hit on a player an earlier hit had
    already left at 0 HP.
  - Two clocks. ACT stamps the lines it writes from memory (a combatant's or an object's add and remove, the combat
    flag) before the packet lines of the same moment. `PullLog.LagOfMemoryLines` measures by how much, per pull: the
    median over the memory lines that two packet lines less than 0.1 s apart bracket, 0.35 to 0.44 s in these logs,
    and nothing with fewer than 20 readings. `TeaPull` moves its memory lines onto the packets' clock
    (`alignMemoryLines`), so what is timed from the flag or from an actor's appearing sits on the clock of the casts;
    the report prints each pull's lag. TOP's pulls are read as before.
  - A hit carries its own effects (`ActHit.Applies`, `KnockbackRow`): the statuses it applied and the Knockback
    sheet's row it threw by. A debuff is tied to the hit that left it and timed from that line, and a throw is read
    as its row's distance, with the measured slides beside it. `ActMusic` is the duty's change of BGM row, kept in a
    pull's `Standing` when it comes between pulls.
  - What the sim plays on its own terms is measured as the sim asks it: a boss's stand-off from its target as the
    distance past their two hitboxes while the target stood still, an Embolus's contact as the distance past the
    toucher's hitbox, the rim by who was seen past it and who came back.
  - A verdict reads matches only where a sample could have said otherwise. A slam's distance needs slams on both
    sides of it, and its finding prints the room the pulls leave for the line; with every slam on one side it reads
    inconclusive. The rim under Cruise Chaser's throws counts every player a hit left alive whose named row ends a
    yalm and more past the rim, and reads DIFFERS if one that was seen to slide lived on; one that lived without
    sliding is listed and left out, as is a throw's length taken from a hit that names no row.
  - A finding over several literals (Limit Cut's series of blasts, cleaves and charges) names every row's line, so a
    literal the scenario no longer holds shows as `:?` and "not found in the scenario", and `TeaPullLogReportTests`
    fails on it. That test also feeds every measure pulls that show little (a wipe in the first seconds, one boss
    alone, an Embolus on two move lines) and fails on a NaN in any measured or detail line.
  - The retune of both TEA scenarios took its values from this report; `tea-unverified.txt` now reads matches for
    them, "inconclusive" for what one or two pulls showed (those keep an UNVERIFIED mark with the bounds seen) and
    "measured, not in the sim" for health and the auto-attacks. The pull was built from the same report; nobody has
    run it on the logs since, so the pull's findings are tested on made-up pulls only. Three of the pull's unmarked
    constants were read off that report's rows by hand and have no "matches" yet: `ProvokeTakesHold` (0.67),
    `DollStopsPastTheHitboxes` (a median 3.3 y) and `DollTethersAfterItsFirstHit` (0.6). Run `TeaUnverifiedValues`
    on the logs before the next release and settle what it says of them; the six Provokes seen on the Living Liquid
    are not measured, since the finding reads the Hand's set-off only.
- ACT's folder may hold only recent logs: older ones have been moved out of it into an archive folder and renamed,
  and the reports read `Network_*.log` in the one folder they are given. TOP's logs went that way, so
  `TopUnverifiedValues` finds no TOP pull in ACT's folder now; point `PullLogs` at a folder that holds them.

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
`Game.UserPaused` through `Game.Pause`/`Resume`/`TogglePause` (all via `Framework.Run`). The button sits beside
upstream's solo Stop and hides with it through the death freeze, where upstream's Leave shows; while paused the title
reads PAUSED (`PausedTitle`, over upstream's `Title(failed)`) and upstream's status line Paused.
- Refused with a chat line (`Game.PauseRefusal`) when there is no run, in a multiplayer session (only `Local`
  runs pause), or during the death freeze (`Paused`). Redundant requests do nothing.
- While halted (`IsHalted` = user pause or death freeze) the scheduler, children and `IScenario.Tick` don't
  run. In Local runs `SimWorld.BeginHalt`/`HoldHalted`/`EndHalt` hold the scene: `SimCast` writes the native
  cast time back every frame (near the end it holds 0.45s short so the omen stays drawn), statuses are resent so
  their timers stand still, bots and NPCs get one settle move to where the sim stopped them, and
  `ActorFreeze` stops the animations of every enemy and doppel (never the local player). Both halts hold the
  scene; only the user pause also locks the player. A halted run gets no `SimWorld.Tick`, so upstream's sim events
  (`IWorldEventListener`) queue nothing new and reach the scenario after the resume.
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
  leaves the run's settings in place. It ends Sprint, Peloton and Smudge (`ZoneControl.EndSpeedBuffs`) before its
  start gate, as upstream's Start does, starts through `EnterAndStart` as Start does, zoning in from the inn and
  waiting out a settle, and the window first selects the retried scenario.
- Drill (beside Auto-restart, in every build; forced off in a session): a run in which anyone died plays its roll
  again 3 s after it freezes (`RollReplay.FreezeHoldSeconds`), so the player sees the frozen scene, or, when godmode
  kept everyone up, as soon as it ends; a run upstream's `SimWorld.Fail` failed without a freeze plays again 3 s
  after it ends (`CompletedFailed`, which raises no `RunFinished`, and `DrillReplays` counts it); a clean run moves on
  to a fresh roll. Deaths switch Auto-restart off but not Drill, and with Drill on Drill makes the restart. A run
  stopped before it froze isn't played again.
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
  jitter, so it stays within the rot's reach. Upstream's `SimTether` ends itself when an end dies (`TetherEnded`), so
  a tether goes with a death, as Hello World's regressions already do.
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
Upstream's Tuufless strat (`TopP5DeltaTuuflessAi`, a subclass of its Standard) is third and inherits Standard's NA
group, the automarkers and the eye's turn; `TopP5DeltaLpduAiTests` holds the order.
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
- Every helper cast goes through `TopP6AlphaOmega.CastFromHelper`, which casts through Alpha Omega's
  `SimEnemy.CastFromHelper` from TOP's zone pool (upstream's `HelperPool(9020, 36)`, spawned at every TOP run's
  start), each cast shown under Alpha Omega's name and linked to him. `TopP6HelperBudgetTests` holds every part and
  the whole Gauntlet to no `HelperPoolExhausted`, which `ScenarioRun` and `TimelineRun` fail a run on.
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
  - A bot's resolve goes once: with "Resolve your own actions" on, `PartyLimitBreakCast` hands it to upstream's
    `JobActions.ApplyEffects`, which sends the resolve and plays a tank LB3's status; with it off, which every
    headless run the debug driver walks has, it sends the resolve itself and `TopP6LimitBreaks` puts the tank LB3's
    status (upstream's 8 s) on the party, as it does for the run's press for the player's seat. The player's seat is
    sent nothing, since `ApplyEffects` can't play the effects without sending that client the resolve.
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
  random), upstream's Wild Charge rules, and that the bait, Flash Gale, Cosmo Dive, the spreads, the flares and the
  stack kill as they do.
- A laser's Magic Vulnerability Up is upstream's 1.96 s, shown with the laser's damage 0.64 s after it hits
  (`delayToEffectResult` in upstream's `TopActions.WaveCannonProtean`, NoirMech's one edit there), so it runs 2.6 s from
  the hit and a laser of the next set, 2.0 s on, kills a player the first set hit. Applied at the hit, as upstream's
  line has it, it ran out 0.04 s before the next set: `ALaserTargetInTheNextSetsLineDies` and upstream's own two Wave
  Cannon 2 cases (`FirstProteanTargetInSecondProteanDies`, `SecondProteanTargetHitByFirstProteanDies`) hold it.
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
`Scenarios/Tea/`: upstream's `TeaZone` (The Epic of Alexander, territory 887) with NoirMech's `P1` phase beside
upstream's `I1` and `P3`, `P1LivingLiquid/`, the phase's fight from the pull to the kill (next section), and
`P1LimitCut/`, the intermission that ends the phase, which the rest of this section is about. Ids and tunables are in
`TeaP1Constants.cs`, beside upstream's `TeaConstants.cs`: NoirMech's nested classes are all `P1`-prefixed and its
fields always qualified, so the two don't collide. Both scenarios were retuned to the pull-log report (above): what
it could not settle keeps an UNVERIFIED mark there, in `TeaP1Actions.cs` and in the scenarios, with the bounds the
pulls leave it.
- The arena has no wall, and whoever goes past its rim (r 20, the zone's `TeaConstants.Geometry.ArenaRadius`, which
  `P1Geometry.ArenaRadius` names) dies at once: `TeaZone.Run` asks the world for the boundary, with NoirMech's reason
  (`WentPastTheRim`) in every phase; upstream's own TEA tests read the environment's action alone.
- The zone's duty data is upstream's (`TeaReplayData`): the entrance barrier (EObj 2000182 at (0, 15)), 17
  invisible 9020 helpers and the arena's slots as the zone loads; every run sets the arena's eight objects and its
  seven slots (`TeaConstants.ArenaStates`) as it starts, beside P1's own helpers. Two waymark presets: NoirMech's
  "EU" first, so it is the default and kept setups don't move, then upstream's "PF Trines", the same layout within
  a yalm.
- P1 plays Fair Skies (`TeaConstants.Weather.FairSkies`, 2), the fight's weather until J Kick: a phase that sets none
  keeps the stay's last run's, so a stay that played Wormhole (Beyond Time) would have kept that into P1, since a
  start in the same zone starts in place. The phase's BGM is 706; the fight changes to 723 with J Kick, which ends
  the run here, so that isn't played.
- Limit Cut starts 8 s before the first Hawk Blaster, the party in the middle and no boss up, and ends after J Kick;
  Brute Justice and Cruise Chaser turning targetable isn't played. Party-only: no solo, no multiplayer.
  - The numbers show from 5.95, the first two together and each later one 0.045 s on.
  - Hawk Blasters (r 10, centred 14 y out, no omen, from 3 invisible 9020 helpers in a `SpawnedHelperPool`) go off in
    pairs on opposite spots at 8.0, 10.2, 12.4, 14.6 and 19.0, 21.2, 23.45, 25.6, each pair 45 degrees clockwise of
    the last and the fifth on the first's spots, and in the middle at 16.8 and 27.8.
  - Cruise Chaser, hidden until he first lands and shown by a warp-in at every landing, cleaves the odd numbers (Alpha
    Sword at 15.4, 20.05, 24.7, 29.35) and charges the even ones (Super Blassty Charge at 16.9, 21.55, 26.2, 30.85).
    He lands behind the odd number, by the reverse of that player's facing, 1.17 s before the cleave, and turns to
    that player again 0.1 s before it, so a number can't step out of its own cleave; he charges from the same spot,
    turned to the even number 0.1 s before, and stays there. The turn 0.1 s ahead is for his model, which shows a turn
    a flush late; he is turned once more as each hit goes off, so a number that crosses his centre inside that 0.1 s
    is still hit. A dead number's hit goes to a random living player. Dead means fallen: a blast's death shows 1 s
    after it (`P1Duration.HawkBlasterDamageDelay`), so a number a blast killed inside the second before he lands or
    aims still draws its own hit.
  - How far behind he lands (3 y) is UNVERIFIED: the logs place him against a running player only as the logging
    client shows that player, a yalm or two behind the server's.
  - J Kick at 34.05 hurts nobody. Brute Justice casts it himself in the fight, landing at (-6, 0); he isn't drawn
    here, so an unseen helper under his name casts it from there.
- Rolls: the numbers (a permutation of the party) and the first pair's axis, one of four. The settings pin the
  axis and the player's number. TEA has no automarkers: the user removed Limit Cut's (the setting, the State's field
  and the strat's signs), and a setup kept with the old setting still loads, its value skipped.
- A number for another seat needs a host's session, which the scenario doesn't support: a local run shows only
  "Your number". The per-seat rows, `HasPerPlayerSettings` and the one-seat-per-number conflict are there for that
  day and are reached only by tests (`PerRole.ForceSeats`).
- A Hawk Blaster kills. A hit from Cruise Chaser leaves Magic Vulnerability Up (1138, 9.96 s) and Physical
  Vulnerability Up (695, 16.96 s), and a second hit on a carrier of the physical one kills, so each cleave and charge
  may catch only its own number. Each death's reason states the fight's rule.
- Both of his hits knock back straight away from him, the cleave 5 y at 10 y/s and the charge 20 y at 40 y/s (the
  Knockback sheet's rows 2 and 3), the whole distance: a throw that ends past the rim kills there. From the strat's
  spots a charge throws its even number out of the arena, so an even number has to hold it off. Bots are taken to be
  immune and hold their spots, a headless run's debug-driven player among them. A human is thrown unless its own
  Arm's Length or Surecast was pressed in the 6 s before, or a warrior's Inner Release, whose Inner Strength holds
  15 s: `TeaP1Presses` hears the press through `IUserActions.Acted`, with "Resolve your own actions" on or off, and
  times it on the run's clock, so the rule holds the same either way: upstream's `JobActions` shows Arm's Length's and
  Surecast's statuses (1209, 160) with the module on or off, but Inner Strength (2663) only with it on, and the rule
  never reads them. It doesn't ask the job: only a warrior's client can report Inner Release. Since the merge,
  upstream's `283e864` reports a press as the id its button is adjusted to, so Inner Release comes as 7389 alone:
  the arm for 38, the Berserk a button may have been placed as, does nothing now, and is left for P2 to drop.
- In godmode a player past the rim isn't killed, and the engine's boundary reports it again on every tick it stays
  there (upstream's behaviour, left as it is).
- One scenario instance plays every run of a session. `Run` clears what the last run left on it and replaces the
  press listener, which a scenario can't dispose of as its run ends (`TeaP1LimitCutSecondRunTests`).
- One strat, EU `[LPDU] Light PF` (`TeaP1LimitCutLpduAi`). Numbers 1, 2, 5, 6 take the first pair's blaster in the
  west-to-north-east half and 3, 4, 7, 8 the opposite one, and each group follows the blasts round clockwise, its
  spot k 45k degrees on from its first. On a spot at bearing b the pair hit next stands 14 y out, the odd at b - 2
  turned clockwise along the rim and the even at b - 30, and the group's other pair waits 19.3 y out at b - 4 and
  b - 8. #1 and #2 are hit on their group's spot 2, #5 and #6 on its spot 5, #3 and #4 on their group's spot 3 and
  #7 and #8 on its spot 6. A cleaved odd walks on at once, onto the outer lane.
  - He lands 14.23 into the run for #1, 1.8 s after the blast that frees its spot, so #1 and #2 sprint that walk
    (12.5, arriving about 13.8); every other number has a round or more. A human rolled number 1 is told so as the
    numbers show (`TeaP1LimitCutScenario.NumberOneHasTheLeastTime`, the fight's own rule: he lands 1.8 s after the
    third pair, behind wherever number 1 is then, and cleaves through it from there). Only a human in the player's
    seat is told; #2, not charged until 16.9, needs no sprint.
- The strat turns its odd bots along the rim, but nothing turns a human or puts it exactly on a spot, and both of
  Cruise Chaser's hits pass close to the group's other pair, waiting at the rim:
  - Landing 3 y behind, an odd number's cleave reaches its group's waiting odd bot from about 20 degrees outward of
    the rim, and #5's reaches #4, crossing ahead of it, from about 17 degrees inward (the other odds have 35 or
    more). A step sideways after he lands turns the cleave the same way, 1.1 y for 20 degrees. The waiting odd's
    b - 4 sets the outward limit.
  - A human that stops facing the way it walked in, along the chord from its last spot, faces 22.5 degrees
    outward: on its spot exactly, its cleave still clears the waiting odd bot, by 0.2 y.
  - A #1 still walking in as he lands has him land behind its walk, short of its spot, and its cleave follows the
    chord from further back: the waiting odd bot is caught in about half the runs (below).
  - A bot caught carries the vulnerability and dies at its own cleave, or at once if it had been hit already.
  - An even number's room is a wedge round its spot, and the other group sets its near and inner edges. He lands
    at about b - 14 on the lane and turns to the even for the charge, so an even within about a yalm of where he
    lands, or inside the lane near it, turns the charge across the arena into the other group (group A, leaving
    its spot 4, for #4's). On the lane it is clear from 18 degrees behind the spot's bearing; on its spot's own
    bearing from 12 y out to about 16, where the charge reaches the group's waiting even bot; 13 y out at b - 20
    is not clear and 13.5 is. Landing further behind closes the wedge from the odd's side (at 4.5 y, b - 22 on
    the lane is caught in most runs). The human suite holds the lane at b - 20, where the guides draw the even,
    and 12.5 y out on the spot's bearing; the other edges come from replaying the strat's geometry before the
    retune, not from a test.
- UNVERIFIED in `TeaP1Constants`: the 3 y landing, that an anti-knockback holds from the press and that Inner
  Strength holds his throws off, the 1 s after which each hit's damage shows and the 0.6 s a blast keeps its helper
  (the engine's placeholders). In the State and the scenario: how often each axis comes up, that he turns to his
  number again before the cleave, and that a dead number's hit goes to anyone living.
- No source fixes these strat values, so they are UNVERIFIED picks: the run-out from the middle 12 degrees behind
  the first spot for the odds and 16 for the waiting even, the 1 y ring everyone waits on in the middle and the
  2.5 y one for J Kick, when each odd turns along the rim (13.95, 18.6, 23.25, 27.9) and every move's time. The
  turns are literals 0.28 s before he lands: `P1Duration.CruiseChaserLandsBeforeAlphaSword` can't pass about 1.4 s
  without moving the turns and #1's walk (`EachOddFacesClockwiseAlongTheRimAsCruiseChaserLandsBehindIt` fails).
- Change bot movement only with `TeaP1LimitCutLpduHumanTests` and `TeaP1LimitCutLpduMarginTests` green.
  - The human suite plays every number as a human from the first frame, walking or sprinting (#1 and #2 sprint the
    walk onto their hit spot at either pace), reacting 0, 0.25 or 0.5 s late, pressing its anti-knockback or thrown,
    over every seat. On the spot it is hit on, an odd number faces along the rim, 10 degrees inward or outward of
    it, or the way it walked in, and an even number stands on its spot, 1.5 y outward or inward of it, or on the
    lane at b - 20. Nobody may die, but in these cases, each its own test:
    - an even number that doesn't press: thrown out of the arena, it dies past the rim, and nobody else may. Thrown
      from 1.5 y inside its spot it goes across the arena and lands on the floor; the suite plays no way back from
      there, so it may die of what comes next (#2 of a Hawk Blaster, #4 in some runs of #8's charge), and nobody
      else may;
    - a #1 that reacts 0.5 s late, still short of its spot as he lands: its cleave catches its group's waiting odd
      bot in 21 of the 40 runs on the usual rolls, and the case may lose no more, and only bots to a second hit of
      his;
    - a #1 or a #2 that walks the leg onto its hit spot (`ANumberOneOrTwoThatWalksOntoItsHitSpot...`): #2 loses
      nobody at any reaction time, nor does a #1 that sets off the moment the blast goes off; a walking #1 a quarter
      second late or more is short of its spot in all 40 runs, and loses a bot in each.
    Both of #1's counts hang on where he lands against a number still walking (the 3 y, and that he takes its
    facing), neither of which is measured: count them again when either changes. No waiting spot for #5 and #6 was
    found that clears both a chord-aimed cleave and #2's charge; hitting #1 and #2 a spot earlier would end the
    squeeze and is the user's pick.
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
`Scenarios/Tea/P1LivingLiquid/`, "Living Liquid", listed ahead of Limit Cut: the fight from the pull to the Living
Liquid's death, in three parts with a Start from pick (`TeaP1LivingLiquidParts`), each on its own clock: the pull (the
fight's clock, from the first hit on the Living Liquid), Protean 1 (60.9) and Cascade 2 (84.8, so 23.9 s into a run
from Protean 1); a whole run is 124.5 s to the kill. Start from offers "The pull" first and by default, so a kept
setup that held the old default (Protean 1, stored as nothing) now starts at the pull; kept setups hold a part as a
number (0, 1, 2). The track starts at 0 s on a run from the pull. One strat, EU `[LPDU] Light PF`; party-only, no
solo or multiplayer. The pull has its own section below; this one is about the two later parts and what all three
share.
- Script and check: no enmity and no health are played. The raidwides (Cascade, Splash, both Hands) hurt nobody,
  Hand of Pain never fails, and the Living Liquid falls on the timeline, 39.7 s into Cascade 2, which ends the run;
  the enrage isn't played. Positions are played for real and the human's own presses are heard. A mistake kills by
  the fight's own rule, with the reason, never a strat spot.
- `TeaP1LivingLiquid` (`TeaP1Mechanics.cs`) is the one piece that plays both bosses and every attack, as
  `TopP6AlphaOmega` does for P6; the scenario's three methods read as the timeline (the pull's is below):
  - Protean 1: the telegraphed Protean Wave's cast 4.8 (lands 7.8), Hand of Pain 5.0, the first unseen set and the
    Sluices 9.9 (the Sluices land 12.9), the second set 13.0, the Drainage tethers 17.5, six Splashes from 19.1,
    1.12 s apart, Drainage 24.7, with which the first tornadoes leave;
  - Cascade 2: Hand of Pain 0.25 (24.15 in a run of both parts), Cascade 1.9, landing at 5.9, and the second
    tornadoes 1.4 s after that; Liquid Gaol 10.05 and Throttle 10.5, running out at 20.5; Protean Wave 18.15 (lands
    21.15), Hand of Pain 20.1, the first set and the Sluices 23.25, the tornadoes' telegraphed waves aimed 24.2 (land
    27.2), the second set 26.3, the tornadoes' unseen waves 29.25, the Hand's glow 33.15, Pressurize 33.2, with its
    two Emboli showing 0.7 s later and setting off at 38.75, the slam 38.25, the Liquid falls 39.7 and the Hand
    leaves 1.3 s later. Protean 1's Drainage and its last Splash fall 0.8 s into this part's clock: a run that starts
    here has no first tornadoes and goes without both.
  - A cast with a bar decides whom it hits as the bar ends, 0.3 s before the time it lands. No Fluid Swing or Fluid
    Strike falls in these two parts; the pull has five.
  - Its 17 invisible 9020 helpers, the fight's own count, are a `SpawnedHelperPool`, every helper cast going through
    `CastFromHelper`; the most busy at once is 11, in Cascade 2, and 3 in the pull
    (`TeaP1LivingLiquidHelperBudgetTests`). They stand beside the zone's own 17, which P1 doesn't cast from yet
    (Merging).
  - The bosses, and a Protean 1 start's tornadoes, are spawned in `Run` itself, ahead of the strat's `Run`: the
    strat's first move reads where the Liquid stands, and events of one time fire in the order they were added.
- The tornadoes (`TeaP1Tornadoes`, `TeaP1TornadoLayout`): three Liquid Rages 15 y out on three quarters, each with
  a puddle, and the quarter without one names the frame everything is written in (relative north; the tornadoes are
  then West, Middle and East). A puddle (7.6 y, UNVERIFIED: at least about 7.4 and under 9.0) kills whoever enters
  it, from 1 s after it appears, since Cascade 2's set can land under someone who had no way to know where; bots walk
  round each as an obstacle. The first set leaves with Drainage and the second as the Liquid falls, water and all
  (UNVERIFIED when the fight's second set goes).
- Targets: the Living Liquid on the main tank's seat and the Liquid Hand on the off tank's, from 0.5. A boss stands
  still, turned to its target, while that target is within 3 y past their two hitboxes (7.0 y from the Liquid's
  centre, 5.1 y from the Hand's), and beyond that walks after it at 9 y/s until it is within 2 y past them (6.0 y,
  4.1 y): `EachBossWalksAfterATargetOutOfReach`, from the scenario's `Tick`, since the engine's `Follow` walks up
  to the hitboxes whatever the distance. The Hand's stop and the walk speed were measured; the Liquid's stop was seen
  twice and both set-off distances are taken for an auto-attack's reach (UNVERIFIED). A Provoke on either boss gives
  it to whoever pressed it (the pull, below); nothing else changes a target.
  - A main tank that steps more than 7 y from the Liquid drags it, and its tether fetch does: the bot's by up to
    2.7 y, most where its own tether is the second to come free (the west one starts on the main tank itself, or its
    own starts on the off tank). Protean 2's spots, round the Liquid and beside the tornadoes, are spaced for a
    Liquid on the middle. The bot leads it back when it stands more than 0.7 y off; no move of the strat's reaches
    a human, so the start's chat line asks a human main tank to. The suite's human drags it a yalm and a half taking
    its tether where the line is nearest, and 2.2 y taking it by its tornado; left there it costs nobody. A Liquid
    left farther off than that is not played.
- The facing lock: from a telegraphed Protean Wave's cast until 3 s after its second unseen set, when its next
  auto-attack would go off, the Liquid keeps its facing whatever its target does, and stands still.
- Protean Waves (15 degrees either side, UNVERIFIED, 40 y): the telegraphed cast aims one along the Liquid's facing
  and one at every player but the Liquid's own target as it starts; an unseen set has one straight ahead and one at
  each of the four closest; the tornadoes aim one each at their closest player, telegraphed at 24.2 and unseen at
  29.25. Every wave leaves Water Resistance Down (1025) for 3.96 s, and a wave on someone who carries it kills, bots
  included, short of a tank's invulnerability; a first wave never does, but for an unseen set's wave straight ahead,
  which kills whoever it hits, carrier or not (UNVERIFIED that a tank's cooldowns don't take one through it). Two
  waves that catch one player in one frame are a first and a second. That a telegraphed row leaves the debuff at all
  is UNVERIFIED, since none was seen to hit anyone: the rows seen to leave it are the unseen ones, the Liquid's own
  cone, both cleaves and a Sluice that does not kill. A death in the pull hangs on it (below).
  In the fight the tornadoes' telegraphed waves land 3.88 to 3.98 s after Protean 2's first unseen set, either side
  of the debuff's 3.96 s, and none ever hit anyone: whether one kills a carrier of that set's wave there is a coin's
  toss nobody saw. Here a bar decides as it ends, 3.65 s after the set (24.2 + 2.7 against 23.25), so it always
  kills; retune the 3.96 s or either literal with that pair in mind.
  The telegraphed rows also knock back 10 y straight away from their source (UNVERIFIED: none was seen to hit
  anyone): bots are not pushed, nor is a human whose anti-knockback is up as the bar ends. Whom a wave throws is
  decided then, with the hit; the slide starts as the wave goes out, 0.3 s later, when the client would slide its
  player, the whole 10 y from where the human stands by then (`TeaP1Actions`' own effect,
  `ThrownAsTheWaveGoesOut`), and a throw that ends past the rim kills.
- Sluice: a 5 y circle under each of the four farthest from the Liquid as the first set goes off, lethal 3 s later,
  short of a tank's invulnerability, as a second wave is.
- Drainage: two of the three tornadoes tether a player each at 17.5, 7.2 s before it goes off. The tethers are
  passable and declared player first, as P5 Omega's Blaster tethers are: only a tether's source carries it, and a
  pass skips whoever carries one, so nobody can hold both, a free tank can take a tether from a tank that holds it,
  and two holding tanks can't swap. The fight draws the channel from the tornado to the player; declared this way it
  shows from the player to the tornado. At 24.7 a non-tank seat that holds one dies, so does anyone but the holder
  within 6 y of a holder (a tank seat that holds none too, where the fight's one buster would likely leave a tank
  alive), and a tank seat in two circles, none of which kills a seat under a tank's invulnerability
  (`TeaP1Actions.Invulnerable`); a human tank on a tank's job needs a cooldown under its own
  (`P1Rules.DrainageMitigation`, asked through `DamageCheck.LethalCause` only with "Require tank mitigation" and
  "Resolve your own actions" on, as the Gauntlet asks Cosmo Dive's). In the stack a tether that starts on one of the
  six passes at once to whoever of them stands in its line.
- Throttle: Liquid Gaol, which all three tornadoes cast, puts it on six 0.45 s later for 10 s, and whoever still
  carries it then dies. An Esuna or a Warden's Paean heard on a carrier as it resolves takes it off (`TeaP1Presses`
  reports the cleanse, with "Resolve your own actions" on or off; one aimed at no party member counts for the
  player). The piece keeps its own list of carriers: a status that runs out is gone before anything scheduled at its
  end can see it, a frame early on a long frame. It drops a carrier whose Throttle the sim took off
  (`StatusLossReason.Removed`, heard through the scenario's `OnStatusLost`): with the module on, upstream's
  `JobActions` gives Esuna and the Warden's Paean their own `DispelEffect`, which lifts the recipient's first
  dispellable status 1.11 s after the press, Throttle being the only one in P1, and reads the recipient off the press
  itself, so a press aimed at the client's current-target stand-in goes to its caster there and to the player's target
  in `TeaP1Presses`. Either cleanse counts, so a healer's own icon never goes while the run still counts it. The order
  of the cleanses is nobody's rule. A cleanse on a seat that
  carries none takes nothing off, an Esuna pressed before Throttle lands included; the Warden's Paean then leaves its
  30 s barrier on that seat instead, and Throttle doesn't land on a seat one covers (`TeaP1Cleanse.ActionId` tells
  the two apart; UNVERIFIED that the barrier stops Throttle).
- The Hand's last slam: as it glows it takes one of two forms by how far apart the bosses' centres are, the far one
  beyond 15.6 y (it showed the close one up to 15.15 y and the far one from 16.13 y). The far form, the sim's palm,
  slams as Hand of Parting and kills everyone unless the bosses are within 5.1 y by then (UNVERIFIED where between
  2.9 and 7.1 y the fight's line lies: nobody died up to 2.87 y, and the one slam that killed found them 7.15 y
  apart, the Hand still walking in); the close form, its fist, slams as Hand of Prayer and kills everyone within
  12 y (UNVERIFIED: it killed up to 2.39 y, and nobody was seen to take one apart). Which model state looks a fist
  and which a palm is UNVERIFIED. A fist with the bosses between 12 and 15.6 y apart kills nobody as it stands.
- Emboli: two pairs, one from the pull's Pressurize and one from Cascade 2's. Two orbs show on two tornadoes 0.7 s
  after Pressurize, wait there, and set off 5.55 s after it (the fight's first move line of one comes at 5.67 s, with
  it 0.2 y on its way), at 1.92 y/s, through where a boss stood at the cast and on out of the arena, where each is
  faded out, once, 21.5 y out. One bursts on a player or a boss whose hitbox comes within 1 y of its centre, and
  kills everyone (the Living Liquid was seen to burst them so; UNVERIFIED for the Hand and for a player). In
  Cascade 2 the Liquid falls 0.95 s after they set off, so they can hardly be touched: the rule tests pin faster
  ones. The pull's cross the arena for some 17 s.
- Each death's reason states its rule: a second Protean Wave on a carrier of Water Resistance Down; an unseen set's
  wave straight ahead; a Sluice going off; a Liquid Rage's water; a Drainage tether held by a non-tank, a Drainage
  aimed at someone else, two Drainages, and Drainage without a tank's cooldown; Throttle running out; the palm's slam
  with the bosses apart and the fist's with them together; a touched Embolus; going past the rim. A wave's or a
  Sluice's death shows a second after the hit, as its damage does; what the piece decides itself (a puddle, Drainage,
  Throttle, the slam, an Embolus) kills at once. Nothing kills once the Liquid has fallen.
- Rolls, each drawn whether or not it is forced: both tornado layouts (8 each; the second may repeat the first), the
  tornado without a tether and the two seats the tethers start on, Throttle's six, the tornado without an Embolus
  and which orb heads for which boss. The pull's own roll, its Emboli, is drawn from a fork of the run's stream
  (`rng.Fork`), never from the stream itself: a draw added there would shift every later roll of a seed, and the
  stream is drawn from again after the State (a tether rolled onto a dead seat). So a run from Protean 1 or Cascade 2
  rolls and plays what it did before the pull existed. Settings: Start from; each part's Tornadoes (Auto or the open
  quarter); Drainage tethers start on (Auto, The tanks, Others); Your Throttle (Auto, Yes, No). The per-seat Throttle
  rows need a host's session, as Limit Cut's numbers do, and are reached only by tests.
- A start puts both bosses in the middle, the Liquid turned to relative south-east and the Hand beside it to the
  south-west, in the first tornadoes' frame, which a run from Cascade 2 never shows; the strat's 0 s teleport places
  the party, the player's seat included.
- The strat (`TeaP1LivingLiquidLpduAi`) writes every spot with the open quarter north and turns it with the
  tornadoes standing then; a bait round the Liquid hangs off where it stands as the move is called.
  - The main tank holds the Liquid by standing 3.9 y from it, well inside its 7 y, whenever it is free to walk. Its
    Drainage spot is an arena point 3.9 y south-east of the middle. At Cascade 2's 6.4, if its fetch has dragged the
    Liquid more than 0.7 y off the middle, it walks across the middle to the far side from the Liquid, 6 y out or as
    far as it takes to stand 7.4 y from the Liquid, which follows onto the middle; at 9.5 it turns the Liquid north.
  - Each tank fetches its own tether (`Intercept`, every 0.25 s from 17.75) and walks it to its spot: the east
    tornado's is the main tank's and the west one's the off tank's, and the middle one's goes to the tank whose side
    has none. A free tank takes its own even off the other tank; only both tanks crossed from the start stay so.
  - Before Cascade 2 lands everyone gathers within 6 y of the middle, inside any new puddle's reach (7.4 y). The
    main tank, leading the Liquid back, then stands up to 6.7 y out until it turns the Liquid at 9.5, with the new
    puddles lethal from 8.3.
  - The healers cleanse at 11.2, 13.7, 16.2 and 18.7, each itself first, then H1 down the party list and H2 up it.
  - Protean 2's two stacks are taken at 12.0 on the Liquid's north-south line, and again, every 0.25 s until 17.75,
    whenever the Liquid stands more than 0.25 y from where they were last taken. The telegraphs are aimed at the
    stacks as the bar shows (18.15) and the 18.7 spots hang off the Liquid, so stacks left where a human main tank's
    Liquid stood at 12.0 put those spots inside the telegraphs. With bots alone the Liquid stands still from before
    12.0 and nothing is taken again.
  - In Protean 2 the healers and ranged bait the tornadoes' aim 8.4 to 8.5 y from a tornado and then sprint in to be
    the second set's four closest, the tightest move of the strat; the main tank backs out to 8.5 y for that set, and
    the off tank leaves for its tornado at 25.4, 1.2 s after the melee, clear of the middle tornado's telegraph.
  - For the last hand the tanks hold the bosses about 22 y apart, and as the palm shows each walks through the
    middle and stops its boss's stopping distance beyond it (6 y, 4.1 y), which leaves each bot tank on the side the
    other tank comes in from. Under a fist the bot tanks stay where they are: only a human tank can bring one about
    (an off tank that waits for the glow in the middle shows one), and where it stands the bosses are still more
    than 12 y apart.
  - No move brings a bot's boss onto a human tank's. A human off tank that stops on the middle has the Hand within
    the palm's 5.1 y, by a yalm; a human main tank that stops there leaves the Liquid 6 y short, and one that stops a
    yalm beside the other tank is short of the middle too: everyone dies, by the sim's 5.1 y. Those leave the
    bosses about 6 y apart, inside the 2.9 to 7.1 y the fight's own line is only known to lie in. A bot tank that
    walked its boss onto the other boss's live spot would cover that, and would also carry an off tank that never
    brings the Hand in, which the rule's test expects to lose: not made, the user's to pick.
- `TeaP1LivingLiquidPlan` is the one piece of strat the scenario reads, for the start's chat lines: a human tank's
  tether, and that two tanks who start with each other's keep them, each on its own spot; a human main tank's standing
  away from the open quarter before Protean 1, so the Liquid's own wave goes that way, its leading the Liquid back onto
  the middle (it stays put within 7 y and walks to 6 y from its tank) and turning it to the open quarter 2 s or more
  before Protean 2's bar, and its last hand (hold the Liquid more than 17 y from the Hand until the palm shows, then
  walk on through the middle until the Liquid stands on the Hand); a human off tank's last hand, the same with the
  Hand, which stops 4 y behind it; a human healer's cleanse order. A healer's seat on a job without Esuna is held to
  no cleanse: the run cleanses for it. The strat presses nothing for a human in its seat that its job can press. In
  headless runs the debug driver walks the player's seat and presses nothing, so the strat presses for it too (its
  cleanses, and in the pull its Provokes, invulnerability and doll), unless a test sets the internal
  `PlayerPressesItsOwnActions`. The human suites leave that off: the seat a human from the first frame is all that
  tells the strat, as in the plugin.
- UNVERIFIED in `TeaP1Constants`: the puddle's 7.6 y and that its object takes no spawn arguments, the waves' and
  cleaves' half-angles, the telegraphed wave's knockback (10 y, its speed, straight away from its source), the puddle's
  first harmless second, the Liquid's 6.0 y stop and both set-off distances, where between 15.15 and 16.13 y the
  Hand's form changes and which form looks which, the palm's 5.1 y (between 2.9 and 7.1) and the fist's 12 y,
  Drainage's 0.2 mitigation, the second after which a wave's, a Sluice's and Drainage's damage shows, an Embolus's
  1 y for the Hand and a player, and where a start puts the bosses. In the State, the actions and the mechanics: the
  odds of every roll and whom the tethers pick, that a holder can't take the second tether, that one Drainage circle
  kills a tank that holds no tether, that a telegraphed wave leaves Water Resistance Down and kills no more than a
  second wave does, that a tank's cooldowns don't take it through the wave straight ahead, that the Warden's Paean's
  barrier stops Throttle, and when the second tornadoes leave. In the plan: that the client's party list shows the
  seats in the order the cleanses go by (MT, OT, H1, H2, M1, M2, R1, R2); it may sort each role by job.
- No source fixes these strat values, so they are UNVERIFIED picks. Protean 1's spots, the six's Drainage stack, the
  last hand's and the kill's are the guide's drawing; these depart from it or fill what it leaves open, each for
  the margin named, and the margin suite's floors hold them:
  - the main tank 3.9 y from the Liquid whenever it is free to walk, where the guide draws it 7.4 y out ((5.0, 5.5)
    for Drainage, (-0.8, -7.4) for Protean 2's stack): the Liquid walks after a tank more than 7.0 y away;
  - everyone inside r 6 for Cascade 2's cast, where the guide keeps the six stacked at r 7 to 9.4: a new puddle
    reaches in to r 7.4 (the main tank up to r 6.7 while it leads the Liquid back);
  - Protean 2's two stacks on the Liquid's north-south line (H1 5.7 and R2 7.5 y north of it; H2 3.2, M1 3.8, R1
    4.4, M2 5.0 and the off tank 5.6 y south), where the guide scatters them: two narrow bundles of telegraphs;
  - M1 aside at bearing 215 for the telegraph and onto its south bait at 21.3: that bait is inside the south
    bundle;
  - the first set's baits: M2 at bearing 85 (the guide's 91), so R1 beside the east tornado is outside its cone, and
    the off tank 4.0 y west (5.1), farther from the west tornado than H1, which that tornado has to aim at;
  - the second set's baits: R2 4.0 y out at 275 (4.4 at 269) and R1 at 95 (92), so the off tank by the west tornado
    and M2 by the east one clear their cones; the main tank out to 8.5 y at 43 (8.1), so the four that run in are
    nearer by 3 y;
  - every tornado spot 8.4 to 8.5 y from its tornado (8.2 to 8.8), outside its water after the jitter, on bearings
    from it that keep the arena points they had with the ring at 14.5 y, so the run in is no longer and M1's cone, a
    yalm aside, still misses H2: H1 at 17.3 and R2, 10 y out, at 17.8 from the west one, H2 at 273.4 from the middle
    one, R1 at 175.4 from the east one, and for the tornadoes' unseen waves M2 at 8.7 from the east one and the off
    tank at 187.7 from the west one. M1 stands at 80 from the middle one, 16 degrees outside the east tornado's
    telegraph at R1 and with M2 still 30 degrees outside the wave it draws. The off tank leaves for its own at 25.4,
    1.2 s after the melee;
  - the tanks' spots for the last hand, (10.2, -11.9) and (-13.0, 10.8), inside the rim and with the bosses 6 y past
    the form's 15.6 y line;
  - every move's time; the fetch from 17.75 every 0.25 s, to 1.5 y short of either end of the tether; the lead back
    at 6.4, its 0.7 y threshold and its 7.4 y; the cleanses' 11.2, 13.7, 16.2 and 18.7; the stacks' follow every
    0.25 s from 12.25 to 17.75 and its 0.25 y threshold.
- Change bot movement only with `TeaP1LivingLiquidLpduMarginTests` and both human suites green.
  - The rule tests play no strat (`TeaP1LivingLiquidStage` teleports every seat cue by cue; its header has the
    timeline). `TeaP1LivingLiquidLpduAiTests` reads the strat's spots against the stage's on every layout, and the
    fetch from all 21 starts.
  - The margin suite holds every pick by distance, every cone, Sluice and puddle, Drainage and the last hand to
    floors derived from the spots (`MarginSeeds` asks for more runs). The thinnest is the tornadoes' aim, the next
    closest 0.75 y and more behind the baiter. Through every start of the tethers both are on their tanks by 22.5
    (about 21 in the slowest, where the main tank takes the middle one off the off tank before the off tank can
    fetch the west one), both tanks are on their spots at Drainage, and the Liquid is at most 3.5 y off the middle
    then (2.7 y in these runs).
  - The human suites (`TeaP1LivingLiquidProtean1LpduHumanTests`, `...Cascade2LpduHumanTests`, on
    `TeaP1LivingLiquidHuman`) seat a human from the first frame who reads its north off the tornadoes and sets off
    from what a player sees (each telegraph's bar, each unseen set landing, the tethers, Cascade landing, the
    Throttle icons, the palm), the strat's beat and 0, 0.25 or 0.5 s later, walking or sprinting, round the water,
    on every layout. A tank walks into its own tether wherever both start and presses Rampart; a healer presses
    Esuna every 2.5 s; the main tank also plays the guides' 7.4 y spots, dragging the Liquid 1.4 y, and turns it up
    to 20 degrees off south for Protean 1 and off north for Protean 2. In both parts a walking human also stands a
    yalm farther from the Liquid, or a yalm aside, on every spot it baits from, short of the water. Nobody may die,
    but in these cases, each its own test:
    - a tank that never takes its tether (the six die); a healer that never cleanses (two die) or starts three GCDs
      late (one);
    - a healer or a ranged that stands in its own telegraphed wave in Protean 1 without its anti-knockback: thrown
      out of the arena, it dies past the rim, alone;
    - the last hand: either tank that never brings its boss in, a main tank that stops on the middle, the Liquid
      6 y short of it, and either tank that stops a yalm beside the other (everyone, to the palm). An off tank that
      stops on the middle loses nobody. One that waits for the glow in the middle is shown a fist, with the bosses
      still more than 12 y apart: staying, or stepping 8 or 10 y away, loses nobody, and walking on toward the Liquid
      loses everyone to the fist;
    - a main tank that never leads the Liquid back onto the middle: nobody, however its tether comes to it (from the
      stack, once the off tank has taken the other one off it, or off the off tank), with the Liquid a yalm and a
      half off, or 2.2 y for one that takes its tether by its tornado. One that leads it back 2 to 7 s late loses
      nobody either; that is played from the stack's starts alone;
    - a main tank that never turns the Liquid and waits for the bar where the start put it. In Protean 1 the Liquid's
      own wave throws it down the Liquid's facing, where the first set's wave straight ahead kills it; the Sluice
      that drops under it and the cone that goes to another in its place cost three more. In Protean 2 the Liquid
      faces any eighth of the second frame, and whoever's spot is in its face as either set goes off dies of that
      set's wave straight ahead, and whoever then draws a wave too many of a second wave: nobody at the open
      quarter; itself and M2 at 45; M2 and R1 at 90; M1 at 135, walking through it to its tornado, and with it the
      four in the middle; M1 and H2 at 180; M1, H2 on its edge and the main tank at 225; the off tank and R2 at 270;
      H1, R2 and both tanks at 315;
    - the run in walked 0.5 s late: R1 from its spot or a yalm farther out and H2 from a yalm farther out are still
      farther from the Liquid than the main tank as the second set goes off (the main tank dies; 0.25 s late they
      are in time); R1 from a yalm aside is in time on some rolls; and R2 from a yalm aside is still on its way in,
      along H1's wave, on a few (both die).
- Change a rule, a roll or a press only with the strat-free tests green: `TeaP1LivingLiquidScenarioTests` (each rule,
  one mistake at a time, and each part on its own), `TeaP1LivingLiquidTankTests` (when a boss stands and when it
  walks, the facing lock, the cleaves, a tank's cooldown under Drainage), `TeaP1LivingLiquidThrottleTests` (a human's
  cleanses), `TeaP1LivingLiquidStateTests` (the rolls and the settings), `TeaP1LivingLiquidActionsTests` (each attack
  alone), `TeaP1PressesTests`, `TeaP1LivingLiquidSecondRunTests` (one scenario instance plays every run of a session,
  and `Run` replaces the listener and the piece) and `TeaP1LivingLiquidHelperBudgetTests`;
  `TeaP1LivingLiquidPlanTests` for the plan and its chat lines.
- The gate's buckets are `tea-p1-pull-human`, `tea-p1-protean-human`, `tea-p1-cascade-human` and `tea-p1-liquid`
  (the rest of the scenario's tests). The soak filter is
  `FullyQualifiedName~ScenarioCatalogTests&Name~Alexander&Name~Living&Name~Liquid`; it plays the whole fight from
  the pull, and `TeaP1LivingLiquidHandoverTests` plays each later start. A hand-run filter that selects more than
  2000 tests makes the NUnit adapter run the whole assembly: add `-- NUnit.AssemblySelectLimit=1000000` to a filter
  as wide as `FullyQualifiedName~Tea`, as `tools\test.ps1` does for every shard.

## TEA P1 Living Liquid: the pull
Part 0 of "Living Liquid", on the fight's own clock from the first hit on the Living Liquid: `ThePullIntoTheCentre`
in the scenario, whose lines take no `start` since nothing comes before it, `TeaP1JagdDolls.cs`, and the strat's
`TeaP1LivingLiquidLpduAi.Pull.cs`. At 60.9 the Protean 1 part begins and the run plays on to the kill.
- The timeline: the Liquid waits on (0, -15) facing south and takes the main tank's seat at 0.5; Fluid Swing 10.1;
  Cascade 14.2 (lands 18.2); at 19.6 the Liquid Hand splits off on the middle, on the off tank's seat, with the first
  tornadoes, whose water kills from 20.6; the Hand glows 29.3; the tornadoes' telegraphed waves are aimed 32.4
  (decide 35.1); four Jagd Dolls appear 32.6 and turn targetable 33.4; the Hand slams 34.4; Fluid Swing 36.4 and
  Fluid Strike 36.5; the tornadoes' unseen waves 37.5; Exhaust 38.5 and every 10.64 s; Hand of Pain 38.6;
  Pressurize 40.4, its Emboli setting off at 45.95; Fluid Swing 55.55 and Fluid Strike 55.8. Not played: both
  bosses' and the dolls' auto-attacks (the animation of one every 3 s would stall a boss's walk), the Liquid's
  health halving at the split, Hand of Pain's check, a doll killed.
- Rolls and settings: the pull plays the first tornadoes' layout, which Protean 1 goes on with (the Tornadoes row
  under "The pull" in the settings), and rolls its own Emboli from the fork (above): the tornado without one and
  which orb heads for which boss, six rolls. No setting forces them; tests pin them with the internal `FirstEmboli`.
  The dolls' spots are no roll.
- The Hand and the dolls are spawned from an event, and the client has a spawned actor only some frames later and
  drops what is written to it before: a boss turned to its target that early is shown it again once it is there, a
  boss still on its way reads no hitbox and does not walk, and a doll turns targetable on the first frame it is
  there (`FakeBattleCharas.PacketSpawnFrames` delays an actor in tests).
- Provoke: `TeaP1Presses` hears the human's (7533, with "Resolve your own actions" on or off) and the piece gives
  the boss it was aimed at to whoever pressed it 0.67 s after the press (`P1Duration.ProvokeTakesHold`; measured for
  the Hand, UNVERIFIED for the Liquid), counted from when it was heard. The target changes then; the turn and the
  walk wait out a cast. The main tank Provokes the Hand once it shows its palm and the off tank Provokes it back
  after the first busters. Pressed before about 28.2 the Hand has walked within 15.6 y by the glow and shows a fist,
  which then finds them together; pressed after about 31.9, or never, the palm's slam finds the Hand still on its
  way: everyone dies either way. An off tank that never Provokes back loses nobody in the pull; the Hand then stays
  with the main tank and the last hand shows a fist.
  - The run's own Provoke, for a seat nobody presses for, is held to Provoke's reach as the client holds a human's:
    the sheet's 25 y past the two hitboxes (`P1Rules.ProvokeReach`; `TeaP1Presses.ProvokeReaches`), 27.1 y between
    a tank's centre and the Hand's. Out of reach it is none. From the main tank's bot at the rim that is a Hand up to
    about 8.5 y from the middle on the off tank's bearing, its tank, 4.1 y farther out, up to about 12.6 y.
  - A human's press heard in the gap before a frame settles ahead of the run's own hit or Provoke of that frame:
    `TeaP1Presses.Strike` and `Provoke` catch up first, so a doll goes to whoever pressed first.
- The cleaves ask for no cooldown. A tank at full health lived through a bare Fluid Swing in the fight (it took three
  fifths of its health), so each is a plain tank buster: a non-tank seat in one dies, a tank seat lives, whatever its
  job. UNVERIFIED, both halves: no non-tank was hit by either cleave, and no Fluid Strike was taken with nothing up.
  No rule kills for both cleaves at once either. Each leaves Water Resistance Down, and what kills is the wave that
  follows: the main tank holds both bosses and is the middle tornado's closest player, so that tornado's unseen wave
  at 37.5 finds it carrying the cleaves' debuff. That is what its invulnerability is for; pressed from 27.5 to 37.5
  it takes it through, and a main tank that leaves the tornado after the cleaves needs none.
- The invulnerability is read as its four statuses (82, 409, 810, 1836), as every TEA rule reads it. With "Resolve
  your own actions" off, `TeaP1Presses` adds the status itself as it hears the press (30, 43, 3638, 16152), for
  10 s, in the call the sim's own resolving would add it from, so a press in the gap before a frame counts in that
  frame with the setting on or off. That holds in the two later parts too: a human tank's invulnerability now counts
  under their waves, Sluices and Drainage with that setting off. A listener outlives its run (a scenario has no hook
  for its run's end, and `Acted` is raised for every scenario): one that hears a press after the world's `Rng` has
  been replaced, as each run does, does nothing and lets go.
- A tornado's telegraphed wave throws a human it hits 10 y, past the rim from a bait spot. The bots are not thrown:
  the main tank bot stays in its telegraph under its invulnerability and the ranged step out and back. A human main
  tank steps out and back, or presses Arm's Length.
- The Jagd Dolls (`TeaP1JagdDolls`): four, 7 y out on true bearings 67.5, 157.5, 247.5 and 337.5 whatever the
  layout, so each quarter of the tornadoes' frame holds one. They are the phase's one demihuman, which draws nothing
  without its spawn packet's parts: `P1Appearance.JagdDoll` is its NpcEquip row's, decoded as upstream's FRU
  appearances decode from theirs (UNVERIFIED until one is seen drawn).
  - A doll tethers the first player whose action aimed at it is heard, 0.6 s after the press, and walks after that
    player at 9 y/s to 3.3 y from it (setting off again past 4.3 y, UNVERIFIED). A holder that dies leaves it where
    it stands. What is a hit is read off the Action sheet: an action aimed at one target that is not its user and
    not the ground, pressed at something that is no party member, whatever its kind: a weaponskill, a spell or an
    ability, a tank's Provoke and a single-target oGCD among them, from any seat. Don't narrow the filter to
    weaponskills and spells. A press comes with the client's target whatever the button, so a cooldown, a heal, an
    invulnerability or a ground-targeted button pressed with a doll targeted is none. Thunderclap and its like,
    which can land on a party member or an enemy, count, on the taking that the client refuses a party-only button
    aimed at an enemy before it is heard (UNVERIFIED). A button that turns into another action comes with its own
    id, whose row is aimed at its user, so ten of them are let through by id
    (`P1PlayerActionId.BecomeAHitOnTheTarget`: Ninjutsu, Iaijutsu, Tsubame-gaeshi, Masterful Blitz, Continuation,
    Serpent's Tail, Twinfang, Twinblood, Astral Flow, Living Muse): a ninja's Raiton or a samurai's Higanbana
    tethers a doll, and so does one that became something laid round its user (Doton, Elixir Field) pressed with a
    doll targeted. Since the merge brought upstream's `283e864` those ids no longer come: Raiton (2267) and
    Higanbana (7489) arrive as themselves and pass on their own rows, but a Doton or an Elixir Field pressed with a
    doll targeted no longer counts; the ids are left for P2 to drop. The fight's doll also
    tethers on an auto-attack now and then, and so does the run's: with "Resolve your own actions" on, upstream's
    `AutoAttackHandler` reports each of the player's swings through `IUserActions.Acted` (Attack 7, Shot 8, whose rows
    pass the filter), so a swing that lands on a doll first tethers it. With the module off no swing is heard, and a
    doll waits for the first action.
  - No damage is played, so a doll's health is scripted: whole until its tether shows, a quarter 4 s later
    (`DollUnderAQuarterAfter`, UNVERIFIED: the party's doing), then down to a tenth, where it stays; no doll dies.
    The bar is sent in twentieths through the status list (UNVERIFIED that the client's follows), and a human's own
    doll says in chat, once, when it is under a quarter.
  - Exhaust from every doll that is up, each reaching the sheet's 8 y past its hitbox (8.8 y). One harms nobody and
    leaves Luminous Aetheroplasm for 4.96 s; a player two reach at once explodes and kills everyone within 8.5 y,
    itself included, short of a tank's invulnerability, as every other kill of the phase that falls on one player
    is: an invulnerable tank that two reach still explodes, and lives, and so does one within 8.5 y of another's
    explosion (UNVERIFIED: nobody was seen to explode under one; the explosion killed everyone it was seen to hit).
    The Exhausts chain on the run's clock and stop with the scenario's last line, so a doll nobody feeds neither
    keeps a finished run going nor Exhausts past the kill.
  - A doll a player has hit is fed to a boss once their centres are within `P1Geometry.DollIsFedWithin`, 4.6 y
    (UNVERIFIED where the line lies: dolls walking in were fed from 3.26 to 4.57 y, most near the far end, so it is
    at least the farthest; 4.14 y, the mean, was taken for it at first). Fed, it turns untargetable and casts
    Reducible Complexity 1.07 s later: harmless under a quarter of its health, and killing everyone from a quarter
    up (24.5 % was harmless and 25.3 % killed six of eight). A doll nobody has hit is never fed, however close a
    boss comes (UNVERIFIED past a doll's first 0.75 s), and one hit beside a boss is fed there and then, whole.
    That last is UNVERIFIED, and the one row behind it speaks against it since the line moved to 4.6 y: the one doll
    seen fed before its tether showed was hit, then had the Living Liquid 3.9 y from it for 1.9 s before its feed
    went out. A rule that waits (a hit doll fed only once a boss has stood inside the line for a while, or only from
    its tether's time) would spare a human who lands a first hit with a boss passing within 4.6 y; it is not made,
    the user's to pick, and the bots keep their own 5.2 y either way.
- Each death's reason states its rule, as in the later parts; the pull's own are the explosion and the doll fed too
  soon.
- `TeaP1LivingLiquidPlan`'s chat lines for the pull. Where a tank takes its boss is its seat's to do whatever its
  job, since each boss walks after its seat and no move reaches a human; its presses are its job's.
  - Main tank: the pull (turn the Liquid away, within 6 y of the middle as Cascade lands); the first hand (the rim
    beside the middle tornado, be that tornado's closest, the telegraph, and on a tank's job the Provoke as the palm
    shows and the invulnerability between 28 and 37 s); only from 42 s, once the melee have fed, through the middle
    to 6 y past it, then within 4 y of the Liquid, since an Embolus from the tornado right of the open quarter
    passes 7 y out on that side. It is to set off between 42 and 44 s. Led in sooner, the way in is inside two
    dolls' reach until the first Exhaust, a doll the Liquid passes is fed, and the Embolus of 40.4 heads for wherever
    the Liquid stands then. Later, an Embolus from the middle tornado, which two rolls in three send, finds the
    Liquid still at the rim at 47 s: the tank's way in runs through the Liquid, which follows only once the tank is
    7 y past it. On another job it is told the run's presses instead: Provoke at 30.0 if a palm shows, or as soon
    after as the Hand is in reach, invulnerability at 34.0.
  - Off tank: take the Hand out to the right of the open quarter until the palm, standing 9 to 12 y from the middle
    itself, the Hand 4 y short of it: the main tank Provokes the Hand from the rim with Provoke's 25 y, and with the
    off tank farther out the Hand is out of its reach. Then wait beside the Liquid, not near the middle, which is
    inside two dolls' reach until the first Exhaust; Provoke back between the busters (the run does at 38.0 for
    another job) and set off for the middle between 42 and 47 s, once the melee have fed and before an Embolus from
    the middle tornado finds the Hand at the rim at 48 s.
  - Both tanks and both healers: leave the dolls' first hits to the damage dealers, a tank's Provoke included. Any
    seat's hit tethers a doll, and nothing in a tank's or a healer's part feeds one: on its way to the main tank at
    the rim it is fed to the Liquid whole, on its way to the off tank on the flank it is fed whole or stops beside
    it for a second Exhaust, and beside H2 far south-east it is a second Exhaust.
  - A damage dealer: its doll's quarter, hit it first (an auto-attack counts while "Resolve your own actions" is on,
    so keep the other dolls untargeted), stand where only its Exhaust reaches, keep it more than 5 y from both bosses until it is under a quarter, then draw it through a boss from
    the far side, clear of the water. M1 is also told to hit by 38 s: on every layout the tanks lead the bosses to
    the middle past the south-west doll's spot from 42.5, which feeds a hit doll there, and its health falls by
    the script, so a first hit from about 39.0 wipes the run at 44.7. The bosses pass no other doll's spot. A ranged
    is also told its tornado and the side to bait it from: the open quarter's side, at the rim, so both waves run
    out of the arena; baited from the inner side they cross it, and whoever both hit dies.
  - A healer: wait far out until the first Exhaust, then come in on the open quarter's side of the middle: the
    bosses stand on it from 46 s and their busters at 55 s cleave toward the tanks, on the middle tornado's side.
- The strat (`TeaP1LivingLiquidLpduAi.Pull.cs`), with no `arrivalTime` anywhere. What it does on sight is looked at
  every 0.25 s and sent once per decision: a walk sent again while its bot rounds a puddle would pick its side
  afresh.
  - 0.0 the party in a knot behind the main tank, 2 y south of the middle; 2.0 the main tank through the Liquid to
    turn it north; 14.3 it steps in to 6 y for Cascade; 24.0 the split: the main tank sprints to the rim beside the
    middle tornado (8.4 y from it at bearing 258), the off tank goes 11.5 y out at 39, the Hand to 7.4 y out behind
    it, the ranged bait the side tornadoes 8.4 y from them, 10 degrees outward of north, H1 far north, H2 far
    south-east, the melee south-west of the middle.
  - 30.0 the main tank Provokes the Hand if it shows a palm, and the off tank walks to the Liquid's flank once the
    Hand has left it; 32.5 the ranged step out of their telegraphs and 35.15 back; 33.5 each melee half a yalm off
    its doll, away from the tornado beside it; 34.0 the main tank's invulnerability; from 34.25 each damage dealer
    hits its doll once it is in reach (3.5 y, 25 y) with both bosses 5.2 y or more from it; 38.0 the off tank
    Provokes back.
  - Each Provoke waits for the Hand to stand within its reach, looked at every 0.25 s: the main tank's to 34.25,
    the off tank's to 55.0. With bots alone both go out on the first look (the Hand 25.9 y from the main tank). A
    human off tank that stands more than about 12.6 y from the middle on the bots' bearing, as the guides' 14.1 y
    is, has the Hand more than 8.5 y out and leaves the main tank's bot no Provoke: the palm finds the bosses apart.
    One that then draws the Hand in gets the Provoke on the next look, in time for the slam if that is by about 31.8.
  - From 38.5 to 45.0 a melee leaves a doll of a melee's quarter alone, whatever stands by it: hit then, it would
    still be whole as the tanks lead the bosses in past it from 42.5. It arises only when a boss kept the melee
    from its doll until then (a human main tank's Liquid at rest within 5.2 y of it, led away early).
  - From 36.0 a bot whose doll someone else hit takes that one's own, a melee walking to it first. When the taker
    is not a melee the doll goes out to it, into reach of whoever stands by the taker's own, so the melee waits out
    the first Exhaust and goes from 38.75. A melee takes no doll from 47.0 and goes north without one: hit later it
    would not be fed before the second busters, which cleave over the melee dolls' spots.
  - From 38.75 each melee whose doll is under a quarter draws it through the nearer boss at rest, 2.5 y past it,
    turned round it by up to 100 degrees to a spot 8.3 y from every tornado, then goes north; the ranged do so
    from 49.3, after the second Exhaust, and go back out. 42.5 each tank across the middle, its boss's stopping
    distance past it, and the healers and ranged to their Protean 1 spots; from 46.0, by 49.0, each tank takes its
    hold once it and its boss have come to rest.
  - These depart from the guides' drawing or fill what it leaves open, all UNVERIFIED picks the margin suite's
    floors hold: the off tank's split at r 11.5 (drawn at 14.1, out of Provoke's reach); the main tank in to r 6 for
    Cascade (drawn at r 8, inside a puddle on three layouts) and staying in its telegraph; the ranged 10 degrees
    outward; the melee dolls left where they appear with the melee half a yalm off them (drawn dragged east), so
    the healers stay out until 42.5 where the guides draw them in the middle; the feeds at the rim and on the
    middle; the 5.2 y rule, 0.6 y past the feed line; every time and the looks' 0.25 s.
- Change the pull's bot movement only with `TeaP1LivingLiquidPullLpduHumanTests`, the pull's margin tests
  (`TeaP1LivingLiquidLpduMarginTests.Pull.cs`), `TeaP1LivingLiquidHandoverTests` and `TeaP1LivingLiquidLpduAiTests`'
  pull half green.
  - The margin suite holds, on every layout: the Liquid at rest 6 y behind the main tank and 5.3 y or more from the
    nearest doll's spot (5.50 in the build, where no spot fixes it: it trails a tank that sprints); the bosses apart
    at the glow and together at the slam; each tornado aiming at its baiter both times; nobody but the main tank in
    a telegraph; every cleave on its tank alone; the second-nearest doll 0.3 y past its reach at the first Exhaust;
    a hit doll 0.55 y past the feed line from both bosses while it holds a quarter; the melee's dolls fed by 42.4
    and the ranged's by 52.0; the Emboli clear of every seat and boss; every seat on its Protean 1 spot at 60.9.
  - The handover tests play a run from the pull beside one from Protean 1 on the same seed: the same seats, bosses,
    targets and casts from 0.6 s into Protean 1 to the fall. At most one of the pull's Emboli is still crossing
    then, 16 y out, gone 3 s in. `ALaterStartPlaysASeedAsItDidBeforeThePullWasPlayed` holds a later start to the
    build before the pull, on five seeds each: the run's own stream by its next draw, early in the part and at the
    fall (nothing in a later start draws from it), and for the bots' moves the jitter stream's next draw at the fall
    with where four seats stand then, since each jittered move draws from that stream and from no other. Its rows
    come from a build of that commit: a change meant to alter a later start has to retake them.
  - The human suite seats a human from the first frame, which is all that tells the strat to press nothing for it
    (no internal override), on every layout, walking or sprinting, 0 to 0.5 s late, pressing its own Provokes,
    invulnerability and doll hit, and feeding once the chat says its doll is ready. No scripted tank presses a
    cooldown for a cleave. Nobody may die, every doll goes to its damage dealer and is fed, and each boss ends on
    its tank. The seeds alone would give two of the six Emboli rolls, so the sweep and the whole fight pin them:
    every seat meets all six. Nobody may die but in these cases, each its own test:
    - the main tank's Provoke at 27.8 (the fist), at 32.4 or never (the palm): everyone; its invulnerability at 27,
      at 37.6 or never: it alone, of the unseen wave; staying in the telegraph without Arm's Length: it alone, past
      the rim; 3 y along the rim, no longer the tornado's closest: R2 or M1 on most rolls, the count held; 6.5 y
      from the Liquid on the middle, on the line of the orb that heads for it: everyone; never turning the Liquid at
      the pull: the six behind it, of the first Fluid Swing; leading the Liquid in at 36.6: on the cardinal layouts
      it explodes and lives through it under its invulnerability, both melee dying of it and at a walk the off tank
      too, and on the diagonal ones it feeds M1's doll too soon; leading it in at 37.6: the melee feed in time, but
      the Embolus of 40.4 heads for where the Liquid stands then and bursts on the bosses, at a sprint on every
      layout at 51.6, at a walk on two of eight at about 59, the count held; leading it in 4 s late: everyone at
      about 47.5, on the four rolls of six with an orb from the middle tornado (2 s late, nobody on any roll);
    - an off tank that waits on the middle once the Hand has left: it explodes at the first Exhaust with both
      melee; one that leads the Hand in at 37.5: it explodes on the cardinal layouts and feeds M1's doll too soon
      on the diagonal ones (a quarter second later Hand of Pain holds the Hand back and nobody dies); one that
      keeps the Hand near the Liquid: a fist, and everyone at the slam or of the Hand's Embolus; one that stands
      13.5, 14.1 or 16.1 y out, the Hand past the main tank's Provoke: everyone at the slam (9 or 12 y out loses
      nobody), and one that draws the Hand back in from 14.1 y only at 30.3: the Provoke comes at 32.3, too late;
      one that leads the Hand in 6.5 s late: everyone at about 48.6, on the rolls with an orb from the middle
      tornado (5.5 s late, nobody); one that never Provokes back: everyone at the last hand;
    - a tank or a healer that hits a doll 0.2 s after they turn targetable, ahead of the bots: H2 with M1's, R1's
      or R2's explodes at the first Exhaust and M2 with it; the main tank with M1's, M2's or R2's: everyone, of the
      doll fed to the Liquid on its way; the off tank with M2's: everyone the same way, and with R2's it explodes
      with M1; H1 with any, and H2 with M2's, lose nobody, the bot it was taken from taking no other;
    - a melee 2 y toward the other melee's doll: it explodes; M1 hitting 5 s late: everyone at 44.7 (M2 loses
      nobody); a melee feeding 2 s after its tether: everyone, or M2 exploding on its way; a melee that hits both
      melee dolls: it explodes, and the other melee's bot, left without one, goes north; a damage dealer that never
      hits its doll loses nobody;
    - a ranged staying in its telegraph: it alone, past the rim; a ranged baiting from its tornado's inner side
      (bearing 310 from the east one, mirrored for the west): H1 alone, of both waves; a healer in the middle from
      the dolls' appearing: it and both melee, but H1 on a cardinal layout; a healer that comes in 3 y on the tanks'
      side of the middle: it alone, of the second busters.
    - Losing nobody, each its own test: a tank on another job (the run presses), cooldowns pressed with a doll
      targeted, the sim not resolving the tank's actions, the main tank a yalm along the rim, baiting from where
      some guides draw it (the Liquid then rests 5.24 y from the south-west doll's spot at the nearest, just past
      the bots' 5.2 y), not quite back from its step out, or leading the Liquid in up to 2 s late; the off tank
      leading the Hand in up to 5.5 s late, or drawing a Hand it took too far out back into the main tank's Provoke
      by 30.0 (the bot Provokes on a later look), or on another job waiting out of its own Provoke's reach until it
      comes in (the run Provokes back on a later look); a damage dealer taking the other's doll, a ranged taking a
      melee's, hits 2 and 3 s late, a caster's long cast, a melee on its doll or half a yalm toward its tornado,
      feeds once the bosses rest on the middle; and the whole fight from the pull.
- Change a rule or a press of the pull only with the strat-free tests green: `TeaP1LivingLiquidPullTests` (each
  rule, one mistake at a time, on the stage's pull cues), `TeaP1JagdDollsTests` (the piece alone),
  `TeaP1PressesTests`, `TeaP1LivingLiquidActionsTests`, `TeaP1LivingLiquidStateTests` (the fork, and that every
  later roll of a seed is unchanged), `TeaP1LivingLiquidSecondRunTests` and `TeaP1LivingLiquidHelperBudgetTests`.
- UNVERIFIED for the pull, in `TeaP1Constants` unless said: the doll's appearance; where the feed line lies; a
  doll's set-off distance, its scripted health (4 s to a quarter, the floor) and that the bar follows; that a Provoke
  takes hold on the Liquid as on the Hand; the cleaves' half-angle. In `TeaP1Actions`: that a cleave kills a non-tank
  seat and that a bare Fluid Strike leaves a tank alive. In the pieces: that the Hand starts on the off
  tank's seat (the fight's goes to whoever holds its enmity); that a doll nobody has hit is never fed and one can be
  fed from its first hit; that a doll fed at a quarter or more kills everyone, where the fight's kills most; the
  explosion's 8.5 y and that a tank's invulnerability takes it through one; that a hit doll is fed the moment a boss
  stands inside the line; that every action aimed at a doll alone tethers it and one that only clips it does not;
  that the client refuses a party-only button aimed at an enemy; the client's current-target stand-in. In
  `TeaP1Actions`, for all three parts: that a telegraphed wave leaves Water Resistance Down. In the pull a ranged,
  or any human, that holds its tornado's telegraph with its anti-knockback and is still that tornado's closest for
  the unseen wave 2.4 s later dies of a second wave by that taking alone; the main tank carries the cleaves' debuff
  either way.
- In-game checks still owed, in this order: the four dolls are drawn; the Hand shows the off tank as its target a
  moment after it appears; a weaponskill and a hard cast on a doll are heard and tether it, and a cooldown pressed
  with one targeted does not; the doll's bar falling and the chat line; Provoke moving the Hand two thirds of a
  second after the press, and the invulnerability with the module on and off.

## TEA P3 Wormhole
Upstream's `Scenarios/Tea/P3Wormhole/`, "Wormhole", in upstream's `TeaZone.P3` phase (Beyond Time, BGM 725 from
115.2 s): from 1 s before Alexander Prime's Wormhole Formation through the Enumerations, with upstream's Limit Cut
numbers, Cruise Chaser's warps and knockbacks an Arm's Length or Surecast status stops (`ai.Kbi` for bots).
Multiplayer, no solo. NoirMech ships it as upstream has it but for the strats; NoirMech's later P3 parts are to go on
the same phase.
- Two strats. `TeaP3WormholeLpduAi`, EU `[LPDU] Light PF`, is first, so it is the default; upstream's NAUR
  (`TeaP3WormholeAi`) is index 1 with `Group => "NA"`, and upstream's `TeaP3WormholeScenarioTests` pass
  `NaStrat = 1`. The index goes to multiplayer peers, so both sides need the same build.
- The LPDU strat is NAUR's moves and spots, which match LPDU's Onyxia everywhere but the Enumerations, with the
  Enumerations taken by LPDU's priority: marked M1 or R1 east beside the tanks, marked M2 or R2 west beside the
  healers, and of two marked on one side the melee goes to the other; the unmarked DPS wait north as NAUR has them.
  At 46.0 the east ring is (4.4, -0.3) with the tanks at (5.2, 0.5) and (5.2, -1.1), the west (-5.2, -0.3) with the
  healers at (-6.0, 0.5) and (-6.0, -1.1), the unmarked at (0, -6) and (-0.8, -7.4). NAUR sends a lone marked melee
  east, so the two part only when M1 and R1, or M2 and R1, are marked: there a human playing LPDU's priority among
  NAUR's bots shares a ring with the other marked, and the Enumerations kill.
- Upstream's I1 Limit Cut (phase I1, strat "1256") stays off the menu: NoirMech's P1 Limit Cut plays the same
  intermission on its own clock, and listing both would only confuse. Its files and tests stay.
- Change bot movement only with `TeaP3WormholeLpduAiTests` (the order, the priority on every marked pair and layout,
  every seat frame by frame against NAUR's, bots alone on every pair, layout and seat) and
  `TeaP3WormholeLpduHumanTests` (a human taking its spot by LPDU's priority on every pair, seat, layout and reaction
  time loses nobody) green. The gate has no bucket of its own for either; they run in the rest.
