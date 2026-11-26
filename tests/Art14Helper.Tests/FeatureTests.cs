using Xunit;

namespace Art14Helper.Tests;

/// <summary>Charge windows, keyboard light codes and BIOS key events.</summary>
public class FeatureTests
{
    // The feature enums are internal, so test data passes their names.

    [Theory]
    [InlineData("Home", 40, 70)]
    [InlineData("Office", 70, 90)]
    [InlineData("Travel", 95, 100)]
    [InlineData("Off", 0, 100)]
    public void ChargeLimitsRoundTripThroughTheirWindow(string name, int start, int end)
    {
        var limit = Enum.Parse<ChargeLimit>(name);
        Assert.Equal((start, end), ChargeLimits.Range(limit));
        Assert.Equal(limit, ChargeLimits.FromRange(start, end));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(50, 80)]
    [InlineData(40, null)]
    public void ForeignWindowsHaveNoLimit(int? start, int? end)
        => Assert.Null(ChargeLimits.FromRange(start, end));

    [Theory]
    [InlineData(0x02, "Off")]
    [InlineData(0x03, "Dim")]
    [InlineData(0x04, "Bright")]
    [InlineData(0x10, "Auto")]
    public void DecodesLightModeBytes(int code, string expected)
        => Assert.Equal(Enum.Parse<KeyboardLight>(expected), KeyboardLights.FromModeByte(code));

    [Fact]
    public void LightModeBytesRoundTrip()
    {
        foreach (var light in Enum.GetValues<KeyboardLight>())
            Assert.Equal(light, KeyboardLights.FromModeByte(KeyboardLights.ModeByte(light)));
        Assert.Null(KeyboardLights.FromModeByte(0x07));
        Assert.Null(KeyboardLights.FromModeByte(null));
    }

    [Theory]
    [InlineData(0x287u, "Microphone")]
    [InlineData(0x288u, "Camera")]
    [InlineData(0x28Au, "F10")]
    [InlineData(0x28Bu, "F11")]
    [InlineData(0x28Eu, "ScreenSnip")]
    public void MapsKeyCodes(uint code, string expected)
    {
        using var keys = new BiosKeys(() => false);
        BiosKey? seen = null;
        keys.KeyPressed += key => seen = key;
        keys.Dispatch(code);
        Assert.Equal(Enum.Parse<BiosKey>(expected), seen);
    }

    [Fact]
    public void ReportsTouchpadAndLightEvents()
    {
        var ignoreLight = false;
        using var keys = new BiosKeys(() => ignoreLight);
        var touchpad = new List<bool>();
        var lights = new List<KeyboardLight>();
        keys.TouchpadSwitched += touchpad.Add;
        keys.LightChanged += lights.Add;

        keys.Dispatch(0x283);
        keys.Dispatch(0x2A3);
        keys.Dispatch(0x2B3);
        ignoreLight = true;   // right after wake
        keys.Dispatch(0x2B1);

        Assert.Equal([true, false], touchpad);
        Assert.Equal([KeyboardLight.Bright], lights);
    }

    [Fact]
    public void ReportsCameraChangesOnly()
    {
        using var keys = new BiosKeys(() => false);
        var camera = new List<bool>();
        keys.CameraDocked += camera.Add;

        keys.Dispatch(0x2E0);   // taken off
        keys.Dispatch(0x2E0);   // repeated: no change
        keys.Dispatch(0x2E1);   // put back
        keys.Dispatch(0x2E1);   // re-announced: no change
        keys.Dispatch(0x2E0);

        Assert.Equal([false, true, false], camera);
    }

    [Fact]
    public void PopupAppearsVisibleFirstThenGlidesIntoPlace()
    {
        var (below, opacity) = Art14Helper.Ui.LayeredWindow.AppearFrame(16, 0);
        Assert.Equal((16, (byte)0), (below, opacity));                       // starts the full rise below, invisible

        (below, opacity) = Art14Helper.Ui.LayeredWindow.AppearFrame(16, Art14Helper.Ui.LayeredWindow.FadeInMs);
        Assert.Equal(255, opacity);                                          // fully visible early…
        Assert.InRange(below, 1, 3);                                         // …while still gliding the last pixels

        (below, opacity) = Art14Helper.Ui.LayeredWindow.AppearFrame(16, Art14Helper.Ui.LayeredWindow.RiseMs);
        Assert.Equal((0, (byte)255), (below, opacity));                      // at rest
        Assert.Equal((0, (byte)255), Art14Helper.Ui.LayeredWindow.AppearFrame(16, 5_000));   // a late timer overshoots nothing
    }

    [Fact]
    public void PopupSinksAsItFadesOut()
    {
        Assert.Equal((0, (byte)255), Art14Helper.Ui.LayeredWindow.LeaveFrame(16, 0));
        var (below, opacity) = Art14Helper.Ui.LayeredWindow.LeaveFrame(16, Art14Helper.Ui.LayeredWindow.FadeOutMs / 2);
        Assert.InRange(opacity, 200, 240);                                   // gentle at first
        Assert.InRange(below, 0, 2);
        Assert.Equal((8, (byte)0), Art14Helper.Ui.LayeredWindow.LeaveFrame(16, Art14Helper.Ui.LayeredWindow.FadeOutMs));   // half the rise, gone
    }

    [Fact]
    public void RepeatedPressesCountOncePerBurst()
    {
        using var keys = new BiosKeys(() => false);
        Assert.True(keys.IsFirstOfBurst(BiosKey.Camera, 1_000));
        Assert.False(keys.IsFirstOfBurst(BiosKey.Camera, 1_300));
        Assert.False(keys.IsFirstOfBurst(BiosKey.Camera, 1_650));   // still within 400 ms of the last repeat
        Assert.True(keys.IsFirstOfBurst(BiosKey.Camera, 2_100));
        Assert.True(keys.IsFirstOfBurst(BiosKey.Microphone, 2_150)); // each key has its own window
    }
}
