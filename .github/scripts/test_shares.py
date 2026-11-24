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
    def test_longest_first_balances_the_measured_seconds(self):
        measured = {"A": 10, "B": 9, "C": 8, "D": 3, "E": 2, "F": 1}
        actual = shares.partition(measured, 3, measured)
        shares.check_partition(set(measured), actual)
        self.assertEqual([["A", "F"], ["B", "E"], ["C", "D"]], actual)
        self.assertEqual([11, 11, 11], [sum(measured[name] for name in share) for share in actual])

    def test_the_same_classes_and_seconds_give_the_same_shares_in_any_order(self):
        classes = {f"Example.Tests.Class{index}" for index in range(100)}
        measured = {name: (index * 37) % 23 + 0.5 for index, name in enumerate(sorted(classes))}
        expected = shares.partition(classes, 6, measured, 0.1)
        self.assertEqual(expected, shares.partition(reversed(sorted(classes)), 6, dict(reversed(list(measured.items()))), 0.1))
        shares.check_partition(classes, expected, 6)
        self.assertTrue(all(expected))

    def test_a_class_without_a_measurement_weighs_its_assemblys_median(self):
        weights = shares.weights_for({"A", "B", "C", "New"}, {"A": 1, "B": 5, "C": 9, "Gone": 100})
        self.assertEqual({"A": 1.0, "B": 5.0, "C": 9.0, "New": 5.0}, weights)
        self.assertEqual({"X": 1.0, "Y": 1.0}, shares.weights_for({"X", "Y"}, {}))

    def test_a_reserve_gives_the_first_share_less_of_every_assembly(self):
        measured = {f"C{index:02}": 1 for index in range(40)}
        plain = shares.partition(measured, 4, measured)
        reserved = shares.partition(measured, 4, measured, 0.2)
        self.assertEqual([10, 10, 10, 10], [len(share) for share in plain])
        self.assertEqual([4, 12, 12, 12], [len(share) for share in reserved])

    def test_every_share_gets_a_class_when_the_assembly_has_enough(self):
        measured = {"Long": 100, "Short1": 1, "Short2": 1, "Short3": 1}
        actual = shares.partition(measured, 4, measured, 0.5)
        shares.check_partition(set(measured), actual, 4)
        self.assertTrue(all(actual), actual)
        self.assertEqual([[], ["Only"]], shares.partition({"Only"}, 2, {}, 0.5))

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
        with patch("shares.load_plan", return_value=None), patch("shares.test_assemblies", return_value=[self.assembly]), \
                patch("shares.discover", return_value=classes):
            # A runner that ignores the filter would otherwise run every class in every share.
            with self.assertRaisesRegex(ValueError, "class filter disagrees with discovery"):
                shares.checked_shares(Path("."))

    def test_guard_checks_all_three_filters(self):
        classes = {f"Example.Tests.Class{index}" for index in range(30)}

        def discover(assembly, selected=None):
            return classes if selected is None else set(selected)

        with patch("shares.load_plan", return_value=None), patch("shares.test_assemblies", return_value=[self.assembly]), \
                patch("shares.discover", side_effect=discover) as run:
            actual = shares.checked_shares(Path("."))
        self.assertEqual(shares.partition(classes), actual)
        self.assertEqual(4, run.call_count)

    def test_a_platform_takes_its_share_count_reserve_and_seconds_from_the_plan(self):
        classes = {f"Example.Tests.Class{index}" for index in range(30)}
        measured = {name: index + 1 for index, name in enumerate(sorted(classes))}
        plan = {"platforms": {"windows-latest": {"shares": 6, "reserve": 0.1}}, "classes": {"Example.Tests": measured}}

        def discover(assembly, selected=None):
            return classes if selected is None else set(selected)

        with patch("shares.load_plan", return_value=plan), patch("shares.test_assemblies", return_value=[self.assembly]), \
                patch("shares.discover", side_effect=discover) as run:
            actual = shares.checked_shares(Path("."), "windows-latest")
            with self.assertRaisesRegex(ValueError, "names no platform 'macos-latest'"):
                shares.checked_shares(Path("."), "macos-latest")
        self.assertEqual(shares.partition(classes, 6, measured, 0.1), actual)
        self.assertEqual(7, run.call_count)

    def test_platform_settings_refuse_an_unusable_count_or_reserve(self):
        for entry in ({"shares": 0}, {"shares": 13}, {"shares": "3"}, {"shares": True}, {"shares": 3, "reserve": 1},
                      {"shares": 3, "reserve": -0.1}, {"shares": 3, "reserve": "0.1"}):
            with self.subTest(entry=entry), self.assertRaises(ValueError):
                shares.platform_settings({"platforms": {"p": entry}, "classes": {}}, "p")
        self.assertEqual((4, 0.0), shares.platform_settings({"platforms": {"p": {"shares": 4}}, "classes": {}}, "p"))

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


class CommittedPlanTests(unittest.TestCase):
    """The repository's own shard plan: every platform ci-plan.py runs, with a usable count and reserve, and seconds for
    every test project's classes."""

    root = Path(__file__).resolve().parents[2]

    def test_the_plan_names_every_platform_with_a_usable_count_and_reserve(self):
        plan = shares.load_plan(self.root)
        self.assertEqual({"ubuntu-latest", "macos-latest", "windows-latest"}, set(plan["platforms"]))
        for platform in plan["platforms"]:
            count, reserve = shares.platform_settings(plan, platform)
            self.assertGreaterEqual(count, 3)
            self.assertLess(reserve, 0.5)
        projects = {path.stem for path in (self.root / "tests").rglob("*.csproj") if "<IsTestProject>true" in path.read_text()}
        self.assertEqual(projects, set(plan["classes"]))
        for assembly, measured in plan["classes"].items():
            self.assertTrue(measured, assembly)
            self.assertTrue(all(name.startswith(assembly + ".") and seconds >= 0 for name, seconds in measured.items()), assembly)
