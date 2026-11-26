namespace Art14Helper;

// TrayApp: the right-click menu, the settings window, and the display choices both show.
internal sealed partial class TrayApp
{
    private SettingsWindow? _settings;
    private TrayMenu? _openMenu;

    private void ShowMenu()
    {
        using var menu = new TrayMenu();
        if (_limited)
        {
            menu.AddItem(T("Restart with admin rights"), RestartWithAdminRights,
                tooltip: T("Running without admin rights: fans, temperatures and BIOS features are off. This starts the app again with them."));
            menu.AddSeparator();
        }
        if (UpdateCheck.Available is { } release)
        {
            menu.AddItem(T("Version {0} is out", release.Version.ToString(3)), UpdateCheck.OpenPage,
                tooltip: T("Opens the release page on GitHub. Download the new exe there and run it to update; your settings stay."));
            menu.AddSeparator();
        }
        menu.AddItem(T("Settings"), () => ShowSettings(), tooltip: T("Opens the settings window (so does a left click on the icon)."));
        menu.AddSeparator();
        BuildMenu(menu);
        menu.AddSeparator();
        menu.AddItem(T("Start with Windows"), ToggleStartup, @checked: Autostart.IsEnabled);
        menu.AddItem(T("Exit"), () => Exit("Exit chosen in the tray menu"));

        _openMenu = menu;
        var chosen = menu.Show(_window.Handle);
        _openMenu = null;
        _menuTip.Hide();
        if (chosen is not null)
        {
            UiThread.Post(chosen);   // after the menu is fully gone
            RefreshSettingsSoon();
        }
    }

    /// <summary>
    /// A setting changed outside the settings window (tray menu, a key, wake): the open
    /// window reads everything again, at once and after background changes have landed.
    /// Safe from any thread.
    /// </summary>
    private void RefreshSettingsSoon() => UiThread.Post(() => _settings?.RebuildSoon());

    // Limited mode's fix: the copy with admin rights that starts asks this one to close.
    private static void RestartWithAdminRights() => _ = Task.Run(() =>
    {
        Log.Info("Restart with admin rights chosen");
        if (!Installer.StartWithAdminRights())
            Popup.Show(PopupIcon.Warning, T("Still in limited mode"), T("The app with admin rights didn't start"));
    });

    // While the menu is open, WM_MENUSELECT names the highlighted entry.
    private void OnMenuHighlight(nint wParam, nint lParam)
    {
        var idOrPosition = (int)(wParam & 0xFFFF);
        var flags = (uint)((wParam >> 16) & 0xFFFF);
        var tip = lParam == 0 || idOrPosition == 0xFFFF ? null
            : _openMenu?.TipFor(lParam, idOrPosition, PopupMenus.IsSubMenuEntry(flags));
        if (tip is null)
        {
            _menuTip.Hide();
            return;
        }
        var pointer = User32.CursorPosition;
        _menuTip.Show(tip, pointer.X + 16, pointer.Y + 20);   // below right of the pointer, clear of it
    }

    /// <summary>Every choice except Settings, Start with Windows and Exit; the settings window shows the same.</summary>
    private void BuildMenu(IMenuBuilder menu) => BuildMenu(menu, () => ShowSettings(SettingsPages.Drivers));

    /// <summary>
    /// The same menu without the running app: only <paramref name="openDrivers"/> needs it.
    /// Reads the current state; building it changes nothing.
    /// </summary>
    internal static void BuildMenu(IMenuBuilder menu, Action openDrivers)
    {
        KeepAwakeMenu.Build(menu.AddSubMenu(T("Keep awake"), T("Stops the laptop from sleeping, for a while or while you work remotely.")));
        ChargeLimitMenu.Build(menu.AddSubMenu(T("Charge limit"), T("Keeps the battery below full, so it ages slower.")));
        KeyboardLightMenu.Build(menu.AddSubMenu(T("Keyboard"), T("Backlight level, and when it turns off.")));
        Art14Menu.BuildFnLock(menu.AddSubMenu(T("Fn lock"), T("What the top row of keys does without Fn.")));
        Art14Menu.BuildHotkeys(menu.AddSubMenu(T("F10 / F11 keys"), T("What F10 and F11 open while the top row is set to media keys.")));
        if (TouchpadLink.IsPresent)
            TouchpadSettingsMenu.Build(menu.AddSubMenu(T("Touchpad"), T("Vibration, press sensitivity, edge gestures and knuckle screenshot.")));
        if (SpatialAudioController.IsSupported())
            Art14Menu.BuildSound(menu.AddSubMenu(T("Sound"), T("Spatial or stereo sound from the speakers.")));
        BuildDisplayMenu(menu.AddSubMenu(T("Display"), T("Refresh rate and colors of the built-in screen.")));
        if (PowerMode.Current is { } powerMode)   // null with another power plan active
            PowerModeMenu.Build(menu.AddSubMenu(T("Power mode"), T("Windows' power mode: battery life against speed.")), powerMode);
        Art14Menu.AddTouchscreen(menu);
        menu.AddItem(T("Drivers"), openDrivers, tooltip: T("BIOS and driver updates from HONOR."));
    }

    /// <summary>Opens the settings window, on a page if given, or brings the open one forward with fresh values.</summary>
    private void ShowSettings(string? page = null)
    {
        if (_disposed)
            return;
        if (_settings is not { IsOpen: true } window)
        {
            Efficiency.Set(false);   // full speed while a window is open
            _settings = new SettingsWindow(BuildMenu, () => Autostart.IsEnabled, ToggleStartup,
                () => _ = ReadSensorsAsync(), () => _drivers.Value, page);
            _settings.Destroyed += () =>
            {
                _settings = null;
                if (_drivers.IsValueCreated)
                    _drivers = new(ReadDriversInBackground);
                UiThread.After(2000, () =>
                {
                    Memory.Trim();   // fonts, page parts and the driver list are garbage now
                    if (_settings is null)
                        Efficiency.Set(true);
                });
            };
            return;
        }
        if (page is not null)
            window.Navigate(page);   // reads everything again too
        else
            window.Rebuild();
        window.ShowAndActivate();
    }

    /// <summary>Also kept as the setup choice, so an update keeps autostart off when it was turned off here.</summary>
    private void ToggleStartup()
    {
        var on = !Autostart.IsEnabled;
        _ = Privileged.StartupEnabled.ApplyFromMenuAsync(on, T("Start with Windows couldn't be changed."),
            () => AppSettings.SetupStartWithWindows = on);
    }

    /// <summary>
    /// Refresh rate (one choice of three: picking a fixed rate turns Auto off) and, where
    /// the calibration works, the color mode. Rebuilt every time the menu opens.
    /// </summary>
    private static void BuildDisplayMenu(IMenuBuilder menu)
    {
        var highest = RefreshRate.Highest;
        var current = RefreshRate.Current;
        var auto = RefreshRate.IsAuto;
        menu.AddItem(T("Refresh rate"), null, enabled: false);
        if (highest > 0)
            menu.AddItem($"{highest} Hz", () => RefreshRate.Choose(highest), @checked: !auto && current == highest,
                tooltip: T("The panel's fastest rate, {0} Hz: the smoothest motion.", highest));
        menu.AddItem("60 Hz", () => RefreshRate.Choose(60), @checked: !auto && current == 60,
            tooltip: T("Uses less battery and runs cooler."));
        menu.AddItem(T("Auto: max on charger, 60 Hz on battery"), () => RefreshRate.SetAuto(true), @checked: auto,
            tooltip: T("Follows the power source, and comes back after sleep."));

        if (ColorModeService.IsSupported != true)
            return;
        menu.AddSeparator();
        if (DisplayPanel.IsHdrOn())
        {
            menu.AddItem(T("Color modes: turn off HDR to use"), null, enabled: false,
                tooltip: T("sRGB and P3 need SDR. Turn HDR off in Windows Settings > Display and the chosen mode returns by itself."));
            return;
        }
        menu.AddItem(T("Color mode"), null, enabled: false);
        var mode = ColorModeService.Current;
        AddColorMode(menu, ColorMode.Native, T("Native"), mode, T("Everything the panel can show: the most vivid."));
        AddColorMode(menu, ColorMode.Srgb, "sRGB", mode, T("Calibrated sRGB: true colors for the web, photos and office work."));
        AddColorMode(menu, ColorMode.P3, "P3", mode, T("Calibrated Display P3: wide, true colors for photo and video work."));
        if (ColorModeService.IsAvailable(ColorMode.AdobeRgb))
            AddColorMode(menu, ColorMode.AdobeRgb, "Adobe RGB", mode,
                T("Calibrated Adobe RGB, the space many photographers edit print work in. The panel covers about 96% of it."));
        var nativeWhite = AppSettings.ColorNativeWhite;
        menu.AddItem(T("Keep native white"), () => Task.Run(() =>
            {
                var on = !AppSettings.ColorNativeWhite;   // now, not when the menu was built
                ReportProblem(ColorModeService.SetNativeWhite(on));
            }),
            @checked: nativeWhite, toggle: true,
            tooltip: T("Calibrated colors with the panel's own cool white instead of a warmer one. Off: standard D65 white, the most accurate."));
    }

    private static void AddColorMode(IMenuBuilder menu, ColorMode mode, string name, ColorMode? current, string tooltip)
        => menu.AddItem(name, () => Task.Run(() => ReportProblem(ColorModeService.Set(mode))),
            @checked: current == mode, tooltip: tooltip);

    /// <summary>A color change the user made needs no popup; one that didn't apply does, with the reason.</summary>
    private static void ReportProblem(ColorModeResult result)
    {
        switch (result)
        {
            case ColorModeResult.HdrOn:
                Popup.Show(PopupIcon.Warning, T("Turn off HDR first"), T("Color modes need SDR"));   // not applied: always shown
                break;
            case ColorModeResult.Failed:
                Popup.Failed(T("Color mode failed"));
                break;
        }
    }
}
