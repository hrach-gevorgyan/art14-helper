using System.Runtime.InteropServices;

namespace Art14Helper.Native;

/// <summary>
/// Window-message notifications from Windows: the display turning on (after sleep or
/// a screen-off timeout) and HID devices arriving or leaving. Subscribing returns a
/// handle; disposing it unsubscribes.
/// </summary>
internal static unsafe partial class SystemNotifications
{
    private const int WmPowerBroadcast = 0x0218;
    private const int PbtPowerSettingChange = 0x8013;
    private const int WmDeviceChange = 0x0219;
    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDeviceRemoveComplete = 0x8004;
    private const int DbtDevTypDeviceInterface = 5;
    private const uint ToWindowHandle = 0; // DEVICE_NOTIFY_WINDOW_HANDLE

    /// <summary>GUID_CONSOLE_DISPLAY_STATE: data 0 = off, 1 = on, 2 = dimmed.</summary>
    private static readonly Guid ConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    internal sealed class PowerSubscription : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
    {
        public PowerSubscription() : base(ownsHandle: true) { }
        protected override bool ReleaseHandle() => UnregisterPowerSettingNotification(handle) != 0;
    }

    internal sealed class DeviceSubscription : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
    {
        public DeviceSubscription() : base(ownsHandle: true) { }
        protected override bool ReleaseHandle() => UnregisterDeviceNotification(handle) != 0;
    }

    /// <summary>Sends WM_POWERBROADCAST to <paramref name="window"/> when the display state changes.</summary>
    internal static PowerSubscription WatchDisplay(IntPtr window)
        => RegisterPowerSettingNotification(window, in ConsoleDisplayState, ToWindowHandle);

    /// <summary>Sends WM_DEVICECHANGE to <paramref name="window"/> when a HID interface comes or goes.</summary>
    internal static DeviceSubscription WatchHidDevices(IntPtr window)
    {
        var filter = new DeviceInterfaceHeader
        {
            Size = sizeof(DeviceInterfaceHeader),
            DeviceType = DbtDevTypDeviceInterface,
            ClassGuid = Hid.InterfaceClass
        };
        return RegisterDeviceNotificationW(window, &filter, ToWindowHandle);
    }

    /// <summary>The display state a window message reports (0 off, 1 on, 2 dimmed), or null for other messages.</summary>
    internal static int? DisplayState(int message, IntPtr wParam, IntPtr lParam)
    {
        if (message != WmPowerBroadcast || (int)wParam != PbtPowerSettingChange || lParam == IntPtr.Zero)
            return null;
        var setting = (PowerSetting*)lParam;
        return setting->Setting == ConsoleDisplayState && setting->DataLength >= 1 ? setting->Data : null;
    }

    /// <summary>The interface path of a device that arrived or was removed, or null for other messages.</summary>
    internal static string? ChangedInterfacePath(int message, IntPtr wParam, IntPtr lParam)
    {
        if (message != WmDeviceChange || lParam == IntPtr.Zero
            || (int)(long)wParam is not (DbtDeviceArrival or DbtDeviceRemoveComplete))
            return null;
        var header = (DeviceInterfaceHeader*)lParam;
        if (header->DeviceType != DbtDevTypDeviceInterface)
            return null;
        // dbcc_name: a zero-terminated path right after the header.
        var path = new string((char*)(header + 1));
        return path.Length > 0 ? path : null;
    }

    /// <summary>POWERBROADCAST_SETTING with a one-byte payload.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PowerSetting
    {
        public Guid Setting;
        public uint DataLength;
        public byte Data;
    }

    /// <summary>DEV_BROADCAST_DEVICEINTERFACE_W without the trailing name.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceInterfaceHeader
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
        public Guid ClassGuid;
    }

    [LibraryImport(Libraries.User32, SetLastError = true)]
    private static partial PowerSubscription RegisterPowerSettingNotification(IntPtr recipient, in Guid setting, uint flags);

    [LibraryImport(Libraries.User32, SetLastError = true)]
    private static partial int UnregisterPowerSettingNotification(IntPtr handle);

    [LibraryImport(Libraries.User32, SetLastError = true)]
    private static partial DeviceSubscription RegisterDeviceNotificationW(IntPtr recipient, void* filter, uint flags);

    [LibraryImport(Libraries.User32)]
    private static partial int UnregisterDeviceNotification(IntPtr handle);
}
