# AGENTS.md - Development & Versioning Guidelines

This document provides mandatory guidelines and workflow instructions for AI coding agents and human contributors working on **Eureka Suite**.

---

## 1. Versioning System & Release Policy

### Semantic Versioning Format
Dalamud requires an assembly version format of **`MAJOR.MINOR.PATCH.REVISION`** (e.g., `1.0.0.0`), while Git tags use the standard semver **`vMAJOR.MINOR.PATCH`** (e.g., `v1.0.0`).

- **PATCH** (`1.0.0.0` -> `1.0.1.0` / `v1.0.1`): Bug fixes, aggro classification tweaks, threshold updates, or rendering optimizations.
- **MINOR** (`1.0.0.0` -> `1.1.0.0` / `v1.1.0`): New user-facing features (e.g., sound alerts, Bozja support, new overlay modes).
- **MAJOR** (`1.0.0.0` -> `2.0.0.0` / `v2.0.0`): Breaking architectural redesigns, major Dalamud API migrations (e.g. Dalamud 16+).

### MANDATORY RULE: Never Push Changes Without Bumping the Version
> **Why?** Dalamud's plugin installer (`/xlplugins`) compares the local `AssemblyVersion` against `pluginmaster.json`. If you push code without bumping the version, players already running the plugin will **never receive the update prompt**.

### How to Bump Version
Before committing any new feature or fix to `main`:
1. Run the bump script from PowerShell:
   ```powershell
   # For bugfixes or minor tweaks:
   .\scripts\bump-version.ps1 -Type patch

   # For new features:
   .\scripts\bump-version.ps1 -Type minor

   # For major rewrites:
   .\scripts\bump-version.ps1 -Type major
   ```
2. Verify that both [`EurekaSuite.json`](file:///c:/Users/shuns/Desktop/cosa2/eurekaAggro/EurekaSuite.json) (`"AssemblyVersion"`) and [`EurekaSuite.csproj`](file:///c:/Users/shuns/Desktop/cosa2/eurekaAggro/EurekaSuite.csproj) (`<Version>`) reflect the updated version number.
3. Commit with a descriptive conventional commit message (e.g., `fix: correct Pagos minotaur aggro (v1.0.1)`).

---

## 2. CI/CD & Automated Distribution Pipeline

- **Private Source Repository**: `https://github.com/Kyuuso/EurekaSuite`
- **Public Plugin Distribution Repository**: `https://github.com/Kyuuso/dalamud-plugins`
- **Dalamud Custom Repository Feed**:
  `https://raw.githubusercontent.com/Kyuuso/dalamud-plugins/main/pluginmaster.json`

### Automated Workflow
When changes are pushed to `main`:
1. `.github/workflows/build.yml` reads the version from `EurekaSuite.json`.
2. Downloads the matching Dalamud SDK and compiles in `Release` mode using .NET 10.
3. Creates a versioned GitHub Release (e.g. `Release v1.0.6` with tag `v1.0.6`) containing `EurekaSuite-v1.0.6.zip` (NEVER use `latest.zip` to prevent Fastly/GitHub CDN caching collisions).
4. Automatically commits the versioned `EurekaSuite-v1.0.6.zip`, `EurekaSuite.json`, and updates `pluginmaster.json` in `Kyuuso/dalamud-plugins` with direct versioned download URLs (`DownloadLinkInstall`, `DownloadLinkUpdate`, `DownloadLinkTesting`).

---

## 3. Language & Code Style Guidelines

- **MANDATORY English Code & Comments**: ALL identifiers, class names, member variables, local variables, parameters, XML documentation comments (`///`), inline comments (`//`), log messages, and UI strings MUST be in **English**. No Spanish or mixed language in source code. Code and comments must make technical sense and explain behavior accurately.
- **Documentation Standards & Anti-Slop**: Follow `.agents/rules/no_ai_slop.md`. Avoid emdashes (`—`/`–`), promotional buzzwords, and redundant comments that merely echo member names.
- **User Interactions**: Conversations with the repository owner in the chat interface must remain in **Spanish** unless requested otherwise.
- **Zero Allocations**: Avoid heap allocations in rendering loops (`OverlayRenderer.cs`) to prevent GC stalls and memory leaks during gameplay.
- **NO Unicode Emojis in UI or Chat**: NEVER use Unicode emojis in any ImGui strings, chat log messages (`ChatGui.Print`), toasts, notifications, or drawlists. Dalamud's ImGui font glyph atlas does NOT include standard Unicode emoji codepoints, which will inevitably render as broken characters (`=`, `?`, or missing glyphs). ALWAYS use `FontAwesomeIcon` via Dalamud's `ImGuiComponents.IconButton` / `FontAwesomeIcon.<Name>.ToIconString()`, or clean textual tags (e.g., `[OK]`, `[WARN]`, `[Combat]`, `[Key]`).
