# Search subsystem

Search is an optional storage capability that produces item results without requiring generic code to know how a provider performs discovery.

## Contract

A provider may implement search locally, remotely, through an index, or not at all. Generic search/browse presentation consumes a common result contract and capability presence.

## Invariants

- search is optional;
- results preserve normal storage identity/reference semantics;
- results can be streamed/progressively consumed where possible;
- cancellation stops stale queries and stale results cannot publish;
- provider query syntax does not leak into generic UI unless intentionally exposed as a provider-specific advanced feature.

## Result integration

Search results should reuse the normal item capability/property/thumbnail model rather than inventing a parallel presentation-only item type.

## Performance

Do not require complete result materialization before showing results when the backend supports progressive results. Apply the same responsiveness priorities as folder browsing.

## Tests

Cover unsupported search, progressive results, cancellation/query replacement, stable identity, provider errors, and enrichment of search-result items.
