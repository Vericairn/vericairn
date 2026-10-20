# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

import hashlib
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET
import zipfile


MCP_NAME = "io.github.vericairn/vericairn"
MCP_SCHEMA = "https://static.modelcontextprotocol.io/schemas/2025-12-11/server.schema.json"


def verify_mcp_package(path, allow_legacy=False):
    """Check the packed tool's distribution contract; registry schema validation is separate."""
    try:
        with zipfile.ZipFile(path) as package:
            names = package.namelist()
            if len(names) != len(set(names)):
                raise ValueError("duplicate archive entries")
            [nuspec] = [name for name in names if name.endswith(".nuspec")]
            metadata = ET.fromstring(package.read(nuspec)).find("{*}metadata")
            if metadata is None or metadata.findtext("{*}id") != "dotnet-vc":
                raise ValueError("expected dotnet-vc metadata")
            types = [item.get("name") for item in metadata.findall("{*}packageTypes/{*}packageType")]
            version = metadata.findtext("{*}version")
            # Publication retries still use this verifier for immutable releases predating discovery.
            # A manifest or McpServer type always opts into the complete check, even on a dev version.
            release = re.fullmatch(r"(\d+)\.(\d+)\.(\d+)(?:[-+].*)?", version or "")
            if (allow_legacy and release and tuple(map(int, release.groups())) < (0, 8, 1)
                    and types == ["DotnetTool"] and ".mcp/server.json" not in names):
                return None
            if sorted(types) != ["DotnetTool", "McpServer"]:
                raise ValueError("expected both DotnetTool and McpServer package types")
            readme = metadata.findtext("{*}readme")
            marker = f"<!-- mcp-name: {MCP_NAME} -->"
            if not readme or package.read(readme).decode("utf-8-sig").splitlines().count(marker) != 1:
                raise ValueError("package README must carry the mcp-name line exactly once")
            manifest = json.loads(package.read(".mcp/server.json"))
            if not version or manifest["version"] != version:
                raise ValueError("server version must equal the package version")
            if manifest["$schema"] != MCP_SCHEMA or manifest["name"] != MCP_NAME:
                raise ValueError("unexpected registry schema or server name")
            [server] = manifest["packages"]
            if (server["registryType"], server["identifier"], server["version"], server["transport"]) != (
                    "nuget", "dotnet-vc", version, {"type": "stdio"}):
                raise ValueError("expected the same NuGet tool version over stdio")
            if server["runtimeArguments"] != [{"type": "named", "name": "--verbosity", "isRequired": True, "value": "quiet"}]:
                raise ValueError("dnx must keep installation messages off the protocol stream")
            estate, principal, mcp, serve = server["packageArguments"]
            for argument, name, variable in ((estate, "--estate", "estate_root"),
                                              (principal, "--as", "acting_principal")):
                if (argument["type"], argument["name"], argument["value"]) != ("named", name, "{" + variable + "}") or argument["isRequired"] is not True:
                    raise ValueError(f"expected the required {name} input")
                entry = argument["variables"][variable]
                if entry["isRequired"] is not True or entry["format"] != "string" or "default" in entry:
                    raise ValueError(f"{name} must require an explicit string input")
            if mcp != {"type": "positional", "isRequired": True, "value": "mcp"} or serve != {"type": "positional", "isRequired": True, "value": "serve"}:
                raise ValueError("expected mcp serve after the required inputs")
            return manifest
    except (KeyError, TypeError, ValueError, ET.ParseError, zipfile.BadZipFile) as error:
        raise SystemExit(f"Invalid MCP tool package {path.name}: {error}") from error


ICON_NAME = "package-icon.png"
ICON = Path(__file__).resolve().parents[2] / "brand" / ICON_NAME


def verify_icon(path, expected_icon, allow_legacy=False):
    try:
        with zipfile.ZipFile(path) as package:
            names = package.namelist()
            nuspecs = [name for name in names if name.endswith(".nuspec") and "/" not in name]
            if len(nuspecs) != 1:
                raise SystemExit(f"Package icon requires exactly one nuspec: {path.name}")
            metadata = ET.fromstring(package.read(nuspecs[0]))
            icons = metadata.findall("./{*}metadata/{*}icon")
            # Publication retries still verify immutable releases that predate the icon; an icon always gets the complete check.
            version = metadata.findtext("./{*}metadata/{*}version") or ""
            release = re.fullmatch(r"(\d+)\.(\d+)\.(\d+)(?:[-+].*)?", version)
            if (allow_legacy and release and tuple(map(int, release.groups())) < (0, 8, 1)
                    and not icons and ICON_NAME not in names):
                return
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
            verify_icon(path, expected_icon, allow_legacy=True)
        if name.lower().startswith("dotnet-vc.") and name.endswith(".nupkg"):
            verify_mcp_package(path, allow_legacy=True)
        print(f"Verified {name}")


if __name__ == "__main__":
    verify(Path(sys.argv[1] if len(sys.argv) > 1 else "packages"))
