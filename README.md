# DalamudPlugins

Custom Dalamud plugin repository for **NoirMech**, a modified version of
[AnoMech](https://github.com/anomek/AnoMech) by Anomek that adds an EU **LPDU** Party Synergy strat and a pause.
Like AnoMech, it simulates FFXIV ultimate-raid mechanics client-side so you can practice them in an inn.

## Install

1. In game, type `/xlsettings`, open the **Experimental** tab, and under **Custom Plugin Repositories** paste:
   ```
   https://raw.githubusercontent.com/NoirYamato/DalamudPlugins/main/repo.json
   ```
   Click **+**, make sure it's ticked, then **Save and Close**.
2. Type `/xlplugins`, search for **NoirMech** and click **Install**.
3. If you also have **AnoMech** installed, disable it. The two hook the same parts of the game and must
   never run at the same time.
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
  [Monitors](https://ff14.toolboxgaming.space/?id=736282987145761&preview=1).
- **TOP P3 Intermission:** a new scenario with an `[LPDU] Light PF` strat under the **EU** button, following
  the [LPDU P3 Intermission raidplan](https://raidplan.io/plan/46r495ZATq3kCqz1). Its settings have an
  **Automarkers** option: On (the default) marks the spreads, stacks and their partners in conga order; Off
  leaves you to read your own debuff.
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

## Publishing (maintainer)

From this folder, with the AnoMech checkout next to it on the `noirmech-stable` branch (the official
AnoMech release plus NoirMech's changes):

```powershell
powershell -ExecutionPolicy Bypass -File .\publish.ps1 -Changelog "What changed"
```
