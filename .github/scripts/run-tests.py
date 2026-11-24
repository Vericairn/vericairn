# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

"""Run every test suite (or one checked share, or the documentation check), then retry each failed test once, reporting
every retry.

A failure that a retry clears is an intermittent failure until its cause is fixed: the run's summary and a warning name
it, so it is fixed rather than hidden. A test that fails twice fails the run. So does any failure the run's summary does
not fully account for, such as a crashed test host or a run that stopped for another reason, and so do more failed tests
than an intermittent failure explains.

A share is one of a platform's balanced shares of every assembly's classes (shares.py); --platform takes the platform's
share count and reserve from the shard plan, and without it there are three, as the release runs. The documentation check
runs, assembly by assembly, the test classes that read the repository's documentation (docs_classes.py). --reports writes
each assembly's xUnit XML report, whose test times shard-durations.py turns into the plan's measured seconds.
"""

import argparse
import os
import re
import subprocess
import sys
from pathlib import Path

from docs_classes import documentation_classes
from shares import COUNT, checked_shares, discover, load_plan, platform_settings, test_assemblies

ANSI = re.compile(r"\x1b\[[0-9;]*[A-Za-z]")
FAILED = re.compile(r"^\s*failed (Vericairn\.[\w.]+?)(?:\(.*\))? \(\d")
FROM = re.compile(r"^\s*from .*[\\/](Vericairn\.\w+\.Tests)\.dll ")
RESULT = re.compile(r"^\s*\S.*[\\/]([^\\/]+)\.dll \([^)]*\) (.+)$")
ERRORS = re.compile(r"^failed with (\d+) error\(s\)(?: |$)")
TOTAL_FAILED = re.compile(r"^\s*failed: (\d+)$")
ASSEMBLY = re.compile(r"^Vericairn\.\w+\.Tests$")
TESTS_FAILED = 2  # The test platform's exit code when the tests ran and some failed.
LIMIT = 5


def run(command):
    """Run a command, echo its output as it arrives, and return its exit code with its lines, colour codes removed."""
    process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace")
    lines = []
    for line in process.stdout:
        sys.stdout.write(line)
        sys.stdout.flush()
        lines.append(ANSI.sub("", line.rstrip("\r\n")))
    return process.wait(), lines


def failures(code, lines):
    """Each failed test with the test assembly that reported it, or None unless the run's summary accounts for every
    failure by a named test: the run ended because tests failed, every assembly passed or failed with counted errors, and
    the failed total is the named failures' count. The summary repeats each assembly's result, so only it is counted."""
    if code != TESTS_FAILED:
        return None
    starts = [index for index, line in enumerate(lines) if line.strip().startswith("Test run summary:")]
    if not starts:
        return None
    body, summary = lines[:starts[-1]], lines[starts[-1] + 1:]
    found = []
    for index, line in enumerate(body):
        match = FAILED.match(line)
        if not match:
            continue
        assembly = next((m.group(1) for m in (FROM.match(after) for after in body[index + 1:index + 3]) if m), None)
        if assembly is None:
            return None
        found.append((assembly, match.group(1)))
    errors, total = {}, None
    for line in summary:
        result, failed_total = RESULT.match(line), TOTAL_FAILED.match(line)
        if result:
            counted = ERRORS.match(result.group(2))
            if counted and ASSEMBLY.match(result.group(1)):
                errors[result.group(1)] = errors.get(result.group(1), 0) + int(counted.group(1))
            elif not result.group(2).startswith("passed"):
                return None
        elif failed_total:
            total = int(failed_total.group(1))
    reported = {}
    for assembly, _ in found:
        reported[assembly] = reported.get(assembly, 0) + 1
    if not found or reported != errors or total != len(found):
        return None
    return sorted(set(found))


def run_and_retry(command):
    """Run one test command, then retry each failed test once; the exit code a test step reports."""
    code, lines = run(command)
    if code == 0:
        return 0
    failed = failures(code, lines)
    if failed is None:
        print("Not retried: the output does not account for every failure by a named test.")
        return code
    if len(failed) > LIMIT:
        print(f"Not retried: {len(failed)} tests failed, more than an intermittent failure explains.")
        return code
    results = []
    for assembly, test in failed:
        print(f"::group::Retrying {test} once")
        again, _ = run(["dotnet", "test", "--project", str(Path("tests") / assembly), "--no-build", "--configuration", "Release", "--", "--filter-method", test])
        print("::endgroup::")
        results.append((test, again == 0))
    for test, passed in results:
        if passed:
            print(f"::warning title=Retried test::{test} failed once and passed when retried; it stays listed until its cause is fixed.")
        else:
            print(f"::error title=Failed twice::{test} failed again when retried.")
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as out:
            out.write("### Tests retried once\n\n")
            for test, passed in results:
                out.write(f"- `{test}`: {'passed when retried; fix its cause' if passed else 'failed again'}\n")
    return 0 if all(passed for _, passed in results) else 1


def documentation_selection(root):
    """{assembly: classes} of the documentation check, each assembly's filter checked against discovery."""
    assemblies = {assembly.stem: assembly for assembly in test_assemblies(root)}
    selected = documentation_classes(root, {name: discover(assembly) for name, assembly in assemblies.items()})
    for name, classes in selected.items():
        if discover(assemblies[name], classes) != set(classes):
            raise ValueError(f"{name}: the documentation check's class filter disagrees with discovery.")
        print(f"{name}: {len(classes)} classes read the documentation")
    return selected


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--share", type=int, help="Run one share of every assembly's classes (from 1).")
    parser.add_argument("--platform", help="Take the share count and reserve for this runner label from the shard plan.")
    parser.add_argument("--check-shares", action="store_true", help="Check discovery and every share's filter without running tests.")
    parser.add_argument("--docs", action="store_true", help="Run only the test classes that read the repository's documentation.")
    parser.add_argument("--reports", help="Write each assembly's xUnit XML report into this directory.")
    args = parser.parse_args()
    if args.docs and (args.share is not None or args.check_shares):
        parser.error("--docs runs its own selection, not a share.")
    if args.platform is not None and args.share is None and not args.check_shares:
        parser.error("--platform chooses a share plan; give --share or --check-shares.")
    # Test output can hold any character; a Windows runner's default code page cannot encode them all.
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    root = Path.cwd()
    count = COUNT
    if args.platform is not None:
        try:
            count, _ = platform_settings(load_plan(root), args.platform)
        except (OSError, ValueError) as error:
            print(f"Test share guard failed: {error}")
            return 1
    if args.share is not None and not 1 <= args.share <= count:
        parser.error(f"--share must be from 1 to {count}.")
    options = []
    if args.reports:
        options = ["--report-xunit-xml", "--results-directory", str(Path(args.reports).resolve())]
    command = ["dotnet", "test", "--no-build", "--configuration", "Release"]
    if args.docs:
        try:
            selected = documentation_selection(root)
        except (OSError, ValueError) as error:
            print(f"Documentation check guard failed: {error}")
            return 1
        codes = [run_and_retry(command + ["--project", str(Path("tests") / name), "--", "--filter-class", *classes, *options])
                 for name, classes in selected.items()]
        return next((code for code in codes if code), 0)
    if args.share is not None or args.check_shares:
        try:
            shares = checked_shares(root, args.platform)
        except (OSError, ValueError) as error:
            print(f"Test share guard failed: {error}")
            return 1
        if args.check_shares:
            return 0
        command += ["--", "--filter-class", *shares[args.share - 1], *options]
        print(f"Running share {args.share} of {count}.", flush=True)
    elif options:
        command += ["--", *options]
    return run_and_retry(command)


if __name__ == "__main__":
    sys.exit(main())
