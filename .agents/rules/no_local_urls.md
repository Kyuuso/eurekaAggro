# Rule: No Local Machine URLs or File URIs

1. **Strict Ban on Local Absolute Paths in Repository Files**:
   - NEVER commit local machine file paths or URIs to tracked repository files (e.g. `file:///C:/Users/...`, `file:///c:/...`, or `C:\Users\<username>\...`).
   - Markdown links must ALWAYS use relative repository paths (e.g., `[EurekaSuite.json](EurekaSuite.json)`, `[build.yml](.github/workflows/build.yml)`) or simple code spans (e.g., `` `EurekaSuite.json` ``).
   - Scripts and configuration files must NEVER contain hardcoded user profiles, machine usernames, or temporary artifact paths. Use relative paths or script parameters instead.

2. **Why?**
   - Local paths leak user environment details into public git history.
   - Links pointing to local file URIs are broken and unclickable for any other collaborator or visitor on GitHub.
