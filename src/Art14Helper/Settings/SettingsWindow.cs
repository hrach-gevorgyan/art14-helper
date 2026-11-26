namespace Art14Helper;

/// <summary>
/// The settings window: a navigation pane on the left and the current page on the right,
/// drawn on its own (no controls), so switching or refreshing a page never flickers.
/// Opens centred on the monitor under the pointer, where the tray icon was clicked.
/// </summary>
internal sealed class SettingsWindow : PaintedWindow
{
    private const uint LiveRefresh = 3000;

    private readonly Action<IMenuBuilder> _buildMenu;
    private readonly Action _readSensors;
    private readonly DriversPage _drivers;
    private readonly SettingsPages _pages;
    private readonly UiTimer _liveTimer;
    private readonly UiTimer _soonRebuild;
    private readonly UiTimer _lateRebuild;
    private readonly Surface _nav;
    private readonly Surface _content;
    private readonly List<NavPart> _navItems = [];
    private HeadingPart? _heading;
    private List<SettingsPage> _pageList = [];
    private string _current;
    private string? _shown;
    private DeviceCardPart? _device;
    private StatusTilesPart? _hardware;

    internal SettingsWindow(Action<IMenuBuilder> buildMenu, Func<bool> startsWithWindows, Action toggleStartup,
        Action readSensors, Func<Task<IReadOnlyList<DriverComponent>>> driverList, string? startPage = null)
    {
        _buildMenu = buildMenu;
        _readSensors = readSensors;
        _current = startPage ?? SettingsPages.Home;
        _drivers = new DriversPage(driverList);
        _drivers.Changed += () =>
        {
            if (_current == SettingsPages.Drivers)
                ShowPage();
        };
        _pages = new SettingsPages(Run, Navigate, startsWithWindows, toggleStartup, _drivers);
        _liveTimer = new UiTimer(LiveRefresh, RefreshLive);
        _soonRebuild = new UiTimer(300, () => { _soonRebuild!.Stop(); Rebuild(); });
        _lateRebuild = new UiTimer(1500, () => { _lateRebuild!.Stop(); Rebuild(); });
        _nav = AddSurface();
        _content = AddSurface(scrolls: true);
        FluentTheme.Reload();

        var (area, scale) = User32.MonitorAt(User32.CursorPosition);
        var width = Math.Min((int)(1000 * scale), (int)(area.Width * 0.92));
        var height = Math.Min((int)(720 * scale), (int)(area.Height * 0.92));
        var bounds = new Rectangle(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, width, height);
        Open("Art14Helper.Settings", AppInfo.Name,
            User32.Caption | User32.SystemMenu | User32.ThickFrame | User32.MinimizeBox | User32.MaximizeBox, bounds, scale);
        _drivers.Owner = Handle;
        Rebuild();
        ShowAndActivate();
        _liveTimer.Start();
    }

    protected override Size MinimumSize => new(S(720), S(500));

    protected override void ScaleChanged()
    {
        _shown = null;   // fonts and sizes change: rebuild everything
        if (_pageList.Count > 0)
        {
            ShowNav();
            ShowPage();
        }
    }

    protected override bool KeyPressed(int key)
    {
        var page = Math.Max(S(40), _content.Area.Height - S(60));
        switch (key)
        {
            case User32.KeyEscape: Close(); break;
            case User32.KeyPageDown: _content.ScrollBy(page); break;
            case User32.KeyPageUp: _content.ScrollBy(-page); break;
            case User32.KeyDown: _content.ScrollBy(S(40)); break;
            case User32.KeyUp: _content.ScrollBy(-S(40)); break;
            case User32.KeyHome: _content.ScrollTo(0); break;
            case User32.KeyEnd: _content.ScrollTo(int.MaxValue); break;
            default: return false;
        }
        return true;
    }

    /// <summary>Reads every setting again and redraws the current page, keeping the scroll position.</summary>
    internal void Rebuild()
    {
        if (!IsOpen)
            return;
        var menu = new MenuModel();
        try
        {
            _buildMenu(menu);
        }
        catch (Exception exception)
        {
            Log.Error("Settings pages not built", exception);
        }
        var pages = _pages.From(menu);
        var navChanged = !pages.Select(page => page.Title).SequenceEqual(_pageList.Select(page => page.Title));
        _pageList = pages;
        if (_pageList.All(page => page.Title != _current))
            _current = SettingsPages.Home;
        if (navChanged || _navItems.Count == 0)
            ShowNav();
        ShowPage();
    }

    internal void Navigate(string title)
    {
        if (_pageList.Count > 0 && _pageList.All(page => page.Title != title))
            return;
        _current = title;
        foreach (var item in _navItems)
            item.Selected = item.Text == SettingsPages.Shown(title);   // items show the shown title, see ShowNav
        Rebuild();   // fresh values: a key or the tray menu may have changed something meanwhile
    }

    /// <summary>Runs a click, then reads the settings again: at once for instant changes, and once more for those applied in the background.</summary>
    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            Log.Error("Settings action failed", exception);
        }
        RebuildSoon();
    }

    /// <summary>
    /// Reads the settings again shortly (instant changes) and once more a little later (changes
    /// applied in the background). Requests in between move both back, so a burst of changes
    /// costs two rebuilds, not two each.
    /// </summary>
    internal void RebuildSoon()
    {
        _soonRebuild.Stop();
        _soonRebuild.Start();
        _lateRebuild.Stop();
        _lateRebuild.Start();
    }

    private void ShowNav()
    {
        _heading?.Free();
        _nav.Clear();
        _navItems.Clear();
        _heading = new HeadingPart(AppInfo.Name, UiFont.Text(12, Scale), S(24), S(12), S(52));
        _nav.Add(_heading);
        foreach (var page in _pageList)
        {
            var title = page.Title;
            // The title is the page's identity; Home, Drivers and System are named in English
            // in the code, menu pages arrive translated already (T of those finds nothing).
            var item = new NavPart(SettingsPages.Shown(title), page.Glyph, Scale, () => Navigate(title)) { Selected = title == _current };
            _navItems.Add(item);
            _nav.Add(item);
        }
        LayoutNav();
    }

    private void ShowPage()
    {
        if (_pageList.FirstOrDefault(page => page.Title == _current) is not { } page)
            return;
        if (page.Title == SettingsPages.Drivers)
            _drivers.StartIfNeeded();

        List<SettingsSection> sections;
        try
        {
            sections = page.Build().Where(section => section.Rows.Count > 0).ToList();
        }
        catch (Exception exception)
        {
            Log.Error($"Settings page {page.Title} not built", exception);
            sections = [];
        }

        var keepScroll = _shown == page.Title ? _content.ScrollY : 0;
        if (_shown != page.Title)
            _content.ClearFocus();   // the same position on another page is another control
        _content.Clear();
        _device = null;
        _hardware = null;
        _content.Add(new TextPart(SettingsPages.Shown(page.Title), UiFont.Strong(28, Scale), FluentTheme.Text, fixedHeight: S(46)));
        if (!string.IsNullOrEmpty(page.Description))
            _content.Add(new TextPart(page.Description, UiFont.Text(14, Scale), FluentTheme.TextSecondary, fixedHeight: S(30)));

        if (page.Title == SettingsPages.Home)
        {
            _content.Add(_device = new DeviceCardPart(Scale) { State = SettingsPages.Device() });
            _content.Add(new SectionTitle(T("Quick settings"), Scale));
            _content.Add(new QuickTilesPart(Scale) { Items = _pages.QuickSettings() });
            _content.Add(new SectionTitle(T("Hardware"), Scale));
            _content.Add(_hardware = new StatusTilesPart(Scale) { Tiles = SettingsPages.Hardware() });
        }
        foreach (var section in sections)
            _content.Add(new CardPart(section.Title, section.Rows, Scale));

        _shown = page.Title;
        LayoutContent();
        _content.ScrollTo(keepScroll);
        Invalidate();
    }

    protected override void Layout()
    {
        var client = ClientSize;
        var navWidth = S(280);
        _nav.Area = new Rectangle(0, 0, navWidth, client.Height);
        _content.Area = new Rectangle(navWidth, 0, Math.Max(0, client.Width - navWidth), client.Height);
        LayoutNav();
        LayoutContent();
    }

    private void LayoutNav()
    {
        var y = S(12);
        foreach (var part in _nav.Parts)
        {
            var height = part.HeightFor(_nav.Area.Width);
            var inset = part is HeadingPart ? S(20) : S(8);
            part.Bounds = new Rectangle(inset, y, _nav.Area.Width - inset - S(8), height);
            y += height + (part is HeadingPart ? S(8) : 0);
        }
    }

    private void LayoutContent()
    {
        var left = S(32);
        var width = Math.Max(S(200), Math.Min(_content.Area.Width - S(64), S(1000)));
        var y = S(28);
        TextPart? previous = null;
        foreach (var part in _content.Parts)
        {
            var height = part.HeightFor(width);
            part.Bounds = new Rectangle(left, y, width, height);
            y += height + part switch
            {
                SectionTitle => S(6),
                TextPart when previous is null => S(2),   // the page title sits close to its description
                TextPart => S(16),
                _ => S(24)
            };
            previous = part as TextPart ?? previous;
        }
        _content.ContentHeight = y + S(8);
        _content.ScrollTo(_content.ScrollY);
    }

    /// <summary>Battery, temperatures and fans on Home, every few seconds while it's shown.</summary>
    private void RefreshLive()
    {
        // Only while the window is in front: each round reads the BIOS sensors.
        if (!IsOpen || User32.GetForegroundWindow() != Handle || _current != SettingsPages.Home || _hardware is null)
            return;
        _readSensors();   // the same throttled read as hovering the tray icon
        var height = _hardware.HeightFor(_hardware.Bounds.Width);
        if (_device is not null)
            _device.State = SettingsPages.Device();
        _hardware.Tiles = SettingsPages.Hardware();
        if (_hardware.HeightFor(_hardware.Bounds.Width) != height)
            LayoutContent();
    }

    protected override nint? Receive(uint message, nint wParam, nint lParam)
    {
        if (message == WindowMessage.Destroy)
        {
            _liveTimer.Dispose();
            _soonRebuild.Dispose();
            _lateRebuild.Dispose();
            _heading?.Free();
            _drivers.Dispose();
        }
        return base.Receive(message, wParam, lParam);
    }

    /// <summary>A heading between groups on Home.</summary>
    private sealed class SectionTitle(string text, float scale) : Part
    {
        internal override int HeightFor(int width) => (int)Math.Round(30 * scale);

        internal override void Paint(Canvas canvas) => canvas.Text(text, UiFont.Strong(14, scale), Bounds, FluentTheme.Text);
    }
}
