# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

"""ci.yml's ci-gate step, run as GitHub runs it (bash -e) on simulated needs and a shard plan in its working directory."""

import json
from pathlib import Path
import runpy
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github" / "workflows" / "ci.yml"
STEP = "      - name: Require every job this event runs\n"
COUNTS = {"ubuntu-latest": 3, "macos-latest": 4, "windows-latest": 6}
FULL_GATE = ("plan", "workflow-policy", "build-test", "remote-ios", "remote-android", "package")
ci_plan = runpy.run_path(str(Path(__file__).with_name("ci-plan.py")))


def gate_script():
    """The run block of ci-gate's one script step, dedented."""
    text = WORKFLOW.read_text(encoding="utf-8")
    step = text[text.index(STEP):]
    lines = step[step.index("        run: |\n") + len("        run: |\n"):].split("\n")
    body = []
    for line in lines:
        if line.strip() and not line.startswith(" " * 10):
            break
        body.append(line[10:])
    return "\n".join(body).rstrip("\n") + "\n"


def matrix(counts):
    return {"include": [{"os": os_, "share": share} for os_, count in counts.items() for share in range(1, count + 1)]}


def needs(results, plan=None, skip="false"):
    """toJSON(needs) as GitHub gives it: every job's result, and the lookup's and the plan's outputs as strings."""
    jobs = {job: {"result": result, "outputs": {}} for job, result in results.items()}
    jobs["queue-tested"]["outputs"] = {"skip": skip}
    if plan is not None:
        jobs["plan"]["outputs"] = {name: value if isinstance(value, str) else json.dumps(value, separators=(",", ":"))
                                   for name, value in plan.items()}
    return jobs


def full_gate(matrix_=None, **plan):
    outputs = {"full": "true", "build": "true", "docs": "false", "matrix": matrix_ or matrix(COUNTS)}
    outputs.update(plan)
    results = {"queue-tested": "success", "docs-check": "skipped", **{job: "success" for job in FULL_GATE}}
    return results, outputs


def pull_request(docs=False, matrix_=None):
    outputs = {"full": "false", "build": "false" if docs else "true", "docs": "true" if docs else "false",
               "matrix": matrix_ or matrix({"ubuntu-latest": COUNTS["ubuntu-latest"]})}
    results = {"queue-tested": "success", "plan": "success", "workflow-policy": "success",
               "build-test": "skipped" if docs else "success", "docs-check": "success" if docs else "skipped",
               "remote-ios": "skipped", "remote-android": "skipped", "package": "skipped"}
    return results, outputs


def all_skipped():
    return {"queue-tested": "success", **{job: "skipped" for job in FULL_GATE + ("docs-check",)}}


@unittest.skipUnless(shutil.which("bash") and shutil.which("jq"), "the gate's step needs bash and jq")
class GateTests(unittest.TestCase):
    def run_gate(self, event, jobs, shard_plan=None, write_plan=True):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            script = directory / "gate.sh"
            script.write_text(gate_script(), encoding="utf-8")
            if write_plan:
                (directory / ".github").mkdir()
                plan = shard_plan if shard_plan is not None else {
                    "platforms": {os_: {"shares": count, "reserve": 0.1} for os_, count in COUNTS.items()}, "classes": {}}
                (directory / ".github" / "test-shards.json").write_text(
                    plan if isinstance(plan, str) else json.dumps(plan), encoding="utf-8")
            env = {"PATH": str(Path(shutil.which("jq")).parent) + ":/usr/bin:/bin", "EVENT": event, "NEEDS": json.dumps(jobs)}
            result = subprocess.run([shutil.which("bash"), "-e", str(script)], cwd=directory, env=env, capture_output=True, text=True)
            return result.returncode, result.stdout + result.stderr

    def assertPasses(self, event, jobs, **options):
        code, output = self.run_gate(event, jobs, **options)
        self.assertEqual(0, code, output)

    def assertFails(self, event, jobs, **options):
        code, output = self.run_gate(event, jobs, **options)
        self.assertNotEqual(0, code, output)
        return output

    def test_the_full_gate_passes_with_every_share_the_shard_plan_gives(self):
        for event in ("merge_group", "push", "workflow_dispatch"):
            with self.subTest(event=event):
                results, plan = full_gate()
                self.assertPasses(event, needs(results, plan))

    def test_the_plan_the_repository_writes_passes_its_own_shard_plan(self):
        shard_plan = json.loads((ROOT / ".github" / "test-shards.json").read_text(encoding="utf-8"))
        decided = ci_plan["plan"]("merge_group", [], ci_plan["shares"](ROOT))
        results, plan = full_gate(decided["matrix"])
        self.assertPasses("merge_group", needs(results, plan), shard_plan=shard_plan)

    def test_a_matrix_that_drops_trailing_shares_fails(self):
        results, plan = full_gate(matrix({"ubuntu-latest": 3, "macos-latest": 2, "windows-latest": 1}))
        output = self.assertFails("merge_group", needs(results, plan))
        self.assertIn("macos-latest shares: [1,2] (the shard plan's: [1,2,3,4])", output)

    def test_a_matrix_that_misses_repeats_adds_or_quotes_a_share_or_drops_a_platform_fails(self):
        legs = matrix(COUNTS)["include"]
        cases = {
            "missing": [leg for leg in legs if leg != {"os": "windows-latest", "share": 3}],
            "repeated": legs + [{"os": "ubuntu-latest", "share": 2}],
            "extra": legs + [{"os": "macos-latest", "share": 5}],
            "quoted": [dict(leg, share=str(leg["share"])) for leg in legs],
            "no platform": [leg for leg in legs if leg["os"] != "ubuntu-latest"],
        }
        for name, include in cases.items():
            with self.subTest(case=name):
                results, plan = full_gate({"include": include})
                self.assertFails("merge_group", needs(results, plan))
        results, plan = full_gate("not json")
        self.assertFails("merge_group", needs(results, plan))

    def test_an_unusable_or_missing_shard_plan_fails(self):
        for name, shard_plan in {"zero": 0, "string": "4", "boolean": True, "fraction": 2.5, "thirteen": 13, "absent": None}.items():
            with self.subTest(case=name):
                platforms = {os_: {"shares": count} for os_, count in COUNTS.items()}
                if shard_plan is None:
                    del platforms["macos-latest"]
                else:
                    platforms["macos-latest"]["shares"] = shard_plan
                results, plan = full_gate()
                self.assertFails("merge_group", needs(results, plan), shard_plan={"platforms": platforms, "classes": {}})
        results, plan = full_gate()
        self.assertFails("merge_group", needs(results, plan), shard_plan="{")
        self.assertFails("merge_group", needs(results, plan), write_plan=False)

    def test_a_plan_that_is_not_the_full_gate_or_a_job_that_did_not_pass_fails(self):
        for change in ({"full": "false"}, {"build": "false"}, {"docs": "true"}):
            with self.subTest(change=change):
                results, plan = full_gate(**change)
                self.assertFails("merge_group", needs(results, plan))
        for job in FULL_GATE:
            for result in ("failure", "cancelled", "skipped"):
                with self.subTest(job=job, result=result):
                    results, plan = full_gate()
                    results[job] = result
                    self.assertFails("merge_group", needs(results, plan))

    def test_only_a_push_may_skip(self):
        self.assertPasses("push", needs(all_skipped(), skip="true"))
        for event in ("merge_group", "workflow_dispatch", "pull_request"):
            with self.subTest(event=event):
                output = self.assertFails(event, needs(all_skipped(), skip="true"))
                self.assertIn(f"queue-tested: skip on a {event} run, where only a push can skip", output)
        results = all_skipped()
        results["build-test"] = "success"
        self.assertFails("push", needs(results, skip="true"))

    def test_a_pull_request_runs_all_of_linux_or_the_documentation_check(self):
        self.assertPasses("pull_request", needs(*pull_request()))
        self.assertPasses("pull_request", needs(*pull_request(docs=True)))
        # The documentation check builds no shares, so its matrix is not read.
        self.assertPasses("pull_request", needs(*pull_request(docs=True, matrix_={"include": []})))
        results, plan = pull_request(matrix_=matrix({"ubuntu-latest": 2}))
        output = self.assertFails("pull_request", needs(results, plan))
        self.assertIn("ubuntu-latest shares: [1,2] (the shard plan's: [1,2,3])", output)
        results, plan = pull_request()
        results["remote-ios"] = "success"
        self.assertFails("pull_request", needs(results, plan))
        results, plan = pull_request()
        plan["docs"] = "true"
        self.assertFails("pull_request", needs(results, plan))

    def test_the_step_reads_the_shard_plan_from_the_checkout_only(self):
        script = gate_script()
        self.assertIn(" .github/test-shards.json)", script)
        self.assertNotIn("curl", script)
        self.assertNotIn("gh ", script)


if __name__ == "__main__":
    unittest.main()
