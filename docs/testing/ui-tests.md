# UI and presentation tests

`tests/Files.UITests` provides a WinUI test host and presentation/control test boundary. The historically named `tests/Files.AxeTests` project currently covers full-process navigation stress automation, not Axe accessibility scans.

## Presentation tests

Presentation tests should validate Core-to-UI adaptation without unnecessarily testing every behavior through full app automation.

Important contracts include:

- first rows are published before enumeration completes;
- UI work remains bounded/coalesced;
- canceled navigation cannot leak stale rows;
- repeated navigation does not duplicate avoidable work;
- async disposal waits for owned cleanup;
- property enrichment updates existing rows;
- grouping/column layout/control contracts remain correct.

## Real WinUI realization

`Items.Count > 0` is not identical to "the user has a rendered row." End-to-end performance tests should observe actual control/container realization when measuring time-to-first-visible-content.

See [`performance-tests.md`](performance-tests.md) and [issue #5](https://github.com/0x5bfa/ReFiles/issues/5).

## Accessibility

UI changes that affect controls, focus, names, patterns, selection, or navigation should include/adjust accessibility coverage at the appropriate boundary. Do not rely only on visual inspection.

The current navigation stress project verifies responsiveness through Windows UI Automation but does not replace a dedicated accessibility scanner.

## Avoid app-wide tests for local contracts

If a presentation adapter or control can prove a behavior deterministically in-process, prefer that test over launching the entire application. Reserve end-to-end automation for interactions that depend on the full process/window/automation boundary.
