# Files.Core benchmarks

The benchmark project measures deterministic architecture overhead only. It does not access the Windows Shell or the file system.

Run the benchmark project in Release mode on a stable machine:

```powershell
dotnet run --project tests/Files.Benchmarks/Files.Benchmarks.csproj -c Release -- --filter '*'
```

Use `--smoke` for a one-iteration validation run of the capability registry:

```powershell
dotnet run --project tests/Files.Benchmarks/Files.Benchmarks.csproj -c Release -- --smoke
```

The Shell and disk scenarios should be run separately because their results depend on the machine, file-system cache, and installed Shell extensions.
