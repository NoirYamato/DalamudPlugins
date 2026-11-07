# DalamudPlugins

Custom Dalamud plugin repository for **NoirMech**, a modified version of
[AnoMech](https://github.com/anomek/AnoMech) by Anomek that adds EU **LPDU** strats for TOP P2, P3, P4, P5
Delta and P6, and a pause.
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
  [Monitors](https://ff14.toolboxgaming.space/?id=736282987145761&preview=1). P3 Monitors' settings have an
  **Automarkers** option: On (the default) marks the three monitor players Bind 1-3 and everyone else Attack
  1-5, in line-up order.
- **TOP P3 Intermission:** a new scenario with an `[LPDU] Light PF` strat under the **EU** button, following
  the [LPDU P3 Intermission raidplan](https://raidplan.io/plan/46r495ZATq3kCqz1). Its settings have an
  **Automarkers** option: On (the default) marks the spreads, stacks and their partners in conga order; Off
  leaves you to read your own debuff.
- **TOP P4 Blue Screen:** a new scenario with an `[LPDU] Light PF` strat under the **EU** button, following
  the [LPDU Blue Screen toolbox](https://ff14.toolboxgaming.space/?id=595188367474761&preview=1). Its settings
  can force or rule out your marker in each set, and whether both of a set's markers land in one light party,
  so someone has to flex.
- **TOP P5 Delta:** an `[LPDU] Light PF` strat under the **EU** button, following the
  [LPDU Delta toolbox](https://ff14.toolboxgaming.space/?id=955389449416761&preview=1). It is the default; NA pf
  is still under the **NA** button. In each colour, when only one tether pair has a healer, that pair takes the
  inner spots. Its settings have an **Automarkers** option for both strats: On (the default) puts a triangle on
  the Near World player and a cross (+) on the Distant World player. The eye spawns on any cardinal, not just
  north or south, and in multiplayer everyone's tether, monitor and Hello World choices are honoured more
  reliably.
- **TOP P6:** an `[LPDU] Light PF` strat for Exasquares/WC2 under the **EU** button, following the
  [LPDU P6 raidplan](https://raidplan.io/plan/mh3bX4KJ9d8rT9nk). It is the default; NA pf is still under the
  **NA** button. Four new scenarios, each with an LPDU strat, cover the rest of the phase up to Magic Number:
  Exasquares/Dive (Cosmo Memory to Cosmo Dive 1), Exaflares/WC1, Exaflares/Dive (Unlimited Wave Cannon 2 to
  Cosmo Dive 2) and Cosmo Meteor. Their settings can force which Cosmo Arrow comes first, where the first
  exaflare starts and which way they turn, and who gets a flare. With **Require tank mitigation** on, Cosmo
  Memory needs a tank limit break 3 and the bots use their limit breaks.
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
