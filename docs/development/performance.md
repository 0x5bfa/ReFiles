# Performance contract

ReFiles is an interactive file manager. Performance work is judged first by **responsiveness and time to useful UI**, not only by total throughput.

## Priorities

For browse/navigation work, optimize in this order:

1. UI responsiveness;
2. time to first useful/realized content;
3. stable interaction while enumeration/enrichment continues;
4. total enumeration completion time;
5. background metadata completeness.

> Do not trade a noticeable UI-thread stall for a small improvement in total enumeration duration.

## Critical path

Initial rows should require only the information needed to identify/present basic items. Properties, thumbnails, preview, and other optional metadata should be progressive enrichment when possible.

## Bounded work

Avoid both extremes:

- one dispatcher notification per item;
- giant UI batches that monopolize the dispatcher.

Prefer bounded/coalesced publication and measure callback duration/latency.

## Large collections

Hot-path warning signs include:

- O(N) full scans on every batch/selection query;
- full snapshot copies per append;
- complete regroup/resort per tiny mutation;
- eager capability dictionaries/resources for tens of thousands of items;
- ViewModel recreation for metadata-only changes;
- virtualization being disabled accidentally.

## Concurrency

More parallelism is not automatically faster. Shell handlers, disk access, decoding, network providers, and UI publication all have different contention limits. Keep concurrency bounded and profile the complete pipeline.

## Measurement layers

### Deterministic Core benchmarks

`tests/Files.Benchmarks` measures architecture/Core overhead without mixing in Shell, disk, network, or UI-rendering noise.

### Presentation/UI contract tests

`tests/Files.UITests` protects incremental presentation and WinUI/control behavior.

### End-to-end browse baseline

[Issue #5](https://github.com/0x5bfa/ReFiles/issues/5) tracks measurements through real WinUI row realization, including time-to-first-row, dispatcher responsiveness, notification/ViewModel counts, and virtualization for large synthetic folders.

### Real-folder scenarios

Windows/Shell/filesystem measurements must record environment metadata and remain separate from deterministic benchmark numbers. Hosted-runner timings should initially be informational rather than brittle release gates.

## Before optimizing

1. Reproduce.
2. Identify the slow stage.
3. Measure UI-thread stalls as well as total time.
4. Change one architectural cost at a time.
5. Verify allocation/notification/realization counts.
6. Confirm correctness, cancellation, and lifetime behavior did not regress.
