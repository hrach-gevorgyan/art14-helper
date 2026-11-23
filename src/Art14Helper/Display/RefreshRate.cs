namespace Art14Helper;

/// <summary>
/// The built-in panel's refresh rate. Windows forgets a chosen rate after sleep or a
/// restart (and keeps the battery-hungry maximum on battery), so the app remembers the
/// choice and puts it back, or in Auto picks it by power source: the highest rate on the
/// charger, 60 Hz on battery.
/// </summary>
internal static class RefreshRate
{
    private static readonly Lock SwitchGate = new();
    private const int OnBattery = 60;

    internal static bool IsAuto => AppSettings.ScreenRateAuto;

    internal static int Highest => DisplayPanel.HighestRate();

    internal static int Current => DisplayPanel.CurrentRate();

    /// <summary>
    /// The rate to apply: Auto by power source, otherwise the saved choice. 0 means
    /// nothing to do (no choice yet, or the panel's rates are unknown).
    /// </summary>
    internal static int Wanted(bool auto, bool pluggedIn, int highest, int? saved)
        => auto ? (pluggedIn ? Math.Max(highest, 0) : OnBattery) : saved ?? 0;

    /// <summary>Auto on or off; switching it on applies the rule right away. No popup: the user chose it.</summary>
    internal static void SetAuto(bool auto)
    {
        AppSettings.ScreenRateAuto = auto;
        if (auto)
            _ = Task.Run(() => Restore());
    }

    /// <summary>A fixed rate the user picked: Auto goes off and the rate is remembered.</summary>
    internal static void Choose(int hz)
    {
        AppSettings.ScreenRateAuto = false;
        AppSettings.ScreenRefreshRate = hz;
        _ = Task.Run(() => Apply(hz));
    }

    /// <summary>
    /// Puts the wanted rate back (at startup, after wake, on a power change). Only touches
    /// the panel when the rate differs.
    /// </summary>
    /// <returns>The new rate when the panel was switched (the screen blanked a moment), else null.</returns>
    internal static int? Restore()
    {
        var wanted = Wanted(IsAuto, PowerSource.IsPluggedIn, IsAuto ? Highest : 0, AppSettings.ScreenRefreshRate);
        return wanted > 0 && wanted != Current ? Apply(wanted) : null;
    }

    /// <summary>
    /// This panel switches 60 and 120 Hz only with a full mode change (its Intel driver offers
    /// no seamless low refresh rate), which blanks the screen a moment and drops the color
    /// calibration. The calibration goes back at once, so there's one flicker, not a second jump.
    /// </summary>
    private static int? Apply(int hz)
    {
        try
        {
            // One switch at a time (wake, charger and restore can all ask at once), and "already
            // there" is no switch: only a real one may be announced as the reason for a flicker.
            lock (SwitchGate)
            {
                if (Current == hz || !DisplayPanel.SetRate(hz))
                    return null;
            }
            ColorModeService.Restore();
            // In case the driver resets its color table a little after the mode change: once more.
            _ = Task.Delay(1500).ContinueWith(_ => ColorModeService.Restore(), TaskScheduler.Default);
            return hz;
        }
        catch (Exception exception)
        {
            Log.Error($"Refresh rate {hz} Hz not applied", exception);
            return null;
        }
    }
}
