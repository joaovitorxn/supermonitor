# Supermonitor Plan 3 — FPS Capture and Overlay: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Capture the real FPS and frametime of the foreground process via ETW and display them (together with CPU, GPU, RAM, network, ping and clock) in a click-through overlay drawn with Direct2D, with an appearance editor, live preview and presets.

**Architecture:** `EtwPresentSource` (TraceEvent) emits one event per present from the `Microsoft-Windows-DxgKrnl` provider. `FrameCaptureService` turns the session on only while a `Demand.FrameCapture` lease exists, picks the foreground process as the target and feeds a `FrameTimeAggregator` (pure logic). The overlay is a layered Win32 window on its own thread (`Supermonitor.Overlay`): `OverlayFrameBuilder` (Core, pure) turns the style and values into text rows; `OverlayRenderer` draws those rows with Direct2D into a premultiplied DIB; `UpdateLayeredWindow` presents the DIB. The same renderer produces the editor preview. Settings move to schema v3 (`Overlay` with style, items, user presets).

**Tech Stack:** same as Plans 1–2 + `Microsoft.Diagnostics.Tracing.TraceEvent` 3.2.8 (Core) and `Vortice.Direct2D1` 3.8.3 (Overlay; brings DirectWrite and DXGI).

**Spec:** `docs/superpowers/specs/2026-10-10-supermonitor-design.md` (sections 3.4, 3.5, 4.4)
**Index:** `docs/superpowers/plans/2026-10-10-supermonitor-plan-index.md`
**Base:** `main` at `63c564f` (Plan 2 complete; results in `docs/superpowers/plans/2026-10-10-supermonitor-02-results.md`).

## Global Constraints

- Everything from Plans 1–2 still applies: Windows 11, x64, `net10.0-windows10.0.22621.0`, unpackaged, `requireAdministrator`, UI in EN-US, `N/A` for a missing value, `Supermonitor.Core` with no WinUI dependency, comments and identifiers as in the existing code (comments, names and UI text in English).
- Unit tests: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`. Hardware tests: `dotnet test --project tests/Supermonitor.Tests --filter-trait "Category=Hardware"`. Full suite: `dotnet test --project tests/Supermonitor.Tests`.
- App build: `dotnet build src/Supermonitor/Supermonitor.csproj -c Debug -p:Platform=x64`. With the app open the exe is locked; to check compilation use `-o <temporary folder>`.
- The elevated app cannot be closed or clicked by a non-elevated shell, and this shell is not elevated. Visual checks and tests that require administrator (ETW) are left to the user (exit through the tray, approve UAC). ETW tests use `Assert.Skip` when the process is not administrator.
- Performance criteria remain: dashboard open < 1% CPU, tray only **with overlay visible and FPS on** < 0.3% CPU (Release build, 1 s interval).
- FPS source: `Present` event (id 184, version 1) of the `Microsoft-Windows-DxgKrnl` provider (GUID `802ec45a-1e99-4b83-9920-87c98277ba9d`), `Present` keyword (`0x8000000`). Measured on this machine on 2026-10-10: the header `ProcessID` is that of the presenting app (e.g. the Claude process at ~240 events/s), and the whole provider with that keyword generates ~6000 events/s on the system and costs ~31 ms of CPU in 8 s. The `Microsoft-Windows-DXGI` Present_Start event (id 42) exists, but did not arrive for the tested process (Chromium uses DirectComposition); so the plan uses only DxgKrnl and does **not** use DXGI (deviation from spec 3.4, recorded in the results). If a DX11/DX12 game does not show FPS in the manual check of Task 10, DXGI becomes the fallback.
- Vortice 3.8.3, API verified by spike on 2026-10-10: `D2D1.D2D1CreateFactory<ID2D1Factory1>()`, `DWrite.DWriteCreateFactory<IDWriteFactory>()`, `factory.CreateDCRenderTarget(new RenderTargetProperties(RenderTargetType.Default, new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied), 0, 0, RenderTargetUsage.GdiCompatible, FeatureLevel.Default))`, `target.BindDC(hdc, new RectI(0, 0, w, h))`, `new RoundedRectangle(new System.Drawing.RectangleF(...), rx, ry)`, `dwrite.CreateTextFormat(family, FontWeight.SemiBold, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, size)`, `dwrite.CreateTextLayout(text, format, maxWidth, maxHeight)`, `target.DrawTextLayout(Vector2, layout, brush)`. `AlphaMode` needs the alias `using AlphaMode = Vortice.DCommon.AlphaMode;`. The result in a top-down DIB has a transparent corner (alpha 0) and a 60% background with alpha 153, i.e. correctly premultiplied for `UpdateLayeredWindow`.
- Every commit ends with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

## Review Focus

1. Target disappears or changes (alt-tab, game closes, Supermonitor's own window in the foreground) → overlay shows `N/A` instead of freezing on the last FPS, and aggregation restarts without mixing games. Tests: Task 4 (`FrameCaptureService`: target change restarts, target = own process becomes none, idle > 2 s becomes `N/A`).
2. ETW session fails (no administrator, orphan session from a crash) → FPS `N/A` with a reason, retry every 30 s, app without exception. Tests: Task 4 (failure, retry with `FakeTimeProvider`) and Task 5 (hardware: orphan session is replaced).
3. Long pause between presents (loading, minimized game, alt-tab) → a frametime of minutes must not become a 1% low of 0 FPS. Test: Task 1 (`MaxGapMs` restarts the sequence).
4. Plan 2 v2 settings and a file with invalid overlay values (empty font, opacity 7, unknown enum, color "blue", duplicate items) → loads normalized, without losing the rest. Tests: Task 2.
5. Overlay on a monitor that disappeared, different DPI or position outside the screen → window always inside the chosen monitor (falls back to the primary if the monitor does not exist). Tests: Task 3 (`OverlayPlacement` clamps to the screen) and Task 6 (monitor resolution).

---

## File Structure

```
src/Supermonitor.Core/
  Supermonitor.Core.csproj                  + Microsoft.Diagnostics.Tracing.TraceEvent
  AppHost.cs                                + Frames (FrameCaptureService)
  Frames/Percentile.cs                      nearest-rank percentile (reused by Plan 4's benchmark)
  Frames/FrameTimeAggregator.cs             FrameStats, FrameTimeAggregator
  Frames/FrameCaptureService.cs             IPresentSource, IForegroundProbe, FrameCaptureService
  Frames/EtwPresentSource.cs                ETW session Supermonitor-FPS
  Frames/ForegroundProbe.cs                 GetForegroundWindow → PID
  Interop/User32.cs                         + GetForegroundWindow, GetWindowThreadProcessId
  Overlay/OverlaySettings.cs                enums, OverlayItemSettings, OverlayStyle, OverlayPreset, OverlaySettings
  Overlay/OverlayPresets.cs                 Minimal, Default, Full
  Overlay/OverlayEditor.cs                  items, user presets
  Overlay/Rgba.cs
  Overlay/OverlayMetricCatalog.cs           label, unit, MetricStore id, demand
  Overlay/IOverlayValues.cs                 IOverlayValues, DemoOverlayValues
  Overlay/OverlayFrame.cs                   OverlayRow, OverlayFrame
  Overlay/OverlayFrameBuilder.cs            style + values → rows
  Overlay/OverlayPlacement.cs               PixelRect, anchors
  Overlay/OverlayRefresh.cs                 effective rate
  Sensors/DemandTracker.cs                  + Demand.FrameCapture
  Settings/AppSettings.cs                   + Overlay
  Settings/SettingsMigrator.cs              v2 → v3
  Settings/SettingsJsonContext.cs           + OverlayStyle
  Shell/TrayIcon.cs                         + "Show overlay" item
src/Supermonitor.Overlay/
  Supermonitor.Overlay.csproj               Vortice.Direct2D1
  Interop/Gdi32.cs, Interop/User32.cs, Interop/Shcore.cs
  OverlayRenderer.cs                        Direct2D → DIB
  OverlayWindow.cs                          layered window + thread + timer
  OverlayService.cs                         leases, live values, monitors
  LiveOverlayValues.cs                      IOverlayValues over MetricStore + FrameCaptureService
  MonitorCatalog.cs                         MonitorInfo, enumeration, DPI
src/Supermonitor/
  Supermonitor.csproj                       + ProjectReference Overlay
  App.xaml.cs                               OverlayService, tray item
  MainWindow.xaml(.cs)                      Overlay navigation item
  Views/OverlayPage.xaml(.cs)               editor + preview
  Dialogs/OverlayItemDialog.cs              colors and thresholds per item
tests/Supermonitor.Tests/                   one file per unit, listed in each task; references Supermonitor.Overlay
```

---

### Task 1: FrameTimeAggregator and percentile

**Files:**
- Create: `src/Supermonitor.Core/Frames/Percentile.cs`, `src/Supermonitor.Core/Frames/FrameTimeAggregator.cs`
- Test: `tests/Supermonitor.Tests/Frames/PercentileTests.cs`, `tests/Supermonitor.Tests/Frames/FrameTimeAggregatorTests.cs`

**Interfaces:**
- Consumes: `Supermonitor.Core.Collections.RingBuffer<T>`.
- Produces:
  - `static class Percentile { static float NearestRank(Span<float> values, double percent) }` — sorts `values` in place; `NaN` if empty.
  - `readonly record struct FrameStats(bool HasData, double Fps, double FrameTimeMs, double Low1PercentFps, long FrameCount)` with `static FrameStats None`.
  - `sealed class FrameTimeAggregator` with `const double FpsWindowMs = 1000`, `LowWindowMs = 10_000`, `MaxGapMs = 5000`, `const int Capacity = 16384`; `void AddPresent(double timestampMs)`, `FrameStats GetStats()`, `int CopyFrameTimes(Span<float> destination)`, `long Version { get; }`, `void Reset()`.

- [ ] **Step 1: Write the percentile tests**

`tests/Supermonitor.Tests/Frames/PercentileTests.cs`:

```csharp
using Supermonitor.Core.Frames;

namespace Supermonitor.Tests.Frames;

public class PercentileTests
{
    private static float[] Range(int count) => Enumerable.Range(1, count).Select(i => (float)i).ToArray();

    [Fact]
    public void Ninety_ninth_percentile_of_one_to_hundred_is_ninety_nine()
    {
        Assert.Equal(99f, Percentile.NearestRank(Range(100), 99));
    }

    [Fact]
    public void Median_of_one_to_hundred_is_fifty()
    {
        Assert.Equal(50f, Percentile.NearestRank(Range(100), 50));
    }

    [Fact]
    public void Ninety_nine_point_nine_of_one_to_thousand_is_nine_ninety_nine()
    {
        // 99.9 * 1000 / 100 in floating point exceeds 999 due to rounding error; the correct result is rank 999.
        Assert.Equal(999f, Percentile.NearestRank(Range(1000), 99.9));
    }

    [Fact]
    public void Hundredth_percentile_is_the_maximum()
    {
        Assert.Equal(100f, Percentile.NearestRank(Range(100), 100));
    }

    [Fact]
    public void Single_value_is_every_percentile()
    {
        Assert.Equal(7f, Percentile.NearestRank(new[] { 7f }, 99));
    }

    [Fact]
    public void Empty_input_gives_nan()
    {
        Assert.True(float.IsNaN(Percentile.NearestRank(Span<float>.Empty, 99)));
    }

    [Fact]
    public void Unsorted_input_is_sorted_first()
    {
        Assert.Equal(5f, Percentile.NearestRank(new[] { 5f, 1f, 4f, 2f, 3f }, 100));
    }
}
```

- [ ] **Step 2: Run and see it fail**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-class "Supermonitor.Tests.Frames.PercentileTests"`
Expected: compile error (`Percentile` does not exist).

- [ ] **Step 3: Implement `Percentile`**

`src/Supermonitor.Core/Frames/Percentile.cs`:

```csharp
namespace Supermonitor.Core.Frames;

public static class Percentile
{
    // Nearest-rank: the smallest value with at least `percent`% of the data less than or equal to it. Sorts `values` in place.
    public static float NearestRank(Span<float> values, double percent)
    {
        if (values.IsEmpty)
        {
            return float.NaN;
        }

        values.Sort();
        // The epsilon prevents 99.9 * 1000 / 100 = 999.0000000000001 from becoming rank 1000.
        var rank = (int)Math.Ceiling(percent * values.Length / 100d - 1e-9);
        return values[Math.Clamp(rank, 1, values.Length) - 1];
    }
}
```

- [ ] **Step 4: Run and see it pass**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-class "Supermonitor.Tests.Frames.PercentileTests"`
Expected: 7 pass.

- [ ] **Step 5: Write the aggregator tests**

`tests/Supermonitor.Tests/Frames/FrameTimeAggregatorTests.cs`:

```csharp
using Supermonitor.Core.Frames;

namespace Supermonitor.Tests.Frames;

public class FrameTimeAggregatorTests
{
    // The first present produces no frametime (there is no previous one); each interval becomes a frametime.
    private static FrameTimeAggregator Feed(params double[] frameTimesMs)
    {
        var aggregator = new FrameTimeAggregator();
        var timestamp = 1000.0;
        aggregator.AddPresent(timestamp);
        foreach (var frameTime in frameTimesMs)
        {
            timestamp += frameTime;
            aggregator.AddPresent(timestamp);
        }

        return aggregator;
    }

    [Fact]
    public void Steady_sixty_fps_reports_sixty()
    {
        var aggregator = Feed(Enumerable.Repeat(1000.0 / 60, 120).ToArray());

        var stats = aggregator.GetStats();

        Assert.True(stats.HasData);
        Assert.Equal(60, stats.Fps, 0.5);
        Assert.Equal(1000.0 / 60, stats.FrameTimeMs, 0.01);
        Assert.Equal(60, stats.Low1PercentFps, 0.5);
        Assert.Equal(120, stats.FrameCount);
    }

    [Fact]
    public void New_aggregator_has_no_data()
    {
        var stats = new FrameTimeAggregator().GetStats();

        Assert.False(stats.HasData);
        Assert.True(double.IsNaN(stats.Fps));
    }

    [Fact]
    public void A_single_present_has_no_frame_time_yet()
    {
        var stats = Feed().GetStats();

        Assert.False(stats.HasData);
    }

    [Fact]
    public void One_percent_low_comes_from_the_ninety_ninth_percentile_frame_time()
    {
        // 100 frames: 96 of 10 ms and 4 of 40 ms → rank 99 = 40 ms → 25 FPS.
        var frameTimes = Enumerable.Repeat(10.0, 100).ToArray();
        frameTimes[20] = frameTimes[40] = frameTimes[60] = frameTimes[80] = 40;

        var stats = Feed(frameTimes).GetStats();

        Assert.Equal(25, stats.Low1PercentFps, 1e-6);
        Assert.Equal(10, stats.FrameTimeMs, 1e-6);
    }

    [Fact]
    public void Fps_uses_only_the_last_second()
    {
        // 5 s at 50 FPS (20 ms) followed by 1.5 s at 100 FPS (10 ms).
        var frameTimes = Enumerable.Repeat(20.0, 250).Concat(Enumerable.Repeat(10.0, 150)).ToArray();

        var stats = Feed(frameTimes).GetStats();

        Assert.Equal(100, stats.Fps, 1);
    }

    [Fact]
    public void A_gap_longer_than_the_limit_restarts_the_sequence()
    {
        var aggregator = Feed(10, 10, 10);
        var last = 1000.0 + 30;

        aggregator.AddPresent(last + FrameTimeAggregator.MaxGapMs + 1);

        Assert.False(aggregator.GetStats().HasData);

        aggregator.AddPresent(last + FrameTimeAggregator.MaxGapMs + 11);
        var stats = aggregator.GetStats();
        Assert.True(stats.HasData);
        Assert.Equal(10, stats.FrameTimeMs, 1e-6);
        Assert.Equal(100, stats.Low1PercentFps, 1e-6);
    }

    [Fact]
    public void A_short_pause_is_kept_as_a_frame_time()
    {
        // 4 frames: rank 99 is the largest (800 ms) → 1.25 FPS 1% low; the current frametime remains the last one.
        var stats = Feed(10, 10, 800, 10).GetStats();

        Assert.Equal(1000.0 / 800, stats.Low1PercentFps, 1e-6);
        Assert.Equal(10, stats.FrameTimeMs, 1e-6);
    }

    [Fact]
    public void Duplicate_and_out_of_order_timestamps_are_ignored()
    {
        var aggregator = Feed(10, 10);
        var version = aggregator.Version;

        aggregator.AddPresent(1020);
        aggregator.AddPresent(1015);

        Assert.Equal(version, aggregator.Version);
        Assert.Equal(2, aggregator.GetStats().FrameCount);
    }

    [Fact]
    public void More_frames_than_the_capacity_do_not_overflow()
    {
        var aggregator = Feed(Enumerable.Repeat(1.0, FrameTimeAggregator.Capacity + 4000).ToArray());

        var stats = aggregator.GetStats();
        Span<float> buffer = new float[100];

        Assert.True(stats.HasData);
        Assert.Equal(1000, stats.Fps, 1);
        Assert.Equal(100, aggregator.CopyFrameTimes(buffer));
    }

    [Fact]
    public void Copy_returns_the_most_recent_frame_times_oldest_first()
    {
        var aggregator = Feed(1, 2, 3, 4, 5);
        Span<float> buffer = new float[3];

        var copied = aggregator.CopyFrameTimes(buffer);

        Assert.Equal(3, copied);
        Assert.Equal(new[] { 3f, 4f, 5f }, buffer.ToArray());
    }

    [Fact]
    public void Version_changes_with_every_accepted_frame_and_reset_clears_the_data()
    {
        var aggregator = Feed(10);
        var version = aggregator.Version;

        aggregator.AddPresent(1020);
        Assert.NotEqual(version, aggregator.Version);

        aggregator.Reset();
        Assert.False(aggregator.GetStats().HasData);
        Assert.Equal(0, aggregator.GetStats().FrameCount);
    }
}
```

- [ ] **Step 6: Run and see it fail**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-class "Supermonitor.Tests.Frames.FrameTimeAggregatorTests"`
Expected: compile error (`FrameTimeAggregator` does not exist).

- [ ] **Step 7: Implement the aggregator**

`src/Supermonitor.Core/Frames/FrameTimeAggregator.cs`:

```csharp
using Supermonitor.Core.Collections;

namespace Supermonitor.Core.Frames;

public readonly record struct FrameStats(bool HasData, double Fps, double FrameTimeMs, double Low1PercentFps, long FrameCount)
{
    public static FrameStats None { get; } = new(false, double.NaN, double.NaN, double.NaN, 0);
}

// Receives the instants (ms) of the target process's presents and computes FPS, frametime and 1% low.
// Written by the ETW thread and read by the overlay and dashboard threads; everything under a short lock.
public sealed class FrameTimeAggregator
{
    public const int Capacity = 16384;
    public const double FpsWindowMs = 1000;
    public const double LowWindowMs = 10_000;
    public const double MaxGapMs = 5000;

    private readonly Lock _gate = new();
    private readonly RingBuffer<double> _timestamps = new(Capacity);
    private readonly RingBuffer<float> _frameTimes = new(Capacity);
    private readonly float[] _scratch = new float[Capacity];
    private double _last = double.NaN;
    private long _frameCount;
    private long _version;

    public long Version => Interlocked.Read(ref _version);

    public void AddPresent(double timestampMs)
    {
        lock (_gate)
        {
            if (double.IsNaN(_last))
            {
                _last = timestampMs;
                return;
            }

            var delta = timestampMs - _last;
            if (delta <= 0)
            {
                return;
            }

            if (delta > MaxGapMs)
            {
                // Long pause (loading, alt-tab, minimized game): restarts the sequence without recording the interval.
                _timestamps.Clear();
                _frameTimes.Clear();
                _last = timestampMs;
                _version++;
                return;
            }

            _timestamps.Add(timestampMs);
            _frameTimes.Add((float)delta);
            _last = timestampMs;
            _frameCount++;
            _version++;
        }
    }

    public FrameStats GetStats()
    {
        lock (_gate)
        {
            var count = _frameTimes.Count;
            if (count == 0)
            {
                return FrameStats.None;
            }

            var latest = _timestamps[count - 1];
            double sum = 0;
            var inFpsWindow = 0;
            var inLowWindow = 0;
            for (var i = count - 1; i >= 0; i--)
            {
                var age = latest - _timestamps[i];
                if (age >= LowWindowMs)
                {
                    break;
                }

                var frameTime = _frameTimes[i];
                _scratch[inLowWindow++] = frameTime;
                if (age < FpsWindowMs)
                {
                    sum += frameTime;
                    inFpsWindow++;
                }
            }

            var fps = sum > 0 ? inFpsWindow * 1000d / sum : double.NaN;
            var p99 = Percentile.NearestRank(_scratch.AsSpan(0, inLowWindow), 99);
            var low = p99 > 0 ? 1000d / p99 : double.NaN;
            return new FrameStats(true, fps, _frameTimes.Latest, low, _frameCount);
        }
    }

    public int CopyFrameTimes(Span<float> destination)
    {
        lock (_gate)
        {
            return _frameTimes.CopyLast(destination);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _timestamps.Clear();
            _frameTimes.Clear();
            _last = double.NaN;
            _frameCount = 0;
            _version++;
        }
    }
}
```

- [ ] **Step 8: Run and see it pass**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: all pass (the previous ones + 18 new).

- [ ] **Step 9: Commit**

```powershell
git add src/Supermonitor.Core/Frames tests/Supermonitor.Tests/Frames
git commit -m "feat(core): frame time aggregator with FPS, frame time and 1% low"
```

---

### Task 2: Overlay settings (schema v3), presets and editor

**Files:**
- Create: `src/Supermonitor.Core/Overlay/OverlaySettings.cs`, `src/Supermonitor.Core/Overlay/OverlayPresets.cs`, `src/Supermonitor.Core/Overlay/OverlayEditor.cs`
- Modify: `src/Supermonitor.Core/Settings/AppSettings.cs`, `src/Supermonitor.Core/Settings/SettingsMigrator.cs`, `src/Supermonitor.Core/Settings/SettingsJsonContext.cs`, `tests/Supermonitor.Tests/Settings/AppSettingsTests.cs`
- Test: `tests/Supermonitor.Tests/Overlay/OverlaySettingsTests.cs`, `tests/Supermonitor.Tests/Overlay/OverlayEditorTests.cs`, `tests/Supermonitor.Tests/Settings/SettingsMigratorTests.cs`

**Interfaces:**
- Consumes: `ColorText.TryParseRgb`, `MetricThreshold`, `AppSettings`, `SettingsStore`.
- Produces (namespace `Supermonitor.Core.Overlay`):
  - `enum OverlayAnchor { TopLeft, TopCenter, TopRight, MiddleLeft, Center, MiddleRight, BottomLeft, BottomCenter, BottomRight }`
  - `enum OverlayLayout { Horizontal, Vertical, Compact }`
  - `enum OverlayMetric { Fps, FrameTime, Low1Percent, CpuUsage, CpuTemperature, CpuClock, CpuPower, GpuUsage, GpuTemperature, GpuClock, GpuPower, GpuVram, Ram, NetworkDown, NetworkUp, Ping, Clock }`
  - `class OverlayItemSettings { OverlayMetric Metric; string? LabelColor; string? ValueColor; MetricThreshold? Threshold; OverlayItemSettings Clone(); }`
  - `class OverlayStyle` with `OverlayAnchor Anchor`, `int OffsetX`, `int OffsetY`, `OverlayLayout Layout`, `string FontFamily`, `float FontSize`, `string LabelColor`, `string ValueColor`, `string BackgroundColor`, `float BackgroundOpacity`, `int CornerRadius`, `int Padding`, `bool ShowFrameGraph`, `int GraphWidth`, `int GraphHeight`, `int RefreshHz`, `List<OverlayItemSettings> Items`; `static IReadOnlyList<int> AllowedRefreshHz`; `OverlayStyle Clone()`; `void Normalize()`.
  - `class OverlayPreset { string Name; OverlayStyle Style; }`
  - `class OverlaySettings { bool Enabled; string? MonitorId; OverlayStyle Style; List<OverlayPreset> UserPresets; void Normalize(); }`
  - `static class OverlayPresets { const string MinimalName, DefaultName, FullName; OverlayStyle Minimal(); OverlayStyle Default(); OverlayStyle Full(); IReadOnlyList<OverlayPreset> BuiltIn(); }`
  - `static class OverlayEditor { bool AddItem(OverlayStyle, OverlayMetric); bool RemoveItem(OverlayStyle, OverlayMetric); int MoveItem(OverlayStyle, OverlayMetric, int delta); void ApplyPreset(OverlaySettings, OverlayPreset); bool SavePreset(OverlaySettings, string name); bool DeletePreset(OverlaySettings, string name); IReadOnlyList<OverlayPreset> AllPresets(OverlaySettings); string? MatchingPresetName(OverlaySettings); }`
  - `AppSettings.Overlay` (`OverlaySettings`), `SettingsMigrator.CurrentVersion == 3`.

- [ ] **Step 1: Write the settings tests**

`tests/Supermonitor.Tests/Overlay/OverlaySettingsTests.cs`:

```csharp
using System.Text.Json;
using Supermonitor.Core.Overlay;
using Supermonitor.Core.Settings;

namespace Supermonitor.Tests.Overlay;

public class OverlaySettingsTests
{
    [Fact]
    public void Defaults_are_disabled_with_the_default_preset_items()
    {
        var settings = new AppSettings();

        Assert.False(settings.Overlay.Enabled);
        Assert.Null(settings.Overlay.MonitorId);
        Assert.Equal(OverlayAnchor.TopLeft, settings.Overlay.Style.Anchor);
        Assert.Equal(4, settings.Overlay.Style.RefreshHz);
        Assert.Contains(settings.Overlay.Style.Items, i => i.Metric == OverlayMetric.Fps);
        Assert.Equal(3, settings.Version);
    }

    [Fact]
    public void Built_in_presets_are_already_normalized()
    {
        foreach (var preset in OverlayPresets.BuiltIn())
        {
            var before = JsonSerializer.Serialize(preset.Style, SettingsJsonContext.Default.OverlayStyle);
            preset.Style.Normalize();
            var after = JsonSerializer.Serialize(preset.Style, SettingsJsonContext.Default.OverlayStyle);

            Assert.Equal(before, after);
        }
    }

    [Fact]
    public void Built_in_presets_are_minimal_default_and_full()
    {
        var presets = OverlayPresets.BuiltIn();

        Assert.Equal(new[] { "Minimal", "Default", "Full" }, presets.Select(p => p.Name));
        Assert.Equal(new[] { OverlayMetric.Fps }, presets[0].Style.Items.Select(i => i.Metric));
        Assert.Equal(Enum.GetValues<OverlayMetric>().Length, presets[2].Style.Items.Count);
    }

    [Fact]
    public void Built_in_presets_are_independent_copies()
    {
        var first = OverlayPresets.Default();
        first.Items.Clear();

        Assert.NotEmpty(OverlayPresets.Default().Items);
    }

    [Fact]
    public void Normalize_replaces_invalid_values_with_defaults()
    {
        var style = new OverlayStyle
        {
            Anchor = (OverlayAnchor)99,
            Layout = (OverlayLayout)99,
            FontFamily = "  ",
            FontSize = float.NaN,
            LabelColor = "blue",
            ValueColor = "#12",
            BackgroundColor = null!,
            BackgroundOpacity = 7,
            CornerRadius = 500,
            Padding = -3,
            GraphWidth = 1,
            GraphHeight = 9999,
            RefreshHz = 3,
            OffsetX = 1_000_000,
            OffsetY = -1_000_000,
        };

        style.Normalize();

        Assert.Equal(OverlayAnchor.TopLeft, style.Anchor);
        Assert.Equal(OverlayLayout.Vertical, style.Layout);
        Assert.Equal("Segoe UI", style.FontFamily);
        Assert.Equal(14f, style.FontSize);
        Assert.Equal("#B0B0B0", style.LabelColor);
        Assert.Equal("#FFFFFF", style.ValueColor);
        Assert.Equal("#000000", style.BackgroundColor);
        Assert.Equal(1f, style.BackgroundOpacity);
        Assert.Equal(32, style.CornerRadius);
        Assert.Equal(0, style.Padding);
        Assert.Equal(40, style.GraphWidth);
        Assert.Equal(200, style.GraphHeight);
        Assert.Equal(4, style.RefreshHz);
        Assert.Equal(4000, style.OffsetX);
        Assert.Equal(-4000, style.OffsetY);
    }

    [Fact]
    public void Normalize_uppercases_colors_and_clamps_the_font_size()
    {
        var style = new OverlayStyle { LabelColor = "#aabbcc", FontSize = 400 };

        style.Normalize();

        Assert.Equal("#AABBCC", style.LabelColor);
        Assert.Equal(48f, style.FontSize);
    }

    [Fact]
    public void Normalize_drops_duplicate_and_unknown_items_and_bad_item_values()
    {
        var style = new OverlayStyle
        {
            Items =
            [
                new() { Metric = OverlayMetric.Fps, ValueColor = "red" },
                new() { Metric = OverlayMetric.Fps },
                new() { Metric = (OverlayMetric)99 },
                null!,
                new() { Metric = OverlayMetric.CpuTemperature, LabelColor = "#ff0000", Threshold = new() { Warning = 90, Critical = 80 } },
                new() { Metric = OverlayMetric.GpuTemperature, Threshold = new() { Warning = float.NaN, Critical = 10 } },
            ],
        };

        style.Normalize();

        Assert.Equal(new[] { OverlayMetric.Fps, OverlayMetric.CpuTemperature, OverlayMetric.GpuTemperature }, style.Items.Select(i => i.Metric));
        Assert.Null(style.Items[0].ValueColor);
        Assert.Equal("#FF0000", style.Items[1].LabelColor);
        Assert.Equal(80, style.Items[1].Threshold!.Warning);
        Assert.Equal(90, style.Items[1].Threshold!.Critical);
        Assert.Null(style.Items[2].Threshold);
    }

    [Fact]
    public void Normalize_restores_null_collections()
    {
        var overlay = new OverlaySettings { Style = null!, UserPresets = null!, MonitorId = "  " };

        overlay.Normalize();

        Assert.NotNull(overlay.Style);
        Assert.Empty(overlay.UserPresets);
        Assert.Null(overlay.MonitorId);
    }

    [Fact]
    public void Normalize_drops_user_presets_that_are_unnamed_duplicated_or_shadow_a_built_in()
    {
        var overlay = new OverlaySettings
        {
            UserPresets =
            [
                new() { Name = " Mine ", Style = new OverlayStyle() },
                new() { Name = "mine", Style = new OverlayStyle() },
                new() { Name = "", Style = new OverlayStyle() },
                new() { Name = "default", Style = new OverlayStyle() },
                new() { Name = "Broken", Style = null! },
            ],
        };

        overlay.Normalize();

        Assert.Equal(new[] { "Mine" }, overlay.UserPresets.Select(p => p.Name));
    }

    [Fact]
    public void App_settings_normalize_covers_the_overlay()
    {
        var settings = new AppSettings { Overlay = null! };

        settings.Normalize();

        Assert.NotNull(settings.Overlay);
    }

    [Fact]
    public void Overlay_settings_round_trip_through_json_with_string_enums()
    {
        var settings = new AppSettings();
        settings.Overlay.Enabled = true;
        settings.Overlay.MonitorId = @"\\.\DISPLAY2";
        settings.Overlay.Style.Anchor = OverlayAnchor.BottomRight;
        settings.Overlay.Style.Items[0].Threshold = new MetricThreshold { Warning = 30, Critical = 20 };
        settings.Overlay.UserPresets.Add(new OverlayPreset { Name = "Mine", Style = OverlayPresets.Minimal() });

        var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
        var loaded = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)!;

        Assert.Contains("\"anchor\": \"BottomRight\"", json);
        Assert.Contains("\"metric\": \"Fps\"", json);
        Assert.True(loaded.Overlay.Enabled);
        Assert.Equal(@"\\.\DISPLAY2", loaded.Overlay.MonitorId);
        Assert.Equal(OverlayAnchor.BottomRight, loaded.Overlay.Style.Anchor);
        Assert.Equal(30, loaded.Overlay.Style.Items[0].Threshold!.Warning);
        Assert.Equal("Mine", loaded.Overlay.UserPresets.Single().Name);
    }
}
```

`tests/Supermonitor.Tests/Settings/SettingsMigratorTests.cs` — add inside the class:

```csharp
    [Fact]
    public void Version_two_document_is_bumped_to_three_and_keeps_its_fields()
    {
        var root = JsonNode.Parse("""{ "version": 2, "pingHost": "9.9.9.9", "cards": [ { "metricId": "cpu.usage" } ] }""")!.AsObject();

        var migrated = SettingsMigrator.Migrate(root);

        Assert.Equal(3, migrated["version"]!.GetValue<int>());
        Assert.Equal("9.9.9.9", migrated["pingHost"]!.GetValue<string>());
        Assert.Single(migrated["cards"]!.AsArray());
        Assert.Null(migrated["overlay"]);
    }

    [Fact]
    public void Version_two_file_loads_with_default_overlay_settings()
    {
        var dir = Directory.CreateTempSubdirectory("smtest");
        try
        {
            var path = Path.Combine(dir.FullName, "settings.json");
            File.WriteAllText(path, """{ "version": 2, "pingHost": "9.9.9.9" }""");
            using var store = new SettingsStore(path, TimeProvider.System, TimeSpan.FromMilliseconds(500));

            var result = store.Load();

            Assert.Equal(SettingsLoadResult.Loaded, result);
            Assert.Equal("9.9.9.9", store.Current.PingHost);
            Assert.False(store.Current.Overlay.Enabled);
            Assert.Equal(3, store.Current.Version);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
```

And in `tests/Supermonitor.Tests/Settings/AppSettingsTests.cs` replace `Assert.Equal(2, settings.Version);` with `Assert.Equal(3, settings.Version);`.

- [ ] **Step 2: Write the editor tests**

`tests/Supermonitor.Tests/Overlay/OverlayEditorTests.cs`:

```csharp
using Supermonitor.Core.Overlay;

namespace Supermonitor.Tests.Overlay;

public class OverlayEditorTests
{
    private static OverlayStyle Style(params OverlayMetric[] metrics) =>
        new() { Items = metrics.Select(m => new OverlayItemSettings { Metric = m }).ToList() };

    [Fact]
    public void Add_appends_a_new_metric_once()
    {
        var style = Style(OverlayMetric.Fps);

        Assert.True(OverlayEditor.AddItem(style, OverlayMetric.Ping));
        Assert.False(OverlayEditor.AddItem(style, OverlayMetric.Ping));
        Assert.Equal(new[] { OverlayMetric.Fps, OverlayMetric.Ping }, style.Items.Select(i => i.Metric));
    }

    [Fact]
    public void Remove_deletes_only_an_existing_metric()
    {
        var style = Style(OverlayMetric.Fps, OverlayMetric.Ping);

        Assert.True(OverlayEditor.RemoveItem(style, OverlayMetric.Fps));
        Assert.False(OverlayEditor.RemoveItem(style, OverlayMetric.Fps));
        Assert.Equal(new[] { OverlayMetric.Ping }, style.Items.Select(i => i.Metric));
    }

    [Fact]
    public void Move_shifts_an_item_and_clamps_at_the_ends()
    {
        var style = Style(OverlayMetric.Fps, OverlayMetric.FrameTime, OverlayMetric.Ping);

        Assert.Equal(2, OverlayEditor.MoveItem(style, OverlayMetric.Fps, 5));
        Assert.Equal(new[] { OverlayMetric.FrameTime, OverlayMetric.Ping, OverlayMetric.Fps }, style.Items.Select(i => i.Metric));
        Assert.Equal(0, OverlayEditor.MoveItem(style, OverlayMetric.Fps, -9));
        Assert.Equal(-1, OverlayEditor.MoveItem(style, OverlayMetric.Clock, 1));
    }

    [Fact]
    public void Apply_preset_copies_the_style_and_keeps_enabled_and_monitor()
    {
        var overlay = new OverlaySettings { Enabled = true, MonitorId = "M1" };
        var preset = new OverlayPreset { Name = "Mine", Style = OverlayPresets.Minimal() };

        OverlayEditor.ApplyPreset(overlay, preset);
        overlay.Style.Items.Clear();

        Assert.True(overlay.Enabled);
        Assert.Equal("M1", overlay.MonitorId);
        Assert.Single(preset.Style.Items);
        Assert.Equal(OverlayLayout.Compact, preset.Style.Layout);
    }

    [Fact]
    public void Save_preset_stores_a_copy_under_a_trimmed_name_and_replaces_by_name()
    {
        var overlay = new OverlaySettings { Style = Style(OverlayMetric.Fps) };

        Assert.True(OverlayEditor.SavePreset(overlay, "  Mine "));
        overlay.Style.Items.Add(new OverlayItemSettings { Metric = OverlayMetric.Ping });
        Assert.True(OverlayEditor.SavePreset(overlay, "mine"));

        var preset = Assert.Single(overlay.UserPresets);
        Assert.Equal("mine", preset.Name);
        Assert.Equal(2, preset.Style.Items.Count);
        overlay.Style.Items.Clear();
        Assert.Equal(2, preset.Style.Items.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Default")]
    [InlineData("minimal")]
    [InlineData("FULL")]
    public void Save_preset_rejects_blank_and_built_in_names(string name)
    {
        var overlay = new OverlaySettings();

        Assert.False(OverlayEditor.SavePreset(overlay, name));
        Assert.Empty(overlay.UserPresets);
    }

    [Fact]
    public void Delete_preset_removes_only_user_presets()
    {
        var overlay = new OverlaySettings();
        OverlayEditor.SavePreset(overlay, "Mine");

        Assert.False(OverlayEditor.DeletePreset(overlay, "Default"));
        Assert.True(OverlayEditor.DeletePreset(overlay, "MINE"));
        Assert.False(OverlayEditor.DeletePreset(overlay, "Mine"));
        Assert.Empty(overlay.UserPresets);
    }

    [Fact]
    public void All_presets_lists_built_in_first_then_user_presets()
    {
        var overlay = new OverlaySettings();
        OverlayEditor.SavePreset(overlay, "Mine");

        Assert.Equal(new[] { "Minimal", "Default", "Full", "Mine" }, OverlayEditor.AllPresets(overlay).Select(p => p.Name));
    }

    [Fact]
    public void Matching_preset_name_follows_the_current_style()
    {
        var overlay = new OverlaySettings { Style = OverlayPresets.Full() };
        Assert.Equal("Full", OverlayEditor.MatchingPresetName(overlay));

        overlay.Style.FontSize += 1;
        Assert.Null(OverlayEditor.MatchingPresetName(overlay));

        OverlayEditor.SavePreset(overlay, "Mine");
        Assert.Equal("Mine", OverlayEditor.MatchingPresetName(overlay));
    }
}
```

- [ ] **Step 3: Run and see it fail**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: compile error (overlay types do not exist).

- [ ] **Step 4: Implement the settings**

`src/Supermonitor.Core/Overlay/OverlaySettings.cs`:

```csharp
using Supermonitor.Core.Settings;

namespace Supermonitor.Core.Overlay;

public enum OverlayAnchor
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    Center,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

public enum OverlayLayout
{
    Horizontal,
    Vertical,
    Compact,
}

public enum OverlayMetric
{
    Fps,
    FrameTime,
    Low1Percent,
    CpuUsage,
    CpuTemperature,
    CpuClock,
    CpuPower,
    GpuUsage,
    GpuTemperature,
    GpuClock,
    GpuPower,
    GpuVram,
    Ram,
    NetworkDown,
    NetworkUp,
    Ping,
    Clock,
}

public sealed class OverlayItemSettings
{
    public OverlayMetric Metric { get; set; }

    public string? LabelColor { get; set; }

    public string? ValueColor { get; set; }

    public MetricThreshold? Threshold { get; set; }

    public OverlayItemSettings Clone() => new()
    {
        Metric = Metric,
        LabelColor = LabelColor,
        ValueColor = ValueColor,
        Threshold = Threshold is null ? null : new MetricThreshold { Warning = Threshold.Warning, Critical = Threshold.Critical },
    };
}

public sealed class OverlayStyle
{
    public const string DefaultFontFamily = "Segoe UI";
    public const string DefaultLabelColor = "#B0B0B0";
    public const string DefaultValueColor = "#FFFFFF";
    public const string DefaultBackgroundColor = "#000000";
    public const int DefaultRefreshHz = 4;
    private const int MaxOffset = 4000;

    public static IReadOnlyList<int> AllowedRefreshHz { get; } = [1, 2, 4, 10, 30];

    public OverlayAnchor Anchor { get; set; } = OverlayAnchor.TopLeft;

    public int OffsetX { get; set; } = 16;

    public int OffsetY { get; set; } = 16;

    public OverlayLayout Layout { get; set; } = OverlayLayout.Vertical;

    public string FontFamily { get; set; } = DefaultFontFamily;

    public float FontSize { get; set; } = 14;

    public string LabelColor { get; set; } = DefaultLabelColor;

    public string ValueColor { get; set; } = DefaultValueColor;

    public string BackgroundColor { get; set; } = DefaultBackgroundColor;

    public float BackgroundOpacity { get; set; } = 0.55f;

    public int CornerRadius { get; set; } = 8;

    public int Padding { get; set; } = 10;

    public bool ShowFrameGraph { get; set; }

    public int GraphWidth { get; set; } = 160;

    public int GraphHeight { get; set; } = 40;

    public int RefreshHz { get; set; } = DefaultRefreshHz;

    public List<OverlayItemSettings> Items { get; set; } = DefaultItems();

    public static List<OverlayItemSettings> DefaultItems() => ItemsOf(
        OverlayMetric.Fps,
        OverlayMetric.FrameTime,
        OverlayMetric.Low1Percent,
        OverlayMetric.CpuUsage,
        OverlayMetric.CpuTemperature,
        OverlayMetric.GpuUsage,
        OverlayMetric.GpuTemperature,
        OverlayMetric.Ram);

    public static List<OverlayItemSettings> ItemsOf(params OverlayMetric[] metrics) =>
        metrics.Select(m => new OverlayItemSettings { Metric = m }).ToList();

    public OverlayStyle Clone()
    {
        var copy = (OverlayStyle)MemberwiseClone();
        copy.Items = Items.Select(i => i.Clone()).ToList();
        return copy;
    }

    public void Normalize()
    {
        Anchor = Enum.IsDefined(Anchor) ? Anchor : OverlayAnchor.TopLeft;
        Layout = Enum.IsDefined(Layout) ? Layout : OverlayLayout.Vertical;
        OffsetX = Math.Clamp(OffsetX, -MaxOffset, MaxOffset);
        OffsetY = Math.Clamp(OffsetY, -MaxOffset, MaxOffset);
        FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? DefaultFontFamily : FontFamily.Trim();
        FontSize = float.IsFinite(FontSize) ? Math.Clamp(FontSize, 8f, 48f) : 14f;
        LabelColor = ValidColor(LabelColor) ?? DefaultLabelColor;
        ValueColor = ValidColor(ValueColor) ?? DefaultValueColor;
        BackgroundColor = ValidColor(BackgroundColor) ?? DefaultBackgroundColor;
        BackgroundOpacity = float.IsFinite(BackgroundOpacity) ? Math.Clamp(BackgroundOpacity, 0f, 1f) : 0.55f;
        CornerRadius = Math.Clamp(CornerRadius, 0, 32);
        Padding = Math.Clamp(Padding, 0, 48);
        GraphWidth = Math.Clamp(GraphWidth, 40, 600);
        GraphHeight = Math.Clamp(GraphHeight, 16, 200);
        RefreshHz = AllowedRefreshHz.Contains(RefreshHz) ? RefreshHz : DefaultRefreshHz;

        Items = Items is null
            ? DefaultItems()
            : Items.Where(i => i is not null && Enum.IsDefined(i.Metric)).DistinctBy(i => i.Metric).ToList();
        foreach (var item in Items)
        {
            item.LabelColor = ValidColor(item.LabelColor);
            item.ValueColor = ValidColor(item.ValueColor);
            if (item.Threshold is { } threshold)
            {
                if (!float.IsFinite(threshold.Warning) || !float.IsFinite(threshold.Critical))
                {
                    item.Threshold = null;
                }
                else if (threshold.Warning > threshold.Critical)
                {
                    (threshold.Warning, threshold.Critical) = (threshold.Critical, threshold.Warning);
                }
            }
        }
    }

    private static string? ValidColor(string? text) =>
        ColorText.TryParseRgb(text, out _, out _, out _) ? text!.ToUpperInvariant() : null;
}

public sealed class OverlayPreset
{
    public string Name { get; set; } = string.Empty;

    public OverlayStyle Style { get; set; } = new();
}

public sealed class OverlaySettings
{
    public bool Enabled { get; set; }

    public string? MonitorId { get; set; }

    public OverlayStyle Style { get; set; } = OverlayPresets.Default();

    public List<OverlayPreset> UserPresets { get; set; } = [];

    public void Normalize()
    {
        MonitorId = string.IsNullOrWhiteSpace(MonitorId) ? null : MonitorId.Trim();
        Style ??= OverlayPresets.Default();
        Style.Normalize();

        var seen = new HashSet<string>(OverlayPresets.BuiltInNames, StringComparer.OrdinalIgnoreCase);
        var kept = new List<OverlayPreset>();
        foreach (var preset in UserPresets ?? [])
        {
            var name = preset?.Name?.Trim();
            if (preset?.Style is null || string.IsNullOrEmpty(name) || !seen.Add(name))
            {
                continue;
            }

            preset.Name = name;
            preset.Style.Normalize();
            kept.Add(preset);
        }

        UserPresets = kept;
    }
}
```

`src/Supermonitor.Core/Overlay/OverlayPresets.cs`:

```csharp
namespace Supermonitor.Core.Overlay;

public static class OverlayPresets
{
    public const string MinimalName = "Minimal";
    public const string DefaultName = "Default";
    public const string FullName = "Full";

    public static IReadOnlyList<string> BuiltInNames { get; } = [MinimalName, DefaultName, FullName];

    // FPS only, on one line.
    public static OverlayStyle Minimal() => new()
    {
        Layout = OverlayLayout.Compact,
        Padding = 6,
        BackgroundOpacity = 0.45f,
        Items = OverlayStyle.ItemsOf(OverlayMetric.Fps),
    };

    public static OverlayStyle Default() => new() { ShowFrameGraph = true };

    public static OverlayStyle Full() => new()
    {
        FontSize = 13,
        GraphWidth = 200,
        ShowFrameGraph = true,
        Items = OverlayStyle.ItemsOf(Enum.GetValues<OverlayMetric>()),
    };

    public static IReadOnlyList<OverlayPreset> BuiltIn() =>
    [
        new OverlayPreset { Name = MinimalName, Style = Minimal() },
        new OverlayPreset { Name = DefaultName, Style = Default() },
        new OverlayPreset { Name = FullName, Style = Full() },
    ];
}
```

`src/Supermonitor.Core/Overlay/OverlayEditor.cs`:

```csharp
using System.Text.Json;
using Supermonitor.Core.Settings;

namespace Supermonitor.Core.Overlay;

public static class OverlayEditor
{
    public static bool AddItem(OverlayStyle style, OverlayMetric metric)
    {
        if (style.Items.Any(i => i.Metric == metric))
        {
            return false;
        }

        style.Items.Add(new OverlayItemSettings { Metric = metric });
        return true;
    }

    public static bool RemoveItem(OverlayStyle style, OverlayMetric metric)
    {
        var index = style.Items.FindIndex(i => i.Metric == metric);
        if (index < 0)
        {
            return false;
        }

        style.Items.RemoveAt(index);
        return true;
    }

    public static int MoveItem(OverlayStyle style, OverlayMetric metric, int delta)
    {
        var index = style.Items.FindIndex(i => i.Metric == metric);
        if (index < 0)
        {
            return -1;
        }

        var target = Math.Clamp(index + delta, 0, style.Items.Count - 1);
        var item = style.Items[index];
        style.Items.RemoveAt(index);
        style.Items.Insert(target, item);
        return target;
    }

    public static void ApplyPreset(OverlaySettings overlay, OverlayPreset preset) => overlay.Style = preset.Style.Clone();

    public static bool SavePreset(OverlaySettings overlay, string name)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0 || OverlayPresets.BuiltInNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        overlay.UserPresets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        overlay.UserPresets.Add(new OverlayPreset { Name = name, Style = overlay.Style.Clone() });
        return true;
    }

    public static bool DeletePreset(OverlaySettings overlay, string name) =>
        overlay.UserPresets.RemoveAll(p => string.Equals(p.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase)) > 0;

    public static IReadOnlyList<OverlayPreset> AllPresets(OverlaySettings overlay) =>
        [.. OverlayPresets.BuiltIn(), .. overlay.UserPresets];

    // Name of the preset whose style is identical to the current one, or null when the user changed something.
    public static string? MatchingPresetName(OverlaySettings overlay)
    {
        var current = Serialize(overlay.Style);
        return AllPresets(overlay).FirstOrDefault(p => Serialize(p.Style) == current)?.Name;
    }

    private static string Serialize(OverlayStyle style) =>
        JsonSerializer.Serialize(style, SettingsJsonContext.Default.OverlayStyle);
}
```

`SettingsJsonContext` needs to expose `OverlayStyle`. In `src/Supermonitor.Core/Settings/SettingsJsonContext.cs` add the attribute (and `using Supermonitor.Core.Overlay;`):

```csharp
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(OverlayStyle))]
```

In `AppSettings.cs` (add `using Supermonitor.Core.Overlay;`): property `public OverlaySettings Overlay { get; set; } = new();` after `Thresholds`, and at the end of `Normalize()`:

```csharp
        Overlay ??= new OverlaySettings();
        Overlay.Normalize();
```

In `SettingsMigrator.cs`: `public const int CurrentVersion = 3;` and, before `root["version"] = version;`:

```csharp
        // v2 → v3: the "overlay" block is new; when absent, AppSettings uses the defaults. No field changed.
        if (version < 3)
        {
            version = 3;
        }
```

- [ ] **Step 5: Run and see it pass**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: all pass.

- [ ] **Step 6: Commit**

```powershell
git add src/Supermonitor.Core tests/Supermonitor.Tests
git commit -m "feat(core): overlay settings, presets and editor (settings schema v3)"
```

---

### Task 3: Overlay content (catalog, rows, placement, rate)

**Files:**
- Create: `src/Supermonitor.Core/Overlay/Rgba.cs`, `OverlayMetricCatalog.cs`, `IOverlayValues.cs`, `OverlayFrame.cs`, `OverlayFrameBuilder.cs`, `OverlayPlacement.cs`, `OverlayRefresh.cs` (all in `src/Supermonitor.Core/Overlay/`)
- Modify: `src/Supermonitor.Core/Sensors/DemandTracker.cs`
- Test: `tests/Supermonitor.Tests/Overlay/OverlayFrameBuilderTests.cs`, `OverlayPlacementTests.cs`, `OverlayCatalogTests.cs`

**Interfaces:**
- Consumes: `OverlayStyle`, `OverlayMetric`, `MetricThreshold`, `UnitFormatter`, `UnitOptions`, `MetricIds`, `MetricUnit`, `ThresholdEvaluator`, `ColorText`.
- Produces:
  - `Demand.FrameCapture` (new member of `Demand`).
  - `readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)` with `static bool TryParse(string? hex, out Rgba color)` and `static Rgba Parse(string hex, Rgba fallback)`.
  - `static class OverlayMetricCatalog { string Label(OverlayMetric); MetricUnit Unit(OverlayMetric); string? StoreMetricId(OverlayMetric); Demand? Demand(OverlayMetric); }` (MetricStore ids use GPU 0).
  - `static class OverlayDemand { (bool Hardware, bool Frames) For(OverlayStyle style); }`
  - `interface IOverlayValues { double Get(OverlayMetric metric); DateTime Now { get; } long FrameVersion { get; } int CopyFrameTimes(Span<float> destination); }`; `sealed class DemoOverlayValues : IOverlayValues`.
  - `sealed record OverlayRow(string Label, string Value, Rgba LabelColor, Rgba ValueColor)`; `sealed class OverlayFrame { OverlayRow[] Rows; float[] Graph; long GraphVersion; bool SameContentAs(OverlayFrame? other); }`
  - `static class OverlayFrameBuilder { OverlayFrame Build(OverlayStyle style, IOverlayValues values, UnitOptions units, IReadOnlyDictionary<string, MetricThreshold> thresholds); string FormatValue(OverlayMetric metric, double value, DateTime now, UnitOptions units); }`
  - `readonly record struct PixelRect(int X, int Y, int Width, int Height)` with `Right`/`Bottom`; `static class OverlayPlacement { (int X, int Y) Compute(OverlayAnchor anchor, int offsetX, int offsetY, PixelRect monitor, int width, int height); }`
  - `static class OverlayRefresh { int EffectiveHz(OverlayStyle style); }`

- [ ] **Step 1: Write the tests**

`tests/Supermonitor.Tests/Overlay/OverlayCatalogTests.cs`:

```csharp
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Overlay;
using Supermonitor.Core.Sensors;

namespace Supermonitor.Tests.Overlay;

public class OverlayCatalogTests
{
    [Fact]
    public void Every_metric_has_a_label_and_a_unit()
    {
        foreach (var metric in Enum.GetValues<OverlayMetric>())
        {
            Assert.False(string.IsNullOrWhiteSpace(OverlayMetricCatalog.Label(metric)), metric.ToString());
            Assert.True(Enum.IsDefined(OverlayMetricCatalog.Unit(metric)), metric.ToString());
        }
    }

    [Fact]
    public void Store_backed_metrics_use_known_ids_and_frame_metrics_have_none()
    {
        Assert.Equal(MetricIds.CpuUsage, OverlayMetricCatalog.StoreMetricId(OverlayMetric.CpuUsage));
        Assert.Equal(MetricIds.GpuTemperature(0), OverlayMetricCatalog.StoreMetricId(OverlayMetric.GpuTemperature));
        Assert.Equal(MetricIds.GpuVramUsed(0), OverlayMetricCatalog.StoreMetricId(OverlayMetric.GpuVram));
        Assert.Equal(MetricIds.MemoryUsage, OverlayMetricCatalog.StoreMetricId(OverlayMetric.Ram));
        Assert.Equal(MetricIds.NetworkTotalDown, OverlayMetricCatalog.StoreMetricId(OverlayMetric.NetworkDown));
        Assert.Equal(MetricIds.Ping, OverlayMetricCatalog.StoreMetricId(OverlayMetric.Ping));
        Assert.Null(OverlayMetricCatalog.StoreMetricId(OverlayMetric.Fps));
        Assert.Null(OverlayMetricCatalog.StoreMetricId(OverlayMetric.Clock));
    }

    [Fact]
    public void Demand_follows_the_sensor_behind_each_metric()
    {
        Assert.Equal(Demand.FrameCapture, OverlayMetricCatalog.Demand(OverlayMetric.Fps));
        Assert.Equal(Demand.FrameCapture, OverlayMetricCatalog.Demand(OverlayMetric.Low1Percent));
        Assert.Equal(Demand.HardwareSensors, OverlayMetricCatalog.Demand(OverlayMetric.CpuTemperature));
        Assert.Equal(Demand.HardwareSensors, OverlayMetricCatalog.Demand(OverlayMetric.GpuPower));
        Assert.Null(OverlayMetricCatalog.Demand(OverlayMetric.CpuUsage));
        Assert.Null(OverlayMetricCatalog.Demand(OverlayMetric.Ram));
    }

    [Fact]
    public void Demand_for_a_style_unions_its_items()
    {
        var minimal = OverlayDemand.For(OverlayPresets.Minimal());
        var full = OverlayDemand.For(OverlayPresets.Full());
        var network = OverlayDemand.For(new OverlayStyle { Items = OverlayStyle.ItemsOf(OverlayMetric.Ping, OverlayMetric.Clock) });

        Assert.Equal((false, true), minimal);
        Assert.Equal((true, true), full);
        Assert.Equal((false, false), network);
    }

    [Fact]
    public void Refresh_is_thirty_hertz_with_the_graph_and_the_configured_rate_without()
    {
        Assert.Equal(4, OverlayRefresh.EffectiveHz(new OverlayStyle { RefreshHz = 4, ShowFrameGraph = false }));
        Assert.Equal(1, OverlayRefresh.EffectiveHz(new OverlayStyle { RefreshHz = 1 }));
        Assert.Equal(30, OverlayRefresh.EffectiveHz(new OverlayStyle { RefreshHz = 4, ShowFrameGraph = true }));
    }
}
```

`tests/Supermonitor.Tests/Overlay/OverlayPlacementTests.cs`:

```csharp
using Supermonitor.Core.Overlay;

namespace Supermonitor.Tests.Overlay;

public class OverlayPlacementTests
{
    private static readonly PixelRect Monitor = new(100, 50, 1920, 1080);

    [Theory]
    [InlineData(OverlayAnchor.TopLeft, 110, 60)]
    [InlineData(OverlayAnchor.TopCenter, 970, 60)]
    [InlineData(OverlayAnchor.TopRight, 1810, 60)]
    [InlineData(OverlayAnchor.MiddleLeft, 110, 550)]
    [InlineData(OverlayAnchor.Center, 970, 550)]
    [InlineData(OverlayAnchor.MiddleRight, 1810, 550)]
    [InlineData(OverlayAnchor.BottomLeft, 110, 1020)]
    [InlineData(OverlayAnchor.BottomCenter, 970, 1020)]
    [InlineData(OverlayAnchor.BottomRight, 1810, 1020)]
    public void Anchors_place_the_overlay_with_the_offset_as_an_inner_margin(OverlayAnchor anchor, int x, int y)
    {
        // Overlay 200x100, offset 10 px. For right and bottom anchors the offset moves away from the edge; at the center, it shifts right/down.
        var position = OverlayPlacement.Compute(anchor, 10, 10, Monitor, 200, 100);

        Assert.Equal((x, y), position);
    }

    [Fact]
    public void Center_offset_shifts_right_and_down()
    {
        var centered = OverlayPlacement.Compute(OverlayAnchor.Center, 0, 0, Monitor, 200, 100);
        var shifted = OverlayPlacement.Compute(OverlayAnchor.Center, 30, -20, Monitor, 200, 100);

        Assert.Equal((centered.X + 30, centered.Y - 20), shifted);
    }

    [Fact]
    public void Position_is_clamped_inside_the_monitor()
    {
        var farRight = OverlayPlacement.Compute(OverlayAnchor.TopLeft, 99999, 99999, Monitor, 200, 100);
        var farLeft = OverlayPlacement.Compute(OverlayAnchor.TopLeft, -99999, -99999, Monitor, 200, 100);

        Assert.Equal((Monitor.Right - 200, Monitor.Bottom - 100), farRight);
        Assert.Equal((Monitor.X, Monitor.Y), farLeft);
    }

    [Fact]
    public void An_overlay_larger_than_the_monitor_sticks_to_the_top_left()
    {
        var position = OverlayPlacement.Compute(OverlayAnchor.BottomRight, 0, 0, new PixelRect(0, 0, 100, 100), 400, 400);

        Assert.Equal((0, 0), position);
    }
}
```

`tests/Supermonitor.Tests/Overlay/OverlayFrameBuilderTests.cs`:

```csharp
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Overlay;
using Supermonitor.Core.Settings;

namespace Supermonitor.Tests.Overlay;

public class OverlayFrameBuilderTests
{
    private sealed class FakeValues : IOverlayValues
    {
        public Dictionary<OverlayMetric, double> Values { get; } = [];

        public DateTime Now { get; set; } = new(2026, 10, 10, 9, 5, 0);

        public long FrameVersion { get; set; }

        public float[] FrameTimes { get; set; } = [];

        public double Get(OverlayMetric metric) => Values.GetValueOrDefault(metric, double.NaN);

        public int CopyFrameTimes(Span<float> destination)
        {
            var count = Math.Min(destination.Length, FrameTimes.Length);
            FrameTimes.AsSpan(FrameTimes.Length - count).CopyTo(destination);
            return count;
        }
    }

    private static readonly IReadOnlyDictionary<string, MetricThreshold> NoThresholds = new Dictionary<string, MetricThreshold>();

    private static OverlayFrame Build(OverlayStyle style, FakeValues values, UnitOptions? units = null, IReadOnlyDictionary<string, MetricThreshold>? thresholds = null) =>
        OverlayFrameBuilder.Build(style, values, units ?? UnitOptions.Default, thresholds ?? NoThresholds);

    [Fact]
    public void Rows_follow_the_item_order_with_labels_and_formatted_values()
    {
        var values = new FakeValues();
        values.Values[OverlayMetric.Fps] = 143.6;
        values.Values[OverlayMetric.FrameTime] = 6.94;
        values.Values[OverlayMetric.CpuUsage] = 23.4;
        var style = new OverlayStyle { Items = OverlayStyle.ItemsOf(OverlayMetric.CpuUsage, OverlayMetric.Fps, OverlayMetric.FrameTime) };

        var frame = Build(style, values);

        Assert.Equal(new[] { "CPU", "FPS", "Frametime" }, frame.Rows.Select(r => r.Label));
        Assert.Equal(new[] { "23%", "144", "6.9 ms" }, frame.Rows.Select(r => r.Value));
    }

    [Fact]
    public void Missing_values_show_na()
    {
        var frame = Build(new OverlayStyle { Items = OverlayStyle.ItemsOf(OverlayMetric.Fps, OverlayMetric.GpuTemperature) }, new FakeValues());

        Assert.All(frame.Rows, r => Assert.Equal("N/A", r.Value));
    }

    [Fact]
    public void Units_follow_the_user_options()
    {
        var values = new FakeValues();
        values.Values[OverlayMetric.CpuTemperature] = 50;
        values.Values[OverlayMetric.NetworkDown] = 1_000_000;
        var style = new OverlayStyle { Items = OverlayStyle.ItemsOf(OverlayMetric.CpuTemperature, OverlayMetric.NetworkDown) };

        var celsius = Build(style, values);
        var imperial = Build(style, values, new UnitOptions(NetworkInBits: true, Fahrenheit: true));

        Assert.Equal("50 °C", celsius.Rows[0].Value);
        Assert.Equal("122 °F", imperial.Rows[0].Value);
        Assert.Equal("8.0 Mbps", imperial.Rows[1].Value);
    }

    [Fact]
    public void Clock_shows_hours_and_minutes()
    {
        var style = new OverlayStyle { Items = OverlayStyle.ItemsOf(OverlayMetric.Clock) };

        var frame = Build(style, new FakeValues { Now = new DateTime(2026, 10, 10, 21, 7, 33) });

        Assert.Equal("21:07", frame.Rows[0].Value);
    }

    [Fact]
    public void Colors_come_from_the_style_unless_the_item_overrides_them()
    {
        var values = new FakeValues();
        values.Values[OverlayMetric.Fps] = 100;
        values.Values[OverlayMetric.Ping] = 20;
        var style = new OverlayStyle
        {
            LabelColor = "#111111",
            ValueColor = "#222222",
            Items =
            [
                new() { Metric = OverlayMetric.Fps },
                new() { Metric = OverlayMetric.Ping, LabelColor = "#AA0000", ValueColor = "#00BB00" },
            ],
        };

        var frame = Build(style, values);

        Assert.Equal(new Rgba(0x11, 0x11, 0x11), frame.Rows[0].LabelColor);
        Assert.Equal(new Rgba(0x22, 0x22, 0x22), frame.Rows[0].ValueColor);
        Assert.Equal(new Rgba(0xAA, 0, 0), frame.Rows[1].LabelColor);
        Assert.Equal(new Rgba(0, 0xBB, 0), frame.Rows[1].ValueColor);
    }

    [Fact]
    public void Thresholds_color_the_value_using_the_base_unit()
    {
        var values = new FakeValues();
        values.Values[OverlayMetric.CpuTemperature] = 85;
        values.Values[OverlayMetric.GpuTemperature] = 95;
        values.Values[OverlayMetric.CpuUsage] = 10;
        var thresholds = new Dictionary<string, MetricThreshold>
        {
            [MetricIds.CpuTemperature] = new() { Warning = 80, Critical = 90 },
            [MetricIds.GpuTemperature(0)] = new() { Warning = 80, Critical = 90 },
        };
        var style = new OverlayStyle { Items = OverlayStyle.ItemsOf(OverlayMetric.CpuTemperature, OverlayMetric.GpuTemperature, OverlayMetric.CpuUsage) };

        var frame = Build(style, values, new UnitOptions(Fahrenheit: true), thresholds);

        Assert.Equal(OverlayFrameBuilder.WarningColor, frame.Rows[0].ValueColor);
        Assert.Equal(OverlayFrameBuilder.CriticalColor, frame.Rows[1].ValueColor);
        Assert.Equal(new Rgba(0xFF, 0xFF, 0xFF), frame.Rows[2].ValueColor);
    }

    [Fact]
    public void An_item_threshold_wins_over_the_global_one()
    {
        var values = new FakeValues();
        values.Values[OverlayMetric.CpuTemperature] = 85;
        var thresholds = new Dictionary<string, MetricThreshold> { [MetricIds.CpuTemperature] = new() { Warning = 80, Critical = 90 } };
        var style = new OverlayStyle
        {
            Items = [new() { Metric = OverlayMetric.CpuTemperature, Threshold = new MetricThreshold { Warning = 60, Critical = 80 } }],
        };

        var frame = Build(style, values, thresholds: thresholds);

        Assert.Equal(OverlayFrameBuilder.CriticalColor, frame.Rows[0].ValueColor);
    }

    [Fact]
    public void Graph_is_filled_only_when_enabled_and_sized_by_the_graph_width()
    {
        var values = new FakeValues { FrameVersion = 7, FrameTimes = Enumerable.Range(1, 500).Select(i => (float)i).ToArray() };

        var off = Build(new OverlayStyle { ShowFrameGraph = false }, values);
        var on = Build(new OverlayStyle { ShowFrameGraph = true, GraphWidth = 160 }, values);

        Assert.Empty(off.Graph);
        Assert.Equal(80, on.Graph.Length);
        Assert.Equal(500f, on.Graph[^1]);
        Assert.Equal(7, on.GraphVersion);
    }

    [Fact]
    public void Same_content_ignores_the_graph_version_when_the_graph_is_off()
    {
        var values = new FakeValues();
        values.Values[OverlayMetric.Fps] = 60;
        var style = new OverlayStyle { Items = OverlayStyle.ItemsOf(OverlayMetric.Fps) };

        var first = Build(style, values);
        values.FrameVersion = 99;
        var second = Build(style, values);
        values.Values[OverlayMetric.Fps] = 61;
        var third = Build(style, values);

        Assert.True(first.SameContentAs(second));
        Assert.False(first.SameContentAs(third));
        Assert.False(first.SameContentAs(null));
    }

    [Fact]
    public void Same_content_notices_a_new_frame_when_the_graph_is_on()
    {
        var values = new FakeValues { FrameTimes = [10f, 11f] };
        var style = new OverlayStyle { ShowFrameGraph = true, Items = OverlayStyle.ItemsOf(OverlayMetric.Fps) };

        values.FrameVersion = 1;
        var first = Build(style, values);
        values.FrameVersion = 2;
        var second = Build(style, values);

        Assert.False(first.SameContentAs(second));
    }

    [Fact]
    public void Demo_values_build_a_full_frame_for_every_metric()
    {
        var demo = new DemoOverlayValues();

        var frame = Build(OverlayPresets.Full(), demo);

        Assert.Equal(Enum.GetValues<OverlayMetric>().Length, frame.Rows.Length);
        Assert.All(frame.Rows, r => Assert.NotEqual("N/A", r.Value));
        Assert.NotEmpty(frame.Graph);
    }

    private static OverlayFrame Build(OverlayStyle style, IOverlayValues values) =>
        OverlayFrameBuilder.Build(style, values, UnitOptions.Default, NoThresholds);
}
```

- [ ] **Step 2: Run and see it fail**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: compile error (new types do not exist).

- [ ] **Step 3: Implement**

`Demand` (`src/Supermonitor.Core/Sensors/DemandTracker.cs`): add the member

```csharp
public enum Demand
{
    HardwareSensors,
    Processes,
    FrameCapture,
}
```

`src/Supermonitor.Core/Overlay/Rgba.cs`:

```csharp
using Supermonitor.Core.Settings;

namespace Supermonitor.Core.Overlay;

public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    public static bool TryParse(string? hex, out Rgba color)
    {
        if (ColorText.TryParseRgb(hex, out var r, out var g, out var b))
        {
            color = new Rgba(r, g, b);
            return true;
        }

        color = default;
        return false;
    }

    public static Rgba Parse(string? hex, Rgba fallback) => TryParse(hex, out var color) ? color : fallback;
}
```

`src/Supermonitor.Core/Overlay/OverlayMetricCatalog.cs`:

```csharp
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors;

namespace Supermonitor.Core.Overlay;

public static class OverlayMetricCatalog
{
    public static string Label(OverlayMetric metric) => metric switch
    {
        OverlayMetric.Fps => "FPS",
        OverlayMetric.FrameTime => "Frametime",
        OverlayMetric.Low1Percent => "1% low",
        OverlayMetric.CpuUsage => "CPU",
        OverlayMetric.CpuTemperature => "CPU temp",
        OverlayMetric.CpuClock => "CPU clock",
        OverlayMetric.CpuPower => "CPU power",
        OverlayMetric.GpuUsage => "GPU",
        OverlayMetric.GpuTemperature => "GPU temp",
        OverlayMetric.GpuClock => "GPU clock",
        OverlayMetric.GpuPower => "GPU power",
        OverlayMetric.GpuVram => "VRAM",
        OverlayMetric.Ram => "RAM",
        OverlayMetric.NetworkDown => "Down",
        OverlayMetric.NetworkUp => "Up",
        OverlayMetric.Ping => "Ping",
        OverlayMetric.Clock => "Time",
        _ => metric.ToString(),
    };

    public static MetricUnit Unit(OverlayMetric metric) => metric switch
    {
        OverlayMetric.Fps or OverlayMetric.Low1Percent => MetricUnit.FramesPerSecond,
        OverlayMetric.FrameTime or OverlayMetric.Ping => MetricUnit.Milliseconds,
        OverlayMetric.CpuUsage or OverlayMetric.GpuUsage or OverlayMetric.Ram => MetricUnit.Percent,
        OverlayMetric.CpuTemperature or OverlayMetric.GpuTemperature => MetricUnit.Celsius,
        OverlayMetric.CpuClock or OverlayMetric.GpuClock => MetricUnit.Megahertz,
        OverlayMetric.CpuPower or OverlayMetric.GpuPower => MetricUnit.Watts,
        OverlayMetric.GpuVram => MetricUnit.Bytes,
        OverlayMetric.NetworkDown or OverlayMetric.NetworkUp => MetricUnit.BytesPerSecond,
        _ => MetricUnit.Count,
    };

    // MetricStore id; null for FPS/frametime/1% low (they come from FrameCaptureService) and for the clock.
    public static string? StoreMetricId(OverlayMetric metric) => metric switch
    {
        OverlayMetric.CpuUsage => MetricIds.CpuUsage,
        OverlayMetric.CpuTemperature => MetricIds.CpuTemperature,
        OverlayMetric.CpuClock => MetricIds.CpuClock,
        OverlayMetric.CpuPower => MetricIds.CpuPower,
        OverlayMetric.GpuUsage => MetricIds.GpuUsage(0),
        OverlayMetric.GpuTemperature => MetricIds.GpuTemperature(0),
        OverlayMetric.GpuClock => MetricIds.GpuClock(0),
        OverlayMetric.GpuPower => MetricIds.GpuPower(0),
        OverlayMetric.GpuVram => MetricIds.GpuVramUsed(0),
        OverlayMetric.Ram => MetricIds.MemoryUsage,
        OverlayMetric.NetworkDown => MetricIds.NetworkTotalDown,
        OverlayMetric.NetworkUp => MetricIds.NetworkTotalUp,
        OverlayMetric.Ping => MetricIds.Ping,
        _ => null,
    };

    public static Demand? Demand(OverlayMetric metric) => metric switch
    {
        OverlayMetric.Fps or OverlayMetric.FrameTime or OverlayMetric.Low1Percent => Sensors.Demand.FrameCapture,
        OverlayMetric.CpuTemperature or OverlayMetric.CpuClock or OverlayMetric.CpuPower
            or OverlayMetric.GpuTemperature or OverlayMetric.GpuClock or OverlayMetric.GpuPower => Sensors.Demand.HardwareSensors,
        _ => null,
    };
}

public static class OverlayDemand
{
    public static (bool Hardware, bool Frames) For(OverlayStyle style)
    {
        var demands = style.Items.Select(i => OverlayMetricCatalog.Demand(i.Metric)).ToList();
        return (demands.Contains(Demand.HardwareSensors), demands.Contains(Demand.FrameCapture));
    }
}

public static class OverlayRefresh
{
    // The frametime mini-chart redraws at up to 30 Hz; without it the configured rate applies.
    public static int EffectiveHz(OverlayStyle style) => style.ShowFrameGraph ? 30 : style.RefreshHz;
}
```

In `OverlayMetricCatalog` the method is called `Demand`, like the enum; so the body uses `Sensors.Demand.FrameCapture` (the name `Sensors` resolves to the namespace `Supermonitor.Core.Sensors`). In `OverlayDemand` (another class) `Demand.HardwareSensors` resolves to the enum.

`src/Supermonitor.Core/Overlay/IOverlayValues.cs`:

```csharp
namespace Supermonitor.Core.Overlay;

public interface IOverlayValues
{
    DateTime Now { get; }

    // Changes with every new frame; the overlay uses it to redraw the mini-chart.
    long FrameVersion { get; }

    // Value in the metric's base unit; NaN when unavailable.
    double Get(OverlayMetric metric);

    int CopyFrameTimes(Span<float> destination);
}

// Fixed, plausible values for the editor preview.
public sealed class DemoOverlayValues : IOverlayValues
{
    private const int FrameCount = 300;

    public DateTime Now => new(2026, 10, 10, 21, 7, 0);

    public long FrameVersion => 1;

    public double Get(OverlayMetric metric) => metric switch
    {
        OverlayMetric.Fps => 143,
        OverlayMetric.FrameTime => 6.9,
        OverlayMetric.Low1Percent => 98,
        OverlayMetric.CpuUsage => 23,
        OverlayMetric.CpuTemperature => 62,
        OverlayMetric.CpuClock => 4950,
        OverlayMetric.CpuPower => 71.5,
        OverlayMetric.GpuUsage => 97,
        OverlayMetric.GpuTemperature => 71,
        OverlayMetric.GpuClock => 2850,
        OverlayMetric.GpuPower => 245,
        OverlayMetric.GpuVram => 9.4 * 1024 * 1024 * 1024,
        OverlayMetric.Ram => 48,
        OverlayMetric.NetworkDown => 1_250_000,
        OverlayMetric.NetworkUp => 85_000,
        OverlayMetric.Ping => 12,
        _ => double.NaN,
    };

    public int CopyFrameTimes(Span<float> destination)
    {
        var count = Math.Min(destination.Length, FrameCount);
        for (var i = 0; i < count; i++)
        {
            var phase = (FrameCount - count + i) / 14d;
            destination[i] = 6.9f + (float)(Math.Sin(phase) * 1.2) + (i % 47 == 0 ? 9f : 0f);
        }

        return count;
    }
}
```

`src/Supermonitor.Core/Overlay/OverlayFrame.cs`:

```csharp
namespace Supermonitor.Core.Overlay;

public sealed record OverlayRow(string Label, string Value, Rgba LabelColor, Rgba ValueColor);

public sealed class OverlayFrame
{
    public OverlayFrame(OverlayRow[] rows, float[] graph, long graphVersion)
    {
        Rows = rows;
        Graph = graph;
        GraphVersion = graphVersion;
    }

    public OverlayRow[] Rows { get; }

    // Frametimes (ms), from oldest to newest; empty when the mini-chart is off.
    public float[] Graph { get; }

    public long GraphVersion { get; }

    // Decides whether redrawing is worthwhile: same text and no new frame in the chart → no.
    public bool SameContentAs(OverlayFrame? other) =>
        other is not null
        && Rows.AsSpan().SequenceEqual(other.Rows)
        && Graph.Length == other.Graph.Length
        && (Graph.Length == 0 || GraphVersion == other.GraphVersion);
}
```

`SequenceEqual` over `ReadOnlySpan<OverlayRow>` uses the record's `Equals` (value equality). If it does not compile, use `Enumerable.SequenceEqual(Rows, other.Rows)`.

`src/Supermonitor.Core/Overlay/OverlayFrameBuilder.cs`:

```csharp
using System.Globalization;
using Supermonitor.Core.Dashboard;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Settings;

namespace Supermonitor.Core.Overlay;

public static class OverlayFrameBuilder
{
    public static readonly Rgba WarningColor = new(0xFF, 0xB9, 0x00);
    public static readonly Rgba CriticalColor = new(0xE8, 0x11, 0x23);

    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    public static OverlayFrame Build(OverlayStyle style, IOverlayValues values, UnitOptions units, IReadOnlyDictionary<string, MetricThreshold> thresholds)
    {
        var defaultLabel = Rgba.Parse(style.LabelColor, new Rgba(0xB0, 0xB0, 0xB0));
        var defaultValue = Rgba.Parse(style.ValueColor, new Rgba(0xFF, 0xFF, 0xFF));
        var now = values.Now;

        var rows = new OverlayRow[style.Items.Count];
        for (var i = 0; i < rows.Length; i++)
        {
            var item = style.Items[i];
            var value = values.Get(item.Metric);
            var valueColor = Rgba.Parse(item.ValueColor, defaultValue);

            var threshold = item.Threshold;
            if (threshold is null && OverlayMetricCatalog.StoreMetricId(item.Metric) is { } id)
            {
                thresholds.TryGetValue(id, out threshold);
            }

            valueColor = ThresholdEvaluator.Evaluate((float)value, threshold) switch
            {
                ThresholdLevel.Critical => CriticalColor,
                ThresholdLevel.Warning => WarningColor,
                _ => valueColor,
            };

            rows[i] = new OverlayRow(
                OverlayMetricCatalog.Label(item.Metric),
                FormatValue(item.Metric, value, now, units),
                Rgba.Parse(item.LabelColor, defaultLabel),
                valueColor);
        }

        var graph = Array.Empty<float>();
        long version = 0;
        if (style.ShowFrameGraph)
        {
            // Two samples per pixel of chart width.
            var buffer = new float[Math.Max(1, style.GraphWidth / 2)];
            var copied = values.CopyFrameTimes(buffer);
            graph = copied == buffer.Length ? buffer : buffer.AsSpan(0, copied).ToArray();
            version = values.FrameVersion;
        }

        return new OverlayFrame(rows, graph, version);
    }

    public static string FormatValue(OverlayMetric metric, double value, DateTime now, UnitOptions units)
    {
        if (metric == OverlayMetric.Clock)
        {
            return now.ToString("HH:mm", Culture);
        }

        if (!double.IsFinite(value))
        {
            return "N/A";
        }

        // The label already says "FPS"; the value shows only the number.
        if (metric is OverlayMetric.Fps or OverlayMetric.Low1Percent)
        {
            return value.ToString("0", Culture);
        }

        return UnitFormatter.Format(value, OverlayMetricCatalog.Unit(metric), units);
    }
}
```

`src/Supermonitor.Core/Overlay/OverlayPlacement.cs`:

```csharp
namespace Supermonitor.Core.Overlay;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;
}

public static class OverlayPlacement
{
    // The offset is an inward margin from the anchor's edge; at the center it shifts right/down.
    // The result always fits in the monitor (or sticks to the top-left corner if the overlay is larger).
    public static (int X, int Y) Compute(OverlayAnchor anchor, int offsetX, int offsetY, PixelRect monitor, int width, int height)
    {
        var column = (int)anchor % 3;
        var row = (int)anchor / 3;

        var x = column switch
        {
            0 => monitor.X + offsetX,
            1 => monitor.X + (monitor.Width - width) / 2 + offsetX,
            _ => monitor.Right - width - offsetX,
        };
        var y = row switch
        {
            0 => monitor.Y + offsetY,
            1 => monitor.Y + (monitor.Height - height) / 2 + offsetY,
            _ => monitor.Bottom - height - offsetY,
        };

        var maxX = Math.Max(monitor.X, monitor.Right - width);
        var maxY = Math.Max(monitor.Y, monitor.Bottom - height);
        return (Math.Clamp(x, monitor.X, maxX), Math.Clamp(y, monitor.Y, maxY));
    }
}
```

- [ ] **Step 4: Run and see it pass**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: all pass.

- [ ] **Step 5: Commit**

```powershell
git add src/Supermonitor.Core tests/Supermonitor.Tests
git commit -m "feat(core): overlay content builder, metric catalog, placement and refresh rate"
```

---

### Task 4: FrameCaptureService (target, lifecycle, retry) with a fake source

**Files:**
- Create: `src/Supermonitor.Core/Frames/FrameCaptureService.cs`
- Modify: `src/Supermonitor.Core/Sensors/DemandTracker.cs`
- Test: `tests/Supermonitor.Tests/Frames/FrameCaptureServiceTests.cs`, `tests/Supermonitor.Tests/Sensors/DemandTrackerTests.cs`

**Interfaces:**
- Consumes: `FrameTimeAggregator`, `FrameStats`, `DemandTracker`, `Demand.FrameCapture`, `Log`.
- Produces:
  - `DemandTracker.Changed` — `event Action<Demand, bool>?`, fired on the thread of whoever acquires/releases when the count of a `Demand` goes from 0 to 1 (`true`) or from 1 to 0 (`false`). Subscribers must be fast.
  - `interface IPresentSource : IDisposable { event Action<int, double>? Presented; void Start(); }` — `Presented(processId, timestampMs)`, monotonic timestamp in milliseconds; `Start()` throws on failure.
  - `interface IForegroundProbe { int GetForegroundProcessId(); }` — 0 when there is no window.
  - `enum FrameCaptureState { Stopped, Running, Failed }`
  - `sealed class FrameCaptureService : IDisposable` — constructor `(DemandTracker demand, Func<IPresentSource> sourceFactory, IForegroundProbe probe, TimeProvider time, int ownProcessId, bool runLoop = true)`; `FrameCaptureState State`, `string? FailureReason`, `int TargetProcessId`, `FrameStats Current`, `long FrameVersion`, `int CopyFrameTimes(Span<float>)`, `static readonly TimeSpan IdleAfter` (2 s), `RetryInterval` (30 s), `PollInterval` (250 ms); `internal void Poll()`.

- [ ] **Step 1: Write the `DemandTracker.Changed` tests**

Add inside `DemandTrackerTests`:

```csharp
    [Fact]
    public void Changed_fires_when_the_first_lease_is_taken_and_the_last_is_released()
    {
        var tracker = new DemandTracker();
        var events = new List<(Demand, bool)>();
        tracker.Changed += (demand, active) => events.Add((demand, active));

        var first = tracker.Acquire(Demand.FrameCapture);
        var second = tracker.Acquire(Demand.FrameCapture);
        first.Dispose();
        Assert.Equal(new[] { (Demand.FrameCapture, true) }, events);

        second.Dispose();
        second.Dispose();

        Assert.Equal(new[] { (Demand.FrameCapture, true), (Demand.FrameCapture, false) }, events);
    }

    [Fact]
    public void Changed_reports_each_demand_separately()
    {
        var tracker = new DemandTracker();
        var events = new List<(Demand, bool)>();
        tracker.Changed += (demand, active) => events.Add((demand, active));

        using var processes = tracker.Acquire(Demand.Processes);
        using var hardware = tracker.Acquire(Demand.HardwareSensors);

        Assert.Equal(new[] { (Demand.Processes, true), (Demand.HardwareSensors, true) }, events);
    }
```

- [ ] **Step 2: Write the service tests**

`tests/Supermonitor.Tests/Frames/FrameCaptureServiceTests.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using Supermonitor.Core.Frames;
using Supermonitor.Core.Sensors;

namespace Supermonitor.Tests.Frames;

public class FrameCaptureServiceTests
{
    private const int GamePid = 100;
    private const int OtherPid = 200;
    private const int OwnPid = 1;

    private sealed class FakeSource : IPresentSource
    {
        public event Action<int, double>? Presented;

        public Exception? FailOnStart { get; init; }

        public bool Started { get; private set; }

        public bool Disposed { get; private set; }

        public void Start()
        {
            if (FailOnStart is not null)
            {
                throw FailOnStart;
            }

            Started = true;
        }

        public void Raise(int pid, double timestampMs) => Presented?.Invoke(pid, timestampMs);

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeProbe : IForegroundProbe
    {
        public int Pid { get; set; } = GamePid;

        public int GetForegroundProcessId() => Pid;
    }

    private sealed class Rig : IDisposable
    {
        private readonly Queue<Func<FakeSource>> _plans = new();

        public Rig(bool runLoop = false)
        {
            Service = new FrameCaptureService(Demand, () =>
            {
                var source = _plans.Count > 0 ? _plans.Dequeue()() : new FakeSource();
                Sources.Add(source);
                return source;
            }, Probe, Time, OwnPid, runLoop);
        }

        public DemandTracker Demand { get; } = new();

        public FakeTimeProvider Time { get; } = new();

        public FakeProbe Probe { get; } = new();

        public List<FakeSource> Sources { get; } = [];

        public FrameCaptureService Service { get; }

        public void Plan(Func<FakeSource> source) => _plans.Enqueue(source);

        // Delivers `count` 10 ms frames for the process (count + 1 presents), starting at `start`; returns the last instant.
        public double Feed(int pid, double start, int count)
        {
            var timestamp = start;
            for (var i = 0; i <= count; i++)
            {
                Sources[^1].Raise(pid, timestamp);
                timestamp += 10;
            }

            return timestamp - 10;
        }

        public void Dispose() => Service.Dispose();
    }

    [Fact]
    public void Nothing_starts_without_demand()
    {
        using var rig = new Rig();

        rig.Service.Poll();

        Assert.Empty(rig.Sources);
        Assert.Equal(FrameCaptureState.Stopped, rig.Service.State);
        Assert.False(rig.Service.Current.HasData);
    }

    [Fact]
    public void Demand_starts_the_source_and_targets_the_foreground_process()
    {
        using var rig = new Rig();
        using var lease = rig.Demand.Acquire(Demand.FrameCapture);

        rig.Service.Poll();

        Assert.True(Assert.Single(rig.Sources).Started);
        Assert.Equal(FrameCaptureState.Running, rig.Service.State);
        Assert.Equal(GamePid, rig.Service.TargetProcessId);
    }

    [Fact]
    public void Only_presents_of_the_target_are_aggregated()
    {
        using var rig = new Rig();
        using var lease = rig.Demand.Acquire(Demand.FrameCapture);
        rig.Service.Poll();

        rig.Feed(GamePid, 1000, 10);
        rig.Feed(OtherPid, 1000, 50);

        var stats = rig.Service.Current;
        Assert.True(stats.HasData);
        Assert.Equal(10, stats.FrameCount);
        Assert.Equal(100, stats.Fps, 0.5);
    }

    [Fact]
    public void Changing_the_foreground_process_restarts_the_aggregation()
    {
        using var rig = new Rig();
        using var lease = rig.Demand.Acquire(Demand.FrameCapture);
        rig.Service.Poll();
        rig.Feed(GamePid, 1000, 10);

        rig.Probe.Pid = OtherPid;
        rig.Service.Poll();

        Assert.Equal(OtherPid, rig.Service.TargetProcessId);
        Assert.False(rig.Service.Current.HasData);

        rig.Feed(GamePid, 2000, 5);
        Assert.False(rig.Service.Current.HasData);

        rig.Feed(OtherPid, 3000, 5);
        Assert.Equal(5, rig.Service.Current.FrameCount);
    }

    [Fact]
    public void The_own_process_in_the_foreground_means_no_target()
    {
        using var rig = new Rig();
        using var lease = rig.Demand.Acquire(Demand.FrameCapture);
        rig.Probe.Pid = OwnPid;

        rig.Service.Poll();
        rig.Feed(OwnPid, 1000, 10);

        Assert.Equal(0, rig.Service.TargetProcessId);
        Assert.False(rig.Service.Current.HasData);
    }

    [Fact]
    public void No_foreground_window_means_no_target()
    {
        using var rig = new Rig();
        using var lease = rig.Demand.Acquire(Demand.FrameCapture);
        rig.Probe.Pid = 0;

        rig.Service.Poll();
        rig.Feed(0, 1000, 10);

        Assert.Equal(0, rig.Service.TargetProcessId);
        Assert.False(rig.Service.Current.HasData);
    }

    [Fact]
    public void Stats_go_idle_after_two_seconds_without_presents_and_return_with_the_next_one()
    {
        using var rig = new Rig();
        using var lease = rig.Demand.Acquire(Demand.FrameCapture);
        rig.Service.Poll();
        var last = rig.Feed(GamePid, 1000, 10);

        rig.Time.Advance(FrameCaptureService.IdleAfter - TimeSpan.FromMilliseconds(1));
        Assert.True(rig.Service.Current.HasData);

        rig.Time.Advance(TimeSpan.FromMilliseconds(2));
        Assert.False(rig.Service.Current.HasData);

        rig.Sources[^1].Raise(GamePid, last + 10);
        Assert.True(rig.Service.Current.HasData);
    }

    [Fact]
    public void Releasing_the_demand_stops_the_source_and_clears_the_stats()
    {
        using var rig = new Rig();
        var lease = rig.Demand.Acquire(Demand.FrameCapture);
        rig.Service.Poll();
        rig.Feed(GamePid, 1000, 10);

        lease.Dispose();
        rig.Service.Poll();

        Assert.True(rig.Sources[0].Disposed);
        Assert.Equal(FrameCaptureState.Stopped, rig.Service.State);
        Assert.Equal(0, rig.Service.TargetProcessId);
        Assert.False(rig.Service.Current.HasData);
    }

    [Fact]
    public void A_new_demand_after_a_stop_creates_a_new_source()
    {
        using var rig = new Rig();
        var lease = rig.Demand.Acquire(Demand.FrameCapture);
        rig.Service.Poll();
        lease.Dispose();
        rig.Service.Poll();

        using var again = rig.Demand.Acquire(Demand.FrameCapture);
        rig.Service.Poll();

        Assert.Equal(2, rig.Sources.Count);
        Assert.Equal(FrameCaptureState.Running, rig.Service.State);
    }

    [Fact]
    public void A_failed_start_is_reported_and_retried_every_thirty_seconds()
    {
        using var rig = new Rig();
        rig.Plan(() => new FakeSource { FailOnStart = new UnauthorizedAccessException("needs administrator") });
        using var lease = rig.Demand.Acquire(Demand.FrameCapture);

        rig.Service.Poll();

        Assert.Equal(FrameCaptureState.Failed, rig.Service.State);
        Assert.Equal("needs administrator", rig.Service.FailureReason);
        Assert.True(rig.Sources[0].Disposed);
        Assert.False(rig.Service.Current.HasData);

        rig.Time.Advance(FrameCaptureService.RetryInterval - TimeSpan.FromSeconds(1));
        rig.Service.Poll();
        Assert.Single(rig.Sources);

        rig.Time.Advance(TimeSpan.FromSeconds(2));
        rig.Service.Poll();
        Assert.Equal(2, rig.Sources.Count);
        Assert.Equal(FrameCaptureState.Running, rig.Service.State);
        Assert.Null(rig.Service.FailureReason);
    }

    [Fact]
    public void A_factory_that_throws_is_also_a_failed_start()
    {
        var demand = new DemandTracker();
        using var service = new FrameCaptureService(demand, () => throw new InvalidOperationException("no etw"), new FakeProbe(), new FakeTimeProvider(), OwnPid, runLoop: false);
        using var lease = demand.Acquire(Demand.FrameCapture);

        service.Poll();

        Assert.Equal(FrameCaptureState.Failed, service.State);
        Assert.Equal("no etw", service.FailureReason);
    }

    [Fact]
    public void Dropping_the_demand_after_a_failure_returns_to_stopped()
    {
        using var rig = new Rig();
        rig.Plan(() => new FakeSource { FailOnStart = new InvalidOperationException("boom") });
        var lease = rig.Demand.Acquire(Demand.FrameCapture);
        rig.Service.Poll();

        lease.Dispose();
        rig.Service.Poll();

        Assert.Equal(FrameCaptureState.Stopped, rig.Service.State);
        Assert.Null(rig.Service.FailureReason);
    }

    [Fact]
    public void Disposing_the_service_disposes_the_source()
    {
        var rig = new Rig();
        using var lease = rig.Demand.Acquire(Demand.FrameCapture);
        rig.Service.Poll();

        rig.Dispose();

        Assert.True(rig.Sources[0].Disposed);
    }

    [Fact]
    public void The_loop_starts_the_source_when_demand_appears_and_stops_it_when_it_ends()
    {
        using var rig = new Rig(runLoop: true);

        var lease = rig.Demand.Acquire(Demand.FrameCapture);
        Assert.True(SpinWait.SpinUntil(() => rig.Service.State == FrameCaptureState.Running, TimeSpan.FromSeconds(5)));

        lease.Dispose();
        SpinWait.SpinUntil(() =>
        {
            rig.Time.Advance(FrameCaptureService.PollInterval);
            return rig.Service.State == FrameCaptureState.Stopped;
        }, TimeSpan.FromSeconds(5));

        Assert.Equal(FrameCaptureState.Stopped, rig.Service.State);
        Assert.True(rig.Sources[0].Disposed);
    }
}
```

- [ ] **Step 3: Run and see it fail**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: compile error (`Changed`, `FrameCaptureService` do not exist).

- [ ] **Step 4: Implement `DemandTracker.Changed`**

In `src/Supermonitor.Core/Sensors/DemandTracker.cs` replace the `DemandTracker` class with:

```csharp
public sealed class DemandTracker
{
    private readonly int[] _counts = new int[Enum.GetValues<Demand>().Length];

    // Fired on the thread of whoever acquires/releases, when the count of a Demand goes from 0 to 1 or from 1 to 0.
    public event Action<Demand, bool>? Changed;

    public IDisposable Acquire(Demand demand)
    {
        if (Interlocked.Increment(ref _counts[(int)demand]) == 1)
        {
            Changed?.Invoke(demand, true);
        }

        return new Lease(this, demand);
    }

    public bool IsActive(Demand demand) => Volatile.Read(ref _counts[(int)demand]) > 0;

    private sealed class Lease(DemandTracker owner, Demand demand) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0
                && Interlocked.Decrement(ref owner._counts[(int)demand]) == 0)
            {
                owner.Changed?.Invoke(demand, false);
            }
        }
    }
}
```

- [ ] **Step 5: Implement the service**

`src/Supermonitor.Core/Frames/FrameCaptureService.cs`:

```csharp
using Supermonitor.Core.Diagnostics;
using Supermonitor.Core.Sensors;

namespace Supermonitor.Core.Frames;

public interface IPresentSource : IDisposable
{
    // processId of the app that presented and the instant in ms (monotonic).
    event Action<int, double>? Presented;

    // Throws if capture cannot start.
    void Start();
}

public interface IForegroundProbe
{
    // 0 when there is no foreground window.
    int GetForegroundProcessId();
}

public enum FrameCaptureState
{
    Stopped,
    Running,
    Failed,
}

// Turns the present source on only while a Demand.FrameCapture lease exists, follows the foreground process
// and feeds the FrameTimeAggregator. Start failures are reported and retried every 30 s.
public sealed class FrameCaptureService : IDisposable
{
    public static readonly TimeSpan IdleAfter = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly DemandTracker _demand;
    private readonly Func<IPresentSource> _sourceFactory;
    private readonly IForegroundProbe _probe;
    private readonly TimeProvider _time;
    private readonly int _ownProcessId;
    private readonly bool _runLoop;
    private readonly FrameTimeAggregator _aggregator = new();
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _cts = new();
    private IPresentSource? _source;
    private int _state;
    private int _target;
    private long _lastPresent;
    private long _failedAt;
    private int _loopActive;
    private Task? _loop;

    public FrameCaptureService(DemandTracker demand, Func<IPresentSource> sourceFactory, IForegroundProbe probe, TimeProvider time, int ownProcessId, bool runLoop = true)
    {
        _demand = demand;
        _sourceFactory = sourceFactory;
        _probe = probe;
        _time = time;
        _ownProcessId = ownProcessId;
        _runLoop = runLoop;
        _demand.Changed += OnDemandChanged;
    }

    public FrameCaptureState State => (FrameCaptureState)Volatile.Read(ref _state);

    public string? FailureReason { get; private set; }

    public int TargetProcessId => Volatile.Read(ref _target);

    public long FrameVersion => _aggregator.Version;

    // With no presents for longer than IdleAfter (game stopped, alt-tab) it goes back to "no data".
    public FrameStats Current
    {
        get
        {
            var last = Volatile.Read(ref _lastPresent);
            return last == 0 || _time.GetElapsedTime(last) > IdleAfter ? FrameStats.None : _aggregator.GetStats();
        }
    }

    public int CopyFrameTimes(Span<float> destination) => _aggregator.CopyFrameTimes(destination);

    public void Dispose()
    {
        _demand.Changed -= OnDemandChanged;
        _cts.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        lock (_gate)
        {
            StopSource();
        }

        _cts.Dispose();
    }

    internal void Poll()
    {
        lock (_gate)
        {
            if (!_demand.IsActive(Demand.FrameCapture))
            {
                StopSource();
                SetTarget(0);
                FailureReason = null;
                Volatile.Write(ref _state, (int)FrameCaptureState.Stopped);
                return;
            }

            if (_source is null)
            {
                if (State == FrameCaptureState.Failed && _time.GetElapsedTime(_failedAt) < RetryInterval)
                {
                    return;
                }

                TryStart();
                if (_source is null)
                {
                    return;
                }
            }

            var pid = _probe.GetForegroundProcessId();
            SetTarget(pid == _ownProcessId ? 0 : pid);
        }
    }

    private void TryStart()
    {
        IPresentSource? source = null;
        try
        {
            source = _sourceFactory();
            source.Presented += OnPresented;
            source.Start();
            _source = source;
            FailureReason = null;
            Volatile.Write(ref _state, (int)FrameCaptureState.Running);
            Log.Info("Frame capture started");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            source?.Dispose();
            _failedAt = _time.GetTimestamp();
            FailureReason = ex.Message;
            Volatile.Write(ref _state, (int)FrameCaptureState.Failed);
            Log.Warn("Frame capture could not start; will retry", ex);
        }
    }

    private void StopSource()
    {
        if (_source is { } source)
        {
            _source = null;
            source.Dispose();
            Log.Info("Frame capture stopped");
        }

        _aggregator.Reset();
        Volatile.Write(ref _lastPresent, 0);
    }

    private void SetTarget(int pid)
    {
        if (pid == Volatile.Read(ref _target))
        {
            return;
        }

        _aggregator.Reset();
        Volatile.Write(ref _lastPresent, 0);
        Volatile.Write(ref _target, pid);
    }

    // ETW thread.
    private void OnPresented(int pid, double timestampMs)
    {
        if (pid == 0 || pid != Volatile.Read(ref _target))
        {
            return;
        }

        _aggregator.AddPresent(timestampMs);
        Volatile.Write(ref _lastPresent, _time.GetTimestamp());
    }

    private void OnDemandChanged(Demand demand, bool active)
    {
        if (demand == Demand.FrameCapture && active && _runLoop)
        {
            EnsureLoop();
        }
    }

    private void EnsureLoop()
    {
        if (Interlocked.CompareExchange(ref _loopActive, 1, 0) == 0)
        {
            _loop = Task.Run(RunAsync);
        }
    }

    private async Task RunAsync()
    {
        var completed = false;
        try
        {
            using var timer = new PeriodicTimer(PollInterval, _time);
            do
            {
                Poll();
            }
            while (_demand.IsActive(Demand.FrameCapture) && await timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false));

            Poll();
            completed = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error("Frame capture loop failed", ex);
        }
        finally
        {
            Volatile.Write(ref _loopActive, 0);
            // Demand may have returned between the last check and the end of the loop. After an exception it does not restart, to avoid spinning in vain.
            if (completed && !_cts.IsCancellationRequested && _demand.IsActive(Demand.FrameCapture))
            {
                EnsureLoop();
            }
        }
    }
}
```

- [ ] **Step 6: Run and see it pass**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: all pass. If `The_loop_starts_the_source_when_demand_appears_and_stops_it_when_it_ends` becomes flaky, increase the timeout to 10 s; the loop uses `PeriodicTimer` over `FakeTimeProvider`, so advancing time in the test is what wakes it.

- [ ] **Step 7: Commit**

```powershell
git add src/Supermonitor.Core tests/Supermonitor.Tests
git commit -m "feat(core): frame capture service with foreground target, idle detection and retry"
```

---

### Task 5: Real ETW source, foreground probe and AppHost

**Files:**
- Create: `src/Supermonitor.Core/Frames/EtwPresentSource.cs`, `src/Supermonitor.Core/Frames/ForegroundProbe.cs`
- Modify: `src/Supermonitor.Core/Supermonitor.Core.csproj`, `src/Supermonitor.Core/Interop/User32.cs`, `src/Supermonitor.Core/AppHost.cs`
- Test: `tests/Supermonitor.Tests/Hardware/FrameCaptureSmokeTests.cs`

**Interfaces:**
- Consumes: `IPresentSource`, `IForegroundProbe`, `FrameCaptureService`, `Log`.
- Produces:
  - `sealed class EtwPresentSource : IPresentSource` with `const string SessionName = "Supermonitor-FPS"`.
  - `sealed class ForegroundProbe : IForegroundProbe`.
  - `AppHost.Frames` (`FrameCaptureService`).

- [ ] **Step 1: Write the smoke tests (Hardware trait)**

`tests/Supermonitor.Tests/Hardware/FrameCaptureSmokeTests.cs`:

```csharp
using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Supermonitor.Core.Frames;

namespace Supermonitor.Tests.Hardware;

[Trait("Category", "Hardware")]
public class FrameCaptureSmokeTests
{
    private static bool IsElevated => TraceEventSession.IsElevated() == true;

    [Fact]
    public void Foreground_probe_returns_an_existing_process()
    {
        var pid = new ForegroundProbe().GetForegroundProcessId();

        Assert.True(pid > 0, "No foreground window; run the test with a desktop session.");
        using var process = Process.GetProcessById(pid);
        Assert.False(process.HasExited);
    }

    [Fact]
    public void Etw_source_starts_and_removes_its_session_on_dispose()
    {
        if (!IsElevated)
        {
            Assert.Skip("ETW needs an administrator process; run the test runner elevated.");
        }

        var received = 0;
        using (var source = new EtwPresentSource())
        {
            source.Presented += (_, _) => Interlocked.Increment(ref received);
            source.Start();

            Assert.Contains(EtwPresentSource.SessionName, TraceEventSession.GetActiveSessionNames());
            Thread.Sleep(3000);
        }

        Assert.DoesNotContain(EtwPresentSource.SessionName, TraceEventSession.GetActiveSessionNames());
        // The number depends on there being animating windows; we only log it.
        Console.WriteLine($"present events received in 3 s: {received}");
    }

    [Fact]
    public void Etw_source_replaces_an_orphan_session_with_the_same_name()
    {
        if (!IsElevated)
        {
            Assert.Skip("ETW needs an administrator process; run the test runner elevated.");
        }

        // Simulates a previous crash: the session stays active after the process "dies".
        var orphan = new TraceEventSession(EtwPresentSource.SessionName) { StopOnDispose = false };
        orphan.EnableProvider(new Guid("802ec45a-1e99-4b83-9920-87c98277ba9d"), TraceEventLevel.Informational, 0x8000000);
        orphan.Dispose();
        Assert.Contains(EtwPresentSource.SessionName, TraceEventSession.GetActiveSessionNames());

        using (var source = new EtwPresentSource())
        {
            source.Start();

            Assert.Single(TraceEventSession.GetActiveSessionNames(), name => name == EtwPresentSource.SessionName);
        }

        Assert.DoesNotContain(EtwPresentSource.SessionName, TraceEventSession.GetActiveSessionNames());
    }

    [Fact]
    public void Etw_source_without_administrator_rights_fails_with_a_clear_error()
    {
        if (IsElevated)
        {
            Assert.Skip("Only meaningful in a non-administrator process.");
        }

        using var source = new EtwPresentSource();

        var error = Assert.Throws<UnauthorizedAccessException>(source.Start);
        Assert.Contains("administrator", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Add the package and verify the test does not compile yet**

In `src/Supermonitor.Core/Supermonitor.Core.csproj`, inside the packages `ItemGroup`:

```xml
    <PackageReference Include="Microsoft.Diagnostics.Tracing.TraceEvent" Version="3.2.8" />
```

Run: `dotnet build tests/Supermonitor.Tests 2>&1 | Select-String "error"`
Expected: errors because `EtwPresentSource` and `ForegroundProbe` do not exist.

- [ ] **Step 3: P/Invoke**

In `src/Supermonitor.Core/Interop/User32.cs`, inside the class, add:

```csharp
    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);
```

- [ ] **Step 4: Implement the probe and the ETW source**

`src/Supermonitor.Core/Frames/ForegroundProbe.cs`:

```csharp
using Supermonitor.Core.Interop;

namespace Supermonitor.Core.Frames;

public sealed class ForegroundProbe : IForegroundProbe
{
    public int GetForegroundProcessId()
    {
        var window = User32.GetForegroundWindow();
        if (window == 0)
        {
            return 0;
        }

        User32.GetWindowThreadProcessId(window, out var processId);
        return (int)processId;
    }
}
```

`src/Supermonitor.Core/Frames/EtwPresentSource.cs`:

```csharp
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Supermonitor.Core.Diagnostics;

namespace Supermonitor.Core.Frames;

// One event per present (DxgKrnl, id 184) from any process; the header ProcessID is that of the presenting app.
public sealed class EtwPresentSource : IPresentSource
{
    public const string SessionName = "Supermonitor-FPS";

    private static readonly Guid DxgKrnl = new("802ec45a-1e99-4b83-9920-87c98277ba9d");
    private const int PresentEventId = 184;
    private const ulong PresentKeyword = 0x8000000;

    private TraceEventSession? _session;
    private Task? _processing;

    public event Action<int, double>? Presented;

    public void Start()
    {
        if (TraceEventSession.IsElevated() != true)
        {
            throw new UnauthorizedAccessException("FPS capture needs administrator rights.");
        }

        // The default constructor (Create) stops and recreates an orphan session with the same name left by a previous crash.
        var session = new TraceEventSession(SessionName);
        try
        {
            session.EnableProvider(DxgKrnl, TraceEventLevel.Informational, PresentKeyword);
            session.Source.AllEvents += OnEvent;
        }
        catch
        {
            session.Dispose();
            throw;
        }

        _session = session;
        _processing = Task.Run(() =>
        {
            try
            {
                session.Source.Process();
            }
            catch (Exception ex)
            {
                Log.Warn("ETW processing stopped", ex);
            }
        });
    }

    public void Dispose()
    {
        var session = Interlocked.Exchange(ref _session, null);
        if (session is null)
        {
            return;
        }

        // Stopping the session makes Process() return.
        session.Dispose();
        try
        {
            _processing?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
    }

    private void OnEvent(TraceEvent data)
    {
        if ((int)data.ID == PresentEventId && data.ProviderGuid == DxgKrnl)
        {
            Presented?.Invoke(data.ProcessID, data.TimeStampRelativeMSec);
        }
    }
}
```

- [ ] **Step 5: Wire into `AppHost`**

In `src/Supermonitor.Core/AppHost.cs`: add `using Supermonitor.Core.Frames;`, the property `public FrameCaptureService Frames { get; }`, the parameter `FrameCaptureService frames` in the private constructor (`Frames = frames;`) and, in `Create()`, after creating `demand`:

```csharp
        var frames = new FrameCaptureService(demand, () => new EtwPresentSource(), new ForegroundProbe(), TimeProvider.System, Environment.ProcessId);
```

passing `frames` to the `new AppHost(...)` constructor. In `DisposeAsync()`, before `await Hub.DisposeAsync()`:

```csharp
        Frames.Dispose();
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: all pass (the hardware ones do not run here).

Run (non-elevated shell): `dotnet test --project tests/Supermonitor.Tests --filter-class "Supermonitor.Tests.Hardware.FrameCaptureSmokeTests"`
Expected: `Foreground_probe...` and `Etw_source_without_administrator_rights...` pass; the two tests that require administrator show as skipped.

Run (elevated terminal — the user opens a PowerShell as administrator and runs the same command): the two ETW tests pass and `present events received in 3 s: N` is printed. N > 0 is expected with any animating window; N = 0 does not fail the test, but investigate before moving on (Task 10 depends on it).

- [ ] **Step 7: Commit**

```powershell
git add src/Supermonitor.Core tests/Supermonitor.Tests
git commit -m "feat(core): ETW present source, foreground probe and AppHost wiring"
```

---

### Task 6: Supermonitor.Overlay project and OverlayRenderer (Direct2D → DIB)

**Files:**
- Create: `src/Supermonitor.Overlay/Supermonitor.Overlay.csproj`, `src/Supermonitor.Overlay/Interop/Gdi32.cs`, `src/Supermonitor.Overlay/OverlayRenderer.cs`
- Modify: `Supermonitor.slnx`, `tests/Supermonitor.Tests/Supermonitor.Tests.csproj`
- Test: `tests/Supermonitor.Tests/Overlay/OverlayRendererTests.cs`

**Interfaces:**
- Consumes: `OverlayFrame`, `OverlayRow`, `OverlayStyle`, `OverlayLayout`, `Rgba` (Tasks 3 and 2).
- Produces (namespace `Supermonitor.Overlay`):
  - `readonly record struct OverlayBitmap(nint Hdc, nint Bits, int Width, int Height)` with `ReadOnlySpan<byte> Pixels` (premultiplied BGRA, top-down, `Width * Height * 4` bytes). Valid until the next `Render` call of the same renderer.
  - `sealed class OverlayRenderer : IDisposable` with `OverlayBitmap Render(OverlayFrame frame, OverlayStyle style, float scale)`. **Thread-affine**: always use it from the same thread (the Direct2D factory is single-threaded). `scale` = monitor DPI / 96; font, padding, radius, chart and gaps are multiplied by it.

- [ ] **Step 1: Create the project and reference it**

`src/Supermonitor.Overlay/Supermonitor.Overlay.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Supermonitor.Overlay</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Vortice.Direct2D1" Version="3.8.3" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Supermonitor.Core\Supermonitor.Core.csproj" />
  </ItemGroup>
</Project>
```

In `Supermonitor.slnx`, inside `<Folder Name="/src/">`, after the Core project:

```xml
    <Project Path="src/Supermonitor.Overlay/Supermonitor.Overlay.csproj" />
```

In `tests/Supermonitor.Tests/Supermonitor.Tests.csproj`, next to the Core `ProjectReference`:

```xml
    <ProjectReference Include="..\..\src\Supermonitor.Overlay\Supermonitor.Overlay.csproj" />
```

Run: `dotnet build tests/Supermonitor.Tests 2>&1 | Select-String "error|Erro"`
Expected: no errors (`0 Error(s)`; restores the Vortice packages).

- [ ] **Step 2: Write the renderer tests**

`tests/Supermonitor.Tests/Overlay/OverlayRendererTests.cs`:

```csharp
using Supermonitor.Core.Overlay;
using Supermonitor.Overlay;

namespace Supermonitor.Tests.Overlay;

public sealed class OverlayRendererTests : IDisposable
{
    private readonly OverlayRenderer _renderer = new();

    public void Dispose() => _renderer.Dispose();

    private static OverlayFrame Frame(int rows, float[]? graph = null)
    {
        var items = Enumerable.Range(0, rows)
            .Select(i => new OverlayRow($"Label {i}", $"{100 + i} ms", new Rgba(0xB0, 0xB0, 0xB0), new Rgba(0xFF, 0xFF, 0xFF)))
            .ToArray();
        return new OverlayFrame(items, graph ?? [], graph is null ? 0 : 1);
    }

    private static OverlayStyle Style(Action<OverlayStyle>? configure = null)
    {
        var style = new OverlayStyle { Padding = 10, CornerRadius = 0, BackgroundOpacity = 1, BackgroundColor = "#FF0000" };
        configure?.Invoke(style);
        return style;
    }

    private static (byte B, byte G, byte R, byte A) Pixel(OverlayBitmap bitmap, int x, int y)
    {
        var pixels = bitmap.Pixels;
        var index = (y * bitmap.Width + x) * 4;
        return (pixels[index], pixels[index + 1], pixels[index + 2], pixels[index + 3]);
    }

    [Fact]
    public void More_rows_make_a_vertical_overlay_taller_but_not_wider()
    {
        var one = _renderer.Render(Frame(1), Style(), 1);
        var oneWidth = one.Width;
        var oneHeight = one.Height;

        var three = _renderer.Render(Frame(3), Style(), 1);

        Assert.True(three.Height > oneHeight * 2, $"{three.Height} vs {oneHeight}");
        Assert.InRange(three.Width, oneWidth - 2, oneWidth + 2);
    }

    [Fact]
    public void Horizontal_layout_is_wider_and_shorter_than_vertical()
    {
        var vertical = _renderer.Render(Frame(3), Style(s => s.Layout = OverlayLayout.Vertical), 1);
        var (verticalWidth, verticalHeight) = (vertical.Width, vertical.Height);

        var horizontal = _renderer.Render(Frame(3), Style(s => s.Layout = OverlayLayout.Horizontal), 1);

        Assert.True(horizontal.Width > verticalWidth);
        Assert.True(horizontal.Height < verticalHeight);
    }

    [Fact]
    public void Compact_layout_is_narrower_than_horizontal()
    {
        var horizontal = _renderer.Render(Frame(3), Style(s => s.Layout = OverlayLayout.Horizontal), 1);
        var horizontalWidth = horizontal.Width;

        var compact = _renderer.Render(Frame(3), Style(s => s.Layout = OverlayLayout.Compact), 1);

        Assert.True(compact.Width < horizontalWidth);
    }

    [Fact]
    public void Padding_grows_the_bitmap_on_every_side()
    {
        var small = _renderer.Render(Frame(2), Style(s => s.Padding = 0), 1);
        var (smallWidth, smallHeight) = (small.Width, small.Height);

        var large = _renderer.Render(Frame(2), Style(s => s.Padding = 20), 1);

        Assert.Equal(smallWidth + 40, large.Width);
        Assert.Equal(smallHeight + 40, large.Height);
    }

    [Fact]
    public void Scale_multiplies_the_size()
    {
        var normal = _renderer.Render(Frame(2), Style(), 1);
        var (normalWidth, normalHeight) = (normal.Width, normal.Height);

        var doubled = _renderer.Render(Frame(2), Style(), 2);

        // DirectWrite metrics round per glyph; a 10% tolerance covers that.
        Assert.InRange(doubled.Width, normalWidth * 2 * 0.9, normalWidth * 2 * 1.1);
        Assert.InRange(doubled.Height, normalHeight * 2 * 0.9, normalHeight * 2 * 1.1);
    }

    [Fact]
    public void Opaque_background_fills_the_padding_and_rounded_corners_stay_transparent()
    {
        var bitmap = _renderer.Render(Frame(1), Style(s => s.CornerRadius = 12), 1);

        var padding = Pixel(bitmap, bitmap.Width - 3, bitmap.Height / 2);
        var corner = Pixel(bitmap, 0, 0);

        Assert.Equal(((byte)0, (byte)0, (byte)255, (byte)255), padding);
        Assert.Equal(0, corner.A);
    }

    [Fact]
    public void Background_opacity_is_premultiplied()
    {
        var bitmap = _renderer.Render(Frame(1), Style(s => s.BackgroundOpacity = 0.5f), 1);

        var padding = Pixel(bitmap, bitmap.Width - 3, bitmap.Height / 2);

        Assert.InRange(padding.A, 126, 130);
        Assert.InRange(padding.R, 126, 130);
        Assert.Equal(0, padding.G);
        Assert.Equal(0, padding.B);
    }

    [Fact]
    public void Text_is_drawn_even_without_a_background()
    {
        var bitmap = _renderer.Render(Frame(2), Style(s => s.BackgroundOpacity = 0), 1);

        var drawn = 0;
        var pixels = bitmap.Pixels;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                drawn++;
            }
        }

        Assert.True(drawn > 30, $"only {drawn} pixels drawn");
        Assert.Equal(0, Pixel(bitmap, 0, 0).A);
    }

    [Fact]
    public void The_graph_adds_its_height_and_is_drawn()
    {
        var graph = Enumerable.Range(0, 80).Select(i => 6f + (i % 10)).ToArray();
        var without = _renderer.Render(Frame(2), Style(s => s.ShowFrameGraph = true), 1);
        var withoutHeight = without.Height;

        var with = _renderer.Render(Frame(2, graph), Style(s => { s.ShowFrameGraph = true; s.GraphWidth = 160; s.GraphHeight = 40; s.BackgroundOpacity = 0; }), 1);

        Assert.True(with.Height >= withoutHeight + 40, $"{with.Height} vs {withoutHeight}");
        Assert.True(with.Width >= 160 + 20);
        var drawnInGraphArea = 0;
        for (var y = with.Height - 10 - 40; y < with.Height - 10; y++)
        {
            for (var x = 10; x < 170; x++)
            {
                if (Pixel(with, x, y).A != 0)
                {
                    drawnInGraphArea++;
                }
            }
        }

        Assert.True(drawnInGraphArea > 100, $"only {drawnInGraphArea} pixels in the graph area");
    }

    [Fact]
    public void A_graph_with_fewer_than_two_samples_is_not_drawn()
    {
        var without = _renderer.Render(Frame(1), Style(s => s.ShowFrameGraph = true), 1);
        var withoutHeight = without.Height;

        var one = _renderer.Render(Frame(1, [10f]), Style(s => s.ShowFrameGraph = true), 1);

        Assert.Equal(withoutHeight, one.Height);
    }

    [Fact]
    public void An_empty_frame_renders_without_throwing()
    {
        var bitmap = _renderer.Render(Frame(0), Style(), 1);

        Assert.True(bitmap.Width >= 1);
        Assert.True(bitmap.Height >= 1);
    }

    [Fact]
    public void Rendering_repeatedly_with_changing_sizes_stays_valid()
    {
        for (var rows = 1; rows <= 6; rows++)
        {
            var bitmap = _renderer.Render(Frame(rows), Style(), 1);

            Assert.Equal(bitmap.Width * bitmap.Height * 4, bitmap.Pixels.Length);
            Assert.NotEqual(0, Pixel(bitmap, bitmap.Width - 3, bitmap.Height / 2).A);
        }
    }
}
```

- [ ] **Step 3: Run and see it fail**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-class "Supermonitor.Tests.Overlay.OverlayRendererTests"`
Expected: compile error (`OverlayRenderer` does not exist).

- [ ] **Step 4: GDI P/Invoke**

`src/Supermonitor.Overlay/Interop/Gdi32.cs`:

```csharp
using System.Runtime.InteropServices;

namespace Supermonitor.Overlay.Interop;

internal static partial class Gdi32
{
    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateDIBSection(nint hdc, ref BITMAPINFOHEADER bitmapInfo, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    public static partial nint SelectObject(nint hdc, nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(nint hdc);
}
```

- [ ] **Step 5: Implement the renderer**

`src/Supermonitor.Overlay/OverlayRenderer.cs`:

```csharp
using System.Numerics;
using System.Runtime.InteropServices;
using Supermonitor.Core.Overlay;
using Supermonitor.Overlay.Interop;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using AlphaMode = Vortice.DCommon.AlphaMode;

namespace Supermonitor.Overlay;

public readonly record struct OverlayBitmap(nint Hdc, nint Bits, int Width, int Height)
{
    public unsafe ReadOnlySpan<byte> Pixels => new((void*)Bits, Width * Height * 4);
}

// Draws the OverlayFrame with Direct2D/DirectWrite into a premultiplied 32-bit DIB (the UpdateLayeredWindow format).
// Does not touch windows: the overlay presents the DIB and the editor preview copies the pixels.
public sealed class OverlayRenderer : IDisposable
{
    private readonly ID2D1Factory1 _d2d = D2D1.D2D1CreateFactory<ID2D1Factory1>();
    private readonly IDWriteFactory _dwrite = DWrite.DWriteCreateFactory<IDWriteFactory>();
    private readonly nint _hdc = Gdi32.CreateCompatibleDC(0);
    private ID2D1DCRenderTarget? _target;
    private ID2D1SolidColorBrush? _brush;
    private IDWriteTextFormat? _format;
    private string _formatKey = string.Empty;
    private nint _dib;
    private nint _originalBitmap;
    private nint _bits;
    private int _width;
    private int _height;
    private bool _disposed;

    public OverlayBitmap Render(OverlayFrame frame, OverlayStyle style, float scale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var fontSize = style.FontSize * scale;
        var padding = style.Padding * scale;
        var format = GetFormat(style.FontFamily, fontSize);
        var count = frame.Rows.Length;
        var labels = new IDWriteTextLayout[count];
        var values = new IDWriteTextLayout[count];
        try
        {
            for (var i = 0; i < count; i++)
            {
                labels[i] = _dwrite.CreateTextLayout(frame.Rows[i].Label, format, 4096f, fontSize * 3);
                values[i] = _dwrite.CreateTextLayout(frame.Rows[i].Value, format, 4096f, fontSize * 3);
            }

            var labelX = new float[count];
            var valueX = new float[count];
            var rowY = new float[count];
            var (textWidth, textHeight) = Arrange(style.Layout, labels, values, fontSize, labelX, valueX, rowY);

            var hasGraph = style.ShowFrameGraph && frame.Graph.Length >= 2;
            var graphWidth = style.GraphWidth * scale;
            var graphHeight = style.GraphHeight * scale;
            var graphGap = count == 0 ? 0 : fontSize * 0.5f;
            var contentWidth = Math.Max(textWidth, hasGraph ? graphWidth : 0);
            var contentHeight = textHeight + (hasGraph ? graphGap + graphHeight : 0);
            var width = Math.Max(1, (int)Math.Ceiling(contentWidth + 2 * padding));
            var height = Math.Max(1, (int)Math.Ceiling(contentHeight + 2 * padding));

            EnsureSurface(width, height);
            var target = EnsureTarget();
            target.BindDC(_hdc, new RectI(0, 0, width, height));
            target.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;
            target.BeginDraw();
            target.Clear(new Color4(0f, 0f, 0f, 0f));

            if (style.BackgroundOpacity > 0)
            {
                SetBrush(Rgba.Parse(style.BackgroundColor, new Rgba(0, 0, 0)), style.BackgroundOpacity);
                var radius = Math.Min(style.CornerRadius * scale, Math.Min(width, height) / 2f);
                target.FillRoundedRectangle(new RoundedRectangle(new System.Drawing.RectangleF(0, 0, width, height), radius, radius), _brush!);
            }

            for (var i = 0; i < count; i++)
            {
                SetBrush(frame.Rows[i].LabelColor);
                target.DrawTextLayout(new Vector2(padding + labelX[i], padding + rowY[i]), labels[i], _brush!);
                SetBrush(frame.Rows[i].ValueColor);
                target.DrawTextLayout(new Vector2(padding + valueX[i], padding + rowY[i]), values[i], _brush!);
            }

            if (hasGraph)
            {
                DrawGraph(target, frame.Graph, padding, padding + textHeight + graphGap, graphWidth, graphHeight, scale, Rgba.Parse(style.ValueColor, new Rgba(0xFF, 0xFF, 0xFF)));
            }

            target.EndDraw();
            return new OverlayBitmap(_hdc, _bits, width, height);
        }
        finally
        {
            foreach (var layout in labels.Concat(values))
            {
                layout?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _brush?.Dispose();
        _target?.Dispose();
        _format?.Dispose();
        if (_dib != 0)
        {
            Gdi32.SelectObject(_hdc, _originalBitmap);
            Gdi32.DeleteObject(_dib);
        }

        Gdi32.DeleteDC(_hdc);
        _dwrite.Dispose();
        _d2d.Dispose();
    }

    // Fills in the position of each row and returns the size of the text block.
    private static (float Width, float Height) Arrange(OverlayLayout layout, IDWriteTextLayout[] labels, IDWriteTextLayout[] values, float fontSize, float[] labelX, float[] valueX, float[] rowY)
    {
        var count = labels.Length;
        if (count == 0)
        {
            return (0, 0);
        }

        var lineHeight = 0f;
        var labelColumn = 0f;
        var valueColumn = 0f;
        for (var i = 0; i < count; i++)
        {
            var label = labels[i].Metrics;
            var value = values[i].Metrics;
            lineHeight = Math.Max(lineHeight, Math.Max(label.Height, value.Height));
            labelColumn = Math.Max(labelColumn, label.Width);
            valueColumn = Math.Max(valueColumn, value.Width);
        }

        if (layout == OverlayLayout.Vertical)
        {
            var gap = fontSize * 0.8f;
            var rowGap = fontSize * 0.15f;
            for (var i = 0; i < count; i++)
            {
                rowY[i] = i * (lineHeight + rowGap);
                labelX[i] = 0;
                valueX[i] = labelColumn + gap + (valueColumn - values[i].Metrics.Width);
            }

            return (labelColumn + gap + valueColumn, count * lineHeight + (count - 1) * rowGap);
        }

        var itemGap = layout == OverlayLayout.Compact ? fontSize * 0.7f : fontSize * 1.4f;
        var labelGap = fontSize * 0.35f;
        var x = 0f;
        for (var i = 0; i < count; i++)
        {
            labelX[i] = x;
            valueX[i] = x + labels[i].Metrics.Width + labelGap;
            rowY[i] = 0;
            x = valueX[i] + values[i].Metrics.Width + itemGap;
        }

        return (x - itemGap, lineHeight);
    }

    private void DrawGraph(ID2D1DCRenderTarget target, float[] samples, float x, float y, float width, float height, float scale, Rgba lineColor)
    {
        SetBrush(new Rgba(0xFF, 0xFF, 0xFF), 0.10f);
        target.FillRoundedRectangle(new RoundedRectangle(new System.Drawing.RectangleF(x, y, width, height), 0, 0), _brush!);

        var ceiling = Math.Max(16.7f, samples.Max() * 1.1f);
        var step = width / (samples.Length - 1);
        SetBrush(lineColor);
        var previous = default(Vector2);
        for (var i = 0; i < samples.Length; i++)
        {
            var point = new Vector2(x + i * step, y + height - Math.Clamp(samples[i] / ceiling, 0f, 1f) * height);
            if (i > 0)
            {
                target.DrawLine(previous, point, _brush!, Math.Max(1f, scale));
            }

            previous = point;
        }
    }

    private void SetBrush(Rgba color, float alpha = 1f) =>
        _brush!.Color = new Color4(color.R / 255f, color.G / 255f, color.B / 255f, alpha * color.A / 255f);

    private IDWriteTextFormat GetFormat(string family, float size)
    {
        var key = $"{family}|{size:0.##}";
        if (_format is null || key != _formatKey)
        {
            _format?.Dispose();
            _format = _dwrite.CreateTextFormat(family, FontWeight.SemiBold, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, size);
            _format.WordWrapping = WordWrapping.NoWrap;
            _formatKey = key;
        }

        return _format;
    }

    private ID2D1DCRenderTarget EnsureTarget()
    {
        if (_target is null)
        {
            var properties = new RenderTargetProperties(
                RenderTargetType.Default,
                new PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied),
                0,
                0,
                RenderTargetUsage.GdiCompatible,
                FeatureLevel.Default);
            _target = _d2d.CreateDCRenderTarget(properties);
            _brush = _target.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 1f));
        }

        return _target;
    }

    private void EnsureSurface(int width, int height)
    {
        if (_dib != 0 && width == _width && height == _height)
        {
            return;
        }

        var info = new Gdi32.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<Gdi32.BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
        };
        var dib = Gdi32.CreateDIBSection(_hdc, ref info, 0, out var bits, 0, 0);
        if (dib == 0)
        {
            throw new InvalidOperationException("CreateDIBSection failed.");
        }

        var previous = Gdi32.SelectObject(_hdc, dib);
        if (_dib == 0)
        {
            _originalBitmap = previous;
        }
        else
        {
            Gdi32.DeleteObject(_dib);
        }

        _dib = dib;
        _bits = bits;
        _width = width;
        _height = height;
    }
}
```

If `target.DrawLine(previous, point, brush, width)` does not compile in version 3.8.3 (signature with mandatory `ID2D1StrokeStyle?`), pass `null` as the last argument. If `IDWriteTextLayout.Metrics` exposes `Width`/`Height` under different names, use `WidthIncludingTrailingWhitespace` or `LayoutWidth` according to IntelliSense; the spike used `layout.Metrics.Width`.

- [ ] **Step 6: Run and see it pass**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: all pass (including the 12 renderer ones).

- [ ] **Step 7: Commit**

```powershell
git add Supermonitor.slnx src/Supermonitor.Overlay tests/Supermonitor.Tests
git commit -m "feat(overlay): Direct2D overlay renderer drawing into a premultiplied DIB"
```

---

### Task 7: Layered window, monitors and OverlayService

**Files:**
- Create: `src/Supermonitor.Overlay/Interop/User32.cs`, `src/Supermonitor.Overlay/Interop/Shcore.cs`, `src/Supermonitor.Overlay/Interop/Kernel32.cs`, `src/Supermonitor.Overlay/MonitorCatalog.cs`, `src/Supermonitor.Overlay/LiveOverlayValues.cs`, `src/Supermonitor.Overlay/OverlayWindow.cs`, `src/Supermonitor.Overlay/OverlayService.cs`
- Test: `tests/Supermonitor.Tests/Overlay/MonitorCatalogTests.cs`, `tests/Supermonitor.Tests/Overlay/LiveOverlayValuesTests.cs`, `tests/Supermonitor.Tests/Hardware/OverlayWindowSmokeTests.cs`

**Interfaces:**
- Consumes: `OverlayRenderer`, `OverlayBitmap`, `OverlayFrameBuilder`, `OverlayPlacement`, `PixelRect`, `OverlayDemand`, `OverlayRefresh`, `IOverlayValues`, `AppHost`, `FrameCaptureService`, `MetricStore`, `Demand`.
- Produces (namespace `Supermonitor.Overlay`):
  - `sealed record MonitorInfo(string Id, string Name, PixelRect Bounds, bool IsPrimary, float Scale)`; `static class MonitorCatalog { IReadOnlyList<MonitorInfo> Enumerate(); MonitorInfo? Resolve(IReadOnlyList<MonitorInfo> monitors, string? id); }` — `Resolve` returns the monitor with the `Id`, otherwise the primary, otherwise the first, otherwise `null`.
  - `sealed class LiveOverlayValues(MetricStore store, FrameCaptureService frames, TimeProvider time) : IOverlayValues`.
  - `sealed class OverlayService : IDisposable` — `OverlayService(AppHost host)`; `void Apply()` (rereads `host.Settings.Current.Overlay`, any thread), `bool Enabled`, `void SetEnabled(bool enabled)` (writes, saves and applies), `event Action? Changed` (fired by `SetEnabled`, on the caller's thread).

- [ ] **Step 1: Write the tests**

`tests/Supermonitor.Tests/Overlay/MonitorCatalogTests.cs`:

```csharp
using Supermonitor.Core.Overlay;
using Supermonitor.Overlay;

namespace Supermonitor.Tests.Overlay;

public class MonitorCatalogTests
{
    private static readonly MonitorInfo Primary = new(@"\\.\DISPLAY1", "Display 1", new PixelRect(0, 0, 2560, 1440), true, 1.25f);
    private static readonly MonitorInfo Second = new(@"\\.\DISPLAY2", "Display 2", new PixelRect(2560, 0, 1920, 1080), false, 1f);

    [Fact]
    public void A_known_id_picks_that_monitor()
    {
        Assert.Equal(Second, MonitorCatalog.Resolve([Primary, Second], @"\\.\DISPLAY2"));
    }

    [Fact]
    public void No_id_picks_the_primary_monitor()
    {
        Assert.Equal(Primary, MonitorCatalog.Resolve([Second, Primary], null));
    }

    [Fact]
    public void An_unplugged_monitor_falls_back_to_the_primary()
    {
        Assert.Equal(Primary, MonitorCatalog.Resolve([Primary], @"\\.\DISPLAY9"));
    }

    [Fact]
    public void Without_a_primary_flag_the_first_monitor_is_used()
    {
        var other = Second with { IsPrimary = false };

        Assert.Equal(other, MonitorCatalog.Resolve([other], null));
    }

    [Fact]
    public void No_monitors_gives_null()
    {
        Assert.Null(MonitorCatalog.Resolve([], null));
    }
}
```

`tests/Supermonitor.Tests/Overlay/LiveOverlayValuesTests.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using Supermonitor.Core.Frames;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Overlay;
using Supermonitor.Core.Sensors;
using Supermonitor.Overlay;

namespace Supermonitor.Tests.Overlay;

public class LiveOverlayValuesTests
{
    private sealed class NoProbe : IForegroundProbe
    {
        public int GetForegroundProcessId() => 0;
    }

    private static FrameCaptureService IdleFrames(FakeTimeProvider time) =>
        new(new DemandTracker(), () => throw new InvalidOperationException("not used"), new NoProbe(), time, 1, runLoop: false);

    [Fact]
    public void Store_backed_metrics_read_the_latest_value()
    {
        var store = new MetricStore(10);
        var expected = new Dictionary<OverlayMetric, double>();
        var number = 1;
        foreach (var metric in Enum.GetValues<OverlayMetric>())
        {
            if (OverlayMetricCatalog.StoreMetricId(metric) is not { } id)
            {
                continue;
            }

            var handle = store.Register(new MetricDescriptor(id, metric.ToString(), MetricCategory.System, OverlayMetricCatalog.Unit(metric)), "test");
            store.Write(handle, number);
            expected[metric] = number++;
        }

        store.CommitTick();
        var time = new FakeTimeProvider();
        using var frames = IdleFrames(time);
        var values = new LiveOverlayValues(store, frames, time);

        Assert.NotEmpty(expected);
        foreach (var (metric, value) in expected)
        {
            Assert.Equal(value, values.Get(metric));
        }
    }

    [Fact]
    public void Frame_metrics_and_missing_store_metrics_are_nan_without_data()
    {
        var time = new FakeTimeProvider();
        using var frames = IdleFrames(time);
        var values = new LiveOverlayValues(new MetricStore(10), frames, time);

        Assert.True(double.IsNaN(values.Get(OverlayMetric.Fps)));
        Assert.True(double.IsNaN(values.Get(OverlayMetric.FrameTime)));
        Assert.True(double.IsNaN(values.Get(OverlayMetric.Low1Percent)));
        Assert.True(double.IsNaN(values.Get(OverlayMetric.CpuUsage)));
        Assert.True(double.IsNaN(values.Get(OverlayMetric.Clock)));
        Assert.Equal(0, values.CopyFrameTimes(new float[10]));
    }

    [Fact]
    public void Now_is_the_local_time_of_the_provider()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 10, 21, 7, 0, TimeSpan.Zero));
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        using var frames = IdleFrames(time);

        var values = new LiveOverlayValues(new MetricStore(10), frames, time);

        Assert.Equal(new DateTime(2026, 10, 10, 21, 7, 0), values.Now);
    }
}
```

`tests/Supermonitor.Tests/Hardware/OverlayWindowSmokeTests.cs`:

```csharp
using Supermonitor.Overlay;

namespace Supermonitor.Tests.Hardware;

[Trait("Category", "Hardware")]
public class OverlayWindowSmokeTests
{
    [Fact]
    public void At_least_one_monitor_is_found_and_one_is_primary_with_a_plausible_scale()
    {
        var monitors = MonitorCatalog.Enumerate();

        Assert.NotEmpty(monitors);
        var primary = Assert.Single(monitors, m => m.IsPrimary);
        Assert.InRange(primary.Scale, 1f, 4f);
        Assert.True(primary.Bounds.Width > 0 && primary.Bounds.Height > 0);
        Assert.All(monitors, m => Assert.False(string.IsNullOrWhiteSpace(m.Id)));
        Assert.Equal(monitors.Count, monitors.Select(m => m.Id).Distinct().Count());
    }
}
```

- [ ] **Step 2: Run and see it fail**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: compile error (`MonitorInfo`, `MonitorCatalog`, `LiveOverlayValues` do not exist).

- [ ] **Step 3: P/Invoke**

`src/Supermonitor.Overlay/Interop/Kernel32.cs`:

```csharp
using System.Runtime.InteropServices;

namespace Supermonitor.Overlay.Interop;

internal static partial class Kernel32
{
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandleW(string? moduleName);
}
```

`src/Supermonitor.Overlay/Interop/Shcore.cs`:

```csharp
using System.Runtime.InteropServices;

namespace Supermonitor.Overlay.Interop;

internal static partial class Shcore
{
    public const int MDT_EFFECTIVE_DPI = 0;

    [LibraryImport("shcore.dll")]
    public static partial int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}
```

`src/Supermonitor.Overlay/Interop/User32.cs`:

```csharp
using System.Runtime.InteropServices;

namespace Supermonitor.Overlay.Interop;

internal static unsafe partial class User32
{
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_TRANSPARENT = 0x00000020;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_NOACTIVATE = 0x08000000;
    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;
    public const uint ULW_ALPHA = 0x00000002;
    public const byte AC_SRC_OVER = 0;
    public const byte AC_SRC_ALPHA = 1;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_APP = 0x8000;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint MONITORINFOF_PRIMARY = 1;
    public static readonly nint HWND_TOPMOST = -1;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public nint lpszMenuName;
        public nint lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFOEXW
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        public fixed char szDevice[32];
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial ushort RegisterClassExW(WNDCLASSEXW* windowClass);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterClassW(nint className, nint instance);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint CreateWindowExW(uint exStyle, nint className, nint windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint DefWindowProcW(nint hwnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UpdateLayeredWindow(nint hwnd, nint hdcDestination, POINT* destination, SIZE* size, nint hdcSource, POINT* source, uint colorKey, BLENDFUNCTION* blend, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    public static partial int GetMessageW(MSG* message, nint hwnd, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(MSG* message);

    [LibraryImport("user32.dll")]
    public static partial nint DispatchMessageW(MSG* message);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(nint hwnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll")]
    public static partial nuint SetTimer(nint hwnd, nuint id, uint elapseMs, nint timerProc);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool KillTimer(nint hwnd, nuint id);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumDisplayMonitors(nint hdc, RECT* clip, delegate* unmanaged[Stdcall]<nint, nint, RECT*, nint, int> callback, nint data);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfoW(nint monitor, MONITORINFOEXW* info);
}
```

- [ ] **Step 4: Monitors and live values**

`src/Supermonitor.Overlay/MonitorCatalog.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Supermonitor.Core.Overlay;
using Supermonitor.Overlay.Interop;

namespace Supermonitor.Overlay;

public sealed record MonitorInfo(string Id, string Name, PixelRect Bounds, bool IsPrimary, float Scale);

public static unsafe class MonitorCatalog
{
    public static MonitorInfo? Resolve(IReadOnlyList<MonitorInfo> monitors, string? id) =>
        monitors.FirstOrDefault(m => id is not null && m.Id == id)
        ?? monitors.FirstOrDefault(m => m.IsPrimary)
        ?? monitors.FirstOrDefault();

    public static IReadOnlyList<MonitorInfo> Enumerate()
    {
        var found = new List<MonitorInfo>();
        var handle = GCHandle.Alloc(found);
        try
        {
            User32.EnumDisplayMonitors(0, null, &OnMonitor, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        return found
            .Select((m, index) => m with { Name = $"Display {index + 1} ({m.Bounds.Width}×{m.Bounds.Height}){(m.IsPrimary ? ", primary" : string.Empty)}" })
            .ToArray();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnMonitor(nint monitor, nint hdc, User32.RECT* rect, nint data)
    {
        var list = (List<MonitorInfo>)GCHandle.FromIntPtr(data).Target!;
        var info = new User32.MONITORINFOEXW { cbSize = (uint)sizeof(User32.MONITORINFOEXW) };
        if (User32.GetMonitorInfoW(monitor, &info))
        {
            var bounds = info.rcMonitor;
            Shcore.GetDpiForMonitor(monitor, Shcore.MDT_EFFECTIVE_DPI, out var dpi, out _);
            list.Add(new MonitorInfo(
                new string(info.szDevice),
                string.Empty,
                new PixelRect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top),
                (info.dwFlags & User32.MONITORINFOF_PRIMARY) != 0,
                dpi == 0 ? 1f : dpi / 96f));
        }

        return 1;
    }
}
```

`src/Supermonitor.Overlay/LiveOverlayValues.cs`:

```csharp
using Supermonitor.Core.Frames;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Overlay;

namespace Supermonitor.Overlay;

// Reads the latest value of each metric from the MetricStore and the FPS numbers from the FrameCaptureService.
public sealed class LiveOverlayValues(MetricStore store, FrameCaptureService frames, TimeProvider time) : IOverlayValues
{
    public DateTime Now => time.GetLocalNow().DateTime;

    public long FrameVersion => frames.FrameVersion;

    public double Get(OverlayMetric metric) => metric switch
    {
        OverlayMetric.Fps => frames.Current.Fps,
        OverlayMetric.FrameTime => frames.Current.FrameTimeMs,
        OverlayMetric.Low1Percent => frames.Current.Low1PercentFps,
        _ => OverlayMetricCatalog.StoreMetricId(metric) is { } id ? store.GetLatest(id) : double.NaN,
    };

    public int CopyFrameTimes(Span<float> destination) => frames.CopyFrameTimes(destination);
}
```

- [ ] **Step 5: The layered window**

`src/Supermonitor.Overlay/OverlayWindow.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;
using Supermonitor.Overlay.Interop;

namespace Supermonitor.Overlay;

// Layered Win32 window, click-through, always on top, without focus and outside Alt+Tab.
// It must be created and used on the thread that runs the message loop.
internal sealed unsafe class OverlayWindow : IDisposable
{
    private const string ClassName = "Supermonitor.Overlay";

    private readonly Func<uint, nuint, nint, bool> _handler;
    private readonly User32.WndProc _wndProc;
    private readonly nint _className;
    private readonly nint _instance;

    public OverlayWindow(Func<uint, nuint, nint, bool> handler)
    {
        _handler = handler;
        _wndProc = WndProc;
        _instance = Kernel32.GetModuleHandleW(null);
        _className = Marshal.StringToHGlobalUni(ClassName);

        var windowClass = new User32.WNDCLASSEXW
        {
            cbSize = (uint)sizeof(User32.WNDCLASSEXW),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = _instance,
            lpszClassName = _className,
        };
        if (User32.RegisterClassExW(&windowClass) == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        const uint exStyle = User32.WS_EX_LAYERED | User32.WS_EX_TRANSPARENT | User32.WS_EX_TOPMOST | User32.WS_EX_NOACTIVATE | User32.WS_EX_TOOLWINDOW;
        Handle = User32.CreateWindowExW(exStyle, _className, _className, User32.WS_POPUP, 0, 0, 1, 1, 0, 0, _instance, 0);
        if (Handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    public nint Handle { get; private set; }

    // Swaps content and position at once; the DIB already comes premultiplied.
    public void Present(OverlayBitmap bitmap, int x, int y)
    {
        var position = new User32.POINT { X = x, Y = y };
        var size = new User32.SIZE { cx = bitmap.Width, cy = bitmap.Height };
        var origin = new User32.POINT();
        var blend = new User32.BLENDFUNCTION { BlendOp = User32.AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = User32.AC_SRC_ALPHA };
        if (!User32.UpdateLayeredWindow(Handle, 0, &position, &size, bitmap.Hdc, &origin, 0, &blend, User32.ULW_ALPHA))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    public void Show() => User32.ShowWindow(Handle, User32.SW_SHOWNOACTIVATE);

    public void Hide() => User32.ShowWindow(Handle, User32.SW_HIDE);

    // Borderless games are also topmost; reasserting the order from time to time keeps the overlay on top.
    public void KeepOnTop() =>
        User32.SetWindowPos(Handle, User32.HWND_TOPMOST, 0, 0, 0, 0, User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);

    public void Dispose()
    {
        if (Handle != 0)
        {
            User32.DestroyWindow(Handle);
            Handle = 0;
            User32.UnregisterClassW(_className, _instance);
            Marshal.FreeHGlobal(_className);
        }
    }

    private nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (_handler(msg, wParam, lParam))
        {
            return 0;
        }

        if (msg == User32.WM_DESTROY)
        {
            User32.PostQuitMessage(0);
            return 0;
        }

        return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
```

- [ ] **Step 6: The service**

`src/Supermonitor.Overlay/OverlayService.cs`:

```csharp
using System.Diagnostics;
using Supermonitor.Core;
using Supermonitor.Core.Diagnostics;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Overlay;
using Supermonitor.Core.Sensors;
using Supermonitor.Core.Settings;
using Supermonitor.Overlay.Interop;

namespace Supermonitor.Overlay;

// Owner of the overlay window. A dedicated thread runs the message loop, draws at the configured rate
// and only redraws when some text (or the chart) changed. Holds the demand leases while visible.
public sealed unsafe class OverlayService : IDisposable
{
    private const nuint RenderTimerId = 1;
    private const uint ApplyMessage = User32.WM_APP + 1;
    private static readonly TimeSpan HousekeepingInterval = TimeSpan.FromSeconds(1);

    private sealed record Runtime(bool Enabled, string? MonitorId, OverlayStyle Style, UnitOptions Units, IReadOnlyDictionary<string, MetricThreshold> Thresholds);

    private readonly AppHost _host;
    private readonly LiveOverlayValues _values;
    private readonly Thread _thread;
    private Runtime? _pending;
    private nint _handle;
    private bool _disposed;

    // Overlay thread state.
    private Runtime _current = new(false, null, new OverlayStyle(), UnitOptions.Default, new Dictionary<string, MetricThreshold>());
    private OverlayWindow? _window;
    private OverlayRenderer? _renderer;
    private OverlayFrame? _lastFrame;
    private MonitorInfo? _monitor;
    private IDisposable? _hardwareLease;
    private IDisposable? _frameLease;
    private bool _visible;
    private bool _force;
    private bool _failing;
    private long _lastHousekeeping;

    public OverlayService(AppHost host)
    {
        _host = host;
        _values = new LiveOverlayValues(host.Metrics, host.Frames, TimeProvider.System);
        _thread = new Thread(Run) { IsBackground = true, Name = "Overlay" };
        Apply();
        _thread.Start();
    }

    public event Action? Changed;

    public bool Enabled => _host.Settings.Current.Overlay.Enabled;

    public void SetEnabled(bool enabled)
    {
        _host.Settings.Current.Overlay.Enabled = enabled;
        _host.Settings.RequestSave();
        Apply();
        Changed?.Invoke();
    }

    // Takes a copy of the settings (call it on the thread that changes them) and notifies the overlay thread.
    public void Apply()
    {
        var settings = _host.Settings.Current;
        var overlay = settings.Overlay;
        var thresholds = settings.Thresholds.ToDictionary(
            pair => pair.Key,
            pair => new MetricThreshold { Warning = pair.Value.Warning, Critical = pair.Value.Critical });
        Volatile.Write(ref _pending, new Runtime(overlay.Enabled, overlay.MonitorId, overlay.Style.Clone(), settings.ToUnitOptions(), thresholds));

        var handle = Volatile.Read(ref _handle);
        if (handle != 0)
        {
            User32.PostMessageW(handle, ApplyMessage, 0, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var handle = Volatile.Read(ref _handle);
        if (handle != 0)
        {
            User32.PostMessageW(handle, User32.WM_CLOSE, 0, 0);
        }

        _thread.Join(TimeSpan.FromSeconds(3));
        ReleaseLeases();
    }

    private void Run()
    {
        try
        {
            _renderer = new OverlayRenderer();
            _window = new OverlayWindow(HandleMessage);
            Volatile.Write(ref _handle, _window.Handle);
            ApplyPending();

            User32.MSG message;
            while (User32.GetMessageW(&message, 0, 0, 0) > 0)
            {
                User32.TranslateMessage(&message);
                User32.DispatchMessageW(&message);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Overlay thread failed", ex);
        }
        finally
        {
            Volatile.Write(ref _handle, 0);
            ReleaseLeases();
            _window?.Dispose();
            _renderer?.Dispose();
        }
    }

    private bool HandleMessage(uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (message == ApplyMessage)
            {
                ApplyPending();
                return true;
            }

            if (message == User32.WM_TIMER && wParam == RenderTimerId)
            {
                Tick();
                return true;
            }
        }
        catch (Exception ex)
        {
            // One error at a time in the log: at 30 Hz, repeating the same failure would flood the file.
            if (!_failing)
            {
                _failing = true;
                Log.Error("Overlay update failed", ex);
            }

            return message is ApplyMessage or User32.WM_TIMER;
        }

        return false;
    }

    private void ApplyPending()
    {
        var pending = Interlocked.Exchange(ref _pending, null);
        if (pending is null)
        {
            return;
        }

        _current = pending;
        _force = true;
        _lastFrame = null;
        UpdateLeases();

        if (!pending.Enabled)
        {
            User32.KillTimer(_window!.Handle, RenderTimerId);
            if (_visible)
            {
                _window.Hide();
                _visible = false;
            }

            return;
        }

        RefreshMonitor();
        var interval = (uint)(1000 / OverlayRefresh.EffectiveHz(pending.Style));
        User32.SetTimer(_window!.Handle, RenderTimerId, interval, 0);
        Tick();
    }

    private void Tick()
    {
        var now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(_lastHousekeeping, now) >= HousekeepingInterval)
        {
            _lastHousekeeping = now;
            RefreshMonitor();
            if (_visible)
            {
                _window!.KeepOnTop();
            }
        }

        var runtime = _current;
        var frame = OverlayFrameBuilder.Build(runtime.Style, _values, runtime.Units, runtime.Thresholds);
        if (!_force && frame.SameContentAs(_lastFrame))
        {
            return;
        }

        if (_monitor is not { } monitor)
        {
            return;
        }

        _force = false;
        var bitmap = _renderer!.Render(frame, runtime.Style, monitor.Scale);
        var (x, y) = OverlayPlacement.Compute(
            runtime.Style.Anchor,
            (int)(runtime.Style.OffsetX * monitor.Scale),
            (int)(runtime.Style.OffsetY * monitor.Scale),
            monitor.Bounds,
            bitmap.Width,
            bitmap.Height);
        _window!.Present(bitmap, x, y);
        if (!_visible)
        {
            _window.Show();
            _visible = true;
        }

        _lastFrame = frame;
        _failing = false;
    }

    // Chosen monitor (or the primary if it disappeared); changed position, size or DPI → redraw.
    private void RefreshMonitor()
    {
        var resolved = MonitorCatalog.Resolve(MonitorCatalog.Enumerate(), _current.MonitorId);
        if (resolved != _monitor)
        {
            _monitor = resolved;
            _force = true;
        }
    }

    private void UpdateLeases()
    {
        var wanted = _current.Enabled ? OverlayDemand.For(_current.Style) : (Hardware: false, Frames: false);
        Lease(ref _hardwareLease, wanted.Hardware, Demand.HardwareSensors);
        Lease(ref _frameLease, wanted.Frames, Demand.FrameCapture);
    }

    private void Lease(ref IDisposable? lease, bool wanted, Demand demand)
    {
        if (wanted)
        {
            lease ??= _host.Demand.Acquire(demand);
        }
        else
        {
            lease?.Dispose();
            lease = null;
        }
    }

    private void ReleaseLeases()
    {
        _hardwareLease?.Dispose();
        _hardwareLease = null;
        _frameLease?.Dispose();
        _frameLease = null;
    }
}
```

- [ ] **Step 7: Run and see it pass**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: all pass.

Run: `dotnet test --project tests/Supermonitor.Tests --filter-class "Supermonitor.Tests.Hardware.OverlayWindowSmokeTests"`
Expected: passes (monitors enumerated, one primary, scale between 1 and 4).

Run: `dotnet build src/Supermonitor.Overlay -p:Platform=x64 2>&1 | Select-String "error|Erro"`
Expected: no errors (`0 Error(s)`). If `delegate* unmanaged[Stdcall]` in `LibraryImport` is rejected by the generator, replace the parameter with `nint callback` and pass `(nint)(delegate* unmanaged[Stdcall]<nint, nint, User32.RECT*, nint, int>)&OnMonitor`.

- [ ] **Step 8: Commit**

```powershell
git add src/Supermonitor.Overlay tests/Supermonitor.Tests
git commit -m "feat(overlay): layered overlay window, monitor catalog and overlay service"
```

---

### Task 8: App integration (OverlayService and tray item)

**Files:**
- Modify: `src/Supermonitor.Core/Interop/User32.cs`, `src/Supermonitor.Core/Shell/TrayIcon.cs`, `src/Supermonitor/Supermonitor.csproj`, `src/Supermonitor/App.xaml.cs`

**Interfaces:**
- Consumes: `OverlayService` (Task 7), `TrayIcon`, `AppHost`.
- Produces:
  - `TrayIcon(string tooltip, string iconPath, Action onOpen, Action onExit, Func<bool>? isOverlayEnabled = null, Action? onToggleOverlay = null)` — with the last two filled in, the menu gains the "Show overlay" item, checked when on.
  - `App.Overlay` (`OverlayService?`) — the editor page (Task 9) uses it to apply changes.

This task has no unit test (tray and window depend on a desktop session); verification is manual at the end.

- [ ] **Step 1: Menu constant**

In `src/Supermonitor.Core/Interop/User32.cs`, next to `MF_STRING`:

```csharp
    public const uint MF_CHECKED = 0x0008;
```

- [ ] **Step 2: Tray menu item**

In `src/Supermonitor.Core/Shell/TrayIcon.cs`:

1. Constant `private const int MenuOverlay = 3;`.
2. Fields `private readonly Func<bool>? _isOverlayEnabled;` and `private readonly Action? _onToggleOverlay;`.
3. Constructor:

```csharp
    public TrayIcon(string tooltip, string iconPath, Action onOpen, Action onExit, Func<bool>? isOverlayEnabled = null, Action? onToggleOverlay = null)
    {
        _tooltip = tooltip;
        _onOpen = onOpen;
        _onExit = onExit;
        _isOverlayEnabled = isOverlayEnabled;
        _onToggleOverlay = onToggleOverlay;
```

(the rest of the constructor stays as is).

4. In `ShowMenu()`, between "Open Supermonitor" and the separator:

```csharp
        User32.AppendMenuW(menu, User32.MF_STRING, MenuOpen, "Open Supermonitor");
        if (_onToggleOverlay is not null)
        {
            var flags = User32.MF_STRING | (_isOverlayEnabled?.Invoke() == true ? User32.MF_CHECKED : 0);
            User32.AppendMenuW(menu, flags, MenuOverlay, "Show overlay");
        }

        User32.AppendMenuW(menu, User32.MF_SEPARATOR, 0, null);
```

5. In the command handling, after `MenuExit`:

```csharp
        else if (command == MenuOverlay)
        {
            _onToggleOverlay?.Invoke();
        }
```

- [ ] **Step 3: Reference the Overlay project**

In `src/Supermonitor/Supermonitor.csproj`, in the `ProjectReference` `ItemGroup`:

```xml
    <ProjectReference Include="..\Supermonitor.Overlay\Supermonitor.Overlay.csproj" />
```

- [ ] **Step 4: Wire into `App`**

In `src/Supermonitor/App.xaml.cs`:

1. `using Supermonitor.Overlay;`
2. Field `private OverlayService? _overlay;` and public property `public OverlayService? Overlay => _overlay;` (next to `PawnIoBannerDismissed`).
3. In `OnLaunched`, after `_host.Start();` and before creating the tray:

```csharp
        _overlay = new OverlayService(_host);
```

and in the tray creation:

```csharp
        _tray = new TrayIcon(
            "Supermonitor",
            AppPaths.IconPath,
            onOpen: () => _dispatcher.TryEnqueue(ShowDashboard),
            onExit: () => _dispatcher.TryEnqueue(ExitApp),
            isOverlayEnabled: () => _overlay?.Enabled == true,
            onToggleOverlay: () => _dispatcher.TryEnqueue(() => _overlay?.SetEnabled(_overlay.Enabled == false)));
```

4. In `ExitApp()`, after `_window?.Close();` and before `_host.DisposeAsync()`:

```csharp
            _overlay?.Dispose();
```

- [ ] **Step 5: Build**

Run: `dotnet build src/Supermonitor/Supermonitor.csproj -c Debug -p:Platform=x64 -o <temporary folder> 2>&1 | Select-String "error|Erro"`
Expected: no errors (`0 Error(s)`).

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: all pass.

- [ ] **Step 6: Manual verification (user)**

The user exits the app through the tray, the assistant rebuilds and opens the elevated app. Check, in this order:

1. Tray menu shows "Show overlay" unchecked. Click it: the overlay appears in the top-left corner (Default preset, 16 px margin) and the check mark appears when the menu is reopened.
2. Click another window that animates (video, page with animation): FPS and frametime appear and the mini-chart moves. Click a still window: FPS goes back to `N/A` in ~2 s.
3. The overlay does not steal focus, clicks pass through it and it does not appear in Alt+Tab.
4. The overlay's CPU, RAM and GPU match Task Manager (±2 points).
5. Turn off through the menu: the overlay disappears. `%LocalAppData%\Supermonitor\settings.json` has `"overlay": { "enabled": ... }` and `"version": 3`.
6. Exit through the "Exit" menu: `logman query -ets` (elevated PowerShell) does not list `Supermonitor-FPS`.

- [ ] **Step 7: Commit**

```powershell
git add src
git commit -m "feat(app): run the overlay service and toggle it from the tray menu"
```

---

### Task 9: Overlay page (editor with live preview and presets)

**Files:**
- Create: `src/Supermonitor/Views/OverlayPage.xaml`, `src/Supermonitor/Views/OverlayPage.xaml.cs`, `src/Supermonitor/Dialogs/OverlayItemDialog.cs`
- Modify: `src/Supermonitor/MainWindow.xaml`, `src/Supermonitor/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `OverlayEditor`, `OverlayPresets`, `OverlaySettings`, `OverlayStyle`, `OverlayItemSettings`, `OverlayMetric`, `OverlayMetricCatalog`, `OverlayFrameBuilder`, `DemoOverlayValues`, `OverlayRenderer`, `MonitorCatalog`, `OverlayService` (via `App.Current.Overlay`), `PageContext`, `ColorText`, `SettingsStore`.
- Produces: "Overlay" navigation item (tag `overlay`); `OverlayItemDialog(OverlayItemSettings item)` with `OverlayItemSettings Result`.

This task is UI; the tests come from Tasks 2 and 3 (logic) and from the manual verification at the end.

- [ ] **Step 1: Navigation item**

In `src/Supermonitor/MainWindow.xaml`, after the "Processes" item:

```xml
                <NavigationViewItem Content="Overlay" Tag="overlay">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE7F4;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
```

In `MainWindow.xaml.cs`, in the `switch` of `Navigate`, before `case "settings":`:

```csharp
            case "overlay":
                ContentFrame.Navigate(typeof(OverlayPage), context);
                break;
```

- [ ] **Step 2: Per-item colors and thresholds dialog**

`src/Supermonitor/Dialogs/OverlayItemDialog.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Supermonitor.Core.Overlay;
using Supermonitor.Core.Settings;

namespace Supermonitor.Dialogs;

public sealed class OverlayItemDialog : ContentDialog
{
    private readonly TextBox _labelColor;
    private readonly TextBox _valueColor;
    private readonly NumberBox _warning;
    private readonly NumberBox _critical;
    private readonly TextBlock _error;
    private readonly OverlayItemSettings _item;

    public OverlayItemDialog(OverlayItemSettings item)
    {
        _item = item;
        Result = item.Clone();
        Title = $"{OverlayMetricCatalog.Label(item.Metric)} — colors and thresholds";
        PrimaryButtonText = "Save";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        _labelColor = new TextBox { Header = "Label color", PlaceholderText = "#RRGGBB — empty uses the overlay default", Text = item.LabelColor ?? string.Empty };
        _valueColor = new TextBox { Header = "Value color", PlaceholderText = "#RRGGBB — empty uses the overlay default", Text = item.ValueColor ?? string.Empty };
        _warning = new NumberBox { Header = "Warning at", Value = item.Threshold?.Warning ?? double.NaN, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        _critical = new NumberBox { Header = "Critical at", Value = item.Threshold?.Critical ?? double.NaN, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        _error = new TextBlock { Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"] };
        Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                _labelColor,
                _valueColor,
                new TextBlock { Text = "Thresholds use the metric's base unit (°C, %, bytes, ms). Leave both empty to use the dashboard thresholds.", TextWrapping = TextWrapping.Wrap },
                _warning,
                _critical,
                _error,
            },
        };

        PrimaryButtonClick += OnSave;
    }

    public OverlayItemSettings Result { get; private set; }

    private void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var label = _labelColor.Text.Trim();
        var value = _valueColor.Text.Trim();
        if ((label.Length > 0 && !ColorText.TryParseRgb(label, out _, out _, out _))
            || (value.Length > 0 && !ColorText.TryParseRgb(value, out _, out _, out _)))
        {
            Fail(args, "Colors must look like #RRGGBB.");
            return;
        }

        var hasWarning = !double.IsNaN(_warning.Value);
        var hasCritical = !double.IsNaN(_critical.Value);
        if (hasWarning != hasCritical)
        {
            Fail(args, "Fill both thresholds or leave both empty.");
            return;
        }

        var result = _item.Clone();
        result.LabelColor = label.Length == 0 ? null : label.ToUpperInvariant();
        result.ValueColor = value.Length == 0 ? null : value.ToUpperInvariant();
        result.Threshold = hasWarning ? new MetricThreshold { Warning = (float)_warning.Value, Critical = (float)_critical.Value } : null;
        Result = result;
    }

    private void Fail(ContentDialogButtonClickEventArgs args, string message)
    {
        args.Cancel = true;
        _error.Text = message;
        _error.Visibility = Visibility.Visible;
    }
}
```

- [ ] **Step 3: Page XAML**

`src/Supermonitor/Views/OverlayPage.xaml`:

```xml
<Page
    x:Class="Supermonitor.Views.OverlayPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <ScrollViewer>
        <Grid Padding="24,0,24,24" ColumnSpacing="32">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="440" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>

            <StackPanel Spacing="16">
                <TextBlock Text="Overlay" Style="{StaticResource TitleTextBlockStyle}" />
                <ToggleSwitch x:Name="EnabledSwitch" Header="Show overlay" Toggled="OnEnabledToggled" />
                <TextBlock
                    Style="{StaticResource CaptionTextBlockStyle}"
                    Opacity="0.7"
                    TextWrapping="Wrap"
                    Text="Shows over windowed and borderless games. FPS is measured on the window in the foreground; exclusive fullscreen is not supported." />
                <InfoBar x:Name="CaptureBar" Severity="Warning" IsOpen="False" IsClosable="False" Title="FPS capture unavailable" />

                <StackPanel Spacing="8">
                    <TextBlock Text="Preset" Style="{StaticResource BodyStrongTextBlockStyle}" />
                    <Grid ColumnSpacing="8">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <ComboBox x:Name="PresetBox" HorizontalAlignment="Stretch" SelectionChanged="OnPresetChanged" />
                        <Button x:Name="SavePresetButton" Grid.Column="1" Content="Save as…" Click="OnSavePresetClick" />
                        <Button x:Name="DeletePresetButton" Grid.Column="2" Content="Delete" Click="OnDeletePresetClick" />
                    </Grid>
                </StackPanel>

                <StackPanel Spacing="8">
                    <TextBlock Text="Position" Style="{StaticResource BodyStrongTextBlockStyle}" />
                    <ComboBox x:Name="MonitorBox" Header="Monitor" HorizontalAlignment="Stretch" SelectionChanged="OnMonitorChanged" />
                    <ComboBox x:Name="AnchorBox" Header="Anchor" HorizontalAlignment="Stretch" SelectionChanged="OnAnchorChanged">
                        <ComboBoxItem Content="Top left" />
                        <ComboBoxItem Content="Top center" />
                        <ComboBoxItem Content="Top right" />
                        <ComboBoxItem Content="Middle left" />
                        <ComboBoxItem Content="Center" />
                        <ComboBoxItem Content="Middle right" />
                        <ComboBoxItem Content="Bottom left" />
                        <ComboBoxItem Content="Bottom center" />
                        <ComboBoxItem Content="Bottom right" />
                    </ComboBox>
                    <Grid ColumnSpacing="8">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>
                        <NumberBox x:Name="OffsetXBox" Tag="offsetX" Header="Offset X (px)" Minimum="-4000" Maximum="4000" SpinButtonPlacementMode="Compact" ValueChanged="OnNumberChanged" />
                        <NumberBox x:Name="OffsetYBox" Tag="offsetY" Grid.Column="1" Header="Offset Y (px)" Minimum="-4000" Maximum="4000" SpinButtonPlacementMode="Compact" ValueChanged="OnNumberChanged" />
                    </Grid>
                </StackPanel>

                <StackPanel Spacing="8">
                    <TextBlock Text="Metrics" Style="{StaticResource BodyStrongTextBlockStyle}" />
                    <ComboBox x:Name="LayoutBox" Header="Layout" HorizontalAlignment="Stretch" SelectionChanged="OnLayoutChanged">
                        <ComboBoxItem Content="Horizontal" />
                        <ComboBoxItem Content="Vertical" />
                        <ComboBoxItem Content="Compact" />
                    </ComboBox>
                    <StackPanel x:Name="ItemsPanel" Spacing="2" />
                    <Button x:Name="AddItemButton" Content="Add metric">
                        <Button.Flyout>
                            <MenuFlyout x:Name="AddItemFlyout" Opening="OnAddItemMenuOpening" />
                        </Button.Flyout>
                    </Button>
                </StackPanel>

                <StackPanel Spacing="8">
                    <TextBlock Text="Text" Style="{StaticResource BodyStrongTextBlockStyle}" />
                    <Grid ColumnSpacing="8">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="120" />
                        </Grid.ColumnDefinitions>
                        <ComboBox x:Name="FontBox" Header="Font" IsEditable="True" HorizontalAlignment="Stretch" SelectionChanged="OnFontChanged" TextSubmitted="OnFontSubmitted" />
                        <NumberBox x:Name="FontSizeBox" Tag="fontSize" Grid.Column="1" Header="Size" Minimum="8" Maximum="48" SpinButtonPlacementMode="Compact" ValueChanged="OnNumberChanged" />
                    </Grid>
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <Button x:Name="LabelColorButton">
                            <StackPanel Orientation="Horizontal" Spacing="8">
                                <Border x:Name="LabelSwatch" Width="16" Height="16" CornerRadius="3" />
                                <TextBlock Text="Label color" />
                            </StackPanel>
                            <Button.Flyout>
                                <Flyout>
                                    <ColorPicker x:Name="LabelColorPicker" Tag="label" IsAlphaEnabled="False" ColorChanged="OnColorChanged" />
                                </Flyout>
                            </Button.Flyout>
                        </Button>
                        <Button x:Name="ValueColorButton">
                            <StackPanel Orientation="Horizontal" Spacing="8">
                                <Border x:Name="ValueSwatch" Width="16" Height="16" CornerRadius="3" />
                                <TextBlock Text="Value color" />
                            </StackPanel>
                            <Button.Flyout>
                                <Flyout>
                                    <ColorPicker x:Name="ValueColorPicker" Tag="value" IsAlphaEnabled="False" ColorChanged="OnColorChanged" />
                                </Flyout>
                            </Button.Flyout>
                        </Button>
                    </StackPanel>
                </StackPanel>

                <StackPanel Spacing="8">
                    <TextBlock Text="Background" Style="{StaticResource BodyStrongTextBlockStyle}" />
                    <Button x:Name="BackgroundColorButton">
                        <StackPanel Orientation="Horizontal" Spacing="8">
                            <Border x:Name="BackgroundSwatch" Width="16" Height="16" CornerRadius="3" />
                            <TextBlock Text="Background color" />
                        </StackPanel>
                        <Button.Flyout>
                            <Flyout>
                                <ColorPicker x:Name="BackgroundColorPicker" Tag="background" IsAlphaEnabled="False" ColorChanged="OnColorChanged" />
                            </Flyout>
                        </Button.Flyout>
                    </Button>
                    <Slider x:Name="OpacitySlider" Header="Opacity (%)" Minimum="0" Maximum="100" ValueChanged="OnOpacityChanged" />
                    <Grid ColumnSpacing="8">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>
                        <NumberBox x:Name="RadiusBox" Tag="radius" Header="Corner radius" Minimum="0" Maximum="32" SpinButtonPlacementMode="Compact" ValueChanged="OnNumberChanged" />
                        <NumberBox x:Name="PaddingBox" Tag="padding" Grid.Column="1" Header="Padding" Minimum="0" Maximum="48" SpinButtonPlacementMode="Compact" ValueChanged="OnNumberChanged" />
                    </Grid>
                </StackPanel>

                <StackPanel Spacing="8">
                    <TextBlock Text="Frametime graph" Style="{StaticResource BodyStrongTextBlockStyle}" />
                    <ToggleSwitch x:Name="GraphSwitch" Header="Show graph" Toggled="OnGraphToggled" />
                    <Grid ColumnSpacing="8">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>
                        <NumberBox x:Name="GraphWidthBox" Tag="graphWidth" Header="Width (px)" Minimum="40" Maximum="600" SpinButtonPlacementMode="Compact" ValueChanged="OnNumberChanged" />
                        <NumberBox x:Name="GraphHeightBox" Tag="graphHeight" Grid.Column="1" Header="Height (px)" Minimum="16" Maximum="200" SpinButtonPlacementMode="Compact" ValueChanged="OnNumberChanged" />
                    </Grid>
                </StackPanel>

                <ComboBox x:Name="RefreshBox" Header="Refresh rate" HorizontalAlignment="Stretch" SelectionChanged="OnRefreshChanged">
                    <ComboBoxItem Content="1 Hz" />
                    <ComboBoxItem Content="2 Hz" />
                    <ComboBoxItem Content="4 Hz" />
                    <ComboBoxItem Content="10 Hz" />
                    <ComboBoxItem Content="30 Hz" />
                </ComboBox>
                <TextBlock
                    Style="{StaticResource CaptionTextBlockStyle}"
                    Opacity="0.7"
                    TextWrapping="Wrap"
                    Text="With the frametime graph on, the overlay redraws at up to 30 Hz; otherwise it only redraws when a displayed value changes." />
            </StackPanel>

            <StackPanel Grid.Column="1" Spacing="8">
                <TextBlock Text="Preview" Style="{StaticResource BodyStrongTextBlockStyle}" />
                <Border Background="#FF20242A" CornerRadius="8" Padding="24" MinHeight="200">
                    <Image x:Name="PreviewImage" Stretch="None" HorizontalAlignment="Left" VerticalAlignment="Top" />
                </Border>
                <TextBlock
                    Style="{StaticResource CaptionTextBlockStyle}"
                    Opacity="0.7"
                    Text="The preview uses sample values and the same renderer as the overlay." />
            </StackPanel>
        </Grid>
    </ScrollViewer>
</Page>
```

- [ ] **Step 4: Code-behind**

`src/Supermonitor/Views/OverlayPage.xaml.cs`:

```csharp
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Supermonitor.Core;
using Supermonitor.Core.Frames;
using Supermonitor.Core.Overlay;
using Supermonitor.Core.Settings;
using Supermonitor.Dialogs;
using Supermonitor.Overlay;
using Supermonitor.ViewModels;
using Windows.UI;

namespace Supermonitor.Views;

public sealed partial class OverlayPage : Page
{
    private const string CustomPreset = "Custom";

    private static readonly string[] Fonts = ["Segoe UI", "Segoe UI Variable Display", "Bahnschrift", "Calibri", "Consolas", "Cascadia Mono", "Arial", "Verdana"];

    private readonly DemoOverlayValues _demo = new();
    private readonly DispatcherQueueTimer _statusTimer;
    private AppHost? _host;
    private OverlayService? _overlay;
    private OverlayRenderer? _renderer;
    private WriteableBitmap? _bitmap;
    private IReadOnlyList<MonitorInfo> _monitors = [];
    private bool _loading;

    public OverlayPage()
    {
        InitializeComponent();
        _statusTimer = DispatcherQueue.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromSeconds(1);
        _statusTimer.Tick += (_, _) => UpdateCaptureStatus();
        Unloaded += OnUnloaded;
    }

    private OverlaySettings Overlay => _host!.Settings.Current.Overlay;

    private OverlayStyle Look => Overlay.Style;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _host = ((PageContext)e.Parameter).Host;
        _overlay = App.Current.Overlay;
        _renderer = new OverlayRenderer();
        if (_overlay is not null)
        {
            _overlay.Changed += OnOverlayChanged;
        }

        _monitors = MonitorCatalog.Enumerate();
        Reload();
        _statusTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _statusTimer.Stop();
        if (_overlay is not null)
        {
            _overlay.Changed -= OnOverlayChanged;
        }

        _renderer?.Dispose();
        _renderer = null;
    }

    private void OnOverlayChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        _loading = true;
        EnabledSwitch.IsOn = Overlay.Enabled;
        _loading = false;
        UpdateCaptureStatus();
    });

    // Copies the settings into the controls.
    private void Reload()
    {
        _loading = true;
        EnabledSwitch.IsOn = Overlay.Enabled;
        FillMonitors();
        AnchorBox.SelectedIndex = (int)Look.Anchor;
        LayoutBox.SelectedIndex = (int)Look.Layout;
        OffsetXBox.Value = Look.OffsetX;
        OffsetYBox.Value = Look.OffsetY;
        FillFonts();
        FontSizeBox.Value = Look.FontSize;
        SetColor(LabelColorPicker, LabelSwatch, Look.LabelColor);
        SetColor(ValueColorPicker, ValueSwatch, Look.ValueColor);
        SetColor(BackgroundColorPicker, BackgroundSwatch, Look.BackgroundColor);
        OpacitySlider.Value = Math.Round(Look.BackgroundOpacity * 100);
        RadiusBox.Value = Look.CornerRadius;
        PaddingBox.Value = Look.Padding;
        GraphSwitch.IsOn = Look.ShowFrameGraph;
        GraphWidthBox.Value = Look.GraphWidth;
        GraphHeightBox.Value = Look.GraphHeight;
        GraphWidthBox.IsEnabled = GraphHeightBox.IsEnabled = Look.ShowFrameGraph;
        RefreshBox.SelectedIndex = Math.Max(0, OverlayStyle.AllowedRefreshHz.ToList().IndexOf(Look.RefreshHz));
        FillPresets();
        RebuildItems();
        _loading = false;
        UpdateCaptureStatus();
        RefreshPreview();
    }

    // Saves, applies to the real overlay and updates the preview and preset selection.
    private void Commit()
    {
        _host!.Settings.RequestSave();
        _overlay?.Apply();
        var wasLoading = _loading;
        _loading = true;
        FillPresets();
        _loading = wasLoading;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_renderer is null || _host is null)
        {
            return;
        }

        var settings = _host.Settings.Current;
        var scale = (float)(XamlRoot?.RasterizationScale ?? 1.0);
        var frame = OverlayFrameBuilder.Build(Look, _demo, settings.ToUnitOptions(), settings.Thresholds);
        var bitmap = _renderer.Render(frame, Look, scale);

        if (_bitmap is null || _bitmap.PixelWidth != bitmap.Width || _bitmap.PixelHeight != bitmap.Height)
        {
            _bitmap = new WriteableBitmap(bitmap.Width, bitmap.Height);
        }

        using (var stream = _bitmap.PixelBuffer.AsStream())
        {
            stream.Position = 0;
            stream.Write(bitmap.Pixels);
        }

        _bitmap.Invalidate();
        PreviewImage.Source = _bitmap;
        PreviewImage.Width = bitmap.Width / scale;
        PreviewImage.Height = bitmap.Height / scale;
    }

    private void UpdateCaptureStatus()
    {
        var frames = _host?.Frames;
        var failed = Overlay.Enabled && frames is { State: FrameCaptureState.Failed };
        CaptureBar.IsOpen = failed;
        if (failed)
        {
            CaptureBar.Message = $"{frames!.FailureReason} FPS shows N/A and Supermonitor retries every 30 seconds.";
        }
    }

    private void OnEnabledToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || _overlay is null)
        {
            return;
        }

        _loading = true;
        _overlay.SetEnabled(EnabledSwitch.IsOn);
        _loading = false;
        UpdateCaptureStatus();
    }

    // ----- presets -----

    private void FillPresets()
    {
        var names = OverlayEditor.AllPresets(Overlay).Select(p => p.Name).ToList();
        var matching = OverlayEditor.MatchingPresetName(Overlay);
        if (matching is null)
        {
            names.Insert(0, CustomPreset);
        }

        PresetBox.ItemsSource = names;
        PresetBox.SelectedItem = matching ?? CustomPreset;
        DeletePresetButton.IsEnabled = matching is not null && Overlay.UserPresets.Any(p => p.Name == matching);
    }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || PresetBox.SelectedItem is not string name || name == CustomPreset)
        {
            return;
        }

        var preset = OverlayEditor.AllPresets(Overlay).FirstOrDefault(p => p.Name == name);
        if (preset is null)
        {
            return;
        }

        OverlayEditor.ApplyPreset(Overlay, preset);
        _host!.Settings.RequestSave();
        _overlay?.Apply();
        Reload();
    }

    private async void OnSavePresetClick(object sender, RoutedEventArgs e)
    {
        var name = new TextBox { Header = "Preset name", MaxLength = 40 };
        var error = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
        };
        var dialog = new ContentDialog
        {
            Title = "Save preset",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            Content = new StackPanel { Spacing = 8, Children = { name, error } },
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!OverlayEditor.SavePreset(Overlay, name.Text))
            {
                args.Cancel = true;
                error.Text = "Choose a name that is not empty and is not Minimal, Default or Full.";
                error.Visibility = Visibility.Visible;
            }
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Commit();
        }
    }

    private void OnDeletePresetClick(object sender, RoutedEventArgs e)
    {
        if (PresetBox.SelectedItem is string name && OverlayEditor.DeletePreset(Overlay, name))
        {
            Commit();
        }
    }

    // ----- position -----

    private void FillMonitors()
    {
        MonitorBox.Items.Clear();
        MonitorBox.Items.Add(new ComboBoxItem { Content = "Primary display", Tag = null });
        var selected = 0;
        foreach (var monitor in _monitors)
        {
            MonitorBox.Items.Add(new ComboBoxItem { Content = monitor.Name, Tag = monitor.Id });
            if (monitor.Id == Overlay.MonitorId)
            {
                selected = MonitorBox.Items.Count - 1;
            }
        }

        MonitorBox.SelectedIndex = selected;
    }

    private void OnMonitorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MonitorBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        Overlay.MonitorId = item.Tag as string;
        Commit();
    }

    private void OnAnchorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AnchorBox.SelectedIndex < 0)
        {
            return;
        }

        Look.Anchor = (OverlayAnchor)AnchorBox.SelectedIndex;
        Commit();
    }

    private void OnLayoutChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LayoutBox.SelectedIndex < 0)
        {
            return;
        }

        Look.Layout = (OverlayLayout)LayoutBox.SelectedIndex;
        Commit();
    }

    private void OnNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(sender.Value))
        {
            return;
        }

        var whole = (int)Math.Round(sender.Value);
        switch (sender.Tag as string)
        {
            case "offsetX":
                Look.OffsetX = whole;
                break;
            case "offsetY":
                Look.OffsetY = whole;
                break;
            case "fontSize":
                Look.FontSize = (float)sender.Value;
                break;
            case "radius":
                Look.CornerRadius = whole;
                break;
            case "padding":
                Look.Padding = whole;
                break;
            case "graphWidth":
                Look.GraphWidth = whole;
                break;
            case "graphHeight":
                Look.GraphHeight = whole;
                break;
            default:
                return;
        }

        Commit();
    }

    // ----- metrics -----

    private void RebuildItems()
    {
        ItemsPanel.Children.Clear();
        foreach (var item in Look.Items)
        {
            var row = new Grid { ColumnSpacing = 4 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < 4; i++)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }

            row.Children.Add(new TextBlock { Text = OverlayMetricCatalog.Label(item.Metric), VerticalAlignment = VerticalAlignment.Center });
            AddRowButton(row, 1, "", "Move up", item.Metric, OnItemUp);
            AddRowButton(row, 2, "", "Move down", item.Metric, OnItemDown);
            AddRowButton(row, 3, "", "Colors and thresholds", item.Metric, OnItemEdit);
            AddRowButton(row, 4, "", "Remove", item.Metric, OnItemRemove);
            ItemsPanel.Children.Add(row);
        }
    }

    private static void AddRowButton(Grid row, int column, string glyph, string tooltip, OverlayMetric metric, RoutedEventHandler handler)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 12 }, Tag = metric, Padding = new Thickness(8, 4, 8, 4) };
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += handler;
        Grid.SetColumn(button, column);
        row.Children.Add(button);
    }

    private void OnItemUp(object sender, RoutedEventArgs e) => MoveItem(sender, -1);

    private void OnItemDown(object sender, RoutedEventArgs e) => MoveItem(sender, 1);

    private void MoveItem(object sender, int delta)
    {
        if (sender is FrameworkElement { Tag: OverlayMetric metric })
        {
            OverlayEditor.MoveItem(Look, metric, delta);
            RebuildItems();
            Commit();
        }
    }

    private void OnItemRemove(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OverlayMetric metric } && OverlayEditor.RemoveItem(Look, metric))
        {
            RebuildItems();
            Commit();
        }
    }

    private async void OnItemEdit(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: OverlayMetric metric })
        {
            return;
        }

        var index = Look.Items.FindIndex(i => i.Metric == metric);
        if (index < 0)
        {
            return;
        }

        var dialog = new OverlayItemDialog(Look.Items[index]) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Look.Items[index] = dialog.Result;
            Commit();
        }
    }

    private void OnAddItemMenuOpening(object sender, object e)
    {
        AddItemFlyout.Items.Clear();
        var used = Look.Items.Select(i => i.Metric).ToHashSet();
        foreach (var metric in Enum.GetValues<OverlayMetric>().Where(m => !used.Contains(m)))
        {
            var entry = new MenuFlyoutItem { Text = OverlayMetricCatalog.Label(metric), Tag = metric };
            entry.Click += OnAddItem;
            AddItemFlyout.Items.Add(entry);
        }

        if (AddItemFlyout.Items.Count == 0)
        {
            AddItemFlyout.Items.Add(new MenuFlyoutItem { Text = "All metrics are shown", IsEnabled = false });
        }
    }

    private void OnAddItem(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: OverlayMetric metric } && OverlayEditor.AddItem(Look, metric))
        {
            RebuildItems();
            Commit();
        }
    }

    // ----- text, colors and background -----

    private void FillFonts()
    {
        FontBox.ItemsSource = Fonts;
        FontBox.SelectedItem = Fonts.FirstOrDefault(f => string.Equals(f, Look.FontFamily, StringComparison.OrdinalIgnoreCase));
        FontBox.Text = Look.FontFamily;
    }

    private void OnFontChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || FontBox.SelectedItem is not string family)
        {
            return;
        }

        Look.FontFamily = family;
        Commit();
    }

    private void OnFontSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        args.Handled = true;
        if (_loading || string.IsNullOrWhiteSpace(args.Text))
        {
            return;
        }

        Look.FontFamily = args.Text.Trim();
        Commit();
    }

    private static void SetColor(ColorPicker picker, Border swatch, string hex)
    {
        var color = ToColor(Rgba.Parse(hex, new Rgba(0xFF, 0xFF, 0xFF)));
        picker.Color = color;
        swatch.Background = new SolidColorBrush(color);
    }

    private static Color ToColor(Rgba rgba) => Color.FromArgb(0xFF, rgba.R, rgba.G, rgba.B);

    private void OnColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_loading)
        {
            return;
        }

        var hex = ColorText.ToHex(args.NewColor.R, args.NewColor.G, args.NewColor.B);
        switch (sender.Tag as string)
        {
            case "label":
                Look.LabelColor = hex;
                LabelSwatch.Background = new SolidColorBrush(args.NewColor);
                break;
            case "value":
                Look.ValueColor = hex;
                ValueSwatch.Background = new SolidColorBrush(args.NewColor);
                break;
            case "background":
                Look.BackgroundColor = hex;
                BackgroundSwatch.Background = new SolidColorBrush(args.NewColor);
                break;
            default:
                return;
        }

        Commit();
    }

    private void OnOpacityChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        Look.BackgroundOpacity = (float)(e.NewValue / 100d);
        Commit();
    }

    private void OnGraphToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        Look.ShowFrameGraph = GraphSwitch.IsOn;
        GraphWidthBox.IsEnabled = GraphHeightBox.IsEnabled = GraphSwitch.IsOn;
        Commit();
    }

    private void OnRefreshChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || RefreshBox.SelectedIndex < 0)
        {
            return;
        }

        Look.RefreshHz = OverlayStyle.AllowedRefreshHz[RefreshBox.SelectedIndex];
        Commit();
    }
}
```

Notes for the implementer:
- The page's private property is called `Look` (and not `Style`) so it does not hide `FrameworkElement.Style`. `Overlay` is also the name of a namespace (`Supermonitor.Overlay`), but inside the class the simple name resolves to the property; if the compiler complains about ambiguity in `Overlay.Enabled`, rename the property to `Config`.
- `ColorText.ToHex` exists in `Supermonitor.Core.Settings`. `Windows.UI.Color` needs `using Windows.UI;` (already included).
- `Application.Current.Resources["SystemFillColorCriticalBrush"]` is the same resource used in `MetricCard.xaml.cs`.
- `PixelBuffer.AsStream()` comes from `System.Runtime.InteropServices.WindowsRuntime` (already imported). If the extension does not resolve, use `Marshal`/`IBufferByteAccess`: get the pointer with `((IBufferByteAccess)(object)_bitmap.PixelBuffer).Buffer` and copy with `Marshal.Copy`.

- [ ] **Step 5: Build**

Run: `dotnet build src/Supermonitor/Supermonitor.csproj -c Debug -p:Platform=x64 -o <temporary folder> 2>&1 | Select-String "error|Erro"`
Expected: no errors (`0 Error(s)`). Fix XAML/C# errors before moving on.

- [ ] **Step 6: Manual verification (user)**

The user exits the app through the tray, the assistant rebuilds and opens the app. In **Overlay**:

1. The preview shows the Default preset with sample values (144 FPS, 6.9 ms, …) and a mini-chart. Switching the preset to Minimal and Full changes the preview and the real overlay (if on) immediately.
2. Each control updates the preview: anchor, offsets, layout (Horizontal/Vertical/Compact), font (pick from the list and type `Consolas` + Enter), size, label/value/background color (picker), opacity, radius, padding, chart (on/off, width, height), rate.
3. Items: move up/down/remove change the order; "Add metric" lists only what is missing; the gear icon opens the dialog and a `#FF0000` color on the value paints the value red in the preview; threshold 1 / 2 for CPU temp paints the value orange/red (the example's 62 °C exceeds both).
4. Any change switches the preset selector to "Custom". "Save as…" with the name `Mine` creates the preset and selects it; empty names or `Default` show the error in the dialog; "Delete" removes `Mine`.
5. Turning on the overlay with the page switch is reflected in the tray (check mark on "Show overlay") and vice versa when the page is reopened.
6. Changing the monitor, anchor or size with the overlay on moves/resizes the real window in real time.
7. Restart the app: the style, user presets and on/off state come back as they were.

- [ ] **Step 7: Commit**

```powershell
git add src
git commit -m "feat(app): overlay editor page with live preview, item editor and user presets"
```

---

### Task 10: Performance check, real FPS and wrap-up

**Files:**
- Create: `docs/superpowers/plans/2026-10-10-supermonitor-03-results.md`

**Interfaces:**
- Consumes: complete app.
- Produces: record of spec criteria 1–4 (section 1) with the overlay active, test results and deviations from the plan.

- [ ] **Step 1: Testes**

Run: `dotnet test --project tests/Supermonitor.Tests`
Expected: all pass; the ETW tests show as skipped in this non-elevated shell.

Ask the user to run, in a PowerShell **as administrator**, `dotnet test --project tests/Supermonitor.Tests --filter-class "Supermonitor.Tests.Hardware.FrameCaptureSmokeTests"` and report the output. Both ETW tests must pass and print `present events received in 3 s: N` with N > 0.

- [ ] **Step 2: Real FPS check (spec criterion 4)**

With the Debug or Release build open as administrator and the overlay on:

1. Open a game in windowed or borderless mode (DX11 or DX12). FPS, frametime and 1% low appear within ~2 s and the mini-chart follows.
2. Compare with an independent reference (the game's own FPS counter, Steam's or Xbox Game Bar > Performance). Criterion: difference of up to 2 FPS or 3%, whichever is greater.
3. Alt-tab to the desktop: FPS goes back to `N/A` in ~2 s; on returning to the game, the numbers reappear.
4. Move the game between monitors and change the overlay's monitor on the Overlay page: the overlay follows the chosen monitor.
5. Vulkan or OpenGL game, if one is at hand: same behavior (the DxgKrnl event covers all APIs).

If a DX11/DX12 game shows `N/A` or double the real FPS: **stop** and apply superpowers:systematic-debugging. The first hypothesis is that DxgKrnl event 184 does not represent one present per frame for that game; the fallback foreseen in the Global Constraints is to also subscribe to `Microsoft-Windows-DXGI` (GUID `ca11c036-0102-4a2d-a6ad-f03cfed5d3c9`, keyword `0x1`, event `42` = `Present_Start`) and, per process, use DXGI when it appears. Record the decision in the results.

- [ ] **Step 3: Crash recovery (item 6 of the spec's manual verification, ETW part)**

1. With the overlay on and FPS at `N/A` or with values, force-kill the app: `taskkill /f /im Supermonitor.exe` (PowerShell as administrator).
2. `logman query -ets` should still list `Supermonitor-FPS` (orphan session).
3. Open the app again: the overlay comes back on, FPS works and `logman query -ets` lists **one** `Supermonitor-FPS` session.
4. Exit through the "Exit" menu: the session disappears.

- [ ] **Step 4: Measure on the Release build**

1. `dotnet build src/Supermonitor/Supermonitor.csproj -c Release -p:Platform=x64`; the user exits the app through the tray and opens the exe from `bin\x64\Release\…\win-x64\` as administrator.
2. Overlay on (Default preset) and Overview open, 1 s interval, 60 s: `pwsh -NoProfile tools/measure-cpu.ps1`. Criterion: < 1%.
3. Main window closed (tray + overlay only), overlay on with the Default preset (FPS and mini-chart at 30 Hz), an animating window in the foreground, 60 s: `pwsh -NoProfile tools/measure-cpu.ps1`. Criterion: < 0.3%.
4. Same scenario with the Minimal preset (4 Hz, no chart), 60 s. Record (reference).
5. Overlay off and window closed, 60 s. Criterion: < 0.3% (proves the leases and the ETW session were released: `logman query -ets` does not list `Supermonitor-FPS`).

If a criterion fails, apply superpowers:systematic-debugging. Suspects, in order: (1) ETW event volume (measured on 2026-10-10: ~6000 events/s and ~31 ms of CPU in 8 s with only the `Present` keyword); (2) `FrameCaptureService.Current` called three times per overlay tick, each sorting up to ~2400 frametimes in `FrameTimeAggregator.GetStats` (cache the `FrameStats` by `FrameVersion` and by 50 ms); (3) redraw at 30 Hz with the mini-chart, recreating DirectWrite text layouts every frame (cache layouts by text); (4) `MonitorCatalog.Enumerate` once per second; (5) EcoQoS: the process enters efficiency mode with no window, which can delay the overlay thread and the ETW thread (per-thread opt-out with `SetThreadInformation(ThreadPowerThrottling)` on the `Overlay` thread).

- [ ] **Step 5: Record results**

`docs/superpowers/plans/2026-10-10-supermonitor-03-results.md`: date, build, CPU and memory in the Step 4 scenarios, test count, real FPS check (game, reference, difference), crash recovery, manual checks of Tasks 8 and 9 and deviations from the plan with the decisions taken. Already-known deviations to record:
- Capture uses only the DxgKrnl `Present` event (spec 3.4 also mentions DXGI); reason and evidence in the Global Constraints.
- No FPS metrics in the `MetricStore`: FPS, frametime and 1% low are read directly from `FrameCaptureService`, so they do not appear as dashboard cards.
- The overlay's "limiter state" item (spec 4.4) is left for Plan 4, when `IFrameLimiter` exists; the `OverlayMetric` catalog gains the member there.
- The FPS target is the foreground process (any app). Restriction to detected games comes with Plan 4's `GameDetector`.

- [ ] **Step 6: Commit**

```powershell
git add docs/superpowers/plans/2026-10-10-supermonitor-03-results.md
git commit -m "docs: record FPS capture and overlay plan results"
```

---

## Self-Review

**Spec coverage (3.4, 3.5, 4.4):**
- 3.4 ETW session named `Supermonitor-FPS`, orphan stopped on startup → Task 5 (`EtwPresentSource`, orphan session test). Target = foreground process, frametime = interval between presents → Tasks 4 and 1. `FrameTimeAggregator` with the window's average FPS, current frametime and frametime buffer for the mini-chart and the benchmark → Task 1 (`CopyFrameTimes`, reusable `Percentile`). DXGI as a complement → recorded deviation (Global Constraints, Task 10).
- 3.5 styles `WS_EX_LAYERED | TRANSPARENT | TOPMOST | NOACTIVATE | TOOLWINDOW` → Task 7 (`OverlayWindow`). Direct2D into a premultiplied 32-bit DIB + `UpdateLayeredWindow`, grayscale DirectWrite → Tasks 6/7. Redraw only when a value changes, 4 Hz default, chart up to 30 Hz → Tasks 3 (`SameContentAs`, `OverlayRefresh`) and 7 (`Tick`).
- 4.4 monitor and 9 anchors + offset → Tasks 3 (`OverlayPlacement`) and 7 (`MonitorCatalog`) and 9; metrics and order → Tasks 2/3/9 (limiter: Plan 4); horizontal/vertical/compact layout → Task 6; font, size, per-metric colors, thresholds → Tasks 2/3/9; background (color, opacity, radius, padding) → Tasks 2/6/9; mini-chart on/off, width, height → Tasks 2/6/9; update rate → Tasks 2/9; Minimal/Default/Full and user presets → Task 2; live preview with the same renderer → Task 9.
- Success criteria 2 (tray + overlay only < 0.3%) and 4 (real FPS) → Task 10.
- Spec 7 error states: failure creating ETW session → FPS "N/A", reason in the UI, retry every 30 s → Task 4 (`Failed`/`RetryInterval`), Task 9 (`CaptureBar`).

**Placeholders:** no "TBD"/"TODO"; the conditional passages (Vortice signatures, `AsStream`) carry the concrete alternative.

**Type consistency:** `FrameStats`/`FrameTimeAggregator` (Task 1) → `FrameCaptureService.Current/CopyFrameTimes/FrameVersion` (Task 4) → `LiveOverlayValues` (Task 7); `OverlayStyle/OverlayItemSettings` (Task 2) → `OverlayFrameBuilder` (Task 3) → `OverlayRenderer` (Task 6) → `OverlayService` (Task 7) and `OverlayPage` (Task 9); `Demand.FrameCapture` (Task 3) → `DemandTracker.Changed` (Task 4) → `OverlayService` (Task 7); `PixelRect` (Task 3) → `MonitorInfo.Bounds` (Task 7); `OverlayService.Changed/SetEnabled/Apply/Enabled` (Task 7) → `App` (Task 8) → `OverlayPage` (Task 9).

**Review Focus:** (1) Task 4; (2) Tasks 4 and 5; (3) Task 1; (4) Task 2; (5) Task 3 (`OverlayPlacement`) and Task 7 (`MonitorCatalog.Resolve`).
