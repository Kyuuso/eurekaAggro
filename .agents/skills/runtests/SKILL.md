---
name: runtests
description: Create, maintain, and run project-local regression tests and function inventories, then turn failures into timestamped Markdown breakdowns and source-backed fix gameplans. Use whenever implementing or changing a function, adding a feature, fixing or investigating a bug, writing or running unit or regression tests, reviewing test coverage, scaffolding a tests folder or test manifest, documenting current functions, recording an error and its regression test, triaging failed tests, planning retests, or when the user invokes $runtests or asks for a whole-project function check.
---

# Run Tests

## Purpose

Keep each behavior change tied to durable evidence. Inventory application functions, map every current function to regression coverage, prove that every function actually executed in an exact test case, run the project's real test tools, and turn failures into an actionable daily report without confusing static/build checks with runtime acceptance.

## Required order

1. Resolve the exact project root from the user's path; otherwise use the current directory. Do not scan sibling projects.
2. Read repository instructions and inspect existing test projects, runners, CI files, and `.gitignore` before changing anything.
3. Preserve existing test layouts and commands. Add the contract below around them; do not replace working framework-native tests.
4. Bootstrap missing test infrastructure with `scripts/runtests.py init --project <project-root>`.
5. Inventory before implementation. Use `inventory` and inspect the generated changes before editing production code. If legacy current-state artifacts are oversized or contain backup/rollback paths, back them up and run `compact` first.
6. For a bug, reproduce it with a failing regression test and add an error record before or with the fix.
7. For a feature or behavior change, add or update tests and the function-change record in the same implementation.
8. Update `coverage.json` only after the mapped tests exist with exact `file::selector` references. Copy the current function hash from `functions.json`; never mark stale coverage current without rerunning the mapped test. A mapping is traceability, not execution proof.
9. Run `scripts/runtests.py run --project <project-root>`. Also run any repository-specific build, lint, integration, or in-client gates that the manifest cannot express.
10. Open the emitted `RUNTESTS_REPORT` and every new failure-evidence file. Diagnose each current failure from source and tests; do not stop at raw runner output.
11. Replace the report's pending gameplan fields with the confirmed root cause or bounded hypothesis, affected files/functions, suggested solution, concrete Before/After change, risks, and targeted/full retest commands. If evidence is insufficient, name the exact missing evidence and how to obtain it.
12. Report function inventory, traceability, measured execution, individual test-case count, passed/failed test-command counts, files created, suggested next steps, and unrun runtime gates separately. Do not implement production fixes unless the user requested fixes or approves the proposed gameplan.

## Project contract

Use `<project-root>/tests/` as the project-owned test workspace. Record the actual project name in `test-manifest.json`; do not create a second literal `<project>.tests` directory when the project already has `tests/`.

Required paths:

```text
tests/
|-- errors/
|-- current functions/
|   |-- changes.md
|   |-- coverage.json
|   |-- execution.json
|   `-- functions.json
|-- function history/
|   `-- removed-functions-YYYY.jsonl
|-- README.md
`-- test-manifest.json
docs/
`-- runtests_MM_DD_YY-HH-MM-SS.md
```

Treat `functions.json`, `coverage.json`, and `execution.json` as current-state evidence only. Preserve meaningful removed mappings/execution evidence as streamable yearly JSONL under `tests/function history/`; discard generated missing/unexecuted scaffolding for excluded or removed functions. Keep generated `changes.md` deltas bounded to counts plus samples. Treat `changes.md`, regression tests, and error records as maintained project knowledge. Treat each `docs/runtests_MM_DD_YY-HH-MM-SS.md` as one run's timestamped dashboard and approval-ready fix gameplan. Never overwrite or repurpose an earlier run's report during a rerun. If that second is already occupied, advance the filename timestamp to the next unused second while retaining the exact local run-start time inside the report.

The bootstrap command adds an anchored `/tests/` entry to the project root `.gitignore` (equivalent uncommented entries are recognized, never duplicated), keeping the workspace local-only; if `tests/` already contains git-tracked files it preserves that tracked policy instead. Use `--track-tests` only when the user or repository explicitly requires versioned tests.

Read [references/test-contract.md](references/test-contract.md) before bootstrapping, documenting a bug, editing `coverage.json`, or deciding whether an existing test layout satisfies the contract.

## Commands

Resolve the skill directory from the loaded `SKILL.md`, then run its bundled script with Python 3:

```powershell
python -B "<skill-directory>\scripts\runtests.py" init --project "<project-root>"
python -B "<skill-directory>\scripts\runtests.py" inventory --project "<project-root>"
python -B "<skill-directory>\scripts\runtests.py" compact --project "<project-root>"
python -B "<skill-directory>\scripts\runtests.py" check --project "<project-root>"
python -B "<skill-directory>\scripts\runtests.py" run --project "<project-root>"
```

- `init`: create only missing contract files, detect native test commands, then run a first inventory; never overwrite existing files.
- `inventory`: prune excluded trees before descent; scan supported sources (Python via AST; C/C++, C#, Go, Java, JavaScript/TypeScript, PowerShell, Ruby, Rust via regex); hash complete function bodies with line ranges; archive meaningful removed evidence; compact current-state files; mark new coverage gaps; and append a bounded delta to `changes.md`. Requires the manifest from `init`.
- `compact`: run the same safe inventory/compaction transaction explicitly and print current, archived, and pruned counts. Back up unusually large legacy artifacts before the first compaction.
- `check`: refresh inventory, then fail on missing, stale, invalid, or nonexistent exact function-to-test mappings. It also audits measured execution and fails on its gaps when `function_execution.required` is true. It prints separate mapping/execution counts, `EXECUTION_GATE=REQUIRED|OPTIONAL`, and `FUNCTION_CHECK=PASS|FAIL`.
- `run`: refresh traceability, execute every enabled manifest command without a shell, collect configured per-test function execution after successful builds/tests, audit final mappings and execution evidence, write redacted failure evidence under `tests/errors/`, create `docs/runtests_MM_DD_YY-HH-MM-SS.md` using the local run-start time, and print count fields, `EXECUTION_GATE`, `RUNTESTS_REPORT=<path>`, and `RUNTESTS=PASS|FAIL`. A configured collector failure remains blocking even when execution evidence is optional.

Exit codes: 0 pass, 1 failed gate or test command, 2 invalid project or configuration (`ERROR:` on stderr). `--project` defaults to the current directory.

## Bounded output and scale safety

- Keep exact aggregate function, gap, command, and execution counts while printing only bounded issue samples. `MAPPING_ISSUES_OMITTED` and `EXECUTION_ISSUES_OMITTED` state how many additional diagnostics were suppressed.
- Spool each test command's merged stdout/stderr to a temporary file. Retain and print only a redacted 24 KB head/tail excerpt with the exact omitted-byte count, then discard the spool. Never copy a command's complete noisy output into memory, evidence, or the report.
- Allow at most 100 manifest commands and sample at most 25 passing and 25 failing command sections in a report. Preserve exact totals and explicit omitted-command counts.
- Refuse any active `functions.json`, `coverage.json`, or `execution.json` larger than 128 MiB before parsing or mutation. Back it up and stream/archive legacy entries outside active state before rerunning `compact`.
- Abort on invalid Python syntax, malformed manifest entries, or invalid boolean gate settings. Never silently omit an unparseable source file or malformed command.
- Prune default and manifest-excluded directories before source or command discovery. Preserve meaningful leading-dot names such as `.Research/**` when normalizing exclusion patterns.

Edit `tests/test-manifest.json` when auto-detection is incomplete. Store commands as argument arrays, keep working directories inside the project, and set bounded timeouts.

## Failure triage and reruns

The generated daily report is a deterministic starting point, not the finished diagnosis. After a failed run:

1. Reproduce or isolate each failing command with the smallest safe targeted invocation.
2. Trace the failure to the owning source and existing regression. Separate product defects, test defects, infrastructure/configuration failures, missing function coverage, and unavailable runtime acceptance.
3. Enrich the daily report. For every current failure, include:
   - observed and expected behavior;
   - confirmed root cause, or a bounded hypothesis plus required evidence;
   - affected stable function IDs and files;
   - suggested solution and why it addresses the cause;
   - concrete `Before` and `After` code or pseudocode excerpts;
   - safety/compatibility/data/runtime risks;
   - exact targeted retest and full `$runtests` rerun;
   - recommended priority and whether it is ready for user approval.
4. Preserve failing regressions. Never make a test pass by weakening its assertion or by marking coverage `tested` without a passing relevant test.
5. When the user approves fixes, implement only the approved items, run targeted tests first, then run the full workflow again. Enrich the newly emitted timestamped report so resolved failures are recorded in the new run while earlier reports remain unchanged history.

When the request is test-only, diagnosis and proposed changes are authorized; production changes are not. Leave solution proposals in the report for the user's decision.

## User-facing response contract

Lead with concrete counts, not an acceptance label. Use this shape:

```text
<passed> test commands passed; <failed> failed.
Function traceability: <mapped>/<total> current; <mapping-gaps> gaps.
Measured function execution: <executed>/<total> current; <execution-gaps> gaps.
Execution gate: <required or optional; full-verification impact>.
Report created or updated: <project-relative report path>.
Failure evidence created: <count and paths, or none>.
Next steps: <prioritized approval-ready actions and open runtime gates>.
```

If a native runner exposes individual test-case counts, include those separately from manifest command counts. Never use `not accepted`, `rejected`, or a bare `RUNTESTS=FAIL` as the headline or substitute for the counts, report path, diagnoses, and next steps. A zero-failure response should say that all automated test commands passed while still listing any unrun runtime gates.

`function_execution.required: false` makes missing execution evidence an optional automated gap: keep the exact counts visible, do not create failure evidence solely for that gap, and allow `FUNCTION_CHECK=PASS`/`RUNTESTS=PASS` when every required gate passes. It does not authorize a claim that the project is fully verified; measured execution remains part of the completion gate below.

## Change rules

- Require a regression test for every new or changed function and every fixed bug.
- Test public behavior plus failure, boundary, cancellation, cleanup, and state-transition paths relevant to the change.
- Prefer deterministic unit tests; add integration or runtime tests when behavior crosses process, network, UI, game, device, database, or filesystem boundaries.
- Prefer positive framework/native coverage ranges for execution proof. A native `FUNCTION_HIT <stable-function-id>` marker is a narrow fallback only when the configured collector independently reports that exact compiler function as `instrumentation_failure` or `has_external_branch`; emit it only after the exact test executes and asserts the behavior.
- Never use explicit hit markers to bypass an ordinary uncovered function, a missing test, a failing assertion, an unbuilt binary, or an unavailable runtime gate.
- Never weaken an assertion merely to make a failing test pass. Reconcile it with the intended behavior first.
- Never delete a failing historical regression test unless the behavior is intentionally retired and the reason is recorded.
- Preserve meaningful removed tested/executed evidence in yearly `tests/function history/removed-functions-YYYY.jsonl`; never retain removed entries in the active current-state JSON files. Explain intentional replacement or retirement in `changes.md`.
- Keep generated files, vendors, dependencies, build outputs, and test code out of the application-function inventory. Configure exclusions or manual functions in the manifest when discovery needs correction.
- Treat regex-discovered non-Python functions as a review aid. Manually compare the inventory with the changed source and add `manual_functions` for syntax the scanner missed.

## Completion gate

Do not call the project fully verified unless all of these are true:

- current function inventory was refreshed with complete source ranges;
- every in-scope function has a current `tested` mapping to an existing exact test selector;
- every in-scope function has a positive current execution hit tied to an exact test case;
- every enabled manifest command passed;
- the feature or error documentation names the behavior change and evidence;
- required integration/runtime acceptance was actually performed.

If an environment or runtime gate cannot run, state it as open. A passing `$runtests` function check is strong regression evidence, not proof of external systems or unexercised runtime behavior.
