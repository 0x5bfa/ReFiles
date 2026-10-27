# Archives

Archive browsing uses the same file and folder models as other sources. Archive technology stays behind a managed reader boundary; application models and navigation do not depend on SevenZipSharp or native interfaces. No OwlCore dependency is used.

## Responsibilities

| Component | Responsibility |
| --- | --- |
| `IArchiveBackend` / `IArchiveReadSession` | Open borrowed seekable input, enumerate physical records, and verify extracted content. |
| `ArchiveCatalog` / `ArchiveStorageSource` | Project records into one read-only storage hierarchy and serialize access to its reader. |
| `ArchiveModel` | Own an independent archive view and expose ordinary `IFolderModel` / `IFileModel` objects. |
| `IArchiveSource` | Advertise an archive candidate and open its application model through configured dependencies. |
| `ArchiveBrowseLocationHandler` / context | Adapt an archive model to browsing and cross-container navigation. |
| Presentation | Invoke application navigation and display results. It does not choose readers or manage native sessions. |

`ArchiveModel.OpenAsync` also works without a browse session. The execution owner supplies a configured source, model factory, readers, and optional credential resolver. This is the opening boundary for later headless operations.

## Records, identity, and reopening

`ArchiveEntryInfo.Id` identifies a physical record within one reader's current input. `Entries` preserves duplicate names, case differences, explicit directory records, and names that cannot safely appear in a folder hierarchy. Invalid or absolute paths are excluded from that hierarchy, not silently removed from the raw catalog.

Files are resolved by record identity, never by a first-name match. A file and folder can share a path. Implicit directories and repeated explicit directory records share a logical folder; their original records remain in `Entries`.

Each opened view receives a fresh `SourceId`. Its item references are valid only in that view, preventing a later open from interpreting an old record reference against changed content. Item models borrow the view's lifetime.

`ArchiveLocator` identifies the configured outermost item and a bounded chain of nested record IDs with expected normalized paths. Reopening resolves **current content** and checks each nested selection. It does not retain an old view or guarantee an immutable content revision. A record at the same index and path can have different content after reopening. Jobs requiring unchanged input must acquire their own input and apply an explicit validation policy.

`IArchiveEntry.Archive` is the container reference observed during the open; for nested archives it can belong to an intermediate session that has since closed. Use `Locator`, not that reference, to reopen the container chain. Persisted view settings include the complete chain and do not share a scope between sibling nested archives.

## Hierarchy and navigation

The archive storage root has no parent inside its hierarchy. Its backing file is provenance, not a parent folder. Up from the outer root navigates to the backing file's containing folder; Up from a nested root navigates to the enclosing archive folder containing that nested file. Breadcrumbs preserve the complete locator.

Nested archives open independently. Once a nested input has been extracted and verified, its enclosing view can close. Navigation can reopen the enclosing chain when needed. Nesting is limited to 32 containers.

## Input and ownership

- Configured storage sources and capability results are borrowed.
- A successful `IArchiveSource.OpenAsync` returns an owned `ArchiveModel`.
- `ArchiveModel.Root` is borrowed. Resolved or enumerated item models are owned by their caller and borrow the archive lifetime.
- An archive view owns its seekable input, reader session, and root model. Closing waits for active extraction and releases these once.
- `IArchiveReadSession` borrows its input and must leave it open on every opening outcome and during disposal.
- `OpenReadAsync` returns an owned, verified, independent seekable stream. It can outlive the archive view. Failed or canceled extraction publishes no stream.

Opening always resolves the composed `IReadableContent`, respecting wrappers and access policies. An internal `IArchiveInput` optimization may provide independent input directly through that capability. Other content is materialized to a delete-on-close temporary file; the original read stream must dispose successfully before opening proceeds. This includes an FTP transfer's final completion response.

Local Windows input is pinned with `FileShare.Read`, and identity is checked again after opening. Writes and deletion conflict while the view is open. If another incompatible handle already exists, opening fails. Generic remote content is materialized; there is no claim of a provider-independent immutable revision.

## Reader failures and credentials

Only an explicit `Unsupported` result permits reader fallback. Corruption, access failures, and credential challenges are not interpreted as unsupported input. The standard configuration registers one SevenZip reader.

Credential handling belongs to the archive model/provider, including challenges discovered during extraction. Retry count is bounded. A published view retains its selected backend and catalog; reopening after a challenge must reproduce the catalog or fail. Passwords are not stored in locators or item identities.

Never log passwords or place them in addresses, diagnostics, or persisted state. Archive paths remain untrusted: extraction code must not concatenate decoded record paths into filesystem destinations, even if browsing accepts them as logical names.

The SevenZip reader extracts a selected record to a temporary file and checks completion before returning it. Cancellation is checked before extraction, on output writes, and before publishing the result. A solid archive can still spend time decoding preceding data between cancellation checks. There is no hard termination guarantee for native calls.

## Shipped dependencies

The local feed contains `SevenZipSharp.1.0.3-desktop.1.nupkg`, built from the unmodified Files Community fork at [`9725bda`](https://github.com/files-community/SevenZipSharp/tree/9725bda53e1ee018b39a51c268a90c4905915926), using its `Desktop` configuration. That configuration loads an absolute native library path without requiring package identity. Rebuild it with `build/Build-SevenZipSharp.ps1`; the ordinary 1.0.3 package uses `LoadPackagedLibrary` and cannot serve unpackaged execution.

`Files.Core/Native` contains the unchanged x64 and ARM64 7-Zip 26.00 binaries from upstream Files commit `77d95dde8`. The project copies the matching binary and dependency notices into build/publish output and packs both runtime assets. Native sources and license information are linked in `7zip-LICENSE.txt`; wrapper source and LGPLv3/GPLv3 notices are distributed alongside the binaries.

## Deliberate limits

This implementation is read-only. It does not introduce archive updates, a session pool, a persistent catalog cache, a transfer executor, or cross-process live models. Individual entry reads can repeat solid-block decoding. The later transfer plan should add one batch extraction boundary for selected records and an operation-owned destination import contract, without turning folder models into mutation services.

## Tests

Unit tests cover exact record resolution, collisions, traversal rejection, nested navigation, session identity, fallback, credential replacement, late cancellation, disposal races, and input completion failures. Real native fixtures cover duplicate ZIP names, CRC rejection, 7z content and header encryption, pinned local input, and composed read wrappers. Package-consumer smoke checks must also verify native asset and notice placement independently of project references.
