# Native application implementation log

User authorized applying DESIGN.md to the real Windows app and testing it. Continue until the app, first-download flow, and appropriate tests are complete. No release requested in this turn. Work sequentially; no subagents. Preserve the user's running launcher and servers.

## Initial implementation snapshot (superseded by final verification below)

- Completed: native preset editing and applied selection are separate. New and duplicate presets are unsaved drafts. Save/apply, save without reassigning during navigation, discard, shared-use explanation, delete only affected references, atomic settings-save failure handling.
- Completed: actual PresetEditorView now has a full-width prompt/glossary editing area, collapsed generation options, save/apply and discard flow, shared-copy action. MainWindow configures native confirmation dialogs.
- Completed: LauncherDialog retains the current session's stop/cancel action independently of pending edits. Tested with a real native dialog on the private test desktop.
- Completed: RunOperationPanel is one shared native control moved into the main workspace or secondary-page footer. PrimaryAction and OperationProgress names remain unique.
- Completed: model-purpose subtitle is stable; readiness is a separate check, following user feedback about E2B showing “files ready” instead of its purpose.
- In progress: compact main workspace needs final visual alignment, settings and advanced options should move to owned dialogs, default selection E2B and six separate choices still need completion.
- Not implemented yet: ANIS backend, catalog sixth model, first-use authorized downloading, runtime bootstrap, engine/session routing and direction UI, dedicated recovery actions.
- Not completed: final full native/Core/Python/localization checks, real download from an empty data directory, final screenshots, standalone executable.

## Verified so far

- Release solution build passes after the latest changes.
- Added native failing regression before preset changes: new preset was registered before save. Fixed.
- Latest full isolated native suite passed, including new TestPresetDraftLifecycleAsync and TestDialogStopPreservesDraftAsync, before the recent RunOperationPanel/layout edits. Rerun after final work.
- Latest screenshot rendering succeeded before the small subtitle/readiness fix. Actual rendered screenshots are under artifacts/ux-redesign. Main still needs compact refinement and missing ui.preset.edit was subsequently localized.
- Added failing Core regression: catalog must contain six models with anis-trans last. Currently EXPECTED TO FAIL because the sixth entry is not implemented. Existing CatalogTests also still checks Length==5 and must be updated deliberately.

## User's test expectation

User rejected asking for an already-downloaded ANIS folder: “없지. 당연히 내가 지금 눌러서 받아지는거부터 테스트가 시작인데”. Test from a genuinely empty model directory through the app's download button. Do not make predownloaded model files a prerequisite. ANIS gated access must be handled inside the first-use flow; never accept the upstream license or assume credentials on the user's behalf.

## External research saved outside repo

Read-only official source files: C:/Users/fox/.gstack/projects/GemmaLauncher/anis-research/{server.py,engine.py,pyproject.toml,official-source.zip}. Do NOT vendor upstream code or models into our source/EXE.

ANIS source commit 1165f0cda25a9afdd0c8e0491038e949d9446e6e. Official zip https://codeload.github.com/jintigers/anis-trans/zip/1165f0cda25a9afdd0c8e0491038e949d9446e6e ; bytes 71071; SHA256 ef664aa5d77ff53bb8c0d57c2d4188b474c86ace0f61a6c5d4ea88c1027b19f9.

HF model commit c99c0367790cb82e76bb4edc948b5d6353e29454, gated:auto. Model manifest: see previous tool output or public /api/models/jintigers/anis-trans?blobs=true. model.bin bytes300265575 SHA25607212fcad6655f632de0345713981d8e171d8891b71039289125d79c3ae8bcde; tokenizer.model bytes275719 SHA25632a11bc0c1c45a477d4a4cdbc0dbc04935063b849e25a7c5d94f1294a1e3a15c. Small files have immutable Git blob IDs, usable to verify downloaded content with SHA1(blob header + content).

uv0.13.7 Windows x64 official ZIP: https://github.com/astral-sh/uv/releases/download/0.13.7/uv-x86_64-pc-windows-msvc.zip ; bytes15236317 SHA2560cdc5b38fe5a8168ed6a25814f5ba43c21d313dd3187314fccb37b7cec46e021.

Verified PyPI cp312-win_amd64 wheels to lock for a private Python3.12 environment:
- ctranslate2==4.8.5 sha256:16b1478f9dccd3c6eedb7a1b2e23cb2a41f03976266b71afa1bd9019df7c7014
- sentencepiece==0.2.3 sha256:972af959c63cde446210a8935524806820c253afa4b0a97d83c206b857085f44
- numpy==2.4.3 sha256:65f3c2455188f09678355f5cae1f959a06b778bc66d535da07bf2ef20cd319d5
- PyYAML==6.0.3 sha256:5fcd34e47f6e0b794d17de1b4ff496c00986e1c83f7ab2fb8fcfe9616ff7477b

Official ANIS server can run python -m anis_trans.server, args --model ROOT --host127.0.0.1 --port PORT --device auto|cpu|cuda. Engine auto falls back to CPU if CUDA initialization fails. No /health endpoint; GET / and /v1/models available. owned_by is anis-trans already. Model aliases anis-trans:auto, :ja-ko, :ko-ja. Source root must be importable from its downloaded src/ directory (use an isolated Python bootstrap to insert exactly that path). One launcher-owned process/job; reuse ServerSession lifecycle and gateway, suppress LLM preset transforms for ANIS. Existing ServerSession readiness currently requires /health and checks configured alias, so branch for ANIS.

## Known follow-up cleanup

- New locale keys ui.preset.* added in 17 locale files. ms/pl currently received English text for those keys and need proper translation; ui.connection.copyValue also English outside ko/ja/zh.
- Existing native tests configured Fixture.ResolvePresetEdits=>Save and ConfirmPresetDeletion=>true to explicitly model old scenarios; production MainWindow supplies native dialogs.
- Current NativeUiTests now select PresetEditorTabs glossary page before real cell edits and return to page2 after Save/apply navigation. Keep this behavior.
- No Browser tools: local browser use is disabled in Work settings. Native isolated UI runner and DrawToBitmap can validate app UI without touching the user's desktop.

## Later implementation update (same task, supersedes initial progress above)

- ANIS backend is now implemented in Core/AnisBackend.cs. Its model/source downloads are pinned and verified; temporary read tokens are never stored; official source is downloaded rather than vendored. Private uv/Python/dependencies were ACTUALLY downloaded and successfully probed in artifacts/anis-runtime-smoke. Ready receipt was subsequently renamed ready.receipt, and repair/cancellation logic hardened; rerun runtime smoke once after final changes.
- Catalog contains six models; anis-trans is last. UI ordering now uses familyOrder to put HY before 12B and keeps custom models after built-ins. DisplayName for ANIS is localized; E2B subtitle always describes its purpose, readiness is a separate check.
- Actual FIRST DOWNLOAD test PASSED from an empty directory through MainWindow's real PrimaryAction button: downloaded runtime and HY-MT2 1.8B (1.13GB), ran CPU inference through the gateway, received “선장이 도착했습니다. 문을 열어주세요.”, stopped and cleaned up. Then selected ANIS with no files, clicked download, hit the actual official access restriction, displayed native AnisDownloadToken dialog, and cancelled cleanly. No account credentials or license acceptance were assumed.
- Evidence: artifacts/first-download-20261110-172526-90a975/{running.png,first-translation.json,anis-first-access.png}, artifacts/first-download-smoke.log. Test process exited0. Existing log Korean output was garbled due to console encoding; Program.Main now sets UTF8. Actual JSON response is correct UTF8. Do not rerun full model download unnecessarily; cached private test files remain for verification.
- Latest isolated native full suite passed including TestRecoveryRoutingAsync before final small validation/compact-layout changes. Last session70495 completed0. Need final rerun.
- Real official ANIS API does NOT implement /health and /v1/models lists latest rather than auto. Fixed readiness to use GET / plus GET /v1/models/{requested alias}. Added offline child-process fixture with identical behavior; first regression failed before fix, session tests passed after fix.
- Main UI now embeds one shared RunOperationPanel in main detail area and moves it to global footer on secondary pages or small work areas (UseCompactLayout). Header has Settings; add-model action is in catalog footer. AppSettingsView contains language and CloseToTray. Advanced options use LauncherDialog and maintain stop control, with controls reparented back before dialog disposal.
- Typed failure recovery now handles permission, memory, port, storage, reconnect and integrity paths; source log tail recognizes actual engine OOM. New memory-failure/session and native recovery tests must be included in final full run.
- Native preset validation now focuses invalid field/tab and uses ErrorProvider, while preserving drafts. Successful saves normalize trimmed name/numeric strings to avoid a repeated save prompt.
- New app files: LauncherViewModel.Presets.cs, LauncherViewModel.Anis.cs, LauncherViewModel.Recovery.cs, LauncherDialogs.cs, RunOperationPanel.cs, AppSettingsView.cs. Core models add Engine/defaultllama, Anis launch-source/device properties; Settings adds anis folder/direction/device and CloseToTray.
- Remaining: final screenshots/render inspection (normal, glossary, ANIS, owned options, small/scaled windows), complete Core/UI/Python/catalog/locale runs, reliability/security review sequentially, README/THIRD-PARTY updates, standalone EXE through publish-launcher.ps1 (default v0.2.0 output exists: use a NEW version e.g.0.3.0; never overwrite old release). No commit/push/release performed.

## Final verification · 2026-11-10

Applied the accepted design to the real WinForms application and built local version 0.3.0. Six built-in models are independently visible before installation, with ANIS last. E2B keeps its purpose subtitle; file readiness uses a separate check. Settings and advanced options use owned dialogs. Preset drafts, save/apply, shared-copy and deletion behavior are implemented. The preset action footer stays visible while the editing area scrolls on small windows. Final captures are in artifacts/ux-redesign, including main.png, glossary.png, anis.png, settings.png, load-options.png, compact.png and scaled-layout.png.

Verification completed:

- Release solution build: zero warnings and errors.
- Full Core suite: Hugging Face, gateway, ANIS, library, installation, session lifecycle, catalog/settings, process metrics, prerequisite and localization tests passed. Added Vulkan ErrorOutOfDeviceMemory fixture failed before the fix and passed afterwards, alongside ordinary OOM handling.
- Full isolated native suite: passed, including glossary viewport usability, pending load options, preset transactions, dialog stop/cancel, recovery routing, controls, language/layout and telemetry visibility. Final log: artifacts/native-completion-suite.log.
- Python helper suite: 5 tests passed.
- Catalog generation check: passed; six models match registrations.
- Locale structure/placeholders: 17 languages, 3 domains, 364 keys per language passed. Malay and Polish preset translations were completed; some new ANIS/recovery text still uses English outside ko/ja/en.
- Real first-download smoke: empty private data directory → actual download button → HY-MT2 1.8B/runtime download → CPU API translation → clean stop, followed by the actual ANIS first-access dialog and clean cancellation. Evidence paths above remain intact.
- Actual ANIS private runtime preparation passed again. Launching the downloaded official API entry point with the production bootstrap and --help passed. This exposed and fixed cp949 output failures by enabling isolated Python UTF8 mode explicitly.
- Published the exact final single EXE through scripts/publish-launcher.ps1; its own empty-directory verification passed with six models, 17 languages, embedded notices and the gateway check. Output: dist/GemmaLauncher-0.3.0-win-x64.exe (1,135,753 bytes); SHA-256 e69f98afb0968f34aa2090b9665c944c14573f513c03ffb5851efeeaf03281ca. Checksum file is alongside it. Verification report: dist/.publish-15b82d91e8dd4b3a97c0ccb7fa7eb013/verification.json.
- Sequential correctness/reliability/API/security/native-layout review recorded in docs/reviews/2026-11-10-native-implementation-review.md. Temporary layout dumps were removed after preserving final PNG evidence; remaining dump count is zero. Prior 0.2.0 release was preserved.

Limits: gated ANIS weight download and actual Japanese/Korean inference require the user's officially approved account/token and remain unverified. The runtime, upstream entry point, API readiness protocol and first-use access path were verified separately. Native screenshots use the isolated test desktop; manual layout scaling is not a physical monitor DPI test. No public release, commit or push was performed. An already-running older launcher must be exited from its tray before opening the new EXE because the launcher deliberately permits one instance per Windows user.

## Navigation copy follow-up · 2026-11-10

User correctly identified that the top-left “실행” button on Add model sounds like starting a server. Its handler only returns to model selection. Replaced the legacy Run label with a dedicated localized “← 모델 선택으로” navigation label, used on both secondary screens, in all 17 languages. Release build passed with zero warnings/errors, locale validation passed with 365 keys, and native rendering confirmed the Add model header. Rebuilt and verified the local 0.3.0 EXE. Latest checksum supersedes the earlier one above: 622aa9f36fd597fce30b0a4d1fda6cd70b5b76573223f006a53b869b8856afd3. Verification report: dist/.publish-82ec07f011084ad6ae8918e5f160dfa2/verification.json. The prior task-owned build and checksum were preserved under artifacts/superseded-build-0.3.0-before-navigation-copy.

## Whole-app button and copy audit · 2026-11-10

Completed the requested review across reachable main/add-model/preset/settings/advanced/ANIS/recovery/tray actions. Findings and corrections are in docs/reviews/2026-11-10-ui-copy-audit.md. Changes clarify navigation, closing versus cancellation, automatic saving, save versus apply, inactive/shared presets, actual download/start behavior and folder-opening actions. Corrected misleading non-server error recovery, disabled model selection without a target, displayed load-option validation inside its dialog, fixed clipped confirmation text, and made disabled primary buttons visually distinct. New preset edits clear stale save notifications. Updated copy in all 17 locales; 380 keys per language now pass structure and placeholder checks.

Final evidence for this follow-up:

- Release solution build: 0 warnings, 0 errors.
- Full isolated native suite passed: artifacts/wording-audit-tests-final.log. This includes the import-recovery regression observed failing before the fix, empty target controls, option rejection feedback, draft lifecycle, sharing/deletion, restart/cancellation, language/layout and telemetry checks.
- Native render and dialog-state checks passed: artifacts/wording-audit-render-final.log. Additional captures include settings-exit.png, preset-inactive.png, preset-shared-compact.png, unsaved-dialog.png, delete-dialog.png, import-error.png and load-options-validation.png under artifacts/ux-redesign. Inspected actual HWND captures; no browser automation or user windows were used.
- Localization tests and shipping catalog checks passed. No new model-weight download or inference was required for this UI follow-up.
- Rebuilt the final local 0.3.0 EXE through the package script; exact-EXE verification passed. Latest SHA-256 supersedes prior local builds: e8ae5201da45012e51e516d97961496acb294b303e0ce47bb3d1e15b3bc09057. Report: dist/.publish-17c3447c6c7642e490e3397acabc3d77/verification.json. Prior task-owned package and checksum are preserved in artifacts/superseded-build-0.3.0-before-wording-audit. No public release, commit or push was performed.
