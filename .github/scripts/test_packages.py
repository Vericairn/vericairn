# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

import contextlib
import hashlib
import io
from pathlib import Path
import runpy
import tempfile
import unittest

verify = runpy.run_path(str(Path(__file__).with_name("verify-packages.py")))["verify"]


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
