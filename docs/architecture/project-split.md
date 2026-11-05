# Project split status

The split is implemented incrementally. The Contracts/Hosting boundary and operation-host migration are in place; the Files UI migration is still pending. Adding projects alone does not establish a boundary for Files while it retains its Core reference.

## Assemblies and packages

| Assembly | NuGet package | Responsibility |
| --- | --- | --- |
| `Files.Core.Contracts` | `ReFiles.Core.Contracts` | Consumer capabilities, item models, identifiers, browse snapshots, operation requests, session interfaces, and runtime contracts. No implementation or generated Win32 references. |
| `Files.Core` | `ReFiles.Core` | Runtime and provider implementations plus the explicit composition/provider SDK for extension authors. References Contracts. |
| `Files.Hosting` | `ReFiles.Hosting` | Fixed UI-process and operation-host startup. Public signatures use Contracts and borrowed logging/services; Core is a runtime dependency with its compile assets hidden from downstream consumers. |

Existing namespaces are retained. Core forwards moved public types to Contracts. This preserves moved type identity, but changed signatures such as `IArchiveSource.OpenAsync` returning `IArchiveModel` still require consumers to rebuild; this is not a promise of complete binary compatibility.

NuGet users that explicitly reference Core retain `FilesCoreBuilder`, custom factories, combiners, wrappers, and `SetBorrowedView<T>()`. Consumers of Hosting alone cannot compile against that implementation API. Core's FluentFTP, SevenZipSharp and System.Management dependencies retain runtime assets while hiding downstream compile assets.

## Consumer ownership

`IFilesCoreRuntime` exposes session interfaces and an `IStorageReader`. The reader is a distinct borrowed facade: it returns `StorageSourceDescriptor` values and application models, and cannot be cast to raw source registration or disposal roles. The provider-facing `IStorageWorkspace` remains in the Core SDK.

`IStorableModel.Locator` carries immutable transfer provenance. Capturing a locator no longer requires a consumer-side raw-model cast. Preview trust tokens live in Contracts internally; target identity and Windows path matching remain in Core.

## Operation host

BackgroundServer obtains `IOperationHostRuntime` from Hosting. FTP registration, Shell New menu operations, filesystem attribute changes, source resolution, operations, and thumbnails pass through typed contracts. Request acceptance, credential-update checks, progress, cancellation, and job ownership remain in the server.

BackgroundServer generates private CsWin32 declarations for process activation, window validation and tray hosting. `DisableTransitiveProjectReferences` and the resolved-reference check in `Directory.Build.targets` reject Core and implementation package compiler references. Their runtime assets are still copied for execution.

## Remaining Files migration

1. Replace Files' concrete session/runtime consumers with the new interfaces.
2. Define and migrate Windows UI contracts for previews, menus, clipboard and drag/drop, preserving COM apartments, callbacks and resource ownership.
3. Move direct item I/O and settings persistence behind explicit services, including startup settings access before runtime construction.
4. Switch App to Hosting; remove Files' Core reference and enable its resolved-reference check. Internalize provider interop after its remaining callers are migrated.
5. Add reviewed public API baselines and negative compilation checks to CI. Validate packaged deployment and supported architectures, including native 7-Zip assets and the server WinRT projection.

Public API closure tests cover Contracts and Hosting. A real Hosting NuGet consumer has also been checked to reject raw Core construction, while an explicit Core package reference retains custom capability composition. The full acceptance criteria in [issues.md](issues.md) remain applicable.
