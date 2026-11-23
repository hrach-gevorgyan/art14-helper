using System.Text;
using Xunit;

namespace Art14Helper.Tests;

public class ColorCalibrationTests
{
    private static string IdentityXml(int size)
    {
        var text = new StringBuilder();
        for (var r = 0; r < size; r++)
        for (var g = 0; g < size; g++)
        {
            for (var b = 0; b < size; b++)
                text.Append($"{r * 4095 / (size - 1)},{g * 4095 / (size - 1)},{b * 4095 / (size - 1)},");
            text.Append('\n');
        }
        return $"<DisplayEngineConfig><Mode>sRGB</Mode><GMP_Feature>{text}</GMP_Feature></DisplayEngineConfig>";
    }

    [Fact]
    public void ParsesFullLutInRedGreenBlueOrder()
    {
        var samples = ColorModeService.ParseCalibration(IdentityXml(17), 17);
        Assert.True(samples is not null);
        Assert.Equal(17 * 17 * 17 * 3, samples!.Length);
        Assert.Equal(0.0, samples[0]);
        // Second sample: blue moves first.
        Assert.Equal(0.0, samples[3]);
        // Same integer step as IdentityXml: 4095 / 16 = 255.
        Assert.True(Math.Abs(samples[5] - 4095 / 16 / 4095.0) < 1e-9);
        // Last sample is white.
        Assert.Equal(1.0, samples[^1]);
        Assert.Equal(1.0, samples[^3]);
    }

    [Fact]
    public void RejectsWrongSize()
        => Assert.Null(ColorModeService.ParseCalibration(IdentityXml(9), 17));

    [Fact]
    public void RejectsOutOfRangeValues()
        => Assert.Null(ColorModeService.ParseCalibration(IdentityXml(17).Replace(">0,0,0,", ">5000,0,0,"), 17));

    [Fact]
    public void Gamma22MappingKeepsEndsAndDarkensNearBlack()
    {
        Assert.Equal(0.0, ColorModeService.Gamma22ToSrgbInput(0));
        Assert.Equal(1.0, ColorModeService.Gamma22ToSrgbInput(1));
        // Near black the sRGB curve is brighter, so a gamma 2.2 signal maps lower.
        Assert.True(ColorModeService.Gamma22ToSrgbInput(1 / 16.0) < 1 / 16.0 * 0.6);
        // Mid-gray barely moves.
        Assert.True(Math.Abs(ColorModeService.Gamma22ToSrgbInput(0.5) - 0.5) < 0.01);
    }

    [Fact]
    public void Gamma22ResampleOfIdentityFollowsTheMapping()
    {
        const int size = 17;
        var identity = ColorModeService.ParseCalibration(IdentityXml(size), size)!;
        var mapped = ColorModeService.ToGamma22(identity, size);
        var expected = ColorModeService.Gamma22ToSrgbInput(1 / 16.0);
        // Gray (1,1,1) of 16 now reads the identity table at the mapped position.
        var index = ((1 * size + 1) * size + 1) * 3;
        Assert.True(Math.Abs(mapped[index] - expected) < 0.002);
        Assert.True(Math.Abs(mapped[^1] - 1.0) < 1e-6);
    }
}
