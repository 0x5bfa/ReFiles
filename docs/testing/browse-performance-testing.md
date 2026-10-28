# Browse performance testing

ReFiles has two complementary browse performance paths:

1. deterministic synthetic scenarios that run with the normal WinUI test suite; and
2. opt-in real Windows folder scenarios that intentionally include Windows storage, Shell, filesystem, cache, and machine-specific behavior.

These scenarios measure the production browse presentation path through an actual realized `TableView` row. They are not replacements for `Files.Benchmarks`, which remains the place for isolated microbenchmarks.

## What the synthetic scenarios exercise

The synthetic performance test uses the production path:

```text
Synthetic browse provider
    -> BrowseSession
    -> BrowsePresentationAdapter
    -> FolderBrowserViewModel
    -> CollectionViewSource
    -> DetailsFolderView
    -> TableView
    -> WinUI row realization
```

The baseline scenarios are:

- Details / 100 items
- Details / 1,000 items
- Details / 10,000 items
- Details / 44,000 items
- Details / 44,000 items with progressive property enrichment

Thumbnails are intentionally excluded from the deterministic baseline. Thumbnail decoding, Shell thumbnail handlers, filesystem cache state, and installed extensions are machine-dependent and would obscure the cost of the browse and presentation pipeline.

## Metrics

Each scenario records:

- time to first Core batch;
- time to first presentation item;
- time to first actually realized WinUI row;
- time to enumeration completion;
- time to the first property and thumbnail presentation update;
- time until properties and thumbnails are ready for the initially realized items;
- repeated thumbnail source updates, separated from fallback-to-content upgrades, that can indicate visible icon flashing;
- maximum UI-dispatcher latency;
- p95 UI-dispatcher latency;
- UI stalls over 16, 50, and 100 ms;
- dispatcher probe count;
- collection notification count;
- number of unique browse item ViewModels observed;
- maximum realized row count.

The primary user-visible timing is **time to first realized row**.

`Items.Count > 0` is not considered proof that content reached the screen. The test observes row realization from the production `TableView` rows host.

## Hard invariants and informational timings

The initial baseline deliberately avoids strict hosted-runner timing thresholds.

Structural invariants may fail the test, including:

- the final item count is incorrect;
- a synthetic first row is not realized before enumeration completes;
- the 44,000-item scenario realizes an excessive number of rows and loses virtualization;
- presentation creates replacement ViewModels for already-published synthetic items;
- collection notifications become obviously unbounded;
- a catastrophic UI-thread stall exceeds the deliberately loose safety threshold.

Absolute values such as `TimeToFirstRealizedRowMs`, total enumeration duration, and p95 dispatcher latency are recorded in JSON so variance can be understood before stable regression thresholds are introduced.

## CI results

Normal CI runs the synthetic scenarios as part of `Files.UITests` and uploads the generated JSON as the `browse-performance-<commit>` artifact.

The JSON destination can be controlled with:

```text
FILES_PERF_RESULTS_DIR
```

When the variable is not supplied, the test writes under the process temporary directory.

## Running the synthetic scenarios locally

Build the UI test project for a Windows architecture, then run its self-hosted WinUI test application with Microsoft Testing Platform in the same way as CI.

To run only the deterministic performance category, use a test filter equivalent to:

```powershell
dotnet run --project tests/Files.UITests/Files.UITests.csproj `
    --configuration Debug -p:Platform=x64 `
    -- --filter "TestCategory=Performance"
```

The test host is a self-hosted, unpackaged WinUI application. A separate temporary window hosts `DetailsFolderView` so layout and row realization are included in the measurement.

## Real Windows folder scenarios

Real-folder measurements intentionally include environment-dependent behavior such as:

- Windows storage and Shell integration;
- filesystem enumeration and identity work;
- Shell/COM scheduling;
- property and thumbnail handlers;
- disk and filesystem cache state;
- installed Shell extensions;
- production presentation and WinUI row realization.

They are therefore scenario measurements rather than deterministic unit/benchmark results.

The repository provides the **Browse performance** GitHub Actions workflow. Run it manually with `workflow_dispatch` and provide:

- `folder` — the folder to measure, defaulting to `C:\Windows\WinSxS`;
- `iterations` — number of measurements, defaulting to 3;
- `environment_notes` — optional machine/cache/Shell notes.

The workflow uploads a separate `browse-real-folder-<run>` artifact.

### Running a real folder locally

Set these variables before running the `RealFolderPerformance` test category:

```powershell
$env:FILES_PERF_REAL_FOLDER = 'C:\Windows\WinSxS'
$env:FILES_PERF_ITERATIONS = '5'
$env:FILES_PERF_RESULTS_DIR = "$PWD\artifacts\performance"
$env:FILES_PERF_ENVIRONMENT_NOTES = 'Warm-cache local developer run'
```

To measure several folders in one run, set `FILES_PERF_REAL_FOLDERS` to a semicolon-separated list. It takes precedence over `FILES_PERF_REAL_FOLDER`.

Then run the self-hosted UI test application with:

```powershell
dotnet run --project tests/Files.UITests/Files.UITests.csproj `
    --configuration Debug -p:Platform=x64 `
    -- --filter "TestCategory=RealFolderPerformance"
```

If `FILES_PERF_REAL_FOLDER` is absent, the real-folder test is reported as inconclusive instead of running accidentally during normal CI.

## Environment metadata

Real-folder JSON includes machine/environment context alongside the scenario result, including:

- Windows version;
- process architecture;
- processor identifier when available;
- available memory reported by the runtime;
- target folder;
- cache-state hint (`unknown` for the first navigation and `warm` for subsequent refreshes);
- property/thumbnail enablement;
- optional environment notes.

Do not compare real-folder numbers from different machines as if they were deterministic benchmark results.

## Using the results

Use the measurements to decide which layer to investigate next.

For example:

- fast Core batch + slow first realized row suggests presentation/layout work;
- slow first Core batch suggests provider/enumeration/identity work;
- good total duration + large dispatcher latency suggests UI responsiveness problems;
- unexpectedly high realized-row count suggests a virtualization regression.

Optional ETW/WPR tracing for deeper root-cause analysis is tracked separately. Normal performance regression testing does not require generating an ETL file or opening WPA.

Related issues: #5, #8.
