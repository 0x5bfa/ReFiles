# Coding guidelines

Repository configuration such as `.editorconfig`, project analyzers, compiler warnings, and CI is the source of truth for formatting and enforceable style. This document covers architecture-oriented coding practices that are harder to express as analyzers.

## Prefer explicit contracts

Make ownership, cancellation, unsupported behavior, and capability presence visible in APIs. Avoid hidden global state or conventions that require knowledge of one implementation.

## Async code

- propagate cancellation when the operation can become stale;
- do not block UI-sensitive code on asynchronous cleanup/work;
- reject late results against the active generation/state;
- keep backend-specific apartment/threading requirements behind schedulers/providers;
- avoid `async void` except event-handler boundaries.

## Collections and hot paths

Choose data structures from access patterns. Frequent identity lookup should not repeatedly scan a 44k-item list if an index/key map belongs in the owning subsystem. Avoid defensive copying/snapshot allocation in high-frequency paths unless the contract requires it.

## UI boundary

Do not introduce WinUI/XAML dependencies into `Files.Core`. Keep localized formatting and visual resource construction in presentation.

## Provider boundary

Avoid type checks for specific providers in generic code. Add/extend a capability when the behavior is genuinely optional/backend-defined.

## Resource lifetime

Use `using`/`await using` where ownership is local and explicit. When ownership transfers, document it. Unsubscribe events and cancel owned work as part of disposal.

## Comments and XML documentation

Comments should explain non-obvious invariants, lifetime/threading requirements, or why a simpler-looking implementation would be incorrect. Do not narrate obvious code.

Public/internal contracts that are easy to misuse should document ownership, cancellation, threading, and failure behavior close to the API.

## Documentation

When a code change alters a contract described under `docs/`, update the corresponding document in the same PR.
