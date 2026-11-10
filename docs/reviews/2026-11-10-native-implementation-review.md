# Native implementation review

Reviewed sequentially in the main thread, as required by AGENTS.md, against the accepted DESIGN.md and local diff from 66d9eca. Review covered session ownership, API compatibility, settings transactions, preset drafts, download authentication, pinned dependencies, failure recovery and native layouts.

## Findings corrected

- Model purpose was replaced by file readiness. The list now keeps the purpose subtitle and draws readiness separately.
- ANIS does not implement llama.cpp's health endpoint, and its model list advertises a different default alias. Readiness now probes the official root and requested alias endpoint; a child-process protocol fixture verifies this behavior.
- Load options could not be queued while running. They now persist separately from active launch options and apply on explicit restart. A failing native regression was observed before the fix and passed afterwards.
- Engine memory failures were generic startup failures. The bounded log tail now preserves an actionable memory cause; inability to read the diagnostic log cannot mask the original failure.
- Vulkan's ErrorOutOfDeviceMemory spelling needed its own regression; both it and ordinary OOM now select the memory recovery path. The official ANIS entry-point smoke also exposed cp949 output errors; explicit isolated Python UTF8 mode resolves them.
- Preset saves could retain unnormalized form values and ask to save again. Successful saves now reload normalized values; failed saves preserve the draft and persisted state.
- Actual native captures exposed a nearly collapsed glossary grid. The editor now reserves its action footer and scrolls the editing area independently on small windows. Narrow model rows retain E2B/E4B identifiers.

## Verification and boundaries

Core, native controller/control, Python, catalog, locale and package checks are recorded in the implementation log. Native screenshot inspection covers main, glossary, ANIS, settings, advanced options, small windows and manual layout scaling; manual scaling is not a physical monitor DPI test. The isolated rendering harness uses simulated file state; the separate first-download smoke performed real download and HY-MT2 inference.

ANIS source, private runtime and official first-access dialog were exercised. Full ANIS model download and Japanese/Korean inference still require the user's official approved account and token and were not claimed as passing. No credentials were obtained or stored. No upstream program or weights are redistributed in the EXE. Some new ANIS/recovery strings use English fallback outside Korean/Japanese/English; existing language choices are retained.

This review does not authorize a commit, push or public release.
