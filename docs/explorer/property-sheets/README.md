# Explorer property sheets

Explorer property sheets are a composition of pages supplied by `shell32.dll` and specialized Shell extension providers. The provider returns an `HPROPSHEETPAGE`; Common Controls owns the containing sheet, tab control, page activation, Apply/Cancel state, and destruction.

This investigation covers the standard filesystem pages shown below. Select a page for its text-based UI region map, exact provider entry points, data APIs, write path, and ownership notes.

## Page catalog

| Page | Primary provider | Page entry point | Investigation |
| --- | --- | --- | --- |
| General | `shell32.dll` | `FileSystem_AddPages` | [General](general.md) |
| Shortcut | `shell32.dll` | `AddLinkPage` | [Shortcut](shortcut.md) |
| Sharing | `ntshrui.dll` | `CShare::AddPages` | [Sharing](sharing.md) |
| Security | `rshx32.dll` + `aclui.dll` | `CSecurityExtension::AddPages` → `CreateSecurityPage` | [Security](security.md) |
| Previous Versions | `twext.dll` | `CTimeWarpProp::AddPages` | [Previous Versions](previous-versions.md) |
| Customize | `shell32.dll` | `CFolderCustomize::AddPages` | [Customize](customize.md) |
| Digital Signatures | `cryptext.dll` + `cryptui.dll` | `CCryptSig::AddPages` → `CryptUIGetViewSignaturesPagesW` | [Digital Signatures](digital-signatures.md) |
| Details | `shell32.dll` | `CSummaryPage::AddPages` | [Details](details.md) |

## Window construction

The pages are not independent top-level windows. Providers describe dialog pages, while Common Controls creates and hosts them. See [Property-sheet window construction](construction.md) for the complete path from `CreatePropertySheetPageW` and `PropertySheetW` to `CreateDialogIndirectParamW`.

## Screenshot notation

Each page document contains an unmodified live Windows property-page screenshot. A text-only UI region map below the image identifies:

- the internal DLL and function responsible for the region;
- the public or private API used to obtain or change its data;
- any allocator, handle, COM, or persistence requirement;
- the recommended ReFiles boundary.

The original captures are retained in [`images/source/`](images/source/). Documentation must not draw on, redact, resample, or otherwise modify these source images.

> [!NOTE]
> Paths, account names, dates, and signatures in the captures are sample data. Their presence does not affect the identified code path.

## Analysis set

| Module | Analyzed version | Image base |
| --- | --- | --- |
| `shell32.dll` | `10.0.26100.8521` | `0x180000000` |
| `ntshrui.dll` | `10.0.26100.8117` | `0x180000000` |
| `rshx32.dll` | `10.0.26100.1` | `0x180000000` |
| `aclui.dll` | `10.0.26100.8115` | `0x180000000` |
| `twext.dll` | `10.0.26100.8117` | `0x180000000` |
| `cryptext.dll` | `10.0.26100.1` | `0x180000000` |
| `cryptui.dll` | `10.0.26100.1` | `0x180000000` |
| Common Controls v6 `comctl32.dll` | `10.0.26100.8972` | `0x180000000` |

The capture host reports Windows NT `10.0.26200`; serviced system components can retain `10.0.26100.x` file versions. Addresses in each page refer to the analyzed module version, not the host OS version.

## Scope boundaries

This documentation describes built-in filesystem property pages. A third-party `IShellPropSheetExt` can add other pages with arbitrary code and UI. ReFiles does not need to instantiate those providers to reproduce the built-in data views, and doing so would reintroduce in-process extension risk and transient native-window behavior.

## Related content

- [Drive property sheets](../drive-property-sheets/README.md)
- [Windows Explorer implementation notes](../README.md)
