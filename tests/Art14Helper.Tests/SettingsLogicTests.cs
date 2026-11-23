using Xunit;

namespace Art14Helper.Tests;

/// <summary>The privileged command line: what it accepts and what it refuses before touching hardware.</summary>
public class SettingsLogicTests
{
    [Fact]
    public void LightStateParses()
    {
        Assert.True(Privileged.ParseLightState("Auto,30", out var state));
        Assert.Equal((KeyboardLight.Auto, (ushort)30), state);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Auto")]
    [InlineData("Glow,30")]
    [InlineData("Dim,-5")]
    public void BadLightStateIsRejected(string value)
        => Assert.False(Privileged.ParseLightState(value, out _));

    [Theory]
    [InlineData("unknown", "1")]
    [InlineData("fn-mode", "banana")]
    [InlineData("charge-limit", "Weekend")]
    [InlineData("spatial-audio", "maybe")]
    [InlineData("keyboard-light-timeout", "-1")]
    public void UnknownActionsOrValuesDoNothing(string name, string value)
        // Parsing fails before any hardware is touched, so this is safe anywhere.
        => Assert.False(Privileged.RunFromCommandLine(name, value));
}
