# Project split status

The application consumer migration is implemented. Files and BackgroundServer compile against consumer Contracts and Hosting, while Core and Providers remain independent implementations. Resolved-reference checks reject implementation and backend compiler references in both executables. Their runtime assets are retained for deployment.

## Assemblies and packages

| Assembly | NuGet package | Responsibility |
| --- | --- | --- |
| `Files.Core.Contracts` | `ReFiles.Core.Contracts` | Consumer capabilities, item models, identifiers, browse snapshots, operation requests, session interfaces, and runtime contracts. No implementation or generated Win32 references. |
| `Files.Core.ProviderContracts` | `ReFiles.Core.ProviderContracts` | Raw item/source and backend contracts, capability factories/contexts, runtime registration and model-adaptation interfaces, and provider-neutral contract helpers. References consumer Contracts, not either implementation. |
| `Files.Core` | `ReFiles.Core` | Generic runtime, model adaptation, capability composition, archive mounts, browsing and transfer orchestration. References Contracts and ProviderContracts. |
| `Files.Providers` | `ReFiles.Providers` | Windows Shell, FTP and SevenZip implementations, native declarations, and built-in registration extensions. References Contracts and ProviderContracts, never Core. |
| `Files.Hosting` | `ReFiles.Hosting` | Fixed UI-process and operation-host startup. Public signatures use Contracts and borrowed logging/services; Core and Providers are runtime dependencies with their compile assets hidden from downstream consumers. |

Existing namespaces are retained. Core forwards moved public types to consumer Contracts or ProviderContracts. Provider implementations are not forwarded from Core, because doing so would restore the forbidden implementation reference. This preserves moved type identity, but changed signatures such as `IArchiveSource.OpenAsync` returning `IArchiveModel` still require consumers to rebuild; this is not a promise of complete binary compatibility.

NuGet users that explicitly reference Core retain `FilesCoreBuilder` and capability composition. ProviderContracts supplies custom factories, combiners and wrappers, and Providers supplies `AddWindowsStorage`, `AddFtpStorage`, `AddArchiveBrowsing` and `AddStorageTransfers` as extensions over `IProviderRuntimeBuilder`. Existing method-call syntax is retained when Providers is referenced. FluentFTP, SevenZipSharp and System.Management now belong only to Providers; their runtime assets are retained while downstream compile assets are hidden.

`IProviderRuntimeBuilder` lets modules register sources, readers/loaders, handlers and provider-service factories without naming concrete Core classes. Core owns the registry, generic adapters and workspace construction. Provider-service factories return borrowed integrations; modules register their resource owners through `Own` before runtime construction. `IStorableModelFactory.OpenArchiveAsync` bridges archive model creation; `IArchiveRootSource` identifies the outer source of a mount. Preview authorization uses a captured `IPreviewTrustIdentity`, with Windows path matching implemented by the Windows provider. Shared archive-input buffering preserves the existing seekable-stream and disposal contract.

`IProviderRuntimeServices` is a provider-SDK integration role, not part of `IFilesCoreRuntime`. Explicit provider-SDK consumers can retrieve Windows preview session factories through `GetWindowsShellPreviewSessions()`. Files instead uses `IUiFilesRuntime.Windows.Previews`. Hosting's consumer facade does not implement the provider-SDK role or expose arbitrary registration callbacks. The resolved-reference checks reject Providers and backend packages from Core, and reject Core from Providers and ProviderContracts.

## Consumer ownership

`IFilesCoreRuntime` exposes session interfaces and an `IStorageReader`. The reader is a distinct borrowed facade: it returns `StorageSourceDescriptor` values and application models, and cannot be cast to raw source registration or disposal roles. The provider-facing `IStorageWorkspace` lives in ProviderContracts.

`IStorableModel.Locator` carries immutable transfer provenance. Capturing a locator no longer requires a consumer-side raw-model cast. Preview trust tokens live in Contracts internally; generic target authorization remains in Core, while Windows path matching is supplied by Providers.

## Operation host

BackgroundServer obtains `IOperationHostRuntime` from Hosting. FTP registration, Shell New menu operations, filesystem attribute changes, source resolution, operations, and thumbnails pass through typed contracts. Request acceptance, credential-update checks, progress, cancellation, and job ownership remain in the server.

BackgroundServer generates private CsWin32 declarations for process activation, window validation and tray hosting. `DisableTransitiveProjectReferences` and the resolved-reference check in `Directory.Build.targets` reject Core, Providers, ProviderContracts and backend package compiler references. Their runtime assets are still copied for execution.

## Native boundaries

Providers, Files, BackgroundServer and Launcher generate independent internal CsWin32 declarations. Providers exports no `Windows.Win32` types or native COM signatures. This intentionally requires callers of the former native signatures to rebuild and adapt to the managed API.

Preview hosts and focus results use borrowed `nint` window handles; Providers never destroys caller-owned windows. Accelerator callbacks carry `WindowsPreviewMessage` snapshots rather than `MSG`. Files retains its own window procedures, Direct3D/DirectComposition setup and `ContentExternalOutputLink` rendering, using its locally generated types.

Drag sources accept WinRT `DataPackage` values. `WindowsShellDataTransfer` retains the incoming data object's generated COM wrapper without exposing its interface; create and use it on the originating STA. Drop sessions retain their existing same-thread disposal requirement. Native wrappers retain COM references and release them when collected. Shell property editors return `WindowsShellOperationResult`, preserving HRESULT success, cancellation and failure statuses.

Managed preview, menu, clipboard, drag/drop and property-sheet contracts live in consumer Contracts. Providers retains the implementations and its explicit extension SDK. Opaque transfer/menu targets expose no COM interfaces or PIDL buffers through consumer contracts. Reflection tests reject exported native types and public/protected native signatures.

## Files consumer migration

`FilesHost.CreateUiRuntime` returns `IUiFilesRuntime`. Its borrowed Windows services provide previews, clipboard access, native data-transfer capture, menus, contextual/default commands, property data and registered synchronization roots. Files consumes session interfaces and `IStorageReader`; pane presenters select the registered interface while retaining exact custom-content registrations. Windows property editors and filesystem inspection are obtained through typed Hosting factories. Attribute edits continue through the configured operation server.

Files generates only its own UI/window/composition interop. Native clipboard extraction, filesystem metadata and settings file operations reside in Providers. `DisableTransitiveProjectReferences` plus `EnforceConsumerReferenceBoundary` prevent direct and transitive Core, Providers, ProviderContracts and backend package compiler references. UI tests explicitly reference implementations only to construct fixtures, then pass consumer readers to the product.

## Settings before runtime startup

`FilesHost.OpenSettingsStorage` configures an `ISettingsStorage` confined to the application's `Settings` directory. App settings can load before XAML initialization and runtime construction. JSON schemas, generated settings properties, save timers, retry policy and change notifications remain in Files. Providers owns UTF-8 I/O, temporary-file cleanup and atomic commit; failed or cancelled writes preserve the previous document. Native replacement retains destination metadata.

The browse-settings production source is also exercised by the headless unit runner. Tests cover reload, initial creation, replacement, a locked destination, retry after failure, cancellation and rejection of path components.

## Remaining validation

Reviewed public API baselines and broader negative-compilation checks can still be added to CI. Packaged deployment and supported architectures require separate validation, including native 7-Zip assets and the server WinRT projection. Public API closure tests cover Contracts and Hosting, and the executable compiler-reference checks enforce their consumer boundary. The original review and acceptance criteria remain recorded in [issues.md](issues.md).
