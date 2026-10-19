# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

"""Run every test suite once, then retry each failed test once, reporting every retry.

A failure that a retry clears is an intermittent failure until its cause is fixed: the run's summary and a warning name
it, so it is fixed rather than hidden. A test that fails twice fails the run. So does any failure the output does not
fully account for, such as a crashed test host, and so do more failed tests than an intermittent failure explains.
"""

import os
import re
import subprocess
import sys
from pathlib import Path

ANSI = re.compile(r"\x1b\[[0-9;]*[A-Za-z]")
FAILED = re.compile(r"^\s*failed (Vericairn\.[\w.]+?)(?:\(.*\))? \(\d")
FROM = re.compile(r"^\s*from .*[\\/](Vericairn\.\w+\.Tests)\.dll ")
ASSEMBLY_FAILED = re.compile(r"[\\/](Vericairn\.\w+\.Tests)\.dll \([^)]*\) failed with (\d+) error\(s\)")
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


def failures(lines):
    """Each failed test with the test assembly that reported it, or None unless they account for every reported error."""
    found = []
    for index, line in enumerate(lines):
        match = FAILED.match(line)
        if not match:
            continue
        assembly = next((m.group(1) for m in (FROM.match(after) for after in lines[index + 1:index + 3]) if m), None)
        if assembly is None:
            return None
        found.append((assembly, match.group(1)))
    errors = {}
    for line in lines:
        match = ASSEMBLY_FAILED.search(line)
        if match:
            errors[match.group(1)] = errors.get(match.group(1), 0) + int(match.group(2))
    reported = {}
    for assembly, _ in found:
        reported[assembly] = reported.get(assembly, 0) + 1
    if not found or reported != errors:
        return None
    return sorted(set(found))


def main():
    # Test output can hold any character; a Windows runner's default code page cannot encode them all.
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    code, lines = run(["dotnet", "test", "--no-build", "--configuration", "Release"])
    if code == 0:
        return 0
    failed = failures(lines)
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


if __name__ == "__main__":
    sys.exit(main())
