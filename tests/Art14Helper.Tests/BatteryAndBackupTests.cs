using Art14Helper.Native;
using Xunit;

namespace Art14Helper.Tests;

/// <summary>Charger popup text, time to full, power modes and the settings backup file.</summary>
public class BatteryAndBackupTests
{
    private static BatteryStatus Status(int percent, bool charging) => new(true, charging, percent, null);

    [Fact]
    public void TimeToFullFollowsTheRate()
    {
        var charge = new BatteryCharge(RemainingMilliwattHours: 30_000, FullMilliwattHours: 60_000, RateMilliwatts: 30_000);
        Assert.Equal(TimeSpan.FromHours(1), charge.TimeTo(100));
        Assert.Equal(TimeSpan.FromMinutes(24), charge.TimeTo(70));
        Assert.Null(charge.TimeTo(40));                                   // already above
        Assert.Null((charge with { RateMilliwatts = 0 }).TimeTo(100));   // not charging
        Assert.Null((charge with { RateMilliwatts = -5000 }).TimeTo(100));
    }

    [Fact]
    public void ChargerPopupSaysWhatHappensNext()
    {
        var home = (40, 70);
        var charge = new BatteryCharge(30_000, 60_000, 30_000);
        Assert.Equal("24 min to 70%", TrayApp.ChargerDetail(Status(50, true), charge, home));
        Assert.Equal("1 h 00 min to full", TrayApp.ChargerDetail(Status(50, true), charge, (0, 100)));
        Assert.Equal("To 70%", TrayApp.ChargerDetail(Status(50, true), null, home));
        Assert.Equal("Limit reached", TrayApp.ChargerDetail(Status(70, false), charge, home));
        Assert.Equal("Charges below 40%", TrayApp.ChargerDetail(Status(55, false), charge, home));
        Assert.Equal("Full", TrayApp.ChargerDetail(Status(100, false), charge, (0, 100)));
    }

    [Fact]
    public void LongChargesShowHoursWithoutMangling()
    {
        var slow = new BatteryCharge(0, 60_000, 5_000);   // 12 hours
        Assert.Equal("12 h 00 min to full", TrayApp.ChargerDetail(Status(0, true), slow, (0, 100)));
    }

    [Fact]
    public void PowerModesMapFromWindowsOverlays()
    {
        Assert.Equal(PowerModeChoice.Balanced, PowerMode.FromGuid(Guid.Empty));
        Assert.Equal(PowerModeChoice.Balanced, PowerMode.FromGuid(new Guid("381b4222-f694-41f0-9685-ff5bb260df2e")));
        Assert.Equal(PowerModeChoice.Efficiency, PowerMode.FromGuid(new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a")));
        Assert.Equal(PowerModeChoice.Performance, PowerMode.FromGuid(new Guid("ded574b5-45a0-4f42-8737-46345c09c238")));
        Assert.Null(PowerMode.FromGuid(Guid.NewGuid()));
    }

    [Fact]
    public void BackupKeepsChoicesAndLeavesOutHardwareState()
    {
        var stored = new Dictionary<string, object>
        {
            ["Charge.Chosen"] = "Home",
            ["Charge.Current"] = "Home",
            ["Charge.Once"] = 1,
            ["Light.IdleSeconds"] = 30,
            ["Keys.F10Command"] = @"C:\Tools\My App.exe",
            ["Setup.DefaultsApplied"] = 1
        };
        var text = SettingsBackup.Write(stored);
        var read = SettingsBackup.Read(text);

        Assert.NotNull(read);
        Assert.Equal(3, read.Count);
        Assert.Equal("Home", read["Charge.Chosen"]);
        Assert.Equal(30, read["Light.IdleSeconds"]);
        Assert.Equal(@"C:\Tools\My App.exe", read["Keys.F10Command"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Charge.Chosen = \"Home\"")]
    [InlineData("<html>")]
    public void OnlyRealBackupsAreRead(string content) => Assert.Null(SettingsBackup.Read(content));

    [Fact]
    public void BackupReaderSkipsHardwareStateEvenIfAdded()
    {
        var read = SettingsBackup.Read("# Art14 Helper settings\r\nSetup.DefaultsApplied = 1\r\nCharge.Current = \"Off\"\r\nSound.Spatial = 0\r\n");
        Assert.NotNull(read);
        Assert.Equal(["Sound.Spatial"], read.Keys);
    }
}

/// <summary>The screenshot's clipboard copy: a standard bitmap every app can paste, plus PNG.</summary>
public class ClipboardImageTests
{
    [Fact]
    public void ScreenshotBecomesABottomUpBitmapAndAPng()
    {
        using var image = new Bitmap(3, 2, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        image.SetPixel(0, 0, Color.FromArgb(255, 10, 20, 30));   // top-left
        image.SetPixel(0, 1, Color.FromArgb(255, 40, 50, 60));   // bottom-left
        var (dib, png) = Art14Helper.Native.Clipboard.ImageData(image);

        Assert.Equal(40, BitConverter.ToInt32(dib, 0));            // BITMAPINFOHEADER
        Assert.Equal(3, BitConverter.ToInt32(dib, 4));
        Assert.Equal(2, BitConverter.ToInt32(dib, 8));             // positive height: bottom row first
        Assert.Equal(32, BitConverter.ToInt16(dib, 14));
        Assert.Equal(40 + 3 * 4 * 2, dib.Length);
        Assert.Equal(new byte[] { 60, 50, 40 }, dib[40..43]);       // first stored pixel: bottom-left, as BGR
        Assert.Equal(new byte[] { 30, 20, 10 }, dib[(40 + 12)..(40 + 15)]);
        Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, png[..4]);
    }
}
