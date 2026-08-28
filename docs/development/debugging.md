# Debugging ReFiles

Debug the narrowest layer that can reproduce the problem, then move outward only when necessary.

## Browse problems

Separate the pipeline into milestones:

1. location resolution;
2. provider enumeration;
3. Core projection/publication;
4. presentation adapter/UI dispatch;
5. control realization/layout;
6. property/thumbnail enrichment.

A folder that resolves/enumerates quickly can still feel slow if presentation realization or enrichment blocks the UI.

## Cancellation/race problems

Record identity plus navigation/content generation/current-state information. A late async result is suspicious when it publishes after navigation, selection, rename, refresh, or content replacement.

## Windows Shell problems

Verify which scheduler lane/apartment executes the call before blaming the API itself. COM objects may be apartment-bound; capture stable identity/data rather than moving raw objects across arbitrary async boundaries.

Use debugger/ETW/WPR/WPA tooling for hangs or expensive Shell/property/thumbnail paths when simple timings are insufficient.

## Lifetime/leak problems

Look for:

- event subscriptions retaining panes/ViewModels;
- background tasks retaining a browse generation;
- streams/capability objects without clear owners;
- COM objects cached longer than their apartment/session lifetime;
- sync-over-async disposal;
- operation/session ownership surviving unexpectedly.

## Performance problems

Do not optimize from a single total-duration stopwatch. Use the performance boundaries in [`performance.md`](performance.md) and issue #5 to determine whether the bottleneck is Core, presentation, UI realization, Shell, or enrichment.

## Reproduction notes

For Windows/Shell performance or behavior, record OS build, architecture, folder/item count, cache state when relevant, provider, installed Shell extensions/handlers if relevant, and whether properties/thumbnails were enabled.
