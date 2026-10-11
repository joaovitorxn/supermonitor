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
