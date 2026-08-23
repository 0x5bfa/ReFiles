# Explorer drive property sheets

Explorer composes a drive property dialog from three built-in `shell32.dll` pages and capability-dependent Shell extension pages. The captured NTFS system drive exposed General, Tools, Hardware, Sharing, Security, Previous Versions, and Quota.

## Page catalog

| Page | Primary UI owner | Page entry point | Investigation |
| --- | --- | --- | --- |
| General | `shell32.dll` | `CDrives_AddPages` / `CDrives_AddPagesForMountedVolume` | [General](general.md) |
| Tools | `shell32.dll` | `CDrives_AddPagesHelper` → `_DiskToolsDlgProc` | [Tools](tools.md) |
| Hardware | `shell32.dll` + `devmgr.dll` | `_DriveHWDlgProc` → `DeviceCreateHardwarePageEx` | [Hardware](hardware.md) |
| Sharing | `ntshrui.dll` | `CShare::AddPages` | [Sharing](sharing.md) |
| Security | `aclui.dll` plus an `ISecurityInformation` provider | `CreateSecurityPage` | [Security](security.md) |
| Previous Versions | `twext.dll` | `CTimeWarpProp::AddPages` | [Previous Versions](previous-versions.md) |
| Quota | `dskquoui.dll` | `DiskQuotaPropSheetExt::AddPages` | [Quota](quota.md) |

See [Drive property-sheet construction](construction.md) for page order, eligibility checks, dialog resources, and the handoff to Common Controls.

## Screenshot policy

Each page article contains an unmodified `PrintWindow` capture of the live `Local Disk (C:) Properties` window. UI regions are identified in text tables; the images are not cropped, annotated, redrawn, or resampled.

The source files are retained in [`images/source/`](images/source/).

> [!NOTE]
> Volume labels, capacity, free space, device model, accounts, SIDs, and snapshot dates are sample data from the analysis host.

## Conditional pages

The seven captured tabs are not a universal fixed list.

| Condition | Effect |
| --- | --- |
| `DRIVE_NO_ROOT_DIR` or `DRIVE_REMOTE` | The built-in Tools and Hardware pages are not added by `CDrives_AddPagesHelper`. |
| `REST_NOHARDWARETAB` policy | Hardware is suppressed. |
| WOW64 property host | Hardware is suppressed by this `shell32.dll` build. |
| Sharing unavailable or inapplicable | `ntshrui.dll` does not add Sharing. |
| Object is not securable | No ACLUI Security page is added. |
| No snapshot/File History provider | Previous Versions can be absent or empty. |
| Filesystem has no disk-quota support | `dskquoui.dll` does not add Quota. |
| Removable-media and servicing-dependent extensions | Other pages, historically including ReadyBoost, can be added or removed independently. ReadyBoost was not present on the captured fixed NVMe drive. |

ReFiles should derive built-in page availability from drive capabilities and selection shape. The presence of a page on one volume must not become a global constant.

## Analysis set

| Module | Analyzed or observed version | Image base | Role |
| --- | --- | --- | --- |
| `shell32.dll` | analyzed `10.0.26100.8521`; live capture loaded `10.0.26100.8972` | `0x180000000` | General, Tools, Hardware wrapper, base drive orchestration |
| `devmgr.dll` | `10.0.26100.8737` | `0x180000000` | Embedded Hardware UI |
| `dskquoui.dll` | `10.0.26100.8115` | `0x180000000` | Quota page and elevated quota UI |
| `ntshrui.dll` | `10.0.26100.8117` | `0x180000000` | Sharing |
| `aclui.dll` | `10.0.26100.8115` | `0x180000000` | Security UI |
| `twext.dll` | `10.0.26100.8117` | `0x180000000` | Previous Versions |
| Common Controls v6 `comctl32.dll` | `10.0.26100.8972` | `0x180000000` | Containing property sheet and page hosting |

Addresses apply only to the stated analyzed module. The live screenshot can come from a later serviced binary with equivalent observed behavior.

## Related content

- [Filesystem item property sheets](../property-sheets/README.md)
- [Common property-sheet window construction](../property-sheets/construction.md)
- [Windows Explorer implementation notes](../README.md)
