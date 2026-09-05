# Files.Core

Files.Core is the UI-independent model, storage, capability, and application
state foundation for Files hosts.

## Public entry point

Construct one `FilesCoreRuntime` at process startup:

```csharp
await using var runtime = new FilesCoreBuilder(
		persistedViewSettings,
		thumbnailCache)
	.AddWindowsStorage(
		streamPreviewPolicy: streamPolicy,
		shellPreviewPolicy: shellPolicy)
	.Build();

var window = await runtime.ShellSession.CreateWindowAsync(
	HomeLocation.Instance,
	cancellationToken);
```

The runtime exposes separate roots for the storage AppModel graph and the shell
session graph. A CLI or background host can use `runtime.Workspace` and
`runtime.StorageOperations` without creating any window, tab, pane, ViewModel,
or WinUI object:

```csharp
foreach (var source in runtime.Workspace.Sources)
{
	await foreach (var root in runtime.Workspace.GetRootsAsync(source.SourceId, cancellationToken))
	{
		await using (root)
		{
			Console.WriteLine(root.Name);
		}
	}
}
```

## Implemented model graph

```mermaid
flowchart TB
    Runtime["FilesCoreRuntime"]
    Workspace["IStorageWorkspace"]
    App["FilesApplicationSession<br/>(ShellSession)"]
    Window["WindowSession"]
    Tab["TabSession"]
    Pane["PaneSession"]
    Content["IPaneContentSession"]
    BrowsePane["BrowsePaneSession"]
    Session["BrowseSession"]
    Items["IStorableModel + capabilities"]

    Runtime --> Workspace
    Runtime --> App
    App --> Window
    Window --> Tab
    Tab --> Pane
    Pane --> Content
    Content --> BrowsePane
    BrowsePane --> Session
    Workspace --> Items
```

Implemented areas:

- stable source and item identity with recovery addresses;
- OwlCore.Storage CoreModels wrapped by Files AppModels;
- lazy capability factories, combiners, wrappers, and ownership;
- UI-independent shell sessions for application, window, tab, split-pane,
  navigation-history, and browsing state;
- home/folder/archive locations plus extensible search/tag location contracts;
- immutable item snapshots, selection, sorting, granular changes, and
  UI-agnostic presentation data;
- in-memory and pluggable view settings;
- cached encoded thumbnails, typed properties, and folder changes;
- stream previews and hosted Windows Shell preview sessions;
- Shell-first archive browsing with SevenZip fallback and UI-independent
  credential requests;
- FTP/FTPS sources with credential-free identity, owned remote streams,
  listing properties, and same-source storage operations;
- create, rename, copy, move, delete, collision, and progress contracts;
- one composition root with deterministic asynchronous disposal;
- Windows integration tests, architecture benchmarks, and Core CI.

## Windows vertical slice

`AddWindowsStorage` supplies:

- filesystem and virtual Shell resolution;
- versioned filesystem identity and encoded virtual-item identity;
- managed PIDL locators without exposing COM interfaces;
- parent lookup, bounded folder enumeration, and affine virtual streams;
- message-pumped STA lanes for metadata, extraction, operations, and preview;
- PNG thumbnails, typed Shell properties, and shared folder notifications;
- stream and Shell preview loaders;
- `IFileOperation` create/rename/copy/move/delete.
- Windows Shell archive folders with SevenZipSharp fallback on Windows 10,
  encrypted archives, unsupported Shell formats, and remote streams.

Files.Core targets Windows and owns its source-generated CsWin32 interop. It
does not reference WinUI.

## FTP vertical slice

`AddFtpStorage` supplies one source per saved connection:

- plain FTP, explicit TLS, and implicit TLS profiles;
- credentials supplied separately through `IFtpCredentialResolver`;
- normalized remote identities and credential-free recovery addresses;
- immutable files/folders, parent lookup, and one-listing enumeration;
- data streams that retain and complete their FTP control session;
- listing-backed properties and create/rename/copy/move/permanent-delete;
- automatic reuse of Core stream previews and archive browsing.

SFTP, runtime profile registration, polling changes, remote thumbnail policy,
and cross-source transfer coordination remain separate extension boundaries.

## Boundaries

- UI-independent Windows integration lives under `Windows`, grouped by
  responsibility with a shared `Files.Core.Windows` namespace. Low-level native
  declarations remain under `Windows/Interop`.
- WinUI ViewModels, dispatcher adaptation, image decoding, media/document
  rendering, preview presentation, item activation, drag/drop gestures,
  `DataPackage` adaptation, and menu presentation belong to the Files UI host.
- Search/tag behavior needs a selected backend and custom location handler.
- Additional storage sources plug into the same storage, capability,
  location, and operation contracts.
- Durable settings and window-session serialization are application policy.
- Retired storage projects no longer define competing contracts. The future of
  `Files.Shared` and migration of remaining Files.App consumers are separate
  concerns.

The complete design and Files.App implementation blueprint are in
[`docs`](../../docs/README.md).
