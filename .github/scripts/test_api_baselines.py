# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

import hashlib
import io
import json
from pathlib import Path
import runpy
import tempfile
import unittest
from urllib.error import URLError
import xml.etree.ElementTree as ET
import zipfile

api = runpy.run_path(str(Path(__file__).with_name("api-baselines.py")))


def package(name="Example", version="1.10.0"):
    data = io.BytesIO()
    with zipfile.ZipFile(data, "w") as archive:
        archive.writestr(f"{name}.nuspec", f"<package><metadata><id>{name}</id><version>{version}</version></metadata></package>")
    return data.getvalue()


class ApiBaselineTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.output = self.root / "baselines"
        self.project("Example")

    def project(self, name, extra=""):
        directory = self.root / "src" / name
        directory.mkdir(parents=True)
        (directory / f"{name}.csproj").write_text(
            f"<Project><PropertyGroup><IsPackable>true</IsPackable><PackageId>{name}</PackageId>{extra}</PropertyGroup></Project>")

    def test_latest_stable_is_selected_numerically_without_assuming_index_order(self):
        self.assertEqual("1.10.0", api["latest"](["1.10.0", "2.0.0-preview.1", "1.9.9", "1.0.0"]))
        self.assertEqual("1.2.3.4", api["latest"](["1.2.3.4", "1.2.3"]))
        with self.assertRaisesRegex(ValueError, "No stable"):
            api["latest"](["1.0.0-preview.1"])

    def test_every_library_is_discovered_but_the_cli_tool_is_not_a_library(self):
        self.project("Another")
        self.project("Tool", "<PackAsTool>true</PackAsTool>")
        self.assertEqual(["Another", "Example"], api["libraries"](self.root))

    def test_implicit_packability_cannot_silently_skip_a_new_library(self):
        (self.root / "src" / "Example" / "Example.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk" />')
        with self.assertRaisesRegex(ValueError, "declare IsPackable explicitly"):
            api["libraries"](self.root)

    def test_baseline_bytes_identity_and_version_are_retained_for_the_sdk(self):
        data = package()
        urls = []

        def fetch(url):
            urls.append(url)
            return json.dumps({"versions": ["1.9.0", "1.10.0"]}).encode() if url.endswith("index.json") else data

        api["prepare"](self.root, self.output, fetch)
        self.assertEqual([f"{api['FEED']}/example/index.json", f"{api['FEED']}/example/1.10.0/example.1.10.0.nupkg"], urls)
        self.assertEqual(data, (self.output / "example.1.10.0.nupkg").read_bytes())
        self.assertEqual([{"package": "Example", "version": "1.10.0", "sha256": hashlib.sha256(data).hexdigest()}],
                         json.loads((self.output / "baselines.json").read_text()))
        props = ET.parse(self.output / "Example.props").getroot()
        self.assertEqual("$(MSBuildThisFileDirectory)example.1.10.0.nupkg", props.findtext(".//PackageValidationBaselinePath"))
        self.assertEqual("1.10.0", props.findtext(".//PackageValidationBaselineVersion"))

    def test_missing_feed_or_package_fails_instead_of_skipping_validation(self):
        for missing_index in (True, False):
            def fetch(url):
                if missing_index or not url.endswith("index.json"):
                    raise URLError("Not found")
                return b'{"versions": ["1.10.0"]}'
            with self.assertRaises(URLError):
                api["prepare"](self.root, self.output, fetch)

    def test_a_different_package_or_version_is_not_a_baseline(self):
        for data in (package("Other"), package(version="1.9.0")):
            with self.assertRaisesRegex(ValueError, "identity"):
                api["prepare"](self.root, self.output, lambda url: b'{"versions": ["1.10.0"]}' if url.endswith("index.json") else data)

    def test_every_repository_library_has_an_explicit_expected_change_file(self):
        root = Path(__file__).resolve().parents[2]
        names = api["libraries"](root)
        self.assertEqual(7, len(names))
        for name in names:
            document = ET.parse(root / ".github" / "api-compatibility" / f"{name}.xml").getroot()
            self.assertEqual("Suppressions", document.tag)
            # A suppression must name a diagnostic and member, never a blanket diagnostic allowance.
            for suppression in document:
                self.assertTrue(suppression.findtext("DiagnosticId"))
                target = suppression.findtext("Target")
                self.assertTrue(target and "*" not in target)
                self.assertTrue(suppression.findtext("Left"))
                self.assertTrue(suppression.findtext("Right"))


if __name__ == "__main__":
    unittest.main()
