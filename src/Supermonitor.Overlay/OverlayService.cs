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
