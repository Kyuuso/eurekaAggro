from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock


SCRIPT = Path(__file__).resolve().parents[1] / "scripts" / "runtests.py"
SPEC = importlib.util.spec_from_file_location("runtests_script", SCRIPT)
assert SPEC and SPEC.loader
runtests = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runtests)


class RunTestsScriptTests(unittest.TestCase):
    def make_project(self, source: str = "def add(left, right):\n    return left + right\n") -> tuple[tempfile.TemporaryDirectory[str], Path]:
        temporary = tempfile.TemporaryDirectory()
        root = Path(temporary.name)
        (root / "app.py").write_text(source, encoding="utf-8")
        return temporary, root

    def mark_all_tested(
        self,
        root: Path,
        test_reference: str = "tests/test_app.py::AppTests.test_add",
    ) -> None:
        paths = runtests.project_paths(root)
        test_path_text, _, selector = test_reference.partition("::")
        test_path = root / test_path_text
        test_path.parent.mkdir(parents=True, exist_ok=True)
        if selector and (
            not test_path.exists()
            or not runtests.test_selector_exists(test_path, selector)
        ):
            with test_path.open("a", encoding="utf-8") as handle:
                handle.write(
                "import unittest\n\n"
                "class AppTests(unittest.TestCase):\n"
                "    def test_add(self):\n"
                "        self.assertTrue(True)\n",
                )
        functions = runtests.load_json(paths["functions"], {})["functions"]
        coverage = runtests.load_json(paths["coverage"], {})
        execution = runtests.load_json(paths["execution"], {})
        for function in functions:
            coverage["functions"][function["id"]] = {
                "source_sha256": function["source_sha256"],
                "status": "tested",
                "tests": [test_reference],
                "notes": "Covered by the fixture regression suite.",
            }
            execution["functions"][function["id"]] = {
                "source_sha256": function["source_sha256"],
                "status": "executed",
                "hits": 1,
                "tests": [test_reference],
            }
        runtests.write_json(paths["coverage"], coverage)
        runtests.write_json(paths["execution"], execution)

    def test_bootstrap_creates_required_layout_and_ignores_new_workspace(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        subprocess.run(["git", "init", "--quiet", str(root)], check=True)

        result = runtests.bootstrap(root, track_tests=False)

        paths = runtests.project_paths(root)
        self.assertTrue(paths["errors"].is_dir())
        self.assertTrue(paths["functions_dir"].is_dir())
        self.assertTrue(paths["history_dir"].is_dir())
        self.assertTrue(paths["readme"].is_file())
        self.assertTrue(paths["manifest"].is_file())
        self.assertIn("/tests/", (root / ".gitignore").read_text(encoding="utf-8"))
        self.assertEqual("added", result["git_policy"])
        self.assertEqual("already-ignored", runtests.ensure_tests_gitignore(root))

    def test_bootstrap_preserves_existing_tracked_tests(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        subprocess.run(["git", "init", "--quiet", str(root)], check=True)
        tests = root / "tests"
        tests.mkdir()
        (tests / "existing.txt").write_text("tracked\n", encoding="utf-8")
        subprocess.run(["git", "-C", str(root), "add", "tests/existing.txt"], check=True)

        result = runtests.bootstrap(root, track_tests=False)

        self.assertEqual("preserved-existing-tracked-tests", result["git_policy"])
        self.assertFalse((root / ".gitignore").exists())

    def test_tracked_tests_are_detected_from_a_nested_project_root(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        repository = Path(temporary.name)
        project = repository / "apps" / "sample"
        (project / "tests").mkdir(parents=True)
        (project / "app.py").write_text("def run():\n    return True\n", encoding="utf-8")
        (project / "tests" / "existing.txt").write_text("tracked\n", encoding="utf-8")
        subprocess.run(["git", "init", "--quiet", str(repository)], check=True)
        subprocess.run(["git", "-C", str(repository), "add", "apps/sample/tests/existing.txt"], check=True)

        result = runtests.bootstrap(project, track_tests=False)

        self.assertEqual("preserved-existing-tracked-tests", result["git_policy"])
        self.assertFalse((project / ".gitignore").exists())

    def test_python_inventory_tracks_nested_methods_changes_and_removals(self) -> None:
        source = """def outer(value):
    def nested():
        return value
    return nested()

class Example:
    async def method(self):
        return 1
"""
        temporary, root = self.make_project(source)
        self.addCleanup(temporary.cleanup)
        result = runtests.bootstrap(root, track_tests=True)
        self.assertEqual(3, result["function_count"])
        functions = runtests.load_json(runtests.project_paths(root)["functions"], {})["functions"]
        self.assertEqual(
            {"app.py::outer", "app.py::outer.nested", "app.py::Example.method"},
            {item["id"] for item in functions},
        )

        (root / "app.py").write_text("def outer(value):\n    return value + 1\n", encoding="utf-8")
        changed = runtests.inventory_project(root)
        self.assertEqual(["app.py::outer"], changed["changed"])
        self.assertEqual(["app.py::Example.method", "app.py::outer.nested"], changed["removed"])
        changes_text = runtests.project_paths(root)["changes"].read_text(encoding="utf-8")
        self.assertIn("Generated inventory delta", changes_text)
        self.assertIn("app.py::Example.method", changes_text)

    def test_inventory_prunes_default_backup_and_rollback_directories(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        for relative in (
            "backups/root-copy.py",
            "src/backups/nested-copy.py",
            "docs/.rollback/old-copy.py",
        ):
            path = root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("def should_never_be_inventoried():\n    return False\n", encoding="utf-8")

        result = runtests.bootstrap(root, track_tests=True)

        self.assertEqual(1, result["function_count"])
        functions = runtests.load_json(runtests.project_paths(root)["functions"], {})["functions"]
        self.assertEqual(["app.py::add"], [item["id"] for item in functions])

    def test_manifest_double_star_exclusion_matches_root_and_nested_directories(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        for relative in ("snapshots/root.py", "src/snapshots/nested.py"):
            path = root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("def excluded():\n    return False\n", encoding="utf-8")
        runtests.bootstrap(root, track_tests=True)
        paths = runtests.project_paths(root)
        manifest = runtests.load_json(paths["manifest"], {})
        manifest["inventory"]["exclude"] = ["**/snapshots/**"]
        runtests.write_json(paths["manifest"], manifest)

        result = runtests.inventory_project(root)

        self.assertEqual(1, result["function_count"])
        functions = runtests.load_json(paths["functions"], {})["functions"]
        self.assertEqual(["app.py::add"], [item["id"] for item in functions])

    def test_manifest_exclusion_preserves_leading_dot_directory_name(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        (root / ".Research" / "pkg").mkdir(parents=True)
        (root / ".Research" / "pkg" / "hidden.py").write_text(
            "def hidden():\n    return 1\n", encoding="utf-8"
        )
        (root / "keep.py").write_text("def keep():\n    return 2\n", encoding="utf-8")
        runtests.bootstrap(root, track_tests=True)
        manifest_path = runtests.project_paths(root)["manifest"]
        manifest = runtests.load_json(manifest_path, {})
        manifest["inventory"]["exclude"] = [".Research/**"]
        runtests.write_json(manifest_path, manifest)

        result = runtests.inventory_project(root)
        functions = runtests.load_json(
            runtests.project_paths(root)["functions"], {}
        )["functions"]

        self.assertEqual(1, result["function_count"])
        self.assertEqual(["keep.py::keep"], [item["id"] for item in functions])

    def test_large_inventory_delta_is_bounded(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        changes = Path(temporary.name) / "changes.md"
        values = [f"src/file_{index}.py::run" for index in range(200)]

        runtests.append_inventory_changes(changes, values, [], [])

        content = changes.read_text(encoding="utf-8")
        self.assertIn("200 total; showing first", content)
        self.assertIn("src/file_0.py::run", content)
        self.assertNotIn("src/file_199.py::run", content)
        self.assertLess(len(content), 10_000)

    def test_regex_inventory_supports_multiple_languages_and_stable_duplicates(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        (root / "sample.cs").write_text(
            "class C {\n public int Add(int x) { return x + 1; }\n public int Add(string x) { return x.Length; }\n}\n",
            encoding="utf-8",
        )
        (root / "sample.ts").write_text(
            "export function first() { return 1; }\nconst second = () => 2;\n",
            encoding="utf-8",
        )
        runtests.bootstrap(root, track_tests=True)

        functions = runtests.load_json(runtests.project_paths(root)["functions"], {})["functions"]
        identifiers = {item["id"] for item in functions}
        self.assertIn("sample.cs::Add", identifiers)
        self.assertIn("sample.cs::Add#2", identifiers)
        self.assertIn("sample.ts::first", identifiers)
        self.assertIn("sample.ts::second", identifiers)

    def test_cpp_inventory_rejects_call_expressions_as_function_definitions(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        (root / "sample.cpp").write_text(
            "bool real_function() {\n"
            "    if constexpr (true) { return true; }\n"
            "    return std::ranges::any_of(values, [](auto value) { return value; });\n"
            "}\n"
            "auto parsed() {\n"
            "    return parse_enum(value, values, [](auto candidate) { return candidate; });\n"
            "}\n"
            "void metrics() {\n"
            "    api_call(SPI_VALUE, sizeof(buffer), [](auto item) { return item; });\n"
            "}\n",
            encoding="utf-8",
        )

        runtests.bootstrap(root, track_tests=True)

        functions = runtests.load_json(runtests.project_paths(root)["functions"], {})["functions"]
        identifiers = {item["id"] for item in functions}
        self.assertEqual(
            {"sample.cpp::real_function", "sample.cpp::parsed", "sample.cpp::metrics"},
            identifiers,
        )
        self.assertTrue(all(item["end_line"] >= item["line"] for item in functions))

    def test_cpp_inventory_records_complete_multiline_function_range_and_body_hash(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        (root / "sample.cpp").write_text(
            "\nint add_values(\n"
            "    const int left,\n"
            "    const int right) {\n"
            "    return left + right;\n"
            "}\n",
            encoding="utf-8",
        )
        runtests.bootstrap(root, track_tests=True)

        function = runtests.load_json(
            runtests.project_paths(root)["functions"], {}
        )["functions"][0]
        self.assertEqual(2, function["line"])
        self.assertEqual(6, function["end_line"])
        self.assertNotEqual(runtests.function_hash([]), function["source_sha256"])

    def test_regex_inventory_ignores_braces_in_comments_templates_and_raw_strings(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        (root / "sample.cpp").write_text(
            "int first() {\n"
            "    // } must not close the function\n"
            "    const char* raw = R\"tag(})tag\";\n"
            "    /* another } */\n"
            "    return 1;\n"
            "}\n"
            "int second() { return 2; }\n",
            encoding="utf-8",
        )
        (root / "sample.js").write_text(
            "function render() {\n"
            "  const value = `template } text`;\n"
            "  return value;\n"
            "}\n"
            "function next() { return 2; }\n",
            encoding="utf-8",
        )

        result = runtests.bootstrap(root, track_tests=True)
        functions = {
            item["id"]: item
            for item in runtests.load_json(
                runtests.project_paths(root)["functions"], {}
            )["functions"]
        }

        self.assertEqual(4, result["function_count"])
        self.assertEqual(6, functions["sample.cpp::first"]["end_line"])
        self.assertEqual(7, functions["sample.cpp::second"]["line"])
        self.assertEqual(4, functions["sample.js::render"]["end_line"])
        self.assertEqual(5, functions["sample.js::next"]["line"])

    def test_pester_detection_falls_back_to_windows_powershell(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        tests = root / "tests"
        tests.mkdir()
        (tests / "Contract.Tests.ps1").write_text("Describe 'contract' {}\n", encoding="utf-8")

        with mock.patch.object(runtests.shutil, "which", return_value=None):
            commands = runtests.detect_commands(root)

        pester = next(command for command in commands if command["name"] == "powershell-pester")
        self.assertEqual("powershell", pester["argv"][0])

    def test_coverage_gate_detects_missing_stale_and_invalid_mappings(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        runtests.bootstrap(root, track_tests=True)
        issues, count = runtests.audit_coverage(root)
        self.assertEqual(1, count)
        self.assertTrue(any(issue.startswith("UNTESTED") for issue in issues))

        tests = root / "tests" / "test_app.py"
        tests.write_text("# regression test fixture\n", encoding="utf-8")
        self.mark_all_tested(root)
        self.assertEqual([], runtests.audit_coverage(root)[0])

        (root / "app.py").write_text("def add(left, right):\n    return left - right\n", encoding="utf-8")
        runtests.inventory_project(root)
        self.assertTrue(any(issue.startswith("STALE") for issue in runtests.audit_coverage(root)[0]))

        coverage_path = runtests.project_paths(root)["coverage"]
        coverage = runtests.load_json(coverage_path, {})
        entry = coverage["functions"]["app.py::add"]
        entry["source_sha256"] = runtests.load_json(runtests.project_paths(root)["functions"], {})["functions"][0]["source_sha256"]
        entry["tests"] = ["../outside.py"]
        runtests.write_json(coverage_path, coverage)
        self.assertTrue(any(issue.startswith("INVALID") for issue in runtests.audit_coverage(root)[0]))

    def test_coverage_gate_requires_an_existing_exact_test_selector(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        runtests.bootstrap(root, track_tests=True)
        test_file = root / "tests" / "test_app.py"
        test_file.write_text(
            "import unittest\n\n"
            "class AppTests(unittest.TestCase):\n"
            "    def test_add(self):\n"
            "        self.assertEqual(3, 1 + 2)\n",
            encoding="utf-8",
        )

        self.mark_all_tested(root)
        coverage_path = runtests.project_paths(root)["coverage"]
        coverage = runtests.load_json(coverage_path, {})
        coverage["functions"]["app.py::add"]["tests"] = [
            "tests/test_app.py::AppTests.test_missing"
        ]
        runtests.write_json(coverage_path, coverage)
        issues, _ = runtests.audit_coverage(root)

        self.assertTrue(any(issue.startswith("INVALID_SELECTOR") for issue in issues))

    def test_coverage_gate_rejects_selectorless_test_reference(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        runtests.bootstrap(root, track_tests=True)
        test_file = root / "tests" / "test_app.py"
        test_file.write_text(
            "import unittest\n\nclass AppTests(unittest.TestCase):\n"
            "    def test_add(self):\n        self.assertEqual(3, 1 + 2)\n",
            encoding="utf-8",
        )
        paths = runtests.project_paths(root)
        function = runtests.load_json(paths["functions"], {})["functions"][0]
        coverage = runtests.load_json(paths["coverage"], {})
        coverage["functions"][function["id"]] = {
            "source_sha256": function["source_sha256"],
            "status": "tested",
            "tests": ["tests/test_app.py"],
        }
        runtests.write_json(paths["coverage"], coverage)

        issues, _ = runtests.audit_coverage(root)

        self.assertTrue(any(issue.startswith("INVALID_SELECTOR") for issue in issues))

    def test_python_parse_failure_aborts_inventory_with_file_and_line(self) -> None:
        temporary, root = self.make_project("def broken(:\n    pass\n")
        self.addCleanup(temporary.cleanup)

        with self.assertRaisesRegex(RuntimeError, r"app\.py.*line 1"):
            runtests.bootstrap(root, track_tests=True)

    def test_execution_gate_requires_current_measured_function_hits(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        runtests.bootstrap(root, track_tests=True)
        test_file = root / "tests" / "test_app.py"
        test_file.write_text(
            "import unittest\n\n"
            "class AppTests(unittest.TestCase):\n"
            "    def test_add(self):\n"
            "        self.assertEqual(3, 1 + 2)\n",
            encoding="utf-8",
        )
        reference = "tests/test_app.py::AppTests.test_add"
        self.mark_all_tested(root, reference)
        runtests.write_json(
            runtests.project_paths(root)["execution"],
            {"version": 1, "functions": {}},
        )

        issues, count, executed = runtests.audit_execution(root)
        self.assertEqual(1, count)
        self.assertEqual(0, executed)
        self.assertTrue(any(issue.startswith("UNEXECUTED") for issue in issues))

        functions = runtests.load_json(runtests.project_paths(root)["functions"], {})["functions"]
        runtests.write_json(runtests.project_paths(root)["execution"], {
            "version": 1,
            "functions": {
                functions[0]["id"]: {
                    "source_sha256": functions[0]["source_sha256"],
                    "status": "executed",
                    "hits": 1,
                    "tests": [reference],
                }
            },
        })
        self.assertEqual(([], 1, 1), runtests.audit_execution(root))

    def test_msvc_collector_records_exact_test_that_executed_function(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        (root / "src").mkdir()
        source = root / "src" / "sample.cpp"
        source.write_text("int add_one(int value) {\n    return value + 1;\n}\n", encoding="utf-8")
        (root / "tests").mkdir()
        test_file = root / "tests" / "UnitTests.cpp"
        test_file.write_text('XA_WDD_TEST("add one executes") { }\n', encoding="utf-8")
        executable = root / "build" / "tests.exe"
        executable.parent.mkdir()
        executable.write_bytes(b"test executable")
        tool = root / "coverage.exe"
        tool.write_bytes(b"coverage tool")
        runtests.bootstrap(root, track_tests=True)

        def fake_run(argv: list[str], **kwargs: object) -> subprocess.CompletedProcess[str]:
            if argv[1:] == ["--list"]:
                return subprocess.CompletedProcess(argv, 0, stdout="TEST add one executes\n", stderr="")
            report = Path(argv[argv.index("--output") + 1])
            escaped_source = str(source.resolve()).replace("&", "&amp;")
            report.write_text(
                "<results><modules><module>"
                f'<source_files><source_file id="1" path="{escaped_source}" /></source_files>'
                '<functions><function><ranges><range source_id="1" start_line="1" '
                'end_line="3" covered="yes" /></ranges></function></functions>'
                "</module></modules></results>",
                encoding="utf-8",
            )
            return subprocess.CompletedProcess(argv, 0, stdout="", stderr="")

        with mock.patch.object(runtests.subprocess, "run", side_effect=fake_run):
            result = runtests.collect_msvc_function_execution(root, {
                "tool_path": str(tool),
                "timeout_seconds": 30,
                "test_targets": [{
                    "test_file": "tests/UnitTests.cpp",
                    "executable": "build/tests.exe",
                }],
            })

        self.assertEqual({"cases_run": 1, "functions": 1, "executed": 1}, result)
        entry = runtests.load_json(
            runtests.project_paths(root)["execution"], {}
        )["functions"]["src/sample.cpp::add_one"]
        self.assertEqual("executed", entry["status"])
        self.assertGreater(entry["hits"], 0)
        self.assertEqual(["tests/UnitTests.cpp::add one executes"], entry["tests"])
        self.assertEqual([], runtests.audit_coverage(root)[0])
        self.assertEqual([], runtests.audit_execution(root)[0])

    def test_msvc_collector_accepts_marker_only_for_named_compiler_exclusion(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        (root / "src").mkdir()
        (root / "src" / "sample.cpp").write_text(
            "void marker_only() {\n    external_branch();\n}\n",
            encoding="utf-8",
        )
        (root / "tests").mkdir()
        (root / "tests" / "UnitTests.cpp").write_text(
            'XA_WDD_TEST("marker executes") { }\n', encoding="utf-8"
        )
        executable = root / "build" / "tests.exe"
        executable.parent.mkdir()
        executable.write_bytes(b"test executable")
        tool = root / "coverage.exe"
        tool.write_bytes(b"coverage tool")
        runtests.bootstrap(root, track_tests=True)

        def fake_run(argv: list[str], **kwargs: object) -> subprocess.CompletedProcess[str]:
            if argv[1:] == ["--list"]:
                return subprocess.CompletedProcess(argv, 0, stdout="TEST marker executes\n", stderr="")
            report = Path(argv[argv.index("--output") + 1])
            report.write_text(
                "<results><modules><module><skipped_functions>"
                '<skipped_function name="marker_only(class std::string const &amp;)" type_name="" '
                'reason="has_external_branch" />'
                "</skipped_functions></module></modules></results>",
                encoding="utf-8",
            )
            return subprocess.CompletedProcess(
                argv,
                0,
                stdout="PASS marker executes\nFUNCTION_HIT src/sample.cpp::marker_only\n",
                stderr="",
            )

        with mock.patch.object(runtests.subprocess, "run", side_effect=fake_run):
            result = runtests.collect_msvc_function_execution(root, {
                "tool_path": str(tool),
                "allow_explicit_hit_markers": True,
                "test_targets": [{
                    "test_file": "tests/UnitTests.cpp",
                    "executable": "build/tests.exe",
                }],
            })

        self.assertEqual(1, result["executed"])
        entry = runtests.load_json(
            runtests.project_paths(root)["execution"], {}
        )["functions"]["src/sample.cpp::marker_only"]
        self.assertEqual(["compiler-exclusion-runtime-marker"], entry["evidence_kinds"])
        self.assertEqual(["tests/UnitTests.cpp::marker executes"], entry["tests"])

    def test_run_collects_execution_and_reaudits_collector_owned_mappings(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        runtests.bootstrap(root, track_tests=True)
        (root / "tests" / "test_app.py").write_text(
            "import unittest\n\nclass AppTests(unittest.TestCase):\n"
            "    def test_add(self):\n        self.assertEqual(3, 1 + 2)\n",
            encoding="utf-8",
        )
        reference = "tests/test_app.py::AppTests.test_add"
        self.mark_all_tested(root, reference)
        (root / "app.py").write_text(
            "def add(left, right):\n    return left + right + 0\n", encoding="utf-8"
        )
        paths = runtests.project_paths(root)
        runtests.write_json(paths["execution"], {"version": 1, "functions": {}})
        manifest = runtests.load_json(paths["manifest"], {})
        manifest["function_execution"]["collector"] = {"kind": "fixture"}
        runtests.write_json(paths["manifest"], manifest)

        def collect_fixture(_: Path) -> dict[str, int]:
            function = runtests.load_json(paths["functions"], {})["functions"][0]
            coverage = runtests.load_json(paths["coverage"], {})
            coverage["functions"][function["id"]].update({
                "source_sha256": function["source_sha256"],
                "status": "tested",
                "tests": [reference],
            })
            runtests.write_json(paths["coverage"], coverage)
            runtests.write_json(paths["execution"], {
                "version": 1,
                "functions": {
                    function["id"]: {
                        "source_sha256": function["source_sha256"],
                        "status": "executed",
                        "hits": 1,
                        "tests": [reference],
                    }
                },
            })
            return {"cases_run": 1, "functions": 1, "executed": 1}

        buffer = io.StringIO()
        with mock.patch.object(runtests, "collect_function_execution", side_effect=collect_fixture):
            with contextlib.redirect_stdout(buffer):
                self.assertEqual(0, runtests.command_run(root))

        output = buffer.getvalue()
        self.assertIn("FUNCTIONS_EXECUTED=1", output)
        self.assertIn("EXECUTION_GAPS=0", output)
        report = next((root / "docs").glob("runtests_*.md"))
        report_text = report.read_text(encoding="utf-8")
        self.assertNotIn("replace every pending gameplan field", report_text.lower())

    def test_run_records_execution_collector_failure_as_durable_evidence(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        runtests.bootstrap(root, track_tests=True)
        (root / "tests" / "test_app.py").write_text(
            "import unittest\n\nclass AppTests(unittest.TestCase):\n"
            "    def test_add(self):\n        self.assertEqual(3, 1 + 2)\n",
            encoding="utf-8",
        )
        self.mark_all_tested(root)
        paths = runtests.project_paths(root)
        manifest = runtests.load_json(paths["manifest"], {})
        manifest["function_execution"]["collector"] = {"kind": "broken"}
        runtests.write_json(paths["manifest"], manifest)

        with mock.patch.object(
            runtests,
            "collect_function_execution",
            side_effect=RuntimeError("simulated collector failure"),
        ):
            self.assertEqual(1, runtests.command_run(root))

        collector_evidence = list(paths["errors"].glob("*function-execution-collector*.md"))
        self.assertEqual(1, len(collector_evidence))
        self.assertIn("simulated collector failure", collector_evidence[0].read_text(encoding="utf-8"))

    def test_run_executes_native_tests_and_passes_complete_gate(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        test_file = root / "tests" / "test_app.py"
        test_file.parent.mkdir()
        test_file.write_text(
            "import unittest\n\nclass AppTests(unittest.TestCase):\n    def test_add(self):\n        self.assertEqual(3, 1 + 2)\n",
            encoding="utf-8",
        )
        runtests.bootstrap(root, track_tests=True)
        self.mark_all_tested(root)

        self.assertEqual(0, runtests.command_run(root))
        self.assertEqual([], list(runtests.project_paths(root)["errors"].glob("*.md")))

    def test_run_records_command_failures_without_a_shell(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        runtests.bootstrap(root, track_tests=True)
        manifest_path = runtests.project_paths(root)["manifest"]
        manifest = runtests.load_json(manifest_path, {})
        manifest["commands"] = [runtests.command_record("intentional failure", ["python", "-c", "raise SystemExit(7)"])]
        runtests.write_json(manifest_path, manifest)

        self.assertEqual(1, runtests.command_run(root))
        evidence = list(runtests.project_paths(root)["errors"].glob("*.md"))
        self.assertEqual(1, len(evidence))
        self.assertIn("Exit code: 7", evidence[0].read_text(encoding="utf-8"))

    def test_run_creates_count_based_daily_report_and_gameplan(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        runtests.bootstrap(root, track_tests=True)
        manifest_path = runtests.project_paths(root)["manifest"]
        manifest = runtests.load_json(manifest_path, {})
        manifest["commands"] = [runtests.command_record(
            "intentional failure",
            ["python", "-c", "print('actionable failure'); raise SystemExit(7)"],
        )]
        runtests.write_json(manifest_path, manifest)

        buffer = io.StringIO()
        with contextlib.redirect_stdout(buffer):
            self.assertEqual(1, runtests.command_run(root))

        reports = list((root / "docs").glob("runtests_??_??_??-??-??-??.md"))
        self.assertEqual(1, len(reports))
        content = reports[0].read_text(encoding="utf-8")
        self.assertIn("0 test commands passed; 1 failed", content)
        self.assertIn("0/1 mappings are current", content)
        self.assertIn("0/1 functions have measured execution", content)
        self.assertIn("tests/current functions/coverage.json", content)
        self.assertIn("Investigation and gameplan", content)
        self.assertIn("Root cause: Pending source-backed diagnosis", content)
        self.assertIn("Before: Capture the current failing behavior", content)
        self.assertIn("After: Describe the intended behavior", content)
        self.assertIn("actionable failure", content)
        self.assertNotIn("not accepted", content.lower())
        output = buffer.getvalue()
        self.assertIn("TEST_COMMANDS_PASSED=0", output)
        self.assertIn("TEST_COMMANDS_FAILED=1", output)
        self.assertIn("FUNCTION_MAPPINGS_CURRENT=0", output)
        self.assertIn("MAPPING_GAPS=1", output)
        self.assertIn("FUNCTIONS_EXECUTED=0", output)
        self.assertIn("EXECUTION_GAPS=1", output)
        self.assertIn("RUNTESTS_REPORT=docs/runtests_", output)

    def test_daily_report_path_includes_local_run_timestamp(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        when = runtests.dt.datetime(
            2026, 8, 4, 18, 5, 7,
            tzinfo=runtests.dt.timezone(runtests.dt.timedelta(hours=-6)),
        )

        report = runtests.daily_report_path(root, when)

        self.assertEqual("runtests_08_04_26-18-05-07.md", report.name)

    def test_available_daily_report_path_preserves_same_second_runs(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        (root / "docs").mkdir()
        when = runtests.dt.datetime(
            2026, 8, 4, 18, 5, 7,
            tzinfo=runtests.dt.timezone(runtests.dt.timedelta(hours=-6)),
        )
        runtests.daily_report_path(root, when).write_text("first run\n", encoding="utf-8")

        report = runtests.available_daily_report_path(root, when)

        self.assertEqual("runtests_08_04_26-18-05-08.md", report.name)
        self.assertEqual(
            "first run\n",
            runtests.daily_report_path(root, when).read_text(encoding="utf-8"),
        )

    def test_timestamped_reports_preserve_failure_then_record_passing_rerun(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        runtests.bootstrap(root, track_tests=True)
        paths = runtests.project_paths(root)
        manifest = runtests.load_json(paths["manifest"], {})
        manifest["commands"] = [runtests.command_record(
            "intentional failure", ["python", "-c", "raise SystemExit(4)"],
        )]
        runtests.write_json(paths["manifest"], manifest)
        failed_report = root / "docs" / "runtests_08_04_26-18-05-07.md"
        with mock.patch.object(runtests, "daily_report_path", return_value=failed_report):
            self.assertEqual(1, runtests.command_run(root))
        self.assertIn("1 failed", failed_report.read_text(encoding="utf-8"))

        (root / "tests" / "test_app.py").write_text("# passing regression\n", encoding="utf-8")
        self.mark_all_tested(root)
        manifest["commands"] = [runtests.command_record(
            "passing command", ["python", "-c", "raise SystemExit(0)"],
        )]
        runtests.write_json(paths["manifest"], manifest)
        passed_report = root / "docs" / "runtests_08_04_26-18-06-12.md"
        with mock.patch.object(runtests, "daily_report_path", return_value=passed_report):
            self.assertEqual(0, runtests.command_run(root))

        failed_content = failed_report.read_text(encoding="utf-8")
        passed_content = passed_report.read_text(encoding="utf-8")
        self.assertIn("1 failed", failed_content)
        self.assertIn("intentional failure", failed_content)
        self.assertIn("Automated gates passed: 1 test commands passed; 0 failed", passed_content)
        self.assertIn("No automated function or test-command failures", passed_content)
        self.assertNotIn("intentional failure", passed_content)

    def test_daily_report_redacts_failed_command_output(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        runtests.bootstrap(root, track_tests=True)
        paths = runtests.project_paths(root)
        manifest = runtests.load_json(paths["manifest"], {})
        manifest["commands"] = [runtests.command_record(
            "secret output",
            ["python", "-c", "print('access_token=TOPSECRET123'); raise SystemExit(2)"],
        )]
        runtests.write_json(paths["manifest"], manifest)

        self.assertEqual(1, runtests.command_run(root))

        reports = list((root / "docs").glob("runtests_??_??_??-??-??-??.md"))
        self.assertEqual(1, len(reports))
        content = reports[0].read_text(encoding="utf-8")
        self.assertIn("access_token=[REDACTED]", content)
        self.assertNotIn("TOPSECRET123", content)

    def test_failure_evidence_redacts_common_credentials(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        runtests.bootstrap(root, track_tests=True)

        evidence = runtests.record_failure(root, "redaction", "access_token=TOPSECRET123")

        content = evidence.read_text(encoding="utf-8")
        self.assertIn("access_token=[REDACTED]", content)
        self.assertNotIn("TOPSECRET123", content)

    def test_manifest_rejects_manual_function_outside_project(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        runtests.bootstrap(root, track_tests=True)
        manifest_path = runtests.project_paths(root)["manifest"]
        manifest = runtests.load_json(manifest_path, {})
        manifest["inventory"]["manual_functions"] = [
            {"id": "outside::bad", "path": "../outside.py", "name": "bad"}
        ]
        runtests.write_json(manifest_path, manifest)

        with self.assertRaisesRegex(RuntimeError, "manual function path is invalid"):
            runtests.inventory_project(root)

    def test_cli_reports_invalid_project(self) -> None:
        missing = str(Path(tempfile.gettempdir()) / "runtests-definitely-missing-project")
        self.assertEqual(2, runtests.main(["check", "--project", missing]))

    def test_init_is_idempotent_and_never_overwrites_existing_files(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        runtests.bootstrap(root, track_tests=True)
        paths = runtests.project_paths(root)

        paths["readme"].write_text("SENTINEL README\n", encoding="utf-8")
        with paths["changes"].open("a", encoding="utf-8") as handle:
            handle.write("\nSENTINEL CHANGES\n")
        manifest = runtests.load_json(paths["manifest"], {})
        manifest["project_name"] = "sentinel-project"
        runtests.write_json(paths["manifest"], manifest)

        second = runtests.bootstrap(root, track_tests=True)

        self.assertEqual([], second["created"])
        self.assertEqual("SENTINEL README\n", paths["readme"].read_text(encoding="utf-8"))
        self.assertIn("SENTINEL CHANGES", paths["changes"].read_text(encoding="utf-8"))
        self.assertEqual(
            "sentinel-project",
            runtests.load_json(paths["manifest"], {})["project_name"],
        )

    def test_inventory_preserves_tested_coverage_when_functions_json_is_reset(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        runtests.bootstrap(root, track_tests=True)
        (root / "tests" / "test_app.py").write_text("# regression test fixture\n", encoding="utf-8")
        self.mark_all_tested(root)

        runtests.project_paths(root)["functions"].unlink()
        result = runtests.inventory_project(root)

        self.assertEqual(["app.py::add"], result["added"])
        coverage = runtests.load_json(runtests.project_paths(root)["coverage"], {})
        entry = coverage["functions"]["app.py::add"]
        self.assertEqual("tested", entry["status"])
        self.assertEqual(["tests/test_app.py::AppTests.test_add"], entry["tests"])
        self.assertEqual([], runtests.audit_coverage(root)[0])

    def test_inventory_marks_removed_history_and_reappearance(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        source = (root / "app.py").read_text(encoding="utf-8")
        runtests.bootstrap(root, track_tests=True)
        (root / "tests" / "test_app.py").write_text("# regression test fixture\n", encoding="utf-8")
        self.mark_all_tested(root)

        (root / "app.py").unlink()
        removed_run = runtests.inventory_project(root)
        self.assertEqual(["app.py::add"], removed_run["removed"])
        coverage = runtests.load_json(runtests.project_paths(root)["coverage"], {})
        self.assertNotIn("app.py::add", coverage["functions"])
        execution = runtests.load_json(runtests.project_paths(root)["execution"], {})
        self.assertNotIn("app.py::add", execution["functions"])
        history_files = list(runtests.project_paths(root)["history_dir"].glob("removed-functions-*.jsonl"))
        self.assertEqual(1, len(history_files))
        history = [json.loads(line) for line in history_files[0].read_text(encoding="utf-8").splitlines()]
        self.assertEqual("app.py::add", history[0]["id"])
        self.assertEqual(["tests/test_app.py::AppTests.test_add"], history[0]["coverage"]["tests"])
        self.assertEqual(["tests/test_app.py::AppTests.test_add"], history[0]["execution"]["tests"])

        (root / "app.py").write_text(source, encoding="utf-8")
        reappeared_run = runtests.inventory_project(root)
        self.assertEqual(["app.py::add"], reappeared_run["added"])
        coverage = runtests.load_json(runtests.project_paths(root)["coverage"], {})
        entry = coverage["functions"]["app.py::add"]
        self.assertEqual("missing", entry["status"])
        self.assertEqual([], entry["tests"])
        self.assertIn("Add regression coverage", entry["notes"])
        execution = runtests.load_json(runtests.project_paths(root)["execution"], {})
        execution_entry = execution["functions"]["app.py::add"]
        self.assertEqual("unexecuted", execution_entry["status"])
        self.assertEqual(0, execution_entry["hits"])
        self.assertEqual([], execution_entry["tests"])

    def test_compact_cli_refreshes_current_state(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        runtests.bootstrap(root, track_tests=True)
        runtests.project_paths(root)["history_dir"].rmdir()

        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            result = runtests.main(["compact", "--project", str(root)])

        self.assertEqual(0, result)
        self.assertTrue(runtests.project_paths(root)["history_dir"].is_dir())
        self.assertIn("FUNCTIONS=1", output.getvalue())
        self.assertIn("COMPACTION=PASS", output.getvalue())

    def test_inventory_refuses_oversized_legacy_state_before_mutation(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        runtests.bootstrap(root, track_tests=True)
        paths = runtests.project_paths(root)
        functions_before = paths["functions"].read_bytes()
        with paths["coverage"].open("wb") as handle:
            handle.truncate(runtests.MAX_ACTIVE_STATE_FILE_BYTES + 1)

        with self.assertRaisesRegex(
            RuntimeError,
            r"too large to load safely.*No active state was changed",
        ):
            runtests.inventory_project(root)

        self.assertEqual(functions_before, paths["functions"].read_bytes())

    def test_manual_function_records_include_valid_execution_line_range(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        (root / "legacy.cpp").write_text("// manually inventoried\n", encoding="utf-8")
        paths = runtests.project_paths(root)
        runtests.write_json(paths["manifest"], {
            "version": 1,
            "project_name": "manual-fixture",
            "commands": [],
            "inventory": {
                "exclude": ["legacy.cpp"],
                "manual_functions": [{
                    "id": "legacy.cpp::generated_entry",
                    "path": "legacy.cpp",
                    "name": "generated_entry",
                    "line": 4,
                    "end_line": 9,
                }],
            },
        })

        runtests.bootstrap(root, track_tests=True)

        function = runtests.load_json(paths["functions"], {})["functions"][0]
        self.assertEqual(4, function["line"])
        self.assertEqual(9, function["end_line"])

    def test_check_cli_fails_with_exit_one_on_stale_hash(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        runtests.bootstrap(root, track_tests=True)
        (root / "tests" / "test_app.py").write_text("# regression test fixture\n", encoding="utf-8")
        self.mark_all_tested(root)

        (root / "app.py").write_text("def add(left, right):\n    return left - right\n", encoding="utf-8")
        buffer = io.StringIO()
        with contextlib.redirect_stdout(buffer):
            exit_code = runtests.main(["check", "--project", str(root)])

        self.assertEqual(1, exit_code)
        self.assertIn("FUNCTION_CHECK=FAIL", buffer.getvalue())
        self.assertIn("STALE app.py::add", buffer.getvalue())

    def test_check_counts_mapping_gaps_by_function_not_source_file(self) -> None:
        temporary, root = self.make_project(
            "def add(left, right):\n    return left + right\n\n"
            "def subtract(left, right):\n    return left - right\n"
        )
        self.addCleanup(temporary.cleanup)
        runtests.bootstrap(root, track_tests=True)
        test_file = root / "tests" / "test_app.py"
        test_file.write_text(
            "import unittest\n\nclass AppTests(unittest.TestCase):\n"
            "    def test_add(self):\n        self.assertEqual(3, 1 + 2)\n",
            encoding="utf-8",
        )
        paths = runtests.project_paths(root)
        functions = runtests.load_json(paths["functions"], {})["functions"]
        add = next(item for item in functions if item["id"] == "app.py::add")
        coverage = runtests.load_json(paths["coverage"], {})
        coverage["functions"][add["id"]] = {
            "source_sha256": add["source_sha256"],
            "status": "tested",
            "tests": ["tests/test_app.py::AppTests.test_add"],
            "notes": "fixture",
        }
        runtests.write_json(paths["coverage"], coverage)

        buffer = io.StringIO()
        with contextlib.redirect_stdout(buffer):
            self.assertEqual(1, runtests.command_check(root))

        output = buffer.getvalue()
        self.assertIn("FUNCTIONS=2", output)
        self.assertIn("FUNCTION_MAPPINGS_CURRENT=1", output)
        self.assertIn("MAPPING_GAPS=1", output)

    def test_large_function_audit_preserves_counts_with_bounded_output(self) -> None:
        source = "\n".join(
            f"def function_{index}():\n    return {index}\n"
            for index in range(500)
        )
        temporary, root = self.make_project(source)
        self.addCleanup(temporary.cleanup)
        runtests.bootstrap(root, track_tests=True)

        buffer = io.StringIO()
        with contextlib.redirect_stdout(buffer):
            self.assertEqual(1, runtests.command_check(root))

        output = buffer.getvalue()
        self.assertIn("FUNCTIONS=500", output)
        self.assertIn("MAPPING_GAPS=500", output)
        self.assertIn("EXECUTION_GAPS=500", output)
        self.assertRegex(output, r"MAPPING_ISSUES_OMITTED=[1-9]\d*")
        self.assertRegex(output, r"EXECUTION_ISSUES_OMITTED=[1-9]\d*")
        self.assertLess(len(output.splitlines()), 300)
        self.assertLess(len(output.encode("utf-8")), 40_000)

    def test_run_writes_coverage_gate_evidence_for_untested_functions(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        runtests.bootstrap(root, track_tests=True)

        buffer = io.StringIO()
        with contextlib.redirect_stdout(buffer):
            exit_code = runtests.command_run(root)

        self.assertEqual(1, exit_code)
        self.assertIn("FUNCTION_EVIDENCE=tests/errors/", buffer.getvalue())
        evidence = list(runtests.project_paths(root)["errors"].glob("*function-verification-gate*.md"))
        self.assertEqual(1, len(evidence))
        self.assertIn("UNTESTED app.py::add", evidence[0].read_text(encoding="utf-8"))

    def test_manifest_timeout_records_failure_evidence(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        runtests.bootstrap(root, track_tests=True)
        manifest_path = runtests.project_paths(root)["manifest"]
        manifest = runtests.load_json(manifest_path, {})
        slow = runtests.command_record(
            "slow command",
            ["python", "-c", "import time; print('BEFORE_TIMEOUT', flush=True); time.sleep(5)"],
        )
        slow["timeout_seconds"] = 1
        manifest["commands"] = [slow]
        runtests.write_json(manifest_path, manifest)

        buffer = io.StringIO()
        with contextlib.redirect_stdout(buffer):
            failures = runtests.run_manifest_commands(root)

        self.assertEqual(1, len(failures))
        self.assertIn("FAILED slow command", failures[0])
        self.assertIn("timed out", failures[0])
        self.assertIn("BEFORE_TIMEOUT", buffer.getvalue())
        evidence = list(runtests.project_paths(root)["errors"].glob("*slow-command*.md"))
        self.assertEqual(1, len(evidence))
        evidence_text = evidence[0].read_text(encoding="utf-8")
        self.assertIn("timed out", evidence_text)
        self.assertIn("BEFORE_TIMEOUT", evidence_text)

    def test_manifest_command_output_is_spooled_and_bounded_head_to_tail(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        runtests.bootstrap(root, track_tests=True)
        manifest_path = runtests.project_paths(root)["manifest"]
        manifest = runtests.load_json(manifest_path, {})
        manifest["commands"] = [runtests.command_record(
            "large failure",
            [
                "python",
                "-c",
                "import sys; print('HEAD_SENTINEL'); "
                "sys.stdout.write('X' * 250000); "
                "print('\\nTAIL_SENTINEL'); raise SystemExit(7)",
            ],
        )]
        runtests.write_json(manifest_path, manifest)

        buffer = io.StringIO()
        with contextlib.redirect_stdout(buffer):
            self.assertEqual(1, runtests.command_run(root))

        output = buffer.getvalue()
        self.assertIn("HEAD_SENTINEL", output)
        self.assertIn("TAIL_SENTINEL", output)
        self.assertIn("output bytes omitted", output)
        self.assertLess(len(output.encode("utf-8")), 80_000)

        evidence = next(runtests.project_paths(root)["errors"].glob("*large-failure*.md"))
        evidence_text = evidence.read_text(encoding="utf-8")
        self.assertIn("Command:", evidence_text)
        self.assertIn("Exit code: 7", evidence_text)
        self.assertIn("HEAD_SENTINEL", evidence_text)
        self.assertIn("TAIL_SENTINEL", evidence_text)
        self.assertIn("output bytes omitted", evidence_text)
        self.assertLess(evidence.stat().st_size, 40_000)

        report = next((root / "docs").glob("runtests_*.md"))
        report_text = report.read_text(encoding="utf-8")
        self.assertIn("HEAD_SENTINEL", report_text)
        self.assertIn("TAIL_SENTINEL", report_text)
        self.assertIn("output bytes omitted", report_text)
        self.assertLess(report.stat().st_size, 60_000)

    def test_optional_execution_gaps_remain_visible_without_failing_run(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        runtests.bootstrap(root, track_tests=True)
        self.mark_all_tested(root)
        paths = runtests.project_paths(root)
        runtests.write_json(paths["execution"], {"version": 1, "functions": {}})
        manifest = runtests.load_json(paths["manifest"], {})
        manifest["function_execution"]["required"] = False
        manifest["commands"] = [runtests.command_record(
            "passing command", ["python", "-c", "raise SystemExit(0)"]
        )]
        runtests.write_json(paths["manifest"], manifest)

        buffer = io.StringIO()
        with contextlib.redirect_stdout(buffer):
            self.assertEqual(0, runtests.command_run(root))

        output = buffer.getvalue()
        self.assertIn("EXECUTION_GATE=OPTIONAL", output)
        self.assertIn("EXECUTION_GAPS=1", output)
        self.assertIn("FUNCTION_CHECK=PASS", output)
        self.assertIn("RUNTESTS=PASS", output)
        self.assertEqual([], list(paths["errors"].glob("*.md")))
        report = next((root / "docs").glob("runtests_*.md"))
        report_text = report.read_text(encoding="utf-8")
        self.assertIn("optional", report_text.lower())
        self.assertIn("0/1 functions have measured execution", report_text)
        self.assertIn("not written; optional evidence gaps are not failure records", report_text)

    def test_optional_execution_section_does_not_link_mapping_only_evidence(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        runtests.bootstrap(root, track_tests=True)
        paths = runtests.project_paths(root)
        manifest = runtests.load_json(paths["manifest"], {})
        manifest["function_execution"]["required"] = False
        manifest["commands"] = [runtests.command_record(
            "passing command", ["python", "-c", "raise SystemExit(0)"]
        )]
        runtests.write_json(paths["manifest"], manifest)

        buffer = io.StringIO()
        with contextlib.redirect_stdout(buffer):
            self.assertEqual(1, runtests.command_run(root))

        evidence = next(paths["errors"].glob("*function-verification-gate*.md"))
        evidence_path = evidence.relative_to(root).as_posix()
        evidence_text = evidence.read_text(encoding="utf-8")
        self.assertIn("UNTESTED app.py::add", evidence_text)
        self.assertNotIn("UNEXECUTED app.py::add", evidence_text)
        report = next((root / "docs").glob("runtests_*.md"))
        report_text = report.read_text(encoding="utf-8")
        mapping_section = report_text.split("### Function traceability", 1)[1].split(
            "### Measured function execution", 1
        )[0]
        execution_section = report_text.split("### Measured function execution", 1)[1].split(
            "## Passing test commands", 1
        )[0]
        self.assertIn(evidence_path, mapping_section)
        self.assertNotIn(evidence_path, execution_section)
        self.assertIn("not written; optional evidence gaps are not failure records", execution_section)

    def test_run_reports_invalid_timeout_without_crashing(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        runtests.bootstrap(root, track_tests=True)
        manifest_path = runtests.project_paths(root)["manifest"]
        manifest = runtests.load_json(manifest_path, {})
        broken = runtests.command_record("broken timeout", ["python", "-c", "pass"])
        broken["timeout_seconds"] = "soon"
        manifest["commands"] = [broken]
        runtests.write_json(manifest_path, manifest)

        failures = runtests.run_manifest_commands(root)

        self.assertEqual(1, len(failures))
        self.assertIn("INVALID_COMMAND broken timeout", failures[0])
        self.assertIn("timeout_seconds", failures[0])

    def test_manifest_surfaces_malformed_entries_and_nonboolean_enabled(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        runtests.bootstrap(root, track_tests=True)
        manifest_path = runtests.project_paths(root)["manifest"]
        manifest = runtests.load_json(manifest_path, {})
        manifest["commands"] = [
            "not-an-object",
            {
                "name": "bad enabled",
                "argv": ["python", "-c", "raise SystemExit(0)"],
                "enabled": "yes",
            },
        ]
        runtests.write_json(manifest_path, manifest)
        results: list[dict[str, object]] = []

        failures = runtests.run_manifest_commands(root, results)

        self.assertEqual(2, len(failures))
        self.assertIn("each command must be an object", failures[0])
        self.assertIn("enabled must be true or false", failures[1])
        self.assertEqual(2, len(results))
        self.assertTrue(all(result["status"] == "failed" for result in results))

    def test_daily_report_bounds_large_command_result_cardinality(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        inventory = runtests.bootstrap(root, track_tests=True)
        command_results = [
            {
                "name": f"command-{index}",
                "argv": ["tool", str(index)],
                "status": "failed" if index % 2 else "passed",
                "exit_code": 1 if index % 2 else 0,
                "details": "diagnostic " * 1000,
                "evidence": None,
            }
            for index in range(1000)
        ]

        report, summary = runtests.write_daily_report(
            root,
            inventory,
            runtests.BoundedIssues(),
            runtests.BoundedIssues(),
            command_results,
            None,
        )

        content = report.read_text(encoding="utf-8")
        self.assertEqual(500, summary["commands_passed"])
        self.assertEqual(500, summary["commands_failed"])
        self.assertIn("FAILED_COMMANDS_OMITTED=475", content)
        self.assertIn("PASSED_COMMANDS_OMITTED=475", content)
        self.assertLess(report.stat().st_size, 150_000)

    def test_run_survives_undecodable_command_output(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        runtests.bootstrap(root, track_tests=True)
        manifest_path = runtests.project_paths(root)["manifest"]
        manifest = runtests.load_json(manifest_path, {})
        manifest["commands"] = [runtests.command_record(
            "binary output",
            ["python", "-c", "import sys; sys.stdout.buffer.write(b'\\xff\\xfe raw bytes'); sys.exit(5)"],
        )]
        runtests.write_json(manifest_path, manifest)

        buffer = io.StringIO()
        with contextlib.redirect_stdout(buffer):
            failures = runtests.run_manifest_commands(root)

        self.assertEqual(1, len(failures))
        self.assertIn("FAILED binary output: exit 5", failures[0])
        self.assertEqual(1, len(list(runtests.project_paths(root)["errors"].glob("*binary-output*.md"))))

    def test_python_duplicate_definitions_get_stable_ids_instead_of_crashing(self) -> None:
        source = (
            "class Example:\n"
            "    @property\n"
            "    def value(self):\n"
            "        return self._value\n"
            "    @value.setter\n"
            "    def value(self, new_value):\n"
            "        self._value = new_value\n"
        )
        temporary, root = self.make_project(source)
        self.addCleanup(temporary.cleanup)

        result = runtests.bootstrap(root, track_tests=True)

        self.assertEqual(2, result["function_count"])
        functions = runtests.load_json(runtests.project_paths(root)["functions"], {})["functions"]
        self.assertEqual(
            {"app.py::Example.value", "app.py::Example.value#2"},
            {item["id"] for item in functions},
        )

    def test_resolve_argv_maps_python_launcher_to_interpreter(self) -> None:
        resolved = runtests.resolve_argv(["python", "-V"])
        self.assertEqual(sys.executable, resolved[0])
        self.assertEqual(["-V"], resolved[1:])

    @unittest.skipUnless(os.name == "nt", "Windows PATHEXT shim resolution")
    def test_resolve_argv_resolves_windows_cmd_shims(self) -> None:
        located = str(Path("tools") / "apache-maven" / "bin" / "mvn.cmd")
        with mock.patch.object(runtests.shutil, "which", return_value=located):
            resolved = runtests.resolve_argv(["mvn", "test"])
        self.assertEqual(located, resolved[0])

        with mock.patch.object(runtests.shutil, "which", return_value=None):
            resolved = runtests.resolve_argv(["npm", "test"])
        self.assertEqual("npm.cmd", resolved[0])

    def test_write_json_preserves_existing_file_when_replace_fails(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        target = Path(temporary.name) / "coverage.json"
        runtests.write_json(target, {"version": 1, "functions": {"keep": True}})
        original = target.read_text(encoding="utf-8")

        with mock.patch.object(runtests.os, "replace", side_effect=OSError("simulated crash")):
            with self.assertRaises(OSError):
                runtests.write_json(target, {"version": 1, "functions": {}})

        self.assertEqual(original, target.read_text(encoding="utf-8"))
        self.assertEqual([], list(Path(temporary.name).glob("*.tmp")))

    def test_load_json_reports_clear_error_for_corrupt_file(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        corrupt = Path(temporary.name) / "corrupt.json"
        corrupt.write_text("{not json", encoding="utf-8")
        with self.assertRaisesRegex(RuntimeError, "Cannot read valid JSON"):
            runtests.load_json(corrupt, {})

    def test_inventory_rejects_corrupt_coverage_without_touching_functions(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        runtests.bootstrap(root, track_tests=True)
        paths = runtests.project_paths(root)
        original_functions = paths["functions"].read_text(encoding="utf-8")
        paths["coverage"].write_text("{corrupt", encoding="utf-8")
        (root / "app.py").write_text("def add(left, right):\n    return left * right\n", encoding="utf-8")

        with self.assertRaisesRegex(RuntimeError, "Cannot read valid JSON"):
            runtests.inventory_project(root)

        self.assertEqual(original_functions, paths["functions"].read_text(encoding="utf-8"))

    def test_gitignore_recognizes_equivalent_existing_entries(self) -> None:
        for existing in ("/tests/\n", "tests/\n", "/tests\n", "tests\n"):
            with self.subTest(existing=existing):
                temporary = tempfile.TemporaryDirectory()
                self.addCleanup(temporary.cleanup)
                root = Path(temporary.name)
                (root / ".gitignore").write_text(existing, encoding="utf-8")
                self.assertEqual("already-ignored", runtests.ensure_tests_gitignore(root))
                self.assertEqual(existing, (root / ".gitignore").read_text(encoding="utf-8"))

    def test_gitignore_commented_entry_is_not_treated_as_ignored(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        (root / ".gitignore").write_text("# /tests/\n", encoding="utf-8")
        self.assertEqual("added", runtests.ensure_tests_gitignore(root))
        content = (root / ".gitignore").read_text(encoding="utf-8")
        self.assertIn("# /tests/", content)
        self.assertIn("\n/tests/\n", content)

    def test_bootstrap_survives_corrupt_package_json(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        (root / "package.json").write_text("{invalid json", encoding="utf-8")

        result = runtests.bootstrap(root, track_tests=True)

        self.assertEqual(1, result["function_count"])
        manifest = runtests.load_json(runtests.project_paths(root)["manifest"], {})
        self.assertNotIn("node-test", [command["name"] for command in manifest["commands"]])

    def test_has_tracked_tests_returns_false_when_git_is_unavailable(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        with mock.patch.object(runtests.subprocess, "run", side_effect=FileNotFoundError("git missing")):
            self.assertFalse(runtests.has_tracked_tests(Path(temporary.name)))

    def test_python_detection_honors_manifest_inventory_exclusions(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        (root / "legacy").mkdir()
        (root / "legacy" / "util.py").write_text("def helper():\n    return 1\n", encoding="utf-8")
        manifest_path = runtests.project_paths(root)["manifest"]
        runtests.write_json(manifest_path, {
            "version": 1,
            "project_name": "sample",
            "commands": [],
            "inventory": {"exclude": ["legacy/**"], "manual_functions": []},
        })

        self.assertIsNone(runtests.detect_python_command(root))

        manifest_path.unlink()
        detected = runtests.detect_python_command(root)
        self.assertIsNotNone(detected)
        self.assertIn(detected["name"], {"python-pytest", "python-unittest"})

    def test_command_detection_prunes_backup_vendor_and_manifest_exclusions(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        for relative in (
            "backups/Backup.Tests.csproj",
            "vendor/Vendor.Tests.csproj",
            "legacy/Legacy.Tests.csproj",
        ):
            path = root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("<Project />\n", encoding="utf-8")
        runtests.write_json(runtests.project_paths(root)["manifest"], {
            "version": 1,
            "project_name": "sample",
            "commands": [],
            "inventory": {"exclude": ["legacy/**"], "manual_functions": []},
            "function_execution": {"required": True},
        })

        commands = runtests.detect_commands(root)

        self.assertNotIn("dotnet-test", {command["name"] for command in commands})

    def test_error_record_timestamps_are_utc(self) -> None:
        import datetime as dt

        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)

        before = dt.datetime.now(dt.timezone.utc).replace(tzinfo=None)
        evidence = runtests.record_failure(root, "utc stamp", "details")
        after = dt.datetime.now(dt.timezone.utc).replace(tzinfo=None)

        stamp = "-".join(evidence.name.split("-")[:5])
        parsed = dt.datetime.strptime(stamp, "%Y-%m-%d-%H%M%S-%f")
        self.assertGreaterEqual(parsed, before - dt.timedelta(seconds=2))
        self.assertLessEqual(parsed, after + dt.timedelta(seconds=2))

    def test_run_evidence_reflects_pre_run_audit_even_if_commands_mutate_coverage(self) -> None:
        temporary, root = self.make_project()
        self.addCleanup(temporary.cleanup)
        runtests.bootstrap(root, track_tests=True)
        (root / "tests" / "test_app.py").write_text("# regression test fixture\n", encoding="utf-8")
        mutator = root / "tests" / "mutate_coverage.py"
        mutator.write_text(
            "import json\n"
            "from pathlib import Path\n"
            "functions_dir = Path('tests') / 'current functions'\n"
            "functions = json.loads((functions_dir / 'functions.json').read_text(encoding='utf-8'))['functions']\n"
            "coverage = json.loads((functions_dir / 'coverage.json').read_text(encoding='utf-8'))\n"
            "for item in functions:\n"
            "    coverage['functions'][item['id']] = {\n"
            "        'source_sha256': item['source_sha256'],\n"
            "        'status': 'tested',\n"
            "        'tests': ['tests/test_app.py'],\n"
            "        'notes': 'mutated during run',\n"
            "    }\n"
            "(functions_dir / 'coverage.json').write_text(json.dumps(coverage), encoding='utf-8')\n",
            encoding="utf-8",
        )
        manifest_path = runtests.project_paths(root)["manifest"]
        manifest = runtests.load_json(manifest_path, {})
        manifest["commands"] = [runtests.command_record("coverage mutator", ["python", "tests/mutate_coverage.py"])]
        runtests.write_json(manifest_path, manifest)

        buffer = io.StringIO()
        with contextlib.redirect_stdout(buffer):
            exit_code = runtests.command_run(root)

        self.assertEqual(1, exit_code)
        evidence = list(runtests.project_paths(root)["errors"].glob("*function-verification-gate*.md"))
        self.assertEqual(1, len(evidence))
        self.assertIn("UNTESTED app.py::add", evidence[0].read_text(encoding="utf-8"))

    def test_gradle_detection_requires_an_existing_wrapper(self) -> None:
        def gradle_argv(root: Path) -> list[str]:
            command = next(item for item in runtests.detect_commands(root) if item["name"] == "java-gradle-test")
            return command["argv"]

        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        (root / "build.gradle").write_text("// gradle build\n", encoding="utf-8")

        with mock.patch.object(runtests.os, "name", "nt"):
            self.assertEqual(["gradle", "test"], gradle_argv(root))
            (root / "gradlew.bat").write_text("@echo off\r\n", encoding="utf-8")
            self.assertEqual(["gradlew.bat", "test"], gradle_argv(root))

        with mock.patch.object(runtests.os, "name", "posix"):
            self.assertEqual(["gradle", "test"], gradle_argv(root))
            (root / "gradlew").write_text("#!/bin/sh\n", encoding="utf-8")
            self.assertEqual(["./gradlew", "test"], gradle_argv(root))

    def test_full_pipeline_works_in_project_path_with_spaces(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name) / "my spaced project"
        root.mkdir()
        (root / "app.py").write_text("def add(left, right):\n    return left + right\n", encoding="utf-8")

        runtests.bootstrap(root, track_tests=True)
        (root / "tests" / "test_app.py").write_text(
            "import unittest\n\nclass AppTests(unittest.TestCase):\n    def test_add(self):\n        self.assertEqual(2, 1 + 1)\n",
            encoding="utf-8",
        )
        self.mark_all_tested(root)

        buffer = io.StringIO()
        with contextlib.redirect_stdout(buffer):
            self.assertEqual(0, runtests.command_run(root))
        self.assertIn("RUNTESTS=PASS", buffer.getvalue())


if __name__ == "__main__":
    unittest.main()
