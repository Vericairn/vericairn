# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

"""Exercise the repository's SDK package gate with local packages and no feed access."""

from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


class PackageValidationTests(unittest.TestCase):
    def test_only_the_listed_break_is_accepted_and_the_gate_runs_again(self):
        with tempfile.TemporaryDirectory(prefix="api-validation-") as temporary:
            root = Path(temporary)
            shutil.copyfile(ROOT / "global.json", root / "global.json")
            source = root / "src" / "Fixture.Api"
            source.mkdir(parents=True)
            shutil.copyfile(ROOT / "src" / "Directory.Build.targets", root / "src" / "Directory.Build.targets")
            project = source / "Fixture.Api.csproj"
            project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
                               '<IsPackable>true</IsPackable><PackageId>Fixture.Api</PackageId></PropertyGroup></Project>')
            config = root / "NuGet.Config"
            config.write_text('<configuration><packageSources><clear /></packageSources></configuration>')
            expected = root / ".github" / "api-compatibility" / "Fixture.Api.xml"
            expected.parent.mkdir(parents=True)
            expected.write_text('<Suppressions />')
            code = source / "Api.cs"

            def members(*names):
                code.write_text("namespace CompatibilityFixture; public class Api { "
                                + " ".join(f"public void {name}() {{ }}" for name in names) + " }")

            def dotnet(*arguments):
                result = subprocess.run(["dotnet", *map(str, arguments), "-m:1", "-nr:false", "-p:UseSharedCompilation=false"],
                                        cwd=root, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120)
                return result.returncode, result.stdout

            def pack(version, output, baseline=None):
                arguments = ["pack", project, "--no-restore", "--configuration", "Release", "--output", output, f"-p:Version={version}"]
                if baseline is not None:
                    arguments.append(f"-p:VericairnApiBaselineRoot={baseline}")
                return dotnet(*arguments)

            members("Kept", "Removed")
            status, output = dotnet("restore", project, "--configfile", config, "-p:NuGetAudit=false")
            self.assertEqual(0, status, output)
            baselines = root / "baselines"
            status, output = pack("1.0.0", baselines)
            self.assertEqual(0, status, output)
            members("Kept", "Removed", "Introduced")
            status, output = pack("1.0.1", baselines)
            self.assertEqual(0, status, output)
            props = baselines / "Fixture.Api.props"
            props.write_text('<Project><PropertyGroup><PackageValidationBaselinePath>'
                             '$(MSBuildThisFileDirectory)Fixture.Api.1.0.0.nupkg</PackageValidationBaselinePath>'
                             '<PackageValidationBaselineVersion>1.0.0</PackageValidationBaselineVersion></PropertyGroup></Project>')
            current = root / "current"
            members("Kept", "Removed", "Added")
            status, output = pack("1.1.0", current, baselines)
            self.assertEqual(0, status, output)

            # Changing only the fetched baseline must invalidate the SDK's previous successful check.
            props.write_text(props.read_text().replace("1.0.0", "1.0.1"))
            status, output = pack("1.1.0", current, baselines)
            self.assertNotEqual(0, status, output)
            self.assertIn("CP0002", output)
            self.assertIn("Introduced", output)
            props.write_text(props.read_text().replace("1.0.1", "1.0.0"))

            members("Kept")
            status, output = pack("1.1.0", current, baselines)
            self.assertNotEqual(0, status, output)
            self.assertIn("CP0002", output)
            expected.write_text('''<Suppressions>
  <Suppression>
    <DiagnosticId>CP0002</DiagnosticId>
    <Target>M:CompatibilityFixture.Api.Removed</Target>
    <Left>lib/net10.0/Fixture.Api.dll</Left>
    <Right>lib/net10.0/Fixture.Api.dll</Right>
    <IsBaselineSuppression>true</IsBaselineSuppression>
  </Suppression>
</Suppressions>''')
            status, output = pack("1.1.0", current, baselines)
            self.assertEqual(0, status, output)

            members()
            status, output = pack("1.1.0", current, baselines)
            self.assertNotEqual(0, status, output)
            self.assertIn("CP0002", output)
            self.assertIn("Kept", output)

            members("Kept", "Removed")
            status, output = pack("1.1.0", current, baselines)
            self.assertNotEqual(0, status, output)
            self.assertIn("suppression", output.lower())

            expected.write_text('<Suppressions />')
            props.unlink()
            status, output = pack("1.1.0", current, baselines)
            self.assertNotEqual(0, status, output)
            self.assertIn("Every library requires its latest published API baseline", output)


if __name__ == "__main__":
    unittest.main()
