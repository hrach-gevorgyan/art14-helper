using Win32Exception = System.ComponentModel.Win32Exception;

namespace Art14Helper;

/// <summary>Settings the touchpad firmware accepts; each value is the report's subcommand byte.</summary>
internal enum TouchpadCommand : byte
{
    PressSensitivity = 0x01,   // 0 less, 1 more sensitive
    Vibration = 0x02,          // 0 soft, 1 medium, 2 strong
    BrightnessEdge = 0x07,     // left-edge swipe reports on/off
    VolumeEdge = 0x08,         // right-edge swipe reports on/off
    KnuckleScreenshot = 0x0B   // knuckle double-tap reports on/off
}

/// <summary>What the firmware reports in an input report; the value is the subcommand byte.</summary>
internal enum TouchpadEvent : byte
{
    BrightnessSwipe = 0x03,    // value 1 up, 2 down
    VolumeSwipe = 0x04,        // value 1 up, 2 down
    KnuckleDoubleTap = 0x06
}

/// <summary>
/// The HONOR touchpad's vendor channel (a Goodix force pad). Settings go out and gestures
/// come in as short reports on the vendor HID collection: report id 0x0E, a subcommand,
/// a value, then zero padding to the collection's report length (9 bytes).
/// </summary>
internal static class TouchpadLink
{
    internal const byte ReportId = 0x0E;
    private const ushort VendorPage = 0xFF00;

    /// <summary>Vendor and product ids of touchpads that use this protocol.</summary>
    internal static readonly (ushort Vendor, ushort Product)[] Models = [(0x27C6, 0x0F9A), (0x35CC, 0x0104)];

    // A HID search touches all input devices and takes tens of milliseconds, yet the menu
    // needs the touchpad on each opening. So the control interface found once is kept
    // until a plug/unplug or a write that fails.
    private static readonly Lock CacheLock = new();
    private static HidInterface? _control;
    private static bool _searched;

    internal static bool IsModel(HidInterface device) => Models.Contains((device.VendorId, device.ProductId));

    /// <summary>The collection settings are written to: vendor page, usage 1, room for a 3-byte report.</summary>
    internal static bool IsControl(HidInterface device)
        => IsModel(device) && device.UsagePage == VendorPage && device.Usage == 0x01 && device.OutputReportLength >= 3;

    /// <summary>Collections that deliver gesture reports: any vendor-defined page of a known model.</summary>
    internal static bool IsEventSource(HidInterface device) => IsModel(device) && device.UsagePage >= VendorPage;

    /// <summary>Whether a device path from a plug/unplug notification belongs to a known touchpad vendor.</summary>
    internal static bool IsTouchpadPath(string devicePath)
        => Models.Any(model => devicePath.Contains($"vid_{model.Vendor:x4}", StringComparison.OrdinalIgnoreCase));

    /// <summary>An output report: id, subcommand, value, zeros up to <paramref name="length"/>.</summary>
    internal static byte[] BuildReport(TouchpadCommand command, byte value, int length)
    {
        var report = new byte[Math.Max(length, 3)];
        report[0] = ReportId;
        report[1] = (byte)command;
        report[2] = value;
        return report;
    }

    internal static bool IsPresent => Control() is not null;

    /// <summary>Drops the remembered device, after a plug/unplug.</summary>
    internal static void Forget()
    {
        lock (CacheLock)
        {
            _control = null;
            _searched = false;
        }
    }

    /// <summary>
    /// Writes one setting. A remembered device path dies when the touchpad reconnects, so a
    /// failed write searches again once before giving up. Throws when it can't be written.
    /// </summary>
    internal static void Send(TouchpadCommand command, byte value)
    {
        for (var attempt = 1; ; attempt++)
        {
            var device = Control() ?? throw new InvalidOperationException("There is no HONOR touchpad on this laptop.");
            var failure = Write(device, BuildReport(command, value, device.OutputReportLength));
            if (failure is null)
                return;
            Forget();
            if (attempt == 2)
                throw failure;
        }
    }

    /// <summary>Null when the report went through, else the Windows error that stopped it.</summary>
    private static Win32Exception? Write(HidInterface device, byte[] report)
    {
        using var handle = device.OpenForWriting();
        // The HID stack accepts only reports exactly as long as the collection's output report.
        var problem = handle.IsInvalid ? "The touchpad couldn't be opened for a setting change."
            : !Hid.WriteReport(handle, report) ? "The touchpad didn't accept the setting."
            : null;
        return problem is null ? null : new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), problem);
    }

    private static HidInterface? Control()
    {
        lock (CacheLock)
        {
            if (_searched)
                return _control;
            try
            {
                _control = Hid.ListPresent().FirstOrDefault(IsControl);
            }
            catch (Exception exception)
            {
                Log.Error("HID devices couldn't be listed while looking for the touchpad", exception);
                _control = null;
            }
            _searched = true;
            return _control;
        }
    }
}
