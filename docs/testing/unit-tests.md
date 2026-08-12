# Unit tests

`tests/Files.UnitTests` is the main deterministic correctness boundary for UI-independent contracts.

## Prefer test doubles for external systems

Use controlled doubles for provider sessions, storage models, preview controllers, operation executors, and other environmental dependencies when testing generic logic.

## Important contract areas

Unit tests should protect behavior such as:

- item-feature resolution/composition/ownership;
- thumbnail cache invalidation and eviction logic;
- preview stream ownership/cancellation/limits;
- reference navigation, projection, selection, and prefetch policy;
- application/window/tab/pane lifetime;
- async disposal and cleanup failure aggregation;
- operation routing/result/progress invariants;
- archive path safety/backend routing/authentication flow;
- FTP normalization/root containment/session ownership;
- storage identity/reference equality.

## Ownership in tests

If a test creates an owned model/resource and does not transfer it into a session/owner, the test is responsible for disposing it. Prefer `await using` for async disposable objects.

## Avoid brittle tests

Do not assert private list types, exact internal call counts, or specific batch constants unless those details are themselves a required performance/correctness contract. Test observable invariants instead.

## Windows-dependent behavior

A test using actual Shell/filesystem APIs is an integration test even if it lives in the same project. Keep its setup/cleanup and parallelization rules explicit; see [`integration-tests.md`](integration-tests.md).
