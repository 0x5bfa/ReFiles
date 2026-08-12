# Building ReFiles

ReFiles is a Windows solution containing WinUI/.NET projects plus a native launcher. Use a supported Windows development environment with the SDK/tooling required by the projects.

## Solution

The repository solution is [`../../Files.slnx`](../../Files.slnx). It currently includes:

- `src/Files.Core`
- `src/Files`
- `src/Files.Controls`
- `src/Files.Operations`
- `src/Files.SourceGenerators`
- `src/FilesLauncher`
- unit, benchmark, UI, and accessibility test projects.

Supported solution platforms are `x64`, `x86`, and `arm64`; individual test/build workflows may intentionally target only a subset.

## Typical validation

For Core-focused changes, a useful local Release validation is:

```powershell
dotnet build tests/Files.UnitTests/Files.UnitTests.csproj -c Release -p:Platform=x64
dotnet test tests/Files.UnitTests/Files.UnitTests.csproj -c Release -p:Platform=x64 --no-build
dotnet run --project tests/Files.Benchmarks/Files.Benchmarks.csproj -c Release -p:Platform=x64 -- --smoke
git diff --check
```

For application/control changes, also build the affected WinUI project and run the relevant UI/control/accessibility tests described under [`../testing/`](../testing/strategy.md).

## Warnings and compatibility analysis

Do not treat a Debug-only successful test run as sufficient validation for Core changes. Release builds exercise analyzers/compatibility settings that may not be visible in a narrow test invocation.

## CI is the source of truth

Repository workflows may evolve. Before changing build instructions or troubleshooting a CI-only failure, inspect `.github/workflows/` and project files rather than copying old commands from archived documentation.

## Related docs

- [`repository-layout.md`](repository-layout.md)
- [`../testing/strategy.md`](../testing/strategy.md)
- [`debugging.md`](debugging.md)
