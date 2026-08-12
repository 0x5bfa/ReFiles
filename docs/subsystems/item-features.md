# Item features and capabilities

Item features describe optional behavior that a storage item can support without forcing every provider into one monolithic interface.

## Why capabilities

Filesystems, Shell namespaces, archives, FTP, and future remote providers do not have identical capabilities. Generic code should ask an item for the behavior it needs instead of switching on provider type.

Examples include:

- property retrieval;
- thumbnails;
- preview;
- readable/streamable content;
- hierarchy;
- operations;
- search;
- change observation.

## Invariants

1. Capability absence is normal and must be handled.
2. Feature discovery remains UI independent.
3. Provider-specific implementation stays behind the feature contract.
4. Feature lifetime/ownership is explicit.
5. Resolution should avoid unnecessary allocation/work on hot paths.
6. A feature result must not publish into stale browse state merely because resolution completed.

## Composition

A storage item may obtain features from one or more contributors/providers and compose them according to the subsystem contract. Composition rules must be deterministic: contributors should know which implementation wins or how results combine.

## Lazy resolution

Expensive feature objects should generally be resolved lazily. Browsing tens of thousands of items must not eagerly allocate every possible feature/provider object for every row.

## Presentation boundary

Core exposes the semantic result. Presentation converts it into display state. For example, Core may expose thumbnail bytes/streams while `Files` constructs WinUI image objects.

## Adding a feature

Before adding a new capability, define:

- what semantic behavior it represents;
- whether it is optional;
- ownership of returned resources;
- cancellation/concurrency behavior;
- composition/priority when multiple contributors exist;
- how providers can implement it without UI dependencies;
- tests for missing, single, multiple, failure, and cancellation cases.

## Tests

Protect lazy resolution, caching if applicable, contributor priority/composition, ownership, async disposal, construction failure cleanup, and absence of eager per-item work.
