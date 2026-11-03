# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

import json
from pathlib import Path
import runpy
import subprocess
import sys
import tempfile
import unittest

runtime = runpy.run_path(str(Path(__file__).with_name("runtime-packs.py")))


class RuntimePackTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.version = "0.9.1"
        self.evidence = self.root / "evidence"
        for project in runtime["PROJECTS"]:
            (self.evidence / project).mkdir(parents=True)
            for rid in runtime["RIDS"]:
                content = f"runtime|Microsoft.NETCore.App.Runtime.{rid}|10.0.11\napphost|Microsoft.NETCore.App.Host.{rid}|10.0.11\n"
                if project == "Vericairn.Cli":
                    content += f"runtime|Microsoft.AspNetCore.App.Runtime.{rid}|10.0.11\n"
                (self.evidence / project / f"{rid}.txt").write_text(content, encoding="utf-8-sig")
        self.bom = self.root / "bom.json"
        self.original = {"type": "library", "name": "Ordinary", "version": "1.0.0", "bom-ref": "ordinary"}
        self.generated = {
            "bomFormat": "CycloneDX", "specVersion": "1.6",
            "metadata": {"component": {"type": "application", "name": "Solution", "version": "0.0.0", "bom-ref": "root"},
                         "tools": [{"vendor": "CycloneDX", "name": "CycloneDX for .NET", "version": "6.3.1"}]},
            "components": [self.original],
            "dependencies": [{"ref": "root", "dependsOn": ["ordinary"]}, {"ref": "ordinary", "dependsOn": []}],
        }
        self.bom.write_text(json.dumps(self.generated))

    def completed(self):
        runtime["complete"](self.bom, self.evidence, self.version)
        return json.loads(self.bom.read_text())

    def test_metadata_names_the_packaged_version_and_preserves_other_generated_fields(self):
        for self.version in ("0.9.1", "2.3.4"):
            with self.subTest(version=self.version):
                document = self.completed()
                expected = json.loads(json.dumps(self.generated))
                expected["metadata"]["component"]["name"] = "Vericairn"
                expected["metadata"]["component"]["version"] = self.version
                expected["metadata"]["component"]["bom-ref"] = f"Vericairn@{self.version}"
                expected["dependencies"][0]["ref"] = f"Vericairn@{self.version}"
                document["components"] = [self.original]
                self.assertEqual(expected, document)

    def test_completion_adds_the_application_when_metadata_is_absent(self):
        del self.generated["metadata"]
        self.bom.write_text(json.dumps(self.generated))
        self.assertEqual({"type": "application", "name": "Vericairn", "version": self.version, "bom-ref": f"Vericairn@{self.version}"},
                         self.completed()["metadata"]["component"])

    def test_packaging_commands_require_the_version_and_reject_a_different_release(self):
        scripts = Path(__file__).parent
        lock = self.root / "packages.distributable.lock.json"
        lock.write_text(json.dumps({"version": 1, "dependencies": {
            "net10.0": {"Ordinary": {"type": "Direct", "resolved": "1.0.0"}},
        }}))
        complete = [sys.executable, str(scripts / "runtime-packs.py"), str(self.bom), str(self.evidence)]
        verify = [sys.executable, str(scripts / "verify-sbom.py"), str(self.bom), str(lock), "--runtime-packs", str(self.evidence)]
        for command in (complete, verify):
            missing = subprocess.run(command, capture_output=True, text=True)
            self.assertNotEqual(0, missing.returncode)
            self.assertIn("--version", missing.stderr)
        for version in ("0.9.1", "2.3.4"):
            with self.subTest(version=version):
                for command in (complete, verify):
                    result = subprocess.run(command + ["--version", version], capture_output=True, text=True)
                    self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertEqual(version, json.loads(self.bom.read_text())["metadata"]["component"]["version"])
                mismatch = subprocess.run(verify + ["--version", "0.0.0"], capture_output=True, text=True)
                self.assertNotEqual(0, mismatch.returncode)
                self.assertIn("metadata component", mismatch.stderr)

    def test_all_packs_are_named_and_workbench_includes_its_cli_copy(self):
        document = self.completed()
        self.assertEqual(19, len(document["components"]))
        self.assertEqual(self.original, document["components"][0])
        for component in document["components"][1:]:
            rid = component["name"].rsplit(".", 1)[1]
            self.assertEqual("10.0.11", component["version"])
            self.assertEqual({f"vericairn/{rid}", f"vericairn-workbench/{rid}"}, {p["value"] for p in component["properties"]})
            self.assertTrue(component["purl"].startswith("pkg:nuget/"))
        self.assertNotIn(str(self.root), self.bom.read_text())
        self.assertEqual(document, self.completed())

    def test_missing_platform_or_project_is_not_an_attestation(self):
        (self.evidence / "Vericairn.Workbench" / "win-arm64.txt").unlink()
        with self.assertRaisesRegex(ValueError, "Missing bundled-pack evidence"):
            self.completed()

    def test_missing_apphost_or_runtime_is_refused(self):
        path = self.evidence / "Vericairn.Cli" / "osx-arm64.txt"
        for content in ("runtime|Microsoft.NETCore.App.Runtime.osx-arm64|10.0.11\n",
                        "apphost|Microsoft.NETCore.App.Host.osx-arm64|10.0.11\n"):
            path.write_text(content)
            with self.assertRaisesRegex(ValueError, "Incomplete"):
                self.completed()

    def test_wrong_rid_unknown_pack_and_missing_version_are_refused(self):
        path = self.evidence / "Vericairn.Cli" / "osx-arm64.txt"
        for line in ("runtime|Microsoft.NETCore.App.Runtime.osx-x64|10.0.11", "runtime|Unknown|10.0.11",
                     "apphost|Microsoft.NETCore.App.Host.osx-arm64|", "runtime|Microsoft.NETCore.App.Runtime.osx-arm64|10"):
            path.write_text(line + "\n")
            with self.assertRaisesRegex(ValueError, "Invalid"):
                self.completed()

    def test_mismatched_versions_and_bundle_membership_fail_verification(self):
        for change in (lambda doc: doc["components"].pop(),
                       lambda doc: doc["components"][1].update(version="10.0.10"),
                       lambda doc: doc["components"][1]["properties"].pop()):
            document = self.completed()
            change(document)
            with self.assertRaisesRegex(ValueError, "omits or misattributes"):
                runtime["verify"](document, self.evidence)

    def test_different_pack_versions_in_the_two_publishes_are_preserved(self):
        path = self.evidence / "Vericairn.Workbench" / "linux-x64.txt"
        path.write_text(path.read_text(encoding="utf-8-sig").replace("10.0.11", "10.0.12"))
        document = self.completed()
        self.assertEqual(21, len(document["components"]))
        for component in document["components"]:
            if component["version"] == "10.0.12":
                self.assertEqual([{"name": runtime["BUNDLED_IN"], "value": "vericairn-workbench/linux-x64"}], component["properties"])


if __name__ == "__main__":
    unittest.main()
