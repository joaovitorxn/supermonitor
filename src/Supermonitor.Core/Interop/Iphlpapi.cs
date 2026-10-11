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
