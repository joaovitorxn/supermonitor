# Supermonitor Plano 2 — Sensores de hardware e dashboard completo: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Adicionar sensores de hardware (temperaturas, clocks, potência, fans via LibreHardwareMonitor), lista de processos, páginas de detalhe (CPU, Memory, GPU, Disks, Network, Processes) e customização completa do dashboard e da aparência.

**Architecture:** Continua o processo único da fundação. `DemandTracker` liga provedores caros (LHM, processos) só enquanto alguém consome. `HardwareProvider` mapeia sensores LHM para a mesma `MetricStore`, com ids estáveis (`cpu.temp`, `gpu0.temp`…) para os sensores mais usados. `ProcessMonitor` lê `NtQuerySystemInformation` e cruza com o uso de GPU por PID vindo do `GpuProvider`. Configurações passam para o schema v2 (cards com tamanho e tipo de gráfico, tema, fundo, cor de destaque, unidades, limiares). Páginas de detalhe usam uma `CategoryPage` genérica que agrupa métricas por dispositivo.

**Tech Stack:** igual ao Plano 1 + `LibreHardwareMonitorLib` 0.9.6 (driver PawnIO, winget id `namazso.PawnIO`).

**Spec:** `docs/superpowers/specs/2026-10-10-supermonitor-design.md`
**Índice:** `docs/superpowers/plans/2026-10-10-supermonitor-plan-index.md`
**Base:** `main` em `1a4ad29` (Plano 1 concluído; resultados em `docs/superpowers/plans/2026-10-10-supermonitor-01-results.md`).

## Global Constraints

- Tudo do Plano 1 continua valendo: Windows 11, x64, `net10.0-windows10.0.22621.0`, unpackaged, `requireAdministrator`, UI em EN-US, `N/A` para valor ausente, sem alocação por tick nos provedores do caminho quente (`ProcessMonitor` e `HardwareProvider` só rodam sob demanda e podem alocar dentro do LHM/snapshot).
- Testes de unidade: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`. Testes de hardware: `dotnet test --project tests/Supermonitor.Tests --filter-trait "Category=Hardware"`. Suíte completa: `dotnet test --project tests/Supermonitor.Tests`.
- Build do app: `dotnet build src/Supermonitor/Supermonitor.csproj -c Debug -p:Platform=x64`. Com o app aberto o exe fica bloqueado; para checar compilação use `-p:OutDir=<pasta temporária>\`.
- O app elevado não pode ser encerrado nem clicado por um shell sem elevação. Verificações visuais ficam com o usuário (sair pela bandeja, aprovar UAC).
- Critérios de desempenho continuam: dashboard aberto < 1% CPU, só bandeja < 0,3% CPU (build Release, intervalo 1 s).
- Sensores LHM nunca são lidos na thread de UI. `Computer.Open()` leva ~765 ms nesta máquina.
- Limiares são comparados na unidade base da métrica (°C, %, bytes), mesmo quando a exibição está em °F ou bits.
- Glifos (Segoe Fluent Icons, conferidos visualmente): Home `E80F`, CPU `E950`, Memory `E964`, GPU `F211`, Disks `EDA2`, Network `E968`, Processes `E8FD`, Edit `E70F`, Add `E710`, Remove `E74D`.
- Nomes reais dos sensores LHM nesta máquina (probe 2026-10-10): CPU `Core (Tctl/Tdie)` (temperatura), `Package` (potência), `Cores (Average)` (clock); GPU AMD `GPU Core`/`GPU Hot Spot`/`GPU Memory` (temperatura), `GPU Core`/`GPU Memory` (clock), `GPU Package` (potência), `GPU Fan` (Fan e Control).
- Todo commit termina com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. LHM indisponível ou parcial (PawnIO ausente, sem admin, GPU sem suporte ADL) → app abre normalmente; métricas LHM ausentes ou `N/A`; GPU via PDH continua funcionando. Testes: Task 3 (mapeador ignora tipos desconhecidos) e Task 2 (provedor sem demanda fica `N/A`).
2. Arquivo de configurações v1 do Plano 1 (`overviewCards`) → carrega no schema v2 sem perder os cards. Teste na Task 6.
3. Processos que nascem e morrem entre amostras e PID reutilizado → sem exceção; CPU% só calculado para o mesmo `(pid, createTime)`. Testes na Task 5 (`ProcessTracker`).
4. Card cuja métrica não existe na máquina (ex.: `gpu0.temp` sem LHM) → card mostra `N/A` e pode ser removido, movido e redimensionado. Teste na Task 7 (`DashboardEditor` opera só por id).
5. Lease de demanda que vaza (página descarregada, janela fechada) → LHM continuaria rodando na bandeja. Teste de lease na Task 2; verificação de CPU só-bandeja na Task 15.

---

## File Structure

```
src/Supermonitor.Core/
  Supermonitor.Core.csproj                  + LibreHardwareMonitorLib
  AppHost.cs                                + Demand, Processes, HardwareProvider, ProcessGpuUsage
  Diagnostics/Log.cs                        prune tolera arquivo somente leitura
  Metrics/MetricUnit.cs                     MetricDescriptor ganha Device
  Metrics/MetricIds.cs                      + ids conhecidos de hardware
  Sensors/ISensorProvider.cs                + RequiredDemand (membro padrão)
  Sensors/DemandTracker.cs                  Demand, DemandTracker
  Sensors/SensorHub.cs                      handlers isolados, init fora da UI, demanda
  Sensors/Hardware/HardwareSensorMapper.cs  categoria, unidade, ids conhecidos, GpuIndexMatcher
  Sensors/Hardware/HardwareProvider.cs      LHM
  Sensors/Hardware/PawnIoInstaller.cs
  Sensors/Gpu/GpuEngineAggregator.cs        + uso por PID
  Sensors/Gpu/ProcessGpuUsage.cs
  Sensors/Gpu/GpuProvider.cs                publica uso por PID
  Sensors/Processes/SystemProcessParser.cs  RawProcessEntry, parse do buffer nativo
  Sensors/Processes/ProcessTracker.cs       ProcessSample, ProcessCpuCalculator, ProcessTracker
  Sensors/Processes/ProcessMonitor.cs
  Sensors/Processes/ProcessSorting.cs
  Settings/AppSettings.cs                   schema v2
  Settings/CardSettings.cs                  CardSettings, CardSize, ChartKind, AppTheme, BackdropKind, MetricThreshold
  Settings/ColorText.cs
  Settings/SettingsMigrator.cs              v1 → v2
  Settings/SettingsJsonContext.cs           enums como string
  Dashboard/DashboardEditor.cs
  Dashboard/ThresholdEvaluator.cs
  Dashboard/ChartSampler.cs
  Dashboard/MemoryComposition.cs
  Shell/RestartArguments.cs
  Shell/AutostartManager.cs                 timeout do schtasks
src/Supermonitor/
  Program.cs                                --wait-for-pid
  App.xaml.cs                               Restart, avisos únicos, log de exceções em background
  MainWindow.xaml(.cs)                      ícones, novas páginas, aparência, lease, banner PawnIO
  Appearance/AppearanceService.cs, AccentPalette.cs
  Controls/TimeSeriesChart.cs               substitui SparklineChart.cs
  Controls/CardWrapLayout.cs, CardMetrics.cs
  Controls/MetricCard.xaml(.cs)             tamanho, tipo de gráfico, limiar, menu, arraste
  Controls/MemoryCompositionBar.xaml(.cs)
  Dialogs/AddCardDialog.cs, ThresholdDialog.cs
  ViewModels/MetricCardViewModel.cs, ICardActions.cs, OverviewViewModel.cs
  ViewModels/CategoryViewModel.cs, MetricGroupViewModel.cs, ProcessRowViewModel.cs, ProcessesViewModel.cs
  Views/OverviewPage.xaml(.cs), CategoryPage.xaml(.cs), ProcessesPage.xaml(.cs), SettingsPage.xaml(.cs)
tests/Supermonitor.Tests/                   um arquivo por unidade, listado em cada task
```

---

### Task 1: Robustez do hub, log e campo Device

**Files:**
- Modify: `src/Supermonitor.Core/Metrics/MetricUnit.cs`, `src/Supermonitor.Core/Sensors/SensorHub.cs`, `src/Supermonitor.Core/Diagnostics/Log.cs`
- Modify: provedores `Cpu/CpuProvider.cs`, `Memory/MemoryProvider.cs`, `Gpu/GpuProvider.cs`, `Disk/DiskProvider.cs`, `Disk/DriveSpaceProvider.cs`, `Network/NetworkProvider.cs`, `Network/PingProvider.cs`
- Modify: `src/Supermonitor/App.xaml.cs`
- Test: `tests/Supermonitor.Tests/Sensors/SensorHubTests.cs`, `tests/Supermonitor.Tests/Sensors/FakeProvider.cs`, `tests/Supermonitor.Tests/Diagnostics/LogTests.cs`, `tests/Supermonitor.Tests/Hardware/ProviderSmokeTests.cs`

**Interfaces:**
- Produces: `sealed record MetricDescriptor(string Id, string DisplayName, MetricCategory Category, MetricUnit Unit, string? Device = null)`.
- Produces: `SensorHub.SampleReady` passa a ser `event Action` com lista copy-on-write; um handler que lança não impede os seguintes; falhas de handler são logadas no máximo uma vez por minuto.
- Produces: `SensorHub.Start()` não bloqueia: `InitializeProviders()` roda na thread do loop.
- Produces: valores de `Device`: CPU total `"CPU"`, núcleos `"CPU cores"`, memória `"Memory"`, GPU `GpuAdapterInfo.Name`, disco `"Disk n (X:)"`, drives `"Drives"`, rede por adaptador = alias, totais de rede `"Network"`, ping `"Latency"`.

- [ ] **Step 1: Testes que falham**

Em `tests/Supermonitor.Tests/Sensors/FakeProvider.cs`, adicionar a propriedade e a espera no `Initialize`:
```csharp
    public ManualResetEventSlim? InitializeGate { get; init; }
```
```csharp
    public void Initialize(MetricStore store)
    {
        InitializeGate?.Wait();
        if (FailInitialize)
        {
            throw new InvalidOperationException("no hardware");
        }

        _handle = store.Register(new MetricDescriptor(MetricId, Name, MetricCategory.System, MetricUnit.Count), Name);
    }
```

Adicionar em `SensorHubTests`:
```csharp
    [Fact]
    public void Throwing_SampleReady_handler_does_not_block_later_handlers()
    {
        var hub = CreateHub(new MetricStore(10), new FakeTimeProvider(), new FakeProvider("p"));
        var second = 0;
        hub.SampleReady += () => throw new InvalidOperationException("ui bug");
        hub.SampleReady += () => second++;

        hub.Tick();

        Assert.Equal(1, second);
    }

    [Fact]
    public void Unsubscribed_handler_is_not_called()
    {
        var hub = CreateHub(new MetricStore(10), new FakeTimeProvider(), new FakeProvider("p"));
        var calls = 0;
        Action handler = () => calls++;
        hub.SampleReady += handler;
        hub.SampleReady -= handler;

        hub.Tick();

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Start_returns_before_slow_provider_initialization_finishes()
    {
        using var gate = new ManualResetEventSlim();
        var provider = new FakeProvider("slow") { InitializeGate = gate };
        var hub = new SensorHub(new MetricStore(10), [provider], new FakeTimeProvider(), TimeSpan.FromSeconds(1));

        var started = Task.Run(hub.Start);
        var returned = started.Wait(TimeSpan.FromSeconds(2));
        gate.Set();
        await started;
        await hub.DisposeAsync();

        Assert.True(returned);
    }
```

`tests/Supermonitor.Tests/Diagnostics/LogTests.cs`:
```csharp
using Supermonitor.Core.Diagnostics;

namespace Supermonitor.Tests.Diagnostics;

public sealed class LogTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("smlog");

    public void Dispose() => _dir.Delete(recursive: true);

    [Fact]
    public void Initialize_skips_old_logs_it_cannot_delete()
    {
        var old = Path.Combine(_dir.FullName, "supermonitor-20200101.log");
        File.WriteAllText(old, "x");
        File.SetAttributes(old, FileAttributes.ReadOnly);

        var exception = Record.Exception(() => Log.Initialize(_dir.FullName));
        File.SetAttributes(old, FileAttributes.Normal);

        Assert.Null(exception);
    }
}
```

Em `ProviderSmokeTests`, no fim de `Cpu_provider_reports_total_and_per_core_usage`:
```csharp
        Assert.True(store.TryGetDescriptor(MetricIds.CpuUsage, out var descriptor));
        Assert.Equal("CPU", descriptor!.Device);
```
E no fim de `Gpu_provider_reports_usage_and_vram_for_first_gpu`:
```csharp
        Assert.True(store.TryGetDescriptor(MetricIds.GpuUsage(0), out var descriptor));
        Assert.False(string.IsNullOrWhiteSpace(descriptor!.Device));
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test --project tests/Supermonitor.Tests`
Expected: compila; falham `Throwing_SampleReady_handler_does_not_block_later_handlers` (segundo handler não chamado), `Start_returns_before_slow_provider_initialization_finishes` (`returned` falso), `Initialize_skips_old_logs_it_cannot_delete` (`UnauthorizedAccessException`); os dois smoke tests falham por `Device` nulo — se a compilação falhar porque `Device` não existe, isso também conta como RED.

- [ ] **Step 3: Implementar**

`src/Supermonitor.Core/Metrics/MetricUnit.cs` — trocar a linha do record:
```csharp
public sealed record MetricDescriptor(string Id, string DisplayName, MetricCategory Category, MetricUnit Unit, string? Device = null);
```

`src/Supermonitor.Core/Diagnostics/Log.cs` — trocar o corpo do `try` em `Initialize`:
```csharp
        var names = Directory.EnumerateFiles(directory).Select(Path.GetFileName).OfType<string>();
        foreach (var name in LogRetention.FilesToDelete(names, DateOnly.FromDateTime(DateTime.Now), LogRetention.KeepDays).ToArray())
        {
            try
            {
                File.Delete(Path.Combine(directory, name));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Arquivo antigo bloqueado ou somente leitura: fica para a próxima limpeza.
            }
        }
```
(remover o `try/catch (IOException)` externo; `Directory.EnumerateFiles` de um diretório recém-criado não falha).

`src/Supermonitor.Core/Sensors/SensorHub.cs` — campos, evento, `Start`, `RunAsync` e o final do `Tick`:
```csharp
    private readonly Lock _handlersGate = new();
    private Action[] _handlers = [];
    private bool _handlerErrorLogged;
    private long _lastHandlerError;

    public event Action SampleReady
    {
        add
        {
            lock (_handlersGate)
            {
                _handlers = [.. _handlers, value];
            }
        }

        remove
        {
            lock (_handlersGate)
            {
                var index = Array.IndexOf(_handlers, value);
                if (index >= 0)
                {
                    _handlers = [.. _handlers.AsSpan(0, index), .. _handlers.AsSpan(index + 1)];
                }
            }
        }
    }
```
```csharp
    public void Start()
    {
        _timer = new PeriodicTimer(_interval, _time);
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }
```
```csharp
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        InitializeProviders();
        Tick();
        try
        {
            while (await _timer!.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                Tick();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
```
No fim de `Tick`, substituir o bloco `try { SampleReady?.Invoke(); } catch ...` por:
```csharp
        foreach (var handler in Volatile.Read(ref _handlers))
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                LogHandlerFailure(ex);
            }
        }
    }

    private void LogHandlerFailure(Exception exception)
    {
        var now = _time.GetTimestamp();
        if (_handlerErrorLogged && _time.GetElapsedTime(_lastHandlerError, now) < TimeSpan.FromMinutes(1))
        {
            return;
        }

        _handlerErrorLogged = true;
        _lastHandlerError = now;
        Log.Error("SampleReady handler failed", exception);
    }
```
Remover a declaração antiga `public event Action? SampleReady;`.

Provedores — passar `Device` em cada `MetricDescriptor`:
- `CpuProvider`: total → `new MetricDescriptor(MetricIds.CpuUsage, "CPU usage", MetricCategory.Cpu, MetricUnit.Percent, "CPU")`; núcleos → mesmo construtor com `"CPU cores"` no fim.
- `MemoryProvider.Register`: `new MetricDescriptor(id, name, MetricCategory.Memory, unit, "Memory")`.
- `GpuProvider.Register`, função local `Add`: `new MetricDescriptor(id, $"{prefix} {name}", MetricCategory.Gpu, unit, info.Name)`.
- `DiskProvider`: o helper `Register` recebe `string device` e repassa; as quatro chamadas passam `title`.
- `DriveSpaceProvider.Register`: `new MetricDescriptor(id, name, MetricCategory.Disk, unit, "Drives")`.
- `NetworkProvider.Register` recebe `string device`: adaptadores passam `alias`; totais passam `"Network"`.
- `PingProvider`: os três descriptors recebem `"Latency"`.

`src/Supermonitor/App.xaml.cs` — no construtor, depois de `UnhandledException += ...`:
```csharp
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test --project tests/Supermonitor.Tests`
Expected: todos passam (unidade + hardware).

Run: `dotnet build src/Supermonitor/Supermonitor.csproj -c Debug -p:Platform=x64 -p:OutDir=<temp>\`
Expected: `0 Erro(s)`.

- [ ] **Step 5: Commit**

```powershell
git add src tests
git commit -m "feat(core): isolate SampleReady handlers, init providers off the UI thread, add metric device"
```

---

### Task 2: DemandTracker e provedores sob demanda

**Files:**
- Create: `src/Supermonitor.Core/Sensors/DemandTracker.cs`
- Modify: `src/Supermonitor.Core/Sensors/ISensorProvider.cs`, `src/Supermonitor.Core/Sensors/SensorHub.cs`
- Test: `tests/Supermonitor.Tests/Sensors/DemandTrackerTests.cs`, `SensorHubTests.cs`, `FakeProvider.cs`

**Interfaces:**
- Produces: `enum Demand { HardwareSensors, Processes }`; `DemandTracker` com `IDisposable Acquire(Demand)` (lease idempotente) e `bool IsActive(Demand)`.
- Produces: `ISensorProvider.RequiredDemand` — `Demand? RequiredDemand => null;` (membro padrão).
- Produces: `SensorHub(..., TimeSpan interval, DemandTracker? demand = null)`. Provedor com demanda inativa não é amostrado; ao ficar inativo suas métricas viram NaN uma vez; ao reativar, a primeira amostra recebe `elapsed = TimeSpan.Zero`.

- [ ] **Step 1: Testes que falham**

`tests/Supermonitor.Tests/Sensors/DemandTrackerTests.cs`:
```csharp
using Supermonitor.Core.Sensors;

namespace Supermonitor.Tests.Sensors;

public class DemandTrackerTests
{
    [Fact]
    public void Demand_is_active_while_a_lease_is_held()
    {
        var tracker = new DemandTracker();
        var lease = tracker.Acquire(Demand.Processes);

        Assert.True(tracker.IsActive(Demand.Processes));
        Assert.False(tracker.IsActive(Demand.HardwareSensors));

        lease.Dispose();
        Assert.False(tracker.IsActive(Demand.Processes));
    }

    [Fact]
    public void Demand_stays_active_until_every_lease_is_released()
    {
        var tracker = new DemandTracker();
        var first = tracker.Acquire(Demand.HardwareSensors);
        var second = tracker.Acquire(Demand.HardwareSensors);

        first.Dispose();
        Assert.True(tracker.IsActive(Demand.HardwareSensors));

        second.Dispose();
        Assert.False(tracker.IsActive(Demand.HardwareSensors));
    }

    [Fact]
    public void Disposing_a_lease_twice_releases_it_once()
    {
        var tracker = new DemandTracker();
        var lease = tracker.Acquire(Demand.Processes);
        lease.Dispose();
        lease.Dispose();

        using var other = tracker.Acquire(Demand.Processes);

        Assert.True(tracker.IsActive(Demand.Processes));
    }
}
```

Em `FakeProvider`, adicionar:
```csharp
    public Demand? RequiredDemand { get; init; }
```

Em `SensorHubTests`, adicionar `using Supermonitor.Core.Sensors;` (já existe) e:
```csharp
    [Fact]
    public void Provider_without_active_demand_is_not_sampled_and_reads_nan()
    {
        var store = new MetricStore(10);
        var demand = new DemandTracker();
        var provider = new FakeProvider("p") { RequiredDemand = Demand.Processes };
        var hub = new SensorHub(store, [provider], new FakeTimeProvider(), TimeSpan.FromSeconds(1), demand);
        hub.InitializeProviders();

        hub.Tick();

        Assert.Empty(provider.Elapsed);
        Assert.True(float.IsNaN(store.GetLatest(provider.MetricId)));
    }

    [Fact]
    public void Provider_samples_while_demand_is_active_and_restarts_elapsed_after_a_pause()
    {
        var store = new MetricStore(10);
        var demand = new DemandTracker();
        var time = new FakeTimeProvider();
        var provider = new FakeProvider("p") { RequiredDemand = Demand.Processes };
        var hub = new SensorHub(store, [provider], time, TimeSpan.FromSeconds(1), demand);
        hub.InitializeProviders();

        var lease = demand.Acquire(Demand.Processes);
        hub.Tick();
        time.Advance(TimeSpan.FromSeconds(1));
        hub.Tick();
        Assert.Equal(1, store.GetLatest(provider.MetricId));

        lease.Dispose();
        time.Advance(TimeSpan.FromSeconds(1));
        hub.Tick();
        Assert.True(float.IsNaN(store.GetLatest(provider.MetricId)));

        using var again = demand.Acquire(Demand.Processes);
        time.Advance(TimeSpan.FromSeconds(1));
        hub.Tick();

        Assert.Equal(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.Zero }, provider.Elapsed);
    }
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: erro de compilação `DemandTracker`/`Demand` não encontrados.

- [ ] **Step 3: Implementar**

`src/Supermonitor.Core/Sensors/DemandTracker.cs`:
```csharp
namespace Supermonitor.Core.Sensors;

public enum Demand
{
    HardwareSensors,
    Processes,
}

public sealed class DemandTracker
{
    private readonly int[] _counts = new int[Enum.GetValues<Demand>().Length];

    public IDisposable Acquire(Demand demand)
    {
        Interlocked.Increment(ref _counts[(int)demand]);
        return new Lease(this, demand);
    }

    public bool IsActive(Demand demand) => Volatile.Read(ref _counts[(int)demand]) > 0;

    private sealed class Lease(DemandTracker owner, Demand demand) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref owner._counts[(int)demand]);
            }
        }
    }
}
```

`src/Supermonitor.Core/Sensors/ISensorProvider.cs` — dentro da interface:
```csharp
    Demand? RequiredDemand => null;
```

`src/Supermonitor.Core/Sensors/SensorHub.cs`:
- campo `private readonly DemandTracker _demand;`
- construtor: `public SensorHub(MetricStore store, IEnumerable<ISensorProvider> providers, TimeProvider time, TimeSpan interval, DemandTracker? demand = null)` com `_demand = demand ?? new DemandTracker();`
- em `Tick`, logo depois de `if (!slot.Available) continue;`:
```csharp
            if (slot.Provider.RequiredDemand is { } required && !_demand.IsActive(required))
            {
                if (slot.HasSampled)
                {
                    _store.InvalidateOwner(slot.Provider.Name);
                    slot.HasSampled = false;
                }

                continue;
            }
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"` → todos passam.

- [ ] **Step 5: Commit**

```powershell
git add src/Supermonitor.Core/Sensors tests/Supermonitor.Tests/Sensors
git commit -m "feat(core): add demand tracker and demand-gated providers"
```

---

### Task 3: Mapeamento de sensores LHM

**Files:**
- Modify: `src/Supermonitor.Core/Supermonitor.Core.csproj`, `src/Supermonitor.Core/Metrics/MetricIds.cs`
- Create: `src/Supermonitor.Core/Sensors/Hardware/HardwareSensorMapper.cs`
- Test: `tests/Supermonitor.Tests/Sensors/HardwareSensorMapperTests.cs`

**Interfaces:**
- Produces: `MetricIds.CpuTemperature` (`"cpu.temp"`), `CpuPower` (`"cpu.power"`), `CpuClock` (`"cpu.clock"`), e por GPU `GpuTemperature(n)` (`"gpu{n}.temp"`), `GpuHotspot(n)` (`.hotspot`), `GpuMemoryTemperature(n)` (`.memtemp`), `GpuClock(n)` (`.clock`), `GpuMemoryClock(n)` (`.memclock`), `GpuPower(n)` (`.power`), `GpuFan(n)` (`.fan`), `GpuFanPercent(n)` (`.fanpct`).
- Produces: `HardwareSensorMapper.CategoryOf(HardwareType) -> MetricCategory?`, `UnitOf(SensorType) -> MetricUnit?`, `MetricId(string identifier) -> string` (`"hw" + identifier`), `WellKnown(HardwareType, SensorType, string name, int gpuIndex, int gpuCount) -> WellKnownMetric?`; `readonly record struct WellKnownMetric(string Id, string DisplayName)`.
- Produces: `GpuIndexMatcher.Match(IReadOnlyList<string> adapterNames, string name, ICollection<int> used) -> int`.

- [ ] **Step 1: Pacote**

Em `src/Supermonitor.Core/Supermonitor.Core.csproj`, adicionar:
```xml
  <ItemGroup>
    <PackageReference Include="LibreHardwareMonitorLib" Version="0.9.6" />
  </ItemGroup>
```

- [ ] **Step 2: Testes que falham**

`tests/Supermonitor.Tests/Sensors/HardwareSensorMapperTests.cs`:
```csharp
using LibreHardwareMonitor.Hardware;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors.Hardware;

namespace Supermonitor.Tests.Sensors;

public class HardwareSensorMapperTests
{
    [Theory]
    [InlineData(HardwareType.Cpu, MetricCategory.Cpu)]
    [InlineData(HardwareType.GpuAmd, MetricCategory.Gpu)]
    [InlineData(HardwareType.GpuNvidia, MetricCategory.Gpu)]
    [InlineData(HardwareType.GpuIntel, MetricCategory.Gpu)]
    [InlineData(HardwareType.Storage, MetricCategory.Disk)]
    [InlineData(HardwareType.Motherboard, MetricCategory.System)]
    [InlineData(HardwareType.SuperIO, MetricCategory.System)]
    public void Maps_hardware_types_to_categories(HardwareType type, MetricCategory expected)
    {
        Assert.Equal(expected, HardwareSensorMapper.CategoryOf(type));
    }

    [Theory]
    [InlineData(HardwareType.Memory)]
    [InlineData(HardwareType.Network)]
    [InlineData(HardwareType.Battery)]
    public void Ignores_hardware_already_covered_or_out_of_scope(HardwareType type)
    {
        Assert.Null(HardwareSensorMapper.CategoryOf(type));
    }

    [Theory]
    [InlineData(SensorType.Temperature, MetricUnit.Celsius)]
    [InlineData(SensorType.Clock, MetricUnit.Megahertz)]
    [InlineData(SensorType.Power, MetricUnit.Watts)]
    [InlineData(SensorType.Voltage, MetricUnit.Volts)]
    [InlineData(SensorType.Fan, MetricUnit.Rpm)]
    [InlineData(SensorType.Control, MetricUnit.Percent)]
    public void Maps_sensor_types_to_units(SensorType type, MetricUnit expected)
    {
        Assert.Equal(expected, HardwareSensorMapper.UnitOf(type));
    }

    [Theory]
    [InlineData(SensorType.Load)]
    [InlineData(SensorType.Data)]
    [InlineData(SensorType.SmallData)]
    [InlineData(SensorType.Factor)]
    [InlineData(SensorType.Throughput)]
    public void Ignores_sensor_types_covered_elsewhere(SensorType type)
    {
        Assert.Null(HardwareSensorMapper.UnitOf(type));
    }

    [Fact]
    public void Metric_id_prefixes_lhm_identifier()
    {
        Assert.Equal("hw/amdcpu/0/temperature/2", HardwareSensorMapper.MetricId("/amdcpu/0/temperature/2"));
    }

    [Theory]
    [InlineData(HardwareType.Cpu, SensorType.Temperature, "Core (Tctl/Tdie)", "cpu.temp", "CPU temperature")]
    [InlineData(HardwareType.Cpu, SensorType.Temperature, "CPU Package", "cpu.temp", "CPU temperature")]
    [InlineData(HardwareType.Cpu, SensorType.Power, "Package", "cpu.power", "CPU power")]
    [InlineData(HardwareType.Cpu, SensorType.Clock, "Cores (Average)", "cpu.clock", "CPU clock")]
    [InlineData(HardwareType.GpuAmd, SensorType.Temperature, "GPU Core", "gpu0.temp", "GPU temperature")]
    [InlineData(HardwareType.GpuAmd, SensorType.Temperature, "GPU Hot Spot", "gpu0.hotspot", "GPU hot spot")]
    [InlineData(HardwareType.GpuAmd, SensorType.Temperature, "GPU Memory", "gpu0.memtemp", "GPU memory temperature")]
    [InlineData(HardwareType.GpuAmd, SensorType.Clock, "GPU Core", "gpu0.clock", "GPU clock")]
    [InlineData(HardwareType.GpuAmd, SensorType.Clock, "GPU Memory", "gpu0.memclock", "GPU memory clock")]
    [InlineData(HardwareType.GpuAmd, SensorType.Power, "GPU Package", "gpu0.power", "GPU power")]
    [InlineData(HardwareType.GpuIntel, SensorType.Power, "GPU Power", "gpu0.power", "GPU power")]
    [InlineData(HardwareType.GpuNvidia, SensorType.Fan, "GPU Fan", "gpu0.fan", "GPU fan")]
    [InlineData(HardwareType.GpuNvidia, SensorType.Control, "GPU Fan", "gpu0.fanpct", "GPU fan speed")]
    public void Recognizes_well_known_sensors(HardwareType hardware, SensorType sensor, string name, string expectedId, string expectedName)
    {
        var metric = HardwareSensorMapper.WellKnown(hardware, sensor, name, gpuIndex: 0, gpuCount: 1);

        Assert.NotNull(metric);
        Assert.Equal(expectedId, metric.Value.Id);
        Assert.Equal(expectedName, metric.Value.DisplayName);
    }

    [Fact]
    public void Well_known_gpu_names_carry_index_when_there_are_several_gpus()
    {
        var metric = HardwareSensorMapper.WellKnown(HardwareType.GpuNvidia, SensorType.Temperature, "GPU Core", gpuIndex: 1, gpuCount: 2);

        Assert.Equal(new WellKnownMetric("gpu1.temp", "GPU 1 temperature"), metric);
    }

    [Theory]
    [InlineData(HardwareType.Cpu, SensorType.Temperature, "CCD1 (Tdie)")]
    [InlineData(HardwareType.Motherboard, SensorType.Temperature, "GPU Core")]
    [InlineData(HardwareType.GpuAmd, SensorType.Voltage, "GPU Core")]
    public void Other_sensors_are_not_well_known(HardwareType hardware, SensorType sensor, string name)
    {
        Assert.Null(HardwareSensorMapper.WellKnown(hardware, sensor, name, 0, 1));
    }

    [Fact]
    public void Gpu_matcher_prefers_same_name_then_first_unused()
    {
        string[] adapters = ["AMD Radeon RX 9070 XT", "Intel UHD"];
        var used = new List<int>();

        Assert.Equal(1, GpuIndexMatcher.Match(adapters, "intel uhd", used));
        used.Add(1);
        Assert.Equal(0, GpuIndexMatcher.Match(adapters, "Unknown GPU", used));
        used.Add(0);
        Assert.Equal(2, GpuIndexMatcher.Match(adapters, "Extra GPU", used));
    }

    [Fact]
    public void Gpu_matcher_assigns_identical_cards_in_order()
    {
        string[] adapters = ["RTX 4090", "RTX 4090"];
        var used = new List<int>();

        var first = GpuIndexMatcher.Match(adapters, "RTX 4090", used);
        used.Add(first);
        var second = GpuIndexMatcher.Match(adapters, "RTX 4090", used);

        Assert.Equal((0, 1), (first, second));
    }
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: erro de compilação `HardwareSensorMapper` não encontrado.

- [ ] **Step 4: Implementar**

Em `MetricIds.cs`, adicionar:
```csharp
    public const string CpuTemperature = "cpu.temp";
    public const string CpuPower = "cpu.power";
    public const string CpuClock = "cpu.clock";

    public static string GpuTemperature(int gpu) => $"gpu{gpu}.temp";

    public static string GpuHotspot(int gpu) => $"gpu{gpu}.hotspot";

    public static string GpuMemoryTemperature(int gpu) => $"gpu{gpu}.memtemp";

    public static string GpuClock(int gpu) => $"gpu{gpu}.clock";

    public static string GpuMemoryClock(int gpu) => $"gpu{gpu}.memclock";

    public static string GpuPower(int gpu) => $"gpu{gpu}.power";

    public static string GpuFan(int gpu) => $"gpu{gpu}.fan";

    public static string GpuFanPercent(int gpu) => $"gpu{gpu}.fanpct";
```

`src/Supermonitor.Core/Sensors/Hardware/HardwareSensorMapper.cs`:
```csharp
using LibreHardwareMonitor.Hardware;
using Supermonitor.Core.Metrics;

namespace Supermonitor.Core.Sensors.Hardware;

public readonly record struct WellKnownMetric(string Id, string DisplayName);

public static class HardwareSensorMapper
{
    public static MetricCategory? CategoryOf(HardwareType type) => type switch
    {
        HardwareType.Cpu => MetricCategory.Cpu,
        HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel => MetricCategory.Gpu,
        HardwareType.Storage => MetricCategory.Disk,
        HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController or HardwareType.Cooler or HardwareType.Psu => MetricCategory.System,
        _ => null,
    };

    // Carga, memória e tráfego já vêm de PDH/Win32; aqui só o que o Windows não expõe.
    public static MetricUnit? UnitOf(SensorType type) => type switch
    {
        SensorType.Temperature => MetricUnit.Celsius,
        SensorType.Clock => MetricUnit.Megahertz,
        SensorType.Power => MetricUnit.Watts,
        SensorType.Voltage => MetricUnit.Volts,
        SensorType.Fan => MetricUnit.Rpm,
        SensorType.Control => MetricUnit.Percent,
        _ => null,
    };

    public static string MetricId(string identifier) => "hw" + identifier;

    public static WellKnownMetric? WellKnown(HardwareType hardware, SensorType sensor, string name, int gpuIndex, int gpuCount)
    {
        var gpu = gpuCount > 1 ? $"GPU {gpuIndex}" : "GPU";
        return (hardware, sensor, name) switch
        {
            (HardwareType.Cpu, SensorType.Temperature, "Core (Tctl/Tdie)" or "CPU Package") => new(MetricIds.CpuTemperature, "CPU temperature"),
            (HardwareType.Cpu, SensorType.Power, "Package" or "CPU Package") => new(MetricIds.CpuPower, "CPU power"),
            (HardwareType.Cpu, SensorType.Clock, "Cores (Average)") => new(MetricIds.CpuClock, "CPU clock"),
            _ when !IsGpu(hardware) => null,
            (_, SensorType.Temperature, "GPU Core") => new(MetricIds.GpuTemperature(gpuIndex), $"{gpu} temperature"),
            (_, SensorType.Temperature, "GPU Hot Spot") => new(MetricIds.GpuHotspot(gpuIndex), $"{gpu} hot spot"),
            (_, SensorType.Temperature, "GPU Memory") => new(MetricIds.GpuMemoryTemperature(gpuIndex), $"{gpu} memory temperature"),
            (_, SensorType.Clock, "GPU Core") => new(MetricIds.GpuClock(gpuIndex), $"{gpu} clock"),
            (_, SensorType.Clock, "GPU Memory") => new(MetricIds.GpuMemoryClock(gpuIndex), $"{gpu} memory clock"),
            (_, SensorType.Power, "GPU Package" or "GPU Power") => new(MetricIds.GpuPower(gpuIndex), $"{gpu} power"),
            (_, SensorType.Fan, "GPU Fan") => new(MetricIds.GpuFan(gpuIndex), $"{gpu} fan"),
            (_, SensorType.Control, "GPU Fan") => new(MetricIds.GpuFanPercent(gpuIndex), $"{gpu} fan speed"),
            _ => null,
        };
    }

    public static bool IsGpu(HardwareType type) =>
        type is HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel;
}

public static class GpuIndexMatcher
{
    // Alinha a GPU do LHM com o índice DXGI usado pelo GpuProvider (gpu{n}).
    public static int Match(IReadOnlyList<string> adapterNames, string name, ICollection<int> used)
    {
        for (var i = 0; i < adapterNames.Count; i++)
        {
            if (!used.Contains(i) && string.Equals(adapterNames[i].Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        for (var i = 0; i < adapterNames.Count; i++)
        {
            if (!used.Contains(i))
            {
                return i;
            }
        }

        var next = adapterNames.Count;
        while (used.Contains(next))
        {
            next++;
        }

        return next;
    }
}
```

- [ ] **Step 5: Rodar e ver passar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"` → todos passam.

- [ ] **Step 6: Commit**

```powershell
git add src/Supermonitor.Core tests/Supermonitor.Tests/Sensors/HardwareSensorMapperTests.cs
git commit -m "feat(sensors): map LibreHardwareMonitor sensors to metrics and well-known ids"
```

---

### Task 4: HardwareProvider e demanda da janela

**Files:**
- Create: `src/Supermonitor.Core/Sensors/Hardware/HardwareProvider.cs`
- Modify: `src/Supermonitor.Core/AppHost.cs`, `src/Supermonitor/MainWindow.xaml.cs`
- Test: `tests/Supermonitor.Tests/Hardware/ProviderSmokeTests.cs`

**Interfaces:**
- Consumes: `HardwareSensorMapper`, `GpuIndexMatcher` (Task 3), `Demand` (Task 2), `DxgiAdapters`/`GpuAdapterFilter` (Plano 1).
- Produces: `HardwareProvider(Func<IReadOnlyList<string>> gpuNames)`, `Name = "Hardware sensors"`, `RequiredDemand = Demand.HardwareSensors`, `static bool IsPawnIoInstalled`. Sensor com id conhecido é registrado só com o id conhecido; os demais com `hw/<identifier>`. Sensores `IsDefaultHidden` são ignorados.
- Produces: `AppHost.Demand` (`DemandTracker`), `static IReadOnlyList<string> AppHost.MonitoredGpuNames()`.
- Produces: `MainWindow` segura `Demand.HardwareSensors` enquanto existe e não está minimizada.

- [ ] **Step 1: Teste de hardware que falha**

Em `ProviderSmokeTests`, adicionar `using Supermonitor.Core;` e `using Supermonitor.Core.Sensors.Hardware;` e:
```csharp
    [Fact]
    public void Hardware_provider_reports_gpu_temperature()
    {
        using var provider = new HardwareProvider(AppHost.MonitoredGpuNames);
        var store = SampleTwice(provider);

        Assert.InRange(store.GetLatest(MetricIds.GpuTemperature(0)), 1, 120);
        Assert.True(store.TryGetDescriptor(MetricIds.GpuTemperature(0), out var descriptor));
        Assert.False(string.IsNullOrWhiteSpace(descriptor!.Device));
    }
```

Run: `dotnet test --project tests/Supermonitor.Tests --filter-trait "Category=Hardware"`
Expected: erro de compilação `HardwareProvider` não encontrado.

- [ ] **Step 2: Implementar o provedor**

`src/Supermonitor.Core/Sensors/Hardware/HardwareProvider.cs`:
```csharp
using LibreHardwareMonitor.Hardware;
using Supermonitor.Core.Metrics;

namespace Supermonitor.Core.Sensors.Hardware;

public sealed class HardwareProvider(Func<IReadOnlyList<string>> gpuNames) : ISensorProvider
{
    private Computer? _computer;
    private IHardware[] _hardware = [];
    private Entry[] _entries = [];

    public static bool IsPawnIoInstalled => LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;

    public string Name => "Hardware sensors";

    public TimeSpan MinInterval => TimeSpan.Zero;

    public Demand? RequiredDemand => Demand.HardwareSensors;

    public void Initialize(MetricStore store)
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
            IsControllerEnabled = true,
        };
        _computer.Open();

        var adapterNames = gpuNames();
        var usedGpus = new List<int>();
        var usedAliases = new HashSet<string>(StringComparer.Ordinal);
        var hardware = new List<IHardware>();
        var entries = new List<Entry>();

        foreach (var top in _computer.Hardware)
        {
            var category = HardwareSensorMapper.CategoryOf(top.HardwareType);
            var all = new List<IHardware> { top };
            all.AddRange(top.SubHardware);
            hardware.AddRange(all);
            foreach (var item in all)
            {
                item.Update();
            }

            if (category is null)
            {
                continue;
            }

            var gpuIndex = -1;
            if (HardwareSensorMapper.IsGpu(top.HardwareType))
            {
                gpuIndex = GpuIndexMatcher.Match(adapterNames, top.Name, usedGpus);
                usedGpus.Add(gpuIndex);
            }

            foreach (var sensor in all.SelectMany(h => h.Sensors))
            {
                var unit = HardwareSensorMapper.UnitOf(sensor.SensorType);
                if (sensor.IsDefaultHidden || unit is null)
                {
                    continue;
                }

                var wellKnown = HardwareSensorMapper.WellKnown(top.HardwareType, sensor.SensorType, sensor.Name, gpuIndex, Math.Max(adapterNames.Count, usedGpus.Count));
                var descriptor = wellKnown is { } known && usedAliases.Add(known.Id)
                    ? new MetricDescriptor(known.Id, known.DisplayName, category.Value, unit.Value, top.Name)
                    : new MetricDescriptor(HardwareSensorMapper.MetricId(sensor.Identifier.ToString()), sensor.Name, category.Value, unit.Value, top.Name);
                entries.Add(new Entry(sensor, store.Register(descriptor, Name)));
            }
        }

        _hardware = hardware.ToArray();
        _entries = entries.ToArray();
    }

    public void Sample(MetricStore store, TimeSpan elapsed)
    {
        foreach (var item in _hardware)
        {
            item.Update();
        }

        foreach (var entry in _entries)
        {
            store.Write(entry.Handle, entry.Sensor.Value ?? float.NaN);
        }
    }

    public void Dispose() => _computer?.Close();

    private sealed record Entry(ISensor Sensor, MetricHandle Handle);
}
```

- [ ] **Step 3: Composição**

Em `AppHost.cs`:
- `using Supermonitor.Core.Sensors.Hardware;`
- propriedade `public DemandTracker Demand { get; }` (no construtor privado, novo parâmetro `DemandTracker demand`).
- método:
```csharp
    public static IReadOnlyList<string> MonitoredGpuNames() =>
        DxgiAdapters.Enumerate()
            .Where(a => GpuAdapterFilter.IsMonitored(a.Flags, a.DedicatedVideoMemory))
            .Select(a => a.Name)
            .ToArray();
```
- em `Create`: `var demand = new DemandTracker();`, inserir `new HardwareProvider(MonitoredGpuNames),` logo depois de `new GpuProvider(),` e criar o hub com `new SensorHub(metrics, providers, TimeProvider.System, TimeSpan.FromMilliseconds(settings.Current.SampleIntervalMs), demand)`; passar `demand` ao construtor.

Em `src/Supermonitor/MainWindow.xaml.cs`:
```csharp
    private IDisposable? _sensorLease;
```
No fim do construtor:
```csharp
        Closed += (_, _) => ReleaseSensorLease();
        UpdateSensorLease();
```
Em `OnAppWindowChanged`, dentro do `if (minimized != _lastMinimized)`, depois de `SetEfficiencyMode`, chamar `UpdateSensorLease();`. Métodos:
```csharp
    private void UpdateSensorLease()
    {
        if (IsMinimized)
        {
            ReleaseSensorLease();
        }
        else
        {
            _sensorLease ??= _host.Demand.Acquire(Demand.HardwareSensors);
        }
    }

    private void ReleaseSensorLease()
    {
        _sensorLease?.Dispose();
        _sensorLease = null;
    }
```
(`using Supermonitor.Core.Sensors;`).

- [ ] **Step 4: Rodar testes e build**

Run: `dotnet test --project tests/Supermonitor.Tests` → todos passam (o teste de hardware lê a temperatura da RX 9070 XT sem elevação).
Run: build do app com `-p:OutDir=<temp>\` → `0 Erro(s)`.

- [ ] **Step 5: Commit**

```powershell
git add src tests
git commit -m "feat(sensors): add LibreHardwareMonitor provider gated by dashboard visibility"
```

---

### Task 5: Processos e uso de GPU por processo

**Files:**
- Modify: `src/Supermonitor.Core/Sensors/Gpu/GpuEngineAggregator.cs`, `src/Supermonitor.Core/Sensors/Gpu/GpuProvider.cs`, `src/Supermonitor.Core/Interop/Ntdll.cs`, `src/Supermonitor.Core/AppHost.cs`
- Create: `src/Supermonitor.Core/Sensors/Gpu/ProcessGpuUsage.cs`, `src/Supermonitor.Core/Sensors/Processes/SystemProcessParser.cs`, `ProcessTracker.cs`, `ProcessMonitor.cs`
- Test: `tests/Supermonitor.Tests/Sensors/GpuEngineAggregatorTests.cs`, `SystemProcessParserTests.cs`, `ProcessTrackerTests.cs`; `Hardware/ProviderSmokeTests.cs`

**Interfaces:**
- Produces: `GpuEngineAggregator.CopyProcessUsage(Dictionary<int, float> target)` (uso por PID = maior engine, limitado a 100).
- Produces: `ProcessGpuUsage` com `Publish(GpuEngineAggregator)` e `float Get(int pid)` (0 se ausente); `GpuProvider(ProcessGpuUsage? processUsage = null)`.
- Produces: `readonly record struct RawProcessEntry(int ProcessId, long CreateTime, long CpuTime, long PrivateWorkingSet, nint NamePointer, int NameChars)`; `SystemProcessParser.EntrySize` (0x100), `Parse(byte* buffer, int length, List<RawProcessEntry> output)`, `string? ReadName(RawProcessEntry)`.
- Produces: `readonly record struct ProcessSample(int ProcessId, string Name, float CpuPercent, long PrivateBytes, float GpuPercent)`; `ProcessCpuCalculator.Percent(long previous, long current, TimeSpan elapsed, int processorCount) -> double`; `ProcessTracker.Update(IReadOnlyList<RawProcessEntry>, TimeSpan elapsed, int processorCount, Func<RawProcessEntry, string> resolveName, Func<int, float> gpuPercent) -> ProcessSample[]`.
- Produces: `ProcessMonitor(ProcessGpuUsage)` com `Name = "Processes"`, `RequiredDemand = Demand.Processes`, `IReadOnlyList<ProcessSample> Snapshot`; `AppHost.Processes`.

- [ ] **Step 1: Testes que falham**

Em `GpuEngineAggregatorTests`, adicionar:
```csharp
    [Fact]
    public void Per_process_usage_takes_the_busiest_engine_of_each_pid()
    {
        var aggregator = new GpuEngineAggregator([Luid]);
        aggregator.Add(Instance(100, 0, "3D"), 30);
        aggregator.Add(Instance(100, 1, "Copy"), 10);
        aggregator.Add(Instance(200, 0, "3D"), 25);
        aggregator.Add(Instance(300, 0, "3D", "0x00000000_0x0000AAAA"), 90);
        var usage = new Dictionary<int, float>();

        aggregator.CopyProcessUsage(usage);

        Assert.Equal(30, usage[100]);
        Assert.Equal(25, usage[200]);
        Assert.False(usage.ContainsKey(300));
    }

    [Fact]
    public void Reset_clears_per_process_usage()
    {
        var aggregator = new GpuEngineAggregator([Luid]);
        aggregator.Add(Instance(100, 0, "3D"), 30);
        aggregator.Reset();
        var usage = new Dictionary<int, float>();

        aggregator.CopyProcessUsage(usage);

        Assert.Empty(usage);
    }
```

`tests/Supermonitor.Tests/Sensors/SystemProcessParserTests.cs`:
```csharp
using Supermonitor.Core.Sensors.Processes;

namespace Supermonitor.Tests.Sensors;

public unsafe class SystemProcessParserTests
{
    private const int Entry = SystemProcessParser.EntrySize;

    [Fact]
    public void Parses_entries_and_follows_next_offsets()
    {
        var buffer = GC.AllocateArray<byte>(Entry * 2 + 64, pinned: true);
        fixed (byte* p = buffer)
        {
            const string name = "game.exe";
            var nameOffset = Entry * 2;
            fixed (char* n = name)
            {
                Buffer.MemoryCopy(n, p + nameOffset, 64, name.Length * 2);
            }

            WriteEntry(p, next: (uint)Entry, pid: 0, create: 0, user: 0, kernel: 0, privateBytes: 0, nameChars: 0, namePointer: 0);
            WriteEntry(p + Entry, next: 0, pid: 1234, create: 99, user: 300, kernel: 200, privateBytes: 4096, nameChars: name.Length, namePointer: (nint)(p + nameOffset));
            var output = new List<RawProcessEntry>();

            SystemProcessParser.Parse(p, buffer.Length, output);

            Assert.Equal(2, output.Count);
            var entry = output[1];
            Assert.Equal(1234, entry.ProcessId);
            Assert.Equal(99, entry.CreateTime);
            Assert.Equal(500, entry.CpuTime);
            Assert.Equal(4096, entry.PrivateWorkingSet);
            Assert.Equal("game.exe", SystemProcessParser.ReadName(entry));
            Assert.Null(SystemProcessParser.ReadName(output[0]));
        }
    }

    [Fact]
    public void Stops_when_next_offset_points_past_the_buffer()
    {
        var buffer = GC.AllocateArray<byte>(Entry, pinned: true);
        fixed (byte* p = buffer)
        {
            WriteEntry(p, next: (uint)Entry, pid: 7, create: 1, user: 0, kernel: 0, privateBytes: 0, nameChars: 0, namePointer: 0);
            var output = new List<RawProcessEntry>();

            SystemProcessParser.Parse(p, buffer.Length, output);

            Assert.Single(output);
        }
    }

    private static void WriteEntry(byte* e, uint next, int pid, long create, long user, long kernel, long privateBytes, int nameChars, nint namePointer)
    {
        *(uint*)e = next;
        *(long*)(e + 0x08) = privateBytes;
        *(long*)(e + 0x20) = create;
        *(long*)(e + 0x28) = user;
        *(long*)(e + 0x30) = kernel;
        *(ushort*)(e + 0x38) = (ushort)(nameChars * 2);
        *(ushort*)(e + 0x3A) = (ushort)(nameChars * 2);
        *(nint*)(e + 0x40) = namePointer;
        *(nint*)(e + 0x50) = pid;
    }
}
```

`tests/Supermonitor.Tests/Sensors/ProcessTrackerTests.cs`:
```csharp
using Supermonitor.Core.Sensors.Processes;

namespace Supermonitor.Tests.Sensors;

public class ProcessTrackerTests
{
    private static RawProcessEntry Raw(int pid, long create, long cpu, long privateBytes = 0) => new(pid, create, cpu, privateBytes, 0, 0);

    private static ProcessSample[] Update(ProcessTracker tracker, TimeSpan elapsed, params RawProcessEntry[] raw) =>
        tracker.Update(raw, elapsed, processorCount: 2, resolveName: e => $"p{e.ProcessId}", gpuPercent: pid => pid == 10 ? 12 : 0);

    [Fact]
    public void Cpu_percent_uses_delta_since_previous_sample_across_all_cores()
    {
        var tracker = new ProcessTracker();
        Update(tracker, TimeSpan.Zero, Raw(10, 1, 0));

        var samples = Update(tracker, TimeSpan.FromSeconds(1), Raw(10, 1, 5_000_000, 2048));

        var sample = Assert.Single(samples);
        Assert.Equal(25, sample.CpuPercent, 3);
        Assert.Equal(2048, sample.PrivateBytes);
        Assert.Equal(12, sample.GpuPercent);
        Assert.Equal("p10", sample.Name);
    }

    [Fact]
    public void First_sample_of_a_process_has_unknown_cpu()
    {
        var samples = Update(new ProcessTracker(), TimeSpan.FromSeconds(1), Raw(10, 1, 100));

        Assert.True(float.IsNaN(samples[0].CpuPercent));
    }

    [Fact]
    public void Reused_pid_with_new_create_time_is_a_new_process()
    {
        var tracker = new ProcessTracker();
        Update(tracker, TimeSpan.Zero, Raw(10, 1, 0));

        var samples = Update(tracker, TimeSpan.FromSeconds(1), Raw(10, 2, 9_000_000));

        Assert.True(float.IsNaN(samples[0].CpuPercent));
    }

    [Fact]
    public void Idle_pid_zero_is_skipped_and_exited_processes_disappear()
    {
        var tracker = new ProcessTracker();
        Update(tracker, TimeSpan.Zero, Raw(0, 0, 0), Raw(10, 1, 0), Raw(20, 1, 0));

        var samples = Update(tracker, TimeSpan.FromSeconds(1), Raw(0, 0, 0), Raw(20, 1, 0));

        Assert.Equal(new[] { 20 }, samples.Select(s => s.ProcessId));
    }

    [Fact]
    public void Name_is_resolved_once_per_process()
    {
        var tracker = new ProcessTracker();
        var calls = 0;
        string Resolve(RawProcessEntry e)
        {
            calls++;
            return "x";
        }

        tracker.Update([Raw(10, 1, 0)], TimeSpan.Zero, 1, Resolve, _ => 0);
        tracker.Update([Raw(10, 1, 0)], TimeSpan.FromSeconds(1), 1, Resolve, _ => 0);

        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(0, 10_000_000, 1, 1, 100)]
    [InlineData(0, 40_000_000, 1, 4, 100)]
    [InlineData(100, 50, 1, 1, 0)]
    public void Calculator_clamps_to_valid_range(long previous, long current, int seconds, int cores, double expected)
    {
        Assert.Equal(expected, ProcessCpuCalculator.Percent(previous, current, TimeSpan.FromSeconds(seconds), cores), 3);
    }

    [Fact]
    public void Calculator_returns_nan_without_elapsed_time()
    {
        Assert.True(double.IsNaN(ProcessCpuCalculator.Percent(0, 10, TimeSpan.Zero, 1)));
    }
}
```

Em `ProviderSmokeTests` (`using Supermonitor.Core.Sensors.Gpu;` já existe; adicionar `using Supermonitor.Core.Sensors.Processes;`):
```csharp
    [Fact]
    public void Process_monitor_lists_this_process_with_cpu_usage()
    {
        using var provider = new ProcessMonitor(new ProcessGpuUsage());
        SampleTwice(provider);

        var self = Assert.Single(provider.Snapshot, p => p.ProcessId == Environment.ProcessId);
        Assert.True(float.IsFinite(self.CpuPercent));
        Assert.True(self.PrivateBytes > 0);
    }
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: erro de compilação (`CopyProcessUsage`, `SystemProcessParser`, `ProcessTracker` não encontrados).

- [ ] **Step 3: Implementar agregação por PID**

Em `GpuEngineAggregator.cs`:
- campo `private readonly Dictionary<int, double> _processes = [];`
- em `Reset()`, adicionar `_processes.Clear();`
- em `Add`, depois de `_matched[adapter] = true;`:
```csharp
        if (TryParsePid(instanceName, out var pid))
        {
            _processes[pid] = Math.Max(_processes.GetValueOrDefault(pid), value);
        }
```
- métodos:
```csharp
    public void CopyProcessUsage(Dictionary<int, float> target)
    {
        foreach (var (pid, value) in _processes)
        {
            target[pid] = (float)Math.Min(value, 100);
        }
    }

    private static bool TryParsePid(ReadOnlySpan<char> name, out int pid)
    {
        pid = 0;
        if (!name.StartsWith("pid_"))
        {
            return false;
        }

        var rest = name[4..];
        var end = rest.IndexOf('_');
        return end > 0 && int.TryParse(rest[..end], NumberStyles.None, CultureInfo.InvariantCulture, out pid);
    }
```

`src/Supermonitor.Core/Sensors/Gpu/ProcessGpuUsage.cs`:
```csharp
namespace Supermonitor.Core.Sensors.Gpu;

public sealed class ProcessGpuUsage
{
    private readonly Lock _gate = new();
    private Dictionary<int, float> _current = [];
    private Dictionary<int, float> _spare = [];

    public void Publish(GpuEngineAggregator aggregator)
    {
        _spare.Clear();
        aggregator.CopyProcessUsage(_spare);
        lock (_gate)
        {
            (_current, _spare) = (_spare, _current);
        }
    }

    public float Get(int processId)
    {
        lock (_gate)
        {
            return _current.GetValueOrDefault(processId);
        }
    }
}
```

`GpuProvider`: virar `public sealed class GpuProvider(ProcessGpuUsage? processUsage = null) : ISensorProvider` e, em `Sample`, logo depois do laço que alimenta o agregador: `processUsage?.Publish(_aggregator);`.

- [ ] **Step 4: Implementar parser, tracker e monitor**

Em `Ntdll.cs`, adicionar `public const int SystemProcessInformation = 5;`.

`src/Supermonitor.Core/Sensors/Processes/SystemProcessParser.cs`:
```csharp
namespace Supermonitor.Core.Sensors.Processes;

public readonly record struct RawProcessEntry(int ProcessId, long CreateTime, long CpuTime, long PrivateWorkingSet, nint NamePointer, int NameChars);

// Layout x64 de SYSTEM_PROCESS_INFORMATION (0x100 bytes, seguido pelas threads).
public static unsafe class SystemProcessParser
{
    public const int EntrySize = 0x100;

    public static void Parse(byte* buffer, int length, List<RawProcessEntry> output)
    {
        output.Clear();
        var offset = 0;
        while (offset >= 0 && offset + EntrySize <= length)
        {
            var e = buffer + offset;
            output.Add(new RawProcessEntry(
                (int)*(nint*)(e + 0x50),
                *(long*)(e + 0x20),
                *(long*)(e + 0x28) + *(long*)(e + 0x30),
                *(long*)(e + 0x08),
                *(nint*)(e + 0x40),
                *(ushort*)(e + 0x38) / 2));

            var next = *(uint*)e;
            if (next == 0)
            {
                break;
            }

            offset += (int)next;
        }
    }

    public static string? ReadName(RawProcessEntry entry) =>
        entry.NamePointer == 0 || entry.NameChars == 0 ? null : new string((char*)entry.NamePointer, 0, entry.NameChars);
}
```

`src/Supermonitor.Core/Sensors/Processes/ProcessTracker.cs`:
```csharp
namespace Supermonitor.Core.Sensors.Processes;

public readonly record struct ProcessSample(int ProcessId, string Name, float CpuPercent, long PrivateBytes, float GpuPercent);

public static class ProcessCpuCalculator
{
    // Tempos em unidades de 100 ns; percentual sobre todos os núcleos lógicos (igual ao Gerenciador de Tarefas).
    public static double Percent(long previous, long current, TimeSpan elapsed, int processorCount)
    {
        if (elapsed <= TimeSpan.Zero || processorCount <= 0)
        {
            return double.NaN;
        }

        var delta = Math.Max(0, current - previous);
        return Math.Clamp(delta * 100.0 / (elapsed.Ticks * (double)processorCount), 0, 100);
    }
}

public sealed class ProcessTracker
{
    private Dictionary<(int Pid, long Create), Tracked> _tracked = [];
    private Dictionary<(int Pid, long Create), Tracked> _next = [];

    public ProcessSample[] Update(
        IReadOnlyList<RawProcessEntry> raw,
        TimeSpan elapsed,
        int processorCount,
        Func<RawProcessEntry, string> resolveName,
        Func<int, float> gpuPercent)
    {
        var samples = new ProcessSample[raw.Count];
        var count = 0;
        _next.Clear();
        foreach (var entry in raw)
        {
            if (entry.ProcessId == 0)
            {
                continue;
            }

            var key = (entry.ProcessId, entry.CreateTime);
            var cpu = float.NaN;
            string name;
            if (_tracked.TryGetValue(key, out var previous))
            {
                cpu = (float)ProcessCpuCalculator.Percent(previous.CpuTime, entry.CpuTime, elapsed, processorCount);
                name = previous.Name;
            }
            else
            {
                name = resolveName(entry);
            }

            _next[key] = new Tracked(entry.CpuTime, name);
            samples[count++] = new ProcessSample(entry.ProcessId, name, cpu, entry.PrivateWorkingSet, gpuPercent(entry.ProcessId));
        }

        (_tracked, _next) = (_next, _tracked);
        Array.Resize(ref samples, count);
        return samples;
    }

    private readonly record struct Tracked(long CpuTime, string Name);
}
```

`src/Supermonitor.Core/Sensors/Processes/ProcessMonitor.cs`:
```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;
using Supermonitor.Core.Interop;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors.Gpu;

namespace Supermonitor.Core.Sensors.Processes;

public sealed unsafe class ProcessMonitor(ProcessGpuUsage gpuUsage) : ISensorProvider
{
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    private readonly ProcessTracker _tracker = new();
    private readonly List<RawProcessEntry> _raw = [];
    private byte* _buffer;
    private int _bufferSize;
    private ProcessSample[] _snapshot = [];

    public string Name => "Processes";

    public TimeSpan MinInterval => TimeSpan.Zero;

    public Demand? RequiredDemand => Demand.Processes;

    public IReadOnlyList<ProcessSample> Snapshot => Volatile.Read(ref _snapshot);

    public void Initialize(MetricStore store)
    {
    }

    public void Sample(MetricStore store, TimeSpan elapsed)
    {
        Query();
        var samples = _tracker.Update(
            _raw,
            elapsed,
            Environment.ProcessorCount,
            e => SystemProcessParser.ReadName(e) ?? $"PID {e.ProcessId}",
            gpuUsage.Get);
        Volatile.Write(ref _snapshot, samples);
    }

    public void Dispose()
    {
        NativeMemory.Free(_buffer);
        _buffer = null;
    }

    private void Query()
    {
        if (_buffer == null)
        {
            Grow(1 << 20);
        }

        while (true)
        {
            var status = Ntdll.NtQuerySystemInformation(Ntdll.SystemProcessInformation, _buffer, (uint)_bufferSize, out var needed);
            if (status == StatusInfoLengthMismatch)
            {
                Grow(Math.Max((int)needed + 65536, _bufferSize * 2));
                continue;
            }

            if (status < 0)
            {
                throw new Win32Exception($"NtQuerySystemInformation failed with 0x{status:X8}");
            }

            SystemProcessParser.Parse(_buffer, _bufferSize, _raw);
            return;
        }
    }

    private void Grow(int size)
    {
        NativeMemory.Free(_buffer);
        _buffer = (byte*)NativeMemory.Alloc((nuint)size);
        _bufferSize = size;
    }
}
```

Em `AppHost.cs`: `var processGpu = new ProcessGpuUsage(); var processes = new ProcessMonitor(processGpu);`, trocar `new GpuProvider()` por `new GpuProvider(processGpu)` e adicionar `processes` como último provedor; propriedade `public ProcessMonitor Processes { get; }` (novo parâmetro do construtor privado).

- [ ] **Step 5: Rodar e ver passar**

Run: `dotnet test --project tests/Supermonitor.Tests` → todos passam.

- [ ] **Step 6: Commit**

```powershell
git add src/Supermonitor.Core tests/Supermonitor.Tests
git commit -m "feat(sensors): add process monitor with per-process CPU, memory and GPU"
```

---

### Task 6: Configurações v2

**Files:**
- Create: `src/Supermonitor.Core/Settings/CardSettings.cs`, `src/Supermonitor.Core/Settings/ColorText.cs`
- Modify: `src/Supermonitor.Core/Settings/AppSettings.cs`, `SettingsMigrator.cs`, `SettingsJsonContext.cs`
- Modify: `src/Supermonitor/ViewModels/OverviewViewModel.cs`
- Test: `tests/Supermonitor.Tests/Settings/AppSettingsTests.cs` (reescrever), `SettingsMigratorTests.cs`, `ColorTextTests.cs`, `SettingsStoreTests.cs`

**Interfaces:**
- Produces: `enum AppTheme { System, Light, Dark }`, `enum BackdropKind { Mica, MicaAlt, Acrylic, Solid }`, `enum CardSize { Compact, Normal, Large }`, `enum ChartKind { Line, Area, Bar }`.
- Produces: `CardSettings { string MetricId; CardSize Size = Normal; ChartKind Chart = Line }`, `MetricThreshold { float Warning; float Critical }`.
- Produces: `AppSettings` v2: `Version` (2), `SampleIntervalMs`, `PingHost`, `StartMinimized`, `List<CardSettings> Cards`, `int HistoryMinutes` (1/5/10), `AppTheme Theme`, `string? AccentColor` (`#RRGGBB` ou null = cor do Windows), `BackdropKind Backdrop`, `bool NetworkInBits`, `bool Fahrenheit`, `Dictionary<string, MetricThreshold> Thresholds`; `static IReadOnlyList<int> AllowedHistoryMinutes`; `static List<CardSettings> DefaultCards()`; `static Dictionary<string, MetricThreshold> DefaultThresholds()`; `UnitOptions ToUnitOptions()`; `Normalize()`.
- Produces: `SettingsMigrator.CurrentVersion = 2`; v1 `overviewCards: [id]` → `cards: [{ metricId }]`.
- Produces: `ColorText.TryParseRgb(string?, out byte r, out byte g, out byte b)`, `ColorText.ToHex(byte r, byte g, byte b)` (`"#RRGGBB"` maiúsculo).
- Enums gravados como string (`"theme": "Dark"`).

- [ ] **Step 1: Testes que falham**

`tests/Supermonitor.Tests/Settings/AppSettingsTests.cs` (substituir o arquivo):
```csharp
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Settings;

namespace Supermonitor.Tests.Settings;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_include_temperature_cards_and_thresholds()
    {
        var settings = new AppSettings();

        Assert.Equal(2, settings.Version);
        Assert.Contains(settings.Cards, c => c.MetricId == MetricIds.CpuTemperature);
        Assert.Contains(settings.Cards, c => c.MetricId == MetricIds.GpuTemperature(0));
        Assert.Equal(80, settings.Thresholds[MetricIds.CpuTemperature].Warning);
        Assert.Equal(90, settings.Thresholds[MetricIds.CpuTemperature].Critical);
    }

    [Fact]
    public void Normalize_replaces_invalid_scalars_with_defaults()
    {
        var settings = new AppSettings
        {
            SampleIntervalMs = 7,
            PingHost = "  ",
            HistoryMinutes = 7,
            Theme = (AppTheme)42,
            Backdrop = (BackdropKind)42,
            AccentColor = "blue",
            Cards = null!,
            Thresholds = null!,
        };

        settings.Normalize();

        Assert.Equal(1000, settings.SampleIntervalMs);
        Assert.Equal("1.1.1.1", settings.PingHost);
        Assert.Equal(1, settings.HistoryMinutes);
        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.Equal(BackdropKind.Mica, settings.Backdrop);
        Assert.Null(settings.AccentColor);
        Assert.Equal(AppSettings.DefaultCards().Select(c => c.MetricId), settings.Cards.Select(c => c.MetricId));
        Assert.NotEmpty(settings.Thresholds);
    }

    [Fact]
    public void Normalize_cleans_cards()
    {
        var settings = new AppSettings
        {
            Cards =
            [
                null!,
                new CardSettings { MetricId = " " },
                new CardSettings { MetricId = "cpu.usage", Size = (CardSize)9, Chart = (ChartKind)9 },
                new CardSettings { MetricId = "cpu.usage", Size = CardSize.Large },
                new CardSettings { MetricId = "mem.usage", Size = CardSize.Compact, Chart = ChartKind.Bar },
            ],
        };

        settings.Normalize();

        Assert.Equal(new[] { "cpu.usage", "mem.usage" }, settings.Cards.Select(c => c.MetricId));
        Assert.Equal(CardSize.Normal, settings.Cards[0].Size);
        Assert.Equal(ChartKind.Line, settings.Cards[0].Chart);
        Assert.Equal(CardSize.Compact, settings.Cards[1].Size);
        Assert.Equal(ChartKind.Bar, settings.Cards[1].Chart);
    }

    [Fact]
    public void Normalize_fixes_thresholds()
    {
        var settings = new AppSettings
        {
            Thresholds = new()
            {
                ["a"] = new MetricThreshold { Warning = 90, Critical = 80 },
                ["b"] = new MetricThreshold { Warning = float.NaN, Critical = 10 },
                ["c"] = null!,
            },
        };

        settings.Normalize();

        Assert.Equal(80, settings.Thresholds["a"].Warning);
        Assert.Equal(90, settings.Thresholds["a"].Critical);
        Assert.False(settings.Thresholds.ContainsKey("b"));
        Assert.False(settings.Thresholds.ContainsKey("c"));
    }

    [Fact]
    public void Normalize_keeps_valid_accent()
    {
        var settings = new AppSettings { AccentColor = "#1E90FF", HistoryMinutes = 10 };

        settings.Normalize();

        Assert.Equal("#1E90FF", settings.AccentColor);
        Assert.Equal(10, settings.HistoryMinutes);
    }

    [Fact]
    public void Unit_options_follow_settings()
    {
        var settings = new AppSettings { NetworkInBits = true, Fahrenheit = true };

        Assert.Equal(new UnitOptions(NetworkInBits: true, Fahrenheit: true), settings.ToUnitOptions());
    }
}
```

`tests/Supermonitor.Tests/Settings/SettingsMigratorTests.cs`:
```csharp
using System.Text.Json.Nodes;
using Supermonitor.Core.Settings;

namespace Supermonitor.Tests.Settings;

public class SettingsMigratorTests
{
    [Fact]
    public void Version_one_overview_cards_become_v2_cards()
    {
        var root = JsonNode.Parse("""{ "version": 1, "overviewCards": ["cpu.usage", 5, "net.ping"] }""")!.AsObject();

        var migrated = SettingsMigrator.Migrate(root);

        Assert.Equal(2, migrated["version"]!.GetValue<int>());
        Assert.Null(migrated["overviewCards"]);
        var cards = migrated["cards"]!.AsArray();
        Assert.Equal(new[] { "cpu.usage", "net.ping" }, cards.Select(c => c!["metricId"]!.GetValue<string>()));
    }

    [Fact]
    public void Unversioned_document_is_migrated_to_current_version()
    {
        var root = new JsonObject { ["pingHost"] = "9.9.9.9" };

        var migrated = SettingsMigrator.Migrate(root);

        Assert.Equal(SettingsMigrator.CurrentVersion, migrated["version"]!.GetValue<int>());
        Assert.Equal("9.9.9.9", migrated["pingHost"]!.GetValue<string>());
    }

    [Fact]
    public void Current_version_document_is_left_as_is()
    {
        var root = JsonNode.Parse("""{ "version": 2, "cards": [ { "metricId": "cpu.usage" } ] }""")!.AsObject();

        var migrated = SettingsMigrator.Migrate(root);

        Assert.Single(migrated["cards"]!.AsArray());
    }
}
```

`tests/Supermonitor.Tests/Settings/ColorTextTests.cs`:
```csharp
using Supermonitor.Core.Settings;

namespace Supermonitor.Tests.Settings;

public class ColorTextTests
{
    [Fact]
    public void Parses_hex_rgb()
    {
        Assert.True(ColorText.TryParseRgb("#1E90ff", out var r, out var g, out var b));
        Assert.Equal((0x1E, 0x90, 0xFF), (r, g, b));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1E90FF")]
    [InlineData("#1E90F")]
    [InlineData("#GGGGGG")]
    [InlineData("#1E90FF00")]
    public void Rejects_other_formats(string? text)
    {
        Assert.False(ColorText.TryParseRgb(text, out _, out _, out _));
    }

    [Fact]
    public void Formats_hex_in_upper_case()
    {
        Assert.Equal("#0A0BFF", ColorText.ToHex(10, 11, 255));
    }
}
```

Em `SettingsStoreTests`, adicionar:
```csharp
    [Fact]
    public void Enums_are_saved_as_strings_and_v1_files_load_their_cards()
    {
        File.WriteAllText(FilePath, """{ "version": 1, "overviewCards": ["cpu.usage"] }""");
        using (var store = CreateStore())
        {
            Assert.Equal(SettingsLoadResult.Loaded, store.Load());
            Assert.Equal(new[] { "cpu.usage" }, store.Current.Cards.Select(c => c.MetricId));
            store.Current.Theme = AppTheme.Dark;
            store.RequestSave();
            store.Flush();
        }

        Assert.Contains("\"theme\": \"Dark\"", File.ReadAllText(FilePath));
    }
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: erro de compilação (`CardSettings`, `AppTheme`, `ColorText` não encontrados).

- [ ] **Step 3: Implementar**

`src/Supermonitor.Core/Settings/CardSettings.cs`:
```csharp
namespace Supermonitor.Core.Settings;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

public enum BackdropKind
{
    Mica,
    MicaAlt,
    Acrylic,
    Solid,
}

public enum CardSize
{
    Compact,
    Normal,
    Large,
}

public enum ChartKind
{
    Line,
    Area,
    Bar,
}

public sealed class CardSettings
{
    public string MetricId { get; set; } = string.Empty;

    public CardSize Size { get; set; } = CardSize.Normal;

    public ChartKind Chart { get; set; } = ChartKind.Line;
}

public sealed class MetricThreshold
{
    public float Warning { get; set; }

    public float Critical { get; set; }
}
```

`src/Supermonitor.Core/Settings/ColorText.cs`:
```csharp
using System.Globalization;

namespace Supermonitor.Core.Settings;

public static class ColorText
{
    public static bool TryParseRgb(string? text, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (text is not { Length: 7 } || text[0] != '#')
        {
            return false;
        }

        return byte.TryParse(text.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
            && byte.TryParse(text.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
            && byte.TryParse(text.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
    }

    public static string ToHex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";
}
```

`src/Supermonitor.Core/Settings/AppSettings.cs` (substituir):
```csharp
using Supermonitor.Core.Metrics;

namespace Supermonitor.Core.Settings;

public sealed class AppSettings
{
    public const string DefaultPingHost = "1.1.1.1";
    public const int DefaultSampleIntervalMs = 1000;
    public const int DefaultHistoryMinutes = 1;

    public static IReadOnlyList<int> AllowedIntervalsMs { get; } = [250, 500, 1000, 2000];

    public static IReadOnlyList<int> AllowedHistoryMinutes { get; } = [1, 5, 10];

    public int Version { get; set; } = SettingsMigrator.CurrentVersion;

    public int SampleIntervalMs { get; set; } = DefaultSampleIntervalMs;

    public string PingHost { get; set; } = DefaultPingHost;

    public bool StartMinimized { get; set; }

    public List<CardSettings> Cards { get; set; } = DefaultCards();

    public int HistoryMinutes { get; set; } = DefaultHistoryMinutes;

    public AppTheme Theme { get; set; } = AppTheme.System;

    public string? AccentColor { get; set; }

    public BackdropKind Backdrop { get; set; } = BackdropKind.Mica;

    public bool NetworkInBits { get; set; }

    public bool Fahrenheit { get; set; }

    public Dictionary<string, MetricThreshold> Thresholds { get; set; } = DefaultThresholds();

    public static List<CardSettings> DefaultCards() =>
    [
        new() { MetricId = MetricIds.CpuUsage },
        new() { MetricId = MetricIds.CpuTemperature },
        new() { MetricId = MetricIds.MemoryUsage },
        new() { MetricId = MetricIds.GpuUsage(0) },
        new() { MetricId = MetricIds.GpuTemperature(0) },
        new() { MetricId = MetricIds.GpuVramUsed(0) },
        new() { MetricId = MetricIds.DiskActive(0) },
        new() { MetricId = MetricIds.NetworkTotalDown },
        new() { MetricId = MetricIds.NetworkTotalUp },
        new() { MetricId = MetricIds.Ping },
    ];

    public static Dictionary<string, MetricThreshold> DefaultThresholds() => new()
    {
        [MetricIds.CpuTemperature] = new() { Warning = 80, Critical = 90 },
        [MetricIds.GpuTemperature(0)] = new() { Warning = 80, Critical = 90 },
        [MetricIds.GpuHotspot(0)] = new() { Warning = 90, Critical = 100 },
        [MetricIds.GpuMemoryTemperature(0)] = new() { Warning = 90, Critical = 100 },
    };

    public UnitOptions ToUnitOptions() => new(NetworkInBits, Fahrenheit);

    public void Normalize()
    {
        if (!AllowedIntervalsMs.Contains(SampleIntervalMs))
        {
            SampleIntervalMs = DefaultSampleIntervalMs;
        }

        if (!AllowedHistoryMinutes.Contains(HistoryMinutes))
        {
            HistoryMinutes = DefaultHistoryMinutes;
        }

        PingHost = string.IsNullOrWhiteSpace(PingHost) ? DefaultPingHost : PingHost.Trim();
        Theme = Enum.IsDefined(Theme) ? Theme : AppTheme.System;
        Backdrop = Enum.IsDefined(Backdrop) ? Backdrop : BackdropKind.Mica;
        AccentColor = ColorText.TryParseRgb(AccentColor, out _, out _, out _) ? AccentColor!.ToUpperInvariant() : null;

        Cards = Cards is null
            ? DefaultCards()
            : Cards
                .Where(c => c is not null && !string.IsNullOrWhiteSpace(c.MetricId))
                .DistinctBy(c => c.MetricId)
                .ToList();
        foreach (var card in Cards)
        {
            card.Size = Enum.IsDefined(card.Size) ? card.Size : CardSize.Normal;
            card.Chart = Enum.IsDefined(card.Chart) ? card.Chart : ChartKind.Line;
        }

        Thresholds ??= DefaultThresholds();
        foreach (var (id, threshold) in Thresholds.ToArray())
        {
            if (threshold is null || !float.IsFinite(threshold.Warning) || !float.IsFinite(threshold.Critical))
            {
                Thresholds.Remove(id);
            }
            else if (threshold.Warning > threshold.Critical)
            {
                (threshold.Warning, threshold.Critical) = (threshold.Critical, threshold.Warning);
            }
        }
    }
}
```

`src/Supermonitor.Core/Settings/SettingsMigrator.cs` (substituir):
```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Supermonitor.Core.Settings;

public static class SettingsMigrator
{
    public const int CurrentVersion = 2;

    public static JsonObject Migrate(JsonObject root)
    {
        var version = root["version"]?.GetValue<int>() ?? 0;

        // v0 → v1: arquivos sem número de versão. Nenhum campo mudou.
        if (version < 1)
        {
            version = 1;
        }

        // v1 → v2: "overviewCards": ["id"] vira "cards": [{ "metricId": "id" }].
        if (version < 2)
        {
            if (root["overviewCards"] is JsonArray ids)
            {
                var cards = new JsonArray();
                foreach (var id in ids)
                {
                    if (id?.GetValueKind() == JsonValueKind.String)
                    {
                        cards.Add(new JsonObject { ["metricId"] = id.GetValue<string>() });
                    }
                }

                root["cards"] = cards;
            }

            root.Remove("overviewCards");
            version = 2;
        }

        root["version"] = version;
        return root;
    }
}
```

`src/Supermonitor.Core/Settings/SettingsJsonContext.cs`:
```csharp
using System.Text.Json.Serialization;

namespace Supermonitor.Core.Settings;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
```

Em `src/Supermonitor/ViewModels/OverviewViewModel.cs`, trocar o laço do construtor por:
```csharp
        foreach (var card in host.Settings.Current.Cards)
        {
            Cards.Add(new MetricCardViewModel(card.MetricId, host.Metrics));
        }
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"` → todos passam.
Run: build do app com `-p:OutDir=<temp>\` → `0 Erro(s)`.

- [ ] **Step 5: Commit**

```powershell
git add src tests
git commit -m "feat(settings): schema v2 with card layout, appearance, units and thresholds"
```

---

### Task 7: Edição do dashboard e limiares

**Files:**
- Create: `src/Supermonitor.Core/Dashboard/DashboardEditor.cs`, `src/Supermonitor.Core/Dashboard/ThresholdEvaluator.cs`
- Test: `tests/Supermonitor.Tests/Dashboard/DashboardEditorTests.cs`, `ThresholdEvaluatorTests.cs`

**Interfaces:**
- Produces: `DashboardEditor.IndexOf(List<CardSettings>, string id) -> int`, `Add(...) -> bool` (false se já existe), `Remove(...) -> bool`, `MoveTo(cards, id, newIndex) -> int` (índice final, -1 se ausente, limitado à faixa), `MoveBy(cards, id, delta) -> int`.
- Produces: `enum ThresholdLevel { Normal, Warning, Critical }`; `ThresholdEvaluator.Evaluate(float value, MetricThreshold? threshold) -> ThresholdLevel`.

- [ ] **Step 1: Testes que falham**

`tests/Supermonitor.Tests/Dashboard/DashboardEditorTests.cs`:
```csharp
using Supermonitor.Core.Dashboard;
using Supermonitor.Core.Settings;

namespace Supermonitor.Tests.Dashboard;

public class DashboardEditorTests
{
    private static List<CardSettings> Cards(params string[] ids) => ids.Select(id => new CardSettings { MetricId = id }).ToList();

    private static string[] Ids(List<CardSettings> cards) => cards.Select(c => c.MetricId).ToArray();

    [Fact]
    public void Add_appends_new_ids_only()
    {
        var cards = Cards("a");

        Assert.True(DashboardEditor.Add(cards, "b"));
        Assert.False(DashboardEditor.Add(cards, "a"));
        Assert.Equal(new[] { "a", "b" }, Ids(cards));
    }

    [Fact]
    public void Remove_works_for_metrics_missing_on_this_machine()
    {
        var cards = Cards("cpu.usage", "gpu0.temp");

        Assert.True(DashboardEditor.Remove(cards, "gpu0.temp"));
        Assert.False(DashboardEditor.Remove(cards, "gpu0.temp"));
        Assert.Equal(new[] { "cpu.usage" }, Ids(cards));
    }

    [Fact]
    public void MoveTo_reorders_and_clamps()
    {
        var cards = Cards("a", "b", "c");

        Assert.Equal(0, DashboardEditor.MoveTo(cards, "c", 0));
        Assert.Equal(new[] { "c", "a", "b" }, Ids(cards));
        Assert.Equal(2, DashboardEditor.MoveTo(cards, "c", 99));
        Assert.Equal(new[] { "a", "b", "c" }, Ids(cards));
        Assert.Equal(-1, DashboardEditor.MoveTo(cards, "x", 0));
    }

    [Fact]
    public void MoveBy_moves_relative_to_current_position()
    {
        var cards = Cards("a", "b", "c");

        Assert.Equal(2, DashboardEditor.MoveBy(cards, "a", 2));
        Assert.Equal(new[] { "b", "c", "a" }, Ids(cards));
        Assert.Equal(0, DashboardEditor.MoveBy(cards, "c", -5));
        Assert.Equal(new[] { "c", "b", "a" }, Ids(cards));
    }
}
```

`tests/Supermonitor.Tests/Dashboard/ThresholdEvaluatorTests.cs`:
```csharp
using Supermonitor.Core.Dashboard;
using Supermonitor.Core.Settings;

namespace Supermonitor.Tests.Dashboard;

public class ThresholdEvaluatorTests
{
    private static readonly MetricThreshold Temps = new() { Warning = 80, Critical = 90 };

    [Theory]
    [InlineData(79.9f, ThresholdLevel.Normal)]
    [InlineData(80f, ThresholdLevel.Warning)]
    [InlineData(89.9f, ThresholdLevel.Warning)]
    [InlineData(90f, ThresholdLevel.Critical)]
    [InlineData(float.NaN, ThresholdLevel.Normal)]
    public void Evaluates_levels(float value, ThresholdLevel expected)
    {
        Assert.Equal(expected, ThresholdEvaluator.Evaluate(value, Temps));
    }

    [Fact]
    public void No_threshold_is_always_normal()
    {
        Assert.Equal(ThresholdLevel.Normal, ThresholdEvaluator.Evaluate(1000, null));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: erro de compilação `Supermonitor.Core.Dashboard` não existe.

- [ ] **Step 3: Implementar**

`src/Supermonitor.Core/Dashboard/DashboardEditor.cs`:
```csharp
using Supermonitor.Core.Settings;

namespace Supermonitor.Core.Dashboard;

public static class DashboardEditor
{
    public static int IndexOf(List<CardSettings> cards, string metricId) => cards.FindIndex(c => c.MetricId == metricId);

    public static bool Add(List<CardSettings> cards, string metricId)
    {
        if (IndexOf(cards, metricId) >= 0)
        {
            return false;
        }

        cards.Add(new CardSettings { MetricId = metricId });
        return true;
    }

    public static bool Remove(List<CardSettings> cards, string metricId)
    {
        var index = IndexOf(cards, metricId);
        if (index < 0)
        {
            return false;
        }

        cards.RemoveAt(index);
        return true;
    }

    public static int MoveTo(List<CardSettings> cards, string metricId, int newIndex)
    {
        var index = IndexOf(cards, metricId);
        if (index < 0)
        {
            return -1;
        }

        newIndex = Math.Clamp(newIndex, 0, cards.Count - 1);
        var card = cards[index];
        cards.RemoveAt(index);
        cards.Insert(newIndex, card);
        return newIndex;
    }

    public static int MoveBy(List<CardSettings> cards, string metricId, int delta)
    {
        var index = IndexOf(cards, metricId);
        return index < 0 ? -1 : MoveTo(cards, metricId, index + delta);
    }
}
```

`src/Supermonitor.Core/Dashboard/ThresholdEvaluator.cs`:
```csharp
using Supermonitor.Core.Settings;

namespace Supermonitor.Core.Dashboard;

public enum ThresholdLevel
{
    Normal,
    Warning,
    Critical,
}

public static class ThresholdEvaluator
{
    public static ThresholdLevel Evaluate(float value, MetricThreshold? threshold)
    {
        if (threshold is null || !float.IsFinite(value))
        {
            return ThresholdLevel.Normal;
        }

        if (value >= threshold.Critical)
        {
            return ThresholdLevel.Critical;
        }

        return value >= threshold.Warning ? ThresholdLevel.Warning : ThresholdLevel.Normal;
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"` → todos passam.

- [ ] **Step 5: Commit**

```powershell
git add src/Supermonitor.Core/Dashboard tests/Supermonitor.Tests/Dashboard
git commit -m "feat(dashboard): add card editing and threshold evaluation"
```

---

### Task 8: Amostragem de gráfico, composição de memória e ordenação de processos

**Files:**
- Create: `src/Supermonitor.Core/Dashboard/ChartSampler.cs`, `src/Supermonitor.Core/Dashboard/MemoryComposition.cs`, `src/Supermonitor.Core/Sensors/Processes/ProcessSorting.cs`
- Test: `tests/Supermonitor.Tests/Dashboard/ChartSamplerTests.cs`, `MemoryCompositionTests.cs`, `tests/Supermonitor.Tests/Sensors/ProcessSortingTests.cs`

**Interfaces:**
- Produces: `ChartSampler.Downsample(ReadOnlySpan<float> source, Span<float> destination) -> int` (cópia direta se couber; senão máximo finito por balde, NaN se o balde não tem valor finito), `ChartSampler.Maximum(ReadOnlySpan<float> values, float? fixedMaximum) -> float` (fixo, ou maior finito × 1,1, ou 1).
- Produces: `readonly record struct MemoryComposition(double InUse, double Modified, double Standby, double Free)` com `static MemoryComposition From(double total, double used, double modified, double standby, double free)`. Sem listas de memória (NaN): `Standby = 0`, `Free = total − used`, `Modified = 0`.
- Produces: `enum ProcessSortKey { Cpu, Memory, Gpu, Name }`; `ProcessSorting.Top(IReadOnlyList<ProcessSample>, ProcessSortKey, string? filter, int count) -> ProcessSample[]` (desempate por PID).

- [ ] **Step 1: Testes que falham**

`tests/Supermonitor.Tests/Dashboard/ChartSamplerTests.cs`:
```csharp
using Supermonitor.Core.Dashboard;

namespace Supermonitor.Tests.Dashboard;

public class ChartSamplerTests
{
    [Fact]
    public void Short_series_is_copied_as_is()
    {
        var destination = new float[5];

        var count = ChartSampler.Downsample([1, 2, 3], destination);

        Assert.Equal(3, count);
        Assert.Equal(new float[] { 1, 2, 3 }, destination[..3]);
    }

    [Fact]
    public void Long_series_keeps_the_peak_of_each_bucket()
    {
        var destination = new float[3];

        var count = ChartSampler.Downsample([1, 5, 2, 8, 3, 9], destination);

        Assert.Equal(3, count);
        Assert.Equal(new float[] { 5, 8, 9 }, destination);
    }

    [Fact]
    public void Bucket_without_finite_values_is_nan()
    {
        var destination = new float[2];

        ChartSampler.Downsample([float.NaN, float.NaN, float.NaN, 4], destination);

        Assert.True(float.IsNaN(destination[0]));
        Assert.Equal(4, destination[1]);
    }

    [Fact]
    public void Maximum_uses_fixed_value_headroom_or_one()
    {
        Assert.Equal(100, ChartSampler.Maximum([5, 50], 100f));
        Assert.Equal(55, ChartSampler.Maximum([5, 50, float.NaN], null), 3);
        Assert.Equal(1, ChartSampler.Maximum([0, float.NaN], null));
    }
}
```

`tests/Supermonitor.Tests/Dashboard/MemoryCompositionTests.cs`:
```csharp
using Supermonitor.Core.Dashboard;

namespace Supermonitor.Tests.Dashboard;

public class MemoryCompositionTests
{
    [Fact]
    public void Splits_used_memory_into_in_use_and_modified()
    {
        var composition = MemoryComposition.From(total: 32, used: 14, modified: 1, standby: 12, free: 6);

        Assert.Equal(new MemoryComposition(13, 1, 12, 6), composition);
    }

    [Fact]
    public void Without_memory_lists_everything_available_is_free()
    {
        var composition = MemoryComposition.From(total: 32, used: 14, modified: double.NaN, standby: double.NaN, free: double.NaN);

        Assert.Equal(new MemoryComposition(14, 0, 0, 18), composition);
    }

    [Fact]
    public void Invalid_total_yields_empty_composition()
    {
        Assert.Equal(default, MemoryComposition.From(double.NaN, 1, 0, 0, 0));
    }
}
```

`tests/Supermonitor.Tests/Sensors/ProcessSortingTests.cs`:
```csharp
using Supermonitor.Core.Sensors.Processes;

namespace Supermonitor.Tests.Sensors;

public class ProcessSortingTests
{
    private static readonly ProcessSample[] Processes =
    [
        new(1, "game.exe", 40, 4_000, 90),
        new(2, "chrome.exe", float.NaN, 9_000, 5),
        new(3, "Discord.exe", 10, 2_000, 0),
        new(4, "audio.exe", 10, 1_000, 0),
    ];

    [Fact]
    public void Sorts_by_cpu_descending_with_unknown_last_and_pid_tiebreak()
    {
        var top = ProcessSorting.Top(Processes, ProcessSortKey.Cpu, null, 10);

        Assert.Equal(new[] { 1, 3, 4, 2 }, top.Select(p => p.ProcessId));
    }

    [Fact]
    public void Sorts_by_memory_and_gpu_descending()
    {
        Assert.Equal(new[] { 2, 1 }, ProcessSorting.Top(Processes, ProcessSortKey.Memory, null, 2).Select(p => p.ProcessId));
        Assert.Equal(1, ProcessSorting.Top(Processes, ProcessSortKey.Gpu, null, 1)[0].ProcessId);
    }

    [Fact]
    public void Sorts_by_name_ignoring_case()
    {
        var names = ProcessSorting.Top(Processes, ProcessSortKey.Name, null, 10).Select(p => p.Name);

        Assert.Equal(new[] { "audio.exe", "chrome.exe", "Discord.exe", "game.exe" }, names);
    }

    [Fact]
    public void Filters_by_name_substring()
    {
        var top = ProcessSorting.Top(Processes, ProcessSortKey.Cpu, " DISC ", 10);

        Assert.Equal(new[] { 3 }, top.Select(p => p.ProcessId));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: erro de compilação (`ChartSampler`, `MemoryComposition`, `ProcessSorting` não encontrados).

- [ ] **Step 3: Implementar**

`src/Supermonitor.Core/Dashboard/ChartSampler.cs`:
```csharp
namespace Supermonitor.Core.Dashboard;

public static class ChartSampler
{
    // Reduz a série para a largura do gráfico guardando o pico de cada balde (picos importam para gamers).
    public static int Downsample(ReadOnlySpan<float> source, Span<float> destination)
    {
        if (destination.IsEmpty)
        {
            return 0;
        }

        if (source.Length <= destination.Length)
        {
            source.CopyTo(destination);
            return source.Length;
        }

        for (var bucket = 0; bucket < destination.Length; bucket++)
        {
            var start = (int)((long)bucket * source.Length / destination.Length);
            var end = (int)((long)(bucket + 1) * source.Length / destination.Length);
            var max = float.NaN;
            for (var i = start; i < end; i++)
            {
                var value = source[i];
                if (float.IsFinite(value) && !(value <= max))
                {
                    max = value;
                }
            }

            destination[bucket] = max;
        }

        return destination.Length;
    }

    public static float Maximum(ReadOnlySpan<float> values, float? fixedMaximum)
    {
        if (fixedMaximum is { } fixedValue)
        {
            return fixedValue;
        }

        var max = 0f;
        foreach (var value in values)
        {
            if (float.IsFinite(value) && value > max)
            {
                max = value;
            }
        }

        return max > 0 ? max * 1.1f : 1f;
    }
}
```

`src/Supermonitor.Core/Dashboard/MemoryComposition.cs`:
```csharp
namespace Supermonitor.Core.Dashboard;

public readonly record struct MemoryComposition(double InUse, double Modified, double Standby, double Free)
{
    public static MemoryComposition From(double total, double used, double modified, double standby, double free)
    {
        if (!(total > 0) || !double.IsFinite(used))
        {
            return default;
        }

        if (!double.IsFinite(standby) || !double.IsFinite(free))
        {
            return new MemoryComposition(used, 0, 0, Math.Max(0, total - used));
        }

        var modifiedBytes = double.IsFinite(modified) ? modified : 0;
        return new MemoryComposition(Math.Max(0, used - modifiedBytes), modifiedBytes, standby, free);
    }
}
```

`src/Supermonitor.Core/Sensors/Processes/ProcessSorting.cs`:
```csharp
namespace Supermonitor.Core.Sensors.Processes;

public enum ProcessSortKey
{
    Cpu,
    Memory,
    Gpu,
    Name,
}

public static class ProcessSorting
{
    public static ProcessSample[] Top(IReadOnlyList<ProcessSample> processes, ProcessSortKey key, string? filter, int count)
    {
        IEnumerable<ProcessSample> query = processes;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var term = filter.Trim();
            query = query.Where(p => p.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = key switch
        {
            ProcessSortKey.Memory => query.OrderByDescending(p => p.PrivateBytes),
            ProcessSortKey.Gpu => query.OrderByDescending(p => Known(p.GpuPercent)),
            ProcessSortKey.Name => query.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            _ => query.OrderByDescending(p => Known(p.CpuPercent)),
        };

        return ordered.ThenBy(p => p.ProcessId).Take(count).ToArray();
    }

    private static float Known(float value) => float.IsFinite(value) ? value : -1;
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"` → todos passam.

- [ ] **Step 5: Commit**

```powershell
git add src/Supermonitor.Core tests/Supermonitor.Tests
git commit -m "feat(dashboard): add chart downsampling, memory composition and process sorting"
```

---

### Task 9: Gráfico, layout e card com tamanho, tipo e limiar

**Files:**
- Create: `src/Supermonitor/Appearance/AccentPalette.cs`, `src/Supermonitor/Controls/TimeSeriesChart.cs`, `src/Supermonitor/Controls/CardWrapLayout.cs`, `src/Supermonitor/Controls/CardMetrics.cs`, `src/Supermonitor/ViewModels/ICardActions.cs`
- Delete: `src/Supermonitor/Controls/SparklineChart.cs`
- Modify: `src/Supermonitor/Controls/MetricCard.xaml(.cs)`, `src/Supermonitor/ViewModels/MetricCardViewModel.cs`, `src/Supermonitor/ViewModels/OverviewViewModel.cs`, `src/Supermonitor/Views/OverviewPage.xaml`

**Interfaces:**
- Consumes: `ChartSampler` (Task 8), `ThresholdEvaluator` (Task 7), `CardSize`, `ChartKind`, `MetricThreshold` (Task 6).
- Produces: `AccentPalette.Current` (`Windows.UI.Color`), `AccentPalette.Set(Color)`, `event Action? AccentPalette.Changed`.
- Produces: `TimeSeriesChart` (`Source`, `Kind`), `CardWrapLayout` (`Spacing`), `CardMetrics.Width(CardSize)`, `Height(CardSize)`, `ChartVisibility(CardSize)`.
- Produces: `ICardActions` com `Remove(card)`, `Move(card, int delta)`, `MoveTo(string id, string targetId)`, `SetSize(card, CardSize)`, `SetChart(card, ChartKind)`, `MetricThreshold? GetThreshold(string id)`, `SetThreshold(string id, MetricThreshold?)`.
- Produces: `MetricCardViewModel` com `Size`, `Chart`, `Level`, `IsEditing` (observáveis), `ICardActions? Actions { get; init; }`, `Refresh(UnitOptions options, MetricThreshold? threshold)`.

Componentes de UI sem lógica testável isolada: a lógica de amostragem e limiar já está coberta nas Tasks 7 e 8. A verificação desta task é build + execução.

- [ ] **Step 1: Paleta, layout e métricas de card**

`src/Supermonitor/Appearance/AccentPalette.cs`:
```csharp
using Microsoft.UI.Xaml;
using Windows.UI;

namespace Supermonitor.Appearance;

public static class AccentPalette
{
    private static Color? _current;

    public static event Action? Changed;

    public static Color Current => _current ??= (Color)Application.Current.Resources["SystemAccentColor"];

    public static void Set(Color color)
    {
        _current = color;
        Changed?.Invoke();
    }
}
```

`src/Supermonitor/Controls/CardMetrics.cs`:
```csharp
using Microsoft.UI.Xaml;
using Supermonitor.Core.Settings;

namespace Supermonitor.Controls;

public static class CardMetrics
{
    public static double Width(CardSize size) => size switch
    {
        CardSize.Compact => 200,
        CardSize.Large => 572,
        _ => 280,
    };

    public static double Height(CardSize size) => size switch
    {
        CardSize.Compact => 96,
        CardSize.Large => 260,
        _ => 170,
    };

    public static Visibility ChartVisibility(CardSize size) => size == CardSize.Compact ? Visibility.Collapsed : Visibility.Visible;
}
```

`src/Supermonitor/Controls/CardWrapLayout.cs`:
```csharp
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Supermonitor.Controls;

// Fluxo da esquerda para a direita; quebra de linha quando o próximo card não cabe.
public sealed partial class CardWrapLayout : NonVirtualizingLayout
{
    public double Spacing { get; set; } = 12;

    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize) =>
        Place(context, availableSize.Width, arrange: false);

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize) =>
        Place(context, finalSize.Width, arrange: true);

    private Size Place(NonVirtualizingLayoutContext context, double width, bool arrange)
    {
        double x = 0, y = 0, rowHeight = 0, usedWidth = 0;
        foreach (var child in context.Children)
        {
            if (!arrange)
            {
                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            }

            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > width)
            {
                x = 0;
                y += rowHeight + Spacing;
                rowHeight = 0;
            }

            if (arrange)
            {
                child.Arrange(new Rect(x, y, size.Width, size.Height));
            }

            x += size.Width + Spacing;
            rowHeight = Math.Max(rowHeight, size.Height);
            usedWidth = Math.Max(usedWidth, x - Spacing);
        }

        return new Size(double.IsInfinity(width) ? usedWidth : width, y + rowHeight);
    }
}
```

- [ ] **Step 2: Gráfico**

`src/Supermonitor/Controls/TimeSeriesChart.cs`:
```csharp
using System.Numerics;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Supermonitor.Appearance;
using Supermonitor.Core.Dashboard;
using Supermonitor.Core.Settings;
using Windows.UI;

namespace Supermonitor.Controls;

public sealed partial class TimeSeriesChart : UserControl
{
    private CanvasControl? _canvas;
    private ISeriesSource? _source;
    private ChartKind _kind;
    private float[] _history = new float[120];
    private float[] _plot = new float[256];

    public TimeSeriesChart()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ChartKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            Invalidate();
        }
    }

    public ISeriesSource? Source
    {
        get => _source;
        set
        {
            if (_source is not null && _canvas is not null)
            {
                _source.Refreshed -= Invalidate;
            }

            _source = value;
            if (_source is not null && _canvas is not null)
            {
                _source.Refreshed += Invalidate;
            }

            Invalidate();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_canvas is not null)
        {
            return;
        }

        _canvas = new CanvasControl();
        _canvas.Draw += OnDraw;
        Content = _canvas;
        AccentPalette.Changed += Invalidate;
        if (_source is not null)
        {
            _source.Refreshed += Invalidate;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_canvas is null)
        {
            return;
        }

        if (_source is not null)
        {
            _source.Refreshed -= Invalidate;
        }

        AccentPalette.Changed -= Invalidate;
        _canvas.Draw -= OnDraw;
        _canvas.RemoveFromVisualTree();
        _canvas = null;
        Content = null;
    }

    private void Invalidate() => _canvas?.Invalidate();

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_source is null)
        {
            return;
        }

        var width = (float)sender.ActualWidth;
        var height = (float)sender.ActualHeight;
        var count = Math.Max(2, _source.PointCount);
        if (width < 2 || height < 2)
        {
            return;
        }

        if (_history.Length < count)
        {
            _history = new float[count];
        }

        var history = _history.AsSpan(0, count);
        var n = _source.CopyHistory(history);
        if (n < 2)
        {
            return;
        }

        // A janela inteira (count pontos) ocupa a largura; a série fica alinhada à direita.
        var slots = Math.Max(2, Math.Min(count, (int)width));
        var visibleSlots = Math.Max(2, (int)Math.Ceiling((double)n * slots / count));
        if (_plot.Length < visibleSlots)
        {
            _plot = new float[visibleSlots];
        }

        var plotted = ChartSampler.Downsample(history[..n], _plot.AsSpan(0, visibleSlots));
        var points = _plot.AsSpan(0, plotted);
        var max = ChartSampler.Maximum(points, _source.FixedMaximum);
        var step = width / (slots - 1);
        var offset = slots - plotted;
        var stroke = AccentPalette.Current;

        float X(int i) => (offset + i) * step;
        float Y(float v) => height - Math.Clamp(v / max, 0, 1) * height;

        var session = args.DrawingSession;
        switch (_kind)
        {
            case ChartKind.Bar:
                var barWidth = Math.Max(1, step - 1);
                for (var i = 0; i < plotted; i++)
                {
                    if (float.IsFinite(points[i]))
                    {
                        var y = Y(points[i]);
                        session.FillRectangle(X(i) - barWidth / 2, y, barWidth, height - y, stroke);
                    }
                }

                return;

            case ChartKind.Area:
                var fill = Color.FromArgb(0x50, stroke.R, stroke.G, stroke.B);
                var run = new List<Vector2>();
                for (var i = 0; i <= plotted; i++)
                {
                    if (i < plotted && float.IsFinite(points[i]))
                    {
                        run.Add(new Vector2(X(i), Y(points[i])));
                        continue;
                    }

                    if (run.Count >= 2)
                    {
                        run.Add(new Vector2(run[^1].X, height));
                        run.Add(new Vector2(run[0].X, height));
                        using var geometry = CanvasGeometry.CreatePolygon(sender, run.ToArray());
                        session.FillGeometry(geometry, fill);
                    }

                    run.Clear();
                }

                goto default;

            default:
                for (var i = 1; i < plotted; i++)
                {
                    float a = points[i - 1], b = points[i];
                    if (float.IsFinite(a) && float.IsFinite(b))
                    {
                        session.DrawLine(X(i - 1), Y(a), X(i), Y(b), stroke, 1.5f);
                    }
                }

                return;
        }
    }
}
```

Apagar `src/Supermonitor/Controls/SparklineChart.cs`.

- [ ] **Step 3: ViewModel do card e ações**

`src/Supermonitor/ViewModels/ICardActions.cs`:
```csharp
using Supermonitor.Core.Settings;

namespace Supermonitor.ViewModels;

public interface ICardActions
{
    void Remove(MetricCardViewModel card);

    void Move(MetricCardViewModel card, int delta);

    void MoveTo(string metricId, string targetMetricId);

    void SetSize(MetricCardViewModel card, CardSize size);

    void SetChart(MetricCardViewModel card, ChartKind chart);

    MetricThreshold? GetThreshold(string metricId);

    void SetThreshold(string metricId, MetricThreshold? threshold);
}
```

`src/Supermonitor/ViewModels/MetricCardViewModel.cs` (substituir):
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Supermonitor.Controls;
using Supermonitor.Core.Dashboard;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Settings;

namespace Supermonitor.ViewModels;

public sealed partial class MetricCardViewModel : ObservableObject, ISeriesSource
{
    private readonly MetricStore _store;

    public MetricCardViewModel(string metricId, MetricStore store)
    {
        MetricId = metricId;
        _store = store;
        Title = metricId;
        ValueText = "N/A";
        Size = CardSize.Normal;
        Chart = ChartKind.Line;
        Level = ThresholdLevel.Normal;
    }

    public event Action? Refreshed;

    public string MetricId { get; }

    public ICardActions? Actions { get; init; }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string ValueText { get; set; }

    [ObservableProperty]
    public partial CardSize Size { get; set; }

    [ObservableProperty]
    public partial ChartKind Chart { get; set; }

    [ObservableProperty]
    public partial ThresholdLevel Level { get; set; }

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    public int PointCount { get; set; } = 60;

    public float? FixedMaximum { get; private set; }

    public int CopyHistory(Span<float> destination) => _store.CopyHistory(MetricId, destination);

    public void Refresh(UnitOptions options, MetricThreshold? threshold)
    {
        var latest = _store.GetLatest(MetricId);
        if (_store.TryGetDescriptor(MetricId, out var descriptor))
        {
            Title = descriptor.DisplayName;
            FixedMaximum = descriptor.Unit == MetricUnit.Percent ? 100f : null;
            ValueText = UnitFormatter.Format(latest, descriptor.Unit, options);
        }
        else
        {
            ValueText = "N/A";
        }

        Level = ThresholdEvaluator.Evaluate(latest, threshold);
        Refreshed?.Invoke();
    }
}
```

Em `OverviewViewModel.RefreshNow`, trocar o laço por (a customização completa vem na Task 11):
```csharp
        var settings = _host.Settings.Current;
        var points = Math.Max(2, 60_000 / settings.SampleIntervalMs);
        var options = settings.ToUnitOptions();
        foreach (var card in Cards)
        {
            card.PointCount = points;
            card.Refresh(options, settings.Thresholds.GetValueOrDefault(card.MetricId));
        }
```

- [ ] **Step 4: Card**

`src/Supermonitor/Controls/MetricCard.xaml` (substituir):
```xml
<UserControl
    x:Class="Supermonitor.Controls.MetricCard"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:Supermonitor.Controls"
    Width="{x:Bind local:CardMetrics.Width(ViewModel.Size), Mode=OneWay}"
    Height="{x:Bind local:CardMetrics.Height(ViewModel.Size), Mode=OneWay}">
    <Border
        x:Name="CardBorder"
        Padding="16"
        CornerRadius="8"
        BorderThickness="1"
        Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
        BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}">
        <Border.ContextFlyout>
        <MenuFlyout x:Name="CardMenu" Opening="OnMenuOpening">
            <MenuFlyoutSubItem Text="Size">
                <RadioMenuFlyoutItem x:Name="SizeCompact" Text="Compact" GroupName="size" Click="OnSizeClick" Tag="Compact" />
                <RadioMenuFlyoutItem x:Name="SizeNormal" Text="Normal" GroupName="size" Click="OnSizeClick" Tag="Normal" />
                <RadioMenuFlyoutItem x:Name="SizeLarge" Text="Large" GroupName="size" Click="OnSizeClick" Tag="Large" />
            </MenuFlyoutSubItem>
            <MenuFlyoutSubItem Text="Chart">
                <RadioMenuFlyoutItem x:Name="ChartLine" Text="Line" GroupName="chart" Click="OnChartClick" Tag="Line" />
                <RadioMenuFlyoutItem x:Name="ChartArea" Text="Area" GroupName="chart" Click="OnChartClick" Tag="Area" />
                <RadioMenuFlyoutItem x:Name="ChartBar" Text="Bar" GroupName="chart" Click="OnChartClick" Tag="Bar" />
            </MenuFlyoutSubItem>
            <MenuFlyoutItem Text="Thresholds…" Click="OnThresholdsClick" />
            <MenuFlyoutSeparator />
            <MenuFlyoutItem Text="Move earlier" Click="OnMoveEarlierClick" />
            <MenuFlyoutItem Text="Move later" Click="OnMoveLaterClick" />
            <MenuFlyoutSeparator />
            <MenuFlyoutItem Text="Remove" Click="OnRemoveClick">
                <MenuFlyoutItem.Icon>
                    <FontIcon Glyph="&#xE74D;" />
                </MenuFlyoutItem.Icon>
            </MenuFlyoutItem>
        </MenuFlyout>
        </Border.ContextFlyout>
        <Grid RowSpacing="4">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
            </Grid.RowDefinitions>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <TextBlock
                Text="{x:Bind ViewModel.Title, Mode=OneWay}"
                Style="{StaticResource BodyStrongTextBlockStyle}"
                Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                TextTrimming="CharacterEllipsis" />
            <Button
                x:Name="RemoveButton"
                Grid.Column="1"
                Padding="4"
                Visibility="Collapsed"
                Click="OnRemoveClick"
                ToolTipService.ToolTip="Remove card">
                <FontIcon Glyph="&#xE74D;" FontSize="12" />
            </Button>
            <TextBlock
                x:Name="ValueText"
                Grid.Row="1"
                Grid.ColumnSpan="2"
                Text="{x:Bind ViewModel.ValueText, Mode=OneWay}"
                Style="{StaticResource SubtitleTextBlockStyle}" />
            <local:TimeSeriesChart
                x:Name="Chart"
                Grid.Row="2"
                Grid.ColumnSpan="2"
                Visibility="{x:Bind local:CardMetrics.ChartVisibility(ViewModel.Size), Mode=OneWay}" />
        </Grid>
    </Border>
</UserControl>
```

`src/Supermonitor/Controls/MetricCard.xaml.cs` (substituir):
```csharp
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Supermonitor.Core.Dashboard;
using Supermonitor.Core.Settings;
using Supermonitor.Dialogs;
using Supermonitor.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Supermonitor.Controls;

public sealed partial class MetricCard : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(MetricCardViewModel),
        typeof(MetricCard),
        new PropertyMetadata(null, OnViewModelChanged));

    public MetricCard()
    {
        InitializeComponent();
        DragStarting += OnDragStarting;
        DragOver += OnDragOver;
        Drop += OnDrop;
    }

    public MetricCardViewModel? ViewModel
    {
        get => (MetricCardViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var card = (MetricCard)d;
        if (e.OldValue is MetricCardViewModel old)
        {
            old.PropertyChanged -= card.OnViewModelPropertyChanged;
        }

        var vm = e.NewValue as MetricCardViewModel;
        card.Chart.Source = vm;
        card.CardBorder.ContextFlyout = vm?.Actions is null ? null : card.CardMenu;
        if (vm is not null)
        {
            vm.PropertyChanged += card.OnViewModelPropertyChanged;
        }

        card.ApplyState();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MetricCardViewModel.Level) or nameof(MetricCardViewModel.Chart) or nameof(MetricCardViewModel.IsEditing))
        {
            ApplyState();
        }
    }

    private void ApplyState()
    {
        var vm = ViewModel;
        Chart.Kind = vm?.Chart ?? ChartKind.Line;
        var editing = vm is { IsEditing: true, Actions: not null };
        CanDrag = editing;
        AllowDrop = editing;
        RemoveButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;

        switch (vm?.Level ?? ThresholdLevel.Normal)
        {
            case ThresholdLevel.Warning:
                ValueText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
                break;
            case ThresholdLevel.Critical:
                ValueText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                break;
            default:
                ValueText.ClearValue(TextBlock.ForegroundProperty);
                break;
        }
    }

    private void OnMenuOpening(object? sender, object e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        SizeCompact.IsChecked = vm.Size == CardSize.Compact;
        SizeNormal.IsChecked = vm.Size == CardSize.Normal;
        SizeLarge.IsChecked = vm.Size == CardSize.Large;
        ChartLine.IsChecked = vm.Chart == ChartKind.Line;
        ChartArea.IsChecked = vm.Chart == ChartKind.Area;
        ChartBar.IsChecked = vm.Chart == ChartKind.Bar;
    }

    private void OnSizeClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { Actions: { } actions } vm && sender is FrameworkElement { Tag: string tag })
        {
            actions.SetSize(vm, Enum.Parse<CardSize>(tag));
        }
    }

    private void OnChartClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { Actions: { } actions } vm && sender is FrameworkElement { Tag: string tag })
        {
            actions.SetChart(vm, Enum.Parse<ChartKind>(tag));
        }
    }

    private async void OnThresholdsClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { Actions: { } actions } vm)
        {
            return;
        }

        var dialog = new ThresholdDialog(vm.Title, actions.GetThreshold(vm.MetricId)) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.None)
        {
            actions.SetThreshold(vm.MetricId, dialog.Result);
        }
    }

    private void OnMoveEarlierClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { Actions: { } actions } vm)
        {
            actions.Move(vm, -1);
        }
    }

    private void OnMoveLaterClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { Actions: { } actions } vm)
        {
            actions.Move(vm, 1);
        }
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { Actions: { } actions } vm)
        {
            actions.Remove(vm);
        }
    }

    private void OnDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (ViewModel is { } vm)
        {
            args.Data.SetText(vm.MetricId);
            args.Data.RequestedOperation = DataPackageOperation.Move;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.Text))
        {
            e.AcceptedOperation = DataPackageOperation.Move;
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (ViewModel is not { Actions: { } actions } vm || !e.DataView.Contains(StandardDataFormats.Text))
        {
            return;
        }

        var source = await e.DataView.GetTextAsync();
        if (source != vm.MetricId)
        {
            actions.MoveTo(source, vm.MetricId);
        }
    }
}
```

`ThresholdDialog` é usado aqui e criado neste passo para o projeto compilar. `src/Supermonitor/Dialogs/ThresholdDialog.cs`:
```csharp
using Microsoft.UI.Xaml.Controls;
using Supermonitor.Core.Settings;

namespace Supermonitor.Dialogs;

public sealed partial class ThresholdDialog : ContentDialog
{
    private readonly NumberBox _warning;
    private readonly NumberBox _critical;
    private bool _cleared;

    public ThresholdDialog(string metricName, MetricThreshold? current)
    {
        Title = $"Thresholds — {metricName}";
        PrimaryButtonText = "Save";
        SecondaryButtonText = "Clear";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        _warning = new NumberBox { Header = "Warning at", Value = current?.Warning ?? double.NaN, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        _critical = new NumberBox { Header = "Critical at", Value = current?.Critical ?? double.NaN, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Values use the metric's base unit (°C, %, bytes).", TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
                _warning,
                _critical,
            },
        };

        PrimaryButtonClick += (_, args) =>
        {
            if (double.IsNaN(_warning.Value) || double.IsNaN(_critical.Value))
            {
                args.Cancel = true;
            }
        };
        SecondaryButtonClick += (_, _) => _cleared = true;
    }

    public MetricThreshold? Result => _cleared
        ? null
        : new MetricThreshold
        {
            Warning = (float)Math.Min(_warning.Value, _critical.Value),
            Critical = (float)Math.Max(_warning.Value, _critical.Value),
        };
}
```

Em `src/Supermonitor/Views/OverviewPage.xaml`, trocar o `<UniformGridLayout ... />` por `<controls:CardWrapLayout Spacing="12" />`.

- [ ] **Step 5: Build e verificação**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"` → todos passam.
Run: `dotnet build src/Supermonitor/Supermonitor.csproj -c Debug -p:Platform=x64 -p:OutDir=<temp>\` → `0 Erro(s)`.
Se `x:Bind` de função com `ViewModel` nulo gerar erro de compilação XAML, trocar `Width`/`Height`/`Visibility` por atribuição em `ApplyState()` (`Width = CardMetrics.Width(vm?.Size ?? CardSize.Normal)` etc.) e registrar a decisão.

- [ ] **Step 6: Commit**

```powershell
git add -A src/Supermonitor
git commit -m "feat(app): add chart kinds, card sizes, threshold colors and wrap layout"
```

---

### Task 10: Aparência, banner PawnIO e reinício

**Files:**
- Create: `src/Supermonitor.Core/Shell/RestartArguments.cs`, `src/Supermonitor.Core/Sensors/Hardware/PawnIoInstaller.cs`, `src/Supermonitor/Appearance/AppearanceService.cs`
- Modify: `src/Supermonitor/Program.cs`, `src/Supermonitor/App.xaml.cs`, `src/Supermonitor/MainWindow.xaml`, `src/Supermonitor/MainWindow.xaml.cs`
- Test: `tests/Supermonitor.Tests/Shell/RestartArgumentsTests.cs`

**Interfaces:**
- Produces: `RestartArguments.WaitForPid` (`"--wait-for-pid"`), `string[] Build(int processId)`, `bool TryGetWaitPid(IReadOnlyList<string> args, out int processId)`.
- Produces: `PawnIoInstaller.InstallAsync(CancellationToken) -> Task<int>` (código de saída do winget).
- Produces: `AppearanceService.Apply(Window, Panel root, AppSettings)`, `AppearanceService.NotifyChanged()`, `event Action? AppearanceService.Changed`.
- Produces: `App.Restart()`, `App.ConsumeSettingsWarning() -> bool` (true só na primeira chamada), `App.PawnIoBannerDismissed`.

- [ ] **Step 1: Testes que falham**

`tests/Supermonitor.Tests/Shell/RestartArgumentsTests.cs`:
```csharp
using Supermonitor.Core.Shell;

namespace Supermonitor.Tests.Shell;

public class RestartArgumentsTests
{
    [Fact]
    public void Build_and_parse_round_trip()
    {
        var args = RestartArguments.Build(4321);

        Assert.True(RestartArguments.TryGetWaitPid(args, out var pid));
        Assert.Equal(4321, pid);
    }

    [Theory]
    [InlineData(new string[0])]
    [InlineData(new[] { "--minimized" })]
    [InlineData(new[] { "--wait-for-pid" })]
    [InlineData(new[] { "--wait-for-pid", "abc" })]
    [InlineData(new[] { "--wait-for-pid", "-5" })]
    public void Missing_or_invalid_pid_is_rejected(string[] args)
    {
        Assert.False(RestartArguments.TryGetWaitPid(args, out _));
    }
}
```

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"`
Expected: erro de compilação `RestartArguments` não encontrado.

- [ ] **Step 2: Core**

`src/Supermonitor.Core/Shell/RestartArguments.cs`:
```csharp
using System.Globalization;

namespace Supermonitor.Core.Shell;

public static class RestartArguments
{
    public const string WaitForPid = "--wait-for-pid";

    public static string[] Build(int processId) => [WaitForPid, processId.ToString(CultureInfo.InvariantCulture)];

    public static bool TryGetWaitPid(IReadOnlyList<string> args, out int processId)
    {
        processId = 0;
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == WaitForPid)
            {
                return int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out processId) && processId > 0;
            }
        }

        return false;
    }
}
```

`src/Supermonitor.Core/Sensors/Hardware/PawnIoInstaller.cs`:
```csharp
using System.Diagnostics;

namespace Supermonitor.Core.Sensors.Hardware;

public static class PawnIoInstaller
{
    public static async Task<int> InstallAsync(CancellationToken cancellationToken = default)
    {
        using var process = Process.Start(new ProcessStartInfo(
            "winget.exe",
            "install --id namazso.PawnIO --exact --silent --accept-package-agreements --accept-source-agreements")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("winget did not start.");

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }
}
```

Run os testes → passam.

- [ ] **Step 3: Program, App e aparência**

`src/Supermonitor/Program.cs` — antes de `SingleInstance.TryAcquire`:
```csharp
        if (RestartArguments.TryGetWaitPid(args, out var previousPid))
        {
            WaitForExit(previousPid);
        }
```
e o método:
```csharp
    private static void WaitForExit(int processId)
    {
        try
        {
            using var previous = System.Diagnostics.Process.GetProcessById(processId);
            previous.WaitForExit(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Processo anterior já terminou.
        }
    }
```

`src/Supermonitor/App.xaml.cs` — adicionar:
```csharp
    private bool _settingsWarningShown;

    public static new App Current => (App)Application.Current;

    public bool PawnIoBannerDismissed { get; set; }

    public bool ConsumeSettingsWarning()
    {
        if (_settingsWarningShown || _host?.SettingsLoadResult != SettingsLoadResult.RecoveredFromCorrupt)
        {
            return false;
        }

        _settingsWarningShown = true;
        return true;
    }

    public void Restart()
    {
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        foreach (var argument in RestartArguments.Build(Environment.ProcessId))
        {
            start.ArgumentList.Add(argument);
        }

        System.Diagnostics.Process.Start(start);
        ExitApp();
    }
```
(`using Supermonitor.Core.Settings;`).

`src/Supermonitor/Appearance/AppearanceService.cs`:
```csharp
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Supermonitor.Core.Settings;
using Windows.UI;

namespace Supermonitor.Appearance;

public static class AppearanceService
{
    public static event Action? Changed;

    public static void NotifyChanged() => Changed?.Invoke();

    public static void Apply(Window window, Panel root, AppSettings settings)
    {
        root.RequestedTheme = settings.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        window.AppWindow.TitleBar.PreferredTheme = settings.Theme switch
        {
            AppTheme.Light => TitleBarTheme.Light,
            AppTheme.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };

        window.SystemBackdrop = settings.Backdrop switch
        {
            BackdropKind.MicaAlt => new MicaBackdrop { Kind = MicaKind.BaseAlt },
            BackdropKind.Acrylic => new DesktopAcrylicBackdrop(),
            BackdropKind.Solid => null,
            _ => new MicaBackdrop(),
        };

        if (settings.Backdrop == BackdropKind.Solid)
        {
            root.Background = new SolidColorBrush(root.ActualTheme == ElementTheme.Dark
                ? Color.FromArgb(255, 0x20, 0x20, 0x20)
                : Color.FromArgb(255, 0xF3, 0xF3, 0xF3));
        }
        else
        {
            root.ClearValue(Panel.BackgroundProperty);
        }

        AccentPalette.Set(ResolveAccent(settings.AccentColor));
    }

    public static Color ResolveAccent(string? hex) =>
        ColorText.TryParseRgb(hex, out var r, out var g, out var b)
            ? Color.FromArgb(255, r, g, b)
            : (Color)Application.Current.Resources["SystemAccentColor"];
}
```

- [ ] **Step 4: MainWindow**

`src/Supermonitor/MainWindow.xaml` (substituir):
```xml
<Window
    x:Class="Supermonitor.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="Supermonitor">
    <Grid x:Name="RootGrid">
        <Grid.RowDefinitions>
            <RowDefinition Height="48" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>
        <Grid x:Name="TitleBarArea" Padding="16,0,0,0">
            <StackPanel Orientation="Horizontal" Spacing="12" VerticalAlignment="Center">
                <Image Source="ms-appx:///Assets/Supermonitor.ico" Width="16" Height="16" />
                <TextBlock Text="Supermonitor" Style="{StaticResource CaptionTextBlockStyle}" VerticalAlignment="Center" />
            </StackPanel>
        </Grid>
        <StackPanel Grid.Row="1">
            <InfoBar
                x:Name="SettingsInfoBar"
                Severity="Warning"
                Title="Settings were reset"
                Message="The settings file was unreadable and was saved as settings.json.bak. Default settings are in use."
                IsOpen="False" />
            <InfoBar
                x:Name="PawnIoInfoBar"
                Severity="Informational"
                Title="CPU sensors need the PawnIO driver"
                Message="Install it to read CPU temperature, clock and power."
                IsOpen="False"
                Closed="OnPawnIoInfoBarClosed">
                <InfoBar.ActionButton>
                    <Button x:Name="PawnIoButton" Content="Install PawnIO" Click="OnPawnIoButtonClick" />
                </InfoBar.ActionButton>
            </InfoBar>
        </StackPanel>
        <NavigationView
            x:Name="Nav"
            Grid.Row="2"
            IsBackButtonVisible="Collapsed"
            PaneDisplayMode="Left"
            IsSettingsVisible="True"
            SelectionChanged="OnNavSelectionChanged">
            <NavigationView.MenuItems>
                <NavigationViewItem Content="Overview" Tag="overview">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE80F;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
            </NavigationView.MenuItems>
            <Frame x:Name="ContentFrame" />
        </NavigationView>
    </Grid>
</Window>
```

`src/Supermonitor/MainWindow.xaml.cs` — mudanças:
- construtor: depois de `AppWindow.Changed += OnAppWindowChanged;`:
```csharp
        AppearanceService.Apply(this, RootGrid, host.Settings.Current);
        AppearanceService.Changed += OnAppearanceChanged;
        RootGrid.ActualThemeChanged += (_, _) => AppearanceService.Apply(this, RootGrid, _host.Settings.Current);
        Closed += (_, _) => AppearanceService.Changed -= OnAppearanceChanged;

        SettingsInfoBar.IsOpen = App.Current.ConsumeSettingsWarning();
        PawnIoInfoBar.IsOpen = !HardwareProvider.IsPawnIoInstalled && !App.Current.PawnIoBannerDismissed;
```
(remover a linha antiga `SettingsInfoBar.IsOpen = host.SettingsLoadResult == ...`).
- métodos:
```csharp
    private bool _pawnIoReady;

    private void OnAppearanceChanged() => AppearanceService.Apply(this, RootGrid, _host.Settings.Current);

    private void OnPawnIoInfoBarClosed(InfoBar sender, InfoBarClosedEventArgs args) => App.Current.PawnIoBannerDismissed = true;

    private async void OnPawnIoButtonClick(object sender, RoutedEventArgs e)
    {
        if (_pawnIoReady)
        {
            App.Current.Restart();
            return;
        }

        PawnIoButton.IsEnabled = false;
        PawnIoInfoBar.Message = "Installing PawnIO…";
        try
        {
            var exitCode = await PawnIoInstaller.InstallAsync();
            if (exitCode == 0 || HardwareProvider.IsPawnIoInstalled)
            {
                _pawnIoReady = true;
                PawnIoInfoBar.Severity = InfoBarSeverity.Success;
                PawnIoInfoBar.Title = "PawnIO installed";
                PawnIoInfoBar.Message = "Restart Supermonitor to load the driver.";
                PawnIoButton.Content = "Restart now";
            }
            else
            {
                PawnIoInfoBar.Severity = InfoBarSeverity.Error;
                PawnIoInfoBar.Message = $"Installation failed (winget exit code {exitCode}). You can install PawnIO manually from pawnio.eu.";
                PawnIoButton.Content = "Try again";
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Warn("winget could not be started", ex);
            PawnIoInfoBar.Severity = InfoBarSeverity.Error;
            PawnIoInfoBar.Message = "winget is not available. Install PawnIO manually from pawnio.eu.";
        }
        finally
        {
            PawnIoButton.IsEnabled = true;
        }
    }
```
(`using Supermonitor.Appearance; using Supermonitor.Core.Diagnostics; using Supermonitor.Core.Sensors.Hardware;`).

- [ ] **Step 5: Build e verificação manual**

Run: `dotnet test --project tests/Supermonitor.Tests --filter-not-trait "Category=Hardware"` → todos passam.
Run: `dotnet build ... -p:OutDir=<temp>\` → `0 Erro(s)`. Se `TitleBarTheme` tiver nomes de membros diferentes, ajustar para os membros reais do enum em `Microsoft.UI.Windowing` e registrar a decisão.

Peça ao usuário sair pela bandeja e rode `pwsh tools/run-elevated.ps1` (UAC). Expected: janela abre com Mica; temperaturas de CPU e GPU aparecem nos cards (`cpu.temp`, `gpu0.temp`); banner PawnIO não aparece (PawnIO 2.2.0 já instalado nesta máquina).

- [ ] **Step 6: Commit**

```powershell
git add src tests
git commit -m "feat(app): apply theme, backdrop and accent; PawnIO install banner with restart"
```

---

### Task 11: Customização do Overview

**Files:**
- Create: `src/Supermonitor/Dialogs/AddCardDialog.cs`
- Modify: `src/Supermonitor/ViewModels/OverviewViewModel.cs` (substituir), `src/Supermonitor/Views/OverviewPage.xaml(.cs)` (substituir)

**Interfaces:**
- Consumes: `DashboardEditor` (Task 7), `ICardActions`, `MetricCardViewModel` (Task 9), `AppSettings` v2 (Task 6).
- Produces: `OverviewViewModel : ObservableObject, ICardActions, IDisposable` com `Cards`, `IsEditing`, `HistoryIndex` (0..2), `AddCard(string)`, `IReadOnlyList<MetricDescriptor> AvailableMetrics()`.

- [ ] **Step 1: Diálogo de adicionar card**

`src/Supermonitor/Dialogs/AddCardDialog.cs`:
```csharp
using Microsoft.UI.Xaml.Controls;
using Supermonitor.Core.Metrics;

namespace Supermonitor.Dialogs;

public sealed partial class AddCardDialog : ContentDialog
{
    private readonly MetricOption[] _all;
    private readonly ListView _list;

    public AddCardDialog(IReadOnlyList<MetricDescriptor> available)
    {
        Title = "Add card";
        PrimaryButtonText = "Add";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        IsPrimaryButtonEnabled = false;

        _all = available.Select(d => new MetricOption(d.Id, $"{d.DisplayName} — {d.Device ?? d.Category.ToString()}")).ToArray();
        var search = new TextBox { PlaceholderText = "Search metrics" };
        _list = new ListView { Height = 360, SelectionMode = ListViewSelectionMode.Single, ItemsSource = _all };
        search.TextChanged += (_, _) =>
        {
            var term = search.Text.Trim();
            _list.ItemsSource = term.Length == 0
                ? _all
                : _all.Where(o => o.Label.Contains(term, StringComparison.OrdinalIgnoreCase)).ToArray();
        };
        _list.SelectionChanged += (_, _) => IsPrimaryButtonEnabled = _list.SelectedItem is MetricOption;
        Content = new StackPanel { Spacing = 8, Width = 420, Children = { search, _list } };
    }

    public string? SelectedMetricId => (_list.SelectedItem as MetricOption)?.Id;

    private sealed record MetricOption(string Id, string Label)
    {
        public override string ToString() => Label;
    }
}
```

- [ ] **Step 2: ViewModel**

`src/Supermonitor/ViewModels/OverviewViewModel.cs` (substituir):
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Supermonitor.Core;
using Supermonitor.Core.Dashboard;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Settings;

namespace Supermonitor.ViewModels;

public sealed partial class OverviewViewModel : ObservableObject, ICardActions, IDisposable
{
    private readonly AppHost _host;
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<bool> _isMinimized;
    private readonly DispatcherQueueHandler _refreshHandler;
    private int _refreshQueued;

    public OverviewViewModel(AppHost host, DispatcherQueue dispatcher, Func<bool> isMinimized)
    {
        _host = host;
        _dispatcher = dispatcher;
        _isMinimized = isMinimized;
        _refreshHandler = RefreshNow;
        foreach (var card in Settings.Cards)
        {
            Cards.Add(CreateCard(card));
        }

        RefreshNow();
        host.Hub.SampleReady += OnSampleReady;
        host.Metrics.MetricsChanged += OnSampleReady;
    }

    public ObservableCollection<MetricCardViewModel> Cards { get; } = [];

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    public int HistoryIndex
    {
        get => Math.Max(0, AppSettings.AllowedHistoryMinutes.ToList().IndexOf(Settings.HistoryMinutes));
        set
        {
            if (value < 0 || value >= AppSettings.AllowedHistoryMinutes.Count)
            {
                return;
            }

            Settings.HistoryMinutes = AppSettings.AllowedHistoryMinutes[value];
            Save();
            RefreshNow();
        }
    }

    private AppSettings Settings => _host.Settings.Current;

    public IReadOnlyList<MetricDescriptor> AvailableMetrics() =>
        _host.Metrics.GetDescriptors()
            .Where(d => DashboardEditor.IndexOf(Settings.Cards, d.Id) < 0)
            .OrderBy(d => d.Category)
            .ThenBy(d => d.Device, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public void AddCard(string metricId)
    {
        if (DashboardEditor.Add(Settings.Cards, metricId))
        {
            var card = CreateCard(Settings.Cards[^1]);
            card.IsEditing = IsEditing;
            Cards.Add(card);
            Save();
            RefreshNow();
        }
    }

    public void Remove(MetricCardViewModel card)
    {
        if (DashboardEditor.Remove(Settings.Cards, card.MetricId))
        {
            Cards.Remove(card);
            Save();
        }
    }

    public void Move(MetricCardViewModel card, int delta)
    {
        var from = Cards.IndexOf(card);
        var to = DashboardEditor.MoveBy(Settings.Cards, card.MetricId, delta);
        if (from >= 0 && to >= 0 && from != to)
        {
            Cards.Move(from, to);
            Save();
        }
    }

    public void MoveTo(string metricId, string targetMetricId)
    {
        var from = IndexOfCard(metricId);
        var target = DashboardEditor.IndexOf(Settings.Cards, targetMetricId);
        if (from < 0 || target < 0)
        {
            return;
        }

        var to = DashboardEditor.MoveTo(Settings.Cards, metricId, target);
        if (to != from)
        {
            Cards.Move(from, to);
            Save();
        }
    }

    public void SetSize(MetricCardViewModel card, CardSize size)
    {
        if (FindSettings(card.MetricId) is { } settings)
        {
            settings.Size = size;
            card.Size = size;
            Save();
        }
    }

    public void SetChart(MetricCardViewModel card, ChartKind chart)
    {
        if (FindSettings(card.MetricId) is { } settings)
        {
            settings.Chart = chart;
            card.Chart = chart;
            Save();
        }
    }

    public MetricThreshold? GetThreshold(string metricId) => Settings.Thresholds.GetValueOrDefault(metricId);

    public void SetThreshold(string metricId, MetricThreshold? threshold)
    {
        if (threshold is null)
        {
            Settings.Thresholds.Remove(metricId);
        }
        else
        {
            Settings.Thresholds[metricId] = threshold;
        }

        Save();
        RefreshNow();
    }

    public void Dispose()
    {
        _host.Hub.SampleReady -= OnSampleReady;
        _host.Metrics.MetricsChanged -= OnSampleReady;
    }

    partial void OnIsEditingChanged(bool value)
    {
        foreach (var card in Cards)
        {
            card.IsEditing = value;
        }
    }

    private MetricCardViewModel CreateCard(CardSettings settings) =>
        new(settings.MetricId, _host.Metrics) { Size = settings.Size, Chart = settings.Chart, Actions = this };

    private CardSettings? FindSettings(string metricId)
    {
        var index = DashboardEditor.IndexOf(Settings.Cards, metricId);
        return index < 0 ? null : Settings.Cards[index];
    }

    private int IndexOfCard(string metricId)
    {
        for (var i = 0; i < Cards.Count; i++)
        {
            if (Cards[i].MetricId == metricId)
            {
                return i;
            }
        }

        return -1;
    }

    private void Save() => _host.Settings.RequestSave();

    private void OnSampleReady()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
        {
            return;
        }

        if (!_dispatcher.TryEnqueue(DispatcherQueuePriority.Low, _refreshHandler))
        {
            Volatile.Write(ref _refreshQueued, 0);
        }
    }

    private void RefreshNow()
    {
        Volatile.Write(ref _refreshQueued, 0);
        if (_isMinimized())
        {
            return;
        }

        var settings = Settings;
        var points = Math.Clamp(settings.HistoryMinutes * 60_000 / settings.SampleIntervalMs, 2, _host.Metrics.HistoryCapacity);
        var options = settings.ToUnitOptions();
        foreach (var card in Cards)
        {
            card.PointCount = points;
            card.Refresh(options, settings.Thresholds.GetValueOrDefault(card.MetricId));
        }
    }
}
```

- [ ] **Step 3: Página**

`src/Supermonitor/Views/OverviewPage.xaml` (substituir):
```xml
<Page
    x:Class="Supermonitor.Views.OverviewPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:controls="using:Supermonitor.Controls"
    xmlns:vm="using:Supermonitor.ViewModels">
    <ScrollViewer Padding="24">
        <StackPanel Spacing="16">
            <Grid ColumnSpacing="8">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <TextBlock Text="Overview" Style="{StaticResource TitleTextBlockStyle}" />
                <ComboBox x:Name="HistoryBox" Grid.Column="1" VerticalAlignment="Center" SelectionChanged="OnHistoryChanged" ToolTipService.ToolTip="Chart history">
                    <ComboBoxItem Content="1 min" />
                    <ComboBoxItem Content="5 min" />
                    <ComboBoxItem Content="10 min" />
                </ComboBox>
                <Button x:Name="AddButton" Grid.Column="2" Visibility="Collapsed" Click="OnAddClick">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <FontIcon Glyph="&#xE710;" FontSize="14" />
                        <TextBlock Text="Add card" />
                    </StackPanel>
                </Button>
                <ToggleButton x:Name="EditButton" Grid.Column="3" Checked="OnEditToggled" Unchecked="OnEditToggled">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <FontIcon Glyph="&#xE70F;" FontSize="14" />
                        <TextBlock Text="Customize" />
                    </StackPanel>
                </ToggleButton>
            </Grid>
            <TextBlock
                x:Name="EditHint"
                Visibility="Collapsed"
                Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                Text="Drag cards to reorder. Right-click a card to change its size, chart, or thresholds." />
            <ItemsRepeater ItemsSource="{x:Bind ViewModel.Cards, Mode=OneWay}">
                <ItemsRepeater.Layout>
                    <controls:CardWrapLayout Spacing="12" />
                </ItemsRepeater.Layout>
                <ItemsRepeater.ItemTemplate>
                    <DataTemplate x:DataType="vm:MetricCardViewModel">
                        <controls:MetricCard ViewModel="{x:Bind}" />
                    </DataTemplate>
                </ItemsRepeater.ItemTemplate>
            </ItemsRepeater>
        </StackPanel>
    </ScrollViewer>
</Page>
```

`src/Supermonitor/Views/OverviewPage.xaml.cs` (substituir):
```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Supermonitor.Dialogs;
using Supermonitor.ViewModels;

namespace Supermonitor.Views;

public sealed partial class OverviewPage : Page
{
    public OverviewPage()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
        {
            ViewModel?.Dispose();
            ViewModel = null;
        };
    }

    public OverviewViewModel? ViewModel { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var context = (PageContext)e.Parameter;
        ViewModel = new OverviewViewModel(context.Host, DispatcherQueue, context.IsMinimized);
        HistoryBox.SelectedIndex = ViewModel.HistoryIndex;
        Bindings.Update();
    }

    private void OnHistoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not null && HistoryBox.SelectedIndex >= 0)
        {
            ViewModel.HistoryIndex = HistoryBox.SelectedIndex;
        }
    }

    private void OnEditToggled(object sender, RoutedEventArgs e)
    {
        var editing = EditButton.IsChecked == true;
        if (ViewModel is not null)
        {
            ViewModel.IsEditing = editing;
        }

        AddButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        EditHint.Visibility = AddButton.Visibility;
    }

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var dialog = new AddCardDialog(ViewModel.AvailableMetrics()) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.SelectedMetricId is { } id)
        {
            ViewModel.AddCard(id);
        }
    }
}
```

- [ ] **Step 4: Build e verificação manual**

Run: testes de unidade → passam. Build com `-p:OutDir=<temp>\` → `0 Erro(s)`.
Usuário sai pela bandeja; `pwsh tools/run-elevated.ps1`. Expected:
1. "Customize" mostra "Add card", dica e botão de remover em cada card.
2. Arrastar um card para outro troca a posição; reabrir o app mantém a ordem.
3. Botão direito → Size Large/Compact muda o card; Chart Area/Bar muda o gráfico.
4. Thresholds… em "CPU temperature" com Warning 30 e Critical 40 → valor fica amarelo/vermelho.
5. "Add card" → buscar "hot spot" → adicionar "GPU hot spot".
6. Histórico 5 min → gráficos mostram janela maior.

- [ ] **Step 5: Commit**

```powershell
git add src/Supermonitor
git commit -m "feat(app): customizable overview with drag reorder, sizes, charts and thresholds"
```

---

### Task 12: Páginas de detalhe por categoria

**Files:**
- Create: `src/Supermonitor/ViewModels/MetricGroupViewModel.cs`, `ProcessRowViewModel.cs`, `CategoryViewModel.cs`
- Create: `src/Supermonitor/Controls/MemoryCompositionBar.xaml(.cs)`
- Create: `src/Supermonitor/Views/CategoryPage.xaml(.cs)`
- Modify: `src/Supermonitor/MainWindow.xaml(.cs)` (itens de navegação)

**Interfaces:**
- Consumes: `MetricStore.GetDescriptors()`, `ProcessMonitor.Snapshot`, `ProcessSorting.Top` (Task 8), `MemoryComposition` (Task 8), `Demand.Processes`.
- Produces: `sealed record CategoryContext(PageContext Page, MetricCategory Category, string Title)`.
- Produces: `CategoryViewModel(AppHost, MetricCategory, string title, DispatcherQueue, Func<bool>)` com `Title`, `Groups`, `TopProcesses`, `ShowsProcesses`, `IsMemory`, `event Action<MemoryComposition>? MemoryUpdated`, `Dispose()`.
- Produces: `ProcessRowViewModel` (`Name`, `Pid`, `CpuText`, `MemoryText`, `GpuText`, `Update(ProcessSample, UnitOptions)`).

- [ ] **Step 1: ViewModels**

`src/Supermonitor/ViewModels/MetricGroupViewModel.cs`:
```csharp
using System.Collections.ObjectModel;

namespace Supermonitor.ViewModels;

public sealed record MetricGroupViewModel(string Name, ObservableCollection<MetricCardViewModel> Items);

public sealed record CategoryContext(PageContext Page, Core.Metrics.MetricCategory Category, string Title);
```

`src/Supermonitor/ViewModels/ProcessRowViewModel.cs`:
```csharp
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors.Processes;

namespace Supermonitor.ViewModels;

public sealed partial class ProcessRowViewModel : ObservableObject
{
    public ProcessRowViewModel()
    {
        Name = string.Empty;
        Pid = string.Empty;
        CpuText = string.Empty;
        MemoryText = string.Empty;
        GpuText = string.Empty;
    }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Pid { get; set; }

    [ObservableProperty]
    public partial string CpuText { get; set; }

    [ObservableProperty]
    public partial string MemoryText { get; set; }

    [ObservableProperty]
    public partial string GpuText { get; set; }

    public void Update(ProcessSample sample, UnitOptions options)
    {
        Name = sample.Name;
        Pid = sample.ProcessId.ToString(CultureInfo.InvariantCulture);
        CpuText = float.IsFinite(sample.CpuPercent) ? sample.CpuPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%" : "—";
        MemoryText = UnitFormatter.Format(sample.PrivateBytes, MetricUnit.Bytes, options);
        GpuText = sample.GpuPercent > 0 ? sample.GpuPercent.ToString("0", CultureInfo.InvariantCulture) + "%" : "—";
    }
}
```

`src/Supermonitor/ViewModels/CategoryViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Supermonitor.Core;
using Supermonitor.Core.Dashboard;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors;
using Supermonitor.Core.Sensors.Processes;
using Supermonitor.Core.Settings;

namespace Supermonitor.ViewModels;

public sealed class CategoryViewModel : IDisposable
{
    private const int CompactGroupThreshold = 8;
    private const int TopProcessCount = 10;

    private readonly AppHost _host;
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<bool> _isMinimized;
    private readonly DispatcherQueueHandler _refreshHandler;
    private readonly IDisposable? _processLease;
    private readonly ProcessSortKey _sortKey;
    private int _refreshQueued;
    private int _descriptorCount = -1;

    public CategoryViewModel(AppHost host, MetricCategory category, string title, DispatcherQueue dispatcher, Func<bool> isMinimized)
    {
        _host = host;
        _dispatcher = dispatcher;
        _isMinimized = isMinimized;
        _refreshHandler = RefreshNow;
        Category = category;
        Title = title;
        IsMemory = category == MetricCategory.Memory;
        ShowsProcesses = category is MetricCategory.Cpu or MetricCategory.Memory or MetricCategory.Gpu;
        _sortKey = category switch
        {
            MetricCategory.Memory => ProcessSortKey.Memory,
            MetricCategory.Gpu => ProcessSortKey.Gpu,
            _ => ProcessSortKey.Cpu,
        };

        if (ShowsProcesses)
        {
            _processLease = host.Demand.Acquire(Demand.Processes);
            for (var i = 0; i < TopProcessCount; i++)
            {
                TopProcesses.Add(new ProcessRowViewModel());
            }
        }

        RefreshNow();
        host.Hub.SampleReady += OnSampleReady;
        host.Metrics.MetricsChanged += OnSampleReady;
    }

    public event Action<MemoryComposition>? MemoryUpdated;

    public MetricCategory Category { get; }

    public string Title { get; }

    public bool IsMemory { get; }

    public bool ShowsProcesses { get; }

    public ObservableCollection<MetricGroupViewModel> Groups { get; } = [];

    public ObservableCollection<ProcessRowViewModel> TopProcesses { get; } = [];

    public void Dispose()
    {
        _host.Hub.SampleReady -= OnSampleReady;
        _host.Metrics.MetricsChanged -= OnSampleReady;
        _processLease?.Dispose();
    }

    private void OnSampleReady()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
        {
            return;
        }

        if (!_dispatcher.TryEnqueue(DispatcherQueuePriority.Low, _refreshHandler))
        {
            Volatile.Write(ref _refreshQueued, 0);
        }
    }

    private void RefreshNow()
    {
        Volatile.Write(ref _refreshQueued, 0);
        if (_isMinimized())
        {
            return;
        }

        var descriptors = _host.Metrics.GetDescriptors().Where(d => d.Category == Category).ToArray();
        if (descriptors.Length != _descriptorCount)
        {
            Rebuild(descriptors);
        }

        var settings = _host.Settings.Current;
        var options = settings.ToUnitOptions();
        var points = Math.Clamp(settings.HistoryMinutes * 60_000 / settings.SampleIntervalMs, 2, _host.Metrics.HistoryCapacity);
        foreach (var group in Groups)
        {
            foreach (var card in group.Items)
            {
                card.PointCount = points;
                card.Refresh(options, settings.Thresholds.GetValueOrDefault(card.MetricId));
            }
        }

        if (ShowsProcesses)
        {
            var top = ProcessSorting.Top(_host.Processes.Snapshot, _sortKey, null, TopProcessCount);
            for (var i = 0; i < TopProcesses.Count; i++)
            {
                if (i < top.Length)
                {
                    TopProcesses[i].Update(top[i], options);
                }
                else
                {
                    TopProcesses[i].Update(new ProcessSample(0, string.Empty, float.NaN, 0, 0), options);
                }
            }
        }

        if (IsMemory)
        {
            var store = _host.Metrics;
            MemoryUpdated?.Invoke(MemoryComposition.From(
                store.GetLatest(MetricIds.MemoryTotal),
                store.GetLatest(MetricIds.MemoryUsed),
                store.GetLatest(MetricIds.MemoryModified),
                store.GetLatest(MetricIds.MemoryStandby),
                store.GetLatest(MetricIds.MemoryFree)));
        }
    }

    private void Rebuild(IReadOnlyList<MetricDescriptor> descriptors)
    {
        _descriptorCount = descriptors.Count;
        Groups.Clear();
        foreach (var group in descriptors.GroupBy(d => d.Device ?? "General"))
        {
            var items = group.ToArray();
            var size = items.Length > CompactGroupThreshold ? CardSize.Compact : CardSize.Normal;
            var cards = new ObservableCollection<MetricCardViewModel>(
                items.Select(d => new MetricCardViewModel(d.Id, _host.Metrics) { Size = size }));
            Groups.Add(new MetricGroupViewModel(group.Key, cards));
        }
    }
}
```

- [ ] **Step 2: Barra de composição de memória**

`src/Supermonitor/Controls/MemoryCompositionBar.xaml`:
```xml
<UserControl
    x:Class="Supermonitor.Controls.MemoryCompositionBar"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel Spacing="8">
        <Grid x:Name="Bar" Height="28" CornerRadius="4" BorderThickness="1" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}">
            <Grid.ColumnDefinitions>
                <ColumnDefinition x:Name="InUseColumn" Width="0*" />
                <ColumnDefinition x:Name="ModifiedColumn" Width="0*" />
                <ColumnDefinition x:Name="StandbyColumn" Width="0*" />
                <ColumnDefinition x:Name="FreeColumn" Width="1*" />
            </Grid.ColumnDefinitions>
            <Rectangle x:Name="InUseRect" Grid.Column="0" />
            <Rectangle Grid.Column="1" Fill="#E8A33D" />
            <Rectangle Grid.Column="2" Fill="#7A8FA6" Opacity="0.6" />
        </Grid>
        <TextBlock x:Name="Legend" Foreground="{ThemeResource TextFillColorSecondaryBrush}" TextWrapping="Wrap" />
    </StackPanel>
</UserControl>
```

`src/Supermonitor/Controls/MemoryCompositionBar.xaml.cs`:
```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Supermonitor.Appearance;
using Supermonitor.Core.Dashboard;
using Supermonitor.Core.Metrics;

namespace Supermonitor.Controls;

public sealed partial class MemoryCompositionBar : UserControl
{
    public MemoryCompositionBar()
    {
        InitializeComponent();
    }

    public void Update(MemoryComposition composition, UnitOptions options)
    {
        InUseRect.Fill = new SolidColorBrush(AccentPalette.Current);
        InUseColumn.Width = new GridLength(composition.InUse, GridUnitType.Star);
        ModifiedColumn.Width = new GridLength(composition.Modified, GridUnitType.Star);
        StandbyColumn.Width = new GridLength(composition.Standby, GridUnitType.Star);
        FreeColumn.Width = new GridLength(composition.Free, GridUnitType.Star);

        string F(double bytes) => UnitFormatter.Format(bytes, MetricUnit.Bytes, options);
        Legend.Text = $"In use {F(composition.InUse)} · Modified {F(composition.Modified)} · Standby {F(composition.Standby)} · Free {F(composition.Free)}";
    }
}
```

- [ ] **Step 3: Página**

`src/Supermonitor/Views/CategoryPage.xaml`:
```xml
<Page
    x:Class="Supermonitor.Views.CategoryPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:controls="using:Supermonitor.Controls"
    xmlns:vm="using:Supermonitor.ViewModels">
    <ScrollViewer Padding="24">
        <StackPanel Spacing="20">
            <TextBlock Text="{x:Bind ViewModel.Title, Mode=OneWay}" Style="{StaticResource TitleTextBlockStyle}" />
            <controls:MemoryCompositionBar x:Name="MemoryBar" Visibility="Collapsed" />
            <ItemsRepeater ItemsSource="{x:Bind ViewModel.Groups, Mode=OneWay}">
                <ItemsRepeater.Layout>
                    <StackLayout Spacing="20" />
                </ItemsRepeater.Layout>
                <ItemsRepeater.ItemTemplate>
                    <DataTemplate x:DataType="vm:MetricGroupViewModel">
                        <StackPanel Spacing="8">
                            <TextBlock Text="{x:Bind Name}" Style="{StaticResource SubtitleTextBlockStyle}" />
                            <ItemsRepeater ItemsSource="{x:Bind Items}">
                                <ItemsRepeater.Layout>
                                    <controls:CardWrapLayout Spacing="12" />
                                </ItemsRepeater.Layout>
                                <ItemsRepeater.ItemTemplate>
                                    <DataTemplate x:DataType="vm:MetricCardViewModel">
                                        <controls:MetricCard ViewModel="{x:Bind}" />
                                    </DataTemplate>
                                </ItemsRepeater.ItemTemplate>
                            </ItemsRepeater>
                        </StackPanel>
                    </DataTemplate>
                </ItemsRepeater.ItemTemplate>
            </ItemsRepeater>
            <StackPanel x:Name="ProcessesPanel" Spacing="8" Visibility="Collapsed">
                <TextBlock Text="Top processes" Style="{StaticResource SubtitleTextBlockStyle}" />
                <ItemsRepeater ItemsSource="{x:Bind ViewModel.TopProcesses, Mode=OneWay}">
                    <ItemsRepeater.ItemTemplate>
                        <DataTemplate x:DataType="vm:ProcessRowViewModel">
                            <Grid ColumnSpacing="12" Padding="8,4">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="70" />
                                    <ColumnDefinition Width="70" />
                                    <ColumnDefinition Width="100" />
                                    <ColumnDefinition Width="60" />
                                </Grid.ColumnDefinitions>
                                <TextBlock Text="{x:Bind Name, Mode=OneWay}" TextTrimming="CharacterEllipsis" />
                                <TextBlock Grid.Column="1" Text="{x:Bind Pid, Mode=OneWay}" Foreground="{ThemeResource TextFillColorSecondaryBrush}" />
                                <TextBlock Grid.Column="2" Text="{x:Bind CpuText, Mode=OneWay}" HorizontalAlignment="Right" />
                                <TextBlock Grid.Column="3" Text="{x:Bind MemoryText, Mode=OneWay}" HorizontalAlignment="Right" />
                                <TextBlock Grid.Column="4" Text="{x:Bind GpuText, Mode=OneWay}" HorizontalAlignment="Right" />
                            </Grid>
                        </DataTemplate>
                    </ItemsRepeater.ItemTemplate>
                </ItemsRepeater>
            </StackPanel>
        </StackPanel>
    </ScrollViewer>
</Page>
```

`src/Supermonitor/Views/CategoryPage.xaml.cs`:
```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Supermonitor.Core;
using Supermonitor.Core.Dashboard;
using Supermonitor.ViewModels;

namespace Supermonitor.Views;

public sealed partial class CategoryPage : Page
{
    private AppHost? _host;

    public CategoryPage()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
        {
            if (ViewModel is not null)
            {
                ViewModel.MemoryUpdated -= OnMemoryUpdated;
                ViewModel.Dispose();
                ViewModel = null;
            }
        };
    }

    public CategoryViewModel? ViewModel { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var context = (CategoryContext)e.Parameter;
        _host = context.Page.Host;
        ViewModel = new CategoryViewModel(_host, context.Category, context.Title, DispatcherQueue, context.Page.IsMinimized);
        MemoryBar.Visibility = ViewModel.IsMemory ? Visibility.Visible : Visibility.Collapsed;
        ProcessesPanel.Visibility = ViewModel.ShowsProcesses ? Visibility.Visible : Visibility.Collapsed;
        ViewModel.MemoryUpdated += OnMemoryUpdated;
        Bindings.Update();
    }

    private void OnMemoryUpdated(MemoryComposition composition) =>
        MemoryBar.Update(composition, _host!.Settings.Current.ToUnitOptions());
}
```

- [ ] **Step 4: Navegação**

Em `MainWindow.xaml`, depois do item Overview:
```xml
                <NavigationViewItem Content="CPU" Tag="cpu">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE950;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
                <NavigationViewItem Content="Memory" Tag="memory">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE964;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
                <NavigationViewItem Content="GPU" Tag="gpu">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xF211;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
                <NavigationViewItem Content="Disks" Tag="disk">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xEDA2;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
                <NavigationViewItem Content="Network" Tag="network">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE968;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
```

Em `MainWindow.Navigate`, adicionar casos (`using Supermonitor.Core.Metrics;`):
```csharp
            case "cpu":
                ContentFrame.Navigate(typeof(CategoryPage), new CategoryContext(context, MetricCategory.Cpu, "CPU"));
                break;
            case "memory":
                ContentFrame.Navigate(typeof(CategoryPage), new CategoryContext(context, MetricCategory.Memory, "Memory"));
                break;
            case "gpu":
                ContentFrame.Navigate(typeof(CategoryPage), new CategoryContext(context, MetricCategory.Gpu, "GPU"));
                break;
            case "disk":
                ContentFrame.Navigate(typeof(CategoryPage), new CategoryContext(context, MetricCategory.Disk, "Disks"));
                break;
            case "network":
                ContentFrame.Navigate(typeof(CategoryPage), new CategoryContext(context, MetricCategory.Network, "Network"));
                break;
```

- [ ] **Step 5: Build e verificação manual**

Run: testes de unidade → passam. Build com `-p:OutDir=<temp>\` → `0 Erro(s)`.
Usuário sai pela bandeja; `pwsh tools/run-elevated.ps1`. Expected:
1. CPU: grupo "CPU", grupo "CPU cores" (16 cards compactos), grupo "AMD Ryzen 7 7800X3D" com temperatura, clocks e potência; top processos por CPU.
2. Memory: barra de composição com legenda; top processos por memória.
3. GPU: grupo "AMD Radeon RX 9070 XT" com uso por engine, VRAM, temperaturas, clock, fan, potência; top processos por GPU.
4. Disks: um grupo por disco e "Drives"; temperatura SMART aparece sob o nome do SSD.
5. Network: um grupo por adaptador, "Network" e "Latency".
6. Ícones da navegação batem com os glifos listados em Global Constraints.

- [ ] **Step 6: Commit**

```powershell
git add src/Supermonitor
git commit -m "feat(app): add CPU, Memory, GPU, Disks and Network detail pages"
```

---

### Task 13: Página de processos

**Files:**
- Create: `src/Supermonitor/ViewModels/ProcessesViewModel.cs`, `src/Supermonitor/Views/ProcessesPage.xaml(.cs)`
- Modify: `src/Supermonitor/MainWindow.xaml(.cs)`

**Interfaces:**
- Consumes: `ProcessSorting.Top`, `ProcessRowViewModel` (Task 12), `Demand.Processes`.
- Produces: `ProcessesViewModel(AppHost, DispatcherQueue, Func<bool>)` com `Rows`, `SortKey`, `Filter`, `Dispose()`, propriedade observável `CountText`.

- [ ] **Step 1: ViewModel**

`src/Supermonitor/ViewModels/ProcessesViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Supermonitor.Core;
using Supermonitor.Core.Sensors;
using Supermonitor.Core.Sensors.Processes;

namespace Supermonitor.ViewModels;

public sealed partial class ProcessesViewModel : ObservableObject, IDisposable
{
    private const int MaxRows = 100;

    private readonly AppHost _host;
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<bool> _isMinimized;
    private readonly DispatcherQueueHandler _refreshHandler;
    private readonly IDisposable _lease;
    private int _refreshQueued;

    public ProcessesViewModel(AppHost host, DispatcherQueue dispatcher, Func<bool> isMinimized)
    {
        _host = host;
        _dispatcher = dispatcher;
        _isMinimized = isMinimized;
        _refreshHandler = RefreshNow;
        _lease = host.Demand.Acquire(Demand.Processes);
        CountText = string.Empty;
        host.Hub.SampleReady += OnSampleReady;
    }

    public ObservableCollection<ProcessRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    public partial string CountText { get; set; }

    public ProcessSortKey SortKey { get; private set; } = ProcessSortKey.Cpu;

    public string? Filter { get; private set; }

    public void SortBy(ProcessSortKey key)
    {
        SortKey = key;
        RefreshNow();
    }

    public void SetFilter(string? filter)
    {
        Filter = filter;
        RefreshNow();
    }

    public void Dispose()
    {
        _host.Hub.SampleReady -= OnSampleReady;
        _lease.Dispose();
    }

    private void OnSampleReady()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
        {
            return;
        }

        if (!_dispatcher.TryEnqueue(DispatcherQueuePriority.Low, _refreshHandler))
        {
            Volatile.Write(ref _refreshQueued, 0);
        }
    }

    private void RefreshNow()
    {
        Volatile.Write(ref _refreshQueued, 0);
        if (_isMinimized())
        {
            return;
        }

        var snapshot = _host.Processes.Snapshot;
        var top = ProcessSorting.Top(snapshot, SortKey, Filter, MaxRows);
        var options = _host.Settings.Current.ToUnitOptions();
        while (Rows.Count < top.Length)
        {
            Rows.Add(new ProcessRowViewModel());
        }

        while (Rows.Count > top.Length)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }

        for (var i = 0; i < top.Length; i++)
        {
            Rows[i].Update(top[i], options);
        }

        CountText = $"{snapshot.Count} processes";
    }
}
```

- [ ] **Step 2: Página**

`src/Supermonitor/Views/ProcessesPage.xaml`:
```xml
<Page
    x:Class="Supermonitor.Views.ProcessesPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:vm="using:Supermonitor.ViewModels">
    <Grid Padding="24" RowSpacing="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>
        <Grid ColumnSpacing="12">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="280" />
            </Grid.ColumnDefinitions>
            <TextBlock Text="Processes" Style="{StaticResource TitleTextBlockStyle}" />
            <TextBlock Grid.Column="1" Text="{x:Bind ViewModel.CountText, Mode=OneWay}" VerticalAlignment="Center" Foreground="{ThemeResource TextFillColorSecondaryBrush}" />
            <TextBox Grid.Column="2" PlaceholderText="Filter by name" TextChanged="OnFilterChanged" />
        </Grid>
        <Grid Grid.Row="1" ColumnSpacing="12" Padding="8,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="70" />
                <ColumnDefinition Width="70" />
                <ColumnDefinition Width="100" />
                <ColumnDefinition Width="60" />
            </Grid.ColumnDefinitions>
            <HyperlinkButton Content="Name" Tag="Name" Click="OnSortClick" Padding="0" />
            <TextBlock Grid.Column="1" Text="PID" VerticalAlignment="Center" />
            <HyperlinkButton Grid.Column="2" Content="CPU" Tag="Cpu" Click="OnSortClick" HorizontalAlignment="Right" Padding="0" />
            <HyperlinkButton Grid.Column="3" Content="Memory" Tag="Memory" Click="OnSortClick" HorizontalAlignment="Right" Padding="0" />
            <HyperlinkButton Grid.Column="4" Content="GPU" Tag="Gpu" Click="OnSortClick" HorizontalAlignment="Right" Padding="0" />
        </Grid>
        <ScrollViewer Grid.Row="2">
            <ItemsRepeater ItemsSource="{x:Bind ViewModel.Rows, Mode=OneWay}">
                <ItemsRepeater.ItemTemplate>
                    <DataTemplate x:DataType="vm:ProcessRowViewModel">
                        <Grid ColumnSpacing="12" Padding="8,4">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="70" />
                                <ColumnDefinition Width="70" />
                                <ColumnDefinition Width="100" />
                                <ColumnDefinition Width="60" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="{x:Bind Name, Mode=OneWay}" TextTrimming="CharacterEllipsis" />
                            <TextBlock Grid.Column="1" Text="{x:Bind Pid, Mode=OneWay}" Foreground="{ThemeResource TextFillColorSecondaryBrush}" />
                            <TextBlock Grid.Column="2" Text="{x:Bind CpuText, Mode=OneWay}" HorizontalAlignment="Right" />
                            <TextBlock Grid.Column="3" Text="{x:Bind MemoryText, Mode=OneWay}" HorizontalAlignment="Right" />
                            <TextBlock Grid.Column="4" Text="{x:Bind GpuText, Mode=OneWay}" HorizontalAlignment="Right" />
                        </Grid>
                    </DataTemplate>
                </ItemsRepeater.ItemTemplate>
            </ItemsRepeater>
        </ScrollViewer>
    </Grid>
</Page>
```

`src/Supermonitor/Views/ProcessesPage.xaml.cs`:
```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Supermonitor.Core.Sensors.Processes;
using Supermonitor.ViewModels;

namespace Supermonitor.Views;

public sealed partial class ProcessesPage : Page
{
    public ProcessesPage()
    {
        InitializeComponent();
        Unloaded += (_, _) =>
        {
            ViewModel?.Dispose();
            ViewModel = null;
        };
    }

    public ProcessesViewModel? ViewModel { get; private set; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var context = (PageContext)e.Parameter;
        ViewModel = new ProcessesViewModel(context.Host, DispatcherQueue, context.IsMinimized);
        Bindings.Update();
    }

    private void OnSortClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
        {
            ViewModel?.SortBy(Enum.Parse<ProcessSortKey>(tag));
        }
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e) =>
        ViewModel?.SetFilter(((TextBox)sender).Text);
}
```

- [ ] **Step 3: Navegação**

Em `MainWindow.xaml`, depois de Network:
```xml
                <NavigationViewItem Content="Processes" Tag="processes">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE8FD;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
```
Em `Navigate`:
```csharp
            case "processes":
                ContentFrame.Navigate(typeof(ProcessesPage), context);
                break;
```

- [ ] **Step 4: Build e verificação manual**

Run: testes de unidade → passam. Build → `0 Erro(s)`.
Usuário: abrir Processes. Expected: lista ordenada por CPU com nomes, PID, CPU%, memória privada e GPU%; clicar "Memory" reordena; filtro "chrome" filtra; contagem bate aproximadamente com o Gerenciador de Tarefas (aba Detalhes).

- [ ] **Step 5: Commit**

```powershell
git add src/Supermonitor
git commit -m "feat(app): add sortable, filterable processes page"
```

---

### Task 14: Página de configurações v2

**Files:**
- Modify: `src/Supermonitor/Views/SettingsPage.xaml(.cs)` (substituir), `src/Supermonitor.Core/Shell/AutostartManager.cs`

**Interfaces:**
- Consumes: `AppSettings` v2 (Task 6), `AppearanceService.NotifyChanged` (Task 10), `ColorText` (Task 6), `SensorHub.GetStatuses()`, `HardwareProvider.IsPawnIoInstalled`.
- Produces: seções Appearance (tema, fundo, cor de destaque), Units, Monitoring (intervalo), Startup, Sensors (status do PawnIO e dos provedores).

- [ ] **Step 1: Timeout do schtasks**

Em `AutostartManager.RunSchtasks`, trocar `process.WaitForExit(10_000); return process.ExitCode;` por:
```csharp
        if (!process.WaitForExit(10_000))
        {
            process.Kill();
            return -1;
        }

        return process.ExitCode;
```

- [ ] **Step 2: Página**

`src/Supermonitor/Views/SettingsPage.xaml` (substituir):
```xml
<Page
    x:Class="Supermonitor.Views.SettingsPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <ScrollViewer Padding="24">
        <StackPanel Spacing="16" MaxWidth="720" HorizontalAlignment="Left">
            <TextBlock Text="Settings" Style="{StaticResource TitleTextBlockStyle}" />

            <TextBlock Text="Appearance" Style="{StaticResource SubtitleTextBlockStyle}" />
            <RadioButtons x:Name="ThemeButtons" Header="Theme" SelectionChanged="OnThemeChanged">
                <RadioButton Content="Use system setting" />
                <RadioButton Content="Light" />
                <RadioButton Content="Dark" />
            </RadioButtons>
            <ComboBox x:Name="BackdropBox" Header="Window background" MinWidth="200" SelectionChanged="OnBackdropChanged">
                <ComboBoxItem Content="Mica" />
                <ComboBoxItem Content="Mica Alt" />
                <ComboBoxItem Content="Acrylic" />
                <ComboBoxItem Content="Solid" />
            </ComboBox>
            <ToggleSwitch x:Name="SystemAccentSwitch" Header="Use the Windows accent color for charts" Toggled="OnSystemAccentToggled" />
            <ColorPicker x:Name="AccentPicker" IsAlphaEnabled="False" IsMoreButtonVisible="False" ColorChanged="OnAccentChanged" Visibility="Collapsed" />

            <TextBlock Text="Units" Style="{StaticResource SubtitleTextBlockStyle}" />
            <ToggleSwitch x:Name="BitsSwitch" Header="Show network speed in bits per second" Toggled="OnUnitsToggled" />
            <ToggleSwitch x:Name="FahrenheitSwitch" Header="Show temperatures in °F" Toggled="OnUnitsToggled" />

            <TextBlock Text="Monitoring" Style="{StaticResource SubtitleTextBlockStyle}" />
            <ComboBox x:Name="IntervalBox" Header="Update interval" MinWidth="200" SelectionChanged="OnIntervalChanged">
                <ComboBoxItem Content="250 ms" />
                <ComboBoxItem Content="500 ms" />
                <ComboBoxItem Content="1 second" />
                <ComboBoxItem Content="2 seconds" />
            </ComboBox>
            <StackPanel Spacing="4">
                <TextBox x:Name="PingHostBox" Header="Ping host" Width="320" HorizontalAlignment="Left" LostFocus="OnPingHostLostFocus" />
                <TextBlock x:Name="PingHostError" Visibility="Collapsed" Foreground="{ThemeResource SystemFillColorCriticalBrush}" TextWrapping="Wrap" />
            </StackPanel>

            <TextBlock Text="Startup" Style="{StaticResource SubtitleTextBlockStyle}" />
            <ToggleSwitch x:Name="StartWithWindowsSwitch" Header="Start with Windows" Toggled="OnStartWithWindowsToggled" />
            <TextBlock x:Name="AutostartError" Visibility="Collapsed" Foreground="{ThemeResource SystemFillColorCriticalBrush}" TextWrapping="Wrap" />
            <ToggleSwitch x:Name="StartMinimizedSwitch" Header="Start minimized to the system tray" Toggled="OnStartMinimizedToggled" />

            <TextBlock Text="Sensors" Style="{StaticResource SubtitleTextBlockStyle}" />
            <TextBlock x:Name="PawnIoStatus" TextWrapping="Wrap" />
            <TextBlock x:Name="ProviderStatus" TextWrapping="Wrap" Foreground="{ThemeResource TextFillColorSecondaryBrush}" />
        </StackPanel>
    </ScrollViewer>
</Page>
```

`src/Supermonitor/Views/SettingsPage.xaml.cs` (substituir):
```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Supermonitor.Appearance;
using Supermonitor.Core;
using Supermonitor.Core.Diagnostics;
using Supermonitor.Core.Sensors.Hardware;
using Supermonitor.Core.Settings;
using Supermonitor.Core.Shell;
using Supermonitor.ViewModels;

namespace Supermonitor.Views;

public sealed partial class SettingsPage : Page
{
    private AppHost? _host;
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
    }

    private AppSettings Settings => _host!.Settings.Current;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        _host = ((PageContext)e.Parameter).Host;
        _loading = true;
        ThemeButtons.SelectedIndex = (int)Settings.Theme;
        BackdropBox.SelectedIndex = (int)Settings.Backdrop;
        SystemAccentSwitch.IsOn = Settings.AccentColor is null;
        AccentPicker.Visibility = SystemAccentSwitch.IsOn ? Visibility.Collapsed : Visibility.Visible;
        AccentPicker.Color = AppearanceService.ResolveAccent(Settings.AccentColor);
        BitsSwitch.IsOn = Settings.NetworkInBits;
        FahrenheitSwitch.IsOn = Settings.Fahrenheit;
        IntervalBox.SelectedIndex = Math.Max(0, AppSettings.AllowedIntervalsMs.ToList().IndexOf(Settings.SampleIntervalMs));
        PingHostBox.Text = Settings.PingHost;
        StartMinimizedSwitch.IsOn = Settings.StartMinimized;
        PawnIoStatus.Text = HardwareProvider.IsPawnIoInstalled
            ? "PawnIO driver: installed. CPU temperature, clock and power are available."
            : "PawnIO driver: not installed. CPU temperature, clock and power are unavailable.";
        ProviderStatus.Text = string.Join(
            Environment.NewLine,
            _host.Hub.GetStatuses().Select(s => s.IsAvailable ? $"{s.Name}: available" : $"{s.Name}: unavailable ({s.UnavailableReason})"));

        try
        {
            StartWithWindowsSwitch.IsOn = await Task.Run(AutostartManager.IsEnabled);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Warn("Could not read the startup task", ex);
            StartWithWindowsSwitch.IsEnabled = false;
        }
        finally
        {
            _loading = false;
        }
    }

    private void SaveAppearance()
    {
        _host!.Settings.RequestSave();
        AppearanceService.NotifyChanged();
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _host is null || ThemeButtons.SelectedIndex < 0)
        {
            return;
        }

        Settings.Theme = (AppTheme)ThemeButtons.SelectedIndex;
        SaveAppearance();
    }

    private void OnBackdropChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _host is null || BackdropBox.SelectedIndex < 0)
        {
            return;
        }

        Settings.Backdrop = (BackdropKind)BackdropBox.SelectedIndex;
        SaveAppearance();
    }

    private void OnSystemAccentToggled(object sender, RoutedEventArgs e)
    {
        AccentPicker.Visibility = SystemAccentSwitch.IsOn ? Visibility.Collapsed : Visibility.Visible;
        if (_loading || _host is null)
        {
            return;
        }

        var color = AccentPicker.Color;
        Settings.AccentColor = SystemAccentSwitch.IsOn ? null : ColorText.ToHex(color.R, color.G, color.B);
        SaveAppearance();
    }

    private void OnAccentChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_loading || _host is null || SystemAccentSwitch.IsOn)
        {
            return;
        }

        Settings.AccentColor = ColorText.ToHex(args.NewColor.R, args.NewColor.G, args.NewColor.B);
        SaveAppearance();
    }

    private void OnUnitsToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || _host is null)
        {
            return;
        }

        Settings.NetworkInBits = BitsSwitch.IsOn;
        Settings.Fahrenheit = FahrenheitSwitch.IsOn;
        _host.Settings.RequestSave();
    }

    private void OnIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _host is null || IntervalBox.SelectedIndex < 0)
        {
            return;
        }

        var milliseconds = AppSettings.AllowedIntervalsMs[IntervalBox.SelectedIndex];
        Settings.SampleIntervalMs = milliseconds;
        _host.Hub.Interval = TimeSpan.FromMilliseconds(milliseconds);
        _host.Settings.RequestSave();
    }

    private void OnPingHostLostFocus(object sender, RoutedEventArgs e)
    {
        if (_host is null)
        {
            return;
        }

        var host = PingHostBox.Text.Trim();
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            PingHostError.Text = "Enter a valid host name or IP address.";
            PingHostError.Visibility = Visibility.Visible;
            PingHostBox.Text = Settings.PingHost;
            return;
        }

        PingHostError.Visibility = Visibility.Collapsed;
        Settings.PingHost = host;
        _host.Settings.RequestSave();
    }

    private async void OnStartWithWindowsToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || _host is null)
        {
            return;
        }

        var enable = StartWithWindowsSwitch.IsOn;
        try
        {
            await Task.Run(() => AutostartManager.SetEnabled(enable, Environment.ProcessPath!));
            AutostartError.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Log.Warn("Changing the startup task failed", ex);
            AutostartError.Text = $"Could not change the startup task: {ex.Message}";
            AutostartError.Visibility = Visibility.Visible;
            _loading = true;
            StartWithWindowsSwitch.IsOn = !enable;
            _loading = false;
        }
    }

    private void OnStartMinimizedToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || _host is null)
        {
            return;
        }

        Settings.StartMinimized = StartMinimizedSwitch.IsOn;
        _host.Settings.RequestSave();
    }
}
```

- [ ] **Step 3: Build e verificação manual**

Run: suíte completa → passa. Build → `0 Erro(s)`.
Usuário: em Settings, trocar tema para Dark/Light (janela e barra de título mudam na hora), fundo Acrylic/Solid, desligar "Use the Windows accent color" e escolher uma cor (gráficos mudam de cor), ligar bits e °F (cards mostram Mbps e °F), conferir "Sensors" (PawnIO instalado; todos os provedores "available").

- [ ] **Step 4: Commit**

```powershell
git add src
git commit -m "feat(app): settings page with appearance, units and sensor status"
```

---

### Task 15: Verificação de desempenho e fechamento

**Files:**
- Create: `docs/superpowers/plans/2026-10-10-supermonitor-02-results.md`

**Interfaces:**
- Consumes: app completo.
- Produces: registro dos critérios de desempenho com LHM e processos ativos.

- [ ] **Step 1: Testes**

Run: `dotnet test --project tests/Supermonitor.Tests`
Expected: todos passam (unidade + hardware).

- [ ] **Step 2: Medir no build Release**

1. `dotnet build src/Supermonitor/Supermonitor.csproj -c Release -p:Platform=x64`; usuário sai do app pela bandeja e abre o exe de `bin\x64\Release\…\win-x64\` como administrador.
2. Overview aberto, intervalo 1 s, 60 s: `pwsh tools/measure-cpu.ps1`. Critério: < 1%.
3. Página Processes aberta, 60 s: `pwsh tools/measure-cpu.ps1`. Registrar (sem critério na spec; referência para o Plano 3).
4. Janela fechada (só bandeja), 60 s: `pwsh tools/measure-cpu.ps1`. Critério: < 0,3% (prova que as leases de LHM e processos foram liberadas).

Se um critério falhar, aplicar superpowers:systematic-debugging. Suspeito principal: `HardwareProvider.Sample` (~9 ms/tick medidos no probe) ou lease não liberada.

- [ ] **Step 3: Registrar resultados**

`docs/superpowers/plans/2026-10-10-supermonitor-02-results.md`: data, build, CPU e memória nos três cenários, resultado dos testes, verificações manuais das Tasks 10–14 e desvios do plano com as decisões tomadas.

- [ ] **Step 4: Commit**

```powershell
git add docs/superpowers/plans/2026-10-10-supermonitor-02-results.md
git commit -m "docs: record hardware and dashboard plan results"
```
