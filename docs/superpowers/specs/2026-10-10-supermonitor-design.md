# Supermonitor — Design

Date: 2026-10-10
Status: approved in conversation, awaiting spec review

## 1. Goal

Supermonitor is a Windows 11 desktop app for gamers. It offers:

- Detailed performance dashboard: CPU, RAM, GPU, disks, network and processes.
- Customizable visualization.
- Customizable in-game overlay.
- FPS limiter and gamer features: automatic Game Mode, session benchmark, global hotkeys, RAM standby list cleanup.
- Minimal resource consumption by the app itself.

### Success criteria

1. With the dashboard open and updating every 1 s, the app uses less than 1% CPU on a Ryzen 7 7800X3D.
2. With the dashboard closed (tray + overlay only), the app uses less than 0.3% CPU.
3. CPU, RAM, disk and network values match Task Manager (tolerance of ±2 percentage points or ±5%).
4. The overlay shows real FPS and frametime from a game in windowed or borderless mode.
5. The FPS limiter works on the user's AMD RX 9070 XT GPU.

### Out of scope

- Overlay in true exclusive fullscreen (would require injection into the game process). DX11/DX12 games in fullscreen with Windows 11 Fullscreen Optimizations (flip model) show the overlay normally.
- DLL injection or `Present()` hooking.
- MSIX package and Microsoft Store.
- Languages other than EN-US.
- Separate agent process (a single-process architecture was chosen).

## 2. Decisions

| Topic | Decision |
|---|---|
| Architecture | Single WinUI 3 process with a tray icon |
| Language/runtime | C#, .NET 10, Windows App SDK (latest stable), x64 |
| Packaging | Unpackaged (`WindowsPackageType=None`), self-contained Windows App SDK |
| Privilege | `requireAdministrator` in the manifest; autostart via scheduled task with "run with highest privileges" |
| Hardware sensors | LibreHardwareMonitorLib (signed PawnIO driver) |
| FPS limiter | Driver: ADLX (AMD) and NVAPI (NVIDIA); no injection |
| FPS capture | Real-time ETW session (TraceEvent) |
| Overlay | Layered, click-through Win32 window, Direct2D/DirectWrite via Vortice |
| Charts | Custom Win2D control |
| MVVM | CommunityToolkit.Mvvm |
| Settings | JSON at `%LocalAppData%\Supermonitor\settings.json`, System.Text.Json with source generator |
| UI language | EN-US (UI text in English) |

## 3. Architecture

### 3.1 Projects

```
Supermonitor.sln
├─ src/Supermonitor            WinUI 3: windows, pages, ViewModels, tray, chart controls
├─ src/Supermonitor.Core       No UI: sensors, SensorHub, ETW FPS, games, limiter, benchmark, RAM, hotkeys, settings
├─ src/Supermonitor.Overlay    Win32 overlay window + Direct2D rendering
└─ tests/Supermonitor.Tests    xUnit for Core logic
```

Dependencies: `Supermonitor` → `Supermonitor.Core`, `Supermonitor.Overlay`. `Supermonitor.Overlay` → `Supermonitor.Core`. `Supermonitor.Core` does not depend on WinUI.

### 3.2 SensorHub

- One background thread with `PeriodicTimer`.
- Three sampling tiers:
  - **Fast** (default 250 ms): FPS/frametime aggregation.
  - **Normal** (default 1 s, configurable from 250 ms to 2 s): CPU, RAM, GPU, disk, network.
  - **Slow** (5 s): disk space, adapter list, SMART data.
- Each sample is a struct written to a pre-allocated `RingBuffer<T>`. 10-minute history. No per-tick allocation on the hot path.
- Consumers (dashboard, overlay, Game Mode, benchmark) subscribe to a `SampleReady` event and read the latest value or the history. The hub does not know the consumers.
- Expensive metrics are only sampled when some consumer asks for them. Examples: process list and per-core usage only with the dashboard visible; ETW only with a detected game or an overlay showing FPS.

### 3.3 Providers

Each provider implements `ISensorProvider` (`Initialize`, `Sample`, `IsAvailable`, `UnavailableReason`). A failure in one provider never brings down the hub. The affected metric shows "N/A" and the error goes to the log.

| Area | Source | Metrics |
|---|---|---|
| CPU | PDH `\Processor Information(*)\% Processor Utility` (single batched query) | Total and per-logical-core usage |
| CPU | LHM | Per-core clock, temperature (Tctl/Tdie, CCD), package power, voltage |
| RAM | `GlobalMemoryStatusEx`, `GetPerformanceInfo`, `NtQuerySystemInformation(SystemMemoryListInformation)` | In use, available, standby, free, modified, commit, cache |
| GPU | PDH `\GPU Engine(*)\Utilization Percentage`, `\GPU Adapter Memory(*)\*` | Usage per engine (3D, copy, video), usage per process, dedicated and shared VRAM |
| GPU | LHM | Temperature (edge, hotspot, memory), clock, fan (RPM and %), power |
| Disk | PDH `\PhysicalDisk(*)\*`, `DriveInfo` | Reads/writes per second, active time, queue, free space |
| Disk | LHM | SMART temperature |
| Network | `GetIfTable2` (`MIB_IF_ROW2` deltas) | Upload/download per adapter |
| Network | ICMP to a configurable host (default `1.1.1.1`, every 2 s) | Latency, jitter, loss |
| Processes | `NtQuerySystemInformation(SystemProcessInformation)` + PDH GPU per PID | Top N by CPU, RAM, GPU |
| FPS | ETW: `Microsoft-Windows-DxgKrnl` (present/flip events) and `Microsoft-Windows-DXGI` | FPS, frametime, per process |

### 3.4 FPS capture

- ETW session named `Supermonitor-FPS`. On startup, the app stops any orphan session with that name.
- DxgKrnl events cover DX9–12, Vulkan and OpenGL. DXGI complements for DXGI apps.
- The target is the foreground game process. Frametime is the interval between consecutive presents of the target process.
- `FrameTimeAggregator` (pure, testable logic) receives timestamps and produces the window's average FPS, the current frametime and a frametime buffer for the mini-chart and the benchmark.

### 3.5 Overlay

- Win32 window with `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`. It receives no clicks or focus and does not appear in Alt+Tab.
- Rendering: Direct2D draws into a premultiplied 32-bit DIB, then `UpdateLayeredWindow`. Text via DirectWrite with grayscale antialiasing.
- Redraws only when a displayed value changes, capped at the configured rate (default 4 Hz; frametime mini-chart up to 30 Hz).
- Works in windowed and borderless games. Exclusive fullscreen is out of scope.

### 3.6 Lifecycle and consumption

- Closing the main window destroys the window and keeps the app in the tray.
- With no visible window, the process enters EcoQoS (`SetProcessInformation` with `ProcessPowerThrottling`) and stops dashboard-only sampling.
- Build: ReadyToRun, Tiered PGO, non-concurrent workstation GC.
- Single instance: a named mutex. The second instance asks the first to open the dashboard and exits.

## 4. Interface

### 4.1 Navigation

`NavigationView` on the left with the pages:

UI labels in English.

1. **Overview**: card grid.
2. **CPU**: per-core usage, clocks, temperatures, power.
3. **Memory**: RAM composition, commit, top processes by RAM, "Clear standby" button.
4. **GPU**: usage per engine, VRAM, temperatures, clock, fan, power, usage per process.
5. **Disks**: activity and space per disk, temperature.
6. **Network**: traffic per adapter, latency/jitter/loss.
7. **Processes**: table sortable by CPU, RAM, GPU.
8. **Games**: list of detected games and profiles, Game Mode configuration.
9. **Benchmarks**: session list, frametime chart, comparison between sessions.
10. **Overlay**: editor with live preview.
11. **Settings**: appearance, sampling, units, hotkeys, startup, sensors.

### 4.2 Dashboard customization

- Cards: show, hide and reorder by dragging; compact, normal or large size.
- Chart per card: line, area or bar.
- History window: 1, 5 or 10 minutes.
- Update interval: 250 ms, 500 ms, 1 s or 2 s.
- Theme: light, dark or system. Accent color. Background Mica, Acrylic or solid.
- Units: bits or bytes for network; °C or °F.
- Color thresholds per metric (e.g. temperature above 80 °C in red).

### 4.3 Charts

Custom `SparklineChart` and `TimeSeriesChart` controls on top of Win2D `CanvasControl`. They read directly from the ring buffer. Points are reused between frames, with no per-draw allocation. Drawing stops when the control is not visible.

### 4.4 Overlay customization

- Target monitor and position: 9 anchors + pixel offset.
- Displayed metrics and order: FPS, frametime, 1% low, CPU usage/temp/clock/power, GPU usage/temp/clock/power/VRAM, RAM, network, ping, clock, limiter state.
- Layout: horizontal, vertical or compact.
- Font, size, label and value color per metric, color thresholds.
- Background: color, opacity, corner radius, padding.
- Frametime mini-chart on/off, width and height.
- Update rate.
- Presets: Minimal (FPS only), Default, Full. The user can save their own presets.
- The editor shows a live preview using the same renderer as the overlay.

## 5. Gamer features

### 5.1 FPS limiter

`IFrameLimiter` interface (`IsSupported`, `UnsupportedReason`, `Apply(int fps)`, `Clear()`, `CurrentLimit`).

- **AMD (`AdlxFrameLimiter`)**: loads `amdadlx64.dll` installed by the driver and calls ADLX through the C ABI (vtables via function pointers). Uses Frame Rate Target Control when supported; otherwise uses Radeon Chill with minimum = maximum = limit. The limit is global to the driver.
- **NVIDIA (`NvapiFrameLimiter`)**: `nvapi64.dll`, Driver Settings (DRS), `FRL_FPS` setting (0x10835002) in the game's application profile, or in the base profile when there is no game. Marked experimental: no NVIDIA hardware for testing.
- No supported GPU: limiter disabled with the reason shown in the UI.
- Before changing the driver, the app writes the original value to `pending-restore.json`. On exit, or on startup after a crash, it restores that value.

### 5.2 Game Mode

- **Detection** (`GameDetector`, testable logic): the foreground process is a game if:
  1. it is in the user's game list; or
  2. the executable is in a known Steam, Epic, GOG, EA or Ubisoft library; or
  3. the process presents frames via ETW with a window covering the entire monitor.
  The user's exclusion list overrides rules 2 and 3. Games detected by rules 2 and 3 are added to the list for the user to confirm.
- **Per-game profile** with optional actions:
  - power plan (e.g. Ultimate Performance);
  - game process priority (Normal, Above Normal, High);
  - FPS limit;
  - clear standby on start;
  - show overlay;
  - start benchmark automatically.
- Default profile used for games without their own profile.
- When the game exits, all changes are reverted. The changed state also goes to `pending-restore.json`.

### 5.3 Session benchmark

- Start and end via hotkey, the UI button or Game Mode.
- Records all frametimes of the target process in a pre-allocated buffer that grows in blocks.
- `BenchmarkStatistics` (pure logic) computes:
  - average FPS = frame count / duration;
  - 1% low = FPS corresponding to the 99th percentile of frametime;
  - 0.1% low = FPS corresponding to the 99.9th percentile of frametime;
  - minimum and maximum FPS, average and median frametime;
  - stutters = frames with frametime greater than 2× the moving median.
- Output in `Documents\Supermonitor\Benchmarks\<game>_<date-time>.csv` (frametimes, CPU/GPU usage and temperature per second) + `.json` (summary).
- Benchmarks page: list, frametime chart, side-by-side comparison of sessions.

### 5.4 Global hotkeys

`RegisterHotKey` on a message-only window. Configurable actions with defaults:

| Action | Default |
|---|---|
| Show/hide overlay | `Ctrl+Shift+O` |
| Turn limiter on/off | `Ctrl+Shift+L` |
| Start/stop benchmark | `Ctrl+Shift+B` |
| Clear standby | `Ctrl+Shift+M` |
| Open dashboard | `Ctrl+Shift+S` |

Registration conflict: the UI marks the hotkey as unavailable.

### 5.5 Standby list cleanup

- `NtSetSystemInformation(SystemMemoryListInformation, MemoryPurgeStandbyList)` with the `SeProfileSingleProcessPrivilege` privilege enabled.
- Manual: button on the Memory page and hotkey.
- Automatic (off by default): every 5 s, cleans if standby > X GB **and** free < Y GB. Defaults: X = 1 GB, Y = 1 GB. Minimum interval of 60 s between cleanups.

## 6. Settings and persistence

- `AppSettings` with a version number and migrations (`SettingsMigrator`, testable).
- Writes with 500 ms debounce and atomic write (temporary file + rename).
- Corrupted file: renamed to `.bak` and the app uses the defaults, with a warning.
- Logs in `%LocalAppData%\Supermonitor\logs`, daily rotation, 7 days.

## 7. Error handling

| Situation | Behavior |
|---|---|
| PawnIO driver missing | LHM metrics show "N/A"; banner with a button to install |
| ADLX/NVAPI missing or unsupported | Limiter disabled with the reason |
| Failure creating ETW session | FPS "N/A"; reason in the UI; retry every 30 s |
| PDH counter missing | Metric "N/A" |
| Exception in provider | Caught in the hub, logged, provider marked unavailable after 5 consecutive failures |
| Crash with changes applied | `pending-restore.json` restored on next start |
| Hotkey already in use | Marked as unavailable in the UI |

## 8. Tests

### 8.1 Unit (xUnit, `Supermonitor.Tests`)

- `RingBuffer<T>`: capacity, overwrite, window read.
- `FrameTimeAggregator`: synthetic present events → FPS and frametime.
- `BenchmarkStatistics`: average, 1% low, 0.1% low, stutters with known data.
- `GameDetector`: user list, libraries, exclusions.
- `SettingsMigrator` and `AppSettings` serialization.
- `StandbyCleanerPolicy`: thresholds and minimum interval.
- Unit formatting.

### 8.2 Real-machine smoke

Test marked with the `Hardware` trait, outside the default run: initializes each provider and checks `IsAvailable` and values within plausible ranges.

### 8.3 Manual verification

1. Compare CPU, RAM, disk and network with Task Manager.
2. Measure the app's own CPU with the dashboard open and closed (criteria 1 and 2).
3. Open a borderless game, check overlay and FPS.
4. Apply an FPS limit and check it in the overlay.
5. Run a benchmark and check the CSV and summary.
6. Force-kill the app with Game Mode active and check the restoration on next start.
