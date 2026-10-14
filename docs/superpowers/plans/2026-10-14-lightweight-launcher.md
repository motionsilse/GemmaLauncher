# Lightweight launcher implementation plan

**Goal:** Preserve the local 0.2.0-preview.2 feature set while removing WPF and bundled .NET, and stopping invisible UI telemetry.

**Architecture:** Native WinForms controls and an ApplicationContext own the window, tray and single-instance protocol. The existing controller, model services, gateway, settings and localization remain. Pages are created on demand. Server telemetry has one reusable reader while the Run page is visible, with asynchronous cleanup.

**Baseline:** Local `860170b` is the feature baseline. Public 0.1.4 lacks the local library/import/preset extensions; it is unsuitable as the replacement source. Preserve all existing untracked research files. Read CatRoute only as a reference. Work sequentially in this checkout; no remote changes or release publication.

## Constraints

- Keep .NET 10 and all 17 languages, existing settings and storage paths.
- Retain HTTP gateway, streaming/cancellation and preset semantics.
- Keep selection transactions, download verification, cancel/restart, local and Hugging Face import, advanced load options and MTP.
- Retain per-user single instance and legacy folder activation, tray restore and owned-process shutdown.
- Do not operate user windows, browser, model server or normal app instance. Test with temporary data, fake inference and a private Windows desktop.
- Report launcher and inference memory separately; no forced GC/working-set trimming.

## Implementation units

1. Preserve an immutable source snapshot under ignored `artifacts/lightweight/baseline`. Run existing tests and add a transitive-reference regression check that rejects WPF.
2. Replace Application/Dispatcher, WPF windows and XAML with WinForms lifecycle and lazy pages. Keep controller APIs and replace only UI services. Use property notifications to update controls without polling or rebuilding focused editors.
3. Add a visibility-aware telemetry owner; reuse ProcessMetrics for the same PID and release on hide, stop or disposal. Test in-flight reads, resume and stale results.
4. Port the existing executable UI harness, keeping controller assertions and adding actual control interaction, language refresh, lazy page creation, glossary commit, narrow/high-DPI layout and cleanup checks.
5. Change publishing to framework-dependent single EXE. Bundle launcher notices, verify catalog/locales/HTTP gateway and reject WPF in the exact package. Document .NET 10 Desktop and ASP.NET Core prerequisites.
6. Build and run Core/UI/Python suites; publish locally. Measure same Release builds, data and desktop conditions before/after for visible idle, hidden idle, visible simulated server and hidden simulated server. Record private bytes, working set, CPU time, I/O, graphics modules and file sizes. Clearly distinguish simulated inference from real-model validation.

## Review focus

- Selection callbacks during collection refresh must not select an unintended model or preset.
- Language updates must preserve edits, navigation and selected variants.
- Hidden/minimized pages must not leave PDH readers/timers alive or publish stale readings.
- Exit must await in-progress model operations and telemetry before disposing services.
- Native controls must remain usable at small work areas and higher DPI; glossary edits must reach the preset before Save or changing selection.

## Verification

`dotnet build GemmaLauncher.sln -c Release`; run both test executables (UI on isolated desktop); `python -m unittest discover -s tests/scripts`; `scripts/publish-launcher.ps1 -Version 0.2.0-preview.3`; inspect the exact package and measurement JSON. Save methodology, results and remaining limits in `docs/research/2026-10-14-lightweight-launcher.md`.
