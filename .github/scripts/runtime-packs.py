# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

"""Add and verify the runtime and apphost packs resolved by each finished publish."""

import json
from pathlib import Path
import re
import sys

RIDS = ("linux-x64", "linux-arm64", "osx-x64", "osx-arm64", "win-x64", "win-arm64")
PROJECTS = {"Vericairn.Cli": "vericairn", "Vericairn.Workbench": "vericairn-workbench"}
BUNDLED_IN = "vericairn:bundled-in"


def packs(directory):
    found = {}
    for project, product in PROJECTS.items():
        for rid in RIDS:
            evidence = Path(directory) / project / f"{rid}.txt"
            if not evidence.is_file():
                raise ValueError(f"Missing bundled-pack evidence: {project} {rid}")
            entries = set()
            for line in evidence.read_text(encoding="utf-8-sig").splitlines():
                kind, name, version = line.split("|")
                name = name.lower()
                valid_names = {"runtime": {f"microsoft.netcore.app.runtime.{rid}", f"microsoft.aspnetcore.app.runtime.{rid}"},
                               "apphost": {f"microsoft.netcore.app.host.{rid}"}}
                if name not in valid_names.get(kind, set()) or not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?", version):
                    raise ValueError(f"Invalid bundled-pack identity: {project} {rid}")
                if (name, version) in entries or any(entry[0] == name for entry in entries):
                    raise ValueError(f"Duplicate bundled-pack identity: {project} {rid}")
                entries.add((name, version))
            required = {f"microsoft.netcore.app.runtime.{rid}", f"microsoft.netcore.app.host.{rid}"}
            if project == "Vericairn.Cli":
                required.add(f"microsoft.aspnetcore.app.runtime.{rid}")
            if not required.issubset({name for name, _ in entries}):
                raise ValueError(f"Incomplete runtime and apphost evidence: {project} {rid}")
            for entry in entries:
                found.setdefault(entry, set()).add(f"{product}/{rid}")
                # Workbench ships the CLI beside its own executable, including the CLI's embedded packs.
                if project == "Vericairn.Cli":
                    found[entry].add(f"vericairn-workbench/{rid}")
    return found


def verify(document, directory):
    expected = packs(directory)
    named = {}
    for component in document.get("components", []):
        key = (component["name"].lower(), component["version"])
        if key in expected:
            if key in named:
                raise ValueError("Duplicate bundled pack in the bill of materials")
            named[key] = {prop["value"] for prop in component.get("properties", []) if prop["name"] == BUNDLED_IN}
    if named != expected:
        raise ValueError("The bill of materials omits or misattributes resolved runtime or apphost packs")
    print(f"The bill of materials names {len(expected)} resolved runtime and apphost packs across all six platforms.")


def complete(bom, directory):
    document = json.loads(Path(bom).read_text(encoding="utf-8"))
    components = document.setdefault("components", [])
    for (name, version), bundles in sorted(packs(directory).items()):
        matching = [component for component in components if (component["name"].lower(), component["version"]) == (name, version)]
        if len(matching) > 1:
            raise ValueError("Duplicate bundled pack in the bill of materials")
        if matching:
            component = matching[0]
        else:
            purl = f"pkg:nuget/{name}@{version}"
            component = {"type": "library", "name": name, "version": version, "bom-ref": purl, "purl": purl,
                         "description": ".NET pack resolved by the self-contained single-file publish; the host pack supplies the apphost."}
            components.append(component)
        properties = [prop for prop in component.get("properties", []) if prop["name"] != BUNDLED_IN]
        component["properties"] = properties + [{"name": BUNDLED_IN, "value": bundle} for bundle in sorted(bundles)]
    verify(document, directory)
    Path(bom).write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("Usage: runtime-packs.py <bom.json> <runtime-pack-evidence-directory>")
    complete(sys.argv[1], sys.argv[2])
