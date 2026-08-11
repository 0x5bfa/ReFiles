# Archive storage

Archive support exposes archive contents through normal storage/provider contracts while isolating archive-library and Windows Shell implementation details.

## Responsibilities

- represent archive entries as storage items/folders;
- normalize and validate logical archive paths;
- provide stream access and supported operations;
- route to available archive backends/fallbacks;
- handle encryption/password retry through explicit contracts;
- preserve safe extraction/copy semantics.

## Security invariant: archive paths are untrusted

Archive entry names can contain traversal or ambiguous paths. Normalize paths and prevent entries from escaping the intended archive/extraction root. Never concatenate unvalidated entry names into filesystem destinations.

## Backend abstraction

Shell archive support and library-based formats/fallbacks should remain behind archive/provider contracts. Generic browsing must not care which backend produced the item.

## Streams and ownership

Archive readers may share backing streams/sessions. Define whether entry streams are independent, seekable, and allowed to outlive the archive session. Cleanup must work when opening an encrypted/corrupt entry fails partway through.

## Encryption

Credentials/passwords must never appear in logs, addresses, snapshots, diagnostics, or visible CI variables. Authentication retry is a control-flow result, not an excuse to retain credentials globally.

## Tests

Keep small deterministic fixtures for supported formats, encrypted variants, header encryption where relevant, nested/synthetic folders, case differences, traversal entries, and non-seekable backing streams. Test fallback/routing independently from OS-version guesses when capability detection can be used.
