# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

"""Write the shard plan's measured seconds from xUnit XML test reports.

Each report's assembly is its <assembly name>, and a class's seconds are the sum of its tests' times. A class reported more
than once, as by every platform's build-test reports of one run, gets the mean of its reports. Assemblies the reports do
not cover keep their seconds; each covered assembly's classes are replaced, so classes that no longer exist drop out. The
platforms' share counts and reserves are left as they are.

    gh run download <run> --pattern 'test-reports-*' --dir <reports>
    python3 .github/scripts/shard-durations.py <reports> [--plan .github/test-shards.json]
"""

import argparse
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET


def measure(reports):
    """{assembly: {class: mean seconds}} from every *.xml report under the given paths."""
    sums = {}
    for path in sorted(file for report in reports for file in ([Path(report)] if Path(report).is_file() else Path(report).rglob("*.xml"))):
        root = ET.parse(path).getroot()
        for assembly in root.iter("assembly"):
            name = Path(assembly.get("name", "").replace("\\", "/")).stem
            if not name:
                raise ValueError(f"{path}: an assembly without a name.")
            seconds = {}
            for test in assembly.iter("test"):
                kind = test.get("type")
                if kind:
                    seconds[kind] = seconds.get(kind, 0.0) + float(test.get("time", "0") or 0)
            for kind, total in seconds.items():
                sums.setdefault(name, {}).setdefault(kind, []).append(total)
    if not sums:
        raise ValueError("The reports hold no test times.")
    return {name: {kind: round(sum(values) / len(values), 2) for kind, values in sorted(classes.items())}
            for name, classes in sorted(sums.items())}


def update(plan, measured):
    classes = dict(plan.get("classes", {}))
    classes.update(measured)
    plan["classes"] = dict(sorted(classes.items()))
    return plan


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("reports", nargs="+", help="Report files or directories holding them.")
    parser.add_argument("--plan", default=str(Path(".github") / "test-shards.json"))
    args = parser.parse_args()
    path = Path(args.plan)
    try:
        plan = json.loads(path.read_text(encoding="utf-8"))
        measured = measure(args.reports)
    except (OSError, ValueError, ET.ParseError) as error:
        print(f"Not written: {error}", file=sys.stderr)
        return 1
    path.write_text(json.dumps(update(plan, measured), indent=2) + "\n", encoding="utf-8")
    for name, classes in measured.items():
        print(f"{name}: {len(classes)} classes, {sum(classes.values()):.0f}s")
    return 0


if __name__ == "__main__":
    sys.exit(main())
