# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

import importlib.util
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("examples_smoke", ROOT / "tools/examples/smoke.py")
SMOKE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SMOKE)
FIXTURE = ROOT / "tests/examples/read-fixture"


class ExamplesJourneyTests(unittest.TestCase):
    def test_tier_two_runs_only_the_prebuilt_driver(self):
        with tempfile.TemporaryDirectory(prefix="example-driver-test-") as directory:
            scratch = Path(directory)
            cli = scratch / "candidate"
            journey_report = scratch / "journeys.json"
            environment = {}
            report = {}
            finished = subprocess.CompletedProcess([], 0, stdout="", stderr="")
            SMOKE.write_json(journey_report, [{"result": "passed"}])
            with (patch.object(SMOKE, "execute", return_value=finished) as execute,
                  patch.object(SMOKE.time, "monotonic", return_value=100.0)):
                SMOKE.journey_leg(cli, scratch, environment, [{"legs": ["tier2"]}], report, 100.0)

            driver = ROOT / "artifacts/bin/Vericairn.Cli.Tests/release/Vericairn.Cli.Tests.dll"
            self.assertEqual(1, execute.call_count, "Tier 2 must not build the driver inside the smoke.")
            execute.assert_called_once_with(
                ["dotnet", str(driver), "--filter-method",
                 "Vericairn.Cli.Tests.ExamplesJourneyTests.TierTwoRunsEveryRegisteredJourney"],
                ROOT,
                {"VERICAIRN_EXAMPLES_BINARY": str(cli), "VERICAIRN_EXAMPLES_JOURNEY_REPORT": str(journey_report)},
                timeout=SMOKE.STEP_SECONDS - SMOKE.REPORT_MARGIN_SECONDS,
            )
            self.assertEqual({"journeys": [{"result": "passed"}]}, report)

    def test_the_journeys_get_what_the_read_leg_left_of_the_step(self):
        with tempfile.TemporaryDirectory(prefix="example-driver-test-") as directory:
            scratch = Path(directory)
            SMOKE.write_json(scratch / "journeys.json", [{"result": "passed"}])
            finished = subprocess.CompletedProcess([], 0, stdout="", stderr="")
            with (patch.object(SMOKE, "execute", return_value=finished) as execute,
                  patch.object(SMOKE.time, "monotonic", return_value=160.0)):
                SMOKE.journey_leg(scratch / "candidate", scratch, {}, [{"legs": ["tier2"]}], {}, 100.0)
            self.assertEqual(SMOKE.STEP_SECONDS - SMOKE.REPORT_MARGIN_SECONDS - 60, execute.call_args.kwargs["timeout"])

    def test_journeys_that_outrun_the_step_fail_the_smoke_with_its_own_message(self):
        with tempfile.TemporaryDirectory(prefix="example-driver-test-") as directory:
            scratch = Path(directory)
            expired = subprocess.TimeoutExpired(["dotnet"], 1)
            with (patch.object(SMOKE, "execute", side_effect=expired),
                  patch.object(SMOKE.time, "monotonic", return_value=100.0)):
                with self.assertRaisesRegex(SMOKE.SmokeFailure, "did not finish in the 170 seconds"):
                    SMOKE.journey_leg(scratch / "candidate", scratch, {}, [{"legs": ["tier2"]}], {}, 100.0)
            with (patch.object(SMOKE, "execute") as execute,
                  patch.object(SMOKE.time, "monotonic", return_value=400.0)):
                with self.assertRaisesRegex(SMOKE.SmokeFailure, "no time is left"):
                    SMOKE.journey_leg(scratch / "candidate", scratch, {}, [{"legs": ["tier2"]}], {}, 100.0)
            execute.assert_not_called()


class ExamplesReadTests(unittest.TestCase):
    def setUp(self):
        scratch = self.enterContext(tempfile.TemporaryDirectory(prefix="example-read-test-"))
        self.scratch = Path(scratch)
        self.environment = SMOKE.isolated_environment(self.scratch)
        self.repo = self.scratch / "fixture"
        self.repo.mkdir()
        SMOKE.git(self.repo, self.environment, "init", "--quiet", "--initial-branch=fixture-default")
        self.example = SMOKE.examples()[0]
        for name in SMOKE.read_json(FIXTURE / "paths.json"):
            target = self.repo / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.touch()
        shutil.copyfile(FIXTURE / "README.md", self.repo / "README.md")
        shutil.copyfile(FIXTURE / "estate-manifest.json", self.repo / "estate/manifest.json")

    def commit_fixture(self):
        SMOKE.git(self.repo, self.environment, "add", "--all")
        SMOKE.git(self.repo, self.environment, "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid",
                  "-c", "commit.gpgsign=false", "commit", "--quiet", "-m", "Fixture")

    def findings(self):
        self.commit_fixture()
        return SMOKE.readme_findings(self.repo, self.environment, self.example["release"])

    def fixture_pin(self):
        commit = SMOKE.git(self.repo, self.environment, "rev-parse", "HEAD").strip()
        return dict(self.example["repositories"][0], commit=commit)

    def test_checkout_accepts_an_ancestor_of_the_default_branch(self):
        self.commit_fixture()
        pin = self.fixture_pin()
        (self.repo / "later.txt").touch()
        self.commit_fixture()

        checkout = self.scratch / "checkout"
        SMOKE.checkout(pin, checkout, self.environment, self.repo)

        self.assertEqual(pin["commit"], SMOKE.git(checkout, self.environment, "rev-parse", "HEAD").strip())
        self.assertFalse((checkout / "later.txt").exists())

    def test_checkout_refuses_a_pin_on_another_branch(self):
        self.commit_fixture()
        SMOKE.git(self.repo, self.environment, "switch", "--quiet", "-c", "other")
        (self.repo / "other.txt").touch()
        self.commit_fixture()
        pin = self.fixture_pin()
        SMOKE.git(self.repo, self.environment, "switch", "--quiet", "fixture-default")

        with self.assertRaisesRegex(SMOKE.SmokeFailure, rf"{pin['name']}.*{pin['commit']}.*fixture-default"):
            SMOKE.checkout(pin, self.scratch / "checkout", self.environment, self.repo)

    def test_checkout_refuses_a_source_without_a_default_branch(self):
        self.commit_fixture()
        pin = self.fixture_pin()
        SMOKE.git(self.repo, self.environment, "checkout", "--quiet", "--detach")

        with self.assertRaisesRegex(SMOKE.SmokeFailure, rf"{pin['name']}.*{pin['commit']}.*default branch"):
            SMOKE.checkout(pin, self.scratch / "checkout", self.environment, self.repo)

    def test_pinned_fixture(self):
        findings = self.findings()
        self.assertEqual([("error", "readme.relative-link", "README.md:docs/registers/decisions/")], findings)
        SMOKE.check_findings(self.example, self.example["repositories"][0], findings)

    def test_wrong_readme_release(self):
        readme = self.repo / "README.md"
        readme.write_text(readme.read_text().replace("pins [Vericairn 0.9.3]", "pins [Vericairn 0.0.1]"))
        with self.assertRaisesRegex(SMOKE.SmokeFailure, "readme.release-pin"):
            SMOKE.check_findings(self.example, self.example["repositories"][0], self.findings())

    def test_dead_relative_link_at_commit(self):
        # The file exists by the time the checker runs, in a working tree not containing it at the pin.
        link = self.repo / ".github/workflows/check.yml"
        link.unlink()
        self.commit_fixture()
        link.touch()
        findings = SMOKE.readme_findings(self.repo, self.environment, self.example["release"])
        with self.assertRaisesRegex(SMOKE.SmokeFailure, "README.md:.github/workflows/check.yml"):
            SMOKE.check_findings(self.example, self.example["repositories"][0], findings)

    def test_link_forms(self):
        content = """[relative](./a%20b)
[reference]: docs/ref.md
![image](<docs/an image.png> "tooltip")
<a href='docs/html.md'>
<a data-href=ignore.md href=docs/search.md?key=value>
```markdown
<!--
[code](no-target.md)
```
[after-code](docs/after.md)
"""
        self.assertCountEqual(
            ["./a%20b", "docs/ref.md", "docs/an image.png", "docs/html.md", "docs/search.md?key=value", "docs/after.md"],
            SMOKE.readme_destinations(content))

    def test_each_release_on_the_pin_line_is_checked(self):
        readme = self.repo / "README.md"
        good = "[Vericairn 0.9.3](https://github.com/Vericairn/vericairn/releases/tag/v0.9.3)"
        bad = "[Vericairn 0.0.1](https://github.com/Vericairn/vericairn/releases/tag/v0.0.1)"
        original = readme.read_text(encoding="utf-8")
        for pins in (f"pins {bad} and pins {good}", f"pins {good} and {bad}", f"pins {good} while it pins Vericairn 0.0.1"):
            with self.subTest(pins=pins):
                readme.write_text(original.replace(f"pins {good}", pins), encoding="utf-8")
                self.assertIn(("error", "readme.release-pin", "README.md"), self.findings())

    def test_relative_destinations_in_more_link_forms(self):
        for source in (
            "<a title='href=other.md' href=docs/gone.md>",
            '<video src="docs/gone.md"></video>',
            "<source src=docs/gone.md>",
            "<docs/gone.md>",
            "> [ref]: docs/gone.md",
            "- Item\n\n    [ref]: docs/gone.md",
            "> - [ref]: docs/gone.md",
            "> [ref]:\n>   docs/gone.md",
        ):
            with self.subTest(source=source):
                self.assertEqual(["docs/gone.md"], list(SMOKE.readme_destinations(source)))

    def test_non_fences_cannot_hide_relative_links(self):
        for prefix in ("``` a`b", "<!--\n```\n[comment](ignored.md)\n-->"):
            with self.subTest(prefix=prefix):
                source = prefix + "\n[relative](docs/gone.md)"
                self.assertEqual(["docs/gone.md"], list(SMOKE.readme_destinations(source)))

    @unittest.skipUnless(os.environ.get("VERICAIRN_EXAMPLES_TEST_CLI"), "requires the candidate executable")
    def test_shipped_cli_refuses_a_broken_record(self):
        fixture = self.scratch / "records-fixture"
        shutil.copytree(ROOT / "tests/fixtures/format-1-estate", fixture)
        cli = Path(os.environ["VERICAIRN_EXAMPLES_TEST_CLI"])
        command = ["estate", "check"]
        code, before = SMOKE.run_cli(cli, fixture, self.environment, command)
        self.assertEqual((0, 0), (code, before["errors"]), before["findings"])
        next((fixture / "records/work-order").rglob("*.json")).write_text("{broken\n")
        code, after = SMOKE.run_cli(cli, fixture, self.environment, command)
        self.assertNotEqual(0, code)
        self.assertTrue(any(SMOKE.finding_key(finding)[0] == "error" and SMOKE.finding_key(finding)[2].startswith("records/work-order/")
                            for finding in after["findings"]), after["findings"])

    def test_read_estate_accepts_exit_one_with_findings_for_check_and_status(self):
        finding = {"severity": "error", "code": "fixture.finding", "location": "fixture.json"}
        responses = [(1, {"findings": [finding]}), (1, {"findings": [finding]}), (0, {"applied": False})]
        report = []
        with patch.object(SMOKE, "run_cli", side_effect=responses):
            findings = SMOKE.read_estate(None, self.repo, self.environment, self.scratch, "0.9.5", report)
        self.assertEqual({("error", "fixture.finding", "fixture.json")}, findings)
        self.assertEqual([1, 1, 0], [step["exitCode"] for step in report])

    def test_check_findings_refuses_an_expected_finding_that_is_absent(self):
        pin = self.example["repositories"][0]
        finding = {"repository": pin["name"], "commit": pin["commit"], "severity": "error",
                   "code": "fixture.finding", "location": "fixture.json"}
        with self.assertRaisesRegex(SMOKE.SmokeFailure, "expected but absent: .*fixture.finding"):
            SMOKE.check_findings({"expectedReadFindings": [finding]}, pin, set())

    def test_read_estate_refuses_failed_commands(self):
        finding = {"severity": "error", "code": "fixture.finding", "location": "fixture.json"}
        ok = (0, {})
        plan = (0, {"applied": False})
        cases = (
            ("estate check exited 1", [(1, {"findings": []}), ok, plan]),
            ("status exited 2", [ok, (2, {"findings": [finding]}), plan]),
            ("estate upgrade exited 1", [ok, ok, (1, {"applied": False, "findings": [finding]})]),
        )
        for message, responses in cases:
            with self.subTest(message=message), patch.object(SMOKE, "run_cli", side_effect=responses):
                with self.assertRaisesRegex(SMOKE.SmokeFailure, message):
                    SMOKE.read_estate(None, self.repo, self.environment, self.scratch, "0.9.5", [])

    def test_read_estate_requires_an_explicitly_unapplied_upgrade_plan(self):
        for payload in ({"applied": True}, {}):
            responses = [(0, {}), (0, {}), (0, payload)]
            with self.subTest(payload=payload), patch.object(SMOKE, "run_cli", side_effect=responses):
                with self.assertRaisesRegex(SMOKE.SmokeFailure, "unapplied plan"):
                    SMOKE.read_estate(None, self.repo, self.environment, self.scratch, "0.9.5", [])

    def test_smoke_refuses_a_dirty_tree_even_when_the_write_is_ignored(self):
        (self.repo / ".gitignore").write_text("candidate-cache/\n", encoding="utf-8")
        self.commit_fixture()
        pin = self.fixture_pin()
        expected = [dict(finding, commit=pin["commit"]) for finding in self.example["expectedReadFindings"]]
        entry = dict(self.example, repositories=[pin], legs=["read"], expectedReadFindings=expected)
        execute = SMOKE.execute

        def write_cache(cli, clone, environment, args):
            if args == ["estate", "check"]:
                cache = clone / "candidate-cache"
                cache.mkdir()
                (cache / "unexpected").touch()
            return 0, {"applied": False}

        def execute_step(argv, cwd, environment, timeout=120):
            if argv[0] == "git":
                return execute(argv, cwd, environment, timeout)
            # Stub the final regression subprocess so a missing assertion cannot nest this test.
            return subprocess.CompletedProcess(argv, 0)

        with (patch.object(SMOKE, "examples", return_value=[entry]),
              patch.object(SMOKE, "offline_inputs"),
              patch.object(SMOKE, "bundled_cli"),
              patch.object(SMOKE, "run_cli", side_effect=write_cache),
              patch.object(SMOKE, "execute", side_effect=execute_step)):
            with self.assertRaisesRegex(SMOKE.SmokeFailure, "tree is no longer clean"):
                SMOKE.smoke(self.scratch, "0.9.5", "fixture", pin["commit"], {}, self.repo)


if __name__ == "__main__":
    unittest.main()
