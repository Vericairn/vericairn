# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

"""Build the nightly's platform archive and feed it to the package smoke."""

import argparse
import hashlib
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import tarfile
import zipfile

from smoke import ROOT


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True, choices=("linux-x64", "osx-arm64", "win-x64"))
    parser.add_argument("--commit", required=True)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    version = re.search(r"<VersionPrefix>([0-9]+\.[0-9]+\.[0-9]+)</VersionPrefix>", (ROOT / "Directory.Build.props").read_text())[1]
    with tempfile.TemporaryDirectory(prefix="vericairn-nightly-") as scratch:
        scratch = Path(scratch)
        output = scratch / "out"
        build = ["src/Vericairn.Cli/Vericairn.Cli.csproj", "-p:VericairnDistributable=true",
                 "-p:SelfContained=true", "-p:PublishSingleFile=true"]
        subprocess.run(["dotnet", "restore", "--locked-mode", *build], cwd=ROOT, check=True)
        subprocess.run(["dotnet", "publish", "--no-restore", "-c", "Release", "-r", args.rid,
                        "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:VersionSuffix=",
                        "-p:ContinuousIntegrationBuild=true", "--output", str(output / "bin"), *build], cwd=ROOT, check=True)
        if args.rid.startswith("osx-"):
            subprocess.run(["swiftc", "-O", "-target", "arm64-apple-macos13.0", "-o", str(output / "bin/vericairn-enclave"),
                            "src/Vericairn.Enclave/vericairn-enclave.swift"], cwd=ROOT, check=True)
        archive = scratch / f"vericairn-{version}-{args.rid}"
        files = sorted(path for path in (output / "bin").iterdir() if path.suffix not in {".pdb", ".xml"})
        if args.rid.startswith("win-"):
            archive = Path(str(archive) + ".zip")
            with zipfile.ZipFile(archive, mode="w", compression=zipfile.ZIP_DEFLATED) as package:
                for path in files:
                    package.write(path, path.relative_to(output))
        else:
            archive = Path(str(archive) + ".tar.gz")
            with tarfile.open(archive, mode="w:gz") as package:
                for path in files:
                    package.add(path, arcname=path.relative_to(output))
        with archive.open("rb") as stream:
            digest = hashlib.file_digest(stream, "sha256").hexdigest()
        (scratch / "SHA256SUMS").write_text(f"{digest}  {archive.name}\n", encoding="utf-8")
        return subprocess.run([sys.executable, "-B", "tools/examples/smoke.py", "--dist", str(scratch), "--version", version,
                               "--rid", args.rid, "--commit", args.commit, "--report", str(args.report)], cwd=ROOT, check=False).returncode


if __name__ == "__main__":
    raise SystemExit(main())
