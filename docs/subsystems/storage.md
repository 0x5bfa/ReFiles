# Storage subsystem

The storage subsystem represents files, folders, references, identity, hierarchy, and provider-backed access without requiring presentation code to understand a particular backend.

## Responsibilities

- represent storage items and locations in UI-independent models/contracts;
- provide stable identity suitable for selection and change reconciliation;
- resolve references into usable models;
- enumerate hierarchy progressively;
- expose optional behavior through item capabilities;
- define ownership for streams/resources returned by providers.

## Non-responsibilities

Storage must not own:

- XAML or ViewModels;
- localized display formatting;
- visual selection or viewport state;
- WinUI image objects;
- provider-specific UI.

## Invariants

1. Identity is distinct from mutable display metadata such as name/path.
2. Generic callers do not need provider-type switches for ordinary behavior.
3. Expensive optional behavior is discoverable rather than forced onto every item.
4. Cancellation and ownership are defined for asynchronous access.
5. Enumeration can be consumed progressively.

## Identity and references

A reference describes how to find/recover an item; an item model represents resolved state. Avoid using a mutable display path as the only notion of identity when the provider can provide a more stable key.

Rename/move/change-notification handling should preserve identity where the backend allows it.

## Hierarchy and enumeration

Files and folders implement `IFile` and `IFolder` directly. Parent traversal is a separate contract; folder enumeration does not establish write permissions.

Folder enumeration supplies the browse pipeline. Providers should yield useful items progressively and honor cancellation instead of materializing the complete folder before returning whenever the backend supports streaming enumeration.

## Optional behavior

Properties, thumbnails, preview, streams, operations, search, and change observation belong behind capability contracts. An item lacking a capability is valid; generic code must tolerate that absence.

Item resolution goes through the configured storage source and workspace. Folder change observation uses `IFolderChangeSource`; there is no separate mutable-folder watcher API.

## Mutations

Folder types describe hierarchy, not write permissions. A Windows folder can be a read-only namespace, an access-controlled directory, or a writable directory; its CLR type does not establish whether a particular mutation is allowed.

Mutation intent goes through `IStorageOperationService` and `IStorageOperationHandler`. `CanHandle(request)` checks routing support for the actual references and operation. It is not an authorization guarantee: execution must still validate current permissions and report failures. Do not infer writability from a folder type or add a parallel mutable-folder API.

## Ownership

A resolved model, stream, provider session, or capability result may have different lifetime semantics. APIs must document whether results are borrowed, owned, or tied to a parent/session lifetime. See [`../architecture/ownership-and-lifetime.md`](../architecture/ownership-and-lifetime.md).

## Extension points

New providers should implement common storage contracts and compose optional capabilities without adding provider-specific conditions to generic browsing/presentation. See [`storage-providers.md`](storage-providers.md).

## Tests

Protect reference/identity equality, resolution, hierarchy, enumeration/cancellation, provider capability absence/presence, stream ownership, and rename/move identity behavior.
