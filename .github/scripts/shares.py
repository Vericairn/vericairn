# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

"""Shares of the built test classes, balanced by their measured durations and checked against test discovery.

Each test assembly's classes are split on their own, so every share runs part of every assembly and the assemblies, which
run side by side, finish together. The split is the longest-first greedy one: classes in order of measured seconds, longest
first (ties by name), each to the share with the least so far (ties to the lowest). .github/test-shards.json holds the
seconds, written by shard-durations.py from the test reports, and each platform's share count; a class it does not list yet
weighs its assembly's median. A platform's reserve starts its first share with that fraction of each assembly's total,
because that share also runs the build checks that follow the tests. The same inputs give the same shares on every machine.
"""

from collections import Counter
import json
import re
import statistics
import subprocess
import xml.etree.ElementTree as ET

COUNT = 3
PLAN = (".github", "test-shards.json")
CLASS = re.compile(r"[\w`]+(?:[.+][\w`]+)*")


def load_plan(root):
    """The shard plan, or None when the repository has none (a plain run then weighs every class alike)."""
    path = root.joinpath(*PLAN)
    if not path.is_file():
        return None
    plan = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(plan.get("platforms"), dict) or not isinstance(plan.get("classes"), dict):
        raise ValueError(f"{'/'.join(PLAN)}: expected 'platforms' and 'classes' objects.")
    return plan


def platform_settings(plan, platform):
    """(count, reserve) for a platform the plan names."""
    if plan is None or platform not in plan["platforms"]:
        raise ValueError(f"The shard plan names no platform {platform!r}.")
    entry = plan["platforms"][platform]
    count, reserve = entry.get("shares"), entry.get("reserve", 0)
    if not isinstance(count, int) or isinstance(count, bool) or not 1 <= count <= 12:
        raise ValueError(f"{platform}: shares must be a whole number from 1 to 12.")
    if not isinstance(reserve, (int, float)) or isinstance(reserve, bool) or not 0 <= reserve < 1:
        raise ValueError(f"{platform}: reserve must be a fraction from 0 up to 1.")
    return count, float(reserve)


def weights_for(classes, measured):
    """Each class's measured seconds; a class without a measurement weighs the median of those with one."""
    known = [measured[name] for name in classes if isinstance(measured.get(name), (int, float)) and measured[name] > 0]
    default = statistics.median(known) if known else 1.0
    return {name: float(measured[name]) if isinstance(measured.get(name), (int, float)) and measured[name] > 0 else default
            for name in classes}


def partition(classes, count=COUNT, measured=None, reserve=0.0):
    """Longest-first greedy shares of one assembly's classes; every share gets a class when there are enough."""
    weights = weights_for(set(classes), measured or {})
    loads = [reserve * sum(weights.values())] + [0.0] * (count - 1)
    shares = [[] for _ in range(count)]
    for name in sorted(weights, key=lambda item: (-weights[item], item)):
        index = min(range(count), key=lambda share: (loads[share], share))
        shares[index].append(name)
        loads[index] += weights[name]
    # A reserve can leave the first share none of a small assembly's classes; it takes the lightest of the fullest share.
    for empty in [index for index, share in enumerate(shares) if not share]:
        donor = max(range(count), key=lambda share: (len(shares[share]), -share))
        if len(shares[donor]) < 2:
            break
        lightest = min(shares[donor], key=lambda item: (weights[item], item))
        shares[donor].remove(lightest)
        shares[empty].append(lightest)
    return [sorted(share) for share in shares]


def check_partition(classes, shares, count=COUNT):
    """Refuse missing, repeated or unknown classes, including repetitions within one share."""
    if len(shares) != count:
        raise ValueError(f"Expected {count} shares, found {len(shares)}.")
    counts = Counter(name for share in shares for name in share)
    errors = [f"{name}: in {counts[name]} shares, expected exactly one" for name in sorted(classes) if counts[name] != 1]
    errors.extend(f"{name}: not a discovered test class" for name in sorted(counts.keys() - set(classes)))
    if errors:
        raise ValueError("\n".join(errors))


def test_assemblies(root):
    """Require every test project to be in the solution and to have its Release build available."""
    projects = sorted(path for path in (root / "tests").rglob("*.csproj")
                      if ET.parse(path).findtext(".//IsTestProject") == "true")
    if not projects:
        raise ValueError("No test projects found.")
    solution = {item.attrib["Path"] for item in ET.parse(root / "Vericairn.slnx").iter("Project")}
    assemblies = []
    for project in projects:
        if project.relative_to(root).as_posix() not in solution:
            raise ValueError(f"{project.stem}: test project is not in the solution.")
        assembly = root / "artifacts" / "bin" / project.stem / "release" / (project.stem + ".dll")
        if not assembly.is_file():
            raise ValueError(f"{project.stem}: missing Release build; build the solution before checking shares.")
        assemblies.append(assembly)
    return assemblies


def discover(assembly, selected=None):
    # JSON supplies type metadata independently of test display names. Do not enumerate theory data just to list classes.
    command = ["dotnet", str(assembly), "--list-tests", "json", "--pre-enumerate-theories", "off"]
    if selected is not None:
        if not selected:
            raise ValueError(f"{assembly.stem}: empty class filter would run the whole assembly.")
        command += ["--filter-class", *selected]
    result = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode:
        raise ValueError(f"{assembly.stem}: discovery failed with exit code {result.returncode}.")
    try:
        document = json.loads(result.stdout)
        if document["schemaVersion"] != 1 or not isinstance(document["tests"], list) or not document["tests"]:
            raise ValueError("Expected a nonempty version 1 discovery document.")
        classes = set()
        for test in document["tests"]:
            kind = test["type"]
            if kind["assemblyFullName"].split(",")[0] != assembly.stem:
                raise ValueError("Discovered a test from a different assembly.")
            name = ".".join(part for part in (kind["namespace"], kind["typeName"]) if part)
            if not CLASS.fullmatch(name):
                raise ValueError("Discovered a class that cannot be used as an exact class filter.")
            classes.add(name)
        return classes
    except (KeyError, TypeError, ValueError, AttributeError) as error:
        raise ValueError(f"{assembly.stem}: invalid test discovery: {error}") from error


def checked_shares(root, platform=None):
    """Check the partition and the test platform's actual selection before any share runs tests. With a platform, its
    share count and reserve come from the shard plan; without one, three shares and no reserve, as the release runs."""
    plan = load_plan(root)
    count, reserve = platform_settings(plan, platform) if platform is not None else (COUNT, 0.0)
    combined = [set() for _ in range(count)]
    for assembly in test_assemblies(root):
        classes = discover(assembly)
        measured = (plan or {}).get("classes", {}).get(assembly.stem, {})
        shares = partition(classes, count, measured, reserve)
        check_partition(classes, shares, count)
        for index, share in enumerate(shares):
            if not share:
                raise ValueError(f"{assembly.stem}: share {index + 1} has no classes; cannot run an empty suite.")
            actual = discover(assembly, share)
            if actual != set(share):
                raise ValueError(f"{assembly.stem}: share {index + 1} class filter disagrees with discovery.")
            combined[index].update(share)
        weights = weights_for(classes, measured)
        seconds = ", ".join(f"{sum(weights[name] for name in share):.0f}s" for share in shares)
        print(f"{assembly.stem}: {len(classes)} classes exactly once; shares " + ", ".join(str(len(s)) for s in shares)
              + f"; measured {seconds}")
    return [sorted(share) for share in combined]
