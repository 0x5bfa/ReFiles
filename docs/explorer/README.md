# Windows Explorer implementation notes

This section documents Windows Explorer behavior that was established through binary analysis, runtime observation, and controlled API tracing. It is intended for contributors implementing Windows-compatible behavior in ReFiles.

> [!IMPORTANT]
> Internal symbols, resource identifiers, COM class layouts, and virtual addresses are implementation details, not supported Windows contracts. Always keep undocumented behavior behind a narrow Windows-only boundary and provide a safe fallback.

## Available investigations

| Area | Description |
| --- | --- |
| [Property sheets](property-sheets/README.md) | How Explorer discovers, constructs, displays, reads, and writes the General, Shortcut, Sharing, Security, Previous Versions, Customize, Digital Signatures, and Details pages. |
| [Drive property sheets](drive-property-sheets/README.md) | How Explorer constructs and populates the drive General, Tools, Hardware, Sharing, Security, Previous Versions, and Quota pages. |
| [Property-sheet window construction](property-sheets/construction.md) | The path from Shell page providers and `PROPSHEETPAGEW` to the `comctl32` sheet dialog, tab control, and individual page dialogs. |

## Evidence labels

The documents use the following labels so that verified behavior is not confused with a recommended replacement:

- **Verified** — observed in disassembly or decompiled control flow for the stated binary version.
- **Observed** — confirmed from the live dialog or runtime behavior.
- **Inferred** — derived from surrounding control flow, types, or call sites, but not proven at every layer.
- **Reimplementation** — a supported or isolated API boundary suitable for ReFiles; it may not be the exact internal Explorer call path.

## Version policy

Every address is relative to the module image base shown in the document and applies only to the analyzed binary. Function names obtained from public symbols are more durable than addresses, but neither is an API guarantee. Revalidate the chain when a Windows servicing update changes the relevant module.

## Safety and compatibility

Do not copy an undocumented COM vtable or native structure directly into presentation code. Interop code must own:

- COM apartment selection and message pumping;
- pointer and handle lifetime;
- allocator matching;
- cancellation and timeout behavior;
- version checks and fallback behavior;
- isolation from third-party in-process Shell extensions.

