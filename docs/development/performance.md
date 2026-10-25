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

## Append projection costs

Append-only enumeration updates the key-to-index map for the appended range. It does not rebuild indices for previously published rows. Sorting and structural edits retain their explicit reindexing costs.

`IBrowseSession.ItemCount` provides a count without forcing an immutable item snapshot. Diagnostics use this count; a log message must not trigger a full collection copy. `Items` remains a cached immutable snapshot, rebuilt only when requested after a mutation. Consumers should use keyed lookup or `ItemCount` when they do not need the snapshot.

Compare Release `--browse-scenario` results before and after projection changes. Report these as synthetic Core timings, separately from actual Shell enumeration and WinUI row realization. The architecture refactoring retains bounded publication and does not imply a measured improvement in real-folder rendering.

### Local refactoring measurements (2026-10-26)

Windows 10.0.26300, x64, Intel Family 6 Model 170. Release Core scenarios report the median of five runs. The before measurement was taken before incremental append indexing and count-only diagnostics; these figures are informational, not a regression gate.

| Core scenario | Before total (ms) | After total (ms) | After first batch (ms) | After notifications |
| --- | ---: | ---: | ---: | ---: |
| 10,000 items | 20.86 | 19.74 | 0.085 | 12 |
| 44,000 items | 154.27 | 131.55 | 0.069 | 46 |

Use `--browse-scenario --large` for 10,000, 100,000, and 1,000,000 items. A separate run measured 268.36 ms / 80,729,968 allocated bytes for 100,000 items and 3,150.68 ms / 783,709,984 allocated bytes for 1,000,000 items. First batches were 0.102 ms and 0.104 ms, with 100 and 980 notifications respectively. These scenarios include enumeration and final ordering; they do not isolate every sorting or snapshot cost.

The Debug WinUI synthetic scenario also passed through real row realization. In the final local run, 44,000 items produced the first realized row in 15.95 ms, completed enumeration in 489.22 ms, and realized at most 24 rows. With property enrichment, those values were 15.69 ms, 570.48 ms, and 26 rows. There is no equivalent before-run UI measurement, and no real-folder throughput improvement is claimed.
