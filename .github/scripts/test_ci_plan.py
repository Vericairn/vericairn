# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

import json
import os
from pathlib import Path
import runpy
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).with_name("ci-plan.py")
ci = runpy.run_path(str(SCRIPT))
COUNTS = {"ubuntu-latest": 3, "macos-latest": 4, "windows-latest": 6}


class DocumentationTests(unittest.TestCase):
    def test_documentation_is_docs_outside_the_frozen_sets_and_the_root_readme_and_roadmap(self):
        for path in ("docs/cli.md", "docs/reference/cli.md", "docs/changes/0.9.5/x.md", "docs/state-language.json", "README.md", "ROADMAP.md"):
            with self.subTest(path=path):
                self.assertTrue(ci["is_documentation"](path))
        for path in ("docs/releases/0.9.4/cli.md", "docs/releases/frozen-sha256.json", "src/README.md", "tests/README.md",
                     "readme.md", "AGENTS.md", ".github/workflows/README.md", "docs", "Directory.Build.props"):
            with self.subTest(path=path):
                self.assertFalse(ci["is_documentation"](path))

    def test_one_product_file_or_no_file_makes_a_product_change(self):
        self.assertTrue(ci["documentation_only"](["docs/cli.md", "README.md"]))
        self.assertFalse(ci["documentation_only"](["docs/cli.md", "src/Vericairn.Cli/Program.cs"]))
        self.assertFalse(ci["documentation_only"]([]))


class PlanTests(unittest.TestCase):
    def legs(self, decided):
        return [(leg["os"], leg["share"]) for leg in decided["matrix"]["include"]]

    def test_the_queue_a_push_and_a_manual_run_get_the_full_gate_whatever_changed(self):
        for event in ("merge_group", "push", "workflow_dispatch"):
            for paths in ([], ["docs/cli.md"], None):
                with self.subTest(event=event, paths=paths):
                    decided = ci["plan"](event, paths, COUNTS)
                    self.assertEqual((True, True, False), (decided["full"], decided["build"], decided["docs"]))
                    self.assertEqual(3 + 4 + 6, len(decided["matrix"]["include"]))
                    for platform, count in COUNTS.items():
                        self.assertEqual(list(range(1, count + 1)), [share for os_, share in self.legs(decided) if os_ == platform])

    def test_a_pull_request_runs_linux_and_only_the_documentation_check_when_it_changes_only_documentation(self):
        product = ci["plan"]("pull_request", ["src/x.cs"], COUNTS)
        self.assertEqual((False, True, False), (product["full"], product["build"], product["docs"]))
        self.assertEqual([("ubuntu-latest", 1), ("ubuntu-latest", 2), ("ubuntu-latest", 3)], self.legs(product))
        docs = ci["plan"]("pull_request", ["docs/cli.md", "ROADMAP.md"], COUNTS)
        self.assertEqual((False, False, True), (docs["full"], docs["build"], docs["docs"]))

    def test_unreadable_changes_build(self):
        decided = ci["plan"]("pull_request", None, COUNTS)
        self.assertEqual((True, False), (decided["build"], decided["docs"]))

    def test_outputs_are_single_lines(self):
        lines = ci["outputs"](ci["plan"]("merge_group", [], COUNTS))
        self.assertEqual(["full=true", "build=true", "docs=false"], lines[:3])
        self.assertTrue(lines[3].startswith("matrix={\"include\":[{\"os\":\"ubuntu-latest\",\"share\":1}"))
        self.assertTrue(all("\n" not in line for line in lines))


class ScriptTests(unittest.TestCase):
    """The script against a real repository: a pull request's merge commit is diffed against its first parent."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        plan = {"platforms": {name: {"shares": count} for name, count in COUNTS.items()}, "classes": {}}
        (self.root / ".github").mkdir()
        (self.root / ".github" / "test-shards.json").write_text(json.dumps(plan))
        (self.root / "src").mkdir()
        (self.root / "src" / "guide.md").write_text("product\n")
        (self.root / "docs").mkdir()
        self.git("init", "-q", "-b", "main")
        self.git("add", "-A")
        self.commit("base")

    def git(self, *args):
        env = dict(os.environ, GIT_CONFIG_GLOBAL=os.devnull, GIT_CONFIG_NOSYSTEM="1")
        subprocess.run(["git", "-c", "user.name=Test", "-c", "user.email=test@example.invalid", *args], cwd=self.root, env=env,
                       check=True, capture_output=True)

    def commit(self, message):
        self.git("commit", "-q", "--allow-empty", "-m", message)

    def merge(self, change):
        """Branch, apply the change, and merge it into main without fast-forward, as GitHub's test merge commit is."""
        self.git("checkout", "-q", "-b", "topic")
        change()
        self.git("add", "-A")
        self.commit("topic")
        self.git("checkout", "-q", "main")
        self.git("merge", "-q", "--no-ff", "-m", "merge", "topic")

    def run_plan(self, event="pull_request"):
        result = subprocess.run([sys.executable, str(SCRIPT), "--event", event], cwd=self.root, capture_output=True, text=True)
        self.assertEqual(0, result.returncode, result.stderr)
        return dict(line.split("=", 1) for line in result.stdout.splitlines())

    def test_a_documentation_only_pull_request_runs_the_documentation_check(self):
        self.merge(lambda: (self.root / "docs" / "cli.md").write_text("guide\n"))
        self.assertEqual(("false", "true"), (self.run_plan()["build"], self.run_plan()["docs"]))

    def test_a_file_moved_from_the_product_into_docs_is_a_product_change(self):
        self.merge(lambda: self.git("mv", "src/guide.md", "docs/guide.md"))
        self.assertEqual(("true", "false"), (self.run_plan()["build"], self.run_plan()["docs"]))

    def test_without_a_first_parent_the_pull_request_builds(self):
        self.assertEqual(("true", "false"), (self.run_plan()["build"], self.run_plan()["docs"]))

    def test_an_unusable_shard_plan_fails_the_plan(self):
        (self.root / ".github" / "test-shards.json").write_text(json.dumps({"platforms": {"ubuntu-latest": {"shares": 0}}}))
        result = subprocess.run([sys.executable, str(SCRIPT), "--event", "merge_group"], cwd=self.root, capture_output=True, text=True)
        self.assertEqual(1, result.returncode)
        self.assertEqual("", result.stdout)


if __name__ == "__main__":
    unittest.main()
