# Installing MitCue

There are two ways to install MitCue:

- **A. Custom plugin repository**: the normal way for the static. Dalamud installs MitCue and keeps it
  updated, like any other plugin.
- **B. Dev plugin**: for trying a build that has not been released yet, such as a CI build of a branch or
  your own build. You load the DLL from a folder and update it by hand.

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
https://raw.githubusercontent.com/OWNER/REPO/repo/repo.json
```

> **Maintainer:** replace `OWNER/REPO` with the GitHub account and repository name before you share this page.
> Each release run prints the exact URL in its summary (Actions > Release > the run > Summary). Nothing is
> served from that URL until the first `v*` tag has been released, and the repository must be **public**,
> because Dalamud downloads `repo.json` and the plugin zip without signing in.

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

Dalamud reads the repository when the game starts and each time you open `/xlplugins`. A new release can take
a few minutes to show up, because GitHub caches `repo.json` for up to about five minutes.

- To update by hand: `/xlplugins` > **Installed Plugins** > **Update plugins** (at the bottom of the window).
- To update automatically: `/xlsettings` > **Auto-Updates** > **Auto-update all plugins**. The option
  *Auto-update main repository plugins* does **not** cover custom repositories such as this one.

Dalamud only offers an update when the new version number is higher than the installed one, and only from
the repository you installed from. Keep the same repository URL from then on.

### Uninstall

`/xlplugins` > **Installed Plugins** > MitCue: switch the plugin off, then click the trash-can button
(**Delete plugin**). Your settings, imported plans and recordings stay on disk (see [Where MitCue keeps its
files](#where-mitcue-keeps-its-files)). To remove them as well, delete them yourself once the plugin is gone:
the file `pluginConfigs\MitCue.json` and the folder `pluginConfigs\MitCue\`.

**Reset plugin data and reload** (right-click MitCue in **Installed Plugins**) deletes the same files, but if
MitCue is switched on at the time, Dalamud loads it again straight away, and switching it off afterwards
saves a default `MitCue.json`. That file and an empty `MitCue` folder then stay behind and still need
deleting by hand.

---

## B. Dev plugin (test builds)

### 1. Get a build

Choose one of these:

- **A CI build**: on GitHub, open **Actions** > **CI** > the run for the commit or branch you want >
  **Artifacts** > **MitCue-plugin**. You need to be signed in to GitHub to download artifacts. The download
  contains `latest.zip` and `MitCue.json`.
- **A release**: download `latest.zip` from the release page.
- **Your own build**: run `dotnet build MitCue.slnx -c Release` (see
  [Building from source](../README.md#building-from-source)). The files are in `src/MitCue/bin/Release/MitCue/`.

### 2. Unpack it into a folder you keep

Unzip **`latest.zip`** into a folder of its own that you will keep, for example `C:\FFXIV\MitCue-dev\`.
Afterwards the folder must contain at least:

```
MitCue.dll          the plugin
MitCue.json         its manifest (Dalamud needs it next to the DLL)
MitCue.Core.dll     MitCue's core library (fight data and timelines are inside it)
YamlDotNet.dll      a library MitCue needs
MitCue.deps.json
```

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

## Where MitCue keeps its files

On Windows, Dalamud's folder is `%AppData%\XIVLauncher\`. MitCue's settings window shows the full paths: the
recordings folder in **General** and every path in the **Debug** tab.

| What | Where |
|------|-------|
| Settings | `%AppData%\XIVLauncher\pluginConfigs\MitCue.json` |
| Your imported plans | `%AppData%\XIVLauncher\pluginConfigs\MitCue\plans\<plan id>.json` |
| Pull recordings (if enabled) | `%AppData%\XIVLauncher\pluginConfigs\MitCue\recordings\<zone id>-<date>-<time>.jsonl` |
| Installed plugin (route A) | `%AppData%\XIVLauncher\installedPlugins\MitCue\<version>\` |

On Linux (XIVLauncher.Core), `%AppData%\XIVLauncher` is `~/.xlcore`. All fight data is inside
`MitCue.Core.dll`, so there is nothing else to install. Bundled plans will be inside it too once they ship;
this version has none (see [Plans and importing](../README.md#plans-and-importing)).

## If something goes wrong

- **MitCue is not in "All Plugins"**: check that the repository row is **Enabled** and that you clicked
  **Save**. Then reopen `/xlplugins`. If it still doesn't appear, open the URL in a browser. You should see a
  JSON list that contains `"InternalName": "MitCue"`. A 404 means the repository is private, or that no
  release has run yet.
- **"outdated" or "incompatible"**: Dalamud's API level changed. MitCue needs a rebuild for the new Dalamud.
- **Installs but doesn't load**: type `/xllog` and look for red lines that mention MitCue. Then send the
  lines and the [debug report](../README.md#troubleshooting).
- **Dev plugin: "File does not exist." / "Manifest does not exist."**: the path must be the full path to
  `MitCue.dll`, and `MitCue.json` must be in the same folder.
