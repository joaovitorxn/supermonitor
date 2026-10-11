# Supermonitor Plano 3 — Captura de FPS e overlay: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Capturar FPS e frametime reais do processo em primeiro plano por ETW e exibi-los (junto com CPU, GPU, RAM, rede, ping e relógio) em um overlay click-through desenhado com Direct2D, com editor de aparência, preview ao vivo e presets.

**Architecture:** `EtwPresentSource` (TraceEvent) emite um evento por present do provedor `Microsoft-Windows-DxgKrnl`. `FrameCaptureService` liga a sessão só enquanto existe uma lease `Demand.FrameCapture`, escolhe o processo em primeiro plano como alvo e alimenta um `FrameTimeAggregator` (lógica pura). O overlay é uma janela Win32 layered em thread própria (`Supermonitor.Overlay`): `OverlayFrameBuilder` (Core, puro) transforma o estilo e os valores em linhas de texto; `OverlayRenderer` desenha essas linhas com Direct2D num DIB premultiplicado; `UpdateLayeredWindow` apresenta o DIB. O mesmo renderizador gera o preview do editor. Configurações passam para o schema v3 (`Overlay` com estilo, itens, presets do usuário).

**Tech Stack:** igual aos Planos 1–2 + `Microsoft.Diagnostics.Tracing.TraceEvent` 3.2.8 (Core) e `Vortice.Direct2D1` 3.8.3 (Overlay; traz DirectWrite e DXGI).

**Spec:** `docs/superpowers/specs/2026-10-10-supermonitor-design.md` (seções 3.4, 3.5, 4.4)
**Índice:** `docs/superpowers/plans/2026-10-10-supermonitor-plan-index.md`
**Base:** `main` em `63c564f` (Plano 2 concluído; resultados em `docs/superpowers/plans/2026-10-10-supermonitor-02-results.md`).

## Global Constraints

- Tudo dos Planos 1–2 continua valendo: Windows 11, x64, `net10.0-windows10.0.22621.0`, unpackaged, `requireAdministrator`, UI em EN-US, `N/A` para valor ausente, `Supermonitor.Core` sem dependência de WinUI, comentários e identificadores como no código existente (comentários em português, nomes e textos de UI em inglês).
- Testes de unidade: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`. Testes de hardware: `dotnet test --project tests/Supermonitor.Tests --filter-trait "Category=Hardware"`. Suíte completa: `dotnet test --project tests/Supermonitor.Tests`.
- Build do app: `dotnet build src/Supermonitor/Supermonitor.csproj -c Debug -p:Platform=x64`. Com o app aberto o exe fica bloqueado; para checar compilação use `-o <pasta temporária>`.
- O app elevado não pode ser encerrado nem clicado por um shell sem elevação, e este shell não é elevado. Verificações visuais e testes que exigem administrador (ETW) ficam com o usuário (sair pela bandeja, aprovar UAC). Testes ETW usam `Assert.Skip` quando o processo não é administrador.
- Critérios de desempenho continuam: dashboard aberto < 1% CPU, só bandeja **com overlay visível e FPS ligado** < 0,3% CPU (build Release, intervalo 1 s).
- Fonte do FPS: evento `Present` (id 184, versão 1) do provedor `Microsoft-Windows-DxgKrnl` (GUID `802ec45a-1e99-4b83-9920-87c98277ba9d`), palavra-chave `Present` (`0x8000000`). Medido nesta máquina em 2026-10-10: o `ProcessID` do cabeçalho é o do app que apresenta (ex.: o processo do Claude a ~240 eventos/s), e o provedor inteiro com essa palavra-chave gera ~6000 eventos/s no sistema e custa ~31 ms de CPU em 8 s. O evento `Microsoft-Windows-DXGI` Present_Start (id 42) existe, mas não chegou para o processo testado (Chromium usa DirectComposition); por isso o plano usa só o DxgKrnl e **não** usa o DXGI (desvio da spec 3.4, registrado nos resultados). Se um jogo DX11/DX12 não mostrar FPS na verificação manual da Task 10, o DXGI vira o fallback.
- Vortice 3.8.3, API conferida por spike em 2026-10-10: `D2D1.D2D1CreateFactory<ID2D1Factory1>()`, `DWrite.DWriteCreateFactory<IDWriteFactory>()`, `factory.CreateDCRenderTarget(new RenderTargetProperties(RenderTargetType.Default, new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied), 0, 0, RenderTargetUsage.GdiCompatible, FeatureLevel.Default))`, `target.BindDC(hdc, new RectI(0, 0, w, h))`, `new RoundedRectangle(new System.Drawing.RectangleF(...), rx, ry)`, `dwrite.CreateTextFormat(family, FontWeight.SemiBold, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, size)`, `dwrite.CreateTextLayout(text, format, maxWidth, maxHeight)`, `target.DrawTextLayout(Vector2, layout, brush)`. `AlphaMode` precisa do alias `using AlphaMode = Vortice.DCommon.AlphaMode;`. O resultado num DIB top-down tem canto transparente (alpha 0) e fundo a 60% com alpha 153, ou seja, premultiplicado correto para `UpdateLayeredWindow`.
- Todo commit termina com `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

## Review Focus

1. Alvo some ou troca (alt-tab, jogo fecha, janela do próprio Supermonitor em primeiro plano) → overlay mostra `N/A` em vez de congelar no último FPS, e a agregação reinicia sem misturar jogos. Testes: Task 4 (`FrameCaptureService`: troca de alvo reinicia, alvo = processo próprio vira nenhum, ocioso > 2 s vira `N/A`).
2. Sessão ETW falha (sem administrador, sessão órfã de um crash) → FPS `N/A` com motivo, nova tentativa a cada 30 s, app sem exceção. Testes: Task 4 (falha, retry com `FakeTimeProvider`) e Task 5 (hardware: sessão órfã é substituída).
3. Pausa longa entre presents (loading, jogo minimizado, alt-tab) → um frametime de minutos não pode virar 1% low de 0 FPS. Teste: Task 1 (`MaxGapMs` reinicia a sequência).
4. Configurações v2 do Plano 2 e arquivo com valores inválidos de overlay (fonte vazia, opacidade 7, enum desconhecido, cor "blue", itens duplicados) → carrega normalizado, sem perder o resto. Testes: Task 2.
5. Overlay em monitor que sumiu, DPI diferente ou posição fora da tela → janela sempre dentro do monitor escolhido (volta ao primário se o monitor não existe). Testes: Task 3 (`OverlayPlacement` limita à tela) e Task 6 (resolução de monitor).

---

## File Structure

```
src/Supermonitor.Core/
  Supermonitor.Core.csproj                  + Microsoft.Diagnostics.Tracing.TraceEvent
  AppHost.cs                                + Frames (FrameCaptureService)
  Frames/Percentile.cs                      percentil nearest-rank (reaproveitado pelo benchmark do Plano 4)
  Frames/FrameTimeAggregator.cs             FrameStats, FrameTimeAggregator
  Frames/FrameCaptureService.cs             IPresentSource, IForegroundProbe, FrameCaptureService
  Frames/EtwPresentSource.cs                sessão ETW Supermonitor-FPS
  Frames/ForegroundProbe.cs                 GetForegroundWindow → PID
  Interop/User32.cs                         + GetForegroundWindow, GetWindowThreadProcessId
  Overlay/OverlaySettings.cs                enums, OverlayItemSettings, OverlayStyle, OverlayPreset, OverlaySettings
  Overlay/OverlayPresets.cs                 Minimal, Default, Full
  Overlay/OverlayEditor.cs                  itens, presets do usuário
  Overlay/Rgba.cs
  Overlay/OverlayMetricCatalog.cs           rótulo, unidade, id na MetricStore, demanda
  Overlay/IOverlayValues.cs                 IOverlayValues, DemoOverlayValues
  Overlay/OverlayFrame.cs                   OverlayRow, OverlayFrame
  Overlay/OverlayFrameBuilder.cs            estilo + valores → linhas
  Overlay/OverlayPlacement.cs               PixelRect, âncoras
  Overlay/OverlayRefresh.cs                 taxa efetiva
  Sensors/DemandTracker.cs                  + Demand.FrameCapture
  Settings/AppSettings.cs                   + Overlay
  Settings/SettingsMigrator.cs              v2 → v3
  Settings/SettingsJsonContext.cs           + OverlayStyle
  Shell/TrayIcon.cs                         + item "Show overlay"
src/Supermonitor.Overlay/
  Supermonitor.Overlay.csproj               Vortice.Direct2D1
  Interop/Gdi32.cs, Interop/User32.cs, Interop/Shcore.cs
  OverlayRenderer.cs                        Direct2D → DIB
  OverlayWindow.cs                          janela layered + thread + timer
  OverlayService.cs                         leases, valores ao vivo, monitores
  LiveOverlayValues.cs                      IOverlayValues sobre MetricStore + FrameCaptureService
  MonitorCatalog.cs                         MonitorInfo, enumeração, DPI
src/Supermonitor/
  Supermonitor.csproj                       + ProjectReference Overlay
  App.xaml.cs                               OverlayService, item de bandeja
  MainWindow.xaml(.cs)                      item de navegação Overlay
  Views/OverlayPage.xaml(.cs)               editor + preview
  Dialogs/OverlayItemDialog.cs              cores e limiares por item
tests/Supermonitor.Tests/                   um arquivo por unidade, listado em cada task; referencia Supermonitor.Overlay
```

---

### Task 1: FrameTimeAggregator e percentil

**Files:**
- Create: `src/Supermonitor.Core/Frames/Percentile.cs`, `src/Supermonitor.Core/Frames/FrameTimeAggregator.cs`
- Test: `tests/Supermonitor.Tests/Frames/PercentileTests.cs`, `tests/Supermonitor.Tests/Frames/FrameTimeAggregatorTests.cs`

**Interfaces:**
- Consumes: `Supermonitor.Core.Collections.RingBuffer<T>`.
- Produces:
  - `static class Percentile { static float NearestRank(Span<float> values, double percent) }` — ordena `values` no lugar; `NaN` se vazio.
  - `readonly record struct FrameStats(bool HasData, double Fps, double FrameTimeMs, double Low1PercentFps, long FrameCount)` com `static FrameStats None`.
  - `sealed class FrameTimeAggregator` com `const double FpsWindowMs = 1000`, `LowWindowMs = 10_000`, `MaxGapMs = 5000`, `const int Capacity = 16384`; `void AddPresent(double timestampMs)`, `FrameStats GetStats()`, `int CopyFrameTimes(Span<float> destination)`, `long Version { get; }`, `void Reset()`.

- [ ] **Step 1: Escrever os testes do percentil**

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
        // 99,9 * 1000 / 100 em ponto flutuante passa de 999 por erro de arredondamento; o resultado correto é o posto 999.
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

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-class "Supermonitor.Tests.Frames.PercentileTests"`
Expected: erro de compilação (`Percentile` não existe).

- [ ] **Step 3: Implementar `Percentile`**

`src/Supermonitor.Core/Frames/Percentile.cs`:

```csharp
namespace Supermonitor.Core.Frames;

public static class Percentile
{
    // Nearest-rank: o menor valor com pelo menos `percent`% dos dados menores ou iguais a ele. Ordena `values` no lugar.
    public static float NearestRank(Span<float> values, double percent)
    {
        if (values.IsEmpty)
        {
            return float.NaN;
        }

        values.Sort();
        // O epsilon evita que 99,9 * 1000 / 100 = 999,0000000000001 vire o posto 1000.
        var rank = (int)Math.Ceiling(percent * values.Length / 100d - 1e-9);
        return values[Math.Clamp(rank, 1, values.Length) - 1];
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-class "Supermonitor.Tests.Frames.PercentileTests"`
Expected: 7 passam.

- [ ] **Step 5: Escrever os testes do agregador**

`tests/Supermonitor.Tests/Frames/FrameTimeAggregatorTests.cs`:

```csharp
using Supermonitor.Core.Frames;

namespace Supermonitor.Tests.Frames;

public class FrameTimeAggregatorTests
{
    // Primeiro present não gera frametime (não há anterior); cada intervalo vira um frametime.
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
        // 100 frames: 96 de 10 ms e 4 de 40 ms → posto 99 = 40 ms → 25 FPS.
        var frameTimes = Enumerable.Repeat(10.0, 100).ToArray();
        frameTimes[20] = frameTimes[40] = frameTimes[60] = frameTimes[80] = 40;

        var stats = Feed(frameTimes).GetStats();

        Assert.Equal(25, stats.Low1PercentFps, 1e-6);
        Assert.Equal(10, stats.FrameTimeMs, 1e-6);
    }

    [Fact]
    public void Fps_uses_only_the_last_second()
    {
        // 5 s a 50 FPS (20 ms) seguidos de 1,5 s a 100 FPS (10 ms).
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
        // 4 frames: o posto 99 é o maior (800 ms) → 1,25 FPS de 1% low; o frametime atual segue sendo o último.
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

- [ ] **Step 6: Rodar e ver falhar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-class "Supermonitor.Tests.Frames.FrameTimeAggregatorTests"`
Expected: erro de compilação (`FrameTimeAggregator` não existe).

- [ ] **Step 7: Implementar o agregador**

`src/Supermonitor.Core/Frames/FrameTimeAggregator.cs`:

```csharp
using Supermonitor.Core.Collections;

namespace Supermonitor.Core.Frames;

public readonly record struct FrameStats(bool HasData, double Fps, double FrameTimeMs, double Low1PercentFps, long FrameCount)
{
    public static FrameStats None { get; } = new(false, double.NaN, double.NaN, double.NaN, 0);
}

// Recebe os instantes (ms) dos presents do processo alvo e calcula FPS, frametime e 1% low.
// Escrito pela thread do ETW e lido pelas threads do overlay e do dashboard; tudo sob um lock curto.
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
                // Pausa longa (loading, alt-tab, jogo minimizado): recomeça a sequência sem registrar o intervalo.
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

- [ ] **Step 8: Rodar e ver passar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: todos passam (os anteriores + 18 novos).

- [ ] **Step 9: Commit**

```powershell
git add src/Supermonitor.Core/Frames tests/Supermonitor.Tests/Frames
git commit -m "feat(core): frame time aggregator with FPS, frame time and 1% low"
```

---

### Task 2: Configurações do overlay (schema v3), presets e editor

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
  - `class OverlayStyle` com `OverlayAnchor Anchor`, `int OffsetX`, `int OffsetY`, `OverlayLayout Layout`, `string FontFamily`, `float FontSize`, `string LabelColor`, `string ValueColor`, `string BackgroundColor`, `float BackgroundOpacity`, `int CornerRadius`, `int Padding`, `bool ShowFrameGraph`, `int GraphWidth`, `int GraphHeight`, `int RefreshHz`, `List<OverlayItemSettings> Items`; `static IReadOnlyList<int> AllowedRefreshHz`; `OverlayStyle Clone()`; `void Normalize()`.
  - `class OverlayPreset { string Name; OverlayStyle Style; }`
  - `class OverlaySettings { bool Enabled; string? MonitorId; OverlayStyle Style; List<OverlayPreset> UserPresets; void Normalize(); }`
  - `static class OverlayPresets { const string MinimalName, DefaultName, FullName; OverlayStyle Minimal(); OverlayStyle Default(); OverlayStyle Full(); IReadOnlyList<OverlayPreset> BuiltIn(); }`
  - `static class OverlayEditor { bool AddItem(OverlayStyle, OverlayMetric); bool RemoveItem(OverlayStyle, OverlayMetric); int MoveItem(OverlayStyle, OverlayMetric, int delta); void ApplyPreset(OverlaySettings, OverlayPreset); bool SavePreset(OverlaySettings, string name); bool DeletePreset(OverlaySettings, string name); IReadOnlyList<OverlayPreset> AllPresets(OverlaySettings); string? MatchingPresetName(OverlaySettings); }`
  - `AppSettings.Overlay` (`OverlaySettings`), `SettingsMigrator.CurrentVersion == 3`.

- [ ] **Step 1: Escrever os testes das configurações**

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

`tests/Supermonitor.Tests/Settings/SettingsMigratorTests.cs` — acrescentar dentro da classe:

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

E em `tests/Supermonitor.Tests/Settings/AppSettingsTests.cs` troque `Assert.Equal(2, settings.Version);` por `Assert.Equal(3, settings.Version);`.

- [ ] **Step 2: Escrever os testes do editor**

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

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: erro de compilação (tipos do overlay não existem).

- [ ] **Step 4: Implementar as configurações**

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

    // Só FPS, numa linha.
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

    // Nome do preset cujo estilo é idêntico ao atual, ou null quando o usuário mexeu em algo.
    public static string? MatchingPresetName(OverlaySettings overlay)
    {
        var current = Serialize(overlay.Style);
        return AllPresets(overlay).FirstOrDefault(p => Serialize(p.Style) == current)?.Name;
    }

    private static string Serialize(OverlayStyle style) =>
        JsonSerializer.Serialize(style, SettingsJsonContext.Default.OverlayStyle);
}
```

`SettingsJsonContext` precisa expor `OverlayStyle`. Em `src/Supermonitor.Core/Settings/SettingsJsonContext.cs` acrescente o atributo (e `using Supermonitor.Core.Overlay;`):

```csharp
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(OverlayStyle))]
```

Em `AppSettings.cs` (adicione `using Supermonitor.Core.Overlay;`): propriedade `public OverlaySettings Overlay { get; set; } = new();` depois de `Thresholds`, e no fim de `Normalize()`:

```csharp
        Overlay ??= new OverlaySettings();
        Overlay.Normalize();
```

Em `SettingsMigrator.cs`: `public const int CurrentVersion = 3;` e, antes de `root["version"] = version;`:

```csharp
        // v2 → v3: o bloco "overlay" é novo; ausente, o AppSettings usa os padrões. Nenhum campo mudou.
        if (version < 3)
        {
            version = 3;
        }
```

- [ ] **Step 5: Rodar e ver passar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: todos passam.

- [ ] **Step 6: Commit**

```powershell
git add src/Supermonitor.Core tests/Supermonitor.Tests
git commit -m "feat(core): overlay settings, presets and editor (settings schema v3)"
```

---

### Task 3: Conteúdo do overlay (catálogo, linhas, posição, taxa)

**Files:**
- Create: `src/Supermonitor.Core/Overlay/Rgba.cs`, `OverlayMetricCatalog.cs`, `IOverlayValues.cs`, `OverlayFrame.cs`, `OverlayFrameBuilder.cs`, `OverlayPlacement.cs`, `OverlayRefresh.cs` (todos em `src/Supermonitor.Core/Overlay/`)
- Modify: `src/Supermonitor.Core/Sensors/DemandTracker.cs`
- Test: `tests/Supermonitor.Tests/Overlay/OverlayFrameBuilderTests.cs`, `OverlayPlacementTests.cs`, `OverlayCatalogTests.cs`

**Interfaces:**
- Consumes: `OverlayStyle`, `OverlayMetric`, `MetricThreshold`, `UnitFormatter`, `UnitOptions`, `MetricIds`, `MetricUnit`, `ThresholdEvaluator`, `ColorText`.
- Produces:
  - `Demand.FrameCapture` (novo membro de `Demand`).
  - `readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)` com `static bool TryParse(string? hex, out Rgba color)` e `static Rgba Parse(string hex, Rgba fallback)`.
  - `static class OverlayMetricCatalog { string Label(OverlayMetric); MetricUnit Unit(OverlayMetric); string? StoreMetricId(OverlayMetric); Demand? Demand(OverlayMetric); }` (ids da MetricStore usam a GPU 0).
  - `static class OverlayDemand { (bool Hardware, bool Frames) For(OverlayStyle style); }`
  - `interface IOverlayValues { double Get(OverlayMetric metric); DateTime Now { get; } long FrameVersion { get; } int CopyFrameTimes(Span<float> destination); }`; `sealed class DemoOverlayValues : IOverlayValues`.
  - `sealed record OverlayRow(string Label, string Value, Rgba LabelColor, Rgba ValueColor)`; `sealed class OverlayFrame { OverlayRow[] Rows; float[] Graph; long GraphVersion; bool SameContentAs(OverlayFrame? other); }`
  - `static class OverlayFrameBuilder { OverlayFrame Build(OverlayStyle style, IOverlayValues values, UnitOptions units, IReadOnlyDictionary<string, MetricThreshold> thresholds); string FormatValue(OverlayMetric metric, double value, DateTime now, UnitOptions units); }`
  - `readonly record struct PixelRect(int X, int Y, int Width, int Height)` com `Right`/`Bottom`; `static class OverlayPlacement { (int X, int Y) Compute(OverlayAnchor anchor, int offsetX, int offsetY, PixelRect monitor, int width, int height); }`
  - `static class OverlayRefresh { int EffectiveHz(OverlayStyle style); }`

- [ ] **Step 1: Escrever os testes**

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
        // Overlay 200x100, offset 10 px. Em âncoras da direita e de baixo o offset afasta da borda; no centro, desloca para direita/baixo.
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

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: erro de compilação (tipos novos não existem).

- [ ] **Step 3: Implementar**

`Demand` (`src/Supermonitor.Core/Sensors/DemandTracker.cs`): acrescente o membro

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

    // Id da MetricStore; null para FPS/frametime/1% low (vêm do FrameCaptureService) e para o relógio.
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
    // O mini-gráfico de frametime redesenha a até 30 Hz; sem ele vale a taxa configurada.
    public static int EffectiveHz(OverlayStyle style) => style.ShowFrameGraph ? 30 : style.RefreshHz;
}
```

Em `OverlayMetricCatalog` o método se chama `Demand`, como o enum; por isso o corpo usa `Sensors.Demand.FrameCapture` (o nome `Sensors` resolve para o namespace `Supermonitor.Core.Sensors`). Em `OverlayDemand` (outra classe) `Demand.HardwareSensors` resolve para o enum.

`src/Supermonitor.Core/Overlay/IOverlayValues.cs`:

```csharp
namespace Supermonitor.Core.Overlay;

public interface IOverlayValues
{
    DateTime Now { get; }

    // Muda a cada frame novo; o overlay usa para redesenhar o mini-gráfico.
    long FrameVersion { get; }

    // Valor na unidade base da métrica; NaN quando indisponível.
    double Get(OverlayMetric metric);

    int CopyFrameTimes(Span<float> destination);
}

// Valores fixos e plausíveis para o preview do editor.
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

    // Frametimes (ms), do mais antigo ao mais novo; vazio quando o mini-gráfico está desligado.
    public float[] Graph { get; }

    public long GraphVersion { get; }

    // Decide se vale redesenhar: texto igual e gráfico sem frame novo → não.
    public bool SameContentAs(OverlayFrame? other) =>
        other is not null
        && Rows.AsSpan().SequenceEqual(other.Rows)
        && Graph.Length == other.Graph.Length
        && (Graph.Length == 0 || GraphVersion == other.GraphVersion);
}
```

`SequenceEqual` sobre `ReadOnlySpan<OverlayRow>` usa `Equals` do record (igualdade por valor). Se não compilar, use `Enumerable.SequenceEqual(Rows, other.Rows)`.

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
            // Duas amostras por pixel de largura do gráfico.
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

        // O rótulo já diz "FPS"; o valor mostra só o número.
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
    // O offset é uma margem para dentro a partir da borda da âncora; no centro desloca para direita/baixo.
    // O resultado sempre cabe no monitor (ou cola no canto superior esquerdo se o overlay for maior).
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

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: todos passam.

- [ ] **Step 5: Commit**

```powershell
git add src/Supermonitor.Core tests/Supermonitor.Tests
git commit -m "feat(core): overlay content builder, metric catalog, placement and refresh rate"
```

---

### Task 4: FrameCaptureService (alvo, ciclo de vida, retry) com fonte falsa

**Files:**
- Create: `src/Supermonitor.Core/Frames/FrameCaptureService.cs`
- Modify: `src/Supermonitor.Core/Sensors/DemandTracker.cs`
- Test: `tests/Supermonitor.Tests/Frames/FrameCaptureServiceTests.cs`, `tests/Supermonitor.Tests/Sensors/DemandTrackerTests.cs`

**Interfaces:**
- Consumes: `FrameTimeAggregator`, `FrameStats`, `DemandTracker`, `Demand.FrameCapture`, `Log`.
- Produces:
  - `DemandTracker.Changed` — `event Action<Demand, bool>?`, disparado na thread de quem adquire/libera quando a contagem de um `Demand` passa de 0 para 1 (`true`) ou de 1 para 0 (`false`). Assinantes devem ser rápidos.
  - `interface IPresentSource : IDisposable { event Action<int, double>? Presented; void Start(); }` — `Presented(processId, timestampMs)`, timestamp monotônico em milissegundos; `Start()` lança em caso de falha.
  - `interface IForegroundProbe { int GetForegroundProcessId(); }` — 0 quando não há janela.
  - `enum FrameCaptureState { Stopped, Running, Failed }`
  - `sealed class FrameCaptureService : IDisposable` — construtor `(DemandTracker demand, Func<IPresentSource> sourceFactory, IForegroundProbe probe, TimeProvider time, int ownProcessId, bool runLoop = true)`; `FrameCaptureState State`, `string? FailureReason`, `int TargetProcessId`, `FrameStats Current`, `long FrameVersion`, `int CopyFrameTimes(Span<float>)`, `static readonly TimeSpan IdleAfter` (2 s), `RetryInterval` (30 s), `PollInterval` (250 ms); `internal void Poll()`.

- [ ] **Step 1: Escrever os testes do `DemandTracker.Changed`**

Acrescente dentro de `DemandTrackerTests`:

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

- [ ] **Step 2: Escrever os testes do serviço**

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

        // Entrega `count` frames de 10 ms do processo (count + 1 presents), começando em `start`; devolve o último instante.
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

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: erro de compilação (`Changed`, `FrameCaptureService` não existem).

- [ ] **Step 4: Implementar `DemandTracker.Changed`**

Em `src/Supermonitor.Core/Sensors/DemandTracker.cs` substitua a classe `DemandTracker` por:

```csharp
public sealed class DemandTracker
{
    private readonly int[] _counts = new int[Enum.GetValues<Demand>().Length];

    // Disparado na thread de quem adquire/libera, quando a contagem de um Demand passa de 0 para 1 ou de 1 para 0.
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

- [ ] **Step 5: Implementar o serviço**

`src/Supermonitor.Core/Frames/FrameCaptureService.cs`:

```csharp
using Supermonitor.Core.Diagnostics;
using Supermonitor.Core.Sensors;

namespace Supermonitor.Core.Frames;

public interface IPresentSource : IDisposable
{
    // processId do app que apresentou e instante em ms (monotônico).
    event Action<int, double>? Presented;

    // Lança se a captura não puder começar.
    void Start();
}

public interface IForegroundProbe
{
    // 0 quando não há janela em primeiro plano.
    int GetForegroundProcessId();
}

public enum FrameCaptureState
{
    Stopped,
    Running,
    Failed,
}

// Liga a fonte de presents só enquanto existe uma lease Demand.FrameCapture, segue o processo em primeiro plano
// e alimenta o FrameTimeAggregator. Falhas ao iniciar são reportadas e tentadas de novo a cada 30 s.
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

    // Sem presents há mais de IdleAfter (jogo parou, alt-tab) volta a "sem dados".
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

    // Thread do ETW.
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
            // A demanda pode ter voltado entre a última checagem e o fim do laço. Após uma exceção não reinicia, para não girar em falso.
            if (completed && !_cts.IsCancellationRequested && _demand.IsActive(Demand.FrameCapture))
            {
                EnsureLoop();
            }
        }
    }
}
```

- [ ] **Step 6: Rodar e ver passar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: todos passam. Se `The_loop_starts_the_source_when_demand_appears_and_stops_it_when_it_ends` ficar instável, aumente o timeout para 10 s; o laço usa `PeriodicTimer` sobre o `FakeTimeProvider`, então o avanço do tempo no teste é o que o acorda.

- [ ] **Step 7: Commit**

```powershell
git add src/Supermonitor.Core tests/Supermonitor.Tests
git commit -m "feat(core): frame capture service with foreground target, idle detection and retry"
```

---

### Task 5: Fonte ETW real, sonda de primeiro plano e AppHost

**Files:**
- Create: `src/Supermonitor.Core/Frames/EtwPresentSource.cs`, `src/Supermonitor.Core/Frames/ForegroundProbe.cs`
- Modify: `src/Supermonitor.Core/Supermonitor.Core.csproj`, `src/Supermonitor.Core/Interop/User32.cs`, `src/Supermonitor.Core/AppHost.cs`
- Test: `tests/Supermonitor.Tests/Hardware/FrameCaptureSmokeTests.cs`

**Interfaces:**
- Consumes: `IPresentSource`, `IForegroundProbe`, `FrameCaptureService`, `Log`.
- Produces:
  - `sealed class EtwPresentSource : IPresentSource` com `const string SessionName = "Supermonitor-FPS"`.
  - `sealed class ForegroundProbe : IForegroundProbe`.
  - `AppHost.Frames` (`FrameCaptureService`).

- [ ] **Step 1: Escrever os testes de fumaça (trait Hardware)**

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
        // O número depende de haver janelas animando; só registramos.
        Console.WriteLine($"present events received in 3 s: {received}");
    }

    [Fact]
    public void Etw_source_replaces_an_orphan_session_with_the_same_name()
    {
        if (!IsElevated)
        {
            Assert.Skip("ETW needs an administrator process; run the test runner elevated.");
        }

        // Simula um crash anterior: a sessão continua ativa depois que o processo "morre".
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

- [ ] **Step 2: Adicionar o pacote e verificar que o teste não compila ainda**

Em `src/Supermonitor.Core/Supermonitor.Core.csproj`, dentro do `ItemGroup` de pacotes:

```xml
    <PackageReference Include="Microsoft.Diagnostics.Tracing.TraceEvent" Version="3.2.8" />
```

Run: `dotnet build tests/Supermonitor.Tests 2>&1 | Select-String "error"`
Expected: erros por `EtwPresentSource` e `ForegroundProbe` inexistentes.

- [ ] **Step 3: P/Invoke**

Em `src/Supermonitor.Core/Interop/User32.cs`, dentro da classe, acrescente:

```csharp
    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);
```

- [ ] **Step 4: Implementar a sonda e a fonte ETW**

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

// Um evento por present (DxgKrnl, id 184) de qualquer processo; o ProcessID do cabeçalho é o do app que apresenta.
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

        // O construtor padrão (Create) para e recria uma sessão órfã com o mesmo nome, deixada por um crash anterior.
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

        // Parar a sessão faz Process() retornar.
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

- [ ] **Step 5: Ligar ao `AppHost`**

Em `src/Supermonitor.Core/AppHost.cs`: adicione `using Supermonitor.Core.Frames;`, a propriedade `public FrameCaptureService Frames { get; }`, o parâmetro `FrameCaptureService frames` no construtor privado (`Frames = frames;`) e, em `Create()`, depois de criar `demand`:

```csharp
        var frames = new FrameCaptureService(demand, () => new EtwPresentSource(), new ForegroundProbe(), TimeProvider.System, Environment.ProcessId);
```

passando `frames` ao construtor `new AppHost(...)`. Em `DisposeAsync()`, antes de `await Hub.DisposeAsync()`:

```csharp
        Frames.Dispose();
```

- [ ] **Step 6: Rodar os testes**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: todos passam (os de hardware não rodam aqui).

Run (shell sem elevação): `dotnet test --project tests/Supermonitor.Tests --filter-class "Supermonitor.Tests.Hardware.FrameCaptureSmokeTests"`
Expected: `Foreground_probe...` e `Etw_source_without_administrator_rights...` passam; os dois testes que exigem administrador aparecem como ignorados.

Run (terminal elevado — o usuário abre um PowerShell como administrador e roda o mesmo comando): os dois testes ETW passam e `present events received in 3 s: N` é impresso. N > 0 é esperado com qualquer janela animando; N = 0 não reprova o teste, mas investigue antes de seguir (a Task 10 depende disso).

- [ ] **Step 7: Commit**

```powershell
git add src/Supermonitor.Core tests/Supermonitor.Tests
git commit -m "feat(core): ETW present source, foreground probe and AppHost wiring"
```

---

### Task 6: Projeto Supermonitor.Overlay e OverlayRenderer (Direct2D → DIB)

**Files:**
- Create: `src/Supermonitor.Overlay/Supermonitor.Overlay.csproj`, `src/Supermonitor.Overlay/Interop/Gdi32.cs`, `src/Supermonitor.Overlay/OverlayRenderer.cs`
- Modify: `Supermonitor.slnx`, `tests/Supermonitor.Tests/Supermonitor.Tests.csproj`
- Test: `tests/Supermonitor.Tests/Overlay/OverlayRendererTests.cs`

**Interfaces:**
- Consumes: `OverlayFrame`, `OverlayRow`, `OverlayStyle`, `OverlayLayout`, `Rgba` (Task 3 e 2).
- Produces (namespace `Supermonitor.Overlay`):
  - `readonly record struct OverlayBitmap(nint Hdc, nint Bits, int Width, int Height)` com `ReadOnlySpan<byte> Pixels` (BGRA premultiplicado, top-down, `Width * Height * 4` bytes). Válido até a próxima chamada de `Render` do mesmo renderizador.
  - `sealed class OverlayRenderer : IDisposable` com `OverlayBitmap Render(OverlayFrame frame, OverlayStyle style, float scale)`. **Thread-affine**: use sempre da mesma thread (a fábrica Direct2D é single-thread). `scale` = DPI do monitor / 96; fonte, padding, raio, gráfico e gaps são multiplicados por ele.

- [ ] **Step 1: Criar o projeto e referenciá-lo**

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

Em `Supermonitor.slnx`, dentro de `<Folder Name="/src/">`, depois do projeto Core:

```xml
    <Project Path="src/Supermonitor.Overlay/Supermonitor.Overlay.csproj" />
```

Em `tests/Supermonitor.Tests/Supermonitor.Tests.csproj`, ao lado do `ProjectReference` do Core:

```xml
    <ProjectReference Include="..\..\src\Supermonitor.Overlay\Supermonitor.Overlay.csproj" />
```

Run: `dotnet build tests/Supermonitor.Tests 2>&1 | Select-String "error|Erro"`
Expected: `0 Erro(s)` (restaura os pacotes Vortice).

- [ ] **Step 2: Escrever os testes do renderizador**

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

        // A métrica do DirectWrite arredonda por glifo; 10% de tolerância cobre isso.
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

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-class "Supermonitor.Tests.Overlay.OverlayRendererTests"`
Expected: erro de compilação (`OverlayRenderer` não existe).

- [ ] **Step 4: P/Invoke do GDI**

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

- [ ] **Step 5: Implementar o renderizador**

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

// Desenha o OverlayFrame com Direct2D/DirectWrite num DIB 32 bits premultiplicado (o formato do UpdateLayeredWindow).
// Não toca em janelas: o overlay apresenta o DIB e o preview do editor copia os pixels.
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

    // Preenche as posições de cada linha e devolve o tamanho do bloco de texto.
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

Se `target.DrawLine(previous, point, brush, width)` não compilar na versão 3.8.3 (assinatura com `ID2D1StrokeStyle?` obrigatório), passe `null` como último argumento. Se `IDWriteTextLayout.Metrics` expuser `Width`/`Height` com nomes diferentes, use `WidthIncludingTrailingWhitespace` ou `LayoutWidth` conforme o IntelliSense; o spike usou `layout.Metrics.Width`.

- [ ] **Step 6: Rodar e ver passar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: todos passam (incluindo os 12 do renderizador).

- [ ] **Step 7: Commit**

```powershell
git add Supermonitor.slnx src/Supermonitor.Overlay tests/Supermonitor.Tests
git commit -m "feat(overlay): Direct2D overlay renderer drawing into a premultiplied DIB"
```

---

### Task 7: Janela layered, monitores e OverlayService

**Files:**
- Create: `src/Supermonitor.Overlay/Interop/User32.cs`, `src/Supermonitor.Overlay/Interop/Shcore.cs`, `src/Supermonitor.Overlay/Interop/Kernel32.cs`, `src/Supermonitor.Overlay/MonitorCatalog.cs`, `src/Supermonitor.Overlay/LiveOverlayValues.cs`, `src/Supermonitor.Overlay/OverlayWindow.cs`, `src/Supermonitor.Overlay/OverlayService.cs`
- Test: `tests/Supermonitor.Tests/Overlay/MonitorCatalogTests.cs`, `tests/Supermonitor.Tests/Overlay/LiveOverlayValuesTests.cs`, `tests/Supermonitor.Tests/Hardware/OverlayWindowSmokeTests.cs`

**Interfaces:**
- Consumes: `OverlayRenderer`, `OverlayBitmap`, `OverlayFrameBuilder`, `OverlayPlacement`, `PixelRect`, `OverlayDemand`, `OverlayRefresh`, `IOverlayValues`, `AppHost`, `FrameCaptureService`, `MetricStore`, `Demand`.
- Produces (namespace `Supermonitor.Overlay`):
  - `sealed record MonitorInfo(string Id, string Name, PixelRect Bounds, bool IsPrimary, float Scale)`; `static class MonitorCatalog { IReadOnlyList<MonitorInfo> Enumerate(); MonitorInfo? Resolve(IReadOnlyList<MonitorInfo> monitors, string? id); }` — `Resolve` devolve o monitor com o `Id`, senão o primário, senão o primeiro, senão `null`.
  - `sealed class LiveOverlayValues(MetricStore store, FrameCaptureService frames, TimeProvider time) : IOverlayValues`.
  - `sealed class OverlayService : IDisposable` — `OverlayService(AppHost host)`; `void Apply()` (relê `host.Settings.Current.Overlay`, qualquer thread), `bool Enabled`, `void SetEnabled(bool enabled)` (grava, salva e aplica), `event Action? Changed` (disparado por `SetEnabled`, na thread de quem chamou).

- [ ] **Step 1: Escrever os testes**

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

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: erro de compilação (`MonitorInfo`, `MonitorCatalog`, `LiveOverlayValues` não existem).

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

- [ ] **Step 4: Monitores e valores ao vivo**

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

// Lê o último valor de cada métrica da MetricStore e os números de FPS do FrameCaptureService.
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

- [ ] **Step 5: A janela layered**

`src/Supermonitor.Overlay/OverlayWindow.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;
using Supermonitor.Overlay.Interop;

namespace Supermonitor.Overlay;

// Janela Win32 layered, click-through, sempre no topo, sem foco e fora do Alt+Tab.
// Precisa ser criada e usada na thread que roda o laço de mensagens.
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

    // Troca o conteúdo e a posição de uma vez; o DIB já vem premultiplicado.
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

    // Jogos borderless também são topmost; reafirmar a ordem de tempos em tempos mantém o overlay por cima.
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

- [ ] **Step 6: O serviço**

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

// Dono da janela do overlay. Uma thread própria roda o laço de mensagens, desenha na taxa configurada
// e só redesenha quando algum texto (ou o gráfico) mudou. Segura as leases de demanda enquanto visível.
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

    // Estado da thread do overlay.
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

    // Tira uma cópia das configurações (chamar na thread que as altera) e avisa a thread do overlay.
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
            // Um erro por vez no log: a 30 Hz, repetir a mesma falha inundaria o arquivo.
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

    // Monitor escolhido (ou o primário se sumiu); mudou de posição, tamanho ou DPI → redesenha.
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

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: todos passam.

Run: `dotnet test --project tests/Supermonitor.Tests --filter-class "Supermonitor.Tests.Hardware.OverlayWindowSmokeTests"`
Expected: passa (monitores enumerados, um primário, escala entre 1 e 4).

Run: `dotnet build src/Supermonitor.Overlay -p:Platform=x64 2>&1 | Select-String "error|Erro"`
Expected: `0 Erro(s)`. Se `delegate* unmanaged[Stdcall]` em `LibraryImport` for rejeitado pelo gerador, troque o parâmetro por `nint callback` e passe `(nint)(delegate* unmanaged[Stdcall]<nint, nint, User32.RECT*, nint, int>)&OnMonitor`.

- [ ] **Step 8: Commit**

```powershell
git add src/Supermonitor.Overlay tests/Supermonitor.Tests
git commit -m "feat(overlay): layered overlay window, monitor catalog and overlay service"
```

---

### Task 8: Integração no app (OverlayService e item de bandeja)

**Files:**
- Modify: `src/Supermonitor.Core/Interop/User32.cs`, `src/Supermonitor.Core/Shell/TrayIcon.cs`, `src/Supermonitor/Supermonitor.csproj`, `src/Supermonitor/App.xaml.cs`

**Interfaces:**
- Consumes: `OverlayService` (Task 7), `TrayIcon`, `AppHost`.
- Produces:
  - `TrayIcon(string tooltip, string iconPath, Action onOpen, Action onExit, Func<bool>? isOverlayEnabled = null, Action? onToggleOverlay = null)` — com os dois últimos preenchidos, o menu ganha o item "Show overlay" marcado quando ligado.
  - `App.Overlay` (`OverlayService?`) — a página do editor (Task 9) usa para aplicar mudanças.

Esta task não tem teste de unidade (bandeja e janela dependem de sessão de desktop); a verificação é manual no fim.

- [ ] **Step 1: Constante do menu**

Em `src/Supermonitor.Core/Interop/User32.cs`, junto de `MF_STRING`:

```csharp
    public const uint MF_CHECKED = 0x0008;
```

- [ ] **Step 2: Item de menu na bandeja**

Em `src/Supermonitor.Core/Shell/TrayIcon.cs`:

1. Constante `private const int MenuOverlay = 3;`.
2. Campos `private readonly Func<bool>? _isOverlayEnabled;` e `private readonly Action? _onToggleOverlay;`.
3. Construtor:

```csharp
    public TrayIcon(string tooltip, string iconPath, Action onOpen, Action onExit, Func<bool>? isOverlayEnabled = null, Action? onToggleOverlay = null)
    {
        _tooltip = tooltip;
        _onOpen = onOpen;
        _onExit = onExit;
        _isOverlayEnabled = isOverlayEnabled;
        _onToggleOverlay = onToggleOverlay;
```

(o restante do construtor fica como está).

4. Em `ShowMenu()`, entre "Open Supermonitor" e o separador:

```csharp
        User32.AppendMenuW(menu, User32.MF_STRING, MenuOpen, "Open Supermonitor");
        if (_onToggleOverlay is not null)
        {
            var flags = User32.MF_STRING | (_isOverlayEnabled?.Invoke() == true ? User32.MF_CHECKED : 0);
            User32.AppendMenuW(menu, flags, MenuOverlay, "Show overlay");
        }

        User32.AppendMenuW(menu, User32.MF_SEPARATOR, 0, null);
```

5. No tratamento do comando, depois do `MenuExit`:

```csharp
        else if (command == MenuOverlay)
        {
            _onToggleOverlay?.Invoke();
        }
```

- [ ] **Step 3: Referenciar o projeto Overlay**

Em `src/Supermonitor/Supermonitor.csproj`, no `ItemGroup` de `ProjectReference`:

```xml
    <ProjectReference Include="..\Supermonitor.Overlay\Supermonitor.Overlay.csproj" />
```

- [ ] **Step 4: Ligar no `App`**

Em `src/Supermonitor/App.xaml.cs`:

1. `using Supermonitor.Overlay;`
2. Campo `private OverlayService? _overlay;` e propriedade pública `public OverlayService? Overlay => _overlay;` (junto de `PawnIoBannerDismissed`).
3. Em `OnLaunched`, depois de `_host.Start();` e antes de criar a bandeja:

```csharp
        _overlay = new OverlayService(_host);
```

e na criação da bandeja:

```csharp
        _tray = new TrayIcon(
            "Supermonitor",
            AppPaths.IconPath,
            onOpen: () => _dispatcher.TryEnqueue(ShowDashboard),
            onExit: () => _dispatcher.TryEnqueue(ExitApp),
            isOverlayEnabled: () => _overlay?.Enabled == true,
            onToggleOverlay: () => _dispatcher.TryEnqueue(() => _overlay?.SetEnabled(_overlay.Enabled == false)));
```

4. Em `ExitApp()`, depois de `_window?.Close();` e antes do `_host.DisposeAsync()`:

```csharp
            _overlay?.Dispose();
```

- [ ] **Step 5: Compilar**

Run: `dotnet build src/Supermonitor/Supermonitor.csproj -c Debug -p:Platform=x64 -o <pasta temporária> 2>&1 | Select-String "error|Erro"`
Expected: `0 Erro(s)`.

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: todos passam.

- [ ] **Step 6: Verificação manual (usuário)**

O usuário sai do app pela bandeja, o assistente recompila e abre o app elevado. Conferir, nesta ordem:

1. Menu da bandeja mostra "Show overlay" sem marca. Clicar: o overlay aparece no canto superior esquerdo (preset Default, 16 px de margem) e a marca aparece ao reabrir o menu.
2. Clicar em outra janela que anime (vídeo, página com animação): FPS e frametime aparecem e o mini-gráfico se move. Clicar numa janela parada: FPS volta a `N/A` em ~2 s.
3. O overlay não rouba o foco, o clique passa através dele e ele não aparece no Alt+Tab.
4. CPU, RAM e GPU do overlay batem com o Gerenciador de Tarefas (±2 pontos).
5. Desligar pelo menu: o overlay some. `%LocalAppData%\Supermonitor\settings.json` tem `"overlay": { "enabled": ... }` e `"version": 3`.
6. Sair pelo menu "Exit": `logman query -ets` (PowerShell elevado) não lista `Supermonitor-FPS`.

- [ ] **Step 7: Commit**

```powershell
git add src
git commit -m "feat(app): run the overlay service and toggle it from the tray menu"
```

---

### Task 9: Página Overlay (editor com preview ao vivo e presets)

**Files:**
- Create: `src/Supermonitor/Views/OverlayPage.xaml`, `src/Supermonitor/Views/OverlayPage.xaml.cs`, `src/Supermonitor/Dialogs/OverlayItemDialog.cs`
- Modify: `src/Supermonitor/MainWindow.xaml`, `src/Supermonitor/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `OverlayEditor`, `OverlayPresets`, `OverlaySettings`, `OverlayStyle`, `OverlayItemSettings`, `OverlayMetric`, `OverlayMetricCatalog`, `OverlayFrameBuilder`, `DemoOverlayValues`, `OverlayRenderer`, `MonitorCatalog`, `OverlayService` (via `App.Current.Overlay`), `PageContext`, `ColorText`, `SettingsStore`.
- Produces: item de navegação "Overlay" (tag `overlay`); `OverlayItemDialog(OverlayItemSettings item)` com `OverlayItemSettings Result`.

Esta task é de interface; os testes vêm das Tasks 2 e 3 (lógica) e da verificação manual no fim.

- [ ] **Step 1: Item de navegação**

Em `src/Supermonitor/MainWindow.xaml`, depois do item "Processes":

```xml
                <NavigationViewItem Content="Overlay" Tag="overlay">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE7F4;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
```

Em `MainWindow.xaml.cs`, no `switch` de `Navigate`, antes de `case "settings":`:

```csharp
            case "overlay":
                ContentFrame.Navigate(typeof(OverlayPage), context);
                break;
```

- [ ] **Step 2: Diálogo de cores e limiares por item**

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

- [ ] **Step 3: XAML da página**

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

    // Copia as configurações para os controles.
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

    // Grava, aplica no overlay real e atualiza preview e seleção de preset.
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

    // ----- posição -----

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

    // ----- métricas -----

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

    // ----- texto, cores e fundo -----

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

Observações para quem executa:
- A propriedade privada da página se chama `Look` (e não `Style`) para não esconder `FrameworkElement.Style`. `Overlay` também é o nome de um namespace (`Supermonitor.Overlay`), mas dentro da classe o nome simples resolve para a propriedade; se o compilador reclamar de ambiguidade em `Overlay.Enabled`, renomeie a propriedade para `Config`.
- `ColorText.ToHex` existe em `Supermonitor.Core.Settings`. `Windows.UI.Color` precisa de `using Windows.UI;` (já incluído).
- `Application.Current.Resources["SystemFillColorCriticalBrush"]` é o mesmo recurso usado em `MetricCard.xaml.cs`.
- `PixelBuffer.AsStream()` vem de `System.Runtime.InteropServices.WindowsRuntime` (já importado). Se a extensão não resolver, use `Marshal`/`IBufferByteAccess`: obtenha o ponteiro com `((IBufferByteAccess)(object)_bitmap.PixelBuffer).Buffer` e copie com `Marshal.Copy`.

- [ ] **Step 5: Compilar**

Run: `dotnet build src/Supermonitor/Supermonitor.csproj -c Debug -p:Platform=x64 -o <pasta temporária> 2>&1 | Select-String "error|Erro"`
Expected: `0 Erro(s)`. Corrija erros de XAML/C# antes de seguir.

- [ ] **Step 6: Verificação manual (usuário)**

O usuário sai do app pela bandeja, o assistente recompila e abre o app. Em **Overlay**:

1. O preview mostra o preset Default com valores de exemplo (144 FPS, 6.9 ms, …) e um mini-gráfico. Trocar o preset para Minimal e Full muda o preview e o overlay real (se ligado) na hora.
2. Cada controle atualiza o preview: âncora, offsets, layout (Horizontal/Vertical/Compact), fonte (escolher da lista e digitar `Consolas` + Enter), tamanho, cor do rótulo/valor/fundo (picker), opacidade, raio, padding, gráfico (liga/desliga, largura, altura), taxa.
3. Itens: subir/descer/remover mudam a ordem; "Add metric" lista só o que falta; o ícone de engrenagem abre o diálogo e uma cor `#FF0000` no valor pinta o valor de vermelho no preview; limiar 1 / 2 para CPU temp pinta o valor de laranja/vermelho (62 °C do exemplo passa dos dois).
4. Qualquer alteração troca o seletor de preset para "Custom". "Save as…" com nome `Meu` cria o preset e o seleciona; nomes vazios ou `Default` mostram o erro no diálogo; "Delete" remove `Meu`.
5. Ligar o overlay pelo interruptor da página reflete na bandeja (marca em "Show overlay") e vice-versa ao reabrir a página.
6. Mudar o monitor, a âncora ou o tamanho com o overlay ligado move/redimensiona a janela real em tempo real.
7. Reiniciar o app: o estilo, os presets do usuário e o estado ligado/desligado voltam como estavam.

- [ ] **Step 7: Commit**

```powershell
git add src
git commit -m "feat(app): overlay editor page with live preview, item editor and user presets"
```

---

### Task 10: Verificação de desempenho, FPS real e fechamento

**Files:**
- Create: `docs/superpowers/plans/2026-10-10-supermonitor-03-results.md`

**Interfaces:**
- Consumes: app completo.
- Produces: registro dos critérios 1–4 da spec (seção 1) com o overlay ativo, resultado dos testes e desvios do plano.

- [ ] **Step 1: Testes**

Run: `dotnet test --project tests/Supermonitor.Tests`
Expected: todos passam; os testes ETW aparecem como ignorados neste shell sem elevação.

Pedir ao usuário que rode, em um PowerShell **como administrador**, `dotnet test --project tests/Supermonitor.Tests --filter-class "Supermonitor.Tests.Hardware.FrameCaptureSmokeTests"` e informe a saída. Os dois testes ETW devem passar e imprimir `present events received in 3 s: N` com N > 0.

- [ ] **Step 2: Verificação de FPS real (critério 4 da spec)**

Com o build Debug ou Release aberto como administrador e o overlay ligado:

1. Abrir um jogo em modo janela ou borderless (DX11 ou DX12). FPS, frametime e 1% low aparecem em até ~2 s e o mini-gráfico acompanha.
2. Comparar com uma referência independente (contador de FPS do próprio jogo, do Steam ou Xbox Game Bar > Desempenho). Critério: diferença de até 2 FPS ou 3%, o maior dos dois.
3. Alt-tab para a área de trabalho: FPS volta a `N/A` em ~2 s; ao voltar ao jogo, os números reaparecem.
4. Mover o jogo entre monitores e trocar o monitor do overlay na página Overlay: o overlay segue o monitor escolhido.
5. Jogo Vulkan ou OpenGL, se houver um à mão: mesmo comportamento (o evento DxgKrnl cobre todas as APIs).

Se um jogo DX11/DX12 mostrar `N/A` ou o dobro do FPS real: **parar** e aplicar superpowers:systematic-debugging. A primeira hipótese é o evento DxgKrnl 184 não representar um present por frame para esse jogo; o fallback previsto nas Global Constraints é assinar também `Microsoft-Windows-DXGI` (GUID `ca11c036-0102-4a2d-a6ad-f03cfed5d3c9`, palavra-chave `0x1`, evento `42` = `Present_Start`) e, por processo, usar o DXGI quando ele aparecer. Registrar a decisão nos resultados.

- [ ] **Step 3: Recuperação após falha (item 6 da verificação manual da spec, parte ETW)**

1. Com o overlay ligado e FPS em `N/A` ou com valores, encerrar o app à força: `taskkill /f /im Supermonitor.exe` (PowerShell como administrador).
2. `logman query -ets` deve ainda listar `Supermonitor-FPS` (sessão órfã).
3. Abrir o app de novo: o overlay volta ligado, o FPS funciona e `logman query -ets` lista **uma** sessão `Supermonitor-FPS`.
4. Sair pelo menu "Exit": a sessão some.

- [ ] **Step 4: Medir no build Release**

1. `dotnet build src/Supermonitor/Supermonitor.csproj -c Release -p:Platform=x64`; usuário sai do app pela bandeja e abre o exe de `bin\x64\Release\…\win-x64\` como administrador.
2. Overlay ligado (preset Default) e Overview aberto, intervalo 1 s, 60 s: `pwsh -NoProfile tools/measure-cpu.ps1`. Critério: < 1%.
3. Janela principal fechada (só bandeja + overlay), overlay ligado com o preset Default (FPS e mini-gráfico a 30 Hz), uma janela animando em primeiro plano, 60 s: `pwsh -NoProfile tools/measure-cpu.ps1`. Critério: < 0,3%.
4. Mesmo cenário com o preset Minimal (4 Hz, sem gráfico), 60 s. Registrar (referência).
5. Overlay desligado e janela fechada, 60 s. Critério: < 0,3% (prova que as leases e a sessão ETW foram liberadas: `logman query -ets` não lista `Supermonitor-FPS`).

Se um critério falhar, aplicar superpowers:systematic-debugging. Suspeitos, em ordem: (1) volume de eventos ETW (medido em 2026-10-10: ~6000 eventos/s e ~31 ms de CPU em 8 s só com a palavra-chave `Present`); (2) `FrameCaptureService.Current` chamado três vezes por tick do overlay, cada uma ordenando até ~2400 frametimes em `FrameTimeAggregator.GetStats` (cachear o `FrameStats` por `FrameVersion` e por 50 ms); (3) redesenho a 30 Hz com o mini-gráfico, recriando text layouts do DirectWrite a cada frame (cachear layouts por texto); (4) `MonitorCatalog.Enumerate` uma vez por segundo; (5) EcoQoS: o processo entra em modo de eficiência sem janela, o que pode atrasar a thread do overlay e a do ETW (opt-out por thread com `SetThreadInformation(ThreadPowerThrottling)` na thread `Overlay`).

- [ ] **Step 5: Registrar resultados**

`docs/superpowers/plans/2026-10-10-supermonitor-03-results.md`: data, build, CPU e memória nos cenários do Step 4, contagem de testes, verificação de FPS real (jogo, referência, diferença), recuperação após falha, verificações manuais das Tasks 8 e 9 e desvios do plano com as decisões tomadas. Desvios já conhecidos para registrar:
- A captura usa só o evento `Present` do DxgKrnl (a spec 3.4 também cita o DXGI); motivo e evidência nas Global Constraints.
- Sem métricas de FPS na `MetricStore`: FPS, frametime e 1% low são lidos direto do `FrameCaptureService`, então não aparecem como cards do dashboard.
- O item "estado do limitador" do overlay (spec 4.4) fica para o Plano 4, quando existir `IFrameLimiter`; o catálogo `OverlayMetric` ganha o membro lá.
- O alvo do FPS é o processo em primeiro plano (qualquer app). A restrição a jogos detectados vem com o `GameDetector` do Plano 4.

- [ ] **Step 6: Commit**

```powershell
git add docs/superpowers/plans/2026-10-10-supermonitor-03-results.md
git commit -m "docs: record FPS capture and overlay plan results"
```

---

## Self-Review

**Cobertura da spec (3.4, 3.5, 4.4):**
- 3.4 sessão ETW nomeada `Supermonitor-FPS`, órfã parada ao iniciar → Task 5 (`EtwPresentSource`, teste de sessão órfã). Alvo = processo em primeiro plano, frametime = intervalo entre presents → Tasks 4 e 1. `FrameTimeAggregator` com FPS médio da janela, frametime atual e buffer de frametimes para o mini-gráfico e o benchmark → Task 1 (`CopyFrameTimes`, `Percentile` reutilizável). DXGI como complemento → desvio registrado (Global Constraints, Task 10).
- 3.5 estilos `WS_EX_LAYERED | TRANSPARENT | TOPMOST | NOACTIVATE | TOOLWINDOW` → Task 7 (`OverlayWindow`). Direct2D num DIB 32 bits premultiplicado + `UpdateLayeredWindow`, DirectWrite em tons de cinza → Task 6/7. Redesenho só quando um valor muda, 4 Hz padrão, gráfico até 30 Hz → Tasks 3 (`SameContentAs`, `OverlayRefresh`) e 7 (`Tick`).
- 4.4 monitor e 9 âncoras + deslocamento → Tasks 3 (`OverlayPlacement`) e 7 (`MonitorCatalog`) e 9; métricas e ordem → Tasks 2/3/9 (limitador: Plano 4); layout horizontal/vertical/compacto → Task 6; fonte, tamanho, cores por métrica, limiares → Tasks 2/3/9; fundo (cor, opacidade, raio, padding) → Tasks 2/6/9; mini-gráfico liga/desliga, largura, altura → Tasks 2/6/9; taxa de atualização → Tasks 2/9; presets Minimal/Default/Full e do usuário → Task 2; preview ao vivo com o mesmo renderizador → Task 9.
- Critérios de sucesso 2 (só bandeja + overlay < 0,3%) e 4 (FPS real) → Task 10.
- Estados de erro da spec 7: falha ao criar sessão ETW → FPS "N/A", motivo na UI, retry a cada 30 s → Task 4 (`Failed`/`RetryInterval`), Task 9 (`CaptureBar`).

**Placeholders:** nenhum "TBD"/"TODO"; os trechos condicionais (assinaturas do Vortice, `AsStream`) trazem a alternativa concreta.

**Consistência de tipos:** `FrameStats`/`FrameTimeAggregator` (Task 1) → `FrameCaptureService.Current/CopyFrameTimes/FrameVersion` (Task 4) → `LiveOverlayValues` (Task 7); `OverlayStyle/OverlayItemSettings` (Task 2) → `OverlayFrameBuilder` (Task 3) → `OverlayRenderer` (Task 6) → `OverlayService` (Task 7) e `OverlayPage` (Task 9); `Demand.FrameCapture` (Task 3) → `DemandTracker.Changed` (Task 4) → `OverlayService` (Task 7); `PixelRect` (Task 3) → `MonitorInfo.Bounds` (Task 7); `OverlayService.Changed/SetEnabled/Apply/Enabled` (Task 7) → `App` (Task 8) → `OverlayPage` (Task 9).

**Review Focus:** (1) Task 4; (2) Tasks 4 e 5; (3) Task 1; (4) Task 2; (5) Task 3 (`OverlayPlacement`) e Task 7 (`MonitorCatalog.Resolve`).
