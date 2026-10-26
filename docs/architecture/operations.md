# Operations architecture

The target architecture routes user-initiated storage I/O through `Files.BackgroundServer`. This includes read/query services as well as mutations. The server uses ordinary `Files.Core` providers internally; its exported facade uses WinRT-compatible contracts.

The app now uses `WinRTStorageOperationService` for copy/move/delete batches, rename/create requests, Shell New commands, and property attribute updates. Complete batches execute in the server. Browsing/enrichment reads, UI-owned Shell interactions, and application settings storage still use their existing owners.

## Goals

- keep the UI responsive during expensive file work;
- let accepted background operations finish after normal or unexpected UI termination;
- express intent through explicit contracts;
- make progress, cancellation, failure, and ownership observable;
- keep provider-specific execution behind provider/operation boundaries.

## Boundary

```mermaid
flowchart LR
    UI[UI / command]
    Client[Client adapter in Files]
    Host[Files.BackgroundServer]
    Provider[Files.Core providers]

    UI --> Client
    Client -->|WinRT| Host
    Host --> Provider
```

This is the target runtime call flow, not the project-reference graph. Both `Files` and `Files.BackgroundServer` reference `Files.Core`; Core does not reference the server. The client projection and adapter stay in `Files`, without another client project.

## Composition

A runtime has one execution owner. The main app supplies `WinRTStorageOperationService` with `UseStorageOperations` and registers client-side sources with `enableOperations: false`. Windows and FTP registration both support this option. Browsing a source does not imply that the configured server can mutate it.

The builder rejects mixing an external executor with local handlers in either registration order. It never silently discards provider handlers or falls back to in-process mutations. In-process hosts, including the background server, register local provider handlers instead. Additional server providers require server-side configuration and a corresponding execution contract before the client advertises those operations.

## Requests and background operations

All domain I/O belongs behind server services, but not every request is a retained Status Center operation:

| Work | Ownership and reporting |
| --- | --- |
| Enumeration, properties, thumbnail bytes, short queries | Client/browse-generation request; cancel when stale or disconnected; no durable history entry by default. |
| Copy, move, delete, extraction, transfers, other tracked user jobs | Submit complete intent to a server-owned operation; progress, outcome, and cancellation are observable after reconnecting. |
| Search or another potentially long read | Choose request or background-operation lifetime explicitly according to the feature. |
| Selection, visual models, image decoding, graphs, localization, HWND-bound UI | Presentation ownership; storage data comes through server services after migration. |

Do not make a universal remote Core object model. Transfer stable identities and value data; acquire provider objects in the server. Shell UI integration and native window contracts need individual designs rather than moving their objects across processes.

## Lifetime

The server independently holds activity leases for client connections, in-flight requests, and accepted operations. Disconnecting a client cancels its observation requests, not its operations. Operation execution uses a server-owned cancellation source.

With no activity leases, the server waits 30 seconds and exits. Acquiring a lease and entering shutdown are synchronized; an admitted operation prevents idle exit. New admission after shutdown begins fails so a client can reconnect. Retained terminal summaries do not keep the process resident.

The entire batch/item loop must move to the server. Remoting one item at a time while the ViewModel owns the loop does not survive UI termination.

Server-process failure, OS shutdown, and power loss are separate from UI failure. No replay or server-restart recovery is currently implemented. The later journal must record unknown/interrupted outcomes without automatically repeating mutations.

## Progress and cancellation

`IServerOperation` is the internal provider-independent execution contract. An implementation has an identity, a stable kind, and one complete asynchronous execution method with progress and a server-owned cancellation token. It can use Core types internally. It must continue using its provider's scheduler, including the Shell operation STA.

`OperationHost` owns execution, revisions, terminal outcomes, and cancellation. `OperationList` returns value summaries for any operation kind. Unavailable byte progress is explicit; a flag identifies whether byte counts describe the whole job or its current item. Cancellation requested and cancellation acknowledged are distinct: an operation that successfully completes despite a late cancellation request is successful.

The WinRT facade exposes `GetOperations()`, `WaitForOperationChangesAsync(instanceId, revision)`, `CancelOperation(id)`, and `ForgetOperation(id)`. Waiting is an `IAsyncAction`; after it completes, read a fresh operation list. This avoids a custom async generic result contract. Checking a revision and registering the waiter share the state lock. Waits are limited to 25 seconds and observations are spaced by at least 100 ms.

The in-memory host retains at most 256 active and terminal entries, and a storage batch contains at most 10000 items. Further admission fails explicitly until terminal entries are forgotten. Duplicate retained IDs are rejected; they do not trigger a second execution. Submission is typed (`SubmitStorageOperations`, `SubmitShellNewItem`, `SubmitAttributeChanges`, `SubmitPropertyChanges`), and a lost submission reply is never retried automatically.

Status Center projects server state through `IOperationStatusSource`. Its tracker samples graphs and formats local presentation data; it no longer owns execution, cancellation sources, or interruption decisions. Pause/resume requests and backend acknowledgements are distinct. Interruption replies include the displayed interruption ID, so an obsolete reply cannot answer a new conflict. Apply-to-all decisions belong to the server job.

Disconnecting the last client leaves ordinary execution running. If a job is paused or awaiting a decision with no reconnecting client for two minutes, the host requests cooperative cancellation rather than leaving it resident indefinitely.

## Failure

Operation results should distinguish success, cancellation, unsupported behavior, and failure without requiring presentation to decode low-level implementation details. Preserve enough diagnostic information for logs/troubleshooting without exposing secrets.

## Provider boundary

Generic operation intent should not contain Windows Shell, FTP, archive, or other backend-specific implementation logic. Providers/operation executors decide how an intent is fulfilled.

## Tests

Protect operation routing, enum/input validation, progress/result invariants, cancellation, cleanup, provider fallback behavior, and process-boundary contracts where practical.

## Names and source files

`OperationList` means an immutable value copy of the operation summaries at one revision. It is not a second operation owner or a live model shared with the server. The client materializes its values once, and Status Center displays local `StorageOperationStatus` records. Each introduced type has its own source file; owned acronym names use `WinRT`, `WinUI`, and `UI`.

## Shared composition and headless observation

`UseStorageOperations` injects the execution boundary into both `FilesCoreRuntime.StorageOperations` and every browse application model. The app configures its WinRT implementation once. `Jobs.SubmitBatchAsync` transfers the complete selection to an independent executor and returns its accepted identity; its work remains server-owned. The default local batch implementation snapshots and preflights all requests, then stops at the first execution failure. It does not promise atomicity or rollback of completed requests.

`IStorageOperationMonitor` exposes snapshots, cancellation, pause/resume, interruption responses, and terminal-history removal without UI dependencies. `StorageOperationSnapshot` contains execution data and acknowledgements. Transfer-rate smoothing, estimated time remaining, localized text, and graph points remain presentation data.

The server's `StorageOperationExecutor` owns a configured `FilesCoreRuntime` and routes through its operation service rather than invoking a Windows handler directly. The default server configuration still registers Windows storage. Additional providers must be configured in the server explicitly; source identity alone does not establish a remote connection or transfer client-owned provider objects.

This restructuring does not add crash replay, durable journals, automatic retry of uncertain submissions, or a universal cross-source transfer implementation. Unsupported transfers continue to fail through explicit provider routing. These remain separate feature protocols; no mutation is silently retried or downgraded to a stream copy.

## Job acceptance and observation

`IStorageOperationService.ExecuteAsync` and `ExecuteBatchAsync` describe inline execution. Independent executors expose `Jobs` and reject inline execution. `IStorageJobService.SubmitBatchAsync` accepts a caller-selected ID and returns a `StorageJobHandle` after acceptance, before completion. Cancellation applies before dispatch; an in-flight acceptance call is awaited without automatically retrying it.

`WaitForCompletionAsync` observes that handle. Canceling observation does not send job cancellation. `StorageJobObservationCanceledException` retains the accepted handle; the same job can be observed again. Explicit cancellation continues through `IStorageOperationMonitor.Cancel` and the server acknowledgement.

`StorageJobUnknownException.Accepted` distinguishes a lost acceptance reply from an unknown outcome after confirmed acceptance. Both retain the job and server identities. A lost reply, disconnected server, restarted server, or forgotten result is not an operation failure and must not trigger automatic resubmission. There is no restart recovery or durable journal implied by this contract.

Application flows that need completion use the explicitly named `ExecuteAndWaitAsync` convenience workflow. Callers needing an immediate receipt can submit through `Jobs` directly and retain the handle independently of a pane's lifetime.
