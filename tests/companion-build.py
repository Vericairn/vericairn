# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

"""Exercise the build's existing generate/compare operations without the CLI suite."""

import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parent.parent
TOOL = ROOT / "artifacts/bin/Vericairn.DocsTool/release/vericairn-docs.dll"


class CompanionBuildTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="companion-build-", dir=ROOT / "artifacts")
        self.root = Path(self.temporary.name)
        self.home = self.root / "home"
        self.home.mkdir()
        # Establish an empty test home without inspecting or copying the caller's setting.
        os.environ["VERICAIRN_HOME"] = str(self.home)
        self.output = self.root / "set"
        self.declaration = self.root / "documents.json"
        self.declaration.write_text('["cli-reference.md", "api-reference.md", "record-reference.md"]\n', encoding="utf-8")

    def tearDown(self):
        self.temporary.cleanup()

    def tool(self, *args, expected=0):
        result = subprocess.run(["dotnet", str(TOOL), *map(str, args)], cwd=ROOT, capture_output=True, text=True, timeout=45)
        self.assertEqual(expected, result.returncode, result.stdout + result.stderr)
        return result.stdout + result.stderr

    def generate(self, at="2026-10-18T00:00:00Z"):
        args = ["generate", "--out", self.output, "--documents", self.declaration]
        if at is not None:
            args += ["--provenance-at", at]
        self.tool(*args)

    def snapshot(self):
        return {p.relative_to(self.output).as_posix(): p.read_bytes() for p in self.output.rglob("*") if p.is_file()}

    def test_complete_bytes_repeat_with_retained_instant_and_detect_hand_edits(self):
        self.generate()
        first = self.snapshot()
        self.generate(None)
        self.assertEqual(first, self.snapshot())
        self.tool("compare", self.output)
        manifest = json.loads(first["manifest.json"])
        for output in manifest["companions"]["outputs"]:
            data = first[output["path"]]
            self.assertIn(manifest["commit"].encode(), data)
            self.assertIn(output["inputDigest"].encode(), data)
            self.assertNotIn(b"\r", data)
            self.assertFalse(data.startswith(b"\xef\xbb\xbf"))
            self.assertTrue(data.endswith(b"\n"))
            self.assertFalse(data.endswith(b"\n\n"))
        reference = self.output / "api-reference.md"
        reference.write_bytes(first["api-reference.md"].replace(b"# API reference", b"# Edited API reference"))
        self.assertIn("docs.companion-bytes", self.tool("compare", self.output, expected=1))
        reference.write_bytes(first["api-reference.md"].replace(b"\n", b"\r\n"))
        self.assertIn("docs.companion-bytes", self.tool("compare", self.output, expected=1))

    def test_source_tampering_inventory_omission_and_extra_inputs_fail(self):
        self.generate()
        before = self.snapshot()
        source = self.output / "inputs/commands.json"
        source.write_bytes(source.read_bytes().replace(b"Command", b"Changed", 1) + b" ")
        self.assertIn("docs.companion-source", self.tool("compare", self.output, expected=1))
        source.write_bytes(before["inputs/commands.json"])
        manifest = json.loads(before["manifest.json"])
        manifest["companions"]["outputs"].pop()
        (self.output / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
        self.assertIn("docs.companion-inventory", self.tool("compare", self.output, expected=1))
        (self.output / "manifest.json").write_bytes(before["manifest.json"])
        (self.output / "inputs/unlisted.json").write_text("{}", encoding="utf-8")
        self.assertIn("docs.companion-inventory", self.tool("compare", self.output, expected=1))

    def test_new_set_preserves_old_frozen_bytes_and_the_checksum_index(self):
        old = self.root / "old"
        old.mkdir()
        (old / "reference.md").write_bytes(b"frozen bytes\r\n")
        digest = "sha256:" + hashlib.sha256((old / "reference.md").read_bytes()).hexdigest()
        index = self.root / "frozen-sha256.json"
        index.write_text(json.dumps({"format": "vericairn.frozen-docs/1", "sets": {"old": {"reference.md": digest}}}), encoding="utf-8")
        original_index = index.read_bytes()
        self.generate()
        self.tool("compare", self.output)
        self.tool("generate", "--out", old, "--documents", self.declaration, expected=2)
        self.assertEqual(b"frozen bytes\r\n", (old / "reference.md").read_bytes())
        self.assertEqual(original_index, index.read_bytes())
        self.assertEqual({"reference.md"}, {p.name for p in old.iterdir()})

    def test_explicit_new_date_changes_basis_and_notes_window_is_preserved(self):
        self.generate()
        before = self.snapshot()
        manifest = json.loads(before["manifest.json"])
        window = {"from": "2026-10-01T00:00:00Z", "to": "2026-10-17T23:59:59Z"}
        manifest["notes"] = window
        (self.output / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
        self.generate("2026-10-19T00:00:00Z")
        after = self.snapshot()
        self.assertNotEqual(before["cli-reference.md"], after["cli-reference.md"])
        self.assertEqual(window, json.loads(after["manifest.json"])["notes"])
        self.tool("compare", self.output)

    def test_serial_release_retains_exact_inputs_and_reproducible_archive_without_a_checksum_cycle(self):
        self.generate()
        version = json.loads((self.output / "manifest.json").read_bytes())["version"]
        artifacts = self.root / "payload"
        artifacts.mkdir()
        (artifacts / "fixture.bin").write_bytes(b"Exact payload bytes.\n")
        (artifacts / f"vericairn-{version}-sbom.cdx.json").write_text(
            '{"bomFormat":"CycloneDX","specVersion":"1.6","components":[{"type":"library","name":"Fixture","version":"1"}],"dependencies":[]}\n', encoding="utf-8")
        args = ["generate", "--out", self.output, "--documents", self.declaration, "--artifacts", artifacts]
        self.tool(*args)
        snapshot = self.snapshot()
        archive = artifacts / f"vericairn-{version}-companions.zip"
        before = archive.read_bytes()
        self.assertNotIn(archive.name.encode(), snapshot["inputs/artifacts/SHA256SUMS"])
        self.assertIn(b"fixture", snapshot["checksums.md"])
        self.assertIn(b"Not recorded", snapshot["components.md"])
        with zipfile.ZipFile(archive) as package:
            self.assertEqual(set(snapshot), set(package.namelist()))
            for item in package.infolist():
                self.assertEqual((1980, 1, 1, 0, 0, 0), item.date_time)
                self.assertEqual(0, item.create_system)
                self.assertEqual(zipfile.ZIP_STORED, item.compress_type)
                self.assertEqual(snapshot[item.filename], package.read(item))
        self.tool(*args)
        self.assertEqual(snapshot, self.snapshot())
        self.assertEqual(before, archive.read_bytes())
        self.tool("compare", self.output, "--artifacts", artifacts)

        # Updating the capture's declared digest cannot make invented parser results authoritative.
        capture_path = "inputs/workflows/workflow-inputs.json"
        capture = json.loads(snapshot[capture_path])
        capture["allowed"].append("invented/action@" + "a" * 40)
        changed = json.dumps(capture).encode()
        (self.output / capture_path).write_bytes(changed)
        manifest = json.loads(snapshot["manifest.json"])
        entry = next(i for i in manifest["companions"]["artifactInputs"] if i["path"] == capture_path)
        entry["hash"] = "sha256:" + hashlib.sha256(changed).hexdigest()
        entry["bytes"] = len(changed)
        (self.output / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
        self.assertIn("actual retained parser", self.tool("compare", self.output, expected=1))
        (self.output / capture_path).write_bytes(snapshot[capture_path])
        (self.output / "manifest.json").write_bytes(snapshot["manifest.json"])
        (artifacts / "fixture.bin").write_bytes(b"Different payload.\n")
        self.assertIn("payload bytes differ", self.tool("compare", self.output, "--artifacts", artifacts, expected=1))

    def test_index_links_selected_register_snapshot_and_preserves_it_on_regeneration(self):
        register = self.root / "register.md"
        register.write_text("# Vocabulary\n\n## Generated reference coverage\n\n| Category | Reference | Term |\n|---|---|---|\n| command | vericairn status | Estate state |\n", encoding="utf-8")
        self.tool("generate", "--out", self.output, "--documents", self.declaration, "--register", register,
                  "--provenance-at", "2026-10-18T00:00:00Z")
        before = self.snapshot()
        self.assertIn(b"[Estate state](inputs/reference-register.md#generated-reference-coverage)", before["reference-index.md"])
        self.generate(None)
        self.assertEqual(before, self.snapshot())
        self.tool("compare", self.output)


if __name__ == "__main__":
    unittest.main()
