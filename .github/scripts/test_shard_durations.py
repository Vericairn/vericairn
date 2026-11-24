# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

import json
from pathlib import Path
import runpy
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).with_name("shard-durations.py")
durations = runpy.run_path(str(SCRIPT))


def report(assembly, tests):
    rows = "".join(f'<test name="{kind}.M{index}" type="{kind}" time="{seconds}" />' for index, (kind, seconds) in enumerate(tests))
    return (f'<assemblies schema-version="3"><assembly name="D:\\a\\artifacts\\bin\\{assembly}\\release\\{assembly}.dll">'
            f'<collection name="c">{rows}</collection></assembly></assemblies>')


class DurationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def write(self, name, text):
        path = self.root / "reports" / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
        return path

    def test_a_class_is_the_sum_of_its_tests_and_the_mean_of_its_reports(self):
        self.write("windows/A.xml", report("Example.Tests", [("Example.Tests.One", 1.5), ("Example.Tests.One", 2.5), ("Example.Tests.Two", 3)]))
        self.write("linux/A.xml", report("Example.Tests", [("Example.Tests.One", 2), ("Example.Tests.Two", 1)]))
        self.assertEqual({"Example.Tests": {"Example.Tests.One": 3.0, "Example.Tests.Two": 2.0}}, durations["measure"]([self.root / "reports"]))

    def test_the_plan_keeps_its_platforms_and_uncovered_assemblies_and_drops_gone_classes(self):
        plan = self.root / "test-shards.json"
        plan.write_text(json.dumps({"platforms": {"ubuntu-latest": {"shares": 3}},
                                    "classes": {"Example.Tests": {"Example.Tests.Gone": 9}, "Other.Tests": {"Other.Tests.Kept": 4}}}))
        self.write("A.xml", report("Example.Tests", [("Example.Tests.One", 1.234)]))
        result = subprocess.run([sys.executable, str(SCRIPT), str(self.root / "reports"), "--plan", str(plan)], capture_output=True, text=True)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual({"platforms": {"ubuntu-latest": {"shares": 3}},
                          "classes": {"Example.Tests": {"Example.Tests.One": 1.23}, "Other.Tests": {"Other.Tests.Kept": 4}}},
                         json.loads(plan.read_text()))

    def test_reports_without_test_times_write_nothing(self):
        plan = self.root / "test-shards.json"
        plan.write_text('{"platforms": {}, "classes": {}}')
        self.write("A.xml", "<assemblies />")
        result = subprocess.run([sys.executable, str(SCRIPT), str(self.root / "reports"), "--plan", str(plan)], capture_output=True, text=True)
        self.assertEqual(1, result.returncode)
        self.assertEqual('{"platforms": {}, "classes": {}}', plan.read_text())


if __name__ == "__main__":
    unittest.main()
