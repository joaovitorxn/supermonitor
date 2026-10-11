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
