using Xunit;

namespace Art14Helper.Tests;

/// <summary>Status words for the charge limit and keyboard light, and what setup leaves alone.</summary>
public class StatusAndSetupTests
{
    private static SensorReading WithCharge(int start, int end)
        => new(DateTime.UtcNow, null, null, null, null, null, null, null, start, end);

    [Fact]
    public void ChargeLimitOffReadsAsOffEvenFromAFreshReading()
        => Assert.Equal("off", StatusReport.ChargeWord(WithCharge(0, 100)));

    [Fact]
    public void ChargeWindowsShowTheirRange()
        => Assert.Equal("40–70%", StatusReport.ChargeWord(WithCharge(40, 70)));

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("Auto", null, "Auto")]
    [InlineData("Dim", null, "Dim")]
    [InlineData("Auto", "Dim", "Dim")]        // Auto mode standing in for Dim reads as Dim
    [InlineData("Off", "Dim", "Off")]         // any real mode wins
    public void EmulatedLevelsReadAsTheChosenLevel(string? read, string? emulated, string? expected)
    {
        static KeyboardLight? Parse(string? text) => text is null ? null : Enum.Parse<KeyboardLight>(text);
        Assert.Equal(Parse(expected), KeyboardLights.Effective(Parse(read), Parse(emulated)));
    }

    [Fact]
    public void SetupLeavesTheChargeLimitAloneUnlessAsked()
        => Assert.False(new SetupWindow.Choices(true, true, false).RemoveChargeLimit);
}
