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

## Item property sheets

The ReFiles property window hosts dynamically discovered sheets in a `SelectorBar`. Every built-in sheet has a dedicated XAML-backed WinUI `UserControl`; code-behind is limited to data adaptation and interaction handlers. Controls are cached by page kind for the lifetime of the window, and tab changes only switch visibility. Filesystem size and containment are loaded asynchronously, while rename and attribute changes are applied only after the user chooses OK or Apply. Local drive roots add Tools, Hardware, Sharing, Security, Previous Versions, and Quota only when the same underlying drive type, policy, filesystem, and capability checks used by Explorer permit them.

The built-in page list is derived from item capabilities and selection shape rather than by reading registration keys or briefly creating the system property dialog. Executables and shortcuts that resolve to a valid Windows executable add Compatibility after `GetBinaryTypeW` validates the target. ReFiles reads each implemented page through a typed Windows service and renders the result in WinUI. It does not load arbitrary `IShellPropSheetExt` implementations merely to discover their titles.

Previous Versions uses the filesystem snapshot control path and Explorer's local administrative-share fallback to resolve `@GMT-*` snapshot items. An unavailable snapshot provider is represented as an empty page, not as a native dialog probe.

The reverse-engineered ownership, data APIs, and native window-construction chain are documented in [`explorer/property-sheets/README.md`](../explorer/property-sheets/README.md). That material is evidence for compatibility work; undocumented module addresses and private COM layouts must remain outside the presentation layer.

## Stale results

Before publishing an async property result, verify that the item/content/generation it describes is still current. Cancellation alone does not guarantee this.

## Tests

Cover typed values, missing properties, progressive updates to stable rows, stale-result rejection, sort/group reconciliation, provider failures, and cancellation.
