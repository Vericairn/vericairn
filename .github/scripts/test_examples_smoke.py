# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

import importlib.util
import os
from pathlib import Path
import shutil
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("examples_smoke", ROOT / "tools/examples/smoke.py")
SMOKE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SMOKE)
FIXTURE = ROOT / "tests/examples/read-fixture"


class ExamplesReadTests(unittest.TestCase):
    def setUp(self):
        scratch = self.enterContext(tempfile.TemporaryDirectory(prefix="example-read-test-"))
        self.scratch = Path(scratch)
        self.environment = SMOKE.isolated_environment(self.scratch)
        self.repo = self.scratch / "fixture"
        self.repo.mkdir()
        SMOKE.git(self.repo, self.environment, "init", "--quiet")
        self.example = SMOKE.examples()[0]
        for name in SMOKE.read_json(FIXTURE / "paths.json"):
            target = self.repo / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.touch()
        shutil.copyfile(FIXTURE / "README.md", self.repo / "README.md")
        shutil.copyfile(FIXTURE / "estate-manifest.json", self.repo / "estate/manifest.json")

    def commit_fixture(self):
        SMOKE.git(self.repo, self.environment, "add", "--all")
        SMOKE.git(self.repo, self.environment, "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid",
                  "-c", "commit.gpgsign=false", "commit", "--quiet", "-m", "Fixture")

    def findings(self):
        self.commit_fixture()
        return SMOKE.readme_findings(self.repo, self.environment, self.example["release"])

    def test_pinned_fixture(self):
        findings = self.findings()
        self.assertEqual([("error", "readme.relative-link", "README.md:docs/registers/decisions/")], findings)
        SMOKE.check_findings(self.example, self.example["repositories"][0], findings)

    def test_wrong_readme_release(self):
        readme = self.repo / "README.md"
        readme.write_text(readme.read_text().replace("pins [Vericairn 0.9.3]", "pins [Vericairn 0.0.1]"))
        with self.assertRaisesRegex(SMOKE.SmokeFailure, "readme.release-pin"):
            SMOKE.check_findings(self.example, self.example["repositories"][0], self.findings())

    def test_dead_relative_link_at_commit(self):
        # The file exists by the time the checker runs, in a working tree not containing it at the pin.
        link = self.repo / ".github/workflows/check.yml"
        link.unlink()
        self.commit_fixture()
        link.touch()
        findings = SMOKE.readme_findings(self.repo, self.environment, self.example["release"])
        with self.assertRaisesRegex(SMOKE.SmokeFailure, "README.md:.github/workflows/check.yml"):
            SMOKE.check_findings(self.example, self.example["repositories"][0], findings)

    def test_link_forms(self):
        content = """[relative](./a%20b)
[reference]: docs/ref.md
![image](<docs/an image.png> "tooltip")
<a href='docs/html.md'>
```markdown
[code](no-target.md)
```
"""
        self.assertCountEqual(["./a%20b", "docs/ref.md", "docs/an image.png", "docs/html.md"], SMOKE.readme_destinations(content))

    @unittest.skipUnless(os.environ.get("VERICAIRN_EXAMPLES_TEST_CLI"), "requires the candidate executable")
    def test_shipped_cli_refuses_a_broken_record(self):
        fixture = self.scratch / "records-fixture"
        shutil.copytree(ROOT / "tests/fixtures/format-1-estate", fixture)
        cli = Path(os.environ["VERICAIRN_EXAMPLES_TEST_CLI"])
        command = ["estate", "check"]
        code, before = SMOKE.run_cli(cli, fixture, self.environment, command)
        self.assertEqual((0, 0), (code, before["errors"]), before["findings"])
        next((fixture / "records/work-order").rglob("*.json")).write_text("{broken\n")
        code, after = SMOKE.run_cli(cli, fixture, self.environment, command)
        self.assertNotEqual(0, code)
        self.assertTrue(any(SMOKE.finding_key(finding)[0] == "error" and SMOKE.finding_key(finding)[2].startswith("records/work-order/")
                            for finding in after["findings"]), after["findings"])


if __name__ == "__main__":
    unittest.main()
