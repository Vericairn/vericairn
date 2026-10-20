# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

import contextlib
import hashlib
import io
import json
from pathlib import Path
import runpy
import tempfile
import unittest
import zipfile

verify = runpy.run_path(str(Path(__file__).with_name("verify-packages.py")))["verify"]
verify_mcp_package = runpy.run_path(str(Path(__file__).with_name("verify-packages.py")))["verify_mcp_package"]


class McpPackageVerificationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.package = Path(self.temporary.name) / "dotnet-vc.1.2.3.nupkg"
        root = Path(__file__).resolve().parents[2]
        self.manifest = json.loads((root / ".mcp/server.json.in").read_text().replace("@PACKAGE_VERSION@", "1.2.3"))
        self.nuspec = '''<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata>
          <id>dotnet-vc</id><version>1.2.3</version><readme>README.md</readme>
          <packageTypes><packageType name="DotnetTool" /><packageType name="McpServer" /></packageTypes>
        </metadata></package>'''
        self.readme = (root / "docs/nuget-readme.md").read_text()

    def write(self, include_manifest=True):
        with zipfile.ZipFile(self.package, "w") as package:
            package.writestr("dotnet-vc.nuspec", self.nuspec)
            package.writestr("README.md", self.readme)
            if include_manifest:
                package.writestr(".mcp/server.json", json.dumps(self.manifest))

    def test_tool_and_mcp_types_with_matching_versions_pass(self):
        self.write()
        self.assertEqual(self.manifest, verify_mcp_package(self.package))

    def test_either_missing_package_type_fails(self):
        original = self.nuspec
        for kind in ("DotnetTool", "McpServer"):
            with self.subTest(kind=kind):
                self.nuspec = original.replace(f'<packageType name="{kind}" />', '')
                self.write()
                with self.assertRaisesRegex(SystemExit, "both DotnetTool and McpServer"):
                    verify_mcp_package(self.package)

    def test_missing_embedded_manifest_fails(self):
        self.write(include_manifest=False)
        with self.assertRaisesRegex(SystemExit, "server.json"):
            verify_mcp_package(self.package)

    def test_stale_server_or_nuget_version_fails(self):
        for target in (self.manifest, self.manifest["packages"][0]):
            with self.subTest(target=target):
                target["version"] = "1.2.2"
                self.write()
                with self.assertRaisesRegex(SystemExit, "version"):
                    verify_mcp_package(self.package)
                target["version"] = "1.2.3"

    def test_both_inputs_must_be_required(self):
        for argument in self.manifest["packages"][0]["packageArguments"][:2]:
            variable = next(iter(argument["variables"].values()))
            variable["isRequired"] = False
            self.write()
            with self.assertRaisesRegex(SystemExit, "require an explicit"):
                verify_mcp_package(self.package)
            variable["isRequired"] = True

    def test_missing_readme_verification_line_fails(self):
        self.readme = self.readme.replace("<!-- mcp-name: io.github.vericairn/vericairn -->", "")
        self.write()
        with self.assertRaisesRegex(SystemExit, "mcp-name"):
            verify_mcp_package(self.package)

    def test_older_immutable_tool_releases_remain_verifiable(self):
        self.nuspec = self.nuspec.replace("1.2.3", "0.8.0").replace('<packageType name="McpServer" />', '')
        self.readme = "The earlier release README."
        self.write(include_manifest=False)
        self.assertIsNone(verify_mcp_package(self.package, allow_legacy=True))
        with self.assertRaisesRegex(SystemExit, "both DotnetTool and McpServer"):
            verify_mcp_package(self.package)

    def test_a_dev_version_with_a_manifest_cannot_use_the_legacy_exception(self):
        self.nuspec = self.nuspec.replace("1.2.3", "0.8.0-dev").replace('<packageType name="McpServer" />', '')
        self.write()
        with self.assertRaisesRegex(SystemExit, "both DotnetTool and McpServer"):
            verify_mcp_package(self.package, allow_legacy=True)

    def test_a_new_version_cannot_use_the_legacy_exception(self):
        self.nuspec = self.nuspec.replace("1.2.3", "0.8.1").replace('<packageType name="McpServer" />', '')
        self.write(include_manifest=False)
        with self.assertRaisesRegex(SystemExit, "both DotnetTool and McpServer"):
            verify_mcp_package(self.package, allow_legacy=True)

    def test_checksum_verifier_also_checks_the_tool_contract(self):
        self.nuspec = self.nuspec.replace('<packageType name="McpServer" />', '')
        self.write()
        symbols = self.package.with_suffix(".snupkg")
        symbols.write_bytes(b"symbols")
        (self.package.parent / "SHA256SUMS").write_text("".join(
            f"{hashlib.sha256(path.read_bytes()).hexdigest()}  {path.name}\n" for path in (self.package, symbols)))
        with self.assertRaisesRegex(SystemExit, "both DotnetTool and McpServer"):
            verify(self.package.parent)


class PackageVerificationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        self.lines = []
        for name in ("Example.1.0.0.nupkg", "Example.1.0.0.snupkg"):
            content = name.encode()
            (self.directory / name).write_bytes(content)
            self.lines.append(f"{hashlib.sha256(content).hexdigest()}  {name}\n")
        self.manifest = self.directory / "SHA256SUMS"
        self.manifest.write_text("".join(self.lines))

    def check(self):
        with contextlib.redirect_stdout(io.StringIO()):
            verify(self.directory)

    def test_complete_packages_and_symbols_pass(self):
        self.check()

    def test_release_manifest_may_also_name_other_assets(self):
        self.manifest.write_text("".join(self.lines) + "a" * 64 + "  release.tar.gz\n")
        self.check()

    def test_tampered_package_fails(self):
        (self.directory / "Example.1.0.0.nupkg").write_bytes(b"changed")
        with self.assertRaisesRegex(SystemExit, "Checksum mismatch"):
            self.check()

    def test_missing_extra_or_unpaired_assets_fail(self):
        for name in ("Example.1.0.0.nupkg", "Example.1.0.0.snupkg"):
            with self.subTest(missing=name):
                path = self.directory / name
                content = path.read_bytes()
                path.unlink()
                with self.assertRaises(SystemExit):
                    self.check()
                path.write_bytes(content)
        (self.directory / "Extra.nupkg").write_bytes(b"extra")
        with self.assertRaises(SystemExit):
            self.check()
        (self.directory / "Extra.nupkg").unlink()
        (self.directory / "Orphan.snupkg").write_bytes(b"orphan")
        self.manifest.write_text("".join(self.lines) + hashlib.sha256(b"orphan").hexdigest() + "  Orphan.snupkg\n")
        with self.assertRaisesRegex(SystemExit, "no matching package"):
            self.check()

    def test_bad_or_duplicate_checksum_entries_fail(self):
        for bad in ("bad\n", "a" * 64 + "  ../escape.nupkg\n", self.lines[0]):
            with self.subTest(entry=bad):
                self.manifest.write_text("".join(self.lines) + bad)
                with self.assertRaisesRegex(SystemExit, "Malformed or duplicate"):
                    self.check()

    def test_symlink_package_fails(self):
        path = self.directory / "Example.1.0.0.nupkg"
        path.rename(self.directory / "target")
        path.symlink_to("target")
        with self.assertRaisesRegex(SystemExit, "Not a regular package"):
            self.check()


if __name__ == "__main__":
    unittest.main()
