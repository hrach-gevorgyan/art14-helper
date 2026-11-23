using System.Runtime.InteropServices;
using Art14Helper.Native;
using Xunit;

namespace Art14Helper.Tests;

/// <summary>Parsing of the raw structures Windows hands to the app.</summary>
public unsafe class NativeInteropTests
{
    private const int WmPowerBroadcast = 0x0218, PbtPowerSettingChange = 0x8013;
    private const int WmDeviceChange = 0x0219, DbtDeviceArrival = 0x8000, DbtDeviceRemoveComplete = 0x8004;
    private static readonly Guid ConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    [Fact]
    public void SplitsMultiStrings()
    {
        Assert.Equal(["\\\\?\\HID#A", "\\\\?\\HID#B"], Hid.SplitMultiString("\\\\?\\HID#A\0\\\\?\\HID#B\0\0"));
        Assert.Empty(Hid.SplitMultiString("\0"));
        Assert.Empty(Hid.SplitMultiString(""));
        Assert.Equal(["x"], Hid.SplitMultiString("x"));
    }

    [Theory]
    [InlineData(1)]   // on
    [InlineData(0)]   // off
    [InlineData(2)]   // dimmed
    public void DisplayStateNotification(byte state)
    {
        var setting = new SystemNotifications.PowerSetting { Setting = ConsoleDisplayState, DataLength = 1, Data = state };
        Assert.Equal((int?)state, SystemNotifications.DisplayState(WmPowerBroadcast, PbtPowerSettingChange, (nint)(&setting)));
    }

    [Fact]
    public void OtherPowerMessagesHaveNoDisplayState()
    {
        var other = new SystemNotifications.PowerSetting { Setting = Guid.NewGuid(), DataLength = 1, Data = 1 };
        Assert.Null(SystemNotifications.DisplayState(WmPowerBroadcast, PbtPowerSettingChange, (nint)(&other)));
        Assert.Null(SystemNotifications.DisplayState(WmPowerBroadcast, 0x0007, (nint)(&other))); // PBT_APMRESUMESUSPEND
        Assert.Null(SystemNotifications.DisplayState(WmPowerBroadcast, PbtPowerSettingChange, 0));
    }

    [Theory]
    [InlineData(DbtDeviceArrival)]
    [InlineData(DbtDeviceRemoveComplete)]
    public void ReadsChangedDevicePath(int eventType)
    {
        var buffer = DeviceInterfaceMessage(5, "\\\\?\\HID#VID_35CC&PID_0104#1");
        try
        {
            Assert.Equal("\\\\?\\HID#VID_35CC&PID_0104#1",
                SystemNotifications.ChangedInterfacePath(WmDeviceChange, eventType, buffer));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [Fact]
    public void IgnoresOtherDeviceMessages()
    {
        var volume = DeviceInterfaceMessage(2, "C:"); // DBT_DEVTYP_VOLUME
        try
        {
            Assert.Null(SystemNotifications.ChangedInterfacePath(WmDeviceChange, DbtDeviceArrival, volume));
            Assert.Null(SystemNotifications.ChangedInterfacePath(WmDeviceChange, 0x0007, volume)); // DBT_DEVNODES_CHANGED
            Assert.Null(SystemNotifications.ChangedInterfacePath(WmDeviceChange, DbtDeviceArrival, 0));
        }
        finally
        {
            Marshal.FreeHGlobal(volume);
        }
    }

    [Fact]
    public void ReadsMuteFromVolumeNotification()
    {
        var data = stackalloc byte[32];
        new Span<byte>(data, 32).Clear();
        Assert.False(CoreAudio.IsMutedIn((nint)data));
        *(int*)(data + 16) = 1; // after the 16-byte event context GUID
        Assert.True(CoreAudio.IsMutedIn((nint)data));
    }

    [Fact]
    public void MenuSelectFlags()
    {
        Assert.True(PopupMenus.IsSubMenuEntry(0x0010 | 0x0080)); // MF_POPUP | MF_HILITE
        Assert.False(PopupMenus.IsSubMenuEntry(0x0080));
    }

    /// <summary>A DEV_BROADCAST_DEVICEINTERFACE_W-shaped block with the name after the header.</summary>
    private static IntPtr DeviceInterfaceMessage(int deviceType, string name)
    {
        var header = sizeof(SystemNotifications.DeviceInterfaceHeader);
        var size = header + (name.Length + 1) * 2;
        var block = Marshal.AllocHGlobal(size);
        new Span<byte>((void*)block, size).Clear();
        *(SystemNotifications.DeviceInterfaceHeader*)block = new() { Size = size, DeviceType = deviceType };
        name.AsSpan().CopyTo(new Span<char>((void*)(block + header), name.Length));
        return block;
    }
}
