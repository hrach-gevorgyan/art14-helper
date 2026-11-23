using System.Runtime.CompilerServices;
using Art14Helper.Native;
using Xunit;

namespace Art14Helper.Tests;

/// <summary>Refresh rate rules and the on-screen popup.</summary>
public class DisplayTests
{
    [Theory]
    [InlineData(true, true, 165, null, 165)]    // Auto on the charger: the panel's best
    [InlineData(true, false, 165, null, 60)]    // Auto on battery: 60 Hz
    [InlineData(true, true, 0, 120, 0)]         // Auto with unknown rates: leave it
    [InlineData(false, true, 165, 120, 120)]    // fixed choice, whatever the power
    [InlineData(false, false, 165, 120, 120)]
    [InlineData(false, true, 165, null, 0)]     // nothing chosen yet
    public void WantedRateFollowsTheRules(bool auto, bool pluggedIn, int highest, int? saved, int expected)
        => Assert.Equal(expected, RefreshRate.Wanted(auto, pluggedIn, highest, saved));

    [Theory]
    [InlineData(new double[] { 175, 80, 62, 186, 35, 10, 80, 84 })]          // top 8 bits, as this laptop reports
    [InlineData(new double[] { 700, 320, 248, 744, 140, 40, 320, 336 })]     // full 10 bits
    public void EdidPrimariesReadInEitherEncoding(double[] raw)
    {
        var colors = PanelColorimetry.FromEdidValues(raw);
        Assert.True(colors.IsValid());
        Assert.Equal(0.684, colors.Red.X, 2);
        Assert.Equal(0.3125, colors.White.X, 2);
    }

    [Fact]
    public void AdapterIdMatchesTheWindowsLuid() => Assert.Equal(8, Unsafe.SizeOf<AdapterId>());

    [Fact]
    public void EveryPopupIconLooksDifferent()
    {
        // One symbol each; an "off" state may reuse its "on" symbol with a slash drawn across.
        var looks = Enum.GetValues<PopupIcon>().Select(icon => (Popup.Glyph(icon), Popup.IsSlashed(icon))).ToList();
        Assert.All(looks, look => Assert.Single(look.Item1));
        Assert.Equal(looks.Count, looks.Distinct().Count());
    }

    [Fact]
    public void PopupScalesWithTheScreen()
    {
        using var small = Popup.Render(PopupIcon.Microphone, "Microphone muted", "No app can hear you.", 1f);
        using var large = Popup.Render(PopupIcon.Microphone, "Microphone muted", "No app can hear you.", 1.5f);
        Assert.InRange(large.Width, small.Width * 1.5 - 3, small.Width * 1.5 + 3);
        Assert.InRange(large.Height, small.Height * 1.5 - 3, small.Height * 1.5 + 3);
        Assert.Equal(0, small.GetPixel(0, 0).A);                                    // shadow margin and corner stay see-through
        Assert.True(small.GetPixel(small.Width / 2, small.Height / 2).A > 240);    // the card itself is solid
    }

    [Fact]
    public void LevelPopupsDrawABarInsteadOfText()
    {
        using var bar = Popup.Render(PopupIcon.KeyboardLight, "Keyboard light", null, 1f, level: 0.5f);
        using var plain = Popup.Render(PopupIcon.KeyboardLight, "Keyboard light", null, 1f);
        Assert.True(bar.Width > plain.Width);   // room for the bar on the right
    }

    [Theory]
    [InlineData(0, false, 0xE850)]
    [InlineData(69, false, 0xE857)]
    [InlineData(100, false, 0xE83F)]
    [InlineData(55, true, 0xE860)]
    [InlineData(90, true, 0xE83E)]
    [InlineData(100, true, 0xEA93)]
    public void BatteryIconShowsTheLevelInTenths(int percent, bool charging, int glyph)
        => Assert.Equal(glyph, Popup.BatteryGlyph(percent, charging)[0]);

    [Fact]
    public void LongPopupTextKeepsTheCardCompact()
    {
        using var card = Popup.Render(PopupIcon.Info, new string('x', 200), new string('y', 300), 1f);
        Assert.True(card.Width <= 440 + 2 * 20 + 1);   // capped (plus the shadow margin), text cut with an ellipsis
    }
}
