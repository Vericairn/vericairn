# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

import json
import os
from pathlib import Path
import stat
import subprocess
import sys
import tempfile
import textwrap
import unittest

SCRIPT = Path(__file__).with_name("run-tests.py")

# A stand-in dotnet: the first run prints the scenario's output and exit code; a retry (--filter-method) records its
# filter and exits as the scenario says. Output imitates the test platform's lines, colour codes included: each failed
# test, each assembly's result as it finishes, and a summary that repeats every assembly's result.
FAKE = textwrap.dedent('''\
    #!/usr/bin/env python3
    import json, os, sys
    scenario, state = os.environ["FAKE_SCENARIO"], os.environ["FAKE_STATE"]
    args = sys.argv[1:]
    def failed(name, assembly="Vericairn.Cli.Tests"):
        print("\\x1b[31mfailed " + name + " (12ms)\\x1b[m")
        print("  from /work/artifacts/bin/" + assembly + "/release/" + assembly + ".dll (net10.0|x64)")
    def result(outcome, assembly="Vericairn.Cli.Tests"):
        return "/work/artifacts/bin/" + assembly + "/release/" + assembly + ".dll (net10.0|x64) " + outcome
    def finish(results, failures, code=2):
        for line in results: print(line)
        print(""); print("Test run summary: Failed!")
        for line in results: print("  " + line)
        print(""); print("  total: 40"); print("  failed: " + str(failures)); print("  succeeded: " + str(40 - failures))
        print("Test run completed with non-success exit code: " + str(code)); sys.exit(code)
    passed = result("passed (5s 1ms)", "Vericairn.Records.Tests")
    if "--list-tests" in args:
        if scenario == "discovery-failed": sys.exit(1)
        names = ["Vericairn.Cli.Tests.Class" + str(i) for i in range(30)]
        if "--filter-class" in args and scenario != "ignored-filter":
            names = [name for name in names if name in args[args.index("--filter-class") + 1:]]
        print(json.dumps({"schemaVersion": 1, "tests": [{"type": {
            "assemblyFullName": "Vericairn.Cli.Tests, Version=1.0.0.0",
            "namespace": "Vericairn.Cli.Tests", "typeName": name.rsplit(".", 1)[1]}} for name in names]}))
        sys.exit(0)
    if "--filter-method" in args:
        with open(os.path.join(state, "retries"), "a") as out:
            out.write(args[args.index("--filter-method") + 1] + " " + args[args.index("--project") + 1] + "\\n")
        sys.exit(2 if scenario in ("persistent", "share-persistent") else 0)
    with open(os.path.join(state, "commands"), "a") as out:
        out.write(json.dumps(args) + "\\n")
    if scenario in ("share-flaky", "share-persistent"):
        failed(args[args.index("--filter-class") + 1] + ".Case")
        finish([result("failed with 1 error(s) (1m 2s)")], 1)
    if scenario == "pass":
        sys.exit(0)
    if scenario == "unicode":
        sys.stdout.buffer.write("skipped Vericairn.Records.Tests.TextTests.Keeps(text: \\"Cafe\\u0301\\") \\u2713\\n".encode("utf-8")); sys.exit(0)
    if scenario in ("flaky", "persistent"):
        failed("Vericairn.Cli.Tests.SoftwareJourneyTests.J04_Route"); finish([result("failed with 1 error(s) (1m 2s)"), passed], 1)
    if scenario == "crash":
        failed("Vericairn.Cli.Tests.SoftwareJourneyTests.J04_Route"); finish([result("failed with 2 error(s) (1m 2s)"), passed], 2)
    if scenario == "unfinished":
        failed("Vericairn.Cli.Tests.SoftwareJourneyTests.J04_Route")
        finish([result("failed with 1 error(s) (1m 2s)"), result("crashed (3s)", "Vericairn.Runtime.Tests")], 1)
    if scenario == "aborted":
        failed("Vericairn.Cli.Tests.SoftwareJourneyTests.J04_Route"); finish([result("failed with 1 error(s) (1m 2s)")], 1, code=3)
    if scenario == "uncounted":
        failed("Vericairn.Cli.Tests.SoftwareJourneyTests.J04_Route"); finish([result("failed with 1 error(s) (1m 2s)"), passed], 2)
    if scenario == "many":
        for i in range(6): failed("Vericairn.Cli.Tests.ManyTests.Case" + str(i))
        finish([result("failed with 6 error(s) (1m 2s)")], 6)
    if scenario == "theory":
        failed('Vericairn.Runtime.Tests.SdkTests.Preserves(kind: "json")', "Vericairn.Runtime.Tests")
        failed('Vericairn.Runtime.Tests.SdkTests.Preserves(kind: "io")', "Vericairn.Runtime.Tests")
        finish([result("failed with 2 error(s) (1m 2s)", "Vericairn.Runtime.Tests"), passed], 2)
    print("error: the build output is missing"); sys.exit(1)
    ''')


class RetryRunnerTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        fake = self.root / "bin" / "dotnet"
        fake.parent.mkdir()
        fake.write_text(FAKE)
        fake.chmod(fake.stat().st_mode | stat.S_IXUSR)
        self.summary = self.root / "summary.md"

    def run_scenario(self, scenario, *arguments, **extra):
        for path in (self.root / "retries", self.root / "commands", self.summary):
            if path.exists():
                path.unlink()
        env = dict(os.environ, FAKE_SCENARIO=scenario, FAKE_STATE=str(self.root), GITHUB_STEP_SUMMARY=str(self.summary),
                   PATH=str(self.root / "bin") + os.pathsep + os.environ["PATH"], **extra)
        result = subprocess.run([sys.executable, str(SCRIPT), *arguments], cwd=self.root, env=env, capture_output=True, text=True,
                                encoding="utf-8")
        retries = (self.root / "retries").read_text().splitlines() if (self.root / "retries").exists() else []
        summary = self.summary.read_text() if self.summary.exists() else ""
        return result.returncode, result.stdout, retries, summary

    def prepare_share_project(self):
        project = self.root / "tests" / "Vericairn.Cli.Tests" / "Vericairn.Cli.Tests.csproj"
        project.parent.mkdir(parents=True)
        project.write_text("<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>")
        (self.root / "Vericairn.slnx").write_text(
            '<Solution><Project Path="tests/Vericairn.Cli.Tests/Vericairn.Cli.Tests.csproj" /></Solution>')
        assembly = self.root / "artifacts" / "bin" / "Vericairn.Cli.Tests" / "release" / "Vericairn.Cli.Tests.dll"
        assembly.parent.mkdir(parents=True)
        assembly.touch()

    def test_every_share_runs_its_classes_once_and_reports_its_retry(self):
        self.prepare_share_project()
        selected = []
        for share in (1, 2, 3):
            with self.subTest(share=share):
                code, out, retries, summary = self.run_scenario("share-flaky", "--share", str(share))
                self.assertEqual(0, code, out)
                [command] = [json.loads(line) for line in (self.root / "commands").read_text().splitlines()]
                self.assertEqual(["test", "--no-build", "--configuration", "Release", "--", "--filter-class"], command[:6])
                classes = command[6:]
                self.assertTrue(classes)
                selected.extend(classes)
                self.assertEqual([classes[0] + ".Case " + os.path.join("tests", "Vericairn.Cli.Tests")], retries)
                self.assertIn("::warning title=Retried test::", out)
                self.assertIn("passed when retried; fix its cause", summary)
        self.assertCountEqual(["Vericairn.Cli.Tests.Class" + str(i) for i in range(30)], selected)

    def test_a_persistent_failure_still_fails_each_share(self):
        self.prepare_share_project()
        for share in (1, 2, 3):
            with self.subTest(share=share):
                code, out, retries, summary = self.run_scenario("share-persistent", "--share", str(share))
                self.assertEqual(1, code, out)
                self.assertEqual(1, len(retries))
                self.assertIn("::error title=Failed twice::", out)
                self.assertIn("failed again", summary)

    def test_a_bad_guard_stops_before_any_test_or_retry_runs(self):
        self.prepare_share_project()
        for scenario in ("discovery-failed", "ignored-filter"):
            with self.subTest(scenario=scenario):
                code, out, retries, summary = self.run_scenario(scenario, "--share", "1")
                self.assertEqual(1, code)
                self.assertIn("Test share guard failed:", out)
                self.assertEqual(([], ""), (retries, summary))
                self.assertFalse((self.root / "commands").exists())

    def test_checking_shares_does_not_execute_tests(self):
        self.prepare_share_project()
        code, out, retries, summary = self.run_scenario("pass", "--check-shares")
        self.assertEqual(0, code, out)
        self.assertEqual(([], ""), (retries, summary))
        self.assertFalse((self.root / "commands").exists())

    def test_an_invalid_share_cannot_run_unfiltered_tests(self):
        for share in ("0", "4"):
            with self.subTest(share=share):
                code, _, retries, _ = self.run_scenario("pass", "--share", share)
                self.assertEqual(2, code)
                self.assertEqual([], retries)
                self.assertFalse((self.root / "commands").exists())

    def test_a_clean_run_passes_without_retrying(self):
        code, _, retries, summary = self.run_scenario("pass")
        self.assertEqual((0, [], ""), (code, retries, summary))

    def test_a_failure_cleared_by_one_retry_passes_and_is_reported(self):
        code, out, retries, summary = self.run_scenario("flaky")
        self.assertEqual(0, code)
        self.assertEqual(["Vericairn.Cli.Tests.SoftwareJourneyTests.J04_Route " + os.path.join("tests", "Vericairn.Cli.Tests")], retries)
        self.assertIn("::warning title=Retried test::Vericairn.Cli.Tests.SoftwareJourneyTests.J04_Route", out)
        self.assertIn("`Vericairn.Cli.Tests.SoftwareJourneyTests.J04_Route`: passed when retried; fix its cause", summary)

    def test_a_test_that_fails_twice_fails_the_run(self):
        code, out, retries, summary = self.run_scenario("persistent")
        self.assertEqual(1, code)
        self.assertEqual(1, len(retries))
        self.assertIn("::error title=Failed twice::", out)
        self.assertIn("failed again", summary)

    def test_errors_the_named_failures_do_not_account_for_are_not_retried(self):
        code, out, retries, summary = self.run_scenario("crash")
        self.assertEqual((2, [], ""), (code, retries, summary))
        self.assertIn("Not retried", out)

    def test_an_assembly_that_did_not_finish_is_not_retried(self):
        code, out, retries, summary = self.run_scenario("unfinished")
        self.assertEqual((2, [], ""), (code, retries, summary))
        self.assertIn("Not retried", out)

    def test_a_run_that_ended_for_another_reason_is_not_retried(self):
        code, out, retries, summary = self.run_scenario("aborted")
        self.assertEqual((3, [], ""), (code, retries, summary))
        self.assertIn("Not retried", out)

    def test_a_failed_total_beyond_the_named_failures_is_not_retried(self):
        code, out, retries, summary = self.run_scenario("uncounted")
        self.assertEqual((2, [], ""), (code, retries, summary))
        self.assertIn("Not retried", out)

    def test_more_failures_than_an_intermittent_failure_explains_are_not_retried(self):
        code, out, retries, _ = self.run_scenario("many")
        self.assertEqual((2, []), (code, retries))
        self.assertIn("6 tests failed", out)

    def test_a_theory_with_several_failed_rows_is_retried_once_by_its_method(self):
        code, _, retries, _ = self.run_scenario("theory")
        self.assertEqual(0, code)
        self.assertEqual(["Vericairn.Runtime.Tests.SdkTests.Preserves " + os.path.join("tests", "Vericairn.Runtime.Tests")], retries)

    def test_output_the_console_code_page_cannot_encode_is_echoed_as_utf8(self):
        # A Windows runner's console code page, imitated on any platform.
        code, out, _, _ = self.run_scenario("unicode", PYTHONIOENCODING="cp1252")
        self.assertEqual(0, code)
        self.assertIn("Cafe\u0301\") \u2713", out)

    def test_a_failure_that_names_no_test_is_not_retried(self):
        code, out, retries, _ = self.run_scenario("unnamed")
        self.assertEqual((1, []), (code, retries))
        self.assertIn("Not retried", out)


if __name__ == "__main__":
    unittest.main()
