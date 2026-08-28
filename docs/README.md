# ReFiles technical documentation

This directory is the technical handbook for contributors working on ReFiles.

The documentation is organized around **architectural contracts, invariants, ownership, data flow, concurrency, extension points, testing, and performance** rather than around individual classes. Class names and implementations may change; the rules that keep the system correct should remain understandable.

## Start here

Read these documents before making architectural changes:

1. [`architecture/overview.md`](architecture/overview.md) — high-level architecture and project responsibilities.
2. [`architecture/principles.md`](architecture/principles.md) — rules that changes are expected to preserve.
3. [`architecture/layering.md`](architecture/layering.md) — dependency direction and where code belongs.
4. [`architecture/ownership-and-lifetime.md`](architecture/ownership-and-lifetime.md) — ownership, cancellation, and disposal.
5. [`architecture/browsing.md`](architecture/browsing.md) — end-to-end browse pipeline.
6. [`architecture/presentation.md`](architecture/presentation.md) — the Core/WinUI boundary.

## Repository layers

```text
ReFiles
├─ Files.Core          UI-independent application, browsing, storage, and model logic
├─ Files               WinUI application and presentation
├─ Files.Controls      Reusable WinUI controls
├─ Files.Operations    Out-of-process operation host
├─ Files.SourceGenerators
└─ FilesLauncher       Native launcher/integration component
```

The most important dependency rule is:

> Core logic must not depend on presentation.

`Files.Core` must remain independent from XAML, WinUI controls, ViewModels, visual state, and UI formatting.

## By task

### Browsing and navigation

- [`architecture/browsing.md`](architecture/browsing.md)
- [`subsystems/storage.md`](subsystems/storage.md)
- [`subsystems/capabilities.md`](subsystems/capabilities.md)
- [`development/performance.md`](development/performance.md)

### Storage providers

- [`subsystems/storage-providers.md`](subsystems/storage-providers.md)
- [`development/adding-a-storage-provider.md`](development/adding-a-storage-provider.md)
- [`architecture/layering.md`](architecture/layering.md)

### Windows Shell

- [`subsystems/windows-shell.md`](subsystems/windows-shell.md)
- [`explorer/README.md`](explorer/README.md)
- [`explorer/property-sheets/README.md`](explorer/property-sheets/README.md)
- [`explorer/drive-property-sheets/README.md`](explorer/drive-property-sheets/README.md)
- [`architecture/ownership-and-lifetime.md`](architecture/ownership-and-lifetime.md)

### Properties, thumbnails, and preview

- [`subsystems/capabilities.md`](subsystems/capabilities.md)
- [`subsystems/properties.md`](subsystems/properties.md)
- [`subsystems/thumbnails.md`](subsystems/thumbnails.md)
- [`subsystems/preview.md`](subsystems/preview.md)

### UI and controls

- [`architecture/presentation.md`](architecture/presentation.md)
- [`architecture/layering.md`](architecture/layering.md)
- [`testing/ui-tests.md`](testing/ui-tests.md)

### Testing and performance

- [`testing/strategy.md`](testing/strategy.md)
- [`testing/unit-tests.md`](testing/unit-tests.md)
- [`testing/integration-tests.md`](testing/integration-tests.md)
- [`testing/ui-tests.md`](testing/ui-tests.md)
- [`testing/performance-tests.md`](testing/performance-tests.md)

## Documentation contract

When a pull request changes an architectural contract, update the relevant document in the same pull request. In particular, documentation should be reviewed when changing:

- layer ownership or dependency direction;
- lifetime/disposal semantics;
- concurrency or COM apartment behavior;
- navigation generations or cancellation;
- provider/capability contracts;
- browse publication or enrichment flow;
- operation execution boundaries;
- performance expectations;
- extension points.

A subsystem document should answer **why the subsystem exists, what it owns, what it does not own, what must remain true, and how it is tested**. Avoid documentation that only restates methods or current private implementation details.

## Legacy documentation

The previous documentation set is preserved under [`archive/legacy/`](archive/legacy/README.md).

It is historical reference material only and must not be treated as current contributor guidance.
