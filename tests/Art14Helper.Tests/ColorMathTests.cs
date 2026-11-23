using Xunit;

namespace Art14Helper.Tests;

public class ColorMathTests
{
    [Fact]
    public void SameSpaceGivesIdentity()
    {
        var lut = ColorMath.BuildLut(Chromaticities.Srgb, Chromaticities.Srgb, 17);
        for (var i = 0; i < 17; i++)
        {
            var index = ((i * 17 + i) * 17 + i) * 3;
            Assert.True(Math.Abs(lut[index] - i / 16.0) < 1e-6);
        }
    }

    [Fact]
    public void WhiteKeepsFullBrightness()
    {
        var panel = new Chromaticities((0.686, 0.3137), (0.2375, 0.731), (0.1391, 0.0423), (0.3105, 0.3273), "test");
        var lut = ColorMath.BuildLut(panel, Chromaticities.Srgb, 17);
        var white = lut.AsSpan(lut.Length - 3).ToArray();
        Assert.True(Math.Abs(white.Max() - 1.0) < 1e-9);
        Assert.True(white.Min() > 0.95);
    }

    [Fact]
    public void ReadsMeasuredPointsFromFactoryFile()
    {
        const string xml = "<DisplayEngineConfig><DUTInfo_Feature>"
            + "<Origin_White>0.310517,0.327254,439.2</Origin_White><Origin_Red>0.686160,0.313656,116.3</Origin_Red>"
            + "<Origin_Green>0.237502,0.731006,328.8</Origin_Green><Origin_Blue>0.139131,0.042275,24.7</Origin_Blue>"
            + "</DUTInfo_Feature></DisplayEngineConfig>";
        var panel = Chromaticities.FromFactoryCalibration(xml);
        Assert.True(panel is not null);
        Assert.True(Math.Abs(panel!.Red.X - 0.68616) < 1e-9);
    }

    [Fact]
    public void IccProfileHasValidHeader()
    {
        var data = ColorMath.BuildIccProfile(Chromaticities.DisplayP3, "test");
        var size = (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];
        Assert.Equal(data.Length, size);
        Assert.Equal("acsp", System.Text.Encoding.ASCII.GetString(data, 36, 4));
        Assert.Equal("mntr", System.Text.Encoding.ASCII.GetString(data, 12, 4));
        Assert.Equal(9, data[131]);
    }

    [Fact]
    public void NativeWhiteMakesWhiteFull()
    {
        var panel = new Chromaticities((0.686, 0.3137), (0.2375, 0.731), (0.1391, 0.0423), (0.3105, 0.3273), "test");
        var lut = ColorMath.KeepNativeWhite(ColorMath.BuildLut(panel, Chromaticities.Srgb, 17));
        foreach (var value in lut.AsSpan(lut.Length - 3).ToArray())
            Assert.True(Math.Abs(value - 1.0) < 1e-9);
        Assert.Equal(0.0, lut[0]);
    }

    [Fact]
    public void AdobeRgbBuildsFromPanel()
    {
        var panel = new Chromaticities((0.686, 0.3137), (0.2375, 0.731), (0.1391, 0.0423), (0.3105, 0.3273), "test");
        var lut = ColorMath.BuildLut(panel, Chromaticities.AdobeRgb, 17);
        Assert.Equal(17 * 17 * 17 * 3, lut.Length);
        Assert.True(lut.All(v => v >= 0 && v <= 1));
    }
}
