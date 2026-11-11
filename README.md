# DalamudPlugins

Custom Dalamud plugin repository for two plugins:

- **NoirMech**, a modified version of
  [AnoMech](https://github.com/anomek/AnoMech) by Anomek that adds EU **LPDU** strats for TOP P2, P3, P4, P5
  Delta, P5 Omega and P6, and practice tools: a pause, Retry and Drill.
  Like AnoMech, it simulates FFXIV ultimate-raid mechanics client-side so you can practice them in an inn.
- **[MitCue](#mitcue)**, reminder-only mitigation cues: it tells you which of your actions to press next, synced
  to the boss from your raid's mitigation plan.

Both install from the same repository URL.

## Install

1. In game, type `/xlsettings`, open the **Experimental** tab, and under **Custom Plugin Repositories** paste:
   ```
   https://raw.githubusercontent.com/NoirYamato/DalamudPlugins/main/repo.json
   ```
   Click **+**, make sure it's ticked, then **Save and Close**.
2. Type `/xlplugins`, search for **NoirMech** and click **Install**.
3. If you also have **AnoMech** installed, disable it. The two hook the same parts of the game and must
   never run at the same time, so NoirMech won't start a run while AnoMech is loaded.
4. Go to any inn and type `/noirmech` (or `/noir`).

Updates arrive through the plugin installer. If your Dalamud auto-update is set to update only main-repo
plugins, click **Update** in `/xlplugins` yourself, or set auto-update to all plugins.

Keep the repository URL exactly as above and leave it enabled. If it's removed, disabled or edited, Dalamud
stops loading NoirMech until you reinstall it. To change it, remove the entry and add it again.

## What's different from AnoMech

- **TOP P2 Party Synergy:** an `[LPDU] BPOG/GOPB` strat under the **EU** button, following
  [Dylan Luxor's LPDU toolbox](https://ff14.toolboxgaming.space/?id=502396712985961&preview=1). It is the
  default; NA pf is still under the **NA** button.
- **TOP P2 Party Synergy, solo:** you no longer die to the Spotlight stack when you're picked as a stack
  target with no bots around.
- **TOP P3 Hello World and P3 Monitors:** new scenarios, each with an `[LPDU] Light PF` strat under the
  **EU** button, following the LPDU toolboxes for
  [Hello World](https://ff14.toolboxgaming.space/?id=702399575985961&preview=1) and
  [Monitors](https://ff14.toolboxgaming.space/?id=736282987145761&preview=1). P3 Monitors' settings have an
  **Automarkers** option: On (the default) marks the three monitor players Bind 1-3 and everyone else Attack
  1-5, in line-up order. Hello World's towers, tethers and timers follow real pulls: a set's towers land on the
  cardinals or the intercardinals (its **Towers** setting can force either), and remote tethers break past 9 y. In
  Monitors, Omega's own monitor hits everyone on his side.
- **TOP P3 Intermission:** a new scenario with an `[LPDU] Light PF` strat under the **EU** button, following
  the [LPDU P3 Intermission raidplan](https://raidplan.io/plan/46r495ZATq3kCqz1). Its settings have an
  **Automarkers** option: On (the default) marks the spreads, stacks and their partners in conga order; Off
  leaves you to read your own debuff.
- **TOP P4 Blue Screen:** a new scenario with an `[LPDU] Light PF` strat under the **EU** button, following
  the [LPDU Blue Screen toolbox](https://ff14.toolboxgaming.space/?id=595188367474761&preview=1). Its settings
  can force or rule out your marker in each set, and whether both of a set's markers land in one light party,
  so someone has to flex. As in the game, each beam follows its player as it fires, while the repeat lines keep
  the aim.
- **TOP P5 Delta:** an `[LPDU] Light PF` strat under the **EU** button, following the
  [LPDU Delta toolbox](https://ff14.toolboxgaming.space/?id=955389449416761&preview=1). It is the default; NA pf
  is still under the **NA** button. In each colour, when only one tether pair has a healer, that pair takes the
  inner spots. Its settings have an **Automarkers** option for both strats: On (the default) puts a triangle on
  the Near World player and a cross (+) on the Distant World player. The eye spawns on any cardinal, not just
  north or south, and in multiplayer everyone's tether, monitor and Hello World choices are honoured more
  reliably.
- **TOP P5 Omega:** an `[LPDU] Light PF` strat under the **EU** button, with the Hello World layout of
  [Tessan's visual guide](https://youtu.be/qNdjZOgCU1I?t=2310). It is the default; NA pf is still under the
  **NA** button. Distant World stands south in the first Hello World, and the second Hello World's layout is
  the NA one mirrored across the beetle's line. The dodges and timings are the NA strat's. Its bot tether
  takers also fetch a Blaster tether left on a player who isn't a taker, you included.
- **TOP P6 Gauntlet:** all of P6 as one scenario, from Cosmo Memory through both Magic Numbers to the enrage, with
  an `[LPDU] Light PF` strat under the **EU** button following the
  [LPDU P6 raidplan](https://raidplan.io/plan/mh3bX4KJ9d8rT9nk). You press your own limit breaks as LPDU assigns
  them (tanks and melee by job priority, healers and ranged by seat) and the bots press theirs. A missing or wasted
  one plays out as in the fight, and a late or missed melee one kills you as its window closes; a chat line at the
  start names yours. Its settings can start from any part, force whether each Cosmo Arrow starts in or out, where
  each Unlimited Wave Cannon's first exaflare starts and which way they turn, and whether you get a flare, and
  change your own tank, melee or healer limit breaks. R2's caster limit break must be centred on Alpha Omega to
  catch every comet, and the enrage's melee limit break counts if it lands before Run: ****mi* hits. Limit breaks
  always count; **Require tank mitigation** decides whether a tank must also have its own cooldowns up for Cosmo
  Dive's busters and Wild Charge. It needs **Resolve your own actions** on. It plays with bots only and replaces
  all five P6 entries, AnoMech's own Exasquares/WC2 included, so P6 has no NA pf strat, solo or multiplayer for
  now.
- **Practice:** **Retry this roll** (the button, or `/noirmech retry`) plays the roll you just played again, and
  **Drill** replays a roll you failed, after a 3 s look at where everyone stood, until you clear it. Auto-restart
  rolls fresh every run. Your role, region, strat and every scenario's settings are kept across game restarts,
  and the **Rotate** role alternates MT/OT or M1/M2 each run.
- **Pause:** `/noirmech pause`, `resume` or `toggle` (or the Pause button) freezes a solo run mid-mechanic:
  cast bars, omens, debuff timers, bots and boss animations all hold. You can still walk but not use actions.
- Renamed to NoirMech (commands `/noirmech` and `/noir`), so it installs alongside AnoMech instead of
  replacing it. Settings start fresh.

Everything else is the official AnoMech v0.5.1.0 release. Its warnings apply: while simulating you are cut
off from server traffic, so party changes and ready checks won't show until you leave the sim zone.

## Source and licence

NoirMech is AnoMech by Anomek, modified by Noir Yamato from 14 October 2026, and is licensed under the
GNU Affero General Public License v3.0 or later, as AnoMech is. See [LICENSE.md](LICENSE.md).
Every build in [`plugins/NoirMech`](plugins/NoirMech) ships with a `-source.zip` holding the exact source
it was built from.

## MitCue

MitCue shows reminder-only mitigation cues: during the fight, a small window tells you which of **your** actions
to press next, synced to the boss from your raid's mitigation plan. It never presses, queues or targets anything,
and only reads what your game already shows you. It comes with two plans for The Omega Protocol (Ultimate):
NoirMit - WHM for the White Mage (made from Carry the Clear's White Mage tab) and LPDU (every job). It also follows
The Epic of Alexander (Ultimate) and Dancing Mad (Ultimate) with a plan you import.

### Install

1. Add the repository URL above, as in step 1 of [Install](#install), if you haven't already.
2. Type `/xlplugins`, search for **MitCue** and click **Install**.

### Quick start

1. Type `/mitcue` to open its settings, and in **General** set **My usual party slot** (H1, H2, ...).
2. In the **Plans** tab, check the **Active plan** and **My column** in the row for your fight and job. In TOP a
   White Mage gets NoirMit - WHM and a Scholar gets LPDU without choosing anything. To use your raid's own plan,
   click **Import from sheet...** to bring it in from its Google Sheets link, or paste a share code you were sent.
3. Type `/mitcue preview top` to watch a simulated TOP pull, and drag the overlay where you want it.
   `/mitcue preview` stops the preview.

### Source and licence

MitCue's source is the [`mitcue`](https://github.com/NoirYamato/DalamudPlugins/tree/mitcue) branch of this
repository. MitCue is Noir Yamato's own code, not a modified AnoMech, so the AGPL in [LICENSE.md](LICENSE.md),
which is NoirMech's licence, doesn't cover it. MitCue bundles fight timelines from
[cactbot](https://github.com/OverlayPlugin/cactbot), which are licensed under the Apache License 2.0 (see
`data/timelines/NOTICE` on the `mitcue` branch), and its zip carries the MIT-licensed libraries it uses, such as
YamlDotNet. Every build in [`plugins/MitCue`](plugins/MitCue) ships with a `-source.zip` holding the exact
source it was built from.

## Publishing (maintainer)

From this folder, with the AnoMech checkout next to it on the `noirmech-stable` branch (the official
AnoMech release plus NoirMech's changes):

```powershell
powershell -ExecutionPolicy Bypass -File .\publish.ps1 -Changelog "What changed" -DryRun
powershell -ExecutionPolicy Bypass -File .\publish.ps1 -Changelog "What changed"
```

It publishes only with a changelog, when this folder is on `main` with nothing uncommitted or untracked, when the
checkout's commit has passed a full `tools\test.ps1` run (its `TestResults\green-stamp.txt` names it) and is in
the pushed `noirmech-stable` (`-Fetch` fetches it first), and when an identity scan of the built plugin zip and
the source zip finds every one of its control strings and none of the terms listed in
`%APPDATA%\NoirMech\identity-terms.txt` (one per line, in any case; a missing or empty list refuses too). Its
commit takes only `plugins/NoirMech` and `repo.json`. `-DryRun` runs every gate, the build and the scan, then
stops before copying, committing or pushing; `-NoPush` stops before the push.

It runs at Idle priority on 4 CPUs, the Release build included, so the PC stays usable; `-Affinity` takes another
CPU mask (`0xF0` by default). Run it as above: started through `cmd /c start /wait`, its exit code is lost.

## Publishing MitCue (maintainer)

MitCue's source lives on the `mitcue` branch, with its own history. Once, make its checkout next to this folder as
a worktree of this clone on that branch:

```powershell
git -C <DalamudPlugins clone> remote set-branches --add origin mitcue
git -C <DalamudPlugins clone> fetch origin
git -C <DalamudPlugins clone> worktree add ..\MitCue mitcue
```

`<DalamudPlugins clone>` is this folder, so from here these are the same commands without the `-C` part. The first
one lets a clone made with `--single-branch`, which fetches only `main`, fetch `mitcue` and keep `origin/mitcue`
up to date; it's harmless in a full clone. Before its first push, `mitcue` isn't on GitHub yet, and with the first
line in place `fetch origin` fails. For that push, fetch its commits from where they are into a local `mitcue`
instead (from a bundle file: `git fetch <file> mitcue:mitcue`), make the worktree, scan as below, push with
`git push -u origin mitcue`, and only then run the first two lines.

Change MitCue in `..\MitCue` and commit there. A push of `mitcue` makes its commits public, and the first push
makes its whole history public at once, so before each `git push origin mitcue`, scan what it would publish:

```powershell
powershell -ExecutionPolicy Bypass -File .\publish-mitcue.ps1 -ScanHistory
```

It reads the whole history of `..\MitCue`'s `mitcue` (every commit's author, committer, message and paths, and
every version of every file, inside the `.xlsx` test fixtures too) for the identity terms below, and builds and
pushes nothing. Push only when it's clean. Pushes and pull requests to `mitcue` run its CI. Then, from this folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\publish-mitcue.ps1 -Changelog "What changed" -DryRun
powershell -ExecutionPolicy Bypass -File .\publish-mitcue.ps1 -Changelog "What changed"
```

It publishes only with a changelog, when this folder is on `main` with nothing uncommitted or untracked, when
`..\MitCue` (or `-Source`) is on `mitcue` with a clean tree and its commit is in the pushed `mitcue` of the
remote that points at NoirYamato/DalamudPlugins (`origin` here; `-Remote` names another, `-Fetch` fetches it
first), when the history scan above is clean, when the new version is higher than the listed one, when
`dotnet test MitCue.slnx -c Release` passes on that commit, and when an identity scan of the built plugin zip and
the source zip, the files inside their zips (such as the source's `.xlsx` test fixtures) included, finds every one
of its control strings and none of the terms in NoirMech's `%APPDATA%\NoirMech\identity-terms.txt` (or the file
`-TermsFile` names). The version is the `<Version>` in `src\MitCue\MitCue.csproj` with a fourth part that goes up
by one on every publish. Its commit takes only `plugins/MitCue` and `repo.json`, and in `repo.json` it replaces
only MitCue's entry, as `publish.ps1` replaces only NoirMech's. `-DryRun` runs every gate, the tests, the build and
the scans, then stops before copying, committing or pushing; `-NoPush` stops before the push.

Like `publish.ps1`, it runs at Idle priority on the `-Affinity` CPUs (`0xF0` by default), the tests and the
Release build included. Run it as above, so its exit code isn't lost.

After changing either publish script, run
`powershell -ExecutionPolicy Bypass -File .\tests\Test-PublishScripts.ps1`. Without building anything, it checks
that each script keeps the other's `repo.json` entry intact, non-ASCII text included, and that `-ScanHistory`
finds terms anywhere in a branch's history.
