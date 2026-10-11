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
    private readonly bool[] _matched;
    private readonly Dictionary<int, double> _processes = [];

    public GpuEngineAggregator(IReadOnlyList<string> adapterLuids)
    {
        _luids = adapterLuids.ToArray();
        _sums = new double[_luids.Length * MaxEngines];
        _kinds = new GpuEngineKind[_luids.Length * MaxEngines];
        _matched = new bool[_luids.Length];
    }

    public void Reset()
    {
        Array.Clear(_sums);
        Array.Clear(_matched);
        _processes.Clear();
    }

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
        _matched[adapter] = true;
        if (TryParsePid(instanceName, out var pid))
        {
            _processes[pid] = Math.Max(_processes.GetValueOrDefault(pid), value);
        }
    }

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

    public GpuUsage GetUsage(int adapter)
    {
        if (!_matched[adapter])
        {
            // No instance for this adapter (e.g. LUID changed after a driver update).
            return new GpuUsage(float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN);
        }

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
