using Art14Helper.Ui;
using Xunit;

namespace Art14Helper.Tests;

/// <summary>Settings pages, theme colors and the tray menu's bookkeeping.</summary>
public class UiTests
{
    [Theory]
    [InlineData("Home · 40–70%", "Home")]
    [InlineData("Auto: max on charger, 60 Hz on battery", "Auto")]
    [InlineData("60 Hz", "60 Hz")]
    public void QuickSettingsShowTheShortName(string item, string shown) => Assert.Equal(shown, SettingsPages.ShortName(item));

    [Fact]
    public void ShadeMovesTowardWhiteOrBlack()
    {
        var gray = Color.FromArgb(100, 100, 100);
        Assert.Equal(Color.FromArgb(255, 255, 255).ToArgb(), FluentTheme.Shade(gray, 1f).ToArgb());
        Assert.Equal(Color.FromArgb(0, 0, 0).ToArgb(), FluentTheme.Shade(gray, -1f).ToArgb());
        Assert.Equal(Color.FromArgb(116, 116, 116).ToArgb(), FluentTheme.Shade(gray, 0.1f).ToArgb());
    }

    [Fact]
    public void BatteryHealthIsFullChargeOverDesign()
        => Assert.Equal(102, new Art14Helper.Native.BatteryHealth(59673, 61104, 2).Percent);

    [Theory]
    [InlineData(61104, "100%+")]   // new, above its rating
    [InlineData(59673, "100%")]
    [InlineData(50000, "84%")]
    public void HealthAboveTheRatingShowsAs100Plus(int full, string shown)
        => Assert.Equal(shown, new Art14Helper.Native.BatteryHealth(59673, full, 2).Shown);

    [Fact]
    public void EachHardwareTileShowsItsOwnDetail()
    {
        var tiles = new StatusTilesPart(1f)
        {
            Tiles = [new StatusTile("a", "A", "1"), new StatusTile("b", "B", "2", "second"), new StatusTile("c", "C", "3", "third")],
            Bounds = new Rectangle(0, 0, 300, 84)
        };
        Assert.Null(tiles.Tip(new Point(50, 40)));
        Assert.Equal("second", tiles.Tip(new Point(150, 40)));
        Assert.Equal("third", tiles.Tip(new Point(299, 40)));
    }

    [Fact]
    public void LongDescriptionsWrapInsteadOfBeingCut()
    {
        static int Height(string text, int width) => new CardPart(null, [new RowSpec { Text = "Row", Secondary = text }], 1f).HeightFor(width);
        var longText = string.Join(' ', Enumerable.Repeat("words that need room", 12));
        Assert.Equal(Height("Short", 600), Height("Short", 300));
        Assert.True(Height(longText, 400) > Height("Short", 400));
        Assert.True(Height(longText, 300) >= Height(longText, 900));
        Assert.Equal(Height(longText, 100), Height(longText, 120));   // capped at three lines, then a tooltip
    }

    [Fact]
    public void HomeFindsTheGroupsTheMenusReallyHave()
    {
        var menu = new MenuModel();
        KeyboardLightMenu.Build(menu.AddSubMenu("Keyboard", "Backlight level, and when it turns off."));
        var pages = new SettingsPages(_ => { }, _ => { }, () => true, () => { },
            new DriversPage(() => Task.FromResult<IReadOnlyList<DriverComponent>>([])));
        pages.From(menu);
        Assert.Contains(pages.QuickSettings(), tile => tile.Title == "Keyboard light");
    }

    [Fact]
    public void TrayMenuFindsDescriptionsByIdAndPosition()
    {
        using var menu = new TrayMenu();
        menu.AddItem("Settings", () => { });
        menu.AddSeparator();
        var sub = menu.AddSubMenu("Keyboard", "Backlight level");
        var id = sub.AddItem("Auto", () => { }, tooltip: "Follows the room's light");

        Assert.Equal("Backlight level", menu.TipFor(menu.Handle, 2, isSubMenu: true));
        Assert.Equal("Follows the room's light", menu.TipFor(IntPtr.Zero, id, isSubMenu: false));
        Assert.Null(menu.TipFor(menu.Handle, 0, isSubMenu: true));
    }
}
