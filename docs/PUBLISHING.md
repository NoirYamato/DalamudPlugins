# Publishing MitCue

MitCue is published through the same custom Dalamud plugin repository as NoirMech, so players add one URL
and get both plugins:

```
https://raw.githubusercontent.com/NoirYamato/DalamudPlugins/main/repo.json
```

## Branch layout

Everything lives in the public GitHub repository **NoirYamato/DalamudPlugins**. It must stay public: Dalamud
downloads `repo.json` and the plugin zips without signing in.

| Branch | What it holds |
|--------|---------------|
| `main` | The plugin repository that Dalamud reads: `repo.json`, `plugins/NoirMech/`, `plugins/MitCue/`, `icons/` (`noirmech.png`, `mitcue.png`), the publish scripts `publish.ps1` (NoirMech) and `publish-mitcue.ps1` (MitCue), the README and `LICENSE.md` (NoirMech's AGPL licence). |
| `mitcue` | MitCue's source: this repository, with its own history, which `main` doesn't share. |
| `noirmech-stable` | NoirMech's source, which `publish.ps1` builds. |

`plugins/MitCue/` holds the last two builds, `MitCue-<version>.zip` and `MitCue-<version>-source.zip` (the exact
source each was built from), and `MitCue.json`, the latest build's manifest. `repo.json` has one entry per
plugin. Each publish script replaces only its own plugin's entry, in place, and leaves the other entries and
their order alone.

CI (`.github/workflows/ci.yml`) builds and tests every push to `mitcue` and every pull request that targets it.
CI doesn't publish anything. Only `publish-mitcue.ps1` does, from the maintainer's PC.

## One-time setup

On the maintainer's Windows PC, next to the existing DalamudPlugins clone, make a worktree of that clone on
`mitcue`, so the MitCue source sits in the sibling folder `MitCue`:

```powershell
git -C <DalamudPlugins clone> fetch origin
git -C <DalamudPlugins clone> worktree add ..\MitCue mitcue
```

`worktree add` creates a local `mitcue` branch that tracks `origin/mitcue`. In a clone made with
`--single-branch`, run `git -C <DalamudPlugins clone> fetch origin mitcue:mitcue` first, and publish with
`-Fetch`.

Building needs the .NET 10 SDK and Dalamud's developer files, which XIVLauncher installs (see
[README > Building from source](../README.md#building-from-source)). The identity scan reads the same terms file
as NoirMech's, `%APPDATA%\NoirMech\identity-terms.txt`, one term per line.

## Changing MitCue

Work in `..\MitCue`, commit, and `git push origin mitcue`. The version is `<Version>` in
`src/MitCue/MitCue.csproj`, written as MAJOR.MINOR.PATCH (`0.1.0`). Raise it for a release that deserves a new
number. The publish script adds a fourth part that goes up by one on every publish, so each publish reaches
players as an update.

## Publishing a build

From the DalamudPlugins folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\publish-mitcue.ps1 -Changelog "What changed" -DryRun
powershell -ExecutionPolicy Bypass -File .\publish-mitcue.ps1 -Changelog "What changed"
```

The plugin installer shows the changelog. In order, the script:

1. refuses without `-Changelog`, or when the terms file is missing or lists no terms;
2. refuses unless the DalamudPlugins folder is on `main` with nothing uncommitted or untracked and holds
   `icons/mitcue.png`;
3. refuses unless the MitCue checkout (`-Source`, by default `..\MitCue`) is on `mitcue` with a clean tree and
   its commit is in the pushed `mitcue`. The remote is the one whose URL points at NoirYamato/DalamudPlugins
   (`origin` in the worktree; `-Remote` names another). `-Fetch` fetches it first;
4. works out the version: `<Version>` plus the fourth part, 1 more than the listed MitCue build's when the first
   three parts match, otherwise 1. It refuses a version that isn't higher than the listed one, which Dalamud
   would never offer as an update;
5. runs `dotnet test MitCue.slnx -c Release` and refuses if anything fails;
6. builds `src/MitCue/MitCue.csproj` in Release from scratch, with that version and `ContinuousIntegrationBuild`,
   which maps source paths to `/_/`, so no local folder ends up in the DLLs. DalamudPackager writes
   `bin/Release/MitCue/latest.zip` and `MitCue.json`. The script checks the manifest's name, version and
   `IconUrl`;
7. zips the exact source with `git archive` (`MitCue-<version>-source/` inside);
8. scans both zips for the identity terms, in any case, as ASCII, UTF-8 and UTF-16 text. The scan covers
   entry names, the DLLs' embedded PDBs, and the files inside zip-based files such as the `.xlsx` test
   fixtures. It refuses if a term turns up. Each problem names the term by its line in the terms file, never by
   its text. Control strings that every build contains must all be found, which shows that each part of the
   scan works (see below);
9. stops here with `-DryRun`;
10. copies the build into `plugins/MitCue/`, keeps the last two builds (raw.githubusercontent.com can serve a
    cached `repo.json` for a few minutes, and its download link must keep working), and replaces MitCue's entry
    in `repo.json` (appending it the first time). The entry has the same fields, in the same order, as
    NoirMech's;
11. commits only `plugins/MitCue` and `repo.json`, as `Publish MitCue <version>` with
    `Built from mitcue @ <commit>`, and pushes `main` to `origin`, unless `-NoPush` is given.

On Windows it runs at Idle priority on the `-Affinity` CPUs (`0xF0` by default), the tests and the build
included, like `publish.ps1`. Run it as shown above: started through `cmd /c start /wait`, its exit code is
lost. With PowerShell 7 on Linux or macOS it works too, for testing, with two differences: there is no
`%APPDATA%`, so pass `-TermsFile`, and it runs at normal priority on every CPU.

| Option | Meaning |
|--------|---------|
| `-Changelog "..."` | Required. The installer shows it |
| `-DryRun` | Every gate, the tests, the build and the scan; nothing is copied, committed or pushed |
| `-NoPush` | Everything except the push |
| `-Fetch` | Fetch the remote's `mitcue` before checking that the commit is pushed |
| `-Source <dir>` | The MitCue checkout (default: `..\MitCue`) |
| `-Remote <name>` | The remote that points at NoirYamato/DalamudPlugins (default: the one remote whose URL does) |
| `-TermsFile <file>` | The identity terms (default: `%APPDATA%\NoirMech\identity-terms.txt`) |
| `-Affinity <mask>` | CPU mask on Windows (default `0xF0`) |

### The scan's control strings

`publish-mitcue.ps1` lists them in `$Controls`. Each was checked against a real build, and each fails when the
part of the scan it covers reads nothing:

| Where | Text | What it proves |
|-------|------|----------------|
| `ExcelNumberFormat.dll` | `D:\A\EXCELNUMBERFORMAT\` | A dependency is read, and matching ignores case (the DLL holds `D:\a\ExcelNumberFormat\`) |
| `MitCue.dll` | `/mitcue preview` (UTF-16) | The plugin's UTF-16 strings are read |
| `MitCue.Core.dll` | `/_/src/MitCue.Core/obj/` | The build kept `ContinuousIntegrationBuild`; without it the local folder is there instead |
| `MitCue.dll` embedded PDB | `OverlayWindow.cs` | The embedded PDB is unpacked and read |
| entry name | `MitCue.dll` | Entry names are read |
| source `src/MitCue/Plugin.cs` | `class Plugin : IDalamudPlugin` | The source zip is read |
| source `src/MitCue/Import/GoogleLinks.cs` | `"…" + id[^5..]` (UTF-8) | Non-ASCII UTF-8 text is read |
| source `tests/fixtures/sheets/lpdu/export/lpdu.xlsx` > `xl/workbook.xml` | `name="General Tank Mit til P5"` | Files inside a zip in the zip are read |

When a dependency update or a code change removes one of these strings, the publish refuses. Check that the
string really is gone for a harmless reason, then pick a new one from a real build.

## NoirMech

`publish.ps1` publishes NoirMech from its own checkout (`..\Anomech`, branch `noirmech-stable`) in the same
way. The two scripts don't depend on each other: each replaces only its own entry in `repo.json` and commits
only its own `plugins/<Name>` folder and `repo.json`.

## Retired: tag releases and the `repo` branch

Before MitCue moved into DalamudPlugins, it was meant to ship from its own GitHub repository: a pushed `v*` tag
ran `release.yml`, which made a GitHub release and wrote `repo.json` to a `repo` branch. That workflow is gone.
Pushing a tag no longer publishes anything, and no `repo` branch is used. `tools/make-repo-json.py` and
`tools/RepoJsonCheck` stay: CI uses them to check the plugin zip and a sample `repo.json` against Dalamud's own
manifest types. RepoJsonCheck can also check the published file:

```sh
dotnet run --project tools/RepoJsonCheck -- <DalamudPlugins>/repo.json \
  --zip <DalamudPlugins>/plugins/MitCue/MitCue-<version>.zip --internal-name MitCue
```
