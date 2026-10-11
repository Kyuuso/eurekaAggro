#!/usr/bin/env python3
"""Scaffold, inventory, audit, and run project-local regression tests."""

from __future__ import annotations

import argparse
import ast
import bisect
import datetime as dt
import fnmatch
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
from typing import Any, Iterable, Sequence
import xml.etree.ElementTree as ET


SCHEMA_VERSION = 1
TESTS_DIR_NAME = "tests"
FUNCTIONS_DIR_NAME = "current functions"
FUNCTION_HISTORY_DIR_NAME = "function history"
DEFAULT_TIMEOUT_SECONDS = 300
MAX_INVENTORY_DELTA_ITEMS = 50
MAX_AUDIT_ISSUE_SAMPLES = 40
MAX_COMMAND_OUTPUT_BYTES = 24_000
MAX_FAILURE_DETAILS_CHARS = 20_000
MAX_REPORT_EXCERPT_CHARS = 6_000
MAX_MANIFEST_COMMANDS = 100
MAX_REPORT_COMMAND_SAMPLES = 25
MAX_ACTIVE_STATE_FILE_BYTES = 128 * 1024 * 1024
SUPPORTED_EXTENSIONS = {
    ".c": "c",
    ".cc": "cpp",
    ".cpp": "cpp",
    ".cxx": "cpp",
    ".cs": "csharp",
    ".go": "go",
    ".h": "cpp",
    ".hh": "cpp",
    ".hpp": "cpp",
    ".java": "java",
    ".js": "javascript",
    ".jsx": "javascript",
    ".mjs": "javascript",
    ".ps1": "powershell",
    ".psm1": "powershell",
    ".py": "python",
    ".rb": "ruby",
    ".rs": "rust",
    ".ts": "typescript",
    ".tsx": "typescript",
}
DEFAULT_EXCLUDED_PARTS = {
    ".git",
    ".hg",
    ".idea",
    ".svn",
    ".venv",
    ".vs",
    ".vscode",
    ".rollback",
    "__pycache__",
    "backups",
    "bin",
    "build",
    "coverage",
    "dist",
    "generated",
    "node_modules",
    "obj",
    "packages",
    "target",
    TESTS_DIR_NAME,
    "third_party",
    "vendor",
    "venv",
}
CONTROL_WORDS = {"catch", "do", "else", "for", "foreach", "if", "lock", "switch", "using", "while"}


class BoundedIssues(list[str]):
    """Retain representative diagnostics while counting every observed issue."""

    def __init__(self, limit: int = MAX_AUDIT_ISSUE_SAMPLES) -> None:
        super().__init__()
        self.limit = limit
        self.total_count = 0
        self.gap_count = 0

    def add(self, issue: str) -> None:
        self.total_count += 1
        if len(self) < self.limit:
            super().append(issue)

    def mark_gap(self) -> None:
        self.gap_count += 1

    @property
    def omitted_count(self) -> int:
        return max(0, self.total_count - len(self))


def utc_now() -> str:
    return dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def load_json(path: Path, default: Any) -> Any:
    if not path.exists():
        return default
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise RuntimeError(f"Cannot read valid JSON from {path}: {exc}") from exc


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = json.dumps(value, indent=2, ensure_ascii=False) + "\n"
    temp_path = path.with_name(f"{path.name}.{os.getpid()}.tmp")
    try:
        temp_path.write_text(payload, encoding="utf-8")
        os.replace(temp_path, path)
    finally:
        if temp_path.exists():
            try:
                temp_path.unlink()
            except OSError:
                pass


def write_text_atomic(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp_path = path.with_name(f"{path.name}.{os.getpid()}.tmp")
    try:
        temp_path.write_text(content, encoding="utf-8")
        os.replace(temp_path, path)
    finally:
        if temp_path.exists():
            try:
                temp_path.unlink()
            except OSError:
                pass


def print_output(text: str) -> None:
    """Print captured command output without crashing on console encodings."""
    try:
        print(text)
    except UnicodeEncodeError:
        encoding = getattr(sys.stdout, "encoding", None) or "utf-8"
        print(text.encode(encoding, errors="replace").decode(encoding, errors="replace"))


def write_if_missing(path: Path, content: str) -> bool:
    if path.exists():
        return False
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")
    return True


def project_paths(root: Path) -> dict[str, Path]:
    tests = root / TESTS_DIR_NAME
    functions = tests / FUNCTIONS_DIR_NAME
    history = tests / FUNCTION_HISTORY_DIR_NAME
    return {
        "docs": root / "docs",
        "tests": tests,
        "errors": tests / "errors",
        "functions_dir": functions,
        "history_dir": history,
        "functions": functions / "functions.json",
        "coverage": functions / "coverage.json",
        "execution": functions / "execution.json",
        "changes": functions / "changes.md",
        "readme": tests / "README.md",
        "manifest": tests / "test-manifest.json",
    }


def ensure_active_state_size_safe(paths: dict[str, Path]) -> None:
    """Refuse giant legacy JSON before parsing or mutating active state."""
    for key in ("functions", "coverage", "execution"):
        path = paths[key]
        if not path.exists():
            continue
        size = path.stat().st_size
        if size > MAX_ACTIVE_STATE_FILE_BYTES:
            limit_mib = MAX_ACTIVE_STATE_FILE_BYTES // (1024 * 1024)
            raise RuntimeError(
                f"Active test state is too large to load safely: {path} is {size} bytes; "
                f"limit is {limit_mib} MiB. Back up the tests workspace, move or stream-archive "
                "legacy entries outside the active JSON, then rerun compact. No active state was changed."
            )


def resolve_project(value: str) -> Path:
    root = Path(value).expanduser().resolve()
    if not root.is_dir():
        raise RuntimeError(f"Project directory does not exist: {root}")
    return root


def is_relative_to(path: Path, parent: Path) -> bool:
    try:
        path.relative_to(parent)
        return True
    except ValueError:
        return False


def has_tracked_tests(root: Path) -> bool:
    try:
        worktree = subprocess.run(
            ["git", "-C", str(root), "rev-parse", "--is-inside-work-tree"],
            check=False,
            capture_output=True,
            encoding="utf-8",
            errors="replace",
            timeout=30,
        )
        if worktree.returncode != 0 or worktree.stdout.strip().lower() != "true":
            return False
        completed = subprocess.run(
            ["git", "-C", str(root), "ls-files", "--error-unmatch", "--", TESTS_DIR_NAME],
            check=False,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            timeout=30,
        )
    except (OSError, subprocess.SubprocessError):
        return False
    return completed.returncode == 0


def ensure_tests_gitignore(root: Path) -> str:
    gitignore = root / ".gitignore"
    entry = f"/{TESTS_DIR_NAME}/"
    existing = gitignore.read_text(encoding="utf-8-sig") if gitignore.exists() else ""
    active_lines = {
        line.strip().replace("\\", "/")
        for line in existing.splitlines()
        if line.strip() and not line.lstrip().startswith("#")
    }
    equivalent_entries = {entry, f"{TESTS_DIR_NAME}/", f"/{TESTS_DIR_NAME}", TESTS_DIR_NAME}
    if active_lines & equivalent_entries:
        return "already-ignored"
    separator = "" if not existing or existing.endswith(("\n", "\r")) else "\n"
    addition = f"{separator}\n# Local project function-check workspace\n{entry}\n"
    gitignore.write_text(existing + addition, encoding="utf-8")
    return "added"


def default_readme(project_name: str) -> str:
    return f"""# {project_name} tests

This local test workspace inventories application functions, maps them to regression tests, records reproduced errors, and runs the native project test commands.

## Folders

- `errors/`: bug records and automated command-failure evidence.
- `current functions/functions.json`: generated current source inventory.
- `current functions/coverage.json`: maintained current function-to-test traceability.
- `current functions/execution.json`: current measured function-to-test execution evidence.
- `current functions/changes.md`: generated deltas plus human before/after behavior notes.
- `function history/removed-functions-YYYY.jsonl`: meaningful removed tested/executed evidence.
- `test-manifest.json`: native test commands and inventory configuration.
- `../docs/runtests_MM_DD_YY-HH-MM-SS.md`: timestamped counts, failure evidence, gameplan, and retest steps for one run.

## Function check

Invoke `$runtests` from this project directory, or run the installed skill's `scripts/runtests.py run --project .` command.
Use `scripts/runtests.py compact --project .` after backing up legacy oversized current-state artifacts.

Every `run` creates a new timestamped report without overwriting earlier runs. A complete result requires a current tested mapping and current positive measured execution for every inventoried function, plus a passing result from every enabled native test command. External integration and runtime gates remain separate and must be recorded here when applicable.
"""


def manifest_exclusions(root: Path) -> list[str]:
    """Inventory exclusions from an existing manifest; empty when unavailable."""
    manifest_path = project_paths(root)["manifest"]
    if not manifest_path.exists():
        return []
    try:
        manifest = load_json(manifest_path, {})
    except RuntimeError:
        return []
    inventory_config = manifest.get("inventory", {}) if isinstance(manifest, dict) else {}
    exclusions = inventory_config.get("exclude", []) if isinstance(inventory_config, dict) else []
    if isinstance(exclusions, list) and all(isinstance(item, str) for item in exclusions):
        return exclusions
    return []


def detect_python_command(root: Path) -> dict[str, Any] | None:
    paths = project_paths(root)
    exclusions = manifest_exclusions(root)
    has_python_tests = (
        any(
            is_relative_to(path, paths["tests"])
            and path.name.startswith("test_")
            and path.suffix.lower() == ".py"
            for path in iter_project_files(root, exclusions, allow_tests=True)
        )
        if paths["tests"].exists()
        else False
    )
    has_python = any(path.suffix.lower() == ".py" for path in iter_source_files(root, exclusions))
    if not has_python and not has_python_tests:
        return None
    markers = [root / "pytest.ini", root / "conftest.py", root / "tox.ini"]
    config_text = ""
    for candidate in [root / "pyproject.toml", root / "requirements.txt", root / "requirements-dev.txt"]:
        if candidate.exists():
            config_text += candidate.read_text(encoding="utf-8-sig", errors="replace").lower()
    if any(path.exists() for path in markers) or "pytest" in config_text:
        argv = ["python", "-B", "-m", "pytest", "tests"]
        name = "python-pytest"
    else:
        argv = ["python", "-B", "-m", "unittest", "discover", "-s", "tests", "-p", "test_*.py", "-v"]
        name = "python-unittest"
    return command_record(name, argv)


def detect_commands(root: Path) -> list[dict[str, Any]]:
    commands: list[dict[str, Any]] = []
    exclusions = manifest_exclusions(root)
    python_command = detect_python_command(root)
    if python_command:
        commands.append(python_command)

    solution = next(iter(sorted(root.glob("*.sln"))), None)
    test_projects = sorted(
        path for path in iter_project_files(root, exclusions, allow_tests=True)
        if path.suffix.lower() == ".csproj"
        if TESTS_DIR_NAME in {part.lower() for part in path.parts}
        or "test" in path.stem.lower()
    )
    if solution or test_projects:
        target = solution or test_projects[0]
        commands.append(command_record("dotnet-test", ["dotnet", "test", str(target.relative_to(root)), "--nologo"]))

    package_json = root / "package.json"
    if package_json.exists():
        try:
            package = load_json(package_json, {})
        except RuntimeError:
            package = {}
        test_script = package.get("scripts", {}).get("test", "") if isinstance(package, dict) else ""
        if test_script and "no test specified" not in test_script.lower():
            commands.append(command_record("node-test", ["npm", "test"]))

    if (root / "Cargo.toml").exists():
        commands.append(command_record("rust-cargo-test", ["cargo", "test"]))
    if (root / "go.mod").exists():
        commands.append(command_record("go-test", ["go", "test", "./..."]))
    if (root / "pom.xml").exists():
        commands.append(command_record("java-maven-test", ["mvn", "test"]))
    if (root / "build.gradle").exists() or (root / "build.gradle.kts").exists():
        if os.name == "nt" and (root / "gradlew.bat").exists():
            gradle = "gradlew.bat"
        elif os.name != "nt" and (root / "gradlew").exists():
            gradle = "./gradlew"
        else:
            # No usable wrapper: fall back to a PATH gradle, which
            # resolve_argv maps to its .cmd/.bat shim on Windows.
            gradle = "gradle"
        commands.append(command_record("java-gradle-test", [gradle, "test"]))
    if any(
        path.name.lower().endswith(".tests.ps1")
        for path in iter_project_files(root, exclusions, allow_tests=True)
        if is_relative_to(path, root / TESTS_DIR_NAME)
    ):
        powershell = "pwsh" if shutil.which("pwsh") else "powershell"
        commands.append(command_record(
            "powershell-pester",
            [powershell, "-NoProfile", "-Command", "Invoke-Pester -Path ./tests -CI"],
        ))
    if (root / "CMakeLists.txt").exists():
        commands.append(command_record("cmake-ctest", ["ctest", "--test-dir", "build", "--output-on-failure"]))
    return commands


def command_record(name: str, argv: Sequence[str]) -> dict[str, Any]:
    return {
        "name": name,
        "argv": list(argv),
        "cwd": ".",
        "timeout_seconds": DEFAULT_TIMEOUT_SECONDS,
        "enabled": True,
    }


def bootstrap(root: Path, track_tests: bool) -> dict[str, Any]:
    paths = project_paths(root)
    for key in ("tests", "errors", "functions_dir", "history_dir"):
        paths[key].mkdir(parents=True, exist_ok=True)

    created: list[str] = []
    if write_if_missing(paths["readme"], default_readme(root.name)):
        created.append(str(paths["readme"].relative_to(root)))
    if write_if_missing(paths["changes"], "# Function changes\n\nGenerated inventory deltas and human-authored behavior changes are recorded here.\n"):
        created.append(str(paths["changes"].relative_to(root)))

    manifest = {
        "version": SCHEMA_VERSION,
        "project_name": root.name,
        "commands": detect_commands(root),
        "inventory": {"exclude": [], "manual_functions": []},
        "function_execution": {
            "required": True,
            "evidence_file": "tests/current functions/execution.json",
        },
    }
    if not paths["manifest"].exists():
        write_json(paths["manifest"], manifest)
        created.append(str(paths["manifest"].relative_to(root)))
    if not paths["coverage"].exists():
        write_json(paths["coverage"], {"version": SCHEMA_VERSION, "functions": {}})
        created.append(str(paths["coverage"].relative_to(root)))
    if not paths["execution"].exists():
        write_json(paths["execution"], {"version": SCHEMA_VERSION, "functions": {}})
        created.append(str(paths["execution"].relative_to(root)))

    git_policy = "tracked"
    if not track_tests:
        if has_tracked_tests(root):
            git_policy = "preserved-existing-tracked-tests"
        else:
            git_policy = ensure_tests_gitignore(root)

    inventory = inventory_project(root)
    return {"created": created, "git_policy": git_policy, **inventory}


def exclusion_pattern_variants(pattern: str) -> tuple[str, ...]:
    normalized = pattern.replace("\\", "/")
    while normalized.startswith("./"):
        normalized = normalized[2:]
    normalized = normalized.lstrip("/")
    if normalized.startswith("**/"):
        return normalized, normalized[3:]
    return (normalized,)


def inventory_path_is_excluded(
    relative: str,
    exclusions: Sequence[str],
    *,
    is_dir: bool = False,
    allow_tests: bool = False,
) -> bool:
    normalized = relative.replace("\\", "/").strip("/")
    if not normalized:
        return False
    excluded_parts = DEFAULT_EXCLUDED_PARTS - ({TESTS_DIR_NAME} if allow_tests else set())
    if {part.lower() for part in normalized.split("/")} & excluded_parts:
        return True

    candidates = [normalized]
    if is_dir:
        candidates.extend([f"{normalized}/", f"{normalized}/__runtests_descendant__"])
    for pattern in exclusions:
        for variant in exclusion_pattern_variants(pattern):
            if any(fnmatch.fnmatch(candidate, variant) for candidate in candidates):
                return True
    return False


def iter_project_files(
    root: Path,
    exclusions: Sequence[str],
    *,
    allow_tests: bool = False,
) -> Iterable[Path]:
    """Yield files while pruning excluded directories before descent."""
    for directory, directory_names, file_names in os.walk(root, topdown=True):
        base = root.__class__(directory)
        directory_names[:] = sorted(
            name
            for name in directory_names
            if not inventory_path_is_excluded(
                (base / name).relative_to(root).as_posix(),
                exclusions,
                is_dir=True,
                allow_tests=allow_tests,
            )
        )
        for name in sorted(file_names):
            path = base / name
            relative = path.relative_to(root).as_posix()
            if not inventory_path_is_excluded(
                relative,
                exclusions,
                allow_tests=allow_tests,
            ):
                yield path


def iter_source_files(root: Path, exclusions: Sequence[str]) -> Iterable[Path]:
    for path in iter_project_files(root, exclusions):
        if path.suffix.lower() not in SUPPORTED_EXTENSIONS:
            continue
        lowered_name = path.name.lower()
        if lowered_name.endswith((".designer.cs", ".g.cs", ".generated.cs", ".min.js")):
            continue
        yield path


def function_hash(lines: Sequence[str]) -> str:
    normalized = "\n".join(line.rstrip() for line in lines).strip() + "\n"
    return hashlib.sha256(normalized.encode("utf-8")).hexdigest()


def python_functions(path: Path, root: Path, text: str) -> list[dict[str, Any]]:
    try:
        tree = ast.parse(text, filename=str(path))
    except SyntaxError as exc:
        relative = path.relative_to(root).as_posix()
        location = f"line {exc.lineno or 'unknown'}"
        if exc.offset:
            location += f", column {exc.offset}"
        raise RuntimeError(
            f"Cannot inventory Python source {relative}: syntax error at {location}: {exc.msg}"
        ) from exc
    lines = text.splitlines()
    records: list[dict[str, Any]] = []

    class Visitor(ast.NodeVisitor):
        def __init__(self) -> None:
            self.scope: list[str] = []
            self.counts: dict[str, int] = {}

        def visit_ClassDef(self, node: ast.ClassDef) -> None:
            self.scope.append(node.name)
            self.generic_visit(node)
            self.scope.pop()

        def visit_FunctionDef(self, node: ast.FunctionDef) -> None:
            self._record(node)

        def visit_AsyncFunctionDef(self, node: ast.AsyncFunctionDef) -> None:
            self._record(node)

        def _record(self, node: ast.FunctionDef | ast.AsyncFunctionDef) -> None:
            qualified = ".".join([*self.scope, node.name])
            count = self.counts.get(qualified, 0) + 1
            self.counts[qualified] = count
            stable_name = qualified if count == 1 else f"{qualified}#{count}"
            start = max(1, node.lineno)
            end = max(start, getattr(node, "end_lineno", start))
            relative = path.relative_to(root).as_posix()
            records.append(make_record(relative, "python", stable_name, start, lines[start - 1:end]))
            self.scope.append(node.name)
            self.generic_visit(node)
            self.scope.pop()

    Visitor().visit(tree)
    return records


REGEX_PATTERNS: dict[str, list[re.Pattern[str]]] = {
    "javascript": [
        re.compile(r"(?m)^\s*(?:export\s+)?(?:default\s+)?(?:async\s+)?function\s+(?P<name>[A-Za-z_$][\w$]*)\s*\("),
        re.compile(r"(?m)^\s*(?:export\s+)?(?:const|let|var)\s+(?P<name>[A-Za-z_$][\w$]*)\s*=\s*(?:async\s*)?(?:\([^\n)]*\)|[A-Za-z_$][\w$]*)\s*=>"),
    ],
    "typescript": [
        re.compile(r"(?m)^\s*(?:export\s+)?(?:default\s+)?(?:async\s+)?function\s+(?P<name>[A-Za-z_$][\w$]*)\s*(?:<[^\n>{}]+>)?\s*\("),
        re.compile(r"(?m)^\s*(?:export\s+)?(?:const|let|var)\s+(?P<name>[A-Za-z_$][\w$]*)\s*=\s*(?:async\s*)?(?:\([^\n)]*\)|[A-Za-z_$][\w$]*)\s*=>"),
    ],
    "csharp": [
        re.compile(r"(?m)^\s*(?:(?:public|private|protected|internal|static|virtual|override|abstract|sealed|async|extern|unsafe|partial|new)\s+)*(?:[A-Za-z_][\w<>,.?\[\]:]*\s+)?(?P<name>[A-Za-z_]\w*)\s*\([^;{}]*\)\s*(?:where\s+[^{}]+)?\{"),
    ],
    "java": [
        re.compile(r"(?m)^\s*(?:(?:public|private|protected|static|final|abstract|synchronized|native|default)\s+)*(?:[A-Za-z_][\w<>,.?\[\]]*\s+)?(?P<name>[A-Za-z_]\w*)\s*\([^;{}]*\)\s*(?:throws\s+[^{}]+)?\{"),
    ],
    "c": [
        re.compile(r"(?m)^\s*(?:[A-Za-z_]\w*[\s*]+)+(?P<name>[A-Za-z_]\w*)\s*\([^;{}]*\)\s*\{"),
    ],
    "cpp": [
        re.compile(r"(?m)^\s*(?:template\s*<[^;{}]+>\s*)?(?:[A-Za-z_~][\w:<>,~*&\s]*\s+)(?P<name>(?:[A-Za-z_]\w*::)*~?[A-Za-z_]\w*)\s*\([^;{}]*\)\s*(?:const\s*)?(?:noexcept(?:\([^)]*\))?\s*)?\{"),
    ],
    "go": [
        re.compile(r"(?m)^\s*func\s+(?:\([^)]*\)\s*)?(?P<name>[A-Za-z_]\w*)\s*\("),
    ],
    "rust": [
        re.compile(r"(?m)^\s*(?:(?:pub(?:\([^)]*\))?|async|unsafe|const|extern\s+\"[^\"]+\")\s+)*fn\s+(?P<name>[A-Za-z_]\w*)\s*(?:<[^\n>{}]+>)?\s*\("),
    ],
    "powershell": [
        re.compile(r"(?im)^\s*function\s+(?P<name>[A-Za-z_][\w-]*)\s*(?:\([^)]*\))?\s*\{"),
    ],
    "ruby": [
        re.compile(r"(?m)^\s*def\s+(?P<name>(?:self\.)?[A-Za-z_]\w*[!?=]?)"),
    ],
}


def brace_block(
    text: str,
    start: int,
    known_brace: int | None = None,
    language: str = "",
) -> str:
    brace = known_brace if known_brace is not None else text.find("{", start)
    line_end = text.find("\n", start)
    if brace < 0 or (line_end >= 0 and brace > line_end + 300):
        end = line_end if line_end >= 0 else len(text)
        return text[start:end]
    depth = 0
    quote = ""
    escaped = False
    line_comment = False
    block_comment = False
    index = brace
    while index < len(text):
        char = text[index]
        next_char = text[index + 1] if index + 1 < len(text) else ""
        if line_comment:
            if char == "\n":
                line_comment = False
            index += 1
            continue
        if block_comment:
            if char == "*" and next_char == "/":
                block_comment = False
                index += 2
            else:
                index += 1
            continue
        if quote:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == quote:
                quote = ""
            index += 1
            continue
        if language in {"c", "cpp"} and char == "R" and next_char == '"':
            delimiter_end = text.find("(", index + 2, min(len(text), index + 20))
            if delimiter_end >= 0:
                delimiter = text[index + 2:delimiter_end]
                if not any(token in delimiter for token in (" ", "\\", ")", "\t", "\r", "\n")):
                    terminator = ")" + delimiter + '"'
                    raw_end = text.find(terminator, delimiter_end + 1)
                    if raw_end >= 0:
                        index = raw_end + len(terminator)
                        continue
        if char == "/" and next_char == "/":
            line_comment = True
            index += 2
            continue
        if char == "/" and next_char == "*":
            block_comment = True
            index += 2
            continue
        if char == "#" and language in {"powershell", "ruby"}:
            line_comment = True
            index += 1
            continue
        if char in ("'", '"', "`"):
            quote = char
        elif char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
        index += 1
    return text[start:]


def regex_functions(path: Path, root: Path, text: str, language: str) -> list[dict[str, Any]]:
    matches: list[tuple[int, str, int | None]] = []
    for pattern in REGEX_PATTERNS.get(language, []):
        for match in pattern.finditer(text):
            name = match.group("name")
            if name.lower() in CONTROL_WORDS:
                continue
            raw_match = match.group(0)
            matched_text = raw_match.lstrip()
            if language in {"c", "cpp"} and (
                re.match(r"(?:return|co_return|if\s+constexpr)\b", matched_text)
                or name in {"sizeof", "alignof", "decltype", "constexpr"}
                or name.startswith("std::")
            ):
                continue
            definition_start = match.start() + len(raw_match) - len(matched_text)
            brace_offset = raw_match.rfind("{")
            known_brace = match.start() + brace_offset if brace_offset >= 0 else None
            matches.append((definition_start, name, known_brace))
    records: list[dict[str, Any]] = []
    counts: dict[str, int] = {}
    relative = path.relative_to(root).as_posix()
    newline_positions = [match.start() for match in re.finditer("\n", text)]
    for start, name, known_brace in sorted(set(matches), key=lambda item: (item[0], item[1])):
        counts[name] = counts.get(name, 0) + 1
        stable_name = name if counts[name] == 1 else f"{name}#{counts[name]}"
        line = bisect.bisect_right(newline_positions, start) + 1
        records.append(make_record(
            relative,
            language,
            stable_name,
            line,
            brace_block(text, start, known_brace, language).splitlines(),
        ))
    return records


def make_record(relative: str, language: str, name: str, line: int, body_lines: Sequence[str]) -> dict[str, Any]:
    return {
        "id": f"{relative}::{name}",
        "language": language,
        "path": relative,
        "line": line,
        "end_line": line + max(0, len(body_lines) - 1),
        "name": name,
        "source_sha256": function_hash(body_lines),
    }


def scan_functions(root: Path, manifest: dict[str, Any]) -> list[dict[str, Any]]:
    inventory_config = manifest.get("inventory", {}) if isinstance(manifest, dict) else {}
    exclusions = inventory_config.get("exclude", []) if isinstance(inventory_config, dict) else []
    if not isinstance(exclusions, list) or not all(isinstance(item, str) for item in exclusions):
        raise RuntimeError("manifest inventory.exclude must be an array of glob strings")
    records: list[dict[str, Any]] = []
    for path in iter_source_files(root, exclusions):
        text = path.read_text(encoding="utf-8-sig", errors="replace")
        language = SUPPORTED_EXTENSIONS[path.suffix.lower()]
        if language == "python":
            records.extend(python_functions(path, root, text))
        else:
            records.extend(regex_functions(path, root, text, language))

    manual = inventory_config.get("manual_functions", []) if isinstance(inventory_config, dict) else []
    if not isinstance(manual, list):
        raise RuntimeError("manifest inventory.manual_functions must be an array")
    for item in manual:
        if not isinstance(item, dict) or not all(key in item for key in ("id", "path", "name")):
            raise RuntimeError("each manual function requires id, path, and name")
        source_path = (root / str(item["path"])).resolve()
        if not is_relative_to(source_path, root) or not source_path.is_file():
            raise RuntimeError(f"manual function path is invalid: {item.get('path')}")
        try:
            line_number = int(item.get("line", 1))
            end_line = int(item.get("end_line", line_number))
        except (TypeError, ValueError) as exc:
            raise RuntimeError(f"manual function line and end_line must be integers: {item.get('id')}") from exc
        if line_number < 1 or end_line < line_number:
            raise RuntimeError(f"manual function line range is invalid: {item.get('id')}")
        digest = hashlib.sha256(source_path.read_bytes()).hexdigest()
        records.append({
            "id": str(item["id"]),
            "language": str(item.get("language", "manual")),
            "path": source_path.relative_to(root).as_posix(),
            "line": line_number,
            "end_line": end_line,
            "name": str(item["name"]),
            "source_sha256": digest,
        })

    by_id: dict[str, dict[str, Any]] = {}
    for record in sorted(records, key=lambda item: (item["path"].lower(), item["line"], item["id"])):
        if record["id"] in by_id:
            raise RuntimeError(f"duplicate function id; add a manual unique id: {record['id']}")
        by_id[record["id"]] = record
    return list(by_id.values())


def format_inventory_delta(label: str, values: Sequence[str], *, total_count: int | None = None) -> str:
    total = len(values) if total_count is None else total_count
    if total == 0:
        return f"- {label}: None"
    shown = list(values[:MAX_INVENTORY_DELTA_ITEMS])
    rendered = ", ".join(f"`{value}`" for value in shown)
    if total <= MAX_INVENTORY_DELTA_ITEMS:
        return f"- {label}: {rendered}"
    return (
        f"- {label}: {total} total; showing first {len(shown)}: {rendered}. "
        "See functions.json and yearly removed-function history for structured evidence."
    )


def compact_changes_log(path: Path) -> int:
    if not path.exists():
        return 0
    temp_path = path.with_name(f"{path.name}.{os.getpid()}.tmp")
    compacted = 0
    try:
        with path.open("r", encoding="utf-8-sig", errors="replace") as source, temp_path.open(
            "w", encoding="utf-8"
        ) as destination:
            for line in source:
                match = re.match(r"^(\s*-\s+)(Added|Changed|Removed):\s+", line)
                entry_count = line.count("`") // 2 if match else 0
                if not match or entry_count <= MAX_INVENTORY_DELTA_ITEMS:
                    destination.write(line)
                    continue
                samples: list[str] = []
                for item in re.finditer(r"`([^`]*)`", line):
                    samples.append(item.group(1))
                    if len(samples) == MAX_INVENTORY_DELTA_ITEMS:
                        break
                destination.write(
                    format_inventory_delta(match.group(2), samples, total_count=entry_count) + "\n"
                )
                compacted += 1
        if compacted:
            os.replace(temp_path, path)
        else:
            temp_path.unlink()
    finally:
        if temp_path.exists():
            try:
                temp_path.unlink()
            except OSError:
                pass
    return compacted


def removed_entry_has_meaningful_evidence(coverage_entry: Any, execution_entry: Any) -> bool:
    if isinstance(coverage_entry, dict) and (
        coverage_entry.get("status") == "tested" or coverage_entry.get("tests")
    ):
        return True
    return isinstance(execution_entry, dict) and (
        execution_entry.get("status") == "executed"
        or execution_entry.get("tests")
        or (isinstance(execution_entry.get("hits"), int) and execution_entry.get("hits", 0) > 0)
    )


def archive_removed_function_history(
    root: Path,
    function_ids: Iterable[str],
    coverage: dict[str, Any],
    execution: dict[str, Any],
    exclusions: Sequence[str],
    removed_at: str,
) -> tuple[int, int, list[str]]:
    records: list[dict[str, Any]] = []
    pruned = 0
    for function_id in sorted(function_ids):
        coverage_entry = coverage.get(function_id)
        execution_entry = execution.get(function_id)
        source_path = function_id.split("::", 1)[0]
        if inventory_path_is_excluded(source_path, exclusions) or not removed_entry_has_meaningful_evidence(
            coverage_entry, execution_entry
        ):
            pruned += 1
            continue
        records.append({
            "id": function_id,
            "removed_at": removed_at,
            "coverage": coverage_entry if isinstance(coverage_entry, dict) else None,
            "execution": execution_entry if isinstance(execution_entry, dict) else None,
        })

    if not records:
        return 0, pruned, []
    history_dir = project_paths(root)["history_dir"]
    history_dir.mkdir(parents=True, exist_ok=True)
    history_path = history_dir / f"removed-functions-{removed_at[:4]}.jsonl"
    with history_path.open("a", encoding="utf-8") as handle:
        for record in records:
            handle.write(json.dumps(record, ensure_ascii=False, sort_keys=True) + "\n")
    return len(records), pruned, [history_path.relative_to(root).as_posix()]


def append_inventory_changes(path: Path, added: list[str], changed: list[str], removed: list[str]) -> None:
    if not (added or changed or removed):
        return
    if not path.exists():
        write_if_missing(path, "# Function changes\n")
    compact_changes_log(path)
    lines = ["", f"## {utc_now()} - Generated inventory delta", ""]
    for label, values in (("Added", added), ("Changed", changed), ("Removed", removed)):
        lines.append(format_inventory_delta(label, values))
    lines.extend(["- Intent: Add the human reason for this change.", "- Before: Add the observable old behavior.", "- After: Add the observable new behavior.", "- Tests: Add exact regression test cases before completion.", ""])
    with path.open("a", encoding="utf-8") as handle:
        handle.write("\n".join(lines))


def inventory_project(root: Path) -> dict[str, Any]:
    paths = project_paths(root)
    if not paths["manifest"].exists():
        raise RuntimeError("tests/test-manifest.json is missing; run init first")
    manifest = load_json(paths["manifest"], {})
    if not isinstance(manifest, dict):
        raise RuntimeError(f"test-manifest.json must contain a JSON object: {paths['manifest']}")
    inventory_config = manifest.get("inventory", {})
    exclusions = inventory_config.get("exclude", []) if isinstance(inventory_config, dict) else []
    if not isinstance(exclusions, list) or not all(isinstance(item, str) for item in exclusions):
        raise RuntimeError("manifest inventory.exclude must be an array of glob strings")
    ensure_active_state_size_safe(paths)
    paths["history_dir"].mkdir(parents=True, exist_ok=True)
    previous_payload = load_json(paths["functions"], {"functions": []})
    if not isinstance(previous_payload, dict):
        raise RuntimeError(f"functions.json must contain a JSON object: {paths['functions']}")
    coverage_payload = load_json(paths["coverage"], {"version": SCHEMA_VERSION, "functions": {}})
    if not isinstance(coverage_payload, dict):
        raise RuntimeError(f"coverage.json must contain a JSON object: {paths['coverage']}")
    coverage = coverage_payload.get("functions", {})
    if not isinstance(coverage, dict):
        raise RuntimeError("coverage.json functions must be an object")
    execution_payload = load_json(paths["execution"], {"version": SCHEMA_VERSION, "functions": {}})
    if not isinstance(execution_payload, dict):
        raise RuntimeError(f"execution.json must contain a JSON object: {paths['execution']}")
    execution = execution_payload.get("functions", {})
    if not isinstance(execution, dict):
        raise RuntimeError("execution.json functions must be an object")
    previous_functions = previous_payload.get("functions", [])
    if not isinstance(previous_functions, list):
        raise RuntimeError("functions.json functions must be an array")
    previous = {item["id"]: item for item in previous_functions if isinstance(item, dict) and "id" in item}
    current_records = scan_functions(root, manifest)
    current = {item["id"]: item for item in current_records}

    added = sorted(current.keys() - previous.keys())
    removed = sorted(previous.keys() - current.keys())
    changed = sorted(
        key for key in current.keys() & previous.keys()
        if current[key]["source_sha256"] != previous[key].get("source_sha256")
    )
    write_json(paths["functions"], {
        "version": SCHEMA_VERSION,
        "generated_at": utc_now(),
        "project_name": manifest.get("project_name", root.name),
        "functions": current_records,
    })
    current_ids = set(current)
    obsolete_state_ids = (set(coverage) | set(execution)) - current_ids
    removed_at = utc_now()
    archived_history, pruned_state, history_files = archive_removed_function_history(
        root,
        obsolete_state_ids,
        coverage,
        execution,
        exclusions,
        removed_at,
    )
    current_coverage: dict[str, Any] = {}
    current_execution: dict[str, Any] = {}
    for function_id in current.keys():
        coverage_entry = coverage.get(function_id)
        if not isinstance(coverage_entry, dict):
            current_coverage[function_id] = {
                "source_sha256": current[function_id]["source_sha256"],
                "status": "missing",
                "tests": [],
                "notes": "Add regression coverage and change status to tested after it passes.",
            }
        elif coverage_entry.get("status") == "removed":
            current_coverage[function_id] = {
                "source_sha256": current[function_id]["source_sha256"],
                "status": "missing",
                "tests": [],
                "notes": "Function reappeared; add and rerun regression coverage.",
            }
        else:
            current_coverage[function_id] = coverage_entry
        execution_entry = execution.get(function_id)
        if isinstance(execution_entry, dict) and execution_entry.get("status") == "removed":
            current_execution[function_id] = {
                "source_sha256": current[function_id]["source_sha256"],
                "status": "unexecuted",
                "hits": 0,
                "tests": [],
                "notes": "Function reappeared; measured execution must be collected again.",
            }
        elif isinstance(execution_entry, dict):
            current_execution[function_id] = execution_entry
        else:
            current_execution[function_id] = {
                "source_sha256": current[function_id]["source_sha256"],
                "status": "unexecuted",
                "hits": 0,
                "tests": [],
                "notes": "Measured execution must be collected for this current function.",
            }
    coverage_payload = {"version": SCHEMA_VERSION, "functions": dict(sorted(current_coverage.items()))}
    write_json(paths["coverage"], coverage_payload)
    write_json(paths["execution"], {
        "version": SCHEMA_VERSION,
        "functions": dict(sorted(current_execution.items())),
    })
    append_inventory_changes(paths["changes"], added, changed, removed)
    return {
        "function_count": len(current),
        "added": added,
        "changed": changed,
        "removed": removed,
        "history_archived": archived_history,
        "state_pruned": pruned_state,
        "history_files": history_files,
    }


def test_reference_path(reference: str) -> str:
    return reference.split("::", 1)[0].replace("\\", "/")


def test_reference_selector(reference: str) -> str:
    parts = reference.split("::", 1)
    return parts[1].strip() if len(parts) == 2 else ""


def test_selector_exists(
    path: Path,
    selector: str,
    cache: dict[Path, tuple[str, Any]] | None = None,
) -> bool:
    if not selector:
        return False
    key = path.resolve()
    cached = cache.get(key) if cache is not None else None
    if cached is None:
        text = path.read_text(encoding="utf-8-sig", errors="replace")
        suffix = path.suffix.lower()
        if suffix in {".c", ".cc", ".cpp", ".cxx", ".h", ".hh", ".hpp"}:
            cached = (
                "selectors",
                set(re.findall(r'XA_WDD_TEST\(\s*"([^"]+)"\s*\)', text)),
            )
        elif suffix == ".py":
            try:
                tree = ast.parse(text, filename=str(path))
            except SyntaxError:
                cached = ("selectors", set())
            else:
                identifiers: set[str] = set()
                for node in tree.body:
                    if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
                        identifiers.add(node.name)
                    elif isinstance(node, ast.ClassDef):
                        for child in node.body:
                            if isinstance(child, (ast.FunctionDef, ast.AsyncFunctionDef)):
                                identifiers.add(f"{node.name}.{child.name}")
                cached = ("selectors", identifiers)
        else:
            cached = ("text", text)
        if cache is not None:
            cache[key] = cached
    kind, value = cached
    if kind == "selectors":
        return selector in value
    return selector in value


def audit_coverage(root: Path) -> tuple[BoundedIssues, int]:
    paths = project_paths(root)
    functions_payload = load_json(paths["functions"], {"functions": []})
    coverage_payload = load_json(paths["coverage"], {"functions": {}})
    issues = BoundedIssues()
    if not isinstance(functions_payload, dict) or not isinstance(coverage_payload, dict):
        issues.add("invalid functions.json or coverage.json schema")
        return issues, 0
    functions = functions_payload.get("functions", [])
    coverage = coverage_payload.get("functions", {})
    if not isinstance(functions, list) or not isinstance(coverage, dict):
        issues.add("invalid functions.json or coverage.json schema")
        return issues, 0

    selector_cache: dict[Path, tuple[str, Any]] = {}
    for function in functions:
        function_failed = False
        if not isinstance(function, dict):
            issues.add("INVALID_FUNCTION: functions.json entry must be an object")
            issues.mark_gap()
            continue
        function_id = function.get("id", "<missing-id>")
        entry = coverage.get(function_id)
        if not isinstance(entry, dict):
            issues.add(f"MISSING {function_id}: no coverage entry")
            issues.mark_gap()
            continue
        if entry.get("status") != "tested":
            issues.add(f"UNTESTED {function_id}: status is {entry.get('status', '<missing>')}")
            function_failed = True
        if entry.get("source_sha256") != function.get("source_sha256"):
            issues.add(f"STALE {function_id}: source hash changed after recorded test")
            function_failed = True
        references = entry.get("tests", [])
        if not isinstance(references, list) or not references:
            issues.add(f"UNMAPPED {function_id}: no regression test references")
            issues.mark_gap()
            continue
        for reference in references:
            if not isinstance(reference, str) or not reference.strip():
                issues.add(f"INVALID {function_id}: empty or non-string test reference")
                function_failed = True
                continue
            candidate = (root / test_reference_path(reference)).resolve()
            if not is_relative_to(candidate, paths["tests"].resolve()) or not candidate.is_file():
                issues.add(f"INVALID {function_id}: test file does not exist under tests/: {reference}")
                function_failed = True
                continue
            selector = test_reference_selector(reference)
            if not selector:
                issues.add(f"INVALID_SELECTOR {function_id}: exact test selector required: {reference}")
                function_failed = True
            elif not test_selector_exists(candidate, selector, selector_cache):
                issues.add(f"INVALID_SELECTOR {function_id}: test selector does not exist: {reference}")
                function_failed = True
        if function_failed:
            issues.mark_gap()
    return issues, len(functions)


def audit_execution(root: Path) -> tuple[BoundedIssues, int, int]:
    paths = project_paths(root)
    functions_payload = load_json(paths["functions"], {"functions": []})
    coverage_payload = load_json(paths["coverage"], {"functions": {}})
    execution_payload = load_json(paths["execution"], {"functions": {}})
    functions = functions_payload.get("functions", []) if isinstance(functions_payload, dict) else []
    coverage = coverage_payload.get("functions", {}) if isinstance(coverage_payload, dict) else {}
    execution = execution_payload.get("functions", {}) if isinstance(execution_payload, dict) else {}
    issues = BoundedIssues()
    if not isinstance(functions, list) or not isinstance(coverage, dict) or not isinstance(execution, dict):
        issues.add("invalid functions.json, coverage.json, or execution.json schema")
        return issues, 0, 0

    executed = 0
    selector_cache: dict[Path, tuple[str, Any]] = {}
    for function in functions:
        if not isinstance(function, dict):
            issues.add("INVALID_FUNCTION: functions.json entry must be an object")
            issues.mark_gap()
            continue
        function_id = function.get("id", "<missing-id>")
        entry = execution.get(function_id)
        if not isinstance(entry, dict):
            issues.add(f"UNEXECUTED {function_id}: no measured execution entry")
            issues.mark_gap()
            continue
        valid = True
        if entry.get("status") != "executed":
            issues.add(f"UNEXECUTED {function_id}: status is {entry.get('status', '<missing>')}")
            valid = False
        if entry.get("source_sha256") != function.get("source_sha256"):
            issues.add(f"STALE_EXECUTION {function_id}: source hash changed after measured execution")
            valid = False
        hits = entry.get("hits", 0)
        if not isinstance(hits, int) or isinstance(hits, bool) or hits < 1:
            issues.add(f"NO_HITS {function_id}: measured execution hit count must be positive")
            valid = False
        references = entry.get("tests", [])
        if not isinstance(references, list) or not references:
            issues.add(f"NO_EXECUTING_TEST {function_id}: no exact executing test references")
            valid = False
            references = []
        for reference in references:
            if not isinstance(reference, str) or not test_reference_selector(reference):
                issues.add(f"INVALID_EXECUTION_TEST {function_id}: exact test selector required: {reference}")
                valid = False
                continue
            candidate = (root / test_reference_path(reference)).resolve()
            if (
                not is_relative_to(candidate, paths["tests"].resolve())
                or not candidate.is_file()
                or not test_selector_exists(candidate, test_reference_selector(reference), selector_cache)
            ):
                issues.add(f"INVALID_EXECUTION_TEST {function_id}: executing test does not exist: {reference}")
                valid = False
        declared = coverage.get(function_id, {})
        declared_tests = declared.get("tests", []) if isinstance(declared, dict) else []
        if isinstance(declared_tests, list) and declared_tests and references and not set(declared_tests) & set(references):
            issues.add(f"UNPROVEN_MAPPING {function_id}: declared tests did not execute the function")
            valid = False
        if valid:
            executed += 1
        else:
            issues.mark_gap()
    return issues, len(functions), executed


def find_msvc_coverage_tool(config: dict[str, Any]) -> Path:
    configured = config.get("tool_path")
    candidates: list[Path] = []
    if isinstance(configured, str) and configured.strip():
        candidate = Path(configured)
        candidates.append(candidate if candidate.is_absolute() else candidate.resolve())
    located = shutil.which("Microsoft.CodeCoverage.Console.exe")
    if located:
        candidates.append(Path(located))
    for variable in ("ProgramFiles(x86)", "ProgramFiles"):
        base = os.environ.get(variable)
        if not base:
            continue
        candidates.extend([
            Path(base) / "Microsoft Visual Studio" / "2022" / "BuildTools" / "Common7" / "IDE" /
            "Extensions" / "Microsoft" / "CodeCoverage.Console" / "Microsoft.CodeCoverage.Console.exe",
            Path(base) / "Microsoft Visual Studio" / "2022" / "Enterprise" / "Common7" / "IDE" /
            "Extensions" / "Microsoft" / "CodeCoverage.Console" / "Microsoft.CodeCoverage.Console.exe",
            Path(base) / "Microsoft Visual Studio" / "2022" / "Professional" / "Common7" / "IDE" /
            "Extensions" / "Microsoft" / "CodeCoverage.Console" / "Microsoft.CodeCoverage.Console.exe",
            Path(base) / "Microsoft Visual Studio" / "2022" / "Community" / "Common7" / "IDE" /
            "Extensions" / "Microsoft" / "CodeCoverage.Console" / "Microsoft.CodeCoverage.Console.exe",
        ])
    for candidate in candidates:
        if candidate.is_file():
            return candidate.resolve()
    raise RuntimeError("Microsoft.CodeCoverage.Console.exe is required for measured native function execution")


def parse_msvc_coverage_hits(report: Path, root: Path) -> dict[str, list[tuple[int, int]]]:
    try:
        tree = ET.parse(report)
    except (OSError, ET.ParseError) as exc:
        raise RuntimeError(f"Cannot parse native coverage XML {report}: {exc}") from exc
    resolved_root = root.resolve()
    hits: dict[str, list[tuple[int, int]]] = {}
    for module in tree.getroot().findall("./modules/module"):
        sources: dict[str, str] = {}
        for source in module.findall("./source_files/source_file"):
            source_id = source.get("id")
            source_path = source.get("path")
            if not source_id or not source_path:
                continue
            resolved = Path(source_path).resolve()
            if not is_relative_to(resolved, resolved_root):
                continue
            relative = resolved.relative_to(resolved_root).as_posix()
            sources[source_id] = relative
        for range_node in module.findall("./functions/function/ranges/range"):
            if range_node.get("covered") not in {"yes", "partial"}:
                continue
            relative = sources.get(range_node.get("source_id", ""))
            if not relative:
                continue
            try:
                start = int(range_node.get("start_line", "0"))
                end = int(range_node.get("end_line", "0"))
            except ValueError:
                continue
            if start > 0 and end >= start:
                hits.setdefault(relative, []).append((start, end))
    return hits


def parse_msvc_skipped_function_names(report: Path) -> set[str]:
    try:
        tree = ET.parse(report)
    except (OSError, ET.ParseError) as exc:
        raise RuntimeError(f"Cannot parse native coverage XML {report}: {exc}") from exc
    names: set[str] = set()
    for node in tree.getroot().findall(".//skipped_function"):
        if node.get("reason") not in {"instrumentation_failure", "has_external_branch"}:
            continue
        name = node.get("name", "")
        type_name = node.get("type_name", "")
        if name:
            names.add(name)
            signatureless_name = name.split("(", 1)[0].strip()
            if signatureless_name:
                names.add(signatureless_name)
            if type_name:
                names.add(f"{type_name}::{name}")
                if signatureless_name:
                    names.add(f"{type_name}::{signatureless_name}")
    return names


def explicit_function_hit_markers(output: str) -> list[str]:
    prefix = "FUNCTION_HIT "
    return [
        line[len(prefix):].strip()
        for line in output.splitlines()
        if line.startswith(prefix) and line[len(prefix):].strip()
    ]


def interval_hits_function(intervals: Sequence[tuple[int, int]], start: int, end: int) -> int:
    return sum(1 for hit_start, hit_end in intervals if hit_start <= end and hit_end >= start)


def list_native_test_cases(executable: Path, timeout: int) -> list[str]:
    try:
        completed = subprocess.run(
            [str(executable), "--list"],
            cwd=executable.parent,
            check=False,
            capture_output=True,
            encoding="utf-8",
            errors="replace",
            timeout=timeout,
        )
    except (OSError, subprocess.SubprocessError) as exc:
        raise RuntimeError(f"Cannot list native tests from {executable}: {exc}") from exc
    if completed.returncode != 0:
        raise RuntimeError(
            f"Cannot list native tests from {executable}: exit {completed.returncode}: "
            f"{report_excerpt((completed.stdout or '') + (completed.stderr or ''))}"
        )
    cases = [line[5:] for line in completed.stdout.splitlines() if line.startswith("TEST ")]
    if not cases or len(cases) != len(set(cases)):
        raise RuntimeError(f"Native test listing is empty or contains duplicate names: {executable}")
    return cases


def collect_msvc_function_execution(root: Path, config: dict[str, Any]) -> dict[str, int]:
    targets = config.get("test_targets", [])
    if not isinstance(targets, list) or not targets:
        raise RuntimeError("function_execution.collector.test_targets must be a non-empty array")
    try:
        timeout = int(config.get("timeout_seconds", DEFAULT_TIMEOUT_SECONDS))
    except (TypeError, ValueError) as exc:
        raise RuntimeError("function_execution collector timeout_seconds must be an integer") from exc
    if timeout < 1 or timeout > 86400:
        raise RuntimeError("function_execution collector timeout_seconds must be between 1 and 86400")

    tool = find_msvc_coverage_tool(config)
    allow_explicit_markers = config.get("allow_explicit_hit_markers", False)
    if not isinstance(allow_explicit_markers, bool):
        raise RuntimeError("function execution collector allow_explicit_hit_markers must be a boolean")
    paths = project_paths(root)
    inventory_payload = load_json(paths["functions"], {"functions": []})
    functions = inventory_payload.get("functions", []) if isinstance(inventory_payload, dict) else []
    if not isinstance(functions, list):
        raise RuntimeError("functions.json functions must be an array")
    by_path: dict[str, list[dict[str, Any]]] = {}
    by_id: dict[str, dict[str, Any]] = {}
    for function in functions:
        if not isinstance(function, dict):
            continue
        by_path.setdefault(str(function.get("path", "")).casefold(), []).append(function)
        by_id[str(function.get("id"))] = function
    executing_tests: dict[str, set[str]] = {str(item.get("id")): set() for item in functions if isinstance(item, dict)}
    hit_counts: dict[str, int] = {identifier: 0 for identifier in executing_tests}
    coverage_hit_functions: set[str] = set()
    marker_hit_functions: set[str] = set()
    cases_run = 0

    with tempfile.TemporaryDirectory(prefix="runtests-native-coverage-") as temporary:
        output_root = Path(temporary)
        for target_index, target in enumerate(targets):
            if not isinstance(target, dict):
                raise RuntimeError("each native coverage target must be an object")
            executable_value = target.get("executable")
            test_file_value = target.get("test_file")
            if not isinstance(executable_value, str) or not isinstance(test_file_value, str):
                raise RuntimeError("each native coverage target requires executable and test_file")
            executable = (root / executable_value).resolve()
            test_file = (root / test_file_value).resolve()
            if not is_relative_to(executable, root) or not executable.is_file():
                raise RuntimeError(f"native coverage executable is invalid: {executable_value}")
            if not is_relative_to(test_file, paths["tests"].resolve()) or not test_file.is_file():
                raise RuntimeError(f"native coverage test_file is invalid: {test_file_value}")
            relative_test_file = test_file.relative_to(root).as_posix()
            for case_index, case in enumerate(list_native_test_cases(executable, timeout)):
                report = output_root / f"{target_index:02d}-{case_index:04d}.xml"
                try:
                    completed = subprocess.run(
                        [
                            str(tool), "collect", str(executable), case,
                            "--include-files", str(executable),
                            "--output", str(report), "--output-format", "xml",
                            "--nologo", "--disable-console-output",
                        ],
                        cwd=executable.parent,
                        check=False,
                        capture_output=True,
                        encoding="utf-8",
                        errors="replace",
                        timeout=timeout,
                    )
                except (OSError, subprocess.SubprocessError) as exc:
                    raise RuntimeError(
                        f"Native coverage failed for {relative_test_file}::{case}: {exc}"
                    ) from exc
                if completed.returncode != 0 or not report.is_file():
                    details = (completed.stdout or "") + (completed.stderr or "")
                    raise RuntimeError(
                        f"Native coverage failed for {relative_test_file}::{case}: "
                        f"exit {completed.returncode}: {report_excerpt(details)}"
                    )
                hits = parse_msvc_coverage_hits(report, root)
                reference = f"{relative_test_file}::{case}"
                for relative, intervals in hits.items():
                    for function in by_path.get(relative.casefold(), []):
                        start = int(function.get("line", 0))
                        end = int(function.get("end_line", start))
                        count = interval_hits_function(intervals, start, end)
                        if count > 0:
                            identifier = str(function["id"])
                            executing_tests[identifier].add(reference)
                            hit_counts[identifier] += count
                            coverage_hit_functions.add(identifier)
                markers = explicit_function_hit_markers(
                    (completed.stdout or "") + (completed.stderr or "")
                )
                if markers and not allow_explicit_markers:
                    raise RuntimeError(
                        f"Explicit function hit marker is not enabled for {relative_test_file}::{case}"
                    )
                if markers:
                    skipped_names = parse_msvc_skipped_function_names(report)
                    for identifier in markers:
                        function = by_id.get(identifier)
                        if function is None:
                            raise RuntimeError(
                                f"Explicit function hit marker names an unknown current function: {identifier}"
                            )
                        function_name = str(function.get("name", "")).split("#", 1)[0]
                        if function_name not in skipped_names:
                            raise RuntimeError(
                                "Explicit function hit marker is allowed only when MSVC reports "
                                f"instrumentation_failure or has_external_branch for {identifier}"
                            )
                        executing_tests[identifier].add(reference)
                        hit_counts[identifier] += 1
                        marker_hit_functions.add(identifier)
                cases_run += 1

    previous_payload = load_json(paths["execution"], {"version": SCHEMA_VERSION, "functions": {}})
    previous = previous_payload.get("functions", {}) if isinstance(previous_payload, dict) else {}
    if not isinstance(previous, dict):
        previous = {}
    coverage_payload = load_json(paths["coverage"], {"version": SCHEMA_VERSION, "functions": {}})
    coverage = coverage_payload.get("functions", {}) if isinstance(coverage_payload, dict) else {}
    if not isinstance(coverage, dict):
        raise RuntimeError("coverage.json functions must be an object")
    current_ids = {str(item.get("id")) for item in functions if isinstance(item, dict)}
    now = utc_now()
    previous = {
        identifier: entry
        for identifier, entry in previous.items()
        if identifier in current_ids and isinstance(entry, dict)
    }
    for function in functions:
        identifier = str(function["id"])
        references = sorted(executing_tests.get(identifier, set()))
        evidence_kinds: list[str] = []
        if identifier in coverage_hit_functions:
            evidence_kinds.append("msvc-covered-range")
        if identifier in marker_hit_functions:
            evidence_kinds.append("compiler-exclusion-runtime-marker")
        previous[identifier] = {
            "source_sha256": function.get("source_sha256"),
            "status": "executed" if references else "unexecuted",
            "hits": hit_counts.get(identifier, 0),
            "tests": references,
            "measured_at": now,
            "collector": "msvc-code-coverage",
            "evidence_kinds": evidence_kinds,
        }
        if references:
            coverage_entry = coverage.get(identifier)
            if not isinstance(coverage_entry, dict):
                coverage_entry = {}
                coverage[identifier] = coverage_entry
            coverage_entry.update({
                "source_sha256": function.get("source_sha256"),
                "status": "tested",
                "tests": references,
            })
            coverage_entry.pop("removed_at", None)
            coverage_entry.setdefault(
                "notes",
                "Mapped automatically from per-test measured function execution.",
            )
    write_json(paths["execution"], {
        "version": SCHEMA_VERSION,
        "generated_at": now,
        "functions": dict(sorted(previous.items())),
    })
    write_json(paths["coverage"], {
        "version": SCHEMA_VERSION,
        "functions": dict(sorted(coverage.items())),
    })
    executed = sum(1 for identifier in current_ids if executing_tests.get(identifier))
    return {"cases_run": cases_run, "functions": len(current_ids), "executed": executed}


def collect_function_execution(root: Path) -> dict[str, int] | None:
    manifest = load_json(project_paths(root)["manifest"], {})
    config = manifest.get("function_execution", {}) if isinstance(manifest, dict) else {}
    if not isinstance(config, dict):
        raise RuntimeError("manifest function_execution must be an object")
    evidence_file = config.get("evidence_file", "tests/current functions/execution.json")
    if not isinstance(evidence_file, str) or not evidence_file.strip():
        raise RuntimeError("manifest function_execution.evidence_file must be a project-relative path")
    if (root / evidence_file).resolve() != project_paths(root)["execution"].resolve():
        raise RuntimeError(
            "function execution evidence_file must be tests/current functions/execution.json"
        )
    collector = config.get("collector")
    if collector is None:
        return None
    if not isinstance(collector, dict):
        raise RuntimeError("manifest function_execution.collector must be an object")
    kind = collector.get("kind")
    if kind == "msvc-code-coverage":
        return collect_msvc_function_execution(root, collector)
    raise RuntimeError(f"unsupported function execution collector: {kind}")


def execution_gate_required(manifest: dict[str, Any]) -> bool:
    config = manifest.get("function_execution", {})
    if not isinstance(config, dict):
        raise RuntimeError("manifest function_execution must be an object")
    required = config.get("required", True)
    if not isinstance(required, bool):
        raise RuntimeError("manifest function_execution.required must be true or false")
    return required


def safe_slug(value: str) -> str:
    slug = re.sub(r"[^a-z0-9]+", "-", value.lower()).strip("-")
    return slug[:60] or "test-failure"


def redact_sensitive(value: str) -> str:
    patterns = (
        (re.compile(r"(?i)(authorization\s*:\s*bearer)\s+[^\s]+"), r"\1 [REDACTED]"),
        (
            re.compile(r"(?i)\b(api[_ -]?key|access[_ -]?token|refresh[_ -]?token|password|passwd|secret)\b(\s*[:=]\s*)[^\s,;]+"),
            r"\1\2[REDACTED]",
        ),
    )
    redacted = value
    for pattern, replacement in patterns:
        redacted = pattern.sub(replacement, redacted)
    return redacted


def record_failure(root: Path, name: str, details: str) -> Path:
    errors = project_paths(root)["errors"]
    errors.mkdir(parents=True, exist_ok=True)
    stamp = dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%d-%H%M%S-%f")
    path = errors / f"{stamp}-{safe_slug(name)}.md"
    safe_details = report_excerpt(details, limit=MAX_FAILURE_DETAILS_CHARS)
    content = f"# {name}\n\n- Observed: Automated verification failed.\n- Expected: The configured automated gate passes.\n- Root cause: Unconfirmed; diagnose before closing this record.\n- Regression test: See command and output below.\n- Fix: Pending.\n- Verification: Pending.\n\n## Evidence\n\n```text\n{safe_details}\n```\n"
    path.write_text(content, encoding="utf-8")
    return path


def daily_report_path(root: Path, when: dt.datetime | None = None) -> Path:
    local_time = when or dt.datetime.now().astimezone()
    stamp = local_time.strftime("%m_%d_%y-%H-%M-%S")
    return project_paths(root)["docs"] / f"runtests_{stamp}.md"


def available_daily_report_path(root: Path, when: dt.datetime) -> Path:
    report_time = when
    report = daily_report_path(root, report_time)
    while report.exists():
        report_time += dt.timedelta(seconds=1)
        report = daily_report_path(root, report_time)
    return report


def report_excerpt(value: str, limit: int = 3000) -> str:
    stripped = value.strip()
    if len(stripped) > limit:
        output_marker_match = re.search(r"\[\d+ output bytes omitted\]", stripped)
        output_marker = f"\n{output_marker_match.group(0)}\n" if output_marker_match else ""
        marker_budget = len(output_marker)
        available = max(2, limit - marker_budget)
        head_size = available // 2
        tail_size = available - head_size
        omitted = len(stripped) - head_size - tail_size
        stripped = (
            stripped[:head_size]
            + f"\n[{omitted} characters omitted]\n"
            + output_marker.lstrip("\n")
            + stripped[-tail_size:]
        )
    cleaned = redact_sensitive(stripped).replace("```", "'''")
    if not cleaned:
        return "(no command output)"
    return cleaned


def spooled_output_excerpt(handle: Any, limit: int = MAX_COMMAND_OUTPUT_BYTES) -> tuple[str, int]:
    """Read a bounded UTF-8 head/tail excerpt from a binary output spool."""
    handle.flush()
    handle.seek(0, os.SEEK_END)
    total = handle.tell()
    handle.seek(0)
    if total <= limit:
        payload = handle.read()
        return payload.decode("utf-8", errors="replace"), total
    head_size = limit // 2
    tail_size = limit - head_size
    head = handle.read(head_size)
    handle.seek(-tail_size, os.SEEK_END)
    tail = handle.read(tail_size)
    omitted = total - len(head) - len(tail)
    excerpt = (
        head.decode("utf-8", errors="replace")
        + f"\n[{omitted} output bytes omitted]\n"
        + tail.decode("utf-8", errors="replace")
    )
    return excerpt, total


def issue_text(issues: Sequence[str], label: str) -> str:
    lines = list(issues)
    omitted = issues.omitted_count if isinstance(issues, BoundedIssues) else 0
    if omitted:
        lines.append(f"{label}_ISSUES_OMITTED={omitted}")
    return "\n".join(lines)


def print_issue_samples(issues: Sequence[str], label: str) -> None:
    for issue in issues:
        print(issue)
    omitted = issues.omitted_count if isinstance(issues, BoundedIssues) else 0
    if omitted:
        print(f"{label}_ISSUES_OMITTED={omitted}")


def markdown_cell(value: Any) -> str:
    return str(value).replace("|", "\\|").replace("\r", " ").replace("\n", " ")


def redacted_argv(argv: Any) -> str:
    value = argv if isinstance(argv, list) else []
    return redact_sensitive(json.dumps(value, ensure_ascii=False))


def function_run_summary(root: Path, issues: Sequence[str]) -> dict[str, Any]:
    payload = load_json(project_paths(root)["functions"], {"functions": []})
    functions = payload.get("functions", []) if isinstance(payload, dict) else []
    if not isinstance(functions, list):
        functions = []
    identifiers = [
        str(item.get("id"))
        for item in functions
        if isinstance(item, dict) and item.get("id")
    ]
    if isinstance(issues, BoundedIssues):
        gap_count = min(len(identifiers), issues.gap_count)
        if issues.total_count and not gap_count:
            gap_count = len(identifiers)
        gap_ids: list[str] = []
    else:
        matched_gap_ids = {
            identifier
            for issue in issues
            for identifier in identifiers
            if f" {identifier}:" in issue
        }
        if issues and not matched_gap_ids:
            matched_gap_ids = set(identifiers)
        gap_count = len(matched_gap_ids)
        gap_ids = sorted(matched_gap_ids)
    return {
        "total": len(identifiers),
        "current": max(0, len(identifiers) - gap_count),
        "gaps": gap_count,
        "gap_ids": gap_ids,
    }


def write_daily_report(
    root: Path,
    inventory: dict[str, Any],
    mapping_issues: Sequence[str],
    execution_issues: Sequence[str],
    command_results: Sequence[dict[str, Any]],
    function_evidence: Path | None,
    collector_result: dict[str, Any] | None = None,
    run_started: dt.datetime | None = None,
    execution_required: bool = True,
) -> tuple[Path, dict[str, int]]:
    manifest = load_json(project_paths(root)["manifest"], {})
    project_name = manifest.get("project_name", root.name) if isinstance(manifest, dict) else root.name
    mappings = function_run_summary(root, mapping_issues)
    executions = function_run_summary(root, execution_issues)
    passed = sum(1 for result in command_results if result.get("status") == "passed")
    failed = sum(1 for result in command_results if result.get("status") != "passed")
    local_run_started = run_started or dt.datetime.now().astimezone()
    report = available_daily_report_path(root, local_run_started)
    generated_utc = utc_now()
    relative_report = report.relative_to(root).as_posix()
    collector_failed = bool(collector_result and collector_result.get("status") == "failed")
    blocking_follow_up = (
        failed > 0
        or mappings["gaps"] > 0
        or (execution_required and executions["gaps"] > 0)
        or collector_failed
    )
    optional_execution_open = not execution_required and executions["gaps"] > 0
    if blocking_follow_up:
        headline = (
        f"Follow-up required: {passed} test commands passed; {failed} failed; "
        f"{mappings['current']}/{mappings['total']} mappings are current; "
        f"{executions['current']}/{executions['total']} functions have measured execution."
        )
    elif optional_execution_open:
        headline = (
            f"Automated required gates passed: {passed} test commands passed; 0 failed; "
            f"{mappings['current']}/{mappings['total']} mappings are current; "
            f"{executions['current']}/{executions['total']} functions have measured execution "
            "under an optional execution gate; full verification remains open."
        )
    else:
        headline = (
            f"Automated gates passed: {passed} test commands passed; 0 failed; "
            f"{mappings['current']}/{mappings['total']} mappings are current; "
            f"{executions['current']}/{executions['total']} functions have measured execution."
        )

    lines = [
        f"# RunTests report - {local_run_started.strftime('%m/%d/%y %H:%M:%S')}",
        "",
        f"- Project: `{project_name}`",
        f"- Project root: `{root}`",
        f"- Run started (local): `{local_run_started.isoformat(timespec='seconds')}`",
        f"- Generated: `{generated_utc}`",
        f"- Latest result: **{headline}**",
        "",
        "## Counts",
        "",
        "| Surface | Passed/current | Failed/gaps |",
        "|---|---:|---:|",
        f"| Test commands | {passed} | {failed} |",
        f"| Function mappings | {mappings['current']} | {mappings['gaps']} |",
        f"| Measured function executions ({'required' if execution_required else 'optional'}) | {executions['current']} | {executions['gaps']} |",
        f"| Individual test cases measured | {collector_result.get('cases_run', 0) if collector_result else 0} | 0 |",
        "",
        "## Inventory delta",
        "",
        f"- Functions inventoried: {mappings['total']}",
        f"- Added: {len(inventory.get('added', []))}",
        f"- Changed: {len(inventory.get('changed', []))}",
        f"- Removed: {len(inventory.get('removed', []))}",
        f"- Meaningful removed records archived: {inventory.get('history_archived', 0)}",
        f"- Obsolete generated/excluded state pruned: {inventory.get('state_pruned', 0)}",
        "",
        "## Function verification details",
        "",
    ]

    function_evidence_text = (
        function_evidence.relative_to(root).as_posix()
        if function_evidence
        else "not written; no blocking function-verification evidence was required"
    )
    execution_evidence_text = (
        function_evidence_text
        if execution_required and execution_issues and function_evidence
        else "not written; optional evidence gaps are not failure records"
    )
    if mapping_issues:
        lines.extend([
            "### Function traceability",
            "",
            f"- Current mappings: {mappings['current']}/{mappings['total']}",
            f"- Functions needing mapping work: {mappings['gaps']}",
            f"- Evidence: `{function_evidence_text}`",
            "",
            "```text",
            report_excerpt(issue_text(mapping_issues, "MAPPING"), limit=MAX_REPORT_EXCERPT_CHARS),
            "```",
            "",
        ])

    if execution_issues:
        lines.extend([
            "### Measured function execution",
            "",
            f"- Functions measured as executed: {executions['current']}/{executions['total']}",
            f"- Functions without current execution proof: {executions['gaps']}",
            f"- Gate: {'required' if execution_required else 'optional; gaps remain open for full verification'}",
            f"- Evidence: `{execution_evidence_text}`",
            "",
            "```text",
            report_excerpt(issue_text(execution_issues, "EXECUTION"), limit=MAX_REPORT_EXCERPT_CHARS),
            "```",
            "",
        ])

    if collector_result:
        collector_evidence = collector_result.get("evidence") or "No separate collector evidence file."
        lines.extend([
            "### Function execution collector",
            "",
            f"- Result: {collector_result.get('status', 'unknown')}",
            f"- Individual test cases measured: {collector_result.get('cases_run', 0)}",
            f"- Functions measured as executed: {collector_result.get('executed', 0)}",
            f"- Evidence: `{collector_evidence}`",
            f"- Details: {collector_result.get('details', 'None.')}",
            "",
        ])

    all_failed_results = [result for result in command_results if result.get("status") != "passed"]
    failed_results = all_failed_results[:MAX_REPORT_COMMAND_SAMPLES]
    for index, result in enumerate(failed_results, start=1):
        evidence = result.get("evidence") or "No separate evidence file; see this report."
        lines.extend([
            f"### Test command T-{index:02d}: {result.get('name', 'unnamed-test-command')}",
            "",
            f"- Result: {result.get('status', 'failed')}",
            f"- Exit code: {result.get('exit_code', 'not available')}",
            f"- Command argv: `{redacted_argv(result.get('argv', []))}`",
            f"- Evidence: `{evidence}`",
            "",
            "```text",
            report_excerpt(str(result.get("details", result.get("reason", "No details captured.")))),
            "```",
            "",
        ])
    failed_results_omitted = len(all_failed_results) - len(failed_results)
    if failed_results_omitted:
        lines.extend([
            f"FAILED_COMMANDS_OMITTED={failed_results_omitted}",
            "See the exact aggregate counts and project-local failure evidence for omitted commands.",
            "",
        ])

    if (
        not mapping_issues
        and (not execution_issues or not execution_required)
        and not all_failed_results
        and not collector_failed
    ):
        lines.extend(["No automated function or test-command failures were recorded.", ""])

    lines.extend(["## Passing test commands", ""])
    all_passed_results = [result for result in command_results if result.get("status") == "passed"]
    passed_results = all_passed_results[:MAX_REPORT_COMMAND_SAMPLES]
    if passed_results:
        lines.extend(["| Command | Result |", "|---|---|"])
        for result in passed_results:
            lines.append(f"| {markdown_cell(result.get('name', 'unnamed-test-command'))} | PASS |")
        passed_results_omitted = len(all_passed_results) - len(passed_results)
        if passed_results_omitted:
            lines.append(f"| PASSED_COMMANDS_OMITTED={passed_results_omitted} | PASS |")
        lines.append("")
    else:
        lines.extend(["None.", ""])

    evidence_files: list[str] = []
    if function_evidence:
        evidence_files.append(function_evidence.relative_to(root).as_posix())
    if collector_result and isinstance(collector_result.get("evidence"), str):
        evidence_files.append(str(collector_result["evidence"]))
    for result in failed_results:
        evidence = result.get("evidence")
        if isinstance(evidence, str) and evidence not in evidence_files:
            evidence_files.append(evidence)
    lines.extend([
        "## Files created or updated",
        "",
        f"- `{relative_report}` - timestamped summary and gameplan for this run.",
        "- `tests/current functions/functions.json` - refreshed inventory.",
        "- `tests/current functions/coverage.json` - current function-to-test traceability.",
        "- `tests/current functions/execution.json` - current measured function execution evidence.",
    ])
    for evidence in evidence_files:
        lines.append(f"- `{evidence}` - failure evidence.")
    for history_file in inventory.get("history_files", []):
        lines.append(f"- `{history_file}` - streamable removed-function history.")

    lines.extend(["", "## Investigation and gameplan", ""])
    if mapping_issues:
        lines.extend([
            "### G-01 - Function traceability gaps",
            "",
            "- Root cause: Pending source-backed diagnosis. Do not mark coverage `tested` without a passing mapped test.",
            "- Suggested solution: Add deterministic seams/tests for the listed functions, run them, then copy the refreshed hashes into `coverage.json`.",
            "- Before: The listed functions are missing, stale, invalid, or unmapped.",
            "- After: Each current hash maps to an existing regression test that has passed.",
            "- Risks: False confidence if mappings are updated without executing the relevant behavior.",
            f"- Targeted retest: `python -B <skill-directory>/scripts/runtests.py check --project {json.dumps(str(root))}`",
            f"- Full retest: `python -B <skill-directory>/scripts/runtests.py run --project {json.dumps(str(root))}`",
            "",
        ])
    if execution_issues or collector_failed:
        lines.extend([
            "### G-02 - Measured function execution gaps"
            + ("" if execution_required or collector_failed else " (optional automated gate)"),
            "",
            "- Root cause: Pending source-backed diagnosis. A test-file reference alone is not execution proof.",
            "- Suggested solution: Run each exact test case through the configured coverage collector, then add deterministic tests or test-owned probes for every unexecuted function. Optional execution gaps do not fail the required automated gate, but they remain open before a full-verification claim.",
            "- Before: One or more current functions lack a positive measured hit tied to an exact test selector.",
            "- After: Every current function has a positive hit and names the exact test case that executed it.",
            "- Risks: Compiler optimization, inlining, stale binaries, and static source checks can all create false confidence unless the latest test binary is measured.",
            f"- Targeted retest: `python -B <skill-directory>/scripts/runtests.py check --project {json.dumps(str(root))}`",
            f"- Full retest: `python -B <skill-directory>/scripts/runtests.py run --project {json.dumps(str(root))}`",
            "",
        ])
    for index, result in enumerate(failed_results, start=1):
        lines.extend([
            f"### T-{index:02d} - {result.get('name', 'unnamed-test-command')}",
            "",
            "- Root cause: Pending source-backed diagnosis.",
            "- Suggested solution: Reproduce the smallest failing case, trace the owning code, and propose the smallest safe fix without weakening the regression.",
            "- Before: Capture the current failing behavior and relevant code excerpt.",
            "- After: Describe the intended behavior and proposed code/test change.",
            "- Risks: Identify production, data, compatibility, security, or runtime risks before implementation.",
            f"- Targeted retest argv: `{redacted_argv(result.get('argv', []))}`",
            f"- Full retest: `python -B <skill-directory>/scripts/runtests.py run --project {json.dumps(str(root))}`",
            "",
        ])
    if not blocking_follow_up and not optional_execution_open:
        lines.extend([
            "No automated failure requires a code-change gameplan. Record any still-required integration, device, browser, network, database, or live-runtime acceptance below.",
            "",
        ])
    lines.extend(["## Next steps", ""])
    if blocking_follow_up:
        lines.extend([
            "1. Replace every pending gameplan field with a source-backed diagnosis or a bounded hypothesis plus the missing evidence needed.",
            "2. Review the proposed Before/After changes and choose which fixes to authorize.",
            "3. Implement only the authorized changes, preserving each failing regression.",
            "4. Run targeted retests first, then rerun the complete `$runtests` workflow; the rerun will create a new timestamped report and preserve this one.",
            "5. Keep unrun integration/runtime acceptance listed as open; do not infer it from static or unit tests.",
            "",
        ])
    elif optional_execution_open:
        lines.extend([
            "1. Treat the required automated gate as passed while keeping measured execution explicitly open.",
            "2. Configure or run a supported per-test execution collector before calling the project fully verified.",
            "3. Add a regression and refresh both mapping and execution evidence whenever a function changes or a defect is found.",
            "",
        ])
    else:
        lines.extend([
            "1. Record and perform any still-required integration, device, browser, network, database, UI, game, or live-runtime acceptance.",
            "2. Add a regression and refresh both mapping and measured-execution evidence whenever a function changes or a defect is found.",
            "",
        ])
    write_text_atomic(report, "\n".join(lines))
    return report, {
        "commands_passed": passed,
        "commands_failed": failed,
        "function_mappings_current": int(mappings["current"]),
        "mapping_gaps": int(mappings["gaps"]),
        "functions_executed": int(executions["current"]),
        "execution_gaps": int(executions["gaps"]),
    }


def resolve_argv(argv: Sequence[str]) -> list[str]:
    if not argv:
        raise RuntimeError("test command argv cannot be empty")
    resolved = list(argv)
    first = resolved[0]
    if first.lower() in {"python", "python3", "py"}:
        resolved[0] = sys.executable
    elif os.name == "nt" and "\\" not in first and "/" not in first:
        # CreateProcess does not honor PATHEXT, so bare launcher names like
        # npm, mvn, or gradle must be resolved to their .cmd/.bat shims.
        located = shutil.which(first)
        if located:
            resolved[0] = located
        elif first.lower() == "npm":
            resolved[0] = "npm.cmd"
    return resolved


def run_manifest_commands(root: Path, results: list[dict[str, Any]] | None = None) -> list[str]:
    manifest = load_json(project_paths(root)["manifest"], {})
    commands = manifest.get("commands", [])
    if not isinstance(commands, list):
        raise RuntimeError("manifest commands must be an array")
    failures: list[str] = []
    command_results = results if results is not None else []
    if len(commands) > MAX_MANIFEST_COMMANDS:
        reason = (
            f"INVALID_COMMANDS: manifest contains {len(commands)} commands; "
            f"maximum is {MAX_MANIFEST_COMMANDS}"
        )
        failures.append(reason)
        command_results.append({
            "name": "test manifest",
            "argv": [],
            "status": "failed",
            "exit_code": None,
            "reason": reason,
            "details": reason,
            "evidence": None,
        })
        return failures
    enabled: list[dict[str, Any]] = []
    for index, item in enumerate(commands, start=1):
        if not isinstance(item, dict):
            reason = f"INVALID_COMMAND #{index}: each command must be an object"
            failures.append(reason)
            command_results.append({
                "name": f"manifest command #{index}",
                "argv": [],
                "status": "failed",
                "exit_code": None,
                "reason": reason,
                "details": reason,
                "evidence": None,
            })
            continue
        enabled_value = item.get("enabled", True)
        if not isinstance(enabled_value, bool):
            name = str(item.get("name", f"manifest command #{index}"))
            reason = f"INVALID_COMMAND {name}: enabled must be true or false"
            failures.append(reason)
            command_results.append({
                "name": name,
                "argv": item.get("argv", []) if isinstance(item.get("argv"), list) else [],
                "status": "failed",
                "exit_code": None,
                "reason": reason,
                "details": reason,
                "evidence": None,
            })
            continue
        if enabled_value:
            enabled.append(item)
    if not enabled:
        if failures:
            return failures
        reason = "NO_TEST_COMMANDS: add at least one enabled native test command to tests/test-manifest.json"
        failures.append(reason)
        command_results.append({
            "name": "test manifest",
            "argv": [],
            "status": "failed",
            "exit_code": None,
            "reason": reason,
            "details": reason,
            "evidence": None,
        })
        return failures

    resolved_root = root.resolve()
    for command in enabled:
        name = str(command.get("name", "unnamed-test-command"))
        argv = command.get("argv")
        if not isinstance(argv, list) or not all(isinstance(item, str) and item for item in argv):
            reason = f"INVALID_COMMAND {name}: argv must be a non-empty string array"
            failures.append(reason)
            command_results.append({
                "name": name,
                "argv": argv if isinstance(argv, list) else [],
                "status": "failed",
                "exit_code": None,
                "reason": reason,
                "details": reason,
                "evidence": None,
            })
            continue
        cwd = (resolved_root / str(command.get("cwd", "."))).resolve()
        if not is_relative_to(cwd, resolved_root) or not cwd.is_dir():
            reason = f"INVALID_COMMAND {name}: cwd must be an existing directory inside the project"
            failures.append(reason)
            command_results.append({
                "name": name,
                "argv": argv,
                "status": "failed",
                "exit_code": None,
                "reason": reason,
                "details": reason,
                "evidence": None,
            })
            continue
        try:
            timeout = int(command.get("timeout_seconds", DEFAULT_TIMEOUT_SECONDS))
        except (TypeError, ValueError):
            reason = f"INVALID_COMMAND {name}: timeout_seconds must be an integer number of seconds"
            failures.append(reason)
            command_results.append({
                "name": name,
                "argv": argv,
                "status": "failed",
                "exit_code": None,
                "reason": reason,
                "details": reason,
                "evidence": None,
            })
            continue
        if timeout < 1 or timeout > 86400:
            reason = f"INVALID_COMMAND {name}: timeout_seconds must be between 1 and 86400"
            failures.append(reason)
            command_results.append({
                "name": name,
                "argv": argv,
                "status": "failed",
                "exit_code": None,
                "reason": reason,
                "details": reason,
                "evidence": None,
            })
            continue
        resolved_argv = resolve_argv(argv)
        print(f"RUN {name}: {redacted_argv(argv)}")
        environment = os.environ.copy()
        environment["PYTHONDONTWRITEBYTECODE"] = "1"
        with tempfile.TemporaryFile(mode="w+b") as output_spool:
            try:
                completed = subprocess.run(
                    resolved_argv,
                    cwd=cwd,
                    check=False,
                    stdout=output_spool,
                    stderr=subprocess.STDOUT,
                    timeout=timeout,
                    env=environment,
                )
                combined, output_bytes = spooled_output_excerpt(output_spool)
                if combined:
                    print_output(redact_sensitive(combined).rstrip())
                if completed.returncode != 0:
                    details = (
                        f"Command: {json.dumps(argv)}\n"
                        f"Exit code: {completed.returncode}\n"
                        f"Captured output bytes: {output_bytes}\n\n{combined}"
                    )
                    evidence = record_failure(root, name, details)
                    relative_evidence = evidence.relative_to(root).as_posix()
                    failures.append(f"FAILED {name}: exit {completed.returncode}; evidence {relative_evidence}")
                    command_results.append({
                        "name": name,
                        "argv": argv,
                        "status": "failed",
                        "exit_code": completed.returncode,
                        "details": combined,
                        "output_bytes": output_bytes,
                        "evidence": relative_evidence,
                    })
                else:
                    print(f"PASS {name}")
                    command_results.append({
                        "name": name,
                        "argv": argv,
                        "status": "passed",
                        "exit_code": completed.returncode,
                        "details": "",
                        "output_bytes": output_bytes,
                        "evidence": None,
                    })
            except subprocess.TimeoutExpired as exc:
                combined, output_bytes = spooled_output_excerpt(output_spool)
                timeout_details = f"Command timed out after {timeout} seconds."
                if combined:
                    print_output(redact_sensitive(combined).rstrip())
                    timeout_details += f"\nCaptured output bytes: {output_bytes}\n\n{combined}"
                details = f"Command: {json.dumps(argv)}\n{timeout_details}"
                evidence = record_failure(root, name, details)
                relative_evidence = evidence.relative_to(root).as_posix()
                failures.append(f"FAILED {name}: timed out after {timeout} seconds; evidence {relative_evidence}")
                command_results.append({
                    "name": name,
                    "argv": argv,
                    "status": "failed",
                    "exit_code": None,
                    "details": timeout_details,
                    "output_bytes": output_bytes,
                    "evidence": relative_evidence,
                })
            except OSError as exc:
                details = f"Command: {json.dumps(argv)}\nException: {exc}"
                evidence = record_failure(root, name, details)
                relative_evidence = evidence.relative_to(root).as_posix()
                safe_exception = redact_sensitive(str(exc))
                failures.append(f"FAILED {name}: {safe_exception}; evidence {relative_evidence}")
                command_results.append({
                    "name": name,
                    "argv": argv,
                    "status": "failed",
                    "exit_code": None,
                    "details": safe_exception,
                    "output_bytes": 0,
                    "evidence": relative_evidence,
                })
    return failures


def command_init(root: Path, track_tests: bool) -> int:
    result = bootstrap(root, track_tests)
    print(json.dumps(bounded_inventory_result(result), indent=2))
    return 0


def command_inventory(root: Path) -> int:
    result = inventory_project(root)
    print(json.dumps(bounded_inventory_result(result), indent=2))
    return 0


def bounded_inventory_result(result: dict[str, Any]) -> dict[str, Any]:
    bounded = {key: value for key, value in result.items() if key not in {"added", "changed", "removed"}}
    for key in ("added", "changed", "removed"):
        values = result.get(key, [])
        if not isinstance(values, list):
            values = []
        bounded[f"{key}_count"] = len(values)
        bounded[f"{key}_sample"] = values[:MAX_INVENTORY_DELTA_ITEMS]
    return bounded


def command_compact(root: Path) -> int:
    result = inventory_project(root)
    print(f"FUNCTIONS={result['function_count']}")
    print(f"ADDED={len(result['added'])}")
    print(f"CHANGED={len(result['changed'])}")
    print(f"REMOVED={len(result['removed'])}")
    print(f"HISTORY_ARCHIVED={result['history_archived']}")
    print(f"STATE_PRUNED={result['state_pruned']}")
    for history_file in result["history_files"]:
        print(f"HISTORY_FILE={history_file}")
    print("COMPACTION=PASS")
    return 0


def run_check(root: Path) -> tuple[int, list[str], dict[str, Any]]:
    inventory = inventory_project(root)
    manifest = load_json(project_paths(root)["manifest"], {})
    if not isinstance(manifest, dict):
        raise RuntimeError("test-manifest.json must contain a JSON object")
    execution_required = execution_gate_required(manifest)
    mapping_issues, function_count = audit_coverage(root)
    execution_issues, execution_count, executed = audit_execution(root)
    issues = [*mapping_issues, *execution_issues]
    mapping_summary = function_run_summary(root, mapping_issues)
    print(f"FUNCTIONS={function_count}")
    print(f"FUNCTION_MAPPINGS_CURRENT={mapping_summary['current']}")
    print(f"MAPPING_GAPS={mapping_summary['gaps']}")
    print(f"FUNCTIONS_EXECUTED={executed}")
    print(f"EXECUTION_GAPS={max(0, execution_count - executed)}")
    print(f"EXECUTION_GATE={'REQUIRED' if execution_required else 'OPTIONAL'}")
    print(f"ADDED={len(inventory['added'])}")
    print(f"CHANGED={len(inventory['changed'])}")
    print(f"REMOVED={len(inventory['removed'])}")
    print(f"HISTORY_ARCHIVED={inventory['history_archived']}")
    print(f"STATE_PRUNED={inventory['state_pruned']}")
    gate_failed = bool(mapping_issues) or (execution_required and bool(execution_issues))
    if gate_failed:
        print("FUNCTION_CHECK=FAIL")
    else:
        print("FUNCTION_CHECK=PASS")
    print_issue_samples(mapping_issues, "MAPPING")
    print_issue_samples(execution_issues, "EXECUTION")
    return (1 if gate_failed else 0), issues, inventory


def command_check(root: Path) -> int:
    return run_check(root)[0]


def command_run(root: Path) -> int:
    run_started = dt.datetime.now().astimezone()
    inventory = inventory_project(root)
    manifest = load_json(project_paths(root)["manifest"], {})
    if not isinstance(manifest, dict):
        raise RuntimeError("test-manifest.json must contain a JSON object")
    execution_required = execution_gate_required(manifest)
    pre_run_mapping_issues, function_count = audit_coverage(root)
    command_results: list[dict[str, Any]] = []
    command_failures = run_manifest_commands(root, command_results)
    collector_result: dict[str, Any] | None = None
    execution_config = manifest.get("function_execution", {})
    collector_configured = isinstance(execution_config, dict) and execution_config.get("collector") is not None
    if collector_configured and command_failures:
        collector_result = {
            "status": "skipped",
            "cases_run": 0,
            "executed": 0,
            "details": "Skipped because one or more build/test commands failed; stale binaries are not valid execution evidence.",
            "evidence": None,
        }
    elif collector_configured:
        try:
            collected = collect_function_execution(root)
            if collected is None:
                raise RuntimeError("configured function execution collector did not run")
            collector_result = {
                "status": "passed",
                **collected,
                "details": "Collected per-test native coverage from the binaries produced by this run.",
                "evidence": None,
            }
        except (RuntimeError, OSError, subprocess.SubprocessError, ValueError) as exc:
            collector_evidence = record_failure(root, "function execution collector", str(exc))
            collector_result = {
                "status": "failed",
                "cases_run": 0,
                "executed": 0,
                "details": str(exc),
                "evidence": collector_evidence.relative_to(root).as_posix(),
            }

    if collector_result and collector_result.get("status") == "passed":
        mapping_issues, function_count = audit_coverage(root)
    else:
        mapping_issues = pre_run_mapping_issues
    execution_issues, execution_count, executed = audit_execution(root)
    collector_failed = bool(collector_result and collector_result.get("status") == "failed")
    function_gate_failed = (
        bool(mapping_issues)
        or (execution_required and bool(execution_issues))
        or collector_failed
    )
    mapping_summary = function_run_summary(root, mapping_issues)
    print(f"FUNCTIONS={function_count}")
    print(f"ADDED={len(inventory['added'])}")
    print(f"CHANGED={len(inventory['changed'])}")
    print(f"REMOVED={len(inventory['removed'])}")
    print(f"HISTORY_ARCHIVED={inventory['history_archived']}")
    print(f"STATE_PRUNED={inventory['state_pruned']}")
    print(f"EXECUTION_GATE={'REQUIRED' if execution_required else 'OPTIONAL'}")
    if function_gate_failed:
        print("FUNCTION_CHECK=FAIL")
    else:
        print("FUNCTION_CHECK=PASS")
    print_issue_samples(mapping_issues, "MAPPING")
    print_issue_samples(execution_issues, "EXECUTION")
    if collector_failed:
        print(f"COLLECTOR_FAILED: {collector_result.get('details', 'unknown collector failure')}")

    function_evidence: Path | None = None
    if function_gate_failed:
        # Manifest commands cannot self-certify mappings. Only a successful
        # configured collector can replace the pre-run mapping audit.
        evidence_sections: list[str] = []
        if mapping_issues:
            evidence_sections.append(issue_text(mapping_issues, "MAPPING"))
        if execution_required and execution_issues:
            evidence_sections.append(issue_text(execution_issues, "EXECUTION"))
        if collector_failed:
            evidence_sections.append(
                f"COLLECTOR_FAILED: {collector_result.get('details', 'unknown collector failure')}"
            )
        function_evidence = record_failure(
            root,
            "function verification gate",
            "\n\n".join(evidence_sections),
        )
        print(f"FUNCTION_EVIDENCE={function_evidence.relative_to(root).as_posix()}")
    report, summary = write_daily_report(
        root,
        inventory,
        mapping_issues,
        execution_issues,
        command_results,
        function_evidence,
        collector_result,
        run_started,
        execution_required,
    )
    for failure in command_failures:
        print(failure)
    evidence_count = sum(1 for result in command_results if result.get("evidence"))
    if function_evidence:
        evidence_count += 1
    if collector_result and collector_result.get("evidence"):
        evidence_count += 1
    print(f"TEST_COMMANDS_PASSED={summary['commands_passed']}")
    print(f"TEST_COMMANDS_FAILED={summary['commands_failed']}")
    print(f"TEST_CASES_MEASURED={collector_result.get('cases_run', 0) if collector_result else 0}")
    print(f"FUNCTION_MAPPINGS_CURRENT={summary['function_mappings_current']}")
    print(f"MAPPING_GAPS={summary['mapping_gaps']}")
    print(f"FUNCTIONS_EXECUTED={summary['functions_executed']}")
    print(f"EXECUTION_GAPS={summary['execution_gaps']}")
    print(f"FAILURE_EVIDENCE_FILES={evidence_count}")
    print(f"RUNTESTS_REPORT={report.relative_to(root).as_posix()}")
    if function_gate_failed or command_failures:
        print("RUNTESTS=FAIL")
        return 1
    print("RUNTESTS=PASS")
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    for name in ("init", "inventory", "compact", "check", "run"):
        subparser = subparsers.add_parser(name)
        subparser.add_argument("--project", default=".", help="Exact project root; defaults to the current directory")
        if name == "init":
            subparser.add_argument("--track-tests", action="store_true", help="Do not add /tests/ to the root .gitignore")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        root = resolve_project(args.project)
        if args.command == "init":
            return command_init(root, args.track_tests)
        if args.command == "inventory":
            return command_inventory(root)
        if args.command == "compact":
            return command_compact(root)
        if args.command == "check":
            return command_check(root)
        if args.command == "run":
            return command_run(root)
        raise RuntimeError(f"Unsupported command: {args.command}")
    except (RuntimeError, OSError, ValueError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
