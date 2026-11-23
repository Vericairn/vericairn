# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

import json
import os
from pathlib import Path
import runpy
import stat
import subprocess
import sys
import tempfile
import textwrap
import unittest

SCRIPT = Path(__file__).with_name("runner-failures.py")
rf = runpy.run_path(str(SCRIPT))
WORKFLOWS = Path(__file__).resolve().parents[1] / "workflows"

KILLED = "Process completed with exit code 137."
LOST = ("The hosted runner: GitHub Actions 1000014 lost communication with the server. Anything in your workflow that "
        "terminates the runner process, starves it for CPU/Memory, or blocks its network access can cause this error.")
SHUTDOWN = "The runner has received a shutdown signal. This can happen when the runner service is stopped."
TESTS_FAILED = "Process completed with exit code 2."


def step(number, name, conclusion="success", status="completed"):
    return {"number": number, "name": name, "status": status, "conclusion": conclusion}


def build_test(ended, conclusion="failure", status="completed"):
    """A build-test leg's steps as GitHub's jobs API names them, ending in the named step."""
    names = ["Set up job", "Run actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1",
             "Run actions/setup-dotnet@0f4a354dd5547a28c70b7853dbd95d5b67886e73", "Restore (locked)", "Build",
             "Install a secret service", "Test, with a secret service", "Complete job"]
    steps = []
    for number, name in enumerate(names, 1):
        if name == ended:
            steps.append(step(number, name, conclusion, status))
            break
        steps.append(step(number, name))
    return steps


def job(identifier, name, steps, conclusion="failure"):
    return {"id": identifier, "name": name, "conclusion": conclusion, "steps": steps}


class VerdictTests(unittest.TestCase):
    def test_exit_137_or_a_lost_runner_in_setup_is_a_runner_failure(self):
        for ended in ("Set up job", "Run actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1",
                      "Run actions/setup-dotnet@0f4a354dd5547a28c70b7853dbd95d5b67886e73", "Restore (locked)"):
            for message in (KILLED, LOST, SHUTDOWN):
                with self.subTest(ended=ended, message=message[:30]):
                    retry, lines = rf["verdict"]([job(1, "build-test (windows-latest, 2)", build_test(ended))], {1: [message]})
                    self.assertTrue(retry, lines)
                    self.assertIn(f"in '{ended}'", lines[0])

    def test_a_runner_lost_before_any_step_is_a_runner_failure(self):
        retry, _ = rf["verdict"]([job(1, "remote-ios", [])], {1: ["The job was not acquired by Runner of type hosted even after multiple attempts"]})
        self.assertTrue(retry)

    def test_a_lost_runner_still_running_a_setup_step_is_a_runner_failure(self):
        steps = build_test("Restore (locked)", conclusion=None, status="in_progress")
        self.assertTrue(rf["verdict"]([job(1, "build-test (macos-latest, 1)", steps)], {1: [LOST]})[0])

    def test_nothing_after_setup_is_retried_whatever_ended_it(self):
        for ended in ("Build", "Install a secret service", "Test, with a secret service"):
            for message in (KILLED, LOST, TESTS_FAILED):
                with self.subTest(ended=ended, message=message[:30]):
                    retry, lines = rf["verdict"]([job(1, "build-test (ubuntu-latest, 1)", build_test(ended))], {1: [message]})
                    self.assertFalse(retry)
                    self.assertIn("not a runner failure", lines[0])

    def test_a_setup_step_that_failed_for_another_reason_is_not_retried(self):
        for message in ("Process completed with exit code 1.", "The action has timed out.", ""):
            with self.subTest(message=message):
                self.assertFalse(rf["verdict"]([job(1, "build-test (ubuntu-latest, 1)", build_test("Restore (locked)"))], {1: [message]})[0])

    def test_one_test_failure_beside_runner_failures_retries_nothing(self):
        jobs = [job(1, "build-test (windows-latest, 1)", build_test("Restore (locked)")),
                job(2, "build-test (windows-latest, 2)", build_test("Test, with a secret service"))]
        self.assertFalse(rf["verdict"](jobs, {1: [KILLED], 2: [TESTS_FAILED]})[0])

    def test_the_gate_and_jobs_that_did_not_fail_are_not_judged(self):
        jobs = [job(1, "build-test (windows-latest, 1)", build_test("Restore (locked)")),
                job(2, "ci-gate", [step(1, "Set up job"), step(2, "Require every job this event runs", "failure")]),
                job(3, "build-test (windows-latest, 2)", build_test("Complete job"), conclusion="success"),
                job(4, "package / package", [], conclusion="skipped"),
                job(5, "remote-ios", build_test("Build", conclusion="cancelled"), conclusion="cancelled")]
        retry, lines = rf["verdict"](jobs, {1: [KILLED]})
        self.assertTrue(retry)
        self.assertEqual(1, len(lines))

    def test_a_run_whose_only_failure_is_the_gate_is_not_retried(self):
        self.assertFalse(rf["verdict"]([job(2, "ci-gate", [])], {})[0])

    def test_every_named_setup_step_is_a_step_of_a_workflow(self):
        # A renamed step would silently stop being retried; each name the script trusts must still exist.
        names = set()
        for workflow in WORKFLOWS.glob("*.yml"):
            for line in workflow.read_text(encoding="utf-8").splitlines():
                stripped = line.strip()
                if stripped.startswith("- name: "):
                    names.add(stripped[len("- name: "):])
        self.assertEqual(set(), set(rf["SETUP_STEPS"]) - names - {"Set up job"})


# A stand-in gh: answers the jobs and annotations of one scenario and records a re-run.
FAKE = textwrap.dedent('''\
    #!/usr/bin/env python3
    import json, os, sys
    args = sys.argv[1:]
    state = os.environ["FAKE_STATE"]
    scenario = json.load(open(os.path.join(state, "scenario.json")))
    route = next(arg for arg in args if arg.startswith("repos/"))
    if "--method" in args:
        open(os.path.join(state, "reruns"), "a").write(route + "\\n"); sys.exit(0)
    if scenario.get("broken"):
        sys.stderr.write("HTTP 502"); sys.exit(1)
    if "/jobs?" in route:
        print(json.dumps({"total_count": len(scenario["jobs"]), "jobs": scenario["jobs"]})); sys.exit(0)
    identifier = route.split("/check-runs/")[1].split("/")[0]
    print(json.dumps([{"message": message} for message in scenario["annotations"].get(identifier, [])]))
    ''')


class ScriptTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        fake = self.root / "bin" / "gh"
        fake.parent.mkdir()
        fake.write_text(FAKE)
        fake.chmod(fake.stat().st_mode | stat.S_IXUSR)

    def run_script(self, scenario, *arguments):
        (self.root / "scenario.json").write_text(json.dumps(scenario))
        reruns = self.root / "reruns"
        if reruns.exists():
            reruns.unlink()
        env = dict(os.environ, FAKE_STATE=str(self.root), PATH=str(self.root / "bin") + os.pathsep + os.environ["PATH"])
        result = subprocess.run([sys.executable, str(SCRIPT), "--repository", "example/repo", "--run", "42", *arguments],
                                env=env, capture_output=True, text=True)
        return result.returncode, result.stdout, reruns.read_text().splitlines() if reruns.exists() else []

    def test_a_first_attempt_ended_only_by_runners_is_rerun_once(self):
        scenario = {"jobs": [job(7, "build-test (windows-latest, 3)", build_test("Restore (locked)"))], "annotations": {"7": [KILLED]}}
        code, out, reruns = self.run_script(scenario, "--rerun")
        self.assertEqual(0, code, out)
        self.assertEqual(["repos/example/repo/actions/runs/42/rerun-failed-jobs"], reruns)

    def test_a_test_failure_is_never_rerun(self):
        scenario = {"jobs": [job(7, "build-test (windows-latest, 3)", build_test("Test, with a secret service"))], "annotations": {"7": [KILLED]}}
        code, out, reruns = self.run_script(scenario, "--rerun")
        self.assertEqual((3, []), (code, reruns))
        self.assertIn("not a runner failure", out)

    def test_a_later_attempt_is_never_rerun(self):
        scenario = {"jobs": [job(7, "remote-ios", build_test("Restore (locked)"))], "annotations": {"7": [LOST]}}
        code, out, reruns = self.run_script(scenario, "--rerun", "--attempt", "2")
        self.assertEqual((3, []), (code, reruns))
        self.assertIn("already the re-run", out)

    def test_without_rerun_it_only_judges(self):
        scenario = {"jobs": [job(7, "remote-ios", build_test("Restore (locked)"))], "annotations": {"7": [LOST]}}
        self.assertEqual((0, []), (lambda c, _, r: (c, r))(*self.run_script(scenario)))

    def test_an_unreadable_run_reruns_nothing(self):
        code, out, reruns = self.run_script({"broken": True}, "--rerun")
        self.assertEqual((1, []), (code, reruns))
        self.assertIn("Not re-run", out)


if __name__ == "__main__":
    unittest.main()
