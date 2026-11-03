# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

"""Name the packaged release and add and verify the packs resolved by each finished publish."""

import argparse
import json
from pathlib import Path
import re

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


def complete(bom, directory, release_version):
    document = json.loads(Path(bom).read_text(encoding="utf-8"))
    component = document.setdefault("metadata", {}).setdefault("component", {"type": "application"})
    component["name"] = "Vericairn"
    component["version"] = release_version
    # The generator's reference reads Vericairn@0.0.0; the release's reference and every dependency entry naming it move together.
    reference, component["bom-ref"] = component.get("bom-ref"), f"Vericairn@{release_version}"
    for dependency in document.get("dependencies", []):
        if dependency.get("ref") == reference:
            dependency["ref"] = component["bom-ref"]
        dependency["dependsOn"] = [component["bom-ref"] if ref == reference else ref for ref in dependency.get("dependsOn", [])]
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
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("bom")
    parser.add_argument("runtime_packs")
    parser.add_argument("--version", required=True)
    args = parser.parse_args()
    complete(args.bom, args.runtime_packs, args.version)
