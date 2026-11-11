"""Tests for tools/make-repo-json.py (run: python3 -m unittest discover -s tools/tests -v).

The end-to-end check against Dalamud's own types is tools/RepoJsonCheck, which CI runs on the
real Release build; these tests pin the generator's own rules.
"""

from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import os
import tempfile
import unittest
import zipfile

_HERE = os.path.dirname(os.path.abspath(__file__))
_SCRIPT = os.path.join(os.path.dirname(_HERE), "make-repo-json.py")
_spec = importlib.util.spec_from_file_location("make_repo_json", _SCRIPT)
assert _spec is not None and _spec.loader is not None
mrj = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(mrj)

# What DalamudPackager 15 wrote for MitCue 0.1.0.0 (src/MitCue/bin/Release/MitCue/MitCue.json).
MANIFEST = {
    "Author": "MitCue contributors",
    "Name": "MitCue",
    "InternalName": "MitCue",
    "AssemblyVersion": "0.1.0.0",
    "Description": "Shows which of your actions to press next. Reminder-only.",
    "ApplicableVersion": "any",
    "Tags": ["mitigation", "healer"],
    "DalamudApiLevel": 15,
    "LoadRequiredState": 0,
    "LoadSync": False,
    "CanUnloadAsync": False,
    "LoadPriority": 0,
    "Punchline": "Mitigation plan reminders, synced to the fight.",
    "AcceptsFeedback": True,
}

URL = "https://github.com/owner/MitCue/releases/download/v0.1.0/latest.zip"

# docs/research/dalamud-api.md fact 62: the JSON properties of Dalamud 15's RemotePluginManifest.
FACT_62_FIELDS = {
    "Author", "Name", "InternalName", "AssemblyVersion", "Punchline", "Description", "Changelog", "Tags",
    "CategoryTags", "RepoUrl", "IconUrl", "ImageUrls", "ApplicableVersion", "MinimumDalamudVersion",
    "DalamudApiLevel", "TestingAssemblyVersion", "TestingDalamudApiLevel", "IsTestingExclusive",
    "TestingChangelog", "DownloadLinkInstall", "DownloadLinkUpdate", "DownloadLinkTesting", "DownloadCount",
    "LastUpdate", "LoadRequiredState", "LoadSync", "LoadPriority", "CanUnloadAsync", "SupportsProfiles",
    "AcceptsFeedback", "FeedbackMessage", "IsHide", "_Dip17Channel",
}


def build(manifest=None, **kwargs):
    kwargs.setdefault("download_url", URL)
    kwargs.setdefault("last_update", 1_800_000_000)
    return mrj.build_entry(dict(MANIFEST if manifest is None else manifest), "manifest", **kwargs)


class VersionTests(unittest.TestCase):
    def test_parses_like_system_version(self):
        self.assertEqual(mrj.parse_version("1.2"), (1, 2, -1, -1))
        self.assertEqual(mrj.parse_version("1.2.3"), (1, 2, 3, -1))
        self.assertEqual(mrj.parse_version("1.2.3.4"), (1, 2, 3, 4))

    def test_missing_parts_differ_from_zero(self):
        # Dalamud compares System.Version objects: 1.2.3 != 1.2.3.0 and 1.2.3 < 1.2.3.0.
        self.assertNotEqual(mrj.parse_version("1.2.3"), mrj.parse_version("1.2.3.0"))
        self.assertLess(mrj.parse_version("1.2.3"), mrj.parse_version("1.2.3.0"))
        self.assertEqual(mrj.padded_version("1.2.3"), mrj.padded_version("1.2.3.0"))

    def test_rejects_non_versions(self):
        for bad in ("1", "1.2.3.4.5", "v1.2.3", "1.2.x", "", " 1.2", "1.-2", None, 12, "1.2147483648"):
            with self.subTest(bad=bad), self.assertRaises(mrj.RepoJsonError):
                mrj.parse_version(bad)


class EntryTests(unittest.TestCase):
    def test_has_exactly_the_remote_manifest_fields_in_order(self):
        entry = build()
        self.assertEqual(tuple(entry), mrj.REMOTE_MANIFEST_FIELDS)
        self.assertEqual(set(entry), FACT_62_FIELDS)

    def test_copies_identity_verbatim(self):
        entry = build()
        self.assertEqual(entry["Name"], "MitCue")
        self.assertEqual(entry["InternalName"], "MitCue")
        self.assertEqual(entry["AssemblyVersion"], "0.1.0.0")
        self.assertEqual(entry["DalamudApiLevel"], 15)
        self.assertEqual(entry["ApplicableVersion"], "any")
        self.assertEqual(entry["Tags"], ["mitigation", "healer"])
        self.assertEqual(entry["Punchline"], MANIFEST["Punchline"])

    def test_repository_fields(self):
        entry = build()
        self.assertEqual(entry["DownloadLinkInstall"], URL)
        self.assertEqual(entry["DownloadLinkUpdate"], URL)
        self.assertEqual(entry["DownloadLinkTesting"], URL)
        self.assertEqual(entry["LastUpdate"], 1_800_000_000)
        self.assertEqual(entry["DownloadCount"], 0)
        self.assertIs(entry["IsHide"], False)
        self.assertIs(entry["IsTestingExclusive"], False)
        self.assertIsNone(entry["TestingAssemblyVersion"])
        self.assertIsNone(entry["TestingDalamudApiLevel"])
        self.assertIsNone(entry["_Dip17Channel"])
        self.assertIs(entry["SupportsProfiles"], True)
        self.assertEqual(entry["CategoryTags"], [])
        self.assertEqual(entry["ImageUrls"], [])

    def test_overrides(self):
        entry = build(
            update_url="https://example.org/u.zip",
            testing_url="https://example.org/t.zip",
            repo_url="https://example.org/repo",
            icon_url="https://example.org/icon.png",
            image_urls=["https://example.org/1.png"],
            changelog="Fixed things.",
        )
        self.assertEqual(entry["DownloadLinkUpdate"], "https://example.org/u.zip")
        self.assertEqual(entry["DownloadLinkTesting"], "https://example.org/t.zip")
        self.assertEqual(entry["RepoUrl"], "https://example.org/repo")
        self.assertEqual(entry["IconUrl"], "https://example.org/icon.png")
        self.assertEqual(entry["ImageUrls"], ["https://example.org/1.png"])
        self.assertEqual(entry["Changelog"], "Fixed things.")

    def test_expect_version_pads_missing_parts(self):
        build(expect_version="0.1.0")
        build(expect_version="0.1.0.0")
        with self.assertRaisesRegex(mrj.RepoJsonError, "expected 0.2.0"):
            build(expect_version="0.2.0")

    def test_require_api_level(self):
        build(require_api_level=15)
        with self.assertRaisesRegex(mrj.RepoJsonError, "API level 15"):
            build(require_api_level=16)

    def test_required_manifest_fields(self):
        for key in ("Name", "InternalName", "AssemblyVersion", "DalamudApiLevel"):
            manifest = dict(MANIFEST)
            del manifest[key]
            with self.subTest(key=key), self.assertRaises(mrj.RepoJsonError):
                build(manifest)
        with self.assertRaises(mrj.RepoJsonError):
            build(dict(MANIFEST, InternalName="  "))
        with self.assertRaises(mrj.RepoJsonError):
            build(dict(MANIFEST, AssemblyVersion="1.0-beta"))
        with self.assertRaises(mrj.RepoJsonError):
            build(dict(MANIFEST, DalamudApiLevel="15"))

    def test_download_url_must_be_absolute(self):
        for bad in ("latest.zip", "ftp://example.org/latest.zip", "", "https://"):
            with self.subTest(bad=bad), self.assertRaises(mrj.RepoJsonError):
                build(download_url=bad)
        warnings = []
        build(download_url="http://example.org/latest.zip", warnings=warnings)
        self.assertTrue(any("plain http" in w for w in warnings))

    def test_unknown_manifest_keys_are_reported_and_dropped(self):
        warnings = []
        entry = build(dict(MANIFEST, SomethingNew=1), warnings=warnings)
        self.assertNotIn("SomethingNew", entry)
        self.assertTrue(any("SomethingNew" in w for w in warnings))

    def test_pinned_applicable_version_warns(self):
        warnings = []
        build(dict(MANIFEST, ApplicableVersion="2025.01.01.0000.0000"), warnings=warnings)
        self.assertTrue(any("ApplicableVersion" in w for w in warnings))


class MergeTests(unittest.TestCase):
    def test_no_previous(self):
        entry = build()
        self.assertEqual(mrj.merge_with_previous(entry, None), [entry])

    def test_replaces_same_plugin_and_keeps_others(self):
        other = {"InternalName": "OtherPlugin", "AssemblyVersion": "9.0.0.0"}
        old = {"InternalName": "mitcue", "AssemblyVersion": "0.0.9.0"}
        entry = build()
        self.assertEqual(mrj.merge_with_previous(entry, [other, old]), [other, entry])

    def test_same_version_is_allowed(self):
        entry = build()
        self.assertEqual(mrj.merge_with_previous(entry, [dict(entry, LastUpdate=1)]), [entry])

    def test_refuses_downgrade(self):
        entry = build()
        newer = {"InternalName": "MitCue", "AssemblyVersion": "0.2.0.0"}
        with self.assertRaisesRegex(mrj.RepoJsonError, "refusing to downgrade"):
            mrj.merge_with_previous(entry, [newer])
        warnings = []
        self.assertEqual(mrj.merge_with_previous(entry, [newer], allow_downgrade=True, warnings=warnings), [entry])
        self.assertTrue(warnings)

    def test_rejects_non_list(self):
        with self.assertRaises(mrj.RepoJsonError):
            mrj.merge_with_previous(build(), {"InternalName": "MitCue"})


class LastUpdateTests(unittest.TestCase):
    def test_formats(self):
        self.assertEqual(mrj.parse_last_update("1800000000"), 1_800_000_000)
        self.assertEqual(mrj.parse_last_update("2026-11-11T00:00:00Z"), 1_794_355_200)
        self.assertEqual(mrj.parse_last_update("2026-11-11T01:00:00+01:00"), 1_794_355_200)
        self.assertEqual(mrj.parse_last_update("2026-11-11T00:00:00"), 1_794_355_200)
        self.assertGreater(mrj.parse_last_update(None), 1_700_000_000)
        with self.assertRaises(mrj.RepoJsonError):
            mrj.parse_last_update("yesterday")


class CommandLineTests(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.dir.cleanup)
        self.manifest = os.path.join(self.dir.name, "MitCue.json")
        with open(self.manifest, "w", encoding="utf-8") as f:
            json.dump(MANIFEST, f)
        self.out = os.path.join(self.dir.name, "out", "repo.json")

    def run_main(self, *args):
        stderr = io.StringIO()
        with contextlib.redirect_stderr(stderr):
            code = mrj.main(list(args))
        return code, stderr.getvalue()

    def test_writes_a_json_array(self):
        code, log = self.run_main("--manifest", self.manifest, "--download-url", URL, "--last-update", "1800000000",
                                  "--expect-version", "0.1.0", "--require-api-level", "15", "--output", self.out)
        self.assertEqual(code, 0, log)
        with open(self.out, encoding="utf-8") as f:
            text = f.read()
        self.assertTrue(text.endswith("\n"))
        data = json.loads(text)
        self.assertIsInstance(data, list)
        self.assertEqual(len(data), 1)
        self.assertEqual(data[0]["AssemblyVersion"], "0.1.0.0")
        self.assertEqual(data[0]["LastUpdate"], 1_800_000_000)

    def test_reads_the_manifest_from_the_zip(self):
        zip_path = os.path.join(self.dir.name, "latest.zip")
        with zipfile.ZipFile(zip_path, "w") as zf:
            zf.writestr("MitCue.dll", b"MZ")
            zf.writestr("MitCue.json", json.dumps(MANIFEST))
            zf.writestr("MitCue.deps.json", json.dumps({"runtimeTarget": {}}))
            zf.writestr("YamlDotNet.dll", b"MZ")
        code, log = self.run_main("--manifest", zip_path, "--download-url", URL, "--output", self.out)
        self.assertEqual(code, 0, log)
        with open(self.out, encoding="utf-8") as f:
            self.assertEqual(json.load(f)[0]["InternalName"], "MitCue")

    def test_zip_without_manifest_fails(self):
        zip_path = os.path.join(self.dir.name, "latest.zip")
        with zipfile.ZipFile(zip_path, "w") as zf:
            zf.writestr("MitCue.dll", b"MZ")
        code, log = self.run_main("--manifest", zip_path, "--download-url", URL, "--output", self.out)
        self.assertEqual(code, 1)
        self.assertIn("exactly one plugin manifest", log)

    def test_changelog_file_and_previous(self):
        changelog = os.path.join(self.dir.name, "notes.md")
        with open(changelog, "w", encoding="utf-8") as f:
            f.write("First release.\n\n- reminders\n")
        previous = os.path.join(self.dir.name, "previous.json")
        with open(previous, "w", encoding="utf-8") as f:
            json.dump([{"InternalName": "MitCue", "AssemblyVersion": "0.0.1.0"}], f)
        code, log = self.run_main("--manifest", self.manifest, "--download-url", URL, "--changelog-file", changelog,
                                  "--previous", previous, "--output", self.out)
        self.assertEqual(code, 0, log)
        with open(self.out, encoding="utf-8") as f:
            data = json.load(f)
        self.assertEqual(len(data), 1)
        self.assertEqual(data[0]["Changelog"], "First release.\n\n- reminders")

    def test_error_writes_nothing(self):
        code, log = self.run_main("--manifest", self.manifest, "--download-url", URL, "--expect-version", "9.9.9",
                                  "--output", self.out)
        self.assertEqual(code, 1)
        self.assertIn("error:", log)
        self.assertFalse(os.path.exists(self.out))

    def test_missing_manifest_fails(self):
        code, log = self.run_main("--manifest", os.path.join(self.dir.name, "nope.json"), "--download-url", URL,
                                  "--output", self.out)
        self.assertEqual(code, 1)
        self.assertIn("cannot read", log)


if __name__ == "__main__":
    unittest.main()
