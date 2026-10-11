# Supermonitor Plan 2 — Results

Date: 2026-10-10 (measurements taken on 2026-10-11 by the host clock)
Build: Release x64, branch `feat/hardware-dashboard`, run as administrator.
Machine: Ryzen 7 7800X3D, RX 9070 XT, Windows 11 26H2.

## Tests

`dotnet test --project tests/Supermonitor.Tests` (unit + hardware): 215 passed, 0 failed, 0 skipped.

## Performance (60 s per scenario, `tools/measure-cpu.ps1`)

| Scenario | CPU | Working set | Private | Criterion | Result |
|---|---|---|---|---|---|
| Overview open, 1 s interval | 0.27% | 291 MB | 275 MB | < 1% | Passed |
| Processes page open | 0.31% | 301 MB | 286 MB | no criterion | Reference for Plan 3 |
| Window closed (tray only) | 0.01% | 301 MB | 285 MB | < 0.3% | Passed |

The tray-only scenario confirms that the LHM and process leases are released when the window closes.

## Manual checks (Tasks 10–14)

Verified by the user in the running app: appearance (Mica, no PawnIO banner, live cards), settings page, light theme, accent color on all WinUI controls, categories, Processes page, and card reordering (drag and the Move before/after menu).

## Deviations from the plan and decisions

- Task 1: `CpuProvider` clamps per-core usage to 0..100, like Task Manager; the PDH `% Processor Utility` reached 119% with turbo.
- Task 10: `RestartArgumentsTests` uses space-separated command lines, because `InlineData(string[])` produces CS0182. Migrated v1 settings keep the user's 8 cards, so temperature cards do not appear in Overview until they are added.
- Task 11: commit `0d6925b` landed with two unsaved files and `app.sh` masked the build failure; fixed in a following commit and the script now exits with the build's exit code. One broken intermediate commit remains in the history.
- Task 11: WinUI OLE drag does not work in elevated apps (UIPI). Reordering uses pointer capture. After that, `ObservableCollection.Move` did not trigger a new layout in the `ItemsRepeater`; reordering now uses `RemoveAt` + `Insert`. The card follows the pointer during the drag, slides to its new place on release (`Translation` animation in `CardWrapLayout`, keyed by `MetricId`) and returns to its place if released outside a card.
- Task 14: the light theme crashed (AV 0xc0000005 in CoreMessaging) due to a reentrant `Apply` from `ActualThemeChanged` swapping the `SystemBackdrop`; fixed without a unit test because it is native WinUI behavior.
- Task 14: the custom accent color was extended from charts to all WinUI controls via `SystemAccentColor*` overrides (TDD in `AccentShades`), with a refresh on theme change.
