# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

"""Fetch the latest stable published version of every library the repository packs.

The SDK validates against these exact packages during pack. Discovery or download
failures stop packaging; an unavailable baseline is never a compatibility pass.
"""

import hashlib
import json
from pathlib import Path
import re
import sys
from urllib.request import urlopen
import xml.etree.ElementTree as ET
import zipfile

FEED = "https://api.nuget.org/v3-flatcontainer"


def libraries(root):
    found = {}
    for path in sorted((Path(root) / "src").glob("*/*.csproj")):
        project = ET.parse(path).getroot()
        packable = project.findtext(".//IsPackable")
        if packable not in ("true", "false"):
            raise ValueError(f"Every source project must declare IsPackable explicitly: {path.name}")
        if packable == "false" or project.findtext(".//PackAsTool") == "true":
            continue
        name = project.findtext(".//PackageId")
        if not name or not re.fullmatch(r"[A-Za-z0-9_.-]+", name) or name.lower() in found:
            raise ValueError(f"Missing, invalid or duplicate library PackageId in {path.name}")
        found[name.lower()] = name
    if not found:
        raise ValueError("No packable libraries found")
    return sorted(found.values())


def latest(versions):
    stable = [version for version in versions if re.fullmatch(r"\d+\.\d+\.\d+(?:\.\d+)?", version)]
    if not stable:
        raise ValueError("No stable published baseline")
    return max(stable, key=lambda version: tuple(map(int, version.split("."))) + (0,) * (4 - len(version.split("."))))


def download(url):
    with urlopen(url, timeout=60) as response:
        return response.read()


def prepare(root, output, fetch=download):
    output = Path(output)
    output.mkdir(parents=True, exist_ok=True)
    manifest = []
    for name in libraries(root):
        package = name.lower()
        version = latest(json.loads(fetch(f"{FEED}/{package}/index.json"))["versions"])
        filename = f"{package}.{version}.nupkg"
        data = fetch(f"{FEED}/{package}/{version}/{filename}")
        path = output / filename
        path.write_bytes(data)
        # A successful HTTP response alone is no evidence of the requested package.
        with zipfile.ZipFile(path) as archive:
            [nuspec] = [entry for entry in archive.namelist() if entry.endswith(".nuspec")]
            metadata = ET.fromstring(archive.read(nuspec)).find("{*}metadata")
            if metadata is None or (metadata.findtext("{*}id").lower(), metadata.findtext("{*}version")) != (package, version):
                raise ValueError(f"Baseline identity does not match {name} {version}")
        props = ET.Element("Project")
        group = ET.SubElement(props, "PropertyGroup")
        ET.SubElement(group, "PackageValidationBaselinePath").text = "$(MSBuildThisFileDirectory)" + filename
        ET.SubElement(group, "PackageValidationBaselineVersion").text = version
        ET.ElementTree(props).write(output / f"{name}.props", encoding="utf-8", xml_declaration=True)
        manifest.append({"package": name, "version": version, "sha256": hashlib.sha256(data).hexdigest()})
        print(f"API baseline: {name} {version}")
    (output / "baselines.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: api-baselines.py <output-directory>")
    prepare(Path(__file__).resolve().parents[2], sys.argv[1])
