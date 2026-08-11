# Architectural principles

These are the rules contributors should preserve even when individual implementations change.

## 1. Keep Core independent from UI

`Files.Core` must not depend on XAML, WinUI controls, ViewModels, visual state, or presentation formatting. Presentation may depend on Core; Core must not depend on presentation.

## 2. Prefer progressive work over blocking completion

Large folders, remote providers, properties, thumbnails, and previews can be expensive. Publish useful state early and continue enrichment asynchronously instead of withholding the whole result.

## 3. Preserve stable identity

Selection, change reconciliation, property enrichment, thumbnail enrichment, and presentation state depend on stable item identity. Do not replace an item merely because additional metadata became available.

## 4. Make ownership explicit

Every disposable or asynchronous resource must have a clear owner responsible for lifetime, cancellation, cleanup, and disposal. See [`ownership-and-lifetime.md`](ownership-and-lifetime.md).

## 5. Treat cancellation as correctness

Work can become stale during navigation, enrichment, preview, provider access, and operations. Cancellation is cooperative, so a token alone is not enough: publication must also verify that the result still belongs to the active state/generation.

## 6. Keep provider-specific behavior behind provider boundaries

Provider differences should be represented by contracts/capabilities rather than provider-type checks in generic browse or presentation code.

## 7. Treat Windows Shell as a constrained subsystem

Shell APIs have COM apartment, message-pump, lifetime, and ordering requirements. Do not replace scheduler-mediated work with arbitrary `Task.Run` calls without proving the API is safe. See [`../subsystems/windows-shell.md`](../subsystems/windows-shell.md).

## 8. Preserve responsiveness before throughput

For interactive browsing, optimize in this order:

1. UI responsiveness;
2. time to first useful content;
3. stable interaction while background work continues;
4. total enumeration completion time;
5. background metadata completeness.

A smaller total duration does not justify noticeable UI-thread stalls.

## 9. Test contracts, not implementation accidents

Tests should protect externally meaningful behavior and architectural invariants while allowing harmless internal refactors.

## 10. Keep technical documentation with contract changes

Changes to layering, lifetime, concurrency, provider contracts, browse flow, operation boundaries, or performance expectations should update these docs in the same PR.
