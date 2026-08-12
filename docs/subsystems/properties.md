# Property retrieval

Properties enrich a storage item with typed metadata such as size, dates, attributes, or provider-defined values.

## Contract

Property semantics and retrieval belong below presentation. Localized column labels and human-readable formatting belong in `Files`.

Do not make Core return display strings simply because a Details column needs them.

## Progressive enrichment

Rows should be publishable from identity/basic data before expensive property retrieval finishes.

```text
item identity/basic data
    -> publish row
    -> determine visible/required properties
    -> retrieve properties
    -> update the existing row
```

Property enrichment should preserve item/ViewModel identity.

## Prioritization

Prefer properties needed for visible rows, sorting/grouping, or currently displayed columns. Avoid querying every possible property for every item up front.

## Sorting/grouping

A sort/group key may not be available immediately. The browse pipeline must tolerate incomplete metadata and reconcile ordering without repeatedly monopolizing the UI thread.

## Provider behavior

Property types and cost differ by provider. Windows Shell property handlers may be expensive or third-party; remote providers may require network requests. Keep retrieval bounded and cancellable.

## Stale results

Before publishing an async property result, verify that the item/content/generation it describes is still current. Cancellation alone does not guarantee this.

## Tests

Cover typed values, missing properties, progressive updates to stable rows, stale-result rejection, sort/group reconciliation, provider failures, and cancellation.
