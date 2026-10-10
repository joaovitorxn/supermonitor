# Supermonitor Plano 1 — Fundação: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** App WinUI 3 funcional que roda como admin na bandeja e mostra um dashboard Overview com CPU, RAM, GPU, disco, rede e ping em tempo real, com custo mínimo de CPU.

**Architecture:** `Supermonitor.Core` (sem UI) contém um `SensorHub` que chama provedores (`ISensorProvider`) em uma thread de fundo e grava valores `float` em ring buffers pré-alocados de uma `MetricStore`. O app WinUI 3 (`Supermonitor`) lê a store e desenha cards com gráficos Win2D. Ícone de bandeja, instância única, autostart e EcoQoS ficam no Core como Win32 puro.

**Tech Stack:** .NET 10 (SDK 10.0.401), C# 14, Windows App SDK 2.x (pacotes de componente `Microsoft.WindowsAppSDK.WinUI` 2.3.9 + `Microsoft.WindowsAppSDK.Foundation` 2.3.12), Win2D 1.4.0, CommunityToolkit.Mvvm 8.4.2, xUnit v3 4.0.2, `Microsoft.Extensions.TimeProvider.Testing` 10.10.0. P/Invoke via `LibraryImport`.

**Spec:** `docs/superpowers/specs/2026-10-10-supermonitor-design.md`
**Índice:** `docs/superpowers/plans/2026-10-10-supermonitor-plan-index.md`

## Global Constraints

- Windows 11 apenas; `TargetFramework` `net10.0-windows10.0.22621.0`, `TargetPlatformMinVersion` `10.0.22000.0`, x64.
- App unpackaged: `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`.
- Manifest `requireAdministrator`. O exe não roda via `dotnet run` sem shell elevado; use `tools/run-elevated.ps1`.
- Textos da UI em inglês (EN-US). Valor ausente sempre exibido como `N/A`.
- Configurações em `%LocalAppData%\Supermonitor\settings.json`; logs em `%LocalAppData%\Supermonitor\logs`, 7 dias.
- Nenhuma alocação por tick no caminho quente dos provedores (strings de id e handles criados no `Initialize`).
- Falha de provedor nunca derruba o app: métrica vira `NaN` e o erro vai para o log.
- Histórico: `MetricStore` com capacidade de 2400 amostras por métrica (10 min a 250 ms).
- Testes de unidade: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"`. Testes de hardware: `--filter "Category=Hardware"`.
- Todo commit termina com a linha `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. Arquivo de configurações com valores fora da faixa ou tipo errado (`"sampleIntervalMs": 7`, `"sampleIntervalMs": "fast"`, `"pingHost": ""`) → o app inicia com valores normalizados ou padrões, sem crash. Testes na Task 6.
2. Instâncias de contadores que aparecem e somem (processos usando a GPU que encerram, contador de rede que zera, nomes malformados) → valores ignorados ou zerados, nunca exceção. Testes na Task 10 (`RateCalculator`) e Task 12 (`GpuEngineAggregator`).
3. Segunda execução do exe com o app já aberto → ativa a janela existente e encerra, sem segundo processo. Testes na Task 13.
4. Handler de `SampleReady` que lança exceção ou provedor que falha sempre → o hub continua amostrando os demais. Testes na Task 5.
5. Máquina com adaptador de vídeo virtual (o PC do usuário tem "SudoMaker Virtual Display Adapter") ou GPU de software → só GPUs reais aparecem, com índice estável. Testes na Task 12 (`GpuAdapterFilter`).

---

## File Structure

```
Supermonitor.slnx
global.json
Directory.Build.props
.gitignore
tools/
  make-icon.ps1                      Gera Assets/Supermonitor.ico
  run-elevated.ps1                   Build + executa o app elevado
  measure-cpu.ps1                    Mede CPU/RAM do processo
src/Supermonitor.Core/
  Supermonitor.Core.csproj
  AppHost.cs                         Raiz de composição (settings, store, provedores, hub, ping)
  Collections/RingBuffer.cs
  Diagnostics/Log.cs, LogRetention.cs
  Metrics/MetricUnit.cs              MetricUnit, MetricCategory, MetricDescriptor, MetricHandle
  Metrics/MetricIds.cs
  Metrics/MetricStore.cs
  Metrics/UnitFormatter.cs           UnitFormatter, UnitOptions
  Sensors/ISensorProvider.cs         ISensorProvider, ProviderStatus
  Sensors/SensorHub.cs
  Sensors/Pdh/PdhQuery.cs            PdhQuery, PdhCounter, PdhArray, PdhException
  Sensors/Cpu/CpuInstanceName.cs, CpuProvider.cs
  Sensors/Memory/MemoryProvider.cs
  Sensors/Disk/DiskInstanceName.cs, DiskProvider.cs, DriveSpaceProvider.cs
  Sensors/Network/RateCalculator.cs, NetworkAdapterFilter.cs, NetworkProvider.cs
  Sensors/Network/PingStatistics.cs, PingMonitor.cs, PingProvider.cs
  Sensors/Gpu/GpuAdapterInfo.cs      GpuAdapterInfo, GpuAdapterFilter, DxgiAdapters
  Sensors/Gpu/GpuEngineAggregator.cs
  Sensors/Gpu/GpuProvider.cs
  Settings/AppSettings.cs, SettingsMigrator.cs, SettingsJsonContext.cs, SettingsStore.cs
  Shell/AppPaths.cs, SingleInstance.cs, HiddenWindow.cs, TrayIcon.cs, WindowActivation.cs
  Shell/AutostartManager.cs, ProcessEfficiency.cs, Privileges.cs
  Interop/Pdh.cs, Kernel32.cs, Psapi.cs, Ntdll.cs, Iphlpapi.cs, Dxgi.cs, User32.cs, Shell32.cs
src/Supermonitor/
  Supermonitor.csproj, app.manifest, Program.cs
  App.xaml, App.xaml.cs
  MainWindow.xaml, MainWindow.xaml.cs
  Assets/Supermonitor.ico
  ViewModels/PageContext.cs, MetricCardViewModel.cs, OverviewViewModel.cs
  Controls/ISeriesSource.cs, SparklineChart.cs, MetricCard.xaml, MetricCard.xaml.cs
  Views/OverviewPage.xaml(.cs), SettingsPage.xaml(.cs)
tests/Supermonitor.Tests/
  Supermonitor.Tests.csproj
  (um arquivo de teste por unidade, listado em cada task)
  Hardware/ProviderSmokeTests.cs
```

---

### Task 1: Toolchain, solução e log

**Files:**
- Create: `global.json`, `Directory.Build.props`, `.gitignore`, `Supermonitor.slnx`
- Create: `src/Supermonitor.Core/Supermonitor.Core.csproj`
- Create: `src/Supermonitor.Core/Diagnostics/LogRetention.cs`, `src/Supermonitor.Core/Diagnostics/Log.cs`
- Create: `tests/Supermonitor.Tests/Supermonitor.Tests.csproj`
- Test: `tests/Supermonitor.Tests/Diagnostics/LogRetentionTests.cs`

**Interfaces:**
- Produces: `Supermonitor.Core.Diagnostics.Log` com `static void Initialize(string directory)`, `Info(string)`, `Warn(string, Exception? = null)`, `Error(string, Exception? = null)`. Antes de `Initialize`, todas as chamadas são no-op.
- Produces: `LogRetention.FilePrefix` (`"supermonitor-"`), `LogRetention.KeepDays` (7), `static IEnumerable<string> FilesToDelete(IEnumerable<string> fileNames, DateOnly today, int keepDays)`.

- [ ] **Step 1: Instalar o .NET 10 SDK**

Run (abre UAC; o usuário aprova):
```powershell
winget install --id Microsoft.DotNet.SDK.10 --exact --accept-source-agreements --accept-package-agreements
```
Depois, em um shell novo: `dotnet --list-sdks`
Expected: linha começando com `10.0.401`.

- [ ] **Step 2: Criar arquivos da raiz**

`global.json`:
```json
{
  "sdk": {
    "version": "10.0.401",
    "rollForward": "latestFeature"
  }
}
```

`Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.22000.0</TargetPlatformMinVersion>
    <SupportedOSPlatformVersion>10.0.22000.0</SupportedOSPlatformVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <PlatformTarget>x64</PlatformTarget>
    <SatelliteResourceLanguages>en-US</SatelliteResourceLanguages>
  </PropertyGroup>
</Project>
```

`.gitignore`:
```
bin/
obj/
.vs/
*.user
TestResults/
```

`src/Supermonitor.Core/Supermonitor.Core.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <InternalsVisibleTo Include="Supermonitor.Tests" />
  </ItemGroup>
</Project>
```

`tests/Supermonitor.Tests/Supermonitor.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3" Version="4.0.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="4.0.1" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.10.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Supermonitor.Core\Supermonitor.Core.csproj" />
  </ItemGroup>
</Project>
```

Run:
```powershell
dotnet new sln -n Supermonitor --format slnx
dotnet sln Supermonitor.slnx add src/Supermonitor.Core/Supermonitor.Core.csproj tests/Supermonitor.Tests/Supermonitor.Tests.csproj
```
Expected: `Supermonitor.slnx` com os dois projetos.

- [ ] **Step 3: Escrever o teste que falha**

`tests/Supermonitor.Tests/Diagnostics/LogRetentionTests.cs`:
```csharp
using Supermonitor.Core.Diagnostics;

namespace Supermonitor.Tests.Diagnostics;

public class LogRetentionTests
{
    [Fact]
    public void Deletes_only_log_files_older_than_the_keep_window()
    {
        string[] files =
        [
            "supermonitor-20261003.log",
            "supermonitor-20261004.log",
            "supermonitor-20261010.log",
            "other.txt",
            "supermonitor-garbage.log",
        ];

        var result = LogRetention.FilesToDelete(files, new DateOnly(2026, 10, 10), keepDays: 7).ToArray();

        Assert.Equal(new[] { "supermonitor-20261003.log" }, result);
    }
}
```

- [ ] **Step 4: Rodar e ver falhar**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"`
Expected: erro de compilação `The type or namespace name 'LogRetention' could not be found`.

- [ ] **Step 5: Implementar**

`src/Supermonitor.Core/Diagnostics/LogRetention.cs`:
```csharp
using System.Globalization;

namespace Supermonitor.Core.Diagnostics;

public static class LogRetention
{
    public const string FilePrefix = "supermonitor-";
    public const int KeepDays = 7;

    public static IEnumerable<string> FilesToDelete(IEnumerable<string> fileNames, DateOnly today, int keepDays)
    {
        var cutoff = today.AddDays(-(keepDays - 1));
        foreach (var name in fileNames)
        {
            if (!name.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var datePart = name.AsSpan(FilePrefix.Length, name.Length - FilePrefix.Length - ".log".Length);
            if (DateOnly.TryParseExact(datePart, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                && date < cutoff)
            {
                yield return name;
            }
        }
    }
}
```

`src/Supermonitor.Core/Diagnostics/Log.cs`:
```csharp
namespace Supermonitor.Core.Diagnostics;

public static class Log
{
    private static readonly Lock Gate = new();
    private static string? _directory;

    public static void Initialize(string directory)
    {
        Directory.CreateDirectory(directory);
        _directory = directory;
        try
        {
            var names = Directory.EnumerateFiles(directory).Select(Path.GetFileName).OfType<string>();
            foreach (var name in LogRetention.FilesToDelete(names, DateOnly.FromDateTime(DateTime.Now), LogRetention.KeepDays).ToArray())
            {
                File.Delete(Path.Combine(directory, name));
            }
        }
        catch (IOException)
        {
        }
    }

    public static void Info(string message) => Write("INF", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WRN", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        var directory = _directory;
        if (directory is null)
        {
            return;
        }

        var now = DateTime.Now;
        var line = exception is null
            ? $"{now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}"
            : $"{now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}{exception}{Environment.NewLine}";

        lock (Gate)
        {
            try
            {
                File.AppendAllText(Path.Combine(directory, $"{LogRetention.FilePrefix}{now:yyyyMMdd}.log"), line);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
```

- [ ] **Step 6: Rodar e ver passar**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"`
Expected: `Passed: 1`.

- [ ] **Step 7: Commit**

```powershell
git add global.json Directory.Build.props .gitignore Supermonitor.slnx src tests
git commit -m "build: scaffold solution with Core, tests and file logger"
```

---

### Task 2: RingBuffer

**Files:**
- Create: `src/Supermonitor.Core/Collections/RingBuffer.cs`
- Test: `tests/Supermonitor.Tests/Collections/RingBufferTests.cs`

**Interfaces:**
- Produces: `Supermonitor.Core.Collections.RingBuffer<T> where T : struct` com `RingBuffer(int capacity)`, `int Capacity`, `int Count`, `void Add(T)`, `T this[int index]` (0 = mais antigo), `T Latest`, `int CopyLast(Span<T> destination)` (copia os mais recentes em ordem cronológica, retorna quantidade), `void Clear()`. Não é thread-safe.

- [ ] **Step 1: Escrever os testes que falham**

`tests/Supermonitor.Tests/Collections/RingBufferTests.cs`:
```csharp
using Supermonitor.Core.Collections;

namespace Supermonitor.Tests.Collections;

public class RingBufferTests
{
    [Fact]
    public void Add_within_capacity_keeps_insertion_order()
    {
        var buffer = new RingBuffer<int>(3);
        buffer.Add(1);
        buffer.Add(2);

        Assert.Equal(2, buffer.Count);
        Assert.Equal(1, buffer[0]);
        Assert.Equal(2, buffer[1]);
        Assert.Equal(2, buffer.Latest);
    }

    [Fact]
    public void Add_beyond_capacity_overwrites_oldest()
    {
        var buffer = new RingBuffer<int>(3);
        for (var i = 1; i <= 5; i++)
        {
            buffer.Add(i);
        }

        Assert.Equal(3, buffer.Count);
        Assert.Equal(new[] { 3, 4, 5 }, new[] { buffer[0], buffer[1], buffer[2] });
    }

    [Fact]
    public void CopyLast_returns_most_recent_items_in_chronological_order()
    {
        var buffer = new RingBuffer<int>(4);
        for (var i = 1; i <= 6; i++)
        {
            buffer.Add(i);
        }

        Span<int> destination = stackalloc int[3];
        var copied = buffer.CopyLast(destination);

        Assert.Equal(3, copied);
        Assert.Equal(new[] { 4, 5, 6 }, destination.ToArray());
    }

    [Fact]
    public void CopyLast_with_larger_destination_copies_everything()
    {
        var buffer = new RingBuffer<int>(4);
        buffer.Add(7);
        buffer.Add(8);
        var destination = new int[10];

        Assert.Equal(2, buffer.CopyLast(destination));
        Assert.Equal(7, destination[0]);
        Assert.Equal(8, destination[1]);
    }

    [Fact]
    public void Indexer_out_of_range_throws()
    {
        var buffer = new RingBuffer<int>(2);
        buffer.Add(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer[1]);
    }

    [Fact]
    public void Latest_on_empty_buffer_throws()
    {
        Assert.Throws<InvalidOperationException>(() => new RingBuffer<int>(1).Latest);
    }

    [Fact]
    public void Capacity_below_one_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer<int>(0));
    }

    [Fact]
    public void Clear_empties_the_buffer()
    {
        var buffer = new RingBuffer<int>(2);
        buffer.Add(1);
        buffer.Clear();

        Assert.Equal(0, buffer.Count);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"`
Expected: erro de compilação `RingBuffer<>` não encontrado.

- [ ] **Step 3: Implementar**

`src/Supermonitor.Core/Collections/RingBuffer.cs`:
```csharp
namespace Supermonitor.Core.Collections;

public sealed class RingBuffer<T> where T : struct
{
    private readonly T[] _items;
    private int _start;
    private int _count;

    public RingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _items = new T[capacity];
    }

    public int Capacity => _items.Length;

    public int Count => _count;

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _items[(_start + index) % _items.Length];
        }
    }

    public T Latest => _count == 0
        ? throw new InvalidOperationException("Buffer is empty.")
        : this[_count - 1];

    public void Add(T item)
    {
        var index = (_start + _count) % _items.Length;
        _items[index] = item;
        if (_count < _items.Length)
        {
            _count++;
        }
        else
        {
            _start = (_start + 1) % _items.Length;
        }
    }

    public int CopyLast(Span<T> destination)
    {
        var copied = Math.Min(destination.Length, _count);
        var first = _count - copied;
        for (var i = 0; i < copied; i++)
        {
            destination[i] = this[first + i];
        }

        return copied;
    }

    public void Clear()
    {
        _start = 0;
        _count = 0;
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"`
Expected: `Passed: 9`.

- [ ] **Step 5: Commit**

```powershell
git add src/Supermonitor.Core/Collections tests/Supermonitor.Tests/Collections
git commit -m "feat(core): add preallocated ring buffer"
```

---

### Task 3: Modelo de métricas e formatação de unidades

**Files:**
- Create: `src/Supermonitor.Core/Metrics/MetricUnit.cs`, `src/Supermonitor.Core/Metrics/MetricIds.cs`, `src/Supermonitor.Core/Metrics/UnitFormatter.cs`
- Test: `tests/Supermonitor.Tests/Metrics/UnitFormatterTests.cs`

**Interfaces:**
- Produces: `enum MetricUnit { Percent, Bytes, BytesPerSecond, Celsius, Megahertz, Watts, Volts, Rpm, Milliseconds, FramesPerSecond, Count }`.
- Produces: `enum MetricCategory { Cpu, Memory, Gpu, Disk, Network, Frame, System }`.
- Produces: `sealed record MetricDescriptor(string Id, string DisplayName, MetricCategory Category, MetricUnit Unit)`.
- Produces: `readonly record struct MetricHandle(int Index)`.
- Produces: `MetricIds` (constantes e funções listadas no código abaixo).
- Produces: `sealed record UnitOptions(bool NetworkInBits = false, bool Fahrenheit = false)` com `static UnitOptions Default`; `UnitFormatter.Format(double value, MetricUnit unit, UnitOptions options) -> string`.

- [ ] **Step 1: Escrever os testes que falham**

`tests/Supermonitor.Tests/Metrics/UnitFormatterTests.cs`:
```csharp
using Supermonitor.Core.Metrics;

namespace Supermonitor.Tests.Metrics;

public class UnitFormatterTests
{
    [Theory]
    [InlineData(42.4, MetricUnit.Percent, "42%")]
    [InlineData(0, MetricUnit.Bytes, "0 B")]
    [InlineData(1536, MetricUnit.Bytes, "1.5 KB")]
    [InlineData(157286400, MetricUnit.Bytes, "150 MB")]
    [InlineData(33930241638, MetricUnit.Bytes, "31.6 GB")]
    [InlineData(2097152, MetricUnit.BytesPerSecond, "2.0 MB/s")]
    [InlineData(80, MetricUnit.Celsius, "80 °C")]
    [InlineData(5050, MetricUnit.Megahertz, "5.05 GHz")]
    [InlineData(800, MetricUnit.Megahertz, "800 MHz")]
    [InlineData(16.66, MetricUnit.Milliseconds, "16.7 ms")]
    [InlineData(143.6, MetricUnit.FramesPerSecond, "144 FPS")]
    [InlineData(1.2346, MetricUnit.Volts, "1.235 V")]
    [InlineData(120.26, MetricUnit.Watts, "120.3 W")]
    [InlineData(1450, MetricUnit.Rpm, "1450 RPM")]
    public void Formats_values_with_default_options(double value, MetricUnit unit, string expected)
    {
        Assert.Equal(expected, UnitFormatter.Format(value, unit, UnitOptions.Default));
    }

    [Fact]
    public void Network_rate_in_bits_uses_decimal_prefixes()
    {
        Assert.Equal("10.0 Mbps", UnitFormatter.Format(1_250_000, MetricUnit.BytesPerSecond, new UnitOptions(NetworkInBits: true)));
    }

    [Fact]
    public void Fahrenheit_option_converts_celsius()
    {
        Assert.Equal("176 °F", UnitFormatter.Format(80, MetricUnit.Celsius, new UnitOptions(Fahrenheit: true)));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_values_render_as_not_available(double value)
    {
        Assert.Equal("N/A", UnitFormatter.Format(value, MetricUnit.Percent, UnitOptions.Default));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"`
Expected: erro de compilação `UnitFormatter` não encontrado.

- [ ] **Step 3: Implementar**

`src/Supermonitor.Core/Metrics/MetricUnit.cs`:
```csharp
namespace Supermonitor.Core.Metrics;

public enum MetricUnit
{
    Percent,
    Bytes,
    BytesPerSecond,
    Celsius,
    Megahertz,
    Watts,
    Volts,
    Rpm,
    Milliseconds,
    FramesPerSecond,
    Count,
}

public enum MetricCategory
{
    Cpu,
    Memory,
    Gpu,
    Disk,
    Network,
    Frame,
    System,
}

public sealed record MetricDescriptor(string Id, string DisplayName, MetricCategory Category, MetricUnit Unit);

public readonly record struct MetricHandle(int Index);
```

`src/Supermonitor.Core/Metrics/MetricIds.cs`:
```csharp
namespace Supermonitor.Core.Metrics;

public static class MetricIds
{
    public const string CpuUsage = "cpu.usage";

    public const string MemoryTotal = "mem.total";
    public const string MemoryUsed = "mem.used";
    public const string MemoryAvailable = "mem.available";
    public const string MemoryUsage = "mem.usage";
    public const string MemoryStandby = "mem.standby";
    public const string MemoryFree = "mem.free";
    public const string MemoryModified = "mem.modified";
    public const string MemoryCommitted = "mem.commit";
    public const string MemoryCommitLimit = "mem.commitlimit";
    public const string MemoryCache = "mem.cache";

    public const string NetworkTotalDown = "net.down";
    public const string NetworkTotalUp = "net.up";
    public const string Ping = "net.ping";
    public const string PingJitter = "net.jitter";
    public const string PingLoss = "net.loss";

    public static string CpuCoreUsage(int core) => $"cpu.core{core}.usage";

    public static string GpuUsage(int gpu) => $"gpu{gpu}.usage";

    public static string Gpu3D(int gpu) => $"gpu{gpu}.3d";

    public static string GpuCopy(int gpu) => $"gpu{gpu}.copy";

    public static string GpuVideoDecode(int gpu) => $"gpu{gpu}.decode";

    public static string GpuVideoEncode(int gpu) => $"gpu{gpu}.encode";

    public static string GpuCompute(int gpu) => $"gpu{gpu}.compute";

    public static string GpuVramUsed(int gpu) => $"gpu{gpu}.vram.used";

    public static string GpuVramTotal(int gpu) => $"gpu{gpu}.vram.total";

    public static string GpuSharedUsed(int gpu) => $"gpu{gpu}.shared.used";

    public static string DiskRead(int disk) => $"disk{disk}.read";

    public static string DiskWrite(int disk) => $"disk{disk}.write";

    public static string DiskActive(int disk) => $"disk{disk}.active";

    public static string DiskQueue(int disk) => $"disk{disk}.queue";

    public static string DriveFree(char letter) => $"drive.{char.ToUpperInvariant(letter)}.free";

    public static string DriveTotal(char letter) => $"drive.{char.ToUpperInvariant(letter)}.total";

    public static string DriveUsage(char letter) => $"drive.{char.ToUpperInvariant(letter)}.usage";

    public static string NetworkDown(int adapter) => $"net{adapter}.down";

    public static string NetworkUp(int adapter) => $"net{adapter}.up";
}
```

`src/Supermonitor.Core/Metrics/UnitFormatter.cs`:
```csharp
using System.Globalization;

namespace Supermonitor.Core.Metrics;

public sealed record UnitOptions(bool NetworkInBits = false, bool Fahrenheit = false)
{
    public static UnitOptions Default { get; } = new();
}

public static class UnitFormatter
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");
    private static readonly string[] ByteUnits = ["B", "KB", "MB", "GB", "TB"];
    private static readonly string[] BitUnits = ["bps", "Kbps", "Mbps", "Gbps"];

    public static string Format(double value, MetricUnit unit, UnitOptions options)
    {
        if (!double.IsFinite(value))
        {
            return "N/A";
        }

        return unit switch
        {
            MetricUnit.Percent => value.ToString("0", Culture) + "%",
            MetricUnit.Bytes => FormatBytes(value, string.Empty),
            MetricUnit.BytesPerSecond => options.NetworkInBits ? FormatBits(value * 8) : FormatBytes(value, "/s"),
            MetricUnit.Celsius => options.Fahrenheit
                ? (value * 9 / 5 + 32).ToString("0", Culture) + " °F"
                : value.ToString("0", Culture) + " °C",
            MetricUnit.Megahertz => value >= 1000
                ? (value / 1000).ToString("0.00", Culture) + " GHz"
                : value.ToString("0", Culture) + " MHz",
            MetricUnit.Watts => value.ToString("0.0", Culture) + " W",
            MetricUnit.Volts => value.ToString("0.000", Culture) + " V",
            MetricUnit.Rpm => value.ToString("0", Culture) + " RPM",
            MetricUnit.Milliseconds => value.ToString("0.0", Culture) + " ms",
            MetricUnit.FramesPerSecond => value.ToString("0", Culture) + " FPS",
            _ => value.ToString("0", Culture),
        };
    }

    private static string FormatBytes(double value, string suffix)
    {
        var index = 0;
        while (Math.Abs(value) >= 1024 && index < ByteUnits.Length - 1)
        {
            value /= 1024;
            index++;
        }

        var format = index == 0 || Math.Abs(value) >= 100 ? "0" : "0.0";
        return value.ToString(format, Culture) + " " + ByteUnits[index] + suffix;
    }

    private static string FormatBits(double bits)
    {
        var index = 0;
        while (Math.Abs(bits) >= 1000 && index < BitUnits.Length - 1)
        {
            bits /= 1000;
            index++;
        }

        var format = index == 0 || Math.Abs(bits) >= 100 ? "0" : "0.0";
        return bits.ToString(format, Culture) + " " + BitUnits[index];
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"`
Expected: todos passam (`Passed: 28`).

- [ ] **Step 5: Commit**

```powershell
git add src/Supermonitor.Core/Metrics tests/Supermonitor.Tests/Metrics
git commit -m "feat(core): add metric model and unit formatter"
```

---

### Task 4: MetricStore

**Files:**
- Create: `src/Supermonitor.Core/Metrics/MetricStore.cs`
- Test: `tests/Supermonitor.Tests/Metrics/MetricStoreTests.cs`

**Interfaces:**
- Consumes: `RingBuffer<float>` (Task 2), `MetricDescriptor`, `MetricHandle` (Task 3).
- Produces: `MetricStore(int historyCapacity)`, `int HistoryCapacity`, `event Action? MetricsChanged` (dispara quando um id novo é registrado), `MetricHandle Register(MetricDescriptor descriptor, string owner)` (idempotente por id), `void Write(MetricHandle handle, float value)` (valor pendente), `void CommitTick()` (empurra o valor pendente de cada série; valor não reescrito é repetido), `void InvalidateOwner(string owner)` (pendente = NaN para as séries do dono), `IReadOnlyList<MetricDescriptor> GetDescriptors()`, `bool TryGetDescriptor(string id, out MetricDescriptor? descriptor)`, `float GetLatest(string id)` (NaN se desconhecido ou vazio), `int CopyHistory(string id, Span<float> destination)`. Thread-safe.

- [ ] **Step 1: Escrever os testes que falham**

`tests/Supermonitor.Tests/Metrics/MetricStoreTests.cs`:
```csharp
using Supermonitor.Core.Metrics;

namespace Supermonitor.Tests.Metrics;

public class MetricStoreTests
{
    private static MetricDescriptor Descriptor(string id) => new(id, id, MetricCategory.System, MetricUnit.Count);

    [Fact]
    public void Register_same_id_twice_returns_same_handle_and_raises_event_once()
    {
        var store = new MetricStore(10);
        var raised = 0;
        store.MetricsChanged += () => raised++;

        var first = store.Register(Descriptor("a"), "p");
        var second = store.Register(Descriptor("a"), "p");

        Assert.Equal(first, second);
        Assert.Equal(1, raised);
        Assert.Single(store.GetDescriptors());
    }

    [Fact]
    public void Written_value_is_visible_only_after_commit()
    {
        var store = new MetricStore(10);
        var handle = store.Register(Descriptor("a"), "p");

        store.Write(handle, 5);
        Assert.True(float.IsNaN(store.GetLatest("a")));

        store.CommitTick();
        Assert.Equal(5, store.GetLatest("a"));
    }

    [Fact]
    public void Commit_without_new_write_repeats_last_value()
    {
        var store = new MetricStore(10);
        var handle = store.Register(Descriptor("a"), "p");
        store.Write(handle, 3);
        store.CommitTick();
        store.CommitTick();

        var history = new float[5];
        Assert.Equal(2, store.CopyHistory("a", history));
        Assert.Equal(3, history[0]);
        Assert.Equal(3, history[1]);
    }

    [Fact]
    public void InvalidateOwner_sets_only_that_owners_series_to_nan()
    {
        var store = new MetricStore(10);
        var a = store.Register(Descriptor("a"), "p1");
        var b = store.Register(Descriptor("b"), "p2");
        store.Write(a, 1);
        store.Write(b, 2);

        store.InvalidateOwner("p1");
        store.CommitTick();

        Assert.True(float.IsNaN(store.GetLatest("a")));
        Assert.Equal(2, store.GetLatest("b"));
    }

    [Fact]
    public void Unknown_id_returns_nan_and_empty_history()
    {
        var store = new MetricStore(10);

        Assert.True(float.IsNaN(store.GetLatest("missing")));
        Assert.Equal(0, store.CopyHistory("missing", new float[3]));
        Assert.False(store.TryGetDescriptor("missing", out _));
    }

    [Fact]
    public void TryGetDescriptor_returns_registered_descriptor()
    {
        var store = new MetricStore(10);
        store.Register(new MetricDescriptor("cpu", "CPU usage", MetricCategory.Cpu, MetricUnit.Percent), "p");

        Assert.True(store.TryGetDescriptor("cpu", out var descriptor));
        Assert.Equal("CPU usage", descriptor!.DisplayName);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"`
Expected: erro de compilação `MetricStore` não encontrado.

- [ ] **Step 3: Implementar**

`src/Supermonitor.Core/Metrics/MetricStore.cs`:
```csharp
using System.Diagnostics.CodeAnalysis;
using Supermonitor.Core.Collections;

namespace Supermonitor.Core.Metrics;

public sealed class MetricStore
{
    private readonly Lock _gate = new();
    private readonly List<Series> _series = [];
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

    public MetricStore(int historyCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(historyCapacity, 1);
        HistoryCapacity = historyCapacity;
    }

    public event Action? MetricsChanged;

    public int HistoryCapacity { get; }

    public MetricHandle Register(MetricDescriptor descriptor, string owner)
    {
        MetricHandle handle;
        var added = false;
        lock (_gate)
        {
            if (_index.TryGetValue(descriptor.Id, out var existing))
            {
                handle = new MetricHandle(existing);
            }
            else
            {
                _series.Add(new Series(descriptor, owner, HistoryCapacity));
                handle = new MetricHandle(_series.Count - 1);
                _index[descriptor.Id] = handle.Index;
                added = true;
            }
        }

        if (added)
        {
            MetricsChanged?.Invoke();
        }

        return handle;
    }

    public void Write(MetricHandle handle, float value)
    {
        lock (_gate)
        {
            _series[handle.Index].Current = value;
        }
    }

    public void InvalidateOwner(string owner)
    {
        lock (_gate)
        {
            foreach (var series in _series)
            {
                if (series.Owner == owner)
                {
                    series.Current = float.NaN;
                }
            }
        }
    }

    public void CommitTick()
    {
        lock (_gate)
        {
            foreach (var series in _series)
            {
                series.History.Add(series.Current);
            }
        }
    }

    public IReadOnlyList<MetricDescriptor> GetDescriptors()
    {
        lock (_gate)
        {
            return _series.Select(s => s.Descriptor).ToArray();
        }
    }

    public bool TryGetDescriptor(string id, [NotNullWhen(true)] out MetricDescriptor? descriptor)
    {
        lock (_gate)
        {
            if (_index.TryGetValue(id, out var index))
            {
                descriptor = _series[index].Descriptor;
                return true;
            }
        }

        descriptor = null;
        return false;
    }

    public float GetLatest(string id)
    {
        lock (_gate)
        {
            if (!_index.TryGetValue(id, out var index) || _series[index].History.Count == 0)
            {
                return float.NaN;
            }

            return _series[index].History.Latest;
        }
    }

    public int CopyHistory(string id, Span<float> destination)
    {
        lock (_gate)
        {
            return _index.TryGetValue(id, out var index) ? _series[index].History.CopyLast(destination) : 0;
        }
    }

    private sealed class Series(MetricDescriptor descriptor, string owner, int capacity)
    {
        public MetricDescriptor Descriptor { get; } = descriptor;

        public string Owner { get; } = owner;

        public RingBuffer<float> History { get; } = new(capacity);

        public float Current { get; set; } = float.NaN;
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"`
Expected: todos passam.

- [ ] **Step 5: Commit**

```powershell
git add src/Supermonitor.Core/Metrics/MetricStore.cs tests/Supermonitor.Tests/Metrics/MetricStoreTests.cs
git commit -m "feat(core): add thread-safe metric store with history"
```

---

### Task 5: SensorHub

**Files:**
- Create: `src/Supermonitor.Core/Sensors/ISensorProvider.cs`, `src/Supermonitor.Core/Sensors/SensorHub.cs`
- Test: `tests/Supermonitor.Tests/Sensors/SensorHubTests.cs`, `tests/Supermonitor.Tests/Sensors/FakeProvider.cs`

**Interfaces:**
- Consumes: `MetricStore` (Task 4), `Log` (Task 1).
- Produces:
  ```csharp
  public interface ISensorProvider : IDisposable
  {
      string Name { get; }
      TimeSpan MinInterval { get; }            // TimeSpan.Zero = todo tick
      void Initialize(MetricStore store);      // registra métricas; exceção = indisponível
      void Sample(MetricStore store, TimeSpan elapsed); // elapsed = desde a última amostra deste provedor; Zero na primeira
  }
  public sealed record ProviderStatus(string Name, bool IsAvailable, string? UnavailableReason);
  ```
- Produces: `SensorHub(MetricStore store, IEnumerable<ISensorProvider> providers, TimeProvider time, TimeSpan interval)`, `const int MaxConsecutiveFailures = 5`, `event Action? SampleReady` (thread de fundo), `TimeSpan Interval { get; set; }` (mínimo 100 ms, efeito no próximo tick), `IReadOnlyList<ProviderStatus> GetStatuses()`, `void InitializeProviders()`, `void Start()`, `void Tick()`, `ValueTask DisposeAsync()`.

- [ ] **Step 1: Escrever o fake e os testes que falham**

`tests/Supermonitor.Tests/Sensors/FakeProvider.cs`:
```csharp
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors;

namespace Supermonitor.Tests.Sensors;

internal sealed class FakeProvider(string name) : ISensorProvider
{
    private MetricHandle _handle;

    public string Name { get; } = name;

    public TimeSpan MinInterval { get; init; } = TimeSpan.Zero;

    public bool FailInitialize { get; init; }

    public int FailuresRemaining { get; set; }

    public float Value { get; set; } = 1;

    public List<TimeSpan> Elapsed { get; } = [];

    public bool Disposed { get; private set; }

    public string MetricId => $"{Name}.value";

    public void Initialize(MetricStore store)
    {
        if (FailInitialize)
        {
            throw new InvalidOperationException("no hardware");
        }

        _handle = store.Register(new MetricDescriptor(MetricId, Name, MetricCategory.System, MetricUnit.Count), Name);
    }

    public void Sample(MetricStore store, TimeSpan elapsed)
    {
        Elapsed.Add(elapsed);
        if (FailuresRemaining > 0)
        {
            FailuresRemaining--;
            throw new InvalidOperationException("boom");
        }

        store.Write(_handle, Value);
    }

    public void Dispose() => Disposed = true;
}
```

`tests/Supermonitor.Tests/Sensors/SensorHubTests.cs`:
```csharp
using Microsoft.Extensions.Time.Testing;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors;

namespace Supermonitor.Tests.Sensors;

public class SensorHubTests
{
    private static SensorHub CreateHub(MetricStore store, FakeTimeProvider time, params ISensorProvider[] providers)
    {
        var hub = new SensorHub(store, providers, time, TimeSpan.FromSeconds(1));
        hub.InitializeProviders();
        return hub;
    }

    [Fact]
    public void Initialize_failure_marks_provider_unavailable_and_others_still_sample()
    {
        var store = new MetricStore(10);
        var broken = new FakeProvider("broken") { FailInitialize = true };
        var good = new FakeProvider("good") { Value = 7 };
        var hub = CreateHub(store, new FakeTimeProvider(), broken, good);

        hub.Tick();

        var status = hub.GetStatuses().Single(s => s.Name == "broken");
        Assert.False(status.IsAvailable);
        Assert.Equal("no hardware", status.UnavailableReason);
        Assert.Empty(broken.Elapsed);
        Assert.Equal(7, store.GetLatest(good.MetricId));
    }

    [Fact]
    public void Consecutive_failures_mark_provider_unavailable_and_invalidate_its_metrics()
    {
        var store = new MetricStore(10);
        var provider = new FakeProvider("p");
        var hub = CreateHub(store, new FakeTimeProvider(), provider);
        hub.Tick();
        Assert.Equal(1, store.GetLatest(provider.MetricId));

        provider.FailuresRemaining = SensorHub.MaxConsecutiveFailures;
        for (var i = 0; i < SensorHub.MaxConsecutiveFailures; i++)
        {
            hub.Tick();
        }

        Assert.False(hub.GetStatuses().Single().IsAvailable);
        Assert.True(float.IsNaN(store.GetLatest(provider.MetricId)));

        var samplesBefore = provider.Elapsed.Count;
        hub.Tick();
        Assert.Equal(samplesBefore, provider.Elapsed.Count);
    }

    [Fact]
    public void Failures_below_threshold_followed_by_success_keep_provider_available()
    {
        var store = new MetricStore(10);
        var provider = new FakeProvider("p") { FailuresRemaining = SensorHub.MaxConsecutiveFailures - 1 };
        var hub = CreateHub(store, new FakeTimeProvider(), provider);

        for (var i = 0; i < SensorHub.MaxConsecutiveFailures; i++)
        {
            hub.Tick();
        }

        Assert.True(hub.GetStatuses().Single().IsAvailable);
        Assert.Equal(1, store.GetLatest(provider.MetricId));
    }

    [Fact]
    public void MinInterval_skips_samples_until_due_and_reports_elapsed()
    {
        var time = new FakeTimeProvider();
        var provider = new FakeProvider("slow") { MinInterval = TimeSpan.FromSeconds(5) };
        var hub = CreateHub(new MetricStore(10), time, provider);

        hub.Tick();
        time.Advance(TimeSpan.FromSeconds(1));
        hub.Tick();
        time.Advance(TimeSpan.FromSeconds(4));
        hub.Tick();

        Assert.Equal(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(5) }, provider.Elapsed);
    }

    [Fact]
    public void Throwing_SampleReady_handler_does_not_stop_ticks()
    {
        var provider = new FakeProvider("p");
        var hub = CreateHub(new MetricStore(10), new FakeTimeProvider(), provider);
        var raised = 0;
        hub.SampleReady += () =>
        {
            raised++;
            throw new InvalidOperationException("ui bug");
        };

        hub.Tick();
        hub.Tick();

        Assert.Equal(2, raised);
        Assert.Equal(2, provider.Elapsed.Count);
    }

    [Fact]
    public async Task DisposeAsync_disposes_providers()
    {
        var provider = new FakeProvider("p");
        var hub = CreateHub(new MetricStore(10), new FakeTimeProvider(), provider);

        await hub.DisposeAsync();

        Assert.True(provider.Disposed);
    }

    [Fact]
    public void Interval_below_minimum_throws()
    {
        var hub = CreateHub(new MetricStore(10), new FakeTimeProvider());

        Assert.Throws<ArgumentOutOfRangeException>(() => hub.Interval = TimeSpan.FromMilliseconds(50));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"`
Expected: erro de compilação `ISensorProvider`/`SensorHub` não encontrados.

- [ ] **Step 3: Implementar**

`src/Supermonitor.Core/Sensors/ISensorProvider.cs`:
```csharp
using Supermonitor.Core.Metrics;

namespace Supermonitor.Core.Sensors;

public interface ISensorProvider : IDisposable
{
    string Name { get; }

    TimeSpan MinInterval { get; }

    void Initialize(MetricStore store);

    void Sample(MetricStore store, TimeSpan elapsed);
}

public sealed record ProviderStatus(string Name, bool IsAvailable, string? UnavailableReason);
```

`src/Supermonitor.Core/Sensors/SensorHub.cs`:
```csharp
using Supermonitor.Core.Diagnostics;
using Supermonitor.Core.Metrics;

namespace Supermonitor.Core.Sensors;

public sealed class SensorHub : IAsyncDisposable
{
    public const int MaxConsecutiveFailures = 5;
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(100);

    private readonly MetricStore _store;
    private readonly TimeProvider _time;
    private readonly Slot[] _slots;
    private readonly CancellationTokenSource _cts = new();
    private PeriodicTimer? _timer;
    private Task? _loop;
    private TimeSpan _interval;
    private bool _initialized;

    public SensorHub(MetricStore store, IEnumerable<ISensorProvider> providers, TimeProvider time, TimeSpan interval)
    {
        _store = store;
        _time = time;
        _slots = providers.Select(p => new Slot(p)).ToArray();
        Interval = interval;
    }

    public event Action? SampleReady;

    public TimeSpan Interval
    {
        get => _interval;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, MinimumInterval);
            _interval = value;
            if (_timer is not null)
            {
                _timer.Period = value;
            }
        }
    }

    public IReadOnlyList<ProviderStatus> GetStatuses() =>
        _slots.Select(s => new ProviderStatus(s.Provider.Name, s.Available, s.Reason)).ToArray();

    public void InitializeProviders()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        foreach (var slot in _slots)
        {
            try
            {
                slot.Provider.Initialize(_store);
            }
            catch (Exception ex)
            {
                slot.Available = false;
                slot.Reason = ex.Message;
                Log.Warn($"Provider '{slot.Provider.Name}' unavailable", ex);
            }
        }
    }

    public void Start()
    {
        InitializeProviders();
        _timer = new PeriodicTimer(_interval, _time);
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Tick()
    {
        var now = _time.GetTimestamp();
        foreach (var slot in _slots)
        {
            if (!slot.Available)
            {
                continue;
            }

            var elapsed = slot.HasSampled ? _time.GetElapsedTime(slot.LastSample, now) : TimeSpan.Zero;
            if (slot.HasSampled && elapsed < slot.Provider.MinInterval)
            {
                continue;
            }

            try
            {
                slot.Provider.Sample(_store, elapsed);
                slot.Failures = 0;
            }
            catch (Exception ex)
            {
                slot.Failures++;
                Log.Warn($"Provider '{slot.Provider.Name}' failed ({slot.Failures}/{MaxConsecutiveFailures})", ex);
                if (slot.Failures >= MaxConsecutiveFailures)
                {
                    slot.Available = false;
                    slot.Reason = ex.Message;
                    _store.InvalidateOwner(slot.Provider.Name);
                }
            }

            slot.LastSample = now;
            slot.HasSampled = true;
        }

        _store.CommitTick();

        try
        {
            SampleReady?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("SampleReady handler failed", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_loop is not null)
        {
            await _loop.ConfigureAwait(false);
        }

        _timer?.Dispose();
        foreach (var slot in _slots)
        {
            try
            {
                slot.Provider.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warn($"Provider '{slot.Provider.Name}' failed to dispose", ex);
            }
        }

        _cts.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
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

    private sealed class Slot(ISensorProvider provider)
    {
        public ISensorProvider Provider { get; } = provider;

        public bool Available { get; set; } = true;

        public string? Reason { get; set; }

        public int Failures { get; set; }

        public long LastSample { get; set; }

        public bool HasSampled { get; set; }
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"`
Expected: todos passam.

- [ ] **Step 5: Commit**

```powershell
git add src/Supermonitor.Core/Sensors tests/Supermonitor.Tests/Sensors
git commit -m "feat(core): add sensor hub with failure isolation"
```

---

### Task 6: Configurações

**Files:**
- Create: `src/Supermonitor.Core/Settings/AppSettings.cs`, `SettingsMigrator.cs`, `SettingsJsonContext.cs`, `SettingsStore.cs`
- Test: `tests/Supermonitor.Tests/Settings/SettingsStoreTests.cs`, `tests/Supermonitor.Tests/Settings/AppSettingsTests.cs`

**Interfaces:**
- Consumes: `MetricIds` (Task 3), `Log` (Task 1).
- Produces: `AppSettings` com propriedades `int Version`, `int SampleIntervalMs` (padrão 1000), `string PingHost` (padrão `"1.1.1.1"`), `bool StartMinimized`, `List<string> OverviewCards`; `static IReadOnlyList<int> AllowedIntervalsMs` = `[250, 500, 1000, 2000]`; `static IReadOnlyList<string> DefaultOverviewCards`; `void Normalize()`.
- Produces: `SettingsMigrator.CurrentVersion` (1), `static JsonObject Migrate(JsonObject root)`.
- Produces: `enum SettingsLoadResult { Loaded, CreatedDefault, RecoveredFromCorrupt }`; `SettingsStore(string filePath, TimeProvider time, TimeSpan debounce)`, `AppSettings Current`, `SettingsLoadResult Load()`, `void RequestSave()`, `void Flush()`, `void Dispose()`.

- [ ] **Step 1: Escrever os testes que falham**

`tests/Supermonitor.Tests/Settings/AppSettingsTests.cs`:
```csharp
using System.Text.Json.Nodes;
using Supermonitor.Core.Settings;

namespace Supermonitor.Tests.Settings;

public class AppSettingsTests
{
    [Fact]
    public void Normalize_replaces_invalid_values_with_defaults()
    {
        var settings = new AppSettings { SampleIntervalMs = 7, PingHost = "  ", OverviewCards = null! };

        settings.Normalize();

        Assert.Equal(1000, settings.SampleIntervalMs);
        Assert.Equal("1.1.1.1", settings.PingHost);
        Assert.Equal(AppSettings.DefaultOverviewCards, settings.OverviewCards);
    }

    [Fact]
    public void Normalize_keeps_valid_values()
    {
        var settings = new AppSettings { SampleIntervalMs = 250, PingHost = "8.8.8.8", OverviewCards = ["cpu.usage"] };

        settings.Normalize();

        Assert.Equal(250, settings.SampleIntervalMs);
        Assert.Equal("8.8.8.8", settings.PingHost);
        Assert.Equal(new[] { "cpu.usage" }, settings.OverviewCards);
    }

    [Fact]
    public void Migrator_adds_version_to_unversioned_document()
    {
        var root = new JsonObject { ["pingHost"] = "9.9.9.9" };

        var migrated = SettingsMigrator.Migrate(root);

        Assert.Equal(SettingsMigrator.CurrentVersion, migrated["version"]!.GetValue<int>());
        Assert.Equal("9.9.9.9", migrated["pingHost"]!.GetValue<string>());
    }
}
```

`tests/Supermonitor.Tests/Settings/SettingsStoreTests.cs`:
```csharp
using Microsoft.Extensions.Time.Testing;
using Supermonitor.Core.Settings;

namespace Supermonitor.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("smtest");
    private readonly FakeTimeProvider _time = new();

    private string FilePath => Path.Combine(_dir.FullName, "settings.json");

    private SettingsStore CreateStore() => new(FilePath, _time, TimeSpan.FromMilliseconds(500));

    public void Dispose() => _dir.Delete(recursive: true);

    [Fact]
    public void Load_without_file_returns_defaults()
    {
        using var store = CreateStore();

        Assert.Equal(SettingsLoadResult.CreatedDefault, store.Load());
        Assert.Equal(1000, store.Current.SampleIntervalMs);
    }

    [Fact]
    public void RequestSave_is_debounced_and_round_trips()
    {
        using (var store = CreateStore())
        {
            store.Load();
            store.Current.PingHost = "8.8.4.4";
            store.RequestSave();

            _time.Advance(TimeSpan.FromMilliseconds(400));
            Assert.False(File.Exists(FilePath));

            store.Current.SampleIntervalMs = 500;
            store.RequestSave();
            _time.Advance(TimeSpan.FromMilliseconds(400));
            Assert.False(File.Exists(FilePath));

            _time.Advance(TimeSpan.FromMilliseconds(100));
            Assert.True(File.Exists(FilePath));
        }

        using var reloaded = CreateStore();
        Assert.Equal(SettingsLoadResult.Loaded, reloaded.Load());
        Assert.Equal("8.8.4.4", reloaded.Current.PingHost);
        Assert.Equal(500, reloaded.Current.SampleIntervalMs);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Flush_writes_pending_changes_immediately()
    {
        using var store = CreateStore();
        store.Load();
        store.Current.StartMinimized = true;
        store.RequestSave();

        store.Flush();

        Assert.Contains("\"startMinimized\": true", File.ReadAllText(FilePath));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{ \"sampleIntervalMs\": \"fast\" }")]
    public void Corrupt_file_is_moved_to_bak_and_defaults_are_used(string content)
    {
        File.WriteAllText(FilePath, content);
        using var store = CreateStore();

        Assert.Equal(SettingsLoadResult.RecoveredFromCorrupt, store.Load());
        Assert.Equal(1000, store.Current.SampleIntervalMs);
        Assert.Equal(content, File.ReadAllText(FilePath + ".bak"));
    }

    [Fact]
    public void Out_of_range_values_are_normalized_on_load()
    {
        File.WriteAllText(FilePath, "{ \"version\": 1, \"sampleIntervalMs\": 7, \"pingHost\": \"\" }");
        using var store = CreateStore();

        Assert.Equal(SettingsLoadResult.Loaded, store.Load());
        Assert.Equal(1000, store.Current.SampleIntervalMs);
        Assert.Equal("1.1.1.1", store.Current.PingHost);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"`
Expected: erro de compilação `AppSettings`/`SettingsStore` não encontrados.

- [ ] **Step 3: Implementar**

`src/Supermonitor.Core/Settings/AppSettings.cs`:
```csharp
using Supermonitor.Core.Metrics;

namespace Supermonitor.Core.Settings;

public sealed class AppSettings
{
    public const string DefaultPingHost = "1.1.1.1";
    public const int DefaultSampleIntervalMs = 1000;

    public static IReadOnlyList<int> AllowedIntervalsMs { get; } = [250, 500, 1000, 2000];

    public static IReadOnlyList<string> DefaultOverviewCards { get; } =
    [
        MetricIds.CpuUsage,
        MetricIds.MemoryUsage,
        MetricIds.GpuUsage(0),
        MetricIds.GpuVramUsed(0),
        MetricIds.DiskActive(0),
        MetricIds.NetworkTotalDown,
        MetricIds.NetworkTotalUp,
        MetricIds.Ping,
    ];

    public int Version { get; set; } = SettingsMigrator.CurrentVersion;

    public int SampleIntervalMs { get; set; } = DefaultSampleIntervalMs;

    public string PingHost { get; set; } = DefaultPingHost;

    public bool StartMinimized { get; set; }

    public List<string> OverviewCards { get; set; } = [.. DefaultOverviewCards];

    public void Normalize()
    {
        if (!AllowedIntervalsMs.Contains(SampleIntervalMs))
        {
            SampleIntervalMs = DefaultSampleIntervalMs;
        }

        PingHost = string.IsNullOrWhiteSpace(PingHost) ? DefaultPingHost : PingHost.Trim();
        OverviewCards ??= [.. DefaultOverviewCards];
    }
}
```

`src/Supermonitor.Core/Settings/SettingsMigrator.cs`:
```csharp
using System.Text.Json.Nodes;

namespace Supermonitor.Core.Settings;

public static class SettingsMigrator
{
    public const int CurrentVersion = 1;

    public static JsonObject Migrate(JsonObject root)
    {
        var version = root["version"]?.GetValue<int>() ?? 0;
        if (version < 1)
        {
            // v0: arquivos sem número de versão. Nenhum campo mudou.
            root["version"] = 1;
        }

        return root;
    }
}
```

`src/Supermonitor.Core/Settings/SettingsJsonContext.cs`:
```csharp
using System.Text.Json.Serialization;

namespace Supermonitor.Core.Settings;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
```

`src/Supermonitor.Core/Settings/SettingsStore.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using Supermonitor.Core.Diagnostics;

namespace Supermonitor.Core.Settings;

public enum SettingsLoadResult
{
    Loaded,
    CreatedDefault,
    RecoveredFromCorrupt,
}

public sealed class SettingsStore : IDisposable
{
    private readonly string _path;
    private readonly TimeSpan _debounce;
    private readonly ITimer _timer;
    private readonly Lock _gate = new();
    private string? _pendingJson;

    public SettingsStore(string filePath, TimeProvider time, TimeSpan debounce)
    {
        _path = filePath;
        _debounce = debounce;
        _timer = time.CreateTimer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public AppSettings Current { get; private set; } = new();

    public SettingsLoadResult Load()
    {
        if (!File.Exists(_path))
        {
            Current = new AppSettings();
            return SettingsLoadResult.CreatedDefault;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject
                ?? throw new JsonException("Settings root is not an object.");
            var migrated = SettingsMigrator.Migrate(root);
            Current = migrated.Deserialize(SettingsJsonContext.Default.AppSettings)
                ?? throw new JsonException("Settings deserialized to null.");
            Current.Normalize();
            return SettingsLoadResult.Loaded;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or NotSupportedException)
        {
            Log.Warn("Settings file is corrupt; moving it to .bak and using defaults", ex);
            File.Move(_path, _path + ".bak", overwrite: true);
            Current = new AppSettings();
            return SettingsLoadResult.RecoveredFromCorrupt;
        }
    }

    public void RequestSave()
    {
        var json = JsonSerializer.Serialize(Current, SettingsJsonContext.Default.AppSettings);
        lock (_gate)
        {
            _pendingJson = json;
        }

        _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
    }

    public void Flush()
    {
        string? json;
        lock (_gate)
        {
            json = _pendingJson;
            _pendingJson = null;
        }

        if (json is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);
        }
        catch (IOException ex)
        {
            Log.Error("Failed to save settings", ex);
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        Flush();
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"`
Expected: todos passam.

- [ ] **Step 5: Commit**

```powershell
git add src/Supermonitor.Core/Settings tests/Supermonitor.Tests/Settings
git commit -m "feat(core): add settings store with migration, debounce and corruption recovery"
```

---

### Task 7: PDH e provedor de CPU

**Files:**
- Create: `src/Supermonitor.Core/Interop/Pdh.cs`, `src/Supermonitor.Core/Sensors/Pdh/PdhQuery.cs`
- Create: `src/Supermonitor.Core/Sensors/Cpu/CpuInstanceName.cs`, `src/Supermonitor.Core/Sensors/Cpu/CpuProvider.cs`
- Test: `tests/Supermonitor.Tests/Sensors/CpuInstanceNameTests.cs`, `tests/Supermonitor.Tests/Hardware/ProviderSmokeTests.cs`

**Interfaces:**
- Consumes: `ISensorProvider`, `MetricStore`, `MetricIds`.
- Produces: `PdhQuery` (`AddCounter(string englishPath) -> PdhCounter`, `Collect()`, `Dispose()`), `PdhCounter` (`double GetValue()` → NaN se inválido, `PdhArray GetArray()`), `ref struct PdhArray` (`int Count`, `ReadOnlySpan<char> NameAt(int)`, `double ValueAt(int)` → NaN se inválido), `PdhException(string operation, uint status)`.
- Produces: `CpuInstanceName.TryParse(ReadOnlySpan<char> name, out int key)` (`key = group * 1024 + number`), `CpuProvider` (métricas `cpu.usage`, `cpu.core{i}.usage`).

- [ ] **Step 1: Escrever os testes que falham**

`tests/Supermonitor.Tests/Sensors/CpuInstanceNameTests.cs`:
```csharp
using Supermonitor.Core.Sensors.Cpu;

namespace Supermonitor.Tests.Sensors;

public class CpuInstanceNameTests
{
    [Theory]
    [InlineData("0,0", 0)]
    [InlineData("0,15", 15)]
    [InlineData("1,3", 1027)]
    public void Parses_group_and_core_number(string name, int expected)
    {
        Assert.True(CpuInstanceName.TryParse(name, out var key));
        Assert.Equal(expected, key);
    }

    [Theory]
    [InlineData("_Total")]
    [InlineData("0,_Total")]
    [InlineData("")]
    [InlineData("abc")]
    public void Rejects_totals_and_malformed_names(string name)
    {
        Assert.False(CpuInstanceName.TryParse(name, out _));
    }
}
```

`tests/Supermonitor.Tests/Hardware/ProviderSmokeTests.cs`:
```csharp
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors;
using Supermonitor.Core.Sensors.Cpu;

namespace Supermonitor.Tests.Hardware;

[Trait("Category", "Hardware")]
public class ProviderSmokeTests
{
    internal static MetricStore SampleTwice(ISensorProvider provider)
    {
        var store = new MetricStore(10);
        provider.Initialize(store);
        provider.Sample(store, TimeSpan.Zero);
        store.CommitTick();
        Thread.Sleep(1000);
        provider.Sample(store, TimeSpan.FromSeconds(1));
        store.CommitTick();
        return store;
    }

    [Fact]
    public void Cpu_provider_reports_total_and_per_core_usage()
    {
        using var provider = new CpuProvider();
        var store = SampleTwice(provider);

        Assert.InRange(store.GetLatest(MetricIds.CpuUsage), 0, 100);
        for (var core = 0; core < Environment.ProcessorCount; core++)
        {
            Assert.InRange(store.GetLatest(MetricIds.CpuCoreUsage(core)), 0, 100);
        }
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"`
Expected: erro de compilação `CpuInstanceName`/`CpuProvider` não encontrados.

- [ ] **Step 3: Implementar interop e wrapper PDH**

`src/Supermonitor.Core/Interop/Pdh.cs`:
```csharp
using System.Runtime.InteropServices;

namespace Supermonitor.Core.Interop;

internal static unsafe partial class Pdh
{
    public const uint PDH_FMT_DOUBLE = 0x00000200;
    public const uint PDH_MORE_DATA = 0x800007D2;
    public const uint PDH_CSTATUS_NEW_DATA = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    public struct PDH_FMT_COUNTERVALUE
    {
        public uint CStatus;
        public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PDH_FMT_COUNTERVALUE_ITEM_W
    {
        public char* szName;
        public PDH_FMT_COUNTERVALUE FmtValue;
    }

    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PdhOpenQuery(string? dataSource, nint userData, out nint query);

    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PdhAddEnglishCounter(nint query, string fullCounterPath, nint userData, out nint counter);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhCollectQueryData(nint query);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);

    [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    public static partial uint PdhGetFormattedCounterArray(nint counter, uint format, ref uint bufferSize, out uint itemCount, void* itemBuffer);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhCloseQuery(nint query);
}
```

`src/Supermonitor.Core/Sensors/Pdh/PdhQuery.cs`:
```csharp
using System.Runtime.InteropServices;

namespace Supermonitor.Core.Sensors.Pdh;

public sealed class PdhException(string operation, uint status)
    : Exception($"{operation} failed with 0x{status:X8}")
{
    public uint Status { get; } = status;
}

public sealed class PdhQuery : IDisposable
{
    private readonly List<PdhCounter> _counters = [];
    private nint _handle;

    public PdhQuery()
    {
        Check(Interop.Pdh.PdhOpenQuery(null, 0, out _handle), "PdhOpenQuery");
    }

    public PdhCounter AddCounter(string englishPath)
    {
        Check(Interop.Pdh.PdhAddEnglishCounter(_handle, englishPath, 0, out var counter), $"PdhAddEnglishCounter({englishPath})");
        var wrapper = new PdhCounter(counter);
        _counters.Add(wrapper);
        return wrapper;
    }

    public void Collect() => Check(Interop.Pdh.PdhCollectQueryData(_handle), "PdhCollectQueryData");

    public void Dispose()
    {
        foreach (var counter in _counters)
        {
            counter.FreeBuffer();
        }

        if (_handle != 0)
        {
            Interop.Pdh.PdhCloseQuery(_handle);
            _handle = 0;
        }
    }

    internal static void Check(uint status, string operation)
    {
        if (status != 0)
        {
            throw new PdhException(operation, status);
        }
    }
}

public sealed unsafe class PdhCounter
{
    private readonly nint _handle;
    private byte* _buffer;
    private uint _bufferSize;

    internal PdhCounter(nint handle) => _handle = handle;

    public double GetValue()
    {
        var status = Interop.Pdh.PdhGetFormattedCounterValue(_handle, Interop.Pdh.PDH_FMT_DOUBLE, out _, out var value);
        return status == 0 && value.CStatus <= Interop.Pdh.PDH_CSTATUS_NEW_DATA ? value.DoubleValue : double.NaN;
    }

    public PdhArray GetArray()
    {
        while (true)
        {
            var size = _bufferSize;
            var status = Interop.Pdh.PdhGetFormattedCounterArray(_handle, Interop.Pdh.PDH_FMT_DOUBLE, ref size, out var count, _buffer);
            if (status == Interop.Pdh.PDH_MORE_DATA)
            {
                Grow(size);
                continue;
            }

            return status == 0
                ? new PdhArray((Interop.Pdh.PDH_FMT_COUNTERVALUE_ITEM_W*)_buffer, (int)count)
                : default;
        }
    }

    internal void FreeBuffer()
    {
        NativeMemory.Free(_buffer);
        _buffer = null;
        _bufferSize = 0;
    }

    private void Grow(uint required)
    {
        NativeMemory.Free(_buffer);
        _bufferSize = Math.Max(required, _bufferSize * 2);
        _buffer = (byte*)NativeMemory.Alloc(_bufferSize);
    }
}

public readonly unsafe ref struct PdhArray
{
    private readonly Interop.Pdh.PDH_FMT_COUNTERVALUE_ITEM_W* _items;

    internal PdhArray(Interop.Pdh.PDH_FMT_COUNTERVALUE_ITEM_W* items, int count)
    {
        _items = items;
        Count = count;
    }

    public int Count { get; }

    public ReadOnlySpan<char> NameAt(int index) => MemoryMarshal.CreateReadOnlySpanFromNullTerminated(_items[index].szName);

    public double ValueAt(int index) =>
        _items[index].FmtValue.CStatus <= Interop.Pdh.PDH_CSTATUS_NEW_DATA ? _items[index].FmtValue.DoubleValue : double.NaN;
}
```

- [ ] **Step 4: Implementar CPU**

`src/Supermonitor.Core/Sensors/Cpu/CpuInstanceName.cs`:
```csharp
using System.Globalization;

namespace Supermonitor.Core.Sensors.Cpu;

public static class CpuInstanceName
{
    // Instâncias de "Processor Information": "grupo,núcleo" (ex.: "0,7"), "0,_Total", "_Total".
    public static bool TryParse(ReadOnlySpan<char> name, out int key)
    {
        key = 0;
        var comma = name.IndexOf(',');
        if (comma <= 0)
        {
            return false;
        }

        if (!int.TryParse(name[..comma], NumberStyles.None, CultureInfo.InvariantCulture, out var group)
            || !int.TryParse(name[(comma + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        key = group * 1024 + number;
        return true;
    }
}
```

`src/Supermonitor.Core/Sensors/Cpu/CpuProvider.cs`:
```csharp
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors.Pdh;

namespace Supermonitor.Core.Sensors.Cpu;

public sealed class CpuProvider : ISensorProvider
{
    private readonly Dictionary<int, MetricHandle> _cores = [];
    private PdhQuery? _query;
    private PdhCounter? _total;
    private PdhCounter? _perCore;
    private MetricHandle _totalHandle;

    public string Name => "CPU";

    public TimeSpan MinInterval => TimeSpan.Zero;

    public void Initialize(MetricStore store)
    {
        _query = new PdhQuery();
        _total = _query.AddCounter(@"\Processor Information(_Total)\% Processor Utility");
        _perCore = _query.AddCounter(@"\Processor Information(*)\% Processor Utility");
        _query.Collect();
        _query.Collect();

        _totalHandle = store.Register(new MetricDescriptor(MetricIds.CpuUsage, "CPU usage", MetricCategory.Cpu, MetricUnit.Percent), Name);

        var keys = new List<int>();
        var array = _perCore.GetArray();
        for (var i = 0; i < array.Count; i++)
        {
            if (CpuInstanceName.TryParse(array.NameAt(i), out var key))
            {
                keys.Add(key);
            }
        }

        keys.Sort();
        for (var index = 0; index < keys.Count; index++)
        {
            _cores[keys[index]] = store.Register(
                new MetricDescriptor(MetricIds.CpuCoreUsage(index), $"Core {index} usage", MetricCategory.Cpu, MetricUnit.Percent),
                Name);
        }
    }

    public void Sample(MetricStore store, TimeSpan elapsed)
    {
        _query!.Collect();
        store.Write(_totalHandle, (float)_total!.GetValue());

        var array = _perCore!.GetArray();
        for (var i = 0; i < array.Count; i++)
        {
            if (CpuInstanceName.TryParse(array.NameAt(i), out var key) && _cores.TryGetValue(key, out var handle))
            {
                store.Write(handle, (float)array.ValueAt(i));
            }
        }
    }

    public void Dispose() => _query?.Dispose();
}
```

- [ ] **Step 5: Rodar unitários e hardware**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"`
Expected: todos passam.

Run: `dotnet test tests/Supermonitor.Tests --filter "Category=Hardware"`
Expected: `Cpu_provider_reports_total_and_per_core_usage` passa (16 núcleos lógicos no 7800X3D).

- [ ] **Step 6: Commit**

```powershell
git add src/Supermonitor.Core/Interop/Pdh.cs src/Supermonitor.Core/Sensors/Pdh src/Supermonitor.Core/Sensors/Cpu tests/Supermonitor.Tests
git commit -m "feat(sensors): add PDH wrapper and CPU provider"
```

---

### Task 8: Provedor de memória e privilégios

**Files:**
- Create: `src/Supermonitor.Core/Interop/Kernel32.cs`, `Psapi.cs`, `Ntdll.cs`
- Create: `src/Supermonitor.Core/Shell/Privileges.cs`
- Create: `src/Supermonitor.Core/Sensors/Memory/MemoryProvider.cs`
- Test: `tests/Supermonitor.Tests/Interop/StructLayoutTests.cs`; modificar `tests/Supermonitor.Tests/Hardware/ProviderSmokeTests.cs`

**Interfaces:**
- Produces: `Privileges.ProfileSingleProcess` (`"SeProfileSingleProcessPrivilege"`), `static bool Privileges.TryEnable(string name)`.
- Produces: `Kernel32` (internal) com `GlobalMemoryStatusEx`, `GetCurrentProcess`, `CloseHandle`, `GetModuleHandleW`, `SetProcessInformation`, `SetPriorityClass` — usados também nas Tasks 13 e 14.
- Produces: `MemoryProvider` com métricas `mem.total`, `mem.used`, `mem.available`, `mem.usage`, `mem.standby`, `mem.free`, `mem.modified`, `mem.commit`, `mem.commitlimit`, `mem.cache`. Standby/free/modified exigem admin + privilégio; sem eles ficam NaN.

- [ ] **Step 1: Escrever os testes que falham**

`tests/Supermonitor.Tests/Interop/StructLayoutTests.cs`:
```csharp
using System.Runtime.CompilerServices;
using Supermonitor.Core.Interop;

namespace Supermonitor.Tests.Interop;

public class StructLayoutTests
{
    [Fact]
    public void Native_memory_structs_match_x64_sizes()
    {
        Assert.Equal(64, Unsafe.SizeOf<Kernel32.MEMORYSTATUSEX>());
        Assert.Equal(104, Unsafe.SizeOf<Psapi.PERFORMANCE_INFORMATION>());
        Assert.Equal(176, Unsafe.SizeOf<Ntdll.SYSTEM_MEMORY_LIST_INFORMATION>());
    }
}
```

Adicionar em `ProviderSmokeTests` (e `using Supermonitor.Core.Sensors.Memory;` no topo):
```csharp
    [Fact]
    public void Memory_provider_reports_total_and_used()
    {
        using var provider = new MemoryProvider();
        var store = SampleTwice(provider);

        var total = store.GetLatest(MetricIds.MemoryTotal);
        Assert.True(total > 1024f * 1024 * 1024);
        Assert.InRange(store.GetLatest(MetricIds.MemoryUsed), 1, total);
        Assert.InRange(store.GetLatest(MetricIds.MemoryUsage), 0, 100);
        Assert.True(store.GetLatest(MetricIds.MemoryCommitted) > 0);
    }
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"`
Expected: erro de compilação `Kernel32` não encontrado.

- [ ] **Step 3: Implementar interop**

`src/Supermonitor.Core/Interop/Kernel32.cs`:
```csharp
using System.Runtime.InteropServices;

namespace Supermonitor.Core.Interop;

internal static unsafe partial class Kernel32
{
    public const int ProcessPowerThrottling = 4;
    public const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
    public const uint NORMAL_PRIORITY_CLASS = 0x20;
    public const uint BELOW_NORMAL_PRIORITY_CLASS = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandleW(string? moduleName);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessInformation(nint process, int infoClass, void* info, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetPriorityClass(nint process, uint priorityClass);
}
```

`src/Supermonitor.Core/Interop/Psapi.cs`:
```csharp
using System.Runtime.InteropServices;

namespace Supermonitor.Core.Interop;

internal static partial class Psapi
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PERFORMANCE_INFORMATION
    {
        public uint cb;
        public nuint CommitTotal;
        public nuint CommitLimit;
        public nuint CommitPeak;
        public nuint PhysicalTotal;
        public nuint PhysicalAvailable;
        public nuint SystemCache;
        public nuint KernelTotal;
        public nuint KernelPaged;
        public nuint KernelNonpaged;
        public nuint PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetPerformanceInfo(ref PERFORMANCE_INFORMATION information, uint cb);
}
```

`src/Supermonitor.Core/Interop/Ntdll.cs`:
```csharp
using System.Runtime.InteropServices;

namespace Supermonitor.Core.Interop;

internal static unsafe partial class Ntdll
{
    public const int SystemMemoryListInformation = 80;

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_MEMORY_LIST_INFORMATION
    {
        public ulong ZeroPageCount;
        public ulong FreePageCount;
        public ulong ModifiedPageCount;
        public ulong ModifiedNoWritePageCount;
        public ulong BadPageCount;
        public fixed ulong PageCountByPriority[8];
        public fixed ulong RepurposedPagesByPriority[8];
        public ulong ModifiedPageCountPageFile;
    }

    [LibraryImport("ntdll.dll")]
    public static partial int NtQuerySystemInformation(int infoClass, void* info, uint length, out uint returnLength);
}
```

`src/Supermonitor.Core/Shell/Privileges.cs`:
```csharp
using System.Runtime.InteropServices;
using Supermonitor.Core.Interop;

namespace Supermonitor.Core.Shell;

public static unsafe partial class Privileges
{
    public const string ProfileSingleProcess = "SeProfileSingleProcessPrivilege";

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    public static bool TryEnable(string name)
    {
        if (!OpenProcessToken(Kernel32.GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
        {
            return false;
        }

        try
        {
            if (!LookupPrivilegeValueW(null, name, out var luid))
            {
                return false;
            }

            var privileges = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
            if (!AdjustTokenPrivileges(token, false, &privileges, 0, 0, 0))
            {
                return false;
            }

            // ERROR_NOT_ALL_ASSIGNED (1300): o token não tem o privilégio (app sem elevação).
            return Marshal.GetLastPInvokeError() == 0;
        }
        finally
        {
            Kernel32.CloseHandle(token);
        }
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValueW(string? systemName, string name, out LUID luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(
        nint token,
        [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        TOKEN_PRIVILEGES* newState,
        uint bufferLength,
        nint previousState,
        nint returnLength);
}
```

- [ ] **Step 4: Implementar o provedor**

`src/Supermonitor.Core/Sensors/Memory/MemoryProvider.cs`:
```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;
using Supermonitor.Core.Interop;
using Supermonitor.Core.Metrics;

namespace Supermonitor.Core.Sensors.Memory;

public sealed unsafe class MemoryProvider : ISensorProvider
{
    private MetricHandle _total;
    private MetricHandle _used;
    private MetricHandle _available;
    private MetricHandle _usage;
    private MetricHandle _standby;
    private MetricHandle _free;
    private MetricHandle _modified;
    private MetricHandle _commit;
    private MetricHandle _commitLimit;
    private MetricHandle _cache;

    public string Name => "Memory";

    public TimeSpan MinInterval => TimeSpan.Zero;

    public void Initialize(MetricStore store)
    {
        _total = Register(store, MetricIds.MemoryTotal, "Memory total", MetricUnit.Bytes);
        _used = Register(store, MetricIds.MemoryUsed, "Memory used", MetricUnit.Bytes);
        _available = Register(store, MetricIds.MemoryAvailable, "Memory available", MetricUnit.Bytes);
        _usage = Register(store, MetricIds.MemoryUsage, "Memory usage", MetricUnit.Percent);
        _standby = Register(store, MetricIds.MemoryStandby, "Standby", MetricUnit.Bytes);
        _free = Register(store, MetricIds.MemoryFree, "Free", MetricUnit.Bytes);
        _modified = Register(store, MetricIds.MemoryModified, "Modified", MetricUnit.Bytes);
        _commit = Register(store, MetricIds.MemoryCommitted, "Committed", MetricUnit.Bytes);
        _commitLimit = Register(store, MetricIds.MemoryCommitLimit, "Commit limit", MetricUnit.Bytes);
        _cache = Register(store, MetricIds.MemoryCache, "Cached", MetricUnit.Bytes);
    }

    public void Sample(MetricStore store, TimeSpan elapsed)
    {
        var status = new Kernel32.MEMORYSTATUSEX { dwLength = (uint)sizeof(Kernel32.MEMORYSTATUSEX) };
        if (!Kernel32.GlobalMemoryStatusEx(ref status))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var used = status.ullTotalPhys - status.ullAvailPhys;
        store.Write(_total, status.ullTotalPhys);
        store.Write(_used, used);
        store.Write(_available, status.ullAvailPhys);
        store.Write(_usage, (float)(used * 100.0 / status.ullTotalPhys));

        var performance = new Psapi.PERFORMANCE_INFORMATION { cb = (uint)sizeof(Psapi.PERFORMANCE_INFORMATION) };
        if (Psapi.GetPerformanceInfo(ref performance, performance.cb))
        {
            ulong page = performance.PageSize;
            store.Write(_commit, performance.CommitTotal * page);
            store.Write(_commitLimit, performance.CommitLimit * page);
            store.Write(_cache, performance.SystemCache * page);
        }
        else
        {
            store.Write(_commit, float.NaN);
            store.Write(_commitLimit, float.NaN);
            store.Write(_cache, float.NaN);
        }

        Ntdll.SYSTEM_MEMORY_LIST_INFORMATION lists;
        if (Ntdll.NtQuerySystemInformation(Ntdll.SystemMemoryListInformation, &lists, (uint)sizeof(Ntdll.SYSTEM_MEMORY_LIST_INFORMATION), out _) >= 0)
        {
            var pageSize = (ulong)Environment.SystemPageSize;
            ulong standbyPages = 0;
            for (var i = 0; i < 8; i++)
            {
                standbyPages += lists.PageCountByPriority[i];
            }

            store.Write(_standby, standbyPages * pageSize);
            store.Write(_free, (lists.ZeroPageCount + lists.FreePageCount) * pageSize);
            store.Write(_modified, lists.ModifiedPageCount * pageSize);
        }
        else
        {
            store.Write(_standby, float.NaN);
            store.Write(_free, float.NaN);
            store.Write(_modified, float.NaN);
        }
    }

    public void Dispose()
    {
    }

    private MetricHandle Register(MetricStore store, string id, string name, MetricUnit unit) =>
        store.Register(new MetricDescriptor(id, name, MetricCategory.Memory, unit), Name);
}
```

- [ ] **Step 5: Rodar unitários e hardware**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"` → todos passam.
Run: `dotnet test tests/Supermonitor.Tests --filter "Category=Hardware"` → os dois testes de hardware passam.

- [ ] **Step 6: Commit**

```powershell
git add src/Supermonitor.Core tests/Supermonitor.Tests
git commit -m "feat(sensors): add memory provider with standby/free/modified lists"
```

---

### Task 9: Provedores de disco e espaço

**Files:**
- Create: `src/Supermonitor.Core/Sensors/Disk/DiskInstanceName.cs`, `DiskProvider.cs`, `DriveSpaceProvider.cs`
- Test: `tests/Supermonitor.Tests/Sensors/DiskInstanceNameTests.cs`; modificar `tests/Supermonitor.Tests/Hardware/ProviderSmokeTests.cs`

**Interfaces:**
- Consumes: `PdhQuery`, `PdhCounter`, `PdhArray` (Task 7).
- Produces: `DiskInstanceName.TryParse(ReadOnlySpan<char> name, out int index, out ReadOnlySpan<char> label)`; `DiskProvider` (`disk{n}.read`, `disk{n}.write`, `disk{n}.active`, `disk{n}.queue`); `DriveSpaceProvider` (`MinInterval` 5 s; `drive.{L}.free`, `drive.{L}.total`, `drive.{L}.usage`, registra drives novos quando aparecem).

- [ ] **Step 1: Escrever os testes que falham**

`tests/Supermonitor.Tests/Sensors/DiskInstanceNameTests.cs`:
```csharp
using Supermonitor.Core.Sensors.Disk;

namespace Supermonitor.Tests.Sensors;

public class DiskInstanceNameTests
{
    [Theory]
    [InlineData("0 C:", 0, "C:")]
    [InlineData("1 D: E:", 1, "D: E:")]
    [InlineData("2", 2, "")]
    public void Parses_index_and_label(string name, int expectedIndex, string expectedLabel)
    {
        Assert.True(DiskInstanceName.TryParse(name, out var index, out var label));
        Assert.Equal(expectedIndex, index);
        Assert.Equal(expectedLabel, label.ToString());
    }

    [Theory]
    [InlineData("_Total")]
    [InlineData("")]
    [InlineData("C:")]
    public void Rejects_total_and_malformed_names(string name)
    {
        Assert.False(DiskInstanceName.TryParse(name, out _, out _));
    }
}
```

Adicionar em `ProviderSmokeTests` (e `using Supermonitor.Core.Sensors.Disk;`):
```csharp
    [Fact]
    public void Disk_provider_reports_activity_for_disk_zero()
    {
        using var provider = new DiskProvider();
        var store = SampleTwice(provider);

        Assert.InRange(store.GetLatest(MetricIds.DiskActive(0)), 0, 100);
        Assert.True(store.GetLatest(MetricIds.DiskRead(0)) >= 0);
    }

    [Fact]
    public void Drive_space_provider_reports_system_drive()
    {
        using var provider = new DriveSpaceProvider();
        var store = SampleTwice(provider);
        var letter = Environment.GetFolderPath(Environment.SpecialFolder.Windows)[0];

        Assert.True(store.GetLatest(MetricIds.DriveTotal(letter)) > 0);
        Assert.InRange(store.GetLatest(MetricIds.DriveUsage(letter)), 0, 100);
    }
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"`
Expected: erro de compilação `DiskInstanceName` não encontrado.

- [ ] **Step 3: Implementar**

`src/Supermonitor.Core/Sensors/Disk/DiskInstanceName.cs`:
```csharp
using System.Globalization;

namespace Supermonitor.Core.Sensors.Disk;

public static class DiskInstanceName
{
    // Instâncias de "PhysicalDisk": "0 C:", "1 D: E:", "_Total".
    public static bool TryParse(ReadOnlySpan<char> name, out int index, out ReadOnlySpan<char> label)
    {
        label = default;
        var space = name.IndexOf(' ');
        var number = space < 0 ? name : name[..space];
        if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out index))
        {
            return false;
        }

        label = space < 0 ? ReadOnlySpan<char>.Empty : name[(space + 1)..].Trim();
        return true;
    }
}
```

`src/Supermonitor.Core/Sensors/Disk/DiskProvider.cs`:
```csharp
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors.Pdh;

namespace Supermonitor.Core.Sensors.Disk;

public sealed class DiskProvider : ISensorProvider
{
    private enum Field
    {
        Read,
        Write,
        Active,
        Queue,
    }

    private readonly Dictionary<string, DiskHandles> _disks = new(StringComparer.Ordinal);
    private PdhQuery? _query;
    private PdhCounter? _read;
    private PdhCounter? _write;
    private PdhCounter? _idle;
    private PdhCounter? _queue;

    public string Name => "Disks";

    public TimeSpan MinInterval => TimeSpan.Zero;

    public void Initialize(MetricStore store)
    {
        _query = new PdhQuery();
        _read = _query.AddCounter(@"\PhysicalDisk(*)\Disk Read Bytes/sec");
        _write = _query.AddCounter(@"\PhysicalDisk(*)\Disk Write Bytes/sec");
        _idle = _query.AddCounter(@"\PhysicalDisk(*)\% Idle Time");
        _queue = _query.AddCounter(@"\PhysicalDisk(*)\Current Disk Queue Length");
        _query.Collect();
        _query.Collect();

        var array = _queue.GetArray();
        for (var i = 0; i < array.Count; i++)
        {
            var name = array.NameAt(i);
            if (!DiskInstanceName.TryParse(name, out var index, out var label))
            {
                continue;
            }

            var title = label.IsEmpty ? $"Disk {index}" : $"Disk {index} ({label.ToString()})";
            _disks[name.ToString()] = new DiskHandles(
                Register(store, MetricIds.DiskRead(index), $"{title} read", MetricUnit.BytesPerSecond),
                Register(store, MetricIds.DiskWrite(index), $"{title} write", MetricUnit.BytesPerSecond),
                Register(store, MetricIds.DiskActive(index), $"{title} active time", MetricUnit.Percent),
                Register(store, MetricIds.DiskQueue(index), $"{title} queue", MetricUnit.Count));
        }
    }

    public void Sample(MetricStore store, TimeSpan elapsed)
    {
        _query!.Collect();
        WriteArray(store, _read!.GetArray(), Field.Read);
        WriteArray(store, _write!.GetArray(), Field.Write);
        WriteArray(store, _idle!.GetArray(), Field.Active);
        WriteArray(store, _queue!.GetArray(), Field.Queue);
    }

    public void Dispose() => _query?.Dispose();

    private void WriteArray(MetricStore store, PdhArray array, Field field)
    {
        var lookup = _disks.GetAlternateLookup<ReadOnlySpan<char>>();
        for (var i = 0; i < array.Count; i++)
        {
            if (!lookup.TryGetValue(array.NameAt(i), out var handles))
            {
                continue;
            }

            var value = array.ValueAt(i);
            switch (field)
            {
                case Field.Read:
                    store.Write(handles.Read, (float)value);
                    break;
                case Field.Write:
                    store.Write(handles.Write, (float)value);
                    break;
                case Field.Active:
                    store.Write(handles.Active, (float)Math.Clamp(100 - value, 0, 100));
                    break;
                case Field.Queue:
                    store.Write(handles.Queue, (float)value);
                    break;
            }
        }
    }

    private MetricHandle Register(MetricStore store, string id, string name, MetricUnit unit) =>
        store.Register(new MetricDescriptor(id, name, MetricCategory.Disk, unit), Name);

    private sealed record DiskHandles(MetricHandle Read, MetricHandle Write, MetricHandle Active, MetricHandle Queue);
}
```

`src/Supermonitor.Core/Sensors/Disk/DriveSpaceProvider.cs`:
```csharp
using Supermonitor.Core.Metrics;

namespace Supermonitor.Core.Sensors.Disk;

public sealed class DriveSpaceProvider : ISensorProvider
{
    private readonly Dictionary<char, (MetricHandle Free, MetricHandle Total, MetricHandle Usage)> _drives = [];

    public string Name => "Drive space";

    public TimeSpan MinInterval => TimeSpan.FromSeconds(5);

    public void Initialize(MetricStore store) => Sample(store, TimeSpan.Zero);

    public void Sample(MetricStore store, TimeSpan elapsed)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
            {
                continue;
            }

            var letter = char.ToUpperInvariant(drive.Name[0]);
            if (!_drives.TryGetValue(letter, out var handles))
            {
                handles = (
                    Register(store, MetricIds.DriveFree(letter), $"{letter}: free", MetricUnit.Bytes),
                    Register(store, MetricIds.DriveTotal(letter), $"{letter}: size", MetricUnit.Bytes),
                    Register(store, MetricIds.DriveUsage(letter), $"{letter}: used", MetricUnit.Percent));
                _drives[letter] = handles;
            }

            store.Write(handles.Free, drive.TotalFreeSpace);
            store.Write(handles.Total, drive.TotalSize);
            store.Write(handles.Usage, (float)((drive.TotalSize - drive.TotalFreeSpace) * 100.0 / drive.TotalSize));
        }
    }

    public void Dispose()
    {
    }

    private MetricHandle Register(MetricStore store, string id, string name, MetricUnit unit) =>
        store.Register(new MetricDescriptor(id, name, MetricCategory.Disk, unit), Name);
}
```

- [ ] **Step 4: Rodar unitários e hardware**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"` → todos passam.
Run: `dotnet test tests/Supermonitor.Tests --filter "Category=Hardware"` → todos passam.

- [ ] **Step 5: Commit**

```powershell
git add src/Supermonitor.Core/Sensors/Disk tests/Supermonitor.Tests
git commit -m "feat(sensors): add disk activity and drive space providers"
```

---

### Task 10: Provedor de rede

**Files:**
- Create: `src/Supermonitor.Core/Interop/Iphlpapi.cs`
- Create: `src/Supermonitor.Core/Sensors/Network/RateCalculator.cs`, `NetworkAdapterFilter.cs`, `NetworkProvider.cs`
- Test: `tests/Supermonitor.Tests/Sensors/RateCalculatorTests.cs`, `tests/Supermonitor.Tests/Sensors/NetworkAdapterFilterTests.cs`; modificar `StructLayoutTests.cs` e `ProviderSmokeTests.cs`

**Interfaces:**
- Produces: `RateCalculator.PerSecond(ulong previous, ulong current, TimeSpan elapsed) -> double` (NaN se `elapsed <= 0`; 0 se contador voltou).
- Produces: `NetworkAdapterFilter.IsMonitored(uint ifType, int operStatus, byte flags) -> bool`.
- Produces: `NetworkProvider` (`net{n}.down`, `net{n}.up`, `net.down`, `net.up`).

- [ ] **Step 1: Escrever os testes que falham**

`tests/Supermonitor.Tests/Sensors/RateCalculatorTests.cs`:
```csharp
using Supermonitor.Core.Sensors.Network;

namespace Supermonitor.Tests.Sensors;

public class RateCalculatorTests
{
    [Fact]
    public void Computes_bytes_per_second()
    {
        Assert.Equal(500, RateCalculator.PerSecond(1000, 2000, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Counter_reset_yields_zero()
    {
        Assert.Equal(0, RateCalculator.PerSecond(5000, 100, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Zero_elapsed_yields_nan()
    {
        Assert.True(double.IsNaN(RateCalculator.PerSecond(0, 100, TimeSpan.Zero)));
    }
}
```

`tests/Supermonitor.Tests/Sensors/NetworkAdapterFilterTests.cs`:
```csharp
using Supermonitor.Core.Sensors.Network;

namespace Supermonitor.Tests.Sensors;

public class NetworkAdapterFilterTests
{
    private const uint Ethernet = 6;
    private const byte Hardware = 0x1;
    private const byte Filter = 0x2;

    [Fact]
    public void Hardware_adapter_that_is_up_is_monitored()
    {
        Assert.True(NetworkAdapterFilter.IsMonitored(Ethernet, NetworkAdapterFilter.OperStatusUp, Hardware));
    }

    [Theory]
    [InlineData(Ethernet, 2, Hardware)]
    [InlineData(Ethernet, 1, 0)]
    [InlineData(Ethernet, 1, Hardware | Filter)]
    [InlineData(24u, 1, Hardware)]
    [InlineData(131u, 1, Hardware)]
    public void Down_virtual_filter_loopback_and_tunnel_adapters_are_ignored(uint type, int operStatus, byte flags)
    {
        Assert.False(NetworkAdapterFilter.IsMonitored(type, operStatus, flags));
    }
}
```

Adicionar ao teste `Native_memory_structs_match_x64_sizes` em `StructLayoutTests.cs`:
```csharp
        Assert.Equal(1352, Unsafe.SizeOf<Iphlpapi.MIB_IF_ROW2>());
```

Adicionar em `ProviderSmokeTests` (e `using Supermonitor.Core.Sensors.Network;`):
```csharp
    [Fact]
    public void Network_provider_reports_totals()
    {
        using var provider = new NetworkProvider();
        var store = SampleTwice(provider);

        Assert.True(store.GetLatest(MetricIds.NetworkTotalDown) >= 0);
        Assert.True(store.GetLatest(MetricIds.NetworkTotalUp) >= 0);
    }
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"`
Expected: erro de compilação `RateCalculator`/`Iphlpapi` não encontrados.

- [ ] **Step 3: Implementar**

`src/Supermonitor.Core/Interop/Iphlpapi.cs`:
```csharp
using System.Runtime.InteropServices;

namespace Supermonitor.Core.Interop;

internal static unsafe partial class Iphlpapi
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_IF_ROW2
    {
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public Guid InterfaceGuid;
        public fixed char Alias[257];
        public fixed char Description[257];
        public uint PhysicalAddressLength;
        public fixed byte PhysicalAddress[32];
        public fixed byte PermanentPhysicalAddress[32];
        public uint Mtu;
        public uint Type;
        public int TunnelType;
        public int MediaType;
        public int PhysicalMediumType;
        public int AccessType;
        public int DirectionType;
        public byte InterfaceAndOperStatusFlags;
        public int OperStatus;
        public int AdminStatus;
        public int MediaConnectState;
        public Guid NetworkGuid;
        public int ConnectionType;
        public ulong TransmitLinkSpeed;
        public ulong ReceiveLinkSpeed;
        public ulong InOctets;
        public ulong InUcastPkts;
        public ulong InNUcastPkts;
        public ulong InDiscards;
        public ulong InErrors;
        public ulong InUnknownProtos;
        public ulong InUcastOctets;
        public ulong InMulticastOctets;
        public ulong InBroadcastOctets;
        public ulong OutOctets;
        public ulong OutUcastPkts;
        public ulong OutNUcastPkts;
        public ulong OutDiscards;
        public ulong OutErrors;
        public ulong OutUcastOctets;
        public ulong OutMulticastOctets;
        public ulong OutBroadcastOctets;
        public ulong OutQLen;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_IF_TABLE2
    {
        public uint NumEntries;
        public MIB_IF_ROW2 Table;
    }

    [LibraryImport("iphlpapi.dll")]
    public static partial uint GetIfTable2(MIB_IF_TABLE2** table);

    [LibraryImport("iphlpapi.dll")]
    public static partial uint GetIfEntry2(MIB_IF_ROW2* row);

    [LibraryImport("iphlpapi.dll")]
    public static partial void FreeMibTable(void* memory);
}
```

`src/Supermonitor.Core/Sensors/Network/RateCalculator.cs`:
```csharp
namespace Supermonitor.Core.Sensors.Network;

public static class RateCalculator
{
    public static double PerSecond(ulong previous, ulong current, TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero)
        {
            return double.NaN;
        }

        return current < previous ? 0 : (current - previous) / elapsed.TotalSeconds;
    }
}
```

`src/Supermonitor.Core/Sensors/Network/NetworkAdapterFilter.cs`:
```csharp
namespace Supermonitor.Core.Sensors.Network;

public static class NetworkAdapterFilter
{
    public const uint IfTypeSoftwareLoopback = 24;
    public const uint IfTypeTunnel = 131;
    public const int OperStatusUp = 1;

    private const byte HardwareInterfaceFlag = 0x1;
    private const byte FilterInterfaceFlag = 0x2;

    public static bool IsMonitored(uint ifType, int operStatus, byte flags) =>
        (flags & HardwareInterfaceFlag) != 0
        && (flags & FilterInterfaceFlag) == 0
        && operStatus == OperStatusUp
        && ifType != IfTypeSoftwareLoopback
        && ifType != IfTypeTunnel;
}
```

`src/Supermonitor.Core/Sensors/Network/NetworkProvider.cs`:
```csharp
using System.ComponentModel;
using Supermonitor.Core.Interop;
using Supermonitor.Core.Metrics;

namespace Supermonitor.Core.Sensors.Network;

public sealed unsafe class NetworkProvider : ISensorProvider
{
    private Adapter[] _adapters = [];
    private MetricHandle _totalDown;
    private MetricHandle _totalUp;

    public string Name => "Network";

    public TimeSpan MinInterval => TimeSpan.Zero;

    public void Initialize(MetricStore store)
    {
        Iphlpapi.MIB_IF_TABLE2* table;
        var error = Iphlpapi.GetIfTable2(&table);
        if (error != 0)
        {
            throw new Win32Exception((int)error);
        }

        var adapters = new List<Adapter>();
        try
        {
            var rows = &table->Table;
            for (var i = 0; i < table->NumEntries; i++)
            {
                var row = &rows[i];
                if (!NetworkAdapterFilter.IsMonitored(row->Type, row->OperStatus, row->InterfaceAndOperStatusFlags))
                {
                    continue;
                }

                var alias = new string(row->Alias);
                var n = adapters.Count;
                adapters.Add(new Adapter
                {
                    Luid = row->InterfaceLuid,
                    Down = Register(store, MetricIds.NetworkDown(n), $"{alias} download"),
                    Up = Register(store, MetricIds.NetworkUp(n), $"{alias} upload"),
                });
            }
        }
        finally
        {
            Iphlpapi.FreeMibTable(table);
        }

        _adapters = adapters.ToArray();
        _totalDown = Register(store, MetricIds.NetworkTotalDown, "Download");
        _totalUp = Register(store, MetricIds.NetworkTotalUp, "Upload");
    }

    public void Sample(MetricStore store, TimeSpan elapsed)
    {
        double down = 0;
        double up = 0;
        var any = false;

        foreach (var adapter in _adapters)
        {
            var row = new Iphlpapi.MIB_IF_ROW2 { InterfaceLuid = adapter.Luid };
            if (Iphlpapi.GetIfEntry2(&row) != 0)
            {
                store.Write(adapter.Down, float.NaN);
                store.Write(adapter.Up, float.NaN);
                adapter.HasLast = false;
                continue;
            }

            if (adapter.HasLast)
            {
                var d = RateCalculator.PerSecond(adapter.LastIn, row.InOctets, elapsed);
                var u = RateCalculator.PerSecond(adapter.LastOut, row.OutOctets, elapsed);
                store.Write(adapter.Down, (float)d);
                store.Write(adapter.Up, (float)u);
                if (double.IsFinite(d) && double.IsFinite(u))
                {
                    down += d;
                    up += u;
                    any = true;
                }
            }

            adapter.LastIn = row.InOctets;
            adapter.LastOut = row.OutOctets;
            adapter.HasLast = true;
        }

        store.Write(_totalDown, any ? (float)down : float.NaN);
        store.Write(_totalUp, any ? (float)up : float.NaN);
    }

    public void Dispose()
    {
    }

    private MetricHandle Register(MetricStore store, string id, string name) =>
        store.Register(new MetricDescriptor(id, name, MetricCategory.Network, MetricUnit.BytesPerSecond), Name);

    private sealed class Adapter
    {
        public ulong Luid { get; init; }

        public MetricHandle Down { get; init; }

        public MetricHandle Up { get; init; }

        public ulong LastIn { get; set; }

        public ulong LastOut { get; set; }

        public bool HasLast { get; set; }
    }
}
```

- [ ] **Step 4: Rodar unitários e hardware**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"` → todos passam (inclui `MIB_IF_ROW2` = 1352).
Run: `dotnet test tests/Supermonitor.Tests --filter "Category=Hardware"` → todos passam.

- [ ] **Step 5: Commit**

```powershell
git add src/Supermonitor.Core tests/Supermonitor.Tests
git commit -m "feat(sensors): add network throughput provider"
```

---

### Task 11: Ping

**Files:**
- Create: `src/Supermonitor.Core/Sensors/Network/PingStatistics.cs`, `PingMonitor.cs`, `PingProvider.cs`
- Test: `tests/Supermonitor.Tests/Sensors/PingStatisticsTests.cs`

**Interfaces:**
- Consumes: `RingBuffer<float>` (Task 2).
- Produces: `readonly record struct PingSnapshot(double LatencyMs, double JitterMs, double LossPercent)`; `PingStatistics(int window)` com `AddReply(double ms)`, `AddLoss()`, `PingSnapshot GetSnapshot()`. Latência = última amostra (NaN se a última foi perda); jitter = média das diferenças absolutas entre respostas consecutivas; perda = % de perdas na janela.
- Produces: `PingMonitor(Func<string> host, TimeSpan period)` com `Start()`, `PingSnapshot Snapshot`, `Dispose()`; `PingProvider(PingMonitor monitor)` (`net.ping`, `net.jitter`, `net.loss`).

- [ ] **Step 1: Escrever os testes que falham**

`tests/Supermonitor.Tests/Sensors/PingStatisticsTests.cs`:
```csharp
using Supermonitor.Core.Sensors.Network;

namespace Supermonitor.Tests.Sensors;

public class PingStatisticsTests
{
    [Fact]
    public void Empty_statistics_are_not_available()
    {
        var snapshot = new PingStatistics(10).GetSnapshot();

        Assert.True(double.IsNaN(snapshot.LatencyMs));
        Assert.True(double.IsNaN(snapshot.JitterMs));
        Assert.True(double.IsNaN(snapshot.LossPercent));
    }

    [Fact]
    public void Replies_produce_latest_latency_and_mean_jitter()
    {
        var stats = new PingStatistics(10);
        stats.AddReply(20);
        stats.AddReply(30);
        stats.AddReply(25);

        var snapshot = stats.GetSnapshot();

        Assert.Equal(25, snapshot.LatencyMs);
        Assert.Equal(7.5, snapshot.JitterMs);
        Assert.Equal(0, snapshot.LossPercent);
    }

    [Fact]
    public void Losses_count_toward_loss_and_are_skipped_for_jitter()
    {
        var stats = new PingStatistics(10);
        stats.AddReply(20);
        stats.AddLoss();
        stats.AddReply(30);

        var snapshot = stats.GetSnapshot();

        Assert.Equal(30, snapshot.LatencyMs);
        Assert.Equal(10, snapshot.JitterMs);
        Assert.Equal(33.33, snapshot.LossPercent, 2);
    }

    [Fact]
    public void Latest_loss_makes_latency_not_available()
    {
        var stats = new PingStatistics(10);
        stats.AddReply(20);
        stats.AddLoss();

        var snapshot = stats.GetSnapshot();

        Assert.True(double.IsNaN(snapshot.LatencyMs));
        Assert.Equal(50, snapshot.LossPercent);
    }

    [Fact]
    public void Window_drops_oldest_samples()
    {
        var stats = new PingStatistics(2);
        stats.AddLoss();
        stats.AddReply(10);
        stats.AddReply(12);

        Assert.Equal(0, stats.GetSnapshot().LossPercent);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"`
Expected: erro de compilação `PingStatistics` não encontrado.

- [ ] **Step 3: Implementar**

`src/Supermonitor.Core/Sensors/Network/PingStatistics.cs`:
```csharp
using Supermonitor.Core.Collections;

namespace Supermonitor.Core.Sensors.Network;

public readonly record struct PingSnapshot(double LatencyMs, double JitterMs, double LossPercent);

public sealed class PingStatistics(int window)
{
    private readonly RingBuffer<float> _samples = new(window);

    public void AddReply(double milliseconds) => _samples.Add((float)milliseconds);

    public void AddLoss() => _samples.Add(float.NaN);

    public PingSnapshot GetSnapshot()
    {
        if (_samples.Count == 0)
        {
            return new PingSnapshot(double.NaN, double.NaN, double.NaN);
        }

        var lost = 0;
        double jitterSum = 0;
        var jitterCount = 0;
        var previous = float.NaN;
        for (var i = 0; i < _samples.Count; i++)
        {
            var sample = _samples[i];
            if (float.IsNaN(sample))
            {
                lost++;
                continue;
            }

            if (!float.IsNaN(previous))
            {
                jitterSum += Math.Abs(sample - previous);
                jitterCount++;
            }

            previous = sample;
        }

        return new PingSnapshot(
            _samples.Latest,
            jitterCount == 0 ? double.NaN : jitterSum / jitterCount,
            lost * 100.0 / _samples.Count);
    }
}
```

`src/Supermonitor.Core/Sensors/Network/PingMonitor.cs`:
```csharp
using System.Net.NetworkInformation;

namespace Supermonitor.Core.Sensors.Network;

public sealed class PingMonitor(Func<string> host, TimeSpan period) : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private readonly PingStatistics _statistics = new(10);
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly byte[] _payload = new byte[32];
    private Task? _loop;

    public PingSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _statistics.GetSnapshot();
            }
        }
    }

    public void Start() => _loop = Task.Run(() => RunAsync(_cts.Token));

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _cts.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var ping = new Ping();
        using var timer = new PeriodicTimer(period);
        try
        {
            do
            {
                try
                {
                    var reply = await ping.SendPingAsync(host(), Timeout, _payload, null, cancellationToken).ConfigureAwait(false);
                    lock (_gate)
                    {
                        if (reply.Status == IPStatus.Success)
                        {
                            _statistics.AddReply(reply.RoundtripTime);
                        }
                        else
                        {
                            _statistics.AddLoss();
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lock (_gate)
                    {
                        _statistics.AddLoss();
                    }
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
        }
    }
}
```

`src/Supermonitor.Core/Sensors/Network/PingProvider.cs`:
```csharp
using Supermonitor.Core.Metrics;

namespace Supermonitor.Core.Sensors.Network;

public sealed class PingProvider(PingMonitor monitor) : ISensorProvider
{
    private MetricHandle _latency;
    private MetricHandle _jitter;
    private MetricHandle _loss;

    public string Name => "Ping";

    public TimeSpan MinInterval => TimeSpan.Zero;

    public void Initialize(MetricStore store)
    {
        _latency = store.Register(new MetricDescriptor(MetricIds.Ping, "Ping", MetricCategory.Network, MetricUnit.Milliseconds), Name);
        _jitter = store.Register(new MetricDescriptor(MetricIds.PingJitter, "Jitter", MetricCategory.Network, MetricUnit.Milliseconds), Name);
        _loss = store.Register(new MetricDescriptor(MetricIds.PingLoss, "Packet loss", MetricCategory.Network, MetricUnit.Percent), Name);
    }

    public void Sample(MetricStore store, TimeSpan elapsed)
    {
        var snapshot = monitor.Snapshot;
        store.Write(_latency, (float)snapshot.LatencyMs);
        store.Write(_jitter, (float)snapshot.JitterMs);
        store.Write(_loss, (float)snapshot.LossPercent);
    }

    public void Dispose()
    {
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"` → todos passam.

- [ ] **Step 5: Commit**

```powershell
git add src/Supermonitor.Core/Sensors/Network tests/Supermonitor.Tests/Sensors/PingStatisticsTests.cs
git commit -m "feat(sensors): add ping latency, jitter and loss"
```

---

### Task 12: Provedor de GPU (PDH + DXGI)

**Files:**
- Create: `src/Supermonitor.Core/Interop/Dxgi.cs`
- Create: `src/Supermonitor.Core/Sensors/Gpu/GpuAdapterInfo.cs`, `GpuEngineAggregator.cs`, `GpuProvider.cs`
- Test: `tests/Supermonitor.Tests/Sensors/GpuEngineAggregatorTests.cs`, `tests/Supermonitor.Tests/Sensors/GpuAdapterFilterTests.cs`; modificar `StructLayoutTests.cs` e `ProviderSmokeTests.cs`

**Interfaces:**
- Consumes: `PdhQuery`/`PdhArray` (Task 7).
- Produces: `sealed record GpuAdapterInfo(string Name, uint LuidLow, int LuidHigh, ulong DedicatedVideoMemory, uint Flags)` com `string PdhLuid` (`"0x{High:X8}_0x{Low:X8}"`); `GpuAdapterFilter.IsMonitored(uint flags, ulong dedicatedVideoMemory)`; `DxgiAdapters.Enumerate() -> IReadOnlyList<GpuAdapterInfo>`.
- Produces: `enum GpuEngineKind { Other, ThreeD, Copy, VideoDecode, VideoEncode, Compute }`; `readonly record struct GpuUsage(float Total, float ThreeD, float Copy, float VideoDecode, float VideoEncode, float Compute)`; `GpuEngineAggregator(IReadOnlyList<string> adapterLuids)` com `Reset()`, `Add(ReadOnlySpan<char> instanceName, double value)`, `GpuUsage GetUsage(int adapter)`. Uso por engine = soma entre processos; por tipo = máximo entre engines do tipo; total = máximo entre engines (igual ao Gerenciador de Tarefas); limitado a 100.
- Produces: `GpuProvider` (`gpu{n}.usage`, `.3d`, `.copy`, `.decode`, `.encode`, `.compute`, `.vram.used`, `.vram.total`, `.shared.used`).

- [ ] **Step 1: Escrever os testes que falham**

`tests/Supermonitor.Tests/Sensors/GpuEngineAggregatorTests.cs`:
```csharp
using Supermonitor.Core.Sensors.Gpu;

namespace Supermonitor.Tests.Sensors;

public class GpuEngineAggregatorTests
{
    private const string Luid = "0x00000000_0x0000D1F2";

    private static string Instance(int pid, int engine, string type, string luid = Luid) =>
        $"pid_{pid}_luid_{luid}_phys_0_eng_{engine}_engtype_{type}";

    [Fact]
    public void Sums_processes_per_engine_and_takes_max_per_type()
    {
        var aggregator = new GpuEngineAggregator([Luid]);
        aggregator.Add(Instance(100, 0, "3D"), 30);
        aggregator.Add(Instance(200, 0, "3D"), 25);
        aggregator.Add(Instance(100, 1, "Copy"), 10);
        aggregator.Add(Instance(100, 2, "VideoDecode"), 5);
        aggregator.Add(Instance(100, 3, "VideoEncode"), 4);
        aggregator.Add(Instance(100, 4, "Compute_0"), 3);

        var usage = aggregator.GetUsage(0);

        Assert.Equal(55, usage.ThreeD);
        Assert.Equal(10, usage.Copy);
        Assert.Equal(5, usage.VideoDecode);
        Assert.Equal(4, usage.VideoEncode);
        Assert.Equal(3, usage.Compute);
        Assert.Equal(55, usage.Total);
    }

    [Fact]
    public void Ignores_other_adapters_malformed_names_and_non_finite_values()
    {
        var aggregator = new GpuEngineAggregator([Luid]);
        aggregator.Add(Instance(1, 0, "3D", "0x00000000_0x0000AAAA"), 90);
        aggregator.Add("garbage", 90);
        aggregator.Add("pid_1_luid_0x00000000_0x0000D1F2_phys_0_eng_x_engtype_3D", 90);
        aggregator.Add(Instance(1, 0, "3D"), double.NaN);

        Assert.Equal(0, aggregator.GetUsage(0).Total);
    }

    [Fact]
    public void Luid_match_is_case_insensitive_and_usage_is_capped_at_100()
    {
        var aggregator = new GpuEngineAggregator([Luid]);
        aggregator.Add(Instance(1, 0, "3D", "0x00000000_0x0000d1f2"), 80);
        aggregator.Add(Instance(2, 0, "3D"), 70);

        Assert.Equal(100, aggregator.GetUsage(0).Total);
    }

    [Fact]
    public void Reset_clears_previous_tick()
    {
        var aggregator = new GpuEngineAggregator([Luid]);
        aggregator.Add(Instance(1, 0, "3D"), 40);
        aggregator.Reset();

        Assert.Equal(0, aggregator.GetUsage(0).Total);
    }
}
```

`tests/Supermonitor.Tests/Sensors/GpuAdapterFilterTests.cs`:
```csharp
using Supermonitor.Core.Sensors.Gpu;

namespace Supermonitor.Tests.Sensors;

public class GpuAdapterFilterTests
{
    [Fact]
    public void Real_gpu_with_dedicated_memory_is_monitored()
    {
        Assert.True(GpuAdapterFilter.IsMonitored(0, 16UL * 1024 * 1024 * 1024));
    }

    [Fact]
    public void Software_adapter_is_ignored()
    {
        Assert.False(GpuAdapterFilter.IsMonitored(0x2, 0));
    }

    [Fact]
    public void Virtual_display_adapter_without_dedicated_memory_is_ignored()
    {
        Assert.False(GpuAdapterFilter.IsMonitored(0, 0));
    }

    [Fact]
    public void PdhLuid_matches_performance_counter_format()
    {
        var info = new GpuAdapterInfo("AMD Radeon RX 9070 XT", 0xD1F2, 0, 1, 0);

        Assert.Equal("0x00000000_0x0000D1F2", info.PdhLuid);
    }
}
```

Adicionar ao teste em `StructLayoutTests.cs`:
```csharp
        Assert.Equal(312, Unsafe.SizeOf<Dxgi.DXGI_ADAPTER_DESC1>());
```

Adicionar em `ProviderSmokeTests` (e `using Supermonitor.Core.Sensors.Gpu;`):
```csharp
    [Fact]
    public void Gpu_provider_reports_usage_and_vram_for_first_gpu()
    {
        using var provider = new GpuProvider();
        var store = SampleTwice(provider);

        Assert.InRange(store.GetLatest(MetricIds.GpuUsage(0)), 0, 100);
        Assert.True(store.GetLatest(MetricIds.GpuVramTotal(0)) > 0);
        Assert.True(store.GetLatest(MetricIds.GpuVramUsed(0)) > 0);
    }
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"`
Expected: erro de compilação `GpuEngineAggregator` não encontrado.

- [ ] **Step 3: Implementar DXGI**

`src/Supermonitor.Core/Interop/Dxgi.cs`:
```csharp
using System.Runtime.InteropServices;

namespace Supermonitor.Core.Interop;

internal static unsafe partial class Dxgi
{
    public const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);
    public const uint DXGI_ADAPTER_FLAG_SOFTWARE = 0x2;

    public static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_ADAPTER_DESC1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    [LibraryImport("dxgi.dll")]
    public static partial int CreateDXGIFactory1(Guid* riid, void** factory);

    // Índices da vtable: IUnknown (0-2), IDXGIObject (3-6), IDXGIFactory (7-11), IDXGIFactory1.EnumAdapters1 = 12.
    public static int EnumAdapters1(void* factory, uint index, void** adapter) =>
        ((delegate* unmanaged<void*, uint, void**, int>)(*(void***)factory)[12])(factory, index, adapter);

    // IUnknown (0-2), IDXGIObject (3-6), IDXGIAdapter (7-9), IDXGIAdapter1.GetDesc1 = 10.
    public static int GetDesc1(void* adapter, DXGI_ADAPTER_DESC1* description) =>
        ((delegate* unmanaged<void*, DXGI_ADAPTER_DESC1*, int>)(*(void***)adapter)[10])(adapter, description);

    public static uint Release(void* unknown) =>
        ((delegate* unmanaged<void*, uint>)(*(void***)unknown)[2])(unknown);
}
```

`src/Supermonitor.Core/Sensors/Gpu/GpuAdapterInfo.cs`:
```csharp
using System.Runtime.InteropServices;
using Supermonitor.Core.Interop;

namespace Supermonitor.Core.Sensors.Gpu;

public sealed record GpuAdapterInfo(string Name, uint LuidLow, int LuidHigh, ulong DedicatedVideoMemory, uint Flags)
{
    public string PdhLuid => $"0x{(uint)LuidHigh:X8}_0x{LuidLow:X8}";
}

public static class GpuAdapterFilter
{
    public static bool IsMonitored(uint flags, ulong dedicatedVideoMemory) =>
        (flags & Dxgi.DXGI_ADAPTER_FLAG_SOFTWARE) == 0 && dedicatedVideoMemory > 0;
}

public static unsafe class DxgiAdapters
{
    public static IReadOnlyList<GpuAdapterInfo> Enumerate()
    {
        var iid = Dxgi.IID_IDXGIFactory1;
        void* factory;
        Marshal.ThrowExceptionForHR(Dxgi.CreateDXGIFactory1(&iid, &factory));

        var adapters = new List<GpuAdapterInfo>();
        try
        {
            for (uint index = 0; ; index++)
            {
                void* adapter;
                var hr = Dxgi.EnumAdapters1(factory, index, &adapter);
                if (hr == Dxgi.DXGI_ERROR_NOT_FOUND)
                {
                    break;
                }

                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    Dxgi.DXGI_ADAPTER_DESC1 description;
                    Marshal.ThrowExceptionForHR(Dxgi.GetDesc1(adapter, &description));
                    adapters.Add(new GpuAdapterInfo(
                        new string(description.Description),
                        description.LuidLow,
                        description.LuidHigh,
                        description.DedicatedVideoMemory,
                        description.Flags));
                }
                finally
                {
                    Dxgi.Release(adapter);
                }
            }
        }
        finally
        {
            Dxgi.Release(factory);
        }

        return adapters;
    }
}
```

- [ ] **Step 4: Implementar agregador e provedor**

`src/Supermonitor.Core/Sensors/Gpu/GpuEngineAggregator.cs`:
```csharp
using System.Globalization;

namespace Supermonitor.Core.Sensors.Gpu;

public enum GpuEngineKind
{
    Other,
    ThreeD,
    Copy,
    VideoDecode,
    VideoEncode,
    Compute,
}

public readonly record struct GpuUsage(float Total, float ThreeD, float Copy, float VideoDecode, float VideoEncode, float Compute);

public sealed class GpuEngineAggregator
{
    public const int MaxEngines = 64;

    private readonly string[] _luids;
    private readonly double[] _sums;
    private readonly GpuEngineKind[] _kinds;

    public GpuEngineAggregator(IReadOnlyList<string> adapterLuids)
    {
        _luids = adapterLuids.ToArray();
        _sums = new double[_luids.Length * MaxEngines];
        _kinds = new GpuEngineKind[_luids.Length * MaxEngines];
    }

    public void Reset() => Array.Clear(_sums);

    // Formato: "pid_1234_luid_0x00000000_0x0000D1F2_phys_0_eng_0_engtype_3D".
    public void Add(ReadOnlySpan<char> instanceName, double value)
    {
        if (!double.IsFinite(value) || !TryParse(instanceName, out var luid, out var engine, out var engineType))
        {
            return;
        }

        var adapter = IndexOf(luid);
        if (adapter < 0 || engine < 0 || engine >= MaxEngines)
        {
            return;
        }

        var slot = adapter * MaxEngines + engine;
        _sums[slot] += value;
        _kinds[slot] = Classify(engineType);
    }

    public GpuUsage GetUsage(int adapter)
    {
        double total = 0, threeD = 0, copy = 0, decode = 0, encode = 0, compute = 0;
        for (var engine = 0; engine < MaxEngines; engine++)
        {
            var slot = adapter * MaxEngines + engine;
            var value = Math.Min(_sums[slot], 100);
            total = Math.Max(total, value);
            switch (_kinds[slot])
            {
                case GpuEngineKind.ThreeD:
                    threeD = Math.Max(threeD, value);
                    break;
                case GpuEngineKind.Copy:
                    copy = Math.Max(copy, value);
                    break;
                case GpuEngineKind.VideoDecode:
                    decode = Math.Max(decode, value);
                    break;
                case GpuEngineKind.VideoEncode:
                    encode = Math.Max(encode, value);
                    break;
                case GpuEngineKind.Compute:
                    compute = Math.Max(compute, value);
                    break;
            }
        }

        return new GpuUsage((float)total, (float)threeD, (float)copy, (float)decode, (float)encode, (float)compute);
    }

    internal static bool TryParse(ReadOnlySpan<char> name, out ReadOnlySpan<char> luid, out int engine, out ReadOnlySpan<char> engineType)
    {
        luid = default;
        engineType = default;
        engine = -1;

        var luidStart = name.IndexOf("luid_");
        if (luidStart < 0)
        {
            return false;
        }

        var rest = name[(luidStart + "luid_".Length)..];
        var physIndex = rest.IndexOf("_phys_");
        var engIndex = rest.IndexOf("_eng_");
        if (physIndex < 0 || engIndex < 0)
        {
            return false;
        }

        luid = rest[..physIndex];
        var afterEng = rest[(engIndex + "_eng_".Length)..];
        var typeIndex = afterEng.IndexOf("_engtype_");
        if (typeIndex < 0 || !int.TryParse(afterEng[..typeIndex], NumberStyles.None, CultureInfo.InvariantCulture, out engine))
        {
            engine = -1;
            return false;
        }

        engineType = afterEng[(typeIndex + "_engtype_".Length)..];
        return true;
    }

    private static GpuEngineKind Classify(ReadOnlySpan<char> type)
    {
        if (type.Equals("3D", StringComparison.OrdinalIgnoreCase))
        {
            return GpuEngineKind.ThreeD;
        }

        if (type.Equals("Copy", StringComparison.OrdinalIgnoreCase))
        {
            return GpuEngineKind.Copy;
        }

        if (type.StartsWith("VideoDecode", StringComparison.OrdinalIgnoreCase))
        {
            return GpuEngineKind.VideoDecode;
        }

        if (type.StartsWith("VideoEncode", StringComparison.OrdinalIgnoreCase))
        {
            return GpuEngineKind.VideoEncode;
        }

        return type.StartsWith("Compute", StringComparison.OrdinalIgnoreCase) ? GpuEngineKind.Compute : GpuEngineKind.Other;
    }

    private int IndexOf(ReadOnlySpan<char> luid)
    {
        for (var i = 0; i < _luids.Length; i++)
        {
            if (luid.Equals(_luids[i], StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
```

`src/Supermonitor.Core/Sensors/Gpu/GpuProvider.cs`:
```csharp
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors.Pdh;

namespace Supermonitor.Core.Sensors.Gpu;

public sealed class GpuProvider : ISensorProvider
{
    private Adapter[] _adapters = [];
    private GpuEngineAggregator? _aggregator;
    private PdhQuery? _query;
    private PdhCounter? _engines;
    private PdhCounter? _dedicated;
    private PdhCounter? _shared;

    public string Name => "GPU";

    public TimeSpan MinInterval => TimeSpan.Zero;

    public void Initialize(MetricStore store)
    {
        var infos = DxgiAdapters.Enumerate()
            .Where(a => GpuAdapterFilter.IsMonitored(a.Flags, a.DedicatedVideoMemory))
            .ToArray();
        if (infos.Length == 0)
        {
            throw new InvalidOperationException("No hardware GPU found.");
        }

        _adapters = infos.Select((info, n) => Register(store, info, n, infos.Length)).ToArray();
        _aggregator = new GpuEngineAggregator(infos.Select(i => i.PdhLuid).ToArray());

        _query = new PdhQuery();
        _engines = _query.AddCounter(@"\GPU Engine(*)\Utilization Percentage");
        _dedicated = _query.AddCounter(@"\GPU Adapter Memory(*)\Dedicated Usage");
        _shared = _query.AddCounter(@"\GPU Adapter Memory(*)\Shared Usage");
        _query.Collect();
    }

    public void Sample(MetricStore store, TimeSpan elapsed)
    {
        _query!.Collect();
        _aggregator!.Reset();

        var engines = _engines!.GetArray();
        for (var i = 0; i < engines.Count; i++)
        {
            _aggregator.Add(engines.NameAt(i), engines.ValueAt(i));
        }

        for (var n = 0; n < _adapters.Length; n++)
        {
            var adapter = _adapters[n];
            var usage = _aggregator.GetUsage(n);
            store.Write(adapter.Usage, usage.Total);
            store.Write(adapter.ThreeD, usage.ThreeD);
            store.Write(adapter.Copy, usage.Copy);
            store.Write(adapter.Decode, usage.VideoDecode);
            store.Write(adapter.Encode, usage.VideoEncode);
            store.Write(adapter.Compute, usage.Compute);
            store.Write(adapter.VramTotal, adapter.Info.DedicatedVideoMemory);
        }

        WriteMemory(store, _dedicated!.GetArray(), dedicated: true);
        WriteMemory(store, _shared!.GetArray(), dedicated: false);
    }

    public void Dispose() => _query?.Dispose();

    private void WriteMemory(MetricStore store, PdhArray array, bool dedicated)
    {
        for (var i = 0; i < array.Count; i++)
        {
            var name = array.NameAt(i);
            foreach (var adapter in _adapters)
            {
                if (name.StartsWith(adapter.MemoryPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    store.Write(dedicated ? adapter.VramUsed : adapter.SharedUsed, (float)array.ValueAt(i));
                }
            }
        }
    }

    private Adapter Register(MetricStore store, GpuAdapterInfo info, int n, int total)
    {
        var prefix = total > 1 ? $"GPU {n}" : "GPU";
        MetricHandle Add(string id, string name, MetricUnit unit) =>
            store.Register(new MetricDescriptor(id, $"{prefix} {name}", MetricCategory.Gpu, unit), Name);

        return new Adapter(info, $"luid_{info.PdhLuid}")
        {
            Usage = Add(MetricIds.GpuUsage(n), "usage", MetricUnit.Percent),
            ThreeD = Add(MetricIds.Gpu3D(n), "3D", MetricUnit.Percent),
            Copy = Add(MetricIds.GpuCopy(n), "copy", MetricUnit.Percent),
            Decode = Add(MetricIds.GpuVideoDecode(n), "video decode", MetricUnit.Percent),
            Encode = Add(MetricIds.GpuVideoEncode(n), "video encode", MetricUnit.Percent),
            Compute = Add(MetricIds.GpuCompute(n), "compute", MetricUnit.Percent),
            VramUsed = Add(MetricIds.GpuVramUsed(n), "VRAM used", MetricUnit.Bytes),
            VramTotal = Add(MetricIds.GpuVramTotal(n), "VRAM total", MetricUnit.Bytes),
            SharedUsed = Add(MetricIds.GpuSharedUsed(n), "shared memory used", MetricUnit.Bytes),
        };
    }

    private sealed class Adapter(GpuAdapterInfo info, string memoryPrefix)
    {
        public GpuAdapterInfo Info { get; } = info;

        public string MemoryPrefix { get; } = memoryPrefix;

        public MetricHandle Usage { get; init; }

        public MetricHandle ThreeD { get; init; }

        public MetricHandle Copy { get; init; }

        public MetricHandle Decode { get; init; }

        public MetricHandle Encode { get; init; }

        public MetricHandle Compute { get; init; }

        public MetricHandle VramUsed { get; init; }

        public MetricHandle VramTotal { get; init; }

        public MetricHandle SharedUsed { get; init; }
    }
}
```

- [ ] **Step 5: Rodar unitários e hardware**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"` → todos passam.
Run: `dotnet test tests/Supermonitor.Tests --filter "Category=Hardware"` → todos passam; GPU 0 é a RX 9070 XT (o adaptador virtual SudoMaker fica de fora).

- [ ] **Step 6: Commit**

```powershell
git add src/Supermonitor.Core tests/Supermonitor.Tests
git commit -m "feat(sensors): add GPU engine and memory provider"
```

---

### Task 13: Utilitários de shell e composição

**Files:**
- Create: `src/Supermonitor.Core/Shell/AppPaths.cs`, `SingleInstance.cs`, `AutostartManager.cs`, `ProcessEfficiency.cs`
- Create: `src/Supermonitor.Core/AppHost.cs`
- Test: `tests/Supermonitor.Tests/Shell/SingleInstanceTests.cs`, `tests/Supermonitor.Tests/Shell/AutostartManagerTests.cs`

**Interfaces:**
- Consumes: tudo das Tasks 1–12; `Kernel32.SetProcessInformation`, `SetPriorityClass`, `GetCurrentProcess` (Task 8).
- Produces: `AppPaths.DataDirectory`, `LogDirectory`, `SettingsFile`, `IconPath`.
- Produces: `sealed record SingleInstanceNames(string Mutex, string ActivateEvent)` com `Default`; `SingleInstance.TryAcquire(SingleInstanceNames) -> SingleInstance?`, `static void SignalExisting(SingleInstanceNames)`, `void ListenForActivation(Action onActivate)`, `Dispose()` (no mesmo thread que adquiriu).
- Produces: `AutostartManager.TaskName` (`"Supermonitor"`), `static string BuildTaskXml(string exePath, string userId)`, `static bool IsEnabled()`, `static void SetEnabled(bool enabled, string exePath)`.
- Produces: `ProcessEfficiency.SetEfficiencyMode(bool enabled) -> bool` (EcoQoS + BELOW_NORMAL quando ligado; NORMAL quando desligado).
- Produces: `AppHost` com `static AppHost Create()`, `SettingsStore Settings`, `SettingsLoadResult SettingsLoadResult`, `MetricStore Metrics`, `SensorHub Hub`, `void Start()`, `ValueTask DisposeAsync()`.

- [ ] **Step 1: Escrever os testes que falham**

`tests/Supermonitor.Tests/Shell/SingleInstanceTests.cs`:
```csharp
using Supermonitor.Core.Shell;

namespace Supermonitor.Tests.Shell;

public class SingleInstanceTests
{
    private static SingleInstanceNames UniqueNames()
    {
        var id = Guid.NewGuid().ToString("N");
        return new SingleInstanceNames($@"Local\SmTest.{id}.Mutex", $@"Local\SmTest.{id}.Activate");
    }

    [Fact]
    public void Second_acquire_fails_while_first_is_held()
    {
        var names = UniqueNames();
        using var first = SingleInstance.TryAcquire(names);

        Assert.NotNull(first);
        Assert.Null(SingleInstance.TryAcquire(names));
    }

    [Fact]
    public void Acquire_succeeds_again_after_dispose()
    {
        var names = UniqueNames();
        SingleInstance.TryAcquire(names)!.Dispose();

        using var again = SingleInstance.TryAcquire(names);
        Assert.NotNull(again);
    }

    [Fact]
    public void Signal_from_second_instance_invokes_activation_callback()
    {
        var names = UniqueNames();
        using var first = SingleInstance.TryAcquire(names)!;
        using var activated = new ManualResetEventSlim();
        first.ListenForActivation(activated.Set);

        SingleInstance.SignalExisting(names);

        Assert.True(activated.Wait(TimeSpan.FromSeconds(5)));
    }
}
```

`tests/Supermonitor.Tests/Shell/AutostartManagerTests.cs`:
```csharp
using System.Xml.Linq;
using Supermonitor.Core.Shell;

namespace Supermonitor.Tests.Shell;

public class AutostartManagerTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Fact]
    public void Task_xml_runs_elevated_at_logon_without_time_limit()
    {
        var xml = XDocument.Parse(AutostartManager.BuildTaskXml(@"C:\Apps\Supermonitor.exe", @"PC\user"));

        Assert.Equal("HighestAvailable", xml.Descendants(Ns + "RunLevel").Single().Value);
        Assert.Equal("PT0S", xml.Descendants(Ns + "ExecutionTimeLimit").Single().Value);
        Assert.Equal("false", xml.Descendants(Ns + "DisallowStartIfOnBatteries").Single().Value);
        Assert.Equal(@"PC\user", xml.Descendants(Ns + "LogonTrigger").Single().Element(Ns + "UserId")!.Value);
        Assert.Equal(@"C:\Apps\Supermonitor.exe", xml.Descendants(Ns + "Command").Single().Value);
        Assert.Equal("--minimized", xml.Descendants(Ns + "Arguments").Single().Value);
    }

    [Fact]
    public void Task_xml_escapes_special_characters_in_path()
    {
        var xml = XDocument.Parse(AutostartManager.BuildTaskXml(@"C:\A&B <x>\Supermonitor.exe", @"PC\user"));

        Assert.Equal(@"C:\A&B <x>\Supermonitor.exe", xml.Descendants(Ns + "Command").Single().Value);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"`
Expected: erro de compilação `SingleInstance`/`AutostartManager` não encontrados.

- [ ] **Step 3: Implementar**

`src/Supermonitor.Core/Shell/AppPaths.cs`:
```csharp
namespace Supermonitor.Core.Shell;

public static class AppPaths
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Supermonitor");

    public static string LogDirectory { get; } = Path.Combine(DataDirectory, "logs");

    public static string SettingsFile { get; } = Path.Combine(DataDirectory, "settings.json");

    public static string IconPath { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "Supermonitor.ico");
}
```

`src/Supermonitor.Core/Shell/SingleInstance.cs`:
```csharp
namespace Supermonitor.Core.Shell;

public sealed record SingleInstanceNames(string Mutex, string ActivateEvent)
{
    public static SingleInstanceNames Default { get; } =
        new(@"Local\Supermonitor.SingleInstance", @"Local\Supermonitor.Activate");
}

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private RegisteredWaitHandle? _wait;

    private SingleInstance(Mutex mutex, SingleInstanceNames names)
    {
        _mutex = mutex;
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, names.ActivateEvent);
    }

    public static SingleInstance? TryAcquire(SingleInstanceNames names)
    {
        var mutex = new Mutex(initiallyOwned: true, names.Mutex, out var created);
        if (!created)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstance(mutex, names);
    }

    public static void SignalExisting(SingleInstanceNames names)
    {
        if (EventWaitHandle.TryOpenExisting(names.ActivateEvent, out var handle))
        {
            using (handle)
            {
                handle.Set();
            }
        }
    }

    public void ListenForActivation(Action onActivate)
    {
        _wait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _wait?.Unregister(null);
        _activate.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
```

`src/Supermonitor.Core/Shell/AutostartManager.cs`:
```csharp
using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace Supermonitor.Core.Shell;

public static class AutostartManager
{
    public const string TaskName = "Supermonitor";

    public static string BuildTaskXml(string exePath, string userId) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <Triggers>
            <LogonTrigger>
              <Enabled>true</Enabled>
              <UserId>{SecurityElement.Escape(userId)}</UserId>
            </LogonTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <UserId>{SecurityElement.Escape(userId)}</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>HighestAvailable</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <Priority>5</Priority>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>{SecurityElement.Escape(exePath)}</Command>
              <Arguments>--minimized</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;

    public static bool IsEnabled() => RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;

    public static void SetEnabled(bool enabled, string exePath)
    {
        if (!enabled)
        {
            RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
            return;
        }

        var xmlPath = Path.Combine(Path.GetTempPath(), $"supermonitor-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, BuildTaskXml(exePath, WindowsIdentity.GetCurrent().Name), Encoding.Unicode);
            var exitCode = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F");
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"schtasks exited with code {exitCode}.");
            }
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    private static int RunSchtasks(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        process.WaitForExit(10_000);
        return process.ExitCode;
    }
}
```

`src/Supermonitor.Core/Shell/ProcessEfficiency.cs`:
```csharp
using Supermonitor.Core.Interop;

namespace Supermonitor.Core.Shell;

public static unsafe class ProcessEfficiency
{
    public static bool SetEfficiencyMode(bool enabled)
    {
        var process = Kernel32.GetCurrentProcess();
        var state = new Kernel32.PROCESS_POWER_THROTTLING_STATE
        {
            Version = 1,
            ControlMask = Kernel32.PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
            StateMask = enabled ? Kernel32.PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0,
        };

        var throttled = Kernel32.SetProcessInformation(process, Kernel32.ProcessPowerThrottling, &state, (uint)sizeof(Kernel32.PROCESS_POWER_THROTTLING_STATE));
        var priority = Kernel32.SetPriorityClass(process, enabled ? Kernel32.BELOW_NORMAL_PRIORITY_CLASS : Kernel32.NORMAL_PRIORITY_CLASS);
        return throttled && priority;
    }
}
```

`src/Supermonitor.Core/AppHost.cs`:
```csharp
using Supermonitor.Core.Diagnostics;
using Supermonitor.Core.Metrics;
using Supermonitor.Core.Sensors;
using Supermonitor.Core.Sensors.Cpu;
using Supermonitor.Core.Sensors.Disk;
using Supermonitor.Core.Sensors.Gpu;
using Supermonitor.Core.Sensors.Memory;
using Supermonitor.Core.Sensors.Network;
using Supermonitor.Core.Settings;
using Supermonitor.Core.Shell;

namespace Supermonitor.Core;

public sealed class AppHost : IAsyncDisposable
{
    public const int HistoryCapacity = 2400;

    private readonly PingMonitor _ping;

    private AppHost(SettingsStore settings, SettingsLoadResult loadResult, MetricStore metrics, SensorHub hub, PingMonitor ping)
    {
        Settings = settings;
        SettingsLoadResult = loadResult;
        Metrics = metrics;
        Hub = hub;
        _ping = ping;
    }

    public SettingsStore Settings { get; }

    public SettingsLoadResult SettingsLoadResult { get; }

    public MetricStore Metrics { get; }

    public SensorHub Hub { get; }

    public static AppHost Create()
    {
        Log.Initialize(AppPaths.LogDirectory);
        Log.Info("Supermonitor starting");
        if (!Privileges.TryEnable(Privileges.ProfileSingleProcess))
        {
            Log.Warn("Could not enable SeProfileSingleProcessPrivilege; memory lists will be unavailable");
        }

        var settings = new SettingsStore(AppPaths.SettingsFile, TimeProvider.System, TimeSpan.FromMilliseconds(500));
        var loadResult = settings.Load();

        var metrics = new MetricStore(HistoryCapacity);
        var ping = new PingMonitor(() => settings.Current.PingHost, TimeSpan.FromSeconds(2));
        ISensorProvider[] providers =
        [
            new CpuProvider(),
            new MemoryProvider(),
            new GpuProvider(),
            new DiskProvider(),
            new DriveSpaceProvider(),
            new NetworkProvider(),
            new PingProvider(ping),
        ];
        var hub = new SensorHub(metrics, providers, TimeProvider.System, TimeSpan.FromMilliseconds(settings.Current.SampleIntervalMs));
        return new AppHost(settings, loadResult, metrics, hub, ping);
    }

    public void Start()
    {
        _ping.Start();
        Hub.Start();
    }

    public async ValueTask DisposeAsync()
    {
        _ping.Dispose();
        await Hub.DisposeAsync().ConfigureAwait(false);
        Settings.Dispose();
        Log.Info("Supermonitor stopped");
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"` → todos passam.

- [ ] **Step 5: Commit**

```powershell
git add src/Supermonitor.Core tests/Supermonitor.Tests/Shell
git commit -m "feat(core): add single instance, autostart task, efficiency mode and app host"
```

---

### Task 14: Shell WinUI 3 com bandeja

**Files:**
- Create: `src/Supermonitor.Core/Interop/User32.cs`, `src/Supermonitor.Core/Interop/Shell32.cs`
- Create: `src/Supermonitor.Core/Shell/HiddenWindow.cs`, `TrayIcon.cs`, `WindowActivation.cs`
- Create: `tools/make-icon.ps1`, `tools/run-elevated.ps1`
- Create: `src/Supermonitor/Supermonitor.csproj`, `app.manifest`, `Program.cs`, `App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `ViewModels/PageContext.cs`, `Assets/Supermonitor.ico` (gerado)
- Test: `tests/Supermonitor.Tests/Interop/StructLayoutTests.cs` (modificar)

**Interfaces:**
- Consumes: `AppHost`, `SingleInstance`, `ProcessEfficiency`, `AppPaths` (Task 13).
- Produces: `HiddenWindow(string className, Func<uint, nint, nint, nint?> handler)` com `nint Handle`, `Dispose()`. O handler devolve `null` para delegar a `DefWindowProcW`.
- Produces: `TrayIcon(string tooltip, string iconPath, Action onOpen, Action onExit)` com `Dispose()`. Deve ser criado no thread de UI.
- Produces: `WindowActivation.ForceForeground(nint hwnd)`.
- Produces: `sealed record PageContext(AppHost Host, Func<bool> IsMinimized)` (namespace `Supermonitor.ViewModels`).
- Produces: `MainWindow(AppHost host)` com `bool IsMinimized`, `void BringToFront()`. NavigationView com item `overview` e Settings; as páginas entram nas Tasks 15 e 16.

- [ ] **Step 1: Teste de layout das structs nativas (falha)**

Adicionar ao teste em `StructLayoutTests.cs`:
```csharp
        Assert.Equal(976, Unsafe.SizeOf<Shell32.NOTIFYICONDATAW>());
        Assert.Equal(80, Unsafe.SizeOf<User32.WNDCLASSEXW>());
```

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"`
Expected: erro de compilação `Shell32`/`User32` não encontrados.

- [ ] **Step 2: Interop Win32**

`src/Supermonitor.Core/Interop/User32.cs`:
```csharp
using System.Runtime.InteropServices;

namespace Supermonitor.Core.Interop;

internal static unsafe partial class User32
{
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WM_NULL = 0x0000;
    public const uint WM_CONTEXTMENU = 0x007B;
    public const uint WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_APP = 0x8000;
    public const uint MF_STRING = 0x0000;
    public const uint MF_SEPARATOR = 0x0800;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_NONOTIFY = 0x0080;
    public const uint TPM_RETURNCMD = 0x0100;
    public const uint IMAGE_ICON = 1;
    public const uint LR_LOADFROMFILE = 0x0010;
    public const int SM_CXSMICON = 49;
    public const int SM_CYSMICON = 50;

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
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

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
    public static partial nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll")]
    public static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AppendMenuW(nint menu, uint flags, nuint id, string? text);

    [LibraryImport("user32.dll")]
    public static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint parameters);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint LoadImageW(nint instance, string name, uint type, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);
}
```

`src/Supermonitor.Core/Interop/Shell32.cs`:
```csharp
using System.Runtime.InteropServices;

namespace Supermonitor.Core.Interop;

internal static unsafe partial class Shell32
{
    public const uint NIM_ADD = 0;
    public const uint NIM_DELETE = 2;
    public const uint NIM_SETVERSION = 4;
    public const uint NIF_MESSAGE = 0x1;
    public const uint NIF_ICON = 0x2;
    public const uint NIF_TIP = 0x4;
    public const uint NIF_SHOWTIP = 0x80;
    public const uint NOTIFYICON_VERSION_4 = 4;
    public const uint NIN_SELECT = 0x0400;
    public const uint NIN_KEYSELECT = 0x0401;

    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Shell_NotifyIconW(uint message, NOTIFYICONDATAW* data);
}
```

Run: `dotnet test tests/Supermonitor.Tests --filter "Category!=Hardware"`
Expected: todos passam (976 e 80 bytes).

- [ ] **Step 3: HiddenWindow, TrayIcon, WindowActivation**

`src/Supermonitor.Core/Shell/HiddenWindow.cs`:
```csharp
using System.ComponentModel;
using System.Runtime.InteropServices;
using Supermonitor.Core.Diagnostics;
using Supermonitor.Core.Interop;

namespace Supermonitor.Core.Shell;

public sealed unsafe class HiddenWindow : IDisposable
{
    private readonly Func<uint, nint, nint, nint?> _handler;
    private readonly User32.WndProc _wndProc;
    private readonly nint _className;
    private readonly nint _instance;

    public HiddenWindow(string className, Func<uint, nint, nint, nint?> handler)
    {
        _handler = handler;
        _wndProc = WndProc;
        _instance = Kernel32.GetModuleHandleW(null);
        _className = Marshal.StringToHGlobalUni(className);

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

        Handle = User32.CreateWindowExW(User32.WS_EX_TOOLWINDOW, _className, _className, User32.WS_POPUP, 0, 0, 0, 0, 0, 0, _instance, 0);
        if (Handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    public nint Handle { get; private set; }

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

    private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            var result = _handler(msg, wParam, lParam);
            if (result.HasValue)
            {
                return result.Value;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Hidden window handler failed", ex);
        }

        return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
```

`src/Supermonitor.Core/Shell/TrayIcon.cs`:
```csharp
using Supermonitor.Core.Interop;

namespace Supermonitor.Core.Shell;

public sealed unsafe class TrayIcon : IDisposable
{
    private const uint CallbackMessage = User32.WM_APP + 1;
    private const int MenuOpen = 1;
    private const int MenuExit = 2;

    private readonly string _tooltip;
    private readonly Action _onOpen;
    private readonly Action _onExit;
    private readonly uint _taskbarCreated;
    private readonly nint _icon;
    private readonly HiddenWindow _window;

    public TrayIcon(string tooltip, string iconPath, Action onOpen, Action onExit)
    {
        _tooltip = tooltip;
        _onOpen = onOpen;
        _onExit = onExit;
        _taskbarCreated = User32.RegisterWindowMessageW("TaskbarCreated");
        _icon = User32.LoadImageW(0, iconPath, User32.IMAGE_ICON, User32.GetSystemMetrics(User32.SM_CXSMICON), User32.GetSystemMetrics(User32.SM_CYSMICON), User32.LR_LOADFROMFILE);
        _window = new HiddenWindow("Supermonitor.Tray", HandleMessage);
        Add();
    }

    public void Dispose()
    {
        var data = CreateData(0);
        Shell32.Shell_NotifyIconW(Shell32.NIM_DELETE, &data);
        if (_icon != 0)
        {
            User32.DestroyIcon(_icon);
        }

        _window.Dispose();
    }

    private void Add()
    {
        var data = CreateData(Shell32.NIF_MESSAGE | Shell32.NIF_ICON | Shell32.NIF_TIP | Shell32.NIF_SHOWTIP);
        Shell32.Shell_NotifyIconW(Shell32.NIM_ADD, &data);
        data.uVersion = Shell32.NOTIFYICON_VERSION_4;
        Shell32.Shell_NotifyIconW(Shell32.NIM_SETVERSION, &data);
    }

    private Shell32.NOTIFYICONDATAW CreateData(uint flags)
    {
        var data = new Shell32.NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(Shell32.NOTIFYICONDATAW),
            hWnd = _window.Handle,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
        };
        _tooltip.AsSpan(0, Math.Min(_tooltip.Length, 127)).CopyTo(new Span<char>(data.szTip, 128));
        return data;
    }

    private nint? HandleMessage(uint msg, nint wParam, nint lParam)
    {
        if (msg == _taskbarCreated)
        {
            Add();
            return 0;
        }

        if (msg != CallbackMessage)
        {
            return null;
        }

        var notification = (uint)(lParam & 0xFFFF);
        if (notification is Shell32.NIN_SELECT or Shell32.NIN_KEYSELECT or User32.WM_LBUTTONDBLCLK)
        {
            _onOpen();
        }
        else if (notification == User32.WM_CONTEXTMENU)
        {
            ShowMenu();
        }

        return 0;
    }

    private void ShowMenu()
    {
        var menu = User32.CreatePopupMenu();
        User32.AppendMenuW(menu, User32.MF_STRING, MenuOpen, "Open Supermonitor");
        User32.AppendMenuW(menu, User32.MF_SEPARATOR, 0, null);
        User32.AppendMenuW(menu, User32.MF_STRING, MenuExit, "Exit");

        User32.GetCursorPos(out var point);
        User32.SetForegroundWindow(_window.Handle);
        var command = User32.TrackPopupMenuEx(menu, User32.TPM_RETURNCMD | User32.TPM_RIGHTBUTTON | User32.TPM_NONOTIFY, point.X, point.Y, _window.Handle, 0);
        User32.PostMessageW(_window.Handle, User32.WM_NULL, 0, 0);
        User32.DestroyMenu(menu);

        if (command == MenuOpen)
        {
            _onOpen();
        }
        else if (command == MenuExit)
        {
            _onExit();
        }
    }
}
```

`src/Supermonitor.Core/Shell/WindowActivation.cs`:
```csharp
using Supermonitor.Core.Interop;

namespace Supermonitor.Core.Shell;

public static class WindowActivation
{
    public static void ForceForeground(nint hwnd) => User32.SetForegroundWindow(hwnd);
}
```

- [ ] **Step 4: Ícone e scripts**

`tools/make-icon.ps1`:
```powershell
param([string]$Out = (Join-Path $PSScriptRoot '..\src\Supermonitor\Assets\Supermonitor.ico'))

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null

$sizes = 16, 24, 32, 48, 64, 256
$pngs = foreach ($s in $sizes) {
    $bmp = [System.Drawing.Bitmap]::new($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $d = [Math]::Max(4, [int]($s * 0.44)); $w = $s - 1
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($w - $d, 0, $d, $d, 270, 90)
    $path.AddArc($w - $d, $w - $d, $d, $d, 0, 90); $path.AddArc(0, $w - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 15, 23, 42)), $path)

    $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 34, 197, 94), [Math]::Max(1.5, $s * 0.09))
    $pen.LineJoin = 'Round'; $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $pts = @(@(0.14, 0.58), @(0.34, 0.58), @(0.44, 0.30), @(0.56, 0.76), @(0.66, 0.50), @(0.86, 0.50)) |
        ForEach-Object { [System.Drawing.PointF]::new($_[0] * $s, $_[1] * $s) }
    $g.DrawLines($pen, [System.Drawing.PointF[]]$pts)
    $g.Dispose()

    $ms = [IO.MemoryStream]::new()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

$fs = [IO.File]::Create($Out)
$bw = [IO.BinaryWriter]::new($fs)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Dispose()
Write-Host "Icon written to $Out"
```

`tools/run-elevated.ps1`:
```powershell
param([switch]$NoBuild)

$root = Split-Path $PSScriptRoot
$project = Join-Path $root 'src\Supermonitor\Supermonitor.csproj'
if (-not $NoBuild) {
    dotnet build $project -c Debug -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$exe = Get-ChildItem (Join-Path $root 'src\Supermonitor\bin\x64\Debug') -Recurse -Filter Supermonitor.exe | Select-Object -First 1
if (-not $exe) { throw 'Supermonitor.exe not found; build first.' }
Start-Process $exe.FullName -Verb RunAs
```

Run: `pwsh tools/make-icon.ps1`
Expected: `Icon written to …\src\Supermonitor\Assets\Supermonitor.ico`.

- [ ] **Step 5: Projeto WinUI**

`src/Supermonitor/Supermonitor.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <RootNamespace>Supermonitor</RootNamespace>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <ApplicationIcon>Assets\Supermonitor.ico</ApplicationIcon>
    <Platforms>x64</Platforms>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <UseWinUI>true</UseWinUI>
    <EnableMsixTooling>true</EnableMsixTooling>
    <WindowsPackageType>None</WindowsPackageType>
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
    <DefineConstants>$(DefineConstants);DISABLE_XAML_GENERATED_MAIN</DefineConstants>
    <ServerGarbageCollection>false</ServerGarbageCollection>
    <ConcurrentGarbageCollection>false</ConcurrentGarbageCollection>
    <TieredPGO>true</TieredPGO>
    <PublishReadyToRun>true</PublishReadyToRun>
  </PropertyGroup>
  <ItemGroup>
    <Content Include="Assets\Supermonitor.ico" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK.WinUI" Version="2.3.9" />
    <PackageReference Include="Microsoft.WindowsAppSDK.Foundation" Version="2.3.12" />
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.28000.2705" />
    <PackageReference Include="Microsoft.Graphics.Win2D" Version="1.4.0" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Supermonitor.Core\Supermonitor.Core.csproj" />
  </ItemGroup>
</Project>
```

`src/Supermonitor/app.manifest`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="Supermonitor.app" />
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

`src/Supermonitor/Program.cs`:
```csharp
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Supermonitor.Core.Shell;

namespace Supermonitor;

public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var instance = SingleInstance.TryAcquire(SingleInstanceNames.Default);
        if (instance is null)
        {
            SingleInstance.SignalExisting(SingleInstanceNames.Default);
            return;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App(args, instance);
        });
    }
}
```

`src/Supermonitor/App.xaml`:
```xml
<Application
    x:Class="Supermonitor.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

`src/Supermonitor/App.xaml.cs`:
```csharp
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Supermonitor.Core;
using Supermonitor.Core.Diagnostics;
using Supermonitor.Core.Shell;

namespace Supermonitor;

public partial class App : Application
{
    private readonly string[] _args;
    private readonly SingleInstance _instance;
    private DispatcherQueue? _dispatcher;
    private AppHost? _host;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private bool _exiting;

    public App(string[] args, SingleInstance instance)
    {
        _args = args;
        _instance = instance;
        InitializeComponent();
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += (_, e) => Log.Error("Unhandled UI exception", e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _host = AppHost.Create();
        _host.Start();

        _tray = new TrayIcon(
            "Supermonitor",
            AppPaths.IconPath,
            onOpen: () => _dispatcher.TryEnqueue(ShowDashboard),
            onExit: () => _dispatcher.TryEnqueue(ExitApp));
        _instance.ListenForActivation(() => _dispatcher.TryEnqueue(ShowDashboard));

        var minimized = _args.Contains("--minimized", StringComparer.OrdinalIgnoreCase) || _host.Settings.Current.StartMinimized;
        if (minimized)
        {
            ProcessEfficiency.SetEfficiencyMode(true);
        }
        else
        {
            ShowDashboard();
        }
    }

    private void ShowDashboard()
    {
        if (_exiting || _host is null)
        {
            return;
        }

        if (_window is null)
        {
            _window = new MainWindow(_host);
            _window.Closed += OnWindowClosed;
        }

        ProcessEfficiency.SetEfficiencyMode(false);
        _window.BringToFront();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _window = null;
        ProcessEfficiency.SetEfficiencyMode(true);
    }

    private async void ExitApp()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _tray?.Dispose();
        _window?.Close();
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }

        Exit();
    }
}
```

`src/Supermonitor/ViewModels/PageContext.cs`:
```csharp
using Supermonitor.Core;

namespace Supermonitor.ViewModels;

public sealed record PageContext(AppHost Host, Func<bool> IsMinimized);
```

`src/Supermonitor/MainWindow.xaml`:
```xml
<Window
    x:Class="Supermonitor.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="Supermonitor">
    <Window.SystemBackdrop>
        <MicaBackdrop />
    </Window.SystemBackdrop>
    <Grid>
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
        <InfoBar
            x:Name="SettingsInfoBar"
            Grid.Row="1"
            Severity="Warning"
            Title="Settings were reset"
            Message="The settings file was unreadable and was saved as settings.json.bak. Default settings are in use."
            IsOpen="False" />
        <NavigationView
            x:Name="Nav"
            Grid.Row="2"
            IsBackButtonVisible="Collapsed"
            PaneDisplayMode="Left"
            IsSettingsVisible="True"
            SelectionChanged="OnNavSelectionChanged">
            <NavigationView.MenuItems>
                <NavigationViewItem Content="Overview" Tag="overview" Icon="Home" />
            </NavigationView.MenuItems>
            <Frame x:Name="ContentFrame" />
        </NavigationView>
    </Grid>
</Window>
```

`src/Supermonitor/MainWindow.xaml.cs` (nesta task a navegação ainda não tem páginas; as Tasks 15 e 16 preenchem `Navigate`):
```csharp
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Supermonitor.Core;
using Supermonitor.Core.Settings;
using Supermonitor.Core.Shell;
using Supermonitor.ViewModels;
using WinRT.Interop;

namespace Supermonitor;

public sealed partial class MainWindow : Window
{
    private readonly AppHost _host;
    private bool _lastMinimized;

    public MainWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        AppWindow.SetIcon(AppPaths.IconPath);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));
        AppWindow.Changed += OnAppWindowChanged;

        SettingsInfoBar.IsOpen = host.SettingsLoadResult == SettingsLoadResult.RecoveredFromCorrupt;
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    public bool IsMinimized => AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized };

    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
        WindowActivation.ForceForeground(WindowNative.GetWindowHandle(this));
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        var minimized = IsMinimized;
        if (minimized != _lastMinimized)
        {
            _lastMinimized = minimized;
            ProcessEfficiency.SetEfficiencyMode(minimized);
        }
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var context = new PageContext(_host, () => IsMinimized);
        Navigate(args.IsSettingsSelected ? "settings" : (args.SelectedItem as NavigationViewItem)?.Tag as string, context);
    }

    private void Navigate(string? tag, PageContext context)
    {
        // Páginas adicionadas nas Tasks 15 (overview) e 16 (settings).
    }
}
```

Adicionar o projeto à solução:
```powershell
dotnet sln Supermonitor.slnx add src/Supermonitor/Supermonitor.csproj
```

- [ ] **Step 6: Build**

Run: `dotnet build src/Supermonitor/Supermonitor.csproj -c Debug -p:Platform=x64`
Expected: `Build succeeded`.

Se o restore/build falhar com erro sobre targets ou runtime do Windows App SDK nos pacotes de componente, troque as duas referências `Microsoft.WindowsAppSDK.WinUI` e `Microsoft.WindowsAppSDK.Foundation` por `<PackageReference Include="Microsoft.WindowsAppSDK" Version="2.5.1" />` e rode o build de novo. Registre a troca na mensagem de commit.

- [ ] **Step 7: Rodar e verificar manualmente**

Run: `pwsh tools/run-elevated.ps1 -NoBuild` (o usuário aprova o UAC).
Expected:
1. Janela "Supermonitor" com Mica, barra de título própria e NavigationView com "Overview" e "Settings" (conteúdo vazio).
2. Ícone na bandeja. Clique esquerdo traz a janela. Clique direito mostra "Open Supermonitor" / "Exit".
3. Fechar a janela no X: o processo continua (Gerenciador de Tarefas mostra Supermonitor com folha de "Modo de eficiência").
4. Rodar `pwsh tools/run-elevated.ps1 -NoBuild` de novo: a janela existente volta; nenhum segundo processo.
5. "Exit" na bandeja encerra o processo.
6. `%LocalAppData%\Supermonitor\logs\supermonitor-AAAAMMDD.log` contém "Supermonitor starting" e "Supermonitor stopped".

- [ ] **Step 8: Commit**

```powershell
git add src tools tests Supermonitor.slnx
git commit -m "feat(app): add WinUI 3 shell with tray icon, single instance and efficiency mode"
```

---

### Task 15: Dashboard Overview

**Files:**
- Create: `src/Supermonitor/Controls/ISeriesSource.cs`, `SparklineChart.cs`, `MetricCard.xaml`, `MetricCard.xaml.cs`
- Create: `src/Supermonitor/ViewModels/MetricCardViewModel.cs`, `OverviewViewModel.cs`
- Create: `src/Supermonitor/Views/OverviewPage.xaml`, `OverviewPage.xaml.cs`
- Modify: `src/Supermonitor/MainWindow.xaml.cs` (método `Navigate`)

**Interfaces:**
- Consumes: `AppHost.Metrics`, `AppHost.Hub.SampleReady`, `AppHost.Settings.Current.OverviewCards` e `SampleIntervalMs`, `MetricStore.TryGetDescriptor/GetLatest/CopyHistory`, `UnitFormatter`, `PageContext`.
- Produces: `interface ISeriesSource { int PointCount { get; } float? FixedMaximum { get; } int CopyHistory(Span<float> destination); event Action? Refreshed; }`.
- Produces: `MetricCardViewModel(string metricId, MetricStore store)` com `MetricId`, `Title`, `ValueText`, `PointCount`, `FixedMaximum`, `void Refresh(UnitOptions options)`.
- Produces: `OverviewViewModel(AppHost host, DispatcherQueue dispatcher, Func<bool> isMinimized)` com `ObservableCollection<MetricCardViewModel> Cards`, `Dispose()`. Atualiza no máximo uma vez por tick, só com a janela não minimizada.

- [ ] **Step 1: Controles**

`src/Supermonitor/Controls/ISeriesSource.cs`:
```csharp
namespace Supermonitor.Controls;

public interface ISeriesSource
{
    int PointCount { get; }

    float? FixedMaximum { get; }

    event Action? Refreshed;

    int CopyHistory(Span<float> destination);
}
```

`src/Supermonitor/Controls/SparklineChart.cs`:
```csharp
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace Supermonitor.Controls;

public sealed partial class SparklineChart : UserControl
{
    private CanvasControl? _canvas;
    private ISeriesSource? _source;
    private float[] _buffer = new float[120];
    private Color _stroke;

    public SparklineChart()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
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
        _stroke = (Color)Application.Current.Resources["SystemAccentColor"];
        _canvas = new CanvasControl();
        _canvas.Draw += OnDraw;
        Content = _canvas;
        if (_source is not null)
        {
            _source.Refreshed += Invalidate;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_source is not null)
        {
            _source.Refreshed -= Invalidate;
        }

        if (_canvas is null)
        {
            return;
        }

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

        var count = Math.Max(2, _source.PointCount);
        if (_buffer.Length < count)
        {
            _buffer = new float[count];
        }

        var points = _buffer.AsSpan(0, count);
        var n = _source.CopyHistory(points);
        if (n < 2)
        {
            return;
        }

        var max = _source.FixedMaximum ?? MaxFinite(points[..n]) * 1.1f;
        if (!(max > 0))
        {
            max = 1;
        }

        var width = (float)sender.ActualWidth;
        var height = (float)sender.ActualHeight;
        var step = width / (count - 1);
        var offset = count - n;
        for (var i = 1; i < n; i++)
        {
            float a = points[i - 1], b = points[i];
            if (!float.IsFinite(a) || !float.IsFinite(b))
            {
                continue;
            }

            var x0 = (offset + i - 1) * step;
            var x1 = (offset + i) * step;
            var y0 = height - Math.Clamp(a / max, 0, 1) * height;
            var y1 = height - Math.Clamp(b / max, 0, 1) * height;
            args.DrawingSession.DrawLine(x0, y0, x1, y1, _stroke, 1.5f);
        }
    }

    private static float MaxFinite(ReadOnlySpan<float> values)
    {
        var max = 0f;
        foreach (var value in values)
        {
            if (float.IsFinite(value) && value > max)
            {
                max = value;
            }
        }

        return max;
    }
}
```

`src/Supermonitor/Controls/MetricCard.xaml`:
```xml
<UserControl
    x:Class="Supermonitor.Controls.MetricCard"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:Supermonitor.Controls">
    <Border
        Padding="16"
        CornerRadius="8"
        BorderThickness="1"
        Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
        BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}">
        <Grid RowSpacing="4">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" />
            </Grid.RowDefinitions>
            <TextBlock
                Text="{x:Bind ViewModel.Title, Mode=OneWay}"
                Style="{StaticResource BodyStrongTextBlockStyle}"
                Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                TextTrimming="CharacterEllipsis" />
            <TextBlock
                Grid.Row="1"
                Text="{x:Bind ViewModel.ValueText, Mode=OneWay}"
                Style="{StaticResource SubtitleTextBlockStyle}" />
            <local:SparklineChart x:Name="Chart" Grid.Row="2" MinHeight="60" />
        </Grid>
    </Border>
</UserControl>
```

`src/Supermonitor/Controls/MetricCard.xaml.cs`:
```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Supermonitor.ViewModels;

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
    }

    public MetricCardViewModel? ViewModel
    {
        get => (MetricCardViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var card = (MetricCard)d;
        card.Chart.Source = e.NewValue as MetricCardViewModel;
    }
}
```

- [ ] **Step 2: ViewModels**

`src/Supermonitor/ViewModels/MetricCardViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Supermonitor.Controls;
using Supermonitor.Core.Metrics;

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
    }

    public event Action? Refreshed;

    public string MetricId { get; }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string ValueText { get; set; }

    public int PointCount { get; set; } = 60;

    public float? FixedMaximum { get; private set; }

    public int CopyHistory(Span<float> destination) => _store.CopyHistory(MetricId, destination);

    public void Refresh(UnitOptions options)
    {
        if (_store.TryGetDescriptor(MetricId, out var descriptor))
        {
            Title = descriptor.DisplayName;
            FixedMaximum = descriptor.Unit == MetricUnit.Percent ? 100f : null;
            ValueText = UnitFormatter.Format(_store.GetLatest(MetricId), descriptor.Unit, options);
        }
        else
        {
            ValueText = "N/A";
        }

        Refreshed?.Invoke();
    }
}
```

`src/Supermonitor/ViewModels/OverviewViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Supermonitor.Core;
using Supermonitor.Core.Metrics;

namespace Supermonitor.ViewModels;

public sealed class OverviewViewModel : IDisposable
{
    private readonly AppHost _host;
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<bool> _isMinimized;
    private int _refreshQueued;

    public OverviewViewModel(AppHost host, DispatcherQueue dispatcher, Func<bool> isMinimized)
    {
        _host = host;
        _dispatcher = dispatcher;
        _isMinimized = isMinimized;
        foreach (var id in host.Settings.Current.OverviewCards)
        {
            Cards.Add(new MetricCardViewModel(id, host.Metrics));
        }

        RefreshNow();
        host.Hub.SampleReady += OnSampleReady;
        host.Metrics.MetricsChanged += OnSampleReady;
    }

    public ObservableCollection<MetricCardViewModel> Cards { get; } = [];

    public void Dispose()
    {
        _host.Hub.SampleReady -= OnSampleReady;
        _host.Metrics.MetricsChanged -= OnSampleReady;
    }

    private void OnSampleReady()
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
        {
            return;
        }

        if (!_dispatcher.TryEnqueue(DispatcherQueuePriority.Low, RefreshNow))
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

        var points = Math.Max(2, 60_000 / _host.Settings.Current.SampleIntervalMs);
        foreach (var card in Cards)
        {
            card.PointCount = points;
            card.Refresh(UnitOptions.Default);
        }
    }
}
```

- [ ] **Step 3: Página e navegação**

`src/Supermonitor/Views/OverviewPage.xaml`:
```xml
<Page
    x:Class="Supermonitor.Views.OverviewPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:controls="using:Supermonitor.Controls"
    xmlns:vm="using:Supermonitor.ViewModels">
    <ScrollViewer Padding="24">
        <StackPanel Spacing="16">
            <TextBlock Text="Overview" Style="{StaticResource TitleTextBlockStyle}" />
            <ItemsRepeater ItemsSource="{x:Bind ViewModel.Cards, Mode=OneWay}">
                <ItemsRepeater.Layout>
                    <UniformGridLayout
                        MinItemWidth="280"
                        MinItemHeight="160"
                        MinColumnSpacing="12"
                        MinRowSpacing="12"
                        ItemsStretch="Fill" />
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

`src/Supermonitor/Views/OverviewPage.xaml.cs`:
```csharp
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
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
        Bindings.Update();
    }
}
```

Em `src/Supermonitor/MainWindow.xaml.cs`, substituir o método `Navigate` e adicionar `using Supermonitor.Views;`:
```csharp
    private void Navigate(string? tag, PageContext context)
    {
        switch (tag)
        {
            case "overview":
                ContentFrame.Navigate(typeof(OverviewPage), context);
                break;
        }
    }
```

- [ ] **Step 4: Build e verificação manual**

Run: `pwsh tools/run-elevated.ps1`
Expected:
1. Overview mostra 8 cards: CPU usage, Memory usage, GPU usage, GPU VRAM used, Disk 0 (…) active time, Download, Upload, Ping.
2. Valores mudam a cada 1 s e o gráfico de linha cresce da direita para a esquerda.
3. Com o Gerenciador de Tarefas aberto ao lado: CPU, memória, GPU 3D, disco e rede batem dentro de ±2 pontos percentuais ou ±5%.
4. Minimizar a janela: valores param de atualizar; restaurar volta a atualizar.
5. Nenhum card fica travado em "N/A" exceto se o recurso não existir na máquina.

- [ ] **Step 5: Commit**

```powershell
git add src/Supermonitor
git commit -m "feat(app): add overview dashboard with live metric cards and sparklines"
```

---

### Task 16: Página Settings

**Files:**
- Create: `src/Supermonitor/Views/SettingsPage.xaml`, `SettingsPage.xaml.cs`
- Modify: `src/Supermonitor/MainWindow.xaml.cs` (método `Navigate`)

**Interfaces:**
- Consumes: `AppSettings.AllowedIntervalsMs`, `SettingsStore.RequestSave`, `SensorHub.Interval`, `AutostartManager.IsEnabled/SetEnabled`, `Log`.
- Produces: página de configurações com intervalo, host de ping, iniciar com o Windows e iniciar minimizado.

- [ ] **Step 1: Página**

`src/Supermonitor/Views/SettingsPage.xaml`:
```xml
<Page
    x:Class="Supermonitor.Views.SettingsPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <ScrollViewer Padding="24">
        <StackPanel Spacing="16" MaxWidth="720" HorizontalAlignment="Left">
            <TextBlock Text="Settings" Style="{StaticResource TitleTextBlockStyle}" />
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
            <ToggleSwitch x:Name="StartWithWindowsSwitch" Header="Start with Windows" Toggled="OnStartWithWindowsToggled" />
            <TextBlock x:Name="AutostartError" Visibility="Collapsed" Foreground="{ThemeResource SystemFillColorCriticalBrush}" TextWrapping="Wrap" />
            <ToggleSwitch x:Name="StartMinimizedSwitch" Header="Start minimized to the system tray" Toggled="OnStartMinimizedToggled" />
        </StackPanel>
    </ScrollViewer>
</Page>
```

`src/Supermonitor/Views/SettingsPage.xaml.cs`:
```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Supermonitor.Core;
using Supermonitor.Core.Diagnostics;
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

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        _host = ((PageContext)e.Parameter).Host;
        var settings = _host.Settings.Current;

        _loading = true;
        IntervalBox.SelectedIndex = Math.Max(0, AppSettings.AllowedIntervalsMs.ToList().IndexOf(settings.SampleIntervalMs));
        PingHostBox.Text = settings.PingHost;
        StartMinimizedSwitch.IsOn = settings.StartMinimized;
        StartWithWindowsSwitch.IsOn = await Task.Run(AutostartManager.IsEnabled);
        _loading = false;
    }

    private void OnIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _host is null || IntervalBox.SelectedIndex < 0)
        {
            return;
        }

        var milliseconds = AppSettings.AllowedIntervalsMs[IntervalBox.SelectedIndex];
        _host.Settings.Current.SampleIntervalMs = milliseconds;
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
            PingHostBox.Text = _host.Settings.Current.PingHost;
            return;
        }

        PingHostError.Visibility = Visibility.Collapsed;
        _host.Settings.Current.PingHost = host;
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

        _host.Settings.Current.StartMinimized = StartMinimizedSwitch.IsOn;
        _host.Settings.RequestSave();
    }
}
```

Em `MainWindow.xaml.cs`, adicionar o caso ao `switch` de `Navigate`:
```csharp
            case "settings":
                ContentFrame.Navigate(typeof(SettingsPage), context);
                break;
```

- [ ] **Step 2: Build e verificação manual**

Run: `pwsh tools/run-elevated.ps1`
Expected:
1. Settings mostra "1 second", ping `1.1.1.1`, toggles desligados.
2. Mudar para "250 ms": os cards do Overview passam a atualizar 4×/s; `settings.json` tem `"sampleIntervalMs": 250` depois de ~0,5 s.
3. Ping host `abc def`: mensagem de erro e o valor volta ao anterior. `8.8.8.8`: aceito e salvo.
4. "Start with Windows" ligado: `schtasks /Query /TN Supermonitor` lista a tarefa; desligado: a tarefa some.
5. Fechar pelo tray "Exit", reabrir: configurações persistem.

- [ ] **Step 3: Commit**

```powershell
git add src/Supermonitor
git commit -m "feat(app): add settings page for interval, ping host and startup"
```

---

### Task 17: Verificação de desempenho e fechamento

**Files:**
- Create: `tools/measure-cpu.ps1`
- Create: `docs/superpowers/plans/2026-10-10-supermonitor-01-results.md`

**Interfaces:**
- Consumes: app completo.
- Produces: registro dos critérios de sucesso 1–3 da spec.

- [ ] **Step 1: Script de medição**

`tools/measure-cpu.ps1`:
```powershell
param([int]$Seconds = 60)

$process = Get-Process Supermonitor -ErrorAction Stop | Select-Object -First 1
$start = $process.TotalProcessorTime
Start-Sleep -Seconds $Seconds
$process.Refresh()
$used = ($process.TotalProcessorTime - $start).TotalMilliseconds
$percent = $used / ($Seconds * 1000 * [Environment]::ProcessorCount) * 100
'CPU: {0:N2}% | Working set: {1:N0} MB | Private: {2:N0} MB' -f $percent, ($process.WorkingSet64 / 1MB), ($process.PrivateMemorySize64 / 1MB)
```
Se `TotalProcessorTime` der acesso negado (processo elevado), rode o script em um PowerShell elevado.

- [ ] **Step 2: Medir**

1. Build Release: `dotnet build src/Supermonitor/Supermonitor.csproj -c Release -p:Platform=x64`; executar o exe de `bin\x64\Release\…\win-x64\` como administrador.
2. Dashboard aberto no Overview, intervalo 1 s: `pwsh tools/measure-cpu.ps1`. Critério: CPU < 1%.
3. Janela fechada (só bandeja): `pwsh tools/measure-cpu.ps1`. Critério: CPU < 0,3%.
4. Rodar todos os testes: `dotnet test tests/Supermonitor.Tests` (unidade + hardware). Critério: todos passam.

Se um critério falhar, aplique superpowers:systematic-debugging antes de mudar código. Suspeito principal: o contador `\GPU Engine(*)` tem centenas de instâncias. Meça o tempo de `GpuProvider.Sample` com `Stopwatch` e registre no log antes de otimizar.

- [ ] **Step 3: Registrar resultados**

`docs/superpowers/plans/2026-10-10-supermonitor-01-results.md` com: data, build (Release), CPU e memória medidos nos dois cenários, resultado dos testes, divergências vistas contra o Gerenciador de Tarefas e qualquer desvio do plano (ex.: troca para o metapacote do Windows App SDK).

- [ ] **Step 4: Commit**

```powershell
git add tools/measure-cpu.ps1 docs/superpowers/plans/2026-10-10-supermonitor-01-results.md
git commit -m "docs: record foundation performance results"
```
