namespace Art14Helper;

/// <summary>
/// "Keep running with the lid closed", separately on the charger and on battery: the lid
/// close action set to Do nothing, and the earlier action put back when turned off.
/// </summary>
internal static class LidClosed
{
    internal static bool KeepsRunning(bool onBattery) => PowerSettings.GetLid(onBattery) == LidAction.Nothing;

    /// <summary>False when Windows refused the change.</summary>
    internal static bool SetKeepRunning(bool onBattery, bool on)
    {
        var current = PowerSettings.GetLid(onBattery);
        if (on)
        {
            if (current == LidAction.Nothing)
                return true;
            if (current is { } before)
            {
                AppSettings.SetLidBefore(onBattery, before);   // what to put back, and where
                AppSettings.SetLidPlan(onBattery, PowerSettings.ActivePlan);
            }
            return PowerSettings.SetLid(onBattery, LidAction.Nothing);
        }
        // In the plan it was changed in: the user may have switched plans since.
        var plan = AppSettings.GetLidPlan(onBattery);
        if ((plan is { } changed ? PowerSettings.GetLid(onBattery, changed) : current) != LidAction.Nothing)
            return true;
        var restore = AppSettings.GetLidBefore(onBattery) ?? LidAction.Sleep;
        AppSettings.SetLidBefore(onBattery, null);
        AppSettings.SetLidPlan(onBattery, null);
        return PowerSettings.SetLid(onBattery, restore, plan);
    }
}

/// <summary>Turns the screen off for Remote mode; the laptop keeps running. Any mouse or key input turns it back on.</summary>
internal static class Screen
{
    private const uint SystemCommand = 0x0112, MonitorPower = 0xF170;   // WM_SYSCOMMAND, SC_MONITORPOWER
    private static readonly IntPtr Everyone = 0xFFFF;                   // HWND_BROADCAST

    /// <summary>After a moment, so letting go of the click doesn't wake it right away.</summary>
    internal static void TurnOff() => UiThread.After(800, () =>
    {
        KeepAwake.HoldWhileScreenOff();   // dark, not asleep: work keeps running
        User32.PostMessageW(Everyone, SystemCommand, (nint)MonitorPower, 2);
        // A touch of the mouse at that moment, or a driver that ignores the request, leaves the
        // screen on: then the hold must not keep the laptop awake for good.
        UiThread.After(5000, () =>
        {
            if (!KeepAwake.ScreenIsOff)
                KeepAwake.EndScreenHold();
        });
    });
}

/// <summary>
/// For running work remotely (a long task, a build, a download) with nobody at the laptop:
/// keep awake until turned off, the screen allowed off (and turned off now), and the lid
/// free to close on the charger and on battery. Turning it off puts back the lid actions it
/// changed and ends keep awake. Survives a restart of the app. Calls on the UI thread.
/// </summary>
internal static class RemoteMode
{
    internal static bool IsOn => AppSettings.RemoteMode;

    internal static void Start()
    {
        AppSettings.RemoteMode = true;
        foreach (var battery in new[] { false, true })
        {
            // Remember what was already on, so turning remote mode off leaves the user's own choice alone.
            AppSettings.SetRemoteChangedLid(battery, !LidClosed.KeepsRunning(battery));
            if (!LidClosed.SetKeepRunning(battery, true))
                Popup.Show(PopupIcon.Warning, T("Lid setting not changed"), T("Lid may still sleep"));
        }
        KeepAwake.Start(null);
        Log.Info("Remote mode on");
        Screen.TurnOff();
    }

    internal static void Stop()
    {
        if (!IsOn)
            return;
        AppSettings.RemoteMode = false;
        foreach (var battery in new[] { false, true })
        {
            if (AppSettings.GetRemoteChangedLid(battery))
                LidClosed.SetKeepRunning(battery, false);
            AppSettings.SetRemoteChangedLid(battery, false);
        }
        KeepAwake.Stop();
        KeepAwake.EndScreenHold();   // turned off remotely while the screen is still dark: let it sleep again
        Log.Info("Remote mode off");
    }

    /// <summary>At app start: still on from before (a restart, an update), so keep the laptop awake again.</summary>
    internal static void Resume()
    {
        if (IsOn && !KeepAwake.IsOn)
            KeepAwake.Start(null);
    }
}
