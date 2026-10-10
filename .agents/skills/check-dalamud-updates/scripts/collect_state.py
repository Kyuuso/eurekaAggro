#!/usr/bin/env python3
"""Collect a deterministic, read-only Git delta for a local Dalamud clone."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


DEPENDENCY_NAMES = {
    "directory.build.props",
    "directory.packages.props",
    "nuget.config",
    "packages.lock.json",
}


class CollectionError(RuntimeError):
    """Raised when local Git evidence cannot be collected exactly."""


def _run_git(repo: Path, *args: str, allowed: tuple[int, ...] = (0,)) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(
        ["git", "-C", str(repo), *args],
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    if result.returncode not in allowed:
        detail = result.stderr.strip() or result.stdout.strip() or "unknown git error"
        raise CollectionError(f"git {' '.join(args)} failed ({result.returncode}): {detail}")
    return result


def _resolve(repo: Path, ref: str) -> dict[str, str]:
    commit = _run_git(repo, "rev-parse", "--verify", f"{ref}^{{commit}}").stdout.strip()
    observed = _run_git(repo, "show", "-s", "--format=%cI", commit).stdout.strip()
    subject = _run_git(repo, "show", "-s", "--format=%s", commit).stdout.strip()
    return {
        "ref": ref,
        "commit": commit,
        "commit_date": observed,
        "subject": subject,
    }


def _is_dependency_file(path: str) -> bool:
    normalized = path.replace("\\", "/")
    name = normalized.rsplit("/", 1)[-1].lower()
    return (
        name in DEPENDENCY_NAMES
        or name.endswith(".csproj")
        or name.endswith(".props")
        or name.endswith(".targets")
    )


def _normalize_checked_at(value: str | None) -> str:
    if value is None:
        return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    normalized = value.strip()
    candidate = normalized[:-1] + "+00:00" if normalized.endswith("Z") else normalized
    try:
        parsed = datetime.fromisoformat(candidate)
    except ValueError as exc:
        raise CollectionError(f"--checked-at is not valid ISO 8601: {value}") from exc
    if parsed.tzinfo is None or parsed.utcoffset() is None:
        raise CollectionError("--checked-at must include a timezone offset or Z")
    return parsed.isoformat().replace("+00:00", "Z")


def collect(repo: Path, old: str, new: str, checked_at: str | None = None) -> dict[str, Any]:
    """Return local Git evidence without fetching, checking out, or writing."""
    repo = repo.expanduser().resolve()
    if not repo.is_dir():
        raise CollectionError(f"repository directory does not exist: {repo}")

    inside = _run_git(repo, "rev-parse", "--is-inside-work-tree").stdout.strip().lower()
    if inside != "true":
        raise CollectionError(f"not a Git worktree: {repo}")

    baseline = _resolve(repo, old)
    endpoint = _resolve(repo, new)

    ancestor_result = _run_git(
        repo,
        "merge-base",
        "--is-ancestor",
        baseline["commit"],
        endpoint["commit"],
        allowed=(0, 1),
    )
    baseline_is_ancestor = ancestor_result.returncode == 0
    if not baseline_is_ancestor:
        raise CollectionError(
            "baseline is not an ancestor of endpoint; choose an exact old-to-new "
            "range instead of mixing rev-list and merge-base comparison semantics"
        )

    commit_count_text = _run_git(
        repo, "rev-list", "--count", f"{baseline['commit']}..{endpoint['commit']}"
    ).stdout.strip()
    changed_text = _run_git(
        repo, "diff", "--name-only", f"{baseline['commit']}...{endpoint['commit']}"
    ).stdout
    changed_files = [line.strip() for line in changed_text.splitlines() if line.strip()]

    origin = _run_git(repo, "remote", "get-url", "origin", allowed=(0, 2)).stdout.strip() or None
    timestamp = _normalize_checked_at(checked_at)

    return {
        "schema_version": 1,
        "collector": "collect_state.py",
        "collected_at": timestamp,
        "repository": {
            "path": str(repo),
            "origin": origin,
        },
        "baseline": baseline,
        "endpoint": endpoint,
        "delta": {
            "compare": f"{old}...{new}",
            "baseline_is_ancestor": baseline_is_ancestor,
            "commit_count": int(commit_count_text),
            "changed_file_count": len(changed_files),
            "changed_files": changed_files,
            "dependency_files": [path for path in changed_files if _is_dependency_file(path)],
        },
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Read a local Git worktree and print exact old-to-new commit and changed-file "
            "evidence. This command never fetches, checks out, pulls, or writes files."
        )
    )
    parser.add_argument("--repo", required=True, help="Path to a local Dalamud Git worktree")
    parser.add_argument("--old", required=True, help="Old baseline ref, tag, or commit")
    parser.add_argument("--new", required=True, help="New endpoint ref, tag, or commit")
    parser.add_argument(
        "--checked-at",
        help="Optional timezone-aware ISO 8601 observation time for reproducible output",
    )
    parser.add_argument("--pretty", action="store_true", help="Pretty-print JSON")
    args = parser.parse_args(argv)

    try:
        result = collect(Path(args.repo), args.old, args.new, args.checked_at)
    except CollectionError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2

    print(json.dumps(result, indent=2 if args.pretty else None, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
