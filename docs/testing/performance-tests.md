# Performance tests

Performance testing is split so deterministic architecture overhead is not confused with machine/environment-dependent Windows/UI behavior.

## Deterministic benchmarks

`tests/Files.Benchmarks` should measure stable architecture costs such as:

- capability resolution cold/cached behavior;
- thumbnail-cache operations;
- browse enumeration/projection/publication at representative sizes;
- allocations and notification counts.

Do not mix real network, Shell extension, disk-cache, or interactive UI noise into these micro/architecture benchmarks.

## Presentation performance

The UI test boundary should measure structural invariants and useful milestones such as:

- time to first presented item;
- time to first actually realized row;
- enumeration completion separately;
- dispatcher responsiveness/stall counts;
- collection notification count;
- ViewModel creation/churn;
- maximum realized container count/virtualization.

[Issue #5](https://github.com/0x5bfa/ReFiles/issues/5) tracks the end-to-end browse baseline for 100, 1,000, 10,000, and 44,000-item scenarios and real-folder measurement.

## CI gates

Prefer reliable structural assertions (correct result, stale-work rejection, bounded notifications, virtualization still active) over aggressive absolute wall-clock thresholds on noisy hosted runners.

Collect timing metrics as machine-readable artifacts first; tighten regression gates only after enough baseline data demonstrates stability.

## Real Windows folder scenarios

Shell/filesystem/property/thumbnail measurements should record environment metadata such as:

- Windows build;
- CPU and process architecture;
- provider/folder and item count;
- warm/cold cache state when known;
- enabled properties/thumbnails;
- relevant Shell extension/handler context.

These results are scenario measurements, not directly comparable to deterministic BenchmarkDotNet results.

## Interpretation

If Core publishes quickly but first WinUI realization is slow, optimize presentation/layout rather than Core enumeration. If the UI stays responsive while total enumeration is longer, do not sacrifice responsiveness solely to shorten the final completion number.
