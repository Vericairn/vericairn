# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

"""Three platform-independent shares of the built test classes, checked against test discovery."""

from collections import Counter
import hashlib
import json
import re
import subprocess
import xml.etree.ElementTree as ET

COUNT = 3
CLASS = re.compile(r"[\w`]+(?:[.+][\w`]+)*")


def partition(classes):
    """A class stays in the same share on every platform, including when other classes are added."""
    shares = [[] for _ in range(COUNT)]
    for name in sorted(classes):
        shares[int(hashlib.sha256(name.encode("utf-8")).hexdigest(), 16) % COUNT].append(name)
    return shares


def check_partition(classes, shares):
    """Refuse missing, repeated or unknown classes, including repetitions within one share."""
    if len(shares) != COUNT:
        raise ValueError(f"Expected {COUNT} shares, found {len(shares)}.")
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


def checked_shares(root):
    """Check the partition and the test platform's actual selection before any share runs tests."""
    combined = [set() for _ in range(COUNT)]
    for assembly in test_assemblies(root):
        classes = discover(assembly)
        shares = partition(classes)
        check_partition(classes, shares)
        for index, share in enumerate(shares):
            if not share:
                raise ValueError(f"{assembly.stem}: share {index + 1} has no classes; cannot run an empty suite.")
            actual = discover(assembly, share)
            if actual != set(share):
                raise ValueError(f"{assembly.stem}: share {index + 1} class filter disagrees with discovery.")
            combined[index].update(share)
        print(f"{assembly.stem}: {len(classes)} classes exactly once; shares " + ", ".join(str(len(s)) for s in shares))
    return [sorted(share) for share in combined]
