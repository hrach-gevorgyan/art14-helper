using System.Runtime.InteropServices;

namespace Art14Helper.Native;

/// <summary>Battery level and time left, as Windows reports them; null where it doesn't know.</summary>
internal readonly record struct BatteryStatus(bool PluggedIn, bool Charging, int? Percent, TimeSpan? TimeLeft);

/// <summary>Charger and battery state.</summary>
internal static partial class PowerSource
{
    /// <summary>True on AC power; also true when Windows can't tell (desktop, unknown state).</summary>
    internal static bool IsPluggedIn => GetSystemPowerStatus(out var status) == 0 || status.AcLineStatus != 0;

    internal static BatteryStatus Battery
    {
        get
        {
            if (GetSystemPowerStatus(out var status) == 0)
                return new BatteryStatus(true, false, null, null);
            const byte Unknown = 255, Charging = 8;
            return new BatteryStatus(
                status.AcLineStatus == 1,
                status.BatteryFlag != Unknown && (status.BatteryFlag & Charging) != 0,
                status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : null,
                status.BatteryLifeTime is > 0 and not uint.MaxValue ? TimeSpan.FromSeconds(status.BatteryLifeTime) : null);
        }
    }

    /// <summary>SYSTEM_POWER_STATUS: AC line 0 off, 1 on, 255 unknown.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Status
    {
        public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }

    [LibraryImport(Libraries.Kernel32)]
    private static partial int GetSystemPowerStatus(out Status status);
}
