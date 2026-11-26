namespace Art14Helper;

// TrayApp: Windows' messages to the hidden window. Sleep, wake and the charger, devices,
// the display, the theme, and the end of the session; much of it puts back what the
// firmware or Windows forgot.
internal sealed partial class TrayApp
{
    // After waking, the firmware sends a few light events of its own; they're not the user's.
    private const int WakeSettleMilliseconds = 8000;
    // The classic resume and the display turning on both mean "woke up": act once.
    private const int WakeDebounceMilliseconds = 10000;
    private const int PowerStatusChanged = 0x000A, ResumedBySystem = 0x0012, ResumedByUser = 0x0007;   // PBT_*

    private readonly UiTimer _deviceSettle;
    private readonly UiTimer _chargerSettle;
    private long _ignoreLightEventsUntil;
    private long _lastWake = -WakeDebounceMilliseconds;
    private int? _displayState;          // null until Windows reports the first one
    private bool _pluggedIn = PowerSource.IsPluggedIn;

    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        if (message == TrayCallback)
        {
            OnTrayMouse((uint)(lParam & 0xFFFF));
            return 0;
        }
        if (message == TrayIcon.TaskbarCreated && message != 0)
        {
            // Explorer restarted, maybe at another scale: the icon has to be added again.
            _tray.Readd(TrayIcons.Create());
            return 0;
        }
        if (message == Installer.WmShowSettings)
        {
            ShowSettings();
            return 0;
        }
        if (message == Installer.WmExit)
        {
            Exit("the installer is updating or removing the app");
            return 0;
        }
        if (message == Installer.WmHandOver)
        {
            Exit("a copy with admin rights takes over");
            return 0;
        }
        switch (message)
        {
            case WindowMessage.MenuSelect:
                OnMenuHighlight(wParam, lParam);
                return 0;
            case WindowMessage.PowerBroadcast:
                if (SystemNotifications.DisplayState((int)message, wParam, lParam) is { } state)
                {
                    // Only off to on is a wake. Windows also reports the current state right after
                    // registering (the first one), and dimmed to on after a short idle.
                    var previous = _displayState;
                    _displayState = state;
                    KeepAwake.ScreenIsOff = state == 0;
                    // Back from a short screen-off (Remote mode) isn't a wake: the laptop stayed awake.
                    // A long one may have ended in a real sleep (lid, power button): restore as after one.
                    var held = state != 0 ? KeepAwake.EndScreenHold() : null;
                    if (previous == 0 && state == 1 && held is not { TotalSeconds: < 60 })
                        OnWake();
                }
                else if ((int)wParam is ResumedBySystem or ResumedByUser)
                {
                    KeepAwake.EndScreenHold();
                    OnWake();
                }
                else if ((int)wParam == PowerStatusChanged)
                    _chargerSettle.Start();   // also sent for battery percentage steps: see OnPowerSettled
                return null;
            case WindowMessage.DeviceChange:
                if (SystemNotifications.ChangedInterfacePath((int)message, wParam, lParam) is { } path)
                    OnDeviceChanged(path);
                return null;
            case WindowMessage.DisplayChange:
                // HDR off, a monitor in or out, another resolution: the driver may have dropped
                // the calibration. Windows sends several of these per change; this waits for the last.
                ColorModeService.RestoreSoon();
                UpdateIcon();   // the scale may have changed with it
                return null;
            case WindowMessage.SettingChange or WindowMessage.ThemeChanged:
                FluentTheme.Reload();
                WindowTheme.RefreshMenus();
                UpdateIcon();
                return null;
            case WindowMessage.QueryEndSession:
                return 1;
            case WindowMessage.EndSession when wParam != 0:
                Exit("Windows is signing out, restarting or shutting down");   // the process ends soon; don't leave a dead icon behind
                return 0;
        }
        return null;
    }

    /// <summary>
    /// A power status change has settled (Windows updates the AC state a moment after the
    /// message). Acts only when the charger really was plugged in or out: Auto picks the
    /// rate for the new power source and says so; a fixed rate is put back quietly, in case
    /// Windows changed it.
    /// </summary>
    private void OnPowerSettled()
    {
        _chargerSettle.Stop();
        var pluggedIn = PowerSource.IsPluggedIn;
        if (_disposed || pluggedIn == _pluggedIn)
            return;
        _pluggedIn = pluggedIn;
        // Auto switches the refresh rate here, which blanks the screen a moment: when it did,
        // the popup says so, so the flicker has a reason. Otherwise unplugging stays quiet.
        if (pluggedIn)
            _ = Task.Run(() =>
            {
                if (AppSettings.ChargeOnce)
                    AppSettings.ChargeOnceSawCharger = true;
                var switched = RefreshRate.Restore();
                UiThread.Post(() => UiThread.After(ChargeRateSettleMilliseconds, () => AnnounceCharger(switched)));   // the charge rate takes a moment to show
            });
        else
            _ = Task.Run(AnnounceUnplugAsync);
        RefreshSettingsSoon();
    }

    private const uint ChargeRateSettleMilliseconds = 2500;

    /// <summary>The charger popup: the charge level, how long to the limit or why it isn't charging, and a new refresh rate.</summary>
    private void AnnounceCharger(int? switchedHz)
    {
        if (_disposed || !PowerSource.IsPluggedIn)
            return;
        _ = Task.Run(() =>
        {
            var battery = PowerSource.Battery;
            var title = (battery.Charging, battery.Percent) switch
            {
                (true, { } percent) => T("Charging · {0}%", percent),
                (false, { } percent) => T("Plugged in · {0}%", percent),
                (true, null) => T("Charging"),
                (false, null) => T("Plugged in")
            };
            var detail = Join(ChargerDetail(battery, Battery.ReadCharge(), ChargeTarget()), switchedHz is { } hz ? T("{0} Hz", hz) : null);
            Popup.Show(battery.Charging ? PopupIcon.BatteryCharging : PopupIcon.Battery, title, detail, battery: battery.Percent);
        });
    }

    /// <summary>Unplugged: a popup only when Auto switched the refresh rate (the screen just flickered).</summary>
    private async Task AnnounceUnplugAsync()
    {
        try
        {
            var switched = RefreshRate.Restore();
            await ChargeLimitMenu.EndChargeOnceAsync().ConfigureAwait(false);
            if (switched is not { } hz || _disposed)
                return;
            var percent = PowerSource.Battery.Percent;
            Popup.Show(PopupIcon.Battery, percent is { } level ? T("On battery · {0}%", level) : T("On battery"), T("{0} Hz to save power", hz), battery: percent);
        }
        catch (Exception exception)
        {
            Log.Error("Unplugging not handled", exception);
        }
    }

    private static string? Join(params string?[] parts)
        => parts.Where(part => !string.IsNullOrEmpty(part)).ToList() is { Count: > 0 } shown ? string.Join(" · ", shown) : null;

    /// <summary>The highest charge the limit allows now: 100 without a limit or during a one-time full charge.</summary>
    private static (int Start, int End) ChargeTarget()
        => AppSettings.ChargeOnce || AppSettings.CurrentCharge is not { } limit ? (0, 100) : ChargeLimits.Range(limit);

    internal static string? ChargerDetail(BatteryStatus battery, BatteryCharge? charge, (int Start, int End) window)
    {
        if (battery.Percent is { } percent && percent >= window.End)
            return window.End >= 100 ? T("Full") : T("Limit reached");   // the title already says the percentage
        if (!battery.Charging)
            return window.Start > 0 && battery.Percent is { } level && level >= window.Start
                ? T("Charges below {0}%", window.Start)
                : null;
        // One whole sentence per case, so each language can order it its own way.
        var full = window.End >= 100;
        if (charge?.TimeTo(window.End) is not { } left)
            return full ? T("To full") : T("To {0}%", window.End);
        var minutes = Math.Max(1, (int)Math.Round(left.TotalMinutes));
        if (minutes >= 60)
            return full ? T("{0} h {1:00} min to full", minutes / 60, minutes % 60) : T("{0} h {1:00} min to {2}%", minutes / 60, minutes % 60, window.End);
        return full ? T("{0} min to full", minutes) : T("{0} min to {1}%", minutes, window.End);
    }

    private static async Task EndChargeOnceAsync()
    {
        try
        {
            await ChargeLimitMenu.EndChargeOnceAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.Error("Charge limit not put back after the one-time full charge", exception);
        }
    }

    /// <summary>
    /// The laptop woke up (or the screen came back on: with modern standby that's the only
    /// dependable sign). Puts back what the firmware and drivers lose in sleep.
    /// </summary>
    private void OnWake()
    {
        KeepAwake.Recheck();   // its countdown didn't run during sleep: end on time
        var now = Environment.TickCount64;
        if (_disposed || now - Interlocked.Read(ref _lastWake) < WakeDebounceMilliseconds)
            return;
        Interlocked.Exchange(ref _lastWake, now);
        Interlocked.Exchange(ref _ignoreLightEventsUntil, now + WakeSettleMilliseconds);
        Log.Info("Woke up: restoring keyboard light, charge limit, display and touchpad");
        // All of it off the UI thread: these talk to the BIOS, the graphics driver and HID.
        _ = Task.Run(_lightRestore.RunAsync);
        _ = Task.Run(_autoBacklight.Resend);
        _ = Task.Run(_chargeGuard.RunAsync);
        _ = Task.Run(() => RefreshRate.Restore());
        _ = Task.Run(ColorModeService.Restore);
        _ = Task.Run(TouchpadSettings.Reapply);
        _ = Task.Run(_micMute.Sync);   // the mic LED is lost in sleep too
        _ = Task.Run(() => UpdateCheck.CheckAndAnnounceAsync());   // a week may have passed in sleep; nothing while off
        RefreshSettingsSoon();
    }

    private bool IsBacklightEventStale() => Environment.TickCount64 < Interlocked.Read(ref _ignoreLightEventsUntil);

    // Windows reports a burst of these per plug or unplug; act once it's quiet.
    private void OnDeviceChanged(string devicePath)
    {
        if (_disposed || !TouchpadLink.IsTouchpadPath(devicePath))
            return;
        _deviceSettle.Start();
    }

    private void OnDevicesSettled()
    {
        _deviceSettle.Stop();
        if (_disposed)
            return;
        // The touchpad came back under a new path, with factory settings. Off the UI thread:
        // finding it again means listing the HID devices.
        _ = Task.Run(() =>
        {
            TouchpadLink.Forget();
            _touchpadEvents.Restart();
            TouchpadSettings.Reapply();
        });
    }
}
