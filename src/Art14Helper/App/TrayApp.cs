namespace Art14Helper;

/// <summary>
/// The running app: tray icon, its hidden window, the hardware services and, when open,
/// the settings window. Created and used on the UI thread. The other files of this class:
/// .Menu (tray menu and settings window), .Keys (keys reported by the BIOS),
/// .Power (sleep, wake, charger, devices and other Windows broadcasts), .Icon (tray icon,
/// tooltip and sensor reads).
/// </summary>
internal sealed partial class TrayApp : IDisposable
{
    private const uint TrayCallback = WindowMessage.App + 2;

    private readonly TrayWindow _window;
    private readonly TrayIcon _tray;
    private readonly TrackingTooltip _menuTip;
    private readonly TouchpadEvents _touchpadEvents;
    private readonly SettleWatch _lightRestore;
    private readonly AutoBacklightService _autoBacklight = new();
    private readonly SettleWatch _chargeGuard;
    private readonly MicrophoneMute _micMute;
    private readonly SystemNotifications.PowerSubscription _displayWatch;
    private readonly SystemNotifications.DeviceSubscription _hidWatch;
    // The device inventory: only when the Drivers page is opened, and kept while the
    // settings window is open. Dropped when it closes, so
    // versions changed by a restore or an update are read fresh next time.
    private Lazy<Task<IReadOnlyList<DriverComponent>>> _drivers = new(ReadDriversInBackground);
    private BiosKeys? _biosKeys;
    private bool _disposed;

    internal TrayApp()
    {
        _window = new TrayWindow(OnMessage);
        WindowMessaging.AcceptFromAnyProcess(_window.Handle, Installer.WmShowSettings);
        // Explorer runs without admin rights; an elevated app only hears it restart if it says so.
        WindowMessaging.AcceptFromAnyProcess(_window.Handle, (int)TrayIcon.TaskbarCreated);
        WindowTheme.MenusFollowSystem(_window.Handle);
        _menuTip = new TrackingTooltip(_window.Handle);
        _lightRestore = KeyboardLights.CreateRestore(() => _autoBacklight.Refresh());
        _chargeGuard = ChargeLimits.CreateWakeGuard();
        _micMute = new MicrophoneMute();
        Popup.Attach(work => UiThread.Post(() =>
        {
            if (!_disposed)
                work();
        }));

        // The real tooltip needs WMI reads; the icon shouldn't wait for them.
        _tray = new TrayIcon(_window.Handle, TrayCallback, TrayIcons.Create(), AppInfo.Name);
        _tray.Add();
        KeepAwake.Changed += RefreshSettingsSoon;
        // Still on from before a restart or an update: keep awake first, then remote mode (which keeps awake too).
        KeepAwake.Resume();
        RemoteMode.Resume();
        _hoverTimer = new UiTimer(SensorInterval, OnHoverTick);
        _deviceSettle = new UiTimer(1000, OnDevicesSettled);
        _chargerSettle = new UiTimer(1500, OnPowerSettled);

        _touchpadEvents = new TouchpadEvents(message => Log.Error(message));
        _hidWatch = SystemNotifications.WatchHidDevices(_window.Handle);
        _displayWatch = SystemNotifications.WatchDisplay(_window.Handle);
        _lightRestore.Start();
        _ = Task.Run(StartHardware);
        // By then startup is over: what it allocated (HID lists, WMI rows, color tables) can go,
        // and from now on the app mostly waits, which efficiency mode is made for.
        UiThread.After(30_000, () =>
        {
            Memory.Trim();
            if (_settings is null)
                Efficiency.Set(true);
        });
    }

    /// <summary>
    /// Everything that talks to WMI or HID starts off the UI thread, because the tray icon
    /// is already up. The Fn keys come first, as they're what the user notices; then three
    /// groups that don't depend on each other run side by side. Each step is on its own, so
    /// one failing doesn't skip the rest.
    /// </summary>
    private void StartHardware()
    {
        if (_disposed)
            return;
        Step("BIOS key events (the Fn keys won't react)", StartBiosKeys);

        _ = Task.Run(() =>
        {
            Step("touchpad readers", _touchpadEvents.Start);
            // The touchpad wakes up with factory settings. This also finds and remembers it,
            // so the first tray menu doesn't search for it.
            Step("touchpad settings", TouchpadSettings.Reapply);
        });
        _ = Task.Run(async () =>
        {
            // First run only; may wait for a UAC answer, which holds up just this group.
            await FirstRunDefaults.ApplyIfNeededAsync().ConfigureAwait(false);
            // Unplugged while the app wasn't running, after a one-time full charge. Not when the
            // charger never came (turned on at night on battery, then a restart before plugging in).
            if (!PowerSource.IsPluggedIn && AppSettings.ChargeOnceSawCharger)
                await EndChargeOnceAsync().ConfigureAwait(false);
            else if (PowerSource.IsPluggedIn && AppSettings.ChargeOnce)
                AppSettings.ChargeOnceSawCharger = true;
            // The firmware keeps neither the Fn mode nor the mic LED across restarts (a lit
            // LED with a live mic would mislead). Skipped without admin rights.
            Step("Fn mode", () =>
            {
                if (AppSettings.FnMode is { } fnMode)
                    Bios.TryRun(Bios.Command(0x04, 0x07, (byte)fnMode));
            });
            Step("mic LED", _micMute.Sync);
            Step("Auto keyboard light", _autoBacklight.Start);
        });
        _ = Task.Run(() =>
        {
            // An audio driver update can reset spatial sound; put the user's choice back.
            Step("spatial sound", () =>
            {
                AppSettings.SpatialAudio ??= SpatialAudioController.IsSpatial();
                if (AppSettings.SpatialAudio is { } spatial && SpatialAudioController.IsSupported() && SpatialAudioController.IsSpatial() != spatial)
                    _ = Privileged.SpatialAudio.RunAsync(spatial, AdminPrompt.Never);
            });
            // Windows forgets the panel's refresh rate, and the driver its color calibration.
            Step("refresh rate", () => RefreshRate.Restore());
            Step("color mode", ColorModeService.Initialize);
        });
        _ = ReadSensorsAsync();
    }

    private void StartBiosKeys()
    {
        var keys = new BiosKeys(IsBacklightEventStale);
        keys.KeyPressed += OnBiosKey;
        keys.TouchpadSwitched += OnTouchpadSwitched;
        keys.LightChanged += OnLightChanged;
        keys.CameraDocked += OnCameraDocked;
        keys.Start();
        _biosKeys = keys;
        if (_disposed)   // closed while the listener was starting
            keys.Dispose();
    }

    private static void Step(string what, Action step)
    {
        try
        {
            step();
        }
        catch (Exception exception)
        {
            Log.Error($"Startup: {what} failed", exception);
        }
    }

    private static Task<IReadOnlyList<DriverComponent>> ReadDriversInBackground()
    {
        var reading = new DriverUpdateService().BuildDeviceListAsync();
        reading.ContinueWith(failed => Log.Error("Driver list not read", failed.Exception!),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        return reading;
    }

    /// <summary>Closes the app: removes the icon, stops the services, ends the message loop.</summary>
    internal void Exit()
    {
        Dispose();
        UiThread.Quit();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        KeepAwake.Changed -= RefreshSettingsSoon;
        _settings?.Dispose();
        _touchpadEvents.Dispose();
        _biosKeys?.Dispose();
        _lightRestore.Dispose();
        _autoBacklight.Dispose();
        _chargeGuard.Dispose();
        _micMute.Dispose();
        Popup.Detach();
        _menuTip.Dispose();
        _hoverTimer.Dispose();
        _deviceSettle.Dispose();
        _chargerSettle.Dispose();
        _displayWatch.Dispose();
        _hidWatch.Dispose();
        _tray.Dispose();
        _window.Dispose();
    }

    /// <summary>The hidden window that owns the tray icon and hears Windows' broadcasts.</summary>
    private sealed class TrayWindow : Window
    {
        private readonly Func<uint, nint, nint, nint?> _onMessage;

        internal TrayWindow(Func<uint, nint, nint, nint?> onMessage)
        {
            _onMessage = onMessage;
            // A real (hidden) top-level window: message-only windows get no broadcasts. The
            // title lets a second launch find it and ask for the settings window.
            Create(Installer.TrayWindowClass, Installer.TrayWindowCaption, User32.Popup, User32.ToolWindow);
        }

        protected override nint? Receive(uint message, nint wParam, nint lParam) => _onMessage(message, wParam, lParam);
    }
}
