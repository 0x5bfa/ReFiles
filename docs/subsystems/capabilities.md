# Capabilities

Capabilities describe optional behavior that a storage item can support without forcing every provider into one monolithic interface.

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
2. Capability discovery remains UI independent.
3. Provider-specific implementation stays behind the capability contract.
4. Capability lifetime/ownership is explicit.
5. Resolution should avoid unnecessary allocation/work on hot paths.
6. A capability result must not publish into stale browse state merely because resolution completed.

## Composition

A storage item may obtain capabilities from one or more contributors/providers and compose them according to the subsystem contract. Composition rules must be deterministic: contributors should know which implementation wins or how results combine.

Discovery and registration accept only interfaces derived from `ICapability`. Generic constraints exclude unrelated types such as `object` and `IStorable`; runtime validation additionally rejects implementation classes and the marker itself. Invalid requests throw `ArgumentException` before cached resolution. A valid but unavailable capability returns null.

Implementing a capability on a CoreModel does not register it. Providers explicitly register factories through `CapabilityBuilder`; the registry combines their results and then applies wrappers. FTP and archive modules register their content factories once per builder. The transfer module also registers FTP content so operation hosts can resolve sources added after runtime construction. An unbound `ItemContext.GetCapability<T>()` returns null instead of casting its CoreModel. Provider code that needs composed dependencies must use the context supplied during resolution.

Custom capability interfaces must derive from `ICapability`, and registrations/queries must use the interface rather than its implementation type. This is an API compatibility change. The marker identifies a capability contract; it does not authorize provider code. Built-in results are projected through dedicated borrowed adapters after all composition, so a read result cannot be cast to writing, raw storage or disposal interfaces. Internal property batching and pinned/nested archive input remain available to Core. Custom interfaces require their own narrow borrowed implementations; arbitrary custom interfaces are not automatically projected.

## Ownership and borrowed access

`IHasCapabilities.Capabilities` exposes an `ICapabilities` facade without disposal or owning-collection interfaces. `IOwnedCapabilities` is the owned composition result transferred into model construction; the model retains it privately and disposes it before its storage item. Borrowed built-in capabilities similarly hide implementation disposal interfaces. `IFolderChangeSource` has no disposal members, and event senders refer to its borrowed facade.

Streams, preview results and independently opened archive models remain caller-owned. Read capabilities wrap writable streams to enforce read-only access while preserving reads, seeking, cancellation and underlying stream disposal. Capability projection stays lazy and cached per item; it does not replace the registry's ownership tracking of factory, combiner and wrapper instances.

## Lazy resolution

Expensive capability objects should generally be resolved lazily. Browsing tens of thousands of items must not eagerly allocate every possible capability/provider object for every row.

## Presentation boundary

Core exposes the semantic result. Presentation converts it into display state. For example, Core may expose thumbnail bytes/streams while `Files` constructs WinUI image objects.

## Adding a capability

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
