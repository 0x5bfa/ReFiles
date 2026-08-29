---
name: windows-system-re
description: Reverse-engineer actual Windows system and inbox-app behavior from local artifacts under D:\reverse-engineered. Use whenever the user asks how Windows, Explorer, the Shell, or another Windows component really behaves, requests confirmation against the system implementation, or asks for an implementation comparison. Do not use for generic application behavior unrelated to Windows internals.
---

# Windows System Reverse Engineering

Use the local reverse-engineering corpus as the primary source of truth for Windows implementation questions.

## Workflow

1. Start in `D:\reverse-engineered`. Identify the smallest relevant component and artifact set with targeted directory listings and `rg --files`; do not dump large decompilations or generated trees.
2. Read any repository-specific instructions before inspecting artifacts. Record the binary/component name, architecture, and Windows build or file version when the corpus exposes them.
3. Search for concrete anchors from the observed behavior: exported or recovered symbols, UI strings, COM interfaces, Win32 APIs, property keys, GUIDs, message IDs, registry names, and nearby call sites.
4. Trace the value or control flow far enough to identify both the producer and the consumer. Read focused context around each match and follow callees only when they affect the conclusion.
5. When practical, corroborate the static evidence with a read-only probe against the installed Windows build. Treat a live probe as separate evidence because its version may differ from the corpus.
6. Compare the recovered path with the user's implementation at the API and data-representation boundaries. Distinguish metadata, raw values, formatted display values, sorting keys, and cached or persisted state where relevant.

## Evidence Rules

- Prefer decompiled code, symbols, manifests, resources, and read-only live observations over memory or analogy.
- Do not modify the reverse-engineering corpus or execute unknown recovered binaries unless the user explicitly requests it and the action is safe.
- Label conclusions as directly observed, inferred, or unconfirmed. Do not present symbol-name guesses or pseudocode types as authoritative source declarations.
- Cite local artifact paths and recovered function or symbol names. Include line numbers when stable text artifacts provide them.
- If artifacts are missing, stale, ambiguous, or from a different Windows build, state that limitation and give the strongest supported conclusion instead of filling gaps speculatively.
- Preserve raw values separately from display formatting when analyzing Shell property behavior; Explorer frequently uses folder-provided or Property System formatting that is not equivalent to `ToString()`.

## Result

Report the observed system behavior first, then the supporting path through the recovered implementation, the comparison with the user's code, and the confidence or remaining uncertainty. Make no product-code changes unless the user also asks for them.
