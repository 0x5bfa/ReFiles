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

Current provider guarantees differ:

| Provider key | Continuity and limits |
| --- | --- |
| Windows native file ID | Can survive same-volume rename/move. Cross-volume moves and file ID reuse do not preserve an incarnation guarantee. |
| Windows address fallback | Identifies a namespace entry by parsing name, including hard links. Path changes change the key; changing hard-link topology can also change the selected identity scheme. |
| FTP path | Identifies the normalized path within one configured source. Rename changes the key; deletion and recreation at the same path can reuse it. |
| SevenZip entry path | Identifies a path within an archive-reference-derived namespace. A remount can reuse the key for changed content; resolved objects still borrow the active mount. |

Equality is not proof of unchanged content or an unchanged object incarnation. Keep `LastKnownAddress` outside equality. Path-changing operation results can relate old and new references explicitly; they must not redefine equality. Caches also need change invalidation or content revisions, particularly when refreshing path-backed sources or remounting an archive.

## Hierarchy and enumeration

Files and folders implement `IFile` and `IFolder` directly. Parent traversal is a separate contract; folder enumeration does not establish write permissions.

Folder enumeration supplies the browse pipeline. Providers should yield useful items progressively and honor cancellation instead of materializing the complete folder before returning whenever the backend supports streaming enumeration.

An archive entry has a parent inside its mounted hierarchy; the mounted root has none there. `IArchiveEntry.Archive` identifies the containing archive item separately. Browse navigation crosses the mount boundary: Up from an archive root returns the folder containing the archive, not the archive file as a parent folder. Nested mounts currently require an explicit mount-chain and lifetime contract before they can be supported.

Cross-source parent relationships need references carrying `SourceId`; callers must not assume that a returned raw parent's source matches the child's. A storage parent also cannot represent every occurrence of an item in search results or collections; the browse location determines those navigation relationships.

## Optional behavior

Properties, thumbnails, preview, streams, operations, search, and change observation belong behind capability contracts. An item lacking a capability is valid; generic code must tolerate that absence.

Item resolution goes through the configured storage source and workspace. Folder change observation uses `IFolderChangeSource`; there is no separate mutable-folder watcher API.

### Content access

`IFile` classifies an item; it does not promise stream access. Discover `IReadableContent` and `IWritableContent` separately through the model's capabilities. A folder can expose readable content, as Shell archive folders do, without every folder becoming a stream source.

Preview and archive loading resolve composed read capabilities, including registered factories and wrappers. An address is not permission to bypass a missing capability and open a local file directly. Factories and loaders can resolve dependencies through their bound `ItemContext.GetCapability<T>()`.

Windows composition exposes reads for file-system files and Shell stream items, and writes for file-system files. FTP files expose independent read and write streams. SevenZip entries expose reads only. Presence describes backend support, not authorization or current availability.

`OpenWriteAsync` does not guarantee truncation or atomic replacement. Windows preserves the existing file length unless the caller changes it; FTP retains its session implementation's overwrite behavior. Use the operation service when requesting replacement semantics.

## Mutations

Folder types describe hierarchy, not write permissions. A Windows folder can be a read-only namespace, an access-controlled directory, or a writable directory; its CLR type does not establish whether a particular mutation is allowed.

Mutation intent goes through `IStorageOperationService` and `IStorageOperationHandler`. `CanHandle(request)` checks routing support for the actual references and operation. It is not an authorization guarantee: execution must still validate current permissions and report failures. Do not infer writability from a folder type or add a parallel mutable-folder API.

## Ownership

A resolved model, stream, provider session, or capability result may have different lifetime semantics. APIs must document whether results are borrowed, owned, or tied to a parent/session lifetime. See [`../architecture/ownership-and-lifetime.md`](../architecture/ownership-and-lifetime.md).

`Get<T>()` and `ItemContext.GetCapability<T>()` return borrowed capabilities. Do not dispose them; their model or shared service owns them. By contrast, each yielded child, resolved parent, and successfully opened stream transfers responsibility for the result to its caller. Transfer raw items to an owning model, or dispose them when disposable. Stopping enumeration does not release items already yielded.

Keep the item model and source/mount alive until their streams close unless the provider guarantees independence. An FTP stream owns its connected session; an extracted SevenZip entry owns its temporary file. Windows Shell streams borrow the source's scheduler. Cancellation during opening or enumeration must release unpublished results; cancellation after stream ownership transfers does not close the stream.

## Extension points

New providers should implement common storage contracts and compose optional capabilities without adding provider-specific conditions to generic browsing/presentation. See [`storage-providers.md`](storage-providers.md).

## Tests

Protect reference/identity equality, resolution, hierarchy, enumeration/cancellation, provider capability absence/presence, stream ownership, and rename/move identity behavior.
