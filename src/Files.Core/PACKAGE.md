# ReFiles.Core

ReFiles.Core is the UI-independent application, browsing, storage, and capability foundation used by ReFiles. The separate `ReFiles.Providers` package supplies Windows Shell, FTP, and 7-Zip integration; neither package depends on WinUI.

The package is currently prerelease. Pin an exact version while its public API evolves:

```xml
<PackageReference Include="ReFiles.Core" Version="0.1.0" />
<PackageReference Include="ReFiles.Providers" Version="0.1.0" />
```

## Platform requirements

- .NET 10
- Windows 10 version 2004 or later
- x64

Archive support uses SevenZipSharp 1.0.4, including its native 7-Zip runtime assets. Restore it from this repository's `src/Files/Assets/Libraries` feed as configured by `nuget.config`. See [archive architecture](https://github.com/0x5bfa/ReFiles/blob/main/docs/subsystems/archives.md) for ownership contracts.

Documentation and source are available in the [ReFiles repository](https://github.com/0x5bfa/ReFiles).

## Consumer and hosting packages

`ReFiles.Core.Contracts` contains consumer interfaces and value types. `ReFiles.Hosting` provides fixed process startup through `FilesHost`, returning `IFilesCoreRuntime` or `IOperationHostRuntime` without exposing Core compile assets. Reference `ReFiles.Core` explicitly for runtime composition and `ReFiles.Core.ProviderContracts` for provider-facing models, factories, and registration interfaces. Standard provider registration extensions are supplied by `ReFiles.Providers`. Core and Providers do not reference each other.

See [project split status](https://github.com/0x5bfa/ReFiles/blob/main/docs/architecture/project-split.md) for migration progress and compatibility details.
