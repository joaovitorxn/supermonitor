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
