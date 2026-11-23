using System.Runtime.InteropServices;

namespace Art14Helper.Native;

/// <summary>
/// Windows 11's efficiency mode (EcoQoS) for the whole process: its threads run on the
/// efficient cores at low clock speed, as Task Manager's green leaf shows. Right for a
/// tray app that mostly waits; turned off while a window is open so it stays responsive.
/// Older Windows ignores the request.
/// </summary>
internal static unsafe partial class Efficiency
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Throttling
    {
        public uint Version, ControlMask, StateMask;
    }

    /// <summary>False when this Windows doesn't support it.</summary>
    internal static bool Set(bool on)
    {
        const uint ExecutionSpeed = 0x1;   // PROCESS_POWER_THROTTLING_EXECUTION_SPEED
        var state = new Throttling { Version = 1, ControlMask = ExecutionSpeed, StateMask = on ? ExecutionSpeed : 0 };
        return SetProcessInformation(GetCurrentProcess(), 4, &state, (uint)sizeof(Throttling)) != 0;   // ProcessPowerThrottling
    }

    [LibraryImport(Libraries.Kernel32)]
    private static partial IntPtr GetCurrentProcess();

    [LibraryImport(Libraries.Kernel32)]
    private static partial int SetProcessInformation(IntPtr process, int infoClass, void* info, uint size);
}
