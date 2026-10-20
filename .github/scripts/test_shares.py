# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import shares


class PartitionTests(unittest.TestCase):
    def test_new_classes_are_assigned_once_without_moving_existing_classes(self):
        classes = {f"Example.Tests.Class{index}" for index in range(100)}
        original = shares.partition(classes)
        shares.check_partition(classes, original)
        self.assertTrue(all(original))
        extended = shares.partition(classes | {"Example.Tests.NewTests"})
        shares.check_partition(classes | {"Example.Tests.NewTests"}, extended)
        for before, after in zip(original, extended):
            self.assertTrue(set(before).issubset(after))
        self.assertEqual(original, shares.partition(reversed(sorted(classes))))

    def test_guard_refuses_a_class_in_no_share(self):
        with self.assertRaisesRegex(ValueError, "Missing: in 0 shares"):
            shares.check_partition({"A", "B", "Missing"}, [["A"], ["B"], []])

    def test_guard_refuses_a_class_in_two_shares(self):
        with self.assertRaisesRegex(ValueError, "A: in 2 shares"):
            shares.check_partition({"A", "B"}, [["A"], ["A", "B"], []])

    def test_guard_also_refuses_a_repetition_within_one_share(self):
        with self.assertRaisesRegex(ValueError, "A: in 2 shares"):
            shares.check_partition({"A"}, [["A", "A"], [], []])

    def test_guard_refuses_unknown_classes_and_the_wrong_number_of_shares(self):
        with self.assertRaisesRegex(ValueError, "Unknown: not a discovered test class"):
            shares.check_partition({"A"}, [["A"], ["Unknown"], []])
        with self.assertRaisesRegex(ValueError, "Expected 3 shares"):
            shares.check_partition({"A"}, [["A"]])


class DiscoveryTests(unittest.TestCase):
    assembly = Path("Example.Tests.dll")

    @staticmethod
    def node(name="Outer+Inner", namespace="Example.Tests"):
        return {"displayName": "A friendly display name, unrelated to the class", "type": {
            "assemblyFullName": "Example.Tests, Version=1.0.0.0", "namespace": namespace, "typeName": name}}

    def discover(self, nodes, selected=None):
        output = json.dumps({"schemaVersion": 1, "tests": nodes})
        with patch("shares.subprocess.run", return_value=subprocess.CompletedProcess([], 0, output)) as run:
            found = shares.discover(self.assembly, selected)
        return found, run.call_args.args[0]

    def test_class_metadata_is_independent_of_display_names_and_theory_rows(self):
        found, command = self.discover([self.node(), self.node(), self.node("Other")])
        self.assertEqual({"Example.Tests.Outer+Inner", "Example.Tests.Other"}, found)
        self.assertEqual(["dotnet", str(self.assembly), "--list-tests", "json", "--pre-enumerate-theories", "off"], command)

    def test_exact_class_filters_are_passed_as_separate_arguments(self):
        names = ["Example.Tests.Outer+Inner", "Example.Tests.Other"]
        _, command = self.discover([self.node()], names)
        self.assertEqual(["--filter-class", *names], command[-3:])

    def test_missing_class_metadata_and_empty_discovery_fail_closed(self):
        for nodes in ([], [{"displayName": "An unclassifiable test"}], [self.node("*")]):
            with self.subTest(nodes=nodes), self.assertRaisesRegex(ValueError, "invalid test discovery"):
                self.discover(nodes)

    def test_empty_filters_cannot_turn_into_an_unfiltered_run(self):
        with patch("shares.subprocess.run") as run, self.assertRaisesRegex(ValueError, "empty class filter"):
            shares.discover(self.assembly, [])
        run.assert_not_called()

    def test_a_failed_discovery_cannot_authorize_running_a_share(self):
        for code in (1, 2, 8):
            with self.subTest(code=code), patch("shares.subprocess.run", return_value=subprocess.CompletedProcess([], code, "")):
                with self.assertRaisesRegex(ValueError, f"discovery failed with exit code {code}"):
                    shares.discover(self.assembly)

    def test_guard_checks_what_the_test_platform_actually_selects(self):
        classes = {f"Example.Tests.Class{index}" for index in range(30)}
        with patch("shares.test_assemblies", return_value=[self.assembly]), patch("shares.discover", return_value=classes):
            # A runner that ignores the filter would otherwise run every class in every share.
            with self.assertRaisesRegex(ValueError, "class filter disagrees with discovery"):
                shares.checked_shares(Path("."))

    def test_guard_checks_all_three_filters(self):
        classes = {f"Example.Tests.Class{index}" for index in range(30)}

        def discover(assembly, selected=None):
            return classes if selected is None else set(selected)

        with patch("shares.test_assemblies", return_value=[self.assembly]), patch("shares.discover", side_effect=discover) as run:
            actual = shares.checked_shares(Path("."))
        self.assertEqual(shares.partition(classes), actual)
        self.assertEqual(4, run.call_count)

    def test_every_test_project_must_be_built_and_in_the_solution(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            project = root / "tests" / "Example.Tests" / "Example.Tests.csproj"
            project.parent.mkdir(parents=True)
            project.write_text("<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>")
            solution = root / "Vericairn.slnx"
            solution.write_text("<Solution />")
            with self.assertRaisesRegex(ValueError, "not in the solution"):
                shares.test_assemblies(root)
            solution.write_text('<Solution><Project Path="tests/Example.Tests/Example.Tests.csproj" /></Solution>')
            with self.assertRaisesRegex(ValueError, "missing Release build"):
                shares.test_assemblies(root)
            assembly = root / "artifacts" / "bin" / "Example.Tests" / "release" / "Example.Tests.dll"
            assembly.parent.mkdir(parents=True)
            assembly.touch()
            self.assertEqual([assembly], shares.test_assemblies(root))


if __name__ == "__main__":
    unittest.main()
