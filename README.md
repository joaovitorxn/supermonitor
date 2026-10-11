# Supermonitor

A performance monitor and in-game overlay for Windows 11, built for gamers. One WinUI 3 process lives in the tray, samples your hardware at very low cost, and can draw a click-through overlay with FPS, frametime and system metrics over windowed and borderless games.

> Status: **0.1.0 preview.** The FPS limiter, Game Mode, benchmarks, global hotkeys and standby-list cleaner are planned for 0.2.

## Features

- **Dashboard**: CPU (total and per core), memory, GPU, disks, network and a sortable process table (CPU, RAM, GPU).
- **Customizable Overview**: show, hide, resize and drag-reorder cards; line, area or bar charts; 1, 5 or 10 minute history; color thresholds per metric (for example temperature above 80 °C turns red).
- **Appearance**: light, dark or system theme, accent color, Mica, Mica Alt, Acrylic or solid backdrop, bits or bytes for network, °C or °F.
- **Hardware sensors**: temperatures, clocks, power, voltages and fans through [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor). CPU sensors need the signed [PawnIO](https://pawnio.eu) driver; the app offers to install it with `winget`.
- **FPS capture**: FPS, frametime and 1% low of the foreground window, measured from Event Tracing for Windows present events. No injection, no hooks.
- **Overlay**: a layered, click-through, always-on-top window drawn with Direct2D. Choose the monitor, one of nine anchors plus an offset, horizontal, vertical or compact layout, font, colors, background, a frametime graph and the refresh rate. A live preview uses the same renderer. Presets: Minimal, Default, Full, plus your own.
- **Low overhead**: about 0.01% CPU in the tray and about 0.15% with the overlay on, measured on a Ryzen 7 7800X3D (see the [results](docs/superpowers/plans/2026-10-10-supermonitor-03-results.md)). Expensive sensors only run while something needs them.

## Requirements

- Windows 11 (build 22000 or newer), x64.
- [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0). The Windows App SDK is bundled.
- Administrator rights. The app asks for elevation on launch because hardware sensors and ETW sessions need it.

## Install

1. Download `Supermonitor-0.1.0-win-x64.zip` from the [latest release](https://github.com/joaovitorxn/supermonitor/releases/latest).
2. Extract it anywhere and run `Supermonitor.exe`. Approve the UAC prompt.
3. Closing the window keeps the app in the tray. Right-click the tray icon to open the dashboard, toggle the overlay or exit.

Settings can start the app with Windows (a scheduled task that runs elevated, with no UAC prompt at logon).

## Using the overlay

Open the **Overlay** page, or use **Show overlay** in the tray menu. FPS comes from whichever window is in the foreground; it shows `N/A` while that window is not presenting frames. Exclusive fullscreen is not supported (windowed, borderless and flip-model fullscreen work).

## Build from source

Requires the .NET 10 SDK (see `global.json`) and Windows 11.

```
dotnet build src/Supermonitor/Supermonitor.csproj -c Release -p:Platform=x64
dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"
```

Tests marked `Category=Hardware` read real sensors and need a desktop session; the ETW ones also need an elevated runner and are skipped otherwise.

## Project layout

| Path | Contents |
|---|---|
| `src/Supermonitor` | WinUI 3 app: windows, pages, view models, tray, chart controls |
| `src/Supermonitor.Core` | UI-free logic: sensors and hub, metrics, settings, FPS capture, overlay content |
| `src/Supermonitor.Overlay` | Overlay window and Direct2D renderer |
| `tests/Supermonitor.Tests` | xUnit v3 tests |
| `docs/superpowers` | Design spec and implementation plans with their results |

## Data and logs

Settings, a settings backup when the file is corrupt, and daily logs (7 days) live in `%LocalAppData%\Supermonitor`.

## Documentation

- Design: [`docs/superpowers/specs/2026-10-10-supermonitor-design.md`](docs/superpowers/specs/2026-10-10-supermonitor-design.md) (in Portuguese)
- Plans and results: [`docs/superpowers/plans`](docs/superpowers/plans), starting with the [plan index](docs/superpowers/plans/2026-10-10-supermonitor-plan-index.md)
