# mitcue (developer CLI)

Command-line access to MitCue.Core: fights and timelines, plan import, a player's checklist, simulated pulls, plan
checks and data validation. It never talks to the game.

```sh
dotnet run --project tools/MitCue.Cli -- <command> [arguments] [options]
dotnet run --project tools/MitCue.Cli -- <command> --help
```

Data comes from the repository's `data/` (found above the current or program directory), or from `--data <dir>`;
without either, the copy built into MitCue.Core is used. Plans can be YAML, JSON or share codes (`-` reads stdin).

| Command | What it does |
|---|---|
| `fights` | The fights: key, name, territory, sync level, phases, mechanics, timeline file. |
| `mechanics <fight> [--phase p2] [--kind raidwide] [--find text]` | Phases, calibration presets and mechanics (hit as phase m:ss and timeline seconds, kind, cactbot entry, hits, aliases). |
| `timeline <fight\|file.txt> [--syncs]` | The parsed cactbot timeline: entries with syncs, non-default windows, jumps; labels; parser warnings. |
| `import <source> --fight <key> [...]` | A sheet (.xlsx, fixture folder, .csv/.tsv/.json, or a public Google Sheets link) to plan YAML, with the importer's diagnostics by severity and each phase's fitted zero point and spread. |
| `plan <plan> --job <job> [--slot] [--assignee]` | The player's checklist in press-time order: phase time, offset from the hit (`*` = default timing), action, mechanic, target and notes, flags. |
| `simulate <plan> --job <job> [--push p2=110,...] [--seed n] [--miss a,b] [--from p3] [--wipe t]` | A generated pull through the timeline player and the reminder scheduler, set up like the plugin's encounter session; logs every reminder state change and prints the pull summary. |
| `check <plan> --job <job> [--slot] [--assignee]` | Plan checks (`PlanChecker`): unknown mechanics/actions, actions the job cannot use at the sync level, recast conflicts and tight margins, gauges and follow-ups (heuristic). |
| `validate` | Every data file through the real loaders, with the consistency checks of the data tests; plans in `plans/` too. |
| `share <plan>` / `unshare <code> [-o file]` | Share codes (`MITCUE1:...`). |

Exit codes: 0 ok; 1 problems found (`check` errors, `validate` problems, `import` error diagnostics); 2 wrong command
line; 3 failure (missing or unreadable input, download error, a core part not implemented yet).

Examples:

```sh
mitcue import tests/fixtures/sheets/carry-the-clear --fight top --tabs "TOP – White Mage" \
       --calibration carry_the_clear -o plans/ctc-whm.yaml
mitcue check plans/ctc-whm.yaml --job WHM --slot H1
mitcue simulate plans/ctc-whm.yaml --job WHM --push p2=2:10,p3=5:20 --miss Temperance
```

Notes:

- `simulate` plays only the event types the plugin sees in game (combat start/end, cast starts, resolved boss
  abilities, wipes) unless `--all-events` is given; TOP's P2, for example, is then entered by the 7B40 cast at 203.4
  rather than the MapEffect at 200.0. `--push p2=110` starts P2 110 s (real time) after the pull and ends P1's events
  3 s before; `--from p3` starts at P3 like `/mitcue preview top p3`. The player presses every action at its press
  time except the `--miss` ones.
- `check` compares planned uses on the timeline's nominal times. Within a phase these are exact, so a conflict is an
  error; across a phase transition the real gap depends on when the earlier phase ends, so it is a warning. Gauge and
  follow-up findings are marked `(h)`: they rest on stated assumptions (e.g. Lilies full at the pull, +1 every 20 s).
