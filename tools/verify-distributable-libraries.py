#!/usr/bin/env python3
# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

"""Compare Vericairn libraries inside the actual .NET single-file distributables.

The SDK's bundle format stores the manifest offset immediately before its fixed
signature in the native host. Format 6 is used by the pinned SDK. Read the managed
payloads without executing foreign-platform hosts or loading their assemblies.
Runtime and third-party native assets intentionally vary; Vericairn libraries do
not. Identical library bytes also hold the assembly name and module version ID
used by PolicySources.Digest equal, including the Runtime's foundation policies.
"""

import argparse
import hashlib
import mmap
from pathlib import Path
import struct
import sys
import zlib


RIDS = ("linux-x64", "linux-arm64", "osx-x64", "osx-arm64", "win-x64", "win-arm64")
BUNDLE_SIGNATURE = bytes.fromhex(
    "8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae"
)


class BundleError(ValueError):
    """A missing, unreadable or unequal distributable refuses packaging."""


def library_hashes(path):
    """Return hashes of the library payloads, failing closed on unknown formats."""
    with Path(path).open("rb") as stream:
        if not stream.seek(0, 2):
            raise BundleError(f"{path}: empty bundle")
        with mmap.mmap(stream.fileno(), 0, access=mmap.ACCESS_READ) as bundle:
            marker = bundle.find(BUNDLE_SIGNATURE)
            if marker < 8:
                raise BundleError(f"{path}: missing bundle signature")
            cursor = struct.unpack_from("<q", bundle, marker - 8)[0]
            header = cursor
            if header < marker + len(BUNDLE_SIGNATURE) or header >= len(bundle):
                raise BundleError(f"{path}: invalid bundle manifest offset")

            def read(count):
                nonlocal cursor
                if count < 0 or cursor + count > len(bundle):
                    raise BundleError(f"{path}: truncated bundle manifest")
                value = bundle[cursor:cursor + count]
                cursor += count
                return value

            def unpack(format_string):
                return struct.unpack(format_string, read(struct.calcsize(format_string)))

            def string():
                length = 0
                for shift in range(0, 35, 7):
                    byte = read(1)[0]
                    length |= (byte & 0x7f) << shift
                    if byte < 0x80:
                        return read(length).decode("utf-8")
                raise BundleError(f"{path}: invalid bundle string length")

            major, minor, count = unpack("<IIi")
            if (major, minor) != (6, 0):
                raise BundleError(f"{path}: unsupported bundle format {major}.{minor}")
            if count <= 0 or count > (len(bundle) - cursor) // 26:
                raise BundleError(f"{path}: invalid bundle entry count")
            string()  # Bundle ID, not an assembly identity.
            read(40)  # deps.json and runtimeconfig.json offsets/sizes, then flags.
            libraries = {}
            for _ in range(count):
                offset, size, compressed, kind = unpack("<qqqB")
                name = string()
                stored = compressed or size
                if min(offset, size, compressed) < 0 or offset + stored > header:
                    raise BundleError(f"{path}: invalid payload bounds for {name}")
                if not (name.startswith("Vericairn.") and name.endswith(".dll")):
                    continue
                if kind != 1 or "/" in name or "\\" in name or name in libraries:
                    raise BundleError(f"{path}: invalid or duplicate library {name}")
                payload = bundle[offset:offset + stored]
                if compressed:
                    payload = zlib.decompress(payload, -zlib.MAX_WBITS)
                if len(payload) != size:
                    raise BundleError(f"{path}: incorrect library size for {name}")
                libraries[name] = hashlib.sha256(payload).hexdigest()
            if not libraries:
                raise BundleError(f"{path}: no Vericairn library assemblies")
            return libraries


def compare(expected, actual, label):
    if expected.keys() != actual.keys():
        missing = sorted(expected.keys() - actual.keys())
        extra = sorted(actual.keys() - expected.keys())
        raise BundleError(f"{label}: library set differs; missing={missing}, extra={extra}")
    for name, digest in expected.items():
        if actual[name] != digest:
            raise BundleError(f"{label}: library bytes differ: {name}")


def verify(cli_root, workbench_root):
    """Check both applications on every RID, including Workbench's CLI copy."""
    baselines = {}
    for rid in RIDS:
        extension = ".exe" if rid.startswith("win-") else ""
        cli = library_hashes(Path(cli_root) / rid / "bin" / ("vericairn" + extension))
        desktop = library_hashes(Path(workbench_root) / rid / "bin" / ("vericairn-workbench" + extension))
        companion = library_hashes(Path(workbench_root) / rid / "bin" / ("vericairn" + extension))
        for name, libraries, required in (
            ("CLI", cli, "Vericairn.Runtime.dll"),
            ("Workbench", desktop, "Vericairn.Records.dll"),
        ):
            if required not in libraries:
                raise BundleError(f"{name}/{rid}: missing required library {required}")
            baseline = baselines.setdefault(name, libraries)
            compare(baseline, libraries, f"{name}/{rid}")
        compare(cli, companion, f"Workbench CLI/{rid}")
        for name in cli.keys() & desktop.keys():
            if cli[name] != desktop[name]:
                raise BundleError(f"{rid}: CLI and Workbench library bytes differ: {name}")
    return baselines


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("cli", type=Path, help="published CLI root, with one directory per RID")
    parser.add_argument("workbench", type=Path, help="published Workbench root, with one directory per RID")
    args = parser.parse_args(argv)
    try:
        baselines = verify(args.cli, args.workbench)
    except (BundleError, OSError, UnicodeError, zlib.error) as error:
        print(f"Distributable library check failed: {error}", file=sys.stderr)
        return 1
    for name, libraries in baselines.items():
        print(f"{name}: {len(libraries)} identical library assemblies across all {len(RIDS)} platforms")
    print("Policy implementation assembly names and module identities are equal across platforms.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
