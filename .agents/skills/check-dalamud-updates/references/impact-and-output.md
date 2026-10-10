# Impact and Public Output

## Impact scan

Inspect only the plugins requested by the user. Exclude generated output, unrelated dependency trees, backups, packaged releases, and unrelated repositories. Include dependency or vendor source compiled into the plugin when it consumes changed native contracts or constants; confirm its inclusion from the project inputs.

Check for overlap with the changed framework surfaces, including:

- SDK, packager, manifest API level, and dependency graph;
- service lifecycle and client-state APIs;
- direct ClientStructs and Lumina usage;
- addon, agent, UI, texture, input, and window APIs;
- hooks, signatures, scanners, and generated native bindings;
- chat and command APIs; and
- IPC contracts owned by updated dependencies.

Do not infer impact from the number of upstream changes. Cite the local symbol, package reference, or call site that creates the overlap. If source already uses the safer pattern, record it as already aligned.

Complete the breakdown for every scoped plugin before preparing the public/site release files. Each plugin needs a required, not-required or undetermined source-change decision, exact affected symbols and evidence, a concrete next action, and separate implementation/build/runtime states. Missing source or native proof is a gap, not a clean result. Keep optional improvements and runtime-only acceptance outside the required-repair count. Private source excerpts may support an internal checklist but must not enter the public report.

## Migration examples

When code guidance is useful, include:

- the affected public file or symbol;
- the old or search-for pattern;
- the safer current pattern;
- the upstream evidence that requires the change; and
- the validation still needed.

Do not reproduce unpublished code. Use bounded pseudocode when the underlying source is not public or the user has not authorized disclosure. An address candidate is not a complete native port: preserve game-version guards and require the matching binary, hashes, byte proofs and dependent contract evidence before a separate implementation workflow activates it.

## Publication check

A public report and its attachments must omit:

- local filesystem paths and user/profile names;
- private repository, plugin, customer, or host names;
- credentials, tokens, configuration values, and logs containing them;
- internal operator commands and release infrastructure details;
- unpublished source excerpts; and
- transient review-session identifiers or comment-anchor URLs.

Keep released runtime requirements separate from post-release and open-PR watch items. Label compilation, packaging, and live runtime evidence independently.

## Requested same-day revisions

When the user asks to recheck beta and update today's same files, preserve earlier raw observations and the exact prior report, checklist and manifest bytes in excluded local backups. Append fresh endpoint/dependency/source evidence with distinct IDs and timestamps; do not replace an earlier response with later bytes or imply that the earlier observation was wrong merely because rollout advanced.

Revise the existing findings, plugin reports, checklist run, public report filename, website slug and catalog row. Record the new observation/revision time separately from the original run start. Update affected checksums, byte counts and discovery text without duplicating the report or increasing archive/featured-card counts. Keep stable tag, requested/returned beta channel, applicable game contract, local installation and live runtime acceptance separate. A beta announcement does not prove delivery or supply missing native evidence.

## Wiki release handoff

For a release, hotfix, patch-day or supplied-notice review, or an explicit public-report request, unless the user requests internal-only work:

1. Add the report as a Markdown snapshot, or revise the existing snapshot for an explicitly requested same-day refresh after preserving its prior bytes. Preserve observation-time channel state even if rollout later advances. When the official tag is unchanged, use a dated hotfix or post-tag advisory with exact source endpoints and explicit delivery uncertainty; do not invent a new tagged release.
2. Synchronize exact bytes and SHA-256 in the release manifest and human card; update the archive, latest review pointer, counts, site manifest, wiki home, agent discovery files, and sitemap. Keep the latest stable tag separate from the newest advisory.
3. Keep the site's existing featured-card count and retain every report in the archive.
4. Update this curated public skill path-neutrally when its workflow changes, rebuild its archive and catalog metadata, and scan all public text and archive entries for local paths or private identifiers.
5. Run site regressions, JSON/XML validation, archive/hash checks, representative browser and local HTTP checks, and source-to-stage parity.
6. Preserve the previous staging tree and rebuild the clean publication tree from an explicit allow-list. Do not connect to FTP or upload without separate authorization.
7. Verify report, website copy, catalog/discovery updates, validation and handoff paths before completion. An unchanged runtime tag does not excuse missing public outputs. If the website root is unavailable, identify that incomplete stage and its exact next action.

A prepared staging tree is not a live public release. Report source-complete, tested, staged, and remotely published states independently.

## Exact upload handoff

Include the same explicit upload instructions in the handoff and final response after the scoped plugin breakdown and public/site preparation result. Name the local staging directory and remote document root, then show a fenced `text` tree containing every destination-relative file to upload. Show changed hidden files, including required `.htaccess` files. A directory link, wildcard or file count does not replace the tree. Upload stage contents to the document root; do not add the local staging directory as an extra URL level.

Compute added/changed paths from file bytes against an identified baseline. Prefer a verified published snapshot when available. If only the preceding local stage exists, label that baseline as local and publication-unverified; changed-only upload assumes those baseline bytes are already on the website. Keep this changed set separate from the complete staged inventory. When the published baseline is unknown, provide the full-stage tree as the complete upload option and state that the remote difference is unknown.

Keep private evidence, backups, validation records and operator instructions outside the public allow-list. Preserve the previous stage before rebuilding, include exact file inventories and hashes in the internal handoff, and confirm that the final tree matches verified staged bytes.

State remote-deletion status separately. List any baseline-only paths for review, distinguish them from verified stale public paths, and state whether deletion was performed. Missing files in a local comparison are not permission to delete remote content; do not use deletion-enabled mirroring without a separately authorized publication step.

Route an assigned project's required repairs to a separate implementation workflow with its current report and evidence. Revalidate that project's actual source and target dependency/runtime before editing it. The review workflow does not select a project, apply every proposal, relax a build restriction, or authorize publication.
