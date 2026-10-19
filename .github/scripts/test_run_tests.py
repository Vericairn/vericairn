# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

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
# filter and exits as the scenario says. Output imitates the test platform's lines, colour codes included.
FAKE = textwrap.dedent('''\
    #!/usr/bin/env python3
    import os, sys
    scenario, state = os.environ["FAKE_SCENARIO"], os.environ["FAKE_STATE"]
    args = sys.argv[1:]
    def failed(name, assembly="Vericairn.Cli.Tests"):
        print("\\x1b[31mfailed " + name + " (12ms)\\x1b[m")
        print("  from /work/artifacts/bin/" + assembly + "/release/" + assembly + ".dll (net10.0|x64)")
    def errors(count, assembly="Vericairn.Cli.Tests"):
        print("/work/artifacts/bin/" + assembly + "/release/" + assembly + ".dll (net10.0|x64) failed with " + str(count) + " error(s) (1m 2s)")
    if "--filter-method" in args:
        with open(os.path.join(state, "retries"), "a") as out:
            out.write(args[args.index("--filter-method") + 1] + " " + args[args.index("--project") + 1] + "\\n")
        sys.exit(2 if scenario == "persistent" else 0)
    if scenario == "pass":
        sys.exit(0)
    if scenario == "unicode":
        sys.stdout.buffer.write("skipped Vericairn.Records.Tests.TextTests.Keeps(text: \\"Cafe\\u0301\\") \\u2713\\n".encode("utf-8")); sys.exit(0)
    if scenario in ("flaky", "persistent"):
        failed("Vericairn.Cli.Tests.SoftwareJourneyTests.J04_Route"); errors(1); sys.exit(2)
    if scenario == "crash":
        failed("Vericairn.Cli.Tests.SoftwareJourneyTests.J04_Route"); errors(2); sys.exit(2)
    if scenario == "many":
        for i in range(6): failed("Vericairn.Cli.Tests.ManyTests.Case" + str(i))
        errors(6); sys.exit(2)
    if scenario == "theory":
        failed('Vericairn.Runtime.Tests.SdkTests.Preserves(kind: "json")', "Vericairn.Runtime.Tests")
        failed('Vericairn.Runtime.Tests.SdkTests.Preserves(kind: "io")', "Vericairn.Runtime.Tests")
        errors(2, "Vericairn.Runtime.Tests"); sys.exit(2)
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

    def run_scenario(self, scenario, **extra):
        env = dict(os.environ, FAKE_SCENARIO=scenario, FAKE_STATE=str(self.root), GITHUB_STEP_SUMMARY=str(self.summary),
                   PATH=str(self.root / "bin") + os.pathsep + os.environ["PATH"], **extra)
        result = subprocess.run([sys.executable, str(SCRIPT)], cwd=self.root, env=env, capture_output=True, text=True,
                                encoding="utf-8")
        retries = (self.root / "retries").read_text().splitlines() if (self.root / "retries").exists() else []
        summary = self.summary.read_text() if self.summary.exists() else ""
        return result.returncode, result.stdout, retries, summary

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
