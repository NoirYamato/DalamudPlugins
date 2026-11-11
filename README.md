# MitCue

MitCue is a Dalamud plugin for FINAL FANTASY XIV. It takes your raid's mitigation plan and, during the
fight, shows a small window that tells **you** which of **your** actions to press next. The reminders follow
the boss in real time, using the fight's cactbot timeline and the boss casts the game shows you.

**MitCue only reminds you.** It never presses, queues or targets anything and never changes your hotbars.
It reads what your client already shows you (enemy cast bars, resolved actions, your cooldowns, the
countdown) and draws a window.

It is built for one raid static. The first users are a White Mage on the *Carry the Clear* TOP plan and a
Scholar on the *LPDU* TOP plan. Supported fights:

| Fight | Zone id | Sync level | Status |
|-------|---------|------------|--------|
| The Omega Protocol (Ultimate), TOP | 1122 | 90 | first target |
| The Epic of Alexander (Ultimate), TEA | 887 | 80 | planned |
| Dancing Mad (Ultimate), DMU | 1363 | 100 | planned |

Design and research notes: [docs/DESIGN.md](docs/DESIGN.md). How the plugin works inside, and what to test
in game: [docs/PLUGIN.md](docs/PLUGIN.md).

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [Commands](#commands)
- [What the overlay shows](#what-the-overlay-shows)
- [Settings](#settings)
- [Plans and importing](#plans-and-importing)
- [Troubleshooting](#troubleshooting)
- [Privacy](#privacy)
- [Building from source](#building-from-source)
- [Releasing](#releasing)
- [Credits and licenses](#credits-and-licenses)

## Install

Full step-by-step instructions are in **[docs/INSTALL.md](docs/INSTALL.md)**. In short:

- **For the static (normal):** add this custom plugin repository in `/xlsettings` > **Experimental** >
  **Custom Plugin Repositories**, click **Save**, then install MitCue from `/xlplugins` > **All Plugins**:

  ```
  https://raw.githubusercontent.com/OWNER/REPO/repo/repo.json
  ```

  *(Maintainer: replace `OWNER/REPO` with the real repository after the first release. The URL only works
  once a `v*` tag has been released and the repository is public.)*
- **For testing unreleased builds:** load MitCue as a dev plugin. Tick **Enable Developer Mode** in
  `/xlsettings` > **Experimental**, then add the full path to `MitCue.dll` under **Dev Plugin Locations**.

## Quick start

This version has two gaps that later updates close: it loads its fight data and plans only when a preview
starts (step 2), and no plans come with it yet (step 4).

1. Type `/mitcue` to open the settings window. The **Status** line at the top tells you what MitCue is doing
   right now.
2. Type `/mitcue preview`, and if a preview starts, type `/mitcue preview` again to stop it. This loads the
   fight data and the plans. If you have no plan yet, MitCue answers *No plan for The Omega Protocol
   (Ultimate)*: the data is loaded all the same. **Do this every time you start the game, and again whenever
   MitCue is reloaded (for example after an update).** Until then the Status line says *Fight data did not
   load*, the **Plans** tab only says *Loading...*, and MitCue shows no reminders in TOP.
3. **General** tab: set **My usual party slot** (for example H1 for the White Mage, H2 for the Scholar). MitCue
   uses it to work out which column of a plan is yours.
4. **Plans** tab: add your plan. No plans come with the plugin yet, so you get yours from whoever looks after
   the static's plans, or from MitCue's developer, in one of two forms:
   - a **share code**, one long line of text that starts with `MITCUE1:`. Paste it into **Share code** and
     click **Import share code**;
   - a **plan file** called `<plan id>.json`. Copy it into the plans folder,
     `%AppData%\XIVLauncher\pluginConfigs\MitCue\plans\` (the **Debug** tab shows the exact path; create the
     `plans` folder if it is missing), then click **Reload plans**.

   Then, in **Active plan per fight and job**, find the row *The Omega Protocol (Ultimate)* with your job and:
   - choose the plan under **Active plan**, or leave it on *automatic*, which uses the first plan that has a
     column for your job;
   - check **My column**: MitCue suggests a column from your job and slot, and you can pick another one (for
     example *White Mage* or *H2 SCH*).
5. Type `/mitcue preview top` to watch a simulated pull: a 10-second countdown, then the fight at real speed.
   Drag the overlay to where you want it. `/mitcue preview top p3` starts at phase 3, and `/mitcue preview`
   stops the preview.
6. **Overlay** tab: tick **Lock position**, and **Click-through** if you like. Adjust **Scale**, **Background
   opacity** and **Maximum rows**.
7. In TOP, with the fight data loaded (step 2): when you enter the zone on a job that has an active plan, the
   Status line says *Ready* and the overlay shows *TOP - waiting for pull*. Start a `/countdown` and the
   pre-pull reminders appear. From the pull onwards the reminders follow the boss. After a wipe you get a
   one-line summary in chat.

## Commands

| Command | What it does |
|---------|--------------|
| `/mitcue` | Opens or closes the settings window (the plugin installer's settings button does the same) |
| `/mitcue overlay` | Shows or hides the reminder overlay |
| `/mitcue preview [fight] [phase]` | Starts a simulated pull, e.g. `/mitcue preview`, `/mitcue preview top`, `/mitcue preview top p5`. If you leave out `fight`, MitCue uses the fight of the zone you are in, else TOP. Phases are `p1`–`p6` for TOP. `/mitcue preview` on its own stops a running preview |
| `/mitcue reset` | Resets the live session: ends the current pull without a summary and starts again from a clean state. Use it if reminders get stuck or after an error |
| `/mitcue debug` | Opens the settings window on the **Debug** tab |

### The preview

`/mitcue preview` plays a simulated pull through its own copy of the reminder engine, so it never affects the
real fight. Use it to place the overlay and to read through a plan before raid night.

- **Which plan:** the active plan for the fight and your current job. If your job has no plan for that fight
  (for example you are on a Dancer, or in town on a crafter), MitCue takes the first plan for the fight and
  previews it as the job of the column you saved for it, or else as the plan's first job.
- **Timing:** a whole-fight preview begins with a simulated 10-second countdown. A phase preview starts
  2 seconds before a simulated pull and jumps to that phase about 2 seconds after the pull. Tick **Run
  /mitcue preview at 4x speed** in **General** to run it 4 times as fast.
- **Your own presses count:** go to a striking dummy and use your actions when they turn **NOW**. They are
  marked done, so you can try the usage tracking out of combat. Cooldown and resource warnings are not
  shown in the preview.
- **The end:** the preview stops by itself after the last event, says *Preview finished.* in chat, and prints
  a `[Preview]` summary.

## What the overlay shows

```
TOP - P1: Omega - 1:15
 [icon]  NOW   Temperance           Pantokrator
 [icon]  4.3s  Divine Benison       Pantokrator     on MT
 [icon]  12s   Plenary Indulgence   Pantokrator                 ready in 6s
```

- **Header:** the fight, then *pull in 12.3s* during a countdown, or the phase and fight clock during a pull,
  or *waiting for pull (plan name)* while you wait in the zone. A preview adds `PREVIEW 1x` or `PREVIEW 4x`
  at the front.
- **Rows:** up to **Maximum rows** of your next reminders, earliest first. Each row has the action icon, the
  time left, the action name (in your client's language), the mechanic, and the target or note from the plan.
- **Time and state:**
  - a countdown (`12s`, `3.1s`): coming up;
  - **NOW** on a pulsing orange row: press it now. A sound plays once when a row turns NOW;
  - a green check mark: done. If you pressed it before its useful window, the name shows *(early)*;
  - a red cross: missed;
  - `skip`: a phase push skipped it (only shown if you ask for it).

  Finished rows stay for 3 seconds, then disappear.
- **Warnings (orange):** *ready in 6s* when the action will still be on cooldown at its planned time, and
  *no Lily*, *no Aetherflow* and so on when a job resource is missing (Lily, Blood Lily, Aetherflow,
  Addersgall, Addersting).
- **Other columns:** with **Also show the plan's other columns** ticked, your co-healer's calls are listed
  dimmed with a tag, e.g. `[H2] Expedient`. Your own rows are never pushed out by theirs.

When reminders appear and turn NOW: a reminder appears **Show reminders this early** (15 s) before its planned
press time. It turns NOW **Turn "NOW" this early** (1.5 s) before. It stays NOW until just before the hit for
mitigation and shields, or until 5 s after the planned time for heals, and then counts as missed. An entry
marked *on cooldown* in the plan stays NOW until the action is ready. Plan entries without a time get a
sensible press time from the action's kind and duration (details in [docs/DESIGN.md](docs/DESIGN.md) §5).

A pull ends on a wipe, a duty restart, a clear, when you leave the zone, or after 10 seconds out of combat
with no enemy activity. The chat summary then reads, for example:
`TOP pull ended (wipe): 12 done, 2 missed. Missed: Temperance (Cosmo Memory), Divine Benison (Solar Ray).`

## Settings

Changes apply immediately and are saved automatically. The exception is changes that need a new reminder
session, such as switching plan or column: those wait until the current countdown or pull is over. Defaults
are in brackets.

**General**

| Setting | Meaning |
|---------|---------|
| Show reminders this early [15 s] | How long before the planned press time a reminder appears (3–60 s) |
| Turn "NOW" this early [1.5 s] | How long before the planned press time a reminder turns NOW (0–10 s) |
| Play a sound when a reminder turns NOW [on] | Plays one of the game's chat sound effects |
| Sound effect &lt;se.N&gt; [6] | Which effect, 1–16, the same sounds as `<se.1>`–`<se.16>` in chat. **Test** plays it |
| Print a done/missed summary after each pull [on] | One chat line per pull, listing up to five missed calls |
| Also show the plan's other columns, dimmed [off] | Shows your co-healer's (or anyone else's) calls in the overlay |
| Show reminders skipped by a phase push [off] | Keeps skipped calls visible for a moment, marked `skip` |
| My usual party slot [not set] | MT, OT, H1, H2, M1, M2, R1 or R2. Used to suggest your plan column |
| Run /mitcue preview at 4x speed [off] | Preview speed |
| Record pulls [off] | Writes a log of each zone visit for tuning the sync (see [Troubleshooting](#troubleshooting)). The folder is shown under the checkbox |

**Overlay**

| Setting | Meaning |
|---------|---------|
| Show the overlay [on] | Same as `/mitcue overlay` |
| Lock position [off] | The overlay can't be dragged |
| Click-through [off] | Mouse clicks go through the overlay to the game. This also locks it |
| Always show [off] | Keeps the overlay visible with sample rows when nothing is happening, so you can place it |
| Scale [1.00x] | Text and icon size (0.5–3) |
| Background opacity [0.85] | 0 = transparent |
| Maximum rows [5] | 1–15 |
| Columns [all on] | Action icons, mechanic name, target and notes, cooldown/resource warnings, header line |
| Reset position | Moves the overlay back to the default position |

**Plans:** the active plan and your column for each fight and job, the list of plans, and the import controls
(see [Plans and importing](#plans-and-importing)).

**Debug:** a live report of what MitCue sees, with buttons: **Copy to clipboard**, **Reset live session**,
**Reload data** and **Clear problems**. See [Troubleshooting](#troubleshooting).

**About:** a short description, the commands, credits and the version number.

Above the tabs, every tab shows the **Status** line, the running preview, and a **Problems** list when
something has gone wrong.

## Plans and importing

A plan is MitCue's own format for a mitigation sheet: who presses what, for which mechanic, when. Each entry
is tied to a mechanic of the fight, such as *Cosmo Memory*, rather than to a clock time, so it stays in sync
when the boss is pushed early. The format is described in [docs/DESIGN.md](docs/DESIGN.md) §3.2. A plan can
hold every player's column. Each player picks theirs in **My column**, so the whole static can share one plan.

**Available now**

- **Share codes.** **Copy share code** in the plan list copies a plan as one long line of text that starts
  with `MITCUE1:`. Send it to a static member. They paste it into the **Share code** box (or click **Paste from
  clipboard**) and click **Import share code**. Imported plans are listed as *user* plans and can be deleted
  (click **Delete**, then click again to confirm).
- **Plan files.** MitCue keeps each imported plan as a `<plan id>.json` file in
  `%AppData%\XIVLauncher\pluginConfigs\MitCue\plans\`. **Reload plans** re-reads that folder, for example after
  you copy a plan `.json` file into it. Only `.json` files are read there: YAML plan files (`*.mitplan.yaml`,
  the format in [docs/DESIGN.md](docs/DESIGN.md) §3.2) are not.

**Coming later**

- **Bundled plans.** The static's TOP plans, converted from the original sheets, will ship inside the plugin
  (from `plans/` in this repository) and appear in the Plans tab as *bundled*, read-only. This version
  contains none yet.
- **The developer command line** (`tools/MitCue.Cli`, to convert sheets and simulate pulls) is not written
  yet.

**Coming soon: the import window**

**Import from sheet...** is visible in the Plans tab but not finished yet. In this version it only prints a
note in chat. It will offer three ways in:

1. **Sheet link**: paste the link to a public Google Sheet (or Doc). MitCue downloads it only when you click
   import, detects the layout, and converts it.
2. **Paste**: copy cells from any sheet and paste them in as text.
3. **AI prompt**: for layouts MitCue can't read, it gives you a prompt to use with any AI chat. The prompt
   holds the plan format, the fight's mechanic list and your job's action names. You paste the answer back in.

Whichever way you use, the result goes through a **review** screen: each entry shows how sure the importer
is, you correct whatever it got wrong, and your corrections are remembered as personal shorthand for next
time. Until then, plans reach MitCue as share codes or plan files from the developer.

## Troubleshooting

**The overlay doesn't appear**

Read the **Status** line at the top of `/mitcue`:

- *No supported fight in this zone*: MitCue only runs in its fights (TOP for now). Use `/mitcue preview` to
  see the overlay anywhere.
- *no plan for WHM. Choose one in /mitcue > Plans*: no plan has a column for your job. Pick one in the Plans
  tab.
- *none of its N entries match …*: the plan is active, but the column chosen in **My column** is not yours.
- *Fight data did not load*, or the Plans tab only says *Loading...*: this version loads its fight data and
  plans only when a preview starts, so this is what you see after every game start or plugin reload (see
  [Quick start](#quick-start) step 2). Type `/mitcue preview`, and `/mitcue preview` again if a preview
  started. If the message stays after that, look at the **Problems** list.
- Also check that the overlay is switched on (`/mitcue overlay`, or **Show the overlay** in the Overlay tab).

**Reminders come too early or too late, or the clock jumps**

Note the mechanic and roughly how many seconds off it was, then record a pull and send it in (see below).
The Debug tab's *last syncs* list shows each time MitCue moved its fight clock, and why.

**No sound**

Click **Test** next to **Sound effect** in General. If you hear nothing, check the game's sound settings for
system sounds (the `<se.N>` sounds); muting system sounds mutes MitCue too.

**My presses are not marked done**

Look at *Recent own uses* in the Debug tab. It should list your actions as you use them. If it stays empty,
send the debug report.

**An error appeared, or reminders stopped**

The **Problems** list (top of the settings window, and at the end of the debug report) names what failed.
`/mitcue reset` restarts the live session. **Reload data** in the Debug tab unloads the fight data and ends
the live session; both come back only when the next `/mitcue preview` loads the data again, so don't press it
during a pull. Plans are re-read with **Reload plans** in the Plans tab. Dalamud's own log (`/xllog`) has the
full error.

### Sending a bug report

1. Right after it happens, type `/mitcue debug` and click **Copy to clipboard**.
2. Paste the report into the static's channel or a text file, and add a short note: what you expected, what
   happened, which phase and roughly when.

The report covers the game state (zone, job, synced level, client language), which data loaded, the live
session (plan, column, fight clock, recent syncs, every reminder with its times and state), the preview,
the recorder, the last 50 game events, your recent action uses and the problem list. Other players' actions
are left out.

### Recording pulls

A recording is a log of what MitCue saw in one zone visit: enemy casts, resolved actions, combat start and
end, countdowns, duty wipes and your own action uses, each with a timestamp. Recordings are the best way to
tune the sync to real pulls.

1. Before you zone in: `/mitcue` > **General** > tick **Record pulls**. The folder is shown under the checkbox.
2. Play as usual. Each zone visit gets its own file, `<zone id>-<date>-<time>.jsonl` (TOP is `1122-…`), and
   every pull of that visit goes into it. While a file is open, the General tab shows *Recording to … (N
   lines)*.
3. When you're done, untick **Record pulls**, or simply leave the zone. Either one closes the file.
4. Open the folder: `%AppData%\XIVLauncher\pluginConfigs\MitCue\recordings\` (the exact path is also in the
   Debug tab, under *Recorder*). Zip the file and send it with a note of which pulls went wrong.

Recording runs in every zone while it is ticked, so switch it off when you don't need it.

## Privacy

- **No network traffic of its own.** MitCue doesn't phone home, check for updates or send telemetry. The only
  downloads it will make are the ones you start yourself: a plan link you import, once the import window
  arrives. Installing and updating the plugin is done by Dalamud, from the repository you added.
- **Everything stays on your PC.** Settings, imported plans and recordings are files in Dalamud's
  `pluginConfigs` folder (see [docs/INSTALL.md](docs/INSTALL.md#where-mitcue-keeps-its-files)).
- **Recordings are off by default and never contain player names.** They hold timestamps, action IDs,
  numeric entity IDs, enemy names, countdowns and your own action uses. They don't include chat, player names
  or your character name.
- **The debug report** is only built when you open the Debug tab and only leaves your PC if you paste it
  somewhere. It leaves out other players' actions, but it does contain file paths, which include your Windows
  user name (`C:\Users\<name>\…`). Remove that part before you post the report in public.
- **Chat output is local.** The pull summary and MitCue's messages are printed to your own chat log only.

## Building from source

You need the .NET 10 SDK and Dalamud's developer files:

- Windows: XIVLauncher installs them at `%AppData%\XIVLauncher\addon\Hooks\dev\`.
- Linux: `~/.xlcore/dalamud/Hooks/dev/`.
- Anywhere else: unzip <https://goatcorp.github.io/dalamud-distrib/latest.zip> and set `DALAMUD_HOME` to that
  folder.

```sh
dotnet build MitCue.slnx -c Release
dotnet test tests/MitCue.Core.Tests/MitCue.Core.Tests.csproj
```

The plugin package (the zip Dalamud installs, plus its manifest) is written to
`src/MitCue/bin/Release/MitCue/latest.zip` and `MitCue.json`. To test without packaging, add
`src/MitCue/bin/Release/MitCue.dll` (or the Debug build) as a dev plugin; `MitCue.json` sits next to it.

Repository layout:

```
src/MitCue.Core/        core library: plans, timelines, sync engine, reminders, importer (no Dalamud)
src/MitCue/             the Dalamud plugin
tests/MitCue.Core.Tests xUnit tests (+ tests/fixtures/sheets: snapshots of the example sheets)
tools/MitCue.Cli/       developer command line (convert and simulate; not written yet)
tools/make-repo-json.py writes the Dalamud custom-repository file (repo.json)
tools/RepoJsonCheck/    checks repo.json and the plugin zip with Dalamud's own manifest types
data/                   fight timelines (cactbot), fight catalogs, action data and shorthand aliases
docs/                   design, install guide, plugin internals and test checklist, research notes
.github/workflows/      CI (build, test, package) and Release (tag -> GitHub release + repo.json)
```

**CI** (`.github/workflows/ci.yml`) runs on every push and pull request. It downloads the current Dalamud
release (cached per Dalamud version), builds the solution in Release, runs the unit tests and the tools'
tests, writes a sample `repo.json` and checks it, together with the zip, against Dalamud's own manifest
types. It uploads `latest.zip` as the **MitCue-plugin** artifact. The check runs with `--strict`. If a new
Dalamud release adds a manifest field, CI fails at this step until `tools/make-repo-json.py` writes the new
field too. Releases are not blocked by this. For a few minutes after a Dalamud release, Dalamud's version file
and its `latest.zip` can disagree. CI and releases then fail at the Dalamud download step instead of caching
the wrong Dalamud: re-run the job a little later. The tools' tests (`tools/tests`) also run the workflows'
Dalamud download and `repo` branch steps against local stand-ins.

## Releasing

1. Make sure CI is green for the commit you want to release.
2. Create an annotated tag and push it:

   ```sh
   git tag -a v0.2.0 -m "MitCue 0.2.0" -m "- what changed, one line per change"
   git push origin v0.2.0
   ```

3. The **Release** workflow (`.github/workflows/release.yml`) then:
   - builds with `-p:Version=0.2.0`, so the plugin version is 0.2.0.0, and runs all the tests;
   - writes `repo.json` with `tools/make-repo-json.py`. The download links point at this release's
     `latest.zip`, the changelog is the tag message, and `LastUpdate` is now;
   - checks `repo.json` and `latest.zip` with `tools/RepoJsonCheck`, which loads Dalamud's own
     `RemotePluginManifest` and `LocalPluginManifest` types and repeats the installer's checks: same internal
     name and the exact same version in the zip as in `repo.json`, the right API level, valid download links;
   - creates the GitHub release with `latest.zip` attached and checks that it downloads without signing in;
   - commits `repo.json` to the **`repo`** branch. That branch serves
     `https://raw.githubusercontent.com/OWNER/REPO/repo/repo.json`. The run's summary shows the exact URL.
     Next to it, `published.json` records the release and commit each listed version was built from.

Rules:

- Tags are `vMAJOR.MINOR`, `vMAJOR.MINOR.PATCH` or `vMAJOR.MINOR.PATCH.REVISION`, with numbers only.
  Pre-release suffixes such as `-beta` are rejected, because Dalamud compares plain version numbers.
- Each new release needs a higher version than the one in `repo.json`. Dalamud only offers an update when the
  version goes up, so the workflow refuses a lower version, and also the same version built from another tag
  or commit: players who installed that version would never get the new build. For example `v1.3` and
  `v1.3.0` are both version 1.3.0.0, and a tag that is moved to another commit keeps its version. **Never
  move a released tag to another commit**: tag the fix as a new version, such as `v1.3.1`.
- Re-running a release job (same tag, same commit) replaces the release's `latest.zip` and publishes
  `repo.json` again.
- Push one release tag at a time. Releases run one after another, and tags pushed together wait their turn,
  but they may start in any order, and an older version that runs after a newer one is refused.
- The repository must be **public**, because Dalamud downloads anonymously.
- Leave `repo.json` and `published.json` on the `repo` branch to the workflow.

To build a `repo.json` by hand, run `python3 tools/make-repo-json.py --help` and
`dotnet run --project tools/RepoJsonCheck -- --help`.

## Credits and licenses

- **Fight timelines** (`data/timelines/*.txt`) are copied unmodified from
  [cactbot](https://github.com/OverlayPlugin/cactbot) (`ui/raidboss/data/`). They are licensed under the
  Apache License 2.0, copyright cactbot contributors. See [data/timelines/NOTICE](data/timelines/NOTICE) and
  [data/timelines/LICENSE.cactbot](data/timelines/LICENSE.cactbot).
- **YamlDotNet**, shipped in the plugin zip as `YamlDotNet.dll`: MIT License.
- **Dalamud**, **FFXIVClientStructs** and **Lumina** are provided by Dalamud at run time and are not shipped
  with MitCue.
- **Example plans:** *Carry the Clear: TOP Healing Plan* and the *LPDU TOP Mitigation Sheet*, by their
  authors. They are used as test fixtures and converted for the static's own use.
- **MitCue's own code** doesn't have a license file yet. Ask the author before you reuse it.

FINAL FANTASY XIV © SQUARE ENIX CO., LTD. MitCue is a third-party tool and is not affiliated with or endorsed
by Square Enix.
