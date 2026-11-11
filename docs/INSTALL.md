# Installing MitCue

There are two ways to install MitCue:

- **A. Custom plugin repository**: the normal way for the static. Dalamud installs MitCue and keeps it
  updated, like any other plugin.
- **B. Dev plugin**: for trying a build that has not been published yet, such as a CI build or your own
  build. You load the DLL from a folder and update it by hand.

Use one route at a time. Both routes share the same settings and plans, because Dalamud stores them by the
plugin's internal name (`MitCue`), so you can switch routes without losing anything.

## What you need

- FINAL FANTASY XIV started through **XIVLauncher** with **Dalamud** turned on. On Linux this is
  XIVLauncher.Core, and on macOS it is XIV on Mac.
- A Dalamud release with **API level 15**, which is current at the time of writing. If MitCue appears in the
  installer as *outdated*, Dalamud has moved to a newer API and MitCue needs a new build.
- An English game client works best. Other client languages work too. Actions are matched by their game ID,
  so names and icons appear in your language. The only difference is that the timeline sync cannot use enemy
  names, which cactbot writes in English.

Dalamud's settings window opens with `/xlsettings` and the plugin installer with `/xlplugins`. You can also
open both from the Dalamud menu on the title screen.

---

## A. Custom plugin repository (recommended)

The repository URL is:

```
https://raw.githubusercontent.com/NoirYamato/DalamudPlugins/main/repo.json
```

It is the repository of NoirMech as well: one entry gives you both plugins. **If you already added it for
NoirMech, skip to [Install MitCue](#install-mitcue).**

### Add the repository (once)

1. In game, type `/xlsettings`.
2. Open the **Experimental** tab.
3. Scroll to **Custom Plugin Repositories**.
   - The first time, Dalamud shows a **READ THIS FIRST!** warning about third-party plugins. Read it, wait for
     the countdown, then click **Ok, I have read and understood this warning**.
4. In the empty row at the bottom of the list, paste the URL above, then click the **+** button at the end of
   the row. The new row should have **Enabled** ticked.
5. Click **Save** (the floppy-disk button at the bottom right of the settings window).

### Install MitCue

1. Type `/xlplugins`.
2. Open **All Plugins** and type `MitCue` in the **Search** box.
3. Click **Install v…**. The plugin details say *From custom plugin repository https://raw.githubusercontent.com/…*.
4. MitCue loads straight away. Type `/mitcue` to open its settings. Then continue with the
   [quick start](../README.md#quick-start).

### Updates

Dalamud reads the repository when the game starts and each time you open `/xlplugins`. A new build can take
a few minutes to show up, because GitHub caches `repo.json` for up to about five minutes.

- To update by hand: `/xlplugins` > **Installed Plugins** > **Update plugins** (at the bottom of the window).
- To update automatically: `/xlsettings` > **Auto-Updates** > **Auto-update all plugins**. The option
  *Auto-update main repository plugins* does **not** cover custom repositories such as this one.

Dalamud only offers an update when the new version number is higher than the installed one, and only from
the repository you installed from.

**Keep the repository row exactly as it is and enabled.** Dalamud loads a plugin installed from a custom
repository only while that repository is in the list, enabled, with the same URL. If the row is removed,
disabled or edited, MitCue (and NoirMech) stop loading until the row is back as it was.

### Uninstall

`/xlplugins` > **Installed Plugins** > MitCue: switch the plugin off, then click the trash-can button
(**Delete plugin**). Your settings, imported plans and recordings stay on disk (see [Where MitCue keeps its
files](#where-mitcue-keeps-its-files)). To remove them as well, delete them yourself once the plugin is gone:
the file `pluginConfigs\MitCue.json` and the folder `pluginConfigs\MitCue\`.

**Reset plugin data and reload** (right-click MitCue in **Installed Plugins**) deletes the same files, but if
MitCue is switched on at the time, Dalamud loads it again straight away, and switching it off afterwards
saves a default `MitCue.json`. That file and an empty `MitCue` folder then stay behind and still need
deleting by hand.

Uninstalling leaves the repository row in place. Keep it if you use NoirMech.

---

## B. Dev plugin (test builds)

### 1. Get a build

Choose one of these:

- **A CI build**: CI runs in the [NoirYamato/DalamudPlugins](https://github.com/NoirYamato/DalamudPlugins)
  repository for every push to its `mitcue` branch and every pull request into it. On GitHub, open
  **Actions** > **CI** > the run for the commit you want > **Artifacts** > **MitCue-plugin**. You need to be
  signed in to GitHub to download artifacts. The download is a zip that contains the plugin zip
  (`latest.zip`) and `MitCue.json`.
- **A published build**: `MitCue-<version>.zip` in
  [`plugins/MitCue/`](https://github.com/NoirYamato/DalamudPlugins/tree/main/plugins/MitCue) on that
  repository's `main` branch (not `MitCue-<version>-source.zip`, which holds the source code). The last two
  builds are kept there.
- **Your own build**: run `dotnet build MitCue.slnx -c Release` (see
  [Building from source](../README.md#building-from-source)). The files are in `src/MitCue/bin/Release/MitCue/`.

### 2. Unpack it into a folder you keep

Unzip the plugin zip (`MitCue-<version>.zip`, or the `latest.zip` of a CI or your own build) into a folder
of its own that you will keep, for example `C:\FFXIV\MitCue-dev\`. For a CI build, unzip the download first,
then unzip the `latest.zip` inside it into your folder: `MitCue.dll` is only in the plugin zip. Keep every
file of the plugin zip together (see [What the plugin zip contains](#what-the-plugin-zip-contains)): MitCue
needs all of them. Without the Open XML and ExcelNumberFormat DLLs, for example, it cannot read a Google
Sheets link.

Don't point Dalamud at a file inside the zip or inside your Downloads folder. To update later, close the game
(or unload MitCue), then overwrite the files in this folder.

### 3. Tell Dalamud where it is

1. Type `/xlsettings` and open the **Experimental** tab.
2. Tick **Enable Developer Mode**. The **Dev Plugin Locations** section only appears after you tick it.
3. Under **Dev Plugin Locations**, either:
   - click **Select Dev Plugin DLL** and choose `MitCue.dll` in your folder, or
   - paste the full path, e.g. `C:\FFXIV\MitCue-dev\MitCue.dll`, into the empty row and click **+**.

   The row must point at the **DLL**, not at the folder. Leave **Enabled** ticked. If Dalamud says
   *Manifest does not exist.*, then `MitCue.json` is missing from that folder.
4. Click **Save**.

### 4. Load it

1. Type `/xlplugins` and open **Dev Tools** > **Installed Dev Plugins**.
2. Switch MitCue on.
3. Leave the two small buttons on the plugin's row as they are: **Start on boot** (power icon), which loads
   MitCue with the game, and **Automatic reloading** (arrows icon), which reloads it whenever the DLL changes.
   Both are on by default, shown in green. Each click switches one off (red) or on again, so don't click them
   unless you mean to.
4. Type `/mitcue`.

Notes:

- Developer Mode also turns on extra Dalamud checks (*Checks that plugins call game APIs in safe ways*). This
  is useful for testing: if one of these checks catches MitCue, Dalamud shows an error. Please report it, with
  the [debug report](../README.md#troubleshooting).
- If MitCue is installed from the custom repository as well, delete that copy first (route A >
  [Uninstall](#uninstall)), so that only one MitCue is loaded.
- When you are done testing, switch the dev plugin off, remove its row from **Dev Plugin Locations**, and
  install from the custom repository again.

---

## What the plugin zip contains

| File | What it is | License |
|------|------------|---------|
| `MitCue.dll` | the plugin | MitCue's own code (no license file yet) |
| `MitCue.json` | its manifest; Dalamud needs it next to the DLL | |
| `MitCue.deps.json` | the list of libraries MitCue uses | |
| `MitCue.Core.dll` | MitCue's core library. The fight data, cactbot timelines, action data, shorthand aliases and the three bundled TOP plans are built into it | MitCue's own code; timelines: Apache License 2.0, cactbot contributors |
| `MitCue.Core.pdb`, `MitCue.Core.xml` | debug symbols and documentation of the core library | |
| `YamlDotNet.dll` | reads and writes plans and data files (YAML) | MIT |
| `DocumentFormat.OpenXml.dll`, `DocumentFormat.OpenXml.Framework.dll` | Open XML SDK: reads the `.xlsx` export of a Google Sheets link | MIT, © Microsoft Corporation |
| `System.IO.Packaging.dll` | opens `.xlsx` files for the Open XML SDK (part of .NET) | MIT, © Microsoft Corporation |
| `ExcelNumberFormat.dll` | shows numbers and times in sheet cells as the sheet displays them | MIT |

Dalamud's own assemblies (Dalamud, FFXIVClientStructs, Lumina and the rest) are not in the zip: Dalamud
provides them. Credits for all of these: [README > Credits and licenses](../README.md#credits-and-licenses).

## Where MitCue keeps its files

On Windows, Dalamud's folder is `%AppData%\XIVLauncher\`. MitCue's settings window shows the full paths of
the recordings folder (in **General**) and of the plans and recordings folders (in the **Debug** tab).

| What | Where |
|------|-------|
| Settings, including your personal aliases (shorthand remembered in the import review) | `%AppData%\XIVLauncher\pluginConfigs\MitCue.json` |
| Your imported plans | `%AppData%\XIVLauncher\pluginConfigs\MitCue\plans\<plan id>.json` |
| Pull recordings (if enabled) | `%AppData%\XIVLauncher\pluginConfigs\MitCue\recordings\<zone id>-<date>-<time>.jsonl` |
| Installed plugin (route A) | `%AppData%\XIVLauncher\installedPlugins\MitCue\<version>\` |

On Linux (XIVLauncher.Core), `%AppData%\XIVLauncher` is `~/.xlcore`. The fight data and the bundled plans
are inside `MitCue.Core.dll`, so there is nothing else to install.

## If something goes wrong

- **MitCue is not in "All Plugins"**: check that the repository row is **Enabled** and that you clicked
  **Save**. Then reopen `/xlplugins`. If it still doesn't appear, open the URL in a browser: you should see a
  JSON list. Search the page for `MitCue`: it is listed there, next to NoirMech, since version 0.1.0.1.
- **MitCue (and NoirMech) stopped loading**: the repository row was removed, disabled or changed. Put it back
  exactly as above (see [Updates](#updates)).
- **"outdated" or "incompatible"**: Dalamud's API level changed. MitCue needs a rebuild for the new Dalamud.
- **Installs but doesn't load**: type `/xllog` and look for red lines that mention MitCue. Then send the
  lines and the [debug report](../README.md#troubleshooting).
- **Dev plugin: "File does not exist." / "Manifest does not exist."**: the path must be the full path to
  `MitCue.dll`, and `MitCue.json` must be in the same folder.
