# Repository layout

This page is a navigation map, not a replacement for the architecture docs.

```text
.github/                  Repository/CI configuration
src/
  Files.Core/             UI-independent Core and provider/platform logic
  Files/                  WinUI application and presentation
  Files.Controls/         Reusable WinUI controls
  Files.Operations/       Out-of-process operation host
  Files.SourceGenerators/ Build-time source generation
  FilesLauncher/          Native launcher/integration component
tests/
  Files.UnitTests/        Unit and Windows integration tests
  Files.Benchmarks/       Deterministic micro/architecture benchmarks
  Files.UITests/          WinUI test host and presentation/control tests
  Files.AxeTests/         Accessibility automation boundary
docs/                     Current contributor technical documentation
```

## Before adding a new project/folder

Ask whether the new code has a genuinely different dependency/lifetime/deployment boundary. Do not create a new layer solely to group similar class names.

## Before moving code

Check [`../architecture/layering.md`](../architecture/layering.md). A physical move that introduces the wrong dependency direction is not an architectural improvement.

## Tests

Put tests at the lowest layer that can validate the contract deterministically. Environment-dependent Shell/UI behavior belongs at a higher integration/scenario boundary rather than being simulated as a fragile unit test.
