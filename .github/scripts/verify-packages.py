# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

import hashlib
from pathlib import Path
import re
import sys


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
        print(f"Verified {name}")


if __name__ == "__main__":
    verify(Path(sys.argv[1] if len(sys.argv) > 1 else "packages"))
