# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

import json
from pathlib import Path
import runpy
import tempfile
import unittest

_verifier = runpy.run_path(str(Path(__file__).with_name("verify-sbom.py")))
verify = _verifier["verify"]


class BillOfMaterialsTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.version = "0.9.1"
        self.lock = self.root / "packages.distributable.lock.json"
        self.lock.write_text(json.dumps({"version": 1, "dependencies": {
            "net10.0": {"System.CommandLine": {"type": "Direct", "resolved": "2.0.12"},
                        "vericairn.records": {"type": "Project"}},
            "net10.0/linux-x64": {"SQLitePCLRaw.lib.e_sqlite3": {"type": "Transitive", "resolved": "2.1.12"}},
        }}))

    def bom(self, *components):
        path = self.root / "bom.json"
        path.write_text(json.dumps({
            "bomFormat": "CycloneDX", "specVersion": "1.6",
            "metadata": {"component": {"type": "application", "name": "Vericairn", "version": self.version}},
            "components": [{"name": n, "version": v} for n, v in components],
        }))
        return path

    def test_a_bill_naming_every_package_of_every_target_passes(self):
        verify(self.bom(("System.CommandLine", "2.0.12"), ("SQLitePCLRaw.lib.e_sqlite3", "2.1.12"), ("Extra", "1.0.0")), [self.lock], self.version)

    def test_wrong_missing_or_unversioned_metadata_is_refused_even_with_all_packages(self):
        for metadata in ({}, {"component": {}},
                         {"component": {"name": "Vericairn"}},
                         {"component": {"name": "Vericairn", "version": ""}},
                         {"component": {"name": "Vericairn", "version": "0.0.0"}},
                         {"component": {"name": "Vericairn", "version": "0.9.0"}},
                         {"component": {"name": "Another", "version": self.version}}):
            with self.subTest(metadata=metadata):
                path = self.bom(("System.CommandLine", "2.0.12"), ("SQLitePCLRaw.lib.e_sqlite3", "2.1.12"))
                document = json.loads(path.read_text())
                document["metadata"] = metadata
                path.write_text(json.dumps(document))
                with self.assertRaisesRegex(SystemExit, "metadata component.*Vericairn 0.9.1"):
                    verify(path, [self.lock], self.version)

    def test_an_absent_metadata_section_is_refused(self):
        path = self.bom(("System.CommandLine", "2.0.12"), ("SQLitePCLRaw.lib.e_sqlite3", "2.1.12"))
        document = json.loads(path.read_text())
        del document["metadata"]
        path.write_text(json.dumps(document))
        with self.assertRaisesRegex(SystemExit, "metadata component.*Vericairn 0.9.1"):
            verify(path, [self.lock], self.version)

    def test_a_platform_package_missing_from_the_bill_fails(self):
        with self.assertRaisesRegex(SystemExit, "sqlitepclraw.lib.e_sqlite3 2.1.12"):
            verify(self.bom(("System.CommandLine", "2.0.12")), [self.lock], self.version)

    def test_another_version_does_not_count(self):
        with self.assertRaisesRegex(SystemExit, "system.commandline 2.0.12"):
            verify(self.bom(("System.CommandLine", "2.0.11"), ("SQLitePCLRaw.lib.e_sqlite3", "2.1.12")), [self.lock], self.version)

    def test_no_lock_file_is_no_evidence(self):
        with self.assertRaisesRegex(SystemExit, "No distributable packages"):
            verify(self.bom(("System.CommandLine", "2.0.12")), [], self.version)


if __name__ == "__main__":
    unittest.main()
