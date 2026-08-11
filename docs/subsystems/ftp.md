# FTP storage

FTP support adapts FTP/FTPS servers to the common storage/provider model. Network behavior and server differences stay inside the provider.

## Responsibilities

- normalize FTP addresses/paths and preserve root containment;
- manage authenticated sessions and reconnect/retry behavior;
- enumerate directories progressively;
- expose streams, properties, hierarchy, and supported operations;
- handle explicit/implicit TLS according to configuration;
- translate server errors into provider outcomes.

## Identity and paths

Do not assume all servers share Windows path or case semantics. Preserve provider/server rules and ensure normalization cannot escape the configured logical root.

## Network behavior

Never block the UI thread on FTP I/O. Operations must honor cancellation/timeouts and release sessions/streams after failures. Limit concurrency according to session/server capabilities rather than opening an unbounded connection per item.

## Feature detection

Servers vary in supported commands such as MLST and in encoding behavior. Detect/fallback inside the provider rather than scattering server-feature checks through generic browse code.

## Security

- prefer protected transport when configured;
- never log passwords;
- do not embed credentials in display addresses, snapshots, or test output;
- validate certificate/TLS behavior according to the product's security policy;
- keep disposable test servers isolated from public endpoints.

## Tests

Unit tests should use `IFtpSession`-style doubles and require no network. Integration tests should use an isolated disposable server and cover plain FTP, TLS modes, auth failure, missing MLST, UTF-8/escaped names, non-seekable streams, recursive operations, cancellation, and server case behavior.
