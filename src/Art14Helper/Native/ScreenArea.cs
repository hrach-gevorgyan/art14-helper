using System.Runtime.InteropServices;

namespace Art14Helper.Native;

/// <summary>The desktop as one rectangle across all monitors, in real pixels.</summary>
internal static partial class ScreenArea
{
    /// <summary>
    /// The bounds of all monitors together. Call on a thread that has entered
    /// <see cref="UseRealPixels"/>, or Windows reports scaled sizes.
    /// </summary>
    internal static Rectangle AllMonitors()
        => new(GetSystemMetrics(VirtualLeft), GetSystemMetrics(VirtualTop), GetSystemMetrics(VirtualWidth), GetSystemMetrics(VirtualHeight));

    /// <summary>Makes the calling thread per-monitor DPI aware (v2), so coordinates are physical pixels.</summary>
    internal static void UseRealPixels() => SetThreadDpiAwarenessContext(PerMonitorAwareV2);

    private const int VirtualLeft = 76, VirtualTop = 77, VirtualWidth = 78, VirtualHeight = 79;   // SM_*VIRTUALSCREEN
    private static readonly IntPtr PerMonitorAwareV2 = -4;   // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2

    [LibraryImport(Libraries.User32)]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport(Libraries.User32)]
    private static partial IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
