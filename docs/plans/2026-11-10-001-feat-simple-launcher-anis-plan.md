---
title: Restore direct model selection and add ANIS Trans
date: 2026-11-10
status: superseded
superseded_by: 2026-11-10-002-launcher-redesign.md
---

# Intent

This initial plan was paused at the user's request. The 2026-11-10 redesign in `2026-11-10-002-launcher-redesign.md` and root `DESIGN.md` supersedes its UI decisions. Existing local implementation changes are preserved as an unshipped draft.

The launcher should let a person select one of six built-in model choices, prepare missing files, load it, and copy its OpenAI-compatible connection details. Registration, model-family browsing and a separate Run tab must not stand between selection and starting. The user explicitly rejected the 0.2.0 information architecture and unstyled controls, including the preset editor, and requested external UX research and visible ANIS attribution.

# Research and design decisions

- Microsoft recommends a prominently displayed selection list for important choices and keeping the full set visible when space permits: https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/combo-box . Use a single native model list, six separate entries, no family variant dropdowns or installed-only filtering.
- Microsoft form guidance emphasizes labeled controls and deliberate grouping: https://learn.microsoft.com/en-us/windows/apps/design/controls/forms . Give presets a clear selector, prompt editor and two-column glossary. Place numeric generation options in an expander.
- Microsoft desktop UX guidance uses progressive disclosure for secondary options: https://learn.microsoft.com/en-us/windows/win32/uxguide/top-violations . Custom imports and advanced load settings remain available as secondary surfaces.
- LM Studio presents local serving as an explicit start/stop action: https://lmstudio.ai/docs/developer/core/server . Jan shows server connection information alongside serving configuration: https://docs.jan.ai/desktop/api-server . Preserve one stable public localhost API origin and one clear start/stop action.
- Adopt the pre-0.2 launcher’s restrained green/neutral palette, clear text hierarchy, generous consistent spacing and a distinct primary action. Retain native WinForms, keyboard navigation and system .NET; no web runtime, decorative animation or custom window chrome.

# Scope

1. Default screen: all bundled choices (existing five plus ANIS last), selected model description/requirements/credit, primary prepare/start/stop action, API address and model ID with copy actions. Do not auto-start, auto-download or register a model merely to make it discoverable. Existing saved selection and settings remain usable.
2. Custom GGUF/Hugging Face import, file reconnection and advanced options remain secondary. Presets retain their existing semantics for llama.cpp. Selection rollback, cancellation, settings migration and one owned server at a time remain intact.
3. Preset editor: selection and New in one header; editable name and large prompt area; readable source/target grid and Import; clear Save; destructive Delete separated; advanced numeric options collapsed by default. Commit active grid edits before save/navigation.
4. ANIS Trans: one built-in entry named `ANIS Trans (일본어 전용)` in Korean, description explicitly says Japanese ↔ Korean. Attribution `ANIS`, links to official model, source repository and license. Model/API aliases expose automatic, ja-ko and ko-ja directions.
5. ANIS uses the upstream API server only, launched hidden and owned by our launcher. No upstream GUI, CLI interaction or browser chat. Unsupported prompt/glossary and llama-specific generation controls are hidden for this model. Requests preserve source text and direction; no pretend glossary support.
6. Obtain ANIS model/program from official sources, preserve notices and never mirror upstream copyrighted files into our repository or release EXE. The upstream model is gated: first use links to official license/download and accepts a downloaded model folder. The launcher prepares its private runtime automatically. No account credentials, terms acceptance or commercial permission is assumed. Direct-download automation requires an authorized model-access flow rather than bypassing the gate.

# Implementation units

- U1: `LauncherViewModel.cs`, `LauncherViewModel.Library.cs`, `LauncherWorkspace.cs`, `MainWindow.cs`, `UiBindings.cs`, new native model list. Restore direct selection and a coherent main workspace. Tests: `tests/GemmaLauncher.UiTests/Program.cs`, `NativeUiTests.cs`; fresh/migrated settings show all choices, missing files lead to download/start, rejected selection reverts, stop/cancel remain reachable, narrow/high-DPI layout and every locale.
- U2: `PresetEditorView.cs`, secondary model tools, localized copy and README. Tests: actual glossary cell edits, switching/saving/deleting presets, advanced controls collapsed, keyboard access and readable screenshots.
- U3: catalog/config, explicit model engine/capability marker, installation/session routing and ANIS runtime bootstrap; preserve the existing llama.cpp path. Tests: `AnisTests.cs`, installer/session/gateway tests; complete/missing model folders, pinned integrity, process ownership, startup failures, cancellation, model aliases/attribution, API errors and port collision. Runtime dependencies only download when ANIS is used.
- U4: review source changes, run Release/Core/UI/Python/catalog/locale checks, render main/advanced/preset screens at desktop and small sizes on a private inactive Windows desktop, inspect screenshots, fix any clipping. Validate exact single EXE. Real ANIS translation requires user-authorized model access; distinguish this from fixtures and protocol tests.

# Execution notes

Work sequentially in the existing clean checkout on `codex/anis-trans`. Preserve the running user launcher and model servers. No subagents. Add regression assertions before changing behavior; do not keep tests that encode the rejected registration-first UX. Record source URLs and honest verification limits. No release labeled preview without user request.
