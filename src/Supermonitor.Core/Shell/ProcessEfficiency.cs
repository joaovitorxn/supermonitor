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
