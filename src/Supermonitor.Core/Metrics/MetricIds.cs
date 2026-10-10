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
