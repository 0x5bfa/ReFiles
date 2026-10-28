# Layering and code placement

This document answers: **where should this behavior live?** Choose the layer from ownership and semantics, not from whichever project is easiest to reference.

## Dependency direction

```mermaid
flowchart TD
    Files[Files]
    Controls[Files.Controls]
    Core[Files.Core]
    Operations[Files.BackgroundServer]

    Files --> Controls
    Files --> Core
    Operations --> Core
```

`Files` separately consumes `Files.BackgroundServer.winmd` through generated C#/WinRT projection code. Runtime calls cross that boundary; they do not make Core reference the server. No separate client project is needed.

These architectural rules remain:

- presentation may depend on Core;
- Core must not depend on presentation;
- provider implementations must not depend on ViewModels;
- reusable controls must not acquire application/storage ownership;
- the operations host must communicate through explicit contracts.

## Code placement guide

| Concern | Preferred owner |
|---|---|
| Storage abstractions and identity | `Files.Core` |
| Provider contracts and implementations | `Files.Core` provider/platform areas |
| Browse/navigation state | `Files.Core` |
| Capability contracts | `Files.Core` |
| Property/thumbnail/preview retrieval contracts | `Files.Core` |
| Provider-specific retrieval | provider implementation |
| WinUI image creation/decoding | `Files` |
| ViewModels and presentation adapters | `Files` |
| XAML and localized display formatting | `Files` |
| Viewport/focus/visual selection | presentation/UI boundary |
| Generic reusable WinUI controls | `Files.Controls` |
| App-specific control behavior | `Files` |
| Domain I/O execution and accepted background operations | `Files.BackgroundServer`, using Core providers |
| WinRT projection, conversion, and reconnection | adapter inside `Files` |
| Authoritative operation progress and cancellation | `Files.BackgroundServer` |
| Status Center formatting and graphs | `Files` |

## Core source layout

Folders group code by responsibility. `FilesCoreBuilder` and `FilesCoreRuntime` are public entry points in the `Files.Core` namespace. Other existing public namespaces are retained independently of folder organization.

| Folder under `src/Files.Core` | Responsibility |
|---|---|
| `Application` | Application/window/tab/pane ownership, content sessions, navigation history, and user intent. The existing namespace is `Files.Core.Sessions`. |
| `Browsing` | One pane's browse engine and its locations, navigation, selection, projection, prefetch, preview, search, changes, and view settings. |
| `Storage/Items` | Storage item contracts and basic item types. |
| `Storage/Identity` | Source identities, addresses, references, and operation locators. |
| `Storage/Models` | Item models that compose storage items with optional capabilities. The existing namespace is `Files.Core.Models`. |
| `Storage/Workspace` | Source registration, item resolution, and source lifetime ownership. The existing namespace is `Files.Core.Data`. |
| `Storage/Operations` | Operation routing and execution contracts, with `Requests`, `Jobs`, and `Transfers` subfolders. |
| `Storage/Windows`, `Storage/Ftp`, `Storage/Archives` | Provider implementations and their implementation-specific contracts. |
| `Capabilities` | Optional item behavior, grouped by feature. |
| `FilesCoreBuilder*.cs`, `FilesCoreRuntime.cs` | Runtime construction and shared service composition at the Core root. |

`Application/BrowsePaneSession` owns a `Browsing/BrowseSession`, a preview model, and navigation history. It coordinates Back/Forward, search, and selection operations. `BrowseSession` loads a location, enumerates and projects items, tracks selection and changes, and applies view settings. Each split pane gets an independent browse session; a settings pane has different content and does not require one.

Related interfaces and implementations stay together in their feature folder. Operation requests and Windows preview contracts use individual files named after their types. Keep partial declarations such as `BrowseSession.cs` and `BrowseSession.Changes.cs` together.

`FilesCoreBuilder.Windows.cs`, `.Ftp.cs`, `.Archives.cs`, and `.Transfers.cs` contain feature registration as partial declarations of the same builder. `AddWindowsStorage` is an instance method alongside `AddFtpStorage`, `AddArchiveBrowsing`, and `AddStorageTransfers`. Registration helpers remain private to the builder; the feature files preserve source ownership, capability ordering, and cleanup behavior.

## Example: a new storage provider

Identity, hierarchy, enumeration, streams, capabilities, properties, and operations belong behind Core/provider contracts. Provider-specific authentication UI belongs in `Files`.

Avoid generic code like:

```csharp
if (provider is FtpProvider) { ... }
else if (provider is SomeCloudProvider) { ... }
```

when a capability or provider contract can express the required behavior.

## Example: a Details column

The property's semantic meaning and retrieval belong below the presentation boundary. Localized labels, human-readable formatting, alignment, and visual templates belong in presentation.

Core should not return UI strings solely because a WinUI column needs them.

## Example: thumbnails

Retrieval and cache semantics belong below the UI boundary. Creating WinUI-specific image objects belongs in presentation.

## Review red flags

- `Microsoft.UI.*` dependencies entering `Files.Core`;
- ViewModels in provider implementations;
- provider-type switches in generic browsing/presentation;
- arbitrary-thread Shell calls;
- UI controls disposing storage resources they merely display;
- storage models producing localized presentation strings;
- app-specific state in reusable controls;
- duplicated provider behavior instead of a shared capability.

## Rule of thumb

Ask:

1. Is this behavior meaningful in a non-WinUI client?
2. Does it define application/storage semantics, or only how those semantics are displayed?

If it is meaningful without WinUI, it usually belongs at or below the Core boundary.

## Application models and provider boundaries

`Files.Core.Storage` remains the project's own storage abstraction. It has no OwlCore dependency. Provider items implement the small storage contracts; Files item models compose optional capabilities over them.

`BrowseApplicationServices` supplies borrowed workspace, search, and operation dependencies to the pane factory. `BrowsePaneSession` owns application intent: search navigation, selection deletion, history, and the browse lifetime. `BrowseNavigationModel` traverses hierarchy and produces navigation values while disposing temporary provider models. ViewModels format these values and bind application behavior; they do not resolve storage addresses or select search backends.

Optional provider differences use `IStorageItemAttributes`, `IInteractiveFolder`, and `IFolderBrowseMetadata`. `BrowseColumnSet` is the shared column contract. Windows metadata conversion and native ordering live in `WindowsFolderBrowseMetadata`; generic folder models and browse contexts do not inspect Windows implementation types.

The composition root selects operation execution with `FilesCoreBuilder.UseStorageOperations`. The runtime, browse models, and presentation use the same borrowed service. The runtime exposes its optional `IStorageOperationMonitor` without requiring a ViewModel or WinRT client type. The host that supplies an external execution service retains its lifetime ownership.
