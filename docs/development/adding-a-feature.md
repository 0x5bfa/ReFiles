# Adding a feature

Use this checklist before implementation. The goal is to add behavior without eroding layer, ownership, and performance boundaries.

## 1. Define the behavior, not the UI

Write down what the feature means independently of how it is rendered. If it is meaningful without WinUI, its semantic contract probably belongs at or below Core.

## 2. Identify the owner

Decide which subsystem owns:

- state;
- lifetime/disposal;
- cancellation;
- failures;
- persistence, if any;
- provider-specific behavior.

Use [`../architecture/layering.md`](../architecture/layering.md) and [`../architecture/ownership-and-lifetime.md`](../architecture/ownership-and-lifetime.md).

## 3. Decide whether behavior is optional

If only some storage items/providers support it, prefer an item feature/capability instead of expanding a universal interface or adding provider switches.

## 4. Define async/concurrency behavior

Ask:

- Can this block?
- Which thread/apartment may execute it?
- Can multiple instances run concurrently?
- What becomes stale after navigation/selection changes?
- Who cancels and who rejects late results?

## 5. Preserve progressive UI

Do not put optional expensive work on the critical path to first useful content. For browsing features, consult [`performance.md`](performance.md).

## 6. Add tests at the contract boundary

Prefer deterministic tests for semantics. Add Windows/UI/scenario tests only for behavior that actually depends on those environments.

## 7. Update docs

If the feature changes ownership, layering, a subsystem invariant, or an extension contract, update that technical doc in the same PR.

## Review checklist

- [ ] No UI dependency leaked into Core/provider code.
- [ ] Ownership and disposal are explicit.
- [ ] Cancellation and stale-result behavior are defined.
- [ ] Provider-specific behavior is isolated.
- [ ] Hot paths avoid unnecessary full scans/eager work.
- [ ] Tests protect the behavior rather than private implementation.
- [ ] Relevant contributor docs are updated.
