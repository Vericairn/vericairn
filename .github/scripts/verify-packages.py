# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

import hashlib
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET
import zipfile

ICON_NAME = "package-icon.png"
ICON = Path(__file__).resolve().parents[2] / "brand" / ICON_NAME


def verify_icon(path, expected_icon):
    try:
        with zipfile.ZipFile(path) as package:
            names = package.namelist()
            nuspecs = [name for name in names if name.endswith(".nuspec") and "/" not in name]
            if len(nuspecs) != 1:
                raise SystemExit(f"Package icon requires exactly one nuspec: {path.name}")
            metadata = ET.fromstring(package.read(nuspecs[0]))
            icons = metadata.findall("./{*}metadata/{*}icon")
            if len(icons) != 1 or icons[0].text != ICON_NAME:
                raise SystemExit(f"Package icon must be named as {ICON_NAME} in the nuspec: {path.name}")
            if names.count(ICON_NAME) != 1 or package.read(ICON_NAME) != expected_icon:
                raise SystemExit(f"Package icon must embed the brand's {ICON_NAME} exactly once: {path.name}")
    except (zipfile.BadZipFile, ET.ParseError) as error:
        raise SystemExit(f"Cannot verify package icon in {path.name}: {error}") from error


def verify(directory):
    manifest = {}
    for line in (directory / "SHA256SUMS").read_text().splitlines():
        match = re.fullmatch(r"([a-fA-F0-9]{64}) [ *]([A-Za-z0-9][A-Za-z0-9._-]*)", line)
        if not match or match[2] in manifest:
            raise SystemExit(f"Malformed or duplicate checksum entry: {line}")
        manifest[match[2]] = match[1].lower()
    expected = {name for name in manifest if name.endswith((".nupkg", ".snupkg"))}
    actual = {path.name for path in directory.iterdir() if path.suffix in (".nupkg", ".snupkg")}
    if expected != actual or not any(name.endswith(".nupkg") for name in actual) or not any(name.endswith(".snupkg") for name in actual):
        raise SystemExit("Package assets and checksums must match, including packages and symbols.")
    expected_icon = ICON.read_bytes()
    for name in sorted(actual):
        path = directory / name
        if path.is_symlink() or not path.is_file():
            raise SystemExit(f"Not a regular package file: {name}")
        if name.endswith(".snupkg") and name.removesuffix(".snupkg") + ".nupkg" not in actual:
            raise SystemExit(f"Symbol package has no matching package: {name}")
        with path.open("rb") as content:
            digest = hashlib.file_digest(content, "sha256").hexdigest()
        if digest != manifest[name]:
            raise SystemExit(f"Checksum mismatch: {name}")
        if name.endswith(".nupkg"):
            verify_icon(path, expected_icon)
        print(f"Verified {name}")


if __name__ == "__main__":
    verify(Path(sys.argv[1] if len(sys.argv) > 1 else "packages"))
