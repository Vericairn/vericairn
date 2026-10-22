# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

"""Exercise the publish target's downloaded and SDK-installed pack metadata offline."""

from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]


class PublishEvidenceTests(unittest.TestCase):
    def test_both_host_metadata_shapes_and_missing_evidence(self):
        with tempfile.TemporaryDirectory(prefix="publish-evidence-") as temporary:
            root = Path(temporary)
            shutil.copyfile(ROOT / "global.json", root / "global.json")
            (root / "tools").mkdir()
            shutil.copyfile(ROOT / "tools" / "BundledPacks.targets", root / "tools" / "BundledPacks.targets")
            for installed in (False, True):
                project = ET.Element("Project")
                properties = ET.SubElement(project, "PropertyGroup")
                for name, value in {"VericairnDistributable": "true", "SelfContained": "true", "PublishSingleFile": "true",
                                    "RuntimeIdentifier": "osx-arm64"}.items():
                    ET.SubElement(properties, name).text = value
                items = ET.SubElement(project, "ItemGroup")
                for name in ("Microsoft.NETCore.App.Runtime.osx-arm64", "Microsoft.AspNetCore.App.Runtime.osx-arm64"):
                    item = ET.SubElement(items, "ResolvedRuntimePack", Include=name)
                    ET.SubElement(item, "NuGetPackageId").text = name
                    ET.SubElement(item, "NuGetPackageVersion").text = "10.0.11"
                host = ET.SubElement(items, "ResolvedSingleFileHostPack", Include="SingleFileHost")
                if installed:
                    ET.SubElement(host, "PackageDirectory").text = str(root / "packs" / "Microsoft.NETCore.App.Host.osx-arm64" / "10.0.11")
                else:
                    ET.SubElement(host, "NuGetPackageId").text = "Microsoft.NETCore.App.Host.osx-arm64"
                    ET.SubElement(host, "NuGetPackageVersion").text = "10.0.11"
                ET.SubElement(project, "Import", Project="tools/BundledPacks.targets")
                ET.SubElement(project, "Target", Name="Publish")
                path = root / "Vericairn.Cli.proj"

                def run():
                    ET.ElementTree(project).write(path, encoding="utf-8")
                    return subprocess.run(["dotnet", "msbuild", str(path), "-t:Publish", "-m:1", "-nr:false", "-nologo"],
                                          cwd=root, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=60)

                result = run()
                self.assertEqual(0, result.returncode, result.stdout)
                evidence = root / "artifacts" / "runtime-packs" / "Vericairn.Cli" / "osx-arm64.txt"
                lines = evidence.read_text(encoding="utf-8-sig").splitlines()
                self.assertEqual(["runtime|Microsoft.NETCore.App.Runtime.osx-arm64|10.0.11",
                                  "runtime|Microsoft.AspNetCore.App.Runtime.osx-arm64|10.0.11",
                                  "apphost|Microsoft.NETCore.App.Host.osx-arm64|10.0.11"], lines)
                self.assertNotIn(str(root), evidence.read_text(encoding="utf-8-sig"))
                items.remove(host)
                result = run()
                self.assertNotEqual(0, result.returncode, result.stdout)
                self.assertIn("did not resolve its runtime and single-file apphost packs", result.stdout)


if __name__ == "__main__":
    unittest.main()
