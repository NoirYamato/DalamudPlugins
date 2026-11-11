# MitCue

MitCue is a Dalamud plugin for FINAL FANTASY XIV. It takes your raid's mitigation plan and, during the
fight, shows a small window that tells **you** which of **your** actions to press next. The reminders follow
the boss in real time, using the fight's cactbot timeline and the boss casts the game shows you.

**MitCue only reminds you.** It never presses, queues or targets anything and never changes your hotbars.
It reads what your client already shows you (enemy cast bars, resolved actions, your cooldowns, the
countdown) and draws a window.

It is built for one raid static: a White Mage (H1) on the *Carry the Clear* TOP plan and a Scholar (H2) on
the *LPDU* TOP plan. Both plans come with the plugin.

| Fight | Zone id | Sync level | Plans that come with MitCue |
|-------|---------|------------|-----------------------------|
| The Omega Protocol (Ultimate), TOP | 1122 | 90 | *Carry the Clear – WHM*, *Carry the Clear – WHM + SCH*, *LPDU TOP Mitigation* |
| The Epic of Alexander (Ultimate), TEA | 887 | 80 | none: [import](#importing-a-plan) your own |
| Dancing Mad (Ultimate), DMU | 1363 | 100 | none: [import](#importing-a-plan) your own |

The TEA and DMU fight data is a first draft: no mitigation sheet has been mapped onto it yet. In TEA the
fight clock follows phase pushes like in TOP. In DMU, cactbot marks the starts of P2 and P4 with an event
MitCue cannot see, so MitCue uses boss casts there instead; those have not been checked against real pulls
yet.

More documentation: [docs/INSTALL.md](docs/INSTALL.md) (installing, updating, files),
[docs/PLUGIN.md](docs/PLUGIN.md) (how the plugin works, in-game test checklist),
[docs/DESIGN.md](docs/DESIGN.md) (the design).

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [Commands](#commands)
- [What the overlay shows](#what-the-overlay-shows)
- [Settings](#settings)
- [Plans and importing](#plans-and-importing)
- [Troubleshooting](#troubleshooting)
- [Privacy](#privacy)
- [For developers](#for-developers)
- [Credits and licenses](#credits-and-licenses)

## Install

MitCue comes from the same custom plugin repository as NoirMech. **If you already added it for NoirMech,
skip to step 2.**

1. Type `/xlsettings`, open **Experimental**, and under **Custom Plugin Repositories** paste this URL into the
   empty row, click **+**, then click **Save**:

   ```
   https://raw.githubusercontent.com/NoirYamato/DalamudPlugins/main/repo.json
   ```

2. Type `/xlplugins`, search **All Plugins** for `MitCue` and click **Install**.

Keep that repository row as it is and enabled: Dalamud stops loading MitCue (and NoirMech) when it is
removed, disabled or changed. Updates, uninstalling, test builds and where MitCue keeps its files:
[docs/INSTALL.md](docs/INSTALL.md).

## Quick start

1. Type `/mitcue` to open the settings window. The **Status** line at the top tells you what MitCue is doing
   right now.
2. **General** tab: set **My usual party slot**, H1 for the White Mage and H2 for the Scholar.
3. **Plans** tab: in **Active plan per fight and job**, find the row *The Omega Protocol (Ultimate)* with your
   job. Without a choice of yours, MitCue uses:
   - White Mage: *(automatic) Carry the Clear – WHM*, column *(suggested) White Mage (WHM)*;
   - Scholar: *(automatic) LPDU TOP Mitigation*, column *(suggested) (any column for my job in slot H2)*.
     LPDU writes the Scholar's P1/P2 calls in its "H2" column and the later ones in "H2 SCH"; this choice
     takes both.

   To follow Carry the Clear's combined healer tab instead, choose *Carry the Clear – WHM + SCH* under
   **Active plan** (the Scholar's column is then *Scholar*).
4. Type `/mitcue preview top` to watch a simulated pull: a 10-second countdown, then the fight at real speed.
   Drag the overlay to where you want it. `/mitcue preview top p3` starts at phase 3, and `/mitcue preview`
   stops the preview.
5. **Overlay** tab: tick **Lock position**, and **Click-through** if you like. Adjust **Scale**, **Background
   opacity** and **Maximum rows**.
6. In TOP, the Status line says *Ready: Carry the Clear – WHM (57 of 57 entries are yours).* (or the
   Scholar's plan) and the overlay shows *TOP - waiting for pull (Carry the Clear – WHM)*. Start a
   `/countdown` and the pre-pull reminders appear. From the pull on, the reminders follow the boss. After a
   wipe you get a one-line summary in chat.

## Commands

| Command | What it does |
|---------|--------------|
| `/mitcue` | Opens or closes the settings window (so do MitCue's buttons in the plugin installer) |
| `/mitcue import` | Opens the import window (see [Importing a plan](#importing-a-plan)) |
| `/mitcue overlay` | Shows or hides the reminder overlay |
| `/mitcue preview [fight] [phase]` | Starts a simulated pull, e.g. `/mitcue preview`, `/mitcue preview top`, `/mitcue preview top p5`. Fights are `top`, `tea` and `dmu`. If you leave out `fight`, MitCue uses the fight of the zone you are in, else TOP. Phases are `p1`–`p6` for TOP, `p1`–`p4` for TEA and `p1`–`p5` for DMU. `/mitcue preview` on its own stops a running preview |
| `/mitcue reset` | Resets the live session: ends the current pull without a summary and starts again from a clean state. Use it if reminders get stuck or after an error |
| `/mitcue debug` | Opens the settings window on the **Debug** tab |

### The preview

`/mitcue preview` plays a simulated pull through its own copy of the reminder engine, so it never affects the
real fight. Use it to place the overlay and to read through a plan before raid night.

- **Which plan:** the plan MitCue would use in the fight on your current job. If no plan applies to your job
  (for example in town on a crafter), MitCue takes the first plan for the fight and previews it as the job of
  the column you chose for it, or else as the plan's first job. TEA and DMU have no plan until you import one.
- **Timing:** a whole-fight preview begins with a simulated 10-second countdown. A phase preview starts
  2 seconds before a simulated pull and jumps to that phase about 2 seconds after the pull. Tick **Run
  /mitcue preview at 4x speed** in **General** to run it 4 times as fast.
- **Like a real pull:** the preview plays only the kinds of event the game gives MitCue (combat start and end,
  boss casts, resolved actions, wipes), so the fight clock follows it the way it follows a real pull.
- **Your own presses count:** use your actions when they turn **NOW**, for example on a striking dummy. They
  are marked done. Cooldown and resource warnings are not shown in the preview.
- **The end:** the preview stops by itself after the last event and says *Preview finished.* in chat, after
  a `[Preview]` summary when **Print a done/missed summary after each pull** is on.

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
  If more of your calls are coming than fit, the last line says `+N more`.
- **Time and state:**
  - a countdown (`12s`, `3.1s`): coming up;
  - **NOW** on a pulsing orange row: press it now. A sound plays once when a row turns NOW;
  - a green check mark: done. If you pressed it before its useful window, the name shows *(early)*;
  - a red cross: missed;
  - `skip`: a phase push skipped it (only shown if you ask for it).

  Finished rows stay for 3 seconds, then disappear. With **Track done/missed** off, a call disappears as soon
  as you use it or its time has passed.
- **Warnings (orange):** *ready in 6s* when the action will still be on cooldown at its planned time, and
  *no Lily*, *no Aetherflow* and so on when a job resource is missing (Lily, Blood Lily, Aetherflow,
  Addersgall, Addersting).
- **Your co-healer's calls:** with **Also show my co-healer's calls** ticked, they are listed dimmed with
  their column as a tag, e.g. `[H1 WHM] Temperance`. Your own rows are never pushed out by theirs.

When reminders appear and turn NOW: a reminder appears **Show reminders this early** (15 s) before its planned
press time. It turns NOW **Turn "NOW" this early** (1.5 s) before. It stays NOW until just before the hit for
mitigation and shields, or until 5 s after the planned time for heals, and then counts as missed. An entry
marked *on cooldown* in the plan stays NOW until the action is ready. A call for the end of a phase (*as P1
dies*) turns NOW when a push ends that phase early. Plan entries without a time get a sensible press time
from the action's kind and duration (details in [docs/DESIGN.md](docs/DESIGN.md) §5).

A pull ends on a wipe, a duty restart, a clear, when you leave the zone, or after 10 seconds out of combat
with no enemy activity. The chat summary then reads, for example:
`TOP pull ended (wipe): 12 done, 2 missed. Missed: Temperance (Cosmo Memory), Divine Benison (Solar Ray).`

## Settings

Changes apply immediately and are saved automatically. The exception is changes that need a new reminder
session, such as switching plan or column: those wait until the current countdown or pull is over.

**General**

| Setting | Default | Meaning |
|---------|---------|---------|
| *Timing* | | |
| Show reminders this early | 15 s | How long before the planned press time a reminder appears (3–60 s) |
| Turn "NOW" this early | 1.5 s | How long before the planned press time a reminder turns NOW (0–10 s, and never more than the setting above) |
| *Sound* | | |
| Play a sound when a reminder turns NOW | on | Plays one of the game's chat sound effects |
| Sound effect &lt;se.N&gt; | 6 | Which effect, 1–16, the same sounds as `<se.1>`–`<se.16>` in chat. **Test** plays it |
| *Reminders* | | |
| Track done/missed (keep used and missed calls listed with a check or a cross) | on | Off: a call disappears once used or once its time has passed, and no summary is printed |
| Print a done/missed summary after each pull | on | One chat line per pull, listing up to five missed calls. Greyed out while **Track done/missed** is off |
| Also show my co-healer's calls (the plan's other columns for my role), dimmed | off | Your co-healer's calls (a tank's co-tank, a DPS's other DPS), never your own seat's other columns. In a party, only columns of jobs someone in the party plays |
| Show reminders skipped by a phase push | off | Keeps skipped calls listed for 3 seconds, marked `skip` |
| My usual party slot | (not set) | MT, OT, H1, H2, M1, M2, R1 or R2. Used to suggest your plan column |
| Run /mitcue preview at 4x speed | off | Preview speed |
| *Recording* | | |
| Record pulls (event log for tuning the sync; no player names) | off | Writes a log of each duty or fight-zone visit (see [Recording pulls](#recording-pulls)). The folder is shown under the checkbox |

**Overlay**

| Setting | Default | Meaning |
|---------|---------|---------|
| Show the overlay (/mitcue overlay) | on | Same as `/mitcue overlay` |
| Lock position | off | The overlay can't be dragged |
| Click-through (mouse clicks pass through; implies locked) | off | Mouse clicks go through the overlay to the game |
| Always show (sample rows when idle, to position it) | off | Keeps the overlay visible with sample rows when nothing is happening, so you can place it |
| Scale | 1.00x | Text and icon size (0.5–3) |
| Background opacity | 0.85 | 0 = transparent |
| Maximum rows | 5 | 1–15 |
| Columns | all on | Action icons, Mechanic name, Target and notes, Cooldown / resource warnings, Header line |
| Reset position | | Moves the overlay back to the default position |

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
when the boss is pushed early. A plan can hold every player's column; each player picks theirs in **My
column**, so the whole static can share one plan. The format is described in [docs/DESIGN.md](docs/DESIGN.md)
§3.2.

### Plans that come with MitCue

Three TOP plans, converted from the original sheets, are listed as *bundled* in the Plans tab. They can't be
changed or deleted.

| Plan | From |
|------|------|
| *Carry the Clear – WHM* | *Carry the Clear: TOP Healing Plan*, White Mage tab |
| *Carry the Clear – WHM + SCH* | the same sheet's combined White Mage and Scholar tab |
| *LPDU TOP Mitigation* | the *LPDU TOP Mitigation Sheet*: its plan tabs, with all eight seats and the tank pairs |

Plans you import are listed as *user* plans.

### Which plan and column MitCue uses

The table **Active plan per fight and job** has a row for every job the fight's plans name, plus your current
job (in green).

- **Active plan:** the plan you choose. On *(automatic: my own plan for this job, else the bundled plan meant
  for it)*, MitCue takes your own imported plan with calls for that job, else the bundled plan meant for the
  job (TOP: White Mage → *Carry the Clear – WHM*, Scholar → *LPDU TOP Mitigation*), else the first bundled
  plan by name with calls for the job. *(no plan: no reminders for this job)* switches MitCue off for that
  job.
- **My column:** the column you choose, or *(any column for my job)*. Without a choice (*suggested*), MitCue
  takes your job's column (the one in your slot if there are several), else your slot's column. When that
  column would leave out some of your calls, it takes the narrowest choice that has them all: on LPDU, the
  Scholar's *any column for my job in slot H2*, which includes the job-less "H2" column of P1 and P2.

### Importing a plan

Open the import window with `/mitcue import`, or with **Import from sheet...** in the Plans tab. It has four
steps.

1. **Source.** Choose one:
   - **Google Sheets / Docs link:** paste the link of a sheet or doc shared as "Anyone with the link"
     (Viewer) and click **Fetch**. A sheet keeps its colours, so grey *optional* calls stay optional, and its
     tab names as Google shows them. A doc's tables become tabs.
   - **Paste cells:** select the plan's cells in Google Sheets, including the header row, copy them and click
     **Paste from clipboard**, then **Read the cells**. A plan over several tabs is pasted one tab at a time:
     **Add as another tab** for each further tab. Pasted cells carry no colours.
   - **Paste plan YAML (AI route):** for layouts MitCue can't read. Check the fight and the jobs (yours is
     ticked), click **Copy AI prompt** and paste the prompt into any AI chat. If you loaded the sheet first,
     the prompt already holds the plan; otherwise paste the plan after it. Paste the AI's answer back and click
     **Check the YAML and review**. A MitCue plan file (YAML or JSON) can be pasted here too.
2. **Options:** the fight, the tabs and columns to import (MitCue ticks yours after looking at the tabs), the
   plan name and the calibration preset. Click **Import and review**. If the result looks wrong, **Not reading
   it right? Use the AI route**.
3. **Review:** every entry with its mechanic, timing, actions, who it is for, flags (*optional*, *when
   ready*, *at phase end*), problems and source cell. You can pick another mechanic, drag the offset
   (right-click for automatic timing), set the flags, change what to press, and delete entries. When you
   replace a note with the action it stands for, **Remember ...** saves that shorthand as a personal alias,
   so later imports read it.
4. **Save:** the plan name; **Save as** a new plan or in place of an earlier import of the same sheet (which
   keeps that plan's active-plan and column settings); and **Make it my active plan for … as …**, ticked by
   default. When your job's own column would miss some of your calls, the plan is saved with the column
   choice that has them all.

Downloads, reading and importing run in the background: the window shows what it is doing, **Cancel** stops
it, and closing the window cancels it too.

### Share codes and plan files

- **Share codes.** **Copy share code** in the plan list copies a plan as one long line of text that starts
  with `MITCUE1:`. Send it to a static member. They paste it into the **Share code** box (or click **Paste from
  clipboard**) and click **Import share code**. The plan is saved as a *user* plan; a share code of a bundled
  plan replaces the bundled one until it is deleted.
- **Plan files.** MitCue keeps each user plan as a `<plan id>.json` file in
  `%AppData%\XIVLauncher\pluginConfigs\MitCue\plans\`. **Reload plans** re-reads that folder and the bundled
  plans, for example after you copy a plan `.json` file into it. Only `.json` files are read there.
- **Checks.** Share codes and plan files skip the review, so MitCue checks them against the fight and the
  action data when they load: unknown mechanics and actions, actions the entry's job can't use at the
  fight's level, and the like. Problems are listed under **Problems** and named in the share-code message;
  calls with such a problem can never be done.
- **Delete.** User plans have a **Delete** button: click it, then **Click again to delete**.

## Troubleshooting

**The overlay doesn't appear**

Read the **Status** line at the top of `/mitcue`:

- *No supported fight in this zone (…)*: MitCue runs only in TOP, TEA and DMU. Use `/mitcue preview` to see
  the overlay anywhere.
- *… no plan for SCH. Choose one in /mitcue > Plans.*: no plan applies to your job, or you chose *(no
  plan)*. In TEA and DMU, import a plan first.
- *… none of its N entries match …*: the plan is active, but the column chosen in **My column** is not
  yours.
- *… N of them could not be scheduled (unknown mechanic, see the Debug tab).*: some entries name a mechanic
  the fight doesn't have; the Debug tab lists them.
- *Fight data did not load (see Problems in /mitcue).*: the data built into MitCue failed to load. Look at
  the **Problems** list, then try **Reload data** in the Debug tab.
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
`/mitcue reset` restarts the live session. **Reload data** in the Debug tab reloads the fight data on the
next frame; it ends the current pull and any running preview, so don't press it during a pull. Plans are
re-read with **Reload plans** in the Plans tab. Dalamud's own log (`/xllog`) has the full error.

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

MitCue records only inside duties and in the zones of its fights, never in towns or the open world (so a
striking dummy outside a duty is not recorded).

## Privacy

- **No network traffic of its own.** MitCue doesn't phone home, check for updates or send telemetry. It
  downloads only when you click **Fetch** in the import window: the sheet or doc you linked, from Google
  (`docs.google.com`, which hands exports over from `googleusercontent.com`). Installing and updating the
  plugin is done by Dalamud, from the repository you added.
- **The AI route sends nothing.** **Copy AI prompt** only copies text to your clipboard. What you paste into
  an AI chat is up to you.
- **Everything stays on your PC.** Settings (with your personal aliases), imported plans and recordings are
  files in Dalamud's `pluginConfigs` folder (see
  [docs/INSTALL.md](docs/INSTALL.md#where-mitcue-keeps-its-files)).
- **Recordings are off by default and never contain player names.** They hold timestamps, action IDs,
  numeric entity IDs, enemy names, countdowns and your own action uses. They don't include chat, player names
  or your character name.
- **The debug report** is only built when you open the Debug tab and only leaves your PC if you paste it
  somewhere. It leaves out other players' actions, but it does contain file paths, which include your Windows
  user name (`C:\Users\<name>\…`). Remove that part before you post the report in public.
- **Chat output is local.** The pull summary and MitCue's messages are printed to your own chat log only.

## For developers

MitCue's source is the `mitcue` branch of
[NoirYamato/DalamudPlugins](https://github.com/NoirYamato/DalamudPlugins/tree/mitcue). `publish-mitcue.ps1`
publishes builds to that repository's `main` branch, which serves the shared `repo.json`: see
[docs/PUBLISHING.md](docs/PUBLISHING.md).

### Building from source

You need the .NET 10 SDK and Dalamud's developer files:

- Windows: XIVLauncher installs them at `%AppData%\XIVLauncher\addon\Hooks\dev\`.
- Linux: `~/.xlcore/dalamud/Hooks/dev/`.
- Anywhere else: unzip <https://goatcorp.github.io/dalamud-distrib/latest.zip> and set `DALAMUD_HOME` to that
  folder.

```sh
dotnet build MitCue.slnx -c Release
dotnet test MitCue.slnx                               # core, plugin and CLI tests
python3 -m unittest discover -s tools/tests           # repo.json tools and the CI workflow's Dalamud step
dotnet run --project tools/MitCue.Cli -- --help       # developer CLI, see tools/MitCue.Cli/README.md
```

The plugin package (the zip Dalamud installs, plus its manifest) is written to
`src/MitCue/bin/Release/MitCue/latest.zip` and `MitCue.json`. To test without packaging, add
`src/MitCue/bin/Release/MitCue.dll` (or the Debug build) as a dev plugin; `MitCue.json` sits next to it.

Repository layout:

```
src/MitCue.Core/           core library (no Dalamud): fights, timelines, sync engine, plans, reminders, importer
src/MitCue/                the Dalamud plugin
tests/MitCue.Core.Tests/   core tests (+ tests/fixtures/sheets: snapshots of the two example sheets)
tests/MitCue.Plugin.Tests/ plugin tests (sessions, plan store, import window logic; built against Dalamud)
tests/MitCue.Cli.Tests/    CLI tests
tools/MitCue.Cli/          developer CLI: import sheets, print checklists, simulate pulls, check plans and data
tools/RepoJsonCheck/       checks a repo.json and the plugin zip with Dalamud's own manifest types
tools/make-repo-json.py    writes a repo.json from the plugin manifest (CI uses it for a sample)
tools/tests/               tests of the two tools above and of the CI workflow's Dalamud step
data/                      fight catalogs, cactbot timelines, action data, aliases (built into MitCue.Core.dll)
plans/                     the bundled TOP plans (built into MitCue.Core.dll)
assets/                    the icon
docs/                      design, install guide, plugin internals and test checklist, publishing, research notes
.github/workflows/ci.yml   CI
```

**CI** (`.github/workflows/ci.yml`) runs on every push to `mitcue` and every pull request into it. It builds
against the current Dalamud release, runs all three test projects and the tools' tests, checks the plugin zip
and a sample `repo.json` with `tools/RepoJsonCheck --strict` (a manifest field that a new Dalamud adds fails CI
until `tools/make-repo-json.py` writes it), and uploads `latest.zip` and `MitCue.json` as the
**MitCue-plugin** artifact. CI publishes nothing. For a few minutes after a Dalamud release, Dalamud's version
file and its `latest.zip` can disagree; CI then fails at the Dalamud download step: re-run it a little later.

## Credits and licenses

- **Fight timelines** (`data/timelines/*.txt`, built into `MitCue.Core.dll`) are copied unmodified from
  [cactbot](https://github.com/OverlayPlugin/cactbot) (`ui/raidboss/data/`). They are licensed under the
  Apache License 2.0, copyright cactbot contributors. See [data/timelines/NOTICE](data/timelines/NOTICE) and
  [data/timelines/LICENSE.cactbot](data/timelines/LICENSE.cactbot).
- Libraries shipped in the plugin zip, all under the MIT License:
  - **YamlDotNet** (`YamlDotNet.dll`), copyright Antoine Aubry and contributors;
  - **Open XML SDK** (`DocumentFormat.OpenXml.dll`, `DocumentFormat.OpenXml.Framework.dll`), copyright
    Microsoft Corporation;
  - **System.IO.Packaging** (`System.IO.Packaging.dll`, part of .NET), copyright Microsoft Corporation;
  - **ExcelNumberFormat** (`ExcelNumberFormat.dll`), by the ExcelNumberFormat developers.
- **Dalamud**, **FFXIVClientStructs** and **Lumina** are provided by Dalamud at run time and are not shipped
  with MitCue.
- **Plans:** *Carry the Clear: TOP Healing Plan* and the *LPDU TOP Mitigation Sheet*, by their authors. They
  are converted for the static's own use (the bundled plans) and used as test fixtures.
- **MitCue's own code** doesn't have a license file yet. Ask the author, Noir Yamato, before you reuse it.

FINAL FANTASY XIV © SQUARE ENIX CO., LTD. MitCue is a third-party tool and is not affiliated with or endorsed
by Square Enix.
