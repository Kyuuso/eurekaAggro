# Project test contract

Read this reference when creating or repairing a project's `tests/` workspace, mapping function coverage, or documenting an error.

## Contents

- Folder meanings and Git policy
- Coverage mappings and function changes
- Error records and daily reports
- Manifest schema
- Inventory scanner

## Folder meanings

- `tests/README.md`: project-specific entry point, commands, scope, environments, and open runtime gates.
- `tests/errors/`: one Markdown record per reproduced bug or failed automated run. Keep the symptom, cause, regression test, fix, and verification together.
- `tests/current functions/functions.json`: generated current function inventory. Do not hand-edit it.
- `tests/current functions/coverage.json`: maintained mapping from current source hashes to regression tests; current functions only.
- `tests/current functions/execution.json`: generated exact-test runtime evidence with current source hashes, positive hits, and evidence kinds; current functions only.
- `tests/current functions/changes.md`: append-only behavioral history for added, changed, and removed functions. Generated inventory deltas are a starting point; add intent and observable before/after behavior.
- `tests/function history/removed-functions-YYYY.jsonl`: streamable yearly history for meaningful removed tested/executed evidence. Generated missing/unexecuted scaffolding is not historical knowledge and is pruned.
- `tests/test-manifest.json`: native runner commands and inventory configuration.
- `docs/runtests_MM_DD_YY-HH-MM-SS.md`: one run's local timestamp, counts, failure breakdown, source-backed solution gameplan, evidence index, and retest sequence.

The required folder is literally `tests/` inside each project. The manifest's `project_name` distinguishes projects.

## Git policy

For a newly created local test workspace, add anchored `/tests/` to the project root `.gitignore`; an existing uncommented `tests`, `/tests`, or `tests/` entry already counts as ignored. Do not add it when the repository already tracks files under `tests/`; changing tracked test policy requires explicit user approval. Ignoring the folder makes its history machine-local, so call that out if the repository needs tests shared through Git or CI.

Ignore transient framework caches and outputs even when tests are tracked. Never place credentials, production data, or unredacted user data in tests or error records.

## Coverage mapping

Each current function entry in `coverage.json` must have:

```json
{
  "source_sha256": "hash copied from functions.json after the test passes",
  "status": "tested",
  "tests": [
    "tests/test_example.py::ExampleTests.test_expected_behavior"
  ],
  "notes": "What behavior and edge cases this mapping covers."
}
```

Use project-relative paths and require an exact selector after `::`; the file part must be an existing file under `tests/`, and the selector must resolve to a real test case. Use multiple test references when unit and integration coverage are separate. Existing selectorless entries are migration debt and fail the current mapping gate rather than silently counting as coverage.

Only `tested` satisfies the gate. New functions start as `missing`; a changed function remains stale until its stored hash is updated after a successful relevant test. Removed functions leave active `coverage.json`; meaningful tested mappings are copied to yearly JSONL history first. A reappearing function starts `missing` and must be retested. Re-inventory never overwrites another current function's maintained coverage entry.

Coverage mapping is traceability, not measured execution. The mapping gate and execution audit are separate. Both are required before a full-verification claim; the manifest may make execution optional only for the automated `check`/`run` exit gate.

## Measured function execution

Each current function entry in `execution.json` must have:

```json
{
  "source_sha256": "current hash copied from functions.json by the collector",
  "status": "executed",
  "hits": 1,
  "tests": [
    "tests/UnitTests.cpp::exact test case name"
  ],
  "collector": "msvc-code-coverage",
  "evidence_kinds": ["msvc-covered-range"]
}
```

Only a positive current hit tied to an existing exact test selector satisfies the execution gate. Per-test collection is required: aggregate suite coverage cannot identify which regression executed the function. The collector refreshes `coverage.json` only for functions it proved executed. Removed positive execution evidence moves to yearly JSONL history; active `execution.json` remains current-only.

When `function_execution.required` is false, missing or stale execution evidence remains visible as an optional gap and is written into the run report, but it does not by itself make `FUNCTION_CHECK` or `RUNTESTS` fail and does not create failure evidence. A configured collector failure is still blocking because a requested measurement did not complete. Optional execution never satisfies the full-verification contract.

MSVC may report a native function as `instrumentation_failure` or `has_external_branch` even when an exact test calls it. A manifest may opt into `allow_explicit_hit_markers`; then that exact test may emit `FUNCTION_HIT <stable-function-id>` after it executes and asserts the behavior. The collector accepts the marker only when the same per-test XML names the compiler function with one of those two exclusion reasons. Markers for ordinary uncovered functions, unknown IDs, or other exclusion reasons fail closed.

## Function change entry

Add a human-authored entry after the generated inventory delta:

```markdown
## YYYY-MM-DD - Short change name

- Functions: `relative/path.ext::FunctionName`
- Before: Observable old behavior or limitation.
- After: Observable new behavior.
- Why: Requirement, defect, or safety reason.
- Tests: Exact test files/cases and important boundaries.
- Runtime gates: Passed gates and explicitly open gates.
```

Describe behavior, not only filenames or implementation mechanics.

Generated delta rows are capped at 50 stable IDs per Added/Changed/Removed category and include the total count. Do not expand a large generated delta back into a single unbounded Markdown line.

## Error record

Create `tests/errors/YYYY-MM-DD-HHMMSS-short-error-name.md`:

```markdown
# Short error name

- Observed: Exact symptom and context.
- Expected: Intended behavior.
- Root cause: Confirmed cause, or `Unconfirmed` while investigating.
- Affected functions: Stable IDs from `functions.json`.
- Regression test: Test path and case that fails before the fix.
- Fix: What changed and why it prevents recurrence.
- Verification: Commands and results after the fix.
- Runtime gates: Passed and open external checks.
```

Do not record secrets or full sensitive logs. A generated command-failure record (its filename uses a UTC timestamp plus microseconds) redacts common credential patterns and keeps a bounded head/tail excerpt with an explicit omission count while preserving command and exit metadata. It is evidence, not a root-cause analysis — enrich it after diagnosis.

## Timestamped run report

Every full `run` creates a new project-local report named from the machine's local run-start time, for example `docs/runtests_08_04_26-18-05-07.md`. The format is exactly `runtests_MM_DD_YY-HH-MM-SS.md` using a 24-hour clock. Do not overwrite an earlier report during a rerun. If that exact second already exists, advance the filename to the next unused second and retain the actual local run-start time in the report body. Timestamped reports preserve the sequence of failures, approved fixes, and retest outcomes while `tests/errors/*.md` preserves the raw failure evidence.

The generated report must contain:

- manifest test-command pass/fail counts;
- current function-mapping tested/gap counts;
- current measured function-execution hit/gap counts and individual test cases measured;
- inventory added/changed/removed counts;
- bounded samples of failed commands with argv, exit status, redacted head/tail excerpt, evidence path, and an exact omitted-command count when needed;
- the coverage-gate issues and evidence path;
- passing command names;
- files created or updated;
- an investigation/gameplan section with root cause, solution, Before, After, risks, targeted retest, and full rerun fields;
- next steps and an explicit reminder that unrun runtime acceptance remains open.

The script supplies deterministic facts and pending gameplan fields. The agent must then inspect current source/tests and replace each pending field with a source-backed diagnosis and approval-ready proposal. If the cause cannot be confirmed, record the leading bounded hypothesis, what was ruled out, the exact evidence still needed, and the safest next diagnostic action.

Do not use `not accepted`, `rejected`, or a bare pass/fail label as the user-facing summary. State counts, report/evidence paths, failure names, proposed actions, and open runtime gates. Keep manifest command counts separate from framework-reported individual test-case counts.

## Manifest schema

Keep version `1` and use this shape:

```json
{
  "version": 1,
  "project_name": "Example",
  "commands": [
    {
      "name": "python-unittest",
      "argv": ["python", "-B", "-m", "unittest", "discover", "-s", "tests", "-p", "test_*.py", "-v"],
      "cwd": ".",
      "timeout_seconds": 300,
      "enabled": true
    }
  ],
  "inventory": {
    "exclude": ["vendor/**", "generated/**"],
    "manual_functions": []
  },
  "function_execution": {
    "required": true,
    "evidence_file": "tests/current functions/execution.json",
    "collector": {
      "kind": "msvc-code-coverage",
      "timeout_seconds": 300,
      "allow_explicit_hit_markers": false,
      "test_targets": [
        {
          "test_file": "tests/UnitTests.cpp",
          "executable": "build/vs2022-debug/Debug/project_unit_tests.exe"
        }
      ]
    }
  }
}
```

Never store a shell command string. Use `argv` so the runner can execute without a shell. `cwd` must be an existing directory inside the project; `timeout_seconds` must be 1-86400 (default 300); `enabled` defaults to true and must be Boolean; `function_execution.required` defaults to true and must be Boolean. A run with zero enabled commands fails with `NO_TEST_COMMANDS`, malformed command entries fail configuration, and a manifest may contain at most 100 commands. The runner replaces a leading `python`/`python3`/`py` with its own interpreter and, on Windows, resolves any bare command name through PATH to its `.exe`/`.cmd` shim. Add a command for each test surface in a polyglot application.

The MSVC collector requires each configured test executable to support `--list` with one `TEST <exact name>` line per case and positional execution of one exact name, returning failure when that name does not exist. Build/test commands run first so stale binaries never become current execution evidence.

## Inventory scanner

- Python (`.py`) is parsed by AST with qualified names (`Class.method`, `outer.nested`); a syntax error aborts inventory with the affected file and location instead of silently omitting it. Line-anchored regexes cover C (`.c`), C++ (`.cc .cpp .cxx .h .hh .hpp`), C# (`.cs`), Go (`.go`), Java (`.java`), JavaScript (`.js .jsx .mjs`), PowerShell (`.ps1 .psm1`), Ruby (`.rb`), Rust (`.rs`), TypeScript (`.ts .tsx`).
- Function IDs are `relative/path.ext::Name`; repeated names within a file — including duplicate Python definitions such as property setter pairs, overloads, or redefinitions — become `Name#2`, `Name#3`.
- Always pruned before descent: directories named `.git`, `.venv`, `.rollback`, `__pycache__`, `backups`, `bin`, `build`, `coverage`, `dist`, `generated`, `node_modules`, `obj`, `packages`, `target`, `tests`, `third_party`, `vendor`, `venv`, common IDE folders, and files matching `*.designer.cs`, `*.g.cs`, `*.generated.cs`, `*.min.js`.
- `inventory.exclude` holds glob patterns matched against project-relative POSIX paths (e.g. `"src/legacy/**"` or `".Research/**"`). A leading `**/` also matches the same directory at project root. Meaningful leading dots are preserved, and excluded directories are pruned before source or auto-detected test-command discovery visits descendants.
- Each discovered function records inclusive `line` and `end_line` values and hashes its complete body; multiline signatures must not collapse to a one-line or empty hash.
- Each `manual_functions` entry requires `id`, `path`, and `name` (`language`, `line`, and `end_line` optional); `path` must be an existing file inside the project and is hashed whole-file. Duplicate function IDs abort the scan, so keep manual IDs unique.

## Output and scale limits

- Function audits retain exact gap counts while holding and printing at most 40 representative issue strings per mapping/execution surface. Omission markers carry exact suppressed-issue counts.
- Manifest commands write merged stdout/stderr to a temporary spool. Console, failure evidence, and reports retain a redacted 24 KB head/tail excerpt with the exact omitted-byte count; the spool is deleted after the command.
- Reports show at most 25 passing and 25 failing command sections while preserving aggregate totals and omitted-command counts.
- Current JSON inventory/coverage/execution files still require memory proportional to current state. The runner refuses any one of these active files above 128 MiB before parsing or mutation. Back it up and stream/archive legacy entries outside active state before rerunning `compact`; do not treat output bounds as proof that arbitrary multi-gigabyte legacy JSON can be loaded safely.
