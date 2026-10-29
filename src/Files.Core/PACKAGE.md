# ReFiles.Core

ReFiles.Core is the UI-independent application, browsing, storage, and capability foundation used by ReFiles. It includes Windows Shell integration and optional FTP and archive providers without depending on WinUI.

The package is currently prerelease. Pin an exact version while its public API evolves:

```xml
<PackageReference Include="ReFiles.Core" Version="0.1.0-alpha" />
```

## Platform requirements

- .NET 10
- Windows 10 version 2004 or later
- x64

Archive support uses SevenZipSharp 1.0.4, including its native 7-Zip runtime assets. Restore it from this repository's `src/Files/Assets/Libraries` feed as configured by `nuget.config`. See [archive architecture](https://github.com/0x5bfa/ReFiles/blob/main/docs/subsystems/archives.md) for ownership contracts.

Documentation and source are available in the [ReFiles repository](https://github.com/0x5bfa/ReFiles).
