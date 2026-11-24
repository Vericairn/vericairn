# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

"""Decide which of ci.yml's jobs an event runs, as GitHub step outputs (name=value lines on standard output).

A merge queue group, a push and a manual run get the full gate: the three-platform build-test with every share the shard
plan gives each platform, both Remote builds and the packaging path. A pull request gets the light run: Linux build-test
in its shares, or, when the pull request changes only documentation, the documentation check instead. The queue then runs
the full gate on the merged result before main or a release branch moves, so nothing reaches either untested.

Documentation is every file under docs/ except the frozen release sets under docs/releases/, and the root README.md and
ROADMAP.md. A pull request's files are the merge commit's difference from its first parent, the base it was tested on, with
renames listed as a deletion and an addition, so a file moved out of the product into docs/ still counts as a product change.
Anything that cannot be read counts as a product change.
"""

import argparse
import json
from pathlib import Path
import subprocess
import sys

PLAN = Path(".github") / "test-shards.json"
PLATFORMS = ("ubuntu-latest", "macos-latest", "windows-latest")
LIGHT = ("ubuntu-latest",)
DOCUMENTATION_FILES = frozenset({"README.md", "ROADMAP.md"})


def is_documentation(path):
    return path in DOCUMENTATION_FILES or (path.startswith("docs/") and not path.startswith("docs/releases/"))


def documentation_only(paths):
    return bool(paths) and all(is_documentation(path) for path in paths)


def changed_files():
    """The files the checked-out pull request merge commit changes against its base, or None when git cannot say."""
    result = subprocess.run(["git", "diff", "--name-only", "--no-renames", "-z", "HEAD^1", "HEAD"],
                            capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode:
        return None
    return [path for path in result.stdout.split("\0") if path]


def shares(root):
    """Each platform's share count from the shard plan, checked."""
    plan = json.loads((root / PLAN).read_text(encoding="utf-8"))
    counts = {}
    for platform in PLATFORMS:
        count = plan["platforms"][platform]["shares"]
        if not isinstance(count, int) or isinstance(count, bool) or not 1 <= count <= 12:
            raise ValueError(f"{platform}: shares must be a whole number from 1 to 12, not {count!r}.")
        counts[platform] = count
    if set(plan["platforms"]) != set(PLATFORMS):
        raise ValueError("The shard plan must name exactly " + ", ".join(PLATFORMS) + ".")
    return counts


def plan(event, paths, counts):
    full = event != "pull_request"
    docs = not full and paths is not None and documentation_only(paths)
    platforms = PLATFORMS if full else LIGHT
    matrix = {"include": [{"os": platform, "share": share} for platform in platforms for share in range(1, counts[platform] + 1)]}
    return {"full": full, "build": not docs, "docs": docs, "matrix": matrix}


def outputs(decided):
    return [f"full={str(decided['full']).lower()}", f"build={str(decided['build']).lower()}",
            f"docs={str(decided['docs']).lower()}", "matrix=" + json.dumps(decided["matrix"], separators=(",", ":"))]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--event", required=True, help="github.event_name")
    args = parser.parse_args()
    try:
        counts = shares(Path.cwd())
    except (OSError, KeyError, TypeError, ValueError) as error:
        print(f"The shard plan is unusable: {error}", file=sys.stderr)
        return 1
    paths = changed_files() if args.event == "pull_request" else []
    decided = plan(args.event, paths, counts)
    if args.event == "pull_request":
        listed = "unreadable" if paths is None else f"{len(paths)} changed files"
        kind = "documentation only" if decided["docs"] else "a product change"
        print(f"Pull request: {listed}, {kind}.", file=sys.stderr)
    for line in outputs(decided):
        print(line)
    return 0


if __name__ == "__main__":
    sys.exit(main())
