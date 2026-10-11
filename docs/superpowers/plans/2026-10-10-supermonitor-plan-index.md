# Supermonitor — Plan Index

Spec: `docs/superpowers/specs/2026-10-10-supermonitor-design.md`

The spec covers four subsystems. Each plan delivers working, testable software on its own. Plans 2–4 are written after the previous plan is finished, because they depend on real APIs and names created earlier.

| # | Plan | Delivers | Spec sections |
|---|---|---|---|
| 1 | `2026-10-10-supermonitor-01-foundation.md` | Solution, Core (ring buffer, metrics, SensorHub, settings, log), CPU/RAM/GPU (PDH)/disk/network/ping providers, WinUI 3 app with tray, single instance, EcoQoS, autostart, Overview page and basic Settings | 2, 3.1, 3.2, 3.3 (except LHM and processes), 3.6, 4.1 (Overview/Settings), 6, 7 (partial), 8 |
| 2 | `…-02-hardware-and-dashboard.md` | LibreHardwareMonitor (PawnIO, install banner), CPU/Memory/GPU/Disks/Network/Processes pages, DemandTracker, full dashboard customization (cards, charts, history, theme, highlight, background, units, thresholds) | 3.3 (LHM, processes), 4.1–4.3 |
| 3 | `…-03-fps-and-overlay.md` | ETW session, FrameTimeAggregator, Direct2D overlay window, editor with preview, presets | 3.4, 3.5, 4.4 |
| 4 | `…-04-gamer-features.md` | GameDetector, Game Mode and profiles, pending-restore, ADLX/NVAPI limiter, benchmark and page, hotkeys, standby cleanup | 5, 7 (rest) |
