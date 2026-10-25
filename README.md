# SuyunCraft Launcher

A lightweight Minecraft Java Edition launcher for Windows, built on .NET Framework 4.8 with zero external dependencies. Supports vanilla and Fabric, offline profiles, dual download sources (official + BMCLAPI), version isolation, save-instance management, and log capture.

> Current version: 1.10 · 256 offline regression checks + 16 UI checks passing · single `csc.exe` build, no NuGet required.

## Feature Highlights

| Area | What you get |
| --- | --- |
| **Launch pipeline** | Staged preparation (metadata → mod preflight → Java selection → file verification → plan build) with per-stage timing, cancellable at every step, diagnostics written to `%APPDATA%/SuyunCraft/logs/` |
| **Downloads** | Official manifest + BMCLAPI mirror with automatic failover; concurrent (12-worker) hash-verified downloads; manifest cache for offline starts |
| **Fast launch** | Persistent verify cache: unchanged files skip full SHA-1 hashing — 9.3s → 0.88s on a 528MB / 4,677-file instance (10.6×). Layered tamper detection: length+mtime for small files, +4KB head sample for large ones; "Force full verification" button as the escape hatch |
| **Mod preflight** | Fabric `fabric.mod.json` parsing with typed version constraints (SemVer, `>= <= ~ ^`, wildcards, AND/OR); three-state verdicts (satisfied / unsatisfied / **unknown** — parse failures are never assumed compatible); `depends` / `breaks` / `conflicts`; nested jars as providers; loader override config |
| **Save instances** | One world per instance with independent mods / config / saves; create, import (copy-in), rename, **export to zip**, delete (with active-instance and running-game guards); instance locks prevent concurrent use |
| **Mod manager** | Import .jar files (multi-select), enable/disable with rollback, metadata diagnostics (id, version, loader markers) |
| **In-game AI assistant** | Optional Fabric mod (see `ai-mod/`) connecting to a local bridge; chat screen, blueprints, environment probe, local builder |
| **UI** | Custom-drawn dark theme (grass-block accents), DPI-aware (PerMonitorV2), page transitions, owner-drawn controls, no third-party UI libraries |

## Build & Verify

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1          # build SuyunCraft.exe
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Tests   # build + run offline regression
.\tests\RegressionTests.exe --integration                                # networked checks (optional)
.\tests\RegressionTests.exe --ui-smoke                                   # offscreen UI checks
```

No NuGet packages or third-party runtimes. The build script uses the C# compiler bundled with Windows (`csc.exe`), compiling everything in `src/*.cs`. Test sources in `tests/*.cs` build alongside. Networked tests download version metadata and Windows natives into `tests/artifacts` — never into your game directory. UI checks render the three pages offscreen to `tests/ui-*.png`.

## Source Layout

| File | Responsibility |
| --- | --- |
| `Program.cs` | Entry point, version string, CLI (`--dry-run`, `--manifest`) |
| `Configuration.cs` | Thread-safe JSON config load/save with clamping |
| `Network.cs` | HTTPS-only requests, bounded redirects, mirror mapping, manifest cache, multi-endpoint racing |
| `Downloads.cs` | Concurrent download manager: cancellation, retry, hash verification, atomic replace |
| `FileSafety.cs` | Path containment, reparse-point checks, safe ZIP extraction, file verification |
| `VerifyStore.cs` | Persistent verify cache with layered tamper detection and invalidation rules |
| `Minecraft.cs` | Version inheritance merging, dependency rules, asset verification, launch plan |
| `CommandLine.cs` | Windows argument parsing and quoting |
| `JavaRuntime.cs` | Java discovery, timed probing, version matching |
| `LaunchBackend.cs` | Launch preparation pipeline, config snapshots, staged diagnostics, preflight extension points |
| `ModSupport.cs` / `ModResolution.cs` | Bounded Fabric metadata reading; typed dependency resolution engine |
| `ModManagement.cs` | Mod import / enable / disable with rollback |
| `SaveIsolation.cs` | Save instances: create, import, rename, delete, export, locking |
| `MainForm*.cs` | UI: shell & async orchestration; `Home` / `Download` / `Settings` / `Instances` partials |
| `SaveInstanceForm.cs`, `ModManagerForm.cs`, `TextInputForm.cs` | Manager dialogs |
| `UiControls.cs`, `UiTheme.cs`, `ThemedControls.cs` | Custom-drawn widget library, theme, dark-mode helpers |
| `AiIntegration.cs` | Launcher side of the optional in-game AI bridge |
| `ai-mod/` | Fabric mod (Java) + local Python bridge; build with `build-mod.ps1` (requires Gradle + JDK) |

## Extension Points

- `ILaunchPreflightCheck.Inspect(context, token)` — add custom launch checks (Java/mod compatibility, modpack constraints). Keep `new ModPreflightCheck()` in the list when adding others.
- `IModMetadataReader.Read(ZipArchive)` — return `null` for unsupported formats; never execute mod code.
- `ILoaderDetector.Detect(metadata)` — recognize loaders by coordinates/main class; keep installation out of detectors.
- `selectJava` / `download` delegates — injectable for tests (no network, no Java processes).

## Known Boundaries

- Offline login only; Microsoft OAuth / Xbox Live / XSTS are not implemented.
- No Forge / NeoForge / OptiFine installation, modpack import, or Bedrock support (Forge/NeoForge mods are detected and reported as loader mismatches).
- `old_alpha` / `old_beta` versions are unsupported.
- Downloads resume at file granularity (no HTTP Range byte resume).
- The verify cache trusts length+mtime for files ≤32KB; head-samples larger files. A targeted equal-length, mtime-preserving tail tamper is only caught by "Force full verification" (documented trade-off, industry-standard behavior).
- Log4j compatibility flags are set, but that is not a full Log4Shell remediation for mod-provided libraries.
- Single-file download cap 1GB; JRE zip expansion cap 2GB / 100,000 entries.

## Verification Status

- 256 offline regression checks (backend, dependency resolution, verify store, save-instance management, mod manager, startup reliability) and 16 UI checks pass on this machine.
- Launcher-side integration with real network endpoints was verified historically (v1.1, 28 checks). The in-game AI mod builds via Gradle and is verified separately in `ai-mod/`.
- Actual gameplay (reaching the main menu) has been exercised manually but is not part of the automated suite.

## Backups & History

Pre-change snapshots live under `backup/before-*`. The audit trail is in `AUDIT-2026-10-21.md`; ongoing handoff notes in `GLM53-HANDOFF.md`; save-isolation design in `SAVE-ISOLATION.md`; mod manager notes in `MODS.md`.
