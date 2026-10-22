# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

"""Exercise the release gate with complete six-platform single-file fixtures."""

import contextlib
import importlib.util
import io
from pathlib import Path
import struct
import tempfile
import unittest
import zlib


SPEC = importlib.util.spec_from_file_location(
    "distributable_libraries", Path(__file__).with_name("verify-distributable-libraries.py")
)
CHECK = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CHECK)


def encoded_string(value):
    data = value.encode("utf-8")
    length = len(data)
    prefix = bytearray()
    while length >= 0x80:
        prefix.append((length & 0x7f) | 0x80)
        length >>= 7
    prefix.append(length)
    return prefix + data


def bundle(path, libraries, compressed=False):
    """Write the SDK's v6 layout; payloads need not execute to compare bytes."""
    data = bytearray(b"native host" + bytes(8) + CHECK.BUNDLE_SIGNATURE)
    entries = bytearray()
    for name, payload in libraries:
        stored = payload
        if compressed:
            compressor = zlib.compressobj(wbits=-zlib.MAX_WBITS)
            stored = compressor.compress(payload) + compressor.flush()
        entries += struct.pack("<qqqB", len(data), len(payload), len(stored) if compressed else 0, 1)
        entries += encoded_string(name)
        data += stored
    struct.pack_into("<q", data, len(b"native host"), len(data))
    data += struct.pack("<IIi", 6, 0, len(libraries)) + encoded_string("bundle-id") + bytes(40) + entries
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)


class DistributableLibraryTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.cli = self.root / "cli"
        self.workbench = self.root / "workbench"
        self.cli_libraries = [
            ("Vericairn.Runtime.dll", b"runtime policy implementation"),
            ("Vericairn.Records.dll", b"records"),
        ]
        self.desktop_libraries = [
            ("Vericairn.Records.dll", b"records"),
            ("Vericairn.Presentation.dll", b"presentation"),
        ]
        self.reset()

    def path(self, root, rid, application):
        extension = ".exe" if rid.startswith("win-") else ""
        return root / rid / "bin" / (application + extension)

    def reset(self):
        for rid in CHECK.RIDS:
            # Native/framework assets and the application itself legitimately vary.
            cli = self.cli_libraries + [("System.Native.dll", rid.encode()), ("vericairn.dll", rid.encode())]
            bundle(self.path(self.cli, rid, "vericairn"), cli)
            bundle(self.path(self.workbench, rid, "vericairn"), cli)
            bundle(self.path(self.workbench, rid, "vericairn-workbench"), self.desktop_libraries)

    def assert_failed(self, message):
        with contextlib.redirect_stderr(io.StringIO()) as errors:
            self.assertEqual(1, CHECK.main([str(self.cli), str(self.workbench)]))
        self.assertIn(message, errors.getvalue())

    def test_all_six_platforms_and_both_applications_pass(self):
        with contextlib.redirect_stdout(io.StringIO()) as output:
            self.assertEqual(0, CHECK.main([str(self.cli), str(self.workbench)]))
        self.assertIn("across all 6 platforms", output.getvalue())
        self.assertIn("Policy implementation assembly names and module identities are equal", output.getvalue())

    def test_any_platforms_policy_implementation_difference_fails(self):
        for rid in CHECK.RIDS:
            with self.subTest(rid=rid):
                changed = [("Vericairn.Runtime.dll", b"different policy implementation"), self.cli_libraries[1]]
                bundle(self.path(self.cli, rid, "vericairn"), changed)
                bundle(self.path(self.workbench, rid, "vericairn"), changed)
                self.assert_failed("library bytes differ: Vericairn.Runtime.dll")
                self.reset()

    def test_any_platforms_workbench_library_difference_fails(self):
        for rid in CHECK.RIDS:
            with self.subTest(rid=rid):
                changed = [self.desktop_libraries[0], ("Vericairn.Presentation.dll", b"different presentation")]
                bundle(self.path(self.workbench, rid, "vericairn-workbench"), changed)
                self.assert_failed("library bytes differ: Vericairn.Presentation.dll")
                self.reset()

    def test_workbenchs_cli_copy_is_checked_on_every_platform(self):
        for rid in CHECK.RIDS:
            with self.subTest(rid=rid):
                bundle(self.path(self.workbench, rid, "vericairn"),
                       [("Vericairn.Runtime.dll", b"stale policy implementation"), self.cli_libraries[1]])
                self.assert_failed("Workbench CLI/" + rid)
                self.reset()

    def test_shared_libraries_must_match_between_applications(self):
        for rid in CHECK.RIDS:
            bundle(self.path(self.workbench, rid, "vericairn-workbench"),
                   [("Vericairn.Records.dll", b"different records"), self.desktop_libraries[1]])
        self.assert_failed("CLI and Workbench library bytes differ: Vericairn.Records.dll")

    def test_a_missing_platform_or_companion_fails(self):
        for root, application in ((self.cli, "vericairn"), (self.workbench, "vericairn-workbench"), (self.workbench, "vericairn")):
            for rid in CHECK.RIDS:
                with self.subTest(application=application, rid=rid, root=root.name):
                    self.path(root, rid, application).unlink()
                    self.assert_failed("Distributable library check failed")
                    self.reset()

    def test_missing_and_extra_libraries_fail(self):
        for changed in (self.cli_libraries[:1], self.cli_libraries + [("Vericairn.Extra.dll", b"extra")]):
            with self.subTest(changed=changed):
                bundle(self.path(self.cli, "win-arm64", "vericairn"), changed)
                self.assert_failed("library set differs")
                self.reset()

    def test_omitting_the_policy_implementation_from_every_platform_fails(self):
        for rid in CHECK.RIDS:
            bundle(self.path(self.cli, rid, "vericairn"), self.cli_libraries[1:])
        self.assert_failed("missing required library Vericairn.Runtime.dll")

    def test_empty_or_unbundled_files_fail(self):
        path = self.path(self.cli, "linux-x64", "vericairn")
        for data in (b"", b"an ordinary executable"):
            with self.subTest(data=data):
                path.write_bytes(data)
                self.assert_failed("bundle")

    def test_unknown_and_truncated_bundle_headers_fail(self):
        path = self.path(self.cli, "linux-x64", "vericairn")
        original = path.read_bytes()
        offset = struct.unpack_from("<q", original, len(b"native host"))[0]
        unsupported = bytearray(original)
        struct.pack_into("<I", unsupported, offset, 7)
        for data, message in ((unsupported, "unsupported bundle format"), (original[:-5], "truncated bundle manifest")):
            with self.subTest(message=message):
                path.write_bytes(data)
                self.assert_failed(message)

    def test_invalid_manifest_offset_and_payload_bounds_fail(self):
        path = self.path(self.cli, "linux-x64", "vericairn")
        original = path.read_bytes()
        invalid_header = bytearray(original)
        struct.pack_into("<q", invalid_header, len(b"native host"), 0)
        invalid_payload = bytearray(original)
        header = struct.unpack_from("<q", original, len(b"native host"))[0]
        entry = header + 12 + len(encoded_string("bundle-id")) + 40
        struct.pack_into("<q", invalid_payload, entry, len(original))
        for data, message in ((invalid_header, "invalid bundle manifest offset"), (invalid_payload, "invalid payload bounds")):
            with self.subTest(message=message):
                path.write_bytes(data)
                self.assert_failed(message)

    def test_duplicate_libraries_fail(self):
        bundle(self.path(self.cli, "linux-x64", "vericairn"), self.cli_libraries * 2)
        self.assert_failed("duplicate library")

    def test_compressed_payloads_compare_by_uncompressed_assembly_bytes(self):
        bundle(self.path(self.cli, "win-arm64", "vericairn"), self.cli_libraries, compressed=True)
        CHECK.verify(self.cli, self.workbench)


if __name__ == "__main__":
    unittest.main()
