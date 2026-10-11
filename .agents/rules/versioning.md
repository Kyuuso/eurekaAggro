# Rule: Semantic Versioning Policy for Eureka Suite

1. Any modification that fixes a bug, updates mob aggro data, or enhances features MUST bump the version before pushing to `main`.
2. Format: `MAJOR.MINOR.PATCH.REVISION` (e.g. `1.0.1.0`).
3. Use the helper script: `powershell -File .\scripts\bump-version.ps1 -Type patch` (or `minor` / `major`).
4. Both `EurekaSuite.json` and `EurekaSuite.csproj` must stay in sync with the new version.
5. GitHub Actions will read `EurekaSuite.json` to publish the tagged release `vX.Y.Z` and update `pluginmaster.json` in `Kyuuso/dalamud-plugins`.
