using Art14Helper.Native;
using Xunit;

namespace Art14Helper.Tests;

/// <summary>Touchpad vendor reports, gesture parsing and device selection.</summary>
public class TouchpadTests
{
    private static HidInterface Device(ushort vendor, ushort product, ushort page, ushort usage, ushort output)
        => new($"\\\\?\\hid#vid_{vendor:x4}&pid_{product:x4}", vendor, product, page, usage, 9, output);

    [Fact]
    public void BuildsPaddedOutputReports()
    {
        Assert.Equal([0x0E, 0x02, 0x01, 0, 0, 0, 0, 0, 0], TouchpadLink.BuildReport(TouchpadCommand.Vibration, 1, 9));
        Assert.Equal([0x0E, 0x0B, 0x00], TouchpadLink.BuildReport(TouchpadCommand.KnuckleScreenshot, 0, 0));
    }

    [Theory]
    [InlineData(new byte[] { 0x0E, 0x03, 0x01 }, "BrightnessSwipe", true)]
    [InlineData(new byte[] { 0x0E, 0x03, 0x02, 0, 0 }, "BrightnessSwipe", false)]
    [InlineData(new byte[] { 0x0E, 0x04, 0x01 }, "VolumeSwipe", true)]
    [InlineData(new byte[] { 0x0E, 0x06, 0x00 }, "KnuckleDoubleTap", false)]
    public void ParsesGestureReports(byte[] report, string expected, bool up)
    {
        var gesture = TouchpadEvents.Parse(report);
        Assert.NotNull(gesture);
        Assert.Equal(Enum.Parse<TouchpadEvent>(expected), gesture.Value.Event);
        Assert.Equal(up, gesture.Value.Up);
    }

    [Theory]
    [InlineData(new byte[] { 0x0D, 0x03, 0x01 })]   // another report id
    [InlineData(new byte[] { 0x0E, 0x03, 0x03 })]   // swipe without a valid direction
    [InlineData(new byte[] { 0x0E, 0x02, 0x01 })]   // a setting echo, not an event
    [InlineData(new byte[] { 0x0E, 0x03 })]         // too short
    public void IgnoresOtherReports(byte[] report) => Assert.Null(TouchpadEvents.Parse(report));

    [Fact]
    public void ChoosesTheRightCollections()
    {
        var control = Device(0x35CC, 0x0104, 0xFF00, 0x01, 9);
        var touch = Device(0x35CC, 0x0104, 0x000D, 0x05, 0);
        var stranger = Device(0x046D, 0xC52B, 0xFF00, 0x01, 9);
        Assert.True(TouchpadLink.IsControl(control));
        Assert.False(TouchpadLink.IsControl(touch));
        Assert.False(TouchpadLink.IsControl(stranger));
        Assert.True(TouchpadLink.IsEventSource(control));
        Assert.False(TouchpadLink.IsEventSource(touch));
        Assert.False(TouchpadLink.IsEventSource(stranger));
    }

    [Theory]
    [InlineData(@"\\?\HID#VID_35CC&PID_0104&MI_01#7&abc", true)]
    [InlineData(@"\\?\hid#vid_27c6&pid_0f9a#1", true)]
    [InlineData(@"\\?\HID#VID_046D&PID_C52B#1", false)]
    public void RecognisesTouchpadPlugEvents(string path, bool expected)
        => Assert.Equal(expected, TouchpadLink.IsTouchpadPath(path));

    [Fact]
    public void EdgesMapToTheirFirmwareSwitches()
    {
        Assert.Equal(0x07, (byte)Enum.Parse<TouchpadEdge>("Brightness"));
        Assert.Equal(0x08, (byte)Enum.Parse<TouchpadEdge>("Volume"));
    }

    [Fact]
    public void ScreenshotNamesNeverOverwrite()
    {
        var when = new DateTime(2026, 11, 23, 14, 5, 9);
        var taken = new HashSet<string> { @"C:\Shots\Screenshot_20261123_140509.png", @"C:\Shots\Screenshot_20261123_140509_2.png" };
        Assert.Equal(@"C:\Shots\Screenshot_20261123_140509_3.png", KnuckleScreenshot.FreeName(@"C:\Shots", when, taken.Contains));
        Assert.Equal(@"C:\Other\Screenshot_20261123_140509.png", KnuckleScreenshot.FreeName(@"C:\Other", when, _ => false));
    }
}
