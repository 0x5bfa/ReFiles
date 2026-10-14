# Files.Core benchmarks

Most benchmarks measure deterministic architecture overhead without accessing the Windows Shell or the file system. App-extension catalog and context-menu benchmarks are exceptions: they exercise the installed Windows Shell and its handlers.

Run the benchmark project in Release mode on a stable machine:

```powershell
dotnet run --project tests/Files.Benchmarks/Files.Benchmarks.csproj -c Release -- --filter '*'
```

Use `--smoke` for a one-iteration validation run of the capability registry:

```powershell
dotnet run --project tests/Files.Benchmarks/Files.Benchmarks.csproj -c Release -- --smoke
```

The Shell and disk scenarios should be run separately because their results depend on the machine, file-system cache, and installed Shell extensions.

Measure fresh context-menu sessions with warmed registration caches:

```powershell
dotnet run --project tests/Files.Benchmarks/Files.Benchmarks.csproj -c Release -p:Platform=x64 -- --filter '*WindowsShellContextMenuBenchmarks*'
```

Inspect actual selections without invoking their commands:

```powershell
dotnet run --project tests/Files.Benchmarks/Files.Benchmarks.csproj -c Release -p:Platform=x64 -- --shell-menu-scenario 'C:\path\file.txt' 'C:\path\cloud-folder'
```

The scenario prints three JSON results per path, including construction time, time with icons, successful icon loads, and the final menu hierarchy. It rebuilds native state each time rather than timing a cached list of command states.
