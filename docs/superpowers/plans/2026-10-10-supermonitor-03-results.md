# Supermonitor Plan 3 — Results

Date: 2026-10-11
Build: Release x64, branch `feat/fps-overlay`, run as administrator.
Machine: Ryzen 7 7800X3D, RX 9070 XT, Windows 11 26H2, primary monitor 2560×1440 at 240 Hz.

## Tests

`dotnet test --project tests/Supermonitor.Tests` (unit + hardware, non-elevated shell): 328 total, 326 passed, 0 failed, 2 skipped (ETW tests that require administrator).

The ETW tests (`FrameCaptureSmokeTests`) were also run elevated (pwsh launched with UAC): 3 passed and 1 skipped (the "no administrator" case, which only applies when not elevated). The orphan session is replaced and the session disappears on `Dispose`.

## Performance (60 s per scenario, `tools/measure-cpu.ps1`, 1 s interval)

| Scenario | CPU | Working set | Private | Criterion | Result |
|---|---|---|---|---|---|
| Main window open (I did not check which page) + overlay on (Default) | 0.21% | 292 MB | 282 MB | < 1% | Passed |
| Tray only + overlay on (Default, chart at 30 Hz, window animating in foreground) | 0.15% | 302 MB | 291 MB | < 0.3% | Passed |
| Tray only, overlay off | 0.01% | 300 MB | 289 MB | < 0.3% | Passed |

Two earlier "tray only" measurements (0.19% with overlay on and 0.15–0.17% with overlay off) were discarded: the main window was still open (checked with `MainWindowHandle`). With the window actually closed and the overlay off, the cost went back to 0.01%, the same as Plan 2, and the ETW session does not exist (`logman query -ets` without `Supermonitor-FPS`), which shows the FPS and hardware leases were released. The cost of the overlay being on (≈0.14 point) comes from the 30 Hz mini-chart and the processing of ETW events. The Minimal preset (4 Hz, no chart) was not measured.

## Real FPS (spec criterion 4)

Verified by the user with a game in windowed/borderless mode: the overlay FPS matches the game's and the overlay works. There was no numeric comparison with a second tool beyond this confirmation. DX11/DX12 worked with only the DxgKrnl `Present` event, so the DXGI fallback was not needed.

## Crash recovery

With the overlay on, `Stop-Process -Force` on the app left the `Supermonitor-FPS` session active (`logman query -ets`). On reopening the app, the log shows "Frame capture started" and the session list has a single `Supermonitor-FPS`. The hardware test `Etw_source_replaces_an_orphan_session_with_the_same_name` covers the same case.

## Manual checks (Tasks 8 and 9)

Confirmed by the user: the overlay works (turns on and off through the tray's "Show overlay" item, repeatedly, and keeps working with the main window closed). The Overlay page (editor, presets, preview) compiled and opens, but the user did not report the full Step 6 script of Task 9; it remains a pending check.

## Deviations from the plan and decisions

- Capture only through the DxgKrnl `Present` event (id 184); the DXGI from spec 3.4 is not used. Reason: the DXGI event did not arrive for the tested process (Chromium uses DirectComposition) and DxgKrnl carries the correct PID. The fallback stayed documented and was not needed.
- FPS, frametime and 1% low are not `MetricStore` metrics (there are no FPS cards on the dashboard); the overlay reads directly from `FrameCaptureService`.
- The overlay's "limiter state" item (spec 4.4) is left for Plan 4, with `IFrameLimiter`.
- The FPS target is the foreground process, any app. Restriction to detected games comes with Plan 4's `GameDetector`.
- The existing test `Version_one_overview_cards_become_v2_cards` now asserts `SettingsMigrator.CurrentVersion` (a v1 document now migrates up to v3); the plan did not foresee this change.
- An elevated app instance keeps the overlay in EcoQoS when the window closes; the measurement above shows the cost stays within the criterion.
