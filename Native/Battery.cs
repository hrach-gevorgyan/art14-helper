using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Art14Helper.Native;

/// <summary>How much the battery holds now against when it was new, and how often it was cycled.</summary>
internal readonly record struct BatteryHealth(int DesignedMilliwattHours, int FullMilliwattHours, int? Cycles)
{
    /// <summary>Full charge as a share of the design capacity; can be a little over 100% when new.</summary>
    internal int Percent => (int)Math.Round(100.0 * FullMilliwattHours / DesignedMilliwattHours);

    /// <summary>As shown: a new battery a little above its rating reads "100%+"; the exact numbers go in the details.</summary>
    internal string Shown => Percent > 100 ? "100%+" : $"{Percent}%";
}

/// <summary>What the battery holds now and how fast it charges (+) or drains (-), in mWh and mW.</summary>
internal readonly record struct BatteryCharge(int RemainingMilliwattHours, int FullMilliwattHours, int RateMilliwatts)
{
    /// <summary>
    /// Time until the battery reaches <paramref name="targetPercent"/> of a full charge at the
    /// current rate; null when it isn't charging or is already there. Charging slows near the
    /// top, so this is an estimate.
    /// </summary>
    internal TimeSpan? TimeTo(int targetPercent)
    {
        var missing = FullMilliwattHours * targetPercent / 100.0 - RemainingMilliwattHours;
        return RateMilliwatts > 0 && missing > 0 ? TimeSpan.FromHours(missing / RateMilliwatts) : null;
    }
}

/// <summary>
/// The battery's capacity data straight from its driver (IOCTL_BATTERY_QUERY_*), as
/// Windows' own battery report reads it; no admin rights needed.
/// </summary>
internal static unsafe partial class Battery
{
    private static readonly Guid BatteryInterface = new("72631E54-78A4-11D0-BCF7-00AA00B7B32A");   // GUID_DEVICE_BATTERY
    private const uint QueryTag = 0x294040, QueryInformation = 0x294044, QueryStatus = 0x29404C;   // IOCTL_BATTERY_*
    private const uint Read = 0x80000000;                                                         // GENERIC_READ
    private const uint Unknown = uint.MaxValue;                                                    // BATTERY_UNKNOWN_*

    [StructLayout(LayoutKind.Sequential)]
    private struct Query
    {
        public uint Tag;
        public int Level;    // BatteryInformation = 0
        public int AtRate;
    }

    /// <summary>BATTERY_INFORMATION</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Information
    {
        public uint Capabilities;
        public byte Technology, Reserved1, Reserved2, Reserved3;
        public uint Chemistry;
        public uint DesignedCapacity, FullChargedCapacity, DefaultAlert1, DefaultAlert2, CriticalBias, CycleCount;
    }

    /// <summary>BATTERY_WAIT_STATUS: no waiting, any state.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WaitStatus
    {
        public uint Tag, Timeout, PowerState, LowCapacity, HighCapacity;
    }

    /// <summary>BATTERY_STATUS</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Status
    {
        public uint PowerState, Capacity, Voltage;
        public int Rate;
    }

    /// <summary>The first battery's health; null without a battery or when the driver doesn't say.</summary>
    internal static BatteryHealth? ReadHealth() => First<BatteryHealth>(static (device, tag) =>
    {
        if (ReadInformation(device, tag) is not { } info)
            return null;
        return new BatteryHealth((int)info.DesignedCapacity, (int)info.FullChargedCapacity,
            info.CycleCount is 0 or Unknown ? null : (int)info.CycleCount);
    });

    /// <summary>The first battery's charge and rate; null without a battery or when the driver doesn't say.</summary>
    internal static BatteryCharge? ReadCharge() => First<BatteryCharge>(static (device, tag) =>
    {
        if (ReadInformation(device, tag) is not { } info)
            return null;
        var wait = new WaitStatus { Tag = tag };
        Status status;
        uint returned;
        if (DeviceIoControl(device, QueryStatus, &wait, (uint)sizeof(WaitStatus), &status, (uint)sizeof(Status), &returned, IntPtr.Zero) == 0
            || status.Capacity == Unknown)
            return null;
        const int UnknownRate = unchecked((int)0x80000000);   // BATTERY_UNKNOWN_RATE
        return new BatteryCharge((int)status.Capacity, (int)info.FullChargedCapacity, status.Rate == UnknownRate ? 0 : status.Rate);
    });

    private static Information? ReadInformation(SafeFileHandle device, uint tag)
    {
        var query = new Query { Tag = tag };
        Information info;
        uint returned;
        if (DeviceIoControl(device, QueryInformation, &query, (uint)sizeof(Query), &info, (uint)sizeof(Information), &returned, IntPtr.Zero) == 0)
            return null;
        // 0 and the "unknown" value mean the driver doesn't report it.
        return info.DesignedCapacity is 0 or Unknown || info.FullChargedCapacity is 0 or Unknown ? null : info;
    }

    /// <summary>Runs <paramref name="read"/> on each battery until one answers.</summary>
    private static T? First<T>(Func<SafeFileHandle, uint, T?> read) where T : struct
    {
        try
        {
            foreach (var path in Hid.InterfacePaths(BatteryInterface))
            {
                using var device = CreateFileW(path, Read, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);   // share read+write, OPEN_EXISTING
                if (device.IsInvalid)
                    continue;
                uint wait = 0, tag, returned;
                if (DeviceIoControl(device, QueryTag, &wait, sizeof(uint), &tag, sizeof(uint), &returned, IntPtr.Zero) == 0 || tag == 0)
                    continue;
                if (read(device, tag) is { } result)
                    return result;
            }
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            Log.Error("Battery details unreadable", exception);
        }
        return null;
    }

    [LibraryImport(Libraries.Kernel32, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    private static partial int DeviceIoControl(SafeFileHandle device, uint code, void* input, uint inputSize, void* output, uint outputSize, uint* returned, IntPtr overlapped);
}
