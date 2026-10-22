# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

import argparse
import json
from pathlib import Path
import runpy


def packages(lock):
    """Every package, with its resolved version, in every target of a NuGet lock file: the portable one and each platform's."""
    found = set()
    for target in json.loads(Path(lock).read_text(encoding="utf-8"))["dependencies"].values():
        for name, entry in target.items():
            if entry.get("type") != "Project":
                found.add((name.lower(), entry["resolved"]))
    return found


def verify(bom, locks, runtime_packs=None):
    """A bill of materials describes what ships only when it names each package the distributables' lock files hold."""
    document = json.loads(Path(bom).read_text(encoding="utf-8"))
    named = {(component["name"].lower(), component["version"]) for component in document.get("components", [])}
    expected = set().union(*(packages(lock) for lock in locks)) if locks else set()
    if not expected:
        raise SystemExit("No distributable packages to check.")
    missing = sorted(expected - named)
    if missing:
        raise SystemExit("The bill of materials omits packages the distributables ship: "
                         + ", ".join(f"{name} {version}" for name, version in missing))
    if runtime_packs is not None:
        runpy.run_path(str(Path(__file__).with_name("runtime-packs.py")))["verify"](document, runtime_packs)
    print(f"The bill of materials names all {len(expected)} packages the distributables ship.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("bom")
    parser.add_argument("locks", nargs="+")
    parser.add_argument("--runtime-packs", required=True)
    args = parser.parse_args()
    verify(args.bom, args.locks, args.runtime_packs)
