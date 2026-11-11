"""Runs steps of .github/workflows/*.yml locally (run: python3 -m unittest discover -s tools/tests -v).

Each test takes the `run:` script of one step, by its `id:`, verbatim from the workflow file and runs it
the way a GitHub-hosted Linux runner does (`bash -e`), against local stand-ins: a bare git repository
served as a file:// URL in place of GitHub, and a folder in place of goatcorp's dalamud-distrib.
They need Linux with bash, git, curl, jq and unzip, like ubuntu-latest; elsewhere they are skipped.
"""

from __future__ import annotations

import json
import os
import platform
import re
import shutil
import subprocess
import tempfile
import textwrap
import unittest
import zipfile
from typing import Dict, List, Optional, Tuple

_HERE = os.path.dirname(os.path.abspath(__file__))
_ROOT = os.path.dirname(os.path.dirname(_HERE))
_WORKFLOWS = os.path.join(_ROOT, ".github", "workflows")
_MISSING = [tool for tool in ("bash", "git", "curl", "jq", "unzip") if shutil.which(tool) is None]
if platform.system() != "Linux":
    _MISSING.insert(0, "Linux (the workflows run on ubuntu-latest)")


def _indent(line: str) -> int:
    return len(line) - len(line.lstrip(" "))


def step_script(workflow: str, step_id: str) -> str:
    """The `run: |` script of the step with `id: <step_id>` in .github/workflows/<workflow>."""
    with open(os.path.join(_WORKFLOWS, workflow), encoding="utf-8") as f:
        lines = f.read().splitlines()
    found = [i for i, line in enumerate(lines) if re.sub(r"\s+#.*$", "", line.strip()) == f"id: {step_id}"]
    if len(found) != 1:
        raise AssertionError(f"{workflow}: expected one step with id {step_id}, found {len(found)}")
    key_indent = _indent(lines[found[0]])
    start = found[0]
    while not (lines[start].lstrip().startswith("- ") and _indent(lines[start]) == key_indent - 2):
        start -= 1
        if start < 0:
            raise AssertionError(f"{workflow}: id {step_id} is not inside a step")
    end = found[0] + 1
    while end < len(lines) and (not lines[end].strip() or _indent(lines[end]) >= key_indent):
        end += 1
    for i in range(start + 1, end):
        if lines[i].strip() == "run: |" and _indent(lines[i]) == key_indent:
            block: List[str] = []
            for line in lines[i + 1:end]:
                if line.strip() and _indent(line) <= key_indent:
                    break
                block.append(line)
            script = textwrap.dedent("\n".join(block)).strip("\n") + "\n"
            # Expressions are passed in through env:, never pasted into the script.
            if "${{" in script:
                raise AssertionError(f"{workflow}: step {step_id} has a ${{{{ }}}} expression in its script")
            return script
    raise AssertionError(f"{workflow}: step {step_id} has no 'run: |' script")


def _clean_env(temp: str) -> Dict[str, str]:
    """The test process's environment without the runner's own GitHub variables, with git isolated."""
    env = {k: v for k, v in os.environ.items() if not k.startswith(("GITHUB_", "RUNNER_", "GH_", "GIT_"))}
    home = os.path.join(temp, "home")
    os.makedirs(home, exist_ok=True)
    gitconfig = os.path.join(home, ".gitconfig")
    open(gitconfig, "a").close()
    env.update(
        {
            "HOME": home,
            "GIT_CONFIG_GLOBAL": gitconfig,
            "GIT_CONFIG_NOSYSTEM": "1",
            "GIT_AUTHOR_NAME": "Test",
            "GIT_AUTHOR_EMAIL": "test@example.invalid",
            "GIT_COMMITTER_NAME": "Test",
            "GIT_COMMITTER_EMAIL": "test@example.invalid",
            "LC_ALL": "C",
        }
    )
    return env


class StepRun:
    """One run of a step script: exit code, output, and what it wrote to GITHUB_ENV / GITHUB_OUTPUT."""

    def __init__(self, returncode: int, output: str, env_file: str, output_file: str) -> None:
        self.returncode = returncode
        self.output = output
        self.github_env = self._read(env_file)
        self.github_output = self._read(output_file)

    @staticmethod
    def _read(path: str) -> Dict[str, str]:
        values: Dict[str, str] = {}
        if os.path.exists(path):
            with open(path, encoding="utf-8") as f:
                for line in f.read().splitlines():
                    if "=" in line:
                        key, value = line.split("=", 1)
                        values[key] = value
        return values


@unittest.skipIf(_MISSING, f"needs {', '.join(_MISSING)}")
class WorkflowStepTest(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.mkdtemp(prefix="mitcue-wf-")
        self.addCleanup(shutil.rmtree, self.temp, True)
        self.env = _clean_env(self.temp)
        self.runner_temp = os.path.join(self.temp, "runner")
        os.makedirs(self.runner_temp)

    def git(self, *args: str, cwd: Optional[str] = None) -> str:
        done = subprocess.run(["git", *args], cwd=cwd, env=self.env, capture_output=True, text=True)
        if done.returncode != 0:
            raise AssertionError(f"git {' '.join(args)} failed: {done.stderr}")
        return done.stdout.strip()

    def run_step(self, workflow: str, step_id: str, env: Dict[str, str]) -> StepRun:
        script_path = os.path.join(self.temp, f"{step_id}.sh")
        with open(script_path, "w", encoding="utf-8") as f:
            f.write(step_script(workflow, step_id))
        env_file = os.path.join(self.runner_temp, "github_env")
        output_file = os.path.join(self.runner_temp, "github_output")
        for path in (env_file, output_file):
            open(path, "w").close()
        full_env = dict(self.env)
        full_env.update({"RUNNER_TEMP": self.runner_temp, "GITHUB_ENV": env_file, "GITHUB_OUTPUT": output_file})
        full_env.update(env)
        workspace = os.path.join(self.temp, "workspace")
        os.makedirs(workspace, exist_ok=True)
        # The default shell of a run: step on Linux runners is `bash -e {0}`.
        done = subprocess.run(["bash", "-e", script_path], cwd=workspace, env=full_env,
                              stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, timeout=120)
        return StepRun(done.returncode, done.stdout, env_file, output_file)


class RepoBranchCheckoutTests(WorkflowStepTest):
    """release.yml, step 'repo-branch': clones the 'repo' branch that holds the published repo.json."""

    def setUp(self) -> None:
        super().setUp()
        self.server = os.path.join(self.temp, "server")
        self.bare = os.path.join(self.server, "owner", "MitCue.git")
        os.makedirs(self.bare)
        self.git("init", "--quiet", "--bare", self.bare)
        self.work = os.path.join(self.temp, "work")
        self.git("init", "--quiet", "-b", "main", self.work)
        with open(os.path.join(self.work, "README.md"), "w") as f:
            f.write("MitCue\n")
        self.git("add", "README.md", cwd=self.work)
        self.git("commit", "--quiet", "-m", "main", cwd=self.work)
        self.main_commit = self.git("rev-parse", "HEAD", cwd=self.work)
        self.git("push", "--quiet", self.bare, "main:refs/heads/main", cwd=self.work)

    def push_repo_branch(self, ref: str = "refs/heads/repo") -> str:
        """Pushes an orphan commit holding repo.json to `ref`; returns the commit id."""
        self.git("checkout", "--quiet", "--orphan", "published", cwd=self.work)
        self.git("rm", "--quiet", "-rf", ".", cwd=self.work)
        with open(os.path.join(self.work, "repo.json"), "w") as f:
            json.dump([{"InternalName": "MitCue", "AssemblyVersion": "0.2.0.0"}], f)
        self.git("add", "repo.json", cwd=self.work)
        self.git("commit", "--quiet", "-m", "repo.json", cwd=self.work)
        commit = self.git("rev-parse", "HEAD", cwd=self.work)
        self.git("push", "--quiet", self.bare, f"HEAD:{ref}", cwd=self.work)
        self.git("checkout", "--quiet", "main", cwd=self.work)
        return commit

    def run_checkout(self, server: Optional[str] = None) -> Tuple[StepRun, str]:
        run = self.run_step("release.yml", "repo-branch", {
            "GH_TOKEN": "ghs_test",
            "GITHUB_SERVER_URL": "file://" + (server or self.server),
            "GITHUB_REPOSITORY": "owner/MitCue",
            "REPO_BRANCH": "repo",
        })
        return run, os.path.join(self.runner_temp, "repo-branch")

    def test_checks_out_the_existing_branch(self):
        published = self.push_repo_branch()
        run, repo_dir = self.run_checkout()
        self.assertEqual(run.returncode, 0, run.output)
        self.assertEqual(run.github_env.get("REPO_DIR"), repo_dir)
        self.assertEqual(self.git("rev-parse", "HEAD", cwd=repo_dir), published)
        self.assertEqual(self.git("symbolic-ref", "--short", "HEAD", cwd=repo_dir), "repo")
        self.assertTrue(os.path.isfile(os.path.join(repo_dir, "repo.json")))

    def test_first_release_starts_an_empty_branch(self):
        run, repo_dir = self.run_checkout()
        self.assertEqual(run.returncode, 0, run.output)
        self.assertIn("does not exist yet", run.output)
        self.assertEqual(run.github_env.get("REPO_DIR"), repo_dir)
        self.assertFalse(os.path.exists(os.path.join(repo_dir, "repo.json")))

    def test_a_branch_that_only_ends_in_repo_is_not_the_repo_branch(self):
        # Review finding: `ls-remote --heads origin repo` also matched docs/repo, and the fetch then failed.
        self.git("push", "--quiet", self.bare, "main:refs/heads/docs/repo", cwd=self.work)
        run, repo_dir = self.run_checkout()
        self.assertEqual(run.returncode, 0, run.output)
        self.assertIn("does not exist yet", run.output)
        self.assertFalse(os.path.exists(os.path.join(repo_dir, "repo.json")))

    def test_a_tag_named_repo_is_not_the_repo_branch(self):
        # Review finding: `fetch origin repo` preferred refs/tags/repo over refs/heads/repo.
        published = self.push_repo_branch()
        self.git("push", "--quiet", self.bare, f"{self.main_commit}:refs/tags/repo", cwd=self.work)
        run, repo_dir = self.run_checkout()
        self.assertEqual(run.returncode, 0, run.output)
        self.assertEqual(self.git("rev-parse", "HEAD", cwd=repo_dir), published)

    def test_a_tag_named_repo_without_the_branch_starts_an_empty_branch(self):
        self.git("push", "--quiet", self.bare, f"{self.main_commit}:refs/tags/repo", cwd=self.work)
        run, repo_dir = self.run_checkout()
        self.assertEqual(run.returncode, 0, run.output)
        self.assertIn("does not exist yet", run.output)
        self.assertFalse(os.path.exists(os.path.join(repo_dir, "repo.json")))

    def test_an_unreachable_repository_fails(self):
        run, _ = self.run_checkout(server=os.path.join(self.temp, "nowhere"))
        self.assertNotEqual(run.returncode, 0, run.output)
        self.assertIn("::error::", run.output)
        self.assertNotIn("REPO_DIR", run.github_env)


class DalamudDownloadTests(WorkflowStepTest):
    """ci.yml and release.yml, step 'dalamud-files': puts the Dalamud named by /version into DALAMUD_HOME."""

    WORKFLOWS = ("ci.yml", "release.yml")

    def setUp(self) -> None:
        super().setUp()
        self.distrib = os.path.join(self.temp, "distrib")
        os.makedirs(self.distrib)
        self.home = os.path.join(self.runner_temp, "dalamud")

    def publish_zip(self, version: str) -> None:
        """What goatcorp's dalamud-distrib serves as latest.zip, reduced to the files the step reads."""
        with zipfile.ZipFile(os.path.join(self.distrib, "latest.zip"), "w") as zf:
            zf.writestr("Dalamud.dll", b"MZ")
            zf.writestr("Dalamud.deps.json", self.deps_json(version))

    def restore_cache(self, version: str) -> None:
        """A DALAMUD_HOME restored by actions/cache."""
        os.makedirs(self.home)
        with open(os.path.join(self.home, "Dalamud.dll"), "wb") as f:
            f.write(b"MZ")
        with open(os.path.join(self.home, "Dalamud.deps.json"), "w", encoding="utf-8") as f:
            f.write(self.deps_json(version))

    @staticmethod
    def deps_json(version: str) -> str:
        return json.dumps({"libraries": {
            "Dalamud.Bindings.ImGui/1.0.0": {"type": "project"},
            f"Dalamud/{version}": {"type": "project"},
            "Newtonsoft.Json/13.0.3": {"type": "package"},
        }})

    def installed_version(self) -> str:
        with open(os.path.join(self.home, "Dalamud.deps.json"), encoding="utf-8") as f:
            names = json.load(f)["libraries"]
        return next(n.split("/", 1)[1] for n in names if n.startswith("Dalamud/"))

    def run_download(self, workflow: str, expected: str, cache_hit: bool) -> StepRun:
        return self.run_step(workflow, "dalamud-files", {
            "DALAMUD_DISTRIB": "file://" + self.distrib,
            "DALAMUD_HOME": self.home,
            "EXPECTED": expected,
            "CACHE_HIT": "true" if cache_hit else "false",
        })

    def reset(self) -> None:
        shutil.rmtree(self.home, ignore_errors=True)
        for name in os.listdir(self.distrib):
            os.remove(os.path.join(self.distrib, name))

    def test_both_workflows_use_the_same_script(self):
        self.assertEqual(step_script("ci.yml", "dalamud-files"), step_script("release.yml", "dalamud-files"))

    def test_downloads_on_a_cache_miss(self):
        for workflow in self.WORKFLOWS:
            with self.subTest(workflow=workflow):
                self.reset()
                self.publish_zip("15.0.4.0")
                run = self.run_download(workflow, "15.0.4.0", cache_hit=False)
                self.assertEqual(run.returncode, 0, run.output)
                self.assertTrue(os.path.isfile(os.path.join(self.home, "Dalamud.dll")))
                self.assertEqual(run.github_output.get("version"), "15.0.4.0")

    def test_a_zip_that_does_not_match_the_version_file_fails(self):
        # Review finding: this only warned, the job succeeded, and actions/cache then saved Dalamud 15.0.4.0
        # under the key for 15.0.5.0, so every later run built against the wrong Dalamud without a warning.
        for workflow in self.WORKFLOWS:
            with self.subTest(workflow=workflow):
                self.reset()
                self.publish_zip("15.0.4.0")
                run = self.run_download(workflow, "15.0.5.0", cache_hit=False)
                self.assertNotEqual(run.returncode, 0, run.output)
                self.assertIn("::error::", run.output)
                self.assertIn("15.0.4.0", run.output)
                self.assertNotIn("version", run.github_output)

    def test_uses_a_matching_cache_without_downloading(self):
        for workflow in self.WORKFLOWS:
            with self.subTest(workflow=workflow):
                self.reset()  # nothing to download: a download attempt would fail
                self.restore_cache("15.0.4.0")
                run = self.run_download(workflow, "15.0.4.0", cache_hit=True)
                self.assertEqual(run.returncode, 0, run.output)
                self.assertEqual(run.github_output.get("version"), "15.0.4.0")

    def test_a_cache_that_holds_another_version_is_replaced(self):
        # A cache entry saved before the fix can hold the wrong Dalamud under the right key.
        for workflow in self.WORKFLOWS:
            with self.subTest(workflow=workflow):
                self.reset()
                self.restore_cache("15.0.4.0")
                self.publish_zip("15.0.5.0")
                run = self.run_download(workflow, "15.0.5.0", cache_hit=True)
                self.assertEqual(run.returncode, 0, run.output)
                self.assertEqual(self.installed_version(), "15.0.5.0")
                self.assertEqual(run.github_output.get("version"), "15.0.5.0")

    def test_a_cache_that_holds_another_version_fails_if_the_download_does_too(self):
        for workflow in self.WORKFLOWS:
            with self.subTest(workflow=workflow):
                self.reset()
                self.restore_cache("15.0.4.0")
                self.publish_zip("15.0.4.0")
                run = self.run_download(workflow, "15.0.5.0", cache_hit=True)
                self.assertNotEqual(run.returncode, 0, run.output)
                self.assertIn("::error::", run.output)


if __name__ == "__main__":
    unittest.main()
