# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

"""The test classes that read the repository's own documentation, for the documentation check of a pull request that
changes only documentation.

A test source reads it when it finds the repository root, as every such test does, by looking for Vericairn.slnx upward
from its build output, and names docs/, README.md or ROADMAP.md. Every class the source declares at its top level and test
discovery lists is selected. The selection can only be wider than the tests that read documentation, never narrower than
those the sources show; the merge queue's full gate then runs every test on every platform before anything merges.
"""

from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT_MARKER = '"Vericairn.slnx"'
READS = re.compile(r'"(?:docs|README\.md|ROADMAP\.md)"|"docs/')
NAMESPACE = re.compile(r"^namespace\s+([\w.]+)\s*;", re.MULTILINE)
DECLARED = re.compile(r"^(?:(?:public|internal|sealed|static|partial|abstract)\s+)*class\s+(\w+)", re.MULTILINE)


def documentation_sources(root):
    """{test project name: [(source, declared class names)]} for the sources that read the repository's documentation."""
    found = {}
    for project in sorted((root / "tests").rglob("*.csproj")):
        if ET.parse(project).findtext(".//IsTestProject") != "true":
            continue
        for source in sorted(project.parent.rglob("*.cs")):
            if any(part in ("bin", "obj") for part in source.relative_to(project.parent).parts):
                continue
            text = source.read_text(encoding="utf-8", errors="replace")
            if ROOT_MARKER not in text or not READS.search(text):
                continue
            namespace = NAMESPACE.search(text)
            prefix = namespace.group(1) + "." if namespace else ""
            names = sorted({prefix + name for name in DECLARED.findall(text)})
            if names:
                found.setdefault(project.stem, []).append((source.relative_to(root).as_posix(), names))
    return found


def documentation_classes(root, discovered):
    """{assembly: sorted test classes} that read the documentation, limited to the classes discovery lists.
    discovered maps each assembly name to its discovered classes."""
    selected = {}
    for assembly, sources in documentation_sources(Path(root)).items():
        classes = sorted({name for _, names in sources for name in names} & set(discovered.get(assembly, ())))
        if classes:
            selected[assembly] = classes
    if not selected:
        raise ValueError("No test class reads the repository's documentation; the documentation check would run nothing.")
    return selected
