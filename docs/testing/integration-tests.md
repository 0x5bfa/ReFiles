# Integration tests

Integration tests validate provider/platform behavior that cannot be proved with pure doubles.

## Windows filesystem and Shell

Use unique temporary directories/items and clean them in `finally`/disposal. Cover:

- resolution and stable filesystem identity;
- folder enumeration and streams;
- typed Shell property retrieval;
- thumbnail extraction;
- Shell scheduler apartment/concurrency behavior;
- change notifications;
- create, rename including case-only rename, copy, move, and permanent delete.

Tests sharing process-level Shell behavior should not run in parallel when isolation cannot be guaranteed.

## Archives

Use small committed deterministic fixtures covering supported formats/encryption modes, nested/synthetic folders, case-different names, malicious traversal entries, and non-seekable backing streams. Detect backend capability rather than assuming behavior only from OS version when possible.

Never expose fixture passwords in production telemetry/error paths.

## FTP

Use an isolated disposable server, not a public endpoint. Cover transport modes, authentication failure, feature fallbacks such as missing MLST, UTF-8/escaped names, non-seekable streams, recursive operations, cancellation, and server-specific case rules.

Never emit FTP passwords in test output or visible CI variables.

## Third-party handlers

Installed preview/property/Shell extensions are not deterministic CI dependencies. Keep representative third-party handler compatibility in manual/scenario tests unless the repository controls the handler fixture.
