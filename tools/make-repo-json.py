#!/usr/bin/env python3
"""Write the Dalamud custom plugin repository file (repo.json, a "pluginmaster") for MitCue.

Dalamud downloads the file from the URL a user adds under /xlsettings > Experimental >
Custom Plugin Repositories and reads it with Newtonsoft.Json as List<RemotePluginManifest>
(Dalamud 15: Dalamud/Plugin/Internal/Types/PluginRepository.cs). This script copies the
plugin's identity and metadata from the manifest DalamudPackager writes next to latest.zip
(src/MitCue/bin/Release/MitCue/MitCue.json) and adds what only the repository knows: the
download links, LastUpdate and the testing fields.

Every entry it writes has exactly the JSON properties of Dalamud 15's RemotePluginManifest
(docs/research/dalamud-api.md, fact 62), in a fixed order. tools/RepoJsonCheck validates the
result with Dalamud's own type.

Rules taken from the Dalamud 15.0.4.0 source:
  * The installer accepts an entry only when Name, InternalName and AssemblyVersion are set.
  * Install AND update both download DownloadLinkInstall (DownloadLinkTesting for testers);
    DownloadLinkUpdate is not read, but it is set to the same URL anyway.
  * After downloading, Dalamud reads <InternalName>.json from the zip and refuses the plugin
    unless its AssemblyVersion equals the repository's as a System.Version ("1.2.3.0" is not
    "1.2.3"), so AssemblyVersion is copied verbatim from the built manifest.
  * Updates are offered only when DalamudApiLevel equals Dalamud's API level (15) and the
    new AssemblyVersion is greater than the installed one.

So with --previous (the repo.json being replaced), a lower version is refused, and so is the
same version unless it is the same build: the same download link (release) and the same git
commit. Otherwise players who installed the listed build would never be offered the new one,
for example after tagging v1.3.0 when v1.3 (also 1.3.0.0) is listed, or after moving a tag to
another commit. The commit of each listed build is kept in a publish record next to repo.json
(published.json on the 'repo' branch): --record-output writes it, --previous-record reads it.

Example (this is what .github/workflows/release.yml runs):

  python3 tools/make-repo-json.py \\
      --manifest src/MitCue/bin/Release/MitCue/MitCue.json \\
      --download-url https://github.com/OWNER/REPO/releases/download/v1.2.3/latest.zip \\
      --repo-url https://github.com/OWNER/REPO \\
      --expect-version 1.2.3 --require-api-level 15 --commit "$(git rev-parse HEAD)" \\
      --previous old/repo.json --previous-record old/published.json \\
      --output repo.json --record-output published.json

Only the Python standard library is used (Python 3.8 or newer).
"""

from __future__ import annotations

import argparse
import datetime as _dt
import json
import os
import re
import sys
import time
import zipfile
from typing import Any, Dict, List, Optional, Sequence, Tuple
from urllib.parse import urlsplit

# The JSON properties of Dalamud 15's RemotePluginManifest (PluginManifest + TestingChangelog),
# minus SourceRepo, which is [JsonIgnore]. Order is the order written to repo.json.
REMOTE_MANIFEST_FIELDS: Tuple[str, ...] = (
    "Author",
    "Name",
    "InternalName",
    "AssemblyVersion",
    "Punchline",
    "Description",
    "Changelog",
    "Tags",
    "CategoryTags",
    "RepoUrl",
    "IconUrl",
    "ImageUrls",
    "ApplicableVersion",
    "MinimumDalamudVersion",
    "DalamudApiLevel",
    "TestingAssemblyVersion",
    "TestingDalamudApiLevel",
    "IsTestingExclusive",
    "TestingChangelog",
    "DownloadLinkInstall",
    "DownloadLinkUpdate",
    "DownloadLinkTesting",
    "DownloadCount",
    "LastUpdate",
    "LoadRequiredState",
    "LoadSync",
    "LoadPriority",
    "CanUnloadAsync",
    "SupportsProfiles",
    "AcceptsFeedback",
    "FeedbackMessage",
    "IsHide",
    "_Dip17Channel",
)

# Keys DalamudPackager 15 may write into the plugin manifest. They are copied when present;
# anything else in the manifest is reported and left out.
PACKAGER_FIELDS = frozenset(
    {
        "Author",
        "Name",
        "InternalName",
        "AssemblyVersion",
        "Description",
        "ApplicableVersion",
        "RepoUrl",
        "Tags",
        "CategoryTags",
        "DalamudApiLevel",
        "LoadRequiredState",
        "LoadSync",
        "CanUnloadAsync",
        "LoadPriority",
        "ImageUrls",
        "IconUrl",
        "Punchline",
        "Changelog",
        "AcceptsFeedback",
        "FeedbackMessage",
        "MinimumDalamudVersion",
    }
)

_VERSION_RE = re.compile(r"^\d+(\.\d+){1,3}$")
_COMMIT_RE = re.compile(r"^[0-9a-f]{7,64}$")
_INT32_MAX = 2**31 - 1


class RepoJsonError(Exception):
    """A problem that makes the output unusable; reported as 'error: ...' with exit code 1."""


def parse_version(text: Any, what: str = "version") -> Tuple[int, int, int, int]:
    """Parses like System.Version: 2 to 4 numeric parts; a missing build/revision is -1.

    The -1 matters: .NET compares Versions part by part with -1 for missing parts, so
    1.2.3 != 1.2.3.0 and 1.2.3 < 1.2.3.0, exactly like Dalamud does.
    """
    if not isinstance(text, str) or not _VERSION_RE.match(text):
        raise RepoJsonError(f"{what} {text!r} is not a version like 1.2.3.4")
    parts = [int(p) for p in text.split(".")]
    if any(p > _INT32_MAX for p in parts):
        raise RepoJsonError(f"{what} {text!r} has a part larger than {_INT32_MAX}")
    while len(parts) < 4:
        parts.append(-1)
    return parts[0], parts[1], parts[2], parts[3]


def padded_version(text: str, what: str = "version") -> Tuple[int, int, int, int]:
    """Like parse_version, but missing parts count as 0 (1.2.3 == 1.2.3.0)."""
    return tuple(max(p, 0) for p in parse_version(text, what))  # type: ignore[return-value]


def check_url(url: Any, what: str, warnings: List[str]) -> str:
    """Returns the URL when it is an absolute http(s) URL; raises otherwise."""
    if not isinstance(url, str) or not url.strip():
        raise RepoJsonError(f"{what} is empty")
    url = url.strip()
    parts = urlsplit(url)
    if parts.scheme not in ("https", "http") or not parts.netloc:
        raise RepoJsonError(f"{what} {url!r} is not an absolute http(s) URL")
    if parts.scheme == "http":
        warnings.append(f"{what} uses plain http: {url}")
    return url


def parse_last_update(text: Optional[str]) -> int:
    """Unix seconds from an integer string or an ISO 8601 time; now when None."""
    if text is None:
        return int(time.time())
    text = text.strip()
    if re.fullmatch(r"\d+", text):
        return int(text)
    iso = text[:-1] + "+00:00" if text.endswith(("Z", "z")) else text
    try:
        moment = _dt.datetime.fromisoformat(iso)
    except ValueError as e:
        raise RepoJsonError(f"--last-update {text!r} is neither unix seconds nor an ISO 8601 time") from e
    if moment.tzinfo is None:
        moment = moment.replace(tzinfo=_dt.timezone.utc)
    return int(moment.timestamp())


def _read_json_text(text: str, source: str) -> Any:
    try:
        return json.loads(text)
    except json.JSONDecodeError as e:
        raise RepoJsonError(f"{source} is not valid JSON: {e}") from e


def _manifest_from_zip(path: str) -> Tuple[Dict[str, Any], str]:
    """Finds <X>.json next to <X>.dll at the root of a DalamudPackager zip."""
    try:
        with zipfile.ZipFile(path) as zf:
            names = set(zf.namelist())
            found: List[Tuple[str, Dict[str, Any]]] = []
            for name in sorted(names):
                if "/" in name or not name.lower().endswith(".json"):
                    continue
                stem = name[: -len(".json")]
                if stem.lower().endswith((".deps", ".runtimeconfig")) or f"{stem}.dll" not in names:
                    continue
                data = _read_json_text(zf.read(name).decode("utf-8-sig"), f"{path}!{name}")
                if isinstance(data, dict) and "InternalName" in data:
                    found.append((name, data))
    except (OSError, zipfile.BadZipFile) as e:
        raise RepoJsonError(f"cannot read {path}: {e}") from e
    if len(found) != 1:
        listed = ", ".join(n for n, _ in found) or "none"
        raise RepoJsonError(f"{path} must contain exactly one plugin manifest next to its DLL (found: {listed})")
    name, data = found[0]
    return data, f"{path}!{name}"


def load_manifest(path: str) -> Tuple[Dict[str, Any], str]:
    """Reads the DalamudPackager manifest from a .json file or from inside latest.zip."""
    if path.lower().endswith(".zip"):
        return _manifest_from_zip(path)
    try:
        with open(path, "r", encoding="utf-8-sig") as f:
            data = _read_json_text(f.read(), path)
    except OSError as e:
        raise RepoJsonError(f"cannot read the manifest {path}: {e}") from e
    if not isinstance(data, dict):
        raise RepoJsonError(f"{path} is not a plugin manifest (expected a JSON object)")
    return data, path


def _string(manifest: Dict[str, Any], key: str, source: str, required: bool = False) -> Optional[str]:
    value = manifest.get(key)
    if value is None:
        if required:
            raise RepoJsonError(f"{source} has no {key}")
        return None
    if not isinstance(value, str):
        raise RepoJsonError(f"{source}: {key} must be a string, not {type(value).__name__}")
    if required and not value.strip():
        raise RepoJsonError(f"{source}: {key} is empty")
    return value


def _string_list(manifest: Dict[str, Any], key: str, source: str) -> List[str]:
    value = manifest.get(key)
    if value is None:
        return []
    if not isinstance(value, list) or not all(isinstance(v, str) for v in value):
        raise RepoJsonError(f"{source}: {key} must be a list of strings")
    return list(value)


def _int(manifest: Dict[str, Any], key: str, source: str, default: int) -> int:
    value = manifest.get(key, default)
    if value is None:
        return default
    if isinstance(value, bool) or not isinstance(value, int):
        raise RepoJsonError(f"{source}: {key} must be an integer")
    if not -(2**31) <= value <= _INT32_MAX:
        raise RepoJsonError(f"{source}: {key} is out of range")
    return value


def _bool(manifest: Dict[str, Any], key: str, source: str, default: bool) -> bool:
    value = manifest.get(key, default)
    if value is None:
        return default
    if not isinstance(value, bool):
        raise RepoJsonError(f"{source}: {key} must be true or false")
    return value


def build_entry(
    manifest: Dict[str, Any],
    source: str,
    *,
    download_url: str,
    update_url: Optional[str] = None,
    testing_url: Optional[str] = None,
    last_update: int,
    changelog: Optional[str] = None,
    repo_url: Optional[str] = None,
    icon_url: Optional[str] = None,
    image_urls: Optional[Sequence[str]] = None,
    expect_version: Optional[str] = None,
    require_api_level: Optional[int] = None,
    warnings: Optional[List[str]] = None,
) -> Dict[str, Any]:
    """Builds one RemotePluginManifest object (an ordered dict with every field)."""
    warnings = warnings if warnings is not None else []

    unknown = sorted(k for k in manifest if k not in PACKAGER_FIELDS)
    if unknown:
        warnings.append(f"{source}: ignored keys that are not repository fields: {', '.join(unknown)}")

    name = _string(manifest, "Name", source, required=True)
    internal_name = _string(manifest, "InternalName", source, required=True)
    assembly_version = _string(manifest, "AssemblyVersion", source, required=True)
    parse_version(assembly_version, f"{source}: AssemblyVersion")
    for key in ("Author", "Punchline", "Description"):
        if not (_string(manifest, key, source) or "").strip():
            warnings.append(f"{source}: {key} is empty (the plugin installer shows it)")

    if expect_version is not None and padded_version(assembly_version) != padded_version(expect_version, "--expect-version"):
        raise RepoJsonError(
            f"{source} has AssemblyVersion {assembly_version}, expected {expect_version} "
            "(was the plugin built with -p:Version=...?)"
        )

    if "DalamudApiLevel" not in manifest:
        raise RepoJsonError(f"{source} has no DalamudApiLevel")
    api_level = _int(manifest, "DalamudApiLevel", source, 0)
    if require_api_level is not None and api_level != require_api_level:
        raise RepoJsonError(
            f"{source} targets Dalamud API level {api_level}, but {require_api_level} is required; "
            "Dalamud only offers installs and updates for its own API level"
        )

    applicable = _string(manifest, "ApplicableVersion", source) or "any"
    if applicable.lower() != "any":
        warnings.append(f"ApplicableVersion is {applicable!r}; Dalamud hides the plugin once the game is newer")

    minimum_dalamud = _string(manifest, "MinimumDalamudVersion", source)
    if minimum_dalamud is not None:
        parse_version(minimum_dalamud, f"{source}: MinimumDalamudVersion")

    install = check_url(download_url, "--download-url", warnings)
    update = check_url(update_url, "--update-url", warnings) if update_url else install
    testing = check_url(testing_url, "--testing-url", warnings) if testing_url else install

    entry_repo_url = check_url(repo_url, "--repo-url", warnings) if repo_url else _string(manifest, "RepoUrl", source)
    entry_icon_url = check_url(icon_url, "--icon-url", warnings) if icon_url else _string(manifest, "IconUrl", source)
    if image_urls:
        entry_image_urls = [check_url(u, "--image-url", warnings) for u in image_urls]
    else:
        entry_image_urls = _string_list(manifest, "ImageUrls", source)

    if last_update <= 0:
        raise RepoJsonError("LastUpdate must be a positive unix time")

    values: Dict[str, Any] = {
        "Author": _string(manifest, "Author", source),
        "Name": name,
        "InternalName": internal_name,
        "AssemblyVersion": assembly_version,
        "Punchline": _string(manifest, "Punchline", source),
        "Description": _string(manifest, "Description", source),
        "Changelog": changelog if changelog is not None else _string(manifest, "Changelog", source),
        "Tags": _string_list(manifest, "Tags", source),
        "CategoryTags": _string_list(manifest, "CategoryTags", source),
        "RepoUrl": entry_repo_url,
        "IconUrl": entry_icon_url,
        "ImageUrls": entry_image_urls,
        "ApplicableVersion": applicable,
        "MinimumDalamudVersion": minimum_dalamud,
        "DalamudApiLevel": api_level,
        # No testing channel: testers simply get the normal version.
        "TestingAssemblyVersion": None,
        "TestingDalamudApiLevel": None,
        "IsTestingExclusive": False,
        "TestingChangelog": None,
        "DownloadLinkInstall": install,
        "DownloadLinkUpdate": update,
        "DownloadLinkTesting": testing,
        "DownloadCount": 0,
        "LastUpdate": last_update,
        "LoadRequiredState": _int(manifest, "LoadRequiredState", source, 0),
        "LoadSync": _bool(manifest, "LoadSync", source, False),
        "LoadPriority": _int(manifest, "LoadPriority", source, 0),
        "CanUnloadAsync": _bool(manifest, "CanUnloadAsync", source, False),
        "SupportsProfiles": _bool(manifest, "SupportsProfiles", source, True),
        "AcceptsFeedback": _bool(manifest, "AcceptsFeedback", source, True),
        "FeedbackMessage": _string(manifest, "FeedbackMessage", source),
        "IsHide": False,
        "_Dip17Channel": None,
    }
    if values["LoadRequiredState"] not in (0, 1, 2):
        raise RepoJsonError(f"{source}: LoadRequiredState must be 0, 1 or 2")
    return {key: values[key] for key in REMOTE_MANIFEST_FIELDS}


def check_commit(commit: Optional[str]) -> Optional[str]:
    """A git commit id (7 to 64 hex digits), lower-cased; None stays None."""
    if commit is None:
        return None
    value = commit.strip().lower()
    if not _COMMIT_RE.match(value):
        raise RepoJsonError(f"--commit {commit!r} is not a git commit id")
    return value


def load_record(data: Any, source: str) -> Dict[str, Any]:
    """Checks that a publish record is a JSON object: InternalName -> {AssemblyVersion, DownloadLinkInstall, Commit}.

    The entries themselves are checked where they are used: one that does not match the listed build
    counts as unknown, which only matters when the same version is published again.
    """
    if not isinstance(data, dict):
        raise RepoJsonError(f"{source} is not a publish record (expected a JSON object)")
    return data


def updated_record(record: Optional[Dict[str, Any]], entry: Dict[str, Any], commit: str) -> Dict[str, Any]:
    """The publish record for the new repo.json: this plugin's build replaces its old one, others are kept."""
    key = entry["InternalName"].lower()
    result = {name: value for name, value in (record or {}).items() if name.lower() != key}
    result[entry["InternalName"]] = {
        "AssemblyVersion": entry["AssemblyVersion"],
        "DownloadLinkInstall": entry["DownloadLinkInstall"],
        "Commit": commit,
    }
    return result


def _same_commit(a: str, b: str) -> bool:
    """True when two commit ids name the same commit (either may be abbreviated)."""
    a, b = a.lower(), b.lower()
    return len(min(a, b, key=len)) >= 7 and (a.startswith(b) or b.startswith(a))


def _same_version_problem(
    entry: Dict[str, Any], old: Dict[str, Any], commit: Optional[str], record: Optional[Dict[str, Any]]
) -> Optional[str]:
    """None when publishing `entry` over the listed `old` (same version) is a re-run of the same build.

    Dalamud offers an update only for a higher version, so a different build under the listed version
    reaches new installs only: the players who already have that version keep the old build.
    """
    name, version = old["InternalName"], old["AssemblyVersion"]
    old_link = old.get("DownloadLinkInstall")
    if old_link != entry["DownloadLinkInstall"]:
        return f"the repository already lists {name} {version} from {old_link}, a different release"
    if commit is None:
        return f"the repository already lists {name} {version}, and without --commit this build cannot be checked to be the same one"
    recorded = None
    for key, value in (record or {}).items():
        if key.lower() == name.lower():
            recorded = value
    if (
        not isinstance(recorded, dict)
        or not isinstance(recorded.get("Commit"), str)
        or recorded.get("DownloadLinkInstall") != old_link
        or not isinstance(recorded.get("AssemblyVersion"), str)
        or not _VERSION_RE.match(recorded["AssemblyVersion"])
        or parse_version(recorded["AssemblyVersion"]) != parse_version(version)
    ):
        return f"the repository already lists {name} {version}, and --previous-record does not say which commit it was built from"
    if not _same_commit(recorded["Commit"], commit):
        return (
            f"the repository already lists {name} {version} built from commit {recorded['Commit'][:12]}, "
            f"but this build is from commit {commit[:12]} (was the tag moved?)"
        )
    return None


def merge_with_previous(
    entry: Dict[str, Any],
    previous: Optional[List[Any]],
    *,
    allow_downgrade: bool = False,
    allow_same_version: bool = False,
    commit: Optional[str] = None,
    record: Optional[Dict[str, Any]] = None,
    warnings: Optional[List[str]] = None,
) -> List[Dict[str, Any]]:
    """Replaces the entry with the same InternalName in an existing pluginmaster, keeping the others.

    Refuses to publish a lower AssemblyVersion than the one already listed (a re-run of an old
    release would otherwise roll everyone's installer back) unless allow_downgrade is set.
    The same AssemblyVersion is accepted only as a re-run of the listed build: the same download
    link, and the same commit as the publish record says (unless allow_same_version is set).
    """
    warnings = warnings if warnings is not None else []
    if previous is None:
        return [entry]
    if not isinstance(previous, list):
        raise RepoJsonError("--previous is not a pluginmaster (expected a JSON array)")

    result: List[Dict[str, Any]] = []
    placed = False
    key = entry["InternalName"].lower()
    for old in previous:
        if not isinstance(old, dict):
            raise RepoJsonError("--previous contains something that is not a plugin entry")
        old_name = old.get("InternalName")
        if not isinstance(old_name, str) or old_name.lower() != key:
            result.append(old)
            continue
        old_version = old.get("AssemblyVersion")
        try:
            older = parse_version(old_version, "listed AssemblyVersion")
        except RepoJsonError:
            warnings.append(f"--previous lists {old_name} with an unreadable AssemblyVersion {old_version!r}; replacing it")
            older = (-1, -1, -1, -1)
        newer = parse_version(entry["AssemblyVersion"])
        if newer < older:
            if not allow_downgrade:
                raise RepoJsonError(
                    f"the repository already lists {old_name} {old_version}, which is newer than "
                    f"{entry['AssemblyVersion']}; refusing to downgrade (use --allow-downgrade to force)"
                )
            warnings.append(f"downgrading {old_name} from {old_version} to {entry['AssemblyVersion']}")
        elif newer == older:
            problem = _same_version_problem(entry, old, commit, record)
            if problem is not None:
                if not allow_same_version:
                    raise RepoJsonError(
                        f"{problem}. Dalamud offers an update only when the version goes up, so players who "
                        f"installed {old_version} would never get this build: give it a new version number "
                        "(--allow-same-version publishes it anyway)"
                    )
                warnings.append(f"{problem}; publishing it anyway, but existing installs will not update to it")
        if not placed:
            result.append(entry)
            placed = True
    if not placed:
        result.append(entry)
    return result


def render(entries: List[Dict[str, Any]]) -> str:
    """The file content: a JSON array, two-space indent, UTF-8, trailing newline."""
    return json.dumps(entries, indent=2, ensure_ascii=False) + "\n"


def _parse_args(argv: Optional[Sequence[str]]) -> argparse.Namespace:
    p = argparse.ArgumentParser(
        prog="make-repo-json.py",
        description="Write a Dalamud custom plugin repository file (repo.json) from a built plugin manifest.",
        epilog="Exit codes: 0 written, 1 invalid input (nothing written), 2 bad arguments.",
    )
    p.add_argument("--manifest", required=True, metavar="PATH",
                   help="DalamudPackager manifest (bin/Release/<Name>/<Name>.json), or latest.zip to read it from the zip")
    p.add_argument("--download-url", required=True, metavar="URL",
                   help="URL of latest.zip (DownloadLinkInstall; Dalamud 15 uses it for installs and updates)")
    p.add_argument("--update-url", metavar="URL", help="DownloadLinkUpdate (default: the download URL)")
    p.add_argument("--testing-url", metavar="URL", help="DownloadLinkTesting (default: the download URL)")
    p.add_argument("--repo-url", metavar="URL", help="RepoUrl shown in the installer (default: from the manifest)")
    p.add_argument("--icon-url", metavar="URL", help="IconUrl (default: from the manifest)")
    p.add_argument("--image-url", action="append", metavar="URL", help="ImageUrls entry (repeatable; default: from the manifest)")
    group = p.add_mutually_exclusive_group()
    group.add_argument("--changelog", metavar="TEXT", help="Changelog text (default: from the manifest)")
    group.add_argument("--changelog-file", metavar="PATH", help="read the Changelog from a UTF-8 text file")
    p.add_argument("--last-update", metavar="TIME", help="LastUpdate as unix seconds or ISO 8601 (default: now)")
    p.add_argument("--expect-version", metavar="VERSION",
                   help="fail unless the manifest's AssemblyVersion is this version (1.2.3 matches 1.2.3.0)")
    p.add_argument("--require-api-level", type=int, metavar="N",
                   help="fail unless the manifest's DalamudApiLevel is N")
    p.add_argument("--previous", metavar="PATH",
                   help="current repo.json: other plugins in it are kept, a lower version than the listed one is refused, "
                        "and so is the same version unless it is the same build (same download link and --commit)")
    p.add_argument("--previous-record", metavar="PATH",
                   help="the publish record written with --previous (published.json): which commit each listed build is from")
    p.add_argument("--commit", metavar="SHA", help="the git commit the plugin was built from (recorded with --record-output)")
    p.add_argument("--allow-downgrade", action="store_true", help="publish even if --previous lists a newer version")
    p.add_argument("--allow-same-version", action="store_true",
                   help="publish even if --previous lists the same version from another release or commit "
                        "(players who installed it will not be offered this build)")
    p.add_argument("--output", "-o", default="repo.json", metavar="PATH", help="where to write (default: repo.json; '-' = stdout)")
    p.add_argument("--record-output", metavar="PATH",
                   help="also write the publish record for the new repo.json (needs --commit); keep it next to repo.json")
    return p.parse_args(argv)


def _read_json_file(path: str, option: str) -> Any:
    try:
        with open(path, "r", encoding="utf-8-sig") as f:
            return _read_json_text(f.read(), path)
    except OSError as e:
        raise RepoJsonError(f"cannot read {option}: {e}") from e


def _write_text(path: str, text: str) -> None:
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    os.replace(tmp, path)


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = _parse_args(argv)
    warnings: List[str] = []
    try:
        commit = check_commit(args.commit)
        if args.record_output and commit is None:
            raise RepoJsonError("--record-output needs --commit")
        if args.record_output and args.output == "-":
            raise RepoJsonError("--record-output cannot be used with --output -")

        manifest, source = load_manifest(args.manifest)

        changelog = args.changelog
        if args.changelog_file:
            try:
                with open(args.changelog_file, "r", encoding="utf-8-sig") as f:
                    changelog = f.read().strip()
            except OSError as e:
                raise RepoJsonError(f"cannot read --changelog-file: {e}") from e
        if changelog is not None and not changelog.strip():
            changelog = None

        entry = build_entry(
            manifest,
            source,
            download_url=args.download_url,
            update_url=args.update_url,
            testing_url=args.testing_url,
            last_update=parse_last_update(args.last_update),
            changelog=changelog,
            repo_url=args.repo_url,
            icon_url=args.icon_url,
            image_urls=args.image_url,
            expect_version=args.expect_version,
            require_api_level=args.require_api_level,
            warnings=warnings,
        )

        previous = _read_json_file(args.previous, "--previous") if args.previous else None
        record = None
        if args.previous_record:
            record = load_record(_read_json_file(args.previous_record, "--previous-record"), args.previous_record)
        entries = merge_with_previous(
            entry,
            previous,
            allow_downgrade=args.allow_downgrade,
            allow_same_version=args.allow_same_version,
            commit=commit,
            record=record,
            warnings=warnings,
        )
        text = render(entries)
        record_text = render(updated_record(record, entry, commit)) if args.record_output and commit else None
    except RepoJsonError as e:
        for w in warnings:
            print(f"warning: {w}", file=sys.stderr)
        print(f"error: {e}", file=sys.stderr)
        return 1

    for w in warnings:
        print(f"warning: {w}", file=sys.stderr)

    if args.output == "-":
        sys.stdout.write(text)
    else:
        _write_text(args.output, text)
        print(
            f"wrote {args.output}: {entry['Name']} {entry['AssemblyVersion']} (API {entry['DalamudApiLevel']}), "
            f"{len(entries)} plugin(s), LastUpdate {entry['LastUpdate']}",
            file=sys.stderr,
        )
        if record_text is not None:
            _write_text(args.record_output, record_text)
            print(f"wrote {args.record_output}: {entry['Name']} {entry['AssemblyVersion']} built from {commit}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
