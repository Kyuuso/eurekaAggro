# PR Activity Review

Read this file when the user provides a PR review/comment URL or asks about
recent Dalamud activity.

## Procedure

1. Resolve review or comment URLs to the overall PR.
2. Record PR number, title, base/head branches, state, draft state, merge state,
   merge commit, dates, and changed files.
3. Check merged PRs since the old baseline date or tag.
4. Check open PRs that mention ClientStructs, schema, lifecycle, texture,
   client state, unlock state, plugin manager, windows, hooks, or dependencies.
5. Classify each PR:
   - `included-in-endpoint` -> runtime requirement;
   - `merged-post-endpoint` -> watch item;
   - `open`, `draft`, or `pending` -> watch item only;
   - `closed-unmerged` -> no-action history.
6. Prove runtime inclusion from the target compare range or tag. Merge status
   alone is not enough.
7. Cite PR evidence IDs in the review manifest and affected analysis.

## Durable-Link Rule

Use the overall PR, merged-PR search, repository, compare, or commit URL in
durable docs. Do not preserve review/comment anchors as long-term evidence.

## Public Reports

Summarize public behavior areas and common migration patterns. Omit local
paths, private plugin names, private snippets, and anchor-specific URLs.
