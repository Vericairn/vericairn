# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

import contextlib
import hashlib
import io
import json
from pathlib import Path
import runpy
import struct
import tempfile
import unittest
import warnings
import zipfile
import zlib

_verifier = runpy.run_path(str(Path(__file__).with_name("verify-packages.py")))
verify = _verifier["verify"]
verify_mcp_package = _verifier["verify_mcp_package"]
verify_icon = _verifier["verify_icon"]
BRAND = Path(__file__).resolve().parents[2] / "brand"
ICON_NAME = "package-icon.png"
NUSPEC = f'''<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata><id>Example</id><version>1.0.0</version><icon>{ICON_NAME}</icon></metadata>
</package>'''


def package_bytes(nuspec=NUSPEC, entries=None):
    if entries is None:
        entries = [(ICON_NAME, (BRAND / ICON_NAME).read_bytes())]
    output = io.BytesIO()
    with zipfile.ZipFile(output, "w") as package:
        if nuspec is not None:
            package.writestr("Example.nuspec", nuspec)
        for name, content in entries:
            package.writestr(name, content)
    return output.getvalue()


class McpPackageVerificationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.package = Path(self.temporary.name) / "dotnet-vc.1.2.3.nupkg"
        root = Path(__file__).resolve().parents[2]
        self.manifest = json.loads((root / ".mcp/server.json.in").read_text().replace("@PACKAGE_VERSION@", "1.2.3"))
        self.nuspec = '''<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata>
          <id>dotnet-vc</id><version>1.2.3</version><readme>README.md</readme><icon>package-icon.png</icon>
          <packageTypes><packageType name="DotnetTool" /><packageType name="McpServer" /></packageTypes>
        </metadata></package>'''
        self.readme = (root / "docs/nuget-readme.md").read_text()

    def write(self, include_manifest=True):
        with zipfile.ZipFile(self.package, "w") as package:
            package.writestr("dotnet-vc.nuspec", self.nuspec)
            package.writestr("README.md", self.readme)
            package.writestr(ICON_NAME, (BRAND / ICON_NAME).read_bytes())
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

    def test_a_description_the_registry_refuses_fails(self):
        for description in ("", "x" * 101):
            with self.subTest(length=len(description)):
                self.manifest["description"] = description
                self.write()
                with self.assertRaisesRegex(SystemExit, "description"):
                    verify_mcp_package(self.package)

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

    def test_older_immutable_releases_without_the_icon_remain_verifiable(self):
        path = Path(self.temporary.name) / "Example.0.8.0.nupkg"
        path.write_bytes(package_bytes(NUSPEC.replace("1.0.0", "0.8.0").replace(f"<icon>{ICON_NAME}</icon>", ""), entries=[]))
        icon = (BRAND / ICON_NAME).read_bytes()
        self.assertIsNone(verify_icon(path, icon, allow_legacy=True))
        with self.assertRaisesRegex(SystemExit, "icon"):
            verify_icon(path, icon)

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
            content = package_bytes() if name.endswith(".nupkg") else b"symbol fixture"
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

    def replace_package(self, content):
        name = "Example.1.0.0.nupkg"
        (self.directory / name).write_bytes(content)
        self.lines[0] = f"{hashlib.sha256(content).hexdigest()}  {name}\n"
        self.manifest.write_text("".join(self.lines))

    def test_missing_wrong_or_duplicate_icon_metadata_fails_even_with_valid_checksums(self):
        for metadata in ("", "<iconUrl>https://example.invalid/icon.png</iconUrl>",
                         "<icon>elsewhere.png</icon>", f"<icon>{ICON_NAME}</icon>" * 2):
            with self.subTest(metadata=metadata):
                self.replace_package(package_bytes(NUSPEC.replace(f"<icon>{ICON_NAME}</icon>", metadata)))
                with self.assertRaisesRegex(SystemExit, "Package icon must be named"):
                    self.check()

    def test_missing_misplaced_empty_or_changed_icon_bytes_fail_even_with_valid_checksums(self):
        for entries in ([], [("brand/" + ICON_NAME, (BRAND / ICON_NAME).read_bytes())],
                        [(ICON_NAME, b"")], [(ICON_NAME, b"a different image")]):
            with self.subTest(entries=[name for name, _ in entries]):
                self.replace_package(package_bytes(entries=entries))
                with self.assertRaisesRegex(SystemExit, "Package icon must embed"):
                    self.check()

    def test_ambiguous_nuspec_or_icon_entries_fail(self):
        icon = (BRAND / ICON_NAME).read_bytes()
        for entries in ([(ICON_NAME, icon), ("Other.nuspec", NUSPEC)], [(ICON_NAME, icon)] * 2):
            with self.subTest(names=[name for name, _ in entries]), warnings.catch_warnings():
                warnings.simplefilter("ignore", UserWarning)
                self.replace_package(package_bytes(entries=entries))
                with self.assertRaisesRegex(SystemExit, "Package icon"):
                    self.check()

    def test_invalid_zip_missing_nuspec_and_invalid_xml_fail(self):
        for content in (b"not a zip", package_bytes(nuspec=None), package_bytes(nuspec="<package>")):
            with self.subTest(content=content[:20]):
                self.replace_package(content)
                with self.assertRaisesRegex(SystemExit, "[Pp]ackage icon"):
                    self.check()

    def test_every_package_is_checked(self):
        for name in ("dotnet-vc", "Vericairn", "Vericairn.Daemon", "Vericairn.Mcp", "Vericairn.Protocol",
                     "Vericairn.Records", "Vericairn.Runtime", "Vericairn.Sdk"):
            with self.subTest(package=name):
                path = self.directory / f"{name}.0.8.1.nupkg"
                content = package_bytes(entries=[])
                path.write_bytes(content)
                self.manifest.write_text("".join(self.lines) + f"{hashlib.sha256(content).hexdigest()}  {path.name}\n")
                with self.assertRaisesRegex(SystemExit, "Package icon must embed"):
                    self.check()
                path.unlink()

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


class PackageIconAssetTests(unittest.TestCase):
    def test_committed_icon_reproduces_from_the_svg_offline(self):
        render = runpy.run_path(str(BRAND / "render-package-icon.py"))["render"]
        self.assertEqual((BRAND / ICON_NAME).read_bytes(), render())
        self.assertEqual([ICON_NAME], sorted(path.name for path in BRAND.glob("*.png")))

    def test_icon_is_an_opaque_128_pixel_png_in_the_dark_palette(self):
        content = (BRAND / ICON_NAME).read_bytes()
        self.assertLess(len(content), 1024 * 1024)
        self.assertEqual(b"\x89PNG\r\n\x1a\n", content[:8])
        chunks = []
        offset = 8
        while offset < len(content):
            size = struct.unpack(">I", content[offset:offset + 4])[0]
            kind = content[offset + 4:offset + 8]
            data = content[offset + 8:offset + 8 + size]
            crc = struct.unpack(">I", content[offset + 8 + size:offset + 12 + size])[0]
            self.assertEqual(zlib.crc32(kind + data), crc)
            chunks.append((kind, data))
            offset += size + 12
        self.assertEqual(len(content), offset)
        self.assertEqual([b"IHDR", b"IDAT", b"IEND"], [kind for kind, _ in chunks])
        self.assertEqual((128, 128, 8, 2, 0, 0, 0), struct.unpack(">IIBBBBB", chunks[0][1]))
        pixels = zlib.decompress(chunks[1][1])
        self.assertEqual(128 * (1 + 128 * 3), len(pixels))

        def pixel(x, y):
            start = y * (1 + 128 * 3) + 1 + x * 3
            return pixels[start:start + 3]

        for y in range(128):
            self.assertEqual(0, pixels[y * (1 + 128 * 3)])
            for x in range(128):
                r, g, b = pixel(x, y)
                self.assertEqual((r, r), (g, b))
                self.assertTrue(0x1E <= r <= 0xEA)
        for point in ((64, 102), (68, 76), (56, 50), (66, 25)):
            self.assertEqual(b"\xea" * 3, pixel(*point))
        for point in ((0, 0), (127, 127), (64, 36), (64, 63), (64, 89)):
            self.assertEqual(b"\x1e" * 3, pixel(*point))


if __name__ == "__main__":
    unittest.main()
